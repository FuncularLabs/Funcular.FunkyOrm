using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
#if NETSTANDARD2_0
using System.Data.Common;
#endif
using System.Threading.Tasks;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Funcular.Data.Orm;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.Interfaces;
using Funcular.Data.Orm.PostgreSql.Visitors;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql
{
    /// <summary>
    /// A PostgreSQL specific implementation of an ORM data provider.
    /// </summary>
    public partial class PostgreSqlOrmDataProvider : OrmDataProvider, ISqlOrmProvider
    {
        #region Static Initializer

        static PostgreSqlOrmDataProvider()
        {
            // Must be set before any NpgsqlConnection is opened.
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }

        #endregion

        #region Fields

        private readonly string _connectionString;
        private IDbTransaction _transaction;
        internal static readonly ConcurrentDictionary<string, Dictionary<string, int>> _columnOrdinalsCache = new ConcurrentDictionary<string, Dictionary<string, int>>();
        internal static readonly ConcurrentDictionary<string, Delegate> _entityMappers = new ConcurrentDictionary<string, Delegate>();

        #endregion

        #region Properties

        internal static ConcurrentDictionary<string, string> ColumnNamesCache => _columnNames;
        internal static ConcurrentDictionary<Type, ICollection<PropertyInfo>> UnmappedPropertiesCache => _unmappedPropertiesCache;

        public IDbConnection Connection { get; set; }

        public IDbTransaction Transaction
        {
            get => _transaction;
            set => _transaction = value;
        }

        public string TransactionName { get; protected set; }
        public ISqlDialect Dialect { get; }

        #endregion

        #region Constructors

        public PostgreSqlOrmDataProvider(string connectionString, IDbConnection connection = null,
            IDbTransaction transaction = null, ISqlDialect dialect = null)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            Connection = connection;
            Transaction = transaction;
            Dialect = dialect ?? new PostgreSqlDialect();
        }

        #endregion

        #region Async Methods

        public override async Task<T> GetAsync<T>(dynamic key = null)
        {
            DiscoverColumns<T>();
            using (var connectionScope = new ConnectionScope(this))
            {
                var commandText = CreateGetOneOrSelectCommandText<T>(key);
                using (var command = BuildSqlCommandObject(commandText, connectionScope.Connection))
                    return await ExecuteReaderSingleAsync<T>(command).ConfigureAwait(false);
            }
        }

        public override async Task<ICollection<T>> QueryAsync<T>(Expression<Func<T, bool>> expression)
        {
            DiscoverColumns<T>();
            using (var connectionScope = new ConnectionScope(this))
            {
                var elements = CreateSelectQueryObject(expression);
                var commandText = elements.SelectClause;
                if (!string.IsNullOrEmpty(elements.WhereClause)) commandText += $"\r\nWHERE {elements.WhereClause}";
                if (!string.IsNullOrEmpty(elements.OrderByClause)) commandText += $"\r\n{elements.OrderByClause}";
                using (var command = BuildSqlCommandObject(commandText, connectionScope.Connection, elements.SqlParameters))
                    return await ExecuteReaderListAsync<T>(command).ConfigureAwait(false);
            }
        }

        public override async Task<ICollection<T>> GetListAsync<T>()
        {
            DiscoverColumns<T>();
            using (var connectionScope = new ConnectionScope(this))
            {
                using (var command = BuildSqlCommandObject(CreateGetOneOrSelectCommandText<T>(), connectionScope.Connection))
                    return await ExecuteReaderListAsync<T>(command).ConfigureAwait(false);
            }
        }

        public override async Task<object> InsertAsync<T>(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            DiscoverColumns<T>();
            var primaryKey = GetCachedPrimaryKey<T>();
            ValidateInsertPrimaryKey(entity, primaryKey);
            using (var connectionScope = new ConnectionScope(this))
            {
                var insertCommand = BuildInsertCommandObject(entity, primaryKey);
                using (var command = BuildSqlCommandObject(insertCommand.CommandText, connectionScope.Connection, insertCommand.Parameters))
                    return await ExecuteInsertAsync(command, entity, primaryKey).ConfigureAwait(false);
            }
        }

        public override async Task<TKey> InsertAsync<T, TKey>(T entity)
        {
            var result = await InsertAsync(entity).ConfigureAwait(false);
            return (TKey)result;
        }

        public override async Task<T> UpdateAsync<T>(T entity)
        {
            DiscoverColumns<T>();
            var primaryKey = GetCachedPrimaryKey<T>();
            ValidateUpdatePrimaryKey(entity, primaryKey);
            using (var connectionScope = new ConnectionScope(this))
            {
                var existingEntity = await GetAsync<T>((dynamic)primaryKey.GetValue(entity)).ConfigureAwait(false);
                if (existingEntity == null) throw new InvalidOperationException("Entity does not exist in database.");
                CommandParameters commandParameters = BuildUpdateCommand(entity, existingEntity, primaryKey);
                if (commandParameters.Parameters.Any())
                {
                    using (var command = BuildSqlCommandObject(commandParameters.CommandText, connectionScope.Connection, commandParameters.Parameters))
                        await ExecuteUpdateAsync(command).ConfigureAwait(false);
                }
                else { Log?.Invoke($"No update needed for {typeof(T).Name} with id {primaryKey.GetValue(entity)}."); }
                return entity;
            }
        }

        public override async Task<int> DeleteAsync<T>(Expression<Func<T, bool>> predicate)
        {
            if (Transaction == null) throw new InvalidOperationException("Delete operations must be performed within an active transaction.");
            if (predicate == null) throw new InvalidOperationException("A WHERE clause (predicate) is required for deletes.");
            var components = GenerateWhereClause(predicate);
            ValidateWhereClause<T>(components.WhereClause);
            var tableName = GetTableName<T>();
            var commandText = Dialect.BuildDeleteCommand(tableName, $" WHERE {components.WhereClause}");
            using (var command = BuildSqlCommandObject(commandText, Connection, components.SqlParameters))
            {
                InvokeLogAction(command);
                var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                if (affected == 0) Log?.Invoke($"Delete affected zero rows for {typeof(T).Name}.");
                return affected;
            }
        }

        public async Task<bool> DeleteAsync<T>(long id) where T : class, new()
        {
            if (Transaction == null) throw new InvalidOperationException("Delete operations must be performed within an active transaction.");
            var pk = GetCachedPrimaryKey<T>();
            var tableName = GetTableName<T>();
            var pkColumn = GetCachedColumnName(pk);
            var commandText = Dialect.BuildDeleteCommand(tableName, $" WHERE {pkColumn} = @id");
            var param = new NpgsqlParameter("@id", id);
            using (var command = BuildSqlCommandObject(commandText, Connection, new[] { param }))
            {
                InvokeLogAction(command);
                var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                if (affected == 0) Log?.Invoke($"Delete affected zero rows for {typeof(T).Name} with id {id}.");
                return affected > 0;
            }
        }

        #endregion

        #region Async Execution Helpers

        private void HandlePostgresException<T>(Npgsql.PostgresException ex)
        {
            if (ex.SqlState == "42P01") // undefined_table
            {
                var typeName = typeof(T).Name;
                var snakeCase = IgnoreUnderscoreAndCaseStringComparer.ToLowerSnakeCase(typeName);
                var message = $"The table or view for entity '{typeName}' was not found in the database. " +
                              $"Ensure that the table exists and is named correctly. " +
                              $"Expected table names: '{typeName}' or '{snakeCase}'. " +
                              $"If the table has a different name, use the [Table(\"TableName\")] attribute on the entity class.";
                throw new InvalidOperationException(message, ex);
            }
            throw ex;
        }

        protected internal async Task<T> ExecuteReaderSingleAsync<T>(NpgsqlCommand command) where T : class, new()
        {
            InvokeLogAction(command);
            try
            {
                using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
                    return await reader.ReadAsync().ConfigureAwait(false) ? MapEntity<T>(reader) : null;
            }
            catch (Npgsql.PostgresException ex) { HandlePostgresException<T>(ex); throw; }
        }

        protected internal async Task<ICollection<T>> ExecuteReaderListAsync<T>(NpgsqlCommand command) where T : class, new()
        {
            InvokeLogAction(command);
            var results = new List<T>();
            try
            {
                using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
                    while (await reader.ReadAsync().ConfigureAwait(false))
                        results.Add(MapEntity<T>(reader));
                return results;
            }
            catch (Npgsql.PostgresException ex) { HandlePostgresException<T>(ex); throw; }
        }

        protected internal async Task<object> ExecuteInsertAsync<T>(NpgsqlCommand command, T entity, PropertyInfo primaryKey) where T : class, new()
        {
            InvokeLogAction(command);
            var executeScalar = await command.ExecuteScalarAsync().ConfigureAwait(false);
            if (executeScalar != null && executeScalar != DBNull.Value)
            {
                var targetType = Nullable.GetUnderlyingType(primaryKey.PropertyType) ?? primaryKey.PropertyType;
                var result = Convert.ChangeType(executeScalar, targetType);
                primaryKey.SetValue(entity, result);
                return result;
            }
            throw new InvalidOperationException("Insert failed: No ID returned.");
        }

        protected internal async Task ExecuteUpdateAsync(NpgsqlCommand command)
        {
            InvokeLogAction(command);
            var rowsAffected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            if (rowsAffected == 0) throw new InvalidOperationException("Update failed: No rows affected.");
        }

        #endregion

        #region CRUD Operations

        public override T Get<T>(dynamic key = null)
        {
            DiscoverColumns<T>();
            using (var connectionScope = new ConnectionScope(this))
            {
                var commandText = CreateGetOneOrSelectCommandText<T>(key);
                using (var command = BuildSqlCommandObject(commandText, connectionScope.Connection))
                    return ExecuteReaderSingle<T>(command);
            }
        }

        [Obsolete("Use Query<T>().Where(predicate) instead. This method materializes results immediately.")]
        public override ICollection<T> Query<T>(Expression<Func<T, bool>> expression)
        {
            DiscoverColumns<T>();
            using (var connectionScope = new ConnectionScope(this))
            {
                var elements = CreateSelectQueryObject(expression);
                var commandText = elements.SelectClause;
                if (!string.IsNullOrEmpty(elements.WhereClause)) commandText += $"\r\nWHERE {elements.WhereClause}";
                if (!string.IsNullOrEmpty(elements.OrderByClause)) commandText += $"\r\n{elements.OrderByClause}";
                using (var command = BuildSqlCommandObject(commandText, connectionScope.Connection, elements.SqlParameters))
                    return ExecuteReaderList<T>(command);
            }
        }

        public override ICollection<T> GetList<T>()
        {
            DiscoverColumns<T>();
            using (var connectionScope = new ConnectionScope(this))
            {
                using (var command = BuildSqlCommandObject(CreateGetOneOrSelectCommandText<T>(), connectionScope.Connection))
                    return ExecuteReaderList<T>(command);
            }
        }

        public override object Insert<T>(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            DiscoverColumns<T>();
            var primaryKey = GetCachedPrimaryKey<T>();
            ValidateInsertPrimaryKey(entity, primaryKey);
            using (var connectionScope = new ConnectionScope(this))
            {
                var insertCommand = BuildInsertCommandObject(entity, primaryKey);
                using (var command = BuildSqlCommandObject(insertCommand.CommandText, connectionScope.Connection, insertCommand.Parameters))
                    return ExecuteInsert(command, entity, primaryKey);
            }
        }

        public override TKey Insert<T, TKey>(T entity) { return (TKey)Insert(entity); }

        public override T Update<T>(T entity)
        {
            DiscoverColumns<T>();
            var primaryKey = GetCachedPrimaryKey<T>();
            ValidateUpdatePrimaryKey(entity, primaryKey);
            using (var connectionScope = new ConnectionScope(this))
            {
                var existingEntity = Get<T>((dynamic)primaryKey.GetValue(entity));
                if (existingEntity == null) throw new InvalidOperationException("Entity does not exist in database.");
                CommandParameters commandParameters = BuildUpdateCommand(entity, existingEntity, primaryKey);
                if (commandParameters.Parameters.Any())
                {
                    using (var command = BuildSqlCommandObject(commandParameters.CommandText, connectionScope.Connection, commandParameters.Parameters))
                        ExecuteUpdate(command);
                }
                else { Log?.Invoke($"No update needed for {typeof(T).Name} with id {primaryKey.GetValue(entity)}."); }
                return entity;
            }
        }

        public override IQueryable<T> Query<T>()
        {
            string selectCommand = CreateGetOneOrSelectCommandText<T>();
            return CreateQueryable<T>(selectCommand);
        }

        public override int Delete<T>(Expression<Func<T, bool>> predicate)
        {
            if (Transaction == null) throw new InvalidOperationException("Delete operations must be performed within an active transaction.");
            if (predicate == null) throw new InvalidOperationException("A WHERE clause (predicate) is required for deletes.");
            var components = GenerateWhereClause(predicate);
            ValidateWhereClause<T>(components.WhereClause);
            var tableName = GetTableName<T>();
            var commandText = Dialect.BuildDeleteCommand(tableName, $" WHERE {components.WhereClause}");
            using (var command = BuildSqlCommandObject(commandText, Connection, components.SqlParameters))
            {
                InvokeLogAction(command);
                var affected = command.ExecuteNonQuery();
                if (affected == 0) Log?.Invoke($"Delete affected zero rows for {typeof(T).Name}.");
                return affected;
            }
        }

        public override bool Delete<T>(long id)
        {
            if (Transaction == null) throw new InvalidOperationException("Delete operations must be performed within an active transaction.");
            var pk = GetCachedPrimaryKey<T>();
            var tableName = GetTableName<T>();
            var pkColumn = GetCachedColumnName(pk);
            var commandText = Dialect.BuildDeleteCommand(tableName, $" WHERE {pkColumn} = @id");
            var param = new NpgsqlParameter("@id", id);
            using (var command = BuildSqlCommandObject(commandText, Connection, new[] { param }))
            {
                InvokeLogAction(command);
                var affected = command.ExecuteNonQuery();
                if (affected == 0) Log?.Invoke($"Delete affected zero rows for {typeof(T).Name} with id {id}.");
                return affected > 0;
            }
        }

        #endregion

        #region Transaction Management

        public void BeginTransaction(string name = null)
        {
            EnsureConnectionOpen();
            if (Transaction != null) throw new InvalidOperationException("Transaction already in progress.");
            Transaction = Connection?.BeginTransaction(IsolationLevel.ReadCommitted);
            TransactionName = name ?? string.Empty;
        }

        public void CommitTransaction(string name = null)
        {
            if (Transaction != null && (string.IsNullOrEmpty(name) || TransactionName == name))
            {
                Transaction.Commit();
                CleanupTransaction();
            }
        }

        public void RollbackTransaction(string name = null)
        {
            if (Transaction != null && (string.IsNullOrEmpty(name) || TransactionName == name))
            {
                Transaction.Rollback();
                CleanupTransaction();
            }
        }

        #endregion

        #region Utility

        public override void Dispose()
        {
            Connection?.Dispose();
            Transaction?.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Connection Management

        protected IDbConnection GetConnection()
        {
            EnsureConnectionOpen();
            return Connection;
        }

        protected void EnsureConnectionOpen()
        {
            if (Connection == null || Connection.State != ConnectionState.Open)
            {
                Connection = new NpgsqlConnection(_connectionString);
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

        #region Query Building

        internal class ResolvedRemoteJoinInfo
        {
            public string JoinClauses { get; set; }
            public List<string> IndividualJoinClauses { get; set; }
            public string ExtraColumns { get; set; }
            public Dictionary<string, string> PropertyToColumnMap { get; set; }
        }

        internal ResolvedRemoteJoinInfo ResolveRemoteJoins<T>(string tableName)
        {
            var info = new ResolvedRemoteJoinInfo
            {
                JoinClauses = string.Empty,
                IndividualJoinClauses = new List<string>(),
                ExtraColumns = string.Empty,
                PropertyToColumnMap = new Dictionary<string, string>()
            };

            var remoteProperties = typeof(T).GetProperties()
                .Where(p => Attribute.IsDefined(p, typeof(RemoteAttributeBase)))
                .ToList();

            if (!remoteProperties.Any()) return info;

            var resolver = new RemotePathResolver();
            var joinClauses = new StringBuilder();
            var extraColumns = new StringBuilder();
            var existingJoins = new Dictionary<string, string>();
            var aliasCounts = new Dictionary<string, int>();

            string GetTableNameByType(Type t) => _tableNames.GetOrAdd(t, type =>
                Dialect.EncloseIdentifier(type.GetCustomAttribute<TableAttribute>()?.Name ?? type.Name.ToLower()));

            foreach (var prop in remoteProperties)
            {
                var attr = (RemoteAttributeBase)prop.GetCustomAttributes(typeof(RemoteAttributeBase), true).First();
                var remoteType = attr.RemoteEntityType;
                string[] keyPath = attr.KeyPath;

                var resolvedPath = resolver.Resolve(typeof(T), remoteType, keyPath);
                string currentAlias = tableName;

                foreach (var step in resolvedPath.Joins)
                {
                    string targetTableName = GetTableNameByType(step.TargetTableType);
                    // Use double-quote stripped name for aliases
                    string targetTableCleanName = targetTableName.Trim('"');

                    string joinKey = $"{currentAlias}|{targetTableName}|{step.ForeignKeyProperty}";

                    if (existingJoins.ContainsKey(joinKey))
                    {
                        currentAlias = existingJoins[joinKey];
                    }
                    else
                    {
                        if (!aliasCounts.ContainsKey(targetTableCleanName)) aliasCounts[targetTableCleanName] = 0;
                        int count = aliasCounts[targetTableCleanName]++;
                        string targetAlias = $"\"{targetTableCleanName}_{count}\"";

                        var fkProp = step.SourceTableType.GetProperty(step.ForeignKeyProperty);
                        string fkColumn = fkProp != null ? GetCachedColumnName(fkProp) : Dialect.EncloseIdentifier(step.ForeignKeyProperty.ToLower());

                        var pkProp = step.TargetTableType.GetProperty(step.TargetKeyProperty);
                        string pkColumn = pkProp != null ? GetCachedColumnName(pkProp) : Dialect.EncloseIdentifier(step.TargetKeyProperty.ToLower());

                        string joinClause = $" LEFT JOIN {targetTableName} {targetAlias} ON {currentAlias}.{fkColumn} = {targetAlias}.{pkColumn}";
                        joinClauses.Append(joinClause);
                        info.IndividualJoinClauses.Add(joinClause);

                        existingJoins[joinKey] = targetAlias;
                        currentAlias = targetAlias;
                    }
                }

                var finalProp = resolvedPath.TargetProperty;
                string finalColumn = finalProp != null ? GetCachedColumnName(finalProp) : Dialect.EncloseIdentifier(resolvedPath.FinalColumnName.ToLower());

                if (attr is RemoteKeyAttribute || attr is RemotePropertyAttribute)
                    info.PropertyToColumnMap[prop.Name] = $"{currentAlias}.{finalColumn}";

                extraColumns.Append($", {currentAlias}.{finalColumn} AS \"{prop.Name}\"");
            }

            info.JoinClauses = joinClauses.ToString();
            info.ExtraColumns = extraColumns.ToString();
            return info;
        }

        protected internal string CreateGetOneOrSelectCommandText<T>(dynamic key = null) where T : class, new()
        {
            var tableName = GetTableName<T>();
            string columnNames = GetColumnNames<T>();
            var whereClause = key != null ? GetWhereClause<T>(key) : string.Empty;

            var remoteInfo = ResolveRemoteJoins<T>(tableName);
            string joinClauses = null;

            if (!string.IsNullOrEmpty(remoteInfo.JoinClauses))
            {
                var columns = columnNames.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < columns.Length; i++)
                    columns[i] = $"{tableName}.{columns[i]}";
                columnNames = string.Join(", ", columns);

                if (!string.IsNullOrEmpty(whereClause))
                {
                    var pk = GetCachedPrimaryKey<T>();
                    var pkCol = GetCachedColumnName(pk);
                    whereClause = whereClause.Replace(pkCol, $"{tableName}.{pkCol}");
                }
                columnNames += remoteInfo.ExtraColumns;
                joinClauses = remoteInfo.JoinClauses;
            }

            return Dialect.BuildSelectCommand(tableName, columnNames, whereClause, joinClauses);
        }

        protected internal PostgreSqlQueryComponents<T> CreateSelectQueryObject<T>(Expression<Func<T, bool>> whereExpression) where T : class, new()
        {
            var selectClause = CreateGetOneOrSelectCommandText<T>();
            var elements = GenerateWhereClause(whereExpression);
            elements.SelectClause = selectClause;
            elements.OriginalExpression = whereExpression;
            return elements;
        }

        protected internal PostgreSqlQueryComponents<T> GenerateWhereClause<T>(
            Expression<Func<T, bool>> expression,
            PostgreSqlQueryComponents<T> commandElements = null,
            PostgreSqlParameterGenerator parameterGenerator = null,
            PostgreSqlExpressionTranslator translator = null) where T : class, new()
        {
            var paramGen = parameterGenerator ?? new PostgreSqlParameterGenerator();
            var trans = translator ?? new PostgreSqlExpressionTranslator(paramGen);

            var tableName = GetTableName<T>();
            var remoteInfo = ResolveRemoteJoins<T>(tableName);

            var visitor = new PostgreSqlWhereClauseVisitor<T>(
                ColumnNamesCache,
                _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>),
                paramGen, trans, tableName, remoteInfo.PropertyToColumnMap);
            visitor.Visit(expression);

            if (commandElements != null)
            {
                commandElements.WhereClause = visitor.WhereClauseBody;
                commandElements.JoinClause = remoteInfo.JoinClauses;
                commandElements.JoinClausesList = remoteInfo.IndividualJoinClauses;
                commandElements.OriginalExpression = commandElements.OriginalExpression ?? expression;
                if (visitor.Parameters.Any())
                {
                    commandElements.SqlParameters = commandElements.SqlParameters ?? new List<NpgsqlParameter> { };
                    commandElements.SqlParameters.AddRange(visitor.Parameters);
                }
            }
            else
            {
                commandElements = new PostgreSqlQueryComponents<T>(expression, string.Empty, visitor.WhereClauseBody, remoteInfo.JoinClauses, string.Empty, visitor.Parameters);
                commandElements.JoinClausesList = remoteInfo.IndividualJoinClauses;
            }
            return commandElements;
        }

        protected internal PostgreSqlQueryComponents<T> GenerateOrderByClause<T>(Expression<Func<T, bool>> expression,
            PostgreSqlQueryComponents<T> commandElements = null) where T : class, new()
        {
            var visitor = new PostgreSqlOrderByClauseVisitor<T>(
                ColumnNamesCache,
                _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>));
            visitor.Visit(expression);
            if (commandElements == null)
                commandElements = new PostgreSqlQueryComponents<T>(expression, string.Empty, string.Empty, string.Empty, visitor.OrderByClause, new List<NpgsqlParameter> { });
            else
            {
                commandElements.OrderByClause = visitor.OrderByClause;
                commandElements.OriginalExpression = commandElements.OriginalExpression ?? expression;
            }
            return commandElements;
        }

        private void ValidateWhereClause<T>(string whereClause)
        {
            if (string.IsNullOrWhiteSpace(whereClause))
                throw new InvalidOperationException("Delete operation requires a non-empty, valid WHERE clause.");
            var trivialPatterns = new[] { "1=1", "1 < 2", "1 > 0", "true", "WHERE 1=1", "WHERE 1 < 2" };
            if (trivialPatterns.Any(p => whereClause.Replace(" ", "").Contains(p.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Delete operation requires a non-trivial WHERE clause.");
            var regex = new System.Text.RegularExpressions.Regex(@"\b(\w+)\s*=\s*\1\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (regex.IsMatch(whereClause))
                throw new InvalidOperationException("Delete operation WHERE clause cannot be a self-referencing column expression.");
            var tableColumns = typeof(T).GetProperties()
                .Where(p => p.GetCustomAttributes(typeof(NotMappedAttribute), true).Length == 0)
                .Select(p => GetCachedColumnName(p))
                .ToList();
            bool columnReferenced = tableColumns.Any(col => whereClause.IndexOf(col, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!columnReferenced)
                throw new InvalidOperationException("Delete operation WHERE clause must reference at least one column from the target table.");
        }

        #endregion

        #region Execution Helpers

        protected internal T ExecuteReaderSingle<T>(NpgsqlCommand command) where T : class, new()
        {
            InvokeLogAction(command);
            try
            {
                using (var reader = command.ExecuteReader())
                    return reader.Read() ? MapEntity<T>(reader) : null;
            }
            catch (Npgsql.PostgresException ex) { HandlePostgresException<T>(ex); throw; }
        }

        protected internal ICollection<T> ExecuteReaderList<T>(NpgsqlCommand command) where T : class, new()
        {
            InvokeLogAction(command);
            var results = new List<T>();
            try
            {
                using (var reader = command.ExecuteReader())
                    while (reader.Read())
                        results.Add(MapEntity<T>(reader));
            }
            catch (Npgsql.PostgresException ex) { HandlePostgresException<T>(ex); throw; }
            return results;
        }

        protected internal NpgsqlCommand BuildSqlCommandObject(string commandText, IDbConnection connection, ICollection<NpgsqlParameter> parameters = null)
        {
            var command = new NpgsqlCommand(commandText, (NpgsqlConnection)connection)
            {
                CommandType = CommandType.Text,
                Transaction = (NpgsqlTransaction)Transaction
            };
            if (parameters?.Any() == true)
                command.Parameters.AddRange(parameters.ToArray());
            return command;
        }

        protected void DiscoverColumns<T>()
        {
            if (_mappedTypes.Contains(typeof(T))) return;
            var table = GetTableName<T>();
            // PostgreSQL: Use LIMIT 0 instead of TOP 0
            var commandText = $"SELECT * FROM {table} LIMIT 0";
            var properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties().ToArray());

            using (var connectionScope = new ConnectionScope(this))
            {
                using (var command = BuildSqlCommandObject(commandText, connectionScope.Connection, Array.Empty<NpgsqlParameter>()))
                {
                    try
                    {
                        using (var reader = command.ExecuteReader(CommandBehavior.SchemaOnly))
                        {
                            ICollection<string> columnNamesList = new List<string>();
#if NET8_0_OR_GREATER
                            var columnSchema = reader.GetColumnSchema();
                            foreach (var dbColumn in columnSchema) columnNamesList.Add(dbColumn.ColumnName);
#else
                            var schemaTable = reader.GetSchemaTable();
                            foreach (DataRow row in schemaTable?.Rows)
                                columnNamesList.Add(row["ColumnName"].ToString());
#endif
                            var comparer = new IgnoreUnderscoreAndCaseStringComparer();
                            foreach (var property in properties)
                            {
                                if (property.GetCustomAttribute<NotMappedAttribute>() != null) continue;
                                var columnAttr = property.GetCustomAttribute<ColumnAttribute>();
                                string actualColumnName = columnAttr?.Name ??
                                    columnNamesList.FirstOrDefault(c => comparer.Equals(c, property.Name));
                                if (actualColumnName != null)
                                {
                                    var key = property.ToDictionaryKey();
                                    ColumnNamesCache[key] = Dialect.EncloseIdentifier(actualColumnName);
                                }
                            }
                            _mappedTypes.Add(typeof(T));
                        }
                    }
                    catch (Npgsql.PostgresException ex) { HandlePostgresException<T>(ex); throw; }
                }
            }
        }

        private static string GetSchemaSignature(NpgsqlDataReader reader)
        {
            var cols = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
                cols.Add($"{reader.GetName(i)}:{reader.GetFieldType(i)?.FullName}");
            return string.Join("|", cols);
        }

        protected T MapEntity<T>(NpgsqlDataReader reader) where T : class, new()
        {
            string schemaKey = typeof(T).FullName + "|" + GetSchemaSignature(reader);
            var mapper = (Func<NpgsqlDataReader, T>)_entityMappers.GetOrAdd(schemaKey, _ => BuildDataReaderMapper<T>(reader));
            return mapper(reader);
        }

        private Func<NpgsqlDataReader, T> BuildDataReaderMapper<T>(NpgsqlDataReader reader) where T : class, new()
        {
            var type = typeof(T);
            string schemaSignature = GetSchemaSignature(reader);
            string ordinalsKey = type.FullName + "|" + schemaSignature;

            var schemaOrdinals = _columnOrdinalsCache.GetOrAdd(ordinalsKey, _ =>
            {
                var ordinals = new Dictionary<string, int>(new IgnoreUnderscoreAndCaseStringComparer());
                for (int i = 0; i < reader.FieldCount; i++) ordinals[reader.GetName(i)] = i;
                return ordinals;
            });

            var properties = _propertiesCache.GetOrAdd(type, t => t.GetProperties());
            var unmappedNames = new HashSet<string>(
                _unmappedPropertiesCache.GetOrAdd(type, GetUnmappedProperties<T>).Select(p => p.Name));

            var mappings = properties.Select(p =>
            {
                string columnName;
                if (unmappedNames.Contains(p.Name))
                    columnName = p.Name;
                else
                    columnName = GetCachedColumnName(p);

                if (string.IsNullOrEmpty(columnName)) return null;

                if (!schemaOrdinals.TryGetValue(columnName, out int ordinal))
                {
                    // Try removing quotes if present (e.g. "Order" -> Order)
                    if (columnName.StartsWith("\"") && columnName.EndsWith("\""))
                    {
                        var unquoted = columnName.Substring(1, columnName.Length - 2);
                        if (!schemaOrdinals.TryGetValue(unquoted, out ordinal))
                            return null;
                    }
                    else return null;
                }

                var setter = GetOrCreateSetter(p);
                var propertyType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                return new { Ordinal = ordinal, Setter = setter, Type = propertyType, IsEnum = propertyType.IsEnum };
            }).Where(m => m != null).ToArray();

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

        #region Insert/Update Helpers

        protected internal CommandParameters BuildInsertCommandObject<T>(T entity, PropertyInfo primaryKey) where T : class, new()
        {
            var tableName = GetTableName<T>();
            var unmapped = _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>);
            var properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties().ToArray())
                .Where(p => unmapped.All(up => up.Name != p.Name));
            var result = Dialect.BuildInsertCommand(entity, tableName, primaryKey, GetCachedColumnName, GetDefault, properties);
            return new CommandParameters(result.CommandText, result.Parameters.Cast<NpgsqlParameter>().ToList());
        }

        protected internal object ExecuteInsert<T>(NpgsqlCommand command, T entity, PropertyInfo primaryKey) where T : class, new()
        {
            InvokeLogAction(command);
            var executeScalar = command.ExecuteScalar();
            if (executeScalar != null && executeScalar != DBNull.Value)
            {
                var targetType = Nullable.GetUnderlyingType(primaryKey.PropertyType) ?? primaryKey.PropertyType;
                var result = Convert.ChangeType(executeScalar, targetType);
                primaryKey.SetValue(entity, result);
                return result;
            }
            throw new InvalidOperationException("Insert failed: No ID returned.");
        }

        protected internal CommandParameters BuildUpdateCommand<T>(T entity, T existing, PropertyInfo primaryKey) where T : class, new()
        {
            var tableName = GetTableName<T>();
            var unmapped = _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>);
            var properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties().ToArray())
                .Where(p => unmapped.All(up => up.Name != p.Name));
            var result = Dialect.BuildUpdateCommand(entity, existing, tableName, primaryKey, GetCachedColumnName, properties);
            return new CommandParameters(result.CommandText, result.Parameters.Cast<NpgsqlParameter>().ToList());
        }

        protected internal void ExecuteUpdate(NpgsqlCommand command)
        {
            InvokeLogAction(command);
            var rowsAffected = command.ExecuteNonQuery();
            if (rowsAffected == 0) throw new InvalidOperationException("Update failed: No rows affected.");
        }

        #endregion

        #region Validation and Helpers

        protected internal void ValidateInsertPrimaryKey<T>(T entity, PropertyInfo primaryKey)
        {
            if (primaryKey.PropertyType == typeof(int) || primaryKey.PropertyType == typeof(long))
            {
                var defaultValue = GetDefault(primaryKey.PropertyType);
                if (!Equals(primaryKey.GetValue(entity), defaultValue))
                    throw new InvalidOperationException($"Primary key must be default value for insert: {primaryKey.GetValue(entity)}");
            }
        }

        protected internal void ValidateUpdatePrimaryKey<T>(T entity, PropertyInfo primaryKey)
        {
            var value = primaryKey.GetValue(entity);
            if (value == null || Equals(value, GetDefault(primaryKey.PropertyType)))
                throw new InvalidOperationException("Primary key must be set for update.");
        }

        protected internal NpgsqlParameter CreateParameter<T>(string name, object value, Type propertyType) where T : class, new()
        {
            var npgsqlType = PostgreSqlParameterGenerator.GetNpgsqlDbType(value);
            return new NpgsqlParameter(name, value ?? DBNull.Value)
            {
                NpgsqlDbType = npgsqlType,
                IsNullable = Nullable.GetUnderlyingType(propertyType) != null || propertyType.IsClass
            };
        }

        protected internal void InvokeLogAction(NpgsqlCommand command)
        {
            Log?.Invoke(command.CommandText);
            foreach (var param in command.Parameters.AsEnumerable())
                Log?.Invoke($"{param.ParameterName}: {param.Value}");
        }

        protected internal void CleanupTransaction()
        {
            Transaction.Dispose();
            Transaction = null;
            TransactionName = null;
            CloseConnectionIfNoTransaction();
        }

        internal string GetTableNameInternal<T>() where T : class, new() => GetTableName<T>();

        internal string GetCachedColumnNameInternal(PropertyInfo property) => GetCachedColumnName(property);

        #endregion

        #region Nested Types

        public class CommandParameters
        {
            public CommandParameters(string command, List<NpgsqlParameter> parameters)
            {
                CommandText = command;
                Parameters = parameters;
            }
            public string CommandText;
            public List<NpgsqlParameter> Parameters = new List<NpgsqlParameter>();
        }

        internal class ConnectionScope : IDisposable
        {
            public ConnectionScope(PostgreSqlOrmDataProvider provider)
            {
                _provider = provider;
                _createdConnection = provider.Connection == null;
            }

            public IDbConnection Connection => _provider.GetConnection();
            protected internal readonly PostgreSqlOrmDataProvider _provider;
            protected internal readonly bool _createdConnection;

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

        protected internal static ICollection<PropertyInfo> GetUnmappedProperties<T>(Type type)
            where T : class, new()
        {
            var properties = typeof(T).GetProperties();
            var explicitlyUnmapped = properties.Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null);
            var remoteUnmapped = properties.Where(p => p.GetCustomAttribute<Funcular.Data.Orm.Attributes.RemoteAttributeBase>() != null);
            var knownUnmapped = explicitlyUnmapped.Concat(remoteUnmapped).ToList();
            var implicitlyUnmapped = properties.Where(p =>
            {
                if (knownUnmapped.Any(up => up.Name == p.Name)) return false;
                var columnAttr = p.GetCustomAttribute<ColumnAttribute>();
                if (columnAttr != null) return false;
                var key = p.ToDictionaryKey();
                return !_columnNames.ContainsKey(key);
            });
            return knownUnmapped.Concat(implicitlyUnmapped).Distinct().ToArray();
        }

        protected internal object GetDefault(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;

        protected internal string GetColumnNames<T>() where T : class, new()
        {
            DiscoverColumns<T>();
            var unmapped = _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>);
            return string.Join(", ", typeof(T).GetProperties()
                .Where(p => unmapped.All(up => up.Name != p.Name))
                .Select(p => GetCachedColumnName(p)));
        }

        protected string GetWhereClause<T>(dynamic key) where T : class
        {
            var pk = GetCachedPrimaryKey<T>();
            var pkColumn = GetCachedColumnName(pk);
            // Quote non-numeric keys (Guid, string) for PostgreSQL
            if (key is string || key is Guid)
                return $" WHERE {pkColumn} = '{key}'";
            return $" WHERE {pkColumn} = {key}";
        }

        internal PropertyInfo GetPrimaryKeyProperty<T>() => typeof(T).GetProperties().FirstOrDefault(p =>
            p.GetCustomAttribute<KeyAttribute>() != null ||
            p.GetCustomAttribute<DatabaseGeneratedAttribute>()?.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity ||
            p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals($"{typeof(T).Name}Id", StringComparison.OrdinalIgnoreCase));

        protected override string GetTableName<T>() => _tableNames.GetOrAdd(typeof(T), t =>
            Dialect.EncloseIdentifier(t.GetCustomAttribute<TableAttribute>()?.Name ?? t.Name.ToLower()));

        private string GetTableNameByType(Type type) => _tableNames.GetOrAdd(type, t =>
            Dialect.EncloseIdentifier(t.GetCustomAttribute<TableAttribute>()?.Name ?? t.Name.ToLower()));

        /// <summary>
        /// Overrides the base column name resolution to use the same dictionary key
        /// as DiscoverColumns (ToDictionaryKey) and to apply identifier quoting for
        /// PostgreSQL reserved words.
        /// </summary>
        protected override string GetCachedColumnName(PropertyInfo property)
        {
            var key = property.ToDictionaryKey();
            return ColumnNamesCache.GetOrAdd(key, _ =>
            {
                var columnAttribute = property.GetCustomAttribute<ColumnAttribute>();
                var rawName = columnAttribute != null ? columnAttribute.Name : property.Name;
                return Dialect.EncloseIdentifier(rawName);
            });
        }

        private IQueryable<T> CreateQueryable<T>(string selectClause) where T : class, new()
        {
            DiscoverColumns<T>();
            var provider = new PostgreSqlLinqQueryProvider<T>(this, selectClause);
            return new PostgreSqlQueryable<T>(provider);
        }

        protected internal int ExecuteNonQuery(string sql, params NpgsqlParameter[] parameters)
        {
            using (var connectionScope = new ConnectionScope(this))
            {
                using (var command = BuildSqlCommandObject(sql, connectionScope.Connection, parameters))
                {
                    InvokeLogAction(command);
                    return command.ExecuteNonQuery();
                }
            }
        }

        #endregion
    }
}
