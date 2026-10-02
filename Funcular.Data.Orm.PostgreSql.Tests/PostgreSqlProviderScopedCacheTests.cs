using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Funcular.Data.Orm.Tests.Caching;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    /// <summary>
    /// The PostgreSQL rows of the provider-scoped caches plan (docs/plans/PROVIDER_SCOPED_CACHES_PLAN.md, §4.1, D10): the
    /// AC7 LINQ rows (P1 differs from P2 by <c>Application Name</c>) and the AC5 mapper row through a real read of
    /// <c>person</c>. Their workflow doesn't run on PRs into development/**, so they gate through a local run (§4.4).
    /// </summary>
    [TestClass]
    public class PostgreSqlProviderScopedCacheTests : LinqScopeHarness
    {
        private static string P2ConnectionString => PostgreSqlTestConnection.Resolve();

        private static string P1ConnectionString =>
            new NpgsqlConnectionStringBuilder(P2ConnectionString) { ApplicationName = "zz_psc_p1" }.ConnectionString;

        protected override void RequireDatabase()
        {
            try
            {
                using var connection = new NpgsqlConnection(P2ConnectionString);
                connection.Open();
            }
            catch (Exception ex)
            {
                Assert.Inconclusive($"PostgreSQL test database is unreachable ({ex.GetType().Name}).");
            }
        }

        private static void Execute(string sql)
        {
            using var connection = new NpgsqlConnection(P2ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        protected override void CreateLinqTable() => Execute(
            "DROP TABLE IF EXISTS zz_psc_linq; " +
            "CREATE TABLE zz_psc_linq (id INT PRIMARY KEY, first_name VARCHAR(50) NULL, last_name VARCHAR(50) NULL, " +
            "middle_initial VARCHAR(50) NULL, employer_id INT NOT NULL); " +
            "INSERT INTO zz_psc_linq (id, first_name, last_name, middle_initial, employer_id) VALUES " +
            "(1, 'a', 'x', 'x', 10), (2, 'b', 'y', 'y', 20), (3, 'c', 'z', 'w', 30);");

        protected override void DropLinqTable() => Execute("DROP TABLE IF EXISTS zz_psc_linq;");

        protected override OrmDataProvider CreateP2() => new PostgreSqlOrmDataProvider(P2ConnectionString);

        protected override OrmDataProvider CreateP1() => new PostgreSqlOrmDataProvider(P1ConnectionString);

        protected override OrmDataProvider CreateUniqueScopeProvider() => new PostgreSqlOrmDataProvider(
            new NpgsqlConnectionStringBuilder(P2ConnectionString) { ApplicationName = "zz_psc_" + Guid.NewGuid().ToString("N") }
                .ConnectionString);

        protected override LinqCaches CachesOf(OrmDataProvider provider)
        {
            var p = (PostgreSqlOrmDataProvider)provider;
            return new LinqCaches
            {
                Tables = p.ScopeTableNames, Columns = p.ScopeColumnNames, Unmapped = p.ScopeUnmappedProperties,
                Mapped = p.ScopeMappedTypes
            };
        }

        [Table("person")]
        public class PscMapperPerson
        {
            [Key] public int Id { get; set; }
            public string FirstName { get; set; }
        }

        [TestMethod]
        public void MapperCache_IsScoped_ThroughARealRead()
        {
            RequireDatabase();
            using var a = new PostgreSqlOrmDataProvider(P2ConnectionString);
            using var b = new PostgreSqlOrmDataProvider(P1ConnectionString);
            var prefix = typeof(PscMapperPerson).FullName + "|";

            Assert.AreEqual(1, a.Query<PscMapperPerson>().Take(1).ToList().Count, "P_A reads person (it needs a row)");
            Assert.IsTrue(a.ScopeEntityMappers.Keys.Any(k => k.StartsWith(prefix)), "A's mapper cache has no entry after P_A's read");
            Assert.IsFalse(b.ScopeEntityMappers.Keys.Any(k => k.StartsWith(prefix)), "B's mapper cache has an entry before P_B has read");

            Assert.AreEqual(1, b.Query<PscMapperPerson>().Take(1).ToList().Count, "P_B reads person");
            var entryA = a.ScopeEntityMappers.Single(e => e.Key.StartsWith(prefix)).Value;
            var entryB = b.ScopeEntityMappers.Single(e => e.Key.StartsWith(prefix)).Value;
            Assert.AreNotSame(entryA, entryB, "B has its own mapper");
        }
    }
}
