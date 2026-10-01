# Delete on a cold column cache — Implementation Plan

> **Goal:** make `Delete<T>`/`DeleteAsync<T>`, by predicate and by id, work when type `T` hasn't been mapped yet in the
> process. Also, no path may cache a wrong unmapped-property set before `T` is discovered.
> - Today a predicate delete throws `NotSupportedException: Expression type Parameter is not supported`, and the
>   type stays broken for the rest of the process.
> - Branch `fix/mysql-delete-cold-cache`, from `master` (`fae4472`, 3.9.0), per release-branch alignment. It doesn't
>   target `development/3.10`; see Task 4 for the merge path.
> - Recorded in the 3.10 plan's §8, "MySQL `Delete<T>(predicate)` on a cold column cache".

> **Status (2026-10-01):** rev 2, the test plan after the Task 0 review (§9.1). Nothing is implemented yet.

> **Revision 2 — what changed:** the §1 callers corrected; D1 is the first statement; D2 adds the unmapped-cache guard
> and D3 discovery for delete by id; AC6–AC9 added; the SQLite test uses its own property-named table; a coldness
> precondition; direct `GenerateWhereClause` rows; suites and coverage named; release mechanics. Dispositions: §9.1.

## 1. Verified premises (author by reading `fae4472`; Task 0 reviewer executed, §9.1)

- **How the unmapped set goes wrong.**
  - `GenerateWhereClause<T>` reads `_unmappedPropertiesCache.GetOrAdd(typeof(T), GetUnmappedProperties<T>)`
    (`MySqlOrmDataProvider.cs:822`, `PostgreSqlOrmDataProvider.cs:844`, `SqlServerOrmDataProvider.cs:1440`,
    `SqliteOrmDataProvider.cs:784`).
  - `GetUnmappedProperties<T>` counts as unmapped every property with no `[Column]` and no column-cache entry
    (`MySqlOrmDataProvider.cs:1486-1493`, and the same in the others).
  - On a cold cache, every convention-mapped property is unmapped. The visitor then visits the lambda parameter and
    throws (`MySqlWhereClauseVisitor.cs:150 → 223-225 → 66`).
  - `GetOrAdd` keeps the wrong set: after the failure, `Query<T>()` on that type throws too (executed).
- **The live gap is Delete/DeleteAsync by predicate, in all four providers.**
  - `CreateSelectQueryObject` discovers through `GetColumnNames` (MySQL `801→770→1501`, SQL Server
    `1405→1360→2233`, PostgreSQL `792→1466`, SQLite `764→733→1235`).
  - The LINQ path discovers in `Query<T>()`.
  - `GenerateOrderByClause<T>` reads the unmapped cache without discovery, but it has no callers.
- **Two more paths reach the cache cold.**
  - **Unmapped cache:** `ExecProcedure<T>` never discovers (`MySqlOrmDataProvider.cs:992-1001`), but its row mapper
    fills the unmapped cache (`:1227-1228`).
    - Executed on MySQL and SQL Server: `ExecProcedure<T>` as the first use of a convention type caches
      {Id, FirstName, LastName} as unmapped. After that, `Query<T>()` and `Delete<T>(predicate)` throw.
  - **Column names:** `Delete<T>(long id)`/`DeleteAsync<T>(long id)` don't discover (MySQL `357-372`, `175-190`).
    - Executed: a convention primary key `ZzProbePkId` over column `zz_probe_pk_id` fails, cold, with
      `Unknown column 'ZzProbePkId'`. A key named `Id` is unaffected.
- **Order inside `GenerateWhereClause`.** `ResolveRemoteJoins` resolves `T`'s own columns for `[SqlExpression]` tokens
  and for the parent key of `[SubqueryAggregate]`/`[JsonCollection]` (MySQL `649/751`, `666-667`, `701-702`). So
  discovery must precede it.
  - Executed, cold `COALESCE({OrganizationId}, 0)`: discovery after it gives `Unknown column
    'project.OrganizationId'`; discovery first gives `COALESCE(project.organization_id, 0)`.
- **SQLite can't show this fix with snake_case columns (a separate defect).** SQLite doesn't override
  `GetCachedColumnName`.
  - The base method keys on `DeclaringType.FullName` (`OrmDataProvider.cs:381-389`), while discovery writes
    `ToDictionaryKey()` (`DeclaringType.Name`) keys.
  - So SQLite never uses discovered names: a warm `Query` of a snake_case convention type emits
    `SELECT Id, FirstName, LastName` and fails (executed at `fae4472`).
  - `fix/provider-scoped-caches` (D5) fixes it on `development/3.10`.
- **Coldness.**
  - Column keys use the declaring type's simple name and an ignore-underscore-and-case comparer
    (`GeneralExtensions.cs:56`, `OrmDataProvider.cs:32`), so nesting doesn't namespace a type.
  - No test project parallelises (no `Parallelize` attribute, no runsettings).

## 2. Decisions

- **D1 — Discover first in the predicate path.** `DiscoverColumns<T>()` is the **first statement** of
  `GenerateWhereClause<T>` in all four providers, before `ResolveRemoteJoins` and before the unmapped cache.
  - Discovery under an open transaction borrows the transactional connection (existing behaviour). No nested scope
    and no open reader are involved (executed).
- **D2 — Never cache an unmapped set for an undiscovered type.** One helper per provider, used at every
  `_unmappedPropertiesCache.GetOrAdd` site in that provider:
  - when `T` is in `_mappedTypes`, it returns the cached set;
  - otherwise it computes the set without caching it.

  This closes the poisoning from any path, including `ExecProcedure<T>` and future callers. If discovery throws,
  nothing is cached and the next call retries (AC7).
- **D3 — Discover first in delete by id.** `DiscoverColumns<T>()` is the first statement of `Delete<T>(long id)` and
  `DeleteAsync<T>(long id)` in all four providers.
- **D4 — No change to the visitors.** How a visitor treats a member it doesn't know is out of scope.

## 3. Acceptance criteria

- **AC1:** On a cold cache, `Delete<T>(predicate)` on a convention-mapped inherited member deletes exactly the
  matching rows, on all four providers.
- **AC2:** The same for `DeleteAsync<T>(predicate)`.
- **AC3:** A cold delete leaves the type usable: a following `Query<T>()` returns the surviving row with `Id`,
  `LastName` and `FirstName` populated.
- **AC4:** The two MySQL tests named in the 3.10 plan's §8 pass when run alone. They exist only on
  `development/3.10`, so this is a merge-forward gate (Task 4).
- **AC5:** No regressions in any of the suites listed in §4.4.
- **AC6:** No path caches an unmapped set before the type is discovered. Shown by `ExecProcedure<T>` as the first use,
  then `Query<T>` and `Delete<T>` on SQL Server and MySQL.
- **AC7:** A cold delete whose discovery fails caches nothing, and the next call succeeds once the cause is fixed.
- **AC8:** The predicate path discovers before it resolves anything: a cold `GenerateWhereClause<T>` renders `T`'s
  snake_case columns, including inside a `[SqlExpression]` token.
- **AC9:** A cold `Delete<T>(id)`/`DeleteAsync<T>(id)` with a convention primary key that isn't `Id` deletes the row.

## 4. Test plan

### 4.1 AC → test matrix (`ColdCacheDeleteTests` in each provider's test project)

| AC | Test | Providers |
|---|---|---|
| AC1, AC3 | `Delete_ByInheritedMember_OnAColdCache_DeletesTheRow_AndTheTypeStaysQueryable` | all 4 |
| AC2, AC3 | `DeleteAsync_ByInheritedMember_OnAColdCache_DeletesTheRow_AndTheTypeStaysQueryable` | all 4 |
| AC1 | `Delete_ByMethodCallPredicate_OnAColdCache_DeletesTheRow` (`LastName.StartsWith(marker)`) | all 4 |
| AC6 | `ExecProcedureFirst_ThenQueryAndDelete_Work` | SQL Server, MySQL (the providers with procedures) |
| AC7 | `ColdDelete_WhenDiscoveryFails_CachesNothing_AndTheNextCallWorks` | MySQL, SQLite |
| AC8 | `GenerateWhereClause_Cold_RendersSnakeCaseColumns` (includes a `[SqlExpression]` member on `project`) | all 4 |
| AC9 | `DeleteById_Cold_WithAConventionKeyNotNamedId_DeletesTheRow` (sync and async) | all 4 |
| AC4 | the two named tests, each run alone (`--filter`), plus the D1-removed mutant | MySql, on `development/3.10` after the merge (Task 4) |
| AC5 | the suites of §4.4 | — |

**Construction.**
- **Seeding:** rows are seeded and removed with raw SQL, so no FunkyORM cache is warmed.
- **Server-provider tables:**
  - The predicate and procedure tests use `person` (snake_case: `id`, `first_name`, `last_name`). The snake_case
    columns are what defeat naive-name alternatives to D1.
  - The AC9 table `zz_cold_pk (zz_probe_pk_id, label)` and the AC7 table (created mid-test) are each created and
    dropped by their own test, on a raw connection outside the provider's transaction (MySQL DDL commits
    implicitly).
- **SQLite:** the class uses its own temp-file database with **property-named** columns (`Id`, `FirstName`,
  `LastName`), to isolate this defect from the separate SQLite key mismatch (§1).
  - The database is created in `ClassInitialize`; `ClassCleanup` calls `SqliteConnection.ClearAllPools()` and then
    deletes the file.
- **Entity types** are used only here: `ColdDeletePerson`/`ColdDeleteBase`, `ColdDeleteAsyncPerson`/
  `ColdDeleteAsyncBase`, `ColdMethodCallPerson`/`ColdMethodCallBase`, `ColdProcPerson`/`ColdProcBase`, `ColdRetryRow`,
  `ColdWherePerson`/`ColdWhereBase`, `ColdComputedProject` and `ColdPkRow`.
- **Coldness precondition.** Each test first asserts, through the provider's internal accessors, that:
  - its type isn't in the unmapped cache;
  - no column key for its types exists.

  A failed precondition fails the test, rather than letting it pass vacuously.
- **Assertions:**
  - the delete returns 1;
  - the survivors are exactly `kept`;
  - `Id`, `LastName` and `FirstName` are populated.
- **Red runs:** each red test is run alone on `fae4472`, and its exception message is recorded.

### 4.2 Mutations each key test must kill

| Mutation | Killed by |
|---|---|
| No D1 (per provider) | AC1/AC2 rows of that provider |
| D1 placed after the unmapped `GetOrAdd` | AC1/AC2 rows |
| D1 placed after `ResolveRemoteJoins` | AC8 `[SqlExpression]` row |
| Discovery in `Delete`/`DeleteAsync` only, not `GenerateWhereClause` | AC8 rows |
| Discovery wrapped in a swallowing try/catch | AC7 |
| No D2 guard (cache always filled) | AC6 |
| No D3 | AC9 |

### 4.3 Coverage

- Per-file line coverage (coverlet, cobertura, deduplicated) of the four touched provider files, at base and at HEAD.
- `MySqlOrmDataProvider.cs` is at 83.3 % at base. AC9 covers the 28 lines of the delete-by-id overloads, for
  roughly 85.9 %.
- Any file below 85 % is reported with its baseline, for an owner waiver.

### 4.4 Suites, where they run

| Project | Command | CI on a PR into `master` |
|---|---|---|
| `Funcular.Data.Orm.SqlServer.Tests` | `dotnet test` (FUNKY_CONNECTION) | yes (`ci.yml`, LocalDB) |
| `Funcular.Data.Orm.MySql.Tests` | `dotnet test` (FUNKY_MYSQL_CONNECTION; Inconclusive without it) | yes (MySQL workflow) |
| `Funcular.Data.Orm.PostgreSql.Tests` | `dotnet test` | no (its workflow triggers on PRs to `main`); local |
| `Funcular.Data.Orm.Sqlite.Tests` | `dotnet test` | no workflow; local |
| `Funcular.Data.Orm.SqlServer.Tests.DotNet9` | `dotnet test` | local |
| `Funcular.Data.Orm.SqlServer.Tests.NetFramework` (exercises the `#else` `GetSchemaTable` discovery path) | build `FunkyORM.sln`, run the dll | local |

Local runs are recorded with their sha.

## 5. Tasks

1. **Task 0** — test-plan review: rev 1 NOT CLEAN (§9.1). Rev 2 is re-reviewed.
2. **Task 1** — red tests (§4.1), each red run alone on `fae4472`, with its message recorded.
3. **Task 2** — D1–D3 in all four providers; green; mutations (§4.2); suites (§4.4); coverage (§4.3).
4. **Task 3** — Changelog "Fixed"; hostile review and fix-verification to CLEAN.
5. **Task 4 — Merge path (owner).** A push or merge to `master` runs `ci.yml`'s `publish` job (NuGet push), so
   nothing lands on `master` without a release decision:
   - **(a)** release it as **3.9.1**: version bump, PR into `master`, publish; or
   - **(b)** merge it forward into `development/3.10` only, so it ships in 3.10.0.

   In both cases, the AC4 gate runs on `development/3.10`. D1–D3 and the cold tests are then re-verified against
   `fix/provider-scoped-caches` (its keys move to FullName, and `GetUnmappedProperties` becomes an instance method),
   whichever lands first.

## 6. Out of scope (recorded)

- **SQLite key mismatch (§1).** SQLite never uses discovered names. It is fixed on `development/3.10` by
  `fix/provider-scoped-caches` D5; on the 3.9.x line it remains unless (a) above takes it too.
- **The LINQ providers' own unmapped lambdas** (`[NotMapped]` only). They fill the same cache after `Query<T>()` has
  discovered, so the first writer decides. This is pre-existing.
- **`_mappedTypes` is a plain `HashSet`**, read and written concurrently. It is pre-existing, and fixed by
  `fix/provider-scoped-caches` (a concurrent set).
- **`GenerateOrderByClause<T>`** is dead code (no callers). It is left unchanged.

## 9. Review dispositions

### 9.1 Task 0 review of rev 1 (`f730612`; non-author; D1 applied and probed on all four providers)

Verdict: NOT CLEAN.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| F1 | blocker | PLAN-GAP | The SQLite test couldn't turn green: SQLite never uses discovered snake_case names (a separate key mismatch). | SQLite class uses its own property-named table; mismatch recorded (§1, §6). |
| F2 | major | AC-GAP | `ExecProcedure<T>` caches a poisoned unmapped set before discovery; D1 isn't the chokepoint for poisoning. | D2 guard at every `GetOrAdd` site; AC6. |
| F3 | major | AC-GAP | The retry claim was untested; a swallowing mutant survived. | AC7 (MySQL, SQLite). |
| F4 | minor | AC-GAP | D1's wording allowed placement after `ResolveRemoteJoins`, which breaks `[SqlExpression]`. | "First statement"; AC8 `[SqlExpression]` row. |
| F5 | minor | AC-GAP | Cold delete by id with a convention key not named `Id`. | D3; AC9. |
| F6 | minor | PLAN-GAP | §1's caller list was wrong; the chokepoint claim was untestable. | §1 corrected; AC8 direct `GenerateWhereClause` rows. |
| F7 | minor | HOUSE-RULE | Coverage was sidestepped. | §4.3 per-file numbers, or an owner waiver. |
| F8 | minor | HOUSE-RULE | Suites and CI weren't named; net9/net48 were missing. | §4.4. |
| F9 | minor | TEST-GAP | "Cold by construction" was asserted, not checked. | Coldness precondition; types named. |
| F10 | minor | PLAN-GAP | AC4 can't be proved on this branch; interaction with `fix/provider-scoped-caches`. | Task 4. |
| F11 | minor | PLAN-GAP | A merge to `master` publishes; no version decision. | Task 4 (owner: 3.9.1 or forward-merge only). |
| N1 | nit | TEST-GAP | Assertions and red reasons weren't spelled out. | §4.1. |
| N2 | nit | — | Method-call predicate. | AC1 `StartsWith` row. |
| N3 | nit | — | `GenerateOrderByClause<T>` is dead code. | §6. |
| N4 | nit | — | Plan and drafts differed on seeding. | Raw SQL (§4.1). |
