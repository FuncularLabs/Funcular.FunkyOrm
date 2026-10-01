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
> - Task 0 is waiting on a clean fix-verification of this revision (r6, scoped to the rev-6 diff).

> **Revision 6 (Task 0 fix-verification r5, 2026-09-30) — what changed:**
> The r5 reviewer found 8 issues at `dfeae2e`: 0 blocker, 2 major, 6 minor. All 8 were introduced by rev 5.
> Blame: AC-GAP 1, TEST-GAP 7. Disposition is in §9.5. Both majors rested on **System.Linq premises nobody had
> executed**:
> - **`Queryable.Cast` always creates a node**, even `Cast<T>()`. My §1.4 bullet confused it with
>   `Enumerable.Cast`.
> - **`ThenBy*` after any non-ordering call throws `ArgumentException` inside System.Linq** before the
>   provider sees it. That's r3 N2's premise, never run.
>
> **Process fix: every shape premise now cites an executed probe (new §1.5)**, run on the real providers on
> 2026-09-30. Changes:
> - **D10 clause 2 deleted (R5-2).** A separated `ThenBy` is unreachable, so its message, five rows and two
>   mutation rows are gone. D10 is just "second `OrderBy*` rejected". The sort-helper crash this exposed is
>   pre-existing and filed as [#16](https://github.com/FuncularLabs/Funcular.FunkyOrm/issues/16) (owner:
>   follow-up, not 3.10.0).
> - **D5 amended by the owner (R5-1).** A **reference-conversion `Cast<TBase>()`** behaves like an implicit
>   conversion. It's transparent, and the operators after it go through the covariance table using the
>   *effective* element type.
>   - `q.Cast<object>().Count()`/`.First()` keep working; they're correct in 3.9.0 (P5).
>   - Enumerating it now works too, through a generalized `isCollection` in `Execute`. In 3.9.0 it throws
>     `InvalidCastException` (P14).
>   - `Select(p => p.FirstName).Cast<object>().First()` is still rejected; in 3.9.0 it returns the whole list
>     (P6).
> - **The scalar covariance row is corrected against new probes.** Covariant `Skip`/`Take`/`Distinct` and
>   enumeration over a scalar projection return correct results (P7b–P7e), so they stay allowed. Only
>   terminals and lambda-bearing operators are rejected.
> - **Covariance rows** are spelled both ways: the no-node conversion `IQueryable<object> o = …` and
>   `Cast<object>()`.
> - **Second-layer scalar guard (R5-3)** can't be reached through `Queryable`. It becomes a Core helper,
>   `ScalarProjectionGuard`, tested directly.
> - **Rule order is pinned at each node (R5-5):** allow-list → D8 → D10 → covariance/D5. The pre-pass runs in
>   two passes so the root element type is known.
> - **Messages (R5-6):** a scalar-source covariant variant; `Cast<…>` dropped from the advice; an "other
>   projected type" source defers to the existing `Select`-shape guard.
> - **Wording (R5-4, R5-7):** AC13-5 restated (the rules reject some shapes that 3.9.0 happened to answer
>   correctly; the Changelog lists them), and AC12-3 exceptions widened.
> - **Formatting (R5-8):** §4.2 table repaired.
> - Signing deferred by the owner (§8).
> - The spine root must be the queryable's own root (`((IQueryable)c.Value).Expression == c`).

> **Revision 5 (Task 0 fix-verification r4, 2026-09-30) — what changed:**
> The r4 reviewer found 7 issues at `7e97e93`: 0 blocker, 1 major, 6 minor. 5 were introduced by rev 4. Blame:
> AC-GAP 2, TEST-GAP 3, PLAN-GAP 2. Disposition is in §9.4.
> - **Covariance rule redesigned (F1, major).** This was the second fix-introduced finding in a row on this
>   rule. Rev 4's exemption let `Select(p => p.FirstName).Cast<object>().First()` through, and it would return
>   the entire `List<string>` as the "first" element. Following the house rule, the rule is now an explicit
>   **cell table** (§5.2.1), and every cell has a test row. The exemption applies **only when the source
>   element type is still the entity type**.
>
>   A second layer is added too: `ExecuteScalarProjection` rejects any **terminal** call, not only a terminal
>   whose result type fails to match, so `TResult = object` can't slip past it.
> - **D8 refined (F6a).** `Skip(n).Select(…).Take(k)` returns correct rows in 3.9.0, because projection
>   commutes with `Take`. So `Take` may follow `Skip` when only `Select`/identity `OfType` sit between them.
>   D8's promise to reject nothing that works today holds again.
> - **Messages (F2).** A separated `ThenBy` gets its own D10 message ("move ThenBy before {Op}"). A covariant
>   mismatch gets its own message ("apply {Op} before converting the element type"). D8 is evaluated before
>   D10 at the same node (F3), and the rows pin both.
> - **Tests.**
>   - SQL-shape asserts normalize whitespace (F4). PostgreSQL and SQLite put `LIMIT`/`OFFSET` on separate
>     lines.
>   - AC12-1/AC12-5 state that ordering and paging come *before* the projection. The "no predicate or ordering
>     after a subset `Select`" rule now binds all shapes, not just oracle rows (F5).
>   - AC12-3 is narrowed to the ORDER BY translation (F6b).
> - **Bookkeeping (F7).** A stale test name in §4.3, the §5.2.4 `Id DESC` wording, the §9.3 arithmetic, and
>   task mapping for the covariant rows.

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
- **Every FunkyORM queryable implements `IOrderedQueryable<T>`** (SqlQueryable.cs:9 and the siblings). But
  `Queryable.ThenBy` checks the source **expression's** type: after any non-ordering call it is
  `IQueryable<T>`, so System.Linq throws `ArgumentException` before the provider sees it (§1.5 P3/P3b). Only
  the root (typed as the concrete queryable) and ordering calls can precede `ThenBy*` (P4). One consequence
  is pre-existing and out of scope: `is IOrderedQueryable` sort helpers crash on composed queries (P4b; issue
  #16).
- **Covariance.** `IQueryable<object> q = db.Query<T>()` creates **no node** (P2b), so `q.Count()` is
  `Count<object>(root)`.
  - Over an **entity** source, parameterless terminals work in 3.9.0 (P5, P5b, P13, P13b).
  - Over a **scalar** source, `First` returns the whole projected list (P6, P7): a silent wrong result.
- **`Queryable.Cast<TResult>` always creates a node**, even `Cast<T>()` on an `IQueryable<T>` (P1, P2). The
  short-circuit belongs to `Enumerable.Cast`, not `Queryable.Cast`. The 3.9.0 parse loop has no `Cast`
  branch, so the node is ignored: `q.Cast<object>().Count()` is correct (P5), and
  `Select(scalar).Cast<object>().First()` is the scalar bug (P6).
- **`OfType`** always creates a node and, like `Enumerable.OfType`, **drops nulls**.
- **Core `InternalsVisibleTo`** already covers `SqlServer.Tests`, `.DotNet9` and `.NetFramework`.
- **The net48 test project** is an old-style csproj compiled as C# 7.3. net48's `Queryable` lacks
  `Chunk`/`DistinctBy`/`MinBy`/`Order`/`Take(Range)`/the default-value overloads.

### 1.5 Executed premise probes (2026-09-30)

**Rule (rev 6):** every premise about how a shape behaves (System.Linq or a database) cites a probe that was
actually run. Reasoning alone doesn't count. Two plan rounds were spent on premises nobody had executed.

Throwaway probe tests ran on the real providers (SQL Server `FUNKY_CONNECTION`; local PostgreSQL 18; MySQL;
SQLite temp DB) and were deleted afterwards. The r5 reviewer separately confirmed P1–P4 on net48, net8 and
net9 with a mimic provider. Results are for 3.9.0.

| # | Shape | Result |
|---|---|---|
| P1 | `q.Cast<T>()` (identity) | **New node** (`…SqlQueryable.Cast()`); not the same instance |
| P2 | `q.Cast<object>()` | New node |
| P2b | `IQueryable<object> o = q` | **No node** (same instance) |
| P3 | `((IOrderedQueryable<T>)q.Where(w)).ThenBy(k)` | `ArgumentException` from System.Linq (`IQueryable` where `IOrderedQueryable` expected) |
| P3b | `((IOrderedQueryable<T>)q.Skip(1)).ThenBy(k)` | `ArgumentException` (same) |
| P4 | `((IOrderedQueryable<T>)root).ThenBy(k)` | Works: `ORDER BY id ASC` |
| P4b | helper `w is IOrderedQueryable<T> o ? o.ThenBy(k) : w.OrderBy(k)` over `q.Where(…)` | `ArgumentException` (pre-existing; #16) |
| P5 | `q.Cast<object>().Count()` | Correct (`SELECT COUNT(*)`) |
| P5b | `q.OrderBy(Id).Cast<object>().First()` | Correct (row Id=1) |
| P6 | `q.Select(p => p.FirstName).Cast<object>().First()` | **Wrong:** returns the whole `List<string>` (8,546 items) |
| P7 | `IQueryable<object> s = q.Select(p => p.FirstName); s.First()` | **Wrong:** returns the whole list |
| P7b | same `s`; `s.Take(3).ToList()` | Correct (3 items) |
| P7c | `IQueryable<object> s = q.OrderBy(Id).Select(p => p.FirstName); s.Skip(2).Take(3).ToList()` | Correct (matches in-memory `Skip(2).Take(3)`) |
| P7d | `IQueryable<object> s = q.Select(p => p.FirstName); s.Distinct().ToList()` | Correct (1,503 = in-memory distinct count) |
| P7e | `q.Select(p => p.FirstName).Cast<object>().ToList()` | Correct (8,546 items) |
| P7f | `q.Select(p => p.FirstName).Cast<object>().Count()` | Clean `NotSupportedException` (scalar result-type guard) |
| P14 | `q.Where(Id <= 3).Cast<object>().ToList()` (entity, enumerated) | **`InvalidCastException`**: `Execute` treats `IEnumerable<object>` as a single row, because `isCollection` tests assignability to `IEnumerable<T>` (SqlLinqQueryProvider.cs:99 and the sibling equivalents at :58) |
| P14b | `IQueryable<object> c = q.Where(Id <= 3); c.ToList()` | Correct (no node, so it enumerates as `IEnumerable<T>`) |
| P14c | `q.Where(Id <= 3).Cast<PersonEntity>().ToList()` (identity) | Correct |
| P14d | `q.Where(Id <= 3).OrderBy(Id).Cast<object>().First()` | Correct |
| P8 | `OrderBy(Id).ThenBy(Id)` / `.ThenByDescending(Id)` | SQL Server: `SqlException` ("column specified more than once in the order by list"). PostgreSQL / MySQL / SQLite: execute. |
| P9 | join entity `OrderBy(Id).Take(2).Select(new T { Id, FirstName })` (key projected) | Works on all 4: bare `ORDER BY id` binds to the `Id` alias |
| P10 | `Skip(1).ToList()` / `OrderBy(Id).Skip(1).First()` (no `Take`) | SQLite: **syntax error near `OFFSET`**. PostgreSQL: `OFFSET 1`, OK. MySQL: `LIMIT 18446744073709551615 OFFSET 1`, OK. SQL Server: OK. |
| P11 | `Take(3).Distinct()` over the full (keyed) entity | Correct on all 4 (rows already distinct) |
| P13 | `IQueryable<object> c = q; c.Count()` | Correct |
| P13b | `IQueryable<object> c = q.OrderBy(Id).Skip(1); c.First()` | Correct (row Id=2) |

**Still unexecuted** (Task 1 red tests): the new 3.10 behaviors themselves; the net48/net9 rendering of
`MethodInfo` signatures for the literal set.

---

## 2. Decisions (all made by the owner, 2026-09-30)

| # | Decision | Outcome |
|---|---|---|
| **D1** | `Single`/`SingleOrDefault`/`Last`/`LastOrDefault`/`LongCount` | **Implement.** `Single*` uses a row limit, not the paging path (§5.2.3). |
| **D2** | Allow-list form | **Exact-overload allow-list in Core** (`QueryOperatorPolicy`), run as a **pre-pass over the method spine** before translation. It guarantees that no query or aggregate command runs for a rejected chain; schema discovery at `Query<T>()` is unaffected. |
| **D3** | Version / branch | **3.10.0** on `development/3.10`; beta first, stable after a Sentinel call-list smoke test. |
| **D4** | Local PostgreSQL | Native PostgreSQL 18 via the suite's fallback connection; CI uses `postgres:17`. |
| **D5** | `Cast`/`OfType` | **Identity**, plus *(owner amendment, rev 6)* **reference-conversion `Cast`**.<br>• `Cast<X>` where `X` = the source element type: a no-op.<br>• `Cast<TBase>` where the source element type is a **reference type assignable to `TBase`** (e.g. `object`, an interface, a base class): **transparent**, like the implicit conversion `IQueryable<TBase> b = q` (P2b, P5). Later operators are checked by the covariance table against the *effective* element type, which looks through transparent `Cast` nodes.<br>• `OfType<X>`: a no-op only when `X` is the source element type and that type is the entity `T` (rows are never null) or a non-nullable value type.<br>• Everything else is rejected: boxing `Cast` (`Select(p => p.Id).Cast<object>()`), unrelated-type `Cast`, non-identity `OfType`, and `OfType` over a nullable or reference scalar (it would have to drop nulls; the message says to use `Where(x => x.M != null)` before the projection). |
| **D6** | `Last*`/`ElementAt*` with paging | Reject both. |
| **D7** | Disclosure | Changelog + README "upgrade strongly recommended"; the owner decides on a GitHub Security Advisory. |
| **D8** | Operators after `Skip`/`Take` | **Reject by position** in the Core pre-pass. Allowed after the first `Skip`/`Take`:<br>• one `Take` after a `Skip`, when only `Select`/an allowed D5 `Cast`/`OfType` sit between them (projection and conversion commute with `Take`);<br>• `Select`;<br>• parameterless `First*`/`Single*`;<br>• an allowed D5 `Cast`/`OfType`.<br>Everything else is rejected before any query runs. Some rejected shapes happen to be correct in 3.9.0, e.g. `Take(3).Distinct()` over a keyed entity (P11); the Changelog lists them.<br>`Take(n ≤ 0)` returns an empty sequence without SQL. `Skip(n < 0)` is treated as `Skip(0)`. |
| **D9** | Aggregate correctness | **Moved to 3.10.1** (D12). The owner accepts the recommended direction with changes; details and open questions are in §10. In 3.10.0, aggregates keep their 3.9 behavior, except `LongCount` (new) and rejection of the non-generic `Sum()`/`Average()` overloads, which already threw in 3.9. |
| **D10** | A second `OrderBy*` after an earlier `OrderBy*`/`ThenBy*` anywhere on the spine | **Reject**, in the Core pre-pass. The message says to use `ThenBy`, or put the primary key first. *(Rev 4 added a second clause for a `ThenBy*` separated from its ordering. Rev 6 removed it: System.Linq throws `ArgumentException` for that shape before the provider sees it (§1.5 P3), so it can't reach the policy. A `ThenBy*` directly on the root works as the primary ordering (P4).)* |
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

  Ordering and paging always come **before** the projection: `OrderBy…[ThenBy…][Skip/Take].Select(…)`.
  Ordering *after* a subset `Select` is the §8 divergence, and is never used as a passing shape.
- **AC12-2** Ordering by a remote/computed member emits exactly its resolved fragment, with no base-table
  prefix, unchanged from 3.9.0.
- **AC12-3** For an entity without remote joins, the **ORDER BY translation** of
  `OrderBy*`/`ThenBy*` chains consumed by enumeration or `First*` is byte-identical to 3.9.0. Other ACs change
  ordering SQL on purpose, and are excluded:
  - AC12-8: ternary nulls;
  - AC12-9: duplicate keys;
  - AC13-2: `Last*` inverts or synthesizes the order;
  - AC13-12: a second `OrderBy` is rejected.

  Other ACs also change non-ORDER-BY SQL on purpose: the `Single*` row limit, SQLite `LIMIT -1`.
- **AC12-4** A ternary/`CASE` ordering over own columns on a join entity is qualified inside the `CASE`.
- **AC12-5** The ORDER BY synthesized for `Last*` (AC13-2) is qualified on join entities, including with a
  narrow projection that omits the key (no `Distinct`). The shape is `Select(…).Last()` with no ordering
  after the projection.
- **AC12-6** On the join entity, `Distinct()` + custom projection + own-column `OrderBy`, spelled
  ordering-first (`OrderBy(k).Select(…).Distinct()`):
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

  **Covariance follows the §5.2.1 cell table.** A covariant conversion is either the no-node
  `IQueryable<TBase> b = q` or a reference-conversion `Cast<TBase>()` (D5). Both are judged against the
  *effective* element type.
  - **Entity source.** A parameterless terminal over a converted entity source is not rejected. `Count`, `Any`
    and `First*` keep their correct 3.9.0 behavior (P5, P13); `LongCount`, `Single*` and `Last*` get the
    corrected semantics of AC13-1/AC13-2/AC13-3. This holds at the root and after
    `Where`/`OrderBy*`/a subset `Select`. **After `Skip`/`Take`, D8 governs**: `First*`/`Single*` are allowed
    (P13b), and `Count`/`Any`/`LongCount`/`Last*` get the D8 message.
  - **Scalar source, terminal or lambda-bearing.** A covariant **terminal** (`First*`, `Single*`, `Last*`,
    `Count`, `LongCount`, `Any`) or covariant **lambda-bearing** operator over a **scalar projection** is
    rejected before any query, with the scalar-source covariant message.
    - Both spellings are covered: `Select(p => p.FirstName).Cast<object>().First()` and
      `IQueryable<object> s = q.Select(p => p.FirstName); s.First()`.
    - Each returns the whole list in 3.9.0 (P6, P7). `Count` already threw cleanly there (P7f).
  - **Scalar source, sequence or enumeration.** Covariant `Skip`/`Take`/`Distinct` and plain enumeration
    (`…Cast<object>().ToList()`) **stay allowed**: they return the correct projected values (P7b–P7e).
  - **Entity source, lambda-bearing or sequence operator** with a covariant `TSource`: rejected with the
    covariant message.
  - **Other projected types** (a `Select` to a DTO or an expression): the covariance check defers, and the
    existing `Select`-shape guard rejects the `Select` with its own message.

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
- **AC13-5** Every shape built only from allowed operators, in allowed positions, that returned correct
  results in 3.9.0 still works identically.
  - The pre-pass rejects by **rule** (allow-list, D5, D8, D10, covariance). Most rejected shapes returned wrong
    results or threw obscurely in 3.9.0. A few happened to be correct: `Take(k).Distinct()` over a keyed entity
    (P11), `Take(k≥1).Any()`, `Take(10).Take(5)`, `OrderBy(k).ElementAt(0)`, `DefaultIfEmpty()` over a
    non-empty set.
  - The Changelog **Changed** section lists each rule with such examples.
  - All four existing suites stay green, and there's one oracle row per allowed family. Aggregate rows are limited to shapes correct in
  3.9.0 on all four providers: `Count`, `Any`/`All`, and `Sum`/`Min`/`Max` over `int`, on non-empty sets. No
  `Average` rows: SQLite's `ROUND(…, 10)` alters even `double` averages. The rest is 3.10.1. Oracle predicates
  reference only members seeded non-null, and oracle shapes never place a predicate or ordering after a
  subset `Select` (§4.1, §8).
- **AC13-6** *(D5)*
  - Identity `OfType<X>()` is a no-op on the entity (at the root and after `OrderBy`) and on a non-nullable
    scalar (`Select(p => p.Id).OfType<int>()`).
  - Identity `Cast<T>()` is a no-op.
  - A reference-conversion `Cast<TBase>()` is transparent: `q.Cast<object>().Count()`/`.First()` match 3.9.0
    (P5, P5b), and later operators follow AC13-4's covariance rules.
  - **Enumerating** a reference-conversion `Cast` over the entity (`q.Where(…).Cast<object>().ToList()`)
    returns the entity rows, the same as the implicit conversion (P14b). In 3.9.0 it throws
    `InvalidCastException` (P14); fixed by §5.2.2's collection-detection change.
  - These throw: `OfType` over a nullable or reference scalar; non-identity `OfType`; boxing `Cast`
    (`Select(p => p.Id).Cast<object>()`); unrelated-type `Cast` (`Cast<AddressEntity>()` on a person query).
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
  even after a scalar `Select`. Precedence:
  - pre-pass, evaluated per node inner→outer in this order: allow-list (`GroupBy` special-cased) → D8 → D10 →
    D5/covariance;
  - then parse-loop guards;
  - then execute-time guards.
- **AC13-9** `Advanced.md` and `FUNKYORM_AI_ADVANCED.md` carry the operator table, including the paging rule,
  the second-`OrderBy` rule and the `Last`/`Distinct` note. A doc test checks the table against
  `SupportedOperators`. Changelog and README record the fixes, the behavior changes, and the aggregate known
  issues deferred to 3.10.1.
- **AC13-10** *(D8)* After `Skip`/`Take`:
  - **Allowed and correct:**
    - `Skip(n).Take(k)`, and `Skip(n).X.Take(k)` where `X` is only `Select` and/or an allowed D5
      `Cast`/`OfType`, e.g. `Skip(n).Select(…).Take(k)`, `Skip(n).OfType<T>().Take(k)`,
      `Skip(n).Cast<T>().Take(k)`;
    - `Select` (subset or scalar);
    - parameterless `First*`/`Single*`;
    - an allowed D5 `Cast`/`OfType` (identity, or a reference conversion over the entity).
  - **Rejected before any query**, each asserting the "after Skip/Take" message: every aggregate, `Where`,
    predicate-bearing `First*`/`Single*`, `Last*`, `OrderBy*`, `Distinct`, a second `Skip`/`Take`, and
    `Take(n).Skip(m)`. Any other operator placed between `Skip` and `Take` is itself rejected at its own node.
    (`ThenBy*` can't follow `Skip`/`Take` at all; System.Linq throws first, P3b.)
  - **Precedence.** When D8 and D10 both apply at a node (e.g. `OrderBy(a).Skip(n).OrderBy(b)`), the D8
    message wins.
  - **Empty results without SQL:** `Take(n ≤ 0)` returns an empty sequence, whether it's the full entity,
    a subset projection, a scalar projection, or `Skip(n).Take(0)`. `First`/`Single` then throw and
    `*OrDefault` returns `null`.
  - `Skip(n < 0)` behaves as `Skip(0)`.
- **AC13-11** *(deferred to 3.10.1 — §10.)*
- **AC13-12** *(D10)* `OrderBy*` after any earlier `OrderBy*`/`ThenBy*` on the spine throws
  `NotSupportedException` with the D10 message before any query runs, including across
  `Where`/`Select`(subset)/`Distinct` in between.
  - A `ThenBy*` applied directly to the root (`((IOrderedQueryable<T>)q).ThenBy(k)`) still works as the primary
    ordering (P4).
  - A `ThenBy*` after any other call can't be built: System.Linq throws `ArgumentException` (P3). That also
    breaks `is IOrderedQueryable` sort helpers on composed queries (P4b), a pre-existing bug tracked in #16
    and not changed here.
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
  - **No predicate or ordering after a subset `Select`**, in **any** passing shape, oracle or not. FunkyORM
    evaluates it against base columns, while LINQ sees the projected defaults (§8). The only exceptions are
    rejection rows, where the shape is rejected for another reason (D8/D10).
- **Fresh root per shape.** One `Query<T>()` root is never reused, except by the AC13-13 tests, which do it
  on purpose.
- **Covariance rows are spelled both ways:** the no-node `IQueryable<object> o = …` (P2b) and
  `.Cast<object>()` (a node, P2). The two take different paths through the policy, so each needs its own row.
- **Shape premises cite §1.5.** A test whose premise about System.Linq or a database hasn't been executed is
  flagged UNVERIFIED in the matrix and must go red for the right reason in Task 1.
- **SQL capture.** For "nothing executed": obtain the `IQueryable`, clear `Log`'s builder, run the shape,
  then assert the builder is empty. **SQL-shape asserts normalize whitespace first** (collapse `\s+` to one
  space), because PostgreSQL and SQLite emit `LIMIT`/`OFFSET` on separate lines and MySQL on one.
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
| AC12-1 | `[DataTestMethod] OwnColumnOrdering_OnJoinEntity_QualifiedSql_ExecutesInOrder` — {OrderBy, OrderByDesc, ThenBy-after-remote, ThenByDesc-after-remote} × {full, subset-without-key, scalar-of-FirstName} × {paged, unpaged} (24 rows); every row is `OrderBy…[Skip/Take].Select(…)`, with ordering and paging before the projection | all 4 |
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
| AC13-4 | `[DataTestMethod] Rejected_Operator_ThrowsNotSupported_NamesOperator_NoQueryExecuted` (one row per AC13-4 shape, including `ScalarProjection_ParameterlessSum`, `CovariantCastObject_ThenWhere` and `Covariant_Take`), `Allowed_PredicateWithCollectionContains_NotRejected`, `NonQueryableSpineMethod_Rejected`, `NonCallNonRootSpineNode_Rejected` (DB-free, hand-built `Convert` node), `ForeignQueryableConstantRoot_Rejected` (DB-free: a constant whose value is a composed queryable)<br>**Covariance cell table (§5.2.1)**, one row per cell, each spelled **both ways**: a no-node `IQueryable<object> o = …` and a `.Cast<object>()`.<br>• `Covariant_EntitySource_ParameterlessTerminal_NotRejected_MatchesOracle` over {Count, LongCount, Any, First, FirstOrDefault, Single (source pre-filtered to one row), Last}, at the root, after `Where`, after `OrderBy` (First/Last) and after a subset `Select`.<br>• `Covariant_EntitySource_AfterPaging_D8Governs` over {`Skip(1)`→First: allowed and matches the oracle; `Take(5)`→Count: D8 message; `Take(5)`→Where: D8 message}.<br>• `Covariant_EntitySource_LambdaOrSequence_Rejected` over {`Where`, `OrderBy`, `Take`, `Distinct`}, asserting the entity covariant message.<br>• `Covariant_ScalarSource_TerminalOrLambda_Rejected_NoQuery` over {First, FirstOrDefault, Single, SingleOrDefault, Last, LastOrDefault, Count, Any, `Where`}, asserting the scalar covariant message. **Red on 3.9.0**: `First*`/`Single*`/`Last*` return the whole list (P6, P7); `Count`/`Any` give the old guard message (P7f).<br>• `Covariant_ScalarSource_SequenceOrEnumeration_Allowed_MatchesOracle` over {`Skip.Take`, `Take`, `Distinct`, enumeration of `Cast<object>()`} (P7b–P7e).<br>• `Covariant_OtherProjectedSource_DefersToSelectShapeGuard` (`Select(p => new Dto{…})` then a covariant `First`: the `Select`-shape message).<br>• `NonCovariant_EntityAndScalarSources_Unchanged` (control rows with `TSource` equal).<br>**Second layer**, DB-free and direct (§5.2.2): `ScalarProjectionGuard_TerminalWithObjectResult_Throws`, `ScalarProjectionGuard_CollectionEnumeration_Passes`. | all 4 + Core |
| AC13-5 | `[DataTestMethod] Allowed_Operator_MatchesOracle` (one row per allowed family, with its expected outcome); the four existing suites | all 4 |
| AC13-6 | `OfType_Identity_AtRoot_IsNoOp`, `OfType_Identity_AfterOrderBy_IsNoOp`, `OfType_Identity_AfterScalarProjection_NonNullable_IsNoOp`, `OfType_Identity_OverNullableScalar_Rejected` (seeded nulls), `OfType_NonIdentity_Throws`, `Cast_Identity_IsNoOp` (`q.Cast<PersonEntity>().ToList()`; P1 shows it's a real node), `Cast_ReferenceConversion_Count_MatchesOracle` and `Cast_ReferenceConversion_OrderedFirst_MatchesOracle` (P5, P5b), `Cast_ReferenceConversion_Enumerated_MatchesOracle` (`q.Where(marker).Cast<object>().ToList()`; red on 3.9.0, P14), `Cast_Boxing_Rejected` (`Select(p => p.Id).Cast<object>()`), `Cast_NonIdentity_UnrelatedType_Throws` (`Cast<AddressEntity>()` on a person query) | all 4 + Core |
| AC13-7 | `SupportedOperators_ExactLiteralSetPinned`, `ClassifierSweep_EveryQueryableMethod_MatchesLiteralSet`, `NonQueryableOverload_IsRejected` (MSTest: `SqlServer.Tests` net8, `SqlServer.Tests.NetFramework` net48); xUnit twin `QueryOperatorPolicyLiteralSetTests` (`SqlServer.Tests.DotNet9` net9) | Core, 3 runtimes |
| AC13-8 | `GroupBy_Rejected_KeepsDedicatedMessage`; existing scalar tests; new in the siblings: `ScalarProjection_WithReducingTerminals_ThrowNotSupported`; `ScalarProjection_WithSingleOrLast_ThrowsNotSupported`; `Rejected_OperatorOuterToFailingInnerOperator_PolicyMessageWins` (`Select(p => p.Id).Where(x => x > 0).Reverse()`) | all 4 |
| AC13-9 | `OperatorDocTable_MatchesSupportedOperators` (reads the table from both docs); prose reviewed in the gauntlet | SqlServer.Tests |
| AC13-10 | `[DataTestMethod] Operator_AfterPaging_Rejected_BeforeAnyQuery` over {Count, LongCount, Any, All, Sum, Average, Min, Max, Where, First(pred), Single(pred), Last, OrderBy, OrderByDescending, OrderBy(a).Skip(n).OrderBy(b) [D8 wins over D10], Distinct, Skip-after-Skip, Take-after-Take, Take-then-Skip} — each row asserts the "after Skip/Take" message; `[DataTestMethod] Operator_AfterPaging_Allowed_MatchesOracle` over {Skip.Take, `Skip.Select(subset).Take`, `Skip.Select(scalar).Take`, `Skip.OfType<T>().Take`, `Skip.Cast<T>().Take`, `Take.Cast<object>()` enumerated, Select subset, Select scalar, First(), FirstOrDefault(), Single(), SingleOrDefault(), OfType-identity entity, `Skip(n).Select(p => p.Id).OfType<int>()`, Skip-only.First()}; `[DataTestMethod] TakeNonPositive_ReturnsEmpty_NoQuery` over {Take(0) full, Take(0) subset, Take(0) scalar, Skip(2).Take(0), Take(-1)}; `Take0_First_Throws_NoQuery`, `Take0_FirstOrDefault_ReturnsNull_NoQuery`, `Take0_Single_Throws_NoQuery`, `Take0_SingleOrDefault_ReturnsNull_NoQuery`; `ScalarProjection_Take0_First_ThrowsScalarGuard_NoQuery` (the scalar result-type guard wins over the empty short-circuit); `SkipNegative_BehavesAsSkipZero` | all 4 |
| AC13-12 | `[DataTestMethod] Ordering_AfterEarlierOrdering_Rejected_BeforeAnyQuery` over {OrderBy.OrderBy, OrderBy.ThenBy.OrderByDescending, OrderBy.Where.OrderBy, OrderBy.Select(**subset**).OrderBy, OrderBy.Distinct.OrderBy}, each asserting the D10 message; `ThenBy_OnRoot_IsPrimaryOrder` (`((IOrderedQueryable<T>)q).ThenBy(k).Where(marker)` gives the same rows and order as `OrderBy(k).Where(marker)`, P4) | all 4 |
| AC13-13 | `SqliteRoot_ReusedAfterProjection_BareRootNotNarrowed`, `SqliteRoot_ReusedAfterOrderedQuery_BareRootNoInheritedOrder`, `SqliteRoot_ReusedAfterOrderedQuery_ThenLast_UsesIdDesc`, `SqliteRoot_ReusedAfterParameterizedProjection_NoDuplicateParameters` | SQLite |
| AC13-14 | `SkipOnly_ToList_Executes`, `SkipOnly_First_ReturnsExpectedRow` (SQLite red; regression rows in the others); SQLite only: `SkipOnly_EmitsLimitMinusOneOffset` (SQL shape) | all 4 |

### 4.3 Interface coverage (new/changed members → tests)

| Member | Tests that call it on purpose |
|---|---|
| Core `QueryOperatorPolicy.EnsureSupported(Expression expression)` *(new, public, `void`)* | `QueryOperatorPolicyTests.*`; every `Rejected_*`/`Allowed_*`/`Operator_AfterPaging_*`/`Ordering_AfterEarlierOrdering_*`/`Covariant_*` row |
| Core `ScalarProjectionGuard.EnsureCollectionResult(Type resultType, Type memberType, Expression expression)` *(new, public; rev 6)* | `ScalarProjectionGuard_TerminalWithObjectResult_Throws`, `ScalarProjectionGuard_CollectionEnumeration_Passes` (direct); every existing scalar-projection test through `ExecuteScalarProjection` |
| `*LinqQueryProvider.Execute` — generalized `isCollection` *(rev 6)* | `Cast_ReferenceConversion_Enumerated_MatchesOracle`; all existing enumeration tests (the `X = T` path) |
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
| Skip the `TSource` = source-element check | `Covariant_EntitySource_LambdaOrSequence_Rejected` rows, `Rejected_…[CovariantCastObject_ThenWhere]` (`InvalidCastException` instead of the covariant-mismatch message) |
| Apply the `TSource` check to parameterless terminals over the entity too | `Covariant_EntitySource_ParameterlessTerminal_NotRejected_MatchesOracle` rows |
| Exemption not restricted to entity-typed sources (the rev 4 bug) | `Covariant_ScalarSource_TerminalOrLambda_Rejected_NoQuery[First/FirstOrDefault/Single/Last]`, both spellings (whole list returned instead of NotSupported) |
| Effective element type doesn't look through a transparent `Cast` | `Covariant_ScalarSource_TerminalOrLambda_Rejected_NoQuery[First, Cast spelling]` (whole list again), `Covariant_EntitySource_LambdaOrSequence_Rejected[Where, Cast spelling]` (`InvalidCastException` instead of NotSupported) |
| Scalar covariant **sequence** operators rejected too | `Covariant_ScalarSource_SequenceOrEnumeration_Allowed_MatchesOracle` rows |
| Covariance evaluated before D8 | `Covariant_EntitySource_AfterPaging_D8Governs[Take(5)→Where]` (expects the D8 message) |
| Second-layer guard checks only the result type (not "is a terminal") | `ScalarProjectionGuard_TerminalWithObjectResult_Throws` (direct, DB-free) |
| Second-layer guard rejects valid enumeration | `ScalarProjectionGuard_CollectionEnumeration_Passes` |
| Covariant rejections use the generic "not translated" message, or the entity/scalar variants are swapped | `Covariant_*_Rejected` rows (assert "before converting the element type" for entity, "over a scalar projection" for scalar) |
| Treat any non-call node as the root | `NonCallNonRootSpineNode_Rejected` |
| Accept any `IQueryable` constant as the root (not only the queryable's own root) | `ForeignQueryableConstantRoot_Rejected` |
| `isCollection` unchanged (still `IEnumerable<T>` only) | `Cast_ReferenceConversion_Enumerated_MatchesOracle` (`InvalidCastException`, P14) |
| Policy visits the whole tree | `Allowed_PredicateWithCollectionContains_NotRejected` |
| Allow non-`Queryable` spine methods | `NonQueryableSpineMethod_Rejected` |
| Classifier allow-by-default | `ClassifierSweep_EveryQueryableMethod_MatchesLiteralSet` |
| Per-TFM computed expectation instead of the literal set | `SupportedOperators_ExactLiteralSetPinned` (literal count/signatures) |
| Policy inside the loop instead of a pre-pass | `Rejected_OperatorOuterToFailingInnerOperator_PolicyMessageWins` |
| Drop the `GroupBy` message | `GroupBy_Rejected_KeepsDedicatedMessage` |
| Identity check against the entity type instead of the source element type | `OfType_Identity_AfterScalarProjection_NonNullable_IsNoOp` |
| Identity `OfType` allowed over a nullable scalar | `OfType_Identity_OverNullableScalar_Rejected` |
| Reject all `Cast`/`OfType` | `OfType_Identity_AtRoot_IsNoOp`, `Cast_Identity_IsNoOp` |
| Reject reference-conversion `Cast` (D5 before the owner's amendment) | `Cast_ReferenceConversion_Count_MatchesOracle`, `Cast_ReferenceConversion_OrderedFirst_MatchesOracle` |
| Allow any `Cast`/`OfType` | `OfType_NonIdentity_Throws`, `Cast_NonIdentity_UnrelatedType_Throws`, `Cast_Boxing_Rejected` |
| Drop the positional guard | `Operator_AfterPaging_Rejected_BeforeAnyQuery` rows |
| Positional guard rejects `Skip.Take` or `Skip`-only `First` | `Operator_AfterPaging_Allowed_MatchesOracle` rows |
| `Take(0)` sent to the DB | `TakeNonPositive_ReturnsEmpty_NoQuery` rows |
| Empty-`Take` short-circuit only on the entity path | `TakeNonPositive_…[Take(0) scalar]` |
| Empty-`Take` short-circuit placed before the scalar dispatch (bypasses the result-type guard) | `ScalarProjection_Take0_First_ThrowsScalarGuard_NoQuery` |
| No negative-`Skip` clamp | `SkipNegative_BehavesAsSkipZero` |
| Allow a second `OrderBy` / only check adjacent calls | `Ordering_AfterEarlierOrdering_Rejected_BeforeAnyQuery` rows (incl. across `Where`/`Select`/`Distinct`) |
| D10 evaluated before D8 at the same node | `Operator_AfterPaging_Rejected_…[OrderBy(a).Skip(n).OrderBy(b)]` (expects the D8 message) |
| D8 rejects `Take` after `Skip` even with only `Select`/D5 conversions between | `Operator_AfterPaging_Allowed_…[Skip.Select(subset).Take]`, `[Skip.Select(scalar).Take]`, `[Skip.OfType<T>().Take]`, `[Skip.Cast<T>().Take]` |
| Policy rejects a `ThenBy*` directly on the root | `ThenBy_OnRoot_IsPrimaryOrder` |
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
   - **Spine walk, two passes.**
     - **Pass 1 (outer→inner):** follow `MethodCallExpression.Arguments[0]` to the root, classifying each node
       with the allow-list **before** reading its `Arguments[0]`.
       - **Terminal:** a `ConstantExpression` whose value is an `IQueryable` **whose `Expression` is that same
         constant**, i.e. the queryable's own root. A constant wrapping a composed queryable would otherwise
         silently drop that queryable's operators.
       - Any other non-call node → reject.
       - Lambdas and other arguments are never visited.
     - **Pass 2 (inner→outer):** apply D8, D10 and D5/covariance in that order at each node. The root
       element type is now known.
   - **Element type** of a node = the `T` in the `IEnumerable<T>` interface of its `Type`. That works for
     `SqlQueryable<T>`, `IQueryable<T>` and `IOrderedQueryable<T>` alike.
   - **Effective element type** = the element type, except that a transparent (reference-conversion) `Cast`
     node passes through its source's effective element type. The no-node conversion `IQueryable<TBase>`
     needs no special handling, because it leaves the source node's type unchanged (P2b).
   - **Classification.** `IsAllowed(MethodInfo m)` uses the key
     `m.IsGenericMethod ? m.GetGenericMethodDefinition() : m`. It rejects non-`Queryable` declaring types, and
     keys outside a static allowed set built from `typeof(Queryable).GetMethods()` by name plus shape:
     - one `Expression<Func<TSource, …>>` lambda;
     - `Skip`/`Take` take `int`;
     - no comparer, default-value or `Range` parameters.

     Non-generic overloads are never in the set.
   - **`TSource` check: covariance cell table** (rev 5 redesign; corrected in rev 6 against §1.5 evidence;
     every cell has a §4.2 row).
     - `TSource` is the first generic argument of an allowed method whose first parameter is
       `IQueryable<TSource>`. It's compared with the source's **effective** element type.
     - **Entity source:** the effective element type equals the **root** element type (the entity `T`). That
       holds at the root, and after `Where`/`OrderBy*`/`Skip`/`Take`/`Distinct`/a subset
       `Select(x => new T { … })`/identity `OfType<T>`/a transparent `Cast`.
     - **Scalar source:** after `Select(x => x.M)`. **Other source:** after any other `Select` body.
     - `Cast`/`OfType` nodes themselves follow D5; this table governs the operators **after** them.

     | Source | Operator kind | `TSource` = effective element type | `TSource` ≠ (covariant supertype) |
     |---|---|---|---|
     | Entity `T` | Parameterless terminal: `Count`, `LongCount`, `Any`, `First*`, `Single*`, `Last*` with no predicate | allowed | **allowed**; entity rows are read as `T` and cast (P5, P5b, P13). After `Skip`/`Take`, D8 decides first (P13b). |
     | Entity `T` | Lambda-bearing: `Where`, `Select`, `OrderBy*`, `ThenBy*`, predicate terminals, selector aggregates, `All` | allowed | rejected (entity covariant message) |
     | Entity `T` | Sequence without a lambda: `Skip`, `Take`, `Distinct` | allowed | rejected (entity covariant message) |
     | Scalar `X` | Parameterless terminal | allowed by the policy (the scalar guards then reject it) | **rejected** (scalar covariant message); 3.9.0 returns the whole list for `First*`/`Single*`/`Last*` (P6, P7) |
     | Scalar `X` | Lambda-bearing | allowed by the policy (the scalar composition guard then rejects it) | rejected (scalar covariant message) |
     | Scalar `X` | Sequence without a lambda: `Skip`, `Take`, `Distinct` | allowed | **allowed**; correct in 3.9.0 (P7b, P7c, P7d) |
     | Other projected type | Any | — | defers: the parse loop's `Select`-shape guard rejects the `Select` with its own message |
   - `IsAllowed` is **public**.
   - **Allowed operators:**
     - `Where`, `Select`;
     - `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`;
     - `Skip`, `Take`, `Distinct`;
     - `First*`, `Single*`, `Last*` (with and without predicate);
     - `Any`, `All`, `Count`, `LongCount`;
     - `Sum`, `Average`, `Min`, `Max` (generic, with selector; behavior unchanged until 3.10.1);
     - `Cast`/`OfType` per D5 (identity, or a reference-conversion `Cast`).
   - **Pass-2 rules, in this order at each node:**
     1. D8, after the first `Skip`/`Take`. A `Take` is allowed after the `Skip` when only `Select` and/or
        allowed D5 `Cast`/`OfType` sit between them.
     2. D10: an `OrderBy`/`OrderByDescending` after any earlier ordering call is rejected.
     3. D5 and the covariance table.

     *(Rev 4's separated-`ThenBy*` clause was removed in rev 6: the shape can't be built, P3.)*
   - **Messages.**
     - `GroupBy` keeps its dedicated message.
     - Other rejected operators: `"{Op}(...) is not translated to SQL in this version. Materialize first and
       apply it in memory: query.ToList().{Op}(...)."`
     - Positional (D8): `"{Op}(...) after Skip/Take is not translated …"`.
     - D10, second `OrderBy*`: `"A second OrderBy is not translated; use ThenBy, or put the primary key
       first."`
     - Covariant mismatch, entity source: `"{Op}(...) over a converted element type ({TSource}) is not
       translated. Apply {Op} before converting the element type, or query the concrete type."`
     - Covariant mismatch, scalar source: `"{Op}(...) over a scalar projection converted to {TSource} is not
       translated. Materialize first and apply it in memory: query.Select(x => x.M).ToList().{Op}(...)."`
     - Boxing or unrelated-type `Cast` (D5): `"Cast<{X}>() is not translated; FunkyORM supports only
       identity and reference-conversion casts. Materialize first: query.ToList().Cast<{X}>()."`
     - Nullable `OfType`: the message points to `Where(x => x.M != null)` before the projection.
   - `EnsureSupported` returns `void`. It only throws.
2. **Wiring.**
   - Each provider's `ParseExpression` calls `QueryOperatorPolicy.EnsureSupported(expression)` first. For
     SQLite, that's after the state reset (D11) and before the early return.
   - Remove the now-dead inline `GroupBy` check and the parameterless-aggregate-after-scalar guard
     (SqlLinqQueryProvider.cs:339-346 and the sibling copies).
   - Fix the stale comment at :128-129 and the sibling copies.
   - The scalar composition and result-type guards stay (precedence per AC13-8).
   - **Covariant entity enumeration (rev 6, owner's D5 amendment).** In each provider's `Execute`,
     `isCollection` becomes: `TResult` is (or implements) `IEnumerable<X>` with `X.IsAssignableFrom(T)`, and
     `TResult != T`. Previously it was `typeof(IEnumerable<T>).IsAssignableFrom(typeof(TResult))`
     (SqlLinqQueryProvider.cs:99; siblings :58).
     - The `List<T>` read from the database is returned as `IEnumerable<X>` through covariance.
     - This turns P14's `InvalidCastException` into the correct rows, and leaves every currently working path
       unchanged: `X = T` behaves as before.
   - **Second layer (rev 5; made testable in rev 6).** A Core public helper,
     `ScalarProjectionGuard.EnsureCollectionResult(Type resultType, Type memberType, Expression expression)`,
     is called first by `ExecuteScalarProjection` in all four providers.
     - It throws, using the existing scalar-guard message (which names the operator), when either holds:
       - the outermost call is a **terminal**, i.e. its `Method.ReturnType` isn't an `IQueryable`;
       - `resultType` can't accept a `List<memberType>`. That's the existing result-type check, now in one
         place.
     - It closes the `TResult = object` hole, where `typeof(object).IsAssignableFrom(List<X>)` is true.
     - **No `Queryable` shape reaches it**: the pre-pass rejects them first (scalar covariant cell). So it's
       tested directly, not through LINQ: `(object, string, First<object>(…))` must throw;
       `(IEnumerable<object>, string, Select(…))` must pass.
3. **`Single*` (row limit).**
   - The First/Last predicate branch generalizes to `First*`/`Single*`/`Last*` × {with, without predicate}.
     It routes the predicate to WHERE and sets `components.Terminal`.
   - **No user `Skip`/`Take`:** `RowLimit = 2`. SQL Server emits `SELECT TOP (2)` / `SELECT DISTINCT TOP (2)`;
     the others emit `LIMIT 2` after any ORDER BY. No ORDER BY is synthesized.
   - **User `Skip` or `Take`:** `Take = min(Take ?? 2, 2)` on the existing paging path.
   - It reads up to two rows through the list path, then applies LINQ cardinality.
4. **`Last*`.**
   - The visitor exposes `OrderByTerms` (`(fragment, isDescending)`, after duplicate removal).
   - `Last*` inverts every term. With no terms it uses `Id DESC`, table-qualified when the entity has joins
     (the §5.1.2 call site passes the qualifier).
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
  - ✅ Review r1: 25 findings (§9.1). Fix-verification r2: 21 (§9.2), r3: 10 (§9.3), r4: 7 (§9.4),
    r5: 8 (§9.5).
  - ✅ Owner answered the §10 questions (3.10.1), amended D5 (reference-conversion `Cast`), filed the
    sort-helper crash as follow-up #16, and deferred signing.
  - ✅ Executed premise probes recorded (§1.5).
  - ⏳ Fix-verification r6 of the rev-6 diff must be clean.
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
- **Task 4 — Core policy (two-pass, own-root check) + D8, D10 + D5 with reference-conversion `Cast` +
  covariance cell table + generalized `isCollection` + wiring + dead-guard removal + Core
  `ScalarProjectionGuard`** (4 providers).
  → AC13-4 (except the **entity-source** covariant `LongCount`/`Single`/`Last` rows), AC13-6, AC13-7,
  AC13-8, AC13-10 (rejection rows), AC13-12.
- **Task 5 — `Single*` row limit, `Skip`/`Take` values, empty-`Take` short-circuit** (4 providers).
  → AC13-1, AC13-10 (allowed and empty rows), covariant `Single` row of AC13-4.
- **Task 6 — Visitor `OrderByTerms` + ternary null** (4 providers).
  → AC12-8.
- **Task 7 — `Last*`** (4 providers).
  → AC13-2, AC12-5, covariant `Last` row of AC13-4.
- **Task 8 — `LongCount`** (4 providers).
  → AC13-3, covariant `LongCount` row of AC13-4.
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
- **Strong naming: deferred past 3.10.0 by the owner (2026-09-30).** Shipping assemblies stay
  `SignAssembly=False`. When signing is scheduled, the `InternalsVisibleTo` friends must be signed and their
  entries public-key-qualified. The new `IsAllowed` and `ScalarProjectionGuard` seams are public, so they
  add no `InternalsVisibleTo` dependency.
- **`is IOrderedQueryable` sort helpers crash on composed queries** (pre-existing; §1.5 P4b). Tracked as
  [#16](https://github.com/FuncularLabs/Funcular.FunkyOrm/issues/16) (owner: follow-up, not 3.10.0).
  Candidate fix: `CreateQuery` returns an ordered queryable only when `expression.Type` is
  `IOrderedQueryable<>`.
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
N8 weren't. The r3 reviewer also confirmed that all §9.1 dispositions still hold, apart from the net9 leg of
§9.1 #17 (now N3). It marked F1–F21 as 17 RESOLVED / 4 PARTIAL. The PARTIALs are F8, F10, F12 and F13, now
N1, N4, N5 and N10b. *(Rev 3 read "16 / 5"; corrected in rev 5, F7c.)*

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| N1 | MINOR | AC-GAP | yes | `TSource` check rejects covariant parameterless terminals that work in 3.9.0 | Exempted parameterless terminals (§5.2.1); `Covariant_ParameterlessTerminal_NotRejected_MatchesOracle`; `Covariant_Take` rejection row; two mutation rows. |
| N2 | MAJOR | AC-GAP | yes (claim) / pre-existing behavior | A `ThenBy*` cast past a `Where` drops the earlier ordering | D10 extended (same class); AC13-12 rows including the cast shapes; `ThenBy_WithNoEarlierOrdering_IsPrimaryOrder`; two mutation rows. **Superseded in rev 6 (R5-2):** the premise was never executed, and the shape throws `ArgumentException` in System.Linq (§1.5 P3), so it was reverted. |
| N3 | MINOR | PLAN-GAP | no | net9 project is xUnit; net48 project unnamed | xUnit twin plus a shared literal-set source file; net48 project named; §4.6 corrected. |
| N4 | MINOR | TEST-GAP | yes | AC13-14 misstated `Single`; the cap mutation survived | AC13-14 reworded; the cap is asserted in SQL; mutation row fixed; SQLite `SkipOnly_EmitsLimitMinusOneOffset`. |
| N5 | MINOR | TEST-GAP | yes | Empty-`Take` vs scalar guard order ambiguous | §5.2.6 pins the order; `ScalarProjection_Take0_First_ThrowsScalarGuard_NoQuery`; mutation row. |
| N6 | MINOR | TEST-GAP | yes | D10 `Select` row vacuous if scalar; precedence unpinned | Rows use a subset `Select`; D10 and "after Skip/Take" messages asserted per row. |
| N7 | MINOR | TEST-GAP | yes | Oracle rows break on NULL semantics | Oracle rule: non-null predicate members. Divergence recorded in §8 with a 3.11 recommendation. |
| N8 | MINOR | AC-GAP | no | Predicate after subset `Select` reads base columns | Oracle rule: none after a subset `Select`. Recorded in §8 as a follow-up with a candidate fix. |
| N9 | MINOR | PLAN-GAP | yes | §10 `time`-range claim wrong | Reworded per provider (§10). |
| N10 | MINOR | PLAN-GAP | yes | Bookkeeping: 16→19; `OfType` after paging; §8 `Single` wording; `Id DESC` qualification; §1.4 premise | All corrected. `Skip(n).Select(p => p.Id).OfType<int>()` row added. |

### 9.4 Task 0 fix-verification r4 of `7e97e93`

Totals: AC-GAP 2, TEST-GAP 3, HOUSE-RULE 0, PLAN-GAP 2, OTHER 0. 5 of 7 were introduced by rev 4; only F5 and
F6 weren't. The reviewer judged that none blocks Task 1, on one condition: the covariance rule is redesigned,
not patched, before the AC13-4 rows are written. It marked N1–N10 as 8 RESOLVED (some with follow-on findings)
and 2 PARTIAL (N8 → F5, N10 → F7b).

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| F1 | MAJOR | AC-GAP | yes (2nd in a row on this rule) | Covariance exemption lets `Select(scalar).Cast<object>().First()` return the whole list | **Redesigned:** cell table (§5.2.1), with the exemption limited to entity-typed sources. Second-layer terminal guard in `ExecuteScalarProjection`. One row per cell, including `Covariant_ScalarSource_Rejected_NoQuery` (red on 3.9.0). Mutation rows; AC13-4 restated. **Revised in rev 6 (R5-1, R5-3):** rows re-spelled, the scalar sequence cell corrected, and the guard moved to a testable Core helper. |
| F2 | MINOR | AC-GAP | yes | D10 and `TSource` messages wrong for the shapes routed to them | Separated-`ThenBy` and covariant-mismatch message variants; rows assert the distinctive text; mutation row. **Revised in rev 6:** the separated-`ThenBy` message was removed as unreachable (P3); the covariant message was split into entity and scalar variants (R5-6). |
| F3 | MINOR | TEST-GAP | yes | No `ThenBy`-after-paging rows; D8/D10 precedence unpinned | Rows added (with and without an earlier ordering; combined `OrderBy.Skip.OrderBy`/`ThenBy`). D8 is evaluated before D10 (§5.2.1). Mutation row. **Revised in rev 6:** the `ThenBy`-after-paging rows were removed because they can't be built (P3b); `OrderBy.Skip.OrderBy` is kept as the precedence killer. |
| F4 | MINOR | TEST-GAP | yes | Literal `LIMIT 2 OFFSET n` fails on PG/SQLite line breaks | §4.1: normalize whitespace before SQL-shape asserts. |
| F5 | MINOR | TEST-GAP | no | AC12-1/AC12-5 subset rows don't fix operator order | AC12-1/AC12-5 require ordering and paging before the projection. The §4.1 rule binds all passing shapes. |
| F6 | MINOR | PLAN-GAP | no | AC13-5 over-claims (D8 rejected the correct `Skip.Select.Take`); AC12-3 over-claims byte-identity | **D8 refined:** `Take` after `Skip` with only `Select`/identity `OfType` between is allowed, with new allowed rows and a mutation. AC13-5 restated (rejected shapes were all wrong in 3.9.0; Changelog enumerates them). AC12-3 narrowed to the ORDER BY translation. |
| F7 | MINOR | PLAN-GAP | yes | Stale name in §4.3; §5.2.4 `Id DESC` wording; §9.3 arithmetic; covariant rows mapped to Task 4 | All fixed. Covariant `Single`/`Last`/`LongCount` rows mapped to Tasks 5/7/8. |

Author's own check while applying rev 5: a drafted mutation row, "D8 allows `Take` after `Skip` with any
operator between", was **vacuous and was removed**. Any operator other than `Select`/`OfType` after `Skip` is
rejected at its own node first, so that path can't be reached.

### 9.5 Task 0 fix-verification r5 of `dfeae2e`

Totals: AC-GAP 1, TEST-GAP 7, HOUSE-RULE 0, PLAN-GAP 0, OTHER 0. All 8 were introduced by rev 5. Both majors
rested on unexecuted System.Linq premises: my §1.4 `Cast` claim, and r3 N2's `ThenBy` claim. **Process fix:**
§1.5 executed-probe evidence; every shape premise must cite it. The reviewer judged F1–F7 as 3 RESOLVED / 4
PARTIAL (F1, F2, F3, F6).

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| R5-1 | MAJOR | TEST-GAP | yes | `Queryable.Cast` always creates a node (§1.4 was false): scalar-cell rows unreachable as spelled; AC13-4 vs D5 conflict; D8 over-rejects identity `Cast` | §1.4 corrected with P1/P2/P2b. **Owner amended D5:** reference-conversion `Cast` is transparent, judged on the effective element type; generalized `isCollection` (P14 fix). Covariance rows spelled both ways; killers re-pointed. D8 lists include D5 casts. |
| R5-2 | MAJOR | TEST-GAP | yes | `ThenBy*` after any non-ordering call throws `ArgumentException` in System.Linq: D10 clause 2, its message, 5 rows and 2 mutations unreachable | Executed (P3, P3b, P4, P4b). D10 clause 2, the message, the rows and the mutations were deleted. `ThenBy_OnRoot_IsPrimaryOrder` re-spelled on the root. AC13-12 helper sentence fixed. The helper crash is filed as #16 (owner: follow-up). §9.3 N2 marked superseded. |
| R5-3 | MINOR | TEST-GAP | yes | The second-layer guard can't be reached through `Queryable`; its test was vacuous | Moved into the Core public `ScalarProjectionGuard`, tested directly (throw and pass cases). §4.3 row added. The plan states the layer is unreachable via LINQ. |
| R5-4 | MINOR | TEST-GAP | yes | Rules reject some shapes 3.9.0 answered correctly; AC13-5 / "holds again" over-claimed | AC13-5 restated: rejection is by rule, the Changelog lists the examples. Scalar covariant `Skip`/`Take`/`Distinct` are **kept allowed** after executing P7c/P7d. "Holds again" dropped. |
| R5-5 | MINOR | TEST-GAP | yes | Covariance after paging untested; rule order unpinned; root element type needs a second pass | AC13-4: "after `Skip`/`Take`, D8 governs". `Covariant_EntitySource_AfterPaging_D8Governs` rows. Pass-2 order pinned (allow-list → D8 → D10 → D5/covariance). Two-pass walk specified. Mutation row. |
| R5-6 | MINOR | AC-GAP | yes | Covariant message gives wrong advice for scalar/other sources; no "other projected type" row | Entity and scalar message variants; `Cast<…>` dropped from the advice; "other projected type" defers to the `Select`-shape guard, with a row. |
| R5-7 | MINOR | TEST-GAP | yes | AC12-3 still over-claimed | Exceptions now include AC13-2 and AC13-12; scoped to `OrderBy*/ThenBy*` chains consumed by enumeration or `First*`. |
| R5-8 | MINOR | TEST-GAP | yes | §4.2 table broken by a blank line | The AC13-4 cell is one line with `<br>`. |

**Also from r5 (not counted):**
- The spine root must be the queryable's own root (adopted: `ForeignQueryableConstantRoot_Rejected`).
- F5 residual: AC12-6 and the `Last`/`Distinct` tests are spelled ordering-first (adopted).
- F7 residual: Task 4 mapping says "entity-source" (adopted).

**Author's own probes while applying rev 6** (cited in §1.5): P7c–P7f corrected the scalar covariance cell
before it was written; P14 showed that the owner's "like implicit conversion" choice needs the `isCollection`
generalization to hold for enumeration.

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
