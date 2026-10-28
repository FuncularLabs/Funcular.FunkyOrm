using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Funcular.Data.Orm;
using Funcular.Data.Orm.MySql.Tests.Domain;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>
    /// Issue #24 (3.13.0): a transaction the provider didn't begin is primed once on its first command, and a strict
    /// provider fails closed on it. MySQL is attribution only, so the tests read the session variable back on the same
    /// connection. Every test rolls its transaction back.
    /// </summary>
    [TestClass]
    public class MySqlAuditContextExternalTransactionTests : MySqlTestFixture
    {
        private sealed class TestAccessor : IAuditContextAccessor
        {
            public FunkyAuditContext Current { get; set; }
        }

        [TestInitialize]
        public void Setup() => InitProvider();

        [TestCleanup]
        public void Cleanup() => DisposeProvider();

        private static string NewId() => Guid.NewGuid().ToString("N");

        private static FunkyAuditContext Context(string userId)
            => new FunkyAuditContext
            {
                Entries = new[]
                {
                    new SessionContextEntry("myapp_user_id", userId),
                    new SessionContextEntry("myapp_group_ids", ""),
                },
                AuditSubjectId = userId,
            };

        private MySqlConnection Open()
        {
            var connection = new MySqlConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private MySqlOrmDataProvider Provider(TestAccessor accessor, MySqlConnection connection, MySqlTransaction transaction)
            => new MySqlOrmDataProvider(_connectionString, connection, transaction)
            {
                AuditContext = new AuditContextOptions { Accessor = accessor, RequireAuditContext = true },
            };

        private static string Variable(MySqlConnection connection, MySqlTransaction transaction)
        {
            using (var command = new MySqlCommand("SELECT CAST(@myapp_user_id AS CHAR)", connection, transaction))
            {
                var value = command.ExecuteScalar();
                return value is DBNull ? null : (string)value;
            }
        }

        [TestMethod]
        public void ExternalTransaction_ConstructorSupplied_Primes()
        {
            var a = NewId();
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                var db = Provider(new TestAccessor { Current = Context(a) }, connection, transaction);

                db.Query<Person>(p => p.Id == -1);

                Assert.AreEqual(a, Variable(connection, transaction));
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
                var db = Provider(accessor, connection, transaction);

                var ex = Assert.ThrowsException<InvalidOperationException>(() => db.Query<Person>(p => p.Id == -1));
                StringAssert.Contains(ex.Message, "An audit context is required");

                accessor.Current = Context(a);
                db.Query<Person>(p => p.Id == -1);
                Assert.AreEqual(a, Variable(connection, transaction));
                transaction.Rollback();
            }
        }

        [TestMethod]
        public void ExternalTransaction_InvalidKey_ThrowsBeforeAnyCommand()
        {
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                // Keys become variable names in the SQL text, so they are validated before the first read.
                var accessor = new TestAccessor
                {
                    Current = new FunkyAuditContext { Entries = new[] { new SessionContextEntry("bad-key", "v") } },
                };
                var db = Provider(accessor, connection, transaction);
                var log = new List<string>();
                db.Log = log.Add;

                var ex = Assert.ThrowsException<InvalidOperationException>(() => db.Query<Person>(p => p.Id == -1));
                StringAssert.Contains(ex.Message, "[A-Za-z0-9_]");
                Assert.IsFalse(log.Any(l => l.Contains("CAST(@") || l.StartsWith("SET @")), "no read or set may be sent");
                transaction.Rollback();
            }
        }

        [TestMethod]
        public async Task ExternalTransaction_Deletes_Strict_NoContext_Throw()
        {
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                var db = Provider(new TestAccessor(), connection, transaction);

                StringAssert.Contains(Assert.ThrowsException<InvalidOperationException>(() => db.Delete<Person>(p => p.Id == -1)).Message,
                    "An audit context is required");
                StringAssert.Contains(Assert.ThrowsException<InvalidOperationException>(() => db.Delete<Person>(-1L)).Message,
                    "An audit context is required");
                StringAssert.Contains((await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => db.DeleteAsync<Person>(p => p.Id == -1))).Message,
                    "An audit context is required");
                StringAssert.Contains((await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => db.DeleteAsync<Person>(-1L))).Message,
                    "An audit context is required");
                transaction.Rollback();
            }
        }

        [TestMethod]
        public void ExternalTransaction_DifferentReadOnlyValue_Throws()
        {
            var a = NewId();
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                using (var command = new MySqlCommand("SET @myapp_user_id = 'another-identity'", connection, transaction))
                    command.ExecuteNonQuery();
                var db = Provider(new TestAccessor { Current = Context(a) }, connection, transaction);

                var ex = Assert.ThrowsException<InvalidOperationException>(() => db.Query<Person>(p => p.Id == -1));
                StringAssert.Contains(ex.Message, "myapp_user_id");
                Assert.IsFalse(ex.Message.Contains(a) || ex.Message.Contains("another-identity"), "the message must not carry either value");
                Assert.AreEqual("another-identity", Variable(connection, transaction));
                transaction.Rollback();
            }
        }
    }
}
