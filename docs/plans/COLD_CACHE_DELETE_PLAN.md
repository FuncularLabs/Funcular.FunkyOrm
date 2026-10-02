# Delete on a cold column cache — Implementation Plan

> **Goal:** make `Delete<T>`/`DeleteAsync<T>`, by predicate and by id, work when type `T` hasn't been mapped yet in
> the process. Also, no `XxxOrmDataProvider` path may cache an unmapped-property set before `T` is discovered.
> - Today a predicate delete throws `NotSupportedException: Expression type Parameter is not supported`, and the
>   type stays broken for the rest of the process.
> - Branch `fix/mysql-delete-cold-cache`, from `master` (`fae4472`, 3.9.0), per release-branch alignment. It doesn't
>   target `development/3.10`; see Task 4.
> - Recorded in the 3.10 plan's §8, "MySQL `Delete<T>(predicate)` on a cold column cache".

> **Status (2026-10-01):** rev 10, the test plan after the ninth Task 0 review (§9.9).
> - The §9.6 reviewer ran the implementer's own tests:
>   - every expected-red row was red at the seam for its stated reason, and every other row was green there;
>   - every row was green with a faithful D1–D3;
>   - every §4.2 mutant was killed on its listed providers;
>   - the §4.3 faithful-build numbers reproduced exactly.
> - Task 1 landed the pass-through seam at `dd121c5`. The rev 4 and rev 5 rows are written (uncommitted) and each
>   was run alone there:
>   - every expected-red row is red for its stated reason;
>   - the guard rows, the no-match row and the SQLite execution row are green;
>   - with the class included, only expected-red rows fail in the four full suites.
> - The rev 5 reviewer's own rows and faithful D1–D3: every row was green, and every §4.2 mutant was killed on
>   its listed providers except "D3 before the guard", which survived a guard row on a never-created table with a
>   type-only assertion (§9.5 G1). The implementer's guard rows kill it (§9.6).

> **Revision 10 — what changed (re-check K1–K2):** documents only. The reviewer's text, verbatim, for the §9.8 J1
> row (K1) and the rev 5 bullet (K2).

> **Revision 9 — what changed (re-check J1–J2):** documents only. The guard-coverage bullet takes the reviewer's
> text verbatim (J1); the Status block is split so each bullet keeps its own subject (J2). The rev 5 reviewer's
> bullet now states its one exception (found by the author).

> **Revision 8 — what changed (re-check I1–I3):** documents only. The guard-line clause (I1); the Status bullet
> scoped to what was run (I2); H2's deferral tracked in Task 2 (I3).

> **Revision 7 — what changed (re-review H1–H2):** documents only. Which guard rows cover a line (H1). H2 is a
> test comment, to be reworded in Task 2's commit.

> **Revision 6 — what changed (re-review G1–G4):** documents only.
> - The guard rows' types, existing table and asserted message are named. These are the rows Task 1 built (G1).
> - The no-match row's type, key form and class are named (G2).
> - The coverage metric is defined, with the measured faithful numbers and margin (G3).
> - D3's error-shape change is scoped to the server providers, and the §4.3 gap sentence corrected (G4).
> - The rev 3 and rev 4 reviewers ran a faithful D1–D3 sketch: every row went red→green and every listed mutant
>   was killed.

> **Revision 5 — what changed (re-review F1–F5):**
> - An async missing-table row; both missing-table rows check coldness after the rollback (F1).
> - Guard rows: a cold delete by id outside a transaction doesn't discover (F3). They also cover base-uncovered
>   SQL Server lines, with a no-match row (F2).
> - The missing-table types named (F4); stale prose (F5).

> **Revision 3 — what changed (re-review R1–R9):**
> - The matrix and mutations are per provider. The SQLite rows that can't go red→green at `fae4472` are dropped
>   (R1).
> - AC7 asserts the discovery error and uses a `[Table]` type (R2).
> - D2's scope is narrowed to the provider classes, and the guarded sites are listed (R3).
> - DDL ordering (R4); the `[SqlExpression]` row moved to `person` (R5); direct helper rows and a member→tests table
>   (R6).
> - D3 comes after the transaction guard (R7); construction details (R8); prose (R9).

## 1. Verified premises (author by reading `fae4472`; Task 0 reviewers executed, §9.1–§9.4)

- **How the unmapped set goes wrong.**
  - `GenerateWhereClause<T>` reads `_unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>)`
    (`MySqlOrmDataProvider.cs:822`, `PostgreSqlOrmDataProvider.cs:844`, `SqlServerOrmDataProvider.cs:1440`,
    `SqliteOrmDataProvider.cs:784`).
  - `GetUnmappedProperties<T>` counts as unmapped every property with no `[Column]` and no column-cache entry
    (`MySqlOrmDataProvider.cs:1486-1493`, and the same in the others).
  - On a cold cache, every convention-mapped property is unmapped. The visitor then visits the lambda parameter and
    throws (`MySqlWhereClauseVisitor.cs:150 → 223-225 → 66`).
  - `GetOrAdd` keeps the wrong set, so `Query<T>()` on that type then throws too (executed).
- **The live gap is Delete/DeleteAsync by predicate, in all four providers.**
  - `CreateSelectQueryObject` discovers through `GetColumnNames`; `Query<T>()` discovers.
  - `GenerateOrderByClause<T>` has only a commented-out caller.
- **Two more paths reach the cache cold.**
  - **Unmapped cache:** `ExecProcedure<T>` doesn't discover, but its mapper fills the unmapped cache (MySQL `992-1001`,
    `1227-1228`).
    - Executed on MySQL: without a guard, the poisoned set is cached, and `Query`/`Delete` then throw. With the guard
      (D2), `Query` works.
    - PostgreSQL's `ExecProcedure<T>` throws (`:958-968`), and SQLite inherits a throwing default.
  - **Column names:** `Delete<T>(long id)`/`DeleteAsync<T>(long id)` don't discover (MySQL `357-372`, `175-190`).
    - A convention key not named `Id` fails cold on the server providers (executed on MySQL).
- **Order inside `GenerateWhereClause`.** `ResolveRemoteJoins` resolves `T`'s own `[SqlExpression]` tokens through
  `GetCachedColumnName`, which caches a naive name cold (MySQL `649/751 → 1590-1599`; SQL Server `ComputeColumnName`
  gives `[organizationid]`). So discovery must come first.
- **SQLite can't use discovered names (a separate defect).**
  - The base `GetCachedColumnName` keys on `DeclaringType.FullName` (`OrmDataProvider.cs:381-389`), while discovery
    writes `ToDictionaryKey()` keys (`Sqlite:920-921`).
  - So on SQLite, snake_case convention columns fail cold or warm, and `[SqlExpression]` tokens render naive names.
  - A fix is *planned* on `development/3.10` (`PROVIDER_SCOPED_CACHES_PLAN.md` D5; plan only so far).
- **Coldness.**
  - Column keys use the declaring type's simple name and an ignore-underscore-and-case comparer.
  - No test project parallelises. `SqlServer.Tests.NetFramework/test.runsettings` sets only the adapter path.
- **`_mappedTypes` as the "discovered" signal.**
  - Its `Add` follows the column-key writes in every provider (MySQL 1191, PostgreSQL 1169, SQL Server 1719,
    SQLite 924), and only that marker skips discovery.
  - A type used only with `ExecProcedure` is never mapped. Its set is then recomputed once per result shape: the
    mapper is cached per `FullName|schema`.

## 2. Decisions

- **D1 — Discover first in the predicate path.** `DiscoverColumns<T>()` is the first statement of
  `GenerateWhereClause<T>` in all four providers, before `ResolveRemoteJoins` and the unmapped cache.
- **D2 — No provider-class path caches an unmapped set for an undiscovered type.**
  - One helper per provider, `UnmappedPropertiesFor<T>()`: the cached set when `T` is in `_mappedTypes`, otherwise
    the set computed without caching.
  - It is used at every `_unmappedPropertiesCache.GetOrAdd` site in the provider classes:
    - MySQL `822, 851, 1228, 1288, 1325, 1502`;
    - PostgreSQL `844, 873, 1206, 1266, 1291, 1467`;
    - SQL Server `1440, 1481, 1812, 1897, 1951, 2234`;
    - SQLite `784, 959, 1051, 1080, 1236`.
  - Not covered (§6): the LINQ providers' `[NotMapped]`-only lambdas (5 per provider), reachable cold only through
    a LINQ provider's public constructor, and Core's uncalled `GetUnmappedProperties<T>()` (`OrmDataProvider.cs:415-423`).
- **D3 — Discover first in delete by id.** `DiscoverColumns<T>()` is the first statement after the transaction guard
  in `Delete<T>(long id)` and `DeleteAsync<T>(long id)`, in all four providers.
  - **Changed error shape (C10), on SQL Server, MySQL and PostgreSQL:** a cold delete by id on a missing table now
    throws discovery's `InvalidOperationException`, with the provider exception as `InnerException`, the same
    shape as a predicate delete. Before, the raw provider exception was thrown. Pinned by a row; Changelog
    "Changed". SQLite's discovery doesn't wrap, so there it is the raw `SqliteException` before and after
    (executed) *(rev 6, G4)*.
  - **On SQLite**, D3 is behaviour-neutral at `fae4472`: the base `GetCachedColumnName` keys on FullName. It is
    kept for consistency, with an execution row (C9).
- **D4 — No visitor change.**

## 3. Acceptance criteria

- **AC1:** On a cold cache, `Delete<T>(predicate)` on a convention-mapped inherited member deletes exactly the
  matching rows, on all four providers.
- **AC2:** The same for `DeleteAsync<T>(predicate)`.
- **AC3:** A cold delete leaves the type usable: a following `Query<T>()` returns the surviving row with `Id`,
  `LastName` and `FirstName` populated.
- **AC4:** The two MySQL tests in the 3.10 plan's §8 pass when run alone, on `development/3.10` (Task 4):
  - `MySqlOrderByQualificationTests.ComputedMemberOrderBy_EmitsExpression_Unchanged`;
  - `ComputedAttributeEntityWithoutJoins_OwnColumnOrder_Unqualified`.
- **AC5:** No regressions in the suites of §4.4.
- **AC6:** No provider-class path caches an unmapped set before the type is discovered. Shown by `ExecProcedure<T>`
  as the first use, then `Query<T>` and `Delete<T>`, on SQL Server and MySQL. On all four providers, by a direct
  helper row.
- **AC7:** On all four providers, a cold delete whose discovery fails reports the discovery error, caches no
  unmapped set and no column names, and the next call succeeds once the table exists. (`[Table]` types; an
  unattributed type's table-name fallback is §6.)
- **AC8:** A cold `GenerateWhereClause<T>` discovers `T` before translating. On SQL Server, MySQL and PostgreSQL it
  renders `T`'s own snake_case columns, including inside a `[SqlExpression]` token.
- **AC9:** A cold `Delete<T>(id)` and, separately, a cold `DeleteAsync<T>(id)` delete the row on SQL Server, MySQL and
  PostgreSQL. The key is a `[Key]` whose snake_case column (`zz_probe_pk_id`) is convention-mapped.
  - On a missing table, sync and async, the error has D3's shape, and nothing is cached for the type.
  - Without a transaction, a cold delete by id (sync and async, all four providers) throws the transaction guard's
    error and doesn't discover the type *(rev 5, F3)*.

## 4. Test plan

### 4.1 AC → test matrix (`ColdCacheDeleteTests` per provider)

| AC | Test | SqlServer | MySql | PostgreSql | Sqlite |
|---|---|---|---|---|---|
| AC1, AC3 | `Delete_ByInheritedMember_OnAColdCache_DeletesTheRow_AndTheTypeStaysQueryable` | ✓ | ✓ | ✓ | ✓ (property-named table) |
| AC2, AC3 | `DeleteAsync_ByInheritedMember_…` | ✓ | ✓ | ✓ | ✓ |
| AC1 | `Delete_ByMethodCallPredicate_OnAColdCache_DeletesTheRow` (`LastName.StartsWith(marker)`; survivors read through `Query<T>()`) | ✓ | ✓ | ✓ | ✓ |
| AC6 | `ExecProcedureFirst_ThenQueryAndDelete_Work` (`sp_get_person_by_id`; SQL Server `@person_id`, MySQL `p_person_id`) | ✓ | ✓ | — (throws) | — (throws) |
| AC6 | `UnmappedHelper_UndiscoveredType_IsComputedNotCached_ThenCachedAfterDiscovery` (direct, type `ColdHelperPerson`) | ✓ | ✓ | ✓ | ✓ |
| AC7 | `ColdDelete_WhenDiscoveryFails_ReportsIt_CachesNothing_AndTheNextCallWorks` | ✓ (inner `SqlException.Number == 208`) | ✓ (inner `MySqlException.Number == 1146`) | ✓ (inner `PostgresException.SqlState == 42P01`) | ✓ (`no such table`) |
| AC8 | `GenerateWhereClause_Cold_DiscoversFirst` (direct; **member-access** predicate `p.LastName == x`; asserts no `NotSupportedException`, and that `T` is in `_mappedTypes` afterwards, observed through a test-only derived provider) | ✓ | ✓ | ✓ | ✓ |
| AC8 | `GenerateWhereClause_Cold_RendersSnakeCaseColumns` (incl. `[SqlExpression("COALESCE({LastName}, '')")]` on `person`) | ✓ | ✓ | ✓ | — (§1 key mismatch) |
| AC9 | `DeleteById_Cold_WithASnakeCaseKey_DeletesTheRow` (type `ColdPkRow`) | ✓ | ✓ | ✓ | — (§1 key mismatch) |
| AC9 | `DeleteByIdAsync_Cold_WithASnakeCaseKey_DeletesTheRow` (type `ColdPkAsyncRow`) | ✓ | ✓ | ✓ | — (§1 key mismatch) |
| AC9 | `DeleteById_Cold_MissingTable_ThrowsTheDiscoveryError` (type `ColdMissingPkRow`; D3's shape; coldness re-checked after the rollback) | ✓ | ✓ | ✓ | — |
| AC9 | `DeleteByIdAsync_Cold_MissingTable_ThrowsTheDiscoveryError` (type `ColdMissingPkAsyncRow`; as above) *(rev 5, F1)* | ✓ | ✓ | ✓ | — |
| AC9 | `DeleteById_Cold_WithoutTransaction_ThrowsTheGuard_AndDoesNotDiscover` (type `ColdPkNoTxRow` on an **existing** table; asserts the exact guard message, that the type is still cold including `_mappedTypes`, and that ids 1 and 2 survive) *(rev 5, F3; rev 6, G1)* | ✓ | ✓ | ✓ | ✓ |
| AC9 | `DeleteByIdAsync_Cold_WithoutTransaction_ThrowsTheGuard_AndDoesNotDiscover` (type `ColdPkNoTxAsyncRow`; as above) | ✓ | ✓ | ✓ | ✓ |
| coverage | `DeleteByIdAsync_Cold_NoMatchingRow_ReturnsFalse` (type `ColdPkNoMatchAsyncRow`, key `Id` on column `id`; green at `fae4472`; covers a line, kills no mutant) *(rev 5, F2; rev 6, G2)* | ✓ | — | — | — |
| D3 (SQLite) | `DeleteById_Cold_SqliteSyncAndAsync_Execute` (property-named key; equivalent change at `fae4472`, coverage only) | — | — | — | ✓ |
| AC4 | the two named tests, each run alone, plus the D1-removed mutant | — | on `development/3.10` (Task 4) | — | — |

**Construction.**
- **Entity types**, all with `[Table]`, used only here:
  - `ColdDeletePerson` over `ColdDeleteBase` (declares `Id`, `LastName`; the derived class declares `FirstName`);
  - the same shape for `ColdDeleteAsync…`, `ColdMethodCall…` and `ColdWhere…`;
  - `ColdProcPerson`;
  - `ColdRetryRow` (`[Table("zz_cold_retry")]`);
  - `ColdComputedPerson` (the `[SqlExpression]` row);
  - `ColdPkRow` and `ColdPkAsyncRow` (each `[Table("zz_cold_pk")]`, `[Key] ZzProbePkId`), over
    `zz_cold_pk (zz_probe_pk_id INT PRIMARY KEY, label …)`. A column named like the property would make the row
    vacuous (C2).
  - `ColdHelperPerson` (direct helper row).
  - `ColdMissingPkRow` and `ColdMissingPkAsyncRow` (each `[Table("zz_cold_missing")]`, never created) *(rev 5, F4)*.
  - `ColdExecPkRow` and `ColdExecPkAsyncRow` (`zz_cold_exec_pk`, SQLite's execution row), kept apart from `ColdPkRow`
    so the snake_case SQLite AC9 rows (Task 4) can reuse that name.
  - `ColdPkNoTxRow` and `ColdPkNoTxAsyncRow` (`zz_cold_pk_notx`, created and seeded with ids 1 and 2: snake_case key
    on the server providers, property-named on SQLite).
    - The table must exist. On a never-created table, "D3 before the guard" fails discovery, which also throws
      `InvalidOperationException` and leaves the type cold, so a type-only assertion would let that mutant survive
      (executed, §9.5 G1).
  - `ColdPkNoMatchAsyncRow` (`zz_cold_pk_nomatch (id INT PRIMARY KEY)`, SQL Server).
    - The key is named like its column. A snake_case key would be red at the seam, since a cold delete uses the
      naive key name.
  - Table names are fixed, so two simultaneous runs against one database would collide. Runs are serial (§1).
- **Seeding:** rows are seeded and removed with raw SQL.
- **Server tables:** they use `person` (snake_case columns, which defeat naive-name alternatives to D1).
- **SQLite:** one temp-file database per class, with **property-named** columns (`Id`, `FirstName`, `LastName`).
  - Created in `ClassInitialize`; `ClassCleanup` calls `SqliteConnection.ClearAllPools()`, then deletes the file.
- **DDL ordering** (R4, C7). Raw DDL runs only while no provider transaction is open, and a killed run must not
  poison the next one:
  - AC7: `DROP TABLE IF EXISTS` → tx1 → attempt 1 → **rollback** → create the table → seed one row → tx2 →
    attempt 2 → commit.
  - AC9: `DROP TABLE IF EXISTS` → create → test.
  - Missing-table rows: `DROP TABLE IF EXISTS` → tx → attempt → rollback → coldness check.
  - Guard and no-match rows: `DROP TABLE IF EXISTS` → create → seed → the call → assertions.
  - Both drop in `finally`, after disposing the provider.
  - Raw MySQL connections set `lock_wait_timeout` and `innodb_lock_wait_timeout` to 10 s.
- **Coldness precondition.** Each test first asserts, through the provider's internal accessors (each provider grants
  its test project `InternalsVisibleTo`), that its type isn't in the unmapped cache, and that no column key for its
  types exists. The keys are built as `DeclaringType.Name + "." + Name`, i.e. `ToDictionaryKey()`.
  `_mappedTypes` is `protected static`, so it is reached through a test-only derived provider.
- **Assertions:**
  - the delete returns 1;
  - the survivors are exactly `kept`;
  - `Id`, `LastName` and `FirstName` are populated.
- **Red runs:** each red row is run alone, and its message recorded per provider. The expected reds:
  - the Parameter `NotSupportedException` for the member-access rows;
  - the naive column for the method-call row on the server providers: `Unknown column 'person.lastname'`, `Invalid
    column name 'lastname'`, `42703`;
  - on SQLite, the method-call row reads survivors through `Query<T>()`, so its red is `near "FROM": syntax error`;
  - the naive key column for the AC9 rows;
  - for the missing-table rows, the raw provider exception (SQL Server `SqlException` 208, MySQL
    `MySqlException` 1146, PostgreSQL `PostgresException` 42P01) instead of D3's `InvalidOperationException`.
- **Not red at base:** the guard rows and the no-match row are green at `fae4472`.
  - The guard rows kill mutants (§4.2).
  - Only SQL Server's async guard row covers a line no other row covers: its throw at `:337` *(rev 7, H1; rev 8,
    I1)*. Every other guard's lines are reached by other rows too. A one-line guard is reached by any row that
    calls the method (the AC9 rows, or SQLite's execution row). SQL Server's sync throw `:894` is covered at base
    by `Delete_ById_ThrowsIfNoTransaction` *(rev 9, J1)*.
  - The no-match row only covers a line (§4.3).
  - The direct helper rows can fail only once the pass-through seam exists (`dd121c5`), since the helper
    (`protected internal`) doesn't exist at `fae4472` *(rev 5, F5)*.

### 4.2 Mutations each key test must kill (per provider)

| Mutation | Killed by | On |
|---|---|---|
| No D1 | AC1/AC2 rows; AC8 `DiscoversFirst` | all 4 |
| D1 after the unmapped `GetOrAdd` | AC1/AC2 rows | all 4 |
| D1 after `ResolveRemoteJoins` | AC8 `[SqlExpression]` row | SQL Server, MySQL, PostgreSQL |
| Discovery in `Delete`/`DeleteAsync` only | AC8 `DiscoversFirst` | all 4 |
| Discovery wrapped in a swallowing try/catch | AC7 (attempt 1 must report the discovery error) | all 4 |
| No D2 guard | AC6 (`ExecProcedure`-first: SQL Server, MySQL; direct helper: all 4) | all 4 |
| No D3 | AC9 rows | SQL Server, MySQL, PostgreSQL |
| D3 in only one of `Delete`/`DeleteAsync(long)` | the AC9 row of the other | SQL Server, MySQL, PostgreSQL |
| D3 wrapped in a swallowing try/catch (each method) | that method's missing-table row *(rev 5, F1)* | SQL Server, MySQL, PostgreSQL |
| D3 before the transaction guard (each method) | that method's guard row *(rev 5, F3)* | all 4 |

Notes:
- The swallowing-try/catch row is killed on all four providers (AC7 now runs on all four).
- The SQLite method-call row kills no single-decision mutant: it fails only without both D1 and D2.
- "Helper bypassed at the mapper site only" is equivalent on PostgreSQL and SQLite: every mapper caller discovers
  first.

### 4.3 Member → tests, and coverage

| Member | Tests |
|---|---|
| Unmapped helper (×4) | AC6 direct; AC6 `ExecProcedure` (SQL Server, MySQL) |
| `GenerateWhereClause<T>` (×4) | AC8 rows; AC1/AC2 |
| `Delete<T>(long)` / `DeleteAsync<T>(long)` (×4) | AC9 rows (server providers, sync and async separately); the guard rows (all 4); SQLite's execution row; SQL Server's no-match row |

Per-file line coverage (coverlet, deduplicated cobertura), at base `fae4472` (executed by the rev 3 reviewer):

| File | Base |
|---|---|
| `MySqlOrmDataProvider.cs` | 83.3 % |
| `SqlServerOrmDataProvider.cs` | 84.9 % |
| `PostgreSqlOrmDataProvider.cs` | 86.6 % |
| `SqliteOrmDataProvider.cs` | 89.3 % |

Uncovered at base:
- `Delete`/`DeleteAsync(long)` on MySQL (0/28);
- `DeleteAsync(long)` on PostgreSQL and SQLite (0/14 each).

The AC9 and missing-table rows cover these gaps on MySQL and PostgreSQL. On SQLite, the execution row covers
`DeleteAsync(long)` (15/15); the guard rows add no line there, since the guard is one line
(`SqliteOrmDataProvider.cs:206`) *(rev 7, H1)*. AC7 calls the predicate delete and never
reaches `Delete(long)` *(rev 6, G4)*.

**The metric** *(rev 6, G3)*. A file's coverage is the distinct line numbers across **every** cobertura
`<class filename=…>` element for that file, a line counting as covered if any element gives it hits. Async
methods compile to their own state-machine classes, so the `line-rate` attribute of the provider's main class
element alone understates the file: for SQL Server at HEAD it reads 84.78 %, against 85.14 % by this metric.

**SQL Server.** The rev 4 rows cover no line that is uncovered at base (§9.4 F2). The rev 5 rows add two:
- the async guard row covers the throw at `SqlServerOrmDataProvider.cs:337`;
- the no-match row covers the log line at `:351`.

Measured by the rev 5 reviewer with a faithful D1–D3 (all 265 SqlServer.Tests passing):

| Helper body | Covered / lines | Rate | Margin over 85 % |
|---|---|---|---|
| expression-bodied | 1077 / 1265 | 85.14 % | 1 line |
| block-bodied | 1079 / 1267 | 85.16 % | 2 lines |

MySQL at HEAD measured 939 / 1082 = 86.78 %. The floor is 85 % per file at HEAD, with no pre-arranged waiver. Task 2
records the real number. If it falls below the floor, the fix is a row for another base-uncovered SQL Server line,
not a waiver.

### 4.4 Suites, where they run

| Project | Command | CI on a PR into `master` |
|---|---|---|
| SqlServer.Tests | `dotnet test` (FUNKY_CONNECTION) | yes (`ci.yml`, LocalDB) |
| MySql.Tests | `dotnet test` (FUNKY_MYSQL_CONNECTION; Inconclusive without it) | yes |
| PostgreSql.Tests | `dotnet test` | no (its workflow triggers on `main`); local |
| Sqlite.Tests | `dotnet test` | no workflow; local |
| SqlServer.Tests.DotNet9 | `dotnet test` | local |
| SqlServer.Tests.NetFramework | build `FunkyORM.sln`, run the dll | local |

Local runs are recorded with their sha.

## 5. Tasks

1. **Task 0** — test-plan review: revs 1–9 NOT CLEAN (§9.1–§9.9). Rev 10 is re-checked on its diff.
2. **Task 1** — red tests (§4.1).
   - The pass-through seam is committed at `dd121c5`: `protected internal UnmappedPropertiesFor<T>()` at every listed
     site.
   - Each red row was run alone at `dd121c5` and its message recorded.
   - The rev 5 rows are added, and each is run alone, before Task 2.
3. **Task 2** — D1–D3 in all four providers; green; mutations (§4.2); suites (§4.4); coverage (§4.3); H2's test
   comment reworded (§9.6) *(rev 8, I3)*.
4. **Task 3** — Changelog "Fixed"; hostile review and fix-verification to CLEAN.
5. **Task 4 — Merge path (owner).** A merge to `master` runs `ci.yml`'s `publish` (NuGet push), so nothing lands on
   `master` without a release decision:
   - **(a)** 3.9.1: version bump, PR into `master`, publish; or
   - **(b)** merge it forward into `development/3.10` only.

   Either way:
   - AC4 runs on `development/3.10`.
   - Once `fix/provider-scoped-caches` lands, D1–D3 and the cold tests are re-verified against it, and the dropped
     SQLite AC8/AC9 rows are added as snake_case red→green rows.
     - Its D5 keys by FullName.
     - Its D7 removes the `_mappedTypes` and `_unmappedPropertiesCache` statics, so the D2 helper and the coldness
       preconditions are **rewritten** there against the scoped caches, not just re-verified.
   - Changelog "Changed": D3's error shape on SQL Server, MySQL and PostgreSQL.

## 6. Out of scope (recorded)

- **SQLite key mismatch (§1).** A fix is planned on `development/3.10`, not on the 3.9.x line.
- **The LINQ providers' `[NotMapped]`-only lambdas** (5 per provider; e.g. `SqliteLinqQueryProvider.cs:246`).
  - Through `Query<T>()` they run after discovery.
  - Through a LINQ provider's public constructor on a cold type they cache an empty set first, and that type then
    fails for the process (executed on SQLite).
  - Pre-existing; a follow-up candidate.
- **Core's `GetUnmappedProperties<T>()`** (`OrmDataProvider.cs:415-423`): unguarded and uncalled.
- **Table-name fallback.** For a type without `[Table]`, `ResolveTableName` caches the unmatched fallback before
  discovery fails (MySQL `1524-1526`, `1581`). A table created later is never found in that process. Pre-existing.
- **`_mappedTypes` is a plain `HashSet`** read and written concurrently, and D2 adds readers.
  - A false "not discovered" is harmless: the set is computed without caching.
  - A corrupted set would mean recomputing on every call, and a `HashSet` modified concurrently can also throw.
  - Pre-existing; a concurrent set is planned in `fix/provider-scoped-caches`.
- **`[SubqueryAggregate]`/`[JsonCollection]` source-type columns** are still resolved without discovering the source
  type (MySQL `661-665`, `696-700`). AC8 covers `T`'s own columns only.
- **`GenerateOrderByClause<T>`** is dead code. D2 rewrites its unmapped reads to the helper (MySQL 851,
  PostgreSQL 873, SQL Server 1481); it is otherwise unchanged.

## 9. Review dispositions

### 9.1 Task 0 review of rev 1 (`f730612`)

NOT CLEAN: F1–F11, N1–N4. Dispositioned in rev 2, and re-checked in §9.2.

### 9.2 Task 0 re-review of rev 2 (`a3391fe`; non-author; D1/D2 applied and probed on SQLite and MySQL)

Verdict: NOT CLEAN.
- **Resolved:** F6, F8, F9, F10, F11, N2–N4.
- **Partial:** F1, F2, F4, F5, F7, N1.
- **Not fixed:** F3.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| R1 | major | TEST-GAP | The SQLite AC8 `[SqlExpression]` and AC9 rows can't go red→green (SQLite key mismatch). | Per-provider matrix; those SQLite rows dropped, then added in Task 4 after D5; mutation kills scoped per provider. |
| R2 | major | TEST-GAP | Under D2, AC7 no longer killed the swallow mutant; "caches nothing" was false without `[Table]`. | AC7 asserts the discovery error; `[Table]`; reworded; the table-name fallback is in §6. |
| R3 | minor | TEST-GAP | D2 isn't an "any path" chokepoint: the LINQ public-constructor path and Core's dead method. | Goal, D2 and AC6 scoped to provider classes; guarded sites listed; the rest in §6. |
| R4 | minor | PLAN-GAP | Raw DDL under an open provider transaction locks or hangs. | DDL ordering and lock timeouts (§4.1). |
| R5 | minor | PLAN-GAP | The PostgreSQL schema has no `project` table. | `[SqlExpression]` on `person`. |
| R6 | minor | TEST-GAP | D2's uncached branch is untested on PostgreSQL and SQLite; no member→tests table. | Direct helper rows; §4.3 table. |
| R7 | nit | PLAN-GAP | D3 did I/O before the transaction guard. | "After the guard". |
| R8 | nit | PLAN-GAP | Construction details. | §4.1. |
| R9 | nit | PLAN-GAP | Prose: "fixes" should be "planned"; thread safety; runsettings; AC8's scope. | §1, §6, AC8. |

### 9.3 Task 0 re-review of rev 3 (`b1ce507`; non-author; reviewer's own D1–D3 sketch, every row run alone at base and on the sketch, mutants M1–M9, coverage, AC4 on `development/3.10`)

Verdict: NOT CLEAN on construction. The design is confirmed: every §4.1 row is red at `fae4472` and green with a
faithful D1–D3; every listed mutant is killed; AC4 holds.
- **Resolved:** R1, R3, R4 (happy path), R5, R6, R7, R9.
- **Partial:** R2, R8, F5, F7, N1.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| C1 | major | TEST-GAP | AC9's sync and async halves shared one type, so "D3 only in `Delete`" survived. | Two rows, two types; mutation added. |
| C2 | minor | TEST-GAP | AC9's key column name was unspecified; a property-named column made it vacuous. | `zz_probe_pk_id`; AC9 reworded. |
| C3 | minor | TEST-GAP | AC8 `DiscoversFirst`'s predicate and observer were unfixed: the method-call path warms a naive key. | Member-access predicate; `_mappedTypes` through a derived provider; key construction named. |
| C4 | minor | TEST-GAP | The method-call row's red reasons differ per provider; on SQLite it needs a `Query<T>()` read and kills no single mutant. | Messages per provider; SQLite read through `Query<T>()`; §4.2 note. |
| C5 | minor | TEST-GAP | AC7 was tested on MySQL and SQLite only; the swallow mutant survived on SQL Server and PostgreSQL. | AC7 rows on all four. |
| C6 | minor | PLAN-GAP | The direct-helper rows couldn't compile at base; the helper was unnamed. | `UnmappedPropertiesFor<T>()`; Task 1 lands it as an unguarded pass-through (the "No D2" body), so the row is red by assertion; own type. |
| C7 | minor | HOUSE-RULE | The DDL had no pre-clean; a killed run poisoned the next. | `DROP TABLE IF EXISTS` first; drop in `finally`; AC7 seeds a row. |
| C8 | minor | HOUSE-RULE | Coverage: SQL Server is below the floor at base. | Four baselines recorded; rows cover the gaps; no pre-arranged waiver. |
| C9 | minor | TEST-GAP | The §4.3 delete-by-id cell was partly false; SQLite D3 is behaviour-neutral. | Cell corrected; SQLite execution row recorded as equivalent. |
| C10 | minor | PLAN-GAP | D3 changes a public error shape. | Recorded in D3; pinned by a row; Changelog. |
| C11 | nit | PLAN-GAP | §6 said `GenerateOrderByClause` was unchanged, but D2 rewrites its sites. | §6 corrected. |
| C12 | nit | PLAN-GAP | The provider-scoped D5/D7 interaction; `HashSet` can throw. | Task 4, §6. |

### 9.4 Task 0 re-review of rev 4 (`43fa1f2`; non-author; own D1–D3 sketch; rows run alone at base and on the sketch on all four providers; coverage)

Verdict: NOT CLEAN.
- **Resolved:** C1–C7, C9, C11, C12.
- **Partial:** C8 (F2), C10 (F1).

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| F1 | minor | TEST-GAP | The async missing-table row was missing. A swallowing try/catch around the async D3 survived every row and the MySQL suite, and left a naive key cached. | Async row (own type); both rows re-check coldness; mutation row. |
| F2 | minor | HOUSE-RULE | SQL Server: the rows covered no base-uncovered line. The floor depended on how the helper was written (85.004 % or 84.96 %). | Async guard row (`:337`) and no-match row (`:351`); executed 1090 → 1092. |
| F3 | nit | AC-GAP | "After the transaction guard" was unpinned: a "D3 before the guard" mutant passed the SQL Server suite. | AC9 amended; guard rows, sync and async, on all four; mutation row. |
| F4 | nit | PLAN-GAP | The missing-table row's type, table, DDL ordering and expected red were unspecified. | Named (Task 1's `ColdMissingPkRow` on `zz_cold_missing`), ordering and expected exceptions listed. |
| F5 | nit | PLAN-GAP | Stale prose: the swallow row's "On" cell, the Task 0 line, and "red on `fae4472`" for the helper rows. | Corrected; the helper's access level stated. |

### 9.5 Task 0 re-review of rev 5 (`46da2c8`; non-author; own D1–D3 sketch with mutant switches; every row written fresh; faithful-build coverage, both helper forms)

Verdict: NOT CLEAN.
- **Resolved:** F1, F2 (under the defined metric), F4 (missing-table rows), F5.
- **Partial:** F3 (G1).

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| G1 | minor | TEST-GAP | Fix-introduced. The guard rows named no table or assertion. On a never-created table with an exception-type-only assertion, "D3 before the guard" survived on SQL Server, MySQL and PostgreSQL. | Each guard row has its own type on an existing, seeded table and asserts the exact guard message. These are the rows Task 1 built. |
| G2 | nit | PLAN-GAP | The no-match row had no type or key; it is green at the seam only with a key named like its column, and it kills no mutant. | `ColdPkNoMatchAsyncRow`, key `Id`; it is labelled coverage-only. |
| G3 | minor | HOUSE-RULE | "Deduplicated cobertura" was undefined. The main class's `line-rate` reads 84.78 %, and the plan's "1090 → 1092" came from a sketch. | The metric is defined; the faithful numbers and the 1–2 line margin are recorded; below the floor means a row, not a waiver. |
| G4 | nit | PLAN-GAP | D3's error-shape change doesn't apply on SQLite (raw `SqliteException` before and after); AC7 never reaches `Delete(long)`. | D3, the Changelog item and §4.3 scoped. |

### 9.6 Task 0 re-review of rev 6 (`44745bd` plus the uncommitted Task 1 tests; non-author; four exports: seam, faithful D1–D3 in both helper forms, mutant switches; 51 rows alone on each; coverlet)

Verdict: NOT CLEAN on two nits.
- **Resolved:** G1, G2 and G3.
- **Partial:** G4 (H1).
- **Tests vs plan:** no row is missing, vacuous or misclassed, and every §4.2 mutant is killed on the listed
  providers, plus the reviewer's own "D3 late" mutant.
- **Faithful build:** the full suites pass (SQL Server 265, MySQL 108, PostgreSQL 156, SQLite 176), and every
  touched file is at least 85 % (SQL Server 85.14 %, MySQL 86.78 %, PostgreSQL 88.88 %, SQLite 90.83 %).

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| H1 | nit | PLAN-GAP | Fix-introduced: "the async guard row adds its throw line" on SQLite was false. The guard is one line, already covered by the execution row. Only SQL Server's async guard covers a line. | §4.1 and §4.3 corrected. |
| H2 | nit | HOUSE-RULE | The SQL Server test file's comment called the no-match row a guard row. | To be reworded in Task 2's commit, with the tests (§5 Task 2). |

### 9.7 Narrow re-check of rev 7 (`44745bd..e4ce919`; non-author; committed content and the §9.6 coverage outputs, read-only)

Verdict: NOT CLEAN on three nits, all in the rev 7 text. H1's measured claims are true.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| I1 | nit | PLAN-GAP | Fix-introduced: "`:337`, the only guard whose throw is on its own line" is false. SQL Server's sync guard throws on `:894`, already covered at base by `Delete_ById_ThrowsIfNoTransaction`. | The reviewer's wording. |
| I2 | nit | PLAN-GAP | Fix-introduced. The Status bullet said every row was red at the seam (10 are green by design); it dropped "on the listed providers"; and it claimed the §4.3 base numbers were reproduced, though only the faithful-build numbers were. | The reviewer's wording. |
| I3 | nit | PLAN-GAP | H2's deferral read as done, and §5's Task 2 didn't carry it. | "To be reworded"; Task 2 lists it. |

### 9.8 Narrow re-check of rev 8 (`e4ce919..2aef8c6`; non-author; committed content and the §9.6 artifacts, read-only)

Verdict: NOT CLEAN on two nits, both from rev 8's own rewording. I1–I3 resolved.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| J1 | nit | PLAN-GAP | Fix-introduced: "Only SQL Server's async guard row also covers a line" read as exclusive against base. MySQL's two guards and the PostgreSQL and SQLite async guards run lines that are uncovered at base, though other rows reach them too. | The reviewer's replacement text, verbatim. |
| J2 | nit | PLAN-GAP | Fix-introduced: the lead-in "The §9.6 reviewer ran …" was the parent of every Status bullet, including two that reviewer didn't run. | That reviewer's results are nested under their own bullet. |

### 9.9 Narrow re-check of rev 9 (`2aef8c6..a104d48`; non-author; committed content, the §9.6 artifacts, and the §9.5 and §9.8 reports, read-only)

Verdict: NOT CLEAN on two nits, neither of which would mislead an implementer. J1 and J2 resolved.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| K1 | nit | PLAN-GAP | The §9.8 J1 row dropped "async": PostgreSQL's and SQLite's sync guards are covered at base. | The reviewer's text. |
| K2 | nit | PLAN-GAP | "Every row turned green" implied red to green; the guard and no-match rows were already green. | "Every row was green". |
