# Query Operator Correctness (#12, #13) — Implementation Plan

> **Goal**: Fix the two query-translation defects logged after 3.9.0 — [#12](https://github.com/FuncularLabs/Funcular.FunkyOrm/issues/12)
> (unqualified own-column `ORDER BY` on remote-join entities) and [#13](https://github.com/FuncularLabs/Funcular.FunkyOrm/issues/13)
> (LINQ operators silently ignored) — across all four providers, test-first. Target release: **v3.10.0**
> (branch `development/3.10`). This is a **prerequisite for the sponsored-tutorial program**: a tutorial
> audience coming from EF will reach for `SingleOrDefault` and the narrow-projection paging idiom on day one.

> **Status (2026-09-30)**: Decisions D1–D7 **approved by the owner as recommended**. Task 0 in progress:
> premises re-verified on all four providers, baselines recorded; awaiting the non-author test-plan review.

> **Revision 1 (Task 0 results, 2026-09-30) — what changed:**
> - §1.3 (new): #12 and #13 re-verified on **PostgreSQL, MySQL and SQLite**. #13 is identical everywhere.
>   #12 is **worse on SQLite**: full-entity `OrderBy(Id)` also fails, and **any `Skip`/`Take` on a
>   remote-join entity fails** (`ambiguous column name: rowid`).
> - AC12-7 is no longer conditional, and Task 3 is required. AC12-1 on SQLite also covers the full-entity case
>   (already listed).
> - D4 resolved without Docker: a native PostgreSQL 18 service is running locally, and the PG suite's
>   built-in fallback connection (`funky_db` / `funky_user`) reaches it. CI uses `postgres:17`.
> - §4.5: PostgreSQL baseline added. `PostgreSqlOrderByClauseVisitor.cs` is at 35.6%.
> - §4.1: two harness rules added from probe mishaps: cleanup deletes need a transaction, and subset
>   projections don't populate `Id`.
> - §1.1: earliest affected version pinned. `Single*` and parameterless `Last()` have never been handled,
>   since the first LINQ provider (`c9837a4`, 2025-04-29), so every NuGet release from `0.1.1-alpha.2`
>   through `3.9.0` is affected.

---

## 1. Verified premises (probe, 2026-09-30)

Both issues were filed from code reading. Before planning, a throwaway probe ran each shape against live
SQL Server (`FUNKY_CONNECTION`, 8,369 `person` rows, ids 1…8673). The probe was deleted afterwards; its
cases become the red tests in §4. **The findings change the scope of both issues.**

### 1.1 #13 is broader and more severe than filed

| Query (SQL Server, 3.9.0) | Correct result | FunkyORM 3.9.0 returns | SQL actually sent |
|---|---|---|---|
| `Single(p => p.Id == 8673)` | row 8673 | **row 1** | `SELECT … FROM person` (predicate dropped) |
| `SingleOrDefault(p => p.Id == -1)` | `null` | **row 1** | predicate dropped |
| `Single()` over 8,369 rows | throws "more than one element" | **row 1** | no cardinality check |
| `Where(p => p.Id == -1).Single()` | throws "no elements" | **`null`** | no cardinality check |
| `Last()` / `OrderBy(Id).Last()` | row 8673 | **row 1** | `ORDER BY id ASC`, first row read |
| `OrderBy(Id).Reverse().ToList()` | descending | ascending | `Reverse` dropped |
| `TakeWhile(p => p.Id < 0).ToList()` | 0 rows | **8,369 rows** | dropped |
| `SkipWhile(p => p.Id > 0).ToList()` | 0 rows | **8,369 rows** | dropped |
| `OrderBy(Id).TakeLast(1).ToList()` | 1 row | **8,369 rows** | dropped |
| `OrderBy(Id).ElementAt(1)` | 2nd row | **row 1** | dropped |
| `Where(Id==-1).DefaultIfEmpty().ToList()` | 1 element (`null`) | 0 elements | dropped |
| `q1.Concat(q2).ToList()` | 2 rows | 1 row | second query dropped |
| `LongCount()` | `8369L` | `InvalidCastException` | full `SELECT *` |
| *(sanity)* `First()`, `FirstOrDefault(pred)`, `Min(sel)`, `AsQueryable()` | — | correct | — |

Root cause: `ParseExpression` translates a fixed set of operator **names** and silently skips every other
`MethodCallExpression` in the chain; `ExecuteQuery` then reads the first row for any non-collection
`TResult`. `Single*` are never matched at all, and the `First/Last` predicate branch requires
`Arguments.Count == 2`, so parameterless `Last()` falls through to "read first row".

**Severity: critical (silent wrong row).** `Query<User>().SingleOrDefault(u => u.Email == email)` returns
an arbitrary *different* user. In a consuming app that is an authorization flaw, not just a bug.
**Affected versions: every NuGet release from `0.1.1-alpha.2` through `3.9.0`.** `Single*` and parameterless
`Last()` have not been translated since the first LINQ provider (`c9837a4`, 2025-04-29); the only commits
that ever mention `Single` are the 3.9 scalar-projection guards.

### 1.2 #12 is narrower than filed on SQL Server, PostgreSQL and MySQL (broader on SQLite — see §1.3)

| Query on `PersonDetailEntity` (joins `organization`, `address`, `country` — all have `id`) | 3.9.0 result |
|---|---|
| `OrderBy(p => p.Id).Take(3).ToList()` (full entity) | ✅ works — `ORDER BY id` binds to the single `id` column in the SELECT list |
| `OrderBy(p => p.Id).Take(3).Select(p => p.MiddleInitial)` (scalar projection) | ❌ `Ambiguous column name 'id'` |
| `OrderBy(p => p.Id).Take(3).Select(p => new PersonDetailEntity { FirstName = p.FirstName })` | ❌ `Ambiguous column name 'id'` |
| `OrderBy(p => p.EmployerHeadquartersCountryName).ThenBy(p => p.Id)` + subset projection | ❌ `Ambiguous column name 'id'` (`ORDER BY [country_0].name ASC, id ASC`) |
| `OrderBy(p => p.FirstName)` + `Select(p => p.Id)` | ✅ works — only because no joined table has `first_name` |
| `Where(p => p.Id > 0)` + default paging | ✅ WHERE emits `person.id`; default order is `person.id` (3.9 round-1 fix) |

**Real trigger:** an own-column `OrderBy`/`ThenBy` combined with a **narrow projection** on a remote-join
entity, when any joined table has a column of the same name. That is exactly the performant
call-list idiom documented in 3.8.5/3.9, so it's the path tutorials and real apps take.

**Consumer exposure:** Sentinel.MVP `main` (pinned to 3.9.0-beta1) uses this exact shape in
`CallListOrder.Apply` → `.ThenBy(r => r.CallId)` → `Skip/Take` → `Select(r => new CallListQueryRow { CallId = r.CallId })`.
It works **only because** `CallId` maps to `call_id` and no joined table has that name. A new sort key on
a shared name (`id`, `uid`, `dateutc_created`) would crash that page. No Sentinel code uses any #13
operator on a FunkyORM query (scanned 30 `Query<` files).

### 1.3 Cross-provider verification (Task 0, 2026-09-30)

Same shapes on PostgreSQL (local PG 18), MySQL, and SQLite. Each probe seeded three marker-tagged rows
(target = the third) and was deleted afterwards. The join entity is `PersonDetailEntity` (PG, SQLite) or
`PersonWithEmployer` (MySQL); every joined table has an `id` column.

| Shape | SQL Server | PostgreSQL | MySQL | SQLite |
|---|---|---|---|---|
| `Single(pred → 3rd row)` | row 1 of table | row 1 of table | an unrelated row (22) | row 1 |
| `SingleOrDefault(no match)` | a row | a row | a row | a row |
| `Where(3 rows).Single()` | a row, no throw | a row | a row | a row |
| `Last()` / `OrderBy(Id).Last()` / `OrderBy(Id).LastOrDefault(pred)` | first row | first row | first row | first row |
| `OrderBy(Id).Reverse()` | ignored | ignored | ignored | ignored |
| `LongCount()` | `InvalidCastException` | `InvalidCastException` | `InvalidCastException` | `InvalidCastException` |
| #12 full entity `OrderBy(Id)` | ✅ | ✅ | ✅ | ❌ `ambiguous column name: id` |
| #12 subset / scalar projection + `OrderBy(Id)` | ❌ | ❌ `42702` | ❌ | ❌ |
| #12 remote `OrderBy` + `ThenBy(Id)` + subset | ❌ | ❌ | ❌ | ❌ |
| AC12-7 default paging (`Take`) on join entity | ✅ `person.id` | ✅ `person.id` | ✅ `person.id` | ❌ `ambiguous column name: rowid` |

**Consequence for SQLite:** on 3.9.0, *any* `Skip`/`Take` on an entity with remote attributes fails,
whether or not the query has an explicit order.

### 1.4 Code facts the design relies on

- Each provider has its own order-by visitor (`OrderByClauseVisitor`, `PostgreSqlOrderByClauseVisitor`,
  `MySqlOrderByClauseVisitor`, `SqliteOrderByClauseVisitor`). All four resolve own columns through a private
  `ResolveOrderColumn` → bare `GetColumnName(property)`; every ORDER BY path (member, nested member,
  ternary/`CASE`) funnels through it.
- All four WHERE visitors already qualify own columns as `{_tableName}.{column}` (unquoted). ORDER BY
  should use the identical form.
- Two visitor call sites per provider in `*LinqQueryProvider.ParseExpression`: the `OrderBy/ThenBy` branch
  (passes the remote map) and the `Last/LastOrDefault(pred)` synthesized `ORDER BY Id DESC` (passes no map).
  `GenerateOrderByClause` in three providers is a third caller but is dead code (only a commented-out
  reference) — untouched.
- Remote joins are entity-level: an entity with `[RemoteProperty]`/`[RemoteKey]` emits its LEFT JOINs even in
  narrow projections (the probe SQL confirms), and WHERE-induced joins only arise from remote members. So
  "this query has joins" ⇔ `ResolveRemoteJoins<T>(table).IndividualJoinClauses` is non-empty.
- SQLite's default paging order is bare `ORDER BY rowid` and did not get the 3.9 round-1 qualification.
  With joins it **is** ambiguous (§1.3), so every paged query on a remote-join entity fails on SQLite.
- SQLite stores the parsed ORDER BY in an instance field (`_lastOrderByClause`), overwritten on every parse.
- No async LINQ surface exists; `Execute<TResult>` is the only execution entry point per provider.

---

## 2. Decisions — all approved by the owner as recommended (2026-09-30)

| # | Decision | Recommendation | Why |
|---|---|---|---|
| **D1** | `Single`/`SingleOrDefault`/`Last`/`LastOrDefault`/`LongCount`: **implement** correctly, or **reject**? | **Implement.** | They're EF muscle memory (the tutorial audience); correct implementations are small and reuse the WHERE/paging machinery. Rejecting would turn silent-wrong into loud-but-unhelpful for the most common calls. |
| **D2** | Allow-list form | **Exact-overload allow-list in Core** (`QueryOperatorPolicy`), matched on `MethodInfo.GetGenericMethodDefinition()`, run as a **pre-pass** over the whole chain before any translation. | Name-only lists miss overload traps that exist today: indexed `Where((x,i)=>…)` (→ `InvalidCastException`), `OrderBy(key, comparer)` and `Distinct(comparer)` (comparer silently ignored), `Take(Range)` (→ `InvalidCastException`), `FirstOrDefault(defaultValue)` (default ignored). One Core policy removes the four-way mirror-drift risk that drove the 3.9 review rounds. Pre-pass guarantees rejection happens before *any* SQL, including schema discovery. |
| **D3** | Version / branch | **3.10.0** on `development/3.10`; ship `3.10.0-beta1`, promote to stable after the Sentinel call list is smoke-tested on it. | Behavior change (throws where it used to return). The branch must match `development/**` so the PostgreSQL workflow runs (it doesn't trigger on `release/*` or on PRs to `master`). |
| **D4** | PostgreSQL red→green + coverage (no local `FUNKY_PG_CONNECTION`) | **Local Postgres** for the dev loop; CI as the second gate. *Resolved in Task 0: the native PostgreSQL 18 service is already running and the suite's fallback connection (`funky_db`/`funky_user`) reaches it — no Docker needed. CI uses `postgres:17`.* | The test-first rule needs red→green observed on every provider; CI-only makes each red/green cycle a push. |
| **D5** | `Cast<T>()` / `OfType<T>()` | **Allow identity only** (`T` = element type) as a no-op; reject any other type. | Identity is already correct today; rejecting it would be a pointless regression. Non-identity is silently wrong today. |
| **D6** | `Last*` or `Single*` combined with `Skip`/`Take` | `Single*`: allowed (fetch `min(Take, 2)`). `Last*` with `Skip`/`Take`: **reject** (`NotSupportedException`: materialize, or invert your `OrderBy` and use `First`). `ElementAt*`: **reject** (use `Skip(n).First()`). | Inverting an ORDER BY under an OFFSET changes which window is read; correct translation is a subquery — not worth it for a rare shape. |
| **D7** | Disclosure of #13 | Changelog + README "Upgrade strongly recommended" line; **consider a GitHub Security Advisory** for the `Single*` predicate drop. | The failure mode is "returns a different user's row". Owner's call on the advisory. |

---

## 3. Acceptance criteria

These will be posted back to #12 and #13 (Task 0), since the issues carry only repro steps.
Unless stated otherwise, each AC holds for **all four providers**.

### #12 — own-column ORDER BY on remote-join entities

- **AC12-1** On an entity with remote joins, `OrderBy`/`OrderByDescending`/`ThenBy`/`ThenByDescending`
  over an own mapped column emits `{baseTable}.{column}`. The query executes and returns rows in the requested
  order, for: full entity; subset projection `Select(x => new T { … })`; scalar projection
  `Select(x => x.M)`; each with and without `Skip`/`Take`.
- **AC12-2** Ordering by a remote/computed member still emits its resolved fragment
  (`[country_0].name`, JSON accessor, expression, subquery), unchanged from 3.9.0.
- **AC12-3** For an entity **without** remote joins, the generated SQL is byte-identical to 3.9.0 (bare
  column names).
- **AC12-4** A ternary/`CASE` ordering over own columns on a join entity is qualified inside the `CASE`.
- **AC12-5** The ORDER BY synthesized for `Last*` (AC13-2) is qualified on join entities and works with a
  narrow projection.
- **AC12-6** `Distinct()` + custom projection + own-column `OrderBy` still executes when the key is in the
  projection, and still throws the existing `InvalidOperationException` when it isn't.
- **AC12-7** *(SQLite; confirmed red in Task 0)* `Skip`/`Take` on a join entity with no explicit order
  executes, for the full entity and for a subset projection. Qualify `rowid` the way 3.9 qualified `id` for the
  other three.

### #13 — operator correctness

- **AC13-1** `Single`/`SingleOrDefault`, with and without a predicate, match LINQ-to-objects:
  - The predicate is applied as SQL `WHERE`.
  - Zero rows: `Single` throws `InvalidOperationException`; `SingleOrDefault` returns `null`.
  - Two or more rows: both throw `InvalidOperationException`.
  - They compose with prior `Where`/`OrderBy`/`Skip`/`Take`.
- **AC13-2** `Last`/`LastOrDefault`, with and without a predicate, match LINQ-to-objects:
  - They honor an explicit `OrderBy`/`ThenBy` chain (every term inverted).
  - With no ordering they default to `Id DESC`. The existing "no `Id` property" error is kept.
  - Empty: `Last` throws; `LastOrDefault` returns `null`.
  - With `Skip`/`Take` they throw `NotSupportedException` (D6).
- **AC13-3** `LongCount()` and `LongCount(pred)` return an `Int64` equal to `Count`.
- **AC13-4** Every `Queryable` overload **not** on the allow-list throws `NotSupportedException` naming the
  operator, and **no SQL is executed**: the provider `Log` receives no command, not even schema discovery.
  The rejection table (§4.2, T13-R) covers, at minimum:
  - **Ordering and slicing:** `Reverse`, `TakeWhile`, `SkipWhile`, `TakeLast`, `SkipLast`, `ElementAt`,
    `ElementAtOrDefault`, `Order`, `OrderDescending`.
  - **Set, join and sequence:** `DefaultIfEmpty`, `Concat`, `Union`, `Intersect`, `Except`, `Zip`,
    `SelectMany`, `Join`, `GroupJoin`, `Append`, `Prepend`, `SequenceEqual`, `Chunk`.
  - **Reducing:** `Contains`, `Aggregate`, `DistinctBy`, `MinBy`, `MaxBy`, and parameterless `Min()`/`Max()`
    on an entity.
  - **Type filters:** non-identity `Cast`/`OfType`.
  - **Unsupported overloads of allowed operators:** indexed `Where`/`Select`; `OrderBy`/`ThenBy` with a
    comparer; `Distinct` with a comparer; `First*`/`Single*`/`Last*` with a default-value argument;
    `Take(Range)`.
- **AC13-5** Every shape that worked correctly in 3.9.0 still works identically. The four existing suites
  stay green, and oracle tests cover each allowed overload.
- **AC13-6** *(D5)* Identity `Cast<T>()`/`OfType<T>()` are no-ops.
- **AC13-7** The allow-list is pinned: a test asserts the exact set of allowed `Queryable` overload
  definitions (count + signatures). A reflection sweep asserts every public `Queryable` method in the running
  framework is classified, so a new .NET operator is rejected by default and widening is a deliberate,
  reviewed change.
- **AC13-8** Existing specific messages are preserved: the `GroupBy` message, the post-scalar-projection
  composition message, and the scalar result-type guard (existing tests stay green unmodified).
- **AC13-9** `Advanced.md` and `FUNKYORM_AI_ADVANCED.md` carry a works/doesn't-work **operator table**;
  Changelog and README "Recent Changes" record the fix and the behavior change.

---

## 4. Test plan (written and reviewed first — Task 0)

### 4.1 Harness

- **Discriminating seed data.** Every semantics test inserts its own rows, tagged with a per-test GUID marker
  (the pattern the suites already use), and scopes every query with `Where(p => p.LastName == marker)`.
  The target row must be **neither the first nor the last by id**, and each test must say which wrong
  answer it rules out. A probe-style test that could pass on "first row" is vacuous.
- **Oracle assertion.** `AssertMatchesLinqToObjects(Func<IQueryable<T>, object> shape)` runs the same shape
  against `provider.Query<T>()` and against `provider.Query<T>().Where(marker).ToList().AsQueryable()`.
  It compares results (by id / by value / by exception type).
- **SQL capture.** `provider.Log` → `StringBuilder` (already used across the suites) for SQL-shape
  assertions and the AC13-4 "nothing executed" check.
- **Per-provider join entity.** SQL Server / PostgreSQL / SQLite use `PersonDetailEntity` (joins
  `organization`, `address`, `country`); MySQL uses `PersonWithEmployer` (joins `organization`). Every joined
  table has an `id` column, so `id` is the #12 test column (confirmed in Task 0).
- **Cleanup deletes run in a transaction.** `Delete<T>(predicate)` throws "must be performed within an
  active transaction". Wrap cleanup in `BeginTransaction()`/`CommitTransaction()`. The Task 0 PG probe leaked
  three rows this way (removed by hand).
- **Subset projections don't populate `Id`.** `Select(x => new T { FirstName = x.FirstName })` materializes
  `T` with only the projected members, so `Id` reads 0. Oracle comparisons for subset projections compare the
  projected members, never `Id`. Where order matters, project the ordering key too.
- **SQLite suites create their own temp database** in `[ClassInitialize]` (no shared fixture). New SQLite test
  classes copy that schema block, or share it through a small helper added in Task 1.

### 4.2 AC → test matrix

Class names are per provider: `OrderByQualificationTests`, `QueryOperatorSemanticsTests`, and
`QueryOperatorRejectionTests` in each of the four test projects (prefixed `PostgreSql`/`MySql`/`Sqlite` in the
siblings, per convention). `QueryOperatorPolicyTests` lives in `Funcular.Data.Orm.SqlServer.Tests`; it's
DB-free and exercises Core.

| AC | Test(s) | Project(s) |
|---|---|---|
| AC12-1 | `OwnColumnOrderBy_FullEntity_Qualified_ReturnsOrdered`, `OwnColumnOrderBy_SubsetProjection_Paged_Qualified_Executes`, `OwnColumnOrderBy_ScalarProjection_Paged_Qualified_Executes`, `RemoteOrderBy_ThenByOwnColumn_SubsetProjection_Executes`, `OrderByDescending_OwnColumn_ScalarProjection_OrderIsDescending` | all 4 |
| AC12-2 | `RemoteMemberOrderBy_EmitsResolvedFragment_Unchanged`, `ComputedMemberOrderBy_EmitsExpression_Unchanged` | all 4 |
| AC12-3 | `SingleTableEntity_OrderBy_SqlByteIdenticalTo390` (golden-string assertion for `PersonEntity`) | all 4 |
| AC12-4 | `TernaryOrderBy_OwnColumns_OnJoinEntity_QualifiedInsideCase` | all 4 |
| AC12-5 | `Last_OnJoinEntity_NarrowProjection_SynthesizedOrderQualified` | all 4 |
| AC12-6 | `Distinct_Projection_OrderByKeyInProjection_Executes`, `Distinct_Projection_OrderByKeyNotInProjection_ThrowsExisting` | all 4 |
| AC12-7 | `DefaultPaging_OnJoinEntity_Executes`, `DefaultPaging_OnJoinEntity_SubsetProjection_Executes` | SQLite (the other three keep these as regression rows) |
| AC13-1 | `Single_Predicate_ReturnsTargetNotFirst`, `SingleOrDefault_Predicate_NoMatch_ReturnsNull`, `Single_NoMatch_Throws`, `Single_TwoMatches_Throws`, `SingleOrDefault_TwoMatches_Throws`, `Single_AfterWhereOrderBySkipTake_MatchesOracle` | all 4 |
| AC13-2 | `Last_Parameterless_Unordered_ReturnsMaxId`, `Last_AfterOrderBy_ReturnsLastInOrder`, `Last_AfterOrderByThenByDescending_InvertsEveryTerm`, `LastOrDefault_Predicate_WithExplicitOrderBy_MatchesOracle` (3.9.0 returns the *first*), `Last_Empty_Throws`, `LastOrDefault_Empty_ReturnsNull`, `Last_WithSkipOrTake_ThrowsNotSupported`, existing PG `LastOrDefault(x => …guid…)` stays green | all 4 |
| AC13-3 | `LongCount_EqualsCount_ReturnsInt64`, `LongCount_Predicate_EqualsCountPredicate` | all 4 |
| AC13-4 | `[DataTestMethod] Rejected_Operator_ThrowsNotSupported_NamesOperator_NoSqlExecuted` — one row per AC13-4 shape | all 4 |
| AC13-5 | `[DataTestMethod] Allowed_Operator_MatchesOracle` — one row per allowed overload family; plus the four full existing suites | all 4 |
| AC13-6 | `Cast_Identity_IsNoOp`, `OfType_Identity_IsNoOp`, `Cast_NonIdentity_Throws` | all 4 |
| AC13-7 | `SupportedOperators_ExactSetPinned`, `EveryQueryableMethod_IsClassified_UnknownDefaultsToReject` | SqlServer.Tests (Core) |
| AC13-8 | Existing `GroupBy_IsNotTranslated_ThrowsNotSupported`, `ScalarProjection_ThenComposingLambdaOperators_ThrowClearNotSupported`, `ScalarProjection_WithReducingTerminals_ThrowNotSupported` — **unmodified** | all 4 |
| AC13-9 | Doc review in the Task 10 gauntlet (not an executable test; recorded as such) | — |

### 4.3 Interface coverage (new/changed members → tests)

| Member | Tests that call it on purpose |
|---|---|
| Core `QueryOperatorPolicy.EnsureSupported(Expression chain, Type elementType)` *(new, public)* | `QueryOperatorPolicyTests.*`, every `Rejected_*`/`Allowed_*` row |
| Core `QueryOperatorPolicy.SupportedOperators` *(new, public read-only — also feeds docs)* | `SupportedOperators_ExactSetPinned` |
| `*OrderByClauseVisitor` ctor — new optional `tableQualifier` | all T12 tests; `SingleTableEntity_…ByteIdentical` (null path) |
| `*OrderByClauseVisitor.OrderByTerms` *(new: structured `(column, isDescending)` list)* | `Last_AfterOrderByThenByDescending_InvertsEveryTerm` |
| `QueryComponents.Terminal` *(new enum: None/First/Single/Last × OrDefault)* | AC13-1/AC13-2 tests |
| `QueryComponents.OrderByTerms` *(new)* | AC13-2 tests |
| `*LinqQueryProvider.ParseExpression` (pre-pass, Single/Last/LongCount branches) | AC13-* |
| `*LinqQueryProvider.ExecuteQuery` (cardinality path) | AC13-1 |
| `*LinqQueryProvider.HandleAggregateQuery` (LongCount) | AC13-3 |
| `*LinqQueryProvider.BuildAggregateClause` (LongCount = Count SQL) | AC13-3 |

### 4.4 Mutations each key test must kill (run them; record the result in the handoff)

| Mutation | Test that must fail |
|---|---|
| Revert `ResolveOrderColumn` to bare `GetColumnName` | AC12-1 projection tests (runtime `Ambiguous column name`) + SQL-shape asserts |
| Qualify unconditionally (drop the "has joins" condition) | `SingleTableEntity_OrderBy_SqlByteIdenticalTo390` |
| Don't pass the qualifier at the `Last*` synthesized site | `Last_OnJoinEntity_NarrowProjection_SynthesizedOrderQualified` |
| `Single*`: drop the predicate→WHERE | `Single_Predicate_ReturnsTargetNotFirst` |
| `Single*`: fetch 1 row instead of 2 | `Single_TwoMatches_Throws`, `SingleOrDefault_TwoMatches_Throws` |
| `SingleOrDefault` throws on empty | `SingleOrDefault_Predicate_NoMatch_ReturnsNull` |
| `Last*`: don't invert | `Last_AfterOrderBy_ReturnsLastInOrder` |
| `Last*`: invert only the first term | `Last_AfterOrderByThenByDescending_InvertsEveryTerm` |
| `LongCount` returns `Int32` boxed | `LongCount_EqualsCount_ReturnsInt64` (asserts `IsInstanceOfType(long)`) |
| Add `Reverse` to the allow-list | `Rejected_…[Reverse]` and `SupportedOperators_ExactSetPinned` |
| Match the allow-list by **name** instead of overload | `Rejected_…[IndexedWhere]`, `[OrderByWithComparer]`, `[DistinctWithComparer]`, `[TakeRange]` |
| Run the policy inside the loop instead of as a pre-pass | `Rejected_…_NoSqlExecuted` for an operator placed after an `OrderBy` on a remote-join entity (the `OrderBy` handler triggers schema discovery) |
| Allow any `Cast`/`OfType` | `Cast_NonIdentity_Throws` |

### 4.5 Coverage (coverlet already referenced in all four test projects)

Baseline measured 2026-09-30 (`dotnet test --collect:"XPlat Code Coverage"`, cobertura `line-rate`):

| Touched file | SQL Server | SQLite | MySQL | PostgreSQL |
|---|---|---|---|---|
| `*LinqQueryProvider.cs` | 87.4% | 89.6% | **82.0%** | 88.6% |
| `*OrderByClauseVisitor.cs` | **58.2%** | **59.1%** | **35.6%** | **35.6%** |
| `QueryComponents.cs` | 100% | 100% | 100% | — |
| Core `QueryOperatorPolicy.cs` | new | | | |

All touched files must reach **≥ 85%**. The visitor lift is the largest test-writing item (Task 8): the
ternary/`CASE`, nested-member, and error branches are what's uncovered. The per-file numbers (or the
cobertura XML) go into the PR.

### 4.6 Opt-in suites

- **SQL Server:** `FUNKY_CONNECTION` (.\SQL2019). **MySQL:** `FUNKY_MYSQL_CONNECTION`. Both are set locally.
- **SQLite:** file-backed, always runs.
- **PostgreSQL:** no `FUNKY_PG_CONNECTION` is set, but `PostgreSqlTestConnection` falls back to
  `Host=localhost;Database=funky_db;Username=funky_user` (or `postgres_user`/`postgres_pwd`). That reaches the
  native PostgreSQL 18 service. All 143 PG tests run locally. The PG workflow in CI (`postgres:17`) only runs
  on push to `development/**` (not on PRs to `master`), so PG red→green is observed locally *and* on the
  branch push.

---

## 5. Design

### 5.1 #12 — qualify own columns in ORDER BY when joins exist

1. Each provider's order-by visitor gains an optional ctor parameter `string tableQualifier = null`.
   `ResolveOrderColumn` becomes:
   - map hit → resolved fragment (unchanged);
   - else → `tableQualifier != null ? $"{tableQualifier}.{GetColumnName(p)}" : GetColumnName(p)`.

   Because every path (member, nested member, `CASE`) goes through `ResolveOrderColumn`, one change covers
   them all.
2. Both call sites in each `ParseExpression` resolve
   `var remote = _dataProvider.ResolveRemoteJoins<T>(table)` once and pass:
   - `remote.PropertyToColumnMap`;
   - `tableQualifier: remote.IndividualJoinClauses?.Count > 0 ? table : null`.

   The `Last*` synthesized site starts passing the map too (it passes none today).
3. The qualifier form matches the WHERE visitors exactly (`{table}.{column}`, unquoted), so ORDER BY, WHERE and
   the qualified default paging order all agree.
4. SQLite (AC12-7, only if its red test is red): the default `ORDER BY rowid` becomes
   `ORDER BY {table}.rowid` when the command has joins, mirroring the 3.9 round-1 fix in the other three.

*Rejected alternatives:*
- **Always qualify:** changes SQL for every single-table query and breaks log-assertion tests, for no benefit.
- **String-rewrite the ORDER BY in `BuildQueryComponents`:** fragile against `CASE` and computed fragments.

### 5.2 #13 — allow-list pre-pass + correct `Single*`/`Last*`/`LongCount`

1. **Core `QueryOperatorPolicy`** (e.g. `Funcular.Data.Orm.Core/Linq/QueryOperatorPolicy.cs`).
   - Builds, once and statically, the set of allowed **generic method definitions** from
     `typeof(Queryable).GetMethods()` by name plus a parameter-shape predicate:
     - lambdas must be `Expression<Func<T, …>>` with exactly one element parameter;
     - `Skip`/`Take` take `int`;
     - no comparer or default-value parameters.
   - The allowed set is:
     - `Where`, `Select`;
     - `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`;
     - `Skip`, `Take`, `Distinct`;
     - `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault` (with and without
       predicate);
     - `Any` (with and without predicate), `All`;
     - `Count` and `LongCount` (with and without predicate);
     - `Sum`, `Average`, `Min`, `Max` (with selector);
     - identity `Cast`/`OfType` (D5).
   - `EnsureSupported(Expression, Type elementType)` walks the whole chain **before translation**:
     - `GroupBy` throws with the existing `GroupBy` message (AC13-8);
     - anything outside the allowed set throws
       `NotSupportedException("{Op}(...) is not translated to SQL in this version. Materialize first and
       apply it in memory: query.ToList().{Op}(...).")`.
   - Frameworks without some operators (netstandard2.0/net48) simply never contain them; the set is resolved
     from the running framework.
2. **Wiring.** Each provider's `ParseExpression` calls `QueryOperatorPolicy.EnsureSupported(expression,
   typeof(T))` immediately after collecting `methodCalls`. The existing inline `GroupBy` check becomes
   unreachable and is removed. The post-scalar composition guard and the scalar result-type guard stay as
   they are: they cover *allowed* operators in unsupported positions, which the policy doesn't model.
3. **`Single*`.**
   - The First/Last predicate branch generalizes to `First*`/`Single*`/`Last*` × {with, without predicate}.
     It routes the predicate to WHERE (existing code) and sets `components.Terminal`.
   - At execution time a `Single*` terminal forces `Take = min(Take ?? 2, 2)`. This reuses the existing
     paging path, including the qualified default order on join entities.
   - It reads up to two rows (list path), then applies LINQ cardinality:
     - 0 rows: `Single` throws "Sequence contains no (matching) element(s)"; `SingleOrDefault` returns `null`;
     - 2 rows: both throw "Sequence contains more than one element".
4. **`Last*`.**
   - The visitor exposes `OrderByTerms`; `QueryComponents.OrderByTerms` carries them (SQLite: alongside
     `_lastOrderByClause`).
   - A `Last*` terminal inverts every term's direction. With no terms, it synthesizes `{qualified id} DESC`,
     keeping the existing "requires an Id property" error.
   - It sets `Take = 1` and reads a single row. `Last` throws on empty; `LastOrDefault` returns `null`.
   - `Skip`/`Take` already present → `NotSupportedException` (D6).
   - This replaces the current `LastOrDefault(pred)` block, which only synthesizes an order when none exists
     and otherwise returns the *first* row of the explicit order.
5. **`LongCount`.** `BuildAggregateClause` treats `LongCount` like `Count`, using `COUNT_BIG(*)` on SQL Server
   and `COUNT(*)` elsewhere (bigint on PG/MySQL, integer on SQLite). `HandleAggregateQuery` returns
   `Convert.ToInt64`.

---

## 6. Tasks

Each task lists the tests it turns **green**. Every implementation task starts with those tests present
and **red**.

- **Task 0 — Test-plan review gate (no production code).**
  - ✅ Branch `development/3.10` from `master`; commit this plan.
  - ✅ Owner signed off D1–D7 (2026-09-30).
  - ✅ Earliest affected version: `0.1.1-alpha.2` (§1.1).
  - ✅ Local PG reached (D4); PG coverage baseline recorded (§4.5).
  - ✅ Join entities confirmed (§4.1).
  - ✅ SQLite AC12-7 probe: red (§1.3).
  - ✅ Cross-provider premise check (§1.3).
  - Post the §3 ACs to #12/#13. Retitle #13 to reflect `Single*`/`Last*` wrong rows; mark it critical.
  - A non-author reviewer agent reviews §2–§4 (decisions, ACs, matrix, mutations) **before** Task 1.
    Findings are blamed (AC-GAP / TEST-GAP / HOUSE-RULE / PLAN-GAP / OTHER) and folded in as Revision 2.
- **Task 1 — Harness + all red tests.** Add the §4.1 harness to each suite. Write every §4.2 test. Run and
  record them red (the AC13-8 tests and the "unchanged" rows are expected green).
- **Task 2 — #12 qualifier** (4 providers × visitor + 2 call sites).
  → AC12-1…AC12-6 green.
- **Task 3 — SQLite default-order qualification** (required: AC12-7 was red).
  → `DefaultPaging_OnJoinEntity_Executes`, `DefaultPaging_OnJoinEntity_SubsetProjection_Executes`.
- **Task 4 — Core `QueryOperatorPolicy` + pre-pass wiring** (4 providers).
  → AC13-4, AC13-6, AC13-7, AC13-8.
- **Task 5 — `Single*`** (4 providers).
  → AC13-1.
- **Task 6 — `Last*` + `OrderByTerms`** (4 providers).
  → AC13-2, AC12-5.
- **Task 7 — `LongCount`** (4 providers).
  → AC13-3.
- **Task 8 — Coverage lift.**
  - Visitor ternary/nested/error-path tests to bring the four order-by visitors to ≥85%.
  - `MySqlLinqQueryProvider.cs` from 82% to ≥85%.
  - Attach per-file numbers.
  → §4.5 table all ≥85%.
- **Task 9 — Docs.**
  - `Advanced.md` + `FUNKYORM_AI_ADVANCED.md` operator table (generated from `SupportedOperators` or kept in
    sync by a doc test).
  - Changelog 3.10.0: **Fixed (correctness)** with the §1.1 table in prose, and **Changed** for "untranslated
    operators now throw".
  - README "Recent Changes" one-liner with "upgrade strongly recommended".
  - Usage.md aggregates section: `LongCount`.
  → AC13-9.
- **Task 10 — Gauntlet and release.**
  - Mutation runs (§4.4) recorded.
  - A fresh adversarial pass by an agent that didn't write the code, then a fix-verification pass over any
    fix layer.
  - Write the sentinel only for the exact clean sha.
  - Push `development/3.10` (CI + MySQL + **PostgreSQL**) → PR → merge → `3.10.0-beta1`.
  - Smoke-test Sentinel's call list on the beta → promote to `3.10.0`.
  - Close #12/#13. Decide on the GitHub Security Advisory (D7).

**Rough effort:**

| Work | Estimate |
|---|---|
| #12 (Tasks 2–3) | ~1 day |
| #13 (Tasks 4–7) | ~2–3 days |
| Harness + coverage lift (Tasks 1, 8) | ~1–1.5 days |
| Docs + gauntlet (Tasks 9–10) | ~1 day |
| **Total** | **~5–6 working days** |

---

## 7. Risks

- **Someone relies on a silently ignored operator.** For example, a stray `DefaultIfEmpty()` or
  `.OrderBy(..).Last()` returning the "first" row that a caller has adapted to. That code now throws or
  returns a different row. **Mitigation:** minor version bump, a prominent Changelog "Changed" list, and
  exception messages that tell the caller what to write instead.
- **Over-rejection of a valid shape.** **Mitigation:** the allowed set is built from the operators the loop
  already translates; AC13-5 oracle rows cover each allowed family; all four existing suites are the
  regression gate.
- **Mirror drift across providers.** **Mitigation:** the policy lives in Core; the per-provider changes are
  mechanical and each is proved by the same test names in each suite.
- **`Single*` now always sends ORDER BY + FETCH.** This is a small sort cost on large unfiltered sets.
  `Single` is almost always key-filtered, so this is accepted and noted in docs.

## 8. Out of scope (recorded, not fixed here)

- `Sum`/`Min`/`Max`/`Average` over selector types outside {`int`, `long`, `double`, `decimal`, `DateTime(?)`}
  throw `NotSupportedException` **after** the SQL round-trip. It's clean, but late.
- `GenerateOrderByClause` in the SQL Server, PostgreSQL and MySQL providers is dead code.
- SQLite keeps per-query state in provider instance fields. It's overwritten on every parse (no sequential
  leak), but isn't safe for concurrent execution of queries composed from one `Query<T>()` root.
- Sentinel.MVP pins `3.9.0-beta1`; upgrading it is the D3 smoke test, tracked in Sentinel, not here.
