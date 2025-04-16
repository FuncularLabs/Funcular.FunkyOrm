using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer
{
    /// <summary>
    /// A LINQ query provider that translates LINQ queries into SQL queries for execution against a SQL Server database.
    /// </summary>
    /// <typeparam name="T">The type of the entity being queried.</typeparam>
    public class SqlQueryProvider<T> : IQueryProvider where T : class, new()
    {
        private readonly FunkySqlDataProvider _dataProvider;
        private readonly string? _selectClause;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlQueryProvider{T}"/> class.
        /// </summary>
        /// <param name="dataProvider">The data provider used to execute SQL commands.</param>
        /// <param name="selectClause">An optional custom SELECT clause to use in queries. If null, a default SELECT clause is generated.</param>
        public SqlQueryProvider(FunkySqlDataProvider dataProvider, string? selectClause = null)
        {
            _dataProvider = dataProvider;
            _selectClause = selectClause;
        }

        /// <summary>
        /// Creates a new <see cref="IQueryable"/> that can be used to query the specified expression.
        /// </summary>
        /// <param name="expression">The expression to query.</param>
        /// <returns>An <see cref="IQueryable"/> that represents the query.</returns>
        public IQueryable CreateQuery(Expression expression)
        {
            return new SqlQueryable<T>(this, expression);
        }

        /// <summary>
        /// Creates a new <see cref="IQueryable{TElement}"/> that can be used to query the specified expression.
        /// </summary>
        /// <typeparam name="TElement">The type of the elements in the query result.</typeparam>
        /// <param name="expression">The expression to query.</param>
        /// <returns>An <see cref="IQueryable{TElement}"/> that represents the query.</returns>
        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            return new SqlQueryable<TElement>(this, expression);
        }

        /// <summary>
        /// Executes the specified expression and returns the result as an object.
        /// </summary>
        /// <param name="expression">The expression to execute.</param>
        /// <returns>The result of the query execution.</returns>
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
            // Extract query components
            string? whereClause = null;
            List<SqlParameter>? parameters = null;
            string? orderByClause = null;
            int? skip = null;
            int? take = null;
            string? aggregateClause = null;
            bool isAggregate = false;
            MethodCallExpression? outerMethodCall = null;

            // Process the expression tree
            if (expression is MethodCallExpression methodCall)
            {
                outerMethodCall = methodCall;

                // Traverse the expression tree to collect components (Where, Skip, Take, etc.)
                Expression currentExpression = expression;
                while (currentExpression is MethodCallExpression currentCall)
                {
                    if (currentCall.Method.Name == "Where")
                    {
                        var lambda = (LambdaExpression)((UnaryExpression)currentCall.Arguments[1]).Operand;
                        var whereExpression = (Expression<Func<T, bool>>)lambda;
                        var elements = _dataProvider.GenerateWhereClause(whereExpression);
                        whereClause = elements.WhereClause;
                        parameters = elements.SqlParameters;
                    }
                    else if (currentCall.Method.Name == "Skip")
                    {
                        var skipValue = (int)((ConstantExpression)currentCall.Arguments[1]).Value;
                        skip = skipValue;
                    }
                    else if (currentCall.Method.Name == "Take")
                    {
                        var takeValue = (int)((ConstantExpression)currentCall.Arguments[1]).Value;
                        take = takeValue;
                    }
                    else if (currentCall.Method.Name is "FirstOrDefault" or "First" or "LastOrDefault" or "Last" && currentCall.Arguments.Count == 2)
                    {
                        var lambda = (LambdaExpression)((UnaryExpression)currentCall.Arguments[1]).Operand;
                        var whereExpression = (Expression<Func<T, bool>>)lambda;
                        var elements = _dataProvider.GenerateWhereClause(whereExpression);
                        whereClause = elements.WhereClause;
                        parameters = elements.SqlParameters;

                        if (currentCall.Method.Name is "LastOrDefault" or "Last")
                        {
                            orderByClause = "ORDER BY id DESC";
                        }
                    }
                    currentExpression = currentCall.Arguments[0];
                }

                // Handle aggregation methods (Any, All, Count, Average, Min, Max)
                if (methodCall.Method.Name is "Any" or "All" or "Count" or "Average" or "Min" or "Max")
                {
                    isAggregate = true;

                    // Handle Any, All, Count with predicates
                    if (methodCall.Method.Name is "Any" or "All" or "Count" && methodCall.Arguments.Count == 2)
                    {
                        var lambda = (LambdaExpression)((UnaryExpression)methodCall.Arguments[1]).Operand;
                        var predicateExpression = (Expression<Func<T, bool>>)lambda;
                        var elements = _dataProvider.GenerateWhereClause(predicateExpression);

                        // Combine parameters and ensure unique names
                        parameters = parameters ?? new List<SqlParameter>();
                        if (elements.SqlParameters != null)
                        {
                            // Check for duplicate parameter names and rename if necessary
                            foreach (var param in elements.SqlParameters)
                            {
                                var existingParam = parameters.FirstOrDefault(p => p.ParameterName == param.ParameterName);
                                if (existingParam != null)
                                {
                                    // Generate a new unique parameter name
                                    int suffix = 1;
                                    string newParamName;
                                    do
                                    {
                                        newParamName = param.ParameterName + "_" + suffix++;
                                    } while (parameters.Any(p => p.ParameterName == newParamName));

                                    // Replace the parameter name in the whereClause
                                    elements.WhereClause = elements.WhereClause.Replace(param.ParameterName, newParamName);
                                    param.ParameterName = newParamName;
                                }
                                parameters.Add(param);
                            }
                        }

                        // Build the inner query (the filtered set from Where)
                        string innerQuery = $"SELECT * FROM {_dataProvider.GetTableName<T>()}";
                        if (!string.IsNullOrEmpty(whereClause))
                        {
                            innerQuery += "\r\nWHERE " + whereClause;
                        }

                        if (methodCall.Method.Name == "Any")
                        {
                            aggregateClause = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM ({innerQuery}) AS innerQuery WHERE {elements.WhereClause}) THEN 1 ELSE 0 END";
                        }
                        else if (methodCall.Method.Name == "All")
                        {
                            aggregateClause = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM ({innerQuery}) AS innerQuery WHERE NOT ({elements.WhereClause})) THEN 0 ELSE 1 END";
                        }
                        else if (methodCall.Method.Name == "Count")
                        {
                            aggregateClause = $"SELECT COUNT(*) FROM ({innerQuery}) AS innerQuery WHERE {elements.WhereClause}";
                        }
                    }
                    // Handle Any, All, Count without predicates
                    else if (methodCall.Method.Name is "Any" or "All" or "Count" && methodCall.Arguments.Count == 1)
                    {
                        if (methodCall.Method.Name == "All")
                        {
                            throw new InvalidOperationException("All method requires a predicate.");
                        }

                        string innerQuery = $"SELECT * FROM {_dataProvider.GetTableName<T>()}";
                        if (!string.IsNullOrEmpty(whereClause))
                        {
                            innerQuery += "\r\nWHERE " + whereClause;
                        }

                        if (methodCall.Method.Name == "Any")
                        {
                            aggregateClause = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM ({innerQuery}) AS innerQuery) THEN 1 ELSE 0 END";
                        }
                        else if (methodCall.Method.Name == "Count")
                        {
                            aggregateClause = $"SELECT COUNT(*) FROM ({innerQuery}) AS innerQuery";
                        }

                        isAggregate = true;
                    }
                    // Handle Average, Min, Max with selectors
                    else if (outerMethodCall.Method.Name is "Average" or "Min" or "Max")
                    {
                        var lambda = (LambdaExpression)((UnaryExpression)outerMethodCall.Arguments[1]).Operand;
                        string columnExpression;

                        // Extract the MemberExpression, whether directly or via UnaryExpression
                        MemberExpression memberExpression = null;
                        if (lambda.Body is MemberExpression directMember)
                        {
                            memberExpression = directMember;
                        }
                        else if (lambda.Body is UnaryExpression unaryExpression && unaryExpression.Operand is MemberExpression unaryMember)
                        {
                            memberExpression = unaryMember;
                        }
                        else
                        {
                            throw new NotSupportedException("Aggregate function Average does not support expression evaluation; aggregates are only supported on column selectors.");
                        }

                        // Validate that the member access is direct (i.e., on the parameter)
                        if (memberExpression.Expression is not ParameterExpression)
                        {
                            throw new NotSupportedException("Aggregate function Average does not support expression evaluation; aggregates are only supported on column selectors.");
                        }

                        var property = memberExpression.Member as PropertyInfo;
                        if (property != null && FunkySqlDataProvider._unmappedPropertiesCache.All(p => p.Key.Name != property.Name))
                        {
                            columnExpression = FunkySqlDataProvider._columnNames.GetOrAdd(property.ToDictionaryKey(), p => _dataProvider.GetColumnName(property));
                        }
                        else
                        {
                            throw new NotSupportedException("Only simple member access is supported in aggregate expressions.");
                        }

                        var aggregateFunction = outerMethodCall.Method.Name.ToUpper() == "AVERAGE" ? "AVG" : outerMethodCall.Method.Name.ToUpper();
                        aggregateClause = $"SELECT {aggregateFunction}({columnExpression}) FROM {_dataProvider.GetTableName<T>()}";
                        if (!string.IsNullOrEmpty(whereClause))
                        {
                            aggregateClause += "\r\nWHERE " + whereClause;
                        }
                    }
                }
            }

            // Determine if we're executing a single result or a collection
            bool isCollection = typeof(IEnumerable<T>).IsAssignableFrom(typeof(TResult)) && typeof(TResult) != typeof(T);
            var connectionScope = new FunkySqlDataProvider.ConnectionScope(_dataProvider);

            try
            {
                if (isAggregate)
                {
                    using var sqlCommand = _dataProvider.BuildCommand(aggregateClause, connectionScope.Connection, parameters);
                    var result = sqlCommand.ExecuteScalar();
                    if (result == DBNull.Value)
                    {
                        if (outerMethodCall?.Method.Name is "Average" or "Min" or "Max")
                        {
                            throw new InvalidOperationException($"Sequence contains no elements for {outerMethodCall.Method.Name}.");
                        }
                        result = 0; // For Count, Any, All, return 0 if no rows
                    }

                    // Debug: Log the type of result
                    Console.WriteLine($"Aggregate result type: {result?.GetType()?.FullName ?? "null"}");
                    Console.WriteLine($"Aggregate result value: {result}");

                    if (outerMethodCall?.Method.Name is "Any" or "All")
                    {
                        return (TResult)(object)(Convert.ToInt32(result) == 1);
                    }
                    else if (outerMethodCall?.Method.Name == "Count")
                    {
                        return (TResult)(object)Convert.ToInt32(result);
                    }
                    else if (outerMethodCall?.Method.Name == "Average")
                    {
                        // Average always returns double in LINQ
                        return (TResult)(object)Convert.ToDouble(result);
                    }
                    else if (outerMethodCall?.Method.Name is "Min" or "Max")
                    {
                        // Determine the selector type dynamically
                        var lambda = (LambdaExpression)((UnaryExpression)outerMethodCall.Arguments[1]).Operand;
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
                            throw new NotSupportedException($"Unsupported selector type {selectorType} for {outerMethodCall.Method.Name}.");
                        }
                    }
                }

                string commandText = _selectClause ?? _dataProvider.CreateGetOneOrSelectCommand<T>();
                if (!string.IsNullOrEmpty(whereClause))
                {
                    commandText += "\r\nWHERE " + whereClause;
                }

                if (!string.IsNullOrEmpty(orderByClause) || skip.HasValue || take.HasValue)
                {
                    if (string.IsNullOrEmpty(orderByClause))
                    {
                        orderByClause = "ORDER BY id";
                    }
                    commandText += "\r\n" + orderByClause;
                }

                if (skip.HasValue || take.HasValue)
                {
                    commandText += $"\r\nOFFSET {skip ?? 0} ROWS";
                    if (take.HasValue)
                    {
                        commandText += $"\r\nFETCH NEXT {take.Value} ROWS ONLY";
                    }
                }

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
            finally
            {
                connectionScope.Dispose();
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