using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using Funcular.Data.Orm.Attributes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.Tests.NullSemantics
{
    /// <summary>
    /// 3.12.0 (docs/plans/NULL_SEMANTICS_PLAN.md): a WHERE predicate answers as C# and EF Core do when a column can be
    /// NULL. Every row computes its expected ids with LINQ to objects over the seed (or with the key's oracle where C#
    /// would throw on a null receiver: EF's answer, lifted to nullable), and asserts them on every path: Query,
    /// QueryAsync, Query&lt;T&gt;().Where, Count, Any, and Delete/DeleteAsync in a transaction that is rolled back. As in
    /// <c>WhereParityHarness</c>, the DELETE's WHERE text and parameters must be byte-identical to the SELECT's, and a key
    /// the all-rows guard refuses (plan §4.3) asserts the refusal and that nothing was deleted. Compiled into
    /// SqlServer.Tests and linked into PostgreSql.Tests, MySql.Tests and Sqlite.Tests; one derived [TestClass] per
    /// provider supplies the tables and the provider.
    /// </summary>
    public abstract class NullSemanticsHarness
    {
        public const string RowTable = "zz_ns_row";
        public const string ParentTable = "zz_ns_parent";
        public const string ChildTable = "zz_ns_child";

        public const string NoColumnMessage = "The WHERE clause must reference at least one column of the entity.";
        public const string AlwaysTrueMessage = "The WHERE clause is trivially true; a non-trivial WHERE clause is required.";

        public enum Color { Red = 0, Green = 1 }

        #region Entities and seed

        /// <summary>Plain columns only: every N1–N11/N14 key runs on it, delete paths included.</summary>
        [Table(RowTable)]
        public class NsRow
        {
            [Key, Column("id")] public int Id { get; set; }
            [Column("n")] public int? N { get; set; }
            [Column("m")] public int? M { get; set; }
            [Column("name")] public string Name { get; set; }
            [Column("req")] public string Req { get; set; }
            [Column("flag")] public bool? Flag { get; set; }
            [Column("d")] public DateTime? D { get; set; }
            [Column("code")] public int Code { get; set; }
            [Column("e")] public Color? E { get; set; }
            [Column("long_col")] public long LongCol { get; set; }
            [Column("price")] public decimal Price { get; set; }
            [Column("n_long")] public long? NLong { get; set; }
            [Column("n_price")] public decimal? NPrice { get; set; }
            [Column("parent_id")] public int? ParentId { get; set; }
            /// <summary>NOT NULL bools: a SQL Server <c>bit</c> in a value position (R5-1).</summary>
            [Column("active")] public bool Active { get; set; }
            [Column("other")] public bool Other { get; set; }
            [NotMapped] public NsInner Child { get; set; }
        }

        public class NsInner
        {
            public int Id { get; set; }
        }

        /// <summary>Inline computed attributes over the same table: their SQL sits in the SELECT and WHERE, so deletes run.</summary>
        [Table(RowTable)]
        public class NsComputed
        {
            [Key, Column("id")] public int Id { get; set; }
            [Column("n")] public int? N { get; set; }

            [JsonPath("doc", "$.k")] public string DocK { get; set; }

            /// <summary>A non-nullable CLR type over a value that can be NULL (n is NULL on rows 3 and 4).</summary>
            [SqlExpression("{N} + 1")] public int NPlusOneInt { get; set; }

            /// <summary>
            /// A non-nullable Sum: the bare <c>SUM</c>, NULL for a row with no children, compared as 3.11.0 compared it, without
            /// the NULL's compensation (the owner's ruling on Task 8's T8E2-4: a Known issue).
            /// </summary>
            [SubqueryAggregate(typeof(NsChild), nameof(NsChild.RowId), AggregateFunction.Sum, AggregateColumn = nameof(NsChild.Amount))]
            public decimal ChildSumD { get; set; }

            /// <summary>T8E2-4: an integer Sum (SQLite binds a decimal as text), non-nullable: uncompensated, as 3.11.0.</summary>
            [SubqueryAggregate(typeof(NsChild), nameof(NsChild.RowId), AggregateFunction.Sum, AggregateColumn = nameof(NsChild.Id))]
            public int ChildIdSum { get; set; }

            /// <summary>T8E2-4: the same Sum declared nullable can be NULL, which it reads back as null: compensated.</summary>
            [SubqueryAggregate(typeof(NsChild), nameof(NsChild.RowId), AggregateFunction.Sum, AggregateColumn = nameof(NsChild.Id))]
            public int? ChildIdSumN { get; set; }

            /// <summary>
            /// Task 8 (T8F-3): a non-nullable Avg, likewise the bare <c>AVG</c>, uncompensated (over the decimal amount: SQL
            /// Server's AVG over an int column is an int, which FunkyORM doesn't read into a double).
            /// </summary>
            [SubqueryAggregate(typeof(NsChild), nameof(NsChild.RowId), AggregateFunction.Avg, AggregateColumn = nameof(NsChild.Amount))]
            public decimal ChildAvgD { get; set; }

            [SubqueryAggregate(typeof(NsChild), nameof(NsChild.RowId), AggregateFunction.Count)]
            public int ChildCount { get; set; }

            [Column("active")] public bool Active { get; set; }

            /// <summary>A bool that can be NULL as far as FunkyORM knows (a [SqlExpression]), though active never is (R10-2).</summary>
            [SqlExpression("{Active}")] public bool ActiveX { get; set; }

            /// <summary>N's [SqlExpression] alias (R14-3): the column's own value, NULL included.</summary>
            [SqlExpression("{N}")] public int? NX { get; set; }

            /// <summary>Name's and D's aliases (R15-2): a part of each compared with the same part of the column.</summary>
            [SqlExpression("{Name}")] public string NameX { get; set; }

            [Column("d")] public DateTime? D { get; set; }

            [SqlExpression("{D}")] public DateTime? DX { get; set; }

            [Column("code")] public int Code { get; set; }

            /// <summary>A CASE in a template (R15-3): its SQL nests a level the expression doesn't show.</summary>
            [SqlExpression("CASE WHEN {Code} > 5 THEN {Code} ELSE 0 END")] public int? CodeCase { get; set; }

            /// <summary>A template whose SQL holds `IS NULL AND` (R17-1), and its alias.</summary>
            [SqlExpression("CASE WHEN {N} IS NULL AND {Flag} IS NULL THEN 0 ELSE {N} END")] public int? NBoth { get; set; }

            [SqlExpression("{NBoth}")] public int? NBothX { get; set; }

            [Column("flag")] public bool? Flag { get; set; }

            [Column("name")] public string Name { get; set; }

            /// <summary>R18-1: twins only each provider's text shows: two templates that differ in letter case (with no CASE,
            /// so the rewrite distributes them), and two [JsonPath] properties on one path.</summary>
            [SqlExpression("ABS({N})")] public int? NAbsUpper { get; set; }

            [SqlExpression("abs({N})")] public int? NAbsLower { get; set; }

            [JsonPath("doc", "$.k")] public string DocK2 { get; set; }
        }

        [Table(ParentTable)]
        public class NsParent
        {
            [Key, Column("id")] public int Id { get; set; }
            [Column("label")] public string Label { get; set; }
            [Column("pcode")] public int PCode { get; set; }
        }

        [Table(ChildTable)]
        public class NsChild
        {
            [Key, Column("id")] public int Id { get; set; }
            [Column("row_id")] public int RowId { get; set; }
            [Column("amount")] public decimal Amount { get; set; }
        }

        /// <summary>Remote attributes through parent_id: a LEFT JOIN, so every one of them can be NULL whatever its CLR type.</summary>
        [Table(RowTable)]
        public class NsRemote
        {
            [Key, Column("id")] public int Id { get; set; }

            [Column("parent_id"), RemoteLink(typeof(NsParent))] public int? ParentId { get; set; }

            [RemoteProperty(typeof(NsParent), nameof(ParentId), nameof(NsParent.Label))]
            public string ParentLabel { get; set; }

            [RemoteProperty(typeof(NsParent), nameof(ParentId), nameof(NsParent.PCode))]
            public int ParentCode { get; set; }

            [RemoteKey(typeof(NsParent), nameof(ParentId), nameof(NsParent.Id))]
            public int ParentKey { get; set; }
        }

        /// <summary>
        /// The parent with a reverse key to its rows: a one-to-many join, so Count and All refuse a filter that names it
        /// (fan-out). §4.3's reverse-join row.
        /// </summary>
        [Table(ParentTable)]
        public class NsReverse : NsParent
        {
            [RemoteKey(typeof(NsRemote), nameof(NsRemote.Id))]
            public int ReverseRowId { get; set; }
        }

        public sealed class SeedRow
        {
            public int Id; public int? N; public int? M; public string Name; public string Req; public bool? Flag; public DateTime? D;
            public int Code; public Color? E; public long LongCol; public decimal Price; public int? ParentId; public string Doc;
            public bool Active; public bool Other;

            /// <summary>The nullable twins of <c>long_col</c> and <c>price</c>: <c>n</c> widened, NULL where it is (plan §6.3, R3-3).</summary>
            public long? NLong => N;
            public decimal? NPrice => N;
        }

        /// <summary>The seed (plan §6.3), inserted verbatim by <see cref="CreateTables"/>.</summary>
        public static readonly SeedRow[] Rows =
        {
            new SeedRow { Id = 1, N = 5, M = 5, Name = "a", Req = "a", Flag = true, D = new DateTime(2020, 6, 1), Code = 5, E = Color.Red, LongCol = 5, Price = 5m, ParentId = 1, Doc = "{\"k\":\"5\"}", Active = true, Other = false },
            new SeedRow { Id = 2, N = 5, M = null, Name = null, Req = "b", Flag = false, D = null, Code = 6, E = null, LongCol = 6, Price = 6m, ParentId = 2, Doc = "{\"k\":null}", Active = false, Other = true },
            new SeedRow { Id = 3, N = null, M = 5, Name = "b", Req = "a", Flag = null, D = new DateTime(2021, 1, 1), Code = 7, E = Color.Green, LongCol = 7, Price = 7m, ParentId = null, Doc = "{}", Active = true, Other = true },
            new SeedRow { Id = 4, N = null, M = null, Name = null, Req = "c", Flag = null, D = null, Code = 8, E = null, LongCol = 8, Price = 8m, ParentId = 1, Doc = null, Active = false, Other = false },
            new SeedRow { Id = 5, N = 7, M = 8, Name = "a", Req = "d", Flag = true, D = new DateTime(2020, 1, 1), Code = 5, E = Color.Red, LongCol = 5, Price = 5m, ParentId = 2, Doc = "{\"k\":\"7\"}", Active = true, Other = false },
            new SeedRow { Id = 6, N = 1, M = 1, Name = "ca", Req = "a", Flag = false, D = new DateTime(2022, 3, 3), Code = 1, E = Color.Green, LongCol = 1, Price = 1m, ParentId = null, Doc = "{\"k\":\"1\"}", Active = false, Other = true },
        };

        public static readonly NsParent[] Parents =
        {
            new NsParent { Id = 1, Label = "p", PCode = 5 },
            new NsParent { Id = 2, Label = null, PCode = 7 },
        };

        public static readonly NsChild[] Children =
        {
            new NsChild { Id = 1, RowId = 1, Amount = 10m },
            new NsChild { Id = 2, RowId = 1, Amount = 5m },
            new NsChild { Id = 3, RowId = 5, Amount = 10m },
        };

        private static NsRow AsRow(SeedRow s) => new NsRow
        {
            Id = s.Id, N = s.N, M = s.M, Name = s.Name, Req = s.Req, Flag = s.Flag, D = s.D, Code = s.Code, E = s.E,
            LongCol = s.LongCol, Price = s.Price, NLong = s.NLong, NPrice = s.NPrice, ParentId = s.ParentId,
            Active = s.Active, Other = s.Other,
        };

        #endregion

        #region Provider seam

        /// <summary>Marks the test inconclusive when the provider's test database is unavailable.</summary>
        protected virtual void RequireDatabase() { }

        /// <summary>Drops the three tables if they exist, creates them and inserts the seed (plan §6.3).</summary>
        protected abstract void CreateTables();

        /// <summary>Drops the three tables (or deletes the SQLite file).</summary>
        protected abstract void DropTables();

        protected abstract OrmDataProvider CreateProvider();

        /// <summary>
        /// True when the database itself rejects the SELECT (and the DELETE) this key translates to, before and after
        /// 3.12.0: SQL Server has no boolean expression type, so a <c>bool</c> CASE in a truth position is a syntax error.
        /// A key may also name a <see cref="ReadOnce_CountingGetter"/> row, whose SELECT alone is run (<see cref="QueryForReads"/>).
        /// </summary>
        protected virtual bool RejectedByTheDialect(string key) => false;

        /// <summary>
        /// The exception the provider's driver throws for this key before and after 3.12.0, for a reason outside this
        /// plan (PostgreSQL can't bind a CLR enum parameter: backlog Appendix A #5); null when the key runs.
        /// </summary>
        protected virtual Type RejectedByTheProvider(string key) => null;

        private bool AssertedProviderRejection(string key, Action query)
        {
            var expected = RejectedByTheProvider(key);
            if (expected == null)
                return false;
            Exception thrown = null;
            try { query(); } catch (Exception ex) { thrown = ex; }
            Assert.IsNotNull(thrown, key + ": the provider no longer rejects this; it now runs, so remove it from RejectedByTheProvider");
            Assert.IsInstanceOfType(thrown, expected, key + ": " + thrown.GetType().Name + ": " + thrown.Message);
            return true;
        }

        /// <summary>The provider's literal WHERE text and parameters for an N11 key, captured from 25956e0 (3.11.0); null until captured.</summary>
        protected abstract string SqlOf3110(string key);

        /// <summary>The CREATE TABLE and INSERT statements, built from the seed with the provider's column types and literals.</summary>
        protected string SeedSql(Func<string, string> type, Func<object, string> literal) =>
            $"CREATE TABLE {ParentTable} (id {type("int")} PRIMARY KEY, label {type("text")} NULL, pcode {type("int")} NOT NULL); " +
            $"INSERT INTO {ParentTable} (id, label, pcode) VALUES " +
            string.Join(", ", Parents.Select(p => $"({literal(p.Id)}, {literal(p.Label)}, {literal(p.PCode)})")) + "; " +
            $"CREATE TABLE {ChildTable} (id {type("int")} PRIMARY KEY, row_id {type("int")} NOT NULL, amount {type("decimal")} NOT NULL); " +
            $"INSERT INTO {ChildTable} (id, row_id, amount) VALUES " +
            string.Join(", ", Children.Select(c => $"({literal(c.Id)}, {literal(c.RowId)}, {literal(c.Amount)})")) + "; " +
            $"CREATE TABLE {RowTable} (id {type("int")} PRIMARY KEY, n {type("int")} NULL, m {type("int")} NULL, name {type("text")} NULL, " +
            $"req {type("text")} NOT NULL, flag {type("bool")} NULL, d {type("datetime")} NULL, code {type("int")} NOT NULL, e {type("int")} NULL, " +
            $"long_col {type("bigint")} NOT NULL, price {type("decimal")} NOT NULL, n_long {type("bigint")} NULL, n_price {type("decimal")} NULL, " +
            $"parent_id {type("int")} NULL, doc {type("json")} NULL, active {type("bool")} NOT NULL, other {type("bool")} NOT NULL); " +
            $"INSERT INTO {RowTable} (id, n, m, name, req, flag, d, code, e, long_col, price, n_long, n_price, parent_id, doc, active, other) VALUES " +
            string.Join(", ", Rows.Select(r =>
                $"({literal(r.Id)}, {literal(r.N)}, {literal(r.M)}, {literal(r.Name)}, {literal(r.Req)}, {literal(r.Flag)}, {literal(r.D)}, " +
                $"{literal(r.Code)}, {literal(r.E.HasValue ? (int?)(int)r.E.Value : null)}, {literal(r.LongCol)}, {literal(r.Price)}, " +
                $"{literal(r.NLong)}, {literal(r.NPrice)}, {literal(r.ParentId)}, {literal(r.Doc)}, {literal(r.Active)}, {literal(r.Other)})")) + ";";

        /// <summary>A SQL literal for the seed: NULL, a quoted string, an invariant number or date; booleans as the provider writes them.</summary>
        protected static string Literal(object value, Func<bool, string> boolean)
        {
            switch (value)
            {
                case null: return "NULL";
                case string s: return "'" + s.Replace("'", "''") + "'";
                case bool b: return boolean(b);
                case DateTime d: return "'" + d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "'";
                case decimal m: return m.ToString(CultureInfo.InvariantCulture);
                case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
                default: throw new ArgumentException("no literal for " + value.GetType().Name);
            }
        }

        #endregion

        #region Captured values and predicates

        /// <summary>Counts every read of its values (R19-1, R20-1).</summary>
        public sealed class ReadCounter
        {
            public int Reads;
            public bool T { get { Reads++; return true; } }
            public bool F { get { Reads++; return false; } }
            public int Five { get { Reads++; return 5; } }
            public int Two { get { Reads++; return 2; } }
            public string A { get { Reads++; return "a"; } }
            public bool? NB { get { Reads++; return null; } }
            public bool? NT { get { Reads++; return true; } }
        }

        /// <summary>Captured values with counted getters (AC-N13).</summary>
        public class Holder
        {
            public int FiveReads, FlagReads, FlipReads;
            private int _flipCount;
            public int Five { get { FiveReads++; return 5; } }
            public int NullableFiveReads;
            public int? NullableFive { get { NullableFiveReads++; return 5; } }
            public short ShortVal => 5;
            public int IntVal => 5;
            public Color ColorVal => Color.Green;
            public bool Flag { get { FlagReads++; return true; } }
            public int NullFlagReads;
            public bool? NullFlag { get { NullFlagReads++; return null; } }
            public int AReads;
            public string A { get { AReads++; return "a"; } }
            public int NoneReads;
            public int? None { get { NoneReads++; return null; } }
            public int FalseReads;
            public bool False { get { FalseReads++; return false; } }

            /// <summary>Null on odd reads, 5 on even ones: a WHERE whose form disagrees with its bound value would show it.</summary>
            public int? Flip { get { FlipReads++; return (_flipCount++ % 2 == 0) ? (int?)null : 5; } }
        }

        /// <summary>A lazy collection that counts how often it is enumerated (R5-8, R6-5).</summary>
        public sealed class CountingIds : IEnumerable<int?>
        {
            public int Enumerations;

            public IEnumerator<int?> GetEnumerator()
            {
                Enumerations++;
                yield return 5;
                yield return 8;
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private readonly int? v = 5;
        private readonly int? none = null;
        private readonly string a = "a";
        private readonly string noName = null;
        private readonly Color? noColor = null;
        private readonly short? shortNull = null;
        private readonly bool include = false;
        private readonly bool? nullBool = null;
        private readonly bool? trueBool = true;
        private readonly string lowerA = "a";
        private readonly int?[] ids = { 1, 2 };
        private readonly int?[] idsWithNull = { 1, null };
        private readonly int?[] onlyNull = { null };
        private readonly int[] intIds = { 1, 2 };
        private readonly string[] names = { "a", "b" };
        /// <summary>An <c>int[]</c>: against the remote <c>int</c> key the item needs no cast (a cast item is rejected, §3).</summary>
        private readonly int[] parentKeys = { 1 };

        /// <summary>The predicate for <paramref name="key"/> over <see cref="NsRow"/>.</summary>
        protected Expression<Func<NsRow, bool>> Predicate(string key)
        {
            switch (key)
            {
                // AC-N1
                case "nNeV": return x => x.N != v;
                case "nNe5": return x => x.N != 5;
                case "vNeN": return x => v != x.N;
                case "nameNeA": return x => x.Name != a;
                case "aNeName": return x => a != x.Name;
                case "reqNeA": return x => x.Req != a;
                case "flagNeTrue": return x => x.Flag != true;
                case "flagNeFalse": return x => x.Flag != false;
                case "dYearNe2020": return x => x.D.Value.Year != 2020;
                case "nameLengthNe1": return x => x.Name.Length != 1;
                case "eNeRed": return x => x.E != Color.Red;
                case "eNeGreenLiteral": return x => x.E != Color.Green;
                case "condNullableNe": return x => (x.Id > 2 ? x.N : x.M) != 5;
                // AC-N1, the Convert twins on nullable columns (R3-3): 3.11.0's comparison and binding, plus OR col IS NULL
                case "nNeCapturedShort": return x => x.N != holder.ShortVal;
                case "nLongNeCapturedInt": return x => x.NLong != holder.IntVal;
                case "nPriceNeCapturedInt": return x => x.NPrice != holder.IntVal;
                case "colorNeCapturedColor": return x => x.E != holder.ColorVal;
                // AC-N2
                case "nEqNone": return x => x.N == none;
                case "nNeNone": return x => x.N != none;
                case "noneNeN": return x => none != x.N;
                case "nameEqNoName": return x => x.Name == noName;
                case "nameNeNoName": return x => x.Name != noName;
                case "eEqNoColor": return x => x.E == noColor;
                case "nEqShortNull": return x => x.N == shortNull;
                case "condFoldedTest": return x => (x.Id == none ? x.N : x.M) != 5;
                case "nEq5OrNNe5": return x => x.N == 5 || x.N != 5;
                case "idEqNoneInt": return x => x.Id == none;
                case "idNeNoneInt": return x => x.Id != none;
                case "idEq2OrIdNeNoneInt": return x => x.Id == 2 || x.Id != none;
#pragma warning disable CS0472 // The result of the expression is always the same: the shape is the subject.
                case "idEqNullLiteral": return x => x.Id == null;
                case "idNeNullLiteral": return x => x.Id != null;
                case "notIdEqNullLiteral": return x => !(x.Id == null);
#pragma warning restore CS0472
                case "notIdEqNone": return x => !(x.Id == none);
                case "reqNeNoName": return x => x.Req != noName;
                // AC-N3
                case "nEqM": return x => x.N == x.M;
                case "nNeM": return x => x.N != x.M;
                case "nEqCode": return x => x.N == x.Code;
                case "nNeCode": return x => x.N != x.Code;
                case "nameLengthSelf": return x => x.Name.Length == x.Name.Length;
                // AC-N4
                case "notNEqV": return x => !(x.N == v);
                case "notNNeV": return x => !(x.N != v);
                case "notNGtV": return x => !(x.N > v);
                case "notNGeNone": return x => !(x.N >= none);
                case "idOrNotNGeNone": return x => x.Id == 2 || !(x.N >= none);
                case "notAndEq": return x => !(x.N == 5 && x.Name == a);
                case "notOrEq": return x => !(x.N == 5 || x.Name == a);
                case "nNe5OrNameEqA": return x => x.N != 5 || x.Name == a;
                case "doubleNot": return x => !!(x.N == 5);
                case "notNameLengthGt1": return x => !(x.Name.Length > 1);
                case "condTestNe": return x => (x.N != 5 ? 1 : 0) == 1;
                case "notBoolCond": return x => !(x.N > 3 ? x.M != 5 : x.Name == a);
                case "boolCondBranches": return x => x.N > 3 ? x.M != 5 : x.Name == a;
                // AC-N5
                case "nGtNone": return x => x.N > none;
                case "nGtV": return x => x.N > v;
                // AC-N6
                case "idsContainsN": return x => ids.Contains(x.N);
                case "notIdsContainsN": return x => !ids.Contains(x.N);
                case "idsWithNullContainsN": return x => idsWithNull.Contains(x.N);
                case "notIdsWithNullContainsN": return x => !idsWithNull.Contains(x.N);
                case "onlyNullContainsN": return x => onlyNull.Contains(x.N);
                case "notOnlyNullContainsN": return x => !onlyNull.Contains(x.N);
                case "id3AndIdsWithNullContainsN": return x => x.Id == 3 && idsWithNull.Contains(x.N);
                case "id3AndNotIdsContainsN": return x => x.Id == 3 && !ids.Contains(x.N);
                case "notIntIdsContainsId": return x => !intIds.Contains(x.Id);
                case "notNamesContainsName": return x => !names.Contains(x.Name);
                // AC-N7
                case "notNameContainsA": return x => !x.Name.Contains(a);
                case "notNameStartsWithA": return x => !x.Name.StartsWith(a);
                case "notNameEndsWithA": return x => !x.Name.EndsWith(a);
                // AC-N8
                case "nameContainsNull": return x => x.Name.Contains(noName);
                case "notNameContainsNull": return x => !x.Name.Contains(noName);
                case "notNameStartsWithNull": return x => !x.Name.StartsWith(noName);
                case "notNameEndsWithNull": return x => !x.Name.EndsWith(noName);
                case "idOrNotNameContainsNull": return x => x.Id == 2 || !x.Name.Contains(noName);
                case "idAndNotNameContainsNull": return x => x.Id == 2 && !x.Name.Contains(noName);
                case "notAndContainsNull": return x => !(x.N == 5 && x.Name.Contains(noName));
                // AC-N14
                case "nameContainsAEqTrue": return x => x.Name.Contains(a) == true;
                case "nameContainsAEqFalse": return x => x.Name.Contains(a) == false;
                case "notNameContainsAEqTrue": return x => !(x.Name.Contains(a) == true);
                case "nameContainsANeTrue": return x => x.Name.Contains(a) != true;
                case "nameContainsNullEqFalse": return x => x.Name.Contains(noName) == false;
                // AC-N11 pins
                case "idNe5": return x => x.Id != 5;
                case "idEq5": return x => x.Id == 5;
                case "notIdEq5": return x => !(x.Id == 5);
                case "notCodeGt5": return x => !(x.Code > 5);
                case "nameEqA": return x => x.Name == a;
                case "nEq5": return x => x.N == 5;
                case "nameContainsA": return x => x.Name.Contains(a);
                case "nEqNullLiteral": return x => x.N == null;
                case "nGt5": return x => x.N > 5;
                case "condNe": return x => (x.N > 3 ? 1 : 0) != 1;
                case "capturedNEqV": return x => x.N == v;
                case "codeNeCapturedShort": return x => x.Code != holder.ShortVal;
                case "longNeCapturedInt": return x => x.LongCol != holder.IntVal;
                case "priceNeCapturedInt": return x => x.Price != holder.IntVal;
                case "notIdEqCapturedV": return x => !(x.Id == v);
                case "idNeCapturedNullableFive": return x => x.Id != holder.NullableFive;
                // AC-N11 exception (a): a conditional's test that doesn't read the row is a literal
                case "condCapturedTest": return x => (holder.IntVal == 5 ? x.Id : x.Code) != 5;
                case "condCollationTest": return x => (lowerA == "A" ? x.Id : 0) > 0;
                // AC-N14: a bool compared with a predicate, as two-valued truths
                case "nEq5EqMEq5": return x => (x.N == 5) == (x.M == 5);
                case "nEq5NeMEq5": return x => (x.N == 5) != (x.M == 5);
                case "flagEqNGt5": return x => x.Flag == (x.N > 5);
                case "flagNeNGt5": return x => x.Flag != (x.N > 5);
                case "nameContainsAEqInclude": return x => x.Name.Contains(a) == include;
                // R4-1: a negated call beside a sibling that needs nothing is still compensated (De Morgan)
                case "notIdAndNameContainsA": return x => !(x.Id == 2 && x.Name.Contains(a));
                case "notIdOrIdsContainsN": return x => !(x.Id == 2 || ids.Contains(x.N));
                case "notIdAndNameContainsNull": return x => !(x.Id == 2 && x.Name.Contains(noName));
                case "notIdAndCodeEq5": return x => !(x.Id == 2 && x.Code == 5);
                // R4-2: a conditional's test is rewritten wherever the conditional is, whatever the comparison around it
                case "condTestNeGt0": return x => (x.N != 5 ? x.Id : 0) > 0;
                case "condTestNeEqV": return x => (x.N != 5 ? 5 : 0) == v;
                case "nestedCondTestNe": return x => (x.Id > 2 ? (x.N != 5 ? 1 : 0) : 0) == 1;
                // §4.3's rows in full (R4-3)
                case "idOrNotOnlyNullContainsN": return x => x.Id == 2 || !onlyNull.Contains(x.N);
                case "notIdAndOnlyNullContainsN": return x => !(x.Id == 2 && onlyNull.Contains(x.N));
                case "nameContainsNullNeTrue": return x => x.Name.Contains(noName) != true;
                case "notNameContainsNullEqTrue": return x => !(x.Name.Contains(noName) == true);
                case "notNGtNone": return x => !(x.N > none);
                case "notNLtNone": return x => !(x.N < none);
                case "notNLeNone": return x => !(x.N <= none);
                // R4-4: a predicate against a null bool?, and a predicate against itself
                case "nameContainsANeNullBool": return x => x.Name.Contains(a) != nullBool;
                case "nameContainsAEqNullBool": return x => x.Name.Contains(a) == nullBool;
                case "nEq5EqNEq5": return x => (x.N == 5) == (x.N == 5);
                // R4-9 and R4-11: a captured bool as a conditional's test, and a bool branch that doesn't read the row
                case "condCapturedBoolTest": return x => (holder.Flag ? x.Id : x.Code) != 5;
                case "boolBranchCapturedLiteral": return x => x.N > 3 ? holder.IntVal == 5 : x.Name == a;
                // Coverage of the rarer compensations (the rewrite's lines no other key reached)
                case "nameLengthSelfNe": return x => x.Name.Length != x.Name.Length;
                case "notNGtM": return x => !(x.N > x.M);
                case "flagEqFoldedPredicate": return x => x.Flag == (holder.IntVal == 5);
                case "flagNeFoldedPredicate": return x => x.Flag != (holder.IntVal == 5);
                // R5-1: a bool in a value position that SQL Server only accepts there keeps 3.11.0's text
                case "boolCondEqTrue": return x => (x.Id > 2 ? x.Active : false) == true;
                case "colEqBoolCond": return x => x.Active == (x.Id > 2 ? x.Other : x.Active);
                case "notActiveEqTrue": return x => !x.Active == true;
                case "notActiveEqOther": return x => !x.Active == x.Other;
                case "nboolCondLiteralBranchEqTrue": return x => (x.Id > 2 ? x.Flag : (bool?)true) == true;
                // R5-4: a null captured value in a conditional's value branch is the literal NULL
                case "condNullBranchEq5": return x => (x.Id > 2 ? x.N : none) == 5;
                case "condNullBranchGt3": return x => (x.Id > 2 ? x.N : none) > 3;
                // R5-5: a negated comparison that needs nothing, around a conditional whose test is compensated
                case "notCondTestNeEq1": return x => !((x.N != 5 ? 1 : 0) == 1);
                // R5-9: always true once a null value's comparison folds, in a conditional's test
                case "nGtNoneCondCode": return x => x.N > none ? x.Code == 5 : true;
                // R6-1: `!` over a value-position conditional keeps its branches values (3.11.0's text; valid T-SQL)
                case "notBoolCondEqTrue": return x => !(x.Id > 2 ? x.Active : false) == true;
                case "notNboolCondLiteralNeFalse": return x => !(x.Id > 2 ? x.Flag : (bool?)true) != false;
                // R6-2: `!` of a column or of a captured bool, and a conditional with such a branch, are operands
                case "nEq5EqNotActive": return x => (x.N == 5) == !x.Active;
                case "notActiveEqNNe5": return x => !x.Active == (x.N != 5);
                case "nEq5EqNotCapturedFlag": return x => (x.N == 5) == !holder.Flag;
                case "flagNeNotCapturedFlag": return x => x.Flag != (bool?)!holder.Flag;
                case "nEq5EqCondNotOther": return x => (x.N == 5) == (x.Id > 3 ? x.Active : !x.Other);
                // R6-3: a side whose truth the fold knows, against a column that can be null, is that value
                case "condFoldedEqFlag": return x => (x.Name.StartsWith(noName) ? (bool?)true : (bool?)true) == x.Flag;
                // R6-4: `!` of a bool? column, and a bool? conditional with a predicate branch, are compensated
                case "notFlagNeTrue": return x => !x.Flag != true;
                case "notFlagEqNull": return x => !x.Flag == null;
                case "flagEqNotFlag": return x => x.Flag == !x.Flag;
                case "nboolCondPredicateBranchNeTrue": return x => (x.Id > 2 ? (bool?)(x.N == 5) : x.Flag) != true;
                // R6-6: `!` of a predicate is a predicate; a conditional's predicate branch is its two-valued truth
                case "notNEq5EqActive": return x => !(x.N == 5) == x.Active;
                case "condPredicateBranchEqActive": return x => (x.Id > 2 ? x.N == 5 : false) == x.Active;
                // Self-check fuzz: `!` over a conditional whose test the rewrite folds stays a value (no literal there)
                case "notCondFoldedTestNeOther": return x => !(x.Id >= none ? x.Active : false) != x.Other;
                // R7-1: `!` over a lifted predicate is `!p`, a predicate
                case "flagNeNotLiftedNGt3": return x => x.Flag != !(bool?)(x.N > 3);
                case "notLiftedNGt3EqNull": return x => !(bool?)(x.N > 3) == null;
                case "notLiftedNameContainsANeFalse": return x => !(bool?)x.Name.Contains(a) != false;
                case "condNotLiftedBranchNeTrue": return x => (x.Id > 3 ? x.Flag : !(bool?)(x.N > 3)) != true;
                // R7-4: the redesign's branches that had no killer
                case "notNotCondFoldedTestNeOther": return x => !!(x.Id >= none ? x.Active : false) != x.Other;
                case "nboolCondNePredicateBranchNeTrue": return x => (x.Id > 2 ? (bool?)(x.N != 5) : x.Flag) != true;
                case "nboolCondNotCondBranchNeTrue": return x => (x.Id <= 2 ? x.Flag : !(x.N != 5 ? (bool?)false : x.Flag)) != true;
                // R7-5: a bool compared with the same operand (here a conditional) is a self-reference, also inside an ||
                case "condSelfOrCode5": return x => (x.Id > 2 ? x.Active : x.Other) == (x.Id > 2 ? x.Active : x.Other) || x.Code == 5;
                // R8-1: a cast back to bool over a lifted form is the form itself: `(bool)!(bool?)p` is `!p`
                case "castNotLiftedNGt3": return x => (bool)!(bool?)(x.N > 3);
                case "castNotLiftedMGtNoneAndOnlyNull": return x => (bool)!(bool?)(x.M > none && onlyNull.Contains(x.M));
                case "castLiftedNGt3EqActive": return x => (bool)(bool?)(x.N > 3) == x.Active;
                // R8-2: refused by 3.11.0 (its text read as a self-comparison), accepted now with C#'s rows
                case "notFlagEqNotFlag": return x => !(x.Flag == !x.Flag);
                // R9-1: a compensated comparison's witness under `!`s or a cast, which the fold knows once rewritten
                case "notNotCondFoldedFlagNeTrue": return x => !!(x.Id >= none ? x.Flag : (bool?)true) != true;
                case "notActiveNeCastCondFolded": return x => !x.Active != (bool)(x.Id >= none ? x.Flag : (bool?)true);
                // R9-2: a cast back to bool over a bool? conditional of lifts is the bool conditional; a cast through object is
                // the cast without it
                case "castCondActiveFalse": return x => (bool)(x.N != 5 ? (bool?)x.Active : (bool?)false);
                case "castCondNNe5Active": return x => (bool)(x.Id > 2 ? (bool?)(x.N != 5) : (bool?)x.Active);
                case "castNotCondActiveFalse": return x => (bool)!(x.N != 5 ? (bool?)x.Active : (bool?)false);
                case "castNotLiftedCondHybrid": return x => (bool)!(bool?)(bool)((x.Code != none) ? (bool?)(none <= 1) : (bool?)onlyNull.Contains(x.M));
                case "objLiftedNotNGt3EqTrue": return x => (bool?)(object)!(x.N > 3) == true;
                case "nullEqNotObjNotFlag": return x => null == !(bool?)(object)!x.Flag;
                // R9-4: the conditional the cast hid is in a truth position: its known branch is a literal (AC-N11 (a))
                case "castLiftedCondKnownBranch": return x => (bool)(bool?)(x.Id > 3 ? x.Active : true);
                // R10-1: a cast back to bool over a conditional with a branch that isn't a lift (a captured bool?) is a row
                // operand in a truth position: its test is rewritten
                case "castCondCapturedBranch": return x => (bool)(x.N != 5 ? (bool?)x.Active : trueBool);
                case "notCastCondCapturedBranch": return x => !(bool)(x.N != 5 ? (bool?)x.Active : trueBool);
                case "idGt2AndCastCondCapturedBranch": return x => x.Id > 2 && (bool)(x.N != 5 ? (bool?)x.Active : trueBool);
                case "castCondCapturedBranchAsTest": return x => ((bool)(x.N != 5 ? (bool?)x.Active : trueBool) ? x.Code : 0) > 4;
                // R11-4: rule 9 through a `!` between the cast and the conditional
                case "castNotCondCapturedBranch": return x => (bool)!(x.N != 5 ? (bool?)x.Active : trueBool);
                case "idGt4AndCastNotCondCapturedBranch": return x => x.Id > 4 && (bool)!(x.N != 5 ? (bool?)x.Active : trueBool);
                // A null test under an odd number of `!`s tests the operand under them (R11-1): not `NOT CASE … IS NULL`
#pragma warning disable CS0472 // the cast's value is never null: the shape is the subject
                case "nullEqLiftedCastNotCond": return x => (bool?)(bool)!(holder.False ? nullBool : (bool?)!x.Other) == null;
#pragma warning restore CS0472
                // R10-3: a cast through object no longer hides a known or self-referencing part from the fold and the guard
                case "objCodeSelfAndIdEq2": return x => (bool)(object)(x.Code == x.Code) && x.Id == 2;
                case "idEq2OrObjCodeSelf": return x => x.Id == 2 || (bool)(object)(x.Code == x.Code);
                case "falseNeObjNameLengthSelfLe": return x => holder.False != (bool)(object)(x.Name.Length <= x.Name.Length);
                // R11-5: comparisons nested in another's operand, no level repeating an earlier one (AC-N14's two-valued
                // sides, EF's form; a compensation over a nested conditional distributed into its branches, R12-2)
                case "nestedCondNeTrue":
                    return x => (x.Id > 1 ? (bool?)((x.Code > 5 ? (bool?)((x.Id > 3 ? (bool?)(x.M > 3) : nullBool) != true) : nullBool) != true) : nullBool) != true;
                case "nestedPredicateEqPredicate": return x => (x.N > 2) == ((x.Code > 2) == ((x.N > 4) == (x.M > 3)));
                case "nestedFlagEqPredicate": return x => x.Flag == ((x.N > 2) == (x.Flag == (x.M > 3)));
                case "nestedCondNeOther":
                    return x => (x.Id > 1 ? (bool?)((x.Id > 2 ? (bool?)((x.Id > 3 ? (bool?)(x.M > 3) : nullBool) != x.Other) : nullBool) != x.Other) : nullBool) != x.Other;
                case "nestedNotCondNeOther":
                    return x => !(x.Id > 1 ? (bool?)(!(x.Id > 2 ? (bool?)(!(x.Id > 3 ? (bool?)(x.M > 3) : nullBool) != x.Other) : nullBool) != x.Other) : nullBool) != x.Other;
                case "notCondGtPredicateValue": return x => !((x.Id > 1 ? x.N : none) > ((x.M > 3) ? 1 : 2));
                // Two operands that can both be null keep EF's forms, the conditional repeated for its null tests (§3)
                case "condNeFlagBothNull": return x => (x.Id > 1 ? (bool?)(x.M > 3) : nullBool) != x.Flag;
                // R12-1: a bool compared with a copy of itself under casts or a string's ToString() is a self-reference, as
                // 3.11.0's text check refused it (rev 12's two-valued sides hid the copy from that check)
                case "nIsNullEqCastNIsNull": return x => (x.N == null) == ((long?)x.N == null);
                case "idGtCodeEqLongCasts": return x => (x.Id > x.Code) == ((long)x.Id > (long)x.Code);
                case "nameIsNullEqToStringIsNull": return x => (x.Name == null) == (x.Name.ToString() == null);
                // Round 12: conditionals compared within conditionals' tests, eight levels down (3.11.0 ran them on SQL Server,
                // with wrong rows where n or m is NULL), and two operands that can both be null nested (R12-2)
                case "nestedCondTestNe8": return NestedInTests(8, (e, x) => Expression.NotEqual(Expression.Condition(e, N(x), M(x)), V()));
                case "nestedCondTestNotGt8": return NestedInTests(8, (e, x) => Expression.Not(Expression.GreaterThan(Expression.Condition(e, N(x), M(x)), V())));
                case "nestedCondBothNullNe": return x => ((x.Id > 2 ? x.N : x.M) != v ? x.N : x.M) != x.M;
                case "nestedCondBothNullEq": return x => ((x.Id > 2 ? x.N : x.M) != v ? x.N : x.M) == x.M;
                // Round 13 (R13-1): a nested conditional against a conditional takes EF's forms; distributed (rev 13), the
                // 9-deep flat operand sat below the distributed CASE, 11 levels, past SQL Server's 10, where 3.11.0 sent 9
                case "nestedCondNeFlat9":
                    return x => ((x.Id > 2 ? x.N : x.M) != v ? x.N : x.M)
                        != (x.Id > 1 ? x.N : x.Id > 2 ? x.M : x.Id > 3 ? x.N : x.Id > 4 ? x.M : x.Id > 5 ? x.N : x.Id > 6 ? x.M : x.Id > 7 ? x.N : x.Id > 8 ? x.M : x.Id > 9 ? x.N : x.M);
                // R13-4: a distributed comparison under negation, in step 5, in step 6, and over a bool
                case "notNestedCondEqV": return x => !(((x.Id > 2 ? x.N : x.M) != v ? x.N : x.M) == v);
                case "notNestedCondEqM": return x => !(((x.Id > 2 ? x.N : x.M) != v ? x.N : x.M) == x.M);
                case "notNestedBoolCondEqFlag": return x => !(((x.Id > 2 ? x.N : x.M) != v ? (bool?)x.Other : nullBool) == x.Flag);
                // R13-2: a bool compared with a copy of itself under lifting casts, whose text holds parameters: 3.11.0 accepted
                // its delete (every row on SQLite and MySQL), R12-1's rule refuses it
                case "idGt2EqLiftedIdGt2": return x => (x.Id > 2) == ((int?)x.Id > (int?)2);
                // R13-3: a distributed leaf that reads no row is C#'s value, so two strings compare ordinally, not under the
                // collation (AC-N11 (a)): "A" != "a" is true on SQL Server's and MySQL's case-insensitive collations too
                case "nestedCondUpperANeA": return x => ((x.Id > 2 ? x.N : x.M) != v ? "A" : x.Name) != lowerA;
                // R14-3: a length that can be null compared with itself under a cast through object: EF's form with one
                // witness, `(LENGTH(name) = LENGTH(name) OR name IS NULL)`, refused as 3.11.0 refused `LENGTH(name) = LENGTH(name)`
                case "objNameLengthSelfEq": return x => (bool)(object)((int?)x.Name.Length == (int?)x.Name.Length);
                // R14-1: a nested conditional compared with a conditional at every level, eight deep (rev 14: EF's form at each
                // level, 36,081 parameters, past SQLite's and SQL Server's limits; 3.11.0: 25, with C#'s rows)
                case "nestedCondChainNN8": return ChainAgainstConditionals(8, false);
                case "nestedCondChainNNRight8": return ChainAgainstConditionals(8, true);
                // R15-1: two nested sides, the chain's earlier level on the right of a shallow nested conditional: the larger
                // side is distributed first, so the shallow one is what the leaves repeat (rev 15: left first, threefold per
                // level). Seven levels: the source nests 8, and SQL Server's Any() sends its query inside a CASE, so at the
                // ruled + 1 that path nests 10, its limit
                case "nestedCondChainNestedLeft7": return ChainAgainstNested(7);
                // A nullable integer's ToString() is COALESCE(CAST(...), ''), which SQL Server counts as a CASE: a nested
                // conditional nine CASEs deep against a flat one eight deep ending in it keeps EF's form, 3.11.0's 9 (compared
                // in the branches: 11, Msg 125)
                case "nestedStrNeFlat8NToString": return NestedAgainstToString();
                // AC-N6 leaves a cast item rejected (§3)
                case "castItemContains": return x => onlyNull.Contains((int?)x.Code);
                default: throw new ArgumentException("unknown key " + key);
            }
        }

        private readonly Holder holder = new Holder();

        /// <summary>
        /// <c>x.Id &gt; 2</c> compared within <paramref name="levels"/> conditionals' tests: each level is
        /// <paramref name="level"/> of the one below it (<c>((x.Id &gt; 2 ? x.N : x.M) != v ? x.N : x.M) != v</c> and on).
        /// </summary>
        private Expression<Func<NsRow, bool>> NestedInTests(int levels, Func<Expression, ParameterExpression, Expression> level)
        {
            var x = Expression.Parameter(typeof(NsRow), "x");
            Expression body = Expression.GreaterThan(Expression.Property(x, nameof(NsRow.Id)), Expression.Constant(2));
            for (var i = 0; i < levels; i++)
                body = level(body, x);
            return Expression.Lambda<Func<NsRow, bool>>(body, x);
        }

        /// <summary>
        /// R14-1: <c>L(j) = (L(j-1) != C(j)) ? (int?)x.Code : v</c> with <c>C(j) = x.Id &gt; j ? (int?)x.Code : v</c>, a
        /// conditional that is never NULL by value though its type can hold null, <paramref name="levels"/> deep; with
        /// <paramref name="nestedOnRight"/>, each comparison is <c>C(j) != L(j-1)</c>.
        /// </summary>
        private Expression<Func<NsRow, bool>> ChainAgainstConditionals(int levels, bool nestedOnRight)
        {
            var x = Expression.Parameter(typeof(NsRow), "x");
            Func<Expression> code = () => Expression.Convert(Expression.Property(x, nameof(NsRow.Code)), typeof(int?));
            Func<int, Expression> other = j => Expression.Condition(Expression.GreaterThan(Expression.Property(x, nameof(NsRow.Id)), Expression.Constant(j)), code(), V());
            Func<Expression, Expression, Expression> ne = (nested, conditional) =>
                nestedOnRight ? Expression.NotEqual(conditional, nested) : Expression.NotEqual(nested, conditional);
            var chain = other(0);
            for (var j = 1; j < levels; j++)
                chain = Expression.Condition(ne(chain, other(j)), code(), V());
            return Expression.Lambda<Func<NsRow, bool>>(ne(chain, other(levels)), x);
        }

        /// <summary>
        /// R15-1: <c>L(j) = (S(j) != L(j-1)) ? (int?)x.Code : v</c>, where <c>S(j) = ((x.Id &gt; j ? (int?)x.Code : v) != v) ?
        /// (int?)x.Code : v</c> is a shallow nested conditional on the left of every level, <paramref name="levels"/> deep.
        /// </summary>
        private Expression<Func<NsRow, bool>> ChainAgainstNested(int levels)
        {
            var x = Expression.Parameter(typeof(NsRow), "x");
            Func<Expression> code = () => Expression.Convert(Expression.Property(x, nameof(NsRow.Code)), typeof(int?));
            Func<int, Expression> shallow = j => Expression.Condition(
                Expression.NotEqual(Expression.Condition(Expression.GreaterThan(Expression.Property(x, nameof(NsRow.Id)), Expression.Constant(j)), code(), V()), V()),
                code(), V());
            Expression chain = shallow(0);
            for (var j = 1; j < levels; j++)
                chain = Expression.Condition(Expression.NotEqual(shallow(j), chain), code(), V());
            return Expression.Lambda<Func<NsRow, bool>>(Expression.NotEqual(shallow(levels), chain), x);
        }

        /// <summary>
        /// R16-3: <c>L(j) = (C(j) != L(j-1)) ? (int?)x.Code : v</c>, with <c>C(j) = x.Id &gt; j ? (int?)x.Code : v</c> and the
        /// first level <c>((x.Id &gt; 0 ? x.NX : x.N) != v) ? (int?)x.Code : v</c>, whose test reads the alias
        /// <see cref="NsComputed.NX"/>; <paramref name="levels"/> deep.
        /// </summary>
        private Expression<Func<NsComputed, bool>> ChainNXInTest(int levels)
        {
            var x = Expression.Parameter(typeof(NsComputed), "x");
            Func<string, Expression> col = name => Expression.Property(x, name);
            Func<Expression> code = () => Expression.Convert(col(nameof(NsComputed.Code)), typeof(int?));
            Func<int, Expression> other = j => Expression.Condition(Expression.GreaterThan(col(nameof(NsComputed.Id)), Expression.Constant(j)), code(), V());
            Expression chain = Expression.Condition(
                Expression.NotEqual(Expression.Condition(Expression.GreaterThan(col(nameof(NsComputed.Id)), Expression.Constant(0)), col(nameof(NsComputed.NX)), col(nameof(NsComputed.N))), V()),
                code(), V());
            for (var j = 1; j < levels; j++)
                chain = Expression.Condition(Expression.NotEqual(other(j), chain), code(), V());
            return Expression.Lambda<Func<NsComputed, bool>>(Expression.NotEqual(other(levels), chain), x);
        }

        /// <summary>
        /// A nested string conditional nine CASEs deep (an eight-level chain in its test) compared with a flat one eight deep
        /// whose last branch is <c>x.N.ToString()</c>, sent as <c>COALESCE(CAST(n AS …), '')</c> since <c>N</c> can be null
        /// (R4-10): SQL Server counts the COALESCE as a CASE, 9 at 3.11.0, which sent the same COALESCE inside the flat one.
        /// </summary>
        private Expression<Func<NsRow, bool>> NestedAgainstToString()
        {
            var x = Expression.Parameter(typeof(NsRow), "x");
            Func<string, Expression> col = name => Expression.Property(x, name);
            Expression test = Expression.GreaterThan(col(nameof(NsRow.Id)), Expression.Constant(2));
            for (var j = 0; j < 8; j++)
                test = Expression.NotEqual(Expression.Condition(test, col(nameof(NsRow.N)), col(nameof(NsRow.M))), V());
            var nested = Expression.Condition(test, col(nameof(NsRow.Name)), col(nameof(NsRow.Req)));
            Expression flat = Expression.Call(col(nameof(NsRow.N)), typeof(int?).GetMethod(nameof(ToString), Type.EmptyTypes));
            for (var j = 8; j >= 1; j--)
                flat = Expression.Condition(Expression.GreaterThan(col(nameof(NsRow.Id)), Expression.Constant(j)), col(nameof(NsRow.Name)), flat);
            return Expression.Lambda<Func<NsRow, bool>>(Expression.NotEqual(nested, flat), x);
        }

        /// <summary>
        /// R15-3: a nested conditional nine CASEs deep (an eight-level chain in its test) against a flat one eight deep whose
        /// last branch is <see cref="NsComputed.CodeCase"/>, a template with its own CASE: SQL Server's text nests 9 at
        /// 3.11.0. Compared in the branches, the template would sit below the leaf's CASE: 11 (Msg 125).
        /// </summary>
        private Expression<Func<NsComputed, bool>> NestedAgainstCodeCase()
        {
            var x = Expression.Parameter(typeof(NsComputed), "x");
            Func<string, Expression> col = name => Expression.Property(x, name);
            Expression test = Expression.GreaterThan(col(nameof(NsComputed.Id)), Expression.Constant(2));
            for (var j = 0; j < 8; j++)
                test = Expression.NotEqual(Expression.Condition(test, col(nameof(NsComputed.N)), col(nameof(NsComputed.NX))), V());
            var nested = Expression.Condition(test, col(nameof(NsComputed.N)), col(nameof(NsComputed.NX)));
            Expression flat = col(nameof(NsComputed.CodeCase));
            for (var j = 8; j >= 1; j--)
                flat = Expression.Condition(Expression.GreaterThan(col(nameof(NsComputed.Id)), Expression.Constant(j)), col(nameof(NsComputed.N)), flat);
            return Expression.Lambda<Func<NsComputed, bool>>(Expression.NotEqual(nested, flat), x);
        }

        private static Expression N(ParameterExpression x) => Expression.Property(x, nameof(NsRow.N));

        private static Expression M(ParameterExpression x) => Expression.Property(x, nameof(NsRow.M));

        private Expression V() => Expression.Field(Expression.Constant(this, typeof(NullSemanticsHarness)), nameof(v));

        /// <summary>The most CASEs nested in <paramref name="sql"/> (SQL Server allows 10), outside quotes.</summary>
        protected static int CaseNesting(string sql)
        {
            int depth = 0, deepest = 0;
            foreach (Match token in Regex.Matches(sql ?? string.Empty, @"'(?:[^']|'')*'|\bCASE\b|\bEND\b", RegexOptions.IgnoreCase))
            {
                if (string.Equals(token.Value, "CASE", StringComparison.OrdinalIgnoreCase))
                    deepest = Math.Max(deepest, ++depth);
                else if (string.Equals(token.Value, "END", StringComparison.OrdinalIgnoreCase))
                    depth--;
            }
            return deepest;
        }

        /// <summary>
        /// EF's answer where C# would throw on a null receiver (a string method, <c>.Value.Year</c>, <c>.Length</c>) or on
        /// a null search value: the same predicate, null-safe. Null means the predicate itself is the oracle.
        /// </summary>
        protected virtual Func<NsRow, bool> Oracle(string key)
        {
            switch (key)
            {
                case "dYearNe2020": return x => x.D == null || x.D.Value.Year != 2020;
                case "nameLengthNe1": return x => x.Name == null || x.Name.Length != 1;
                case "nameLengthSelf": return x => true;
                case "objNameLengthSelfEq": return x => true;
                case "nameLengthSelfNe": return x => false;
                case "nGtNoneCondCode": return x => true;
                case "notNameLengthGt1": return x => !(x.Name != null && x.Name.Length > 1);
                case "notNameContainsA": return x => !(x.Name != null && x.Name.Contains(a));
                case "notNameStartsWithA": return x => !(x.Name != null && x.Name.StartsWith(a));
                case "notNameEndsWithA": return x => !(x.Name != null && x.Name.EndsWith(a));
                case "nameContainsNull": return x => false;
                case "notNameContainsNull":
                case "notNameStartsWithNull":
                case "notNameEndsWithNull": return x => true;
                case "idOrNotNameContainsNull": return x => true;
                case "idAndNotNameContainsNull": return x => x.Id == 2;
                case "notAndContainsNull": return x => true;
                case "nameContainsAEqTrue":
                case "nameContainsA": return x => x.Name != null && x.Name.Contains(a);
                case "nameContainsAEqFalse":
                case "notNameContainsAEqTrue":
                case "nameContainsANeTrue":
                case "nameContainsAEqInclude": return x => !(x.Name != null && x.Name.Contains(a));
                case "nameContainsNullEqFalse": return x => true;
                case "boolCondBranches": return x => x.N > 3 ? x.M != 5 : x.Name == a;
                case "notIdAndNameContainsA": return x => !(x.Id == 2 && x.Name != null && x.Name.Contains(a));
                case "notIdAndNameContainsNull":
                case "nameContainsNullNeTrue":
                case "notNameContainsNullEqTrue":
                case "nameContainsANeNullBool": return x => true;
                case "nameContainsAEqNullBool": return x => false;
                // C# throws on StartsWith(null); both branches are true whatever the test says.
                case "condFoldedEqFlag": return x => x.Flag == true;
                // C# throws on a null Name; EF's Contains is false there, so its `!` is true.
                case "notLiftedNameContainsANeFalse": return x => !(x.Name != null && x.Name.Contains(a));
                // C# throws on a null Name; EF's LENGTH of a NULL is NULL, so the comparison is false there.
                case "falseNeObjNameLengthSelfLe": return x => x.Name != null;
                // C# throws on a null Name's ToString(); EF's two sides are the same NULL test, so every row.
                case "nameIsNullEqToStringIsNull": return x => true;
                default: return null;
            }
        }

        #endregion

        #region Expectations (plan §4.3)

        /// <summary>The guard's refusal of a delete (§4.3 and the unchanged 3.11.0 refusals), as its message; null when Delete must agree with Query.</summary>
        protected virtual string ExpectedDeleteRejection(string key)
        {
            switch (key)
            {
                case "notNameContainsNull":
                case "notNameStartsWithNull":
                case "notNameEndsWithNull":
                case "idOrNotNameContainsNull":
                case "notAndContainsNull":
                case "nameContainsNullEqFalse":
                case "notNGeNone":
                case "idOrNotNGeNone":
                case "idNeNoneInt":
                case "idEq2OrIdNeNoneInt":
                case "notIdEqNone":
                case "idNeNullLiteral":
                case "notIdEqNullLiteral":
                case "notIdAndNameContainsNull":
                case "nameContainsNullNeTrue":
                case "notNameContainsNullEqTrue":
                case "notNGtNone":
                case "notNLtNone":
                case "notNLeNone":
                case "nameContainsANeNullBool":
                case "nGtNoneCondCode":
                case "condSelfOrCode5":
                case "castNotLiftedMGtNoneAndOnlyNull":
                case "castNotLiftedCondHybrid":
                case "idEq2OrObjCodeSelf":
                // Since rev 18 (R17-2) an [SqlExpression] alias is its column to the verdict, so these are a self-comparison
                // inside another operator, always true, as their column forms have been since 3.10 (the first two were refused
                // as self-referencing by the text check; the last one 3.11.0 accepted, every row)
                case "activeEqActiveXNeFalse":
                case "nToStringEqNXToStringOrNNull":
                case "activeEqActiveXOrFlagNull":
                // Since rev 18 the verdict sees through the cast through object: a self-comparison under `false !=`, always
                // true (the text check had refused it as self-referencing; R10-3's row says either)
                case "falseNeObjNameLengthSelfLe":
                    return AlwaysTrueMessage;
                case "nameLengthSelf":
                case "nEq5EqNEq5":
                case "nIsNullEqCastNIsNull":
                case "idGtCodeEqLongCasts":
                case "nameIsNullEqToStringIsNull":
                case "idGt2EqLiftedIdGt2":
                case "nEqNX":
                case "notNNeNX":
                case "objNameLengthSelfEq":
                case "nameLengthEqNameXLength":
                case "nameXLengthEqNameLength":
                case "dYearEqDXYear":
                case "notNameLengthNeNameXLength":
                case "nestedCondSelfCopy":
                case "nestedCondAliasCopy":
                case "nBothEqNBothX":
                case "nestedNAbsUpperEqNAbsLower":
                case "nestedDocKEqDocK2":
                    return "The WHERE clause cannot be a self-referencing column expression.";
                default:
                    return null;
            }
        }

        /// <summary>
        /// The SQL half of a rows-pin / SQL-proof key (plan §6.2): the SELECT's WHERE must have this shape. At 25956e0 the
        /// rows already agree and this assertion is what fails.
        /// </summary>
        protected virtual void AssertWhereShape(string key, string where)
        {
            switch (key)
            {
                case "idEqNoneInt":
                case "idEqNullLiteral":
                case "nGtNone":
                    StringAssert.Contains(where, "1=0", key + ": a comparison that is false for every row is sent as 1=0");
                    Assert.IsFalse(where.Contains("@"), key + ": no parameter is bound for the folded comparison: " + where);
                    break;
                case "reqNeA":
                    StringAssert.Contains(where, " IS NULL", key + ": a string column is compensated (it can be NULL as far as FunkyORM knows)");
                    break;
                case "condNullBranchEq5":
                case "condNullBranchGt3":
                    // R5-4: the rows agree where the database binds the null; the literal is what PostgreSQL needs (42804).
                    StringAssert.Contains(where, "ELSE NULL END", key + ": a null captured value in a value branch is the literal NULL");
                    break;
                case "boolCondEqTrue":
                case "nboolCondLiteralBranchEqTrue":
                case "notBoolCondEqTrue":
                case "notNboolCondLiteralNeFalse":
                    // R5-1, R6-1: a value-position conditional stays a value, also under `!`: no literal in a branch.
                    Assert.IsFalse(where.Contains("1=0"), key + ": a literal in a value-position branch: " + where);
                    break;
                case "notFlagNeTrue":
                case "notFlagEqNull":
                case "flagEqNotFlag":
                    // R6-4: `!` of the column moves to the comparison, so the compensated form has no NOT on a bit value.
                    Assert.IsFalse(where.Contains("NOT "), key + ": NOT on a value: " + where);
                    StringAssert.Contains(where, RowTable + ".flag IS NULL", key + ": the column's NULLs are C#'s answer");
                    break;
                case "notCondFoldedTestNeOther":
                    // A literal is no value T-SQL accepts: the folded conditional stays a CASE under the NOT.
                    StringAssert.Contains(where, "NOT CASE WHEN 1=0 THEN ", key + ": the conditional, its folded test a literal");
                    break;
                case "notNotCondFoldedTestNeOther":
                    StringAssert.Contains(where, "NOT NOT CASE WHEN 1=0 THEN ", key + ": the conditional under two NOTs stays a CASE");
                    break;
                case "nboolCondPredicateBranchNeTrue":
                case "condPredicateBranchEqActive":
                    // R6-4, R6-6: a conditional's predicate branch is its two-valued truth, a CASE (EF's form).
                    StringAssert.Contains(where, "THEN CASE WHEN ", key + ": the predicate branch as CASE WHEN p THEN 1 ELSE 0 END");
                    break;
                case "nameLengthSelfNe":
                    // The rows agree (none, before and after): the same witness under != is tested once, and IS NOT NULL.
                    StringAssert.Contains(where, RowTable + ".name IS NOT NULL", key + ": EF's != of two values that can be null");
                    break;
                case "flagEqNGt5":
                    // SQLite's precedence reads 3.11.0's `flag = n > @p` as `flag = (n > @p)`, and no seed row has a null n
                    // with a false flag, so the rows agree there; the predicate's two-valued CASE (AC-N14, R11-5) is what differs.
                    StringAssert.Contains(where, "CASE WHEN " + RowTable + ".n > ", key + ": the predicate as its two-valued value, CASE WHEN p THEN 1 ELSE 0 END");
                    break;
                case "nestedCondNeFlat9":
                    // R13-1: EF's forms add no CASE level, so the nesting is the source's 9 (rev 13 distributed it: 11).
                    Assert.AreEqual(9, CaseNesting(where), key + ": the CASE nesting of " + where);
                    break;
                case "nxChainInTest7":
                    // R16-3: an [SqlExpression] alias without a level of its own in the innermost test: distributed at every
                    // level, linear (rev 16 refused the distribution against a remote-map property: EF's form at every level
                    // above it, past SQL Server's 2,100 parameters at 6). The source nests 8.
                    Assert.IsTrue(CaseNesting(where) <= 9, key + ": " + CaseNesting(where) + " CASEs nested in " + where);
                    Assert.IsTrue(Regex.Matches(where, @"@p__linq__\d+").Count <= 200, key + ": " + Regex.Matches(where, @"@p__linq__\d+").Count + " parameters");
                    break;
                case "nestedStrNeFlat8NToString":
                    // EF's form adds no CASE level: the source's 9, with the COALESCE inside the flat conditional's 8, where
                    // 3.11.0 sent it too (compared in the branches: 10 CASEs, the COALESCE an eleventh level on SQL Server).
                    Assert.AreEqual(9, CaseNesting(where), key + ": the CASE nesting of " + where);
                    StringAssert.Contains(where, "COALESCE(", key + ": " + where);
                    break;
                case "nestedCondChainNN8":
                case "nestedCondChainNNRight8":
                case "nestedCondChainNestedLeft7":
                    // R14-1: distributed where that nests at most one CASE more than the source (8), so the SQL grows linearly:
                    // rev 13 sent 110 parameters, rev 14 36,081, and rev 15 R15-1's chain past SQL Server's 2,100.
                    Assert.IsTrue(CaseNesting(where) <= 9, key + ": " + CaseNesting(where) + " CASEs nested in " + where);
                    Assert.IsTrue(Regex.Matches(where, @"@p__linq__\d+").Count <= 200, key + ": " + Regex.Matches(where, @"@p__linq__\d+").Count + " parameters");
                    break;
                case "nestedCondNeFlat8CodeCase":
                    // R15-3: a template's CASE counts too, so the distribution isn't kept and EF's form adds no level: at most
                    // SQL Server's 10 (rev 15: 11, Msg 125), 3.11.0's 9 + 1.
                    Assert.IsTrue(CaseNesting(where) <= 10, key + ": the CASE nesting of " + where);
                    break;
                case "nestedCondBothNullNe":
                case "nestedCondBothNullEq":
                    // R12-2: the outer conditional is distributed, so its test, which holds the inner level, is sent once; only
                    // the flat inner conditional is repeated, for its IS NULL (rev 12 sent the outer three times, EF's forms).
                    Assert.IsTrue(Regex.Matches(where, Regex.Escape(RowTable) + @"\.id > ").Count <= 2, key + ": the inner level is repeated: " + where);
                    break;
                case "nestedCondNeTrue":
                case "nestedPredicateEqPredicate":
                case "nestedFlagEqPredicate":
                case "nestedCondNeOther":
                case "nestedNotCondNeOther":
                case "notCondGtPredicateValue":
                    // R11-5, R12-2: no level repeats an earlier one, so the innermost column is sent at most three times (a
                    // flat innermost conditional, repeated as EF repeats it; rev 11's forms repeated it per level), and each
                    // level is a CASE: a predicate's two-valued value, or a comparison distributed into a conditional.
                    Assert.IsTrue(Regex.Matches(where, Regex.Escape(RowTable) + @"\.m\b").Count <= 3, key + ": m is sent more than three times: " + where);
                    StringAssert.Contains(where, "CASE WHEN ", key + ": each level as a CASE");
                    break;
                case "nestedCondTestNe8":
                case "nestedCondTestNotGt8":
                    // Round 12: eight conditionals nested in tests are eight CASEs, as 3.11.0 sent them (rev 12 nested sixteen,
                    // past SQL Server's 10), and linear: the compared value is sent once per level and its branches.
                    Assert.AreEqual(8, CaseNesting(where), key + ": the CASE nesting of " + where);
                    break;
                case "nameLengthSelf":
                    Assert.AreEqual(1, Regex.Matches(where, @"\bIS NULL\b").Count, key + ": the repeated witness is tested once: " + where);
                    break;
                case "notIdsWithNullContainsN":
                    StringAssert.Contains(where, " IS NOT NULL AND NOT ", key + ": a negated IN with a null item excludes the NULL rows explicitly");
                    break;
                case "objCodeSelfAndIdEq2":
                    // R10-3: the rows agree before and after; 3.11.0 sent the self-comparison, hidden from the fold by the cast.
                    StringAssert.Contains(where, "NOT 1=0", key + ": the self-comparison a cast through object hid is a literal");
                    break;
                case "castLiftedCondKnownBranch":
                    // R9-4: the rows agree before and after; 3.11.0 sent the branch as a parameter, the cast keeping the
                    // conditional in a value position.
                    StringAssert.Contains(where, "ELSE NOT 1=0 END", key + ": the known branch of a conditional in a truth position is a literal");
                    break;
            }
        }

        #endregion

        #region Plumbing

        protected static string Ids(IEnumerable<int> ids) => string.Join(",", ids.OrderBy(i => i));

        private static string Normalize(string sql) => Regex.Replace(sql ?? string.Empty, @"\s+", " ").Trim();

        /// <summary>
        /// The WHERE body of the first logged command that starts with <paramref name="verb"/>, whitespace-normalized: the
        /// outer WHERE, found outside parentheses and quotes, so a <c>[SubqueryAggregate]</c>'s inner WHERE in the SELECT
        /// list isn't taken for it.
        /// </summary>
        protected static string WhereOf(List<string> log, string verb, string context)
        {
            var command = log.FirstOrDefault(e => e != null && Regex.IsMatch(e, @"^\s*" + verb + @"\b", RegexOptions.IgnoreCase));
            Assert.IsNotNull(command, context + ": no " + verb + " logged: " + string.Join(" | ", log.Select(Normalize)));
            var text = Normalize(command);
            var index = OuterKeyword(text, "WHERE");
            Assert.IsTrue(index >= 0, context + ": no WHERE in " + text);
            var where = text.Substring(index + "WHERE".Length);
            foreach (var trailer in new[] { "ORDER BY", "LIMIT", "OFFSET" })
            {
                var end = OuterKeyword(where, trailer);
                if (end >= 0)
                    where = where.Substring(0, end);
            }
            return where.Trim();
        }

        /// <summary>The index of <paramref name="keyword"/> as a whole word at parenthesis depth 0 and outside quotes; -1 if none.</summary>
        private static int OuterKeyword(string text, string keyword)
        {
            var depth = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\'')
                {
                    i++;
                    while (i < text.Length && text[i] != '\'')
                        i++;
                    continue;
                }
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (depth == 0 && string.Compare(text, i, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0 &&
                         (i == 0 || !char.IsLetterOrDigit(text[i - 1])) &&
                         (i + keyword.Length == text.Length || !char.IsLetterOrDigit(text[i + keyword.Length])))
                    return i;
            }
            return -1;
        }

        /// <summary>The logged parameter values, in order.</summary>
        protected static string ParametersOf(List<string> log) =>
            string.Join(" | ", log.Where(e => e != null && Regex.IsMatch(e, @"^@\w+: ")).Select(Normalize));

        private static List<int> Remaining(OrmDataProvider provider) => provider.Query<NsRow>().ToList().Select(r => r.Id).ToList();

        protected void WithTables(Action<OrmDataProvider, List<string>> body)
        {
            RequireDatabase();
            try
            {
                CreateTables();
                using (var provider = CreateProvider())
                {
                    // Discover the types before the log is captured, so the first command logged is the query's.
                    provider.Query<NsRow>().Where(x => x.Id > 0).ToList();
                    provider.Query<NsComputed>().Where(x => x.Id > 0).ToList();
                    provider.Query<NsRemote>().Where(x => x.Id > 0).ToList();
                    var log = new List<string>();
                    provider.Log = entry => log.Add(entry);
                    body(provider, log);
                }
            }
            finally
            {
                DropTables();
            }
        }

        private string Expected(string key)
        {
            var oracle = Oracle(key) ?? Predicate(key).Compile();
            var rows = Rows.Select(AsRow).ToList();
            try
            {
                return Ids(rows.Where(oracle).Select(r => r.Id));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(key + ": the oracle throws on the seed (" + ex.GetType().Name + "); give the key an Oracle", ex);
            }
        }

        #endregion

        /// <summary>
        /// AC-N1..N8, N10, N14: every path selects (counts, deletes) the oracle's rows, sends one WHERE, and the guard's
        /// delete verdict is §4.3's.
        /// </summary>
        [DataTestMethod]
        // AC-N1
        [DataRow("nNeV")] [DataRow("nNe5")] [DataRow("vNeN")] [DataRow("nameNeA")] [DataRow("aNeName")] [DataRow("reqNeA")]
        [DataRow("flagNeTrue")] [DataRow("flagNeFalse")] [DataRow("dYearNe2020")] [DataRow("nameLengthNe1")] [DataRow("eNeRed")]
        [DataRow("eNeGreenLiteral")] [DataRow("condNullableNe")] [DataRow("nNeCapturedShort")] [DataRow("nLongNeCapturedInt")]
        [DataRow("nPriceNeCapturedInt")] [DataRow("colorNeCapturedColor")]
        // AC-N2
        [DataRow("nEqNone")] [DataRow("nNeNone")] [DataRow("noneNeN")] [DataRow("nameEqNoName")] [DataRow("nameNeNoName")]
        [DataRow("eEqNoColor")] [DataRow("idEqNoneInt")] [DataRow("idNeNoneInt")] [DataRow("idEq2OrIdNeNoneInt")]
        [DataRow("idEqNullLiteral")] [DataRow("idNeNullLiteral")] [DataRow("notIdEqNullLiteral")] [DataRow("notIdEqNone")]
        [DataRow("reqNeNoName")] [DataRow("nEqShortNull")] [DataRow("condFoldedTest")] [DataRow("nEq5OrNNe5")]
        // AC-N3
        [DataRow("nEqM")] [DataRow("nNeM")] [DataRow("nEqCode")] [DataRow("nNeCode")] [DataRow("nameLengthSelf")]
        // AC-N4
        [DataRow("notNEqV")] [DataRow("notNNeV")] [DataRow("notNGtV")] [DataRow("notNGeNone")] [DataRow("idOrNotNGeNone")]
        [DataRow("notAndEq")] [DataRow("notOrEq")] [DataRow("nNe5OrNameEqA")] [DataRow("doubleNot")] [DataRow("notNameLengthGt1")]
        [DataRow("condTestNe")] [DataRow("notBoolCond")] [DataRow("boolCondBranches")]
        // AC-N5
        [DataRow("nGtNone")] [DataRow("nGtV")]
        // AC-N6
        [DataRow("idsContainsN")] [DataRow("notIdsContainsN")] [DataRow("idsWithNullContainsN")] [DataRow("notIdsWithNullContainsN")]
        [DataRow("onlyNullContainsN")] [DataRow("notOnlyNullContainsN")] [DataRow("id3AndIdsWithNullContainsN")]
        [DataRow("id3AndNotIdsContainsN")] [DataRow("notIntIdsContainsId")] [DataRow("notNamesContainsName")]
        // AC-N7
        [DataRow("notNameContainsA")] [DataRow("notNameStartsWithA")] [DataRow("notNameEndsWithA")]
        // AC-N8
        [DataRow("nameContainsNull")] [DataRow("notNameContainsNull")] [DataRow("notNameStartsWithNull")] [DataRow("notNameEndsWithNull")]
        [DataRow("idOrNotNameContainsNull")] [DataRow("idAndNotNameContainsNull")] [DataRow("notAndContainsNull")]
        // AC-N14
        [DataRow("nameContainsAEqTrue")] [DataRow("nameContainsAEqFalse")] [DataRow("notNameContainsAEqTrue")]
        [DataRow("nameContainsANeTrue")] [DataRow("nameContainsNullEqFalse")] [DataRow("nameContainsAEqInclude")]
        [DataRow("nEq5EqMEq5")] [DataRow("nEq5NeMEq5")] [DataRow("flagEqNGt5")] [DataRow("flagNeNGt5")]
        [DataRow("notIdAndNameContainsA")] [DataRow("notIdOrIdsContainsN")] [DataRow("notIdAndNameContainsNull")]
        [DataRow("condTestNeGt0")] [DataRow("condTestNeEqV")] [DataRow("nestedCondTestNe")]
        [DataRow("idOrNotOnlyNullContainsN")] [DataRow("notIdAndOnlyNullContainsN")] [DataRow("nameContainsNullNeTrue")]
        [DataRow("notNameContainsNullEqTrue")] [DataRow("notNGtNone")] [DataRow("notNLtNone")] [DataRow("notNLeNone")]
        [DataRow("nameContainsANeNullBool")] [DataRow("nameContainsAEqNullBool")] [DataRow("nEq5EqNEq5")]
        [DataRow("nameLengthSelfNe")] [DataRow("notNGtM")] [DataRow("flagEqFoldedPredicate")] [DataRow("flagNeFoldedPredicate")]
        [DataRow("boolCondEqTrue")] [DataRow("colEqBoolCond")] [DataRow("notActiveEqTrue")] [DataRow("notActiveEqOther")]
        [DataRow("nboolCondLiteralBranchEqTrue")] [DataRow("condNullBranchEq5")] [DataRow("condNullBranchGt3")]
        [DataRow("notCondTestNeEq1")] [DataRow("nGtNoneCondCode")]
        // Round 6
        [DataRow("notBoolCondEqTrue")] [DataRow("notNboolCondLiteralNeFalse")] [DataRow("nEq5EqNotActive")] [DataRow("notActiveEqNNe5")]
        [DataRow("nEq5EqNotCapturedFlag")] [DataRow("flagNeNotCapturedFlag")] [DataRow("nEq5EqCondNotOther")] [DataRow("condFoldedEqFlag")]
        [DataRow("notFlagNeTrue")] [DataRow("notFlagEqNull")] [DataRow("flagEqNotFlag")] [DataRow("nboolCondPredicateBranchNeTrue")]
        [DataRow("notNEq5EqActive")] [DataRow("condPredicateBranchEqActive")] [DataRow("notCondFoldedTestNeOther")]
        // Round 7
        [DataRow("flagNeNotLiftedNGt3")] [DataRow("notLiftedNGt3EqNull")] [DataRow("notLiftedNameContainsANeFalse")]
        [DataRow("condNotLiftedBranchNeTrue")] [DataRow("notNotCondFoldedTestNeOther")] [DataRow("nboolCondNePredicateBranchNeTrue")]
        [DataRow("nboolCondNotCondBranchNeTrue")] [DataRow("condSelfOrCode5")]
        // Round 8
        [DataRow("castNotLiftedNGt3")] [DataRow("castNotLiftedMGtNoneAndOnlyNull")] [DataRow("castLiftedNGt3EqActive")]
        [DataRow("notFlagEqNotFlag")]
        // Round 9
        [DataRow("notNotCondFoldedFlagNeTrue")] [DataRow("notActiveNeCastCondFolded")] [DataRow("castCondActiveFalse")]
        [DataRow("castCondNNe5Active")] [DataRow("castNotCondActiveFalse")] [DataRow("castNotLiftedCondHybrid")]
        [DataRow("objLiftedNotNGt3EqTrue")] [DataRow("nullEqNotObjNotFlag")] [DataRow("castLiftedCondKnownBranch")]
        // Round 10
        [DataRow("castCondCapturedBranch")] [DataRow("notCastCondCapturedBranch")] [DataRow("idGt2AndCastCondCapturedBranch")]
        [DataRow("castCondCapturedBranchAsTest")] [DataRow("nullEqLiftedCastNotCond")] [DataRow("objCodeSelfAndIdEq2")]
        [DataRow("idEq2OrObjCodeSelf")] [DataRow("falseNeObjNameLengthSelfLe")]
        // Round 11
        [DataRow("castNotCondCapturedBranch")] [DataRow("idGt4AndCastNotCondCapturedBranch")] [DataRow("nestedCondNeTrue")]
        [DataRow("nestedPredicateEqPredicate")] [DataRow("nestedFlagEqPredicate")] [DataRow("nestedCondNeOther")]
        [DataRow("nestedNotCondNeOther")] [DataRow("notCondGtPredicateValue")] [DataRow("condNeFlagBothNull")]
        // Round 12
        [DataRow("nIsNullEqCastNIsNull")] [DataRow("idGtCodeEqLongCasts")] [DataRow("nameIsNullEqToStringIsNull")]
        [DataRow("nestedCondTestNe8")] [DataRow("nestedCondTestNotGt8")] [DataRow("nestedCondBothNullNe")] [DataRow("nestedCondBothNullEq")]
        // Round 13
        [DataRow("nestedCondNeFlat9")] [DataRow("notNestedCondEqV")] [DataRow("notNestedCondEqM")] [DataRow("notNestedBoolCondEqFlag")]
        [DataRow("idGt2EqLiftedIdGt2")] [DataRow("nestedCondUpperANeA")]
        // Round 14
        [DataRow("objNameLengthSelfEq")] [DataRow("nestedCondChainNN8")] [DataRow("nestedCondChainNNRight8")]
        // Round 15
        [DataRow("nestedCondChainNestedLeft7")] [DataRow("nestedStrNeFlat8NToString")]
        public void EveryPath_SelectsTheOraclesRows(string key) => EveryPath(key, Predicate(key), Expected(key), r => r.Id);

        private void EveryPath<T>(string key, Expression<Func<T, bool>> predicate, string expected, Func<T, int> id) where T : class, new()
        {
            var rejection = ExpectedDeleteRejection(key);
            var expectedCount = expected.Length == 0 ? 0 : expected.Split(',').Length;

            WithTables((provider, log) =>
            {
                if (RejectedByTheDialect(key))
                {
                    AssertDialectRejects(key, provider, predicate);
                    return;
                }
                if (AssertedProviderRejection(key, () => provider.Query(predicate)))
                    return;

                log.Clear();
                Assert.AreEqual(expected, Ids(provider.Query(predicate).Select(id)), key + " query");
                var selectWhere = WhereOf(log, "SELECT", key + " query");
                var selectParameters = ParametersOf(log);
                AssertWhereShape(key, selectWhere);

                log.Clear();
                Assert.AreEqual(expected, Ids(provider.QueryAsync(predicate).GetAwaiter().GetResult().Select(id)), key + " queryAsync");
                Assert.AreEqual(selectWhere, WhereOf(log, "SELECT", key + " queryAsync"), key + ": QueryAsync sent a different WHERE");

                log.Clear();
                Assert.AreEqual(expected, Ids(provider.Query<T>().Where(predicate).ToList().Select(id)), key + " linq");
                Assert.AreEqual(selectWhere, WhereOf(log, "SELECT", key + " linq"), key + ": Query<T>().Where sent a different WHERE");
                Assert.AreEqual(selectParameters, ParametersOf(log), key + ": Query<T>().Where sent different parameters");

                log.Clear();
                Assert.AreEqual(expectedCount, provider.Query<T>().Count(predicate), key + " count");
                Assert.AreEqual(selectWhere, WhereOf(log, "SELECT", key + " count"), key + ": Count(predicate) sent a different WHERE");

                Assert.AreEqual(expectedCount > 0, provider.Query<T>().Any(predicate), key + " any");

                foreach (var async in new[] { false, true })
                    AssertDelete(key + (async ? " deleteAsync" : " delete"), provider, log, predicate, async, rejection, expected, selectWhere, selectParameters);
            });
        }

        private static void AssertDelete<T>(string path, OrmDataProvider provider, List<string> log, Expression<Func<T, bool>> predicate,
            bool async, string rejection, string expected, string selectWhere, string selectParameters) where T : class, new()
        {
            var transactional = (ISqlOrmProvider)provider;
            transactional.BeginTransaction();
            Exception thrown = null;
            string deleted = null, deleteWhere = null, deleteParameters = null;
            var deleteSent = false;
            try
            {
                log.Clear();
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
                deleteSent = log.Any(e => e != null && Regex.IsMatch(e, @"^\s*DELETE\b", RegexOptions.IgnoreCase));
                if (thrown == null)
                {
                    deleteWhere = WhereOf(log, "DELETE", path);
                    deleteParameters = ParametersOf(log);
                    var remaining = Remaining(provider);
                    deleted = Ids(Rows.Select(r => r.Id).Where(id => !remaining.Contains(id)));
                }
            }
            finally
            {
                // A failed statement aborts a PostgreSQL transaction: roll back before anything else is read.
                transactional.RollbackTransaction();
            }

            if (rejection == null)
            {
                Assert.IsNull(thrown, path + ": Delete threw where Query returned rows: " + thrown?.GetType().Name + ": " + thrown?.Message);
                Assert.AreEqual(expected, deleted, path + ": Delete removed other rows than Query selects");
                Assert.AreEqual(selectWhere, deleteWhere, path + ": the DELETE's WHERE text differs from the SELECT's");
                Assert.AreEqual(selectParameters, deleteParameters, path + ": the DELETE's parameters differ from the SELECT's");
            }
            else
            {
                Assert.IsNotNull(thrown, path + ": expected the all-rows guard to refuse this delete");
                Assert.AreEqual(typeof(InvalidOperationException), thrown.GetType(), path + ": " + thrown.Message);
                Assert.AreEqual(rejection, thrown.Message, path);
                Assert.IsFalse(deleteSent, path + ": a DELETE was sent");
                Assert.AreEqual(Rows.Length, Remaining(provider).Count, path + ": rows were deleted");
            }
        }

        /// <summary>
        /// Runs <paramref name="predicate"/> for the captured values its translation reads (AC-N13). Where the database
        /// rejects the shape on this provider before and after 3.12.0 (<see cref="RejectedByTheDialect"/>), the query
        /// must fail there, and the reads still count.
        /// </summary>
        private void QueryForReads(string key, OrmDataProvider provider, Expression<Func<NsRow, bool>> predicate)
        {
            if (!RejectedByTheDialect(key))
            {
                provider.Query(predicate);
                return;
            }
            Exception thrown = null;
            try { provider.Query(predicate); } catch (Exception ex) { thrown = ex; }
            Assert.IsInstanceOfType(thrown, typeof(System.Data.Common.DbException),
                key + ": expected the database to reject this shape, got " + (thrown == null ? "rows" : thrown.GetType().Name + ": " + thrown.Message));
        }

        private static void AssertDialectRejects<T>(string key, OrmDataProvider provider, Expression<Func<T, bool>> predicate) where T : class, new()
        {
            var calls = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("query", () => provider.Query(predicate)),
                new KeyValuePair<string, Action>("linq", () => provider.Query<T>().Where(predicate).ToList()),
                new KeyValuePair<string, Action>("count", () => provider.Query<T>().Count(predicate)),
            };
            foreach (var call in calls)
            {
                Exception thrown = null;
                try { call.Value(); } catch (Exception ex) { thrown = ex; }
                Assert.IsInstanceOfType(thrown, typeof(System.Data.Common.DbException),
                    key + " " + call.Key + ": expected the database to reject this shape, got " + (thrown == null ? "rows" : thrown.GetType().Name + ": " + thrown.Message));
            }
            var transactional = (ISqlOrmProvider)provider;
            transactional.BeginTransaction();
            Exception deleteThrown = null;
            try { provider.Delete(predicate); } catch (Exception ex) { deleteThrown = ex; }
            finally { transactional.RollbackTransaction(); }
            Assert.IsInstanceOfType(deleteThrown, typeof(System.Data.Common.DbException), key + " delete: expected the database to reject this shape");
        }

        /// <summary>AC-N9: All answers as C# (EF) does, and its WHERE goes through the same translation as Query.</summary>
        [DataTestMethod]
        [DataRow("allNGt0", false)]
        [DataRow("allNameContainsNull", false)]
        [DataRow("allNIn517", false)]
        [DataRow("allNNe5", false)]
        [DataRow("allReqNeZz", true)]
        [DataRow("allNotNameContainsNull", true)]
        [DataRow("firstTwoAllNameContainsA", false)]
        public void All_AnswersAsTheOracle(string key, bool expected)
        {
            WithTables((provider, log) =>
            {
                bool actual;
                switch (key)
                {
                    case "allNGt0": actual = provider.Query<NsRow>().All(x => x.N > 0); break;
                    case "allNameContainsNull": actual = provider.Query<NsRow>().All(x => x.Name.Contains(noName)); break;
                    case "allNIn517": actual = provider.Query<NsRow>().All(x => x.N == 5 || x.N == 7 || x.N == 1); break;
                    case "allNNe5": actual = provider.Query<NsRow>().All(x => x.N != 5); break;
                    case "allReqNeZz": actual = provider.Query<NsRow>().All(x => x.Req != "zz"); break;
                    case "allNotNameContainsNull": actual = provider.Query<NsRow>().All(x => !x.Name.Contains(noName)); break;
                    case "firstTwoAllNameContainsA": actual = provider.Query<NsRow>().Where(x => x.Id <= 2).All(x => x.Name.Contains(a)); break;
                    default: throw new ArgumentException(key);
                }
                Assert.AreEqual(expected, actual, key);
            });
        }

        /// <summary>
        /// AC-N11: where C# and SQL already agree, the WHERE text and parameters are exactly 3.11.0's (captured from
        /// 25956e0 into the provider's <see cref="SqlOf3110"/>). The listed exception (a conditional's test that doesn't
        /// read the row) is pinned to its new literal form by <c>condCapturedTest</c>/<c>condFoldedTest</c> after 3.12.0.
        /// </summary>
        [DataTestMethod]
        [DataRow("idNe5")] [DataRow("idEq5")] [DataRow("notIdEq5")] [DataRow("notCodeGt5")] [DataRow("notIntIdsContainsId")]
        [DataRow("nameEqA")] [DataRow("nEq5")] [DataRow("nameContainsA")] [DataRow("nEqNullLiteral")] [DataRow("nGt5")] [DataRow("condNe")]
        [DataRow("capturedNEqV")] [DataRow("codeNeCapturedShort")] [DataRow("longNeCapturedInt")] [DataRow("priceNeCapturedInt")]
        [DataRow("notIdEqCapturedV")] [DataRow("idNeCapturedNullableFive")] [DataRow("notIdAndCodeEq5")]
        [DataRow("doubleNot")] [DataRow("notNNeV")] [DataRow("nEqCode")] [DataRow("idsContainsN")]
        public void NonNullableShapes_SendTheSqlOf3110(string key)
        {
            var predicate = Predicate(key);
            WithTables((provider, log) =>
            {
                if (AssertedProviderRejection(key, () => provider.Query(predicate)))
                    return;
                log.Clear();
                provider.Query(predicate);
                var parameters = ParametersOf(log);
                var actual = WhereOf(log, "SELECT", key) + " ||" + (parameters.Length > 0 ? " " + parameters : string.Empty);
                var pinned = SqlOf3110(key);
                Assert.IsNotNull(pinned, key + ": not captured yet. Captured: " + actual);
                Assert.AreEqual(pinned, actual, key + ": the SQL differs from 3.11.0's");
            });
        }

        /// <summary>
        /// AC-N1 (R3-3): on a column that can be NULL, a captured value under a <c>Convert</c> keeps 3.11.0's comparison and
        /// binding exactly, and gains <c>OR col IS NULL</c>. The base text and parameters are 3.11.0's, captured from
        /// 25956e0 into <see cref="SqlOf3110"/>.
        /// </summary>
        [DataTestMethod]
        [DataRow("nNeCapturedShort", "n")]
        [DataRow("nLongNeCapturedInt", "n_long")]
        [DataRow("nPriceNeCapturedInt", "n_price")]
        [DataRow("colorNeCapturedColor", "e")]
        [DataRow("eNeGreenLiteral", "e")]
        public void ConvertTwins_SendTheComparisonOf3110PlusIsNull(string key, string column)
        {
            var predicate = Predicate(key);
            WithTables((provider, log) =>
            {
                if (AssertedProviderRejection(key, () => provider.Query(predicate)))
                    return;
                log.Clear();
                provider.Query(predicate);
                var where = WhereOf(log, "SELECT", key);
                var parameters = ParametersOf(log);
                var actual = where + " ||" + (parameters.Length > 0 ? " " + parameters : string.Empty);
                var baseline = SqlOf3110(key);
                Assert.IsNotNull(baseline, key + ": not captured yet. Captured: " + actual);
                var split = baseline.IndexOf(" ||", StringComparison.Ordinal);
                var expected = "(" + baseline.Substring(0, split) + " OR " + RowTable + "." + column + " IS NULL)" + baseline.Substring(split);
                Assert.AreEqual(expected, actual, key + ": 3.11.0's comparison and binding, plus OR " + column + " IS NULL");
            });
        }

        /// <summary>
        /// AC-N11 exception (a): a conditional's test that doesn't read the row is a literal, and two strings there compare
        /// as C# compares them (ordinal), whatever the column collation.
        /// </summary>
        [DataTestMethod]
        [DataRow("condCapturedTest", "CASE WHEN NOT 1=0 THEN")]
        [DataRow("condCollationTest", "CASE WHEN 1=0 THEN")]
        [DataRow("condCapturedBoolTest", "CASE WHEN NOT 1=0 THEN")]
        [DataRow("boolBranchCapturedLiteral", "THEN NOT 1=0 ELSE")]
        public void ConditionalTestsThatDontReadTheRow_AreLiterals(string key, string fragment)
        {
            var predicate = Predicate(key);
            var expected = Expected(key);
            WithTables((provider, log) =>
            {
                if (RejectedByTheDialect(key))
                {
                    AssertDialectRejects(key, provider, predicate);
                    return;
                }
                log.Clear();
                Assert.AreEqual(expected, Ids(provider.Query(predicate).Select(r => r.Id)), key + " query");
                StringAssert.Contains(WhereOf(log, "SELECT", key), fragment, key);
            });
        }

        /// <summary>AC-N11 exception (c): All sends the translation of <c>!p</c>, not <c>NOT (p)</c> around p's text.</summary>
        [TestMethod]
        public void All_SendsTheTranslationOfNotP()
        {
            WithTables((provider, log) =>
            {
                log.Clear();
                Assert.IsFalse(provider.Query<NsRow>().All(x => x.Id > 5), "All(x => x.Id > 5)");
                var select = log.FirstOrDefault(e => e != null && Regex.IsMatch(e, @"^\s*SELECT\b", RegexOptions.IgnoreCase));
                Assert.IsNotNull(select, "no SELECT logged");
                var text = Regex.Replace(select, @"\s+", " ");
                StringAssert.Contains(text, "NOT " + RowTable + ".id > ", "All(x => x.Id > 5): the WHERE is the translation of !p");
                Assert.IsFalse(text.Contains("NOT (" + RowTable + ".id"), "All no longer wraps p's text in NOT (…): " + text);
            });
        }

        /// <summary>AC-N6 (§3): a cast collection item stays rejected, with the same exception and message on every path.</summary>
        [DataTestMethod]
        [DataRow("castItemContains")]
        public void Untranslatable_IsRejectedIdentically(string key)
        {
            var predicate = Predicate(key);
            WithTables((provider, log) =>
            {
                var calls = new List<KeyValuePair<string, Action>>
                {
                    new KeyValuePair<string, Action>("query", () => provider.Query(predicate)),
                    new KeyValuePair<string, Action>("queryAsync", () => provider.QueryAsync(predicate).GetAwaiter().GetResult()),
                    new KeyValuePair<string, Action>("linq", () => provider.Query<NsRow>().Where(predicate).ToList()),
                    new KeyValuePair<string, Action>("count", () => provider.Query<NsRow>().Count(predicate)),
                    new KeyValuePair<string, Action>("delete", () => provider.Delete(predicate)),
                };
                string firstMessage = null;
                foreach (var call in calls)
                {
                    var transactional = (ISqlOrmProvider)provider;
                    var isDelete = call.Key == "delete";
                    if (isDelete)
                        transactional.BeginTransaction();
                    Exception thrown = null;
                    try
                    {
                        log.Clear();
                        try { call.Value(); } catch (Exception ex) { thrown = ex; }
                    }
                    finally
                    {
                        if (isDelete)
                            transactional.RollbackTransaction();
                    }
                    Assert.IsInstanceOfType(thrown, typeof(NotSupportedException),
                        key + " " + call.Key + ": expected NotSupportedException, got " + (thrown == null ? "no exception" : thrown.GetType().Name + ": " + thrown.Message));
                    firstMessage = firstMessage ?? thrown.Message;
                    Assert.AreEqual(firstMessage, thrown.Message, key + " " + call.Key + ": the paths reject with different messages");
                    Assert.IsFalse(log.Any(e => e != null && Regex.IsMatch(e, @"^\s*(SELECT|DELETE)\b", RegexOptions.IgnoreCase)), key + " " + call.Key + ": a command was sent");
                }
                Assert.AreEqual(Rows.Length, Remaining(provider).Count, key + ": rows were deleted");
            });
        }

        /// <summary>AC-N1 through the attributes (T0-8, R2-5): computed columns, inline in the SQL, so every path including Delete runs.</summary>
        [DataTestMethod]
        [DataRow("docKNe5")]
        [DataRow("nPlusOneIntNe6")]
        [DataRow("childSumDNe99")]
        [DataRow("childCountNe1")]
        // T8E2-4, the owner's ruling: a non-nullable Sum or Avg compared as 3.11.0 compared it (a parent with no children is
        // the SQL NULL, so `== 0` leaves it out: a Known issue), and a nullable Sum, whose NULL compares as C#'s null does
        [DataRow("childIdSumNe0")]
        [DataRow("childIdSumEq0")]
        [DataRow("childIdSumEq3")]
        // T8H-3: the Known issue's rows under the two-valued CASE (the owner's ruling, 2026-10-24): a parent with no child
        // rows makes the predicate false there
        [DataRow("activeNeChildIdSumEq0Pin")]
        [DataRow("condChildIdSumEq0EqFalsePin")]
        // T8I-1, T8I-3: a bool value against the predicate is p or its NOT, the NULL left unknown; a distributed leaf is
        // two-valued
        [DataRow("childIdSumEq5EqFalse")]
        [DataRow("nestedCondChildIdSumNe5Pin")]
        [DataRow("childIdSumNNe0")]
        [DataRow("childAvgDNe0")]
        [DataRow("docKEqNone")]
        [DataRow("computedCondWitnessNeTrue")]
        // R11-1: a round trip through a nullable type keeps the operand's kind (a property can be NULL whatever its CLR type)
        [DataRow("nPlusOneIntRoundTripNe6")]
        [DataRow("nPlusOneIntObjectNe6")]
        [DataRow("childSumDRoundTripNe99")]
        // R11-4: a re-decided == (the witness's only nullable branch folds) beside a column that can be null
        [DataRow("flagEqCondWitnessFolds")]
        // R12-1: a column compared with its [SqlExpression] alias, under a comparison with a value (§4.3)
        [DataRow("activeEqActiveXNeFalse")]
        // R14-3: a column that can be null compared with its alias, whose text is EF's form of n = n: refused, as 3.11.0
        // refused `n = n` (and its `NOT n != n`, which 3.11.0 accepted: R15-4)
        [DataRow("nEqNX")]
        [DataRow("notNNeNX")]
        // R15-2: a part of a column compared with the same part of its alias: refused as 3.11.0 refused its text; R15-4: a
        // NULL test of a column the comparison doesn't read beside a column against its alias, which 3.11.0 accepted, the
        // text check accepts too, and since rev 18 the verdict refuses it as always true, as its column form (R17-2); R15-3: a
        // CASE in a template
        [DataRow("nameLengthEqNameXLength")]
        [DataRow("nameXLengthEqNameLength")]
        [DataRow("dYearEqDXYear")]
        [DataRow("activeEqActiveXOrFlagNull")]
        [DataRow("nestedCondNeFlat8CodeCase")]
        // R16-5: always true in C#, accepted by 3.11.0, refused since R14-3's check: a part of a column against its alias
        // under a `!` (3.11.0 deleted the rows with a name), and 3.11.0's own `(A = A OR n IS NULL)` where A reads n
        // (3.11.0 deleted every row); R16-3: an alias in a chain's innermost test
        [DataRow("notNameLengthNeNameXLength")]
        [DataRow("nToStringEqNXToStringOrNNull")]
        [DataRow("nxChainInTest7")]
        // R17-2: a nested conditional compared with its own copy, or its alias's: distributed, so the verdict decides it
        // (3.11.0 accepted both, its text giving each copy's constants their own parameters; rev 17 deleted every row);
        // R17-1: a template whose SQL holds `IS NULL AND`, against its alias
        [DataRow("nestedCondSelfCopy")]
        [DataRow("nestedCondAliasCopy")]
        [DataRow("nBothEqNBothX")]
        // R18-1: a nested conditional against its twin only the provider's text shows (a template in another letter case,
        // a [JsonPath] on the same path): distributed, so the verdict asks the provider's operand SQL (3.11.0 refused both;
        // rev 18 deleted every row); a neighbour against another template runs
        [DataRow("nestedNAbsUpperEqNAbsLower")]
        [DataRow("nestedDocKEqDocK2")]
        [DataRow("nestedNAbsUpperEqCodeCase")]
        public void Computed_EveryPath_SelectsTheOraclesRows(string key)
        {
            Expression<Func<NsComputed, bool>> predicate;
            Func<SeedRow, bool> oracle;
            switch (key)
            {
                case "docKNe5": predicate = x => x.DocK != "5"; oracle = r => JsonK(r) != "5"; break;
                case "nPlusOneIntNe6": predicate = x => x.NPlusOneInt != 6; oracle = r => r.N == null || r.N.Value + 1 != 6; break;
                // 99 is no row's sum: SQLite binds a decimal as text, which a SUM (no column affinity) compares as text, so an
                // equal sum would be selected there before and after (outside this plan; plan §3).
                // A non-nullable aggregate's NULL isn't compensated (the owner's ruling, as 3.11.0): SQL's comparison, NULL unknown.
                case "childSumDNe99": predicate = x => x.ChildSumD != 99m; oracle = r => ChildSum(r) != null && ChildSum(r) != 99m; break;
                case "childIdSumNe0": predicate = x => x.ChildIdSum != 0; oracle = r => ChildIdSumN(r) != null && ChildIdSumN(r) != 0; break;
                case "childIdSumEq0": predicate = x => x.ChildIdSum == 0; oracle = r => ChildIdSumN(r) != null && ChildIdSumN(r) == 0; break;
                // T8G-6: a sum some parents have (1 and 5), so a translation that selects nothing can't pass.
                case "childIdSumEq3": predicate = x => x.ChildIdSum == 3; oracle = r => ChildIdSumN(r) == 3; break;
                // T8H-3, a Known issue pinned: the two-valued CASE takes the SQL NULL's unknown as false, which C#'s lifted
                // null == 0 also gives, so the oracle compares the SQL value (C# over the rows read back, 0, differs).
                case "activeNeChildIdSumEq0Pin": predicate = x => x.Active != (x.ChildIdSum == 0); oracle = r => r.Active != (ChildIdSumN(r) == 0); break;
                case "condChildIdSumEq0EqFalsePin":
                    predicate = x => (x.Id > 2 ? x.ChildIdSum == 0 : x.Active) == false;
                    oracle = r => (r.Id > 2 ? ChildIdSumN(r) == 0 : r.Active) == false;
                    break;
                // T8I-1: compared with a bool value the predicate is sent as itself or its NOT (AC-N14), so a parent with no
                // child rows is left out, as by `x.ChildIdSum != 5`; C# over the rows read back (0) selects it.
                case "childIdSumEq5EqFalse": predicate = x => (x.ChildIdSum == 5) == false; oracle = r => ChildIdSumN(r) != null && ChildIdSumN(r) != 5; break;
                // T8I-3: a distributed comparison's leaf over the non-nullable Sum is its two-valued CASE (R12-2): a parent with
                // no child rows on that branch is left out, 3.11.0's rows (the owner's ruling); C# over the rows read back (0) differs.
                case "nestedCondChildIdSumNe5Pin":
                    predicate = x => ((x.Id > 2 ? x.Code : x.Id) > 3 ? x.ChildIdSum : x.N) != 5;
                    oracle = r => (r.Id > 2 ? r.Code : r.Id) > 3 ? ChildIdSumN(r) != null && ChildIdSumN(r) != 5 : r.N != 5;
                    break;
                case "childIdSumNNe0": predicate = x => x.ChildIdSumN != 0; oracle = r => ChildIdSumN(r) != 0; break;
                case "childAvgDNe0": predicate = x => x.ChildAvgD != 0m; oracle = r => ChildAvgD(r) != null && ChildAvgD(r) != 0m; break;
                case "childCountNe1": predicate = x => x.ChildCount != 1; oracle = r => Children.Count(c => c.RowId == r.Id) != 1; break;
                // A JSON null reads as the text 'null' on MySQL (a Known issue): JsonKeyMatchesOnThisProvider has its answer.
                case "docKEqNone": predicate = x => x.DocK == noName; oracle = r => JsonK(r) == null; break;
                // R10-2: the witness's only branch that can be NULL (ActiveX) folds away once the test is rewritten.
                case "computedCondWitnessNeTrue":
                    predicate = x => (x.Id > 2 ? (x.Id >= none ? x.ActiveX : true) : x.Active) != true;
                    oracle = r => r.Id <= 2 && !r.Active;
                    break;
                case "nPlusOneIntRoundTripNe6": predicate = x => (int)(int?)x.NPlusOneInt != 6; oracle = r => r.N == null || r.N.Value + 1 != 6; break;
                case "nPlusOneIntObjectNe6": predicate = x => (int)(object)x.NPlusOneInt != 6; oracle = r => r.N == null || r.N.Value + 1 != 6; break;
                case "childSumDRoundTripNe99": predicate = x => (decimal)(decimal?)x.ChildSumD != 99m; oracle = r => ChildSum(r) != null && ChildSum(r) != 99m; break;
                case "flagEqCondWitnessFolds":
                    predicate = x => x.Flag == (x.Id > 2 ? (x.Id >= none ? x.ActiveX : true) : x.Active);
                    oracle = r => r.Id > 2 ? r.Flag == true : r.Flag == r.Active;
                    break;
                // R12-1, §4.3: `p != false` is p, whose text compares the column with itself through the [SqlExpression]
                // alias (`active = active`), refused as self-referencing; 3.11.0 sent `… != @p` and deleted every row.
                case "activeEqActiveXNeFalse": predicate = x => (x.Active == x.ActiveX) != false; oracle = r => true; break;
                case "nEqNX": predicate = x => x.N == x.NX; oracle = r => true; break;
                // R15-4: 3.11.0 sent `NOT n != n` and deleted the rows whose n isn't NULL; refused now, a self-reference.
                case "notNNeNX": predicate = x => !(x.N != x.NX); oracle = r => true; break;
                case "nameLengthEqNameXLength": predicate = x => x.Name.Length == x.NameX.Length; oracle = r => true; break;
                case "nameXLengthEqNameLength": predicate = x => x.NameX.Length == x.Name.Length; oracle = r => true; break;
                case "dYearEqDXYear": predicate = x => x.D.Value.Year == x.DX.Value.Year; oracle = r => true; break;
                case "activeEqActiveXOrFlagNull": predicate = x => x.Active == x.ActiveX || x.Flag == null; oracle = r => true; break;
                case "notNameLengthNeNameXLength": predicate = x => !(x.Name.Length != x.NameX.Length); oracle = r => true; break;
                case "nToStringEqNXToStringOrNNull": predicate = x => x.N.ToString() == x.NX.ToString() || x.N == null; oracle = r => true; break;
                case "nestedCondSelfCopy":
                    predicate = x => ((x.Id > 2 ? x.N : (int?)x.Code) != 5 ? x.N : (int?)x.Code) == ((x.Id > 2 ? x.N : (int?)x.Code) != 5 ? x.N : (int?)x.Code);
                    oracle = r => true;
                    break;
                case "nestedCondAliasCopy":
                    predicate = x => ((x.Id > 2 ? x.N : (int?)x.Code) != 5 ? x.N : (int?)x.Code) == ((x.Id > 2 ? x.NX : (int?)x.Code) != 5 ? x.NX : (int?)x.Code);
                    oracle = r => true;
                    break;
                case "nBothEqNBothX": predicate = x => x.NBoth == x.NBothX; oracle = r => true; break;
                case "nestedNAbsUpperEqNAbsLower":
                    predicate = x => ((x.Id > 2 ? x.N : (int?)x.Code) == x.Code ? x.NAbsUpper : x.N) == ((x.Id > 2 ? x.N : (int?)x.Code) == x.Code ? x.NAbsLower : x.N);
                    oracle = r => true;
                    break;
                case "nestedDocKEqDocK2":
                    predicate = x => ((x.Id > 2 ? x.N : (int?)x.Code) == x.Code ? x.DocK : x.Name) == ((x.Id > 2 ? x.N : (int?)x.Code) == x.Code ? x.DocK2 : x.Name);
                    oracle = r => true;
                    break;
                case "nestedNAbsUpperEqCodeCase":
                    {
                        Expression<Func<NsComputed, bool>> neighbour = x =>
                            ((x.Id > 2 ? x.N : (int?)x.Code) == x.Code ? x.NAbsUpper : x.N) == ((x.Id > 2 ? x.N : (int?)x.Code) == x.Code ? x.CodeCase : x.N);
                        var compiled = neighbour.Compile();
                        predicate = neighbour;
                        oracle = r => compiled(new NsComputed
                        {
                            Id = r.Id, N = r.N, Code = r.Code,
                            NAbsUpper = r.N == null ? (int?)null : Math.Abs(r.N.Value), CodeCase = r.Code > 5 ? r.Code : 0,
                        });
                        break;
                    }
                case "nxChainInTest7":
                    {
                        var chain = ChainNXInTest(7);
                        var compiled = chain.Compile();
                        predicate = chain;
                        oracle = r => compiled(new NsComputed { Id = r.Id, N = r.N, NX = r.N, Code = r.Code });
                        break;
                    }
                case "nestedCondNeFlat8CodeCase":
                    {
                        var nested = NestedAgainstCodeCase();
                        var compiled = nested.Compile();
                        predicate = nested;
                        oracle = r => compiled(new NsComputed { Id = r.Id, N = r.N, NX = r.N, Code = r.Code, CodeCase = r.Code > 5 ? r.Code : 0 });
                        break;
                    }
                default: throw new ArgumentException(key);
            }
            // Every path, as for NsRow (AC-N10; R4-12): Query, QueryAsync, Where (text and parameters), Count, Any, Delete, DeleteAsync.
            EveryPath(key, predicate, Ids(Rows.Where(r => JsonKeyMatchesOnThisProvider(key, r) ?? oracle(r)).Select(r => r.Id)), r => r.Id);
        }

        /// <summary>The JSON key's value: null when absent or a JSON null.</summary>
        private static string JsonK(SeedRow r)
        {
            if (r.Doc == null)
                return null;
            var match = Regex.Match(r.Doc, "\"k\":\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        // An aggregate's SQL value: NULL over no child rows.
        private static decimal? ChildSum(SeedRow r) =>
            Children.Any(c => c.RowId == r.Id) ? Children.Where(c => c.RowId == r.Id).Sum(c => c.Amount) : (decimal?)null;

        private static int? ChildIdSumN(SeedRow r) =>
            Children.Any(c => c.RowId == r.Id) ? Children.Where(c => c.RowId == r.Id).Sum(c => c.Id) : (int?)null;

        private static decimal? ChildAvgD(SeedRow r) =>
            Children.Any(c => c.RowId == r.Id) ? Children.Where(c => c.RowId == r.Id).Average(c => c.Amount) : (decimal?)null;

        /// <summary>A provider's own answer for a JSON-null row where it differs from EF's (MySQL reads a JSON null as the text 'null'); null otherwise.</summary>
        protected virtual bool? JsonKeyMatchesOnThisProvider(string key, SeedRow row) => null;

        /// <summary>AC-N1/N6 through the remote attributes (a LEFT JOIN): the reads; a DELETE has no JOIN (unchanged, a 3.11.0 Known issue).</summary>
        [DataTestMethod]
        [DataRow("parentLabelNeP")]
        [DataRow("parentCodeNe5")]
        [DataRow("parentKeyNe1")]
        [DataRow("parentCodeEq0")]
        [DataRow("parentCodeToStringNe5")]
        [DataRow("parentCodeToStringEqEmpty")]
        [DataRow("notParentKeysContainsParentKey")]
        // R11-1: a round trip through a nullable type keeps a remote column's kind
        [DataRow("parentCodeRoundTripNe5")]
        [DataRow("parentKeyRoundTripNe1")]
        [DataRow("notParentCodeRoundTripGt3")]
        public void Remote_ReadsSelectTheOraclesRows(string key)
        {
            Expression<Func<NsRemote, bool>> predicate;
            Func<NsParent, bool> oracle; // the parent, or null when the row has none (EF: a missing parent is NULL)
            switch (key)
            {
                case "parentLabelNeP": predicate = x => x.ParentLabel != "p"; oracle = p => p == null || p.Label != "p"; break;
                case "parentCodeNe5": predicate = x => x.ParentCode != 5; oracle = p => p == null || p.PCode != 5; break;
                case "parentKeyNe1": predicate = x => x.ParentKey != 1; oracle = p => p == null || p.Id != 1; break;
                case "parentCodeEq0": predicate = x => x.ParentCode == 0; oracle = p => p != null && p.PCode == 0; break;
                case "parentCodeToStringNe5": predicate = x => x.ParentCode.ToString() != "5"; oracle = p => p == null || p.PCode.ToString(CultureInfo.InvariantCulture) != "5"; break;
                // EF's text of a missing parent's integer is '' (COALESCE), which a bare CAST (3.11.0) leaves NULL (R4-10).
                case "parentCodeToStringEqEmpty": predicate = x => x.ParentCode.ToString() == ""; oracle = p => p == null; break;
                case "notParentKeysContainsParentKey": predicate = x => !parentKeys.Contains(x.ParentKey); oracle = p => p == null || !parentKeys.Contains(p.Id); break;
                case "parentCodeRoundTripNe5": predicate = x => (int)(int?)x.ParentCode != 5; oracle = p => p == null || p.PCode != 5; break;
                case "parentKeyRoundTripNe1": predicate = x => (int)(int?)x.ParentKey != 1; oracle = p => p == null || p.Id != 1; break;
                case "notParentCodeRoundTripGt3": predicate = x => !((int)(int?)x.ParentCode > 3); oracle = p => p == null || !(p.PCode > 3); break;
                default: throw new ArgumentException(key);
            }
            var expected = Ids(Rows.Where(r => oracle(Parents.FirstOrDefault(p => p.Id == r.ParentId))).Select(r => r.Id));
            var expectedCount = expected.Length == 0 ? 0 : expected.Split(',').Length;
            WithTables((provider, log) =>
            {
                log.Clear();
                Assert.AreEqual(expected, Ids(provider.Query(predicate).Select(r => r.Id)), key + " query");
                var selectWhere = WhereOf(log, "SELECT", key + " query");
                log.Clear();
                Assert.AreEqual(expected, Ids(provider.QueryAsync(predicate).GetAwaiter().GetResult().Select(r => r.Id)), key + " queryAsync");
                Assert.AreEqual(selectWhere, WhereOf(log, "SELECT", key + " queryAsync"), key + ": QueryAsync sent a different WHERE");
                log.Clear();
                Assert.AreEqual(expected, Ids(provider.Query<NsRemote>().Where(predicate).ToList().Select(r => r.Id)), key + " linq");
                Assert.AreEqual(selectWhere, WhereOf(log, "SELECT", key + " linq"), key + ": Where sent a different WHERE");
                Assert.AreEqual(expectedCount, provider.Query<NsRemote>().Count(predicate), key + " count");
                Assert.AreEqual(expectedCount > 0, provider.Query<NsRemote>().Any(predicate), key + " any");
            });
        }

        /// <summary>
        /// §6.1: a DELETE has no JOIN, so a remote attribute in its WHERE names an alias the statement doesn't have, and the
        /// database rejects it, one kind each (unchanged, a 3.11.0 Known issue); nothing is deleted.
        /// </summary>
        [DataTestMethod]
        [DataRow("RemoteProperty")]
        [DataRow("RemoteKey")]
        public void Remote_Delete_IsTheDatabasesUnknownColumnError(string kind)
        {
            Expression<Func<NsRemote, bool>> predicate;
            if (kind == "RemoteProperty")
                predicate = x => x.ParentLabel != "p";
            else
                predicate = x => x.ParentKey != 1;
            WithTables((provider, log) =>
            {
                var transactional = (ISqlOrmProvider)provider;
                transactional.BeginTransaction();
                Exception thrown = null;
                try { provider.Delete(predicate); } catch (Exception ex) { thrown = ex; }
                finally { transactional.RollbackTransaction(); }
                Assert.IsInstanceOfType(thrown, typeof(System.Data.Common.DbException),
                    kind + ": expected the database's error, got " + (thrown == null ? "no exception" : thrown.GetType().Name + ": " + thrown.Message));
                Assert.AreEqual(Rows.Length, Remaining(provider).Count, kind + ": rows were deleted");
            });
        }

        /// <summary>
        /// §4.3's reverse-join row: Count and All over a reverse (one-to-many) join refuse a filter that names the join
        /// (fan-out). One whose only remote comparison folds away (a null value: <c>1=0</c>) needs no join, and runs.
        /// </summary>
        [TestMethod]
        public void ReverseJoin_AFilterThatFoldsAway_NeedsNoJoin()
        {
            WithTables((provider, log) =>
            {
                Assert.AreEqual(0, provider.Query<NsReverse>().Count(x => x.ReverseRowId > none), "Count(x => x.ReverseRowId > none)");
                Assert.IsFalse(provider.Query<NsReverse>().All(x => x.ReverseRowId > none), "All(x => x.ReverseRowId > none)");
                // A filter that names the join is refused, as before.
                Assert.ThrowsException<NotSupportedException>(() => provider.Query<NsReverse>().Count(x => x.ReverseRowId > 1), "Count(x => x.ReverseRowId > 1)");
            });
        }

        /// <summary>
        /// AC-N10's third bullet: the delete verdicts that change in 3.12.0, plan §4.3, one row per shape. EveryPath also
        /// asserts each key's delete; this pins the table itself, so a verdict can't move without the table moving.
        /// </summary>
        [DataTestMethod]
        [DataRow("notNameContainsNull", true)] [DataRow("notNameStartsWithNull", true)] [DataRow("notNameEndsWithNull", true)]
        [DataRow("idOrNotNameContainsNull", true)]
        [DataRow("notAndContainsNull", true)] [DataRow("notIdAndNameContainsNull", true)]
        [DataRow("nameContainsNullEqFalse", true)] [DataRow("nameContainsNullNeTrue", true)] [DataRow("notNameContainsNullEqTrue", true)]
        [DataRow("notNGeNone", true)] [DataRow("notNGtNone", true)] [DataRow("notNLtNone", true)] [DataRow("notNLeNone", true)]
        [DataRow("idOrNotNGeNone", true)]
        [DataRow("idNeNoneInt", true)] [DataRow("notIdEqNone", true)]
        [DataRow("idNeNullLiteral", true)] [DataRow("notIdEqNullLiteral", true)]
        [DataRow("notOnlyNullContainsN", false)] [DataRow("idOrNotOnlyNullContainsN", false)] [DataRow("notIdAndOnlyNullContainsN", false)]
        [DataRow("reqNeNoName", false)] [DataRow("nEq5OrNNe5", false)]
        [DataRow("idEq2OrIdNeNoneInt", true)] [DataRow("nGtNoneCondCode", true)]
        [DataRow("nameContainsANeNullBool", true)] [DataRow("nEq5EqNEq5", true)] [DataRow("condSelfOrCode5", true)]
        [DataRow("notFlagEqNotFlag", false)] [DataRow("castNotLiftedCondHybrid", true)]
        [DataRow("idEq2OrObjCodeSelf", true)] [DataRow("falseNeObjNameLengthSelfLe", true)]
        public void VerdictChanges_AsListed(string key, bool refused)
        {
            var rejection = ExpectedDeleteRejection(key);
            Assert.AreEqual(refused, rejection != null, key + ": ExpectedDeleteRejection disagrees with §4.3");
            var predicate = Predicate(key);
            var expected = Expected(key);
            WithTables((provider, log) =>
            {
                log.Clear();
                provider.Query(predicate);
                var selectWhere = WhereOf(log, "SELECT", key + " query");
                var selectParameters = ParametersOf(log);
                foreach (var async in new[] { false, true })
                    AssertDelete(key + (async ? " deleteAsync" : " delete"), provider, log, predicate, async, rejection, expected, selectWhere, selectParameters);
            });
        }

        /// <summary>
        /// R19-1, R20-1 (docs/plans/NULL_SEMANTICS_PLAN.md AC-N13): a delete, and a query, read a captured value no more
        /// often than 3.11.0's did, on every provider: the guard gives its verdict once, after translation (rev 19 gave it
        /// twice), and folds a cast through <c>object</c> around a part that reads the row without reading under it, where
        /// 3.11.0's verdict read nothing (rev 18 to rev 20 read each captured value it evaluated there, twice). The counts
        /// are 3.11.0's: at <c>25956e0</c> this test passes on SQLite and MySQL, so 3.11.0 reads no more, and round 21's
        /// count through each provider's visitor at <c>25956e0</c> gives exactly these; 3.11.0 sent some of these shapes to
        /// SQL Server and PostgreSQL as invalid SQL, which the Changelog lists as running now. The delete selects the
        /// query's rows.
        /// </summary>
        [DataTestMethod]
        [DataRow("idEq5AndT", 1, 3)]
        [DataRow("tAndIdEq5", 1, 3)]
        [DataRow("idEq5OrF", 1, 3)]
        [DataRow("flagNeT", 1, 1)]
        [DataRow("activeEqT", 1, 1)]
        [DataRow("nNeFive", 1, 1)]
        [DataRow("condTEq5", 1, 1)]
        [DataRow("nameContainsAEqT", 2, 3)]
        [DataRow("idGtTwoAndNEq5", 1, 1)]
        [DataRow("nEq5EqT", 1, 1)]
        [DataRow("flagEqNB", 1, 1)]
        [DataRow("objNBEqFlag", 2, 4)]
        [DataRow("objNTEqFlag", 2, 4)]
        [DataRow("idEq5AndObjT", 1, 3)]
        [DataRow("objFNeActive", 2, 4)]
        [DataRow("objNBNeFlagOrIdEq2", 2, 4)]
        [DataRow("idEq5AndObjActiveNeT", 1, 1)]
        public void Delete_ReadsACapturedValue_NoMoreOftenThan3110(string key, int queryReads3110, int deleteReads3110)
        {
            Func<ReadCounter, Expression<Func<NsRow, bool>>> make;
            switch (key)
            {
                case "idEq5AndT": make = c => x => x.Id == 5 && c.T; break;
                case "tAndIdEq5": make = c => x => c.T && x.Id == 5; break;
                case "idEq5OrF": make = c => x => x.Id == 5 || c.F; break;
                case "flagNeT": make = c => x => x.Flag != c.T; break;
                case "activeEqT": make = c => x => x.Active == c.T; break;
                case "nNeFive": make = c => x => x.N != c.Five; break;
                case "condTEq5": make = c => x => (c.T ? x.N : x.M) == 5; break;
                case "nameContainsAEqT": make = c => x => x.Name.Contains(c.A) == c.T; break;
                case "idGtTwoAndNEq5": make = c => x => x.Id > c.Two && x.N == 5; break;
                case "nEq5EqT": make = c => x => (x.N == 5) == c.T; break;
                case "flagEqNB": make = c => x => x.Flag == c.NB; break;
                // Casts through object (R20-1): around a captured value, and around a part that reads the row.
                case "objNBEqFlag": make = c => x => (bool?)(object)c.NB == x.Flag; break;
                case "objNTEqFlag": make = c => x => (bool?)(object)c.NT == x.Flag; break;
                case "idEq5AndObjT": make = c => x => x.Id == 5 && (bool)(object)c.T; break;
                case "objFNeActive": make = c => x => (bool)(object)c.F != x.Active; break;
                case "objNBNeFlagOrIdEq2": make = c => x => (bool?)(object)c.NB != x.Flag || x.Id == 2; break;
                case "idEq5AndObjActiveNeT": make = c => x => x.Id == 5 && (bool)(object)(x.Active != c.T); break;
                default: throw new ArgumentException(key);
            }
            WithTables((provider, log) =>
            {
                var forQuery = new ReadCounter();
                var selected = provider.Query(make(forQuery)).Count;
                Assert.IsTrue(forQuery.Reads <= queryReads3110, key + ": the query read " + forQuery.Reads + " times; 3.11.0, " + queryReads3110);
                var forDelete = new ReadCounter();
                var transactional = (ISqlOrmProvider)provider;
                transactional.BeginTransaction();
                try
                {
                    Assert.AreEqual(selected, provider.Delete(make(forDelete)), key + ": the delete's rows");
                }
                finally { transactional.RollbackTransaction(); }
                Assert.IsTrue(forDelete.Reads <= deleteReads3110, key + ": the delete read " + forDelete.Reads + " times; 3.11.0, " + deleteReads3110);
            });
        }

        /// <summary>
        /// R19-1: the shared WHERE translation checks the argument contract before it discovers or translates anything, as
        /// 3.11.0's guard did, though the verdict now comes after translation: a null predicate is an
        /// <see cref="ArgumentNullException"/>, not a visitor's null reference, on every provider.
        /// </summary>
        [TestMethod]
        public void Query_OfANullPredicate_ThrowsArgumentNull()
        {
            WithTables((provider, log) =>
                Assert.ThrowsException<ArgumentNullException>(() => provider.Query((Expression<Func<NsRow, bool>>)null)));
        }

        /// <summary>
        /// AC-N13: the rewrite never reads a captured value more often than 3.11.0 does, and a WHERE's form agrees with the
        /// value it binds (a getter that alternates null and 5).
        /// </summary>
        [TestMethod]
        public void ReadOnce_CountingGetter()
        {
            WithTables((provider, log) =>
            {
                var c = new Holder();
                provider.Query<NsRow>(x => x.N != c.Five);
                Assert.AreEqual(1, c.FiveReads, "x.N != c.Five");

                c = new Holder();
                Assert.ThrowsException<NotSupportedException>(() => provider.Query<NsRow>(x => x.Child.Id != c.Five));
                Assert.AreEqual(0, c.FiveReads, "x.Child.Id != c.Five (rejected): the value isn't read");

                c = new Holder();
                Assert.ThrowsException<NotSupportedException>(() => provider.Query<NsRow>(x => c.Five != x.Child.Id));
                Assert.AreEqual(1, c.FiveReads, "c.Five != x.Child.Id (rejected): read once, as 3.11.0 does");

                c = new Holder();
                provider.Query<NsRow>(x => x.N == c.Five && x.M == 5);
                Assert.AreEqual(1, c.FiveReads, "x.N == c.Five && x.M == 5");

                // A non-null value against a column that can't be null: read once, though no rule changes the comparison.
                c = new Holder();
                provider.Query<NsRow>(x => x.Id != c.NullableFive);
                Assert.AreEqual(1, c.NullableFiveReads, "x.Id != c.NullableFive");

                // A conditional used as a witness is substituted before it is duplicated.
                c = new Holder();
                provider.Query<NsRow>(x => (x.Id > 2 ? x.N : c.Flip) != 5);
                Assert.AreEqual(1, c.FlipReads, "(x.Id > 2 ? x.N : c.Flip) != 5");

                // A literal null against a conditional: the test's captured value is read once, and bound (R5-2).
                c = new Holder();
                provider.Query<NsRow>(x => (x.Id > c.Five ? x.N : x.M) == null);
                Assert.AreEqual(1, c.FiveReads, "(x.Id > c.Five ? x.N : x.M) == null");

                // A conditional's test that folds a lifted comparison of captured bools: each read once (R5-3).
                c = new Holder();
                Assert.AreEqual("2,3,4", Ids(provider.Query<NsRow>(x => (((bool?)c.Flag == c.NullFlag) ? x.Id : x.Code) > 5).Select(r => r.Id)),
                    "(((bool?)c.Flag == c.NullFlag) ? x.Id : x.Code) > 5");
                Assert.AreEqual(1, c.FlagReads, "c.Flag in a folded test");
                Assert.AreEqual(1, c.NullFlagReads, "c.NullFlag in a folded test");

                // The fold's read of a captured bool is the one bound (3.11.0 read it twice: in the fold, then in the visitor).
                c = new Holder();
                provider.Query<NsRow>(x => c.Flag != x.Flag);
                Assert.AreEqual(1, c.FlagReads, "c.Flag != x.Flag");

                // AC-N14 duplicates both sides: their captured values are read once (R4-8).
                c = new Holder();
                provider.Query<NsRow>(x => (x.N == c.Five) == (x.M == 5));
                Assert.AreEqual(1, c.FiveReads, "(x.N == c.Five) == (x.M == 5)");

                c = new Holder();
                provider.Query<NsRow>(x => x.Name.Contains(a) == c.Flag);
                Assert.AreEqual(1, c.FlagReads, "x.Name.Contains(a) == c.Flag");

                // The expansion's form agrees with the value bound: Flip is null on its first read, so its side is n IS NULL.
                c = new Holder();
                Assert.AreEqual("2,3,5,6", Ids(provider.Query<NsRow>(x => (x.N == c.Flip) == (x.M == 5)).Select(r => r.Id)),
                    "(x.N == c.Flip) == (x.M == 5), Flip null on the first read");
                Assert.AreEqual(1, c.FlipReads, "(x.N == c.Flip) == (x.M == 5)");

                // The form agrees with the bound value: the first read is null, so the WHERE is IS NOT NULL and binds nothing.
                c = new Holder();
                log.Clear();
                var flipped = provider.Query<NsRow>(x => x.N != c.Flip).Select(r => r.Id).ToList();
                Assert.AreEqual(1, c.FlipReads, "x.N != c.Flip: read once");
                Assert.AreEqual(Ids(Rows.Where(r => r.N != null).Select(r => r.Id)), Ids(flipped), "x.N != c.Flip (null on the first read)");

                // R6-5: a conditional beside a predicate is substituted before AC-N14 duplicates it (3.11.0: one read)...
                c = new Holder();
                Assert.AreEqual("2,4,5,6", Ids(provider.Query<NsRow>(x => (x.Id > c.Five ? x.Active : x.Other) == (x.N == 5)).Select(r => r.Id)),
                    "(x.Id > c.Five ? x.Active : x.Other) == (x.N == 5)");
                Assert.AreEqual(1, c.FiveReads, "(x.Id > c.Five ? x.Active : x.Other) == (x.N == 5)");

                // ...and a lazy collection in its test is enumerated once.
                var counter = new CountingIds();
                IEnumerable<int?> lazy = counter;
                Assert.AreEqual("4,5,6", Ids(provider.Query<NsRow>(x => (lazy.Contains(x.M) ? x.Other : x.Active) == (x.N == 5)).Select(r => r.Id)),
                    "(lazy.Contains(x.M) ? x.Other : x.Active) == (x.N == 5)");
                Assert.AreEqual(1, counter.Enumerations, "(lazy.Contains(x.M) ? x.Other : x.Active) == (x.N == 5)");

                // A conditional that doesn't read the row is a value: its test is read once (3.11.0: twice).
                c = new Holder();
                Assert.AreEqual("1,2", Ids(provider.Query<NsRow>(x => (c.Five > 3 ? true : false) == (x.N == 5)).Select(r => r.Id)),
                    "(c.Five > 3 ? true : false) == (x.N == 5)");
                Assert.AreEqual(1, c.FiveReads, "(c.Five > 3 ? true : false) == (x.N == 5)");

                // `!` of a captured bool beside a predicate: read once (R6-2).
                c = new Holder();
                provider.Query<NsRow>(x => (x.N == 5) == !c.Flag);
                Assert.AreEqual(1, c.FlagReads, "(x.N == 5) == !c.Flag");

                // A branch whose truth the fold knows is that value: nothing under it is read (3.11.0's visitor folded it away).
                c = new Holder();
                provider.Query<NsRow>(x => (x.Id > 2 ? x.Flag : !(x.N > c.Five ? (bool?)true : (bool?)true)) != true);
                Assert.AreEqual(0, c.FiveReads, "(x.Id > 2 ? x.Flag : !(x.N > c.Five ? true : true)) != true");

                // R8-3: a captured value the fold never reaches (an || whose other operand is true) isn't read to be substituted.
                c = new Holder();
                provider.Query<NsRow>(x => (x.M == 5) == (x.Active != !((x.Name == c.A) || c.Flag)));
                Assert.AreEqual(0, c.AReads, "(x.M == 5) == (x.Active != !((x.Name == c.A) || c.Flag)): c.A");
                Assert.AreEqual(1, c.FlagReads, "(x.M == 5) == (x.Active != !((x.Name == c.A) || c.Flag)): c.Flag");

                // R7-2: `!` over a lifted predicate that reads no row, in a value branch: read once, as its value.
                c = new Holder();
                provider.Query<NsRow>(x => x.Flag != (x.Id > 100 ? x.Flag : !(bool?)(c.NullableFive == none)));
                Assert.AreEqual(1, c.NullableFiveReads, "x.Flag != (x.Id > 100 ? x.Flag : !(bool?)(c.NullableFive == none))");

                // A null bool? that the fold read inside a predicate side isn't read again by the substitution (R6-5's residue).
                c = new Holder();
                provider.Query<NsRow>(x => (1 != x.Code) == ((bool?)(x.Id == x.Id) != c.NullFlag));
                Assert.AreEqual(1, c.NullFlagReads, "(1 != x.Code) == ((bool?)(x.Id == x.Id) != c.NullFlag)");

                // R9-3: a cast back to bool over a conditional of captured values reads what 3.11.0 reads: the cast is seen
                // through, so the conditional's known parts are literals, and the visitor doesn't bind them again. The first
                // two are read in the translation, so they count where the database then rejects the shape (SQL Server).
                c = new Holder();
                QueryForReads("readsCastCondCaptured", provider,
                    x => (bool)(bool?)((x.Id < x.Id) ? (bool)((x.N != 5) ? (bool?)(c.Five != c.None) : (bool?)!x.Other) : x.Active));
                Assert.AreEqual(1, c.FiveReads, "(bool)(bool?)(… ? (bool)(… ? (bool?)(c.Five != c.None) : …) : x.Active): c.Five");
                Assert.AreEqual(1, c.NoneReads, "(bool)(bool?)(… ? (bool)(… ? (bool?)(c.Five != c.None) : …) : x.Active): c.None");

                c = new Holder();
                QueryForReads("readsCondTestCastCond", provider,
                    x => (((x.Flag == c.NullFlag) ? (x.Id == 1) : (bool)((x.Id > 100) ? (bool?)c.False : (bool?)c.Flag)) ? x.N : x.M) < 5);
                Assert.AreEqual(1, c.NullFlagReads, "((x.Flag == c.NullFlag) ? … : (bool)(… ? (bool?)c.False : (bool?)c.Flag)) ? …: c.NullFlag");
                Assert.AreEqual(1, c.FalseReads, "((x.Flag == c.NullFlag) ? … : (bool)(… ? (bool?)c.False : (bool?)c.Flag)) ? …: c.False");
                Assert.AreEqual(1, c.FlagReads, "((x.Flag == c.NullFlag) ? … : (bool)(… ? (bool?)c.False : (bool?)c.Flag)) ? …: c.Flag");

                c = new Holder();
                provider.Query<NsRow>(x => ((bool)((x.Code != c.None) ? (bool?)c.Flag : (bool?)false) ? x.N : x.M) == 5);
                Assert.AreEqual(1, c.NoneReads, "((bool)((x.Code != c.None) ? (bool?)c.Flag : (bool?)false) ? x.N : x.M) == 5: c.None");
                Assert.AreEqual(1, c.FlagReads, "((bool)((x.Code != c.None) ? (bool?)c.Flag : (bool?)false) ? x.N : x.M) == 5: c.Flag");

                // R9-3: the fold stops at an operand that decides && or ||, as C# does (3.11.0 also read the other).
                c = new Holder();
                provider.Query<NsRow>(x => (c.False && c.Five == 5) || x.Id == 2);
                Assert.AreEqual(1, c.FalseReads, "(c.False && c.Five == 5) || x.Id == 2: c.False");
                Assert.AreEqual(0, c.FiveReads, "(c.False && c.Five == 5) || x.Id == 2: c.Five, after a false left operand");

                // ...and asks an operand that reads no row first. A cast through object, which 3.11.0's fold read as one
                // value, is seen through now; where the captured operand decides, nothing under the cast is read (a pin).
                c = new Holder();
                provider.Query<NsRow>(x => (bool)(object)(c.NullableFive == 5 && x.Active) || c.Flag);
                Assert.AreEqual(0, c.NullableFiveReads, "(bool)(object)(c.NullableFive == 5 && x.Active) || c.Flag: under the cast");
                Assert.AreEqual(1, c.FlagReads, "(bool)(object)(c.NullableFive == 5 && x.Active) || c.Flag: c.Flag");

                // R11-2: a comparison whose form is decided again after the substitution (the witness's only nullable branch
                // folds) sends what the substitution read, so the visitor reads nothing again, nor runs a lazy sequence twice.
                c = new Holder();
                provider.Query<NsComputed>(x => x.Flag == (x.Name.StartsWith(c.A) ? (x.Id >= c.None ? x.ActiveX : true) : x.Active));
                Assert.AreEqual(1, c.AReads, "x.Flag == (x.Name.StartsWith(c.A) ? (x.Id >= c.None ? x.ActiveX : true) : x.Active): c.A");
                Assert.AreEqual(1, c.NoneReads, "x.Flag == (x.Name.StartsWith(c.A) ? (x.Id >= c.None ? x.ActiveX : true) : x.Active): c.None");
                var runs = new CountingIds();
                IEnumerable<int?> sequence = runs;
                c = new Holder();
                provider.Query<NsComputed>(x => x.Flag == (sequence.Contains(x.N) ? (x.Id >= c.None ? x.ActiveX : true) : x.Active));
                Assert.AreEqual(1, runs.Enumerations, "x.Flag == (sequence.Contains(x.N) ? (x.Id >= c.None ? x.ActiveX : true) : x.Active)");
            });
        }
    }
}
