using System;
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
        /// (<see cref="System.Linq.IQueryable"/>-typed) expression whose result type can hold a
        /// <c>List&lt;memberType&gt;</c>.
        /// </summary>
        public static void EnsureCollectionResult(Expression expression, Type resultType, Type memberType) =>
            throw new NotImplementedException("ScalarProjectionGuard.EnsureCollectionResult is not implemented yet (3.10 Task 4).");
    }
}
