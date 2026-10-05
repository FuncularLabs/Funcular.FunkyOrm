using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Funcular.Data.Orm.Linq;
using Funcular.Data.Orm.Tests.Shared;

namespace Funcular.Data.Orm.SqlServer.Tests.DotNet9
{
    /// <summary>
    /// xUnit twin of the MSTest <c>QueryOperatorPolicyTests</c> literal-set tests (AC13-7), run on net9, whose
    /// <c>Queryable</c> has methods net8's lacks (e.g. <c>AggregateBy</c>, <c>CountBy</c>, <c>Index</c>). The literal
    /// set is the same linked source file, so the twins can't drift.
    /// </summary>
    public class QueryOperatorPolicyLiteralSetTests
    {
        private static List<MethodInfo> QueryableMethods() =>
            typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static).ToList();

        [Fact]
        public void SupportedOperators_ExactLiteralSetPinned()
        {
            Assert.Equal(SupportedOperatorSignatures.Count, SupportedOperatorSignatures.All.Length);
            Assert.Equal(SupportedOperatorSignatures.Count, SupportedOperatorSignatures.All.Distinct(StringComparer.Ordinal).Count());

            var actual = QueryOperatorPolicy.SupportedOperators
                .Select(SupportedOperatorSignatures.Format)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();
            var expected = SupportedOperatorSignatures.All.OrderBy(s => s, StringComparer.Ordinal).ToList();

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ClassifierSweep_EveryQueryableMethod_MatchesLiteralSet()
        {
            var expected = new HashSet<string>(SupportedOperatorSignatures.All, StringComparer.Ordinal);
            var methods = QueryableMethods();

            var present = new HashSet<string>(methods.Select(SupportedOperatorSignatures.Format), StringComparer.Ordinal);
            Assert.Empty(expected.Where(s => !present.Contains(s)));

            var mismatches = methods
                .Where(m => QueryOperatorPolicy.IsAllowed(m) != expected.Contains(SupportedOperatorSignatures.Format(m)))
                .Select(SupportedOperatorSignatures.Format)
                .ToList();
            Assert.Empty(mismatches);

            // net9-only methods exist here and must be rejected.
            Assert.Contains(methods, m => m.Name == "CountBy");
            Assert.All(methods.Where(m => m.Name is "AggregateBy" or "CountBy" or "Index"),
                m => Assert.False(QueryOperatorPolicy.IsAllowed(m), SupportedOperatorSignatures.Format(m)));
        }

        [Fact]
        public void NonQueryableOverload_IsRejected()
        {
            var enumerableWhere = typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Where" && m.GetParameters()[1].ParameterType.GetGenericArguments().Length == 2);
            Assert.False(QueryOperatorPolicy.IsAllowed(enumerableWhere));

            var nonGenericSum = typeof(Queryable).GetMethod("Sum", new[] { typeof(IQueryable<int>) })!;
            Assert.False(QueryOperatorPolicy.IsAllowed(nonGenericSum));
        }
    }
}
