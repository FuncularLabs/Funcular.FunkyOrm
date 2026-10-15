using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Funcular.Data.Orm.Sqlite.Visitors
{
    /// <summary>
    /// Visits LINQ expressions to generate SQL WHERE clauses with parameterized SQLite queries.
    /// </summary>
    /// <typeparam name="T">The type of entity being queried.</typeparam>
    public class SqliteWhereClauseVisitor<T> : BaseExpressionVisitor<T> where T : class, new()
    {
        private readonly StringBuilder _commandTextBuilder = new StringBuilder();
        private readonly List<SqliteParameter> _parameters = new List<SqliteParameter>();
        private readonly SqliteParameterGenerator _parameterGenerator;
        private readonly SqliteExpressionTranslator _translator;
        private readonly string _tableName;
        private readonly Dictionary<string, string> _remotePropertyMap;

        public string WhereClauseBody => _commandTextBuilder.ToString();
        public List<SqliteParameter> Parameters => _parameters;

        public SqliteWhereClauseVisitor(
            ConcurrentDictionary<string, string> columnNames,
            ICollection<PropertyInfo> unmappedProperties,
            SqliteParameterGenerator parameterGenerator,
            SqliteExpressionTranslator translator,
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
                VisitPredicate(lambda.Body);
            }
            else
            {
                VisitExpression(expression);
            }
        }

        /// <summary>
        /// Visits a part of the predicate in a truth position (the body, an operand of <c>&amp;&amp;</c>, <c>||</c> or
        /// <c>!</c>). A part whose truth is known without a row (<see cref="WherePredicateGuard.Fold"/>) is sent as a
        /// literal, so every path answers it as C# does (D-003 as amended by D-128).
        /// </summary>
        private readonly Dictionary<Expression, bool?> _truths = new Dictionary<Expression, bool?>();

        private void VisitPredicate(Expression node)
        {
            var truth = WherePredicateGuard.Fold(node, typeof(T), _truths);
            if (truth == true)
                _commandTextBuilder.Append(Funcular.Data.Orm.Linq.RowIndependentEvaluator.TrueSql);
            else if (truth == false)
                _commandTextBuilder.Append(Funcular.Data.Orm.Linq.RowIndependentEvaluator.FalseSql);
            else
                VisitExpression(node);
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
                VisitPredicate(node.Operand);
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
            else if (needsParentheses)
            {
                // The operands of && and || are truth positions: a part that doesn't read the row is sent as a literal.
                VisitPredicate(node.Left);
                _commandTextBuilder.Append(GetOperator(node.NodeType));
                VisitPredicate(node.Right);
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
                    $"For date parts, use p.{propName}.Value.Year / .Month / .Day which are translated to SQL strftime().");
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

                // A member of the entity with no column: [NotMapped], or a field (3.11.0, FD-01; before, the visitor
                // recursed into the parameter and failed with "Expression type Parameter is not supported").
                throw new NotSupportedException(
                    $"Member '{node.Member.Name}' of the entity isn't mapped to a column, so it can't be used in a WHERE clause: {node}");
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

            // A member of a member (x.D.Year, x.D.Value.Year, x.Name.Length, x.Child.Id, …): the translator has the
            // dialect's function for it, or rejects it by name (3.11.0, FD-01). Before, the visitor recursed into the
            // receiver and silently dropped the outer member (x.Name.Length > 3 became name > 3), on every operation.
            if (node.Expression != null)
            {
                _translator.TranslateMemberOfMember(node, _commandTextBuilder, ColumnFor);
                return;
            }

            throw new NotSupportedException($"Member {node.Member.Name} is not supported. Expression: {node}");
        }

        /// <summary>The column for a property of the row: its remote alias, or its name qualified with the table's.</summary>
        private string ColumnFor(PropertyInfo prop)
        {
            if (_remotePropertyMap != null && _remotePropertyMap.TryGetValue(prop.Name, out string remoteColumn))
                return remoteColumn;
            var col = GetColumnName(prop);
            return !string.IsNullOrEmpty(_tableName) ? $"{_tableName}.{col}" : col;
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
            _translator.TranslateMethodCall(node, _commandTextBuilder, _parameters, ColumnFor);
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
