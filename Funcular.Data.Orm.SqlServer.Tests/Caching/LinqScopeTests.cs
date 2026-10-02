using Funcular.Data.Orm.Sqlite;
using Funcular.Data.Orm.Tests.Caching;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using static Funcular.Data.Orm.SqlServer.Tests.Caching.CacheScopeTestSupport;

namespace Funcular.Data.Orm.SqlServer.Tests.Caching
{
    /// <summary>AC7 on SQL Server (CI): P1 differs from P2 by <c>Application Name</c>.</summary>
    [TestClass]
    public class SqlServerLinqScopeTests : LinqScopeHarness
    {
        private static string P2ConnectionString => SqlServerTestConnectionString();

        protected override void RequireDatabase() =>
            RequireReachable(() => new SqlConnection(P2ConnectionString), "SQL Server");

        protected override void CreateLinqTable() => ExecuteSqlServer(P2ConnectionString,
            "IF OBJECT_ID(N'dbo.zz_psc_linq', N'U') IS NOT NULL DROP TABLE dbo.zz_psc_linq; " +
            "CREATE TABLE dbo.zz_psc_linq (id INT PRIMARY KEY, first_name NVARCHAR(50) NULL, last_name NVARCHAR(50) NULL, " +
            "middle_initial NVARCHAR(50) NULL, employer_id INT NOT NULL); " +
            "INSERT INTO dbo.zz_psc_linq (id, first_name, last_name, middle_initial, employer_id) VALUES " +
            "(1, 'a', 'x', 'x', 10), (2, 'b', 'y', 'y', 20), (3, 'c', 'z', 'w', 30);");

        protected override void DropLinqTable() => ExecuteSqlServer(P2ConnectionString,
            "IF OBJECT_ID(N'dbo.zz_psc_linq', N'U') IS NOT NULL DROP TABLE dbo.zz_psc_linq;");

        protected override OrmDataProvider CreateP2() => new SqlServerOrmDataProvider(P2ConnectionString);

        protected override OrmDataProvider CreateP1()
        {
            var builder = new SqlConnectionStringBuilder(P2ConnectionString) { ApplicationName = "zz_psc_p1" };
            return new SqlServerOrmDataProvider(builder.ConnectionString);
        }

        protected override OrmDataProvider CreateUniqueScopeProvider()
        {
            var builder = new SqlConnectionStringBuilder(P2ConnectionString) { ApplicationName = "zz_psc_" + Unique() };
            return new SqlServerOrmDataProvider(builder.ConnectionString);
        }

        protected override LinqCaches CachesOf(OrmDataProvider provider)
        {
            var p = (SqlServerOrmDataProvider)provider;
            return new LinqCaches
            {
                Tables = p.ScopeTableNames, Columns = p.ScopeColumnNames, Unmapped = p.ScopeUnmappedProperties,
                Mapped = p.ScopeMappedTypes, Mappers = p.ScopeEntityMappers
            };
        }
    }

    /// <summary>
    /// AC7 on SQLite, a temp file (CI): P1 differs from P2 by <c>Default Timeout</c>. Until D5, SQLite's SELECT list reads
    /// Core's base key, so the table also has <c>Id</c> and <c>FirstName</c> columns (plan §4.1, R4-4).
    /// </summary>
    [TestClass]
    public class SqliteLinqScopeTests : LinqScopeHarness
    {
        private string _path;

        protected override string DiscoveredFirstName => "FirstName";
        protected override string DiscoveredId => "Id";
        protected override string AverageFragment(string qualifiedColumn) => $"ROUND(AVG({qualifiedColumn}), 10)";

        protected override void CreateLinqTable()
        {
            _path = CreateSqliteDatabase(
                "DROP TABLE IF EXISTS zz_psc_linq; " +
                "CREATE TABLE zz_psc_linq (Id INTEGER PRIMARY KEY, FirstName TEXT, last_name TEXT, middle_initial TEXT, " +
                "employer_id INTEGER NOT NULL); " +
                "INSERT INTO zz_psc_linq (Id, FirstName, last_name, middle_initial, employer_id) VALUES " +
                "(1, 'a', 'x', 'x', 10), (2, 'b', 'y', 'y', 20), (3, 'c', 'z', 'w', 30);");
        }

        protected override void DropLinqTable() => DeleteSqliteDatabase(_path);

        protected override OrmDataProvider CreateP2() => new SqliteOrmDataProvider($"Data Source={_path}");

        protected override OrmDataProvider CreateP1()
        {
            var builder = new SqliteConnectionStringBuilder($"Data Source={_path}");
            builder.DefaultTimeout += 1;
            return new SqliteOrmDataProvider(builder.ConnectionString);
        }

        protected override OrmDataProvider CreateUniqueScopeProvider()
        {
            // The database file is new for each test, and the timeout differs from P1's and P2's.
            var builder = new SqliteConnectionStringBuilder($"Data Source={_path}");
            builder.DefaultTimeout += 2;
            return new SqliteOrmDataProvider(builder.ConnectionString);
        }

        protected override LinqCaches CachesOf(OrmDataProvider provider)
        {
            var p = (SqliteOrmDataProvider)provider;
            return new LinqCaches
            {
                Tables = p.ScopeTableNames, Columns = p.ScopeColumnNames, Unmapped = p.ScopeUnmappedProperties,
                Mapped = p.ScopeMappedTypes, Mappers = p.ScopeEntityMappers
            };
        }
    }
}
