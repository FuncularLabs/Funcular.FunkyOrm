using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer
{
    using UpdateCommand = (string CommandText, List<SqlParameter> Parameters);

    /// <summary>
    /// Provides SQL Server data access functionality implementing basic CRUD operations and transaction management.
    /// This class ensures thread-safe caching and robust database interactions while adhering to SOLID principles.
    /// </summary>
    public partial class FunkySqlDataProvider : ISqlDataProvider, IDisposable
    {
        #region Fields

        private readonly string _connectionString;
        private SqlTransaction? _transaction;
        private int _parameterCounter;
        private static readonly ConcurrentDictionary<Type, string> _tableNames = new();
        private static readonly ConcurrentDictionary<string, string> _columnNames = new();
        private static readonly ConcurrentDictionary<Type, PropertyInfo> _primaryKeys = new();
        private static readonly ConcurrentDictionary<Type, ImmutableArray<PropertyInfo>> _propertiesCache = new();
        private static readonly ConcurrentDictionary<Type, ImmutableArray<PropertyInfo>> _unmappedPropertiesCache = new();
        private static readonly ConcurrentDictionary<Type, Dictionary<string, int>> _columnOrdinalsCache = new();

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the logging action for SQL operations.
        /// </summary>
        /// <example>Log = Console.WriteLine;</example>
        public Action<string>? Log { get; set; }

        /// <summary>
        /// Gets or sets the SQL connection. Creates a new connection if set to null.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if connection cannot be established.</exception>
        public SqlConnection? Connection { get; set; }

        /// <summary>
        /// Gets or sets the current transaction for database operations.
        /// </summary>
        public SqlTransaction? Transaction
        {
            get => _transaction;
            set => _transaction = value;
        }

        /// <summary>
        /// Gets or sets the transaction name for identification.
        /// </summary>
        /// <example>"BulkUpdateTransaction"</example>
        public string? TransactionName { get; protected set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance with specified connections parameters.
        /// </summary>
        /// <param name="connectionString">SQL Server connection string.</param>
        /// <param name="connection">Optional existing connection.</param>
        /// <param name="transaction">Optional existing transaction.</param>
        /// <exception cref="ArgumentNullException">Thrown when connectionString is null.</exception>
        public FunkySqlDataProvider(string connectionString, SqlConnection? connection = null, SqlTransaction? transaction = null)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            Connection = connection ?? new SqlConnection(_connectionString);
            Transaction = transaction;
        }

        #endregion

        #region Public Methods - CRUD Operations

        /// <summary>
        /// Retrieves an entity by its primary key.
        /// </summary>
        /// <typeparam name="T">Entity type with parameterless constructor.</typeparam>
        /// <param name="key">Primary key value, null for default key.</param>
        /// <returns>The entity if found; otherwise, null.</returns>
        /// <exception cref="SqlException">Thrown on database access errors.</exception>
        public T? Get<T>(dynamic? key = null) where T : class, new()
        {
            using var connectionScope = new ConnectionScope(this);
            var commandText = CreateSelectCommand<T>(key);

            using var command = BuildCommand(commandText, connectionScope.Connection);
            return ExecuteReaderSingle<T>(command);
        }

        /// <summary>
        /// Queries entities based on a LINQ expression.
        /// </summary>
        /// <typeparam name="T">Entity type with parameterless constructor.</typeparam>
        /// <param name="expression">LINQ expression for filtering.</param>
        /// <returns>Collection of matching entities.</returns>
        /// <exception cref="SqlException">Thrown on database errors.</exception>
        public ICollection<T> Query<T>(Expression<Func<T, bool>> expression) where T : class, new()
        {
            using var connectionScope = new ConnectionScope(this);
            var elements = CreateSelectQuery(expression);

            using var command = BuildCommand(elements.SelectClause + "\r\nWHERE " + elements.WhereClause,
                connectionScope.Connection, elements.SqlParameters);
            return ExecuteReaderList<T>(command);
        }

        /// <summary>
        /// Retrieves all entities of a type.
        /// </summary>
        /// <typeparam name="T">Entity type with parameterless constructor.</typeparam>
        /// <returns>Collection of all entities.</returns>
        /// <exception cref="SqlException">Thrown on database errors.</exception>
        public ICollection<T> GetList<T>() where T : class, new()
        {
            using var connectionScope = new ConnectionScope(this);
            using var command = BuildCommand(CreateSelectCommand<T>(), connectionScope.Connection);
            return ExecuteReaderList<T>(command);
        }

        /// <summary>
        /// Inserts a new entity into the database.
        /// </summary>
        /// <typeparam name="T">Entity type with parameterless constructor.</typeparam>
        /// <param name="entity">Entity to insert.</param>
        /// <returns>Number of affected rows.</returns>
        /// <exception cref="InvalidOperationException">Thrown if primary key is set.</exception>
        public int Insert<T>(T entity) where T : class, new()
        {
            var primaryKey = GetPrimaryKeyCached<T>();
            ValidateInsertPrimaryKey(entity, primaryKey);

            using var connectionScope = new ConnectionScope(this);
            var insertCommand = BuildInsertCommand(entity, primaryKey);

            using var command = BuildCommand(insertCommand.CommandText, connectionScope.Connection,
                insertCommand.Parameters);
            return ExecuteInsert(command, entity, primaryKey);
        }

        /// <summary>
        /// Updates an existing entity in the database.
        /// </summary>
        /// <typeparam name="T">Entity type with parameterless constructor.</typeparam>
        /// <param name="entity">Entity with updated data.</param>
        /// <returns>Updated entity.</returns>
        /// <exception cref="InvalidOperationException">Thrown if entity doesn't exist or key is unset.</exception>
        public T Update<T>(T entity) where T : class, new()
        {
            var primaryKey = GetPrimaryKeyCached<T>();
            ValidateUpdatePrimaryKey(entity, primaryKey);

            using var connectionScope = new ConnectionScope(this);
            var existingEntity = Get<T>((dynamic)primaryKey.GetValue(entity));
            if (existingEntity == null)
                throw new InvalidOperationException("Entity does not exist in database.");

            UpdateCommand updateCommand = BuildUpdateCommand(entity, existingEntity, primaryKey);
            if (updateCommand.Parameters.Any())
            {
                using var command = BuildCommand(updateCommand.CommandText, connectionScope.Connection,
                    updateCommand.Parameters);
                ExecuteUpdate(command);
            }
            else
            {
                Log?.Invoke($"No update needed for {typeof(T).Name} with id {primaryKey.GetValue(entity)}.");
            }

            return entity;
        }

        #endregion

        #region Public Methods - Transaction Management

        /// <summary>
        /// Begins a new transaction with optional name.
        /// </summary>
        /// <param name="name">Transaction identifier.</param>
        /// <exception cref="InvalidOperationException">Thrown if transaction already exists.</exception>
        public void BeginTransaction(string? name = null)
        {
            EnsureConnectionOpen();
            if (Transaction != null)
                throw new InvalidOperationException("Transaction already in progress.");

            Transaction = Connection?.BeginTransaction(IsolationLevel.ReadCommitted);
            TransactionName = name ?? string.Empty;
        }

        /// <summary>
        /// Commits the current transaction if matching name or no name provided.
        /// </summary>
        /// <param name="name">Transaction name to match.</param>
        public void CommitTransaction(string? name = null)
        {
            if (Transaction != null && (string.IsNullOrEmpty(name) || TransactionName == name))
            {
                Transaction.Commit();
                CleanupTransaction();
            }
        }

        /// <summary>
        /// Rolls back the current transaction if matching name or no name provided.
        /// </summary>
        /// <param name="name">Transaction name to match.</param>
        public void RollbackTransaction(string? name = null)
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
        /// Clears all cached mappings.
        /// </summary>
        public static void ClearMappings()
        {
            _tableNames.Clear();
            _columnNames.Clear();
            _primaryKeys.Clear();
            _propertiesCache.Clear();
            _unmappedPropertiesCache.Clear();
            _columnOrdinalsCache.Clear();
        }

        /// <summary>
        /// Disposes of managed resources.
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
        /// Ensures the connection is open and returns it.
        /// </summary>
        /// <returns>Open SQL connection.</returns>
        protected SqlConnection GetConnection()
        {
            EnsureConnectionOpen();
            return Connection!;
        }

        /// <summary>
        /// Opens connection if not already open.
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
        /// Closes connection if no active transaction.
        /// </summary>
        protected void CloseConnectionIfNoTransaction()
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
        /// Creates a SELECT command for entity retrieval.
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="key">Optional primary key filter.</param>
        /// <returns>SQL SELECT command string.</returns>
        protected string CreateSelectCommand<T>(dynamic? key = null) where T : class, new()
        {
            var tableName = GetTableName<T>();
            var selectClause = GetSelectClause<T>();
            var whereClause = key != null ? GetWhereClause<T>(key) : string.Empty;
            return $"SELECT {selectClause} FROM {tableName}{whereClause}";
        }

        /// <summary>
        /// Creates a query with WHERE clause from LINQ expression.
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="whereExpression">LINQ filter expression.</param>
        /// <returns>Query elements including SELECT and WHERE clauses.</returns>
        protected WhereClauseElements<T> CreateSelectQuery<T>(Expression<Func<T, bool>> whereExpression) where T : class, new()
        {
            var selectClause = CreateSelectCommand<T>();
            var elements = GenerateWhereClause(whereExpression);
            elements.SelectClause = selectClause;
            return elements;
        }

        /// <summary>
        /// Generates WHERE clause from LINQ expression.
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="expression">LINQ filter expression.</param>
        /// <returns>WHERE clause elements.</returns>
        protected WhereClauseElements<T> GenerateWhereClause<T>(Expression<Func<T, bool>> expression) where T : class, new()
        {
            var visitor = new ExpressionVisitor<T>(new List<SqlParameter>(), _columnNames,
                _unmappedPropertiesCache, ref _parameterCounter);
            visitor.Visit(expression);
            return new WhereClauseElements<T>(expression, visitor.WhereClauseBody, visitor.Parameters);
        }

        #endregion

        #region Private Methods - Execution Helpers

        private T? ExecuteReaderSingle<T>(SqlCommand command) where T : class, new()
        {
            
            InvokeLogAction(command);
            using var reader = command.ExecuteReader();
            return reader.Read() ? MapEntity<T>(reader) : null;
        }

        private ICollection<T> ExecuteReaderList<T>(SqlCommand command) where T : class, new()
        {
            
            InvokeLogAction(command);
            var results = new List<T>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(MapEntity<T>(reader));
            }
            return results;
        }

        private SqlCommand BuildCommand(string commandText, SqlConnection connection,
            IEnumerable<SqlParameter>? parameters = null)
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

        private T MapEntity<T>(SqlDataReader reader) where T : class, new()
        {
            var entity = new T();
            var properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties().ToImmutableArray());
            var columnOrdinals = _columnOrdinalsCache.GetOrAdd(typeof(T), t => GetColumnOrdinals(t, reader));
            var unmapped = _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>);

            foreach (var property in properties)
            {
                if (unmapped.Any(p => p.Name == property.Name)) continue;

                var columnName = GetColumnNameCached(property);
                if (columnOrdinals.TryGetValue(columnName, out int ordinal))
                {
                    var value = reader[ordinal];
                    if (value != DBNull.Value)
                    {
                        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                        property.SetValue(entity, Convert.ChangeType(value, propertyType));
                    }
                }
            }
            return entity;
        }

        #endregion

        #region Private Methods - Insert/Update Helpers

        private UpdateCommand BuildInsertCommand<T>(T entity, PropertyInfo primaryKey)
            where T : class, new()
        {
            var tableName = GetTableName<T>();
            var properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties().ToImmutableArray())
                .Where(p => !p.GetCustomAttributes<NotMappedAttribute>().Any() && p != primaryKey);
            var parameters = new List<SqlParameter>();
            var columns = new List<string>();
            var parameterNames = new List<string>();

            foreach (var property in properties)
            {
                var columnName = GetColumnNameCached(property);
                var value = property.GetValue(entity);
                var parameterName = $"@{property.Name}";

                columns.Add(columnName);
                parameterNames.Add(parameterName);
                parameters.Add(CreateParameter<object>(parameterName, value, property.PropertyType));
            }

            var commandText = $@"
                INSERT INTO {tableName} ({string.Join(", ", columns)})
                OUTPUT INSERTED.{GetColumnNameCached(primaryKey)}
                VALUES ({string.Join(", ", parameterNames)})";
            return (commandText, parameters);
        }

        private int ExecuteInsert<T>(SqlCommand command, T entity, PropertyInfo primaryKey) where T : class, new()
        {
            
            InvokeLogAction(command);
            var result = (int)command.ExecuteScalar();
            if (result != 0)
                primaryKey.SetValue(entity, result);
            return result;
        }

        private UpdateCommand BuildUpdateCommand<T>(T entity, T existing, PropertyInfo primaryKey)
            where T : class, new()
        {
            var tableName = GetTableName<T>();
            var parameters = new List<SqlParameter>();
            var setClause = new StringBuilder();
            var properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties().ToImmutableArray())
                .Where(p => p != primaryKey && !p.GetCustomAttributes<NotMappedAttribute>().Any());

            foreach (var property in properties)
            {
                var newValue = property.GetValue(entity);
                var oldValue = property.GetValue(existing);
                if (!Equals(newValue, oldValue))
                {
                    var columnName = GetColumnNameCached(property);
                    setClause.Append($"{columnName} = @{columnName}, ");
                    parameters.Add(CreateParameter<object>($"@{columnName}", newValue, property.PropertyType));
                }
            }

            if (setClause.Length == 0) return (string.Empty, parameters);

            setClause.Length -= 2; // Remove trailing ", "
            var pkColumn = GetColumnNameCached(primaryKey);
            parameters.Add(CreateParameter<object>($"@{pkColumn}", primaryKey.GetValue(entity), primaryKey.PropertyType));

            return ($"UPDATE {tableName} SET {setClause} WHERE {pkColumn} = @{pkColumn}", parameters);
        }

        private void ExecuteUpdate(SqlCommand command)
        {
            
            InvokeLogAction(command);
            var rowsAffected = command.ExecuteNonQuery();
            if (rowsAffected == 0)
                throw new InvalidOperationException("Update failed: No rows affected.");
        }

        #endregion

        #region Private Methods - Validation and Helpers

        private void ValidateInsertPrimaryKey<T>(T entity, PropertyInfo primaryKey)
        {
            var defaultValue = GetDefault(primaryKey.PropertyType);
            if (!Equals(primaryKey.GetValue(entity), defaultValue))
                throw new InvalidOperationException($"Primary key must be default value for insert: {primaryKey.GetValue(entity)}");
        }

        private void ValidateUpdatePrimaryKey<T>(T entity, PropertyInfo primaryKey)
        {
            var value = primaryKey.GetValue(entity);
            if (value == null || Equals(value, GetDefault(primaryKey.PropertyType)))
                throw new InvalidOperationException("Primary key must be set for update.");
        }

        private SqlParameter CreateParameter<T>(string name, object? value, Type propertyType) where T : class, new()
        {
            var sqlType = ExpressionVisitor<T>.GetSqlDbType(value);
            return new SqlParameter(name, value ?? DBNull.Value)
            {
                SqlDbType = sqlType,
                IsNullable = Nullable.GetUnderlyingType(propertyType) != null || propertyType.IsClass
            };
        }

        private void InvokeLogAction(SqlCommand command)
        {
            Log?.Invoke(command.CommandText);
            foreach (var param in command.Parameters.AsEnumerable())
                Log?.Invoke($"{param.ParameterName}: {param.Value}");
        }

        private PropertyInfo GetPrimaryKeyCached<T>() where T : class
        {
            return _primaryKeys.GetOrAdd(typeof(T), t => GetPrimaryKey<T>() ??
                throw new InvalidOperationException($"No primary key found for {typeof(T).FullName}"));
        }

        private string GetColumnNameCached(PropertyInfo property)
        {
            return _columnNames.GetOrAdd(property.ToDictionaryKey(), p => GetColumnName(property));
        }

        private void CleanupTransaction()
        {
            Transaction?.Dispose();
            Transaction = null;
            TransactionName = null;
            CloseConnectionIfNoTransaction();
        }

        #endregion

        #region Nested Types

        private class ConnectionScope : IDisposable
        {
            private readonly FunkySqlDataProvider _provider;
            private readonly bool _createdConnection;

            public SqlConnection Connection => _provider.GetConnection();

            public ConnectionScope(FunkySqlDataProvider provider)
            {
                _provider = provider;
                _createdConnection = provider.Connection == null;
            }

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

        #region Original Protected Helpers (unchanged signatures)

        protected object? GetDefault(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;
        protected string GetSelectClause<T>() => string.Join(", ", typeof(T).GetProperties()
            .Where(p => !p.GetCustomAttributes<NotMappedAttribute>().Any())
            .Select(p => GetColumnNameCached(p)));
        protected string GetTableName<T>() => _tableNames.GetOrAdd(typeof(T), t =>
            t.GetCustomAttribute<TableAttribute>()?.Name ?? t.Name.ToLower());
        protected string GetWhereClause<T>(dynamic key) where T : class
        {
            var pk = GetPrimaryKeyCached<T>();
            return $" WHERE {GetColumnNameCached(pk)} = {key}";
        }
        protected Dictionary<string, int> GetColumnOrdinals(Type type, SqlDataReader reader)
        {
            var ordinals = new Dictionary<string, int>();
            var schemas = reader.GetColumnSchema().Select(x => x.ColumnName)
                .ToHashSet(new IgnoreUnderscoreAndCaseStringComparer());
            foreach (var property in _propertiesCache.GetOrAdd(type, t => t.GetProperties().ToImmutableArray()))
            {
                var columnName = GetColumnNameCached(property);
                if (schemas.Contains(columnName))
                    ordinals[columnName] = reader.GetOrdinal(columnName);
            }
            return ordinals;
        }
        protected string GetColumnName(PropertyInfo property) =>
            property.GetCustomAttribute<ColumnAttribute>()?.Name ?? property.Name.ToLower();
        internal PropertyInfo? GetPrimaryKey<T>() => typeof(T).GetProperties().FirstOrDefault(p =>
            p.GetCustomAttribute<KeyAttribute>() != null ||
            p.GetCustomAttribute<DatabaseGeneratedAttribute>()?.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity ||
            p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals(typeof(T).Name + "Id", StringComparison.OrdinalIgnoreCase));
        internal static ImmutableArray<PropertyInfo> GetUnmappedProperties<T>(Type type) where T : class, new() =>
            typeof(T).GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToImmutableArray();

        #endregion
    }
}