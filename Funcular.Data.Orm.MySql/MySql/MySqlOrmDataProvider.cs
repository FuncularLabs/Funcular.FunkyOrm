using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
#if NETSTANDARD2_0
using System.Data.Common;
#endif
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Funcular.Data.Orm;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.Interfaces;
using Funcular.Data.Orm.MySql.Visitors;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql
{
    /// <summary>
    /// A MySQL specific implementation of an ORM data provider, built on the MIT-licensed MySqlConnector driver.
    /// </summary>
    public partial class MySqlOrmDataProvider : OrmDataProvider, ISqlOrmProvider
    {
        #region Fields

        private readonly string _connectionString;
        private IDbTransaction _transaction;

        /// <summary>
        /// Tracks the number of <see cref="ConnectionScope"/> instances currently using the provider's
        /// shared transactional connection. Used to detect invalid concurrent usage during a transaction.
        /// </summary>
        private int _activeTransactionalScopes;
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

        public MySqlOrmDataProvider(string connectionString, IDbConnection connection = null,
            IDbTransaction transaction = null, ISqlDialect dialect = null)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            Connection = connection;
            Transaction = transaction;
            Dialect = dialect ?? new MySqlDialect();
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
                // Read the existing row on the scope's own connection (not via GetAsync<T>, which would
                // open a second ConnectionScope and trip the transactional concurrency guard).
                string existingCommandText = CreateGetOneOrSelectCommandText<T>((dynamic)primaryKey.GetValue(entity));
                T existingEntity;
                using (var existingCommand = BuildSqlCommandObject(existingCommandText, connectionScope.Connection))
                    existingEntity = await ExecuteReaderSingleAsync<T>(existingCommand).ConfigureAwait(false);
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
            var param = new MySqlParameter("@id", id);
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

        private void HandleMySqlException<T>(MySqlException ex)
        {
            // 1146 = ER_NO_SUCH_TABLE
            if (ex.Number == 1146)
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

        protected internal async Task<T> ExecuteReaderSingleAsync<T>(MySqlCommand command) where T : class, new()
        {
            InvokeLogAction(command);
            try
            {
                using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
                    return await reader.ReadAsync().ConfigureAwait(false) ? MapEntity<T>(reader) : null;
            }
            catch (MySqlException ex) { HandleMySqlException<T>(ex); throw; }
        }

        protected internal async Task<ICollection<T>> ExecuteReaderListAsync<T>(MySqlCommand command) where T : class, new()
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
            catch (MySqlException ex) { HandleMySqlException<T>(ex); throw; }
        }

        protected internal async Task<object> ExecuteInsertAsync<T>(MySqlCommand command, T entity, PropertyInfo primaryKey) where T : class, new()
        {
            InvokeLogAction(command);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            return AssignInsertedKey(command, entity, primaryKey);
        }

        protected internal async Task ExecuteUpdateAsync(MySqlCommand command)
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
                // Read the existing row on the scope's own connection (not via Get<T>, which would
                // open a second ConnectionScope and trip the transactional concurrency guard).
                string existingCommandText = CreateGetOneOrSelectCommandText<T>((dynamic)primaryKey.GetValue(entity));
                T existingEntity;
                using (var existingCommand = BuildSqlCommandObject(existingCommandText, connectionScope.Connection))
                    existingEntity = ExecuteReaderSingle<T>(existingCommand);
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
            var param = new MySqlParameter("@id", id);
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

        /// <summary>
        /// Begins a new database transaction. The provider will create and open a connection if necessary.
        /// </summary>
        /// <remarks>
        /// <b>Concurrency Warning:</b> All operations within a transaction share a single underlying
        /// <see cref="IDbConnection"/>. Operations within a transaction <b>must be awaited sequentially</b>.
        /// Outside of a transaction, each operation receives its own pooled connection.
        /// </remarks>
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
                Connection = new MySqlConnection(_connectionString);
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

            var allProperties = typeof(T).GetProperties();
            var remoteProperties = allProperties
                .Where(p => Attribute.IsDefined(p, typeof(RemoteAttributeBase)))
                .ToList();
            var jsonPathProperties = allProperties
                .Where(p => Attribute.IsDefined(p, typeof(JsonPathAttribute)))
                .ToList();
            var sqlExprProperties = allProperties
                .Where(p => Attribute.IsDefined(p, typeof(SqlExpressionAttribute)))
                .ToList();
            var subqueryAggProperties = allProperties
                .Where(p => Attribute.IsDefined(p, typeof(SubqueryAggregateAttribute)))
                .ToList();
            var jsonCollectionProperties = allProperties
                .Where(p => Attribute.IsDefined(p, typeof(JsonCollectionAttribute)))
                .ToList();

            if (!remoteProperties.Any() && !jsonPathProperties.Any()
                && !sqlExprProperties.Any() && !subqueryAggProperties.Any()
                && !jsonCollectionProperties.Any()) return info;

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
                    // Use backtick-stripped name for aliases
                    string targetTableCleanName = targetTableName.Trim('`');

                    string joinKey = $"{currentAlias}|{targetTableName}|{step.ForeignKeyProperty}";

                    if (existingJoins.ContainsKey(joinKey))
                    {
                        currentAlias = existingJoins[joinKey];
                    }
                    else
                    {
                        if (!aliasCounts.ContainsKey(targetTableCleanName)) aliasCounts[targetTableCleanName] = 0;
                        int count = aliasCounts[targetTableCleanName]++;
                        string targetAlias = $"`{targetTableCleanName}_{count}`";

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

                extraColumns.Append($", {currentAlias}.{finalColumn} AS `{prop.Name}`");
            }

            // --- JsonPath attribute processing ---
            foreach (var prop in jsonPathProperties)
            {
                var attr = prop.GetCustomAttribute<JsonPathAttribute>();
                if (attr == null) continue;

                string jsonColumn = $"{tableName}.{Dialect.EncloseIdentifier(attr.ColumnName)}";
                string jsonExpr = Dialect.BuildJsonValueExpression(jsonColumn, attr.Path, attr.SqlType);

                info.PropertyToColumnMap[prop.Name] = jsonExpr;
                extraColumns.Append($", {jsonExpr} AS `{prop.Name}`");
            }

            // --- SqlExpression attribute processing ---
            foreach (var prop in sqlExprProperties)
            {
                var attr = prop.GetCustomAttribute<SqlExpressionAttribute>();
                if (attr == null) continue;

                string rawExpr = attr.GetExpression(Dialect.ProviderName);
                string resolvedExpr = ResolveExpressionTokens(rawExpr, typeof(T), tableName, info.PropertyToColumnMap);

                info.PropertyToColumnMap[prop.Name] = resolvedExpr;
                extraColumns.Append($", {resolvedExpr} AS `{prop.Name}`");
            }

            // --- SubqueryAggregate attribute processing ---
            foreach (var prop in subqueryAggProperties)
            {
                var attr = prop.GetCustomAttribute<SubqueryAggregateAttribute>();
                if (attr == null) continue;

                string childTable = GetTableNameForType(attr.SourceType);
                var childFkPropInfo = attr.SourceType.GetProperty(attr.ForeignKey);
                string childFkCol = childFkPropInfo != null
                    ? GetCachedColumnName(childFkPropInfo)
                    : Dialect.EncloseIdentifier(attr.ForeignKey.ToLower());
                var pk = GetCachedPrimaryKey<T>();
                string parentPk = $"{tableName}.{GetCachedColumnName(pk)}";

                string aggCol = null;
                if (!string.IsNullOrEmpty(attr.AggregateColumn))
                {
                    var aggProp = attr.SourceType.GetProperty(attr.AggregateColumn);
                    aggCol = aggProp != null ? GetCachedColumnName(aggProp) : Dialect.EncloseIdentifier(attr.AggregateColumn.ToLower());
                }

                string condCol = null;
                if (!string.IsNullOrEmpty(attr.ConditionColumn))
                {
                    var condProp = attr.SourceType.GetProperty(attr.ConditionColumn);
                    condCol = condProp != null ? GetCachedColumnName(condProp) : Dialect.EncloseIdentifier(attr.ConditionColumn.ToLower());
                }

                string subquery = Dialect.BuildScalarSubquery(childTable, childFkCol, parentPk,
                    attr.Function, aggCol, condCol, attr.ConditionValue);

                info.PropertyToColumnMap[prop.Name] = subquery;
                extraColumns.Append($", {subquery} AS `{prop.Name}`");
            }

            // --- JsonCollection attribute processing ---
            foreach (var prop in jsonCollectionProperties)
            {
                var attr = prop.GetCustomAttribute<JsonCollectionAttribute>();
                if (attr == null) continue;

                string childTable = GetTableNameForType(attr.SourceType);
                var childFkPropInfo = attr.SourceType.GetProperty(attr.ForeignKey);
                string childFkCol = childFkPropInfo != null
                    ? GetCachedColumnName(childFkPropInfo)
                    : Dialect.EncloseIdentifier(attr.ForeignKey.ToLower());
                var pk = GetCachedPrimaryKey<T>();
                string parentPk = $"{tableName}.{GetCachedColumnName(pk)}";

                var colExprs = new List<string>();
                if (attr.Columns != null)
                {
                    foreach (var colName in attr.Columns)
                    {
                        var colProp = attr.SourceType.GetProperty(colName);
                        string resolved = colProp != null ? GetCachedColumnName(colProp) : Dialect.EncloseIdentifier(colName.ToLower());
                        colExprs.Add(resolved);
                    }
                }
                else
                {
                    colExprs.Add("*");
                }

                string orderByCol = null;
                if (!string.IsNullOrEmpty(attr.OrderBy))
                {
                    var orderProp = attr.SourceType.GetProperty(attr.OrderBy);
                    orderByCol = orderProp != null ? GetCachedColumnName(orderProp) : Dialect.EncloseIdentifier(attr.OrderBy.ToLower());
                }

                string subquery = Dialect.BuildJsonCollectionSubquery(childTable, childFkCol, parentPk, colExprs, orderByCol);

                info.PropertyToColumnMap[prop.Name] = subquery;
                extraColumns.Append($", {subquery} AS `{prop.Name}`");
            }

            info.JoinClauses = joinClauses.ToString();
            info.ExtraColumns = extraColumns.ToString();
            return info;
        }

        /// <summary>
        /// Resolves <c>{PropertyName}</c> tokens in an SQL expression to fully qualified column references.
        /// </summary>
        private string ResolveExpressionTokens(string expression, Type entityType, string tableName, Dictionary<string, string> existingMap)
        {
            return System.Text.RegularExpressions.Regex.Replace(expression, @"\{(\w+)\}", match =>
            {
                var propName = match.Groups[1].Value;
                if (existingMap.TryGetValue(propName, out string existing))
                    return existing;

                var prop = entityType.GetProperty(propName);
                if (prop != null)
                {
                    string col = GetCachedColumnName(prop);
                    return $"{tableName}.{col}";
                }
                return match.Value;
            });
        }

        /// <summary>
        /// Gets the resolved table name for a type.
        /// </summary>
        private string GetTableNameForType(Type t)
        {
            return _tableNames.GetOrAdd(t, type =>
                Dialect.EncloseIdentifier(type.GetCustomAttribute<TableAttribute>()?.Name ?? type.Name.ToLower()));
        }

        protected internal string CreateGetOneOrSelectCommandText<T>(dynamic key = null) where T : class, new()
        {
            var tableName = GetTableName<T>();
            string columnNames = GetColumnNames<T>();
            var whereClause = key != null ? GetWhereClause<T>(key) : string.Empty;

            var remoteInfo = ResolveRemoteJoins<T>(tableName);
            string joinClauses = null;

            bool hasJoins = !string.IsNullOrEmpty(remoteInfo.JoinClauses);
            bool hasExtraColumns = !string.IsNullOrEmpty(remoteInfo.ExtraColumns);

            if (hasJoins || hasExtraColumns)
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
                joinClauses = hasJoins ? remoteInfo.JoinClauses : null;
            }

            return Dialect.BuildSelectCommand(tableName, columnNames, whereClause, joinClauses);
        }

        protected internal MySqlQueryComponents<T> CreateSelectQueryObject<T>(Expression<Func<T, bool>> whereExpression) where T : class, new()
        {
            var selectClause = CreateGetOneOrSelectCommandText<T>();
            var elements = GenerateWhereClause(whereExpression);
            elements.SelectClause = selectClause;
            elements.OriginalExpression = whereExpression;
            return elements;
        }

        protected internal MySqlQueryComponents<T> GenerateWhereClause<T>(
            Expression<Func<T, bool>> expression,
            MySqlQueryComponents<T> commandElements = null,
            MySqlParameterGenerator parameterGenerator = null,
            MySqlExpressionTranslator translator = null) where T : class, new()
        {
            var paramGen = parameterGenerator ?? new MySqlParameterGenerator();
            var trans = translator ?? new MySqlExpressionTranslator(paramGen);

            var tableName = GetTableName<T>();
            var remoteInfo = ResolveRemoteJoins<T>(tableName);

            var visitor = new MySqlWhereClauseVisitor<T>(
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
                    commandElements.SqlParameters = commandElements.SqlParameters ?? new List<MySqlParameter> { };
                    commandElements.SqlParameters.AddRange(visitor.Parameters);
                }
            }
            else
            {
                commandElements = new MySqlQueryComponents<T>(expression, string.Empty, visitor.WhereClauseBody, remoteInfo.JoinClauses, string.Empty, visitor.Parameters);
                commandElements.JoinClausesList = remoteInfo.IndividualJoinClauses;
            }
            return commandElements;
        }

        protected internal MySqlQueryComponents<T> GenerateOrderByClause<T>(Expression<Func<T, bool>> expression,
            MySqlQueryComponents<T> commandElements = null) where T : class, new()
        {
            var visitor = new MySqlOrderByClauseVisitor<T>(
                ColumnNamesCache,
                _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>));
            visitor.Visit(expression);
            if (commandElements == null)
                commandElements = new MySqlQueryComponents<T>(expression, string.Empty, string.Empty, string.Empty, visitor.OrderByClause, new List<MySqlParameter> { });
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

        protected internal T ExecuteReaderSingle<T>(MySqlCommand command) where T : class, new()
        {
            InvokeLogAction(command);
            try
            {
                using (var reader = command.ExecuteReader())
                    return reader.Read() ? MapEntity<T>(reader) : null;
            }
            catch (MySqlException ex) { HandleMySqlException<T>(ex); throw; }
        }

        protected internal ICollection<T> ExecuteReaderList<T>(MySqlCommand command) where T : class, new()
        {
            InvokeLogAction(command);
            var results = new List<T>();
            try
            {
                using (var reader = command.ExecuteReader())
                    while (reader.Read())
                        results.Add(MapEntity<T>(reader));
            }
            catch (MySqlException ex) { HandleMySqlException<T>(ex); throw; }
            return results;
        }

        protected internal MySqlCommand BuildSqlCommandObject(string commandText, IDbConnection connection, ICollection<MySqlParameter> parameters = null, CommandType commandType = CommandType.Text)
        {
            var command = new MySqlCommand(commandText, (MySqlConnection)connection)
            {
                CommandType = commandType,
                Transaction = (MySqlTransaction)Transaction
            };
            if (parameters?.Any() == true)
                command.Parameters.AddRange(parameters.ToArray());
            return command;
        }

        #region Stored Procedure Execution

        /// <summary>Per-type cache of resolved stored procedure names (mirrors the table-name cache).</summary>
        private static readonly ConcurrentDictionary<Type, string> _procedureNames = new ConcurrentDictionary<Type, string>();

        /// <inheritdoc />
        public override ICollection<T> ExecProcedure<T>(object parameters = null)
            => ExecProcedureInternal<T>(ResolveProcedureName<T>(null), NormalizeParameters(parameters));

        /// <inheritdoc />
        public override ICollection<T> ExecProcedure<T>(string procedureName, object parameters = null)
            => ExecProcedureInternal<T>(ResolveProcedureName<T>(procedureName), NormalizeParameters(parameters));

        /// <inheritdoc />
        public override ICollection<T> ExecProcedure<T>(string procedureName, params SqlParam[] parameters)
            => ExecProcedureInternal<T>(ResolveProcedureName<T>(procedureName), NormalizeParameters(parameters));

        /// <inheritdoc />
        public override TResult ExecScalar<TResult>(string procedureName, object parameters = null)
            => ExecScalarInternal<TResult>(procedureName, NormalizeParameters(parameters));

        /// <inheritdoc />
        public override TResult ExecScalar<TResult>(string procedureName, params SqlParam[] parameters)
            => ExecScalarInternal<TResult>(procedureName, NormalizeParameters(parameters));

        /// <inheritdoc />
        public override int ExecNonQuery(string procedureName, object parameters = null)
            => ExecNonQueryInternal(procedureName, NormalizeParameters(parameters));

        /// <inheritdoc />
        public override int ExecNonQuery(string procedureName, params SqlParam[] parameters)
            => ExecNonQueryInternal(procedureName, NormalizeParameters(parameters));

        /// <inheritdoc />
        public override Task<ICollection<T>> ExecProcedureAsync<T>(object parameters = null)
            => ExecProcedureInternalAsync<T>(ResolveProcedureName<T>(null), NormalizeParameters(parameters));

        /// <inheritdoc />
        public override Task<ICollection<T>> ExecProcedureAsync<T>(string procedureName, object parameters = null)
            => ExecProcedureInternalAsync<T>(ResolveProcedureName<T>(procedureName), NormalizeParameters(parameters));

        /// <inheritdoc />
        public override Task<ICollection<T>> ExecProcedureAsync<T>(string procedureName, params SqlParam[] parameters)
            => ExecProcedureInternalAsync<T>(ResolveProcedureName<T>(procedureName), NormalizeParameters(parameters));

        /// <inheritdoc />
        public override Task<TResult> ExecScalarAsync<TResult>(string procedureName, object parameters = null)
            => ExecScalarInternalAsync<TResult>(procedureName, NormalizeParameters(parameters));

        /// <inheritdoc />
        public override Task<TResult> ExecScalarAsync<TResult>(string procedureName, params SqlParam[] parameters)
            => ExecScalarInternalAsync<TResult>(procedureName, NormalizeParameters(parameters));

        /// <inheritdoc />
        public override Task<int> ExecNonQueryAsync(string procedureName, object parameters = null)
            => ExecNonQueryInternalAsync(procedureName, NormalizeParameters(parameters));

        /// <inheritdoc />
        public override Task<int> ExecNonQueryAsync(string procedureName, params SqlParam[] parameters)
            => ExecNonQueryInternalAsync(procedureName, NormalizeParameters(parameters));

        private ICollection<T> ExecProcedureInternal<T>(string procedureName, IReadOnlyList<NormalizedParameter> normalized) where T : class, new()
        {
            using (var scope = new ConnectionScope(this))
            using (var command = BuildSqlCommandObject(procedureName, scope.Connection, BuildProcedureParameters(normalized), CommandType.StoredProcedure))
            {
                var results = ExecuteReaderList<T>(command);
                BackPopulateOutputParameters(command, normalized);
                return results;
            }
        }

        private async Task<ICollection<T>> ExecProcedureInternalAsync<T>(string procedureName, IReadOnlyList<NormalizedParameter> normalized) where T : class, new()
        {
            using (var scope = new ConnectionScope(this))
            using (var command = BuildSqlCommandObject(procedureName, scope.Connection, BuildProcedureParameters(normalized), CommandType.StoredProcedure))
            {
                var results = await ExecuteReaderListAsync<T>(command).ConfigureAwait(false);
                BackPopulateOutputParameters(command, normalized);
                return results;
            }
        }

        private TResult ExecScalarInternal<TResult>(string procedureName, IReadOnlyList<NormalizedParameter> normalized)
        {
            using (var scope = new ConnectionScope(this))
            using (var command = BuildSqlCommandObject(procedureName, scope.Connection, BuildProcedureParameters(normalized), CommandType.StoredProcedure))
            {
                InvokeLogAction(command);
                var raw = command.ExecuteScalar();
                BackPopulateOutputParameters(command, normalized);
                return ConvertScalar<TResult>(raw);
            }
        }

        private async Task<TResult> ExecScalarInternalAsync<TResult>(string procedureName, IReadOnlyList<NormalizedParameter> normalized)
        {
            using (var scope = new ConnectionScope(this))
            using (var command = BuildSqlCommandObject(procedureName, scope.Connection, BuildProcedureParameters(normalized), CommandType.StoredProcedure))
            {
                InvokeLogAction(command);
                var raw = await command.ExecuteScalarAsync().ConfigureAwait(false);
                BackPopulateOutputParameters(command, normalized);
                return ConvertScalar<TResult>(raw);
            }
        }

        private int ExecNonQueryInternal(string procedureName, IReadOnlyList<NormalizedParameter> normalized)
        {
            using (var scope = new ConnectionScope(this))
            using (var command = BuildSqlCommandObject(procedureName, scope.Connection, BuildProcedureParameters(normalized), CommandType.StoredProcedure))
            {
                InvokeLogAction(command);
                var rowsAffected = command.ExecuteNonQuery();
                BackPopulateOutputParameters(command, normalized);
                return rowsAffected;
            }
        }

        private async Task<int> ExecNonQueryInternalAsync(string procedureName, IReadOnlyList<NormalizedParameter> normalized)
        {
            using (var scope = new ConnectionScope(this))
            using (var command = BuildSqlCommandObject(procedureName, scope.Connection, BuildProcedureParameters(normalized), CommandType.StoredProcedure))
            {
                InvokeLogAction(command);
                var rowsAffected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                BackPopulateOutputParameters(command, normalized);
                return rowsAffected;
            }
        }

        /// <summary>
        /// Resolves the stored procedure name for <typeparamref name="T"/>: explicit name wins, then
        /// <c>[Procedure]</c>, then convention inference against information_schema.routines (cached). Catalog
        /// lookup runs on its own scope before the execution scope opens, so it never nests inside a transaction.
        /// </summary>
        private string ResolveProcedureName<T>(string procedureName)
        {
            if (!string.IsNullOrEmpty(procedureName)) return procedureName;
            return _procedureNames.GetOrAdd(typeof(T), t =>
            {
                var attributeName = GetProcedureNameFromAttribute<T>();
                if (!string.IsNullOrEmpty(attributeName)) return attributeName;
                var normalized = NormalizeProcedureNameForLookup(t.Name);
                return LookupProcedureName(normalized) ?? t.Name;
            });
        }

        private string LookupProcedureName(string normalized)
        {
            using (var scope = new ConnectionScope(this))
            using (var command = BuildSqlCommandObject(
                       "SELECT routine_name FROM information_schema.routines " +
                       "WHERE routine_schema = DATABASE() AND routine_type = 'PROCEDURE' " +
                       "AND REPLACE(LOWER(routine_name), '_', '') = @normalized",
                       scope.Connection,
                       new[] { new MySqlParameter("@normalized", normalized) }))
            {
                InvokeLogAction(command);
                var result = command.ExecuteScalar();
                return result == null || result is DBNull ? null : result.ToString();
            }
        }

        /// <summary>Converts normalized parameters to native <see cref="MySqlParameter"/>s (re-adding the <c>@</c> prefix).</summary>
        private static ICollection<MySqlParameter> BuildProcedureParameters(IReadOnlyList<NormalizedParameter> normalized)
        {
            var list = new List<MySqlParameter>(normalized.Count);
            foreach (var np in normalized)
            {
                var parameter = new MySqlParameter("@" + np.Name, np.Value ?? DBNull.Value)
                {
                    Direction = np.Direction
                };
                if (np.DbType.HasValue) parameter.DbType = np.DbType.Value;
                if (np.Size.HasValue) parameter.Size = np.Size.Value;
                list.Add(parameter);
            }
            return list;
        }

        /// <summary>After execution, copies output/input-output parameter values back onto the source <see cref="SqlParam"/>s.</summary>
        private static void BackPopulateOutputParameters(MySqlCommand command, IReadOnlyList<NormalizedParameter> normalized)
        {
            foreach (var np in normalized)
            {
                if (np.Source == null || np.Direction == ParameterDirection.Input) continue;
                var native = command.Parameters["@" + np.Name];
                np.Source.Value = native.Value is DBNull ? null : native.Value;
            }
        }

        #endregion

        protected void DiscoverColumns<T>()
        {
            if (_mappedTypes.Contains(typeof(T))) return;
            var table = GetTableName<T>();
            // MySQL: Use LIMIT 0 to fetch schema only
            var commandText = $"SELECT * FROM {table} LIMIT 0";
            var properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties().ToArray());

            using (var connectionScope = new ConnectionScope(this))
            {
                using (var command = BuildSqlCommandObject(commandText, connectionScope.Connection, Array.Empty<MySqlParameter>()))
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
                    catch (MySqlException ex) { HandleMySqlException<T>(ex); throw; }
                }
            }
        }

        private static string GetSchemaSignature(MySqlDataReader reader)
        {
            var cols = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
                cols.Add($"{reader.GetName(i)}:{reader.GetFieldType(i)?.FullName}");
            return string.Join("|", cols);
        }

        protected T MapEntity<T>(MySqlDataReader reader) where T : class, new()
        {
            string schemaKey = typeof(T).FullName + "|" + GetSchemaSignature(reader);
            var mapper = (Func<MySqlDataReader, T>)_entityMappers.GetOrAdd(schemaKey, _ => BuildDataReaderMapper<T>(reader));
            return mapper(reader);
        }

        private Func<MySqlDataReader, T> BuildDataReaderMapper<T>(MySqlDataReader reader) where T : class, new()
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
                    // Try removing backtick quotes if present (e.g. `Order` -> Order)
                    if (columnName.StartsWith("`") && columnName.EndsWith("`"))
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
                .Where(p => unmapped.All(up => up.Name != p.Name))
                .Where(p => !IsDatabaseGenerated(p));
            var result = Dialect.BuildInsertCommand(entity, tableName, primaryKey, GetCachedColumnName, GetDefault, properties);
            return new CommandParameters(result.CommandText, result.Parameters.Cast<MySqlParameter>().ToList());
        }

        protected internal object ExecuteInsert<T>(MySqlCommand command, T entity, PropertyInfo primaryKey) where T : class, new()
        {
            InvokeLogAction(command);
            command.ExecuteNonQuery();
            return AssignInsertedKey(command, entity, primaryKey);
        }

        /// <summary>
        /// Assigns the primary key after an INSERT. For identity (int/long) keys, MySQL exposes the
        /// generated value via <see cref="MySqlCommand.LastInsertedId"/> (no RETURNING clause). For
        /// non-identity keys (Guid/string) the caller supplies the value, which is returned as-is.
        /// </summary>
        private object AssignInsertedKey<T>(MySqlCommand command, T entity, PropertyInfo primaryKey) where T : class, new()
        {
            var targetType = Nullable.GetUnderlyingType(primaryKey.PropertyType) ?? primaryKey.PropertyType;
            if (targetType == typeof(int) || targetType == typeof(long))
            {
                var id = command.LastInsertedId;
                var result = Convert.ChangeType(id, targetType);
                primaryKey.SetValue(entity, result);
                return result;
            }
            // Non-identity primary key supplied by the caller.
            return primaryKey.GetValue(entity);
        }

        protected internal CommandParameters BuildUpdateCommand<T>(T entity, T existing, PropertyInfo primaryKey) where T : class, new()
        {
            var tableName = GetTableName<T>();
            var unmapped = _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>);
            var properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties().ToArray())
                .Where(p => unmapped.All(up => up.Name != p.Name))
                .Where(p => !IsDatabaseGenerated(p));
            var result = Dialect.BuildUpdateCommand(entity, existing, tableName, primaryKey, GetCachedColumnName, properties);
            return new CommandParameters(result.CommandText, result.Parameters.Cast<MySqlParameter>().ToList());
        }

        protected internal void ExecuteUpdate(MySqlCommand command)
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

        protected internal MySqlParameter CreateParameter<T>(string name, object value, Type propertyType) where T : class, new()
        {
            var param = new MySqlParameter(name, value ?? DBNull.Value)
            {
                IsNullable = Nullable.GetUnderlyingType(propertyType) != null || propertyType.IsClass
            };
            var mySqlType = MySqlParameterGenerator.GetMySqlDbType(value);
            if (mySqlType.HasValue) param.MySqlDbType = mySqlType.Value;
            return param;
        }

        protected internal void InvokeLogAction(MySqlCommand command)
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
            public CommandParameters(string command, List<MySqlParameter> parameters)
            {
                CommandText = command;
                Parameters = parameters;
            }
            public string CommandText;
            public List<MySqlParameter> Parameters = new List<MySqlParameter>();
        }

        /// <summary>
        /// Helper type that manages a connection scope for a single operation.
        /// When a transaction is active, the scope uses the provider's shared connection (required for
        /// transaction semantics). Otherwise, each scope creates its own dedicated <see cref="MySqlConnection"/>
        /// from the connection string, ensuring concurrent operations do not share a reader/connection.
        /// </summary>
        internal class ConnectionScope : IDisposable
        {
            public ConnectionScope(MySqlOrmDataProvider provider)
            {
                _provider = provider;

                if (provider.Transaction != null)
                {
                    _ownedConnection = null;
                    _isTransactional = true;

                    // Guard: detect concurrent transactional usage which ADO.NET cannot support.
                    if (Interlocked.Increment(ref provider._activeTransactionalScopes) > 1)
                    {
                        Interlocked.Decrement(ref provider._activeTransactionalScopes);
                        throw new InvalidOperationException(
                            "A concurrent operation is already using the transactional connection. " +
                            "Operations within a transaction must be awaited sequentially " +
                            "(e.g., 'await A(); await B();'). Do not use Task.WhenAll or " +
                            "fire-and-forget patterns within a transaction scope. " +
                            "It can also surface from re-entrant (nested) use on a single thread — " +
                            "e.g. invoking a provider operation that opens its own connection from " +
                            "inside another operation on the same transactional provider.");
                    }
                }
                else
                {
                    var conn = new MySqlConnection(provider._connectionString);
                    conn.Open();
                    _ownedConnection = conn;
                    _isTransactional = false;
                }
            }

            public IDbConnection Connection => _ownedConnection ?? _provider.GetConnection();
            protected internal readonly MySqlOrmDataProvider _provider;
            private readonly MySqlConnection _ownedConnection;
            private readonly bool _isTransactional;

            public void Dispose()
            {
                if (_ownedConnection != null)
                {
                    _ownedConnection.Close();
                    _ownedConnection.Dispose();
                }
                else if (_isTransactional)
                {
                    Interlocked.Decrement(ref _provider._activeTransactionalScopes);
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
            // Quote non-numeric keys (Guid, string)
            if (key is string || key is Guid)
                return $" WHERE {pkColumn} = '{key}'";
            return $" WHERE {pkColumn} = {key}";
        }

        internal PropertyInfo GetPrimaryKeyProperty<T>() => typeof(T).GetProperties().FirstOrDefault(p =>
            p.GetCustomAttribute<KeyAttribute>() != null ||
            p.GetCustomAttribute<DatabaseGeneratedAttribute>()?.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity ||
            p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals($"{typeof(T).Name}Id", StringComparison.OrdinalIgnoreCase));

        protected override string GetTableName<T>() => _tableNames.GetOrAdd(typeof(T), ResolveTableName);

        private string GetTableNameByType(Type type) => _tableNames.GetOrAdd(type, ResolveTableName);

        /// <summary>
        /// Resolves the table name for a CLR type. Resolution order:
        /// <list type="number">
        ///   <item>[Table] attribute (on the class itself, a base class, or an interface) — <c>inherit: true</c></item>
        ///   <item>Exact case-insensitive match of the CLR type name to a database table name</item>
        ///   <item>Case- and underscore-ignoring match via <see cref="IgnoreUnderscoreAndCaseStringComparer"/></item>
        /// </list>
        /// </summary>
        private string ResolveTableName(Type t)
        {
            var tableAttribute = t.GetCustomAttribute<TableAttribute>(inherit: true);
            if (tableAttribute != null)
                return Dialect.EncloseIdentifier(tableAttribute.Name);

            var comparer = new IgnoreUnderscoreAndCaseStringComparer();
            string exactMatch = null;
            string fuzzyMatch = null;

            using (var connectionScope = new ConnectionScope(this))
            {
                // MySQL: information_schema.tables; the current database is the "schema".
                var sql = "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE() AND table_type = 'BASE TABLE'";
                using (var cmd = new MySqlCommand(sql, (MySqlConnection)connectionScope.Connection))
                {
                    if (Transaction != null)
                        cmd.Transaction = (MySqlTransaction)Transaction;

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var dbTableName = reader.GetString(0);

                            if (exactMatch == null
                                && string.Equals(dbTableName, t.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                exactMatch = dbTableName;
                            }
                            else if (fuzzyMatch == null
                                     && comparer.Equals(dbTableName, t.Name))
                            {
                                fuzzyMatch = dbTableName;
                            }

                            if (exactMatch != null)
                                break;
                        }
                    }
                }
            }

            var resolved = exactMatch ?? fuzzyMatch ?? t.Name.ToLower();
            return Dialect.EncloseIdentifier(resolved);
        }

        /// <summary>
        /// Overrides the base column name resolution to use the same dictionary key
        /// as DiscoverColumns (ToDictionaryKey) and to apply backtick quoting for
        /// MySQL reserved words.
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
            var provider = new MySqlLinqQueryProvider<T>(this, selectClause);
            return new MySqlQueryable<T>(provider);
        }

        protected internal int ExecuteNonQuery(string sql, params MySqlParameter[] parameters)
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
