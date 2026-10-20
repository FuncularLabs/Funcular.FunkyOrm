using System;
using System.Globalization;
using System.Linq;
using Funcular.Data.Orm.Tests.Parity;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql.Tests.Parity
{
    /// <summary>The WHERE parity rows (D-001) on MySQL (FUNKY_MYSQL_CONNECTION).</summary>
    [TestClass]
    public class MySqlWhereParityTests : WhereParityHarness
    {
        private static string ConnectionString
        {
            get
            {
                // As MySqlTestFixture resolves it: process, then Machine and User scope.
                var raw = Environment.GetEnvironmentVariable("FUNKY_MYSQL_CONNECTION");
                if (string.IsNullOrWhiteSpace(raw) && OperatingSystem.IsWindows())
                    raw = Environment.GetEnvironmentVariable("FUNKY_MYSQL_CONNECTION", EnvironmentVariableTarget.Machine);
                if (string.IsNullOrWhiteSpace(raw) && OperatingSystem.IsWindows())
                    raw = Environment.GetEnvironmentVariable("FUNKY_MYSQL_CONNECTION", EnvironmentVariableTarget.User);
                if (string.IsNullOrWhiteSpace(raw))
                    return null;
                var builder = new MySqlConnectionStringBuilder(raw) { GuidFormat = MySqlGuidFormat.Char36, AllowUserVariables = true };
                if (string.IsNullOrEmpty(builder.Database))
                    builder.Database = "funky_db";
                return builder.ConnectionString;
            }
        }

        protected override void RequireDatabase()
        {
            var connectionString = ConnectionString;
            if (connectionString == null)
                Assert.Inconclusive("FUNKY_MYSQL_CONNECTION is not set.");
            try
            {
                using (var connection = new MySqlConnection(connectionString))
                    connection.Open();
            }
            catch (Exception ex) when (ex is MySqlException || ex is InvalidOperationException)
            {
                Assert.Inconclusive("MySQL test database not available: " + ex.GetType().Name);
            }
        }

        private static void Execute(string sql)
        {
            using (var connection = new MySqlConnection(ConnectionString))
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
            $"CREATE TABLE {Table} (id INT PRIMARY KEY, name VARCHAR(40) NULL, code INT NOT NULL, posted_at DATETIME NOT NULL, nullable_id INT NULL); " +
            $"INSERT INTO {Table} (id, name, code, posted_at, nullable_id) VALUES " +
            string.Join(", ", Rows.Select(r =>
                $"({r.Id}, '{r.Name}', {r.Code}, '{r.PostedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}', " +
                (r.NullableId.HasValue ? r.NullableId.Value.ToString(CultureInfo.InvariantCulture) : "NULL") + ")")) + ";");

        protected override void DropTable() => Execute($"DROP TABLE IF EXISTS {Table};");

        protected override OrmDataProvider CreateProvider() => new MySqlOrmDataProvider(ConnectionString);

        protected override string DatePartSql(string part, string column) => "EXTRACT(" + part.ToUpperInvariant() + " FROM " + column + ")";

        protected override string LengthSql(string column) => "CHAR_LENGTH(" + column + ")";

        protected override string CastToTextSql(string column) => "CAST(" + column + " AS CHAR)";
    }
}
