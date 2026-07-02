using System;
using System.Linq;
using System.Text;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Npgsql;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    /// <summary>
    /// Regression test for BUG C (3.8.4): a [RemoteProperty] whose remote VALUE column is declared on a BASE
    /// class of the target entity. The 3.8.3 cold-cache fix discovered the value property's DeclaringType as a
    /// table; when that column is inherited (e.g. Uid on EntityBase, Call : EntityBase) the base has no [Table]
    /// and discovery threw "Invalid object name 'entitybase'". The fix discovers the concrete [Table] target
    /// (remoteType) instead. Mirrors the reporter's ChildInhBaseCol harness.
    /// </summary>
    [TestClass]
    public class PostgreSqlRemoteInheritedColumnTests
    {
        private string _connectionString;
        private PostgreSqlOrmDataProvider _provider;
        private readonly StringBuilder _sb = new();

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
            _connectionString = PostgreSqlTestConnection.Resolve();
            EnsureSchema();
            _provider = new PostgreSqlOrmDataProvider(_connectionString) { Log = s => _sb.AppendLine(s) };
        }

        [TestCleanup]
        public void Cleanup() => _provider?.Dispose();

        private void EnsureSchema()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                try
                {
                    conn.Open();
                }
                catch (NpgsqlException ex)
                {
                    Assert.Inconclusive(
                        $"PostgreSQL not available. Start Docker: docker compose -f Database/PostgreSql/docker-compose.yml up -d\n{ex.Message}");
                    return;
                }

                var drop = conn.CreateCommand();
                drop.CommandText = @"
                    DROP TABLE IF EXISTS funky_inh_child_b;
                    DROP TABLE IF EXISTS funky_inh_child_d;
                    DROP TABLE IF EXISTS funky_inh_parent;";
                drop.ExecuteNonQuery();

                var create = conn.CreateCommand();
                create.CommandText = @"
                    CREATE TABLE funky_inh_parent (id SERIAL PRIMARY KEY, uid VARCHAR(40) NULL, display_name VARCHAR(100) NULL);
                    CREATE TABLE funky_inh_child_b (id SERIAL PRIMARY KEY, inh_parent_id INT NULL);
                    CREATE TABLE funky_inh_child_d (id SERIAL PRIMARY KEY, inh_parent_id INT NULL);
                    INSERT INTO funky_inh_parent (uid, display_name) VALUES ('uid-alpha', 'Alpha'), ('uid-bravo', 'Bravo');
                    INSERT INTO funky_inh_child_b (inh_parent_id) VALUES (1), (2), (1);
                    INSERT INTO funky_inh_child_d (inh_parent_id) VALUES (1), (2), (1);";
                create.ExecuteNonQuery();
            }
        }

        [TestMethod]
        public void RemoteProperty_InheritedValueColumn_ResolvesAndExecutes()
        {
            // Pre-fix: threw PostgresException "relation \"inhparentbase\" does not exist"
            // during DiscoverColumns for the value column's (base) declaring type.
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
