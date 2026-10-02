using System;
using System.Linq.Expressions;

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
    /// Rejects delete-by-predicate calls whose predicate would match every row (docs/plans/DELETE_GUARD_PLAN.md).
    /// <para>Task 1 seam: nothing is classified yet. <see cref="Classify"/> returns
    /// <see cref="DeletePredicateVerdict.Acceptable"/>, <see cref="Validate"/> does nothing and
    /// <see cref="HasLiteralTautology"/> returns false.</para>
    /// </summary>
    public static class DeletePredicateGuard
    {
        /// <summary>Classifies <paramref name="predicate"/> before it is translated.</summary>
        public static DeletePredicateVerdict Classify(LambdaExpression predicate) => DeletePredicateVerdict.Acceptable;

        /// <summary>Throws <see cref="InvalidOperationException"/> when <paramref name="predicate"/> is rejected.</summary>
        public static void Validate(LambdaExpression predicate)
        {
        }

        /// <summary>Returns true when the translated <paramref name="whereClause"/> always holds.</summary>
        public static bool HasLiteralTautology(string whereClause) => false;
    }
}
