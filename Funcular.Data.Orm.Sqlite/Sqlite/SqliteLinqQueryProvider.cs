using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.ExceptionServices;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.Sqlite.Visitors;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;

using Funcular.Data.Orm.Linq;
namespace Funcular.Data.Orm.Sqlite
{
    /// <summary>
    /// Provides LINQ query translation and execution for SQLite.
    /// </summary>
    public class SqliteLinqQueryProvider<T> : IQueryProvider where T : class, new()
    {
        private readonly SqliteOrmDataProvider _dataProvider;
        private readonly string _selectClause;

        public SqliteLinqQueryProvider(SqliteOrmDataProvider dataProvider, string selectClause = null)
        {
            _dataProvider = dataProvider;
            _selectClause = selectClause;
        }

        public IQueryable CreateQuery(Expression expression)
        {
            return new SqliteQueryable<T>(this, expression);
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            return new SqliteQueryable<TElement>(this, expression);
        }

        public object Execute(Expression expression)
        {
            // I2: a collection (IQueryable-typed) expression returns the list; a terminal (First, Count, ...) returns
            // its single result. Pinning IEnumerable<T> here returned the whole list as the "result" of a First.
            var resultType = typeof(IQueryable).IsAssignableFrom(expression.Type)
                ? typeof(IEnumerable<>).MakeGenericType(ElementTypeOf(expression.Type))
                : expression.Type;
            try
            {
                return GenericExecuteMethod.MakeGenericMethod(resultType).Invoke(this, new object[] { expression });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                // Surface the real exception (type and stack trace), not the reflection wrapper.
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private static readonly MethodInfo GenericExecuteMethod = typeof(SqliteLinqQueryProvider<T>).GetMethods()
            .Single(m => m.Name == nameof(Execute) && m.IsGenericMethodDefinition);

        private static Type ElementTypeOf(Type sequenceType)
        {
            var enumerable = sequenceType.IsGenericType && sequenceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? sequenceType
                : sequenceType.GetInterfaces().First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            return enumerable.GetGenericArguments()[0];
        }

        public TResult Execute<TResult>(Expression expression)
        {
            Debug.WriteLine($"Executing expression: {expression}");
            var parameterGenerator = new SqliteParameterGenerator();
            var translator = new SqliteExpressionTranslator(parameterGenerator, _dataProvider.StringComparison);

            var components = ParseExpression(expression, parameterGenerator, translator);

            // Top-level scalar projection Select(x => x.Member): materialize the entity (narrow SELECT) then
            // project the member in memory → List<memberType>.
            if (components.ScalarSelector != null)
            {
                return ExecuteScalarProjection<TResult>(components, expression);
            }

            // I2: collection vs single row is decided by the expression's shape, not TResult. A terminal over an
            // IQueryable<object> view requests object, and an enumerated Cast<object>() requests IEnumerable<object>.
            bool isCollection = typeof(IQueryable).IsAssignableFrom(expression.Type);

            // Take(n <= 0): no command. Decided by the expression's shape (I2), never by TResult.
            if (components.IsEmptyByTake)
                return EmptyResult<TResult>(expression, isCollection);

            TResult executeResult;
            if (components.Terminal == "Single" || components.Terminal == "SingleOrDefault")
            {
                // Up to two rows through the list path, then LINQ's cardinality rules.
                string commandText = BuildQueryComponents(components);
                var rows = ExecuteQuery<List<T>>(commandText, components.Parameters, isCollection: true, expression);
                executeResult = SingleOf<TResult>(rows, components.Terminal, ((MethodCallExpression)expression).Arguments.Count == 2);
            }
            else if (components.IsAggregate)
            {
                executeResult = HandleAggregateQuery<TResult>(components, expression);
            }
            else
            {
                string commandText = BuildQueryComponents(components);
                executeResult = ExecuteQuery<TResult>(commandText, components.Parameters, isCollection, expression);
            }

            Debug.WriteLine($"Execute result: {executeResult}");
            return executeResult;
        }

        /// <summary>
        /// Executes a top-level scalar projection (<c>Select(x =&gt; x.Member)</c>): materializes the entity via the
        /// narrow SELECT built for the member, then applies the selector in memory to produce
        /// <c>List&lt;memberType&gt;</c> (assignable to the caller's <c>List&lt;memberType&gt;</c> /
        /// <c>IEnumerable&lt;memberType&gt;</c>).
        /// </summary>
        private TResult ExecuteScalarProjection<TResult>(QueryComponents components, Expression expression)
        {
            // A scalar projection yields List<memberType>, so it supports enumeration only: every terminal is rejected,
            // decided by the expression's shape (I2).
            ScalarProjectionGuard.EnsureCollectionResult(expression, typeof(TResult), components.ScalarMemberType);
            var listType = typeof(List<>).MakeGenericType(components.ScalarMemberType);
            if (components.IsEmptyByTake)
                return (TResult)Activator.CreateInstance(listType);

            string commandText = BuildQueryComponents(components);
            var entities = ExecuteQuery<List<T>>(commandText, components.Parameters, isCollection: true, expression);

            // Compile the selector to Func<T, object> (boxes the member value), then fill a typed List<memberType>.
            var param = components.ScalarSelector.Parameters[0];
            var boxed = Expression.Lambda<Func<T, object>>(
                Expression.Convert(components.ScalarSelector.Body, typeof(object)), param).Compile();

            var list = (System.Collections.IList)Activator.CreateInstance(listType);
            foreach (var e in entities)
                list.Add(boxed(e));
            return (TResult)(object)list;
        }

        /// <summary>
        /// The result of a <c>Take(n &lt;= 0)</c> query on the entity path: an empty list for a collection, "no elements"
        /// for <c>First</c>/<c>Single</c>/<c>Last</c>, <c>default</c> for <c>*OrDefault</c>.
        /// </summary>
        private static TResult EmptyResult<TResult>(Expression expression, bool isCollection)
        {
            if (isCollection)
                return (TResult)(object)new List<T>();
            var terminal = (expression as MethodCallExpression)?.Method.Name;
            if (terminal == "First" || terminal == "Single" || terminal == "Last")
                throw new InvalidOperationException("Sequence contains no elements");
            return default(TResult);
        }

        /// <summary>
        /// LINQ's <c>Single</c>/<c>SingleOrDefault</c> cardinality over the (at most two) rows read.
        /// </summary>
        private static TResult SingleOf<TResult>(List<T> rows, string terminal, bool hasPredicate)
        {
            if (rows.Count > 1)
                throw new InvalidOperationException(hasPredicate
                    ? "Sequence contains more than one matching element"
                    : "Sequence contains more than one element");
            if (rows.Count == 1)
                return (TResult)(object)rows[0];
            if (terminal == "Single")
                throw new InvalidOperationException(hasPredicate ? "Sequence contains no matching element" : "Sequence contains no elements");
            return default(TResult);
        }

        private QueryComponents ParseExpression(Expression expression, SqliteParameterGenerator parameterGenerator, SqliteExpressionTranslator translator)
        {
            // Reject unsupported operators, and supported ones in unsupported positions, before translating anything.
            QueryOperatorPolicy.EnsureSupported(expression);

            var components = new QueryComponents();
            components.Parameters = new List<SqliteParameter>();

            var callExpression = expression as MethodCallExpression;
            if (callExpression == null) return components;

            var methodCalls = new List<MethodCallExpression>();
            Expression currentExpression = callExpression;
            while (currentExpression is MethodCallExpression methodCall)
            {
                methodCalls.Add(methodCall);
                currentExpression = methodCall.Arguments[0];
            }

            string orderByClause = null;

            for (int i = methodCalls.Count - 1; i >= 0; i--)
            {
                var currentCall = methodCalls[i];

                if (currentCall.Method.Name == "Any" || currentCall.Method.Name == "All" || currentCall.Method.Name == "Count" || currentCall.Method.Name == "LongCount" || currentCall.Method.Name == "Average" || currentCall.Method.Name == "Min" || currentCall.Method.Name == "Max" || currentCall.Method.Name == "Sum")
                {
                    components.OuterMethodCall = currentCall.Method.Name;
                }
                else if (i == methodCalls.Count - 1 && components.OuterMethodCall == null)
                {
                    components.OuterMethodCall = currentCall.Method.Name;
                }

                // A scalar projection changes the element type from T to the projected member. The chain is walked
                // inner→outer, so if ScalarSelector is already set we are now processing an operator OUTER of the
                // scalar Select. Any such operator carrying a lambda over the projected element (Where/OrderBy/
                // predicate- or selector-bearing aggregates/chained Select) would be hard-cast to Func<T,...> and
                // throw an obscure InvalidCastException. Reject the whole class here with one clear message.
                // Constant-arg operators (Skip/Take/Distinct) are unaffected and still compose.
                if (components.ScalarSelector != null && currentCall.Arguments.Count >= 2
                    && (currentCall.Arguments[1] is LambdaExpression
                        || (currentCall.Arguments[1] is UnaryExpression scalarComposeUnary && scalarComposeUnary.Operand is LambdaExpression)))
                {
                    throw new NotSupportedException(
                        $"A scalar projection Select(x => x.Member) must be the outermost query operator; applying " +
                        $"{currentCall.Method.Name}(...) after it is not translated. Filter, order, or aggregate BEFORE " +
                        $"the projection, or materialize and compose in memory: " +
                        $"query.Select(x => x.Member).ToList().{currentCall.Method.Name}(...).");
                }

                if (currentCall.Method.Name == "Where")
                {
                    var lambda = (LambdaExpression)((UnaryExpression)currentCall.Arguments[1]).Operand;
                    var whereExpression = (Expression<Func<T, bool>>)lambda;
                    var elements = _dataProvider.GenerateWhereClause(whereExpression, parameterGenerator: parameterGenerator, translator: translator);
                    if (string.IsNullOrEmpty(components.WhereClause))
                        components.WhereClause = elements.WhereClause;
                    else
                        components.WhereClause = $"({components.WhereClause}) AND ({elements.WhereClause})";
                    if (!string.IsNullOrEmpty(elements.JoinClause))
                    {
                        components.JoinClause = elements.JoinClause;
                    }
                    if (elements.SqlParameters != null) components.Parameters.AddRange(elements.SqlParameters);
                }
                else if (currentCall.Method.Name == "Skip")
                {
                    object value = ((ConstantExpression)currentCall.Arguments[1]).Value;
                    if (value != null)
                        components.Skip = Math.Max(0, (int)value); // Skip(n < 0) behaves as Skip(0)
                }
                else if (currentCall.Method.Name == "Take")
                {
                    object value = ((ConstantExpression)currentCall.Arguments[1]).Value;
                    if (value != null)
                    {
                        components.Take = (int)value;
                        // Take(n <= 0) is empty: it's answered without a command (Execute / ExecuteScalarProjection).
                        if ((int)value <= 0)
                            components.IsEmptyByTake = true;
                    }
                }
                else if ((currentCall.Method.Name == "FirstOrDefault" || currentCall.Method.Name == "First" || currentCall.Method.Name == "SingleOrDefault" || currentCall.Method.Name == "Single" || currentCall.Method.Name == "LastOrDefault" || currentCall.Method.Name == "Last") && currentCall.Arguments.Count == 2)
                {
                    var lambda = (LambdaExpression)((UnaryExpression)currentCall.Arguments[1]).Operand;
                    var whereExpression = (Expression<Func<T, bool>>)lambda;
                    var elements = _dataProvider.GenerateWhereClause(whereExpression, parameterGenerator: parameterGenerator, translator: translator);
                    if (string.IsNullOrEmpty(components.WhereClause))
                        components.WhereClause = elements.WhereClause;
                    else
                        components.WhereClause = $"({components.WhereClause}) AND ({elements.WhereClause})";
                    if (!string.IsNullOrEmpty(elements.JoinClause))
                        components.JoinClause = elements.JoinClause;
                    if (elements.SqlParameters != null) components.Parameters.AddRange(elements.SqlParameters);
                }
                else if (currentCall.Method.Name == "OrderBy" || currentCall.Method.Name == "OrderByDescending" || currentCall.Method.Name == "ThenBy" || currentCall.Method.Name == "ThenByDescending")
                {
                    // Own columns are table-qualified when the entity has joins (#12).
                    var orderByTable = _dataProvider.GetTableNameInternal<T>();
                    var orderByRemote = _dataProvider.ResolveRemoteJoins<T>(orderByTable);
                    var orderByVisitor = new SqliteOrderByClauseVisitor<T>(
                        SqliteOrmDataProvider.ColumnNamesCache,
                        SqliteOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                            t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray()),
                        orderByRemote.PropertyToColumnMap,
                        orderByRemote.IndividualJoinClauses?.Count > 0 ? orderByTable : null);
                    orderByVisitor.Visit(currentCall);
                    orderByClause = orderByVisitor.OrderByClause;
                    components.OrderByTerms = orderByVisitor.OrderByTerms.ToList();
                }
                else if ((currentCall.Method.Name == "Any" || currentCall.Method.Name == "All" || currentCall.Method.Name == "Count" || currentCall.Method.Name == "LongCount") && (currentCall.Arguments.Count == 1 || currentCall.Arguments.Count == 2))
                {
                    components.IsAggregate = true;
                    components.AggregateClause = BuildAggregateClause(currentCall, components.WhereClause, components.Parameters, parameterGenerator, translator);
                }
                else if (currentCall.Method.Name == "Average" || currentCall.Method.Name == "Min" || currentCall.Method.Name == "Max" || currentCall.Method.Name == "Sum")
                {
                    components.IsAggregate = true;
                    components.AggregateClause = BuildAggregateClause(currentCall, components.WhereClause, components.Parameters, parameterGenerator, translator);
                }
                else if (currentCall.Method.Name == "Select")
                {
                    var lambda = (LambdaExpression)((UnaryExpression)currentCall.Arguments[1]).Operand;
                    // FunkyORM materializes the source entity type T. Supported top-level projections:
                    //   (a) Select(x => new T { ... }) — same entity, column subset (narrow SELECT); and
                    //   (b) Select(x => x.Member)      — a single mapped member (v3.9): emit the narrow SELECT for
                    //       that member, materialize T, then read the member in memory (see Execute).
                    // Anonymous types / other DTOs / expression bodies are not translated — fail clearly rather
                    // than emit a full-entity SELECT and throw an obscure InvalidCastException at materialization.
                    Expression selectBody;
                    if (lambda.Body is MemberInitExpression mi && mi.Type == typeof(T))
                    {
                        selectBody = mi;
                    }
                    else if (lambda.Body is MemberExpression scalarMember
                             && scalarMember.Expression is ParameterExpression
                             && scalarMember.Member is PropertyInfo sp && sp.CanWrite)
                    {
                        // Project as if `new T { Member = x.Member }` for the SQL + materialization, then apply the
                        // original selector in memory to yield List<memberType>.
                        selectBody = Expression.MemberInit(Expression.New(typeof(T)),
                            Expression.Bind(scalarMember.Member, scalarMember));
                        components.ScalarSelector = lambda;
                        components.ScalarMemberType = scalarMember.Type;
                    }
                    else
                    {
                        throw new NotSupportedException(
                            $"A top-level Select must project to {typeof(T).Name} — a column subset, " +
                            $"Select(x => new {typeof(T).Name} {{ ... }}) — or to a single mapped member, " +
                            $"Select(x => x.Member). An anonymous type, another DTO, or a computed expression is not " +
                            $"supported; materialize first and project in memory: query.ToList().Select(...).");
                    }

                    var selectTable = _dataProvider.GetTableNameInternal<T>();
                    var selectRemoteMap = _dataProvider.ResolveRemoteJoins<T>(selectTable).PropertyToColumnMap;
                    var selectVisitor = new SqliteSelectClauseVisitor<T>(
                        SqliteOrmDataProvider.ColumnNamesCache,
                        SqliteOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                            t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray()),
                        parameterGenerator,
                        translator,
                        selectTable,
                        selectRemoteMap);
                    selectVisitor.Visit(selectBody);
                    _lastSelectProjection = selectVisitor.SelectClause;
                    _lastSelectParameters = selectVisitor.Parameters;
                }
                else if (currentCall.Method.Name == "Distinct")
                {
                    components.IsDistinct = true;
                }
            }

            // Store order by
            if (!string.IsNullOrEmpty(orderByClause))
            {
                // Store in a field accessible by BuildQueryComponents
                components.AggregateClause = components.IsAggregate ? components.AggregateClause : null;
                // Use a workaround: put orderBy into a temp field
            }
            // Directly assign
            _lastOrderByClause = orderByClause;

            if (components.IsDistinct && components.IsAggregate)
                throw new NotSupportedException(
                    "Distinct() combined with an aggregate (e.g. Count) is not supported in this version. " +
                    "Apply the aggregate without Distinct, or materialize the distinct rows and count client-side.");

            // Single*: read at most two rows to check cardinality. Without user paging that is a row limit (no ORDER BY
            // is synthesized); with user Skip/Take it caps the page at min(Take ?? 2, 2).
            var terminal = (expression as MethodCallExpression)?.Method.Name;
            if (terminal == "Single" || terminal == "SingleOrDefault")
            {
                components.Terminal = terminal;
                if (components.Skip.HasValue || components.Take.HasValue)
                    components.Take = Math.Min(components.Take ?? 2, 2);
                else
                    components.RowLimit = 2;
            }

            // Last*: the first row of the inverted order. Every term is inverted whole (own, remote/computed and CASE
            // fragments); with no explicit order it is Id DESC, qualified when the entity has joins. Paging before
            // Last* is rejected by the policy, so the row limit never meets a user Skip/Take.
            if (terminal == "Last" || terminal == "LastOrDefault")
            {
                components.Terminal = terminal;
                _lastOrderByClause = components.OrderByTerms.Count > 0
                    ? "ORDER BY " + string.Join(", ", components.OrderByTerms.Select(t => $"{t.Fragment} {(t.IsDescending ? "ASC" : "DESC")}"))
                    : DefaultLastOrderBy();
                components.RowLimit = 1;
            }

            return components;
        }

        /// <summary>
        /// The order for <c>Last*</c> without an explicit one: <c>Id DESC</c>, table-qualified when the entity has joins.
        /// </summary>
        private string DefaultLastOrderBy()
        {
            var idProperty = typeof(T).GetProperty("Id");
            if (idProperty == null)
                throw new InvalidOperationException($"Entity type {typeof(T).Name} does not have an 'Id' property.");

            var parameter = Expression.Parameter(typeof(T), "x");
            var orderByDescending = typeof(Queryable).GetMethods()
                .First(m => m.Name == "OrderByDescending" && m.GetParameters().Length == 2)
                .MakeGenericMethod(typeof(T), idProperty.PropertyType);
            var orderByExpression = Expression.Call(orderByDescending, Expression.Constant(null, typeof(IQueryable<T>)),
                Expression.Quote(Expression.Lambda(Expression.Property(parameter, idProperty), parameter)));

            var table = _dataProvider.GetTableNameInternal<T>();
            var remote = _dataProvider.ResolveRemoteJoins<T>(table);
            var orderByVisitor = new SqliteOrderByClauseVisitor<T>(
                SqliteOrmDataProvider.ColumnNamesCache,
                SqliteOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                    t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray()),
                remote.PropertyToColumnMap,
                remote.IndividualJoinClauses?.Count > 0 ? table : null);
            orderByVisitor.Visit(orderByExpression);
            return orderByVisitor.OrderByClause;
        }

        // Temporary storage for order-by clause between ParseExpression and BuildQueryComponents
        private string _lastOrderByClause;
        // Temporary storage for custom SELECT projection between ParseExpression and BuildQueryComponents
        private string _lastSelectProjection;
        private List<SqliteParameter> _lastSelectParameters;

        private string BuildAggregateClause(MethodCallExpression methodCall, string whereClause, List<SqliteParameter> existingParameters, SqliteParameterGenerator parameterGenerator, SqliteExpressionTranslator translator)
        {
            string aggregateClause = null;
            var parameters = existingParameters != null ? new List<SqliteParameter>(existingParameters) : new List<SqliteParameter>();
            string table = _dataProvider.GetTableNameInternal<T>();
            // Remote attributes contribute LEFT JOINs the WHERE may reference. We append them to the aggregate
            // FROM only when the WHERE actually references a remote column (a plain / no-filter aggregate stays on
            // the base table), and REJECT the aggregate when it would need a reverse (one-to-many) join that could
            // inflate the result. `joins` is set per-branch via ResolveAggregateJoins.
            var remoteInfo = _dataProvider.ResolveRemoteJoins<T>(table);
            string joins = string.Empty;

            string methodName = methodCall.Method.Name;
            bool isAny = methodName == "Any";
            bool isAll = methodName == "All";
            bool isCount = methodName == "Count" || methodName == "LongCount"; // LongCount: the same rows, an Int64 result
            bool isPredicateBased = isAny || isAll || isCount;

            if (isPredicateBased)
            {
                bool hasPredicate = methodCall.Arguments.Count == 2;
                if (isAll && !hasPredicate)
                    throw new InvalidOperationException("All method requires a predicate.");

                string modifiedWhereClause = null;
                if (hasPredicate)
                {
                    var lambda = (LambdaExpression)((UnaryExpression)methodCall.Arguments[1]).Operand;
                    var predicateExpression = (Expression<Func<T, bool>>)lambda;
                    var whereVisitor = new SqliteWhereClauseVisitor<T>(
                        SqliteOrmDataProvider.ColumnNamesCache,
                        SqliteOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                            t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray()),
                        parameterGenerator, translator, table,
                        remoteInfo.PropertyToColumnMap);
                    whereVisitor.Visit(predicateExpression);
                    modifiedWhereClause = whereVisitor.WhereClauseBody;

                    if (whereVisitor.Parameters != null)
                    {
                        foreach (var param in whereVisitor.Parameters)
                        {
                            var existingParam = parameters.FirstOrDefault(p => p.ParameterName == param.ParameterName);
                            if (existingParam != null)
                            {
                                int suffix = 1;
                                string newParamName;
                                do { newParamName = $"{param.ParameterName}_{suffix++}"; } while (parameters.Any(p => p.ParameterName == newParamName));
                                modifiedWhereClause = modifiedWhereClause.Replace(param.ParameterName, newParamName);
                                param.ParameterName = newParamName;
                            }
                            parameters.Add(param);
                        }
                    }
                }

                // Append remote joins only if the WHERE references a remote column; reject a reverse (fan-out)
                // join for Count/All (fan-out-sensitive), allow it for Any (EXISTS is fan-out-safe).
                joins = ResolveAggregateJoins(
                    (whereClause ?? string.Empty) + "\n" + (modifiedWhereClause ?? string.Empty),
                    remoteInfo, fanOutSafe: isAny);

                if (isCount)
                {
                    aggregateClause = $"SELECT COUNT(*) FROM {table}{joins}";
                    string combinedWhere = "";
                    if (!string.IsNullOrEmpty(whereClause)) combinedWhere += whereClause;
                    if (hasPredicate) { if (!string.IsNullOrEmpty(combinedWhere)) combinedWhere += " AND "; combinedWhere += modifiedWhereClause; }
                    if (!string.IsNullOrEmpty(combinedWhere)) aggregateClause += $"\r\nWHERE {combinedWhere}";
                }
                else if (isAny)
                {
                    aggregateClause = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {table}{joins}";
                    string combinedWhere = "";
                    if (!string.IsNullOrEmpty(whereClause)) combinedWhere += whereClause;
                    if (hasPredicate) { if (!string.IsNullOrEmpty(combinedWhere)) combinedWhere += " AND "; combinedWhere += modifiedWhereClause; }
                    if (!string.IsNullOrEmpty(combinedWhere)) aggregateClause += $"\r\nWHERE {combinedWhere}";
                    aggregateClause += ") THEN 1 ELSE 0 END";
                }
                else if (isAll)
                {
                    aggregateClause = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {table}{joins}";
                    string combinedWhere = "";
                    if (!string.IsNullOrEmpty(whereClause)) { combinedWhere += whereClause; if (hasPredicate) combinedWhere += " AND "; }
                    if (hasPredicate) combinedWhere += $"NOT ({modifiedWhereClause})";
                    if (!string.IsNullOrEmpty(combinedWhere)) aggregateClause += $"\r\nWHERE {combinedWhere}";
                    aggregateClause += ") THEN 0 ELSE 1 END";
                }
            }
            else if (methodCall.Method.Name == "Average" || methodCall.Method.Name == "Min" || methodCall.Method.Name == "Max" || methodCall.Method.Name == "Sum")
            {
                var lambda = (LambdaExpression)((UnaryExpression)methodCall.Arguments[1]).Operand;
                MemberExpression memberExpression = lambda.Body as MemberExpression ?? (lambda.Body as UnaryExpression)?.Operand as MemberExpression;
                var paramExpr = memberExpression?.Expression as ParameterExpression;
                if (paramExpr == null) throw new NotSupportedException("Aggregate function does not support expression evaluation; aggregates are only supported on column selectors.");
                var property = memberExpression?.Member as PropertyInfo;
                string columnExpression;
                if (property != null &&
                    SqliteOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t => t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray())
                        .All(p => p.Name != property.Name))
                {
                    columnExpression = SqliteOrmDataProvider.ColumnNamesCache.GetOrAdd(property.ToDictionaryKey(), p => _dataProvider.GetCachedColumnNameInternal(property));
                }
                else throw new NotSupportedException("Only simple member access is supported in aggregate expressions.");

                var aggregateFunction = methodCall.Method.Name.ToUpper() == "AVERAGE" ? "AVG" : methodCall.Method.Name.ToUpper();
                // Append remote joins only if the WHERE references a remote column; reject a reverse (fan-out)
                // join for Sum/Average (fan-out-sensitive), allow it for Min/Max (fan-out-safe).
                joins = ResolveAggregateJoins(whereClause, remoteInfo,
                    fanOutSafe: methodCall.Method.Name == "Min" || methodCall.Method.Name == "Max");
                // For integer aggregates that produce REAL in SQLite, round to match SQL Server behavior
                // Qualify the selector with the base table: once remote joins are present, a bare column
                // (e.g. `id`) is ambiguous against joined tables that share the name.
                if (aggregateFunction == "AVG")
                    aggregateClause = $"SELECT ROUND({aggregateFunction}({table}.{columnExpression}), 10) FROM {table}{joins}";
                else
                    aggregateClause = $"SELECT {aggregateFunction}({table}.{columnExpression}) FROM {table}{joins}";
                if (!string.IsNullOrEmpty(whereClause)) aggregateClause += $"\r\nWHERE {whereClause}";
            }

            if (existingParameters != null) { existingParameters.Clear(); existingParameters.AddRange(parameters); }
            return aggregateClause;
        }

        // Clear, agent- and developer-facing message for the reverse-remote-join aggregate limitation (choice A).
        private const string ReverseAggregateNotSupportedMessage =
            "Aggregating with a filter on a reverse ([RemoteKey]/[RemoteProperty]) one-to-many join is not supported: " +
            "the join fans out each base row once per matching child, which would inflate Count/Sum/Average. " +
            "Materialize the query and aggregate in memory instead — e.g. query.Where(...).ToList().Count() (or .Sum(...)). " +
            "Forward (many-to-one) remote joins are unaffected, as are Any/Min/Max over reverse joins.";

        /// <summary>
        /// Returns the LEFT JOIN clauses to append to an aggregate's FROM, or empty when the aggregate's WHERE does
        /// not reference a remote (join-backed) column. Throws <see cref="NotSupportedException"/> when the required
        /// join is a reverse (one-to-many) join and the aggregate is fan-out-sensitive (Count/All/Sum/Average).
        /// </summary>
        private string ResolveAggregateJoins(string aggregateWhere, SqliteOrmDataProvider.ResolvedRemoteJoinInfo remoteInfo, bool fanOutSafe)
        {
            if (!WhereReferencesRemoteColumn(aggregateWhere, remoteInfo))
                return string.Empty; // no remote column filtered → base-table aggregate, no join needed
            if (remoteInfo.HasReverseJoin && !fanOutSafe)
                throw new NotSupportedException(ReverseAggregateNotSupportedMessage);
            return remoteInfo.JoinClauses ?? string.Empty;
        }

        /// <summary>
        /// True when the WHERE text references the resolved column of any [RemoteProperty]/[RemoteKey] on T — i.e. a
        /// column that only exists via a LEFT JOIN. Self-contained attributes ([JsonPath]/[SqlExpression]/
        /// [SubqueryAggregate]) resolve inline and never need a join, so they are excluded.
        /// </summary>
        private static bool WhereReferencesRemoteColumn(string whereText, SqliteOrmDataProvider.ResolvedRemoteJoinInfo remoteInfo)
        {
            if (string.IsNullOrEmpty(whereText) || remoteInfo.PropertyToColumnMap == null) return false;
            foreach (var prop in typeof(T).GetProperties())
            {
                if (prop.GetCustomAttribute<RemoteAttributeBase>() == null) continue;
                if (remoteInfo.PropertyToColumnMap.TryGetValue(prop.Name, out var col)
                    && !string.IsNullOrEmpty(col)
                    && whereText.IndexOf(col, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private string BuildQueryComponents(QueryComponents components)
        {
            var baseCommand = _selectClause ?? _dataProvider.CreateGetOneOrSelectCommandText<T>();
            var outerFromIdx = FindOuterKeywordIndex(baseCommand, " FROM ");
            string selectPart, fromPart;
            if (outerFromIdx >= 0)
            {
                selectPart = baseCommand.Substring(0, outerFromIdx);
                fromPart = "FROM " + baseCommand.Substring(outerFromIdx + 6);
            }
            else
            {
                selectPart = baseCommand;
                fromPart = $"FROM {_dataProvider.GetTableNameInternal<T>()}";
            }

            // Apply custom SELECT projection if one was parsed
            if (!string.IsNullOrEmpty(_lastSelectProjection))
            {
                selectPart = $"SELECT {_lastSelectProjection}";
                if (_lastSelectParameters != null && _lastSelectParameters.Count > 0)
                {
                    components.Parameters.AddRange(_lastSelectParameters);
                }
            }

            if (components.IsDistinct)
            {
                // Last* with no explicit order would use Id DESC, which the projection doesn't carry.
                if (!string.IsNullOrEmpty(_lastSelectProjection)
                    && (components.Terminal == "Last" || components.Terminal == "LastOrDefault") && components.OrderByTerms.Count == 0)
                    throw new NotSupportedException(
                        $"{components.Terminal}() after Distinct() with a custom Select(...) projection requires an explicit " +
                        "OrderBy whose keys are in the projection (the default Id DESC order is not). Add an OrderBy before " +
                        "the Select, or materialize first.");

                // Under DISTINCT with a custom projection, paging without an explicit ORDER BY is non-deterministic.
                if (!string.IsNullOrEmpty(_lastSelectProjection) && string.IsNullOrEmpty(_lastOrderByClause)
                    && (components.Skip.HasValue || components.Take.HasValue))
                {
                    throw new InvalidOperationException(
                        "Distinct() with a custom Select(...) projection and paging (Skip/Take) requires an explicit OrderBy whose keys are in the projection.");
                }

                // Under DISTINCT with a custom projection, every ORDER BY key must be in the SELECT list.
                if (!string.IsNullOrEmpty(_lastSelectProjection) && !string.IsNullOrEmpty(_lastOrderByClause))
                {
                    var selectLower = _lastSelectProjection.ToLowerInvariant();
                    var orderBody = _lastOrderByClause.Substring("ORDER BY".Length);
                    foreach (var rawTerm in SplitTopLevelCommas(orderBody))
                    {
                        var col = rawTerm.Trim();
                        if (col.EndsWith(" ASC", StringComparison.OrdinalIgnoreCase)) col = col.Substring(0, col.Length - 4).Trim();
                        else if (col.EndsWith(" DESC", StringComparison.OrdinalIgnoreCase)) col = col.Substring(0, col.Length - 5).Trim();
                        if (col.Length == 0) continue;
                        if (!selectLower.Contains(col.ToLowerInvariant()))
                            throw new InvalidOperationException(
                                $"Under Distinct(), every ORDER BY key must be part of the projection. '{col}' is not in the " +
                                "Select(...) list. Add it to the projection, or remove Distinct().");
                    }
                }

                var trimmedSelect = selectPart.TrimStart();
                if (trimmedSelect.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase))
                    selectPart = "SELECT DISTINCT " + trimmedSelect.Substring("SELECT ".Length);
            }

            var whereInFromIdx = FindOuterKeywordIndex(fromPart, " WHERE ");
            if (whereInFromIdx >= 0)
            {
                fromPart = fromPart.Substring(0, whereInFromIdx);
            }

            string commandText = $"{selectPart}\r\n{fromPart}";

            if (!string.IsNullOrEmpty(components.JoinClause))
            {
                if (!fromPart.Contains(components.JoinClause.Trim()))
                    commandText += $"\r\n{components.JoinClause}";
            }

            if (!string.IsNullOrEmpty(components.WhereClause))
                commandText += $"\r\nWHERE {components.WhereClause}";

            string orderBy = _lastOrderByClause;
            if (!string.IsNullOrEmpty(orderBy) || components.Skip.HasValue || components.Take.HasValue)
                commandText += $"\r\n{(orderBy ?? DefaultPagingOrderBy(components))}";

            if (components.Take.HasValue)
                commandText += $"\r\nLIMIT {components.Take.Value}";
            else if (components.Skip.HasValue)
                commandText += "\r\nLIMIT -1"; // SQLite has no OFFSET without LIMIT; -1 means no limit.
            if (components.Skip.HasValue)
                commandText += $"\r\nOFFSET {components.Skip.Value}";

            // Single*/Last*: a row limit when the user didn't page (RowLimit is never set alongside Skip/Take).
            if (components.RowLimit.HasValue)
                commandText += $"\r\nLIMIT {components.RowLimit.Value}";

            return commandText;
        }

        /// <summary>
        /// The ORDER BY for paging without an explicit order. Qualified as <c>{table}.rowid</c> when the command has
        /// joins: every joined table has its own <c>rowid</c>, so a bare one is ambiguous (AC12-7).
        /// </summary>
        private string DefaultPagingOrderBy(QueryComponents components)
        {
            var table = _dataProvider.GetTableNameInternal<T>();
            var hasJoins = _dataProvider.ResolveRemoteJoins<T>(table).IndividualJoinClauses?.Count > 0
                           || !string.IsNullOrEmpty(components.JoinClause);
            return hasJoins ? $"ORDER BY {table}.rowid" : "ORDER BY rowid";
        }

        private TResult HandleAggregateQuery<TResult>(QueryComponents components, Expression expression)
        {
            using (var connectionScope = new SqliteOrmDataProvider.ConnectionScope(_dataProvider))
            {
                using (var command = _dataProvider.BuildSqlCommandObject(components.AggregateClause, connectionScope.Connection, components.Parameters))
                {
                    _dataProvider.InvokeLogAction(command);
                    var result = command.ExecuteScalar();
                    if (result == DBNull.Value || result == null)
                    {
                        var outerMethod = components.OuterMethodCall;
                        if (outerMethod == "Average" || outerMethod == "Min" || outerMethod == "Max")
                            throw new InvalidOperationException($"Sequence contains no elements for {outerMethod}.");
                        result = 0;
                    }

                    var outerMethodName = components.OuterMethodCall;
                    if (outerMethodName == "Any" || outerMethodName == "All")
                        return (TResult)(object)(Convert.ToInt64(result) == 1);
                    else if (outerMethodName == "Count")
                        return (TResult)(object)Convert.ToInt32(result);
                    else if (outerMethodName == "LongCount")
                        return (TResult)(object)Convert.ToInt64(result);
                    else if (outerMethodName == "Average")
                        return (TResult)(object)Convert.ToDouble(result);
                    else if (outerMethodName == "Min" || outerMethodName == "Max" || outerMethodName == "Sum")
                    {
                        // Try to return the correct type
                        var targetType = typeof(TResult);
                        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
                        if (underlyingType == typeof(int)) return (TResult)(object)Convert.ToInt32(result);
                        if (underlyingType == typeof(long)) return (TResult)(object)Convert.ToInt64(result);
                        if (underlyingType == typeof(double)) return (TResult)(object)Convert.ToDouble(result);
                        if (underlyingType == typeof(decimal)) return (TResult)(object)Convert.ToDecimal(result);
                        if (underlyingType == typeof(DateTime)) return (TResult)(object)Convert.ToDateTime(result);
                        return (TResult)Convert.ChangeType(result, underlyingType);
                    }
                    return default;
                }
            }
        }

        private TResult ExecuteQuery<TResult>(string commandText, List<SqliteParameter> parameters, bool isCollection, Expression expression)
        {
            using (var connectionScope = new SqliteOrmDataProvider.ConnectionScope(_dataProvider))
            {
                using (var command = _dataProvider.BuildSqlCommandObject(commandText, connectionScope.Connection, parameters))
                {
                    if (isCollection)
                        return (TResult)(object)_dataProvider.ExecuteReaderList<T>(command);
                    else
                    {
                        var result = _dataProvider.ExecuteReaderSingle<T>(command);
                        if (result == null && expression is MethodCallExpression call)
                        {
                            if (call.Method.Name == "First" || call.Method.Name == "Last")
                                throw new InvalidOperationException($"Sequence contains no matching element for {call.Method.Name}.");
                        }
                        return (TResult)(object)result;
                    }
                }
            }
        }

        private static List<string> SplitTopLevelCommas(string s)
        {
            var parts = new List<string>();
            int depth = 0, start = 0;
            bool inQuote = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\'') inQuote = !inQuote;          // SQL string-literal delimiter ('' escape nets out)
                else if (inQuote) continue;                 // ignore commas/parens inside a string literal
                else if (c == '(') depth++;
                else if (c == ')') { if (depth > 0) depth--; }
                else if (c == ',' && depth == 0) { parts.Add(s.Substring(start, i - start)); start = i + 1; }
            }
            parts.Add(s.Substring(start));
            return parts;
        }

        private static int FindOuterKeywordIndex(string sql, string keyword)
        {
            int depth = 0;
            for (int i = 0; i <= sql.Length - keyword.Length; i++)
            {
                char c = sql[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (depth == 0 && string.Compare(sql, i, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
