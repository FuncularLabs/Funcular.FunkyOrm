namespace Funcular.Data.Orm.Sqlite
{
    /// <summary>
    /// Controls string comparison behavior for the SQLite provider.
    /// SQLite is case-sensitive by default for <c>=</c> comparisons.
    /// </summary>
    public enum SqliteStringComparison
    {
        /// <summary>
        /// Case-insensitive comparisons (default). Uses <c>COLLATE NOCASE</c> on text columns
        /// to match SQL Server default behavior.
        /// </summary>
        CaseInsensitive = 0,

        /// <summary>
        /// Case-sensitive comparisons. Uses default SQLite binary comparison semantics.
        /// </summary>
        CaseSensitive = 1
    }
}
