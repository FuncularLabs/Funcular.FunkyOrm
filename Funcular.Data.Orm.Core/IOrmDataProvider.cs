using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Funcular.Data.Orm
{
    /// <summary>
    /// Defines the core contract for an Object-Relational Mapper (ORM) provider.
    /// This interface is storage-agnostic and focuses on CRUD operations.
    /// </summary>
    public interface IOrmDataProvider
    {
        /// <summary>
        /// Gets or sets the log action (e.g., write to console, write to debug).
        /// </summary>
        /// <value>The log.</value>
        Action<string> Log { get; set; }

        /// <summary>
        /// Gets the entity having the specified key, if it exists.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">The key.</param>
        /// <returns>System.Nullable&lt;T&gt;.</returns>
        T Get<T>(dynamic key = null!) where T : class, new();

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
        [Obsolete("Use Query<T>().Where(predicate) instead. This method materializes results immediately.")]
        ICollection<T> Query<T>(Expression<Func<T, bool>> expression) where T : class, new();

        /// <summary>
        /// Gets the entire list of entities of type T.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>ICollection&lt;T&gt;.</returns>
        ICollection<T> GetList<T>() where T : class, new();

        /// <summary>
        /// Inserts the provided entity into the data store. Returns the primary key of the inserted entity.
        /// </summary>
        /// <typeparam name="T">The type of entity to insert. Must have a parameterless constructor.</typeparam>
        /// <param name="entity">The entity to insert.</param>
        /// <returns>The primary key of the inserted entity.</returns>
        object Insert<T>(T entity) where T : class, new();

        /// <summary>
        /// Inserts the provided entity into the data store. Returns the primary key of the inserted entity cast to TKey.
        /// </summary>
        /// <typeparam name="T">The type of entity to insert. Must have a parameterless constructor.</typeparam>
        /// <typeparam name="TKey">The type of the primary key.</typeparam>
        /// <param name="entity">The entity to insert.</param>
        /// <returns>The primary key of the inserted entity.</returns>
        TKey Insert<T, TKey>(T entity) where T : class, new();

        /// <summary>
        /// Updates the provided entity in the data store.
        /// </summary>
        /// <typeparam name="T">The type of entity to update. Must have a parameterless constructor.</typeparam>
        /// <param name="entity">The entity to update.</param>
        /// <returns>The updated entity.</returns>
        T Update<T>(T entity) where T : class, new();

        /// <summary>
        /// Asynchronously retrieves a single entity of type <typeparamref name="T"/> by the provided key or, if key is null, executes a select that may return the first matching row.
        /// </summary>
        Task<T> GetAsync<T>(dynamic key = null!) where T : class, new();

        /// <summary>
        /// Asynchronously executes a query generated from a LINQ expression and returns the matching entities.
        /// </summary>
        Task<ICollection<T>> QueryAsync<T>(Expression<Func<T, bool>> expression) where T : class, new();

        /// <summary>
        /// Asynchronously retrieves all rows for the specified entity type.
        /// </summary>
        Task<ICollection<T>> GetListAsync<T>() where T : class, new();

        /// <summary>
        /// Asynchronously inserts the provided entity into the data store and returns the generated primary key.
        /// </summary>
        Task<object> InsertAsync<T>(T entity) where T : class, new();

        /// <summary>
        /// Asynchronously inserts the provided entity into the data store and returns the generated primary key cast to TKey.
        /// </summary>
        Task<TKey> InsertAsync<T, TKey>(T entity) where T : class, new();

        /// <summary>
        /// Asynchronously updates the specified entity in the data store.
        /// </summary>
        Task<T> UpdateAsync<T>(T entity) where T : class, new();

        /// <summary>
        /// Asynchronously deletes entities of type <typeparamref name="T"/> matching the given predicate.
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="predicate">Expression specifying which entities to delete (WHERE clause).</param>
        /// <returns>The number of rows deleted.</returns>
        Task<int> DeleteAsync<T>(Expression<Func<T, bool>> predicate) where T : class, new();

        /// <summary>
        /// Deletes entities of type <typeparamref name="T"/> matching the given predicate.
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="predicate">Expression specifying which entities to delete (WHERE clause).</param>
        /// <returns>The number of rows deleted.</returns>
        int Delete<T>(Expression<Func<T, bool>> predicate) where T : class, new();

        /// <summary>
        /// Deletes the entity of type <typeparamref name="T"/> with the specified ID.
        /// </summary>
        bool Delete<T>(long id) where T : class, new();
    }
}
