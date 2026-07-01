using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using Funcular.Data.Orm.Attributes;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>
    /// Regression test for the cold-cache remote-column bug (mirrors the SQL Server
    /// RemoteColdCacheTests): a [RemoteProperty]'s target column was resolved via the naive
    /// property-name fallback (e.g. "coldvalue") until the target type was itself materialized
    /// somewhere in the process, so a fresh process emitted an invalid column and threw.
    /// <para>
    /// The target entity <see cref="ColdTarget"/> is used ONLY here and is NEVER queried directly, so its
    /// schema is genuinely "cold" whenever this test runs — deterministically exercising the fix.
    /// </para>
    /// </summary>
    [TestClass]
    public class MySqlRemoteColdCacheTests
    {
        private string _connectionString;
        private MySqlOrmDataProvider _provider;
        private readonly StringBuilder _sb = new StringBuilder();

        // Remote target with a SNAKE_CASE column. The naive fallback (property.Name.ToLower() = "coldvalue")
        // differs from the real column "cold_value" — which is exactly what surfaces the bug.
        [Table("funky_cold_target")]
        public class ColdTarget
        {
            public int Id { get; set; }
            public string ColdValue { get; set; } // column: cold_value
        }

        // Wide [Table] DTO (does not inherit the target), joining to ColdTarget via [RemoteProperty].
        // FK name follows the {Type}Id convention so the explicit remote-path resolver can find the target.
        [Table("funky_cold_source")]
        public sealed class ColdSource
        {
            public int Id { get; set; }
            public int? ColdTargetId { get; set; } // column: cold_target_id

            [RemoteProperty(typeof(ColdTarget), nameof(ColdTargetId), nameof(ColdTarget.ColdValue))]
            public string RemoteColdValue { get; set; }
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
                Exec(conn, "DROP TABLE IF EXISTS funky_cold_source");
                Exec(conn, "DROP TABLE IF EXISTS funky_cold_target");
                Exec(conn, "CREATE TABLE funky_cold_target (id INT AUTO_INCREMENT PRIMARY KEY, cold_value VARCHAR(100) NULL)");
                Exec(conn, "CREATE TABLE funky_cold_source (id INT AUTO_INCREMENT PRIMARY KEY, cold_target_id INT NULL)");
                Exec(conn, "INSERT INTO funky_cold_target (cold_value) VALUES ('alpha'), ('bravo')");
                Exec(conn, "INSERT INTO funky_cold_source (cold_target_id) VALUES (1), (2), (1)");
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
        public void RemoteProperty_ColdTarget_ResolvesSnakeCaseColumn_AndExecutes()
        {
            // Pre-fix (cold): the remote column resolved to naive "coldvalue" -> MySqlException "Unknown column".
            // Post-fix: ResolveRemoteJoins discovers the target schema first -> real "cold_value" -> succeeds.
            _sb.Clear();
            var rows = _provider.Query<ColdSource>()
                .Where(s => s.RemoteColdValue != null)
                .OrderByDescending(s => s.RemoteColdValue)
                .ToList();

            var sql = _sb.ToString();
            StringAssert.Contains(sql, "cold_value", "remote column must resolve to the real snake_case column");
            Assert.IsFalse(sql.Contains("coldvalue"),
                "remote column must NOT be the naive 'coldvalue' fallback (cold-cache BUG A regression)");
            Assert.AreEqual(3, rows.Count, "all three seeded source rows have a non-null remote value");
        }
    }
}
