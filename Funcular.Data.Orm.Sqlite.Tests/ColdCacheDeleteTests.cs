using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.Sqlite.Tests
{
    /// <summary>
    /// Cold-cache delete matrix (docs/plans/COLD_CACHE_DELETE_PLAN.md §4.1, SQLite column). One temp-file database
    /// per class with PROPERTY-named columns: at fae4472 the SQLite provider's base <c>GetCachedColumnName</c> keyed
    /// on <c>DeclaringType.FullName</c> while discovery wrote <c>ToDictionaryKey()</c> keys (plan §1), so snake_case
    /// rows (AC8 <c>[SqlExpression]</c>, AC9) are not in this column. SQLite has no stored procedures, so the AC6
    /// ExecProcedure-first row is not either. Every test's entity type is used by that test only and nowhere else in
    /// this assembly, and rows/tables are seeded and removed with raw SQL; each test first asserts coldness through
    /// the provider instance under test.
    /// </summary>
    [TestClass]
    public class ColdCacheDeleteTests
    {
        #region Entity types (one per test)

        public abstract class ColdDeleteBase
        {
            public int Id { get; set; }
            public string LastName { get; set; }
        }

        [Table("person")]
        public class ColdDeletePerson : ColdDeleteBase
        {
            public string FirstName { get; set; }
        }

        public abstract class ColdDeleteAsyncBase
        {
            public int Id { get; set; }
            public string LastName { get; set; }
        }

        [Table("person")]
        public class ColdDeleteAsyncPerson : ColdDeleteAsyncBase
        {
            public string FirstName { get; set; }
        }

        public abstract class ColdMethodCallBase
        {
            public int Id { get; set; }
            public string LastName { get; set; }
        }

        [Table("person")]
        public class ColdMethodCallPerson : ColdMethodCallBase
        {
            public string FirstName { get; set; }
        }

        public abstract class ColdWhereBase
        {
            public int Id { get; set; }
            public string LastName { get; set; }
        }

        [Table("person")]
        public class ColdWherePerson : ColdWhereBase
        {
            public string FirstName { get; set; }
        }

        [Table("person")]
        public class ColdHelperPerson
        {
            public int Id { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
        }

        [Table(RetryTable)]
        public class ColdRetryRow
        {
            public int Id { get; set; }
            public string RetryLabel { get; set; } // column: RetryLabel (property-named on SQLite)
        }

        [Table(ExecPkTable)]
        public class ColdExecPkRow
        {
            [Key] public int ZzProbePkId { get; set; } // column: ZzProbePkId (property-named on SQLite)
            public string Label { get; set; }
        }

        [Table(ExecPkTable)]
        public class ColdExecPkAsyncRow
        {
            [Key] public int ZzProbePkId { get; set; } // column: ZzProbePkId (property-named on SQLite)
            public string Label { get; set; }
        }

        [Table(NoTxTable)]
        public class ColdPkNoTxRow
        {
            [Key] public int ZzProbePkId { get; set; } // column: ZzProbePkId (property-named on SQLite)
            public string Label { get; set; }
        }

        [Table(NoTxTable)]
        public class ColdPkNoTxAsyncRow
        {
            [Key] public int ZzProbePkId { get; set; } // column: ZzProbePkId (property-named on SQLite)
            public string Label { get; set; }
        }

        /// <summary>
        /// Test-only derived provider: reaches discovery. The row that uses it tests this instance, and reads its
        /// scope through this instance's own accessors.
        /// </summary>
        private sealed class ProbeProvider : SqliteOrmDataProvider
        {
            public ProbeProvider(string connectionString) : base(connectionString) { }
            public void Discover<T>() where T : class, new() => DiscoverColumns<T>();
        }

        #endregion

        private const string RetryTable = "zz_cold_retry";
        private const string ExecPkTable = "zz_cold_exec_pk";
        private const string NoTxTable = "zz_cold_pk_notx";

        private static string _dbPath;
        private static string _connectionString;

        private string _marker;
        private readonly List<long> _seededPersonIds = new List<long>();

        [ClassInitialize]
        public static void CreateDatabase(TestContext context)
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"funky_cold_delete_{Guid.NewGuid():N}.db");
            _connectionString = $"Data Source={_dbPath}";
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE person (Id INTEGER PRIMARY KEY AUTOINCREMENT, FirstName TEXT NOT NULL, LastName TEXT NOT NULL)";
            command.ExecuteNonQuery();
        }

        [ClassCleanup]
        public static void DeleteDatabase()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (_dbPath != null && File.Exists(_dbPath)) File.Delete(_dbPath);
            }
            catch (IOException)
            {
                // A handle outside the pool is still open; the temp file is left behind.
            }
        }

        [TestInitialize]
        public void Init()
        {
            // No underscore: the method-call row uses it inside LIKE patterns.
            _marker = "cold" + Guid.NewGuid().ToString("N").Substring(0, 12);
            _seededPersonIds.Clear();
        }

        [TestCleanup]
        public void RemoveSeededRows()
        {
            foreach (var id in _seededPersonIds)
                ExecRaw("DELETE FROM person WHERE Id = @id", ("@id", id));
        }

        #region AC1 / AC2 / AC3 — predicate delete on a cold cache

        [TestMethod]
        public void Delete_ByInheritedMember_OnAColdCache_DeletesTheRow_AndTheTypeStaysQueryable()
        {
            using var provider = new SqliteOrmDataProvider(_connectionString);
            AssertCold(provider, typeof(ColdDeletePerson));
            var marker = _marker;
            SeedPerson("gone", marker);
            var keptId = SeedPerson("kept", marker);

            provider.BeginTransaction();
            int deleted;
            try
            {
                deleted = provider.Delete<ColdDeletePerson>(p => p.LastName == marker && p.FirstName == "gone");
                provider.CommitTransaction();
            }
            catch
            {
                provider.RollbackTransaction();
                throw;
            }

            Assert.AreEqual(1, deleted, "the cold delete must remove exactly the matching row");
            var left = provider.Query<ColdDeletePerson>().Where(p => p.LastName == marker).ToList();
            AssertOnlyKept(left, keptId, marker, p => p.Id, p => p.FirstName, p => p.LastName);
        }

        [TestMethod]
        public async Task DeleteAsync_ByInheritedMember_OnAColdCache_DeletesTheRow_AndTheTypeStaysQueryable()
        {
            using var provider = new SqliteOrmDataProvider(_connectionString);
            AssertCold(provider, typeof(ColdDeleteAsyncPerson));
            var marker = _marker;
            SeedPerson("gone", marker);
            var keptId = SeedPerson("kept", marker);

            provider.BeginTransaction();
            int deleted;
            try
            {
                deleted = await provider.DeleteAsync<ColdDeleteAsyncPerson>(p => p.LastName == marker && p.FirstName == "gone");
                provider.CommitTransaction();
            }
            catch
            {
                provider.RollbackTransaction();
                throw;
            }

            Assert.AreEqual(1, deleted, "the cold async delete must remove exactly the matching row");
            var left = provider.Query<ColdDeleteAsyncPerson>().Where(p => p.LastName == marker).ToList();
            AssertOnlyKept(left, keptId, marker, p => p.Id, p => p.FirstName, p => p.LastName);
        }

        /// <summary>
        /// On SQLite the method-call delete itself succeeds cold (the naive name is the property-named column);
        /// the red is the survivor read through <c>Query&lt;T&gt;()</c>, which hits the unmapped set the delete
        /// cached. Kills no single-decision mutant: it fails only without both D1 and D2 (plan §4.2).
        /// </summary>
        [TestMethod]
        public void Delete_ByMethodCallPredicate_OnAColdCache_DeletesTheRow()
        {
            using var provider = new SqliteOrmDataProvider(_connectionString);
            AssertCold(provider, typeof(ColdMethodCallPerson));
            var marker = _marker;
            var goneLast = "g" + marker;
            var keptLast = "k" + marker;
            SeedPerson("gone", goneLast);
            var keptId = SeedPerson("kept", keptLast);

            provider.BeginTransaction();
            int deleted;
            try
            {
                deleted = provider.Delete<ColdMethodCallPerson>(p => p.LastName.StartsWith(goneLast));
                provider.CommitTransaction();
            }
            catch
            {
                provider.RollbackTransaction();
                throw;
            }

            Assert.AreEqual(1, deleted, "the cold method-call delete must remove exactly the matching row");
            var left = provider.Query<ColdMethodCallPerson>().Where(p => p.LastName.EndsWith(marker)).ToList();
            AssertOnlyKept(left, keptId, keptLast, p => p.Id, p => p.FirstName, p => p.LastName);
        }

        #endregion

        #region AC6 — no provider-class path caches an unmapped set before discovery (direct helper row)

        [TestMethod]
        public void UnmappedHelper_UndiscoveredType_IsComputedNotCached_ThenCachedAfterDiscovery()
        {
            using var probe = new ProbeProvider(_connectionString);
            AssertCold(probe, typeof(ColdHelperPerson));

            var computed = probe.UnmappedPropertiesFor<ColdHelperPerson>();
            CollectionAssert.AreEquivalent(new[] { "Id", "FirstName", "LastName" }, computed.Select(p => p.Name).ToList(),
                "for an undiscovered type every convention-mapped property is computed as unmapped");
            Assert.IsFalse(probe.ScopeUnmappedProperties.ContainsKey(typeof(ColdHelperPerson)),
                "D2: the set computed for an undiscovered type must not be cached");

            probe.Discover<ColdHelperPerson>();
            Assert.IsTrue(probe.ScopeMappedTypes.Contains(typeof(ColdHelperPerson)), "discovery marks the type mapped");

            var warm = probe.UnmappedPropertiesFor<ColdHelperPerson>();
            Assert.AreEqual(0, warm.Count, "after discovery no convention-mapped property is unmapped");
            Assert.IsTrue(probe.ScopeUnmappedProperties.TryGetValue(typeof(ColdHelperPerson), out var cached),
                "after discovery the set is cached");
            Assert.AreEqual(0, cached.Count, "the cached set is the post-discovery set");
        }

        #endregion

        #region AC7 — a failed cold discovery reports itself, caches nothing, and the next call works

        [TestMethod]
        public void ColdDelete_WhenDiscoveryFails_ReportsIt_CachesNothing_AndTheNextCallWorks()
        {
            var label = _marker;
            ExecRaw($"DROP TABLE IF EXISTS {RetryTable}");
            try
            {
                using (var provider = new SqliteOrmDataProvider(_connectionString))
                {
                    AssertCold(provider, typeof(ColdRetryRow));
                    provider.BeginTransaction();
                    var failure = Capture(() => provider.Delete<ColdRetryRow>(r => r.RetryLabel == label));
                    provider.RollbackTransaction();

                    AssertDiscoveryError(failure, "attempt 1 (table missing)");
                    AssertCold(provider, typeof(ColdRetryRow), "after the failed attempt");

                    ExecRaw($"CREATE TABLE {RetryTable} (Id INTEGER PRIMARY KEY, RetryLabel TEXT NOT NULL)");
                    ExecRaw($"INSERT INTO {RetryTable} (Id, RetryLabel) VALUES (1, @label)", ("@label", label));

                    provider.BeginTransaction();
                    int deleted;
                    try
                    {
                        deleted = provider.Delete<ColdRetryRow>(r => r.RetryLabel == label);
                        provider.CommitTransaction();
                    }
                    catch
                    {
                        provider.RollbackTransaction();
                        throw;
                    }
                    Assert.AreEqual(1, deleted, "attempt 2 (table now exists) must delete the seeded row");
                }
                Assert.AreEqual(0L, Convert.ToInt64(ScalarRaw($"SELECT COUNT(*) FROM {RetryTable}")));
            }
            finally
            {
                ExecRaw($"DROP TABLE IF EXISTS {RetryTable}");
            }
        }

        #endregion

        #region AC8 — a cold GenerateWhereClause<T> discovers T before translating

        [TestMethod]
        public void GenerateWhereClause_Cold_DiscoversFirst()
        {
            using var provider = new SqliteOrmDataProvider(_connectionString);
            AssertCold(provider, typeof(ColdWherePerson));
            var marker = _marker;

            SqliteQueryComponents components = null;
            var failure = Capture(() => components = provider.GenerateWhereClause<ColdWherePerson>(p => p.LastName == marker));

            Assert.IsNull(failure, $"a cold member-access predicate must translate; threw {Describe(failure)}");
            Assert.IsTrue(provider.ScopeMappedTypes.Contains(typeof(ColdWherePerson)), "GenerateWhereClause<T> must discover T");
            Assert.IsFalse(string.IsNullOrWhiteSpace(components.WhereClause));
        }

        #endregion

        #region D3 on SQLite — behaviour-neutral at fae4472 (base GetCachedColumnName keys on FullName); coverage only

        [TestMethod]
        public async Task DeleteById_Cold_SqliteSyncAndAsync_Execute()
        {
            try
            {
                ExecRaw($"DROP TABLE IF EXISTS {ExecPkTable}");
                ExecRaw($"CREATE TABLE {ExecPkTable} (ZzProbePkId INTEGER PRIMARY KEY, Label TEXT NOT NULL)");
                ExecRaw($"INSERT INTO {ExecPkTable} (ZzProbePkId, Label) VALUES (1, 'gone'), (2, 'gone async'), (3, 'kept')");
                using (var provider = new SqliteOrmDataProvider(_connectionString))
                {
                    AssertCold(provider, typeof(ColdExecPkRow));
                    AssertCold(provider, typeof(ColdExecPkAsyncRow));
                    provider.BeginTransaction();
                    bool deleted, deletedAsync;
                    try
                    {
                        deleted = provider.Delete<ColdExecPkRow>(1L);
                        deletedAsync = await provider.DeleteAsync<ColdExecPkAsyncRow>(2L);
                        provider.CommitTransaction();
                    }
                    catch
                    {
                        provider.RollbackTransaction();
                        throw;
                    }
                    Assert.IsTrue(deleted, "the cold delete by id must remove the row");
                    Assert.IsTrue(deletedAsync, "the cold async delete by id must remove the row");
                }
                CollectionAssert.AreEqual(new long[] { 3 }, KeyTableIds(ExecPkTable), "only the kept row survives");
            }
            finally
            {
                ExecRaw($"DROP TABLE IF EXISTS {ExecPkTable}");
            }
        }

        #endregion

        #region Guards — the transaction guard precedes D3 (green at base; kill "D3 before the guard")

        [TestMethod]
        public void DeleteById_Cold_WithoutTransaction_ThrowsTheGuard_AndDoesNotDiscover()
        {
            try
            {
                CreateNoTxTable(); // exists, so a discovery would cache the key column
                using (var provider = new SqliteOrmDataProvider(_connectionString))
                {
                    AssertCold(provider, typeof(ColdPkNoTxRow));
                    var failure = Capture(() => provider.Delete<ColdPkNoTxRow>(1L));
                    AssertTransactionGuard(failure, "cold delete by id without a transaction");
                    AssertCold(provider, typeof(ColdPkNoTxRow), "after the guard");
                }
                CollectionAssert.AreEqual(new long[] { 1, 2 }, KeyTableIds(NoTxTable), "nothing is deleted");
            }
            finally
            {
                ExecRaw($"DROP TABLE IF EXISTS {NoTxTable}");
            }
        }

        [TestMethod]
        public async Task DeleteByIdAsync_Cold_WithoutTransaction_ThrowsTheGuard_AndDoesNotDiscover()
        {
            try
            {
                CreateNoTxTable(); // exists, so a discovery would cache the key column
                using (var provider = new SqliteOrmDataProvider(_connectionString))
                {
                    AssertCold(provider, typeof(ColdPkNoTxAsyncRow));
                    var failure = await CaptureAsync(() => provider.DeleteAsync<ColdPkNoTxAsyncRow>(1L));
                    AssertTransactionGuard(failure, "cold async delete by id without a transaction");
                    AssertCold(provider, typeof(ColdPkNoTxAsyncRow), "after the guard");
                }
                CollectionAssert.AreEqual(new long[] { 1, 2 }, KeyTableIds(NoTxTable), "nothing is deleted");
            }
            finally
            {
                ExecRaw($"DROP TABLE IF EXISTS {NoTxTable}");
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Coldness, read through <paramref name="provider"/>, the instance under test: no unmapped set for the type in
        /// its scope, no column key (<c>ToDictionaryKey()</c>) for any of the type's properties, and the type is not in
        /// its <c>MappedTypes</c>.
        /// </summary>
        private static void AssertCold(SqliteOrmDataProvider provider, Type type, string stage = "precondition")
        {
            Assert.IsFalse(provider.ScopeUnmappedProperties.ContainsKey(type),
                $"{stage}: {type.Name} already has a cached unmapped set");
            Assert.IsFalse(provider.ScopeMappedTypes.Contains(type), $"{stage}: {type.Name} is already in MappedTypes");
            foreach (var property in type.GetProperties())
            {
                var key = property.ToDictionaryKey();
                Assert.IsFalse(provider.ScopeColumnNames.ContainsKey(key),
                    $"{stage}: column key '{key}' is already cached");
            }
        }

        /// <summary>The discovery error on SQLite: the raw <see cref="SqliteException"/> "no such table".</summary>
        private static void AssertDiscoveryError(Exception failure, string stage)
        {
            Assert.IsInstanceOfType(failure, typeof(SqliteException),
                $"{stage}: expected the discovery error; got {Describe(failure)}");
            StringAssert.Contains(failure.Message, "no such table", $"{stage}: got {Describe(failure)}");
        }

        private static void AssertOnlyKept<T>(IList<T> left, long keptId, string keptLast,
            Func<T, int> id, Func<T, string> first, Func<T, string> last)
        {
            Assert.AreEqual(1, left.Count, "exactly the kept row survives");
            Assert.AreEqual(keptId, (long)id(left[0]), "Id is populated");
            Assert.AreEqual("kept", first(left[0]), "FirstName is populated");
            Assert.AreEqual(keptLast, last(left[0]), "LastName is populated");
        }

        private static Exception Capture(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        private static async Task<Exception> CaptureAsync(Func<Task> action)
        {
            try
            {
                await action();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <summary>The transaction guard: thrown before any discovery or command when no transaction is open.</summary>
        private static void AssertTransactionGuard(Exception failure, string stage)
        {
            Assert.IsInstanceOfType(failure, typeof(InvalidOperationException),
                $"{stage}: expected the transaction guard; got {Describe(failure)}");
            StringAssert.Contains(failure.Message, "Delete operations must be performed within an active transaction",
                $"{stage}: got {Describe(failure)}");
        }

        private static string Describe(Exception ex) => ex == null ? "no exception" : $"{ex.GetType().Name}: {ex.Message}";

        private static void CreateNoTxTable()
        {
            ExecRaw($"DROP TABLE IF EXISTS {NoTxTable}");
            ExecRaw($"CREATE TABLE {NoTxTable} (ZzProbePkId INTEGER PRIMARY KEY, Label TEXT NOT NULL)");
            ExecRaw($"INSERT INTO {NoTxTable} (ZzProbePkId, Label) VALUES (1, 'gone'), (2, 'kept')");
        }

        private static List<long> KeyTableIds(string table)
        {
            var ids = new List<long>();
            using var connection = OpenRaw();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT ZzProbePkId FROM {table} ORDER BY ZzProbePkId";
            using var reader = command.ExecuteReader();
            while (reader.Read()) ids.Add(reader.GetInt64(0));
            return ids;
        }

        private long SeedPerson(string firstName, string lastName)
        {
            using var connection = OpenRaw();
            using (var insert = connection.CreateCommand())
            {
                insert.CommandText = "INSERT INTO person (FirstName, LastName) VALUES (@first, @last)";
                insert.Parameters.AddWithValue("@first", firstName);
                insert.Parameters.AddWithValue("@last", lastName);
                insert.ExecuteNonQuery();
            }
            using var identity = connection.CreateCommand();
            identity.CommandText = "SELECT last_insert_rowid()";
            var id = Convert.ToInt64(identity.ExecuteScalar());
            _seededPersonIds.Add(id);
            return id;
        }

        private static SqliteConnection OpenRaw()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private static void ExecRaw(string sql, params (string Name, object Value)[] parameters)
        {
            using var connection = OpenRaw();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        private static object ScalarRaw(string sql)
        {
            using var connection = OpenRaw();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }

        #endregion
    }
}
