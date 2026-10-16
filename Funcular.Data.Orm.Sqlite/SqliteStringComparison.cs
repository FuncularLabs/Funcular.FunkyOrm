using System;

namespace Funcular.Data.Orm.Sqlite
{
    /// <summary>
    /// Accepted by <c>SqliteOrmDataProvider</c>'s constructor and stored, but never read, so it has no effect (3.11.0,
    /// docs/plans/DECISIONS-3.10.2.md FD-06): no <c>COLLATE NOCASE</c> is emitted. SQLite compares <c>=</c>, <c>!=</c>,
    /// <c>&lt;</c> and <c>&gt;</c> with its <c>BINARY</c> collation (case-sensitive) unless the column is declared
    /// <c>COLLATE NOCASE</c>, and <c>LIKE</c> (<c>Contains</c>, <c>StartsWith</c>, <c>EndsWith</c>) folds ASCII case
    /// whatever the collation.
    /// </summary>
    [Obsolete("SqliteStringComparison has no effect: the provider stores it and never reads it, and no COLLATE NOCASE is emitted. SQLite compares = with BINARY (case-sensitive) unless the column is declared COLLATE NOCASE; LIKE folds ASCII case. To be removed in 4.0.")]
    public enum SqliteStringComparison
    {
        /// <summary>No effect. Declare the column <c>COLLATE NOCASE</c> for case-insensitive <c>=</c>.</summary>
        CaseInsensitive = 0,

        /// <summary>No effect. SQLite's default <c>BINARY</c> collation already compares <c>=</c> case-sensitively.</summary>
        CaseSensitive = 1
    }
}
