using System;
using System.Linq;
using System.Text;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
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
        private string _connectionString;
        private SqlServerOrmDataProvider _provider;
        private readonly StringBuilder _sb = new();

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
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
                                "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=funky_db;Integrated Security=True;";
            EnsureSchema();
            _provider = new SqlServerOrmDataProvider(_connectionString) { Log = s => _sb.AppendLine(s) };
        }

        [TestCleanup]
        public void Cleanup() => _provider?.Dispose();

        private void EnsureSchema()
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                // Drop first, in its own batch, so the CREATE+INSERT batch parses against a clean state
                // (avoids SQL Server binding the INSERT to a stale existing table with a different column set).
                var drop = conn.CreateCommand();
                drop.CommandText = @"
                    IF OBJECT_ID('funky_cold_source','U') IS NOT NULL DROP TABLE funky_cold_source;
                    IF OBJECT_ID('funky_cold_target','U') IS NOT NULL DROP TABLE funky_cold_target;";
                drop.ExecuteNonQuery();

                var create = conn.CreateCommand();
                create.CommandText = @"
                    CREATE TABLE funky_cold_target (id INT IDENTITY(1,1) PRIMARY KEY, cold_value NVARCHAR(100) NULL);
                    CREATE TABLE funky_cold_source (id INT IDENTITY(1,1) PRIMARY KEY, cold_target_id INT NULL);
                    INSERT INTO funky_cold_target (cold_value) VALUES ('alpha'), ('bravo');
                    INSERT INTO funky_cold_source (cold_target_id) VALUES (1), (2), (1);";
                create.ExecuteNonQuery();
            }
        }

        [TestMethod]
        public void RemoteProperty_ColdTarget_ResolvesSnakeCaseColumn_AndExecutes()
        {
            // Pre-fix (cold): the remote column resolved to naive "coldvalue" → SqlException "Invalid column name".
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
