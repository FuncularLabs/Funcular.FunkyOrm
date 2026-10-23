using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using Funcular.Data.Orm.Attributes;
using Microsoft.Data.Sqlite;

namespace Funcular.Data.Orm.Sqlite.Tests.NullSemantics
{
    /// <summary>
    /// R16-1 (3.12.0, docs/plans/NULL_SEMANTICS_PLAN.md §4.3): a computed property whose template is SQLite's own text for
    /// a column's <c>Length</c> or year, compared with that part of the column, is the column's part compared with itself.
    /// 3.11.0 refused the delete as self-referencing; EF's form, <c>(A = A OR (A IS NULL AND col IS NULL))</c>, tests two
    /// witnesses, and is refused too. The query selects every row, as the comparison holds for every row.
    /// </summary>
    [TestClass]
    public class SqliteComputedPartSelfReferenceTests
    {
        private const string SelfReferenceMessage = "The WHERE clause cannot be a self-referencing column expression.";
        private const string AlwaysTrueMessage = "The WHERE clause is trivially true; a non-trivial WHERE clause is required.";
        private const string UnboundedSearchMessage = "The WHERE clause's Contains, StartsWith or EndsWith has an empty search value, which matches every non-null row; a non-trivial WHERE clause is required.";
        private static bool CapturedTrue = true;

        [Table("zz_r16_row")]
        public class R16Row
        {
            [Key, Column("id")] public int Id { get; set; }
            [Column("name")] public string Name { get; set; }
            [Column("d")] public DateTime? D { get; set; }
            [SqlExpression("LENGTH({Name})")] public int? NameLen { get; set; }
            [SqlExpression("CAST(strftime('%Y', {D}) AS INTEGER)")] public int? DYear { get; set; }
            [SqlExpression("{Name}")] public string NameX { get; set; }
        }

        /// <summary>
        /// R18-1: the same twins inside nested conditionals (and a template that differs only in letter case, two
        /// [JsonPath] properties on one path), where the rewrite distributes the comparison into its branches.
        /// </summary>
        [Table("zz_r18_row")]
        public class R18Row
        {
            [Key, Column("id")] public int Id { get; set; }
            [Column("n")] public int? N { get; set; }
            [Column("m")] public int? M { get; set; }
            [Column("code")] public int Code { get; set; }
            [Column("active")] public bool Active { get; set; }
            [Column("name")] public string Name { get; set; }
            [Column("d")] public DateTime? D { get; set; }
            [SqlExpression("LENGTH({Name})")] public int? NameLen { get; set; }
            [SqlExpression("length({Name})")] public int? NameLenLower { get; set; }
            [SqlExpression("CAST(strftime('%Y', {D}) AS INTEGER)")] public int? DYear { get; set; }
            [JsonPath("doc", "$.k")] public int? K { get; set; }
            [JsonPath("doc", "$.k")] public int? K2 { get; set; }
            // R19-3: another JSON key, which differs from k only in letter case: another value.
            [JsonPath("doc", "$.K")] public int? KUpper { get; set; }
        }

        private string _path;

        [TestInitialize]
        public void CreateTable()
        {
            _path = Path.Combine(Path.GetTempPath(), "zz_r16_" + Guid.NewGuid().ToString("N") + ".db");
            using (var connection = new SqliteConnection("Data Source=" + _path))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE TABLE zz_r16_row (id INTEGER PRIMARY KEY, name TEXT, d TEXT);" +
                                          "INSERT INTO zz_r16_row (id, name, d) VALUES (1, 'a', '2020-01-01 00:00:00'), (2, NULL, '2021-02-03 00:00:00')," +
                                          " (3, 'abc', NULL), (4, NULL, NULL), (5, 'de', '2022-03-04 00:00:00');" +
                                          "CREATE TABLE zz_r18_row (id INTEGER PRIMARY KEY, n INTEGER, m INTEGER, code INTEGER NOT NULL, active INTEGER NOT NULL," +
                                          " name TEXT, d TEXT, doc TEXT);" +
                                          "INSERT INTO zz_r18_row (id, n, m, code, active, name, d, doc) VALUES" +
                                          " (1, 1, 2, 1, 1, 'a', '2020-01-01 00:00:00', '{\"k\":1}'), (2, NULL, 3, 3, 0, NULL, '2021-02-03 00:00:00', '{\"k\":2}')," +
                                          " (3, 2, NULL, 0, 1, 'abc', NULL, NULL), (4, NULL, NULL, 5, 0, NULL, NULL, '{}'), (5, 5, 1, 5, 1, 'de', '2022-03-04 00:00:00', '{\"k\":5}');";
                    command.ExecuteNonQuery();
                }
            }
        }

        private long Rows(string table)
        {
            using (var connection = new SqliteConnection("Data Source=" + _path))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM " + table;
                    return (long)command.ExecuteScalar();
                }
            }
        }

        [TestMethod]
        public void Delete_OfANestedTwin_IsRefused()
        {
            var provider = new SqliteOrmDataProvider("Data Source=" + _path);
            var refused = new (string Key, Expression<Func<R18Row, bool>> Predicate, string Message)[]
            {
                ("T1", x => ((x.Active ? x.N : x.M) == x.Code ? x.NameLen : x.M) == ((x.Active ? x.N : x.M) == x.Code ? (int?)x.Name.Length : x.M), SelfReferenceMessage),
                ("T2", x => ((x.Active ? x.NameLen : x.M) == x.Code ? x.N : x.M) == ((x.Active ? (int?)x.Name.Length : x.M) == x.Code ? x.N : x.M), SelfReferenceMessage),
                ("T3", x => ((x.Active ? x.N : x.M) == x.Code ? x.DYear : x.M) == ((x.Active ? x.N : x.M) == x.Code ? (int?)x.D.Value.Year : x.M), SelfReferenceMessage),
                ("T5", x => !(((x.Active ? x.N : x.M) == x.Code ? x.NameLen : x.M) != ((x.Active ? x.N : x.M) == x.Code ? (int?)x.Name.Length : x.M)), SelfReferenceMessage),
                ("T7", x => ((x.Active ? x.N : x.M) == x.Code ? x.NameLen : x.M) == ((x.Active ? x.N : x.M) == x.Code ? (int?)x.Name.Length : x.M) || x.Id == 2, AlwaysTrueMessage),
                ("T8", x => (bool)(object)(((x.Active ? x.N : x.M) == x.Code ? x.NameLen : x.M) == ((x.Active ? x.N : x.M) == x.Code ? (int?)x.Name.Length : x.M)), SelfReferenceMessage),
                ("L2", x => ((x.Active ? x.N : x.M) == x.Code ? x.NameLenLower : x.M) == ((x.Active ? x.N : x.M) == x.Code ? x.NameLen : x.M), SelfReferenceMessage),
                ("L5", x => ((x.Active ? x.N : x.M) == x.Code ? x.NameLenLower : x.M) == ((x.Active ? x.N : x.M) == x.Code ? (int?)x.Name.Length : x.M), SelfReferenceMessage),
                ("K1", x => ((x.Id > 2 ? x.N : (int?)x.Code) != 5 ? x.NameLen : (int?)x.Code) == ((x.Id > 2 ? x.N : (int?)x.Code) != 5 ? (int?)x.Name.Length : (int?)x.Code), SelfReferenceMessage),
                ("J2", x => ((x.Active ? x.N : x.M) == x.Code ? x.K : x.M) == ((x.Active ? x.N : x.M) == x.Code ? x.K2 : x.M), SelfReferenceMessage),
                ("J3", x => ((x.Active ? x.K : x.M) == x.Code ? x.N : x.M) == ((x.Active ? x.K2 : x.M) == x.Code ? x.N : x.M), SelfReferenceMessage),
                // R20-4, a Known issue: the check of the translated clause reads quoted text letter case aside, as 3.11.0's did,
                // so two JSON keys that differ only in letter case, compared under a `!`, are refused though their values differ.
                ("R20-4", x => !(x.K != x.KUpper), SelfReferenceMessage),
                // R20-1: the verdict folds a part under a cast through object without reading under it, so a captured true there
                // is the translation's to see: its literal NOT 1=0 holds, and the text check refuses it, as the Changelog says.
                ("R20-1", x => (bool)(object)(x.Id == 2 || CapturedTrue), AlwaysTrueMessage),
                // R21-1: an empty search value under such a cast, written as `""` or string.Empty, which reads nothing the caller wrote.
                ("R21-1", x => (bool)(object)x.Name.Contains(""), UnboundedSearchMessage),
                ("R21-1b", x => x.Id == 2 || (bool)(object)x.Name.StartsWith(string.Empty), UnboundedSearchMessage),
            };
            foreach (var shape in refused)
            {
                provider.BeginTransaction();
                try
                {
                    var ex = Assert.ThrowsException<InvalidOperationException>(() => provider.Delete(shape.Predicate), shape.Key + ": the delete");
                    Assert.AreEqual(shape.Message, ex.Message, shape.Key);
                }
                finally { provider.RollbackTransaction(); }
                Assert.AreEqual(5L, Rows("zz_r18_row"), shape.Key + ": rows left");
            }
            // R18-2 (a): arithmetic is no provider's to translate, so its delete throws the translator's exception first,
            // the verdict coming once the predicate has translated (D-005), as in 3.11.0.
            provider.BeginTransaction();
            try { Assert.ThrowsException<NotSupportedException>(() => provider.Delete<R18Row>(x => x.Id + 1 == x.Id + 1), "arithmetic"); }
            finally { provider.RollbackTransaction(); }
            // Neighbours, rolled back: the computed part's branch against another column's (C#'s rows: 3 and 4, where both
            // sides are M, NULL), and the computed part against a value (the one name of length 1).
            provider.BeginTransaction();
            try
            {
                Assert.AreEqual(2, provider.Delete<R18Row>(x => ((x.Active ? x.N : x.M) == x.Code ? x.NameLen : x.M) == ((x.Active ? x.N : x.M) == x.Code ? x.M : x.M)), "N1");
                Assert.AreEqual(1, provider.Delete<R18Row>(x => x.NameLen == 1), "N2");
            }
            finally { provider.RollbackTransaction(); }
            Assert.AreEqual(5L, Rows("zz_r18_row"), "rows left after the rolled-back deletes");
            // R19-3: two JSON keys that differ in letter case are two values: C#'s rows are 2 (the Id), 3 and 4 (both NULL).
            provider.BeginTransaction();
            try { Assert.AreEqual(3, provider.Delete<R18Row>(x => x.K == x.KUpper || x.Id == 2), "JSON keys k and K"); }
            finally { provider.RollbackTransaction(); }
            Assert.AreEqual(5L, Rows("zz_r18_row"), "rows left after the rolled-back deletes");
        }

        [TestCleanup]
        public void DropTable()
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
        }

        private long Rows()
        {
            using (var connection = new SqliteConnection("Data Source=" + _path))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM zz_r16_row";
                    return (long)command.ExecuteScalar();
                }
            }
        }

        [TestMethod]
        public void Delete_OfAComputedPartAgainstTheColumnsPart_IsRefused()
        {
            var provider = new SqliteOrmDataProvider("Data Source=" + _path);
            var shapes = new Expression<Func<R16Row, bool>>[]
            {
                x => x.NameLen == x.Name.Length,
                x => x.Name.Length == x.NameLen,
                x => (int?)x.Name.Length == x.NameLen,
                x => x.NameLen == x.NameX.Length,
                x => x.DYear == x.D.Value.Year,
                x => x.D.Value.Year == x.DYear,
                // R17-5: under a `!`, always true; 3.11.0 accepted it (NOT LENGTH(name) != LENGTH(name), the names), refused now.
                x => !(x.NameLen != x.Name.Length),
            };
            foreach (var shape in shapes)
            {
                Assert.AreEqual(5, provider.Query<R16Row>().Count(shape), shape + ": the query");
                provider.BeginTransaction();
                try
                {
                    var refused = Assert.ThrowsException<InvalidOperationException>(() => provider.Delete(shape), shape + ": the delete");
                    Assert.AreEqual(SelfReferenceMessage, refused.Message, shape.ToString());
                }
                finally { provider.RollbackTransaction(); }
                Assert.AreEqual(5L, Rows(), shape + ": rows left");
            }
            // A neighbour: the computed part against a value is no self-reference, and the delete runs (C#'s rows: the one name
            // of length 1), rolled back.
            provider.BeginTransaction();
            try { Assert.AreEqual(1, provider.Delete<R16Row>(x => x.NameLen == 1), "a computed part against a value"); }
            finally { provider.RollbackTransaction(); }
            Assert.AreEqual(5L, Rows(), "rows left after the rolled-back delete");
        }
    }
}
