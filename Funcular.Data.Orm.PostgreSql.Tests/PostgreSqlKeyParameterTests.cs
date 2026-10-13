using System;
using Funcular.Data.Orm.Tests.KeyParameters;
using Npgsql;

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

        private static void Execute(string sql)
        {
            using var connection = new NpgsqlConnection(ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private const string Drop =
            "DROP TABLE IF EXISTS " + TextTable + "; DROP TABLE IF EXISTS " + GuidTable + "; DROP TABLE IF EXISTS " + NumTable +
            "; DROP TABLE IF EXISTS " + PkTable + "; ";

        protected override void CreateTables() => Execute(Drop +
            $"CREATE TABLE {TextTable} (name VARCHAR(100) NOT NULL PRIMARY KEY, val INT NOT NULL); " +
            $"CREATE TABLE {GuidTable} (id UUID NOT NULL PRIMARY KEY, val INT NOT NULL); " +
            $"CREATE TABLE {NumTable} (id INT NOT NULL PRIMARY KEY, label VARCHAR(20) NOT NULL); " +
            $"INSERT INTO {NumTable} (id, label) VALUES (1, 'one'), (2, 'two'); " +
            $"CREATE TABLE {PkTable} (pk VARCHAR(100) NOT NULL PRIMARY KEY, val INT NOT NULL);");

        protected override void DropTables() => Execute(Drop);

        protected override OrmDataProvider CreateProvider() => new PostgreSqlOrmDataProvider(ConnectionString);
    }
}
