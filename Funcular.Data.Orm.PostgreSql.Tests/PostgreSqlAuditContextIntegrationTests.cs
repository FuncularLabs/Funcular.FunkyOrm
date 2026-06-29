using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using Funcular.Data.Orm;
using Funcular.Data.Orm.PostgreSql;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    /// <summary>Entity over the standalone RLS demo table (policy enforced for non-superusers).</summary>
    [Table("rls_demo")]
    public class RlsDemo
    {
        public int Id { get; set; }
        public string OwnerId { get; set; }
        public string Payload { get; set; }
    }

    /// <summary>
    /// Integration tests for PostgreSQL session-context priming + RLS (v3.8.0). PostgreSQL superusers bypass
    /// RLS, so these run as the dedicated non-superuser role <c>funky_rls_tester</c> (created in the test DDL)
    /// so the policy actually enforces. Proves: set_config priming filters rows; no leak across pooled ops;
    /// CSV TeamIds membership grants access; fail-closed; audit comment.
    /// </summary>
    [TestClass]
    public class PostgreSqlAuditContextIntegrationTests
    {
        private sealed class TestAccessor : IAuditContextAccessor
        {
            public FunkyAuditContext Current { get; set; }
        }

        private string _rlsConnectionString;
        private TestAccessor _accessor;
        private PostgreSqlOrmDataProvider _provider;

        [TestInitialize]
        public void Setup()
        {
            var baseConn = PostgreSqlTestConnection.Resolve();
            var builder = new NpgsqlConnectionStringBuilder(baseConn)
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

            _accessor = new TestAccessor();
            _provider = new PostgreSqlOrmDataProvider(_rlsConnectionString)
            {
                AuditContext = new AuditContextOptions { Accessor = _accessor, RequireAuditContext = true }
            };
        }

        [TestCleanup]
        public void Cleanup() => _provider?.Dispose();

        private static string NewId() => Guid.NewGuid().ToString("N");

        private void SetUser(string userId, string teamIds = "")
            => _accessor.Current = new FunkyAuditContext
            {
                Entries = new[]
                {
                    new SessionContextEntry("UserId", userId),
                    new SessionContextEntry("TeamIds", teamIds),
                },
                AuditSubjectId = userId,
            };

        [TestMethod]
        public void Rls_FiltersByPrimedUserId_NoLeakAcrossOps()
        {
            var a = NewId();
            var b = NewId();

            SetUser(a);
            _provider.Insert(new RlsDemo { OwnerId = a, Payload = "a-row" });

            SetUser(b);
            _provider.Insert(new RlsDemo { OwnerId = b, Payload = "b-row" });

            SetUser(a);
            var asA = _provider.GetList<RlsDemo>();
            Assert.IsTrue(asA.Count >= 1);
            Assert.IsTrue(asA.All(r => r.OwnerId == a), "A must see only its own rows");

            SetUser(b);
            var asB = _provider.GetList<RlsDemo>();
            Assert.IsTrue(asB.All(r => r.OwnerId == b), "B must see only its own rows");
            Assert.IsFalse(asB.Any(r => r.OwnerId == a), "A's row must not leak to B (pooled-connection reset)");
        }

        [TestMethod]
        public void Rls_TeamIdsCsv_GrantsAccess()
        {
            var team = NewId();
            var memberA = NewId();

            SetUser(memberA, teamIds: team);
            _provider.Insert(new RlsDemo { OwnerId = team, Payload = "team-row" });

            SetUser(memberA, teamIds: team);
            Assert.IsTrue(_provider.GetList<RlsDemo>().Any(r => r.OwnerId == team), "team member should see the team row");

            SetUser(NewId(), teamIds: "");
            Assert.IsFalse(_provider.GetList<RlsDemo>().Any(r => r.OwnerId == team), "non-member must not see the team row");
        }

        [TestMethod]
        public void FailClosed_RequiredContextMissing_Throws()
        {
            _accessor.Current = null;
            Assert.ThrowsException<InvalidOperationException>(() => _provider.GetList<RlsDemo>());
        }

        [TestMethod]
        public void AuditComment_IsPrependedToTextCommands()
        {
            var captured = new StringBuilder();
            _provider.Log = s => captured.AppendLine(s);

            var a = NewId();
            SetUser(a);
            _provider.Insert(new RlsDemo { OwnerId = a, Payload = "log-check" });

            StringAssert.Contains(captured.ToString(), "/* funky:audit sub=" + a + " */");
        }
    }
}
