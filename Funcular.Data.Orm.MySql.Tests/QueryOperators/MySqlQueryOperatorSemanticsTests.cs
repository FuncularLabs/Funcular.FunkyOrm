using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.MySql.Tests.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql.Tests.QueryOperators
{
    /// <summary>
    /// #13: semantics of the supported operators (AC13-1/2/3/5/6), the allowed and empty rows of the paging rule
    /// (AC13-10), a root-level <c>ThenBy</c> (AC13-12), <c>Skip</c> without <c>Take</c> (AC13-14) and the non-generic
    /// <c>IQueryProvider.Execute</c> (AC13-15). Each test names the wrong answer it rules out.
    /// </summary>
    [TestClass]
    public class MySqlQueryOperatorSemanticsTests : MySqlQueryOperatorTestBase
    {
        #region single_probe (a test-owned table with no id column)

        [Table("single_probe")]
        public class SingleProbeEntity
        {
            [Key]
            [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
            [Column("single_probe_key")]
            public int SingleProbeKey { get; set; }

            [Column("name")]
            public string Name { get; set; }
        }

        private readonly List<string> _probeNames = new List<string>();

        [TestInitialize]
        public void EnsureSingleProbeTable()
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE TABLE IF NOT EXISTS single_probe " +
                                          "(single_probe_key INT AUTO_INCREMENT PRIMARY KEY, name VARCHAR(100))";
                    command.ExecuteNonQuery();
                }
            }
        }

        [TestCleanup]
        public void DeleteProbeRows()
        {
            if (_probeNames.Count == 0)
                return;
            _provider.BeginTransaction();
            try
            {
                foreach (var name in _probeNames)
                    _provider.Delete<SingleProbeEntity>(p => p.Name == name);
                _provider.CommitTransaction();
            }
            catch
            {
                _provider.RollbackTransaction();
                throw;
            }
        }

        private int InsertProbe(string name)
        {
            _probeNames.Add(name);
            var row = new SingleProbeEntity { Name = name };
            _provider.Insert(row);
            return row.SingleProbeKey;
        }

        #endregion

        #region A reverse (one-to-many) remote key: organization ← person

        /// <summary>
        /// The organization table seen from the many side: <c>PersonId</c> resolves through a reverse join
        /// (<c>organization.id = person.employer_id</c>), so filtering an aggregate by it would fan out.
        /// </summary>
        [Table("organization")]
        public class OrganizationReverseDetailEntity : Organization
        {
            [RemoteKey(typeof(PersonWithEmployer), keyPath: new[] { nameof(PersonWithEmployer.Id) })]
            public int PersonId { get; set; }
        }

        #endregion

        #region Harness positive control

        /// <summary>
        /// <c>AssertNoQuery</c> relies on every execution path logging its command. If a new reader path skipped
        /// <c>Log</c>, "no query executed" would pass vacuously: this pins that each path is observed.
        /// </summary>
        [TestMethod]
        public void Harness_LogObservesEveryExecutionPath()
        {
            var (marker, _) = SeedAbc();
            var query = People(marker);
            var paths = new Dictionary<string, Action>
            {
                ["enumeration"] = () => query.ToList(),
                ["entity terminal"] = () => query.OrderBy(p => p.Id).FirstOrDefault(),
                ["Count"] = () => query.Count(),
                ["Any"] = () => query.Any(),
                ["selector aggregate"] = () => query.Max(p => p.Id),
                ["scalar projection"] = () => query.Select(p => p.Id).ToList(),
                ["non-generic Execute"] = () => query.Provider.Execute(query.Expression),
                ["Single"] = () => query.Single(p => p.FirstName == "b"),
                ["Last"] = () => query.Last(),
                // 3.9.0 throws InvalidCastException after the command ran (and was logged).
                ["LongCount"] = () => { try { query.LongCount(); } catch (InvalidCastException) { } },
            };

            foreach (var path in paths)
            {
                ClearLog();
                path.Value();
                Assert.AreNotEqual(string.Empty, Sql, path.Key + " must log its command");
            }
        }

        #endregion

        #region AC13-1 Single / SingleOrDefault

        [TestMethod]
        public void Single_Predicate_ReturnsTargetNotFirst()
        {
            var (marker, ids) = SeedAbc();

            // Rules out the first row (3.9.0 drops the predicate) and the last row.
            var row = _provider.Query<PersonWithEmployer>().Single(p => p.LastName == marker && p.FirstName == "b");

            Assert.AreEqual(ids[1], row.Id);
        }

        [TestMethod]
        public void SingleOrDefault_Predicate_NoMatch_ReturnsNull()
        {
            var (marker, _) = SeedAbc();

            // Rules out returning an unrelated row.
            Assert.IsNull(_provider.Query<PersonWithEmployer>().SingleOrDefault(p => p.LastName == marker && p.FirstName == "zzz"));
        }

        [TestMethod]
        public void Single_NoMatch_Throws()
        {
            var (marker, _) = SeedAbc();

            Assert.ThrowsException<InvalidOperationException>(() => People(marker).Where(p => p.FirstName == "zzz").Single());
        }

        [TestMethod]
        public void Single_TwoMatches_Throws()
        {
            var marker = NewMarker();
            SeedPeople(marker, null, "dup", "dup", "x");

            // Rules out a row limit of 1 (no cardinality check).
            Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Query<PersonWithEmployer>().Single(p => p.LastName == marker && p.FirstName == "dup"));
        }

        [TestMethod]
        public void SingleOrDefault_TwoMatches_Throws()
        {
            var marker = NewMarker();
            SeedPeople(marker, null, "dup", "dup", "x");

            Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Query<PersonWithEmployer>().SingleOrDefault(p => p.LastName == marker && p.FirstName == "dup"));
        }

        [TestMethod]
        public void Single_NoUserOrder_EmitsRowLimit_NoIdOrder()
        {
            var (marker, ids) = SeedAbc();

            ClearLog();
            var row = People(marker).Single(p => p.FirstName == "b");

            Assert.AreEqual(ids[1], row.Id);
            // Word-bounded: a plain substring would also accept "LIMIT 2147483647".
            StringAssert.Matches(Sql, new Regex(@"\bLIMIT 2\b"), "Single reads at most two rows");
            Assert.IsFalse(Sql.Contains("ORDER BY"), "no ORDER BY may be synthesized for Single (not the paging path): " + Sql);
        }

        [TestMethod]
        public void Single_OnEntityWithoutIdColumn_Works()
        {
            var suffix = Guid.NewGuid().ToString("N").Substring(0, 12);
            InsertProbe("decoy1_" + suffix);
            var target = InsertProbe("target_" + suffix);
            InsertProbe("decoy2_" + suffix);
            var targetName = "target_" + suffix;

            // Rules out the table's first row (dropped predicate) and an injected ORDER BY id (no id column).
            var row = _provider.Query<SingleProbeEntity>().Single(p => p.Name == targetName);

            Assert.AreEqual(target, row.SingleProbeKey);
        }

        [TestMethod]
        public void Single_AfterDistinctProjection_Works()
        {
            var (marker, _) = SeedAbc();

            // An injected ORDER BY id would break Distinct + projection (the key isn't projected).
            var row = People(marker).Where(p => p.FirstName == "b")
                .Select(p => new PersonWithEmployer { FirstName = p.FirstName })
                .Distinct()
                .Single();

            Assert.AreEqual("b", row.FirstName);
        }

        [TestMethod]
        public void Single_AfterTake1_OverManyRows_ReturnsRow()
        {
            var (marker, ids) = SeedAbc();

            // Rules out ignoring the user's Take(1) (which would read 2 rows and throw).
            var row = People(marker).OrderBy(p => p.Id).Take(1).Single();

            Assert.AreEqual(ids[0], row.Id);
        }

        [TestMethod]
        public void Single_AfterSkipOnly_OverManyRows_Throws()
        {
            var (marker, _) = SeedAbc();

            ClearLog();
            Assert.ThrowsException<InvalidOperationException>(() => People(marker).OrderBy(p => p.Id).Skip(1).Single());

            // 3.9.0 emits LIMIT 18446744073709551615 OFFSET 1 (MySQL's "offset to end") and no cap.
            StringAssert.Contains(Sql, "LIMIT 2 OFFSET 1", "Skip-only Single is capped at two rows");
        }

        [TestMethod]
        public void Single_AfterSkipTake_Parameterless_MatchesOracle()
        {
            var (marker, _) = SeedAbc();

            AssertMatchesOracle(marker, q => q.OrderBy(p => p.Id).Skip(1).Take(1).Single());
        }

        #endregion

        #region AC13-2 Last / LastOrDefault

        [TestMethod]
        public void Last_Parameterless_Unordered_ReturnsMaxId()
        {
            var (marker, ids) = SeedAbc();

            // Rules out the first row (3.9.0). The SQL assert carries the kill where an unordered read comes back in
            // InnoDB clustered-index (id) order, which is not a guarantee: taking its last row lands on the max id by chance.
            ClearLog();
            Assert.AreEqual(ids[2], People(marker).Last().Id);
            StringAssert.Contains(OrderByList(), $"{PersonTable}.id DESC", "default Id DESC, table-qualified on a join entity");
        }

        [TestMethod]
        public void Last_ReadsOneRow_EmitsRowLimit()
        {
            var (marker, ids) = SeedAbc();

            // Last* reads the first row of the inverted order. Without the row limit it reads every row and keeps
            // the first, which returns the same entity: only the SQL shows the difference.
            ClearLog();
            Assert.AreEqual(ids[2], People(marker).Last().Id);
            StringAssert.Matches(Sql, new Regex(@"\bLIMIT 1\b"), "Last reads one row");
        }

        [TestMethod]
        public void Last_AfterOrderByNonIdKey_ReturnsLastInOrder()
        {
            var marker = NewMarker();
            var ids = SeedPeople(marker, null, "c", "a", "b");

            // Last by FirstName is "c", the MIN id: rules out the max-id row and the first row in order.
            ClearLog();
            var row = People(marker).OrderBy(p => p.FirstName).Last();

            Assert.AreEqual(ids[0], row.Id);
            StringAssert.Contains(OrderByList(), $"{PersonTable}.first_name DESC", "the explicit order is inverted");
        }

        [TestMethod]
        public void Last_AfterOrderByThenByDescending_InvertsEveryTerm()
        {
            var marker = NewMarker();
            var employer1 = SeedEmployer("Q310Org1_" + marker);
            var employer2 = SeedEmployer("Q310Org2_" + marker);
            SeedPerson(marker, "a", employer1);
            SeedPerson(marker, "b", employer1);
            SeedPerson(marker, "a", employer2);
            SeedPerson(marker, "b", employer2);

            // Order: (E1,b) (E1,a) (E2,b) (E2,a) → last is (E2,a). Inverting only the first term gives (E2,b).
            AssertMatchesOracle(marker, q => q.OrderBy(p => p.EmployerId).ThenByDescending(p => p.FirstName).Last());
        }

        [TestMethod]
        public void Last_AfterRemoteOrderBy_ReturnsLastInOrder()
        {
            var marker = NewMarker();
            var employerB = SeedEmployer("Q310B_" + marker);
            var employerA = SeedEmployer("Q310A_" + marker);
            SeedPerson(marker, "a", employerB);
            var b = SeedPerson(marker, "b", employerB);
            SeedPerson(marker, "c", employerA);

            // Order: (A,c) (B,a) (B,b) → last is b, which is not the max id: rules out an Id DESC fallback (c) and
            // not inverting the remote term (c).
            ClearLog();
            var row = People(marker).OrderBy(p => p.EmployerName).ThenBy(p => p.Id).Last();

            Assert.AreEqual(b, row.Id);
            StringAssert.Contains(OrderByList(), $"{PersonTable}.id DESC", "the inverted explicit Id term stays qualified");
        }

        [TestMethod]
        public void Last_AfterComputedOrderBy_InvertsComputedTerm()
        {
            var marker = NewMarker();
            var organization = SeedEmployer("Q310Org_" + marker);
            // EffectiveScore = COALESCE(score, 0). Scores in id order 9, null, 5 → ascending order (0) (5) (9) → last is
            // the score-9 project, the MIN id. No inversion gives the null-score one; an Id DESC fallback the score-5 one.
            var expected = SeedProject(marker, organization, 9);
            SeedProject(marker, organization, null);
            SeedProject(marker, organization, 5);

            ClearLog();
            var row = _provider.Query<ProjectScorecard>().Where(p => p.Name == marker).OrderBy(p => p.EffectiveScore).Last();

            Assert.AreEqual(expected, row.Id);
            // Spelling captured from 3.9.0 (6542796) by a throwaway probe of OrderByDescending(EffectiveScore), 2026-10-01.
            StringAssert.Contains(OrderByList(), "COALESCE(project.score, 0) DESC", "a computed fragment is inverted whole");
        }

        [TestMethod]
        public void Last_AfterTernaryOrderBy_InvertsCaseTerm()
        {
            var marker = NewMarker();
            var employerZ = SeedEmployer("Q310OrgZ_" + marker);
            var employerM = SeedEmployer("Q310OrgM_" + marker);
            SeedPerson(marker, "a", employerZ);
            SeedPerson(marker, "b", employerM);
            SeedPerson(marker, "c", employerZ);

            // Keys a=0, b=1, c=0 → order a, c, b → last is b. Not inverting the CASE term gives c.
            AssertMatchesOracle(marker, q => q.OrderBy(p => p.EmployerId == employerZ ? 0 : 1).ThenBy(p => p.Id).Last());
        }

        [TestMethod]
        public void LastOrDefault_Predicate_WithExplicitOrderBy_MatchesOracle()
        {
            var marker = NewMarker();
            SeedPeople(marker, SeedEmployer("Q310Country_" + marker), "c", "b", "a");

            // By FirstName: a, b, c → matching (!= "c"): a, b → last is b. Among the matches the max id is a, so an
            // Id DESC fallback that ignores the explicit order gives a.
            ClearLog();
            AssertMatchesOracle(marker, q => q.OrderBy(p => p.FirstName).LastOrDefault(p => p.FirstName != "c"));
            // b is also the lowest id among the matches, so a Last that drops the ORDER BY could coincide: pin the SQL.
            StringAssert.Contains(OrderByList(), $"{PersonTable}.first_name DESC", "the explicit order is inverted");
        }

        [TestMethod]
        public void Last_Empty_Throws()
        {
            var marker = NewMarker();

            Assert.ThrowsException<InvalidOperationException>(() => People(marker).Last());
        }

        [TestMethod]
        public void LastOrDefault_Empty_ReturnsNull()
        {
            var marker = NewMarker();

            Assert.IsNull(People(marker).LastOrDefault());
        }

        [TestMethod]
        public void Last_EntityWithoutIdProperty_ThrowsExistingInvalidOperation()
        {
            var name = "target_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            InsertProbe(name);

            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Query<SingleProbeEntity>().Where(p => p.Name == name).Last());
            StringAssert.Contains(ex.Message, "does not have an 'Id' property");
        }

        [TestMethod]
        public void Last_AfterDistinctProjection_NoOrder_ThrowsNamingLast()
        {
            var (marker, _) = SeedAbc();

            var ex = Assert.ThrowsException<NotSupportedException>(() =>
                People(marker).Select(p => new PersonWithEmployer { FirstName = p.FirstName }).Distinct().Last());
            StringAssert.Contains(ex.Message, "Last");
        }

        [TestMethod]
        public void Last_AfterDistinctProjection_WithProjectedOrder_Works()
        {
            var (marker, _) = SeedAbc();

            var row = People(marker).OrderBy(p => p.FirstName)
                .Select(p => new PersonWithEmployer { FirstName = p.FirstName })
                .Distinct()
                .Last();

            Assert.AreEqual("c", row.FirstName);
        }

        [DataTestMethod]
        [DataRow("Last", false)]
        [DataRow("LastOrDefault", false)]
        [DataRow("LastPredicate", false)]
        [DataRow("LastOrDefaultPredicate", false)]
        [DataRow("Last", true)]
        public void LastFamily_AfterDistinctProjection_NoOrder_ThrowsNamingTerminal(string terminal, bool projectId)
        {
            var (marker, _) = SeedAbc();
            var distinct = projectId
                ? People(marker).Select(p => new PersonWithEmployer { Id = p.Id, FirstName = p.FirstName }).Distinct()
                : People(marker).Select(p => new PersonWithEmployer { FirstName = p.FirstName }).Distinct();
            Action run;
            switch (terminal)
            {
                case "Last": run = () => distinct.Last(); break;
                case "LastOrDefault": run = () => distinct.LastOrDefault(); break;
                case "LastPredicate": run = () => distinct.Last(p => p.FirstName != "zzz"); break;
                case "LastOrDefaultPredicate": run = () => distinct.LastOrDefault(p => p.FirstName != "zzz"); break;
                default: throw new ArgumentOutOfRangeException(nameof(terminal));
            }

            var ex = Assert.ThrowsException<NotSupportedException>(run);
            StringAssert.Contains(ex.Message, terminal.Replace("Predicate", "") + "() after Distinct()");
            // The stated reason must hold when the key is projected too.
            Assert.IsFalse(ex.Message.Contains("Id DESC"), ex.Message);
        }

        [TestMethod]
        public void Last_NullableKey_EqualsTheProvidersOwnOrder()
        {
            // AC13-2: Last is the last row of the provider's own order. Where NULLs sort differs from LINQ-to-objects
            // (PostgreSQL sorts them last when ascending), Last follows the database, as First and ToList do (§8).
            var marker = NewMarker();
            var employer = SeedEmployer("Q310Org_" + marker);
            SeedPerson(marker, "a", employer);
            SeedPerson(marker, "b", null); // an own nullable column; no remote member is read
            SeedPerson(marker, "c", employer);

            var ascending = People(marker).OrderBy(p => p.EmployerId).ThenBy(p => p.Id);
            Assert.AreEqual(ascending.ToList().Last().Id, ascending.Last().Id, "ascending");
            var descending = People(marker).OrderByDescending(p => p.EmployerId).ThenBy(p => p.Id);
            Assert.AreEqual(descending.ToList().Last().Id, descending.Last().Id, "descending");
        }

        [TestMethod]
        public void Last_EntityWithoutId_ScalarProjection_ScalarGuardWins()
        {
            // The default Id DESC is resolved after the scalar and Distinct guards, so a missing Id property can't mask them.
            var ex = Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<SingleProbeEntity>().Select(x => x.Name).Last());
            StringAssert.Contains(ex.Message, "is only supported for a list/enumeration result");
        }

        [TestMethod]
        public void Last_EntityWithoutId_DistinctProjection_DistinctGuardWins()
        {
            var ex = Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<SingleProbeEntity>().Select(x => new SingleProbeEntity { Name = x.Name }).Distinct().LastOrDefault());
            StringAssert.Contains(ex.Message, "LastOrDefault() after Distinct()");
        }

        #endregion

        #region AC13-3 LongCount

        [TestMethod]
        public void LongCount_EqualsCount_ReturnsInt64()
        {
            var (marker, _) = SeedAbc();

            // The static type is long; the failure mode in 3.9.0 is the InvalidCastException while producing it.
            long count = People(marker).LongCount();

            Assert.AreEqual(3L, count);
            Assert.AreEqual(People(marker).Count(), (int)count);
        }

        [TestMethod]
        public void LongCount_Predicate_EqualsCountPredicate()
        {
            var (marker, _) = SeedAbc();

            Assert.AreEqual(2L, People(marker).LongCount(p => p.FirstName != "a"));
            Assert.AreEqual(People(marker).Count(p => p.FirstName != "a"), (int)People(marker).LongCount(p => p.FirstName != "a"));
        }

        [TestMethod]
        public void LongCount_FilteredByReverseRemoteKey_ThrowsNotSupported()
        {
            // Precondition: the entity really resolves PersonId through a reverse join, so Count (3.9.0) already throws
            // the reverse-join message. Without it, the LongCount assert could pass or fail for an unrelated reason.
            var precondition = Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<OrganizationReverseDetailEntity>().Where(c => c.PersonId == 1).Count(),
                "precondition: Count filtered by the reverse key throws the reverse-join message");
            StringAssert.Contains(precondition.Message, "reverse", "precondition");

            var ex = Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<OrganizationReverseDetailEntity>().Where(c => c.PersonId == 1).LongCount());
            StringAssert.Contains(ex.Message, "reverse");
        }

        #endregion

        #region AC13-5 allowed families vs the oracle

        private static readonly Dictionary<string, Func<IQueryable<PersonWithEmployer>, object>> AllowedFamilies =
            new Dictionary<string, Func<IQueryable<PersonWithEmployer>, object>>
            {
                ["Where"] = q => q.Where(p => p.FirstName != "b").OrderBy(p => p.Id).ToList(),
                ["SelectSubset"] = q => q.OrderBy(p => p.FirstName).Select(p => new PersonWithEmployer { FirstName = p.FirstName }).ToList(),
                ["SelectScalar"] = q => q.OrderBy(p => p.FirstName).Select(p => p.FirstName).ToList(),
                ["OrderBy"] = q => q.OrderBy(p => p.FirstName).ToList(),
                ["OrderByDescending"] = q => q.OrderByDescending(p => p.FirstName).ToList(),
                // One shared employer: EmployerId ties and FirstName decides (PersonWithEmployer maps no Gender).
                ["ThenBy"] = q => q.OrderBy(p => p.EmployerId).ThenBy(p => p.FirstName).ToList(),
                ["ThenByDescending"] = q => q.OrderBy(p => p.EmployerId).ThenByDescending(p => p.FirstName).ToList(),
                ["SkipTake"] = q => q.OrderBy(p => p.Id).Skip(1).Take(1).ToList(),
                ["Distinct"] = q => q.Select(p => p.EmployerId).Distinct().ToList(), // three rows, one employer: Distinct must collapse them
                ["First"] = q => q.OrderBy(p => p.Id).First(),
                ["FirstPredicate"] = q => q.First(p => p.FirstName == "a"), // "a" is neither the first row nor the max id
                ["FirstOrDefault"] = q => q.OrderBy(p => p.Id).FirstOrDefault(),
                ["FirstOrDefaultPredicate"] = q => q.FirstOrDefault(p => p.FirstName == "zzz"),
                ["Single"] = q => q.Where(p => p.FirstName == "a").Single(),
                ["SinglePredicate"] = q => q.Single(p => p.FirstName == "a"),
                ["SingleOrDefault"] = q => q.Where(p => p.FirstName == "zzz").SingleOrDefault(),
                ["SingleOrDefaultPredicate"] = q => q.SingleOrDefault(p => p.FirstName == "a"),
                ["Last"] = q => q.OrderByDescending(p => p.FirstName).Last(),
                ["LastPredicate"] = q => q.OrderByDescending(p => p.FirstName).Last(p => p.FirstName != "a"),
                ["LastOrDefault"] = q => q.OrderByDescending(p => p.FirstName).LastOrDefault(),
                ["LastOrDefaultPredicate"] = q => q.OrderBy(p => p.Id).LastOrDefault(p => p.FirstName == "zzz"),
                ["Any"] = q => q.Any(),
                ["AnyPredicate"] = q => q.Any(p => p.FirstName == "zzz"), // false only if the predicate is applied
                ["AnyPredicateTrue"] = q => q.Any(p => p.FirstName == "a"), // true; kills a swapped CASE / Any emitted in All's shape (AnyPredicate kills a negated predicate)
                ["All"] = q => q.All(p => p.FirstName != "a"), // false: "a" is seeded
                ["AllTrue"] = q => q.All(p => p.FirstName != "zzz"), // true: dropping the NOT in NOT EXISTS gives false
                ["Count"] = q => q.Count(),
                ["CountPredicate"] = q => q.Count(p => p.FirstName != "a"),
                ["LongCount"] = q => q.LongCount(),
                ["LongCountPredicate"] = q => q.LongCount(p => p.FirstName != "a"),
                ["SumInt"] = q => q.Sum(p => p.Id),
                ["MinInt"] = q => q.Min(p => p.Id),
                ["MaxInt"] = q => q.Max(p => p.Id),
                ["CastIdentity"] = q => q.OrderBy(p => p.Id).Cast<PersonWithEmployer>().ToList(),
                ["OfTypeIdentity"] = q => q.OrderBy(p => p.Id).OfType<PersonWithEmployer>().ToList(),
            };

        private static readonly Dictionary<string, string> InvertedLastOrder = new Dictionary<string, string>
        {
            ["Last"] = "first_name ASC",
            ["LastPredicate"] = "first_name ASC",
            ["LastOrDefault"] = "first_name ASC",
            ["LastOrDefaultPredicate"] = "id DESC",
        };

        [DataTestMethod]
        [DataRow("Where")]
        [DataRow("SelectSubset")]
        [DataRow("SelectScalar")]
        [DataRow("OrderBy")]
        [DataRow("OrderByDescending")]
        [DataRow("ThenBy")]
        [DataRow("ThenByDescending")]
        [DataRow("SkipTake")]
        [DataRow("Distinct")]
        [DataRow("First")]
        [DataRow("FirstPredicate")]
        [DataRow("FirstOrDefault")]
        [DataRow("FirstOrDefaultPredicate")]
        [DataRow("Single")]
        [DataRow("SinglePredicate")]
        [DataRow("SingleOrDefault")]
        [DataRow("SingleOrDefaultPredicate")]
        [DataRow("Last")]
        [DataRow("LastPredicate")]
        [DataRow("LastOrDefault")]
        [DataRow("LastOrDefaultPredicate")]
        [DataRow("Any")]
        [DataRow("AnyPredicate")]
        [DataRow("AnyPredicateTrue")]
        [DataRow("All")]
        [DataRow("AllTrue")]
        [DataRow("Count")]
        [DataRow("CountPredicate")]
        [DataRow("LongCount")]
        [DataRow("LongCountPredicate")]
        [DataRow("SumInt")]
        [DataRow("MinInt")]
        [DataRow("MaxInt")]
        [DataRow("CastIdentity")]
        [DataRow("OfTypeIdentity")]
        public void Allowed_Operator_MatchesOracle(string family)
        {
            // Seeded b, a, c (ascending ids), so FirstName order differs from id order: a dropped or ignored ordering,
            // or a Last* that falls back to Id DESC, gives a different answer.
            var marker = NewMarker();
            SeedPeople(marker, SeedEmployer("Q310Org_" + marker), "b", "a", "c");

            ClearLog();
            AssertMatchesOracle(marker, AllowedFamilies[family], family);
            // Last* must invert the explicit order; a missing or uninverted ORDER BY can coincide with the oracle.
            if (InvertedLastOrder.TryGetValue(family, out var inverted))
                StringAssert.Contains(OrderByList(), PersonTable + "." + inverted, family + ": inverted ORDER BY");
        }

        #endregion

        #region AC13-6 Cast / OfType

        [TestMethod]
        public void OfType_Identity_AtRoot_IsNoOp()
        {
            var (marker, ids) = SeedAbc();

            var rows = _provider.Query<PersonWithEmployer>().OfType<PersonWithEmployer>()
                .Where(p => p.LastName == marker).OrderBy(p => p.Id).ToList();

            CollectionAssert.AreEqual(ids, rows.Select(r => r.Id).ToList());
        }

        [TestMethod]
        public void OfType_Identity_AfterOrderBy_IsNoOp()
        {
            var (marker, ids) = SeedAbc();

            var rows = People(marker).OrderBy(p => p.Id).OfType<PersonWithEmployer>().ToList();

            CollectionAssert.AreEqual(ids, rows.Select(r => r.Id).ToList());
        }

        [TestMethod]
        public void OfType_Identity_AfterScalarProjection_NonNullable_IsNoOp()
        {
            var (marker, ids) = SeedAbc();

            var projected = People(marker).OrderBy(p => p.Id).Select(p => p.Id).OfType<int>().ToList();

            CollectionAssert.AreEqual(ids, projected);
        }

        [TestMethod]
        public void OfType_Identity_OverNullableScalar_Rejected()
        {
            var marker = NewMarker();
            var employer = SeedEmployer("Q310Org_" + marker);
            SeedPerson(marker, "a", employer);
            SeedPerson(marker, "b", null); // a seeded null: OfType would have to drop it
            var query = People(marker).Select(p => p.EmployerId).OfType<int?>();

            var ex = AssertThrowsNoQuery<NotSupportedException>(() => query.ToList());
            StringAssert.Contains(ex.Message, "OfType");
            StringAssert.Contains(ex.Message, "!= null", "the message points to filtering nulls before the projection");
        }

        [TestMethod]
        public void OfType_Identity_OverReferenceScalar_Rejected()
        {
            var (marker, _) = SeedAbc();
            // person.first_name is NOT NULL and PersonWithEmployer maps no nullable own string column, so no null is
            // seeded: the rejection is by the element type (a reference type OfType may have to filter), not by the data.
            var query = People(marker).Select(p => p.FirstName).OfType<string>();

            var ex = AssertThrowsNoQuery<NotSupportedException>(() => query.ToList());
            StringAssert.Contains(ex.Message, "OfType");
            StringAssert.Contains(ex.Message, "!= null", "the message points to filtering nulls before the projection");
        }

        [TestMethod]
        public void OfType_NonIdentity_Throws()
        {
            var (marker, _) = SeedAbc();
            var query = People(marker).OfType<PersonBase>();

            var ex = AssertThrowsNoQuery<NotSupportedException>(() => query.ToList());
            StringAssert.Contains(ex.Message, "OfType");
        }

        [TestMethod]
        public void Cast_Identity_IsNoOp()
        {
            var (marker, ids) = SeedAbc();

            var rows = People(marker).OrderBy(p => p.Id).Cast<PersonWithEmployer>().ToList();

            CollectionAssert.AreEqual(ids, rows.Select(r => r.Id).ToList());
        }

        [TestMethod]
        public void Cast_ReferenceConversion_Count_MatchesOracle()
        {
            var (marker, _) = SeedAbc();

            AssertMatchesOracle(marker, q => q.Cast<object>().Count());
        }

        [TestMethod]
        public void Cast_ReferenceConversion_OrderedFirst_MatchesOracle()
        {
            var (marker, _) = SeedAbc();

            AssertMatchesOracle(marker, q => q.OrderBy(p => p.Id).Cast<object>().First());
        }

        [TestMethod]
        public void Cast_ReferenceConversion_Enumerated_MatchesOracle()
        {
            var (marker, _) = SeedAbc();

            // 3.9.0: InvalidCastException (Execute treats IEnumerable<object> as a single row).
            AssertMatchesOracle(marker, q => q.OrderBy(p => p.Id).Cast<object>().ToList());
        }

        [TestMethod]
        public void Cast_ReferenceConversion_BaseClass_Enumerated_MatchesOracle()
        {
            var (marker, _) = SeedAbc();

            AssertMatchesOracle(marker, q => q.OrderBy(p => p.Id).Cast<PersonBase>().ToList());
        }

        [TestMethod]
        public void Cast_ReferenceConversion_Interface_Count_MatchesOracle()
        {
            var (marker, _) = SeedAbc();

            AssertMatchesOracle(marker, q => q.Cast<IHasPersonId>().Count());
        }

        [TestMethod]
        public void Cast_BackToEntityAfterTransparentCast_IsNoOp()
        {
            var (marker, _) = SeedAbc();

            AssertMatchesOracle(marker, q => q.OrderBy(p => p.Id).Cast<object>().Cast<PersonWithEmployer>().ToList());
        }

        [TestMethod]
        public void OfType_Entity_AfterTransparentCast_IsNoOp()
        {
            var (marker, _) = SeedAbc();

            AssertMatchesOracle(marker, q => q.OrderBy(p => p.Id).Cast<object>().OfType<PersonWithEmployer>().ToList());
        }

        [TestMethod]
        public void Cast_Boxing_Rejected()
        {
            var (marker, _) = SeedAbc();
            var query = People(marker).Select(p => p.Id).Cast<object>();

            var ex = AssertThrowsNoQuery<NotSupportedException>(() => query.ToList());
            StringAssert.Contains(ex.Message, "supports only identity and reference-conversion casts");
        }

        [TestMethod]
        public void Cast_NonIdentity_UnrelatedType_Throws()
        {
            var (marker, _) = SeedAbc();
            var query = People(marker).Cast<Address>();

            var ex = AssertThrowsNoQuery<NotSupportedException>(() => query.ToList());
            StringAssert.Contains(ex.Message, "supports only identity and reference-conversion casts");
        }

        #endregion

        #region AC13-10 after Skip/Take: allowed rows, empty Take

        private static readonly Dictionary<string, Func<IQueryable<PersonWithEmployer>, object>> AllowedAfterPaging =
            new Dictionary<string, Func<IQueryable<PersonWithEmployer>, object>>
            {
                ["Skip.Take"] = q => q.OrderBy(p => p.Id).Skip(1).Take(1).ToList(),
                ["Skip.Select(subset).Take"] = q => q.OrderBy(p => p.FirstName).Skip(1).Select(p => new PersonWithEmployer { FirstName = p.FirstName }).Take(1).ToList(),
                ["Skip.Select(scalar).Take"] = q => q.OrderBy(p => p.FirstName).Skip(1).Select(p => p.FirstName).Take(1).ToList(),
                ["Skip.OfType<T>().Take"] = q => q.OrderBy(p => p.Id).Skip(1).OfType<PersonWithEmployer>().Take(1).ToList(),
                ["Skip.Cast<T>().Take"] = q => q.OrderBy(p => p.Id).Skip(1).Cast<PersonWithEmployer>().Take(1).ToList(),
                ["Take.Cast<object>() enumerated"] = q => q.OrderBy(p => p.Id).Take(2).Cast<object>().ToList(),
                ["Skip.Take.Select(scalar).Cast<object>() enumerated"] = q => q.OrderBy(p => p.FirstName).Skip(1).Take(2).Select(p => p.FirstName).Cast<object>().ToList(),
                ["Select subset"] = q => q.OrderBy(p => p.FirstName).Skip(1).Take(2).Select(p => new PersonWithEmployer { FirstName = p.FirstName }).ToList(),
                ["Select scalar"] = q => q.OrderBy(p => p.FirstName).Skip(1).Take(2).Select(p => p.FirstName).ToList(),
                ["First()"] = q => q.OrderBy(p => p.Id).Skip(1).First(),
                ["FirstOrDefault()"] = q => q.OrderBy(p => p.Id).Skip(5).FirstOrDefault(),
                ["Single()"] = q => q.OrderBy(p => p.Id).Skip(1).Take(1).Single(),
                ["SingleOrDefault()"] = q => q.OrderBy(p => p.Id).Skip(5).Take(1).SingleOrDefault(),
                ["OfType-identity entity"] = q => q.OrderBy(p => p.Id).Skip(1).OfType<PersonWithEmployer>().ToList(),
                ["Skip(n).Select(p => p.Id).OfType<int>()"] = q => q.OrderBy(p => p.Id).Skip(1).Select(p => p.Id).OfType<int>().ToList(),
                ["Skip-only.First()"] = q => q.OrderBy(p => p.Id).Skip(2).First(),
            };

        [DataTestMethod]
        [DataRow("Skip.Take")]
        [DataRow("Skip.Select(subset).Take")]
        [DataRow("Skip.Select(scalar).Take")]
        [DataRow("Skip.OfType<T>().Take")]
        [DataRow("Skip.Cast<T>().Take")]
        [DataRow("Take.Cast<object>() enumerated")]
        [DataRow("Skip.Take.Select(scalar).Cast<object>() enumerated")]
        [DataRow("Select subset")]
        [DataRow("Select scalar")]
        [DataRow("First()")]
        [DataRow("FirstOrDefault()")]
        [DataRow("Single()")]
        [DataRow("SingleOrDefault()")]
        [DataRow("OfType-identity entity")]
        [DataRow("Skip(n).Select(p => p.Id).OfType<int>()")]
        [DataRow("Skip-only.First()")]
        public void Operator_AfterPaging_Allowed_MatchesOracle(string shape)
        {
            var (marker, _) = SeedAbc();

            AssertMatchesOracle(marker, AllowedAfterPaging[shape], shape);
        }

        [DataTestMethod]
        [DataRow("Take(0) full")]
        [DataRow("Take(0) subset")]
        [DataRow("Take(0) scalar")]
        [DataRow("Skip(2).Take(0)")]
        [DataRow("Take(-1)")]
        [DataRow("Take(0).Cast<object>() enumerated")]
        public void TakeNonPositive_ReturnsEmpty_NoQuery(string shape)
        {
            var (marker, _) = SeedAbc();
            var people = People(marker); // no explicit order: projection rows must not hit #12's ORDER BY id
            IEnumerable result = null;

            AssertNoQuery(() =>
            {
                switch (shape)
                {
                    case "Take(0) full": result = people.Take(0).ToList(); break;
                    case "Take(0) subset": result = people.Take(0).Select(p => new PersonWithEmployer { FirstName = p.FirstName }).ToList(); break;
                    case "Take(0) scalar": result = people.Take(0).Select(p => p.FirstName).ToList(); break;
                    case "Skip(2).Take(0)": result = people.Skip(2).Take(0).ToList(); break;
                    case "Take(-1)": result = people.Take(-1).ToList(); break;
                    case "Take(0).Cast<object>() enumerated": result = people.Take(0).Cast<object>().ToList(); break;
                    default: throw new ArgumentOutOfRangeException(nameof(shape));
                }
            });

            Assert.AreEqual(0, result.Cast<object>().Count());
        }

        [TestMethod]
        public void Take0_First_Throws_NoQuery()
        {
            var (marker, _) = SeedAbc();
            var query = People(marker).OrderBy(p => p.Id).Take(0);

            AssertThrowsNoQuery<InvalidOperationException>(() => query.First());
        }

        [TestMethod]
        public void Take0_FirstOrDefault_ReturnsNull_NoQuery()
        {
            var (marker, _) = SeedAbc();
            var query = People(marker).OrderBy(p => p.Id).Take(0);
            PersonWithEmployer row = null;

            AssertNoQuery(() => row = query.FirstOrDefault());
            Assert.IsNull(row);
        }

        [TestMethod]
        public void Take0_Single_Throws_NoQuery()
        {
            var (marker, _) = SeedAbc();
            var query = People(marker).OrderBy(p => p.Id).Take(0);

            AssertThrowsNoQuery<InvalidOperationException>(() => query.Single());
        }

        [TestMethod]
        public void Take0_SingleOrDefault_ReturnsNull_NoQuery()
        {
            var (marker, _) = SeedAbc();
            var query = People(marker).OrderBy(p => p.Id).Take(0);
            PersonWithEmployer row = null;

            AssertNoQuery(() => row = query.SingleOrDefault());
            Assert.IsNull(row);
        }

        [DataTestMethod]
        [DataRow("First")]
        [DataRow("FirstOrDefault")]
        [DataRow("Single")]
        [DataRow("SingleOrDefault")]
        public void Take0_CastObject_Terminal_NoQuery(string terminal)
        {
            var (marker, _) = SeedAbc();
            AssertEmptyTerminal(People(marker).OrderBy(p => p.Id).Take(0).Cast<object>(), terminal);
        }

        [DataTestMethod]
        [DataRow("First")]
        [DataRow("FirstOrDefault")]
        [DataRow("Single")]
        [DataRow("SingleOrDefault")]
        public void Take0_ImplicitObject_Terminal_NoQuery(string terminal)
        {
            var (marker, _) = SeedAbc();
            IQueryable<object> query = People(marker).OrderBy(p => p.Id).Take(0);
            AssertEmptyTerminal(query, terminal);
        }

        [DataTestMethod]
        [DataRow("First")]
        [DataRow("FirstOrDefault")]
        [DataRow("Single")]
        [DataRow("SingleOrDefault")]
        public void Take0_SubsetProjection_Terminal_NoQuery(string terminal)
        {
            var (marker, _) = SeedAbc();
            AssertEmptyTerminal(People(marker).Take(0).Select(p => new PersonWithEmployer { FirstName = p.FirstName }), terminal);
        }

        /// <summary>First/Single throw "no elements"; *OrDefault returns null (never an empty list as "first").</summary>
        private void AssertEmptyTerminal<TElement>(IQueryable<TElement> query, string terminal) where TElement : class
        {
            switch (terminal)
            {
                case "First": AssertThrowsNoQuery<InvalidOperationException>(() => query.First()); break;
                case "Single": AssertThrowsNoQuery<InvalidOperationException>(() => query.Single()); break;
                case "FirstOrDefault":
                {
                    TElement row = null;
                    AssertNoQuery(() => row = query.FirstOrDefault());
                    Assert.IsNull(row);
                    break;
                }
                case "SingleOrDefault":
                {
                    TElement row = null;
                    AssertNoQuery(() => row = query.SingleOrDefault());
                    Assert.IsNull(row);
                    break;
                }
                default: throw new ArgumentOutOfRangeException(nameof(terminal));
            }
        }

        [DataTestMethod]
        [DataRow("First")]
        [DataRow("FirstOrDefault")]
        [DataRow("Single")]
        [DataRow("SingleOrDefault")]
        public void ScalarProjection_Take0_Terminal_ThrowsScalarGuard_NoQuery(string terminal)
        {
            var (marker, _) = SeedAbc();
            var query = People(marker).Select(p => p.Id).Take(0);
            Func<int> run;
            switch (terminal)
            {
                case "First": run = () => query.First(); break;
                case "FirstOrDefault": run = () => query.FirstOrDefault(); break;
                case "Single": run = () => query.Single(); break;
                case "SingleOrDefault": run = () => query.SingleOrDefault(); break;
                default: throw new ArgumentOutOfRangeException(nameof(terminal));
            }

            var ex = AssertThrowsNoQuery<NotSupportedException>(() => run());
            StringAssert.Contains(ex.Message, "scalar projection");
        }

        [TestMethod]
        public void SkipNegative_BehavesAsSkipZero()
        {
            var (marker, _) = SeedAbc();

            AssertMatchesOracle(marker, q => q.OrderBy(p => p.Id).Skip(-1).ToList());
        }

        #endregion

        #region AC13-12 root-level ThenBy

        [TestMethod]
        public void ThenBy_OnRoot_IsPrimaryOrder()
        {
            var (marker, ids) = SeedAbc();

            // Descending: ascending would be indistinguishable from no order at all.
            var rows = ((IOrderedQueryable<PersonWithEmployer>)_provider.Query<PersonWithEmployer>())
                .ThenByDescending(p => p.Id)
                .Where(p => p.LastName == marker)
                .ToList();

            CollectionAssert.AreEqual(ids.AsEnumerable().Reverse().ToList(), rows.Select(r => r.Id).ToList());
        }

        #endregion

        #region AC13-14 Skip without Take

        [TestMethod]
        public void SkipOnly_ToList_Executes()
        {
            var (marker, ids) = SeedAbc();

            var rows = People(marker).OrderBy(p => p.Id).Skip(1).ToList();

            CollectionAssert.AreEqual(ids.Skip(1).ToList(), rows.Select(r => r.Id).ToList());
        }

        [TestMethod]
        public void SkipOnly_First_ReturnsExpectedRow()
        {
            var (marker, ids) = SeedAbc();

            Assert.AreEqual(ids[1], People(marker).Skip(1).First().Id);
        }

        #endregion

        #region AC13-15 non-generic IQueryProvider.Execute

        private static MethodCallExpression Call(string method, Type elementType, Expression source) =>
            Expression.Call(typeof(Queryable), method, new[] { elementType }, source);

        [TestMethod]
        public void NonGenericExecute_First_ReturnsSingleRow()
        {
            var (marker, ids) = SeedAbc();
            var query = People(marker).OrderBy(p => p.Id);

            var result = query.Provider.Execute(Call(nameof(Queryable.First), typeof(PersonWithEmployer), query.Expression));

            Assert.IsInstanceOfType(result, typeof(PersonWithEmployer), "3.9.0 returns the whole list");
            Assert.AreEqual(ids[0], ((PersonWithEmployer)result).Id);
        }

        [TestMethod]
        public void NonGenericExecute_FirstOnEmpty_Throws()
        {
            var query = People(NewMarker());

            Assert.ThrowsException<InvalidOperationException>(() =>
                query.Provider.Execute(Call(nameof(Queryable.First), typeof(PersonWithEmployer), query.Expression)));
        }

        [TestMethod]
        public void NonGenericExecute_RejectedOperator_ThrowsNotSupported()
        {
            var (marker, _) = SeedAbc();
            var query = People(marker);

            Assert.ThrowsException<NotSupportedException>(() =>
                query.Provider.Execute(Call(nameof(Queryable.Reverse), typeof(PersonWithEmployer), query.Expression)));
        }

        [TestMethod]
        public void NonGenericExecute_Count_ReturnsInt()
        {
            var (marker, _) = SeedAbc();
            var query = People(marker);

            var result = query.Provider.Execute(Call(nameof(Queryable.Count), typeof(PersonWithEmployer), query.Expression));

            Assert.AreEqual(3, result);
        }

        [TestMethod]
        public void NonGenericExecute_Collection_Unchanged()
        {
            var (marker, ids) = SeedAbc();
            var query = People(marker).OrderBy(p => p.Id);

            var result = query.Provider.Execute(query.Expression);

            var rows = ((IEnumerable)result).Cast<PersonWithEmployer>().ToList();
            CollectionAssert.AreEqual(ids, rows.Select(r => r.Id).ToList());
        }

        #endregion
    }
}
