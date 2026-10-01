using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Funcular.Data.Orm.MySql.Tests.Domain;
using Funcular.Data.Orm.MySql.Visitors;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.MySql.Tests.QueryOperators
{
    /// <summary>
    /// DB-free tests of the MySQL order-by visitor: the table qualifier (#12), <c>OrderByTerms</c> and duplicate
    /// removal (AC12-9/AC13-2), and ternary null tests (AC12-8). Covers branches <c>ParseExpression</c> can't reach.
    /// </summary>
    [TestClass]
    public class MySqlOrderByVisitorDirectTests
    {
        private static readonly IQueryable<PersonWithEmployer> Source = new List<PersonWithEmployer>().AsQueryable();

        /// <summary>
        /// The column cache as schema discovery fills it. The MySQL test entities are convention-mapped (no
        /// <c>[Column]</c>), so an empty cache would fall back to the lowercased property name (<c>firstname</c>).
        /// </summary>
        private static ConcurrentDictionary<string, string> DiscoveredColumns()
        {
            var columns = new ConcurrentDictionary<string, string>();
            columns[typeof(PersonWithEmployer).GetProperty(nameof(PersonWithEmployer.Id)).ToDictionaryKey()] = "id";
            columns[typeof(PersonWithEmployer).GetProperty(nameof(PersonWithEmployer.FirstName)).ToDictionaryKey()] = "first_name";
            columns[typeof(PersonWithEmployer).GetProperty(nameof(PersonWithEmployer.EmployerId)).ToDictionaryKey()] = "employer_id";
            return columns;
        }

        private static MySqlOrderByClauseVisitor<PersonWithEmployer> Visit(Expression ordering, string tableQualifier = null,
            IReadOnlyDictionary<string, string> map = null)
        {
            var visitor = new MySqlOrderByClauseVisitor<PersonWithEmployer>(
                DiscoveredColumns(), new List<PropertyInfo>(), map, tableQualifier);
            visitor.Visit(ordering);
            return visitor;
        }

        private static string Terms(MySqlOrderByClauseVisitor<PersonWithEmployer> visitor) =>
            string.Join(", ", visitor.OrderByTerms.Select(t => t.Fragment + (t.IsDescending ? " DESC" : " ASC")));

        [TestMethod]
        public void TableQualifier_QualifiesOwnColumn()
        {
            var visitor = Visit(Source.OrderBy(p => p.Id).ThenByDescending(p => p.FirstName).Expression, "person");

            Assert.AreEqual("ORDER BY person.id ASC, person.first_name DESC", visitor.OrderByClause);
        }

        [TestMethod]
        public void NoTableQualifier_BareColumn_Unchanged()
        {
            var visitor = Visit(Source.OrderBy(p => p.Id).ThenByDescending(p => p.FirstName).Expression);

            Assert.AreEqual("ORDER BY id ASC, first_name DESC", visitor.OrderByClause);
        }

        [TestMethod]
        public void MapHit_NeverPrefixed()
        {
            var map = new Dictionary<string, string> { ["EmployerName"] = "`organization_0`.name" };

            var visitor = Visit(Source.OrderBy(p => p.EmployerName).ThenBy(p => p.Id).Expression, "person", map);

            Assert.AreEqual("ORDER BY `organization_0`.name ASC, person.id ASC", visitor.OrderByClause);
        }

        [TestMethod]
        public void MapHit_ComputedFragment_NeverPrefixed()
        {
            // A computed member ([SqlExpression]/[JsonPath]/[SubqueryAggregate]) resolves through the map to a full
            // expression; with a table qualifier it must not gain the base-table prefix. The fragment is
            // ProjectScorecard.EffectiveScore as 3.9.0 resolves it (unquoted, like every MySQL computed fragment).
            var map = new Dictionary<string, string> { [nameof(ProjectScorecard.EffectiveScore)] = "COALESCE(project.score, 0)" };
            var columns = new ConcurrentDictionary<string, string>();
            columns[typeof(ProjectScorecard).GetProperty(nameof(ProjectScorecard.Id)).ToDictionaryKey()] = "id";
            var visitor = new MySqlOrderByClauseVisitor<ProjectScorecard>(columns, new List<PropertyInfo>(), map, "project");

            visitor.Visit(new List<ProjectScorecard>().AsQueryable().OrderBy(p => p.EffectiveScore).ThenByDescending(p => p.Id).Expression);

            Assert.AreEqual("ORDER BY COALESCE(project.score, 0) ASC, project.id DESC", visitor.OrderByClause);
        }

        [TestMethod]
        public void OrderByTerms_ExposeFragmentsAndDirections()
        {
            var visitor = Visit(Source.OrderBy(p => p.Id).ThenByDescending(p => p.FirstName).Expression, "person");

            Assert.AreEqual("person.id ASC, person.first_name DESC", Terms(visitor));
        }

        [TestMethod]
        public void OrderByTerms_OnRootThenBy_SeedTheFirstTerm()
        {
            var expression = ((IOrderedQueryable<PersonWithEmployer>)Source).ThenByDescending(p => p.Id).Expression;

            var visitor = Visit(expression, "person");

            Assert.AreEqual("person.id DESC", Terms(visitor));
        }

        [TestMethod]
        public void DuplicateKey_LaterFragmentDropped()
        {
            var visitor = Visit(Source.OrderBy(p => p.Id).ThenBy(p => p.FirstName).ThenByDescending(p => p.Id).Expression, "person");

            Assert.AreEqual("ORDER BY person.id ASC, person.first_name ASC", visitor.OrderByClause);
            Assert.AreEqual("person.id ASC, person.first_name ASC", Terms(visitor));
        }

        [DataTestMethod]
        [DataRow(0, "person.employer_id IS NULL", DisplayName = "x.M == null")]
        [DataRow(1, "person.employer_id IS NULL", DisplayName = "null == x.M")]
        [DataRow(2, "person.employer_id IS NOT NULL", DisplayName = "x.M != null")]
        [DataRow(3, "person.employer_id IS NOT NULL", DisplayName = "null != x.M")]
        public void TernaryNullTest_EmitsIsNull_EitherOperandOrder(int spelling, string expectedTest)
        {
            Expression<Func<PersonWithEmployer, int>> key;
            switch (spelling)
            {
                case 0: key = p => p.EmployerId == null ? 0 : 1; break;
                case 1: key = p => null == p.EmployerId ? 0 : 1; break;
                case 2: key = p => p.EmployerId != null ? 0 : 1; break;
                case 3: key = p => null != p.EmployerId ? 0 : 1; break;
                default: throw new ArgumentOutOfRangeException(nameof(spelling));
            }

            var visitor = Visit(Source.OrderBy(key).Expression, "person");

            Assert.AreEqual($"ORDER BY CASE WHEN {expectedTest} THEN 0 ELSE 1 END ASC", visitor.OrderByClause);
        }

        [TestMethod]
        public void Ternary_OwnColumns_QualifiedInsideCase()
        {
            var visitor = Visit(Source.OrderBy(p => p.EmployerId == 7 ? p.Id : 0).Expression, "person");

            Assert.AreEqual("ORDER BY CASE WHEN person.employer_id = 7 THEN person.id ELSE 0 END ASC", visitor.OrderByClause);
        }
    }
}
