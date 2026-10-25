# FunkyORM backlog after 3.10.0 — deferred items and test-row hygiene

> **Status (2026-10-05):** written after the 3.10.0 release. Not yet reviewed by the owner. Every "Owner decision"
> below is open until the owner answers it in chat.
> - **Baseline:** 3.10.0 is released. `origin/master` is `6d2f3fa` ("Merge 3.10.0 to master (NuGet stable
>   release)"), and its tree equals `development/3.10` at `ec4b46c`. The tag `v3.10.0` and the GitHub release
>   exist, and `Funcular.Data.Orm 3.10.0` is on nuget.org with all 12 DLLs at `3.10.0+6d2f3fa`.
> - **Purpose:** define each deferred item well enough that any agent can pick it up, alone or alongside others.
>   Each item is a **work package (WP)** with its problem, evidence, current state, owner decisions, acceptance
>   criteria (AC) and definition of done (DoD). §2 says which WPs are independent and which must be stacked.
> - **Authority:** where a WP points at an existing plan on a parked branch, that plan's ACs remain authoritative,
>   with the amendments listed here. This document doesn't override an owner decision recorded in those plans; it
>   lists the ones that conflict with later directives so the owner can settle them.

---

## 0. Summary

| WP | Item | Severity | Proposed release | State | Stacks on |
|---|---|---|---|---|---|
| **A** | WHERE drops a member of a member (`x.D.Year`, `x.Name.Length`) | **Data loss in deletes; undisclosed** | 3.10.1 | Not started; no plan | — |
| **B** | A captured value used as a collection `Contains` item is read as a column | **Data loss in deletes; undisclosed** | 3.10.1 (fail closed) | Not started; WP-F fixes it fully | — (F supersedes) |
| **C** | Aggregate correctness (`Average`, `Min`/`Max`, empty sets) | Wrong results; **publicly promised for 3.10.1** | 3.10.1 | Design decided (operator plan §10); no plan | — |
| **D** | Test-row hygiene (suites leak `person` rows) | Test-only | 3.10.1 (any release) | CLEAN at `9c37ac0`, unpushed, 3 owner decisions open | — (land first) |
| **D2** | Hygiene follow-up: the other leaking tables | Test-only | Unscheduled | Recorded only | D |
| **E** | Packaging and documentation truthfulness | Install/load failures; false docs | 3.10.1 | Not started | — |
| **F** | Captured values in WHERE method calls ("I1-6") | Wrong rows | 3.11.0 | CLEAN at `0b19ef2`, unpushed; its Task 6 not done | (A, B) |
| **G** | LIKE escaping and a column's `ToString()` | Wrong rows in queries | 3.11.0 | CLEAN at `75e172f`, unpushed | F |
| **H** | SQL-vs-C# null semantics (`!=`, `All`, `NOT IN`) | Wrong rows; deletes get wider when fixed | 3.11.0 | Scoping notes only | F |
| **I** | SQLite `Guid`/`DateTime` binding and culture-dependent dates | Wrong rows on SQLite | Unscheduled (owner's call) | Recorded only | — |
| **J** | Strong naming | — | Owner to schedule | Deferred by the owner on 2026-09-30 | — |

Appendix A lists other pre-existing issues that have been recorded but not scheduled. Appendix B holds the evidence
for WPs A and B, and Appendix C the inventory of branches and worktrees.

---

## 1. Ground rules for every WP

### 1.1 Scope criteria and owner directives

These are recorded from the 3.10 effort. Unless the owner says otherwise, they still apply.

- **Exclusion criteria (owner, 2026-10-05):** don't pursue an enhancement when the construct doesn't work in Entity
  Framework, or when it isn't something that worked in ≤ 3.9 and broke later. Read strictly, most of this backlog
  fails the second test: these shapes never worked. **Owner decision OD-0:** do the criteria still govern
  post-3.10 work, and is the data-loss rule below an exception to them? Each WP gives its triage against the
  criteria.
- **Data-loss rule (owner, 2026-10-05):** when a construct that looks right to a developer can delete every row (or
  more rows than C# selects) and the real fix is expensive, throw `NotSupportedException` if the shape can be
  recognized cheaply.
- **Don't fix an old bug by making something throw that worked in 3.9** (owner, 2026-10-05). Under the
  2026-10-03 directive, "worked" means the shape returned C#'s rows, not merely that it didn't throw. A shape that
  happened to return the right rows and now throws must be listed under the Changelog's **Changed**, and the owner
  must agree. WPs F and G have such shapes (§3).
- **Reject edge cases (owner, 2026-10-03):** prefer rejecting a parameter shape that needs evaluation (and
  requiring a plain value) over accommodating it. Don't ask the owner again for each new edge case. Disclose
  honestly.
- **Collation (owner, 2026-10-05):** developers are trusted to know their SQL collation and C# comparers. A
  non-trivial WHERE may legitimately evaluate differently in the database than in C#. Never reject a comparison
  that reads a column because of case or collation.
- **Only CLEAN work ships** (owner, 2026-10-05): a branch merges only after a CLEAN non-author review of its exact
  commit.
- **Keep scope fixed:** a WP doesn't grow. A new finding outside the WP's ACs goes into its plan's "Out of scope"
  section and to the owner. Don't fix it in passing.

### 1.2 Branch and release model

- **CI publishes on every push to `master`**: `ci.yml`'s `publish` job packs the SqlServer project and pushes it to
  nuget.org after `test-net8` passes. It doesn't pass `--skip-duplicate`. So **never merge to `master` without a
  version bump**: an unchanged version (3.10.0) makes the publish job fail with a conflict.
- **Integration branches:** cut `development/3.10.1` (and later `development/3.11`) from `origin/master`. Each WP
  works on its own `fix/<name>` branch, preferably in its own worktree, cut from the integration branch (or from the
  branch named in §2). It merges back into the integration branch after a CLEAN non-author pass. The release is a
  PR from the integration branch to `master`, with the version bump, merged with a merge commit.
- **One integrator:** one agent (or the owner) owns merges into the integration branch and every push, as the
  "FunkyORM Work" session did for 3.10. Other WP agents hand their verified commit back to the integrator. They
  don't push.
- **Before the final review of a WP:** fetch, and confirm the target is an ancestor of HEAD
  (`git merge-base --is-ancestor origin/<target> HEAD`). A merge creates a new commit, which needs a new review.

### 1.3 Repository facts an agent needs

- **Projects:**
  - Libraries: `Funcular.Data.Orm.Core`, `.SqlServer` (packed as the one NuGet package `Funcular.Data.Orm`),
    `.PostgreSql`, `.MySql` and `.Sqlite`.
  - The package bundles all four providers for `net8.0` and `netstandard2.0`; `net48` gets SQL Server only.
  - Version lives in each of the five library csprojs (`Version`, `InformationalVersion`).
- **Suites:**
  - SQL Server: `dotnet test Funcular.Data.Orm.SqlServer.Tests/Funcular.Data.Orm.SqlServer.Tests.csproj --filter
    "FullyQualifiedName!~PerformanceTests&FullyQualifiedName!~EntityFramework"`. This is CI's filter; 1475 tests at
    3.10.0.
  - PostgreSQL (919), MySQL (872) and SQLite (800): `dotnet test` on each test project.
  - `Funcular.Data.Orm.SqlServer.Tests.DotNet9` (5).
  - net48 (79): build `Funcular.Data.Orm.SqlServer.Tests.NetFramework` with **Visual Studio's MSBuild**
    (`-restore -t:Rebuild`), then run it with `vstest.console.exe`. `dotnet build` leaves out `System.Memory.dll`
    and the suite can't run.
- **Databases:**
  - Connections: `FUNKY_CONNECTION` (SQL Server), `FUNKY_PG_CONNECTION` (or `postgres_user` + `postgres_pwd`),
    `FUNKY_MYSQL_CONNECTION`.
  - Never print them.
  - Database work runs serially; throwaway objects use a `zz_` prefix, and you leave databases as you found them.
  - **Never delete rows you didn't create** (owner rule): no sweeps by name or marker.
- **Coverage:** `coverlet.collector` is referenced. Run with `--collect:"XPlat Code Coverage"` and read the per-file
  `line-rate` from the cobertura XML. A file's coverage is the union across every `<class filename>` element and
  every suite.
- **Files:**
  - Keep each file's line endings (the tree is CRLF on Windows) and its BOM (`Changelog.md` has one).
  - Make edits that contain backslashes with an editor tool or a script file, not a shell heredoc (the heredoc
    strips them).
  - Keep exported test trees on short paths (`C:\temp\…`); SQLite's native DLL fails on long ones.
- **Security:** details of the MySQL ORDER BY literal-injection finding fixed in 3.10.0 stay out of public files.
  The Changelog's Security entry stays at release-note level.
- **Packing (release only):**
  - Run `dotnet build Funcular.Data.Orm.SqlServer/Funcular.Data.Orm.SqlServer.csproj -c Release --no-incremental`,
    then `dotnet pack … -c Release`. Don't pass `--no-build` (it fails with NETSDK1085).
  - Then extract the `.nupkg` and check that every `lib/*/*.dll` carries the release version. A stale
    `bin/Release` once shipped a 3.8 DLL beside 3.10 ones, which threw `MissingFieldException`.

### 1.4 Common definition of done (every WP)

1. **Plan first.** Write `docs/plans/<NAME>_PLAN.md` with:
   - a goal and status block;
   - verified premises, from probes executed against `origin/master` and recorded;
   - the owner's decisions, with dates;
   - acceptance criteria;
   - a test plan (an AC → test matrix, an interface-coverage table, and the mutation each key test must kill);
   - tasks;
   - out of scope;
   - review dispositions.

   WPs D, F and G already have plans: amend those instead of writing new ones.
2. **Task 0 review.** A non-author reviews the test plan before implementation and finds it CLEAN.
3. **Red, then green.** Each named test exists and fails before the production change that makes it pass. Record
   the red evidence.
4. **Mutations.** Each key test's mutation is executed and killed, and the result is recorded.
5. **Coverage.** Every touched production file reaches ≥ 85 % line coverage.
6. **Suites.** All six suites pass, and their counts are recorded. Opt-in suites are run when they are the coverage
   source.
7. **Database hygiene.** Tests leave the databases as they found them. Once WP-D lands, new integration tests use
   `TestRowLedger.Track`.
8. **Documents.**
   - Changelog entries go in the release's section (`Fixed`, `Changed`, `Known issues`, `New public API`).
   - `Usage.md`, `Advanced.md` (§5 operator table), `README.md` and `docs/ai-instructions/*` are updated wherever
     they describe the behavior.
   - Lists of shapes are given as examples, not as complete lists.
   - Every code shape the documents advise has been executed.
9. **Review.**
   - A non-author does an adversarial review of the exact commit. Each fix layer gets a non-author
     fix-verification, until CLEAN.
   - Every accepted finding is blamed `AC-GAP`, `TEST-GAP`, `HOUSE-RULE`, `PLAN-GAP` or `OTHER` (first match wins).
   - Nits confined to the plan's records, revision notes or commit messages don't block (owner rule, 2026-10-02).
10. **Push gate.** The `ADVERSARIAL_CLEAN` marker is written only after a CLEAN pass on that exact commit. Never
    bypass it.
11. **Pull request.** A PR's author never approves it.

**Release DoD** (the integration branch → `master`):
1. Version bumped in the five csprojs.
2. Changelog section dated, and the README banner updated.
3. The pack verified as in §1.3, plus a consumer smoke test: install from a local feed with an isolated packages
   folder and load every provider.
4. Merged with the subject "Merge X.Y.Z to master (NuGet stable release)".
5. A lightweight tag `vX.Y.Z` on the merge commit, and a GitHub release.
6. The version confirmed on nuget.org, with its DLL versions checked.

---

## 2. Dependencies and stacking

```
origin/master  (3.10.0, 6d2f3fa)
└── development/3.10.1
    ├── WP-D  test-row hygiene ......... land FIRST: later WPs' new tests are then born clean
    ├── WP-E  packaging & docs ......... independent (csproj metadata, docs)
    ├── WP-C  aggregates ............... independent (aggregate path, LINQ providers, new test table)
    ├── WP-A  dropped members .......... touches the 4 WhereClauseVisitors (VisitMember) and the 4 translators
    │                                    (TranslateDateMember)
    └── WP-B  Contains item fail-closed  touches the 4 translators (collection Contains)
          A and B may run in parallel; whichever merges second rebases. Both must land before F.
└── development/3.11  (cut from master after 3.10.1 ships)
    └── WP-F  captured values (I1-6) .... merges master, then its Task 6; flips WP-B's rows from "throws" to
          │                                "C#'s answer"
          ├── WP-G  LIKE / ToString ..... merges F (its plan's merge notes assume F lands first)
          └── WP-H  null semantics ...... needs F's RowIndependentEvaluator.TryReadValue
Unscheduled: D2, I, J, Appendix A.
```

**Conflict hotspots:**

| File(s) | WPs that touch them |
|---|---|
| `*/Visitors/*ExpressionTranslator.cs` (4) | A (date parts), B (Contains), F (343 lines each), G (86 each) |
| `*/Visitors/*WhereClauseVisitor.cs` (4) | A, H |
| `Funcular.Data.Orm.Core/DeletePredicateGuard.cs` and its tests | F (Task 6), G (its D5, already on master), H |
| Test `.csproj` `<Compile Include>` links (PG, MySQL, net48) | D, F, G (keep both sides) |
| `Changelog.md`, `Usage.md` | all (move parked entries out of `[3.10.0-beta1]` into the new version's section) |

### 2.1 Consolidated owner decisions (implementers must not decide these)

| ID | WP | Question | Recommendation |
|---|---|---|---|
| OD-0 | all | Do the 2026-10-05 exclusion criteria still govern? Is data loss an exception? | Yes, data loss is the exception: A and B qualify; F, G and H are judged on their merits. |
| OD-1 | A | Translate non-nullable `.Year/.Month/.Day`, or reject them too? | Translate them (reuse the nullable path's SQL); reject other member chains. |
| OD-2 | B | Ship a fail-closed hotfix in 3.10.1, or wait for F in 3.11? | Hotfix: it's small, and F replaces it. |
| OD-3 | C | Re-confirm §10's direction; the PostgreSQL `uuid` default depends on the Task 0 probe. | As recorded in operator plan §10. |
| OD-4 | D | net48 coverage: exempt it, or add coverlet? | Exempt. |
| OD-5 | D | `MySqlTestFixture.cs` is at 68.2 %, and every uncovered line is an existing environment fallback. Exempt it? | Exempt. |
| OD-6 | D | Wire the EF comparison class here or in D2? | D2. |
| OD-7 | E | Driver dependencies: declare them in the nuspec, or pin minimum versions and document them? | Pin and document. |
| OD-8 | E | `MySqlStringComparison` / `SqliteStringComparison`: fix the documents, or implement the switches? | Fix the documents; implementing is a feature. |
| OD-9 | F, G | Once translation is correct, should D9 keep rejecting those shapes in deletes? (Wildcard, null, empty and captured-property search values.) | Keep in 3.11.0. Relaxing them is a separate, small, reviewed change. |
| OD-10 | F, G | Both reject shapes that returned C#'s rows in 3.9. Accept that (with **Changed** entries), or narrow it? | Owner's call: it conflicts with "don't make 3.9-working shapes throw". |
| OD-11 | H | Design A or B; an opt-out switch; the release level. | Design B, with an opt-out, in a minor release. |
| OD-12 | I | Fix, or disclose as a Known issue? | At least disclose in 3.10.1. |
| OD-13 | J | When? | Owner. |

---

## 3. Work packages

### WP-A — WHERE drops a member of a member (date parts, `Length`, …)

**Problem.** In all four `WhereClauseVisitor.VisitMember`s, a member access whose receiver is itself a member of
the lambda parameter (`x.A.B`) falls through to "recurse into inner expression". That holds unless the shape is one
of the handled ones: a nullable `.Value.Year/.Month/.Day`, or the `.Value` and `.HasValue` guards. The fall-through
emits `x.A`'s column and silently drops `.B`. The SQL Server visitor shows it at the end of `VisitMember`
(`// otherwise recurse into inner expression`).

**Evidence on the released 3.10.0** (SQLite, Appendix B):

| Predicate | SQL sent | C# selects | Query returns | Delete removes |
|---|---|---|---|---|
| `x.PostedAt.Year > 2020` | `posted_at > @p` | 2 | 3 | 3 |
| `x.PostedAt.Year == 2021` | `posted_at = @p` | 1 | 0 | 0 |
| `x.Name.Length > 3` | `name > @p` | 2 | 4 | **4 (every row)** |

**History.**
- First recorded as OBS-1 in `PROVIDER_SCOPED_CACHES_PLAN.md` §9.16, and as V1 in `WHERE_CAPTURED_VALUES_PLAN.md`
  §8, whose D13 assigned it to a "date-part plan".
- The owner placed the fix in 3.10.0 (2026-10-02) and then took date parts out of 3.10.0 (2026-10-05).
- No plan or branch exists. 3.10.0's documents don't disclose it.

**Triage.**
- EF Core translates both kinds of shape.
- They never worked in ≤ 3.9: the results were silently wrong.
- **Deletes can remove more rows than C# selects**, so the data-loss rule applies.

**Owner decision.** OD-1.

**Scope.**
- Non-nullable `DateTime` members: `.Year`, `.Month` and `.Day` (what the nullable path supports).
- Every other member access on a member of the parameter, which is rejected.
- `DateTimeOffset`, `DateOnly` and further parts (`Hour`, `Date`, `DayOfWeek`) are rejected, not translated, unless
  the owner widens the scope.

**Out of scope:**
- ORDER BY (its own visitors) and projections, unless they share the changed code.
- Table qualification of date parts (`PROVIDER_SCOPED_CACHES_PLAN.md` §9.15, FVE-1).

**Design guidance (non-binding):**
- Extend each translator's `TranslateDateMember` to accept `x.D.Year` (receiver = a member of the parameter) as well
  as `x.D.Value.Year`, emitting the same SQL: `YEAR()`/`MONTH()`/`DAY()` on SQL Server, `EXTRACT(… FROM …)` on
  PostgreSQL and MySQL, `strftime` cast to integer on SQLite.
- In `VisitMember`, before the fall-through, throw `NotSupportedException` for any member access whose receiver
  reads the parameter and which isn't translated. Name the member, and advise comparing the column itself or
  computing the value before the query.

**Acceptance criteria.**
- **AC-A1.** On all four providers, `x.D.Year > n`, `x.D.Year == n`, `x.D.Month == m` and `x.D.Day == d` on a
  non-nullable `DateTime` member select exactly the rows LINQ to objects selects over the same rows. This holds by
  `Query` (sync and async), `Query<T>().Where(…)`, `Count(…)`, and `Delete`/`DeleteAsync` in a transaction. The
  data must discriminate: it includes a row in the boundary year (for example 2020 for `> 2020`), which a dropped
  part would get wrong.
- **AC-A2.** The SQL for AC-A1's shapes applies the dialect's date-part function to the column (pinned per
  provider).
- **AC-A3.** These throw `NotSupportedException` before any command is sent, by `Query`, `Count` and `Delete`
  (sync and async), on all four providers:
  - `x.Name.Length > 3`;
  - `x.D.Hour == 1`;
  - `x.D.Date == d`;
  - a member of a non-entity member (`x.Struct.Field`, if the test domain has one).

  A rejected delete logs no DELETE and leaves every row. The message names the member.
- **AC-A4 (unchanged).** These behave exactly as in 3.10.0, and existing tests pass unmodified (add a pin where
  none exists):
  - a nullable `.Value.Year/.Month/.Day`;
  - the `.Value`/`.HasValue` `NullableExpressionException` messages;
  - direct members, remote and computed properties;
  - captured member chains (`closure.Inner.Field`);
  - `DeletePredicateGuard`'s rows.
- **AC-A5 (documents).**
  - Changelog **Fixed**: date parts on non-nullable members.
  - Changelog **Changed**: other member chains now throw. List the shapes that happened to return C#'s rows in
    3.10.0, found by probe (for example `x.D.Date == d` when every stored time is midnight).
  - `Advanced.md` §5 and `Usage.md` updated.

**DoD (beyond §1.4).**
- Mutation: removing the date-part branch fails AC-A1.
- Mutation: removing the rejection fails AC-A3, and its delete row removes rows.

**Files.** `*/Visitors/*WhereClauseVisitor.cs` and `*/Visitors/*ExpressionTranslator.cs` (4 each). Tests go in a
shared harness linked into the four test projects, following the `DeleteGuardHarness` pattern.

### WP-B — A captured value used as a collection `Contains` item is read as a column

**Problem.** In `ids.Contains(req.Id)`, where `req` is a captured object, the item is translated as the entity's
`Id` **column**.

**Evidence on 3.10.0** (Appendix B): `x.Id == 3 || ids.Contains(req.Id)` with `req.Id = 99` and `ids = {1, 2}` is
sent as `id = @p OR id IN (@p1, @p2)`. It returns, and a delete removes, 3 rows; C# selects 1.

**History.**
- The I1-6 probe (`WHERE_CAPTURED_VALUES_PLAN.md` §1, executed on `35c6477`) recorded its siblings: a nested
  property, a static member, and a captured `Nullable<T>.Value`. Some are silent, some are server errors.
- 3.10.0's delete guard (D9) covers string calls only, so this shape gets through.

**Triage.**
- EF Core evaluates captured values.
- This never worked.
- Deletes can remove more rows than C# selects, so the data-loss rule applies.

**Owner decision.** OD-2. If the owner chooses "wait for F", drop this WP and pull WP-F into 3.10.1.

**Design (fail closed).** In each translator's collection-`Contains` translation, an item that doesn't reference the
lambda parameter throws `NotSupportedException` before any command is sent. The message says to evaluate the
`Contains` before the query (`var hit = ids.Contains(req.Id);`).

**Acceptance criteria.**
- **AC-B1.** On all four providers, by `Query`, `Count` and `Delete` (sync and async), a collection `Contains` whose
  item doesn't read the row throws `NotSupportedException` before any command is sent. That covers a captured
  object's property, a nested property, a static member, and a captured `Nullable<T>.Value`. A rejected delete
  leaves every row.
- **AC-B2 (unchanged).** These keep their 3.10.0 behavior (pinned):
  - `ids.Contains(x.Id)` and `!ids.Contains(x.Id)`;
  - an empty collection with a member item;
  - a nullable list with a nullable member;
  - C# 14's `MemoryExtensions.Contains` over an array with a member item.
- **AC-B3.** The plan records a probe of every item shape (member, local, constant, captured property, nested,
  static) on 3.10.0 and after the change. Any shape that returned C#'s rows on 3.10.0 and now throws is listed under
  **Changed**, and the owner is told.
- **AC-B4.** Changelog and `Usage.md` updated.

**DoD (beyond §1.4).** Mutation: removing the rejection fails AC-B1's delete rows.

**Superseded by WP-F.** F evaluates these items in C# instead of rejecting them, and F's plan must flip AC-B1's
rows from "throws" to "C#'s rows".

### WP-C — Aggregate correctness (publicly promised for 3.10.1)

**Problem.** The 3.10.0 Changelog's **Known issues (fixes planned for 3.10.1)**:
- `Average` of whole numbers truncates on SQL Server and loses precision on MySQL and SQLite;
- `Average` over a `decimal` or `float` column throws;
- `Min`/`Max`/`Average` over a nullable column on an empty set throw instead of returning `null`;
- some `Min`/`Max` result types throw after the round-trip.

**Sources.** The design is decided (owner, 2026-09-30) in `docs/plans/QUERY_OPERATOR_CORRECTNESS_PLAN.md` §10.
Fold in that plan's §9.2 r2 inputs: F3 (rule order for NULL results), F4 (type failures), F5 (precision), F6 (test
table) and F15 (collation-neutral strings, floating-point tolerance). Write a new `AGGREGATE_CORRECTNESS_PLAN.md`
with its own Task 0.

**Triage.** EF Core gets these right, and this is a commitment already in the public Changelog, so in scope.

**Owner decision.** OD-3 (re-confirm), plus the PostgreSQL `uuid` hard prerequisite below.

**Acceptance criteria.**
- **AC-C1.** `Average` gives LINQ to objects' value for `int`, `long`, `decimal`, `double` and `float`, nullable or
  not, on all four providers. It is computed as `SUM(wide) / COUNT`, with the widening keyed on the column type.
  SQLite's `ROUND(…, 10)` is gone, and MySQL's scale-4 `AVG` isn't used. Seed `{1, 1, 2}` rows; floating point
  compares within a stated tolerance.
- **AC-C2.** Empty and nullable inputs:
  - `Sum` gives 0, nullable selectors included.
  - A nullable or reference `Min`/`Max`/`Average` gives `null`.
  - A non-nullable one on an empty set throws `InvalidOperationException`, as LINQ does.
- **AC-C3.** `Min`/`Max` by result type:
  - **Enum:** `NotSupportedException`, suggesting `Max(x => (int)x.Status)`. Verify that the cast translates.
  - **`TimeSpan`:** rejected, with the guidance in §10 (store durations as integers).
  - **`Guid`:** rejected by default. The new provider-wide option `QueryOptions.GuidOrdering =
    GuidOrdering.Database` opts in to the database's ordering. PostgreSQL defaults to allowed **only if** Task 0
    shows that `uuid` `MIN`/`MAX` matches `Guid.CompareTo` on discriminating data; otherwise it is rejected, and the
    owner is told.
  - **`DateTimeOffset`:** allowed on SQL Server and PostgreSQL, rejected on MySQL and SQLite (verify first).
- **AC-C4.** The `Guid` guard doesn't cover `OrderBy`/`ThenBy`; the ordering difference is documented. String
  `Min`/`Max` follow the database's collation, documented, and tested with collation-neutral data.
- **AC-C5.** A new aggregate test table exists in every provider's schema scripts. It has `int`, `long`, `float`,
  `double` and `decimal` columns (nullable and not), plus `DateTime`, `string`, `Guid`, `DateTimeOffset`,
  `TimeSpan` and an enum.
- **AC-C6.** Documents:
  - the Changelog's Known issues move to **Fixed**;
  - `QueryOptions.GuidOrdering` is listed under **New public API**;
  - `Advanced.md` §5 and the aggregates row of `FUNKYORM_AI_ADVANCED.md` are updated.

  Closing or splitting GitHub #13 is the owner's call.

### WP-D — Test-row hygiene (land `fix/test-row-hygiene`)

**Problem.** Every run of the integration suites leaves rows behind, measured on 2026-10-03:
- `person` and related tables: SQL Server +177 (CI filter), PostgreSQL +87, MySQL +59, net48 +101.
- **Rule broken:** "integration tests leave databases as found".

**State.**
- **Branch:** `fix/test-row-hygiene`, in worktree `.claude/worktrees/infallible-maxwell-477798`. It was cut from
  `development/3.10` at `35c6477`, which is 81 commits behind master.
- **Review:** CLEAN at `9c37ac0` over `35c6477..9c37ac0`; its push-gate marker is in that worktree's git dir. It
  hasn't been pushed. Any new commit, including a merge, invalidates that marker.
- **Plan:** `docs/plans/TEST_ROW_HYGIENE_PLAN.md` rev 13 (on the branch). Its AC1–AC7 are authoritative.
- **Mechanism:** a shared `TestRowLedger` (`Funcular.Data.Orm.SqlServer.Tests/Shared/TestRowLedger.cs`, linked
  into the PostgreSQL, MySQL and net48 projects). It tracks inserts by primary key, deletes them newest first in
  TestCleanup after the provider is disposed, and fails a test that changes a guarded table's count. Test projects
  and documents only; no library code.

**Owner decisions.** OD-4, OD-5, OD-6.

**Re-entry steps.**
1. Get the owner's answers to OD-4, OD-5 and OD-6.
2. Merge `origin/master` (or `development/3.10.1`) into the branch, then resolve the conflicts:
   - Move its Changelog line out of `[3.10.0-beta1]` into the new version's section.
   - Keep both sides of the test csproj `<Compile Include>` links.
3. **Known merge gotcha.** `MySqlProviderScopedCacheTests.MapperCache_IsScoped_ThroughARealRead` is on master since
   the provider-scoped caches landed. It asserts that `Take(1)` returns a person, which only a leaked row
   satisfies, since MySQL's schema seeds none. It must now seed and `Track` its own row (plan §6).
4. Measure again on the merged code. Classes added to master since `35c6477` (the provider-scoped caches, the
   cold-cache tests, the delete guard's harness rows) weren't in the original audit. Run each suite twice and
   compare the guarded tables' counts. Wire any class that leaks, or record it in §6.
5. Re-run the plan's §4.3 mutations on the merged code. Rebuild after every restore before any `--no-build` run; a
   reviewer once leaked 68 rows by skipping that.
6. Get a non-author review of the merge commit, then hand the commit to the integrator.

**Additional acceptance criteria.**
- **AC-D8.** `MapperCache_IsScoped_ThroughARealRead` passes on an empty MySQL `person` table, and its row is
  tracked. Its PostgreSQL and SQLite namesakes (`PostgreSqlProviderScopedCacheTests`,
  `Caching/ProviderScopedCacheSqliteTests`) are checked for the same reliance on someone else's row.
- **AC-D9.** Every test class on master that inserts into a guarded table is measured, and wired or recorded.
  AC2 (unchanged counts on a second run) holds for all four suites on the merged code.

**Leave alone.** About 150 leftover rows from earlier red, mutation and reviewer runs stay in the shared databases,
recorded in the plan. Don't sweep them.

### WP-D2 — Hygiene follow-up (unscheduled)

These come from the hygiene plan's §6. Each needs its own small plan, and some need an owner decision.

- **`rls_demo`** (SQL Server +15 per run, PostgreSQL +4): RLS filters the delete, so cleanup needs each owner's
  session context or a role that bypasses RLS.
- **`non_identity_guid_entity` / `non_identity_string_entity`** (SQL Server 1 each, MySQL 1), and MySQL's `user`
  table (2).
- **The SQL Server `EntityFramework/EfSqlDataProviderIntegrationTests`** (outside the CI filter). They insert
  `person`, `address` and `person_address` rows and delete only the Delete tests' own.
- **Failure-path leaks:** QueryOperators' `SeedEmployer` records ids only after its third insert;
  `RichRelationshipTests` deletes only on success; `DeleteSeededRows` rolls everything back when one delete fails.
- **SQLite temp files:** eight classes delete their temp `.db` without clearing the pool, and
  `SqliteAuditContextTests` never deletes its two.
- **Fixed-name test tables** (`zz_*`, `funky_*`, `single_probe`) collide when two runs share a database.

### WP-E — Packaging and documentation truthfulness

These were found during the 3.10.0 release. 3.9.0 was identical, so none of them is a regression.

- **E1. Driver dependencies.**
  - The nuspec declares only `Microsoft.Data.SqlClient` and `System.ComponentModel.Annotations` (plus
    `System.Data.Common` on netstandard2.0). Yet the package bundles the PostgreSQL, MySQL and SQLite providers,
    which need `Npgsql`, `MySqlConnector` and `Microsoft.Data.Sqlite`.
  - The provider csprojs **float** those references (`Npgsql 9.*` / `8.0.*`, `MySqlConnector 2.*`,
    `Microsoft.Data.Sqlite 8.0.*`), so the minimum a consumer needs is whatever resolved at build time.
  - **Executed:** a consumer referencing Npgsql 9.0.3 got `FileNotFoundException: Npgsql, Version=9.0.5.0` when it
    loaded the PostgreSQL provider.
  - OD-7.
  - **AC-E1:**
    - Driver references are pinned to stated minimums (no floating).
    - The README's provider table names, for each provider, the driver package and minimum version to install.
    - A consumer smoke test at exactly those minimums loads every provider.
    - The nuspec's dependency list is as the owner decides.
- **E2. Documented switches that don't exist.** The 3.10.0 release review found these:
  - `README.md` (about line 478), `Changelog.md` (3.6.0 entry, about line 355) and
    `docs/ai-instructions/FUNKYORM_AI_INSTRUCTIONS_MYSQL.md` (about line 111) tell users to pass a
    `MySqlStringComparison.CaseSensitive` constructor option. No constructor accepts it, and nothing uses the enum.
  - `SqliteStringComparison` is stored by the SQLite translator but never read, yet it is documented as emitting
    `COLLATE NOCASE`.
  - OD-8.
  - **AC-E2:** no document claims an effect the code doesn't have. If the switches are kept, they are marked
    `[Obsolete]` with a message saying they have no effect, and a test pins that.
- **E3.** The SqlServer csproj's `<Description>` (the NuGet listing) says it includes the SQL Server, PostgreSQL and
  SQLite providers; it leaves out MySQL. **AC-E3:** the description names all four, and says `net48` carries SQL
  Server only.
- **E4. Carried review nits** (documents only):
  - In `Usage.md`'s `NotSupportedException` bullet, "as before" has no antecedent in an evergreen guide: write "as
    in 3.9".
  - Optionally, add to the collation sentence that SQLite's `LIKE` folds ASCII case whatever the collation.
  - `DELETE_GUARD_PLAN.md` rev 16 record nits: §9.16 ran "five mutants", the blame classes are the author's, and
    the cold figure is "in a Release build".
  - `SqlServerOrmDataProvider.cs` has two unresolvable XML `cref`s (CS1574 at about lines 2035 and 2083, seen with
    `GenerateDocumentationFile`).
  - **AC-E4:** each is fixed; a documentation build of Core and SqlServer reports no CS0419 or CS1574.

### WP-F — Captured values in WHERE method calls (land `fix/where-captured-values`, "I1-6")

**Problem.** The translators' method calls don't read captured values the way C# does:
- A captured object's property used as a collection `Contains` item is read as a column (WP-B).
- A captured `this` property used as a string `Contains` value is read as null, which sends `'%%'`.
- The receiver of `StartsWith`/`EndsWith` can be mistaken for a column.

**State.**
- **Branch:** in worktree `.claude/worktrees/epic-dirac-742683`, cut from `35c6477` (81 behind master).
- **Review:** CLEAN at `0b19ef2` over `35c6477..0b19ef2`, with its marker written; unpushed.
- **Plan:** `docs/plans/WHERE_CAPTURED_VALUES_PLAN.md` rev 14; its AC1–AC13 are authoritative.
- **Remaining:** its **Task 6** (the merge with the delete guard, plus AC9) isn't done.
- **Size:** 19 files, about 4.6k lines.
- **Design:** a public Core helper `Funcular.Data.Orm.Linq.RowIndependentEvaluator`. A row-independent `Contains`
  is evaluated in C# and sent as `1=0` / `NOT 1=0`. A null search value matches no row (D8, re-confirmed by the
  owner on 2026-10-03).

**What 3.10.0 changed underneath it.** D9 in the delete guard now rejects some of F's shapes in deletes:
- a string match whose search value is null or empty, or is a property of a captured object;
- a call on a value that doesn't read the row.

The guard's prose and its `IsContainsOfNull`, which F's Task 6 was to edit, also changed.

**Owner decisions.** OD-9 and **OD-10**. F's D16 rejects every captured `ToString()`, including
`objStr.ToString()` and `s.ToString(CultureInfo.InvariantCulture)`, which returned C#'s rows in 3.9. That conflicts
with "don't make 3.9-working shapes throw", so the owner must accept the **Changed** entries or narrow D16.

**Re-entry steps.**
1. Get the owner's answers to OD-9 and OD-10.
2. Merge master (or `development/3.11`). The conflicts are textual: the Changelog (move the entries to the new
   version's section), `Usage.md`, and the test csproj links. Master hasn't touched the four translators since
   `35c6477`.
3. Do Task 6 against the **current** guard, as follows.
   - Add AC9's rows.
   - Reconcile the guard's captured-value prose.
   - Remove `IsContainsOfNull` if it still applies.
   - Make D9's rows agree with OD-9.
   - If WP-B landed, flip B's rows to C#'s answers.
   - Fix the carried status-line nit ("FV5 … is next").
4. Run the full suites and coverage. Then get a non-author review of the merge plus Task 6, and fix-verifications
   until CLEAN.

**Additional acceptance criteria.**
- **AC-F14.** D9's delete rows on master agree with OD-9: either still rejected, or now deleting exactly C#'s
  rows. In both cases they are pinned on all four providers.
- **AC-F15.** If WP-B landed, its rows now give C#'s answer.
- **AC-F16.** Every shape that returned C#'s rows in 3.9 and now throws is listed under **Changed**, as OD-10
  decides.

### WP-G — LIKE escaping and a column's `ToString()` (land `fix/like-escaping-tostring`)

**Problem.**
- `Contains`/`StartsWith`/`EndsWith` send `%`, `_`, `[` (and a backslash, on some dialects) unescaped.
  `x.Name.Contains("%")` matches every non-null row.
- A column's `ToString()` is dropped: wrong rows on three providers, error 42883 on PostgreSQL.

3.10.0's D9 already rejects the wildcard, null and empty search values **in deletes**. Queries still return extra
rows.

**State.**
- **Branch:** cut from the delete guard at `ca82741`, which predates D9; 15 commits behind master. There is no
  worktree now.
- **Review:** CLEAN at `75e172f` over `ca82741..75e172f`; no marker written; unpushed.
- **Plan:** `docs/plans/LIKE_ESCAPING_TOSTRING_PLAN.md` rev 11; its AC1–AC12 are authoritative. Its status line
  still says fix-verification 4 is next; update it.
- **Design:**
  - D1–D2: a public `Funcular.Data.Orm.Linq.LikePattern` with `ESCAPE '!'` on every dialect.
  - D4: an integer column's `ToString()` becomes a cast to invariant text. Any other column type, and any
    `ToString` argument, throws `NotSupportedException`. `x.N.Value.ToString()` throws `NullableExpressionException`.

**Owner decisions.**
- OD-9.
- **OD-10:** D4 rejects shapes that returned C#'s rows in 3.9. Examples: `x.Code.ToString(CultureInfo.InvariantCulture)`
  on SQL Server, MySQL and SQLite; `x.N.Value.ToString()` on three providers; a `Guid` compared with its lower-case
  text.

**Re-entry steps** (after F is in the integration branch).
1. Merge F, following the plan's §5 Task 4 merge notes:
   - the order is F's evaluator, then G's `.Value` check, then G's column cast or rejection, then F's rejection;
   - update F's `ToString_Column` pin;
   - append `ESCAPE '!'` to F's `TranslatorDirectHarness` expected SQL;
   - keep D4's message distinct from F's message prefix.
2. Merge master. Conflicts:
   - `DeletePredicateGuard.cs` and its tests: master already reads through `ToString()` in `MemberChain` (the same
     change as G's D5), so take master's;
   - `DELETE_GUARD_PLAN.md`;
   - the Changelog and `Usage.md`.
3. Make D9's wildcard rows agree with OD-9.
4. G's drafted Known issue about SQLite's `Guid`/`DateTime` binding never shipped. Carry it to WP-I, or ship it
   here.
5. Run the full suites and coverage (the plan's translator baselines need re-measuring after the merge). Get a
   non-author review and fix-verifications.

**Additional acceptance criteria.**
- **AC-G13.** D9's wildcard delete rows agree with OD-9 on all four providers.
- **AC-G14.** Both F's and G's rows pass on the merged code. No `TranslatorDirectHarness` row still expects
  unescaped LIKE text.
- **AC-G15.** Shapes that now throw are listed under **Changed**, as OD-10 decides.

### WP-H — SQL-vs-C# null semantics

**Problem.** SQL's three-valued logic differs from C#. These sources record it:
- `QUERY_OPERATOR_CORRECTNESS_PLAN.md` §8, "SQL NULL semantics";
- `WHERE_CAPTURED_VALUES_PLAN.md` §8, `All`;
- the stood-down session's notes.

In a plain WHERE, a null row is wrong only where C# says **true**:
- `c != v`;
- `c == capturedNull`, which is sent as `c = @p` (only a literal null becomes `IS NULL`);
- `==`/`!=` between two nullable columns.

Under any `NOT`, a null row is wrong where C# says **false**. That includes `All`'s `NOT (p)`, as well as `==`,
`>`, `<` and `!ids.Contains(x.N)` (`NOT IN`). String columns count as nullable.

Example: `Count(x => x.NullableId != 5)` gives 3 where C# gives 4.

**Triage.** EF Core compensates by default (with an opt-out), and FunkyORM never did. Not data loss today: deletes
remove *fewer* rows. **Fixing it widens deletes**: `Delete(x => x.Status != "Archived")` starts deleting
null-status rows. That is C#'s answer, but more than 3.9 deleted, so it needs a minor release and a prominent
**Changed** entry.

**Owner decision.** OD-11.
- **Design A:** always emit two-valued SQL. It's simple, but it changes the SQL of the commonest shape.
- **Design B (preferred):** track the NOT depth and compensate only where it matters. `All` then sends `Not(p)`
  through the visitor instead of wrapping the SQL text (the four `*LinqQueryProvider.cs`).

**Stacks on F.** It needs `RowIndependentEvaluator.TryReadValue`, and it flips F's `allNullableGreaterThanZero`
Known-issue row.

**Acceptance criteria.**
- **AC-H1.** Query, `Count`, `Any`, `All` and `Delete` give LINQ to objects' rows on all four providers. This
  covers every comparison operator on nullable columns (strings included), a captured null, and nullable
  column-to-column comparisons. It holds in a plain WHERE and under `NOT`, `All` and `NOT IN`, with seeded nulls.
- **AC-H2.** An opt-out (if OD-11 includes one) restores 3.10's SQL byte for byte, pinned per provider.
- **AC-H3.** The Changelog's **Changed** entry leads with the wider-delete example. F's Known-issue row is removed
  and its pin flipped.

### WP-I — SQLite `Guid`/`DateTime` fidelity (unscheduled; owner's call)

These are pre-existing, and recorded in the operator plan's §8.
- **Binding:** SQLite's WHERE binds `Guid` and `DateTime` in the driver's formats, not FunkyORM's storage formats
  (lower-case `D` Guid text; `yyyy-MM-dd HH:mm:ss.fff`). So `Where(p => p.UniqueId == g)` misses a Guid with hex
  letters, a `.500` ms date finds no row, and `> d` includes the row equal to `d`.
- **Culture:** SQLite writes and reads dates in the current culture. Under fi-FI the stored text is `03.04.05`;
  under ar-SA the year is Hijri.
- **`DateTimeOffset`:** SQLite can't read a `DateTimeOffset` property back.

The fix needs a tolerant read or a migration note, because databases written under such cultures hold
culture-formatted text. **OD-12.** At minimum, a 3.10.1 Known issue (the text drafted on G's branch can be reused).

### WP-J — Strong naming (owner to schedule)

The owner deferred this past 3.10.0 on 2026-09-30, and asked to be consulted before it is scheduled. The shipping
assemblies stay `SignAssembly=False`.

When it is scheduled:
- every `InternalsVisibleTo` friend must be signed, and its entry public-key-qualified;
- the owner supplies the key;
- the DoD adds a strong-name verification of every DLL in the package, and a consumer smoke test on .NET Framework.

---

## 4. Not in this backlog

- **The 3.11 write guards proposal** (UpdateWhere compare-and-swap): a separate proposal awaiting the owner, not
  something deferred from 3.10. Its second item, a `[ReadModel]` attribute, was withdrawn on 2026-10-20 (owner: an
  agent proposal, not part of the framework's design).
- **The sponsored-tutorial creator brief:** it was on hold until #12/#13 shipped. 3.10.0 shipped them, so it is now
  unblocked (owner's call).

---

## Appendix A — Recorded pre-existing issues, not scheduled

Sources are on master unless a branch is named. Each needs an owner decision before work starts.

| # | Issue | Source |
|---|---|---|
| 1 | The ORDER BY visitors' `OrderByClause` getter writes "Generated ORDER BY clause" to stdout (all four) | operator plan §8 |
| 2 | `GenerateOrderByClause` is dead code (three providers) | operator plan §8 |
| 3 | SQL Server: `OrderBy(computedSubquery).Select(…).Distinct()` fails ("ORDER BY items must appear in the select list") | operator plan §8 |
| 4 | The net48 test project should become SDK-style (`dotnet build` leaves out `System.Memory.dll`) | operator plan §8 |
| 5 | PostgreSQL: an enum WHERE parameter throws `InvalidCastException` (reading enums works) | operator plan §8 |
| 6 | `.HasValue` on a captured or static value in an ORDER BY ternary is read as an entity column | operator plan §8 |
| 7 | An ORDER BY ternary comparison that the compiler wraps in `ConvertChecked` (`char`, `byte`, `short`, `int` vs `long`) throws | operator plan §8 |
| 8 | Unordered default paging hard-codes `id` (wrong for other key names) | operator plan §8 |
| 9 | PostgreSQL sorts NULLs last (LINQ first); `NULLS FIRST`/`LAST` is a candidate | operator plan §8; `Advanced.md` §5 |
| 10 | A predicate or ordering after a subset `Select` reads base columns, not the projected values | operator plan §8 |
| 11 | Over an interface-typed source, interface members don't get the entity's mapped column | operator plan §8 |
| 12 | A generic helper constrained to an interface (`Convert(x, IHasId).Id`) fails in every WHERE translator | operator plan §8 |
| 13 | `is IOrderedQueryable` sort helpers crash on composed queries | GitHub #16 |
| 14 | PostgreSQL WHERE timestamps depend on whether Npgsql or FunkyORM starts first (`EnableLegacyTimestampBehavior`) | operator plan §8 |
| 15 | SQL Server: an ORDER BY ternary with a constant test that selects a constant branch fails (errors 408/1008) | operator plan §8 |
| 16 | SQL Server `DateTime` under a day-first session language is read day-first | operator plan §8 |
| 17 | SQLite provider state isn't safe for concurrent execution from one root | operator plan §8 |
| 18 | A collection holding null drops it from `IN (…)` (fewer rows) | I1-6 plan §8 (branch F) |
| 19 | The `StringComparison` overloads and `Enumerable.Contains` comparers are ignored (the server decides) | I1-6 plan §8; LIKE plan §6 |
| 20 | A `[NotMapped]` member used as an item or receiver is sent as a column (server error) | I1-6 plan §8 |
| 21 | The visitor drops the inner exception, and a closure property read can let `TargetInvocationException` escape | I1-6 plan §8 |
| 22 | `req == null \|\| ids.Contains(req.Id)` with `req` null still throws (both sides are evaluated) | I1-6 plan §8 |
| 23 | SQL Server: a supplementary-plane character in a `LIKE` pattern matches every row under `SQL_Latin1_General_CP1_CI_AS` (a collation behavior: §1.1's collation rule) | LIKE plan §6 (branch G) |
| 24 | A `CHAR(n)` column's padding breaks `EndsWith` on SQL Server and PostgreSQL; `LIKE` patterns are limited to 8,000 bytes on SQL Server (escaping can double a value's length) | LIKE plan §6 |
| 25 | Date-part translation uses the unqualified column (ambiguous with joins) | caches plan §9.15 FVE-1 |
| 26 | SQLite base without a `rowid` and one remote join: unordered paging fails | Changelog 3.10.0 |

## Appendix B — Evidence for WPs A and B (executed 2026-10-05)

- **Probe:** a console app referencing the **published** `Funcular.Data.Orm 3.10.0` from nuget.org, with an
  isolated packages folder.
- **Data:** a throwaway SQLite file holding `probe_row (id, name, posted_at)` with rows (1, `abcd`, 2019-06-01),
  (2, `xyz`, 2021-06-01), (3, `abcdefg`, 2022-01-01) and (4, `ab`, 2020-06-01).
- **Method:**
  - "C# selects" is LINQ to objects over the same four rows.
  - "Delete removes" is the return value of `Delete` inside a transaction that was rolled back.
  - The SQL is from the provider's `Log`.

```
SQL: SELECT id, name, posted_at FROM probe_row WHERE probe_row.posted_at > @p__linq__0
non-nullable date part: x.PostedAt.Year > 2020
    C# selects 2 | Query returns 3 | Delete removes 3 (rolled back)
SQL: SELECT id, name, posted_at FROM probe_row WHERE probe_row.posted_at = @p__linq__0
non-nullable date part: x.PostedAt.Year == 2021
    C# selects 1 | Query returns 0 | Delete removes 0 (rolled back)
SQL: SELECT id, name, posted_at FROM probe_row WHERE probe_row.name > @p__linq__0
member of a member: x.Name.Length > 3
    C# selects 2 | Query returns 4 | Delete removes 4 (rolled back)
SQL: SELECT id, name, posted_at FROM probe_row WHERE (probe_row.id = @p__linq__0 OR probe_row.id IN (@p__linq__1,@p__linq__2))
captured property as Contains item: x.Id == 3 || ids.Contains(req.Id)
    C# selects 1 | Query returns 3 | Delete removes 3 (rolled back)
```

Only SQLite was executed here.
- On master, all four `VisitMember` methods end in the same fall-through: `Visit(node.Expression)`, at line 225 in
  the PostgreSQL, MySQL and SQLite visitors.
- WP-B's shapes were recorded on all four providers by the I1-6 probe at `35c6477`.
- WP-A's shapes were recorded earlier on SQLite only (OBS-1, V1).

A WP's Task 0 re-executes every shape on all four providers.

## Appendix C — Branch and worktree inventory (2026-10-05)

| Branch | Head | Worktree | Pushed | State |
|---|---|---|---|---|
| `development/3.10` | `ec4b46c` | main checkout | yes | Released as 3.10.0 (master `6d2f3fa`). Keep it; don't delete. |
| `fix/test-row-hygiene` | `9c37ac0` | `.claude/worktrees/infallible-maxwell-477798` | no | CLEAN; WP-D |
| `fix/where-captured-values` | `0b19ef2` | `.claude/worktrees/epic-dirac-742683` | no | CLEAN; Task 6 open; WP-F |
| `fix/like-escaping-tostring` | `75e172f` | none | no | CLEAN; WP-G |
| `fix/delete-guard-trivial-predicates`, `fix/provider-scoped-caches`, `fix/mysql-delete-cold-cache` | — | — | — | Merged into 3.10.0; kept |

Worktrees are never removed unless the owner asks.
