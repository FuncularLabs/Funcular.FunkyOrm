using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Funcular.Data.Orm.SqlServer.Tests.Linq
{
    /// <summary>
    /// DB-free tests of the four WHERE visitors' own paths, on shapes the null-semantics rewrite leaves to them (3.12.0,
    /// docs/plans/NULL_SEMANTICS_PLAN.md §6.5's coverage floor): the nullable-unwrap rejections, an operand no visitor
    /// translates, a <c>new</c> operand, `!` in a value position, and a captured value the visitor reads itself (an
    /// expression visited without the rewrite: a body, not a lambda). Each row runs on SQL Server's, PostgreSQL's,
    /// MySQL's and SQLite's visitor.
    /// </summary>
    [TestClass]
    public class WhereVisitorDirectTests
    {
        [Table("t")]
        public class Row
        {
            [Column("id")] public int Id { get; set; }
            [Column("n")] public int? N { get; set; }
            [Column("flag")] public bool? Flag { get; set; }
            [Column("active")] public bool Active { get; set; }
        }

        /// <summary>A lazy collection that counts how often it is enumerated (D9: the caller's code runs at translation).</summary>
        public sealed class CountingIds : IEnumerable<int?>
        {
            public int Enumerations;

            public IEnumerator<int?> GetEnumerator()
            {
                Enumerations++;
                yield return 1;
                yield return 2;
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        /// <summary>A lazy collection whose enumeration throws, as the caller's code may (D9).</summary>
        public sealed class ThrowingIds : IEnumerable<int?>
        {
            public IEnumerator<int?> GetEnumerator() => throw new InvalidOperationException("the enumeration throws");

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        /// <summary>A reference type with a constructor, for a <c>new</c> operand (reference equality, no operator method).</summary>
        public class Pair
        {
            public Pair() { }
            public Pair(int left, int right) { }
        }

        public class Holder
        {
            public int? Value { get; set; } = 7;
            public Holder Inner { get; set; }
        }

        /// <summary>
        /// The WHERE text and parameter values each provider's visitor gives <c>predicate</c>: a lambda is rewritten first
        /// (the providers' path); a body is the visitor's own translation.
        /// </summary>
        private static IEnumerable<KeyValuePair<string, Func<Expression, string>>> Visitors()
        {
            yield return new KeyValuePair<string, Func<Expression, string>>("SqlServer", predicate =>
            {
                var generator = new Funcular.Data.Orm.Visitors.ParameterGenerator();
                var visitor = new Funcular.Data.Orm.Visitors.WhereClauseVisitor<Row>(new ConcurrentDictionary<string, string>(), new List<PropertyInfo>(),
                    generator, new Funcular.Data.Orm.Visitors.SqlExpressionTranslator(generator), "t");
                visitor.Visit(predicate);
                return visitor.WhereClauseBody + Values(visitor.Parameters.ConvertAll(p => p.Value));
            });
            yield return new KeyValuePair<string, Func<Expression, string>>("PostgreSql", predicate =>
            {
                var generator = new Funcular.Data.Orm.PostgreSql.Visitors.PostgreSqlParameterGenerator();
                var visitor = new Funcular.Data.Orm.PostgreSql.Visitors.PostgreSqlWhereClauseVisitor<Row>(new ConcurrentDictionary<string, string>(),
                    new List<PropertyInfo>(), generator, new Funcular.Data.Orm.PostgreSql.Visitors.PostgreSqlExpressionTranslator(generator), "t");
                visitor.Visit(predicate);
                return visitor.WhereClauseBody + Values(visitor.Parameters.ConvertAll(p => p.Value));
            });
            yield return new KeyValuePair<string, Func<Expression, string>>("MySql", predicate =>
            {
                var generator = new Funcular.Data.Orm.MySql.Visitors.MySqlParameterGenerator();
                var visitor = new Funcular.Data.Orm.MySql.Visitors.MySqlWhereClauseVisitor<Row>(new ConcurrentDictionary<string, string>(),
                    new List<PropertyInfo>(), generator, new Funcular.Data.Orm.MySql.Visitors.MySqlExpressionTranslator(generator), "t");
                visitor.Visit(predicate);
                return visitor.WhereClauseBody + Values(visitor.Parameters.ConvertAll(p => p.Value));
            });
            yield return new KeyValuePair<string, Func<Expression, string>>("Sqlite", predicate =>
            {
                var generator = new Funcular.Data.Orm.Sqlite.Visitors.SqliteParameterGenerator();
                var visitor = new Funcular.Data.Orm.Sqlite.Visitors.SqliteWhereClauseVisitor<Row>(new ConcurrentDictionary<string, string>(),
                    new List<PropertyInfo>(), generator, new Funcular.Data.Orm.Sqlite.Visitors.SqliteExpressionTranslator(generator), "t");
                visitor.Visit(predicate);
                return visitor.WhereClauseBody + Values(visitor.Parameters.ConvertAll(p => p.Value));
            });
        }

        private static string Values(List<object> values) => " |" + string.Concat(values.ConvertAll(v => " " + (v is DBNull || v == null ? "NULL" : v)));

        /// <summary>A provider's text with its parameter names as <c>@p</c> and its whitespace collapsed.</summary>
        private static string Normalized(string sql) => Regex.Replace(Regex.Replace(sql, @"@p(__linq__)?\d+", "@p"), @"\s+", " ").Trim();

        private static readonly ParameterExpression X = Expression.Parameter(typeof(Row), "x");

        private static Expression<Func<Row, bool>> Lambda(Expression body) => Expression.Lambda<Func<Row, bool>>(body, X);

        private static Expression Prop(string name) => Expression.Property(X, name);

        /// <summary>
        /// 3.12.1 (D-133(b)): SQL Server compares a bool member alone in a truth position with <c>CAST(1 AS bit)</c> (CAST, so
        /// a JSON path's text converts to bit, not int); a comparison's operand stays the column.
        /// </summary>
        [TestMethod]
        public void ABareBoolMemberInATruthPosition_IsComparedWithCastTrue_OnSqlServer()
        {
            Func<Expression<Func<Row, bool>>, string> sqlServer = predicate => Normalized(Visitors().First(v => v.Key == "SqlServer").Value(predicate));
            Assert.AreEqual("t.active = CAST(1 AS bit) |", sqlServer(x => x.Active));
            Assert.AreEqual("NOT t.active = CAST(1 AS bit) |", sqlServer(x => !x.Active));
            Assert.AreEqual("(t.active = CAST(1 AS bit) OR t.id > @p) | 2", sqlServer(x => x.Active || x.Id > 2));
            Assert.AreEqual("CASE WHEN t.active = CAST(1 AS bit) THEN t.id ELSE @p END > @p | 0 2", sqlServer(x => (x.Active ? x.Id : 0) > 2));
            Assert.AreEqual("t.flag = t.active |", sqlServer(x => x.Flag == x.Active));
        }

        /// <summary>
        /// R5-1(b): a branch of a conditional in a value position is a value: a literal there is a parameter, not
        /// <c>NOT 1=0</c>, which T-SQL doesn't accept as a CASE result.
        /// </summary>
        [TestMethod]
        public void AValuePositionConditional_KeepsItsBranchesAsValues()
        {
            Expression<Func<Row, bool>> literalBranch = x => (x.Id > 2 ? x.Flag : (bool?)true) == true;
            Expression<Func<Row, bool>> falseBranch = x => (x.Id > 2 ? x.Active : false) == true;
            foreach (var visitor in Visitors())
            {
                foreach (var predicate in new[] { literalBranch, falseBranch })
                {
                    var sql = visitor.Value(predicate);
                    Assert.IsFalse(sql.Contains("1=0"), visitor.Key + ": " + sql);
                    StringAssert.Contains(sql, "END = @", visitor.Key + ": the conditional compared, as 3.11.0 sent it: " + sql);
                }
            }
        }

        /// <summary>
        /// R6-1: `!` over a conditional in a value position (a comparison's operand) keeps the conditional's branches values,
        /// as 3.11.0 sent them: a literal there (`ELSE 1=0`) isn't valid T-SQL. Through the rewrite, and the visitor's own.
        /// </summary>
        [TestMethod]
        public void ANotInAValuePosition_KeepsTheConditionalsBranchesValues()
        {
            Expression<Func<Row, bool>> predicate = x => !(x.Id > 2 ? x.Active : false) == true;
            foreach (var visitor in Visitors())
            {
                Assert.AreEqual("NOT CASE WHEN t.id > @p THEN t.active ELSE @p END = @p | 2 False True", Normalized(visitor.Value(predicate)),
                    visitor.Key + ": through the rewrite");
                Assert.AreEqual("NOT CASE WHEN t.id > @p THEN t.active ELSE @p END = @p | 2 False True", Normalized(visitor.Value(predicate.Body)),
                    visitor.Key + ": the visitor's own");
            }
        }

        /// <summary>A conditional under a value-position `!` keeps its form when the rewrite folds its test: no literal there.</summary>
        [TestMethod]
        public void ANotInAValuePosition_OverAFoldedConditional_KeepsItsForm()
        {
            int? none = null;
            foreach (var visitor in Visitors())
                Assert.AreEqual("NOT CASE WHEN 1=0 THEN t.active ELSE @p END != t.active | False",
                    Normalized(visitor.Value((Expression<Func<Row, bool>>)(x => !(x.Id >= none ? x.Active : false) != x.Active))).Replace(" <> ", " != "),
                    visitor.Key);
        }

        /// <summary>
        /// R7-2: in a value position only a conditional, or a chain of `!` and casts that ends at one, keeps its form; any
        /// other known truth under a `!` is the literal 3.11.0 sent, from the memo, and nothing is read again. Two NOTs over
        /// a folded conditional stay a CASE (R7-4, Y10).
        /// </summary>
        [TestMethod]
        public void ANotInAValuePosition_OverAKnownTruth_IsItsLiteral()
        {
            var holder = new Holder();
            Expression<Func<Row, bool>> known = x => x.Flag == !(bool?)(holder.Value == holder.Value);
            int? none = null;
            Expression<Func<Row, bool>> twice = x => !!(x.Id >= none ? x.Active : false) != x.Active;
            foreach (var visitor in Visitors())
            {
                Assert.AreEqual("t.flag = NOT NOT 1=0 |", Normalized(visitor.Value(known.Body)), visitor.Key + ": the visitor's own, as 3.11.0");
                Assert.AreEqual("NOT NOT CASE WHEN 1=0 THEN t.active ELSE @p END != t.active | False",
                    Normalized(visitor.Value(twice)).Replace(" <> ", " != "), visitor.Key + ": two NOTs over a folded conditional");
            }
        }

        /// <summary>
        /// R8-4: the visitor's own (no rewrite), a `!` over a cast over a conditional whose truth the fold knows, compared:
        /// the conditional keeps its form under both, as R6-1 needs on SQL Server.
        /// </summary>
        [TestMethod]
        public void ANotInAValuePosition_OverACastOverAConditional_KeepsItsForm()
        {
            var conditional = Expression.Condition(Expression.GreaterThan(Prop(nameof(Row.Id)), Expression.Constant(2)),
                Expression.Constant(true), Expression.Constant(true));
            var body = Expression.Equal(Expression.Not(Expression.Convert(conditional, typeof(bool?))), Prop(nameof(Row.Flag)));
            foreach (var visitor in Visitors())
                Assert.AreEqual("NOT CASE WHEN t.id > @p THEN @p ELSE @p END = t.flag | 2 True True", Normalized(visitor.Value(body)), visitor.Key);
        }

        /// <summary>
        /// R5-8: a lazy collection in an operand the SQL repeats is enumerated once, as in 3.11.0: a conditional that EF's
        /// forms for two operands that can both be null test for NULL beside the comparison, three copies for <c>!=</c>.
        /// A predicate compared with a <c>bool</c> is sent once since R11-5, so its collection is enumerated once anyway.
        /// </summary>
        [TestMethod]
        public void ALazyCollection_InARepeatedOperand_IsEnumeratedOnce()
        {
            // Declared as IEnumerable<int?>, as a LINQ query is: an array can stand in for it (a variable of a concrete
            // lazy type can't be replaced, and is enumerated per copy: plan §3).
            int? none = null;
            foreach (var visitor in Visitors())
            {
                var counter = new CountingIds();
                IEnumerable<int?> lazy = counter;
                var repeated = visitor.Value((Expression<Func<Row, bool>>)(x => (lazy.Contains(x.N) ? x.N : none) != x.N));
                Assert.AreEqual(3, Regex.Matches(repeated, @"\bIN\b").Count, visitor.Key + ": the conditional, three times: " + repeated);
                Assert.AreEqual(1, counter.Enumerations, visitor.Key + ": (lazy.Contains(x.N) ? x.N : none) != x.N");
                counter = new CountingIds();
                lazy = counter;
                var once = visitor.Value((Expression<Func<Row, bool>>)(x => x.Active == lazy.Contains(x.N)));
                Assert.AreEqual(1, Regex.Matches(once, @"\bIN\b").Count, visitor.Key + ": the predicate, once: " + once);
                Assert.AreEqual(1, counter.Enumerations, visitor.Key + ": x.Active == lazy.Contains(x.N)");
            }
        }

        /// <summary>R6-7 (c): a query held in an <c>IQueryable&lt;T&gt;</c> variable is executed once too.</summary>
        [TestMethod]
        public void ALazyQueryable_InARepeatedOperand_IsExecutedOnce()
        {
            int? none = null;
            foreach (var visitor in Visitors())
            {
                var counter = new CountingIds();
                IQueryable<int?> query = counter.AsQueryable();
                visitor.Value((Expression<Func<Row, bool>>)(x => (query.Contains(x.N) ? x.N : none) != x.N));
                Assert.AreEqual(1, counter.Enumerations, visitor.Key + ": (query.Contains(x.N) ? x.N : none) != x.N");
            }
        }

        /// <summary>
        /// R6-8 (c): a variable of a concrete lazy type can't hold an array, but <c>Contains</c>'s parameter can, so it is
        /// enumerated once there too; one whose enumeration throws is left for the translator, which runs it and reports
        /// it (D9).
        /// </summary>
        [TestMethod]
        public void ALazyCollection_OfAConcreteType_IsEnumeratedOnce_AndOneThatThrows_IsLeftForTheTranslator()
        {
            int? none = null;
            foreach (var visitor in Visitors())
            {
                var counter = new CountingIds();
                visitor.Value((Expression<Func<Row, bool>>)(x => (counter.Contains(x.N) ? x.N : none) != x.N));
                Assert.AreEqual(1, counter.Enumerations, visitor.Key + ": a CountingIds variable");

                // Held in an object variable and cast where it is used: materialized against the cast's type.
                counter = new CountingIds();
                object boxed = counter;
                visitor.Value((Expression<Func<Row, bool>>)(x => (((IEnumerable<int?>)boxed).Contains(x.N) ? x.N : none) != x.N));
                Assert.AreEqual(1, counter.Enumerations, visitor.Key + ": an object variable, cast");

                IEnumerable<int?> throwing = new ThrowingIds();
                var ex = Assert.ThrowsException<NotSupportedException>(
                    () => visitor.Value((Expression<Func<Row, bool>>)(x => (throwing.Contains(x.N) ? x.N : none) != x.N)), visitor.Key);
                StringAssert.StartsWith(ex.Message, "Could not evaluate expression", visitor.Key);
                Assert.AreEqual("the enumeration throws", ex.InnerException?.Message, visitor.Key + ": the caller's exception, reported");
            }
        }

        [TestMethod]
        public void HasValue_OnANullableProperty_IsRejected()
        {
            foreach (var visitor in Visitors())
            {
                // Each provider assembly defines its own NullableExpressionException: asserted by name.
                Exception ex = null;
                try { visitor.Value(Lambda(Expression.Property(Prop(nameof(Row.N)), "HasValue"))); } catch (Exception thrown) { ex = thrown; }
                Assert.IsNotNull(ex, visitor.Key + ": no exception");
                Assert.AreEqual("NullableExpressionException", ex.GetType().Name, visitor.Key);
                StringAssert.Contains(ex.Message, "Do not use '.HasValue' on nullable property 'N'", visitor.Key);
            }
        }

        [TestMethod]
        public void AnOperandNoVisitorTranslates_IsRejected()
        {
            // x == x: the row itself, not a member of it.
            foreach (var visitor in Visitors())
            {
                var ex = Assert.ThrowsException<NotSupportedException>(() => visitor.Value(Lambda(Expression.Equal(X, X))), visitor.Key);
                StringAssert.StartsWith(ex.Message, "Expression type Parameter is not supported", visitor.Key);
            }
        }

        [TestMethod]
        public void ANewOperand_IsItsArgumentsInParentheses()
        {
            var withArguments = Expression.Equal(
                Expression.New(typeof(Pair).GetConstructor(new[] { typeof(int), typeof(int) }), Prop(nameof(Row.Id)), Expression.Constant(1)),
                Expression.New(typeof(Pair)));
            foreach (var visitor in Visitors())
                StringAssert.StartsWith(visitor.Value(Lambda(withArguments)), "(t.id, @", visitor.Key);
        }

        /// <summary>
        /// R6-8 (a): the visitor reads a captured value itself where nothing rewrote it (a body visited without the rewrite):
        /// a property of a constant. Through the rewrite the value is read and substituted first (AC-N11 (g)), and bound
        /// the same; a nested member over a null can't be read by either, and the visitor reports it.
        /// </summary>
        [TestMethod]
        public void TheVisitor_ReadsACapturedValueItself_AndTheRewriteBindsItTheSame()
        {
            var holder = Expression.Constant(new Holder());
            var branch = Expression.Condition(Expression.GreaterThan(Prop(nameof(Row.Id)), Expression.Constant(2)), Prop(nameof(Row.N)),
                Expression.Property(holder, nameof(Holder.Value)));
            var reads = Lambda(Expression.Equal(branch, Expression.Constant(5, typeof(int?))));

            var broken = Expression.Property(Expression.Property(Expression.Constant(new Holder(), typeof(Holder)), nameof(Holder.Inner)), nameof(Holder.Value));
            var throws = Lambda(Expression.Equal(
                Expression.Condition(Expression.GreaterThan(Prop(nameof(Row.Id)), Expression.Constant(2)), Prop(nameof(Row.N)), broken),
                Expression.Constant(5, typeof(int?))));
            foreach (var visitor in Visitors())
            {
                var sql = visitor.Value(reads.Body);
                StringAssert.Contains(sql, "CASE WHEN t.id > @", visitor.Key);
                StringAssert.EndsWith(sql, " | 2 7 5", visitor.Key + ": the branch's value, read by the visitor");
                Assert.AreEqual(Normalized(sql), Normalized(visitor.Value(reads)), visitor.Key + ": the rewrite's substitution binds the same");
                var ex = Assert.ThrowsException<NotSupportedException>(() => visitor.Value(throws), visitor.Key);
                StringAssert.StartsWith(ex.Message, "Could not evaluate expression", visitor.Key);
            }
        }

        [Table("t")]
        public class Computed
        {
            [Column("id")] public int Id { get; set; }
            [Column("name")] public string Name { get; set; }
            [Column("name2")] public string Name2 { get; set; }
            [Column("d")] public DateTime? D { get; set; }
            // The visitors take their SQL from the remote map, which the test fills with each provider's own text.
            [Funcular.Data.Orm.Attributes.SqlExpression("LENGTH({Name})")] public int? NameLen { get; set; }
            [Funcular.Data.Orm.Attributes.SqlExpression("YEAR({D})")] public int? DYear { get; set; }
            [Funcular.Data.Orm.Attributes.SqlExpression("{Name}")] public string NameX { get; set; }
            [Funcular.Data.Orm.Attributes.SqlExpression("{D}")] public DateTime? DX { get; set; }
            // R18-1: the columns of a nested conditional around the computed part, and NameLen's text in lower case.
            [Column("active")] public bool Active { get; set; }
            [Column("n")] public int? N { get; set; }
            [Column("m")] public int? M { get; set; }
            [Column("code")] public int Code { get; set; }
            [Funcular.Data.Orm.Attributes.SqlExpression("length({Name})")] public int? NameLenLower { get; set; }
            // R19-3: templates that differ only in a string literal's letter case, and one only in SQL's own words'.
            [Funcular.Data.Orm.Attributes.SqlExpression("CASE WHEN {Name} = 'a' THEN 1 ELSE 0 END")] public int? IsA { get; set; }
            [Funcular.Data.Orm.Attributes.SqlExpression("CASE WHEN {Name} = 'A' THEN 1 ELSE 0 END")] public int? IsUpperA { get; set; }
            [Funcular.Data.Orm.Attributes.SqlExpression("case when {Name} = 'a' then 1 else 0 end")] public int? IsALower { get; set; }
        }

        /// <summary>
        /// Each provider's operand SQL for <see cref="WherePredicateGuard.Validate(LambdaExpression, WhereOperation, Func{Expression, string})"/>
        /// over <see cref="Computed"/>, as the providers' shared WHERE translation builds it: a fresh visitor over the operand,
        /// its parameters written as their values (<see cref="WherePredicateGuard.OperandText"/>).
        /// </summary>
        private static IEnumerable<KeyValuePair<string, Func<Expression, Dictionary<string, string>, string>>> ComputedOperandSql()
        {
            yield return new KeyValuePair<string, Func<Expression, Dictionary<string, string>, string>>("SqlServer", (operand, map) =>
            {
                var generator = new Funcular.Data.Orm.Visitors.ParameterGenerator();
                var visitor = new Funcular.Data.Orm.Visitors.WhereClauseVisitor<Computed>(new ConcurrentDictionary<string, string>(), new List<PropertyInfo>(),
                    generator, new Funcular.Data.Orm.Visitors.SqlExpressionTranslator(generator), "t", map);
                visitor.Visit(operand);
                return WherePredicateGuard.OperandText(visitor.WhereClauseBody, visitor.Parameters.Select(p => new KeyValuePair<string, object>(p.ParameterName, p.Value)));
            });
            yield return new KeyValuePair<string, Func<Expression, Dictionary<string, string>, string>>("PostgreSql", (operand, map) =>
            {
                var generator = new Funcular.Data.Orm.PostgreSql.Visitors.PostgreSqlParameterGenerator();
                var visitor = new Funcular.Data.Orm.PostgreSql.Visitors.PostgreSqlWhereClauseVisitor<Computed>(new ConcurrentDictionary<string, string>(),
                    new List<PropertyInfo>(), generator, new Funcular.Data.Orm.PostgreSql.Visitors.PostgreSqlExpressionTranslator(generator), "t", map);
                visitor.Visit(operand);
                return WherePredicateGuard.OperandText(visitor.WhereClauseBody, visitor.Parameters.Select(p => new KeyValuePair<string, object>(p.ParameterName, p.Value)));
            });
            yield return new KeyValuePair<string, Func<Expression, Dictionary<string, string>, string>>("MySql", (operand, map) =>
            {
                var generator = new Funcular.Data.Orm.MySql.Visitors.MySqlParameterGenerator();
                var visitor = new Funcular.Data.Orm.MySql.Visitors.MySqlWhereClauseVisitor<Computed>(new ConcurrentDictionary<string, string>(),
                    new List<PropertyInfo>(), generator, new Funcular.Data.Orm.MySql.Visitors.MySqlExpressionTranslator(generator), "t", map);
                visitor.Visit(operand);
                return WherePredicateGuard.OperandText(visitor.WhereClauseBody, visitor.Parameters.Select(p => new KeyValuePair<string, object>(p.ParameterName, p.Value)));
            });
            yield return new KeyValuePair<string, Func<Expression, Dictionary<string, string>, string>>("Sqlite", (operand, map) =>
            {
                var generator = new Funcular.Data.Orm.Sqlite.Visitors.SqliteParameterGenerator();
                var visitor = new Funcular.Data.Orm.Sqlite.Visitors.SqliteWhereClauseVisitor<Computed>(new ConcurrentDictionary<string, string>(),
                    new List<PropertyInfo>(), generator, new Funcular.Data.Orm.Sqlite.Visitors.SqliteExpressionTranslator(generator), "t", map);
                visitor.Visit(operand);
                return WherePredicateGuard.OperandText(visitor.WhereClauseBody, visitor.Parameters.Select(p => new KeyValuePair<string, object>(p.ParameterName, p.Value)));
            });
        }

        /// <summary>
        /// R19-3: letter case is aside only outside quoted text. Two templates that differ in a string literal's letter case
        /// hold different values (on SQLite and PostgreSQL; on SQL Server and MySQL under a case-sensitive collation), so a
        /// comparison of them, nested and in an <c>||</c>, isn't a copy, in each provider's text; two that differ only in
        /// SQL's own words' letter case are.
        /// </summary>
        [TestMethod]
        public void TheGuard_ComparesQuotedTextWithItsLetterCase_InEveryProvider()
        {
            foreach (var operandSql in ComputedOperandSql())
            {
                var map = new Dictionary<string, string>
                {
                    [nameof(Computed.IsA)] = "CASE WHEN t.name = 'a' THEN 1 ELSE 0 END",
                    [nameof(Computed.IsUpperA)] = "CASE WHEN t.name = 'A' THEN 1 ELSE 0 END",
                    [nameof(Computed.IsALower)] = "case when t.name = 'a' then 1 else 0 end",
                };
                Func<Expression, string> sql = operand => operandSql.Value(operand, map);
                Expression<Func<Computed, bool>> differ = x =>
                    ((x.Id > 2 ? x.N : (int?)x.Code) != 5 ? x.IsA : x.M) == ((x.Id > 2 ? x.N : (int?)x.Code) != 5 ? x.IsUpperA : x.M) || x.Id == 2;
                WherePredicateGuard.Validate(differ, WhereOperation.MassMutation, sql);
                Expression<Func<Computed, bool>> same = x =>
                    ((x.Id > 2 ? x.N : (int?)x.Code) != 5 ? x.IsA : x.M) == ((x.Id > 2 ? x.N : (int?)x.Code) != 5 ? x.IsALower : x.M) || x.Id == 2;
                Assert.AreEqual(WherePredicateGuard.AlwaysTrueMessage, Assert.ThrowsException<InvalidOperationException>(
                    () => WherePredicateGuard.Validate(same, WhereOperation.MassMutation, sql), operandSql.Key).Message, operandSql.Key);
            }
        }

        /// <summary>Counts the reads of its value (R18-1: the operand SQL never reads a captured value again, AC-N13).</summary>
        public sealed class CountingInt
        {
            public int Reads;

            public int Value
            {
                get
                {
                    Reads++;
                    return 2;
                }
            }
        }

        /// <summary>
        /// R18-1: a nested conditional whose branch is a computed property that is the provider's own text for a column's
        /// part, compared with its copy over that part (or a template that differs only in letter case), is distributed
        /// into its branches, so the clause's text never shows the copy: with each provider's operand SQL the verdict
        /// refuses it, as 3.11.0 refused the parameter-free ones, under each wrapper, and with constants in the tests.
        /// Neighbours run: another column's part, another constant. A part that reads a captured value is compared as
        /// written, never translated: the same captured member is the same value, so its copy is refused, and the value
        /// isn't read for it (AC-N13).
        /// </summary>
        [TestMethod]
        public void TheGuard_RefusesANestedComputedTwin_WithEveryProvidersOperandSql()
        {
            var visitors = ComputedVisitors().ToDictionary(v => v.Key, v => v.Value);
            foreach (var operandSql in ComputedOperandSql())
            {
                var none = new Dictionary<string, string>();
                Func<Expression<Func<Computed, bool>>, string> leftOfEquals = p =>
                {
                    var where = visitors[operandSql.Key](p, none);
                    return where.Substring(0, where.IndexOf(" = ", StringComparison.Ordinal)).Trim();
                };
                var map = new Dictionary<string, string>
                {
                    [nameof(Computed.NameLen)] = leftOfEquals(x => x.Name.Length == 1),
                    [nameof(Computed.NameLenLower)] = leftOfEquals(x => x.Name.Length == 1).ToLowerInvariant(),
                    [nameof(Computed.DYear)] = leftOfEquals(x => x.D.Value.Year == 2020),
                    [nameof(Computed.NameX)] = leftOfEquals(x => x.Name == "z"),
                };
                Func<Expression, string> sql = operand => operandSql.Value(operand, map);
                var refused = new Dictionary<string, Expression<Func<Computed, bool>>>
                {
                    ["T1"] = x => ((x.Active ? x.N : x.M) == x.Code ? x.NameLen : x.M) == ((x.Active ? x.N : x.M) == x.Code ? (int?)x.Name.Length : x.M),
                    ["T2"] = x => ((x.Active ? x.NameLen : x.M) == x.Code ? x.N : x.M) == ((x.Active ? (int?)x.Name.Length : x.M) == x.Code ? x.N : x.M),
                    ["T3"] = x => ((x.Active ? x.N : x.M) == x.Code ? x.DYear : x.M) == ((x.Active ? x.N : x.M) == x.Code ? (int?)x.D.Value.Year : x.M),
                    ["T4"] = x => ((x.Active ? x.N : x.M) == x.Code ? x.NameLen : x.M) == ((x.Active ? x.N : x.M) == x.Code ? (int?)x.NameX.Length : x.M),
                    ["T5"] = x => !(((x.Active ? x.N : x.M) == x.Code ? x.NameLen : x.M) != ((x.Active ? x.N : x.M) == x.Code ? (int?)x.Name.Length : x.M)),
                    ["T8"] = x => (bool)(object)(((x.Active ? x.N : x.M) == x.Code ? x.NameLen : x.M) == ((x.Active ? x.N : x.M) == x.Code ? (int?)x.Name.Length : x.M)),
                    ["L2"] = x => ((x.Active ? x.N : x.M) == x.Code ? x.NameLenLower : x.M) == ((x.Active ? x.N : x.M) == x.Code ? x.NameLen : x.M),
                    ["L5"] = x => ((x.Active ? x.N : x.M) == x.Code ? x.NameLenLower : x.M) == ((x.Active ? x.N : x.M) == x.Code ? (int?)x.Name.Length : x.M),
                    ["K1"] = x => ((x.Id > 2 ? x.N : (int?)x.Code) != 5 ? x.NameLen : (int?)x.Code) == ((x.Id > 2 ? x.N : (int?)x.Code) != 5 ? (int?)x.Name.Length : (int?)x.Code),
                };
                foreach (var shape in refused)
                {
                    var ex = Assert.ThrowsException<InvalidOperationException>(
                        () => WherePredicateGuard.Validate(shape.Value, WhereOperation.MassMutation, sql), operandSql.Key + ", " + shape.Key);
                    Assert.AreEqual(WherePredicateGuard.SelfReferenceMessage, ex.Message, operandSql.Key + ", " + shape.Key);
                    WherePredicateGuard.Validate(shape.Value, WhereOperation.MassMutation); // the expression alone shows no copy
                }
                Expression<Func<Computed, bool>> orId2 = x =>
                    ((x.Active ? x.N : x.M) == x.Code ? x.NameLen : x.M) == ((x.Active ? x.N : x.M) == x.Code ? (int?)x.Name.Length : x.M) || x.Id == 2;
                Assert.AreEqual(WherePredicateGuard.AlwaysTrueMessage, Assert.ThrowsException<InvalidOperationException>(
                    () => WherePredicateGuard.Validate(orId2, WhereOperation.MassMutation, sql), operandSql.Key + ", T7").Message, operandSql.Key + ", T7");
                foreach (var neighbour in new Expression<Func<Computed, bool>>[]
                         {
                             x => ((x.Active ? x.N : x.M) == x.Code ? x.NameLen : x.M) == ((x.Active ? x.N : x.M) == x.Code ? (int?)x.Name2.Length : x.M),
                             x => ((x.Id > 2 ? x.N : (int?)x.Code) != 5 ? x.NameLen : (int?)x.Code) == ((x.Id > 2 ? x.N : (int?)x.Code) != 6 ? (int?)x.Name.Length : (int?)x.Code),
                         })
                    WherePredicateGuard.Validate(neighbour, WhereOperation.MassMutation, sql);
                var counting = new CountingInt();
                Expression<Func<Computed, bool>> captured = x =>
                    ((x.Id > counting.Value ? x.N : x.M) == x.Code ? x.NameLen : x.M) == ((x.Id > counting.Value ? x.N : x.M) == x.Code ? (int?)x.Name.Length : x.M);
                Assert.AreEqual(WherePredicateGuard.SelfReferenceMessage, Assert.ThrowsException<InvalidOperationException>(
                    () => WherePredicateGuard.Validate(captured, WhereOperation.MassMutation, sql), operandSql.Key + ", captured").Message);
                Assert.AreEqual(0, counting.Reads, operandSql.Key + ": the captured value read for the operand SQL");
            }
        }

        /// <summary>Each provider's WHERE text for a predicate over <see cref="Computed"/>, with <c>map</c> as its remote map.</summary>
        private static IEnumerable<KeyValuePair<string, Func<Expression<Func<Computed, bool>>, Dictionary<string, string>, string>>> ComputedVisitors()
        {
            yield return new KeyValuePair<string, Func<Expression<Func<Computed, bool>>, Dictionary<string, string>, string>>("SqlServer", (predicate, map) =>
            {
                var generator = new Funcular.Data.Orm.Visitors.ParameterGenerator();
                var visitor = new Funcular.Data.Orm.Visitors.WhereClauseVisitor<Computed>(new ConcurrentDictionary<string, string>(), new List<PropertyInfo>(),
                    generator, new Funcular.Data.Orm.Visitors.SqlExpressionTranslator(generator), "t", map);
                visitor.Visit(predicate);
                return visitor.WhereClauseBody;
            });
            yield return new KeyValuePair<string, Func<Expression<Func<Computed, bool>>, Dictionary<string, string>, string>>("PostgreSql", (predicate, map) =>
            {
                var generator = new Funcular.Data.Orm.PostgreSql.Visitors.PostgreSqlParameterGenerator();
                var visitor = new Funcular.Data.Orm.PostgreSql.Visitors.PostgreSqlWhereClauseVisitor<Computed>(new ConcurrentDictionary<string, string>(),
                    new List<PropertyInfo>(), generator, new Funcular.Data.Orm.PostgreSql.Visitors.PostgreSqlExpressionTranslator(generator), "t", map);
                visitor.Visit(predicate);
                return visitor.WhereClauseBody;
            });
            yield return new KeyValuePair<string, Func<Expression<Func<Computed, bool>>, Dictionary<string, string>, string>>("MySql", (predicate, map) =>
            {
                var generator = new Funcular.Data.Orm.MySql.Visitors.MySqlParameterGenerator();
                var visitor = new Funcular.Data.Orm.MySql.Visitors.MySqlWhereClauseVisitor<Computed>(new ConcurrentDictionary<string, string>(),
                    new List<PropertyInfo>(), generator, new Funcular.Data.Orm.MySql.Visitors.MySqlExpressionTranslator(generator), "t", map);
                visitor.Visit(predicate);
                return visitor.WhereClauseBody;
            });
            yield return new KeyValuePair<string, Func<Expression<Func<Computed, bool>>, Dictionary<string, string>, string>>("Sqlite", (predicate, map) =>
            {
                var generator = new Funcular.Data.Orm.Sqlite.Visitors.SqliteParameterGenerator();
                var visitor = new Funcular.Data.Orm.Sqlite.Visitors.SqliteWhereClauseVisitor<Computed>(new ConcurrentDictionary<string, string>(),
                    new List<PropertyInfo>(), generator, new Funcular.Data.Orm.Sqlite.Visitors.SqliteExpressionTranslator(generator), "t", map);
                visitor.Visit(predicate);
                return visitor.WhereClauseBody;
            });
        }

        /// <summary>
        /// R16-1: a computed property whose SQL is the provider's own text for a column's <c>Length</c> or year, compared with
        /// that part of the column (or of the column's alias): 3.11.0 refused each delete as self-referencing, and EF's form,
        /// <c>(A = A OR (A IS NULL AND col IS NULL))</c>, tests two witnesses, both read by A. Each provider's text is
        /// refused; the same part of another column is accepted.
        /// </summary>
        [TestMethod]
        public void TheGuard_RefusesAComputedPartAgainstTheColumnsPart_InEveryProvidersText()
        {
            foreach (var visitor in ComputedVisitors())
            {
                var none = new Dictionary<string, string>();
                Func<Expression<Func<Computed, bool>>, string> leftOfEquals = p =>
                {
                    var where = visitor.Value(p, none);
                    return where.Substring(0, where.IndexOf(" = ", StringComparison.Ordinal)).Trim();
                };
                var day = new DateTime(2020, 1, 1);
                var map = new Dictionary<string, string>
                {
                    [nameof(Computed.NameLen)] = leftOfEquals(x => x.Name.Length == 1),
                    [nameof(Computed.DYear)] = leftOfEquals(x => x.D.Value.Year == 2020),
                    [nameof(Computed.NameX)] = leftOfEquals(x => x.Name == "z"),
                    [nameof(Computed.DX)] = leftOfEquals(x => x.D == day),
                };
                foreach (var shape in new Expression<Func<Computed, bool>>[]
                         {
                             x => x.NameLen == x.Name.Length,
                             x => x.Name.Length == x.NameLen,
                             x => (int?)x.Name.Length == x.NameLen,
                             x => x.NameLen == x.NameX.Length,
                             x => x.DYear == x.D.Value.Year,
                             x => x.D.Value.Year == x.DYear,
                             x => x.DYear == x.DX.Value.Year,
                         })
                {
                    var where = visitor.Value(shape, map);
                    var refused = Assert.ThrowsException<InvalidOperationException>(() => WherePredicateGuard.ValidateTranslation(where),
                        visitor.Key + ", " + shape + ": " + where);
                    Assert.AreEqual(WherePredicateGuard.SelfReferenceMessage, refused.Message, visitor.Key + ", " + shape);
                }
                WherePredicateGuard.ValidateTranslation(visitor.Value(x => x.NameLen == x.Name2.Length, map));
            }
        }
    }
}
