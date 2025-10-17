using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
#if NETSTANDARD20
using System.Data.Common;
#endif
using System.Threading.Tasks;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Funcular.Data.Orm.Visitors;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer
{
    /// <summary>
    /// A SQL Server specific implementation of an ORM data provider.
    /// Provides basic CRUD operations, query generation from expressions,
    /// and simple transaction management for SQL Server using <see cref="SqlConnection"/>.
    /// </summary>
    public partial class SqlServerOrmDataProvider : ISqlDataProvider, IDisposable
    {
        #region Fields

        /// <summary>
        /// Connection string used to create <see cref="SqlConnection"/> instances when needed.
        /// </summary>
        private readonly string _connectionString;

        /// <summary>
        /// The ambient <see cref="SqlTransaction"/> used by the provider when a transaction is in progress.
        /// </summary>
        private SqlTransaction _transaction;

        /// <summary>
        /// Cache mapping entity types to their resolved database table names.
        /// </summary>
        internal static readonly ConcurrentDictionary<Type, string> _tableNames = new ConcurrentDictionary<Type, string>();

        /// <summary>
        /// Cache mapping property dictionary keys (type + property) to actual database column names.
        /// Uses a comparer that ignores underscores and case.
        /// </summary>
        internal static readonly ConcurrentDictionary<string, string> _columnNames = new ConcurrentDictionary<string, string>(new IgnoreUnderscoreAndCaseStringComparer());

        /// <summary>
        /// Cache mapping entity types to their primary key <see cref="PropertyInfo"/>.
        /// </summary>
        internal static readonly ConcurrentDictionary<Type, PropertyInfo> _primaryKeys = new ConcurrentDictionary<Type, PropertyInfo>();

        /// <summary>
        /// Cache mapping entity types to the reflection <see cref="PropertyInfo"/> collection representing properties.
        /// </summary>
        internal static readonly ConcurrentDictionary<Type, ICollection<PropertyInfo>> _propertiesCache = new ConcurrentDictionary<Type, ICollection<PropertyInfo>>();

        /// <summary>
        /// Cache mapping entity types to properties marked with <see cref="NotMappedAttribute"/>.
        /// </summary>
        internal static readonly ConcurrentDictionary<Type, ICollection<PropertyInfo>> _unmappedPropertiesCache = new ConcurrentDictionary<Type, ICollection<PropertyInfo>>();

        /// <summary>
        /// Cache mapping entity types to a mapping of column name -> ordinal index for the most recent reader.
        /// </summary>
        internal static readonly ConcurrentDictionary<string, Dictionary<string, int>> _columnOrdinalsCache = new ConcurrentDictionary<string, Dictionary<string, int>>();

        /// <summary>
        /// Tracks which types have had their mappings discovered (to avoid repeated database schema calls).
        /// </summary>
        internal static readonly HashSet<Type> _mappedTypes = new HashSet<Type> { };

        /// <summary>
        /// Represents a thread-safe collection of entity mappers, where each mapper is identified by a unique string key.    
        /// </summary>
        /// <remarks>This dictionary is used to store and retrieve delegates that map entities to specific
        /// types or formats. It ensures thread-safe access and updates, making it suitable for concurrent
        /// operations.</remarks>
        internal static readonly ConcurrentDictionary<string, Delegate> _entityMappers = new ConcurrentDictionary<string, Delegate>();

        /// <summary>
        /// Cache mapping property types to their corresponding value setters.
        /// </summary>
        internal static readonly ConcurrentDictionary<PropertyInfo, Action<object, object>> _propertySetters = new ConcurrentDictionary<PropertyInfo, Action<object, object>>();

        #endregion

        #region Properties

        /// <summary>
        /// Exposes the internal column name cache to other components (used by visitor classes).
        /// </summary>
        internal static ConcurrentDictionary<string, string> ColumnNames => _columnNames;

        /// <summary>
        /// Action used to write diagnostic or SQL log messages.
        /// Can be set by callers to hook logging (e.g. Console.WriteLine).
        /// </summary>
        public Action<string> Log { get; set; }

        /// <summary>
        /// The current <see cref="SqlConnection"/> used by the provider. May be null until required.
        /// The provider will open and create connections as needed using the configured connection string.
        /// </summary>
        public SqlConnection Connection { get; set; }

        /// <summary>
        /// Gets or sets the current <see cref="SqlTransaction"/> for this provider.
        /// When non-null, the provider will execute commands in the context of this transaction.
        /// </summary>
        public SqlTransaction Transaction
        {
            get => _transaction;
            set => _transaction = value;
        }

        /// <summary>
        /// The optional name assigned to the current transaction. Useful for matching Begin/Commit/Rollback calls.
        /// </summary>
        public string TransactionName { get; protected set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of <see cref="SqlServerOrmDataProvider"/>.
        /// </summary>
        /// <param name="connectionString">The connection string used to create connections when necessary.</param>
        /// <param name="connection">Optional externally managed <see cref="SqlConnection"/>. If null, provider creates its own connections.</param>
        /// <param name="transaction">Optional externally managed <see cref="SqlTransaction"/>. If provided, the provider will use it for commands.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="connectionString"/> is null.</exception>
        public SqlServerOrmDataProvider(string connectionString, SqlConnection connection = null,
            SqlTransaction transaction = null)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            Connection = connection ?? new SqlConnection(_connectionString);
            Transaction = transaction;
        }

        #endregion

        #region  Public Async Methods

        /// <summary>
        /// Asynchronously retrieves a single entity of type <typeparamref name="T"/> by the provided key or, if key is null, executes a select that may return the first matching row.
        /// </summary>
        public async Task<T> GetAsync<T>(dynamic key = null) where T : class, new()
        {
            DiscoverColumns<T>();
            using (var connectionScope = new ConnectionScope(this))
            {
                var commandText = CreateGetOneOrSelectCommandText<T>(key);

                using (var command = BuildSqlCommandObject(commandText, connectionScope.Connection))
                {
                    return await ExecuteReaderSingleAsync<T>(command).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Asynchronously executes a query generated from a LINQ expression and returns the matching entities.
        /// </summary>
        public async Task<ICollection<T>> QueryAsync<T>(Expression<Func<T, bool>> expression) where T : class, new()
        {
            DiscoverColumns<T>();
            using (var connectionScope = new ConnectionScope(this))
            {
                var elements = CreateSelectQueryObject(expression);

                var commandText = elements.SelectClause;
                if (!string.IsNullOrEmpty(elements.WhereClause))
                    commandText += $"\r\nWHERE {elements.WhereClause}";
                if (!string.IsNullOrEmpty(elements.OrderByClause))
                    commandText += $"\r\n{elements.OrderByClause}";

                using (var command =
                             BuildSqlCommandObject(commandText, connectionScope.Connection, elements.SqlParameters))
                {
                    return await ExecuteReaderListAsync<T>(command).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Asynchronously retrieves all rows for the specified entity type.
        /// </summary>
        public async Task<ICollection<T>> GetListAsync<T>() where T : class, new()
        {
            DiscoverColumns<T>();
            using (var connectionScope = new ConnectionScope(this))
            {
                using (var command =
                             BuildSqlCommandObject(CreateGetOneOrSelectCommandText<T>(), connectionScope.Connection))
                {
                    return await ExecuteReaderListAsync<T>(command).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Asynchronously inserts the provided entity into the database and returns the generated primary key.
        /// </summary>
        public async Task<long> InsertAsync<T>(T entity) where T : class, new()
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            DiscoverColumns<T>();
            var primaryKey = GetCachedPrimaryKey<T>();
            ValidateInsertPrimaryKey(entity, primaryKey);

            using (var connectionScope = new ConnectionScope(this))
            {
                var insertCommand = BuildInsertCommandObject(entity, primaryKey);
                using (var command = BuildSqlCommandObject(insertCommand.CommandText, connectionScope.Connection,
                                 insertCommand.Parameters))
                {
                    var insertedId = await ExecuteInsertAsync(command, entity, primaryKey).ConfigureAwait(false);
                    return insertedId;
                }
            }
        }

        /// <summary>
        /// Asynchronously updates the specified entity in the database.
        /// </summary>
        public async Task<T> UpdateAsync<T>(T entity) where T : class, new()
        {
            DiscoverColumns<T>();
            var primaryKey = GetCachedPrimaryKey<T>();
            ValidateUpdatePrimaryKey(entity, primaryKey);

            using (var connectionScope = new ConnectionScope(this))
            {
                var existingEntity = await GetAsync<T>((dynamic)primaryKey.GetValue(entity)).ConfigureAwait(false);
                if (existingEntity == null)
                    throw new InvalidOperationException("Entity does not exist in database.");

                CommandParameters commandParameters = BuildUpdateCommand(entity, existingEntity, primaryKey);
                if (commandParameters.Parameters.Any())
                {
                    using (var command = BuildSqlCommandObject(commandParameters.CommandText, connectionScope.Connection,
                                     commandParameters.Parameters))
                    {
                        await ExecuteUpdateAsync(command).ConfigureAwait(false);
                    }
                }
                else
                {
                    Log?.Invoke($"No update needed for {typeof(T).Name} with id {primaryKey.GetValue(entity)}.");
                }

                return entity;
            }
        }

        /// <summary>
        /// Deletes records from the database table corresponding to the specified entity type that match the given
        /// predicate.
        /// </summary>
        /// <remarks>This method requires an active transaction. If no transaction is active, an <see
        /// cref="InvalidOperationException"/> is thrown. Additionally, the predicate must produce a valid WHERE clause;
        /// trivial or empty conditions (e.g., "1=1") are not allowed and will result in an exception.</remarks>
        /// <typeparam name="T">The type of the entity to delete. Must be a class with a parameterless constructor.</typeparam>
        /// <param name="predicate">An expression that defines the condition for the records to delete. This serves as the WHERE clause in the
        /// delete operation. The predicate must not be null and must result in a valid, non-trivial condition.</param>
        /// <returns>The number of rows affected by the delete operation.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the method is called without an active transaction, if the predicate is null, or if the predicate
        /// results in an invalid or trivial WHERE clause.</exception>
        public async Task<int> DeleteAsync<T>(Expression<Func<T, bool>> predicate) where T : class, new()
        {
            if (Transaction == null)
                throw new InvalidOperationException("Delete operations must be performed within an active transaction.");

            if (predicate == null)
                throw new InvalidOperationException("A WHERE clause (predicate) is required for deletes.");

            var components = GenerateWhereClause(predicate);
            // Defensive: block trivial or parameter-only WHERE clauses
            if (string.IsNullOrWhiteSpace(components.WhereClause) ||
                components.WhereClause.Trim().Equals("@p__linq__0", StringComparison.OrdinalIgnoreCase) ||
                components.WhereClause.Trim().Equals("1=1", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Delete operation requires a non-empty, valid WHERE clause.");

            var tableName = GetTableName<T>();
            var commandText = $"DELETE FROM {tableName} WHERE {components.WhereClause}";

            using (var command = BuildSqlCommandObject(commandText, Connection, components.SqlParameters))
            {
                InvokeLogAction(command);
                var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                if (affected == 0)
                    Log?.Invoke($"Delete affected zero rows for {typeof(T).Name}.");
                return affected;
            }
        }

        /// <summary>
        /// Deletes an entity of the specified type from the database by its primary key.
        /// </summary>
        /// <remarks>This method must be called within the context of an active transaction. If no
        /// transaction is active,  an <see cref="InvalidOperationException"/> is thrown. If the specified entity does
        /// not exist in the  database, the method returns <see langword="false"/> without throwing an
        /// exception.</remarks>
        /// <typeparam name="T">The type of the entity to delete. Must be a class with a parameterless constructor.</typeparam>
        /// <param name="id">The primary key value of the entity to delete. This value is used to identify the entity in the database.</param>
        /// <returns><see langword="true"/> if the entity was successfully deleted; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the method is called without an active transaction.</exception>
        public async Task<bool> DeleteAsync<T>(long id) where T : class, new()
        {
            if (Transaction == null)
                throw new InvalidOperationException("Delete operations must be performed within an active transaction.");

            var pk = GetCachedPrimaryKey<T>();
            var tableName = GetTableName<T>();
            var pkColumn = GetCachedColumnName(pk);

            var commandText = $"DELETE FROM {tableName} WHERE {pkColumn} = @id";
            var param = new SqlParameter("@id", id);

            using (var command = BuildSqlCommandObject(commandText, Connection, new[] { param }))
            {
                InvokeLogAction(command);
                var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                if (affected == 0)
                    Log?.Invoke($"Delete affected zero rows for {typeof(T).Name} with id {id}.");
                return affected > 0;
            }
        }

        #endregion
        // --- Async Execution Helpers ---

        #region Protected Async Execution Helpers

        protected internal async Task<T> ExecuteReaderSingleAsync<T>(SqlCommand command) where T : class, new()
        {
            InvokeLogAction(command);
            using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
            {
                return await reader.ReadAsync().ConfigureAwait(false) ? MapEntity<T>(reader) : null;
            }
        }

        protected internal async Task<ICollection<T>> ExecuteReaderListAsync<T>(SqlCommand command) where T : class, new()
        {
            InvokeLogAction(command);
            var results = new List<T>();
            using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
            {
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    results.Add(MapEntity<T>(reader));
                }
                return results;
            }
        }

        protected internal async Task<long> ExecuteInsertAsync<T>(SqlCommand command, T entity, PropertyInfo primaryKey)
            where T : class, new()
        {
            InvokeLogAction(command);
            var executeScalar = await command.ExecuteScalarAsync().ConfigureAwait(false);
            if (executeScalar != null)
            {
                var result = Convert.ToInt64(executeScalar);
                if (result != 0)
                    if (primaryKey.PropertyType == typeof(int))
                        primaryKey.SetValue(entity, (int)result);
                    else
                        primaryKey.SetValue(entity, result);
                return result;
            }
            throw new InvalidOperationException("Insert failed: No ID returned.");
        }

        protected internal async Task ExecuteUpdateAsync(SqlCommand command)
        {
            InvokeLogAction(command);
            var rowsAffected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            if (rowsAffected == 0)
                throw new InvalidOperationException("Update failed: No rows affected.");
        }

        #endregion


        #region Public Methods - CRUD Operations

        /// <summary>
        /// Retrieves a single entity of type <typeparamref name="T"/> by the provided key or, if key is null, executes a select that may return the first matching row.
        /// </summary>
        /// <typeparam name="T">The entity type to retrieve. Must have a parameterless constructor.</typeparam>
        /// <param name="key">An optional primary key value used to construct a WHERE clause. If null, returns the first row returned by the select.</param>
        /// <returns>An instance of <typeparamref name="T"/> if found; otherwise null.</returns>
        public T Get<T>(dynamic key = null) where T : class, new()
        {
            DiscoverColumns<T>();
            using (var connectionScope = new ConnectionScope(this))
            {
                var commandText = CreateGetOneOrSelectCommandText<T>(key);

                using (var command = BuildSqlCommandObject(commandText, connectionScope.Connection))
                {
                    return ExecuteReaderSingle<T>(command);
                }
            }
        }

        /// <summary>
        /// Executes a query generated from a LINQ expression and returns the matching entities.
        /// This method executes the query immediately and returns an <see cref="ICollection{T}"/> of results.
        /// Note: For performance reasons, prefer <see cref="Query{T}()"/> followed by chained LINQ operations (e.g., .Where(predicate).Count())
        /// when performing aggregates or when deferred execution is desired. Using this overload with a predicate loads all matching
        /// entities into memory before any further operations, whereas chaining on the parameterless overload allows SQL Server
        /// to handle aggregates and filtering efficiently.
        /// </summary>
        /// <typeparam name="T">Entity type being queried.</typeparam>
        /// <param name="expression">Expression used to generate the WHERE clause.</param>
        /// <returns>A collection of matching <typeparamref name="T"/> instances.</returns>
        public ICollection<T> Query<T>(Expression<Func<T, bool>> expression) where T : class, new()
        {
            DiscoverColumns<T>();
            using (var connectionScope = new ConnectionScope(this))
            {
                var elements = CreateSelectQueryObject(expression);

                var commandText = elements.SelectClause;
                if (!string.IsNullOrEmpty(elements.WhereClause))
                    commandText += $"\r\nWHERE {elements.WhereClause}";
                if (!string.IsNullOrEmpty(elements.OrderByClause))
                    commandText += $"\r\n{elements.OrderByClause}";

                using (var command =
                       BuildSqlCommandObject(commandText, connectionScope.Connection, elements.SqlParameters))
                {
                    return ExecuteReaderList<T>(command);
                }
            }
        }

        /// <summary>
        /// Retrieves all rows for the specified entity type.
        /// </summary>
        /// <typeparam name="T">The entity type to retrieve.</typeparam>
        /// <returns>A collection containing all persisted entities of type <typeparamref name="T"/>.</returns>
        public ICollection<T> GetList<T>() where T : class, new()
        {
            DiscoverColumns<T>();
            using (var connectionScope = new ConnectionScope(this))
            {
                using (var command =
                       BuildSqlCommandObject(CreateGetOneOrSelectCommandText<T>(), connectionScope.Connection))
                {
                    return ExecuteReaderList<T>(command);
                }
            }
        }

        /// <summary>
        /// Inserts the provided entity into the database and returns the generated primary key.
        /// The primary key property on the entity will be populated when a non-zero identity value is returned.
        /// </summary>
        /// <typeparam name="T">The entity type to insert.</typeparam>
        /// <param name="entity">The entity instance to insert. Must not be null.</param>
        /// <returns>The integer primary key returned by the database.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="entity"/> is null.</exception>
        public long Insert<T>(T entity) where T : class, new()
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            DiscoverColumns<T>();
            var primaryKey = GetCachedPrimaryKey<T>();
            ValidateInsertPrimaryKey(entity, primaryKey);

            using (var connectionScope = new ConnectionScope(this))
            {
                var insertCommand = BuildInsertCommandObject(entity, primaryKey);
                using (var command = BuildSqlCommandObject(insertCommand.CommandText, connectionScope.Connection,
                           insertCommand.Parameters))
                {
                    var insertedId = ExecuteInsert(command, entity, primaryKey);
                    return insertedId;
                }
            }
        }

        /// <summary>
        /// Updates the specified entity in the database. Only changed columns are included in the generated UPDATE statement.
        /// </summary>
        /// <typeparam name="T">The entity type to update.</typeparam>
        /// <param name="entity">The new entity values. The primary key must be set.</param>
        /// <returns>The same entity instance after a successful update.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the entity does not exist in the database or the primary key is not set.</exception>
        public T Update<T>(T entity) where T : class, new()
        {
            DiscoverColumns<T>();
            var primaryKey = GetCachedPrimaryKey<T>();
            ValidateUpdatePrimaryKey(entity, primaryKey);

            using (var connectionScope = new ConnectionScope(this))
            {
                var existingEntity = Get<T>((dynamic)primaryKey.GetValue(entity));
                if (existingEntity == null)
                    throw new InvalidOperationException("Entity does not exist in database.");

                CommandParameters commandParameters = BuildUpdateCommand(entity, existingEntity, primaryKey);
                if (commandParameters.Parameters.Any())
                {
                    using (var command = BuildSqlCommandObject(commandParameters.CommandText, connectionScope.Connection,
                               commandParameters.Parameters))
                    {
                        ExecuteUpdate(command);
                    }
                }
                else
                {
                    Log?.Invoke($"No update needed for {typeof(T).Name} with id {primaryKey.GetValue(entity)}.");
                }

                return entity;
            }
        }

        /// <summary>
        /// Creates an <see cref="IQueryable{T}"/> backed by the provider, allowing deferred LINQ-to-SQL execution.
        /// Use this overload for chaining LINQ operations (e.g., .Where(predicate), .OrderBy(), .Count(predicate)) where
        /// the query is translated to SQL and executed only when enumerated. This is preferred for aggregates and complex
        /// queries as it allows SQL Server to optimize execution, avoiding unnecessary data transfer.
        /// </summary>
        /// <typeparam name="T">The entity type for the queryable.</typeparam>
        /// <returns>An <see cref="IQueryable{T}"/> instance that will translate LINQ expressions to SQL when enumerated.</returns>
        public IQueryable<T> Query<T>() where T : class, new()
        {
            string selectCommand = CreateGetOneOrSelectCommandText<T>();
            return CreateQueryable<T>(selectCommand);
        }

        /// <summary>
        /// Deletes entities of type <typeparamref name="T"/> matching the given predicate.
        /// Requires a valid WHERE clause and an active transaction.
        /// Throws if either condition is not met.
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="predicate">Expression specifying which entities to delete (WHERE clause).</param>
        /// <returns>The number of rows deleted.</returns>
        public int Delete<T>(Expression<Func<T, bool>> predicate) where T : class, new()
        {
            if (Transaction == null)
                throw new InvalidOperationException("Delete operations must be performed within an active transaction.");

            if (predicate == null)
                throw new InvalidOperationException("A WHERE clause (predicate) is required for deletes.");

            var components = GenerateWhereClause(predicate);

            // Enhanced validation
            if (string.IsNullOrWhiteSpace(components.WhereClause))
                throw new InvalidOperationException("Delete operation requires a non-empty, valid WHERE clause.");

            // Trivial patterns
            var trivialPatterns = new[] { "1=1", "1 < 2", "1 > 0", "true", "WHERE 1=1", "WHERE 1 < 2" };
            if (trivialPatterns.Any(p => components.WhereClause.Replace(" ", "").Contains(p.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Delete operation requires a non-trivial WHERE clause.");

            // Self-referencing column (e.g., x => x.Id == x.Id)
            var regex = new System.Text.RegularExpressions.Regex(@"\b(\w+)\s*=\s*\1\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (regex.IsMatch(components.WhereClause))
                throw new InvalidOperationException("Delete operation WHERE clause cannot be a self-referencing column expression.");

            // Must reference at least one column from the target table
            var tableColumns = typeof(T).GetProperties()
                .Where(p => p.GetCustomAttributes(typeof(NotMappedAttribute), true).Length == 0)
                .Select(p => GetCachedColumnName(p))
                .ToList();

            bool columnReferenced = tableColumns.Any(col =>
                components.WhereClause.IndexOf(col, StringComparison.OrdinalIgnoreCase) >= 0);

            if (!columnReferenced)
                throw new InvalidOperationException("Delete operation WHERE clause must reference at least one column from the target table.");

            var tableName = GetTableName<T>();
            var commandText = $"DELETE FROM {tableName} WHERE {components.WhereClause}";

            using (var command = BuildSqlCommandObject(commandText, Connection, components.SqlParameters))
            {
                InvokeLogAction(command);
                var affected = command.ExecuteNonQuery();
                if (affected == 0)
                    Log?.Invoke($"Delete affected zero rows for {typeof(T).Name}.");
                return affected;
            }
        }

        /// <summary>
        /// Deletes a record from the database table corresponding to the specified type, based on the provided primary
        /// key value.
        /// </summary>
        /// <remarks>This method must be called within an active transaction. If no transaction is active,
        /// an <see cref="InvalidOperationException"/> is thrown. If no record matches the specified primary key, the
        /// method returns <see langword="false"/> and logs a message, if a log action is provided.</remarks>
        /// <typeparam name="T">The type of the entity to delete. Must be a class with a parameterless constructor.</typeparam>
        /// <param name="id">The primary key value of the record to delete.</param>
        /// <returns><see langword="true"/> if the record was successfully deleted; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the method is called without an active transaction.</exception>
        public bool Delete<T>(long id) where T : class, new()
        {
            if (Transaction == null)
                throw new InvalidOperationException("Delete operations must be performed within an active transaction.");

            var pk = GetCachedPrimaryKey<T>();
            var tableName = GetTableName<T>();
            var pkColumn = GetCachedColumnName(pk);

            var commandText = $"DELETE FROM {tableName} WHERE {pkColumn} = @id";
            var param = new SqlParameter("@id", id);

            using (var command = BuildSqlCommandObject(commandText, Connection, new[] { param }))
            {
                InvokeLogAction(command);
                var affected = command.ExecuteNonQuery();
                if (affected == 0)
                    Log?.Invoke($"Delete affected zero rows for {typeof(T).Name} with id {id}.");
                return affected > 0;
            }
        }

        #endregion

        #region Public Methods - Transaction Management

        /// <summary>
        /// Begins a new database transaction. The provider will create and open a connection if necessary.
        /// </summary>
        /// <param name="name">Optional name used to identify the transaction. Use the same name on Commit/Rollback to ensure the correct transaction is affected.</param>
        /// <exception cref="InvalidOperationException">Thrown if a transaction is already in progress.</exception>
        public void BeginTransaction(string name = null)
        {
            EnsureConnectionOpen();
            if (Transaction != null)
                throw new InvalidOperationException("Transaction already in progress.");

            Transaction = Connection?.BeginTransaction(IsolationLevel.ReadCommitted);
            TransactionName = name ?? string.Empty;
        }

        /// <summary>
        /// Commits the current transaction if one exists and if the optional name matches (or is not provided).
        /// </summary>
        /// <param name="name">Optional name to match against the current transaction name.</param>
        public void CommitTransaction(string name = null)
        {
            if (Transaction != null && (string.IsNullOrEmpty(name) || TransactionName == name))
            {
                Transaction.Commit();
                CleanupTransaction();
            }
        }

        /// <summary>
        /// Rolls back the current transaction if one exists and if the optional name matches (or is not provided).
        /// </summary>
        /// <param name="name">Optional name to match against the current transaction name.</param>
        public void RollbackTransaction(string name = null)
        {
            if (Transaction != null && (string.IsNullOrEmpty(name) || TransactionName == name))
            {
                Transaction.Rollback();
                CleanupTransaction();
            }
        }

        #endregion

        #region Public Methods - Utility

        /// <summary>
        /// Disposes the provider, closing and disposing the underlying connection and transaction.
        /// Safe to call multiple times.
        /// </summary>
        public void Dispose()
        {
            Connection?.Dispose();
            Transaction?.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected Methods - Connection Management

        /// <summary>
        /// Obtains an open <see cref="SqlConnection"/> instance for use by the provider.
        /// Ensures a connection is open before returning it.
        /// </summary>
        /// <returns>An open <see cref="SqlConnection"/> instance.</returns>
        protected SqlConnection GetConnection()
        {
            EnsureConnectionOpen();
            return Connection;
        }

        /// <summary>
        /// Ensures the provider has a live open connection. If the existing connection is null or closed,
        /// a new <see cref="SqlConnection"/> is created from the configured connection string and opened.
        /// </summary>
        protected void EnsureConnectionOpen()
        {
            if (Connection == null || Connection.State != ConnectionState.Open)
            {
                Connection = new SqlConnection(_connectionString);
                Connection.Open();
            }
        }

        /// <summary>
        /// Closes and disposes the current connection if there is no active transaction. This is used to
        /// release connections when the provider created them for a single operation.
        /// </summary>
        protected internal void CloseConnectionIfNoTransaction()
        {
            if (Transaction == null && Connection?.State == ConnectionState.Open)
            {
                Connection.Close();
                Connection.Dispose();
                Connection = null;
            }
        }

        #endregion

        #region Protected Methods - Query Building

        /// <summary>
        /// Builds a SELECT statement for the specified type optionally constrained by a primary key.
        /// The returned SQL contains the resolved column list and a WHERE clause when <paramref name="key"/> is provided.
        /// </summary>
        /// <typeparam name="T">Entity type for which to build the SELECT statement.</typeparam>
        /// <param name="key">Optional primary key value to produce a WHERE clause.</param>
        /// <returns>A complete SELECT SQL string for the entity.</returns>
        protected internal string CreateGetOneOrSelectCommandText<T>(dynamic key = null) where T : class, new()
        {
            var tableName = GetTableName<T>();
            string columnNames = GetColumnNames<T>();
            var whereClause = key != null ? GetWhereClause<T>(key) : string.Empty;
            return $"SELECT {columnNames} FROM {tableName}{whereClause}";
        }

        /// <summary>
        /// Creates the pieces required for a SELECT statement from a LINQ expression.
        /// This method translates the expression into a where clause and attaches it to a base SELECT.
        /// </summary>
        /// <typeparam name="T">Entity type for the query.</typeparam>
        /// <param name="whereExpression">The expression that will be translated into a WHERE clause.</param>
        /// <returns>A <see cref="SqlQueryComponents{T}"/> instance containing the translated SQL fragments and parameters.</returns>
        protected internal SqlQueryComponents<T> CreateSelectQueryObject<T>(Expression<Func<T, bool>> whereExpression)
            where T : class, new()
        {
            var selectClause = CreateGetOneOrSelectCommandText<T>();
            var elements = GenerateWhereClause(whereExpression);
            elements.SelectClause = selectClause;
            elements.OriginalExpression = whereExpression;
            elements.WhereClause = elements.WhereClause;
            // elements.OrderByClause = GenerateOrderByClause(whereExpression).OrderByClause;
            return elements;
        }

        /// <summary>
        /// Translates a LINQ expression tree into SQL WHERE clause text and parameters using
        /// helper visitor and translator classes.
        /// </summary>
        /// <typeparam name="T">The entity type referred to by the expression.</typeparam>
        /// <param name="expression">The expression to translate into SQL.</param>
        /// <param name="commandElements">Optional existing command components to augment.</param>
        /// <param name="parameterGenerator">Optional <see cref="ParameterGenerator"/> used for parameter naming; a new one is created if not supplied.</param>
        /// <param name="translator">Optional <see cref="SqlExpressionTranslator"/> to assist with translating method calls and members.</param>
        /// <returns>A <see cref="SqlQueryComponents{T}"/> containing WHERE clause, parameters, and other metadata.</returns>
        protected internal SqlQueryComponents<T> GenerateWhereClause<T>(
            Expression<Func<T, bool>> expression,
            SqlQueryComponents<T> commandElements = null,
            ParameterGenerator parameterGenerator = null,
            SqlExpressionTranslator translator = null) where T : class, new()
        {
            // Use the provided ParameterGenerator and translator, or create new ones if not specified
            var paramGen = parameterGenerator ?? new ParameterGenerator();
            var trans = translator ?? new SqlExpressionTranslator(paramGen);

            var visitor = new WhereClauseVisitor<T>(
                ColumnNames,
                _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>),
                paramGen,
                trans);
            visitor.Visit(expression);
            if (commandElements != null)
            {
                commandElements.WhereClause = visitor.WhereClauseBody;
                commandElements.OriginalExpression = commandElements.OriginalExpression ?? expression;
                if (visitor.Parameters.Any())
                {
                    commandElements.SqlParameters = commandElements.SqlParameters ?? new List<SqlParameter> { };
                    commandElements.SqlParameters.AddRange(visitor.Parameters);
                }
            }
            else
            {
                commandElements = new SqlQueryComponents<T>(expression, string.Empty, visitor.WhereClauseBody,
                    string.Empty, visitor.Parameters);
            }

            return commandElements;
        }

        /// <summary>
        /// Generates an ORDER BY clause from a LINQ expression when applicable.
        /// This method leverages the OrderByClauseVisitor to produce a SQL ORDER BY fragment.
        /// </summary>
        /// <typeparam name="T">The entity type referenced by the expression.</typeparam>
        /// <param name="expression">The expression to inspect for ordering information.</param>
        /// <param name="commandElements">Optional components object to augment with the ORDER BY clause.</param>
        /// <returns>A <see cref="SqlQueryComponents{T}"/> with the OrderByClause populated when relevant.</returns>
        protected internal SqlQueryComponents<T> GenerateOrderByClause<T>(Expression<Func<T, bool>> expression,
            SqlQueryComponents<T> commandElements = null) where T : class, new()
        {
            var visitor = new OrderByClauseVisitor<T>(
                ColumnNames,
                _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>));
            visitor.Visit(expression);
            if (commandElements == null)
            {
                commandElements = new SqlQueryComponents<T>(expression, string.Empty, string.Empty,
                    visitor.OrderByClause, new List<SqlParameter> { });
            }
            else
            {
                commandElements.OrderByClause = visitor.OrderByClause;
                commandElements.OriginalExpression = commandElements.OriginalExpression ?? expression;
            }

            return commandElements;
        }

        /// <summary>
        /// Validates the provided WHERE clause to ensure it meets the requirements for a delete operation.
        /// </summary>
        /// <remarks>This method ensures that the WHERE clause is meaningful and safe for use in a delete
        /// operation. It prevents trivial or invalid conditions that could lead to unintended data
        /// destruction.</remarks>
        /// <typeparam name="T">The type representing the target table. The properties of this type are used to validate column references
        /// in the WHERE clause.</typeparam>
        /// <param name="whereClause">The SQL WHERE clause to validate. Must be a non-empty, non-trivial expression that references at least one
        /// column from the target table.</param>
        /// <exception cref="InvalidOperationException">Thrown if the WHERE clause is null, empty, or consists only of whitespace; if it contains trivial
        /// expressions (e.g., "1=1"); if it includes self-referencing column expressions (e.g., "column = column"); or
        /// if it does not reference any columns from the target table.</exception>
        private void ValidateWhereClause<T>(string whereClause)
        {
            if (string.IsNullOrWhiteSpace(whereClause))
                throw new InvalidOperationException("Delete operation requires a non-empty, valid WHERE clause.");

            var trivialPatterns = new[]
            {
                "1=1", "1 < 2", "1 > 0", "@p__linq__0", "true", "WHERE 1=1", "WHERE 1 < 2"
            };

            // Check for trivial patterns
            if (trivialPatterns.Any(p => whereClause.Replace(" ", "").Contains(p.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Delete operation requires a non-trivial WHERE clause.");

            // Check for self-referencing column expressions (e.g., first_name = first_name)
            var regex = new System.Text.RegularExpressions.Regex(@"\b(\w+)\s*=\s*\1\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (regex.IsMatch(whereClause))
                throw new InvalidOperationException("Delete operation WHERE clause cannot be a self-referencing column expression.");

            // Check that at least one column from the target table is referenced
            var tableColumns = typeof(T).GetProperties()
                .Where(p => p.GetCustomAttributes(typeof(NotMappedAttribute), true).Length == 0)
                .Select(p => GetCachedColumnName(p))
                .ToList();

            bool columnReferenced = tableColumns.Any(col =>
                whereClause.IndexOf(col, StringComparison.OrdinalIgnoreCase) >= 0);

            if (!columnReferenced)
                throw new InvalidOperationException("Delete operation WHERE clause must reference at least one column from the target table.");
        }

        #endregion

        #region Protected Methods - Execution Helpers

        /// <summary>
        /// Executes the provided <see cref="SqlCommand"/>, maps the first row to an entity of type <typeparamref name="T"/>,
        /// and returns it. If no rows are returned, null is returned.
        /// </summary>
        /// <typeparam name="T">The entity type to map the first row to.</typeparam>
        /// <param name="command">The command to execute (must be configured with text, connection and parameters).</param>
        /// <returns>The mapped entity or null if the resultset is empty.</returns>
        protected internal T ExecuteReaderSingle<T>(SqlCommand command) where T : class, new()
        {
            InvokeLogAction(command);
            using (var reader = command.ExecuteReader())
            {
                return reader.Read() ? MapEntity<T>(reader) : null;
            }
        }

        /// <summary>
        /// Executes the provided <see cref="SqlCommand"/> and maps all returned rows to a list of entities.
        /// </summary>
        /// <typeparam name="T">The entity type to map each row to.</typeparam>
        /// <param name="command">The command to execute.</param>
        /// <returns>A list of mapped entities. Empty list if no rows returned.</returns>
        protected internal ICollection<T> ExecuteReaderList<T>(SqlCommand command) where T : class, new()
        {
            InvokeLogAction(command);
            var results = new List<T>();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    results.Add(MapEntity<T>(reader));
                }

                return results;
            }
        }

        /// <summary>
        /// Builds a configured <see cref="SqlCommand"/> instance for the given SQL text and connection, optionally adding parameters.
        /// </summary>
        /// <param name="commandText">The SQL text to execute.</param>
        /// <param name="connection">The connection on which the command will be executed.</param>
        /// <param name="parameters">Optional collection of <see cref="SqlParameter"/> objects to attach to the command.</param>
        /// <returns>A configured <see cref="SqlCommand"/> ready for execution.</returns>
        protected internal SqlCommand BuildSqlCommandObject(string commandText, SqlConnection connection,
            ICollection<SqlParameter> parameters = null)
        {
            var command = new SqlCommand(commandText, connection)
            {
                CommandType = CommandType.Text,
                Transaction = Transaction
            };
            if (parameters?.Any() == true)
            {
                command.Parameters.AddRange(parameters.ToArray());
            }

            return command;
        }

        /// <summary>
        /// Ensures schema/column discovery has been performed for the entity type <typeparamref name="T"/>.
        /// This caches column names and other metadata to avoid repeated schema lookups.
        /// </summary>
        /// <typeparam name="T">The entity type to discover column mappings for.</typeparam>
#if NET8_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        protected void DiscoverColumns<T>()
        {
            if (_mappedTypes.Contains(typeof(T))) return;

            var table = GetTableName<T>();
            var commandText = $"SELECT * FROM {table}";
            var properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties().ToArray());

            using (var connectionScope = new ConnectionScope(this))
            {
                using (var command = BuildSqlCommandObject(commandText, connectionScope.Connection,
                           Array.Empty<SqlParameter>()))
                {
                    using (var reader = command.ExecuteReader(CommandBehavior.SchemaOnly))
                    {

                        ICollection<string> columnNames = new List<string>();
#if NET8_0_OR_GREATER
                        var columnSchema = reader.GetColumnSchema();
                        foreach (var dbColumn in columnSchema)
                        {
                            columnNames.Add(dbColumn.ColumnName);
                        }
#else
                        var schemaTable = reader.GetSchemaTable();
                        foreach (DataRow row in schemaTable?.Rows)
                        {
                            columnNames.Add(row["ColumnName"].ToString());
                        }
#endif
                        var comparer = new IgnoreUnderscoreAndCaseStringComparer();
                        foreach (var property in properties)
                        {
                            if (property.GetCustomAttribute<NotMappedAttribute>() != null) continue;

                            var columnAttr = property.GetCustomAttribute<ColumnAttribute>();
                            string actualColumnName = columnAttr?.Name ??
                                                      columnNames.FirstOrDefault(c =>
                                                              comparer.Equals(c, property.Name));
                            if (actualColumnName != null)
                            {
                                var key = property.ToDictionaryKey();
                                ColumnNames[key] = actualColumnName;
                            }
                        }

                        _mappedTypes.Add(typeof(T));
                    }
                }
            }
        }


        /// <summary>
        /// Generates a schema signature for the current result set of the provided <see cref="SqlDataReader"/>.
        /// </summary>
        /// <remarks>The schema signature is useful for identifying the structure of a result set,
        /// including column names and their corresponding data types. This method assumes that the <paramref
        /// name="reader"/> is positioned on a valid result set.</remarks>
        /// <param name="reader">The <see cref="SqlDataReader"/> from which to generate the schema signature. Must not be <see
        /// langword="null"/>.</param>
        /// <returns>A string representing the schema of the result set, where each column is represented as
        /// "<c>ColumnName:FullyQualifiedTypeName</c>", and columns are separated by a pipe ("|") character.</returns>
        private static string GetSchemaSignature(SqlDataReader reader)
        {
            var cols = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
                cols.Add($"{reader.GetName(i)}:{reader.GetFieldType(i)?.FullName}");
            return string.Join("|", cols);
        }

        /// <summary>
        /// Maps the current row of a <see cref="SqlDataReader"/> to an instance of the specified entity type.
        /// </summary>
        /// <remarks>This method uses a cached mapping function to optimize repeated mappings for the same
        /// entity type and schema. The mapping function is generated based on the schema of the <see
        /// cref="SqlDataReader"/> at runtime.</remarks>
        /// <typeparam name="T">The type of the entity to map to. Must be a reference type with a parameterless constructor.</typeparam>
        /// <param name="reader">The <see cref="SqlDataReader"/> containing the data to map. The reader must be positioned on a valid row.</param>
        /// <returns>An instance of the specified entity type <typeparamref name="T"/> populated with data from the current row
        /// of the reader.</returns>
#if NET8_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        protected T MapEntity<T>(SqlDataReader reader) where T : class, new()
        {
            // Use both type and schema signature as cache key
            string schemaKey = typeof(T).FullName + "|" + GetSchemaSignature(reader);

            var mapper = (Func<SqlDataReader, T>)_entityMappers.GetOrAdd(schemaKey, _ =>
                BuildDataReaderMapper<T>(reader)
            );
            return mapper(reader);
        }

        /// <summary>
        /// Builds a function that maps a <see cref="SqlDataReader"/> to an instance of the specified type <typeparamref
        /// name="T"/>.
        /// </summary>
        /// <remarks>The mapping function dynamically matches the columns in the <see
        /// cref="SqlDataReader"/> to the properties of  the specified type <typeparamref name="T"/> based on their
        /// names. Properties that do not have a corresponding  column in the data reader or are explicitly marked as
        /// unmapped are ignored. <para> The method supports common data types such as <see cref="int"/>, <see
        /// cref="string"/>, <see cref="bool"/>,  <see cref="DateTime"/>, and others. For unsupported types, the method
        /// falls back to using  <see cref="Convert.ChangeType(object, Type)"/> to convert the value. </para> <para>
        /// Nullable properties are handled appropriately, and columns with <see cref="DBNull"/> values are skipped 
        /// during assignment. </para></remarks>
        /// <typeparam name="T">The type of the object to map to. Must be a reference type with a parameterless constructor.</typeparam>
        /// <param name="reader">The <see cref="SqlDataReader"/> instance used to determine column mappings.</param>
        /// <returns>A function that takes a <see cref="SqlDataReader"/> and returns an instance of <typeparamref name="T"/> 
        /// with its properties populated from the corresponding columns in the data reader.</returns>
#if NET8_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private Func<SqlDataReader, T> BuildDataReaderMapper<T>(SqlDataReader reader) where T : class, new()
        {
            var type = typeof(T);
            string schemaSignature = GetSchemaSignature(reader);
            string ordinalsKey = type.FullName + "|" + schemaSignature;

            var schemaOrdinals = _columnOrdinalsCache.GetOrAdd(ordinalsKey, _ =>
            {
                var ordinals = new Dictionary<string, int>(new IgnoreUnderscoreAndCaseStringComparer());
                for (int i = 0; i < reader.FieldCount; i++)
                    ordinals[reader.GetName(i)] = i;
                return ordinals;
            });

            var properties = _propertiesCache.GetOrAdd(type, t => t.GetProperties());
            var unmappedNames = new HashSet<string>(
                _unmappedPropertiesCache.GetOrAdd(type, GetUnmappedProperties<T>).Select(p => p.Name)
            );

            // Precompute mapping array
            var mappings = properties
                .Select(p =>
                {
                    // If the property is marked [NotMapped] we still want to bind it
                    // when the query projection included an alias with the same name.
                    // For mapped properties use the cached column name; for unmapped use the property name
                    // (the projection aliases use the CLR property name).
                    string columnName;
                    if (unmappedNames.Contains(p.Name))
                    {
                        columnName = p.Name; // projection alias expected to match property name
                    }
                    else
                    {
                        columnName = GetCachedColumnName(p);
                    }

                    if (string.IsNullOrEmpty(columnName) || !schemaOrdinals.TryGetValue(columnName, out int ordinal))
                        return null;

                    var setter = GetOrCreateSetter(p);
                    var propertyType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                    return new { Ordinal = ordinal, Setter = setter, Type = propertyType, IsEnum = propertyType.IsEnum };
                })
                .Where(m => m != null)
                .ToArray();

            return r =>
            {
                var entity = new T();
                foreach (var m in mappings)
                {
                    if (r.IsDBNull(m.Ordinal)) continue;
                    object value;
                    if (m.Type == typeof(int)) value = r.GetInt32(m.Ordinal);
                    else if (m.Type == typeof(long)) value = r.GetInt64(m.Ordinal);
                    else if (m.Type == typeof(string)) value = r.GetString(m.Ordinal);
                    else if (m.Type == typeof(bool)) value = r.GetBoolean(m.Ordinal);
                    else if (m.Type == typeof(DateTime)) value = r.GetDateTime(m.Ordinal);
                    else if (m.Type == typeof(Guid)) value = r.GetGuid(m.Ordinal);
                    else if (m.Type == typeof(decimal)) value = r.GetDecimal(m.Ordinal);
                    else if (m.Type == typeof(double)) value = r.GetDouble(m.Ordinal);
                    else if (m.Type == typeof(float)) value = r.GetFloat(m.Ordinal);
                    else if (m.IsEnum) value = Enum.ToObject(m.Type, r.GetValue(m.Ordinal));
                    else value = Convert.ChangeType(r.GetValue(m.Ordinal), m.Type);
                    m.Setter(entity, value);
                }
                return entity;
            };
        }

        #endregion

        #region Protected Methods - Insert/Update Helpers

        /// <summary>
        /// Builds an INSERT statement and the corresponding parameter list for the provided entity.
        /// Excludes properties decorated with <see cref="NotMappedAttribute"/> and the provided primary key.
        /// </summary>
        /// <typeparam name="T">The entity type to insert.</typeparam>
        /// <param name="entity">The entity instance from which to read values.</param>
        /// <param name="primaryKey">The primary key property info for the entity type.</param>
        /// <returns>A tuple containing CommandText and the list of SqlParameter instances.</returns>
        protected internal CommandParameters BuildInsertCommandObject<T>(T entity,
            PropertyInfo primaryKey) where T : class, new()
        {
            var tableName = GetTableName<T>();
            var unmapped = _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>);
            var includedProperties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties().ToArray())
                .Where(p => unmapped.All(up => up.Name != p.Name) && p != primaryKey);
            // Properties that are not marked with [NotMapped] and are not the primary key
            var parameters = new List<SqlParameter>();
            var columnNames = new List<string>();
            var parameterNames = new List<string>();

            foreach (var property in includedProperties)
            {
                var columnName = GetCachedColumnName(property);
                var value = property.GetValue(entity);
                var parameterName = $"@{property.Name}";

                columnNames.Add(columnName);
                parameterNames.Add(parameterName);
                parameters.Add(CreateParameter<object>(parameterName, value, property.PropertyType));
            }

            var commandText = $@"
                INSERT INTO {tableName} ({string.Join(", ", columnNames)})
                OUTPUT INSERTED.{GetCachedColumnName(primaryKey)}
                VALUES ({string.Join(", ", parameterNames)})";
            return new CommandParameters(commandText, parameters);
        }

        /// <summary>
        /// Executes an INSERT command and reads the scalar returned (expected identity).
        /// If a non-zero integer is returned, the primary key property on <paramref name="entity"/> is set.
        /// </summary>
        /// <typeparam name="T">The entity type that was inserted.</typeparam>
        /// <param name="command">A configured insert <see cref="SqlCommand"/> that will return the inserted id.</param>
        /// <param name="entity">The entity instance to update with the generated id.</param>
        /// <param name="primaryKey">Primary key property to set on the entity.</param>
        /// <returns>The integer id returned by the database.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no scalar value is returned by the insert command.</exception>
        protected internal int ExecuteInsert<T>(SqlCommand command, T entity, PropertyInfo primaryKey)
            where T : class, new()
        {
            InvokeLogAction(command);
            var executeScalar = command.ExecuteScalar();
            if (executeScalar != null)
            {
                var result = (int)executeScalar;
                if (result != 0)
                    primaryKey.SetValue(entity, result);
                return result;
            }
            throw new InvalidOperationException("Insert failed: No ID returned.");
        }

        /// <summary>
        /// Builds an UPDATE statement for the specified entity by comparing it to the existing persisted instance.
        /// Only properties whose values have changed are included in the SET clause.
        /// </summary>
        /// <typeparam name="T">The entity type to update.</typeparam>
        /// <param name="entity">The new entity values.</param>
        /// <param name="existing">The existing persisted entity used for change detection.</param>
        /// <param name="primaryKey">The primary key property used in the WHERE clause.</param>
        /// <returns>An <see cref="CommandParameters"/> containing the SQL and parameters. If no changes are detected, the CommandText will be empty.</returns>
        protected internal CommandParameters BuildUpdateCommand<T>(T entity,
            T existing, PropertyInfo primaryKey) where T : class, new()
        {
            var tableName = GetTableName<T>();
            var parameters = new List<SqlParameter>();
            var setClause = new StringBuilder();
            var unmapped = _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>);
            var properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties().ToArray())
                .Where(p => p != primaryKey && unmapped.All(up => up.Name != p.Name));

            foreach (var property in properties)
            {
                var newValue = property.GetValue(entity);
                var oldValue = property.GetValue(existing);
                if (!Equals(newValue, oldValue))
                {
                    var columnName = GetCachedColumnName(property);
                    setClause.Append($"{columnName} = @{columnName}, ");
                    parameters.Add(CreateParameter<object>($"@{columnName}", newValue, property.PropertyType));
                }
            }

            if (setClause.Length == 0) return new CommandParameters(string.Empty, parameters);

            setClause.Length -= 2;
            var pkColumn = GetCachedColumnName(primaryKey);
            parameters.Add(
                CreateParameter<object>($"@{pkColumn}", primaryKey.GetValue(entity), primaryKey.PropertyType));

            return new CommandParameters($"UPDATE {tableName} SET {setClause} WHERE {pkColumn} = @{pkColumn}", parameters);

        }

        /// <summary>
        /// Executes an UPDATE command and throws if no rows were affected.
        /// </summary>
        /// <param name="command">The configured UPDATE <see cref="SqlCommand"/> to execute.</param>
        /// <exception cref="InvalidOperationException">Thrown when the UPDATE affects zero rows.</exception>
        protected internal void ExecuteUpdate(SqlCommand command)
        {
            InvokeLogAction(command);
            var rowsAffected = command.ExecuteNonQuery();
            if (rowsAffected == 0)
                throw new InvalidOperationException("Update failed: No rows affected.");
        }

        #endregion

        #region Protected Internal Methods - Validation and Helpers

        /// <summary>
        /// Validates that the provided entity's primary key has its default value (used for inserts).
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="entity">Entity instance to validate.</param>
        /// <param name="primaryKey">The primary key property info.</param>
        /// <exception cref="InvalidOperationException">Thrown when the primary key is not the default value.</exception>
        protected internal void ValidateInsertPrimaryKey<T>(T entity, PropertyInfo primaryKey)
        {
            var defaultValue = GetDefault(primaryKey.PropertyType);
            if (!Equals(primaryKey.GetValue(entity), defaultValue))
                throw new InvalidOperationException(
                    $"Primary key must be default value for insert: {primaryKey.GetValue(entity)}");
        }

        /// <summary>
        /// Validates that the provided entity's primary key has a non-default, non-null value (used for updates).
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="entity">Entity instance to validate.</param>
        /// <param name="primaryKey">The primary key property info.</param>
        /// <exception cref="InvalidOperationException">Thrown when the primary key is null or default.</exception>
        protected internal void ValidateUpdatePrimaryKey<T>(T entity, PropertyInfo primaryKey)
        {
            var value = primaryKey.GetValue(entity);
            if (value == null || Equals(value, GetDefault(primaryKey.PropertyType)))
                throw new InvalidOperationException("Primary key must be set for update.");
        }

        /// <summary>
        /// Creates a typed <see cref="SqlParameter"/> for the given name and value and sets its SqlDbType.
        /// </summary>
        /// <typeparam name="T">Generic type used to satisfy earlier signature patterns; not used for runtime typing here.</typeparam>
        /// <param name="name">The parameter name (including @).</param>
        /// <param name="value">The value to assign; nulls are converted to <see cref="DBNull.Value"/>.</param>
        /// <param name="propertyType">The CLR property type to determine nullability.</param>
        /// <returns>A configured <see cref="SqlParameter"/> instance.</returns>
        protected internal SqlParameter CreateParameter<T>(string name, object value, Type propertyType)
            where T : class, new()
        {
            var sqlType = ParameterGenerator.GetSqlDbType(value);
            return new SqlParameter(name, value ?? DBNull.Value)
            {
                SqlDbType = sqlType,
                IsNullable = Nullable.GetUnderlyingType(propertyType) != null || propertyType.IsClass
            };
        }

        /// <summary>
        /// Logs the SQL command text and its parameters via the configured <see cref="Log"/> action, if present.
        /// </summary>
        /// <param name="command">The command to log.</param>
#if NET8_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        protected internal void InvokeLogAction(SqlCommand command)
        {
            Log?.Invoke(command.CommandText);
            foreach (var param in command.Parameters.AsEnumerable())
                Log?.Invoke($"{param.ParameterName}: {param.Value}");
        }

        /// <summary>
        /// Gets the cached primary key property for the provided type <typeparamref name="T"/>, or computes and caches it.
        /// Throws if no primary key can be determined.
        /// </summary>
        /// <typeparam name="T">The entity type whose primary key is requested.</typeparam>
        /// <returns>The <see cref="PropertyInfo"/> representing the primary key.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no primary key can be found for the type.</exception>
#if NET8_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        protected internal PropertyInfo GetCachedPrimaryKey<T>() where T : class
        {
            return _primaryKeys.GetOrAdd(typeof(T), t => GetPrimaryKeyProperty<T>() ??
                                                         throw new InvalidOperationException(
                                                             $"No primary key found for {typeof(T).FullName}"));
        }

        /// <summary>
        /// Gets or adds the cached column name for a property. Uses the property.ToDictionaryKey() as the cache key.
        /// </summary>
        /// <param name="property">The property for which to resolve a column name.</param>
        /// <returns>The resolved database column name.</returns>
        protected internal string GetCachedColumnName(PropertyInfo property)
        {
            return ColumnNames.GetOrAdd(property.ToDictionaryKey(), p => ComputeColumnName(property));
        }

        /// <summary>
        /// Cleans up the current transaction instance by disposing it, clearing state, and closing connections if appropriate.
        /// </summary>
        protected internal void CleanupTransaction()
        {
            Transaction.Dispose();
            Transaction = null;
            TransactionName = null;
            CloseConnectionIfNoTransaction();
        }

        #endregion

        #region Nested Types

        /// <summary>
        /// Simple DTO used by the provider to represent an UPDATE command and its parameters.
        /// This is the nested variant used to clearly scope the type to the provider.
        /// </summary>
        public class CommandParameters
        {
            /// <summary>
            /// Initializes a new instance of the nested <see cref="command"/> type.
            /// </summary>
            /// <param name="parameters">The SQL command text.</param>
            /// <param name="command">The parameters to attach to the command.</param>
            public CommandParameters(string command, List<SqlParameter> parameters)
            {
                CommandText = command;
                Parameters = parameters;
            }

            /// <summary>
            /// The SQL update command text.
            /// </summary>
            public string CommandText;

            /// <summary>
            /// The list of <see cref="SqlParameter"/> instances required by the command.
            /// </summary>
            public List<SqlParameter> Parameters = new List<SqlParameter>();
        }

        /// <summary>
        /// Helper type that manages a connection scope for a single operation.
        /// If the provider did not have a connection when the scope was created, the scope
        /// will dispose the created connection on Dispose (unless a transaction is active).
        /// </summary>
        internal class ConnectionScope : IDisposable
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ConnectionScope"/> class.
            /// </summary>
            /// <param name="provider">The provider that will provide or create the SqlConnection.</param>
            public ConnectionScope(SqlServerOrmDataProvider provider)
            {
                _provider = provider;
                _createdConnection = provider.Connection == null;
            }

            /// <summary>
            /// Gets the active connection for the scope. Ensures the connection is open via the provider.
            /// </summary>
            public SqlConnection Connection => _provider.GetConnection();

            /// <summary>
            /// The provider that owns the connection used by this scope.
            /// </summary>
            protected internal readonly SqlServerOrmDataProvider _provider;

            /// <summary>
            /// True when the scope created the connection (provider.Connection was null at construction).
            /// </summary>
            protected internal readonly bool _createdConnection;

            /// <summary>
            /// Disposes the scope, disposing the underlying connection if the scope created it and there is no active transaction.
            /// </summary>
            public void Dispose()
            {
                if (_provider.Transaction == null && _createdConnection && _provider.Connection != null)
                {
                    _provider.Connection.Dispose();
                    _provider.Connection = null;
                }
            }
        }

        #endregion

        #region Original Protected Helpers

        /// <summary>
        /// Returns the default value for the specified CLR <see cref="Type"/> (null for reference types,
        /// a default struct for value types).
        /// </summary>
        /// <param name="t">The type to produce a default value for.</param>
        /// <returns>The default value for the type.</returns>
        protected internal object GetDefault(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;

        /// <summary>
        /// Returns a comma separated list of column names for the specified type T, excluding properties decorated with <see cref="NotMappedAttribute"/>.
        /// </summary>
        /// <typeparam name="T">Entity type to reflect over.</typeparam>
        /// <returns>A comma separated list of resolved column names.</returns>
        protected internal string GetColumnNames<T>() where T : class, new()
        {
            DiscoverColumns<T>(); // Ensure column mappings are discovered
            var unmapped = _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>);
            return string.Join(", ", typeof(T).GetProperties()
                .Where(p => unmapped.All(up => up.Name != p.Name))
                .Select(p => GetCachedColumnName(p)));
        }

        /// <summary>
        /// Gets the table name used for the specified entity type, consulting the cache or the [Table] attribute if present.
        /// Defaults to the lower-cased CLR type name when no attribute is present.
        /// </summary>
        /// <typeparam name="T">The entity type to determine the table name for.</typeparam>
        /// <returns>The resolved table name.</returns>
        protected internal string GetTableName<T>() => _tableNames.GetOrAdd(typeof(T), t =>
            t.GetCustomAttribute<TableAttribute>()?.Name ?? t.Name.ToLower());

        /// <summary>
        /// Builds a simple WHERE clause for a primary key lookup. The caller is responsible for providing a safe key expression.
        /// </summary>
        /// <typeparam name="T">The entity type whose primary key is referenced.</typeparam>
        /// <param name="key">The primary key value to include in the clause.</param>
        /// <returns>A string starting with " WHERE " that constrains the primary key column to the provided value.</returns>
        protected string GetWhereClause<T>(dynamic key) where T : class
        {
            var pk = GetCachedPrimaryKey<T>();
            return $" WHERE {GetCachedColumnName(pk)} = {key}";
        }

        /// <summary>
        /// Computes a mapping of actual database column names to column ordinals for the provided reader.
        /// This mapping is used to quickly map reader columns to entity properties without repeated GetOrdinal calls.
        /// </summary>
        /// <param name="type">The CLR type being mapped.</param>
        /// <param name="reader">The active <see cref="SqlDataReader"/> used to inspect schema and values.</param>
        /// <returns>A dictionary mapping column name to ordinal. Comparisons ignore underscores and case.</returns>
        protected internal Dictionary<string, int> GetColumnOrdinals(Type type, SqlDataReader reader)
        {
            var ordinals = new Dictionary<string, int>(new IgnoreUnderscoreAndCaseStringComparer());
            ICollection<string> columnNames = new List<string>();
#if NET8_0_OR_GREATER
            var columnSchema = reader.GetColumnSchema();
            foreach (var dbColumn in columnSchema)
            {
                columnNames.Add(dbColumn.ColumnName);
            }
#else
                        var schemaTable = reader.GetSchemaTable();
                        foreach (DataRow row in schemaTable?.Rows)
                        {
                            columnNames.Add(row["ColumnName"].ToString());
                        }
#endif

            var comparer = new IgnoreUnderscoreAndCaseStringComparer();
            foreach (var property in _propertiesCache.GetOrAdd(type, t => t.GetProperties().ToArray()))
            {
                if (property.GetCustomAttribute<NotMappedAttribute>() != null) continue;

                var columnAttr = property.GetCustomAttribute<ColumnAttribute>();
                var actualColumnName = columnAttr?.Name;

                if (actualColumnName == null)
                {
                    // Find matching schema column using comparer semantics
                    actualColumnName = columnNames.FirstOrDefault(c => comparer.Equals(c, property.Name));
                }

                if (actualColumnName != null)
                {
                    var ordinal = reader.GetOrdinal(actualColumnName);
                    ordinals[actualColumnName] = ordinal;

                    // Populate _columnNames for future GetColumnName calls
                    _columnNames[property.Name.ToLowerInvariant()] = actualColumnName;
                }
            }
            return ordinals;
        }


        /// <summary>
        /// Computes the database column name for the given property by consulting [Column] and cached schema.
        /// Returns an empty string for properties marked with <see cref="NotMappedAttribute"/>.
        /// </summary>
        /// <param name="property">The property to compute a column name for.</param>
        /// <returns>The column name to use in SQL statements.</returns>
        protected internal string ComputeColumnName(PropertyInfo property) =>
            property.GetCustomAttribute<NotMappedAttribute>() != null
                ? string.Empty
                : property.GetCustomAttribute<ColumnAttribute>()?.Name ??
                  (_columnNames.TryGetValue(property.Name.ToLowerInvariant(), out var columnName)
                      ? columnName
                      : property.Name.ToLowerInvariant());

        /// <summary>
        /// Retrieves an existing property setter delegate for the specified property or creates a new one if it does
        /// not exist.
        /// </summary>
        /// <remarks>This method uses caching to store and reuse compiled property setter delegates for
        /// improved performance. If the property does not already have a cached setter, a new one is created using
        /// expression trees and added to the cache.</remarks>
        /// <param name="property">The <see cref="PropertyInfo"/> representing the property for which the setter delegate is required.</param>
        /// <returns>An <see cref="Action{T1, T2}"/> delegate that sets the value of the specified property.  The first parameter
        /// is the object instance, and the second parameter is the value to set.</returns>
        private static Action<object, object> GetOrCreateSetter(PropertyInfo property)
        {
            return _propertySetters.GetOrAdd(property, prop =>
            {
                var instance = Expression.Parameter(typeof(object), "instance");
                var value = Expression.Parameter(typeof(object), "value");
                var convertInstance = Expression.Convert(instance, prop.DeclaringType);
                var convertValue = Expression.Convert(value, prop.PropertyType);
                var body = Expression.Assign(Expression.Property(convertInstance, prop), convertValue);
                var lambda = Expression.Lambda<Action<object, object>>(body, instance, value);
                return lambda.Compile();
            });
        }

        /// <summary>
        /// Attempts to find the primary key property for type <typeparamref name="T"/>.
        /// The lookup checks for <see cref="KeyAttribute"/>, identity <see cref="DatabaseGeneratedAttribute"/>,
        /// and common naming patterns such as "Id" or "{TypeName}Id".
        /// </summary>
        /// <typeparam name="T">The type to inspect.</typeparam>
        /// <returns>The primary key <see cref="PropertyInfo"/> or null if no primary key can be determined.</returns>
        internal PropertyInfo GetPrimaryKeyProperty<T>() => typeof(T).GetProperties().FirstOrDefault(p =>
            p.GetCustomAttribute<KeyAttribute>() != null ||
            p.GetCustomAttribute<DatabaseGeneratedAttribute>()?.DatabaseGeneratedOption ==
            DatabaseGeneratedOption.Identity ||
            p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals($"{typeof(T).Name}Id", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Returns the properties of <typeparamref name="T"/> that are marked with <see cref="NotMappedAttribute"/>.
        /// Used to avoid attempting to map or read such properties from a data reader.
        /// </summary>
        /// <typeparam name="T">The type whose unmapped properties are requested.</typeparam>
        /// <param name="type">The CLR Type (provided by the cache accessor).</param>
        /// <returns>A collection of properties decorated with <see cref="NotMappedAttribute"/>.</returns>
        protected internal static ICollection<PropertyInfo> GetUnmappedProperties<T>(Type type)
            where T : class, new()
        {
            var properties = typeof(T).GetProperties();
            var explicitlyUnmapped = properties.Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null);
            var implicitlyUnmapped = properties.Where(p =>
            {
                if (explicitlyUnmapped.Any(up => up.Name == p.Name)) return false; // Already handled
                var columnAttr = p.GetCustomAttribute<ColumnAttribute>();
                if (columnAttr != null) return false; // Explicit column mapping
                var key = p.ToDictionaryKey();
                return !_columnNames.ContainsKey(key); // No cached column mapping
            });
            return explicitlyUnmapped.Concat(implicitlyUnmapped).ToArray();
        }

        /// <summary>
        /// Creates an <see cref="IQueryable{T}"/> backed by a <see cref="SqlLinqQueryProvider{T}"/>.
        /// The selectCommand is used as the base SELECT clause for the generated SQL.
        /// </summary>
        /// <typeparam name="T">Entity type for the queryable.</typeparam>
        /// <param name="selectCommand">Optional SELECT command used as the starting point for queries.</param>
        /// <returns>An <see cref="IQueryable{T}"/> that translates LINQ expressions to SQL when enumerated.</returns>
        private IQueryable<T> CreateQueryable<T>(string selectCommand = null) where T : class, new()
        {
            var provider = new SqlLinqQueryProvider<T>(this, selectCommand);
            return new SqlQueryable<T>(provider);
        }

        #endregion
    }
}