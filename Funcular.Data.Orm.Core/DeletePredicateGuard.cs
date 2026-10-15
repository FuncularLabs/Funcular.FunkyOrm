using System;
using System.Linq.Expressions;

namespace Funcular.Data.Orm
{
    /// <summary>
    /// The verdict <see cref="DeletePredicateGuard.Classify"/> gives a predicate: the 3.10.0 name of
    /// <see cref="WherePredicateVerdict"/>, member for member (3.11.0 adds <see cref="UnboundedSearch"/> to both).
    /// </summary>
    [Obsolete("Use WherePredicateVerdict: since 3.11.0 the guard is WherePredicateGuard, which refuses an all-rows predicate for a mass mutation (Delete, an update by predicate) only.")]
    public enum DeletePredicateVerdict
    {
        /// <summary>The predicate may be translated and run.</summary>
        Acceptable,

        /// <summary>The predicate references no column of the entity.</summary>
        NoColumn,

        /// <summary>The predicate is a comparison of a column, or a member of one, with itself.</summary>
        SelfReference,

        /// <summary>The predicate always holds.</summary>
        AlwaysTrue,

        /// <summary><see cref="WherePredicateVerdict.UnboundedSearch"/>: an empty search value on a column that nothing else bounds (3.11.0).</summary>
        UnboundedSearch
    }

    /// <summary>
    /// The 3.10.0 delete-only guard. Since 3.11.0 (docs/plans/DECISIONS-3.10.2.md D-003 as amended by D-128) the guard is
    /// <see cref="WherePredicateGuard"/>, which the shared WHERE translation runs with a <see cref="WhereOperation"/>
    /// flag and which refuses an all-rows predicate for a mass mutation (<c>Delete</c>, an update by predicate) only;
    /// every member here forwards to its mass-mutation form, with its messages, so a custom provider compiled against
    /// 3.10 keeps working. Of the 3.10.0 rejections, the empty search value stays (<c>WherePredicateVerdict.UnboundedSearch</c>);
    /// a LIKE wildcard, a null value, a property of a captured object, a <c>Contains</c>, <c>StartsWith</c>,
    /// <c>EndsWith</c> or <c>ToString()</c> on a value that doesn't read the row, and two strings that don't read the
    /// row are gone: the translation now escapes the value, sends <c>LIKE NULL</c>, reads the property and evaluates the
    /// call or comparison in C#, and the translators reject what they can't evaluate themselves, on every path.
    /// </summary>
    [Obsolete("Use WherePredicateGuard (Validate(predicate, WhereOperation.MassMutation)): since 3.11.0 the guard refuses an all-rows predicate for a mass mutation (Delete, an update by predicate) only; a query runs it. These members forward to the mass-mutation form.")]
    public static class DeletePredicateGuard
    {
        /// <summary>
        /// <see cref="WherePredicateGuard.Classify"/>, as a <see cref="DeletePredicateVerdict"/> (the same verdict by
        /// name, <see cref="DeletePredicateVerdict.UnboundedSearch"/> included), so that <see cref="Validate"/>'s message
        /// is always the message of the verdict <see cref="Classify"/> reports.
        /// </summary>
        /// <param name="predicate">A lambda with one parameter, the entity, and a <c>bool</c> body.</param>
        /// <returns>The verdict.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="predicate"/> doesn't have exactly one parameter, or its
        /// body isn't <c>bool</c>.</exception>
        public static DeletePredicateVerdict Classify(LambdaExpression predicate) =>
            (DeletePredicateVerdict)(int)WherePredicateGuard.Classify(predicate);

        /// <summary><see cref="WherePredicateGuard.Validate(LambdaExpression)"/>: the mass-mutation form.</summary>
        /// <param name="predicate">A lambda with one parameter, the entity, and a <c>bool</c> body.</param>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="predicate"/> doesn't have exactly one parameter, or its
        /// body isn't <c>bool</c>.</exception>
        /// <exception cref="InvalidOperationException">The predicate is rejected as an all-rows mass mutation.</exception>
        public static void Validate(LambdaExpression predicate) => WherePredicateGuard.Validate(predicate);

        /// <summary><see cref="WherePredicateGuard.HasLiteralTautology(string)"/>.</summary>
        /// <param name="whereClause">The WHERE clause, without the <c>WHERE</c> keyword.</param>
        /// <returns>True when the clause always holds by its literals.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="whereClause"/> is null.</exception>
        public static bool HasLiteralTautology(string whereClause) => WherePredicateGuard.HasLiteralTautology(whereClause);
    }
}
