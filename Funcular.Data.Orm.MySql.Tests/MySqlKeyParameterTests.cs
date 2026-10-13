using System;
using Funcular.Data.Orm.Tests.KeyParameters;
using MySqlConnector;

#pragma warning disable CS0618 // The keyed CreateGetOneOrSelectCommandText is obsolete; a row pins its null behaviour.

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>
    /// The key-parameter harness on MySQL (docs/plans/KEY_PARAMETER_PLAN.md, §4.1). Table names keep their exact case,
    /// because Linux MySQL compares them case-sensitively.
    /// </summary>
    [TestClass]
    public class MySqlKeyParameterTests : KeyParameterHarness
    {
        private static string ResolveRaw()
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

        /// <summary>The MySQL test database, built as <see cref="MySqlTestFixture"/> builds it.</summary>
        private static string ConnectionString
        {
            get
            {
                var raw = ResolveRaw();
                if (string.IsNullOrWhiteSpace(raw))
                    Assert.Inconclusive("FUNKY_MYSQL_CONNECTION is not set.");
                var builder = new MySqlConnectionStringBuilder(raw) { GuidFormat = MySqlGuidFormat.Char36, AllowUserVariables = true };
                if (string.IsNullOrEmpty(builder.Database)) builder.Database = "funky_db";
                return builder.ConnectionString;
            }
        }

        protected override void RequireDatabase()
        {
            try
            {
                using var connection = new MySqlConnection(ConnectionString);
                connection.Open();
            }
            catch (Exception ex) when (!(ex is AssertInconclusiveException))
            {
                Assert.Inconclusive($"MySQL test database is unreachable ({ex.GetType().Name}).");
            }
        }

        private static void Execute(params string[] statements)
        {
            using var connection = new MySqlConnection(ConnectionString);
            connection.Open();
            foreach (var sql in statements)
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        private static readonly string[] Drop =
        {
            $"DROP TABLE IF EXISTS {TextTable}", $"DROP TABLE IF EXISTS {GuidTable}", $"DROP TABLE IF EXISTS {NumTable}",
            $"DROP TABLE IF EXISTS {PkTable}", $"DROP TABLE IF EXISTS {EnumTable}", $"DROP TABLE IF EXISTS {UintTable}"
        };

        protected override void CreateTables()
        {
            Execute(Drop);
            Execute(
                $"CREATE TABLE {TextTable} (name VARCHAR(100) NOT NULL PRIMARY KEY, val INT NOT NULL) CHARACTER SET utf8mb4",
                $"CREATE TABLE {GuidTable} (id CHAR(36) NOT NULL PRIMARY KEY, val INT NOT NULL)",
                $"CREATE TABLE {NumTable} (id INT NOT NULL PRIMARY KEY, label VARCHAR(20) NOT NULL)",
                $"INSERT INTO {NumTable} (id, label) VALUES (1, 'one'), (2, 'two')",
                $"CREATE TABLE {PkTable} (pk VARCHAR(100) NOT NULL PRIMARY KEY, val INT NOT NULL) CHARACTER SET utf8mb4",
                $"CREATE TABLE {EnumTable} (id INT NOT NULL PRIMARY KEY, val INT NOT NULL)",
                $"INSERT INTO {EnumTable} (id, val) VALUES (1, 10), (2, 20)",
                $"CREATE TABLE {UintTable} (id INT NOT NULL PRIMARY KEY, val INT NOT NULL)",
                $"INSERT INTO {UintTable} (id, val) VALUES (1, 10), (2, 20)");
        }

        protected override void DropTables() => Execute(Drop);

        protected override OrmDataProvider CreateProvider() => new MySqlOrmDataProvider(ConnectionString);

        private sealed class Probe : MySqlOrmDataProvider
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
    }
}
