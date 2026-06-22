# Changelog

All notable changes to this project will be documented in this file.

## [3.6.1] - 2026-06-22

### Fixed
- **`Update` / `UpdateAsync` inside a transaction** no longer throw *"A concurrent operation is already using the transactional connection."* The read-before-write step opened a second `ConnectionScope`, which the per-connection transactional guard rejected as concurrent access even though the read is strictly sequential. The existing row is now read on the transaction's own open connection, so updates work normally inside a `BeginTransaction()` scope. Fixed identically across all four providers (SQL Server, PostgreSQL, SQLite, MySQL). `Insert`/`InsertAsync`/`Delete` were never affected.

### Added
- Regression tests `Update_WithinTransaction_*` / `UpdateAsync_WithinTransaction_*` in every provider's integration suite, covering update inside a transaction for both the sync and async paths.

### Changed
- The transactional-concurrency guard's exception message now also notes that the error can arise from re-entrant (nested) single-threaded use — not only from `Task.WhenAll`/concurrent access — so that class of cause is discoverable from the message alone. Additive wording; the original guidance is unchanged.

## [3.6.0] - 2026-06-10

### Added
- **🐬 MySQL Provider**: New `MySqlOrmDataProvider` (project `Funcular.Data.Orm.MySql`), bundled into the single `Funcular.Data.Orm` NuGet package alongside the SQL Server, PostgreSQL, and SQLite providers (`net8.0` + `netstandard2.0`).
  - Built on the MIT-licensed **MySqlConnector** driver (chosen over Oracle's GPL `MySql.Data`).
  - Full LINQ-to-SQL translation with MySQL syntax: backtick identifier quoting, `AUTO_INCREMENT` identity retrieved via `LAST_INSERT_ID()` (MySQL has no `RETURNING` clause), `LIMIT`/`OFFSET` paging, `CONCAT`-based string operations, and `EXTRACT()` date parts.
  - Complete `[RemoteKey]` / `[RemoteProperty]` / `[RemoteLink]` support with automatic `LEFT JOIN` generation, plus all four "view-replacing" attributes against native MySQL `JSON`: `[JsonPath]` (`JSON_UNQUOTE(JSON_EXTRACT(...))`, including WHERE predicates and the 3.5.1 method-call fix), `[SqlExpression]`, `[SubqueryAggregate]`, and `[JsonCollection]` (`JSON_ARRAYAGG` / `JSON_OBJECT`).
  - Reserved-word quoting, MySQL error-code mapping (`MySqlException.Number`, e.g. 1146/1062/1452), Guid storage as `CHAR(36)` (`GuidFormat=Char36`), and the concurrency-safe connection model shared with the other providers.
  - `MySqlStringComparison` enum — default `CaseInsensitive` (matching MySQL's default `_ci` collations and SQL Server); opt-in `CaseSensitive` applies `COLLATE utf8mb4_bin`.
  - **37 dialect + integration tests** (CRUD sync/async, LINQ translation, paging, aggregates, remote keys/properties, JsonPath SELECT/WHERE, computed attributes, Guid/non-identity PKs, reserved-word quoting) — green against MySQL 8.0 both locally and in CI.
  - GitHub Actions CI workflow using a MySQL 8.0 service container; `Database/MySql/` DDL + `docker-compose.yml` for local setup.

### Changed
- **Version**: All projects promoted from `3.6.0-beta1` to `3.6.0`.
- **`ISqlDialect`**: Added the MySQL implementation (`MySqlDialect`) with `ProviderName = "mysql"`, backtick `EncloseIdentifier`, the `LAST_INSERT_ID()` insert strategy, and MySQL JSON value/collection builders.

### Notes
- MySQL's default `_ci` collations make string comparisons case-insensitive (matching SQL Server), so no case-folding workaround is emitted. Table-name case sensitivity follows the server's `lower_case_table_names`; the test DDL uses lowercase table names for cross-platform portability.

## [3.5.1] - 2026-06-09

### Fixed
- **`[JsonPath]` honored in WHERE predicates for method-call expressions**: The `WhereClauseVisitor.VisitMethodCall` delegate (used for `Contains`, `StartsWith`, `EndsWith`, and `IN` clauses) bypassed the remote-property map and emitted plain column names instead of JSON accessor expressions. Fixed across all three providers (SQL Server, PostgreSQL, SQLite) so JSON-extracted properties filter correctly in these predicates, including underscore-separated paths (e.g. `$.risk_level`).
- **Aggregate query paths resolve remote joins**: `SqlLinqQueryProvider` aggregate operators (`Any`/`All`/`Count`) created a `WhereClauseVisitor` without resolving remote joins, so `[JsonPath]`/remote properties were not honored inside aggregate predicates. These paths now resolve remote joins before visiting the predicate.

### Added
- Integration tests for JsonPath WHERE predicates across all three providers: equality, inequality, comparison, `IS NULL`, string `Contains`, and collection `Contains` — including underscore-separated JSON paths.

### Changed
- **Documentation reorganized** into a `docs/` hierarchy (`docs/plans`, `docs/ai-instructions`, `docs/architecture`). Provider `.csproj` files now reference the canonical `docs/ai-instructions/` location for NuGet `contentFiles`.
- **Version**: All projects promoted from `3.5.1-beta1` to `3.5.1`.

## [3.5.0] - 2026-06-18

### Added
- **SQLite Provider**: Full SQLite support including LINQ query translation, CASE/conditional projections, paging, identity and non-identity inserts, transactions, async operations, reserved-word handling, and file-path resolution for connection strings.
- **77 SQLite integration tests** with parity coverage matching SQL Server where supported.
- **AI instruction documents**: `FUNKYORM_AI_INSTRUCTIONS_SQLITE.md` for SQLite-specific guidance.

### Changed
- **Package architecture (Option D)**: `Funcular.Data.Orm` remains the single published NuGet package and now bundles SQL Server, PostgreSQL, and SQLite provider assemblies for `net8.0` and `netstandard2.0` targets.
- **Project rename**: The SQL Server provider project file has been renamed from `Funcular.Data.Orm.csproj` to `Funcular.Data.Orm.SqlServer.csproj` to align with the naming conventions of the PostgreSQL and SQLite provider projects. The published package identity (`Funcular.Data.Orm`), assembly name, and root namespace are unchanged — this is a source-level organizational change only and does not affect consumers.
- **SQLite CASE projection fix**: The `SqliteLinqQueryProvider` now correctly wires the `SqliteSelectClauseVisitor` output into the final query, enabling `Select()` projections with conditional/ternary expressions.
- **SQLite null comparison fix**: `SqliteSelectClauseVisitor.VisitBinary` now emits `IS NULL` / `IS NOT NULL` instead of `= NULL` / `!= NULL`.
- **Version**: All projects bumped to `3.5.0`.

### Fixed
- Two previously-ignored SQLite tests (`Query_SelectWithSalutationProjection_GeneratesCaseStatement`, `Query_SelectWithIsTwentyOneOrOverProjection_GeneratesCaseStatement`) now pass and are no longer skipped.

## [3.2.1] - 2026-06-11

This release completes the suite of "view-replacing" attributes, allowing users to build rich, read-only detail entities entirely through property-level decoration — no SQL views, stored procedures, or raw SQL needed. With `[JsonPath]`, `[SqlExpression]`, `[SubqueryAggregate]`, and `[JsonCollection]` now fully operational alongside the existing `[RemoteKey]` and `[RemoteProperty]` attributes, a single detail entity can simultaneously: extract typed scalars from JSON columns, compute expressions across peer columns (with provider-specific overrides), aggregate child tables via correlated subqueries (counts, sums, averages, and conditional counts), and project child record sets as inline JSON arrays. All of these attribute-driven columns participate in standard LINQ queries, including WHERE-clause filtering, enabling complex reporting projections without leaving the type-safe LINQ surface.

### Added
- **Timestamp / DatabaseGenerated column exclusion**: Properties decorated with `[Timestamp]` or `[DatabaseGenerated(DatabaseGeneratedOption.Computed)]` / `[DatabaseGenerated(DatabaseGeneratedOption.Identity)]` are now automatically excluded from INSERT and UPDATE statements. Fixes `Cannot insert an explicit value into a timestamp column` errors for entities with `rowversion` columns.
- **`[SqlExpression]` Attribute (Phase 2)**: Declare raw SQL expressions for computed properties using `{PropertyName}` tokens.
  - Tokens are resolved to fully qualified column references at query time, respecting naming conventions, `[Column]` overrides, and table aliases.
  - Supports provider-specific overrides via dual-expression constructor (`mssql:`, `postgresql:`).
  - Works in `Get<T>`, `Query<T>`, `GetList<T>`, and WHERE clauses.
- **`[SubqueryAggregate]` Attribute (Phase 3)**: Generates correlated scalar subqueries in the SELECT list.
  - Supports `AggregateFunction.Count`, `.Sum`, `.Avg`, and `.ConditionalCount`.
  - Conditional aggregates accept `ConditionColumn` and `ConditionValue` for `WHERE column = 'value'` filters within the subquery.
  - Portable across SQL Server and PostgreSQL (same correlated subquery syntax).
- **`[JsonCollection]` Attribute (Phase 4)**: Projects child records as JSON arrays.
  - **SQL Server**: `(SELECT ... FOR JSON PATH)`.
  - **PostgreSQL**: `(SELECT json_agg(row_to_json(sub)) FROM (...) sub)`.
  - Accepts `Columns` (property names to include), `OrderBy`, and resolves all names to database column names.
- **`AggregateFunction` Enum**: `Count`, `Sum`, `Avg`, `ConditionalCount` — used by `[SubqueryAggregate]`.
- **52 new integration tests** — full parity across both providers:
  - **SQL Server**: 8 JsonPath + 15 Computed Attribute + 3 Timestamp/RowVersion tests (26 total).
  - **PostgreSQL**: 8 JsonPath + 15 Computed Attribute + 3 DatabaseGenerated/Computed tests (26 total).
  - Covers: string/int/nested JSON extraction, NULL metadata, WHERE clauses on JSON values, `COALESCE` expressions, `COUNT`/`ConditionalCount` aggregates, zero-count edge cases, `FOR JSON PATH`/`json_agg` collection projection, combined all-attributes-on-one-entity, multi-row queries, and Timestamp/DatabaseGenerated column exclusion from INSERT/UPDATE.

### Improved
- **Primary key error message**: When no primary key is found for an entity, the exception now provides actionable guidance including the expected attribute (`[Key]` from `System.ComponentModel.DataAnnotations`) and naming conventions.
- **Documentation**: Added explicit guidance in Usage.md that all mapping attributes must come from `System.ComponentModel.DataAnnotations` / `System.ComponentModel.DataAnnotations.Schema` namespaces. Added Timestamp/RowVersion column handling section.

### Changed
- **Version**: All projects bumped to `3.2.1`.
- **`ISqlDialect`**: Added `ProviderName` property, `BuildScalarSubquery()`, and `BuildJsonCollectionSubquery()` methods.
- **`ResolveRemoteJoins<T>`** (both providers): Now detects `[SqlExpression]`, `[SubqueryAggregate]`, and `[JsonCollection]` attributes in addition to `[JsonPath]` and `[RemoteProperty]`/`[RemoteKey]`.
- **`BuildQueryComponents`** (both `SqlLinqQueryProvider` and `PostgreSqlLinqQueryProvider`): Fixed SQL corruption when SELECT list contains subqueries (e.g., `(SELECT COUNT(*) FROM ...)`). The `FROM`/`WHERE` keyword splitting now uses parenthesis-depth tracking (`FindOuterKeywordIndex`) to find only outer-level keywords, preventing subquery expressions from being incorrectly split.

## [3.2.0-beta1] - 2026-04-15

### Added
- **JSON Column Querying**: New `[JsonPath]` attribute enables extracting scalar values from JSON columns without SQL views.
  - Decorate a property with `[JsonPath("column", "$.path")]` to extract a value from a JSON column on the same table.
  - Supports nested paths (e.g., `$.client.name`) and typed extraction via optional `SqlType` property (e.g., `SqlType = "int"`).
  - Extracted values work in `Get<T>`, `Query<T>`, `GetList<T>`, and **WHERE clauses** — filter on JSON values using standard LINQ.
  - Full SQL Server and PostgreSQL support via `ISqlDialect.BuildJsonValueExpression()`:
    - **SQL Server**: `JSON_VALUE(column, '$.path')` with `CAST()` for typed extraction.
    - **PostgreSQL**: `column #>> '{path}' ` with `::type` casting.
  - Follows the established "Detail" entity pattern — add `[JsonPath]` to inherited Detail classes, not canonical entities.
- **Integration Test Schema**: Added `project`, `project_category`, `project_milestone`, and `project_note` tables with JSON metadata column for integration testing.
- **`vw_project_scorecard`**: Demonstration SQL view exercising all planned JSON capability categories.
- **Attribute Roadmap Documented**: Full design and examples for three additional planned attributes:
  - `[SqlExpression]` — computed/expression columns (`COALESCE`, `CONCAT`, `CASE`) with `{PropertyName}` token resolution.
  - `[SubqueryAggregate]` — correlated scalar subqueries (`COUNT`, `SUM`, conditional aggregates) replacing `OUTER APPLY`.
  - `[JsonCollection]` — project child records as JSON arrays (`FOR JSON PATH` / `json_agg`).
  - All four attributes documented in FUNKYORM_AI_INSTRUCTIONS.md, Usage.md, README.md, and AI_ARCHITECTURE_AND_DESIGN.md.

### Changed
- **Version**: All projects bumped to `3.2.0-beta1`.
- **`ISqlDialect`**: Added `BuildJsonValueExpression(qualifiedColumn, jsonPath, castType)` method.
- **`ResolveRemoteJoins<T>`**: Now detects `[JsonPath]` attributes alongside `[RemoteProperty]`/`[RemoteKey]`, appending JSON extraction expressions to `ExtraColumns` and `PropertyToColumnMap`.
- **`CreateGetOneOrSelectCommandText<T>`**: Updated to handle extra columns (from JSON extraction) even when no JOINs are present.
- **AI Instructions Renamed**: `COPILOT_INSTRUCTIONS.md` renamed to `FUNKYORM_AI_INSTRUCTIONS.md` (SQL Server) and `FUNKYORM_AI_INSTRUCTIONS_POSTGRESQL.md` (PostgreSQL) for agent-agnostic naming and collision avoidance when shared across projects. Now packed into both NuGet packages.

## [3.1.0] - 2026-04-09

### Added
- **🐘 PostgreSQL Support**: New `PostgreSqlOrmDataProvider` providing a full PostgreSQL provider with feature parity to the SQL Server provider. Both providers are included in the `Funcular.Data.Orm` package.
  - Full LINQ-to-SQL translation with PostgreSQL syntax (`LIMIT`/`OFFSET`, `RETURNING`, `EXTRACT()`, `||` string concat, `"double-quote"` identifier quoting).
  - Complete `[RemoteKey]`, `[RemoteProperty]`, and `[RemoteLink]` attribute support with BFS path resolution and automatic `LEFT JOIN` generation.
  - Npgsql 9.x for `net8.0`, Npgsql 8.x for `netstandard2.0`.
  - Reserved word quoting for PostgreSQL keywords (tables and columns).
  - 232 integration tests covering all CRUD, query, aggregate, transaction, remote property, and reserved word scenarios.
  - Docker Compose setup and CI workflow for PostgreSQL integration tests.

### Changed
- **Multi-Provider Architecture**: Both SQL Server and PostgreSQL providers now inherit from the shared `OrmDataProvider` base class via `Funcular.Data.Orm.Core`. Entity classes and LINQ query code are fully portable between providers.
- **Documentation**: README.md, Usage.md, and COPILOT_INSTRUCTIONS updated to cover both SQL Server and PostgreSQL providers.

### Fixed
- **Column Name Cache Key Mismatch**: Fixed a bug where `DiscoverColumns` and `GetCachedColumnName` used different dictionary key formats (`TypeName.Property` vs `FullTypeName.Property`), causing discovered column names (including reserved word quoting) to be ignored in favor of raw attribute values.
- **GetTableName Quoting**: The PostgreSQL provider now overrides `GetTableName<T>()` to apply `EncloseIdentifier` for reserved word table names.

## [2.3.1] - 2025-12-08

### Fixed
- **Parameter Naming in Chained Queries**: Resolved an issue where chained `Where` clauses with array `Contains` would reuse parameter names (e.g., `@p0`), causing SQL Server errors. Parameters now use unique names like `@p__linq__0`, `@p__linq__1`, etc., ensuring correct execution of complex queries.

## [2.2.0] - 2025-12-05

### Fixed
- **Span<T>.Contains Support**: Fixed an issue where `array.Contains(item)` in LINQ queries would fail due to C# 12's preference for `Span<T>.Contains` over `IEnumerable<T>.Contains`. The ORM now properly handles implicit conversions to `Span<T>` and supports both instance and extension method calls.

---

## [1.6.0]

### Fixed
- Fixed privately reported issue with predicates captured in closures not being translated correctly in some cases.

### Added
- Added CI build and integration tests using MSSQL instance locally and LocalDB in Azure.

---

## [1.5.2]

### Fixed
- Fixed #3: Unmapped properties lacking `[NotMapped]` attribute were included in some SQL statements, causing errors.

---

## [1.5.0]

### Added
- **Ternary Operator Support**: Added support for the ternary operator (`?:`) in `WHERE` clauses, projections (`SELECT`), and `ORDER BY` clauses. These are translated to SQL `CASE` statements.

---

## [1.1.2]

### Documentation
- Explicitly documented immediate vs. deferred execution behaviors of different `Query<T>` overloads.

### Changed
- Centralized Excel logging in performance tests.

---

## [1.1.1]

### Changed
- Minor improvements to integration tests, performance tests, and README.

---

## [1.1.0]

### Added
- **Delete by ID**: Added `Delete<T>(id)` method.
- **Delete Guardrails**: Added additional safety checks to `Delete<T>(predicate)` method.
- **Performance Tests**: Added Entity Framework performance comparisons and charts.

---

## [1.0.0]

### Added
- **RTM Release**: First major release.
- **Delete Methods**: Initial support for delete operations.
- **Performance Enhancements**: General performance tuning.
- **Integration Testing**: Expanded integration test coverage.

---

## [0.9.3]

### Added
- **Async Support**: Added async implementations of public methods (`GetAsync`, `InsertAsync`, etc.) with corresponding test cases.

### Changed
- Reorganized provider members for better code structure.

---

## [0.9.2] - RC-1

### Added
- **Framework Support**: Added support for .NET 4.8.
- **Feature Complete**: Declared feature complete for .NET 8 and NETSTANDARD 2.0.

### Changed
- Code cleanup and dependency compatibility updates.

---

## [0.9.1]

### Added
- **NET Standard Support**: Implemented support for .NET Standard 2.0.
- **Performance Testing**: Added performance testing for update operations.
