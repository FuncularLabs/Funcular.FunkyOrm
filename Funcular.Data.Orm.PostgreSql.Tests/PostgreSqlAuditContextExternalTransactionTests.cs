using System;
using System.Linq;
using System.Threading.Tasks;
using Funcular.Data.Orm;
using Funcular.Data.Orm.PostgreSql;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    /// <summary>
    /// Issue #24 (3.13.0): a transaction the provider didn't begin is primed once on its first command, and a strict
    /// provider fails closed on it. Runs as the non-superuser role <c>funky_rls_tester</c> so rls_demo's policy
    /// enforces; every test rolls its transaction back.
    /// </summary>
    [TestClass]
    public class PostgreSqlAuditContextExternalTransactionTests
    {
        private sealed class TestAccessor : IAuditContextAccessor
        {
            public FunkyAuditContext Current { get; set; }
        }

        private string _rlsConnectionString;

        [TestInitialize]
        public void Setup()
        {
            var builder = new NpgsqlConnectionStringBuilder(PostgreSqlTestConnection.Resolve())
            {
                Username = "funky_rls_tester",
                Password = "funky_rls_pw"
            };
            _rlsConnectionString = builder.ConnectionString;
            using (var c = new NpgsqlConnection(_rlsConnectionString))
            {
                try { c.Open(); }
                catch (Exception ex) { Assert.Inconclusive($"PostgreSQL non-superuser role unavailable.\n{ex.Message}"); }
            }
        }

        private static string NewId() => Guid.NewGuid().ToString("N");

        private static FunkyAuditContext Context(string userId)
            => new FunkyAuditContext
            {
                Entries = new[]
                {
                    new SessionContextEntry("myapp.user_id", userId),
                    new SessionContextEntry("myapp.group_ids", ""),
                },
                AuditSubjectId = userId,
            };

        private static AuditContextOptions Options(TestAccessor accessor)
            => new AuditContextOptions { Accessor = accessor, RequireAuditContext = true };

        private NpgsqlConnection Open()
        {
            var connection = new NpgsqlConnection(_rlsConnectionString);
            connection.Open();
            return connection;
        }

        private static string Setting(NpgsqlConnection connection, NpgsqlTransaction transaction, string key)
        {
            using (var command = new NpgsqlCommand("SELECT current_setting(@k, true)", connection, transaction))
            {
                command.Parameters.AddWithValue("k", key);
                var value = command.ExecuteScalar();
                return value is DBNull ? null : (string)value;
            }
        }

        [TestMethod]
        public void ExternalTransaction_ConstructorSupplied_PrimesAndRlsFilters()
        {
            var a = NewId();
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                var db = new PostgreSqlOrmDataProvider(_rlsConnectionString, connection, transaction)
                {
                    AuditContext = Options(new TestAccessor { Current = Context(a) }),
                };

                // The policy's WITH CHECK refuses this insert unless the connection carries a's id.
                db.Insert(new RlsDemo { OwnerId = a, Payload = "external-ctor" });
                var rows = db.GetList<RlsDemo>();

                Assert.IsTrue(rows.Any(r => r.Payload == "external-ctor"));
                Assert.IsTrue(rows.All(r => r.OwnerId == a));
                Assert.AreEqual(a, Setting(connection, transaction, "myapp.user_id"));
                transaction.Rollback();
            }
        }

        [TestMethod]
        public void ExternalTransaction_Strict_NoContext_Throws_ThenRecovers()
        {
            var a = NewId();
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                var accessor = new TestAccessor();
                var db = new PostgreSqlOrmDataProvider(_rlsConnectionString, connection, transaction) { AuditContext = Options(accessor) };

                var ex = Assert.ThrowsException<InvalidOperationException>(() => db.GetList<RlsDemo>());
                StringAssert.Contains(ex.Message, "An audit context is required");

                accessor.Current = Context(a);
                db.Insert(new RlsDemo { OwnerId = a, Payload = "recovered" });
                Assert.IsTrue(db.GetList<RlsDemo>().Any(r => r.Payload == "recovered"));
                transaction.Rollback();
            }
        }

        [TestMethod]
        public async Task ExternalTransaction_Deletes_Strict_NoContext_Throw()
        {
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                var db = new PostgreSqlOrmDataProvider(_rlsConnectionString, connection, transaction) { AuditContext = Options(new TestAccessor()) };

                StringAssert.Contains(Assert.ThrowsException<InvalidOperationException>(() => db.Delete<RlsDemo>(r => r.OwnerId == "nobody")).Message,
                    "An audit context is required");
                StringAssert.Contains(Assert.ThrowsException<InvalidOperationException>(() => db.Delete<RlsDemo>(-1L)).Message,
                    "An audit context is required");
                StringAssert.Contains((await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => db.DeleteAsync<RlsDemo>(r => r.OwnerId == "nobody"))).Message,
                    "An audit context is required");
                StringAssert.Contains((await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => db.DeleteAsync<RlsDemo>(-1L))).Message,
                    "An audit context is required");
                transaction.Rollback();
            }
        }

        [TestMethod]
        public void ExternalTransaction_ResetSettingReadsEmpty_IsTreatedAsUnset()
        {
            var a = NewId();
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                // A placeholder setting that was set and then reset reads '' rather than NULL.
                using (var command = new NpgsqlCommand("SELECT set_config('myapp.user_id', '', false)", connection, transaction))
                    command.ExecuteNonQuery();

                var db = new PostgreSqlOrmDataProvider(_rlsConnectionString, connection, transaction)
                {
                    AuditContext = Options(new TestAccessor { Current = Context(a) }),
                };
                db.Insert(new RlsDemo { OwnerId = a, Payload = "was-empty" });

                Assert.AreEqual(a, Setting(connection, transaction, "myapp.user_id"));
                transaction.Rollback();
            }
        }

        [TestMethod]
        public void SharedTransaction_SameContext_SecondProviderRuns()
        {
            var a = NewId();
            var first = new PostgreSqlOrmDataProvider(_rlsConnectionString) { AuditContext = Options(new TestAccessor { Current = Context(a) }) };
            first.BeginTransaction();
            try
            {
                first.Insert(new RlsDemo { OwnerId = a, Payload = "shared" });
                var second = new PostgreSqlOrmDataProvider(_rlsConnectionString, first.Connection, first.Transaction)
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
            var first = new PostgreSqlOrmDataProvider(_rlsConnectionString) { AuditContext = Options(new TestAccessor { Current = Context(a) }) };
            first.BeginTransaction();
            try
            {
                var second = new PostgreSqlOrmDataProvider(_rlsConnectionString, first.Connection, first.Transaction)
                {
                    AuditContext = Options(new TestAccessor { Current = Context(b) }),
                };
                var ex = Assert.ThrowsException<InvalidOperationException>(() => second.GetList<RlsDemo>());
                StringAssert.Contains(ex.Message, "myapp.user_id");
                Assert.IsFalse(ex.Message.Contains(a) || ex.Message.Contains(b), "the message must not carry either id");
                Assert.AreEqual(a, Setting((NpgsqlConnection)first.Connection, (NpgsqlTransaction)first.Transaction, "myapp.user_id"),
                    "the first identity stays on the connection");
            }
            finally
            {
                first.RollbackTransaction();
                first.Dispose();
            }
        }
    }
}
