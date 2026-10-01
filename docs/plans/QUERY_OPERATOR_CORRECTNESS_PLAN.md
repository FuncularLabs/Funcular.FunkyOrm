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
> - Task 0 is waiting on a clean fix-verification of this revision (r11, scoped to the rev-11 diff). Owner
>   instruction: once clean, post the ACs and start Task 1.

> **Revision 11 (Task 0 fix-verification r10, 2026-09-30) — what changed:** r10 found 1 minor issue
> (R10-1) plus 6 nits at `830b707`, all text. It said "ACs safe to post: yes; Task 1 blocked: no".
> Disposition is in §9.10.
> - **R10-1:** the r9 premises that rev 10 cited as bare "r9" are now recorded in a §1.5 r9/r10 table
>   (`r9-I1`, `r9-I2`, `r9-L1`, `r9-C1`, `r10-S1`, `r10-L1`). The SQL Server `single_probe` identity
>   insert and SQLite `Query` are added to "still unexecuted".
> - **Nits:** the interface wording now separates "lowercased column name" from the get-only `Select`
>   rejection (N1); the matrix rationale (N2); the evidence ranges (N3); the stale legend (N4); the
>   "therefore" (N5); and the AC13-6 example now uses `IHasPersonId` (N6).

> **Revision 10 (Task 0 fix-verification r9, 2026-09-30) — what changed:** r9 found 4 minor issues at
> `8d5ac7a`, all text or test-spec. Disposition is in §9.9.
> - **F1:** the interface wording is now "unchanged", not "correct". A member resolves to its lowercased
>   name: `Id`/`Gender` work; others fail loudly (invalid column, or the `Select`-shape exception for
>   get-only members). Fixed in three places.
> - **F2:** `single_probe` uses an identity key per provider, because INSERT omits `int` keys. SQLite needs
>   `INTEGER PRIMARY KEY` exactly.
> - **F3:** the `Cast` rows are spelled per provider (`IHasPersonId`, `PersonBase` on MySQL), and
>   `IHasPersonId` is trimmed to `Id`.
> - **F4:** r8 probe IDs renamed (`r8-PG1`/`r8-MY1`/`r8-MY3`/`r8-SL1`), and AC citations prefixed `r7`.

> **Revision 9 (Task 0 fix-verification r8, 2026-09-30) — what changed:**
> The r8 reviewer found 9 issues at `3091544`: 0 blocker, 1 major, 8 minor. All are text or test-spec fixes;
> no redesign, no owner decision. Disposition is in §9.8.
> - **#1 (major):** the "interface source works" claim held only for `Id`; other interface members hit a
>   pre-existing "invalid column" bug (J21, S8/S9). Narrowed, and the bug is recorded in §8.
> - **#3:** `Single*(pred)` over a converted source silently returns an unrelated row in 3.9.0 rather than
>   throwing. Reworded. These were the two AC13-4 sentences to fix before the ACs are posted.
> - **#2:** "left alone" rows compare with the concrete query, not with in-memory LINQ, because `Average`
>   differs.
> - **#4:** I1 rows for `Last*`/`FirstOrDefault`/`SingleOrDefault(pred)`; predicate operators for the
>   composition row.
> - **#5:** an `IHasPersonId` interface, and MySQL `PersonBase`.
> - **#7:** the provider-neutral "no ORDER BY" assert is the SQLite killer.
> - **#8:** non-generic `Execute` unwraps `TargetInvocationException`.
> - **#9:** ‡ excludes subset-`Select` `Distinct` → `Last`.
> - **#6, superseded by the author:** the reviewer proposed recreating the local `dbo.[User]`. But
>   `DocumentationGapTests` **drops and recreates `[User]` with an `Id` column on every setup**, so `User`
>   can't be the no-`id` entity on SQL Server at all. Rev 8's "verified" claim had only read the scripts. The
>   AC13-1 no-`id` test now owns its own `single_probe` table (§4.1), with no schema-script changes.

> **Revision 8 (Task 0 fix-verification r7, 2026-09-30) — what changed:**
> The r7 reviewer found 6 issues at `765792a`: 0 blocker, 1 major, 5 minor. Blame: AC-GAP 1, TEST-GAP 3,
> HOUSE-RULE 1, PLAN-GAP 1. Disposition is in §9.7. **No silent wrong result** got past I1–I3.
> - **Major (#1):** rev 7's "every lambda over a converted source fails in 3.9.0" was false. My I1 probes
>   only covered `object`. Over a **base-class/interface** source, `OrderBy*`, selector aggregates and scalar
>   `Select` are correct today (r7 J1–J20, executed). **Owner decision: reject only where 3.9.0 fails.**
>   I1 now covers only the predicate operators that hard-cast to `Func<T,…>` (`Where`; `First*`/`Single*`/
>   `Last*`/`Any`/`All`/`Count`/`LongCount` with a predicate); everything else is left alone.
> - **#5 (house rule):** the non-generic `IQueryProvider.Execute` returns the whole list for a `First`
>   (K1). It now dispatches by expression shape (I2), as new **AC13-15**.
> - **#2:** I1 defers to the parse loop after any non-subset `Select`. The scalar/subset predicates are
>   identical to the parse loop's.
> - **#3:** ‡ defined per operator.
> - **#4:** the composition-message assertion strengthened.
> - **#6:** type-filter wording.
> - **Task 1 simplification (author, verified):** all four schemas already have a `User` table with no `id`
>   column, so the no-`id` entity needs **no schema changes**.

> **Revision 7 (Task 0 fix-verification r6, 2026-09-30) — what changed:**
> The r6 reviewer found 8 issues at `44e78b4`: 0 blocker, 1 major, 7 minor. Blame: AC-GAP 1, TEST-GAP 7.
> Disposition is in §9.6.
> - **The major (F1) was the 4th consecutive round of fix-introduced findings on the covariance rule.** A
>   converted scalar source followed by an allowed `Skip`/`Take`/`Distinct` and then a terminal slipped past
>   the rev 6 "effective element type" check, and 3.9.0 returns the whole list there (executed, Q1a–e).
> - **Redesign (not a patch), per the house rule.** The per-node covariance type-flow and cell table are
>   **removed**. Three local invariants (§5.2.1) replace them, each premise executed:
>   - **I1:** every lambda must bind to the entity type. Every violating shape fails in 3.9.0 (I1a–h).
>   - **I2:** collection vs terminal is decided by expression shape. It closes the scalar "whole list" class
>     on every path and fixes P14/F3.
>   - **I3:** `Cast`/`OfType` are judged against a row type `R` that only a scalar `Select` changes.
>
>   Nothing tracks a conversion through the chain any more, so there's nothing left to lose.
> - **Also:**
>   - entity covariant `Skip`/`Take`/`Distinct` are allowed, as they're correct in 3.9.0 (F2);
>   - `TResult`-free empty-`Take` (F3);
>   - base-class and interface `Cast` rows, judged against `R` (F4);
>   - scalar `Cast` after paging (F5);
>   - cross-pass precedence stated and pinned (F6);
>   - `ThenBy_OnRoot` uses `ThenByDescending` (F7);
>   - stale text and bookkeeping (F8).
> - `OfType` stays identity-only (the owner's amendment covered `Cast` only).

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
  - Over an **entity** source, in 3.9.0:
    - `Count`/`Any`/`First*` are correct (P5, P5b, P13, P13b, r6 Q9a/b).
    - `LongCount`/`Single*`/`Last*` are broken for every source (§1.1).
    - Lambda-free sequence operators followed by a terminal are correct (r6 Q3a–e).
    - **Predicate** lambdas fail:
      - `Where`, `First*`/`Last*`/`Any`/`All`/`Count` with a predicate throw `InvalidCastException`
        (§1.5 I1a/b/d/e/f/g, J5/J6/J14/J15, S4–S7b);
      - `Single*(pred)` **silently returns an unrelated row**, because the predicate is dropped (S1–S3, the
        §1.1 class).
    - **Non-predicate** lambdas (`OrderBy*`, selector aggregates, scalar `Select`):
      - over a **base-class** source, they return the same results as the concrete query (r7 J1–J4, J7, J9,
        J10, J12, J13, J16–J18);
      - over an **interface** source, a member resolves to its **lowercased property name** (pre-existing,
        §8). In `OrderBy*` and selector aggregates it works where the column has that name, e.g. `Id`,
        `Gender` (J19/J20, r9-I1, r10-S1). Otherwise the database reports "invalid column" (J21, r8 S8/S9).
        Separately, a scalar `Select` of a **get-only** member is rejected by the `Select`-shape guard
        (r9-I2). 3.10.0 doesn't change this;
      - over `object`, an `OrderBy` on a cast member works (J8), and an unsupported body fails cleanly (I1c).
  - Over a **scalar** source, a row-returning terminal returns the whole projected list. That happens directly
    (P6, P7) and after covariant `Skip`/`Take`/`Distinct` (r6 Q1a–e): a silent wrong result.
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

**Author probes for I1 (rev 7, SQL Server)**: lambdas over an entity source converted to **`object`**
(`o` = `IQueryable<object>` of the entity). *Rev 8 correction:* these only covered `object` and the hard-cast
operators. The r7 J-probes below show that non-predicate lambdas over a **base-class or interface** source
work, so I1 was narrowed.

| # | Shape | 3.9.0 result |
|---|---|---|
| I1a | `o.Where(x => x != null).Take(2).ToList()` (no node) | `InvalidCastException` (`Func<Object,Boolean>` → `Func<PersonEntity,Boolean>`) |
| I1b | `q.Cast<object>().Where(x => x != null)…` | `InvalidCastException` |
| I1c | `o.OrderBy(x => x.GetHashCode()).First()` | `NotSupportedException` (OrderBy visitor: unsupported expression) |
| I1d / I1e / I1f | `o.Any(pred)` / `o.Count(pred)` / `o.First(pred)` | `InvalidCastException` (all three) |
| I1g | `IQueryable<object> s = q.Select(new T { FirstName }); s.Where(pred)` | `InvalidCastException` |
| I1h | `o.Select(x => x.ToString())` | `NotSupportedException` (`Select`-shape guard) |
| I1i | `PersonEntity` implements `INotifyPropertyChanged` | true (usable for interface-cast rows) |

**r6 reviewer probes (SQL Server, executed, cited by ID):**

| ID | Shape | 3.9.0 result |
|---|---|---|
| Q1a–Q1e | scalar `Select(FirstName)` converted (no node or `Cast<object>`), then `Take(3).First()`, `Distinct().FirstOrDefault()`, `Skip(1).Take(2).Single()`, `Distinct().Last()` | **Whole list returned** as the element (3 / 1,503 / 2 / 1,503 items) |
| Q2 | `TSource` of a `First<object>` after `Take<object>` equals its source's element type (`object`) | So a per-node `TSource` check can't see the conversion after one sequence operator (why rev 7 drops it) |
| Q3a–Q3e | entity converted, then `Take(5).First()`, `Skip(1).First()`, `Cast<object>().Distinct().First()` | Correct. Enumeration (Q3d) throws `InvalidCastException` (P14 class) |
| Q4a / Q4b | `Cast<object>().Cast<T>().ToList()` / `Cast<object>().OfType<T>().ToList()` | Correct |
| Q4c / Q4d | `Cast<BaseClass>().ToList()` / `Cast<INotifyPropertyChanged>().Count()` | `InvalidCastException` / correct (3) |
| Q5a–Q5c | `OfType` creates a node; 3.9.0 ignores it and keeps nulls | 8,546 rows vs 5,047 in-memory (null-dropping) |
| Q6 | root `((IQueryable)c.Value).Expression == c`; composed queryable and `List.AsQueryable()` fail it | as stated |
| Q7 | `OrderBy(Id).Skip(1).Take(3).Select(FirstName).Cast<object>().ToList()` | Correct |
| Q8 | `Select(FirstName).Cast<object>()` then `Skip`/`Take`/`Distinct`, enumerated | Correct |
| Q9a–Q9c | entity `Cast<object>().Any()`, no-node `FirstOrDefault()` / scalar `Cast<object>().Any()` | Correct / scalar-guard `NotSupported` |
| Q10a–Q10c | unordered ids `1..5`; root `ThenBy(Id)` → `1..5`; root `ThenByDescending(Id)` → `5..1` | `ThenBy(Id)` is indistinguishable from no order; use `ThenByDescending` |
| Q11 | boxing `Select(p => p.Id).Cast<object>()…` | `NotSupportedException` already |
| Q12 | rev 6's type-based `isCollection` rule, transcribed | Misclassifies `T : IEnumerable<object>` terminals (why I2 uses the expression shape) |

**r7 reviewer probes (SQL Server, executed, cited by ID).** Here `b` = `IQueryable<PersonEntity>` over
`db.Query<Person>()` (`Person : PersonEntity`), `d` = `IQueryable<PersonEntity>` over
`db.Query<PersonDetailEntity>()`, and `i` = an interface-typed source.

| ID | Shape | 3.9.0 result |
|---|---|---|
| J1 | `b.OrderByDescending(p => p.Id).First()` | Correct (max id) |
| J2, J3, J3b, J13 | `b.Max/Min/Sum/Average(p => p.Id)` | Correct |
| J4 | `b.Select(p => p.FirstName).ToList()` | Correct (sequence-equal to the concrete query) |
| J7 | `db.Query<Person>().Cast<PersonEntity>().OrderByDescending(Id).First()` | Correct |
| J8 | `o.OrderByDescending(x => ((PersonEntity)x).Id).First()` | Correct |
| J9 | `b.OrderBy(LastName).ThenByDescending(Id).Skip(1).First()` | Correct |
| J10, J12 | other base-typed `OrderBy`/aggregate shapes | Correct |
| J16–J18 | `d.OrderBy(…)` / `d.Max(…)` (join entity via base type) | Correct |
| J19, J20 | `i.Max(x => x.Id)` / `i.OrderByDescending(x => x.Id).First()` | Correct |
| J5, J6, J14, J15 | base-typed `Where` / predicate terminals | `InvalidCastException` |
| J11 | `object`-param subset `Select(new T { … })` | Throws |
| K1 | non-generic `IQueryProvider.Execute(<First expr>)` | **Whole `List<T>` (8,546) returned** as the "first" element |
| K2 | non-generic `Execute(<collection expr>)` | Correct |
| L1 | `Select(p => new Dto { … }).Where(d => …)` | `Select`-shape guard message |
| M1–M3 | entity/scalar `Distinct()` then `Count`/`Any` | "Distinct() combined with an aggregate" `NotSupported` |
| N1, N2 | `o.Distinct().Where(…)`, `q.Cast<object>().Distinct().Where(…)` | `InvalidCastException` |
| N3 | base-typed `Take(5).ToList()` (`IEnumerable<PersonEntity>` over `Person`) | `InvalidCastException` (P14 class; fixed by I2) |
| N4 | scalar-guard message text | Matches the 3.9.0 message the plan reuses |

**r8 reviewer probes (executed; S/X/K = SQL Server; the `r8-PG*`/`r8-MY*`/`r8-SL*` IDs = PostgreSQL / MySQL /
SQLite):**

| ID | Shape | 3.9.0 result |
|---|---|---|
| S1–S3 | base-class `Single(p => p.Id == max)`, `SingleOrDefault(p => p.Id == -1)`, `o.Single(x => x != null)` | **Row Id=1 returned**: predicate dropped (`SELECT … FROM person`), silent wrong row |
| S4–S6, S7b | base-class `Last(pred)`, `LastOrDefault(pred)`, `All(pred)`, `FirstOrDefault(pred)` | `InvalidCastException` |
| S7 | base-class `LongCount(pred)` | Throws (result cast) |
| S8, S9, S9b / J21 | interface `i.Select(x => x.FirstName)`, `i.OrderBy(x => x.LastName).First()`, J9-shaped chain | SqlException "Invalid column name 'firstname'/'lastname'": interface members resolve to the lowercased property name |
| S9c, S9d | interface `Max(x => x.Id)`, `OrderByDescending(x => x.Id).First()` | Correct (`id` happens to be the lowercased name) |
| S10 / r8-MY3 | base-class `Average(p => p.Id)` vs in-memory LINQ | 4340 vs 4340.40… (SQL Server); 1055.4961 vs 1055.49605… (MySQL): the D9 class, equal to the concrete query |
| K3, K4 | non-generic `Execute`, dispatched via `MakeGenericMethod`: `First` → `T`, `Count` → `int` | Correct |
| X1, K5 | `MethodInfo.Invoke` dispatch with `First` on an empty set | `TargetInvocationException` wrapping `InvalidOperationException` (direct call K5b: unwrapped) |
| live SQL Server `dbo.User` | columns `Id, Order, Key`; `ORDER BY id` succeeds | **Has `id`**: owned and recreated by `DocumentationGapTests` (DocumentationGapTests.cs:21-39) |
| r8-PG1, r8-MY1 | live PostgreSQL/MySQL `User` and `SELECT … ORDER BY id` | Match their scripts; `ORDER BY id` errors (42703 / "Unknown column 'id'") |
| r8-SL1 | SQLite `"User"` (INTEGER PRIMARY KEY) `ORDER BY rowid LIMIT 2` | Works (rowid alias) |
| incidental | same entity type used with SQL Server, then PostgreSQL, in one process | PostgreSQL emitted `FROM [User]` (42601): static identifier caches shared across providers (§8) |

**r9/r10 reviewer probes (executed unless marked "code read"):**

| ID | Shape | Result |
|---|---|---|
| r9-I1 | interface source, settable `Gender`: `Select(x => x.Gender)`, `OrderByDescending(x => x.Gender).First()` (SQL Server) | Correct: the lowercased name `gender` matches the column |
| r9-I2 | interface source, get-only `Id`: `Select(x => x.Id)` / `Max(x => x.Id)`, `OrderByDescending(x => x.Id).First()`, `Cast<IGetOnly>().Count()` (SQL Server) | `Select`-shape `NotSupportedException` (the scalar test requires `CanWrite`) / the other three equal the concrete query |
| r9-L1 | SQLite `INT PRIMARY KEY` vs `INTEGER PRIMARY KEY`, key omitted on INSERT | `INT`: key stored NULL (not a rowid alias). `INTEGER`: rowid alias, keys 1..n |
| r9-C1 | *(code read)* INSERT column list in all four dialects | Omits an `int`/`long` key: SqlServerDialect.cs:46-58, PostgreSqlDialect.cs:60-72, MySqlDialect.cs:86-98, SqliteDialect.cs:62-74 |
| r10-S1 | SQL Server interface source: `Max(x => x.Id)`, `OrderByDescending(x => x.Id).First()`, `Select`/`OrderBy` on `Gender`, `OrderBy(x => x.LastName)` | 8866 / 8866 / correct / `SqlException` "Invalid column name 'lastname'" |
| r10-L1 | SQLite temp DB mimicking FunkyORM's insert (`last_insert_rowid()`); `ORDER BY rowid` / `ORDER BY id` | `INTEGER PRIMARY KEY`: Get-by-key works; `INT PRIMARY KEY`: Get-by-key finds nothing. `ORDER BY rowid` runs on both; `ORDER BY id`: "no such column" |

**Still unexecuted** (Task 1 red tests):
- the new 3.10 behaviors themselves;
- the net48/net9 rendering of `MethodInfo` signatures for the literal set;
- the r6–r10 probes on the providers not listed above (the paths are structurally identical in source);
- `Average` vs in-memory LINQ on PostgreSQL and SQLite (it doesn't gate 3.10.0, because the
  `UnchangedFromConcrete` rows compare with the concrete query, not with in-memory LINQ);
- `single_probe` identity Insert/Get/Query on **SQL Server** (no existing coverage) and `Query` on **SQLite**
  (existing coverage is Insert/Get only); PostgreSQL identity and MySQL `AUTO_INCREMENT` inserts are
  code-read only (r10).

---

## 2. Decisions (all made by the owner, 2026-09-30)

| # | Decision | Outcome |
|---|---|---|
| **D1** | `Single`/`SingleOrDefault`/`Last`/`LastOrDefault`/`LongCount` | **Implement.** `Single*` uses a row limit, not the paging path (§5.2.3). |
| **D2** | Allow-list form | **Exact-overload allow-list in Core** (`QueryOperatorPolicy`), run as a **pre-pass over the method spine** before translation. It guarantees that no query or aggregate command runs for a rejected chain; schema discovery at `Query<T>()` is unaffected. |
| **D3** | Version / branch | **3.10.0** on `development/3.10`; beta first, stable after a Sentinel call-list smoke test. |
| **D4** | Local PostgreSQL | Native PostgreSQL 18 via the suite's fallback connection; CI uses `postgres:17`. |
| **D5** | `Cast`/`OfType` | **Identity**, plus *(owner amendment, rev 6)* **reference-conversion `Cast`**. Since rev 7 both are judged against the **row type `R`** (§5.2.1): the entity `T`, or the member type after a scalar `Select`.<br>• `Cast<X>` with `X == R`: a no-op.<br>• `Cast<TBase>` where `R` is a **reference type assignable to `TBase`** (e.g. `object`, an interface, a base class): **transparent**, like the implicit conversion `IQueryable<TBase> b = q` (P2b, P5). Later operators are governed by invariants I1/I2. A second cast back, such as `Cast<object>().Cast<T>()`, is judged against `R` too, so it's allowed (r6 Q4a).<br>• `OfType<X>`: a no-op only when `X == R` and `R` is the entity `T` (rows are never null) or a non-nullable value type.<br>• Everything else is rejected: boxing `Cast` (`Select(p => p.Id).Cast<object>()`, already `NotSupported` in 3.9.0, r6 Q11), unrelated-type `Cast`, non-identity `OfType`, and `OfType` over a nullable or reference scalar (it would have to drop nulls; the message says to use `Where(x => x.M != null)` before the projection). |
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
  - non-call, non-root nodes.

  **Covariance follows the three §5.2.1 invariants (rev 7).** A conversion (the no-node
  `IQueryable<TBase> b = q`, or a reference-conversion `Cast<TBase>()`) never changes the row type `R`.
  - **I1 (rev 8: narrowed by the owner).** A **predicate** lambda (`Where`; `First*`/`Single*`/`Last*`/
    `Any`/`All`/`Count`/`LongCount` with a predicate) whose parameter isn't `T`, and which isn't outer to a
    non-subset `Select`, is rejected before any query with the I1 message. Every such shape fails in 3.9.0:
    - `Where`, `First*`/`Last*`/`Any`/`All`/`Count` with a predicate throw `InvalidCastException` (§1.5
      I1a/b/d/e/f/g, J5/J6/J14/J15, N1/N2, S4–S6, S7b);
    - `LongCount(pred)` throws on its result cast (S7);
    - `Single*(pred)` **silently returns an unrelated row** (S1–S3).
  - After a scalar `Select`, the existing composition guard rejects all lambdas with its own message
    (AC13-8). After any other non-subset `Select`, the `Select`-shape guard rejects the `Select` (r7 L1, I1h).
  - **I2.**
    - Over a scalar projection, every terminal **that reaches execution** is rejected with the scalar-guard
      message. That covers terminals reached directly (P6, P7) or after a covariant `Distinct` (Q1c/Q1e)
      or after `Skip`/`Take` where D8 allows the terminal (`First*`/`Single*`; Q1a/b/d). In 3.9.0 those
      return the whole list.
    - Over an entity source, enumeration of a converted source returns the entity rows. This fixes P14 and
      base-typed paging (N3).
  - **Left alone** (correct in 3.9.0, and still correct):
    - parameterless terminals over a converted entity source (`Count`/`Any`/`First*`; P5, P13, Q9);
      `LongCount`/`Single*`/`Last*` get the AC13-1/2/3 semantics;
    - lambda-free `Skip`/`Take`/`Distinct` over any converted source (Q3a–e, P7b–P7e);
    - enumeration of a converted scalar source (P7e);
    - **non-predicate lambdas** (`OrderBy*`/`ThenBy*`, selector aggregates, scalar `Select`) over a
      **base-class** source: unchanged from the concrete query (J1–J4, J7, J9, J10, J12, J13, J16–J18).

    **Unchanged, not "correct":** over an **interface** source, a member resolves to its lowercased
    property name (pre-existing, §8). In `OrderBy*` and selector aggregates it works where the column has
    that name, e.g. `Id`, `Gender` (J19/J20, r9-I1, r10-S1). Otherwise the database reports "invalid column"
    (J21, r8 S8/S9). A scalar `Select` of a get-only member is rejected by the `Select`-shape guard (r9-I2).
    3.10.0 doesn't change this.

    After `Skip`/`Take`, D8 still governs (e.g. covariant `Count` after `Take` gets the D8 message). After
    `Distinct`, the existing "Distinct() combined with an aggregate" guard governs `Count`/`Any` (r7 M1–M3).

  Lambdas nested inside allowed operators aren't inspected, so `Where(p => ids.Contains(p.Id))` still works.
  The rejection table covers, at minimum:
  - **Ordering and slicing:** `Reverse`, `TakeWhile`, `SkipWhile`, `TakeLast`, `SkipLast`, `ElementAt`,
    `ElementAtOrDefault`, `Order`, `OrderDescending`.
  - **Set, join and sequence:** `DefaultIfEmpty`, `Concat`, `Union`, `Intersect`, `Except`, `Zip`,
    `SelectMany`, `Join`, `GroupJoin`, `Append`, `Prepend`, `SequenceEqual`, `Chunk`.
  - **Reducing:** `Contains`, `Aggregate`, `DistinctBy`, `MinBy`, `MaxBy`, and parameterless `Min()`/`Max()`
    on an entity.
  - **Type filters:** `Cast`/`OfType` outside D5/I3: boxing or unrelated-type `Cast`; non-identity `OfType`;
    `OfType` over a nullable or reference scalar.
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
- **AC13-6** *(D5/I3: judged against the row type `R`)*
  - Identity `OfType<R>()` is a no-op on the entity (at the root, after `OrderBy`, after `Cast<object>()`) and
    on a non-nullable scalar (`Select(p => p.Id).OfType<int>()`).
  - Identity `Cast<R>()` is a no-op, including back to the entity after a transparent cast
    (`Cast<object>().Cast<T>()`, r6 Q4a).
  - A reference-conversion `Cast<TBase>()` is transparent for `TBase` = `object`, an interface, or a base
    class. `q.Cast<object>().Count()`/`.First()` and a cast to an implemented interface (e.g.
    `Cast<IHasPersonId>().Count()`) match 3.9.0
    (P5, P5b, Q4d), and later operators follow AC13-4.
  - **Enumerating** a reference-conversion `Cast` over the entity (`q.Where(…).Cast<object>().ToList()`, or to
    a base class) returns the entity rows, the same as the implicit conversion (P14b). In 3.9.0 it throws
    `InvalidCastException` (P14, Q4c); fixed by I2.
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
  - **pre-pass 1**: the allow-list over the whole spine, outer→inner (the outermost failure wins; `GroupBy`
    is special-cased);
  - **pre-pass 2**: inner→outer, per node, D8 → D10 → D5/I3 → I1;
  - then parse-loop guards;
  - then execute-time guards (I2, `ScalarProjectionGuard`).

  For example, `q.Take(5).Where(w).Reverse()` gets the `Reverse` allow-list message, not D8's.
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
    - an allowed D5 `Cast`/`OfType` (judged against `R`: e.g. `Skip.Take.Select(p => p.FirstName).Cast<object>()`
      enumerated is correct, r6 Q7).
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
- **AC13-15** *(I2, rev 8)* The non-generic `IQueryProvider.Execute(expression)` returns the single
  element (or throws, as LINQ would) for a terminal expression such as `First`, and the list for a
  collection expression. In 3.9.0 it returns the whole list for `First` (r7 K1).

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
  `.Cast<object>()` (a node, P2). They also include the "**after an allowed sequence operator**" variant
  (`…Take(3).First()`; r6 Q1), since that's where rev 6's design leaked.
- **Shape premises cite §1.5.** A test whose premise about System.Linq or a database hasn't been executed is
  flagged UNVERIFIED in the matrix and must go red for the right reason in Task 1.
- **SQL capture.** For "nothing executed": obtain the `IQueryable`, clear `Log`'s builder, run the shape,
  then assert the builder is empty. **SQL-shape asserts normalize whitespace first** (collapse `\s+` to one
  space), because PostgreSQL and SQLite emit `LIMIT`/`OFFSET` on separate lines and MySQL on one.
- **Test entities.**
  - Join entity: `PersonDetailEntity` (SQL Server/PG/SQLite) or `PersonWithEmployer` (MySQL); every joined
    table has an `id` column.
  - **No-`id` entity: a test-owned table (rev 9; replaces rev 8's `User` choice).** Rev 8 picked `User`
    after reading the scripts only. Live, SQL Server's `DocumentationGapTests` **drops and recreates `[User]`
    with an `Id` column on every setup** (DocumentationGapTests.cs:21-39), and the local `dbo.User` already
    shows that shape (r8). So `User`'s columns depend on test order.
    - Instead, the AC13-1 no-`id` test class creates its own table in its own setup with idempotent DDL.
      `single_probe` has an **identity** key `single_probe_key` and a `name` column, and **no `id` column**.
      The identity is required because FunkyORM's INSERT omits an `int`/`long` key (SqlServerDialect.cs:46-58
      and the sibling dialects; r9-C1). Per provider:
      - SQL Server: `IF OBJECT_ID('single_probe') IS NULL CREATE TABLE single_probe (single_probe_key INT
        IDENTITY(1,1) PRIMARY KEY, name NVARCHAR(100) NULL)`;
      - PostgreSQL: `CREATE TABLE IF NOT EXISTS single_probe (single_probe_key INT GENERATED BY DEFAULT AS
        IDENTITY PRIMARY KEY, name VARCHAR(100))`;
      - MySQL: `CREATE TABLE IF NOT EXISTS single_probe (single_probe_key INT AUTO_INCREMENT PRIMARY KEY,
        name VARCHAR(100))`;
      - SQLite: `CREATE TABLE IF NOT EXISTS single_probe (single_probe_key INTEGER PRIMARY KEY, name TEXT)`.

      The entity maps the key with `[Key]`. Existing coverage of non-`id` `[Key]` identity entities:
      - MySQL: Insert/Get/Query (MySqlReservedWordTests.cs:24-45);
      - PostgreSQL: Insert/Get/Query (PostgreSqlDocumentationGapTests.cs:24-37);
      - SQLite: Insert/Get only (SqliteDataProviderIntegrationTests.cs:636-640);
      - SQL Server: none.

      SQL Server, and SQLite `Query`, are confirmed by Task 1's red run (listed in §1.5's "still
      unexecuted").
    - This follows the same pattern as `DocumentationGapTests` and the SQLite suites. No schema-script
      changes; independent of local drift and test order.
    - On SQLite the key must be spelled exactly `INTEGER PRIMARY KEY`. That makes it a rowid alias that
      receives the generated key; `INT PRIMARY KEY` doesn't, and stores NULL (r9-L1, r10-L1).
    - Separately, the paging mutation's `ORDER BY rowid` runs on any rowid table (r10-L1). So on SQLite the
      §4.4 row's SQL-shape assert carries the kill, alongside `Single_AfterDistinctProjection_Works`.
  - **Base-class and interface sources (r8 #5).** Task 1 adds these to the test domains:
    - In all four test projects, an interface `IHasPersonId { int Id { get; } }`, implemented by the person
      entity. This is for the interface rows, which are limited to `Id`; a marker-only interface can't
      support `p => p.Id`.
    - In MySQL, which has no person base class, a `PersonBase { Id, FirstName, LastName }` as the base of
      `Person` and `PersonWithEmployer`, so `IQueryable<PersonBase>` over `Query<PersonWithEmployer>()` is
      the MySQL base-class source.
    - The other three use `IQueryable<PersonEntity>` over `Query<PersonDetailEntity>()`.
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
| AC13-4 | `[DataTestMethod] Rejected_Operator_ThrowsNotSupported_NamesOperator_NoQueryExecuted` (one row per AC13-4 allow-list shape, including `ScalarProjection_ParameterlessSum`; covariant shapes are in the invariant rows below — note `((IQueryable<object>)q).Take(5)` is now **allowed**, F2), `Allowed_PredicateWithCollectionContains_NotRejected`, `NonQueryableSpineMethod_Rejected`, `NonCallNonRootSpineNode_Rejected` (DB-free, hand-built `Convert` node), `ForeignQueryableConstantRoot_Rejected` (DB-free: a constant whose value is a composed queryable)<br>**Covariance invariants (§5.2.1, rev 7).** Each row is spelled both ways (no-node `IQueryable<object> o = …` and `.Cast<object>()`) and, where marked ‡, also "after an allowed sequence operator". **‡ is defined per operator (rev 8):** after `Take`/`Skip`, only `First*`/`Single*` (D8 rejects the rest; those cells are D8 rows); after `Distinct`, only `First*`/`Single*`/`Last*` (`Count`/`Any` hit the existing Distinct+aggregate guard, r7 M1–M3, which is a regression row), **except `Last*` after `Distinct` at the subset-`Select` position**. AC13-2 makes that shape throw, naming `Last`, and it's covered by `Last_AfterDistinctProjection_NoOrder_ThrowsNamingLast` (r8 #9).<br>• **I1 (predicate lambdas):** `Covariant_PredicateLambda_Rejected_I1Message_NoQuery` over {`Where`, `First(pred)`, `FirstOrDefault(pred)`, `Single(pred)`, `SingleOrDefault(pred)`, `Last(pred)`, `LastOrDefault(pred)`, `Any(pred)`, `All`, `Count(pred)`, `LongCount(pred)`, subset-`Select`-then-`Where`, **`Distinct()`-then-`Where`**} × {`object` source, **base-class source** (`IQueryable<PersonEntity>` over `Query<PersonDetailEntity>()`; MySQL: `IQueryable<PersonBase>` over `Query<PersonWithEmployer>()`)}, asserting the I1 message. Red on 3.9.0: `InvalidCastException` for the hard-cast operators (I1a/b/d/e/f/g, J5/J6/J14/J15, N1/N2, S4–S7b); a **silently unrelated row** for `Single*(pred)` (S1–S3). The `Distinct` row separates a lambda-parameter check from a `TSource` check.<br>• **I1 out of scope, left alone:** `Covariant_BaseClassSource_NonPredicateLambda_UnchangedFromConcrete`, comparing each shape with **the same shape over the concrete, unconverted query** (not the in-memory oracle, so it's independent of the D9 aggregate issues; `Average` differs from in-memory LINQ today, r8 S10/r8-MY3). Over {`OrderBy`→First, `OrderByDescending`→First, `OrderBy.ThenByDescending.Skip(1).First()`, `Max`/`Min`/`Sum`/`Average`(selector), scalar `Select` enumerated} × base class (J1–J4, J9, J16–J18), and over {`OrderByDescending(x => x.Id).First()`, `Max(x => x.Id)`} × interface `IHasPersonId` (J19/J20; `Id` only, since `IHasPersonId` exposes only `Id`. Interface behavior for other members depends on column naming, §8), plus `o.OrderByDescending(x => ((PersonEntity)x).Id).First()` (J8). Green on 3.9.0 and must stay green.<br>• `Covariant_BaseClassSource_PagedEnumerated_MatchesOracle` (`b.Take(5).ToList()`; red on 3.9.0, N3; fixed by I2).<br>• `Covariant_ScalarSource_Lambda_KeepsCompositionMessage` over {`Where`, `Count(pred)`}: asserts the substring **"must be the outermost query operator"**, on both converted and non-converted shapes. Only predicate operators can kill "I1 applied outer to a scalar Select", because I1 doesn't cover `OrderBy`.<br>• **I2 (scalar terminals):** `Covariant_ScalarSource_Terminal_Rejected_NoQuery` ‡ over {First, FirstOrDefault, Single, SingleOrDefault, Last, LastOrDefault}, asserting the scalar-guard message. Red on 3.9.0: the whole list is returned (P6, P7, Q1a–e). `Covariant_ScalarSource_CountAny_Regression` (non-‡; **green** on 3.9.0 with the same message, P7f/Q9c/N4: a regression row, not red). `ScalarDistinctLast_ScalarGuardWins` (`s.Distinct().Last()`: the Last/Distinct check lives in `BuildQueryComponents`, which runs after `ScalarProjectionGuard`, so the scalar-guard message wins).<br>• **I2 (entity enumeration):** `Covariant_EntitySource_Enumerated_MatchesOracle` ‡ (P14, Q3d red).<br>• **Left alone:** `Covariant_EntitySource_ParameterlessTerminal_MatchesOracle` ‡ over {Count, LongCount, Any, First, FirstOrDefault, Single (pre-filtered to one row), Last}, at the root, after `Where`/`OrderBy`/subset `Select` (Q3, Q9, P5, P13); `Covariant_ScalarSource_SequenceOrEnumeration_MatchesOracle` over {`Skip.Take`, `Take`, `Distinct`, `Cast<object>()` enumerated} (P7b–e, Q8).<br>• **D8 interplay:** `Covariant_EntitySource_AfterPaging_D8Governs` over {`Skip(1)`→First: allowed; `Take(5)`→Count: D8 message; `Take(5)`→Where: D8 message (D8 precedes I1)}.<br>• `Covariant_OtherProjectedSource_SelectShapeGuardMessage`: `q.Select(p => new Dto { … }).Where(…)` gets the `Select`-shape message, not I1's (r7 L1).<br>**`ScalarProjectionGuard`**, DB-free and direct: `ScalarProjectionGuard_Terminal_Throws` (object, string, `First<object>(…)`), `ScalarProjectionGuard_Collection_Passes` (`IEnumerable<object>`, string, `Select(…)`). It's also pinned end to end by the I2 rows above. | all 4 + Core |
| AC13-5 | `[DataTestMethod] Allowed_Operator_MatchesOracle` (one row per allowed family, with its expected outcome); the four existing suites | all 4 |
| AC13-6 | `OfType_Identity_AtRoot_IsNoOp`, `OfType_Identity_AfterOrderBy_IsNoOp`, `OfType_Identity_AfterScalarProjection_NonNullable_IsNoOp`, `OfType_Identity_OverNullableScalar_Rejected` (seeded nulls), `OfType_NonIdentity_Throws`, `Cast_Identity_IsNoOp` (`q.Cast<PersonEntity>().ToList()`; P1 shows it's a real node), `Cast_ReferenceConversion_Count_MatchesOracle` and `Cast_ReferenceConversion_OrderedFirst_MatchesOracle` (P5, P5b), `Cast_ReferenceConversion_Enumerated_MatchesOracle` (`q.Where(marker).Cast<object>().ToList()`; red on 3.9.0, P14), `Cast_ReferenceConversion_BaseClass_Enumerated_MatchesOracle` (per provider: `Query<PersonDetailEntity>().Cast<PersonEntity>()` on SQL Server/PG/SQLite, `Query<PersonWithEmployer>().Cast<PersonBase>()` on MySQL; red on 3.9.0, the Q4c/P14 class), `Cast_ReferenceConversion_Interface_Count_MatchesOracle` (`Cast<IHasPersonId>()` on all four, the Q4d shape; `IHasPersonId` is added to all four in Task 1), `Cast_BackToEntityAfterTransparentCast_IsNoOp` (Q4a), `OfType_Entity_AfterTransparentCast_IsNoOp` (Q4b), `Cast_Boxing_Rejected` (`Select(p => p.Id).Cast<object>()`), `Cast_NonIdentity_UnrelatedType_Throws` (`Cast<AddressEntity>()` on a person query) | all 4 + Core |
| AC13-7 | `SupportedOperators_ExactLiteralSetPinned`, `ClassifierSweep_EveryQueryableMethod_MatchesLiteralSet`, `NonQueryableOverload_IsRejected` (MSTest: `SqlServer.Tests` net8, `SqlServer.Tests.NetFramework` net48); xUnit twin `QueryOperatorPolicyLiteralSetTests` (`SqlServer.Tests.DotNet9` net9) | Core, 3 runtimes |
| AC13-8 | `GroupBy_Rejected_KeepsDedicatedMessage`; existing scalar tests; new in the siblings: `ScalarProjection_WithReducingTerminals_ThrowNotSupported`; `ScalarProjection_WithSingleOrLast_ThrowsNotSupported`; `Rejected_OperatorOuterToFailingInnerOperator_PolicyMessageWins` (`Select(p => p.Id).Where(x => x > 0).Reverse()`); `Rejected_AllowListFailureBeatsPass2Failure` (`q.Take(5).Where(w).Reverse()` → the `Reverse` message, not D8's) | all 4 |
| AC13-9 | `OperatorDocTable_MatchesSupportedOperators` (reads the table from both docs); prose reviewed in the gauntlet | SqlServer.Tests |
| AC13-10 | `[DataTestMethod] Operator_AfterPaging_Rejected_BeforeAnyQuery` over {Count, LongCount, Any, All, Sum, Average, Min, Max, Where, First(pred), Single(pred), Last, OrderBy, OrderByDescending, OrderBy(a).Skip(n).OrderBy(b) [D8 wins over D10], Distinct, Skip-after-Skip, Take-after-Take, Take-then-Skip} — each row asserts the "after Skip/Take" message; `[DataTestMethod] Operator_AfterPaging_Allowed_MatchesOracle` over {Skip.Take, `Skip.Select(subset).Take`, `Skip.Select(scalar).Take`, `Skip.OfType<T>().Take`, `Skip.Cast<T>().Take`, `Take.Cast<object>()` enumerated, `Skip.Take.Select(p => p.FirstName).Cast<object>()` enumerated (Q7), Select subset, Select scalar, First(), FirstOrDefault(), Single(), SingleOrDefault(), OfType-identity entity, `Skip(n).Select(p => p.Id).OfType<int>()`, Skip-only.First()}; `[DataTestMethod] TakeNonPositive_ReturnsEmpty_NoQuery` over {Take(0) full, Take(0) subset, Take(0) scalar, Skip(2).Take(0), Take(-1), `Take(0).Cast<object>()` enumerated}; `Take0_First_Throws_NoQuery`, `Take0_FirstOrDefault_ReturnsNull_NoQuery`, `Take0_Single_Throws_NoQuery`, `Take0_SingleOrDefault_ReturnsNull_NoQuery`, `Take0_CastObject_First_Throws_NoQuery` and `Take0_CastObject_FirstOrDefault_ReturnsNull_NoQuery` (I2: not an empty list as "first"); `ScalarProjection_Take0_First_ThrowsScalarGuard_NoQuery` (`ScalarProjectionGuard` wins over the empty short-circuit); `SkipNegative_BehavesAsSkipZero` | all 4 |
| AC13-12 | `[DataTestMethod] Ordering_AfterEarlierOrdering_Rejected_BeforeAnyQuery` over {OrderBy.OrderBy, OrderBy.ThenBy.OrderByDescending, OrderBy.Where.OrderBy, OrderBy.Select(**subset**).OrderBy, OrderBy.Distinct.OrderBy}, each asserting the D10 message; `ThenBy_OnRoot_IsPrimaryOrder` (`((IOrderedQueryable<T>)q).ThenByDescending(p => p.Id).Where(marker)` returns rows in **descending** id order, the same as `OrderByDescending(p => p.Id).Where(marker)`; ascending would be indistinguishable from no order, r6 Q10) | all 4 |
| AC13-13 | `SqliteRoot_ReusedAfterProjection_BareRootNotNarrowed`, `SqliteRoot_ReusedAfterOrderedQuery_BareRootNoInheritedOrder`, `SqliteRoot_ReusedAfterOrderedQuery_ThenLast_UsesIdDesc`, `SqliteRoot_ReusedAfterParameterizedProjection_NoDuplicateParameters` | SQLite |
| AC13-14 | `SkipOnly_ToList_Executes`, `SkipOnly_First_ReturnsExpectedRow` (SQLite red; regression rows in the others); SQLite only: `SkipOnly_EmitsLimitMinusOneOffset` (SQL shape) | all 4 |
| AC13-15 | `NonGenericExecute_First_ReturnsSingleRow` (red on 3.9.0, K1: whole list), `NonGenericExecute_FirstOnEmpty_Throws` (asserts exactly `InvalidOperationException`, not wrapped), `NonGenericExecute_RejectedOperator_ThrowsNotSupported` (asserts exactly `NotSupportedException`, not `TargetInvocationException`), `NonGenericExecute_Count_ReturnsInt` (K4), `NonGenericExecute_Collection_Unchanged` (K2) | all 4 |

### 4.3 Interface coverage (new/changed members → tests)

| Member | Tests that call it on purpose |
|---|---|
| Core `QueryOperatorPolicy.EnsureSupported(Expression expression)` *(new, public, `void`)* | `QueryOperatorPolicyTests.*`; every `Rejected_*`/`Allowed_*`/`Operator_AfterPaging_*`/`Ordering_AfterEarlierOrdering_*`/`Covariant_*` row |
| Core `ScalarProjectionGuard.EnsureCollectionResult(Expression expression, Type resultType, Type memberType)` *(new, public; I2)* | `ScalarProjectionGuard_Terminal_Throws`, `ScalarProjectionGuard_Collection_Passes` (direct); end to end through `Covariant_ScalarSource_Terminal_Rejected_NoQuery` and every existing scalar-projection test |
| `*LinqQueryProvider.Execute(Expression)` (non-generic) — dispatch by expression shape *(I2, rev 8)* | `NonGenericExecute_First_ReturnsSingleRow`, `NonGenericExecute_FirstOnEmpty_Throws`, `NonGenericExecute_Collection_Unchanged` |
| `*LinqQueryProvider.Execute` — `isCollection` = `IQueryable` expression shape *(I2)* | `Cast_ReferenceConversion_Enumerated_MatchesOracle`, `Covariant_EntitySource_Enumerated_MatchesOracle`; all existing enumeration tests |
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
| No duplicate-key removal | `ThenBy_SameKeyTwice_Executes` (SQL Server; red on 3.9.0, §1.5 P8) |
| `Single*`: drop predicate→WHERE | `Single_Predicate_ReturnsTargetNotFirst` |
| `Single*`: limit 1 instead of 2 | `Single_TwoMatches_Throws`, `SingleOrDefault_TwoMatches_Throws` |
| `Single*`: ignore a user `Take(1)` | `Single_AfterTake1_OverManyRows_ReturnsRow` (`Take ?? 2` and `min(Take, 2)` are equivalent for `Take ≥ 2`; noted) |
| `Single*`: no cap after `Skip`-only | `Single_AfterSkipOnly_OverManyRows_Throws` via its SQL-shape assert (`FETCH NEXT 2 ROWS` / `LIMIT 2 OFFSET n`). The throw alone can't kill it. |
| `Single*`: route through the paging path (injects `ORDER BY id`) | `Single_NoUserOrder_EmitsRowLimit_NoIdOrder` (asserts **no `ORDER BY` at all**: provider-neutral, and the SQL-shape killer on SQLite, where the default is `ORDER BY rowid` and runs on any rowid table, r8 #7/r10-L1), `Single_OnEntityWithoutIdColumn_Works` (kills on SQL Server/PG/MySQL: no `id` column), `Single_AfterDistinctProjection_Works` (all 4) |
| `SingleOrDefault` throws on empty | `SingleOrDefault_Predicate_NoMatch_ReturnsNull` |
| `Last*`: don't invert / ignore explicit order | `Last_AfterOrderByNonIdKey_ReturnsLastInOrder` |
| `Last*`: invert only the first term | `Last_AfterOrderByThenByDescending_InvertsEveryTerm` |
| `Last*`: invert own-column terms only | `Last_AfterTernaryOrderBy_InvertsCaseTerm`, `Last_AfterRemoteOrderBy_ReturnsLastInOrder` |
| `LongCount` missing from the `OuterMethodCall` list (returns `0L`) | `LongCount_EqualsCount_ReturnsInt64` |
| `LongCount` boxed as `Int32` / `COUNT(*)` on SQL Server | `LongCount_EqualsCount_ReturnsInt64` / `LongCount_EmitsCountBig` |
| Add `Reverse` to the allow-list | `Rejected_…[Reverse]`, `SupportedOperators_ExactLiteralSetPinned` |
| Match by name instead of overload | `Rejected_…[IndexedWhere]`, `[OrderByWithComparer]`, `[DistinctWithComparer]`, `[TakeRange]` |
| `GetGenericMethodDefinition()` without `IsGenericMethod` | `NonQueryableOverload_IsRejected`, `Rejected_…[ScalarProjection_ParameterlessSum]` |
| I1 removed (predicate lambda parameter type not checked) | `Covariant_PredicateLambda_Rejected_I1Message_NoQuery` rows (`InvalidCastException`, not the I1 message) |
| I1 applied to **every** lambda operator (the rev 7 strict rule) | `Covariant_BaseClassSource_NonPredicateLambda_UnchangedFromConcrete` rows (the base-class and `Id`-interface shapes would be rejected) |
| Non-generic `Execute` lets `TargetInvocationException` escape | `NonGenericExecute_FirstOnEmpty_Throws`, `NonGenericExecute_RejectedOperator_ThrowsNotSupported` (assert the exact exception type) |
| I1 omits one hard-cast operator (e.g. `LongCount(pred)`, `All`, `Single(pred)`) | that operator's `Covariant_PredicateLambda_Rejected_I1Message_NoQuery` row (`InvalidCastException`) |
| I1 applied outer to a scalar `Select` too (masks the composition message) | `Covariant_ScalarSource_Lambda_KeepsCompositionMessage` (asserts "must be the outermost query operator"; the existing `ScalarProjection_ThenComposingLambdaOperators_*` tests only assert the operator name, which the I1 message also contains, so they can't kill this) |
| I1 applied outer to a non-subset (DTO) `Select` (masks the `Select`-shape message) | `Covariant_OtherProjectedSource_SelectShapeGuardMessage` |
| Non-generic `Execute(Expression)` still pins `IEnumerable<T>` | `NonGenericExecute_First_ReturnsSingleRow` (whole list, or `InvalidCastException` under I2-only) |
| I1 compares `TSource` with the source element type instead of checking the lambda parameter type (the rev 3–6 approach) | `Covariant_PredicateLambda_Rejected_I1Message_NoQuery[o.Distinct().Where(…)]`: after a covariant `Distinct<object>`, `TSource == source` (`object`), so a `TSource` check lets it through to the `InvalidCastException` (r6 Q2 mechanism; executed N1/N2). Not `Take`/`Skip`, where D8 rejects first. |
| `ScalarProjectionGuard` decides by `TResult` assignability (not expression shape) | `Covariant_ScalarSource_Terminal_Rejected_NoQuery[First/Single/Last, both spellings, ‡]` (whole list returned, Q1a–e/P6/P7) and `ScalarProjectionGuard_Terminal_Throws` |
| `ScalarProjectionGuard` call removed or bypassed in one provider (call-site mutation) | that provider's `Covariant_ScalarSource_Terminal_Rejected_NoQuery` rows (end to end) |
| Guard rejects valid enumeration | `ScalarProjectionGuard_Collection_Passes`, `Covariant_ScalarSource_SequenceOrEnumeration_MatchesOracle` |
| Lambda-free sequence operators over a converted source rejected | `Covariant_ScalarSource_SequenceOrEnumeration_MatchesOracle`, `Covariant_EntitySource_ParameterlessTerminal_MatchesOracle[‡]` |
| I1 evaluated before D8 | `Covariant_EntitySource_AfterPaging_D8Governs[Take(5)→Where]` (expects the D8 message) |
| `R` changes at a `Cast` (D5/I3 judged against the cast type) | `Cast_BackToEntityAfterTransparentCast_IsNoOp`, `OfType_Entity_AfterTransparentCast_IsNoOp` (rejected as unrelated/non-identity) |
| Reference-conversion `Cast` limited to `object` | `Cast_ReferenceConversion_Interface_Count_MatchesOracle`, `Cast_ReferenceConversion_BaseClass_Enumerated_MatchesOracle` |
| Treat any non-call node as the root | `NonCallNonRootSpineNode_Rejected` |
| Accept any `IQueryable` constant as the root (not only the queryable's own root) | `ForeignQueryableConstantRoot_Rejected` |
| `isCollection` unchanged (still `IEnumerable<T>` only) | `Cast_ReferenceConversion_Enumerated_MatchesOracle`, `Covariant_EntitySource_Enumerated_MatchesOracle` (`InvalidCastException`, P14) |
| Empty-`Take` short-circuit decides by `TResult` assignability | `Take0_CastObject_First_Throws_NoQuery` (empty list returned as "first") |
| Pass-2 failure checked before the whole-spine allow-list | `Rejected_AllowListFailureBeatsPass2Failure` |
| Policy visits the whole tree | `Allowed_PredicateWithCollectionContains_NotRejected` |
| Allow non-`Queryable` spine methods | `NonQueryableSpineMethod_Rejected` |
| Classifier allow-by-default | `ClassifierSweep_EveryQueryableMethod_MatchesLiteralSet` |
| Per-TFM computed expectation instead of the literal set | `SupportedOperators_ExactLiteralSetPinned` (literal count/signatures) |
| Policy inside the loop instead of a pre-pass | `Rejected_OperatorOuterToFailingInnerOperator_PolicyMessageWins` |
| Drop the `GroupBy` message | `GroupBy_Rejected_KeepsDedicatedMessage` |
| Identity check against the entity type `T` instead of the row type `R` | `OfType_Identity_AfterScalarProjection_NonNullable_IsNoOp` |
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
| Root-level `ThenBy*` dropped by the parse loop or visitor (e.g. `OrderByTerms` seeded only at `OrderBy`) | `ThenBy_OnRoot_IsPrimaryOrder` (`ThenByDescending`: ascending would mask it, Q10) |
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
     - **Pass 2 (inner→outer):** at each node, apply D8, D10, D5 and I1 in that order. Pass 2 tracks two
       things:
       - the root element type `T` (known from pass 1);
       - the **row type** `R`.
   - **Element type** of a node = the `T` in the `IEnumerable<T>` interface of its `Type`. That works for
     `SqlQueryable<T>`, `IQueryable<T>` and `IOrderedQueryable<T>` alike.
   - **Row type `R` (rev 7; replaces rev 6's "effective element type").** `R` is what the provider actually
     materializes per row. It starts as `T`, becomes the member type at a scalar `Select(x => x.M)`, and
     changes **nowhere else**. A subset `Select(x => new T { … })` keeps `T`; any other `Select` body is
     rejected by the parse loop's `Select`-shape guard. Conversions (the no-node `IQueryable<TBase>`, a
     `Cast`) and sequence operators never change `R`. So `R` can't be lost along the chain, which was the
     failure class of rounds r3–r6.
   - **Classification.** `IsAllowed(MethodInfo m)` uses the key
     `m.IsGenericMethod ? m.GetGenericMethodDefinition() : m`. It rejects non-`Queryable` declaring types, and
     keys outside a static allowed set built from `typeof(Queryable).GetMethods()` by name plus shape:
     - one `Expression<Func<TSource, …>>` lambda;
     - `Skip`/`Take` take `int`;
     - no comparer, default-value or `Range` parameters.

     Non-generic overloads are never in the set.
   - **Covariance: three invariants (rev 7 redesign, after four consecutive rounds of fix-introduced
     findings on the rev 3–6 cell table).** Each invariant is local and independent of how a conversion was
     spelled, so there's no type-flow to lose.
     - **I1 — predicate lambdas bind to the entity (pre-pass). Narrowed in rev 8 by the owner: reject
       only where 3.9.0 fails.**
       - **Scope:** the predicate operators, i.e. those whose predicate the translator hard-casts to
         `Expression<Func<T,…>>`. Those are `Where`; `First*`/`Last*` with a predicate; and
         `Any`/`All`/`Count` with a predicate (SqlLinqQueryProvider.cs:223/:261/:475; siblings
         PG :173/:199/:361, MySql :173/:199/:363, Sqlite :175/:200/:376). `Single*`/`LongCount` with a
         predicate are in scope too: 3.9.0 doesn't match them at all, and Tasks 5/8 route them through the
         same hard-cast branches.
       - For these, `lambda.Parameters[0].Type` must be `T`; otherwise they're rejected with the I1 message.
       - *Evidence:* every such shape over a converted source fails in 3.9.0. It either throws
         `InvalidCastException` (§1.5 I1a/b/d/e/f/g, r7 J5/J6/J14/J15, N1/N2, r8 S4–S7b) or, for
         `Single*(pred)`, silently returns an unrelated row (r8 S1–S3).
       - **Out of scope, and left alone:** `OrderBy*`/`ThenBy*`, selector aggregates (`Sum`/`Min`/`Max`/
         `Average`) and `Select`. They resolve by member or parameter generically.
         - Over a **base-class** source they return the same results as the concrete query in 3.9.0 (r7
           J1–J4, J7, J9, J10, J12, J13, J16–J18, e.g.
           `IQueryable<PersonEntity> b = db.Query<PersonDetailEntity>(); b.OrderBy(…)`).
         - Over an **interface** source, a member resolves to its lowercased property name (pre-existing,
           §8). In `OrderBy*` and selector aggregates it works where the column has that name (e.g. `Id`,
           `Gender`; J19/J20, r9-I1, r10-S1); otherwise the database reports "invalid column" (J21, S8/S9). A
           scalar `Select` of a get-only member is rejected by the `Select`-shape guard (r9-I2).
         - Otherwise they already fail cleanly (I1c's visitor message, I1h's `Select`-shape message).
         - I1 doesn't change any of this.
       - **Exception:** operators outer to any **non-subset `Select`** are left to the parse loop. After a
         scalar `Select`, the composition guard rejects every lambda with its existing message. After any
         other non-subset `Select`, the `Select`-shape guard rejects the `Select` itself. The pre-pass uses
         the parse loop's exact predicates:
         - **scalar:** body is a `MemberExpression` on the lambda's `ParameterExpression`, a `PropertyInfo`,
           `CanWrite` (SqlLinqQueryProvider.cs ~364-374);
         - **subset:** body is a `MemberInitExpression` of type `T`;
         - **anything else** is "other".
     - **I2 — collection vs single row is decided from the expression, not `TResult` (execution).**
       "Collection" means `typeof(IQueryable).IsAssignableFrom(expression.Type)`; otherwise it's a
       **terminal**. Used in three places:
       - in `Execute`, replacing `isCollection` (fixes P14 and the r6 F3 cases);
       - in the empty-`Take` short-circuit;
       - in `ScalarProjectionGuard`, where a scalar row type (`R` ≠ `T`) supports **collections only**, so
         **every terminal is rejected** whatever `TResult` is.

       That closes the scalar "whole list as First" class on every path: direct (P6, P7) and through
       covariant `Skip`/`Take`/`Distinct` (r6 Q1a–e). The guard is the primary mechanism, and these LINQ
       shapes reach it.
     - **I3 — `Cast`/`OfType` are judged against `R` (D5).**
       - `Cast<X>` is allowed iff `X == R`, or `R` is a reference type and `X.IsAssignableFrom(R)`. It's
         ignored by translation either way.
       - `OfType<X>` is allowed iff `X == R` (identity; the owner's amendment covered only `Cast`) and `R` is
         `T` or a non-nullable value type, so no element can be dropped.
       - Everything else is rejected.
     - **Consequences, all executed:**
       - Parameterless terminals and lambda-free sequence operators (`Skip`/`Take`/`Distinct`) over any
         converted source are left alone: they never bind a lambda, and translation is type-agnostic.
       - Over an entity source they're correct (P5, P5b, P13, P13b, r6 Q3a–e, Q9a/b, Q4d).
       - Over a scalar source, sequence operators are correct (P7b–P7e, Q8), and terminals are rejected by I2.
   - `IsAllowed` is **public**.
   - **Allowed operators:**
     - `Where`, `Select`;
     - `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`;
     - `Skip`, `Take`, `Distinct`;
     - `First*`, `Single*`, `Last*` (with and without predicate);
     - `Any`, `All`, `Count`, `LongCount`;
     - `Sum`, `Average`, `Min`, `Max` (generic, with selector; behavior unchanged until 3.10.1);
     - `Cast`/`OfType` per D5/I3.
   - **Pass-2 rules, in this order at each node:**
     1. D8, after the first `Skip`/`Take`. A `Take` is allowed after the `Skip` when only `Select` and/or
        allowed D5 `Cast`/`OfType` sit between them.
     2. D10: an `OrderBy`/`OrderByDescending` after any earlier ordering call is rejected.
     3. D5/I3 (`Cast`/`OfType` against `R`).
     4. I1 (lambda parameter type).

     Pass 1's allow-list runs over the **whole spine first** (outer→inner), so an allow-list failure anywhere
     wins over any pass-2 failure. Within pass 1, the outermost failure wins.

     *(Rev 4's separated-`ThenBy*` clause was removed in rev 6: the shape can't be built, P3.)*
   - **Messages.**
     - `GroupBy` keeps its dedicated message.
     - Other rejected operators: `"{Op}(...) is not translated to SQL in this version. Materialize first and
       apply it in memory: query.ToList().{Op}(...)."`
     - Positional (D8): `"{Op}(...) after Skip/Take is not translated …"`.
     - D10, second `OrderBy*`: `"A second OrderBy is not translated; use ThenBy, or put the primary key
       first."`
     - I1: `"{Op}(...) takes a predicate over {ParamType}, but the query's rows are {T}. Apply {Op} before
       converting the element type (IQueryable<…>/Cast<…>), make the helper generic
       (`where TEntity : {ParamType}`), or query the concrete type."`
     - I2, scalar terminal: the existing scalar-guard message, which names the operator and says scalar
       projections support enumeration only (materialize first).
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
   - **I2 in `Execute` (rev 7).** In each provider, `isCollection` becomes
     `typeof(IQueryable).IsAssignableFrom(expression.Type)` (SqlLinqQueryProvider.cs:99; siblings :58).
     - Collections return the `List<T>` read from the database, typed as `TResult` through covariance (it's
       always an `IEnumerable<X>` with `X` assignable from `T`, because the expression's element type is `T`
       or a supertype).
     - This fixes P14 (`Cast<object>()` enumerated), and it's exact for pathological types such as
       `T : IEnumerable<object>` (r6 F3a). Paths that work today are unchanged: enumeration always has an
       `IQueryable` expression, and terminals never do.
   - **I2 in the non-generic `IQueryProvider.Execute(Expression)` (rev 8).** Today it pins
     `TResult = IEnumerable<T>` (SqlLinqQueryProvider.cs:70-73; siblings :38-41), so a terminal expression
     returns the whole list as its "result" (r7 K1, a silent wrong result).
     - It dispatches on the expression shape instead: an `IQueryable`-typed expression goes to
       `Execute<IEnumerable<elementType>>`; anything else goes to `Execute<expression.Type>`, via
       `MakeGenericMethod`.
     - **Exceptions must not arrive wrapped (r8 #8).** `MethodInfo.Invoke` wraps everything in
       `TargetInvocationException` (r8 X1, K5), and netstandard2.0/net48 lack `DoNotWrapExceptions`. So the
       dispatcher catches `TargetInvocationException` and rethrows the inner exception with
       `ExceptionDispatchInfo.Capture(inner).Throw()`, preserving its type and stack trace. (A cached typed
       delegate is an equivalent alternative.)
     - Collection calls are unchanged (K2); value-type terminals return correctly (K4).
   - **I2 in `ScalarProjectionGuard` (rev 5; reworked in rev 7).** A Core public helper,
     `ScalarProjectionGuard.EnsureCollectionResult(Expression expression, Type resultType, Type memberType)`,
     is called first by `ExecuteScalarProjection` in all four providers.
     - It throws the existing scalar-guard message (naming the outermost operator) when the expression is a
       **terminal** (I2).
     - It keeps the existing `resultType` ⊇ `List<memberType>` check as a backstop.
     - **It is the primary mechanism for scalar terminals and is reached through LINQ**: P6, P7, r6 Q1a–e. So
       the §4.2 rows pin it end to end, and direct DB-free tests cover its branches.
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
   - With `Distinct` + custom projection and no explicit order, it throws, naming `Last`. This check lives in
     `BuildQueryComponents`, next to the existing `Distinct` guards. On the scalar path,
     `ScalarProjectionGuard` runs first and wins (`ScalarDistinctLast_ScalarGuardWins`).
   - This replaces the current `LastOrDefault(pred)` block. D10 (no second `OrderBy*`) plus System.Linq
     (a `ThenBy*` can only follow the root or an ordering call, §1.5 P3/P4) guarantee the ordering terms form
     one contiguous chain, so the inversion is well defined.
5. **`LongCount`.** Added to the `OuterMethodCall` list and `BuildAggregateClause`, and handled like `Count`
   (including the reverse-key rejection). SQL Server emits `COUNT_BIG(*)`. The result converts to `Int64`.
6. **`Skip`/`Take` values.**
   - `Skip(n < 0)` is stored as 0.
   - `Take(n ≤ 0)` sets `components.IsEmptyByTake`.
   - Two checks, in this order of precedence:
     - `Execute` checks the flag **after** the scalar dispatch (SqlLinqQueryProvider.cs:94-97), so it only
       handles the entity path (`R = T`).
     - `ExecuteScalarProjection` checks it **after** `ScalarProjectionGuard` (I2), with `R` = the scalar
       member type. `Select(p => p.Id).Take(0).First()` therefore throws the scalar guard's
       `NotSupportedException`, not "no elements".
   - Outcomes, decided by **I2** (expression shape), never by `TResult` assignability:
     - collection → an empty `List<R>`, returned as `TResult` through covariance;
     - terminal `First`/`Single` → throw "no elements";
     - terminal `*OrDefault` → `default(TResult)`.

     So `q.Take(0).Cast<object>().First()` throws, rather than returning an empty list as "first" (r6 F3b). No
     command is built.
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
    r5: 8 (§9.5), r6: 8 (§9.6).
  - ✅ Owner answered the §10 questions (3.10.1), amended D5 (reference-conversion `Cast`), filed the
    sort-helper crash as follow-up #16, and deferred signing.
  - ✅ Executed premise probes recorded (§1.5), including the r6 reviewer's and the I1 probes.
  - ✅ r7: 6 findings (§9.7). The owner narrowed I1 to predicate lambdas ("reject only where 3.9.0 fails").
  - ✅ r8: 9 findings (§9.8), r9: 4 (§9.9), r10: 1 + 6 nits (§9.10). Text and test-spec only.
  - ⏳ Fix-verification r11 of the rev-11 diff must be clean. Then post the ACs, including AC13-15, and start
    Task 1 (owner instruction).
  - Then post the §3 ACs to #12/#13 and start Task 1.
- **Task 1 — Stubs, schema, harness, red tests.**
  - Compile-only stubs so the red run fails at runtime: policy members throw `NotImplementedException`; plus
    the visitor `tableQualifier`, `OrderByTerms`, and the `QueryComponents` members.
  - **Schema:** no script changes.
    - The no-`id` rows use a test-owned `single_probe` table, created by the test class itself (§4.1).
    - The SQLite temp-DB helper creates the join tables (`country`, `organization`, `address`, `person`,
      `person_address`), as `SqliteRemoteFeaturesTests` does today.
  - Harness, SQLite helper, and the net48/net9 links.
  - Add `IHasPersonId` (`int Id { get; }` only) to the person entities in all four test projects, and
    MySQL's `PersonBase` (§4.1). That supports the base-class/interface rows and the D5 base-class/interface
    `Cast` rows. Then run the four existing suites to confirm the entity change is behavior-neutral.
  - Write every §4.2 test and record red (or expected-green for regression rows).
  - Confirm §1.5's "still unexecuted" items through that red run. Everything else in §1.5 is already
    executed: P8 error 169, P9 alias binding, P10 SQLite `OFFSET`, P1 `Cast` nodes.
- **Task 2 — #12 qualifier + duplicate removal** (4 providers).
  → AC12-1…AC12-4, AC12-6, AC12-9.
- **Task 3 — SQLite `rowid` qualification + `LIMIT -1 OFFSET`.**
  → AC12-7, AC13-14.
- **Task 4 — Core policy (two-pass, own-root check, row type `R`) + D8, D10 + D5/I3 + I1 + I2 (`isCollection`
  by expression shape; Core `ScalarProjectionGuard`) + wiring + dead-guard removal** (4 providers).
  → AC13-4 (except the entity-source covariant `LongCount`/`Single`/`Last` rows), AC13-6, AC13-7, AC13-8,
  AC13-10 (rejection rows, and the allowed `Take.Cast<object>()`-enumerated and Q7 rows, which depend on
  I2), AC13-12, AC13-15 (non-generic `Execute` dispatch).
- **Task 5 — `Single*` row limit, `Skip`/`Take` values, empty-`Take` short-circuit (I2-based)** (4 providers).
  → AC13-1, AC13-10 (remaining allowed rows, and empty rows), entity-source covariant `Single` row of AC13-4.
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
- **Interface-member column resolution (pre-existing; r7 J21, r8 S8/S9).** Over an interface-typed source
  (`IQueryable<IFoo>`), a lambda referencing an interface member resolves its column by the lowercased
  property name. The column cache is keyed by `DeclaringType.Name.Prop` and never filled for interface
  members, so `FirstName` → `firstname` and the database reports "invalid column". It's loud, not silent,
  and affects only interface-typed queries. Follow-up issue candidate; not in 3.10.0.
- **Static identifier caches are shared across providers (pre-existing; r8 incidental).** `_tableNames`,
  `_columnNames` and `_mappedTypes` are `static` on the Core `OrmDataProvider`. Using the same entity type
  with two providers in one process makes the second emit the first provider's quoting (PostgreSQL emitted
  `FROM [User]`). It matters for multi-provider apps. Follow-up issue candidate; not in 3.10.0.
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

### 9.6 Task 0 fix-verification r6 of `44e78b4`

Totals: AC-GAP 1, TEST-GAP 7, HOUSE-RULE 0, PLAN-GAP 0, OTHER 0. All 8 were introduced by rev 6 (F3 partly
pre-existing). The r6 reviewer executed its premises (Q1–Q12, SQL Server), which are now cited in §1.5. It
marked R5-1..R5-8 as 4 RESOLVED / 4 PARTIAL (R5-2 → F7/F8a; R5-3 → F1; R5-4 → F2; R5-5 → F6).

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| F1 | MAJOR | TEST-GAP | yes (4th consecutive round on the covariance rule) | A converted scalar source plus an allowed `Skip`/`Take`/`Distinct` plus a terminal escapes the "effective element type" check; 3.9.0 returns the whole list (Q1a–e) | **Redesigned**: the covariance type-flow and cell table are removed; invariants I1–I3 (§5.2.1). I2 (expression-shape terminal detection in `ScalarProjectionGuard`) rejects every scalar terminal on every path and is pinned end to end by ‡ rows plus direct tests. Call-site mutation row added. |
| F2 | minor | AC-GAP | yes | Entity covariant `Skip`/`Take`/`Distinct` rejected though correct in 3.9.0 (Q3) | Preferred option taken: allowed. They're lambda-free, so I1 doesn't apply; translation is type-agnostic. Enumeration is fixed by I2. Rows flipped to oracle rows. |
| F3 | minor | TEST-GAP | yes (partly pre-existing) | Type-based `isCollection` misclassifies `T : IEnumerable<X>` terminals; empty-`Take` collection branch could return an empty list as "first" | I2 uses `typeof(IQueryable).IsAssignableFrom(expression.Type)` in `Execute`, the empty-`Take` branch and the guard. `Take0_CastObject_*` rows plus a mutation row. |
| F4 | minor | TEST-GAP | yes | Only `Cast<object>` rows; D5 static vs effective ambiguity | D5 judged against the row type `R` (I3). Base-class (Q4c), interface (Q4d), cast-back (Q4a) and `OfType`-after-cast (Q4b) rows. MySQL marker interface in Task 1. Mutation rows. |
| F5 | minor | TEST-GAP | yes | AC13-10 excluded scalar reference `Cast` after paging | Wording judged against `R`; Q7 allowed row. |
| F6 | minor | TEST-GAP | yes | AC13-8 per-node order contradicted the two-pass allow-list | AC13-8 restated (pass 1 whole spine, outermost wins; pass 2 per node). `Rejected_AllowListFailureBeatsPass2Failure` plus a mutation row. |
| F7 | minor | TEST-GAP | yes | `ThenBy_OnRoot` couldn't tell primary order from no order (Q10) | Uses `ThenByDescending(p => p.Id)`; mutation row "root `ThenBy` dropped". |
| F8 | minor | TEST-GAP | yes | Stale text: §5.2.4 rev 4 `ThenBy` attribution; Task 1 UNVERIFIED list; §4.4 "169 to be confirmed"; "(whole list)" parentheticals; Task mapping; §1.4 over-broad; uncited premises | (a) attributed to D10 + P3/P4; (b) Task 1 defers to §1.5's list; (c) cites P8; (d) the old covariance mutation rows were replaced and all rows assert messages; (e) Q7/`Take.Cast` rows mapped to Task 4; (f) §1.4 precise; (g) r6 Q-probes added to §1.5. |

**Author's own probes while applying rev 7** (§1.5 I1a–i) established I1's premise before it was written. One
self-correction: the first draft of I3 widened `OfType` to reference conversions, beyond the owner's
amendment, and was narrowed back to identity before commit. Another: a drafted I1 mutation killer used
`Take(3)`, where D8 preempts, and was replaced by the `Distinct()` row.

### 9.7 Task 0 fix-verification r7 of `765792a`

Totals: AC-GAP 1, TEST-GAP 3, HOUSE-RULE 1, PLAN-GAP 1, OTHER 0.
- **Fix-introduced:** #1 (the claim was new; the rejection itself came from the rev 3–6 rule), #2, #3, #4.
- **Pre-existing:** #5 and #6.

The reviewer executed J/K/L/M/N probes (SQL Server), now in §1.5. F1–F8 of r6: all RESOLVED.

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| 1 | MAJOR | AC-GAP | yes (claim) | "Every lambda over a converted source fails in 3.9.0" is false: base-class/interface `OrderBy*`/selector aggregates/scalar `Select` are correct (J1–J20) | **Owner: reject only where 3.9.0 fails.** I1 narrowed to the hard-cast predicate operators. False statements corrected (§1.4, §1.5, §5.2.1, AC13-4). Rows: base-class non-predicate (must stay green), base-class and `object` predicate (I1 message), base-typed paging (N3 → I2). Mutation rows for "I1 strict" and "I1 omits an operator". The I1 message suggests a generic helper. |
| 2 | minor | TEST-GAP | yes | I1 masked the `Select`-shape message after a DTO `Select`; I1h was used by two rows with different expectations; the scalar predicate was unspecified | I1 defers after any non-subset `Select`; predicates are identical to the parse loop's. `OtherProjectedSource` row re-spelled as L1. I1h no longer in the I1 row (`Select` is out of I1's scope). |
| 3 | minor | TEST-GAP | yes | ‡ cross-product contradicted D8 and the Distinct+aggregate guard; scalar `Count`/`Any` mislabeled red | ‡ defined per operator. `Count`/`Any` scalar rows are regression rows. AC13-4 I2 says "every terminal that reaches execution". `s.Distinct().Last()` precedence pinned (scalar guard wins; the Last/Distinct check is in `BuildQueryComponents`). |
| 4 | minor | TEST-GAP | yes | Existing composition tests can't kill "I1 outer to scalar Select" | The killer asserts "must be the outermost query operator"; existing tests are dropped from the killer list. |
| 5 | minor | HOUSE-RULE (pattern 4) | no (pre-existing; I2's "every path" claim new) | Non-generic `Execute(Expression)` returns the whole list for `First` (K1) | Fixed: dispatch by expression shape. AC13-15 + 3 rows + a mutation row; mapped to Task 4. |
| 6 | minor | PLAN-GAP | no | Type-filter wording contradicted D5/I3 | Reworded; mutation says "instead of `R`". |

**Author self-check while applying rev 8:** a drafted Task 1 line had the SQLite helper load only
`integration_test_schema.sql`, which lacks `organization`/`country`. It was corrected to combine that script
with the existing join-table DDL before commit.

### 9.8 Task 0 fix-verification r8 of `3091544`

Totals: AC-GAP 2, TEST-GAP 7, HOUSE-RULE 0, PLAN-GAP 0, OTHER 0. 8 of 9 were introduced by rev 8; #7 was
pre-existing. r7 #1–#6: 4 RESOLVED, 2 PARTIAL (#1 → new #1/#3). The reviewer found no I1-scope operator that
works over a base-class source (S1–S7 executed), so the narrowing has no over-rejection.

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| 1 | MAJOR | AC-GAP | yes | "Non-predicate lambdas over an interface source are correct" holds only for `Id`; other members fail on a pre-existing column bug | Claims narrowed (§1.4, AC13-4, §5.2.1); interface rows limited to `Id`; J21/S8/S9 in §1.5; bug recorded in §8. Citations corrected (J11 and J5/6/14/15 removed from the "correct" lists). |
| 2 | minor | TEST-GAP | yes | `Average` in an oracle row contradicts AC13-5 and is red | "Left alone" rows compare with the concrete query (`UnchangedFromConcrete`), independent of D9. |
| 3 | minor | AC-GAP | yes | `Single*(pred)` silently returns an unrelated row (not `InvalidCastException`); `LongCount` fails on the result cast | AC13-4, §5.2.1 and the row red-reasons reworded; S1–S7 in §1.5. |
| 4 | minor | TEST-GAP | yes | I1 rows missing `Last*`/`FirstOrDefault`/`SingleOrDefault(pred)`; composition row operator unspecified | Rows added; composition row uses `Where`/`Count(pred)`. |
| 5 | minor | TEST-GAP | yes | Base-class/interface rows unbuildable (no member interface; MySQL has no base class) | `IHasPersonId` (`Id`, `LastName`) in all four; MySQL `PersonBase`; MySQL source named in the matrix; behavior-neutral check in Task 1. |
| 6 | minor | TEST-GAP | yes | Local `dbo.User` has `id` | **Superseded by the author:** `DocumentationGapTests` owns and recreates `[User]` with `Id` on every setup, so recreating it is futile. The no-`id` test owns a `single_probe` table. |
| 7 | minor | TEST-GAP | no | On SQLite, `ORDER BY rowid` survives the paging mutation | "No ORDER BY" SQL-shape assert named as the SQLite killer; mutation row annotated per provider. |
| 8 | minor | TEST-GAP | yes | Non-generic dispatch via `MethodInfo.Invoke` wraps exceptions | `ExceptionDispatchInfo` rethrow of the inner exception; rows assert exact types (`InvalidOperationException`, `NotSupportedException`); `Count` → `int` row; mutation row. |
| 9 | minor | TEST-GAP | yes | ‡ produces subset-`Select` `Distinct().Last()`, which AC13-2 makes throw | Excluded from ‡; covered by `Last_AfterDistinctProjection_NoOrder_ThrowsNamingLast`. |

**Recorded follow-ups from r8** (§8): interface-member column resolution; static identifier caches shared
across providers (the reviewer filed a suggested-task chip for this one).

### 9.9 Task 0 fix-verification r9 of `8d5ac7a`

Totals: AC-GAP 0, TEST-GAP 2, HOUSE-RULE 0, PLAN-GAP 2, OTHER 0. 3 of 4 were introduced by rev 9; F3 was
pre-existing. r8 #1–#9: 7 RESOLVED, 2 PARTIAL (#1 → F1, #5 → F3). #6 superseded, with its rationale
verified. The reviewer re-read every §3 AC and found them accurate apart from F1 and F4.

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| F1 | minor | PLAN-GAP | yes | Interface wording false both ways: `Gender` works; get-only members fail with the `Select`-shape exception, not a DB error | Reworded in §1.4, AC13-4 and §5.2.1 as "unchanged", with r9 evidence. |
| F2 | minor | TEST-GAP | yes | `single_probe` DDL not identity (INSERT omits `int` keys); SQLite `INT PRIMARY KEY` isn't a rowid alias; the "only killer" under-claim | Identity DDL per provider; SQLite `INTEGER PRIMARY KEY`; kill note corrected (`Single_AfterDistinctProjection_Works` also kills). |
| F3 | minor | TEST-GAP | no | `Cast<INotifyPropertyChanged>` row unbuildable on MySQL | Spelled per provider (`Cast<IHasPersonId>`; `Cast<PersonBase>` on MySQL). |
| F4 | minor | PLAN-GAP | yes | r8 probe IDs collided with r7 IDs (M1, M3, L1) | r8 IDs renamed; AC citations prefixed `r7`. |

### 9.10 Task 0 fix-verification r10 of `830b707`

Totals: AC-GAP 0, TEST-GAP 0, HOUSE-RULE 0, PLAN-GAP 1, OTHER 0 (plus 6 non-blocking nits). F1–F4 of r9 are
all RESOLVED. The reviewer executed the new rev-10 premises (SQL Server interface probes; SQLite key
behavior) and found them true. It re-read every §3 AC: "ACs safe to post: yes; Task 1 blocked: no".

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| R10-1 | minor | PLAN-GAP | yes | Rev-10 premises cited a bare "r9" not recorded in §1.5; "r9 P1" collided with P1; SQL Server `single_probe` insert missing from "still unexecuted" | §1.5 r9/r10 table with unique IDs; citations replaced; "still unexecuted" extended. |
| N1–N6 | nit | — | — | Interface wording (get-only is separate from column naming); matrix rationale; evidence ranges; stale legend; "therefore"; AC13-6 example | All applied. |

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
