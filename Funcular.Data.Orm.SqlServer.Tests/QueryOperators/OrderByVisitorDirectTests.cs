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
        public void MapHit_ComputedFragment_NeverPrefixed()
        {
            // A computed member ([SqlExpression]/[JsonPath]/[SubqueryAggregate]) resolves through the map to a full
            // expression; on a join entity it must not gain the base-table prefix.
            var map = new Dictionary<string, string> { ["Gender"] = "COALESCE(person.gender, 'U')" };

            var visitor = Visit(Source.OrderBy(p => p.Gender).ThenByDescending(p => p.Id).Expression, "person", map);

            Assert.AreEqual("ORDER BY COALESCE(person.gender, 'U') ASC, person.id DESC", visitor.OrderByClause);
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

        #region Remaining visitor branches (Task 9, §4.5): DB-free

        private static readonly Guid MarkerGuid = new Guid("11111111-2222-3333-4444-555555555555");

        private static OrderByClauseVisitor<PersonDetailEntity> VisitWithUnmapped(Expression ordering, string unmappedProperty)
        {
            var visitor = new OrderByClauseVisitor<PersonDetailEntity>(
                new ConcurrentDictionary<string, string>(), new List<PropertyInfo> { typeof(PersonDetailEntity).GetProperty(unmappedProperty) });
            visitor.Visit(ordering);
            return visitor;
        }

        /// <summary>The single ORDER BY fragment the visitor builds for <paramref name="key"/>.</summary>
        private static string Fragment<TKey>(Expression<Func<PersonDetailEntity, TKey>> key) =>
            Visit(Source.OrderBy(key).Expression).OrderByTerms.Single().Fragment;

        [TestMethod]
        public void NoOrdering_EmptyClauseAndNoTerms()
        {
            var visitor = new OrderByClauseVisitor<PersonDetailEntity>(new ConcurrentDictionary<string, string>(), new List<PropertyInfo>());

            Assert.AreEqual(string.Empty, visitor.OrderByClause);
            Assert.AreEqual(0, visitor.OrderByTerms.Count);
        }

        [TestMethod]
        public void LambdaNode_VisitsItsBody()
        {
            var visitor = Visit(Expression.Lambda(Source.OrderBy(p => p.Id).Expression));

            Assert.AreEqual($"ORDER BY {Fragment(p => p.Id)} ASC", visitor.OrderByClause);
        }

        [TestMethod]
        public void ConvertedMemberKey_OrdersByItsColumn()
        {
            Assert.AreEqual(Fragment(p => p.Id), Fragment(p => (object)p.Id));
            Assert.AreEqual(Fragment(p => p.Id), Fragment(p => (long)p.Id));
        }

        private static Dictionary<string, (Func<string> Actual, Func<string> Expected)> CaseRows()
        {
            var id = Fragment(p => p.Id);
            var first = Fragment(p => p.FirstName);
            var employer = Fragment(p => p.EmployerId);
            var created = Fragment(p => p.DateUtcCreated);
            var threshold = 5;
            var guid = MarkerGuid;
            string When(string test, string then = "0", string otherwise = "1") => $"CASE WHEN {test} THEN {then} ELSE {otherwise} END";
            return new Dictionary<string, (Func<string>, Func<string>)>
            {
                ["HasValue"] = (() => Fragment(p => p.EmployerId.HasValue ? 0 : 1), () => When($"{employer} IS NOT NULL")),
                [">"] = (() => Fragment(p => p.Id > 5 ? 0 : 1), () => When($"{id} > 5")),
                [">="] = (() => Fragment(p => p.Id >= 5 ? 0 : 1), () => When($"{id} >= 5")),
                ["<"] = (() => Fragment(p => p.Id < 5 ? 0 : 1), () => When($"{id} < 5")),
                ["<="] = (() => Fragment(p => p.Id <= 5 ? 0 : 1), () => When($"{id} <= 5")),
                ["!= value"] = (() => Fragment(p => p.Id != 5 ? 0 : 1), () => When($"{id} != 5")),
                ["captured variable"] = (() => Fragment(p => p.Id > threshold ? 0 : 1), () => When($"{id} > 5")),
                ["Nullable.Value"] = (() => Fragment(p => p.EmployerId.Value > 3 ? 0 : 1), () => When($"{employer} > 3")),
                ["static member"] = (() => Fragment(p => p.FirstName == string.Empty ? 0 : 1), () => When($"{first} = ''")),
                ["converted operand"] = (() => Fragment(p => (long)p.Id > 5L ? 0 : 1), () => When($"{id} > 5")),
                ["evaluated call"] = (() => Fragment(p => p.Id > Math.Max(1, 2) ? 0 : 1), () => When($"{id} > 2")),
                ["DateTime"] = (() => Fragment(p => p.DateUtcCreated > new DateTime(2000, 1, 2, 3, 4, 5) ? 0 : 1),
                                () => When($"{created} > '2000-01-02 03:04:05.000'")),
                ["quoted string"] = (() => Fragment(p => p.FirstName == "O'Brien" ? 0 : 1), () => When($"{first} = 'O''Brien'")),
                ["bool branches"] = (() => Fragment(p => p.Id > 0 ? true : false), () => When($"{id} > 0", "1", "0")),
                ["Guid branches"] = (() => Fragment(p => p.Id > 0 ? guid : Guid.Empty),
                                     () => When($"{id} > 0", $"'{MarkerGuid}'", "'00000000-0000-0000-0000-000000000000'")),
                ["char branches"] = (() => Fragment(p => p.Id > 0 ? 'a' : 'b'), () => When($"{id} > 0", "'a'", "'b'")),
                ["member and null branches"] = (() => Fragment(p => p.Id > 0 ? p.FirstName : null), () => When($"{id} > 0", first, "NULL")),
            };
        }

        public static IEnumerable<object[]> CaseRowNames => CaseRows().Keys.Select(k => new object[] { k });

        [DataTestMethod]
        [DynamicData(nameof(CaseRowNames))]
        public void Ternary_Branch_BuildsCase(string row)
        {
            var (actual, expected) = CaseRows()[row];

            Assert.AreEqual(expected(), actual());
        }

        private static readonly Dictionary<string, Action> UnsupportedShapes = new Dictionary<string, Action>
        {
            ["constant node"] = () => Visit(Expression.Constant(1)),
            ["non-ordering method"] = () => Visit(Source.Where(p => p.Id > 0).Expression),
            ["arithmetic key"] = () => Visit(Source.OrderBy(p => p.Id + 1).Expression),
            ["unmapped key"] = () => VisitWithUnmapped(Source.OrderBy(p => p.EmployerId).Expression, "EmployerId"),
            ["unmapped converted key"] = () => VisitWithUnmapped(Source.OrderBy(p => (object)p.EmployerId).Expression, "EmployerId"),
            ["&& test"] = () => Visit(Source.OrderBy(p => p.Id > 0 && p.Id < 9 ? 0 : 1).Expression),
            ["method-call test"] = () => Visit(Source.OrderBy(p => p.FirstName.StartsWith("a") ? 0 : 1).Expression),
            ["HasValue on an unmapped member"] = () => VisitWithUnmapped(Source.OrderBy(p => p.EmployerId.HasValue ? 0 : 1).Expression, "EmployerId"),
            ["unmapped member in a branch"] = () => VisitWithUnmapped(Source.OrderBy(p => p.Id > 0 ? p.EmployerId : null).Expression, "EmployerId"),
            ["member of a member"] = () => Visit(Source.OrderBy(p => p.FirstName.Length > 0 ? 0 : 1).Expression),
            ["arithmetic operand"] = () => Visit(Source.OrderBy(p => p.Id + 1 > 5 ? 0 : 1).Expression),
            // Operands that evaluate, under an operator that isn't a comparison: the operator switch rejects it.
            ["^ test"] = () => { var on = true; var off = false; Visit(Source.OrderBy(p => on ^ off ? 0 : 1).Expression); },
            ["Nullable.Value of an unmapped member"] = () => VisitWithUnmapped(Source.OrderBy(p => p.EmployerId.Value > 3 ? 0 : 1).Expression, "EmployerId"),
        };

        public static IEnumerable<object[]> UnsupportedShapeNames => UnsupportedShapes.Keys.Select(k => new object[] { k });

        [DataTestMethod]
        [DynamicData(nameof(UnsupportedShapeNames))]
        public void UnsupportedShape_ThrowsNotSupported(string shape)
        {
            Assert.ThrowsException<NotSupportedException>(UnsupportedShapes[shape]);
        }

        #endregion
    }
}
