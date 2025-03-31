using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer
{
    /// <summary>
    /// Provides a query provider for translating LINQ expressions into SQL queries for a SQL Server database.
    /// This class implements the <see cref="IQueryProvider"/> interface to enable LINQ query execution against
    /// a SQL Server database using the <see cref="FunkySqlDataProvider"/>.
    /// </summary>
    /// <typeparam name="T">The type of entity being queried, which must be a class with a parameterless constructor.</typeparam>
    public class SqlQueryProvider<T> : IQueryProvider where T : class, new()
    {
        private readonly FunkySqlDataProvider _dataProvider;
        private readonly string? _selectClause;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlQueryProvider{T}"/> class.
        /// </summary>
        /// <param name="dataProvider">The <see cref="FunkySqlDataProvider"/> instance used to execute SQL commands.</param>
        /// <param name="selectClause">An optional pre-defined SELECT clause to use for queries. If null, a default SELECT clause is generated.</param>
        public SqlQueryProvider(FunkySqlDataProvider dataProvider, string? selectClause = null)
        {
            _dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
            _selectClause = selectClause;
        }

        /// <summary>
        /// Creates a new <see cref="IQueryable"/> instance for the given expression.
        /// </summary>
        /// <param name="expression">The LINQ expression to be translated into a SQL query.</param>
        /// <returns>An <see cref="IQueryable"/> instance that can be used to execute the query.</returns>
        public IQueryable CreateQuery(Expression expression)
        {
            if (expression == null)
                throw new ArgumentNullException(nameof(expression));
            return new SqlQueryable<T>(this, expression);
        }

        /// <summary>
        /// Creates a new <see cref="IQueryable{TElement}"/> instance for the given expression.
        /// </summary>
        /// <typeparam name="TElement">The type of elements in the query result.</typeparam>
        /// <param name="expression">The LINQ expression to be translated into a SQL query.</param>
        /// <returns>An <see cref="IQueryable{TElement}"/> instance that can be used to execute the query.</returns>
        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            if (expression == null)
                throw new ArgumentNullException(nameof(expression));
            return new SqlQueryable<TElement>(this, expression);
        }

        /// <summary>
        /// Executes the given LINQ expression and returns the result as an object.
        /// This method delegates to the generic <see cref="Execute{TResult}"/> method with a default result type of <see cref="IEnumerable{T}"/>.
        /// </summary>
        /// <param name="expression">The LINQ expression to execute.</param>
        /// <returns>The result of the query execution as an object.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="expression"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the query results in no elements for methods like First or Last when they require a result.</exception>
        /// <exception cref="NotSupportedException">Thrown if the LINQ expression contains unsupported operations.</exception>
        public object Execute(Expression expression)
        {
            if (expression == null)
                throw new ArgumentNullException(nameof(expression));
            return Execute<IEnumerable<T>>(expression);
        }

        /// <summary>
        /// Executes the given LINQ expression and returns the result as the specified type.
        /// This method translates the LINQ expression into a SQL query, executes it using the <see cref="FunkySqlDataProvider"/>,
        /// and returns the result in the requested format.
        /// </summary>
        /// <typeparam name="TResult">The type of the result to return. Can be a single entity, a collection, or an aggregate value.</typeparam>
        /// <param name="expression">The LINQ expression to execute.</param>
        /// <returns>The result of the query execution, cast to <typeparamref name="TResult"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="expression"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the query results in no elements for methods like First, Last, Average, Min, or Max when they require a result.</exception>
        /// <exception cref="NotSupportedException">Thrown if the LINQ expression contains unsupported operations, such as complex selectors in aggregate methods.</exception>
        /// <exception cref="SqlException">Thrown if there is an error executing the SQL query against the database.</exception>
        public TResult Execute<TResult>(Expression expression)
        {
            if (expression == null)
                throw new ArgumentNullException(nameof(expression));

            // Extract query components
            string? whereClause = null;
            List<SqlParameter>? parameters = null;
            string? orderByClause = null;
            int? skip = null;
            int? take = null;
            string? aggregateClause = null;
            bool isAggregate = false;
            MethodCallExpression? outerMethodCall = null;
            WhereClauseElements<T>? elements = null;

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
                        var whereElements = _dataProvider.GenerateWhereClause(whereExpression);
                        whereClause = whereElements.WhereClause;
                        parameters = whereElements.SqlParameters;

                        Debug.WriteLine($"[DEBUG] Where clause: {whereClause}");
                        if (parameters != null)
                        {
                            foreach (var param in parameters)
                            {
                                Debug.WriteLine($"[DEBUG] Parameter: {param.ParameterName} = {param.Value}");
                            }
                        }
                    }
                    else if (currentCall.Method.Name == "Skip")
                    {
                        var skipValue = (int)(((ConstantExpression)currentCall.Arguments[1]).Value ?? throw new InvalidOperationException());
                        skip = skipValue;
                    }
                    else if (currentCall.Method.Name == "Take")
                    {
                        var takeValue = (int)(((ConstantExpression)currentCall.Arguments[1]).Value ?? throw new InvalidOperationException());
                        take = takeValue;
                    }
                    else if (currentCall.Method.Name is "FirstOrDefault" or "First" or "LastOrDefault" or "Last" && currentCall.Arguments.Count == 2)
                    {
                        var lambda = (LambdaExpression)((UnaryExpression)currentCall.Arguments[1]).Operand;
                        var whereExpression = (Expression<Func<T, bool>>)lambda;
                        var whereElements = _dataProvider.GenerateWhereClause(whereExpression);
                        whereClause = whereElements.WhereClause;
                        parameters = whereElements.SqlParameters;

                        Debug.WriteLine($"[DEBUG] Where clause: {whereClause}");
                        foreach (var param in parameters)
                        {
                            Debug.WriteLine($"[DEBUG] Parameter: {param.ParameterName} = {param.Value}");
                        }

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
                    // Handle Any, All, Count with predicates
                    if (methodCall.Method.Name is "Any" or "All" or "Count" && methodCall.Arguments.Count == 2)
                    {
                        var lambda = (LambdaExpression)((UnaryExpression)methodCall.Arguments[1]).Operand;
                        var predicateExpression = (Expression<Func<T, bool>>)lambda;
                        elements = _dataProvider.GenerateWhereClause(predicateExpression);

                        if (methodCall.Method.Name == "Any")
                        {
                            aggregateClause = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {_dataProvider.GetTableName<T>()} WHERE {elements.WhereClause}";
                            if (!string.IsNullOrEmpty(whereClause))
                            {
                                aggregateClause += $" AND {whereClause}";
                            }
                            aggregateClause += ") THEN 1 ELSE 0 END";
                        }
                        else if (methodCall.Method.Name == "All")
                        {
                            aggregateClause = $@"
                        SELECT CASE 
                            WHEN EXISTS (
                                SELECT 1 
                                FROM {_dataProvider.GetTableName<T>()} 
                                WHERE NOT ({elements.WhereClause})
                                {(string.IsNullOrEmpty(whereClause) ? "" : $" AND {whereClause}")}
                            ) THEN 0 
                            ELSE 1 
                        END";
                        }
                        else if (methodCall.Method.Name == "Count")
                        {
                            aggregateClause = $"SELECT COUNT(*) FROM {_dataProvider.GetTableName<T>()}";
                            if (!string.IsNullOrEmpty(whereClause))
                            {
                                aggregateClause += "\r\nWHERE " + whereClause;
                            }
                            if (!string.IsNullOrEmpty(elements.WhereClause))
                            {
                                aggregateClause += string.IsNullOrEmpty(whereClause) ? "\r\nWHERE " : " AND ";
                                aggregateClause += elements.WhereClause;
                            }
                        }

                        parameters = CombineParameters(parameters, elements.SqlParameters, ref whereClause, ref elements);
                        isAggregate = true;
                    }
                    // Handle Count without predicate (e.g., Query<Person>().Count())
                    else if (methodCall.Method.Name == "Count" && methodCall.Arguments.Count == 1)
                    {
                        aggregateClause = $"SELECT COUNT(*) FROM {_dataProvider.GetTableName<T>()}";
                        if (!string.IsNullOrEmpty(whereClause))
                        {
                            aggregateClause += "\r\nWHERE " + whereClause;
                        }
                        isAggregate = true;
                    }
                    // Handle Average, Min, Max with selectors
                    else if (methodCall.Method.Name is "Average" or "Min" or "Max" && methodCall.Arguments.Count == 2)
                    {
                        var lambda = (LambdaExpression)((UnaryExpression)methodCall.Arguments[1]).Operand;

                        // Determine the return type of the selector
                        Type selectorReturnType = lambda.ReturnType;

                        // Only allow simple column names (no function evaluation like YEAR, MONTH, etc.)
                        string columnExpression;
                        if (lambda.Body is MemberExpression memberExpression && memberExpression.Member is PropertyInfo propertyInfo)
                        {
                            columnExpression = _dataProvider.GetColumnName(propertyInfo);
                            Debug.WriteLine($"[DEBUG] Simple member access detected: {memberExpression.Member.Name}, columnExpression = {columnExpression}");
                        }
                        else
                        {
                            throw new NotSupportedException("Aggregate functions (Average, Min, Max) only support simple column names as selectors (e.g., x => x.SomeProperty).");
                        }

                        var aggregateFunction = methodCall.Method.Name switch
                        {
                            "Average" => "AVG",
                            "Min" => "MIN",
                            "Max" => "MAX",
                            _ => throw new NotSupportedException($"Unsupported aggregate function: {methodCall.Method.Name}")
                        };

                        // For AVG, cast the column to FLOAT to ensure decimal precision
                        if (methodCall.Method.Name == "Average")
                        {
                            aggregateClause = $"SELECT {aggregateFunction}(CAST({columnExpression} AS FLOAT)) FROM {_dataProvider.GetTableName<T>()}";
                        }
                        else
                        {
                            aggregateClause = $"SELECT {aggregateFunction}({columnExpression}) FROM {_dataProvider.GetTableName<T>()}";
                        }

                        if (!string.IsNullOrEmpty(whereClause))
                        {
                            aggregateClause += "\r\nWHERE " + whereClause;
                        }
                        Debug.WriteLine($"[DEBUG] Generated aggregateClause: {aggregateClause}");
                        isAggregate = true;
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
                    using var sqlCommand = _dataProvider.BuildCommand(aggregateClause ?? string.Empty, connectionScope.Connection, parameters);
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
                    Debug.WriteLine($"[DEBUG] Aggregate result type: {result?.GetType()?.FullName ?? "null"}");
                    Debug.WriteLine($"[DEBUG] Aggregate result value: {result}");

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
                        Type selectorType = lambda.ReturnType;

                        if (selectorType == typeof(DateTime))
                        {
                            return (TResult)(object)Convert.ToDateTime(result);
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

                string commandText = _selectClause ?? _dataProvider.CreateSelectCommand<T>();
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
        /// Determines the type of a selector expression used in aggregate methods like Min or Max.
        /// </summary>
        /// <param name="selector">The selector expression to analyze.</param>
        /// <returns>The type of the selector's result.</returns>
        /// <exception cref="NotSupportedException">Thrown if the selector expression type is not supported (e.g., not a member access, unary, or method call expression).</exception>
        private Type GetSelectorType(Expression selector)
        {
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

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

        /// <summary>
        /// Creates a new <see cref="SqlParameter"/> by cloning an existing parameter, optionally with a new name.
        /// </summary>
        /// <param name="original">The original <see cref="SqlParameter"/> to clone.</param>
        /// <param name="newParameterName">The new name for the cloned parameter. If null, the original name is used.</param>
        /// <returns>A new <see cref="SqlParameter"/> with the same properties as the original, but with a new name if specified.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="original"/> is null.</exception>
        private static SqlParameter CloneSqlParameter(SqlParameter original, string? newParameterName = null)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));

            return new SqlParameter
            {
                ParameterName = newParameterName ?? original.ParameterName,
                Value = original.Value,
                SqlDbType = original.SqlDbType,
                Direction = original.Direction,
                IsNullable = original.IsNullable,
                Precision = original.Precision,
                Scale = original.Scale,
                Size = original.Size
            };
        }

        /// <summary>
        /// Combines two lists of <see cref="SqlParameter"/> objects, ensuring unique parameter names by renaming duplicates.
        /// Updates the associated WHERE clauses to reflect the new parameter names.
        /// </summary>
        /// <param name="existingParameters">The existing list of parameters, typically from a WHERE clause.</param>
        /// <param name="newParameters">The new list of parameters to add, typically from an aggregate predicate.</param>
        /// <param name="whereClause">The WHERE clause string to update with new parameter names. Passed by reference.</param>
        /// <param name="elements">The <see cref="WhereClauseElements{T}"/> containing the predicate WHERE clause to update. Passed by reference.</param>
        /// <returns>A new list of <see cref="SqlParameter"/> objects with unique names.</returns>
        private List<SqlParameter> CombineParameters(List<SqlParameter>? existingParameters, List<SqlParameter>? newParameters, ref string? whereClause, ref WhereClauseElements<T>? elements)
        {
            var combined = new List<SqlParameter>();
            var usedParameterNames = new HashSet<string>();
            int parameterCounter = 0;

            // Helper to generate a unique parameter name
            string GenerateUniqueParameterName(string baseName)
            {
                string newName;
                do
                {
                    newName = $"@p__linq__{parameterCounter++}";
                } while (usedParameterNames.Contains(newName));
                usedParameterNames.Add(newName);
                return newName;
            }

            // Clone and rename existing parameters (from Where clause)
            if (existingParameters != null && existingParameters.Any())
            {
                foreach (var param in existingParameters)
                {
                    var newName = GenerateUniqueParameterName(param.ParameterName);
                    var clonedParam = CloneSqlParameter(param, newName);

                    // Update the whereClause to use the new parameter name
                    if (whereClause != null)
                    {
                        whereClause = whereClause.Replace(param.ParameterName, newName);
                    }

                    combined.Add(clonedParam);
                }
            }

            // Clone and rename new parameters (from predicate)
            if (newParameters != null && newParameters.Any())
            {
                foreach (var param in newParameters)
                {
                    var newName = GenerateUniqueParameterName(param.ParameterName);
                    var clonedParam = CloneSqlParameter(param, newName);

                    // Update the elements.WhereClause to use the new parameter name
                    if (elements != null)
                    {
                        elements.WhereClause = elements.WhereClause.Replace(param.ParameterName, newName);
                    }

                    combined.Add(clonedParam);
                }
            }

            return combined;
        }
    }
}