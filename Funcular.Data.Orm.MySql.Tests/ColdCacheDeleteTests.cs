using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Funcular.Data.Orm.Attributes;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>
    /// Cold-cache delete matrix (docs/plans/COLD_CACHE_DELETE_PLAN.md §4.1, MySQL column). Every test's entity
    /// type is used by that test only and nowhere else in this assembly, and rows/tables are seeded and removed
    /// with raw SQL, so nothing warms the type's cache entries before the operation under test. Each test first
    /// asserts that coldness (no unmapped set, no column keys, not in <c>_mappedTypes</c>).
    /// </summary>
    [TestClass]
    public class ColdCacheDeleteTests : MySqlTestFixture
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
        public class ColdProcPerson
        {
            public int Id { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
        }

        [Table("person")]
        public class ColdHelperPerson
        {
            public int Id { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
        }

        [Table("person")]
        public class ColdComputedPerson
        {
            public int Id { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }

            [SqlExpression("COALESCE({LastName}, '')")]
            public string SafeLastName { get; set; }
        }

        [Table(RetryTable)]
        public class ColdRetryRow
        {
            public int Id { get; set; }
            public string RetryLabel { get; set; } // column: retry_label
        }

        [Table(PkTable)]
        public class ColdPkRow
        {
            [Key] public int ZzProbePkId { get; set; } // column: zz_probe_pk_id
            public string Label { get; set; }
        }

        [Table(PkTable)]
        public class ColdPkAsyncRow
        {
            [Key] public int ZzProbePkId { get; set; } // column: zz_probe_pk_id
            public string Label { get; set; }
        }

        [Table(MissingTable)] // never created
        public class ColdMissingPkRow
        {
            [Key] public int ZzProbePkId { get; set; }
            public string Label { get; set; }
        }

        [Table(MissingTable)] // never created
        public class ColdMissingPkAsyncRow
        {
            [Key] public int ZzProbePkId { get; set; }
            public string Label { get; set; }
        }

        [Table(MissingTable)] // never created
        public class ColdMissingPredicateRow
        {
            public int Id { get; set; }
            public string LastName { get; set; }
        }

        [Table(NoTxTable)]
        public class ColdPkNoTxRow
        {
            [Key] public int ZzProbePkId { get; set; } // column: zz_probe_pk_id
            public string Label { get; set; }
        }

        [Table(NoTxTable)]
        public class ColdPkNoTxAsyncRow
        {
            [Key] public int ZzProbePkId { get; set; } // column: zz_probe_pk_id
            public string Label { get; set; }
        }

        /// <summary>Test-only derived provider: reaches the <c>protected static</c> <c>_mappedTypes</c> and discovery.</summary>
        private sealed class ProbeProvider : MySqlOrmDataProvider
        {
            public ProbeProvider(string connectionString) : base(connectionString) { }
            public static bool IsMapped(Type type) => _mappedTypes.Contains(type);
            public void Discover<T>() where T : class, new() => DiscoverColumns<T>();
        }

        #endregion

        private const string RetryTable = "zz_cold_retry";
        private const string PkTable = "zz_cold_pk";
        private const string MissingTable = "zz_cold_missing";
        private const string NoTxTable = "zz_cold_pk_notx";

        private string _marker;
        private readonly List<int> _seededPersonIds = new List<int>();

        [TestInitialize]
        public void Init()
        {
            InitProvider();
            // No underscore: the method-call row uses it inside LIKE patterns.
            _marker = "cold" + Guid.NewGuid().ToString("N").Substring(0, 12);
            _seededPersonIds.Clear();
        }

        [TestCleanup]
        public void RemoveSeededRows()
        {
            try
            {
                foreach (var id in _seededPersonIds)
                    ExecRaw("DELETE FROM person WHERE id = @id", ("@id", id));
            }
            finally
            {
                DisposeProvider();
            }
        }

        #region AC1 / AC2 / AC3 — predicate delete on a cold cache

        [TestMethod]
        public void Delete_ByInheritedMember_OnAColdCache_DeletesTheRow_AndTheTypeStaysQueryable()
        {
            AssertCold(typeof(ColdDeletePerson));
            var marker = _marker;
            SeedPerson("gone", marker);
            var keptId = SeedPerson("kept", marker);

            using var provider = new MySqlOrmDataProvider(_connectionString);
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
            AssertCold(typeof(ColdDeleteAsyncPerson));
            var marker = _marker;
            SeedPerson("gone", marker);
            var keptId = SeedPerson("kept", marker);

            using var provider = new MySqlOrmDataProvider(_connectionString);
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

        [TestMethod]
        public void Delete_ByMethodCallPredicate_OnAColdCache_DeletesTheRow()
        {
            AssertCold(typeof(ColdMethodCallPerson));
            var marker = _marker;
            var goneLast = "g" + marker;
            var keptLast = "k" + marker;
            SeedPerson("gone", goneLast);
            var keptId = SeedPerson("kept", keptLast);

            using var provider = new MySqlOrmDataProvider(_connectionString);
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

        #region AC6 — no provider-class path caches an unmapped set before discovery

        [TestMethod]
        public void ExecProcedureFirst_ThenQueryAndDelete_Work()
        {
            AssertCold(typeof(ColdProcPerson));
            var marker = _marker;
            var goneId = SeedPerson("gone", marker);
            var keptId = SeedPerson("kept", marker);

            using var provider = new MySqlOrmDataProvider(_connectionString);

            var fromProc = provider.ExecProcedure<ColdProcPerson>("sp_get_person_by_id", new { p_person_id = goneId });
            Assert.AreEqual(1, fromProc.Count, "the procedure returns the seeded row");
            var procRow = fromProc.Single();
            Assert.AreEqual(goneId, procRow.Id);
            Assert.AreEqual("gone", procRow.FirstName);
            Assert.AreEqual(marker, procRow.LastName);

            var both = provider.Query<ColdProcPerson>().Where(p => p.LastName == marker).ToList();
            Assert.AreEqual(2, both.Count, "Query<T>() after an ExecProcedure-first use must still read the type");
            Assert.IsTrue(both.All(p => p.Id > 0 && !string.IsNullOrEmpty(p.FirstName) && p.LastName == marker),
                "Query<T>() must populate Id, FirstName and LastName");

            provider.BeginTransaction();
            int deleted;
            try
            {
                deleted = provider.Delete<ColdProcPerson>(p => p.LastName == marker && p.FirstName == "gone");
                provider.CommitTransaction();
            }
            catch
            {
                provider.RollbackTransaction();
                throw;
            }

            Assert.AreEqual(1, deleted);
            var left = provider.Query<ColdProcPerson>().Where(p => p.LastName == marker).ToList();
            AssertOnlyKept(left, keptId, marker, p => p.Id, p => p.FirstName, p => p.LastName);
        }

        [TestMethod]
        public void UnmappedHelper_UndiscoveredType_IsComputedNotCached_ThenCachedAfterDiscovery()
        {
            AssertCold(typeof(ColdHelperPerson));
            using var probe = new ProbeProvider(_connectionString);

            var computed = probe.UnmappedPropertiesFor<ColdHelperPerson>();
            CollectionAssert.AreEquivalent(new[] { "Id", "FirstName", "LastName" }, computed.Select(p => p.Name).ToList(),
                "for an undiscovered type every convention-mapped property is computed as unmapped");
            Assert.IsFalse(MySqlOrmDataProvider.UnmappedPropertiesCache.ContainsKey(typeof(ColdHelperPerson)),
                "D2: the set computed for an undiscovered type must not be cached");

            probe.Discover<ColdHelperPerson>();
            Assert.IsTrue(ProbeProvider.IsMapped(typeof(ColdHelperPerson)), "discovery marks the type mapped");

            var warm = probe.UnmappedPropertiesFor<ColdHelperPerson>();
            Assert.AreEqual(0, warm.Count, "after discovery no convention-mapped property is unmapped");
            Assert.IsTrue(MySqlOrmDataProvider.UnmappedPropertiesCache.TryGetValue(typeof(ColdHelperPerson), out var cached),
                "after discovery the set is cached");
            Assert.AreEqual(0, cached.Count, "the cached set is the post-discovery set");
        }

        #endregion

        #region AC7 — a failed cold discovery reports itself, caches nothing, and the next call works

        [TestMethod]
        public void ColdDelete_WhenDiscoveryFails_ReportsIt_CachesNothing_AndTheNextCallWorks()
        {
            AssertCold(typeof(ColdRetryRow));
            var label = _marker;
            ExecRaw($"DROP TABLE IF EXISTS {RetryTable}");
            try
            {
                using (var provider = new MySqlOrmDataProvider(_connectionString))
                {
                    provider.BeginTransaction();
                    var failure = Capture(() => provider.Delete<ColdRetryRow>(r => r.RetryLabel == label));
                    provider.RollbackTransaction();

                    AssertDiscoveryError(failure, "attempt 1 (table missing)");
                    AssertCold(typeof(ColdRetryRow), "after the failed attempt");

                    ExecRaw($"CREATE TABLE {RetryTable} (id INT PRIMARY KEY, retry_label VARCHAR(64) NOT NULL)");
                    ExecRaw($"INSERT INTO {RetryTable} (id, retry_label) VALUES (1, @label)", ("@label", label));

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
            AssertCold(typeof(ColdWherePerson));
            var marker = _marker;
            using var provider = new MySqlOrmDataProvider(_connectionString);

            MySqlQueryComponents<ColdWherePerson> components = null;
            var failure = Capture(() => components = provider.GenerateWhereClause<ColdWherePerson>(p => p.LastName == marker));

            Assert.IsNull(failure, $"a cold member-access predicate must translate; threw {Describe(failure)}");
            Assert.IsTrue(ProbeProvider.IsMapped(typeof(ColdWherePerson)), "GenerateWhereClause<T> must discover T");
            Assert.IsFalse(string.IsNullOrWhiteSpace(components.WhereClause));
        }

        [TestMethod]
        public void GenerateWhereClause_Cold_RendersSnakeCaseColumns()
        {
            AssertCold(typeof(ColdComputedPerson));
            var marker = _marker;
            using var provider = new MySqlOrmDataProvider(_connectionString);

            MySqlQueryComponents<ColdComputedPerson> components = null;
            var failure = Capture(() => components = provider.GenerateWhereClause<ColdComputedPerson>(
                p => p.SafeLastName == marker && p.FirstName == "kept"));

            Assert.IsNull(failure, $"a cold predicate must translate; threw {Describe(failure)}");
            var where = components.WhereClause;
            StringAssert.Contains(where, "first_name", $"T's own snake_case column; WHERE: {where}");
            Assert.IsTrue(Regex.IsMatch(where, @"COALESCE\([^)]*last_name", RegexOptions.IgnoreCase),
                $"the [SqlExpression] token must resolve to the discovered column; WHERE: {where}");
            Assert.IsFalse(Regex.IsMatch(where, @"\b(lastname|firstname)\b", RegexOptions.IgnoreCase),
                $"no naive column name may be rendered; WHERE: {where}");
        }

        #endregion

        #region AC9 — cold delete by id discovers first (D3)

        [TestMethod]
        public void DeleteById_Cold_WithASnakeCaseKey_DeletesTheRow()
        {
            AssertCold(typeof(ColdPkRow));
            try
            {
                CreateKeyTable(PkTable);
                using (var provider = new MySqlOrmDataProvider(_connectionString))
                {
                    provider.BeginTransaction();
                    bool deleted;
                    try
                    {
                        deleted = provider.Delete<ColdPkRow>(1L);
                        provider.CommitTransaction();
                    }
                    catch
                    {
                        provider.RollbackTransaction();
                        throw;
                    }
                    Assert.IsTrue(deleted, "the cold delete by id must remove the row");
                }
                CollectionAssert.AreEqual(new[] { 2 }, KeyTableIds(PkTable), "only the kept row survives");
            }
            finally
            {
                ExecRaw($"DROP TABLE IF EXISTS {PkTable}");
            }
        }

        [TestMethod]
        public async Task DeleteByIdAsync_Cold_WithASnakeCaseKey_DeletesTheRow()
        {
            AssertCold(typeof(ColdPkAsyncRow));
            try
            {
                CreateKeyTable(PkTable);
                using (var provider = new MySqlOrmDataProvider(_connectionString))
                {
                    provider.BeginTransaction();
                    bool deleted;
                    try
                    {
                        deleted = await provider.DeleteAsync<ColdPkAsyncRow>(1L);
                        provider.CommitTransaction();
                    }
                    catch
                    {
                        provider.RollbackTransaction();
                        throw;
                    }
                    Assert.IsTrue(deleted, "the cold async delete by id must remove the row");
                }
                CollectionAssert.AreEqual(new[] { 2 }, KeyTableIds(PkTable), "only the kept row survives");
            }
            finally
            {
                ExecRaw($"DROP TABLE IF EXISTS {PkTable}");
            }
        }

        [TestMethod]
        public void DeleteById_Cold_MissingTable_ThrowsTheDiscoveryError()
        {
            AssertCold(typeof(ColdMissingPkRow));
            try
            {
                ExecRaw($"DROP TABLE IF EXISTS {MissingTable}");
                using (var provider = new MySqlOrmDataProvider(_connectionString))
                {
                    provider.BeginTransaction();
                    var failure = Capture(() => provider.Delete<ColdMissingPkRow>(1L));
                    provider.RollbackTransaction();
                    AssertDiscoveryError(failure, "cold delete by id on a missing table");
                    AssertCold(typeof(ColdMissingPkRow), "after the failed discovery");
                }
            }
            finally
            {
                ExecRaw($"DROP TABLE IF EXISTS {MissingTable}");
            }
        }

        [TestMethod]
        public async Task DeleteByIdAsync_Cold_MissingTable_ThrowsTheDiscoveryError()
        {
            AssertCold(typeof(ColdMissingPkAsyncRow));
            try
            {
                ExecRaw($"DROP TABLE IF EXISTS {MissingTable}");
                using (var provider = new MySqlOrmDataProvider(_connectionString))
                {
                    provider.BeginTransaction();
                    var failure = await CaptureAsync(() => provider.DeleteAsync<ColdMissingPkAsyncRow>(1L));
                    provider.RollbackTransaction();
                    AssertDiscoveryError(failure, "cold async delete by id on a missing table");
                    AssertCold(typeof(ColdMissingPkAsyncRow), "after the failed discovery");
                }
            }
            finally
            {
                ExecRaw($"DROP TABLE IF EXISTS {MissingTable}");
            }
        }

        [TestMethod]
        public void DeletePredicate_Cold_MethodCallOnMissingTable_ThrowsTheDiscoveryError()
        {
            // Before D1, a method call on a member reached the database, and the provider's exception was thrown
            // directly. D1 discovers first, so the error has discovery's shape (review HR1-1).
            AssertCold(typeof(ColdMissingPredicateRow));
            try
            {
                ExecRaw($"DROP TABLE IF EXISTS {MissingTable}");
                using (var provider = new MySqlOrmDataProvider(_connectionString))
                {
                    provider.BeginTransaction();
                    var failure = Capture(() => provider.Delete<ColdMissingPredicateRow>(p => p.LastName.StartsWith("x")));
                    provider.RollbackTransaction();
                    AssertDiscoveryError(failure, "cold predicate delete (method call) on a missing table");
                    AssertCold(typeof(ColdMissingPredicateRow), "after the failed discovery");
                }
            }
            finally
            {
                ExecRaw($"DROP TABLE IF EXISTS {MissingTable}");
            }
        }

        #endregion

        #region Guards — the transaction guard precedes D3 (green at base; kill "D3 before the guard")

        [TestMethod]
        public void DeleteById_Cold_WithoutTransaction_ThrowsTheGuard_AndDoesNotDiscover()
        {
            AssertCold(typeof(ColdPkNoTxRow));
            try
            {
                CreateKeyTable(NoTxTable); // exists with a snake_case key, so a discovery would cache a column
                using (var provider = new MySqlOrmDataProvider(_connectionString))
                {
                    var failure = Capture(() => provider.Delete<ColdPkNoTxRow>(1L));
                    AssertTransactionGuard(failure, "cold delete by id without a transaction");
                    AssertCold(typeof(ColdPkNoTxRow), "after the guard");
                }
                CollectionAssert.AreEqual(new[] { 1, 2 }, KeyTableIds(NoTxTable), "nothing is deleted");
            }
            finally
            {
                ExecRaw($"DROP TABLE IF EXISTS {NoTxTable}");
            }
        }

        [TestMethod]
        public async Task DeleteByIdAsync_Cold_WithoutTransaction_ThrowsTheGuard_AndDoesNotDiscover()
        {
            AssertCold(typeof(ColdPkNoTxAsyncRow));
            try
            {
                CreateKeyTable(NoTxTable); // exists with a snake_case key, so a discovery would cache a column
                using (var provider = new MySqlOrmDataProvider(_connectionString))
                {
                    var failure = await CaptureAsync(() => provider.DeleteAsync<ColdPkNoTxAsyncRow>(1L));
                    AssertTransactionGuard(failure, "cold async delete by id without a transaction");
                    AssertCold(typeof(ColdPkNoTxAsyncRow), "after the guard");
                }
                CollectionAssert.AreEqual(new[] { 1, 2 }, KeyTableIds(NoTxTable), "nothing is deleted");
            }
            finally
            {
                ExecRaw($"DROP TABLE IF EXISTS {NoTxTable}");
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Coldness: no unmapped set for the type, no column key for any of its properties (keys are
        /// <c>DeclaringType.Name + "." + Name</c>, i.e. <c>ToDictionaryKey()</c>; the FullName form is checked too),
        /// and the type is not in <c>_mappedTypes</c>.
        /// </summary>
        private static void AssertCold(Type type, string stage = "precondition")
        {
            Assert.IsFalse(MySqlOrmDataProvider.UnmappedPropertiesCache.ContainsKey(type),
                $"{stage}: {type.Name} already has a cached unmapped set");
            Assert.IsFalse(ProbeProvider.IsMapped(type), $"{stage}: {type.Name} is already in _mappedTypes");
            foreach (var property in type.GetProperties())
            {
                var key = property.DeclaringType.Name + "." + property.Name;
                var fullNameKey = property.DeclaringType.FullName + "." + property.Name;
                Assert.IsFalse(MySqlOrmDataProvider.ColumnNamesCache.ContainsKey(key),
                    $"{stage}: column key '{key}' is already cached");
                Assert.IsFalse(MySqlOrmDataProvider.ColumnNamesCache.ContainsKey(fullNameKey),
                    $"{stage}: column key '{fullNameKey}' is already cached");
            }
        }

        /// <summary>The discovery error: <see cref="InvalidOperationException"/> wrapping MySQL error 1146 (no such table).</summary>
        private static void AssertDiscoveryError(Exception failure, string stage)
        {
            Assert.IsInstanceOfType(failure, typeof(InvalidOperationException),
                $"{stage}: expected the discovery error; got {Describe(failure)}");
            var inner = failure.InnerException as MySqlException;
            Assert.IsNotNull(inner, $"{stage}: the discovery error must carry the MySqlException; inner was {Describe(failure.InnerException)}");
            Assert.AreEqual(1146, inner.Number, $"{stage}: expected ER_NO_SUCH_TABLE; got {inner.Number}: {inner.Message}");
        }

        private static void AssertOnlyKept<T>(IList<T> left, int keptId, string keptLast,
            Func<T, int> id, Func<T, string> first, Func<T, string> last)
        {
            Assert.AreEqual(1, left.Count, "exactly the kept row survives");
            Assert.AreEqual(keptId, id(left[0]), "Id is populated");
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

        /// <summary>Drops and recreates <paramref name="table"/> with a snake_case key and rows 1 ('gone') and 2 ('kept').</summary>
        private void CreateKeyTable(string table)
        {
            ExecRaw($"DROP TABLE IF EXISTS {table}");
            ExecRaw($"CREATE TABLE {table} (zz_probe_pk_id INT PRIMARY KEY, label VARCHAR(64) NOT NULL)");
            ExecRaw($"INSERT INTO {table} (zz_probe_pk_id, label) VALUES (1, 'gone'), (2, 'kept')");
        }

        private List<int> KeyTableIds(string table)
        {
            var ids = new List<int>();
            using var connection = OpenRaw();
            using var command = new MySqlCommand($"SELECT zz_probe_pk_id FROM {table} ORDER BY zz_probe_pk_id", connection);
            using var reader = command.ExecuteReader();
            while (reader.Read()) ids.Add(reader.GetInt32(0));
            return ids;
        }

        private int SeedPerson(string firstName, string lastName)
        {
            using var connection = OpenRaw();
            using var command = new MySqlCommand(
                "INSERT INTO person (first_name, last_name) VALUES (@first, @last); SELECT LAST_INSERT_ID();", connection);
            command.Parameters.AddWithValue("@first", firstName);
            command.Parameters.AddWithValue("@last", lastName);
            var id = Convert.ToInt32(command.ExecuteScalar());
            _seededPersonIds.Add(id);
            return id;
        }

        /// <summary>A raw connection with 10 s lock-wait timeouts, so DDL never hangs behind a leftover lock.</summary>
        private MySqlConnection OpenRaw()
        {
            var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var command = new MySqlCommand(
                "SET SESSION lock_wait_timeout = 10; SET SESSION innodb_lock_wait_timeout = 10;", connection);
            command.ExecuteNonQuery();
            return connection;
        }

        private void ExecRaw(string sql, params (string Name, object Value)[] parameters)
        {
            using var connection = OpenRaw();
            using var command = new MySqlCommand(sql, connection);
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }

        private object ScalarRaw(string sql)
        {
            using var connection = OpenRaw();
            using var command = new MySqlCommand(sql, connection);
            return command.ExecuteScalar();
        }

        #endregion
    }
}
