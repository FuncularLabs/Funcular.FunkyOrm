using System;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    /// <summary>
    /// Regression test for the 3.8.3 transaction-nesting hazard: resolving a cold <see cref="RemotePropertyAttribute"/>
    /// target's schema happens inside command building — which, on the eager/CRUD/async paths, runs inside the
    /// operation's own <c>ConnectionScope</c>. Discovering the remote target used to open a *nested*
    /// <c>ConnectionScope</c>; under an open transaction that tripped the transactional-scope guard
    /// ("A concurrent operation is already using the transactional connection"). The fix borrows the ambient
    /// transactional connection for schema discovery instead of opening a nested scope.
    /// <para>The target <see cref="TxnColdTarget"/> is unique to this class and never queried directly, so it is
    /// genuinely cold when the transaction's first eager operation runs — the exact condition that failed.</para>
    /// </summary>
    [TestClass]
    public class RemoteTransactionColdTests
    {
        private string _connectionString;
        private SqlServerOrmDataProvider _provider;

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

        // A SECOND, independent cold pair for the async test. _mappedTypes is static/process-wide, so the sync
        // test would warm TxnColdTarget; a distinct type keeps the async path genuinely cold.
        [Table("funky_txn_cold_target2")]
        public class TxnColdTarget2
        {
            public int Id { get; set; }
            public string ColdValue { get; set; } // column: cold_value
        }

        [Table("funky_txn_cold_source2")]
        public sealed class TxnColdSource2
        {
            public int Id { get; set; }
            public int? TxnColdTarget2Id { get; set; } // column: txn_cold_target2_id
            public string OwnValue { get; set; }       // column: own_value

            [RemoteProperty(typeof(TxnColdTarget2), nameof(TxnColdTarget2Id), nameof(TxnColdTarget2.ColdValue))]
            public string RemoteColdValue { get; set; }
        }

        [TestInitialize]
        public void Setup()
        {
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
                                "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=funky_db;Integrated Security=True;";
            EnsureSchema();
            _provider = new SqlServerOrmDataProvider(_connectionString);
        }

        [TestCleanup]
        public void Cleanup() => _provider?.Dispose();

        private void EnsureSchema()
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                var drop = conn.CreateCommand();
                drop.CommandText = @"
                    IF OBJECT_ID('funky_txn_cold_source','U') IS NOT NULL DROP TABLE funky_txn_cold_source;
                    IF OBJECT_ID('funky_txn_cold_target','U') IS NOT NULL DROP TABLE funky_txn_cold_target;
                    IF OBJECT_ID('funky_txn_cold_source2','U') IS NOT NULL DROP TABLE funky_txn_cold_source2;
                    IF OBJECT_ID('funky_txn_cold_target2','U') IS NOT NULL DROP TABLE funky_txn_cold_target2;";
                drop.ExecuteNonQuery();

                var create = conn.CreateCommand();
                create.CommandText = @"
                    CREATE TABLE funky_txn_cold_target (id INT IDENTITY(1,1) PRIMARY KEY, cold_value NVARCHAR(100) NULL);
                    CREATE TABLE funky_txn_cold_source (id INT IDENTITY(1,1) PRIMARY KEY, txn_cold_target_id INT NULL, own_value NVARCHAR(100) NULL);
                    INSERT INTO funky_txn_cold_target (cold_value) VALUES ('alpha'), ('bravo');
                    INSERT INTO funky_txn_cold_source (txn_cold_target_id, own_value) VALUES (1, 'x'), (2, 'y'), (1, 'z');
                    CREATE TABLE funky_txn_cold_target2 (id INT IDENTITY(1,1) PRIMARY KEY, cold_value NVARCHAR(100) NULL);
                    CREATE TABLE funky_txn_cold_source2 (id INT IDENTITY(1,1) PRIMARY KEY, txn_cold_target2_id INT NULL, own_value NVARCHAR(100) NULL);
                    INSERT INTO funky_txn_cold_target2 (cold_value) VALUES ('alpha'), ('bravo');
                    INSERT INTO funky_txn_cold_source2 (txn_cold_target2_id, own_value) VALUES (1, 'x'), (2, 'y'), (1, 'z');";
                create.ExecuteNonQuery();
            }
        }

        [TestMethod]
        public void ColdRemoteTarget_EagerGetAndUpdate_InsideTransaction_DoNotNestScopes()
        {
            // Pre-fix: the FIRST eager op below (cold TxnColdTarget discovery inside the operation's scope, under
            // a transaction) threw "A concurrent operation is already using the transactional connection".
            _provider.BeginTransaction();
            try
            {
                var one = _provider.Get<TxnColdSource>(1);       // cold remote-target discovery under the txn
                Assert.IsNotNull(one, "eager Get should return the row");
                Assert.AreEqual("alpha", one.RemoteColdValue, "remote column should be populated");

                var list = _provider.GetList<TxnColdSource>();    // eager list, remote join present
                Assert.AreEqual(3, list.Count);

                one.OwnValue = "changed";
                _provider.Update(one);                            // the 3.6.1 locus — update under a txn

                _provider.CommitTransaction();
            }
            catch
            {
                _provider.RollbackTransaction();
                throw;
            }

            // Confirm the update committed.
            var reread = _provider.Get<TxnColdSource>(1);
            Assert.AreEqual("changed", reread.OwnValue);
        }

        [TestMethod]
        public async System.Threading.Tasks.Task ColdRemoteTarget_AsyncGet_InsideTransaction_DoesNotNestScopes()
        {
            // The async CRUD paths share the same cold-discovery fix. Uses TxnColdSource2 so the remote target is
            // genuinely cold (the sync test warms the other pair; _mappedTypes is process-wide).
            _provider.BeginTransaction();
            try
            {
                var one = await _provider.GetAsync<TxnColdSource2>(1);   // cold remote-target discovery under the txn
                Assert.IsNotNull(one, "async Get should return the row");
                Assert.AreEqual("alpha", one.RemoteColdValue, "remote column should be populated");

                var list = await _provider.GetListAsync<TxnColdSource2>();
                Assert.AreEqual(3, list.Count);

                _provider.CommitTransaction();
            }
            catch
            {
                _provider.RollbackTransaction();
                throw;
            }
        }
    }
}
