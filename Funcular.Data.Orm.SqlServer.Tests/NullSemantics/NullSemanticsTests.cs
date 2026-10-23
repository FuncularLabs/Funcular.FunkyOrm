// Linked into SqlServer.Tests.NetFramework (net48, MSTest 2.x): keep it C# 7.3-compatible.
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests.NullSemantics
{
    /// <summary>
    /// DB-free tests of the Core <see cref="Funcular.Data.Orm.Linq.NullSemantics"/> rewrite (docs/plans/NULL_SEMANTICS_PLAN.md
    /// §5.1, §6.2's "core" row): the shapes it builds, what it reads, and its classification of operands. The rows each
    /// provider selects are proved by the NullSemanticsHarness.
    /// </summary>
    [TestClass]
    public class NullSemanticsTests
    {
        #region Fixtures

        public enum Shade { Red, Green }

        public struct Money
        {
            public decimal Value;
            public static implicit operator decimal?(Money money) => money.Value;
        }

        /// <summary>A value type with its own comparison operators: a comparison with them isn't the rewrite's to change.</summary>
        public struct Points
        {
            public int Value;
            public static bool operator ==(Points left, Points right) => left.Value == right.Value;
            public static bool operator !=(Points left, Points right) => left.Value != right.Value;
            public static bool operator >(Points left, Points right) => left.Value > right.Value;
            public static bool operator <(Points left, Points right) => left.Value < right.Value;
            public override bool Equals(object obj) => obj is Points other && other.Value == Value;
            public override int GetHashCode() => Value;
        }

        public class Inner
        {
            public int Id { get; set; }
        }

        public class Parent
        {
            public int Id { get; set; }
            public int Code { get; set; }
        }

        public class ChildRow
        {
            public int Id { get; set; }
            public int RowId { get; set; }
            public decimal Amount { get; set; }
        }

        /// <summary>R17-4: a base class's template naming a template of the derived row, which the providers resolve against the row's type.</summary>
        public class RowBase
        {
            [SqlExpression("COALESCE({DerivedCase}, 0)")] public int? BaseRef { get; set; }
        }

        public class Row : RowBase
        {
            public int Id { get; set; }
            public int? N { get; set; }
            public int? M { get; set; }
            public string Name { get; set; }
            public short Code { get; set; }
            public long? NLong { get; set; }
            public decimal? NPrice { get; set; }
            public Shade? E { get; set; }
            public DateTimeOffset? Dto { get; set; }
            public DateTime? D { get; set; }
            public DateTime Posted { get; set; }
            public DateTimeOffset Stamp { get; set; }
            public bool? Flag { get; set; }
            public bool? Flag2 { get; set; }
            public bool Active { get; set; }
            public bool Other { get; set; }
            public Money Cost { get; set; }
            public Inner Child { get; set; }
            [NotMapped] public int Unmapped { get; set; }
            [NotMapped] public bool UnmappedFlag { get; set; }
            public Points Score { get; set; }
            public int Field;
            [RemoteLink(typeof(Parent))] public int ParentId { get; set; }
            [RemoteProperty(typeof(Parent), nameof(ParentId), nameof(Parent.Code))] public int ParentCode { get; set; }
            [RemoteKey(typeof(Parent), nameof(ParentId), nameof(Parent.Id))] public int ParentKey { get; set; }
            [JsonPath("doc", "$.k")] public string DocK { get; set; }
            [SqlExpression("{N} + 1")] public int NPlusOne { get; set; }
            // Templates with a level of their own as SQL Server counts them (R16-2, R16-3), and some without.
            [SqlExpression("CASE WHEN {N} > 0 THEN {N} ELSE {M} END")] public int? NCase { get; set; }
            [SqlExpression("{NCase} + 1")] public int? NCaseRef { get; set; }
            [SqlExpression("IIF({N} > 0, {N}, {M})", "{N}")] public int? NIif { get; set; }
            [SqlExpression("COALESCE({N}, {M})")] public int? NCoalesce { get; set; }
            [SqlExpression("NULLIF({N}, 0)")] public int? NNullIf { get; set; }
            [SqlExpression("{N} + LENGTH('CASE COALESCE(')")] public int? NQuoted { get; set; }
            [SqlExpression("{N}")] public int? NX { get; set; }
            [SqlExpression("{CycleB}")] public int? CycleA { get; set; }
            [SqlExpression("{CycleA}")] public int? CycleB { get; set; }
            // Siblings, one level each: a closed function or CASE gives its level back.
            [SqlExpression("COALESCE({N}, 0) + COALESCE({M}, 0)")] public int? NTwoCoalesces { get; set; }
            [SqlExpression("CASE WHEN {N} > 0 THEN 1 ELSE 0 END + CASE WHEN {M} > 0 THEN 1 ELSE 0 END")] public int? NTwoCases { get; set; }
            // Two levels each (R17-4, R17-6): an END inside an identifier or a comment is no END; the derived template a base
            // one names counts; the same template named twice counts where each sits.
            [SqlExpression("CASE WHEN {N} > 0 THEN CASE WHEN {M} > 0 THEN 1 ELSE 2 END ELSE 0 END")] public int? Case2 { get; set; }
            [SqlExpression("CASE WHEN [End] > 0 THEN CASE WHEN {M} > 0 THEN 1 ELSE 2 END ELSE 0 END")] public int? Case2Bracket { get; set; }
            [SqlExpression("CASE WHEN \"End\" > 0 THEN CASE WHEN {M} > 0 THEN 1 ELSE 2 END ELSE 0 END")] public int? Case2Quoted { get; set; }
            [SqlExpression("CASE WHEN `End` > 0 THEN CASE WHEN {M} > 0 THEN 1 ELSE 2 END ELSE 0 END")] public int? Case2Backtick { get; set; }
            [SqlExpression("CASE WHEN {N} > 0 /* END */ THEN CASE WHEN {M} > 0 THEN 1 ELSE 2 END ELSE 0 END")] public int? Case2Comment { get; set; }
            [SqlExpression("CASE WHEN {N} > 0 -- END\n THEN CASE WHEN {M} > 0 THEN 1 ELSE 2 END ELSE 0 END")] public int? Case2LineComment { get; set; }
            [SqlExpression("CASE WHEN {N} > 0 THEN {N} END")] public int? DerivedCase { get; set; }
            [SqlExpression("{NCase} + CASE WHEN {N} > 0 THEN {NCase} ELSE 0 END")] public int? NTwice { get; set; }
            /// <summary>A bool that can be null (a [SqlExpression]) under a lift: the `(bool?)` around a row operand (R9-1).</summary>
            [SqlExpression("{Active}")] public bool Computed { get; set; }
            [SubqueryAggregate(typeof(ChildRow), nameof(ChildRow.RowId), AggregateFunction.Sum, AggregateColumn = nameof(ChildRow.Amount))]
            public decimal ChildSum { get; set; }
            /// <summary>Task 8 (T8E2-4): a Sum declared nullable keeps the SQL NULL; an Avg over no rows is NULL.</summary>
            [SubqueryAggregate(typeof(ChildRow), nameof(ChildRow.RowId), AggregateFunction.Sum, AggregateColumn = nameof(ChildRow.Amount))]
            public decimal? ChildSumN { get; set; }
            [SubqueryAggregate(typeof(ChildRow), nameof(ChildRow.RowId), AggregateFunction.Avg, AggregateColumn = nameof(ChildRow.Amount))]
            public decimal ChildAvg { get; set; }
            [SubqueryAggregate(typeof(ChildRow), nameof(ChildRow.RowId), AggregateFunction.Count)] public int ChildCount { get; set; }
            [SubqueryAggregate(typeof(ChildRow), nameof(ChildRow.RowId), AggregateFunction.ConditionalCount, ConditionColumn = nameof(ChildRow.Amount), ConditionValue = "1")]
            public int ChildConditionalCount { get; set; }
        }

        /// <summary>Captured values with counted getters (AC-N13).</summary>
        public class Holder
        {
            public int Reads;
            private int _flips;
            public int Five { get { Reads++; return 5; } }
            public short ShortVal { get { Reads++; return 5; } }
            public int IntVal { get { Reads++; return 5; } }
            public Shade ColorVal { get { Reads++; return Shade.Green; } }
            public DateTime Dt { get { Reads++; return new DateTime(2020, 1, 1); } }
            public short? ShortNull { get { Reads++; return null; } }
            public int? NullInt { get { Reads++; return null; } }
            public bool True { get { Reads++; return true; } }
            public bool False { get { Reads++; return false; } }
            public int? Flip { get { Reads++; return _flips++ % 2 == 0 ? (int?)null : 5; } }
            public int? Throws { get { Reads++; throw new InvalidOperationException("the getter throws"); } }
            public bool? NullFlag { get { Reads++; return null; } }
            public bool? TrueFlag { get { Reads++; return true; } }
            public bool ThrowsFlag { get { Reads++; throw new InvalidOperationException("the getter throws"); } }
            public Points Ten => new Points { Value = 10 };
        }

        public static class Statics
        {
            public static int Seven => 7;
        }

        private static readonly Func<PropertyInfo, bool> IsMapped = p => p.GetCustomAttribute<NotMappedAttribute>() == null;

        /// <summary>The visitor's remote map: the remote and computed attributes.</summary>
        private static readonly IReadOnlyCollection<string> Remote = new[]
        {
            nameof(Row.ParentCode), nameof(Row.ParentKey), nameof(Row.DocK), nameof(Row.NPlusOne), nameof(Row.ChildSum),
            nameof(Row.NCase), nameof(Row.NCaseRef), nameof(Row.NIif), nameof(Row.NCoalesce), nameof(Row.NNullIf), nameof(Row.NQuoted),
            nameof(Row.NX), nameof(Row.CycleA), nameof(Row.CycleB), nameof(Row.NTwoCoalesces), nameof(Row.NTwoCases),
            nameof(Row.Case2), nameof(Row.Case2Bracket), nameof(Row.Case2Quoted), nameof(Row.Case2Backtick), nameof(Row.Case2Comment),
            nameof(Row.Case2LineComment), nameof(Row.DerivedCase), nameof(Row.NTwice), nameof(RowBase.BaseRef),
            nameof(Row.ChildCount), nameof(Row.ChildConditionalCount), nameof(Row.Computed), nameof(Row.ChildSumN), nameof(Row.ChildAvg)
        };

        private readonly int? v = 5;
        private readonly int? none = null;
        private readonly string a = "a";
        private readonly Shade? noColor = null;
        private readonly int?[] ids = { 1, 2 };
        private readonly int?[] idsWithNull = { 1, null };
        private readonly int[] intIds = { 1, 2 };

        private static Expression Rewrite(Expression<Func<Row, bool>> predicate, IDictionary<Expression, bool?> memo = null) =>
            Funcular.Data.Orm.Linq.NullSemantics.Rewrite(predicate, memo, IsMapped, Remote);

        private static Tuple<NullOperandKind, Expression> Classify<T>(Expression<Func<Row, T>> operand)
        {
            var result = Funcular.Data.Orm.Linq.NullSemantics.Classify(StripLambdaConvert(operand.Body, typeof(T)), operand.Parameters[0], IsMapped, Remote);
            return Tuple.Create(result.Kind, result.Witness);
        }

        // An Expression<Func<Row, object>> wraps a value type in a Convert to object: that cast isn't the operand's.
        private static Expression StripLambdaConvert(Expression body, Type declared) =>
            declared == typeof(object) && body.NodeType == ExpressionType.Convert ? ((UnaryExpression)body).Operand : body;

        private static readonly Row[] Rows =
        {
            new Row { Id = 1, N = 5, M = 5, Name = "a", Code = 5, NLong = 5, NPrice = 5m, E = Shade.Red, Flag = true, D = new DateTime(2020, 6, 1), Active = true },
            new Row { Id = 2, N = 5, M = null, Name = null, Code = 6, NLong = 5, NPrice = 5m, E = null, Flag = false, Flag2 = true, Other = true },
            new Row { Id = 3, N = null, M = 5, Name = "b", Code = 7, E = Shade.Green, Flag = null, Flag2 = false, D = new DateTime(2021, 1, 1), Active = true, Other = true },
            new Row { Id = 4, N = null, M = null, Name = null, Code = 8, Flag = null },
            new Row { Id = 5, N = 7, M = 8, Name = "a", Code = 5, NLong = 7, NPrice = 7m, E = Shade.Red, Flag = true, Flag2 = true, Active = true },
            new Row { Id = 6, N = 1, M = 1, Name = "ca", Code = 1, NLong = 1, NPrice = 1m, E = Shade.Green, Flag = false, Flag2 = false, Other = true },
        };

        /// <summary>The innermost operand under a chain of casts, and the chain's target types, outermost first.</summary>
        private static Expression StripConverts(Expression expression, List<Type> types)
        {
            while (expression.NodeType == ExpressionType.Convert || expression.NodeType == ExpressionType.ConvertChecked)
            {
                types.Add(expression.Type);
                expression = ((UnaryExpression)expression).Operand;
            }
            return expression;
        }

        /// <summary>Whether a tree still holds a member read rooted at a constant or a static member (a value not substituted).</summary>
        private static bool ReadsACapturedMember(Expression expression)
        {
            var finder = new CapturedMemberFinder();
            finder.Visit(expression);
            return finder.Found;
        }

        private sealed class CapturedMemberFinder : ExpressionVisitor
        {
            public bool Found { get; private set; }

            protected override Expression VisitMember(MemberExpression node)
            {
                var root = node.Expression;
                while (root is MemberExpression inner)
                    root = inner.Expression;
                if (root == null || root is ConstantExpression)
                    Found = true;
                return base.VisitMember(node);
            }
        }

        #endregion

        #region Rewrite: C#'s value is kept

        /// <summary>
        /// The rewrite is an equivalence in C#: compiled, the rewritten body gives the original's answer for every row
        /// (the compensations it adds, such as <c>|| x.N == null</c> after <c>x.N != 5</c>, change nothing in C#). What it
        /// changes is the SQL, which the harness proves.
        /// </summary>
        [TestMethod]
        public void Rewrite_KeepsCSharpsValue()
        {
            var c = new Holder();
            string noName = null;
            var predicates = new List<Expression<Func<Row, bool>>>
            {
                x => x.N != v, x => x.N == none, x => x.N != none, x => v != x.N, x => !(x.N == 5), x => !(x.N != 5),
                x => !(x.N > 5), x => !(x.N >= none), x => x.N > none, x => x.N == x.M, x => x.N != x.M, x => x.N != x.Id,
                x => !(x.N == x.M), x => !(x.N == 5 && x.M == 5), x => !(x.N == 5 || x.M == 5), x => (x.N == 5) == (x.M == 5),
                x => (x.N == 5) != (x.M == 5), x => x.Flag == (x.N > 5), x => x.Flag != (x.N > 5), x => (x.N != 5 ? 1 : 0) == 1,
                x => (x.Id > 2 ? x.N : x.M) != 5, x => ids.Contains(x.N), x => !ids.Contains(x.N), x => idsWithNull.Contains(x.N),
                x => !idsWithNull.Contains(x.N), x => x.Id == none, x => x.Id != none, x => (x.N > 3 ? x.M != 5 : x.Id == 2),
                x => !(x.N > 3 ? x.M != 5 : x.Id == 2), x => x.E != Shade.Red, x => x.E == noColor, x => x.NLong != c.IntVal,
                x => x.NPrice != c.IntVal, x => x.N != c.ShortVal, x => x.Flag != true, x => x.Flag == false,
                x => (x.N != 5 ? x.Id : 0) > 0, x => (x.Id > 2 ? (x.N != 5 ? 1 : 0) : 0) == 1, x => !(x.Id == 2 && intIds.Contains(x.Id)),
                x => x.N == 5 || x.N != 5, x => (x.N == 5) == (x.N == 5), x => !(x.N < x.N), x => !(x.N >= x.N),
                // Round 6: `!` of a column or of a captured bool, a side the fold knows, and a conditional's predicate branch.
                x => !x.Active == true, x => !x.Flag != true, x => !x.Flag == null, x => x.Flag == !x.Flag, x => !x.Flag != x.Flag,
                x => !(!x.Flag == true), x => !(x.Flag == !x.Flag), x => (x.N == 5) == !x.Active, x => !x.Active == (x.N != 5),
                x => !((x.N == 5) == !x.Active), x => (x.N == 5) == !c.True, x => x.Flag != (bool?)!c.True,
                x => (x.N == 5) == (x.Id > 3 ? x.Active : !x.Other), x => !(x.Id > 2 ? x.Active : false) == true,
                x => !(x.Id > 2 ? x.Flag : (bool?)true) != false, x => (x.Id > 2 ? (bool?)(x.N == 5) : x.Flag) != true,
                x => (x.Id > 2 ? x.N == 5 : false) == x.Active, x => !(x.N == 5) == x.Active, x => (x.Id > 2 ? x.N == 5 : x.Active) == (x.M == 5),
                x => !((x.Id > 2 ? (bool?)(x.N == 5) : x.Flag) == true), x => (x.Id > 2 ? !x.Flag : x.Flag) != true,
                x => (x.Id == x.Id) == c.NullFlag, x => (c.Five > 3 ? true : false) == (x.N == 5), x => !x.Active != x.Other,
                // Round 7: two distinct bool? columns with one `!` (both guards), and `!` over a lifted predicate.
                x => x.Flag == !x.Flag2, x => !x.Flag == x.Flag2, x => x.Flag != !x.Flag2, x => !(x.Flag == !x.Flag2),
                x => x.Flag != !(bool?)(x.N > 3), x => !(bool?)(x.N > 3) == null, x => (x.Id > 3 ? x.Flag : !(bool?)(x.N > 3)) != true,
                x => (x.Id > 3 ? x.Flag : !(bool?)(c.Five == 5)) != true, x => !((x.Id == x.Id) == c.NullFlag),
                x => (x.Id > 2 ? (bool?)(x.N != 5) : x.Flag) != true, x => (x.Id <= 2 ? x.Flag : !(x.N != 5 ? (bool?)false : x.Flag)) != true,
                // Round 8: casts back to bool over lifted forms.
                x => (bool)!(bool?)(x.N > 3), x => (bool)(bool?)!(x.N > 3), x => (bool)(bool?)(x.N > 3) == x.Active,
                x => !(bool)!(bool?)(x.N > 3), x => (bool)!(bool?)!(bool?)(x.N > 3) && x.Id > 1, x => !(x.Flag == !x.Flag),
                // A row operand the fold knows only once rewritten (its test folds) keeps its witness (R6-3, R8-3's root rule).
                x => (x.Id >= none ? x.Flag : (bool?)true) == x.Flag2, x => (x.Id >= none ? x.Flag : (bool?)true) != x.Flag2,
                // The self-check fuzz's four: an operand whose test the rewrite folds keeps its witness.
                x => ((x.Code <= x.Code) ? c.NullFlag : (bool?)c.True) != x.Flag, x => ((x.M <= none) ? (bool?)c.True : c.NullFlag) == !x.Flag,
                x => c.NullFlag != ((none <= x.M) ? c.NullFlag : c.NullFlag),
                x => !((x.N.ToString() == noName) ? (bool?)c.True : c.NullFlag) == (c.True ? x.Flag : !x.Flag),
                // Round 9: a witness under `!`s or casts that the fold knows once rewritten (R9-1); a cast back to bool over a
                // conditional of lifts, and a cast through object (R9-2).
                x => !!(x.Id >= none ? x.Flag : (bool?)true) != true, x => !x.Active != (bool)(x.Id >= none ? x.Flag : (bool?)true),
                x => (bool?)(x.Id >= none ? x.Computed : true) != x.Flag, x => (x.Id > 2 ? (x.Id >= none ? x.Flag : (bool?)true) : x.Flag2) != true,
                x => (bool)(x.N != 5 ? (bool?)x.Active : (bool?)false), x => (bool)!(x.N != 5 ? (bool?)x.Active : (bool?)x.Other),
                x => (bool)(x.Id > 2 ? (bool?)(x.N != 5) : (bool?)x.Active), x => (bool?)(object)!(x.N > 3) == true,
                x => (bool)(object)(x.N != 5), x => null == !(bool?)(object)!x.Flag, x => (bool)(object)(bool?)(x.N > 3) == x.Active,
                x => (bool)(x.Id > 2 ? (x.N != 5 ? (bool?)x.Active : (bool?)x.Other) : (bool?)x.Active) == (x.M == 5),
                // Round 10: a cast back to bool over a conditional with a branch that isn't a lift (R10-1); a witness whose
                // only branch that can be null folds (R10-2); a cast to a non-nullable type compared with null.
                x => (bool)(x.N != 5 ? (bool?)x.Active : c.TrueFlag), x => !(bool)(x.N != 5 ? (bool?)x.Active : c.TrueFlag),
                x => x.Id > 2 && (bool)(x.N != 5 ? (bool?)x.Active : c.TrueFlag), x => ((bool)(x.N != 5 ? (bool?)x.Active : c.TrueFlag) ? x.Id : 0) > 0,
                x => x.Flag != (x.Active ? (x.Id >= none ? x.Computed : true) : x.Other),
                x => !x.Other != (x.Active ? (x.Id >= none ? x.Computed : true) : x.Other),
                x => !(x.Flag == (x.Active ? (x.Id >= none ? x.Computed : true) : x.Other)),
                x => (x.Active ? (x.Id >= none ? x.Computed : true) : x.Other) != true,
                x => (x.Id > 2 ? (x.Id >= none ? x.Computed : true) : x.Active) != (x.Id > 3 ? x.Computed : x.Other),
                x => (x.Id > 2 ? (x.Id >= none ? x.Computed : true) : x.Active) == (x.Id > 3 ? x.Computed : x.Other),
#pragma warning disable CS0472 // the cast's value is never null: the shape is the subject
                x => (bool?)(bool)!(c.False ? c.NullFlag : (bool?)!x.Other) == null,
#pragma warning restore CS0472
                // Round 11 (R11-5): AC-N14's two-valued sides, and compensations over a conditional (since rev 13 EF's form
                // over a flat one, distributed over a nested one), each C#'s answer too (the visitor reads a part that
                // reads no row in C#).
                x => (x.Id > 2 ? x.Flag : c.NullFlag) != x.Other, x => !(x.Id > 2 ? x.Flag : c.NullFlag) != x.Other,
                x => x.Other != !(x.Id > 2 ? x.Flag : c.NullFlag), x => !x.Other != (x.Id > 2 ? x.Flag : c.NullFlag),
                x => !(!(x.Id > 2 ? x.Flag : c.NullFlag) != x.Other), x => !((x.Id > 2 ? x.N : x.M) > 5),
                x => !((x.Id > 2 ? x.N : none) >= x.M), x => (x.Id > 2 ? x.N : x.M) != c.IntVal, x => !x.Flag == (x.N > 3),
                x => !x.Flag != (x.N > 3), x => (x.N > 3) == !x.Active, x => (x.Id > 2 ? x.Flag : c.NullFlag) == (x.N > 3),
                x => (x.Id > 2 ? x.Flag : c.NullFlag) != (x.N > 3), x => !(x.Id > 2 ? x.Flag : c.NullFlag) != (x.N > 3),
                x => (x.Id > 2 ? x.Flag : c.NullFlag) != x.Flag2, x => (x.Id > 2 ? x.Flag : c.NullFlag) == x.Flag2,
                x => !((x.Id >= none ? x.Flag : (bool?)true) != x.Other),
                // Round 12 (R12-2): a compensation over a nested conditional, distributed into its branches.
                x => ((x.Id > 2 ? x.N : x.M) != 5 ? x.N : x.M) != 5, x => ((x.Id > 2 ? x.N : x.M) != 5 ? x.N : x.M) != x.M,
                x => ((x.Id > 2 ? x.N : x.M) != 5 ? x.N : x.M) == x.M, x => !(((x.Id > 2 ? x.N : x.M) != 5 ? x.N : x.M) > 5),
                x => !((x.Id > 2 ? x.Flag : c.NullFlag) != x.Other ? x.Flag : c.NullFlag) != x.Other,
                x => ((x.Id > 2 ? x.N : x.M) != 5 ? x.N : c.NullInt) != ((x.Id > 3 ? x.M : x.N) != 5 ? x.M : x.N),
                x => x.M != ((x.Id > 2 ? x.N : x.M) != 5 ? x.N : x.M), x => !(x.M <= ((x.Id > 2 ? x.N : x.M) != 5 ? x.N : x.M)),
                x => ((x.Id > 2 ? x.Flag : c.NullFlag) != x.Other ? x.Flag : x.Flag2) != x.Flag2,
                x => ((x.Id > 2 ? x.N : x.M) != 5 ? (x.Id > 3 ? x.N : c.NullInt) : x.M) != 5,
            };
            foreach (var predicate in predicates)
            {
                var rewritten = Expression.Lambda<Func<Row, bool>>(Rewrite(predicate), predicate.Parameters).Compile();
                var original = predicate.Compile();
                foreach (var row in Rows)
                    Assert.AreEqual(original(row), rewritten(row), predicate + " on row " + row.Id);
            }
        }

        #endregion

        #region Rewrite: the structure is kept where nothing is compensated (AC-N4, AC-N11)

        [TestMethod]
        public void Rewrite_ReturnsTheOriginalNode_WhereNothingIsCompensated()
        {
            var predicates = new List<Expression<Func<Row, bool>>>
            {
                x => x.Id == 5, x => x.Id != 5, x => !(x.Id == 5), x => x.N == 5, x => x.N > 5, x => !(x.N != 5), x => !!(x.N == 5),
                x => !(x.Id == 2 && x.Code == 5), x => x.N == x.Code, x => ids.Contains(x.N), x => x.Name.Contains("a"),
                x => (x.N > 3 ? 1 : 0) != 1, x => !(x.Id == 2 && intIds.Contains(x.Id)), x => x.N == null, x => x.Name == "a",
                x => x.Child.Id != 5, x => x.Unmapped != 5, x => !(x.N == null),
            };
            foreach (var predicate in predicates)
                Assert.AreSame(predicate.Body, Rewrite(predicate), predicate.ToString());
        }

        [TestMethod]
        public void Rewrite_LeavesBitwiseAlone()
        {
            Expression<Func<Row, bool>> and = x => (x.N == 5) & (x.M == 5);
            Expression<Func<Row, bool>> notOr = x => !((x.N == 5) | (x.M == 5));
            Assert.AreSame(and.Body, Rewrite(and));
            Assert.AreSame(notOr.Body, Rewrite(notOr));
        }

        /// <summary>De Morgan applies only where an operand changed; a negated LIKE is a change (R4-1).</summary>
        [TestMethod]
        public void Rewrite_PushesNotDown_OnlyWhereAnOperandChanges()
        {
            Expression<Func<Row, bool>> predicate = x => !(x.Id == 2 && x.Name.Contains(a));
            var rewritten = Rewrite(predicate);
            Assert.AreEqual(ExpressionType.OrElse, rewritten.NodeType, rewritten.ToString());
            var or = (BinaryExpression)rewritten;
            Assert.AreEqual(ExpressionType.Not, or.Left.NodeType, "!(x.Id == 2): nothing to compensate, so NOT and the comparison");
            Assert.AreEqual(ExpressionType.Equal, ((UnaryExpression)or.Left).Operand.NodeType);
            Assert.AreEqual(ExpressionType.Not, or.Right.NodeType, "the call, negated, for the translator to compensate");
            Assert.AreEqual(ExpressionType.Call, ((UnaryExpression)or.Right).Operand.NodeType);
        }

        #endregion

        #region Rewrite: the compensations (AC-N1 to AC-N6, AC-N14)

        [TestMethod]
        public void Rewrite_NotEqual_AlsoSelectsTheNulls()
        {
            Expression<Func<Row, bool>> predicate = x => x.N != 5;
            var rewritten = (BinaryExpression)Rewrite(predicate);
            Assert.AreEqual(ExpressionType.OrElse, rewritten.NodeType);
            Assert.AreSame(predicate.Body, rewritten.Left, "the comparison is kept as it is");
            var isNull = (BinaryExpression)rewritten.Right;
            Assert.AreEqual(ExpressionType.Equal, isNull.NodeType);
            Assert.AreEqual(nameof(Row.N), ((MemberExpression)isNull.Left).Member.Name);
            Assert.IsNull(((ConstantExpression)isNull.Right).Value);
        }

        [TestMethod]
        public void Rewrite_BothCanBeNull_TakesEFsForms()
        {
            Expression<Func<Row, bool>> equal = x => x.N == x.M;
            var eq = Rewrite(equal);
            Assert.AreEqual("((x.N == x.M) OrElse ((x.N == null) AndAlso (x.M == null)))", eq.ToString());
            Expression<Func<Row, bool>> notEqual = x => x.N != x.M;
            Assert.AreEqual("((((x.N != x.M) OrElse (x.N == null)) OrElse (x.M == null)) AndAlso ((x.N != null) OrElse (x.M != null)))",
                Rewrite(notEqual).ToString());
        }

        [TestMethod]
        public void Rewrite_ARepeatedWitness_IsTestedOnce()
        {
            Expression<Func<Row, bool>> predicate = x => x.Name.Length == x.Name.Length;
            Assert.AreEqual("((x.Name.Length == x.Name.Length) OrElse (x.Name == null))", Rewrite(predicate).ToString());
        }

        [TestMethod]
        public void Rewrite_NegatedRelational_AlsoSelectsTheNulls_AndANullValueIsAConstant()
        {
            Expression<Func<Row, bool>> notGt = x => !(x.N > 5);
            var rewritten = (BinaryExpression)Rewrite(notGt);
            Assert.AreEqual(ExpressionType.OrElse, rewritten.NodeType, rewritten.ToString());
            Assert.AreEqual(ExpressionType.Not, rewritten.Left.NodeType);
            Assert.AreSame(((UnaryExpression)notGt.Body).Operand, ((UnaryExpression)rewritten.Left).Operand, "NOT and the comparison as it is");
            Assert.AreEqual("(x.N == null)", rewritten.Right.ToString());
            Expression<Func<Row, bool>> gtNone = x => x.N > none;
            Assert.AreEqual(false, ((ConstantExpression)Rewrite(gtNone)).Value);
            Expression<Func<Row, bool>> notGeNone = x => !(x.N >= none);
            Assert.AreEqual(true, ((ConstantExpression)Rewrite(notGeNone)).Value);
        }

        /// <summary>
        /// AC-N14: each predicate is its two-valued value, <c>p ? true : false</c> (EF's CASE WHEN p THEN 1 ELSE 0 END),
        /// sent once, and the values are compared (R11-5: rev 12's <c>(p &amp;&amp; q) || (!p &amp;&amp; !q)</c> sent each
        /// twice, so nested comparisons grew exponentially).
        /// </summary>
        [TestMethod]
        public void Rewrite_ABoolComparedWithAPredicate_IsTwoValuedTruths()
        {
            Expression<Func<Row, bool>> both = x => (x.N == 5) == (x.M == 5);
            var compared = Rewrite(both);
            Assert.AreEqual(ExpressionType.Equal, compared.NodeType, compared.ToString());
            foreach (var side in new[] { ((BinaryExpression)compared).Left, ((BinaryExpression)compared).Right })
            {
                var value = side as ConditionalExpression;
                Assert.IsNotNull(value, "a predicate's two-valued value: " + compared);
                Assert.AreEqual(true, ((ConstantExpression)value.IfTrue).Value, compared.ToString());
                Assert.AreEqual(false, ((ConstantExpression)value.IfFalse).Value, compared.ToString());
            }
            Expression<Func<Row, bool>> againstFalse = x => x.Name.Contains(a) == false;
            var negated = Rewrite(againstFalse);
            Assert.AreEqual(ExpressionType.Not, negated.NodeType, "p == false is !p: " + negated);
#pragma warning disable CS0472 // a bool is never null: the shape is the subject
            Expression<Func<Row, bool>> againstNull = x => x.Name.Contains(a) != (bool?)null;
#pragma warning restore CS0472
            Assert.AreEqual(true, ((ConstantExpression)Rewrite(againstNull)).Value, "a predicate is never null");
        }

        [TestMethod]
        public void Rewrite_ConditionalTests_AreRewritten_WhereverTheConditionalIs()
        {
            Expression<Func<Row, bool>> relational = x => (x.N != 5 ? x.Id : 0) > 0;
            var test = ((ConditionalExpression)((BinaryExpression)relational.Body).Left).Test;
            var rewrittenTest = ((ConditionalExpression)((BinaryExpression)Rewrite(relational)).Left).Test;
            AssertCompensated(test, rewrittenTest, "a positive relational comparison around the conditional (R4-2)");

            Expression<Func<Row, bool>> nested = x => (x.Id > 2 ? (x.N != 5 ? 1 : 0) : 0) == 1;
            var inner = (ConditionalExpression)((ConditionalExpression)((BinaryExpression)nested.Body).Left).IfTrue;
            var rewrittenInner = (ConditionalExpression)((ConditionalExpression)((BinaryExpression)Rewrite(nested)).Left).IfTrue;
            AssertCompensated(inner.Test, rewrittenInner.Test, "a conditional nested in a value branch (R4-2)");
        }

        private static void AssertCompensated(Expression test, Expression rewritten, string context)
        {
            Assert.AreEqual(ExpressionType.OrElse, rewritten.NodeType, context + ": " + rewritten);
            Assert.AreSame(test, ((BinaryExpression)rewritten).Left, context + ": the test as it is, then OR its operand IS NULL");
            Assert.AreEqual("(x.N == null)", ((BinaryExpression)rewritten).Right.ToString(), context);
        }

        #endregion

        #region Substitution and reads (AC-N2, AC-N13; R3-2, R3-5)

        /// <summary>
        /// A non-null value replaces the innermost captured member with a constant of the member's type, and the cast
        /// chain is kept, so the visitors bind it as 3.11.0 bound the member.
        /// </summary>
        [TestMethod]
        public void Substitution_KeepsTheConvertChain()
        {
            var c = new Holder();
            AssertSubstituted(x => x.N != c.ShortVal, typeof(short), (short)5);
            AssertSubstituted(x => x.NLong != c.IntVal, typeof(int), 5);
            AssertSubstituted(x => x.NPrice != c.IntVal, typeof(int), 5);
            AssertSubstituted(x => x.E != c.ColorVal, typeof(Shade), Shade.Green);
            AssertSubstituted(x => x.Dto != c.Dt, typeof(DateTime), new DateTime(2020, 1, 1));
            AssertSubstituted(x => x.Code != c.ShortVal, typeof(short), (short)5);
        }

        private static void AssertSubstituted(Expression<Func<Row, bool>> predicate, Type memberType, object value)
        {
            var original = (BinaryExpression)predicate.Body;
            var rewritten = Rewrite(predicate);
            var comparison = (BinaryExpression)(rewritten.NodeType == ExpressionType.OrElse ? ((BinaryExpression)rewritten).Left : rewritten);
            var originalChain = new List<Type>();
            StripConverts(original.Right, originalChain);
            var chain = new List<Type>();
            var inner = StripConverts(comparison.Right, chain);
            CollectionAssert.AreEqual(originalChain, chain, predicate + ": the Convert chain changed");
            Assert.IsInstanceOfType(inner, typeof(ConstantExpression), predicate + ": the member wasn't substituted");
            Assert.AreEqual(memberType, inner.Type, predicate + ": the constant's type isn't the member's");
            Assert.AreEqual(value, ((ConstantExpression)inner).Value, predicate.ToString());
        }

        /// <summary>A null value replaces the whole operand, so the visitors' bare-null test sends IS NULL (R3-2).</summary>
        [TestMethod]
        public void NullValue_ReplacesTheWholeOperand()
        {
            var c = new Holder();
            Expression<Func<Row, bool>> shortNull = x => x.N == c.ShortNull;
            var rewritten = (BinaryExpression)Rewrite(shortNull);
            Assert.AreEqual(ExpressionType.Equal, rewritten.NodeType);
            Assert.IsInstanceOfType(rewritten.Right, typeof(ConstantExpression), "the whole operand is the null, not its member");
            Assert.AreEqual(typeof(int?), rewritten.Right.Type);
            Assert.IsNull(((ConstantExpression)rewritten.Right).Value);

            Expression<Func<Row, bool>> colorNull = x => x.E == noColor;
            var color = (BinaryExpression)Rewrite(colorNull);
            Assert.IsInstanceOfType(color.Right, typeof(ConstantExpression));
            Assert.AreSame(((BinaryExpression)colorNull.Body).Left, color.Left, "the row side keeps its cast");

            Expression<Func<Row, bool>> cantBeNull = x => x.Id == c.NullInt;
            Assert.AreEqual(false, ((ConstantExpression)Rewrite(cantBeNull)).Value, "1=0 for a column that can't be null");
            Expression<Func<Row, bool>> cantBeNullNe = x => x.Id != c.NullInt;
            Assert.AreEqual(true, ((ConstantExpression)Rewrite(cantBeNullNe)).Value);
        }

        [TestMethod]
        public void Reads_AValueOnce_AndOnlyWhenTheRowOperandIsAccepted()
        {
            var c = new Holder();
            Rewrite(x => x.N != c.Five);
            Assert.AreEqual(1, c.Reads, "x.N != c.Five");

            c = new Holder();
            Rewrite(x => x.Child.Id != c.Five);
            Assert.AreEqual(0, c.Reads, "a nested receiver isn't accepted: nothing is read");

            c = new Holder();
            Rewrite(x => x.Id != c.Five);
            Assert.AreEqual(1, c.Reads, "read, and substituted, though no rule changes the comparison");
        }

        [TestMethod]
        public void Expansion_SubstitutesBeforeDuplicating()
        {
            var c = new Holder();
            var expanded = Rewrite(x => (x.N == c.Five) == (x.M == 5));
            Assert.AreEqual(1, c.Reads, "(x.N == c.Five) == (x.M == 5)");
            Assert.IsFalse(ReadsACapturedMember(expanded), "a captured member is left to read again: " + expanded);

            // EF's forms for two operands that can both be null repeat each (§3): substituted first.
            c = new Holder();
            var witness = Rewrite(x => (x.Id > 2 ? x.N : c.Flip) != x.M);
            Assert.AreEqual(1, c.Reads, "(x.Id > 2 ? x.N : c.Flip) != x.M");
            Assert.IsFalse(ReadsACapturedMember(witness), "the conditional is substituted before it is repeated: " + witness);
            c = new Holder();
            var flat = Rewrite(x => (x.Id > 2 ? x.N : c.Flip) != 5);
            Assert.AreEqual(1, c.Reads, "(x.Id > 2 ? x.N : c.Flip) != 5");
            Assert.IsFalse(ReadsACapturedMember(flat), "the flat conditional, repeated for its IS NULL as EF repeats it, substituted first: " + flat);

            // A predicate side is sent once (R11-5), so a method's receiver and arguments are left for the visitor to read
            // once, as 3.11.0 read them; a nested lambda is left as it is (its parameter isn't the row's).
            var call = Rewrite(x => x.Name.Contains(a) == (x.M == 5));
            var captured = new NodeFinder(e => e is MemberExpression member && member.Member.Name == nameof(a));
            captured.Visit(call);
            Assert.AreEqual(1, captured.Found.Count, "x.Name.Contains(a), sent once: " + call);
            // A repeated operand is substituted, but a nested lambda in it is left as it is.
            Expression<Func<Row, bool>> any = x => (intIds.Any(i => i == x.Id) ? x.N : x.M) != x.M;
            var lambda = ((MethodCallExpression)((ConditionalExpression)((BinaryExpression)any.Body).Left).Test).Arguments[1];
            var rewritten = Rewrite(any);
            var finder = new NodeFinder(e => ReferenceEquals(e, lambda));
            finder.Visit(rewritten);
            Assert.AreEqual(3, finder.Found.Count, "the nested lambda is the original node, in each copy: " + rewritten);
            Assert.IsFalse(ReadsACapturedMember(rewritten), "intIds, substituted: " + rewritten);
        }

        [TestMethod]
        public void Rewrite_FoldedPartsStayConstants()
        {
            var c = new Holder();
            var memo = new Dictionary<Expression, bool?>();
            var rewritten = Rewrite(x => x.N != 5 && c.True, memo);
            Assert.AreEqual(ExpressionType.AndAlso, rewritten.NodeType);
            var right = ((BinaryExpression)rewritten).Right as ConstantExpression;
            Assert.IsNotNull(right, "the known part is a constant: " + rewritten);
            Assert.AreEqual(true, right.Value);
            Assert.AreEqual(1, c.Reads, "the fold reads the captured bool once");
            Assert.IsTrue(memo.Count > 0, "the fold records into the visitor's memo");

            var whole = Rewrite(x => c.True || x.N != 5);
            Assert.AreEqual(true, ((ConstantExpression)whole).Value, "a body that folds is the constant");
        }

        #endregion

        #region What the rewrite leaves as it is, and the rarer compensations

        /// <summary>
        /// A comparison with an operator method declared outside the core library, a value whose read throws, an unmapped
        /// `bool` against a predicate, and a negated call the translator doesn't compensate: each is the original node,
        /// so the visitor answers (or rejects) it as 3.11.0 did.
        /// </summary>
        [TestMethod]
        public void Rewrite_LeavesWhatItDoesntKnow()
        {
            var c = new Holder();
            var points = new Points { Value = 10 };
            var predicates = new List<Expression<Func<Row, bool>>>
            {
                x => x.Score == points, x => x.Score > points, x => x.N != c.Throws, x => x.N > c.Throws,
                x => x.UnmappedFlag == (x.N > 5), x => (x.N > 5) == x.UnmappedFlag, x => !(x.Id == 2 && x.Name.Equals("a")),
                x => x.Flag == !c.ThrowsFlag,
            };
            foreach (var predicate in predicates)
                Assert.AreSame(predicate.Body, Rewrite(predicate), predicate.ToString());
            Expression<Func<Row, bool>> notScore = x => !(x.Score == points);
            Assert.AreSame(((UnaryExpression)notScore.Body).Operand, ((UnaryExpression)Rewrite(notScore)).Operand, "NOT and the user's comparison");
        }

        /// <summary>
        /// R5-1: a `bool` conditional whose branches are values or columns, and `!` over a bare `bool` member, are values,
        /// not predicates, so a comparison with them is rule 6's (3.11.0's text), not AC-N14's expansion, which would
        /// move them into a truth position (a bare `bit`, invalid T-SQL there).
        /// </summary>
        [TestMethod]
        public void Rewrite_ABoolValue_StaysAValue()
        {
            var predicates = new List<Expression<Func<Row, bool>>>
            {
                x => (x.Id > 2 ? x.Active : false) == true, x => x.Active == (x.Id > 2 ? x.Active : false),
                x => !x.Active == true, x => !x.Active == x.Active,
                // R6-1, R6-4: under `!` too, and `!` of a column that can be null where nothing is compensated.
                x => !(x.Id > 2 ? x.Active : false) == true, x => !x.Flag == true, x => !x.Active != x.Other,
            };
            foreach (var predicate in predicates)
                Assert.AreSame(predicate.Body, Rewrite(predicate), predicate.ToString());
        }

        /// <summary>R5-2: a literal null against a conditional keeps the conditional's substituted test, so it is read once.</summary>
        [TestMethod]
        public void Reads_ALiteralNullAgainstAConditional_Once()
        {
            var c = new Holder();
            var rewritten = Rewrite(x => (x.Id > c.Five ? x.N : x.M) == null);
            Assert.AreEqual(1, c.Reads, "(x.Id > c.Five ? x.N : x.M) == null");
            Assert.IsFalse(ReadsACapturedMember(rewritten), "the visitor would read c.Five again: " + rewritten);
        }

        /// <summary>R5-3: a fold of a lifted comparison of captured bools reads each once, as the visitor did in 3.11.0.</summary>
        [TestMethod]
        public void Reads_AFoldedComparisonOfCapturedBools_Once()
        {
            var c = new Holder();
            Rewrite(x => (((bool?)c.True == c.NullFlag) ? x.Id : x.Code) > 5, new Dictionary<Expression, bool?>());
            Assert.AreEqual(2, c.Reads, "c.True and c.NullFlag, once each");
        }

        [TestMethod]
        public void Rewrite_TheSameWitnessUnderNotEqual_IsTestedOnce()
        {
            Expression<Func<Row, bool>> predicate = x => x.Name.Length != x.Name.Length;
            Assert.AreEqual("(((x.Name.Length != x.Name.Length) OrElse (x.Name == null)) AndAlso (x.Name != null))", Rewrite(predicate).ToString());
        }

        [TestMethod]
        public void Rewrite_NegatedRelationalOfTwoColumns_AlsoSelectsEachOnesNulls()
        {
            Expression<Func<Row, bool>> predicate = x => !(x.N > x.M);
            Assert.AreEqual("((Not((x.N > x.M)) OrElse (x.N == null)) OrElse (x.M == null))", Rewrite(predicate).ToString());
        }

        /// <summary>AC-N14's sides that are values: two values compare as C# does; a column against a side that folds is rule 6.</summary>
        [TestMethod]
        public void Rewrite_ABoolComparison_WithASideThatIsAValue()
        {
            var c = new Holder();
            // The fold knows the left side (a self-comparison) but not a null bool? (its truths can't hold null): C#'s lifted ==.
            Expression<Func<Row, bool>> bothValues = x => (x.Id == x.Id) == c.NullFlag;
            Assert.AreEqual(false, ((ConstantExpression)Rewrite(bothValues, new Dictionary<Expression, bool?>())).Value, "true == null is false in C#");
            Assert.AreEqual(1, c.Reads, "c.NullFlag: the fold's read is the one used");

            Expression<Func<Row, bool>> columnEq = x => x.Flag == (c.IntVal == 5);
            var eq = (BinaryExpression)Rewrite(columnEq);
            Assert.AreEqual(ExpressionType.Equal, eq.NodeType, eq.ToString());
            Assert.AreEqual(true, ((ConstantExpression)StripConverts(eq.Right, new List<Type>())).Value, "the folded side is its value, its cast kept");

            Expression<Func<Row, bool>> columnNe = x => x.Flag != (c.IntVal == 5);
            var ne = Rewrite(columnNe);
            Assert.AreEqual(ExpressionType.OrElse, ne.NodeType, "x.Flag != true also selects the NULL flags: " + ne);
        }

        [TestMethod]
        public void Expansion_ReadsAFoldedPartOnce_AndLeavesAThrowingReadToTheVisitor()
        {
            var c = new Holder();
            Rewrite(x => (x.N == 5 && c.True) == (x.M == 5), new Dictionary<Expression, bool?>());
            Assert.AreEqual(1, c.Reads, "the fold's read of c.True (in the visitor's memo) is the one used when the side is duplicated");

            c = new Holder();
            var expanded = Rewrite(x => (x.N == c.Throws) == (x.M == 5));
            Assert.IsTrue(ReadsACapturedMember(expanded), "a read that throws is left for the visitor to report: " + expanded);

            // In an operand that EF's forms repeat, substituted before it is repeated, too (the Substituter's own read).
            c = new Holder();
            var repeated = Rewrite(x => (x.Id > 2 ? x.N : c.Throws) != x.M);
            Assert.IsTrue(ReadsACapturedMember(repeated), "a repeated operand's read that throws is left for the visitor to report: " + repeated);
        }

        #endregion

        #region Round 6: `!` and conditionals in a bool comparison

        /// <summary>
        /// R6-2, R6-4: `!` of a column moves to the comparison (`!m == v` is `m == !v`, C#'s lifted `!`), so a compensated
        /// form has no NOT on a value and a null value tests the column; where nothing is compensated it is as written.
        /// </summary>
        [TestMethod]
        public void Rewrite_NotOfAColumn_MovesToTheComparison()
        {
            Expression<Func<Row, bool>> notNeTrue = x => !x.Flag != true;
            Assert.AreEqual("((x.Flag != False) OrElse (x.Flag == null))", Rewrite(notNeTrue).ToString());
            Expression<Func<Row, bool>> negated = x => !(!x.Flag == true);
            Assert.AreEqual("((x.Flag != False) OrElse (x.Flag == null))", Rewrite(negated).ToString());
            Expression<Func<Row, bool>> notEqNull = x => !x.Flag == null;
            Assert.AreEqual("(x.Flag == null)", Rewrite(notEqNull).ToString(), "the column's NULLs, not NOT flag IS NULL");
            Expression<Func<Row, bool>> eqNotSelf = x => x.Flag == !x.Flag;
            Assert.AreEqual("((x.Flag != x.Flag) OrElse (x.Flag == null))", Rewrite(eqNotSelf).ToString(), "EF's == with the comparison flipped");
            Expression<Func<Row, bool>> lifted = x => x.Flag != (bool?)!x.Active;
            Assert.AreEqual("((x.Flag == Convert(x.Active)) OrElse (x.Flag == null))", Text(Rewrite(lifted)), "under its lift");
            Expression<Func<Row, bool>> notNeSelf = x => !x.Flag != x.Flag;
            Assert.AreEqual("(((x.Flag == x.Flag) OrElse (x.Flag == null)) AndAlso (x.Flag != null))", Rewrite(notNeSelf).ToString());
            Expression<Func<Row, bool>> againstPredicate = x => (x.N == 5) == !x.Active;
            var expansion = Rewrite(againstPredicate);
            Assert.AreEqual("(x.Active == IIF((x.N == Convert(5)), False, True))", Text(expansion),
                "!m == p is m == !p, against p's two-valued value (AC-N14, R11-5)");
            Assert.IsFalse(ContainsNot(expansion, nameof(Row.Active)), "NOT on the column: " + expansion);
        }

        /// <summary>
        /// R7-1: `!` over a lifted predicate, `!(bool?)p`, is `!p`: a predicate (C#'s lifted `!` of a value that is never
        /// null), so it is AC-N14's, a side or a conditional's branch.
        /// </summary>
        [TestMethod]
        public void Rewrite_NotOfALiftedPredicate_IsAPredicate()
        {
            Assert.AreEqual(false, ((ConstantExpression)Rewrite(x => !(bool?)(x.N > 3) == null)).Value, "a predicate is never null");
            var expansion = Rewrite(x => x.Flag != !(bool?)(x.N > 3));
            Assert.AreEqual(ExpressionType.OrElse, expansion.NodeType, "rule 6's != against the predicate's two-valued value: " + expansion);
            var value = new NodeFinder(e => e is ConditionalExpression k && k.Test.NodeType == ExpressionType.OrElse);
            value.Visit(expansion);
            Assert.AreEqual(1, value.Found.Count, "!(x.N > 3), compensated (true where n is NULL), as its two-valued value: " + expansion);
            var branch = Rewrite(x => (x.Id > 3 ? x.Flag : !(bool?)(x.N > 3)) != true);
            var finder = new NodeFinder(e => e is ConditionalExpression c && c.Type == typeof(bool));
            finder.Visit(branch);
            Assert.IsTrue(finder.Found.Count > 0, "the branch as its two-valued truth: " + branch);
        }

        /// <summary>
        /// R8-1: a cast back to `bool` over a lifted form that is never null is the form itself, wherever it is: the
        /// predicate, a side, a branch. `!(bool?)p` is first `(bool?)!p`, so every lift is outermost.
        /// </summary>
        [TestMethod]
        public void Rewrite_ACastBackToBool_IsTheFormItself()
        {
            var negated = Rewrite(x => (bool)!(bool?)(x.N > 3));
            Assert.AreEqual(ExpressionType.OrElse, negated.NodeType, "!(x.N > 3), compensated: " + negated);
            var side = Rewrite(x => (bool)(bool?)(x.N > 3) == x.Active);
            Assert.AreEqual("(x.Active == IIF((x.N > Convert(3)), True, False))", Text(side), "AC-N14: Active against the predicate's two-valued value");
            var c = new Holder();
            var always = Rewrite(x => (bool)!(bool?)(x.N > none && c.True), new Dictionary<Expression, bool?>());
            Assert.AreEqual(true, WherePredicateGuard.Fold(always, typeof(Row)), "!(x.N > null && …) is true for every row: " + always);
        }

        /// <summary>
        /// R9-2: a cast back to `bool` over a `bool?` conditional whose branches are lifts, or constants that aren't null,
        /// is the `bool` conditional, through `!` and nested conditionals. It is in a truth position, so its test is
        /// rewritten (R4-2) and its predicate branches are compensated; a branch that can be null leaves it a leaf.
        /// </summary>
        [TestMethod]
        public void Rewrite_ACastBackToBool_OverAConditionalOfLifts_IsTheConditional()
        {
            var plain = Rewrite(x => (bool)(x.N != 5 ? (bool?)x.Active : (bool?)x.Other));
            Assert.AreEqual(ExpressionType.Conditional, plain.NodeType, "the bool conditional: " + plain);
            Assert.AreEqual(typeof(bool), plain.Type, plain.ToString());
            Assert.AreEqual(ExpressionType.OrElse, ((ConditionalExpression)plain).Test.NodeType, "its test, compensated: " + plain);

            var negated = Rewrite(x => (bool)!(x.N != 5 ? (bool?)x.Active : (bool?)x.Other));
            Assert.AreEqual(ExpressionType.Conditional, negated.NodeType, "the `!` pushed into the branches: " + negated);

            var branch = Rewrite(x => (bool)(x.Id > 2 ? (bool?)(x.N != 5) : (bool?)x.Active));
            Assert.AreEqual(ExpressionType.OrElse, ((ConditionalExpression)branch).IfTrue.NodeType, "a predicate branch, compensated: " + branch);

            var nested = (ConditionalExpression)Rewrite(x => (bool)(x.Id > 2 ? (x.N != 5 ? (bool?)x.Active : (bool?)x.Other) : (bool?)x.Active));
            Assert.AreEqual(ExpressionType.Conditional, nested.IfTrue.NodeType, "the nested conditional, unlifted: " + nested);
            Assert.AreEqual(ExpressionType.OrElse, ((ConditionalExpression)nested.IfTrue).Test.NodeType, "its test, compensated: " + nested);

            // A constant branch that isn't null, as a hand-built tree holds it.
            var row = Expression.Parameter(typeof(Row), "x");
            var test = Expression.NotEqual(Expression.Property(row, nameof(Row.N)), Expression.Constant(5, typeof(int?)));
            var built = Expression.Lambda<Func<Row, bool>>(Expression.Convert(Expression.Condition(test,
                Expression.Convert(Expression.Property(row, nameof(Row.Active)), typeof(bool?)), Expression.Constant(true, typeof(bool?))), typeof(bool)), row);
            var constant = Rewrite(built);
            Assert.AreEqual(ExpressionType.Conditional, constant.NodeType, constant.ToString());
            Assert.AreEqual(true, ((ConstantExpression)((ConditionalExpression)constant).IfFalse).Value, constant.ToString());

            // A branch that can be null leaves the cast (C# fails it there), over the conditional with its test rewritten (R10-1).
            var nullable = Rewrite(x => (bool)(x.N != 5 ? (bool?)x.Active : x.Flag));
            Assert.AreEqual(ExpressionType.Convert, nullable.NodeType, nullable.ToString());
            Assert.AreEqual(ExpressionType.OrElse, ((ConditionalExpression)((UnaryExpression)nullable).Operand).Test.NodeType, nullable.ToString());
        }

        /// <summary>
        /// R10-1: a cast back to `bool` over a conditional the cast pass leaves (a branch C# types as able to be null, such as
        /// a captured `bool?`) is a row operand in a truth position: its test is rewritten (R4-2) and its branches stay values,
        /// as the predicate, under `!`, under `&amp;&amp;`, or as a conditional's test.
        /// </summary>
        [TestMethod]
        public void Rewrite_ACastBackToBool_OverAConditionalItLeaves_IsARowOperand()
        {
            var c = new Holder();
            foreach (var predicate in new Expression<Func<Row, bool>>[]
                     {
                         x => (bool)(x.N != 5 ? (bool?)x.Active : c.TrueFlag),
                         x => !(bool)(x.N != 5 ? (bool?)x.Active : c.TrueFlag),
                         x => x.Id > 2 && (bool)(x.N != 5 ? (bool?)x.Active : c.TrueFlag),
                         x => ((bool)(x.N != 5 ? (bool?)x.Active : c.TrueFlag) ? x.Id : 0) > 0,
                     })
            {
                var rewritten = Rewrite(predicate, new Dictionary<Expression, bool?>());
                var finder = new NodeFinder(e => e is ConditionalExpression conditional && conditional.Type == typeof(bool?)
                                                 && conditional.Test.NodeType == ExpressionType.OrElse);
                finder.Visit(rewritten);
                Assert.IsTrue(finder.Found.Count > 0, predicate + ": the conditional's test, compensated: " + rewritten);
            }
        }

        /// <summary>
        /// R11-1: a cast keeps the operand's kind, as EF does, also to a type that can't hold null: a property can be NULL in
        /// SQL whatever its CLR type (a `[RemoteProperty] int`), so `(int)(int?)x.ParentCode` is the column's kind.
        /// </summary>
        [TestMethod]
        public void Classify_ACastKeepsTheOperandsKind()
        {
            AssertKind(Classify(x => (bool)x.Flag), NullOperandKind.CanBeNull, nameof(Row.Flag));
            AssertKind(Classify(x => (int)x.N), NullOperandKind.CanBeNull, nameof(Row.N));
            AssertKind(Classify(x => (int)(int?)x.ParentCode), NullOperandKind.CanBeNull, nameof(Row.ParentCode));
            AssertKind(Classify(x => (long?)x.N), NullOperandKind.CanBeNull, nameof(Row.N));
            AssertKind(Classify(x => (int)(int?)x.Id), NullOperandKind.CannotBeNull, null);
        }

        /// <summary>
        /// R11-1: NOT NULL is NULL, so a null test of an operand with an odd number of `!`s under its casts (`!` the moved
        /// `!`s didn't reach) tests the operand under them: SQL reads `NOT x IS NULL` as NOT (x IS NULL), the complement.
        /// An even number keeps its text: SQL reads it either way.
        /// </summary>
        [TestMethod]
        public void Rewrite_ANullTest_UnderAnOddNumberOfNots_TestsTheOperandUnderThem()
        {
            var c = new Holder();
#pragma warning disable CS0472 // the shape is the subject
            var odd = Rewrite(x => (bool?)(bool)!(c.False ? c.NullFlag : (bool?)!x.Other) == null, new Dictionary<Expression, bool?>());
            var even = Rewrite(x => (bool?)(bool)!!(c.False ? c.NullFlag : (bool?)!x.Other) == null, new Dictionary<Expression, bool?>());
#pragma warning restore CS0472
            Assert.AreEqual(ExpressionType.Equal, odd.NodeType, odd.ToString());
            Assert.IsInstanceOfType(StripConverts(((BinaryExpression)odd).Left, new List<Type>()), typeof(ConditionalExpression), "tested under the `!`: " + odd);
            Assert.AreEqual(ExpressionType.Not, StripConverts(((BinaryExpression)even).Left, new List<Type>()).NodeType, "an even number kept: " + even);
        }

        /// <summary>
        /// R10-2: a witness whose only branch that can be null folds once the test is rewritten (a `[SqlExpression]` bool
        /// behind a test with a null value) can't be null as sent: `!=` gains no `IS NULL` of it, and two row operands take
        /// the form for what can still be null.
        /// </summary>
        [TestMethod]
        public void Rewrite_AWitnessWhoseNullableBranchFolds_IsNotTestedForNull()
        {
            // Nor a distributed form, (t ? … : …) == true (R12-2): nothing is compensated.
            var isNullOfConditional = new Func<Expression, bool>(e => e.NodeType == ExpressionType.Equal && ((BinaryExpression)e).Right is ConstantExpression k
                                                                     && (k.Value == null && StripConverts(((BinaryExpression)e).Left, new List<Type>()) is ConditionalExpression
                                                                         || true.Equals(k.Value) && ((BinaryExpression)e).Left is ConditionalExpression));
            foreach (var predicate in new Expression<Func<Row, bool>>[]
                     {
                         x => (x.Active ? (x.Id >= none ? x.Computed : true) : x.Other) != true,
                         x => x.Flag != (x.Active ? (x.Id >= none ? x.Computed : true) : x.Other),
                         x => !(x.Flag == (x.Active ? (x.Id >= none ? x.Computed : true) : x.Other)),
                     })
            {
                var rewritten = Rewrite(predicate);
                var finder = new NodeFinder(isNullOfConditional);
                finder.Visit(rewritten);
                Assert.AreEqual(0, finder.Found.Count, predicate + ": no IS NULL of a conditional that can't be null: " + rewritten);
            }
        }

        /// <summary>
        /// R9-2: a cast through `object` between `bool` and `bool?` is the cast without it (a box, then an unbox), so what
        /// is under it reads as it does without the cast.
        /// </summary>
        [TestMethod]
        public void Rewrite_ACastThroughObject_IsTheCastWithoutIt()
        {
            var side = Rewrite(x => (bool?)(object)!(x.N > 3) == true);
            var boxed = new NodeFinder(e => e.NodeType == ExpressionType.Convert && e.Type == typeof(object));
            boxed.Visit(side);
            Assert.AreEqual(0, boxed.Found.Count, "no cast through object is left: " + side);
            Assert.AreEqual(ExpressionType.OrElse, side.NodeType, "!(x.N > 3), compensated: " + side);
            var lifted = Rewrite(x => (bool)(object)(bool?)(x.N != 5));
            Assert.AreEqual(ExpressionType.OrElse, lifted.NodeType, "x.N != 5, compensated: " + lifted);
            Assert.AreEqual("Convert(x.Flag)", Text(Rewrite(x => (bool)(object)x.Flag)), "a bool? unboxed as a bool keeps that cast");
        }

        /// <summary>
        /// R9-1: a compensated comparison keeps its row operand's witness when the witness is under `!`s or a lift and the
        /// fold knows it once rewritten (its test folds): the substitution keeps the chain down to the witness, which is
        /// tested IS NULL (a flat conditional takes EF's form, R12-2). (Under a cast back to `bool` the conditional is a row
        /// operand in a truth position, R10-1.)
        /// </summary>
        [TestMethod]
        public void Rewrite_AWitnessUnderNotsOrCasts_IsKept()
        {
            foreach (var predicate in new Expression<Func<Row, bool>>[]
                     {
                         x => !!(x.Id >= none ? x.Flag : (bool?)true) != true,
                         x => (bool?)!(x.Id >= none ? x.Computed : true) != x.Flag,
                         x => (bool?)(x.Id >= none ? x.Computed : true) != x.Flag,
                     })
            {
                var rewritten = Rewrite(predicate);
                var finder = new NodeFinder(e => e.NodeType == ExpressionType.Equal && ((BinaryExpression)e).Right is ConstantExpression k
                                                 && k.Value == null && StripConverts(((BinaryExpression)e).Left, new List<Type>()) is ConditionalExpression);
                finder.Visit(rewritten);
                Assert.IsTrue(finder.Found.Count > 0, predicate + ": the conditional, tested IS NULL: " + rewritten);
            }
        }

        /// <summary>
        /// R8-3: a captured value the fold never reaches (an `||` whose other operand is true) isn't read to be
        /// substituted, so a getter that throws isn't called, as 3.11.0 didn't call it.
        /// </summary>
        [TestMethod]
        public void Reads_NothingTheFoldSkips()
        {
            var c = new Holder();
            Rewrite(x => (x.M == 5) == (x.Active != !((x.N == c.Throws) || c.True)), new Dictionary<Expression, bool?>());
            Assert.AreEqual(1, c.Reads, "c.True only; c.Throws is under an || the fold knows");
        }

        /// <summary>
        /// R7-4: two values under negation, a conditional that reads no row through its false branch, and a predicate
        /// against a null `bool?` under negation.
        /// </summary>
        [TestMethod]
        public void Rewrite_ValuesUnderNegation_AndAConditionalsFalseBranch()
        {
            var c = new Holder();
            Assert.AreEqual(true, ((ConstantExpression)Rewrite(x => !((x.Id == x.Id) == c.NullFlag), new Dictionary<Expression, bool?>())).Value,
                "!(true == null) is true in C#");
            c = new Holder();
            Assert.AreEqual("(x.Flag == True)", Rewrite(x => x.Flag == (c.False ? c.NullFlag : (bool?)c.True), new Dictionary<Expression, bool?>()).ToString(),
                "the test is false: the false branch's value");
#pragma warning disable CS0472 // a bool is never null: the shape is the subject
            Assert.AreEqual(true, ((ConstantExpression)Rewrite(x => !(x.Name.Contains(a) == (bool?)null))).Value, "!(p == null) is true");
#pragma warning restore CS0472
        }

        /// <summary><paramref name="expression"/>'s text without a cast's target type, which .NET Framework doesn't print (R7-3).</summary>
        private static string Text(Expression expression) => Regex.Replace(expression.ToString(), @"Convert\(([^,()]+), [^()]+\)", "Convert($1)");

        /// <summary>Whether a tree holds `!` directly over the row's <paramref name="member"/>.</summary>
        private static bool ContainsNot(Expression expression, string member)
        {
            var finder = new NodeFinder(e => e.NodeType == ExpressionType.Not && ((UnaryExpression)e).Operand is MemberExpression m
                                                && m.Member.Name == member);
            finder.Visit(expression);
            return finder.Found.Count > 0;
        }

        private sealed class NodeFinder : ExpressionVisitor
        {
            private readonly Func<Expression, bool> _match;

            public NodeFinder(Func<Expression, bool> match)
            {
                _match = match;
            }

            public List<Expression> Found { get; } = new List<Expression>();

            public override Expression Visit(Expression node)
            {
                if (node != null && _match(node))
                    Found.Add(node);
                return base.Visit(node);
            }
        }

        /// <summary>R6-2: a side that reads no row (`!` of a captured bool, a conditional of captured values) is a value.</summary>
        [TestMethod]
        public void Rewrite_ASideThatReadsNoRow_IsAValue()
        {
            var c = new Holder();
            var notCaptured = (BinaryExpression)Rewrite(x => (x.N == 5) == !c.True, new Dictionary<Expression, bool?>());
            Assert.AreEqual(ExpressionType.OrElse, notCaptured.NodeType, "p == false is !p, compensated: " + notCaptured);
            Assert.AreEqual(ExpressionType.NotEqual, notCaptured.Left.NodeType, notCaptured.ToString());
            Assert.AreEqual("(x.N == null)", notCaptured.Right.ToString());
            Assert.AreEqual(1, c.Reads, "(x.N == 5) == !c.True");

            c = new Holder();
            var ne = (BinaryExpression)Rewrite(x => x.Flag != (bool?)!c.True, new Dictionary<Expression, bool?>());
            Assert.AreEqual(ExpressionType.OrElse, ne.NodeType, "x.Flag != false also selects the NULL flags: " + ne);
            var value = StripConverts(((BinaryExpression)ne.Left).Right, new List<Type>());
            Assert.AreEqual(false, ((ConstantExpression)value).Value, "!c.True, read as one value: " + ne);
            Assert.AreEqual(1, c.Reads, "x.Flag != (bool?)!c.True");

            c = new Holder();
            var conditional = Rewrite(x => (c.Five > 3 ? true : false) == (x.N == 5), new Dictionary<Expression, bool?>());
            Assert.AreEqual(1, c.Reads, "the test of a conditional that reads no row: once (3.11.0: twice)");
            Assert.AreEqual(ExpressionType.Equal, conditional.NodeType, "true == p is p: " + conditional);

            // A conditional of captured values that holds null: the fold can't know it, but it is one value, read through its test.
            c = new Holder();
            Assert.AreEqual("(x.Flag == null)", Rewrite(x => x.Flag == (c.True ? c.NullFlag : (bool?)c.True), new Dictionary<Expression, bool?>()).ToString());
            Assert.AreEqual(2, c.Reads, "c.True, then c.NullFlag: once each, and not c.True's other branch");
        }

        /// <summary>R6-3: a side whose truth the fold knows is that value; the other side is the only witness taken.</summary>
        [TestMethod]
        public void Rewrite_ASideTheFoldKnows_IsAValue()
        {
            string noName = null;
            var eq = (BinaryExpression)Rewrite(x => (x.Name.StartsWith(noName) ? (bool?)true : (bool?)true) == x.Flag, new Dictionary<Expression, bool?>());
            Assert.AreEqual(ExpressionType.Equal, eq.NodeType, eq.ToString());
            Assert.AreEqual(true, ((ConstantExpression)eq.Left).Value, "the conditional is its value");
            Assert.AreEqual("((True != x.Flag) OrElse (x.Flag == null))",
                Rewrite(x => (x.Name.StartsWith(noName) ? (bool?)true : (bool?)true) != x.Flag, new Dictionary<Expression, bool?>()).ToString());
        }

        /// <summary>
        /// R6-4, R6-6: a conditional is a value, never a predicate; a predicate branch in it is its two-valued truth,
        /// `p ? true : false` (EF's CASE WHEN p THEN 1 ELSE 0 END), with p rewritten positive.
        /// </summary>
        [TestMethod]
        public void Rewrite_APredicateBranch_IsItsTwoValuedTruth()
        {
            Expression<Func<Row, bool>> lifted = x => (x.Id > 2 ? (bool?)(x.N == 5) : x.Flag) != true;
            var p = ((UnaryExpression)((ConditionalExpression)((BinaryExpression)lifted.Body).Left).IfTrue).Operand;
            AssertTwoValued(Rewrite(lifted), p, "a bool? conditional's lifted predicate branch");

            Expression<Func<Row, bool>> plain = x => (x.Id > 2 ? x.N == 5 : false) == x.Active;
            var q = ((ConditionalExpression)((BinaryExpression)plain.Body).Left).IfTrue;
            var rewritten = Rewrite(plain);
            Assert.AreEqual(ExpressionType.Equal, rewritten.NodeType, "a column compared with a conditional, not AC-N14's expansion: " + rewritten);
            AssertTwoValued(rewritten, q, "a bool conditional's predicate branch");
        }

        private static void AssertTwoValued(Expression rewritten, Expression predicate, string context)
        {
            var finder = new NodeFinder(e => e is ConditionalExpression c && ReferenceEquals(c.Test, predicate));
            finder.Visit(rewritten);
            // Once, or twice where the conditional is also the witness.
            Assert.IsTrue(finder.Found.Count >= 1, context + ": no `p ? true : false`: " + rewritten);
            foreach (ConditionalExpression wrapped in finder.Found)
            {
                Assert.AreEqual(true, ((ConstantExpression)wrapped.IfTrue).Value, context);
                Assert.AreEqual(false, ((ConstantExpression)wrapped.IfFalse).Value, context);
            }
        }

        /// <summary>A branch whose truth the fold knows is that value, so the substitution reads nothing under it.</summary>
        [TestMethod]
        public void Reads_NothingUnderABranchTheFoldKnows()
        {
            var c = new Holder();
            var rewritten = Rewrite(x => (x.Id > 2 ? x.Flag : !(x.N > c.Five ? (bool?)true : (bool?)true)) != true, new Dictionary<Expression, bool?>());
            Assert.AreEqual(0, c.Reads, "c.Five is under a branch whose truth is known: " + rewritten);
            Assert.IsFalse(ReadsACapturedMember(rewritten), rewritten.ToString());
        }

        /// <summary>R6-5: a null bool? that the fold read inside a predicate side isn't read again by the substitution.</summary>
        [TestMethod]
        public void Reads_ANullBoolTheFoldRead_Once()
        {
            var c = new Holder();
            Rewrite(x => (1 != x.Code) == ((bool?)(x.Id == x.Id) != c.NullFlag), new Dictionary<Expression, bool?>());
            Assert.AreEqual(1, c.Reads, "(1 != x.Code) == ((bool?)(x.Id == x.Id) != c.NullFlag)");

            c = new Holder();
            Assert.AreEqual("(x.Flag == null)", Rewrite(x => x.Flag == !c.NullFlag, new Dictionary<Expression, bool?>()).ToString());
            Assert.AreEqual(1, c.Reads, "x.Flag == !c.NullFlag");
        }

        #endregion

        #region Rounds 11 and 12: the SQL grows linearly with the nesting, and its CASE nesting is 3.11.0's or one more (R11-5, R12-2)

        /// <summary>
        /// R11-5 and R12-2 (the owner's rulings, 2026-10-21): a comparison nested in another's operand doesn't multiply the
        /// SQL. AC-N14's sides are their two-valued values (EF's form); a compensation over a conditional repeats a flat one,
        /// as EF does, and distributes a nested one into its branches. For each form, the innermost operand is sent as
        /// many times eight levels down as four, at most three, and the rewrite keeps C#'s value. That includes two
        /// operands that can both be null (rev 12's Known issue, closed by R12-2). A nested conditional compared with a
        /// conditional takes EF's forms instead (R13-1), whose SQL grows as EF's does.
        /// </summary>
        [TestMethod]
        public void Rewrite_NestedComparisons_GrowLinearly()
        {
            var c = new Holder();
            var x = Expression.Parameter(typeof(Row), "x");
            Func<string, Expression> column = name => Expression.Property(x, name);
            Func<Expression, Expression> lift = e => Expression.Convert(e, typeof(bool?));
            Func<Expression, Expression> maybeNull = e => Expression.Condition(column(nameof(Row.Active)), lift(e),
                Expression.Property(Expression.Constant(c), nameof(Holder.NullFlag)));
            var levels = new List<Tuple<string, Func<Expression, Expression>>>
            {
                Tuple.Create<string, Func<Expression, Expression>>("(a ? (bool?)e : null) != true (step 5)",
                    e => Expression.NotEqual(maybeNull(e), lift(Expression.Constant(true)))),
                Tuple.Create<string, Func<Expression, Expression>>("x.Flag == e (AC-N14)", e => Expression.Equal(column(nameof(Row.Flag)), lift(e))),
                Tuple.Create<string, Func<Expression, Expression>>("(x.N > 3) == e (AC-N14)",
                    e => Expression.Equal(Expression.GreaterThan(column(nameof(Row.N)), Expression.Constant(3, typeof(int?))), e)),
                Tuple.Create<string, Func<Expression, Expression>>("e == !x.Active (AC-N14)", e => Expression.Equal(e, Expression.Not(column(nameof(Row.Active))))),
                Tuple.Create<string, Func<Expression, Expression>>("!x.Flag != e (AC-N14)",
                    e => Expression.NotEqual(Expression.Not(column(nameof(Row.Flag))), lift(e))),
                Tuple.Create<string, Func<Expression, Expression>>("(a ? (bool?)e : null) != x.Other (step 6)",
                    e => Expression.NotEqual(maybeNull(e), lift(column(nameof(Row.Other))))),
                Tuple.Create<string, Func<Expression, Expression>>("!(a ? (bool?)e : null) != x.Other (step 6)",
                    e => Expression.NotEqual(Expression.Not(maybeNull(e)), lift(column(nameof(Row.Other))))),
                Tuple.Create<string, Func<Expression, Expression>>("(a ? (bool?)e : null) != x.Flag (step 6, both can be null)",
                    e => Expression.NotEqual(maybeNull(e), column(nameof(Row.Flag)))),
                Tuple.Create<string, Func<Expression, Expression>>("(a ? (bool?)e : null) == x.Flag (step 6, both can be null)",
                    e => Expression.Equal(maybeNull(e), column(nameof(Row.Flag)))),
                Tuple.Create<string, Func<Expression, Expression>>("!((a ? x.N : null) > (e ? 1 : 2)) (rule 7)",
                    e => Expression.Not(Expression.GreaterThan(
                        Expression.Condition(column(nameof(Row.Active)), column(nameof(Row.N)), Expression.Property(Expression.Constant(c), nameof(Holder.NullInt))),
                        Expression.Convert(Expression.Condition(e, Expression.Constant(1), Expression.Constant(2)), typeof(int?))))),
                // (Two nested sides take EF's forms since rev 14, R13-1: Rewrite_ANestedConditionalAgainstAConditional_….)
            };
            foreach (var level in levels)
            {
                var counts = new List<int>();
                foreach (var depth in new[] { 4, 8 })
                {
                    Expression body = Expression.GreaterThan(column(nameof(Row.M)), Expression.Constant(3, typeof(int?)));
                    for (var i = 0; i < depth; i++)
                        body = level.Item2(body);
                    var predicate = Expression.Lambda<Func<Row, bool>>(body, x);
                    var rewritten = Rewrite(predicate);
                    var innermost = new NodeFinder(e => e is MemberExpression member && member.Member.Name == nameof(Row.M));
                    innermost.Visit(rewritten);
                    counts.Add(innermost.Found.Count);
                    var compiled = Expression.Lambda<Func<Row, bool>>(rewritten, x).Compile();
                    var original = predicate.Compile();
                    foreach (var row in Rows)
                        Assert.AreEqual(original(row), compiled(row), level.Item1 + ", " + depth + " levels, on row " + row.Id);
                }
                Assert.AreEqual(counts[0], counts[1], level.Item1 + ": x.M is sent " + counts[0] + " times four levels down, " + counts[1] + " eight down");
                // At most EF's three copies of a flat conditional.
                Assert.IsTrue(counts[1] <= 3, level.Item1 + ": x.M is sent " + counts[1] + " times");
            }
        }

        /// <summary>
        /// R12-2 and round 12's CASE-nesting finding: SQL Server nests at most 10 CASEs, and rev 12's single CASE around a
        /// compensation over a conditional doubled the nesting of conditionals compared within conditionals' tests, past
        /// that limit from 6 levels where 3.11.0 and EF ran them. Distributed, the CASE nesting is the source's: for
        /// conditionals nested in tests (<c>!=</c>, a negated <c>&gt;</c>, two that can be null) and in value branches, nine
        /// levels down, the rewrite nests nine conditionals, and keeps C#'s value. (A distributed branch as deep as its
        /// test nests one more, once: the next test.)
        /// </summary>
        [TestMethod]
        public void Rewrite_NestedConditionalComparisons_KeepTheirCaseNesting()
        {
            var x = Expression.Parameter(typeof(Row), "x");
            Func<string, Expression> column = name => Expression.Property(x, name);
            var five = Expression.Constant(5, typeof(int?));
            var levels = new List<Tuple<string, Func<Expression, int, Expression>>>
            {
                Tuple.Create<string, Func<Expression, int, Expression>>("(e ? x.N : x.M) != 5",
                    (e, k) => Expression.NotEqual(Expression.Condition(e, column(nameof(Row.N)), column(nameof(Row.M))), five)),
                Tuple.Create<string, Func<Expression, int, Expression>>("!((e ? x.N : x.M) > 5)",
                    (e, k) => Expression.Not(Expression.GreaterThan(Expression.Condition(e, column(nameof(Row.N)), column(nameof(Row.M))), five))),
                Tuple.Create<string, Func<Expression, int, Expression>>("(e ? x.N : x.M) != x.M (both can be null)",
                    (e, k) => Expression.NotEqual(Expression.Condition(e, column(nameof(Row.N)), column(nameof(Row.M))), column(nameof(Row.M)))),
            };
            foreach (var level in levels)
            {
                Expression body = Expression.GreaterThan(column(nameof(Row.Id)), Expression.Constant(2));
                for (var k = 1; k <= 9; k++)
                    body = level.Item2(body, k);
                AssertCaseNesting(Expression.Lambda<Func<Row, bool>>(body, x), 9, level.Item1);
            }
            // Conditionals nested in value branches: a flat conditional, repeated as EF repeats it.
            Expression value = column(nameof(Row.N));
            for (var k = 1; k <= 9; k++)
                value = Expression.Condition(Expression.GreaterThan(column(nameof(Row.Id)), Expression.Constant(k)), value, column(nameof(Row.M)));
            AssertCaseNesting(Expression.Lambda<Func<Row, bool>>(Expression.NotEqual(value, five), x), 9, "(x.Id > 9 ? (… ? x.N : x.M) : x.M) != 5");
        }

        [TestMethod]
        public void Rewrite_ADistributedBranchAsDeepAsItsTest_AddsOneCaseLevel()
        {
            // A distributed comparison's leaf is a two-valued CASE, so a branch as deep as the test nests one CASE more
            // than the source, once: level k's true branch is a flat conditional k deep.
            var x = Expression.Parameter(typeof(Row), "x");
            Func<string, Expression> column = name => Expression.Property(x, name);
            var five = Expression.Constant(5, typeof(int?));
            Func<int, Expression> flat = depth =>
            {
                Expression branch = column(nameof(Row.N));
                for (var j = 1; j <= depth; j++)
                    branch = Expression.Condition(Expression.GreaterThan(column(nameof(Row.Id)), Expression.Constant(j)), branch, column(nameof(Row.M)));
                return branch;
            };
            Expression body = Expression.GreaterThan(column(nameof(Row.Id)), Expression.Constant(2));
            for (var k = 1; k <= 8; k++)
            {
                body = Expression.NotEqual(Expression.Condition(body, flat(k), column(nameof(Row.M))), five);
                var predicate = Expression.Lambda<Func<Row, bool>>(body, x);
                AssertCaseNesting(predicate, CaseNesting(predicate) + (k == 1 ? 0 : 1), "level " + k + ": " + body);
            }
        }

        /// <summary>
        /// R13-1, R14-1 (AC-N17; the owner's rulings, 2026-10-21 and 2026-10-22: "EF's form there", then the bound-checked
        /// distribution): a nested conditional compared with an operand that holds a conditional at least as deep isn't
        /// distributed, since each branch would repeat that operand below the distributed levels, past the source's CASE
        /// nesting + 1. It takes EF's forms, which add no CASE level, so the CASE nesting is at most the source's + 1 at
        /// every call site: step 6 with one or two operands that can be null, <c>==</c>, rule 7, both orders, a
        /// conditional nested through its branches, two nested sides, and an operand just deep enough to pass the bound.
        /// </summary>
        [TestMethod]
        public void Rewrite_ANestedConditionalAgainstAConditional_TakesEFsForm()
        {
            var x = Expression.Parameter(typeof(Row), "x");
            Func<string, Expression> column = name => Expression.Property(x, name);
            var five = Expression.Constant(5, typeof(int?));
            Func<int, Expression> id = j => Expression.GreaterThan(column(nameof(Row.Id)), Expression.Constant(j));
            // A flat conditional `depth` deep through its false branches, over N and M, or over values that can't be null.
            Func<int, bool, Expression> flat = (depth, nullable) =>
            {
                Expression branch = nullable ? column(nameof(Row.M)) : Expression.Convert(column(nameof(Row.Code)), typeof(int?));
                for (var j = depth; j >= 1; j--)
                    branch = Expression.Condition(id(j), nullable ? column(nameof(Row.N)) : Expression.Convert(column(nameof(Row.Id)), typeof(int?)), branch);
                return branch;
            };
            // A conditional whose test compares a conditional: ((x.Id > 2 ? x.N : x.M) != 5 ? x.N : x.M).
            Func<int, Expression> nested = j => Expression.Condition(
                Expression.NotEqual(Expression.Condition(id(j), column(nameof(Row.N)), column(nameof(Row.M))), five), column(nameof(Row.N)), column(nameof(Row.M)));
            // Nested through its true branches, each level's test comparing a conditional.
            Func<int, int, Expression> branchNested = (levels, offset) =>
            {
                Expression branch = column(nameof(Row.N));
                for (var j = 1; j <= levels; j++)
                    branch = Expression.Condition(Expression.NotEqual(Expression.Condition(id(j + offset), column(nameof(Row.N)), column(nameof(Row.M))), five),
                        branch, column(nameof(Row.M)));
                return branch;
            };
            var shapes = new List<Tuple<string, Expression>>
            {
                Tuple.Create("step 6, both can be null: nested != flat(9)", (Expression)Expression.NotEqual(nested(2), flat(9, true))),
                Tuple.Create("step 6, the other order: flat(9) != nested", (Expression)Expression.NotEqual(flat(9, true), nested(2))),
                Tuple.Create("step 6, ==: nested == flat(9)", (Expression)Expression.Equal(nested(2), flat(9, true))),
                Tuple.Create("step 6, one can be null: nested != flat(9) over Id and Code", (Expression)Expression.NotEqual(nested(2), flat(9, false))),
                Tuple.Create("rule 7: !(nested > flat(9))", (Expression)Expression.Not(Expression.GreaterThan(nested(2), flat(9, true)))),
                Tuple.Create("rule 7, the other order: !(flat(9) > nested)", (Expression)Expression.Not(Expression.GreaterThan(flat(9, true), nested(2)))),
                Tuple.Create("nested through its branches: branchNested(3) != flat(7)", (Expression)Expression.NotEqual(branchNested(3, 0), flat(7, true))),
                Tuple.Create("two nested sides: branchNested(5) != branchNested(5)'", (Expression)Expression.NotEqual(branchNested(5, 0), branchNested(5, 1))),
                // R14-1: distributed, the 3-deep operand would sit one level below the test's: two CASEs past the source's 3.
                Tuple.Create("as deep as the bound allows: nested != flat(3)", (Expression)Expression.NotEqual(nested(2), flat(3, true))),
            };
            foreach (var shape in shapes)
            {
                var predicate = Expression.Lambda<Func<Row, bool>>(shape.Item2, x);
                var rewritten = Rewrite(predicate);
                Assert.IsTrue(CaseNesting(rewritten) <= CaseNesting(predicate) + 1,
                    shape.Item1 + ": " + CaseNesting(rewritten) + " CASEs nested against the source's " + CaseNesting(predicate) + ": " + rewritten);
                Assert.IsFalse(rewritten is BinaryExpression top && top.NodeType == ExpressionType.Equal && top.Left is ConditionalExpression
                    && top.Right is ConstantExpression,
                    shape.Item1 + ": distributed: " + rewritten);
                var compiled = Expression.Lambda<Func<Row, bool>>(rewritten, x).Compile();
                var original = predicate.Compile();
                foreach (var row in Rows)
                    Assert.AreEqual(original(row), compiled(row), shape.Item1 + " on row " + row.Id);
            }
        }

        /// <summary>
        /// R14-1, R14-2 (the owner's ruling, 2026-10-22: the bound-checked distribution): a nested conditional compared with
        /// a conditional shallower than itself is distributed, since that keeps the CASE nesting within the source's + 1,
        /// so a chain of such comparisons grows linearly (rev 14's EF forms grew threefold per level: 4,005 parameters
        /// six levels down). Each family, four and eight levels down, against flat conditionals over columns that can be
        /// null, against ones never NULL by value, against predicates, under rule 7, and with the nested side on the right
        /// (R14-2): the rewrite at eight is at most three times its size at four, nests at most one CASE more than the
        /// source, and keeps C#'s value.
        /// </summary>
        [TestMethod]
        public void Rewrite_NestedConditionalsAgainstConditionals_GrowLinearly()
        {
            var x = Expression.Parameter(typeof(Row), "x");
            Func<string, Expression> column = name => Expression.Property(x, name);
            Func<int, Expression> id = j => Expression.GreaterThan(column(nameof(Row.Id)), Expression.Constant(j));
            Func<Expression> n = () => column(nameof(Row.N));
            Func<Expression> m = () => column(nameof(Row.M));
            Func<Expression> code = () => Expression.Convert(column(nameof(Row.Code)), typeof(int?));
            var five = Expression.Constant(5, typeof(int?));
            Func<int, Expression> flat = j => Expression.Condition(id(j), m(), n());
            Func<int, Expression> neverNull = j => Expression.Condition(id(j), code(), five);
            Func<int, Expression> predicate = j => Expression.Convert(Expression.GreaterThan(n(), Expression.Constant(j, typeof(int?))), typeof(bool?));
            Func<int, Expression> shallowNested = j => Expression.Condition(Expression.NotEqual(neverNull(j), five), code(), five);
            // Each family: the level built over the one below it, then the comparison at the top.
            var families = new List<Tuple<string, Func<int, Expression>>>
            {
                Tuple.Create<string, Func<int, Expression>>("(L != flat(j)) ? N : M", k => Chain(k, n(), (l, j) => Expression.Condition(Expression.NotEqual(l, flat(j)), n(), m()), (l, j) => Expression.NotEqual(l, flat(j)))),
                Tuple.Create<string, Func<int, Expression>>("(L != C(j)) ? Code : 5, C never NULL", k => Chain(k, neverNull(0), (l, j) => Expression.Condition(Expression.NotEqual(l, neverNull(j)), code(), five), (l, j) => Expression.NotEqual(l, neverNull(j)))),
                Tuple.Create<string, Func<int, Expression>>("(L != (bool?)(N > j)) ? Flag : Flag2", k => Chain(k, Expression.Condition(id(1), column(nameof(Row.Flag)), column(nameof(Row.Flag2))),
                    (l, j) => Expression.Condition(Expression.NotEqual(l, predicate(j)), column(nameof(Row.Flag)), column(nameof(Row.Flag2))), (l, j) => Expression.NotEqual(l, predicate(j)))),
                Tuple.Create<string, Func<int, Expression>>("(flat(j) != L) ? N : M, nested on the right", k => Chain(k, n(), (l, j) => Expression.Condition(Expression.NotEqual(flat(j), l), n(), m()), (l, j) => Expression.NotEqual(flat(j), l))),
                Tuple.Create<string, Func<int, Expression>>("!(L > flat(j)) ? N : M, rule 7", k => Chain(k, n(), (l, j) => Expression.Condition(Expression.Not(Expression.GreaterThan(l, flat(j))), n(), m()), (l, j) => Expression.Not(Expression.GreaterThan(l, flat(j))))),
                Tuple.Create<string, Func<int, Expression>>("!(flat(j) > L) ? N : M, rule 7, nested on the right", k => Chain(k, n(), (l, j) => Expression.Condition(Expression.Not(Expression.GreaterThan(flat(j), l)), n(), m()), (l, j) => Expression.Not(Expression.GreaterThan(flat(j), l)))),
                Tuple.Create<string, Func<int, Expression>>("(M != L) ? N : M, against a column, nested on the right", k => Chain(k, n(), (l, j) => Expression.Condition(Expression.NotEqual(m(), l), n(), m()), (l, j) => Expression.NotEqual(m(), l))),
                Tuple.Create<string, Func<int, Expression>>("!(M > L) ? N : M, rule 7 against a column, nested on the right", k => Chain(k, n(), (l, j) => Expression.Condition(Expression.Not(Expression.GreaterThan(m(), l)), n(), m()), (l, j) => Expression.Not(Expression.GreaterThan(m(), l)))),
                // R15-1: two nested sides, the earlier level on the right of a shallow nested conditional S(j): the larger
                // is distributed first, so the leaves repeat S(j), not the chain.
                Tuple.Create<string, Func<int, Expression>>("(S(j) != L) ? Code : 5, S a shallow nested conditional", k => Chain(k, neverNull(0),
                    (l, j) => Expression.Condition(Expression.NotEqual(shallowNested(j), l), code(), five), (l, j) => Expression.NotEqual(shallowNested(j), l))),
                Tuple.Create<string, Func<int, Expression>>("(L != S(j)) ? Code : 5, the mirror", k => Chain(k, neverNull(0),
                    (l, j) => Expression.Condition(Expression.NotEqual(l, shallowNested(j)), code(), five), (l, j) => Expression.NotEqual(l, shallowNested(j)))),
                // R16-3: an [SqlExpression] alias without a level of its own in the innermost test (rev 16 refused every level
                // above it the distribution: 55,761 parameters eight down).
                Tuple.Create<string, Func<int, Expression>>("(flat(j) != L) ? N : M, NX in the innermost test", k => Chain(k,
                    Expression.Condition(Expression.NotEqual(Expression.Condition(id(1), column(nameof(Row.NX)), m()), five), n(), m()),
                    (l, j) => Expression.Condition(Expression.NotEqual(flat(j), l), n(), m()), (l, j) => Expression.NotEqual(flat(j), l))),
            };
            foreach (var family in families)
            {
                var sizes = new List<int>();
                var pairs = new List<Tuple<int, Expression<Func<Row, bool>>, Expression>>();
                foreach (var levels in new[] { 4, 8 })
                {
                    var source = Expression.Lambda<Func<Row, bool>>(family.Item2(levels), x);
                    var rewritten = Rewrite(source);
                    var nodes = new NodeFinder(e => true);
                    nodes.Visit(rewritten);
                    sizes.Add(nodes.Found.Count);
                    pairs.Add(Tuple.Create(levels, source, rewritten));
                }
                // The size first: a rewrite that multiplied can be too large for its compiled delegate's stack.
                Assert.IsTrue(sizes[1] <= 3 * sizes[0], family.Item1 + ": " + sizes[0] + " nodes four levels down, " + sizes[1] + " eight down");
                foreach (var pair in pairs)
                {
                    Assert.IsTrue(CaseNesting(pair.Item3) <= CaseNesting(pair.Item2) + 1,
                        family.Item1 + ", " + pair.Item1 + " levels: " + CaseNesting(pair.Item3) + " CASEs nested against the source's " + CaseNesting(pair.Item2));
                    var compiled = Expression.Lambda<Func<Row, bool>>(pair.Item3, x).Compile();
                    var original = pair.Item2.Compile();
                    foreach (var row in Rows)
                        Assert.AreEqual(original(row), compiled(row), family.Item1 + ", " + pair.Item1 + " levels, on row " + row.Id);
                }
            }
        }

        /// <summary>
        /// R15-3, R16-2, R16-3: an <c>[SqlExpression]</c>'s template is SQL the expression doesn't show, so the CASE count
        /// reads its levels as SQL Server counts them: a CASE, a COALESCE, an IIF or a NULLIF, in any provider's variant,
        /// through a <c>{Token}</c> that names another template, not inside a quoted string, and a reference cycle ends. A
        /// nested conditional nine CASEs deep against a flat one eight deep ending in a template with a level keeps EF's form
        /// (compared in the branches it would nest 11); ending in one without, it is distributed as over a column (10). Under
        /// rule 7 too; and an alias without a level in the nested side's test doesn't stop the distribution (rev 16's
        /// remote-property refusal did, and a chain of those multiplied).
        /// </summary>
        [TestMethod]
        public void Rewrite_AComputedPropertyOperand_CountsItsTemplatesLevels()
        {
            var x = Expression.Parameter(typeof(Row), "x");
            Func<string, Expression> column = name => Expression.Property(x, name);
            Expression test = Expression.GreaterThan(column(nameof(Row.Id)), Expression.Constant(2));
            for (var j = 0; j < 8; j++)
                test = Expression.NotEqual(Expression.Condition(test, column(nameof(Row.N)), column(nameof(Row.M))), Expression.Constant(5, typeof(int?)));
            var nested = Expression.Condition(test, column(nameof(Row.N)), column(nameof(Row.M)));
            Func<Expression, int, Expression> flatAt = (last, depth) =>
            {
                Expression body = Expression.Convert(last, typeof(int?));
                for (var j = depth; j >= 1; j--)
                    body = Expression.Condition(Expression.GreaterThan(column(nameof(Row.Id)), Expression.Constant(j)), column(nameof(Row.N)), body);
                return body;
            };
            Func<Expression, Expression> flat = last => flatAt(last, 8);
            var shapes = new List<Tuple<string, bool, Expression<Func<Row, bool>>>>();
            foreach (var kept in new[] { nameof(Row.NCase), nameof(Row.NCaseRef), nameof(Row.NIif), nameof(Row.NCoalesce), nameof(Row.NNullIf) })
                shapes.Add(Tuple.Create(kept, true, Expression.Lambda<Func<Row, bool>>(Expression.NotEqual(nested, flat(column(kept))), x)));
            foreach (var distributed in new[] { nameof(Row.NPlusOne), nameof(Row.NQuoted), nameof(Row.NX), nameof(Row.CycleA), nameof(Row.M) })
                shapes.Add(Tuple.Create(distributed, false, Expression.Lambda<Func<Row, bool>>(Expression.NotEqual(nested, flat(column(distributed))), x)));
            shapes.Add(Tuple.Create("rule 7, NCase", true, Expression.Lambda<Func<Row, bool>>(Expression.Not(Expression.GreaterThan(nested, flat(column(nameof(Row.NCase))))), x)));
            shapes.Add(Tuple.Create("rule 7, NPlusOne", false, Expression.Lambda<Func<Row, bool>>(Expression.Not(Expression.GreaterThan(nested, flat(column(nameof(Row.NPlusOne))))), x)));
            // Seven deep, one level fits (compared in the branches: 2 + 7 + 1, the source's 9 + 1) and two don't: siblings are
            // one level, so these are distributed.
            foreach (var oneLevel in new[] { nameof(Row.NCase), nameof(Row.NTwoCoalesces), nameof(Row.NTwoCases) })
                shapes.Add(Tuple.Create(oneLevel + ", 7 deep", false, Expression.Lambda<Func<Row, bool>>(Expression.NotEqual(nested, flatAt(column(oneLevel), 7)), x)));
            foreach (var twoLevels in new[] { nameof(Row.Case2), nameof(Row.Case2Bracket), nameof(Row.Case2Quoted), nameof(Row.Case2Backtick),
                         nameof(Row.Case2Comment), nameof(Row.Case2LineComment), nameof(RowBase.BaseRef), nameof(Row.NTwice) })
                shapes.Add(Tuple.Create(twoLevels + ", 7 deep", true, Expression.Lambda<Func<Row, bool>>(Expression.NotEqual(nested, flatAt(column(twoLevels), 7)), x)));
            Expression<Func<Row, bool>> aliasInTest = r => ((r.Id > 2 ? r.NX : r.M) != 5 ? r.N : r.M) != (r.Id > 4 ? r.M : r.N);
            shapes.Add(Tuple.Create("an alias without a level in the test", false, aliasInTest));
            foreach (var shape in shapes)
            {
                var rewritten = Rewrite(shape.Item3);
                var isDistributed = rewritten is BinaryExpression top && top.NodeType == ExpressionType.Equal && top.Left is ConditionalExpression
                                    && top.Right is ConstantExpression;
                Assert.AreEqual(!shape.Item2, isDistributed, shape.Item1 + (shape.Item2 ? ": distributed: " : ": not distributed: ") + rewritten);
                var original = shape.Item3.Compile();
                var compiled = Expression.Lambda<Func<Row, bool>>(rewritten, shape.Item3.Parameters).Compile();
                foreach (var row in Rows)
                    Assert.AreEqual(original(row), compiled(row), shape.Item1 + " on row " + row.Id);
            }
        }

        /// <summary>
        /// A nullable integer's <c>ToString()</c> is <c>COALESCE(CAST(…), '')</c> (R4-10), which SQL Server counts as a CASE
        /// (as it counts IIF and NULLIF; Msg 125). A nested conditional nine CASEs deep against a flat one eight deep ending
        /// in it: compared in the branches, the flat one would sit below the leaf's CASE, its COALESCE eleven levels down, so
        /// EF's form is kept; a non-nullable integer's is a bare CAST, and that shape is distributed (ten CASEs).
        /// </summary>
        [TestMethod]
        public void Rewrite_ANullableIntegersToString_CountsAsACase()
        {
            var x = Expression.Parameter(typeof(Row), "x");
            Func<string, Expression> column = name => Expression.Property(x, name);
            Expression test = Expression.GreaterThan(column(nameof(Row.Id)), Expression.Constant(2));
            for (var j = 0; j < 8; j++)
                test = Expression.NotEqual(Expression.Condition(test, column(nameof(Row.N)), column(nameof(Row.M))), Expression.Constant(5, typeof(int?)));
            var nested = Expression.Condition(test, column(nameof(Row.Name)), Expression.Constant("a"));
            Func<string, Type, int, Expression<Func<Row, bool>>> againstAt = (name, type, depth) =>
            {
                Expression flat = Expression.Call(column(name), type.GetMethod(nameof(ToString), Type.EmptyTypes));
                for (var j = depth; j >= 1; j--)
                    flat = Expression.Condition(Expression.GreaterThan(column(nameof(Row.Id)), Expression.Constant(j)), column(nameof(Row.Name)), flat);
                return Expression.Lambda<Func<Row, bool>>(Expression.NotEqual(nested, flat), x);
            };
            Func<string, Type, Expression<Func<Row, bool>>> against = (name, type) => againstAt(name, type, 8);
            // The COALESCE holds its operand's SQL: a computed integer's template levels sit inside it. Seven deep, the CASE
            // template's ToString() would nest 11 compared in the branches (2 + 7 + COALESCE + CASE) and keeps EF's form; a
            // column's, 10, is distributed.
            var templateInside = againstAt(nameof(Row.NCase), typeof(int?), 7);
            var columnInside = againstAt(nameof(Row.N), typeof(int?), 7);
            foreach (var pair in new[] { Tuple.Create("NCase.ToString(), 7 deep", templateInside, false), Tuple.Create("N.ToString(), 7 deep", columnInside, true) })
            {
                var rewritten = Rewrite(pair.Item2);
                Assert.AreEqual(pair.Item3, rewritten is BinaryExpression t && t.NodeType == ExpressionType.Equal && t.Left is ConditionalExpression
                    && t.Right is ConstantExpression, pair.Item1 + ": " + rewritten);
                var original = pair.Item2.Compile();
                var compiled = Expression.Lambda<Func<Row, bool>>(rewritten, x).Compile();
                foreach (var row in Rows)
                    Assert.AreEqual(original(row), compiled(row), pair.Item1 + " on row " + row.Id);
            }
            var nullable = against(nameof(Row.N), typeof(int?));
            var notNull = against(nameof(Row.Id), typeof(int));
            var text = against(nameof(Row.Name), typeof(string));
            var kept = Rewrite(nullable);
            var distributed = Rewrite(notNull);
            var textDistributed = Rewrite(text);
            Assert.IsFalse(kept is BinaryExpression top && top.NodeType == ExpressionType.Equal && top.Left is ConditionalExpression
                && top.Right is ConstantExpression, "distributed against a nullable integer's ToString(): " + kept);
            Assert.IsTrue(distributed is BinaryExpression d && d.NodeType == ExpressionType.Equal && d.Left is ConditionalExpression
                && d.Right is ConstantExpression, "a non-nullable integer's ToString(), distributed: " + distributed);
            // R16-6: a string's ToString() is its column, no COALESCE.
            Assert.IsTrue(textDistributed is BinaryExpression s && s.NodeType == ExpressionType.Equal && s.Left is ConditionalExpression
                && s.Right is ConstantExpression, "a string's ToString(), distributed: " + textDistributed);
            foreach (var pair in new[] { Tuple.Create(nullable, kept), Tuple.Create(notNull, distributed), Tuple.Create(text, textDistributed) })
            {
                var original = pair.Item1.Compile();
                var compiled = Expression.Lambda<Func<Row, bool>>(pair.Item2, x).Compile();
                foreach (var row in Rows)
                    Assert.AreEqual(original(row), compiled(row), pair.Item1 + " on row " + row.Id);
            }
        }

        /// <summary><paramref name="levels"/> levels: <paramref name="first"/>, each next one <paramref name="level"/> of the one below, and <paramref name="top"/> of the last.</summary>
        private static Expression Chain(int levels, Expression first, Func<Expression, int, Expression> level, Func<Expression, int, Expression> top)
        {
            var chain = first;
            for (var j = 1; j < levels; j++)
                chain = level(chain, j);
            return top(chain, levels);
        }

        private void AssertCaseNesting(Expression<Func<Row, bool>> predicate, int nesting, string context)
        {
            var rewritten = Rewrite(predicate);
            Assert.AreEqual(nesting, CaseNesting(rewritten), context + ": the CASE nesting of " + rewritten);
            var compiled = Expression.Lambda<Func<Row, bool>>(rewritten, predicate.Parameters).Compile();
            var original = predicate.Compile();
            foreach (var row in Rows)
                Assert.AreEqual(original(row), compiled(row), context + " on row " + row.Id);
        }

        /// <summary>The most conditionals nested in <paramref name="expression"/>: each is a CASE in the SQL.</summary>
        private static int CaseNesting(Expression expression)
        {
            var counter = new CaseNestingCounter();
            counter.Visit(expression);
            return counter.Deepest;
        }

        private sealed class CaseNestingCounter : ExpressionVisitor
        {
            private int _depth;

            public int Deepest { get; private set; }

            protected override Expression VisitConditional(ConditionalExpression node)
            {
                _depth++;
                Deepest = Math.Max(Deepest, _depth);
                var visited = base.VisitConditional(node);
                _depth--;
                return visited;
            }
        }

        /// <summary>
        /// A compensation over a flat conditional (no conditional in its test, and only flat conditionals in its branches)
        /// takes EF's form, the conditional repeated for its IS NULL: a bounded copy that adds no CASE level (R12-2).
        /// </summary>
        [TestMethod]
        public void Rewrite_ACompensationOverAFlatConditional_TakesEFsForm()
        {
            var c = new Holder();
            foreach (var predicate in new Expression<Func<Row, bool>>[]
                     {
                         x => (x.Id > 2 ? x.N : x.M) != 5,
                         x => !((x.Id > 2 ? x.N : x.M) > 5),
                         x => (x.Id > 2 ? x.Flag : c.NullFlag) != x.Other,
                         x => !(x.Id > 2 ? x.Flag : c.NullFlag) != x.Other,
                         x => (x.Id > 2 ? (x.Id > 3 ? x.N : x.M) : x.N) != 5,
                     })
            {
                var rewritten = Rewrite(predicate);
                Assert.AreEqual(ExpressionType.OrElse, rewritten.NodeType, predicate + ": EF's form: " + rewritten);
                var isNull = new NodeFinder(e => e.NodeType == ExpressionType.Equal && ((BinaryExpression)e).Right is ConstantExpression k
                                                 && k.Value == null && StripConverts(((BinaryExpression)e).Left, new List<Type>()) is ConditionalExpression);
                isNull.Visit(rewritten);
                Assert.AreEqual(1, isNull.Found.Count, predicate + ": the conditional, tested IS NULL: " + rewritten);
            }
        }

        /// <summary>
        /// R12-2 (the owner's ruling, 2026-10-21): a compensation over a nested conditional (one that holds an earlier
        /// comparison over a conditional) distributes the comparison into its branches: <c>(t ? (a op v) : (b op v)) ==
        /// true</c>, each branch's comparison compensated as a column's is, as its two-valued value. The test, which holds
        /// the earlier level, is sent once; only the flat innermost conditional is repeated (EF's form there). Against a
        /// flat conditional too, whose distribution nests one CASE more than written, within the bound (R14-1).
        /// </summary>
        [TestMethod]
        public void Rewrite_ACompensationOverANestedConditional_IsDistributed()
        {
            var c = new Holder();
            foreach (var predicate in new Expression<Func<Row, bool>>[]
                     {
                         x => ((x.Id > 2 ? x.N : x.M) != 5 ? x.N : x.M) != 5,
                         x => ((x.Id > 2 ? x.N : x.M) != 5 ? x.N : x.M) != x.M,
                         x => ((x.Id > 2 ? x.N : x.M) != 5 ? x.N : x.M) == x.M,
                         x => !(((x.Id > 2 ? x.N : x.M) != 5 ? x.N : x.M) > 5),
                         x => !((x.Id > 2 ? x.Flag : c.NullFlag) != x.Other ? x.Flag : c.NullFlag) != x.Other,
                         // R14-1: against a flat conditional, one CASE past the comparison as written, within the bound
                         x => ((x.Id > 2 ? x.N : x.M) != 5 ? x.N : x.M) != (x.Id > 4 ? x.M : x.N),
                         x => !(((x.Id > 2 ? x.N : x.M) != 5 ? x.N : x.M) > (x.Id > 4 ? x.M : x.N)),
                         // A conditional inside a lambda is never a CASE, so it doesn't count against the bound.
                         x => !(((x.Id > 2 ? x.N : x.M) != 5 ? x.N : x.M) > (intIds.Any(i => i == (x.Id > 3 ? x.Id : 0)) ? (int?)x.Id : (int?)x.Code)),
                     })
            {
                var rewritten = Rewrite(predicate);
                Assert.AreEqual(ExpressionType.Equal, rewritten.NodeType, predicate + ": distributed: " + rewritten);
                Assert.AreEqual(true, (((BinaryExpression)rewritten).Right as ConstantExpression)?.Value, predicate + ": … == true: " + rewritten);
                var distributed = ((BinaryExpression)rewritten).Left as ConditionalExpression;
                Assert.IsNotNull(distributed, predicate + ": the conditional of the branches' comparisons: " + rewritten);
                foreach (var branch in new[] { distributed.IfTrue, distributed.IfFalse })
                    Assert.IsTrue(branch is ConstantExpression || (branch is ConditionalExpression value && value.IfTrue is ConstantExpression && value.IfFalse is ConstantExpression),
                        predicate + ": a branch's two-valued value: " + branch);
                var inner = new NodeFinder(e => e is ConditionalExpression k && k.Test.ToString() == "(x.Id > 2)");
                inner.Visit(rewritten);
                Assert.AreEqual(2, inner.Found.Count, predicate + ": the flat innermost conditional, repeated for its IS NULL only: " + rewritten);
                Assert.IsFalse(ContainsNot(rewritten, nameof(Row.Other)), predicate + ": NOT on the column: " + rewritten);
            }
        }

        /// <summary>
        /// R12-2, R14-1: a distributed comparison repeats the other operand in each branch, so it is substituted first
        /// (AC-N13), the one rule 7 doesn't test for NULL included, a conditional shallower than the nested side too (the
        /// bound-checked distribution). And a flat conditional left in a leaf is rewritten already, so its test isn't
        /// compensated again, on either side.
        /// </summary>
        [TestMethod]
        public void Distribution_ReadsTheOtherOperandOnce_AndDoesntRewriteALeafAgain()
        {
            // A collection Contains in the test of the operand that can't be null: RewriteOperand leaves it to be read.
            var relational = Rewrite(x => !(((x.Id > 2 ? x.N : x.M) != 5 ? x.N : x.M) > (intIds.Contains(x.Id) ? x.Id : x.Code)));
            Assert.IsFalse(ReadsACapturedMember(relational), "the visitor would read intIds in each branch: " + relational);

            var leaf = Rewrite(x => ((x.Id > 2 ? x.N : x.M) != 5 ? (x.N != 5 ? x.N : x.M) : x.M) != 5);
            var nNull = new NodeFinder(e => e.ToString() == "(x.N == null)");
            nNull.Visit(leaf);
            Assert.AreEqual(2, nNull.Found.Count, "the flat conditional's test, compensated once in each of its two copies: " + leaf);
            // The same with the nested conditional on the right, so the leaf's flat conditional is the right operand.
            var mirrored = Rewrite(x => 5 != ((x.Id > 2 ? x.N : x.M) != 5 ? (x.N != 5 ? x.N : x.M) : x.M));
            var nNullMirrored = new NodeFinder(e => e.ToString() == "(x.N == null)");
            nNullMirrored.Visit(mirrored);
            Assert.AreEqual(2, nNullMirrored.Found.Count, "the right operand's flat conditional, compensated once in each copy: " + mirrored);
        }

        #endregion

        #region Classify and CanBeNull (§4.1)

        [TestMethod]
        public void Classify_ByOperandKind()
        {
            var c = new Holder();
            AssertKind(Classify(x => x.N), NullOperandKind.CanBeNull, "N");
            AssertKind(Classify(x => x.Name), NullOperandKind.CanBeNull, "Name");
            AssertKind(Classify(x => x.Id), NullOperandKind.CannotBeNull, null);
            AssertKind(Classify(x => (int?)x.Id), NullOperandKind.CannotBeNull, null);
            AssertKind(Classify(x => (int?)x.E), NullOperandKind.CanBeNull, "E");
            AssertKind(Classify(x => x.ParentCode), NullOperandKind.CanBeNull, "ParentCode");
            AssertKind(Classify(x => x.ParentKey), NullOperandKind.CanBeNull, "ParentKey");
            AssertKind(Classify(x => x.NPlusOne), NullOperandKind.CanBeNull, "NPlusOne");
            // T8E2-4: a non-nullable Sum is COALESCE(SUM(…), 0), the 0 it reads back; a nullable one and an Avg can be NULL.
            AssertKind(Classify(x => x.ChildSum), NullOperandKind.CannotBeNull, null);
            AssertKind(Classify(x => x.ChildSumN), NullOperandKind.CanBeNull, "ChildSumN");
            AssertKind(Classify(x => x.ChildAvg), NullOperandKind.CanBeNull, "ChildAvg");
            AssertKind(Classify(x => x.ChildCount), NullOperandKind.CannotBeNull, null);
            AssertKind(Classify(x => x.D.Value.Year), NullOperandKind.CanBeNull, "D");
            AssertKind(Classify(x => x.Posted.Month), NullOperandKind.CannotBeNull, null);
            AssertKind(Classify(x => x.Name.Length), NullOperandKind.CanBeNull, "Name");
            AssertKind(Classify(x => x.Name.ToString()), NullOperandKind.CanBeNull, "Name");
            AssertKind(Classify(x => x.N.ToString()), NullOperandKind.CannotBeNull, null);
            AssertKind(Classify(x => x.ParentCode.ToString()), NullOperandKind.CannotBeNull, null);
            AssertKind(Classify(x => x.Id > 2 ? x.N : x.M), NullOperandKind.CanBeNull, null);
            AssertKind(Classify(x => x.Id > 2 ? x.Code : 0), NullOperandKind.CannotBeNull, null);
            AssertKind(Classify(x => x.Id > 2 ? (int?)x.Code : none), NullOperandKind.CanBeNull, null);
            AssertKind(Classify(x => x.Id > 2 ? x.Id : x.ParentCode), NullOperandKind.CanBeNull, null);
            // R6: `!` of an operand is the operand's kind; a conditional's predicate branch is a two-valued truth.
            AssertKind(Classify(x => !x.Flag), NullOperandKind.CanBeNull, "Flag");
            AssertKind(Classify(x => !x.Active), NullOperandKind.CannotBeNull, null);
            AssertKind(Classify(x => x.Id > 2 ? x.N == 5 : false), NullOperandKind.CannotBeNull, null);
            AssertKind(Classify(x => x.Id > 2 ? (bool?)(x.N == 5) : x.Flag), NullOperandKind.CanBeNull, null);
            AssertKind(Classify(x => x.Id > 2 ? !x.Flag : x.Flag), NullOperandKind.CanBeNull, null);

            // Values: read before the query, through casts.
            AssertKind(Classify(x => 5), NullOperandKind.Value, null);
            AssertKind(Classify(x => v), NullOperandKind.Value, null);
            AssertKind(Classify(x => (long?)c.IntVal), NullOperandKind.Value, null);
            AssertKind(Classify(x => Statics.Seven), NullOperandKind.Value, null);
            // `!` of a value is a value (R6-2). Classify is by shape: a conditional or a comparison of captured values is
            // read as one value where it is a side (it reads no row), but is classified as what it is, so an operand the
            // rewrite folds down to constants keeps its kind and its witness.
            AssertKind(Classify(x => !c.True), NullOperandKind.Value, null);
            AssertKind(Classify(x => (bool?)!c.True), NullOperandKind.Value, null);
            AssertKind(Classify(x => c.Five > 3 ? true : false), NullOperandKind.CannotBeNull, null);
            AssertKind(Classify(x => c.Five > 3 ? c.NullFlag : c.NullFlag), NullOperandKind.CanBeNull, null);
            AssertKind(Classify(x => c.Five == 5), NullOperandKind.NotAccepted, null);

            // Not accepted: what the visitors reject.
            AssertKind(Classify(x => x.Unmapped), NullOperandKind.NotAccepted, null);
            AssertKind(Classify(x => x.Field), NullOperandKind.NotAccepted, null);
            AssertKind(Classify(x => x.Child.Id), NullOperandKind.NotAccepted, null);
            AssertKind(Classify(x => x.Posted.Hour), NullOperandKind.NotAccepted, null);
            AssertKind(Classify(x => x.Stamp.Year), NullOperandKind.NotAccepted, null);
            AssertKind(Classify(x => x.Amount().ToString()), NullOperandKind.NotAccepted, null);
            AssertKind(Classify(x => x.NPrice.ToString()), NullOperandKind.NotAccepted, null);
            AssertKind(Classify(x => x.Id.ToString("D2")), NullOperandKind.NotAccepted, null);
            AssertKind(Classify(x => x.Unmapped.ToString()), NullOperandKind.NotAccepted, null);
            AssertKind(Classify(x => x.N + 1), NullOperandKind.NotAccepted, null);
            AssertKind(Classify(x => x.N.HasValue), NullOperandKind.NotAccepted, null);
            AssertKind(Classify(x => x.N.Value), NullOperandKind.NotAccepted, null);
            AssertKind(Classify(x => (decimal?)x.Cost), NullOperandKind.NotAccepted, null);
            AssertKind(Classify(x => x.Id > 2 ? x.Unmapped : 0), NullOperandKind.NotAccepted, null);
            AssertKind(Classify(x => x.Name.Trim().Length), NullOperandKind.NotAccepted, null);
            Assert.AreEqual(0, c.Reads, "Classify reads nothing");
        }

        private static void AssertKind(Tuple<NullOperandKind, Expression> actual, NullOperandKind kind, string witnessMember)
        {
            Assert.AreEqual(kind, actual.Item1);
            if (kind != NullOperandKind.CanBeNull)
            {
                Assert.IsNull(actual.Item2, "only an operand that can be null has a witness");
                return;
            }
            Assert.IsNotNull(actual.Item2, "an operand that can be null has a witness");
            if (witnessMember != null)
                Assert.AreEqual(witnessMember, ((MemberExpression)actual.Item2).Member.Name);
            else
                Assert.AreEqual(ExpressionType.Conditional, actual.Item2.NodeType, "a conditional is its own witness");
        }

        [TestMethod]
        public void CanBeNull_ByPropertyKind()
        {
            Func<string, bool> canBeNull = name => Funcular.Data.Orm.Linq.NullSemantics.CanBeNull(typeof(Row).GetProperty(name));
            Assert.IsFalse(canBeNull(nameof(Row.Id)));
            Assert.IsTrue(canBeNull(nameof(Row.N)));
            Assert.IsTrue(canBeNull(nameof(Row.Name)));
            Assert.IsFalse(canBeNull(nameof(Row.ParentId)), "[RemoteLink] marks a foreign key, a column of the row");
            Assert.IsTrue(canBeNull(nameof(Row.ParentCode)), "[RemoteProperty] int: a LEFT JOIN");
            Assert.IsTrue(canBeNull(nameof(Row.ParentKey)), "[RemoteKey] int: a LEFT JOIN");
            Assert.IsTrue(canBeNull(nameof(Row.DocK)), "[JsonPath]");
            Assert.IsTrue(canBeNull(nameof(Row.NPlusOne)), "[SqlExpression] int");
            Assert.IsFalse(canBeNull(nameof(Row.ChildSum)), "[SubqueryAggregate(Sum)] decimal: COALESCE(SUM(…), 0), as EF sends (T8E2-4)");
            Assert.IsTrue(canBeNull(nameof(Row.ChildSumN)), "[SubqueryAggregate(Sum)] decimal?: NULL over no rows");
            Assert.IsTrue(canBeNull(nameof(Row.ChildAvg)), "[SubqueryAggregate(Avg)] decimal: NULL over no rows");
            Assert.IsFalse(canBeNull(nameof(Row.ChildCount)), "[SubqueryAggregate(Count)]");
            Assert.IsFalse(canBeNull(nameof(Row.ChildConditionalCount)), "[SubqueryAggregate(ConditionalCount)]");
        }

        #endregion

        #region Arguments

        [TestMethod]
        public void Arguments_AreChecked()
        {
            Expression<Func<Row, bool>> predicate = x => x.N != 5;
            var row = predicate.Parameters[0];
            Assert.ThrowsException<ArgumentNullException>(() => Funcular.Data.Orm.Linq.NullSemantics.Rewrite(null, null, IsMapped, Remote));
            Assert.ThrowsException<ArgumentNullException>(() => Funcular.Data.Orm.Linq.NullSemantics.Rewrite(predicate, null, null, Remote));
            Expression<Func<Row, Row, bool>> two = (x, y) => x.N != y.N;
            Assert.ThrowsException<ArgumentException>(() => Funcular.Data.Orm.Linq.NullSemantics.Rewrite(two, null, IsMapped, Remote));
            Assert.AreEqual(ExpressionType.OrElse, Funcular.Data.Orm.Linq.NullSemantics.Rewrite(predicate, null, IsMapped, null).NodeType,
                "no remote map is an empty one");

            Assert.ThrowsException<ArgumentNullException>(() => Funcular.Data.Orm.Linq.NullSemantics.Classify(null, row, IsMapped, Remote));
            Assert.ThrowsException<ArgumentNullException>(() => Funcular.Data.Orm.Linq.NullSemantics.Classify(predicate.Body, null, IsMapped, Remote));
            Assert.ThrowsException<ArgumentNullException>(() => Funcular.Data.Orm.Linq.NullSemantics.Classify(predicate.Body, row, null, Remote));
            Assert.ThrowsException<ArgumentNullException>(() => Funcular.Data.Orm.Linq.NullSemantics.CanBeNull(null));
        }

        #endregion
    }

    internal static class RowExtensions
    {
        /// <summary>A method the visitors don't translate.</summary>
        public static decimal Amount(this NullSemanticsTests.Row row) => 0m;
    }
}
