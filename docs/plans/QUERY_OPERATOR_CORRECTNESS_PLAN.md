# Query Operator Correctness (#12, #13) — Implementation Plan

> **Goal**: Fix the two query-translation defects logged after 3.9.0 — [#12](https://github.com/FuncularLabs/Funcular.FunkyOrm/issues/12)
> (unqualified own-column `ORDER BY` on remote-join entities) and [#13](https://github.com/FuncularLabs/Funcular.FunkyOrm/issues/13)
> (LINQ operators silently ignored or mistranslated) — across all four providers, test-first.
> - **v3.10.0** (branch `development/3.10`) ships the critical fixes. §1–§9 cover it.
> - **v3.10.1** ships aggregate correctness (D9). Its design is still being settled with the owner (§10).
>
> This is a **prerequisite for the sponsored-tutorial program**. A tutorial audience coming from EF will reach
> for `SingleOrDefault` and the narrow-projection paging idiom on day one.

> **Status (2026-09-30)**:
> - All decisions D1–D12 are made by the owner, and the owner has answered the three §10 questions.
> - Task 0 is waiting on a clean fix-verification of this revision (r4).

> **Revision 4 (Task 0 fix-verification r3, 2026-09-30) — what changed:**
> The r3 reviewer found 10 issues at `e48f2b1`: 0 blocker, 1 major, 9 minor. Blame: AC-GAP 3, TEST-GAP 4,
> PLAN-GAP 3. Disposition is in §9.3.
> - **D10 extended (N2, major).** A `ThenBy*` separated from an earlier ordering by a non-ordering call is
>   rejected too. Every FunkyORM queryable is an `IOrderedQueryable`, so
>   `((IOrderedQueryable<T>)q.OrderBy(a).Where(w)).ThenBy(b)` compiles, and it silently drops `a` today. Same
>   class, same rule.
> - **Covariance (N1).** The `TSource` check no longer rejects parameterless terminals that work today, such as
>   `IQueryable<object> q = db.Query<T>(); q.Count()`. It still rejects lambda-bearing and sequence operators.
> - **Test-framework legs (N3).** The policy literal-set test has an **xUnit twin** in the net9 project (which is
>   xUnit). The net48 leg is named: `Funcular.Data.Orm.SqlServer.Tests.NetFramework`.
> - **Test fixes (N4–N6).** `Skip`-only `Single` asserts its SQL shape (`LIMIT 2 OFFSET n` / `FETCH NEXT 2`).
>   The empty-`Take` short-circuit runs after the scalar guard. The D10 rows use a subset `Select`. Every
>   rejection row asserts its specific message.
> - **Oracle rules (N7, N8).** Oracle predicates reference only non-null members, and never follow a subset
>   `Select`. The two pre-existing divergences (SQL NULL semantics; a `Where` after a subset `Select` reading
>   base columns) are recorded in §8 as follow-ups.
> - **§10 (N9 + owner answers).** The `time`-range wording is corrected. The Guid opt-in is a provider-wide
>   option, with PostgreSQL allowed by default once verified. `OrderBy` on Guid isn't guarded. String
>   `Min`/`Max` use database collation.
> - **Bookkeeping (N10).** §9.2 said 16 of 21 were fix-introduced; it's 19. Also: `OfType` after paging aligned
>   with D5, the `Id DESC` qualification wording, the §8 `Single` wording, and the §1.4 premise.
> - `QueryOperatorPolicy.IsAllowed` is **public**, so the test seam doesn't depend on `InternalsVisibleTo`.
>   That dependency would break if the assemblies are strong-named later.

> **Revision 3 (Task 0 fix-verification r2, 2026-09-30) — what changed:**
> The r2 reviewer found 21 issues at `e19a7ae`: 0 blocker, 6 major, 15 minor. 19 were introduced by
> Revision 2. Blame: AC-GAP 5, TEST-GAP 7, HOUSE-RULE 1, PLAN-GAP 8. Following the house rule after repeated
> fix-introduced findings, the two unsound designs were **redesigned rather than patched**:
> - **D10 (second `OrderBy`) is now "reject"** (owner decision). This removes the stable-sort stitching, the
>   tie-seeded tests and the repeated-column problem.
> - **D9 (aggregates) moves to v3.10.1** (owner decision D12). The r2 findings about `Average` precision
>   (SQLite `ROUND`, MySQL scale 4), `ChangeType` failures, and missing test-schema columns become inputs to
>   §10.
>
> The remaining findings are folded in:
> - identity `OfType` over nullable elements is rejected, because LINQ drops nulls;
> - the spine walk is fully specified (element-type resolution, root terminal, `TSource` check);
> - SQLite `Skip` without `Take` gets its own fix (AC13-14);
> - negative `Skip`/`Take` handling;
> - the `Take(0)` short-circuit is pinned;
> - #12 tests assert SQL text and use projections that omit the key, so they can't pass by alias binding;
> - SQLite resets all per-parse state;
> - one literal pinned set across frameworks;
> - net48 compile constraints;
> - the dead scalar-aggregate guard and its stale comment are removed;
> - ternary null checks in either operand order;
> - duplicate `ThenBy` keys are deduplicated.
>
> The disposition is in §9.2. **3.10.0 effort: ~6 working days.**
>
> **Earlier revisions:**
> - **Revision 2** — Task 0 test-plan review r1: 25 findings, disposition in §9.1.
> - **Revision 1** — cross-provider premise verification.

---

## 1. Verified premises

Before planning, throwaway probes ran each shape against live databases on all four providers, then were
deleted. Their cases become the red tests in §4.

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
- `ParseExpression` translates a fixed set of operator **names** and silently skips every other call in the
  chain.
- `ExecuteQuery` then reads the first row for any non-collection `TResult`.
- `Single*` is never matched.
- The `First/Last` predicate branch requires `Arguments.Count == 2`, so parameterless `Last()` falls through
  to "read first row".

**Severity: critical (silent wrong row).** `Query<User>().SingleOrDefault(u => u.Email == email)` returns
an arbitrary *different* user. In a consuming app that is an authorization flaw.

**Affected versions:** every NuGet release from `0.1.1-alpha.2` (the first published version) through
`3.9.0`. `Single*` has never been matched in any version of the LINQ provider. An earlier `IQueryProvider`
at `1a96b3d` (2025-03-30) predates every published package.

**Also confirmed from source by the Task 0 reviews:**
- Aggregates ignore paging (`BuildAggregateClause` never reads `Skip`/`Take`).
- `Where` and predicate terminals after `Skip`/`Take` are ANDed into `WHERE`, so they run before paging.
- Repeated `Skip`/`Take` overwrite each other.
- `OrderBy(a).OrderBy(b)` emits `ORDER BY a, b`.
- SQLite `_lastSelectProjection`/`_lastSelectParameters` are never reset.
- An ORDER BY ternary `x.M == null` emits `col = NULL`.
- SQLite emits `OFFSET` without `LIMIT` for `Skip`-only queries, which SQLite's grammar rejects
  (*Task 1 red test*).
- Aggregate result conversion is wrong for several overloads; that's 3.10.1 (§10).

### 1.2 #12 is narrower than filed on SQL Server, PostgreSQL and MySQL (broader on SQLite — see §1.3)

| Query on `PersonDetailEntity` (joins `organization`, `address`, `country` — all have `id`) | 3.9.0 result |
|---|---|
| `OrderBy(p => p.Id).Take(3).ToList()` (full entity) | ✅ works — `ORDER BY id` binds to the single `id` column in the SELECT list |
| `OrderBy(p => p.Id).Take(3).Select(p => p.MiddleInitial)` (scalar projection) | ❌ `Ambiguous column name 'id'` |
| `OrderBy(p => p.Id).Take(3).Select(p => new PersonDetailEntity { FirstName = p.FirstName })` | ❌ `Ambiguous column name 'id'` |
| `OrderBy(p => p.EmployerHeadquartersCountryName).ThenBy(p => p.Id)` + subset projection | ❌ `Ambiguous column name 'id'` (`ORDER BY [country_0].name ASC, id ASC`) |
| `OrderBy(p => p.FirstName)` + `Select(p => p.Id)` | ✅ works — only because no joined table has `first_name` |
| `Where(p => p.Id > 0)` + default paging | ✅ WHERE emits `person.id`; default order is `person.id` (3.9 round-1 fix) |

**Real trigger:** an own-column `OrderBy`/`ThenBy` combined with a **narrow projection that doesn't include
the key** on a remote-join entity, when any joined table has a column of the same name. When the key *is*
projected, the unqualified name binds to the select alias and works by accident.

**Consumer exposure:** Sentinel.MVP `main` (pinned to 3.9.0-beta1) uses this exact shape in `CallListOrder.Apply`.
- It works only because `CallId` maps to `call_id` and no joined table has that name.
- No Sentinel code uses a #13 operator on a FunkyORM query or composes an operator after `Skip`/`Take`.
- Its `OrderBy` chains contain one `OrderBy` each, so D10 doesn't affect it.

### 1.3 Cross-provider verification (Task 0, 2026-09-30)

Same shapes on PostgreSQL (local PG 18), MySQL and SQLite. Each probe seeded three marker-tagged rows
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
| Default paging (`Take`) on join entity | ✅ `person.id` | ✅ `person.id` | ✅ `person.id` | ❌ `ambiguous column name: rowid` |

**Consequence for SQLite:** on 3.9.0, any `Skip`/`Take` on an entity with remote attributes fails, and
`Skip` without `Take` fails on any entity.

### 1.4 Code facts the design relies on (verified by author and reviewers)

- **Order-by visitors.**
  - Each provider has its own. All four resolve own columns only through a private `ResolveOrderColumn` →
    bare `GetColumnName(property)`; every ORDER BY path goes through it.
  - Each term is already stored as `(ColumnName, IsDescending)`, so inverting terms needs no text parsing.
  - The visitor recurses into a prior ordering call, including a prior `OrderBy`, and stops at
    non-ordering calls (OrderByClauseVisitor.cs:117-134).
- **WHERE and SELECT already qualify.** All four WHERE visitors qualify own columns as `{_tableName}.{column}`;
  `SelectClauseVisitor` emits `person.id AS Id`.
- **Two visitor call sites per provider.** The `OrderBy/ThenBy` branch passes the remote map; the
  `Last/LastOrDefault(pred)` synthesized order passes none. `GenerateOrderByClause` (three providers) is dead
  code.
- **"Has joins" test.** `ResolvedRemoteJoinInfo.IndividualJoinClauses` has the same name in all four and is
  non-empty exactly when joins are emitted.
- **Eager schema discovery.** `Query<T>()` discovers schema eagerly with raw commands that **don't log**. So
  "no SQL executed" is observable only as "no *query or aggregate* command logged after the `IQueryable` is
  obtained".
- **The query root** is `Expression.Constant(this)`, typed as the provider's queryable class, e.g.
  `SqlQueryable<T>` (SqlQueryable.cs:17). After `OrderBy` the node type is `IOrderedQueryable<T>`. Element
  types must therefore be resolved through the `IEnumerable<>` interface.
- **`Skip`/`Take` arguments** are always a `ConstantExpression`, so their values are known during parsing.
- **Default paging order.** SQLite's is a bare `ORDER BY rowid`; the other three qualify `id` with a
  `" JOIN "` text check. It hard-codes the column name `id` (pre-existing, §8).
- **SQLite paging SQL** emits `LIMIT n` and `OFFSET m` separately (SqliteLinqQueryProvider.cs:592-595), so a
  `Skip`-only query emits `OFFSET` with no `LIMIT`.
- **Aggregates (unchanged in 3.10.0 except `LongCount`).**
  - `BuildAggregateClause` ignores `Skip`/`Take`.
  - SQL Server, PostgreSQL and MySQL convert by a selector-type switch; SQLite switches on `TResult` with a
    `ChangeType` fallback (:623-634).
  - `Average` returns `Convert.ToDouble` everywhere.
  - SQLite wraps `AVG` in `ROUND(…, 10)` (:461-462).
- **SQLite per-parse state.** `_lastOrderByClause` (overwritten after each loop), plus `_lastSelectProjection`
  and `_lastSelectParameters` (set only in the Select branch, never reset). The early return for an
  operator-free expression (:119) happens before any assignment.
- **Scalar-projection guards.** The guard for a parameterless numeric aggregate after a scalar projection
  (SqlLinqQueryProvider.cs:339-346; the same in the siblings) becomes dead once the pre-pass rejects
  non-generic `Sum()`/`Average()` and parameterless generic `Min<T>()`/`Max<T>()` (the "with selector" rule).
  The comment at :128-129 then becomes false.
- **Every FunkyORM queryable implements `IOrderedQueryable<T>`** (SqlQueryable.cs:9 and the siblings). So a
  cast lets `ThenBy*` follow any call, and the visitor, which stops at non-ordering calls
  (OrderByClauseVisitor.cs:120-128), silently drops the earlier ordering.
- **Covariance.** `IQueryable<object> q = db.Query<T>()` creates no node, so `q.Count()` is
  `Count<object>(root)`. In 3.9.0, parameterless terminals over a covariant source work: `Count`/`Any` take
  the aggregate path, and `First*` reads a `T` and casts it. Lambda-bearing operators and collection results
  over a covariant source throw `InvalidCastException`.
- **`Queryable.Cast<TResult>`** returns the source unchanged when it's already `IQueryable<TResult>`, so no
  node is created. `OfType` always creates a node and, like `Enumerable.OfType`, **drops nulls**.
- **Core `InternalsVisibleTo`** already covers `SqlServer.Tests`, `.DotNet9` and `.NetFramework`.
- **The net48 test project** is an old-style csproj compiled as C# 7.3. net48's `Queryable` lacks
  `Chunk`/`DistinctBy`/`MinBy`/`Order`/`Take(Range)`/the default-value overloads.

---

## 2. Decisions (all made by the owner, 2026-09-30)

| # | Decision | Outcome |
|---|---|---|
| **D1** | `Single`/`SingleOrDefault`/`Last`/`LastOrDefault`/`LongCount` | **Implement.** `Single*` uses a row limit, not the paging path (§5.2.3). |
| **D2** | Allow-list form | **Exact-overload allow-list in Core** (`QueryOperatorPolicy`), run as a **pre-pass over the method spine** before translation. It guarantees that no query or aggregate command runs for a rejected chain; schema discovery at `Query<T>()` is unaffected. |
| **D3** | Version / branch | **3.10.0** on `development/3.10`; beta first, stable after a Sentinel call-list smoke test. |
| **D4** | Local PostgreSQL | Native PostgreSQL 18 via the suite's fallback connection; CI uses `postgres:17`. |
| **D5** | `Cast`/`OfType` | **Identity only.** The generic argument must equal the call's source element type.<br>• `Cast`: allowed as a no-op.<br>• `OfType`: allowed as a no-op only when the element type is the entity `T` (rows are never null) or a non-nullable value type.<br>• Everything else is rejected: `OfType` over a nullable or reference scalar would have to drop nulls, and the message says to use `Where(x => x.M != null)` before the projection. |
| **D6** | `Last*`/`ElementAt*` with paging | Reject both. |
| **D7** | Disclosure | Changelog + README "upgrade strongly recommended"; the owner decides on a GitHub Security Advisory. |
| **D8** | Operators after `Skip`/`Take` | **Reject by position** in the Core pre-pass. Allowed after the first `Skip`/`Take`:<br>• one `Take` directly after a `Skip`;<br>• `Select`;<br>• parameterless `First*`/`Single*`;<br>• identity `OfType` (per D5).<br>Everything else is rejected before any query runs.<br>`Take(n ≤ 0)` returns an empty sequence without SQL. `Skip(n < 0)` is treated as `Skip(0)`. |
| **D9** | Aggregate correctness | **Moved to 3.10.1** (D12). The owner accepts the recommended direction with changes; details and open questions are in §10. In 3.10.0, aggregates keep their 3.9 behavior, except `LongCount` (new) and rejection of the non-generic `Sum()`/`Average()` overloads, which already threw in 3.9. |
| **D10** | A second `OrderBy*` after an earlier `OrderBy*`/`ThenBy*` anywhere on the spine; and *(rev 4, same class)* a `ThenBy*` whose immediate source isn't an ordering call when an earlier ordering exists | **Reject**, in the Core pre-pass. The message says to use `ThenBy`, or put the primary key first. A `ThenBy*` with no earlier ordering at all (via an `IOrderedQueryable` cast of an unordered query) stays allowed: it works today as the only ordering. |
| **D11** | SQLite state and ternary nulls | **Fix in scope.**<br>• SQLite resets **every** per-parse field (including the new `OrderByTerms`) at the top of every parse, before the early return.<br>• ORDER BY ternary null tests emit `IS NULL`/`IS NOT NULL`, with `null` on either side of the comparison. |
| **D12** | Release sequencing | **Split.** 3.10.0 ships everything in this document except D9; 3.10.1 ships D9 (§10). |

---

## 3. Acceptance criteria (v3.10.0)

These will be posted to #12 and #13 once this revision is verified clean. Each AC holds for **all four
providers** unless stated.

### #12 — own-column ORDER BY on remote-join entities

- **AC12-1** On an entity with remote joins, every combination below emits own columns as
  `{baseTable}.{column}` (asserted on the SQL text) and returns rows in the requested order:
  - operator: `OrderBy`/`OrderByDescending`, or `ThenBy`/`ThenByDescending` after a remote-member `OrderBy`,
    over an own column;
  - shape: full entity, subset projection that **omits the key**, or scalar projection of a different member;
  - paging: with or without `Skip`/`Take`.
- **AC12-2** Ordering by a remote/computed member emits exactly its resolved fragment, with no base-table
  prefix, unchanged from 3.9.0.
- **AC12-3** For an entity without remote joins, the generated SQL is byte-identical to 3.9.0.
- **AC12-4** A ternary/`CASE` ordering over own columns on a join entity is qualified inside the `CASE`.
- **AC12-5** The ORDER BY synthesized for `Last*` (AC13-2) is qualified on join entities, including with a
  narrow projection that omits the key (no `Distinct`).
- **AC12-6** On the join entity, `Distinct()` + custom projection + own-column `OrderBy`:
  - executes when the key is in the projection;
  - still throws the existing `InvalidOperationException` when it isn't.
- **AC12-7** *(SQLite)* `Skip`/`Take` on a join entity with no explicit order executes, for the full entity
  and for a subset projection.
- **AC12-8** An ORDER BY ternary whose test compares a member with `null`, with `null` on either side
  (`x.M == null`, `null == x.M`, `x.M != null`, `null != x.M`), orders rows the way LINQ-to-objects does.
- **AC12-9** A duplicate ordering key (`OrderBy(a).ThenBy(a)`) executes. Later duplicate fragments are
  dropped; they can never break a tie, so the order is unchanged.

### #13 — operator correctness

- **AC13-1** `Single`/`SingleOrDefault` match LINQ-to-objects:
  - The predicate, if any, is applied as `WHERE`.
  - Zero rows: `Single` throws `InvalidOperationException`; `SingleOrDefault` returns `null`.
  - Two or more rows: both throw `InvalidOperationException`.
  - With no user `Skip`/`Take`, it emits `TOP 2` / `LIMIT 2` and **no synthesized `ORDER BY`**. It works on
    an entity whose key column isn't `id`, and after `Distinct()` + custom projection.
  - **Parameterless** `Single*` after user paging reads `min(Take ?? 2, 2)` rows: `Skip(n)` only,
    `Skip(n).Take(k)`, and `Take(1)` (never throws "more than one").
- **AC13-2** `Last`/`LastOrDefault` (with and without a predicate) match LINQ-to-objects:
  - **Explicit order.** Every term of the `OrderBy`+`ThenBy` chain is inverted: own, remote/computed and
    `CASE` terms.
  - **No order.** They default to `Id DESC`, table-qualified when the entity has joins (as in AC12-3/§5.1).
    The existing "no `Id` property" error is kept.
  - **Empty.** `Last` throws; `LastOrDefault` returns `null`.
  - **After `Skip`/`Take`.** Rejected (D6/D8).
  - **After `Distinct()` + custom projection with no explicit order.** Throws `NotSupportedException` naming
    `Last`. With an explicit order on a projected key, it works.
- **AC13-3** `LongCount()` and `LongCount(pred)` return an `Int64` equal to `Count`. SQL Server emits
  `COUNT_BIG(*)`. A predicate over a reverse remote key is rejected like `Count`.
- **AC13-4** Every spine node that isn't an allowed `Queryable` overload throws `NotSupportedException`
  naming the operator, and **no query or aggregate command is executed**: `Log` receives nothing after the
  `IQueryable` is obtained. That includes:
  - non-`Queryable` methods;
  - non-generic `Queryable` overloads;
  - lambda-bearing or sequence operators whose `TSource` doesn't match the source element type (e.g.
    `Cast<object>().Where(…)`, `((IQueryable<object>)q).Take(5)`);
  - non-call, non-root nodes.

  **Covariant parameterless terminals are not rejected** over `IQueryable<object>` (or an interface) of the
  entity:
  - `Count`, `Any` and `First*` keep their 3.9.0 behavior, which is correct;
  - `LongCount`, `Single*` and `Last*` get the corrected semantics of AC13-1/AC13-2/AC13-3.

  Lambdas nested inside allowed operators aren't inspected, so `Where(p => ids.Contains(p.Id))` still works.
  The rejection table covers, at minimum:
  - **Ordering and slicing:** `Reverse`, `TakeWhile`, `SkipWhile`, `TakeLast`, `SkipLast`, `ElementAt`,
    `ElementAtOrDefault`, `Order`, `OrderDescending`.
  - **Set, join and sequence:** `DefaultIfEmpty`, `Concat`, `Union`, `Intersect`, `Except`, `Zip`,
    `SelectMany`, `Join`, `GroupJoin`, `Append`, `Prepend`, `SequenceEqual`, `Chunk`.
  - **Reducing:** `Contains`, `Aggregate`, `DistinctBy`, `MinBy`, `MaxBy`, and parameterless `Min()`/`Max()`
    on an entity.
  - **Type filters:** non-identity `Cast`/`OfType`, and `OfType` over a nullable element (D5).
  - **Unsupported overloads of allowed operators:** indexed `Where`/`Select`; comparer overloads;
    default-value overloads; `Take(Range)`; non-generic `Sum()`/`Average()`.
- **AC13-5** Every shape that worked correctly in 3.9.0 still works identically. All four existing suites
  stay green, and there's one oracle row per allowed family. Aggregate rows are limited to shapes correct in
  3.9.0 on all four providers: `Count`, `Any`/`All`, and `Sum`/`Min`/`Max` over `int`, on non-empty sets. No
  `Average` rows: SQLite's `ROUND(…, 10)` alters even `double` averages. The rest is 3.10.1. Oracle predicates
  reference only members seeded non-null, and oracle shapes never place a predicate or ordering after a
  subset `Select` (§4.1, §8).
- **AC13-6** *(D5)* Identity `OfType<X>()` is a no-op on the entity (at the root and after `OrderBy`) and on a
  non-nullable scalar (`Select(p => p.Id).OfType<int>()`). `OfType` over a nullable or reference scalar,
  non-identity `OfType`, and non-identity `Cast` throw.
- **AC13-7** One **literal** pinned set of allowed overloads (count + signatures) is asserted identically on
  three runtimes:
  - net8, in `SqlServer.Tests` (MSTest);
  - net48, in `Funcular.Data.Orm.SqlServer.Tests.NetFramework` (MSTest, linked file);
  - net9, in `SqlServer.Tests.DotNet9` (an **xUnit twin**: that project is xUnit).

  On each runtime, a sweep drives the public classifier (`IsAllowed`) over every public `Queryable` method and
  compares the result with that literal set. Consumers run the net8 build on newer runtimes, whose
  `Queryable` gains methods, so this matters.
- **AC13-8** Allowed operators keep their specific messages: the post-scalar-projection composition message,
  the scalar result-type guard, and `GroupBy`'s dedicated text. Rejected operators get the policy message,
  even after a scalar `Select`. Precedence: pre-pass (policy, position, D10) → parse-loop guards →
  execute-time guards.
- **AC13-9** `Advanced.md` and `FUNKYORM_AI_ADVANCED.md` carry the operator table, including the paging rule,
  the second-`OrderBy` rule and the `Last`/`Distinct` note. A doc test checks the table against
  `SupportedOperators`. Changelog and README record the fixes, the behavior changes, and the aggregate known
  issues deferred to 3.10.1.
- **AC13-10** *(D8)* After `Skip`/`Take`:
  - **Allowed and correct:** `Skip(n).Take(k)`, `Select` (subset or scalar), parameterless
    `First*`/`Single*`, and identity `OfType` per D5 (the entity, or a non-nullable scalar).
  - **Rejected before any query**, each asserting the "after Skip/Take" message: every aggregate, `Where`,
    predicate-bearing `First*`/`Single*`, `Last*`, `OrderBy*`, `Distinct`, a second `Skip`/`Take`, and
    `Take(n).Skip(m)`.
  - **Empty results without SQL:** `Take(n ≤ 0)` returns an empty sequence, whether it's the full entity,
    a subset projection, a scalar projection, or `Skip(n).Take(0)`. `First`/`Single` then throw and
    `*OrDefault` returns `null`.
  - `Skip(n < 0)` behaves as `Skip(0)`.
- **AC13-11** *(deferred to 3.10.1 — §10.)*
- **AC13-12** *(D10)* Both of these throw `NotSupportedException` with the D10 message before any query runs:
  - `OrderBy*` after any earlier `OrderBy*`/`ThenBy*` on the spine, including across
    `Where`/`Select`/`Distinct` in between;
  - a `ThenBy*` whose immediate source isn't an ordering call when an earlier ordering exists (e.g.
    `((IOrderedQueryable<T>)q.OrderBy(a).Where(w)).ThenBy(b)`).

  A `ThenBy*` with no earlier ordering still works as the primary ordering. That's what the common
  `q is IOrderedQueryable<T> o ? o.ThenBy(k) : q.OrderBy(k)` helper produces, because every FunkyORM queryable
  is an `IOrderedQueryable`.
- **AC13-13** *(D11, SQLite)* A query doesn't inherit a projection, parameters, ordering or `OrderByTerms`
  from an earlier execution on the same `Query<T>()` root, including when the later query is the bare root.
- **AC13-14** *(SQLite)* `Skip(n)` without `Take` executes: it emits `LIMIT -1 OFFSET n`. That covers
  `Skip(n).ToList()` and `Skip(n).First()`. (`Skip(n).Single()` is capped by AC13-1, so it emits
  `LIMIT 2 OFFSET n`.)

---

## 4. Test plan (written and reviewed first — Task 0)

### 4.1 Harness

- **Discriminating seed data.**
  - Each semantics test inserts its own marker-tagged rows and scopes every query with
    `Where(p => p.LastName == marker)`.
  - The target row is neither the first nor the last by id. For `Last`, the order key is not `Id`, and its
    last row isn't the max id.
  - Each test states which wrong answer it rules out.
  - Counts are > 0.
- **#12 ordering assertions.**
  - Rows are seeded so that `FirstName` order equals `Id` order (`"a"`, `"b"`, `"c"`).
  - AC12-1/AC12-5 subset rows project `FirstName` only, never the key, and assert the `FirstName` sequence.
    On 3.9.0 these fail at runtime (ambiguous column), not just on the SQL text.
  - Every #12 test **also** asserts the qualified SQL fragment.
- **Oracle assertion.** `AssertMatchesLinqToObjects(shape)` runs the shape against a **fresh**
  `Query<T>()` and against `provider.Query<T>().Where(marker).ToList().OrderBy(p => p.Id).AsQueryable()`.
  It compares by id, by value, or by exception type.
- **Oracle determinism rules.**
  - Order-dependent shapes have a total order on the **composite key tuple**.
  - Null keys and collation-vs-ordinal string order are never oracle-compared; they get explicit
    per-provider tests.
  - Remote LEFT-JOIN keys are seeded non-null.
  - **Oracle predicates reference only members seeded non-null.** SQL three-valued logic makes `!=` and `All`
    diverge from LINQ when NULLs are present (§8).
  - **No predicate or ordering after a subset `Select`** in oracle shapes. FunkyORM evaluates it against base
    columns, while LINQ sees the projected defaults (§8).
- **Fresh root per shape.** One `Query<T>()` root is never reused, except by the AC13-13 tests, which do it
  on purpose.
- **SQL capture.** For "nothing executed": obtain the `IQueryable`, clear `Log`'s builder, run the shape,
  then assert the builder is empty.
- **Test entities.**
  - Join entity: `PersonDetailEntity` (SQL Server/PG/SQLite) or `PersonWithEmployer` (MySQL); every joined
    table has an `id` column.
  - A **no-`id` entity** (a table keyed `<table>_id`) is added in Task 1 (§6) to the schema scripts and the
    local databases.
- **Cleanup** deletes run in a transaction. **Subset projections** don't populate `Id`.
- **SQLite** gets a shared temp-DB schema helper.
- **DB-free tests don't inherit a DB fixture.**
- **net48-linked tests** compile as C# 7.3, and refer to post-net48 `Queryable` members by name (reflection)
  or inside `#if NET6_0_OR_GREATER`.

### 4.2 AC → test matrix

**Test classes:**
- Per provider (prefixed in the siblings): `OrderByQualificationTests`, `QueryOperatorSemanticsTests`,
  `QueryOperatorRejectionTests`, and `OrderByVisitorDirectTests` (DB-free).
- `QueryOperatorPolicyTests` (Core, DB-free, MSTest) lives in `SqlServer.Tests` and is linked into
  `Funcular.Data.Orm.SqlServer.Tests.NetFramework` (net48, MSTest).
- `SqlServer.Tests.DotNet9` (net9, xUnit) gets `QueryOperatorPolicyLiteralSetTests`, an xUnit twin asserting
  the same literal set and running the same sweep. The literal set lives in one shared source file of
  signatures, linked into all three projects, so the twins can't drift.

| AC | Test(s) | Project(s) |
|---|---|---|
| AC12-1 | `[DataTestMethod] OwnColumnOrdering_OnJoinEntity_QualifiedSql_ExecutesInOrder` — {OrderBy, OrderByDesc, ThenBy-after-remote, ThenByDesc-after-remote} × {full, subset-without-key, scalar-of-FirstName} × {paged, unpaged} (24 rows) | all 4 |
| AC12-2 | `RemoteMemberOrderBy_EmitsExactResolvedFragment_NoBasePrefix`, `ComputedMemberOrderBy_EmitsExpression_Unchanged` | all 4 |
| AC12-3 | `SingleTableEntity_OrderBy_SqlByteIdenticalTo390` | all 4 |
| AC12-4 | `TernaryOrderBy_OwnColumns_OnJoinEntity_QualifiedInsideCase` | all 4 |
| AC12-5 | `Last_OnJoinEntity_ProjectionWithoutKey_SynthesizedOrderQualified` (asserts `{table}.id DESC` and the returned `FirstName`) | all 4 |
| AC12-6 | `Distinct_Projection_JoinEntity_OrderByKeyInProjection_Executes`, `Distinct_Projection_JoinEntity_OrderByKeyNotInProjection_ThrowsExisting` | all 4 |
| AC12-7 | `DefaultPaging_OnJoinEntity_Executes`, `DefaultPaging_OnJoinEntity_SubsetProjection_Executes` | SQLite (regression rows in the other 3) |
| AC12-8 | `[DataTestMethod] TernaryOrderBy_NullComparison_MatchesOracle` over {`x.M == null`, `null == x.M`, `x.M != null`, `null != x.M`} | all 4 |
| AC12-9 | `ThenBy_SameKeyTwice_Executes` (SQL-text asserts one occurrence) | all 4 |
| AC13-1 | `Single_Predicate_ReturnsTargetNotFirst`, `SingleOrDefault_Predicate_NoMatch_ReturnsNull`, `Single_NoMatch_Throws`, `Single_TwoMatches_Throws`, `SingleOrDefault_TwoMatches_Throws`, `Single_NoUserOrder_EmitsRowLimit_NoIdOrder` (SQL shape), `Single_OnEntityWithoutIdColumn_Works`, `Single_AfterDistinctProjection_Works`, `Single_AfterTake1_OverManyRows_ReturnsRow`, `Single_AfterSkipOnly_OverManyRows_Throws` (also asserts the cap in SQL: `FETCH NEXT 2 ROWS` / `LIMIT 2 OFFSET n`), `Single_AfterSkipTake_Parameterless_MatchesOracle` | all 4 |
| AC13-2 | `Last_Parameterless_Unordered_ReturnsMaxId`, `Last_AfterOrderByNonIdKey_ReturnsLastInOrder`, `Last_AfterOrderByThenByDescending_InvertsEveryTerm`, `Last_AfterRemoteOrderBy_ReturnsLastInOrder`, `Last_AfterTernaryOrderBy_InvertsCaseTerm`, `LastOrDefault_Predicate_WithExplicitOrderBy_MatchesOracle`, `Last_Empty_Throws`, `LastOrDefault_Empty_ReturnsNull`, `Last_EntityWithoutIdProperty_ThrowsExistingInvalidOperation`, `Last_AfterDistinctProjection_NoOrder_ThrowsNamingLast`, `Last_AfterDistinctProjection_WithProjectedOrder_Works`; existing PG `LastOrDefault(x => …guid…)` stays green | all 4 |
| AC13-3 | `LongCount_EqualsCount_ReturnsInt64`, `LongCount_Predicate_EqualsCountPredicate`, `LongCount_FilteredByReverseRemoteKey_ThrowsNotSupported`; SQL Server only: `LongCount_EmitsCountBig` | all 4 |
| AC13-4 | `[DataTestMethod] Rejected_Operator_ThrowsNotSupported_NamesOperator_NoQueryExecuted` (one row per AC13-4 shape, including `ScalarProjection_ParameterlessSum`, `CovariantCastObject_ThenWhere` and `Covariant_Take`), `Allowed_PredicateWithCollectionContains_NotRejected`, `NonQueryableSpineMethod_Rejected`, `NonCallNonRootSpineNode_Rejected` (DB-free, hand-built `Convert` node); `[DataTestMethod] Covariant_ParameterlessTerminal_NotRejected_MatchesOracle` over {Count, LongCount, Any, First, FirstOrDefault, Single (source pre-filtered to one row), Last} on `IQueryable<object>` | all 4 + Core |
| AC13-5 | `[DataTestMethod] Allowed_Operator_MatchesOracle` (one row per allowed family, with its expected outcome); the four existing suites | all 4 |
| AC13-6 | `OfType_Identity_AtRoot_IsNoOp`, `OfType_Identity_AfterOrderBy_IsNoOp`, `OfType_Identity_AfterScalarProjection_NonNullable_IsNoOp`, `OfType_Identity_OverNullableScalar_Rejected` (seeded nulls), `OfType_NonIdentity_Throws`, `Cast_NonIdentity_UnrelatedType_Throws`; DB-free `Cast_IdentityNode_HandBuilt_IsAllowed` | all 4 + Core |
| AC13-7 | `SupportedOperators_ExactLiteralSetPinned`, `ClassifierSweep_EveryQueryableMethod_MatchesLiteralSet`, `NonQueryableOverload_IsRejected` (MSTest: `SqlServer.Tests` net8, `SqlServer.Tests.NetFramework` net48); xUnit twin `QueryOperatorPolicyLiteralSetTests` (`SqlServer.Tests.DotNet9` net9) | Core, 3 runtimes |
| AC13-8 | `GroupBy_Rejected_KeepsDedicatedMessage`; existing scalar tests; new in the siblings: `ScalarProjection_WithReducingTerminals_ThrowNotSupported`; `ScalarProjection_WithSingleOrLast_ThrowsNotSupported`; `Rejected_OperatorOuterToFailingInnerOperator_PolicyMessageWins` (`Select(p => p.Id).Where(x => x > 0).Reverse()`) | all 4 |
| AC13-9 | `OperatorDocTable_MatchesSupportedOperators` (reads the table from both docs); prose reviewed in the gauntlet | SqlServer.Tests |
| AC13-10 | `[DataTestMethod] Operator_AfterPaging_Rejected_BeforeAnyQuery` over {Count, LongCount, Any, All, Sum, Average, Min, Max, Where, First(pred), Single(pred), Last, OrderBy, OrderByDescending, Distinct, Skip-after-Skip, Take-after-Take, Take-then-Skip} — each row asserts the "after Skip/Take" message; `[DataTestMethod] Operator_AfterPaging_Allowed_MatchesOracle` over {Skip.Take, Select subset, Select scalar, First(), FirstOrDefault(), Single(), SingleOrDefault(), OfType-identity entity, `Skip(n).Select(p => p.Id).OfType<int>()`, Skip-only.First()}; `[DataTestMethod] TakeNonPositive_ReturnsEmpty_NoQuery` over {Take(0) full, Take(0) subset, Take(0) scalar, Skip(2).Take(0), Take(-1)}; `Take0_First_Throws_NoQuery`, `Take0_FirstOrDefault_ReturnsNull_NoQuery`, `Take0_Single_Throws_NoQuery`, `Take0_SingleOrDefault_ReturnsNull_NoQuery`; `ScalarProjection_Take0_First_ThrowsScalarGuard_NoQuery` (the scalar result-type guard wins over the empty short-circuit); `SkipNegative_BehavesAsSkipZero` | all 4 |
| AC13-12 | `[DataTestMethod] Ordering_AfterEarlierOrdering_Rejected_BeforeAnyQuery` over {OrderBy.OrderBy, OrderBy.ThenBy.OrderByDescending, OrderBy.Where.OrderBy, OrderBy.Select(**subset**).OrderBy, OrderBy.Distinct.OrderBy, OrderBy.Where.(cast)ThenBy, OrderBy.Select(subset).(cast)ThenByDescending} — every row asserts the D10 message; `ThenBy_WithNoEarlierOrdering_IsPrimaryOrder` (`((IOrderedQueryable<T>)q.Where(w)).ThenBy(k)` matches the oracle for `OrderBy(k)`) | all 4 |
| AC13-13 | `SqliteRoot_ReusedAfterProjection_BareRootNotNarrowed`, `SqliteRoot_ReusedAfterOrderedQuery_BareRootNoInheritedOrder`, `SqliteRoot_ReusedAfterOrderedQuery_ThenLast_UsesIdDesc`, `SqliteRoot_ReusedAfterParameterizedProjection_NoDuplicateParameters` | SQLite |
| AC13-14 | `SkipOnly_ToList_Executes`, `SkipOnly_First_ReturnsExpectedRow` (SQLite red; regression rows in the others); SQLite only: `SkipOnly_EmitsLimitMinusOneOffset` (SQL shape) | all 4 |

### 4.3 Interface coverage (new/changed members → tests)

| Member | Tests that call it on purpose |
|---|---|
| Core `QueryOperatorPolicy.EnsureSupported(Expression expression)` *(new, public, `void`)* | `QueryOperatorPolicyTests.*`; every `Rejected_*`/`Allowed_*`/`Operator_AfterPaging_*`/`OrderBy_AfterEarlierOrdering_*` row |
| Core `QueryOperatorPolicy.IsAllowed(MethodInfo)` *(new, **public** — no `InternalsVisibleTo` dependency, which would break if the assemblies are strong-named later)* | `ClassifierSweep_*`, `NonQueryableOverload_IsRejected`, the net9 xUnit twin |
| Core `QueryOperatorPolicy.SupportedOperators` *(new, public read-only)* | `SupportedOperators_ExactLiteralSetPinned`, `OperatorDocTable_MatchesSupportedOperators` |
| `*OrderByClauseVisitor` ctor — optional `tableQualifier` | AC12 tests; `OrderByVisitorDirectTests` |
| `*OrderByClauseVisitor.OrderByTerms` *(new)* + duplicate removal | AC13-2 inversion tests; `ThenBy_SameKeyTwice_Executes`; direct tests |
| `*OrderByClauseVisitor` ternary null handling | AC12-8; direct tests |
| `QueryComponents.Terminal`, `.RowLimit`, `.OrderByTerms`, `.IsEmptyByTake` *(new)* | AC13-1, AC13-2, AC13-10 |
| `*LinqQueryProvider.ParseExpression` (pre-pass call; Single/Last/LongCount branches; negative `Skip` clamp; empty-`Take` flag) | AC13-* |
| `*LinqQueryProvider.Execute` / `ExecuteScalarProjection` (empty-`Take` short-circuit) | AC13-10 `Take0_*`, `TakeNonPositive_*` |
| `*LinqQueryProvider.BuildQueryComponents` (row limit; SQLite `rowid` qualification; SQLite `LIMIT -1 OFFSET`) | AC13-1, AC12-7, AC13-14 |
| `*LinqQueryProvider.ExecuteQuery` (cardinality) | AC13-1 |
| `*LinqQueryProvider.BuildAggregateClause` / `HandleAggregateQuery` (`LongCount` only) | AC13-3 |
| `SqliteLinqQueryProvider` state reset | AC13-13 |

### 4.4 Mutations each key test must kill (run them; record the result in the handoff)

| Mutation | Test that must fail |
|---|---|
| Revert `ResolveOrderColumn` to bare `GetColumnName` | AC12-1 subset-without-key and scalar rows (runtime ambiguous column) |
| Apply the qualifier in the map-hit branch too | `RemoteMemberOrderBy_EmitsExactResolvedFragment_NoBasePrefix` |
| Qualify unconditionally | `SingleTableEntity_OrderBy_SqlByteIdenticalTo390` |
| Don't pass the qualifier at the `Last*` site | `Last_OnJoinEntity_ProjectionWithoutKey_SynthesizedOrderQualified` (runtime + SQL text) |
| SQLite default order back to bare `rowid` | `DefaultPaging_OnJoinEntity_Executes` |
| SQLite `OFFSET` without `LIMIT` | `SkipOnly_ToList_Executes` (SQLite) |
| Ternary null test back to `= NULL` / only one operand order handled | `TernaryOrderBy_NullComparison_MatchesOracle` rows |
| No duplicate-key removal | `ThenBy_SameKeyTwice_Executes` (SQL Server; *red expected — error 169 to be confirmed in Task 1*) |
| `Single*`: drop predicate→WHERE | `Single_Predicate_ReturnsTargetNotFirst` |
| `Single*`: limit 1 instead of 2 | `Single_TwoMatches_Throws`, `SingleOrDefault_TwoMatches_Throws` |
| `Single*`: ignore a user `Take(1)` | `Single_AfterTake1_OverManyRows_ReturnsRow` (`Take ?? 2` and `min(Take, 2)` are equivalent for `Take ≥ 2`; noted) |
| `Single*`: no cap after `Skip`-only | `Single_AfterSkipOnly_OverManyRows_Throws` via its SQL-shape assert (`FETCH NEXT 2 ROWS` / `LIMIT 2 OFFSET n`). The throw alone can't kill it. |
| `Single*`: route through the paging path (injects `ORDER BY id`) | `Single_NoUserOrder_EmitsRowLimit_NoIdOrder`, `Single_OnEntityWithoutIdColumn_Works`, `Single_AfterDistinctProjection_Works` |
| `SingleOrDefault` throws on empty | `SingleOrDefault_Predicate_NoMatch_ReturnsNull` |
| `Last*`: don't invert / ignore explicit order | `Last_AfterOrderByNonIdKey_ReturnsLastInOrder` |
| `Last*`: invert only the first term | `Last_AfterOrderByThenByDescending_InvertsEveryTerm` |
| `Last*`: invert own-column terms only | `Last_AfterTernaryOrderBy_InvertsCaseTerm`, `Last_AfterRemoteOrderBy_ReturnsLastInOrder` |
| `LongCount` missing from the `OuterMethodCall` list (returns `0L`) | `LongCount_EqualsCount_ReturnsInt64` |
| `LongCount` boxed as `Int32` / `COUNT(*)` on SQL Server | `LongCount_EqualsCount_ReturnsInt64` / `LongCount_EmitsCountBig` |
| Add `Reverse` to the allow-list | `Rejected_…[Reverse]`, `SupportedOperators_ExactLiteralSetPinned` |
| Match by name instead of overload | `Rejected_…[IndexedWhere]`, `[OrderByWithComparer]`, `[DistinctWithComparer]`, `[TakeRange]` |
| `GetGenericMethodDefinition()` without `IsGenericMethod` | `NonQueryableOverload_IsRejected`, `Rejected_…[ScalarProjection_ParameterlessSum]` |
| Skip the `TSource` = source-element check | `Rejected_…[CovariantCastObject_ThenWhere]`, `[Covariant_Take]` (`InvalidCastException` instead of the policy message) |
| Apply the `TSource` check to parameterless terminals too | `Covariant_ParameterlessTerminal_NotRejected_MatchesOracle` rows |
| Treat any non-call node as the root | `NonCallNonRootSpineNode_Rejected` |
| Policy visits the whole tree | `Allowed_PredicateWithCollectionContains_NotRejected` |
| Allow non-`Queryable` spine methods | `NonQueryableSpineMethod_Rejected` |
| Classifier allow-by-default | `ClassifierSweep_EveryQueryableMethod_MatchesLiteralSet` |
| Per-TFM computed expectation instead of the literal set | `SupportedOperators_ExactLiteralSetPinned` (literal count/signatures) |
| Policy inside the loop instead of a pre-pass | `Rejected_OperatorOuterToFailingInnerOperator_PolicyMessageWins` |
| Drop the `GroupBy` message | `GroupBy_Rejected_KeepsDedicatedMessage` |
| Identity check against the entity type instead of the source element type | `OfType_Identity_AfterScalarProjection_NonNullable_IsNoOp` |
| Identity `OfType` allowed over a nullable scalar | `OfType_Identity_OverNullableScalar_Rejected` |
| Reject all `Cast`/`OfType` | `OfType_Identity_AtRoot_IsNoOp`, `Cast_IdentityNode_HandBuilt_IsAllowed` |
| Allow any `Cast`/`OfType` | `OfType_NonIdentity_Throws`, `Cast_NonIdentity_UnrelatedType_Throws` |
| Drop the positional guard | `Operator_AfterPaging_Rejected_BeforeAnyQuery` rows |
| Positional guard rejects `Skip.Take` or `Skip`-only `First` | `Operator_AfterPaging_Allowed_MatchesOracle` rows |
| `Take(0)` sent to the DB | `TakeNonPositive_ReturnsEmpty_NoQuery` rows |
| Empty-`Take` short-circuit only on the entity path | `TakeNonPositive_…[Take(0) scalar]` |
| Empty-`Take` short-circuit placed before the scalar dispatch (bypasses the result-type guard) | `ScalarProjection_Take0_First_ThrowsScalarGuard_NoQuery` |
| No negative-`Skip` clamp | `SkipNegative_BehavesAsSkipZero` |
| Allow a second `OrderBy` / only check adjacent calls | `Ordering_AfterEarlierOrdering_Rejected_BeforeAnyQuery` rows (incl. across `Where`/`Select`/`Distinct`) |
| Check only `OrderBy*`, not a separated `ThenBy*` | `Ordering_…[OrderBy.Where.(cast)ThenBy]`, `[OrderBy.Select(subset).(cast)ThenByDescending]` |
| Reject every `ThenBy*` whose source isn't an ordering call (even with no earlier ordering) | `ThenBy_WithNoEarlierOrdering_IsPrimaryOrder` |
| SQLite reset placed after the early return | `SqliteRoot_ReusedAfterProjection_BareRootNotNarrowed`, `SqliteRoot_ReusedAfterOrderedQuery_BareRootNoInheritedOrder` |
| SQLite reset omits `OrderByTerms` | `SqliteRoot_ReusedAfterOrderedQuery_ThenLast_UsesIdDesc` |

### 4.5 Coverage (coverlet already referenced in all four test projects)

Baseline measured 2026-09-30 (cobertura `line-rate`):

| Touched file | SQL Server | SQLite | MySQL | PostgreSQL |
|---|---|---|---|---|
| `*LinqQueryProvider.cs` | 87.4% | 89.6% | **82.0%** | 88.6% |
| `*OrderByClauseVisitor.cs` | **58.2%** | **59.1%** | **35.6%** | **35.6%** |
| `QueryComponents.cs` / `*QueryComponents.cs` | 100% / 84.2% | 100% / 87.5% | 100% / 100% | 100% / 100% |
| Core `QueryOperatorPolicy.cs` | new | | | |

All touched files must reach **≥ 85%**. Visitor branches that can't be reached through `ParseExpression` are
covered by the DB-free direct visitor tests. Per-file numbers go into the PR.

### 4.6 Opt-in suites and frameworks

- **SQL Server:** `FUNKY_CONNECTION`. **MySQL:** `FUNKY_MYSQL_CONNECTION`. Both are set locally.
- **SQLite:** file-backed.
- **PostgreSQL:** the fallback connection reaches the native PostgreSQL 18 service. CI runs `postgres:17`
  only on push to `development/**`.
- **net48:** `QueryOperatorPolicyTests` (linked, MSTest) and the existing suite run locally in
  `Funcular.Data.Orm.SqlServer.Tests.NetFramework`, the net48 project in `FunkyORM.sln`.
  `Funcular.Data.Orm.NetFramework.Tests` is a separate, older project and isn't in the gate.
- **net9:** `SqlServer.Tests.DotNet9` is an xUnit project (global `using Xunit`, nullable on) containing only
  `ContainsTests`. It gets the xUnit twin `QueryOperatorPolicyLiteralSetTests`. The existing MSTest suite
  isn't run there.
- The CI net48 job is commented out (ci.yml:89-93), so these local runs are the gate; results go in the PR.

---

## 5. Design

### 5.1 #12 — qualify own columns in ORDER BY when joins exist

1. **Visitor.** Each provider's order-by visitor gains an optional `string tableQualifier = null`.
   `ResolveOrderColumn` resolves:
   - map hit → resolved fragment, unchanged and never prefixed;
   - otherwise, if `tableQualifier != null` → `{tableQualifier}.{column}`;
   - otherwise → the bare column.
2. **Call sites.** Both sites resolve `ResolveRemoteJoins<T>(table)` once and pass the map, plus
   `tableQualifier = IndividualJoinClauses?.Count > 0 ? table : null`.
3. **SQLite default order.** SQLite's default `ORDER BY rowid` becomes `ORDER BY {table}.rowid` when the
   command has joins.
4. **D11 ternary.** A comparison between a member and `null`, in either operand order, emits `{col} IS NULL` /
   `IS NOT NULL`.
5. **Duplicate keys.** A later term whose fragment equals an earlier term's is dropped (AC12-9).

### 5.2 #13 — Core pre-pass, then correct `Single*`/`Last*`/`LongCount`

1. **Core `QueryOperatorPolicy`** (`Funcular.Data.Orm.Core/Linq/QueryOperatorPolicy.cs`).
   - **Spine walk.** Start at the outermost node and follow `MethodCallExpression.Arguments[0]` to the root.
     - **Terminal:** a `ConstantExpression` whose value is an `IQueryable`, i.e. the provider's own root.
     - Any other non-call node → reject.
     - Each node is classified **before** its `Arguments[0]` is read.
     - Lambdas and other arguments are never visited.
   - **Element type** of a node = the `T` in the `IEnumerable<T>` interface of its `Type`. That works for
     `SqlQueryable<T>`, `IQueryable<T>` and `IOrderedQueryable<T>` alike.
   - **Classification.** `IsAllowed(MethodInfo m)` uses the key
     `m.IsGenericMethod ? m.GetGenericMethodDefinition() : m`. It rejects non-`Queryable` declaring types, and
     keys outside a static allowed set built from `typeof(Queryable).GetMethods()` by name plus shape:
     - one `Expression<Func<TSource, …>>` lambda;
     - `Skip`/`Take` take `int`;
     - no comparer, default-value or `Range` parameters.

     Non-generic overloads are never in the set.
   - **`TSource` check.** For an allowed generic method, the first generic argument must equal its source's
     element type, **except** for parameterless terminals (`Count`, `LongCount`, `Any`, `First*`, `Single*`,
     `Last*` with no predicate). Those work over a covariant source (§1.4), so they're exempt.
     Lambda-bearing and sequence operators with a mismatched `TSource` are rejected with the policy message.
   - `IsAllowed` is **public**.
   - **Allowed operators:**
     - `Where`, `Select`;
     - `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`;
     - `Skip`, `Take`, `Distinct`;
     - `First*`, `Single*`, `Last*` (with and without predicate);
     - `Any`, `All`, `Count`, `LongCount`;
     - `Sum`, `Average`, `Min`, `Max` (generic, with selector; behavior unchanged until 3.10.1);
     - identity `Cast`/`OfType` per D5.
   - **Positional rules**, applied inner→outer:
     - D8, after the first `Skip`/`Take`;
     - D10: an `OrderBy`/`OrderByDescending` after any earlier ordering call is rejected.
     - D10: so is a `ThenBy*` whose immediate source isn't an ordering call when an earlier ordering exists.
       A `ThenBy*` with no earlier ordering is allowed and becomes the primary ordering.
   - **Messages.**
     - `GroupBy` keeps its dedicated message.
     - Other rejected operators: `"{Op}(...) is not translated to SQL in this version. Materialize first and
       apply it in memory: query.ToList().{Op}(...)."`
     - Positional (D8): `"{Op}(...) after Skip/Take is not translated …"`.
     - D10: `"A second OrderBy is not translated; use ThenBy, or put the primary key first."`
     - Nullable `OfType`: the message points to `Where(x => x.M != null)` before the projection.
   - `EnsureSupported` returns `void`. It only throws.
2. **Wiring.**
   - Each provider's `ParseExpression` calls `QueryOperatorPolicy.EnsureSupported(expression)` first. For
     SQLite, that's after the state reset (D11) and before the early return.
   - Remove the now-dead inline `GroupBy` check and the parameterless-aggregate-after-scalar guard
     (SqlLinqQueryProvider.cs:339-346 and the sibling copies).
   - Fix the stale comment at :128-129 and the sibling copies.
   - The scalar composition and result-type guards stay (precedence per AC13-8).
3. **`Single*` (row limit).**
   - The First/Last predicate branch generalizes to `First*`/`Single*`/`Last*` × {with, without predicate}.
     It routes the predicate to WHERE and sets `components.Terminal`.
   - **No user `Skip`/`Take`:** `RowLimit = 2`. SQL Server emits `SELECT TOP (2)` / `SELECT DISTINCT TOP (2)`;
     the others emit `LIMIT 2` after any ORDER BY. No ORDER BY is synthesized.
   - **User `Skip` or `Take`:** `Take = min(Take ?? 2, 2)` on the existing paging path.
   - It reads up to two rows through the list path, then applies LINQ cardinality.
4. **`Last*`.**
   - The visitor exposes `OrderByTerms` (`(fragment, isDescending)`, after duplicate removal).
   - `Last*` inverts every term. With no terms it uses a qualified `Id DESC`.
   - It uses `RowLimit = 1` with that ORDER BY. `Last` throws on empty; `LastOrDefault` returns `null`.
   - With `Distinct` + custom projection and no explicit order, it throws, naming `Last`.
   - This replaces the current `LastOrDefault(pred)` block. D10 (including the rev 4 `ThenBy` rule)
     guarantees the ordering terms form one contiguous chain, so the inversion is well defined.
5. **`LongCount`.** Added to the `OuterMethodCall` list and `BuildAggregateClause`, and handled like `Count`
   (including the reverse-key rejection). SQL Server emits `COUNT_BIG(*)`. The result converts to `Int64`.
6. **`Skip`/`Take` values.**
   - `Skip(n < 0)` is stored as 0.
   - `Take(n ≤ 0)` sets `components.IsEmptyByTake`.
   - Two checks, in this order of precedence:
     - `Execute` checks the flag **after** the scalar dispatch (SqlLinqQueryProvider.cs:94-97), so it only
       handles the entity path (`X = T`).
     - `ExecuteScalarProjection` checks it **after** its result-type guard (:130-139), with `X` = the scalar
       member type. `Select(p => p.Id).Take(0).First()` therefore throws the scalar guard's
       `NotSupportedException`, not "no elements".
   - Outcomes:
     - collection `TResult` → an empty `List<X>`;
     - `First`/`Single` → throw "no elements";
     - `*OrDefault` → `default`.

     No command is built.
7. **SQLite `Skip` without `Take`** emits `LIMIT -1 OFFSET n` (AC13-14).
8. **D11 SQLite reset.** Clear `_lastSelectProjection`, `_lastSelectParameters`, `_lastOrderByClause` and the
   new `OrderByTerms` field at the top of every `ParseExpression`, before the early return.

---

## 6. Tasks (v3.10.0)

Each task lists the tests it turns green. Every implementation task starts with them present and **red**.
**AC13-5 (regression) is a gate on every task:** all four existing suites stay green after each one.

- **Task 0 — Test-plan review gate.**
  - ✅ Branch; D1–D12 decided.
  - ✅ Premises re-verified.
  - ✅ Baselines recorded.
  - ✅ Issues retitled, evidence posted.
  - ✅ Review r1: 25 findings (§9.1). Fix-verification r2: 21 findings (§9.2). Fix-verification r3: 10
    findings (§9.3).
  - ✅ Owner answered the §10 questions (3.10.1).
  - ⏳ Fix-verification r4 of this revision must be clean.
  - Then post the §3 ACs to #12/#13 and start Task 1.
- **Task 1 — Stubs, schema, harness, red tests.**
  - Compile-only stubs so the red run fails at runtime: policy members throw `NotImplementedException`; plus
    the visitor `tableQualifier`, `OrderByTerms`, and the `QueryComponents` members.
  - **Schema:** add the no-`id` entity table to:
    - `Database/integration_test_db.sql`
    - `Database/MySql/integration_test_db.sql`
    - `Database/PostgreSql/integration_test_db.sql`
    - `Database/Sqlite/integration_test_schema.sql`
    - the SQLite schema helper

    Apply it to the three local servers with an idempotent `CREATE TABLE IF NOT EXISTS`, or the SQL Server
    equivalent.
  - Harness, SQLite helper, and the net48/net9 links.
  - Write every §4.2 test and record red (or expected-green for regression rows).
  - Confirm the UNVERIFIED items:
    - SQL Server error 169 on a repeated ORDER BY column;
    - SQLite rejecting `OFFSET` without `LIMIT`;
    - `Queryable.Cast` short-circuit on net48;
    - PG/MySQL/SQLite binding an unqualified `ORDER BY id` to a projected `Id` alias.
- **Task 2 — #12 qualifier + duplicate removal** (4 providers).
  → AC12-1…AC12-4, AC12-6, AC12-9.
- **Task 3 — SQLite `rowid` qualification + `LIMIT -1 OFFSET`.**
  → AC12-7, AC13-14.
- **Task 4 — Core policy + positional (D8, D10) + wiring + dead-guard removal** (4 providers).
  → AC13-4, AC13-6, AC13-7, AC13-8, AC13-10 (rejection rows), AC13-12.
- **Task 5 — `Single*` row limit, `Skip`/`Take` values, empty-`Take` short-circuit** (4 providers).
  → AC13-1, AC13-10 (allowed and empty rows).
- **Task 6 — Visitor `OrderByTerms` + ternary null** (4 providers).
  → AC12-8.
- **Task 7 — `Last*`** (4 providers).
  → AC13-2, AC12-5.
- **Task 8 — `LongCount`** (4 providers).
  → AC13-3.
- **Task 9 — SQLite state reset; coverage lift.**
  - State reset → AC13-13.
  - Direct visitor tests.
  - MySQL provider to ≥85%.
  → §4.5.
- **Task 10 — Docs.**
  - Operator table + doc test.
  - Changelog 3.10.0: **Fixed**, **Changed** (untranslated operators, operators after paging, and a second
    `OrderBy` now throw), and **Known issues** (aggregates, fixed in 3.10.1).
  - README "upgrade strongly recommended".
  - Usage.md: `LongCount`.
  → AC13-9.
- **Task 11 — Gauntlet and release.**
  - Mutation runs recorded; net48/net9 runs.
  - A fresh non-author adversarial pass, then fix-verification.
  - Sentinel file for the exact clean sha.
  - Push `development/3.10` (CI + MySQL + PostgreSQL) → PR → merge → `3.10.0-beta1`.
  - Sentinel smoke test → `3.10.0`.
  - Close #12. Keep #13 open for the 3.10.1 aggregate work, or split it (owner's call).
  - Security Advisory decision (D7).

**Rough effort (3.10.0):**

| Work | Estimate |
|---|---|
| Task 1 (stubs, schema, ~120 rows × 4) | ~1.5 days |
| #12 + SQLite paging (Tasks 2–3) | ~0.75 day |
| Policy + `Single`/`Last`/`LongCount` (Tasks 4–8) | ~2.25 days |
| Coverage + docs + gauntlet (Tasks 9–11) | ~1.5 days |
| **Total** | **~6 working days** |

---

## 7. Risks

- **Callers relying on silently ignored or mistranslated operators.** That code now throws or returns a
  different value. **Mitigation:** minor version, prominent Changelog lists, and messages that say what to
  write instead.
- **Over-rejection.** **Mitigation:** the allowed set mirrors what the loop translates; oracle rows per family;
  existing suites as the gate. No FunkyORM test or Sentinel query composes after paging or uses a second
  `OrderBy`.
- **Mirror drift.** **Mitigation:** policy and positional rules live in Core; per-provider changes are proved
  by identical test names.
- **Row-limit syntax.** `DISTINCT TOP (n)` on SQL Server; `LIMIT` after ORDER BY elsewhere. **Mitigation:**
  covered by tests.
- **net48.** The allowed set is identical across frameworks: the overloads that differ are all excluded
  anyway. **Mitigation:** one literal set, swept on every TFM.

## 8. Out of scope (recorded, not fixed here)

- **Aggregates (D9) → 3.10.1 (§10).** Documented as known issues in the 3.10.0 changelog:
  - `Average` of whole numbers truncates on SQL Server (to be confirmed) and loses precision on MySQL and
    SQLite;
  - `decimal`/`float` `Average` throws;
  - nullable `Min`/`Max`/`Average` on an empty set throws;
  - some `Min`/`Max` result types throw after the round-trip.
- `GenerateOrderByClause` (three providers) is dead code.
- SQLite provider state isn't safe for concurrent execution from one root. Sequential reuse is fixed by D11.
- Unordered default paging hard-codes `id`, which is wrong for entities whose key column isn't `id`.
  `Single*` without user `Skip`/`Take` no longer routes through it (row limit). With user paging it still
  does, like any paged query. A resolved-PK default order is a follow-up issue.
- **SQL NULL semantics differ from C# (pre-existing; follow-up issue).**
  - `x.M != v` emits `col <> @p`, which excludes NULL rows; C# includes them.
  - `All(pred)` is `NOT EXISTS(… WHERE NOT pred)`, which never counts a NULL row as a violation; C# does.

  EF Core compensates for this by default. FunkyORM doesn't, and the tutorial audience will expect it to.
  **Recommended for 3.11:** emit C#-style null semantics (`col <> @p OR col IS NULL`), behind an opt-out.
- **A predicate or ordering after a subset `Select` reads base columns (pre-existing; follow-up issue).** In
  `Select(p => new T { FirstName = p.FirstName }).Where(p => p.Id > 5)`, FunkyORM filters on `person.id`,
  while LINQ sees the projected default `Id = 0`. This only matters when the predicate reads a mapped member
  the projection leaves out. Candidate fix: reject lambda-bearing operators after a subset `Select` when the
  lambda reads an unprojected member.
- **Strong naming.** Shipping assemblies are still `SignAssembly=False`, and Core's `InternalsVisibleTo`
  entries carry no public keys. The new `IsAllowed` seam is public to avoid adding to that dependency.
  Whether 3.10.0 is the release that flips signing on is a separate owner decision. It would also require
  public-key-qualified `InternalsVisibleTo` entries for the existing test projects.
- Sentinel.MVP pins `3.9.0-beta1`; the upgrade is the D3 smoke test.

---

## 9. Review dispositions

Blame classes are evaluated in order; the first that applies wins.

### 9.1 Task 0 test-plan review r1 of `30d3a6d`

Totals: AC-GAP 10, TEST-GAP 11, HOUSE-RULE 0, PLAN-GAP 4, OTHER 0.

| # | Sev | Blame | Finding (short) | Disposition |
|---|---|---|---|---|
| 1 | BLOCKER | AC-GAP | Operators after `Skip`/`Take` mistranslated | D8, AC13-10, positional guard (Task 4). |
| 2 | MAJOR | AC-GAP | `Single(pred)` after `Skip` can't meet AC13-1 | Parameterless-only composition; predicate form rejected (D8). |
| 3 | MAJOR | AC-GAP | Second `OrderBy` reverses priority | D10 → reject (rev 3 redesign), AC13-12. |
| 4 | MAJOR | TEST-GAP | Unlogged schema discovery; "no SQL" unobservable | AC13-4 reworded; D2 corrected; killer replaced. |
| 5 | MAJOR | AC-GAP | `Single*` via paging injects `ORDER BY id` | Row-limit design; no-`id` entity test. |
| 6 | MAJOR | AC-GAP | `Single*`/`Last*` + `Distinct` projection | Row limit; `Last` rule in AC13-2. |
| 7 | MAJOR | AC-GAP | Aggregate overload bugs | D9 → 3.10.1 (§10); known issues documented. |
| 8 | MAJOR | AC-GAP | SQLite projection-state leak | D11 full reset, AC13-13. |
| 9 | MAJOR | TEST-GAP | `Cast` tests toothless; identity used the entity type | Source-element identity; hand-built node and unrelated-type tests. |
| 10 | MINOR | PLAN-GAP | Non-generic overloads | `IsGenericMethod` guard + tests. |
| 11 | MINOR | TEST-GAP | AC13-8 unprovable | Restated with precedence; distinctive-text asserts; sibling tests. |
| 12 | MINOR | TEST-GAP | `Single*`/`Last*` after scalar | Test added. |
| 13 | MINOR | TEST-GAP | Missing mutation rows | Added. |
| 14 | MINOR | TEST-GAP | Whole-tree walk ambiguity | Spine-only walk specified; tests. |
| 15 | MINOR | TEST-GAP | Oracle determinism | Composite-key rule; exclusions. |
| 16 | MINOR | TEST-GAP | Tautological sweep | `IsAllowed` seam vs literal set. |
| 17 | MINOR | AC-GAP | net48/net9 not gated | AC13-7 on three TFMs. |
| 18 | MINOR | PLAN-GAP | Red tests won't compile | Stubs in Task 1. |
| 19 | MINOR | PLAN-GAP | Unreachable visitor branches | DB-free direct tests. |
| 20 | MINOR | AC-GAP | Ternary `= NULL` | D11, AC12-8. |
| 21 | MINOR | TEST-GAP | No-`Id` `Last` untested | Test added. |
| 22 | MINOR | TEST-GAP | `LongCount` reverse-key / `COUNT_BIG` | Tests added. |
| 23 | MINOR | TEST-GAP | AC12-1 combinations; AC12-6 entity | 24-row test; join entity. |
| 24 | MINOR | AC-GAP | `Take(0)` + `Single*` | Empty-`Take` short-circuit. |
| 25 | MINOR | PLAN-GAP | Affected-version wording | Wording fixed; range verified. |

### 9.2 Task 0 fix-verification r2 of `e19a7ae`

Totals: AC-GAP 5, TEST-GAP 7, HOUSE-RULE 1, PLAN-GAP 8, OTHER 0. 19 of 21 were introduced by rev 2; only F11
and F19 weren't. Rev 3 originally said 16; corrected in rev 4 (N10a).

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| F1 | MAJOR | AC-GAP | yes | D10 stable-sort rule wrong; ordering dropped across `Where` | **Redesigned:** D10 = reject (owner). AC13-12 covers the across-`Where` shapes. |
| F2 | MAJOR | TEST-GAP | yes | D10 tests blind to tie-breakers | Moot after the redesign; rejection tests have no tie semantics. Composite-key oracle rule kept. |
| F3 | MINOR | PLAN-GAP | yes | NULL-result rule order (`Sum` → 0; `string` → null) | → 3.10.1 input (§10). |
| F4 | MAJOR | AC-GAP | yes | `ChangeType` fails for enum/Guid/DateTimeOffset/TimeSpan | → 3.10.1; owner direction recorded (§10). |
| F5 | MAJOR | TEST-GAP | yes | `Average` precision (SQLite `ROUND`, MySQL scale 4, decimal scales); {1,2} undiscriminating | → 3.10.1 input: SUM/COUNT design, {1,1,2} rows. |
| F6 | MAJOR | PLAN-GAP | yes | Aggregate columns missing from schemas; effort undercounted | → 3.10.1 (new test table). 3.10.0 Task 1 names the scripts for the no-`id` entity; effort re-estimated. |
| F7 | MAJOR | AC-GAP | yes | Identity `OfType` over nullable elements keeps nulls | D5 refined; `OfType_Identity_OverNullableScalar_Rejected`. |
| F8 | MINOR | PLAN-GAP | yes | Spine walk underspecified | §5.2.1: `IEnumerable<>` element type, root terminal, classify-first, `TSource` check; tests. |
| F9 | MINOR | TEST-GAP | yes | #12 tests pass by alias binding when the key is projected | Subset rows omit the key; SQL-text asserts; AC12-5 reworded. |
| F10 | MINOR | PLAN-GAP | yes | `Single*` after `Skip`-only; SQLite `OFFSET` without `LIMIT` | `min(Take ?? 2, 2)`; AC13-14 `LIMIT -1 OFFSET`. |
| F11 | MINOR | AC-GAP | no | Negative `Take`/`Skip` | D8: `Take(n ≤ 0)` empty; `Skip(n < 0)` → 0; tests. |
| F12 | MINOR | TEST-GAP | yes | `Take(0)` location ambiguous; scalar path | Flag + check in `Execute` and `ExecuteScalarProjection`; rows for subset, scalar, `Skip.Take(0)`, OrDefault, Single. |
| F13 | MINOR | TEST-GAP | yes | "ThenBy after paging" row unbuildable; missing allowed rows; D8/AC misaligned | Row → `OrderByDescending`; `OfType` + `Skip`-only `First` rows; aligned on `OfType`. |
| F14 | MINOR | TEST-GAP | yes | Inherited order leaks only on the bare root; reset list incomplete | Bare-root tests; `ThenLast_UsesIdDesc`; full reset; mutation row. |
| F15 | MINOR | TEST-GAP | yes | String `Min`/`Max` rows vs collation rule; float tolerance | → 3.10.1 (aggregate rows moved). |
| F16 | MINOR | PLAN-GAP | yes | net48 C# 7.3 and missing APIs | §4.1 rule (by-name reflection / `#if`). |
| F17 | MINOR | PLAN-GAP | yes | "Framework-aware" pinned set reintroduces the tautology | One literal set on every TFM; §7 fixed. |
| F18 | MINOR | HOUSE-RULE | yes | Dead scalar-aggregate guard and stale comment after the pre-pass | Removed and fixed in Task 4. Already covered by taxonomy pattern 16 (comment/contract drift), so no new house-test row. |
| F19 | MINOR | PLAN-GAP | no | Repeated ORDER BY column (SQL Server 169) | Duplicate removal, AC12-9. |
| F20 | MINOR | PLAN-GAP | yes | Consistency (Task 0 text, AC12-5 mapping, AC13-5 unmapped, AC13-9 executable, `void` pre-pass, §1.4 SQLite facts) | All corrected. AC13-5 is a gate on every task. |
| F21 | MINOR | AC-GAP | yes | `null == x.M` operand order | AC12-8 covers both orders. |

### 9.3 Task 0 fix-verification r3 of `e48f2b1`

Totals: AC-GAP 3, TEST-GAP 4, HOUSE-RULE 0, PLAN-GAP 3, OTHER 0. 8 of 10 were introduced by rev 3; only N3 and
N8 weren't. The r3 reviewer also confirmed that all §9.1 dispositions still hold, and marked F1–F21 as
16 RESOLVED / 5 PARTIAL. The PARTIALs are F8, F10, F12 and F13 (now N1, N4, N5, N10b) and the net9 leg of
§9.1 #17 (now N3).

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| N1 | MINOR | AC-GAP | yes | `TSource` check rejects covariant parameterless terminals that work in 3.9.0 | Exempted parameterless terminals (§5.2.1); `Covariant_ParameterlessTerminal_NotRejected_MatchesOracle`; `Covariant_Take` rejection row; two mutation rows. |
| N2 | MAJOR | AC-GAP | yes (claim) / pre-existing behavior | A `ThenBy*` cast past a `Where` drops the earlier ordering | D10 extended (same class); AC13-12 rows including the cast shapes; `ThenBy_WithNoEarlierOrdering_IsPrimaryOrder`; two mutation rows. |
| N3 | MINOR | PLAN-GAP | no | net9 project is xUnit; net48 project unnamed | xUnit twin plus a shared literal-set source file; net48 project named; §4.6 corrected. |
| N4 | MINOR | TEST-GAP | yes | AC13-14 misstated `Single`; the cap mutation survived | AC13-14 reworded; the cap is asserted in SQL; mutation row fixed; SQLite `SkipOnly_EmitsLimitMinusOneOffset`. |
| N5 | MINOR | TEST-GAP | yes | Empty-`Take` vs scalar guard order ambiguous | §5.2.6 pins the order; `ScalarProjection_Take0_First_ThrowsScalarGuard_NoQuery`; mutation row. |
| N6 | MINOR | TEST-GAP | yes | D10 `Select` row vacuous if scalar; precedence unpinned | Rows use a subset `Select`; D10 and "after Skip/Take" messages asserted per row. |
| N7 | MINOR | TEST-GAP | yes | Oracle rows break on NULL semantics | Oracle rule: non-null predicate members. Divergence recorded in §8 with a 3.11 recommendation. |
| N8 | MINOR | AC-GAP | no | Predicate after subset `Select` reads base columns | Oracle rule: none after a subset `Select`. Recorded in §8 as a follow-up with a candidate fix. |
| N9 | MINOR | PLAN-GAP | yes | §10 `time`-range claim wrong | Reworded per provider (§10). |
| N10 | MINOR | PLAN-GAP | yes | Bookkeeping: 16→19; `OfType` after paging; §8 `Single` wording; `Id DESC` qualification; §1.4 premise | All corrected. `Skip(n).Select(p => p.Id).OfType<int>()` row added. |

---

## 10. v3.10.1 — aggregate correctness (D9): owner direction and open questions

**Owner direction (2026-09-30):** go with the recommended approach, with the refinements below.

- **`Average`:** computed exactly as `SUM(wide) / COUNT` client-side (LINQ semantics); SQLite's `ROUND` is
  removed.
- **Empty and nullable results** follow LINQ:
  - `Sum` → 0, even for nullable selectors;
  - nullable or reference `Min`/`Max`/`Average` → `null`;
  - non-nullable → throws "no elements".
- **`Min`/`Max` result types:**
  - **Enums:** reject. The message suggests aggregating the underlying number
    (`Max(x => (int)x.Status)`); confirm that FunkyORM translates the cast.
  - **`TimeSpan`:** reject, with guidance. Developers should store durations as `bigint`/`int` (ticks,
    seconds or ms) and aggregate that property. The message explains that time types differ by provider:
    - SQL Server `time` and PostgreSQL `time` hold values under 24 hours;
    - MySQL `TIME` spans ±838 hours;
    - Npgsql maps `TimeSpan` to PostgreSQL `interval`, which is unbounded.
  - **`Guid`:** reject by default, with a message explaining that the database's ordering differs from
    .NET's. Developers can **opt in** to server-side ordering (mechanism open — below).
  - **`DateTimeOffset`:**
    - SQL Server `datetimeoffset` and PostgreSQL `timestamptz` both order by UTC instant, as .NET does, so
      allow them there. PostgreSQL returns a UTC `DateTime` that gets converted.
    - MySQL has no offset type, and SQLite stores text that sorts wrongly across offsets. Reject on both,
      with a message suggesting UTC `DateTime` storage. *To verify.*

**Guid ordering by provider** (from documentation and reasoning; verify in the 3.10.1 Task 0):

| Provider | Storage | Order vs .NET `Guid.CompareTo` |
|---|---|---|
| SQL Server | `uniqueidentifier` | **Differs.** Compares byte groups 10–15, then 8–9, 6–7, 4–5, 0–3. |
| PostgreSQL | `uuid` | **Should match.** Both compare in canonical string order. |
| MySQL | `CHAR(36)` / `BINARY(16)` | Text matches if the case is consistent; binary depends on MySqlConnector's `GuidFormat`. |
| SQLite | `TEXT` / `BLOB` | Text matches if the case is consistent; a blob in .NET byte order differs. |

**Owner answers (2026-09-30):**
1. **Guid opt-in: a provider-wide option**, set at startup/DI, e.g.
   `new SqlServerOrmDataProvider(conn) { QueryOptions = { GuidOrdering = GuidOrdering.Database } }`.
   - **Provider-aware default:** PostgreSQL defaults to *allowed*; SQL Server, MySQL and SQLite default to
     *rejected*.
   - **Hard prerequisite:** the 3.10.1 Task 0 must verify that PostgreSQL `uuid` `MIN`/`MAX` matches .NET
     `Guid.CompareTo` on discriminating data. If it doesn't, the PostgreSQL default falls back to *rejected*,
     and the owner is told.
   - (b) a per-property attribute and (c) a per-query marker weren't chosen; they can be added later if
     targeted control is needed.
2. **The Guid guard does not cover `OrderBy`/`ThenBy`** on Guid columns. The ordering difference is
   documented instead; `OrderBy(Guid)` is usually just a stable tie-breaker.
3. **String `Min`/`Max` use the database's collation**, documented, since they can't match .NET's
   culture-sensitive comparison anyway. Numeric results target exact LINQ-to-objects semantics.

**Inputs from the r2 fix-verification (to fold into the 3.10.1 plan):**
- **F3:** NULL-result rule order.
- **F4:** type failures.
- **F5:** `Average` precision.
  - SQLite `ROUND(…, 10)`.
  - MySQL `AVG` scale 4.
  - Decimal scales on SQL Server / MySQL / PostgreSQL.
  - Key the widening on the column type, not the selector type.
  - Seed {1, 1, 2} rows for `int` and `decimal` on every provider.
- **F6:** a new aggregate test table in all five schema scripts, with `int`/`long`/`float`/`double`/`decimal`
  columns in nullable and non-null forms, plus `DateTime`, `string`, `Guid`, `DateTimeOffset`, `TimeSpan` and
  an enum.
- **F15:** collation-neutral strings and a floating-point tolerance.
