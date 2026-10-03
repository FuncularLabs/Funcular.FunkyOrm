using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Funcular.Data.Orm.Linq
{
    /// <summary>
    /// Evaluates, as C# does, the parts of a predicate that don't read the row, for the providers' translators: a
    /// <c>Contains</c> that doesn't read the row, a captured <c>ToString()</c>, and a captured value
    /// (docs/plans/WHERE_CAPTURED_VALUES_PLAN.md, D4–D10).
    /// </summary>
    public static class RowIndependentEvaluator
    {
        /// <summary>The SQL sent for a <c>Contains</c> that C# finds true: the negation of <see cref="FalseSql"/>.</summary>
        public const string TrueSql = "NOT 1=0";

        /// <summary>The SQL sent for a <c>Contains</c> that C# finds false, as for an empty collection.</summary>
        public const string FalseSql = "1=0";

        /// <summary>
        /// The SQL sent for a string <c>Contains</c> that doesn't read the row when its receiver or its value is null:
        /// SQL's unknown, which matches no row, negated or not.
        /// </summary>
        public const string UnknownSql = "NULL LIKE NULL";

        /// <summary>
        /// Whether <paramref name="expression"/> reads no parameter that it doesn't declare itself (a nested lambda's own
        /// parameters are declared), so that it can be evaluated without a row.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="expression"/> is null.</exception>
        public static bool IsRowIndependent(Expression expression)
        {
            if (expression == null)
                throw new ArgumentNullException(nameof(expression));
            var finder = new FreeParameterFinder();
            finder.Visit(expression);
            return !finder.Found;
        }

        /// <summary>
        /// When <paramref name="call"/> doesn't read the row, evaluates it as C# does and returns the SQL to send for it:
        /// <see cref="TrueSql"/>, <see cref="FalseSql"/>, or, for a string receiver or a string value that is null,
        /// <see cref="UnknownSql"/>. Returns false, with an empty <paramref name="sql"/> and nothing evaluated, when the
        /// call reads the row.
        /// </summary>
        /// <param name="call">A <c>bool</c>-returning method named <c>Contains</c>.</param>
        /// <param name="sql">The SQL to send.</param>
        /// <exception cref="ArgumentNullException"><paramref name="call"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="call"/> isn't a <c>bool</c>-returning <c>Contains</c>.</exception>
        /// <exception cref="NotSupportedException">Evaluating the call threw; the original exception is the inner one.</exception>
        public static bool TryTranslateContains(MethodCallExpression call, out string sql)
        {
            if (call == null)
                throw new ArgumentNullException(nameof(call));
            if (call.Method.Name != "Contains" || call.Type != typeof(bool))
                throw new ArgumentException("Expected a bool-returning method named Contains: " + call, nameof(call));

            sql = string.Empty;
            if (!IsRowIndependent(call))
                return false;

            bool result;
            if (call.Object != null && call.Object.Type == typeof(string))
            {
                // string's Contains: a null receiver or value matches no row (D7, D8); C# would throw.
                var receiver = Evaluate(call.Object, call);
                var parameters = call.Method.GetParameters();
                var arguments = new object?[call.Arguments.Count];
                var anyNullString = false;
                for (var i = 0; i < arguments.Length; i++)
                {
                    arguments[i] = Evaluate(call.Arguments[i], call);
                    if (arguments[i] == null && parameters[i].ParameterType == typeof(string))
                        anyNullString = true;
                }
                if (receiver == null || anyNullString)
                {
                    sql = UnknownSql;
                    return true;
                }
                result = (bool)Invoke(call.Method, receiver, arguments, call);
            }
            else
            {
                result = (bool)Evaluate(RewriteForEvaluation(call), call)!;
            }

            sql = result ? TrueSql : FalseSql;
            return true;
        }

        /// <summary>
        /// When <paramref name="call"/> doesn't read the row and the delete guard would evaluate it too (an instance
        /// <c>ToString</c> with no argument or a format string, on a receiver declared as an enum or a non-generic,
        /// sealed or value type of the core library, or a nullable one, built only from what the guard evaluates),
        /// evaluates the whole call, in the current culture, and returns the string C# produces. Returns false, with a
        /// null <paramref name="value"/> and nothing evaluated, otherwise.
        /// </summary>
        /// <param name="call">A method named <c>ToString</c> returning <c>string</c>.</param>
        /// <param name="value">The string C# produces (<c>""</c> for a nullable with no value).</param>
        /// <exception cref="ArgumentNullException"><paramref name="call"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="call"/> isn't a <c>ToString</c> returning <c>string</c>.</exception>
        /// <exception cref="NotSupportedException">Evaluating the call threw; the original exception is the inner one.</exception>
        public static bool TryEvaluateToString(MethodCallExpression call, out string? value)
        {
            if (call == null)
                throw new ArgumentNullException(nameof(call));
            if (call.Method.Name != "ToString" || call.Type != typeof(string))
                throw new ArgumentException("Expected a method named ToString returning string: " + call, nameof(call));

            value = null;
            // The guard's rule admits no parameter, so a call it evaluates never reads the row.
            if (!CoreEvaluability.IsEvaluable(call))
                return false;

            value = (string?)Evaluate(call, call);
            return true;
        }

        /// <summary>
        /// Reads a constant, or a chain of field and property reads that starts at a constant or a static member, as C#
        /// reads it. Returns false, with a null <paramref name="value"/> and nothing read, for any other expression.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="expression"/> is null.</exception>
        /// <exception cref="NotSupportedException">A read threw; the original exception is the inner one.</exception>
        public static bool TryReadValue(Expression expression, out object? value)
        {
            if (expression == null)
                throw new ArgumentNullException(nameof(expression));

            value = null;
            if (expression is ConstantExpression constant)
            {
                value = constant.Value;
                return true;
            }
            if (!IsRootedChain(expression))
                return false;

            value = Evaluate(expression, expression);
            return true;
        }

        /// <summary><c>LambdaExpression.Compile(bool)</c>, or null on a runtime without it (.NET Framework before 4.7.1).</summary>
        internal static readonly MethodInfo? InterpretingCompile =
            typeof(LambdaExpression).GetMethod("Compile", new[] { typeof(bool) });

        /// <summary>Turns every lambda this class evaluates into a delegate. Tests swap it and restore it.</summary>
        internal static Func<LambdaExpression, Delegate> Compiler { get; set; } = lambda => Compile(lambda, InterpretingCompile);

        /// <summary>
        /// Interprets <paramref name="lambda"/> through <paramref name="interpretingCompile"/>; compiles it when that is
        /// null. The only place this class turns a lambda into a delegate.
        /// </summary>
        internal static Delegate Compile(LambdaExpression lambda, MethodInfo? interpretingCompile) =>
            interpretingCompile == null
                ? lambda.Compile()
                : (Delegate)interpretingCompile.Invoke(lambda, new object[] { true })!;

        /// <summary>
        /// C# 14 binds <c>array.Contains(item)</c> to <c>MemoryExtensions.Contains(ReadOnlySpan&lt;T&gt;, T)</c>, which can't
        /// be interpreted; returns the same question asked of <c>Enumerable.Contains&lt;T&gt;</c> over the array (both use
        /// <c>EqualityComparer&lt;T&gt;.Default</c>), or <paramref name="call"/> itself for any other call.
        /// </summary>
        internal static Expression RewriteForEvaluation(MethodCallExpression call)
        {
            // Compared by name: netstandard2.0 has no MemoryExtensions without System.Memory.
            if (call.Object != null || call.Arguments.Count != 2
                || call.Method.DeclaringType?.FullName != "System.MemoryExtensions")
                return call;

            var source = UnwrapImplicit(call.Arguments[0]);
            var item = call.Arguments[1];
            if (source == null || !typeof(IEnumerable<>).MakeGenericType(item.Type).IsAssignableFrom(source.Type))
                return call;

            return Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), new[] { item.Type }, source, item);
        }

        /// <summary>The operand of an <c>op_Implicit</c> conversion, in either form an expression tree gives it; else null.</summary>
        private static Expression? UnwrapImplicit(Expression expression)
        {
            if (expression is MethodCallExpression conversion && conversion.Method.Name == "op_Implicit"
                && conversion.Arguments.Count == 1)
                return conversion.Arguments[0];
            if (expression is UnaryExpression unary && unary.Method?.Name == "op_Implicit")
                return unary.Operand;
            return null;
        }

        private static bool IsRootedChain(Expression expression)
        {
            var current = expression;
            while (current is MemberExpression member && (member.Member is FieldInfo || member.Member is PropertyInfo))
            {
                if (member.Expression == null)
                    return true; // a static member
                current = member.Expression;
            }
            return current is ConstantExpression;
        }

        /// <summary>Evaluates <paramref name="expression"/> through <see cref="Compiler"/>; failures are reported as <paramref name="reportAs"/>.</summary>
        private static object? Evaluate(Expression expression, Expression reportAs)
        {
            try
            {
                var lambda = Expression.Lambda<Func<object?>>(Expression.Convert(expression, typeof(object)));
                return ((Func<object?>)Compiler(lambda))();
            }
            catch (Exception ex)
            {
                throw CouldNotEvaluate(reportAs, ex);
            }
        }

        private static object Invoke(MethodInfo method, object receiver, object?[] arguments, Expression reportAs)
        {
            try
            {
                return method.Invoke(receiver, arguments)!;
            }
            catch (Exception ex)
            {
                throw CouldNotEvaluate(reportAs, ex);
            }
        }

        /// <summary>The visitor's message (WhereClauseVisitor), with the original exception inside (D9).</summary>
        private static NotSupportedException CouldNotEvaluate(Expression expression, Exception exception)
        {
            while (exception is TargetInvocationException invocation && invocation.InnerException != null)
                exception = invocation.InnerException;
            return new NotSupportedException("Could not evaluate expression: " + expression, exception);
        }

        /// <summary>Finds a parameter that the expression reads but doesn't declare in a nested lambda.</summary>
        private sealed class FreeParameterFinder : ExpressionVisitor
        {
            private readonly HashSet<ParameterExpression> _declared = new HashSet<ParameterExpression>();

            internal bool Found { get; private set; }

            protected override Expression VisitLambda<T>(Expression<T> node)
            {
                var added = node.Parameters.Where(p => _declared.Add(p)).ToList();
                var result = base.VisitLambda(node);
                foreach (var parameter in added)
                    _declared.Remove(parameter);
                return result;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (!_declared.Contains(node))
                    Found = true;
                return node;
            }
        }
    }
}
