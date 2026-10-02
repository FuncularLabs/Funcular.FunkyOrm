using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;

#pragma warning disable CS1718 // Comparisons of a member with itself are the point of these rows.

namespace Funcular.Data.Orm.SqlServer.Tests.DeleteGuard
{
    /// <summary>
    /// The DB-free rows of the delete-guard plan (docs/plans/DELETE_GUARD_PLAN.md, §4.1): <see cref="DeletePredicateGuard"/>'s
    /// classification of predicates (D2), its argument contract (D1) and its SQL check (D3).
    /// </summary>
    [TestClass]
    public class DeletePredicateGuardTests
    {
        private const string NoColumnMessage = "Delete operation WHERE clause must reference at least one column from the target table.";
        private const string SelfReferenceMessage = "Delete operation WHERE clause cannot be a self-referencing column expression.";
        private const string AlwaysTrueMessage = "Delete operation requires a non-trivial WHERE clause.";

        public class GuardRow
        {
            public int Id { get; set; }
            public int Other { get; set; }
            public string Name { get; set; }
            public bool Flag { get; set; }
        }

        public static class StaticFlags
        {
            public static bool On => true;
        }

        public class Request
        {
            public bool IncludeAll => true;
        }

        public class Holder
        {
            public static int Constructed;
            public Holder() => Constructed++;
            public bool Flag => true;
        }

        public class Wrapper
        {
            public static int Conversions;
            public static explicit operator bool(Wrapper w)
            {
                Conversions++;
                return true;
            }
        }

        public class W
        {
            public static int Comparisons;
            public static bool operator ==(W a, W b)
            {
                Comparisons++;
                return true;
            }

            public static bool operator !=(W a, W b) => !(a == b);
            public override bool Equals(object obj) => ReferenceEquals(this, obj);
            public override int GetHashCode() => 0;
        }

        public class Named
        {
            public static int ToStringCalls;
            public override string ToString()
            {
                ToStringCalls++;
                return "x";
            }
        }

        public struct NamedValue
        {
            public static int ToStringCalls;
            public override string ToString()
            {
                ToStringCalls++;
                return "x";
            }
        }

        public class Throwing
        {
            public bool Throws => throw new InvalidOperationException("read");
        }

        private static int _methodCalls;

        public static bool Method()
        {
            _methodCalls++;
            return true;
        }

        // Captured values: each is read through a closure, as a caller's local would be.
        private readonly bool capturedTrue = true;
        private readonly bool capturedFalse = false;
        private readonly int capturedA = 7;
        private readonly string capturedNull = null;
        private readonly string capturedS = "s";
        private readonly Request request = new Request();
        private readonly Wrapper capturedWrapper = new Wrapper();
        private readonly W capturedW = new W();
        private readonly W capturedW2 = new W();
        private readonly Throwing capturedObj = new Throwing();
        private readonly string roles = "admin,user";
        private readonly DayOfWeek capturedDay = DayOfWeek.Monday;
        private readonly object capturedNamed = new Named();
        private readonly NamedValue capturedNamedValue = new NamedValue();
        private readonly int? capturedNullableA = 7;
        private readonly NamedValue? capturedNullableNamedValue = new NamedValue();

        private Dictionary<string, LambdaExpression> Predicates()
        {
            var foreign = Expression.Parameter(typeof(GuardRow), "x2");
            var lambdaParameter = Expression.Parameter(typeof(GuardRow), "x");
            var foreignId = Expression.Property(foreign, nameof(GuardRow.Id));
            var lambdaId = Expression.Property(lambdaParameter, nameof(GuardRow.Id));
            var self = Expression.Equal(lambdaId, Expression.Property(lambdaParameter, nameof(GuardRow.Id)));
            var convertedSelf = Expression.Convert(Expression.Convert(self, typeof(bool?)), typeof(bool));

            return new Dictionary<string, LambdaExpression>
            {
                // Self-comparisons.
                ["selfEq"] = P(x => x.Id == x.Id),
                ["selfGe"] = P(x => x.Id >= x.Id),
                ["selfLe"] = P(x => x.Id <= x.Id),
                ["selfNe"] = P(x => x.Id != x.Id),
                ["selfGt"] = P(x => x.Id > x.Id),
                ["selfLt"] = P(x => x.Id < x.Id),
                ["notSelfNe"] = P(x => !(x.Id != x.Id)),
                ["notSelfGt"] = P(x => !(x.Id > x.Id)),
                ["longSelf"] = P(x => (long)x.Id == (long)x.Id),
                ["nullableSelf"] = P(x => (int?)x.Id == (int?)x.Id),
                ["nameSelf"] = P(x => x.Name == x.Name),
                ["idOther"] = P(x => x.Id == x.Other),
                // Or.
                ["orTrue"] = P(x => x.Id == 2 || true),
                ["orCapturedTrue"] = P(x => x.Id == 2 || capturedTrue),
                ["orStaticOn"] = P(x => x.Id == 2 || StaticFlags.On),
                ["orNowGtMin"] = P(x => x.Id == 2 || DateTime.Now > DateTime.MinValue),
                ["orCapturedAEqA"] = P(x => x.Id == 2 || capturedA == capturedA),
                ["orCapturedNullIsNull"] = P(x => x.Id == 2 || capturedNull == null),
                ["orCapturedSEqS"] = P(x => x.Id == 2 || capturedS == "s"),
                ["orCapturedFalse"] = P(x => x.Id == 2 || capturedFalse),
                ["nonShortOrTrue"] = P(x => x.Id == 2 | true),
                // And.
                ["andTrue"] = P(x => x.Id == 2 && true),
                ["selfAndId2"] = P(x => x.Id == x.Id && x.Id == 2),
                ["selfOrId2"] = P(x => x.Id == x.Id || x.Id == 2),
                ["nonShortAndFalse"] = P(x => x.Id == 2 & false),
                // De Morgan.
                ["deMorganAnd"] = P(x => !(x.Id != x.Id && x.Id == 2)),
                ["deMorganOr"] = P(x => !(x.Id != x.Id || x.Id > x.Id)),
                ["deMorganCapturedFalse"] = P(x => !(x.Id == 2 && capturedFalse)),
                // Property reads.
                ["orIncludeAll"] = P(x => x.Id == 2 || request.IncludeAll),
                // Not folded: a cast through object, and arithmetic on captured values (plan section 6).
                ["orBoxedFlag"] = P(x => x.Id == 2 || (bool)(object)x.Flag),
                ["orArithmetic"] = P(x => x.Id == 2 || capturedA + 1 == 8),
                // String Contains and a core type's ToString(), evaluated (rev 9, I1-1).
                ["orCapturedContains"] = P(x => x.Id == 2 || capturedS.Contains("s")),
                ["orCapturedContainsChar"] = P(x => x.Id == 2 || capturedS.Contains('s')),
                ["orContainsOrdinal"] = P(x => x.Id == 2 || capturedS.Contains("s", StringComparison.Ordinal)),
                ["orLiteralContains"] = P(x => x.Id == 2 || "abc".Contains("b")),
                ["rolesContainsOr"] = P(x => roles.Contains("admin") || x.Id == 2),
                ["orToString"] = P(x => x.Id == 2 || capturedS.ToString() == "s"),
                ["orIntToString"] = P(x => x.Id == 2 || capturedA.ToString() == "7"),
                ["orEnumToString"] = P(x => x.Id == 2 || capturedDay.ToString() == "Monday"),
                ["orNullableToString"] = P(x => x.Id == 2 || capturedNullableA.ToString() == "7"),
                ["orContainsFalse"] = P(x => x.Id == 2 || capturedS.Contains("z")),
                ["nameContains"] = P(x => x.Name.Contains("a")),
                ["orContainsName"] = P(x => x.Id == 2 || capturedS.Contains(x.Name)),
                // Not evaluated: another string method, and ToString() that could run user code.
                ["orStartsWith"] = P(x => x.Id == 2 || capturedS.StartsWith("s")),
                ["orObjectToString"] = P(x => x.Id == 2 || capturedNamed.ToString() == "x"),
                ["orStructToString"] = P(x => x.Id == 2 || capturedNamedValue.ToString() == "x"),
                ["orNullableStructToString"] = P(x => x.Id == 2 || capturedNullableNamedValue.ToString() == "x"),
                // Convert, hand-built.
                ["handConvert"] = Expression.Lambda<Func<GuardRow, bool>>(
                    Expression.OrElse(Expression.Equal(lambdaId, Expression.Constant(2)), convertedSelf), lambdaParameter),
                // Conditional.
                ["condTrueSelf"] = P(x => capturedTrue ? x.Id == x.Id : x.Id == 2),
                ["condFalseSelf"] = P(x => capturedFalse ? x.Id == 2 : x.Id == x.Id),
                ["condSharedTrue"] = P(x => x.Flag ? x.Id == x.Id : x.Id >= x.Id),
                ["condUnknown"] = P(x => x.Flag ? x.Id == x.Id : x.Id == 2),
                // Not evaluated.
                ["orNewHolder"] = P(x => x.Id == 2 || new Holder().Flag),
                ["orMethod"] = P(x => x.Id == 2 || Method()),
                ["orWrapperConversion"] = P(x => x.Id == 2 || (bool)capturedWrapper),
                ["orUserOperator"] = P(x => x.Id == 2 || capturedW == capturedW2),
                ["orThrows"] = P(x => x.Id == 2 || capturedObj.Throws),
                // Roots: the body uses another parameter of the entity type.
                ["foreignId2"] = Expression.Lambda<Func<GuardRow, bool>>(
                    Expression.Equal(foreignId, Expression.Constant(2)), lambdaParameter),
                ["foreignSelf"] = Expression.Lambda<Func<GuardRow, bool>>(
                    Expression.Equal(foreignId, Expression.Property(foreign, nameof(GuardRow.Id))), lambdaParameter),
                // Parameter-free.
                ["true"] = P(x => true),
                ["oneLtTwo"] = P(x => 1 < 2),
                ["abcEqAbc"] = P(x => "abc" == "abc"),
                ["capturedTrue"] = P(x => capturedTrue),
                ["false"] = P(x => false),
                // Validate's Acceptable row.
                ["idEq2"] = P(x => x.Id == 2),
            };
        }

        private static Expression<Func<GuardRow, bool>> P(Expression<Func<GuardRow, bool>> predicate) => predicate;

        [DataTestMethod]
        [DataRow("selfEq", DeletePredicateVerdict.SelfReference)]
        [DataRow("selfGe", DeletePredicateVerdict.SelfReference)]
        [DataRow("selfLe", DeletePredicateVerdict.SelfReference)]
        [DataRow("selfNe", DeletePredicateVerdict.Acceptable)]
        [DataRow("selfGt", DeletePredicateVerdict.Acceptable)]
        [DataRow("selfLt", DeletePredicateVerdict.Acceptable)]
        [DataRow("notSelfNe", DeletePredicateVerdict.SelfReference)]
        [DataRow("notSelfGt", DeletePredicateVerdict.SelfReference)]
        [DataRow("longSelf", DeletePredicateVerdict.SelfReference)]
        [DataRow("nullableSelf", DeletePredicateVerdict.SelfReference)]
        [DataRow("nameSelf", DeletePredicateVerdict.SelfReference)]
        [DataRow("idOther", DeletePredicateVerdict.Acceptable)]
        [DataRow("orTrue", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("orCapturedTrue", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("orStaticOn", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("orNowGtMin", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("orCapturedAEqA", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("orCapturedNullIsNull", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("orCapturedSEqS", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("orCapturedFalse", DeletePredicateVerdict.Acceptable)]
        [DataRow("nonShortOrTrue", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("andTrue", DeletePredicateVerdict.Acceptable)]
        [DataRow("selfAndId2", DeletePredicateVerdict.Acceptable)]
        [DataRow("selfOrId2", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("nonShortAndFalse", DeletePredicateVerdict.Acceptable)]
        [DataRow("deMorganAnd", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("deMorganOr", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("deMorganCapturedFalse", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("orIncludeAll", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("orBoxedFlag", DeletePredicateVerdict.Acceptable)]
        [DataRow("orArithmetic", DeletePredicateVerdict.Acceptable)]
        [DataRow("orCapturedContains", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("orCapturedContainsChar", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("orContainsOrdinal", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("orLiteralContains", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("rolesContainsOr", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("orToString", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("orIntToString", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("orEnumToString", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("orNullableToString", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("orContainsFalse", DeletePredicateVerdict.Acceptable)]
        [DataRow("nameContains", DeletePredicateVerdict.Acceptable)]
        [DataRow("orContainsName", DeletePredicateVerdict.Acceptable)]
        [DataRow("orStartsWith", DeletePredicateVerdict.Acceptable)]
        [DataRow("orObjectToString", DeletePredicateVerdict.Acceptable)]
        [DataRow("orStructToString", DeletePredicateVerdict.Acceptable)]
        [DataRow("orNullableStructToString", DeletePredicateVerdict.Acceptable)]
        [DataRow("handConvert", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("condTrueSelf", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("condFalseSelf", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("condSharedTrue", DeletePredicateVerdict.AlwaysTrue)]
        [DataRow("condUnknown", DeletePredicateVerdict.Acceptable)]
        [DataRow("orNewHolder", DeletePredicateVerdict.Acceptable)]
        [DataRow("orMethod", DeletePredicateVerdict.Acceptable)]
        [DataRow("orWrapperConversion", DeletePredicateVerdict.Acceptable)]
        [DataRow("orUserOperator", DeletePredicateVerdict.Acceptable)]
        [DataRow("orThrows", DeletePredicateVerdict.Acceptable)]
        [DataRow("foreignId2", DeletePredicateVerdict.Acceptable)]
        [DataRow("foreignSelf", DeletePredicateVerdict.SelfReference)]
        [DataRow("true", DeletePredicateVerdict.NoColumn)]
        [DataRow("oneLtTwo", DeletePredicateVerdict.NoColumn)]
        [DataRow("abcEqAbc", DeletePredicateVerdict.NoColumn)]
        [DataRow("capturedTrue", DeletePredicateVerdict.NoColumn)]
        [DataRow("false", DeletePredicateVerdict.NoColumn)]
        public void Classify_ReturnsTheVerdict(string key, DeletePredicateVerdict expected)
        {
            Assert.AreEqual(expected, DeletePredicateGuard.Classify(Predicates()[key]), key);
        }

        [TestMethod]
        public void Classify_NeverCallsMethodsOperatorsOrConstructors()
        {
            _methodCalls = 0;
            Holder.Constructed = 0;
            Wrapper.Conversions = 0;
            W.Comparisons = 0;
            Named.ToStringCalls = 0;
            NamedValue.ToStringCalls = 0;
            var predicates = Predicates();
            foreach (var key in new[] { "orMethod", "orNewHolder", "orWrapperConversion", "orUserOperator", "orObjectToString",
                         "orStructToString", "orNullableStructToString" })
                DeletePredicateGuard.Classify(predicates[key]);

            Assert.AreEqual(0, _methodCalls, "Method() was called");
            Assert.AreEqual(0, Holder.Constructed, "Holder's constructor was called");
            Assert.AreEqual(0, Wrapper.Conversions, "the user-defined explicit operator bool was called");
            Assert.AreEqual(0, W.Comparisons, "the user-defined operator == was called");
            Assert.AreEqual(0, Named.ToStringCalls, "a class's ToString() override was called");
            Assert.AreEqual(0, NamedValue.ToStringCalls, "a struct's ToString() override was called");
        }

        [DataTestMethod]
        [DataRow("true", NoColumnMessage)]
        [DataRow("selfEq", SelfReferenceMessage)]
        [DataRow("orTrue", AlwaysTrueMessage)]
        [DataRow("idEq2", "")]
        public void Validate_ThrowsTheMessageOfEachVerdict(string key, string expectedMessage)
        {
            var predicate = Predicates()[key];
            if (expectedMessage.Length == 0)
            {
                DeletePredicateGuard.Validate(predicate);
                return;
            }
            var exception = Assert.ThrowsException<InvalidOperationException>(() => DeletePredicateGuard.Validate(predicate), key);
            Assert.AreEqual(expectedMessage, exception.Message, key);
        }

        [DataTestMethod]
        [DataRow("classifyNull")]
        [DataRow("validateNull")]
        [DataRow("hasLiteralTautologyNull")]
        [DataRow("zeroParameters")]
        [DataRow("twoParameters")]
        [DataRow("intBody")]
        public void PublicMembers_RejectBadArguments(string kind)
        {
            switch (kind)
            {
                case "classifyNull":
                    Assert.ThrowsException<ArgumentNullException>(() => DeletePredicateGuard.Classify(null));
                    break;
                case "validateNull":
                    Assert.ThrowsException<ArgumentNullException>(() => DeletePredicateGuard.Validate(null));
                    break;
                case "hasLiteralTautologyNull":
                    Assert.ThrowsException<ArgumentNullException>(() => DeletePredicateGuard.HasLiteralTautology(null));
                    break;
                case "zeroParameters":
                    Expression<Func<bool>> zero = () => true;
                    Assert.ThrowsException<ArgumentException>(() => DeletePredicateGuard.Classify(zero));
                    break;
                case "twoParameters":
                    Expression<Func<GuardRow, GuardRow, bool>> two = (a, b) => a.Id == b.Id;
                    Assert.ThrowsException<ArgumentException>(() => DeletePredicateGuard.Classify(two));
                    break;
                case "intBody":
                    Expression<Func<GuardRow, int>> intBody = x => x.Id;
                    Assert.ThrowsException<ArgumentException>(() => DeletePredicateGuard.Classify(intBody));
                    break;
                default:
                    Assert.Fail($"Unknown case {kind}.");
                    break;
            }
        }

        [DataTestMethod]
        // True by rule 1 (a 1=1 outside quotes).
        [DataRow("1=1", true)]
        [DataRow("1 = 1", true)]
        [DataRow("(t.id = @p__linq__0 AND 1=1)", true)]
        [DataRow("1=1 = @p", true)]
        [DataRow("CASE WHEN 1 = 1 THEN 1 END = @p", true)]
        // True by the fold.
        [DataRow("1 < 2", true)]
        [DataRow("2 >= 1", true)]
        [DataRow("1.5 > 1", true)]
        [DataRow("1 != 2", true)]
        [DataRow("1 <> 2", true)]
        [DataRow("1 <= 1", true)]
        [DataRow("(2 > 1)", true)]
        [DataRow("NOT NOT 2 > 1", true)]
        [DataRow("t.id = @p OR NOT 1=0", true)]
        [DataRow("t.a = @p or not 1=0", true)]
        [DataRow("NOT (1=0 AND t.id = @p)", true)]
        // True: emitted shapes in an OR with NOT 1=0.
        [DataRow("(t.first_name IS NOT NULL OR NOT 1=0)", true)]
        [DataRow("(t.id IN (@p0, @p1) OR NOT 1=0)", true)]
        [DataRow("(t.id NOT IN (@p0) OR NOT 1=0)", true)]
        [DataRow("(COALESCE(t.a, 0) = @p OR NOT 1=0)", true)]
        [DataRow("(CAST(strftime('%Y', t.d) AS INTEGER) = @p OR NOT 1=0)", true)]
        [DataRow("(t.name LIKE CONCAT(@p, '%') OR NOT 1=0)", true)]
        [DataRow("((t.a)::int = @p OR NOT 1=0)", true)]
        [DataRow("((SELECT COUNT(*) FROM c WHERE c.pid = t.id) > @p OR NOT 1=0)", true)]
        [DataRow("(CASE WHEN t.a > 0 THEN 1 ELSE 0 END = @p OR NOT 1=0)", true)]
        [DataRow("(t.notes = @p OR NOT 1=0)", true)]
        // Also true.
        [DataRow("1=1 AND (", true)]
        [DataRow("ordinal = @p OR NOT 1=0", true)]
        [DataRow("t.c = 'abc 1=1", true)]
        // False.
        [DataRow("1=0", false)]
        [DataRow("1 <> 1", false)]
        [DataRow("2 <= 1", false)]
        [DataRow("NOT 2 > 1", false)]
        // False: the same emitted shapes in an AND with NOT 1=0.
        [DataRow("(t.first_name IS NOT NULL AND NOT 1=0)", false)]
        [DataRow("(t.id IN (@p0, @p1) AND NOT 1=0)", false)]
        [DataRow("(t.id NOT IN (@p0) AND NOT 1=0)", false)]
        [DataRow("(COALESCE(t.a, 0) = @p AND NOT 1=0)", false)]
        [DataRow("(CAST(strftime('%Y', t.d) AS INTEGER) = @p AND NOT 1=0)", false)]
        [DataRow("(t.name LIKE CONCAT(@p, '%') AND NOT 1=0)", false)]
        [DataRow("((t.a)::int = @p AND NOT 1=0)", false)]
        [DataRow("((SELECT COUNT(*) FROM c WHERE c.pid = t.id) > @p AND NOT 1=0)", false)]
        [DataRow("(CASE WHEN t.a > 0 THEN 1 ELSE 0 END = @p AND NOT 1=0)", false)]
        [DataRow("(t.notes = @p AND NOT 1=0)", false)]
        // False: clauses that don't parse, CASE absorption, keywords inside identifiers.
        [DataRow("(t.id = @p OR NOT 1=0", false)]
        [DataRow("t.c = 'abc OR NOT 1=0", false)]
        [DataRow("CASE WHEN t.a = 0 OR NOT 1 = 0 OR t.b = 0 THEN 0 ELSE 1 END = @p", false)]
        [DataRow("t.a = @p XOR NOT 1=0", false)]
        [DataRow("NOT 1=0) AND t.x = @p", false)]
        [DataRow("CASE WHEN CASE WHEN t.a = 1 THEN 1 END = 1 OR NOT 1=0 OR t.b = 0 THEN 0 END = @p", false)]
        [DataRow("NOT 1=0 OR", false)]
        [DataRow("NOT 1=0 AND", false)]
        [DataRow("t.id IN (@p OR NOT 1=0", false)]
        [DataRow("CASE WHEN t.a = 1 THEN 1 OR NOT 1=0", false)]
        [DataRow("() OR NOT 1=0", false)]
        [DataRow("t.a = @p) OR NOT 1=0", false)]
        // False: the fold, not any true term.
        [DataRow("(1=0 OR t.id = @p)", false)]
        [DataRow("(t.parent_id = @p AND NOT 1=0)", false)]
        [DataRow("(NOT 1=0 AND t.x = @p)", false)]
        [DataRow("2 > 1 AND t.id = @p", false)]
        // False: token boundaries.
        [DataRow("t.col1=1", false)]
        [DataRow("t.col1 = 1", false)]
        [DataRow("@p__linq__1=1", false)]
        [DataRow("$1=1", false)]
        // False: quoted segments.
        [DataRow("'1=1'", false)]
        [DataRow("t.c = 'x OR 1=1 OR y'", false)]
        [DataRow("[a OR 1=1 OR b] = @p", false)]
        [DataRow("\"1\"=1", false)]
        [DataRow("`1`=1", false)]
        [DataRow("t.c = 'a''1=1'", false)]
        // False: ]] inside brackets is an escaped ], not the closing bracket.
        [DataRow("[a]]1=1] = @p", false)]
        // False: a literal next to arithmetic or inside CASE.
        [DataRow("t.a * 100 > 50", false)]
        [DataRow("t.a - 1 >= 0", false)]
        [DataRow("5 < 10 * t.a", false)]
        [DataRow("CASE WHEN t.a - 1 >= 0 THEN 1 ELSE 0 END = @p", false)]
        public void HasLiteralTautology_ReturnsTheRuleOrTheFold(string whereClause, bool expected)
        {
            Assert.AreEqual(expected, DeletePredicateGuard.HasLiteralTautology(whereClause), whereClause);
        }

        /// <summary>
        /// A clause nested too deeply for the parser's stack is one it can't parse: false, not a stack overflow that
        /// ends the process. It runs on a 1 MB thread, the size of a default thread on Windows.
        /// </summary>
        [TestMethod]
        public void HasLiteralTautology_TooDeeplyNested_ReturnsFalse()
        {
            const int depth = 20000;
            var clause = new string('(', depth) + "t.id = @p OR NOT 1=0" + new string(')', depth);
            bool? result = null;
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    result = DeletePredicateGuard.HasLiteralTautology(clause);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            }, 1024 * 1024);
            thread.Start();
            thread.Join();
            Assert.IsNull(error, error?.ToString());
            Assert.AreEqual(false, result);
        }
    }
}
