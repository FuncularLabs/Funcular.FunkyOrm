using System;
using System.Linq.Expressions;

namespace Funcular.Data.Orm
{
    /// <summary>
    /// The verdict <see cref="DeletePredicateGuard.Classify"/> gives a predicate: the 3.10.0 name of
    /// <see cref="WherePredicateVerdict"/>, with the same members.
    /// </summary>
    [Obsolete("Use WherePredicateVerdict: since 3.11.0 the guard is shared by every WHERE clause, not only a delete's.")]
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
    /// The 3.10.0 delete-only guard. Since 3.11.0 (docs/plans/DECISIONS-3.10.2.md D-003) the same checks run on every
    /// WHERE clause, in <see cref="WherePredicateGuard"/>; every member here forwards to it, with its messages, so a
    /// custom provider compiled against 3.10 keeps working. The 3.10.0 rejections of a string match whose value is a
    /// LIKE wildcard, null or empty, or a property of a captured object, and of a <c>Contains</c>, <c>StartsWith</c>,
    /// <c>EndsWith</c> or <c>ToString()</c> on a value that doesn't read the row, are gone: the translation now
    /// escapes the value, sends <c>LIKE NULL</c>, reads the property and answers the <c>Contains</c> in C#, and the
    /// translators reject the remaining receivers themselves, on every path.
    /// </summary>
    [Obsolete("Use WherePredicateGuard: since 3.11.0 the guard is shared by every WHERE clause, not only a delete's.")]
    public static class DeletePredicateGuard
    {
        /// <summary><see cref="WherePredicateGuard.Classify"/>, as a <see cref="DeletePredicateVerdict"/>.</summary>
        /// <param name="predicate">A lambda with one parameter, the entity, and a <c>bool</c> body.</param>
        /// <returns>The verdict.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="predicate"/> doesn't have exactly one parameter, or its
        /// body isn't <c>bool</c>.</exception>
        public static DeletePredicateVerdict Classify(LambdaExpression predicate) =>
            (DeletePredicateVerdict)(int)WherePredicateGuard.Classify(predicate);

        /// <summary><see cref="WherePredicateGuard.Validate"/>.</summary>
        /// <param name="predicate">A lambda with one parameter, the entity, and a <c>bool</c> body.</param>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="predicate"/> doesn't have exactly one parameter, or its
        /// body isn't <c>bool</c>.</exception>
        /// <exception cref="InvalidOperationException">The predicate is rejected.</exception>
        /// <exception cref="NotSupportedException">The predicate compares two strings that don't read the row.</exception>
        public static void Validate(LambdaExpression predicate) => WherePredicateGuard.Validate(predicate);

        /// <summary><see cref="WherePredicateGuard.HasLiteralTautology(string)"/>.</summary>
        /// <param name="whereClause">The WHERE clause, without the <c>WHERE</c> keyword.</param>
        /// <returns>True when the clause always holds by its literals.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="whereClause"/> is null.</exception>
        public static bool HasLiteralTautology(string whereClause) => WherePredicateGuard.HasLiteralTautology(whereClause);
    }
}
