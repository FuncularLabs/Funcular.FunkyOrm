# Query Operator Correctness (#12, #13) — Implementation Plan

> **Goal**: Fix the two query-translation defects logged after 3.9.0 — [#12](https://github.com/FuncularLabs/Funcular.FunkyOrm/issues/12)
> (unqualified own-column `ORDER BY` on remote-join entities) and [#13](https://github.com/FuncularLabs/Funcular.FunkyOrm/issues/13)
> (LINQ operators silently ignored or mistranslated) — across all four providers, test-first. Target release:
> **v3.10.0** (branch `development/3.10`). This is a **prerequisite for the sponsored-tutorial program**: a
> tutorial audience coming from EF will reach for `SingleOrDefault`, `Average`, and the narrow-projection
> paging idiom on day one.

> **Status (2026-09-30)**:
> - D1–D7 approved by the owner.
> - **D8–D11 are new, raised by the Task 0 review, and need owner sign-off before Task 1.**
> - Task 0 is otherwise complete.

> **Revision 2 (Task 0 test-plan review, 2026-09-30) — what changed:**
> A non-author reviewer found 25 issues at `30d3a6d`: 1 blocker, 8 major, 16 minor. Blame: AC-GAP 10,
> TEST-GAP 11, PLAN-GAP 4. All are accepted; the disposition is in §9. In summary:
> - **Scope grows by four silent-wrong-result classes the reviewer found**, each confirmed against source by
>   the author. Each has a new decision (D8–D11), new ACs and tasks:
>   - operators after `Skip`/`Take` are mistranslated (D8, AC13-10);
>   - aggregate overloads return wrong or truncated values (D9, AC13-11);
>   - a second `OrderBy` reverses sort priority (D10, AC13-12);
>   - SQLite leaks projection state between executions, and an ORDER BY ternary null-check is always false
>     (D11, AC13-13, AC12-8).
> - `Single*` no longer rides the paging path. It uses a row limit (`TOP 2` / `LIMIT 2`), so it injects no
>   `ORDER BY id`: some entities have no `id` column, and a synthesized order would trip the `Distinct`
>   projection guard.
> - AC13-4's "not even schema discovery" was false: `Query<T>()` discovers schema eagerly and unlogged.
>   The AC is reworded, the D2 rationale is corrected, and the mutation killer is replaced.
> - Test-design fixes: `Cast` tests had no teeth (`Queryable.Cast` short-circuits); the classification sweep
>   was a tautology; AC13-8 was unprovable as written; oracle determinism rules were missing.
>   Plus mutation rows, net48/DotNet9 runs, compile stubs for red tests, and DB-free visitor tests.
> - §1.1: "first LINQ provider" corrected. An `IQueryProvider` existed at `1a96b3d` (2025-03-30); the
>   `0.1.1-alpha.2` → `3.9.0` NuGet range still holds, because `0.1.1-alpha.1` was never published.
> - Effort: ~5–6 → **~8–9 working days**.
>
> **Revision 1 (Task 0 results, 2026-09-30):** cross-provider verification (§1.3); SQLite paging on
> remote-join entities broken; D4 resolved with native local PostgreSQL; PG baselines; harness rules;
> affected-version range.

---

## 1. Verified premises

Both issues were filed from code reading. Before planning, throwaway probes ran each shape against live
databases on all four providers. The probes were deleted afterwards; their cases become the red tests in §4.

### 1.1 #13 is broader and more severe than filed (SQL Server shown; §1.3 for the others)

| Query (SQL Server, 3.9.0; 8,369 `person` rows, ids 1…8673) | Correct result | FunkyORM 3.9.0 returns | SQL actually sent |
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

**Root cause:**
- `ParseExpression` translates a fixed set of operator **names** and silently skips every other
  `MethodCallExpression` in the chain.
- `ExecuteQuery` then reads the first row for any non-collection `TResult`.
- `Single*` are never matched.
- The `First/Last` predicate branch requires `Arguments.Count == 2`, so parameterless `Last()` falls through
  to "read first row".

**Severity: critical (silent wrong row).** `Query<User>().SingleOrDefault(u => u.Email == email)` returns
an arbitrary *different* user. In a consuming app that is an authorization flaw, not just a bug.

**Affected versions:** every NuGet release from `0.1.1-alpha.2` (the first published version) through
`3.9.0`. `Single*` has never been matched in any version of the LINQ provider: no commit before the 3.9
scalar-projection guards mentions it. An earlier `IQueryProvider` existed at `1a96b3d` (2025-03-30), but it
predates every published package.

**Found by the Task 0 review, confirmed against source by the author:**
- Aggregates ignore paging: `BuildAggregateClause` never reads `Skip`/`Take`.
- `Where` and predicate terminals after `Skip`/`Take` are ANDed into `WHERE`, so they run before paging.
- Repeated `Skip`/`Take` overwrite each other instead of composing.
- `Average` over a `decimal`/`float` selector throws `InvalidCastException`.
- Nullable `Min`/`Max`/`Average` on an empty set throws (LINQ-to-objects returns `null`).
- *Unverified — Task 1 red test:* SQL Server `AVG(int)` truncates.
- `OrderBy(a).OrderBy(b)` emits `ORDER BY a, b`.
- SQLite `_lastSelectProjection`/`_lastSelectParameters` are only ever set, so a reused root inherits a
  stale narrow projection.
- An ORDER BY ternary `x.M == null` emits `col = NULL`, which is never true.

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
entity, when any joined table has a column of the same name. That is exactly the performant call-list idiom
documented in 3.8.5/3.9.

**Consumer exposure:** Sentinel.MVP `main` (pinned to 3.9.0-beta1) uses this exact shape in
`CallListOrder.Apply` → `.ThenBy(r => r.CallId)` → `Skip/Take` → `Select(r => new CallListQueryRow { CallId = r.CallId })`.
- It works **only because** `CallId` maps to `call_id` and no joined table has that name. A new sort key on
  a shared name (`id`, `uid`, `dateutc_created`) would crash that page.
- No Sentinel code uses a #13 operator on a FunkyORM query, and none composes an operator after
  `Skip`/`Take`. The three multiline hits are in-memory LINQ over lists.

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

### 1.4 Code facts the design relies on (verified by author and reviewer)

- **Order-by visitors.** Each provider has its own (`OrderByClauseVisitor`, `PostgreSqlOrderByClauseVisitor`,
  `MySqlOrderByClauseVisitor`, `SqliteOrderByClauseVisitor`). All four resolve own columns only through a
  private `ResolveOrderColumn` → bare `GetColumnName(property)`. Every ORDER BY path goes through it:
  member, Convert-wrapped member, `CASE` `HasValue` test, `CASE` value branches, `.Value` nesting.
- **Prior-ordering recursion.** At an `OrderBy*`/`ThenBy*` node the visitor recurses into any previous
  ordering call, **including a previous `OrderBy`** (OrderByClauseVisitor.cs:117-134), and appends the new key
  after it. That is the root of D10.
- **WHERE and SELECT already qualify.** All four WHERE visitors qualify own columns as
  `{_tableName}.{column}` (unquoted). `SelectClauseVisitor` emits `person.id AS Id`.
- **Two visitor call sites per provider** in `*LinqQueryProvider.ParseExpression`:
  - the `OrderBy/ThenBy` branch, which passes the remote map;
  - the `Last/LastOrDefault(pred)` synthesized `ORDER BY Id DESC`, which passes no map.

  `GenerateOrderByClause` in three providers is dead code and stays untouched.
- **"Has joins" test.** `ResolvedRemoteJoinInfo.IndividualJoinClauses` has the same name in all four. It is
  non-empty exactly when `CreateGetOneOrSelectCommandText` emits joins. WHERE-induced joins are the same
  list.
- **Eager schema discovery.** `Query<T>()` calls `CreateGetOneOrSelectCommandText<T>()` eagerly. That runs
  `ResolveTableName`, `DiscoverColumns` and remote-type discovery with raw commands that **don't log**. So
  "no SQL executed" can only be observed as "no *query or aggregate* command logged after the `IQueryable` is
  obtained".
- **Default paging order.** SQLite's is a bare `ORDER BY rowid`; the other three qualify `id` with a text
  check for `" JOIN "`. That default hard-codes the column name `id`, so it's wrong for entities whose key
  isn't `id`. It is pre-existing, and `Single*` must not start routing through it (see D1/§5.2.3).
- **Aggregates.** `BuildAggregateClause` ignores `Skip`/`Take` (all four). `HandleAggregateQuery` converts by
  a selector-type switch (`int`/`long`/`double`/`decimal`/`DateTime(?)`). `Average` always returns
  `Convert.ToDouble` boxed to `TResult`, and a `NULL` result throws for `Average`/`Min`/`Max`.
- **SQLite state fields.** SQLite keeps `_lastOrderByClause` (overwritten on every parse), plus
  `_lastSelectProjection` and `_lastSelectParameters`, which are **set only in the Select branch and never
  reset**. The early return for an operator-free expression happens before any reset.
- **Execution entry point.** There's no async LINQ surface; `Execute<TResult>` is the only entry point per
  provider. The obsolete `Query<T>(predicate)` doesn't go through the LINQ provider.
- **`Queryable.Cast<TResult>`** returns the source unchanged when it's already `IQueryable<TResult>`, so an
  identity or covariant `Cast` creates no expression node. `OfType` always creates one.

---

## 2. Decisions

| # | Status | Decision | Recommendation | Why |
|---|---|---|---|---|
| **D1** | ✅ approved | `Single`/`SingleOrDefault`/`Last`/`LastOrDefault`/`LongCount`: implement or reject? | **Implement.** *(Rev 2: `Single*` uses a row limit, not the paging path — §5.2.3.)* | EF muscle memory. Rejecting would turn silent-wrong into loud-but-unhelpful for the most common calls. |
| **D2** | ✅ approved | Allow-list form | **Exact-overload allow-list in Core** (`QueryOperatorPolicy`), run as a **pre-pass over the method spine** before any translation. | Name-only lists miss overload traps that exist today: indexed `Where` → `InvalidCastException`; comparers ignored; `Take(Range)` → `InvalidCastException`; `FirstOrDefault(defaultValue)` ignores the default. One Core policy removes the four-way mirror-drift risk. *(Rev 2 correction: the pre-pass guarantees no query or aggregate command runs. Schema discovery already happened at `Query<T>()` and is unaffected.)* |
| **D3** | ✅ approved | Version / branch | **3.10.0** on `development/3.10`; `3.10.0-beta1`, promoted to stable after a Sentinel call-list smoke test. | Behavior change. The PG workflow only runs on `development/**`. |
| **D4** | ✅ approved, resolved | Local PostgreSQL | Native PostgreSQL 18 via the suite's fallback connection; CI `postgres:17`. | Red→green observed on every provider without a push per cycle. |
| **D5** | ✅ approved | `Cast<T>()` / `OfType<T>()` | **Identity only** is a no-op; other types are rejected. *(Rev 2: "identity" means the **source element type** of that call, not the entity type, so `Select(p => p.Id).OfType<int>()` is allowed.)* | Identity is correct today; non-identity is silently wrong. |
| **D6** | ✅ approved | `Last*`/`ElementAt*` with paging | `Last*` after `Skip`/`Take`: reject. `ElementAt*`: reject. *(Rev 2: `Single*` after paging is governed by D8.)* | Inverting an ORDER BY under an OFFSET reads the wrong window. |
| **D7** | ✅ approved | Disclosure | Changelog + README "upgrade strongly recommended"; owner decides on a GitHub Security Advisory. | "Returns a different user's row." |
| **D8** | ⏳ **needs sign-off** | Operators composed **after** `Skip`/`Take` | **Reject by position** in the same Core pre-pass. After the first `Skip`/`Take` on the spine, only these may follow: one `Take` directly after a `Skip` (the canonical `Skip(n).Take(k)`), `Select`, parameterless `First*`/`Single*`, and identity `Cast`/`OfType`. Everything else throws `NotSupportedException` naming the operator and "after Skip/Take": `Where`, any `OrderBy*`/`ThenBy*`, `Distinct`, any aggregate, predicate-bearing terminals, `Last*`, a second `Skip`, and `Take` before `Skip`. **`Take(0)`** short-circuits to an empty sequence without SQL. | Today these silently compute over the wrong rows, e.g. `Take(5).Count()` counts everything. Translating them needs subqueries; rejecting matches D2/D6 and costs nothing that works today. No FunkyORM test or Sentinel query composes after paging. `Take(0)` currently hits SQL Server's `FETCH NEXT 0` error. |
| **D9** | ⏳ **needs sign-off** | Aggregate overload correctness | **Fix, not reject.**<br>• Convert every aggregate result to the called overload's `TResult` generically (nullable-aware); this replaces the selector-type switch.<br>• Nullable `Min`/`Max`/`Average` on an empty set return `null`.<br>• `Average` over integer selectors computes in floating point: SQL Server `AVG(CAST(x AS FLOAT))`; confirm the others in Task 1.<br>• `Average`/`Sum` over `decimal`/`float` return their own type. | `Average(p => p.Age)` is too common to reject, and it's wrong on SQL Server today (truncates — Task 1 confirms) and throws for `decimal`. The generic conversion also fixes SQL Server `int?` selectors and `string` `Min`/`Max`. |
| **D10** | ⏳ **needs sign-off** | `OrderBy(a).OrderBy(b)` semantics | **LINQ-to-objects semantics**: a later `OrderBy*` becomes primary and earlier keys become tie-breakers in their original order. `OrderBy(a).ThenBy(c).OrderBy(b)` → `ORDER BY b, a, c`. | Today it emits `ORDER BY a, b`, which is wrong under both LINQ-to-objects and EF (which drops the earlier keys). The LINQ-to-objects form is deterministic and can be checked by the oracle. |
| **D11** | ⏳ **needs sign-off** | Two adjacent silent-wrong-result bugs in files we're touching | **Fix in scope.**<br>• SQLite resets all per-parse state (`_lastSelectProjection`, `_lastSelectParameters`, `_lastOrderByClause`) at the top of every parse, before the early return.<br>• ORDER BY ternary null tests emit `IS NULL`/`IS NOT NULL`. | Both are in files this plan already edits and lifts to 85% coverage. Leaving them would make the oracle and ternary tests fail for unrelated reasons. |

---

## 3. Acceptance criteria

These will be posted back to #12 and #13 once D8–D11 are signed off. Unless stated otherwise, each AC holds
for **all four providers**.

### #12 — own-column ORDER BY on remote-join entities

- **AC12-1** On an entity with remote joins, **every combination** of the following executes and returns
  rows in the requested order, with own columns emitted as `{baseTable}.{column}`:
  - operator: `OrderBy`/`OrderByDescending`/`ThenBy`/`ThenByDescending` over an own mapped column;
  - shape: full entity, subset projection `Select(x => new T { … })`, or scalar projection `Select(x => x.M)`;
  - paging: with or without `Skip`/`Take`.
- **AC12-2** Ordering by a remote/computed member emits **exactly** its resolved fragment (e.g.
  `[country_0].name`), with no base-table prefix, unchanged from 3.9.0.
- **AC12-3** For an entity **without** remote joins, the generated SQL is byte-identical to 3.9.0.
- **AC12-4** A ternary/`CASE` ordering over own columns on a join entity is qualified inside the `CASE`.
- **AC12-5** The ORDER BY synthesized for `Last*` (AC13-2) is qualified on join entities and works with a
  narrow projection (when the ordering key is projected — see AC13-2).
- **AC12-6** On the **join entity**, `Distinct()` + custom projection + own-column `OrderBy`:
  - executes when the key is in the projection;
  - still throws the existing `InvalidOperationException` when it isn't.
- **AC12-7** *(SQLite; red in Task 0)* `Skip`/`Take` on a join entity with no explicit order executes, for
  the full entity and for a subset projection.
- **AC12-8** *(D11)* An ORDER BY ternary whose test is `x.M == null` / `x.M != null` orders rows the same way
  LINQ-to-objects does (`IS NULL` / `IS NOT NULL`).

### #13 — operator correctness

- **AC13-1** `Single`/`SingleOrDefault` match LINQ-to-objects:
  - The predicate, if any, is applied as SQL `WHERE`.
  - Zero rows: `Single` throws `InvalidOperationException`; `SingleOrDefault` returns `null`.
  - Two or more rows: both throw `InvalidOperationException`.
  - **Parameterless** `Single*` composes with prior `Where`/`OrderBy`/`Skip`/`Take`, including `Take(1)`
    (reads one row, never throws "more than one") and `Take(0)` (AC13-10).
  - With no user `Skip`/`Take`, `Single*` emits **no synthesized `ORDER BY`**. It uses `TOP 2` (SQL Server)
    or `LIMIT 2` (others), so it works on entities whose key column isn't `id`.
  - `Single*` after `Distinct()` + custom projection works.
- **AC13-2** `Last`/`LastOrDefault` (with and without a predicate) match LINQ-to-objects:
  - **Explicit order.** They honor the full explicit ordering, with every term inverted: own, remote/computed,
    and `CASE` terms, including after D10 reordering.
  - **No order.** They default to `Id DESC`. The existing "no `Id` property" `InvalidOperationException` is
    kept.
  - **Empty.** `Last` throws; `LastOrDefault` returns `null`.
  - **After `Skip`/`Take`.** Rejected (D6/D8).
  - **After `Distinct()` + custom projection with no explicit order.** Throws `NotSupportedException` naming
    `Last` and saying an explicit `OrderBy` on a projected key is required. With such an order, it works.
- **AC13-3** `LongCount()` and `LongCount(pred)` return an `Int64` equal to `Count`. SQL Server emits
  `COUNT_BIG(*)`. A predicate over a reverse (one-to-many) remote key is rejected exactly like `Count`.
- **AC13-4** Every operator on the method spine that isn't an allowed `Queryable` overload throws
  `NotSupportedException` naming the operator. That covers non-`Queryable` methods and non-generic
  `Queryable` overloads such as `Sum(IQueryable<int>)`. **No query or aggregate command is executed**: `Log`
  receives nothing after the `IQueryable` is obtained. **Lambdas nested inside allowed operators aren't
  inspected**, so `Where(p => ids.Contains(p.Id))` still works. The rejection table (§4.2) covers, at
  minimum:
  - **Ordering and slicing:** `Reverse`, `TakeWhile`, `SkipWhile`, `TakeLast`, `SkipLast`, `ElementAt`,
    `ElementAtOrDefault`, `Order`, `OrderDescending`.
  - **Set, join and sequence:** `DefaultIfEmpty`, `Concat`, `Union`, `Intersect`, `Except`, `Zip`,
    `SelectMany`, `Join`, `GroupJoin`, `Append`, `Prepend`, `SequenceEqual`, `Chunk`.
  - **Reducing:** `Contains`, `Aggregate`, `DistinctBy`, `MinBy`, `MaxBy`, and parameterless `Min()`/`Max()`
    on an entity.
  - **Type filters:** non-identity `Cast`/`OfType`.
  - **Unsupported overloads of allowed operators:** indexed `Where`/`Select`; comparer overloads; default-value
    overloads of `First*`/`Single*`/`Last*`; `Take(Range)`; non-generic scalar `Sum()`/`Average()`.
- **AC13-5** Every shape that worked correctly in 3.9.0 still works identically: all four existing suites
  stay green, and there is one oracle row per allowed overload family (§4.2 lists each row with its expected
  outcome).
- **AC13-6** *(D5)* `OfType<X>()` where `X` is the call's source element type is a no-op. That includes
  `Select(p => p.Id).OfType<int>()`. Non-identity `Cast`/`OfType` throw.
- **AC13-7** The allow-list is pinned. A test asserts the exact set of allowed overloads (count +
  signatures). A reflection sweep drives the classifier (`IsAllowed(MethodInfo)`) over **every** public
  `Queryable` method in the running framework and compares the result with the pinned set. The sweep runs on
  net8 **and net48**.
- **AC13-8** **Allowed** operators keep their specific messages: the post-scalar-projection composition
  message, the scalar result-type guard, and `GroupBy`'s dedicated text ("GroupBy is not supported in this
  version"). **Rejected** operators get the policy message even after a scalar `Select`. Precedence: pre-pass
  (policy and position) → parse-loop guards → execute-time guards.
- **AC13-9** `Advanced.md` and `FUNKYORM_AI_ADVANCED.md` carry a works/doesn't-work **operator table**,
  including the "nothing after `Skip`/`Take`" rule. Changelog and README "Recent Changes" record the fixes and
  behavior changes.
- **AC13-10** *(D8)* An operator composed after `Skip`/`Take` is either translated correctly or throws
  `NotSupportedException` before any query command runs:
  - **Allowed after paging:** `Skip(n).Take(k)`, `Select`, parameterless `First*`/`Single*`, and identity
    `OfType`.
  - **Rejected after paging:** any aggregate (`Count`/`LongCount`/`Any`/`All`/`Sum`/`Average`/`Min`/`Max`),
    `Where`, predicate-bearing `First*`/`Single*`, `Last*`, `OrderBy*`/`ThenBy*`, `Distinct`, a second
    `Skip`/`Take`, and `Take(n).Skip(m)`.
  - `Take(0)` returns an empty sequence without SQL: `First`/`Single` throw; `*OrDefault` returns `null`.
- **AC13-11** *(D9)* `Sum`/`Average`/`Min`/`Max` match LINQ-to-objects for every allowed selector type, on
  non-empty and empty sets:
  - selector types: `int`, `int?`, `long`, `long?`, `float`, `float?`, `double`, `double?`, `decimal`,
    `decimal?`; plus `DateTime`, `DateTime?` and `string` for `Min`/`Max`;
  - return type and value both match, including `Average` of `{1, 2}` = `1.5` on SQL Server.
- **AC13-12** *(D10)* `OrderBy(a).OrderBy(b)` and `OrderBy(a).ThenBy(c).OrderBy(b)` return rows in
  LINQ-to-objects order, and `Last*` after them inverts that order.
- **AC13-13** *(D11, SQLite)* A query doesn't inherit a projection, parameters, or ordering from an earlier
  execution on the same `Query<T>()` root.

---

## 4. Test plan (written and reviewed first — Task 0)

### 4.1 Harness

- **Discriminating seed data.** Every semantics test inserts its own rows, tagged with a per-test GUID
  marker, and scopes every query with `Where(p => p.LastName == marker)`.
  - The target row is **neither the first nor the last by id**. For `Last`, the order key is **not `Id`**,
    and its last row is not the max id.
  - Each test states which wrong answer it rules out.
  - Aggregate/count tests have a count > 0, so "returns `default`" can't pass.
- **Oracle assertion.** `AssertMatchesLinqToObjects(Func<IQueryable<T>, object> shape)` runs the shape
  against a **fresh** `provider.Query<T>()`. It also runs it against
  `provider.Query<T>().Where(marker).ToList().OrderBy(p => p.Id).AsQueryable()`: a fresh root, with the base
  list sorted by `Id`. It compares results (by id / by value / by exception type).
- **Oracle determinism rules.**
  - Every oracle shape that depends on order has a **total order on unique, non-null keys**.
  - Null-key ordering (PG sorts NULLs last on ASC; LINQ-to-objects sorts them first) and collation-vs-ordinal
    string ordering are covered only by separate, explicit, per-provider tests. The oracle never covers them.
  - Remote LEFT-JOIN keys are seeded non-null in oracle rows.
- **Fresh root per shape.** Never reuse one `Query<T>()` root across shapes or oracle sides. AC13-13 is the
  one deliberate exception.
- **SQL capture.** `provider.Log` → `StringBuilder`. For AC13-4 "nothing executed", clear the builder
  **after** obtaining the `IQueryable`, then assert it's still empty. Schema discovery at `Query<T>()`
  doesn't log.
- **Per-provider join entity.** SQL Server / PostgreSQL / SQLite use `PersonDetailEntity` (joins
  `organization`, `address`, `country`); MySQL uses `PersonWithEmployer` (joins `organization`). Every joined
  table has an `id` column.
- **Entity without an `id` column** (AC13-1 row-limit test). If a suite has no such entity, Task 1 adds one:
  a table keyed `<table>_id`, which the conventions already support.
- **Cleanup deletes run in a transaction.** `Delete<T>(predicate)` requires an active transaction.
- **Subset projections don't populate `Id`.** Compare projected members, never `Id`. Project the ordering
  key too where order matters.
- **SQLite** creates its own temp database in `[ClassInitialize]`. Task 1 adds a shared schema helper.
- **DB-free tests don't inherit a DB fixture:** `QueryOperatorPolicyTests` and the direct visitor tests.

### 4.2 AC → test matrix

**Test classes:**
- Per provider, in each of the four test projects (prefixed `PostgreSql`/`MySql`/`Sqlite` in the siblings):
  `OrderByQualificationTests`, `QueryOperatorSemanticsTests`, `QueryOperatorRejectionTests`,
  `AggregateSemanticsTests`, and `OrderByVisitorDirectTests` (DB-free).
- `QueryOperatorPolicyTests` is DB-free and exercises Core. It lives in `Funcular.Data.Orm.SqlServer.Tests`,
  **and is linked into `Funcular.Data.Orm.SqlServer.Tests.NetFramework` (net48) and `.DotNet9`**.

| AC | Test(s) | Project(s) |
|---|---|---|
| AC12-1 | `[DataTestMethod] OwnColumnOrdering_OnJoinEntity_Executes_InOrder` over {OrderBy, OrderByDesc, ThenBy-after-remote, ThenByDesc-after-remote} × {full, subset, scalar} × {paged, unpaged} (24 rows) | all 4 |
| AC12-2 | `RemoteMemberOrderBy_EmitsExactResolvedFragment_NoBasePrefix`, `ComputedMemberOrderBy_EmitsExpression_Unchanged` | all 4 |
| AC12-3 | `SingleTableEntity_OrderBy_SqlByteIdenticalTo390` (golden string, `PersonEntity`) | all 4 |
| AC12-4 | `TernaryOrderBy_OwnColumns_OnJoinEntity_QualifiedInsideCase` (non-null test expression) | all 4 |
| AC12-5 | `Last_OnJoinEntity_NarrowProjectionWithKey_SynthesizedOrderQualified` | all 4 |
| AC12-6 | `Distinct_Projection_JoinEntity_OrderByKeyInProjection_Executes`, `Distinct_Projection_JoinEntity_OrderByKeyNotInProjection_ThrowsExisting` | all 4 |
| AC12-7 | `DefaultPaging_OnJoinEntity_Executes`, `DefaultPaging_OnJoinEntity_SubsetProjection_Executes` | SQLite (regression rows in the other 3) |
| AC12-8 | `TernaryOrderBy_NullCheck_MatchesOracle`, `TernaryOrderBy_NotNullCheck_MatchesOracle` | all 4 |
| AC13-1 | `Single_Predicate_ReturnsTargetNotFirst`, `SingleOrDefault_Predicate_NoMatch_ReturnsNull`, `Single_NoMatch_Throws`, `Single_TwoMatches_Throws`, `SingleOrDefault_TwoMatches_Throws`, `Single_AfterWhereOrderBySkipTake_Parameterless_MatchesOracle`, `Single_AfterTake1_OverManyRows_ReturnsRow`, `Single_NoUserOrder_EmitsRowLimit_NoIdOrder` (SQL shape), `Single_OnEntityWithoutIdColumn_Works`, `Single_AfterDistinctProjection_Works` | all 4 |
| AC13-2 | `Last_Parameterless_Unordered_ReturnsMaxId`, `Last_AfterOrderByNonIdKey_ReturnsLastInOrder`, `Last_AfterOrderByThenByDescending_InvertsEveryTerm`, `Last_AfterRemoteOrderBy_ReturnsLastInOrder`, `Last_AfterTernaryOrderBy_InvertsCaseTerm`, `LastOrDefault_Predicate_WithExplicitOrderBy_MatchesOracle`, `Last_Empty_Throws`, `LastOrDefault_Empty_ReturnsNull`, `Last_EntityWithoutIdProperty_ThrowsExistingInvalidOperation`, `Last_AfterDistinctProjection_NoOrder_ThrowsNamingLast`, `Last_AfterDistinctProjection_WithProjectedOrder_Works`; existing PG `LastOrDefault(x => …guid…)` stays green | all 4 |
| AC13-3 | `LongCount_EqualsCount_ReturnsInt64`, `LongCount_Predicate_EqualsCountPredicate`, `LongCount_FilteredByReverseRemoteKey_ThrowsNotSupported`; SQL Server only: `LongCount_EmitsCountBig` | all 4 |
| AC13-4 | `[DataTestMethod] Rejected_Operator_ThrowsNotSupported_NamesOperator_NoQueryExecuted` (one row per AC13-4 shape, including `ScalarProjection_ParameterlessSum`), `Allowed_PredicateWithCollectionContains_NotRejected`, `NonQueryableSpineMethod_Rejected` | all 4 |
| AC13-5 | `[DataTestMethod] Allowed_Operator_MatchesOracle` — one row per allowed family, each with its expected outcome listed in the data row; plus the four full existing suites | all 4 |
| AC13-6 | `OfType_Identity_IsNoOp`, `OfType_Identity_AfterScalarProjection_IsNoOp`, `OfType_NonIdentity_Throws`, `Cast_NonIdentity_UnrelatedType_Throws` (e.g. `Cast<AddressEntity>()` on a person query); DB-free `Cast_IdentityNode_HandBuilt_IsAllowed` | all 4 + Core |
| AC13-7 | `SupportedOperators_ExactSetPinned`, `ClassifierSweep_EveryQueryableMethod_MatchesPinnedSet`, `NonQueryableOverload_IsRejected` | Core (net8, net48, net9) |
| AC13-8 | `GroupBy_Rejected_KeepsDedicatedMessage` (asserts "GroupBy is not supported in this version"); existing scalar composition and reducing-terminal tests; **new in the three siblings:** `ScalarProjection_WithReducingTerminals_ThrowNotSupported`; `ScalarProjection_WithSingleOrLast_ThrowsNotSupported` (asserts which message wins per the AC13-8 precedence); `Rejected_OperatorOuterToFailingInnerOperator_PolicyMessageWins` (`Select(p => p.Id).Where(x => x > 0).Reverse()`) | all 4 |
| AC13-9 | Doc review in the Task 12 gauntlet (not executable; recorded as such) | — |
| AC13-10 | `[DataTestMethod] Operator_AfterPaging_Rejected_BeforeAnyQuery` over {Count, LongCount, Any, All, Sum, Average, Min, Max, Where, First(pred), Single(pred), Last, OrderBy, ThenBy, Distinct, Skip-after-Skip, Take-after-Take, Take-then-Skip}; `[DataTestMethod] Operator_AfterPaging_Allowed_MatchesOracle` over {Skip.Take, Select subset, Select scalar, First(), FirstOrDefault(), Single(), SingleOrDefault()}; `Take0_ReturnsEmpty_NoQuery`, `Take0_First_Throws_NoQuery`, `Take0_SingleOrDefault_ReturnsNull_NoQuery` | all 4 |
| AC13-11 | `[DataTestMethod] Aggregate_MatchesOracle` over {Sum, Average, Min, Max} × the AC13-11 type list × {non-empty, empty}, asserting value **and** runtime type; `Average_IntSelector_OneAndTwo_IsOnePointFive` | all 4 |
| AC13-12 | `OrderBy_ThenOrderByAgain_MatchesOracle`, `OrderBy_ThenBy_ThenOrderBy_MatchesOracle`, `Last_AfterDoubleOrderBy_MatchesOracle` | all 4 |
| AC13-13 | `SqliteRoot_ReusedAfterProjection_FullEntityNotNarrowed`, `SqliteRoot_ReusedAfterOrderedQuery_NoInheritedOrder`, `SqliteRoot_ReusedAfterParameterizedProjection_NoDuplicateParameters` | SQLite |

### 4.3 Interface coverage (new/changed members → tests)

| Member | Tests that call it on purpose |
|---|---|
| Core `QueryOperatorPolicy.EnsureSupported(Expression spine)` *(new, public)* | `QueryOperatorPolicyTests.*`, every `Rejected_*`/`Allowed_*`/`Operator_AfterPaging_*` row |
| Core `QueryOperatorPolicy.IsAllowed(MethodInfo)` *(new, internal seam, `InternalsVisibleTo` the test projects)* | `ClassifierSweep_EveryQueryableMethod_MatchesPinnedSet`, `NonQueryableOverload_IsRejected` |
| Core `QueryOperatorPolicy.SupportedOperators` *(new, public read-only)* | `SupportedOperators_ExactSetPinned`; the docs table (Task 11) |
| `*OrderByClauseVisitor` ctor — new optional `tableQualifier` | AC12 tests; `SingleTableEntity_…ByteIdentical` (null path); `OrderByVisitorDirectTests` |
| `*OrderByClauseVisitor.OrderByTerms` *(new)* | `Last_AfterOrderByThenByDescending_InvertsEveryTerm`, `Last_AfterDoubleOrderBy_MatchesOracle`, direct tests |
| `*OrderByClauseVisitor` prior-`OrderBy` handling (D10) | AC13-12 tests, direct tests |
| `*OrderByClauseVisitor` ternary null test (D11) | AC12-8 tests, direct tests |
| `QueryComponents.Terminal` *(new enum)*, `.RowLimit` *(new)*, `.OrderByTerms` *(new)* | AC13-1, AC13-2, AC13-10 tests |
| `*LinqQueryProvider.ParseExpression` (pre-pass wiring; Single/Last/LongCount branches) | AC13-* |
| `*LinqQueryProvider.BuildQueryComponents` (row limit; SQLite `rowid` qualification) | AC13-1, AC12-7 |
| `*LinqQueryProvider.ExecuteQuery` (cardinality, `Take(0)` short-circuit) | AC13-1, AC13-10 |
| `*LinqQueryProvider.BuildAggregateClause` (LongCount; `AVG` cast) | AC13-3, AC13-11 |
| `*LinqQueryProvider.HandleAggregateQuery` (generic `TResult` conversion, nullable-empty) | AC13-11 |
| `SqliteLinqQueryProvider` state reset | AC13-13 |

### 4.4 Mutations each key test must kill (run them; record the result in the handoff)

| Mutation | Test that must fail |
|---|---|
| Revert `ResolveOrderColumn` to bare `GetColumnName` | AC12-1 projection rows (runtime ambiguous column) |
| Apply the qualifier in the map-hit branch too | `RemoteMemberOrderBy_EmitsExactResolvedFragment_NoBasePrefix` |
| Qualify unconditionally (drop the "has joins" condition) | `SingleTableEntity_OrderBy_SqlByteIdenticalTo390` |
| Don't pass the qualifier at the `Last*` synthesized site | `Last_OnJoinEntity_NarrowProjectionWithKey_SynthesizedOrderQualified` |
| SQLite default order back to bare `rowid` | `DefaultPaging_OnJoinEntity_Executes` |
| `Single*`: drop predicate→WHERE | `Single_Predicate_ReturnsTargetNotFirst` |
| `Single*`: row limit 1 instead of 2 | `Single_TwoMatches_Throws`, `SingleOrDefault_TwoMatches_Throws` |
| `Single*`: always limit 2, ignoring a user `Take(1)` | `Single_AfterTake1_OverManyRows_ReturnsRow` (`Take ?? 2` vs `min(Take, 2)` is an equivalent mutant for `Take ≥ 2`; noted) |
| `Single*`: route through the paging path (injects `ORDER BY id`) | `Single_NoUserOrder_EmitsRowLimit_NoIdOrder`, `Single_OnEntityWithoutIdColumn_Works`, `Single_AfterDistinctProjection_Works` |
| `SingleOrDefault` throws on empty | `SingleOrDefault_Predicate_NoMatch_ReturnsNull` |
| `Last*`: don't invert | `Last_AfterOrderByNonIdKey_ReturnsLastInOrder` |
| `Last*`: ignore explicit order, always `Id DESC` | `Last_AfterOrderByNonIdKey_ReturnsLastInOrder` (last row ≠ max id) |
| `Last*`: invert only the first term | `Last_AfterOrderByThenByDescending_InvertsEveryTerm` |
| `Last*`: invert own-column terms only (skip `CASE`/remote) | `Last_AfterTernaryOrderBy_InvertsCaseTerm`, `Last_AfterRemoteOrderBy_ReturnsLastInOrder` |
| `LongCount` missing from the `OuterMethodCall` name list (returns `0L`) | `LongCount_EqualsCount_ReturnsInt64` (count > 0) |
| `LongCount` boxed as `Int32` | `LongCount_EqualsCount_ReturnsInt64` (asserts `long`) |
| Emit `COUNT(*)` on SQL Server | `LongCount_EmitsCountBig` |
| Add `Reverse` to the allow-list | `Rejected_…[Reverse]`, `SupportedOperators_ExactSetPinned` |
| Match by name instead of overload | `Rejected_…[IndexedWhere]`, `[OrderByWithComparer]`, `[DistinctWithComparer]`, `[TakeRange]` |
| `GetGenericMethodDefinition()` without the `IsGenericMethod` check | `NonQueryableOverload_IsRejected`, `Rejected_…[ScalarProjection_ParameterlessSum]` (crash, not NotSupported) |
| Policy visits the whole tree (not just the spine) | `Allowed_PredicateWithCollectionContains_NotRejected` |
| Allow non-`Queryable` spine methods by default | `NonQueryableSpineMethod_Rejected` |
| Classifier allow-by-default | `ClassifierSweep_EveryQueryableMethod_MatchesPinnedSet` |
| Run the policy inside the loop instead of as a pre-pass | `Rejected_OperatorOuterToFailingInnerOperator_PolicyMessageWins` |
| Drop the `GroupBy` special message | `GroupBy_Rejected_KeepsDedicatedMessage` |
| Identity check uses the entity type instead of the source element type | `OfType_Identity_AfterScalarProjection_IsNoOp` |
| Reject all `Cast`/`OfType` | `OfType_Identity_IsNoOp`, `Cast_IdentityNode_HandBuilt_IsAllowed` |
| Allow any `Cast`/`OfType` | `OfType_NonIdentity_Throws`, `Cast_NonIdentity_UnrelatedType_Throws` |
| Drop the positional guard | `Operator_AfterPaging_Rejected_BeforeAnyQuery` rows |
| Positional guard also rejects `Skip(n).Take(k)` | `Operator_AfterPaging_Allowed_MatchesOracle[Skip.Take]` + existing paging tests |
| `Take(0)` sent to the DB | `Take0_*_NoQuery` |
| Average without the float cast (SQL Server) | `Average_IntSelector_OneAndTwo_IsOnePointFive` |
| Keep the selector-type switch (no generic conversion) | `Aggregate_MatchesOracle[Average,decimal]`, `[Max,string]`, `[Sum,int?]` (SQL Server) |
| Nullable-empty throws instead of returning `null` | `Aggregate_MatchesOracle[Min,int?,empty]` |
| Visitor recurses through a prior `OrderBy` (3.9.0 behavior) | `OrderBy_ThenOrderByAgain_MatchesOracle` |
| Ternary null test back to `= NULL` | `TernaryOrderBy_NullCheck_MatchesOracle` |
| SQLite: reset only `_lastOrderByClause` | `SqliteRoot_ReusedAfterProjection_FullEntityNotNarrowed` |

### 4.5 Coverage (coverlet already referenced in all four test projects)

Baseline measured 2026-09-30 (`dotnet test --collect:"XPlat Code Coverage"`, cobertura `line-rate`):

| Touched file | SQL Server | SQLite | MySQL | PostgreSQL |
|---|---|---|---|---|
| `*LinqQueryProvider.cs` | 87.4% | 89.6% | **82.0%** | 88.6% |
| `*OrderByClauseVisitor.cs` | **58.2%** | **59.1%** | **35.6%** | **35.6%** |
| `QueryComponents.cs` / `*QueryComponents.cs` | 100% / 84.2% | 100% / 87.5% | 100% / 100% | 100% / 100% |
| Core `QueryOperatorPolicy.cs` | new | | | |

All touched files must reach **≥ 85%**. Some visitor branches can't be reached through `ParseExpression`: the
`VisitMethodCall` else-throw and the `VisitExpression` default. Task 10 therefore adds **DB-free direct visitor
tests**; the visitors are public. The Core file's coverage comes from `QueryOperatorPolicyTests` plus the
provider suites. Per-file numbers (or the cobertura XML) go into the PR.

### 4.6 Opt-in suites and frameworks

- **SQL Server:** `FUNKY_CONNECTION` (.\SQL2019). **MySQL:** `FUNKY_MYSQL_CONNECTION`. Both are set locally.
- **SQLite:** file-backed, always runs.
- **PostgreSQL:** the suite's fallback connection reaches the native PostgreSQL 18 service; all 143 PG tests
  run locally. The CI PG workflow (`postgres:17`) runs only on push to `development/**`.
- **net48 / net9:** `QueryOperatorPolicyTests` and the existing SQL Server suite run in
  `Funcular.Data.Orm.SqlServer.Tests.NetFramework` and `.DotNet9` locally. Core's netstandard2.0 build reflects
  over net48's `System.Core`, and the package ships net48. The CI net48 job is commented out (ci.yml:89-93),
  so the local run is the gate; results are recorded in the PR.

---

## 5. Design

### 5.1 #12 — qualify own columns in ORDER BY when joins exist

1. **Visitor.** Each provider's order-by visitor gains an optional ctor parameter
   `string tableQualifier = null`. `ResolveOrderColumn` becomes:
   - map hit → resolved fragment, unchanged, never prefixed;
   - otherwise, if `tableQualifier != null` → `$"{tableQualifier}.{GetColumnName(p)}"`;
   - otherwise → `GetColumnName(p)`.

   Every ORDER BY path goes through `ResolveOrderColumn`, so this one change covers them all.
2. **Call sites.** Both sites in each `ParseExpression` resolve
   `var remote = _dataProvider.ResolveRemoteJoins<T>(table)` once and pass:
   - `remote.PropertyToColumnMap`;
   - `tableQualifier: remote.IndividualJoinClauses?.Count > 0 ? table : null`.

   The `Last*` site starts passing the map too.
3. **SQLite default order.** SQLite's default `ORDER BY rowid` becomes `ORDER BY {table}.rowid` when the
   command has joins, the same text check the other three use for `id`.
4. **D11 ternary.** Ternary tests `x.M == null` / `x.M != null` emit `{col} IS NULL` / `IS NOT NULL`.

*Rejected alternatives:*
- **Always qualify:** changes single-table SQL for no benefit.
- **String-rewrite the ORDER BY in `BuildQueryComponents`:** fragile.

### 5.2 #13 — Core pre-pass, then correct `Single*`/`Last*`/`LongCount`/aggregates/ordering

1. **Core `QueryOperatorPolicy`** (`Funcular.Data.Orm.Core/Linq/QueryOperatorPolicy.cs`).
   - **Spine only.** The walk follows `MethodCallExpression.Arguments[0]` from the outermost call down to the
     root. Lambdas and other arguments are never visited, so `Where(p => ids.Contains(p.Id))` is untouched.
   - **Classification.** `IsAllowed(MethodInfo m)` computes the key
     `m.IsGenericMethod ? m.GetGenericMethodDefinition() : m`, then:
     - non-`Queryable` declaring type → rejected;
     - key not in the static allowed set → rejected.

     The allowed set is built once from `typeof(Queryable).GetMethods()` by name plus a shape predicate: one
     `Expression<Func<TSource, …>>` lambda; `Skip`/`Take` take `int`; no comparer, default-value, or `Range`
     parameters. Non-generic `Queryable` overloads are never in the set.
   - **Allowed operators:**
     - `Where`, `Select`;
     - `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`;
     - `Skip`, `Take`, `Distinct`;
     - `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault` (with and without
       predicate);
     - `Any` (with and without predicate), `All`;
     - `Count` and `LongCount` (with and without predicate);
     - `Sum`, `Average`, `Min`, `Max` (generic, with selector);
     - `OfType`/`Cast`, only when the generic argument equals the call's **source element type**
       (`Arguments[0].Type`'s `IQueryable<>` argument).
   - **Positional rules (D8).** Applied inner→outer in the same walk, after the first `Skip`/`Take`, per
     AC13-10.
   - **Messages.**
     - `GroupBy` keeps its dedicated message.
     - Any other rejected operator: `"{Op}(...) is not translated to SQL in this version. Materialize first
       and apply it in memory: query.ToList().{Op}(...)."`
     - Positional rejections: `"{Op}(...) after Skip/Take is not translated …"`.
2. **Wiring.** Each provider's `ParseExpression` calls `QueryOperatorPolicy.EnsureSupported(expression)`
   **first**. For SQLite, that's after the state reset (D11) and before the early return. The inline
   `GroupBy` check becomes unreachable and is removed. The scalar composition and result-type guards stay;
   precedence follows AC13-8.
3. **`Single*` (row limit, not paging).**
   - The First/Last predicate branch generalizes to `First*`/`Single*`/`Last*` × {with, without predicate}.
     It routes the predicate to WHERE and sets `components.Terminal`.
   - **No user `Skip`/`Take`:** set `components.RowLimit = 2`. `BuildQueryComponents` emits
     `SELECT TOP (2)` (or `SELECT DISTINCT TOP (2)`) on SQL Server, and `LIMIT 2` on the others. No ORDER BY
     is synthesized; the user's order, if any, is kept.
   - **User `Take`:** `Take = min(Take, 2)` on the existing paging path (the user already opted into its
     ordering).
   - It reads up to two rows through the list path, then applies LINQ cardinality:
     - 0 rows: `Single` throws "no (matching) element(s)"; `SingleOrDefault` returns `null`;
     - 2 rows: both throw "more than one element".
4. **`Last*`.**
   - The visitor exposes `OrderByTerms`, a structured `(fragment, isDescending)` list covering own, remote,
     and `CASE` terms. `QueryComponents.OrderByTerms` carries it; SQLite carries it alongside its reset
     fields.
   - `Last*` inverts every term. With no terms it synthesizes `{qualified id} DESC` (the "no `Id` property"
     error is kept).
   - It uses `RowLimit = 1` (`TOP 1` / `LIMIT 1`) with that ORDER BY. `Last` throws on empty;
     `LastOrDefault` returns `null`.
   - With `Distinct()` + custom projection and no explicit order: throws `NotSupportedException` naming
     `Last`.
   - This replaces the current `LastOrDefault(pred)` block.
5. **D10 ordering.** At an `OrderBy*` node the visitor computes `[newKey] + priorTerms`; at a `ThenBy*` node,
   `priorTerms + [newKey]`. The result is the LINQ-to-objects stable-sort order.
6. **`LongCount`.** Added to the `OuterMethodCall` name list and to `BuildAggregateClause`. It's treated like
   `Count`, including the reverse-remote-key rejection. SQL Server emits `COUNT_BIG(*)`; the others emit
   `COUNT(*)`. The result converts to `Int64`.
7. **D9 aggregates.**
   - `HandleAggregateQuery` converts `result` to the called overload's return type:
     `Nullable.GetUnderlyingType(TResult) ?? TResult`, via `Convert.ChangeType` (invariant culture).
   - For a `NULL` result:
     - nullable `TResult` → `null`;
     - non-nullable `Min`/`Max`/`Average` → throw "Sequence contains no elements";
     - `Sum` → zero of `TResult`.
   - `Average` over integer selectors: SQL Server emits `AVG(CAST(x AS FLOAT))`. PG returns `numeric` and
     MySQL `decimal` (converted); SQLite returns `real`. Task 1 confirms each with the `{1, 2}` → `1.5` row.
8. **`Take(0)`.** Detected in the pre-pass result (or the `Take` branch). The terminal is resolved without
   SQL: collection → empty; `First`/`Single` → throw; `*OrDefault` → `null`.
9. **D11 SQLite reset.** `_lastSelectProjection`, `_lastSelectParameters` and `_lastOrderByClause` are
   cleared at the top of every `ParseExpression`, before the early return.

---

## 6. Tasks

Each task lists the tests it turns **green**. Every implementation task starts with those tests present and
**red**.

- **Task 0 — Test-plan review gate (no production code).**
  - ✅ Branch; plan committed (`30d3a6d`).
  - ✅ D1–D7 signed off.
  - ✅ Premises re-verified on all four providers.
  - ✅ Baselines recorded.
  - ✅ #12/#13 retitled, #13 marked critical, evidence posted.
  - ✅ Non-author review: 25 findings, all accepted (§9).
  - ⏳ **Owner signs off D8–D11.**
  - Then post the §3 ACs to #12/#13, commit Revision 2, and start Task 1.
- **Task 1 — Stubs, harness, all red tests.**
  - Add compile-only stubs so the red run is a runtime failure, not a build break: `QueryOperatorPolicy`
    (members throw `NotImplementedException`), the visitor `tableQualifier` parameter, `OrderByTerms`, and
    the `QueryComponents` members.
  - Add the §4.1 harness, the SQLite schema helper, and the no-`id` test entity.
  - Link `QueryOperatorPolicyTests` into the net48/net9 projects.
  - Write every §4.2 test. Run them all and record each as red, or as expected-green for regression rows.
  - Confirm the UNVERIFIED items: SQL Server `AVG(int)` truncation; `Queryable.Cast` short-circuit on net8
    and net48.
- **Task 2 — #12 qualifier** (4 providers × visitor + 2 call sites).
  → AC12-1…AC12-6.
- **Task 3 — SQLite `rowid` qualification.**
  → AC12-7.
- **Task 4 — Core policy + positional guard + wiring** (4 providers).
  → AC13-4, AC13-6, AC13-7, AC13-8, AC13-10 (rejection rows).
- **Task 5 — `Single*` row limit + `Take(0)`** (4 providers).
  → AC13-1, AC13-10 (allowed rows, `Take0_*`).
- **Task 6 — Visitor: `OrderByTerms`, D10 ordering, ternary `IS NULL`** (4 providers).
  → AC13-12, AC12-8.
- **Task 7 — `Last*`** (4 providers).
  → AC13-2, AC12-5.
- **Task 8 — `LongCount`** (4 providers).
  → AC13-3.
- **Task 9 — Aggregate conversion + `AVG` cast** (4 providers).
  → AC13-11.
- **Task 10 — SQLite state reset; coverage lift.**
  - SQLite state reset → AC13-13.
  - DB-free direct visitor tests.
  - `MySqlLinqQueryProvider.cs` from 82% to ≥85%.
  - Per-file numbers attached.
  → §4.5 all ≥85%.
- **Task 11 — Docs.**
  - Operator table in `Advanced.md` + `FUNKYORM_AI_ADVANCED.md`, generated from `SupportedOperators` or kept
    in sync by a doc test. It includes the paging rule and the `Last`/`Distinct` note.
  - Changelog 3.10.0:
    - **Fixed (correctness):** `Single*`, `Last*`, `LongCount`, aggregates, double `OrderBy`, the SQLite
      paging/state bugs, and the ternary null check.
    - **Changed:** untranslated operators, and operators after `Skip`/`Take`, now throw.
  - README "Recent Changes": "upgrade strongly recommended".
  - Usage.md: `LongCount`.
  → AC13-9.
- **Task 12 — Gauntlet and release.**
  - Record the §4.4 mutation runs.
  - Run the net48/net9 policy tests.
  - A fresh adversarial pass by a non-author agent, then a fix-verification pass over any fix layer.
  - Write the sentinel only for the exact clean sha.
  - Push `development/3.10` (CI + MySQL + PostgreSQL) → PR → merge → `3.10.0-beta1`.
  - Sentinel call-list smoke test → promote to `3.10.0`.
  - Close #12/#13. Owner decides on the Security Advisory (D7).

**Rough effort:**

| Work | Estimate |
|---|---|
| Tasks 1 (stubs + ~150 test rows × 4 providers) | ~2 days |
| #12 (Tasks 2–3) | ~0.5–1 day |
| #13 core (Tasks 4–5, 7–8) | ~2 days |
| Visitor + aggregates + SQLite (Tasks 6, 9, 10) | ~2 days |
| Docs + gauntlet (Tasks 11–12) | ~1 day |
| **Total** | **~8–9 working days** |

---

## 7. Risks

- **Someone relies on a silently ignored or mistranslated operator.** Examples: `.OrderBy(..).Last()`
  returning the "first" row, `Take(5).Count()` counting everything, or a truncated `Average`. That code now
  throws or returns a different value. **Mitigation:** minor version, a prominent Changelog "Changed/Fixed"
  list, and messages that say what to write instead.
- **Over-rejection of a valid shape.** **Mitigation:** the allowed set is built from what the loop already
  translates; AC13-5 oracle rows cover each family; the existing suites are the regression gate. No FunkyORM
  test or Sentinel query composes after paging.
- **Mirror drift.** **Mitigation:** policy and positional rules live in Core; per-provider changes are proved
  by the same test names in each suite.
- **Row-limit syntax differences.** `TOP (n)` must follow `DISTINCT` on SQL Server, and `LIMIT` must come
  after ORDER BY. Covered by `Single_AfterDistinctProjection_Works` and the `Last` tests.
- **net48 reflection surface** differs from net8's (fewer `Queryable` methods). **Mitigation:** the pinned
  set is framework-aware (asserted per TFM), and the net48 sweep runs locally.

## 8. Out of scope (recorded, not fixed here)

- `GenerateOrderByClause` in three providers is dead code.
- SQLite provider state is not safe for **concurrent** execution of queries composed from one `Query<T>()`
  root. Sequential reuse is fixed by D11.
- **Unordered default paging hard-codes `id`** (`ORDER BY id` / `{table}.id` / `rowid`), which is wrong for
  entities whose key column isn't `id`. This is pre-existing and only reached by user `Skip`/`Take` without
  `OrderBy`. `Single*` no longer routes through it (§5.2.3). A resolved-PK default order is a follow-up issue.
- Sentinel.MVP pins `3.9.0-beta1`; upgrading it is the D3 smoke test, tracked in Sentinel.

---

## 9. Review disposition — Task 0 test-plan review of `30d3a6d`

Blame classes are evaluated in order; the first that applies wins. Ledger totals: **AC-GAP 10, TEST-GAP 11,
HOUSE-RULE 0, PLAN-GAP 4, OTHER 0.**

| # | Sev | Blame | Finding (short) | Disposition |
|---|---|---|---|---|
| 1 | BLOCKER | AC-GAP | Operators after `Skip`/`Take` mistranslated (aggregates ignore paging; `Where`/predicate terminals before OFFSET; repeated `Skip`/`Take` overwrite; `Take`→`Distinct`) | Accepted, confirmed in source. D8, AC13-10, positional guard in Core (Task 4), tests + mutation rows. |
| 2 | MAJOR | AC-GAP | `Single(pred)` after `Skip` can't meet AC13-1 | Accepted. AC13-1 limits composition to parameterless `Single*`; predicate form rejected by D8. |
| 3 | MAJOR | AC-GAP | Second `OrderBy` reverses priority | Accepted, confirmed (OrderByClauseVisitor.cs:117-134). D10, AC13-12. |
| 4 | MAJOR | TEST-GAP | Schema discovery at `Query<T>()`, unlogged; "no SQL" unobservable; mutation killer survives | Accepted. AC13-4 reworded, D2 rationale corrected, killer replaced. |
| 5 | MAJOR | AC-GAP | `Single*` via paging injects `ORDER BY id` | Accepted. Row-limit design (§5.2.3), AC13-1 bullet, no-`id` entity test. Pre-existing default-order issue logged in §8. |
| 6 | MAJOR | AC-GAP | `Single*`/`Last*` + `Distinct` projection | Accepted. Row limit avoids the paging guard for `Single`; `Last` rule in AC13-2. |
| 7 | MAJOR | AC-GAP | Aggregate overloads: `decimal`/`float` `Average` cast; nullable-empty; SQL Server `int?`; `AVG(int)` truncation | Accepted. D9, AC13-11, Task 9. Truncation confirmed in Task 1. |
| 8 | MAJOR | AC-GAP | SQLite projection-state leak; §8 claim false | Accepted, confirmed (fields set only at :315-316). D11, AC13-13; §1.4/§8 corrected. |
| 9 | MAJOR | TEST-GAP | `Cast` tests toothless (short-circuit); identity used entity type | Accepted. Source-element identity; hand-built node test; unrelated-type test; `OfType` after scalar. |
| 10 | MINOR | PLAN-GAP | `GetGenericMethodDefinition()` on non-generic overloads | Accepted. `IsGenericMethod` guard + tests. |
| 11 | MINOR | TEST-GAP | AC13-8 unprovable; weak message asserts; sibling tests missing | Accepted. AC13-8 restated with precedence; distinctive-text asserts; sibling tests added. |
| 12 | MINOR | TEST-GAP | `Single*`/`Last*` after scalar projection untested | Accepted. `ScalarProjection_WithSingleOrLast_ThrowsNotSupported`. |
| 13 | MINOR | TEST-GAP | Missing mutation rows (a)–(e) | Accepted. All added to §4.4. |
| 14 | MINOR | TEST-GAP | "Walks the whole chain" ambiguous | Accepted. Spine-only walk specified; two tests + mutation. |
| 15 | MINOR | TEST-GAP | Oracle determinism | Accepted. §4.1 determinism rules. |
| 16 | MINOR | TEST-GAP | Classification sweep tautological | Accepted. `IsAllowed` seam drives the sweep. |
| 17 | MINOR | AC-GAP | net48/net9 not in the gate | Accepted. AC13-7 runs on net48/net9; §4.6. |
| 18 | MINOR | PLAN-GAP | Red tests won't compile | Accepted. Task 1 compile stubs. |
| 19 | MINOR | PLAN-GAP | Unreachable visitor branches; PG `QueryComponents` baseline; fixture inheritance | Accepted. DB-free direct tests; baseline recorded (100%); DB-free rule. |
| 20 | MINOR | AC-GAP | Ternary `= NULL` bug | Accepted in scope (D11, AC12-8). |
| 21 | MINOR | TEST-GAP | No-`Id` `Last` error untested | Accepted. Test added. |
| 22 | MINOR | TEST-GAP | `LongCount` reverse-key rejection and `COUNT_BIG` unasserted | Accepted. Tests added. |
| 23 | MINOR | TEST-GAP | AC12-1 combinations incomplete; AC12-6 entity unspecified | Accepted. 24-row data test; AC12-6 pinned to the join entity. |
| 24 | MINOR | AC-GAP | `Take(0)` + `Single*` → SQL error | Accepted. `Take(0)` short-circuit in D8/AC13-10. |
| 25 | MINOR | PLAN-GAP | Affected-version wording | Accepted. `0.1.1-alpha.1` not on NuGet (verified), so the range holds; "first provider" wording fixed. |
