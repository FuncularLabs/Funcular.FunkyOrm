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
    /// §2 (3.8.5): the explicit `[RemoteProperty(typeof(Target), ...)]` target is authoritative on the final
    /// hop. A single-hop remote whose FK property name does NOT convention-match the target type (here `LookupId`
    /// → would infer "Lookup", not "B2Target") used to throw `PathNotFoundException` because `ResolveExplicit`
    /// resolved every hop by name-inference and only validated the explicit type at the end. Now the last hop
    /// honors the explicit `remoteType`, so the FK can be named anything. (SQLite provider: columns resolve from
    /// explicit `[Column]` attributes, so the target/FK columns are attribute-mapped.)
    /// </summary>
    [TestClass]
    public class RemoteExplicitTargetAuthorityTests
    {
        private static string _dbPath;
        private string _connectionString;
        private SqliteOrmDataProvider _provider;
        private readonly StringBuilder _sb = new();

        [Table("funky_b2_target")]
        public class B2Target
        {
            [Column("id")] public int Id { get; set; }
            [Column("target_name")] public string TargetName { get; set; } // column: target_name
        }

        [Table("funky_b2_child")]
        public sealed class B2Child
        {
            [Column("id")] public int Id { get; set; }
            [Column("lookup_id")] public int? LookupId { get; set; } // FK → funky_b2_target.id; name "Lookup" does NOT match "B2Target"

            [RemoteProperty(typeof(B2Target), nameof(LookupId), nameof(B2Target.TargetName))]
            public string RemoteName { get; set; }
        }

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            // A file-backed SQLite DB keeps the tables alive across the provider's own short-lived
            // connections (an in-memory DB would vanish the instant a connection closed).
            _dbPath = Path.Combine(Path.GetTempPath(), $"funky_sqlite_b2_{Guid.NewGuid():N}.db");
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            // SQLite executes one statement per command, so run the DDL/DML as discrete statements.
            Exec(conn, "CREATE TABLE funky_b2_target (id INTEGER PRIMARY KEY AUTOINCREMENT, target_name TEXT NULL);");
            Exec(conn, "CREATE TABLE funky_b2_child (id INTEGER PRIMARY KEY AUTOINCREMENT, lookup_id INTEGER NULL);");
            Exec(conn, "INSERT INTO funky_b2_target (target_name) VALUES ('alpha'), ('bravo');");
            Exec(conn, "INSERT INTO funky_b2_child (lookup_id) VALUES (1), (2), (1);");
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
        public void ExplicitTarget_NonMatchingFkName_ResolvesViaExplicitType()
        {
            // Pre-3.8.5: threw PathNotFoundException "Could not determine target type for FK LookupId".
            var rows = _provider.Query<B2Child>()
                .Where(c => c.RemoteName != null)
                .OrderByDescending(c => c.RemoteName)
                .ToList();

            var sql = _sb.ToString();
            StringAssert.Contains(sql, "target_name", "remote value column must resolve via the explicit target");
            StringAssert.Contains(sql, "lookup_id", "join must use the FK column regardless of its name");
            Assert.AreEqual(3, rows.Count);
            Assert.IsTrue(rows.All(r => r.RemoteName == "alpha" || r.RemoteName == "bravo"));
        }
    }
}
