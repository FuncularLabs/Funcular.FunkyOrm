using System.Linq.Expressions;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Project;

namespace Funcular.Data.Orm.Sqlite.Tests.QueryOperators
{
    /// <summary>
    /// #12: own-column ORDER BY on remote-join entities (AC12-1…AC12-9). The own column is <c>Id</c>: every joined
    /// table has an <c>id</c>, so an unqualified <c>ORDER BY id</c> is ambiguous. On SQLite that holds even for the
    /// full entity (its SELECT list has no <c>id</c> alias, §1.3), and the default paging order <c>rowid</c> is
    /// ambiguous too (AC12-7). People are seeded so <c>FirstName</c> order equals <c>Id</c> order ("a", "b", "c"),
    /// and the narrow shapes assert the projected <c>FirstName</c> sequence.
    /// </summary>
    [TestClass]
    public class SqliteOrderByQualificationTests : SqliteQueryOperatorTestBase
    {
        [ClassCleanup]
        public static void DeleteDatabase() => DeleteClassDatabase(typeof(SqliteOrderByQualificationTests));

        #region AC12-1

        [DataTestMethod]
        [DataRow("OrderBy", "full", false)]
        [DataRow("OrderBy", "full", true)]
        [DataRow("OrderBy", "subset", false)]
        [DataRow("OrderBy", "subset", true)]
        [DataRow("OrderBy", "scalar", false)]
        [DataRow("OrderBy", "scalar", true)]
        [DataRow("OrderByDescending", "full", false)]
        [DataRow("OrderByDescending", "full", true)]
        [DataRow("OrderByDescending", "subset", false)]
        [DataRow("OrderByDescending", "subset", true)]
        [DataRow("OrderByDescending", "scalar", false)]
        [DataRow("OrderByDescending", "scalar", true)]
        [DataRow("ThenBy", "full", false)]
        [DataRow("ThenBy", "full", true)]
        [DataRow("ThenBy", "subset", false)]
        [DataRow("ThenBy", "subset", true)]
        [DataRow("ThenBy", "scalar", false)]
        [DataRow("ThenBy", "scalar", true)]
        [DataRow("ThenByDescending", "full", false)]
        [DataRow("ThenByDescending", "full", true)]
        [DataRow("ThenByDescending", "subset", false)]
        [DataRow("ThenByDescending", "subset", true)]
        [DataRow("ThenByDescending", "scalar", false)]
        [DataRow("ThenByDescending", "scalar", true)]
        public void OwnColumnOrdering_OnJoinEntity_QualifiedSql_ExecutesInOrder(string ordering, string shape, bool paged)
        {
            var (marker, _) = SeedAbc();
            IQueryable<PersonDetailEntity> query = People(marker);
            switch (ordering)
            {
                case "OrderBy": query = query.OrderBy(p => p.Id); break;
                case "OrderByDescending": query = query.OrderByDescending(p => p.Id); break;
                // All three people share one employer, so the remote key ties and Id decides.
                case "ThenBy": query = query.OrderBy(p => p.EmployerHeadquartersCountryName).ThenBy(p => p.Id); break;
                case "ThenByDescending": query = query.OrderBy(p => p.EmployerHeadquartersCountryName).ThenByDescending(p => p.Id); break;
                default: throw new ArgumentOutOfRangeException(nameof(ordering));
            }
            if (paged)
                query = query.Skip(1).Take(2);

            ClearLog();
            List<string> names;
            switch (shape)
            {
                case "full": names = query.ToList().Select(p => p.FirstName).ToList(); break;
                case "subset": names = query.Select(p => new PersonDetailEntity { FirstName = p.FirstName }).ToList().Select(p => p.FirstName).ToList(); break;
                case "scalar": names = query.Select(p => p.FirstName).ToList(); break;
                default: throw new ArgumentOutOfRangeException(nameof(shape));
            }

            var descending = ordering.EndsWith("Descending", StringComparison.Ordinal);
            IEnumerable<string> expected = descending ? new[] { "c", "b", "a" } : new[] { "a", "b", "c" };
            if (paged)
                expected = expected.Skip(1).Take(2);
            CollectionAssert.AreEqual(expected.ToList(), names, "rows must come back in the requested order");
            StringAssert.Contains(OrderByList(), $"{PersonTable}.id {(descending ? "DESC" : "ASC")}",
                "own column must be emitted as {baseTable}.{column}");
        }

        #endregion

        #region AC12-2

        [TestMethod]
        public void RemoteMemberOrderBy_EmitsExactResolvedFragment_NoBasePrefix()
        {
            var (marker, _) = SeedAbc();

            ClearLog();
            var rows = People(marker).OrderBy(p => p.EmployerHeadquartersCountryName).ToList();

            Assert.AreEqual(3, rows.Count);
            Assert.AreEqual("\"country_0\".Name ASC", OrderByList(), "the resolved remote fragment, unchanged from 3.9.0, never prefixed");
        }

        [TestMethod]
        public void ComputedMemberOrderBy_EmitsExpression_Unchanged()
        {
            var marker = NewMarker();

            ClearLog();
            _provider.Query<ProjectScorecardFull>().Where(p => p.Name == marker).OrderBy(p => p.EffectiveScore).ToList();

            Assert.AreEqual("COALESCE(project.score, 0) ASC", OrderByList(), "the [SqlExpression] fragment, unchanged from 3.9.0");
        }

        #endregion

        #region AC12-3

        // Captured from 3.9.0 (6542796) by a throwaway probe, 2026-10-01.
        private const string SingleTableOrderedCommand390 =
            "SELECT id, first_name, middle_initial, last_name, birthdate, gender, dateutc_created, dateutc_modified, unique_id, employer_id " +
            "FROM person WHERE person.last_name = @p__linq__0 ORDER BY first_name ASC, id DESC";

        [TestMethod]
        public void SingleTableEntity_OrderBy_SqlByteIdenticalTo390()
        {
            var marker = NewMarker();

            ClearLog();
            _provider.Query<PersonEntity>().Where(p => p.LastName == marker).OrderBy(p => p.FirstName).ThenByDescending(p => p.Id).ToList();
            Assert.AreEqual(SingleTableOrderedCommand390, CommandText(), "enumeration");

            ClearLog();
            _provider.Query<PersonEntity>().Where(p => p.LastName == marker).OrderBy(p => p.FirstName).ThenByDescending(p => p.Id).FirstOrDefault();
            Assert.AreEqual(SingleTableOrderedCommand390, CommandText(), "FirstOrDefault");
        }

        private string CommandText()
        {
            var sql = Sql;
            var parameters = sql.IndexOf(" @p__linq__0:", StringComparison.Ordinal);
            return parameters >= 0 ? sql.Substring(0, parameters) : sql;
        }

        #endregion

        #region AC12-4

        [TestMethod]
        public void TernaryOrderBy_OwnColumns_OnJoinEntity_QualifiedInsideCase()
        {
            var marker = NewMarker();
            var employer = SeedEmployer("Q310Country_" + marker);
            SeedPerson(marker, "a", employer, middleInitial: "Z");
            SeedPerson(marker, "b", employer, middleInitial: "M");
            SeedPerson(marker, "c", employer, middleInitial: "Z");

            ClearLog();
            var names = People(marker).OrderBy(p => p.MiddleInitial == "Z" ? p.Id : 0).Select(p => p.FirstName).ToList();

            // Keys: a = its id, b = 0, c = its id (> a's) → b, a, c.
            CollectionAssert.AreEqual(new[] { "b", "a", "c" }, names);
            StringAssert.Contains(OrderByList(), $"CASE WHEN {PersonTable}.middle_initial = 'Z' THEN {PersonTable}.id ELSE 0 END");
        }

        #endregion

        #region AC12-5

        [TestMethod]
        public void Last_OnJoinEntity_ProjectionWithoutKey_SynthesizedOrderQualified()
        {
            var (marker, _) = SeedAbc();

            ClearLog();
            var last = People(marker).Select(p => new PersonDetailEntity { FirstName = p.FirstName }).Last();

            Assert.AreEqual("c", last.FirstName, "Last() with no order is the max-id row (3.9.0 returns the first row)");
            StringAssert.Contains(OrderByList(), $"{PersonTable}.id DESC");
        }

        #endregion

        #region AC12-6

        [TestMethod]
        public void Distinct_Projection_JoinEntity_OrderByKeyInProjection_Executes()
        {
            var (marker, ids) = SeedAbc();

            var rows = People(marker).OrderBy(p => p.Id)
                .Select(p => new PersonDetailEntity { Id = p.Id, FirstName = p.FirstName })
                .Distinct()
                .ToList();

            CollectionAssert.AreEqual(ids, rows.Select(r => r.Id).ToList());
        }

        [TestMethod]
        public void Distinct_Projection_JoinEntity_OrderByKeyNotInProjection_ThrowsExisting()
        {
            var (marker, _) = SeedAbc();

            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                People(marker).OrderBy(p => p.MiddleInitial)
                    .Select(p => new PersonDetailEntity { FirstName = p.FirstName })
                    .Distinct()
                    .ToList());
            StringAssert.Contains(ex.Message, "every ORDER BY key must be part of the projection");
        }

        #endregion

        #region AC12-7 (SQLite: the default ORDER BY rowid is ambiguous once joins exist)

        [TestMethod]
        public void DefaultPaging_OnJoinEntity_Executes()
        {
            var (marker, ids) = SeedAbc();

            var rows = People(marker).Skip(1).Take(2).ToList();

            CollectionAssert.AreEqual(ids.Skip(1).ToList(), rows.Select(r => r.Id).ToList());
        }

        [TestMethod]
        public void DefaultPaging_OnJoinEntity_SubsetProjection_Executes()
        {
            var (marker, _) = SeedAbc();

            var names = People(marker).Skip(1).Take(2).Select(p => new PersonDetailEntity { FirstName = p.FirstName }).ToList();

            CollectionAssert.AreEqual(new[] { "b", "c" }, names.Select(n => n.FirstName).ToList());
        }

        #endregion

        #region AC12-8

        [DataTestMethod]
        [DataRow(0, DisplayName = "x.M == null")]
        [DataRow(1, DisplayName = "null == x.M")]
        [DataRow(2, DisplayName = "x.M != null")]
        [DataRow(3, DisplayName = "null != x.M")]
        [DataRow(4, DisplayName = "x.M == captured null")]
        [DataRow(5, DisplayName = "captured null == x.M")]
        [DataRow(6, DisplayName = "x.M != captured null")]
        [DataRow(7, DisplayName = "captured null != x.M")]
        public void TernaryOrderBy_NullComparison_MatchesOracle(int spelling)
        {
            var marker = NewMarker();
            var employer = SeedEmployer("Q310Country_" + marker);
            SeedPerson(marker, "a", employer, middleInitial: "A");
            SeedPerson(marker, "b", employer, middleInitial: null);
            SeedPerson(marker, "c", employer, middleInitial: "C");

            string none = null; // rows 4-7: the null is held in a variable, not written as a literal
            Expression<Func<PersonDetailEntity, int>> key;
            switch (spelling)
            {
                case 0: key = p => p.MiddleInitial == null ? 0 : 1; break;
                case 1: key = p => null == p.MiddleInitial ? 0 : 1; break;
                case 2: key = p => p.MiddleInitial != null ? 0 : 1; break;
                case 3: key = p => null != p.MiddleInitial ? 0 : 1; break;
                case 4: key = p => p.MiddleInitial == none ? 0 : 1; break;
                case 5: key = p => none == p.MiddleInitial ? 0 : 1; break;
                case 6: key = p => p.MiddleInitial != none ? 0 : 1; break;
                case 7: key = p => none != p.MiddleInitial ? 0 : 1; break;
                default: throw new ArgumentOutOfRangeException(nameof(spelling));
            }

            // Tie-break on FirstName (= Id order), not Id: on SQLite a bare id is AC12-1's ambiguity, not this AC's.
            AssertMatchesOracle(marker, q => q.OrderBy(key).ThenBy(p => p.FirstName).ToList());
        }

        #endregion

        #region AC12-9

        [TestMethod]
        public void ThenBy_SameKeyTwice_Executes()
        {
            var (marker, ids) = SeedAbc();

            ClearLog();
            var rows = People(marker).OrderBy(p => p.Id).ThenBy(p => p.Id).ToList();

            CollectionAssert.AreEqual(ids, rows.Select(r => r.Id).ToList());
            Assert.AreEqual($"{PersonTable}.id ASC", OrderByList(), "the later duplicate fragment is dropped");
        }

        #endregion
    }
}
