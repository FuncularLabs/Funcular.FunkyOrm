using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer
{
    /// <summary>
    /// An expression visitor that translates LINQ expressions into SQL WHERE clauses. This class handles 
    /// the conversion of expressions to SQL syntax, including parameter generation for query safety.
    /// </summary>
    /// <typeparam name="T">The type of entity for which expressions are being translated.</typeparam>
    internal class ExpressionVisitor<T> : ExpressionVisitor where T : class, new()
    {
        /// <summary>
        /// Stores SQL parameters generated from the expressions for use in SQL commands.
        /// </summary>
        protected readonly List<SqlParameter> _parameters;

        /// <summary>
        /// A cache for mapping property names to their corresponding database column names.
        /// </summary>
        protected readonly ConcurrentDictionary<string, string> _columnNames;

        /// <summary>
        /// A cache for properties that should not be mapped to database columns.
        /// </summary>
        protected readonly ConcurrentDictionary<Type, PropertyInfo[]> _unmappedProperties;

        /// <summary>
        /// StringBuilder used to construct the SQL WHERE clause string.
        /// </summary>
        protected readonly StringBuilder _whereClauseBody = new();

        /// <summary>
        /// Counter for generating unique parameter names in SQL queries.
        /// </summary>
        protected int _parameterCounter;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExpressionVisitor{T}"/> class with the necessary dependencies.
        /// </summary>
        /// <param name="parameters">List to hold generated SQL parameters.</param>
        /// <param name="columnNames">Dictionary mapping properties to column names.</param>
        /// <param name="unmappedProperties">Cache of properties not to be included in SQL queries.</param>
        /// <param name="parameterCounter">Reference to a counter for parameter naming.</param>
        public ExpressionVisitor(List<SqlParameter> parameters,
            ConcurrentDictionary<string, string> columnNames,
            ConcurrentDictionary<Type, PropertyInfo[]> unmappedProperties,
            ref int parameterCounter)
        {
            _parameters = parameters;
            _columnNames = columnNames;
            _unmappedProperties = unmappedProperties;
            _parameterCounter = parameterCounter;
        }

        /// <summary>
        /// Gets the constructed WHERE clause as a string for use in SQL queries.
        /// </summary>
        public string WhereClauseBody => _whereClauseBody.ToString();

        /// <summary>
        /// Visits a binary expression node, translating it into corresponding SQL syntax.
        /// </summary>
        /// <param name="node">The binary expression to visit.</param>
        /// <returns>The original expression node after processing.</returns>
        protected override Expression VisitBinary(BinaryExpression node)
        {
            _whereClauseBody.Append('('); Visit(node.Left); bool handledNull = false; switch (node.NodeType)
            {
                case ExpressionType.Equal:
                    if (node.Right is ConstantExpression
                        {
                            Value: null
                        } ||
                        node.Left is ConstantExpression
                        {
                            Value: null
                        })
                    {
                        _whereClauseBody.Append(" IS NULL ");
                        handledNull = true;
                    }
                    else
                    {
                        _whereClauseBody.Append(" = ");
                    }

                    break;
                case ExpressionType.NotEqual:
                    if (node.Right is ConstantExpression
                        {
                            Value: null
                        } ||
                        node.Left is ConstantExpression
                        {
                            Value: null
                        })
                    {
                        _whereClauseBody.Append(" IS NOT NULL ");
                        handledNull = true;
                    }
                    else
                    {
                        _whereClauseBody.Append(" <> ");
                    }

                    break;
                // Other cases…
                case ExpressionType.GreaterThan:
                    _whereClauseBody.Append(" > ");
                    break;
                case ExpressionType.GreaterThanOrEqual:
                    _whereClauseBody.Append(" >= ");
                    break;
                case ExpressionType.LessThan:
                    _whereClauseBody.Append(" < ");
                    break;
                case ExpressionType.LessThanOrEqual:
                    _whereClauseBody.Append(" <= ");
                    break;
                case ExpressionType.AndAlso:
                    _whereClauseBody.Append(" AND ");
                    break;
                case ExpressionType.OrElse:
                    _whereClauseBody.Append(" OR ");
                    break;
                default:
                    throw new NotSupportedException($"Operation {node.NodeType} not supported");
            }

            if (!handledNull)
            {
                Visit(node.Right);
            }

            _whereClauseBody.Append(')');
            return node;
        }

        /*
        /// <summary>
        /// Visits a binary expression node, translating it into corresponding SQL syntax.
        /// </summary>
        /// <param name="node">The binary expression to visit.</param>
        /// <returns>The original expression node after processing.</returns>
        protected override Expression VisitBinary(BinaryExpression node)
        {
            _whereClauseBody.Append('(');
            Visit(node.Left);

            switch (node.NodeType)
            {
                case ExpressionType.Equal:
                    if (node.Right is ConstantExpression { Value: null })
                    {
                        _whereClauseBody.Append(" IS NULL ");
                    }
                    else if (node.Left is ConstantExpression { Value: null })
                    {
                        _whereClauseBody.Append(" IS NULL ");
                    }
                    else
                    {
                        _whereClauseBody.Append(" = ");
                    }
                    break;
                case ExpressionType.NotEqual:
                    if (node.Right is ConstantExpression { Value: null })
                    {
                        _whereClauseBody.Append(" IS NOT NULL ");
                    }
                    else if (node.Left is ConstantExpression { Value: null })
                    {
                        _whereClauseBody.Append(" IS NOT NULL ");
                    }
                    else
                    {
                        _whereClauseBody.Append(" <> ");
                    }
                    break;
                case ExpressionType.GreaterThan:
                    _whereClauseBody.Append(" > ");
                    break;
                case ExpressionType.GreaterThanOrEqual:
                    _whereClauseBody.Append(" >= ");
                    break;
                case ExpressionType.LessThan:
                    _whereClauseBody.Append(" < ");
                    break;
                case ExpressionType.LessThanOrEqual:
                    _whereClauseBody.Append(" <= ");
                    break;
                case ExpressionType.AndAlso:
                    _whereClauseBody.Append(" AND ");
                    break;
                case ExpressionType.OrElse:
                    _whereClauseBody.Append(" OR ");
                    break;
                default:
                    throw new NotSupportedException($"Operation {node.NodeType} not supported");
            }

            Visit(node.Right);
            _whereClauseBody.Append(')');
            return node;
        }*/

        /// <summary>
        /// Processes member expressions, appending the corresponding column name to the SQL clause if it's a property of the entity.
        /// </summary>
        /// <param name="node">The member expression to visit.</param>
        /// <returns>The original member expression after processing.</returns>
        /*protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression is { NodeType: ExpressionType.Parameter })
            {
                var member = node.Member as PropertyInfo;
                if (member != null &&
                    !FunkySqlDataProvider._unmappedPropertiesCache.GetOrAdd(typeof(T),
                            FunkySqlDataProvider.GetUnmappedProperties<T>())
                        .Contains(member))
                {
                    var columnName = _columnNames[member.ToDictionaryKey()];
                    _whereClauseBody.Append(columnName);
                }
            }
            else
            {
                Visit(node.Expression);
            }
            return node;
        }*/

        protected override Expression VisitMember(MemberExpression node)
        {
            // If this is a closure member access, get its value directly.
            if (node.Expression is ConstantExpression constant && node.Member is FieldInfo field)
            {
                var value = field.GetValue(constant.Value);
                var paramName = $"@p__linq__{_parameterCounter++}";
                _whereClauseBody.Append(paramName);
                _parameters.Add(new SqlParameter(paramName,
                    value));
                return node;
            } // Otherwise, if it’s a property of T, handle it as a column name.
        
            if (node.Expression is ParameterExpression)
            {
                var member = node.Member as PropertyInfo;
                if (member != null &&
                    !FunkySqlDataProvider._unmappedPropertiesCache.GetOrAdd(typeof(T),
                            FunkySqlDataProvider.GetUnmappedProperties<T>())
                        .Contains(member))
                {
                    var columnName = _columnNames[member.ToDictionaryKey()];
                    _whereClauseBody.Append(columnName);
                }
            }
            else
            {
                Visit(node.Expression);
            }

            return node;
        }

        /// <summary>
        /// Handles constant expressions by creating SQL parameters, dealing with value types from closures if necessary.
        /// </summary>
        /// <param name="node">The constant expression node to visit.</param>
        /// <returns>The original constant expression after processing.</returns>
        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value == null)
            {
                _whereClauseBody.Append(" NULL ");
                return node;
            }

            if (node.Value is IQueryable)
            {
                return node;
            }

            if (node.Type.IsPrimitive || node.Type == typeof(string) || node.Type == typeof(DateTime) || node.Type == typeof(Guid))
            {
                var paramName = $"@p__linq__{_parameterCounter++}";
                _whereClauseBody.Append(paramName);
                _parameters.Add(new SqlParameter(paramName, node.Value));
                return node;
            }

            // For complex types or closures
            if (node.Value != null && node.Value.GetType().Name.StartsWith("<>c__DisplayClass"))
            {
                var closureFields = node.Value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var field in closureFields)
                {
                    var fieldValue = field.GetValue(node.Value);
                    if (fieldValue != null && IsSupportedSqlType(fieldValue.GetType()))
                    {
                        var paramName = $"@p__linq__{_parameterCounter++}";
                        _whereClauseBody.Append(paramName);
                        _parameters.Add(new SqlParameter(paramName, fieldValue));
                        return node;
                    }
                }
            }

            throw new NotSupportedException($"The constant value of type {node.Value?.GetType().Name ?? "null"} is not supported for direct SQL parameter use.");
        }


        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (HandleStringMethodCalls(node))
                return node;

            if (HandleContainsMethodCall(node))
                return node;

            return base.VisitMethodCall(node);
        }

        /// <summary>
        /// Determines if the method call expression represents a 'Contains' operation and processes it accordingly.
        /// This method checks if 'Contains' is used as an extension method or directly on a collection.
        /// </summary>
        /// <param name="node">The MethodCallExpression to evaluate.</param>
        /// <returns><c>true</c> if the method call was handled as a 'Contains' operation; otherwise, <c>false</c>.</returns>
        protected bool HandleContainsMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name != "Contains") return false;

            // Check if it's an extension method or directly on a collection
            if (node.Object == null && node.Arguments.Count > 1)
            {
                HandleContainsExtensionMethod(node);
                return true;
            }

            if (node.Object != null)
            {
                var collectionType = node.Object.Type;
                if ((collectionType.IsGenericType && collectionType.GetGenericTypeDefinition() == typeof(List<>)) ||
                    collectionType.GetInterfaces().Contains(typeof(IEnumerable)) && node.Arguments.Count == 1)
                {
                    HandleContainsOnCollection(node);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Processes string method calls like StartsWith, EndsWith, and Contains, converting them into SQL LIKE clauses.
        /// </summary>
        /// <param name="node">The MethodCallExpression representing a string operation.</param>
        /// <returns><c>true</c> if the method call was handled as a string operation; otherwise, <c>false</c>.</returns>
        protected bool HandleStringMethodCalls(MethodCallExpression node)
        {
            if (node.Method.DeclaringType != typeof(string)) return false;

            string memberName = node.Method.Name;
            if (node.Object == null || node.Arguments.Count != 1) return false;

            var argument = node.Arguments[0];
            string pattern;

            if (argument is ConstantExpression constantArg)
            {
                pattern = GetPatternFromConstant(constantArg, memberName);
            }
            else if (argument is MemberExpression memberArg)
            {
                pattern = GetPatternFromMember(memberArg, memberName);
            }
            else
            {
                return false; // Unsupported argument type
            }

            Visit(node.Object);
            _whereClauseBody.Append(" LIKE ");
            AppendLikeParameter(pattern);
            return true;
        }

        /// <summary>
        /// Handles the LINQ 'Contains' method when it's used as an extension method, where the collection 
        /// is passed as the first argument. This method constructs an SQL IN clause from the collection.
        /// </summary>
        /// <param name="node">The MethodCallExpression representing the 'Contains' operation where the collection 
        /// is not the object but passed as an argument.</param>
        /// <exception cref="NotSupportedException">Thrown when the collection argument is neither a constant nor 
        /// a member expression, indicating an unsupported collection type for this operation.</exception>
        protected void HandleContainsExtensionMethod(MethodCallExpression node)
        {
            var member = node.Arguments[0]; // The collection is the first argument
            var item = node.Arguments[1];   // The item to check is the second argument

            _whereClauseBody.Append("(");
            Visit(item);
            _whereClauseBody.Append(" IN (");

            if (member is ConstantExpression constantExpr)
            {
                if (constantExpr.Value != null) 
                    HandleConstantCollection(constantExpr.Value);
            }
            else if (member is MemberExpression memberExpr)
                HandleMemberExpressionCollection(memberExpr);
            else
                throw new NotSupportedException("The collection type is not supported for Contains operation.");

            _whereClauseBody.Append("))");
        }

        /// <summary>
        /// Processes a 'Contains' operation on a collection when the method is called either directly on a collection 
        /// or when the collection is passed as an argument. This method translates the operation into an SQL IN clause.
        /// </summary>
        /// <param name="node">The MethodCallExpression representing the 'Contains' operation on a collection.</param>
        /// <exception cref="NotSupportedException">Thrown when the collection is neither a constant expression nor 
        /// a member expression, indicating an unsupported collection type for this operation.</exception>
        protected void HandleContainsOnCollection(MethodCallExpression node)
        {
            var member = node.Object ?? node.Arguments[0];
            var item = node.Object != null ? node.Arguments[0] : node.Arguments[1];

            _whereClauseBody.Append("(");
            Visit(item);
            _whereClauseBody.Append(" IN (");

            if (member is ConstantExpression constantExpr)
            {
                HandleConstantCollection(constantExpr.Value ?? new string[] { });
            }
            else if (member is MemberExpression memberExpr)
            {
                HandleMemberCollection(memberExpr);
            }
            else
            {
                throw new NotSupportedException("The collection type is not supported for Contains operation.");
            }

            _whereClauseBody.Append("))");
        }

        /// <summary>
        /// Manages a collection referenced by a MemberExpression by compiling the expression to retrieve its runtime value 
        /// and then treating it as a constant collection. This method is used when the collection for a Contains operation 
        /// is referenced indirectly through a member expression.
        /// </summary>
        /// <param name="memberExpr">The MemberExpression that points to a collection property or field.</param>
        /// <exception cref="ArgumentException">Thrown when the member expression does not compile to a valid 
        /// <see cref="IEnumerable"/>.</exception>
        protected void HandleMemberCollection(MemberExpression memberExpr)
        {
            var member = Expression.Lambda<Func<IEnumerable>>(Expression.Convert(memberExpr, typeof(IEnumerable))).Compile()();
            HandleConstantCollection(member);
        }

        /// <summary>
        /// Handles the constant collection. If your existing HandleConstantCollection already supports IEnumerable&lt;object&gt;, you can keep it as is:        
        /// </summary>
        /// <param name="collection">The collection.</param>
        protected void HandleConstantCollection(object collection)
        {
            var items = ((IEnumerable)collection).Cast<object>().ToList();
            if (!items.Any())
            {
                _whereClauseBody.Append(" NULL ");
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) _whereClauseBody.Append(", ");
                var paramName = $"@p__linq__{_parameterCounter++}";
                var itemValue = items[i];
                _whereClauseBody.Append(paramName);
                _parameters.Add(new SqlParameter(paramName, itemValue) { SqlDbType = GetSqlDbType(itemValue) });
            }
        }

        /// <summary>
        /// Handles a collection represented by a MemberExpression by compiling it to retrieve its runtime value 
        /// and then treating it as a constant collection for further processing. This method is used when 
        /// the collection in a Contains operation is not directly available as a constant but as a property or 
        /// field of an object.
        /// </summary>
        /// <param name="memberExpr">The MemberExpression that references a collection property or field.</param>
        /// <exception cref="NotSupportedException">Thrown when the member expression does not resolve to 
        /// an <see cref="IEnumerable"/>.</exception>
        protected void HandleMemberExpressionCollection(MemberExpression memberExpr)
        {
            // Here we assume the member expression points to a field or property that can be accessed at runtime
            var member = Expression.Lambda<Func<object>>(Expression.Convert(memberExpr, typeof(object))).Compile()();
            if (member is IEnumerable collection)
            {
                HandleConstantCollection(collection);
            }
            else
            {
                throw new NotSupportedException("The member does not represent an IEnumerable.");
            }
        }

        /// <summary>
        /// Generates a SQL LIKE pattern from a constant expression based on the method being called (StartsWith, EndsWith, Contains).
        /// </summary>
        /// <param name="constantExpr">The ConstantExpression containing the string value for pattern matching.</param>
        /// <param name="method">The name of the string method being called, which determines the pattern format.</param>
        /// <returns>A string formatted for use in a SQL LIKE clause.</returns>
        protected string GetPatternFromConstant(ConstantExpression constantExpr, string method)
        {
            var value = constantExpr.Value?.ToString() ?? string.Empty;
            return method switch
            {
                "StartsWith" => value.EnsureEndsWith("%"),
                "EndsWith" => value.EnsureStartsWith("%"),
                "Contains" => value.EnsureStartsWith("%").EnsureEndsWith("%"),
                _ => string.Empty
            };
        }

        /// <summary>
        /// Generates a SQL LIKE pattern from a member expression by compiling it at runtime to get its value, 
        /// based on the method being called (StartsWith, EndsWith, Contains).
        /// </summary>
        /// <param name="memberExpr">The MemberExpression representing a string property or field.</param>
        /// <param name="method">The name of the string method being called, which determines the pattern format.</param>
        /// <returns>A string formatted for use in a SQL LIKE clause.</returns>
        protected string GetPatternFromMember(MemberExpression memberExpr, string method)
        {
            // This assumes the member expression can be compiled to get its value at runtime
            var memberValue = Expression.Lambda<Func<string>>(Expression.Convert(memberExpr, typeof(string))).Compile()();
            return method switch
            {
                "StartsWith" => memberValue.EnsureEndsWith("%"),
                "EndsWith" => memberValue.EnsureStartsWith("%"),
                "Contains" => memberValue.EnsureStartsWith("%").EnsureEndsWith("%"),
                _ => string.Empty
            };
        }

        /// <summary>
        /// Appends a LIKE parameter to the SQL WHERE clause for string pattern matching operations.
        /// This method constructs a parameter name, appends it to the SQL string, and adds the corresponding 
        /// SQL parameter with the specified pattern.
        /// </summary>
        /// <param name="pattern">The pattern string to use for the LIKE operation in SQL. This should include 
        /// any necessary wildcard characters ('%') for pattern matching.</param>
        protected void AppendLikeParameter(string pattern)
        {
            var paramName = $"@p__linq__{_parameterCounter++}";
            _whereClauseBody.Append(paramName);
            _parameters.Add(new SqlParameter(paramName, pattern) { SqlDbType = SqlDbType.NVarChar });
        }

        protected bool IsSupportedSqlType(Type? type)
        {
            if (type == null)
                return false;

            // Check if the type is directly supported
            if (type.IsPrimitive || type == typeof(string) || type == typeof(DateTime) || type == typeof(Guid) || type == typeof(byte[]))
            {
                return true;
            }

            // Check for nullable types
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                Type? underlyingType = Nullable.GetUnderlyingType(type);
                if (underlyingType != null)
                {
                    return IsSupportedSqlType(underlyingType);
                }
            }

            // Additional checks for other supported types if any
            // For example, if you want to support TimeSpan, Decimal, etc., you would add them here
            // return type == typeof(TimeSpan) || type == typeof(Decimal) || ...;

            return false;
        }

        /// <summary>
        /// Converts a .NET type to its corresponding <see cref="SqlDbType"/>. This method is used to match 
        /// C# types with SQL Server data types for parameter creation.
        /// </summary>
        /// <param name="value">The value whose type needs to be mapped to a SQL type.</param>
        /// <returns>The appropriate <see cref="SqlDbType"/> for the given value.</returns>
        internal static SqlDbType GetSqlDbType(object? value)
        {
            if (value == null)
            {
                return SqlDbType.VarChar; // Or whatever default you prefer for null values
            }
            Type type = value.GetType();

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean: return SqlDbType.Bit;
                case TypeCode.Byte: return SqlDbType.TinyInt;
                case TypeCode.SByte: return SqlDbType.SmallInt;
                case TypeCode.Int16: return SqlDbType.SmallInt;
                case TypeCode.UInt16: return SqlDbType.Int;
                case TypeCode.Int32: return SqlDbType.Int;
                case TypeCode.UInt32: return SqlDbType.BigInt; // UInt32 might need special handling or mapping
                case TypeCode.Int64: return SqlDbType.BigInt;
                case TypeCode.UInt64: return SqlDbType.Decimal; // UInt64 might need special handling or mapping
                case TypeCode.Single: return SqlDbType.Real;
                case TypeCode.Double: return SqlDbType.Float;
                case TypeCode.Decimal: return SqlDbType.Decimal;
                case TypeCode.DateTime: return SqlDbType.DateTime;
                case TypeCode.String: return SqlDbType.NVarChar;
                case TypeCode.Char: return SqlDbType.NChar;
                case TypeCode.Object:
                    // Handle complex types like DateTimeOffset, Guid, etc.
                    if (type == typeof(DateTimeOffset)) return SqlDbType.DateTimeOffset;
                    if (type == typeof(Guid)) return SqlDbType.UniqueIdentifier;
                    if (type == typeof(byte[])) return SqlDbType.VarBinary;
                    if (type == typeof(TimeSpan)) return SqlDbType.Time;
                    // Fall through to default if not handled above
                    break;
            }

            // Default case for any unhandled type
            return SqlDbType.Variant; // or another appropriate default type
        }
    }
}