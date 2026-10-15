using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.Tests.Parity
{
    /// <summary>
    /// WHERE parity (docs/plans/DECISIONS-3.10.2.md, D-001): a predicate is evaluated identically whether it sits under
    /// SELECT (<c>Query(predicate)</c>, <c>QueryAsync</c>, <c>Query&lt;T&gt;().Where</c>, <c>Count(predicate)</c>) or
    /// DELETE (<c>Delete</c>, <c>DeleteAsync</c>, each in a transaction that is rolled back). Every row asserts, against
    /// LINQ to objects over the same seed, that each path selects (counts, deletes) the same rows, and that the DELETE's
    /// WHERE text and parameter values are byte-identical to the SELECT's: one translation path, no per-operation branch.
    /// There is no update-by-predicate API (D-102), so UPDATE has no row.
    /// <para>
    /// D-003 as amended by D-128 (Paul, 2026-10-14): translation and evaluation are one shared path, so every path
    /// selects (or deletes) the rows LINQ to objects selects, with the parts of a predicate that don't read the row
    /// evaluated in C# (<c>x =&gt; true</c> is <c>NOT 1=0</c>, <c>s == "ZETA"</c> is <c>1=0</c>); the all-rows guard
    /// (<c>WherePredicateGuard</c>: no column, a self-comparison, always true) applies to Delete only. A key in
    /// <see cref="ExpectedDeleteRejection"/> asserts that Query, QueryAsync, Where and Count return C#'s rows while
    /// Delete and DeleteAsync throw that exception and send nothing. The file is compiled into SqlServer.Tests and
    /// linked into PostgreSql.Tests, MySql.Tests and Sqlite.Tests; one derived [TestClass] per provider supplies the
    /// table and the provider.
    /// </para>
    /// </summary>
    public abstract class WhereParityHarness
    {
        public const string Table = "zz_wp_row";

        public const string NoColumnMessage = "The WHERE clause must reference at least one column of the entity.";
        public const string SelfReferenceMessage = "The WHERE clause cannot be a self-referencing column expression.";
        public const string AlwaysTrueMessage = "The WHERE clause is trivially true; a non-trivial WHERE clause is required.";

        /// <summary>The start of the translators' message for a member of a member that isn't translated (FD-01).</summary>
        public static string MemberNotTranslated(string member) => "Member '" + member + "' of '";

        [Table(Table)]
        public class WpRow
        {
            [Key, Column("id")] public int Id { get; set; }
            [Column("name")] public string Name { get; set; }
            [Column("code")] public int Code { get; set; }
            [Column("posted_at")] public DateTime PostedAt { get; set; }
            [Column("nullable_id")] public int? NullableId { get; set; }
            [NotMapped] public WpInner Child { get; set; }
        }

        public class WpInner
        {
            public int Id { get; set; }
        }

        /// <summary>The seed, which the derived class inserts verbatim (<see cref="CreateTable"/>).</summary>
        public static readonly WpRow[] Rows =
        {
            new WpRow { Id = 1, Name = "abcd", Code = 7, PostedAt = new DateTime(2019, 6, 1), NullableId = 1 },
            new WpRow { Id = 2, Name = "xyz", Code = 70, PostedAt = new DateTime(2021, 6, 1), NullableId = null },
            new WpRow { Id = 3, Name = "abcdefg", Code = 5, PostedAt = new DateTime(2022, 1, 1), NullableId = 3 },
            new WpRow { Id = 4, Name = "ab", Code = 0, PostedAt = new DateTime(2020, 6, 1), NullableId = 4 },
            new WpRow { Id = 5, Name = "a_b", Code = 1, PostedAt = new DateTime(2020, 12, 31), NullableId = null },
            new WpRow { Id = 6, Name = "a%b", Code = 2, PostedAt = new DateTime(2021, 1, 15), NullableId = 6 },
        };

        #region Provider seam

        /// <summary>Marks the test inconclusive when the provider's test database is unavailable.</summary>
        protected virtual void RequireDatabase() { }

        /// <summary>Drops <c>zz_wp_row</c> if it exists, creates it and inserts <see cref="Rows"/>.</summary>
        protected abstract void CreateTable();

        /// <summary>Drops <c>zz_wp_row</c> (or deletes the SQLite file).</summary>
        protected abstract void DropTable();

        protected abstract OrmDataProvider CreateProvider();

        /// <summary>The dialect's SQL for a date part (<c>Year</c>, <c>Month</c>, <c>Day</c>) of <paramref name="column"/> (FD-01, AC-A2).</summary>
        protected abstract string DatePartSql(string part, string column);

        /// <summary>The dialect's SQL for the character length of <paramref name="column"/> (FD-01).</summary>
        protected abstract string LengthSql(string column);

        /// <summary>The dialect's cast of an integer <paramref name="column"/> to text, for a column's <c>ToString()</c> (FD-03, D4).</summary>
        protected abstract string CastToTextSql(string column);

        #endregion

        #region Captured values and predicates

        public class Request
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public Request Inner { get; set; }
        }

        public static class StaticHolder
        {
            public static int Id => 99;
        }

        /// <summary>Predicates over its own property: each captures <c>this</c>.</summary>
        public class Holder
        {
            public string Filter { get; set; } = "bcd";
            public Expression<Func<WpRow, bool>> NameContainsFilter() => x => x.Name.Contains(Filter);
        }

        private readonly List<int> ids = new List<int> { 1, 2 };
        private readonly List<int> emptyIds = new List<int>();
        private readonly Request absent = new Request { Id = 99, Name = "zeta", Inner = new Request { Id = 99 } };
        private readonly Request present = new Request { Id = 1 };
        private readonly string s = "zeta";
        private readonly string nullFilter = null;
        private readonly Holder holder = new Holder();

        protected Expression<Func<WpRow, bool>> Predicate(string key)
        {
            switch (key)
            {
                // FD-02: a captured value as a collection Contains item (Appendix B: deleted 3 rows, C# 1).
                case "idOrCapturedItem": return x => x.Id == 3 || ids.Contains(absent.Id);
                case "idAndCapturedItemPresent": return x => x.Id == 3 && ids.Contains(present.Id);
                case "idAndNotCapturedItem": return x => x.Id == 3 && !ids.Contains(absent.Id);
                case "idOrNestedCapturedItem": return x => x.Id == 3 || ids.Contains(absent.Inner.Id);
                case "idOrStaticItem": return x => x.Id == 3 || ids.Contains(StaticHolder.Id);
                case "idOrCapturedItemPresent": return x => x.Id == 3 || ids.Contains(present.Id);
                case "capturedItemAlone": return x => ids.Contains(absent.Id);
                // FD-02: a captured object's property as a search value, and a row-independent string Contains.
                case "nameContainsThisProperty": return holder.NameContainsFilter();
                case "idOrRowIndependentContains": return x => x.Id == 3 || s.Contains("ZZ");
                // FD-01: a member of a member of the row (Appendix B: Year > 2020 deleted 3 rows, C# 2; Length > 3
                // deleted every row, C# 2). Row 5 (2020-12-31) sits on the year boundary; row 6 on the month's.
                case "postedAtYearGt2020": return x => x.PostedAt.Year > 2020;
                case "postedAtYearEq2021": return x => x.PostedAt.Year == 2021;
                case "postedAtMonthEq6": return x => x.PostedAt.Month == 6;
                case "postedAtDayEq1": return x => x.PostedAt.Day == 1;
                case "postedAtYear2020AndMonth12": return x => x.PostedAt.Year == 2020 && x.PostedAt.Month == 12;
                case "idOrPostedAtYearGt2021": return x => x.Id == 4 || x.PostedAt.Year > 2021;
                case "nameLengthGt3": return x => x.Name.Length > 3;
                case "nameLengthEq3": return x => x.Name.Length == 3;
                // FD-01: member-of-member shapes that are rejected on every path (AC-A3).
                case "postedAtHour": return x => x.PostedAt.Hour == 0;
                case "postedAtDate": return x => x.PostedAt.Date == midnight;
                case "childId": return x => x.Child.Id == 1;
                case "conditionalLength": return x => (x.Id > 2 ? x.Name : "zz").Length > 2;
                // FD-03: LIKE metacharacters in a search value are matched literally (rows 5 'a_b' and 6 'a%b'); a column's
                // ToString() compares as text.
                case "nameContainsUnderscore": return x => x.Name.Contains("_");
                case "nameContainsPercent": return x => x.Name.Contains("%");
                case "nameStartsWithAPercent": return x => x.Name.StartsWith("a%");
                case "nameEndsWithUnderscoreB": return x => x.Name.EndsWith("_b");
                case "codeToStringEq7": return x => x.Code.ToString() == "7";
                case "codeToStringEq07": return x => x.Code.ToString() == "07";
                case "codeToStringEq70": return x => x.Code.ToString() == "70";
                // D-003/D-128: the all-rows shapes. Query returns every row (C#'s answer); Delete rejects them.
                case "trueLiteral": return x => true;
                case "oneLtTwo": return x => 1 < 2;
                case "idSelf": return x => x.Id == x.Id;
                case "idOrTrue": return x => x.Id == 2 || true;
                case "notEmptyContains": return x => !emptyIds.Contains(x.Id);
                case "optionalFilterNull": return x => nullFilter == null || x.Name.Contains(nullFilter);
                // False in C# (ordinal), true under a case-insensitive collation: evaluated in C# on every path (1=0).
                case "idOrCapturedStringEquals": return x => x.Id == 3 || s == "ZETA";
                // Shapes that already agreed; they must keep agreeing.
                case "idEquals": return x => x.Id == 2;
                case "idsContainsColumn": return x => ids.Contains(x.Id);
                case "notIdsContainsColumn": return x => !ids.Contains(x.Id);
                case "nameStartsWithLiteral": return x => x.Name.StartsWith("ab");
                case "nullableIsNull": return x => x.NullableId == null;
                default: throw new ArgumentException("Unknown predicate " + key, nameof(key));
            }
        }

        private readonly DateTime midnight = new DateTime(2021, 6, 1);

        /// <summary>A fragment the SELECT's (and so the DELETE's) WHERE text must contain: the dialect's function on the qualified column (FD-01, AC-A2).</summary>
        protected virtual string ExpectedWhereFragment(string key)
        {
            switch (key)
            {
                case "postedAtYearGt2020":
                case "postedAtYearEq2021":
                case "idOrPostedAtYearGt2021": return DatePartSql("Year", Table + ".posted_at");
                case "postedAtMonthEq6": return DatePartSql("Month", Table + ".posted_at");
                case "postedAtDayEq1": return DatePartSql("Day", Table + ".posted_at");
                case "postedAtYear2020AndMonth12": return DatePartSql("Month", Table + ".posted_at");
                case "nameLengthGt3":
                case "nameLengthEq3": return LengthSql(Table + ".name");
                case "nameContainsUnderscore":
                case "nameContainsPercent":
                case "nameStartsWithAPercent":
                case "nameEndsWithUnderscoreB": return " ESCAPE '!'";
                case "codeToStringEq7":
                case "codeToStringEq07":
                case "codeToStringEq70": return CastToTextSql(Table + ".code") + " = ";
                default: return null;
            }
        }

        /// <summary>
        /// The all-rows guard's rejection of a delete (D-003 as amended by D-128), as the exception type and its exact
        /// message; null when Delete must agree with Query. Query, QueryAsync, Where and Count run every key normally.
        /// </summary>
        protected virtual KeyValuePair<Type, string>? ExpectedDeleteRejection(string key)
        {
            switch (key)
            {
                // The evaluated Contains is true, so the WHERE is `… OR NOT 1=0`: the literal check after translation.
                case "idOrCapturedItemPresent":
                case "notEmptyContains":
                // Always true before translation: `true`, or `nullFilter == null` with a null filter.
                case "idOrTrue":
                case "optionalFilterNull":
                    return new KeyValuePair<Type, string>(typeof(InvalidOperationException), AlwaysTrueMessage);
                // The predicate reads no column.
                case "capturedItemAlone":
                case "trueLiteral":
                case "oneLtTwo":
                    return new KeyValuePair<Type, string>(typeof(InvalidOperationException), NoColumnMessage);
                case "idSelf":
                    return new KeyValuePair<Type, string>(typeof(InvalidOperationException), SelfReferenceMessage);
                default: return null;
            }
        }

        #endregion

        #region Plumbing

        protected static string Ids(IEnumerable<WpRow> rows) => string.Join(",", rows.Select(r => r.Id).OrderBy(i => i));

        private static string Normalize(string sql) => Regex.Replace(sql ?? string.Empty, @"\s+", " ").Trim();

        /// <summary>The WHERE body of the first logged command that starts with <paramref name="verb"/>, whitespace-normalized.</summary>
        private static string WhereOf(List<string> log, string verb, string context)
        {
            var command = log.FirstOrDefault(e => e != null && Regex.IsMatch(e, @"^\s*" + verb + @"\b", RegexOptions.IgnoreCase));
            Assert.IsNotNull(command, context + ": no " + verb + " logged: " + string.Join(" | ", log.Select(Normalize)));
            var text = Normalize(command);
            var match = Regex.Match(text, @"\bWHERE\b(.*)$", RegexOptions.IgnoreCase);
            Assert.IsTrue(match.Success, context + ": no WHERE in " + text);
            // Trailing clauses a SELECT may carry and a DELETE can't.
            return Regex.Replace(match.Groups[1].Value, @"\s+(ORDER BY|LIMIT|OFFSET)\b.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
        }

        /// <summary>The logged parameter values, in order.</summary>
        private static string ParametersOf(List<string> log) =>
            string.Join(" | ", log.Where(e => e != null && Regex.IsMatch(e, @"^@\w+: ")).Select(Normalize));

        private static List<int> Remaining(OrmDataProvider provider) => provider.Query<WpRow>().ToList().Select(r => r.Id).ToList();

        private void WithTable(Action<OrmDataProvider, List<string>> body)
        {
            RequireDatabase();
            try
            {
                CreateTable();
                using (var provider = CreateProvider())
                {
                    // Discover the type before the log is captured, so the first command logged is the query's.
                    provider.Query<WpRow>().Where(x => x.Id > 0).ToList();
                    var log = new List<string>();
                    provider.Log = entry => log.Add(entry);
                    body(provider, log);
                }
            }
            finally
            {
                DropTable();
            }
        }

        #endregion

        [DataTestMethod]
        [DataRow("idOrCapturedItem")]
        [DataRow("idAndCapturedItemPresent")]
        [DataRow("idAndNotCapturedItem")]
        [DataRow("idOrNestedCapturedItem")]
        [DataRow("idOrStaticItem")]
        [DataRow("idOrCapturedItemPresent")]
        [DataRow("capturedItemAlone")]
        [DataRow("nameContainsThisProperty")]
        [DataRow("idOrRowIndependentContains")]
        [DataRow("postedAtYearGt2020")]
        [DataRow("postedAtYearEq2021")]
        [DataRow("postedAtMonthEq6")]
        [DataRow("postedAtDayEq1")]
        [DataRow("postedAtYear2020AndMonth12")]
        [DataRow("idOrPostedAtYearGt2021")]
        [DataRow("nameLengthGt3")]
        [DataRow("nameLengthEq3")]
        [DataRow("nameContainsUnderscore")]
        [DataRow("nameContainsPercent")]
        [DataRow("nameStartsWithAPercent")]
        [DataRow("nameEndsWithUnderscoreB")]
        [DataRow("codeToStringEq7")]
        [DataRow("codeToStringEq07")]
        [DataRow("codeToStringEq70")]
        [DataRow("trueLiteral")]
        [DataRow("oneLtTwo")]
        [DataRow("idSelf")]
        [DataRow("idOrTrue")]
        [DataRow("notEmptyContains")]
        [DataRow("optionalFilterNull")]
        [DataRow("idOrCapturedStringEquals")]
        [DataRow("idEquals")]
        [DataRow("idsContainsColumn")]
        [DataRow("notIdsContainsColumn")]
        [DataRow("nameStartsWithLiteral")]
        [DataRow("nullableIsNull")]
        public void SelectCountAndDelete_AgreeWithLinqToObjects(string key)
        {
            var predicate = Predicate(key);
            var rejection = ExpectedDeleteRejection(key);
            var expected = Ids(Rows.Where(predicate.Compile()));
            var expectedCount = expected.Length == 0 ? 0 : expected.Split(',').Length;
            var fragment = ExpectedWhereFragment(key);

            WithTable((provider, log) =>
            {
                log.Clear();
                Assert.AreEqual(expected, Ids(provider.Query(predicate)), key + " query");
                var selectWhere = WhereOf(log, "SELECT", key + " query");
                var selectParameters = ParametersOf(log);
                if (fragment != null)
                    StringAssert.Contains(selectWhere, fragment, key + ": the WHERE doesn't apply the dialect's function to the qualified column");

                log.Clear();
                Assert.AreEqual(expected, Ids(provider.QueryAsync(predicate).GetAwaiter().GetResult()), key + " queryAsync");
                Assert.AreEqual(selectWhere, WhereOf(log, "SELECT", key + " queryAsync"), key + ": QueryAsync sent a different WHERE");

                log.Clear();
                Assert.AreEqual(expected, Ids(provider.Query<WpRow>().Where(predicate).ToList()), key + " linq");
                Assert.AreEqual(selectWhere, WhereOf(log, "SELECT", key + " linq"), key + ": Query<T>().Where sent a different WHERE");
                Assert.AreEqual(selectParameters, ParametersOf(log), key + ": Query<T>().Where sent different parameters");

                log.Clear();
                Assert.AreEqual(expectedCount, provider.Query<WpRow>().Count(predicate), key + " count");
                Assert.AreEqual(selectWhere, WhereOf(log, "SELECT", key + " count"), key + ": Count(predicate) sent a different WHERE");

                foreach (var async in new[] { false, true })
                {
                    var path = key + (async ? " deleteAsync" : " delete");
                    var transactional = (ISqlOrmProvider)provider;
                    transactional.BeginTransaction();
                    try
                    {
                        log.Clear();
                        Exception thrown = null;
                        try
                        {
                            if (async)
                                provider.DeleteAsync(predicate).GetAwaiter().GetResult();
                            else
                                provider.Delete(predicate);
                        }
                        catch (Exception ex)
                        {
                            thrown = ex;
                        }

                        if (rejection == null)
                        {
                            Assert.IsNull(thrown, path + ": Delete threw where Query returned rows: " + thrown?.Message);
                            var remaining = Remaining(provider);
                            var deleted = string.Join(",", Rows.Select(r => r.Id).Where(id => !remaining.Contains(id)));
                            Assert.AreEqual(expected, deleted, path + ": Delete removed other rows than Query selects");
                            Assert.AreEqual(selectWhere, WhereOf(log, "DELETE", path), path + ": the DELETE's WHERE text differs from the SELECT's");
                            Assert.AreEqual(selectParameters, ParametersOf(log), path + ": the DELETE's parameters differ from the SELECT's");
                        }
                        else
                        {
                            // D-128: an all-rows predicate runs normally as a query and is refused as a delete, before any command.
                            Assert.IsNotNull(thrown, path + ": expected the all-rows guard to refuse this delete");
                            Assert.AreEqual(rejection.Value.Key, thrown.GetType(), path + ": " + thrown.Message);
                            Assert.AreEqual(rejection.Value.Value, thrown.Message, path);
                            Assert.IsFalse(log.Any(e => e != null && Regex.IsMatch(e, @"^\s*DELETE\b", RegexOptions.IgnoreCase)), path + ": a DELETE was sent");
                            Assert.AreEqual(Rows.Length, Remaining(provider).Count, path + ": rows were deleted");
                        }
                    }
                    finally
                    {
                        transactional.RollbackTransaction();
                    }
                }
            });
        }

        /// <summary>
        /// A shape no path translates is rejected by Query, QueryAsync, Query&lt;T&gt;().Where, Count, Delete and
        /// DeleteAsync alike, with the same exception type and message, before any command is sent; nothing is deleted.
        /// </summary>
        private void EveryPathRejectsIdentically(Expression<Func<WpRow, bool>> predicate, string key, Type exceptionType, string messagePrefix)
        {
            WithTable((provider, log) =>
            {
                var calls = new List<KeyValuePair<string, Action>>
                {
                    new KeyValuePair<string, Action>("query", () => provider.Query(predicate)),
                    new KeyValuePair<string, Action>("queryAsync", () => provider.QueryAsync(predicate).GetAwaiter().GetResult()),
                    new KeyValuePair<string, Action>("linq", () => provider.Query<WpRow>().Where(predicate).ToList()),
                    new KeyValuePair<string, Action>("count", () => provider.Query<WpRow>().Count(predicate)),
                    new KeyValuePair<string, Action>("delete", () => provider.Delete(predicate)),
                    new KeyValuePair<string, Action>("deleteAsync", () => provider.DeleteAsync(predicate).GetAwaiter().GetResult()),
                };
                string firstMessage = null;
                foreach (var call in calls)
                {
                    var path = key + " " + call.Key;
                    var transactional = (ISqlOrmProvider)provider;
                    var isDelete = call.Key.StartsWith("delete", StringComparison.Ordinal);
                    if (isDelete)
                        transactional.BeginTransaction();
                    try
                    {
                        log.Clear();
                        Exception thrown = null;
                        try { call.Value(); } catch (Exception ex) { thrown = ex; }
                        Assert.IsNotNull(thrown, path + ": expected a rejection");
                        Assert.AreEqual(exceptionType, thrown.GetType(), path + ": " + thrown.Message);
                        StringAssert.StartsWith(thrown.Message, messagePrefix, path);
                        firstMessage = firstMessage ?? thrown.Message;
                        Assert.AreEqual(firstMessage, thrown.Message, path + ": the paths reject with different messages");
                        Assert.IsFalse(log.Any(e => e != null && Regex.IsMatch(e, @"^\s*(SELECT|DELETE)\b", RegexOptions.IgnoreCase)),
                            path + ": a command was sent: " + string.Join(" | ", log.Select(Normalize)));
                        Assert.AreEqual(Rows.Length, Remaining(provider).Count, path + ": rows were deleted");
                    }
                    finally
                    {
                        if (isDelete)
                            transactional.RollbackTransaction();
                    }
                }
            });
        }

        /// <summary>
        /// FD-01, AC-A3: a member of a member the dialect can't express is rejected with <see cref="NotSupportedException"/>
        /// naming the member, before any command is sent, by every path alike: Query, QueryAsync, Query&lt;T&gt;().Where,
        /// Count, Delete and DeleteAsync. Nothing is deleted. Before 3.11.0 the outer member was silently dropped
        /// (<c>x.PostedAt.Hour == 0</c> was sent as <c>posted_at = 0</c>).
        /// </summary>
        [DataTestMethod]
        [DataRow("postedAtHour", "Hour")]
        [DataRow("postedAtDate", "Date")]
        [DataRow("childId", "Id")]
        [DataRow("conditionalLength", "Length")]
        public void EveryPath_RejectsAMemberOfAMemberIdentically(string key, string member) =>
            EveryPathRejectsIdentically(Predicate(key), key, typeof(NotSupportedException), MemberNotTranslated(member));
    }
}
