using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    /// <summary>
    /// Represents a SQL data provider for interacting with SQL Server databases. This class manages connections, transactions, and provides methods for data operations like querying, inserting, updating, and managing transactions.
    /// </summary>
    public partial class FunkySqlDataProvider : ISqlDataProvider
    {
        #region Fields

        protected readonly string _connectionString;
        protected SqlTransaction? _transaction;
        internal static readonly ConcurrentDictionary<Type, string> _tableNames = new();
        internal static readonly ConcurrentDictionary<string, string> _columnNames = new();
        internal static readonly ConcurrentDictionary<Type, PropertyInfo?> _primaryKeys = new();
        internal static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertiesCache = new();
        internal static readonly ConcurrentDictionary<Type, PropertyInfo[]> _unmappedPropertiesCache = new();
        internal static readonly ConcurrentDictionary<Type, Dictionary<string, int>> _columnOrdinalsCache = new();

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the action to use for logging SQL operations. This can be set to log to console, file, or any other logging mechanism.
        /// </summary>
        public Action<string>? Log { get; set; }

        /// <summary>
        /// Gets or sets the SQL connection. When set to null, a new connection will be created using <see cref="_connectionString"/>.
        /// </summary>
        public SqlConnection? Connection { get; set; }

        /// <summary>
        /// Gets or sets the current transaction. This is used for managing database transactions across multiple operations.
        /// </summary>
        public SqlTransaction? Transaction
        {
            get => _transaction;
            set => _transaction = value;
        }

        /// <summary>
        /// Gets or sets the name of the current transaction, if any. This can be used for identifying transactions in complex scenarios.
        /// </summary>
        public string? TransactionName { get; protected set; }

        #endregion

        #region Public members

        /// <summary>
        /// Initializes a new instance of the <see cref="FunkySqlDataProvider"/> class with the specified connection string and optional existing connection or transaction.
        /// </summary>
        /// <param name="connectionString">The connection string for the SQL Server database.</param>
        /// <param name="connection">An existing SQL connection to reuse. If null, a new connection will be created.</param>
        /// <param name="transaction">An existing transaction to join. If null, no transaction will be active initially.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if <paramref name="connectionString"/> is null.</exception>
        public FunkySqlDataProvider(string? connectionString, SqlConnection? connection = null, SqlTransaction? transaction = null)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            Connection = connection ?? new SqlConnection(_connectionString);
            Transaction = transaction;
        }

        /// <summary>
        /// Retrieves an entity of type <typeparamref name="T"/> by its primary key. If no key is provided, it attempts to use the default key value or throw an exception if not found.
        /// </summary>
        /// <typeparam name="T">The type of the entity to retrieve, must have a parameterless constructor.</typeparam>
        /// <param name="key">The primary key value for the entity. If null, assumes a default or auto-generated key.</param>
        /// <returns>An instance of <typeparamref name="T"/> if found, otherwise null.</returns>
        public T? Get<T>(dynamic? key = null) where T : class, new()
        {
            T? result = null;
            SqlConnection? connection = null;
            bool createdConnection = false;
            try
            {
                createdConnection = Connection == null;
                connection = GetConnection();

                using (var command = new SqlCommand(CreateSelectCommand<T>(key), connection))
                {
                    if (Transaction != null)
                        command.Transaction = Transaction;

                    command.CommandType = CommandType.Text;
                    if (Log != null)
                        Log(command.CommandText);

                    using (var reader = command.ExecuteReader())
                    {
                        var rdr = reader;
                        if (rdr.Read())
                        {
                            result = new T();
                            PropertyInfo?[] properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties());
                            var columnOrdinals = _columnOrdinalsCache.GetOrAdd(typeof(T), t => GetColumnOrdinals(t, rdr));

                            foreach (var property in properties)
                            {
                                Debug.Assert(property != null, nameof(property) + " != null");
                                if (_unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>()).Any(p => p.Name == property.Name))
                                    continue;

                                string columnName = _columnNames.GetOrAdd($"{property.ToDictionaryKey()}", GetColumnName(property));

                                if (columnOrdinals.TryGetValue(columnName, out int ordinal))
                                {
                                    object value = rdr[ordinal];
                                    if (value != DBNull.Value)
                                    {
                                        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                                        property.SetValue(result, Convert.ChangeType(value, propertyType));
                                    }
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                if (Transaction == null && createdConnection && connection != null)
                {
                    connection.Dispose();
                }
            }
            return result;
        }

        /// <summary>
        /// Queries for entities of type <typeparamref name="T"/> based on the supplied LINQ expression, converting it into a SQL WHERE clause.
        /// </summary>
        /// <typeparam name="T">The type of entities to query, must have a parameterless constructor.</typeparam>
        /// <param name="expression">A LINQ expression defining the query criteria.</param>
        /// <returns>A collection of <typeparamref name="T"/> entities matching the query criteria.</returns>
        public ICollection<T> Query<T>(Expression<Func<T, bool>> expression) where T : class, new()
        {
            ICollection<T> result = new List<T>();
            SqlConnection? connection = null;
            bool createdConnection = false;
            try
            {
                createdConnection = Connection == null;
                connection = GetConnection();

                using (var command = new SqlCommand())
                {
                    var elements = CreateSelectQuery(whereExpression: expression);
                    command.CommandText = $"{elements.SelectClause}\r\nWHERE (\r\n\t{elements.WhereClause}\r\n)";
                    if (Transaction != null)
                        command.Transaction = Transaction;

                    if (Log != null)
                    {
                        Log(command.CommandText);
                    }

                    foreach (var sqlParameter in elements.SqlParameters)
                    {
                        command.Parameters.Add(sqlParameter);
                        if (Log != null)
                        {
                            Log($"{sqlParameter.ParameterName}: {sqlParameter.Value}");
                        }
                    }

                    command.Connection = connection;

                    using (var reader = command.ExecuteReader())
                    {
                        var rdr = reader;
                        while (rdr != null && rdr.Read())
                        {
                            var item = new T();
                            PropertyInfo?[] properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties());
                            var columnOrdinals = _columnOrdinalsCache.GetOrAdd(typeof(T), t =>
                            {
                                Debug.Assert(rdr != null, nameof(rdr) + " != null");
                                return GetColumnOrdinals(t, rdr);
                            });

                            foreach (var property in properties)
                            {
                                if (_unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>()).Any(p => p.Name == property?.Name))
                                {
                                    continue;
                                }

                                Debug.Assert(property != null, nameof(property) + " != null");
                                string columnName = _columnNames.GetOrAdd($"{property.ToDictionaryKey()}", GetColumnName(property));

                                if (columnOrdinals.TryGetValue(columnName, out int ordinal))
                                {
                                    object value = reader[ordinal];
                                    if (value != DBNull.Value)
                                    {
                                        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                                        property.SetValue(item, Convert.ChangeType(value, propertyType));
                                    }
                                }
                            }
                            result.Add(item);
                        }
                    }
                }
            }
            finally
            {
                if (Transaction == null && createdConnection && connection != null)
                {
                    connection.Dispose();
                }
            }
            return result;
        }

        /// <summary>
        /// Retrieves all entities of type <typeparamref name="T"/> from the database.
        /// </summary>
        /// <typeparam name="T">The type of entities to retrieve, must have a parameterless constructor.</typeparam>
        /// <returns>A collection containing all instances of <typeparamref name="T"/> in the database.</returns>
        public ICollection<T> GetList<T>() where T : class, new()
        {
            ICollection<T> result = new List<T>();
            SqlConnection? connection = null;
            bool createdConnection = false;
            try
            {
                createdConnection = Connection == null;
                connection = GetConnection();
                
                using (var command = new SqlCommand(CreateSelectCommand<T>(), connection))
                {
                    if (Transaction != null)
                        command.Transaction = Transaction;

                    command.CommandType = CommandType.Text;
                    if (Log != null)
                        Log(command.CommandText);

                    using (var reader = command.ExecuteReader())
                    {
                        var rdr = reader;
                        while (rdr.Read())
                        {
                            var item = new T();
                            PropertyInfo?[] properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties());
                            var columnOrdinals = _columnOrdinalsCache.GetOrAdd(typeof(T), t => GetColumnOrdinals(t, rdr));

                            foreach (var property in properties)
                            {
                                Debug.Assert(property != null, nameof(property) + " != null");
                                if (_unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>()).Any(p => p.Name == property.Name))
                                {
                                    continue;
                                }

                                string columnName = _columnNames.GetOrAdd($"{property.ToDictionaryKey()}", GetColumnName(property));

                                if (columnOrdinals.TryGetValue(columnName, out int ordinal))
                                {
                                    object value = rdr[ordinal];
                                    if (value != DBNull.Value)
                                    {
                                        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                                        property.SetValue(item, Convert.ChangeType(value, propertyType));
                                    }
                                }
                            }
                            result.Add(item);
                        }
                    }
                }
            }
            finally
            {
                if (Transaction == null && createdConnection && connection != null)
                {
                    connection.Dispose();
                }
            }
            return result;
        }

        /// <summary>
        /// Inserts a new entity into the database. 
        /// </summary>
        /// <typeparam name="T">The type of the entity to insert, must have a parameterless constructor.</typeparam>
        /// <param name="entity">The entity instance to insert into the database.</param>
        /// <returns>The number of rows affected by the insert operation, typically 1 if successful.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the primary key of the entity is not set to its default value.</exception>
        public int Insert<T>(T entity) where T : class, new()
        {
            var primaryKey = _primaryKeys.GetOrAdd(typeof(T), GetPrimaryKey<T>() ?? throw new InvalidOperationException($"No primary key could by found for type '{typeof(T).FullName}'"));

            Debug.Assert(primaryKey != null, nameof(primaryKey) + " != null");
            var defaultValue = GetDefault(primaryKey.PropertyType);
            var primaryKeyValue = primaryKey.GetValue(entity);

            if (!object.Equals(primaryKeyValue, defaultValue))
            {
                throw new InvalidOperationException($"Primary key must be set to its default value for insert operations. Current value: {primaryKeyValue}");
            }

            var tableName = GetTableName<T>();
            var columns = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties())
                .Where(property =>
                    !property.GetCustomAttributes<NotMappedAttribute>().Any()
                    && !property.GetCustomAttributes<KeyAttribute>().Any())
                .Select(property => new { PropertyName = property.Name, ColumnName = _columnNames.GetOrAdd($"{property.ToDictionaryKey()}", GetColumnName(property)) })
                .ToList();

            var idParameter = new SqlParameter($"@{primaryKey.Name}", ExpressionVisitor<T>.GetSqlDbType(primaryKeyValue)) { Direction = ParameterDirection.Output };

            var parameters = new List<SqlParameter> { idParameter };

            var parameterNames = columns.Select(x => $"@{x.PropertyName}").ToList();

            foreach (var tuple in columns)
            {
                var property = _propertiesCache[typeof(T)].FirstOrDefault(x => x.Name == tuple.PropertyName);
                Debug.Assert(property != null, nameof(property) + " != null");
                var value = property.GetValue(entity);

                SqlDbType dbType = SqlDbType.NVarChar; // Default, will be overridden based on property type
                bool isNullable = true; // Default to nullable

                // Determine the correct SqlDbType and handle nullable value types
                Type? propertyType = property.PropertyType;
                Type? underlyingType = Nullable.GetUnderlyingType(propertyType);
                if (underlyingType != null)
                {
                    propertyType = underlyingType;
                    isNullable = true;
                    value ??= DBNull.Value;
                }
                else
                {
                    // Handle non-nullable types here
                    isNullable = propertyType.IsClass; // Only reference types are nullable by default
                }

                propertyType ??= underlyingType;
                if(propertyType is null)
                    Debugger.Break();
                isNullable = true;
                value ??= DBNull.Value;

                // Mapping property types to SqlDbType
                switch (Type.GetTypeCode(propertyType))
                {
                    case TypeCode.Boolean:
                        dbType = SqlDbType.Bit;
                        break;
                    case TypeCode.Byte:
                    case TypeCode.SByte:
                    case TypeCode.Int16:
                    case TypeCode.UInt16:
                        dbType = SqlDbType.SmallInt;
                        break;
                    case TypeCode.Int32:
                    case TypeCode.UInt32:
                        dbType = SqlDbType.Int;
                        break;
                    case TypeCode.Int64:
                    case TypeCode.UInt64:
                        dbType = SqlDbType.BigInt;
                        break;
                    case TypeCode.Single:
                        dbType = SqlDbType.Real;
                        break;
                    case TypeCode.Double:
                        dbType = SqlDbType.Float;
                        break;
                    case TypeCode.Decimal:
                        dbType = SqlDbType.Decimal;
                        break;
                    case TypeCode.DateTime:
                        dbType = SqlDbType.DateTime2; // Use DateTime2 for better precision
                        break;
                    case TypeCode.String:
                        dbType = SqlDbType.NVarChar;
                        break;
                    default:
                        if (propertyType == typeof(Guid))
                            dbType = SqlDbType.UniqueIdentifier;
                        else if (propertyType == typeof(byte[]))
                            dbType = SqlDbType.VarBinary;
                        else if (propertyType == typeof(TimeSpan))
                            dbType = SqlDbType.Time;
                        else
                            throw new NotSupportedException($"The type {propertyType?.Name} is not supported in this ORM.");
                        break;
                }

                // Ensure value is of the correct type if not null
                if (value != DBNull.Value)
                {
                    value = Convert.ChangeType(value, propertyType ?? throw new InvalidOperationException());
                }

                var parameter = new SqlParameter($"@{tuple.PropertyName}", value ?? DBNull.Value)
                {
                    SqlDbType = dbType,
                    IsNullable = isNullable
                };

                parameters.Add(parameter);
            }

            SqlConnection? connection = null;
            bool createdConnection = false;
            try
            {
                createdConnection = connection == null;
                connection = GetConnection();

                using (var command = new SqlCommand())
                {
                    command.Connection = connection;
                    if (Transaction != null)
                        command.Transaction = Transaction;

                    command.CommandText = $@"INSERT INTO {tableName} ({string.Join(", ", columns.Select(c => c.ColumnName))}) 
OUTPUT INSERTED.{primaryKey.Name}                                        
VALUES ({string.Join(", ", parameterNames)})";

                    command.Parameters.AddRange(parameters.ToArray());

                    if (Log != null)
                        Log(command.CommandText);

                    var result = (int)command.ExecuteScalar();
                    if (result != 0)
                        GetPrimaryKey<T>()?.SetValue(entity, (object)result);
                    return result;
                }
            }
            finally
            {
                if (Transaction == null && createdConnection && connection != null)
                {
                    connection.Dispose();
                }
            }
        }

        /// <summary>
        /// Updates an existing entity in the database by comparing its current state with the database state and only updating changed fields.
        /// </summary>
        /// <typeparam name="T">The type of the entity to update, must have a parameterless constructor.</typeparam>
        /// <param name="entity">The entity instance with updated data to persist to the database.</param>
        /// <returns>The updated entity from the database.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the entity's primary key is not set or if the entity does not exist in the database.</exception>
        public T Update<T>(T entity) where T : class, new()
        {
            var primaryKey = _primaryKeys.GetOrAdd(typeof(T), GetPrimaryKey<T>() ?? throw new InvalidOperationException("Type must have a primary key"));
            Debug.Assert(primaryKey != null, nameof(primaryKey) + " != null");
            var propertyType = primaryKey.PropertyType;
            object? defaultValue = propertyType.IsValueType ? Activator.CreateInstance(propertyType) : null;
            if (primaryKey.GetValue(entity) == null || primaryKey.GetValue(entity) == defaultValue)
            {
                throw new InvalidOperationException("Primary key must be set for update operations.");
            }

            var tableName = GetTableName<T>();
            PropertyInfo?[] properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties());
            var unmappedProperties = _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>());
            var parameters = new List<SqlParameter>();
            var setClause = new StringBuilder();

            // Get the existing entity from the database
            dynamic? id = primaryKey.GetValue(entity);
            T? existingEntity = Get<T>(id);

            if (existingEntity == null)
            {
                throw new InvalidOperationException("Entity does not exist in the database.");
            }

            var columnNameForPrimaryKey = _columnNames.GetOrAdd(primaryKey.ToDictionaryKey(), GetColumnName(primaryKey));
            var whereClause = $"{columnNameForPrimaryKey} = @{columnNameForPrimaryKey}";
            parameters.Add(new SqlParameter($"@{columnNameForPrimaryKey}", id));

            foreach (var property in properties)
            {
                Debug.Assert(property != null, nameof(property) + " != null");
                if (unmappedProperties.Any(p => p.Name == property.Name) || property == primaryKey)
                {
                    continue;
                }

                var columnName = _columnNames.GetOrAdd($"{property.ToDictionaryKey()}", GetColumnName(property));
                var existingValue = property.GetValue(existingEntity);
                var newValue = property.GetValue(entity);

                // Check if the property value has changed (considering null and DBNull)
                if ((newValue == null && existingValue != null) ||
                    (newValue != null && !newValue.Equals(existingValue)) ||
                    (existingValue == null && newValue != null))
                {
                    setClause.Append($"{columnName} = @{columnName}, ");
                    parameters.Add(new SqlParameter($"@{columnName}", newValue ?? DBNull.Value));
                }
            }

            if (setClause.Length > 0)
            {
                // Remove the trailing comma and space
                setClause.Length -= 2;

                SqlConnection? connection = null;
                bool createdConnection = false;
                try
                {
                    createdConnection = Connection == null;
                    connection = GetConnection();

                    using (var command = new SqlCommand())
                    {
                        command.Connection = connection;
                        if (Transaction != null)
                            command.Transaction = Transaction;

                        command.CommandText = $@"UPDATE {tableName} SET {setClause} WHERE {whereClause}";
                        command.Parameters.AddRange(parameters.ToArray());

                        if (Log != null)
                            Log(command.CommandText);

                        var rowsAffected = command.ExecuteNonQuery();

                        if (rowsAffected == 0)
                        {
                            throw new Exception("No rows were updated. The entity might not exist in the database or no changes were made.");
                        }
                    }
                }
                finally
                {
                    if (Transaction == null && createdConnection && connection != null)
                    {
                        connection.Dispose();
                    }
                }
            }
            else
            {
                // Log that no update was necessary since no properties changed
                if (Log != null)
                    Log($"No update necessary for entity of type {typeof(T).Name} with id {id}.");
            }

            // Return the updated entity assuming the update was successful or no update was needed
            return entity;
        }
        
        /// <summary>
        /// Begins a new transaction if none exists, using the optional transaction name.
        /// </summary>
        /// <param name="name">An optional name for the transaction, useful in nested or named transaction scenarios.</param>
        public void BeginTransaction(string? name = "")
        {
            EnsureConnectionOpen();

            if (Transaction == null)
            {
                Transaction = Connection?.BeginTransaction(IsolationLevel.ReadCommitted);
                TransactionName = name;
            }
            else
            {
                throw new InvalidOperationException("A transaction is already in progress.");
            }
        }

        /// <summary>
        /// Rolls back the current transaction if it matches the given name or if no name is provided.
        /// </summary>
        /// <param name="name">The name of the transaction to rollback, or empty to roll back any open transaction.</param>
        public void RollbackTransaction(string name = "")
        {
            if (Transaction != null && (string.IsNullOrEmpty(name) || TransactionName == name))
            {
                Transaction.Rollback();
                Transaction.Dispose();
                Transaction = null;
                TransactionName = null;
                CloseConnectionIfNoTransaction();
            }
        }

        /// <summary>
        /// Commits the current transaction if it matches the given name or if no name is provided.
        /// </summary>
        /// <param name="name">The name of the transaction to commit, or empty to commit any open transaction.</param>
        public void CommitTransaction(string name = "")
        {
            if (Transaction != null && (string.IsNullOrEmpty(name) || TransactionName == name))
            {
                Transaction.Commit();
                Transaction.Dispose();
                Transaction = null;
                TransactionName = null;
                CloseConnectionIfNoTransaction();
            }
        }



        /// <summary>
        /// Ensures the connection is open.
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
        /// Closes the connection if it is open and there is no transaction.
        /// </summary>
        protected void CloseConnectionIfNoTransaction()
        {
            if (Transaction == null && Connection != null && Connection.State == ConnectionState.Open)
            {
                Connection.Close();
                Connection.Dispose();
                Connection = null;
            }
        }

        /// <summary>
        /// Generates SQL WHERE clause elements from a LINQ expression, including the clause text and parameters.
        /// </summary>
        /// <typeparam name="T">The type of entity the expression applies to.</typeparam>
        /// <param name="expression">The LINQ expression to convert into SQL.</param>
        /// <returns>An object encapsulating the SQL WHERE clause, parameters, and the original expression.</returns>
        public WhereClauseElements<T> GenerateWhereClause<T>(Expression<Func<T, bool>> expression) where T : class, new()
        {
            var parameters = new List<SqlParameter>();
            var whereClause = new StringBuilder();

            var parameterCounter = 0;
            var visitor = new ExpressionVisitor<T>(parameters, _columnNames, _unmappedPropertiesCache, ref parameterCounter);
            visitor.Visit(expression);

            whereClause.Append(visitor.WhereClauseBody);

            return new WhereClauseElements<T>(expression, whereClause.ToString(), parameters);
        }

        /// <summary>
        /// Constructs a SQL SELECT command string for retrieving entities of type <typeparamref name="T"/>, optionally including a WHERE clause for the primary key.
        /// </summary>
        /// <typeparam name="T">The type of entity to select.</typeparam>
        /// <param name="key">The primary key value to filter the selection. If null, selects all entities.</param>
        /// <returns>A SQL SELECT command string.</returns>
        public string CreateSelectCommand<T>(dynamic? key = null) where T : class, new()
        {
            var tableName = GetTableName<T>();
            var selectClause = GetSelectClause<T>();
            var whereClause = key != null ? GetWhereClause<T>(key) : string.Empty;

            return $"SELECT {selectClause} FROM {tableName}{whereClause}";
        }

        /// <summary>
        /// Creates a SQL query with a WHERE clause based on the provided LINQ expression.
        /// </summary>
        /// <typeparam name="T">The type of entity for which to create the query.</typeparam>
        /// <param name="whereExpression">The LINQ expression to use for filtering.</param>
        /// <returns>An object containing the SELECT clause, WHERE clause, and parameters for the query.</returns>
        public WhereClauseElements<T> CreateSelectQuery<T>(Expression<Func<T, bool>> whereExpression) where T : class, new()
        {
            var selectClause = CreateSelectCommand<T>();
            var elements = GenerateWhereClause(whereExpression!);
            elements.SelectClause = selectClause;
            return elements;
        }

        /// <summary>
        /// Clears all cached mappings used for performance optimization in this class.
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

        #endregion

        #region IDisposable

        /// <summary>
        /// Releases the resources used by the <see cref="FunkySqlDataProvider"/> instance, disposing of the connection and transaction if they exist.
        /// </summary>
        public void Dispose()
        {
            Connection?.Dispose();
            Transaction?.Dispose();
        }

        #endregion

        #region Protected helper methods

        protected SqlConnection? GetConnection()
        {
            EnsureConnectionOpen();
            return Connection;
        }

        /// <summary>
        /// Retrieves the default value for a given type, useful for checking if a property has its default value.
        /// </summary>
        /// <param name="t">The type whose default value is needed.</param>
        /// <returns>The default value of the type, or null for reference types.</returns>
        protected object? GetDefault(Type t)
        {
            if (t.IsValueType)
            {
                return Activator.CreateInstance(t);
            }
            return null;
        }

        /// <summary>
        /// Constructs the SELECT clause for entities of type <typeparamref name="T"/> by listing all mapped properties.
        /// </summary>
        /// <typeparam name="T">The type of entities for which to build the SELECT clause.</typeparam>
        /// <returns>A string representing the SELECT clause for type <typeparamref name="T"/>.</returns>
        protected string GetSelectClause<T>()
        {
            return string.Join(", ", typeof(T).GetProperties()
                .Where(property => !property.GetCustomAttributes<NotMappedAttribute>().Any())
                .Select(property => _columnNames.GetOrAdd($"{property.ToDictionaryKey()}", GetColumnName(property))));
        }

        /// <summary>
        /// Determines the table name for type <typeparamref name="T"/>, using the <see cref="TableAttribute"/> if present or the type name in lowercase.
        /// </summary>
        /// <typeparam name="T">The type for which to get the table name.</typeparam>
        /// <returns>The name of the table associated with type <typeparamref name="T"/>.</returns>
        protected string GetTableName<T>()
        {
            return _tableNames.GetOrAdd(typeof(T), t =>
                t.GetCustomAttribute<TableAttribute>()?.Name ?? t.Name.ToLower());
        }

        /// <summary>
        /// Generates a WHERE clause for selecting an entity by its primary key.
        /// </summary>
        /// <typeparam name="T">The type of entity for which to generate the WHERE clause.</typeparam>
        /// <param name="key">The value of the primary key.</param>
        /// <returns>A string representing the WHERE clause for the entity's primary key.</returns>
        /// <exception cref="KeyNotFoundException">Thrown if no primary key is found for the type.</exception>
        protected string GetWhereClause<T>(dynamic key)
        {
            var primaryKey = _primaryKeys.GetOrAdd(typeof(T), t => GetPrimaryKey<T>());
            if (primaryKey == null)
                throw new KeyNotFoundException("No primary key found for the type.");

            var columnName = _columnNames.GetOrAdd(primaryKey.ToDictionaryKey(), GetColumnName(primaryKey));
            return $" WHERE {columnName} = {key}";
        }

        /// <summary>
        /// Maps property names to their column ordinals in the SQL data reader for type <paramref name="type"/>.
        /// </summary>
        /// <param name="type">The type of entity whose properties are being mapped to column ordinals.</param>
        /// <param name="reader">The SQL data reader from which to get column ordinals.</param>
        /// <returns>A dictionary with column names as keys and their ordinals as values.</returns>
        protected Dictionary<string, int> GetColumnOrdinals(Type type, SqlDataReader reader)
        {
            var ordinals = new Dictionary<string, int>();
            var schemas = reader.GetColumnSchema().Select(x => x.ColumnName).ToHashSet(new IgnoreUnderscoreAndCaseStringComparer());
            foreach (var property in _propertiesCache.GetOrAdd(type, t => t.GetProperties()))
            {
                string columnName = _columnNames.GetOrAdd($"{property.ToDictionaryKey()}", GetColumnName(property));

                try
                {
                    if (schemas.Contains(columnName) && reader.GetOrdinal(columnName) >= 0)
                    {
                        ordinals[columnName] = reader.GetOrdinal(columnName);
                    }
                }
                catch (Exception e)
                {
                    if (Log != null)
                        Log(e.ToString());
                    continue;
                }
            }
            return ordinals;
        }

        /// <summary>
        /// Retrieves the column name for a given property. If the property has a <see cref="ColumnAttribute"/>, 
        /// its Name property is used; otherwise, the property name is converted to lowercase.
        /// </summary>
        /// <param name="property">The <see cref="PropertyInfo"/> of the property to get the column name for.</param>
        /// <returns>The SQL column name as a string.</returns>
        protected string GetColumnName(PropertyInfo? property)
        {
            var info = property;
            if (info is null)
            {
                throw new ArgumentNullException(nameof(property));
            }

            return info.GetCustomAttribute<ColumnAttribute>()?.Name ?? info.Name.ToLower();
        }

        /// <summary>
        /// Identifies and returns the primary key property for the given entity type <typeparamref name="T"/>.
        /// This method checks for a <see cref="KeyAttribute"/>, an identity column via <see cref="DatabaseGeneratedAttribute"/>,
        /// or looks for common primary key naming conventions like "Id" or "TypeNameId".
        /// </summary>
        /// <typeparam name="T">The type of entity to inspect for its primary key.</typeparam>
        /// <returns>The <see cref="PropertyInfo"/> of the primary key, or null if no primary key is found.</returns>
        internal PropertyInfo? GetPrimaryKey<T>()
        {
            var type = typeof(T);
            var properties = type.GetProperties();
            var prop = properties.FirstOrDefault(p =>
                p.GetCustomAttribute<KeyAttribute>() != null ||
                (p.GetCustomAttribute<DatabaseGeneratedAttribute>()?.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity) ||
                p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Equals(type.Name + "Id", StringComparison.OrdinalIgnoreCase));
            return prop;
        }

        /// <summary>
        /// Retrieves an array of properties from type <typeparamref name="T"/> that are marked with <see cref="NotMappedAttribute"/>,
        /// indicating they should not be included in database operations.
        /// </summary>
        /// <typeparam name="T">The type whose properties are to be checked for mapping status.</typeparam>
        /// <returns>An array of <see cref="PropertyInfo"/> objects representing properties not mapped to database columns.</returns>
        internal static PropertyInfo[] GetUnmappedProperties<T>() where T : class, new()
        {
            var ret = typeof(T).GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null);
            return ret.ToArray();
        }

        #endregion

    }
}