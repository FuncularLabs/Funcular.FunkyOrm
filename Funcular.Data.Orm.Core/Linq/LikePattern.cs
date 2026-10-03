namespace Funcular.Data.Orm.Linq
{
    /// <summary>
    /// The escaping of a value that string <c>Contains</c>, <c>StartsWith</c> and <c>EndsWith</c> match literally in a
    /// <c>LIKE</c> pattern (docs/plans/LIKE_ESCAPING_TOSTRING_PLAN.md, D1).
    /// <para>
    /// Seam (Task 1): <see cref="Escape"/> returns its value unchanged and <see cref="EscapeClause"/> is empty; no
    /// translator calls them yet.
    /// </para>
    /// </summary>
    public static class LikePattern
    {
        /// <summary>The escape character every provider names in <see cref="EscapeClause"/>.</summary>
        public static readonly char EscapeCharacter = '!';

        /// <summary>The clause each provider appends after an escaped pattern.</summary>
        public static readonly string EscapeClause = "";

        /// <summary>Returns <paramref name="value"/> for a <c>LIKE</c> pattern.</summary>
        public static string? Escape(string? value) => value;
    }
}
