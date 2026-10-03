using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Funcular.Data.Orm.Linq
{
    /// <summary>
    /// Which parameter-free expressions may be evaluated while a predicate is translated: the delete guard's rule, so
    /// that the translators evaluate a captured <c>ToString()</c> exactly when the guard can see what it evaluates to
    /// (docs/plans/WHERE_CAPTURED_VALUES_PLAN.md, D7 and D14).
    /// <para>
    /// The bodies of <see cref="IsEvaluable"/>, <see cref="IsEvaluableCall"/>, <see cref="IsLogicalOrComparison"/> and
    /// <see cref="IsCoreLibraryMethod"/> are copied verbatim from <c>DeletePredicateGuard</c> on
    /// <c>fix/delete-guard-trivial-predicates</c> at <c>ca82741</c> (<c>DeletePredicateGuard.cs:296-383</c>). When that
    /// branch merges, the guard uses this class and drops its copy (the plan's Task 6); until then a change to either
    /// copy must be made to both.
    /// </para>
    /// </summary>
    internal static class CoreEvaluability
    {
        private static readonly Assembly CoreLibrary = typeof(object).Assembly;

        /// <summary>
        /// True when <paramref name="expression"/> has no parameter and is built only from constants, field and
        /// property reads, casts, <c>!</c>, the logical operators, comparisons, the conditional operator and the calls
        /// <see cref="IsEvaluableCall"/> allows, with no operator or conversion method declared outside the core library.
        /// </summary>
        internal static bool IsEvaluable(Expression? expression)
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
        internal static bool IsEvaluableCall(MethodCallExpression call)
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
    }
}
