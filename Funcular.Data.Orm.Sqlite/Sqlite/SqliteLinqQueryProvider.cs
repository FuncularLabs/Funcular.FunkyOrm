using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Funcular.Data.Orm.Sqlite.Visitors;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;

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
            return Execute<IEnumerable<T>>(expression);
        }

        public TResult Execute<TResult>(Expression expression)
        {
            Debug.WriteLine($"Executing expression: {expression}");
            var parameterGenerator = new SqliteParameterGenerator();
            var translator = new SqliteExpressionTranslator(parameterGenerator, _dataProvider.StringComparison);

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

        private QueryComponents ParseExpression(Expression expression, SqliteParameterGenerator parameterGenerator, SqliteExpressionTranslator translator)
        {
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

                if (currentCall.Method.Name == "Any" || currentCall.Method.Name == "All" || currentCall.Method.Name == "Count" || currentCall.Method.Name == "Average" || currentCall.Method.Name == "Min" || currentCall.Method.Name == "Max" || currentCall.Method.Name == "Sum")
                {
                    components.OuterMethodCall = currentCall.Method.Name;
                }
                else if (i == methodCalls.Count - 1 && components.OuterMethodCall == null)
                {
                    components.OuterMethodCall = currentCall.Method.Name;
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
                        components.JoinClause = elements.JoinClause;
                    if (elements.SqlParameters != null) components.Parameters.AddRange(elements.SqlParameters);

                    if (currentCall.Method.Name == "LastOrDefault" || currentCall.Method.Name == "Last")
                    {
                        if (string.IsNullOrEmpty(orderByClause))
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

                            var orderByVisitor = new SqliteOrderByClauseVisitor<T>(
                                SqliteOrmDataProvider.ColumnNamesCache,
                                SqliteOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                                    t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray()));
                            orderByVisitor.Visit(orderByExpression);
                            orderByClause = orderByVisitor.OrderByClause;
                        }
                    }
                }
                else if (currentCall.Method.Name == "OrderBy" || currentCall.Method.Name == "OrderByDescending" || currentCall.Method.Name == "ThenBy" || currentCall.Method.Name == "ThenByDescending")
                {
                    var orderByVisitor = new SqliteOrderByClauseVisitor<T>(
                        SqliteOrmDataProvider.ColumnNamesCache,
                        SqliteOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                            t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray()));
                    orderByVisitor.Visit(currentCall);
                    orderByClause = orderByVisitor.OrderByClause;
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
                    var selectVisitor = new SqliteSelectClauseVisitor<T>(
                        SqliteOrmDataProvider.ColumnNamesCache,
                        SqliteOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                            t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray()),
                        parameterGenerator,
                        translator,
                        _dataProvider.GetTableNameInternal<T>());
                    selectVisitor.Visit(lambda.Body);
                    _lastSelectProjection = selectVisitor.SelectClause;
                    _lastSelectParameters = selectVisitor.Parameters;
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

            return components;
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
                    var whereVisitor = new SqliteWhereClauseVisitor<T>(
                        SqliteOrmDataProvider.ColumnNamesCache,
                        SqliteOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                            t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray()),
                        parameterGenerator, translator, _dataProvider.GetTableNameInternal<T>());
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

                if (isCount)
                {
                    aggregateClause = $"SELECT COUNT(*) FROM {table}";
                    string combinedWhere = "";
                    if (!string.IsNullOrEmpty(whereClause)) combinedWhere += whereClause;
                    if (hasPredicate) { if (!string.IsNullOrEmpty(combinedWhere)) combinedWhere += " AND "; combinedWhere += modifiedWhereClause; }
                    if (!string.IsNullOrEmpty(combinedWhere)) aggregateClause += $"\r\nWHERE {combinedWhere}";
                }
                else if (isAny)
                {
                    aggregateClause = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {table}";
                    string combinedWhere = "";
                    if (!string.IsNullOrEmpty(whereClause)) combinedWhere += whereClause;
                    if (hasPredicate) { if (!string.IsNullOrEmpty(combinedWhere)) combinedWhere += " AND "; combinedWhere += modifiedWhereClause; }
                    if (!string.IsNullOrEmpty(combinedWhere)) aggregateClause += $"\r\nWHERE {combinedWhere}";
                    aggregateClause += ") THEN 1 ELSE 0 END";
                }
                else if (isAll)
                {
                    aggregateClause = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {table}";
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
                // For integer aggregates that produce REAL in SQLite, round to match SQL Server behavior
                if (aggregateFunction == "AVG")
                    aggregateClause = $"SELECT ROUND({aggregateFunction}({columnExpression}), 10) FROM {table}";
                else
                    aggregateClause = $"SELECT {aggregateFunction}({columnExpression}) FROM {table}";
                if (!string.IsNullOrEmpty(whereClause)) aggregateClause += $"\r\nWHERE {whereClause}";
            }

            if (existingParameters != null) { existingParameters.Clear(); existingParameters.AddRange(parameters); }
            return aggregateClause;
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
                commandText += $"\r\n{(orderBy ?? "ORDER BY rowid")}";

            if (components.Take.HasValue)
                commandText += $"\r\nLIMIT {components.Take.Value}";
            if (components.Skip.HasValue)
                commandText += $"\r\nOFFSET {components.Skip.Value}";

            return commandText;
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
