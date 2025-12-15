using System.Data;

namespace Funcular.Data.Orm
{
    /// <summary>
    /// Defines the contract for an ORM provider that interacts with a SQL-based data store via ADO.NET.
    /// </summary>
    public interface ISqlOrmProvider : IOrmDataProvider
    {
        /// <summary>
        /// Gets or sets the connection.
        /// </summary>
        /// <value>The connection.</value>
        IDbConnection Connection { get; set; }

        /// <summary>
        /// Gets or sets the transaction.
        /// </summary>
        /// <value>The transaction.</value>
        IDbTransaction Transaction { get; set; }

        /// <summary>
        /// Gets or sets the name of the current transaction, if any. This can be used for identifying transactions in complex scenarios.
        /// </summary>
        string TransactionName { get; }

        /// <summary>
        /// Begins a new transaction.
        /// </summary>
        /// <param name="name">Optional name for the transaction.</param>
        void BeginTransaction(string name = "");

        /// <summary>
        /// Rolls back the current transaction if one exists.
        /// </summary>
        /// <param name="name">Optional name to match when rolling back.</param>
        void RollbackTransaction(string name = "");

        /// <summary>
        /// Commits the current transaction if one exists.
        /// </summary>
        /// <param name="name">Optional name to match when committing.</param>
        void CommitTransaction(string name = "");
    }
}
