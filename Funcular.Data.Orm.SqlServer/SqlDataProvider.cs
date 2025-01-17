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
    /// <summary>Class SqlDataProvider.</summary>
    public partial class SqlDataProvider : ISqlRepository
    {
        #region Fields

        protected readonly string _connectionString;
        private SqlTransaction? _transaction;
        internal static readonly ConcurrentDictionary<Type, string> _tableNames = new();
        internal static readonly ConcurrentDictionary<PropertyInfo, string> _columnNames = new();
        internal static readonly ConcurrentDictionary<Type, PropertyInfo> _primaryKeys = new();
        internal static readonly ConcurrentDictionary<Type, PropertyInfo[]> _propertiesCache = new();
        internal static readonly ConcurrentDictionary<Type, PropertyInfo[]> _unmappedPropertiesCache = new();
        internal static readonly ConcurrentDictionary<Type, Dictionary<string, int>> _columnOrdinalsCache = new();

        #endregion



        #region Properties

        /// <summary>
        /// Gets or sets the log action (e.g., write to console).
        /// </summary>
        /// <value>The log.</value>
        Action<string> ISqlRepository.Log
        {
            get => Log;
            set => Log = value;
        }

        /// <summary>
        /// Gets or sets the connection.
        /// </summary>
        /// <value>The connection.</value>
        SqlConnection? ISqlRepository.Connection
        {
            get => Connection;
            set => Connection = value;
        }

        /// <summary>
        /// Gets or sets the connection.
        /// </summary>
        /// <value>The connection.</value>
        public SqlConnection? Connection { get; set; }

        /// <summary>
        /// Gets or sets the transaction.
        /// </summary>
        /// <value>The transaction.</value>
        public SqlTransaction? Transaction
        {
            get => _transaction;
            set => _transaction = value;
        }

        /// <summary>
        /// Gets or sets the name of the transaction.
        /// </summary>
        /// <value>The name of the transaction.</value>
        public string? TransactionName { get; private set; }

        #endregion



        #region Public members

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlDataProvider"/> class.
        /// </summary>
        /// <param name="connectionString">The connection string.</param>
        /// <param name="connection">An existing connection. If null, a new connection will be created.</param>
        /// <param name="transaction">An existing transaction to join. If null, no transaction is used by default.</param>
        /// <exception cref="System.ArgumentNullException">connectionString</exception>
        public SqlDataProvider(string connectionString, SqlConnection? connection = null, SqlTransaction? transaction = null)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            Connection = connection ?? new SqlConnection(_connectionString);
            Transaction = transaction;
        }

        public Action<string>? Log { get; set; }


        /// <summary>
        /// Gets the entity having the specified key, if it exists.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">The key.</param>
        /// <returns>System.Nullable&lt;T&gt;.</returns>
        public T? Get<T>(dynamic? key = null) where T : class, new()
        {
            T? result = null;
            SqlConnection? connection = null;
            bool createdConnection = false;
            try
            {
                connection = GetConnection();
                createdConnection = Connection == null;

                using (var command = new SqlCommand(CreateSelectCommand<T>(key), connection))
                {
                    if (Transaction != null)
                        command.Transaction = Transaction;

                    command.CommandType = CommandType.Text;
                    if (Log != null)
                        Log(command.CommandText);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            result = new T();
                            var properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties());
                            var columnOrdinals = _columnOrdinalsCache.GetOrAdd(typeof(T), t => GetColumnOrdinals(t, reader));

                            foreach (var property in properties)
                            {
                                if (_unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>()).Any(p => p.Name == property.Name))
                                    continue;

                                string columnName = _columnNames.GetOrAdd(property, GetColumnName);

                                if (columnOrdinals.TryGetValue(columnName, out int ordinal))
                                {
                                    object value = reader[ordinal];
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
                if (createdConnection && connection != null)
                {
                    connection.Dispose();
                }
            }
            return result;
        }

        /*/// <summary>
        /// Queries the specified entity type using the specified expression as the WHERE clause.
        /// Parameterizes the resulting query.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="expression">The expression.</param>
        /// <returns>ICollection&lt;T&gt;.</returns>
        ICollection<T> ISqlRepository.Query<T>(Expression<Func<T, bool>> expression)
        {
            return null;
        }

        /// <summary>
        /// Gets the entire list of entities of type T.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>ICollection&lt;T&gt;.</returns>
        ICollection<T> ISqlRepository.GetList<T>()
        {
            return null;
        }*/

        /// <summary>
        /// Queries the specified entity type using the specified expression as the WHERE clause.
        /// Parameterizes the resulting query.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="expression">The expression.</param>
        /// <returns>ICollection&lt;T&gt;.</returns>
        public ICollection<T> Query<T>(Expression<Func<T, bool>> expression) where T : class, new()
        {
            ICollection<T> result = new List<T>();
            SqlConnection? connection = null;
            bool createdConnection = false;
            try
            {
                connection = GetConnection();
                createdConnection = Connection == null;

                using (var command = new SqlCommand())
                {
                    command.Connection = connection;
                    if (Transaction != null)
                        command.Transaction = Transaction;

                    var elements = CreateSelectQuery(whereExpression: expression);
                    command.CommandText = $"{elements.SelectClause}\r\nWHERE (\r\n\t{elements.WhereClause}\r\n)";
                    foreach (var sqlParameter in elements.SqlParameters)
                    {
                        command.Parameters.Add(sqlParameter);
                    }

                    if (Log != null)
                        Log(command.CommandText);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var item = new T();
                            var properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties());
                            var columnOrdinals = _columnOrdinalsCache.GetOrAdd(typeof(T), t => GetColumnOrdinals(t, reader));

                            foreach (var property in properties)
                            {
                                if (_unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>()).Any(p => p.Name == property.Name))
                                {
                                    continue;
                                }

                                string columnName = _columnNames.GetOrAdd(property, GetColumnName);

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
                if (createdConnection && connection != null)
                {
                    connection.Dispose();
                }
            }
            return result;
        }

        /// <summary>
        /// Gets the entire list of entities of type T.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>ICollection&lt;T&gt;.</returns>
        public ICollection<T> GetList<T>() where T : class, new()
        {
            ICollection<T> result = new List<T>();
            SqlConnection? connection = null;
            bool createdConnection = false;
            try
            {
                connection = GetConnection();
                createdConnection = Connection == null;

                using (var command = new SqlCommand(CreateSelectCommand<T>(), connection))
                {
                    if (Transaction != null)
                        command.Transaction = Transaction;

                    command.CommandType = CommandType.Text;
                    if (Log != null)
                        Log(command.CommandText);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var item = new T();
                            var properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties());
                            var columnOrdinals = _columnOrdinalsCache.GetOrAdd(typeof(T), t => GetColumnOrdinals(t, reader));

                            foreach (var property in properties)
                            {
                                if (_unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>()).Any(p => p.Name == property.Name))
                                {
                                    continue;
                                }

                                string columnName = _columnNames.GetOrAdd(property, GetColumnName);

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
                if (createdConnection && connection != null)
                {
                    connection.Dispose();
                }
            }
            return result;
        }

        /// <summary>
        /// Inserts the provided entity into the database.
        /// </summary>
        /// <typeparam name="T">The type of entity to insert. Must have a parameterless constructor.</typeparam>
        /// <param name="entity">The entity to insert.</param>
        /// <returns>The number of rows affected by the insert operation.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the primary key does not have its default value.</exception>
        public int Insert<T>(T entity) where T : class, new()
        {
            var primaryKey = _primaryKeys.GetOrAdd(typeof(T), GetPrimaryKey<T>() ?? throw new InvalidOperationException($"No primary key could by found for type '{typeof(T).FullName}'"));

            var defaultValue = GetDefault(primaryKey.PropertyType);
            var primaryKeyValue = primaryKey.GetValue(entity);

            if (!object.Equals(primaryKeyValue, defaultValue))
            {
                throw new InvalidOperationException($"Primary key must be set to its default value for insert operations. Current value: {primaryKeyValue}");
            }

            var tableName = GetTableName<T>();
            var columns = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties())
                .Where(p => 
                    !p.GetCustomAttributes<NotMappedAttribute>().Any() 
                    && !p.GetCustomAttributes<KeyAttribute>().Any())
                .Select(p => new { PropertyName = p.Name, ColumnName = _columnNames.GetOrAdd(p, GetColumnName) })
                .ToList();

            var idParameter = new SqlParameter($"@{primaryKey.Name}", ExpressionVisitor<T>.GetSqlDbType(primaryKeyValue)){ Direction = ParameterDirection.Output };

            var parameters = new List<SqlParameter> { idParameter };

            var parameterNames = columns.Select(x => $"@{x.PropertyName}").ToList();

            foreach (var tuple in columns)
            {
                var property = _propertiesCache[typeof(T)].FirstOrDefault(x => x.Name == tuple.PropertyName);
                var value = property?.GetValue(entity);
                parameters.Add(new SqlParameter($"@{tuple.PropertyName}", value ?? DBNull.Value));
            }

            SqlConnection? connection = null;
            bool createdConnection = false;
            try
            {
                connection = GetConnection();
                createdConnection = Connection == null;

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
                    // TODO: Find out why the output parameter's value is not being set with this technique:
                    // var commandParameter = command.Parameters[idParameter.ParameterName];
                    // var id = commandParameter.Value;
                    return result; //(int)id; //executeNonQuery;
                }
            }
            finally
            {
                if (createdConnection && connection != null)
                {
                    connection.Dispose();
                }
            }
        }

        /// <summary>
        /// Updates the provided entity in the database.
        /// </summary>
        /// <typeparam name="T">The type of entity to update. Must have a parameterless constructor.</typeparam>
        /// <param name="entity">The entity to update.</param>
        /// <returns>The updated entity.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the primary key value is not set.</exception>
        public T Update<T>(T entity) where T : class, new()
        {
            var primaryKey = _primaryKeys.GetOrAdd(typeof(T), GetPrimaryKey<T>() ?? throw new InvalidOperationException("Type must have a primary key"));
            var propertyType = primaryKey.PropertyType;
            object? defaultValue = propertyType.IsValueType ? Activator.CreateInstance(propertyType) : null;
            if (primaryKey.GetValue(entity) == null || primaryKey.GetValue(entity) == defaultValue)
            {
                throw new InvalidOperationException("Primary key must be set for update operations.");
            }

            var tableName = GetTableName<T>();
            var properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties());
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

            var columnNameForPrimaryKey = _columnNames.GetOrAdd(primaryKey, GetColumnName);
            var whereClause = $"{columnNameForPrimaryKey} = @{columnNameForPrimaryKey}";
            parameters.Add(new SqlParameter($"@{columnNameForPrimaryKey}", id));

            foreach (var property in properties)
            {
                if (unmappedProperties.Any(p => p.Name == property.Name) || property == primaryKey)
                {
                    continue;
                }

                var columnName = _columnNames.GetOrAdd(property, GetColumnName);
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
                    connection = GetConnection();
                    createdConnection = Connection == null;

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
                    if (createdConnection && connection != null)
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
        /// Begins a new transaction (only if the <see cref="Transaction"/> property is null).
        /// </summary>
        /// <param name="name">Optional name for the transaction.</param>
        public void BeginTransaction(string? name = "")
        {
            TransactionName = name;
            Transaction ??= GetConnection()?.BeginTransaction(IsolationLevel.ReadCommitted, TransactionName);
        }

        /// <summary>
        /// Rolls back the current transaction if one exists.
        /// </summary>
        /// <param name="name">Optional name to match when rolling back.</param>
        public void RollbackTransaction(string name = "")
        {
            if (Transaction != null && (string.IsNullOrEmpty(name)) || TransactionName == name)
            {
                Debug.Assert(Transaction != null, nameof(Transaction) + " != null");
                Transaction.Rollback(name);
                Transaction.Dispose();
                Transaction = null;
            }
        }

        /// <summary>
        /// Commits the current transaction if one exists.
        /// </summary>
        /// <param name="name">Optional name to match when committing.</param>
        public void CommitTransaction(string name = "")
        {
            if (Transaction != null && (string.IsNullOrEmpty(name) || TransactionName == name))
            {
                Transaction.Commit();
                Transaction.Dispose();
                Transaction = null;
            }
        }

        /// <summary>
        /// Gets the where clause from expression.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="expression">The expression.</param>
        /// <returns>System.String.</returns>
        public string GetWhereClauseFromExpression<T>(Expression<Func<T, bool>> expression) where T : class, new()
        {
            var elements = GetWhereClauseElements(expression);
            return $"\r\nWHERE (\\t{elements.WhereClause})";
        }

        /// <summary>
        /// Gets the where clause elements.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="expression">The expression.</param>
        /// <returns>WhereClauseGenerator.WhereClauseElements&lt;T&gt;.</returns>
        public WhereClauseElements<T> GetWhereClauseElements<T>(Expression<Func<T, bool>> expression) where T : class, new()
        {
            var generator = new WhereClauseGenerator();
            // TODO: Make sure caches are populated for T before this:
            var x = generator.GenerateWhereClause<T>(expression);
            return x;
        }

        private SqlConnection? GetConnection()
        {
            if (Connection == null || Connection.State != ConnectionState.Open)
            {
                Connection = new SqlConnection(_connectionString);
                Connection.Open();
            }
            return Connection;
        }

        /// <summary>
        /// Creates the select command for entities of type T, optionally with a WHERE
        /// clause for the primary key.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key">The key.</param>
        /// <returns>System.String.</returns>
        public string CreateSelectCommand<T>(dynamic? key = null) where T : class, new()
        {
            var tableName = GetTableName<T>();
            var selectClause = GetSelectClause<T>();
            var whereClause = key != null ? GetWhereClause<T>(key) : string.Empty;
        

            return $"SELECT {selectClause} FROM {tableName}{whereClause}";
        }

        /// <summary>
        /// Creates a select query with a WHERE clause based on <paramref name="whereExpression"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="whereExpression">The where expression.</param>
        /// <returns>WhereClauseGenerator.WhereClauseElements&lt;T&gt;.</returns>
        public WhereClauseElements<T> CreateSelectQuery<T>(Expression<Func<T, bool>> whereExpression) where T : class, new()
        {
            var selectClause = CreateSelectCommand<T>();
            var elements = GetWhereClauseElements(whereExpression!);
            elements.SelectClause = selectClause;
            return elements;
        }

        /// <summary>
        /// Clears the mappings.
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
        public void Dispose()
        {
            Connection?.Dispose();
            Transaction?.Dispose();
        }
        #endregion


        #region  Protected helper methods

        /// <summary>
        /// Gets the default value for the given type.
        /// </summary>
        /// <param name="t">The type to get the default value for.</param>
        /// <returns>An object representing the default value for the type.</returns>
        protected object GetDefault(Type t)
        {
            if (t.IsValueType)
            {
                return Activator.CreateInstance(t);
            }
            return null;
        }

        protected string GetSelectClause<T>()
        {
            return string.Join(", ", typeof(T).GetProperties()
                .Where(p => !p.GetCustomAttributes<NotMappedAttribute>().Any())
                .Select(p => _columnNames.GetOrAdd(p, GetColumnName)));
        }

        protected string GetTableName<T>()
        {
            return _tableNames.GetOrAdd(typeof(T), t =>
                t.GetCustomAttribute<TableAttribute>()?.Name ?? t.Name.ToLower());
        }

        protected string GetWhereClause<T>(dynamic key)
        {
            var primaryKey = _primaryKeys.GetOrAdd(typeof(T), t => GetPrimaryKey<T>());
            if (primaryKey == null)
                throw new KeyNotFoundException("No primary key found for the type.");

            var columnName = _columnNames.GetOrAdd(primaryKey, GetColumnName);
            return $" WHERE {columnName} = {key}";
        }

        protected Dictionary<string, int> GetColumnOrdinals(Type type, SqlDataReader reader)
        {
            var ordinals = new Dictionary<string, int>();
            foreach (var property in _propertiesCache.GetOrAdd(type, t => t.GetProperties()))
            {
                string columnName = _columnNames.GetOrAdd(property, GetColumnName);

                try
                {
                    if (reader.GetOrdinal(columnName) >= 0)
                    {
                        ordinals[columnName] = reader.GetOrdinal(columnName);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    Debug.WriteLine(e);
                    continue;
                }
            }
            return ordinals;
        }

        protected string GetColumnName(PropertyInfo property)
        {
            return property.GetCustomAttribute<ColumnAttribute>()?.Name ??
                   property.Name.ToLower();
        }

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

        internal static PropertyInfo[] GetUnmappedProperties<T>() where T : class, new()
        {
            var ret = typeof(T).GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null);
            return ret.ToArray();
        }

        #endregion

    }
}