using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using Funcular.Data.Orm;
using Funcular.Data.Orm.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.Sqlite.Tests
{
    /// <summary>
    /// SQLite has no session context or row-level security, so audit-context priming is a no-op. A provider
    /// configured with RequireAuditContext = true is a misconfiguration and must throw rather than silently
    /// run a "PHI" workload without isolation. A lenient provider ignores any context and works normally.
    /// </summary>
    [TestClass]
    public class SqliteAuditContextTests
    {
        [Table("widget")]
        public class Widget
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private sealed class TestAccessor : IAuditContextAccessor
        {
            public FunkyAuditContext Current { get; set; }
        }

        private string _connectionString;

        [TestInitialize]
        public void Setup()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"funky_audit_{Guid.NewGuid():N}.db");
            _connectionString = $"Data Source={dbPath}";
            using (var conn = new SqliteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqliteCommand(
                    "CREATE TABLE widget (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT); INSERT INTO widget(name) VALUES ('w1');",
                    conn))
                    cmd.ExecuteNonQuery();
            }
        }

        [TestMethod]
        public void StrictProvider_OnSqlite_ThrowsNotSupported()
        {
            using (var provider = new SqliteOrmDataProvider(_connectionString)
            {
                AuditContext = new AuditContextOptions { Accessor = new TestAccessor(), RequireAuditContext = true }
            })
            {
                Assert.ThrowsException<NotSupportedException>(() => provider.GetList<Widget>());
            }
        }

        [TestMethod]
        public void LenientProvider_OnSqlite_IgnoresContextAndWorks()
        {
            var accessor = new TestAccessor
            {
                Current = new FunkyAuditContext { Entries = new[] { new SessionContextEntry("UserId", "u") } }
            };
            using (var provider = new SqliteOrmDataProvider(_connectionString)
            {
                AuditContext = new AuditContextOptions { Accessor = accessor, RequireAuditContext = false }
            })
            {
                var rows = provider.GetList<Widget>();
                Assert.IsTrue(rows.Count >= 1);
            }
        }
    }
}
