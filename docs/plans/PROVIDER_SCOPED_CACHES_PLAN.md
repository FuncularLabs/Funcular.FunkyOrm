# Provider-scoped identifier caches — Implementation Plan

> **Goal:** stop one provider's, or one database's, cached identifiers from leaking into another's SQL. Today the
> identifier caches are `static` on the shared Core base class `OrmDataProvider`, so every provider type and every
> database in a process shares them.
> - Ships in **3.10.0** (owner decision 2026-10-01).
> - Branch `fix/provider-scoped-caches`, cut from `development/3.10` (`89383ff`, merged forward); merged back before
>   the beta PR.
> - Supersedes the 3.10 plan's §8 entry "Static identifier caches are shared across providers".

> **Status (2026-10-01):** rev 3, the test plan after the Task 0 re-review (§9.2). Nothing is implemented yet.

> **Revision 3 — what changed (re-review N1–N17):**
> - **D2 simplified.** The identity is the provider's typed builder's canonical connection string with the password
>   cleared; everything else is kept. This removes rev 2's allowlist and its component holes (N2, N8). Differing
>   options cost an extra discovery, never correctness.
> - **D3/D8/D9 unified.** One sentinel: a null identity means a per-instance scope; an empty one means per provider
>   type (N1). SQLite memory modes are per-instance (N9). The cost is recorded (N12), and binding uses
>   `LazyInitializer` (N13).
> - **Mappers.** AC5 covers name-derived mappers, with a SQL Server CI row and a mutation (N3).
> - **Rows and mutations.** AC3's underscore row moves to the SQLite path, red at the seam (N4). The equivalent
>   mutation is replaced (N5). AC9 calls the resolver (N6). The AC7 mechanism and mutant are concrete (N10). Three
>   mutations are added (N14).
> - **Seam.** It adds `InternalsVisibleTo` for SqlServer.Tests in the PostgreSql, MySql and Sqlite assemblies, the
>   procedure-name accessor and the test csproj references (N7).
> - **Gates and residuals.** The PostgreSQL and MySQL pins gate through local runs recorded with sha (N11). The stale
>   prose list and D7 surface are completed (N15, N16). §6 additions (N17).

## 1. Verified premises (author at `89383ff`; Task 0 reviewers executed E1–E11, §9.1–§9.2)

- **Shared statics.** Four fields on `Funcular.Data.Orm.Core/OrmDataProvider.cs`:
  - `_tableNames`, `_columnNames` and `_mappedTypes` are `protected static readonly`;
  - `_unmappedPropertiesCache` is `protected internal static readonly`.

  Each provider's `ColumnNamesCache`, `UnmappedPropertiesCache` and SQL Server's `ColumnNames` are `internal static`
  views of those objects.
- **What the column cache holds.** It is a mix:
  - discovery and the providers' `GetCachedColumnName` overrides store dialect-quoted names;
  - Core's base `GetCachedColumnName` (`OrmDataProvider.cs:381-389`) and the visitors' `GetColumnName` fallback
    (`BaseExpressionVisitor.cs:50-52`, and the same in the other three providers) store unquoted names.
- **Reported repro (E1).** SQL Server, then PostgreSQL, `Query<User>()` sends `[Key]`/`[User]` to PostgreSQL, giving
  `42601`.
- **Two key spaces (E3, E7).**
  - `ToDictionaryKey()` is `$"{DeclaringType.Name}.{Name}"`; Core's base method builds `$"{DeclaringType.FullName}.{Name}"`
    by hand.
  - SQLite has no override, so it never uses discovered names.
  - With the ignore-underscore-and-case comparer, `Outer_X.Thing.Label` and `OuterX.Thing.Label` collide on both paths
    (E7).
- **Bare-name keys (SQL Server).** `GetColumnOrdinals` (`:2263-2305`, no callers) writes them; `ComputeColumnName`
  (`:2318`) reads them.
- **More schema-dependent statics.**
  - `_procedureNames` (SQL Server `:447`, `:584-590`; MySQL `:934`, `:1070-1076`).
  - `_entityMappers` in all four providers. A mapper captures names from the column cache and the unmapped set
    (`SqlServer:1797-1840`).
  - E6, executed: with names scoped and mappers shared, a default-dialect provider reads `Id=0` and `FirstName=null`
    after a double-quoting provider built the mapper.
- **Unmapped and mapped sets are schema-dependent (E5).**
  - Each provider's `protected internal static GetUnmappedProperties<T>(Type)` reads the column cache (`SqlServer:2345`,
    `PG:1444`, `MySql:1479`, `Sqlite:1213`).
  - Core's protected `GetUnmappedProperties<T>()` (`OrmDataProvider.cs:415`) reads the unmapped cache.
- **Dialect.** Each provider declares its own `Dialect` (`SqlServer:105`, `PG:69`, `MySql:59`, `Sqlite:53`).
  Constructors take an optional `ISqlDialect` and open no connection.
- **Connection strings (E9, E10).**
  - The typed builders merge keyword synonyms (SqlClient, Npgsql, MySqlConnector; Npgsql maps `PSW`/`PWD`).
  - SQLite resolves relative paths and environment variables (`Sqlite:74, 89-101`).
  - An unparseable string reaches a provider only on PostgreSQL, MySQL, or SQL Server with an explicit connection.
    SQL Server without one, and SQLite, throw.
- **SQLite memory databases (E8).**
  - Each `:memory:` connection is a separate database.
  - So is `Mode=Memory` without `Cache=Shared`.
  - `ResolveConnectionString` turns `Mode=Memory` names and `file::memory:` into rooted paths.
- **Visibility (N7).**
  - Core and SqlServer grant `InternalsVisibleTo` to SqlServer.Tests.
  - PostgreSql, MySql and Sqlite grant it only to their own test projects.
- **Not affected (reasons).**
  - `_primaryKeys`, `_propertiesCache`, `_propertySetters`, the `RemotePathResolver` statics and
    `AuditComment.SafeIdentifier`: reflection or pure functions.
  - `_columnOrdinalsCache`: type plus reader signature, holding ordinals.
  - `SystemContextScope._depth`: per async flow.
  - The dialects' `_reservedWords`, `Placeholder`, `GenericExecuteMethod` and `QueryOperatorPolicy`: constants.
- **Logging and CI.**
  - Discovery and table resolution aren't logged.
  - `ci.yml` (SQL Server, LocalDB) runs on PRs into `development/**`. The PostgreSQL workflow runs on PRs into `main`
    only, MySQL into `main`/`master`. Nothing runs Sqlite.Tests.
  - ReportGenerator is not installed.

## 2. Decisions

- **D1 — Scope.** (provider runtime type, dialect runtime type, connection identity), mapped by a static registry in
  Core to one cache set.
  - The provider type is in the key because a subclass can override name resolution.
  - The dialect type is in the key because quoting comes from the dialect.
- **D2 — Connection identity.** Each provider parses its connection string with its typed builder
  (`SqlConnectionStringBuilder`, `NpgsqlConnectionStringBuilder`, `MySqlConnectionStringBuilder`,
  `SqliteConnectionStringBuilder`). It then clears the password and takes the builder's canonical `ConnectionString`.
  - **Everything else is kept:** server, port, database, user, search path, `Options`, attach file, application
    name, pooling.
  - A rotated password, or a password under any synonym, maps to the same identity.
  - Any other difference is a different scope: correct, and at worst one more discovery. Growth is one scope per
    distinct password-less connection string.
  - A string the builder rejects is hashed (SHA-256); only the hash is kept.
  - The identity is never logged. That is enforced by review: no code path passes it to `Log`.
- **D3 — Identity source and per-instance scopes.**
  - The source is the constructor string (SQLite: its resolved string).
  - If that is empty and a `connection` was supplied, the connection's `ConnectionString` is used.
  - **A null identity means a per-instance scope:** created for that provider instance and never registered. SQLite
    returns null for `:memory:`, for `Mode=Memory` (shared or not) and for a `file::memory:` data source, since each
    such connection, or each process, holds a database only that instance can name reliably.
  - **An empty identity means one scope per (provider type, dialect type).**
- **D4 — Contents of a scope.**
  - Table names, column names, the unmapped set, and the mapped-type set (a concurrent set).
  - Procedure names (SQL Server, MySQL).
  - Entity mappers (all four).
- **D5 — One key function.** `ToDictionaryKey()` returns `$"{DeclaringType.FullName}.{Name}"`. Core's base
  `GetCachedColumnName` calls it, so SQLite uses discovered names (AC8, Changelog "Fixed"). The comparer becomes
  ordinal.
- **D6 — Bare keys.** `GetColumnOrdinals` is deleted, and `ComputeColumnName` no longer reads a bare key.
- **D7 — Binary-break surface (Changelog "Changed").**
  - Removed from `OrmDataProvider`: `_tableNames`, `_columnNames`, `_mappedTypes` (protected static) and
    `_unmappedPropertiesCache` (protected internal static).
  - Added in their place: protected instance properties `TableNameCache`, `ColumnNameCache`, `UnmappedPropertyCache`
    and `MappedTypes`.
  - The providers' `internal static` accessors become instance accessors.
  - The four providers' `protected internal static GetUnmappedProperties<T>(Type)` become instance methods. Core's
    protected `GetUnmappedProperties<T>()` reads the instance cache.
  - SQL Server's `protected internal GetColumnOrdinals` is removed.
  - The Changelog tells direct `OrmDataProvider` subclasses to override the identity members (D8), or they get one
    scope per provider type.
- **D8 — Binding.** Core declares `protected virtual string CacheScopeIdentity` (default: empty, i.e. per provider type)
  and `protected virtual Type CacheScopeDialectType` (default: null). Each provider overrides both.
  - The scope resolves on first cache use, after the constructor has set `Dialect`, through
    `LazyInitializer.EnsureInitialized`. Registered scopes come from the registry's `GetOrAdd`, so resolution is
    idempotent and thread-safe.
- **D9 — Lifetime and cost.**
  - The registry lives for the process.
  - Per-instance scopes (D3) die with their instance. A provider re-created per request on such a database re-runs
    discovery, mapper builds and procedure lookups each time. Recorded in §6 and the Changelog.
  - On SQLite, non-transactional operations open a new connection from the string (`Sqlite:1173`), so a `:memory:`
    provider only ever sees one database inside a transaction anyway.
- **D10 — Where the tests run.**
  - Tests without a database, SQLite temp-file tests, the SQL Server LINQ pin and the SQL Server mapper row live in
    `Funcular.Data.Orm.SqlServer.Tests/Caching` (the PR-time CI job).
  - The PostgreSQL and MySQL LINQ pins live in their own suites. Their workflows don't run on PRs into
    `development/**`, so their pre-merge gate is a local run recorded with its sha (§4.4).

## 3. Acceptance criteria

- **AC1 — Dialect isolation.** Providers of different types resolve the same entity type's names in their own
  dialect, whichever runs first. The reported repro succeeds.
- **AC2 — Database isolation.** Two databases of one provider type, one entity type: each discovers and uses its own
  table names, column names, mapped set and unmapped set. A column missing in A is read in B.
- **AC3 — Type-name isolation.** Two entity types never share a column name when their simple names are the same, or
  when their full names differ only by underscores.
- **AC4 — Sharing.**
  - Instances in the same scope share one cache set.
  - Connection strings that differ only by password (any synonym) share a scope.
  - A different connection string otherwise (server, database, user, search path or `Options`, and so on) doesn't.
  - Nor does a different provider runtime type or dialect runtime type.
  - SQLite memory databases never share.
- **AC5 — Dialect-instance isolation.** Two providers of one type with different dialect types share neither names
  nor name-derived entity mappers.
- **AC6 — No bare keys.** No cache entry is keyed by a bare property name, and `ComputeColumnName` ignores one.
- **AC7 — Every LINQ read is scoped.** Each provider's LINQ provider reads its own instance's scope at every read
  site: ORDER BY, SELECT, the default `Last` ordering, `Count`/`Any`/`All` with a predicate, and the aggregate
  selector.
- **AC8 — SQLite uses discovered names.** A SQLite entity without `[Column]`, whose column differs from the property
  by underscores, is queryable.
- **AC9 — Procedure names are scoped** (SQL Server, MySQL).
- **AC10 — No regressions.** All four suites, CI's SQL Server job, net48 and net9 are green. The cold-cache tests keep
  their meaning.

## 4. Test plan

### 4.1 AC → test matrix (`Funcular.Data.Orm.SqlServer.Tests/Caching` unless noted)

Each row is one of:
- **Red:** fails at the seam commit (Task 1a) for the stated reason;
- **Guard:** green at the seam, with a named killing mutation (§4.3).

Every row is run alone at the seam and its outcome recorded.

| AC | Test | DB | Class at the seam |
|---|---|---|---|
| AC1 | `CrossProvider_TableAndColumnNames_UseEachProvidersDialect` [4 orders; column assertions on SQL Server, PostgreSQL and MySQL only, not SQLite] | none | Red (`[User]` where `"User"` expected) |
| AC1 | `SqlServerThenPostgreSql_SameEntity_BothQueriesRun` | SQL Server + PostgreSQL; Inconclusive without PostgreSQL | Red (`42601`) |
| AC2 | `TwoSqliteDatabases_SameEntity_DiscoverTheirOwnTableAndColumns` (tables `scoped_widget`/`scopedwidget`, columns `label`/`la_bel`) | 2 SQLite temp files | Red (`no such table`) |
| AC2 | `TwoSqliteDatabases_ColumnMissingInFirst_IsReadInSecond` [sync `Query`, async `GetListAsync`] | 2 SQLite temp files | Red (null where `nick`) |
| AC3 | `SameSimpleTypeName_ColumnsDoNotCollide` (nested `[Column]` types, SQL Server probe) | none | Red (`alpha_label` where `beta_label`) |
| AC3 | `SameSimpleTypeName_SqliteQueryOfTheFirstTypeAfterTheSecondsDiscovery` (distinct `[Column]`; A discovered, then B, then A queried with `Where`) | SQLite temp file | Red (`no such column: …beta_caption`) |
| AC3 | `FullNamesDifferingOnlyByUnderscores_DoNotShareColumns` (`Outer_X.Thing`/`OuterX.Thing`, through Core's base method, a SQLite probe) | none | Red (comparer collision, E7) |
| AC4 | `SameScope_ShareOneCacheSet` [per provider: plant in probe A, seen by probe B] | none | Guard |
| AC4 | `PasswordOnlyDifference_SharesAScope` [per provider, one row per password synonym] | none | Guard |
| AC4 | `OtherConnectionDifference_IsAnotherScope` [per provider: server; database; user; PostgreSQL `Search Path`; PostgreSQL `Options`] | none | Red (planted value seen) |
| AC4 | `ProviderTypeOrDialectTypeDifference_IsAnotherScope` [a provider subclass with its own naming; a different dialect type] | none | Red |
| AC4 | `UnparseableConnectionString_IsHashed` [PostgreSQL, MySQL, SQL Server with an explicit connection] | none | Guard |
| AC4 | `SqliteMemoryDatabases_NeverShare` [`:memory:`, `Mode=Memory`, `Mode=Memory;Cache=Shared`, `file::memory:`]; `EmptyIdentity_IsPerProviderType` (a direct `OrmDataProvider` subclass); `ExplicitConnection_SuppliesTheIdentity` | none | Red at the seam (one shared set) |
| AC5 | `SameProviderType_DifferentDialectType_DoNotShareNames` | none | Red |
| AC5 | `SameProviderType_DifferentDialectType_DoNotShareMappers` (a double-quoting dialect reads `person` first, then the default provider must read the right `Id` and `FirstName`) | SQL Server (CI) | Red (E6: `Id=0`, null) |
| AC6 | `ComputeColumnName_IgnoresABareNameKey` | none | Red (planted bare key used) |
| AC7 | `LinqProvider_ReadsItsOwnScope`: a custom dialect whose quoting the engine rejects (`«x»`) warms its scope through `OrderBy`, `Select`, `Last()` without `OrderBy`, `Count(pred)` and `Max`; then the default-dialect provider's same shapes must run. Entity on `person` with snake_case `[Column]`s. | SQL Server (CI), SQLite temp file | Red at the seam (provider path); the LINQ-site kill is the §4.3 mutant |
| AC7 | the same pin | PostgreSql.Tests, MySql.Tests | as above |
| AC8 | `SqliteEntity_DiscoveredUnderscoreColumn_IsQueryable` | SQLite temp file | Red (`no such column: Label`) |
| AC9 | `ProcedureName_IsScopedPerDatabase` [SQL Server, MySQL]: plant distinct names in scope A and scope B, then call the resolver on each (both cache hits, fake servers) | none | Red at the seam (the shared cache returns A's name for B) |
| AC10 | the four full suites, CI's SQL Server job, net48 (`dotnet build FunkyORM.sln`, run the dll), net9 | — | — |

**How the DB-free rows work.**
- Probe subclasses reach the protected members.
- `InternalsVisibleTo` (Task 1a) reaches the providers' internal accessors.
- Each row uses its own entity types and unique fake connection strings, and asserts that no SQLite probe file is
  created.

### 4.2 Interface coverage

Each new or changed member has a test that calls it on purpose:
- the registry;
- the four providers' `CacheScopeIdentity` and `CacheScopeDialectType`, and Core's defaults;
- the four protected cache properties;
- the instance accessors;
- `ToDictionaryKey`;
- the instance `GetUnmappedProperties<T>` (providers and Core);
- `ComputeColumnName`;
- the procedure-name resolver;
- the mapper cache.

**Coverage.**
- Coverlet runs per project; the cobertura files are merged with ReportGenerator (installed as a local dotnet tool in
  Task 4).
- The baseline is recorded per touched file at base, and the result at HEAD. Floor: 85 % per touched file; a file
  below it is reported with its baseline.
- `GeneralExtensions.Contains` ignores its `comparison` argument (pre-existing). Owner's call: fix it here, or exclude
  it from the floor with that reason.

### 4.3 Mutations each key test must kill

| Mutation | Killed by |
|---|---|
| Scope ignores provider runtime type / dialect type | AC4 provider-and-dialect rows; AC5 |
| Identity is the empty string (connection ignored) | AC2, AC4 `OtherConnectionDifference` |
| Password kept in the identity | AC4 `PasswordOnlyDifference_SharesAScope` |
| Identity built from a raw string (no typed builder) | AC4 synonym rows |
| A fresh scope per instance | AC4 `SameScope_ShareOneCacheSet` |
| SQLite memory databases registered | AC4 memory rows |
| Unparseable string stored as given | `UnparseableConnectionString_IsHashed` |
| `ToDictionaryKey` back to `DeclaringType.Name` | AC3 rows 1–2 |
| Core's base key back to `$"{DeclaringType.Name}.{Name}"` | AC8 |
| Comparer back to ignore-underscore | AC3 underscore row |
| `ComputeColumnName` reads the bare key | AC6 |
| Table-name cache left process-wide | AC1, AC2 row 1 |
| Mapped-type set left process-wide | AC2 row 1 (`label`/`la_bel`) |
| Unmapped set left process-wide | AC2 missing-column row |
| Entity mappers left process-wide | AC5 mapper row |
| Procedure names left process-wide | AC9 |
| One LINQ provider's column-cache reads redirected to a single static dictionary (per provider) | AC7 pin of that provider |

### 4.4 Where each tier runs

- **PR into `development/3.10`, CI:** `ci.yml` runs SqlServer.Tests, covering every DB-free row, the SQLite
  temp-file rows, and the SQL Server AC5 mapper and AC7 rows.
- **Pre-merge gate, local, recorded with sha:**
  - the PostgreSQL and MySQL suites, with their AC7 pins;
  - the SQLite suite;
  - the PostgreSQL repro row;
  - net48 and net9.
- **After the merge:** the PostgreSQL and MySQL workflows run on their own triggers.

## 5. Tasks

1. **Task 0** — test-plan review: rev 1 NOT CLEAN (§9.1); rev 2 NOT CLEAN (§9.2). Rev 3 is re-reviewed.
2. **Task 1a — Seam (no behaviour change, green on its own).**
   - The registry API, the protected properties and the identity members, all returning the existing shared
     statics.
   - Instance accessors in the providers.
   - A procedure-name accessor and a mapper-cache accessor.
   - `InternalsVisibleTo("Funcular.Data.Orm.SqlServer.Tests")` in the PostgreSql, MySql and Sqlite projects (a
     product-assembly change, recorded in the Changelog).
   - SqlServer.Tests references the three providers and links `PostgreSqlTestConnection.cs`.
3. **Task 1b — Red tests** (§4.1) on the seam. Every row is run alone, and its outcome and message recorded.
4. **Task 2 — Core:** real scopes (D1–D3, D8, D9), `ToDictionaryKey` and the base key (D5), the ordinal comparer.
5. **Task 3 — The four providers:**
   - scope properties replace the statics;
   - typed-builder identities;
   - procedure names and mappers in the scope;
   - instance `GetUnmappedProperties`;
   - `GetColumnOrdinals` deleted;
   - the LINQ providers;
   - stale prose updated:
     - `RemoteTransactionColdTests.cs:43, 137`;
     - `SqlServerOrmDataProvider.cs:77, 577, 1140-1141`;
     - `PostgreSqlOrmDataProvider.cs:593`, `MySqlOrmDataProvider.cs:571`, `SqliteOrmDataProvider.cs:545` ("guarded
       (_mappedTypes)");
     - the field docs.
6. **Task 4 — Green and gauntlet.**
   - Suites, net48, net9; mutations (§4.3); coverage (§4.2).
   - Changelog: Fixed (AC1, AC2, AC5, AC8, AC9); Changed (D7, the `ToDictionaryKey` output, `InternalsVisibleTo`,
     the per-instance-scope cost).
   - The 3.10 plan's §8 entry points here.
7. **Task 5 — Hostile review and fix-verification** to CLEAN, then the merge into `development/3.10`.

## 6. Out of scope (recorded)

- **Inherited properties.** Columns are keyed by declaring type, so entity types sharing a base class map an
  inherited property to one entry (pre-existing).
- **Two table-name resolvers write the same key.** The remote-join resolvers (`SqlServer:1126, 1344`, `PG:578, 783`,
  `MySql:556, 763`, `SQLite:530, 726`) and `ResolveTableName`; the first writer wins within a scope (pre-existing).
- **Two unmapped-set writers.** The LINQ path's `[NotMapped]`-only `GetOrAdd` (`SqlLinqQueryProvider.cs:350`, and the
  same in the others) and the provider's full set; the first writer wins within a scope (pre-existing, inference).
- **The visitors' fallback** writes unquoted, lower-cased names into its own scope. Cold predicate paths are fixed on
  `fix/mysql-delete-cold-cache`.
- **Discovery picks the first match:** two comparer-equal columns, and discovery takes the first.
- **Discovery under an open transaction** can cache uncommitted DDL (`SqlServer:1663-1666`).
- **An explicit connection to a different database than the constructor string.** Under a transaction, discovery
  runs on that connection (`SqlServer:1665`), but the scope is keyed by the string.
- **Runtime database changes:** `ChangeDatabase`, `USE`, `SET search_path`, and a re-pointed `Connection`.
- **Principals:** Integrated Security with impersonation, and Entra/`AccessToken` principals, share the string's
  scope.
- **Dialect state:** custom dialects of one type that carry state share a scope.
- **Per-user connection strings** give per-user discovery, and the registry has no eviction.
- **Per-instance scopes (D3)** re-run discovery for each new provider instance.

## 9. Review dispositions

### 9.1 Task 0 review of rev 1 (`14f852f`; non-author; executed E1–E5)

Verdict: NOT CLEAN. F1–F19, dispositioned in rev 2. Rev 2 verified each against the code (§9.2); the partials are
listed there.

### 9.2 Task 0 re-review of rev 2 (`d6ecd2f`; non-author; executed E6–E11)

Verdict: NOT CLEAN. F1, F2, F5, F6, F8, F12, F14, F15, F16 and F19 are resolved. F3, F4, F7, F9, F10, F11, F13, F17
and F18 were partial; their remainders are below.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| N1 | major | PLAN-GAP | D3 and D8 contradicted each other; per-instance scopes had no mechanism. | One sentinel: null means per-instance, empty means per provider type (D3, D8). Rows for both. |
| N2 | major | TEST-GAP | Server, port and attach-file components had no rows. | D2 is no longer an allowlist: everything but the password is kept. Rows for server, database, user, search path and `Options`. |
| N3 | major | AC-GAP | Mappers had no row or mutation; E6 showed silent wrong data. | AC5 amended; SQL Server mapper row; mutation. |
| N4 | minor | PLAN-GAP | The AC3 underscore row is red at the seam, and red for the wrong reason on SQL Server. | Moved to the SQLite (Core base) path; AC3 row 1 isn't on SQLite. |
| N5 | minor | TEST-GAP | The "base key not via `ToDictionaryKey`" mutation is equivalent after D5. | Replaced with `DeclaringType.Name`. |
| N6 | minor | TEST-GAP | AC9 observed only the cache object, and the seam had no accessor. | Resolver calls on two scopes; accessor in the seam. |
| N7 | minor | PLAN-GAP | `InternalsVisibleTo` and seam completeness. | Task 1a. |
| N8 | minor | AC-GAP | Search path wasn't in AC4, and `Options` bypassed the allowlist. | Full-string identity; AC4 rows for both. |
| N9 | minor | TEST-GAP | SQLite memory modes. | D3; AC4 memory rows. |
| N10 | minor | TEST-GAP | The AC7 mechanism was under-specified; the `Count(pred)` site was missed. | Concrete mutant; rejected quoting; snake_case columns; full shape list. |
| N11 | minor | PLAN-GAP | The PostgreSQL and MySQL pins don't gate the PR. | §4.4: a local run recorded with sha. |
| N12 | minor | PLAN-GAP | The per-instance cost was unrecorded. | D9, §6, Changelog. |
| N13 | nit | HOUSE-RULE | "Set once" mechanism; "never logged"; unparseable-string providers. | D8 `LazyInitializer`; D2 review-enforced; row providers named. |
| N14 | minor | TEST-GAP | Three mutations missing. | §4.3. |
| N15 | nit | HOUSE-RULE | Stale-prose list incomplete. | Task 3. |
| N16 | nit | PLAN-GAP | §1 field modifiers; D7 surface; Changelog guidance. | §1, D7. |
| N17 | nit | PLAN-GAP | §6 residuals. | §6. |
