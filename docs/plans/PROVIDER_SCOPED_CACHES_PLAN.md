# Provider-scoped identifier caches — Implementation Plan

> **Goal:** stop one provider's, or one database's, cached identifiers from leaking into another's SQL. Today the
> identifier caches are `static` on the shared Core base class `OrmDataProvider`, so every provider type and every
> database in a process shares them.
> - Ships in **3.10.0** (owner decision 2026-10-01).
> - Branch `fix/provider-scoped-caches`, cut from `development/3.10` (`89383ff`, merged forward); merged back before
>   the beta PR.
> - Supersedes the 3.10 plan's §8 entry "Static identifier caches are shared across providers".

> **Status (2026-10-01):** rev 8, the test plan after the seventh Task 0 review (§9.7). Nothing is implemented yet.
> The rev 7 reviewer ran the AC7 rows on a test-created table on all four providers: Red at the seam (or a Guard),
> green when fixed, and on SQL Server and SQLite every per-site mutant killed by its row.

> **Revision 8 — what changed (R7-1…R7-7).** Six rounds running, a fix caused the next finding. So rev 8
> adopts rules the reviewer already executed instead of designing new ones.
> - D11 strips any matching quote pair. That rule passed the default, reserved-only, always-bracket and
>   always-backtick dialects in the reviewer's run. A CI row covers a reserved-word column on the default
>   dialect (R7-1).
> - A SQLite real-read mapper row (R7-2).
> - An internal mapped-set accessor per provider for observers (R7-3).
> - §4.1/§4.2 completed (R7-4); AC7 type, fragments, seeds and identities named (R7-5).
> - D7's mapper and procedure-name members (R7-6); bookkeeping (R7-7).
> The rev 5 and rev 6 reviewers prototyped the seam, the fixed state and 13 per-site mutants on SQL Server and
> SQLite. On SQLite every AC7 row is Red at the seam (or a Guard) and green after the fix, and every listed mutant
> dies, M1 included. On SQL Server, P1's `Sum`/`Average` overflowed on the shared `person` data (R6-1), so the AC7
> rows now run on a table the test creates.

> **Revision 7 — what changed (R6-1…R6-7):**
> - AC7 runs on a test-created table with fixed rows, on every provider (R6-1).
> - D9's advice is limited to settings that don't affect name resolution (R6-2).
> - The cold-cache observers read the tested instance's own scope, with a self-check (R6-3).
> - The SQLite bracket-quoting mapper row becomes a Guard (R6-4). A new D11 unquotes SQLite's mapper with the
>   dialect, so D5 doesn't break a bracket-quoting SQLite provider (R6-5).
> - §4.2 maps each member to its tests, including Core's `GetUnmappedProperties<T>()` (R6-6). The rev 5 note is
>   corrected (R6-7).

> **Revision 6 — what changed (R5-1…R5-6):**
> - AC7 `NeverReadAnotherScope`: P1 plants the same shape as P2 and its own fragments are asserted, so P1 provably
>   runs every site (R5-2; R4-1 closed on SQLite, and on SQL Server by rev 7's table).
> - The default-`Last` unmapped row moves to the `Id` type (R5-1); the predicate site gets an unmapped row (R5-3).
> - The cold-cache merge interaction covers that plan's coldness checks and AC8 observer (R5-5).
> - Document fixes: the table break, D10, a Guard's killing mutation, the Changelog advice's scope (R5-4, R5-6).

> **Revision 5 — what changed (R4-1…R4-8):**
> - The AC7 rows are rebuilt to pin the aggregate selector and the LINQ unmapped-set reads per site, and the
>   predicate site is added. *(Rev 6: two of these rows couldn't do so as written (R5-1, R5-2), and the predicate
>   site's unmapped read had no row (R5-3).)*
> - The SQLite seam plant; mapper rows through real reads, with `_entityMappers` deleted.
> - The registry-key row; the Changelog advice made safe; bookkeeping and the merge interaction with the cold-cache
>   fix.

> **Revision 4 — what changed (re-review R1–R6):**
> - **AC7.** An own-scope sentinel: valid mappings are planted in one scope through the seam, the SQL is captured,
>   and each LINQ read site must show its own scope's plant and never another scope's. A per-site mutant replaces the
>   warm-up design, which never reached a LINQ site (R1).
> - **Mapper rows** on all four providers (R2).
> - **SQLite.** Empty and temporary data sources are per-instance (R3).
> - **D3/D8.** Mutations and rows completed; the hash row reclassified; the procedure resolver reached through an
>   internal accessor (R4).
> - **Registry and Changelog.** The registry key is a SHA-256 of the identity. `Remove("Password")` is specified.
>   The Changelog states scope growth and coldness, and the cold-cache delete fix is a 3.10.0 ship dependency
>   (R5, R6).

> **Revision 3 — what changed (re-review N1–N17), superseded where rev 4 says so:**
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

## 1. Verified premises (author at `89383ff`; Task 0 reviewers executed E1–E24 and a seam/fixed prototype, §9.1–§9.7)

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
  `SqliteConnectionStringBuilder`). It calls `Remove("Password")`, which merges every synonym; setting the password
  to empty instead leaves Npgsql's key in place (E12). It then takes the builder's canonical `ConnectionString`.
  - **The registry key is the SHA-256 of that string** (*rev 4*), so no secret is retained by the registry,
    including Npgsql `SSL Password` and MySqlConnector `Certificate Password` (E13).
  - **Everything else is kept:** server, port, database, user, search path, `Options`, attach file, application
    name, pooling.
  - A rotated password, or a password under any synonym, maps to the same identity.
  - Any other difference is a different scope: correct, and at worst one more discovery. Growth is one scope per
    distinct password-less connection string.
  - A string the builder rejects is hashed as given (SHA-256).
  - **Over-splits cost performance, never correctness** (E14): key order (Npgsql), host aliases, value case.
  - The identity is never logged. That is enforced by review: no code path passes it to `Log`.
- **D3 — Identity source and per-instance scopes.**
  - The source is the constructor string (SQLite: its resolved string).
  - If that is empty and a `connection` was supplied, the connection's `ConnectionString` is used.
  - **A null identity means a per-instance scope:** created for that provider instance and never registered. SQLite
    returns null for:
    - `:memory:` (including `Filename=:memory:`);
    - `Mode=Memory`, shared or not (for a shared one, this over-splits);
    - an empty resolved data source: `""`, `Data Source=`, whitespace, each a private temporary database per
      connection (E15) *(rev 4)*.

    The rule applies to whichever string supplies the identity, including an explicit connection's. (`file::memory:`
    and URI memory names are turned into rooted paths by the existing resolution; that is pre-existing, §6.)
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
    and `MappedTypes`, plus `EntityMapperCache` and `ProcedureNameCache` (D4 scopes mappers and procedure names, and
    the PostgreSql, MySql and Sqlite assemblies reach Core only through protected members) *(rev 8, R7-6)*.
  - The providers' `internal static` accessors become instance accessors.
  - The four providers' `protected internal static GetUnmappedProperties<T>(Type)` become instance methods. Core's
    protected `GetUnmappedProperties<T>()` reads the instance cache.
  - SQL Server's `protected internal GetColumnOrdinals` is removed.
  - Each provider's `internal static readonly _entityMappers` is **deleted**, so any leftover read fails to compile
    *(rev 5, R4-5)*.
  - The Changelog tells direct `OrmDataProvider` subclasses to override the identity members (D8), or they get one
    scope per provider type.
- **D8 — Binding.** Core declares `protected virtual string CacheScopeIdentity` (default: empty, i.e. per provider type)
  and `protected virtual Type CacheScopeDialectType` (default: null). Each provider overrides both.
  - The scope resolves on first cache use, after the constructor has set `Dialect`, through
    `LazyInitializer.EnsureInitialized`. Registered scopes come from the registry's `GetOrAdd`, so resolution is
    idempotent and thread-safe.
- **D9 — Lifetime and cost.**
  - The registry lives for the process, with no eviction: one scope per distinct password-less string.
  - An app that varies a connection-string option per request gets a scope, a discovery and a mapper set per
    variant. Examples: `Application Name`, PostgreSQL `Options=-c app.user=…`, timeouts. That costs memory and
    time against 3.9.0's single shared cache. Stated in the Changelog *(rev 6, R5-6)*:
    - **Session values that don't affect name resolution** can vary per request without a new scope: a custom
      namespaced PostgreSQL setting (`app.user`), a MySQL user variable, a SQL Server `SESSION_CONTEXT` key. Set
      them through FunkyORM's session context (`AuditContext`, primed on each connection open on SQL Server,
      PostgreSQL and MySQL; PostgreSQL accepts only dotted keys; not available on SQLite). A bare `SET` outside a
      FunkyORM transaction doesn't persist across its per-operation connections.
    - **Settings that affect name resolution** (`search_path` or `Search Path`, `Options=-c search_path=…`, a
      MySQL default database) must stay in the connection string, one scope each. Never change them with `SET`:
      the scope would then hold another schema's names (§6) *(rev 7, R6-2)*.
    - **Connection attributes** (`Application Name`, connect and command timeouts) have no such substitute: each
      distinct value costs one scope.
  - Per-instance scopes (D3) die with their instance. A provider re-created per request on such a database re-runs
    discovery, mapper builds and procedure lookups each time. Recorded in §6 and the Changelog.
  - On SQLite, non-transactional operations open a new connection from the string (`Sqlite:1173`), so a `:memory:`
    provider only ever sees one database inside a transaction anyway.
- **D10 — Where the tests run.**
  - Tests without a database, SQLite temp-file tests, the SQL Server LINQ pin and the SQL Server mapper row live in
    `Funcular.Data.Orm.SqlServer.Tests/Caching` (the PR-time CI job).
  - The PostgreSQL and MySQL LINQ pins and mapper rows (AC5) live in their own suites. Their workflows don't run on PRs into
    `development/**`, so their pre-merge gate is a local run recorded with its sha (§4.4).

- **D11 — SQLite's mapper strips any matching quote pair** *(rev 7, R6-5; rule replaced in rev 8, R7-1)*.
  - Today it strips only `"` (`SqliteOrmDataProvider.cs:973-978`). Once D5 gives it discovered names, a custom
    dialect that quotes with brackets would map nothing: executed, `0:null` where `5:Ann` was read at the seam.
  - **Rule:** a discovered name wrapped in a matching `"…"`, `[…]` or backtick pair loses that pair; any other
    name is used as is.
    - The built-in dialects quote only reserved words (`ISqlDialect.EncloseIdentifier`), so a rule derived from
      the dialect's quoting of `x` finds nothing to strip, and loses reserved-word columns once D5 is in.
    - Executed by the rev 7 reviewer: `ReservedWordTable_InsertAndQuery_Works` read `0` where `42` was expected.
    - The pair rule passed the default, reserved-only bracket, always-bracket and always-backtick dialects.
  - The server providers' mappers have the same limitation, pre-existing (§6).

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
  - SQLite memory and temporary databases never share.
- **AC5 — Dialect-instance isolation.** Two providers of one type with different dialect types share neither names
  nor name-derived entity mappers, on all four providers.
- **AC6 — No bare keys.** No cache entry is keyed by a bare property name, and `ComputeColumnName` ignores one.
- **AC7 — Every LINQ read is scoped.** Each provider's LINQ provider reads its own instance's scope, and never
  another's, at every read site. The sites are:
  - ORDER BY and SELECT;
  - the default `Last` ordering;
  - `Count`/`Any`/`All` with a predicate;
  - the aggregate selector;
  - the predicate site (`Where`, and `First`/`Single`/`Last` with a predicate, through the provider's
    `GenerateWhereClause`) *(rev 5)*;
  - the LINQ unmapped-set reads at every site (ORDER BY, SELECT, default `Last`, `Count`/`Any`/`All` with a
    predicate, the aggregate, and the predicate site *(rev 6)*).
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
| AC4 | `UnparseableConnectionString_IsHashed` [PostgreSQL, MySQL, SQL Server with an explicit connection] | none | Red at the seam (identity is computed in Task 3) |
| AC4 | `RegistryKey_RetainsNoSecret`. PostgreSQL: the password first, plus `SSL Password`. MySQL: `Certificate Password`. SQL Server and SQLite (no other secret keyword): the key part equals the SHA-256 (64 hex characters) of the canonical identity. | none | Red at the seam |
| AC4 | `SqliteMemoryAndTemporaryDatabases_NeverShare` [`:memory:`, `Filename=:memory:`, `Mode=Memory`, `Mode=Memory;Cache=Shared`, `""` and `Data Source=` each with an explicit connection] | none | Red at the seam (one shared set) |
| AC4 | `EmptyIdentity_IsPerProviderType`: two instances of one direct `OrmDataProvider` subclass share; a different subclass doesn't | none | Red at the seam |
| AC4 | `ExplicitConnection_SuppliesTheIdentity` [all four: empty constructor string; two explicit connections to different databases don't share] | none | Red at the seam |
| AC5 | `SameProviderType_DifferentDialectType_DoNotShareNames` | none | Red |
| AC5 | `SameProviderType_DifferentDialectType_DoNotShareMappers` (a double-quoting dialect reads `person` first, then the default provider must read the right `Id` and `FirstName`) | SQL Server (CI) | Red (E6/E17: `Id=0`, null) |
| AC5 | `SqliteBracketDialect_ReadsItsOwnRows`: a SQLite provider whose dialect brackets **every** identifier reads its own property-named table after D5 *(rev 7, R6-5)*. (Rev 7's "same with a bracket-quoting dialect" mapper-sharing row is dropped: with D11 its mutation is equivalent, R7-2.) | SQLite temp file (CI) | Guard: green at the seam; killed by "SQLite mapper strips only `"`" once D5 is in |
| AC5 | `SqliteDefaultDialect_ReadsAReservedWordColumn` (the default dialect, a column named `Order`, after D5) *(rev 8, R7-1)* | SQLite temp file (CI) | Guard: green at the seam; killed by "D11 derived from the dialect's quoting of `x`" and by "no D11" |
| AC5 | `MapperCache_IsScoped_ThroughARealRead` [PostgreSQL in PostgreSql.Tests, MySQL in MySql.Tests, SQLite in SqlServer.Tests/Caching on a temp file *(rev 8, R7-2)*]. P_A reads `person`, then A's mapper cache has an entry and B's has none. P_B reads, then B has its own. | PostgreSQL, MySQL | Red at the seam (one shared cache) |
| AC6 | `ComputeColumnName_IgnoresABareNameKey` | none | Red (planted bare key used) |
| AC2 | `UnmappedSet_IsComputedFromTheInstanceScope` [per provider]: a column name for `T.X` planted in scope A only; A's instance `GetUnmappedProperties<T>` excludes `X`, B's includes it *(rev 8, R7-4)* | none | Red at the seam (A's plant is seen by B) |
| AC2 | `CoreGetUnmappedProperties_ReadsTheInstanceScope` (probe subclass of a direct `OrmDataProvider` subclass) *(rev 7, R6-6; listed here in rev 8, R7-4)* | none | Guard: green at the seam; killed by "Core's `GetUnmappedProperties<T>()` reads a static set" |
| AC4 | `MappedSetAccessor_ReadsTheInstanceScope` [per provider]: the internal accessor (Task 1a) of instance A shows a type that A discovered, and instance B in another scope doesn't *(rev 8, R7-3)* | none | Red at the seam (one shared set) |
| AC7 | `LinqSites_ReadTheirOwnScope`. Through the seam accessors, plant valid mappings for the dedicated type `LinqProbe { Id, FirstName }` (`[Table("zz_psc_linq")]`, below) in P2's scope: table `zz_psc_linq`, `FirstName → last_name`, `Id → employer_id`, the type marked mapped. Then run `OrderBy`/`ThenBy`, both `Select` forms, `Last()` without `OrderBy`, `Where` and `First(pred)` (on `"x"`), `Count`/`Any`/`All` with a predicate, and `Max`/`Sum`/`Average` on `Id`. Capture the SQL through `Log` (logged before execution) and assert each site's exact fragment, e.g. `MAX(zz_psc_linq.employer_id)` (SQLite renders `Average` as `ROUND(AVG(…), 10)`) *(rev 8, R7-5)*. | SQL Server (CI), SQLite temp file (CI) | Guard |
| AC7 | `LinqSites_NeverReadAnotherScope`. P2 plants first. P1 (same database, different identity: `Application Name` on SQL Server and PostgreSQL, `Connection Timeout` on MySQL, `Default Timeout` on SQLite) then plants **the same shape as P2** with distinct values: table `zz_psc_linq`, the type marked mapped, `FirstName → middle_initial`, `Id → id`. P1 runs every site, and the row asserts **P1's own exact fragments** at each one, which proves P1 ran it. Only then does P2 run; P2's exact fragments must show its own plant at every site. *(rev 6, R5-2)* | SQL Server (CI), SQLite temp file (CI) | Red at the seam (shared caches) |
| AC7 | `LinqUnmappedRead_IsScoped` [per site]. Two dedicated types, each `[Table("zz_psc_linq")]` so P1 discovers its table: type A has `FirstName` planted as unmapped in P2's scope, type B has `Id`. Type A runs ORDER BY, SELECT, `Count` with a predicate, and the predicate site (`Where(x => x.FirstName == …)`) *(rev 6, R5-3)*. Type B runs default `Last()` (which orders by `Id`) *(rev 6, R5-1)* and `Max(x => x.Id)`. At each site, P2 is rejected with that site's message and **no SQL is logged**: ORDER BY and `Last` `Only simple member access…`; SELECT `Unmapped properties cannot be selected directly.`; `Count` with a predicate and the predicate site `Expression type Parameter…`; aggregate `Only simple member access is supported in aggregate expressions.`. P1, in another scope, renders and executes the same shape. | SQL Server (CI), SQLite temp file (CI) | Red at the seam |
| AC7 | the three rows above | PostgreSql.Tests, MySql.Tests | as above |
| AC8 | `SqliteEntity_DiscoveredUnderscoreColumn_IsQueryable` | SQLite temp file | Red (`no such column: Label`) |
| AC9 | `ProcedureName_IsScopedPerDatabase` [SQL Server, MySQL]: plant distinct names in scope A and scope B, then call the resolver on each through an internal accessor (both cache hits, fake servers) | none | Red at the seam (the shared cache returns A's name for B) |
| AC10 | the four full suites, CI's SQL Server job, net48 (`dotnet build FunkyORM.sln`, run the dll), net9 | — | — |

**SQLite at the seam (R4-4).** Until D5, SQLite's SELECT list reads Core's base key (`$"{FullName}.{Name}"`), so
the AC7 SQLite temp table also has `Id` and `FirstName` columns, alongside `last_name`, `middle_initial` and
`employer_id`. Plants that the SELECT list misses then fall back to real columns, and the row stays executable.

**The AC7 table (rev 7, R6-1).** Every AC7 row runs on a table the test creates, `zz_psc_linq`, never on the
shared `person`.
- On the local database, `SUM(person.id)` overflows `int` on SQL Server. CI's seed leaves `employer_id` null,
  so `MAX`/`AVG` of it throw "Sequence contains no elements", depending on test order.
- **Server providers:** `zz_psc_linq (id INT PRIMARY KEY, first_name, last_name, middle_initial, employer_id INT NOT
  NULL)`. **SQLite:** `Id INTEGER PRIMARY KEY, FirstName, last_name, middle_initial, employer_id` (R4-4).
- Three fixed rows `(id, first_name, last_name, middle_initial, employer_id)`: `(1, 'a', 'x', 'x', 10)`,
  `(2, 'b', 'y', 'y', 20)`, `(3, 'c', 'z', 'w', 30)`. Every aggregate is non-null and fits in `int`, and `First(pred)`
  on `"x"` finds a row through either scope's mapping (`last_name` or `middle_initial`) *(rev 8, R7-5)*.
- DDL runs outside any provider transaction: drop if exists → create → seed → row → drop in `finally`.

**How the DB-free rows work.**
- Probe subclasses reach the protected members.
- `InternalsVisibleTo` (Task 1a) reaches the providers' internal accessors.
- Each row uses its own entity types and unique fake connection strings, and asserts that no SQLite probe file is
  created.

### 4.2 Interface coverage

Each new or changed member has a test that calls it on purpose *(table: rev 7, R6-6)*:

| Member | Tests |
|---|---|
| The registry | AC4 `SameScope_ShareOneCacheSet`, `PasswordOnlyDifference_SharesAScope`, `RegistryKey_RetainsNoSecret` |
| `CacheScopeIdentity` (four providers) | AC4 `OtherConnectionDifference_IsAnotherScope`, `ExplicitConnection_SuppliesTheIdentity`, `UnparseableConnectionString_IsHashed`, `SqliteMemoryAndTemporaryDatabases_NeverShare` |
| `CacheScopeDialectType` (four providers) | AC4 `ProviderTypeOrDialectTypeDifference_IsAnotherScope`; AC5 rows |
| Core's defaults | AC4 `EmptyIdentity_IsPerProviderType` |
| The four protected cache properties | AC2 rows; AC4 `SameScope_ShareOneCacheSet` |
| The instance accessors | AC4 and AC7 rows (planting and observing) |
| `ToDictionaryKey` | AC3 rows |
| The providers' instance `GetUnmappedProperties<T>` (four providers) | `UnmappedSet_IsComputedFromTheInstanceScope` (per provider); AC2 `TwoSqliteDatabases_ColumnMissingInFirst_IsReadInSecond` |
| Core's base `GetCachedColumnName` (D5 changes its key) | AC8; AC3 `FullNamesDifferingOnlyByUnderscores_DoNotShareColumns` |
| The providers' internal mapped-set accessor (Task 1a) | `MappedSetAccessor_ReadsTheInstanceScope`; the cold-cache observers (Task 5) |
| Core's `protected GetUnmappedProperties<T>()` (uncalled; D7 makes it read the instance scope) | `CoreGetUnmappedProperties_ReadsTheInstanceScope` (probe subclass of a direct `OrmDataProvider` subclass) |
| `ComputeColumnName` | AC6 |
| The procedure-name resolver | AC9 |
| The mapper cache, and SQLite's quote-pair stripping (D11) | AC5 mapper rows; `SqliteBracketDialect_ReadsItsOwnRows`; `SqliteDefaultDialect_ReadsAReservedWordColumn` |

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
| Entity mappers left process-wide (per provider) | AC5 mapper rows of that provider; on SQLite, `MapperCache_IsScoped_ThroughARealRead` *(rev 8, R7-2)* |
| SQLite's mapper strips only `"` (no D11) | `SqliteBracketDialect_ReadsItsOwnRows` |
| D11 derived from the dialect's quoting of `x` (rev 7's rule) | `SqliteDefaultDialect_ReadsAReservedWordColumn`; the existing `ReservedWordTable_InsertAndQuery_Works` (local Sqlite.Tests) |
| The internal mapped-set accessor reads a static set | `MappedSetAccessor_ReadsTheInstanceScope` |
| Core's `GetUnmappedProperties<T>()` reads a static set | `CoreGetUnmappedProperties_ReadsTheInstanceScope` |
| An empty identity treated as null (per-instance) | AC4 `EmptyIdentity_IsPerProviderType` |
| A provider ignores the explicit connection's string when the constructor string is empty (per provider) | AC4 `ExplicitConnection_SuppliesTheIdentity` |
| The registry keyed by the raw identity (no hash) | AC4 `RegistryKey_RetainsNoSecret` |
| Procedure names left process-wide | AC9 |
| A LINQ column read redirected to a fresh static: per site (ORDER BY, SELECT, `Last`, `Count`/`Any`/`All` predicate, predicate site), per provider | AC7 `LinqSites_ReadTheirOwnScope` (the Guard's killing mutation) and `LinqSites_NeverReadAnotherScope` |
| The aggregate-selector column read redirected to a fresh static or to another scope (**M1**), per provider | AC7 `LinqSites_NeverReadAnotherScope` only (P1 fully planted, its fragments asserted) |
| A LINQ unmapped-set read redirected to a fresh static: per site (ORDER BY **M3**, SELECT, `Last` on type B, `Count` predicate, aggregate **M4**, predicate site), per provider | AC7 `LinqUnmappedRead_IsScoped` row of that site (rejected with no SQL logged) |
| The `_entityMappers` static left in place, with reads redirected to it | compile failure (D7 deletes it); AC5 mapper rows |

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

1. **Task 0** — test-plan review: revs 1–7 NOT CLEAN (§9.1–§9.7). Rev 8 is re-reviewed.
2. **Task 1a — Seam (no behaviour change, green on its own).**
   - The registry API, the protected properties and the identity members, all returning the existing shared
     statics.
   - Instance accessors in the providers.
   - A procedure-name cache accessor, an internal route to `ResolveProcedureName<T>`, and a mapper-cache accessor.
   - In each provider, an internal instance accessor for the mapped-type set, reading the instance's own cache
     (at the seam, the static set). Observers in each provider's own test project reach it through that
     provider's `InternalsVisibleTo` *(rev 8, R7-3)*.
   - `InternalsVisibleTo("Funcular.Data.Orm.SqlServer.Tests")` in the PostgreSql, MySql and Sqlite projects (a
     product-assembly change, recorded in the Changelog).
   - SqlServer.Tests references the three providers and links `PostgreSqlTestConnection.cs`.
3. **Task 1b — Red tests** (§4.1) on the seam. Every row is run alone, and its outcome and message recorded.
4. **Task 2 — Core:** real scopes (D1–D3, D8, D9), `ToDictionaryKey` and the base key (D5), the ordinal comparer.
5. **Task 3 — The four providers:**
   - scope properties replace the statics;
   - typed-builder identities;
   - procedure names and mappers in the scope;
   - instance `GetUnmappedProperties`, Core's included;
   - D11 (the quote-pair rule) in SQLite's mapper;
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
   - Changelog: Fixed (AC1, AC2, AC5, AC8, AC9). Changed:
     - D7 and the `ToDictionaryKey` output;
     - `InternalsVisibleTo`;
     - scope growth per distinct password-less string, with no eviction and the advice from D9;
     - coldness per scope;
     - the per-instance-scope cost.
   - The 3.10 plan's §8 entry points here.
7. **Task 5 — Hostile review and fix-verification** to CLEAN, then the merge into `development/3.10`.
   - **Ship dependency (R5):** per-scope coldness makes the cold-cache `Delete<T>(predicate)` defect fire on first use
     in each scope. `fix/mysql-delete-cold-cache` must therefore land in `development/3.10` before 3.10.0 ships
     with this change.
   - **Merge interaction (R4-8).** The cold-cache plan's D2 helper `UnmappedPropertiesFor<T>()` reads
     `_mappedTypes` and `_unmappedPropertiesCache`, which D7 removes. Whichever branch lands second moves the helper
     onto the scope, using the same scope for the mapped check and the unmapped cache. It also rewrites that plan's
     test observers *(rev 6, R5-5)*:
     - its coldness preconditions build keys with `ToDictionaryKey()` against the provider's own scope (hand-built
       `DeclaringType.Name + "." + Name` keys would match nothing after D5, so "no column key" would pass
       vacuously);
     - its AC8 observer reads the scope's mapped set instead of `_mappedTypes`;
     - every observer reads **through the provider instance under test itself**, with its internal mapped-set
       accessor (Task 1a) and its column and unmapped accessors. It never uses a separate instance: a
       `ProbeProvider` subclass instance resolves a different scope after D1, so reading through one would pass
       vacuously *(rev 7, R6-3; rev 8, R7-3: one instance, so no self-check is needed)*.

     It then re-runs the whole cold-cache class on every provider, not only the AC6 helper rows.

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
- **Server providers' mappers** unquote only their default quote character, so a custom dialect that quotes
  differently maps nothing (pre-existing; D11 fixes SQLite only, because D5 would otherwise newly break it).
- **Per-user connection strings** give per-user discovery, and the registry has no eviction.
- **Per-instance scopes (D3)** re-run discovery for each new provider instance.
- **SQLite URI memory names and `file::memory:`** are turned into rooted paths by the existing
  `ResolveConnectionString` (E15). Pre-existing.

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

### 9.3 Task 0 re-review of rev 3 (`3f20ae4`; non-author; executed E12–E17)

Verdict: NOT CLEAN.
- **Resolved:** N1, N2, N4–N8, N11, N13–N17, F3, F7, F9, F10, F17, F18.
- **Partial:** N3, N9, N12, F11, F13.
- **Not fixed:** N10/F4 (R1).

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| R1 | major | TEST-GAP | AC7 couldn't fail for the LINQ-site mutant: the warm-up's rejected quoting throws in discovery before any LINQ site runs (E16). | Own-scope sentinel rows (plant, capture SQL, assert own and never another's), the LINQ unmapped reads, and per-site mutants. |
| R2 | minor | TEST-GAP | Mapper scoping was proved on SQL Server only. | SQLite temp-file mapper row; PostgreSQL and MySQL DB-free mapper rows; per-provider mutation. |
| R3 | minor | AC-GAP | SQLite empty and temporary data sources shared a scope. | D3: null for an empty resolved data source, on any identity source; AC4 amended; rows. |
| R4 | minor | TEST-GAP | D3/D8 had no mutations; explicit-connection row providers; the hash row's class; the resolver route. | Two mutations; rows completed; hash row Red at the seam; internal resolver route in Task 1a. |
| R5 | minor | PLAN-GAP | Users weren't warned about scope growth and per-scope coldness; the cold-cache fix is a dependency. | D9, the Changelog list, and the Task 5 ship dependency. |
| R6 | nit | HOUSE-RULE | "Clears the password" was underspecified; other secrets were kept. | `Remove("Password")`; SHA-256 registry key; a password-first PostgreSQL row. |

### 9.4 Task 0 re-review of rev 4 (`accdf4f`; non-author; executed E18–E24 on SQL Server and SQLite)

Verdict: NOT CLEAN.
- **Resolved:** R3, R4, R5, R6.
- **Partial:** R1 (the mechanism works, E18; two mutant kinds survived), R2.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| R4-1 | major | TEST-GAP | The aggregate-selector read falls back to the provider's own scope, so mutant M1 survived (E19); P1 never ran. | `NeverReadAnotherScope`: P2 plants, P1 plants every property and runs every site, then P2 runs; exact fragments; M1 per provider. |
| R4-2 | major | TEST-GAP | Four of five LINQ unmapped reads had no row (M3 survived, E21). The string aggregate couldn't tell rejection from M4 (E23). | A per-site unmapped row, rejected by message with no SQL logged; the aggregate on `Id`; M3/M4 per site. |
| R4-3 | minor | AC-GAP | AC7 omitted the predicate site (`Where`, `First(pred)`). | Added to AC7, the rows and the mutations. |
| R4-4 | minor | PLAN-GAP | The SQLite own-scope row wasn't a Guard at the seam: the SELECT list reads Core's base key (E24). | The temp table also has `Id`/`FirstName`; SQLite's P1 uses `Default Timeout`. |
| R4-5 | minor | TEST-GAP | The PostgreSQL and MySQL mapper rows checked only the accessor; the static field survived D7. | `_entityMappers` deleted (D7); mapper rows through a real read. |
| R4-6 | minor | TEST-GAP | `RegistryKey_RetainsNoSecret` couldn't kill "no hash" after `Remove("Password")`. | Rows with `SSL Password` / `Certificate Password`; SHA-256 shape on SQL Server and SQLite. |
| R4-7 | minor | HOUSE-RULE | The Changelog advice ("`SET` commands") was unsafe with per-operation connections. | `AuditContext`, or `SET` inside a transaction; not on SQLite. |
| R4-8 | nit | PLAN-GAP | Stale bookkeeping; the merge interaction with the cold-cache helper. | Task 0 line, §1, Task 5. |

### 9.5 Task 0 re-review of rev 5 (`66e4b6d`; non-author; a seam/fixed/mutant prototype on SQL Server and SQLite)

Verdict: NOT CLEAN.
- **Resolved:** R4-3 (column read), R4-4, R4-5 (by design), R4-6 (executed), R4-7.
- **Partial:** R4-1, R4-2, R4-8.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| R5-2 | major | TEST-GAP | Fix-introduced (R4-1 still open). P1 planted no table and didn't mark the type mapped, so after the fix it threw at `Query<T>()` before any site ran, and nothing asserted that it ran. M1 survived; it died once P1 was fully planted. | P1 plants P2's shape; P1's own fragments are asserted at every site; same in PostgreSQL and MySQL. |
| R5-1 | minor | TEST-GAP | Default `Last()` orders by `Id`, so the `FirstName`-unmapped type was never rejected; its mutant had no working row. | `Last` runs on type B (`Id` unmapped); executed: red at the seam, green after, mutant killed. |
| R5-3 | minor | AC-GAP | The predicate site's unmapped read was unpinned (MPredUnm survived). | Added to AC7's list, a row on type A, and §4.3. |
| R5-5 | minor | PLAN-GAP | The merge interaction missed the cold-cache coldness preconditions (vacuous after D5) and its `_mappedTypes` observer (removed by D7). | Task 5 names both; the whole cold-cache class is re-run. |
| R5-4 | nit | PLAN-GAP | Rev 5 document defects: (a) a paragraph inside the §4.1 table broke the rows below it; (b) D10 was stale; (c) the own-scope Guard had no named killing mutation; (d) the rev 5 note over-claimed. | (a) moved below the table; (b) D10 updated; (c) §4.3 row; (d) the note annotated. |
| R5-6 | nit | HOUSE-RULE | D9's advice covers session values only; `Application Name` and timeouts have no substitute. | D9 splits session values from connection attributes. |

### 9.6 Task 0 re-review of rev 6 (`b1b8de1`; non-author; seam/fixed prototype, 13 mutants × 2 providers × 3 rows)

Verdict: NOT CLEAN.
- **Resolved:** R5-1, R5-3, R5-4 (a–c); R5-4 (d) via R6-7.
- **Partial:** R5-2 (R6-1), R5-5 (R6-3), R5-6 (R6-2).

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| R6-1 | minor | TEST-GAP | Fix-introduced. With P1 running `Sum`/`Average` on `person.id`, SQL Server overflowed `int` on the local database. CI's seed leaves `employer_id` null, so P2's `MAX`/`AVG` depended on test order. | AC7 runs on a test-created `zz_psc_linq` with fixed rows on every provider; the Status line is corrected. |
| R6-2 | minor | HOUSE-RULE | Fix-introduced. The advice listed PostgreSQL `Options=-c` settings, which include `search_path`; PostgreSQL priming also rejects undotted keys. | Limited to settings that don't affect name resolution. Schema-affecting settings stay in the connection string, never `SET`. |
| R6-3 | minor | PLAN-GAP | The cold-cache observers read `_mappedTypes` through a `ProbeProvider` subclass, which after D1 is another scope, so they pass vacuously. | Task 5: observers read the tested instance's scope; a same-object self-check. |
| R6-4 | minor | TEST-GAP | The SQLite bracket mapper row was misclassed: red for AC8's reason with snake_case columns, green at the seam with property-named ones. | Property-named columns; Guard; its killing mutation is live once D5 is in. |
| R6-5 | minor | AC-GAP | D5 would break a SQLite provider whose dialect quotes with brackets: its mapper strips only `"` (executed: `0:null` after the fix). | D11: unquote with the dialect; a Guard row; the server providers' pre-existing limitation is in §6. |
| R6-6 | minor | HOUSE-RULE | §4.2 had no member→test mapping; Core's uncalled `GetUnmappedProperties<T>()` had no test. | A member→tests table; a probe-subclass row for Core's method. |
| R6-7 | nit | PLAN-GAP | The rev 5 note's annotation said three rows couldn't pin their reads; it was two, plus a missing site. | Corrected. |

### 9.7 Task 0 re-review of rev 7 (`fb499bc`; non-author; prototype on all four providers; 12 per-site mutants × 3 rows on SQL Server and SQLite; two D11 variants)

Verdict: NOT CLEAN.
- **Resolved:** R6-1, R6-2 and R6-7.
- **Partial:** R6-3, R6-4, R6-5 and R6-6.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| R7-1 | major | TEST-GAP | Fix-introduced. D11's rule ("the text around `x` in `Dialect.QuoteIdentifier("x")`") named a member that doesn't exist (it is `EncloseIdentifier`). The built-in dialects quote only reserved words, so after D5 a reserved-word column was lost: `ReservedWordTable_InsertAndQuery_Works` read `0` where `42` was expected. | The quote-pair rule the reviewer executed; a CI Guard row for the default dialect; the bracket row's dialect brackets every identifier. |
| R7-2 | minor | TEST-GAP | Fix-introduced. With D11 in, nothing killed "SQLite mappers left process-wide". | A SQLite real-read mapper row; the bracket mapper-sharing row dropped (its mutation is equivalent). |
| R7-3 | minor | PLAN-GAP | Task 5's "internal scope accessor" existed in no task, and a self-check comparing a scope with itself can't fail. | An internal mapped-set accessor per provider in Task 1a, with a row; observers read through the tested instance itself; self-check dropped. |
| R7-4 | minor | HOUSE-RULE | §4.2 was incomplete: the Core test wasn't in §4.1; the providers' instance `GetUnmappedProperties` named a SQLite-only test; two members were missing. | §4.1 rows; §4.2 completed. |
| R7-5 | minor | TEST-GAP | Fix-introduced. AC7's text still showed `person` fragments, named no type, didn't specify the seeds `First(pred)` needs, and didn't name P1's identity on PostgreSQL and MySQL. | `LinqProbe`, the fragments, three seed rows and the identities named. |
| R7-6 | minor | PLAN-GAP | D7 didn't list the mapper and procedure-name members that the non-SqlServer assemblies need. | Added to D7 and the Changelog's "Changed". |
| R7-7 | nit | PLAN-GAP | §9.6 omitted R5-4 (d); D11 sat between D9 and D10. | Fixed. |
