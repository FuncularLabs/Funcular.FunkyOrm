using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using Funcular.Data.Orm.Attributes;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>
    /// Regression test for BUG C (3.8.4): a [RemoteProperty] whose remote VALUE column is declared on a BASE
    /// class of the target entity. The 3.8.3 cold-cache fix discovered the value property's DeclaringType as a
    /// table; when that column is inherited (e.g. Uid on InhParentBase, InhParent : InhParentBase) the base has
    /// no [Table] and discovery threw "Unknown table 'inhparentbase'". The fix discovers the concrete [Table]
    /// target (remoteType) instead. Mirrors the SQL Server RemoteInheritedColumnTests harness.
    /// </summary>
    [TestClass]
    public class MySqlRemoteInheritedColumnTests
    {
        private string _connectionString;
        private MySqlOrmDataProvider _provider;
        private readonly StringBuilder _sb = new StringBuilder();

        // Target hierarchy: PK (Id) AND a value column (Uid) are declared on the BASE; DisplayName on the derived.
        public abstract class InhParentBase
        {
            public int Id { get; set; }        // PK on base
            public string Uid { get; set; }    // value column on BASE → column "uid"
        }

        [Table("funky_inh_parent")]
        public class InhParent : InhParentBase
        {
            public string DisplayName { get; set; } // value column on DERIVED → column "display_name"
        }

        // Remote VALUE column is inherited (Uid on the base) — the BUG C case. Own table (separate from the
        // control below) so this isolates the inherited-column variable, not any same-table interaction.
        [Table("funky_inh_child_b")]
        public sealed class InhChildBaseCol
        {
            public int Id { get; set; }
            public int? InhParentId { get; set; } // FK → "inh_parent_id"; name-infers to InhParent (agrees with typeof)

            [RemoteProperty(typeof(InhParent), nameof(InhParentId), nameof(InhParent.Uid))]
            public string RemoteUid { get; set; }
        }

        // Control: remote VALUE column is on the derived target (DisplayName) — worked pre-fix, must still work.
        [Table("funky_inh_child_d")]
        public sealed class InhChildDerivedCol
        {
            public int Id { get; set; }
            public int? InhParentId { get; set; }

            [RemoteProperty(typeof(InhParent), nameof(InhParentId), nameof(InhParent.DisplayName))]
            public string RemoteDisplayName { get; set; }
        }

        [TestInitialize]
        public void Setup()
        {
            _sb.Clear();
            var raw = ResolveConnectionString();
            if (string.IsNullOrWhiteSpace(raw))
                Assert.Inconclusive("FUNKY_MYSQL_CONNECTION is not set. Set it (Machine or User scope) to run this test.");

            var builder = new MySqlConnectionStringBuilder(raw)
            {
                GuidFormat = MySqlGuidFormat.Char36,
                AllowUserVariables = true
            };
            if (string.IsNullOrEmpty(builder.Database)) builder.Database = "funky_db";
            _connectionString = builder.ConnectionString;

            EnsureSchema();
            _provider = new MySqlOrmDataProvider(_connectionString) { Log = s => _sb.AppendLine(s) };
        }

        [TestCleanup]
        public void Cleanup() => _provider?.Dispose();

        private static string ResolveConnectionString()
        {
            var value = Environment.GetEnvironmentVariable("FUNKY_MYSQL_CONNECTION");
            if (!string.IsNullOrWhiteSpace(value)) return value;

            if (OperatingSystem.IsWindows())
            {
                value = Environment.GetEnvironmentVariable("FUNKY_MYSQL_CONNECTION", EnvironmentVariableTarget.Machine);
                if (!string.IsNullOrWhiteSpace(value)) return value;
                value = Environment.GetEnvironmentVariable("FUNKY_MYSQL_CONNECTION", EnvironmentVariableTarget.User);
            }
            return value;
        }

        private void EnsureSchema()
        {
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                // MySQL: run each DDL/DML statement as its own command (no reliance on multi-statement batches).
                Exec(conn, "DROP TABLE IF EXISTS funky_inh_child_b");
                Exec(conn, "DROP TABLE IF EXISTS funky_inh_child_d");
                Exec(conn, "DROP TABLE IF EXISTS funky_inh_parent");
                Exec(conn, "CREATE TABLE funky_inh_parent (id INT AUTO_INCREMENT PRIMARY KEY, uid VARCHAR(40) NULL, display_name VARCHAR(100) NULL)");
                Exec(conn, "CREATE TABLE funky_inh_child_b (id INT AUTO_INCREMENT PRIMARY KEY, inh_parent_id INT NULL)");
                Exec(conn, "CREATE TABLE funky_inh_child_d (id INT AUTO_INCREMENT PRIMARY KEY, inh_parent_id INT NULL)");
                Exec(conn, "INSERT INTO funky_inh_parent (uid, display_name) VALUES ('uid-alpha', 'Alpha'), ('uid-bravo', 'Bravo')");
                Exec(conn, "INSERT INTO funky_inh_child_b (inh_parent_id) VALUES (1), (2), (1)");
                Exec(conn, "INSERT INTO funky_inh_child_d (inh_parent_id) VALUES (1), (2), (1)");
            }
        }

        private static void Exec(MySqlConnection conn, string sql)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        [TestMethod]
        public void RemoteProperty_InheritedValueColumn_ResolvesAndExecutes()
        {
            // Pre-fix: threw MySqlException "Unknown table 'inhparentbase'" during column discovery for the
            // value column's (base) declaring type.
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
