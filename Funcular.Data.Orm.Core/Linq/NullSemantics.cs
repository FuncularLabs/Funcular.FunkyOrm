using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Funcular.Data.Orm.Attributes;

namespace Funcular.Data.Orm.Linq
{
    /// <summary>
    /// What <see cref="NullSemantics.Classify"/> finds an operand of a comparison to be
    /// (docs/plans/NULL_SEMANTICS_PLAN.md §4.1).
    /// </summary>
    public enum NullOperandKind
    {
        /// <summary>Read before the query, as the visitors read it: a constant, or a captured, static or nested member, through casts.</summary>
        Value,

        /// <summary>
        /// Neither a value nor a row operand the visitors translate (a field, an unmapped property, a nested receiver,
        /// arithmetic, another method, a cast with an operator declared outside the core library): a comparison with
        /// it is left as it is, and its value isn't read.
        /// </summary>
        NotAccepted,

        /// <summary>A row operand whose SQL value is never NULL.</summary>
        CannotBeNull,

        /// <summary>A row operand whose SQL value can be NULL; its witness is the expression tested with <c>IS NULL</c>.</summary>
        CanBeNull
    }

    /// <summary>
    /// Rewrites a WHERE predicate so that SQL's three-valued logic gives C#'s answer for every row, as EF Core's default
    /// null semantics do (docs/plans/NULL_SEMANTICS_PLAN.md §5.1): a <c>!=</c> also selects the rows where an operand
    /// that can be null is null, a comparison with a null value is <c>IS NULL</c> / <c>IS NOT NULL</c>, <c>!</c> is
    /// pushed down to the comparisons, and a <c>bool</c> compared with a predicate compares its two-valued value.
    /// Every provider's WHERE visitor translates the rewritten body. The result is built only from shapes the visitors
    /// translate, and a part with nothing to compensate keeps 3.11.0's structure, so it sends 3.11.0's SQL.
    /// </summary>
    public static class NullSemantics
    {
        private static readonly Assembly CoreLibrary = typeof(object).Assembly;

        private static readonly ConcurrentDictionary<PropertyInfo, bool> CanBeNullCache = new ConcurrentDictionary<PropertyInfo, bool>();

        private static readonly ConcurrentDictionary<Tuple<PropertyInfo, Type>, int> TemplateLevelsCache = new ConcurrentDictionary<Tuple<PropertyInfo, Type>, int>();

        // A template's tokens: a quoted string, a quoted identifier ([…], "…", `…`) or a comment (all skipped, R17-4), a
        // {Token}, CASE and END, a function SQL Server counts as a CASE, and the parentheses that close one.
        private static readonly System.Text.RegularExpressions.Regex TemplateToken = new System.Text.RegularExpressions.Regex(
            @"'(?:[^']|'')*'|\[[^\]]*\]|""[^""]*""|`[^`]*`|--[^\r\n]*|/\*[\s\S]*?\*/|\{(?<token>\w+)\}|\b(?<case>CASE)\b|\b(?<end>END)\b|\b(?<function>COALESCE|IIF|NULLIF)\s*\(|(?<open>\()|(?<close>\))",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static readonly IReadOnlyCollection<string> NoRemoteProperties = new string[0];

        /// <summary>
        /// Returns the body of <paramref name="predicate"/> rewritten so that its SQL selects the rows C# selects
        /// (EF Core's answer). Truth positions (the body, the operands of <c>&amp;&amp;</c>, <c>||</c> and <c>!</c>, a
        /// <c>bool</c> conditional's branches, and the test of every conditional reached) are folded first with
        /// <see cref="WherePredicateGuard.Fold(Expression, Type, IDictionary{Expression, bool?})"/> and
        /// <paramref name="memo"/>; then, carrying a polarity, <c>!</c> is pushed down (De Morgan, only where an operand
        /// changes), <c>==</c>/<c>!=</c> and the relational comparisons are compensated for the operands that can be
        /// null, and a <c>bool</c> compared with a predicate compares the predicate's two-valued value. Each captured value a
        /// comparison reads is read once and substituted (its <c>Convert</c> chain kept), so the visitors bind it from
        /// that read. A comparison with an operand the visitors don't translate is left as it is.
        /// </summary>
        /// <param name="predicate">A lambda with one parameter, the entity.</param>
        /// <param name="memo">The visitor's memo of folded truths; null for none.</param>
        /// <param name="isMapped">Whether a property of the entity is mapped to a column (the visitor's own test).</param>
        /// <param name="remoteProperties">The names of the properties in the visitor's remote map; null for none.</param>
        /// <returns>The rewritten body.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> or <paramref name="isMapped"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="predicate"/> doesn't have exactly one parameter.</exception>
        public static Expression Rewrite(
            LambdaExpression predicate,
            IDictionary<Expression, bool?>? memo,
            Func<PropertyInfo, bool> isMapped,
            IReadOnlyCollection<string>? remoteProperties)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));
            if (isMapped == null)
                throw new ArgumentNullException(nameof(isMapped));
            if (predicate.Parameters.Count != 1)
                throw new ArgumentException("A WHERE predicate must have exactly one parameter, the entity.", nameof(predicate));
            var rewriter = new Rewriter(predicate.Parameters[0].Type, memo, isMapped, remoteProperties ?? NoRemoteProperties);
            return rewriter.Truth(new LiftNormalizer().Visit(predicate.Body)!, false).Node;
        }

        /// <summary>
        /// Classifies <paramref name="operand"/>, an operand of a comparison in a predicate over <paramref name="row"/>,
        /// by its shape and declared types only; nothing is read (§4.1). A row operand the visitors translate is
        /// <see cref="NullOperandKind.CanBeNull"/> when its SQL value can be NULL, with the witness to test with
        /// <c>IS NULL</c>: the property (also for its date part, <c>Length</c> or <c>ToString()</c>), or the conditional.
        /// </summary>
        /// <param name="operand">The operand.</param>
        /// <param name="row">The predicate's parameter, the entity.</param>
        /// <param name="isMapped">Whether a property of the entity is mapped to a column.</param>
        /// <param name="remoteProperties">The names of the properties in the remote map; null for none.</param>
        /// <returns>The kind, and the witness for <see cref="NullOperandKind.CanBeNull"/> (else null).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="operand"/>, <paramref name="row"/> or <paramref name="isMapped"/> is null.</exception>
        public static (NullOperandKind Kind, Expression? Witness) Classify(
            Expression operand,
            ParameterExpression row,
            Func<PropertyInfo, bool> isMapped,
            IReadOnlyCollection<string>? remoteProperties)
        {
            if (operand == null)
                throw new ArgumentNullException(nameof(operand));
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            if (isMapped == null)
                throw new ArgumentNullException(nameof(isMapped));
            return new Rewriter(row.Type, null, isMapped, remoteProperties ?? NoRemoteProperties).Classify(operand);
        }

        /// <summary>
        /// Whether FunkyORM counts the SQL value of <paramref name="property"/>'s column as able to be NULL: a
        /// <see cref="Nullable{T}"/> or reference type; and, whatever its CLR type, a <c>[RemoteProperty]</c> or
        /// <c>[RemoteKey]</c> (a LEFT JOIN), a <c>[JsonPath]</c>, a <c>[SqlExpression]</c> or a <c>[JsonCollection]</c>; a
        /// <c>[SubqueryAggregate]</c> <c>Sum</c> or <c>Avg</c> only where its CLR type can hold null. A <c>Sum</c> or
        /// <c>Avg</c> over no rows is NULL in SQL, but one declared non-nullable counts as unable to be NULL: compared with
        /// a value as 3.11.0 compared it, without the NULL's compensation, and with a null value as <c>1=0</c> /
        /// <c>NOT 1=0</c> (the owner's ruling on Task 8's T8E2-4: compensating it made <c>x.Total != 0</c> select the rows
        /// with no children, which the property reads back as 0); where a predicate over it is sent as its two-valued
        /// CASE, a NULL aggregate makes the predicate false (a Known issue). A <c>Count</c>/<c>ConditionalCount</c> is
        /// never NULL. The translators use it for <c>IN</c>, negated <c>LIKE</c> and an integer's <c>ToString()</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="property"/> is null.</exception>
        public static bool CanBeNull(PropertyInfo property)
        {
            if (property == null)
                throw new ArgumentNullException(nameof(property));
            return CanBeNullCache.GetOrAdd(property, ComputeCanBeNull);
        }

        /// <summary>
        /// The CASE levels SQL Server counts in <paramref name="property"/>'s <c>[SqlExpression]</c> template (R16-2): the most
        /// CASEs, COALESCEs, IIFs and NULLIFs nested in it, outside quoted strings, quoted identifiers and comments, over
        /// every provider's variant, a <c>{Token}</c> that names another template adding that template's levels where it
        /// sits. Tokens resolve as the providers resolve them, by exact name against <paramref name="rowType"/>, the row's
        /// type (R17-4); a cycle counts nothing more. 0 for a property without one: the other properties of the remote map
        /// are a joined column, a JSON path, a JSON collection or a subquery aggregate, which hold none.
        /// </summary>
        private static int TemplateLevels(PropertyInfo property, Type rowType) =>
            TemplateLevelsCache.GetOrAdd(Tuple.Create(property, rowType), key => TemplateLevels(key.Item1, key.Item2, new HashSet<PropertyInfo>()));

        private static int TemplateLevels(PropertyInfo property, Type rowType, HashSet<PropertyInfo> visiting)
        {
            var attribute = (SqlExpressionAttribute?)Attribute.GetCustomAttribute(property, typeof(SqlExpressionAttribute));
            if (attribute == null || !visiting.Add(property))
                return 0;
            var deepest = 0;
            foreach (var template in new[] { attribute.Expression, attribute.MsSql, attribute.PostgreSql })
                if (!string.IsNullOrEmpty(template))
                    deepest = Math.Max(deepest, TemplateLevels(template, rowType, visiting));
            visiting.Remove(property);
            return deepest;
        }

        private static int TemplateLevels(string template, Type rowType, HashSet<PropertyInfo> visiting)
        {
            var open = new Stack<bool>(); // a parenthesis, true where it is a counted function's
            int levels = 0, deepest = 0;
            foreach (System.Text.RegularExpressions.Match token in TemplateToken.Matches(template))
            {
                if (token.Groups["token"].Success)
                {
                    foreach (var named in rowType.GetProperties().Where(p => p.Name == token.Groups["token"].Value))
                        deepest = Math.Max(deepest, levels + TemplateLevels(named, rowType, visiting));
                }
                else if (token.Groups["case"].Success)
                    deepest = Math.Max(deepest, ++levels);
                else if (token.Groups["end"].Success)
                    levels = Math.Max(0, levels - 1);
                else if (token.Groups["function"].Success)
                {
                    open.Push(true);
                    deepest = Math.Max(deepest, ++levels);
                }
                else if (token.Groups["open"].Success)
                    open.Push(false);
                else if (token.Groups["close"].Success && open.Count > 0 && open.Pop())
                    levels = Math.Max(0, levels - 1);
            }
            return deepest;
        }

        private static bool ComputeCanBeNull(PropertyInfo property)
        {
            if (Attribute.IsDefined(property, typeof(RemoteKeyAttribute)) || Attribute.IsDefined(property, typeof(RemotePropertyAttribute))
                || Attribute.IsDefined(property, typeof(JsonPathAttribute)) || Attribute.IsDefined(property, typeof(SqlExpressionAttribute))
                || Attribute.IsDefined(property, typeof(JsonCollectionAttribute)))
                return true;
            var aggregate = (SubqueryAggregateAttribute?)Attribute.GetCustomAttribute(property, typeof(SubqueryAggregateAttribute));
            if (aggregate != null)
                return aggregate.Function != AggregateFunction.Count && aggregate.Function != AggregateFunction.ConditionalCount
                    && CanHoldNull(property.PropertyType);
            return CanHoldNull(property.PropertyType);
        }

        private static bool CanHoldNull(Type type) => !type.IsValueType || Nullable.GetUnderlyingType(type) != null;

        private static bool IsBoolean(Type type) => type == typeof(bool) || type == typeof(bool?);

        private static bool IsCoreLibraryMethod(MethodInfo? method) =>
            method == null || method.DeclaringType?.Assembly == CoreLibrary;

        private static bool IsConvert(Expression expression) =>
            expression.NodeType == ExpressionType.Convert || expression.NodeType == ExpressionType.ConvertChecked;

        private static Expression StripConvert(Expression expression)
        {
            while (IsConvert(expression))
                expression = ((UnaryExpression)expression).Operand;
            return expression;
        }

        /// <summary><paramref name="operand"/> with <paramref name="inner"/>, at the end of its <c>Convert</c> chain, replaced; the chain kept.</summary>
        private static Expression Replace(Expression operand, Expression inner, Expression replacement)
        {
            if (ReferenceEquals(operand, inner))
                return replacement;
            var unary = (UnaryExpression)operand;
            return unary.Update(Replace(unary.Operand, inner, replacement));
        }

        /// <summary>One step of the rewrite: <see cref="Node"/> is final under the polarity when <see cref="Rebuilt"/>; else it is the node (substituted) to negate under negation.</summary>
        private readonly struct Step
        {
            private Step(Expression node, bool rebuilt)
            {
                Node = node;
                Rebuilt = rebuilt;
            }

            public Expression Node { get; }

            public bool Rebuilt { get; }

            /// <summary>Nothing compensated: the node keeps its structure (a value may be substituted in it).</summary>
            public static Step Same(Expression node) => new Step(node, false);

            /// <summary>Rebuilt, already under the polarity asked for.</summary>
            public static Step New(Expression node) => new Step(node, true);
        }

        /// <summary>
        /// Puts every lift to <c>bool?</c> outermost, once, before the rewrite (R7-1, R8-1, R9-2), so a predicate, <c>!</c>
        /// of one, a row operand or a conditional reads the same wherever the casts were: a side, a branch, the predicate
        /// itself. <c>!(bool?)p</c> is <c>(bool?)!p</c> (C#'s lifted <c>!</c> of a value that is never null); a
        /// <c>bool?</c> conditional whose branches are each a lift or a constant that isn't null is the lift of the
        /// <c>bool</c> conditional; a cast back to <c>bool</c> over a lift is the form lifted, <c>(bool)(bool?)e</c> being
        /// <c>e</c>; and a cast through <c>object</c> between <c>bool</c> and <c>bool?</c> is the cast without it. A body
        /// with no such chain is the same node. A part that stays in a value position sends the same SQL, since the
        /// visitors drop a cast; a conditional that a cast back to <c>bool</c> kept in a value position is in a truth
        /// position now, so its known parts are literals (AC-N11 (a); R9-4).
        /// </summary>
        private sealed class LiftNormalizer : ExpressionVisitor
        {
            protected override Expression VisitUnary(UnaryExpression node)
            {
                var visited = base.VisitUnary(node);
                return visited is UnaryExpression unary && unary.Method == null ? Normalize(unary) : visited;
            }

            protected override Expression VisitConditional(ConditionalExpression node)
            {
                var visited = base.VisitConditional(node);
                if (!(visited is ConditionalExpression conditional) || conditional.Type != typeof(bool?)
                    || !TryUnlift(conditional.IfTrue, out var ifTrue) || !TryUnlift(conditional.IfFalse, out var ifFalse))
                    return visited;
                return Expression.Convert(Expression.Condition(conditional.Test, ifTrue, ifFalse, typeof(bool)), typeof(bool?));
            }

            /// <summary>A nested lambda (a collection method's) is its own: left as it is.</summary>
            protected override Expression VisitLambda<T>(Expression<T> node) => node;

            private static Expression Normalize(UnaryExpression unary)
            {
                if (unary.NodeType == ExpressionType.Not && unary.Type == typeof(bool?) && IsLift(unary.Operand))
                    return Expression.Convert(Expression.Not(((UnaryExpression)unary.Operand).Operand), typeof(bool?));
                if (unary.NodeType != ExpressionType.Convert || !IsBoolean(unary.Type))
                    return unary;
                var operand = unary.Operand;
                if (operand.NodeType == ExpressionType.Convert && operand.Type == typeof(object) && ((UnaryExpression)operand).Method == null
                    && IsBoolean(((UnaryExpression)operand).Operand.Type))
                {
                    // (T)(object)e is (T)e: a box and an unbox of a bool or bool? are its value (or C#'s failed cast).
                    var boxed = ((UnaryExpression)operand).Operand;
                    return boxed.Type == unary.Type ? boxed : Normalize(Expression.Convert(boxed, unary.Type));
                }
                return unary.Type == typeof(bool) && IsLift(operand) ? ((UnaryExpression)operand).Operand : unary;
            }

            /// <summary>The <c>bool</c> that <paramref name="branch"/>, a <c>bool?</c>, lifts: under a lift, or a constant's value that isn't null.</summary>
            private static bool TryUnlift(Expression branch, out Expression unlifted)
            {
                if (IsLift(branch))
                    unlifted = ((UnaryExpression)branch).Operand;
                else if (branch is ConstantExpression constant && constant.Value is bool value)
                    unlifted = Expression.Constant(value);
                else
                    unlifted = null!;
                return unlifted != null;
            }

            private static bool IsLift(Expression expression) =>
                expression.NodeType == ExpressionType.Convert && ((UnaryExpression)expression).Method == null
                && expression.Type == typeof(bool?) && ((UnaryExpression)expression).Operand.Type == typeof(bool);
        }

        private enum SideKind { NotAccepted, Value, Predicate, Column }

        /// <summary>A side of a <c>bool</c> compared with a <c>bool</c> (AC-N14, rule 6).</summary>
        private readonly struct Side
        {
            public Side(SideKind kind, Expression? expression, bool? value, Expression? core, bool negated)
            {
                Kind = kind;
                Expression = expression;
                Value = value;
                Core = core;
                Negated = negated;
            }

            public static Side NotAccepted => new Side(SideKind.NotAccepted, null, null, null, false);

            public SideKind Kind { get; }

            /// <summary>A value's operand with its read substituted; a predicate (under its lift); a column as written.</summary>
            public Expression? Expression { get; }

            /// <summary>A value's truth, null for a null <c>bool?</c>.</summary>
            public bool? Value { get; }

            /// <summary>A column's row operand under the <c>!</c>s over it (R6-2).</summary>
            public Expression? Core { get; }

            /// <summary>Whether an odd number of <c>!</c>s is over the column's <see cref="Core"/>.</summary>
            public bool Negated { get; }
        }

        private sealed class Rewriter
        {
            private readonly Type _entityType;
            private readonly IDictionary<Expression, bool?>? _memo;
            private readonly Func<PropertyInfo, bool> _isMapped;
            private readonly IReadOnlyCollection<string> _remote;

            /// <summary>Flat conditionals a distributed comparison's leaf holds, rewritten already (R12-2): not rewritten again.</summary>
            private readonly HashSet<Expression> _rewritten = new HashSet<Expression>();

            public Rewriter(Type entityType, IDictionary<Expression, bool?>? memo, Func<PropertyInfo, bool> isMapped, IReadOnlyCollection<string> remote)
            {
                _entityType = entityType;
                _memo = memo;
                _isMapped = isMapped;
                _remote = remote;
            }

            #region Truth positions

            /// <summary>The rewrite of <paramref name="node"/>, a truth position, under <paramref name="negated"/>.</summary>
            public Step Truth(Expression node, bool negated)
            {
                // Rule 1: a known truth is a constant; the visitor sends it as the literal it sends for the node itself.
                var folded = WherePredicateGuard.Fold(node, _entityType, _memo);
                if (folded.HasValue)
                    return Step.Same(node is ConstantExpression ? node : Expression.Constant(folded.Value, node.Type));

                switch (node.NodeType)
                {
                    case ExpressionType.Not when ((UnaryExpression)node).Method == null:
                    {
                        var unary = (UnaryExpression)node;
                        var operand = Truth(unary.Operand, !negated);
                        return operand.Rebuilt ? operand : Step.Same(unary.Update(operand.Node));
                    }
                    case ExpressionType.AndAlso:
                    case ExpressionType.OrElse:
                        return ((BinaryExpression)node).Method == null ? Logical((BinaryExpression)node, negated) : Step.Same(node);
                    case ExpressionType.Conditional when IsBoolean(node.Type):
                    {
                        // The test is rewritten positive; the branches carry the polarity.
                        var conditional = (ConditionalExpression)node;
                        var test = Truth(conditional.Test, false);
                        var ifTrue = Truth(conditional.IfTrue, negated);
                        var ifFalse = Truth(conditional.IfFalse, negated);
                        if (!test.Rebuilt && !ifTrue.Rebuilt && !ifFalse.Rebuilt)
                            return Step.Same(conditional.Update(test.Node, ifTrue.Node, ifFalse.Node));
                        return Step.New(Expression.Condition(test.Node, As(Materialize(ifTrue, negated), node.Type),
                            As(Materialize(ifFalse, negated), node.Type), node.Type));
                    }
                    case ExpressionType.Equal:
                    case ExpressionType.NotEqual:
                        return Equality((BinaryExpression)node, negated);
                    case ExpressionType.GreaterThan:
                    case ExpressionType.GreaterThanOrEqual:
                    case ExpressionType.LessThan:
                    case ExpressionType.LessThanOrEqual:
                        return Relational((BinaryExpression)node, negated);
                    case ExpressionType.Call:
                        return Call((MethodCallExpression)node, negated);
                    case ExpressionType.Convert when ((UnaryExpression)node).Method == null && node.Type == typeof(bool)
                                                     && StripUnary(((UnaryExpression)node).Operand) is ConditionalExpression:
                    {
                        // A cast back to bool over a bool? conditional the LiftNormalizer leaves (a branch C# types as able to
                        // be null): the conditional is a row operand, its test rewritten (R4-2) and its branches values (R10-1).
                        var cast = RewriteOperand(node);
                        return cast.Rebuilt ? Step.New(negated ? Expression.Not(cast.Node) : cast.Node) : Step.Same(cast.Node);
                    }
                    default:
                        // A bool member, a cast over one, bitwise & and |: as they are.
                        return Step.Same(node);
                }
            }

            /// <summary>
            /// <c>&amp;&amp;</c> / <c>||</c>: the operands rewritten, and the operator swapped under negation (De Morgan) only
            /// when an operand changed. A left-deep chain of one operator is walked iteratively, so a long disjunction
            /// doesn't recurse once per term; a node in it whose truth is known is a leaf, as the visitor folds it.
            /// </summary>
            private Step Logical(BinaryExpression node, bool negated)
            {
                var chain = new List<BinaryExpression> { node };
                var innermost = node.Left;
                while (innermost.NodeType == node.NodeType && ((BinaryExpression)innermost).Method == null
                       && !WherePredicateGuard.Fold(innermost, _entityType, _memo).HasValue)
                {
                    chain.Add((BinaryExpression)innermost);
                    innermost = ((BinaryExpression)innermost).Left;
                }

                var result = Truth(innermost, negated);
                for (var i = chain.Count - 1; i >= 0; i--)
                {
                    var binary = chain[i];
                    var right = Truth(binary.Right, negated);
                    if (!result.Rebuilt && !right.Rebuilt)
                    {
                        result = Step.Same(binary.Update(result.Node, binary.Conversion, right.Node));
                        continue;
                    }
                    var l = Materialize(result, negated);
                    var r = Materialize(right, negated);
                    result = Step.New((binary.NodeType == ExpressionType.AndAlso) != negated ? Expression.AndAlso(l, r) : Expression.OrElse(l, r));
                }
                return result;
            }

            private static Expression Materialize(Step step, bool negated) =>
                step.Rebuilt || !negated ? step.Node : Expression.Not(step.Node);

            /// <summary>No step changed the comparison: it keeps its operator, under negation as <c>!(…)</c>.</summary>
            private static Step Kept(Expression comparison, bool negated, bool rebuilt) =>
                rebuilt ? Step.New(negated ? Expression.Not(comparison) : comparison) : Step.Same(comparison);

            private static Expression As(Expression expression, Type type) =>
                expression.Type == type ? expression : Expression.Convert(expression, type);

            #endregion

            #region Comparisons

            private Step Equality(BinaryExpression binary, bool negated)
            {
                if (!IsCoreLibraryMethod(binary.Method))
                    return Step.Same(binary);
                if (binary.Method == null && IsBoolean(binary.Left.Type) && IsBoolean(binary.Right.Type))
                    return BooleanEquality(binary, negated);

                var op = negated ? Flip(binary.NodeType) : binary.NodeType;
                var left = Classify(binary.Left).Kind;
                var right = Classify(binary.Right).Kind;
                if (left == NullOperandKind.NotAccepted || right == NullOperandKind.NotAccepted
                    || (left == NullOperandKind.Value && right == NullOperandKind.Value))
                    return Step.Same(binary);
                if (left == NullOperandKind.Value || right == NullOperandKind.Value)
                {
                    var valueOnLeft = left == NullOperandKind.Value;
                    if (!TryRead(valueOnLeft ? binary.Left : binary.Right, out var value, out var substituted))
                        return Step.Same(binary); // the read threw: the visitor reads it again and reports it
                    var row = valueOnLeft ? binary.Right : binary.Left;
                    return ValueEquality(binary, op, negated, valueOnLeft, value, substituted, row, rowNegated: false);
                }
                return RowEquality(binary, op, negated, binary.Left, false, binary.Right, false);
            }

            /// <summary>
            /// A value against a row operand (rule 6, steps 3–5); the value is already read, and substituted in
            /// <paramref name="substituted"/>. <paramref name="rowCore"/> is the row operand under the <c>!</c>s over it:
            /// with <paramref name="rowNegated"/>, <c>!m == v</c> is compared as <c>m == !v</c> (C#'s lifted <c>!</c>), so
            /// a compensated form has no NOT on a value and a null value tests the column (R6-2, R6-4).
            /// </summary>
            private Step ValueEquality(BinaryExpression binary, ExpressionType op, bool negated, bool valueOnLeft, object? value,
                Expression substituted, Expression rowCore, bool rowNegated)
            {
                var valueOperand = valueOnLeft ? binary.Left : binary.Right;
                var rowOperand = valueOnLeft ? binary.Right : binary.Left;
                var rowStep = RewriteOperand(rowCore);
                // The row operand as written, its core rewritten; and the operand the comparison is about, its `!`s moved.
                var row = Replace(rowOperand, rowCore, rowStep.Node);
                var compared = rowNegated ? As(rowStep.Node, rowOperand.Type) : row;
                var effective = rowNegated ? (value is bool truth ? !truth : (object?)null) : value;

                if (effective == null && CanHoldNull(valueOperand.Type))
                {
                    if (Classify(compared).Kind != NullOperandKind.CanBeNull)
                        return Step.New(Expression.Constant(op == ExpressionType.NotEqual)); // step 4: 1=0 / NOT 1=0
                    // NOT NULL is NULL: an odd number of `!`s under the operand's casts (one the moved `!`s didn't reach) is
                    // tested under them, as SQL reads `NOT x IS NULL` as NOT (x IS NULL), the complement (R11-1).
                    var oddNots = false;
                    for (var unary = compared; IsConvert(unary) || unary.NodeType == ExpressionType.Not; unary = ((UnaryExpression)unary).Operand)
                        oddNots ^= unary.NodeType == ExpressionType.Not;
                    if (oddNots)
                        compared = As(StripUnary(compared), compared.Type);
                    // A literal null is already the whole operand: the comparison is step 3's, and keeps its structure.
                    else if (!rowNegated && valueOperand is ConstantExpression && !rowStep.Rebuilt)
                        return Step.Same(binary.Update(valueOnLeft ? valueOperand : row, binary.Conversion, valueOnLeft ? row : valueOperand));
                    // Step 3: the whole value operand is the null, so the visitors send IS NULL / IS NOT NULL.
                    var nullValue = Expression.Constant(null, valueOperand.Type);
                    return Step.New(MakeEquality(op, valueOnLeft ? nullValue : compared, valueOnLeft ? compared : nullValue));
                }

                if (effective != null && op == ExpressionType.NotEqual && Classify(compared).Kind == NullOperandKind.CanBeNull)
                {
                    // Step 5: also the rows where the operand is null. The operand is substituted before it is sent again.
                    compared = SubstituteOperand(compared);
                    var v = rowNegated ? Expression.Constant(effective, valueOperand.Type) : substituted;
                    var l = valueOnLeft ? v : compared;
                    var r = valueOnLeft ? compared : v;
                    var inequality = negated || rowNegated ? MakeEquality(ExpressionType.NotEqual, l, r) : binary.Update(l, binary.Conversion, r);
                    // Substituted, a witness whose only branch that could be null folded can't be null now (R10-2). A nested
                    // conditional is distributed into its branches (the value holds no conditional); a flat one is repeated for
                    // its IS NULL, as EF repeats it (R12-2).
                    var witness = Classify(compared).Witness;
                    if (IsNested(witness))
                        return Step.New(Distribute((BinaryExpression)inequality, false));
                    return Step.New(witness == null ? inequality : Expression.OrElse(inequality, IsNull(witness)));
                }

                return Kept(binary.Update(valueOnLeft ? substituted : row, binary.Conversion, valueOnLeft ? row : substituted),
                    negated, rowStep.Rebuilt);
            }

            /// <summary>
            /// Two row operands (rule 6, step 6): EF's forms, each witness once. A <c>!</c> over one side but not the other is
            /// moved to the comparison: <c>!a == b</c> is EF's form of <c>a == b</c> around <c>a != b</c> (R6-2, R6-4).
            /// </summary>
            private Step RowEquality(BinaryExpression binary, ExpressionType op, bool negated, Expression leftCore, bool leftNegated,
                Expression rightCore, bool rightNegated)
            {
                var leftStep = RewriteOperand(leftCore);
                var rightStep = RewriteOperand(rightCore);
                var rebuilt = leftStep.Rebuilt || rightStep.Rebuilt;
                var l = As(leftStep.Node, binary.Left.Type);
                var r = As(rightStep.Node, binary.Right.Type);
                var leftNull = Classify(l).Kind == NullOperandKind.CanBeNull;
                var rightNull = Classify(r).Kind == NullOperandKind.CanBeNull;
                if ((!leftNull && !rightNull) || (op == ExpressionType.Equal && !(leftNull && rightNull)))
                    return Kept(binary.Update(Replace(binary.Left, leftCore, leftStep.Node), binary.Conversion,
                        Replace(binary.Right, rightCore, rightStep.Node)), negated, rebuilt);

                var leftSubstituted = SubstituteOperand(leftStep.Node);
                var rightSubstituted = SubstituteOperand(rightStep.Node);
                l = As(leftSubstituted, binary.Left.Type);
                r = As(rightSubstituted, binary.Right.Type);
                // Substituted, a witness whose only branch that could be null folded can't be null now (R10-2): the form is
                // decided again from what is sent, and sends the substituted operands, so nothing is read again (R11-2).
                var leftWitness = leftNull ? Classify(l).Witness : null;
                var rightWitness = rightNull ? Classify(r).Witness : null;
                leftNull = leftWitness != null;
                rightNull = rightWitness != null;
                if ((!leftNull && !rightNull) || (op == ExpressionType.Equal && !(leftNull && rightNull)))
                    return Kept(binary.Update(Replace(binary.Left, leftCore, leftSubstituted), binary.Conversion,
                        Replace(binary.Right, rightCore, rightSubstituted)), negated, rebuilt);
                // A nested conditional among the witnesses is distributed into its branches rather than repeated (R12-2), where
                // that keeps the CASE nesting within the comparison's + 1 (R14-1); otherwise EF's forms are kept (R13-1).
                if (IsNested(leftWitness) || IsNested(rightWitness))
                {
                    var distributed = DistributeWithinTheBound(binary.Update(Replace(binary.Left, leftCore, leftSubstituted), binary.Conversion,
                        Replace(binary.Right, rightCore, rightSubstituted)), negated, binary);
                    if (distributed != null)
                        return Step.New(distributed);
                }
                var coreOp = leftNegated == rightNegated ? op : Flip(op);
                var compared = coreOp == binary.NodeType ? binary.Update(l, binary.Conversion, r) : MakeEquality(coreOp, l, r);

                if (!(leftNull && rightNull))
                    return Step.New(Expression.OrElse(compared, IsNull(leftWitness ?? rightWitness!))); // !=, one can be null

                var same = SameWitness(leftWitness!, rightWitness!);
                if (op == ExpressionType.Equal)
                {
                    // `!a == b` is `a != b` where both have a value: guarded, since C#'s lifted != is true for one null where
                    // SQL's is unknown, so the rewritten body stays C#'s equivalent too.
                    if (coreOp != op && !same)
                        compared = Expression.AndAlso(Expression.AndAlso(compared, IsNotNull(leftWitness!)), IsNotNull(rightWitness!));
                    return Step.New(Expression.OrElse(compared,
                        same ? IsNull(leftWitness!) : Expression.AndAlso(IsNull(leftWitness!), IsNull(rightWitness!))));
                }
                if (same)
                    return Step.New(Expression.AndAlso(Expression.OrElse(compared, IsNull(leftWitness!)), IsNotNull(leftWitness!)));
                return Step.New(Expression.AndAlso(
                    Expression.OrElse(Expression.OrElse(compared, IsNull(leftWitness!)), IsNull(rightWitness!)),
                    Expression.OrElse(IsNotNull(leftWitness!), IsNotNull(rightWitness!))));
            }

            /// <summary><c>&gt;</c>, <c>&gt;=</c>, <c>&lt;</c>, <c>&lt;=</c> (rule 7).</summary>
            private Step Relational(BinaryExpression binary, bool negated)
            {
                if (!IsCoreLibraryMethod(binary.Method))
                    return Step.Same(binary);
                var leftKind = Classify(binary.Left).Kind;
                var rightKind = Classify(binary.Right).Kind;
                if (leftKind == NullOperandKind.NotAccepted || rightKind == NullOperandKind.NotAccepted
                    || (leftKind == NullOperandKind.Value && rightKind == NullOperandKind.Value))
                    return Step.Same(binary);

                var rebuilt = false;
                var operands = new[] { binary.Left, binary.Right };
                var kinds = new[] { leftKind, rightKind };
                for (var i = 0; i < 2; i++)
                {
                    if (kinds[i] == NullOperandKind.Value)
                    {
                        if (!TryRead(operands[i], out var value, out var substituted))
                            return Step.Same(binary);
                        if (value == null && CanHoldNull(operands[i].Type))
                            return Step.New(Expression.Constant(negated)); // false for every row, or true under negation
                        operands[i] = substituted;
                    }
                    else
                    {
                        var step = RewriteOperand(operands[i]);
                        operands[i] = step.Node;
                        rebuilt |= step.Rebuilt;
                    }
                }

                var leftNull = kinds[0] != NullOperandKind.Value && Classify(operands[0]).Kind == NullOperandKind.CanBeNull;
                var rightNull = kinds[1] != NullOperandKind.Value && Classify(operands[1]).Kind == NullOperandKind.CanBeNull;
                if (!negated || (!leftNull && !rightNull))
                    return Kept(binary.Update(operands[0], binary.Conversion, operands[1]), negated, rebuilt);

                // Under negation: also the rows where an operand that can be null is null.
                if (leftNull)
                    operands[0] = SubstituteOperand(operands[0]);
                if (rightNull)
                    operands[1] = SubstituteOperand(operands[1]);
                var compared = binary.Update(operands[0], binary.Conversion, operands[1]);
                var leftWitness = leftNull ? Classify(operands[0]).Witness! : null;
                var rightWitness = rightNull ? Classify(operands[1]).Witness! : null;
                // A nested conditional is distributed into its branches (R12-2) where that keeps the CASE nesting within the
                // comparison's + 1 (R14-1; otherwise EF's form, R13-1). Each branch repeats the other operand, so the one that
                // can't be null is substituted too, and read once; EF's form then sends it substituted.
                if (IsNested(leftWitness) || IsNested(rightWitness))
                {
                    operands[0] = leftNull ? operands[0] : SubstituteOperand(operands[0]);
                    operands[1] = rightNull ? operands[1] : SubstituteOperand(operands[1]);
                    compared = binary.Update(operands[0], binary.Conversion, operands[1]);
                    var distributed = DistributeWithinTheBound(compared, true, binary);
                    if (distributed != null)
                        return Step.New(distributed);
                }
                Expression result = Expression.OrElse(Expression.Not(compared), IsNull(leftWitness ?? rightWitness!));
                if (leftNull && rightNull && !SameWitness(leftWitness!, rightWitness!))
                    result = Expression.OrElse(result, IsNull(rightWitness!));
                return Step.New(result);
            }

            /// <summary>A method call (rule 8): the translator answers it negated; a negation it compensates is a change.</summary>
            private Step Call(MethodCallExpression call, bool negated) =>
                negated && IsCompensatedWhenNegated(call) ? Step.New(Expression.Not(call)) : Step.Same(call);

            /// <summary>
            /// Whether the translator's negated form compensates <paramref name="call"/> (§5.2), so a negated parent must
            /// push the negation down to it: a string <c>Contains</c>/<c>StartsWith</c>/<c>EndsWith</c> on a property of the
            /// row, or a collection <c>Contains</c> whose item is a property that can be null.
            /// </summary>
            private static bool IsCompensatedWhenNegated(MethodCallExpression call)
            {
                if (call.Type != typeof(bool))
                    return false;
                var name = call.Method.Name;
                if ((name == "Contains" || name == "StartsWith" || name == "EndsWith") && call.Object != null && call.Object.Type == typeof(string))
                    return call.Object is MemberExpression receiver && receiver.Expression is ParameterExpression
                           && receiver.Member is PropertyInfo receiverProperty && CanBeNull(receiverProperty);
                if (name != "Contains")
                    return false;
                var item = call.Object != null ? call.Arguments.FirstOrDefault() : call.Arguments.Count > 1 ? call.Arguments[1] : null;
                return item is MemberExpression member && member.Expression is ParameterExpression
                       && member.Member is PropertyInfo property && CanBeNull(property);
            }

            #endregion

            #region A bool compared with a bool (AC-N14)

            /// <summary>
            /// Whether <paramref name="expression"/>, a <c>bool</c>, is a predicate shape: a comparison, a logical operator,
            /// a method call, or <c>!</c> of a predicate. A <c>bool</c> member, <c>!</c> of one, and a conditional are values
            /// (R5-1, R6-6): SQL Server accepts a <c>bit</c> only as a value, so a comparison with one stays a comparison
            /// (rule 6), not AC-N14's two-valued value, which would put it in a CASE test. A predicate branch of a conditional
            /// is sent as its two-valued truth (<see cref="Branch"/>).
            /// </summary>
            private static bool IsPredicate(Expression expression)
            {
                switch (expression.NodeType)
                {
                    case ExpressionType.Equal:
                    case ExpressionType.NotEqual:
                    case ExpressionType.GreaterThan:
                    case ExpressionType.GreaterThanOrEqual:
                    case ExpressionType.LessThan:
                    case ExpressionType.LessThanOrEqual:
                    case ExpressionType.AndAlso:
                    case ExpressionType.OrElse:
                    case ExpressionType.Call:
                        return true;
                    case ExpressionType.Not:
                        return IsPredicate(((UnaryExpression)expression).Operand);
                    default:
                        return false;
                }
            }

            /// <summary>
            /// <paramref name="expression"/> as a <c>bool</c> predicate, or null when it isn't one: itself, or under its lift
            /// to <c>bool?</c>. <c>!(bool?)p</c> reaches here as <c>(bool?)!p</c> (<see cref="LiftNormalizer"/>; R7-1).
            /// </summary>
            private static Expression? AsPredicate(Expression expression)
            {
                var stripped = StripLift(expression);
                return stripped.Type == typeof(bool) && IsPredicate(stripped) ? stripped : null;
            }

            /// <summary>The <c>bool</c> under a lift to <c>bool?</c>.</summary>
            private static Expression StripLift(Expression expression) =>
                expression.NodeType == ExpressionType.Convert && ((UnaryExpression)expression).Method == null
                && expression.Type == typeof(bool?) && ((UnaryExpression)expression).Operand.Type == typeof(bool)
                    ? ((UnaryExpression)expression).Operand
                    : expression;

            /// <summary>
            /// A side of a <c>bool</c> compared with a <c>bool</c>, in order: a value (it reads no row, read once; or the fold
            /// knows its truth, R6-2, R6-3); a predicate; a row operand, under any <c>!</c>s over it (R6-2); or not accepted.
            /// </summary>
            private Side SideOf(Expression operand)
            {
                if (ReadsNoRow(operand))
                {
                    // A read that throws, or a value C# would fail to unbox, is left for the visitor to read and report.
                    if (!TryRead(operand, out var read, out var substituted) || !(read is bool || (read == null && CanHoldNull(operand.Type))))
                        return Side.NotAccepted;
                    return new Side(SideKind.Value, substituted, (bool?)read, null, false);
                }
                var folded = WherePredicateGuard.Fold(operand, _entityType, _memo);
                if (folded.HasValue)
                    return new Side(SideKind.Value, Expression.Constant(folded.Value, operand.Type), folded.Value, null, false);
                var predicate = AsPredicate(operand);
                if (predicate != null)
                    return new Side(SideKind.Predicate, predicate, null, null, false);
                var core = StripNegation(operand, out var negated);
                var kind = Classify(core).Kind;
                return kind == NullOperandKind.CanBeNull || kind == NullOperandKind.CannotBeNull
                    ? new Side(SideKind.Column, operand, null, core, negated)
                    : Side.NotAccepted;
            }

            /// <summary>
            /// Whether <paramref name="operand"/> is read before the query: a <see cref="NullOperandKind.Value"/>, or a
            /// <c>bool</c> that reads no row (a comparison or conditional of captured values; R6-2, R6-5), which is read as
            /// one value. Asked of a side and a branch as written; <see cref="Classify"/> stays by shape, so an operand that
            /// the rewrite folded down to constants keeps its kind (and its witness).
            /// </summary>
            private bool ReadsNoRow(Expression operand) =>
                Classify(operand).Kind == NullOperandKind.Value || (IsBoolean(operand.Type) && RowIndependentEvaluator.IsRowIndependent(operand));

            /// <summary>
            /// The operand under its <c>!</c>s and its lifts to <c>bool?</c>, and whether an odd number of <c>!</c>s was
            /// passed (R6-2); the operand itself when no <c>!</c> is over it.
            /// </summary>
            private static Expression StripNegation(Expression operand, out bool negated)
            {
                negated = false;
                var inner = operand;
                var found = false;
                while (true)
                {
                    if (inner.NodeType == ExpressionType.Not && ((UnaryExpression)inner).Method == null)
                    {
                        negated = !negated;
                        found = true;
                        inner = ((UnaryExpression)inner).Operand;
                    }
                    else if (!ReferenceEquals(StripLift(inner), inner))
                    {
                        inner = StripLift(inner);
                    }
                    else
                    {
                        return found ? inner : operand;
                    }
                }
            }

            /// <summary>
            /// A <c>bool</c> compared with a <c>bool</c>. With a predicate side it is AC-N14's; otherwise rule 6, with the
            /// <c>!</c>s over a row operand moved to the comparison (R6-2, R6-4). Two values compare as C# does: reached
            /// where the fold knows one side and the other is a null <c>bool?</c>, which the fold's truths can't hold.
            /// </summary>
            private Step BooleanEquality(BinaryExpression binary, bool negated)
            {
                var left = SideOf(binary.Left);
                if (left.Kind == SideKind.NotAccepted)
                    return Step.Same(binary);
                var right = SideOf(binary.Right);
                if (right.Kind == SideKind.NotAccepted)
                    return Step.Same(binary);
                var equal = binary.NodeType == ExpressionType.Equal;
                if (left.Kind == SideKind.Value && right.Kind == SideKind.Value)
                {
                    var holds = equal ? left.Value == right.Value : left.Value != right.Value;
                    return Step.New(Expression.Constant(holds != negated));
                }
                if (left.Kind == SideKind.Predicate || right.Kind == SideKind.Predicate)
                    return BooleanComparison(binary, negated, left, right);

                var op = negated ? Flip(binary.NodeType) : binary.NodeType;
                if (left.Kind == SideKind.Value || right.Kind == SideKind.Value)
                {
                    var valueOnLeft = left.Kind == SideKind.Value;
                    var value = valueOnLeft ? left : right;
                    var row = valueOnLeft ? right : left;
                    return ValueEquality(binary, op, negated, valueOnLeft, value.Value, value.Expression!, row.Core!, row.Negated);
                }
                return RowEquality(binary, op, negated, left.Core!, left.Negated, right.Core!, right.Negated);
            }

            /// <summary>
            /// AC-N14: a predicate against a value (true → p; false → !p; null → C#'s lifted answer). Against a predicate or
            /// a row operand, each predicate is its two-valued value, <c>p ? true : false</c> (EF's <c>CASE WHEN p THEN 1 ELSE
            /// 0 END</c>), sent once, and the comparison is rule 6's between values, so the SQL grows linearly with the
            /// nesting (R11-5). A row operand m is compared without its <c>!</c>s: <c>!m == p</c> is <c>m == !p</c>, C#'s
            /// lifted <c>!</c>, so no NOT is sent on a bit value.
            /// </summary>
            private Step BooleanComparison(BinaryExpression binary, bool negated, Side left, Side right)
            {
                var equal = binary.NodeType == ExpressionType.Equal;
                if (left.Kind == SideKind.Value || right.Kind == SideKind.Value)
                {
                    var value = left.Kind == SideKind.Value ? left.Value : right.Value;
                    var predicate = left.Kind == SideKind.Value ? right.Expression! : left.Expression!;
                    if (value == null)
                        return Step.New(Expression.Constant(!equal != negated)); // a predicate is never null: false for ==, true for !=
                    // true → p; false → !p.
                    var polarity = (value == true) == equal ? negated : !negated;
                    return Step.New(Materialize(Truth(predicate, polarity), polarity));
                }

                Expression comparison;
                if (left.Kind == SideKind.Predicate && right.Kind == SideKind.Predicate)
                {
                    comparison = MakeEquality(binary.NodeType, TwoValued(left.Expression!), TwoValued(right.Expression!));
                }
                else
                {
                    var column = left.Kind == SideKind.Column ? left : right;
                    var m = column.Core!;
                    var p = TwoValued(left.Kind == SideKind.Predicate ? left.Expression! : right.Expression!, !column.Negated);
                    comparison = MakeEquality(binary.NodeType, m, As(p, m.Type));
                }
                return Step.New(Materialize(Truth(comparison, negated), negated));
            }

            /// <summary>
            /// A predicate's two-valued value, <c>p ? true : false</c>, or <c>p ? false : true</c> for <c>!p</c>: never null,
            /// a NULL p being false (AC-N14, AC-N16).
            /// </summary>
            private static Expression TwoValued(Expression predicate, bool whenTrue = true) =>
                Expression.Condition(predicate, Expression.Constant(whenTrue), Expression.Constant(!whenTrue));

            /// <summary>
            /// A compensated comparison whose witness is a nested conditional, distributed into the conditional's branches
            /// (R12-2, by the owner's ruling, 2026-10-21): <c>(t ? a : b) op v</c> is <c>(t ? (a op v) : (b op v)) ==
            /// true</c>, through the casts and <c>!</c>s over the conditional and into the nested conditionals in its
            /// branches, on either side, each leaf comparison compensated by rules 6 and 7 as its two-valued value. The test,
            /// which holds the earlier level, is sent once, so the SQL grows linearly with the nesting, and each conditional's
            /// CASE stays where it was (R11-5's single CASE around the comparison doubled the nesting, past SQL Server's 10
            /// levels). A leaf's two-valued CASE adds one level below a branch, and each branch repeats the other operand
            /// below the distributed levels, so the step-6 and rule-7 call sites keep it only within the CASE bound
            /// (<see cref="DistributeWithinTheBound"/>). C# evaluates only the branch the test takes, so it is C#'s answer too.
            /// </summary>
            private Expression Distribute(BinaryExpression comparison, bool negated) =>
                Expression.Equal(DistributedValue(comparison, negated), Expression.Constant(true));

            /// <summary>
            /// <see cref="Distribute"/> where its CASE nesting is at most <paramref name="written"/>'s, the comparison as written,
            /// + 1, or no more than EF's form's, whose operands are <paramref name="comparison"/>'s and which adds no CASE level;
            /// null otherwise, and EF's forms are kept (R14-1; the owner's ruling, 2026-10-22: the bound-checked distribution).
            /// Against a conditional as deep as the nested side, or deeper, the distribution nests more (R13-1); against a
            /// shallower one, or none, it keeps the bound and grows the SQL linearly, where EF's form, repeated at every level of
            /// a chain, multiplies it. EF's form's own nesting counts because a predicate branch is its two-valued CASE (AC-N16),
            /// so a chain of them nests deeper than written whichever form it takes, a shape 3.11.0's SQL Server text rejected.
            /// The nesting is counted as SQL Server counts it (<see cref="CaseNesting"/>), an <c>[SqlExpression]</c>'s template
            /// included, since distribution moves the other operand below the distributed levels (R15-3, R16-2).
            /// </summary>
            private Expression? DistributeWithinTheBound(BinaryExpression comparison, bool negated, Expression written)
            {
                var distributed = Distribute(comparison, negated);
                var bound = Math.Max(CaseNesting(written) + 1, Math.Max(CaseNesting(comparison.Left), CaseNesting(comparison.Right)));
                return CaseNesting(distributed) <= bound ? distributed : null;
            }

            private Expression DistributedValue(BinaryExpression comparison, bool negated)
            {
                var leftInner = StripUnary(comparison.Left);
                var rightInner = StripUnary(comparison.Right);
                // Two nested sides: the larger first, so each of its leaves repeats the smaller one, distributed there in turn;
                // the smaller first would repeat the larger, which holds a chain's earlier levels, at every level (R15-1).
                if (IsNested(leftInner) && (!IsNested(rightInner) || !IsLarger(rightInner, leftInner)))
                {
                    var left = (ConditionalExpression)leftInner;
                    return Expression.Condition(left.Test,
                        DistributedValue(comparison.Update(Replace(comparison.Left, leftInner, left.IfTrue), comparison.Conversion, comparison.Right), negated),
                        DistributedValue(comparison.Update(Replace(comparison.Left, leftInner, left.IfFalse), comparison.Conversion, comparison.Right), negated));
                }
                if (IsNested(rightInner))
                {
                    var right = (ConditionalExpression)rightInner;
                    return Expression.Condition(right.Test,
                        DistributedValue(comparison.Update(comparison.Left, comparison.Conversion, Replace(comparison.Right, rightInner, right.IfTrue)), negated),
                        DistributedValue(comparison.Update(comparison.Left, comparison.Conversion, Replace(comparison.Right, rightInner, right.IfFalse)), negated));
                }
                // A leaf: what is left is flat, which the rules compensate, a flat conditional with EF's forms. Its test is
                // rewritten already, so it isn't rewritten again.
                if (leftInner is ConditionalExpression leftFlat)
                    _rewritten.Add(leftFlat);
                if (rightInner is ConditionalExpression rightFlat)
                    _rewritten.Add(rightFlat);
                var leaf = Materialize(Truth(comparison, negated), negated);
                return leaf is ConstantExpression ? leaf : TwoValued(leaf);
            }

            /// <summary>
            /// Whether <paramref name="witness"/> is a conditional that EF's forms can't repeat without compounding: one with a
            /// conditional in its test, or in a branch other than a nested flat conditional (R12-2). It holds an earlier
            /// comparison's form, so it is distributed (within the CASE bound, <see cref="DistributeWithinTheBound"/>). A flat
            /// one, its test and branches free of conditionals (through nested value conditionals), is repeated for its IS NULL
            /// as EF repeats it: a bounded copy that adds no CASE level.
            /// </summary>
            private static bool IsNested(Expression? witness) => witness is ConditionalExpression conditional && !IsFlat(conditional);

            /// <summary>
            /// The most CASEs nested in <paramref name="expression"/>'s SQL as SQL Server counts them (lambdas aside, since the
            /// WHERE visitors send none): each conditional, the <c>COALESCE(CAST(…), '')</c> a nullable integer's
            /// <c>ToString()</c> is sent as (R4-10), which SQL Server counts as a CASE too, and the levels in an
            /// <c>[SqlExpression]</c>'s template (<see cref="TemplateLevels(PropertyInfo, Type)"/>, R16-2).
            /// </summary>
            private static int CaseNesting(Expression expression)
            {
                var counter = new CaseNestingCounter();
                counter.Visit(expression);
                return counter.Deepest;
            }

            private sealed class CaseNestingCounter : ExpressionVisitor
            {
                private int _depth;

                public int Deepest { get; private set; }

                protected override Expression VisitConditional(ConditionalExpression node)
                {
                    _depth++;
                    Deepest = Math.Max(Deepest, _depth);
                    base.VisitConditional(node);
                    _depth--;
                    return node;
                }

                // As the translators send it: a property of the row, an integer, that can be NULL; its SQL sits inside the
                // COALESCE.
                protected override Expression VisitMethodCall(MethodCallExpression node)
                {
                    if (node.Method.Name == nameof(ToString) && node.Arguments.Count == 0 && node.Object is MemberExpression receiver
                        && receiver.Expression is ParameterExpression && receiver.Member is PropertyInfo property
                        && IsIntegerType(Nullable.GetUnderlyingType(receiver.Type) ?? receiver.Type) && CanBeNull(property))
                    {
                        _depth++;
                        Deepest = Math.Max(Deepest, _depth);
                        base.VisitMethodCall(node);
                        _depth--;
                        return node;
                    }
                    return base.VisitMethodCall(node);
                }

                protected override Expression VisitMember(MemberExpression node)
                {
                    if (node.Expression is ParameterExpression && node.Member is PropertyInfo property)
                        Deepest = Math.Max(Deepest, _depth + TemplateLevels(property, node.Expression.Type));
                    return base.VisitMember(node);
                }

                protected override Expression VisitLambda<T>(Expression<T> node) => node;
            }

            /// <summary>Whether <paramref name="expression"/> has more nodes than <paramref name="other"/> (counted no further than that).</summary>
            private static bool IsLarger(Expression expression, Expression other)
            {
                var size = new NodeCounter(int.MaxValue);
                size.Visit(other);
                var counter = new NodeCounter(size.Count + 1);
                counter.Visit(expression);
                return counter.Count > size.Count;
            }

            private sealed class NodeCounter : ExpressionVisitor
            {
                private readonly int _limit;

                public NodeCounter(int limit) => _limit = limit;

                public int Count { get; private set; }

                public override Expression? Visit(Expression? node)
                {
                    if (node == null || Count >= _limit)
                        return node;
                    Count++;
                    return base.Visit(node);
                }
            }

            private static bool IsFlat(ConditionalExpression conditional) =>
                !ContainsConditional(conditional.Test) && IsFlatBranch(conditional.IfTrue) && IsFlatBranch(conditional.IfFalse);

            private static bool IsFlatBranch(Expression branch) =>
                StripUnary(branch) is ConditionalExpression nested ? IsFlat(nested) : !ContainsConditional(branch);

            private static bool ContainsConditional(Expression expression)
            {
                var finder = new ConditionalFinder();
                finder.Visit(expression);
                return finder.Found;
            }

            private sealed class ConditionalFinder : ExpressionVisitor
            {
                public bool Found { get; private set; }

                public override Expression? Visit(Expression? node)
                {
                    if (Found || node == null)
                        return node;
                    if (node is ConditionalExpression)
                    {
                        Found = true;
                        return node;
                    }
                    return base.Visit(node);
                }

                protected override Expression VisitLambda<T>(Expression<T> node) => node;
            }

            #endregion

            #region Operands

            /// <summary>
            /// Classifies <paramref name="operand"/> (§4.1), by shape and declared type; nothing is read. Mirrors what the
            /// visitors translate: a mapped or remote property of the row; its <c>.Year</c>/<c>.Month</c>/<c>.Day</c>
            /// (also through <c>.Value</c>) or <c>.Length</c>; its no-argument <c>ToString()</c> on an integer or string;
            /// a conditional whose branches are accepted; through casts with core-library operators only. A cast keeps the
            /// operand's kind, as EF does, even to a type that can't hold null: a property can be NULL in SQL whatever
            /// its CLR type (R11-1).
            /// </summary>
            public (NullOperandKind Kind, Expression? Witness) Classify(Expression operand)
            {
                var inner = operand;
                while (IsConvert(inner))
                {
                    var unary = (UnaryExpression)inner;
                    if (!IsCoreLibraryMethod(unary.Method))
                        return (NullOperandKind.NotAccepted, null);
                    inner = unary.Operand;
                }
                if (inner is ConstantExpression || RowIndependentEvaluator.IsRootedChain(inner))
                    return (NullOperandKind.Value, null);

                switch (inner)
                {
                    case MemberExpression member when member.Expression is ParameterExpression:
                        return member.Member is PropertyInfo rowProperty && IsAccepted(rowProperty)
                            ? RowOperand(rowProperty, member)
                            : (NullOperandKind.NotAccepted, null);
                    case MemberExpression member:
                    {
                        // .HasValue and .Value on a nullable property are rejected by the visitors.
                        if (member.Member.Name == "HasValue" || member.Member.Name == "Value")
                            return (NullOperandKind.NotAccepted, null);
                        if (member.Expression == null || !TryGetRowProperty(member.Expression, out var property, out var propertyNode))
                            return (NullOperandKind.NotAccepted, null);
                        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                        var name = member.Member.Name;
                        var translated = (type == typeof(DateTime) && (name == "Year" || name == "Month" || name == "Day"))
                                         || (type == typeof(string) && name == "Length");
                        return translated && IsAccepted(property) ? RowOperand(property, propertyNode) : (NullOperandKind.NotAccepted, null);
                    }
                    case MethodCallExpression call when call.Method.Name == nameof(ToString) && call.Arguments.Count == 0
                                                        && call.Object is MemberExpression receiver
                                                        && receiver.Expression is ParameterExpression
                                                        && receiver.Member is PropertyInfo receiverProperty:
                    {
                        if (!IsAccepted(receiverProperty))
                            return (NullOperandKind.NotAccepted, null);
                        var type = Nullable.GetUnderlyingType(receiver.Type) ?? receiver.Type;
                        if (type == typeof(string))
                            return RowOperand(receiverProperty, receiver);
                        // An integer's text is COALESCE(…, '') wherever its value can be NULL, as EF sends it: never NULL.
                        return IsIntegerType(type) ? (NullOperandKind.CannotBeNull, null) : (NullOperandKind.NotAccepted, null);
                    }
                    case UnaryExpression not when not.NodeType == ExpressionType.Not && not.Method == null && IsBoolean(not.Type):
                    {
                        // `!` of a value is a value, and of a row operand that operand's kind and witness: NOT NULL is NULL
                        // (R6-2, R6-4).
                        var operandKind = Classify(not.Operand);
                        return operandKind.Kind == NullOperandKind.NotAccepted ? (NullOperandKind.NotAccepted, null) : operandKind;
                    }
                    case ConditionalExpression conditional:
                    {
                        var ifTrue = BranchKind(conditional.IfTrue);
                        var ifFalse = BranchKind(conditional.IfFalse);
                        if (ifTrue == NullOperandKind.NotAccepted || ifFalse == NullOperandKind.NotAccepted)
                            return (NullOperandKind.NotAccepted, null);
                        // By type only, no read: a value branch can be null when its type can hold null.
                        var canBeNull = BranchCanBeNull(ifTrue, conditional.IfTrue) || BranchCanBeNull(ifFalse, conditional.IfFalse);
                        return canBeNull ? (NullOperandKind.CanBeNull, (Expression)conditional) : (NullOperandKind.CannotBeNull, null);
                    }
                    default:
                        return (NullOperandKind.NotAccepted, null);
                }
            }

            /// <summary>A conditional's branch: a predicate that reads the row is sent as its two-valued truth, so it is never null (R6-4).</summary>
            private NullOperandKind BranchKind(Expression branch)
            {
                var kind = Classify(branch).Kind;
                return kind == NullOperandKind.NotAccepted && AsPredicate(branch) != null ? NullOperandKind.CannotBeNull : kind;
            }

            private static bool BranchCanBeNull(NullOperandKind kind, Expression branch) =>
                kind == NullOperandKind.CanBeNull || (kind == NullOperandKind.Value && CanHoldNull(branch.Type));

            private static (NullOperandKind Kind, Expression? Witness) RowOperand(PropertyInfo property, Expression witness) =>
                CanBeNull(property) ? (NullOperandKind.CanBeNull, witness) : (NullOperandKind.CannotBeNull, null);

            private bool IsAccepted(PropertyInfo property) => _remote.Contains(property.Name) || _isMapped(property);

            private static bool IsIntegerType(Type type) =>
                type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort) ||
                type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);

            /// <summary>The property of the row that <paramref name="receiver"/> reads: <c>x.P</c>, or <c>x.P.Value</c> on a nullable <c>P</c>.</summary>
            private static bool TryGetRowProperty(Expression receiver, out PropertyInfo property, out Expression propertyNode)
            {
                if (receiver is MemberExpression value && value.Member.Name == "Value" && value.Expression is MemberExpression inner
                    && Nullable.GetUnderlyingType(inner.Type) != null)
                    receiver = inner;
                if (receiver is MemberExpression member && member.Expression is ParameterExpression && member.Member is PropertyInfo found)
                {
                    property = found;
                    propertyNode = member;
                    return true;
                }
                property = null!;
                propertyNode = null!;
                return false;
            }

            /// <summary>
            /// A row operand with the test of every conditional in it rewritten positive (R3-1), through its casts and
            /// <c>!</c>s, and into nested conditionals in its branches.
            /// </summary>
            private Step RewriteOperand(Expression operand)
            {
                var inner = StripUnary(operand);
                if (!(inner is ConditionalExpression conditional) || _rewritten.Contains(conditional))
                    return Step.Same(operand);
                var test = Truth(conditional.Test, false);
                var ifTrue = Branch(conditional.IfTrue);
                var ifFalse = Branch(conditional.IfFalse);
                var rewritten = conditional.Update(test.Node, ifTrue.Node, ifFalse.Node);
                var step = Replace(operand, inner, rewritten);
                return test.Rebuilt || ifTrue.Rebuilt || ifFalse.Rebuilt ? Step.New(step) : Step.Same(step);
            }

            /// <summary>
            /// A value branch of a conditional: a value is read once and substituted (R5-4), so a null one is the literal
            /// <c>NULL</c> that PostgreSQL can type, and the visitor reads nothing again; a <c>bool</c> branch whose truth the
            /// fold knows is that value, so nothing under it is read; a predicate that reads the row is its two-valued truth,
            /// <c>CASE WHEN p THEN 1 ELSE 0 END</c> as EF sends it (R6-4), p rewritten positive; and a nested conditional is
            /// rewritten.
            /// </summary>
            private Step Branch(Expression branch)
            {
                if (ReadsNoRow(branch))
                    return StripConvert(branch) is ConstantExpression || !TryRead(branch, out _, out var read) ? Step.Same(branch) : Step.Same(read);
                var known = IsBoolean(branch.Type) ? WherePredicateGuard.Fold(branch, _entityType, _memo) : null;
                if (known.HasValue)
                    return Step.Same(As(Expression.Constant(known.Value), branch.Type));
                var predicate = Classify(branch).Kind == NullOperandKind.NotAccepted ? AsPredicate(branch) : null;
                if (predicate != null)
                    return Step.New(As(Expression.Condition(Truth(predicate, false).Node, Expression.Constant(true), Expression.Constant(false)),
                        branch.Type));
                return StripUnary(branch) is ConditionalExpression ? RewriteOperand(branch) : Step.Same(branch);
            }

            /// <summary>The operand under its casts and its <c>bool</c> <c>!</c>s.</summary>
            private static Expression StripUnary(Expression expression)
            {
                while (IsConvert(expression)
                       || (expression.NodeType == ExpressionType.Not && ((UnaryExpression)expression).Method == null && IsBoolean(expression.Type)))
                    expression = ((UnaryExpression)expression).Operand;
                return expression;
            }

            /// <summary>
            /// Reads <paramref name="valueOperand"/>, a <see cref="NullOperandKind.Value"/>, once, and substitutes it: the
            /// innermost member (or a <c>bool</c> that reads no row, as one value) becomes <c>Constant(value, type)</c> and
            /// the <c>Convert</c> chain is kept, so the visitors bind it as they bind the member. False, with nothing
            /// substituted, when the read throws or can't be made: the visitor reads it again and reports it.
            /// </summary>
            private bool TryRead(Expression valueOperand, out object? value, out Expression substituted)
            {
                var inner = StripConvert(valueOperand);
                substituted = valueOperand;
                if (inner is ConstantExpression constant)
                {
                    value = constant.Value;
                    return true;
                }
                if (!TryReadValue(inner, out value))
                    return false;
                substituted = Replace(valueOperand, inner, Expression.Constant(value, inner.Type));
                return true;
            }

            /// <summary>
            /// The value of <paramref name="inner"/>, which reads no row. A truth the fold knows, or a value it read, isn't
            /// read again (AC-N13, R6-5); a captured member is read as the visitors read it; a <c>bool</c> is evaluated,
            /// <c>!</c> and a conditional through their parts (R6-2), anything else by the fold.
            /// </summary>
            private bool TryReadValue(Expression inner, out object? value)
            {
                if (_memo != null && IsBoolean(inner.Type) && _memo.TryGetValue(inner, out var known) && known.HasValue)
                {
                    value = known.Value;
                    return true;
                }
                if (WherePredicateGuard.TryGetRead(_memo, inner, out value))
                    return true;
                value = null;
                if (inner is ConstantExpression constant)
                {
                    value = constant.Value;
                    return true;
                }
                if (RowIndependentEvaluator.IsRootedChain(inner))
                {
                    try
                    {
                        return RowIndependentEvaluator.TryReadValue(inner, out value);
                    }
                    catch (NotSupportedException)
                    {
                        return false;
                    }
                }
                switch (inner)
                {
                    case UnaryExpression not when not.NodeType == ExpressionType.Not && not.Method == null:
                    {
                        // C#'s lifted !: a null stays null. A null under a cast to bool (an unboxing C# would fail) isn't read.
                        if (!TryReadValue(StripConvert(not.Operand), out var operand) || !(operand is bool || (operand == null && CanHoldNull(not.Type))))
                            return false;
                        value = operand == null ? null : (object)!(bool)operand;
                        return true;
                    }
                    case ConditionalExpression conditional:
                    {
                        var test = WherePredicateGuard.Fold(conditional.Test, _entityType, _memo);
                        return test.HasValue && TryReadValue(StripConvert(test.Value ? conditional.IfTrue : conditional.IfFalse), out value);
                    }
                    default:
                    {
                        var truth = WherePredicateGuard.Fold(inner, _entityType, _memo);
                        value = truth;
                        return truth.HasValue;
                    }
                }
            }

            /// <summary>
            /// <paramref name="operand"/>, a row operand that is sent again as its witness, with every captured, static or
            /// nested member read once and replaced by its value, and every part whose truth the fold already knows
            /// replaced by that truth, so that repeating it reads nothing again (AC-N13). The casts and <c>!</c>s down to
            /// the witness, and the witness, are kept as they are, so it still has one (R9-1).
            /// </summary>
            private Expression SubstituteOperand(Expression operand) => new Substituter(_memo, _entityType, operand).Visit(operand)!;

            private sealed class Substituter : ExpressionVisitor
            {
                private readonly IDictionary<Expression, bool?>? _memo;
                private readonly Type _entityType;
                private readonly HashSet<Expression> _kept = new HashSet<Expression>();

                public Substituter(IDictionary<Expression, bool?>? memo, Type entityType, Expression root)
                {
                    _memo = memo;
                    _entityType = entityType;
                    _kept.Add(root);
                    // The chain Classify walks to a witness: casts and bool !s, then the witness.
                    while (!ReferenceEquals(StripUnaryOnce(root), root))
                    {
                        root = StripUnaryOnce(root);
                        _kept.Add(root);
                    }
                }

                private static Expression StripUnaryOnce(Expression expression) =>
                    IsConvert(expression) || (expression.NodeType == ExpressionType.Not && ((UnaryExpression)expression).Method == null && IsBoolean(expression.Type))
                        ? ((UnaryExpression)expression).Operand
                        : expression;

                /// <summary>
                /// A <c>bool</c> part whose truth the fold knows, from the memo or now, is that truth, so nothing under it is
                /// read (R8-3: the fold never reads a member under an <c>||</c> it knows is true). The node given, and for a
                /// row operand the chain down to its witness, are never replaced: a row operand stays one, with its witness
                /// (R6-3, R9-1).
                /// </summary>
                public override Expression? Visit(Expression? node)
                {
                    if (node != null && !_kept.Contains(node) && !(node is ConstantExpression) && IsBoolean(node.Type))
                    {
                        var known = WherePredicateGuard.Fold(node, _entityType, _memo);
                        if (known.HasValue)
                            return Expression.Constant(known.Value, node.Type);
                    }
                    return base.Visit(node);
                }

                protected override Expression VisitMember(MemberExpression node) =>
                    RowIndependentEvaluator.IsRootedChain(node) ? Read(node, null) : base.VisitMember(node);

                /// <summary>
                /// A captured member under a cast is read as <see cref="VisitMember"/> reads it, and a lazy collection there
                /// may be materialized against the cast's type (<c>((IEnumerable&lt;int?&gt;)boxed).Contains(x.N)</c>).
                /// </summary>
                protected override Expression VisitUnary(UnaryExpression node) =>
                    IsConvert(node) && node.Operand is MemberExpression member && RowIndependentEvaluator.IsRootedChain(member)
                        ? node.Update(Read(member, node.Type))
                        : base.VisitUnary(node);

                /// <summary>
                /// A static call's captured arguments are read as <see cref="VisitMember"/> reads them, and a lazy collection
                /// there (<c>Enumerable.Contains(lazy, x.N)</c>) may be materialized against the parameter's type, which an
                /// array stands in for where the variable's own concrete type can't (R6-5).
                /// </summary>
                protected override Expression VisitMethodCall(MethodCallExpression node)
                {
                    if (node.Object != null)
                        return base.VisitMethodCall(node);
                    var parameters = node.Method.GetParameters();
                    var arguments = new List<Expression>(node.Arguments.Count);
                    for (var i = 0; i < node.Arguments.Count; i++)
                    {
                        var argument = node.Arguments[i];
                        arguments.Add(argument is MemberExpression member && RowIndependentEvaluator.IsRootedChain(member)
                            ? Read(member, parameters[i].ParameterType)
                            : Visit(argument)!);
                    }
                    return node.Update(null, arguments);
                }

                /// <summary>
                /// <paramref name="node"/>, a captured member, read once (a value the fold read, a null <c>bool?</c> its truths
                /// can't hold, isn't read again; R6-5) and replaced by its value. A lazy collection is enumerated here, once,
                /// so that the copies of a repeated operand don't each run it (R5-8), as an array or an in-memory queryable
                /// of the member's type, or else of <paramref name="parameter"/>'s (the parameter or the cast it is passed
                /// through); one that throws is left for the translator to run and report (D9), and so is a read that throws.
                /// </summary>
                private Expression Read(MemberExpression node, Type? parameter)
                {
                    if (!WherePredicateGuard.TryGetRead(_memo, node, out var value))
                    {
                        try
                        {
                            RowIndependentEvaluator.TryReadValue(node, out value);
                        }
                        catch (NotSupportedException)
                        {
                            return node; // the visitor reads it again and reports it
                        }
                    }
                    if (value is System.Collections.IEnumerable sequence && !(value is string) && !(value is System.Collections.ICollection))
                    {
                        var target = Materialized(node.Type) != null ? node.Type : parameter != null && Materialized(parameter) != null ? parameter : null;
                        if (target != null && TryMaterialize(sequence, target, out var array))
                            return Expression.Constant(array, target);
                    }
                    return Expression.Constant(value, node.Type);
                }

                /// <summary>The element type of the <c>IEnumerable&lt;T&gt;</c> that <paramref name="declared"/> is or implements; null if none.</summary>
                private static Type? ElementOf(Type declared)
                {
                    var enumerable = declared.IsGenericType && declared.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                        ? declared
                        : declared.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
                    return enumerable?.GetGenericArguments()[0];
                }

                /// <summary>
                /// What a variable of <paramref name="declared"/> type can hold in place of a lazy sequence: an array, or an
                /// in-memory queryable over one for an <c>IQueryable&lt;T&gt;</c> (R6-7 c); null for a concrete lazy type.
                /// </summary>
                private static Type? Materialized(Type declared)
                {
                    var element = ElementOf(declared);
                    if (element == null)
                        return null;
                    if (declared.IsAssignableFrom(element.MakeArrayType()))
                        return element.MakeArrayType();
                    var queryable = typeof(EnumerableQuery<>).MakeGenericType(element);
                    return declared.IsAssignableFrom(queryable) ? queryable : null;
                }

                /// <summary>The items of <paramref name="sequence"/> as <see cref="Materialized"/>'s type for <paramref name="declared"/>.</summary>
                private static bool TryMaterialize(System.Collections.IEnumerable sequence, Type declared, out object? array)
                {
                    array = null;
                    var element = ElementOf(declared)!;
                    var queryable = !declared.IsAssignableFrom(element.MakeArrayType());
                    List<object?> items;
                    try
                    {
                        items = sequence.Cast<object?>().ToList();
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                    var result = Array.CreateInstance(element, items.Count);
                    for (var i = 0; i < items.Count; i++)
                        result.SetValue(items[i], i);
                    array = queryable ? Queryable.AsQueryable(result) : (object)result;
                    return true;
                }

                protected override Expression VisitLambda<T>(Expression<T> node) => node;
            }

            #endregion

            #region Building

            private static ExpressionType Flip(ExpressionType nodeType) =>
                nodeType == ExpressionType.Equal ? ExpressionType.NotEqual : ExpressionType.Equal;

            /// <summary><c>==</c> or <c>!=</c>, with the operator method re-resolved (<c>string</c>, <c>decimal</c>, <c>DateTime</c>, lifted forms).</summary>
            private static Expression MakeEquality(ExpressionType op, Expression left, Expression right) =>
                op == ExpressionType.Equal ? Expression.Equal(left, right) : Expression.NotEqual(left, right);

            /// <summary><c>W == null</c>, through a <c>Convert</c> to <see cref="Nullable{T}"/> for a non-nullable value type.</summary>
            private static Expression IsNull(Expression witness)
            {
                var operand = AsNullable(witness);
                return Expression.Equal(operand, Expression.Constant(null, operand.Type));
            }

            private static Expression IsNotNull(Expression witness)
            {
                var operand = AsNullable(witness);
                return Expression.NotEqual(operand, Expression.Constant(null, operand.Type));
            }

            private static Expression AsNullable(Expression witness) =>
                CanHoldNull(witness.Type) ? witness : Expression.Convert(witness, typeof(Nullable<>).MakeGenericType(witness.Type));

            /// <summary>
            /// Whether two witnesses are the same column: a witness is a property of the row (also for a date part,
            /// <c>Length</c> or <c>ToString()</c>) or a conditional, which is the same only as itself.
            /// </summary>
            private static bool SameWitness(Expression left, Expression right) =>
                ReferenceEquals(left, right)
                || (left is MemberExpression l && right is MemberExpression r && l.Member == r.Member
                    && l.Expression is ParameterExpression parameter && r.Expression == parameter);

            #endregion
        }
    }
}
