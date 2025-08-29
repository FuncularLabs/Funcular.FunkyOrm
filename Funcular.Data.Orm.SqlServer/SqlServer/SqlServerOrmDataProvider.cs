using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.Common;
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
        internal static readonly ConcurrentDictionary<Type, Dictionary<string, int>> _columnOrdinalsCache = new ConcurrentDictionary<Type, Dictionary<string, int>>();

        /// <summary>
        /// Tracks which types have had their mappings discovered (to avoid repeated database schema calls).
        /// </summary>
        internal static readonly HashSet<Type> _mappedTypes = new HashSet<Type> { };

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

                UpdateCommand updateCommand = BuildUpdateCommand(entity, existingEntity, primaryKey);
                if (updateCommand.Parameters.Any())
                {
                    using (var command = BuildSqlCommandObject(updateCommand.CommandText, connectionScope.Connection,
                               updateCommand.Parameters))
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
        /// </summary>
        /// <typeparam name="T">The entity type for the queryable.</typeparam>
        /// <returns>An <see cref="IQueryable{T}"/> instance that will translate LINQ expressions to SQL when enumerated.</returns>
        public IQueryable<T> Query<T>() where T : class, new()
        {
            string selectCommand = CreateGetOneOrSelectCommandText<T>();
            return CreateQueryable<T>(selectCommand);
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

        /*public static void ClearMappings()
        {
            _tableNames.Clear();
            ColumnNames.Clear();
            _primaryKeys.Clear();
            _propertiesCache.Clear();
            _unmappedPropertiesCache.Clear();
            _columnOrdinalsCache.Clear();
        }*/

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
        /// Maps the current row of the provided <see cref="SqlDataReader"/> to an instance of <typeparamref name="T"/>.
        /// Uses cached ordinals and conversion to handle nullable and value types correctly.
        /// </summary>
        /// <typeparam name="T">The entity type to map.</typeparam>
        /// <param name="reader">An active <see cref="SqlDataReader"/> positioned at a valid row.</param>
        /// <returns>An instantiated and populated <typeparamref name="T"/> instance.</returns>
        protected T MapEntity<T>(SqlDataReader reader) where T : class, new()
        {
            var properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties());
            var unmappedProperties = _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>);
            var columnOrdinals = _columnOrdinalsCache.GetOrAdd(typeof(T), t => GetColumnOrdinals(t, reader));
            var entity = new T();
            foreach (var property in properties)
            {
                if (unmappedProperties.Any(p => p.Name == property.Name)) continue;

                var columnName = GetCachedColumnName(property);
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

        /// <summary>
        /// Builds an INSERT statement and the corresponding parameter list for the provided entity.
        /// Excludes properties decorated with <see cref="NotMappedAttribute"/> and the provided primary key.
        /// </summary>
        /// <typeparam name="T">The entity type to insert.</typeparam>
        /// <param name="entity">The entity instance from which to read values.</param>
        /// <param name="primaryKey">The primary key property info for the entity type.</param>
        /// <returns>A tuple containing CommandText and the list of SqlParameter instances.</returns>
        protected internal (string CommandText, List<SqlParameter> Parameters) BuildInsertCommandObject<T>(T entity,
            PropertyInfo primaryKey) where T : class, new()
        {
            var tableName = GetTableName<T>();
            // Properties that are not marked with [NotMapped] and are not the primary key
            var includedProperties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties().ToArray())
                .Where(p => !p.GetCustomAttributes<NotMappedAttribute>().Any() && p != primaryKey);
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
            return (commandText, parameters);
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
        /// Simple DTO used by the provider to represent an UPDATE command and its parameters.
        /// This is the nested variant used to clearly scope the type to the provider.
        /// </summary>
        public class UpdateCommand
        {
            /// <summary>
            /// The SQL update command text.
            /// </summary>
            public string CommandText;

            /// <summary>
            /// The list of <see cref="SqlParameter"/> instances required by the command.
            /// </summary>
            public List<SqlParameter> Parameters = new List<SqlParameter>();

            /// <summary>
            /// Initializes a new instance of the nested <see cref="UpdateCommand"/> type.
            /// </summary>
            /// <param name="command">The SQL command text.</param>
            /// <param name="parameters">The parameters to attach to the command.</param>
            public UpdateCommand(string command, List<SqlParameter> parameters)
            {
                CommandText = command;
                Parameters = parameters;
            }
        }

        /// <summary>
        /// Builds an UPDATE statement for the specified entity by comparing it to the existing persisted instance.
        /// Only properties whose values have changed are included in the SET clause.
        /// </summary>
        /// <typeparam name="T">The entity type to update.</typeparam>
        /// <param name="entity">The new entity values.</param>
        /// <param name="existing">The existing persisted entity used for change detection.</param>
        /// <param name="primaryKey">The primary key property used in the WHERE clause.</param>
        /// <returns>An <see cref="UpdateCommand"/> containing the SQL and parameters. If no changes are detected, the CommandText will be empty.</returns>
        protected internal UpdateCommand BuildUpdateCommand<T>(T entity,
            T existing, PropertyInfo primaryKey) where T : class, new()
        {
            var tableName = GetTableName<T>();
            var parameters = new List<SqlParameter>();
            var setClause = new StringBuilder();
            var properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties().ToArray())
                .Where(p => p != primaryKey && !p.GetCustomAttributes<NotMappedAttribute>().Any());

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

            if (setClause.Length == 0) return new UpdateCommand(string.Empty, parameters);

            setClause.Length -= 2;
            var pkColumn = GetCachedColumnName(primaryKey);
            parameters.Add(
                CreateParameter<object>($"@{pkColumn}", primaryKey.GetValue(entity), primaryKey.PropertyType));

            return new UpdateCommand($"UPDATE {tableName} SET {setClause} WHERE {pkColumn} = @{pkColumn}", parameters);

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
        /// Helper type that manages a connection scope for a single operation.
        /// If the provider did not have a connection when the scope was created, the scope
        /// will dispose the created connection on Dispose (unless a transaction is active).
        /// </summary>
        internal class ConnectionScope : IDisposable
        {
            /// <summary>
            /// The provider that owns the connection used by this scope.
            /// </summary>
            protected internal readonly SqlServerOrmDataProvider _provider;

            /// <summary>
            /// True when the scope created the connection (provider.Connection was null at construction).
            /// </summary>
            protected internal readonly bool _createdConnection;

            /// <summary>
            /// Gets the active connection for the scope. Ensures the connection is open via the provider.
            /// </summary>
            public SqlConnection Connection => _provider.GetConnection();

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
        protected internal string GetColumnNames<T>() => string.Join(", ", typeof(T).GetProperties()
            .Where(p => !p.GetCustomAttributes<NotMappedAttribute>().Any())
            .Select(p => GetCachedColumnName(p)));

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
        /// Attempts to find the primary key property for type <typeparamref name="T"/>.
        /// The lookup checks for <see cref="KeyAttribute"/>, identity <see cref="DatabaseGeneratedAttribute"/>,
        /// and common naming patterns such as "Id" or "{TypeName}Id".
        /// </summary>
        /// <typeparam name="T">The type to inspect.</typeparam>
        /// <returns>The primary key <see cref="PropertyInfo"/> or null if none found.</returns>
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
            where T : class, new() =>
            typeof(T).GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToArray();

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