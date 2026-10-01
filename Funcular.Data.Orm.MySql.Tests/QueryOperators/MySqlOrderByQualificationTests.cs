using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Funcular.Data.Orm.MySql.Tests.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.MySql.Tests.QueryOperators
{
    /// <summary>
    /// #12: own-column ORDER BY on remote-join entities (AC12-1…AC12-9). The own column is <c>Id</c>: the joined
    /// <c>organization</c> table has an <c>id</c>, so an unqualified <c>ORDER BY id</c> is ambiguous once the projection
    /// no longer lists it (MySQL error 1052). People are seeded so <c>FirstName</c> order equals <c>Id</c> order
    /// ("a", "b", "c"), and the narrow shapes assert the projected <c>FirstName</c> sequence.
    /// <see cref="PersonWithEmployer"/> doesn't map <c>MiddleInitial</c>/<c>Gender</c>; the ternary tests use the
    /// own nullable column <c>EmployerId</c> instead.
    /// </summary>
    [TestClass]
    public class MySqlOrderByQualificationTests : MySqlQueryOperatorTestBase
    {
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
            IQueryable<PersonWithEmployer> query = People(marker);
            switch (ordering)
            {
                case "OrderBy": query = query.OrderBy(p => p.Id); break;
                case "OrderByDescending": query = query.OrderByDescending(p => p.Id); break;
                // All three people share one employer, so the remote key ties and Id decides.
                case "ThenBy": query = query.OrderBy(p => p.EmployerName).ThenBy(p => p.Id); break;
                case "ThenByDescending": query = query.OrderBy(p => p.EmployerName).ThenByDescending(p => p.Id); break;
                default: throw new ArgumentOutOfRangeException(nameof(ordering));
            }
            if (paged)
                query = query.Skip(1).Take(2);

            ClearLog();
            List<string> names;
            switch (shape)
            {
                case "full": names = query.ToList().Select(p => p.FirstName).ToList(); break;
                case "subset": names = query.Select(p => new PersonWithEmployer { FirstName = p.FirstName }).ToList().Select(p => p.FirstName).ToList(); break;
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
            var rows = People(marker).OrderBy(p => p.EmployerName).ToList();

            Assert.AreEqual(3, rows.Count);
            Assert.AreEqual("`organization_0`.name ASC", OrderByList(), "the resolved remote fragment, unchanged from 3.9.0, never prefixed");
        }

        [TestMethod]
        public void ComputedMemberOrderBy_EmitsExpression_Unchanged()
        {
            var marker = NewMarker();

            ClearLog();
            _provider.Query<ProjectScorecard>().Where(p => p.Name == marker).OrderBy(p => p.EffectiveScore).ToList();

            Assert.AreEqual("COALESCE(project.score, 0) ASC", OrderByList(), "the [SqlExpression] fragment, unchanged from 3.9.0");
        }

        #endregion

        #region AC12-3

        // Captured from 3.9.0 (6542796) by a throwaway probe, 2026-10-01.
        private const string SingleTableOrderedCommand390 =
            "SELECT middle_initial, birthdate, gender, uniqueid, employer_id, dateutc_created, dateutc_modified, id, first_name, last_name " +
            "FROM person WHERE person.last_name = @p__linq__0 ORDER BY first_name ASC, id DESC";

        [TestMethod]
        public void SingleTableEntity_OrderBy_SqlByteIdenticalTo390()
        {
            var marker = NewMarker();

            ClearLog();
            _provider.Query<Person>().Where(p => p.LastName == marker).OrderBy(p => p.FirstName).ThenByDescending(p => p.Id).ToList();
            Assert.AreEqual(SingleTableOrderedCommand390, CommandText(), "enumeration");

            ClearLog();
            _provider.Query<Person>().Where(p => p.LastName == marker).OrderBy(p => p.FirstName).ThenByDescending(p => p.Id).FirstOrDefault();
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
            var employerZ = SeedEmployer("Q310OrgZ_" + marker);
            var employerM = SeedEmployer("Q310OrgM_" + marker);
            SeedPerson(marker, "a", employerZ);
            SeedPerson(marker, "b", employerM);
            SeedPerson(marker, "c", employerZ);

            ClearLog();
            var names = People(marker).OrderBy(p => p.EmployerId == employerZ ? p.Id : 0).Select(p => p.FirstName).ToList();

            // Keys: a = its id, b = 0, c = its id (> a's) → b, a, c.
            CollectionAssert.AreEqual(new[] { "b", "a", "c" }, names);
            StringAssert.Contains(OrderByList(), $"CASE WHEN {PersonTable}.employer_id = {employerZ} THEN {PersonTable}.id ELSE 0 END");
        }

        #endregion

        #region AC12-5

        [TestMethod]
        public void Last_OnJoinEntity_ProjectionWithoutKey_SynthesizedOrderQualified()
        {
            var (marker, _) = SeedAbc();

            ClearLog();
            var last = People(marker).Select(p => new PersonWithEmployer { FirstName = p.FirstName }).Last();

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
                .Select(p => new PersonWithEmployer { Id = p.Id, FirstName = p.FirstName })
                .Distinct()
                .ToList();

            CollectionAssert.AreEqual(ids, rows.Select(r => r.Id).ToList());
        }

        [TestMethod]
        public void Distinct_Projection_JoinEntity_OrderByKeyNotInProjection_ThrowsExisting()
        {
            var (marker, _) = SeedAbc();

            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                People(marker).OrderBy(p => p.EmployerId)
                    .Select(p => new PersonWithEmployer { FirstName = p.FirstName })
                    .Distinct()
                    .ToList());
            StringAssert.Contains(ex.Message, "every ORDER BY key must be part of the projection");
        }

        #endregion

        #region AC12-7 (SQLite; regression rows here)

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

            var names = People(marker).Skip(1).Take(2).Select(p => new PersonWithEmployer { FirstName = p.FirstName }).ToList();

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
        [DataRow(8, DisplayName = "x.M == null computed by a nested lambda")]
        public void TernaryOrderBy_NullComparison_MatchesOracle(int spelling)
        {
            var marker = NewMarker();
            var employer = SeedEmployer("Q310Org_" + marker);
            SeedPerson(marker, "a", employer);
            // The seeded null. §4.1 seeds remote LEFT-JOIN keys non-null, but this one is harmless: no predicate or
            // ordering references a remote member (the CASE reads the own column employer_id and is never NULL; Id
            // breaks ties), so the NULL-joined remote columns can't change which rows match or their order.
            SeedPerson(marker, "b", null);
            SeedPerson(marker, "c", employer);

            int? none = null; // rows 4-7: the null is held in a variable, not written as a literal
            var nums = new int?[] { 1 }; // row 8: the null comes from an operand with its own lambda
            Expression<Func<PersonWithEmployer, int>> key;
            switch (spelling)
            {
                case 0: key = p => p.EmployerId == null ? 0 : 1; break;
                case 1: key = p => null == p.EmployerId ? 0 : 1; break;
                case 2: key = p => p.EmployerId != null ? 0 : 1; break;
                case 3: key = p => null != p.EmployerId ? 0 : 1; break;
                case 4: key = p => p.EmployerId == none ? 0 : 1; break;
                case 5: key = p => none == p.EmployerId ? 0 : 1; break;
                case 6: key = p => p.EmployerId != none ? 0 : 1; break;
                case 7: key = p => none != p.EmployerId ? 0 : 1; break;
                case 8: key = p => p.EmployerId == nums.FirstOrDefault(n => n > 1000) ? 0 : 1; break;
                default: throw new ArgumentOutOfRangeException(nameof(spelling));
            }

            AssertMatchesOracle(marker, q => q.OrderBy(key).ThenBy(p => p.Id).ToList());
        }

        #endregion

        #region AC12-9

        [TestMethod]
        public void ThenBy_SameTernaryKeyTwice_Executes()
        {
            var (marker, _) = SeedAbc();

            // SQL Server rejects a CASE key listed twice (error 169), like a column: the later one is dropped.
            ClearLog();
            AssertMatchesOracle(marker, q => q.OrderBy(p => p.FirstName == "b" ? 1 : 0).ThenBy(p => p.FirstName == "b" ? 1 : 0).ThenBy(p => p.Id).ToList());
            Assert.AreEqual(1, OrderByList().Split(new[] { "CASE WHEN" }, StringSplitOptions.None).Length - 1, OrderByList());
        }

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
