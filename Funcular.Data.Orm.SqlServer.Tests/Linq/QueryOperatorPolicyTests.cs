// Linked into SqlServer.Tests.NetFramework (net48, MSTest 2.x): keep it C# 7.3-compatible and refer to
// post-net48 Queryable members only by name (reflection).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Funcular.Data.Orm.Linq;
using Funcular.Data.Orm.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests.Linq
{
    /// <summary>
    /// DB-free tests of the Core <see cref="QueryOperatorPolicy"/>: the pinned allow-list (AC13-7) and the
    /// spine-walk rules that don't need a provider (AC13-4).
    /// </summary>
    [TestClass]
    public class QueryOperatorPolicyTests
    {
        private static IEnumerable<MethodInfo> QueryableMethods()
        {
            return typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static);
        }

        [TestMethod]
        public void SupportedOperators_IsReadOnlyView()
        {
            // A caller (or the Task 10 doc test) must not be able to rewrite the list through a cast.
            Assert.IsNotInstanceOfType(QueryOperatorPolicy.SupportedOperators, typeof(MethodInfo[]));
            Assert.IsTrue(((ICollection<MethodInfo>)QueryOperatorPolicy.SupportedOperators).IsReadOnly);
        }

        [TestMethod]
        public void SupportedOperators_ExactLiteralSetPinned()
        {
            Assert.AreEqual(SupportedOperatorSignatures.Count, SupportedOperatorSignatures.All.Length,
                "the literal set itself must hold exactly the pinned count");
            Assert.AreEqual(SupportedOperatorSignatures.Count,
                SupportedOperatorSignatures.All.Distinct(StringComparer.Ordinal).Count(), "the literal set has duplicates");

            var actual = QueryOperatorPolicy.SupportedOperators
                .Select(SupportedOperatorSignatures.Format)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();
            var expected = SupportedOperatorSignatures.All.OrderBy(s => s, StringComparer.Ordinal).ToList();

            var missing = expected.Except(actual, StringComparer.Ordinal).ToList();
            var extra = actual.Except(expected, StringComparer.Ordinal).ToList();
            Assert.AreEqual(0, missing.Count + extra.Count,
                "SupportedOperators differs from the pinned literal set.\nMissing: " + string.Join("; ", missing) +
                "\nExtra: " + string.Join("; ", extra));
            Assert.AreEqual(SupportedOperatorSignatures.Count, actual.Count, "SupportedOperators count");
        }

        [TestMethod]
        public void ClassifierSweep_EveryQueryableMethod_MatchesLiteralSet()
        {
            var expected = new HashSet<string>(SupportedOperatorSignatures.All, StringComparer.Ordinal);
            var methods = QueryableMethods().ToList();

            // Every pinned signature exists on this runtime (the literal set isn't stale).
            var present = new HashSet<string>(methods.Select(SupportedOperatorSignatures.Format), StringComparer.Ordinal);
            var absent = expected.Where(s => !present.Contains(s)).ToList();
            Assert.AreEqual(0, absent.Count, "pinned signatures not found on this runtime: " + string.Join("; ", absent));

            // The classifier agrees with the literal set on every public Queryable method, including any this
            // runtime added after the set was written (they must be rejected).
            var mismatches = methods
                .Where(m => QueryOperatorPolicy.IsAllowed(m) != expected.Contains(SupportedOperatorSignatures.Format(m)))
                .Select(m => (QueryOperatorPolicy.IsAllowed(m) ? "allowed but not pinned: " : "pinned but rejected: ") +
                             SupportedOperatorSignatures.Format(m))
                .ToList();
            Assert.AreEqual(0, mismatches.Count, string.Join("\n", mismatches));

            // A closed generic method classifies like its definition.
            var closedWhere = methods
                .First(m => SupportedOperatorSignatures.Format(m) == "Where<TSource>(IQueryable<TSource>, Expression<Func<TSource, Boolean>>)")
                .MakeGenericMethod(typeof(string));
            Assert.IsTrue(QueryOperatorPolicy.IsAllowed(closedWhere), "closed Where<string> must be allowed");
        }

        [TestMethod]
        public void NonQueryableOverload_IsRejected()
        {
            var enumerableWhere = typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Where" && m.GetParameters()[1].ParameterType.GetGenericArguments().Length == 2);
            Assert.IsFalse(QueryOperatorPolicy.IsAllowed(enumerableWhere), "Enumerable.Where (definition)");
            Assert.IsFalse(QueryOperatorPolicy.IsAllowed(enumerableWhere.MakeGenericMethod(typeof(int))), "Enumerable.Where<int>");

            // Non-generic Queryable overloads are never allowed (GetGenericMethodDefinition must not be called on them).
            var nonGenericSum = typeof(Queryable).GetMethod("Sum", new[] { typeof(IQueryable<int>) });
            Assert.IsNotNull(nonGenericSum);
            Assert.IsFalse(QueryOperatorPolicy.IsAllowed(nonGenericSum), "Queryable.Sum(IQueryable<int>)");

            var concat = typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string) });
            Assert.IsFalse(QueryOperatorPolicy.IsAllowed(concat), "string.Concat");
        }

        [TestMethod]
        public void NonCallNonRootSpineNode_Rejected()
        {
            // A hand-built Convert node in the spine: the policy must reject it, not treat it as the root.
            var root = new int[0].AsQueryable();
            var where = QueryableMethods().First(m =>
                    SupportedOperatorSignatures.Format(m) == "Where<TSource>(IQueryable<TSource>, Expression<Func<TSource, Boolean>>)")
                .MakeGenericMethod(typeof(int));
            Expression<Func<int, bool>> predicate = x => x > 0;
            var spine = Expression.Call(where, Expression.Convert(root.Expression, typeof(IQueryable<int>)), Expression.Quote(predicate));

            Assert.ThrowsException<NotSupportedException>(() => QueryOperatorPolicy.EnsureSupported(spine));
        }

        [TestMethod]
        public void ForeignQueryableConstantRoot_Rejected()
        {
            // A constant whose value is a COMPOSED queryable (its Expression isn't that constant) would silently drop
            // the composed operators; only a queryable's own root is a valid spine terminal. The composed operator is
            // an ALLOWED one, so only the own-root rule can reject this.
            var composed = new[] { 1, 2, 3 }.AsQueryable().Where(x => x > 1);
            var take = QueryableMethods().First(m => SupportedOperatorSignatures.Format(m) == "Take<TSource>(IQueryable<TSource>, Int32)")
                .MakeGenericMethod(typeof(int));
            var spine = Expression.Call(take, Expression.Constant(composed, typeof(IQueryable<int>)), Expression.Constant(1));

            Assert.ThrowsException<NotSupportedException>(() => QueryOperatorPolicy.EnsureSupported(spine));
        }

        [TestMethod]
        public void OwnRoot_AllowedChain_Passes()
        {
            var query = new[] { 1, 2, 3 }.AsQueryable().Where(x => x > 0).OrderBy(x => x).Skip(1).Take(1);

            QueryOperatorPolicy.EnsureSupported(query.Expression);
        }

        [TestMethod]
        public void RejectedOperator_NamesOperator()
        {
            var query = new[] { 1, 2, 3 }.AsQueryable().Reverse();

            var ex = Assert.ThrowsException<NotSupportedException>(() => QueryOperatorPolicy.EnsureSupported(query.Expression));
            StringAssert.Contains(ex.Message, "Reverse");
        }
    }
}
