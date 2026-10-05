using System;
using Funcular.Data.Orm.Tests.DeleteGuard;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    /// <summary>The delete-guard harness on PostgreSQL (docs/plans/DELETE_GUARD_PLAN.md, §4.1).</summary>
    [TestClass]
    public class PostgreSqlDeleteGuardTests : DeleteGuardHarness
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

        private static void Execute(string sql)
        {
            using var connection = new NpgsqlConnection(ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        protected override void CreateTables() => Execute(
            $"DROP TABLE IF EXISTS {RowTable}; DROP TABLE IF EXISTS {TrueUpTable}; " +
            $"CREATE TABLE {RowTable} (id INT PRIMARY KEY, first_name VARCHAR(20) NULL, true_up INT NOT NULL, big INT NULL, archived BOOLEAN NOT NULL DEFAULT FALSE); " +
            $"INSERT INTO {RowTable} (id, first_name, true_up) VALUES (1, 'a', 1), (2, 'b', 0), (3, NULL, 0); " +
            $"CREATE TABLE {TrueUpTable} (id INT PRIMARY KEY, amount INT NOT NULL); " +
            $"INSERT INTO {TrueUpTable} (id, amount) VALUES (1, 5), (2, 7);");

        protected override void DropTables() => Execute($"DROP TABLE IF EXISTS {RowTable}; DROP TABLE IF EXISTS {TrueUpTable};");

        protected override void DropMissingTable() => Execute($"DROP TABLE IF EXISTS {MissingTable};");

        protected override OrmDataProvider CreateProvider() => new PostgreSqlOrmDataProvider(ConnectionString);

        protected override bool IsDiscovered(OrmDataProvider provider, Type type) =>
            ((PostgreSqlOrmDataProvider)provider).ScopeMappedTypes.Contains(type);
    }
}
