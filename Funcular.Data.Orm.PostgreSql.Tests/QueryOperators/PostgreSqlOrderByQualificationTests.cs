using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Project;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.PostgreSql.Tests.QueryOperators
{
    /// <summary>
    /// #12: own-column ORDER BY on remote-join entities (AC12-1…AC12-9). The own column is <c>Id</c>: every joined
    /// table has an <c>id</c>, so an unqualified <c>ORDER BY id</c> is ambiguous (42702) once the projection no longer
    /// lists it. People are seeded so <c>FirstName</c> order equals <c>Id</c> order ("a", "b", "c"), and the narrow
    /// shapes assert the projected <c>FirstName</c> sequence.
    /// </summary>
    [TestClass]
    public class PostgreSqlOrderByQualificationTests : PostgreSqlQueryOperatorTestBase
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
            Assert.AreEqual("\"country_0\".name ASC", OrderByList(), "the resolved remote fragment, unchanged from 3.9.0, never prefixed");
        }

        [TestMethod]
        public void ComputedMemberOrderBy_EmitsExpression_Unchanged()
        {
            EnsureProjectTables();
            var marker = NewMarker();

            ClearLog();
            _provider.Query<ProjectScorecardFull>().Where(p => p.Name == marker).OrderBy(p => p.EffectiveScore).ToList();

            Assert.AreEqual("COALESCE(project.score, 0) ASC", OrderByList(), "the [SqlExpression] fragment, unchanged from 3.9.0");
        }

        #endregion

        #region AC12-3

        // Captured from 3.9.0 (6542796) by a throwaway probe, 2026-10-01; identical to the SQL Server command.
        private const string SingleTableOrderedCommand390 =
            "SELECT id, first_name, middle_initial, last_name, birthdate, gender, dateutc_created, dateutc_modified, uniqueid, employer_id " +
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

        [TestMethod]
        public void ComputedAttributeEntityWithoutJoins_OwnColumnOrder_Unqualified()
        {
            // AC12-3: no joins, so own columns stay bare, as in 3.9.0, even though computed attributes put entries in
            // the resolution map. Qualifying on "the map isn't empty" instead of "there are joins" would break this.
            var marker = NewMarker();
            SeedProject(marker, SeedEmployer("Q310Country_" + marker), 9);

            ClearLog();
            _provider.Query<ProjectScorecardFull>().Where(p => p.Name == marker).OrderBy(p => p.Name).ThenBy(p => p.Id).ToList();

            Assert.AreEqual("name ASC, id ASC", OrderByList());
        }

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
            // The value is a parameter since AC12-10; the own columns are qualified inside the CASE (AC12-4).
            StringAssert.Matches(OrderByList(), new System.Text.RegularExpressions.Regex(
                $@"CASE WHEN {PersonTable}\.middle_initial = @p__linq__\d+ THEN {PersonTable}\.id ELSE 0 END"));
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
        [DataRow(8, DisplayName = "x.M == null computed by a nested lambda")]
        public void TernaryOrderBy_NullComparison_MatchesOracle(int spelling)
        {
            var marker = NewMarker();
            var employer = SeedEmployer("Q310Country_" + marker);
            SeedPerson(marker, "a", employer, middleInitial: "A");
            SeedPerson(marker, "b", employer, middleInitial: null);
            SeedPerson(marker, "c", employer, middleInitial: "C");

            string none = null; // rows 4-7: the null is held in a variable, not written as a literal
            var names = new[] { "a" }; // row 8: the null comes from an operand with its own lambda
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
                case 8: key = p => p.MiddleInitial == names.FirstOrDefault(n => n.Length > 100) ? 0 : 1; break;
                default: throw new ArgumentOutOfRangeException(nameof(spelling));
            }

            AssertMatchesOracle(marker, q => q.OrderBy(key).ThenBy(p => p.Id).ToList());
        }

        #endregion

        #region AC12-10 values in an ORDER BY ternary are parameters

        private static readonly string[] AwkwardNames = { "O'Brien", "C:\\path\\", "back\\'slash" };

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        public void TernaryOrderBy_TextWithQuotesOrBackslashes_IsAParameter_MatchesOracle(int target)
        {
            var marker = NewMarker();
            var employer = SeedEmployer("Q310T12_" + marker);
            foreach (var awkward in AwkwardNames)
                SeedPerson(marker, awkward, employer);
            var name = AwkwardNames[target];

            ClearLog();
            AssertMatchesOracle(marker, q => q.OrderBy(p => p.FirstName == name ? 0 : 1).ThenBy(p => p.Id).ToList());
            var commands = CommandTexts();
            Assert.IsFalse(commands.Contains(name) || commands.Contains(name.Replace("'", "''")), "the value is sent as a parameter: " + commands);
        }

        [TestMethod]
        public void Last_AfterTextTernaryOrderBy_InvertedOrderKeepsItsParameters()
        {
            var (marker, _) = SeedAbc();

            ClearLog();
            AssertMatchesOracle(marker, q => q.OrderBy(p => p.FirstName == "b" ? 0 : 1).ThenBy(p => p.Id).Last());
            Assert.IsFalse(CommandTexts().Contains("'b'"), CommandTexts());
        }

        [TestMethod]
        public void TextTernaryOrderBy_WithWhereParameters_MatchesOracle()
        {
            var (marker, _) = SeedAbc();

            AssertMatchesOracle(marker, q => q.Where(p => p.FirstName != "c").OrderBy(p => p.FirstName == "b" ? 0 : 1).ThenBy(p => p.Id).ToList());
        }

        [TestMethod]
        public void Count_AfterTextTernaryOrderBy_Works()
        {
            // The aggregate drops the ORDER BY, and with it the ORDER BY's parameters.
            var (marker, _) = SeedAbc();

            ClearLog();
            Assert.AreEqual(3, People(marker).OrderBy(p => p.FirstName == "b" ? 0 : 1).Count());
            AssertEveryParameterReferenced();
        }

        [TestMethod]
        public void TextTernaryOrderings_SendOnlyTheParametersTheCommandUses()
        {
            // Each ordering call re-visits the chain; only the last visit's parameters belong to the command.
            var (marker, _) = SeedAbc();

            ClearLog();
            AssertMatchesOracle(marker, q => q.OrderBy(p => p.FirstName == "a" ? 0 : 1).ThenBy(p => p.FirstName == "c" ? 0 : 1).ThenBy(p => p.Id).ToList());
            AssertEveryParameterReferenced();
        }

        [TestMethod]
        public void ScalarProjection_AfterTextTernaryOrderBy_BindsTheParameters()
        {
            var (marker, _) = SeedAbc();

            var names = People(marker).OrderBy(p => p.FirstName == "b" ? 0 : 1).ThenBy(p => p.Id).Select(p => p.FirstName).ToList();

            CollectionAssert.AreEqual(new[] { "b", "a", "c" }, names);
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
