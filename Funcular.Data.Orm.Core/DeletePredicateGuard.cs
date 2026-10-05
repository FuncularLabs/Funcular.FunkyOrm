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
    /// The verdict <see cref="DeletePredicateGuard.Classify"/> gives a delete predicate.
    /// </summary>
    public enum DeletePredicateVerdict
    {
        /// <summary>The predicate may be translated and run.</summary>
        Acceptable,

        /// <summary>The predicate references no column of the entity.</summary>
        NoColumn,

        /// <summary>The predicate is a comparison of a member with itself that always holds.</summary>
        SelfReference,

        /// <summary>The predicate always holds.</summary>
        AlwaysTrue
    }

    /// <summary>
    /// Rejects delete-by-predicate calls whose predicate would match every row. Each provider's
    /// <c>Delete&lt;T&gt;(predicate)</c> and <c>DeleteAsync&lt;T&gt;(predicate)</c> call <see cref="Validate"/> before
    /// translating the predicate, and <see cref="HasLiteralTautology"/> on the WHERE clause the translation produces.
    /// A custom provider can call them too.
    /// <para>The guard rejects the shapes it can prove always hold. It doesn't prove that every always-true predicate
    /// is one: <c>x =&gt; x.Id == 2 || x.Id != 2</c>, for example, is accepted.</para>
    /// </summary>
    public static class DeletePredicateGuard
    {
        private const string NoColumnMessage =
            "Delete operation WHERE clause must reference at least one column from the target table.";
        private const string SelfReferenceMessage =
            "Delete operation WHERE clause cannot be a self-referencing column expression.";
        private const string AlwaysTrueMessage = "Delete operation requires a non-trivial WHERE clause.";

        private static readonly Assembly CoreLibrary = typeof(object).Assembly;

        // A 1=1 that isn't part of a longer token: not glued to a word character, @, $, . or :.
        private static readonly Regex OneEqualsOne =
            new Regex(@"(?<![\w@$.:])1\s*=\s*1(?![\w@$.:])", RegexOptions.CultureInvariant);

        private static readonly Regex NumberLiteral = new Regex(@"^[0-9]+(\.[0-9]+)?$", RegexOptions.CultureInvariant);

        /// <summary>
        /// Classifies <paramref name="predicate"/> before it is translated.
        /// <list type="bullet">
        /// <item><see cref="DeletePredicateVerdict.NoColumn"/>: the body reads no member of the entity parameter
        /// (<c>x =&gt; true</c>, <c>x =&gt; 1 &lt; 2</c>, a captured <c>bool</c>).</item>
        /// <item><see cref="DeletePredicateVerdict.SelfReference"/>: the body always holds and is a comparison of a
        /// member with itself by <c>==</c>, <c>&gt;=</c> or <c>&lt;=</c> (also through a cast), or the negation of one by
        /// <c>!=</c>, <c>&gt;</c> or <c>&lt;</c>.</item>
        /// <item><see cref="DeletePredicateVerdict.AlwaysTrue"/>: the body always holds otherwise, for example a
        /// disjunction with a <c>true</c> literal, a captured or static <c>true</c>, a <c>true</c> property of a captured
        /// object, a comparison of constants and captured values that holds, or a <c>Contains</c> on a captured string
        /// that holds (<c>roles.Contains("admin") || …</c>).</item>
        /// <item><see cref="DeletePredicateVerdict.Acceptable"/>: anything else.</item>
        /// </list>
        /// Parameter-free <c>bool</c> parts built from constants, field and property reads, casts, the logical
        /// operators, comparisons, the conditional operator, <see cref="string"/>'s <c>Contains</c>, and <c>ToString()</c>
        /// with no argument or a format string on a receiver declared as an enum or a non-generic, sealed or value type of
        /// the core library (or a nullable one) are evaluated, as C# evaluates them; a receiver declared as
        /// <see cref="object"/> or an interface isn't, whatever it holds. A <c>Contains</c> whose search value is null, on a
        /// receiver that isn't, counts as true, as every provider sends it as <c>LIKE '%%'</c>. A part that calls any
        /// other method, a constructor, or an operator or conversion declared outside the core library is never
        /// evaluated, and counts as unknown, as does any other part whose evaluation throws. So no code outside the core
        /// library runs, except property getters.
        /// </summary>
        /// <param name="predicate">A lambda with one parameter, the entity, and a <c>bool</c> body.</param>
        /// <returns>The verdict.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="predicate"/> doesn't have exactly one parameter, or its
        /// body isn't <c>bool</c>.</exception>
        public static DeletePredicateVerdict Classify(LambdaExpression predicate)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));
            if (predicate.Parameters.Count != 1)
                throw new ArgumentException("A delete predicate must have exactly one parameter, the entity.", nameof(predicate));
            if (predicate.Body.Type != typeof(bool))
                throw new ArgumentException("A delete predicate's body must be bool.", nameof(predicate));

            var entityType = predicate.Parameters[0].Type;
            var finder = new MemberChainFinder(entityType);
            finder.Visit(predicate.Body);
            if (!finder.Found)
                return DeletePredicateVerdict.NoColumn;

            if (Fold(predicate.Body, entityType) != Truth.True)
                return DeletePredicateVerdict.Acceptable;

            return IsSelfComparison(StripNotAndConvert(predicate.Body), entityType)
                ? DeletePredicateVerdict.SelfReference
                : DeletePredicateVerdict.AlwaysTrue;
        }

        /// <summary>
        /// Throws <see cref="InvalidOperationException"/> with the message of the verdict when
        /// <see cref="Classify"/> rejects <paramref name="predicate"/>. When it is
        /// <see cref="DeletePredicateVerdict.Acceptable"/>, throws <see cref="NotSupportedException"/> for a shape the
        /// providers can send so that it matches other rows than C# selects, often every row: a <see cref="string"/>
        /// <c>Contains</c>, <c>StartsWith</c> or <c>EndsWith</c>, or a <c>ToString()</c>, on a value that doesn't read
        /// the row; a comparison of two strings neither of which reads the row (unless one is a <c>null</c> literal),
        /// which the database makes under its collation; or a <see cref="string"/> <c>Contains</c>, <c>StartsWith</c>
        /// or <c>EndsWith</c> on a column whose search value is null, empty, contains <c>%</c>, <c>_</c>, <c>[</c> or a
        /// backslash, or is a property of a captured object (which the providers read as null). Otherwise returns.
        /// </summary>
        /// <param name="predicate">A lambda with one parameter, the entity, and a <c>bool</c> body.</param>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="predicate"/> doesn't have exactly one parameter, or its
        /// body isn't <c>bool</c>.</exception>
        /// <exception cref="InvalidOperationException">The predicate is rejected.</exception>
        /// <exception cref="NotSupportedException">The predicate holds a shape a delete can't send safely.</exception>
        public static void Validate(LambdaExpression predicate)
        {
            switch (Classify(predicate))
            {
                case DeletePredicateVerdict.NoColumn:
                    throw new InvalidOperationException(NoColumnMessage);
                case DeletePredicateVerdict.SelfReference:
                    throw new InvalidOperationException(SelfReferenceMessage);
                case DeletePredicateVerdict.AlwaysTrue:
                    throw new InvalidOperationException(AlwaysTrueMessage);
            }
            var finder = new UnsafeDeleteCallFinder();
            finder.Visit(predicate.Body);
            if (finder.Message != null)
                throw new NotSupportedException(finder.Message);
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

        private static Truth Fold(Expression expression, Type entityType)
        {
            if (!IsBoolean(expression.Type))
                return Truth.Unknown;
            if (expression is ConstantExpression constant)
                return FromValue(constant.Value);
            if (IsEvaluable(expression))
            {
                var value = Evaluate(expression);
                if (value != Truth.Unknown)
                    return value;
                // It threw or read null; its parts may still be known (a Contains of null), so fold them below.
            }

            switch (expression.NodeType)
            {
                case ExpressionType.Call:
                    return IsContainsOfNull((MethodCallExpression)expression) ? Truth.True : Truth.Unknown;
                case ExpressionType.Not:
                {
                    var unary = (UnaryExpression)expression;
                    return unary.Method == null ? Not(Fold(unary.Operand, entityType)) : Truth.Unknown;
                }
                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                {
                    var unary = (UnaryExpression)expression;
                    return unary.Method == null ? Fold(unary.Operand, entityType) : Truth.Unknown;
                }
                case ExpressionType.AndAlso:
                case ExpressionType.And:
                {
                    var binary = (BinaryExpression)expression;
                    return binary.Method == null
                        ? And(Fold(binary.Left, entityType), Fold(binary.Right, entityType))
                        : Truth.Unknown;
                }
                case ExpressionType.OrElse:
                case ExpressionType.Or:
                {
                    var binary = (BinaryExpression)expression;
                    return binary.Method == null
                        ? Or(Fold(binary.Left, entityType), Fold(binary.Right, entityType))
                        : Truth.Unknown;
                }
                case ExpressionType.Conditional:
                {
                    var conditional = (ConditionalExpression)expression;
                    var test = Fold(conditional.Test, entityType);
                    if (test == Truth.True)
                        return Fold(conditional.IfTrue, entityType);
                    if (test == Truth.False)
                        return Fold(conditional.IfFalse, entityType);
                    var ifTrue = Fold(conditional.IfTrue, entityType);
                    return ifTrue == Fold(conditional.IfFalse, entityType) ? ifTrue : Truth.Unknown;
                }
                case ExpressionType.Equal:
                case ExpressionType.GreaterThanOrEqual:
                case ExpressionType.LessThanOrEqual:
                    return IsSelfComparison(expression, entityType) ? Truth.True : Truth.Unknown;
                case ExpressionType.NotEqual:
                case ExpressionType.GreaterThan:
                case ExpressionType.LessThan:
                    return IsSelfComparison(expression, entityType) ? Truth.False : Truth.Unknown;
                default:
                    return Truth.Unknown;
            }
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
        /// The method calls every provider translates into SQL that uses only parameters when their operands are
        /// parameter-free: <see cref="string"/>'s <c>Contains</c>, and <c>ToString()</c>, with no argument or a format
        /// string, on an enum or on a non-generic, sealed or value type of the core library, or a nullable one of those,
        /// so that no override outside the core library runs.
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
                var read = Expression.Lambda<Func<object?>>(Expression.Convert(expression, typeof(object))).Compile();
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

        /// <summary>
        /// True for a parameter-free <c>string.Contains</c> whose search value is null on a receiver that isn't: C#
        /// throws, but every provider sends <c>LIKE '%%'</c>, which holds.
        /// </summary>
        private static bool IsContainsOfNull(MethodCallExpression call) =>
            IsEvaluable(call) && call.Method.DeclaringType == typeof(string) && call.Method.Name == "Contains"
            && call.Arguments[0].Type == typeof(string)
            && TryRead(call.Object!, out var receiver) && receiver != null
            && TryRead(call.Arguments[0], out var search) && search == null;

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

        private static bool IsSelfComparison(Expression expression, Type entityType)
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
            if (left == null || right == null || left.Count != right.Count)
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

        private static readonly char[] LikeWildcards = { '%', '_', '[', '\\' };

        /// <summary>
        /// Finds the first call or string comparison in a delete predicate that the providers can send so that it
        /// matches other rows than C# selects (see <see cref="Validate"/>), and holds its message.
        /// </summary>
        private sealed class UnsafeDeleteCallFinder : ExpressionVisitor
        {
            public string? Message { get; private set; }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                Message ??= Check(node);
                return Message == null ? base.VisitMethodCall(node) : node;
            }

            protected override Expression VisitBinary(BinaryExpression node)
            {
                if (Message == null && (node.NodeType == ExpressionType.Equal || node.NodeType == ExpressionType.NotEqual)
                    && node.Left.Type == typeof(string) && node.Right.Type == typeof(string)
                    && !IsNullLiteral(node.Left) && !IsNullLiteral(node.Right)
                    && !ParameterFinder.Reads(node.Left) && !ParameterFinder.Reads(node.Right))
                    Message = "Comparing strings that don't read the row isn't supported in a delete; the database " +
                              $"compares them under its collation, so compute it before the query: {node}";
                return Message == null ? base.VisitBinary(node) : node;
            }

            private static bool IsNullLiteral(Expression expression) =>
                expression is ConstantExpression constant && constant.Value == null;

            private static string? Check(MethodCallExpression node)
            {
                if (node.Object == null)
                    return null;
                var name = node.Method.Name;
                var isStringMatch = node.Method.DeclaringType == typeof(string)
                                    && (name == "Contains" || name == "StartsWith" || name == "EndsWith");
                if (!isStringMatch && name != nameof(ToString))
                    return null;
                if (!ParameterFinder.Reads(node.Object))
                    return $"{name}() on a value that doesn't read the row isn't supported in a delete; compute it " +
                           $"before the query: {node}";
                if (!isStringMatch || node.Arguments.Count == 0)
                    return null;
                var argument = node.Arguments[0];
                if (argument is MemberExpression member && member.Member is PropertyInfo
                    && member.Expression is ConstantExpression)
                    return $"{name}()'s search value is a property of a captured object, which the translation reads " +
                           $"as null; copy it to a local before the query: {node}";
                if (ParameterFinder.Reads(argument) || !IsEvaluable(argument) || !TryRead(argument, out var value))
                    return null;
                var text = value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
                if (string.IsNullOrEmpty(text))
                    return $"{name}()'s search value is null or empty, which can match every non-null row in a delete: " +
                           $"{node}";
                if (text!.IndexOfAny(LikeWildcards) >= 0)
                    return $"{name}()'s search value contains a LIKE wildcard (%, _, [ or a backslash), which is sent " +
                           $"unescaped, so the database can match other rows than C# does: {node}";
                return null;
            }
        }

        /// <summary>Whether an expression reads any lambda parameter.</summary>
        private sealed class ParameterFinder : ExpressionVisitor
        {
            private bool _found;

            public static bool Reads(Expression expression)
            {
                var finder = new ParameterFinder();
                finder.Visit(expression);
                return finder._found;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                _found = true;
                return node;
            }
        }

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
