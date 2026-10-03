// Linked into SqlServer.Tests.NetFramework (net48, MSTest 2.x): keep it C# 7.3-compatible. Tests that build
// MemoryExtensions calls or use post-net48 overloads are under #if NET.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Funcular.Data.Orm.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests.Linq
{
    /// <summary>
    /// DB-free tests of the Core <see cref="RowIndependentEvaluator"/> and its copy of the delete guard's
    /// evaluability rule, <c>CoreEvaluability</c> (docs/plans/WHERE_CAPTURED_VALUES_PLAN.md, §4.2 and §4.3: AC2, AC3,
    /// AC8, AC11).
    /// </summary>
    [TestClass]
    public class RowIndependentEvaluatorTests
    {
        #region Fixtures

        public class Inner
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        public class Request
        {
            public int Id { get; set; }
            public string Code { get; set; }
            public string Name { get; set; }
            public int? NullableId { get; set; }
            public Inner Inner { get; set; }
            public List<int> Ids { get; set; }
        }

        public class Row
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        /// <summary>Counts reads of its properties, so a test can prove nothing was evaluated.</summary>
        public class Counting
        {
            public int Reads;
            private readonly List<int> _ids = new List<int> { 1, 2 };
            public List<int> Ids { get { Reads++; return _ids; } }
            public Settings Settings { get { Reads++; return new Settings(); } }
            public int Five { get { Reads++; return 5; } }
        }

        public class Throwing
        {
            public string Value { get { throw new InvalidOperationException("boom"); } }
        }

        /// <summary>A collection whose own Contains answers differently from its (empty) enumeration.</summary>
        public class EvenNumbers : IEnumerable<int>
        {
            public bool Contains(int value) { return value % 2 == 0; }
            public IEnumerator<int> GetEnumerator() { return Enumerable.Empty<int>().GetEnumerator(); }
            IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        }

        public class Settings
        {
            public override string ToString() { return "on"; }
        }

        public sealed class SealedSettings
        {
            public override string ToString() { return "on"; }
        }

        public struct UserStruct
        {
            public override string ToString() { return "on"; }
        }

        public struct Wrapper
        {
            public int Value;
            public static explicit operator int(Wrapper wrapper) { return wrapper.Value; }
        }

        /// <summary>A user type with an implicit conversion from an array, to build a non-MemoryExtensions call.</summary>
        public class IntBag
        {
            public int[] Items;
            public static implicit operator IntBag(int[] items) { return new IntBag { Items = items }; }
        }

        public class OddToString
        {
            public new object ToString() { return "odd"; }
        }

        public static class StaticHolder
        {
            public static int IdField = 5;
            public static string Name { get { return "alpha"; } }
        }

        public static class UserExtensions
        {
            public static int Contains(IEnumerable<int> source, int value) { return 1; }
            public static bool Contains(IntBag bag, int value) { return value == 42; }
        }

        /// <summary>Returns predicates over its own properties, so the expression captures <c>this</c>.</summary>
        public class Holder
        {
            public string Filter { get; set; }
            public Expression<Func<string>> FilterRead() { return () => Filter; }
        }

        private static int Seven() { return 7; }

        private static MethodCallExpression Call<T>(Expression<Func<T>> expression)
        {
            return (MethodCallExpression)expression.Body;
        }

        private static Expression Body<T>(Expression<Func<T>> expression)
        {
            return expression.Body;
        }

        private static MethodCallExpression CallOnRow<T>(Expression<Func<Row, T>> expression)
        {
            return (MethodCallExpression)expression.Body;
        }

        private static string Translate(MethodCallExpression call)
        {
            string sql;
            Assert.IsTrue(RowIndependentEvaluator.TryTranslateContains(call, out sql), "expected an evaluated call: " + call);
            return sql;
        }

        private static string Evaluate(MethodCallExpression call)
        {
            string value;
            Assert.IsTrue(RowIndependentEvaluator.TryEvaluateToString(call, out value), "expected an evaluated call: " + call);
            return value;
        }

        private static void AssertNotEvaluated(MethodCallExpression call)
        {
            string value;
            Assert.IsFalse(RowIndependentEvaluator.TryEvaluateToString(call, out value), "expected no evaluation: " + call);
            Assert.IsNull(value);
        }

        private static NotSupportedException AssertCouldNotEvaluate(Action action)
        {
            var ex = Assert.ThrowsException<NotSupportedException>(action);
            StringAssert.StartsWith(ex.Message, "Could not evaluate expression: ");
            Assert.IsNotNull(ex.InnerException, "the original exception must be the inner one");
            Assert.IsNotInstanceOfType(ex.InnerException, typeof(TargetInvocationException));
            return ex;
        }

        /// <summary>Runs <paramref name="action"/> with a Compiler that counts its calls; restores the original.</summary>
        private static int CountCompiles(Action action)
        {
            var original = RowIndependentEvaluator.Compiler;
            var count = 0;
            RowIndependentEvaluator.Compiler = lambda => { count++; return original(lambda); };
            try
            {
                action();
            }
            finally
            {
                RowIndependentEvaluator.Compiler = original;
            }
            return count;
        }

        #endregion

        #region IsRowIndependent

        [TestMethod]
        public void IsRowIndependent_ConstantAndCapturedChains_True()
        {
            var local = 5;
            var request = new Request { Inner = new Inner { Id = 5 } };
            Assert.IsTrue(RowIndependentEvaluator.IsRowIndependent(Expression.Constant(5)));
            Assert.IsTrue(RowIndependentEvaluator.IsRowIndependent(Body(() => local)));
            Assert.IsTrue(RowIndependentEvaluator.IsRowIndependent(Body(() => request.Inner.Id)));
            Assert.IsTrue(RowIndependentEvaluator.IsRowIndependent(Body(() => StaticHolder.Name)));
        }

        [TestMethod]
        public void IsRowIndependent_ReadsTheParameter_False()
        {
            var ids = new List<int> { 1, 2 };
            Expression<Func<Row, int>> id = x => x.Id;
            Assert.IsFalse(RowIndependentEvaluator.IsRowIndependent(id.Body));
            Assert.IsFalse(RowIndependentEvaluator.IsRowIndependent(CallOnRow(x => ids.Contains(x.Id))));
        }

        [TestMethod]
        public void IsRowIndependent_NestedLambdaParameter_True()
        {
            var ids = new List<int> { 1, 2 };
            var list = new List<int> { 1, 2, 3 };
            Assert.IsTrue(RowIndependentEvaluator.IsRowIndependent(Call(() => ids.Contains(list.First(o => o > 1)))));
        }

        [TestMethod]
        public void IsRowIndependent_ParameterOutsideItsLambda_False()
        {
            // The same ParameterExpression declared by a nested lambda and also used outside it, before and after.
            var p = Expression.Parameter(typeof(int), "p");
            Expression lambda = Expression.Lambda<Func<int, bool>>(Expression.GreaterThan(p, Expression.Constant(1)), p);
            Expression outside = Expression.Convert(p, typeof(object));
            var before = Expression.NewArrayInit(typeof(object), outside, lambda);
            var after = Expression.NewArrayInit(typeof(object), lambda, outside);
            Assert.IsFalse(RowIndependentEvaluator.IsRowIndependent(before));
            Assert.IsFalse(RowIndependentEvaluator.IsRowIndependent(after));
        }

        [TestMethod]
        public void IsRowIndependent_Null_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() => RowIndependentEvaluator.IsRowIndependent(null));
        }

        #endregion

        #region TryTranslateContains

        [TestMethod]
        public void TryTranslateContains_CapturedItemAbsent_FalseSql()
        {
            var ids = new List<int> { 1, 2 };
            var absent = new Request { Id = 5 };
            Assert.AreEqual("1=0", RowIndependentEvaluator.FalseSql);
            Assert.AreEqual(RowIndependentEvaluator.FalseSql, Translate(Call(() => ids.Contains(absent.Id))));
        }

        [TestMethod]
        public void TryTranslateContains_CapturedItemPresent_TrueSql()
        {
            var ids = new List<int> { 1, 2 };
            var present = new Request { Id = 1 };
            Assert.AreEqual("NOT 1=0", RowIndependentEvaluator.TrueSql);
            Assert.AreEqual(RowIndependentEvaluator.TrueSql, Translate(Call(() => ids.Contains(present.Id))));
        }

        [TestMethod]
        public void TryTranslateContains_ReadsTheRow_ReturnsFalseWithoutEvaluating()
        {
            var counting = new Counting();
            string sql;
            Assert.IsFalse(RowIndependentEvaluator.TryTranslateContains(CallOnRow(x => counting.Ids.Contains(x.Id)), out sql));
            Assert.AreEqual(string.Empty, sql);
            Assert.AreEqual(0, counting.Reads, "nothing may be evaluated for a call that reads the row");
        }

        [TestMethod]
        public void TryTranslateContains_UsesTheCollectionsComparer()
        {
            var ci = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" };
            var absent = new Request { Code = "a" };
            Assert.AreEqual(RowIndependentEvaluator.TrueSql, Translate(Call(() => ci.Contains(absent.Code))));
        }

        [TestMethod]
        public void TryTranslateContains_EnumerableContainsWithComparer()
        {
            var codes = new List<string> { "A" };
            var absent = new Request { Code = "a" };
            Assert.AreEqual(RowIndependentEvaluator.TrueSql,
                Translate(Call(() => Enumerable.Contains(codes, absent.Code, StringComparer.OrdinalIgnoreCase))));
        }

        [TestMethod]
        public void TryTranslateContains_OrdinalStringEquality()
        {
            var codes = new List<string> { "A" };
            var absent = new Request { Code = "a" };
            Assert.AreEqual(RowIndependentEvaluator.FalseSql, Translate(Call(() => codes.Contains(absent.Code))));
        }

        [TestMethod]
        public void TryTranslateContains_NullItem()
        {
            var nulls = new List<int?> { null };
            var nullHolder = new Request { NullableId = null };
            Assert.AreEqual(RowIndependentEvaluator.TrueSql, Translate(Call(() => nulls.Contains(nullHolder.NullableId))));
        }

        [TestMethod]
        public void TryTranslateContains_UserCollectionContains()
        {
            // Its own Contains says 4 is in it; its enumeration is empty.
            var evens = new EvenNumbers();
            Assert.AreEqual(RowIndependentEvaluator.TrueSql, Translate(Call(() => evens.Contains(4))));
            Assert.AreEqual(RowIndependentEvaluator.FalseSql, Translate(Call(() => evens.Contains(3))));
        }

        [TestMethod]
        public void TryTranslateContains_StringReceiverNull_UnknownSql()
        {
            string nullString = null;
            Assert.AreEqual("NULL LIKE NULL", RowIndependentEvaluator.UnknownSql);
            Assert.AreEqual(RowIndependentEvaluator.UnknownSql, Translate(Call(() => nullString.Contains("a"))));
        }

        [TestMethod]
        public void TryTranslateContains_StringValueNull_UnknownSql()
        {
            var s = "zeta";
            string nullString = null;
            Assert.AreEqual(RowIndependentEvaluator.UnknownSql, Translate(Call(() => s.Contains(nullString))));
        }

        [TestMethod]
        public void TryTranslateContains_StringReceiver_OrdinalAndTrue()
        {
            var s = "zeta";
            Assert.AreEqual(RowIndependentEvaluator.FalseSql, Translate(Call(() => s.Contains("ZETA"))));
            Assert.AreEqual(RowIndependentEvaluator.TrueSql, Translate(Call(() => s.Contains("et"))));
        }

        [TestMethod]
        public void TryTranslateContains_Throwing_WrapsInNotSupported()
        {
            var ids = new List<int> { 1, 2 };
            Request nullRequest = null;
            var ex = AssertCouldNotEvaluate(() => Translate(Call(() => ids.Contains(nullRequest.Id))));
            Assert.IsInstanceOfType(ex.InnerException, typeof(NullReferenceException));
        }

        [TestMethod]
        public void TryTranslateContains_NotABoolContains_Throws()
        {
            var s = "x";
            var ids = new List<int> { 1, 2 };
            string sql;
            Assert.ThrowsException<ArgumentException>(() =>
                RowIndependentEvaluator.TryTranslateContains(Call(() => string.IsNullOrEmpty(s)), out sql));
            Assert.ThrowsException<ArgumentException>(() =>
                RowIndependentEvaluator.TryTranslateContains(Call(() => UserExtensions.Contains(ids, 1)), out sql));
        }

        [TestMethod]
        public void TryTranslateContains_Null_Throws()
        {
            string sql;
            Assert.ThrowsException<ArgumentNullException>(() => RowIndependentEvaluator.TryTranslateContains(null, out sql));
        }

        [TestMethod]
        public void TryTranslateContains_UsesTheCompiler()
        {
            var ids = new List<int> { 1, 2 };
            var absent = new Request { Id = 5 };
            var compiles = CountCompiles(() => Translate(Call(() => ids.Contains(absent.Id))));
            Assert.IsTrue(compiles > 0, "the collection's Contains must be evaluated through Compiler");

            var s = "zeta";
            compiles = CountCompiles(() => Translate(Call(() => s.Contains(absent.Name))));
            Assert.IsTrue(compiles > 0, "a string Contains must be evaluated through Compiler");
        }

#if NET
        [TestMethod]
        public void TryTranslateContains_SpanBound_Evaluates()
        {
            var array = new[] { 1, 2 };
            Assert.AreEqual(RowIndependentEvaluator.TrueSql, Translate(SpanContains(array, 2)));
            Assert.AreEqual(RowIndependentEvaluator.FalseSql, Translate(SpanContains(array, 5)));
        }

        [TestMethod]
        public void TryTranslateContains_StringReceiver_HonorsStringComparison()
        {
            var s = "zeta";
            Assert.AreEqual(RowIndependentEvaluator.TrueSql,
                Translate(Call(() => s.Contains("ZETA", StringComparison.OrdinalIgnoreCase))));
        }

        [TestMethod]
        public void TryTranslateContains_StringInvokeThrows_InnerIsTheOriginal()
        {
            // string's own Contains rejects an undefined StringComparison; it's invoked by reflection, which wraps.
            var s = "zeta";
            var comparison = (StringComparison)99;
            var ex = AssertCouldNotEvaluate(() => Translate(Call(() => s.Contains("z", comparison))));
            Assert.IsInstanceOfType(ex.InnerException, typeof(ArgumentException));
        }
#endif

        #endregion

        #region TryEvaluateToString

        public enum Day { Sunday, Monday }

        [TestMethod]
        public void TryEvaluateToString_CapturedInt_CSharpString()
        {
            var localInt = 5;
            Assert.AreEqual("5", Evaluate(Call(() => localInt.ToString())));
        }

        [TestMethod]
        public void TryEvaluateToString_Formatted()
        {
            var five = 5;
            Assert.AreEqual("05", Evaluate(Call(() => five.ToString("D2"))));
        }

        [TestMethod]
        public void TryEvaluateToString_Enum()
        {
            var day = Day.Monday;
            Assert.AreEqual("Monday", Evaluate(Call(() => day.ToString())));
            Assert.AreEqual("1", Evaluate(Call(() => day.ToString("D"))));
        }

        [TestMethod]
        public void TryEvaluateToString_NullableNoValue_EmptyString()
        {
            int? n = null;
            Assert.AreEqual(string.Empty, Evaluate(Call(() => n.ToString())));
        }

        [TestMethod]
        public void TryEvaluateToString_CastReceiver_Evaluated()
        {
            var five = 5;
            object boxed = 5;
            Assert.AreEqual("5", Evaluate(Call(() => ((long)five).ToString())));
            Assert.AreEqual("5", Evaluate(Call(() => ((int)boxed).ToString())));
        }

        [TestMethod]
        public void TryEvaluateToString_CurrentCulture()
        {
            var amount = 1.5m;
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.AreEqual("1,5", Evaluate(Call(() => amount.ToString())));
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                Assert.AreEqual("1.5", Evaluate(Call(() => amount.ToString())));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [TestMethod]
        public void TryEvaluateToString_NullStringReceiver_WrapsInNotSupported()
        {
            string nullString = null;
            var ex = AssertCouldNotEvaluate(() => Evaluate(Call(() => nullString.ToString())));
            Assert.IsInstanceOfType(ex.InnerException, typeof(NullReferenceException));
        }

        [TestMethod]
        public void TryEvaluateToString_UserType_ReturnsFalse()
        {
            var counting = new Counting();
            AssertNotEvaluated(Call(() => counting.Settings.ToString()));
            Assert.AreEqual(0, counting.Reads, "nothing may be evaluated for a call the guard doesn't evaluate");
        }

        [TestMethod]
        public void TryEvaluateToString_ObjectDeclared_ReturnsFalse()
        {
            object objString = "b";
            IComparable interfaceString = "b";
            AssertNotEvaluated(Call(() => objString.ToString()));
            AssertNotEvaluated(Call(() => interfaceString.ToString()));
        }

        [TestMethod]
        public void TryEvaluateToString_FormatProvider_ReturnsFalse()
        {
            var counting = new Counting();
            AssertNotEvaluated(Call(() => counting.Five.ToString(CultureInfo.InvariantCulture)));
            Assert.AreEqual(0, counting.Reads);
        }

        [TestMethod]
        public void TryEvaluateToString_Static_ReturnsFalse()
        {
            var localInt = 5;
            AssertNotEvaluated(Call(() => Convert.ToString(localInt)));
        }

        [TestMethod]
        public void TryEvaluateToString_UserStruct_ReturnsFalse()
        {
            var userStruct = new UserStruct();
            AssertNotEvaluated(Call(() => userStruct.ToString()));
        }

        [TestMethod]
        public void TryEvaluateToString_NullableUserStruct_ReturnsFalse()
        {
            UserStruct? nullableUserStruct = new UserStruct();
            AssertNotEvaluated(Call(() => nullableUserStruct.ToString()));
        }

        [TestMethod]
        public void TryEvaluateToString_SealedUserClass_ReturnsFalse()
        {
            var sealedSettings = new SealedSettings();
            AssertNotEvaluated(Call(() => sealedSettings.ToString()));
        }

        [TestMethod]
        public void TryEvaluateToString_GenericCoreValueType_ReturnsFalse()
        {
            var pair = new KeyValuePair<int, int>(1, 2);
            AssertNotEvaluated(Call(() => pair.ToString()));
        }

        [TestMethod]
        public void TryEvaluateToString_ArithmeticReceiver_ReturnsFalse()
        {
            var counting = new Counting();
            AssertNotEvaluated(Call(() => (counting.Five + 1).ToString()));
            Assert.AreEqual(0, counting.Reads);
        }

        [TestMethod]
        public void TryEvaluateToString_ConcatenatedFormat_ReturnsFalse()
        {
            var five = 5;
            var digits = "2";
            AssertNotEvaluated(Call(() => five.ToString("D" + digits)));
        }

        [TestMethod]
        public void TryEvaluateToString_UserConversionReceiver_ReturnsFalse()
        {
            var wrapper = new Wrapper { Value = 5 };
            AssertNotEvaluated(Call(() => ((int)wrapper).ToString()));
        }

        [TestMethod]
        public void TryEvaluateToString_ReadsTheRow_ReturnsFalse()
        {
            string value;
            Assert.IsFalse(RowIndependentEvaluator.TryEvaluateToString(CallOnRow(x => x.Id.ToString()), out value));
            Assert.IsNull(value);
        }

        [TestMethod]
        public void TryEvaluateToString_UsesTheCompiler()
        {
            var localInt = 5;
            var compiles = CountCompiles(() => Evaluate(Call(() => localInt.ToString())));
            Assert.IsTrue(compiles > 0, "ToString must be evaluated through Compiler");
        }

        [TestMethod]
        public void TryEvaluateToString_Null_Throws()
        {
            string value;
            Assert.ThrowsException<ArgumentNullException>(() => RowIndependentEvaluator.TryEvaluateToString(null, out value));
        }

        [TestMethod]
        public void TryEvaluateToString_NotAToString_Throws()
        {
            var five = 5;
            var odd = new OddToString();
            string value;
            Assert.ThrowsException<ArgumentException>(() =>
                RowIndependentEvaluator.TryEvaluateToString(Call(() => five.GetHashCode()), out value));
            Assert.ThrowsException<ArgumentException>(() =>
                RowIndependentEvaluator.TryEvaluateToString(Call(() => odd.ToString()), out value));
        }

        #endregion

        #region TryReadValue

        private static object Read(Expression expression)
        {
            object value;
            Assert.IsTrue(RowIndependentEvaluator.TryReadValue(expression, out value), "expected a value: " + expression);
            return value;
        }

        private static void AssertNotRead(Expression expression)
        {
            object value;
            Assert.IsFalse(RowIndependentEvaluator.TryReadValue(expression, out value), "expected no value: " + expression);
            Assert.IsNull(value);
        }

        [TestMethod]
        public void TryReadValue_Constant()
        {
            Assert.AreEqual("x", Read(Expression.Constant("x")));
        }

        [TestMethod]
        public void TryReadValue_NullConstant()
        {
            Assert.IsNull(Read(Expression.Constant(null, typeof(string))));
        }

        [TestMethod]
        public void TryReadValue_ClosureField()
        {
            var local = "zz";
            Assert.AreEqual("zz", Read(Body(() => local)));
        }

        [TestMethod]
        public void TryReadValue_CapturedThisProperty()
        {
            var holder = new Holder { Filter = "zz" };
            Assert.AreEqual("zz", Read(holder.FilterRead().Body));
        }

        [TestMethod]
        public void TryReadValue_NestedProperty()
        {
            var absent = new Request { Inner = new Inner { Name = "inner" } };
            Assert.AreEqual("inner", Read(Body(() => absent.Inner.Name)));
        }

        [TestMethod]
        public void TryReadValue_StaticProperty()
        {
            Assert.AreEqual("alpha", Read(Body(() => StaticHolder.Name)));
        }

        [TestMethod]
        public void TryReadValue_StaticField()
        {
            Assert.AreEqual(5, Read(Body(() => StaticHolder.IdField)));
        }

        [TestMethod]
        public void TryReadValue_NotAChain_ReturnsFalse()
        {
            var counting = new Counting();
            var s = " zz ";
            Expression<Func<Row, string>> parameterChain = x => x.Name;
            AssertNotRead(Body(() => s.Trim()));
            AssertNotRead(parameterChain.Body);
            AssertNotRead(Body(() => (object)counting.Five));
            Assert.AreEqual(0, counting.Reads);
        }

        [TestMethod]
        public void TryReadValue_NullIntermediate_InnerIsNullReference()
        {
            Request nullRequest = null;
            var ex = AssertCouldNotEvaluate(() => Read(Body(() => nullRequest.Name)));
            Assert.IsInstanceOfType(ex.InnerException, typeof(NullReferenceException));
        }

        [TestMethod]
        public void TryReadValue_NullNullableValue_InnerIsInvalidOperation()
        {
            var nullHolder = new Request { NullableId = null };
            var ex = AssertCouldNotEvaluate(() => Read(Body(() => nullHolder.NullableId.Value)));
            Assert.IsInstanceOfType(ex.InnerException, typeof(InvalidOperationException));
        }

        [TestMethod]
        public void TryReadValue_ThrowingGetter_InnerIsTheOriginal()
        {
            var throwing = new Throwing();
            var ex = AssertCouldNotEvaluate(() => Read(Body(() => throwing.Value)));
            Assert.IsInstanceOfType(ex.InnerException, typeof(InvalidOperationException));
            Assert.AreEqual("boom", ex.InnerException.Message);
        }

        [TestMethod]
        public void TryReadValue_UsesTheCompiler()
        {
            var absent = new Request { Inner = new Inner { Name = "inner" } };
            var compiles = CountCompiles(() => Read(Body(() => absent.Inner.Name)));
            Assert.IsTrue(compiles > 0, "a member chain must be read through Compiler");
        }

        [TestMethod]
        public void TryReadValue_Null_Throws()
        {
            object value;
            Assert.ThrowsException<ArgumentNullException>(() => RowIndependentEvaluator.TryReadValue(null, out value));
        }

        #endregion

        #region RewriteForEvaluation and the Compile seam

        [TestMethod]
        public void RewriteForEvaluation_NotSpanBound_Unchanged()
        {
            var ids = new List<int> { 1, 2 };
            var call = Call(() => ids.Contains(1));
            Assert.AreSame(call, RowIndependentEvaluator.RewriteForEvaluation(call));
        }

        [TestMethod]
        public void RewriteForEvaluation_UserImplicitConversion_NotRewritten()
        {
            // A user's static Contains whose first argument is an op_Implicit conversion of an array: not
            // MemoryExtensions, so its own answer stands (42 isn't in the array, yet the user's Contains says true).
            var toBag = typeof(IntBag).GetMethod("op_Implicit", new[] { typeof(int[]) });
            var contains = typeof(UserExtensions).GetMethod("Contains", new[] { typeof(IntBag), typeof(int) });
            var call = Expression.Call(contains,
                Expression.Convert(Expression.Constant(new[] { 1, 2 }), typeof(IntBag), toBag), Expression.Constant(42));
            Assert.AreSame(call, RowIndependentEvaluator.RewriteForEvaluation(call));
            Assert.AreEqual(RowIndependentEvaluator.TrueSql, Translate(call));
        }

        [TestMethod]
        public void Compile_InterpretingCompile_IsFound()
        {
            Assert.IsNotNull(RowIndependentEvaluator.InterpretingCompile,
                "LambdaExpression.Compile(bool) exists on .NET Framework 4.7.1+ and .NET Core");
        }

        [TestMethod]
        public void Compile_WithoutInterpretation_Compiles()
        {
            var lambda = Expression.Lambda<Func<int>>(Expression.Constant(7));
            var compiled = (Func<int>)RowIndependentEvaluator.Compile(lambda, null);
            Assert.AreEqual(7, compiled());
        }

        [TestMethod]
        public void Compile_OnlyInTheSeam()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FunkyORM.sln")))
                dir = dir.Parent;
            Assert.IsNotNull(dir, "repository root (FunkyORM.sln) not found above " + AppContext.BaseDirectory);
            var path = Path.Combine(dir.FullName, "Funcular.Data.Orm.Core", "Linq", "RowIndependentEvaluator.cs");
            Assert.IsTrue(File.Exists(path), "source not found: " + path);

            // Comments may name the method; code may call it in one place only, the seam.
            var code = string.Join("\n", File.ReadAllLines(path).Select(line =>
            {
                var comment = line.IndexOf("//", StringComparison.Ordinal);
                return comment >= 0 ? line.Substring(0, comment) : line;
            }));
            Assert.AreEqual(1, Regex.Matches(code, @"\.Compile\(").Count,
                "only Compile(LambdaExpression, MethodInfo) may compile a lambda; every evaluation goes through Compiler");
            StringAssert.Contains(code, "internal static Delegate Compile(LambdaExpression lambda, MethodInfo? interpretingCompile)");
        }

#if NET
        private static MethodCallExpression SpanContains(int[] array, int item)
        {
            var contains = typeof(MemoryExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "Contains" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2
                             && m.GetParameters()[0].ParameterType.IsGenericType
                             && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>))
                .MakeGenericMethod(typeof(int));
            var toSpan = typeof(ReadOnlySpan<int>).GetMethod("op_Implicit", new[] { typeof(int[]) });
            return Expression.Call(contains, Expression.Call(toSpan, Expression.Constant(array)), Expression.Constant(item));
        }

        [TestMethod]
        public void RewriteForEvaluation_SpanBound_InterpretsWithoutSpan()
        {
            var call = SpanContains(new[] { 1, 2 }, 2);

            // The premise: interpreted as written, the span call can't be evaluated.
            AssertThrowsAny(() => ((Func<bool>)RowIndependentEvaluator.Compile(
                Expression.Lambda<Func<bool>>(call), RowIndependentEvaluator.InterpretingCompile))());

            var rewritten = RowIndependentEvaluator.RewriteForEvaluation(call) as MethodCallExpression;
            Assert.IsNotNull(rewritten);
            Assert.AreEqual(typeof(Enumerable), rewritten.Method.DeclaringType);
            var interpreted = (Func<bool>)RowIndependentEvaluator.Compile(
                Expression.Lambda<Func<bool>>(rewritten), RowIndependentEvaluator.InterpretingCompile);
            Assert.IsTrue(interpreted());
        }

        [TestMethod]
        public void RewriteForEvaluation_UnaryImplicitForm_Rewritten()
        {
            // The other form an op_Implicit conversion takes in an expression tree: a Convert node with a method.
            var contains = SpanContains(new[] { 1, 2 }, 2).Method;
            var toSpan = typeof(ReadOnlySpan<int>).GetMethod("op_Implicit", new[] { typeof(int[]) });
            var call = Expression.Call(contains,
                Expression.Convert(Expression.Constant(new[] { 1, 2 }), typeof(ReadOnlySpan<int>), toSpan), Expression.Constant(2));
            var rewritten = RowIndependentEvaluator.RewriteForEvaluation(call) as MethodCallExpression;
            Assert.IsNotNull(rewritten);
            Assert.AreEqual(typeof(Enumerable), rewritten.Method.DeclaringType);
            Assert.AreEqual(RowIndependentEvaluator.TrueSql, Translate(call));
        }

        [TestMethod]
        public void RewriteForEvaluation_SpanNotFromAConversion_Unchanged()
        {
            // A span built some other way (a constructor) isn't an array to hand to Enumerable.Contains.
            var contains = SpanContains(new[] { 1, 2 }, 2).Method;
            var ctor = typeof(ReadOnlySpan<int>).GetConstructor(new[] { typeof(int[]) });
            var call = Expression.Call(contains, Expression.New(ctor, Expression.Constant(new[] { 1, 2 })), Expression.Constant(2));
            Assert.AreSame(call, RowIndependentEvaluator.RewriteForEvaluation(call));
        }

        [TestMethod]
        public void Compile_WithInterpretation_Interprets()
        {
            // A span call compiles to IL but can't be interpreted: the seam's two paths are told apart by it.
            var lambda = Expression.Lambda<Func<bool>>(SpanContains(new[] { 1, 2 }, 2));
            Assert.IsTrue(((Func<bool>)RowIndependentEvaluator.Compile(lambda, null))());
            AssertThrowsAny(() => ((Func<bool>)RowIndependentEvaluator.Compile(
                lambda, RowIndependentEvaluator.InterpretingCompile))());
        }

        private static void AssertThrowsAny(Action action)
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                return;
            }
            Assert.Fail("expected the interpreter to fail on a span");
        }
#endif

        #endregion

        #region CoreEvaluability (the guard's predicate, copied)

        [TestMethod]
        public void CoreEvaluability_MatchesTheGuardsTable()
        {
            var capturedA = 7;
            var five = 5;
            var day = Day.Monday;
            int? n = null;
            var s = "s";
            var flag = true;
            object boxed = 5;
            object objString = "b";
            var settings = new Settings();
            var userStruct = new UserStruct();
            var pair = new KeyValuePair<int, int>(1, 2);
            var arr = new[] { 1 };
            var wrapper = new Wrapper { Value = 5 };
            Expression<Func<Row, bool>> readsRow = x => x.Id == 2;

            var rows = new List<Tuple<string, Expression, bool>>
            {
                Tuple.Create("constant", (Expression)Expression.Constant(true), true),
                Tuple.Create("captured local", Body(() => capturedA), true),
                Tuple.Create("static property", Body(() => StaticHolder.Name), true),
                Tuple.Create("int ToString()", (Expression)Call(() => capturedA.ToString()), true),
                Tuple.Create("int ToString(format)", (Expression)Call(() => five.ToString("D2")), true),
                Tuple.Create("enum ToString()", (Expression)Call(() => day.ToString()), true),
                Tuple.Create("nullable ToString()", (Expression)Call(() => n.ToString()), true),
                Tuple.Create("string ToString()", (Expression)Call(() => s.ToString()), true),
                Tuple.Create("string Contains", (Expression)Call(() => s.Contains("s")), true),
                Tuple.Create("cast receiver", (Expression)Call(() => ((long)five).ToString()), true),
                Tuple.Create("unboxing receiver", (Expression)Call(() => ((int)boxed).ToString()), true),
                Tuple.Create("conditional", Body(() => flag ? 1 : 2), true),
                Tuple.Create("not", Body(() => !flag), true),
                Tuple.Create("comparison", Body(() => capturedA == five), true),
                Tuple.Create("ToString(provider)", (Expression)Call(() => five.ToString(CultureInfo.InvariantCulture)), false),
                Tuple.Create("object ToString()", (Expression)Call(() => objString.ToString()), false),
                Tuple.Create("user class ToString()", (Expression)Call(() => settings.ToString()), false),
                Tuple.Create("user struct ToString()", (Expression)Call(() => userStruct.ToString()), false),
                Tuple.Create("generic core ToString()", (Expression)Call(() => pair.ToString()), false),
                Tuple.Create("static Convert.ToString", (Expression)Call(() => Convert.ToString(five)), false),
                Tuple.Create("another instance call", (Expression)Call(() => s.Trim()), false),
                Tuple.Create("arithmetic", Body(() => capturedA + 1), false),
                Tuple.Create("arithmetic receiver", (Expression)Call(() => (five + 1).ToString()), false),
                Tuple.Create("indexer receiver", (Expression)Call(() => arr[0].ToString()), false),
                Tuple.Create("method receiver", (Expression)Call(() => Seven().ToString()), false),
                Tuple.Create("coalesce receiver", (Expression)Call(() => (n ?? 7).ToString()), false),
                Tuple.Create("user conversion receiver", (Expression)Call(() => ((int)wrapper).ToString()), false),
                Tuple.Create("reads the row", readsRow.Body, false),
            };

            var wrong = rows.Where(r => CoreEvaluability.IsEvaluable(r.Item2) != r.Item3)
                .Select(r => r.Item1 + " (expected " + r.Item3 + ")").ToList();
            Assert.AreEqual(0, wrong.Count, "differs from the guard's rule: " + string.Join("; ", wrong));
        }

        #endregion
    }
}
