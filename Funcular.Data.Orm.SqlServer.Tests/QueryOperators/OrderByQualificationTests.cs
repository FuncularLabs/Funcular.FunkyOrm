using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Linq;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Project;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests.QueryOperators
{
    /// <summary>
    /// #12: own-column ORDER BY on remote-join entities (AC12-1…AC12-9). The own column is <c>Id</c>: every joined
    /// table has an <c>id</c>, so an unqualified <c>ORDER BY id</c> is ambiguous once the projection no longer lists
    /// it. People are seeded so <c>FirstName</c> order equals <c>Id</c> order ("a", "b", "c"), and the narrow shapes
    /// assert the projected <c>FirstName</c> sequence.
    /// </summary>
    [TestClass]
    public class OrderByQualificationTests : QueryOperatorTestBase
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
            Assert.AreEqual("[country_0].name ASC", OrderByList(), "the resolved remote fragment, unchanged from 3.9.0, never prefixed");
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

        // Captured from 3.9.0 (b177d39) by a throwaway probe, 2026-09-30.
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

        // Typed values (review F1-F3): each is bound as the text 3.9.0 quoted, so the database converts it as it
        // converted the literal.

        private static readonly DateTime Moment = new DateTime(2026, 1, 2, 3, 4, 5);

        [TestMethod]
        public void TernaryOrderBy_GuidValue_MatchesOracle()
        {
            var marker = NewMarker();
            var employer = SeedEmployer("Q310T12_" + marker);
            var guids = new[] { new Guid("3c3c3c3c-0000-0000-0000-00000000000c"), new Guid("1a1a1a1a-0000-0000-0000-00000000000a"), new Guid("2b2b2b2b-0000-0000-0000-00000000000b") };
            for (var i = 0; i < guids.Length; i++)
                SeedTypedPerson(marker, "abc".Substring(i, 1), employer, guids[i], Moment);
            var target = guids[1]; // not the first row, so a value that never matches shows; hex letters, so case shows

            AssertMatchesOracle(marker, q => q.OrderBy(p => p.UniqueId == target ? 0 : 1).ThenBy(p => p.Id).ToList());
        }

        [DataTestMethod]
        [DataRow("==", 0, DateTimeKind.Unspecified)]
        [DataRow("==", 500, DateTimeKind.Unspecified)]
        [DataRow(">", 0, DateTimeKind.Unspecified)]
        [DataRow(">", 500, DateTimeKind.Unspecified)]
        [DataRow("==", 500, DateTimeKind.Utc)]
        [DataRow(">", 0, DateTimeKind.Utc)]
        public void TernaryOrderBy_DateTimeValue_MatchesOracle(string comparison, int milliseconds, DateTimeKind kind)
        {
            var marker = NewMarker();
            var employer = SeedEmployer("Q310T12_" + marker);
            var middle = Moment.AddMilliseconds(milliseconds);
            SeedTypedPerson(marker, "a", employer, null, middle.AddSeconds(-1));
            SeedTypedPerson(marker, "b", employer, null, middle);
            SeedTypedPerson(marker, "c", employer, null, middle.AddSeconds(1));
            var value = DateTime.SpecifyKind(middle, kind);

            if (comparison == "==")
                AssertMatchesOracle(marker, q => q.OrderBy(p => p.DateUtcCreated == value ? 0 : 1).ThenBy(p => p.Id).ToList());
            else
                AssertMatchesOracle(marker, q => q.OrderBy(p => p.DateUtcCreated > value ? 0 : 1).ThenBy(p => p.Id).ToList());
        }

        [TestMethod]
        public void TernaryOrderBy_GuidBranchValues_MatchesOracle()
        {
            // Text order (and Guid.CompareTo) puts low first; SQL Server's uniqueidentifier order compares the last six
            // bytes first and puts high first.
            var (marker, _) = SeedAbc();
            var high = new Guid("10000000-0000-0000-0000-000000000000");
            var low = new Guid("00000000-0000-0000-0000-000000000001");

            AssertMatchesOracle(marker, q => q.OrderBy(p => p.FirstName == "a" ? high : low).ThenBy(p => p.Id).ToList());
        }

        [TestMethod]
        public void TernaryOrderBy_DateTimeOffsetBranchValues_MatchesOracle()
        {
            // Same offset, so the values' text order is their order. Not UTC: a typed parameter can reject that.
            var (marker, _) = SeedAbc();
            var early = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(5));
            var late = early.AddDays(1);

            AssertMatchesOracle(marker, q => q.OrderBy(p => p.FirstName == "b" ? late : early).ThenBy(p => p.Id).ToList());
        }

        [TestMethod]
        public void TernaryOrderBy_NonAsciiText_MatchesOracle()
        {
            var marker = NewMarker();
            var employer = SeedEmployer("Q310T12_" + marker);
            SeedPeople(marker, employer, "alpha", "Ωmega", "zeta");
            var name = "Ωmega";

            AssertMatchesOracle(marker, q => q.OrderBy(p => p.FirstName == name ? 0 : 1).ThenBy(p => p.Id).ToList());
        }

        // Each occurrence of a value is typed where it's used, as each 3.9.0 literal was (verification N1/N2).

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        [DataRow(3)]
        public void TernaryOrderBy_CapturedValueComparedWithNull_MatchesOracle(int shape)
        {
            var (marker, _) = SeedAbc();
            var text = "x";
            Guid? guid = new Guid("1a1a1a1a-0000-0000-0000-00000000000a");
            DateTime? date = Moment;

            switch (shape)
            {
                case 0: AssertMatchesOracle(marker, q => q.OrderBy(p => text == null ? 0 : 1).ThenBy(p => p.Id).ToList()); break;
                case 1: AssertMatchesOracle(marker, q => q.OrderBy(p => text != null ? p.FirstName : p.LastName).ThenByDescending(p => p.Id).ToList()); break;
                case 2: AssertMatchesOracle(marker, q => q.OrderBy(p => null == guid ? p.LastName : p.FirstName).ThenByDescending(p => p.Id).ToList()); break;
                default: AssertMatchesOracle(marker, q => q.OrderBy(p => date != null ? p.FirstName : p.LastName).ThenByDescending(p => p.Id).ToList()); break;
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void TernaryOrderBy_OneValueAgainstADateThenATimestamp_MatchesOracle(bool lessThan)
        {
            // A value typed by its first use (a date) would compare the timestamp with midnight.
            var marker = NewMarker();
            var employer = SeedEmployer("Q310T12_" + marker);
            var day = Moment.Date;
            var noon = day.AddHours(12);
            SeedTypedPerson(marker, "a", employer, null, day.AddHours(6), day);
            SeedTypedPerson(marker, "b", employer, null, day.AddHours(11), day);
            SeedTypedPerson(marker, "c", employer, null, day.AddHours(13), day);

            if (lessThan)
                AssertMatchesOracle(marker, q => q.OrderBy(p => p.Birthdate > noon ? 1 : 0).ThenBy(p => p.DateUtcCreated < noon ? 1 : 0).ThenBy(p => p.Id).ToList());
            else
                AssertMatchesOracle(marker, q => q.OrderBy(p => p.Birthdate == noon ? 1 : 1).ThenBy(p => p.DateUtcCreated > noon ? 0 : 1).ThenBy(p => p.Id).ToList());
        }

        [DataTestMethod]
        [DataRow("guid")]
        [DataRow("date")]
        public void TernaryOrderBy_ValueAsBranchThenCompared_MatchesOracle(string kind)
        {
            // Used first as branch values (text), then against a uuid or timestamp column.
            var marker = NewMarker();
            var employer = SeedEmployer("Q310T12_" + marker);
            var guids = new[] { new Guid("3c3c3c3c-0000-0000-0000-00000000000c"), new Guid("1a1a1a1a-0000-0000-0000-00000000000a"), new Guid("2b2b2b2b-0000-0000-0000-00000000000b") };
            for (var i = 0; i < guids.Length; i++)
                SeedTypedPerson(marker, "abc".Substring(i, 1), employer, guids[i], Moment.AddSeconds(i - 1));
            var target = guids[1];
            var moment = Moment;

            if (kind == "guid")
                AssertMatchesOracle(marker, q => q.OrderBy(p => p.Id > 0 ? target : target).ThenBy(p => p.UniqueId == target ? 0 : 1).ThenBy(p => p.Id).ToList());
            else
                AssertMatchesOracle(marker, q => q.OrderBy(p => p.Id > 0 ? moment : moment).ThenBy(p => p.DateUtcCreated > moment ? 0 : 1).ThenBy(p => p.Id).ToList());
        }

        [Table("legacy_datetime_probe")]
        public class LegacyDateTimeProbe
        {
            public int Id { get; set; }
            public string Label { get; set; }
            public DateTime Stamp { get; set; }
        }

        [TestMethod]
        public void TernaryOrderBy_DateTimeValue_OnALegacyDatetimeColumn_ComparesAs390Did()
        {
            // A datetime (not datetime2) column. 3.9.0's text converts to datetime, so .003 matches the stored .003; a
            // datetime2 parameter compares the column as .0033333 and misses (verification N5). LINQ-to-objects reads
            // the stored value back as .0033333 too, so this pins 3.9.0's order rather than the oracle's. The table is a
            // fixture table (SqlServerTestFixture.EnsureSchema); this test touches only its own rows.
            var marker = NewMarker();
            var stamp = new DateTime(2026, 1, 2, 3, 4, 6, 3);
            try
            {
                var ids = new[] { -1, 0, 1 }.Select(offset =>
                {
                    var row = new LegacyDateTimeProbe { Label = marker, Stamp = stamp.AddSeconds(offset) };
                    _provider.Insert(row);
                    return row.Id;
                }).ToList();

                var ordered = _provider.Query<LegacyDateTimeProbe>().Where(p => p.Label == marker)
                    .OrderBy(p => p.Stamp == stamp ? 0 : 1).ThenBy(p => p.Id).ToList();

                CollectionAssert.AreEqual(new[] { ids[1], ids[0], ids[2] }, ordered.Select(p => p.Id).ToList());
            }
            finally
            {
                _provider.BeginTransaction();
                _provider.Delete<LegacyDateTimeProbe>(p => p.Label == marker);
                _provider.CommitTransaction();
            }
        }

        [TestMethod]
        public void TernaryOrderBy_DateOnlyAndTimeOnlyBranchValues_MatchesOracle()
        {
            // Across a year boundary, and seconds apart: invariant short formats (MM/dd/yyyy, HH:mm) don't sort these
            // chronologically (rev 39, J3).
            var (marker, _) = SeedAbc();
            var later = new DateOnly(2026, 1, 2);
            var earlier = new DateOnly(2025, 12, 31);
            var laterTime = new TimeOnly(10, 0, 30);
            var earlierTime = new TimeOnly(10, 0, 10);

            AssertMatchesOracle(marker, q => q.OrderBy(p => p.FirstName == "a" ? later : earlier).ThenBy(p => p.Id).ToList());
            AssertMatchesOracle(marker, q => q.OrderBy(p => p.FirstName == "a" ? laterTime : earlierTime).ThenBy(p => p.Id).ToList());
        }

        [TestMethod]
        public void AssertEveryParameterReferenced_ChecksEachParameterAgainstItsOwnCommand()
        {
            // The helper itself: a parameter is checked against the command it was logged with, not an earlier one,
            // and a value with a line break doesn't end its command.
            var log = _provider.Log;
            ClearLog();
            log("SELECT @p__linq__0");
            log("SELECT 1");
            log("@p__linq__0: x");
            Assert.ThrowsException<AssertFailedException>(() => AssertEveryParameterReferenced());

            ClearLog();
            log("SELECT @p__linq__0, @p__linq__1");
            log("@p__linq__0: two\nlines");
            log("@p__linq__1: y");
            AssertEveryParameterReferenced();
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
