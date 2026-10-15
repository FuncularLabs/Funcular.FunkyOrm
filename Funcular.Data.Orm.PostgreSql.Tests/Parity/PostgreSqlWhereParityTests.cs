using System;
using System.Globalization;
using System.Linq;
using Funcular.Data.Orm.Tests.Parity;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Tests.Parity
{
    /// <summary>The WHERE parity rows (D-001) on PostgreSQL (the suite's connection resolution).</summary>
    [TestClass]
    public class PostgreSqlWhereParityTests : WhereParityHarness
    {
        private static string ConnectionString => PostgreSqlTestConnection.Resolve();

        protected override void RequireDatabase()
        {
            try
            {
                using (var connection = new NpgsqlConnection(ConnectionString))
                    connection.Open();
            }
            catch (Exception ex) when (ex is NpgsqlException || ex is InvalidOperationException || ex is System.Net.Sockets.SocketException)
            {
                Assert.Inconclusive("PostgreSQL test database not available: " + ex.GetType().Name);
            }
        }

        private static void Execute(string sql)
        {
            using (var connection = new NpgsqlConnection(ConnectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }
            }
        }

        protected override void CreateTable() => Execute(
            $"DROP TABLE IF EXISTS {Table}; " +
            $"CREATE TABLE {Table} (id INT PRIMARY KEY, name VARCHAR(40) NULL, code INT NOT NULL, posted_at TIMESTAMP NOT NULL, nullable_id INT NULL); " +
            $"INSERT INTO {Table} (id, name, code, posted_at, nullable_id) VALUES " +
            string.Join(", ", Rows.Select(r =>
                $"({r.Id}, '{r.Name}', {r.Code}, '{r.PostedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}', " +
                (r.NullableId.HasValue ? r.NullableId.Value.ToString(CultureInfo.InvariantCulture) : "NULL") + ")")) + ";");

        protected override void DropTable() => Execute($"DROP TABLE IF EXISTS {Table};");

        protected override OrmDataProvider CreateProvider() => new PostgreSqlOrmDataProvider(ConnectionString);

        protected override string DatePartSql(string part, string column) => "EXTRACT(" + part.ToUpperInvariant() + " FROM " + column + ")";

        protected override string LengthSql(string column) => "LENGTH(" + column + ")";
    }
}
