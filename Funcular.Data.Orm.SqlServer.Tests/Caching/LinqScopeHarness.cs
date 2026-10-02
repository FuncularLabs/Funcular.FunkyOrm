using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using Funcular.Data.Orm;

namespace Funcular.Data.Orm.Tests.Caching
{
    /// <summary>
    /// The AC7 rows of the provider-scoped caches plan (docs/plans/PROVIDER_SCOPED_CACHES_PLAN.md, §4.1): every LINQ read
    /// site reads its own instance's scope, and never another's. One derived [TestClass] per provider supplies the
    /// table, the two instances and the provider's seam accessors. This file is compiled into SqlServer.Tests (SQL
    /// Server, SQLite), PostgreSql.Tests and MySql.Tests.
    /// <para>
    /// The rows run on <c>zz_psc_linq</c>, which each test creates (drop if exists → create → seed) and drops in
    /// <c>finally</c>, outside any provider transaction. Seeds: <c>(id, first_name, last_name, middle_initial,
    /// employer_id)</c> = (1, a, x, x, 10), (2, b, y, y, 20), (3, c, z, w, 30). P2 is the plain instance; P1 is the
    /// same database under another identity.
    /// </para>
    /// </summary>
    public abstract class LinqScopeHarness
    {
        public const string Table = "zz_psc_linq";

        /// <summary>The dedicated AC7 type: its mappings are planted, so its SQL shows whose scope was read.</summary>
        [Table(Table)]
        public class LinqProbe
        {
            public int Id { get; set; }
            public string FirstName { get; set; }
        }

        /// <summary>FirstName is planted as unmapped in P2's scope.</summary>
        [Table(Table)]
        public class LinqUnmappedA
        {
            public int Id { get; set; }
            public string FirstName { get; set; }
        }

        /// <summary>Id is planted as unmapped in P2's scope.</summary>
        [Table(Table)]
        public class LinqUnmappedB
        {
            public int Id { get; set; }
            public string FirstName { get; set; }
        }

        /// <summary>The caches a provider instance reads, through that provider's internal scope accessors.</summary>
        protected sealed class LinqCaches
        {
            public ConcurrentDictionary<Type, string> Tables { get; set; }
            public ConcurrentDictionary<string, string> Columns { get; set; }
            public ConcurrentDictionary<Type, ICollection<PropertyInfo>> Unmapped { get; set; }
            public ICollection<Type> Mapped { get; set; }
        }

        /// <summary>Drops <c>zz_psc_linq</c> if it exists, creates it and seeds the three rows.</summary>
        protected abstract void CreateLinqTable();

        /// <summary>Drops <c>zz_psc_linq</c> (or deletes its database file).</summary>
        protected abstract void DropLinqTable();

        /// <summary>The plain instance on the test database.</summary>
        protected abstract OrmDataProvider CreateP2();

        /// <summary>The same database under another identity.</summary>
        protected abstract OrmDataProvider CreateP1();

        protected abstract LinqCaches CachesOf(OrmDataProvider provider);

        /// <summary>The column discovery finds for FirstName and Id in this provider's <c>zz_psc_linq</c>.</summary>
        protected virtual string DiscoveredFirstName => "first_name";
        protected virtual string DiscoveredId => "id";

        /// <summary>The aggregate fragment for Average of a qualified column.</summary>
        protected virtual string AverageFragment(string qualifiedColumn) => $"AVG({qualifiedColumn})";

        /// <summary>Marks the test inconclusive when the provider's test database is unavailable.</summary>
        protected virtual void RequireDatabase() { }

        #region Rows

        [TestMethod]
        public void LinqSites_ReadTheirOwnScope()
        {
            RequireDatabase();
            try
            {
                CreateLinqTable();
                using var p2 = CreateP2();
                PlantLinqProbe(p2, "last_name", "employer_id");
                AssertEverySiteReads(p2, "P2", "last_name", "employer_id");
            }
            finally
            {
                DropLinqTable();
            }
        }

        [TestMethod]
        public void LinqSites_NeverReadAnotherScope()
        {
            RequireDatabase();
            try
            {
                CreateLinqTable();
                using var p2 = CreateP2();
                PlantLinqProbe(p2, "last_name", "employer_id");

                using var p1 = CreateP1();
                PlantLinqProbe(p1, "middle_initial", "id");
                AssertEverySiteReads(p1, "P1", "middle_initial", "id");

                AssertEverySiteReads(p2, "P2 (after P1 planted and ran in its own scope)", "last_name", "employer_id");
            }
            finally
            {
                DropLinqTable();
            }
        }

        [DataTestMethod]
        [DataRow("OrderBy")]
        [DataRow("Select")]
        [DataRow("Count(predicate)")]
        [DataRow("Where")]
        [DataRow("Last")]
        [DataRow("Max")]
        public void LinqUnmappedRead_IsScoped(string site)
        {
            RequireDatabase();
            try
            {
                CreateLinqTable();
                using var p2 = CreateP2();
                var p2Caches = CachesOf(p2);
                p2Caches.Unmapped[typeof(LinqUnmappedA)] = new[] { typeof(LinqUnmappedA).GetProperty(nameof(LinqUnmappedA.FirstName)) };
                p2Caches.Unmapped[typeof(LinqUnmappedB)] = new[] { typeof(LinqUnmappedB).GetProperty(nameof(LinqUnmappedB.Id)) };

                var (run, rejection, p1Fragment) = UnmappedSite(site);

                var p2Sql = new List<string>();
                p2.Log = p2Sql.Add;
                var exception = Assert.ThrowsException<NotSupportedException>(() => run(p2),
                    $"P2 {site}: the site must reject the property planted as unmapped in P2's scope");
                StringAssert.StartsWith(exception.Message, rejection, $"P2 {site}: rejection message");
                Assert.AreEqual(0, p2Sql.Count, $"P2 {site}: SQL was logged: {string.Join(" | ", p2Sql)}");

                using var p1 = CreateP1();
                var p1Sql = new List<string>();
                p1.Log = p1Sql.Add;
                try
                {
                    run(p1);
                }
                catch (Exception ex)
                {
                    Assert.Fail($"P1 {site}: P1 is in another scope and must render and execute the shape; it threw " +
                                $"{ex.GetType().Name}: {ex.Message}");
                }
                StringAssert.Contains(string.Join("\n", p1Sql), p1Fragment, $"P1 {site}: rendered SQL");
            }
            finally
            {
                DropLinqTable();
            }
        }

        #endregion

        #region Sites

        private void PlantLinqProbe(OrmDataProvider provider, string firstNameColumn, string idColumn)
        {
            var caches = CachesOf(provider);
            caches.Tables[typeof(LinqProbe)] = Table;
            caches.Columns[typeof(LinqProbe).GetProperty(nameof(LinqProbe.FirstName)).ToDictionaryKey()] = firstNameColumn;
            caches.Columns[typeof(LinqProbe).GetProperty(nameof(LinqProbe.Id)).ToDictionaryKey()] = idColumn;
            if (!caches.Mapped.Contains(typeof(LinqProbe)))
                caches.Mapped.Add(typeof(LinqProbe));
        }

        /// <summary>Runs every AC7 read site on <paramref name="provider"/> and asserts its exact fragments.</summary>
        private void AssertEverySiteReads(OrmDataProvider provider, string who, string firstName, string id)
        {
            var qualifiedFirstName = $"{Table}.{firstName}";
            var qualifiedId = $"{Table}.{id}";
            var sites = new (string Site, Action<OrmDataProvider> Run, string[] Fragments)[]
            {
                ("OrderBy/ThenBy", p => p.Query<LinqProbe>().OrderBy(x => x.FirstName).ThenBy(x => x.Id).ToList(),
                    new[] { $"ORDER BY {firstName} ASC, {id} ASC" }),
                ("Select (member init)", p => p.Query<LinqProbe>().Select(x => new LinqProbe { FirstName = x.FirstName }).ToList(),
                    new[] { $"SELECT {qualifiedFirstName} AS FirstName" }),
                ("Select (member)", p => p.Query<LinqProbe>().Select(x => x.FirstName).ToList(),
                    new[] { $"SELECT {qualifiedFirstName} AS FirstName" }),
                ("Last() without OrderBy", p => p.Query<LinqProbe>().Last(),
                    new[] { $"ORDER BY {id} DESC" }),
                ("Where", p => p.Query<LinqProbe>().Where(x => x.FirstName == "x").ToList(),
                    new[] { $"WHERE {qualifiedFirstName} = " }),
                ("First(predicate)", p => p.Query<LinqProbe>().First(x => x.FirstName == "x"),
                    new[] { $"WHERE {qualifiedFirstName} = " }),
                ("Count(predicate)", p => p.Query<LinqProbe>().Count(x => x.FirstName == "x"),
                    new[] { $"COUNT(*) FROM {Table}", $"WHERE {qualifiedFirstName} = " }),
                ("Any(predicate)", p => p.Query<LinqProbe>().Any(x => x.FirstName == "x"),
                    new[] { $"WHERE {qualifiedFirstName} = " }),
                ("All(predicate)", p => p.Query<LinqProbe>().All(x => x.FirstName == "x"),
                    new[] { $"WHERE NOT ({qualifiedFirstName} = " }),
                ("Max", p => p.Query<LinqProbe>().Max(x => x.Id), new[] { $"MAX({qualifiedId})" }),
                ("Sum", p => p.Query<LinqProbe>().Sum(x => x.Id), new[] { $"SUM({qualifiedId})" }),
                ("Average", p => p.Query<LinqProbe>().Average(x => x.Id), new[] { AverageFragment(qualifiedId) }),
            };

            foreach (var (site, run, fragments) in sites)
            {
                var sql = new List<string>();
                provider.Log = sql.Add;
                try
                {
                    run(provider);
                }
                catch (Exception ex)
                {
                    Assert.Fail($"{who} {site}: threw {ex.GetType().Name}: {ex.Message} (SQL: {string.Join(" | ", sql)})");
                }
                finally
                {
                    provider.Log = null;
                }

                var text = string.Join("\n", sql);
                foreach (var fragment in fragments)
                    StringAssert.Contains(text, fragment, $"{who} {site}: expected its own scope's fragment");
            }
        }

        /// <summary>The run, P2's rejection message and P1's rendered fragment for one unmapped-read site.</summary>
        private (Action<OrmDataProvider> Run, string Rejection, string P1Fragment) UnmappedSite(string site)
        {
            switch (site)
            {
                case "OrderBy":
                    return (p => p.Query<LinqUnmappedA>().OrderBy(x => x.FirstName).ToList(),
                        "Only simple member access", $"ORDER BY {DiscoveredFirstName} ASC");
                case "Select":
                    return (p => p.Query<LinqUnmappedA>().Select(x => new LinqUnmappedA { FirstName = x.FirstName }).ToList(),
                        "Unmapped properties cannot be selected directly.", $"{Table}.{DiscoveredFirstName} AS FirstName");
                case "Count(predicate)":
                    return (p => p.Query<LinqUnmappedA>().Count(x => x.FirstName == "x"),
                        "Expression type Parameter", $"WHERE {Table}.{DiscoveredFirstName} = ");
                case "Where":
                    return (p => p.Query<LinqUnmappedA>().Where(x => x.FirstName == "x").ToList(),
                        "Expression type Parameter", $"WHERE {Table}.{DiscoveredFirstName} = ");
                case "Last":
                    return (p => p.Query<LinqUnmappedB>().Last(),
                        "Only simple member access", $"ORDER BY {DiscoveredId} DESC");
                case "Max":
                    return (p => p.Query<LinqUnmappedB>().Max(x => x.Id),
                        "Only simple member access is supported in aggregate expressions.", $"MAX({Table}.{DiscoveredId})");
                default:
                    throw new ArgumentOutOfRangeException(nameof(site), site, null);
            }
        }

        #endregion
    }
}
