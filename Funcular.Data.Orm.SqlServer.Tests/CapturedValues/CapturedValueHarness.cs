using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.Tests.CapturedValues
{
    /// <summary>
    /// Integration rows of docs/plans/WHERE_CAPTURED_VALUES_PLAN.md §4.1 item 3 and §4.2, run against each provider's
    /// test database through Query, QueryAsync, a LINQ Where, Count, Delete and DeleteAsync. This file is compiled into
    /// SqlServer.Tests and linked into PostgreSql.Tests, MySql.Tests and Sqlite.Tests (C# 14 there, so its arrays bind
    /// to MemoryExtensions); one derived [TestClass] per provider, in that provider's own project.
    /// <para>
    /// Each test creates <c>zz_cv_row</c> (drop if exists, create, seed rows (1,'A','alpha',1), (2,'b','beta',NULL),
    /// (3,'c','gamma',3)) and drops it in <c>finally</c>. Every delete runs in a transaction that is rolled back after
    /// the remaining rows are read through the provider.
    /// </para>
    /// </summary>
    public abstract class CapturedValueHarness
    {
        public const string Table = "zz_cv_row";

        #region Messages (plan §5.3)

        protected const string ItemMessage =
            "Collection Contains is translated only as <property> IN (<values>), with a property of the entity as its item and a collection that doesn't read the row: ";
        protected const string NullCollectionMessage = "The collection of Contains is null: ";
        protected const string ContainsReceiverMessage =
            "Contains() is translated only on a property of the entity, or on a value that doesn't read the row: ";
        protected const string ToStringMessage =
            "ToString() is translated only on a property of the entity; for a value that doesn't read the row, compute the string before the query: ";
        protected const string CouldNotEvaluateMessage = "Could not evaluate expression: ";
        protected const string NoColumnMessage = "Delete operation WHERE clause must reference at least one column from the target table.";

        #endregion

        #region Entity and rows

        /// <summary>
        /// Columns are named explicitly: on development/3.10 SQLite stores discovered names under the type's short name
        /// but reads them under its full name, so a nested entity's <c>NullableId</c> wouldn't find <c>nullable_id</c>
        /// (fixed on fix/provider-scoped-caches; the plan's §6.1 Task 2 record).
        /// </summary>
        [Table(Table)]
        public class CvRow
        {
            [Key, Column("id")] public int Id { get; set; }
            [Column("code")] public string Code { get; set; }
            [Column("name")] public string Name { get; set; }
            [Column("nullable_id")] public int? NullableId { get; set; }
            [NotMapped] public CvInner Child { get; set; }
            [NotMapped] public string Label { get; set; }
            [NotMapped] public bool Flag { get; set; }
        }

        public class CvInner
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private static readonly CvRow[] Rows =
        {
            new CvRow { Id = 1, Code = "A", Name = "alpha", NullableId = 1 },
            new CvRow { Id = 2, Code = "b", Name = "beta", NullableId = null },
            new CvRow { Id = 3, Code = "c", Name = "gamma", NullableId = 3 },
        };

        #endregion

        #region Provider seam

        /// <summary>Marks the test inconclusive when the provider's test database is unavailable.</summary>
        protected virtual void RequireDatabase() { }

        /// <summary>Drops <c>zz_cv_row</c> if it exists, creates and seeds it.</summary>
        protected abstract void CreateTable();

        /// <summary>Drops <c>zz_cv_row</c> (or deletes the SQLite file).</summary>
        protected abstract void DropTable();

        protected abstract OrmDataProvider CreateProvider();

        /// <summary>Runs before the log is captured: MySQL discovers the type so its delete works on a cold cache.</summary>
        protected virtual void WarmUp(OrmDataProvider provider) { }

        /// <summary>A harness switch, not provider API: whether a bare predicate may be projected as a value.</summary>
        protected virtual bool SupportsPredicateAsValue => true;

        /// <summary>The class an array's <c>Contains</c> binds to in this test project (C# 12: Enumerable; C# 14: MemoryExtensions).</summary>
        protected virtual Type ArrayContainsDeclaringType => typeof(Enumerable);

        #endregion

        #region Captured values

        public class Request
        {
            public int Id { get; set; }
            public string Code { get; set; }
            public string Name { get; set; }
            public int? NullableId { get; set; }
            public CvInner Inner { get; set; }
            public List<int> Ids { get; set; }
        }

        public class Settings
        {
            public override string ToString() => "on";
        }

        public sealed class SealedSettings
        {
            public override string ToString() => "on";
        }

        public struct UserStruct
        {
            public override string ToString() => "on";
        }

        public static class StaticHolder
        {
            public static int Id => 5;
            public static string Name => "alpha";
        }

        /// <summary>Predicates over its own properties: each captures <c>this</c>.</summary>
        public class ThisHolder
        {
            public string Filter { get; set; } = "zz";
            public string Code { get; set; } = "b";
            public string NullFilter { get; set; }
            public Expression<Func<CvRow, bool>> ContainsFilter() => x => x.Id == 2 || x.Name.Contains(Filter);
            public Expression<Func<CvRow, bool>> StartsWithFilter() => x => x.Id == 2 || x.Name.StartsWith(Filter);
            public Expression<Func<CvRow, bool>> CodeEqualsCodeToString() => x => x.Code == Code.ToString();
            public Expression<Func<CvRow, bool>> OptionalNullFilter() => x => NullFilter == null || x.Name.Contains(NullFilter);
            public Expression<Func<CvRow, string>> LabelFromThis() => x => Code.ToString();
        }

        private static int Seven() => 7;

        private readonly List<int> ids = new List<int> { 1, 2 };
        private readonly int[] arr = { 1, 2 };
        private readonly Request absent = new Request
        {
            Id = 5, Code = "a", Name = "zeta", NullableId = 5, Inner = new CvInner { Id = 5 }, Ids = new List<int> { 1, 2 }
        };
        private readonly Request present = new Request { Id = 1 };
        private readonly Request nullHolder = new Request { NullableId = null };
        private readonly Request nullRequest = null;
        private readonly Request nullIdsHolder = new Request { Ids = null };
        private readonly Request needle = new Request { Name = "mm" };
        private readonly Request bee = new Request { Code = "b" };
        private readonly int local = 5;
        private readonly int localInt = 5;
        private readonly int five = 5;
        private readonly int? n = null;
        private readonly object boxed = 5;
        private readonly object objStr = "b";
        private readonly IComparable ifaceStr = "b";
        private readonly string sB = "b";
        private readonly List<string> codes = new List<string> { "A" };
        private readonly HashSet<string> ci = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" };
        private readonly List<int?> nulls = new List<int?> { null };
        private readonly List<int?> nullablesWith1 = new List<int?> { 1 };
        private readonly List<int> lengths = new List<int> { 4 };
        private readonly List<long> longs = new List<long> { 1, 2 };
        private readonly List<int> emptyIds = new List<int>();
        private readonly string s = "zeta";
        private readonly string nullFilter = null;
        private readonly Settings settings = new Settings();
        private readonly SealedSettings sealedSettings = new SealedSettings();
        private readonly UserStruct userStruct = new UserStruct();
        private readonly UserStruct? nullableUserStruct = new UserStruct();
        private readonly KeyValuePair<int, int> pair = new KeyValuePair<int, int>(1, 2);
        private readonly ThisHolder holder = new ThisHolder();

        #endregion

        #region Predicates

        private Expression<Func<CvRow, bool>> Predicate(string key)
        {
            switch (key)
            {
                // AC1: a collection Contains whose item doesn't read the row.
                case "orAbsent": return x => x.Id == 2 || ids.Contains(absent.Id);
                case "orPresent": return x => x.Id == 2 || ids.Contains(present.Id);
                case "andPresent": return x => x.Id == 2 && ids.Contains(present.Id);
                case "andAbsent": return x => x.Id == 2 && ids.Contains(absent.Id);
                case "aloneAbsent": return x => ids.Contains(absent.Id);
                case "aloneNotAbsent": return x => !ids.Contains(absent.Id);
                case "orNotAbsent": return x => x.Id == 2 || !ids.Contains(absent.Id);
                case "andNotPresent": return x => x.Id == 2 && !ids.Contains(present.Id);
                case "andNotAbsent": return x => x.Id == 2 && !ids.Contains(absent.Id);
                case "orLocal": return x => x.Id == 2 || ids.Contains(local);
                case "orConstant": return x => x.Id == 2 || ids.Contains(5);
                case "orNested": return x => x.Id == 2 || ids.Contains(absent.Inner.Id);
                case "orStatic": return x => x.Id == 2 || ids.Contains(StaticHolder.Id);
                case "orValue": return x => x.Id == 2 || ids.Contains(absent.NullableId.Value);
                case "orCollectionProperty": return x => x.Id == 2 || absent.Ids.Contains(5);
                case "orBothCaptured": return x => x.Id == 2 || absent.Ids.Contains(absent.Id);
                // AC2: the call as written.
                case "orCodesCase": return x => x.Id == 2 || codes.Contains(absent.Code);
                case "orNullItem": return x => x.Id == 2 || nulls.Contains(nullHolder.NullableId);
                case "andNullItem": return x => x.Id == 2 && nulls.Contains(nullHolder.NullableId);
                case "orComparerSet": return x => x.Id == 2 || ci.Contains(absent.Code);
                case "andComparerSet": return x => x.Id == 2 && ci.Contains(absent.Code);
                case "orArrayAbsent": return x => x.Id == 2 || arr.Contains(absent.Id);
                case "andArrayPresent": return x => x.Id == 2 && arr.Contains(present.Id);
                case "orCapturedReceiverCase": return x => x.Id == 2 || absent.Name.Contains("ZETA");
                case "orLocalReceiverCase": return x => x.Id == 2 || s.Contains("ZETA");
                // AC6: captured values read as C# reads them; the ToString keys are rejected (D16).
                case "containsThisFilter": return holder.ContainsFilter();
                case "startsWithThisFilter": return holder.StartsWithFilter();
                case "codeEqualsThisCodeToString": return holder.CodeEqualsCodeToString();
                case "containsNestedCaptured": return x => x.Name.Contains(needle.Name);
                case "codeEqualsNestedToString": return x => x.Code == bee.Code.ToString();
                case "orCapturedToString05": return x => x.Id == 2 || localInt.ToString() == "05";
                case "orPropertyToString05": return x => x.Id == 2 || absent.Id.ToString() == "05";
                case "orFormattedToString": return x => x.Id == 2 || five.ToString("D2") == "5";
                case "orNullableToStringIsNull": return x => x.Id == 2 || n.ToString() == null;
                case "orNullableToStringIsEmpty": return x => x.Id == 2 || n.ToString() == "";
                // AC8: nulls and failures.
                case "optionalFilterNull": return x => nullFilter == null || x.Name.Contains(nullFilter);
                case "optionalThisFilterNull": return holder.OptionalNullFilter();
                case "optionalStartsWithNull": return x => nullFilter == null || x.Name.StartsWith(nullFilter);
                case "optionalReceiverNull": return x => nullFilter == null || nullFilter.Contains("a");
                case "containsNullValue": return x => x.Id == 2 || x.Name.Contains(nullFilter);
                case "startsWithNullValue": return x => x.Id == 2 || x.Name.StartsWith(nullFilter);
                case "notContainsNullValue": return x => x.Id == 2 || !x.Name.Contains(nullFilter);
                case "throwingItem": return x => x.Id == 2 || ids.Contains(nullRequest.Id);
                case "throwingCollection": return x => nullRequest.Ids.Contains(x.Id);
                case "nullStringToString": return x => x.Code == nullFilter.ToString();
                case "intColumnToStringEqCaptured": return x => x.Id.ToString() == localInt.ToString();
                // A1: All with SQL's unknown.
                case "allNotContainsNull": return x => !x.Name.Contains(nullFilter);
                case "allContainsNull": return x => x.Name.Contains(nullFilter);
                case "allOrContainsNull": return x => x.Id == 2 || x.Name.Contains(nullFilter);
                case "allRowIndependentNull": return x => s.Contains(nullFilter);
                case "allNullableGreaterThanZero": return x => x.NullableId > 0;
                case "allIdPositive": return x => x.Id > 0;
                case "allNotIdsX": return x => !ids.Contains(x.Id);
                // AC4: an item that reads the row other than as a property of the entity.
                case "nestedParamItem": return x => ids.Contains(x.Child.Id);
                case "lengthItem": return x => lengths.Contains(x.Name.Length);
                case "convertItem": return x => longs.Contains(x.Id);
                case "emptyNestedParamItem": return x => emptyIds.Contains(x.Child.Id);
                case "nullCollection": return x => nullIdsHolder.Ids.Contains(x.Id);
                // AC5: receivers.
                case "startsWithCapturedReceiver": return x => x.Id == 2 || absent.Name.StartsWith("al");
                case "endsWithCapturedReceiver": return x => x.Id == 2 || absent.Name.EndsWith("ha");
                case "startsWithLocalReceiver": return x => x.Id == 2 || s.StartsWith("al");
                case "startsWithStaticReceiver": return x => x.Id == 2 || StaticHolder.Name.StartsWith("al");
                case "startsWithNestedParamReceiver": return x => x.Child.Name.StartsWith("a");
                // AC7: a member of a member.
                case "codeEqualsLengthToString": return x => x.Code == x.Name.Length.ToString();
                case "childNameContains": return x => x.Child.Name.Contains("a");
                // D16: more ToString that doesn't read the row, rejected (each C# true where it's compared).
                case "userTypeToString": return x => x.Id == 2 || settings.ToString() == "on";
                case "objectDeclaredToString": return x => x.Id == 2 || boxed.ToString() == "5";
                case "providerToString": return x => x.Id == 2 || five.ToString(CultureInfo.InvariantCulture) == "5";
                case "staticConvertToString": return x => x.Code == Convert.ToString(localInt);
                case "orArithmeticToString": return x => x.Id == 2 || (five + 1).ToString() == "6";
                case "orIndexToString": return x => x.Id == 2 || arr[0].ToString() == "1";
                case "orMethodToString": return x => x.Id == 2 || Seven().ToString() == "7";
                case "orCoalesceToString": return x => x.Id == 2 || (n ?? 7).ToString() == "7";
                case "orStructToString": return x => x.Id == 2 || userStruct.ToString() == "on";
                case "orNullableStructToString": return x => x.Id == 2 || nullableUserStruct.ToString() == "on";
                case "orSealedClassToString": return x => x.Id == 2 || sealedSettings.ToString() == "on";
                case "orPairToString": return x => x.Id == 2 || pair.ToString() == "[1, 2]";
                case "objectHoldingStringToString": return x => x.Code == objStr.ToString();
                case "providerStringToString": return x => x.Code == sB.ToString(CultureInfo.InvariantCulture);
                case "interfaceHoldingStringToString": return x => x.Code == ifaceStr.ToString();
                // AC10: unchanged.
                case "idsX": return x => ids.Contains(x.Id);
                case "notIdsX": return x => !ids.Contains(x.Id);
                case "nullableX": return x => nullablesWith1.Contains(x.NullableId);
                case "emptyX": return x => emptyIds.Contains(x.Id);
                case "startsWithX": return x => x.Name.StartsWith("al");
                case "containsX": return x => x.Name.Contains("mm");
                case "arrayX": return x => arr.Contains(x.Id);
                default: throw new ArgumentException("Unknown predicate " + key, nameof(key));
            }
        }

        private Expression<Func<CvRow, CvRow>> Projection(string key)
        {
            switch (key)
            {
                case "labelAbsent": return x => new CvRow { Id = x.Id, Label = ids.Contains(absent.Id) ? "in" : "out" };
                case "labelX": return x => new CvRow { Id = x.Id, Label = ids.Contains(x.Id) ? "in" : "out" };
                case "flagAbsent": return x => new CvRow { Id = x.Id, Flag = ids.Contains(absent.Id) };
                case "labelNestedParam": return x => new CvRow { Id = x.Id, Label = ids.Contains(x.Child.Id) ? "in" : "out" };
                case "labelObjectToString": return x => new CvRow { Id = x.Id, Label = objStr.ToString() };
                case "labelProviderToString": return x => new CvRow { Id = x.Id, Label = sB.ToString(CultureInfo.InvariantCulture) };
                case "labelFormatted": return x => new CvRow { Id = x.Id, Label = five.ToString("D2") };
                default: throw new ArgumentException("Unknown projection " + key, nameof(key));
            }
        }

        private static bool IsArrayKey(string key) => key.IndexOf("array", StringComparison.OrdinalIgnoreCase) >= 0;

        #endregion

        #region Plumbing

        private static string Ids(IEnumerable<CvRow> rows) => string.Join(",", rows.Select(r => r.Id).OrderBy(i => i));

        private static string Normalize(string sql) => Regex.Replace(sql ?? string.Empty, @"\s+", " ").Trim();

        /// <summary>Creates the table, runs <paramref name="body"/> with a warmed provider whose log is captured from here on, drops the table.</summary>
        private void WithTable(Action<OrmDataProvider, List<string>> body)
        {
            RequireDatabase();
            try
            {
                CreateTable();
                using (var provider = CreateProvider())
                {
                    WarmUp(provider);
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

        private void AssertBinding(string key, Expression<Func<CvRow, bool>> predicate)
        {
            if (!IsArrayKey(key))
                return;
            var call = Find<MethodCallExpression>(predicate.Body, c => c.Method.Name == "Contains");
            Assert.IsNotNull(call, key + ": no Contains call");
            Assert.AreEqual(ArrayContainsDeclaringType, call.Method.DeclaringType,
                key + ": this test project's array Contains binds elsewhere than expected (LangVersion changed?)");
        }

        private static T Find<T>(Expression expression, Func<T, bool> match) where T : Expression
        {
            var finder = new Finder<T>(match);
            finder.Visit(expression);
            return finder.Result;
        }

        private sealed class Finder<T> : ExpressionVisitor where T : Expression
        {
            private readonly Func<T, bool> _match;
            public Finder(Func<T, bool> match) => _match = match;
            public T Result { get; private set; }

            public override Expression Visit(Expression node)
            {
                if (Result == null && node is T candidate && _match(candidate))
                    Result = candidate;
                return base.Visit(node);
            }
        }

        private static List<string> Remaining(OrmDataProvider provider) =>
            provider.Query<CvRow>().ToList().Select(r => r.Id.ToString(CultureInfo.InvariantCulture)).ToList();

        private static void AssertNothingSent(List<string> log, string context)
        {
            var sent = log.Where(e => e != null && Regex.IsMatch(e, @"\b(SELECT|DELETE)\b", RegexOptions.IgnoreCase)).ToList();
            Assert.AreEqual(0, sent.Count, context + ": a command was sent: " + string.Join(" | ", sent.Select(Normalize)));
        }

        #endregion

        #region Query

        [DataTestMethod]
        // AC1
        [DataRow("orAbsent", "2")]
        [DataRow("orPresent", "1,2,3")]
        [DataRow("andPresent", "2")]
        [DataRow("andAbsent", "")]
        [DataRow("aloneAbsent", "")]
        [DataRow("orNotAbsent", "1,2,3")]
        [DataRow("andNotPresent", "")]
        [DataRow("andNotAbsent", "2")]
        [DataRow("orLocal", "2")]
        [DataRow("orConstant", "2")]
        [DataRow("orNested", "2")]
        [DataRow("orStatic", "2")]
        [DataRow("orValue", "2")]
        [DataRow("orCollectionProperty", "2")]
        [DataRow("orBothCaptured", "2")]
        // AC2
        [DataRow("orCodesCase", "2")]
        [DataRow("orNullItem", "1,2,3")]
        [DataRow("orComparerSet", "1,2,3")]
        [DataRow("andComparerSet", "2")]
        [DataRow("orArrayAbsent", "2")]
        [DataRow("andArrayPresent", "2")]
        [DataRow("orCapturedReceiverCase", "2")]
        [DataRow("orLocalReceiverCase", "2")]
        // AC6
        [DataRow("containsThisFilter", "2")]
        [DataRow("containsNestedCaptured", "3")]
        [DataRow("startsWithThisFilter", "2")]
        // AC8
        [DataRow("optionalFilterNull", "1,2,3")]
        [DataRow("optionalThisFilterNull", "1,2,3")]
        [DataRow("optionalStartsWithNull", "1,2,3")]
        [DataRow("optionalReceiverNull", "1,2,3")]
        [DataRow("containsNullValue", "2", "D8")]
        [DataRow("startsWithNullValue", "2", "D8")]
        [DataRow("notContainsNullValue", "2", "D8")]
        // AC10
        [DataRow("idsX", "1,2")]
        [DataRow("notIdsX", "3")]
        [DataRow("nullableX", "1")]
        [DataRow("emptyX", "")]
        [DataRow("startsWithX", "1")]
        [DataRow("containsX", "3")]
        [DataRow("arrayX", "1,2")]
        public void Query_ReturnsWhatCSharpSelects(string key, string expected, string knownDivergence = null)
        {
            var predicate = Predicate(key);
            AssertBinding(key, predicate);
            if (knownDivergence == null)
                Assert.AreEqual(expected, Ids(Rows.Where(predicate.Compile())), key + ": the DataRow disagrees with LINQ to objects");

            WithTable((provider, log) =>
            {
                Assert.AreEqual(expected, Ids(provider.Query(predicate)), key + " query");
                Assert.AreEqual(expected, Ids(provider.QueryAsync(predicate).GetAwaiter().GetResult()), key + " queryAsync");
                Assert.AreEqual(expected, Ids(provider.Query<CvRow>().Where(predicate).ToList()), key + " linq");
                var expectedCount = expected.Length == 0 ? 0 : expected.Split(',').Length;
                Assert.AreEqual(expectedCount, provider.Query<CvRow>().Count(predicate), key + " count");
            });
        }

        #endregion

        #region Delete

        private string DeleteAndReadDeleted(OrmDataProvider provider, Expression<Func<CvRow, bool>> predicate, bool async)
        {
            var transactional = (ISqlOrmProvider)provider;
            transactional.BeginTransaction();
            try
            {
                if (async)
                    provider.DeleteAsync(predicate).GetAwaiter().GetResult();
                else
                    provider.Delete(predicate);
                var remaining = Remaining(provider);
                return string.Join(",", Rows.Select(r => r.Id).Where(id => !remaining.Contains(id.ToString(CultureInfo.InvariantCulture))));
            }
            finally
            {
                transactional.RollbackTransaction();
            }
        }

        [DataTestMethod]
        [DataRow("orAbsent", "2")]
        [DataRow("andAbsent", "")]
        [DataRow("andNotAbsent", "2")]
        [DataRow("orLocal", "2")]
        [DataRow("orConstant", "2")]
        [DataRow("orNested", "2")]
        [DataRow("orStatic", "2")]
        [DataRow("orValue", "2")]
        [DataRow("orCollectionProperty", "2")]
        [DataRow("orBothCaptured", "2")]
        [DataRow("andPresent", "2")]
        [DataRow("andNotPresent", "")]
        [DataRow("orCodesCase", "2")]
        [DataRow("andNullItem", "2")]
        [DataRow("andComparerSet", "2")]
        [DataRow("orArrayAbsent", "2")]
        [DataRow("andArrayPresent", "2")]
        [DataRow("orCapturedReceiverCase", "2")]
        [DataRow("orLocalReceiverCase", "2")]
        [DataRow("containsThisFilter", "2")]
        [DataRow("startsWithThisFilter", "2")]
        [DataRow("containsNullValue", "2", "D8")]
        [DataRow("startsWithNullValue", "2", "D8")]
        [DataRow("idsX", "1,2")]
        [DataRow("notIdsX", "3")]
        [DataRow("nullableX", "1")]
        [DataRow("startsWithX", "1")]
        [DataRow("containsX", "3")]
        [DataRow("arrayX", "1,2")]
        public void Delete_DeletesWhatCSharpSelects(string key, string expected, string knownDivergence = null)
        {
            var predicate = Predicate(key);
            AssertBinding(key, predicate);
            if (knownDivergence == null)
                Assert.AreEqual(expected, Ids(Rows.Where(predicate.Compile())), key + ": the DataRow disagrees with LINQ to objects");

            WithTable((provider, log) =>
            {
                Assert.AreEqual(expected, DeleteAndReadDeleted(provider, predicate, false), key + " sync");
                Assert.AreEqual(expected, DeleteAndReadDeleted(provider, predicate, true), key + " async");
            });
        }

        [DataTestMethod]
        [DataRow("aloneAbsent")]
        [DataRow("aloneNotAbsent")]
        public void Delete_RowIndependentAlone_IsRejected(string key)
        {
            var predicate = Predicate(key);
            WithTable((provider, log) =>
            {
                foreach (var async in new[] { false, true })
                {
                    var transactional = (ISqlOrmProvider)provider;
                    transactional.BeginTransaction();
                    try
                    {
                        var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                        {
                            if (async)
                                provider.DeleteAsync(predicate).GetAwaiter().GetResult();
                            else
                                provider.Delete(predicate);
                        }, key + (async ? " async" : " sync"));
                        Assert.AreEqual(NoColumnMessage, ex.Message);
                        Assert.AreEqual(3, Remaining(provider).Count, key + ": rows were deleted");
                    }
                    finally
                    {
                        transactional.RollbackTransaction();
                    }
                }
            });
        }

        #endregion

        #region Rejections

        [DataTestMethod]
        // AC4
        [DataRow("nestedParamItem", "QD", ItemMessage, null)]
        [DataRow("lengthItem", "QD", ItemMessage, null)]
        [DataRow("convertItem", "QD", ItemMessage, null)]
        [DataRow("emptyNestedParamItem", "QD", ItemMessage, null)]
        [DataRow("nullCollection", "QD", NullCollectionMessage, null)]
        // AC5
        [DataRow("startsWithCapturedReceiver", "QD", "StartsWith() is translated only on a property of the entity: ", null)]
        [DataRow("endsWithCapturedReceiver", "QD", "EndsWith() is translated only on a property of the entity: ", null)]
        [DataRow("startsWithLocalReceiver", "QD", "StartsWith() is translated only on a property of the entity: ", null)]
        [DataRow("startsWithStaticReceiver", "QD", "StartsWith() is translated only on a property of the entity: ", null)]
        [DataRow("startsWithNestedParamReceiver", "QD", "StartsWith() is translated only on a property of the entity: ", null)]
        // AC7
        [DataRow("codeEqualsLengthToString", "Q", ToStringMessage, null)]
        [DataRow("childNameContains", "Q", ContainsReceiverMessage, null)]
        // AC6, D16
        [DataRow("userTypeToString", "QD", ToStringMessage, null)]
        [DataRow("objectDeclaredToString", "QD", ToStringMessage, null)]
        [DataRow("providerToString", "QD", ToStringMessage, null)]
        [DataRow("staticConvertToString", "QD", ToStringMessage, null)]
        [DataRow("orArithmeticToString", "QD", ToStringMessage, null)]
        [DataRow("orIndexToString", "QD", ToStringMessage, null)]
        [DataRow("orMethodToString", "QD", ToStringMessage, null)]
        [DataRow("orCoalesceToString", "QD", ToStringMessage, null)]
        [DataRow("orStructToString", "QD", ToStringMessage, null)]
        [DataRow("orNullableStructToString", "QD", ToStringMessage, null)]
        [DataRow("orSealedClassToString", "QD", ToStringMessage, null)]
        [DataRow("orPairToString", "QD", ToStringMessage, null)]
        [DataRow("objectHoldingStringToString", "QD", ToStringMessage, null)]
        [DataRow("providerStringToString", "QD", ToStringMessage, null)]
        [DataRow("interfaceHoldingStringToString", "QD", ToStringMessage, null)]
        // AC8
        [DataRow("throwingItem", "QD", CouldNotEvaluateMessage, "NullReferenceException")]
        [DataRow("throwingCollection", "QD", CouldNotEvaluateMessage, "NullReferenceException")]
        // D16: a ToString that doesn't read the row is rejected, never evaluated.
        [DataRow("nullStringToString", "QD", ToStringMessage, null)]
        [DataRow("codeEqualsThisCodeToString", "QD", ToStringMessage, null)]
        [DataRow("codeEqualsNestedToString", "QD", ToStringMessage, null)]
        [DataRow("orCapturedToString05", "QD", ToStringMessage, null)]
        [DataRow("orPropertyToString05", "QD", ToStringMessage, null)]
        [DataRow("orFormattedToString", "QD", ToStringMessage, null)]
        [DataRow("orNullableToStringIsNull", "QD", ToStringMessage, null)]
        [DataRow("orNullableToStringIsEmpty", "QD", ToStringMessage, null)]
        [DataRow("intColumnToStringEqCaptured", "QD", ToStringMessage, null)]
        public void Rejected_BeforeAnyCommand(string key, string paths, string messagePrefix, string innerType)
        {
            var predicate = Predicate(key);
            WithTable((provider, log) =>
            {
                var calls = new List<KeyValuePair<string, Action>>
                {
                    new KeyValuePair<string, Action>("query", () => provider.Query(predicate)),
                    new KeyValuePair<string, Action>("queryAsync", () => provider.QueryAsync(predicate).GetAwaiter().GetResult()),
                    new KeyValuePair<string, Action>("linq", () => provider.Query<CvRow>().Where(predicate).ToList()),
                    new KeyValuePair<string, Action>("count", () => provider.Query<CvRow>().Count(predicate)),
                };
                if (paths.Contains("D"))
                {
                    calls.Add(new KeyValuePair<string, Action>("delete", () => provider.Delete(predicate)));
                    calls.Add(new KeyValuePair<string, Action>("deleteAsync", () => provider.DeleteAsync(predicate).GetAwaiter().GetResult()));
                }

                foreach (var call in calls)
                {
                    var transactional = (ISqlOrmProvider)provider;
                    var isDelete = call.Key.StartsWith("delete", StringComparison.Ordinal);
                    if (isDelete)
                        transactional.BeginTransaction();
                    try
                    {
                        log.Clear();
                        var ex = Assert.ThrowsException<NotSupportedException>(call.Value, key + " " + call.Key);
                        StringAssert.StartsWith(ex.Message, messagePrefix, key + " " + call.Key);
                        if (innerType != null)
                            Assert.AreEqual(innerType, ex.InnerException?.GetType().Name, key + " " + call.Key + ": inner exception");
                        AssertNothingSent(log, key + " " + call.Key);
                        Assert.AreEqual(3, Remaining(provider).Count, key + " " + call.Key + ": rows were deleted");
                    }
                    finally
                    {
                        if (isDelete)
                            transactional.RollbackTransaction();
                    }
                }
            });
        }

        #endregion

        #region SQL text

        [DataTestMethod]
        [DataRow("orAbsent", "OR 1=0)", 1)]
        [DataRow("andPresent", "AND NOT 1=0)", 1)]
        [DataRow("andNotPresent", "AND NOT NOT 1=0)", 1)]
        [DataRow("andArrayPresent", "AND NOT 1=0)", 1)]
        [DataRow("orLocalReceiverCase", "OR 1=0)", 1)]
        [DataRow("optionalFilterNull", "name LIKE NULL", 1)]
        [DataRow("optionalReceiverNull", "OR NULL LIKE NULL)", 1)]
        [DataRow("notContainsNullValue", "OR NOT " + Table + ".name LIKE NULL)", 1)]
        public void Sql_RowIndependentContains_IsALiteral(string key, string fragment, int parameters)
        {
            var predicate = Predicate(key);
            WithTable((provider, log) =>
            {
                provider.Query(predicate);
                var command = log.FirstOrDefault(e => e != null && e.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase) >= 0);
                Assert.IsNotNull(command, key + ": no SELECT logged");
                var where = Normalize(command);
                StringAssert.Contains(where, fragment, key);
                Assert.IsFalse(where.Contains(" IN ("), key + ": the evaluated Contains was sent as an IN list: " + where);
                var sentParameters = log.Count(e => e != null && Regex.IsMatch(e, @"^@p__linq__\d+:"));
                Assert.AreEqual(parameters, sentParameters, key + ": parameters sent: " + string.Join(" | ", log.Select(Normalize)));
            });
        }

        #endregion

        #region Projections (AC13)

        [DataTestMethod]
        [DataRow("labelAbsent", "1:out,2:out,3:out")]
        [DataRow("labelX", "1:in,2:in,3:out")]
        public void Projection_GetsCSharpsValue(string key, string expected)
        {
            var projection = Projection(key);
            Assert.AreEqual(expected, string.Join(",", Rows.Select(projection.Compile()).OrderBy(r => r.Id).Select(r => r.Id + ":" + r.Label)),
                key + ": the DataRow disagrees with LINQ to objects");
            WithTable((provider, log) =>
            {
                var actual = provider.Query<CvRow>().Select(projection).ToList().OrderBy(r => r.Id).Select(r => r.Id + ":" + r.Label);
                Assert.AreEqual(expected, string.Join(",", actual), key);
            });
        }

        [TestMethod]
        public void Projection_BoolValue_GetsCSharpsValue()
        {
            if (!SupportsPredicateAsValue)
                Assert.Inconclusive("A predicate in a value position is a syntax error on this server (plan §8).");
            var projection = Projection("flagAbsent");
            WithTable((provider, log) =>
            {
                var actual = provider.Query<CvRow>().Select(projection).ToList().OrderBy(r => r.Id).Select(r => r.Id + ":" + r.Flag);
                Assert.AreEqual("1:False,2:False,3:False", string.Join(",", actual));
            });
        }

        [DataTestMethod]
        [DataRow("labelNestedParam", ItemMessage)]
        [DataRow("labelObjectToString", ToStringMessage)]
        [DataRow("labelProviderToString", ToStringMessage)]
        [DataRow("labelFormatted", ToStringMessage)]
        public void Projection_Rejected_BeforeAnyCommand(string key, string messagePrefix)
        {
            var projection = Projection(key);
            WithTable((provider, log) =>
            {
                log.Clear();
                var ex = Assert.ThrowsException<NotSupportedException>(() => provider.Query<CvRow>().Select(projection).ToList(), key);
                StringAssert.StartsWith(ex.Message, messagePrefix, key);
                AssertNothingSent(log, key);
            });
        }

        #endregion

        #region All (A1)

        /// <summary>
        /// All treats a row whose predicate is unknown in SQL as failing, as C# fails it (or throws): a null search value
        /// (D8) or a nullable column compared with a value. Translated as NOT EXISTS(... WHERE NOT (p)), an unknown row
        /// counted as satisfied.
        /// </summary>
        [DataTestMethod]
        [DataRow("allNotContainsNull", false, "D8")]
        [DataRow("allContainsNull", false, "D8")]
        [DataRow("allOrContainsNull", false, "D8")]
        [DataRow("allRowIndependentNull", false, "D8")]
        [DataRow("allNullableGreaterThanZero", false, null)]
        [DataRow("allIdPositive", true, null)]
        [DataRow("allNotIdsX", false, null)]
        public void All_TreatsUnknownAsFailing(string key, bool expected, string knownDivergence)
        {
            var predicate = Predicate(key);
            if (knownDivergence == null)
                Assert.AreEqual(expected, Rows.All(predicate.Compile()), key + ": the DataRow disagrees with LINQ to objects");
            WithTable((provider, log) => Assert.AreEqual(expected, provider.Query<CvRow>().All(predicate), key));
        }

        #endregion
    }
}
