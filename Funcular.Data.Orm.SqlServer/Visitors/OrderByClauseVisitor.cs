using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Globalization;

namespace Funcular.Data.Orm.Visitors
{
    internal struct OrderByClause
    {
        public string ColumnName;
        public bool IsDescending;
    }

    /// <summary>
    /// Visits LINQ expressions to generate SQL ORDER BY clauses from ordering methods.
    /// </summary>
    /// <typeparam name="T">The type of entity being queried.</typeparam>
    public class OrderByClauseVisitor<T> : BaseExpressionVisitor<T> where T : class, new()
    {
        private readonly ICollection<OrderByClause> _orderByClauses = new List<OrderByClause>();

        /// <summary>
        /// Gets the generated ORDER BY clause.
        /// </summary>
        public string OrderByClause
        {
            get
            {
                if (!_orderByClauses.Any())
                    return string.Empty;

                var clause =
                    $"ORDER BY {string.Join(", ", _orderByClauses.Select(c => $"{c.ColumnName} {(c.IsDescending ? "DESC" : "ASC")}"))}";
                Console.WriteLine($"Generated ORDER BY clause: {clause}");
                return clause;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OrderByClauseVisitor{T}"/> class.
        /// </summary>
        /// <param name="columnNames">Cached column name mappings from property keys to SQL column names.</param>
        /// <param name="unmappedProperties">Cached unmapped properties (marked with NotMappedAttribute).</param>
        public OrderByClauseVisitor(
            ConcurrentDictionary<string, string> columnNames,
            ICollection<PropertyInfo> unmappedProperties)
            : base(columnNames, unmappedProperties)
        {
        }

        /// <summary>
        /// Visits the specified expression to generate an ORDER BY clause.
        /// </summary>
        /// <param name="expression">The LINQ expression to visit.</param>
        public override void Visit(Expression expression)
        {
            VisitExpression(expression);
        }

        /// <summary>
        /// Visits an expression and dispatches to the appropriate method based on its type.
        /// </summary>
        private void VisitExpression(Expression node)
        {
            switch (node)
            {
                case MethodCallExpression methodCall:
                    VisitMethodCall(methodCall);
                    break;
                case LambdaExpression lambda:
                    Visit(lambda.Body);
                    break;
                default:
                    throw new NotSupportedException($"Expression type {node.NodeType} is not supported for ORDER BY clauses.");
            }
        }

        /// <summary>
        /// Visits a method call expression to process OrderBy/ThenBy methods.
        /// </summary>
        private void VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == "OrderBy" || node.Method.Name == "OrderByDescending" || node.Method.Name == "ThenBy" || node.Method.Name == "ThenByDescending")
            {
                // First, traverse to the beginning of the chain to process in correct order
                if (node.Arguments[0] is MethodCallExpression previousCall)
                {
                    // Only continue if the previous call is also an ordering method
                    if (previousCall.Method.Name == "OrderBy" || previousCall.Method.Name == "OrderByDescending" || previousCall.Method.Name == "ThenBy" || previousCall.Method.Name == "ThenByDescending")
                    {
                        Visit(previousCall);
                    }
                    // If it's not an ordering method (like Where), we stop here as we've processed all ordering operations
                }

                // Then process the current method call
                var lambda = (LambdaExpression)((UnaryExpression)node.Arguments[1]).Operand;
                var isDescending = node.Method.Name == "OrderByDescending" || node.Method.Name == "ThenByDescending";
                VisitOrderingExpression(lambda.Body, isDescending);
            }
            else
            {
                throw new NotSupportedException($"Method {node.Method.Name} is not supported in ORDER BY expressions.");
            }
        }

        /// <summary>
        /// Visits an ordering expression to extract the column name and direction.
        /// </summary>
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
                // e.g., conversions: p => (object)p.SomeProp; handle underlying member
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
                // Build a CASE WHEN ... THEN ... ELSE ... END expression for ORDER BY
                var caseSql = BuildCaseExpression(conditional);
                _orderByClauses.Add(new OrderByClause { ColumnName = caseSql, IsDescending = isDescending });
                return;
            }

            throw new NotSupportedException($"Only simple member access or ternary (conditional) expressions are supported in OrderBy expressions. Unsupported expression: {expression}");
        }

        /// <summary>
        /// Builds a CASE WHEN ... THEN ... ELSE ... END SQL fragment from a ConditionalExpression.
        /// Supports simple tests: member.HasValue and binary comparisons like member == constant.
        /// Supports branches that are member accesses or constants.
        /// </summary>
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
                        {
                            var col = GetColumnName(prop);
                            return $"{col} IS NOT NULL";
                        }
                        break;
                    }
                case BinaryExpression bin:
                    {
                        var left = bin.Left;
                        var right = bin.Right;

                        string leftSql = BuildValueSql(left);
                        string rightSql = BuildValueSql(right);

                        switch (bin.NodeType)
                        {
                            case ExpressionType.Equal:
                                return $"{leftSql} = {rightSql}";
                            case ExpressionType.NotEqual:
                                return $"{leftSql} != {rightSql}";
                            case ExpressionType.GreaterThan:
                                return $"{leftSql} > {rightSql}";
                            case ExpressionType.GreaterThanOrEqual:
                                return $"{leftSql} >= {rightSql}";
                            case ExpressionType.LessThan:
                                return $"{leftSql} < {rightSql}";
                            case ExpressionType.LessThanOrEqual:
                                return $"{leftSql} <= {rightSql}";
                            default:
                                throw new NotSupportedException($"Binary operator {bin.NodeType} is not supported in ORDER BY conditional tests.");
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
                        // If it's a parameter member => column
                        if (memberExpr.Expression is ParameterExpression)
                        {
                            var property = memberExpr.Member as PropertyInfo;
                            if (property != null && !IsUnmappedProperty(property))
                                return GetColumnName(property);
                            throw new NotSupportedException($"Member {memberExpr.Member.Name} is not a mapped property.");
                        }

                        // Constant captured in closure (e.g. static field)
                        if (memberExpr.Expression is ConstantExpression constExpr)
                        {
                            var value = (memberExpr.Member as FieldInfo)?.GetValue(constExpr.Value);
                            return FormatConstant(value);
                        }

                        // Nullable.Value access: e.g., p.Birthdate.Value -> map to column
                        if (memberExpr.Member.MemberType == MemberTypes.Property && memberExpr.Member.Name == "Value" && memberExpr.Expression is MemberExpression inner)
                        {
                            if (inner.Expression is ParameterExpression)
                            {
                                var innerProp = inner.Member as PropertyInfo;
                                if (innerProp != null && !IsUnmappedProperty(innerProp))
                                {
                                    return GetColumnName(innerProp);
                                }
                            }
                        }

                        // Fallback: try to evaluate
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
                    // Attempt to evaluate the expression to a constant
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
                case string s:
                    return $"'{s.Replace("'", "''")}'";
                case DateTime dt:
                    // SQL-friendly ISO format (no timezone)
                    return $"'{dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}'";
                case bool b:
                    return b ? "1" : "0";
                case Guid g:
                    return $"'{g}'";
                case byte _:
                case sbyte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                case float _:
                case double _:
                case decimal _:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
                default:
                    // fallback with quoted ToString
                    return $"'{value?.ToString()?.Replace("'", "''")}'";
            }
        }
    }
}