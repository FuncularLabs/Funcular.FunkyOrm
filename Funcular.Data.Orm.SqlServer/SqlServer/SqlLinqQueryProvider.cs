using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Funcular.Data.Orm.Visitors;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;

namespace Funcular.Data.Orm.SqlServer
{
    /// <summary>
    /// Provides LINQ query translation and execution for SQL Server.
    /// Converts LINQ expressions into SQL queries, executes them using the provided data provider,
    /// and returns results as entities or aggregates. Supports filtering, ordering, paging, and aggregates.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    public class SqlLinqQueryProvider<T> : IQueryProvider where T : class, new()
    {
        /// <summary>
        /// The underlying ORM data provider used for SQL execution and mapping.
        /// </summary>
        private readonly SqlServerOrmDataProvider _dataProvider;

        /// <summary>
        /// Optional custom SELECT clause to use for queries. If null, a default SELECT clause is generated.
        /// </summary>
        private readonly string _selectClause;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlLinqQueryProvider{T}"/> class.
        /// </summary>
        /// <param name="dataProvider">The data provider used to execute SQL commands.</param>
        /// <param name="selectClause">An optional custom SELECT clause to use in queries. If null, a default SELECT clause is generated.</param>
        public SqlLinqQueryProvider(SqlServerOrmDataProvider dataProvider, string selectClause = null)
        {
            _dataProvider = dataProvider;
            _selectClause = selectClause;
        }

        /// <summary>
        /// Creates a non-generic <see cref="IQueryable"/> for the given LINQ expression.
        /// </summary>
        /// <param name="expression">The LINQ expression tree representing the query.</param>
        /// <returns>An <see cref="IQueryable"/> instance for deferred execution.</returns>
        public IQueryable CreateQuery(Expression expression)
        {
            return new SqlQueryable<T>(this, expression);
        }

        /// <summary>
        /// Creates a generic <see cref="IQueryable{TElement}"/> for the given LINQ expression.
        /// </summary>
        /// <typeparam name="TElement">The element type of the query.</typeparam>
        /// <param name="expression">The LINQ expression tree representing the query.</param>
        /// <returns>An <see cref="IQueryable{TElement}"/> instance for deferred execution.</returns>
        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            return new SqlQueryable<TElement>(this, expression);
        }

        /// <summary>
        /// Executes the given LINQ expression and returns the result as an object.
        /// Used for non-generic query execution.
        /// </summary>
        /// <param name="expression">The LINQ expression tree representing the query.</param>
        /// <returns>The result of the query execution.</returns>
        public object Execute(Expression expression)
        {
            return Execute<IEnumerable<T>>(expression);
        }

        /// <summary>
        /// Executes the specified LINQ expression and returns the result as the specified type.
        /// Handles both entity queries and aggregate queries (Any, All, Count, Average, Min, Max).
        /// </summary>
        /// <typeparam name="TResult">The type of the result.</typeparam>
        /// <param name="expression">The expression to execute.</param>
        /// <returns>The result of the query execution.</returns>
        /// <exception cref="NotSupportedException">Thrown when the expression type is not supported.</exception>
        /// <exception cref="InvalidOperationException">Thrown when an operation cannot be performed, such as when All is called without a predicate.</exception>
        public TResult Execute<TResult>(Expression expression)
        {
            Debug.WriteLine($"Executing expression: {expression}");
            var parameterGenerator = new ParameterGenerator();
            var translator = new SqlExpressionTranslator(parameterGenerator);

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

        /// <summary>
        /// Parses the LINQ expression tree to extract query components such as WHERE, ORDER BY, paging, and aggregates.
        /// </summary>
        /// <param name="expression">The LINQ expression to parse.</param>
        /// <param name="parameterGenerator">The parameter generator for SQL parameters.</param>
        /// <param name="translator">The SQL expression translator for method calls.</param>
        /// <returns>A <see cref="QueryComponents"/> object containing the extracted components.</returns>
        private QueryComponents ParseExpression(Expression expression, ParameterGenerator parameterGenerator, SqlExpressionTranslator translator)
        {
            var components = new QueryComponents();
            components.Parameters = new List<SqlParameter> { };

            var callExpression = expression as MethodCallExpression;
            if (callExpression == null)
            {
                return components;
            }

            // Collect all method calls in the chain (from outer to inner)
            var methodCalls = new List<MethodCallExpression>();
            Expression currentExpression = callExpression;
            while (currentExpression is MethodCallExpression methodCall)
            {
                methodCalls.Add(methodCall);
                currentExpression = methodCall.Arguments[0];
            }

            // Process method calls in reverse order (from inner to outer)
            for (int i = methodCalls.Count - 1; i >= 0; i--)
            {
                var currentCall = methodCalls[i];

                if (currentCall.Method.Name == "Any" || currentCall.Method.Name == "All" || currentCall.Method.Name == "Count" || currentCall.Method.Name == "Average" || currentCall.Method.Name == "Min" || currentCall.Method.Name == "Max")
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
                    components.WhereClause = elements.WhereClause;
                    if (elements.SqlParameters != null)
                    {
                        components.Parameters.AddRange(elements.SqlParameters);
                    }
                }
                else if (currentCall.Method.Name == "Skip")
                {
                    object value = ((ConstantExpression)currentCall.Arguments[1]).Value;
                    if (value != null)
                        components.Skip = (int)value;
                }
                else if (currentCall.Method.Name == "Take")
                {
                    object value = ((ConstantExpression)currentCall.Arguments[1]).Value;
                    if (value != null)
                        components.Take = (int)value;
                }
                else if ((currentCall.Method.Name == "FirstOrDefault" || currentCall.Method.Name == "First" || currentCall.Method.Name == "LastOrDefault" || currentCall.Method.Name == "Last") && currentCall.Arguments.Count == 2)
                {
                    var lambda = (LambdaExpression)((UnaryExpression)currentCall.Arguments[1]).Operand;
                    var whereExpression = (Expression<Func<T, bool>>)lambda;
                    var elements = _dataProvider.GenerateWhereClause(whereExpression, parameterGenerator: parameterGenerator, translator: translator);
                    components.WhereClause = elements.WhereClause;
                    if (elements.SqlParameters != null)
                    {
                        components.Parameters.AddRange(elements.SqlParameters);
                    }

                    if (currentCall.Method.Name == "LastOrDefault" || currentCall.Method.Name == "Last")
                    {
                        // Check if there's already an ORDER BY clause to avoid conflicts
                        if (string.IsNullOrEmpty(components.OrderByClause))
                        {
                            var idProperty = typeof(T).GetProperty("Id");
                            if (idProperty == null)
                            {
                                throw new InvalidOperationException(
                                    $"Entity type {typeof(T).Name} does not have an 'Id' property. LastOrDefault/Last methods require an Id property for ordering, or use an explicit OrderBy clause.");
                            }

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

                            var orderByVisitor = new OrderByClauseVisitor<T>(
                                SqlServerOrmDataProvider.ColumnNames,
                                SqlServerOrmDataProvider._unmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                                    t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray()));
                            orderByVisitor.Visit(orderByExpression);
                            components.OrderByClause = orderByVisitor.OrderByClause;
                        }
                    }
                }
                else if (currentCall.Method.Name == "OrderBy" || currentCall.Method.Name == "OrderByDescending" || currentCall.Method.Name == "ThenBy" || currentCall.Method.Name == "ThenByDescending")
                {
                    var orderByVisitor = new OrderByClauseVisitor<T>(
                        SqlServerOrmDataProvider.ColumnNames,
                        SqlServerOrmDataProvider._unmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                            t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray()));
                    orderByVisitor.Visit(currentCall);
                    components.OrderByClause = orderByVisitor.OrderByClause;
                }
                else if ((currentCall.Method.Name == "Any" || currentCall.Method.Name == "All" || currentCall.Method.Name == "Count") && (currentCall.Arguments.Count == 1 || currentCall.Arguments.Count == 2))
                {
                    components.IsAggregate = true;
                    components.AggregateClause = BuildAggregateClause(currentCall, components.WhereClause, components.Parameters, parameterGenerator, translator);
                }
                else if (currentCall.Method.Name == "Average" || currentCall.Method.Name == "Min" || currentCall.Method.Name == "Max")
                {
                    components.IsAggregate = true;
                    components.AggregateClause = BuildAggregateClause(currentCall, components.WhereClause, components.Parameters, parameterGenerator, translator);
                }
            }

            return components;
        }

        /// <summary>
        /// Builds the SQL clause for aggregate methods (Any, All, Count, Average, Min, Max).
        /// Handles predicate-based and selector-based aggregates.
        /// </summary>
        /// <param name="methodCall">The method call expression for the aggregate.</param>
        /// <param name="whereClause">The existing WHERE clause, if any.</param>
        /// <param name="existingParameters">The existing SQL parameters, if any.</param>
        /// <param name="parameterGenerator">The parameter generator for SQL parameters.</param>
        /// <param name="translator">The SQL expression translator for method calls.</param>
        /// <returns>The SQL clause for the aggregate operation.</returns>
        private string BuildAggregateClause(MethodCallExpression methodCall, string whereClause, List<SqlParameter> existingParameters, ParameterGenerator parameterGenerator, SqlExpressionTranslator translator)
        {
            string aggregateClause = null;
            var parameters = existingParameters != null ? new List<SqlParameter>(existingParameters) : new List<SqlParameter>();
            string table = _dataProvider.GetTableName<T>();

            string methodName = methodCall.Method.Name;
            bool isAny = methodName == "Any";
            bool isAll = methodName == "All";
            bool isCount = methodName == "Count";
            bool isPredicateBased = isAny || isAll || isCount;

            if (isPredicateBased)
            {
                bool hasPredicate = methodCall.Arguments.Count == 2;
                if (isAll && !hasPredicate)
                {
                    throw new InvalidOperationException("All method requires a predicate.");
                }

                string modifiedWhereClause = null;
                if (hasPredicate)
                {
                    var lambda = (LambdaExpression)((UnaryExpression)methodCall.Arguments[1]).Operand;
                    var predicateExpression = (Expression<Func<T, bool>>)lambda;
                    var whereVisitor = new WhereClauseVisitor<T>(
                        SqlServerOrmDataProvider.ColumnNames,
                        SqlServerOrmDataProvider._unmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                            t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray()),
                        parameterGenerator,
                        translator);
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
                                do
                                {
                                    newParamName = $"{param.ParameterName}_{suffix++}";
                                } while (parameters.Any(p => p.ParameterName == newParamName));
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
                    if (!string.IsNullOrEmpty(whereClause))
                    {
                        combinedWhere += whereClause;
                    }
                    if (hasPredicate)
                    {
                        if (!string.IsNullOrEmpty(combinedWhere))
                        {
                            combinedWhere += " AND ";
                        }
                        combinedWhere += modifiedWhereClause;
                    }
                    if (!string.IsNullOrEmpty(combinedWhere))
                    {
                        aggregateClause += $"\r\nWHERE {combinedWhere}";
                    }
                }
                else if (isAny)
                {
                    aggregateClause = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {table}";
                    string combinedWhere = "";
                    if (!string.IsNullOrEmpty(whereClause))
                    {
                        combinedWhere += whereClause;
                    }
                    if (hasPredicate)
                    {
                        if (!string.IsNullOrEmpty(combinedWhere))
                        {
                            combinedWhere += " AND ";
                        }
                        combinedWhere += modifiedWhereClause;
                    }
                    if (!string.IsNullOrEmpty(combinedWhere))
                    {
                        aggregateClause += $"\r\nWHERE {combinedWhere}";
                    }
                    aggregateClause += ") THEN 1 ELSE 0 END";
                }
                else if (isAll)
                {
                    aggregateClause = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {table}";
                    string combinedWhere = "";
                    if (!string.IsNullOrEmpty(whereClause))
                    {
                        combinedWhere += whereClause;
                        if (hasPredicate)
                        {
                            combinedWhere += " AND ";
                        }
                    }
                    if (hasPredicate)
                    {
                        combinedWhere += $"NOT ({modifiedWhereClause})";
                    }
                    if (!string.IsNullOrEmpty(combinedWhere))
                    {
                        aggregateClause += $"\r\nWHERE {combinedWhere}";
                    }
                    aggregateClause += ") THEN 0 ELSE 1 END";
                }
            }
            else if (methodCall.Method.Name == "Average" || methodCall.Method.Name == "Min" || methodCall.Method.Name == "Max")
            {
                var lambda = (LambdaExpression)((UnaryExpression)methodCall.Arguments[1]).Operand;
                string columnExpression;
                MemberExpression memberExpression = lambda.Body as MemberExpression
                    ?? (lambda.Body as UnaryExpression)?.Operand as MemberExpression;
                var expression = memberExpression?.Expression as ParameterExpression;
                if (expression == null)
                {
                    throw new NotSupportedException("Aggregate function Average does not support expression evaluation; aggregates are only supported on column selectors.");
                }
                var property = memberExpression?.Member as PropertyInfo;
                if (property != null &&
                    SqlServerOrmDataProvider._unmappedPropertiesCache.GetOrAdd(typeof(T),
                            t => t.GetProperties()
                                .Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null)
                                .ToArray())
                        .All(p => p.Name != property.Name))
                {
                    columnExpression = SqlServerOrmDataProvider.ColumnNames.GetOrAdd(property.ToDictionaryKey(), p => _dataProvider.GetCachedColumnName(property));
                }
                else
                {
                    throw new NotSupportedException("Only simple member access is supported in aggregate expressions.");
                }
                var aggregateFunction = methodCall.Method.Name.ToUpper() == "AVERAGE" ? "AVG" : methodCall.Method.Name.ToUpper();
                aggregateClause = $"SELECT {aggregateFunction}({columnExpression}) FROM {table}";
                if (!string.IsNullOrEmpty(whereClause))
                {
                    aggregateClause += $"\r\nWHERE {whereClause}";
                }
            }

            // Update the components with the combined parameters, ensuring we don't lose existing parameters
            if (existingParameters != null)
            {
                existingParameters.Clear();
                existingParameters.AddRange(parameters);
            }

            return aggregateClause;
        }

        /// <summary>
        /// Builds the complete SQL query by combining SELECT, WHERE, ORDER BY, and paging clauses.
        /// </summary>
        /// <param name="components">The query components extracted from the expression.</param>
        /// <returns>The complete SQL query text.</returns>
        private string BuildQueryComponents(QueryComponents components)
        {
            string commandText = _selectClause ?? _dataProvider.CreateGetOneOrSelectCommandText<T>();

            if (!string.IsNullOrEmpty(components.WhereClause))
            {
                commandText += $"\r\nWHERE {components.WhereClause}";
            }

            if (!string.IsNullOrEmpty(components.OrderByClause) || components.Skip.HasValue || components.Take.HasValue)
            {
                commandText += $"\r\n{(components.OrderByClause ?? "ORDER BY id")}";
            }

            if (components.Skip.HasValue || components.Take.HasValue)
            {
                commandText += $"\r\nOFFSET {components.Skip ?? 0} ROWS";
                if (components.Take.HasValue)
                {
                    commandText += $"\r\nFETCH NEXT {components.Take.Value} ROWS ONLY";
                }
            }

            return commandText;
        }

        /// <summary>
        /// Executes an aggregate query and converts the result to the requested type.
        /// Handles conversion for Any, All, Count, Average, Min, and Max.
        /// </summary>
        /// <typeparam name="TResult">The type of the result.</typeparam>
        /// <param name="components">The query components containing the aggregate clause.</param>
        /// <param name="expression">The original LINQ expression.</param>
        /// <returns>The aggregate result cast to TResult.</returns>
        private TResult HandleAggregateQuery<TResult>(QueryComponents components, Expression expression)
        {
            using (var connectionScope = new SqlServerOrmDataProvider.ConnectionScope(_dataProvider))
            {
                using (var sqlCommand = _dataProvider.BuildSqlCommandObject(components.AggregateClause,
                           connectionScope.Connection, components.Parameters))
                {
                    _dataProvider.InvokeLogAction(sqlCommand);
                    var result = sqlCommand.ExecuteScalar();
                    if (result == DBNull.Value)
                    {
                        if (components.OuterMethodCall?.Method.Name == "Average" || components.OuterMethodCall?.Method.Name == "Min" || components.OuterMethodCall?.Method.Name == "Max")
                        {
                            throw new InvalidOperationException(
                                $"Sequence contains no elements for {components.OuterMethodCall.Method.Name}.");
                        }

                        result = 0;
                    }

                    Debug.WriteLine($"Aggregate result type: {result?.GetType()?.FullName ?? "null"}");
                    Debug.WriteLine($"Aggregate result value: {result}");

                    if (components.OuterMethodCall?.Method.Name == "Any" || components.OuterMethodCall?.Method.Name == "All")
                    {
                        var boolResult = Convert.ToInt32(result) == 1;
                        Debug.WriteLine($"Bool result after conversion: {boolResult}");
                        return (TResult)(object)boolResult;
                    }
                    else if (components.OuterMethodCall?.Method.Name == "Count")
                    {
                        return (TResult)(object)Convert.ToInt32(result);
                    }
                    else if (components.OuterMethodCall?.Method.Name == "Average")
                    {
                        return (TResult)(object)Convert.ToDouble(result);
                    }
                    else if (components.OuterMethodCall?.Method.Name == "Min" || components.OuterMethodCall?.Method.Name == "Max")
                    {
                        var lambda =
                            (LambdaExpression)((UnaryExpression)components.OuterMethodCall.Arguments[1]).Operand;
                        Type selectorType = GetSelectorType(lambda.Body);

                        if (selectorType == typeof(DateTime))
                        {
                            return (TResult)(object)Convert.ToDateTime(result);
                        }

                        if (selectorType == typeof(DateTime?))
                        {
                            return result is DBNull
                                ? (TResult)(object)null
                                : (TResult)(object)Convert.ToDateTime(result);
                        }
                        else if (selectorType == typeof(int))
                        {
                            return (TResult)(object)Convert.ToInt32(result);
                        }
                        else if (selectorType == typeof(long))
                        {
                            return (TResult)(object)Convert.ToInt64(result);
                        }
                        else if (selectorType == typeof(double))
                        {
                            return (TResult)(object)Convert.ToDouble(result);
                        }
                        else if (selectorType == typeof(decimal))
                        {
                            return (TResult)(object)Convert.ToDecimal(result);
                        }
                        else
                        {
                            throw new NotSupportedException(
                                $"Unsupported selector type {selectorType} for {components.OuterMethodCall.Method.Name}.");
                        }
                    }

                    return default;
                }
            }
        }

        /// <summary>
        /// Executes the SQL query and returns the result as either a single entity or a collection.
        /// Handles error cases for First/Last when no result is found.
        /// </summary>
        /// <typeparam name="TResult">The type of the result.</typeparam>
        /// <param name="commandText">The SQL query text.</param>
        /// <param name="parameters">The SQL parameters for the query.</param>
        /// <param name="isCollection">Whether the result is a collection (IEnumerable&lt;T&gt;).</param>
        /// <param name="expression">The original LINQ expression for error handling.</param>
        /// <returns>The query result cast to TResult.</returns>
        private TResult ExecuteQuery<TResult>(string commandText, List<SqlParameter> parameters, bool isCollection, Expression expression)
        {
            using (var connectionScope = new SqlServerOrmDataProvider.ConnectionScope(_dataProvider))
            {
                using (var command =
                       _dataProvider.BuildSqlCommandObject(commandText, connectionScope.Connection, parameters))
                {
                    if (isCollection)
                    {
                        return (TResult)(object)_dataProvider.ExecuteReaderList<T>(command);
                    }
                    else
                    {
                        var result = _dataProvider.ExecuteReaderSingle<T>(command);

                        if (result == null && expression is MethodCallExpression call)
                        {
                            if (call.Method.Name == "First" || call.Method.Name == "Last")
                            {
                                throw new InvalidOperationException(
                                    $"Sequence contains no matching element for {call.Method.Name}.");
                            }
                        }

                        return (TResult)(object)result;
                    }
                }
            }
        }

        /// <summary>
        /// Determines the type of the selector expression used in aggregate methods like Average, Min, and Max.
        /// </summary>
        /// <param name="selector">The selector expression.</param>
        /// <returns>The type of the selector expression.</returns>
        /// <exception cref="NotSupportedException">Thrown when the selector expression type is not supported.</exception>
        private Type GetSelectorType(Expression selector)
        {
            if (selector is MemberExpression memberExpression)
            {
                return memberExpression.Type;
            }
            else if (selector is UnaryExpression unaryExpression)
            {
                return unaryExpression.Type;
            }
            else if (selector is MethodCallExpression methodCallExpression)
            {
                return methodCallExpression.Method.ReturnType;
            }
            else
            {
                throw new NotSupportedException("Unsupported selector expression type.");
            }
        }
    }
}