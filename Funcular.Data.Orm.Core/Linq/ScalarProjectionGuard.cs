using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Funcular.Data.Orm.Linq
{
    /// <summary>
    /// Guards the execution of a top-level scalar projection (<c>Select(x =&gt; x.Member)</c>), which supports
    /// enumeration only: any terminal operator over it is rejected.
    /// </summary>
    public static class ScalarProjectionGuard
    {
        /// <summary>
        /// Throws <see cref="NotSupportedException"/> unless <paramref name="expression"/> is a collection
        /// (<see cref="IQueryable"/>-typed) expression whose result type can hold a <c>List&lt;memberType&gt;</c>.
        /// Collection-ness is decided by the expression's shape, never by <paramref name="resultType"/>: a terminal
        /// such as <c>First</c> over an <c>IQueryable&lt;object&gt;</c> view can request <c>object</c>, which a list
        /// would satisfy, but must not be answered with the whole list.
        /// </summary>
        public static void EnsureCollectionResult(Expression expression, Type resultType, Type memberType)
        {
            if (expression == null)
                throw new ArgumentNullException(nameof(expression));
            if (resultType == null)
                throw new ArgumentNullException(nameof(resultType));
            if (memberType == null)
                throw new ArgumentNullException(nameof(memberType));

            var isCollection = typeof(IQueryable).IsAssignableFrom(expression.Type);
            var listType = typeof(List<>).MakeGenericType(memberType);
            if (isCollection && resultType.IsAssignableFrom(listType))
                return;

            var op = (expression as MethodCallExpression)?.Method.Name;
            var opText = (op != null && op != "Select") ? $" followed by {op}()" : "";
            throw new NotSupportedException(
                $"A scalar projection Select(x => x.Member){opText} is only supported for a list/enumeration " +
                "result in this version. Materialize then apply the operator in memory " +
                "(query.Select(x => x.Member).ToList()...), or aggregate off the base query.");
        }
    }
}
