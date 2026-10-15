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
    /// The delete guard (<c>DeletePredicateGuard</c>, 3.10.0 D9) still runs before a delete's translation and is not
    /// modified in 3.10.2 (D-003 is open). A shape it rejects is pinned in <see cref="ExpectedDeleteRejection"/> as an
    /// expected difference: Query returns C#'s rows, Delete throws and deletes nothing. Every such key is listed in the
    /// decisions log. The file is compiled into SqlServer.Tests and linked into PostgreSql.Tests, MySql.Tests and
    /// Sqlite.Tests; one derived [TestClass] per provider supplies the table and the provider.
    /// </para>
    /// </summary>
    public abstract class WhereParityHarness
    {
        public const string Table = "zz_wp_row";

        public const string NoColumnMessage = "Delete operation WHERE clause must reference at least one column from the target table.";
        public const string AlwaysTrueMessage = "Delete operation requires a non-trivial WHERE clause.";
        public const string GuardRowIndependentContains = "Contains() on a value that doesn't read the row isn't supported in a delete";
        public const string GuardCapturedPropertyValue = "Contains()'s search value is a property of a captured object";

        [Table(Table)]
        public class WpRow
        {
            [Key, Column("id")] public int Id { get; set; }
            [Column("name")] public string Name { get; set; }
            [Column("code")] public int Code { get; set; }
            [Column("posted_at")] public DateTime PostedAt { get; set; }
            [Column("nullable_id")] public int? NullableId { get; set; }
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
        private readonly Request absent = new Request { Id = 99, Name = "zeta", Inner = new Request { Id = 99 } };
        private readonly Request present = new Request { Id = 1 };
        private readonly string s = "zeta";
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
                // Shapes that already agreed; they must keep agreeing.
                case "idEquals": return x => x.Id == 2;
                case "idsContainsColumn": return x => ids.Contains(x.Id);
                case "notIdsContainsColumn": return x => !ids.Contains(x.Id);
                case "nameStartsWithLiteral": return x => x.Name.StartsWith("ab");
                case "nullableIsNull": return x => x.NullableId == null;
                default: throw new ArgumentException("Unknown predicate " + key, nameof(key));
            }
        }

        /// <summary>A rejection the delete guard makes (D-003 expected difference), or null when Delete must agree.</summary>
        protected virtual KeyValuePair<Type, string>? ExpectedDeleteRejection(string key)
        {
            switch (key)
            {
                // The evaluated Contains is true, so the WHERE is `… OR NOT 1=0`: the guard's literal check (AC9).
                case "idOrCapturedItemPresent": return new KeyValuePair<Type, string>(typeof(InvalidOperationException), AlwaysTrueMessage);
                // The predicate reads no column.
                case "capturedItemAlone": return new KeyValuePair<Type, string>(typeof(InvalidOperationException), NoColumnMessage);
                // D9: a search value that is a property of a captured object (the translation now reads it).
                case "nameContainsThisProperty": return new KeyValuePair<Type, string>(typeof(NotSupportedException), GuardCapturedPropertyValue);
                // D9: a string Contains on a value that doesn't read the row (the translation now answers it in C#).
                case "idOrRowIndependentContains": return new KeyValuePair<Type, string>(typeof(NotSupportedException), GuardRowIndependentContains);
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
        [DataRow("idEquals")]
        [DataRow("idsContainsColumn")]
        [DataRow("notIdsContainsColumn")]
        [DataRow("nameStartsWithLiteral")]
        [DataRow("nullableIsNull")]
        public void SelectCountAndDelete_AgreeWithLinqToObjects(string key)
        {
            var predicate = Predicate(key);
            var expected = Ids(Rows.Where(predicate.Compile()));
            var expectedCount = expected.Length == 0 ? 0 : expected.Split(',').Length;
            var rejection = ExpectedDeleteRejection(key);

            WithTable((provider, log) =>
            {
                log.Clear();
                Assert.AreEqual(expected, Ids(provider.Query(predicate)), key + " query");
                var selectWhere = WhereOf(log, "SELECT", key + " query");
                var selectParameters = ParametersOf(log);

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
                            // A D-003 expected difference: the guard rejects before translation; nothing is sent or deleted.
                            Assert.IsNotNull(thrown, path + ": expected the delete guard to reject this shape");
                            Assert.AreEqual(rejection.Value.Key, thrown.GetType(), path + ": " + thrown.Message);
                            StringAssert.StartsWith(thrown.Message, rejection.Value.Value, path);
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
    }
}
