using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer
{
    public interface ISqlDataProvider
    {
        /// <summary>
        /// Gets or sets the log action (e.g., write to console, write to debug).
        /// </summary>
        /// <value>The log.</value>
        Action<string>? Log { get; set; }
        /// <summary>
        /// Gets or sets the connection.
        /// </summary>
        /// <value>The connection.</value>
        SqlConnection? Connection { get; set; }
        /// <summary>
        /// Gets or sets the transaction.
        /// </summary>
        /// <value>The transaction.</value>
        SqlTransaction? Transaction { get; set; }

        /// <summary>
        /// Gets or sets the name of the current transaction, if any. This can be used for identifying transactions in complex scenarios.
        /// </summary>
        string? TransactionName { get; }

        /// <summary>
        /// Gets the entity having the specified key, if it exists.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">The key.</param>
        /// <returns>System.Nullable&lt;T&gt;.</returns>
        T? Get<T>(dynamic? key = null) where T : class, new();

        /// <summary>
        /// Queries the specified entity type.
        /// Parameterizes the resulting query.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>IQueryable&lt;T&gt;.</returns>
        IQueryable<T> Query<T>() where T : class, new();

        /// <summary>
        /// Queries the specified entity type using the specified expression as the WHERE clause.
        /// Parameterizes the resulting query.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="expression">The expression.</param>
        /// <returns>ICollection&lt;T&gt;.</returns>
        ICollection<T> Query<T>(Expression<Func<T, bool>> expression) where T : class, new();

        /// <summary>
        /// Gets the entire list of entities of type T.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>ICollection&lt;T&gt;.</returns>
        ICollection<T> GetList<T>() where T : class, new();

        /// <summary>
        /// Inserts the provided entity into the database. TODO: update to return dynamic PK of inserted
        /// </summary>
        /// <typeparam name="T">The type of entity to insert. Must have a parameterless constructor.</typeparam>
        /// <param name="entity">The entity to insert.</param>
        /// <returns>The number of rows affected by the insert operation.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the primary key does not have its default value.</exception>
        long Insert<T>(T entity) where T : class, new();

        /// <summary>
        /// Updates the provided entity in the database.
        /// </summary>
        /// <typeparam name="T">The type of entity to update. Must have a parameterless constructor.</typeparam>
        /// <param name="entity">The entity to update.</param>
        /// <returns>The updated entity.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the primary key value is not set.</exception>
        T Update<T>(T entity) where T : class, new();

        /// <summary>
        /// Begins a new transaction.
        /// </summary>
        /// <param name="name">Optional name for the transaction.</param>
        void BeginTransaction(string? name = "");

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