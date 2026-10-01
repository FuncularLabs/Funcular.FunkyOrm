using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.Sqlite.Visitors;

namespace Funcular.Data.Orm.Sqlite.Tests.QueryOperators
{
    /// <summary>
    /// DB-free tests of the SQLite order-by visitor: the table qualifier (#12), <c>OrderByTerms</c> and duplicate
    /// removal (AC12-9/AC13-2), and ternary null tests (AC12-8). Covers branches <c>ParseExpression</c> can't reach.
    /// </summary>
    [TestClass]
    public class SqliteOrderByVisitorDirectTests
    {
        private static readonly IQueryable<PersonDetailEntity> Source = new List<PersonDetailEntity>().AsQueryable();

        private static SqliteOrderByClauseVisitor<PersonDetailEntity> Visit(Expression ordering, string tableQualifier = null,
            IReadOnlyDictionary<string, string> map = null)
        {
            var visitor = new SqliteOrderByClauseVisitor<PersonDetailEntity>(
                new ConcurrentDictionary<string, string>(), new List<PropertyInfo>(), map, tableQualifier);
            visitor.Visit(ordering);
            return visitor;
        }

        private static string Terms(SqliteOrderByClauseVisitor<PersonDetailEntity> visitor) =>
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
            var map = new Dictionary<string, string> { ["EmployerHeadquartersCountryName"] = "\"country_0\".Name" };

            var visitor = Visit(Source.OrderBy(p => p.EmployerHeadquartersCountryName).ThenBy(p => p.Id).Expression, "person", map);

            Assert.AreEqual("ORDER BY \"country_0\".Name ASC, person.id ASC", visitor.OrderByClause);
        }

        [TestMethod]
        public void MapHit_ComputedFragment_NeverPrefixed()
        {
            // A computed member ([SqlExpression]/[JsonPath]/[SubqueryAggregate]) resolves through the map to a full
            // expression; on a join entity it must not gain the base-table prefix. SQLite spells computed fragments with
            // the bare table name, as in COALESCE(project.score, 0).
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

        private static SqliteOrderByClauseVisitor<PersonDetailEntity> VisitWithUnmapped(Expression ordering, string unmappedProperty)
        {
            var visitor = new SqliteOrderByClauseVisitor<PersonDetailEntity>(
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
            var visitor = new SqliteOrderByClauseVisitor<PersonDetailEntity>(new ConcurrentDictionary<string, string>(), new List<PropertyInfo>());

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
            string noneString = null;
            int? noneInt = null;
            var someName = "a";
            var names = new[] { "a" };
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
                ["captured null =="] = (() => Fragment(p => p.FirstName == noneString ? 0 : 1), () => When($"{first} IS NULL")),
                ["captured null !=, reversed"] = (() => Fragment(p => noneString != p.FirstName ? 0 : 1), () => When($"{first} IS NOT NULL")),
                ["captured int? null"] = (() => Fragment(p => p.EmployerId == noneInt ? 0 : 1), () => When($"{employer} IS NULL")),
                ["captured int? null, reversed"] = (() => Fragment(p => noneInt == p.EmployerId ? 0 : 1), () => When($"{employer} IS NULL")),
                ["null computed by a nested lambda"] = (() => Fragment(p => p.FirstName == names.FirstOrDefault(n => n.Length > 100) ? 0 : 1), () => When($"{first} IS NULL")),
                ["captured value"] = (() => Fragment(p => p.FirstName == someName ? 0 : 1), () => When($"{first} = 'a'")),
                ["member and null branches"] = (() => Fragment(p => p.Id > 0 ? p.FirstName : null), () => When($"{id} > 0", first, "NULL")),
            };
        }

        private static int _evaluations;

        private static string Counted()
        {
            _evaluations++;
            return "x";
        }

        private static string NullOnFirstCall() => _evaluations++ == 0 ? null : "x";

        [TestMethod]
        public void TernaryOperand_EvaluatedOnce_NullCheckAndSqlAgree()
        {
            var first = Fragment(p => p.FirstName);

            _evaluations = 0;
            var counted = Fragment(p => p.FirstName == Counted() ? 0 : 1);
            Assert.AreEqual(1, _evaluations, "a value operand is evaluated once");
            Assert.AreEqual($"CASE WHEN {first} = 'x' THEN 0 ELSE 1 END", counted);

            // Evaluating twice could see null, then a value: the null check and the SQL would disagree.
            _evaluations = 0;
            var nullFirst = Fragment(p => NullOnFirstCall() == p.FirstName ? 0 : 1);
            Assert.AreEqual(1, _evaluations, "a null operand is evaluated once");
            Assert.AreEqual($"CASE WHEN {first} IS NULL THEN 0 ELSE 1 END", nullFirst, "the column stays in the test");
        }

        private static string ThrowOnce()
        {
            if (_evaluations++ == 0)
                throw new InvalidOperationException("first call");
            return null;
        }

        [TestMethod]
        public void TernaryOperand_ThatThrows_EvaluatedOnce_Rejected()
        {
            // A retry after the failure could see a different outcome (here: null) and emit SQL the check never saw.
            _evaluations = 0;
            Assert.ThrowsException<NotSupportedException>(() => Fragment(p => p.FirstName == ThrowOnce() ? 0 : 1));
            Assert.AreEqual(1, _evaluations, "the operand is evaluated once, also when it throws");
        }

        private static string ThrowingProperty => throw new InvalidOperationException("property");

        private static string AlwaysThrows() => throw new InvalidOperationException("call");

        [TestMethod]
        public void TernaryOperand_ThatThrows_KeepsThe390Message()
        {
            var call = Assert.ThrowsException<NotSupportedException>(() => Fragment(p => p.FirstName == AlwaysThrows() ? 0 : 1));
            Assert.AreEqual("Unsupported expression in ORDER BY branch: Call", call.Message);
            var member = Assert.ThrowsException<NotSupportedException>(() => Fragment(p => p.FirstName == ThrowingProperty ? 0 : 1));
            StringAssert.StartsWith(member.Message, "Unsupported member expression in ORDER BY: ");
            StringAssert.EndsWith(member.Message, "ThrowingProperty");
        }

        public enum SmallKind : byte
        {
            X = 7,
        }

        public enum LargeKind : long
        {
            Big = 5000000000,
        }

        public enum ProbeKind
        {
            A = 1,
            B = 2,
        }

        public class CharProbe
        {
            public int Id { get; set; }
            public char Initial { get; set; }
            public char? MaybeInitial { get; set; }
            public ProbeKind Kind { get; set; }
            public SmallKind Small { get; set; }
            public LargeKind Large { get; set; }
        }

        private static string ProbeFragment<TKey>(Expression<Func<CharProbe, TKey>> key)
        {
            var visitor = new SqliteOrderByClauseVisitor<CharProbe>(new ConcurrentDictionary<string, string>(), new List<PropertyInfo>());
            visitor.Visit(new List<CharProbe>().AsQueryable().OrderBy(key).Expression);
            return visitor.OrderByTerms.Single().Fragment;
        }

        [TestMethod]
        public void CapturedCharAndEnum_FormatAsTheirValues()
        {
            var initial = ProbeFragment(x => x.Initial);
            var maybe = ProbeFragment(x => x.MaybeInitial);
            var kind = ProbeFragment(x => x.Kind);
            var c = 'x';
            char? nc = 'y';
            var k = ProbeKind.B;

            // A char or enum comparison compiles through an int conversion. A captured char stays a char literal, as
            // in 3.9.0; an enum is its underlying number, which is how it's stored and how LINQ orders it.
            Assert.AreEqual($"CASE WHEN {initial} = 'x' THEN 0 ELSE 1 END", ProbeFragment(x => x.Initial == c ? 0 : 1));
            Assert.AreEqual($"CASE WHEN {maybe} = 'y' THEN 0 ELSE 1 END", ProbeFragment(x => x.MaybeInitial == nc ? 0 : 1));
            Assert.AreEqual($"CASE WHEN {kind} = 2 THEN 0 ELSE 1 END", ProbeFragment(x => x.Kind == k ? 0 : 1));
            Assert.AreEqual($"CASE WHEN {initial} = 'x' THEN 2 ELSE 1 END", ProbeFragment(x => x.Initial == c ? ProbeKind.B : ProbeKind.A));

            // Enums over other underlying types keep their number (an Int32 conversion would overflow for long).
            var small = SmallKind.X;
            var large = LargeKind.Big;
            Assert.AreEqual($"CASE WHEN {ProbeFragment(x => x.Small)} = 7 THEN 0 ELSE 1 END", ProbeFragment(x => x.Small == small ? 0 : 1));
            Assert.AreEqual($"CASE WHEN {ProbeFragment(x => x.Large)} = 5000000000 THEN 0 ELSE 1 END", ProbeFragment(x => x.Large == large ? 0 : 1));
        }

        [TestMethod]
        public void CheckedConversion_IsEvaluated_NotUnwrapped()
        {
            // As in 3.9.0, only Convert is read through. A checked conversion is part of the value: (int)2.7 is 2, and
            // an overflowing one is rejected, never emitted as the unconverted number.
            var id = Fragment(p => p.Id);
            var d = 2.7;
            long big = 4294967297L;

            Assert.AreEqual($"CASE WHEN {id} = 2 THEN 0 ELSE 1 END", Fragment(p => p.Id == checked((int)d) ? 0 : 1));
            Assert.ThrowsException<NotSupportedException>(() => Fragment(p => p.Id == checked((int)big) ? 0 : 1));
        }

        private string InstanceName => "inst";

        [TestMethod]
        public void CapturedInstanceProperty_SameValueInTestAndBranch()
        {
            // A property of the enclosing object (captured `this`) is read in both positions; 3.9.0 emitted NULL.
            var first = Fragment(p => p.FirstName);

            Assert.AreEqual($"CASE WHEN {first} = 'inst' THEN 0 ELSE 1 END", Fragment(p => p.FirstName == InstanceName ? 0 : 1));
            Assert.AreEqual($"CASE WHEN {Fragment(p => p.Id)} > 0 THEN 'inst' ELSE 'z' END", Fragment(p => p.Id > 0 ? InstanceName : "z"));
            Assert.AreEqual($"CASE WHEN {Fragment(p => p.Id)} > 0 THEN 'z' ELSE 'inst' END", Fragment(p => p.Id > 0 ? "z" : InstanceName));
        }

        [TestMethod]
        public void BlockOperand_DeclaredVariable_DoesNotReadTheRow()
        {
            // A hand-built operand can declare block variables; they don't make it row-dependent.
            var p = Expression.Parameter(typeof(PersonDetailEntity), "p");
            var v = Expression.Variable(typeof(string), "v");
            var block = Expression.Block(new[] { v }, Expression.Assign(v, Expression.Constant(null, typeof(string))), v);
            var key = Expression.Lambda<Func<PersonDetailEntity, int>>(Expression.Condition(
                Expression.Equal(Expression.Property(p, "FirstName"), block), Expression.Constant(0), Expression.Constant(1)), p);

            Assert.AreEqual($"CASE WHEN {Fragment(x => x.FirstName)} IS NULL THEN 0 ELSE 1 END", Fragment(key));

            // So is a catch variable.
            var e = Expression.Variable(typeof(Exception), "e");
            var tryCatch = Expression.TryCatch(Expression.Constant(null, typeof(string)), Expression.Catch(e, Expression.Constant(null, typeof(string))));
            var tryKey = Expression.Lambda<Func<PersonDetailEntity, int>>(Expression.Condition(
                Expression.Equal(Expression.Property(p, "FirstName"), tryCatch), Expression.Constant(0), Expression.Constant(1)), p);
            Assert.AreEqual($"CASE WHEN {Fragment(x => x.FirstName)} IS NULL THEN 0 ELSE 1 END", Fragment(tryKey));
        }

        [TestMethod]
        public void Constructor_390Signature_IsKept()
        {
            // Binary compatibility with 3.9.0: code compiled against (columns, unmapped, map) must still bind.
            var ctor = typeof(SqliteOrderByClauseVisitor<PersonDetailEntity>).GetConstructor(new[]
                { typeof(ConcurrentDictionary<string, string>), typeof(ICollection<PropertyInfo>), typeof(IReadOnlyDictionary<string, string>) });
            Assert.IsNotNull(ctor, "the 3.9.0 constructor");
            var visitor = (SqliteOrderByClauseVisitor<PersonDetailEntity>)ctor.Invoke(new object[] { new ConcurrentDictionary<string, string>(), new List<PropertyInfo>(), null });
            visitor.Visit(Source.OrderBy(p => p.Id).Expression);
            StringAssert.StartsWith(visitor.OrderByClause, "ORDER BY ");
            Assert.IsFalse(visitor.OrderByClause.Contains("."), "no table qualifier through the 3.9.0 constructor");
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

        #region ORDER BY values as parameters (Task 12, AC12-10): DB-free

        private static SqliteOrderByClauseVisitor<PersonDetailEntity> ParameterVisitor() =>
            new SqliteOrderByClauseVisitor<PersonDetailEntity>(new ConcurrentDictionary<string, string>(), new List<PropertyInfo>(), null, null, new SqliteParameterGenerator());

        private static (SqliteOrderByClauseVisitor<PersonDetailEntity> Visitor, string Fragment) WithParameters<TKey>(Expression<Func<PersonDetailEntity, TKey>> key)
        {
            var visitor = ParameterVisitor();
            visitor.Visit(Source.OrderBy(key).Expression);
            return (visitor, visitor.OrderByTerms.Single().Fragment);
        }

        [TestMethod]
        public void ParameterMode_TextValue_IsAParameter()
        {
            var first = Fragment(p => p.FirstName);
            var text = "it's a \\' test";
            var (visitor, fragment) = WithParameters(p => p.FirstName == text ? 0 : 1);

            Assert.AreEqual(1, visitor.Parameters.Count);
            Assert.AreEqual(text, visitor.Parameters[0].Value);
            Assert.AreEqual($"CASE WHEN {first} = {visitor.Parameters[0].ParameterName} THEN 0 ELSE 1 END", fragment);
        }

        [TestMethod]
        public void ParameterMode_TextBranchValues_AreParameters_NumbersAndNullStayInline()
        {
            var id = Fragment(p => p.Id);
            var (visitor, fragment) = WithParameters(p => p.Id > 5 ? "high" : "low");

            Assert.AreEqual($"CASE WHEN {id} > 5 THEN {visitor.Parameters[0].ParameterName} ELSE {visitor.Parameters[1].ParameterName} END", fragment);
            CollectionAssert.AreEqual(new object[] { "high", "low" }, visitor.Parameters.Select(x => x.Value).ToList());

            var (nullVisitor, nullFragment) = WithParameters(p => p.FirstName == null ? 0 : 1);
            Assert.AreEqual(0, nullVisitor.Parameters.Count);
            StringAssert.Contains(nullFragment, "IS NULL");
        }

        [TestMethod]
        public void ParameterMode_EqualValues_ShareOneParameter()
        {
            var visitor = ParameterVisitor();
            visitor.Visit(Source.OrderBy(p => p.FirstName == "b" ? 1 : 0).ThenBy(p => p.FirstName == "b" ? 1 : 0).Expression);

            Assert.AreEqual(1, visitor.OrderByTerms.Count, "the same CASE twice is still one term");
            Assert.AreEqual(1, visitor.Parameters.Count);
        }

        [TestMethod]
        public void ParameterMode_CharGuidAndDates_AreParameters()
        {
            var g = MarkerGuid;
            var (guids, _) = WithParameters(p => p.Id > 0 ? g : Guid.Empty);
            CollectionAssert.AreEqual(new object[] { g, Guid.Empty }, guids.Parameters.Select(x => x.Value).ToList());

            var d = new DateTime(2000, 1, 2, 3, 4, 5);
            var (dates, _) = WithParameters(p => p.DateUtcCreated > d ? 0 : 1);
            Assert.AreEqual(d, dates.Parameters.Single().Value);

            var dto = new DateTimeOffset(2000, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));
            var (offsets, _) = WithParameters(p => p.Id > 0 ? dto : DateTimeOffset.MinValue);
            Assert.AreEqual(dto, offsets.Parameters[0].Value);

            var c = 'x';
            var probe = new SqliteOrderByClauseVisitor<CharProbe>(new ConcurrentDictionary<string, string>(), new List<PropertyInfo>(), null, null, new SqliteParameterGenerator());
            probe.Visit(new List<CharProbe>().AsQueryable().OrderBy(x => x.Initial == c ? 0 : 1).Expression);
            Assert.AreEqual("x", probe.Parameters.Single().Value, "a char is sent as text, as 3.9.0 wrote it");
        }

        #endregion
    }
}
