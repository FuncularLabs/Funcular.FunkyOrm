using System;

namespace Funcular.Data.Orm.MySql
{
    /// <summary>
    /// Declared in 3.6.0 as a constructor option for case-sensitive comparisons, but no constructor ever accepted it and
    /// nothing reads it, so it has no effect (3.11.0, docs/plans/DECISIONS-3.10.2.md FD-06). MySQL compares strings
    /// under the column's collation, case-insensitive under the default <c>_ci</c> collations; to compare
    /// case-sensitively, give the column a <c>_bin</c> or <c>_cs</c> collation.
    /// </summary>
    [Obsolete("MySqlStringComparison has no effect: no constructor accepts it and nothing reads it. MySQL compares strings under the column's collation (case-insensitive under the default _ci collations); use a _bin or _cs column collation for case-sensitive comparisons. To be removed in 4.0.")]
    public enum MySqlStringComparison
    {
        /// <summary>No effect. MySQL's default <c>_ci</c> collations already compare case-insensitively.</summary>
        CaseInsensitive = 0,

        /// <summary>No effect: no <c>COLLATE utf8mb4_bin</c> is emitted. Use a <c>_bin</c> or <c>_cs</c> column collation.</summary>
        CaseSensitive = 1
    }
}
