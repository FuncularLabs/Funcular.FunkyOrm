using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Visitors
{
    /// <summary>
    /// Visits LINQ expressions to generate SQL SELECT clauses with projections for PostgreSQL.
    /// </summary>
    public class PostgreSqlSelectClauseVisitor<T> : BaseExpressionVisitor<T> where T : class, new()
    {
        private readonly StringBuilder _selectBuilder = new StringBuilder();
        private readonly List<NpgsqlParameter> _parameters = new List<NpgsqlParameter> { };
        private readonly PostgreSqlParameterGenerator _parameterGenerator;
        private readonly PostgreSqlExpressionTranslator _translator;
        private readonly string _tableName;

        public string SelectClause => _selectBuilder.ToString();
        public List<NpgsqlParameter> Parameters => _parameters;

        public PostgreSqlSelectClauseVisitor(
            ConcurrentDictionary<string, string> columnNames,
            ICollection<PropertyInfo> unmappedProperties,
            PostgreSqlParameterGenerator parameterGenerator,
            PostgreSqlExpressionTranslator translator,
            string tableName = null)
            : base(columnNames, unmappedProperties)
        {
            _parameterGenerator = parameterGenerator ?? throw new ArgumentNullException(nameof(parameterGenerator));
            _translator = translator ?? throw new ArgumentNullException(nameof(translator));
            _tableName = tableName;
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
            Visit(node.Left);
            _selectBuilder.Append(GetOperator(node.NodeType));
            Visit(node.Right);
        }

        private void VisitMember(MemberExpression node)
        {
            if (node.Member.MemberType == System.Reflection.MemberTypes.Property && node.Member.Name == "HasValue"
                && node.Expression is MemberExpression innerMember)
            {
                var property = innerMember.Member as PropertyInfo;
                if (property != null && !IsUnmappedProperty(property))
                {
                    _selectBuilder.Append($"{GetColumnName(property)} IS NOT NULL");
                    return;
                }
            }
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
                if (property != null && IsUnmappedProperty(property))
                    throw new NotSupportedException("Unmapped properties cannot be selected directly.");
            }
            if (node.Expression is ConstantExpression constantExpression)
            {
                object value;
                if (node.Member is FieldInfo field) value = field.GetValue(constantExpression.Value);
                else if (node.Member is PropertyInfo prop) value = prop.GetValue(constantExpression.Value);
                else
                {
                    try { var lambda = Expression.Lambda<Func<object>>(Expression.Convert(node, typeof(object))); value = lambda.Compile()(); }
                    catch { value = constantExpression.Value; }
                }
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
            else if (node.Value is bool b)
            {
                // PostgreSQL uses native BOOLEAN
                _selectBuilder.Append(b ? " TRUE " : " FALSE ");
            }
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
                    var property = assignment.Member as PropertyInfo;
                    if (property != null && property.DeclaringType == typeof(T) && !IsUnmappedProperty(property))
                    {
                        if (!(assignment.Expression is MemberExpression memberExpr && memberExpr.Member == property && memberExpr.Expression is ParameterExpression))
                            throw new InvalidOperationException("Cannot assign computed values to mapped properties in SELECT projections.");
                    }
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
