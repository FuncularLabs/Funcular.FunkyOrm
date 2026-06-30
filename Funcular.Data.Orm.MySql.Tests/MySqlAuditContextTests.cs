using System;
using System.Linq;
using System.Text;
using Funcular.Data.Orm;
using Funcular.Data.Orm.MySql.Tests.Domain;

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>Maps the sp_funky_session probe result (primed session user variables).</summary>
    public class FunkySession
    {
        public string UserId { get; set; }
        public string TeamIds { get; set; }
    }

    /// <summary>
    /// Integration tests for MySQL session-context priming (v3.8.0). MySQL has no native RLS, so this is
    /// audit-only: the primed session user variables support attribution, and the self-attributing audit
    /// comment is emitted on text commands. Requires AllowUserVariables=true (the fixture sets it).
    /// </summary>
    [TestClass]
    public class MySqlAuditContextTests : MySqlTestFixture
    {
        private sealed class TestAccessor : IAuditContextAccessor
        {
            public FunkyAuditContext Current { get; set; }
        }

        private TestAccessor _accessor;

        [TestInitialize]
        public void Setup()
        {
            InitProvider();
            _accessor = new TestAccessor();
            _provider.AuditContext = new AuditContextOptions { Accessor = _accessor, RequireAuditContext = true };
        }

        [TestCleanup]
        public void Cleanup() => DisposeProvider();

        private static string NewId() => Guid.NewGuid().ToString("N");

        private void SetUser(string userId, string teamIds = "")
            => _accessor.Current = new FunkyAuditContext
            {
                Entries = new[]
                {
                    new SessionContextEntry("myapp_user_id", userId),
                    new SessionContextEntry("myapp_group_ids", teamIds),
                },
                AuditSubjectId = userId,
            };

        [TestMethod]
        public void SessionVariable_IsPrimedForAttribution()
        {
            var a = NewId();
            SetUser(a);
            // The probe proc reads @UserId on its (just-primed) connection.
            var rows = _provider.ExecProcedure<FunkySession>("sp_funky_session");
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(a, rows.First().UserId);
        }

        [TestMethod]
        public void AuditComment_IsPrependedToTextCommands()
        {
            var captured = new StringBuilder();
            _provider.Log = s => captured.AppendLine(s);

            var a = NewId();
            SetUser(a);
            _provider.GetList<Person>(); // text SELECT -> audit comment prepended

            StringAssert.Contains(captured.ToString(), "/* funky:audit sub=" + a + " */");
        }

        [TestMethod]
        public void FailClosed_RequiredContextMissing_Throws()
        {
            _accessor.Current = null;
            Assert.ThrowsException<InvalidOperationException>(() => _provider.GetList<Person>());
        }

        [TestMethod]
        public void InvalidKey_Throws()
        {
            // MySQL user-variable names must be [A-Za-z0-9_]; a dotted key must fail clearly.
            _accessor.Current = new FunkyAuditContext
            {
                Entries = new[] { new SessionContextEntry("bad.key", "x") }
            };
            Assert.ThrowsException<InvalidOperationException>(() => _provider.GetList<Person>());
        }
    }
}
