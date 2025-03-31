using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer
{
    public class SqlQueryProvider<T> : IQueryProvider where T : class, new()
    {
        private readonly FunkySqlDataProvider _dataProvider;
        private readonly string? _selectClause;
        private int _parameterCounter;

        public SqlQueryProvider(FunkySqlDataProvider dataProvider, string? selectClause = null)
        {
            _dataProvider = dataProvider;
            _selectClause = selectClause;
            _parameterCounter = 0;
        }

        public IQueryable CreateQuery(Expression expression)
        {
            return new SqlQueryable<T>(this, expression);
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            return new SqlQueryable<TElement>(this, expression);
        }

        public object Execute(Expression expression)
        {
            return Execute<IEnumerable<T>>(expression);
        }

        /// <summary>
        /// Executes the given LINQ expression and returns the result.
        /// </summary>
        /// <typeparam name="TResult">The type of the result.</typeparam>
        /// <param name="expression">The LINQ expression to execute.</param>
        /// <returns>The result of the executed expression.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the sequence contains no elements for certain operations.</exception>
        /// <exception cref="NotSupportedException">Thrown when an unsupported operation or selector is encountered.</exception>
        public TResult Execute<TResult>(Expression expression)
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

                // Traverse the expression tree to collect components
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
                    else if (currentCall.Method.Name == "OrderBy" || currentCall.Method.Name == "OrderByDescending" ||
                             currentCall.Method.Name == "ThenBy" || currentCall.Method.Name == "ThenByDescending")
                    {
                        var visitor = new OrderByExpressionVisitor<T>(parameters ??= new List<SqlParameter>(),
                            FunkySqlDataProvider._columnNames, FunkySqlDataProvider._unmappedPropertiesCache.GetOrAdd(typeof(T), FunkySqlDataProvider.GetUnmappedProperties<T>),
                            ref _dataProvider._parameterCounter);
                        visitor.Visit(currentCall);
                        orderByClause = visitor.OrderByClause;
                        parameters = visitor.Parameters;
                    }
                    else if (currentCall.Method.Name == "Skip")
                    {
                        skip = (int)((ConstantExpression)currentCall.Arguments[1]).Value;
                    }
                    else if (currentCall.Method.Name == "Take")
                    {
                        take = (int)((ConstantExpression)currentCall.Arguments[1]).Value;
                    }
                    else if (currentCall.Method.Name is "FirstOrDefault" or "First" or "LastOrDefault" or "Last" && currentCall.Arguments.Count == 2)
                    {
                        var lambda = (LambdaExpression)((UnaryExpression)currentCall.Arguments[1]).Operand;
                        var whereExpression = (Expression<Func<T, bool>>)lambda;
                        var elements = _dataProvider.GenerateWhereClause(whereExpression);
                        whereClause = elements.WhereClause;
                        parameters = elements.SqlParameters;

                        if (currentCall.Method.Name is "LastOrDefault" or "Last" && string.IsNullOrEmpty(orderByClause))
                        {
                            var primaryKey = _dataProvider.GetPrimaryKeyCached<T>();
                            var primaryKeyColumn = _dataProvider.GetColumnName(primaryKey);
                            orderByClause = $"ORDER BY {primaryKeyColumn} DESC";
                        }
                    }
                    else if (currentCall.Method.Name is "Any" or "All" or "Count" or "Average" or "Min" or "Max")
                    {
                        HandleAggregateMethod(currentCall, ref whereClause, ref parameters, out aggregateClause, out isAggregate);
                    }
                    currentExpression = currentCall.Arguments[0];
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
                        result = 0;
                    }

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
                        return (TResult)(object)Convert.ToDouble(result);
                    }
                    else if (outerMethodCall?.Method.Name is "Min" or "Max")
                    {
                        var lambda = (LambdaExpression)((UnaryExpression)outerMethodCall.Arguments[1]).Operand;
                        Type selectorType = lambda.Body.Type; // Use the actual return type

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
                    var primaryKey = _dataProvider.GetPrimaryKeyCached<T>();
                    var primaryKeyColumn = _dataProvider.GetColumnName(primaryKey);
                    commandText += "\r\n" + (orderByClause ?? $"ORDER BY {primaryKeyColumn}");
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
        /// Handles aggregation methods (Any, All, Count, Average, Min, Max) by generating the appropriate SQL query.
        /// </summary>
        /// <param name="currentCall">The current method call expression being processed.</param>
        /// <param name="whereClause">The existing WHERE clause, if any.</param>
        /// <param name="parameters">The list of SQL parameters for the query.</param>
        /// <param name="aggregateClause">The generated SQL aggregate clause.</param>
        /// <param name="isAggregate">Flag indicating if the query is an aggregate query.</param>
        /// <exception cref="NotSupportedException">Thrown when the method or selector is not supported.</exception>
        private void HandleAggregateMethod(MethodCallExpression currentCall, ref string? whereClause, ref List<SqlParameter>? parameters, out string? aggregateClause, out bool isAggregate)
        {
            aggregateClause = null;
            isAggregate = false;

            if (currentCall.Method.Name is "Any" or "All" or "Count" && currentCall.Arguments.Count == 2)
            {
                var lambda = (LambdaExpression)((UnaryExpression)currentCall.Arguments[1]).Operand;
                var predicateExpression = (Expression<Func<T, bool>>)lambda;
                var elements = _dataProvider.GenerateWhereClause(predicateExpression);

                if (currentCall.Method.Name == "Any")
                {
                    aggregateClause = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {_dataProvider.GetTableName<T>()} WHERE {elements.WhereClause}";
                    if (!string.IsNullOrEmpty(whereClause))
                    {
                        aggregateClause += $" AND {whereClause}";
                    }
                    aggregateClause += ") THEN 1 ELSE 0 END";
                }
                else if (currentCall.Method.Name == "All")
                {
                    aggregateClause = $@"
                SELECT CASE 
                    WHEN NOT EXISTS (
                        SELECT 1 
                        FROM {_dataProvider.GetTableName<T>()} 
                        WHERE NOT ({elements.WhereClause})
                        {(string.IsNullOrEmpty(whereClause) ? "" : $" AND {whereClause}")}
                    ) THEN 1 
                    ELSE 0 
                END";

                    parameters ??= new List<SqlParameter>();
                    if (elements.SqlParameters?.Any() == true)
                    {
                        // Clone parameters to avoid duplicates
                        var paramList = parameters;
                        var clonedParameters = elements.SqlParameters.Select(p => CloneSqlParameter(p, paramList.Count)).ToList();
                        parameters.AddRange(clonedParameters);
                    }
                }
                else if (currentCall.Method.Name == "Count")
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

                parameters ??= new List<SqlParameter>();
                if (elements.SqlParameters?.Any() == true)
                {
                    var paramList = parameters;
                    parameters.AddRange(elements.SqlParameters.Select(p => CloneSqlParameter(p, paramList.Count)));
                }
                isAggregate = true;
            }
            else if (currentCall.Method.Name == "Count" && currentCall.Arguments.Count == 1)
            {
                aggregateClause = $"SELECT COUNT(*) FROM {_dataProvider.GetTableName<T>()}";
                if (!string.IsNullOrEmpty(whereClause))
                {
                    aggregateClause += "\r\nWHERE " + whereClause;
                }
                isAggregate = true;
            }
            else if (currentCall.Method.Name is "Average" or "Min" or "Max" && currentCall.Arguments.Count == 2)
            {
                var lambda = (LambdaExpression)((UnaryExpression)currentCall.Arguments[1]).Operand;
                string columnExpression;

                // Check if the selector is a simple column (MemberExpression) without nested expressions or nullable unwrapping
                if (lambda.Body is MemberExpression memberExpression && memberExpression.Expression is ParameterExpression)
                {
                    columnExpression = _dataProvider.GetColumnName(memberExpression.Member as PropertyInfo);
                }
                else
                {
                    throw new NotSupportedException($"Aggregate function {currentCall.Method.Name} does not support expression evaluation; aggregates are only supported on column selectors.");
                }

                var aggregateFunction = currentCall.Method.Name.ToUpper() == "AVERAGE" ? "AVG" : currentCall.Method.Name.ToUpper();
                aggregateClause = $"SELECT {aggregateFunction}({columnExpression}) FROM {_dataProvider.GetTableName<T>()}";
                if (!string.IsNullOrEmpty(whereClause))
                {
                    aggregateClause += "\r\nWHERE " + whereClause;
                }
                isAggregate = true;
            }
        }

        private string GetColumnExpression(Expression<Func<T, object>> selectorExpression)
        {
            if (selectorExpression.Body is MemberExpression memberExpression)
            {
                return _dataProvider.GetColumnName(memberExpression.Member as PropertyInfo);
            }
            else if (selectorExpression.Body is UnaryExpression unaryExpression && unaryExpression.Operand is MemberExpression unaryMember)
            {
                return _dataProvider.GetColumnName(unaryMember.Member as PropertyInfo);
            }
            throw new NotSupportedException("Only simple member access selectors are supported for aggregates.");
        }

        private object ConvertToSelectorType(object result, MethodCallExpression methodCall)
        {
            var lambda = (LambdaExpression)((UnaryExpression)methodCall.Arguments[1]).Operand;
            Type selectorType = GetSelectorType(lambda.Body);

            if (selectorType == typeof(DateTime))
                return Convert.ToDateTime(result);
            if (selectorType == typeof(int))
                return Convert.ToInt32(result);
            if (selectorType == typeof(double))
                return Convert.ToDouble(result);
            if (selectorType.CheckTypeIs(typeof(decimal)))
                return Convert.ToDecimal(result);
            throw new NotSupportedException($"Unsupported selector type {selectorType} for {methodCall.Method.Name}.");
        }

        private SqlParameter CloneSqlParameter(SqlParameter original)
        {
            return new SqlParameter
            {
                ParameterName = original.ParameterName,
                Value = original.Value,
                SqlDbType = original.SqlDbType,
                Direction = original.Direction,
                IsNullable = original.IsNullable,
                Precision = original.Precision,
                Scale = original.Scale,
                Size = original.Size
            };
        }

        private SqlParameter CloneSqlParameter(SqlParameter original, int existingParameterCount)
        {
            var newParameter = new SqlParameter
            {
                ParameterName = $"@p__linq__{existingParameterCount + _parameterCounter++}",
                Value = original.Value,
                SqlDbType = original.SqlDbType,
                Direction = original.Direction,
                IsNullable = original.IsNullable,
                Precision = original.Precision,
                Scale = original.Scale,
                Size = original.Size
            };
            return newParameter;
        }

        private Type GetSelectorType(Expression selector)
        {
            return selector switch
            {
                MemberExpression memberExpression => memberExpression.Type,
                UnaryExpression unaryExpression => unaryExpression.Type,
                MethodCallExpression methodCallExpression => methodCallExpression.Method.ReturnType,
                _ => throw new NotSupportedException("Unsupported selector expression type.")
            };
        }
    }

    internal static class Extensions
    {
        public static bool CheckTypeIs(this Type typeToCheck, Type type)
        {
            return typeToCheck == type;
        }
    }
}