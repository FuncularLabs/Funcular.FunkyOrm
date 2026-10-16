using System;
using System.Data.Common;
using Funcular.Data.Orm.Tests.Aggregates;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer.Tests.Aggregates
{
    /// <summary>The aggregate rows (FD-04) on SQL Server (FUNKY_CONNECTION).</summary>
    [TestClass]
    public class SqlServerAggregateTests : AggregateHarness
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

        protected override DbConnection OpenConnection()
        {
            var connection = new SqlConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        protected override void DropTable()
        {
            using (var connection = OpenConnection())
                Execute(connection, $"IF OBJECT_ID(N'dbo.{Table}', N'U') IS NOT NULL DROP TABLE dbo.{Table};");
        }

        protected override string Ddl =>
            $"CREATE TABLE dbo.{Table} (id INT PRIMARY KEY, i32 INT NOT NULL, i32n INT NULL, i64 BIGINT NOT NULL, i64n BIGINT NULL, " +
            "f32 REAL NOT NULL, f32n REAL NULL, f64 FLOAT NOT NULL, f64n FLOAT NULL, amount DECIMAL(10,2) NOT NULL, amountn DECIMAL(10,2) NULL, " +
            "posted DATETIME2 NOT NULL, postedn DATETIME2 NULL, name NVARCHAR(20) NULL, uid UNIQUEIDENTIFIER NOT NULL, " +
            "stamp DATETIMEOFFSET NOT NULL, span TIME NOT NULL, status INT NOT NULL)";

        protected override OrmDataProvider CreateProvider() => new SqlServerOrmDataProvider(ConnectionString);

        protected override bool DateTimeOffsetOrdersByInstant => true;
    }
}
