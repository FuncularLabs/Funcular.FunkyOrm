# Provider-scoped identifier caches — Implementation Plan

> **Goal:** stop one provider's quoted table and column names from leaking into another provider's SQL. Today the
> identifier caches are `static` on the shared Core base class (`OrmDataProvider`), so every provider type and every
> database in a process shares them. Ships in **3.10.0** (owner decision 2026-10-01) on branch
> `fix/provider-scoped-caches`, cut from `development/3.10` at `89383ff` and merged back before the beta PR.
> Supersedes the 3.10 plan's §8 entry "Static identifier caches are shared across providers".

> **Status (2026-10-01):** rev 1, the test plan (Task 0). Nothing is implemented yet.

## 1. Verified premises (author, by reading HEAD `89383ff`)

- **Shared statics.** `Funcular.Data.Orm.Core/OrmDataProvider.cs` declares four statics: `_tableNames`, `_columnNames`,
  `_unmappedPropertiesCache` and `_mappedTypes`, all `protected static readonly`.
  - Each provider's `ColumnNamesCache`, `UnmappedPropertiesCache` and SQL Server's `ColumnNames` are `internal static`
    views of those same objects.
  - The cached values are already dialect-quoted, e.g. SQL Server `[User]` and PostgreSQL `"Key"`.
- **Reported repro (owner, executed 2026-09-30).** In one process, a SQL Server `Query<User>().Count()` followed by a
  PostgreSQL `Query<User>().Take(2).ToList()` sends `SELECT [Key], Name, [Order], [Select] FROM [User] …` to
  PostgreSQL. The result is error 42601.
- **Column keys.** `PropertyInfo.ToDictionaryKey()` is `$"{DeclaringType.Name}.{Name}"`, so two types with the same
  simple name, in different namespaces or nested in different classes, share keys.
- **Bare-name keys (SQL Server only).**
  - `SqlServerOrmDataProvider.GetColumnOrdinals` writes `_columnNames[property.Name.ToLowerInvariant()]`.
  - `ComputeColumnName` reads that bare key back for any type.
  - `GetColumnOrdinals` has no callers in the solution, but it is `protected internal`.
- **Lookup per provider.**
  - Every LINQ provider reads the caches through the static accessors, and holds an instance (`_dataProvider`).
  - The visitors receive the column dictionary through their constructors.
- **Dialect.** Every provider constructor takes an optional `ISqlDialect`, so quoting depends on the dialect
  instance, not only on the provider type.
- **Not affected.** These hold no dialect or schema data, so they stay process-wide:
  - The reflection-only statics `_primaryKeys`, `_propertiesCache` and `_propertySetters`.
  - Each provider's `_entityMappers`: a per-class static keyed by type plus the reader's schema signature.
- **Thread safety.** `_mappedTypes` is a plain `HashSet<Type>` that concurrent queries read and write.

## 2. Decisions

- **D1 — Scope.** A cache scope is identified by three things:
  - the provider's runtime type;
  - the dialect's runtime type;
  - the connection identity (D2).

  A static registry in Core maps each scope to one cache set. Provider instances for the same scope share it, so
  schema discovery still runs once per type and database, as today.
- **D2 — Connection identity.** The connection string passed to the constructor, normalised:
  - parsed with `DbConnectionStringBuilder`;
  - `password` and `pwd` keys removed, so a rotated password doesn't open a new scope;
  - the remaining keys lower-cased and sorted;
  - an unparseable string used as given.

  The user stays in the identity because it can change name resolution (SQL Server's default schema). Different
  spellings of the same database (`Server` vs `Data Source`) are separate scopes; that costs a discovery, not
  correctness.
- **D3 — What moves into a scope.**
  - Table names, column names and unmapped properties.
  - The mapped-types set, which becomes a concurrent set.
  - Nothing else (§1, "Not affected").
- **D4 — Column keys.** `ToDictionaryKey()` uses the declaring type's `FullName`. The key comparer stays
  `IgnoreUnderscoreAndCase` (residual in §6). Bare-name keys go: `GetColumnOrdinals` writes the type-qualified key,
  and `ComputeColumnName` no longer reads a bare one.
- **D5 — Base-class API (binary change, Changelog "Changed").**
  - The four `protected static` fields are removed. Protected instance properties replace them: `TableNameCache`,
    `ColumnNameCache`, `UnmappedPropertyCache` and `MappedTypes`.
  - The providers' `internal static` accessors become instance accessors.
  - `GetUnmappedProperties<T>` becomes an instance method.
  - A derived provider compiled against 3.9.0 that reads the old fields fails loudly (`MissingFieldException`), and
    must be recompiled.
  - Rejected: keeping the fields as obsolete aliases. Such a field would silently stop being populated.
- **D6 — Binding.**
  - Each provider supplies its constructor connection string through a protected virtual member.
  - The scope is resolved on first cache use, after the constructor has set `Dialect`.
  - A subclass that supplies nothing gets the scope (provider type, dialect type, empty), at least per provider type.
- **D7 — Lifetime.** The registry lives for the process: one cache set per distinct scope, the same order of growth
  as one cache per database.

## 3. Acceptance criteria

- **AC1 — Dialect isolation.** In one process, providers of different types resolve the same entity type's table and
  column names in their own dialect, whichever runs first. The reported repro succeeds: SQL Server, then PostgreSQL,
  on the `User` entity.
- **AC2 — Database isolation.** Two databases of the same provider type, one entity type: each discovers and uses
  its own table and column names.
- **AC3 — Type-name isolation.** Two entity types with the same simple name, in different namespaces or nesting,
  never share a column name.
- **AC4 — Sharing preserved.**
  - Two provider instances with the same provider type, dialect type and connection identity share discovery: the
    second runs no schema query for a type the first discovered.
  - Connection strings that differ only by password share a scope.
  - Different databases, provider types or dialect types do not.
- **AC5 — Dialect-instance isolation.** Two providers of the same type with different dialect types don't share
  quoted names.
- **AC6 — No bare keys.** No column-cache entry is keyed by a bare property name, and `ComputeColumnName` ignores one
  if present.
- **AC7 — No regressions.**
  - All four provider suites, net48 and net9 are green.
  - The cold-cache tests (`RemoteColdCacheTests`, `RemoteTransactionColdTests`) keep their meaning: one scope per
    connection string, as their "process-wide" comments assumed.

## 4. Test plan (written and failing before the implementation)

### 4.1 AC → test matrix

| AC | Test | Where | DB |
|---|---|---|---|
| AC1 | `CrossProvider_TableAndColumnNames_UseEachProvidersDialect` [SqlServer→PostgreSql, PostgreSql→SqlServer, all four] | SqlServer.Tests `Caching/ProviderScopedCacheTests` | none |
| AC1 | `SqlServerThenPostgreSql_SameEntity_BothQueriesRun` (the reported repro) | same | SQL Server + PostgreSQL; Inconclusive when PostgreSQL is unreachable (CI's SQL Server job has none) |
| AC2 | `TwoDatabases_SameEntity_DiscoverTheirOwnTableAndColumns` (tables `widget`/`wid_get`, columns `label`/`la_bel`) | Sqlite.Tests `ProviderScopedCacheTests` | two temp files |
| AC3 | `SameSimpleTypeName_ColumnsDoNotCollide` (nested `A.Thing`/`B.Thing`, `[Column]`) | SqlServer.Tests | none |
| AC3 | `SameSimpleTypeName_DiscoveredColumnsDoNotCollide` (no `[Column]`; two tables) | Sqlite.Tests | temp file |
| AC4 | `SameScope_SecondProvider_RunsNoSchemaQuery` (log) | Sqlite.Tests | temp file |
| AC4 | `ScopeKey_PasswordOnlyDifference_IsOneScope`, `ScopeKey_DatabaseProviderOrDialectDifference_IsAnotherScope` | SqlServer.Tests (Core internals) | none |
| AC5 | `SameProviderType_DifferentDialectType_DoNotShareNames` (a forwarding test dialect that always quotes) | SqlServer.Tests | none |
| AC6 | `ComputeColumnName_IgnoresABareNameKey` | SqlServer.Tests | none |
| AC7 | the four full suites, net48, net9 | — | as today |

DB-free tests reach protected members through a probe subclass of each provider (`GetTableName<T>`,
`GetCachedColumnName`, `ComputeColumnName`). Each test uses its own entity types and a unique fake connection
string, so no other test warms its scope.

### 4.2 Interface coverage

Each new or changed member has a test that calls it on purpose:
- the scope registry `For(...)` and its key normalisation;
- the four protected cache properties;
- the providers' instance accessors;
- `ToDictionaryKey`;
- `GetUnmappedProperties<T>` (instance);
- `ComputeColumnName`.

Touched files must reach ≥ 85 % line coverage (coverlet, cobertura per file).

### 4.3 Mutations each key test must kill

| Mutation | Killed by |
|---|---|
| Scope ignores provider type | AC1 DB-free rows |
| Scope ignores dialect type | AC5 |
| Scope ignores connection identity | AC2, `ScopeKey_…AnotherScope` |
| Password kept in the identity | `ScopeKey_PasswordOnlyDifference_IsOneScope` |
| A fresh scope per provider instance (no sharing) | AC4 log test |
| `ToDictionaryKey` back to the simple name | AC3 (both) |
| `ComputeColumnName` reads the bare key again | AC6 |
| One LINQ provider still on a process-wide cache | AC1 repro + AC2 (LINQ paths) |

### 4.4 Opt-in suites

- The PostgreSQL, MySQL and SQLite suites run locally as today.
- CI runs SQL Server (LocalDB), PostgreSQL and MySQL in separate jobs, so the cross-provider repro row is local only;
  the DB-free rows are its CI-enforced counterpart.

## 5. Tasks

1. **Task 0 — test-plan review** (non-author) of this document, before any implementation.
2. **Task 1 — red tests** (§4.1): written and failing on `89383ff`; the failure counts are recorded.
3. **Task 2 — Core:**
   - the scope registry and normalisation (D1, D2);
   - the protected properties (D5);
   - `ToDictionaryKey` (D4);
   - binding (D6).
4. **Task 3 — the four providers:**
   - replace static uses with the scope properties;
   - instance accessors;
   - `GetUnmappedProperties<T>`;
   - SQL Server's bare keys;
   - the LINQ providers.
5. **Task 4 — green and gauntlet.**
   - Suites, net48, net9; the mutations (§4.3); coverage (§4.2).
   - Changelog: Fixed, plus Changed for D5 and the `ToDictionaryKey` output.
   - The 3.10 plan's §8 entry points here.
6. **Task 5 — hostile review and fix-verification** to CLEAN; then merge into `development/3.10`.

## 6. Out of scope (recorded)

- **Inherited properties.** Columns are keyed by declaring type, so entity types that share a base class and map an
  inherited property to differently named columns still share one entry. This is pre-existing.
- **Key comparer.** The comparer ignores underscores and case, so two namespaces differing only by underscores or
  case would share keys. This is theoretical.
- **Re-pointed connections.** A provider whose `Connection` is re-pointed to another database after construction
  keeps its constructor's scope.
