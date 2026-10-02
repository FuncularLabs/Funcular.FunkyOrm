using System;
using Funcular.Data.Orm.Tests.DeleteGuard;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>
    /// The delete-guard harness on MySQL (docs/plans/DELETE_GUARD_PLAN.md, §4.1). Table names keep their exact case,
    /// because Linux MySQL compares them case-sensitively.
    /// </summary>
    [TestClass]
    public class MySqlDeleteGuardTests : DeleteGuardHarness
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

        protected override void CreateTables() => Execute(
            $"DROP TABLE IF EXISTS {RowTable}",
            $"DROP TABLE IF EXISTS {TrueUpTable}",
            $"CREATE TABLE {RowTable} (id INT PRIMARY KEY, first_name VARCHAR(20) NULL, true_up INT NOT NULL, big INT NULL, archived TINYINT(1) NOT NULL DEFAULT 0)",
            $"INSERT INTO {RowTable} (id, first_name, true_up) VALUES (1, 'a', 1), (2, 'b', 0), (3, NULL, 0)",
            $"CREATE TABLE {TrueUpTable} (id INT PRIMARY KEY, amount INT NOT NULL)",
            $"INSERT INTO {TrueUpTable} (id, amount) VALUES (1, 5), (2, 7)");

        protected override void DropTables() => Execute($"DROP TABLE IF EXISTS {RowTable}", $"DROP TABLE IF EXISTS {TrueUpTable}");

        protected override void DropMissingTable() => Execute($"DROP TABLE IF EXISTS {MissingTable}");

        protected override OrmDataProvider CreateProvider() => new MySqlOrmDataProvider(ConnectionString);

        protected override bool IsDiscovered(OrmDataProvider provider, Type type) =>
            ((MySqlOrmDataProvider)provider).ScopeMappedTypes.Contains(type);
    }
}
