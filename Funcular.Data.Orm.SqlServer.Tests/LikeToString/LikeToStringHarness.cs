using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace Funcular.Data.Orm.Tests.LikeToString
{
    /// <summary>
    /// The integration rows of the LIKE-escaping and column-ToString plan (docs/plans/LIKE_ESCAPING_TOSTRING_PLAN.md,
    /// §4.1), run against each provider's Query, QueryAsync, Delete, DeleteAsync and projections. One derived
    /// [TestClass] per provider supplies the DDL, the value conversions, the connection and the provider. This file is
    /// compiled into SqlServer.Tests (SQL Server, SQLite), PostgreSql.Tests and MySql.Tests.
    /// <para>
    /// Each row creates <c>zz_lt_row</c> and <c>zz_lt_num</c> (drop if exists, create, seed), and drops both in
    /// <c>finally</c>. The expected rows (or projected values) come from the same expression run with LINQ-to-objects over
    /// the seed under the invariant culture; none is written by hand. A delete runs in a transaction and is rolled back.
    /// A predicate's captured values are the parameters of the one method that builds it, so they share one closure.
    /// </para>
    /// </summary>
    public abstract class LikeToStringHarness
    {
        public const string RowTable = "zz_lt_row";
        public const string NumTable = "zz_lt_num";

        /// <summary>The start of the message for <c>ToString</c> on a column this change doesn't translate (D4).</summary>
        public const string ColumnToStringPrefix = "ToString() on a column of type ";

        /// <summary>The captured-values fix's message (FD-02, D16) for a <c>ToString()</c> that isn't on a property of the entity.</summary>
        public const string CapturedToStringPrefix = "ToString() is translated only on a property of the entity";

        public const string NullableExpressionExceptionName = "Funcular.Data.Orm.Exceptions.NullableExpressionException";

        // WherePredicateGuard (D-003/D-128) messages: a delete's rejection of an all-rows predicate.
        public const string SelfReferenceMessage = "The WHERE clause cannot be a self-referencing column expression.";
        public const string AlwaysTrueMessage = "The WHERE clause is trivially true; a non-trivial WHERE clause is required.";
        public const string UnboundedSearchMessage = "The WHERE clause's Contains, StartsWith or EndsWith has an empty search value, which matches every non-null row; a non-trivial WHERE clause is required.";

        /// <summary>The message for <c>x.NCode.Value.ToString()</c> (D4; the guidance of <c>x.N.Value == 7</c>).</summary>
        public const string NullableValueToStringMessage =
            "Do not use '.Value' on nullable property 'NCode' in LINQ expressions. " +
            "FunkyORM automatically unwraps nullable types. " +
            "Use 'NCode' directly (e.g., p.NCode.ToString() instead of p.NCode.Value.ToString()).";

        /// <summary>
        /// The start of the message for <c>.Value.ToString()</c> where D4 doesn't translate the property's
        /// <c>ToString()</c> either: a non-integer type, or an argument (T4-2).
        /// </summary>
        public static string NullableValueNotTranslatedPrefix(string property, string rule) =>
            $"Do not use '.Value' on nullable property '{property}' in LINQ expressions. " +
            $"FunkyORM automatically unwraps nullable types, but {rule}; compare the column's value instead: ";

        [Table(RowTable)]
        public class LtRow
        {
            [Key] public int Id { get; set; }
            public string Name { get; set; }
            public int Code { get; set; }
            public int? NCode { get; set; }
            public decimal? NAmount { get; set; }
            public long Big { get; set; }
            public decimal Amount { get; set; }
            public bool Flag { get; set; }
            [NotMapped] public string Label { get; set; }
        }

        [Table(NumTable)]
        public class LtNum
        {
            [Key] public int Id { get; set; }
            public sbyte I8 { get; set; }
            public byte U8 { get; set; }
            public short I16 { get; set; }
            public ushort U16 { get; set; }
            public int I32 { get; set; }
            public uint U32 { get; set; }
            public long I64 { get; set; }
            public ulong U64 { get; set; }
        }

        public static readonly LtRow[] RowSeed =
        {
            new LtRow { Id = 1, Name = "a_b", Code = 7, NCode = 7, NAmount = 1.50m, Big = -5, Amount = 1.50m, Flag = true },
            new LtRow { Id = 2, Name = "axb", Code = 70, NCode = null, Big = 5, Amount = 2.00m },
            new LtRow { Id = 3, Name = "a%b", Code = 5, NCode = null, Big = 10, Amount = 3.00m },
            new LtRow { Id = 4, Name = "a[b", Code = 0, NCode = 0, Big = 0, Amount = 4.00m },
            new LtRow { Id = 5, Name = "a!b", Code = 1, NCode = 1, Big = 1, Amount = 5.00m },
            new LtRow { Id = 6, Name = "a\\b", Code = 2, NCode = 2, Big = 2, Amount = 6.00m },
            new LtRow { Id = 7, Name = "ab]c", Code = 3, NCode = 3, Big = 3, Amount = 7.00m },
            new LtRow { Id = 8, Name = "abcdefghijklmnopqrstuvwxyz", Code = 4, NCode = 4, Big = 4, Amount = 8.00m },
        };

        private static readonly string[] NumColumns = { "I8", "U8", "I16", "U16", "I32", "U32", "I64", "U64" };

        #region Provider seam

        /// <summary>Marks the test inconclusive when the provider's test database is unavailable.</summary>
        protected virtual void RequireDatabase() { }

        /// <summary>Runs before the tables are created (SQLite creates its temp file here).</summary>
        protected virtual void PrepareDatabase() { }

        /// <summary>Drops both tables (SQLite deletes its temp file instead).</summary>
        protected virtual void DropTables()
        {
            using var connection = OpenConnection();
            Execute(connection, $"DROP TABLE IF EXISTS {RowTable}");
            Execute(connection, $"DROP TABLE IF EXISTS {NumTable}");
        }

        /// <summary>An open connection to the test database.</summary>
        protected abstract DbConnection OpenConnection();

        protected abstract string RowDdl { get; }

        protected abstract string NumDdl { get; }

        /// <summary>The value to bind for <paramref name="column"/> (a <see cref="LtNum"/> property, or <c>Flag</c>).</summary>
        protected abstract object ToDb(string column, object value);

        /// <summary>The largest <c>ulong</c> the dialect stores (SQLite's integers are signed).</summary>
        protected virtual ulong U64Max => ulong.MaxValue;

        protected abstract OrmDataProvider CreateProvider();

        /// <summary>The provider assembly, which defines its own <c>NullableExpressionException</c>.</summary>
        protected abstract Assembly ProviderAssembly { get; }

        #endregion

        #region Predicates

        private static readonly MethodInfo StringContains = typeof(string).GetMethod("Contains", new[] { typeof(string) });
        private static readonly MethodInfo StringStartsWith = typeof(string).GetMethod("StartsWith", new[] { typeof(string) });
        private static readonly MethodInfo StringEndsWith = typeof(string).GetMethod("EndsWith", new[] { typeof(string) });

        /// <summary><c>x =&gt; x.Name.Method("value")</c>, with the value as a constant.</summary>
        private static Expression<Func<LtRow, bool>> Literal(string method, string value)
        {
            var x = Expression.Parameter(typeof(LtRow), "x");
            var info = method == "Contains" ? StringContains : method == "StartsWith" ? StringStartsWith : StringEndsWith;
            return Expression.Lambda<Func<LtRow, bool>>(
                Expression.Call(Expression.Property(x, nameof(LtRow.Name)), info, Expression.Constant(value, typeof(string))), x);
        }

        /// <summary><c>x =&gt; x.Name.Method(v)</c>, with <paramref name="v"/> captured.</summary>
        private static Expression<Func<LtRow, bool>> Captured(string method, string v) =>
            method == "Contains" ? x => x.Name.Contains(v)
            : method == "StartsWith" ? x => x.Name.StartsWith(v)
            : (Expression<Func<LtRow, bool>>)(x => x.Name.EndsWith(v));

        private static Expression<Func<LtRow, bool>> CapturedContains(string s, string v) => x => x.Id == 1 || s.Contains(v);

        private static Expression<Func<LtRow, bool>> NonColumnReceiver(decimal d) => x => x.Id == 2 && d.ToString() == d.ToString();

        /// <summary><c>x =&gt; x.Member.ToString() == text</c> on <see cref="LtNum"/>.</summary>
        private static Expression<Func<LtNum, bool>> NumToStringEquals(string member, string text)
        {
            var x = Expression.Parameter(typeof(LtNum), "x");
            var property = Expression.Property(x, member);
            var call = Expression.Call(property, property.Type.GetMethod("ToString", Type.EmptyTypes));
            return Expression.Lambda<Func<LtNum, bool>>(Expression.Equal(call, Expression.Constant(text, typeof(string))), x);
        }

        private static Expression<Func<LtRow, bool>> RowToString(string key)
        {
            switch (key)
            {
                case "code07": return x => x.Code.ToString() == "07";
                case "code7": return x => x.Code.ToString() == "7";
                case "ncodeEmpty": return x => x.NCode.ToString() == "";
                case "ncode7": return x => x.NCode.ToString() == "7";
                case "longName": return x => x.Name.ToString() == "abcdefghijklmnopqrstuvwxyz";
                case "amount": return x => x.Amount.ToString() == "1.50";
                case "flag": return x => x.Flag.ToString() == "True";
                case "codeD2": return x => x.Code.ToString("D2") == "07";
                case "codeInvariant": return x => x.Code.ToString(CultureInfo.InvariantCulture) == "7";
                case "ncodeValue": return x => x.NCode.Value.ToString() == "7";
                case "namountValue": return x => x.NAmount.Value.ToString() == "1.50";
                case "ncodeValueD2": return x => x.NCode.Value.ToString("D2") == "07";
                case "nameInvariant": return x => x.Name.ToString(CultureInfo.InvariantCulture) == "a_b";
                case "codeSelf": return x => x.Code.ToString() == x.Code.ToString();
                case "ncodeSelf": return x => x.NCode.ToString() == x.NCode.ToString();
                case "nameSelf": return x => x.Name.ToString() == x.Name;
                case "bigMinusFive": return x => x.Big.ToString() == "-5";
                default: throw new ArgumentException($"Unknown predicate {key}.", nameof(key));
            }
        }

        private static Expression<Func<LtRow, LtRow>> Projection(string key)
        {
            switch (key)
            {
                case "codeToString": return x => new LtRow { Id = x.Id, Label = x.Code.ToString() };
                case "ncodeToString": return x => new LtRow { Id = x.Id, Label = x.NCode.ToString() };
                case "containsUnderscore": return x => new LtRow { Id = x.Id, Label = x.Name.Contains("_") ? "y" : "n" };
                case "startsWithAPercent": return x => new LtRow { Id = x.Id, Label = x.Name.StartsWith("a%") ? "y" : "n" };
                case "amount": return x => new LtRow { Id = x.Id, Label = x.Amount.ToString() };
                case "codeD2": return x => new LtRow { Id = x.Id, Label = x.Code.ToString("D2") };
                default: throw new ArgumentException($"Unknown projection {key}.", nameof(key));
            }
        }

        #endregion

        #region Row sources

        private static readonly string[] Paths = { "query", "queryAsync", "delete", "deleteAsync" };
        private static readonly string[] QueryPaths = { "query", "queryAsync" };
        private static readonly string[] DeletePaths = { "delete", "deleteAsync" };

        private static readonly (string Method, string Value)[] LiteralCases =
        {
            ("Contains", "_"), ("Contains", "%"), ("Contains", "["), ("Contains", "]"), ("Contains", "!"),
            ("Contains", "\\"), ("Contains", "[x]"), ("Contains", "a_"), ("Contains", "x"), ("Contains", ""),
            ("StartsWith", "a_"), ("StartsWith", "a%"), ("StartsWith", "a["), ("StartsWith", "a!"), ("StartsWith", "a\\"),
            ("StartsWith", "a"),
            ("EndsWith", "_b"), ("EndsWith", "%b"), ("EndsWith", "[b"), ("EndsWith", "!b"), ("EndsWith", "\\b"),
            ("EndsWith", "b"),
        };

        private static readonly (string Method, string Value)[] CapturedCases =
        {
            ("Contains", "_"), ("Contains", "%"), ("Contains", "["), ("Contains", "!"), ("Contains", "\\"),
            ("StartsWith", "a_"), ("StartsWith", "a%"), ("StartsWith", "a["), ("StartsWith", "a!"), ("StartsWith", "a\\"),
            ("EndsWith", "_b"), ("EndsWith", "%b"), ("EndsWith", "[b"), ("EndsWith", "!b"), ("EndsWith", "\\b"),
        };

        public static IEnumerable<object[]> ColumnQueryRows()
        {
            foreach (var (method, value) in LiteralCases)
                foreach (var path in QueryPaths)
                    yield return new object[] { method, value, "Lit", path };
            foreach (var (method, value) in CapturedCases)
                foreach (var path in QueryPaths)
                    yield return new object[] { method, value, "Cap", path };
        }

        // 3.11.0 (D-003/D-128): one shared path; every value the SELECT rows match literally, a delete matches literally too
        // (the former delete-only wildcard rejection is gone with the escaping that made it moot). The empty value is
        // the exception (review R-1, A-1): it matches every non-null row, so a delete of it is refused (EmptySearchValueRows).
        public static IEnumerable<object[]> ColumnDeleteRows()
        {
            foreach (var (method, value) in LiteralCases.Where(c => c.Value.Length > 0))
                foreach (var path in DeletePaths)
                    yield return new object[] { method, value, "Lit", path };
            foreach (var (method, value) in CapturedCases)
                foreach (var path in DeletePaths)
                    yield return new object[] { method, value, "Cap", path };
        }

        public static IEnumerable<object[]> EmptySearchValueRows()
        {
            foreach (var method in new[] { "Contains", "StartsWith", "EndsWith" })
                foreach (var form in new[] { "Lit", "Cap" })
                    foreach (var path in DeletePaths)
                        yield return new object[] { method, form, path };
        }

        // 3.11.0 (D-003/D-128): a string Contains on a value that doesn't read the row is answered in C# on every path;
        // "abc".Contains("b") holds, so x.Id == 1 || … selects every row, and a delete of it is refused (all rows).
        public static IEnumerable<object[]> CapturedContainsRows()
        {
            foreach (var value in new[] { "_", "%", "[a]" })
                foreach (var path in Paths)
                    yield return new object[] { value, path };
            foreach (var path in QueryPaths)
                yield return new object[] { "b", path };
        }

        public static IEnumerable<object[]> PathRows() => Paths.Select(p => new object[] { p });

        public static IEnumerable<object[]> DeletePathRows() => DeletePaths.Select(p => new object[] { p });

        public static IEnumerable<object[]> IntegerToStringRows()
        {
            foreach (var column in NumColumns)
                foreach (var bound in new[] { "min", "max" })
                    foreach (var path in Paths)
                        yield return new object[] { column + "_" + bound, path };
            foreach (var key in new[] { "code07", "code7", "ncodeEmpty", "ncode7", "longName" })
                foreach (var path in Paths)
                    yield return new object[] { key, path };
        }

        public static IEnumerable<object[]> OtherToStringRows()
        {
            foreach (var key in new[] { "amount", "flag", "codeD2", "codeInvariant", "nameInvariant" })
                foreach (var path in Paths)
                    yield return new object[] { key, path };
        }

        public static IEnumerable<object[]> ToStringSelfDeleteRows()
        {
            foreach (var key in new[] { "codeSelf", "ncodeSelf", "nameSelf" })
                foreach (var path in DeletePaths)
                    yield return new object[] { key, path };
        }

        public static IEnumerable<object[]> ToStringSelfQueryRows()
        {
            foreach (var key in new[] { "codeSelf", "ncodeSelf", "nameSelf" })
                foreach (var path in QueryPaths)
                    yield return new object[] { key, path };
        }

        public static IEnumerable<object[]> NullableValueRows()
        {
            foreach (var key in new[] { "ncodeValue", "namountValue", "ncodeValueD2" })
                foreach (var path in Paths)
                    yield return new object[] { key, path };
        }

        public static IEnumerable<object[]> QueryPathRows() => QueryPaths.Select(p => new object[] { p });

        #endregion

        #region Rows

        // AC1
        [DataTestMethod]
        [DynamicData(nameof(ColumnQueryRows), DynamicDataSourceType.Method)]
        public async Task ColumnStringMethods_QuerySelectsWhatCSharpSelects(string method, string value, string form, string path) =>
            await AssertSelects(form == "Lit" ? Literal(method, value) : Captured(method, value), RowSeed, path,
                $"{method}({value}) {form}");

        // AC1
        [DataTestMethod]
        [DynamicData(nameof(ColumnDeleteRows), DynamicDataSourceType.Method)]
        public async Task ColumnStringMethods_DeleteDeletesWhatCSharpSelects(string method, string value, string form, string path) =>
            await AssertSelects(form == "Lit" ? Literal(method, value) : Captured(method, value), RowSeed, path,
                $"{method}({value}) {form}");

        // Review R-1 (A-1), D-003's "unbounded": an empty search value sends LIKE '%%' ESCAPE '!', which matches every
        // non-null name (the SELECT rows pin that it selects all eight); a delete of it is refused before any command.
        [DataTestMethod]
        [DynamicData(nameof(EmptySearchValueRows), DynamicDataSourceType.Method)]
        public async Task EmptySearchValue_DeleteIsRefused(string method, string form, string path)
        {
            var exception = await AssertRejected(form == "Lit" ? Literal(method, "") : Captured(method, ""), path, $"{method}(\"\") {form}");
            Assert.AreEqual(typeof(InvalidOperationException), exception.GetType(), $"{method}(\"\") {form} {path}: {exception}");
            Assert.AreEqual(UnboundedSearchMessage, exception.Message, $"{method}(\"\") {form} {path}");
        }

        // AC2: "abc".Contains(v) is false for "_", "%", "[a]", so x.Id == 1 || … selects and deletes row 1 on every
        // path; it holds for "b", so the query selects every row (C#'s answer).
        [DataTestMethod]
        [DynamicData(nameof(CapturedContainsRows), DynamicDataSourceType.Method)]
        public async Task CapturedStringContains_SelectsWhatCSharpSelects(string value, string path) =>
            await AssertSelects(CapturedContains("abc", value), RowSeed, path, $"\"abc\".Contains({value})");

        // AC2, 3.11.0 (D-128): "abc".Contains("b") holds, so x.Id == 1 || … always holds; a delete of it is refused.
        [DataTestMethod]
        [DynamicData(nameof(DeletePathRows), DynamicDataSourceType.Method)]
        public async Task CapturedStringContains_AlwaysTrue_DeleteIsRefused(string path)
        {
            var exception = await AssertRejected(CapturedContains("abc", "b"), path, "\"abc\".Contains(b)");
            Assert.AreEqual(typeof(InvalidOperationException), exception.GetType(), $"\"abc\".Contains(b) {path}: {exception}");
            Assert.AreEqual(AlwaysTrueMessage, exception.Message, $"\"abc\".Contains(b) {path}");
        }

        // AC3
        [DataTestMethod]
        [DynamicData(nameof(IntegerToStringRows), DynamicDataSourceType.Method)]
        public async Task IntegerColumnToString_ComparesAsText(string key, string path)
        {
            var bound = key.Split('_');
            if (bound.Length == 2)
            {
                var row = NumSeed()[bound[1] == "min" ? 0 : 1];
                var text = Convert.ToString(typeof(LtNum).GetProperty(bound[0]).GetValue(row), CultureInfo.InvariantCulture);
                await AssertSelects(NumToStringEquals(bound[0], text), NumSeed(), path, $"{bound[0]}.ToString() == \"{text}\"");
            }
            else
            {
                await AssertSelects(RowToString(key), RowSeed, path, key);
            }
        }

        // AC4
        [DataTestMethod]
        [DynamicData(nameof(OtherToStringRows), DynamicDataSourceType.Method)]
        public async Task OtherColumnToString_IsRejected(string key, string path)
        {
            var exception = await AssertRejected(RowToString(key), path, key);
            Assert.AreEqual(typeof(NotSupportedException), exception.GetType(), $"{key} {path}: {exception}");
            StringAssert.StartsWith(exception.Message, ColumnToStringPrefix, $"{key} {path}");
        }

        // AC9
        [DataTestMethod]
        [DataRow("codeToString")]
        [DataRow("ncodeToString")]
        [DataRow("containsUnderscore")]
        [DataRow("startsWithAPercent")]
        public void Projection_GivesCSharpsValue(string key)
        {
            RequireDatabase();
            var projection = Projection(key);
            var expected = InvariantCulture(() => RowSeed.Select(projection.Compile()).Select(Labeled).ToArray());
            try
            {
                CreateTables();
                using var provider = CreateProvider();
                string[] actual;
                try
                {
                    actual = provider.Query<LtRow>().Select(projection).ToList().OrderBy(r => r.Id).Select(Labeled).ToArray();
                }
                catch (Exception ex)
                {
                    Assert.Fail($"{key}: {Unwrap(ex).GetType().Name}: {Unwrap(ex).Message}");
                    return;
                }
                CollectionAssert.AreEqual(expected, actual, $"{key}: {string.Join(" ", actual)}");
            }
            finally
            {
                DropTables();
            }
        }

        // AC9
        [DataTestMethod]
        [DataRow("amount")]
        [DataRow("codeD2")]
        public void Projection_OtherColumnToString_IsRejected(string key)
        {
            RequireDatabase();
            try
            {
                CreateTables();
                using var provider = CreateProvider();
                Exception exception = null;
                try
                {
                    provider.Query<LtRow>().Select(Projection(key)).ToList();
                }
                catch (Exception ex)
                {
                    exception = Unwrap(ex);
                }
                Assert.IsNotNull(exception, $"{key}: no exception");
                Assert.AreEqual(typeof(NotSupportedException), exception.GetType(), $"{key}: {exception}");
                StringAssert.StartsWith(exception.Message, ColumnToStringPrefix, key);
            }
            finally
            {
                DropTables();
            }
        }

        // AC10, under D-128: d.ToString() == d.ToString() doesn't read the row and is a core type's ToString(), so the
        // shared evaluation answers it in C# (true) and x.Id == 2 && … selects, and deletes, row 2 on every path.
        [DataTestMethod]
        [DynamicData(nameof(PathRows), DynamicDataSourceType.Method)]
        public async Task NonColumnReceiverToString_IsEvaluated(string path) =>
            await AssertSelects(NonColumnReceiver(1.5m), RowSeed, path, "d.ToString() == d.ToString()");

        // The merge notes' rows: a column's ToString() compared with a captured value's is rejected for the captured side,
        // so the culture difference between the cast's invariant text and C#'s current-culture text can't arise.
        [DataTestMethod]
        [DynamicData(nameof(PathRows), DynamicDataSourceType.Method)]
        public async Task ColumnToStringComparedWithCapturedToString_IsRejected(string path)
        {
            var seven = 7;
            Expression<Func<LtRow, bool>> predicate = x => x.Code.ToString() == seven.ToString();
            var exception = await AssertRejected(predicate, path, "x.Code.ToString() == seven.ToString()");
            Assert.AreEqual(typeof(NotSupportedException), exception.GetType(), $"{path}: {exception}");
            StringAssert.StartsWith(exception.Message, CapturedToStringPrefix, path);

            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("fi-FI");
            try
            {
                long negFive = -5;
                Expression<Func<LtRow, bool>> big = x => x.Big.ToString() == negFive.ToString();
                exception = await AssertRejected(big, path, "fi-FI x.Big.ToString() == negFive.ToString()");
                Assert.AreEqual(typeof(NotSupportedException), exception.GetType(), $"{path}: {exception}");
                StringAssert.StartsWith(exception.Message, CapturedToStringPrefix, path);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        // T4-1, under D-128: a self-comparison through a no-argument ToString() is refused as a delete on every provider…
        [DataTestMethod]
        [DynamicData(nameof(ToStringSelfDeleteRows), DynamicDataSourceType.Method)]
        public async Task ToStringSelfComparison_DeleteIsRefused(string key, string path)
        {
            var exception = await AssertRejected(RowToString(key), path, key);
            Assert.AreEqual(typeof(InvalidOperationException), exception.GetType(), $"{key} {path}: {exception}");
            Assert.AreEqual(SelfReferenceMessage, exception.Message, $"{key} {path}");
        }

        // …and selects every row as a query, as C# does (the row-independent truth of a self-comparison is sent as a literal).
        [DataTestMethod]
        [DynamicData(nameof(ToStringSelfQueryRows), DynamicDataSourceType.Method)]
        public async Task ToStringSelfComparison_QuerySelectsEveryRow(string key, string path) =>
            await AssertSelects(RowToString(key), RowSeed, path, key);

        // AC11
        [DataTestMethod]
        [DynamicData(nameof(NullableValueRows), DynamicDataSourceType.Method)]
        public async Task NullableValueToString_IsRejected(string key, string path)
        {
            var exception = await AssertRejected(RowToString(key), path, key);
            Assert.AreEqual(NullableExpressionExceptionName, exception.GetType().FullName, $"{path}: {exception}");
            Assert.AreEqual(ProviderAssembly, exception.GetType().Assembly, $"{path}: the exception's assembly");
            if (key == "ncodeValue")
                Assert.AreEqual(NullableValueToStringMessage, exception.Message, path);
            else if (key == "namountValue")
                StringAssert.StartsWith(exception.Message, NullableValueNotTranslatedPrefix("NAmount",
                    "ToString() on a column of type Decimal isn't translated, because the database's text for it isn't .NET's"), path);
            else
                StringAssert.StartsWith(exception.Message, NullableValueNotTranslatedPrefix("NCode",
                    "ToString() on a column of type Int32 isn't translated with an argument (a format or a format provider)"), path);
        }

        // AC11
        [DataTestMethod]
        [DynamicData(nameof(QueryPathRows), DynamicDataSourceType.Method)]
        public async Task IntegerToString_IsInvariantUnderFiFi(string path)
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("fi-FI");
            try
            {
                await AssertSelects(RowToString("bigMinusFive"), RowSeed, path, "fi-FI Big.ToString() == \"-5\"");
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        #endregion

        #region Helpers

        private LtNum[] NumSeed() => new[]
        {
            new LtNum
            {
                Id = 1, I8 = sbyte.MinValue, U8 = byte.MinValue, I16 = short.MinValue, U16 = ushort.MinValue,
                I32 = int.MinValue, U32 = uint.MinValue, I64 = long.MinValue, U64 = ulong.MinValue
            },
            new LtNum
            {
                Id = 2, I8 = sbyte.MaxValue, U8 = byte.MaxValue, I16 = short.MaxValue, U16 = ushort.MaxValue,
                I32 = int.MaxValue, U32 = uint.MaxValue, I64 = long.MaxValue, U64 = U64Max
            },
        };

        private void CreateTables()
        {
            PrepareDatabase();
            using var connection = OpenConnection();
            Execute(connection, $"DROP TABLE IF EXISTS {RowTable}");
            Execute(connection, $"DROP TABLE IF EXISTS {NumTable}");
            Execute(connection, RowDdl);
            Execute(connection, NumDdl);
            foreach (var row in RowSeed)
                Execute(connection,
                    $"INSERT INTO {RowTable} (id, name, code, ncode, namount, big, amount, flag) VALUES (@id, @name, @code, @ncode, @namount, @big, @amount, @flag)",
                    ("@id", row.Id), ("@name", row.Name), ("@code", row.Code), ("@ncode", row.NCode), ("@namount", row.NAmount), ("@big", row.Big),
                    ("@amount", row.Amount), ("@flag", ToDb(nameof(LtRow.Flag), row.Flag)));
            foreach (var row in NumSeed())
                Execute(connection,
                    $"INSERT INTO {NumTable} (id, i8, u8, i16, u16, i32, u32, i64, u64) VALUES (@id, @i8, @u8, @i16, @u16, @i32, @u32, @i64, @u64)",
                    new[] { ("@id", (object)row.Id) }.Concat(NumColumns.Select(c =>
                        ("@" + c.ToLowerInvariant(), ToDb(c, typeof(LtNum).GetProperty(c).GetValue(row))))).ToArray());
        }

        protected static void Execute(DbConnection connection, string sql, params (string Name, object Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Runs <paramref name="predicate"/> on <paramref name="path"/> and asserts it selects (or deletes) exactly the rows
        /// C# selects from <paramref name="seed"/> under the invariant culture.
        /// </summary>
        private async Task AssertSelects<T>(Expression<Func<T, bool>> predicate, T[] seed, string path, string label)
            where T : class, new()
        {
            RequireDatabase();
            var expected = InvariantCulture(() => seed.Where(predicate.Compile()).Select(IdOf).OrderBy(i => i).ToArray());
            try
            {
                CreateTables();
                using var provider = CreateProvider();
                if (path == "query" || path == "queryAsync")
                {
                    int[] actual;
                    try
                    {
                        var rows = path == "queryAsync" ? await provider.QueryAsync(predicate) : provider.Query<T>().Where(predicate).ToList();
                        actual = rows.Select(IdOf).OrderBy(i => i).ToArray();
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail($"{label} {path}: {Unwrap(ex).GetType().Name}: {Unwrap(ex).Message}");
                        return;
                    }
                    CollectionAssert.AreEqual(expected, actual, $"{label} {path}: selected {string.Join(",", actual)}");
                    return;
                }

                var transactional = (ISqlOrmProvider)provider;
                transactional.BeginTransaction();
                try
                {
                    int deleted;
                    try
                    {
                        deleted = path == "deleteAsync" ? await provider.DeleteAsync(predicate) : provider.Delete(predicate);
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail($"{label} {path}: {Unwrap(ex).GetType().Name}: {Unwrap(ex).Message}");
                        return;
                    }
                    var remaining = provider.GetList<T>().Select(IdOf).OrderBy(i => i).ToArray();
                    var survivors = seed.Select(IdOf).Except(expected).OrderBy(i => i).ToArray();
                    CollectionAssert.AreEqual(survivors, remaining, $"{label} {path}: surviving ids {string.Join(",", remaining)}");
                    Assert.AreEqual(expected.Length, deleted, $"{label} {path}: deleted count");
                }
                finally
                {
                    transactional.RollbackTransaction();
                }
            }
            finally
            {
                DropTables();
            }
        }

        /// <summary>
        /// Runs <paramref name="predicate"/> on <paramref name="path"/>, asserts it throws, and for a delete that no DELETE was
        /// logged and every row remains. Returns the exception.
        /// </summary>
        private async Task<Exception> AssertRejected(Expression<Func<LtRow, bool>> predicate, string path, string key)
        {
            RequireDatabase();
            try
            {
                CreateTables();
                using var provider = CreateProvider();
                var logged = new List<string>();
                provider.Log = logged.Add;
                if (path == "query" || path == "queryAsync")
                {
                    try
                    {
                        var rows = path == "queryAsync" ? await provider.QueryAsync(predicate) : provider.Query<LtRow>().Where(predicate).ToList();
                        Assert.Fail($"{key} {path}: selected {string.Join(",", rows.Select(r => r.Id))}");
                    }
                    catch (Exception ex) when (!(ex is AssertFailedException))
                    {
                        return Unwrap(ex);
                    }
                }

                var transactional = (ISqlOrmProvider)provider;
                transactional.BeginTransaction();
                try
                {
                    Exception thrown = null;
                    try
                    {
                        if (path == "deleteAsync")
                            await provider.DeleteAsync(predicate);
                        else
                            provider.Delete(predicate);
                    }
                    catch (Exception ex)
                    {
                        thrown = Unwrap(ex);
                    }
                    var deletes = logged.Where(s => s != null && s.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)).ToList();
                    Assert.IsNotNull(thrown, $"{key} {path}: the delete ran; logged {string.Join(" | ", deletes)}");
                    Assert.AreEqual(0, deletes.Count, $"{key} {path}: a DELETE was logged: {string.Join(" | ", deletes)}");
                    Assert.AreEqual(RowSeed.Length, provider.GetList<LtRow>().Count, $"{key} {path}: rows were deleted");
                    return thrown;
                }
                finally
                {
                    transactional.RollbackTransaction();
                }
            }
            finally
            {
                DropTables();
            }
        }

        /// <summary><c>id=label</c>, with a null label written as <c>(null)</c>, so it never passes for <c>""</c>.</summary>
        private static string Labeled(LtRow row) => $"{row.Id}={row.Label ?? "(null)"}";

        private static TResult InvariantCulture<TResult>(Func<TResult> run)
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                return run();
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        private static Exception Unwrap(Exception exception)
        {
            while ((exception is TargetInvocationException || exception is AggregateException) && exception.InnerException != null)
                exception = exception.InnerException;
            return exception;
        }

        private static int IdOf<T>(T row) => row switch
        {
            LtRow r => r.Id,
            LtNum n => n.Id,
            _ => throw new ArgumentException(row?.GetType().Name)
        };

        #endregion
    }
}
