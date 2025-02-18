using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer
{
    /// <summary>
    /// Translates LINQ expressions into SQL WHERE clauses for entity type T.
    /// </summary>
    /// <typeparam name="T">Entity type with parameterless constructor.</typeparam>
    internal class ExpressionVisitor<T> : ExpressionVisitor where T : class, new()
    {
        #region Fields

        private readonly List<SqlParameter> _parameters;
        private readonly ConcurrentDictionary<string, string> _columnNames;
        private readonly ConcurrentDictionary<Type, ImmutableArray<PropertyInfo>> _unmappedProperties;
        private readonly StringBuilder _whereClauseBody = new();
        private int _parameterCounter;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the SQL parameters generated during expression translation.
        /// </summary>
        public List<SqlParameter> Parameters => _parameters;

        /// <summary>
        /// Gets the constructed SQL WHERE clause.
        /// </summary>
        public string WhereClauseBody => _whereClauseBody.ToString();

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new expression visitor instance.
        /// </summary>
        /// <param name="parameters">List for storing SQL parameters.</param>
        /// <param name="columnNames">Cache of property to column name mappings.</param>
        /// <param name="unmappedProperties">Cache of unmapped properties.</param>
        /// <param name="parameterCounter">Reference counter for parameter naming.</param>
        public ExpressionVisitor(List<SqlParameter> parameters,
            ConcurrentDictionary<string, string> columnNames,
            ConcurrentDictionary<Type, ImmutableArray<PropertyInfo>> unmappedProperties,
            ref int parameterCounter)
        {
            _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            _columnNames = columnNames ?? throw new ArgumentNullException(nameof(columnNames));
            _unmappedProperties = unmappedProperties ?? throw new ArgumentNullException(nameof(unmappedProperties));
            _parameterCounter = parameterCounter;
        }

        #endregion

        #region Expression Visiting Methods

        /// <summary>
        /// Translates binary expressions into SQL operators.
        /// </summary>
        /// <param name="node">Binary expression to process.</param>
        /// <returns>The processed expression.</returns>
        protected override Expression VisitBinary(BinaryExpression node)
        {
            _whereClauseBody.Append("(");
            Visit(node.Left);

            AppendBinaryOperator(node);
            if (!HandleNullComparison(node))
                Visit(node.Right);

            _whereClauseBody.Append(")");
            return node;
        }

        /// <summary>
        /// Processes member expressions into column names or values.
        /// </summary>
        /// <param name="node">Member expression to process.</param>
        /// <returns>The processed expression.</returns>
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression is ParameterExpression)
            {
                HandleEntityProperty(node);
            }
            else if (node.Expression is ConstantExpression constant && node.Member is FieldInfo field)
            {
                HandleClosureField(constant, field);
            }
            else
            {
                Visit(node.Expression);
            }
            return node;
        }

        /// <summary>
        /// Converts constant expressions into SQL parameters.
        /// </summary>
        /// <param name="node">Constant expression to process.</param>
        /// <returns>The processed expression.</returns>
        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value == null)
            {
                _whereClauseBody.Append("NULL");
                return node;
            }

            if (node.Value is IQueryable)
                return node;

            if (IsSupportedSqlType(node.Type))
            {
                AppendParameter(node.Value);
                return node;
            }

            throw new NotSupportedException($"Unsupported constant type: {node.Value?.GetType().Name}");
        }

        /// <summary>
        /// Handles method call expressions like Contains or string operations.
        /// </summary>
        /// <param name="node">Method call expression to process.</param>
        /// <returns>The processed expression.</returns>
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (TryHandleStringMethod(node) || TryHandleContainsMethod(node))
                return node;

            return base.VisitMethodCall(node);
        }

        #endregion

        #region Private Helper Methods

        private void AppendBinaryOperator(BinaryExpression node)
        {
            switch (node.NodeType)
            {
                case ExpressionType.Equal: _whereClauseBody.Append(" = "); break;
                case ExpressionType.NotEqual: _whereClauseBody.Append(" <> "); break;
                case ExpressionType.GreaterThan: _whereClauseBody.Append(" > "); break;
                case ExpressionType.GreaterThanOrEqual: _whereClauseBody.Append(" >= "); break;
                case ExpressionType.LessThan: _whereClauseBody.Append(" < "); break;
                case ExpressionType.LessThanOrEqual: _whereClauseBody.Append(" <= "); break;
                case ExpressionType.AndAlso: _whereClauseBody.Append(" AND "); break;
                case ExpressionType.OrElse: _whereClauseBody.Append(" OR "); break;
                default: throw new NotSupportedException($"Binary operator {node.NodeType} not supported.");
            }
        }

        private bool HandleNullComparison(BinaryExpression node)
        {
            if ((node.NodeType == ExpressionType.Equal || node.NodeType == ExpressionType.NotEqual) &&
                (node.Right is ConstantExpression { Value: null } || node.Left is ConstantExpression { Value: null }))
            {
                _whereClauseBody.Replace(node.NodeType == ExpressionType.Equal ? " = " : " <> ",
                    node.NodeType == ExpressionType.Equal ? " IS NULL " : " IS NOT NULL ");
                return true;
            }
            return false;
        }

        private void HandleEntityProperty(MemberExpression node)
        {
            if (node.Member is PropertyInfo property &&
                !_unmappedProperties.GetOrAdd(typeof(T), FunkySqlDataProvider.GetUnmappedProperties<T>(typeof(T)))
                    .Contains(property))
            {
                _whereClauseBody.Append(_columnNames.GetOrAdd(property.ToDictionaryKey(),
                    property.GetCustomAttribute<ColumnAttribute>()?.Name ?? property.Name.ToLower()));
            }
        }

        private void HandleClosureField(ConstantExpression constant, FieldInfo field)
        {
            var value = field.GetValue(constant.Value);
            AppendParameter(value);
        }

        private bool TryHandleStringMethod(MethodCallExpression node)
        {
            if (node.Method.DeclaringType != typeof(string) || node.Object == null || node.Arguments.Count != 1)
                return false;

            Visit(node.Object);
            _whereClauseBody.Append(" LIKE ");
            var pattern = GetPattern(node.Method.Name, node.Arguments[0]);
            AppendLikeParameter(pattern);
            return true;
        }

        private bool TryHandleContainsMethod(MethodCallExpression node)
        {
            if (node.Method.Name != "Contains") return false;

            if (node.Object == null && node.Arguments.Count > 1)
            {
                HandleContainsExtension(node);
                return true;
            }

            if (node.Object != null && IsEnumerableType(node.Object.Type))
            {
                HandleContainsOnCollection(node);
                return true;
            }

            return false;
        }

        private void HandleContainsExtension(MethodCallExpression node)
        {
            _whereClauseBody.Append("(");
            Visit(node.Arguments[1]); // Item to check
            _whereClauseBody.Append(" IN (");
            ProcessCollection(node.Arguments[0]);
            _whereClauseBody.Append("))");
        }

        private void HandleContainsOnCollection(MethodCallExpression node)
        {
            _whereClauseBody.Append("(");
            Visit(node.Arguments[0]); // Item to check
            _whereClauseBody.Append(" IN (");
            ProcessCollection(node.Object!);
            _whereClauseBody.Append("))");
        }

        private void ProcessCollection(Expression collectionExpr)
        {
            if (collectionExpr is ConstantExpression constant)
            {
                HandleConstantCollection(constant.Value ?? Array.Empty<object>());
            }
            else if (collectionExpr is MemberExpression member)
            {
                var value = Expression.Lambda<Func<object>>(Expression.Convert(member, typeof(object))).Compile()();
                HandleConstantCollection(value as IEnumerable ?? throw new NotSupportedException("Invalid collection type."));
            }
            else
            {
                throw new NotSupportedException("Unsupported collection expression type.");
            }
        }

        private void HandleConstantCollection(object collection)
        {
            var items = ((IEnumerable)collection).Cast<object>().ToList();
            if (!items.Any())
            {
                _whereClauseBody.Append("NULL");
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) _whereClauseBody.Append(", ");
                AppendParameter(items[i]);
            }
        }

        private string GetPattern(string method, Expression argument)
        {
            var value = argument switch
            {
                ConstantExpression c => c.Value?.ToString() ?? string.Empty,
                MemberExpression m => Expression.Lambda<Func<string>>(m).Compile()(),
                _ => throw new NotSupportedException("Unsupported argument type for string method.")
            };

            return method switch
            {
                "StartsWith" => $"{value}%",
                "EndsWith" => $"%{value}",
                "Contains" => $"%{value}%",
                _ => string.Empty
            };
        }

        private void AppendParameter(object? value)
        {
            var paramName = $"@p__linq__{_parameterCounter++}";
            _whereClauseBody.Append(paramName);
            _parameters.Add(new SqlParameter(paramName, value ?? DBNull.Value)
            {
                SqlDbType = GetSqlDbType(value)
            });
        }

        private void AppendLikeParameter(string pattern)
        {
            var paramName = $"@p__linq__{_parameterCounter++}";
            _whereClauseBody.Append(paramName);
            _parameters.Add(new SqlParameter(paramName, pattern) { SqlDbType = SqlDbType.NVarChar });
        }

        private bool IsEnumerableType(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) ||
            type.GetInterfaces().Contains(typeof(IEnumerable));

        private bool IsSupportedSqlType(Type type) =>
            type.IsPrimitive || type == typeof(string) || type == typeof(DateTime) ||
            type == typeof(Guid) || type == typeof(byte[]);

        #endregion

        #region Static Helpers

        /// <summary>
        /// Maps .NET types to SQL Server data types.
        /// </summary>
        /// <param name="value">Value to map.</param>
        /// <returns>Corresponding SqlDbType.</returns>
        internal static SqlDbType GetSqlDbType(object? value)
        {
            if (value == null) return SqlDbType.NVarChar;

            var type = value.GetType();
            return Type.GetTypeCode(type) switch
            {
                TypeCode.Boolean => SqlDbType.Bit,
                TypeCode.Byte => SqlDbType.TinyInt,
                TypeCode.Int16 => SqlDbType.SmallInt,
                TypeCode.Int32 => SqlDbType.Int,
                TypeCode.Int64 => SqlDbType.BigInt,
                TypeCode.Single => SqlDbType.Real,
                TypeCode.Double => SqlDbType.Float,
                TypeCode.Decimal => SqlDbType.Decimal,
                TypeCode.DateTime => SqlDbType.DateTime,
                TypeCode.String => SqlDbType.NVarChar,
                _ => type switch
                {
                    _ when type == typeof(Guid) => SqlDbType.UniqueIdentifier,
                    _ when type == typeof(byte[]) => SqlDbType.VarBinary,
                    _ when type == typeof(TimeSpan) => SqlDbType.Time,
                    _ => SqlDbType.Variant
                }
            };
        }

        #endregion
    }
}