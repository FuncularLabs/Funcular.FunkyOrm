namespace Funcular.Data.Orm.Linq
{
    /// <summary>
    /// How <c>Min</c>/<c>Max</c> over a <see cref="System.Guid"/> property are answered
    /// (docs/plans/QUERY_OPERATOR_CORRECTNESS_PLAN.md §10; 3.10.2).
    /// </summary>
    public enum GuidOrdering
    {
        /// <summary>
        /// <c>Min</c>/<c>Max</c> over a <see cref="System.Guid"/> throw <see cref="System.NotSupportedException"/> before any
        /// command is sent: the database's ordering differs from .NET's <see cref="System.Guid.CompareTo(System.Guid)"/> on
        /// SQL Server (byte groups 10–15 are compared first), and depends on the storage format on MySQL and SQLite.
        /// </summary>
        Rejected = 0,

        /// <summary>
        /// <c>Min</c>/<c>Max</c> over a <see cref="System.Guid"/> run in the database and return its ordering's answer
        /// (SQL Server, MySQL, SQLite). PostgreSQL has no <c>MIN</c>/<c>MAX</c> over <c>uuid</c>, so the option has no
        /// effect there and a <see cref="System.Guid"/> is rejected whatever it says.
        /// </summary>
        Database = 1
    }

    /// <summary>
    /// Provider-wide query options, set once at startup or in DI:
    /// <c>new SqlServerOrmDataProvider(connection) { QueryOptions = { GuidOrdering = GuidOrdering.Database } }</c>.
    /// </summary>
    public sealed class QueryOptions
    {
        /// <summary>How <c>Min</c>/<c>Max</c> over a <see cref="System.Guid"/> property are answered. Default: <see cref="Linq.GuidOrdering.Rejected"/>.</summary>
        public GuidOrdering GuidOrdering { get; set; } = GuidOrdering.Rejected;
    }
}
