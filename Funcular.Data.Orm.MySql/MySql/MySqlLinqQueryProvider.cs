using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using MySqlConnector;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.MySql.Visitors;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;

namespace Funcular.Data.Orm.MySql
{
    /// <summary>
    /// Provides LINQ query translation and execution for MySQL.
    /// </summary>
    public class MySqlLinqQueryProvider<T> : IQueryProvider where T : class, new()
    {
        private readonly MySqlOrmDataProvider _dataProvider;
        private readonly string _selectClause;

        public MySqlLinqQueryProvider(MySqlOrmDataProvider dataProvider, string selectClause = null)
        {
            _dataProvider = dataProvider;
            _selectClause = selectClause;
        }

        public IQueryable CreateQuery(Expression expression)
        {
            return new MySqlQueryable<T>(this, expression);
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            return new MySqlQueryable<TElement>(this, expression);
        }

        public object Execute(Expression expression)
        {
            return Execute<IEnumerable<T>>(expression);
        }

        public TResult Execute<TResult>(Expression expression)
        {
            Debug.WriteLine($"Executing expression: {expression}");
            var parameterGenerator = new MySqlParameterGenerator();
            var translator = new MySqlExpressionTranslator(parameterGenerator);

            var components = ParseExpression(expression, parameterGenerator, translator);

            bool isCollection = typeof(IEnumerable<T>).IsAssignableFrom(typeof(TResult)) && typeof(TResult) != typeof(T);

            TResult executeResult;
            if (components.IsAggregate)
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

        private QueryComponents ParseExpression(Expression expression, MySqlParameterGenerator parameterGenerator, MySqlExpressionTranslator translator)
        {
            var components = new QueryComponents();
            components.Parameters = new List<MySqlParameter> { };

            var callExpression = expression as MethodCallExpression;
            if (callExpression == null) return components;

            var methodCalls = new List<MethodCallExpression>();
            Expression currentExpression = callExpression;
            while (currentExpression is MethodCallExpression methodCall)
            {
                methodCalls.Add(methodCall);
                currentExpression = methodCall.Arguments[0];
            }

            for (int i = methodCalls.Count - 1; i >= 0; i--)
            {
                var currentCall = methodCalls[i];

                if (currentCall.Method.Name == "Any" || currentCall.Method.Name == "All" || currentCall.Method.Name == "Count" || currentCall.Method.Name == "Average" || currentCall.Method.Name == "Min" || currentCall.Method.Name == "Max" || currentCall.Method.Name == "Sum")
                {
                    components.OuterMethodCall = currentCall;
                }
                else if (i == methodCalls.Count - 1 && components.OuterMethodCall == null)
                {
                    components.OuterMethodCall = currentCall;
                }

                if (currentCall.Method.Name == "GroupBy")
                {
                    // GroupBy is not translated to SQL. Fail clearly here rather than letting the result path
                    // materialize T and then throw an obscure InvalidCastException (same class as the top-level
                    // Select guard). Group in memory after materializing.
                    throw new NotSupportedException(
                        "GroupBy is not supported in this version — it is not translated to SQL. Materialize " +
                        "first and group in memory: query.ToList().GroupBy(...).");
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
                        if (elements.JoinClausesList != null) components.JoinClausesList.AddRange(elements.JoinClausesList);
                    }
                    if (elements.SqlParameters != null) components.Parameters.AddRange(elements.SqlParameters);
                }
                else if (currentCall.Method.Name == "Skip")
                {
                    object value = ((ConstantExpression)currentCall.Arguments[1]).Value;
                    if (value != null) components.Skip = (int)value;
                }
                else if (currentCall.Method.Name == "Take")
                {
                    object value = ((ConstantExpression)currentCall.Arguments[1]).Value;
                    if (value != null) components.Take = (int)value;
                }
                else if ((currentCall.Method.Name == "FirstOrDefault" || currentCall.Method.Name == "First" || currentCall.Method.Name == "LastOrDefault" || currentCall.Method.Name == "Last") && currentCall.Arguments.Count == 2)
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
                        if (elements.JoinClausesList != null) components.JoinClausesList.AddRange(elements.JoinClausesList);
                    }
                    if (elements.SqlParameters != null) components.Parameters.AddRange(elements.SqlParameters);

                    if (currentCall.Method.Name == "LastOrDefault" || currentCall.Method.Name == "Last")
                    {
                        if (string.IsNullOrEmpty(components.OrderByClause))
                        {
                            var idProperty = typeof(T).GetProperty("Id");
                            if (idProperty == null)
                                throw new InvalidOperationException($"Entity type {typeof(T).Name} does not have an 'Id' property.");

                            var parameter = Expression.Parameter(typeof(T), "x");
                            var propertyAccess = Expression.Property(parameter, idProperty);
                            var orderByLambda = Expression.Lambda(propertyAccess, parameter);

                            var orderByDescendingMethod = typeof(Queryable).GetMethods()
                                .First(m => m.Name == "OrderByDescending" && m.GetParameters().Length == 2)
                                .MakeGenericMethod(typeof(T), idProperty.PropertyType);

                            var orderByExpression = Expression.Call(
                                orderByDescendingMethod,
                                Expression.Constant(null, typeof(IQueryable<T>)),
                                Expression.Quote(orderByLambda));

                            var orderByVisitor = new MySqlOrderByClauseVisitor<T>(
                                MySqlOrmDataProvider.ColumnNamesCache,
                                MySqlOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                                    t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray()));
                            orderByVisitor.Visit(orderByExpression);
                            components.OrderByClause = orderByVisitor.OrderByClause;
                        }
                    }
                }
                else if (currentCall.Method.Name == "OrderBy" || currentCall.Method.Name == "OrderByDescending" || currentCall.Method.Name == "ThenBy" || currentCall.Method.Name == "ThenByDescending")
                {
                    var orderByTable = _dataProvider.GetTableNameInternal<T>();
                    var orderByRemoteMap = _dataProvider.ResolveRemoteJoins<T>(orderByTable).PropertyToColumnMap;
                    var orderByVisitor = new MySqlOrderByClauseVisitor<T>(
                        MySqlOrmDataProvider.ColumnNamesCache,
                        MySqlOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                            t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray()),
                        orderByRemoteMap);
                    orderByVisitor.Visit(currentCall);
                    components.OrderByClause = orderByVisitor.OrderByClause;
                }
                else if ((currentCall.Method.Name == "Any" || currentCall.Method.Name == "All" || currentCall.Method.Name == "Count") && (currentCall.Arguments.Count == 1 || currentCall.Arguments.Count == 2))
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
                    // FunkyORM materializes the source entity type T. A top-level Select projecting to a DIFFERENT
                    // element type (a scalar column, an anonymous type, or another DTO) is not materialized — only
                    // Select(x => new T { ... }) (same entity, subset of columns) is supported. Fail clearly here
                    // rather than letting the result path throw an obscure InvalidCastException at materialization.
                    if (!(lambda.Body is MemberInitExpression mi && mi.Type == typeof(T)))
                        throw new NotSupportedException(
                            $"A top-level Select projecting to a type other than {typeof(T).Name} (e.g. a scalar " +
                            $"column, an anonymous type, or another DTO) is not supported in this version. Project to " +
                            $"the same entity — Select(x => new {typeof(T).Name} {{ ... }}) — and read the field(s) you " +
                            $"need, or materialize first and project in memory: query.ToList().Select(...).");
                    // Resolve computed/view-replacing attrs so self-contained ones ([JsonPath]/[SqlExpression]/
                    // [SubqueryAggregate]) can be projected; [RemoteProperty] is rejected inside the visitor.
                    var selectTable = _dataProvider.GetTableNameInternal<T>();
                    var selectRemoteMap = _dataProvider.ResolveRemoteJoins<T>(selectTable).PropertyToColumnMap;
                    var selectVisitor = new MySqlSelectClauseVisitor<T>(
                        MySqlOrmDataProvider.ColumnNamesCache,
                        MySqlOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                            t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray()),
                        parameterGenerator,
                        translator,
                        selectTable,
                        selectRemoteMap);
                    selectVisitor.Visit(lambda.Body);
                    components.SelectClause = selectVisitor.SelectClause;
                    components.Parameters.AddRange(selectVisitor.Parameters);
                }
                else if (currentCall.Method.Name == "Distinct")
                {
                    components.IsDistinct = true;
                }
            }

            if (components.IsDistinct && components.IsAggregate)
                throw new NotSupportedException(
                    "Distinct() combined with an aggregate (e.g. Count) is not supported in this version. " +
                    "Apply the aggregate without Distinct, or materialize the distinct rows and count client-side.");

            return components;
        }

        private string BuildAggregateClause(MethodCallExpression methodCall, string whereClause, List<MySqlParameter> existingParameters, MySqlParameterGenerator parameterGenerator, MySqlExpressionTranslator translator)
        {
            string aggregateClause = null;
            var parameters = existingParameters != null ? new List<MySqlParameter>(existingParameters) : new List<MySqlParameter>();
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
            bool isCount = methodName == "Count";
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
                    var whereVisitor = new MySqlWhereClauseVisitor<T>(
                        MySqlOrmDataProvider.ColumnNamesCache,
                        MySqlOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t =>
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
                    MySqlOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t => t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray())
                        .All(p => p.Name != property.Name))
                {
                    columnExpression = MySqlOrmDataProvider.ColumnNamesCache.GetOrAdd(property.ToDictionaryKey(), p => _dataProvider.GetCachedColumnNameInternal(property));
                }
                else throw new NotSupportedException("Only simple member access is supported in aggregate expressions.");

                // Append remote joins only if the WHERE references a remote column; reject a reverse (fan-out)
                // join for Sum/Average (fan-out-sensitive), allow it for Min/Max (fan-out-safe).
                joins = ResolveAggregateJoins(whereClause, remoteInfo,
                    fanOutSafe: methodName == "Min" || methodName == "Max");
                var aggregateFunction = methodCall.Method.Name.ToUpper() == "AVERAGE" ? "AVG" : methodCall.Method.Name.ToUpper();
                // Qualify the selector with the base table: once remote joins are present, a bare column
                // (e.g. `id`) is ambiguous against joined tables that share the name.
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
        private string ResolveAggregateJoins(string aggregateWhere, MySqlOrmDataProvider.ResolvedRemoteJoinInfo remoteInfo, bool fanOutSafe)
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
        private static bool WhereReferencesRemoteColumn(string whereText, MySqlOrmDataProvider.ResolvedRemoteJoinInfo remoteInfo)
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

        /// <summary>
        /// Builds the complete SQL query. Uses LIMIT/OFFSET (MySQL requires LIMIT when OFFSET is present).
        /// </summary>
        private string BuildQueryComponents(QueryComponents components)
        {
            var baseCommand = _selectClause ?? _dataProvider.CreateGetOneOrSelectCommandText<T>();
            // Find the outer " FROM " — the one not nested inside parentheses (subqueries).
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

            if (!string.IsNullOrEmpty(components.SelectClause))
            {
                selectPart = $"SELECT {components.SelectClause}";
            }

            if (components.IsDistinct)
            {
                // Under DISTINCT with a custom projection, every ORDER BY key must be in the SELECT list.
                if (!string.IsNullOrEmpty(components.SelectClause))
                {
                    if (string.IsNullOrEmpty(components.OrderByClause) && (components.Skip.HasValue || components.Take.HasValue))
                        throw new InvalidOperationException(
                            "Distinct() with a custom Select(...) projection and paging (Skip/Take) requires an explicit " +
                            "OrderBy whose keys are in the projection.");

                    if (!string.IsNullOrEmpty(components.OrderByClause))
                    {
                        var selectLower = components.SelectClause.ToLowerInvariant();
                        var orderBody = components.OrderByClause.Substring("ORDER BY".Length);
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
                }

                var trimmedSelect = selectPart.TrimStart();
                if (trimmedSelect.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase))
                    selectPart = "SELECT DISTINCT " + trimmedSelect.Substring("SELECT ".Length);
            }

            // If fromPart contains a WHERE clause from the base command, strip it
            // (only the outer WHERE, not one inside subqueries)
            var whereInFromIdx = FindOuterKeywordIndex(fromPart, " WHERE ");
            if (whereInFromIdx >= 0)
            {
                fromPart = fromPart.Substring(0, whereInFromIdx);
            }

            string commandText = $"{selectPart}\r\n{fromPart}";

            if (components.JoinClausesList != null && components.JoinClausesList.Any())
            {
                foreach (var join in components.JoinClausesList)
                {
                    if (!fromPart.Replace("  ", " ").Contains(join.Trim().Replace("  ", " ")))
                        commandText += $"\r\n{join}";
                }
            }
            else if (!string.IsNullOrEmpty(components.JoinClause))
            {
                if (!fromPart.Contains(components.JoinClause.Trim()))
                    commandText += $"\r\n{components.JoinClause}";
            }

            if (!string.IsNullOrEmpty(components.WhereClause))
                commandText += $"\r\nWHERE {components.WhereClause}";

            // Keep ORDER BY fallback for deterministic paging.
            if (!string.IsNullOrEmpty(components.OrderByClause) || components.Skip.HasValue || components.Take.HasValue)
                commandText += $"\r\n{(components.OrderByClause ?? "ORDER BY id")}";

            // MySQL: OFFSET requires a LIMIT. Use the max-BIGINT idiom for "offset to end".
            if (components.Take.HasValue && components.Skip.HasValue)
                commandText += $"\r\nLIMIT {components.Take.Value} OFFSET {components.Skip.Value}";
            else if (components.Take.HasValue)
                commandText += $"\r\nLIMIT {components.Take.Value}";
            else if (components.Skip.HasValue)
                commandText += $"\r\nLIMIT 18446744073709551615 OFFSET {components.Skip.Value}";

            return commandText;
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

        private TResult HandleAggregateQuery<TResult>(QueryComponents components, Expression expression)
        {
            using (var connectionScope = new MySqlOrmDataProvider.ConnectionScope(_dataProvider))
            {
                using (var mySqlCommand = _dataProvider.BuildSqlCommandObject(components.AggregateClause, connectionScope.Connection, components.Parameters))
                {
                    _dataProvider.InvokeLogAction(mySqlCommand);
                    var result = mySqlCommand.ExecuteScalar();
                    if (result == DBNull.Value)
                    {
                        if (components.OuterMethodCall?.Method.Name == "Average" || components.OuterMethodCall?.Method.Name == "Min" || components.OuterMethodCall?.Method.Name == "Max")
                            throw new InvalidOperationException($"Sequence contains no elements for {components.OuterMethodCall.Method.Name}.");
                        result = 0;
                    }

                    if (components.OuterMethodCall?.Method.Name == "Any" || components.OuterMethodCall?.Method.Name == "All")
                        return (TResult)(object)(Convert.ToInt32(result) == 1);
                    else if (components.OuterMethodCall?.Method.Name == "Count")
                        return (TResult)(object)Convert.ToInt32(result);
                    else if (components.OuterMethodCall?.Method.Name == "Average")
                        return (TResult)(object)Convert.ToDouble(result);
                    else if (components.OuterMethodCall?.Method.Name == "Min" || components.OuterMethodCall?.Method.Name == "Max" || components.OuterMethodCall?.Method.Name == "Sum")
                    {
                        var lambda = (LambdaExpression)((UnaryExpression)components.OuterMethodCall.Arguments[1]).Operand;
                        Type selectorType = GetSelectorType(lambda.Body);
                        if (selectorType == typeof(DateTime)) return (TResult)(object)Convert.ToDateTime(result);
                        if (selectorType == typeof(DateTime?)) return result is DBNull ? (TResult)(object)null : (TResult)(object)Convert.ToDateTime(result);
                        if (selectorType == typeof(int)) return (TResult)(object)Convert.ToInt32(result);
                        if (selectorType == typeof(long)) return (TResult)(object)Convert.ToInt64(result);
                        if (selectorType == typeof(double)) return (TResult)(object)Convert.ToDouble(result);
                        if (selectorType == typeof(decimal)) return (TResult)(object)Convert.ToDecimal(result);
                        throw new NotSupportedException($"Unsupported selector type {selectorType} for {components.OuterMethodCall.Method.Name}.");
                    }
                    return default;
                }
            }
        }

        private TResult ExecuteQuery<TResult>(string commandText, List<MySqlParameter> parameters, bool isCollection, Expression expression)
        {
            using (var connectionScope = new MySqlOrmDataProvider.ConnectionScope(_dataProvider))
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

        private Type GetSelectorType(Expression body)
        {
            if (body is UnaryExpression unary) return Nullable.GetUnderlyingType(unary.Operand.Type) ?? unary.Operand.Type;
            return Nullable.GetUnderlyingType(body.Type) ?? body.Type;
        }

        /// <summary>
        /// Finds the index of a keyword in a SQL string that is not nested inside parentheses.
        /// Subqueries like <c>(SELECT COUNT(*) FROM child WHERE ...)</c> contain nested keywords
        /// that must be skipped.
        /// </summary>
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
