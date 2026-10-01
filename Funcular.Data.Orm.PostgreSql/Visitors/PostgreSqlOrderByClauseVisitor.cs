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

        private readonly IReadOnlyDictionary<string, string> _propertyToColumnMap;
        private readonly string _tableQualifier;

        /// <param name="tableQualifier">When the query has joins, the base table name used to qualify own
        /// columns (<c>{table}.{column}</c>); <c>null</c> otherwise.</param>
        public PostgreSqlOrderByClauseVisitor(
            ConcurrentDictionary<string, string> columnNames,
            ICollection<PropertyInfo> unmappedProperties,
            IReadOnlyDictionary<string, string> propertyToColumnMap = null,
            string tableQualifier = null)
            : base(columnNames, unmappedProperties)
        {
            _propertyToColumnMap = propertyToColumnMap;
            _tableQualifier = tableQualifier;
        }

        /// <summary>
        /// The translated ordering terms, in order, after duplicate removal.
        /// </summary>
        public IReadOnlyList<Funcular.Data.Orm.Linq.OrderByTerm> OrderByTerms =>
            _orderByClauses.Select(c => new Funcular.Data.Orm.Linq.OrderByTerm(c.ColumnName, c.IsDescending)).ToList();

        /// <summary>
        /// Resolves a property to its ORDER BY SQL fragment. For a "view-replacing" / remote attribute
        /// ([JsonPath], [RemoteProperty]/[RemoteKey], [SqlExpression], [SubqueryAggregate]) this is the
        /// resolved expression from the remote-join map; otherwise the plain column name.
        /// </summary>
        private string ResolveOrderColumn(PropertyInfo property)
        {
            if (_propertyToColumnMap != null && _propertyToColumnMap.TryGetValue(property.Name, out var resolved))
                return resolved;
            // Own columns are qualified as {table}.{column} when the query has joins (#12): a bare own column such
            // as id is ambiguous against the joined tables once the projection no longer lists it.
            var column = GetColumnName(property);
            return _tableQualifier != null ? $"{_tableQualifier}.{column}" : column;
        }

        /// <summary>
        /// Adds an ordering term unless an earlier term has the same fragment: a later duplicate can never break a tie.
        /// </summary>
        private void AddOrderByClause(string columnName, bool isDescending)
        {
            if (_orderByClauses.Any(c => string.Equals(c.ColumnName, columnName, StringComparison.Ordinal)))
                return;
            _orderByClauses.Add(new OrderByClause { ColumnName = columnName, IsDescending = isDescending });
        }

        /// <summary>
        /// Whether a property can be ordered by: a plain mapped column, or a "view-replacing" / remote
        /// attribute present in the resolution map. Computed attributes are not base-table columns (so the
        /// unmapped-column check excludes them), but they are orderable via their resolved SQL fragment —
        /// so the map takes precedence over the unmapped gate.
        /// </summary>
        private bool IsOrderableProperty(PropertyInfo property)
        {
            if (_propertyToColumnMap != null && _propertyToColumnMap.ContainsKey(property.Name))
                return true;
            return !IsUnmappedProperty(property);
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
                if (property != null && IsOrderableProperty(property))
                {
                    var columnName = ResolveOrderColumn(property);
                    AddOrderByClause(columnName, isDescending);
                    return;
                }
            }
            else if (expression is UnaryExpression unary && unary.Operand is MemberExpression unaryMember)
            {
                var property = unaryMember.Member as PropertyInfo;
                if (property != null && IsOrderableProperty(property))
                {
                    var columnName = ResolveOrderColumn(property);
                    AddOrderByClause(columnName, isDescending);
                    return;
                }
            }
            else if (expression is ConditionalExpression conditional)
            {
                var caseSql = BuildCaseExpression(conditional);
                AddOrderByClause(caseSql, isDescending);
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

        // The compiler types a null literal as the other operand's type (string, int?, ...): a bare null constant.
        private static bool IsNullConstant(Expression expression) =>
            expression is ConstantExpression constant && constant.Value == null;

        private string BuildTestSql(Expression test)
        {
            switch (test)
            {
                case MemberExpression mem when mem.Member.MemberType == MemberTypes.Property && mem.Member.Name == "HasValue" && mem.Expression is MemberExpression inner:
                    {
                        var prop = inner.Member as PropertyInfo;
                        if (prop != null && IsOrderableProperty(prop))
                            return $"{ResolveOrderColumn(prop)} IS NOT NULL";
                        break;
                    }
                case BinaryExpression nullTest when (nullTest.NodeType == ExpressionType.Equal || nullTest.NodeType == ExpressionType.NotEqual)
                                                    && (IsNullConstant(nullTest.Left) || IsNullConstant(nullTest.Right)):
                    {
                        // SQL needs IS [NOT] NULL: `col = NULL` is never true (SQL Server even rejects it as a constant
                        // ORDER BY expression). Either operand order: x.M == null and null == x.M.
                        var operandSql = BuildValueSql(IsNullConstant(nullTest.Left) ? nullTest.Right : nullTest.Left);
                        return nullTest.NodeType == ExpressionType.Equal ? $"{operandSql} IS NULL" : $"{operandSql} IS NOT NULL";
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
                            if (property != null && IsOrderableProperty(property))
                                return ResolveOrderColumn(property);
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
                                if (innerProp != null && IsOrderableProperty(innerProp))
                                    return ResolveOrderColumn(innerProp);
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
