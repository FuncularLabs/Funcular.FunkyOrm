using System;
using System.Data.Common;
using Funcular.Data.Orm.Tests.Aggregates;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql.Tests.Aggregates
{
    /// <summary>The aggregate rows (FD-04) on MySQL (FUNKY_MYSQL_CONNECTION). MySQL has no offset-aware type.</summary>
    [TestClass]
    public class MySqlAggregateTests : AggregateHarness
    {
        private static string ConnectionString
        {
            get
            {
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

        protected override DbConnection OpenConnection()
        {
            var connection = new MySqlConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        protected override string Ddl =>
            $"CREATE TABLE {Table} (id INT PRIMARY KEY, i32 INT NOT NULL, i32n INT NULL, i64 BIGINT NOT NULL, i64n BIGINT NULL, " +
            "f32 FLOAT NOT NULL, f32n FLOAT NULL, f64 DOUBLE NOT NULL, f64n DOUBLE NULL, amount DECIMAL(10,2) NOT NULL, amountn DECIMAL(10,2) NULL, " +
            "posted DATETIME(6) NOT NULL, postedn DATETIME(6) NULL, name VARCHAR(20) NULL, uid CHAR(36) NOT NULL, " +
            "stamp DATETIME(6) NOT NULL, span TIME NOT NULL, status INT NOT NULL)";

        protected override object ToDb(string column, object value)
        {
            switch (column)
            {
                case "uid": return ((Guid)value).ToString("D");
                case "stamp": return ((DateTimeOffset)value).UtcDateTime;
                default: return value;
            }
        }

        protected override OrmDataProvider CreateProvider() => new MySqlOrmDataProvider(ConnectionString);

        protected override bool DateTimeOffsetOrdersByInstant => false;

        // Lower-case Guid text orders as .NET does, but the owner's default is Rejected (storage-format dependent).
        protected override bool GuidDatabaseOrderMatchesLinq => true;
    }
}
