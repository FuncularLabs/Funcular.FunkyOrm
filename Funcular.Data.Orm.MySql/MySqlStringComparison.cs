namespace Funcular.Data.Orm.MySql
{
    /// <summary>
    /// Controls how string comparisons are performed in generated SQL for the MySQL provider.
    /// </summary>
    public enum MySqlStringComparison
    {
        /// <summary>
        /// Case-insensitive comparisons (default). Matches SQL Server default behavior and MySQL's
        /// default <c>_ci</c> collations. No collation override is emitted — the column/connection
        /// collation governs comparison.
        /// </summary>
        CaseInsensitive = 0,

        /// <summary>
        /// Case-sensitive comparisons. Appends <c>COLLATE utf8mb4_bin</c> to string equality and
        /// comparison operations in generated SQL.
        /// </summary>
        CaseSensitive = 1
    }
}
