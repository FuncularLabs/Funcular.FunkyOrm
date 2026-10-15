using System;
using System.Data.Common;
using Funcular.Data.Orm.Tests.Aggregates;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Tests.Aggregates
{
    /// <summary>The aggregate rows (FD-04) on PostgreSQL. PostgreSQL has no MIN/MAX over <c>uuid</c> (42883), so Guid Min/Max is rejected whatever the option says.</summary>
    [TestClass]
    public class PostgreSqlAggregateTests : AggregateHarness
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

        protected override DbConnection OpenConnection()
        {
            var connection = new NpgsqlConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        protected override string Ddl =>
            $"CREATE TABLE {Table} (id INT PRIMARY KEY, i32 INT NOT NULL, i32n INT NULL, i64 BIGINT NOT NULL, i64n BIGINT NULL, " +
            "f32 REAL NOT NULL, f32n REAL NULL, f64 DOUBLE PRECISION NOT NULL, f64n DOUBLE PRECISION NULL, amount NUMERIC(10,2) NOT NULL, amountn NUMERIC(10,2) NULL, " +
            "posted TIMESTAMP NOT NULL, postedn TIMESTAMP NULL, name VARCHAR(20) NULL, uid UUID NOT NULL, " +
            "stamp TIMESTAMPTZ NOT NULL, span INTERVAL NOT NULL, status INT NOT NULL)";

        protected override object ToDb(string column, object value)
        {
            switch (column)
            {
                // Npgsql binds timestamptz from a UTC DateTime.
                case "stamp": return ((DateTimeOffset)value).UtcDateTime;
                default: return value;
            }
        }

        protected override OrmDataProvider CreateProvider() => new PostgreSqlOrmDataProvider(ConnectionString);

        protected override bool DateTimeOffsetOrdersByInstant => true;

        protected override bool GuidAggregateSupportedByDatabase => false;
    }
}
