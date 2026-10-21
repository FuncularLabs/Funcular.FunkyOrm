using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using Funcular.Data.Orm.MySql;
using Funcular.Data.Orm.PostgreSql;
using Funcular.Data.Orm.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Funcular.Data.Orm.SqlServer.Tests.Linq
{
    /// <summary>
    /// GitHub #16 (3.12.0, R1 in docs/plans/NULL_SEMANTICS_PLAN.md): the sort helper
    /// <c>q is IOrderedQueryable&lt;T&gt; o ? o.ThenBy(k) : q.OrderBy(k)</c> crashes on a composed query, and it crashes
    /// the same way on EF Core. EF Core's <c>Where(…)</c> returns an <c>EntityQueryable&lt;T&gt;</c>, which implements
    /// <c>IOrderedQueryable&lt;T&gt;</c>, and System.Linq's <c>Queryable.ThenBy</c> rejects a source expression typed
    /// <c>IQueryable&lt;T&gt;</c>. The owner's directive was to match EF, so the behaviour is pinned, not changed, and the
    /// documented check (<c>q.Expression.Type == typeof(IOrderedQueryable&lt;T&gt;)</c>) is pinned on both.
    /// <para>EF Core needs no database to build a query. FunkyORM discovers a type's columns when <c>Query&lt;T&gt;()</c>
    /// is called, so its side runs on a SQLite file of its own; the four providers' queryables are one template, which
    /// the reflection test pins.</para>
    /// </summary>
    [TestClass]
    public class SortHelperTests
    {
        [Table("zz_sort_row")]
        public class Row
        {
            [Key, Column("id")] public int Id { get; set; }
            [Column("name")] public string Name { get; set; }
        }

        private sealed class EfContext : DbContext
        {
            public DbSet<Row> Rows => Set<Row>();

            protected override void OnConfiguring(DbContextOptionsBuilder options) =>
                options.UseSqlServer("Server=(none);Database=zz_never_opened;Integrated Security=True;TrustServerCertificate=True");
        }

        private string _path;
        private SqliteOrmDataProvider _provider;

        [TestInitialize]
        public void CreateDatabase()
        {
            _path = Path.Combine(Path.GetTempPath(), $"funky_sort_helper_{Guid.NewGuid():N}.db");
            using (var connection = new SqliteConnection($"Data Source={_path}"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE TABLE zz_sort_row (id INTEGER PRIMARY KEY, name TEXT NULL); " +
                                          "INSERT INTO zz_sort_row (id, name) VALUES (1, 'b'), (2, 'a'), (3, 'a');";
                    command.ExecuteNonQuery();
                }
            }
            _provider = new SqliteOrmDataProvider($"Data Source={_path};Pooling=False");
        }

        [TestCleanup]
        public void DeleteDatabase()
        {
            _provider?.Dispose();
            SqliteConnection.ClearAllPools();
            if (_path != null && File.Exists(_path))
                File.Delete(_path);
        }

        /// <summary>The helper as it is commonly written.</summary>
        private static IOrderedQueryable<T> SortByTypeTest<T, TKey>(IQueryable<T> query, Expression<Func<T, TKey>> key) =>
            query is IOrderedQueryable<T> ordered ? ordered.ThenBy(key) : query.OrderBy(key);

        /// <summary>The check Advanced.md documents: whether the query's expression is ordered, not whether its object says it can be.</summary>
        private static IOrderedQueryable<T> SortByExpressionType<T, TKey>(IQueryable<T> query, Expression<Func<T, TKey>> key) =>
            query.Expression.Type == typeof(IOrderedQueryable<T>) ? ((IOrderedQueryable<T>)query).ThenBy(key) : query.OrderBy(key);

        [TestMethod]
        public void EveryProvidersQueryable_IsOrderedQueryable_AsEfCoresIs()
        {
            using (var ef = new EfContext())
                Assert.IsTrue(ef.Rows.Where(r => r.Id > 0) is IOrderedQueryable<Row>, "EF Core's composed query no longer claims to be ordered; revisit #16");
            foreach (var queryable in new[] { typeof(SqlQueryable<Row>), typeof(PostgreSqlQueryable<Row>), typeof(MySqlQueryable<Row>), typeof(SqliteQueryable<Row>) })
                Assert.IsTrue(typeof(IOrderedQueryable<Row>).IsAssignableFrom(queryable), queryable.Name + " no longer implements IOrderedQueryable; revisit #16");
            Assert.IsTrue(_provider.Query<Row>().Where(r => r.Id > 0) is IOrderedQueryable<Row>, "a composed SQLite query no longer claims to be ordered");
        }

        [TestMethod]
        public void IsOrderedQueryableHelper_ThrowsAsEfCore()
        {
            string efMessage;
            using (var ef = new EfContext())
                efMessage = Assert.ThrowsException<ArgumentException>(() => SortByTypeTest(ef.Rows.Where(r => r.Id > 0), r => r.Name)).Message;
            var funky = Assert.ThrowsException<ArgumentException>(() => SortByTypeTest(_provider.Query<Row>().Where(r => r.Id > 0), r => r.Name));
            Assert.AreEqual(efMessage, funky.Message, "System.Linq's message differs from EF Core's");
            StringAssert.Contains(funky.Message, "cannot be used for parameter of type 'System.Linq.IOrderedQueryable");
        }

        [TestMethod]
        public void ExpressionTypeCheck_PicksOrderByThenBy()
        {
            using (var ef = new EfContext())
            {
                Assert.AreEqual("OrderBy", Outermost(SortByExpressionType(ef.Rows, r => r.Name)), "EF Core root");
                Assert.AreEqual("OrderBy", Outermost(SortByExpressionType(ef.Rows.Where(r => r.Id > 0), r => r.Name)), "EF Core after Where");
                Assert.AreEqual("ThenBy", Outermost(SortByExpressionType(ef.Rows.OrderBy(r => r.Id), r => r.Name)), "EF Core after OrderBy");
            }
            Assert.AreEqual("OrderBy", Outermost(SortByExpressionType(_provider.Query<Row>(), r => r.Name)), "root");
            Assert.AreEqual("OrderBy", Outermost(SortByExpressionType(_provider.Query<Row>().Where(r => r.Id > 0), r => r.Name)), "after Where");
            Assert.AreEqual("ThenBy", Outermost(SortByExpressionType(_provider.Query<Row>().OrderBy(r => r.Id), r => r.Name)), "after OrderBy");
        }

        /// <summary>The documented check runs: rows come back in the helper's order, on the root and after Where.</summary>
        [TestMethod]
        public void ExpressionTypeCheck_Runs()
        {
            CollectionAssert.AreEqual(new[] { 2, 3, 1 },
                SortByExpressionType(SortByExpressionType(_provider.Query<Row>(), r => r.Name), r => r.Id).Select(r => r.Id).ToList());
            CollectionAssert.AreEqual(new[] { 3, 1 },
                SortByExpressionType(SortByExpressionType(_provider.Query<Row>().Where(r => r.Id > 2 || r.Name == "b"), r => r.Name), r => r.Id)
                    .Select(r => r.Id).ToList());
        }

        private static string Outermost(IQueryable query) => ((MethodCallExpression)query.Expression).Method.Name;
    }
}
