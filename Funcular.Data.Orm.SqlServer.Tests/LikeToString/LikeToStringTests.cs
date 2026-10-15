using System;
using System.Data.Common;
using System.Reflection;
using Funcular.Data.Orm.Sqlite;
using Funcular.Data.Orm.Tests.LikeToString;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using static Funcular.Data.Orm.SqlServer.Tests.Caching.CacheScopeTestSupport;

namespace Funcular.Data.Orm.SqlServer.Tests.LikeToString
{
    /// <summary>The LIKE-escaping and column-ToString harness on SQL Server (CI).</summary>
    [TestClass]
    public class SqlServerLikeToStringTests : LikeToStringHarness
    {
        private static string ConnectionString => SqlServerTestConnectionString();

        protected override void RequireDatabase() =>
            RequireReachable(() => new SqlConnection(ConnectionString), "SQL Server");

        protected override DbConnection OpenConnection()
        {
            var connection = new SqlConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        protected override string RowDdl =>
            $"CREATE TABLE {RowTable} (id INT PRIMARY KEY, name NVARCHAR(40) NULL, code INT NOT NULL, ncode INT NULL, namount DECIMAL(10,2) NULL, " +
            "big BIGINT NOT NULL, amount DECIMAL(10,2) NOT NULL, flag BIT NOT NULL)";

        protected override string NumDdl =>
            $"CREATE TABLE {NumTable} (id INT PRIMARY KEY, i8 SMALLINT, u8 TINYINT, i16 SMALLINT, u16 INT, i32 INT, " +
            "u32 BIGINT, i64 BIGINT, u64 DECIMAL(20,0))";

        protected override object ToDb(string column, object value) => column switch
        {
            "I8" => (short)(sbyte)value,
            "U16" => (int)(ushort)value,
            "U32" => (long)(uint)value,
            "U64" => (decimal)(ulong)value,
            _ => value
        };

        protected override OrmDataProvider CreateProvider() => new SqlServerOrmDataProvider(ConnectionString);

        protected override Assembly ProviderAssembly => typeof(SqlServerOrmDataProvider).Assembly;
    }

    /// <summary>The LIKE-escaping and column-ToString harness on SQLite, one temp file per row (CI).</summary>
    [TestClass]
    public class SqliteLikeToStringTests : LikeToStringHarness
    {
        private string _path;

        protected override void PrepareDatabase()
        {
            DeleteSqliteDatabase(_path);
            _path = CreateSqliteDatabase("SELECT 1;");
        }

        protected override void DropTables()
        {
            DeleteSqliteDatabase(_path);
            _path = null;
        }

        protected override DbConnection OpenConnection()
        {
            var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
            connection.Open();
            return connection;
        }

        protected override string RowDdl =>
            $"CREATE TABLE {RowTable} (id INTEGER PRIMARY KEY, name TEXT NULL, code INTEGER NOT NULL, ncode INTEGER NULL, namount NUMERIC NULL, " +
            "big INTEGER NOT NULL, amount NUMERIC NOT NULL, flag INTEGER NOT NULL)";

        protected override string NumDdl =>
            $"CREATE TABLE {NumTable} (id INTEGER PRIMARY KEY, i8 INTEGER, u8 INTEGER, i16 INTEGER, u16 INTEGER, " +
            "i32 INTEGER, u32 INTEGER, i64 INTEGER, u64 INTEGER)";

        protected override object ToDb(string column, object value) =>
            column == "Flag" ? ((bool)value ? 1 : 0) : Convert.ToInt64(value);

        protected override ulong U64Max => long.MaxValue;

        protected override OrmDataProvider CreateProvider() => new SqliteOrmDataProvider($"Data Source={_path}");

        protected override Assembly ProviderAssembly => typeof(SqliteOrmDataProvider).Assembly;
    }
}
