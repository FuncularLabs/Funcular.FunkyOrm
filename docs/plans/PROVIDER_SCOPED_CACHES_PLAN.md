# Provider-scoped identifier caches — Implementation Plan

> **Goal:** stop one provider's, or one database's, cached identifiers from leaking into another's SQL. Today the
> identifier caches are `static` on the shared Core base class `OrmDataProvider`, so every provider type and every
> database in a process shares them.
> - Ships in **3.10.0** (owner decision 2026-10-01).
> - Branch `fix/provider-scoped-caches`, cut from `development/3.10` (`89383ff`, merged forward to `26abd27`); merged
>   back before the beta PR.
> - Supersedes the 3.10 plan's §8 entry "Static identifier caches are shared across providers".

> **Status (2026-10-01):** rev 2, the test plan after the Task 0 review (§9.1). Nothing is implemented yet.

> **Revision 2 — what changed (Task 0 review, 19 findings):**
> - §1 premises, corrected and completed: two key spaces, procedure-name caches, entity mappers, an exhaustive
>   "not affected" list.
> - D1–D10: provider type justified; allowlist identities; explicit-connection and `:memory:` scopes; procedure
>   names and entity mappers in the scope; one key function; ordinal comparer; `GetColumnOrdinals` deleted.
> - ACs: AC2 adds the unmapped set; new AC8 (SQLite discovered names) and AC9 (procedure names).
> - The test matrix rebuilt so every row is red at base for the right reason, or is a guard with a named killing
>   mutation, and is CI-enforced. The tests that need a database run in the SQL Server suite or on that provider's CI.
> - A seam task, so the red tests compile at base; an explicit coverage method; §6 residuals.

## 1. Verified premises (author at `89383ff`; Task 0 reviewer executed E1–E5, §9.1)

- **Shared statics.** Four `protected static readonly` fields on `Funcular.Data.Orm.Core/OrmDataProvider.cs`:
  `_tableNames`, `_columnNames`, `_unmappedPropertiesCache` and `_mappedTypes`. Each provider's `ColumnNamesCache`,
  `UnmappedPropertiesCache` and SQL Server's `ColumnNames` are `internal static` views of those objects.
- **What the column cache holds.** It is a mix:
  - Discovery and the providers' `GetCachedColumnName` overrides store dialect-quoted names (`[Key]`, `"Key"`).
  - Core's base `GetCachedColumnName` (`OrmDataProvider.cs:381-389`) and the visitors' `GetColumnName` fallback
    (`BaseExpressionVisitor.cs:50-52`, and the same in the other three providers) store unquoted names.
- **Reported repro.** The owner executed it on 2026-09-30, and the reviewer re-ran it (E1). In one process, SQL
  Server's `Query<User>().Count()` followed by PostgreSQL's `Query<User>().Take(2).ToList()` sends `[Key]`/`[User]`
  to PostgreSQL. Result: `42601`.
- **Two key spaces (F8).**
  - `ToDictionaryKey()` is `$"{DeclaringType.Name}.{Name}"` (`GeneralExtensions.cs:52-57`).
  - Core's base `GetCachedColumnName` builds `$"{DeclaringType.FullName}.{Name}"` by hand.
  - SQLite doesn't override `GetCachedColumnName`. Its discovery writes `ToDictionaryKey` keys, but its select list
    reads the FullName keys, so SQLite never uses a discovered name. A column that differs from its property only by
    underscores fails: `no such column: Label` (E3).
- **Bare-name keys (SQL Server).**
  - `GetColumnOrdinals` (`SqlServerOrmDataProvider.cs:2263-2305`, `protected internal`, no callers) writes
    `_columnNames[property.Name.ToLowerInvariant()]`.
  - `ComputeColumnName` reads that bare key (`:2318`).
- **Schema-dependent caches missing from rev 1 (F7, F13).**
  - `_procedureNames`: a per-type static on SQL Server (`:447`, filled at `:584-590` from `sys.procedures`) and on
    MySQL (`:934`, `:1070-1076`).
  - Each provider's `_entityMappers` is keyed by type plus reader signature. A mapper built for one dialect's quoting
    can be reused by another dialect's provider over the same reader shape: SQL Server strips only `[...]` when
    matching ordinals (`:1835-1847`). This is an inference; the reviewer didn't execute it.
- **Unmapped and mapped sets are schema-dependent (F6).** Each provider's `protected internal static
  GetUnmappedProperties<T>(Type)` reads `_columnNames`: `SqlServer:2345`, `PG:1444`, `MySql:1479`, `Sqlite:1213`.
  Executed (E5): database A lacks `nickname` and database B has it. After A, querying B returns `Nickname = null`.
- **Dialect.** Core has no `Dialect` member; each provider declares its own (`SqlServer:105`, `PG:69`, `MySql:59`,
  `Sqlite:53`). Every constructor takes an optional `ISqlDialect` and opens no connection (executed).
- **SQLite's connection string.** SQLite resolves relative paths and environment variables (`Sqlite:74, 89-101`).
- **Not affected (exhaustive, with reasons).**
  - `_primaryKeys`, `_propertiesCache`, `_propertySetters` and the `RemotePathResolver` statics: reflection only.
  - `_columnOrdinalsCache`: keyed by type plus reader signature, holding ordinals, not names.
  - The dialects' `_reservedWords`, the visitors' `Placeholder`, `GenericExecuteMethod` and `QueryOperatorPolicy`:
    constants.
- **Logging.** Discovery and table resolution are never logged (`SqlServer:1682-1686, 2417`, the same in the
  others; executed for SQLite, E2). A log-based sharing test can't fail.
- **CI** (`.github/workflows`).
  - `ci.yml` (SQL Server on LocalDB) runs on pushes to `development/**` and on PRs into them.
  - The PostgreSQL and MySQL workflows run their own suites.
  - No workflow runs `Sqlite.Tests`, and `fix/**` pushes trigger nothing.
  - The net48 job is commented out.

## 2. Decisions

- **D1 — Scope.** A cache scope is (provider runtime type, dialect runtime type, connection identity). A static
  registry in Core maps each scope to one cache set; instances in the same scope share it, so discovery still runs once
  per type and database.
  - **Why the provider runtime type is in the key:** a subclass can override name resolution, e.g.
    `GetCachedColumnName` with its own naming convention. Two provider types on one dialect and one database must not
    share names.
  - **Why the dialect runtime type is in the key:** quoting comes from the dialect instance.
- **D2 — Connection identity (an allowlist, built by each provider with its typed builder).**
  - **SQL Server:** data source, initial catalog, attach-db filename, user id.
  - **PostgreSQL:** host, port, database, username, search path.
  - **MySQL:** server, port, database, user id.
  - **SQLite:** the resolved data source.

  Everything else is ignored, including passwords under any synonym (`password`, `pwd`, `psw`, `ssl password`),
  application name, pooling and timeouts. So synonyms merge (`Server`/`Data Source`), rotated passwords and volatile
  options don't open scopes, and growth is one scope per database and user.
  - A string the builder can't parse is hashed (SHA-256), never stored as given.
  - The identity is never logged.
- **D3 — Identity source.** The constructor's connection string; SQLite uses its resolved string.
  - When the constructor string is empty and a `connection` was supplied, the connection's `ConnectionString` is used.
  - A SQLite `:memory:` database (and an empty identity) gets a per-instance scope, since each is its own database.
- **D4 — Contents of a scope.**
  - Table names, column names, the unmapped-property set, and the mapped-type set (now a concurrent set).
  - Procedure names (SQL Server, MySQL).
  - Entity mappers (all four).
- **D5 — One key function.** `ToDictionaryKey()` returns `$"{DeclaringType.FullName}.{Name}"`.
  - Core's base `GetCachedColumnName` calls it, so the two key spaces merge. SQLite then uses discovered names: a
    user-visible fix (AC8, Changelog "Fixed").
  - Every key now comes from `ToDictionaryKey`, so the comparer becomes ordinal. That removes rev 1's
    underscore-and-case residual.
- **D6 — Bare keys.** `GetColumnOrdinals` is deleted (dead and buggy; D7 already accepts binary breaks).
  `ComputeColumnName` no longer reads a bare key.
- **D7 — Base-class API (binary change, Changelog "Changed").**
  - Removed: the four `protected static` fields on `OrmDataProvider`.
  - Replaced by protected instance properties: `TableNameCache`, `ColumnNameCache`, `UnmappedPropertyCache`,
    `MappedTypes`.
  - The four providers' `internal static` accessors become instance accessors.
  - The four providers' `protected internal static GetUnmappedProperties<T>(Type)` become instance methods.
  - SQL Server's `protected internal GetColumnOrdinals` is removed.
  - A derived provider compiled against 3.9.0 that uses any of these must be recompiled.
- **D8 — Binding.** Core declares two members:
  - `protected virtual string CacheScopeIdentity` (default: empty);
  - `protected virtual Type CacheScopeDialectType` (default: null).

  Each provider overrides both, computing the identity with its typed builder (D2, D3).
  - The scope resolves on first cache use, after the constructor has set `Dialect`.
  - Resolution is idempotent: registry `GetOrAdd`, and the instance field is set once.
  - A subclass that overrides nothing gets (runtime type, null, empty), which is per provider type.
- **D9 — Lifetime.** The registry lives for the process: one cache set per scope (D2 bounds the growth). Per-instance
  `:memory:` scopes are held only by their instance and not registered.
- **D10 — Where the tests run.** Tests that run without a database, and SQLite temp-file tests, live in
  `Funcular.Data.Orm.SqlServer.Tests/Caching`.
  - That project now references all four providers, and its suite is the PR-time CI job.
  - The PostgreSQL and MySQL LINQ pins live in their own suites and run in those workflows.
  - Each tier is run locally and recorded with its sha (§4.4).

## 3. Acceptance criteria

- **AC1 — Dialect isolation.** In one process, providers of different types resolve the same entity type's table and
  column names in their own dialect, whichever runs first. The reported repro succeeds.
- **AC2 — Database isolation.** Two databases of the same provider type, one entity type: each discovers and uses its
  own table and column names, **and its own mapped and unmapped property sets**. A column missing in A is read in B.
- **AC3 — Type-name isolation.** Two entity types with the same simple name, in different namespaces or nesting, never
  share a column name. Neither do two types whose full names differ only by underscores.
- **AC4 — Sharing preserved.**
  - Provider instances in the same scope share one cache set.
  - Connection strings that differ only by password (any synonym), application name or pooling share a scope.
  - A different database, user, provider runtime type or dialect runtime type doesn't share.
- **AC5 — Dialect-instance isolation.** Two providers of one type with different dialect types don't share names.
- **AC6 — No bare keys.** No cache entry is keyed by a bare property name, and `ComputeColumnName` ignores one.
- **AC7 — Every reader is scoped.** Each provider's LINQ provider reads its own provider instance's scope; checked
  with per-provider pins on that provider's CI database.
- **AC8 — SQLite uses discovered names.** A SQLite entity without `[Column]`, whose column differs from the property
  by underscores, is queryable.
- **AC9 — Procedure names are scoped.** Two databases of one provider (SQL Server, MySQL) don't share an inferred
  procedure name.
- **AC10 — No regressions.**
  - All four provider suites, the net8 SQL Server suite in CI, net48 locally and net9 are green.
  - The cold-cache tests (`RemoteColdCacheTests`, `RemoteTransactionColdTests`) keep their meaning.

## 4. Test plan

### 4.1 AC → test matrix

All rows are in `Funcular.Data.Orm.SqlServer.Tests/Caching` (CI) unless the Suite column says otherwise. Each row is
classified as one of:
- **Red:** fails at base for the stated reason;
- **Guard:** green at base, with a named killing mutation (§4.3).

| AC | Test | DB | Class |
|---|---|---|---|
| AC1 | `CrossProvider_TableAndColumnNames_UseEachProvidersDialect` [4 orders] | none | Red (`[User]` where `"User"` expected) |
| AC1 | `SqlServerThenPostgreSql_SameEntity_BothQueriesRun` (repro) | SQL Server + PostgreSQL; Inconclusive without PostgreSQL | Red (`42601`) |
| AC2 | `TwoSqliteDatabases_SameEntity_DiscoverTheirOwnTableAndColumns` | 2 SQLite temp files | Red (`no such table: scoped_widget`) |
| AC2 | `TwoSqliteDatabases_ColumnMissingInFirst_IsReadInSecond` [sync `Query`, async `GetListAsync`] | 2 SQLite temp files | Red (null where `nick`) |
| AC3 | `SameSimpleTypeName_ColumnsDoNotCollide` (nested `[Column]` types, name resolution) | none | Red (`alpha_label` where `beta_label`) |
| AC3 | `SameSimpleTypeName_SqliteQueryOfTheFirstTypeAfterTheSecondsDiscovery` (distinct `[Column]` names, `Where`) | SQLite temp file | Red (`no such column: …beta_caption`) |
| AC3 | `FullNamesDifferingOnlyByUnderscores_DoNotShareColumns` (`Outer_X.Thing` / `OuterX.Thing`) | none | Guard (comparer back to ignore-underscore) |
| AC4 | `SameScope_ShareOneCacheSet` [per provider: plant in probe A, seen by probe B] | none | Guard (fresh scope per instance) |
| AC4 | `DifferentComponent_IsAnotherScope` [per provider: one row each for database, user, provider runtime type (a subclass with its own naming), dialect type] | none | Red (planted value seen) |
| AC4 | `IgnoredKeys_ShareAScope` [per provider: one row per password synonym, application name, pooling] | none | Guard (password kept / allowlist widened) |
| AC4 | `UnparseableConnectionString_IsHashed_NotStored` | none | Guard (stored as given) |
| AC4 | `SqliteMemoryDatabases_DoNotShare`, `ExplicitConnection_SuppliesTheIdentity` | none | Red at the seam (one shared set) |
| AC5 | `SameProviderType_DifferentDialectType_DoNotShareNames` | none | Red (`plain_names` where `"plain_names"`) |
| AC6 | `ComputeColumnName_IgnoresABareNameKey` | none | Red at the seam (planted bare key used) |
| AC7 | `LinqProvider_ReadsItsOwnScope` (custom-dialect provider warms the scope through `Where`, `OrderBy`, `Select`, `Max`, `Last`; the default-dialect provider's same shapes must run) | SQL Server (CI), SQLite temp file | Red (server rejects the leaked quoting) |
| AC7 | the same pin | **PostgreSql.Tests**, **MySql.Tests** (their workflows) | Red |
| AC8 | `SqliteEntity_DiscoveredUnderscoreColumn_IsQueryable` | SQLite temp file | Red (`no such column: Label`) |
| AC9 | `ProcedureName_IsScopedPerDatabase` [SQL Server, MySQL; plant in A, unseen in B] | none | Red at the seam |
| AC10 | the four full suites, CI's SQL Server job, net48 (`Funcular.Data.Orm.SqlServer.Tests.NetFramework`, built with `dotnet build FunkyORM.sln`, run locally), net9 | — | — |

**How the DB-free rows work.**
- They use probe subclasses: `GetTableName<T>`, `GetCachedColumnName`, `ComputeColumnName`, and the protected cache
  properties.
- Each uses its own entity types and a unique fake identity.
- Each asserts that no SQLite probe file is created.

**Red classification.** Every red row is run alone (`--filter`) at base, and its failure message is recorded; a row
red for any other reason doesn't certify its AC (lesson from rev 1's SQLite AC3 row, F5).

### 4.2 Interface coverage

Each new or changed member has a test that calls it on purpose:
- the registry `For(...)`;
- the four providers' `CacheScopeIdentity`/`CacheScopeDialectType` overrides and Core's defaults (a direct
  `OrmDataProvider` subclass);
- the four protected cache properties;
- the providers' instance accessors;
- `ToDictionaryKey`;
- the instance `GetUnmappedProperties<T>`;
- `ComputeColumnName`;
- the procedure-name lookup;
- the mapper key.

**Coverage method.**
- Coverlet runs per project, and the cobertura files are merged with ReportGenerator.
- The baseline per touched file is recorded at base, and the result at HEAD.
- The source suites are SqlServer.Tests plus the PostgreSQL, MySQL and SQLite suites, run locally.
- Floor: 85 % per touched file; any file below it is reported with its baseline.
- `GeneralExtensions.cs` (19 % at base) gets tests for the members it exports. Its `Contains` ignores the
  `comparison` argument (pre-existing). It is fixed only if the owner agrees; otherwise it is recorded in §6 and
  excluded from the floor, with that reason.

### 4.3 Mutations each key test must kill

| Mutation | Killed by |
|---|---|
| Scope ignores provider runtime type | AC4 `DifferentComponent` (provider-type row) |
| Scope ignores dialect type | AC5, AC4 (dialect row) |
| Scope ignores the connection identity | AC2, AC4 (database and user rows) |
| A fresh scope per instance | AC4 `SameScope_ShareOneCacheSet` |
| Password (any synonym) kept in the identity | AC4 `IgnoredKeys_ShareAScope` |
| Unparseable string stored as given | `UnparseableConnectionString_IsHashed_NotStored` |
| A provider doesn't supply its identity (per provider) | AC4 rows of that provider |
| `ToDictionaryKey` back to the simple name | AC3 rows |
| Comparer back to ignore-underscore | AC3 underscore row |
| Core's base key not via `ToDictionaryKey` | AC8 |
| `ComputeColumnName` reads the bare key | AC6 |
| Unmapped set left process-wide | AC2 missing-column row |
| Procedure names left process-wide | AC9 |
| One LINQ provider still on a process-wide cache (per provider) | AC7 pin of that provider |

### 4.4 Where each tier runs

- **CI (PR into `development/3.10`):** `ci.yml` runs SqlServer.Tests on LocalDB, so every DB-free and SQLite
  temp-file row, and the SQL Server AC7 pin.
- **After the merge:** the PostgreSQL and MySQL workflows run their AC7 pins.
- **Local, recorded with sha:** all four suites, the PostgreSQL repro row, net48, net9.

## 5. Tasks

1. **Task 0** — test-plan review: rev 1 NOT CLEAN (§9.1). Rev 2 is re-reviewed.
2. **Task 1a — Seam (no behaviour change, green on its own).**
   - The registry API and the protected properties, all returning the existing shared statics.
   - The `CacheScopeIdentity`/`CacheScopeDialectType` members.
   - The providers' accessors made instance accessors.

   The red tests then compile at base.
3. **Task 1b — Red tests** (§4.1), run on the seam commit; each red row is run alone and its message recorded.
4. **Task 2 — Core:** real scopes (D1–D3, D8, D9), `ToDictionaryKey` and the base key (D5), the ordinal comparer.
5. **Task 3 — The four providers:**
   - the scope properties replace the statics;
   - typed-builder identities;
   - procedure names and mappers in the scope;
   - instance `GetUnmappedProperties`;
   - `GetColumnOrdinals` deleted;
   - the LINQ providers;
   - stale prose updated (`RemoteTransactionColdTests.cs:43, 137`, `SqlServerOrmDataProvider.cs:1140-1141`, the
     field docs).
6. **Task 4 — Green and gauntlet.**
   - Suites, net48, net9; mutations (§4.3); coverage (§4.2).
   - Changelog: Fixed (AC1, AC2, AC8, AC9), and Changed (D7, the `ToDictionaryKey` output).
   - The 3.10 plan's §8 entry points here.
7. **Task 5 — Hostile review and fix-verification** to CLEAN, then the merge into `development/3.10`.

## 6. Out of scope (recorded)

- **Inherited properties.** Columns are keyed by declaring type, so entity types that share a base class and map an
  inherited property to differently named columns share one entry. This is pre-existing.
- **Two table-name resolvers write the same key.** The remote-join resolvers skip the database lookup (`SqlServer:1126,
  1344`, `PG:578, 783`, `SQLite:530, 726`) and `ResolveTableName` does one; within a scope, the first writer wins.
  This is pre-existing.
- **The visitors' fallback writes unquoted, lower-cased names** for a member not yet in the cache. After this change
  it writes into its own scope only. Cold-cache predicate paths are being fixed on `fix/mysql-delete-cold-cache`.
- **Two comparer-equal columns.** When two columns are equal under the comparer, discovery takes the first.
- **Uncommitted DDL.** Discovery under an open transaction can cache uncommitted DDL (`SqlServer:1663-1666`).
- **Impersonation.** Integrated Security with impersonation shares a scope across impersonated users.
- **A re-pointed connection** keeps its constructor's scope.

## 9. Review dispositions

### 9.1 Task 0 review of rev 1 (`14f852f`; non-author; executed E1–E5)

Verdict: NOT CLEAN.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| F1 | major | TEST-GAP | The log-based AC4 test couldn't fail: discovery isn't logged. | Plant-and-see rows per provider. |
| F2 | major | TEST-GAP | No test isolated "scope ignores provider type"; rows differed in several components. | One row per component; a provider-subclass row; D1 says why the type matters. |
| F3 | major | TEST-GAP | D6 binding was untested for three providers; AC2 and AC4 weren't in CI (no workflow runs Sqlite.Tests). | Per-provider rows; SQLite temp-file tests moved into SqlServer.Tests (D10). |
| F4 | major | TEST-GAP | The LINQ-provider mutation wasn't killed in CI, and for SQL Server and MySQL not at all. | AC7 pins per provider on that provider's CI database. |
| F5 | major | TEST-GAP | The SQLite AC3 test was red for the wrong reason. | Redesigned (distinct `[Column]`, `Where` after the second discovery); every red row is now run alone with its message recorded. |
| F6 | major | AC-GAP | The unmapped set crosses databases: silent null (E5). | AC2 amended; missing-column rows (sync, async). |
| F7 | major | AC-GAP | `_procedureNames` was omitted. | D4, AC9; "not affected" made exhaustive. |
| F8 | major | AC-GAP | Two key spaces; SQLite never uses discovered names (E3). | D5 (one key function), AC8, a Changelog Fixed entry. |
| F9 | major | HOUSE-RULE | Red-first couldn't run: some tests don't compile at base. | Task 1a seam; red/guard classification. |
| F10 | minor | PLAN-GAP | D6 didn't name the dialect member; SQLite's resolved string. | D8, D3. |
| F11 | minor | TEST-GAP | Explicit connection and `:memory:` shared one identity. | D3, D9; AC4 rows. |
| F12 | minor | TEST-GAP | The denylist missed `psw`/`ssl password`; volatile keys; the raw string was stored. | D2 allowlist with typed builders; hashing; rows per synonym. |
| F13 | minor | AC-GAP | `_entityMappers` dialect dependence (inference). | Mappers moved into the scope (D4). |
| F14 | minor | TEST-GAP | AC6's writer half was untested. | `GetColumnOrdinals` deleted (D6). |
| F15 | minor | HOUSE-RULE | The coverage plan had no method; CI was overstated; net48 runs locally. | §4.2 method, §4.4 tiers; the `Contains` disposition goes to the owner. |
| F16 | minor | PLAN-GAP | D5 named the wrong `GetUnmappedProperties`. | D7 names the four provider statics. |
| F17 | nit | PLAN-GAP | Two table-name resolvers. | §6. |
| F18 | nit | HOUSE-RULE | Stale "process-wide" prose. | Task 3 list. |
| F19 | nit | PLAN-GAP | The comparer could be ordinal. | D5; AC3 underscore row. |
