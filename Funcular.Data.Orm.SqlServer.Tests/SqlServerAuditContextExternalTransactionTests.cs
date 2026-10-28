using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using Funcular.Data.Orm;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    /// <summary>
    /// The rls_demo table under a type no other test uses, so its columns are discovered inside the test's
    /// transaction (AC-7, docs/plans/EXTERNAL_TRANSACTION_AUDIT_PLAN.md).
    /// </summary>
    [Table("rls_demo")]
    public class RlsDemoColdForExternalTransaction
    {
        public int Id { get; set; }
        public string OwnerId { get; set; }
        public string Payload { get; set; }
    }

    /// <summary>
    /// Issue #24 (3.13.0): a transaction the provider didn't begin (passed to the constructor, or assigned to
    /// <c>Transaction</c>) is primed once, by its first operation with a context, and a strict provider fails closed on it. Every test
    /// rolls its transaction back, so no rows are left behind. Runs against FUNKY_CONNECTION (.\SQL2019), whose
    /// schema includes rls_demo + dbo.rls_demo_policy (filter and block predicates on owner_id).
    /// </summary>
    [TestClass]
    public class SqlServerAuditContextExternalTransactionTests
    {
        private sealed class TestAccessor : IAuditContextAccessor
        {
            public FunkyAuditContext Current { get; set; }
        }

        private string _connectionString;

        [TestInitialize]
        public void Setup()
        {
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
                "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=funky_db;Integrated Security=True;";
            using (var c = new SqlConnection(_connectionString))
            {
                try { c.Open(); }
                catch (SqlException ex) { Assert.Inconclusive($"SQL Server not available.\n{ex.Message}"); }
            }
        }

        private static string NewId() => Guid.NewGuid().ToString("N");

        private static FunkyAuditContext Context(string userId, params SessionContextEntry[] more)
            => new FunkyAuditContext
            {
                Entries = new[]
                {
                    new SessionContextEntry("myapp.user_id", userId),
                    new SessionContextEntry("myapp.group_ids", ""),
                }.Concat(more).ToArray(),
                AuditSubjectId = userId,
            };

        private static AuditContextOptions Options(TestAccessor accessor, bool strict = true)
            => new AuditContextOptions { Accessor = accessor, RequireAuditContext = strict };

        private SqlConnection Open()
        {
            var connection = new SqlConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private static object SessionValue(SqlConnection connection, SqlTransaction transaction, string key)
        {
            using (var command = new SqlCommand("SELECT SESSION_CONTEXT(@k)", connection, transaction))
            {
                command.Parameters.AddWithValue("@k", key);
                var value = command.ExecuteScalar();
                return value is DBNull ? null : value;
            }
        }

        private static int Count(List<string> log, string text) => log.Count(l => l.Contains(text));

        [TestMethod]
        public void ExternalTransaction_ConstructorSupplied_PrimesAndRlsFilters()
        {
            var a = NewId();
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                var accessor = new TestAccessor { Current = Context(a) };
                var db = new SqlServerOrmDataProvider(_connectionString, connection, transaction) { AuditContext = Options(accessor) };

                // The block predicate refuses this insert unless the connection carries a's id.
                db.Insert(new RlsDemo { OwnerId = a, Payload = "external-ctor" });
                var rows = db.GetList<RlsDemo>();

                Assert.IsTrue(rows.Any(r => r.Payload == "external-ctor"));
                Assert.IsTrue(rows.All(r => r.OwnerId == a), "the filter predicate must see a's id");
                Assert.AreEqual(a, SessionValue(connection, transaction, "myapp.user_id"));
                transaction.Rollback();
            }
        }

        [TestMethod]
        public void ExternalTransaction_Assigned_PrimesAndRlsFilters()
        {
            var a = NewId();
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                var accessor = new TestAccessor { Current = Context(a) };
                var db = new SqlServerOrmDataProvider(_connectionString) { AuditContext = Options(accessor) };
                db.Connection = connection;
                db.Transaction = transaction;

                db.Insert(new RlsDemo { OwnerId = a, Payload = "external-assigned" });
                var rows = db.GetList<RlsDemo>();

                Assert.IsTrue(rows.Any(r => r.Payload == "external-assigned"));
                Assert.IsTrue(rows.All(r => r.OwnerId == a));
                transaction.Rollback();
            }
        }

        [TestMethod]
        public async Task ExternalTransaction_Async_Primes()
        {
            var a = NewId();
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                var accessor = new TestAccessor { Current = Context(a) };
                var db = new SqlServerOrmDataProvider(_connectionString, connection, transaction) { AuditContext = Options(accessor) };

                await db.InsertAsync(new RlsDemo { OwnerId = a, Payload = "external-async" });
                var rows = await db.GetListAsync<RlsDemo>();

                Assert.IsTrue(rows.Any(r => r.Payload == "external-async"));
                Assert.IsTrue(rows.All(r => r.OwnerId == a));
                transaction.Rollback();
            }
        }

        [TestMethod]
        public void ExternalTransaction_PrimesOnce_NoSecondReadOrSet()
        {
            var a = NewId();
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                var log = new List<string>();
                var accessor = new TestAccessor { Current = Context(a) };
                var db = new SqlServerOrmDataProvider(_connectionString, connection, transaction)
                {
                    AuditContext = Options(accessor),
                    Log = log.Add,
                };

                db.Insert(new RlsDemo { OwnerId = a, Payload = "once" });
                db.GetList<RlsDemo>();
                db.GetList<RlsDemo>();

                Assert.AreEqual(1, Count(log, "SESSION_CONTEXT("), "the keys are read once, on the first operation");
                Assert.AreEqual(1, Count(log, "sp_set_session_context"), "the keys are set once, on the first operation");
                transaction.Rollback();
            }
        }

        [TestMethod]
        public void BeginTransaction_LaterOps_DoNotReadOrRePrime()
        {
            var a = NewId();
            var log = new List<string>();
            var accessor = new TestAccessor { Current = Context(a) };
            using (var db = new SqlServerOrmDataProvider(_connectionString) { AuditContext = Options(accessor), Log = log.Add })
            {
                db.BeginTransaction();
                try
                {
                    db.Insert(new RlsDemo { OwnerId = a, Payload = "begun" });
                    db.GetList<RlsDemo>();
                    db.GetList<RlsDemo>();

                    Assert.AreEqual(1, Count(log, "sp_set_session_context"), "BeginTransaction primes once (3.12.1's behaviour)");
                    Assert.AreEqual(0, Count(log, "SESSION_CONTEXT("), "a transaction the provider began is never read back");
                }
                finally
                {
                    db.RollbackTransaction();
                }
            }
        }

        [TestMethod]
        public void ExternalTransaction_Strict_NoContext_Throws_ThenRecovers()
        {
            var a = NewId();
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                var log = new List<string>();
                var accessor = new TestAccessor();
                var db = new SqlServerOrmDataProvider(_connectionString, connection, transaction)
                {
                    AuditContext = Options(accessor),
                    Log = log.Add,
                };

                var ex = Assert.ThrowsException<InvalidOperationException>(() => db.GetList<RlsDemo>());
                StringAssert.Contains(ex.Message, "An audit context is required");
                Assert.IsFalse(log.Any(l => l.Contains("rls_demo") && l.Contains("FROM") && !l.Contains("INFORMATION_SCHEMA")),
                    "the operation's own command must not be sent");

                // The provider stays usable: the next operation, with a context, primes and runs.
                accessor.Current = Context(a);
                db.Insert(new RlsDemo { OwnerId = a, Payload = "recovered" });
                Assert.IsTrue(db.GetList<RlsDemo>().Any(r => r.Payload == "recovered"));
                transaction.Rollback();
            }
        }

        [TestMethod]
        public void SharedTransaction_SameContext_SecondProviderRuns()
        {
            var a = NewId();
            var first = new SqlServerOrmDataProvider(_connectionString) { AuditContext = Options(new TestAccessor { Current = Context(a) }) };
            first.BeginTransaction();
            try
            {
                first.Insert(new RlsDemo { OwnerId = a, Payload = "shared" });

                // A second provider on the first one's transaction, with the same context: the read-only keys
                // already hold these values, so they are not set again (no Msg 15664).
                var second = new SqlServerOrmDataProvider(_connectionString, first.Connection, first.Transaction)
                {
                    AuditContext = Options(new TestAccessor { Current = Context(a) }),
                };
                var rows = second.GetList<RlsDemo>();
                Assert.IsTrue(rows.Any(r => r.Payload == "shared"));
                Assert.IsTrue(rows.All(r => r.OwnerId == a));
            }
            finally
            {
                first.RollbackTransaction();
                first.Dispose();
            }
        }

        [TestMethod]
        public void SharedTransaction_DifferentReadOnlyValue_Throws()
        {
            var a = NewId();
            var b = NewId();
            var first = new SqlServerOrmDataProvider(_connectionString) { AuditContext = Options(new TestAccessor { Current = Context(a) }) };
            first.BeginTransaction();
            try
            {
                var second = new SqlServerOrmDataProvider(_connectionString, first.Connection, first.Transaction)
                {
                    AuditContext = Options(new TestAccessor { Current = Context(b) }),
                };
                var ex = Assert.ThrowsException<InvalidOperationException>(() => second.GetList<RlsDemo>());
                StringAssert.Contains(ex.Message, "myapp.user_id");
                Assert.IsFalse(ex.Message.Contains(a) || ex.Message.Contains(b), "the message must not carry either id");
                Assert.AreEqual(a, SessionValue((SqlConnection)first.Connection, (SqlTransaction)first.Transaction, "myapp.user_id"));
            }
            finally
            {
                first.RollbackTransaction();
                first.Dispose();
            }
        }

        [TestMethod]
        public void ExternalTransaction_WritableKeyDifferentValue_Overwritten()
        {
            var a = NewId();
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                using (var command = new SqlCommand(
                    "EXEC sys.sp_set_session_context @key = N'myapp.request_id', @value = N'old', @read_only = 0;", connection, transaction))
                    command.ExecuteNonQuery();

                var accessor = new TestAccessor { Current = Context(a, new SessionContextEntry("myapp.request_id", "new", readOnly: false)) };
                var db = new SqlServerOrmDataProvider(_connectionString, connection, transaction) { AuditContext = Options(accessor) };

                db.GetList<RlsDemo>();

                Assert.AreEqual("new", SessionValue(connection, transaction, "myapp.request_id"));
                Assert.AreEqual(a, SessionValue(connection, transaction, "myapp.user_id"));
                transaction.Rollback();
            }
        }

        [TestMethod]
        public void ExternalTransaction_Lenient_NoContext_RunsUnprimed()
        {
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                var db = new SqlServerOrmDataProvider(_connectionString, connection, transaction)
                {
                    AuditContext = Options(new TestAccessor(), strict: false),
                };

                var rows = db.GetList<RlsDemo>();

                Assert.AreEqual(0, rows.Count, "with no context the filter predicate matches no rows");
                Assert.IsNull(SessionValue(connection, transaction, "myapp.user_id"));
                transaction.Rollback();
            }
        }

        [TestMethod]
        public void ExternalTransaction_FeatureOff_NoAuditCommands()
        {
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                var log = new List<string>();
                var db = new SqlServerOrmDataProvider(_connectionString, connection, transaction) { Log = log.Add };

                db.GetList<RlsDemo>();

                Assert.AreEqual(0, Count(log, "SESSION_CONTEXT("));
                Assert.AreEqual(0, Count(log, "sp_set_session_context"));
                transaction.Rollback();
            }
        }

        [TestMethod]
        public void ExternalTransaction_ColdEntity_DiscoveryThenPrime()
        {
            var a = NewId();
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                var accessor = new TestAccessor { Current = Context(a) };
                var db = new SqlServerOrmDataProvider(_connectionString, connection, transaction) { AuditContext = Options(accessor) };

                // Smoke test: the type's columns are discovered inside this transaction first, then the insert is
                // primed (the block predicate refuses it otherwise).
                db.Insert(new RlsDemoColdForExternalTransaction { OwnerId = a, Payload = "cold" });

                Assert.IsTrue(db.GetList<RlsDemoColdForExternalTransaction>().Any(r => r.Payload == "cold"));
                transaction.Rollback();
            }
        }

        [TestMethod]
        public void ExternalTransaction_SystemScopeOperation_DoesNotMarkPrimed()
        {
            var a = NewId();
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                var db = new SqlServerOrmDataProvider(_connectionString, connection, transaction)
                {
                    AuditContext = Options(new TestAccessor { Current = Context(a) }),
                };

                // An operation under a system scope primes nothing (as FunkyORM's own bootstrap queries do) and must
                // not mark the transaction primed: the insert that follows still primes, or the block predicate refuses it.
                using (SystemContextScope.Enter())
                    db.GetList<RlsDemo>();
                db.Insert(new RlsDemo { OwnerId = a, Payload = "after-system-scope" });

                Assert.AreEqual(a, SessionValue(connection, transaction, "myapp.user_id"));
                transaction.Rollback();
            }
        }

        [TestMethod]
        public void ExternalTransaction_Lenient_NoContextThenContext_Primes()
        {
            var a = NewId();
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                var accessor = new TestAccessor();
                var db = new SqlServerOrmDataProvider(_connectionString, connection, transaction)
                {
                    AuditContext = Options(accessor, strict: false),
                };

                db.GetList<RlsDemo>();          // no context: nothing primed, the transaction stays unmarked
                accessor.Current = Context(a);
                db.Insert(new RlsDemo { OwnerId = a, Payload = "lenient-then-context" });

                Assert.AreEqual(a, SessionValue(connection, transaction, "myapp.user_id"));
                transaction.Rollback();
            }
        }

        [TestMethod]
        public void ExternalTransaction_Reassigned_NewConnectionAndTransaction_PrimesAgain()
        {
            var a = NewId();
            var accessor = new TestAccessor { Current = Context(a) };
            var db = new SqlServerOrmDataProvider(_connectionString) { AuditContext = Options(accessor) };
            using (var first = Open())
            using (var firstTransaction = first.BeginTransaction())
            {
                db.Connection = first;
                db.Transaction = firstTransaction;
                db.Insert(new RlsDemo { OwnerId = a, Payload = "first" });
                firstTransaction.Rollback();
            }
            using (var second = Open())
            using (var secondTransaction = second.BeginTransaction())
            {
                // Primed once per transaction, not once per provider: the second connection is primed too.
                db.Connection = second;
                db.Transaction = secondTransaction;
                db.Insert(new RlsDemo { OwnerId = a, Payload = "second" });

                Assert.AreEqual(a, SessionValue(second, secondTransaction, "myapp.user_id"));
                secondTransaction.Rollback();
            }
        }

        [TestMethod]
        public void ExternalTransaction_DeleteFirst_Primes()
        {
            var a = NewId();
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                var log = new List<string>();
                var db = new SqlServerOrmDataProvider(_connectionString, connection, transaction)
                {
                    AuditContext = Options(new TestAccessor { Current = Context(a) }),
                    Log = log.Add,
                };

                db.Delete<RlsDemo>(r => r.OwnerId == a);

                Assert.AreEqual(1, Count(log, "SESSION_CONTEXT("), "a delete as the first operation primes the transaction");
                Assert.AreEqual(a, SessionValue(connection, transaction, "myapp.user_id"));
                transaction.Rollback();
            }
        }

        [TestMethod]
        public void ExternalTransaction_Delete_Strict_NoContext_Throws()
        {
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                var db = new SqlServerOrmDataProvider(_connectionString, connection, transaction) { AuditContext = Options(new TestAccessor()) };

                var byPredicate = Assert.ThrowsException<InvalidOperationException>(() => db.Delete<RlsDemo>(r => r.OwnerId == "nobody"));
                StringAssert.Contains(byPredicate.Message, "An audit context is required");
                var byKey = Assert.ThrowsException<InvalidOperationException>(() => db.Delete<RlsDemo>(-1L));
                StringAssert.Contains(byKey.Message, "An audit context is required");
                transaction.Rollback();
            }
        }

        [TestMethod]
        public async Task ExternalTransaction_DeleteAsync_Strict_NoContext_Throws()
        {
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                var db = new SqlServerOrmDataProvider(_connectionString, connection, transaction) { AuditContext = Options(new TestAccessor()) };

                var byPredicate = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => db.DeleteAsync<RlsDemo>(r => r.OwnerId == "nobody"));
                StringAssert.Contains(byPredicate.Message, "An audit context is required");
                var byKey = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => db.DeleteAsync<RlsDemo>(-1L));
                StringAssert.Contains(byKey.Message, "An audit context is required");
                transaction.Rollback();
            }
        }
    }
}
