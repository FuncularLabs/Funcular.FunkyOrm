using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Funcular.Data.Orm
{
    /// <summary>
    /// The verdict <see cref="WherePredicateGuard.Classify"/> gives a WHERE predicate.
    /// </summary>
    public enum WherePredicateVerdict
    {
        /// <summary>The predicate may be translated and run.</summary>
        Acceptable,

        /// <summary>The predicate references no column of the entity.</summary>
        NoColumn,

        /// <summary>The predicate is a comparison of a member with itself that always holds.</summary>
        SelfReference,

        /// <summary>The predicate always holds.</summary>
        AlwaysTrue,

        /// <summary>
        /// A <c>Contains</c>, <c>StartsWith</c> or <c>EndsWith</c> on a column has an empty search value, which matches
        /// every non-null row (<c>LIKE '%%'</c>), and nothing else bounds the predicate (D-003's "unbounded").
        /// </summary>
        UnboundedSearch
    }

    /// <summary>
    /// What a WHERE predicate is used for, which decides whether the all-rows guard applies (docs/plans/DECISIONS-3.10.2.md
    /// D-003 as amended by D-128): one shared translation path, one flag.
    /// </summary>
    public enum WhereOperation
    {
        /// <summary>A read: <c>Query(predicate)</c>, <c>Where</c>, <c>Count</c>, <c>Any</c>, <c>All</c>. An all-rows predicate runs and selects every row.</summary>
        Read = 0,

        /// <summary>A mass mutation: <c>Delete(predicate)</c>, <c>DeleteAsync</c>, any update by predicate. An all-rows predicate is refused.</summary>
        MassMutation = 1
    }

    /// <summary>
    /// The all-rows guard for mass mutations, and the shared evaluation of the parts of a WHERE predicate that don't
    /// read the row (docs/plans/DECISIONS-3.10.2.md D-001, D-003 as amended by D-128).
    /// <para><b>Guard.</b> Every provider's shared WHERE translation calls <see cref="Validate(LambdaExpression, WhereOperation)"/>
    /// before translating the predicate (before the entity's columns are discovered, so a refused predicate never
    /// sends a command) and <see cref="ValidateTranslation(string, WhereOperation)"/> on the WHERE clause it produces,
    /// passing the operation. For a <see cref="WhereOperation.MassMutation"/> (<c>Delete(predicate)</c>, any update by
    /// predicate) a predicate that reads no column, compares a column (or a member of one) with itself, always holds,
    /// or searches a column for an empty value that nothing else bounds (<see cref="WherePredicateVerdict.UnboundedSearch"/>)
    /// is refused; for a <see cref="WhereOperation.Read"/> (<c>Query(predicate)</c>, <c>Where</c>) both checks accept
    /// everything, and the predicate runs and returns every row, as LINQ to objects would; the LINQ
    /// <c>Count</c>/<c>Any</c>/<c>All</c> build the same visitor directly and are reads by construction. The guard
    /// rejects the shapes it can prove touch every row (or every non-null row); it doesn't prove that every always-true
    /// predicate is one: <c>x =&gt; x.Id == 2 || x.Id != 2</c>, for example, is accepted.
    /// What no path can translate (a member of a member the dialect has no function for, a <c>ToString()</c> the
    /// evaluation can't answer, a nested receiver) is rejected by the translators with
    /// <see cref="NotSupportedException"/> on every path, read or mutation.</para>
    /// <para><b>Evaluation.</b> Every provider's WHERE visitor calls <see cref="Fold"/> on each part of a predicate in a
    /// truth position (the body, an operand of <c>&amp;&amp;</c>, <c>||</c> or <c>!</c>) and sends a part that doesn't
    /// read the row as a literal (<c>NOT 1=0</c> or <c>1=0</c>), so a comparison of captured values, a captured
    /// string's <c>Contains</c> or a core type's <c>ToString()</c> is answered as C# answers it, under C#'s ordinal
    /// string comparison, on every path alike. A custom provider can call all of these. This class is the former
    /// delete-only <c>DeletePredicateGuard</c> (3.10.0), which now forwards here.</para>
    /// </summary>
    public static class WherePredicateGuard
    {
        /// <summary>The message of <see cref="WherePredicateVerdict.NoColumn"/>.</summary>
        public const string NoColumnMessage = "The WHERE clause must reference at least one column of the entity.";

        /// <summary>The message of <see cref="WherePredicateVerdict.SelfReference"/>, before and after translation.</summary>
        public const string SelfReferenceMessage = "The WHERE clause cannot be a self-referencing column expression.";

        /// <summary>The message of <see cref="WherePredicateVerdict.AlwaysTrue"/>, and of a translated clause that holds by its literals.</summary>
        public const string AlwaysTrueMessage = "The WHERE clause is trivially true; a non-trivial WHERE clause is required.";

        /// <summary>The message of a translation that produced no WHERE clause.</summary>
        public const string EmptyTranslationMessage = "The WHERE clause translated to nothing; a non-trivial WHERE clause is required.";

        /// <summary>The message of <see cref="WherePredicateVerdict.UnboundedSearch"/>.</summary>
        public const string UnboundedSearchMessage =
            "The WHERE clause's Contains, StartsWith or EndsWith has an empty search value, which matches every non-null row; a non-trivial WHERE clause is required.";

        private static readonly Assembly CoreLibrary = typeof(object).Assembly;

        // A 1=1 that isn't part of a longer token: not glued to a word character, @, $, . or :.
        private static readonly Regex OneEqualsOne =
            new Regex(@"(?<![\w@$.:])1\s*=\s*1(?![\w@$.:])", RegexOptions.CultureInvariant);

        private static readonly Regex NumberLiteral = new Regex(@"^[0-9]+(\.[0-9]+)?$", RegexOptions.CultureInvariant);

        // The whole clause is one operand compared with itself by =, >= or <= (x => x.Id == x.Id, through a cast or a ToString()).
        private static readonly Regex WholeClauseSelfComparison =
            new Regex(@"^(.+?)\s*(=|>=|<=)\s*\1$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Classifies <paramref name="predicate"/> before it is translated.
        /// <list type="bullet">
        /// <item><see cref="WherePredicateVerdict.NoColumn"/>: the body reads no member of the entity parameter
        /// (<c>x =&gt; true</c>, <c>x =&gt; 1 &lt; 2</c>, a captured <c>bool</c>, <c>x =&gt; ids.Contains(request.Id)</c>).</item>
        /// <item><see cref="WherePredicateVerdict.SelfReference"/>: the body is a comparison of a property of the entity,
        /// or of a member of one (<c>x.Name.Length</c>, <c>x.Created.Year</c>, <c>x.Child.Id</c>), with itself by
        /// <c>==</c>, <c>&gt;=</c> or <c>&lt;=</c> (also through a cast or a no-argument <c>ToString()</c>), or the
        /// negation of one by <c>!=</c>, <c>&gt;</c> or <c>&lt;</c>: 3.10's rule, whatever the type. A nullable's
        /// <c>n &gt;= n</c> and a <c>double</c>'s <c>d == d</c> are refused too, although C# answers false for a null
        /// or a NaN: a mass mutation of them would touch every other row, and the verdict is about that, not about
        /// what the SQL sends (<see cref="Fold"/> folds only where C# answers the same for every row).</item>
        /// <item><see cref="WherePredicateVerdict.AlwaysTrue"/>: the body always holds otherwise (a self-comparison
        /// counted as above), for example a disjunction with a <c>true</c> literal, a captured or static <c>true</c>, a
        /// <c>bool</c> unboxed from a captured <see cref="object"/>, a <c>true</c> property of a captured object, a
        /// comparison of constants and captured values that holds (<c>filter == null || …</c> with a null
        /// <c>filter</c>), a <c>Contains</c> on a captured string that holds (<c>roles.Contains("admin") || …</c>), or
        /// <c>x.Id == 2 || x.Name.Length == x.Name.Length</c>.</item>
        /// <item><see cref="WherePredicateVerdict.UnboundedSearch"/>: the body holds for every non-null row once a
        /// <c>Contains</c>, <c>StartsWith</c> or <c>EndsWith</c> on a property of the entity with an empty search value
        /// (a literal, a captured <c>""</c>, <c>string.Empty</c>) counts as true: <c>x.Name.Contains("")</c> alone, in
        /// an <c>||</c>, or compared with a <c>bool</c> (<c>x.Name.Contains("") == true</c>, <c>!= false</c>); not
        /// <c>x.Id == 2 &amp;&amp; x.Name.Contains("")</c>, which the other operand bounds, and not a search on a member
        /// of a member (<c>x.Child.Name.Contains("")</c>), whose receiver the translators reject on every path.</item>
        /// <item><see cref="WherePredicateVerdict.Acceptable"/>: anything else.</item>
        /// </list>
        /// A <c>bool</c> compared with a <c>bool</c> by <c>==</c>/<c>!=</c> composes its operands' truths, so
        /// <c>(x.Id == x.Id) == true</c> always holds and <c>x.Name.Contains("") == true</c> is unbounded.
        /// Parameter-free <c>bool</c> parts built from constants, field and property reads, casts, the logical
        /// operators, comparisons, the conditional operator, <see cref="string"/>'s <c>Contains</c>, and <c>ToString()</c>
        /// with no argument or a format string on a receiver declared as an enum or a non-generic, sealed or value type of
        /// the core library (or a nullable one) are evaluated, as C# evaluates them; a receiver declared as
        /// <see cref="object"/> or an interface isn't, whatever it holds. A part that calls any other method, a
        /// constructor, or an operator or conversion declared outside the core library is never evaluated, and counts
        /// as unknown, as does any other part whose evaluation throws (a <c>Contains</c> of a null value, which the
        /// translation sends as <c>NULL LIKE NULL</c>). So no code outside the core library runs, except property getters.
        /// </summary>
        /// <param name="predicate">A lambda with one parameter, the entity, and a <c>bool</c> body.</param>
        /// <returns>The verdict.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="predicate"/> doesn't have exactly one parameter, or its
        /// body isn't <c>bool</c>.</exception>
        public static WherePredicateVerdict Classify(LambdaExpression predicate)
        {
            CheckContract(predicate);

            var entityType = predicate.Parameters[0].Type;
            var finder = new MemberChainFinder(entityType);
            finder.Visit(predicate.Body);
            if (!finder.Found)
                return WherePredicateVerdict.NoColumn;

            // The verdict counts every self-comparison as holding (3.10's rule): what it decides is whether a mass
            // mutation would touch every row, or every non-null row, not what C# answers for a null (review R-2, A/N-2).
            if (FoldTruth(predicate.Body, entityType, FoldRules.Verdict, null) == Truth.True)
                return IsSelfComparison(StripNotAndConvert(predicate.Body), entityType, broad: true)
                    ? WherePredicateVerdict.SelfReference
                    : WherePredicateVerdict.AlwaysTrue;

            // D-003's "unbounded": with an empty search value on a column counted as true, does the whole hold?
            return FoldTruth(predicate.Body, entityType, FoldRules.VerdictWithUnboundedSearch, null) == Truth.True
                ? WherePredicateVerdict.UnboundedSearch
                : WherePredicateVerdict.Acceptable;
        }

        private static void CheckContract(LambdaExpression predicate)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));
            if (predicate.Parameters.Count != 1)
                throw new ArgumentException("A WHERE predicate must have exactly one parameter, the entity.", nameof(predicate));
            if (predicate.Body.Type != typeof(bool))
                throw new ArgumentException("A WHERE predicate's body must be bool.", nameof(predicate));
        }

        /// <summary>
        /// The mass-mutation form of <see cref="Validate(LambdaExpression, WhereOperation)"/>: throws
        /// <see cref="InvalidOperationException"/> with the message of the verdict when <see cref="Classify"/> rejects
        /// <paramref name="predicate"/>; otherwise returns.
        /// </summary>
        /// <param name="predicate">A lambda with one parameter, the entity, and a <c>bool</c> body.</param>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="predicate"/> doesn't have exactly one parameter, or its
        /// body isn't <c>bool</c>.</exception>
        /// <exception cref="InvalidOperationException">The predicate is rejected.</exception>
        public static void Validate(LambdaExpression predicate) => Validate(predicate, WhereOperation.MassMutation);

        /// <summary>
        /// Checks <paramref name="predicate"/> before it is translated, for <paramref name="operation"/>. For a
        /// <see cref="WhereOperation.MassMutation"/>, throws <see cref="InvalidOperationException"/> with the message
        /// of the verdict when <see cref="Classify"/> rejects the predicate; for a <see cref="WhereOperation.Read"/>,
        /// accepts every well-formed predicate. The argument contract is checked for both.
        /// </summary>
        /// <param name="predicate">A lambda with one parameter, the entity, and a <c>bool</c> body.</param>
        /// <param name="operation">What the predicate is used for.</param>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="predicate"/> doesn't have exactly one parameter, or its
        /// body isn't <c>bool</c>.</exception>
        /// <exception cref="InvalidOperationException">The operation is a mass mutation and the predicate is rejected.</exception>
        public static void Validate(LambdaExpression predicate, WhereOperation operation)
        {
            CheckContract(predicate);
            if (operation != WhereOperation.MassMutation)
                return; // a read isn't classified: nothing would be done with the verdict (review R-1, A-4/B-4)
            switch (Classify(predicate))
            {
                case WherePredicateVerdict.NoColumn:
                    throw new InvalidOperationException(NoColumnMessage);
                case WherePredicateVerdict.SelfReference:
                    throw new InvalidOperationException(SelfReferenceMessage);
                case WherePredicateVerdict.AlwaysTrue:
                    throw new InvalidOperationException(AlwaysTrueMessage);
                case WherePredicateVerdict.UnboundedSearch:
                    throw new InvalidOperationException(UnboundedSearchMessage);
            }
        }

        /// <summary>
        /// The C# truth of <paramref name="expression"/>, a <c>bool</c> (or <c>bool?</c>) part of a predicate over an
        /// entity of <paramref name="entityType"/>, when it can be known without a row: a constant; a part that reads
        /// no parameter and is built from constants, field and property reads, casts, <c>!</c>, the logical operators,
        /// comparisons, the conditional operator, <see cref="string"/>'s <c>Contains</c> and a core type's
        /// <c>ToString()</c> (as <see cref="Classify"/> evaluates them: no code outside the core library runs, except
        /// property getters); or a comparison of a property of the entity with itself where C# answers the same for
        /// every row (<c>x.Id == x.Id</c> is true, <c>x.Id != x.Id</c> false, a nullable's <c>x.N &lt; x.N</c> false;
        /// not a nullable's <c>x.N &gt;= x.N</c>, which is false for null and true otherwise, not a <c>float</c>/<c>double</c>
        /// property, since NaN isn't equal to itself, and not a member of a member such as <c>x.Name.Length</c>, which
        /// the translator accepts or rejects; those translate as written). An empty search value on a column is a
        /// column read, not a truth (<c>LIKE '%%'</c> excludes a NULL column). Null when the part reads the row
        /// otherwise, calls anything else, or its evaluation throws (a <c>Contains</c> of null). The WHERE visitors
        /// send a known truth as a literal (<c>NOT 1=0</c> / <c>1=0</c>) on every path, so Query and Delete agree.
        /// <see cref="Classify"/>'s verdict counts more self-comparisons than this fold does, on purpose: see there.
        /// </summary>
        /// <param name="expression">The part to evaluate.</param>
        /// <param name="entityType">The entity type of the predicate's parameter.</param>
        /// <returns>True, false, or null for unknown.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="expression"/> or <paramref name="entityType"/> is null.</exception>
        public static bool? Fold(Expression expression, Type entityType) => Fold(expression, entityType, null);

        /// <summary>
        /// <see cref="Fold(Expression, Type)"/> with a memo: the truth of every sub-expression folded on the way is
        /// recorded in <paramref name="memo"/> (keyed by reference), and a later call on one of them is a lookup. A
        /// WHERE visitor keeps one memo per translation, so folding the body and then each truth position inside it
        /// is one pass over the tree, not one per level.
        /// </summary>
        /// <param name="expression">The part to evaluate.</param>
        /// <param name="entityType">The entity type of the predicate's parameter.</param>
        /// <param name="memo">The truths recorded so far; null for none.</param>
        /// <returns>True, false, or null for unknown.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="expression"/> or <paramref name="entityType"/> is null.</exception>
        public static bool? Fold(Expression expression, Type entityType, IDictionary<Expression, bool?>? memo)
        {
            if (expression == null)
                throw new ArgumentNullException(nameof(expression));
            if (entityType == null)
                throw new ArgumentNullException(nameof(entityType));
            return ToNullable(FoldTruth(expression, entityType, FoldRules.Sql, memo));
        }

        private static bool? ToNullable(Truth truth) => truth == Truth.True ? true : truth == Truth.False ? false : (bool?)null;

        private static Truth FromNullable(bool? value) => value == true ? Truth.True : value == false ? Truth.False : Truth.Unknown;

        /// <summary>
        /// The mass-mutation form of <see cref="ValidateTranslation(string, WhereOperation)"/>.
        /// </summary>
        /// <param name="whereClause">The translated WHERE clause of one predicate, without the <c>WHERE</c> keyword.</param>
        /// <exception cref="ArgumentNullException"><paramref name="whereClause"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The clause fails one of the checks.</exception>
        public static void ValidateTranslation(string whereClause) => ValidateTranslation(whereClause, WhereOperation.MassMutation);

        /// <summary>
        /// Checks the WHERE clause a predicate translated to, for <paramref name="operation"/>. For a
        /// <see cref="WhereOperation.MassMutation"/> the clause must not be empty, must not hold by its literals
        /// (<see cref="HasLiteralTautology(string)"/>: a <c>1=1</c>, or <c>t.id = @p OR NOT 1=0</c>, which a negated
        /// <c>Contains</c> over an empty collection or an evaluated part that holds produces), and must not be one
        /// operand compared with itself (<c>CAST(t.id AS TEXT) = CAST(t.id AS TEXT)</c>); for a
        /// <see cref="WhereOperation.Read"/> every clause is accepted.
        /// </summary>
        /// <param name="whereClause">The translated WHERE clause of one predicate, without the <c>WHERE</c> keyword.</param>
        /// <param name="operation">What the predicate is used for.</param>
        /// <exception cref="ArgumentNullException"><paramref name="whereClause"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The operation is a mass mutation and the clause fails one of the checks.</exception>
        public static void ValidateTranslation(string whereClause, WhereOperation operation)
        {
            if (whereClause == null)
                throw new ArgumentNullException(nameof(whereClause));
            if (operation != WhereOperation.MassMutation)
                return;
            if (string.IsNullOrWhiteSpace(whereClause))
                throw new InvalidOperationException(EmptyTranslationMessage);
            if (HasLiteralTautology(whereClause))
                throw new InvalidOperationException(AlwaysTrueMessage);
            if (WholeClauseSelfComparison.IsMatch(whereClause.Trim()))
                throw new InvalidOperationException(SelfReferenceMessage);
        }

        /// <summary>
        /// Returns true when the translated <paramref name="whereClause"/> contains a <c>1=1</c> outside quotes, or
        /// folds to true over its literal comparisons (for example <c>t.id = @p OR NOT 1=0</c>, which a negated
        /// <c>Contains</c> over an empty collection produces). A clause that folds to false or unknown, or that can't be
        /// parsed, returns false. A clause nested too deeply for the calling thread's stack is parsed again on a thread
        /// with a 64 MB stack (an unexpected exception there is raised again on the calling thread); one too deep even
        /// for that returns false.
        /// </summary>
        /// <param name="whereClause">The WHERE clause, without the <c>WHERE</c> keyword.</param>
        /// <returns>True when the clause always holds by its literals.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="whereClause"/> is null.</exception>
        public static bool HasLiteralTautology(string whereClause) => HasLiteralTautology(whereClause, LargeStackSize);

        /// <summary>
        /// <see cref="HasLiteralTautology(string)"/>, parsing a clause too deep for the calling thread again on a stack
        /// of <paramref name="largeStackSize"/> bytes. Tests pass a small one: how many levels fit on a stack depends on
        /// how the JIT has compiled the parser by then, so only a stack far too small for the clause is too small on
        /// every run.
        /// </summary>
        internal static bool HasLiteralTautology(string whereClause, int largeStackSize)
        {
            if (whereClause == null)
                throw new ArgumentNullException(nameof(whereClause));

            if (OneEqualsOne.IsMatch(MaskClosedQuotes(whereClause)))
                return true;

            var tokens = Tokenize(whereClause);
            if (tokens == null)
                return false;
            try
            {
                return new ClauseParser(tokens).Parse() == Truth.True;
            }
            catch (InsufficientExecutionStackException)
            {
                // Nested too deeply for this thread's stack: parse it again on a thread with a large one.
                return ParseOnLargeStack(tokens, largeStackSize);
            }
        }

        private const int LargeStackSize = 64 * 1024 * 1024;

        /// <summary>
        /// Folds <paramref name="tokens"/> on a new thread with a <paramref name="stackSize"/>-byte stack (64 MB from
        /// <see cref="HasLiteralTautology(string)"/>), for a clause nested too deeply for the calling thread's. A clause
        /// too deep even for that, or a platform that can't start the thread, gives false, as a clause that doesn't
        /// parse does.
        /// </summary>
        private static bool ParseOnLargeStack(List<Token> tokens, int stackSize)
        {
            var result = false;
            Exception? failure = null;
            try
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        result = new ClauseParser(tokens).Parse() == Truth.True;
                    }
                    catch (InsufficientExecutionStackException)
                    {
                        result = false;
                    }
                    catch (Exception ex)
                    {
                        // Raised again on the calling thread, as the parse would have raised it there; left here,
                        // it would end the process.
                        failure = ex;
                    }
                }, stackSize);
                thread.Start();
                thread.Join();
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException || ex is OutOfMemoryException)
            {
                return false;
            }
            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
            return result;
        }

        #region Expression tree

        private enum Truth { Unknown, False, True }

        /// <summary>
        /// Which self-comparisons a fold counts as holding. <see cref="Sql"/> decides the SQL every path sends, so it
        /// folds only where C# answers the same for every row: a direct property, not <c>float</c>/<c>double</c>
        /// (NaN), and not a nullable's <c>&gt;=</c>/<c>&lt;=</c> (false for null, true otherwise); a nullable's
        /// <c>&lt;</c>/<c>&gt;</c> is false for every row and folds. <see cref="Verdict"/> decides whether a mass
        /// mutation would touch every row, or every non-null row, and counts any member chain of the entity compared
        /// with itself, by any operator, as 3.10 did; <see cref="VerdictWithUnboundedSearch"/> also counts an empty
        /// search value on a column as true. A memo belongs to one rule set; the verdict folds never take one.
        /// </summary>
        private enum FoldRules { Sql, Verdict, VerdictWithUnboundedSearch }

        private static Truth FromValue(object? value) =>
            value is bool b ? (b ? Truth.True : Truth.False) : Truth.Unknown;

        private static Truth Not(Truth value) =>
            value == Truth.True ? Truth.False : value == Truth.False ? Truth.True : Truth.Unknown;

        private static Truth And(Truth left, Truth right) =>
            left == Truth.False || right == Truth.False ? Truth.False
            : left == Truth.True && right == Truth.True ? Truth.True
            : Truth.Unknown;

        private static Truth Or(Truth left, Truth right) =>
            left == Truth.True || right == Truth.True ? Truth.True
            : left == Truth.False && right == Truth.False ? Truth.False
            : Truth.Unknown;

        private static bool IsBoolean(Type type) => type == typeof(bool) || type == typeof(bool?);

        /// <summary>
        /// The truth of <paramref name="expression"/>. The logical operators, <c>!</c>, casts and the conditional
        /// operator compose their operands' truths (the composition equals C#'s short-circuit evaluation wherever a
        /// part can be evaluated at all); a leaf (a constant, a comparison, a call, a member) that reads no parameter is
        /// evaluated. <paramref name="rules"/> says which self-comparisons count as holding and whether an empty search
        /// value on a column does (<see cref="FoldRules"/>). The optional <paramref name="memo"/> is read and written
        /// for every node, so the visitors' folds of the body and of each truth position inside it are one pass
        /// (review R-1, A-4).
        /// </summary>
        private static Truth FoldTruth(Expression expression, Type entityType, FoldRules rules, IDictionary<Expression, bool?>? memo)
        {
            if (memo != null && memo.TryGetValue(expression, out var known))
                return FromNullable(known);
            var truth = FoldNode(expression, entityType, rules, memo);
            if (memo != null)
                memo[expression] = ToNullable(truth);
            return truth;
        }

        private static Truth FoldNode(Expression expression, Type entityType, FoldRules rules, IDictionary<Expression, bool?>? memo)
        {
            if (!IsBoolean(expression.Type))
                return Truth.Unknown;
            if (expression is ConstantExpression constant)
                return FromValue(constant.Value);

            switch (expression.NodeType)
            {
                case ExpressionType.Not:
                {
                    var unary = (UnaryExpression)expression;
                    return unary.Method == null ? Not(FoldTruth(unary.Operand, entityType, rules, memo)) : Truth.Unknown;
                }
                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                {
                    var unary = (UnaryExpression)expression;
                    var operand = unary.Method == null ? FoldTruth(unary.Operand, entityType, rules, memo) : Truth.Unknown;
                    // A cast whose operand isn't bool (a bool unboxed from a captured object) composes nothing: the cast
                    // itself is the leaf to read (review R-2, A/N-1).
                    return operand != Truth.Unknown ? operand : EvaluateLeaf(expression);
                }
                case ExpressionType.AndAlso:
                case ExpressionType.And:
                {
                    var binary = (BinaryExpression)expression;
                    return binary.Method == null
                        ? And(FoldTruth(binary.Left, entityType, rules, memo), FoldTruth(binary.Right, entityType, rules, memo))
                        : Truth.Unknown;
                }
                case ExpressionType.OrElse:
                case ExpressionType.Or:
                {
                    var binary = (BinaryExpression)expression;
                    return binary.Method == null
                        ? Or(FoldTruth(binary.Left, entityType, rules, memo), FoldTruth(binary.Right, entityType, rules, memo))
                        : Truth.Unknown;
                }
                case ExpressionType.Conditional:
                {
                    var conditional = (ConditionalExpression)expression;
                    var test = FoldTruth(conditional.Test, entityType, rules, memo);
                    if (test == Truth.True)
                        return FoldTruth(conditional.IfTrue, entityType, rules, memo);
                    if (test == Truth.False)
                        return FoldTruth(conditional.IfFalse, entityType, rules, memo);
                    var ifTrue = FoldTruth(conditional.IfTrue, entityType, rules, memo);
                    return ifTrue == FoldTruth(conditional.IfFalse, entityType, rules, memo) ? ifTrue : Truth.Unknown;
                }
                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                {
                    // A bool compared with a bool composes its operands' truths (review R-4, round-3 A/N-1): an empty
                    // search or a self-comparison in a value position (`x.Name.Contains("") == true`) is then seen by
                    // the verdict, as 3.10.1's tree walk saw it. An operand that reads the row keeps the whole unknown.
                    var binary = (BinaryExpression)expression;
                    if (binary.Method == null && IsBoolean(binary.Left.Type) && IsBoolean(binary.Right.Type))
                    {
                        var left = FoldTruth(binary.Left, entityType, rules, memo);
                        var right = left == Truth.Unknown ? Truth.Unknown : FoldTruth(binary.Right, entityType, rules, memo);
                        if (left != Truth.Unknown && right != Truth.Unknown)
                            return (left == right) == (expression.NodeType == ExpressionType.Equal) ? Truth.True : Truth.False;
                    }
                    return IsSelfComparison(expression, entityType, broad: rules != FoldRules.Sql)
                        ? (expression.NodeType == ExpressionType.Equal ? Truth.True : Truth.False)
                        : EvaluateLeaf(expression);
                }
                case ExpressionType.GreaterThanOrEqual:
                case ExpressionType.LessThanOrEqual:
                    return IsSelfComparison(expression, entityType, broad: rules != FoldRules.Sql) ? Truth.True : EvaluateLeaf(expression);
                case ExpressionType.GreaterThan:
                case ExpressionType.LessThan:
                    return IsSelfComparison(expression, entityType, broad: rules != FoldRules.Sql) ? Truth.False : EvaluateLeaf(expression);
                case ExpressionType.Call:
                    return rules == FoldRules.VerdictWithUnboundedSearch && IsUnboundedSearch((MethodCallExpression)expression, entityType)
                        ? Truth.True
                        : EvaluateLeaf(expression);
                default:
                    return EvaluateLeaf(expression);
            }
        }

        /// <summary>A leaf's truth: evaluated when it reads no parameter and is built from what <see cref="IsEvaluable"/> allows; else unknown.</summary>
        private static Truth EvaluateLeaf(Expression expression) =>
            IsEvaluable(expression) ? Evaluate(expression) : Truth.Unknown;

        /// <summary>
        /// A string <c>Contains</c>, <c>StartsWith</c> or <c>EndsWith</c> on a property of the entity (a direct one: on a
        /// member of a member the translators reject the receiver, on every path; review R-2, A/N-3) whose search value
        /// reads no parameter and is the empty string: <c>LIKE '%%'</c>, which matches every non-null row.
        /// </summary>
        private static bool IsUnboundedSearch(MethodCallExpression call, Type entityType)
        {
            if (call.Object == null || call.Method.DeclaringType != typeof(string) || call.Arguments.Count == 0
                || call.Arguments[0].Type != typeof(string))
                return false;
            var name = call.Method.Name;
            if (name != "Contains" && name != "StartsWith" && name != "EndsWith")
                return false;
            if (MemberChain(call.Object, entityType)?.Count != 1)
                return false;
            var argument = call.Arguments[0];
            return IsEvaluable(argument) && TryRead(argument, out var value) && value is string text && text.Length == 0;
        }

        /// <summary>
        /// True when <paramref name="expression"/> has no parameter and is built only from constants, field and
        /// property reads, casts, <c>!</c>, the logical operators, comparisons, the conditional operator and the calls
        /// <see cref="IsEvaluableCall"/> allows, with no operator or conversion method declared outside the core library.
        /// </summary>
        private static bool IsEvaluable(Expression? expression)
        {
            switch (expression)
            {
                case ConstantExpression _:
                    return true;
                case MemberExpression member:
                    return (member.Member is FieldInfo || member.Member is PropertyInfo)
                           && (member.Expression == null || IsEvaluable(member.Expression));
                case UnaryExpression unary when unary.NodeType == ExpressionType.Convert
                                                || unary.NodeType == ExpressionType.ConvertChecked
                                                || unary.NodeType == ExpressionType.Not:
                    return IsCoreLibraryMethod(unary.Method) && IsEvaluable(unary.Operand);
                case BinaryExpression binary when IsLogicalOrComparison(binary.NodeType):
                    return IsCoreLibraryMethod(binary.Method) && IsEvaluable(binary.Left) && IsEvaluable(binary.Right);
                case ConditionalExpression conditional:
                    return IsEvaluable(conditional.Test) && IsEvaluable(conditional.IfTrue)
                           && IsEvaluable(conditional.IfFalse);
                case MethodCallExpression call when IsEvaluableCall(call):
                {
                    if (!IsEvaluable(call.Object))
                        return false;
                    foreach (var argument in call.Arguments)
                    {
                        if (!IsEvaluable(argument))
                            return false;
                    }
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>
        /// The method calls every provider answers as C# does when their operands are parameter-free: <see cref="string"/>'s
        /// <c>Contains</c>, and <c>ToString()</c>, with no argument or a format string, on an enum or on a non-generic,
        /// sealed or value type of the core library, or a nullable one of those, so that no override outside the core
        /// library runs.
        /// </summary>
        private static bool IsEvaluableCall(MethodCallExpression call)
        {
            if (call.Object == null)
                return false;
            var receiver = call.Object.Type;
            if (receiver == typeof(string) && call.Method.DeclaringType == typeof(string) && call.Method.Name == "Contains")
                return true;
            if (call.Method.Name != "ToString")
                return false;
            foreach (var parameter in call.Method.GetParameters())
            {
                // A format string only: an IFormatProvider argument could be the caller's own code.
                if (parameter.ParameterType != typeof(string))
                    return false;
            }
            var type = Nullable.GetUnderlyingType(receiver) ?? receiver;
            return type.IsEnum
                   || (type.Assembly == CoreLibrary && !type.IsGenericType && (type.IsValueType || type.IsSealed));
        }

        private static bool IsLogicalOrComparison(ExpressionType nodeType)
        {
            switch (nodeType)
            {
                case ExpressionType.AndAlso:
                case ExpressionType.OrElse:
                case ExpressionType.And:
                case ExpressionType.Or:
                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsCoreLibraryMethod(MethodInfo? method) =>
            method == null || method.DeclaringType?.Assembly == CoreLibrary;

        private static Truth Evaluate(Expression expression) =>
            TryRead(expression, out var value) ? FromValue(value) : Truth.Unknown;

        private static bool TryRead(Expression expression, out object? value)
        {
            try
            {
                // Interpreted where the runtime can (as RowIndependentEvaluator does): a one-shot read doesn't repay a JIT compile.
                var lambda = Expression.Lambda<Func<object?>>(Expression.Convert(expression, typeof(object)));
                var read = (Func<object?>)Linq.RowIndependentEvaluator.Compile(lambda, Linq.RowIndependentEvaluator.InterpretingCompile);
                value = read();
                return true;
            }
            catch (Exception)
            {
                // A read that throws proves nothing; the translation reads it again and reports the error.
                value = null;
                return false;
            }
        }

        private static Expression StripConvert(Expression expression)
        {
            while (expression.NodeType == ExpressionType.Convert || expression.NodeType == ExpressionType.ConvertChecked)
                expression = ((UnaryExpression)expression).Operand;
            return expression;
        }

        private static Expression StripNotAndConvert(Expression expression)
        {
            while (expression.NodeType == ExpressionType.Not || expression.NodeType == ExpressionType.Convert
                   || expression.NodeType == ExpressionType.ConvertChecked)
                expression = ((UnaryExpression)expression).Operand;
            return expression;
        }

        /// <summary>
        /// The members of a chain of reads that ends at a parameter of the entity type, through casts and an outermost
        /// <c>ToString()</c> with no argument, outermost first; null when <paramref name="expression"/> isn't one.
        /// </summary>
        private static List<MemberInfo>? MemberChain(Expression expression, Type entityType)
        {
            var members = new List<MemberInfo>();
            var current = StripConvert(expression);
            if (current is MethodCallExpression call && call.Method.Name == nameof(ToString) && call.Object != null
                && call.Arguments.Count == 0)
                current = StripConvert(call.Object);
            while (current is MemberExpression member && member.Expression != null)
            {
                members.Add(member.Member);
                current = StripConvert(member.Expression);
            }
            return members.Count > 0 && current is ParameterExpression parameter && parameter.Type == entityType
                ? members
                : null;
        }

        /// <summary>
        /// Whether <paramref name="expression"/> compares a member chain of the entity with the same chain. With
        /// <paramref name="broad"/> (the verdict's rule, 3.10's), any chain, type and operator; without it (the rule for
        /// the SQL every path sends), only where C# answers the same for every row: a direct property (a member of a
        /// member is the translator's to accept or reject; review R-1, A-3), not <c>float</c>/<c>double</c> (NaN isn't
        /// equal to itself; A-2), and not a nullable's <c>&gt;=</c>/<c>&lt;=</c> (false for a null operand, true
        /// otherwise, which translating it as written reproduces; A-2). A nullable's <c>&lt;</c>/<c>&gt;</c> is false for
        /// every row, null included, so it does fold (review R-2, A/N-4: <c>!(x.N &lt; x.N)</c> keeps the null rows, as C# does).
        /// </summary>
        private static bool IsSelfComparison(Expression expression, Type entityType, bool broad)
        {
            switch (expression.NodeType)
            {
                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                    break;
                default:
                    return false;
            }
            var binary = (BinaryExpression)expression;
            var left = MemberChain(binary.Left, entityType);
            var right = MemberChain(binary.Right, entityType);
            if (left == null || right == null || !SameChain(left, right))
                return false;
            if (broad)
                return true;

            if (left.Count != 1)
                return false;
            var type = binary.Left.Type;
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (underlying == typeof(double) || underlying == typeof(float))
                return false;
            var orEqual = expression.NodeType == ExpressionType.GreaterThanOrEqual || expression.NodeType == ExpressionType.LessThanOrEqual;
            return !(orEqual && Nullable.GetUnderlyingType(type) != null);
        }

        private static bool SameChain(List<MemberInfo> left, List<MemberInfo> right)
        {
            if (left.Count != right.Count)
                return false;
            for (var i = 0; i < left.Count; i++)
            {
                if (!SameMember(left[i], right[i]))
                    return false;
            }
            return true;
        }

        private static bool SameMember(MemberInfo left, MemberInfo right) =>
            left == right || (left.Module == right.Module && left.MetadataToken == right.MetadataToken);

        /// <summary>Finds a member chain that ends at a parameter of the entity type, anywhere in a tree.</summary>
        private sealed class MemberChainFinder : ExpressionVisitor
        {
            private readonly Type _entityType;

            public MemberChainFinder(Type entityType) => _entityType = entityType;

            public bool Found { get; private set; }

            protected override Expression VisitMember(MemberExpression node)
            {
                if (!Found && MemberChain(node, _entityType) != null)
                    Found = true;
                return base.VisitMember(node);
            }
        }

        #endregion

        #region WHERE clause text

        private static char ClosingQuote(char opening)
        {
            switch (opening)
            {
                case '\'': return '\'';
                case '"': return '"';
                case '`': return '`';
                case '[': return ']';
                default: return '\0';
            }
        }

        /// <summary>
        /// The index of the quote that closes the segment opened at <paramref name="start"/>, where a doubled closing
        /// quote is an escape; -1 when the segment isn't closed.
        /// </summary>
        private static int FindClose(string text, int start, char close)
        {
            for (var i = start + 1; i < text.Length; i++)
            {
                if (text[i] != close)
                    continue;
                if (i + 1 < text.Length && text[i + 1] == close)
                {
                    i++;
                    continue;
                }
                return i;
            }
            return -1;
        }

        /// <summary>
        /// Replaces each closed quoted segment with a word character, so a <c>1=1</c> inside it isn't seen. An unclosed
        /// quote masks nothing.
        /// </summary>
        private static string MaskClosedQuotes(string text)
        {
            var masked = new StringBuilder(text.Length);
            var i = 0;
            while (i < text.Length)
            {
                var close = ClosingQuote(text[i]);
                if (close != '\0')
                {
                    var end = FindClose(text, i, close);
                    if (end < 0)
                    {
                        masked.Append(text, i, text.Length - i);
                        break;
                    }
                    masked.Append('_');
                    i = end + 1;
                    continue;
                }
                masked.Append(text[i]);
                i++;
            }
            return masked.ToString();
        }

        private enum TokenKind { Word, Number, Operator, Open, Close, Quoted, Other }

        private sealed class Token
        {
            public Token(TokenKind kind, string text, decimal value = 0m)
            {
                Kind = kind;
                Text = text;
                Value = value;
            }

            public TokenKind Kind { get; }
            public string Text { get; }
            public decimal Value { get; }

            public bool Is(string keyword) =>
                Kind == TokenKind.Word && string.Equals(Text, keyword, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWordCharacter(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '$' || c == '.' || c == ':';

        /// <summary>The clause's tokens; null when a quoted segment isn't closed.</summary>
        private static List<Token>? Tokenize(string text)
        {
            var tokens = new List<Token>();
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                var close = ClosingQuote(c);
                if (close != '\0')
                {
                    var end = FindClose(text, i, close);
                    if (end < 0)
                        return null;
                    tokens.Add(new Token(TokenKind.Quoted, text.Substring(i, end - i + 1)));
                    i = end + 1;
                    continue;
                }

                if (c == '(' || c == ')')
                {
                    tokens.Add(new Token(c == '(' ? TokenKind.Open : TokenKind.Close, c.ToString()));
                    i++;
                    continue;
                }

                if (IsWordCharacter(c))
                {
                    var start = i;
                    while (i < text.Length && IsWordCharacter(text[i]))
                        i++;
                    var word = text.Substring(start, i - start);
                    tokens.Add(NumberLiteral.IsMatch(word)
                               && decimal.TryParse(word, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                        ? new Token(TokenKind.Number, word, value)
                        : new Token(TokenKind.Word, word));
                    continue;
                }

                if (c == '<' || c == '>' || c == '=' || c == '!')
                {
                    var pair = i + 1 < text.Length ? text.Substring(i, 2) : null;
                    if (pair == "<=" || pair == ">=" || pair == "<>" || pair == "!=")
                    {
                        tokens.Add(new Token(TokenKind.Operator, pair));
                        i += 2;
                        continue;
                    }
                    tokens.Add(new Token(c == '!' ? TokenKind.Other : TokenKind.Operator, c.ToString()));
                    i++;
                    continue;
                }

                tokens.Add(new Token(TokenKind.Other, c.ToString()));
                i++;
            }
            return tokens;
        }

        /// <summary>
        /// Folds a WHERE clause's tokens to true, false or unknown:
        /// <c>or := and (OR and)*</c>, <c>and := not (AND not)*</c>, <c>not := NOT not | primary</c>,
        /// <c>primary := group | run</c>. A group is a parenthesised <c>or</c> that stands as a whole term; a run is any
        /// other sequence of tokens, absorbing balanced parentheses and <c>CASE … END</c>, and is known only when it is
        /// exactly <c>number operator number</c>. Null means the clause doesn't parse.
        /// </summary>
        private sealed class ClauseParser
        {
            private readonly List<Token> _tokens;
            private int _position;

            // For each '(' and CASE, the index of its ')' or END; -1 when it has none.
            private readonly int[] _match;

            public ClauseParser(List<Token> tokens)
            {
                _tokens = tokens;
                _match = new int[tokens.Count];
                var groups = new Stack<int>();
                var cases = new Stack<int>();
                for (var i = 0; i < tokens.Count; i++)
                {
                    _match[i] = -1;
                    var token = tokens[i];
                    if (token.Kind == TokenKind.Open)
                        groups.Push(i);
                    else if (token.Kind == TokenKind.Close && groups.Count > 0)
                        _match[groups.Pop()] = i;
                    else if (token.Is("CASE"))
                        cases.Push(i);
                    else if (token.Is("END") && cases.Count > 0)
                        _match[cases.Pop()] = i;
                }
            }

            public Truth? Parse()
            {
                var value = ParseOr();
                return value != null && _position == _tokens.Count ? value : null;
            }

            private Token? Peek => _position < _tokens.Count ? _tokens[_position] : null;

            private Truth? ParseOr()
            {
                var value = ParseAnd();
                while (value != null && Peek?.Is("OR") == true)
                {
                    _position++;
                    var right = ParseAnd();
                    if (right == null)
                        return null;
                    value = Or(value.Value, right.Value);
                }
                return value;
            }

            private Truth? ParseAnd()
            {
                var value = ParseNot();
                while (value != null && Peek?.Is("AND") == true)
                {
                    _position++;
                    var right = ParseNot();
                    if (right == null)
                        return null;
                    value = And(value.Value, right.Value);
                }
                return value;
            }

            private Truth? ParseNot()
            {
                // Every nesting level (a group or a NOT) passes through here; throws before the stack runs out.
                RuntimeHelpers.EnsureSufficientExecutionStack();
                if (Peek?.Is("NOT") == true)
                {
                    _position++;
                    var operand = ParseNot();
                    return operand == null ? (Truth?)null : Not(operand.Value);
                }
                return ParsePrimary();
            }

            private Truth? ParsePrimary()
            {
                if (Peek?.Kind == TokenKind.Open)
                {
                    var close = MatchingClose(_position);
                    if (close < 0)
                        return null;
                    var after = close + 1 < _tokens.Count ? _tokens[close + 1] : null;
                    if (after == null || after.Is("AND") || after.Is("OR") || after.Kind == TokenKind.Close)
                    {
                        _position++;
                        var value = ParseOr();
                        if (value == null || _position != close)
                            return null;
                        _position = close + 1;
                        return value;
                    }
                }
                return ParseRun();
            }

            private Truth? ParseRun()
            {
                var start = _position;
                while (_position < _tokens.Count)
                {
                    var token = _tokens[_position];
                    if (token.Is("AND") || token.Is("OR") || token.Kind == TokenKind.Close)
                        break;
                    if (token.Kind == TokenKind.Open)
                    {
                        var close = MatchingClose(_position);
                        if (close < 0)
                            return null;
                        _position = close + 1;
                        continue;
                    }
                    if (token.Is("CASE"))
                    {
                        var end = MatchingEnd(_position);
                        if (end < 0)
                            return null;
                        _position = end + 1;
                        continue;
                    }
                    _position++;
                }
                if (_position == start)
                    return null;
                return _position - start == 3 ? Compare(_tokens[start], _tokens[start + 1], _tokens[start + 2]) : Truth.Unknown;
            }

            private int MatchingClose(int open) => _match[open];

            private int MatchingEnd(int caseIndex) => _match[caseIndex];

            private static Truth Compare(Token left, Token op, Token right)
            {
                if (left.Kind != TokenKind.Number || op.Kind != TokenKind.Operator || right.Kind != TokenKind.Number)
                    return Truth.Unknown;
                var comparison = left.Value.CompareTo(right.Value);
                switch (op.Text)
                {
                    case "=": return comparison == 0 ? Truth.True : Truth.False;
                    case "<>":
                    case "!=": return comparison != 0 ? Truth.True : Truth.False;
                    case "<": return comparison < 0 ? Truth.True : Truth.False;
                    case ">": return comparison > 0 ? Truth.True : Truth.False;
                    case "<=": return comparison <= 0 ? Truth.True : Truth.False;
                    default: return comparison >= 0 ? Truth.True : Truth.False; // ">=", the remaining operator token
                }
            }
        }

        #endregion
    }
}
