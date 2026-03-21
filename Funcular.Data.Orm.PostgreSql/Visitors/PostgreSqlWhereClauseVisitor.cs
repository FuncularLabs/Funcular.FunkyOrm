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
    /// Visits LINQ expressions to generate SQL WHERE clauses with parameterized PostgreSQL queries.
    /// </summary>
    /// <typeparam name="T">The type of entity being queried.</typeparam>
    public class PostgreSqlWhereClauseVisitor<T> : BaseExpressionVisitor<T> where T : class, new()
    {
        private readonly StringBuilder _commandTextBuilder = new StringBuilder();
        private readonly List<NpgsqlParameter> _parameters = new List<NpgsqlParameter> { };
        private readonly PostgreSqlParameterGenerator _parameterGenerator;
        private readonly PostgreSqlExpressionTranslator _translator;
        private readonly string _tableName;
        private readonly Dictionary<string, string> _remotePropertyMap;

        public string WhereClauseBody => _commandTextBuilder.ToString();
        public List<NpgsqlParameter> Parameters => _parameters;

        public PostgreSqlWhereClauseVisitor(
            ConcurrentDictionary<string, string> columnNames,
            ICollection<PropertyInfo> unmappedProperties,
            PostgreSqlParameterGenerator parameterGenerator,
            PostgreSqlExpressionTranslator translator,
            string tableName = null,
            Dictionary<string, string> remotePropertyMap = null)
            : base(columnNames, unmappedProperties)
        {
            _parameterGenerator = parameterGenerator ?? throw new ArgumentNullException(nameof(parameterGenerator));
            _translator = translator ?? throw new ArgumentNullException(nameof(translator));
            _tableName = tableName;
            _remotePropertyMap = remotePropertyMap;
        }

        public override void Visit(Expression expression)
        {
            if (expression is LambdaExpression lambda)
            {
                Visit(lambda.Body);
            }
            else
            {
                VisitExpression(expression);
            }
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
                default:
                    throw new NotSupportedException($"Expression type {node.NodeType} is not supported. Expression: {node}");
            }
        }

        private void VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Not)
            {
                _commandTextBuilder.Append("NOT ");
                Visit(node.Operand);
            }
            else if (node.NodeType == ExpressionType.Convert)
            {
                Visit(node.Operand);
            }
            else
            {
                throw new NotSupportedException($"Unary operator {node.NodeType} is not supported. Expression: {node}");
            }
        }

        private void VisitBinary(BinaryExpression node)
        {
            bool needsParentheses = node.NodeType == ExpressionType.AndAlso || node.NodeType == ExpressionType.OrElse;
            bool isNullComparison = (node.Left is ConstantExpression leftConst && leftConst.Value == null) ||
                                    (node.Right is ConstantExpression rightConst && rightConst.Value == null);
            bool isEquality = node.NodeType == ExpressionType.Equal;
            bool isInequality = node.NodeType == ExpressionType.NotEqual;

            if (needsParentheses) _commandTextBuilder.Append("(");

            if (isNullComparison && (isEquality || isInequality))
            {
                Expression nonNullSide = node.Left is ConstantExpression ? node.Right : node.Left;
                Visit(nonNullSide);
                _commandTextBuilder.Append(isEquality ? " IS NULL" : " IS NOT NULL");
            }
            else
            {
                Visit(node.Left);
                _commandTextBuilder.Append(GetOperator(node.NodeType));
                Visit(node.Right);
            }

            if (needsParentheses) _commandTextBuilder.Append(")");
        }

        private void VisitMember(MemberExpression node)
        {
            if (node.Member.Name == "HasValue" &&
                node.Expression is MemberExpression hasValueInner &&
                hasValueInner.Expression is ParameterExpression &&
                Nullable.GetUnderlyingType(hasValueInner.Type) != null)
            {
                var propName = hasValueInner.Member.Name;
                throw new Funcular.Data.Orm.Exceptions.NullableExpressionException(
                    $"Do not use '.HasValue' on nullable property '{propName}' in LINQ expressions. " +
                    $"FunkyORM automatically unwraps nullable types. " +
                    $"Use '{propName} != null' (IS NOT NULL) or '{propName} == null' (IS NULL) instead.");
            }

            if (node.Member.Name == "Value" &&
                node.Expression is MemberExpression valueInner &&
                valueInner.Expression is ParameterExpression &&
                Nullable.GetUnderlyingType(valueInner.Type) != null)
            {
                var propName = valueInner.Member.Name;
                throw new Funcular.Data.Orm.Exceptions.NullableExpressionException(
                    $"Do not use '.Value' on nullable property '{propName}' in LINQ expressions. " +
                    $"FunkyORM automatically unwraps nullable types. " +
                    $"Use '{propName}' directly (e.g., p.{propName} == 5 instead of p.{propName}.Value == 5). " +
                    $"For date parts, use p.{propName}.Value.Year / .Month / .Day which are translated to SQL EXTRACT().");
            }

            if (node.Expression is ParameterExpression)
            {
                var property = node.Member as PropertyInfo;

                if (property != null && _remotePropertyMap != null && _remotePropertyMap.TryGetValue(property.Name, out string remoteColumn))
                {
                    _commandTextBuilder.Append(remoteColumn);
                    return;
                }

                if (property != null && !IsUnmappedProperty(property))
                {
                    var columnName = GetColumnName(property);
                    if (!string.IsNullOrEmpty(_tableName))
                    {
                        columnName = $"{_tableName}.{columnName}";
                    }
                    _commandTextBuilder.Append(columnName);
                    return;
                }
            }

            if (node.Expression is MemberExpression memberExpression &&
                memberExpression.Expression is ParameterExpression &&
                memberExpression.Member.Name == "Value")
            {
                _translator.TranslateDateMember(node, _commandTextBuilder, _parameters, GetColumnName);
                return;
            }

            if (node.Expression is ConstantExpression constantExpression)
            {
                object value;
                if (node.Member is FieldInfo field)
                    value = field.GetValue(constantExpression.Value);
                else if (node.Member is PropertyInfo prop)
                    value = prop.GetValue(constantExpression.Value);
                else
                {
                    try
                    {
                        var lambda = Expression.Lambda<Func<object>>(Expression.Convert(node, typeof(object)));
                        value = lambda.Compile()();
                    }
                    catch
                    {
                        value = constantExpression.Value;
                    }
                }

                var param = _parameterGenerator.CreateParameter(value);
                _parameters.Add(param);
                _commandTextBuilder.Append(param.ParameterName);
                return;
            }

            if ((node.Member.Name == "Year" || node.Member.Name == "Month" || node.Member.Name == "Day") &&
                node.Expression is MemberExpression valueMember && valueMember.Member.Name == "Value" &&
                valueMember.Expression is MemberExpression dateMember && dateMember.Expression is ParameterExpression)
            {
                _translator.TranslateDateMember(node, _commandTextBuilder, _parameters, GetColumnName);
                return;
            }

            if (IsConstantMemberAccess(node))
            {
                object value;
                try
                {
                    var lambda = Expression.Lambda<Func<object>>(Expression.Convert(node, typeof(object)));
                    value = lambda.Compile()();
                }
                catch
                {
                    throw new NotSupportedException($"Could not evaluate expression: {node}");
                }

                var param = _parameterGenerator.CreateParameter(value);
                _parameters.Add(param);
                _commandTextBuilder.Append(param.ParameterName);
                return;
            }

            if (node.Expression != null)
            {
                Visit(node.Expression);
            }
        }

        private bool IsConstantMemberAccess(MemberExpression node)
        {
            var expr = node.Expression;
            while (expr is MemberExpression member)
            {
                expr = member.Expression;
            }
            return expr is ConstantExpression || expr == null;
        }

        private void VisitConstant(ConstantExpression node)
        {
            if (node.Value == null)
            {
                _commandTextBuilder.Append("NULL");
            }
            else
            {
                var param = _parameterGenerator.CreateParameter(node.Value);
                _parameters.Add(param);
                _commandTextBuilder.Append(param.ParameterName);
            }
        }

        private void VisitNew(NewExpression node)
        {
            var arguments = node.Arguments;
            if (arguments.Count == 0) { _commandTextBuilder.Append("1=1"); return; }
            _commandTextBuilder.Append("(");
            for (int i = 0; i < arguments.Count; i++)
            {
                Visit(arguments[i]);
                if (i < arguments.Count - 1) _commandTextBuilder.Append(", ");
            }
            _commandTextBuilder.Append(")");
        }

        private void VisitMethodCall(MethodCallExpression node)
        {
            _translator.TranslateMethodCall(node, _commandTextBuilder, _parameters, prop =>
            {
                var col = GetColumnName(prop);
                return !string.IsNullOrEmpty(_tableName) ? $"{_tableName}.{col}" : col;
            });
        }

        private void VisitConditional(ConditionalExpression node)
        {
            _commandTextBuilder.Append(" CASE WHEN ");
            Visit(node.Test);
            _commandTextBuilder.Append(" THEN ");
            Visit(node.IfTrue);
            _commandTextBuilder.Append(" ELSE ");
            Visit(node.IfFalse);
            _commandTextBuilder.Append(" END");
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
