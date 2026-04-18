# Changelog

All notable changes to this project will be documented in this file.

## [3.2.1-beta1] - 2026-04-17

### Added
- **Timestamp / DatabaseGenerated column exclusion**: Properties decorated with `[Timestamp]` or `[DatabaseGenerated(DatabaseGeneratedOption.Computed)]` / `[DatabaseGenerated(DatabaseGeneratedOption.Identity)]` are now automatically excluded from INSERT and UPDATE statements. Fixes `Cannot insert an explicit value into a timestamp column` errors for entities with `rowversion` columns.

### Improved
- **Primary key error message**: When no primary key is found for an entity, the exception now provides actionable guidance including the expected attribute (`[Key]` from `System.ComponentModel.DataAnnotations`) and naming conventions.
- **Documentation**: Added explicit guidance in Usage.md that all mapping attributes must come from `System.ComponentModel.DataAnnotations` / `System.ComponentModel.DataAnnotations.Schema` namespaces.

### Added
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
- **46 new JSON/computed attribute integration tests** — full parity across both providers:
  - **SQL Server**: 8 JsonPath + 15 Computed Attribute tests (23 total).
  - **PostgreSQL**: 8 JsonPath + 15 Computed Attribute tests (23 total).
  - Covers: string/int/nested JSON extraction, NULL metadata, WHERE clauses on JSON values, `COALESCE` expressions, `COUNT`/`ConditionalCount` aggregates, zero-count edge cases, `FOR JSON PATH`/`json_agg` collection projection, combined all-attributes-on-one-entity, and multi-row queries.

### Changed
- **Version**: All projects bumped to `3.2.1-beta1`.
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

## [3.0.1] - 2026-02-05

Official release of v3.0.1. No changes from beta1.

## [3.0.1-beta1] - 2025-12-15

### Breaking Changes
- **Provider Architecture**: Refactored `SqlServerOrmDataProvider` to inherit from `OrmDataProvider` and use `ISqlDialect` for SQL generation.
- **ISqlDialect**: Introduced `ISqlDialect` interface to support multiple database dialects.
- **Protected Methods**: Several protected methods in `SqlServerOrmDataProvider` have been updated to use `ISqlDialect`. Custom providers inheriting from this class may need updates.
- **Insert Return Type**: The `Insert` method now returns `object` instead of `long`. This is a **breaking change** for code expecting a `long` directly, but enables support for non-integer primary keys.
  - **Migration**: Cast the result to the expected type, or use the new generic overloads.

### Added
- **Generic Insert Overloads**: Added `Insert<T, TKey>(T entity)` and `InsertAsync<T, TKey>(T entity)` to allow type-safe retrieval of the primary key.
  ```csharp
  var id = provider.Insert<Person, int>(person); // Returns int
  var guid = provider.Insert<Log, Guid>(log);    // Returns Guid
  ```
- **Remote Attributes**: Introduced `[RemoteKey]` and `[RemoteProperty]` attributes to simplify working with related data.
  - **RemoteKey**: Maps a property to a column in a related table (e.g., `Person.EmployerName` maps to `Employer.Name`).
  - **RemoteProperty**: Similar to `RemoteKey` but for non-key properties.
- **SqlServerDialect**: Implementation of `ISqlDialect` for SQL Server.
- **Guid and String Primary Key Support**: Added full support for `Guid` and `String` primary keys.
- **Non-Identity Key Handling**: The ORM now correctly handles `INSERT` statements for tables with non-identity primary keys (e.g., client-generated Guids), automatically including the PK column in the `INSERT` statement when a value is provided.

### Changed
- **Performance Tests**: Performance and Entity Framework comparison tests are now excluded from standard `dotnet test` runs. They can be executed manually using the `run-performance-tests.ps1` script.

### Fixed
- **Documentation**: Updated package icon URL in README to ensure correct rendering on NuGet.org.

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
