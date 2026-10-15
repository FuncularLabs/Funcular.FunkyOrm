using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.Tests.CapturedValues
{
    /// <summary>
    /// DB-free tests of a provider's expression translator (<c>TranslateMethodCall</c>, <c>TranslateDateMember</c>):
    /// docs/plans/WHERE_CAPTURED_VALUES_PLAN.md §4.1 item 2 and §4.2's <c>TranslatorDirectHarness</c> rows. Each test
    /// builds a lambda, takes its body's call, and translates it with <c>getColumnName = p =&gt; "c." + p.Name</c>.
    /// This file is compiled into SqlServer.Tests and linked into PostgreSql.Tests, MySql.Tests and Sqlite.Tests; one
    /// derived [TestClass] per provider supplies the translator and the dialect's patterns.
    /// </summary>
    public abstract class TranslatorDirectHarness
    {
        #region Messages (plan §5.3)

        protected const string ItemMessage =
            "Collection Contains is translated only as <property> IN (<values>), with a property of the entity as its item and a collection that doesn't read the row: ";
        protected const string NullCollectionMessage = "The collection of Contains is null: ";
        protected const string NotACollectionMessage = "Contains is translated only on a collection: ";
        protected const string ContainsReceiverMessage =
            "Contains() is translated only on a property of the entity, or on a value that doesn't read the row: ";
        protected const string ToStringMessage =
            "ToString() is translated only on a property of the entity; for a value that doesn't read the row, compute the string before the query: ";
        protected const string CouldNotEvaluateMessage = "Could not evaluate expression: ";
        protected const string ParameterReferencesMessage = "Collection Contains with parameter references is not supported: ";
        protected const string StringContainsValueMessage = "Unsupported expression in string Contains: ";
        /// <summary>FD-03 (D4): a column's <c>ToString()</c> the dialect can't render as .NET's text.</summary>
        protected const string ColumnToStringPrefix = "ToString() on a column of type ";
        /// <summary>FD-03: the clause every LIKE pattern ends with.</summary>
        protected const string Escape = " ESCAPE '!'";

        protected static string ReceiverMessage(string method) => method + "() is translated only on a property of the entity: ";

        protected static string ArgumentMessage(string method) => "Unsupported expression in " + method + ": ";

        #endregion

        #region Provider seam

        /// <summary>One translation: the SQL text and the parameters it created, in order.</summary>
        protected sealed class Translation
        {
            public Translation(string sql, IList<KeyValuePair<string, object>> parameters)
            {
                Sql = sql;
                Parameters = parameters;
            }

            public string Sql { get; }
            public IList<KeyValuePair<string, object>> Parameters { get; }
            public object Value(int index) => Parameters[index].Value is DBNull ? null : Parameters[index].Value;
            public string Name(int index) => Parameters[index].Key;
        }

        protected abstract Translation TranslateMethodCall(MethodCallExpression node, Func<PropertyInfo, string> getColumnName);

        protected abstract Translation TranslateDateMember(MemberExpression node, Func<PropertyInfo, string> getColumnName);

        /// <summary>The provider's <c>TranslateMemberOfMember</c> (FD-01): a date part or a string length on a property of the row, else a rejection.</summary>
        protected abstract Translation TranslateMemberOfMember(MemberExpression node, Func<PropertyInfo, string> getColumnName);

        /// <summary>The dialect's character-length function over <paramref name="column"/>.</summary>
        protected abstract string LengthSql(string column);

        /// <summary>The dialect's cast of an integer <paramref name="column"/> to text (FD-03, D4).</summary>
        protected abstract string CastToTextSql(string column);

        protected abstract string StartsWithPattern(string parameterName);

        protected abstract string EndsWithPattern(string parameterName);

        protected abstract string YearSql(string column);

        protected abstract string MonthSql(string column);

        protected abstract string DaySql(string column);

        #endregion

        #region Fixtures

        public class DtInner
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        /// <summary>A non-enumerable type with its own Contains.</summary>
        public class DtRange
        {
            public bool Contains(int value) => value > 0;
        }

        public class DtRow
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Code { get; set; }
            public int? NullableId { get; set; }
            public DateTime? Birth { get; set; }
            public DateTime Posted { get; set; }
            public DateTimeOffset Stamp { get; set; }
            public decimal Amount { get; set; }
            public List<int> Tags { get; set; }
            public DtRange Window { get; set; }
            public DtInner Child { get; set; }
            public int PublicField;
        }

        public class Request
        {
            public int Id { get; set; }
            public string Code { get; set; }
            public string Name { get; set; }
            public int? NullableId { get; set; }
            public List<int> Ids { get; set; }
        }

        /// <summary>Counts reads of its properties, so a test can prove what was (not) evaluated.</summary>
        public class Counting
        {
            public int Reads;
            private readonly List<int> _ids = new List<int> { 1, 2 };
            public List<int> Ids { get { Reads++; return _ids; } }
            public int Five { get { Reads++; return 5; } }
        }

        public class Settings
        {
            public override string ToString() => "on";
        }

        public static class StaticHolder
        {
            public static string Name => "alpha";
        }

        public class FakeDbException : DbException
        {
            public FakeDbException() : base("server error") { }
        }

        public class OddToString
        {
            public new object ToString() => "odd";
        }

        public static class UserExtensions
        {
            public static int Contains(IEnumerable<int> source, int value) => 1;
        }

        /// <summary>Returns calls over its own properties, so the expression captures <c>this</c>.</summary>
        public class Holder
        {
            public string Filter { get; set; } = "zz";
            public string Code { get; set; } = "b";
            public MethodCallExpression NameContainsFilter() => Body<DtRow>(x => x.Name.Contains(Filter));
            public MethodCallExpression NameStartsWithFilter() => Body<DtRow>(x => x.Name.StartsWith(Filter));
            public MethodCallExpression CodeToString() => Call(() => Code.ToString());
        }

        public enum Day { Sunday, Monday }

        protected static MethodCallExpression Body<T>(Expression<Func<T, bool>> predicate) => (MethodCallExpression)predicate.Body;

        protected static MethodCallExpression Call<T>(Expression<Func<T>> expression) => (MethodCallExpression)expression.Body;

        protected static MethodCallExpression CallOnRow<T>(Expression<Func<DtRow, T>> expression) => (MethodCallExpression)expression.Body;

        private static string Column(PropertyInfo property) => "c." + property.Name;

        protected Translation Translate(MethodCallExpression node) => TranslateMethodCall(node, Column);

        protected void AssertRejected<TException>(MethodCallExpression node, string messagePrefix) where TException : Exception
        {
            var ex = Assert.ThrowsException<TException>(() => TranslateMethodCall(node, Column));
            StringAssert.StartsWith(ex.Message, messagePrefix);
        }

        /// <summary>
        /// Each provider assembly defines its own <c>Funcular.Data.Orm.Exceptions.NullableExpressionException</c>, and
        /// SqlServer.Tests references all four, so the type is asserted by its full name, not by a compile-time type.
        /// </summary>
        protected void AssertRejectedNullableExpression(MethodCallExpression node, string messagePrefix)
        {
            Exception thrown = null;
            try { TranslateMethodCall(node, Column); } catch (Exception ex) { thrown = ex; }
            Assert.IsNotNull(thrown, "no exception was thrown");
            Assert.AreEqual("Funcular.Data.Orm.Exceptions.NullableExpressionException", thrown.GetType().FullName, thrown.Message);
            StringAssert.StartsWith(thrown.Message, messagePrefix);
        }

        private readonly List<int> ids = new List<int> { 1, 2 };
        private readonly List<int> emptyIds = new List<int>();
        private readonly List<long> longs = new List<long> { 1, 2 };
        private readonly List<int> lengths = new List<int> { 4 };
        private readonly List<int?> oneAndNull = new List<int?> { 1, null };
        private readonly Request absent = new Request { Id = 5, Code = "a", Name = "zeta", NullableId = 5, Ids = new List<int> { 1, 2 } };
        private readonly Request present = new Request { Id = 1 };
        private readonly Request bee = new Request { Code = "b" };
        private readonly Request nullRequest = null;
        private readonly Request nullIdsHolder = new Request { Ids = null };
        private readonly string s = "zeta";
        private readonly string nullString = null;
        private readonly int localInt = 5;
        private readonly int five = 5;
        private readonly int? noValue = null;
        private readonly object boxedString = "b";
        private readonly Settings settings = new Settings();
        private readonly Day day = Day.Monday;
        private readonly bool flag = true;
        private readonly string digits = "2";
        private readonly DtRange range = new DtRange();
        private readonly OddToString odd = new OddToString();
        private readonly int zero = 0;

        #endregion

        #region Collection Contains

        [TestMethod]
        public void CollectionContains_RowIndependentItem_FalseSql()
        {
            var result = Translate(Call(() => ids.Contains(absent.Id)));
            Assert.AreEqual("1=0", result.Sql);
            Assert.AreEqual(0, result.Parameters.Count, "neither the item nor the collection is sent");
        }

        [TestMethod]
        public void CollectionContains_RowIndependentItem_TrueSql()
        {
            var result = Translate(Call(() => ids.Contains(present.Id)));
            Assert.AreEqual("NOT 1=0", result.Sql);
            Assert.AreEqual(0, result.Parameters.Count);
        }

        [TestMethod]
        public void CollectionContains_RowIndependent_ReadsCapturedValuesOnce()
        {
            var counting = new Counting();
            Translate(Call(() => counting.Ids.Contains(counting.Five)));
            Assert.AreEqual(2, counting.Reads, "the collection and the item are each read once");
        }

        [TestMethod]
        public void CollectionContains_MemberItem_In()
        {
            var result = Translate(CallOnRow(x => ids.Contains(x.Id)));
            Assert.AreEqual($"c.Id IN ({result.Name(0)},{result.Name(1)})", result.Sql);
            CollectionAssert.AreEqual(new object[] { 1, 2 }, result.Parameters.Select(p => p.Value).ToArray());
        }

        [TestMethod]
        public void CollectionContains_EmptyCollection_OneEqualsZero()
        {
            var result = Translate(CallOnRow(x => emptyIds.Contains(x.Id)));
            Assert.AreEqual("1=0", result.Sql);
        }

        [TestMethod]
        public void CollectionContains_NullValuesSkipped()
        {
            // N1 (§8): the IN list drops a null, as today.
            var result = Translate(CallOnRow(x => oneAndNull.Contains(x.NullableId)));
            Assert.AreEqual($"c.NullableId IN ({result.Name(0)})", result.Sql);
            Assert.AreEqual(1, result.Parameters.Count);
        }

        [TestMethod]
        public void CollectionContains_ConstantCollection_In()
        {
            var list = new List<int> { 3 };
            var x = Expression.Parameter(typeof(DtRow), "x");
            var call = Expression.Call(Expression.Constant(list), typeof(List<int>).GetMethod("Contains"),
                Expression.Property(x, nameof(DtRow.Id)));
            var result = Translate(call);
            Assert.AreEqual($"c.Id IN ({result.Name(0)})", result.Sql);
        }

        [TestMethod]
        public void CollectionContains_ImplicitUnary_In()
        {
            var call = SpanContains(Expression.Convert(Expression.Constant(new[] { 1, 2 }), typeof(ReadOnlySpan<int>), ToSpan()));
            var result = Translate(call);
            Assert.AreEqual($"c.Id IN ({result.Name(0)},{result.Name(1)})", result.Sql);
        }

        [TestMethod]
        public void CollectionContains_ImplicitCall_In()
        {
            var call = SpanContains(Expression.Call(ToSpan(), Expression.Constant(new[] { 1, 2 })));
            var result = Translate(call);
            Assert.AreEqual($"c.Id IN ({result.Name(0)},{result.Name(1)})", result.Sql);
        }

        private static MethodInfo ToSpan() => typeof(ReadOnlySpan<int>).GetMethod("op_Implicit", new[] { typeof(int[]) });

        /// <summary>C# 14's <c>arr.Contains(x.Id)</c>, built by hand so that every test project sees that shape.</summary>
        private static MethodCallExpression SpanContains(Expression span)
        {
            var contains = typeof(MemoryExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "Contains" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2
                             && m.GetParameters()[0].ParameterType.IsGenericType
                             && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>))
                .MakeGenericMethod(typeof(int));
            var x = Expression.Parameter(typeof(DtRow), "x");
            return Expression.Call(contains, span, Expression.Property(x, nameof(DtRow.Id)));
        }

        [TestMethod]
        public void CollectionContains_NestedParamItem_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => ids.Contains(x.Child.Id)), ItemMessage);

        [TestMethod]
        public void CollectionContains_NestedParamItemEmptyCollection_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => emptyIds.Contains(x.Child.Id)), ItemMessage);

        [TestMethod]
        public void CollectionContains_ConvertedItem_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => longs.Contains(x.Id)), ItemMessage);

        [TestMethod]
        public void CollectionContains_LengthItem_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => lengths.Contains(x.Name.Length)), ItemMessage);

        [TestMethod]
        public void CollectionContains_NestedParamItem_CollectionNotRead()
        {
            var counting = new Counting();
            AssertRejected<NotSupportedException>(CallOnRow(x => counting.Ids.Contains(x.Child.Id)), ItemMessage);
            Assert.AreEqual(0, counting.Reads, "a rejected item runs no user code");
        }

        [TestMethod]
        public void CollectionContains_ValueUnwrap_NullableExpressionException() =>
            AssertRejectedNullableExpression(CallOnRow(x => ids.Contains(x.NullableId.Value)), "Do not use '.Value'");

        [TestMethod]
        public void CollectionContains_ValueUnwrapEmptyCollection_NullableExpressionException() =>
            AssertRejectedNullableExpression(CallOnRow(x => emptyIds.Contains(x.NullableId.Value)), "Do not use '.Value'");

        [TestMethod]
        public void CollectionContains_FieldItem_InvalidOperationBeforeCollectionRead()
        {
            var counting = new Counting();
            AssertRejected<InvalidOperationException>(CallOnRow(x => counting.Ids.Contains(x.PublicField)),
                "Member PublicField is not a property.");
            Assert.AreEqual(0, counting.Reads, "the field item is rejected before the collection is read");
        }

        [TestMethod]
        public void CollectionContains_CollectionReadsRow_ParameterReferences() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => x.Tags.Contains(x.Id)), ParameterReferencesMessage);

        [TestMethod]
        public void CollectionContains_RowIndependentItemRowCollection_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => x.Tags.Contains(5)), ItemMessage);

        [TestMethod]
        public void CollectionContains_NullCollection_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => nullIdsHolder.Ids.Contains(x.Id)), NullCollectionMessage);

        [TestMethod]
        public void CollectionContains_NonEnumerableReceiver_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => range.Contains(x.Id)), NotACollectionMessage);

        [TestMethod]
        public void CollectionContains_StringAsCharSequence_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => Enumerable.Contains(x.Name, 'b')), "Method call Contains is not supported.");

        [TestMethod]
        public void CollectionContains_NonBoolCall_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => UserExtensions.Contains(ids, x.Id)), "Method call Contains is not supported.");

        [TestMethod]
        public void CollectionContains_LazyThrowingCollection_NotSupportedWithInner()
        {
            var lazy = ids.Select(i => i / zero);
            var ex = Assert.ThrowsException<NotSupportedException>(() => Translate(CallOnRow(x => lazy.Contains(x.Id))));
            StringAssert.StartsWith(ex.Message, CouldNotEvaluateMessage);
            Assert.IsInstanceOfType(ex.InnerException, typeof(DivideByZeroException));
        }

        // FV1-B3: a FunkyORM query used as the collection runs a query while it is enumerated; a database error, or the
        // query's own NotSupportedException, comes out as it would have without the wrap.
        [TestMethod]
        public void CollectionContains_LazyDbException_PassesThrough()
        {
            var lazy = ids.Select(i => i == 1 ? throw new FakeDbException() : i);
            Assert.ThrowsException<FakeDbException>(() => Translate(CallOnRow(x => lazy.Contains(x.Id))));
        }

        [TestMethod]
        public void CollectionContains_LazyExceptionWithDbInner_PassesThrough()
        {
            var lazy = ids.Select(i => i == 1 ? throw new InvalidOperationException("query failed", new FakeDbException()) : i);
            var ex = Assert.ThrowsException<InvalidOperationException>(() => Translate(CallOnRow(x => lazy.Contains(x.Id))));
            Assert.IsInstanceOfType(ex.InnerException, typeof(FakeDbException));
        }

        [TestMethod]
        public void CollectionContains_LazyExceptionWithNonDbInner_NotSupportedWithInner()
        {
            // Only a database error or a NotSupportedException passes through; any other exception, inner exception
            // or not, is the caller's (D9).
            var lazy = ids.Select(i => i == 1 ? throw new InvalidOperationException("caller's code", new FormatException()) : i);
            var ex = Assert.ThrowsException<NotSupportedException>(() => Translate(CallOnRow(x => lazy.Contains(x.Id))));
            StringAssert.StartsWith(ex.Message, CouldNotEvaluateMessage);
            Assert.IsInstanceOfType(ex.InnerException, typeof(InvalidOperationException));
        }

        [TestMethod]
        public void CollectionContains_RowIndependentLazyDbException_Wrapped()
        {
            // A collection read to answer a Contains before the query is wrapped like any value, database errors included
            // (D9); only the IN path's enumeration lets a database error through.
            var lazy = ids.Select(i => i == 1 ? throw new FakeDbException() : i);
            var ex = Assert.ThrowsException<NotSupportedException>(() => Translate(Call(() => lazy.Contains(5))));
            StringAssert.StartsWith(ex.Message, CouldNotEvaluateMessage);
            Assert.IsInstanceOfType(ex.InnerException, typeof(FakeDbException));
        }

        [TestMethod]
        public void CollectionContains_LazyNotSupported_PassesThrough()
        {
            var lazy = ids.Select(i => i == 1 ? throw new NotSupportedException("the nested query's own message") : i);
            var ex = Assert.ThrowsException<NotSupportedException>(() => Translate(CallOnRow(x => lazy.Contains(x.Id))));
            Assert.AreEqual("the nested query's own message", ex.Message);
        }

        [TestMethod]
        public void CollectionContains_ThrowingCollection_NotSupportedWithInner()
        {
            var ex = Assert.ThrowsException<NotSupportedException>(() => Translate(CallOnRow(x => nullRequest.Ids.Contains(x.Id))));
            StringAssert.StartsWith(ex.Message, CouldNotEvaluateMessage);
            Assert.IsInstanceOfType(ex.InnerException, typeof(NullReferenceException));
        }

        #endregion

        #region String Contains

        [TestMethod]
        public void StringContains_ConstantValue()
        {
            var result = Translate(CallOnRow(x => x.Name.Contains("a")));
            Assert.AreEqual($"c.Name LIKE {result.Name(0)}{Escape}", result.Sql);
            Assert.AreEqual("%a%", result.Value(0));
        }

        // FD-03: LIKE metacharacters in the value are escaped with '!' and the pattern names it; a backslash isn't
        // special once ESCAPE is explicit, so it isn't escaped.
        [TestMethod]
        public void StringContains_WildcardsEscaped()
        {
            var result = Translate(CallOnRow(x => x.Name.Contains("a_b%c[d!e\\f")));
            Assert.AreEqual($"c.Name LIKE {result.Name(0)}{Escape}", result.Sql);
            Assert.AreEqual("%a!_b!%c![d!!e\\f%", result.Value(0));
        }

        [TestMethod]
        public void StringContains_CapturedWildcardEscaped()
        {
            var needle = "50%";
            var result = Translate(CallOnRow(x => x.Name.Contains(needle)));
            Assert.AreEqual("%50!%%", result.Value(0));
        }

        [TestMethod]
        public void StringContains_CapturedLocalValue()
        {
            var result = Translate(CallOnRow(x => x.Name.Contains(s)));
            Assert.AreEqual("%zeta%", result.Value(0));
        }

        [TestMethod]
        public void StringContains_CapturedThisPropertyValue()
        {
            var result = Translate(new Holder().NameContainsFilter());
            Assert.AreEqual($"c.Name LIKE {result.Name(0)}{Escape}", result.Sql);
            Assert.AreEqual("%zz%", result.Value(0), "a property of a captured this is read as C# reads it");
        }

        [TestMethod]
        public void StringContains_NestedCapturedValue()
        {
            var result = Translate(CallOnRow(x => x.Name.Contains(absent.Name)));
            Assert.AreEqual("%zeta%", result.Value(0));
        }

        [TestMethod]
        public void StringContains_StaticValue()
        {
            var result = Translate(CallOnRow(x => x.Name.Contains(StaticHolder.Name)));
            Assert.AreEqual("%alpha%", result.Value(0));
        }

        [TestMethod]
        public void StringContains_NullValue_LikeNull()
        {
            var captured = Translate(CallOnRow(x => x.Name.Contains(nullString)));
            Assert.AreEqual("c.Name LIKE NULL", captured.Sql);
            Assert.AreEqual(0, captured.Parameters.Count);
            var constant = Translate(CallOnRow(x => x.Name.Contains((string)null)));
            Assert.AreEqual("c.Name LIKE NULL", constant.Sql);
        }

        [TestMethod]
        public void StringContains_ValueReadsRow_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => x.Name.Contains(x.Code)), StringContainsValueMessage);

        [TestMethod]
        public void StringContains_ValueMethodCall_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => x.Name.Contains(s.Trim())), StringContainsValueMessage);

        [TestMethod]
        public void StringContains_RowIndependent_FalseSql()
        {
            var result = Translate(Call(() => s.Contains("ZETA")));
            Assert.AreEqual("1=0", result.Sql, "answered as C# answers it: ordinal");
            Assert.AreEqual(0, result.Parameters.Count);
        }

        [TestMethod]
        public void StringContains_RowIndependent_TrueSql()
        {
            Assert.AreEqual("NOT 1=0", Translate(Call(() => s.Contains("et"))).Sql);
            Assert.AreEqual("NOT 1=0", Translate(Call(() => absent.Name.Contains("et"))).Sql);
        }

        [TestMethod]
        public void StringContains_RowIndependentNullReceiver_Unknown() =>
            Assert.AreEqual("NULL LIKE NULL", Translate(Call(() => nullString.Contains("a"))).Sql);

        [TestMethod]
        public void StringContains_RowIndependentNullValue_Unknown() =>
            Assert.AreEqual("NULL LIKE NULL", Translate(Call(() => s.Contains(nullString))).Sql);

        [TestMethod]
        public void StringContains_ReceiverMemberOfMember_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => x.Child.Name.Contains("a")), ContainsReceiverMessage);

        #endregion

        #region StartsWith / EndsWith

        [TestMethod]
        public void StartsWith_Column()
        {
            var result = Translate(CallOnRow(x => x.Name.StartsWith("al")));
            Assert.AreEqual($"c.Name LIKE {StartsWithPattern(result.Name(0))}{Escape}", result.Sql);
            Assert.AreEqual("al", result.Value(0));
        }

        [TestMethod]
        public void EndsWith_Column()
        {
            var result = Translate(CallOnRow(x => x.Name.EndsWith("ha")));
            Assert.AreEqual($"c.Name LIKE {EndsWithPattern(result.Name(0))}{Escape}", result.Sql);
            Assert.AreEqual("ha", result.Value(0));
        }

        [TestMethod]
        public void StartsWith_WildcardsEscaped()
        {
            var result = Translate(CallOnRow(x => x.Name.StartsWith("a%_[!")));
            Assert.AreEqual($"c.Name LIKE {StartsWithPattern(result.Name(0))}{Escape}", result.Sql);
            Assert.AreEqual("a!%!_![!!", result.Value(0));
        }

        [TestMethod]
        public void EndsWith_BackslashKeptLiteral()
        {
            var result = Translate(CallOnRow(x => x.Name.EndsWith("\\b")));
            Assert.AreEqual($"c.Name LIKE {EndsWithPattern(result.Name(0))}{Escape}", result.Sql);
            Assert.AreEqual("\\b", result.Value(0));
        }

        [TestMethod]
        public void StartsWith_CapturedThisPropertyArgument()
        {
            var result = Translate(new Holder().NameStartsWithFilter());
            Assert.AreEqual($"c.Name LIKE {StartsWithPattern(result.Name(0))}{Escape}", result.Sql);
            Assert.AreEqual("zz", result.Value(0));
        }

        [TestMethod]
        public void StartsWith_NestedCapturedArgument()
        {
            var result = Translate(CallOnRow(x => x.Name.StartsWith(absent.Name)));
            Assert.AreEqual("zeta", result.Value(0));
        }

        [TestMethod]
        public void StartsWith_NullArgument_LikeNull()
        {
            var result = Translate(CallOnRow(x => x.Name.StartsWith(nullString)));
            Assert.AreEqual("c.Name LIKE NULL", result.Sql);
            Assert.AreEqual(0, result.Parameters.Count);
        }

        [TestMethod]
        public void EndsWith_NullArgument_LikeNull() =>
            Assert.AreEqual("c.Name LIKE NULL", Translate(CallOnRow(x => x.Name.EndsWith(nullString))).Sql);

        [TestMethod]
        public void StartsWith_ArgumentReadsRow_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => x.Name.StartsWith(x.Code)), ArgumentMessage("StartsWith"));

        [TestMethod]
        public void StartsWith_CapturedReceiver_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => absent.Name.StartsWith("al")), ReceiverMessage("StartsWith"));

        [TestMethod]
        public void EndsWith_CapturedReceiver_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => absent.Name.EndsWith("ha")), ReceiverMessage("EndsWith"));

        [TestMethod]
        public void StartsWith_LocalReceiver_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => s.StartsWith("al")), ReceiverMessage("StartsWith"));

        [TestMethod]
        public void StartsWith_StaticReceiver_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => StaticHolder.Name.StartsWith("al")), ReceiverMessage("StartsWith"));

        [TestMethod]
        public void StartsWith_NestedParamReceiver_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => x.Child.Name.StartsWith("a")), ReceiverMessage("StartsWith"));

        [TestMethod]
        public void StartsWith_MethodCallReceiver_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => x.Name.Trim().StartsWith("a")), ReceiverMessage("StartsWith"));

        #endregion

        #region ToString

        // FD-03 (D4): an integer column's ToString() is the dialect's cast to text; a nullable one gives '' for null,
        // as int?.ToString() does; a string column is itself; other types and any argument are rejected.
        [TestMethod]
        public void ToString_Column() => Assert.AreEqual(CastToTextSql("c.Id"), Translate(CallOnRow(x => x.Id.ToString())).Sql);

        [TestMethod]
        public void ToString_NullableIntegerColumn_Coalesces() =>
            Assert.AreEqual($"COALESCE({CastToTextSql("c.NullableId")}, '')", Translate(CallOnRow(x => x.NullableId.ToString())).Sql);

        [TestMethod]
        public void ToString_StringColumn_IsTheColumn() => Assert.AreEqual("c.Name", Translate(CallOnRow(x => x.Name.ToString())).Sql);

        [TestMethod]
        public void ToString_NonIntegerColumn_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => x.Amount.ToString()), ColumnToStringPrefix + "Decimal");

        [TestMethod]
        public void ToString_DateColumn_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => x.Posted.ToString()), ColumnToStringPrefix + "DateTime");

        [TestMethod]
        public void ToString_ColumnWithFormat_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => x.Id.ToString("D2")), ColumnToStringPrefix + "Int32 isn't translated with an argument");

        [TestMethod]
        public void ToString_StringColumnWithProvider_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => x.Name.ToString(CultureInfo.InvariantCulture)), ColumnToStringPrefix + "String isn't translated with an argument");

        [TestMethod]
        public void ToString_NullableValue_NullableExpressionException() =>
            AssertRejectedNullableExpression(CallOnRow(x => x.NullableId.Value.ToString()), "Do not use '.Value' on nullable property 'NullableId'");

        [TestMethod]
        public void ToString_NullableValueWithFormat_NullableExpressionException() =>
            AssertRejectedNullableExpression(CallOnRow(x => x.NullableId.Value.ToString("D2")), "Do not use '.Value' on nullable property 'NullableId'");

        /// <summary>D16: a ToString that doesn't read the row is rejected, never evaluated or sent raw.</summary>
        private void AssertRejectedToString(MethodCallExpression call) => AssertRejected<NotSupportedException>(call, ToStringMessage);

        [TestMethod]
        public void ToString_CapturedLocal_NotSupported() => AssertRejectedToString(Call(() => localInt.ToString()));

        [TestMethod]
        public void ToString_Constant_NotSupported() =>
            AssertRejectedToString(Expression.Call(Expression.Constant(5), typeof(int).GetMethod("ToString", Type.EmptyTypes)));

        [TestMethod]
        public void ToString_Formatted_NotSupported() => AssertRejectedToString(Call(() => five.ToString("D2")));

        [TestMethod]
        public void ToString_CapturedThisProperty_NotSupported() => AssertRejectedToString(new Holder().CodeToString());

        [TestMethod]
        public void ToString_NestedCaptured_NotSupported() => AssertRejectedToString(Call(() => bee.Code.ToString()));

        [TestMethod]
        public void ToString_StaticMemberReceiver_NotSupported() => AssertRejectedToString(Call(() => StaticHolder.Name.ToString()));

        [TestMethod]
        public void ToString_NullableNoValue_NotSupported() => AssertRejectedToString(Call(() => noValue.ToString()));

        [TestMethod]
        public void ToString_Enum_NotSupported() => AssertRejectedToString(Call(() => day.ToString()));

        [TestMethod]
        public void ToString_ConditionalReceiver_NotSupported() => AssertRejectedToString(Call(() => (flag ? 1 : 2).ToString()));

        [TestMethod]
        public void ToString_NonStringReturn_NotSupported() => AssertRejectedToString(Call(() => odd.ToString()));

        [TestMethod]
        public void ToString_NullStringReceiver_NotSupported() => AssertRejectedToString(Call(() => nullString.ToString()));

        [TestMethod]
        public void ToString_Captured_NothingRead()
        {
            var counting = new Counting();
            AssertRejectedToString(Call(() => counting.Five.ToString("D2")));
            Assert.AreEqual(0, counting.Reads, "a rejected ToString runs no user code");
        }

        [TestMethod]
        public void ToString_MemberOfMember_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => x.Name.Length.ToString()), ToStringMessage);

        [TestMethod]
        public void ToString_FieldOfParameter_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => x.PublicField.ToString()), ToStringMessage);

        [TestMethod]
        public void ToString_OtherExpression_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => (x.Id + 1).ToString()), ToStringMessage);

        [TestMethod]
        public void ToString_RowArgument_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => five.ToString(x.Code)), ToStringMessage);

        [TestMethod]
        public void ToString_UserType_NotSupported() =>
            AssertRejected<NotSupportedException>(Call(() => settings.ToString()), ToStringMessage);

        [TestMethod]
        public void ToString_ObjectDeclared_NotSupported() =>
            AssertRejected<NotSupportedException>(Call(() => boxedString.ToString()), ToStringMessage);

        [TestMethod]
        public void ToString_FormatProvider_NotSupported() =>
            AssertRejected<NotSupportedException>(Call(() => five.ToString(CultureInfo.InvariantCulture)), ToStringMessage);

        [TestMethod]
        public void ToString_Static_NotSupported() =>
            AssertRejected<NotSupportedException>(Call(() => Convert.ToString(localInt)), ToStringMessage);

        [TestMethod]
        public void ToString_ConcatenatedFormat_NotSupported() =>
            AssertRejected<NotSupportedException>(Call(() => five.ToString("D" + digits)), ToStringMessage);

        [TestMethod]
        public void ToString_ArithmeticReceiver_NotSupported() =>
            AssertRejected<NotSupportedException>(Call(() => (five + 1).ToString()), ToStringMessage);

        #endregion

        #region Date members and other calls

        [TestMethod]
        public void DateMember_YearMonthDay()
        {
            Expression<Func<DtRow, int>> year = x => x.Birth.Value.Year;
            Expression<Func<DtRow, int>> month = x => x.Birth.Value.Month;
            Expression<Func<DtRow, int>> dayPart = x => x.Birth.Value.Day;
            Assert.AreEqual(YearSql("c.Birth"), TranslateDateMember((MemberExpression)year.Body, Column).Sql);
            Assert.AreEqual(MonthSql("c.Birth"), TranslateDateMember((MemberExpression)month.Body, Column).Sql);
            Assert.AreEqual(DaySql("c.Birth"), TranslateDateMember((MemberExpression)dayPart.Body, Column).Sql);
        }

        [TestMethod]
        public void DateMember_OtherPart_NotSupported()
        {
            Expression<Func<DtRow, int>> hour = x => x.Birth.Value.Hour;
            var ex = Assert.ThrowsException<NotSupportedException>(() => TranslateDateMember((MemberExpression)hour.Body, Column));
            StringAssert.StartsWith(ex.Message, "Unsupported property Hour");
        }

        [TestMethod]
        public void DateMember_NotAValueChain_NotSupported()
        {
            Expression<Func<DtRow, int>> length = x => x.Name.Length;
            var ex = Assert.ThrowsException<NotSupportedException>(() => TranslateDateMember((MemberExpression)length.Body, Column));
            StringAssert.StartsWith(ex.Message, "Unsupported date member expression");
        }

        [TestMethod]
        public void UnsupportedMethod_NotSupported() =>
            AssertRejected<NotSupportedException>(CallOnRow(x => x.Name.ToUpper()), "Method call ToUpper is not supported.");

        #endregion

        #region Member of a member (FD-01)

        private static MemberExpression Member<T>(Expression<Func<DtRow, T>> expression) => (MemberExpression)expression.Body;

        private Translation TranslateMember(MemberExpression node) => TranslateMemberOfMember(node, Column);

        private void AssertMemberRejected(MemberExpression node, string member, string receiver)
        {
            var ex = Assert.ThrowsException<NotSupportedException>(() => TranslateMemberOfMember(node, Column));
            StringAssert.StartsWith(ex.Message, "Member '" + member + "' of '" + receiver + "'");
            StringAssert.Contains(ex.Message, member, "the message names the member");
        }

        [TestMethod]
        public void MemberOfMember_NonNullableDateParts()
        {
            Assert.AreEqual(YearSql("c.Posted"), TranslateMember(Member(x => x.Posted.Year)).Sql);
            Assert.AreEqual(MonthSql("c.Posted"), TranslateMember(Member(x => x.Posted.Month)).Sql);
            Assert.AreEqual(DaySql("c.Posted"), TranslateMember(Member(x => x.Posted.Day)).Sql);
        }

        [TestMethod]
        public void MemberOfMember_NullableValueDateParts_SameAsTranslateDateMember()
        {
            Assert.AreEqual(YearSql("c.Birth"), TranslateMember(Member(x => x.Birth.Value.Year)).Sql);
            Assert.AreEqual(MonthSql("c.Birth"), TranslateMember(Member(x => x.Birth.Value.Month)).Sql);
            Assert.AreEqual(DaySql("c.Birth"), TranslateMember(Member(x => x.Birth.Value.Day)).Sql);
        }

        [TestMethod]
        public void MemberOfMember_StringLength() =>
            Assert.AreEqual(LengthSql("c.Name"), TranslateMember(Member(x => x.Name.Length)).Sql);

        [TestMethod]
        public void MemberOfMember_UsesTheColumnItIsGiven()
        {
            // The visitor passes its qualified, remote-aware column resolver; the translator must use it, not the bare name.
            var result = TranslateMemberOfMember(Member(x => x.Posted.Year), p => "t." + p.Name.ToLowerInvariant());
            Assert.AreEqual(YearSql("t.posted"), result.Sql);
        }

        [TestMethod]
        public void MemberOfMember_Hour_NotSupported() => AssertMemberRejected(Member(x => x.Posted.Hour), "Hour", "Posted");

        [TestMethod]
        public void MemberOfMember_Date_NotSupported() => AssertMemberRejected(Member(x => x.Posted.Date), "Date", "Posted");

        [TestMethod]
        public void MemberOfMember_NullableValueHour_NotSupported() => AssertMemberRejected(Member(x => x.Birth.Value.Hour), "Hour", "Birth");

        [TestMethod]
        public void MemberOfMember_DateTimeOffsetYear_NotSupported() => AssertMemberRejected(Member(x => x.Stamp.Year), "Year", "Stamp");

        // (An array's Length is an ArrayLength unary node, not a member access; the visitor rejects it as an unsupported
        // unary operator, as before.)
        [TestMethod]
        public void MemberOfMember_ListCount_NotSupported() => AssertMemberRejected(Member(x => x.Tags.Count), "Count", "Tags");

        [TestMethod]
        public void MemberOfMember_NestedEntityMember_NotSupported() => AssertMemberRejected(Member(x => x.Child.Id), "Id", "Child");

        [TestMethod]
        public void MemberOfMember_DeeperChain_NotSupported() =>
            AssertMemberRejected(Member(x => x.Child.Name.Length), "Length", "x.Child.Name");

        [TestMethod]
        public void MemberOfMember_ReceiverNotARowMember_NotSupported() =>
            AssertMemberRejected(Member(x => (x.Id > 2 ? x.Name : "zz").Length), "Length", "IIF((x.Id > 2), x.Name, \"zz\")");

        #endregion
    }
}
