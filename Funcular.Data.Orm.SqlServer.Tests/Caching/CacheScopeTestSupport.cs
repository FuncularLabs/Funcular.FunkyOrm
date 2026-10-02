using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Funcular.Data.Orm.Interfaces;
using Funcular.Data.Orm.MySql;
using Funcular.Data.Orm.PostgreSql;
using Funcular.Data.Orm.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace Funcular.Data.Orm.SqlServer.Tests.Caching
{
    /// <summary>The identifier caches one provider instance reads, through that provider's internal scope accessors.</summary>
    internal sealed class ScopeCaches
    {
        public ConcurrentDictionary<Type, string> Tables { get; init; }
        public ConcurrentDictionary<string, string> Columns { get; init; }
        public ConcurrentDictionary<Type, ICollection<PropertyInfo>> Unmapped { get; init; }
        public ICollection<Type> Mapped { get; init; }
        public ConcurrentDictionary<string, Delegate> Mappers { get; init; }

        /// <summary>SQL Server and MySQL only; null on PostgreSQL and SQLite.</summary>
        public ConcurrentDictionary<Type, string> Procedures { get; init; }
    }

    /// <summary>
    /// Helpers for the provider-scoped cache tests (docs/plans/PROVIDER_SCOPED_CACHES_PLAN.md, §4.1). Fake connection
    /// strings name servers that don't exist; nothing here opens a connection.
    /// </summary>
    internal static class CacheScopeTestSupport
    {
        public const string SqlServerKind = "SqlServer";
        public const string PostgreSqlKind = "PostgreSql";
        public const string MySqlKind = "MySql";
        public const string SqliteKind = "Sqlite";

        public static string Unique() => Guid.NewGuid().ToString("N");

        /// <summary>A path in the temp folder for a SQLite database that a DB-free row must never create.</summary>
        public static string SqliteProbePath() => Path.Combine(Path.GetTempPath(), $"zz_psc_probe_{Unique()}.db");

        /// <summary>
        /// A fake connection string for <paramref name="provider"/>. <paramref name="server"/> is a host name on the
        /// server providers and a database file path on SQLite.
        /// </summary>
        public static string FakeConnectionString(string provider, string server, string database = "zz_psc_db",
            string user = "zz_psc_user", string passwordKeyword = "Password", string password = "zz-psc-pw-1",
            string extra = "")
        {
            switch (provider)
            {
                case SqlServerKind:
                    return $"Server={server};Database={database};User ID={user};{passwordKeyword}={password};Encrypt=False;Connect Timeout=1{extra}";
                case PostgreSqlKind:
                    return $"Host={server};Database={database};Username={user};{passwordKeyword}={password};Timeout=1{extra}";
                case MySqlKind:
                    return $"Server={server};Database={database};User ID={user};{passwordKeyword}={password};Connection Timeout=1{extra}";
                case SqliteKind:
                    return $"Data Source={server};{passwordKeyword}={password}{extra}";
                default:
                    throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
            }
        }

        /// <summary>A fresh fake server (host name, or SQLite file path) for <paramref name="provider"/>.</summary>
        public static string FakeServer(string provider) =>
            provider == SqliteKind ? SqliteProbePath() : $"zz-psc-{Unique()}";

        public static OrmDataProvider Create(string provider, string connectionString, IDbConnection connection = null,
            ISqlDialect dialect = null)
        {
            switch (provider)
            {
                case SqlServerKind: return new SqlServerOrmDataProvider(connectionString, connection, null, dialect);
                case PostgreSqlKind: return new PostgreSqlOrmDataProvider(connectionString, connection, null, dialect);
                case MySqlKind: return new MySqlOrmDataProvider(connectionString, connection, null, dialect);
                case SqliteKind: return new SqliteOrmDataProvider(connectionString, connection, null, dialect);
                default: throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
            }
        }

        /// <summary>An explicit, unopened connection of <paramref name="provider"/>'s type.</summary>
        public static IDbConnection Connection(string provider, string connectionString)
        {
            switch (provider)
            {
                case SqlServerKind: return new SqlConnection(connectionString);
                case PostgreSqlKind: return new Npgsql.NpgsqlConnection(connectionString);
                case MySqlKind: return new MySqlConnector.MySqlConnection(connectionString);
                case SqliteKind: return new SqliteConnection(connectionString);
                default: throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
            }
        }

        /// <summary>The default dialect of <paramref name="provider"/>.</summary>
        public static ISqlDialect DefaultDialect(string provider)
        {
            switch (provider)
            {
                case SqlServerKind: return new SqlServerDialect();
                case PostgreSqlKind: return new PostgreSqlDialect();
                case MySqlKind: return new MySqlDialect();
                case SqliteKind: return new SqliteDialect();
                default: throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
            }
        }

        /// <summary>The caches <paramref name="provider"/> reads, through its internal seam accessors.</summary>
        public static ScopeCaches CachesOf(OrmDataProvider provider)
        {
            switch (provider)
            {
                case SqlServerOrmDataProvider p:
                    return new ScopeCaches
                    {
                        Tables = p.ScopeTableNames, Columns = p.ScopeColumnNames, Unmapped = p.ScopeUnmappedProperties,
                        Mapped = p.ScopeMappedTypes, Mappers = p.ScopeEntityMappers, Procedures = p.ScopeProcedureNames
                    };
                case PostgreSqlOrmDataProvider p:
                    return new ScopeCaches
                    {
                        Tables = p.ScopeTableNames, Columns = p.ScopeColumnNames, Unmapped = p.ScopeUnmappedProperties,
                        Mapped = p.ScopeMappedTypes, Mappers = p.ScopeEntityMappers
                    };
                case MySqlOrmDataProvider p:
                    return new ScopeCaches
                    {
                        Tables = p.ScopeTableNames, Columns = p.ScopeColumnNames, Unmapped = p.ScopeUnmappedProperties,
                        Mapped = p.ScopeMappedTypes, Mappers = p.ScopeEntityMappers, Procedures = p.ScopeProcedureNames
                    };
                case SqliteOrmDataProvider p:
                    return new ScopeCaches
                    {
                        Tables = p.ScopeTableNames, Columns = p.ScopeColumnNames, Unmapped = p.ScopeUnmappedProperties,
                        Mapped = p.ScopeMappedTypes, Mappers = p.ScopeEntityMappers
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(provider), provider?.GetType().FullName, null);
            }
        }

        /// <summary>
        /// Asserts that <paramref name="b"/> reads <paramref name="a"/>'s cache set: a value planted in A's column cache
        /// is seen through B, and every cache is the same object.
        /// </summary>
        public static void AssertSameScope(OrmDataProvider a, OrmDataProvider b, string context)
        {
            var key = $"zz_psc.same.{Unique()}";
            var ca = CachesOf(a);
            var cb = CachesOf(b);
            ca.Columns[key] = "zz_psc_planted";
            try
            {
                Assert.IsTrue(cb.Columns.TryGetValue(key, out var seen) && seen == "zz_psc_planted",
                    $"{context}: a value planted in A's scope is not seen through B (expected one shared scope)");
                Assert.AreSame(ca.Tables, cb.Tables, $"{context}: table-name cache");
                Assert.AreSame(ca.Columns, cb.Columns, $"{context}: column-name cache");
                Assert.AreSame(ca.Unmapped, cb.Unmapped, $"{context}: unmapped-property cache");
                Assert.AreSame(ca.Mapped, cb.Mapped, $"{context}: mapped-type set");
                Assert.AreSame(ca.Mappers, cb.Mappers, $"{context}: entity-mapper cache");
                if (ca.Procedures != null)
                    Assert.AreSame(ca.Procedures, cb.Procedures, $"{context}: procedure-name cache");
            }
            finally
            {
                ca.Columns.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// Asserts that <paramref name="b"/> reads another cache set than <paramref name="a"/>: a value planted in A's
        /// column cache is not seen through B, and no cache is the same object.
        /// </summary>
        public static void AssertOtherScope(OrmDataProvider a, OrmDataProvider b, string context)
        {
            var key = $"zz_psc.other.{Unique()}";
            var ca = CachesOf(a);
            var cb = CachesOf(b);
            ca.Columns[key] = "zz_psc_planted";
            try
            {
                Assert.IsFalse(cb.Columns.ContainsKey(key),
                    $"{context}: planted value seen (a value planted in A's scope is read through B)");
                Assert.AreNotSame(ca.Tables, cb.Tables, $"{context}: table-name cache is shared");
                Assert.AreNotSame(ca.Columns, cb.Columns, $"{context}: column-name cache is shared");
                Assert.AreNotSame(ca.Unmapped, cb.Unmapped, $"{context}: unmapped-property cache is shared");
                Assert.AreNotSame(ca.Mapped, cb.Mapped, $"{context}: mapped-type set is shared");
                Assert.AreNotSame(ca.Mappers, cb.Mappers, $"{context}: entity-mapper cache is shared");
                if (ca.Procedures != null)
                    Assert.AreNotSame(ca.Procedures, cb.Procedures, $"{context}: procedure-name cache is shared");
            }
            finally
            {
                ca.Columns.TryRemove(key, out _);
            }
        }

        /// <summary>Uppercase hexadecimal SHA-256 of the UTF-8 bytes of <paramref name="value"/>.</summary>
        public static string Sha256Hex(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

        public static bool IsSha256Hex(string value) =>
            value != null && value.Length == 64 && value.All(Uri.IsHexDigit);

        public static PropertyInfo Property<T>(string name) =>
            typeof(T).GetProperty(name) ?? throw new ArgumentException($"{typeof(T).Name} has no property {name}.");

        // SQLite temp databases -------------------------------------------------------------------------------------

        /// <summary>Creates a SQLite database file in the temp folder and runs <paramref name="script"/> on it.</summary>
        public static string CreateSqliteDatabase(string script)
        {
            var path = Path.Combine(Path.GetTempPath(), $"zz_psc_{Unique()}.db");
            try
            {
                using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = script;
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch
            {
                DeleteSqliteDatabase(path);
                throw;
            }
            return path;
        }

        /// <summary>Deletes a SQLite database file, releasing pooled handles first.</summary>
        public static void DeleteSqliteDatabase(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            SqliteConnection.ClearAllPools();
            for (var attempt = 0; attempt < 5 && File.Exists(path); attempt++)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException) when (attempt < 4)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    SqliteConnection.ClearAllPools();
                    System.Threading.Thread.Sleep(100);
                }
            }
        }

        // SQL Server test database ----------------------------------------------------------------------------------

        /// <summary>The SQL Server test database, resolved as <see cref="SqlServerTestFixture"/> does.</summary>
        public static string SqlServerTestConnectionString() =>
            Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
            "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=funky_db;Integrated Security=True;";

        /// <summary>Marks the test inconclusive when <paramref name="name"/>'s test database can't be opened.</summary>
        public static void RequireReachable(Func<IDbConnection> create, string name)
        {
            try
            {
                using (var connection = create())
                    connection.Open();
            }
            catch (Exception ex)
            {
                Assert.Inconclusive($"{name} test database is unreachable ({ex.GetType().Name}); this row runs where it is available.");
            }
        }

        public static void ExecuteSqlServer(string connectionString, string sql)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
