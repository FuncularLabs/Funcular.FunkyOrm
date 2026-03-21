using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Npgsql;
using Funcular.Data.Orm.PostgreSql.Visitors;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;

namespace Funcular.Data.Orm.PostgreSql
{
    /// <summary>
    /// Provides LINQ query translation and execution for PostgreSQL.
    /// </summary>
    public class PostgreSqlLinqQueryProvider<T> : IQueryProvider where T : class, new()
    {
        private readonly PostgreSqlOrmDataProvider _dataProvider;
        private readonly string _selectClause;

        public PostgreSqlLinqQueryProvider(PostgreSqlOrmDataProvider dataProvider, string selectClause = null)
        {
            _dataProvider = dataProvider;
            _selectClause = selectClause;
        }

        public IQueryable CreateQuery(Expression expression)
        {
            return new PostgreSqlQueryable<T>(this, expression);
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            return new PostgreSqlQueryable<TElement>(this, expression);
        }

        public object Execute(Expression expression)
        {
            return Execute<IEnumerable<T>>(expression);
        }

        public TResult Execute<TResult>(Expression expression)
        {
            Debug.WriteLine($"Executing expression: {expression}");
            var parameterGenerator = new PostgreSqlParameterGenerator();
            var translator = new PostgreSqlExpressionTranslator(parameterGenerator);

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

        private QueryComponents ParseExpression(Expression expression, PostgreSqlParameterGenerator parameterGenerator, PostgreSqlExpressionTranslator translator)
        {
            var components = new QueryComponents();
            components.Parameters = new List<NpgsqlParameter> { };

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

                            var orderByVisitor = new PostgreSqlOrderByClauseVisitor<T>(
                                PostgreSqlOrmDataProvider.ColumnNamesCache,
                                PostgreSqlOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                                    t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray()));
                            orderByVisitor.Visit(orderByExpression);
                            components.OrderByClause = orderByVisitor.OrderByClause;
                        }
                    }
                }
                else if (currentCall.Method.Name == "OrderBy" || currentCall.Method.Name == "OrderByDescending" || currentCall.Method.Name == "ThenBy" || currentCall.Method.Name == "ThenByDescending")
                {
                    var orderByVisitor = new PostgreSqlOrderByClauseVisitor<T>(
                        PostgreSqlOrmDataProvider.ColumnNamesCache,
                        PostgreSqlOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                            t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray()));
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
                    var selectVisitor = new PostgreSqlSelectClauseVisitor<T>(
                        PostgreSqlOrmDataProvider.ColumnNamesCache,
                        PostgreSqlOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                            t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray()),
                        parameterGenerator,
                        translator,
                        _dataProvider.GetTableNameInternal<T>());
                    selectVisitor.Visit(lambda.Body);
                    components.SelectClause = selectVisitor.SelectClause;
                    components.Parameters.AddRange(selectVisitor.Parameters);
                }
            }

            return components;
        }

        private string BuildAggregateClause(MethodCallExpression methodCall, string whereClause, List<NpgsqlParameter> existingParameters, PostgreSqlParameterGenerator parameterGenerator, PostgreSqlExpressionTranslator translator)
        {
            string aggregateClause = null;
            var parameters = existingParameters != null ? new List<NpgsqlParameter>(existingParameters) : new List<NpgsqlParameter>();
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
                    var whereVisitor = new PostgreSqlWhereClauseVisitor<T>(
                        PostgreSqlOrmDataProvider.ColumnNamesCache,
                        PostgreSqlOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t =>
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
                    PostgreSqlOrmDataProvider.UnmappedPropertiesCache.GetOrAdd(typeof(T), t => t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray())
                        .All(p => p.Name != property.Name))
                {
                    columnExpression = PostgreSqlOrmDataProvider.ColumnNamesCache.GetOrAdd(property.ToDictionaryKey(), p => _dataProvider.GetCachedColumnNameInternal(property));
                }
                else throw new NotSupportedException("Only simple member access is supported in aggregate expressions.");

                var aggregateFunction = methodCall.Method.Name.ToUpper() == "AVERAGE" ? "AVG" : methodCall.Method.Name.ToUpper();
                aggregateClause = $"SELECT {aggregateFunction}({columnExpression}) FROM {table}";
                if (!string.IsNullOrEmpty(whereClause)) aggregateClause += $"\r\nWHERE {whereClause}";
            }

            if (existingParameters != null) { existingParameters.Clear(); existingParameters.AddRange(parameters); }
            return aggregateClause;
        }

        /// <summary>
        /// Builds the complete SQL query. Uses LIMIT/OFFSET instead of OFFSET...FETCH.
        /// </summary>
        private string BuildQueryComponents(QueryComponents components)
        {
            var baseCommand = _selectClause ?? _dataProvider.CreateGetOneOrSelectCommandText<T>();
            var parts = baseCommand.Split(new[] { " FROM " }, StringSplitOptions.None);

            string selectPart = !string.IsNullOrEmpty(components.SelectClause) ? $"SELECT {components.SelectClause}" : parts[0];
            string fromPart = parts.Length > 1 ? $"FROM {parts[1]}" : $"FROM {_dataProvider.GetTableNameInternal<T>()}";

            if (parts.Length > 1 && parts[1].Contains(" WHERE "))
            {
                var fromAndWhere = parts[1].Split(new[] { " WHERE " }, StringSplitOptions.None);
                fromPart = $"FROM {fromAndWhere[0]}";
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

            // PostgreSQL: keep ORDER BY fallback for deterministic paging
            if (!string.IsNullOrEmpty(components.OrderByClause) || components.Skip.HasValue || components.Take.HasValue)
                commandText += $"\r\n{(components.OrderByClause ?? "ORDER BY id")}";

            // PostgreSQL: use LIMIT/OFFSET instead of OFFSET...FETCH
            if (components.Take.HasValue)
                commandText += $"\r\nLIMIT {components.Take.Value}";
            if (components.Skip.HasValue)
                commandText += $"\r\nOFFSET {components.Skip.Value}";

            return commandText;
        }

        private TResult HandleAggregateQuery<TResult>(QueryComponents components, Expression expression)
        {
            using (var connectionScope = new PostgreSqlOrmDataProvider.ConnectionScope(_dataProvider))
            {
                using (var npgsqlCommand = _dataProvider.BuildSqlCommandObject(components.AggregateClause, connectionScope.Connection, components.Parameters))
                {
                    _dataProvider.InvokeLogAction(npgsqlCommand);
                    var result = npgsqlCommand.ExecuteScalar();
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

        private TResult ExecuteQuery<TResult>(string commandText, List<NpgsqlParameter> parameters, bool isCollection, Expression expression)
        {
            using (var connectionScope = new PostgreSqlOrmDataProvider.ConnectionScope(_dataProvider))
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
    }
}
