using System;
using System.Globalization;
using System.Linq;
using Funcular.Data.Orm.Tests.Parity;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer.Tests.Parity
{
    /// <summary>The WHERE parity rows (D-001) on SQL Server (FUNKY_CONNECTION).</summary>
    [TestClass]
    public class SqlServerWhereParityTests : WhereParityHarness
    {
        private static string ConnectionString =>
            Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
            "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=funky_db;Integrated Security=True;";

        protected override void RequireDatabase()
        {
            try
            {
                using (var connection = new SqlConnection(ConnectionString))
                    connection.Open();
            }
            catch (Exception ex) when (ex is SqlException || ex is InvalidOperationException)
            {
                Assert.Inconclusive("SQL Server test database not available: " + ex.GetType().Name);
            }
        }

        private static void Execute(string sql)
        {
            using (var connection = new SqlConnection(ConnectionString))
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
            $"IF OBJECT_ID(N'dbo.{Table}', N'U') IS NOT NULL DROP TABLE dbo.{Table}; " +
            $"CREATE TABLE dbo.{Table} (id INT PRIMARY KEY, name NVARCHAR(40) NULL, code INT NOT NULL, posted_at DATETIME2 NOT NULL, nullable_id INT NULL); " +
            $"INSERT INTO dbo.{Table} (id, name, code, posted_at, nullable_id) VALUES " +
            string.Join(", ", Rows.Select(r =>
                $"({r.Id}, N'{r.Name}', {r.Code}, '{r.PostedAt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}', " +
                (r.NullableId.HasValue ? r.NullableId.Value.ToString(CultureInfo.InvariantCulture) : "NULL") + ")")) + ";");

        protected override void DropTable() => Execute($"IF OBJECT_ID(N'dbo.{Table}', N'U') IS NOT NULL DROP TABLE dbo.{Table};");

        protected override OrmDataProvider CreateProvider() => new SqlServerOrmDataProvider(ConnectionString);

        protected override string DatePartSql(string part, string column) => part.ToUpperInvariant() + "(" + column + ")";

        // LEN alone ignores trailing spaces, which C#'s Length counts (the translator's remark).
        protected override string LengthSql(string column) => "(LEN(" + column + " + N'x') - 1)";

        protected override string CastToTextSql(string column) => "CAST(" + column + " AS NVARCHAR(20))";
    }
}
