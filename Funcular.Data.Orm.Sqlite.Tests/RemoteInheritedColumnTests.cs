using System;
using System.IO;
using System.Linq;
using System.Text;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.Sqlite.Tests
{
    /// <summary>
    /// Regression test for BUG C (3.8.4): a [RemoteProperty] whose remote VALUE column is declared on a BASE
    /// class of the target entity. The 3.8.3 cold-cache fix discovered the value property's DeclaringType as a
    /// table; when that column is inherited (e.g. Uid on InhParentBase, InhParent : InhParentBase) the base has
    /// no [Table] and discovery threw "no such table: inhparentbase". The fix discovers the concrete [Table]
    /// target (remoteType) instead. This discovery-of-the-base-type happens in SQLite too (independent of
    /// SQLite's [Column]-based column resolution), so SQLite genuinely had BUG C. Mirrors the SQL Server harness.
    /// </summary>
    [TestClass]
    public class RemoteInheritedColumnTests
    {
        private static string _dbPath;
        private string _connectionString;
        private SqliteOrmDataProvider _provider;
        private readonly StringBuilder _sb = new();

        // Target hierarchy: PK (Id) AND a value column (Uid) are declared on the BASE; DisplayName on the derived.
        // SQLite resolves columns from explicit [Column] attributes, so every mapped property carries [Column("...")].
        public abstract class InhParentBase
        {
            [Column("id")] public int Id { get; set; }        // PK on base
            [Column("uid")] public string Uid { get; set; }   // value column on BASE → column "uid"
        }

        [Table("funky_inh_parent")]
        public class InhParent : InhParentBase
        {
            [Column("display_name")] public string DisplayName { get; set; } // value column on DERIVED → "display_name"
        }

        // Remote VALUE column is inherited (Uid on the base) — the BUG C case. Own table (separate from the
        // control below) so this isolates the inherited-column variable, not any same-table interaction.
        [Table("funky_inh_child_b")]
        public sealed class InhChildBaseCol
        {
            [Column("id")] public int Id { get; set; }
            [Column("inh_parent_id")] public int? InhParentId { get; set; } // FK → "inh_parent_id"; name-infers to InhParent

            [RemoteProperty(typeof(InhParent), nameof(InhParentId), nameof(InhParent.Uid))]
            public string RemoteUid { get; set; }
        }

        // Control: remote VALUE column is on the derived target (DisplayName) — worked pre-fix, must still work.
        [Table("funky_inh_child_d")]
        public sealed class InhChildDerivedCol
        {
            [Column("id")] public int Id { get; set; }
            [Column("inh_parent_id")] public int? InhParentId { get; set; }

            [RemoteProperty(typeof(InhParent), nameof(InhParentId), nameof(InhParent.DisplayName))]
            public string RemoteDisplayName { get; set; }
        }

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            // A file-backed SQLite DB keeps the tables alive across the provider's own short-lived connections
            // (an in-memory DB would vanish the instant a connection closed).
            _dbPath = Path.Combine(Path.GetTempPath(), $"funky_sqlite_inh_{Guid.NewGuid():N}.db");
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            // SQLite executes one statement per command, so run the DDL/DML as discrete statements.
            Exec(conn, "CREATE TABLE funky_inh_parent (id INTEGER PRIMARY KEY AUTOINCREMENT, uid TEXT NULL, display_name TEXT NULL);");
            Exec(conn, "CREATE TABLE funky_inh_child_b (id INTEGER PRIMARY KEY AUTOINCREMENT, inh_parent_id INTEGER NULL);");
            Exec(conn, "CREATE TABLE funky_inh_child_d (id INTEGER PRIMARY KEY AUTOINCREMENT, inh_parent_id INTEGER NULL);");
            Exec(conn, "INSERT INTO funky_inh_parent (uid, display_name) VALUES ('uid-alpha', 'Alpha');");
            Exec(conn, "INSERT INTO funky_inh_parent (uid, display_name) VALUES ('uid-bravo', 'Bravo');");
            Exec(conn, "INSERT INTO funky_inh_child_b (inh_parent_id) VALUES (1);");
            Exec(conn, "INSERT INTO funky_inh_child_b (inh_parent_id) VALUES (2);");
            Exec(conn, "INSERT INTO funky_inh_child_b (inh_parent_id) VALUES (1);");
            Exec(conn, "INSERT INTO funky_inh_child_d (inh_parent_id) VALUES (1);");
            Exec(conn, "INSERT INTO funky_inh_child_d (inh_parent_id) VALUES (2);");
            Exec(conn, "INSERT INTO funky_inh_child_d (inh_parent_id) VALUES (1);");
        }

        private static void Exec(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        }

        [TestInitialize]
        public void Setup()
        {
            _sb.Clear();
            _connectionString = $"Data Source={_dbPath}";
            _provider = new SqliteOrmDataProvider(_connectionString) { Log = s => _sb.AppendLine(s) };
        }

        [TestCleanup]
        public void Cleanup() => _provider?.Dispose();

        [TestMethod]
        public void RemoteProperty_InheritedValueColumn_ResolvesAndExecutes()
        {
            // Pre-fix: threw "no such table: inhparentbase" during DiscoverColumns for the value column's
            // (base) declaring type. The fix discovers the concrete [Table] target instead.
            _sb.Clear();
            var rows = _provider.Query<InhChildBaseCol>()
                .Where(c => c.RemoteUid != null)
                .OrderByDescending(c => c.RemoteUid)
                .ToList();

            var sql = _sb.ToString();
            StringAssert.Contains(sql, "uid", "remote value column (inherited) must resolve to the real 'uid' column");
            Assert.IsFalse(sql.ToLowerInvariant().Contains("inhparentbase"),
                "must not treat the base class as a table (BUG C)");
            Assert.AreEqual(3, rows.Count, "all three seeded children have a non-null remote uid");
            Assert.IsTrue(rows.All(r => r.RemoteUid != null && r.RemoteUid.StartsWith("uid-")));
        }

        [TestMethod]
        public void RemoteProperty_DerivedValueColumn_StillWorks()
        {
            // Control: value column on the derived target — must still resolve (regression guard for the fix).
            var rows = _provider.Query<InhChildDerivedCol>()
                .Where(c => c.RemoteDisplayName != null)
                .ToList();
            Assert.AreEqual(3, rows.Count);
            Assert.IsTrue(rows.All(r => r.RemoteDisplayName == "Alpha" || r.RemoteDisplayName == "Bravo"));
        }
    }
}
