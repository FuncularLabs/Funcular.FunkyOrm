using System;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>
    /// Regression test for the 3.8.3 transaction-nesting hazard: cold <see cref="RemotePropertyAttribute"/> target
    /// discovery runs inside command building (which, on the eager/CRUD paths, runs inside the operation's own
    /// ConnectionScope). Discovering the remote target used to open a nested ConnectionScope; under an open
    /// transaction that tripped the transactional-scope guard. The fix borrows the ambient transactional
    /// connection for schema discovery instead of opening a nested scope.
    /// </summary>
    [TestClass]
    public class MySqlRemoteTransactionColdTests
    {
        private string _connectionString;
        private MySqlOrmDataProvider _provider;

        [Table("funky_txn_cold_target")]
        public class TxnColdTarget
        {
            public int Id { get; set; }
            public string ColdValue { get; set; } // column: cold_value
        }

        [Table("funky_txn_cold_source")]
        public sealed class TxnColdSource
        {
            public int Id { get; set; }
            public int? TxnColdTargetId { get; set; } // column: txn_cold_target_id
            public string OwnValue { get; set; }      // column: own_value

            [RemoteProperty(typeof(TxnColdTarget), nameof(TxnColdTargetId), nameof(TxnColdTarget.ColdValue))]
            public string RemoteColdValue { get; set; }
        }

        [TestInitialize]
        public void Setup()
        {
            var raw = ResolveConnectionString();
            if (string.IsNullOrWhiteSpace(raw))
                Assert.Inconclusive("FUNKY_MYSQL_CONNECTION is not set. Set it (Machine or User scope) to run this test.");

            var builder = new MySqlConnectionStringBuilder(raw)
            {
                GuidFormat = MySqlGuidFormat.Char36,
                AllowUserVariables = true
            };
            if (string.IsNullOrEmpty(builder.Database)) builder.Database = "funky_db";
            _connectionString = builder.ConnectionString;

            EnsureSchema();
            _provider = new MySqlOrmDataProvider(_connectionString);
        }

        [TestCleanup]
        public void Cleanup() => _provider?.Dispose();

        private static string ResolveConnectionString()
        {
            var value = Environment.GetEnvironmentVariable("FUNKY_MYSQL_CONNECTION");
            if (!string.IsNullOrWhiteSpace(value)) return value;

            if (OperatingSystem.IsWindows())
            {
                value = Environment.GetEnvironmentVariable("FUNKY_MYSQL_CONNECTION", EnvironmentVariableTarget.Machine);
                if (!string.IsNullOrWhiteSpace(value)) return value;
                value = Environment.GetEnvironmentVariable("FUNKY_MYSQL_CONNECTION", EnvironmentVariableTarget.User);
            }
            return value;
        }

        private void EnsureSchema()
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                Exec(conn, "DROP TABLE IF EXISTS funky_txn_cold_source");
                Exec(conn, "DROP TABLE IF EXISTS funky_txn_cold_target");
                Exec(conn, "CREATE TABLE funky_txn_cold_target (id INT AUTO_INCREMENT PRIMARY KEY, cold_value VARCHAR(100) NULL)");
                Exec(conn, "CREATE TABLE funky_txn_cold_source (id INT AUTO_INCREMENT PRIMARY KEY, txn_cold_target_id INT NULL, own_value VARCHAR(100) NULL)");
                Exec(conn, "INSERT INTO funky_txn_cold_target (cold_value) VALUES ('alpha'), ('bravo')");
                Exec(conn, "INSERT INTO funky_txn_cold_source (txn_cold_target_id, own_value) VALUES (1, 'x'), (2, 'y'), (1, 'z')");
            }
        }

        private static void Exec(MySqlConnection conn, string sql)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

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
