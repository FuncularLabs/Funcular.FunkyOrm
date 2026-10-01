using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Npgsql;
using NpgsqlTypes;
using System.Globalization;
using System.Text.RegularExpressions;

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
        private readonly PostgreSqlParameterGenerator _parameterGenerator;
        private readonly List<NpgsqlParameter> _parameters = new List<NpgsqlParameter>();
        private readonly List<(bool IsText, string Text)> _pendingValues = new List<(bool IsText, string Text)>();
        private readonly HashSet<string> _termKeys = new HashSet<string>(StringComparer.Ordinal);

        // Stands for a value in a term's SQL until the term is added (ValueSql, AddOrderByClause).
        private static readonly Regex Placeholder = new Regex("\u0001(\\d+)\u0001", RegexOptions.Compiled);

        /// <summary>The 3.9.0 constructor (no table qualifier), kept for binary compatibility.</summary>
        public PostgreSqlOrderByClauseVisitor(
            ConcurrentDictionary<string, string> columnNames,
            ICollection<PropertyInfo> unmappedProperties,
            IReadOnlyDictionary<string, string> propertyToColumnMap = null)
            : this(columnNames, unmappedProperties, propertyToColumnMap, null)
        {
        }

        /// <param name="tableQualifier">When the query has joins, the base table name used to qualify own
        /// columns (<c>{table}.{column}</c>); <c>null</c> otherwise.</param>
        public PostgreSqlOrderByClauseVisitor(
            ConcurrentDictionary<string, string> columnNames,
            ICollection<PropertyInfo> unmappedProperties,
            IReadOnlyDictionary<string, string> propertyToColumnMap,
            string tableQualifier)
            : this(columnNames, unmappedProperties, propertyToColumnMap, tableQualifier, null)
        {
        }

        /// <param name="parameterGenerator">When given, a value FunkyORM would write as quoted SQL text (a string, char,
        /// <see cref="Guid"/>, date or other non-numeric value) is sent as a command parameter carrying that text, one
        /// per occurrence as each literal was its own literal, listed in <see cref="Parameters"/>. A dropped duplicate
        /// term binds nothing, and PostgreSQL decides such a value's null test without one. Numbers, booleans, enums and
        /// <c>NULL</c> stay inline. Without a generator, values are inlined as literals.</param>
        public PostgreSqlOrderByClauseVisitor(
            ConcurrentDictionary<string, string> columnNames,
            ICollection<PropertyInfo> unmappedProperties,
            IReadOnlyDictionary<string, string> propertyToColumnMap,
            string tableQualifier,
            PostgreSqlParameterGenerator parameterGenerator)
            : base(columnNames, unmappedProperties)
        {
            _propertyToColumnMap = propertyToColumnMap;
            _tableQualifier = tableQualifier;
            _parameterGenerator = parameterGenerator;
        }

        /// <summary>
        /// The translated ordering terms, in order, after duplicate removal.
        /// </summary>
        public IReadOnlyList<Funcular.Data.Orm.Linq.OrderByTerm> OrderByTerms =>
            _orderByClauses.Select(c => new Funcular.Data.Orm.Linq.OrderByTerm(c.ColumnName, c.IsDescending)).ToList();

        /// <summary>
        /// The command parameters the ORDER BY fragments refer to (empty without a parameter generator).
        /// </summary>
        public IReadOnlyList<NpgsqlParameter> Parameters => _parameters;

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
        /// Adds an ordering term unless an earlier term is the same: a later duplicate can never break a tie. Terms
        /// compare by their SQL with each value as its kind and text, so a repeated ternary is a duplicate although each
        /// value is its own parameter. The values of a dropped term are never bound.
        /// </summary>
        private void AddOrderByClause(string columnName, bool isDescending)
        {
            var values = _pendingValues.ToArray();
            _pendingValues.Clear();
            // Placeholders exist only with a parameter generator; without one, every value is already a literal. The
            // key is length-prefixed, so no value's text can pass for the SQL around it.
            var key = _parameterGenerator == null ? columnName : Placeholder.Replace(columnName, m =>
            {
                var value = values[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)];
                return "\u0001" + (value.IsText ? "t" : "v") + value.Text.Length.ToString(CultureInfo.InvariantCulture) + ":"
                       + value.Text + "\u0001";
            });
            if (_termKeys.Contains(key))
                return;
            var sql = _parameterGenerator == null
                ? columnName
                : Placeholder.Replace(columnName, m => Bind(values[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)]));
            _orderByClauses.Add(new OrderByClause { ColumnName = sql, IsDescending = isDescending });
            // Recorded once the term is in, so a term that failed to bind is never taken for a duplicate later.
            _termKeys.Add(key);
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
            // Branch values go through the same operand path as the test, so a value reads the same in both positions.
            string trueSql = OperandSql(conditional.IfTrue, out _);
            string falseSql = OperandSql(conditional.IfFalse, out _);
            return $"CASE WHEN {testSql} THEN {trueSql} ELSE {falseSql} END";
        }

        /// <summary>
        /// The SQL for one operand of a ternary test, or for a THEN/ELSE value. An operand that reads no parameter of
        /// the ordering lambda (a literal, a captured variable, a call such as
        /// <c>names.FirstOrDefault(n =&gt; ...)</c>) is evaluated once and formatted as a constant, so its null check
        /// and its SQL can't disagree; anything else is translated as a column or value. <paramref name="isNull"/>
        /// reports an operand that is or evaluates to null.
        /// </summary>
        private string OperandSql(Expression operand, out bool isNull)
        {
            isNull = false;
            if (FreeParameterFinder.Reads(operand))
                return BuildValueSql(operand);

            // Like BuildValueSql, read through Convert: a char or enum comparison compiles through an int conversion,
            // and a nullable lift wraps the captured value. ConvertChecked is part of the value ((int)2.7 is 2), so it
            // is evaluated, never read through.
            var inner = operand;
            while (inner is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
                inner = unary.Operand;

            object value;
            if (inner is ConstantExpression constant)
                value = constant.Value;
            else if (inner is MemberExpression member && member.Member is FieldInfo field
                     && (member.Expression == null || member.Expression is ConstantExpression))
                value = field.GetValue((member.Expression as ConstantExpression)?.Value); // a captured variable or static field
            else
            {
                try
                {
                    value = Expression.Lambda(Expression.Convert(inner, typeof(object))).Compile().DynamicInvoke();
                }
                catch
                {
                    // Not retried: a second evaluation could see another outcome than the one that failed.
                    throw new NotSupportedException(inner is MemberExpression
                        ? $"Unsupported member expression in ORDER BY: {inner}"
                        : $"Unsupported expression in ORDER BY branch: {inner.NodeType}");
                }
            }

            isNull = value == null;
            return ValueSql(value);
        }

        /// <summary>
        /// Finds a parameter an expression reads but doesn't declare. A lambda inside the operand declares its own
        /// parameters, and reading those doesn't make the operand depend on the row.
        /// </summary>
        private sealed class FreeParameterFinder : ExpressionVisitor
        {
            private readonly HashSet<ParameterExpression> _declared = new HashSet<ParameterExpression>();
            private bool _found;

            public static bool Reads(Expression expression)
            {
                var finder = new FreeParameterFinder();
                finder.Visit(expression);
                return finder._found;
            }

            protected override Expression VisitLambda<TDelegate>(Expression<TDelegate> node)
            {
                foreach (var parameter in node.Parameters)
                    _declared.Add(parameter);
                return base.VisitLambda(node);
            }

            protected override Expression VisitBlock(BlockExpression node)
            {
                foreach (var variable in node.Variables)
                    _declared.Add(variable);
                return base.VisitBlock(node);
            }

            protected override CatchBlock VisitCatchBlock(CatchBlock node)
            {
                if (node.Variable != null)
                    _declared.Add(node.Variable);
                return base.VisitCatchBlock(node);
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (!_declared.Contains(node))
                    _found = true;
                return node;
            }
        }

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
                case BinaryExpression bin:
                    {
                        var leftSql = OperandSql(bin.Left, out var leftIsNull);
                        var rightSql = OperandSql(bin.Right, out var rightIsNull);
                        if ((bin.NodeType == ExpressionType.Equal || bin.NodeType == ExpressionType.NotEqual) && (leftIsNull || rightIsNull))
                        {
                            // SQL needs IS [NOT] NULL: `col = NULL` is never true (SQL Server even rejects it as a constant
                            // ORDER BY expression). Either operand order; the null may be a literal or an evaluated value.
                            var operandSql = leftIsNull ? rightSql : leftSql;
                            // PostgreSQL can't type a parameter that meets only IS NULL (42P18). A parameter holds a value
                            // read from no row and never null, so the test is decided here.
                            if (Placeholder.Match(operandSql).Value == operandSql)
                                return bin.NodeType == ExpressionType.Equal ? "FALSE" : "TRUE";
                            return bin.NodeType == ExpressionType.Equal ? $"{operandSql} IS NULL" : $"{operandSql} IS NOT NULL";
                        }
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
                            return ValueSql(value);
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
                            return ValueSql(evaluated);
                        }
                        catch
                        {
                            throw new NotSupportedException($"Unsupported member expression in ORDER BY: {memberExpr}");
                        }
                    }
                case ConstantExpression constExpr:
                    return ValueSql(constExpr.Value);
                case UnaryExpression unary when unary.NodeType == ExpressionType.Convert:
                    return BuildValueSql(unary.Operand);
                default:
                    try
                    {
                        var evaluated = Expression.Lambda(Expression.Convert(expr, typeof(object))).Compile().DynamicInvoke();
                        return ValueSql(evaluated);
                    }
                    catch
                    {
                        throw new NotSupportedException($"Unsupported expression in ORDER BY branch: {expr.NodeType}");
                    }
            }
        }

        /// <summary>
        /// A value's SQL. With a parameter generator, anything <see cref="FormatConstant"/> would quote becomes a command
        /// parameter carrying the text it would quote (<see cref="LiteralText"/>), so the database converts it as it
        /// converted 3.9.0's literal there: each occurrence is its own parameter, as each literal was its own literal.
        /// Until its term is added the value is a placeholder (<see cref="AddOrderByClause"/>). Numbers, booleans,
        /// enums and <c>NULL</c> stay inline.
        /// </summary>
        private string ValueSql(object value)
        {
            if (_parameterGenerator == null || value == null || value is bool || value is Enum || IsNumber(value))
                return FormatConstant(value);

            _pendingValues.Add((value is string || value is char, LiteralText(value)));
            return "\u0001" + (_pendingValues.Count - 1).ToString(CultureInfo.InvariantCulture) + "\u0001";
        }

        private string Bind((bool IsText, string Text) value)
        {
            var parameter = _parameterGenerator.CreateParameter(value.Text);
            // Untyped, as the literal was: PostgreSQL types it from where it's used (a timestamp, uuid, inet or citext
            // column), and as text in a CASE result.
            parameter.NpgsqlDbType = NpgsqlDbType.Unknown;
            _parameters.Add(parameter);
            return parameter.ParameterName;
        }

        private static bool IsNumber(object value) =>
            value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint
            || value is long || value is ulong || value is float || value is double || value is decimal;

        /// <summary>
        /// The text between the quotes of a value <see cref="FormatConstant"/> quotes, and of the parameter that
        /// replaces it, never in the current culture: a date as <c>yyyy-MM-dd HH:mm:ss.fff</c>, a
        /// <see cref="DateTimeOffset"/> as <c>yyyy-MM-dd HH:mm:ss.fffffffK</c>, a <c>DateOnly</c> as
        /// <c>yyyy-MM-dd</c>, a <c>TimeOnly</c> as <c>HH:mm:ss.FFFFFFF</c>, a Guid as <c>D</c>, anything else as its
        /// invariant text.
        /// </summary>
        private static string LiteralText(object value)
        {
            switch (value)
            {
                case DateTime dt:
                    return dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                case DateTimeOffset dto:
                    return dto.ToString("yyyy-MM-dd HH:mm:ss.fffffffK", CultureInfo.InvariantCulture);
                // By name: netstandard2.0 has no DateOnly or TimeOnly, but an app on .NET 6 or later can pass one.
                case IFormattable day when value.GetType().FullName == "System.DateOnly":
                    return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                case IFormattable time when value.GetType().FullName == "System.TimeOnly":
                    return time.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        private string FormatConstant(object value)
        {
            if (value == null) return "NULL";
            switch (value)
            {
                case bool b: return b ? "TRUE" : "FALSE";
                case Enum e:
                    return Convert.ToString(Convert.ChangeType(e, Enum.GetUnderlyingType(e.GetType()), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                case byte _: case sbyte _: case short _: case ushort _:
                case int _: case uint _: case long _: case ulong _:
                case float _: case double _: case decimal _:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
                default: return $"'{LiteralText(value).Replace("'", "''")}'";
            }
        }
    }
}
