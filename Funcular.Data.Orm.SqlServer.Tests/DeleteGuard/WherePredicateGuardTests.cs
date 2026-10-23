using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using System.Threading;

#pragma warning disable CS1718 // Comparisons of a member with itself are the point of these rows.

namespace Funcular.Data.Orm.SqlServer.Tests.DeleteGuard
{
    /// <summary>
    /// The DB-free rows of the delete-guard plan (docs/plans/DELETE_GUARD_PLAN.md, §4.1), on the guard of
    /// docs/plans/DECISIONS-3.10.2.md D-003 as amended by D-128: <see cref="WherePredicateGuard"/>'s classification of
    /// predicates (D2), its argument contract (D1), its SQL check (D3), its <c>Fold</c> (the C# truth of a part that
    /// doesn't read the row, which every path sends as a literal); and the obsolete <see cref="DeletePredicateGuard"/>,
    /// which forwards to it. Of D9's rejections (3.10.0) only the empty search value stays, as
    /// <see cref="WherePredicateVerdict.UnboundedSearch"/>; what the rest guarded is evaluated in C# on every path.
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
            public DateTime Created { get; set; }
            public Inner Child { get; set; }
            public string Tag; // a field, which no translator accepts as a column (review R-5)
            // R17-2: aliases (a template that is one token), two templates with the same text, and one that isn't an alias.
            [Funcular.Data.Orm.Attributes.SqlExpression("{N}")] public int? NX { get; set; }
            [Funcular.Data.Orm.Attributes.SqlExpression("CASE WHEN {N} IS NULL THEN 0 ELSE {N} END")] public int? NBoth { get; set; }
            [Funcular.Data.Orm.Attributes.SqlExpression("CASE WHEN {N} IS NULL THEN 0 ELSE {N} END")] public int? NBoth2 { get; set; }
            [Funcular.Data.Orm.Attributes.SqlExpression("{NBoth}")] public int? NBothX { get; set; }
            [Funcular.Data.Orm.Attributes.SqlExpression("{N} + 0")] public int? NPlus0 { get; set; }
            // R18-4: an alias of an alias; provider templates whose variants name different tokens or differ in one variant.
            [Funcular.Data.Orm.Attributes.SqlExpression("{NX}")] public int? NXX { get; set; }
            [Funcular.Data.Orm.Attributes.SqlExpression("{Name}")] public string NameX { get; set; }
            [Funcular.Data.Orm.Attributes.SqlExpression("{N}", "{Other}")] public int? NDualOther { get; set; }
            [Funcular.Data.Orm.Attributes.SqlExpression("COALESCE({N}, 0)", "COALESCE({N}, 1)")] public int? NDualA { get; set; }
            [Funcular.Data.Orm.Attributes.SqlExpression("COALESCE({N}, 0)", "COALESCE({N}, 1)")] public int? NDualA2 { get; set; }
            [Funcular.Data.Orm.Attributes.SqlExpression("COALESCE({N}, 0)", "COALESCE({N}, 2)")] public int? NDualB { get; set; }
            [Funcular.Data.Orm.Attributes.SqlExpression("COALESCE({N}, 3)", "COALESCE({N}, 1)")] public int? NDualC { get; set; }
            // R18-1: a template that is a provider's own SQL for a column's part, which only the provider's text shows.
            [Funcular.Data.Orm.Attributes.SqlExpression("LENGTH({Name})")] public int NameLenInt { get; set; }
        }

        public class Inner
        {
            public string Name { get; set; }
        }

        /// <summary>R20-2: a column declared on a base class, and the derived entity's alias of it.</summary>
        public class BaseRow
        {
            public int? N { get; set; }
            public int Other { get; set; }
        }

        public class DerivedRow : BaseRow
        {
            [Funcular.Data.Orm.Attributes.SqlExpression("{N}")] public int? NX { get; set; }
            public int Id { get; set; }
        }

        /// <summary>R21-4: virtual columns, which the entity below overrides.</summary>
        public class VirtualBaseRow
        {
            public virtual int? N { get; set; }
            public virtual int? M { get; set; }
            public int Other { get; set; }
        }

        /// <summary>
        /// R21-4: the compiler writes <c>x.N</c> with <see cref="VirtualBaseRow"/>'s declaration, and the alias's target is
        /// found on this type, the override.
        /// </summary>
        public class OverridingRow : VirtualBaseRow
        {
            public override int? N { get; set; }
            public override int? M { get; set; }
            [Funcular.Data.Orm.Attributes.SqlExpression("{N}")] public int? NX { get; set; }
            public int Id { get; set; }
        }

        /// <summary>
        /// R22-1: an override of the setter alone, so the property found on this type (the alias's target) has no getter,
        /// while the compiler writes <c>x.N</c> with <see cref="VirtualBaseRow"/>'s declaration.
        /// </summary>
        public class SetterOverridingRow : VirtualBaseRow
        {
            public override int? N { set => base.N = value; }
            [Funcular.Data.Orm.Attributes.SqlExpression("{N}")] public int? NX { get; set; }
            public int Id { get; set; }
        }

        /// <summary>R22-3: another type's static field named <c>Empty</c>, which holds a value.</summary>
        public static class OtherStrings
        {
            public static readonly string Empty = "x";
        }

        private const string ConstEmpty = "";

        /// <summary>A user-defined conversion that changes the value (R13-4's V13-8): a cast with an operator method isn't seen through.</summary>
        public readonly struct Parity
        {
            private readonly int _bit;

            private Parity(int bit) => _bit = bit;

            public static explicit operator Parity(int value) => new Parity(value % 2);

            public static explicit operator int(Parity parity) => parity._bit;
        }

        /// <summary>Counts the reads of its property, to show whether a path evaluated the captured part at all.</summary>
        public class CountingHolder
        {
            public static int Reads;
            public bool Flag
            {
                get
                {
                    Reads++;
                    return true;
                }
            }

            public string Empty
            {
                get
                {
                    Reads++;
                    return "";
                }
            }
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

        public static int Five() => 5;

        // Captured values: each is read through a closure, as a caller's local would be.
        private readonly bool capturedTrue = true;
        private readonly bool capturedFalse = false;
        private readonly int capturedA = 7;
        private readonly string capturedNull = null;
        private readonly string capturedS = "s";
        private readonly string capturedWildcard = "a%";
        private readonly string capturedUpperA = "A";
        private readonly string capturedEmpty = "";
        private readonly int?[] capturedIds = { 1, 2 };
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
        private readonly object boxedTrue = true;
        private readonly CountingHolder counting = new CountingHolder();

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
                // A predicate compared with itself (R4-4): a call and a conditional are compared whole (R6-8 d).
                ["containsSelfEq"] = P(x => x.Name.Contains(capturedS) == x.Name.Contains(capturedS)),
                ["containsOtherEq"] = P(x => x.Name.Contains(capturedS) == x.Name.Contains(capturedUpperA)),
                ["condPredicateSelfEq"] = P(x => (x.Id > 2 ? x.Id == 5 : x.Other == 1) == (x.Id > 2 ? x.Id == 5 : x.Other == 1)),
                ["condPredicateOtherEq"] = P(x => (x.Id > 2 ? x.Id == 5 : x.Other == 1) == (x.Id > 2 ? x.Id == 5 : x.Other == 2)),
                ["staticContainsSelfEq"] = P(x => capturedIds.Contains(x.N) == capturedIds.Contains(x.N)),
                ["containsStartsWithEq"] = P(x => x.Name.Contains(capturedS) == x.Name.StartsWith(capturedS)),
                // A type test isn't one of the nodes SameValue compares: not the same.
                ["typeIsSelfEq"] = P(x => (x.Name is string) == (x.Name is string)),
                // R7-5: a bool compared with the same operand, a predicate or not: `!` of a column, a conditional.
                ["notFlagSelfEq"] = P(x => !x.Flag == !x.Flag),
                ["notFlagSelfOrId2"] = P(x => (!x.Flag == !x.Flag) || x.Id == 2),
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
                // Review R-2 (A/N-1): a bool unboxed from a captured object is evaluated as a whole, as 6daabcd did.
                ["orBoxedTrue"] = P(x => (bool)boxedTrue || x.Id == 1),
                ["orNotBoxedTrue"] = P(x => x.Id == 2 || !(bool)boxedTrue),
                // Review R-2 (A/N-2): the verdict keeps 3.10's broad self-comparison (any member chain, any type, any
                // operator); only the fold that decides the SQL is narrowed to where C# makes the answer the same for every row.
                ["idOrNameLengthSelf"] = P(x => x.Id == -1 || x.Name.Length == x.Name.Length),
                ["idOrYearSelf"] = P(x => x.Id == -1 || x.Created.Year == x.Created.Year),
                ["notAmountNeSelf"] = P(x => !(x.Amount != x.Amount)),
                ["idOrNullableGeSelf"] = P(x => x.Id == -1 || x.N >= x.N),
                // Review R-2 (A/N-4): C#'s lifted < is false for every row, null included, so it folds (and !(n < n) holds).
                ["nSelfLt"] = P(x => x.N < x.N),
                ["notNSelfLt"] = P(x => !(x.N < x.N)),
                // Review R-4 (round-3 B/F-2): the other two nullable operators, so a rule that treats >= and <= (or < and >)
                // differently can't pass.
                ["nSelfLe"] = P(x => x.N <= x.N),
                ["nSelfGt"] = P(x => x.N > x.N),
                // Review R-2 (A/N-3): an empty search on a member of a member is the translator's to reject, on every path.
                ["childNameContainsEmpty"] = P(x => x.Child.Name.Contains("")),
                // Review R-4 (round-3 A/N-1): a bool compared with a bool composes its operands' truths, so an empty
                // search or a self-comparison in a value position doesn't escape the verdict (3.10.1 refused the first).
                ["nameContainsEmptyEqTrue"] = P(x => x.Name.Contains("") == true),
                ["nameContainsEmptyNeFalse"] = P(x => x.Name.Contains("") != false),
                ["nameContainsEmptyEqFalse"] = P(x => x.Name.Contains("") == false),
                ["idSelfEqTrue"] = P(x => (x.Id == x.Id) == true),
                ["idSelfNeTrue"] = P(x => (x.Id == x.Id) != true),
                ["flagEqTrue"] = P(x => x.Flag == true),
                ["orCapturedTrueEqFalse"] = P(x => x.Id == 2 || capturedTrue == false),
                ["orCapturedTrueNeFalse"] = P(x => x.Id == 2 || capturedTrue != false),
                // Review R-4 (round-3 A/N-2): the receiver of an unbounded search is a property of the entity, read
                // directly; a ToString() on it is the translator's to reject, on every path.
                ["nameToStringContainsEmpty"] = P(x => x.Name.ToString().Contains("")),
                // Review R-5 (round-4 A/1, A/2, B/1): a cast around the property, or a field: neither is what the
                // translators accept as a receiver, so neither is the guard's to call unbounded.
                ["castNameContainsEmpty"] = P(x => ((string)(object)x.Name).Contains("")),
                ["fieldContainsEmpty"] = P(x => x.Tag.Contains("")),
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
                // R12-1: a bool compared with a copy of itself that differs only by casts or a string's ToString(): the same
                // value for every row, as the scalar rule sees through a cast (3.11.0's text check refused these).
                ["boolSelfUnderNullableCast"] = P(x => (x.N == null) == ((long?)x.N == null)),
                ["boolSelfUnderWideningCasts"] = P(x => (x.Id > x.Other) == ((long)x.Id > (long)x.Other)),
                ["boolSelfUnderEqualityCasts"] = P(x => (x.Id == x.Other) == ((long)x.Id == (long)x.Other)),
                ["boolSelfUnderStringToString"] = P(x => (x.Name == null) == (x.Name.ToString() == null)),
                ["boolSelfConstantWidened"] = P(x => (x.Id > 5) == ((long)x.Id > 5L)),
                ["boolOtherUnderCast"] = P(x => (x.N == null) == ((long?)x.Other == null)),
                ["boolOtherConstantUnderCast"] = P(x => (x.Id > 5) == ((long)x.Id > 6L)),
                // R13-4 (V13-8): a user-defined cast may change the value, so it isn't stripped: Id 1 against Other 3 differs.
                ["boolOtherUnderUserCast"] = P(x => (x.Id == x.Other) == ((int)(Parity)x.Id == (int)(Parity)x.Other)),
                // R17-2: an operand of any type compared with a copy of itself, literal or through an alias (a template that
                // is one token): distribution would hide it from the text check, so the verdict decides it. Alone it is a
                // self-reference; inside an || the predicate is always true.
                ["condSelfCopy"] = P(x => (x.Id > 2 ? x.N : (int?)x.Other) == (x.Id > 2 ? x.N : (int?)x.Other)),
                ["condAliasCopy"] = P(x => (x.Id > 2 ? x.N : (int?)x.Other) == (x.Id > 2 ? x.NX : (int?)x.Other)),
                ["nestedCondAliasCopy"] = P(x => ((x.Id > 2 ? x.N : (int?)x.Other) != 5 ? x.N : (int?)x.Other)
                                                 == ((x.Id > 2 ? x.NX : (int?)x.Other) != 5 ? x.NX : (int?)x.Other)),
                ["notCondAliasCopyNe"] = P(x => !((x.Id > 2 ? x.N : (int?)x.Other) != (x.Id > 2 ? x.NX : (int?)x.Other))),
                ["condAliasCopyOrId2"] = P(x => (x.Id > 2 ? x.N : (int?)x.Other) == (x.Id > 2 ? x.NX : (int?)x.Other) || x.Id == 2),
                ["arithmeticSelf"] = P(x => x.Id + 1 == x.Id + 1),
                ["nAliasSelf"] = P(x => x.N == x.NX),
                ["boolAliasSelf"] = P(x => (x.NX == null) == (x.N == null)),
                ["templateTwinSelf"] = P(x => x.NBoth == x.NBoth2),
                ["templateAliasSelf"] = P(x => x.NBoth == x.NBothX),
                // Through a cast through object (the self-comparison fuzzer, fuzz18s): 3.11.0's text check refused these.
                ["objBoolSelf"] = P(x => (bool)(object)((x.N == null) == (x.N == null))),
                ["objAliasSelf"] = P(x => (bool)(object)(x.N == x.NX)),
                // R20-1: a cast through object that reads no row is evaluated whole, as 3.11.0 evaluated it; one that reads the
                // row is folded inside without evaluating anything (3.11.0's verdict read nothing there), so a captured value
                // under it doesn't decide the verdict, and a self-comparison under it still does.
                ["orObjCapturedTrue"] = P(x => x.Id == 2 || (bool)(object)capturedTrue),
                ["objOrCapturedTrue"] = P(x => (bool)(object)(x.Id == 2 || capturedTrue)),
                ["objOrCapturedTrueEqTrue"] = P(x => (bool)(object)(x.Id == 2 || capturedTrue == true)),
                ["objSelfOrCapturedTrue"] = P(x => (bool)(object)(x.Id == x.Id || capturedTrue)),
                // R21-1: an empty search value under such a cast counts where it is written as `""` or string.Empty, which
                // reads nothing the caller wrote; a captured one isn't read there (3.11.0's verdict read none): a Known issue.
                ["objNameContainsEmpty"] = P(x => (bool)(object)x.Name.Contains("")),
                ["idOrObjNameStartsWithEmpty"] = P(x => x.Id == 2 || (bool)(object)x.Name.StartsWith("")),
                ["objNameEndsWithStringEmptyOrId"] = P(x => (bool)(object)(x.Name.EndsWith(string.Empty) || x.Id == 2)),
                ["objNameContainsCapturedEmpty"] = P(x => (bool)(object)x.Name.Contains(capturedEmpty)),
                // R22-3: a literal that isn't empty, and another type's static Empty that holds a value, aren't the empty
                // value; R22-4: a const holding "" is written in place, a literal.
                ["objNameContainsA"] = P(x => (bool)(object)x.Name.Contains("a")),
                ["objNameContainsOtherEmpty"] = P(x => (bool)(object)x.Name.Contains(OtherStrings.Empty)),
                ["objNameContainsConstEmpty"] = P(x => (bool)(object)x.Name.Contains(ConstEmpty)),
                // R21-5: a cast through object under another one evaluates nothing either, whether it reads the row or not.
                ["objObjOrCapturedTrue"] = P(x => (bool)(object)((bool)(object)(x.Id == 2 || capturedTrue))),
                ["objOrObjCapturedTrue"] = P(x => (bool)(object)(x.Id == 2 || (bool)(object)capturedTrue)),
                // >= and <= of an operand with itself, whose C# answer skips a null: counted, as 3.10's rule counts a
                // column's x.N >= x.N (3.11.0's text check refused such a copy where its text was the same).
                ["condSelfGeNullable"] = P(x => (x.Id > 2 ? x.N : (int?)x.Other) >= (x.Id > 2 ? x.N : (int?)x.Other)),
                ["condSelfGeNotNull"] = P(x => (x.Id > 2 ? x.Id : x.Other) >= (x.Id > 2 ? x.Id : x.Other)),
                ["notCondSelfLtNullable"] = P(x => !((x.Id > 2 ? x.N : (int?)x.Other) < (x.Id > 2 ? x.NX : (int?)x.Other))),
                // R18-4: an alias of an alias is the column; a template whose variants name different tokens is no alias,
                // and two templates are the same only where every variant is.
                ["aliasOfAliasSelf"] = P(x => x.N == x.NXX),
                // R18-2 (d), R18-5: the Changelog's examples of a column against its alias's under a `!`, and compared with a
                // bool value (always true, the self-comparison sitting under another operator).
                ["notNAliasNe"] = P(x => !(x.N != x.NX)),
                ["nAliasEqTrue"] = P(x => (x.N == x.NX) == true),
                ["nameAliasNeNeTrue"] = P(x => (x.Name != x.NameX) != true),
                ["dualTemplatesSame"] = P(x => x.NDualA == x.NDualA2),
                ["nDualOtherNotAlias"] = P(x => x.N == x.NDualOther),
                ["otherDualOtherNotAlias"] = P(x => (int?)x.Other == x.NDualOther),
                ["dualTemplatesDifferPostgreSql"] = P(x => x.NDualA == x.NDualB),
                ["dualTemplatesDifferMsSql"] = P(x => x.NDualA == x.NDualC),
                // Neighbours: a template that isn't one token, another branch, another test.
                ["nNotAnAlias"] = P(x => x.N == x.NPlus0),
                ["condOtherBranch"] = P(x => (x.Id > 2 ? x.N : (int?)x.Other) == (x.Id > 2 ? x.N : (int?)x.Id)),
                ["condOtherTest"] = P(x => (x.Id > 2 ? x.N : (int?)x.Other) == (x.Id > 3 ? x.NX : (int?)x.Other)),
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
        [DataRow("containsSelfEq", WherePredicateVerdict.SelfReference)]
        [DataRow("containsOtherEq", WherePredicateVerdict.Acceptable)]
        [DataRow("condPredicateSelfEq", WherePredicateVerdict.SelfReference)]
        [DataRow("condPredicateOtherEq", WherePredicateVerdict.Acceptable)]
        [DataRow("staticContainsSelfEq", WherePredicateVerdict.SelfReference)]
        [DataRow("containsStartsWithEq", WherePredicateVerdict.Acceptable)]
        [DataRow("typeIsSelfEq", WherePredicateVerdict.Acceptable)]
        [DataRow("notFlagSelfEq", WherePredicateVerdict.SelfReference)]
        [DataRow("notFlagSelfOrId2", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("idToStringSelf", WherePredicateVerdict.SelfReference)]
        [DataRow("nameToStringSelf", WherePredicateVerdict.SelfReference)]
        [DataRow("boolSelfUnderNullableCast", WherePredicateVerdict.SelfReference)]
        [DataRow("boolSelfUnderWideningCasts", WherePredicateVerdict.SelfReference)]
        [DataRow("boolSelfUnderEqualityCasts", WherePredicateVerdict.SelfReference)]
        [DataRow("boolSelfUnderStringToString", WherePredicateVerdict.SelfReference)]
        [DataRow("boolSelfConstantWidened", WherePredicateVerdict.SelfReference)]
        [DataRow("boolOtherUnderCast", WherePredicateVerdict.Acceptable)]
        [DataRow("boolOtherConstantUnderCast", WherePredicateVerdict.Acceptable)]
        [DataRow("boolOtherUnderUserCast", WherePredicateVerdict.Acceptable)]
        [DataRow("condSelfCopy", WherePredicateVerdict.SelfReference)]
        [DataRow("condAliasCopy", WherePredicateVerdict.SelfReference)]
        [DataRow("nestedCondAliasCopy", WherePredicateVerdict.SelfReference)]
        [DataRow("notCondAliasCopyNe", WherePredicateVerdict.SelfReference)]
        [DataRow("condAliasCopyOrId2", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("arithmeticSelf", WherePredicateVerdict.SelfReference)]
        [DataRow("nAliasSelf", WherePredicateVerdict.SelfReference)]
        [DataRow("boolAliasSelf", WherePredicateVerdict.SelfReference)]
        [DataRow("templateTwinSelf", WherePredicateVerdict.SelfReference)]
        [DataRow("templateAliasSelf", WherePredicateVerdict.SelfReference)]
        [DataRow("objBoolSelf", WherePredicateVerdict.SelfReference)]
        [DataRow("objAliasSelf", WherePredicateVerdict.SelfReference)]
        [DataRow("orObjCapturedTrue", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("objOrCapturedTrue", WherePredicateVerdict.Acceptable)]
        [DataRow("objOrCapturedTrueEqTrue", WherePredicateVerdict.Acceptable)]
        [DataRow("objSelfOrCapturedTrue", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("objNameContainsEmpty", WherePredicateVerdict.UnboundedSearch)]
        [DataRow("idOrObjNameStartsWithEmpty", WherePredicateVerdict.UnboundedSearch)]
        [DataRow("objNameEndsWithStringEmptyOrId", WherePredicateVerdict.UnboundedSearch)]
        [DataRow("objNameContainsCapturedEmpty", WherePredicateVerdict.Acceptable)]
        [DataRow("objNameContainsA", WherePredicateVerdict.Acceptable)]
        [DataRow("objNameContainsOtherEmpty", WherePredicateVerdict.Acceptable)]
        [DataRow("objNameContainsConstEmpty", WherePredicateVerdict.UnboundedSearch)]
        [DataRow("objObjOrCapturedTrue", WherePredicateVerdict.Acceptable)]
        [DataRow("objOrObjCapturedTrue", WherePredicateVerdict.Acceptable)]
        [DataRow("condSelfGeNullable", WherePredicateVerdict.SelfReference)]
        [DataRow("condSelfGeNotNull", WherePredicateVerdict.SelfReference)]
        [DataRow("notCondSelfLtNullable", WherePredicateVerdict.SelfReference)]
        [DataRow("aliasOfAliasSelf", WherePredicateVerdict.SelfReference)]
        [DataRow("notNAliasNe", WherePredicateVerdict.SelfReference)]
        [DataRow("nAliasEqTrue", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("nameAliasNeNeTrue", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("dualTemplatesSame", WherePredicateVerdict.SelfReference)]
        [DataRow("nDualOtherNotAlias", WherePredicateVerdict.Acceptable)]
        [DataRow("otherDualOtherNotAlias", WherePredicateVerdict.Acceptable)]
        [DataRow("dualTemplatesDifferPostgreSql", WherePredicateVerdict.Acceptable)]
        [DataRow("dualTemplatesDifferMsSql", WherePredicateVerdict.Acceptable)]
        [DataRow("nNotAnAlias", WherePredicateVerdict.Acceptable)]
        [DataRow("condOtherBranch", WherePredicateVerdict.Acceptable)]
        [DataRow("condOtherTest", WherePredicateVerdict.Acceptable)]
        [DataRow("idToStringSelfNe", WherePredicateVerdict.Acceptable)]
        [DataRow("idToStringOther", WherePredicateVerdict.Acceptable)]
        // Since rev 18 (R17-2) the verdict counts any operand compared with itself, a call with the same arguments included
        // (not evaluated); a delete of it still throws the translator's NotSupportedException for the format, first.
        [DataRow("idToStringFormatSelf", WherePredicateVerdict.SelfReference)]
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
        // Review R-2 (A/N-2): the verdict is 3.10's — a column, or a member of one, compared with itself by ==, >= or <=
        // (or a negated !=, < or >) is refused as a mass mutation (it touches every row, or every non-null row), whatever
        // the type and whatever the SQL fold does with it; the false shapes (!=, <, >) delete nothing and are accepted.
        [DataRow("nSelfGe", WherePredicateVerdict.SelfReference)]
        [DataRow("nSelfEq", WherePredicateVerdict.SelfReference)]
        [DataRow("amountSelfEq", WherePredicateVerdict.SelfReference)]
        [DataRow("nameLengthSelf", WherePredicateVerdict.SelfReference)]
        [DataRow("idOrNameLengthSelf", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("idOrYearSelf", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("notAmountNeSelf", WherePredicateVerdict.SelfReference)]
        [DataRow("idOrNullableGeSelf", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("nSelfLt", WherePredicateVerdict.Acceptable)]
        [DataRow("notNSelfLt", WherePredicateVerdict.SelfReference)]
        [DataRow("nSelfLe", WherePredicateVerdict.SelfReference)]
        [DataRow("nSelfGt", WherePredicateVerdict.Acceptable)]
        // Review R-2 (A/N-1): a cast from a captured object is evaluated as a whole.
        [DataRow("orBoxedTrue", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("orNotBoxedTrue", WherePredicateVerdict.Acceptable)]
        // Review R-2 (A/N-3): an empty search on a member of a member isn't the guard's; the translators reject it on every path.
        [DataRow("childNameContainsEmpty", WherePredicateVerdict.Acceptable)]
        // Review R-4 (round-3 A/N-1): a bool compared with a bool composes the operands' truths.
        [DataRow("nameContainsEmptyEqTrue", WherePredicateVerdict.UnboundedSearch)]
        [DataRow("nameContainsEmptyNeFalse", WherePredicateVerdict.UnboundedSearch)]
        [DataRow("nameContainsEmptyEqFalse", WherePredicateVerdict.Acceptable)]
        [DataRow("idSelfEqTrue", WherePredicateVerdict.AlwaysTrue)]
        [DataRow("idSelfNeTrue", WherePredicateVerdict.Acceptable)]
        [DataRow("flagEqTrue", WherePredicateVerdict.Acceptable)]
        [DataRow("orCapturedTrueEqFalse", WherePredicateVerdict.Acceptable)]
        [DataRow("orCapturedTrueNeFalse", WherePredicateVerdict.AlwaysTrue)]
        // Review R-4 (round-3 A/N-2): a ToString() receiver isn't a property of the entity; the translators reject it on every path.
        [DataRow("nameToStringContainsEmpty", WherePredicateVerdict.Acceptable)]
        // Review R-5 (round-4 A/1, A/2, B/1): a cast or field receiver, likewise.
        [DataRow("castNameContainsEmpty", WherePredicateVerdict.Acceptable)]
        [DataRow("fieldContainsEmpty", WherePredicateVerdict.Acceptable)]
        public void Classify_ReturnsTheVerdict(string key, WherePredicateVerdict expected)
        {
            Assert.AreEqual(expected, WherePredicateGuard.Classify(Predicates()[key]), key);
        }

        /// <summary>
        /// R21-1, R21-5 (AC-N13): under a cast through <c>object</c> around a part that reads the row the verdict reads no
        /// captured value, as 3.11.0's read none there: not a search value, and not one under a second cast, whether that
        /// cast reads the row or not.
        /// </summary>
        [TestMethod]
        public void Classify_UnderACastThroughObject_ReadsNoCapturedValue()
        {
            var shapes = new Dictionary<string, Expression<Func<GuardRow, bool>>>
            {
                ["search"] = x => (bool)(object)x.Name.Contains(counting.Empty),
                ["searchOrId"] = x => (bool)(object)(x.Name.Contains(counting.Empty) || x.Id == 2),
                ["nested"] = x => (bool)(object)((bool)(object)(x.Id == 2 || counting.Flag)),
                ["nestedRowFree"] = x => (bool)(object)(x.Id == 2 || (bool)(object)counting.Flag),
            };
            foreach (var shape in shapes)
            {
                CountingHolder.Reads = 0;
                Assert.AreEqual(WherePredicateVerdict.Acceptable, WherePredicateGuard.Classify(shape.Value), shape.Key);
                Assert.AreEqual(0, CountingHolder.Reads, shape.Key + ": a captured value under the cast is read");
            }
        }

        /// <summary>
        /// R21-4: an alias of a column the entity overrides is that column, as an alias of any column is; two overridden
        /// columns are two.
        /// </summary>
        [TestMethod]
        public void Classify_SeesAnAliasOfAnOverriddenColumnAsTheColumn()
        {
            Expression<Func<OverridingRow, bool>> alias = x => x.N == x.NX;
            Expression<Func<OverridingRow, bool>> two = x => x.N == x.M;
            Assert.AreEqual(WherePredicateVerdict.SelfReference, WherePredicateGuard.Classify(alias), "x.N == x.NX");
            Assert.AreEqual(WherePredicateVerdict.Acceptable, WherePredicateGuard.Classify(two), "x.N == x.M");
            // R22-1: an override of the setter alone is the property it overrides too.
            Expression<Func<SetterOverridingRow, bool>> setterAlias = x => x.N == x.NX;
            Assert.AreEqual(WherePredicateVerdict.SelfReference, WherePredicateGuard.Classify(setterAlias), "setter override: x.N == x.NX");
        }

        /// <summary>
        /// A stand-in for a provider's operand SQL (R18-1), in which <c>NameLenInt</c>'s text is <c>x.Name.Length</c>'s, as a
        /// template that is the provider's own SQL for a column's part is: in lower case for <paramref name="mode"/>
        /// <c>lower</c>, null for <c>null</c>, and a throw for <c>throws</c>. Every operand asked is logged in <paramref name="asked"/>.
        /// </summary>
        private static Func<Expression, string> TwinSql(List<string> asked, string mode = "")
        {
            return operand =>
            {
                asked.Add(operand.ToString());
                if (mode == "throws")
                    throw new NotSupportedException("no operand SQL");
                if (mode == "null")
                    return null;
                return operand.ToString().Replace("x.NameLenInt", mode == "lower" ? "x.name.length" : "x.Name.Length");
            };
        }

        /// <summary>
        /// R18-1: with the provider's operand SQL, two operands that read only the row and translate to the same text (letter
        /// case aside) are an operand compared with itself, under every wrapper, nested or not, where neither the expression
        /// (each row is Acceptable to <see cref="WherePredicateGuard.Classify"/>) nor the rewritten clause's text shows it. A
        /// captured value is never translated again (AC-N13), an operand that reads no row isn't asked, and an operand SQL
        /// that is null or throws leaves the verdict as it was.
        /// </summary>
        [DataTestMethod]
        [DataRow("twinFlat", "", SelfReferenceMessage)]
        [DataRow("twinCond", "", SelfReferenceMessage)]
        [DataRow("twinCondReversed", "", SelfReferenceMessage)]
        [DataRow("twinNested", "", SelfReferenceMessage)]
        [DataRow("twinNotNe", "", SelfReferenceMessage)]
        [DataRow("twinGe", "", SelfReferenceMessage)]
        [DataRow("twinLe", "", SelfReferenceMessage)]
        [DataRow("twinNotLt", "", SelfReferenceMessage)]
        [DataRow("twinNotGt", "", SelfReferenceMessage)]
        [DataRow("twinObj", "", SelfReferenceMessage)]
        [DataRow("twinOrId2", "", AlwaysTrueMessage)]
        [DataRow("twinInsideABoolComparison", "", SelfReferenceMessage)]
        [DataRow("twinWithAliasInTest", "", SelfReferenceMessage)]
        [DataRow("twinCond", "lower", SelfReferenceMessage)]
        [DataRow("twinRowFreeCall", "", "")]
        [DataRow("twinOtherTest", "", "")]
        [DataRow("twinOtherColumn", "", "")]
        [DataRow("twinCapturedTest", "", SelfReferenceMessage)]
        [DataRow("twinCapturedOtherTest", "", "")]
        [DataRow("twinCond", "null", "")]
        [DataRow("twinCond", "throws", "")]
        [DataRow("idEq2", "", "")]
        public void Validate_WithTheOperandSql_RefusesTwoOperandsOfTheSameSql(string key, string mode, string expectedMessage)
        {
            var two = 2;
            var alsoTwo = 2;
            var cases = new Dictionary<string, Expression<Func<GuardRow, bool>>>
            {
                ["twinFlat"] = x => x.NameLenInt == x.Name.Length,
                ["twinCond"] = x => (x.Flag ? x.NameLenInt : x.Other) == (x.Flag ? x.Name.Length : x.Other),
                ["twinCondReversed"] = x => (x.Flag ? x.Name.Length : x.Other) == (x.Flag ? x.NameLenInt : x.Other),
                ["twinNested"] = x => ((x.Flag ? x.Id : x.Other) == 5 ? x.NameLenInt : x.Other) == ((x.Flag ? x.Id : x.Other) == 5 ? x.Name.Length : x.Other),
                ["twinNotNe"] = x => !((x.Flag ? x.NameLenInt : x.Other) != (x.Flag ? x.Name.Length : x.Other)),
                ["twinGe"] = x => (x.Flag ? x.NameLenInt : x.Other) >= (x.Flag ? x.Name.Length : x.Other),
                ["twinLe"] = x => (x.Flag ? x.NameLenInt : x.Other) <= (x.Flag ? x.Name.Length : x.Other),
                ["twinNotLt"] = x => !((x.Flag ? x.NameLenInt : x.Other) < (x.Flag ? x.Name.Length : x.Other)),
                ["twinNotGt"] = x => !((x.Flag ? x.NameLenInt : x.Other) > (x.Flag ? x.Name.Length : x.Other)),
                ["twinObj"] = x => (bool)(object)((x.Flag ? x.NameLenInt : x.Other) == (x.Flag ? x.Name.Length : x.Other)),
                ["twinOrId2"] = x => (x.Flag ? x.NameLenInt : x.Other) == (x.Flag ? x.Name.Length : x.Other) || x.Id == 2,
                ["twinInsideABoolComparison"] = x => (x.NameLenInt == 1) == (x.Name.Length == 1),
                // Compared part by part: the tests differ by an alias, the branches by the twin, and neither whole operand's
                // text is the other's (a visitor folds x.N == x.N, not x.N == x.NX).
                ["twinWithAliasInTest"] = x => (x.N == x.N ? x.NameLenInt : x.Other) == (x.N == x.NX ? x.Name.Length : x.Other),
                // A call that reads no row is never sent for its SQL: a visitor would run it again.
                ["twinRowFreeCall"] = x => x.NameLenInt == Math.Max(x.Name.Length, Five()),
                // Neighbours: another test, another column. A captured value is compared as written, never translated: the
                // same captured member is the same value; another captured member isn't.
                ["twinOtherTest"] = x => (x.Id > 2 ? x.NameLenInt : x.Other) == (x.Id > 3 ? x.Name.Length : x.Other),
                ["twinOtherColumn"] = x => (x.Flag ? x.NameLenInt : x.Other) == (x.Flag ? x.Name.Length : x.Id),
                ["twinCapturedTest"] = x => (x.Id > two ? x.NameLenInt : x.Other) == (x.Id > two ? x.Name.Length : x.Other),
                ["twinCapturedOtherTest"] = x => (x.Id > two ? x.NameLenInt : x.Other) == (x.Id > alsoTwo ? x.Name.Length : x.Other),
                ["idEq2"] = x => x.Id == 2,
            };
            var predicate = cases[key];
            Assert.AreEqual(WherePredicateVerdict.Acceptable, WherePredicateGuard.Classify(predicate), key + ": without the operand SQL");
            var asked = new List<string>();
            var operandSql = TwinSql(asked, mode);
            if (expectedMessage.Length == 0)
            {
                WherePredicateGuard.Validate(predicate, WhereOperation.MassMutation, operandSql);
            }
            else
            {
                var refused = Assert.ThrowsException<InvalidOperationException>(
                    () => WherePredicateGuard.Validate(predicate, WhereOperation.MassMutation, operandSql), key);
                Assert.AreEqual(expectedMessage, refused.Message, key);
            }
            foreach (var operand in asked)
            {
                Assert.IsFalse(operand.Contains("value("), key + ": a captured value is translated again: " + operand);
                Assert.IsFalse(operand.Contains("Five()"), key + ": a call that reads no row is translated again: " + operand);
            }
            if (key == "idEq2")
                Assert.AreEqual(0, asked.Count, "an operand that reads no row is asked");
            if (mode == "null" || mode == "throws")
                Assert.AreNotEqual(0, asked.Count, key + ": nothing was asked");
        }

        /// <summary>
        /// R18-1: the comparer asks each node's SQL once (the parts here are shared nodes), so a long disjunction of twins, and a conditional chain whose every
        /// level compares a twin, stay linear. A timing proof with a wide margin.
        /// </summary>
        [TestMethod]
        public void Validate_WithTheOperandSql_OfLongPredicates_StaysLinear()
        {
            var row = Expression.Parameter(typeof(GuardRow), "x");
            Expression nameLen = Expression.Property(row, nameof(GuardRow.NameLenInt));
            Expression length = Expression.Property(Expression.Property(row, nameof(GuardRow.Name)), nameof(string.Length));
            Expression id = Expression.Property(row, nameof(GuardRow.Id));
            Expression other = Expression.Property(row, nameof(GuardRow.Other));
            Expression disjunction = Expression.Equal(id, Expression.Constant(2));
            for (var i = 0; i < DisjunctionTerms; i++)
                disjunction = Expression.OrElse(disjunction, Expression.OrElse(Expression.Equal(id, Expression.Constant(i)), Expression.Equal(nameLen, length)));
            // A chain whose every level compares the conditional below with a constant: the twin is in each level's branch;
            // and the same chain with another column in the branch, which isn't refused.
            Func<Expression, Expression> chain = branch =>
            {
                Expression level = branch;
                for (var i = 0; i < 60; i++)
                    level = Expression.Condition(Expression.Equal(level, Expression.Constant(i)), branch, other);
                return level;
            };
            // And chains whose every level compares the conditional below with a column, the column on the right and on the
            // left: both sides read the row, so the bound on each side is what keeps the cost per level bounded (R19-4).
            Func<Expression, bool, Expression> columnChain = (branch, columnLeft) =>
            {
                Expression level = branch;
                for (var i = 0; i < 60; i++)
                    level = Expression.Condition(columnLeft ? Expression.Equal(other, level) : Expression.Equal(level, other), branch, id);
                return level;
            };
            var bodies = new[]
            {
                (Body: disjunction, Refused: true),
                (Body: Expression.Equal(chain(nameLen), chain(length)), Refused: true),
                (Body: Expression.Equal(chain(nameLen), chain(id)), Refused: false),
                (Body: Expression.Equal(columnChain(nameLen, false), columnChain(length, false)), Refused: true),
                (Body: Expression.Equal(columnChain(nameLen, true), columnChain(length, true)), Refused: true),
            };
            foreach (var shape in bodies)
            {
                var asked = new List<string>();
                var predicate = Expression.Lambda<Func<GuardRow, bool>>(shape.Body, row);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                if (shape.Refused)
                {
                    var refused = Assert.ThrowsException<InvalidOperationException>(() =>
                        WherePredicateGuard.Validate(predicate, WhereOperation.MassMutation, TwinSql(asked)));
                    Assert.IsTrue(refused.Message == SelfReferenceMessage || refused.Message == AlwaysTrueMessage, refused.Message);
                }
                else
                {
                    WherePredicateGuard.Validate(predicate, WhereOperation.MassMutation, TwinSql(asked));
                }
                Assert.IsTrue(clock.Elapsed.TotalSeconds < 2, asked.Count + " parts asked: " + clock.Elapsed.TotalSeconds.ToString("F1") + " s");
                // Each node is asked once, and only a part of at most 64 nodes: the levels near the bottom, not one per level
                // (sixty levels without the bound ask about 240).
                Assert.IsTrue(asked.Count <= 64, asked.Count + " parts asked");
            }
        }

        public static int StaticFive => 5;

        /// <summary>
        /// R18-1, AC-N13: a part that reads or runs anything but the row and constants is never sent for its SQL, where a
        /// visitor would read it or run it again: a static member, another parameter, a constructor, an initializer, an
        /// array, an invocation, a nested lambda. Each part also reads the row and is compared with a part of another
        /// shape, so only the row-only check stands between it and the provider.
        /// </summary>
        [DataTestMethod]
        [DataRow("staticMember")]
        [DataRow("otherParameter")]
        [DataRow("constructor")]
        [DataRow("memberInit")]
        [DataRow("listInit")]
        [DataRow("newArray")]
        [DataRow("invocation")]
        [DataRow("nestedLambda")]
        [DataRow("parameterlessLambda")]
        public void Validate_WithTheOperandSql_NeverSendsAPartThatReadsOrRunsAnythingElse(string key)
        {
            Func<int, int> same = v => v;
            var row = Expression.Parameter(typeof(GuardRow), "x");
            var other = Expression.Parameter(typeof(GuardRow), "y");
            Expression<Func<GuardRow, bool>> P(Expression<Func<GuardRow, bool>> predicate) => predicate;
            var cases = new Dictionary<string, Expression<Func<GuardRow, bool>>>
            {
                ["staticMember"] = P(x => x.NameLenInt == Math.Max(x.Name.Length, StaticFive)),
                ["otherParameter"] = Expression.Lambda<Func<GuardRow, bool>>(
                    Expression.Equal(Expression.Property(row, nameof(GuardRow.NameLenInt)),
                        Expression.Call(typeof(Math).GetMethod(nameof(Math.Max), new[] { typeof(int), typeof(int) }),
                            Expression.Property(row, nameof(GuardRow.Id)), Expression.Property(other, nameof(GuardRow.Id)))), row),
                ["constructor"] = P(x => x.Created == new DateTime(x.Id, 1, 1)),
                ["memberInit"] = P(x => x.NameLenInt == new Inner { Name = x.Name }.Name.Length),
                ["listInit"] = P(x => x.NameLenInt == new List<int> { x.Id }.Count),
                ["newArray"] = P(x => x.NameLenInt == new[] { x.Id }.Length),
                ["invocation"] = P(x => x.NameLenInt == same(x.Id)),
                ["nestedLambda"] = P(x => x.NameLenInt == System.Linq.Enumerable.Count(x.Name, c => c == 'a')),
                ["parameterlessLambda"] = Expression.Lambda<Func<GuardRow, bool>>(
                    Expression.Equal(Expression.Property(row, nameof(GuardRow.NameLenInt)),
                        Expression.Invoke(Expression.Lambda<Func<int>>(Expression.Property(row, nameof(GuardRow.Id))))), row),
            };
            var asked = new List<string>();
            WherePredicateGuard.Validate(cases[key], WhereOperation.MassMutation, TwinSql(asked));
            Assert.AreEqual(0, asked.Count, key + ": " + string.Join(" / ", asked));
        }

        /// <summary>
        /// R19-2 (a): a member of the row's SQL is asked once, an alias's target's included, though the verdict sees an
        /// alias as a new node for the property it names each time it reaches it (and the compiler makes each written
        /// member a node of its own).
        /// </summary>
        [TestMethod]
        public void Validate_WithTheOperandSql_AsksEachMemberOnce()
        {
            var asked = new List<string>();
            Expression<Func<GuardRow, bool>> twice = x => x.NX == (int?)x.Other || x.NX == (int?)x.Id || x.N == (int?)x.Other;
            WherePredicateGuard.Validate(twice, WhereOperation.MassMutation, TwinSql(asked));
            Assert.AreEqual(asked.Count, new HashSet<string>(asked).Count, string.Join(" / ", asked));
            CollectionAssert.Contains(asked, "x.N");
            // R20-2: a column declared on a base class, whose alias the verdict resolves on the derived type: one member.
            asked.Clear();
            Expression<Func<DerivedRow, bool>> inherited = x => x.NX == (int?)x.Other || x.NX == (int?)x.Id || x.N == (int?)x.Other;
            WherePredicateGuard.Validate(inherited, WhereOperation.MassMutation, TwinSql(asked));
            Assert.AreEqual(asked.Count, new HashSet<string>(asked).Count, "a base class's column: " + string.Join(" / ", asked));
            // R21-4: a column the entity overrides, which the compiler writes as the base declaration and the alias's target
            // finds as the override: one member.
            asked.Clear();
            Expression<Func<OverridingRow, bool>> overridden = x => x.NX == (int?)x.Other || x.N == (int?)x.Id;
            WherePredicateGuard.Validate(overridden, WhereOperation.MassMutation, TwinSql(asked));
            Assert.AreEqual(asked.Count, new HashSet<string>(asked).Count, "an overridden column: " + string.Join(" / ", asked));
            CollectionAssert.Contains(asked, "x.N");
            // R22-1: an override of the setter alone, whose property on the entity has no getter: one member too.
            asked.Clear();
            Expression<Func<SetterOverridingRow, bool>> setterOverridden = x => x.NX == (int?)x.Other || x.N == (int?)x.Id;
            WherePredicateGuard.Validate(setterOverridden, WhereOperation.MassMutation, TwinSql(asked));
            Assert.AreEqual(asked.Count, new HashSet<string>(asked).Count, "a setter-only override: " + string.Join(" / ", asked));
            CollectionAssert.Contains(asked, "x.N");
            // R20-6 (X20-2): only a member of the row is keyed by its member: two columns' Length are two parts.
            asked.Clear();
            Expression<Func<GuardRow, bool>> lengths = x => x.Other == x.Name.Length || x.NameLenInt == x.NameX.Length;
            WherePredicateGuard.Validate(lengths, WhereOperation.MassMutation, TwinSql(asked));
            CollectionAssert.Contains(asked, "x.NameX.Length");
        }

        /// <summary>
        /// R19-4 (X14): the overload gives the verdict itself, whether or not any part is the provider's twin: the providers
        /// call it alone (R19-1).
        /// </summary>
        [DataTestMethod]
        [DataRow("selfEq", SelfReferenceMessage)]
        [DataRow("true", NoColumnMessage)]
        [DataRow("nameContainsEmpty", UnboundedSearchMessage)]
        [DataRow("notFlagSelfOrId2", AlwaysTrueMessage)]
        [DataRow("idEq2", "")]
        public void Validate_WithTheOperandSql_GivesTheVerdict(string key, string expectedMessage)
        {
            var predicate = Predicates()[key];
            var asked = new List<string>();
            if (expectedMessage.Length == 0)
            {
                WherePredicateGuard.Validate(predicate, WhereOperation.MassMutation, TwinSql(asked));
                return;
            }
            var refused = Assert.ThrowsException<InvalidOperationException>(
                () => WherePredicateGuard.Validate(predicate, WhereOperation.MassMutation, TwinSql(asked)), key);
            Assert.AreEqual(expectedMessage, refused.Message, key);
        }

        [TestMethod]
        public void Validate_WithTheOperandSql_ForARead_AsksNothing()
        {
            var asked = new List<string>();
            Expression<Func<GuardRow, bool>> twin = x => x.NameLenInt == x.Name.Length;
            WherePredicateGuard.Validate(twin, WhereOperation.Read, TwinSql(asked));
            Assert.AreEqual(0, asked.Count);
        }

        /// <summary>
        /// R18-1: an operand's text with each parameter written as its value, so two operands that send the same constants
        /// under different parameter names are the same text (letter case aside), and two that send different values, or the
        /// same value as another type, or a string in another letter case, are not. A longer name isn't read as a shorter one
        /// and a digit.
        /// </summary>
        [TestMethod]
        public void OperandText_WritesEachParameterAsItsValue()
        {
            KeyValuePair<string, object> P(string name, object value) => new KeyValuePair<string, object>(name, value);
            var first = WherePredicateGuard.OperandText("CASE WHEN t.id > @p__linq__1 THEN t.n ELSE @p__linq__10 END",
                new[] { P("@p__linq__1", 2), P("@p__linq__10", 7) });
            var renamed = WherePredicateGuard.OperandText("case when t.id > @p__linq__4 then t.n else @p__linq__5 end",
                new[] { P("@p__linq__4", 2), P("@p__linq__5", 7) });
            Assert.IsTrue(string.Equals(first, renamed, StringComparison.OrdinalIgnoreCase), first + " / " + renamed);
            foreach (var other in new[]
                     {
                         WherePredicateGuard.OperandText("CASE WHEN t.id > @p0 THEN t.n ELSE @p1 END", new[] { P("@p0", 3), P("@p1", 7) }),
                         WherePredicateGuard.OperandText("CASE WHEN t.id > @p0 THEN t.n ELSE @p1 END", new[] { P("@p0", 2L), P("@p1", 7) }),
                         WherePredicateGuard.OperandText("CASE WHEN t.id > @p0 THEN t.n ELSE @p1 END", new[] { P("@p0", 7), P("@p1", 2) }),
                     })
                Assert.IsFalse(string.Equals(first, other, StringComparison.OrdinalIgnoreCase), other);
            Assert.IsFalse(string.Equals(WherePredicateGuard.OperandText("t.name = @p0", new[] { P("@p0", "a") }),
                WherePredicateGuard.OperandText("t.name = @p0", new[] { P("@p0", "A") }), StringComparison.OrdinalIgnoreCase), "a string's letter case");
            Assert.AreEqual(WherePredicateGuard.OperandText("t.n = @p0", new[] { P("@p0", DBNull.Value) }),
                WherePredicateGuard.OperandText("t.n = @p0", new[] { P("@p0", null) }), "a null value, either way");
            Assert.AreEqual("LENGTH(t.name)", WherePredicateGuard.OperandText("LENGTH(t.name)", new KeyValuePair<string, object>[0]));
            // R19-3: letter case is aside only outside quoted text: a string literal, a JSON path and a quoted identifier
            // keep theirs, so a letter-case-blind comparison tells them apart; SQL's own words and bare names don't.
            string T(string sql) => WherePredicateGuard.OperandText(sql, new KeyValuePair<string, object>[0]);
            Assert.IsTrue(string.Equals(T("CASE WHEN t.name = 'a' THEN 1 END"), T("case when T.NAME = 'a' then 1 end"), StringComparison.OrdinalIgnoreCase));
            foreach (var pair in new[]
                     {
                         ("CASE WHEN t.name = 'a' THEN 1 END", "CASE WHEN t.name = 'A' THEN 1 END"),
                         ("json_extract(t.doc, '$.k')", "json_extract(t.doc, '$.K')"),
                         ("\"t\".\"Name\"", "\"t\".\"name\""),
                         ("[t].[Name]", "[t].[name]"),
                         ("`t`.`Name`", "`t`.`name`"),
                         ("t.name = 'it''s A'", "t.name = 'it''s a'"),
                     })
                Assert.IsFalse(string.Equals(T(pair.Item1), T(pair.Item2), StringComparison.OrdinalIgnoreCase), pair.Item1 + " / " + pair.Item2);
            Assert.IsTrue(string.Equals(T("t.name = 'it''s a'"), T("T.NAME = 'it''s a'"), StringComparison.OrdinalIgnoreCase), "an escaped quote");
            // R20-6 (X20-1): exactly what is written: a quoted run's characters in hexadecimal between « and », the text around it kept.
            Assert.AreEqual("t.k = \u00ab002700610027\u00bb AND t.m", T("t.k = 'a' AND t.m"));
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
        [DataRow("childNameContainsEmpty")]
        [DataRow("nSelfLt")]
        [DataRow("orNotBoxedTrue")]
        [DataRow("nameContainsEmptyEqFalse")]
        [DataRow("idSelfNeTrue")]
        [DataRow("flagEqTrue")]
        [DataRow("nameToStringContainsEmpty")]
        [DataRow("nSelfGt")]
        [DataRow("castNameContainsEmpty")]
        [DataRow("fieldContainsEmpty")]
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
        // Review R-2: the shapes the round-1 fixes let through (A/N-1, A/N-2) and the lifted-< fold (A/N-4).
        [DataRow("orBoxedTrue", AlwaysTrueMessage)]
        [DataRow("idOrNullableGeSelf", AlwaysTrueMessage)]
        [DataRow("idOrNameLengthSelf", AlwaysTrueMessage)]
        [DataRow("notAmountNeSelf", SelfReferenceMessage)]
        [DataRow("notNSelfLt", SelfReferenceMessage)]
        // Review R-4 (round-3 A/N-1).
        [DataRow("nameContainsEmptyEqTrue", UnboundedSearchMessage)]
        [DataRow("nameContainsEmptyNeFalse", UnboundedSearchMessage)]
        [DataRow("idSelfEqTrue", AlwaysTrueMessage)]
        [DataRow("nSelfLe", SelfReferenceMessage)]
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
        // Review R-2 (A/N-2): the broad verdict doesn't widen the SQL fold.
        [DataRow("idOrNameLengthSelf", null)]
        [DataRow("idOrYearSelf", null)]
        [DataRow("notAmountNeSelf", null)]
        [DataRow("idOrNullableGeSelf", null)]
        [DataRow("childNameContainsEmpty", null)]
        // Review R-2 (A/N-4): C#'s lifted < is false for every row, so it folds, and its negation holds for every row.
        [DataRow("nSelfLt", false)]
        [DataRow("notNSelfLt", true)]
        // Review R-4 (round-3 B/F-2): <= stays unfolded like >=; > folds like <.
        [DataRow("nSelfLe", null)]
        [DataRow("nSelfGt", false)]
        // Review R-2 (A/N-1): a bool unboxed from a captured object is read.
        [DataRow("orBoxedTrue", true)]
        [DataRow("orNotBoxedTrue", null)]
        // Review R-4 (round-3 A/N-1): a bool compared with a bool composes the operands' truths; one that reads the
        // row stays unknown, and an empty search value is a column read for the SQL fold.
        [DataRow("idSelfEqTrue", true)]
        [DataRow("idSelfNeTrue", false)]
        [DataRow("nameContainsEmptyEqTrue", null)]
        [DataRow("flagEqTrue", null)]
        [DataRow("orCapturedTrueEqFalse", null)]
        [DataRow("orCapturedTrueNeFalse", true)]
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
        /// 3.12.0 R9-3 (AC-N13): the fold stops at an operand of <c>&amp;&amp;</c> or <c>||</c> that decides the whole, as C#
        /// does, and asks an operand that reads no row before one that reads it, so a captured value the answer doesn't
        /// need isn't read.
        /// </summary>
        [TestMethod]
        public void Fold_StopsAtAnOperandThatDecides()
        {
            var off = false;
            var on = true;
            CountingHolder.Reads = 0;
            Assert.AreEqual(false, WherePredicateGuard.Fold(P(x => off && counting.Flag).Body, typeof(GuardRow)), "false && …");
            Assert.AreEqual(0, CountingHolder.Reads, "the right operand of a false && is read");
            Assert.AreEqual(true, WherePredicateGuard.Fold(P(x => on || counting.Flag).Body, typeof(GuardRow)), "true || …");
            Assert.AreEqual(0, CountingHolder.Reads, "the right operand of a true || is read");
            Assert.AreEqual(false, WherePredicateGuard.Fold(P(x => (x.Id == 1 && counting.Flag) && off).Body, typeof(GuardRow)), "… && false");
            Assert.AreEqual(0, CountingHolder.Reads, "a captured operand that decides is asked first, so nothing in the row operand is read");
            Assert.AreEqual(true, WherePredicateGuard.Fold(P(x => (x.Id == 1 && counting.Flag) || on).Body, typeof(GuardRow)), "… || true");
            Assert.AreEqual(0, CountingHolder.Reads, "a captured operand that decides is asked first (||)");
            Assert.AreEqual(false, WherePredicateGuard.Fold(P(x => off & counting.Flag).Body, typeof(GuardRow)), "false & …");
            Assert.AreEqual(true, WherePredicateGuard.Fold(P(x => on | counting.Flag).Body, typeof(GuardRow)), "true | …");
            Assert.AreEqual(0, CountingHolder.Reads, "the bitwise forms are decided the same way");
            Assert.IsNull(WherePredicateGuard.Fold(P(x => (x.Id == 1 && counting.Flag) || off).Body, typeof(GuardRow)), "… || false is unknown");
            Assert.AreEqual(1, CountingHolder.Reads, "an operand that doesn't decide: the other is folded");
            Assert.AreEqual(true, WherePredicateGuard.Fold(P(x => counting.Flag || x.Id == 1).Body, typeof(GuardRow)), "both read no row, or the left: left first");
            Assert.AreEqual(2, CountingHolder.Reads);
        }

        /// <summary>
        /// R17-1: the text check of EF's self-equality is linear: a clause that starts as one and runs on for a quarter of a
        /// million characters (16,000 `IS NULL AND`s), a million-character one with no outer parentheses, and EF's form with
        /// 64,000 witnesses in its NULL test, each check in well under a second (rev 17's pattern: 105 and 174 seconds for the
        /// first two). All are accepted: none is one self-equality with EF's two witnesses.
        /// </summary>
        [TestMethod]
        public void ValidateTranslation_OfLongClauses_StaysLinear()
        {
            var nulls = new StringBuilder("(r.n = r.n OR (");
            for (var i = 0; i < 16000; i++)
                nulls.Append("r.n IS NULL AND ");
            nulls.Append("r.m IS NULL) OR r.q = 1)");
            var disjunction = new StringBuilder("(r.n = r.n OR (r.n IS NULL AND r.n IS NULL)) OR (");
            for (var i = 0; i < 16000; i++)
                disjunction.Append("(r.a" + i + " = r.b" + i + " OR (r.a" + i + " IS NULL AND r.b" + i + " IS NULL)) AND ");
            disjunction.Append("r.q = 1)");
            // The parser's own worst case: EF's form with 64,000 witnesses in its NULL test, a million characters, each split a
            // candidate (only those short enough to be tokens of the operand are tried; trying all copies the clause at each).
            var witnesses = new StringBuilder("(r.n = r.n OR (");
            for (var i = 0; i < 64000; i++)
                witnesses.Append("r.n IS NULL AND ");
            witnesses.Append("r.n IS NULL))");
            foreach (var clause in new[] { nulls.ToString(), disjunction.ToString(), witnesses.ToString() })
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                WherePredicateGuard.ValidateTranslation(clause);
                Assert.IsTrue(clock.Elapsed.TotalSeconds < 2, clause.Length + " characters: " + clock.Elapsed.TotalSeconds.ToString("F1") + " s");
            }
        }

        /// <summary>
        /// 3.12.0 R10-4: the fold's order (a small captured operand first) is decided per level without walking the rest of
        /// the chain, so a long chain whose right operands are captured costs about what one whose operands read the row
        /// does. A timing smoke check, best of five, with a wide margin: the linear fold takes six to eight times the row
        /// chain's time, so the bound (ten times, plus 50 ms) holds on a slow or loaded runner (Task 8, T8A-1: four times
        /// plus 15 ms failed once by 0.8 ms under load). The quadratic walk (the plan's Z34) is pinned by
        /// <see cref="Fold_AsksASmallOperandFirst_UpToItsBound"/>: in a Release build the 50 ms can hide it here (T8D-3).
        /// </summary>
        [TestMethod]
        public void Fold_OfALongChainOfCapturedOperands_StaysLinear()
        {
            var x = Expression.Parameter(typeof(GuardRow), "x");
            var off = Expression.Property(Expression.Constant(new ChainHolder()), nameof(ChainHolder.Flag));
            Expression rowChain = Expression.Equal(Expression.Property(x, nameof(GuardRow.Id)), Expression.Constant(0));
            Expression capturedChain = rowChain;
            for (var i = 1; i < DisjunctionTerms; i++)
            {
                rowChain = Expression.OrElse(rowChain, Expression.Equal(Expression.Property(x, nameof(GuardRow.Id)), Expression.Constant(i)));
                capturedChain = Expression.OrElse(capturedChain, Expression.Not(off));
            }
            var rows = Expression.Lambda<Func<GuardRow, bool>>(rowChain, x);
            var captured = Expression.Lambda<Func<GuardRow, bool>>(capturedChain, x);
            Func<LambdaExpression, double> best = lambda =>
            {
                var fastest = double.MaxValue;
                for (var run = 0; run < 5; run++)
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    WherePredicateGuard.Classify(lambda);
                    WherePredicateGuard.Fold(lambda.Body, typeof(GuardRow));
                    fastest = Math.Min(fastest, clock.Elapsed.TotalMilliseconds);
                }
                return fastest;
            };
            best(rows);
            best(captured);
            var rowTime = best(rows);
            var capturedTime = best(captured);
            Assert.IsTrue(capturedTime <= 10 * rowTime + 50,
                "captured operands: " + capturedTime.ToString("F1") + " ms against " + rowTime.ToString("F1") + " ms for row operands");
        }

        /// <summary>
        /// R11-6: the operand asked first is small, 32 nodes at most. A right operand of 32 nodes that decides an `&amp;&amp;` is
        /// asked first, so nothing in the left is read; one of 33 isn't, so the left's captured operand is read.
        /// </summary>
        [TestMethod]
        public void Fold_AsksASmallOperandFirst_UpToItsBound()
        {
            var off = false;
            var on = true;
            Expression thirtyTwo = ((Expression<Func<GuardRow, bool>>)(x => off)).Body;   // 2 nodes, false
            for (var i = 0; i < 30; i++)
                thirtyTwo = Expression.Not(thirtyTwo);                                    // 32 nodes, false
            Expression thirtyThree = ((Expression<Func<GuardRow, bool>>)(x => on)).Body;  // 2 nodes, true
            for (var i = 0; i < 31; i++)
                thirtyThree = Expression.Not(thirtyThree);                                // 33 nodes, false
            var left = P(x => x.Id == 1 && counting.Flag).Body;
            CountingHolder.Reads = 0;
            Assert.AreEqual(false, WherePredicateGuard.Fold(Expression.AndAlso(left, thirtyTwo), typeof(GuardRow)), "32 nodes");
            Assert.AreEqual(0, CountingHolder.Reads, "a right operand of 32 nodes is asked first");
            Assert.AreEqual(false, WherePredicateGuard.Fold(Expression.AndAlso(left, thirtyThree), typeof(GuardRow)), "33 nodes");
            Assert.AreEqual(1, CountingHolder.Reads, "a right operand of 33 nodes isn't");
        }

        /// <summary>A captured flag for the chain above, apart from <see cref="CountingHolder"/>'s shared count.</summary>
        private sealed class ChainHolder
        {
            public bool Flag => true;
        }

        private const int DisjunctionTerms = 1500;

        /// <summary>A left-deep disjunction of <see cref="DisjunctionTerms"/> terms, each reading the row: 1500 comparisons under 1499 <c>||</c>.</summary>
        private static Expression<Func<GuardRow, bool>> LongDisjunction()
        {
            var x = Expression.Parameter(typeof(GuardRow), "x");
            Expression body = Expression.Equal(Expression.Property(x, nameof(GuardRow.Id)), Expression.Constant(0));
            for (var i = 1; i < DisjunctionTerms; i++)
                body = Expression.OrElse(body, Expression.Equal(Expression.Property(x, nameof(GuardRow.Id)), Expression.Constant(i)));
            return Expression.Lambda<Func<GuardRow, bool>>(body, x);
        }

        /// <summary>Every truth position of a disjunction: each <c>||</c> and each of its operands.</summary>
        private static IEnumerable<Expression> TruthPositions(Expression body)
        {
            var pending = new Stack<Expression>();
            pending.Push(body);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                yield return node;
                if (node is BinaryExpression binary && binary.NodeType == ExpressionType.OrElse)
                {
                    pending.Push(binary.Left);
                    pending.Push(binary.Right);
                }
            }
        }

        /// <summary>A memo that counts its reads that hit and its writes, so a test can tell a lookup from a fold.</summary>
        private sealed class CountingMemo : IDictionary<Expression, bool?>
        {
            private readonly Dictionary<Expression, bool?> _inner = new Dictionary<Expression, bool?>();
            public int Hits;
            public int Writes;

            public bool TryGetValue(Expression key, out bool? value)
            {
                var found = _inner.TryGetValue(key, out value);
                if (found)
                    Hits++;
                return found;
            }

            public bool? this[Expression key]
            {
                get => _inner[key];
                set
                {
                    Writes++;
                    _inner[key] = value;
                }
            }

            public int Count => _inner.Count;
            public bool IsReadOnly => false;
            public ICollection<Expression> Keys => _inner.Keys;
            public ICollection<bool?> Values => _inner.Values;
            public void Add(Expression key, bool? value) { Writes++; _inner.Add(key, value); }
            public void Add(KeyValuePair<Expression, bool?> item) => Add(item.Key, item.Value);
            public bool ContainsKey(Expression key) => _inner.ContainsKey(key);
            public bool Contains(KeyValuePair<Expression, bool?> item) => _inner.ContainsKey(item.Key);
            public bool Remove(Expression key) => _inner.Remove(key);
            public bool Remove(KeyValuePair<Expression, bool?> item) => _inner.Remove(item.Key);
            public void Clear() => _inner.Clear();
            public void CopyTo(KeyValuePair<Expression, bool?>[] array, int arrayIndex) => ((ICollection<KeyValuePair<Expression, bool?>>)_inner).CopyTo(array, arrayIndex);
            public IEnumerator<KeyValuePair<Expression, bool?>> GetEnumerator() => _inner.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _inner.GetEnumerator();
        }

        /// <summary>
        /// Review R-1 (A-4, B-4) and R-2 (B/N-4, A/N-6): the memo is pinned by counting, not by a clock. Folding the body
        /// records every <c>bool</c> node once (1500 comparisons and 1499 <c>||</c>); folding each truth position
        /// afterwards with the same memo is all lookups and no new fold, so the per-position folds a visitor makes are
        /// one pass over the tree, not one per level.
        /// </summary>
        [TestMethod]
        public void Fold_WithAMemo_FoldsEachTruthPositionOnce()
        {
            var body = LongDisjunction().Body;
            var memo = new CountingMemo();

            Assert.IsNull(WherePredicateGuard.Fold(body, typeof(GuardRow), memo), "the whole reads the row");
            Assert.AreEqual(2 * DisjunctionTerms - 1, memo.Writes, "the body's fold should record each bool node once");
            Assert.AreEqual(0, memo.Hits, "the body's fold shouldn't look up what it hasn't recorded");

            var positions = 0;
            foreach (var position in TruthPositions(body))
            {
                positions++;
                Assert.IsNull(WherePredicateGuard.Fold(position, typeof(GuardRow), memo), "every position reads the row");
            }
            Assert.AreEqual(2 * DisjunctionTerms - 1, positions);
            Assert.AreEqual(2 * DisjunctionTerms - 1, memo.Writes, "a truth position folded with the memo was folded again");
            Assert.AreEqual(positions, memo.Hits, "a truth position wasn't answered from the memo");
        }

        /// <summary>
        /// Review R-2 (B/N-4): each provider's WHERE visitor keeps one memo per translation and folds through it, so
        /// after translating the long disjunction its memo holds every <c>bool</c> node; a visitor that folded without
        /// the memo would hold none.
        /// </summary>
        [TestMethod]
        public void EveryVisitor_FoldsThroughItsMemo()
        {
            var predicate = LongDisjunction();
            var expected = 2 * DisjunctionTerms - 1;
            var columns = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
            var unmapped = new List<System.Reflection.PropertyInfo>();

            var sqlGenerator = new Funcular.Data.Orm.Visitors.ParameterGenerator();
            var sqlServer = new Funcular.Data.Orm.Visitors.WhereClauseVisitor<GuardRow>(columns, unmapped, sqlGenerator,
                new Funcular.Data.Orm.Visitors.SqlExpressionTranslator(sqlGenerator), "t");
            sqlServer.Visit(predicate);
            Assert.AreEqual(DisjunctionTerms, sqlServer.Parameters.Count, "SQL Server: every term became a parameter");
            Assert.AreEqual(expected, sqlServer.Truths.Count, "SQL Server: the visitor didn't fold through its memo");

            var pgGenerator = new Funcular.Data.Orm.PostgreSql.Visitors.PostgreSqlParameterGenerator();
            var postgres = new Funcular.Data.Orm.PostgreSql.Visitors.PostgreSqlWhereClauseVisitor<GuardRow>(columns, unmapped, pgGenerator,
                new Funcular.Data.Orm.PostgreSql.Visitors.PostgreSqlExpressionTranslator(pgGenerator), "t");
            postgres.Visit(predicate);
            Assert.AreEqual(expected, postgres.Truths.Count, "PostgreSQL: the visitor didn't fold through its memo");

            var myGenerator = new Funcular.Data.Orm.MySql.Visitors.MySqlParameterGenerator();
            var mySql = new Funcular.Data.Orm.MySql.Visitors.MySqlWhereClauseVisitor<GuardRow>(columns, unmapped, myGenerator,
                new Funcular.Data.Orm.MySql.Visitors.MySqlExpressionTranslator(myGenerator), "t");
            mySql.Visit(predicate);
            Assert.AreEqual(expected, mySql.Truths.Count, "MySQL: the visitor didn't fold through its memo");

            var liteGenerator = new Funcular.Data.Orm.Sqlite.Visitors.SqliteParameterGenerator();
            var sqlite = new Funcular.Data.Orm.Sqlite.Visitors.SqliteWhereClauseVisitor<GuardRow>(columns, unmapped, liteGenerator,
                new Funcular.Data.Orm.Sqlite.Visitors.SqliteExpressionTranslator(liteGenerator), "t");
            sqlite.Visit(predicate);
            Assert.AreEqual(expected, sqlite.Truths.Count, "SQLite: the visitor didn't fold through its memo");
        }

        /// <summary>
        /// Review R-1 (A-4) and R-2 (B/N-4): a <see cref="WhereOperation.Read"/> isn't classified, so the captured part
        /// of the predicate isn't read at all; the mass-mutation form reads it to reach its verdict.
        /// </summary>
        [TestMethod]
        public void Validate_ForARead_DoesNotEvaluateThePredicate()
        {
            Expression<Func<GuardRow, bool>> predicate = x => counting.Flag || x.Id == 1;

            CountingHolder.Reads = 0;
            WherePredicateGuard.Validate(predicate, WhereOperation.Read);
            Assert.AreEqual(0, CountingHolder.Reads, "a read evaluated the captured part");

            Assert.ThrowsException<InvalidOperationException>(() => WherePredicateGuard.Validate(predicate, WhereOperation.MassMutation));
            Assert.IsTrue(CountingHolder.Reads >= 1, "the mass-mutation classification didn't read the captured part");
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
        // R14-3: EF's form of a self-comparison whose operand can be null (3.12.0), where 3.11.0 sent `t.n = t.n`: the
        // operand and its own copy, or a witness, tested for NULL beside it. Embedded in an AND or an OR, as 3.11.0's was.
        [DataRow("(t.n = t.n OR (t.n IS NULL AND t.n IS NULL))", SelfReferenceMessage)]
        [DataRow("(LENGTH(t.name) = LENGTH(t.name) OR t.name IS NULL)", SelfReferenceMessage)]
        [DataRow("((LEN(t.name + N'x') - 1) = (LEN(t.name + N'x') - 1) OR t.name IS NULL)", SelfReferenceMessage)]
        [DataRow("(t.n = t.m OR (t.n IS NULL AND t.m IS NULL))", "")]
        [DataRow("(t.n = t.n OR (t.n IS NULL AND t.m IS NULL))", "")]
        [DataRow("((t.n = t.n OR (t.n IS NULL AND t.n IS NULL)) OR t.id = @p__linq__0)", "")]
        [DataRow("(t.id = @p__linq__0 AND (t.n = t.n OR (t.n IS NULL AND t.n IS NULL)))", "")]
        // R15-2: a part of a column compared with the same part of its alias: EF's form tests the column itself for NULL.
        [DataRow("(LENGTH(t.name) = LENGTH(t.name) OR (t.name IS NULL AND t.name IS NULL))", SelfReferenceMessage)]
        [DataRow("((LEN(t.name + N'x') - 1) = (LEN(t.name + N'x') - 1) OR (t.name IS NULL AND t.name IS NULL))", SelfReferenceMessage)]
        [DataRow("(DATEPART(year, t.d) = DATEPART(year, t.d) OR (t.d IS NULL AND t.d IS NULL))", SelfReferenceMessage)]
        [DataRow("((LEN([t].[name] + N'x') - 1) = (LEN([t].[name] + N'x') - 1) OR ([t].[name] IS NULL AND [t].[name] IS NULL))", SelfReferenceMessage)]
        // R16-1: a computed property whose SQL is a column's part, against that part: two witnesses, the computed SQL and
        // the column, each read by the operand.
        [DataRow("(LENGTH(t.name) = LENGTH(t.name) OR (LENGTH(t.name) IS NULL AND t.name IS NULL))", SelfReferenceMessage)]
        [DataRow("((LEN(t.name + N'x') - 1) = (LEN(t.name + N'x') - 1) OR ((LEN(t.name + N'x') - 1) IS NULL AND t.name IS NULL))", SelfReferenceMessage)]
        [DataRow("(CHAR_LENGTH(`t`.`name`) = CHAR_LENGTH(`t`.`name`) OR (`t`.`name` IS NULL AND CHAR_LENGTH(`t`.`name`) IS NULL))", SelfReferenceMessage)]
        [DataRow("(DATEPART(year, t.d) = DATEPART(year, t.d) OR (t.d IS NULL AND DATEPART(year, t.d) IS NULL))", SelfReferenceMessage)]
        [DataRow("(LENGTH(t.name) = LENGTH(t.name) OR (LENGTH(t.name) IS NULL AND t.other IS NULL))", "")]
        // R17-1: an operand whose own SQL holds `IS NULL AND` (a template, or a conditional whose test is EF's both-null
        // equality): every split of the witnesses is tried, not only the first.
        [DataRow("(CASE WHEN t.n IS NULL AND t.m IS NULL THEN 0 ELSE t.n END = CASE WHEN t.n IS NULL AND t.m IS NULL THEN 0 ELSE t.n END OR (CASE WHEN t.n IS NULL AND t.m IS NULL THEN 0 ELSE t.n END IS NULL AND CASE WHEN t.n IS NULL AND t.m IS NULL THEN 0 ELSE t.n END IS NULL))", SelfReferenceMessage)]
        [DataRow("( CASE WHEN (t.n = t.m OR (t.n IS NULL AND t.m IS NULL)) THEN t.n ELSE t.m END =  CASE WHEN (t.n = t.m OR (t.n IS NULL AND t.m IS NULL)) THEN t.n ELSE t.m END OR ( CASE WHEN (t.n = t.m OR (t.n IS NULL AND t.m IS NULL)) THEN t.n ELSE t.m END IS NULL AND  CASE WHEN (t.n = t.m OR (t.n IS NULL AND t.m IS NULL)) THEN t.n ELSE t.m END IS NULL))", SelfReferenceMessage)]
        [DataRow("(CASE WHEN t.n IS NULL AND t.m IS NULL THEN 0 ELSE t.n END = CASE WHEN t.n IS NULL AND t.m IS NULL THEN 0 ELSE t.n END OR (CASE WHEN t.n IS NULL AND t.m IS NULL THEN 0 ELSE t.n END IS NULL AND t.other IS NULL))", "")]
        [DataRow("(t.n = t.n OR (t.n IS NULL AND t.n IS NULL)) OR t.id = @p__linq__0", "")]
        [DataRow("(t.n = t.n OR ')' IS NULL)", "")]
        [DataRow("(COALESCE(t.name, ')') = COALESCE(t.name, ')') OR (COALESCE(t.name, ')') IS NULL AND t.name IS NULL))", SelfReferenceMessage)]
        // The parser's other paths: whitespace before the closing parenthesis and after the opening one, an escaped quote,
        // an unbalanced clause, a group with no OR before it, a group without IS NULL, a bare IS NULL without an OR.
        [DataRow("( t.n = t.n OR t.n IS NULL )", SelfReferenceMessage)]
        [DataRow("(t.n = t.n OR 'x''y' IS NULL)", "")]
        [DataRow("(t.n = t.n))", "")]
        [DataRow("((t.n IS NULL AND t.n IS NULL))", "")]
        [DataRow("(t.n = t.n OR (t.n = 1))", "")]
        [DataRow("(t.n IS NULL)", "")]
        // R15-4: a NULL test of a column the comparison doesn't read is 3.11.0's own text, which it accepted; a column
        // whose name begins another's isn't read by it.
        [DataRow("(t.active = t.active OR t.flag IS NULL)", "")]
        [DataRow("(LENGTH(t.name) = LENGTH(t.name) OR (t.other IS NULL AND t.other IS NULL))", "")]
        [DataRow("(t.name = t.name OR t.n IS NULL)", "")]
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
            // Review R-2 (A/N-5): the 3.10.0 enum gained UnboundedSearch, so Classify's verdict and Validate's message agree.
            foreach (var key in new[] { "idEq2", "true", "selfEq", "orTrue", "nameContainsEmpty" })
                Assert.AreEqual(WherePredicateGuard.Classify(predicates[key]).ToString(),
                    DeletePredicateGuard.Classify(predicates[key]).ToString(), key);
            Assert.AreEqual(DeletePredicateVerdict.UnboundedSearch, DeletePredicateGuard.Classify(predicates["nameContainsEmpty"]));
            var unbounded = Assert.ThrowsException<InvalidOperationException>(() => DeletePredicateGuard.Validate(predicates["nameContainsEmpty"]));
            Assert.AreEqual(UnboundedSearchMessage, unbounded.Message);
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
        [DataRow("validateWithOperandSqlNull")]
        [DataRow("operandSqlNull")]
        [DataRow("operandTextNull")]
        [DataRow("operandTextParametersNull")]
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
                case "validateWithOperandSqlNull":
                    Assert.ThrowsException<ArgumentNullException>(() => WherePredicateGuard.Validate(null, WhereOperation.MassMutation, operand => ""));
                    break;
                case "operandSqlNull":
                    Expression<Func<GuardRow, bool>> idEq2 = x => x.Id == 2;
                    Assert.ThrowsException<ArgumentNullException>(() => WherePredicateGuard.Validate(idEq2, WhereOperation.Read, null));
                    break;
                case "operandTextNull":
                    Assert.ThrowsException<ArgumentNullException>(() => WherePredicateGuard.OperandText(null, new KeyValuePair<string, object>[0]));
                    break;
                case "operandTextParametersNull":
                    Assert.ThrowsException<ArgumentNullException>(() => WherePredicateGuard.OperandText("t.n", null));
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
