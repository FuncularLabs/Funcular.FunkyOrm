using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Funcular.Data.Orm.Attributes;
using Microsoft.Data.Sqlite;

namespace Funcular.Data.Orm.Sqlite.Visitors
{
    /// <summary>
    /// Visits LINQ expressions to generate SQL SELECT clauses with projections for SQLite.
    /// </summary>
    public class SqliteSelectClauseVisitor<T> : BaseExpressionVisitor<T> where T : class, new()
    {
        private readonly StringBuilder _selectBuilder = new StringBuilder();
        private readonly List<SqliteParameter> _parameters = new List<SqliteParameter>();
        private readonly SqliteParameterGenerator _parameterGenerator;
        private readonly SqliteExpressionTranslator _translator;
        private readonly string _tableName;
        private readonly IReadOnlyDictionary<string, string> _propertyToColumnMap;

        public string SelectClause => _selectBuilder.ToString();
        public List<SqliteParameter> Parameters => _parameters;

        public SqliteSelectClauseVisitor(
            ConcurrentDictionary<string, string> columnNames,
            ICollection<PropertyInfo> unmappedProperties,
            SqliteParameterGenerator parameterGenerator,
            SqliteExpressionTranslator translator,
            string tableName = null,
            IReadOnlyDictionary<string, string> propertyToColumnMap = null)
            : base(columnNames, unmappedProperties)
        {
            _parameterGenerator = parameterGenerator ?? throw new ArgumentNullException(nameof(parameterGenerator));
            _translator = translator ?? throw new ArgumentNullException(nameof(translator));
            _tableName = tableName;
            _propertyToColumnMap = propertyToColumnMap;
        }

        public override void Visit(Expression expression)
        {
            if (expression is LambdaExpression lambda) Visit(lambda.Body);
            else VisitExpression(expression);
        }

        private void VisitExpression(Expression node)
        {
            switch (node)
            {
                case UnaryExpression unary: VisitUnary(unary); break;
                case BinaryExpression binary: VisitBinary(binary); break;
                case MemberExpression member: VisitMember(member); break;
                case ConstantExpression constant: VisitConstant(constant); break;
                case MethodCallExpression methodCall: VisitMethodCall(methodCall); break;
                case NewExpression newExpr: VisitNew(newExpr); break;
                case ConditionalExpression conditional: VisitConditional(conditional); break;
                case MemberInitExpression memberInit: VisitMemberInit(memberInit); break;
                default: throw new NotSupportedException($"Expression type {node.NodeType} is not supported in SELECT.");
            }
        }

        private void VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Not) { _selectBuilder.Append("NOT "); Visit(node.Operand); }
            else if (node.NodeType == ExpressionType.Convert) { Visit(node.Operand); }
            else throw new NotSupportedException($"Unary operator {node.NodeType} is not supported in SELECT.");
        }

        private void VisitBinary(BinaryExpression node)
        {
            // Handle IS NULL / IS NOT NULL for null comparisons
            if ((node.NodeType == ExpressionType.Equal || node.NodeType == ExpressionType.NotEqual) && IsNullConstant(node.Right))
            {
                Visit(node.Left);
                _selectBuilder.Append(node.NodeType == ExpressionType.Equal ? " IS NULL" : " IS NOT NULL");
                return;
            }
            if ((node.NodeType == ExpressionType.Equal || node.NodeType == ExpressionType.NotEqual) && IsNullConstant(node.Left))
            {
                Visit(node.Right);
                _selectBuilder.Append(node.NodeType == ExpressionType.Equal ? " IS NULL" : " IS NOT NULL");
                return;
            }
            Visit(node.Left);
            _selectBuilder.Append(GetOperator(node.NodeType));
            Visit(node.Right);
        }

        private static bool IsNullConstant(Expression expr)
        {
            if (expr is ConstantExpression c && c.Value == null) return true;
            if (expr is UnaryExpression u && u.NodeType == ExpressionType.Convert)
                return IsNullConstant(u.Operand);
            return false;
        }

        private void VisitMember(MemberExpression node)
        {
            if (node.Expression is ParameterExpression)
            {
                var property = node.Member as PropertyInfo;
                if (property != null && !IsUnmappedProperty(property))
                {
                    var columnName = GetColumnName(property);
                    if (!string.IsNullOrEmpty(_tableName)) columnName = $"{_tableName}.{columnName}";
                    _selectBuilder.Append(columnName);
                    return;
                }
                if (property != null)
                {
                    // Unmapped property in a custom projection. A [RemoteProperty]/[RemoteKey]
                    // requires a join and cannot be projected directly. Self-contained computed
                    // attributes ([JsonPath]/[SqlExpression]/[SubqueryAggregate]) resolve via the map.
                    if (property.GetCustomAttribute<RemoteAttributeBase>() != null)
                        throw new NotSupportedException($"A [RemoteProperty]/[RemoteKey] ('{property.Name}') cannot be projected in a custom Select(...); it requires a join. Query the whole entity, or use a detail class that declares it.");

                    if (_propertyToColumnMap != null && _propertyToColumnMap.TryGetValue(property.Name, out var resolved))
                    {
                        _selectBuilder.Append(resolved);
                        return;
                    }
                }
            }
            if (node.Expression is ConstantExpression constantExpression)
            {
                object value;
                if (node.Member is FieldInfo field) value = field.GetValue(constantExpression.Value);
                else if (node.Member is PropertyInfo prop) value = prop.GetValue(constantExpression.Value);
                else value = constantExpression.Value;
                var param = _parameterGenerator.CreateParameter(value);
                _parameters.Add(param);
                _selectBuilder.Append(param.ParameterName);
                return;
            }
            if (node.Expression != null) Visit(node.Expression);
        }

        private void VisitConstant(ConstantExpression node)
        {
            if (node.Value == null) { _selectBuilder.Append(" NULL "); }
            else if (node.Value is bool b) { _selectBuilder.Append(b ? " 1 " : " 0 "); }
            else
            {
                var param = _parameterGenerator.CreateParameter(node.Value);
                _parameters.Add(param);
                _selectBuilder.Append(param.ParameterName);
            }
        }

        private void VisitNew(NewExpression node) { }

        private void VisitMemberInit(MemberInitExpression node)
        {
            var bindings = node.Bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding is MemberAssignment assignment)
                {
                    Visit(assignment.Expression);
                    _selectBuilder.Append($" AS {assignment.Member.Name}");
                    if (i < bindings.Count - 1) _selectBuilder.Append(", ");
                }
            }
        }

        private void VisitMethodCall(MethodCallExpression node)
        {
            _translator.TranslateMethodCall(node, _selectBuilder, _parameters, GetColumnName);
        }

        private void VisitConditional(ConditionalExpression node)
        {
            _selectBuilder.Append(" CASE WHEN ");
            Visit(node.Test);
            _selectBuilder.Append(" THEN ");
            Visit(node.IfTrue);
            _selectBuilder.Append(" ELSE ");
            Visit(node.IfFalse);
            _selectBuilder.Append(" END");
        }

        private string GetOperator(ExpressionType nodeType)
        {
            switch (nodeType)
            {
                case ExpressionType.Equal: return " = ";
                case ExpressionType.NotEqual: return " != ";
                case ExpressionType.GreaterThan: return " > ";
                case ExpressionType.GreaterThanOrEqual: return " >= ";
                case ExpressionType.LessThan: return " < ";
                case ExpressionType.LessThanOrEqual: return " <= ";
                case ExpressionType.AndAlso: return " AND ";
                case ExpressionType.OrElse: return " OR ";
                default: throw new NotSupportedException($"Operator {nodeType} is not supported.");
            }
        }
    }
}
