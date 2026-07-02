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
    /// Remote-join column-resolution smoke test for the SQLite provider. NOTE: unlike SQL Server / PostgreSQL /
    /// MySQL, SQLite does NOT have the 3.8.3 cold-cache BUG A. Those providers infer a remote target's column
    /// names from the DB schema (populated lazily by <c>DiscoverColumns</c>), so a cold target fell back to the
    /// naive <c>property.Name.ToLower()</c>. The SQLite provider resolves columns from explicit <c>[Column]</c>
    /// attributes (no schema-inference fallback), so the correct name is baked into the attribute and the
    /// discovery order is irrelevant — this test passes with or without the 3.8.3 discovery loop. It is kept as
    /// a smoke test that a `[Table]` DTO joining via `[RemoteProperty]` to a `[Column]`-mapped snake_case target
    /// resolves and executes; it is NOT a BUG A regression guard. (The discovery loop is still applied in the
    /// SQLite provider for cross-provider consistency; it is a harmless no-op there.)
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
            // Verifies the remote join resolves the [Column]-mapped snake_case target column and executes.
            // (SQLite resolves via [Column], so this is a smoke test, not a cold-cache regression — see class doc.)
            _sb.Clear();
            var rows = _provider.Query<ColdSource>()
                .Where(s => s.RemoteColdValue != null)
                .OrderByDescending(s => s.RemoteColdValue)
                .ToList();

            var sql = _sb.ToString();
            StringAssert.Contains(sql, "cold_value", "remote column must resolve to the [Column]-mapped snake_case column");
            Assert.AreEqual(3, rows.Count, "all three seeded source rows have a non-null remote value");
        }
    }
}
