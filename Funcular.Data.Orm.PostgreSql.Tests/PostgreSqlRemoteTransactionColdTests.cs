using System;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Npgsql;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    /// <summary>
    /// Regression test for the 3.8.3 transaction-nesting hazard: cold <see cref="RemotePropertyAttribute"/> target
    /// discovery runs inside command building (which, on the eager/CRUD paths, runs inside the operation's own
    /// ConnectionScope). Discovering the remote target used to open a nested ConnectionScope; under an open
    /// transaction that tripped the transactional-scope guard. The fix borrows the ambient transactional
    /// connection for schema discovery instead of opening a nested scope.
    /// </summary>
    [TestClass]
    public class PostgreSqlRemoteTransactionColdTests
    {
        private string _connectionString;
        private PostgreSqlOrmDataProvider _provider;

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
            _connectionString = PostgreSqlTestConnection.Resolve();
            EnsureSchema();
            _provider = new PostgreSqlOrmDataProvider(_connectionString);
        }

        [TestCleanup]
        public void Cleanup() => _provider?.Dispose();

        private void EnsureSchema()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                try { conn.Open(); }
                catch (NpgsqlException ex)
                {
                    Assert.Inconclusive(
                        $"PostgreSQL not available. Start Docker: docker compose -f Database/PostgreSql/docker-compose.yml up -d\n{ex.Message}");
                    return;
                }

                var drop = conn.CreateCommand();
                drop.CommandText = @"
                    DROP TABLE IF EXISTS funky_txn_cold_source;
                    DROP TABLE IF EXISTS funky_txn_cold_target;";
                drop.ExecuteNonQuery();

                var create = conn.CreateCommand();
                create.CommandText = @"
                    CREATE TABLE funky_txn_cold_target (id SERIAL PRIMARY KEY, cold_value VARCHAR(100) NULL);
                    CREATE TABLE funky_txn_cold_source (id SERIAL PRIMARY KEY, txn_cold_target_id INT NULL, own_value VARCHAR(100) NULL);
                    INSERT INTO funky_txn_cold_target (cold_value) VALUES ('alpha'), ('bravo');
                    INSERT INTO funky_txn_cold_source (txn_cold_target_id, own_value) VALUES (1, 'x'), (2, 'y'), (1, 'z');";
                create.ExecuteNonQuery();
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
