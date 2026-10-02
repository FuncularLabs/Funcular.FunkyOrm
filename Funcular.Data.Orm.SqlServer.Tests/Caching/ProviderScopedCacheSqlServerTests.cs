using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Funcular.Data.Orm.PostgreSql;
using Funcular.Data.Orm.PostgreSql.Tests;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.User;
using Microsoft.Data.SqlClient;
using Npgsql;
using static Funcular.Data.Orm.SqlServer.Tests.Caching.CacheScopeTestSupport;

namespace Funcular.Data.Orm.SqlServer.Tests.Caching
{
    /// <summary>
    /// The rows of the provider-scoped caches plan (docs/plans/PROVIDER_SCOPED_CACHES_PLAN.md, §4.1) that read the SQL
    /// Server test database: the reported repro (with PostgreSQL) and the SQL Server mapper row. Both only read.
    /// </summary>
    [TestClass]
    public class ProviderScopedCacheSqlServerTests
    {
        [TestMethod]
        public void SqlServerThenPostgreSql_SameEntity_BothQueriesRun()
        {
            // The reported repro: SQL Server's bracketed names reached PostgreSQL (42601). Needs both test databases.
            var sqlServerConnection = SqlServerTestConnectionString();
            var postgreSqlConnection = PostgreSqlTestConnection.Resolve();
            RequireReachable(() => new SqlConnection(sqlServerConnection), "SQL Server");
            RequireReachable(() => new NpgsqlConnection(postgreSqlConnection), "PostgreSQL");

            using (var sqlServer = new SqlServerOrmDataProvider(sqlServerConnection))
                Assert.IsTrue(sqlServer.Query<User>().Count() >= 0, "SQL Server");
            // Unpaged, as in the repro: unordered paging adds ORDER BY id, and "User" has no id column (3.10 plan §8).
            using (var postgreSql = new PostgreSqlOrmDataProvider(postgreSqlConnection))
                Assert.IsNotNull(postgreSql.Query<User>().ToList(), "PostgreSQL");
        }

        [Table("person")]
        public class PscMapperPerson
        {
            [Key] public int Id { get; set; }
            public string FirstName { get; set; }
        }

        [TestMethod]
        public void SameProviderType_DifferentDialectType_DoNotShareMappers()
        {
            var connectionString = SqlServerTestConnectionString();
            RequireReachable(() => new SqlConnection(connectionString), "SQL Server");

            int id;
            string firstName;
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT TOP 1 id, first_name FROM person WHERE first_name IS NOT NULL ORDER BY id";
                using var reader = command.ExecuteReader();
                Assert.IsTrue(reader.Read(), "person has no row with a first name; this row reads one");
                id = reader.GetInt32(0);
                firstName = reader.GetString(1);
            }

            // A double-quoting dialect reads person first and builds the mapper with its quoted names.
            using (var quoted = new SqlServerOrmDataProvider(connectionString,
                       dialect: new AlwaysQuoteDialect(new SqlServerDialect(), "\"", "\"")))
                Assert.AreEqual(1, quoted.Query<PscMapperPerson>().Where(x => x.Id == id).ToList().Count, "the quoting provider reads the row");

            // Then the default provider must read the right Id and FirstName.
            using var standard = new SqlServerOrmDataProvider(connectionString);
            var row = standard.Query<PscMapperPerson>().Where(x => x.Id == id).ToList().Single();
            Assert.AreEqual($"{id}:{firstName}", $"{row.Id}:{row.FirstName}",
                "the default provider read through another dialect type's mapper");
        }
    }
}
