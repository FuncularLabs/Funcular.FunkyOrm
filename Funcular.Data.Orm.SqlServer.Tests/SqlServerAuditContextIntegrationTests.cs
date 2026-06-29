using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using Funcular.Data.Orm;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    /// <summary>Entity over the standalone RLS demo table (filtered + blocked by dbo.rls_demo_policy).</summary>
    [Table("rls_demo")]
    public class RlsDemo
    {
        public int Id { get; set; }
        public string OwnerId { get; set; }
        public string Payload { get; set; }
    }

    /// <summary>
    /// Lean integration cut for SQL Server session-context priming + RLS (v3.8.0). Proves: session context
    /// is primed onto the connection and RLS filters by it; no leak across sequential pooled operations;
    /// CSV TeamIds membership grants access; fail-closed throws when a required context is absent; and the
    /// self-attributing audit comment is emitted. Runs against FUNKY_CONNECTION (.\SQL2019), whose schema
    /// includes rls_demo + dbo.rls_demo_policy.
    /// </summary>
    [TestClass]
    public class SqlServerAuditContextIntegrationTests
    {
        private sealed class TestAccessor : IAuditContextAccessor
        {
            public FunkyAuditContext Current { get; set; }
        }

        private string _connectionString;
        private TestAccessor _accessor;
        private SqlServerOrmDataProvider _provider;

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
            _accessor = new TestAccessor();
            _provider = new SqlServerOrmDataProvider(_connectionString)
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

            // As A: only A's rows are visible.
            SetUser(a);
            var asA = _provider.GetList<RlsDemo>();
            Assert.IsTrue(asA.Count >= 1);
            Assert.IsTrue(asA.All(r => r.OwnerId == a), "A must see only its own rows");
            Assert.IsTrue(asA.Any(r => r.Payload == "a-row"));

            // As B: only B's rows (a fresh pooled connection re-primed for B — no leak from A's op).
            SetUser(b);
            var asB = _provider.GetList<RlsDemo>();
            Assert.IsTrue(asB.All(r => r.OwnerId == b), "B must see only its own rows");
            Assert.IsFalse(asB.Any(r => r.OwnerId == a), "A's row must not leak to B");
        }

        [TestMethod]
        public void Rls_TeamIdsCsv_GrantsAccess()
        {
            var team = NewId();
            var memberA = NewId();

            // Insert a row owned by the team key while acting as a team member (BLOCK predicate allows it via TeamIds).
            SetUser(memberA, teamIds: team);
            _provider.Insert(new RlsDemo { OwnerId = team, Payload = "team-row" });

            // A member sees the team row (via the CSV TeamIds path).
            SetUser(memberA, teamIds: team);
            Assert.IsTrue(_provider.GetList<RlsDemo>().Any(r => r.OwnerId == team), "team member should see the team row");

            // A non-member does not.
            SetUser(NewId(), teamIds: "");
            Assert.IsFalse(_provider.GetList<RlsDemo>().Any(r => r.OwnerId == team), "non-member must not see the team row");
        }

        [TestMethod]
        public void FailClosed_RequiredContextMissing_Throws()
        {
            _accessor.Current = null; // strict provider, no ambient context
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
