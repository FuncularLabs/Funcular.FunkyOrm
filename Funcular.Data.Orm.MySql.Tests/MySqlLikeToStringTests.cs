using System;
using System.Data.Common;
using System.Reflection;
using Funcular.Data.Orm.Tests.LikeToString;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>
    /// The LIKE-escaping and column-ToString harness on MySQL (docs/plans/LIKE_ESCAPING_TOSTRING_PLAN.md, §4.1). Table
    /// names are lower case, because Linux MySQL compares them case-sensitively.
    /// </summary>
    [TestClass]
    public class MySqlLikeToStringTests : LikeToStringHarness
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

        protected override DbConnection OpenConnection()
        {
            var connection = new MySqlConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        protected override string RowDdl =>
            $"CREATE TABLE {RowTable} (id INT PRIMARY KEY, name VARCHAR(40) NULL, code INT NOT NULL, ncode INT NULL, namount DECIMAL(10,2) NULL, " +
            "big BIGINT NOT NULL, amount DECIMAL(10,2) NOT NULL, flag TINYINT(1) NOT NULL)";

        protected override string NumDdl =>
            $"CREATE TABLE {NumTable} (id INT PRIMARY KEY, i8 TINYINT, u8 TINYINT UNSIGNED, i16 SMALLINT, " +
            "u16 SMALLINT UNSIGNED, i32 INT, u32 INT UNSIGNED, i64 BIGINT, u64 BIGINT UNSIGNED)";

        protected override object ToDb(string column, object value) =>
            column == "Flag" ? ((bool)value ? 1 : 0) : value;

        protected override OrmDataProvider CreateProvider() => new MySqlOrmDataProvider(ConnectionString);

        protected override Assembly ProviderAssembly => typeof(MySqlOrmDataProvider).Assembly;
    }
}
