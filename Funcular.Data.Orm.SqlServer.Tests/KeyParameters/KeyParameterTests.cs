using System;
using Funcular.Data.Orm.Sqlite;
using Funcular.Data.Orm.Tests.KeyParameters;
using Microsoft.Data.SqlClient;
using static Funcular.Data.Orm.SqlServer.Tests.Caching.CacheScopeTestSupport;

namespace Funcular.Data.Orm.SqlServer.Tests.KeyParameters
{
    /// <summary>The key-parameter harness on SQL Server (CI).</summary>
    [TestClass]
    public class SqlServerKeyParameterTests : KeyParameterHarness
    {
        private static string ConnectionString => SqlServerTestConnectionString();

        private const string Drop =
            "IF OBJECT_ID(N'dbo." + TextTable + "', N'U') IS NOT NULL DROP TABLE dbo." + TextTable + "; " +
            "IF OBJECT_ID(N'dbo." + GuidTable + "', N'U') IS NOT NULL DROP TABLE dbo." + GuidTable + "; " +
            "IF OBJECT_ID(N'dbo." + NumTable + "', N'U') IS NOT NULL DROP TABLE dbo." + NumTable + "; " +
            "IF OBJECT_ID(N'dbo." + PkTable + "', N'U') IS NOT NULL DROP TABLE dbo." + PkTable + "; ";

        protected override void RequireDatabase() =>
            RequireReachable(() => new SqlConnection(ConnectionString), "SQL Server");

        protected override void CreateTables() => ExecuteSqlServer(ConnectionString, Drop +
            $"CREATE TABLE dbo.{TextTable} (name NVARCHAR(100) NOT NULL PRIMARY KEY, val INT NOT NULL); " +
            $"CREATE TABLE dbo.{GuidTable} (id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, val INT NOT NULL); " +
            $"CREATE TABLE dbo.{NumTable} (id INT NOT NULL PRIMARY KEY, label NVARCHAR(20) NOT NULL); " +
            $"INSERT INTO dbo.{NumTable} (id, label) VALUES (1, N'one'), (2, N'two'); " +
            $"CREATE TABLE dbo.{PkTable} (pk NVARCHAR(100) NOT NULL PRIMARY KEY, val INT NOT NULL);");

        protected override void DropTables() => ExecuteSqlServer(ConnectionString, Drop);

        protected override OrmDataProvider CreateProvider() => new SqlServerOrmDataProvider(ConnectionString);
    }

    /// <summary>The key-parameter harness on SQLite, one temp file per row (CI).</summary>
    [TestClass]
    public class SqliteKeyParameterTests : KeyParameterHarness
    {
        private string _path;

        protected override void CreateTables()
        {
            DropTables();
            _path = CreateSqliteDatabase(
                $"CREATE TABLE {TextTable} (name TEXT NOT NULL PRIMARY KEY, val INTEGER NOT NULL); " +
                $"CREATE TABLE {GuidTable} (id TEXT NOT NULL PRIMARY KEY, val INTEGER NOT NULL); " +
                $"CREATE TABLE {NumTable} (id INTEGER NOT NULL PRIMARY KEY, label TEXT NOT NULL); " +
                $"INSERT INTO {NumTable} (id, label) VALUES (1, 'one'), (2, 'two'); " +
                $"CREATE TABLE {PkTable} (pk TEXT NOT NULL PRIMARY KEY, val INTEGER NOT NULL);");
        }

        protected override void DropTables()
        {
            DeleteSqliteDatabase(_path);
            _path = null;
        }

        protected override OrmDataProvider CreateProvider() => new SqliteOrmDataProvider($"Data Source={_path}");
    }
}
