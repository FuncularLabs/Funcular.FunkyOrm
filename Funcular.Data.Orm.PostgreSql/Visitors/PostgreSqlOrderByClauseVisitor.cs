using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Globalization;

namespace Funcular.Data.Orm.PostgreSql.Visitors
{
    internal struct OrderByClause
    {
        public string ColumnName;
        public bool IsDescending;
    }

    /// <summary>
    /// Visits LINQ expressions to generate SQL ORDER BY clauses from ordering methods.
    /// </summary>
    public class PostgreSqlOrderByClauseVisitor<T> : BaseExpressionVisitor<T> where T : class, new()
    {
        private readonly ICollection<OrderByClause> _orderByClauses = new List<OrderByClause>();

        public string OrderByClause
        {
            get
            {
                if (!_orderByClauses.Any()) return string.Empty;
                var clause = $"ORDER BY {string.Join(", ", _orderByClauses.Select(c => $"{c.ColumnName} {(c.IsDescending ? "DESC" : "ASC")}"))}";
                Console.WriteLine($"Generated ORDER BY clause: {clause}");
                return clause;
            }
        }

        public PostgreSqlOrderByClauseVisitor(
            ConcurrentDictionary<string, string> columnNames,
            ICollection<PropertyInfo> unmappedProperties)
            : base(columnNames, unmappedProperties)
        {
        }

        public override void Visit(Expression expression)
        {
            VisitExpression(expression);
        }

        private void VisitExpression(Expression node)
        {
            switch (node)
            {
                case MethodCallExpression methodCall: VisitMethodCall(methodCall); break;
                case LambdaExpression lambda: Visit(lambda.Body); break;
                default: throw new NotSupportedException($"Expression type {node.NodeType} is not supported for ORDER BY clauses.");
            }
        }

        private void VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == "OrderBy" || node.Method.Name == "OrderByDescending" || node.Method.Name == "ThenBy" || node.Method.Name == "ThenByDescending")
            {
                if (node.Arguments[0] is MethodCallExpression previousCall)
                {
                    if (previousCall.Method.Name == "OrderBy" || previousCall.Method.Name == "OrderByDescending" || previousCall.Method.Name == "ThenBy" || previousCall.Method.Name == "ThenByDescending")
                    {
                        Visit(previousCall);
                    }
                }

                var lambda = (LambdaExpression)((UnaryExpression)node.Arguments[1]).Operand;
                var isDescending = node.Method.Name == "OrderByDescending" || node.Method.Name == "ThenByDescending";
                VisitOrderingExpression(lambda.Body, isDescending);
            }
            else
            {
                throw new NotSupportedException($"Method {node.Method.Name} is not supported in ORDER BY expressions.");
            }
        }

        private void VisitOrderingExpression(Expression expression, bool isDescending)
        {
            if (expression is MemberExpression memberExpression)
            {
                var property = memberExpression.Member as PropertyInfo;
                if (property != null && !IsUnmappedProperty(property))
                {
                    var columnName = GetColumnName(property);
                    _orderByClauses.Add(new OrderByClause { ColumnName = columnName, IsDescending = isDescending });
                    return;
                }
            }
            else if (expression is UnaryExpression unary && unary.Operand is MemberExpression unaryMember)
            {
                var property = unaryMember.Member as PropertyInfo;
                if (property != null && !IsUnmappedProperty(property))
                {
                    var columnName = GetColumnName(property);
                    _orderByClauses.Add(new OrderByClause { ColumnName = columnName, IsDescending = isDescending });
                    return;
                }
            }
            else if (expression is ConditionalExpression conditional)
            {
                var caseSql = BuildCaseExpression(conditional);
                _orderByClauses.Add(new OrderByClause { ColumnName = caseSql, IsDescending = isDescending });
                return;
            }

            throw new NotSupportedException($"Only simple member access or ternary (conditional) expressions are supported in OrderBy expressions. Unsupported expression: {expression}");
        }

        private string BuildCaseExpression(ConditionalExpression conditional)
        {
            string testSql = BuildTestSql(conditional.Test);
            string trueSql = BuildValueSql(conditional.IfTrue);
            string falseSql = BuildValueSql(conditional.IfFalse);
            return $"CASE WHEN {testSql} THEN {trueSql} ELSE {falseSql} END";
        }

        private string BuildTestSql(Expression test)
        {
            switch (test)
            {
                case MemberExpression mem when mem.Member.MemberType == MemberTypes.Property && mem.Member.Name == "HasValue" && mem.Expression is MemberExpression inner:
                    {
                        var prop = inner.Member as PropertyInfo;
                        if (prop != null && !IsUnmappedProperty(prop))
                            return $"{GetColumnName(prop)} IS NOT NULL";
                        break;
                    }
                case BinaryExpression bin:
                    {
                        string leftSql = BuildValueSql(bin.Left);
                        string rightSql = BuildValueSql(bin.Right);
                        switch (bin.NodeType)
                        {
                            case ExpressionType.Equal: return $"{leftSql} = {rightSql}";
                            case ExpressionType.NotEqual: return $"{leftSql} != {rightSql}";
                            case ExpressionType.GreaterThan: return $"{leftSql} > {rightSql}";
                            case ExpressionType.GreaterThanOrEqual: return $"{leftSql} >= {rightSql}";
                            case ExpressionType.LessThan: return $"{leftSql} < {rightSql}";
                            case ExpressionType.LessThanOrEqual: return $"{leftSql} <= {rightSql}";
                            default: throw new NotSupportedException($"Binary operator {bin.NodeType} is not supported in ORDER BY conditional tests.");
                        }
                    }
                default:
                    throw new NotSupportedException($"Unsupported conditional test expression in ORDER BY: {test.NodeType}");
            }
            throw new NotSupportedException($"Unsupported conditional test expression in ORDER BY: {test}");
        }

        private string BuildValueSql(Expression expr)
        {
            switch (expr)
            {
                case MemberExpression memberExpr:
                    {
                        if (memberExpr.Expression is ParameterExpression)
                        {
                            var property = memberExpr.Member as PropertyInfo;
                            if (property != null && !IsUnmappedProperty(property))
                                return GetColumnName(property);
                            throw new NotSupportedException($"Member {memberExpr.Member.Name} is not a mapped property.");
                        }
                        if (memberExpr.Expression is ConstantExpression constExpr)
                        {
                            var value = (memberExpr.Member as FieldInfo)?.GetValue(constExpr.Value);
                            return FormatConstant(value);
                        }
                        if (memberExpr.Member.MemberType == MemberTypes.Property && memberExpr.Member.Name == "Value" && memberExpr.Expression is MemberExpression inner)
                        {
                            if (inner.Expression is ParameterExpression)
                            {
                                var innerProp = inner.Member as PropertyInfo;
                                if (innerProp != null && !IsUnmappedProperty(innerProp))
                                    return GetColumnName(innerProp);
                            }
                        }
                        try
                        {
                            var evaluated = Expression.Lambda(Expression.Convert(memberExpr, typeof(object))).Compile().DynamicInvoke();
                            return FormatConstant(evaluated);
                        }
                        catch
                        {
                            throw new NotSupportedException($"Unsupported member expression in ORDER BY: {memberExpr}");
                        }
                    }
                case ConstantExpression constExpr:
                    return FormatConstant(constExpr.Value);
                case UnaryExpression unary when unary.NodeType == ExpressionType.Convert:
                    return BuildValueSql(unary.Operand);
                default:
                    try
                    {
                        var evaluated = Expression.Lambda(Expression.Convert(expr, typeof(object))).Compile().DynamicInvoke();
                        return FormatConstant(evaluated);
                    }
                    catch
                    {
                        throw new NotSupportedException($"Unsupported expression in ORDER BY branch: {expr.NodeType}");
                    }
            }
        }

        private string FormatConstant(object value)
        {
            if (value == null) return "NULL";
            switch (value)
            {
                case string s: return $"'{s.Replace("'", "''")}'";
                case DateTime dt: return $"'{dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}'";
                case bool b: return b ? "TRUE" : "FALSE";
                case Guid g: return $"'{g}'";
                case byte _: case sbyte _: case short _: case ushort _:
                case int _: case uint _: case long _: case ulong _:
                case float _: case double _: case decimal _:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
                default: return $"'{value?.ToString()?.Replace("'", "''")}'";
            }
        }
    }
}
