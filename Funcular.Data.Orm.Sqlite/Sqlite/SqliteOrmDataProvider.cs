using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Funcular.Data.Orm;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.Interfaces;
using Funcular.Data.Orm.Sqlite.Visitors;
using Microsoft.Data.Sqlite;

namespace Funcular.Data.Orm.Sqlite
{
    /// <summary>
    /// A SQLite specific implementation of an ORM data provider.
    /// Supports file-based databases with any resolvable filename in the connection string.
    /// </summary>
    public partial class SqliteOrmDataProvider : OrmDataProvider, ISqlOrmProvider
    {
        #region Fields

        private readonly string _connectionString;
        private IDbTransaction _transaction;
        private int _activeTransactionalScopes;
        private readonly SqliteStringComparison _stringComparison;

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
        public SqliteStringComparison StringComparison => _stringComparison;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteOrmDataProvider"/> class.
        /// The connection string can reference any resolvable filename (relative or absolute paths,
        /// environment variables, etc.).
        /// </summary>
        /// <param name="connectionString">SQLite connection string (e.g., "Data Source=path/to/db.sqlite").</param>
        /// <param name="connection">Optional pre-existing connection.</param>
        /// <param name="transaction">Optional pre-existing transaction.</param>
        /// <param name="dialect">Optional custom dialect implementation.</param>
        /// <param name="stringComparison">String comparison mode (default: CaseInsensitive to match SQL Server behavior).</param>
        public SqliteOrmDataProvider(string connectionString, IDbConnection connection = null,
            IDbTransaction transaction = null, ISqlDialect dialect = null,
            SqliteStringComparison stringComparison = SqliteStringComparison.CaseInsensitive)
        {
            _connectionString = ResolveConnectionString(connectionString) ?? throw new ArgumentNullException(nameof(connectionString));
            Connection = connection;
            Transaction = transaction;
            Dialect = dialect ?? new SqliteDialect();
            _stringComparison = stringComparison;
        }

        #endregion

        #region Connection String Resolution

        /// <summary>
        /// Resolves the connection string, expanding environment variables in the Data Source path
        /// and resolving relative paths to full paths so that any resolvable filename can be used.
        /// </summary>
        private static string ResolveConnectionString(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return connectionString;

            var builder = new SqliteConnectionStringBuilder(connectionString);
            if (!string.IsNullOrEmpty(builder.DataSource) && builder.DataSource != ":memory:")
            {
                var dataSource = Environment.ExpandEnvironmentVariables(builder.DataSource);
                dataSource = System.IO.Path.GetFullPath(dataSource);
                builder.DataSource = dataSource;
            }
            return builder.ConnectionString;
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
            var param = new SqliteParameter("@id", id);
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

        protected internal async Task<T> ExecuteReaderSingleAsync<T>(SqliteCommand command) where T : class, new()
        {
            InvokeLogAction(command);
            using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
                return await reader.ReadAsync().ConfigureAwait(false) ? MapEntity<T>(reader) : null;
        }

        protected internal async Task<ICollection<T>> ExecuteReaderListAsync<T>(SqliteCommand command) where T : class, new()
        {
            InvokeLogAction(command);
            var results = new List<T>();
            using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
                while (await reader.ReadAsync().ConfigureAwait(false))
                    results.Add(MapEntity<T>(reader));
            return results;
        }

        protected internal async Task<object> ExecuteInsertAsync<T>(SqliteCommand command, T entity, PropertyInfo primaryKey) where T : class, new()
        {
            InvokeLogAction(command);
            var executeScalar = await command.ExecuteScalarAsync().ConfigureAwait(false);
            if (executeScalar != null && executeScalar != DBNull.Value)
            {
                var targetType = Nullable.GetUnderlyingType(primaryKey.PropertyType) ?? primaryKey.PropertyType;
                object result;
                if (targetType == typeof(Guid))
                    result = executeScalar is Guid g ? g : Guid.Parse(executeScalar.ToString());
                else
                    result = Convert.ChangeType(executeScalar, targetType);
                primaryKey.SetValue(entity, result);
                return result;
            }
            throw new InvalidOperationException("Insert failed: No ID returned.");
        }

        protected internal async Task ExecuteUpdateAsync(SqliteCommand command)
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
            var param = new SqliteParameter("@id", id);
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
            Transaction = Connection?.BeginTransaction();
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
                Connection = new SqliteConnection(_connectionString);
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

            foreach (var prop in jsonPathProperties)
            {
                var attr = prop.GetCustomAttribute<JsonPathAttribute>();
                if (attr == null) continue;

                string jsonColumn = $"{tableName}.{Dialect.EncloseIdentifier(attr.ColumnName)}";
                string jsonExpr = Dialect.BuildJsonValueExpression(jsonColumn, attr.Path, attr.SqlType);

                info.PropertyToColumnMap[prop.Name] = jsonExpr;
                extraColumns.Append($", {jsonExpr} AS \"{prop.Name}\"");
            }

            foreach (var prop in sqlExprProperties)
            {
                var attr = prop.GetCustomAttribute<SqlExpressionAttribute>();
                if (attr == null) continue;

                string rawExpr = attr.GetExpression(Dialect.ProviderName);
                string resolvedExpr = ResolveExpressionTokens(rawExpr, typeof(T), tableName, info.PropertyToColumnMap);

                info.PropertyToColumnMap[prop.Name] = resolvedExpr;
                extraColumns.Append($", {resolvedExpr} AS \"{prop.Name}\"");
            }

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
                extraColumns.Append($", {subquery} AS \"{prop.Name}\"");
            }

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
                extraColumns.Append($", {subquery} AS \"{prop.Name}\"");
            }

            info.JoinClauses = joinClauses.ToString();
            info.ExtraColumns = extraColumns.ToString();
            return info;
        }

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

        protected internal SqliteQueryComponents CreateSelectQueryObject<T>(Expression<Func<T, bool>> whereExpression) where T : class, new()
        {
            var selectClause = CreateGetOneOrSelectCommandText<T>();
            var elements = GenerateWhereClause(whereExpression);
            elements.SelectClause = selectClause;
            return elements;
        }

        protected internal SqliteQueryComponents GenerateWhereClause<T>(
            Expression<Func<T, bool>> expression,
            SqliteQueryComponents commandElements = null,
            SqliteParameterGenerator parameterGenerator = null,
            SqliteExpressionTranslator translator = null) where T : class, new()
        {
            var paramGen = parameterGenerator ?? new SqliteParameterGenerator();
            var trans = translator ?? new SqliteExpressionTranslator(paramGen, _stringComparison);

            var tableName = GetTableName<T>();
            var remoteInfo = ResolveRemoteJoins<T>(tableName);

            var visitor = new SqliteWhereClauseVisitor<T>(
                ColumnNamesCache,
                _unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>),
                paramGen, trans, tableName, remoteInfo.PropertyToColumnMap);
            visitor.Visit(expression);

            if (commandElements != null)
            {
                commandElements.WhereClause = visitor.WhereClauseBody;
                commandElements.JoinClause = remoteInfo.JoinClauses;
                if (visitor.Parameters.Any())
                {
                    commandElements.SqlParameters = commandElements.SqlParameters ?? new List<SqliteParameter>();
                    commandElements.SqlParameters.AddRange(visitor.Parameters);
                }
            }
            else
            {
                commandElements = new SqliteQueryComponents
                {
                    SelectClause = string.Empty,
                    WhereClause = visitor.WhereClauseBody,
                    JoinClause = remoteInfo.JoinClauses,
                    SqlParameters = visitor.Parameters
                };
            }
            return commandElements;
        }

        private void ValidateWhereClause<T>(string whereClause)
        {
            if (string.IsNullOrWhiteSpace(whereClause))
                throw new InvalidOperationException("Delete operation requires a non-empty, valid WHERE clause.");
            var trivialPatterns = new[] { "1=1", "1 < 2", "1 > 0", "true", "WHERE 1=1", "WHERE 1 < 2" };
            if (trivialPatterns.Any(p => whereClause.Replace(" ", "").Contains(p.Replace(" ", ""), System.StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Delete operation requires a non-trivial WHERE clause.");
            var regex = new System.Text.RegularExpressions.Regex(@"\b(\w+)\s*=\s*\1\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (regex.IsMatch(whereClause))
                throw new InvalidOperationException("Delete operation WHERE clause cannot be a self-referencing column expression.");
            var tableColumns = typeof(T).GetProperties()
                .Where(p => p.GetCustomAttributes(typeof(NotMappedAttribute), true).Length == 0)
                .Select(p => GetCachedColumnName(p))
                .ToList();
            bool columnReferenced = tableColumns.Any(col => whereClause.IndexOf(col, System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (!columnReferenced)
                throw new InvalidOperationException("Delete operation WHERE clause must reference at least one column from the target table.");
        }

        #endregion

        #region Execution Helpers

        protected internal T ExecuteReaderSingle<T>(SqliteCommand command) where T : class, new()
        {
            InvokeLogAction(command);
            using (var reader = command.ExecuteReader())
                return reader.Read() ? MapEntity<T>(reader) : null;
        }

        protected internal ICollection<T> ExecuteReaderList<T>(SqliteCommand command) where T : class, new()
        {
            InvokeLogAction(command);
            var results = new List<T>();
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                    results.Add(MapEntity<T>(reader));
            return results;
        }

        protected internal SqliteCommand BuildSqlCommandObject(string commandText, IDbConnection connection, ICollection<SqliteParameter> parameters = null)
        {
            var command = new SqliteCommand(commandText, (SqliteConnection)connection)
            {
                CommandType = CommandType.Text,
                Transaction = (SqliteTransaction)Transaction
            };
            if (parameters?.Any() == true)
            {
                foreach (var p in parameters)
                    command.Parameters.Add(p);
            }
            return command;
        }

        protected void DiscoverColumns<T>()
        {
            if (_mappedTypes.Contains(typeof(T))) return;
            var table = GetTableName<T>();
            var commandText = $"SELECT * FROM {table} LIMIT 0";
            var properties = _propertiesCache.GetOrAdd(typeof(T), t => t.GetProperties().ToArray());

            using (var connectionScope = new ConnectionScope(this))
            {
                using (var command = BuildSqlCommandObject(commandText, connectionScope.Connection, Array.Empty<SqliteParameter>()))
                {
                    using (var reader = command.ExecuteReader(CommandBehavior.SchemaOnly))
                    {
                        ICollection<string> columnNamesList = new List<string>();
                        for (int i = 0; i < reader.FieldCount; i++)
                            columnNamesList.Add(reader.GetName(i));

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
            }
        }

        private static string GetSchemaSignature(SqliteDataReader reader)
        {
            var cols = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
                cols.Add($"{reader.GetName(i)}:{reader.GetFieldType(i)?.FullName}");
            return string.Join("|", cols);
        }

        protected T MapEntity<T>(SqliteDataReader reader) where T : class, new()
        {
            string schemaKey = typeof(T).FullName + "|" + GetSchemaSignature(reader);
            var mapper = (Func<SqliteDataReader, T>)_entityMappers.GetOrAdd(schemaKey, _ => BuildDataReaderMapper<T>(reader));
            return mapper(reader);
        }

        private Func<SqliteDataReader, T> BuildDataReaderMapper<T>(SqliteDataReader reader) where T : class, new()
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
                    else if (m.Type == typeof(bool))
                    {
                        // SQLite stores booleans as integers
                        var raw = r.GetValue(m.Ordinal);
                        value = Convert.ToInt64(raw) != 0;
                    }
                    else if (m.Type == typeof(DateTime))
                    {
                        // SQLite stores dates as TEXT
                        var raw = r.GetValue(m.Ordinal);
                        value = raw is DateTime dt ? dt : DateTime.Parse(raw.ToString());
                    }
                    else if (m.Type == typeof(Guid))
                    {
                        var raw = r.GetValue(m.Ordinal);
                        value = raw is Guid g ? g : Guid.Parse(raw.ToString());
                    }
                    else if (m.Type == typeof(decimal))
                    {
                        var raw = r.GetValue(m.Ordinal);
                        value = Convert.ToDecimal(raw);
                    }
                    else if (m.Type == typeof(double))
                    {
                        var raw = r.GetValue(m.Ordinal);
                        value = Convert.ToDouble(raw);
                    }
                    else if (m.Type == typeof(float))
                    {
                        var raw = r.GetValue(m.Ordinal);
                        value = Convert.ToSingle(raw);
                    }
                    else if (m.IsEnum)
                    {
                        var raw = r.GetValue(m.Ordinal);
                        value = Enum.ToObject(m.Type, Convert.ToInt32(raw));
                    }
                    else
                    {
                        value = Convert.ChangeType(r.GetValue(m.Ordinal), m.Type);
                    }
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
            return new CommandParameters(result.CommandText, result.Parameters.Cast<SqliteParameter>().ToList());
        }

        protected internal object ExecuteInsert<T>(SqliteCommand command, T entity, PropertyInfo primaryKey) where T : class, new()
        {
            InvokeLogAction(command);
            var executeScalar = command.ExecuteScalar();
            if (executeScalar != null && executeScalar != DBNull.Value)
            {
                var targetType = Nullable.GetUnderlyingType(primaryKey.PropertyType) ?? primaryKey.PropertyType;
                object result;
                if (targetType == typeof(Guid))
                    result = executeScalar is Guid g ? g : Guid.Parse(executeScalar.ToString());
                else
                    result = Convert.ChangeType(executeScalar, targetType);
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
                .Where(p => unmapped.All(up => up.Name != p.Name))
                .Where(p => !IsDatabaseGenerated(p));
            var result = Dialect.BuildUpdateCommand(entity, existing, tableName, primaryKey, GetCachedColumnName, properties);
            return new CommandParameters(result.CommandText, result.Parameters.Cast<SqliteParameter>().ToList());
        }

        protected internal void ExecuteUpdate(SqliteCommand command)
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

        protected internal void InvokeLogAction(SqliteCommand command)
        {
            Log?.Invoke(command.CommandText);
            foreach (SqliteParameter param in command.Parameters)
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
            public CommandParameters(string command, List<SqliteParameter> parameters)
            {
                CommandText = command;
                Parameters = parameters;
            }
            public string CommandText;
            public List<SqliteParameter> Parameters = new List<SqliteParameter>();
        }

        internal class ConnectionScope : IDisposable
        {
            public ConnectionScope(SqliteOrmDataProvider provider)
            {
                _provider = provider;

                if (provider.Transaction != null)
                {
                    _ownedConnection = null;
                    _isTransactional = true;

                    if (Interlocked.Increment(ref provider._activeTransactionalScopes) > 1)
                    {
                        Interlocked.Decrement(ref provider._activeTransactionalScopes);
                        throw new InvalidOperationException(
                            "A concurrent operation is already using the transactional connection.");
                    }
                }
                else
                {
                    var conn = new SqliteConnection(provider._connectionString);
                    conn.Open();
                    _ownedConnection = conn;
                    _isTransactional = false;
                }
            }

            public IDbConnection Connection => _ownedConnection ?? _provider.GetConnection();
            protected internal readonly SqliteOrmDataProvider _provider;
            private readonly SqliteConnection _ownedConnection;
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
            var remoteUnmapped = properties.Where(p => p.GetCustomAttribute<RemoteAttributeBase>() != null);
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
                .Select(p => Dialect.EncloseIdentifier(GetCachedColumnName(p))));
        }

        protected string GetWhereClause<T>(dynamic key) where T : class
        {
            var pk = GetCachedPrimaryKey<T>();
            var pkColumn = Dialect.EncloseIdentifier(GetCachedColumnName(pk));
            if (key is string || key is Guid)
                return $" WHERE {pkColumn} = '{key}'";
            return $" WHERE {pkColumn} = {key}";
        }

        internal PropertyInfo GetPrimaryKeyProperty<T>() => typeof(T).GetProperties().FirstOrDefault(p =>
            p.GetCustomAttribute<KeyAttribute>() != null ||
            p.GetCustomAttribute<DatabaseGeneratedAttribute>()?.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity ||
            p.Name.Equals("Id", System.StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals($"{typeof(T).Name}Id", System.StringComparison.OrdinalIgnoreCase));

        protected override string GetTableName<T>() => _tableNames.GetOrAdd(typeof(T), ResolveTableName);

        private string GetTableNameByType(Type type) => _tableNames.GetOrAdd(type, ResolveTableName);

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
                var sql = "SELECT name FROM sqlite_master WHERE type='table'";
                using (var cmd = new SqliteCommand(sql, (SqliteConnection)connectionScope.Connection))
                {
                    if (Transaction != null)
                        cmd.Transaction = (SqliteTransaction)Transaction;

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var dbTableName = reader.GetString(0);

                            if (exactMatch == null
                                && string.Equals(dbTableName, t.Name, System.StringComparison.OrdinalIgnoreCase))
                            {
                                exactMatch = dbTableName;
                            }
                            else if (fuzzyMatch == null
                                     && comparer.Equals(dbTableName, t.Name))
                            {
                                fuzzyMatch = dbTableName;
                            }
                        }
                    }
                }
            }

            if (exactMatch != null) return Dialect.EncloseIdentifier(exactMatch);
            if (fuzzyMatch != null) return Dialect.EncloseIdentifier(fuzzyMatch);
            return Dialect.EncloseIdentifier(t.Name.ToLower());
        }

        #endregion

        #region Queryable Creation

        internal IQueryable<T> CreateQueryable<T>(string selectCommand) where T : class, new()
        {
            var linqProvider = new SqliteLinqQueryProvider<T>(this, selectCommand);
            return new Sqlite.SqliteQueryable<T>(linqProvider);
        }

        #endregion
    }
}
