using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Funcular.Data.Orm.Linq
{
    /// <summary>
    /// Exact-overload allow-list for the LINQ operators FunkyORM translates to SQL, applied as a pre-pass over
    /// the method spine of a query before any provider translates it. A rejected chain throws
    /// <see cref="NotSupportedException"/> naming the operator, and no query or aggregate command is executed.
    /// </summary>
    public static class QueryOperatorPolicy
    {
        /// <summary>
        /// The supported <see cref="System.Linq.Queryable"/> overloads, as generic method definitions.
        /// </summary>
        public static IReadOnlyCollection<MethodInfo> SupportedOperators =>
            throw new NotImplementedException("QueryOperatorPolicy.SupportedOperators is not implemented yet (3.10 Task 4).");

        /// <summary>
        /// Returns whether <paramref name="method"/> (or its generic method definition) is a supported
        /// <see cref="System.Linq.Queryable"/> overload. Non-<c>Queryable</c> methods and non-generic overloads are
        /// never supported.
        /// </summary>
        public static bool IsAllowed(MethodInfo method) =>
            throw new NotImplementedException("QueryOperatorPolicy.IsAllowed is not implemented yet (3.10 Task 4).");

        /// <summary>
        /// Throws <see cref="NotSupportedException"/> when the query's method spine contains an unsupported
        /// operator, or a supported operator in an unsupported position.
        /// </summary>
        public static void EnsureSupported(Expression expression) =>
            throw new NotImplementedException("QueryOperatorPolicy.EnsureSupported is not implemented yet (3.10 Task 4).");
    }
}
