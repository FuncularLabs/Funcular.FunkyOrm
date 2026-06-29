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

        [TestMethod]
        public void Transaction_PrimedOnce_OpsSeePrimedIdentity()
        {
            var a = NewId();
            SetUser(a);
            _provider.BeginTransaction();
            try
            {
                // BeginTransaction primes the connection once; subsequent ops reuse it WITHOUT re-priming —
                // a re-prime of a read_only key would throw "the key has already been set".
                _provider.Insert(new RlsDemo { OwnerId = a, Payload = "txn" });
                var rows = _provider.GetList<RlsDemo>();
                Assert.IsTrue(rows.All(r => r.OwnerId == a));
                Assert.IsTrue(rows.Any(r => r.Payload == "txn"));
                _provider.CommitTransaction();
            }
            catch
            {
                _provider.RollbackTransaction();
                throw;
            }
        }

        [TestMethod]
        public void Concurrency_ParallelRequests_NoBleed()
        {
            var accessor = new AsyncLocalAccessor();
            using (var provider = new SqlServerOrmDataProvider(_connectionString)
            {
                AuditContext = new AuditContextOptions { Accessor = accessor, RequireAuditContext = true }
            })
            {
                var users = Enumerable.Range(0, 8).Select(_ => NewId()).ToArray();
                var tasks = users.Select(u => System.Threading.Tasks.Task.Run(() =>
                {
                    accessor.Set(new FunkyAuditContext
                    {
                        Entries = new[] { new SessionContextEntry("UserId", u), new SessionContextEntry("TeamIds", "") },
                        AuditSubjectId = u,
                    });
                    provider.Insert(new RlsDemo { OwnerId = u, Payload = u });
                    var rows = provider.GetList<RlsDemo>();
                    return rows.Count >= 1 && rows.All(r => r.OwnerId == u);
                })).ToArray();

                System.Threading.Tasks.Task.WaitAll(tasks);
                Assert.IsTrue(tasks.All(t => t.Result), "each parallel request must see only its own rows (no context bleed)");
            }
        }

        [TestMethod]
        public void AuditComment_SubjectAndCorrelation_EndToEnd()
        {
            var captured = new StringBuilder();
            _provider.Log = s => captured.AppendLine(s);

            var a = NewId();
            var corr = "req-" + NewId();
            _accessor.Current = new FunkyAuditContext
            {
                Entries = new[] { new SessionContextEntry("UserId", a), new SessionContextEntry("TeamIds", "") },
                AuditSubjectId = a,
                AuditCorrelationId = corr,
            };
            _provider.Insert(new RlsDemo { OwnerId = a, Payload = "log-check2" });

            StringAssert.Contains(captured.ToString(), "/* funky:audit sub=" + a + " corr=" + corr + " */");
        }

        [TestMethod]
        public void LenientProvider_PrimesWhenPresent_NoThrowWhenAbsent()
        {
            var accessor = new TestAccessor();
            using (var lenient = new SqlServerOrmDataProvider(_connectionString)
            {
                AuditContext = new AuditContextOptions { Accessor = accessor, RequireAuditContext = false }
            })
            {
                // Context present: priming happens opportunistically and RLS filters to this user.
                var a = NewId();
                accessor.Current = new FunkyAuditContext
                {
                    Entries = new[] { new SessionContextEntry("UserId", a), new SessionContextEntry("TeamIds", "") }
                };
                lenient.Insert(new RlsDemo { OwnerId = a, Payload = "lenient" });
                Assert.IsTrue(lenient.GetList<RlsDemo>().All(r => r.OwnerId == a));

                // No context: a lenient provider must NOT throw (primes nothing); a's row is simply not visible.
                accessor.Current = null;
                var rows = lenient.GetList<RlsDemo>();
                Assert.IsFalse(rows.Any(r => r.OwnerId == a));
            }
        }

        /// <summary>AsyncLocal-backed accessor for the concurrency test (mirrors a real middleware setup).</summary>
        private sealed class AsyncLocalAccessor : IAuditContextAccessor
        {
            private static readonly System.Threading.AsyncLocal<FunkyAuditContext> _ctx =
                new System.Threading.AsyncLocal<FunkyAuditContext>();
            public FunkyAuditContext Current => _ctx.Value;
            public void Set(FunkyAuditContext context) => _ctx.Value = context;
        }
    }
}
