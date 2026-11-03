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

        #region 3.14.0: a lambda after a column-subset Select may read only identity-bound members (plan stream S)

        // Linked into net48: C# 7.3 only. The row type is private so the rule is proven with no provider at all.
        private const string SubsetMessage = "after a column-subset Select reads";

        private interface IHasRowId { int Id { get; } }

        private class RowBase : IHasRowId
        {
            public int Id { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
        }

        private sealed class Row : RowBase
        {
            public int? N { get; set; }
            public List<int> Tags { get; } = new List<int>();
        }

        private static IQueryable<Row> Rows()
        {
            return new[] { new Row { Id = 1, FirstName = "a", LastName = "x" }, new Row { Id = 2, FirstName = "b", LastName = "y" } }.AsQueryable();
        }

        private static NotSupportedException Rejected(IQueryable query)
        {
            return Assert.ThrowsException<NotSupportedException>(() => QueryOperatorPolicy.EnsureSupported(query.Expression));
        }

        private static void AssertSubsetRejection(IQueryable query, string op, string member)
        {
            var ex = Rejected(query);
            StringAssert.Contains(ex.Message, SubsetMessage);
            StringAssert.Contains(ex.Message, op + "(...)");
            StringAssert.Contains(ex.Message, " " + member + ",");
        }

        [TestMethod]
        public void SubsetSelect_ThenLeftOutMemberRead_Rejected()
        {
            AssertSubsetRejection(Rows().Select(r => new Row { Id = r.Id }).Where(t => t.LastName == "x"), "Where", "LastName");
            AssertSubsetRejection(Rows().Select(r => new Row { Id = r.Id }).OrderBy(t => t.FirstName), "OrderBy", "FirstName");
            AssertSubsetRejection(Rows().Select(r => new Row { Id = r.Id }).OrderBy(t => t.Id).ThenBy(t => t.FirstName), "ThenBy", "FirstName");
        }

        [TestMethod]
        public void SubsetSelect_ThenIdentityBoundRead_Passes()
        {
            // Bound from the parameter's own member of the same name, also through a cast of the parameter.
            QueryOperatorPolicy.EnsureSupported(Rows()
                .Select(r => new Row { Id = ((RowBase)r).Id, LastName = r.LastName })
                .Where(t => t.LastName == "x").OrderBy(t => t.Id).ThenBy(t => t.LastName).Expression);
        }

        [TestMethod]
        public void SubsetSelect_IdIsUnboundUnlessBoundFromItself()
        {
            AssertSubsetRejection(Rows().Select(r => new Row { LastName = r.LastName }).Where(t => t.Id > 0), "Where", "Id");
        }

        [TestMethod]
        public void SubsetSelect_CrossAndConstantBindings_AreNotBound()
        {
            AssertSubsetRejection(Rows().Select(r => new Row { Id = r.Id, FirstName = r.LastName }).Where(t => t.FirstName == "x"), "Where", "FirstName");
            AssertSubsetRejection(Rows().Select(r => new Row { Id = r.Id, FirstName = "c" }).Where(t => t.FirstName == "x"), "Where", "FirstName");
            var other = new Row { Id = 9, FirstName = "o" };
            AssertSubsetRejection(Rows().Select(r => new Row { Id = other.Id }).Where(t => t.Id > 0), "Where", "Id");
        }

        [TestMethod]
        public void SubsetSelect_ReadThroughACastOfTheParameter_IsTheParametersRead()
        {
            AssertSubsetRejection(Rows().Select(r => new Row { Id = r.Id }).Where(t => ((RowBase)t).LastName == "x"), "Where", "LastName");
            QueryOperatorPolicy.EnsureSupported(Rows().Select(r => new Row { Id = r.Id, LastName = r.LastName })
                .Where(t => ((RowBase)t).LastName == "x").Expression);
        }

        [TestMethod]
        public void SubsetSelect_CovariantInterfaceView_MatchesMembersByName()
        {
            IQueryable<IHasRowId> view = Rows().Select(r => new Row { Id = r.Id });
            // The selector reads IHasRowId.Id, a different MemberInfo from Row.Id: the comparison is by name. Max executes
            // at once in LINQ to objects, so the spine is built by hand for the policy (Max<TSource, TResult>).
            Assert.AreEqual(2, Queryable.Max(view, x => x.Id));
            var max = typeof(Queryable).GetMethods()
                .First(m => m.Name == "Max" && m.GetParameters().Length == 2 && m.GetGenericArguments().Length == 2)
                .MakeGenericMethod(typeof(IHasRowId), typeof(int));
            Expression<Func<IHasRowId, int>> selector = x => x.Id;
            var spine = Expression.Call(max, view.Expression, Expression.Quote(selector));
            QueryOperatorPolicy.EnsureSupported(spine);
            // The same view reading an unbound member is still rejected, by name.
            Expression<Func<IHasRowId, int>> unbound = x => ((RowBase)x).LastName.Length;
            var ex = Assert.ThrowsException<NotSupportedException>(() => QueryOperatorPolicy.EnsureSupported(Expression.Call(max, view.Expression, Expression.Quote(unbound))));
            StringAssert.Contains(ex.Message, " LastName,");
        }

        [TestMethod]
        public void SubsetSelect_NestedLambda_OnlyTheOuterParametersReadsCount()
        {
            var others = new List<Row> { new Row { Id = 7, LastName = "a" } };
            // o.LastName is the nested lambda's own parameter; t.FirstName is bound: passes.
            QueryOperatorPolicy.EnsureSupported(Rows().Select(r => new Row { Id = r.Id, FirstName = r.FirstName })
                .Where(t => others.Any(o => o.LastName == t.FirstName)).Expression);
            // The outer parameter's unbound member read inside the nested lambda still counts.
            AssertSubsetRejection(Rows().Select(r => new Row { Id = r.Id, FirstName = r.FirstName })
                .Where(t => others.Any(o => o.LastName == t.LastName)), "Where", "LastName");
        }

        [TestMethod]
        public void SubsetSelect_CapturedObjectOfTheRowType_IsNotARead()
        {
            var other = new Row { Id = 5, LastName = "z" };
            QueryOperatorPolicy.EnsureSupported(Rows().Select(r => new Row { FirstName = r.FirstName })
                .Where(t => t.FirstName == other.LastName).Expression);
        }

        [TestMethod]
        public void SubsetSelect_DistinctCastOfTypeBetween_NeitherResetNorWiden()
        {
            AssertSubsetRejection(Rows().Select(r => new Row { Id = r.Id }).Distinct().Where(t => t.LastName == "x"), "Where", "LastName");
            AssertSubsetRejection(Rows().Select(r => new Row { Id = r.Id }).Cast<Row>().OfType<Row>().OrderBy(t => t.FirstName), "OrderBy", "FirstName");
            QueryOperatorPolicy.EnsureSupported(Rows().Select(r => new Row { Id = r.Id }).Cast<Row>().OfType<Row>().Distinct().OrderBy(t => t.Id).Expression);
        }

        [TestMethod]
        public void SubsetSelect_ThenScalarSelect_OfAnUnboundMember_Rejected()
        {
            AssertSubsetRejection(Rows().Select(r => new Row { FirstName = r.FirstName }).Select(t => t.LastName), "Select", "LastName");
        }

        [TestMethod]
        public void SubsetSelect_ThenScalarSelect_OfABoundMember_ClearsTheSet()
        {
            // The policy has no scalar-outermost rule (the parse loop has); it must not report a subset read here.
            QueryOperatorPolicy.EnsureSupported(Rows().Select(r => new Row { FirstName = r.FirstName })
                .Select(t => t.FirstName).OrderBy(s => s.Length).Expression);
        }

        [TestMethod]
        public void SubsetSelect_ThenChainedSubsetSelect_IsCheckedThenBecomesTheSet()
        {
            AssertSubsetRejection(Rows().Select(r => new Row { FirstName = r.FirstName })
                .Select(t => new Row { FirstName = t.FirstName, LastName = t.LastName }), "Select", "LastName");
            AssertSubsetRejection(Rows().Select(r => new Row { FirstName = r.FirstName, LastName = r.LastName })
                .Select(t => new Row { FirstName = t.FirstName }).Where(t => t.LastName == "x"), "Where", "LastName");
            AssertSubsetRejection(Rows().Select(r => new Row { Id = r.Id, FirstName = r.LastName })
                .Select(t => new Row { Id = t.Id, FirstName = t.FirstName }), "Select", "FirstName");
            QueryOperatorPolicy.EnsureSupported(Rows().Select(r => new Row { FirstName = r.FirstName, LastName = r.LastName })
                .Select(t => new Row { FirstName = t.FirstName }).Where(t => t.FirstName == "x").Expression);
        }

        [TestMethod]
        public void SubsetSelect_MemberListBinding_IsUnbound_AndThrowsNothingElse()
        {
            QueryOperatorPolicy.EnsureSupported(Rows().Select(r => new Row { Id = r.Id, Tags = { 1 } }).Where(t => t.Id > 0).Expression);
            AssertSubsetRejection(Rows().Select(r => new Row { Id = r.Id, Tags = { 1 } }).Where(t => t.Tags.Count > 0), "Where", "Tags");
        }

        // Review round 1, finding 1: SQL applies DISTINCT to the final SELECT list, so a Select after Distinct projects
        // first and makes the projection distinct, where LINQ makes the rows distinct first; the policy rejects it.
        private const string DistinctMessage = "after Distinct() is not translated";

        [TestMethod]
        public void Select_AfterDistinct_Rejected_SubsetOrScalar_BoundOrNot()
        {
            var ex = Rejected(Rows().Distinct().Select(r => new Row { LastName = r.LastName }));
            StringAssert.Contains(ex.Message, DistinctMessage);
            StringAssert.Contains(ex.Message, "Select(...)");
            StringAssert.Contains(Rejected(Rows().Distinct().Select(r => r.LastName)).Message, DistinctMessage);
            StringAssert.Contains(Rejected(Rows().Select(r => new Row { Id = r.Id, LastName = r.LastName }).Distinct().Select(t => t.LastName)).Message, DistinctMessage);
            StringAssert.Contains(Rejected(Rows().Select(r => new Row { FirstName = r.FirstName, LastName = r.LastName }).Distinct().Select(t => new Row { LastName = t.LastName })).Message, DistinctMessage);
        }

        [TestMethod]
        public void Select_ThenDistinct_StillPasses()
        {
            QueryOperatorPolicy.EnsureSupported(Rows().Select(r => new Row { Id = r.Id, LastName = r.LastName }).Distinct().OrderBy(t => t.LastName).Expression);
            QueryOperatorPolicy.EnsureSupported(Rows().Select(r => r.LastName).Distinct().Expression);
            QueryOperatorPolicy.EnsureSupported(Rows().Distinct().Where(r => r.Id > 0).OrderBy(r => r.Id).Expression);
        }

        [TestMethod]
        public void SubsetSelect_AfterPaging_ThePagingRuleWins()
        {
            var ex = Rejected(Rows().OrderBy(r => r.Id).Take(1).Select(r => new Row { Id = r.Id }).Where(t => t.LastName == "x"));
            StringAssert.Contains(ex.Message, "after Skip/Take");
            Assert.IsFalse(ex.Message.Contains(SubsetMessage), ex.Message);
        }

        #endregion
    }
}
