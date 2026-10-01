using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.Visitors;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests.QueryOperators
{
    /// <summary>
    /// DB-free tests of the SQL Server order-by visitor: the table qualifier (#12), <c>OrderByTerms</c> and duplicate
    /// removal (AC12-9/AC13-2), and ternary null tests (AC12-8). Covers branches <c>ParseExpression</c> can't reach.
    /// </summary>
    [TestClass]
    public class OrderByVisitorDirectTests
    {
        private static readonly IQueryable<PersonDetailEntity> Source = new List<PersonDetailEntity>().AsQueryable();

        private static OrderByClauseVisitor<PersonDetailEntity> Visit(Expression ordering, string tableQualifier = null,
            IReadOnlyDictionary<string, string> map = null)
        {
            var visitor = new OrderByClauseVisitor<PersonDetailEntity>(
                new ConcurrentDictionary<string, string>(), new List<PropertyInfo>(), map, tableQualifier);
            visitor.Visit(ordering);
            return visitor;
        }

        private static string Terms(OrderByClauseVisitor<PersonDetailEntity> visitor) =>
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
            var map = new Dictionary<string, string> { ["EmployerHeadquartersCountryName"] = "[country_0].name" };

            var visitor = Visit(Source.OrderBy(p => p.EmployerHeadquartersCountryName).ThenBy(p => p.Id).Expression, "person", map);

            Assert.AreEqual("ORDER BY [country_0].name ASC, person.id ASC", visitor.OrderByClause);
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
            var expression = ((IOrderedQueryable<PersonDetailEntity>)Source).ThenByDescending(p => p.Id).Expression;

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
        [DataRow(0, "person.middle_initial IS NULL", DisplayName = "x.M == null")]
        [DataRow(1, "person.middle_initial IS NULL", DisplayName = "null == x.M")]
        [DataRow(2, "person.middle_initial IS NOT NULL", DisplayName = "x.M != null")]
        [DataRow(3, "person.middle_initial IS NOT NULL", DisplayName = "null != x.M")]
        public void TernaryNullTest_EmitsIsNull_EitherOperandOrder(int spelling, string expectedTest)
        {
            Expression<Func<PersonDetailEntity, int>> key;
            switch (spelling)
            {
                case 0: key = p => p.MiddleInitial == null ? 0 : 1; break;
                case 1: key = p => null == p.MiddleInitial ? 0 : 1; break;
                case 2: key = p => p.MiddleInitial != null ? 0 : 1; break;
                case 3: key = p => null != p.MiddleInitial ? 0 : 1; break;
                default: throw new ArgumentOutOfRangeException(nameof(spelling));
            }

            var visitor = Visit(Source.OrderBy(key).Expression, "person");

            Assert.AreEqual($"ORDER BY CASE WHEN {expectedTest} THEN 0 ELSE 1 END ASC", visitor.OrderByClause);
        }

        [TestMethod]
        public void Ternary_OwnColumns_QualifiedInsideCase()
        {
            var visitor = Visit(Source.OrderBy(p => p.MiddleInitial == "Z" ? p.Id : 0).Expression, "person");

            Assert.AreEqual("ORDER BY CASE WHEN person.middle_initial = 'Z' THEN person.id ELSE 0 END ASC", visitor.OrderByClause);
        }
    }
}
