using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Funcular.Data.Orm.Interfaces;
using Funcular.Data.Orm.MySql;
using Funcular.Data.Orm.PostgreSql;
using Funcular.Data.Orm.Sqlite;

namespace Funcular.Data.Orm.SqlServer.Tests.Caching
{
    /// <summary>Reaches a provider's protected cache-scope members (plan §4.1, "How the DB-free rows work").</summary>
    internal interface IScopeProbe
    {
        string Identity { get; }
        ICollection<Type> MappedTypesOfScope { get; }
        ICollection<PropertyInfo> UnmappedOf<T>() where T : class, new();
    }

    internal sealed class SqlServerProbe : SqlServerOrmDataProvider, IScopeProbe
    {
        public SqlServerProbe(string connectionString, IDbConnection connection = null, ISqlDialect dialect = null)
            : base(connectionString, connection, null, dialect) { }

        public string Identity => CacheScopeIdentity;
        public ICollection<Type> MappedTypesOfScope => MappedTypes;
        public ICollection<PropertyInfo> UnmappedOf<T>() where T : class, new() => GetUnmappedProperties<T>(typeof(T));
    }

    internal sealed class PostgreSqlProbe : PostgreSqlOrmDataProvider, IScopeProbe
    {
        public PostgreSqlProbe(string connectionString, IDbConnection connection = null, ISqlDialect dialect = null)
            : base(connectionString, connection, null, dialect) { }

        public string Identity => CacheScopeIdentity;
        public ICollection<Type> MappedTypesOfScope => MappedTypes;
        public ICollection<PropertyInfo> UnmappedOf<T>() where T : class, new() => GetUnmappedProperties<T>(typeof(T));
    }

    internal sealed class MySqlProbe : MySqlOrmDataProvider, IScopeProbe
    {
        public MySqlProbe(string connectionString, IDbConnection connection = null, ISqlDialect dialect = null)
            : base(connectionString, connection, null, dialect) { }

        public string Identity => CacheScopeIdentity;
        public ICollection<Type> MappedTypesOfScope => MappedTypes;
        public ICollection<PropertyInfo> UnmappedOf<T>() where T : class, new() => GetUnmappedProperties<T>(typeof(T));
    }

    internal sealed class SqliteProbe : SqliteOrmDataProvider, IScopeProbe
    {
        public SqliteProbe(string connectionString, IDbConnection connection = null, ISqlDialect dialect = null)
            : base(connectionString, connection, null, dialect) { }

        public string Identity => CacheScopeIdentity;
        public ICollection<Type> MappedTypesOfScope => MappedTypes;
        public ICollection<PropertyInfo> UnmappedOf<T>() where T : class, new() => GetUnmappedProperties<T>(typeof(T));
    }

    internal static class ScopeProbes
    {
        public static OrmDataProvider Create(string provider, string connectionString, IDbConnection connection = null,
            ISqlDialect dialect = null)
        {
            switch (provider)
            {
                case CacheScopeTestSupport.SqlServerKind: return new SqlServerProbe(connectionString, connection, dialect);
                case CacheScopeTestSupport.PostgreSqlKind: return new PostgreSqlProbe(connectionString, connection, dialect);
                case CacheScopeTestSupport.MySqlKind: return new MySqlProbe(connectionString, connection, dialect);
                case CacheScopeTestSupport.SqliteKind: return new SqliteProbe(connectionString, connection, dialect);
                default: throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
            }
        }
    }

    /// <summary>
    /// A direct <see cref="OrmDataProvider"/> subclass that overrides neither identity member, so its scope is the
    /// Core default: one per provider type (D8). No operation is implemented.
    /// </summary>
    internal class DirectProviderA : OrmDataProvider
    {
        public ConcurrentDictionary<Type, string> Tables => TableNameCache;
        public ConcurrentDictionary<string, string> Columns => ColumnNameCache;
        public ConcurrentDictionary<Type, ICollection<PropertyInfo>> Unmapped => UnmappedPropertyCache;
        public ICollection<Type> Mapped => MappedTypes;

        public override T Get<T>(dynamic key = null) => throw new NotSupportedException();
        public override IQueryable<T> Query<T>() => throw new NotSupportedException();
        public override ICollection<T> Query<T>(Expression<Func<T, bool>> expression) => throw new NotSupportedException();
        public override ICollection<T> GetList<T>() => throw new NotSupportedException();
        public override object Insert<T>(T entity) => throw new NotSupportedException();
        public override TKey Insert<T, TKey>(T entity) => throw new NotSupportedException();
        public override T Update<T>(T entity) => throw new NotSupportedException();
        public override Task<T> GetAsync<T>(dynamic key = null) => throw new NotSupportedException();
        public override Task<ICollection<T>> QueryAsync<T>(Expression<Func<T, bool>> expression) => throw new NotSupportedException();
        public override Task<ICollection<T>> GetListAsync<T>() => throw new NotSupportedException();
        public override Task<object> InsertAsync<T>(T entity) => throw new NotSupportedException();
        public override Task<TKey> InsertAsync<T, TKey>(T entity) => throw new NotSupportedException();
        public override Task<T> UpdateAsync<T>(T entity) => throw new NotSupportedException();
        public override Task<int> DeleteAsync<T>(Expression<Func<T, bool>> predicate) => throw new NotSupportedException();
        public override int Delete<T>(Expression<Func<T, bool>> predicate) => throw new NotSupportedException();
        public override bool Delete<T>(long id) => throw new NotSupportedException();
        public override void Dispose() { }
    }

    /// <summary>Another direct <see cref="OrmDataProvider"/> subclass, so another provider type.</summary>
    internal sealed class DirectProviderB : OrmDataProvider
    {
        public ConcurrentDictionary<Type, string> Tables => TableNameCache;
        public ConcurrentDictionary<string, string> Columns => ColumnNameCache;

        public override T Get<T>(dynamic key = null) => throw new NotSupportedException();
        public override IQueryable<T> Query<T>() => throw new NotSupportedException();
        public override ICollection<T> Query<T>(Expression<Func<T, bool>> expression) => throw new NotSupportedException();
        public override ICollection<T> GetList<T>() => throw new NotSupportedException();
        public override object Insert<T>(T entity) => throw new NotSupportedException();
        public override TKey Insert<T, TKey>(T entity) => throw new NotSupportedException();
        public override T Update<T>(T entity) => throw new NotSupportedException();
        public override Task<T> GetAsync<T>(dynamic key = null) => throw new NotSupportedException();
        public override Task<ICollection<T>> QueryAsync<T>(Expression<Func<T, bool>> expression) => throw new NotSupportedException();
        public override Task<ICollection<T>> GetListAsync<T>() => throw new NotSupportedException();
        public override Task<object> InsertAsync<T>(T entity) => throw new NotSupportedException();
        public override Task<TKey> InsertAsync<T, TKey>(T entity) => throw new NotSupportedException();
        public override Task<T> UpdateAsync<T>(T entity) => throw new NotSupportedException();
        public override Task<int> DeleteAsync<T>(Expression<Func<T, bool>> predicate) => throw new NotSupportedException();
        public override int Delete<T>(Expression<Func<T, bool>> predicate) => throw new NotSupportedException();
        public override bool Delete<T>(long id) => throw new NotSupportedException();
        public override void Dispose() { }
    }

    /// <summary>A probe subclass of a direct <see cref="OrmDataProvider"/> subclass, reaching Core's own
    /// <c>GetUnmappedProperties&lt;T&gt;()</c>.</summary>
    internal sealed class CoreUnmappedProbe : DirectProviderA
    {
        public ICollection<PropertyInfo> CoreUnmappedOf<T>() => GetUnmappedProperties<T>();
    }

    /// <summary>
    /// A dialect that encloses every identifier in one quote pair and otherwise behaves as <paramref name="inner"/>:
    /// a different dialect type for the same provider type (AC5).
    /// </summary>
    internal sealed class AlwaysQuoteDialect : ISqlDialect
    {
        private readonly ISqlDialect _inner;
        private readonly string _open;
        private readonly string _close;

        public AlwaysQuoteDialect(ISqlDialect inner, string open, string close)
        {
            _inner = inner;
            _open = open;
            _close = close;
        }

        public string EncloseIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return identifier;
            if (identifier.StartsWith(_open) && identifier.EndsWith(_close)) return identifier;
            var bare = identifier.Trim('[', ']', '"', '`');
            return _open + bare + _close;
        }

        public bool IsReservedWord(string word) => _inner.IsReservedWord(word);

        public (string CommandText, IEnumerable<IDbDataParameter> Parameters) BuildInsertCommand<T>(T entity, string tableName,
            PropertyInfo primaryKey, Func<PropertyInfo, string> getColumnName, Func<Type, object> getDefaultValue,
            IEnumerable<PropertyInfo> properties) where T : class =>
            _inner.BuildInsertCommand(entity, tableName, primaryKey, getColumnName, getDefaultValue, properties);

        public (string CommandText, IEnumerable<IDbDataParameter> Parameters) BuildUpdateCommand<T>(T entity, T existing,
            string tableName, PropertyInfo primaryKey, Func<PropertyInfo, string> getColumnName,
            IEnumerable<PropertyInfo> properties) where T : class =>
            _inner.BuildUpdateCommand(entity, existing, tableName, primaryKey, getColumnName, properties);

        public string BuildDeleteCommand(string tableName, string whereClause) => _inner.BuildDeleteCommand(tableName, whereClause);

        public string BuildSelectCommand(string tableName, string columnNames, string whereClause, string joinClauses = null) =>
            _inner.BuildSelectCommand(tableName, columnNames, whereClause, joinClauses);

        public string BuildJsonValueExpression(string qualifiedColumn, string jsonPath, string castType = null) =>
            _inner.BuildJsonValueExpression(qualifiedColumn, jsonPath, castType);

        public string ProviderName => _inner.ProviderName;

        public string BuildScalarSubquery(string childTableName, string childFkColumn, string parentPkExpression,
            Funcular.Data.Orm.Attributes.AggregateFunction function, string aggregateColumn = null,
            string conditionColumn = null, string conditionValue = null) =>
            _inner.BuildScalarSubquery(childTableName, childFkColumn, parentPkExpression, function, aggregateColumn,
                conditionColumn, conditionValue);

        public string BuildJsonCollectionSubquery(string childTableName, string childFkColumn, string parentPkExpression,
            IList<string> columnExpressions, string orderByColumn = null) =>
            _inner.BuildJsonCollectionSubquery(childTableName, childFkColumn, parentPkExpression, columnExpressions,
                orderByColumn);
    }
}
