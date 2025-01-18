using System;
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
            _whereClauseBody.Append('(');
            Visit(node.Left);

            switch (node.NodeType)
            {
                case ExpressionType.Equal:
                    _whereClauseBody.Append(" = ");
                    break;
                case ExpressionType.NotEqual:
                    _whereClauseBody.Append(" <> ");
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
        }

        /// <summary>
        /// Processes member expressions, appending the corresponding column name to the SQL clause if it's a property of the entity.
        /// </summary>
        /// <param name="node">The member expression to visit.</param>
        /// <returns>The original member expression after processing.</returns>
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression is { NodeType: ExpressionType.Parameter })
            {
                var member = node.Member as PropertyInfo;
                if (member != null &&
                    SqlDataProvider._unmappedPropertiesCache.GetOrAdd(typeof(T),
                            SqlDataProvider.GetUnmappedProperties<T>())
                        .Contains(member) != true)
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
            var paramName = $"@p__linq__{++_parameterCounter}";
            _whereClauseBody.Append(paramName);

            object? actualValue = node.Value;
            if (node.Value != null && node.Value.GetType().Name.StartsWith("<>c__DisplayClass"))
            {
                // Reflect over the closure to find the actual value
                var closureFields = node.Value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var field in closureFields)
                {
                    if (field.FieldType.IsPrimitive
                        || field.FieldType.IsAssignableFrom(typeof(string))
                        || field.FieldType.IsAssignableFrom(typeof(DateTime))
                        || field.FieldType.IsAssignableFrom(typeof(Guid))
                        || (field.FieldType.Namespace != null && field.FieldType.Namespace.StartsWith("System", StringComparison.OrdinalIgnoreCase)))
                    {
                        actualValue = field.GetValue(node.Value);
                        break; // Assuming there's only one value of interest in the closure
                    }
                }
            }

            // If we couldn't find a field, we might need to recompile the expression or use a different strategy
            if (actualValue == node.Value) // If no change in value, try compiling the expression
            {
                var lambda = Expression.Lambda<Func<object>>(Expression.Convert(node, typeof(object)));
                actualValue = lambda.Compile()();
            }

            // Now use actualValue to determine SqlDbType and set SqlParameter.Value
            var sqlParameter = new SqlParameter(paramName, GetSqlDbType(actualValue))
            {
                Value = actualValue
            };

            _parameters.Add(sqlParameter);
            return node;
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