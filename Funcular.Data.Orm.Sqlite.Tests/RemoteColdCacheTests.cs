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
    /// Regression test for the 3.8.3 cold-cache remote-column bug: a [RemoteProperty]'s target column was
    /// resolved via the naive property-name fallback (e.g. "coldvalue") until the target type was itself
    /// materialized somewhere in the process, so a fresh process emitted an invalid column and threw.
    /// <para>
    /// The target entity <see cref="ColdTarget"/> is used ONLY here and is NEVER queried directly, so its
    /// schema is genuinely "cold" whenever this test runs — deterministically exercising the fix.
    /// </para>
    /// </summary>
    [TestClass]
    public class RemoteColdCacheTests
    {
        private static string _dbPath;
        private string _connectionString;
        private SqliteOrmDataProvider _provider;
        private readonly StringBuilder _sb = new();

        // Remote target with a SNAKE_CASE column. Unlike the SQL Server provider (which infers snake_case from
        // the DB schema), the SQLite provider resolves columns from explicit [Column] attributes, so the target
        // carries [Column("cold_value")]. The bug this guards is that the remote target's schema must be
        // discovered (ColumnNamesCache warmed) BEFORE its columns are resolved into the JOIN/WHERE, even when
        // the target type has never been materialized in the process.
        [Table("funky_cold_target")]
        public class ColdTarget
        {
            [Column("id")] public int Id { get; set; }
            [Column("cold_value")] public string ColdValue { get; set; }
        }

        // Wide [Table] DTO (does not inherit the target), joining to ColdTarget via [RemoteProperty].
        // FK name follows the {Type}Id convention so the explicit remote-path resolver can find the target.
        [Table("funky_cold_source")]
        public sealed class ColdSource
        {
            [Column("id")] public int Id { get; set; }
            [Column("cold_target_id")] public int? ColdTargetId { get; set; }

            [RemoteProperty(typeof(ColdTarget), nameof(ColdTargetId), nameof(ColdTarget.ColdValue))]
            public string RemoteColdValue { get; set; }
        }

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            // A file-backed SQLite DB keeps the cold tables alive across the provider's own short-lived
            // connections (an in-memory DB would vanish the instant a connection closed).
            _dbPath = Path.Combine(Path.GetTempPath(), $"funky_sqlite_cold_{Guid.NewGuid():N}.db");
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            // SQLite executes one statement per command, so run the DDL/DML as discrete statements.
            Exec(conn, "CREATE TABLE funky_cold_target (id INTEGER PRIMARY KEY AUTOINCREMENT, cold_value TEXT NULL);");
            Exec(conn, "CREATE TABLE funky_cold_source (id INTEGER PRIMARY KEY AUTOINCREMENT, cold_target_id INTEGER NULL);");
            Exec(conn, "INSERT INTO funky_cold_target (cold_value) VALUES ('alpha'), ('bravo');");
            Exec(conn, "INSERT INTO funky_cold_source (cold_target_id) VALUES (1), (2), (1);");
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
        public void RemoteProperty_ColdTarget_ResolvesSnakeCaseColumn_AndExecutes()
        {
            // Pre-fix (cold): the remote target's schema was not discovered before its columns were resolved,
            // so the join/WHERE could emit an unresolved column and throw "no such column".
            // Post-fix: ResolveRemoteJoins discovers the target schema first → real "cold_value" → succeeds.
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
