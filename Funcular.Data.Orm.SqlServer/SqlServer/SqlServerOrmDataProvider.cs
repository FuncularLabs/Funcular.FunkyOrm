using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Funcular.Data.Orm.Visitors;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer
{
    using UpdateCommand = (string CommandText, List<SqlParameter> Parameters);

    public partial class SqlServerOrmDataProvider : ISqlDataProvider, IDisposable
    {
        #region Fields

        private readonly string _connectionString;
        private SqlTransaction? _transaction;
        
        internal static readonly ConcurrentDictionary<Type, string> _tableNames = new();
        internal static readonly ConcurrentDictionary<string, string> _columnNames = new(new IgnoreUnderscoreAndCaseStringComparer());
        internal static readonly ConcurrentDictionary<Type, PropertyInfo> _primaryKeys = new();
        internal static readonly ConcurrentDictionary<Type, ImmutableArray<PropertyInfo>> _propertiesCache = new();
        internal static readonly ConcurrentDictionary<Type, ImmutableArray<PropertyInfo>> _unmappedPropertiesCache = new();
        internal static readonly ConcurrentDictionary<Type, Dictionary<string, int>> _columnOrdinalsCache = new();
        internal static readonly HashSet<Type> _mappedTypes = new();

        #endregion

        #region Properties

        internal static ConcurrentDictionary<string, string> ColumnNames
        {
            get { return _columnNames; }
        }

        public Action<string>? Log { get; set; }
        public SqlConnection? Connection { get; set; }

        public SqlTransaction? Transaction
        {
            get => _transaction;
            set => _transaction = value;
        }

        public string? TransactionName { get; protected set; }

        #endregion

        #region Constructors

        public SqlServerOrmDataProvider(string connectionString, SqlConnection? connection = null,
            SqlTransaction? transaction = null)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            Connection = connection ?? new SqlConnection(_connectionString);
            Transaction = transaction;
        }

        #endregion

        #region Public Methods - CRUD Operations

        public T? Get<T>(dynamic? key = null) where T : class, new()
        {
            DiscoverColumns<T>();
            using var connectionScope = new ConnectionScope(this);
            var commandText = CreateGetOneOrSelectCommandText<T>(key);

            using var command = BuildSqlCommandObject(commandText, connectionScope.Connection);
            return ExecuteReaderSingle<T>(command);
        }

        public ICollection<T> Query<T>(Expression<Func<T, bool>> expression) where T : class, new()
        {
            DiscoverColumns<T>();
            using var connectionScope = new ConnectionScope(this);
            var elements = CreateSelectQueryObject(expression);

            var commandText = elements.SelectClause;
            if (!string.IsNullOrEmpty(elements.WhereClause))
                commandText += "\r\nWHERE " + elements.WhereClause;
            if (!string.IsNullOrEmpty(elements.OrderByClause))
                commandText += "\r\n" + elements.OrderByClause;

            using var command = BuildSqlCommandObject(commandText, connectionScope.Connection, elements.SqlParameters);
            return ExecuteReaderList<T>(command);
        }

        public ICollection<T> GetList<T>() where T : class, new()
        {
            DiscoverColumns<T>();
            using var connectionScope = new ConnectionScope(this);
            using var command = BuildSqlCommandObject(CreateGetOneOrSelectCommandText<T>(), connectionScope.Connection);
            return ExecuteReaderList<T>(command);
        }

        public long Insert<T>(T entity) where T : class, new()
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            DiscoverColumns<T>();
            var primaryKey = GetCachedPrimaryKey<T>();
            ValidateInsertPrimaryKey(entity, primaryKey);

            using var connectionScope = new ConnectionScope(this);
            var insertCommand = BuildInsertCommandObject(entity, primaryKey);
            using var command = BuildSqlCommandObject(insertCommand.CommandText, connectionScope.Connection,
                insertCommand.Parameters);
            var insertedId = ExecuteInsert(command, entity, primaryKey);
            return insertedId;
        }

        public T Update<T>(T entity) where T : class, new()
        {
            DiscoverColumns<T>();
            var primaryKey = GetCachedPrimaryKey<T>();
            ValidateUpdatePrimaryKey(entity, primaryKey);

            using var connectionScope = new ConnectionScope(this);
            var existingEntity = Get<T>((dynamic)primaryKey.GetValue(entity));
            if (existingEntity == null)
                throw new InvalidOperationException("Entity does not exist in database.");

            UpdateCommand updateCommand = BuildUpdateCommand(entity, existingEntity, primaryKey);
            if (updateCommand.Parameters.Any())
            {
                using var command = BuildSqlCommandObject(updateCommand.CommandText, connectionScope.Connection,
                    updateCommand.Parameters);
                ExecuteUpdate(command);
            }
            else
            {
                Log?.Invoke($"No update needed for {typeof(T).Name} with id {primaryKey.GetValue(entity)}.");
            }

            return entity;
        }

        public IQueryable<T> Query<T>() where T : class, new()
        {
            string? selectCommand = CreateGetOneOrSelectCommandText<T>();
            return CreateQueryable<T>(selectCommand);
        }

        #endregion

        #region Public Methods - Transaction Management

        public void BeginTransaction(string? name = null)
        {
            EnsureConnectionOpen();
            if (Transaction != null)
                throw new InvalidOperationException("Transaction already in progress.");

            Transaction = Connection?.BeginTransaction(IsolationLevel.ReadCommitted);
            TransactionName = name ?? string.Empty;
        }

        public void CommitTransaction(string? name = null)
        {
            if (Transaction != null && (string.IsNullOrEmpty(name) || TransactionName == name))
            {
                Transaction.Commit();
                CleanupTransaction();
            }
        }

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

        /*public static void ClearMappings()
        {
            _tableNames.Clear();
            ColumnNames.Clear();
            _primaryKeys.Clear();
            _propertiesCache.Clear();
            _unmappedPropertiesCache.Clear();
            _columnOrdinalsCache.Clear();
        }*/

        public void Dispose()
        {
            Connection?.Dispose();
            Transaction?.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected Methods - Connection Management

        protected SqlConnection GetConnection()
        {
            EnsureConnectionOpen();
            return Connection!;
        }

        protected void EnsureConnectionOpen()
        {
            if (Connection == null || Connection.State != ConnectionState.Open)
            {
                Connection = new SqlConnection(_connectionString);
                Connection.Open();
            }
        }

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

        protected internal string? CreateGetOneOrSelectCommandText<T>(dynamic? key = null) where T : class, new()
        {
            var tableName = GetTableName<T>();
            string columnNames = GetColumnNames<T>();
            var whereClause = key != null ? GetWhereClause<T>(key) : string.Empty;
            return $"SELECT {columnNames} FROM {tableName}{whereClause}";
        }

        protected internal SqlQueryComponents<T> CreateSelectQueryObject<T>(Expression<Func<T, bool>> whereExpression)
            where T : class, new()
        {
            var selectClause = CreateGetOneOrSelectCommandText<T>();
            var elements = GenerateWhereClause(whereExpression);
            elements.SelectClause = selectClause;
            elements.OriginalExpression = whereExpression;
            elements.WhereClause = elements.WhereClause;
            elements.OrderByClause = GenerateOrderByClause(whereExpression).OrderByClause;
            return elements;
        }

        protected internal SqlQueryComponents<T> GenerateWhereClause<T>(
            Expression<Func<T, bool>> expression,
            SqlQueryComponents<T>? commandElements = null,
            ParameterGenerator? parameterGenerator = null,
            SqlExpressionTranslator? translator = null) where T : class, new()
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
            if (commandElements is not null)
            {
                commandElements.WhereClause = visitor.WhereClauseBody;
                commandElements.OriginalExpression ??= expression;
                if (visitor.Parameters.Any())
                {
                    commandElements.SqlParameters = commandElements.SqlParameters ?? new List<SqlParameter>();
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

        protected internal SqlQueryComponents<T> GenerateOrderByClause<T>(Expression<Func<T, bool>> expression,
            SqlQueryComponents<T>? commandElements = null) where T : class, new()
        {
            var visitor = new OrderByClauseVisitor<T>(
                ColumnNames,
                _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>));
            visitor.Visit(expression);
            if (commandElements == null)
            {
                commandElements = new SqlQueryComponents<T>(expression, string.Empty, string.Empty,
                    visitor.OrderByClause, new List<SqlParameter>());
            }
            else
            {
                commandElements.OrderByClause = visitor.OrderByClause;
                commandElements.OriginalExpression ??= expression;
            }

            return commandElements;
        }

        #endregion

        #region Private Methods - Execution Helpers

        protected internal T? ExecuteReaderSingle<T>(SqlCommand command) where T : class, new()
        {
            InvokeLogAction(command);
            using var reader = command.ExecuteReader();
            return reader.Read() ? MapEntity<T>(reader) : null;
        }

        protected internal ICollection<T> ExecuteReaderList<T>(SqlCommand command) where T : class, new()
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

        protected internal SqlCommand BuildSqlCommandObject(string? commandText, SqlConnection connection,
            ICollection<SqlParameter>? parameters = null)
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
        /// Discovers the columns for the specified type T by querying the database schema and
        /// caching the results. This method is called before any CRUD operation to ensure columns
        /// are mapped prior to executing any commands.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        protected void DiscoverColumns<T>()
        {
            if (_mappedTypes.Contains(typeof(T))) return;

            var table = GetTableName<T>();
            var commandText = $"SELECT * FROM {table}";
            var properties = _propertiesCache.GetOrAdd(typeof(T), t => [..t.GetProperties()]);
                
            using var connectionScope = new ConnectionScope(this);
            using var command = BuildSqlCommandObject(commandText, connectionScope.Connection, Array.Empty<SqlParameter>());
            using var tempReader = command.ExecuteReader(CommandBehavior.SchemaOnly);
            var schema = tempReader.GetColumnSchema();
            var comparer = new IgnoreUnderscoreAndCaseStringComparer();
            foreach (var property in properties)
            {
                if (property.GetCustomAttribute<NotMappedAttribute>() != null) continue;
                    
                var columnAttr = property.GetCustomAttribute<ColumnAttribute>();
                string? actualColumnName = columnAttr?.Name ?? schema.FirstOrDefault(c => comparer.Equals(c.ColumnName, property.Name))?.ColumnName;
                if (actualColumnName != null)
                {
                    var key = property.ToDictionaryKey();
                    _columnNames[key] = actualColumnName;
                }
            }
            _mappedTypes.Add(typeof(T));
        }


        protected T MapEntity<T>(SqlDataReader reader) where T : class, new()
        {
            var properties = _propertiesCache.GetOrAdd(typeof(T), t => [..t.GetProperties()]);
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
        /// Builds a SQL insert command text and parameters for the specified entity (T) instance
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        /// <param name="primaryKey"></param>
        /// <returns></returns>
        protected internal (string? CommandText, List<SqlParameter> Parameters) BuildInsertCommandObject<T>(T entity,
            PropertyInfo primaryKey) where T : class, new()
        {
            var tableName = GetTableName<T>();
            // Properties that are not marked with [NotMapped] and are not the primary key
            var includedProperties = _propertiesCache.GetOrAdd(typeof(T), t => [..t.GetProperties()])
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

        protected internal int ExecuteInsert<T>(SqlCommand command, T entity, PropertyInfo primaryKey)
            where T : class, new()
        {
            InvokeLogAction(command);
            var result = (int)command.ExecuteScalar();
            if (result != 0)
                primaryKey.SetValue(entity, result);
            return result;
        }

        protected internal (string CommandText, List<SqlParameter> Parameters) BuildUpdateCommand<T>(T entity,
            T existing, PropertyInfo primaryKey) where T : class, new()
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
                    var columnName = GetCachedColumnName(property);
                    setClause.Append($"{columnName} = @{columnName}, ");
                    parameters.Add(CreateParameter<object>($"@{columnName}", newValue, property.PropertyType));
                }
            }

            if (setClause.Length == 0) return (string.Empty, parameters);

            setClause.Length -= 2;
            var pkColumn = GetCachedColumnName(primaryKey);
            parameters.Add(
                CreateParameter<object>($"@{pkColumn}", primaryKey.GetValue(entity), primaryKey.PropertyType));

            return ($"UPDATE {tableName} SET {setClause} WHERE {pkColumn} = @{pkColumn}", parameters);
        }

        protected internal void ExecuteUpdate(SqlCommand command)
        {
            InvokeLogAction(command);
            var rowsAffected = command.ExecuteNonQuery();
            if (rowsAffected == 0)
                throw new InvalidOperationException("Update failed: No rows affected.");
        }

        #endregion

        #region Protected Internal Methods - Validation and Helpers

        protected internal void ValidateInsertPrimaryKey<T>(T entity, PropertyInfo primaryKey)
        {
            var defaultValue = GetDefault(primaryKey.PropertyType);
            if (!Equals(primaryKey.GetValue(entity), defaultValue))
                throw new InvalidOperationException(
                    $"Primary key must be default value for insert: {primaryKey.GetValue(entity)}");
        }

        protected internal void ValidateUpdatePrimaryKey<T>(T entity, PropertyInfo primaryKey)
        {
            var value = primaryKey.GetValue(entity);
            if (value == null || Equals(value, GetDefault(primaryKey.PropertyType)))
                throw new InvalidOperationException("Primary key must be set for update.");
        }

        protected internal SqlParameter CreateParameter<T>(string name, object? value, Type propertyType)
            where T : class, new()
        {
            var sqlType = ParameterGenerator.GetSqlDbType(value);
            return new SqlParameter(name, value ?? DBNull.Value)
            {
                SqlDbType = sqlType,
                IsNullable = Nullable.GetUnderlyingType(propertyType) != null || propertyType.IsClass
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        protected internal void InvokeLogAction(SqlCommand command)
        {
            Log?.Invoke(command.CommandText);
            foreach (var param in command.Parameters.AsEnumerable())
                Log?.Invoke($"{param.ParameterName}: {param.Value}");
        }

        protected internal PropertyInfo GetCachedPrimaryKey<T>() where T : class
        {
            return _primaryKeys.GetOrAdd(typeof(T), t => GetPrimaryKeyProperty<T>() ??
                                                         throw new InvalidOperationException(
                                                             $"No primary key found for {typeof(T).FullName}"));
        }

        /// <summary>
        /// Gets or adds the cached column name for the specified property. The dictionary key is
        /// the type name of the declaring type, a dot, and the property name.
        /// </summary>
        /// <param name="property"></param>
        /// <returns></returns>
        protected internal string GetCachedColumnName(PropertyInfo property)
        {
            return ColumnNames.GetOrAdd(property.ToDictionaryKey(), p => ComputeColumnName(property));
        }

        protected internal void CleanupTransaction()
        {
            Transaction?.Dispose();
            Transaction = null;
            TransactionName = null;
            CloseConnectionIfNoTransaction();
        }

        #endregion

        #region Nested Types

        internal class ConnectionScope : IDisposable
        {
            protected internal readonly SqlServerOrmDataProvider _provider;
            protected internal readonly bool _createdConnection;

            public SqlConnection Connection => _provider.GetConnection();

            public ConnectionScope(SqlServerOrmDataProvider provider)
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

        #region Original Protected Helpers

        protected internal object? GetDefault(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;

        /// <summary>
        /// Returns a comma separated list of column names for the specified type T.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        protected internal string GetColumnNames<T>() => string.Join(", ", typeof(T).GetProperties()
            .Where(p => !p.GetCustomAttributes<NotMappedAttribute>().Any())
            .Select(p => GetCachedColumnName(p)));

        protected internal string GetTableName<T>() => _tableNames.GetOrAdd(typeof(T), t =>
            t.GetCustomAttribute<TableAttribute>()?.Name ?? t.Name.ToLower());

        protected string GetWhereClause<T>(dynamic key) where T : class
        {
            var pk = GetCachedPrimaryKey<T>();
            return $" WHERE {GetCachedColumnName(pk)} = {key}";
        }

        protected internal Dictionary<string, int> GetColumnOrdinals(Type type, SqlDataReader reader)
        {
            var ordinals = new Dictionary<string, int>(new IgnoreUnderscoreAndCaseStringComparer());
            var schema = reader.GetColumnSchema();
            var comparer = new IgnoreUnderscoreAndCaseStringComparer();
            foreach (var property in _propertiesCache.GetOrAdd(type, t => [..t.GetProperties()]))
            {
                if (property.GetCustomAttribute<NotMappedAttribute>() != null) continue;

                var columnAttr = property.GetCustomAttribute<ColumnAttribute>();
                var actualColumnName = columnAttr?.Name;

                if (actualColumnName == null)
                {
                    // Find matching schema column using comparer semantics
                    actualColumnName = schema.FirstOrDefault(c => comparer.Equals(c.ColumnName, property.Name))?.ColumnName;
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
        /// Computes the column name for the specified property, which is the Name of the
        /// Column attribute if present, or otherwise the property's name.
        /// </summary>
        /// <param name="property"></param>
        /// <returns></returns>
        protected internal string ComputeColumnName(PropertyInfo property) =>
            property.GetCustomAttribute<NotMappedAttribute>() != null
                ? string.Empty
                : property.GetCustomAttribute<ColumnAttribute>()?.Name ??
                  (_columnNames.TryGetValue(property.Name.ToLowerInvariant(), out var columnName)
                      ? columnName
                      : property.Name.ToLowerInvariant());
        internal PropertyInfo? GetPrimaryKeyProperty<T>() => typeof(T).GetProperties().FirstOrDefault(p =>
            p.GetCustomAttribute<KeyAttribute>() != null ||
            p.GetCustomAttribute<DatabaseGeneratedAttribute>()?.DatabaseGeneratedOption ==
            DatabaseGeneratedOption.Identity ||
            p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals(typeof(T).Name + "Id", StringComparison.OrdinalIgnoreCase));

        protected internal static ImmutableArray<PropertyInfo> GetUnmappedProperties<T>(Type type)
            where T : class, new() =>
            [..typeof(T).GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null)];

        private IQueryable<T> CreateQueryable<T>(string? selectCommand = null) where T : class, new()
        {
            var provider = new SqlLinqQueryProvider<T>(this, selectCommand);
            return new SqlQueryable<T>(provider);
        }

        #endregion
    }
}