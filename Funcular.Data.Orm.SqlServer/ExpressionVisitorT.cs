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
    internal class ExpressionVisitor<T> : ExpressionVisitor where T : class, new()
    {
        private readonly List<SqlParameter> _parameters;
        private readonly ConcurrentDictionary<PropertyInfo, string> _columnNames;
        private readonly ConcurrentDictionary<Type, PropertyInfo[]> _unmappedProperties;
        private readonly StringBuilder _whereClauseBody = new();
        private int _parameterCounter;

        public ExpressionVisitor(List<SqlParameter> parameters,
            ConcurrentDictionary<PropertyInfo, string> columnNames,
            ConcurrentDictionary<Type, PropertyInfo[]> unmappedProperties,
            ref int parameterCounter)
        {
            _parameters = parameters;
            _columnNames = columnNames;
            _unmappedProperties = unmappedProperties;
            _parameterCounter = parameterCounter;
        }

        public string WhereClauseBody => _whereClauseBody.ToString();

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

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression != null && node.Expression.NodeType == ExpressionType.Parameter)
            {
                var member = node.Member as PropertyInfo;
                if (member != null && SqlDataProvider._unmappedPropertiesCache.GetOrAdd(typeof(T), SqlDataProvider.GetUnmappedProperties<T>()).Contains(member) != true)
                {
                    var columnName = _columnNames[member];
                    _whereClauseBody.Append(columnName);
                }
            }
            else
            {
                Visit(node.Expression);
            }
            return node;
        }

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
                        || field.FieldType.IsAssignableFrom(typeof(Guid))
                        || (field.FieldType.Namespace != null && field.FieldType.Namespace.StartsWith("System", StringComparison.OrdinalIgnoreCase))

                       )
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