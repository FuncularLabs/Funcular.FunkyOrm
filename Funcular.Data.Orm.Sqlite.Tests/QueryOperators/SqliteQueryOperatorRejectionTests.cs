using System.Linq.Expressions;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Person;

namespace Funcular.Data.Orm.Sqlite.Tests.QueryOperators
{
    /// <summary>
    /// #13: rejected shapes (AC13-4) including the covariance invariants I1/I2, message precedence (AC13-8), operators
    /// after Skip/Take (AC13-10) and a second OrderBy (AC13-12). Every rejection asserts exactly
    /// <see cref="NotSupportedException"/> and that no SQL ran. Rows that must execute order by <c>FirstName</c>
    /// (seeded in <c>Id</c> order): on SQLite a bare <c>id</c> is ambiguous on the join entity (#12, §1.3).
    /// </summary>
    [TestClass]
    public class SqliteQueryOperatorRejectionTests : SqliteQueryOperatorTestBase
    {
        [ClassCleanup]
        public static void DeleteDatabase() => DeleteClassDatabase(typeof(SqliteQueryOperatorRejectionTests));

        private const string PolicyMessage = "is not translated to SQL in this version";
        private const string PagingMessage = "after Skip/Take";
        private const string SecondOrderByMessage = "A second OrderBy is not translated";
        private const string I1Message = "takes a predicate over";
        private const string CompositionMessage = "must be the outermost query operator";
        private const string SelectShapeMessage = "A top-level Select must project to";
        private const string ScalarGuardMessage = "is only supported for a list/enumeration result";
        private const string CastMessage = "supports only identity and reference-conversion casts";

        /// <summary>Spells a conversion both ways: the no-node reference conversion, or a <c>Cast</c> node.</summary>
        private static IQueryable<TBase> Convert<TBase>(IQueryable<PersonDetailEntity> query, string spelling) where TBase : class =>
            spelling == "implicit" ? (IQueryable<TBase>)query : query.Cast<TBase>();

        private static IQueryable<object> ConvertScalar(IQueryable<string> query, string spelling) =>
            spelling == "implicit" ? query : query.Cast<object>();

        private static readonly string[] Spellings = { "implicit", "cast" };

        #region AC13-4 allow-list table

        private static readonly Dictionary<string, (string Operator, Func<IQueryable<PersonDetailEntity>, IQueryable<PersonDetailEntity>, object> Run)> RejectedShapes =
            new Dictionary<string, (string, Func<IQueryable<PersonDetailEntity>, IQueryable<PersonDetailEntity>, object>)>
            {
                ["Reverse"] = ("Reverse", (q, o) => q.Reverse().ToList()),
                ["TakeWhile"] = ("TakeWhile", (q, o) => q.TakeWhile(p => p.Id > 0).ToList()),
                ["SkipWhile"] = ("SkipWhile", (q, o) => q.SkipWhile(p => p.Id < 0).ToList()),
                ["TakeLast"] = ("TakeLast", (q, o) => q.TakeLast(1).ToList()),
                ["SkipLast"] = ("SkipLast", (q, o) => q.SkipLast(1).ToList()),
                ["ElementAt"] = ("ElementAt", (q, o) => q.OrderBy(p => p.Id).ElementAt(0)),
                ["ElementAtOrDefault"] = ("ElementAtOrDefault", (q, o) => q.OrderBy(p => p.Id).ElementAtOrDefault(0)),
                ["Order"] = ("Order", (q, o) => q.Order().ToList()),
                ["OrderDescending"] = ("OrderDescending", (q, o) => q.OrderDescending().ToList()),
                ["DefaultIfEmpty"] = ("DefaultIfEmpty", (q, o) => q.DefaultIfEmpty().ToList()),
                ["Concat"] = ("Concat", (q, o) => q.Concat(o).ToList()),
                ["Union"] = ("Union", (q, o) => q.Union(o).ToList()),
                ["Intersect"] = ("Intersect", (q, o) => q.Intersect(o).ToList()),
                ["Except"] = ("Except", (q, o) => q.Except(o).ToList()),
                ["Zip"] = ("Zip", (q, o) => q.Zip(o, (a, b) => a).ToList()),
                ["SelectMany"] = ("SelectMany", (q, o) => q.SelectMany(p => new[] { p }).ToList()),
                ["Join"] = ("Join", (q, o) => q.Join(o, a => a.Id, b => b.Id, (a, b) => a).ToList()),
                ["GroupJoin"] = ("GroupJoin", (q, o) => q.GroupJoin(o, a => a.Id, b => b.Id, (a, bs) => a).ToList()),
                ["Append"] = ("Append", (q, o) => q.Append(new PersonDetailEntity()).ToList()),
                ["Prepend"] = ("Prepend", (q, o) => q.Prepend(new PersonDetailEntity()).ToList()),
                ["SequenceEqual"] = ("SequenceEqual", (q, o) => q.SequenceEqual(o)),
                ["Chunk"] = ("Chunk", (q, o) => q.Chunk(2).ToList()),
                ["Contains"] = ("Contains", (q, o) => q.Contains(new PersonDetailEntity())),
                ["Aggregate"] = ("Aggregate", (q, o) => q.Aggregate((a, b) => a)),
                ["DistinctBy"] = ("DistinctBy", (q, o) => q.DistinctBy(p => p.FirstName).ToList()),
                ["MinBy"] = ("MinBy", (q, o) => q.MinBy(p => p.Id)),
                ["MaxBy"] = ("MaxBy", (q, o) => q.MaxBy(p => p.Id)),
                ["ParameterlessMin"] = ("Min", (q, o) => q.Min()),
                ["ParameterlessMax"] = ("Max", (q, o) => q.Max()),
                ["IndexedWhere"] = ("Where", (q, o) => q.Where((p, i) => i >= 0).ToList()),
                ["IndexedSelect"] = ("Select", (q, o) => q.Select((p, i) => p).ToList()),
                ["OrderByWithComparer"] = ("OrderBy", (q, o) => q.OrderBy(p => p.FirstName, StringComparer.Ordinal).ToList()),
                ["ThenByWithComparer"] = ("ThenBy", (q, o) => q.OrderBy(p => p.Id).ThenBy(p => p.FirstName, StringComparer.Ordinal).ToList()),
                ["DistinctWithComparer"] = ("Distinct", (q, o) => q.Distinct(EqualityComparer<PersonDetailEntity>.Default).ToList()),
                ["FirstOrDefaultWithDefault"] = ("FirstOrDefault", (q, o) => q.FirstOrDefault(new PersonDetailEntity())),
                ["SingleOrDefaultWithDefault"] = ("SingleOrDefault", (q, o) => q.Where(p => p.FirstName == "zzz").SingleOrDefault(new PersonDetailEntity())),
                ["LastOrDefaultWithDefault"] = ("LastOrDefault", (q, o) => q.LastOrDefault(new PersonDetailEntity())),
                ["TakeRange"] = ("Take", (q, o) => q.Take(0..2).ToList()),
                ["ScalarProjection_ParameterlessSum"] = ("Sum", (q, o) => q.Select(p => p.Id).Sum()),
            };

        public static IEnumerable<object[]> RejectedShapeRows => RejectedShapes.Keys.Select(k => new object[] { k });

        [DataTestMethod]
        [DynamicData(nameof(RejectedShapeRows))]
        public void Rejected_Operator_ThrowsNotSupported_NamesOperator_NoQueryExecuted(string shape)
        {
            var (marker, _) = SeedAbc();
            var (op, run) = RejectedShapes[shape];
            var query = People(marker);
            var other = People(marker);

            var ex = AssertThrowsNoQuery<NotSupportedException>(() => run(query, other));
            StringAssert.Contains(ex.Message, op);
            // The policy's message, not an older guard that happens to name the operator (AC13-8).
            StringAssert.Contains(ex.Message, PolicyMessage);
        }

        [TestMethod]
        public void Allowed_PredicateWithCollectionContains_NotRejected()
        {
            var (marker, ids) = SeedAbc();
            var expected = new List<int> { ids[0], ids[2] };
            var list = new List<int>(expected);
            IEnumerable<int> sequence = expected;

            // Lambdas nested inside allowed operators aren't inspected: neither List<T>.Contains (an instance method)
            // nor Enumerable.Contains (a static non-Queryable method, through IEnumerable<int>) is rejected.
            var byList = People(marker).Where(p => list.Contains(p.Id)).OrderBy(p => p.FirstName).ToList();
            var bySequence = People(marker).Where(p => sequence.Contains(p.Id)).OrderBy(p => p.FirstName).ToList();

            CollectionAssert.AreEqual(expected, byList.Select(r => r.Id).ToList(), "List<T>.Contains");
            CollectionAssert.AreEqual(expected, bySequence.Select(r => r.Id).ToList(), "Enumerable.Contains");
        }

        [TestMethod]
        public void NonQueryableSpineMethod_Rejected()
        {
            var (marker, _) = SeedAbc();
            var query = People(marker).NotAQueryableOperator();

            var ex = AssertThrowsNoQuery<NotSupportedException>(() => query.ToList());
            StringAssert.Contains(ex.Message, nameof(ForeignQueryOperators.NotAQueryableOperator));
        }

        [TestMethod]
        public void NonCallNonRootSpineNode_Rejected()
        {
            var marker = NewMarker();
            var root = _provider.Query<PersonDetailEntity>();
            Expression<Func<PersonDetailEntity, bool>> predicate = p => p.LastName == marker;
            var spine = Expression.Call(typeof(Queryable), nameof(Queryable.Where), new[] { typeof(PersonDetailEntity) },
                Expression.Convert(root.Expression, typeof(IQueryable<PersonDetailEntity>)), Expression.Quote(predicate));
            var query = root.Provider.CreateQuery<PersonDetailEntity>(spine);

            AssertThrowsNoQuery<NotSupportedException>(() => query.ToList());
        }

        [TestMethod]
        public void ForeignQueryableConstantRoot_Rejected()
        {
            var (marker, _) = SeedAbc();
            var composed = People(marker); // its Expression is a Where call, not this constant
            var spine = Expression.Call(typeof(Queryable), nameof(Queryable.Take), new[] { typeof(PersonDetailEntity) },
                Expression.Constant(composed, typeof(IQueryable<PersonDetailEntity>)), Expression.Constant(1));
            var query = composed.Provider.CreateQuery<PersonDetailEntity>(spine);

            // 3.9.0 drops the composed Where and reads the whole table (on SQLite, Take's default ORDER BY rowid fails).
            AssertThrowsNoQuery<NotSupportedException>(() => query.ToList());
        }

        #endregion

        #region AC13-4 I1: predicate lambdas bind to the entity

        private static readonly string[] PredicateOperators =
        {
            "Where", "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault",
            "Any", "All", "Count", "LongCount", "SubsetSelectThenWhere", "DistinctThenWhere"
        };

        public static IEnumerable<object[]> PredicateRows =>
            from op in PredicateOperators
            from source in new[] { "object", "base" }
            from spelling in Spellings
            select new object[] { op, source, spelling };

        [DataTestMethod]
        [DynamicData(nameof(PredicateRows))]
        public void Covariant_PredicateLambda_Rejected_I1Message_NoQuery(string op, string source, string spelling)
        {
            var (marker, _) = SeedAbc();
            var run = source == "object"
                ? PredicateOperation<object>(People(marker), op, spelling, x => x != null)
                : PredicateOperation<PersonEntity>(People(marker), op, spelling, p => p.Id > 0);

            var ex = AssertThrowsNoQuery<NotSupportedException>(run);
            StringAssert.Contains(ex.Message, I1Message);
        }

        private static Action PredicateOperation<TBase>(IQueryable<PersonDetailEntity> query, string op, string spelling,
            Expression<Func<TBase, bool>> predicate) where TBase : class
        {
            var source = Convert<TBase>(query, spelling);
            switch (op)
            {
                case "Where": return () => source.Where(predicate).ToList();
                case "First": return () => source.First(predicate);
                case "FirstOrDefault": return () => source.FirstOrDefault(predicate);
                case "Single": return () => source.Single(predicate);
                case "SingleOrDefault": return () => source.SingleOrDefault(predicate);
                case "Last": return () => source.Last(predicate);
                case "LastOrDefault": return () => source.LastOrDefault(predicate);
                case "Any": return () => source.Any(predicate);
                case "All": return () => source.All(predicate);
                case "Count": return () => source.Count(predicate);
                case "LongCount": return () => source.LongCount(predicate);
                case "SubsetSelectThenWhere":
                {
                    var subset = Convert<TBase>(query.Select(p => new PersonDetailEntity { Id = p.Id, FirstName = p.FirstName }), spelling);
                    return () => subset.Where(predicate).ToList();
                }
                // After a covariant Distinct<TBase>, TSource == the source element type: only a lambda-parameter
                // check (not a TSource check) catches this.
                case "DistinctThenWhere": return () => source.Distinct().Where(predicate).ToList();
                default: throw new ArgumentOutOfRangeException(nameof(op));
            }
        }

        #endregion

        #region AC13-4 I1 out of scope: non-predicate lambdas are unchanged

        private static readonly string[] BaseClassShapes =
        {
            "OrderBy.First", "OrderByDescending.First", "OrderBy.ThenByDescending.Skip(1).First",
            "Max", "Min", "Sum", "Average", "ScalarSelect"
        };

        public static IEnumerable<object[]> UnchangedRows =>
            (from shape in BaseClassShapes.Concat(new[] { "Interface.OrderByDescending(Id).First", "Interface.Max(Id)", "Object.OrderByDescending(cast Id).First" })
             from spelling in Spellings
             select new object[] { shape, spelling });

        [DataTestMethod]
        [DynamicData(nameof(UnchangedRows))]
        public void Covariant_BaseClassSource_NonPredicateLambda_UnchangedFromConcrete(string shape, string spelling)
        {
            var (marker, _) = SeedAbc();

            switch (shape)
            {
                // These two order by Id by definition (IHasPersonId exposes only Id; J8 casts to read Id). On SQLite
                // 3.9.0 the concrete shape throws #12's "ambiguous column name: id", so they're red until Task 2.
                case "Interface.OrderByDescending(Id).First":
                    AssertSameOutcome(() => (object)People(marker).OrderByDescending(x => x.Id).First(),
                        () => Convert<IHasPersonId>(People(marker), spelling).OrderByDescending(x => x.Id).First(), shape, requireSuccess: true);
                    break;
                case "Interface.Max(Id)":
                    AssertSameOutcome(() => People(marker).Max(x => x.Id),
                        () => Convert<IHasPersonId>(People(marker), spelling).Max(x => x.Id), shape, requireSuccess: true);
                    break;
                case "Object.OrderByDescending(cast Id).First":
                    AssertSameOutcome(() => (object)People(marker).OrderByDescending(x => x.Id).First(),
                        () => Convert<object>(People(marker), spelling).OrderByDescending(x => ((PersonEntity)x).Id).First(), shape, requireSuccess: true);
                    break;
                default:
                    AssertSameOutcome(() => BaseShape(People(marker), shape),
                        () => BaseShape(Convert<PersonEntity>(People(marker), spelling), shape), shape, requireSuccess: true);
                    break;
            }
        }

        private static object BaseShape<TBase>(IQueryable<TBase> query, string shape) where TBase : PersonEntity
        {
            switch (shape)
            {
                case "OrderBy.First": return query.OrderBy(x => x.FirstName).First();
                case "OrderByDescending.First": return query.OrderByDescending(x => x.FirstName).First();
                case "OrderBy.ThenByDescending.Skip(1).First": return query.OrderBy(x => x.Gender).ThenByDescending(x => x.FirstName).Skip(1).First();
                case "Max": return query.Max(x => x.Id);
                case "Min": return query.Min(x => x.Id);
                case "Sum": return query.Sum(x => x.Id);
                case "Average": return query.Average(x => x.Id);
                case "ScalarSelect": return query.OrderBy(x => x.FirstName).Select(x => x.FirstName).ToList();
                default: throw new ArgumentOutOfRangeException(nameof(shape));
            }
        }

        [DataTestMethod]
        [DataRow("implicit")]
        [DataRow("cast")]
        public void Covariant_BaseClassSource_PagedEnumerated_MatchesOracle(string spelling)
        {
            var (marker, _) = SeedAbc();

            // 3.9.0: InvalidCastException (N3); I2 decides collection-ness by the expression's shape. On SQLite 3.9.0 the
            // default ORDER BY rowid fails first (AC12-7).
            AssertMatchesOracle(marker, q => Convert<PersonEntity>(q, spelling).Take(5).ToList());
        }

        [DataTestMethod]
        [DataRow("Where", "none")]
        [DataRow("Where", "implicit")]
        [DataRow("Where", "cast")]
        [DataRow("Count", "none")]
        [DataRow("Count", "implicit")]
        [DataRow("Count", "cast")]
        public void Covariant_ScalarSource_Lambda_KeepsCompositionMessage(string op, string spelling)
        {
            var (marker, _) = SeedAbc();
            Action run;
            if (spelling == "none")
            {
                var scalar = People(marker).Select(p => p.FirstName);
                run = op == "Where" ? () => scalar.Where(x => x != null).ToList() : () => scalar.Count(x => x != null);
            }
            else
            {
                var converted = ConvertScalar(People(marker).Select(p => p.FirstName), spelling);
                run = op == "Where" ? () => converted.Where(x => x != null).ToList() : () => converted.Count(x => x != null);
            }

            var ex = AssertThrowsNoQuery<NotSupportedException>(run);
            StringAssert.Contains(ex.Message, CompositionMessage, "I1 must not mask the composition message after a scalar Select");
        }

        #endregion

        #region AC13-4 I2: scalar terminals, entity enumeration

        private static readonly string[] SingleRowTerminals = { "First", "FirstOrDefault", "Single", "SingleOrDefault" };
        private static readonly string[] AllRowTerminals = { "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault" };

        public static IEnumerable<object[]> ScalarTerminalRows =>
            (from t in AllRowTerminals select (t, "direct"))
            .Concat(from t in SingleRowTerminals select (t, "afterTake"))
            .Concat(from t in SingleRowTerminals select (t, "afterSkip"))
            .Concat(from t in AllRowTerminals select (t, "afterDistinct"))
            .SelectMany(r => Spellings.Select(s => new object[] { r.Item1, r.Item2, s }));

        [DataTestMethod]
        [DynamicData(nameof(ScalarTerminalRows))]
        public void Covariant_ScalarSource_Terminal_Rejected_NoQuery(string terminal, string position, string spelling)
        {
            var (marker, _) = SeedAbc();
            var converted = ConvertScalar(People(marker).Select(p => p.FirstName), spelling);
            var source = AtPosition(converted, position);

            // 3.9.0 returns the whole projected list as the "row". (On SQLite, after Take/Skip, the unordered paging SQL
            // fails first: AC12-7's ambiguous rowid, AC13-14's OFFSET without LIMIT.)
            var ex = AssertThrowsNoQuery<NotSupportedException>(() => RunTerminal(source, terminal));
            StringAssert.Contains(ex.Message, ScalarGuardMessage);
        }

        [DataTestMethod]
        [DataRow("Count", "implicit")]
        [DataRow("Count", "cast")]
        [DataRow("Any", "implicit")]
        [DataRow("Any", "cast")]
        public void Covariant_ScalarSource_CountAny_Regression(string terminal, string spelling)
        {
            var (marker, _) = SeedAbc();
            var converted = ConvertScalar(People(marker).Select(p => p.FirstName), spelling);

            var ex = AssertThrowsNoQuery<NotSupportedException>(() => RunTerminal(converted, terminal));
            StringAssert.Contains(ex.Message, ScalarGuardMessage);
        }

        public static IEnumerable<object[]> ScalarSequenceRows =>
            from shape in new[] { "Skip.Take", "Take", "Distinct", "enumerated" }
            from spelling in Spellings
            select new object[] { shape, spelling };

        /// <summary>
        /// Left alone (P7b–e): lambda-free sequence operators and plain enumeration over a converted scalar source are
        /// correct in 3.9.0 and must stay so. Kills a guard that rejects valid enumeration, and a policy that rejects
        /// lambda-free operators over a converted source.
        /// </summary>
        [DataTestMethod]
        [DynamicData(nameof(ScalarSequenceRows))]
        public void Covariant_ScalarSource_SequenceOrEnumeration_MatchesOracle(string shape, string spelling)
        {
            var (marker, _) = SeedAbc();

            AssertMatchesOracle(marker, q =>
            {
                switch (shape)
                {
                    case "Skip.Take": return ConvertScalar(q.OrderBy(p => p.FirstName).Select(p => p.FirstName), spelling).Skip(1).Take(1).ToList();
                    case "Take": return ConvertScalar(q.OrderBy(p => p.FirstName).Select(p => p.FirstName), spelling).Take(2).ToList();
                    // Every seeded row shares the marker LastName: Distinct collapses three rows to one.
                    case "Distinct": return ConvertScalar(q.Select(p => p.LastName), spelling).Distinct().ToList();
                    case "enumerated": return ConvertScalar(q.OrderBy(p => p.FirstName).Select(p => p.FirstName), spelling).ToList();
                    default: throw new ArgumentOutOfRangeException(nameof(shape));
                }
            }, $"{shape} {spelling}");
        }

        [TestMethod]
        public void ScalarDistinctLast_ScalarGuardWins()
        {
            var (marker, _) = SeedAbc();
            var query = People(marker).Select(p => p.FirstName).Distinct();

            var ex = AssertThrowsNoQuery<NotSupportedException>(() => query.Last());
            StringAssert.Contains(ex.Message, ScalarGuardMessage);
        }

        public static IEnumerable<object[]> EntityEnumerationRows =>
            from position in new[] { "direct", "afterTake", "afterSkip", "afterDistinct" }
            from spelling in Spellings
            select new object[] { position, spelling };

        [DataTestMethod]
        [DynamicData(nameof(EntityEnumerationRows))]
        public void Covariant_EntitySource_Enumerated_MatchesOracle(string position, string spelling)
        {
            var (marker, _) = SeedAbc();

            AssertMatchesOracle(marker, q => AtPosition(Convert<object>(q.OrderBy(p => p.FirstName), spelling), position).ToList());
        }

        private static IQueryable<object> AtPosition(IQueryable<object> source, string position)
        {
            switch (position)
            {
                case "direct": return source;
                case "afterTake": return source.Take(5);
                case "afterSkip": return source.Skip(1);
                case "afterDistinct": return source.Distinct();
                default: throw new ArgumentOutOfRangeException(nameof(position));
            }
        }

        private static object RunTerminal(IQueryable<object> source, string terminal)
        {
            switch (terminal)
            {
                case "First": return source.First();
                case "FirstOrDefault": return source.FirstOrDefault();
                case "Single": return source.Single();
                case "SingleOrDefault": return source.SingleOrDefault();
                case "Last": return source.Last();
                case "LastOrDefault": return source.LastOrDefault();
                case "Count": return source.Count();
                case "LongCount": return source.LongCount();
                case "Any": return source.Any();
                default: throw new ArgumentOutOfRangeException(nameof(terminal));
            }
        }

        #endregion

        #region AC13-4 left alone: parameterless terminals over a converted entity source

        private static readonly string[] EntityTerminals = { "Count", "LongCount", "Any", "First", "FirstOrDefault", "Single", "Last" };

        public static IEnumerable<object[]> ParameterlessTerminalRows =>
            (from t in EntityTerminals
             from p in new[] { "root", "afterWhere", "afterOrderBy", "afterSubsetSelect" }
             select (t, p))
            .Concat(from t in new[] { "First", "FirstOrDefault", "Single" } from p in new[] { "afterTake", "afterSkip" } select (t, p))
            .Concat(from t in new[] { "First", "FirstOrDefault", "Single", "Last" } select (t, "afterDistinct"))
            .SelectMany(r => Spellings.Select(s => new object[] { r.Item1, r.Item2, s }));

        [DataTestMethod]
        [DynamicData(nameof(ParameterlessTerminalRows))]
        public void Covariant_EntitySource_ParameterlessTerminal_MatchesOracle(string terminal, string position, string spelling)
        {
            var (marker, _) = SeedAbc();

            if (position == "root")
            {
                // At the root nothing can scope the query before the conversion: compare with the concrete query.
                if (terminal == "Single")
                {
                    // Single over the whole table must throw "more than one element", on both sides.
                    Assert.ThrowsException<InvalidOperationException>(() => RunTerminal(_provider.Query<PersonDetailEntity>(), terminal));
                    Assert.ThrowsException<InvalidOperationException>(() =>
                        RunTerminal(Convert<object>(_provider.Query<PersonDetailEntity>(), spelling), terminal));
                    return;
                }
                AssertSameOutcome(() => RunTerminal(_provider.Query<PersonDetailEntity>(), terminal),
                    () => RunTerminal(Convert<object>(_provider.Query<PersonDetailEntity>(), spelling), terminal), terminal + " at root",
                    requireSuccess: true);
                return;
            }

            AssertMatchesOracle(marker, q =>
            {
                // First*/Single* are pre-filtered to one row so the outcome is order-independent.
                var scoped = terminal == "Last" || terminal == "Count" || terminal == "LongCount" || terminal == "Any"
                    ? q
                    : q.Where(p => p.FirstName == "b");
                IQueryable<PersonDetailEntity> before;
                switch (position)
                {
                    case "afterWhere": before = scoped; break;
                    case "afterOrderBy": before = scoped.OrderBy(p => p.FirstName); break;
                    case "afterSubsetSelect": before = scoped.Select(p => new PersonDetailEntity { Id = p.Id, FirstName = p.FirstName }); break;
                    case "afterTake": return RunTerminal(Convert<object>(scoped.OrderBy(p => p.FirstName), spelling).Take(5), terminal);
                    case "afterSkip": return RunTerminal(Convert<object>(scoped.OrderBy(p => p.FirstName), spelling).Skip(0), terminal);
                    case "afterDistinct": return RunTerminal(Convert<object>(scoped, spelling).Distinct(), terminal);
                    default: throw new ArgumentOutOfRangeException(nameof(position));
                }
                return RunTerminal(Convert<object>(before, spelling), terminal);
            }, $"{terminal} {position} {spelling}");
        }

        [DataTestMethod]
        [DataRow("Skip(1).First", "implicit")]
        [DataRow("Skip(1).First", "cast")]
        [DataRow("Take(5).Count", "implicit")]
        [DataRow("Take(5).Count", "cast")]
        [DataRow("Take(5).Where", "implicit")]
        [DataRow("Take(5).Where", "cast")]
        public void Covariant_EntitySource_AfterPaging_D8Governs(string shape, string spelling)
        {
            var (marker, _) = SeedAbc();

            switch (shape)
            {
                case "Skip(1).First":
                    AssertMatchesOracle(marker, q => Convert<object>(q.OrderBy(p => p.FirstName), spelling).Skip(1).First());
                    break;
                case "Take(5).Count":
                {
                    var source = Convert<object>(People(marker).OrderBy(p => p.Id), spelling);
                    var ex = AssertThrowsNoQuery<NotSupportedException>(() => source.Take(5).Count());
                    StringAssert.Contains(ex.Message, PagingMessage);
                    break;
                }
                case "Take(5).Where":
                {
                    var source = Convert<object>(People(marker).OrderBy(p => p.Id), spelling);
                    var ex = AssertThrowsNoQuery<NotSupportedException>(() => source.Take(5).Where(x => x != null).ToList());
                    StringAssert.Contains(ex.Message, PagingMessage, "D8 precedes I1");
                    break;
                }
                default: throw new ArgumentOutOfRangeException(nameof(shape));
            }
        }

        [TestMethod]
        public void Covariant_OtherProjectedSource_SelectShapeGuardMessage()
        {
            var (marker, _) = SeedAbc();
            var query = People(marker).Select(p => new PersonEntity { FirstName = p.FirstName }).Where(p => p.FirstName != null);

            var ex = AssertThrowsNoQuery<NotSupportedException>(() => query.ToList());
            StringAssert.Contains(ex.Message, SelectShapeMessage, "I1 must not mask the Select-shape message after a DTO Select");
        }

        #endregion

        #region AC13-8 messages and precedence

        [TestMethod]
        public void GroupBy_Rejected_KeepsDedicatedMessage()
        {
            var (marker, _) = SeedAbc();
            var query = People(marker).GroupBy(p => p.Gender);

            var ex = AssertThrowsNoQuery<NotSupportedException>(() => query.ToList());
            StringAssert.Contains(ex.Message, "GroupBy is not supported in this version");
        }

        // New in the siblings (§4.2 AC13-8): SQL Server has this test in its existing ScalarProjectionTests.
        [TestMethod]
        public void ScalarProjection_WithReducingTerminals_ThrowNotSupported()
        {
            var (marker, _) = SeedAbc();

            Assert.ThrowsException<NotSupportedException>(() => People(marker).Select(p => p.Id).LongCount());
            Assert.ThrowsException<NotSupportedException>(() => People(marker).Select(p => p.Id).ElementAt(0));
            Assert.ThrowsException<NotSupportedException>(() => People(marker).Select(p => p.Id).Contains(1));
            Assert.ThrowsException<NotSupportedException>(() => People(marker).Select(p => p.Id).Aggregate((a, b) => a + b));
        }

        [TestMethod]
        public void ScalarProjection_WithSingleOrLast_ThrowsNotSupported()
        {
            var (marker, _) = SeedAbc();

            Assert.ThrowsException<NotSupportedException>(() => People(marker).Select(p => p.Id).Single());
            Assert.ThrowsException<NotSupportedException>(() => People(marker).Select(p => p.Id).SingleOrDefault());
            Assert.ThrowsException<NotSupportedException>(() => People(marker).Select(p => p.Id).Last());
            Assert.ThrowsException<NotSupportedException>(() => People(marker).Select(p => p.Id).LastOrDefault());
        }

        [TestMethod]
        public void Rejected_OperatorOuterToFailingInnerOperator_PolicyMessageWins()
        {
            var (marker, _) = SeedAbc();
            var query = People(marker).Select(p => p.Id).Where(x => x > 0).Reverse();

            var ex = AssertThrowsNoQuery<NotSupportedException>(() => query.ToList());
            StringAssert.Contains(ex.Message, "Reverse");
            StringAssert.Contains(ex.Message, PolicyMessage);
        }

        [TestMethod]
        public void Rejected_AllowListFailureBeatsPass2Failure()
        {
            var (marker, _) = SeedAbc();
            var query = People(marker).Take(5).Where(p => p.Id > 0).Reverse();

            var ex = AssertThrowsNoQuery<NotSupportedException>(() => query.ToList());
            StringAssert.Contains(ex.Message, "Reverse");
            Assert.IsFalse(ex.Message.Contains(PagingMessage), "the allow-list failure must win over D8: " + ex.Message);
        }

        [TestMethod]
        public void Rejected_OutermostAllowListFailureWins()
        {
            var (marker, _) = SeedAbc();
            var query = People(marker).Reverse().TakeWhile(p => p.Id > 0);

            // Pass 1 walks outer→inner; with two allow-list failures the outermost (TakeWhile) is reported.
            var ex = AssertThrowsNoQuery<NotSupportedException>(() => query.ToList());
            StringAssert.Contains(ex.Message, "TakeWhile");
            Assert.IsFalse(ex.Message.Contains("Reverse"), "the outermost failure wins: " + ex.Message);
        }

        [TestMethod]
        public void Rejected_Pass2InnerFailureWins()
        {
            var (marker, _) = SeedAbc();
            var query = People(marker).Cast<Domain.Entities.Address.AddressEntity>().Take(1);

            // Pass 2 walks inner→outer: the unrelated-type Cast (D5) is met before Count-after-Take (D8).
            var ex = AssertThrowsNoQuery<NotSupportedException>(() => query.Count());
            StringAssert.Contains(ex.Message, CastMessage);
            Assert.IsFalse(ex.Message.Contains(PagingMessage), "the inner D5 failure wins over the outer D8 one: " + ex.Message);

            // An outer failure that doesn't depend on inner state (I1 on the unrelated type's lambda) must not win either:
            // a walk that met outer nodes first would report I1 here.
            var withPredicate = People(marker).Cast<Domain.Entities.Address.AddressEntity>().Where(a => a.Id > 0);
            var ex2 = AssertThrowsNoQuery<NotSupportedException>(() => withPredicate.ToList());
            StringAssert.Contains(ex2.Message, CastMessage);
            Assert.IsFalse(ex2.Message.Contains(I1Message), "the inner D5 failure wins over the outer I1 one: " + ex2.Message);
        }

        #endregion

        #region AC13-10 after Skip/Take: rejected rows

        private static readonly Dictionary<string, Func<IQueryable<PersonDetailEntity>, object>> RejectedAfterPaging =
            new Dictionary<string, Func<IQueryable<PersonDetailEntity>, object>>
            {
                ["Count"] = q => q.Take(2).Count(),
                ["LongCount"] = q => q.Take(2).LongCount(),
                ["Any"] = q => q.Take(2).Any(),
                ["All"] = q => q.Take(2).All(p => p.Id > 0),
                ["Sum"] = q => q.Take(2).Sum(p => p.Id),
                ["Average"] = q => q.Take(2).Average(p => p.Id),
                ["Min"] = q => q.Take(2).Min(p => p.Id),
                ["Max"] = q => q.Take(2).Max(p => p.Id),
                ["Where"] = q => q.Take(2).Where(p => p.Id > 0).ToList(),
                ["First(pred)"] = q => q.Take(2).First(p => p.Id > 0),
                ["Single(pred)"] = q => q.Take(2).Single(p => p.Id > 0),
                ["Last"] = q => q.OrderBy(p => p.Id).Take(2).Last(),
                ["OrderBy"] = q => q.Take(2).OrderBy(p => p.Id).ToList(),
                ["OrderByDescending"] = q => q.Take(2).OrderByDescending(p => p.Id).ToList(),
                ["OrderBy(a).Skip(n).OrderBy(b)"] = q => q.OrderBy(p => p.FirstName).Skip(1).OrderBy(p => p.Id).ToList(),
                ["Distinct"] = q => q.Take(2).Distinct().ToList(),
                ["Skip-after-Skip"] = q => q.OrderBy(p => p.Id).Skip(1).Skip(1).ToList(),
                ["Take-after-Take"] = q => q.Take(2).Take(1).ToList(),
                ["Take-then-Skip"] = q => q.OrderBy(p => p.Id).Take(2).Skip(1).ToList(),
            };

        public static IEnumerable<object[]> RejectedAfterPagingRows => RejectedAfterPaging.Keys.Select(k => new object[] { k });

        [DataTestMethod]
        [DynamicData(nameof(RejectedAfterPagingRows))]
        public void Operator_AfterPaging_Rejected_BeforeAnyQuery(string shape)
        {
            var (marker, _) = SeedAbc();
            var query = People(marker);

            var ex = AssertThrowsNoQuery<NotSupportedException>(() => RejectedAfterPaging[shape](query));
            StringAssert.Contains(ex.Message, PagingMessage, "D8 wins, including over D10");
        }

        #endregion

        #region AC13-12 a second OrderBy

        private static readonly Dictionary<string, Func<IQueryable<PersonDetailEntity>, object>> SecondOrderings =
            new Dictionary<string, Func<IQueryable<PersonDetailEntity>, object>>
            {
                ["OrderBy.OrderBy"] = q => q.OrderBy(p => p.FirstName).OrderBy(p => p.Id).ToList(),
                ["OrderBy.ThenBy.OrderByDescending"] = q => q.OrderBy(p => p.FirstName).ThenBy(p => p.Id).OrderByDescending(p => p.Id).ToList(),
                ["OrderBy.Where.OrderBy"] = q => q.OrderBy(p => p.FirstName).Where(p => p.Id > 0).OrderBy(p => p.Id).ToList(),
                ["OrderBy.Select(subset).OrderBy"] = q => q.OrderBy(p => p.FirstName)
                    .Select(p => new PersonDetailEntity { Id = p.Id, FirstName = p.FirstName }).OrderBy(p => p.Id).ToList(),
                ["OrderBy.Distinct.OrderBy"] = q => q.OrderBy(p => p.FirstName).Distinct().OrderBy(p => p.Id).ToList(),
            };

        public static IEnumerable<object[]> SecondOrderingRows => SecondOrderings.Keys.Select(k => new object[] { k });

        [DataTestMethod]
        [DynamicData(nameof(SecondOrderingRows))]
        public void Ordering_AfterEarlierOrdering_Rejected_BeforeAnyQuery(string shape)
        {
            var (marker, _) = SeedAbc();
            var query = People(marker);

            var ex = AssertThrowsNoQuery<NotSupportedException>(() => SecondOrderings[shape](query));
            StringAssert.Contains(ex.Message, SecondOrderByMessage);
        }

        #endregion
    }

    /// <summary>A method that composes a query but isn't a <see cref="Queryable"/> operator (AC13-4).</summary>
    public static class ForeignQueryOperators
    {
        public static IQueryable<T> NotAQueryableOperator<T>(this IQueryable<T> source) =>
            source.Provider.CreateQuery<T>(Expression.Call(
                typeof(ForeignQueryOperators).GetMethod(nameof(NotAQueryableOperator)).MakeGenericMethod(typeof(T)),
                source.Expression));
    }
}
