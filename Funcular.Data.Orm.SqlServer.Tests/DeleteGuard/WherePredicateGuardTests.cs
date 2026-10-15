using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;

#pragma warning disable CS1718 // Comparisons of a member with itself are the point of these rows.

namespace Funcular.Data.Orm.SqlServer.Tests.DeleteGuard
{
    /// <summary>
    /// The DB-free rows of the delete-guard plan (docs/plans/DELETE_GUARD_PLAN.md, §4.1), on the guard of
    /// docs/plans/DECISIONS-3.10.2.md D-003 as amended by D-128: <see cref="WherePredicateGuard"/>'s classification of
    /// predicates (D2), its argument contract (D1), its SQL check (D3), its <c>Fold</c> (the C# truth of a part that
    /// doesn't read the row, which every path sends as a literal); and the obsolete <see cref="DeletePredicateGuard"/>,
    /// which forwards to it. D9's rejections are gone: what they guarded is evaluated in C# on every path.
    /// </summary>
    [TestClass]
    public class WherePredicateGuardTests
    {
        private const string NoColumnMessage = "The WHERE clause must reference at least one column of the entity.";
        private const string SelfReferenceMessage = "The WHERE clause cannot be a self-referencing column expression.";
        private const string AlwaysTrueMessage = "The WHERE clause is trivially true; a non-trivial WHERE clause is required.";
        private const string EmptyMessage = "The WHERE clause translated to nothing; a non-trivial WHERE clause is required.";
        private const string UnboundedSearchMessage = "The WHERE clause's Contains, StartsWith or EndsWith has an empty search value, which matches every non-null row; a non-trivial WHERE clause is required.";

        public class GuardRow
        {
            public int Id { get; set; }
            public int Other { get; set; }
            public string Name { get; set; }
            public bool Flag { get; set; }
            public int? N { get; set; }
            public double Amount { get; set; }
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

        public class Bag
        {
            public static int ContainsCalls;
            public bool Contains(string value)
            {
                ContainsCalls++;
                return true;
            }
        }

        public class CountingProvider : IFormatProvider
        {
            public static int Calls;
            public object GetFormat(Type formatType)
            {
                Calls++;
                return null;
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
        private readonly string capturedWildcard = "a%";
        private readonly string capturedUpperA = "A";
        private readonly string capturedEmpty = "";
        private string FilterProperty { get; } = "b";
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
        private readonly DateTime capturedDate = new DateTime(2020, 1, 5);
        private readonly Bag capturedBag = new Bag();
        private readonly CountingProvider capturedProvider = new CountingProvider();
        private readonly KeyValuePair<string, Named> capturedPair = new KeyValuePair<string, Named>("k", new Named());

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
                // D9 (owner 2026-10-05): shapes a delete can't send safely, and their safe neighbours.
                ["nameContainsUnderscore"] = P(x => x.Name.Contains("_")),
                ["nameStartsWithPercent"] = P(x => x.Name.StartsWith("a%")),
                ["nameEndsWithBracket"] = P(x => x.Name.EndsWith("[b")),
                ["nameContainsBackslash"] = P(x => x.Name.Contains("\\")),
                ["nameContainsCharUnderscore"] = P(x => x.Name.Contains('_')),
                ["nameContainsCapturedWildcard"] = P(x => x.Name.Contains(capturedWildcard)),
                ["nameContainsNull"] = P(x => x.Name.Contains(capturedNull)),
                ["nameContainsEmpty"] = P(x => x.Name.Contains("")),
                // Review R-1 (A-1): an empty search value matches every non-null row, so a mass mutation is unbounded.
                ["nameStartsWithEmpty"] = P(x => x.Name.StartsWith("")),
                ["nameEndsWithEmpty"] = P(x => x.Name.EndsWith("")),
                ["nameContainsCapturedEmpty"] = P(x => x.Name.Contains(capturedEmpty)),
                ["nameContainsStringEmpty"] = P(x => x.Name.Contains(string.Empty)),
                ["idAndNameContainsEmpty"] = P(x => x.Id == 2 && x.Name.Contains("")),
                ["idOrNameContainsEmpty"] = P(x => x.Id == 2 || x.Name.Contains("")),
                // Review R-1 (A-2, A-3): a self-comparison folds to a truth only where C# makes it one — not an ordered
                // comparison of a nullable, not a floating-point equality (NaN), not a member of a member.
                ["nSelfGe"] = P(x => x.N >= x.N),
                ["nSelfEq"] = P(x => x.N == x.N),
                ["amountSelfEq"] = P(x => x.Amount == x.Amount),
                ["nameLengthSelf"] = P(x => x.Name.Length == x.Name.Length),
                ["nameContainsThisProperty"] = P(x => x.Name.Contains(FilterProperty)),
                ["nameEqualsCapturedToString"] = P(x => x.Name == capturedS.ToString()),
                ["nameContainsCaptured"] = P(x => x.Name.Contains(capturedS)),
                ["nameStartsWithA"] = P(x => x.Name.StartsWith("a")),
                ["idToStringEq7"] = P(x => x.Id.ToString() == "7"),
                ["orCapturedStringEquals"] = P(x => x.Id == 2 || capturedUpperA == "a"),
                ["orCapturedStringNotEquals"] = P(x => x.Id == 2 || capturedUpperA != "A"),
                ["capturedNullCheckOr"] = P(x => capturedS == null || x.Name == capturedS),
                ["capturedEqualsName"] = P(x => capturedS == x.Name),
                ["idToStringSelf"] = P(x => x.Id.ToString() == x.Id.ToString()),
                ["nameToStringSelf"] = P(x => x.Name.ToString() == x.Name),
                ["idToStringSelfNe"] = P(x => x.Id.ToString() != x.Id.ToString()),
                ["idToStringOther"] = P(x => x.Id.ToString() == x.Other.ToString()),
                ["idToStringFormatSelf"] = P(x => x.Id.ToString("D2") == x.Id.ToString("D2")),
                ["nameContains"] = P(x => x.Name.Contains("a")),
                ["orContainsName"] = P(x => x.Id == 2 || capturedS.Contains(x.Name)),
                // Not evaluated: another string method, and ToString() that could run user code.
                ["orStartsWith"] = P(x => x.Id == 2 || capturedS.StartsWith("s")),
                ["orObjectToString"] = P(x => x.Id == 2 || capturedNamed.ToString() == "x"),
                ["orStructToString"] = P(x => x.Id == 2 || capturedNamedValue.ToString() == "x"),
                ["orNullableStructToString"] = P(x => x.Id == 2 || capturedNullableNamedValue.ToString() == "x"),
                // Fix-verification FV1 (rev 10): a format string; a null search value; shapes section 6 discloses;
                // calls that would run user code.
                ["orToStringFormat"] = P(x => x.Id == 2 || capturedA.ToString("D2") == "07"),
                ["orDateFormat"] = P(x => x.Id == 2 || capturedDate.ToString("yyyy-MM-dd") == "2020-01-05"),
                ["orContainsNull"] = P(x => x.Id == 2 || capturedS.Contains(capturedNull)),
                ["orContainsNullOrdinal"] = P(x => x.Id == 2 || capturedS.Contains(capturedNull, StringComparison.Ordinal)),
                ["orNullContainsNull"] = P(x => x.Id == 2 || capturedNull.Contains(capturedNull)),
                ["orContainsWildcard"] = P(x => x.Id == 2 || capturedS.Contains("_")),
                ["orToStringLeadingZero"] = P(x => x.Id == 2 || capturedA.ToString() == "07"),
                ["orToStringProvider"] = P(x => x.Id == 2 || capturedDate.ToString(capturedProvider) == "x"),
                ["orUserContains"] = P(x => x.Id == 2 || capturedBag.Contains("admin")),
                ["orPairToString"] = P(x => x.Id == 2 || capturedPair.ToString() == "x"),
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
        [DataRow("selfEq", WherePredicateVerdict.SelfReference)]
        [DataRow("selfGe", WherePredicateVerdict.SelfReference)]
        [DataRow("selfLe", WherePredicateVerdict.SelfReference)]
        [DataRow("selfNe", WherePredicateVerdict.Acceptable)]
        [DataRow("selfGt", WherePredicateVerdict.Acceptable)]
        [DataRow("selfLt", WherePredicateVerdict.Acceptable)]
        [DataRow("notSelfNe", WherePredicateVerdict.SelfReference)]
        [DataRow("notSelfGt", WherePredicateVerdict.SelfReference)]
        [DataRow("longSelf", WherePredicateVerdict.SelfReference)]
        [DataRow("nullableSelf", WherePredicateVerdict.SelfReference)]
        [DataRow("nameSelf", WherePredicateVerdict.SelfReference)]
        [DataRow("idToStringSelf", WherePredicateVerdict.SelfReference)]
        [DataRow("nameToStringSelf", WherePredicateVerdict.SelfReference)]
        [DataRow("idToStringSelfNe", WherePredicateVerdict.Acceptable)]
        [DataRow("idToStringOther", WherePredicateVerdict.Acceptable)]
        [DataRow("idToStringFormatSelf", WherePredicateVerdict.Acceptable)]
        [DataRow("idOther", WherePredicateVerdict.Acceptable)]
        [DataRow("orTrue", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orCapturedTrue", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orStaticOn", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orNowGtMin", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orCapturedAEqA", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orCapturedNullIsNull", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orCapturedSEqS", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orCapturedFalse", WherePredicateVerdict.Acceptable)]
        [DataRow("nonShortOrTrue", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("andTrue", WherePredicateVerdict.Acceptable)]
        [DataRow("selfAndId2", WherePredicateVerdict.Acceptable)]
        [DataRow("selfOrId2", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("nonShortAndFalse", WherePredicateVerdict.Acceptable)]
        [DataRow("deMorganAnd", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("deMorganOr", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("deMorganCapturedFalse", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orIncludeAll", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orBoxedFlag", WherePredicateVerdict.Acceptable)]
        [DataRow("orArithmetic", WherePredicateVerdict.Acceptable)]
        [DataRow("orCapturedContains", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orCapturedContainsChar", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orContainsOrdinal", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orLiteralContains", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("rolesContainsOr", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orToString", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orIntToString", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orEnumToString", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orNullableToString", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orContainsFalse", WherePredicateVerdict.Acceptable)]
        [DataRow("nameContains", WherePredicateVerdict.Acceptable)]
        [DataRow("orContainsName", WherePredicateVerdict.Acceptable)]
        [DataRow("orStartsWith", WherePredicateVerdict.Acceptable)]
        [DataRow("orObjectToString", WherePredicateVerdict.Acceptable)]
        [DataRow("orStructToString", WherePredicateVerdict.Acceptable)]
        [DataRow("orNullableStructToString", WherePredicateVerdict.Acceptable)]
        [DataRow("orToStringFormat", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orDateFormat", WherePredicateVerdict.AlwaysTrue)]
        // D-003: s.Contains(null) throws in C#, so it's unknown here (the translation sends NULL LIKE NULL, which matches no row).
        [DataRow("orContainsNull", WherePredicateVerdict.Acceptable)]
        [DataRow("orContainsNullOrdinal", WherePredicateVerdict.Acceptable)]
        [DataRow("orNullContainsNull", WherePredicateVerdict.Acceptable)]
        [DataRow("orContainsWildcard", WherePredicateVerdict.Acceptable)]
        [DataRow("orToStringLeadingZero", WherePredicateVerdict.Acceptable)]
        [DataRow("orToStringProvider", WherePredicateVerdict.Acceptable)]
        [DataRow("orUserContains", WherePredicateVerdict.Acceptable)]
        [DataRow("orPairToString", WherePredicateVerdict.Acceptable)]
        [DataRow("handConvert", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("condTrueSelf", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("condFalseSelf", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("condSharedTrue", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("condUnknown", WherePredicateVerdict.Acceptable)]
        [DataRow("orNewHolder", WherePredicateVerdict.Acceptable)]
        [DataRow("orMethod", WherePredicateVerdict.Acceptable)]
        [DataRow("orWrapperConversion", WherePredicateVerdict.Acceptable)]
        [DataRow("orUserOperator", WherePredicateVerdict.Acceptable)]
        [DataRow("orThrows", WherePredicateVerdict.Acceptable)]
        [DataRow("foreignId2", WherePredicateVerdict.Acceptable)]
        [DataRow("foreignSelf", WherePredicateVerdict.SelfReference)]
        [DataRow("true", WherePredicateVerdict.NoColumn)]
        [DataRow("oneLtTwo", WherePredicateVerdict.NoColumn)]
        [DataRow("abcEqAbc", WherePredicateVerdict.NoColumn)]
        [DataRow("capturedTrue", WherePredicateVerdict.NoColumn)]
        [DataRow("false", WherePredicateVerdict.NoColumn)]
        // Review R-1 (A-1): an empty search value on a column is unbounded (every non-null row) unless something else bounds it.
        [DataRow("nameContainsEmpty", WherePredicateVerdict.UnboundedSearch)]
        [DataRow("nameStartsWithEmpty", WherePredicateVerdict.UnboundedSearch)]
        [DataRow("nameEndsWithEmpty", WherePredicateVerdict.UnboundedSearch)]
        [DataRow("nameContainsCapturedEmpty", WherePredicateVerdict.UnboundedSearch)]
        [DataRow("nameContainsStringEmpty", WherePredicateVerdict.UnboundedSearch)]
        [DataRow("idOrNameContainsEmpty", WherePredicateVerdict.UnboundedSearch)]
        [DataRow("idAndNameContainsEmpty", WherePredicateVerdict.Acceptable)]
        [DataRow("nameContainsNull", WherePredicateVerdict.Acceptable)]
        // Review R-1 (A-2, A-3): C#'s lifted >= is false for null, == is false for NaN, and a member of a member isn't a column.
        [DataRow("nSelfGe", WherePredicateVerdict.Acceptable)]
        [DataRow("nSelfEq", WherePredicateVerdict.SelfReference)]
        [DataRow("amountSelfEq", WherePredicateVerdict.Acceptable)]
        [DataRow("nameLengthSelf", WherePredicateVerdict.Acceptable)]
        public void Classify_ReturnsTheVerdict(string key, WherePredicateVerdict expected)
        {
            Assert.AreEqual(expected, WherePredicateGuard.Classify(Predicates()[key]), key);
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
            Bag.ContainsCalls = 0;
            CountingProvider.Calls = 0;
            var predicates = Predicates();
            foreach (var key in new[] { "orMethod", "orNewHolder", "orWrapperConversion", "orUserOperator", "orObjectToString",
                         "orStructToString", "orNullableStructToString", "orToStringProvider", "orUserContains",
                         "orPairToString" })
                WherePredicateGuard.Classify(predicates[key]);

            Assert.AreEqual(0, _methodCalls, "Method() was called");
            Assert.AreEqual(0, Holder.Constructed, "Holder's constructor was called");
            Assert.AreEqual(0, Wrapper.Conversions, "the user-defined explicit operator bool was called");
            Assert.AreEqual(0, W.Comparisons, "the user-defined operator == was called");
            Assert.AreEqual(0, Named.ToStringCalls, "a class's ToString() override was called");
            Assert.AreEqual(0, NamedValue.ToStringCalls, "a struct's ToString() override was called");
            Assert.AreEqual(0, Bag.ContainsCalls, "a user class's Contains was called");
            Assert.AreEqual(0, CountingProvider.Calls, "a user IFormatProvider was called");
        }

        [DataTestMethod]
        [DataRow("true", NoColumnMessage)]
        [DataRow("selfEq", SelfReferenceMessage)]
        [DataRow("orTrue", AlwaysTrueMessage)]
        [DataRow("nameContainsEmpty", UnboundedSearchMessage)]
        [DataRow("idEq2", "")]
        public void Validate_ThrowsTheMessageOfEachVerdict(string key, string expectedMessage)
        {
            var predicate = Predicates()[key];
            if (expectedMessage.Length == 0)
            {
                WherePredicateGuard.Validate(predicate);
                return;
            }
            var exception = Assert.ThrowsException<InvalidOperationException>(() => WherePredicateGuard.Validate(predicate), key);
            Assert.AreEqual(expectedMessage, exception.Message, key);
        }

        /// <summary>
        /// Of D9's rejections (3.10.0) only the empty search value survives, as the <see cref="WherePredicateVerdict.UnboundedSearch"/>
        /// verdict (review R-1, A-1): the translation now escapes a wildcard, sends <c>LIKE NULL</c> for a null value,
        /// reads a property of a captured object, evaluates a row-independent <c>Contains</c> or string comparison in
        /// C#, and the translators reject a <c>StartsWith</c>/<c>EndsWith</c>/<c>ToString()</c> they can't evaluate
        /// themselves, on every path. The guard accepts these.
        /// </summary>
        [DataTestMethod]
        [DataRow("nameContains")]
        [DataRow("nameContainsCaptured")]
        [DataRow("nameStartsWithA")]
        [DataRow("idToStringEq7")]
        [DataRow("idEq2")]
        [DataRow("capturedNullCheckOr")]
        [DataRow("capturedEqualsName")]
        [DataRow("nameContainsUnderscore")]
        [DataRow("nameStartsWithPercent")]
        [DataRow("nameEndsWithBracket")]
        [DataRow("nameContainsBackslash")]
        [DataRow("nameContainsCharUnderscore")]
        [DataRow("nameContainsCapturedWildcard")]
        [DataRow("nameContainsNull")]
        [DataRow("idAndNameContainsEmpty")]
        [DataRow("nameContainsThisProperty")]
        [DataRow("orContainsFalse")]
        [DataRow("orStartsWith")]
        [DataRow("nameEqualsCapturedToString")]
        [DataRow("orContainsNull")]
        [DataRow("orContainsNullOrdinal")]
        [DataRow("orNullContainsNull")]
        [DataRow("orContainsWildcard")]
        [DataRow("orCapturedStringEquals")]
        [DataRow("orCapturedStringNotEquals")]
        public void Validate_AcceptsTheSafeNeighbours(string key) => WherePredicateGuard.Validate(Predicates()[key]);

        /// <summary>
        /// D-128: the all-rows rejection is gated by one operation flag through the shared guard. For a
        /// <see cref="WhereOperation.Read"/> (Query, Where, Count, Any, All) <see cref="WherePredicateGuard.Validate(LambdaExpression, WhereOperation)"/>
        /// and <see cref="WherePredicateGuard.ValidateTranslation(string, WhereOperation)"/> accept every predicate and
        /// clause; for a <see cref="WhereOperation.MassMutation"/> (Delete, an update by predicate) they reject the
        /// all-rows shapes. The one-argument overloads are the mass-mutation form.
        /// </summary>
        [DataTestMethod]
        [DataRow("true", NoColumnMessage)]
        [DataRow("selfEq", SelfReferenceMessage)]
        [DataRow("orTrue", AlwaysTrueMessage)]
        [DataRow("rolesContainsOr", AlwaysTrueMessage)]
        [DataRow("orCapturedSEqS", AlwaysTrueMessage)]
        [DataRow("nameContainsEmpty", UnboundedSearchMessage)]
        public void Validate_RejectsAnAllRowsPredicate_OnlyForAMassMutation(string key, string expectedMessage)
        {
            var predicate = Predicates()[key];
            WherePredicateGuard.Validate(predicate, WhereOperation.Read);
            var mutation = Assert.ThrowsException<InvalidOperationException>(() => WherePredicateGuard.Validate(predicate, WhereOperation.MassMutation), key);
            Assert.AreEqual(expectedMessage, mutation.Message, key);
            var oneArgument = Assert.ThrowsException<InvalidOperationException>(() => WherePredicateGuard.Validate(predicate), key + " (one argument)");
            Assert.AreEqual(expectedMessage, oneArgument.Message, key + " (one argument)");
        }

        [DataTestMethod]
        [DataRow("", EmptyMessage)]
        [DataRow("1=1", AlwaysTrueMessage)]
        [DataRow("(t.id = @p__linq__0 OR NOT 1=0)", AlwaysTrueMessage)]
        [DataRow("t.id = t.id", SelfReferenceMessage)]
        public void ValidateTranslation_RejectsAnAllRowsClause_OnlyForAMassMutation(string whereClause, string expectedMessage)
        {
            WherePredicateGuard.ValidateTranslation(whereClause, WhereOperation.Read);
            var mutation = Assert.ThrowsException<InvalidOperationException>(() => WherePredicateGuard.ValidateTranslation(whereClause, WhereOperation.MassMutation), whereClause);
            Assert.AreEqual(expectedMessage, mutation.Message, whereClause);
        }

        /// <summary>The argument contract holds for a read too: a null predicate is a programming error on every path.</summary>
        [TestMethod]
        public void Validate_ForARead_StillRejectsBadArguments()
        {
            Assert.ThrowsException<ArgumentNullException>(() => WherePredicateGuard.Validate(null, WhereOperation.Read));
            Assert.ThrowsException<ArgumentNullException>(() => WherePredicateGuard.ValidateTranslation(null, WhereOperation.Read));
            Expression<Func<GuardRow, int>> intBody = x => x.Id;
            Assert.ThrowsException<ArgumentException>(() => WherePredicateGuard.Validate(intBody, WhereOperation.Read));
        }

        /// <summary>An evaluated part that makes the predicate always true is the verdict, with its message.</summary>
        [DataTestMethod]
        [DataRow("rolesContainsOr", AlwaysTrueMessage)]
        [DataRow("orCapturedContains", AlwaysTrueMessage)]
        [DataRow("orCapturedSEqS", AlwaysTrueMessage)]
        public void Validate_ThrowsAlwaysTrueForAnEvaluatedDisjunction(string key, string expectedMessage)
        {
            var exception = Assert.ThrowsException<InvalidOperationException>(() => WherePredicateGuard.Validate(Predicates()[key]), key);
            Assert.AreEqual(expectedMessage, exception.Message, key);
        }

        /// <summary>
        /// <see cref="WherePredicateGuard.Fold"/> (D-128): the C# truth of a part of a predicate that doesn't read the
        /// row, or of a self-comparison, which every path sends as a literal; null when the part reads the row or can't
        /// be evaluated without running code outside the core library.
        /// </summary>
        [DataTestMethod]
        [DataRow("true", true)]
        [DataRow("false", false)]
        [DataRow("oneLtTwo", true)]
        [DataRow("capturedTrue", true)]
        [DataRow("orTrue", true)]
        [DataRow("orCapturedSEqS", true)]
        [DataRow("orLiteralContains", true)]
        [DataRow("selfEq", true)]
        [DataRow("selfNe", false)]
        [DataRow("idToStringSelf", true)]
        [DataRow("idEq2", null)]
        [DataRow("orCapturedFalse", null)]
        [DataRow("orCapturedStringEquals", null)]
        [DataRow("orArithmetic", null)]
        [DataRow("orObjectToString", null)]
        [DataRow("orContainsNull", null)]
        [DataRow("condUnknown", null)]
        // Review R-1: the fold that decides the SQL never makes these true — C#'s `n >= n` is false for a null n, NaN
        // isn't equal to itself, a member of a member is the translator's to accept or reject, and an empty search value
        // is a column read (LIKE '%%' excludes a NULL column, as C# would throw on it).
        [DataRow("nSelfGe", null)]
        [DataRow("amountSelfEq", null)]
        [DataRow("nameLengthSelf", null)]
        [DataRow("nameContainsEmpty", null)]
        [DataRow("nSelfEq", true)]
        public void Fold_GivesCSharpsTruthOrNull(string key, bool? expected)
        {
            var predicate = Predicates()[key];
            Assert.AreEqual(expected, WherePredicateGuard.Fold(predicate.Body, typeof(GuardRow)), key);
        }

        /// <summary>The right side of an OR whose left reads the row: the part itself folds even when the whole doesn't.</summary>
        [TestMethod]
        public void Fold_EvaluatesTheRowIndependentOperandOfADisjunction()
        {
            var orFalse = (BinaryExpression)Predicates()["orCapturedFalse"].Body;
            var orStringEquals = (BinaryExpression)Predicates()["orCapturedStringEquals"].Body;
            Assert.IsNull(WherePredicateGuard.Fold(orFalse.Left, typeof(GuardRow)), "x.Id == 2 reads the row");
            Assert.AreEqual(false, WherePredicateGuard.Fold(orFalse.Right, typeof(GuardRow)), "capturedFalse");
            Assert.AreEqual(false, WherePredicateGuard.Fold(orStringEquals.Right, typeof(GuardRow)), "\"A\" == \"a\" is false in C# (ordinal)");
            Assert.ThrowsException<ArgumentNullException>(() => WherePredicateGuard.Fold(null, typeof(GuardRow)));
            Assert.ThrowsException<ArgumentNullException>(() => WherePredicateGuard.Fold(orFalse, null));
        }

        /// <summary>
        /// Review R-1 (A-4, B-4): a memo makes the folds of a predicate's truth positions one pass over the tree, so a
        /// long disjunction translates in linear time; and a <see cref="WhereOperation.Read"/> doesn't classify at all.
        /// 1500 terms, each reading the row, through the guard and a visitor: the cubic-cost version took far longer
        /// than the bound (measured in the red run, D-141).
        /// </summary>
        [TestMethod]
        public void Fold_WithAMemo_TranslatesALongDisjunctionInLinearTime()
        {
            var x = Expression.Parameter(typeof(GuardRow), "x");
            Expression body = Expression.Equal(Expression.Property(x, nameof(GuardRow.Id)), Expression.Constant(0));
            for (var i = 1; i < 1500; i++)
                body = Expression.OrElse(body, Expression.Equal(Expression.Property(x, nameof(GuardRow.Id)), Expression.Constant(i)));
            var predicate = Expression.Lambda<Func<GuardRow, bool>>(body, x);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            WherePredicateGuard.Validate(predicate, WhereOperation.Read);
            var memo = new Dictionary<Expression, bool?>();
            Assert.IsNull(WherePredicateGuard.Fold(body, typeof(GuardRow), memo), "the whole reads the row");
            Assert.IsTrue(memo.ContainsKey(((BinaryExpression)body).Left), "the memo doesn't record the operands");
            var generator = new Funcular.Data.Orm.Visitors.ParameterGenerator();
            var visitor = new Funcular.Data.Orm.Visitors.WhereClauseVisitor<GuardRow>(
                new System.Collections.Concurrent.ConcurrentDictionary<string, string>(), new List<System.Reflection.PropertyInfo>(),
                generator, new Funcular.Data.Orm.Visitors.SqlExpressionTranslator(generator), "t");
            visitor.Visit(predicate);
            watch.Stop();

            Assert.AreEqual(1500, visitor.Parameters.Count, "every term became a parameter");
            Assert.IsTrue(watch.ElapsedMilliseconds < 5000, "1500 terms took " + watch.ElapsedMilliseconds + " ms");
        }

        /// <summary>
        /// The check on the translated SQL (D3), in its mass-mutation form: an empty clause, a literal tautology
        /// (<c>1=1</c>, or a fold such as <c>t.id = @p OR NOT 1=0</c>) and a whole-clause self-comparison are rejected
        /// with <see cref="InvalidOperationException"/>; anything else passes. (A read accepts every clause:
        /// <see cref="ValidateTranslation_RejectsAnAllRowsClause_OnlyForAMassMutation"/>.)
        /// </summary>
        [DataTestMethod]
        [DataRow("", EmptyMessage)]
        [DataRow("   ", EmptyMessage)]
        [DataRow("1=1", AlwaysTrueMessage)]
        [DataRow("(t.id = @p__linq__0 OR NOT 1=0)", AlwaysTrueMessage)]
        [DataRow("t.id = t.id", SelfReferenceMessage)]
        [DataRow("[person].[id] >= [person].[id]", SelfReferenceMessage)]
        [DataRow("CAST(t.id AS TEXT) = CAST(t.id AS TEXT)", SelfReferenceMessage)]
        [DataRow("t.id = @p__linq__0", "")]
        [DataRow("(t.id = @p__linq__0 AND NOT 1=0)", "")]
        [DataRow("t.id = t.other", "")]
        [DataRow("t.a = t.a AND t.id = @p", "")]
        public void ValidateTranslation_RejectsAnEmptyTautologicalOrSelfComparingClause(string whereClause, string expectedMessage)
        {
            if (expectedMessage.Length == 0)
            {
                WherePredicateGuard.ValidateTranslation(whereClause);
                return;
            }
            var exception = Assert.ThrowsException<InvalidOperationException>(() => WherePredicateGuard.ValidateTranslation(whereClause), whereClause);
            Assert.AreEqual(expectedMessage, exception.Message, whereClause);
        }

        /// <summary>The 3.10.0 API forwards to the shared guard: same verdicts (by name), same exceptions, same SQL check.</summary>
        [TestMethod]
        public void DeletePredicateGuard_ForwardsToTheSharedGuard()
        {
#pragma warning disable CS0618 // The obsolete API is the subject.
            var predicates = Predicates();
            foreach (var key in new[] { "idEq2", "true", "selfEq", "orTrue" })
                Assert.AreEqual(WherePredicateGuard.Classify(predicates[key]).ToString(),
                    DeletePredicateGuard.Classify(predicates[key]).ToString(), key);
            // The 3.10.0 enum has no UnboundedSearch: the forwarder reports it as AlwaysTrue, the nearest 3.10.0 verdict.
            Assert.AreEqual(DeletePredicateVerdict.AlwaysTrue, DeletePredicateGuard.Classify(predicates["nameContainsEmpty"]));
            var selfReference = Assert.ThrowsException<InvalidOperationException>(() => DeletePredicateGuard.Validate(predicates["selfEq"]));
            Assert.AreEqual(SelfReferenceMessage, selfReference.Message);
            DeletePredicateGuard.Validate(predicates["orCapturedStringEquals"]); // D9's string-comparison rejection is gone (D-128)
            DeletePredicateGuard.Validate(predicates["idEq2"]);
            Assert.IsTrue(DeletePredicateGuard.HasLiteralTautology("t.id = @p OR NOT 1=0"));
            Assert.IsFalse(DeletePredicateGuard.HasLiteralTautology("t.id = @p"));
            Assert.ThrowsException<ArgumentNullException>(() => DeletePredicateGuard.Classify(null));
#pragma warning restore CS0618
        }

        [DataTestMethod]
        [DataRow("classifyNull")]
        [DataRow("validateNull")]
        [DataRow("hasLiteralTautologyNull")]
        [DataRow("validateTranslationNull")]
        [DataRow("zeroParameters")]
        [DataRow("twoParameters")]
        [DataRow("intBody")]
        public void PublicMembers_RejectBadArguments(string kind)
        {
            switch (kind)
            {
                case "classifyNull":
                    Assert.ThrowsException<ArgumentNullException>(() => WherePredicateGuard.Classify(null));
                    break;
                case "validateNull":
                    Assert.ThrowsException<ArgumentNullException>(() => WherePredicateGuard.Validate(null));
                    break;
                case "hasLiteralTautologyNull":
                    Assert.ThrowsException<ArgumentNullException>(() => WherePredicateGuard.HasLiteralTautology(null));
                    break;
                case "validateTranslationNull":
                    Assert.ThrowsException<ArgumentNullException>(() => WherePredicateGuard.ValidateTranslation(null));
                    break;
                case "zeroParameters":
                    Expression<Func<bool>> zero = () => true;
                    Assert.ThrowsException<ArgumentException>(() => WherePredicateGuard.Classify(zero));
                    break;
                case "twoParameters":
                    Expression<Func<GuardRow, GuardRow, bool>> two = (a, b) => a.Id == b.Id;
                    Assert.ThrowsException<ArgumentException>(() => WherePredicateGuard.Classify(two));
                    break;
                case "intBody":
                    Expression<Func<GuardRow, int>> intBody = x => x.Id;
                    Assert.ThrowsException<ArgumentException>(() => WherePredicateGuard.Classify(intBody));
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
            Assert.AreEqual(expected, WherePredicateGuard.HasLiteralTautology(whereClause), whereClause);
        }

        /// <summary>
        /// A clause nested more deeply than the calling thread's stack allows is still folded (parsed again on a large
        /// stack), not a stack overflow that ends the process. It runs on a 1 MB thread, the size of a default thread
        /// on Windows; the providers' visitors stop near 1,600 levels on such a thread.
        /// </summary>
        [DataTestMethod]
        [DataRow(20000, "OR", true)]
        [DataRow(20000, "AND", false)]
        public void HasLiteralTautology_DeeplyNested_StillFolds(int depth, string connective, bool expected)
        {
            var clause = new string('(', depth) + "t.id = @p " + connective + " NOT 1=0" + new string(')', depth);
            Assert.AreEqual(expected, OnSmallThread(clause));
        }

        /// <summary>
        /// A clause nested too deeply even for the large stack is one the parser can't parse: false, not a stack
        /// overflow. The large stack here is 1 MB, not 64 MB: how many levels fit on 64 MB depends on how far the JIT
        /// has optimised the parser (in a Release build, about 120,000 cold and 320,000 to 350,000 once earlier tests
        /// have warmed it), but even an optimised parser makes a recursive call per level, at least 16 bytes of stack
        /// in a 64-bit process, so 70,000 levels can't fit in 1 MB. They do fit on 64 MB (about 93,000 do in a Debug
        /// build, the fewest), so this test also fails if the parsing thread gets 64 MB instead of the size passed; a
        /// thread that gets the default size is caught by <see cref="HasLiteralTautology_DeeplyNested_StillFolds"/>.
        /// </summary>
        [TestMethod]
        public void HasLiteralTautology_NestedBeyondTheLargeStack_ReturnsFalse()
        {
            const int depth = 70000;
            var clause = new string('(', depth) + "t.id = @p OR NOT 1=0" + new string(')', depth);
            Assert.AreEqual(false, OnSmallThread(clause, largeStackSize: 1024 * 1024));
        }

        private static bool? OnSmallThread(string clause, int? largeStackSize = null)
        {
            bool? result = null;
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    result = largeStackSize == null
                        ? WherePredicateGuard.HasLiteralTautology(clause)
                        : WherePredicateGuard.HasLiteralTautology(clause, largeStackSize.Value);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            }, 1024 * 1024);
            thread.Start();
            thread.Join();
            Assert.IsNull(error, error?.ToString());
            return result;
        }
    }
}
