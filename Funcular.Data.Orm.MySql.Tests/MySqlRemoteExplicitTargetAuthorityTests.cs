using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using Funcular.Data.Orm.Attributes;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>
    /// §2 (3.8.5): the explicit `[RemoteProperty(typeof(Target), ...)]` target is authoritative on the final
    /// hop. A single-hop remote whose FK property name does NOT convention-match the target type (here `LookupId`
    /// → would infer "Lookup", not "B2Target") used to throw `PathNotFoundException` because `ResolveExplicit`
    /// resolved every hop by name-inference and only validated the explicit type at the end. Now the last hop
    /// honors the explicit `remoteType`, so the FK can be named anything.
    /// </summary>
    [TestClass]
    public class MySqlRemoteExplicitTargetAuthorityTests
    {
        private string _connectionString;
        private MySqlOrmDataProvider _provider;
        private readonly StringBuilder _sb = new StringBuilder();

        [Table("funky_b2_target")]
        public class B2Target
        {
            public int Id { get; set; }
            public string TargetName { get; set; } // column: target_name
        }

        [Table("funky_b2_child")]
        public sealed class B2Child
        {
            public int Id { get; set; }
            public int? LookupId { get; set; } // FK → funky_b2_target.id; name "Lookup" does NOT match "B2Target"

            [RemoteProperty(typeof(B2Target), nameof(LookupId), nameof(B2Target.TargetName))]
            public string RemoteName { get; set; }
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
                Exec(conn, "DROP TABLE IF EXISTS funky_b2_child");
                Exec(conn, "DROP TABLE IF EXISTS funky_b2_target");
                Exec(conn, "CREATE TABLE funky_b2_target (id INT AUTO_INCREMENT PRIMARY KEY, target_name VARCHAR(50) NULL)");
                Exec(conn, "CREATE TABLE funky_b2_child (id INT AUTO_INCREMENT PRIMARY KEY, lookup_id INT NULL)");
                Exec(conn, "INSERT INTO funky_b2_target (target_name) VALUES ('alpha'), ('bravo')");
                Exec(conn, "INSERT INTO funky_b2_child (lookup_id) VALUES (1), (2), (1)");
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
