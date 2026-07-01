using System;
using System.IO;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.Sqlite.Tests
{
    /// <summary>
    /// Regression test for the 3.8.3 transaction-nesting hazard: cold <see cref="RemotePropertyAttribute"/> target
    /// discovery runs inside command building (which, on the eager/CRUD paths, runs inside the operation's own
    /// ConnectionScope). Discovering the remote target used to open a nested ConnectionScope; under an open
    /// transaction that tripped the transactional-scope guard. The fix borrows the ambient transactional
    /// connection for schema discovery instead of opening a nested scope. (SQLite resolves columns from
    /// [Column] attributes, so BUG A itself does not apply here — but the nesting hazard does.)
    /// </summary>
    [TestClass]
    public class RemoteTransactionColdTests
    {
        private static string _dbPath;
        private string _connectionString;
        private SqliteOrmDataProvider _provider;

        [Table("funky_txn_cold_target")]
        public class TxnColdTarget
        {
            [Column("id")] public int Id { get; set; }
            [Column("cold_value")] public string ColdValue { get; set; }
        }

        [Table("funky_txn_cold_source")]
        public sealed class TxnColdSource
        {
            [Column("id")] public int Id { get; set; }
            [Column("txn_cold_target_id")] public int? TxnColdTargetId { get; set; }
            [Column("own_value")] public string OwnValue { get; set; }

            [RemoteProperty(typeof(TxnColdTarget), nameof(TxnColdTargetId), nameof(TxnColdTarget.ColdValue))]
            public string RemoteColdValue { get; set; }
        }

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"funky_sqlite_txncold_{Guid.NewGuid():N}.db");
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            Exec(conn, "CREATE TABLE funky_txn_cold_target (id INTEGER PRIMARY KEY AUTOINCREMENT, cold_value TEXT NULL);");
            Exec(conn, "CREATE TABLE funky_txn_cold_source (id INTEGER PRIMARY KEY AUTOINCREMENT, txn_cold_target_id INTEGER NULL, own_value TEXT NULL);");
            Exec(conn, "INSERT INTO funky_txn_cold_target (cold_value) VALUES ('alpha'), ('bravo');");
            Exec(conn, "INSERT INTO funky_txn_cold_source (txn_cold_target_id, own_value) VALUES (1, 'x'), (2, 'y'), (1, 'z');");
        }

        private static void Exec(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        }

        [TestInitialize]
        public void Setup()
        {
            _connectionString = $"Data Source={_dbPath}";
            _provider = new SqliteOrmDataProvider(_connectionString);
        }

        [TestCleanup]
        public void Cleanup() => _provider?.Dispose();

        [TestMethod]
        public void ColdRemoteTarget_EagerGetAndUpdate_InsideTransaction_DoNotNestScopes()
        {
            _provider.BeginTransaction();
            try
            {
                var one = _provider.Get<TxnColdSource>(1);       // cold remote-target discovery under the txn
                Assert.IsNotNull(one, "eager Get should return the row");
                Assert.AreEqual("alpha", one.RemoteColdValue, "remote column should be populated");

                var list = _provider.GetList<TxnColdSource>();
                Assert.AreEqual(3, list.Count);

                one.OwnValue = "changed";
                _provider.Update(one);

                _provider.CommitTransaction();
            }
            catch
            {
                _provider.RollbackTransaction();
                throw;
            }

            var reread = _provider.Get<TxnColdSource>(1);
            Assert.AreEqual("changed", reread.OwnValue);
        }
    }
}
