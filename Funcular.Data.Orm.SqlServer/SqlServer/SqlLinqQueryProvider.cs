using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Funcular.Data.Orm.Visitors;
using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.SqlServer
{
    /// <summary>
    /// A LINQ query provider that translates LINQ expressions into SQL queries for execution against a SQL Server database.
    /// </summary>
    /// <typeparam name="T">The type of the entity being queried.</typeparam>
    public class SqlLinqQueryProvider<T> : IQueryProvider where T : class, new()
    {
        private readonly SqlServerOrmDataProvider _dataProvider;
        private readonly string? _selectClause;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlLinqQueryProvider{T}"/> class.
        /// </summary>
        /// <param name="dataProvider">The data provider used to execute SQL commands.</param>
        /// <param name="selectClause">An optional custom SELECT clause to use in queries. If null, a default SELECT clause is generated.</param>
        public SqlLinqQueryProvider(SqlServerOrmDataProvider dataProvider, string? selectClause = null)
        {
            _dataProvider = dataProvider;
            _selectClause = selectClause;
        }

        public IQueryable CreateQuery(Expression expression)
        {
            return new SqlQueryable<T>(this, expression);
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            return new SqlQueryable<TElement>(this, expression);
        }

        public object? Execute(Expression expression)
        {
            return Execute<IEnumerable<T>>(expression);
        }

        /// <summary>
        /// Executes the specified expression and returns the result as the specified type.
        /// </summary>
        /// <typeparam name="TResult">The type of the result.</typeparam>
        /// <param name="expression">The expression to execute.</param>
        /// <returns>The result of the query execution.</returns>
        /// <exception cref="NotSupportedException">Thrown when the expression type is not supported.</exception>
        /// <exception cref="InvalidOperationException">Thrown when an operation cannot be performed, such as when All is called without a predicate.</exception>
        public TResult? Execute<TResult>(Expression expression)
        {
            Console.WriteLine($"Executing expression: {expression}");
            var parameterGenerator = new ParameterGenerator();
            var translator = new SqlExpressionTranslator(parameterGenerator);

            var components = ParseExpression(expression, parameterGenerator, translator);

            bool isCollection = typeof(IEnumerable<T>).IsAssignableFrom(typeof(TResult)) && typeof(TResult) != typeof(T);

            TResult? executeResult;
            if (components.IsAggregate)
            {
                executeResult = HandleAggregateQuery<TResult>(components, expression);
            }
            else
            {
                string commandText = BuildQueryComponents(components);
                executeResult = ExecuteQuery<TResult>(commandText, components.Parameters, isCollection, expression);
            }

            Console.WriteLine($"Execute result: {executeResult}");
            return executeResult;
        }

        /// <summary>
        /// Represents the components of a SQL query extracted from an expression.
        /// </summary>
        private class QueryComponents
        {
            private readonly List<SqlParameter> _parameters = new List<SqlParameter>();
            public string? WhereClause { get; set; }

            public List<SqlParameter> Parameters
            {
                get => _parameters;
                set
                {
                    _parameters.Clear();
                    _parameters.AddRange(value);
                }
            }

            public string? OrderByClause { get; set; }
            public int? Skip { get; set; }
            public int? Take { get; set; }
            public string? AggregateClause { get; set; }
            public bool IsAggregate { get; set; }
            public MethodCallExpression? OuterMethodCall { get; set; }
        }

        /// <summary>
        /// Parses the expression tree to extract query components (WHERE, ORDER BY, SKIP, TAKE, aggregates).
        /// </summary>
        /// <param name="expression">The LINQ expression to parse.</param>
        /// <param name="parameterGenerator">The parameter generator to ensure consistent parameter naming.</param>
        /// <param name="translator">The translator for method call expressions.</param>
        /// <returns>A <see cref="QueryComponents"/> object containing the extracted components.</returns>
        private QueryComponents ParseExpression(Expression expression, ParameterGenerator parameterGenerator, SqlExpressionTranslator translator)
        {
            var components = new QueryComponents();
            components.Parameters = new List<SqlParameter>(); // Initialize the parameters list

            if (expression is not MethodCallExpression)
            {
                return components;
            }

            // Collect all method calls in the chain (from outer to inner)
            var methodCalls = new List<MethodCallExpression>();
            Expression currentExpression = expression;
            while (currentExpression is MethodCallExpression methodCall)
            {
                methodCalls.Add(methodCall);
                currentExpression = methodCall.Arguments[0];
            }

            // Process method calls in reverse order (from inner to outer)
            for (int i = methodCalls.Count - 1; i >= 0; i--)
            {
                var currentCall = methodCalls[i];
                
                if (currentCall.Method.Name is "Any" or "All" or "Count" or "Average" or "Min" or "Max")
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
                    components.Skip = (int)((ConstantExpression)currentCall.Arguments[1]).Value;
                }
                else if (currentCall.Method.Name == "Take")
                {
                    components.Take = (int)((ConstantExpression)currentCall.Arguments[1]).Value;
                }
                else if (currentCall.Method.Name is "FirstOrDefault" or "First" or "LastOrDefault" or "Last" && currentCall.Arguments.Count == 2)
                {
                    var lambda = (LambdaExpression)((UnaryExpression)currentCall.Arguments[1]).Operand;
                    var whereExpression = (Expression<Func<T, bool>>)lambda;
                    var elements = _dataProvider.GenerateWhereClause(whereExpression, parameterGenerator: parameterGenerator, translator: translator);
                    components.WhereClause = elements.WhereClause;
                    if (elements.SqlParameters != null)
                    {
                        components.Parameters.AddRange(elements.SqlParameters);
                    }

                    if (currentCall.Method.Name is "LastOrDefault" or "Last")
                    {
                        var orderByVisitor = new OrderByClauseVisitor<T>(
                            SqlServerOrmDataProvider._columnNames,
                            SqlServerOrmDataProvider._unmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                                t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToImmutableArray()));
                        orderByVisitor.Visit(Expression.Call(
                            null,
                            typeof(Queryable).GetMethods().First(m => m.Name == "OrderByDescending" && m.GetParameters().Length == 2),
                            Expression.Constant(null),
                            Expression.Lambda<Func<T, int>>(Expression.Property(Expression.Parameter(typeof(T), "p"), "Id"), Expression.Parameter(typeof(T), "p"))));
                        components.OrderByClause = orderByVisitor.OrderByClause;
                    }
                }
                else if (currentCall.Method.Name is "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending")
                {
                    var orderByVisitor = new OrderByClauseVisitor<T>(
                        SqlServerOrmDataProvider._columnNames,
                        SqlServerOrmDataProvider._unmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                            t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToImmutableArray()));
                    orderByVisitor.Visit(currentCall);
                    components.OrderByClause = orderByVisitor.OrderByClause;
                }
                else if (currentCall.Method.Name is "Any" or "All" or "Count" && currentCall.Arguments.Count == 2)
                {
                    components.IsAggregate = true;
                    components.AggregateClause = BuildAggregateClause(currentCall, components.WhereClause, components.Parameters, parameterGenerator, translator);
                }
                else if (currentCall.Method.Name is "Any" or "All" or "Count" && currentCall.Arguments.Count == 1)
                {
                    components.IsAggregate = true;
                    components.AggregateClause = BuildAggregateClause(currentCall, components.WhereClause, components.Parameters, parameterGenerator, translator);
                }
                else if (currentCall.Method.Name is "Average" or "Min" or "Max")
                {
                    components.IsAggregate = true;
                    components.AggregateClause = BuildAggregateClause(currentCall, components.WhereClause, components.Parameters, parameterGenerator, translator);
                }
            }

            return components;
        }

        /// <summary>
        /// Builds the aggregate clause for methods like Any, All, Count, Average, Min, Max.
        /// </summary>
        /// <param name="methodCall">The method call expression for the aggregate.</param>
        /// <param name="whereClause">The existing WHERE clause, if any.</param>
        /// <param name="existingParameters">The existing SQL parameters, if any.</param>
        /// <param name="parameterGenerator">The parameter generator to ensure consistent parameter naming.</param>
        /// <param name="translator">The translator for method call expressions.</param>
        /// <returns>The SQL clause for the aggregate operation.</returns>
        private string? BuildAggregateClause(MethodCallExpression methodCall, string? whereClause, List<SqlParameter>? existingParameters, ParameterGenerator parameterGenerator, SqlExpressionTranslator translator)
        {
            string? aggregateClause = null;
            var parameters = existingParameters != null ? new List<SqlParameter>(existingParameters) : new List<SqlParameter>();
            string innerQuery = $"SELECT * FROM {_dataProvider.GetTableName<T>()}";
            if (!string.IsNullOrEmpty(whereClause))
            {
                innerQuery += "\r\nWHERE " + whereClause;
            }

            if (methodCall.Method.Name is "Any" or "All" or "Count" && methodCall.Arguments.Count == 2)
            {
                var lambda = (LambdaExpression)((UnaryExpression)methodCall.Arguments[1]).Operand;
                var predicateExpression = (Expression<Func<T, bool>>)lambda;
                var whereVisitor = new WhereClauseVisitor<T>(
                    SqlServerOrmDataProvider._columnNames,
                    SqlServerOrmDataProvider._unmappedPropertiesCache.GetOrAdd(typeof(T), t =>
                        t.GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToImmutableArray()),
                    parameterGenerator,
                    translator);
                whereVisitor.Visit(predicateExpression);

                // Store the WHERE clause in a local variable to modify it
                string modifiedWhereClause = whereVisitor.WhereClauseBody;

                // Combine parameters from the inner query and the predicate
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
                                newParamName = param.ParameterName + "_" + suffix++;
                            } while (parameters.Any(p => p.ParameterName == newParamName));
                            modifiedWhereClause = modifiedWhereClause.Replace(param.ParameterName, newParamName);
                            param.ParameterName = newParamName;
                        }
                        parameters.Add(param);
                    }
                }

                if (methodCall.Method.Name == "Any")
                {
                    aggregateClause = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM ({innerQuery}) AS innerQuery WHERE {modifiedWhereClause}) THEN 1 ELSE 0 END";
                }
                else if (methodCall.Method.Name == "All")
                {
                    aggregateClause = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM ({innerQuery}) AS innerQuery WHERE NOT ({modifiedWhereClause})) THEN 0 ELSE 1 END";
                }
                else if (methodCall.Method.Name == "Count")
                {
                    aggregateClause = $"SELECT COUNT(*) FROM ({innerQuery}) AS innerQuery WHERE {modifiedWhereClause}";
                }
            }
            else if (methodCall.Method.Name is "Any" or "All" or "Count" && methodCall.Arguments.Count == 1)
            {
                if (methodCall.Method.Name == "All")
                {
                    throw new InvalidOperationException("All method requires a predicate.");
                }

                if (methodCall.Method.Name == "Any")
                {
                    aggregateClause = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM ({innerQuery}) AS innerQuery) THEN 1 ELSE 0 END";
                }
                else if (methodCall.Method.Name == "Count")
                {
                    aggregateClause = $"SELECT COUNT(*) FROM ({innerQuery}) AS innerQuery";
                }
            }
            else if (methodCall.Method.Name is "Average" or "Min" or "Max")
            {
                var lambda = (LambdaExpression)((UnaryExpression)methodCall.Arguments[1]).Operand;
                string columnExpression;

                MemberExpression? memberExpression = lambda.Body as MemberExpression
                    ?? (lambda.Body as UnaryExpression)?.Operand as MemberExpression;

                if (memberExpression?.Expression is not ParameterExpression)
                {
                    throw new NotSupportedException("Aggregate function Average does not support expression evaluation; aggregates are only supported on column selectors.");
                }

                var property = memberExpression?.Member as PropertyInfo;
                if (property != null && SqlServerOrmDataProvider._unmappedPropertiesCache.All(p => p.Key.Name != property.Name))
                {
                    columnExpression = SqlServerOrmDataProvider._columnNames.GetOrAdd(property.ToDictionaryKey(), p => _dataProvider.GetColumnName(property));
                }
                else
                {
                    throw new NotSupportedException("Only simple member access is supported in aggregate expressions.");
                }

                var aggregateFunction = methodCall.Method.Name.ToUpper() == "AVERAGE" ? "AVG" : methodCall.Method.Name.ToUpper();
                aggregateClause = $"SELECT {aggregateFunction}({columnExpression}) FROM {_dataProvider.GetTableName<T>()}";
                if (!string.IsNullOrEmpty(whereClause))
                {
                    aggregateClause += "\r\nWHERE " + whereClause;
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
        /// Builds the complete SQL query by combining SELECT, WHERE, ORDER BY, and OFFSET/FETCH clauses.
        /// </summary>
        /// <param name="components">The query components extracted from the expression.</param>
        /// <returns>The complete SQL query text.</returns>
        private string BuildQueryComponents(QueryComponents components)
        {
            string commandText = _selectClause ?? _dataProvider.CreateGetOneOrSelectCommand<T>();

            if (!string.IsNullOrEmpty(components.WhereClause))
            {
                commandText += "\r\nWHERE " + components.WhereClause;
            }

            if (!string.IsNullOrEmpty(components.OrderByClause) || components.Skip.HasValue || components.Take.HasValue)
            {
                commandText += "\r\n" + (components.OrderByClause ?? "ORDER BY id");
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
        /// Handles aggregate query execution and result conversion.
        /// </summary>
        /// <typeparam name="TResult">The type of the result.</typeparam>
        /// <param name="components">The query components containing the aggregate clause.</param>
        /// <param name="expression">The original LINQ expression.</param>
        /// <returns>The aggregate result cast to TResult.</returns>
        private TResult? HandleAggregateQuery<TResult>(QueryComponents components, Expression expression)
        {
            using var connectionScope = new SqlServerOrmDataProvider.ConnectionScope(_dataProvider);
            using var sqlCommand = _dataProvider.BuildCommand(components.AggregateClause, connectionScope.Connection, components.Parameters);

            var result = sqlCommand.ExecuteScalar();
            if (result == DBNull.Value)
            {
                if (components.OuterMethodCall?.Method.Name is "Average" or "Min" or "Max")
                {
                    throw new InvalidOperationException($"Sequence contains no elements for {components.OuterMethodCall.Method.Name}.");
                }
                result = 0;
            }

            Console.WriteLine($"Aggregate result type: {result?.GetType()?.FullName ?? "null"}");
            Console.WriteLine($"Aggregate result value: {result}");

            if (components.OuterMethodCall?.Method.Name is "Any" or "All")
            {
                var boolResult = Convert.ToInt32(result) == 1;
                Console.WriteLine($"Bool result after conversion: {boolResult}");
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
            else if (components.OuterMethodCall?.Method.Name is "Min" or "Max")
            {
                var lambda = (LambdaExpression)((UnaryExpression)components.OuterMethodCall.Arguments[1]).Operand;
                Type selectorType = GetSelectorType(lambda.Body);

                if (selectorType == typeof(DateTime))
                {
                    return (TResult)(object)Convert.ToDateTime(result);
                }
                if (selectorType == typeof(DateTime?))
                {
                    return result is DBNull ? (TResult?)(object?)null : (TResult)(object)Convert.ToDateTime(result);
                }
                else if (selectorType == typeof(int))
                {
                    return (TResult)(object)Convert.ToInt32(result);
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
                    throw new NotSupportedException($"Unsupported selector type {selectorType} for {components.OuterMethodCall.Method.Name}.");
                }
            }

            return default;
        }
        /// <summary>
        /// Executes the SQL query and returns the result as either a single entity or a collection.
        /// </summary>
        /// <typeparam name="TResult">The type of the result.</typeparam>
        /// <param name="commandText">The SQL query text.</param>
        /// <param name="parameters">The SQL parameters for the query.</param>
        /// <param name="isCollection">Whether the result is a collection (IEnumerable<T>).</param>
        /// <param name="expression">The original LINQ expression for error handling.</param>
        /// <returns>The query result cast to TResult.</returns>
        private TResult? ExecuteQuery<TResult>(string commandText, List<SqlParameter>? parameters, bool isCollection, Expression expression)
        {
            using var connectionScope = new SqlServerOrmDataProvider.ConnectionScope(_dataProvider);
            using var command = _dataProvider.BuildCommand(commandText, connectionScope.Connection, parameters);

            if (isCollection)
            {
                return (TResult)(object)_dataProvider.ExecuteReaderList<T>(command);
            }
            else
            {
                var result = _dataProvider.ExecuteReaderSingle<T>(command);

                if (result == null && expression is MethodCallExpression call)
                {
                    if (call.Method.Name is "First" or "Last")
                    {
                        throw new InvalidOperationException($"Sequence contains no matching element for {call.Method.Name}.");
                    }
                }

                return (TResult)(object)result!;
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