using System;
using System.Data.Common;
using System.Reflection;
using Funcular.Data.Orm.Tests.LikeToString;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    /// <summary>The LIKE-escaping and column-ToString harness on PostgreSQL (docs/plans/LIKE_ESCAPING_TOSTRING_PLAN.md, §4.1).</summary>
    [TestClass]
    public class PostgreSqlLikeToStringTests : LikeToStringHarness
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

        protected override DbConnection OpenConnection()
        {
            var connection = new NpgsqlConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        protected override string RowDdl =>
            $"CREATE TABLE {RowTable} (id INT PRIMARY KEY, name VARCHAR(40) NULL, code INT NOT NULL, ncode INT NULL, " +
            "big BIGINT NOT NULL, amount NUMERIC(10,2) NOT NULL, flag BOOLEAN NOT NULL)";

        protected override string NumDdl =>
            $"CREATE TABLE {NumTable} (id INT PRIMARY KEY, i8 SMALLINT, u8 SMALLINT, i16 SMALLINT, u16 INT, i32 INT, " +
            "u32 BIGINT, i64 BIGINT, u64 NUMERIC(20,0))";

        protected override object ToDb(string column, object value) => column switch
        {
            "I8" => (short)(sbyte)value,
            "U8" => (short)(byte)value,
            "U16" => (int)(ushort)value,
            "U32" => (long)(uint)value,
            "U64" => (decimal)(ulong)value,
            _ => value
        };

        protected override OrmDataProvider CreateProvider() => new PostgreSqlOrmDataProvider(ConnectionString);

        protected override Assembly ProviderAssembly => typeof(PostgreSqlOrmDataProvider).Assembly;
    }
}
