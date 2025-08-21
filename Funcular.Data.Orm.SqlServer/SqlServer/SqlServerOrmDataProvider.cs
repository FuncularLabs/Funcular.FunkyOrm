using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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

        #endregion

        #region Properties

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

        public SqlServerOrmDataProvider(string connectionString, SqlConnection? connection = null, SqlTransaction? transaction = null)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            Connection = connection ?? new SqlConnection(_connectionString);
            Transaction = transaction;
        }

        #endregion

        #region Public Methods - CRUD Operations

        public T? Get<T>(dynamic? key = null) where T : class, new()
        {
            using var connectionScope = new ConnectionScope(this);
            var commandText = CreateGetOneOrSelectCommand<T>(key);

            using var command = BuildCommand(commandText, connectionScope.Connection);
            return ExecuteReaderSingle<T>(command);
        }

        public ICollection<T> Query<T>(Expression<Func<T, bool>> expression) where T : class, new()
        {
            using var connectionScope = new ConnectionScope(this);
            var elements = CreateSelectQuery(expression);

            var commandText = elements.SelectClause;
            if (!string.IsNullOrEmpty(elements.WhereClause))
                commandText += "\r\nWHERE " + elements.WhereClause;
            if (!string.IsNullOrEmpty(elements.OrderByClause))
                commandText += "\r\n" + elements.OrderByClause;

            using var command = BuildCommand(commandText, connectionScope.Connection, elements.SqlParameters);
            return ExecuteReaderList<T>(command);
        }

        public ICollection<T> GetList<T>() where T : class, new()
        {
            using var connectionScope = new ConnectionScope(this);
            using var command = BuildCommand(CreateGetOneOrSelectCommand<T>(), connectionScope.Connection);
            return ExecuteReaderList<T>(command);
        }

        public long Insert<T>(T entity) where T : class, new()
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            var primaryKey = GetPrimaryKeyCached<T>();
            ValidateInsertPrimaryKey(entity, primaryKey);

            using var connectionScope = new ConnectionScope(this);
            var insertCommand = BuildInsertCommand(entity, primaryKey);
            using var command = BuildCommand(insertCommand.CommandText, connectionScope.Connection, insertCommand.Parameters);
            var insertedId = ExecuteInsert(command, entity, primaryKey);
            return insertedId;
        }

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
                using var command = BuildCommand(updateCommand.CommandText, connectionScope.Connection, updateCommand.Parameters);
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
            string? selectCommand = CreateGetOneOrSelectCommand<T>();
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

        public static void ClearMappings()
        {
            _tableNames.Clear();
            _columnNames.Clear();
            _primaryKeys.Clear();
            _propertiesCache.Clear();
            _unmappedPropertiesCache.Clear();
            _columnOrdinalsCache.Clear();
        }

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

        protected internal string? CreateGetOneOrSelectCommand<T>(dynamic? key = null) where T : class, new()
        {
            var tableName = GetTableName<T>();
            string columnNames = GetColumnNames<T>();
            var whereClause = key != null ? GetWhereClause<T>(key) : string.Empty;
            return $"SELECT {columnNames} FROM {tableName}{whereClause}";
        }

        protected internal SqlQueryComponents<T> CreateSelectQuery<T>(Expression<Func<T, bool>> whereExpression) where T : class, new()
        {
            var selectClause = CreateGetOneOrSelectCommand<T>();
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
                _columnNames,
                _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>),
                paramGen,
                trans);
            visitor.Visit(expression);
            if (commandElements is not null)
            {
                commandElements.WhereClause = visitor.WhereClauseBody;
                commandElements.OriginalExpression ??= expression;
                if (visitor.Parameters != null)
                {
                    commandElements.SqlParameters = commandElements.SqlParameters ?? new List<SqlParameter>();
                    commandElements.SqlParameters.AddRange(visitor.Parameters);
                }
            }
            else
            {
                commandElements = new SqlQueryComponents<T>(expression, string.Empty, visitor.WhereClauseBody, string.Empty, visitor.Parameters);
            }

            return commandElements;
        }

        protected internal SqlQueryComponents<T> GenerateOrderByClause<T>(Expression<Func<T, bool>> expression, SqlQueryComponents<T>? commandElements = null) where T : class, new()
        {
            var visitor = new OrderByClauseVisitor<T>(
                _columnNames,
                _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>));
            visitor.Visit(expression);
            if (commandElements == null)
            {
                commandElements = new SqlQueryComponents<T>(expression, string.Empty, string.Empty, visitor.OrderByClause, new List<SqlParameter>());
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

        protected internal SqlCommand BuildCommand(string? commandText, SqlConnection connection, IEnumerable<SqlParameter>? parameters = null)
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

        protected T MapEntity<T>(SqlDataReader reader) where T : class, new()
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

        protected internal (string? CommandText, List<SqlParameter> Parameters) BuildInsertCommand<T>(T entity, PropertyInfo primaryKey) where T : class, new()
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

        protected internal int ExecuteInsert<T>(SqlCommand command, T entity, PropertyInfo primaryKey) where T : class, new()
        {
            InvokeLogAction(command);
            var result = (int)command.ExecuteScalar();
            if (result != 0)
                primaryKey.SetValue(entity, result);
            return result;
        }

        protected internal (string CommandText, List<SqlParameter> Parameters) BuildUpdateCommand<T>(T entity, T existing, PropertyInfo primaryKey) where T : class, new()
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

            setClause.Length -= 2;
            var pkColumn = GetColumnNameCached(primaryKey);
            parameters.Add(CreateParameter<object>($"@{pkColumn}", primaryKey.GetValue(entity), primaryKey.PropertyType));

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
                throw new InvalidOperationException($"Primary key must be default value for insert: {primaryKey.GetValue(entity)}");
        }

        protected internal void ValidateUpdatePrimaryKey<T>(T entity, PropertyInfo primaryKey)
        {
            var value = primaryKey.GetValue(entity);
            if (value == null || Equals(value, GetDefault(primaryKey.PropertyType)))
                throw new InvalidOperationException("Primary key must be set for update.");
        }

        protected internal SqlParameter CreateParameter<T>(string name, object? value, Type propertyType) where T : class, new()
        {
            var sqlType = ParameterGenerator.GetSqlDbType(value);
            return new SqlParameter(name, value ?? DBNull.Value)
            {
                SqlDbType = sqlType,
                IsNullable = Nullable.GetUnderlyingType(propertyType) != null || propertyType.IsClass
            };
        }

        protected internal void InvokeLogAction(SqlCommand command)
        {
            Log?.Invoke(command.CommandText);
            foreach (var param in command.Parameters.AsEnumerable())
                Log?.Invoke($"{param.ParameterName}: {param.Value}");
        }

        protected internal PropertyInfo GetPrimaryKeyCached<T>() where T : class
        {
            return _primaryKeys.GetOrAdd(typeof(T), t => GetPrimaryKey<T>() ??
                throw new InvalidOperationException($"No primary key found for {typeof(T).FullName}"));
        }

        protected internal string GetColumnNameCached(PropertyInfo property)
        {
            return _columnNames.GetOrAdd(property.ToDictionaryKey(), p => GetColumnName(property));
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
        protected internal string GetColumnNames<T>() => string.Join(", ", typeof(T).GetProperties()
            .Where(p => !p.GetCustomAttributes<NotMappedAttribute>().Any())
            .Select(p => GetColumnNameCached(p)));
        protected internal string GetTableName<T>() => _tableNames.GetOrAdd(typeof(T), t =>
            t.GetCustomAttribute<TableAttribute>()?.Name ?? t.Name.ToLower());
        protected string GetWhereClause<T>(dynamic key) where T : class
        {
            var pk = GetPrimaryKeyCached<T>();
            return $" WHERE {GetColumnNameCached(pk)} = {key}";
        }
        protected internal Dictionary<string, int> GetColumnOrdinals(Type type, SqlDataReader reader)
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
        protected internal string GetColumnName(PropertyInfo property) =>
            property.GetCustomAttribute<ColumnAttribute>()?.Name ?? property.Name.ToLower();
        internal PropertyInfo? GetPrimaryKey<T>() => typeof(T).GetProperties().FirstOrDefault(p =>
            p.GetCustomAttribute<KeyAttribute>() != null ||
            p.GetCustomAttribute<DatabaseGeneratedAttribute>()?.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity ||
            p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals(typeof(T).Name + "Id", StringComparison.OrdinalIgnoreCase));
        protected internal static ImmutableArray<PropertyInfo> GetUnmappedProperties<T>(Type type) where T : class, new() =>
            typeof(T).GetProperties().Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null).ToImmutableArray();

        private IQueryable<T> CreateQueryable<T>(string? selectCommand = null) where T : class, new()
        {
            var provider = new SqlLinqQueryProvider<T>(this, selectCommand);
            return new SqlQueryable<T>(provider);
        }

        #endregion
    }

    /*
    internal class IgnoreUnderscoreAndCaseStringComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return x.Replace("_", "").Equals(y.Replace("_", ""), StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(string obj)
        {
            return obj.Replace("_", "").ToLower().GetHashCode();
        }
    }*/
}