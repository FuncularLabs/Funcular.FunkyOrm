using System;
using Funcular.Data.Orm.Sqlite;
using Funcular.Data.Orm.Tests.DeleteGuard;
using Microsoft.Data.SqlClient;
using static Funcular.Data.Orm.SqlServer.Tests.Caching.CacheScopeTestSupport;

namespace Funcular.Data.Orm.SqlServer.Tests.DeleteGuard
{
    /// <summary>The delete-guard harness on SQL Server (CI).</summary>
    [TestClass]
    public class SqlServerDeleteGuardTests : DeleteGuardHarness
    {
        private static string ConnectionString => SqlServerTestConnectionString();

        protected override void RequireDatabase() =>
            RequireReachable(() => new SqlConnection(ConnectionString), "SQL Server");

        protected override void CreateTables() => ExecuteSqlServer(ConnectionString,
            $"IF OBJECT_ID(N'dbo.{RowTable}', N'U') IS NOT NULL DROP TABLE dbo.{RowTable}; " +
            $"IF OBJECT_ID(N'dbo.{TrueUpTable}', N'U') IS NOT NULL DROP TABLE dbo.{TrueUpTable}; " +
            $"CREATE TABLE dbo.{RowTable} (id INT PRIMARY KEY, first_name NVARCHAR(20) NULL, true_up INT NOT NULL, big INT NULL, archived BIT NOT NULL DEFAULT 0); " +
            $"INSERT INTO dbo.{RowTable} (id, first_name, true_up) VALUES (1, 'a', 1), (2, 'b', 0), (3, NULL, 0); " +
            $"CREATE TABLE dbo.{TrueUpTable} (id INT PRIMARY KEY, amount INT NOT NULL); " +
            $"INSERT INTO dbo.{TrueUpTable} (id, amount) VALUES (1, 5), (2, 7);");

        protected override void DropTables() => ExecuteSqlServer(ConnectionString,
            $"IF OBJECT_ID(N'dbo.{RowTable}', N'U') IS NOT NULL DROP TABLE dbo.{RowTable}; " +
            $"IF OBJECT_ID(N'dbo.{TrueUpTable}', N'U') IS NOT NULL DROP TABLE dbo.{TrueUpTable};");

        protected override void DropMissingTable() => ExecuteSqlServer(ConnectionString,
            $"IF OBJECT_ID(N'dbo.{MissingTable}', N'U') IS NOT NULL DROP TABLE dbo.{MissingTable};");

        protected override OrmDataProvider CreateProvider() => new SqlServerOrmDataProvider(ConnectionString);

        protected override bool IsDiscovered(OrmDataProvider provider, Type type) =>
            ((SqlServerOrmDataProvider)provider).ScopeMappedTypes.Contains(type);
    }

    /// <summary>The delete-guard harness on SQLite, one temp file per row (CI).</summary>
    [TestClass]
    public class SqliteDeleteGuardTests : DeleteGuardHarness
    {
        private string _path;

        protected override void CreateTables()
        {
            DropTables();
            _path = CreateSqliteDatabase(
                $"CREATE TABLE {RowTable} (id INTEGER PRIMARY KEY, first_name TEXT NULL, true_up INTEGER NOT NULL, big INTEGER NULL, archived INTEGER NOT NULL DEFAULT 0); " +
                $"INSERT INTO {RowTable} (id, first_name, true_up) VALUES (1, 'a', 1), (2, 'b', 0), (3, NULL, 0); " +
                $"CREATE TABLE {TrueUpTable} (id INTEGER PRIMARY KEY, amount INTEGER NOT NULL); " +
                $"INSERT INTO {TrueUpTable} (id, amount) VALUES (1, 5), (2, 7);");
        }

        protected override void DropTables()
        {
            DeleteSqliteDatabase(_path);
            _path = null;
        }

        // The temp file is new for each row, so the missing table never exists in it.
        protected override void DropMissingTable() { }

        protected override OrmDataProvider CreateProvider() => new SqliteOrmDataProvider($"Data Source={_path}");

        protected override bool IsDiscovered(OrmDataProvider provider, Type type) =>
            ((SqliteOrmDataProvider)provider).ScopeMappedTypes.Contains(type);
    }
}
