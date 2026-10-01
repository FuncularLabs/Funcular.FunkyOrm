# Delete on a cold column cache — Implementation Plan

> **Goal:** make `Delete<T>`/`DeleteAsync<T>`, by predicate and by id, work when type `T` hasn't been mapped yet in
> the process. Also, no `XxxOrmDataProvider` path may cache an unmapped-property set before `T` is discovered.
> - Today a predicate delete throws `NotSupportedException: Expression type Parameter is not supported`, and the
>   type stays broken for the rest of the process.
> - Branch `fix/mysql-delete-cold-cache`, from `master` (`fae4472`, 3.9.0), per release-branch alignment. It doesn't
>   target `development/3.10`; see Task 4.
> - Recorded in the 3.10 plan's §8, "MySQL `Delete<T>(predicate)` on a cold column cache".

> **Status (2026-10-01):** rev 3, the test plan after the Task 0 re-review (§9.2). Nothing is implemented yet.

> **Revision 3 — what changed (re-review R1–R9):**
> - The matrix and mutations are per provider. The SQLite rows that can't go red→green at `fae4472` are dropped
>   (R1).
> - AC7 asserts the discovery error and uses a `[Table]` type (R2).
> - D2's scope is narrowed to the provider classes, and the guarded sites are listed (R3).
> - DDL ordering (R4); the `[SqlExpression]` row moved to `person` (R5); direct helper rows and a member→tests table
>   (R6).
> - D3 comes after the transaction guard (R7); construction details (R8); prose (R9).

## 1. Verified premises (author by reading `fae4472`; Task 0 reviewers executed, §9.1–§9.2)

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
  - One helper per provider: the cached set when `T` is in `_mappedTypes`, otherwise the set computed without
    caching.
  - It is used at every `_unmappedPropertiesCache.GetOrAdd` site in the provider classes:
    - MySQL `822, 851, 1228, 1288, 1325, 1502`;
    - PostgreSQL `844, 873, 1206, 1266, 1291, 1467`;
    - SQL Server `1440, 1481, 1812, 1897, 1951, 2234`;
    - SQLite `784, 959, 1051, 1080, 1236`.
  - Not covered (§6): the LINQ providers' `[NotMapped]`-only lambdas (5 per provider), reachable cold only through
    a LINQ provider's public constructor, and Core's uncalled `GetUnmappedProperties<T>()` (`OrmDataProvider.cs:415-423`).
- **D3 — Discover first in delete by id.** `DiscoverColumns<T>()` is the first statement after the transaction guard
  in `Delete<T>(long id)` and `DeleteAsync<T>(long id)`, in all four providers.
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
- **AC7:** A cold delete whose discovery fails reports the discovery error, caches no unmapped set and no column
  names, and the next call succeeds once the table exists. (`[Table]` types; an unattributed type's table-name
  fallback is §6.)
- **AC8:** A cold `GenerateWhereClause<T>` discovers `T` before translating. On SQL Server, MySQL and PostgreSQL it
  renders `T`'s own snake_case columns, including inside a `[SqlExpression]` token.
- **AC9:** A cold `Delete<T>(id)`/`DeleteAsync<T>(id)` with a convention primary key that isn't `Id` deletes the row,
  on SQL Server, MySQL and PostgreSQL.

## 4. Test plan

### 4.1 AC → test matrix (`ColdCacheDeleteTests` per provider)

| AC | Test | SqlServer | MySql | PostgreSql | Sqlite |
|---|---|---|---|---|---|
| AC1, AC3 | `Delete_ByInheritedMember_OnAColdCache_DeletesTheRow_AndTheTypeStaysQueryable` | ✓ | ✓ | ✓ | ✓ (property-named table) |
| AC2, AC3 | `DeleteAsync_ByInheritedMember_…` | ✓ | ✓ | ✓ | ✓ |
| AC1 | `Delete_ByMethodCallPredicate_OnAColdCache_DeletesTheRow` (`LastName.StartsWith(marker)`) | ✓ | ✓ | ✓ | ✓ |
| AC6 | `ExecProcedureFirst_ThenQueryAndDelete_Work` (`sp_get_person_by_id`; SQL Server `@person_id`, MySQL `p_person_id`) | ✓ | ✓ | — (throws) | — (throws) |
| AC6 | `UnmappedHelper_UndiscoveredType_IsComputedNotCached_ThenCachedAfterDiscovery` (direct) | ✓ | ✓ | ✓ | ✓ |
| AC7 | `ColdDelete_WhenDiscoveryFails_ReportsIt_CachesNothing_AndTheNextCallWorks` | — | ✓ (inner `MySqlException.Number == 1146`) | — | ✓ (`no such table`) |
| AC8 | `GenerateWhereClause_Cold_DiscoversFirst` (direct; asserts no `NotSupportedException`, and `T` is mapped afterwards) | ✓ | ✓ | ✓ | ✓ |
| AC8 | `GenerateWhereClause_Cold_RendersSnakeCaseColumns` (incl. `[SqlExpression("COALESCE({LastName}, '')")]` on `person`) | ✓ | ✓ | ✓ | — (§1 key mismatch) |
| AC9 | `DeleteById_Cold_WithAConventionKeyNotNamedId_DeletesTheRow` (sync and async; table `zz_cold_pk`) | ✓ | ✓ | ✓ | — (§1 key mismatch) |
| AC4 | the two named tests, each run alone, plus the D1-removed mutant | — | on `development/3.10` (Task 4) | — | — |

**Construction.**
- **Entity types**, all with `[Table]`, used only here:
  - `ColdDeletePerson` over `ColdDeleteBase` (declares `Id`, `LastName`; the derived class declares `FirstName`);
  - the same shape for `ColdDeleteAsync…`, `ColdMethodCall…` and `ColdWhere…`;
  - `ColdProcPerson`;
  - `ColdRetryRow` (`[Table("zz_cold_retry")]`);
  - `ColdComputedPerson` (the `[SqlExpression]` row);
  - `ColdPkRow` (`[Key] ZzProbePkId` over `zz_cold_pk`).
- **Seeding:** rows are seeded and removed with raw SQL.
- **Server tables:** they use `person` (snake_case columns, which defeat naive-name alternatives to D1).
- **SQLite:** one temp-file database per class, with **property-named** columns (`Id`, `FirstName`, `LastName`).
  - Created in `ClassInitialize`; `ClassCleanup` calls `SqliteConnection.ClearAllPools()`, then deletes the file.
- **DDL ordering** (R4). Raw DDL runs only while no provider transaction is open:
  - AC7: tx1 → attempt 1 → **rollback** → create the table → tx2 → attempt 2 → commit → drop.
  - AC9: create → test → drop.
  - Raw MySQL connections set `lock_wait_timeout` and `innodb_lock_wait_timeout` to 10 s.
- **Coldness precondition.** Each test first asserts, through the provider's internal accessors (each provider grants
  its test project `InternalsVisibleTo`), that its type isn't in the unmapped cache and no column key for its types
  exists.
- **Assertions:**
  - the delete returns 1;
  - the survivors are exactly `kept`;
  - `Id`, `LastName` and `FirstName` are populated.
- **Red runs:** each red row is run alone on `fae4472`, and its message recorded.

### 4.2 Mutations each key test must kill (per provider)

| Mutation | Killed by | On |
|---|---|---|
| No D1 | AC1/AC2 rows; AC8 `DiscoversFirst` | all 4 |
| D1 after the unmapped `GetOrAdd` | AC1/AC2 rows | all 4 |
| D1 after `ResolveRemoteJoins` | AC8 `[SqlExpression]` row | SQL Server, MySQL, PostgreSQL |
| Discovery in `Delete`/`DeleteAsync` only | AC8 `DiscoversFirst` | all 4 |
| Discovery wrapped in a swallowing try/catch | AC7 (attempt 1 must report the discovery error) | MySQL, SQLite |
| No D2 guard | AC6 (`ExecProcedure`-first: SQL Server, MySQL; direct helper: all 4) | all 4 |
| No D3 | AC9 | SQL Server, MySQL, PostgreSQL |

### 4.3 Member → tests, and coverage

| Member | Tests |
|---|---|
| Unmapped helper (×4) | AC6 direct; AC6 `ExecProcedure` (SQL Server, MySQL) |
| `GenerateWhereClause<T>` (×4) | AC8 rows; AC1/AC2 |
| `Delete<T>(long)` / `DeleteAsync<T>(long)` (×4) | AC9 (server providers); the existing delete-by-id tests |

Per-file line coverage (coverlet, deduplicated cobertura) for the four touched provider files, at base and at HEAD.
`MySqlOrmDataProvider.cs` is at 83.3 % at base; AC9 covers its 28 delete-by-id lines. A file below 85 % is reported
with its baseline for an owner waiver.

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

1. **Task 0** — test-plan review: rev 1 and rev 2 NOT CLEAN (§9.1–§9.2). Rev 3 is re-reviewed.
2. **Task 1** — red tests (§4.1); each red row is run alone on `fae4472`, and its message recorded.
3. **Task 2** — D1–D3 in all four providers; green; mutations (§4.2); suites (§4.4); coverage (§4.3).
4. **Task 3** — Changelog "Fixed"; hostile review and fix-verification to CLEAN.
5. **Task 4 — Merge path (owner).** A merge to `master` runs `ci.yml`'s `publish` (NuGet push), so nothing lands on
   `master` without a release decision:
   - **(a)** 3.9.1: version bump, PR into `master`, publish; or
   - **(b)** merge it forward into `development/3.10` only.

   Either way:
   - AC4 runs on `development/3.10`.
   - Once `fix/provider-scoped-caches` D5 lands (keys by FullName; instance `GetUnmappedProperties`), D1–D3 and the
     cold tests are re-verified against it, and the dropped SQLite AC8/AC9 rows are added as snake_case red→green
     rows.

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
  - A corrupted set would mean recomputing on every call.
  - Pre-existing; a concurrent set is planned in `fix/provider-scoped-caches`.
- **`[SubqueryAggregate]`/`[JsonCollection]` source-type columns** are still resolved without discovering the source
  type (MySQL `661-665`, `696-700`). AC8 covers `T`'s own columns only.
- **`GenerateOrderByClause<T>`** is dead code; it is left unchanged.

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
