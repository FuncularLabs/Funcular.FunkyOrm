using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;
using Funcular.Data.Orm.Tests.KeyParameters;
using Npgsql;

#pragma warning disable CS0618 // The keyed CreateGetOneOrSelectCommandText is obsolete; a row pins its null behaviour.

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    /// <summary>The key-parameter harness on PostgreSQL (docs/plans/KEY_PARAMETER_PLAN.md, §4.1).</summary>
    [TestClass]
    public class PostgreSqlKeyParameterTests : KeyParameterHarness
    {
        private static string ConnectionString => PostgreSqlTestConnection.Resolve();

        protected override void RequireDatabase()
        {
            try
            {
                using var connection = new NpgsqlConnection(ConnectionString);
                connection.Open();
            }
            catch (Exception ex)
            {
                Assert.Inconclusive($"PostgreSQL test database is unreachable ({ex.GetType().Name}).");
            }
        }

        private static void Execute(string connectionString, string sql)
        {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private const string Drop =
            "DROP TABLE IF EXISTS " + TextTable + "; DROP TABLE IF EXISTS " + GuidTable + "; DROP TABLE IF EXISTS " + NumTable +
            "; DROP TABLE IF EXISTS " + PkTable + "; DROP TABLE IF EXISTS " + EnumTable + "; DROP TABLE IF EXISTS " + UintTable + "; ";

        protected override void CreateTables() => Execute(ConnectionString, Drop +
            $"CREATE TABLE {TextTable} (name VARCHAR(100) NOT NULL PRIMARY KEY, val INT NOT NULL); " +
            $"CREATE TABLE {GuidTable} (id UUID NOT NULL PRIMARY KEY, val INT NOT NULL); " +
            $"CREATE TABLE {NumTable} (id INT NOT NULL PRIMARY KEY, label VARCHAR(20) NOT NULL); " +
            $"INSERT INTO {NumTable} (id, label) VALUES (1, 'one'), (2, 'two'); " +
            $"CREATE TABLE {PkTable} (pk VARCHAR(100) NOT NULL PRIMARY KEY, val INT NOT NULL); " +
            $"CREATE TABLE {EnumTable} (id INT NOT NULL PRIMARY KEY, val INT NOT NULL); " +
            $"INSERT INTO {EnumTable} (id, val) VALUES (1, 10), (2, 20); " +
            $"CREATE TABLE {UintTable} (id INT NOT NULL PRIMARY KEY, val INT NOT NULL); " +
            $"INSERT INTO {UintTable} (id, val) VALUES (1, 10), (2, 20);");

        protected override void DropTables() => Execute(ConnectionString, Drop);

        protected override OrmDataProvider CreateProvider() => new PostgreSqlOrmDataProvider(ConnectionString);

        private sealed class Probe : PostgreSqlOrmDataProvider
        {
            public Probe() : base(ConnectionString) { }
            public string Select<T>(object key) where T : class, new() => CreateGetOneOrSelectCommandText<T>(key);
        }

        /// <summary>
        /// AC9: the obsolete keyed overload, given null (what code compiled against 3.10.0 calls), builds the unfiltered
        /// SELECT.
        /// </summary>
        [TestMethod]
        public void KeyedOverload_WithNull_BuildsTheUnfilteredSelect()
        {
            RequireDatabase();
            try
            {
                CreateTables();
                using var probe = new Probe();
                var sql = probe.Select<KpText>(null);
                StringAssert.Contains(sql, TextTable);
                Assert.IsFalse(sql.Contains("WHERE"), sql);
            }
            finally
            {
                DropTables();
            }
        }

        [Table("zz_kp_date")]
        public class DateKeyed
        {
            [Key] public DateTime Ts { get; set; }
            public int Val { get; set; }
        }

        /// <summary>
        /// AC6 (round 2, N-1): a <c>timestamp</c> key given as text, with a zone or an offset or neither, finds its row as
        /// 3.10.0's literal did: text is sent untyped, so PostgreSQL reads it as the column's type and ignores the zone. A
        /// typed parameter would compare a UTC <c>DateTime</c> as <c>timestamptz</c> in the session's time zone, which the
        /// provider's connection sets to a non-UTC zone here, and find nothing.
        /// </summary>
        [DataTestMethod]
        [DataRow("2026-01-02T00:00:00Z", "sync")] [DataRow("2026-01-02T00:00:00Z", "async")]
        [DataRow("2026-01-02T00:00:00+02:00", "sync")] [DataRow("2026-01-02T00:00:00+02:00", "async")]
        [DataRow("2026-01-02 00:00:00", "sync")] [DataRow("2026-01-02 00:00:00", "async")]
        public async Task Get_DateKeyGivenAsText_MatchesAsBefore(string key, string path)
        {
            RequireDatabase();
            const string drop = "DROP TABLE IF EXISTS zz_kp_date;";
            try
            {
                Execute(ConnectionString, drop + " CREATE TABLE zz_kp_date (ts TIMESTAMP NOT NULL PRIMARY KEY, val INT NOT NULL); " +
                                          "INSERT INTO zz_kp_date VALUES ('2026-01-02 00:00:00', 1);");
                var chicago = new NpgsqlConnectionStringBuilder(ConnectionString) { Timezone = "America/Chicago" }.ConnectionString;
                using var provider = new PostgreSqlOrmDataProvider(chicago);
                var row = path == "async" ? await provider.GetAsync<DateKeyed>(key) : provider.Get<DateKeyed>(key);
                Assert.IsNotNull(row, $"{key} {path}: no row");
                Assert.AreEqual(1, row.Val, $"{key} {path}: val");
            }
            finally
            {
                Execute(ConnectionString, drop);
            }
        }

        [Table("kv")]
        public class CitextKv
        {
            [Key] public string Name { get; set; }
            public int Val { get; set; }
        }

        /// <summary>
        /// AC5 (round 1, F-4): a string key is sent untyped, as 3.10.0's literal was, so a <c>citext</c> key column still
        /// compares without case (a typed text parameter finds no row). Runs in a throwaway database,
        /// <c>zz_kp_citext</c>, created and dropped here, because <c>citext</c> is an extension; inconclusive where the
        /// test role can't create a database or the extension isn't available.
        /// </summary>
        [DataTestMethod]
        [DataRow("sync")] [DataRow("async")]
        public async Task Get_CitextKey_ComparesWithoutCase(string path)
        {
            RequireDatabase();
            var admin = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres", Pooling = false }.ConnectionString;
            var scratch = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "zz_kp_citext", Pooling = false }.ConnectionString;
            var created = false;
            try
            {
                try
                {
                    Execute(admin, "DROP DATABASE IF EXISTS zz_kp_citext");
                    Execute(admin, "CREATE DATABASE zz_kp_citext");
                    created = true;
                    Execute(scratch, "CREATE EXTENSION IF NOT EXISTS citext; " +
                                     "CREATE TABLE kv (name citext PRIMARY KEY, val INT NOT NULL); INSERT INTO kv VALUES ('abc', 1);");
                }
                catch (PostgresException ex)
                {
                    Assert.Inconclusive($"Can't create the citext scratch database ({ex.SqlState}).");
                }
                // Npgsql loads a database's types once per connection string, on first use; the setup above connected
                // before citext existed, and each run recreates the database, so the provider reads through a connection
                // string of its own for this run.
                var reader = new NpgsqlConnectionStringBuilder(scratch)
                {
                    ApplicationName = "zz_kp_citext_" + Guid.NewGuid().ToString("N").Substring(0, 8)
                }.ConnectionString;
                using var provider = new PostgreSqlOrmDataProvider(reader);
                var logged = new List<string>();
                provider.Log = logged.Add;
                var row = path == "async" ? await provider.GetAsync<CitextKv>("ABC") : provider.Get<CitextKv>("ABC");
                Assert.IsNotNull(row, $"{path}: no row");
                Assert.AreEqual(1, row.Val, $"{path}: val");
                Assert.IsTrue(logged.Exists(m => m.Contains("= " + KeyParameter)), $"{path}: the key wasn't bound");
            }
            finally
            {
                if (created)
                {
                    NpgsqlConnection.ClearAllPools();
                    Execute(admin, "DROP DATABASE IF EXISTS zz_kp_citext");
                }
            }
        }
    }
}
