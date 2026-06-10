# MySQL Provider Implementation Plan

> **Goal**: Create a `Funcular.Data.Orm.MySql` provider with maximum practical feature parity to `Funcular.Data.Orm.SqlServer` and `Funcular.Data.Orm.PostgreSql`, including a complete test suite. This document identifies the (few) features that differ in MySQL, prescribes framework behavior for each, and serves as a ready-to-implement specification. Target release: **v3.6.0**.

> **TL;DR for MySQL vs the other providers**: MySQL is closer to PostgreSQL than to SQLite — it is a full server with rich native types (so almost none of SQLite's type-affinity gymnastics are needed), native `JSON`, native `DECIMAL`, stored procedures, and multi-database ("schema") support. The two real points of friction are (1) **no `RETURNING` clause** — identity is retrieved via `LAST_INSERT_ID()` / the connector's `LastInsertedId` — and (2) **backtick identifier quoting**. Default collations are **case-insensitive**, which *matches* SQL Server (unlike PostgreSQL, which needed a case-folding strategy).

---

## 1. Architecture Overview

The MySQL provider follows the same architecture as the SQL Server and PostgreSQL providers:

```
Funcular.Data.Orm.Core              (existing, shared)
    │ IOrmDataProvider               – Core CRUD contract
    │ ISqlOrmProvider                – ADO.NET connection/transaction contract
    │ ISqlDialect                    – Dialect-specific SQL generation
    │ OrmDataProvider                – Abstract base with reflection caching

Funcular.Data.Orm.MySql             (NEW project)
    │ MySql/
    │   │ MySqlOrmDataProvider.cs          – Main provider (mirrors PostgreSqlOrmDataProvider)
    │   │ MySqlDialect.cs                  – ISqlDialect for MySQL syntax (backticks, LAST_INSERT_ID, JSON)
    │   │ MySqlQueryComponents.cs          – Typed query component container (MySqlParameter)
    │   │ QueryComponents.cs               – Internal query components (paging, aggregates)
    │   │ MySqlLinqQueryProvider.cs        – IQueryProvider for LINQ-to-SQL translation
    │   │ MySqlQueryable.cs                – IOrderedQueryable<T> wrapper
    │   │ RemotePathResolver.cs            – Copy from PostgreSql (BFS is DB-agnostic)
    │ Visitors/
    │   │ MySqlWhereClauseVisitor.cs       – Adapted for MySqlParameter
    │   │ MySqlExpressionTranslator.cs     – Adapted for MySQL syntax (CONCAT, JSON_EXTRACT, etc.)
    │   │ MySqlParameterGenerator.cs       – Uses MySqlParameter / MySqlDbType
    │   │ MySqlOrderByClauseVisitor.cs     – Minimal changes from SqlServer
    │   │ MySqlSelectClauseVisitor.cs      – Adapted for MySQL CASE/aggregate syntax
    │   │ BaseExpressionVisitor.cs         – Copy (parameter-type-agnostic)
    │ Exceptions/
    │   │ RemoteKeyExceptions.cs           – Copy (DB-agnostic)
    │ MySqlStringComparison.cs             – Enum: CaseInsensitive (default) / CaseSensitive
    │ ExtensionMethods.cs

Funcular.Data.Orm.MySql.Tests       (NEW test project)
    │ Domain/                               – Mirror SqlServer/PostgreSql test entities (namespace change only)
    │   │ Entities/ (Person, Address, Organization, Country, Project, …)
    │   │ Objects/  (Person, Address, User, NonIdentity*, …)
    │   │ Enums/
    │ MySqlTestFixture.cs                    – Shared connection/schema/seed base (mirrors SqlServerTestFixture)
    │ MySqlDataProviderIntegrationTests.cs
    │ MySqlDataProviderIntegrationAsyncTests.cs
    │ MySqlDataProviderPerformanceTests.cs
    │ RemoteFeaturesTests.cs / RemoteKeyIntegrationTests.cs / RemoteKeyWhereTests.cs
    │ RemoteKeyReverseTests.cs / RichRelationshipTests.cs
    │ JsonPathIntegrationTests.cs            – [JsonPath] parity (incl. WHERE predicates, the 3.5.1 fix)
    │ ComputedAttributeTests.cs              – [SqlExpression]/[SubqueryAggregate]/[JsonCollection] parity
    │ DocumentationGapTests.cs

Database/                                   (EXTENDED – adds MySql subfolder)
    │ MySql/
        │ integration_test_db.sql            – NEW: MySQL DDL
        │ integration_test_data.sql          – NEW: seed dataset (flat INSERTs)
        │ docker-compose.yml                 – NEW: optional local MySQL for devs without a local install
```

---

## 2. Supported, Unsupported & Workaround Features

### ✅ 2.1 Features Supported Natively (parity is straightforward)

| Feature | Notes |
|---------|-------|
| **Rich native types** | `INT`, `BIGINT`, `DECIMAL(p,s)` (exact — no precision loss), `DATETIME`, `VARCHAR`, `BLOB` all map cleanly. No SQLite-style type-affinity bridging required. |
| **Native `JSON` type** | `JSON_EXTRACT(col,'$.path')` / `col ->> '$.path'` (MySQL 5.7+). `[JsonPath]` (incl. WHERE predicates), and `[JsonCollection]` via `JSON_ARRAYAGG`/`JSON_OBJECT`, are fully supportable. |
| **Stored procedures** | MySQL supports them. FunkyORM does not currently expose stored-proc execution, so this is parity-neutral — no `NotSupportedException` needed (unlike SQLite). |
| **Multi-statement concurrency** | Full multi-user concurrency (InnoDB row-level locking), like SQL Server/PostgreSQL. The 3.2.2 concurrency-safe connection model applies unchanged. |
| **Schemas** | MySQL "schema" ≡ "database". `[Table(Schema = "x")]` maps to `` `x`.`table` ``. Supportable (see §5.8). |
| **`DECIMAL` precision** | Exact fixed-point — no precision loss workaround (contrast SQLite's REAL). |
| **Correlated subqueries** | `[SubqueryAggregate]` (`COUNT`/`SUM`/`AVG`/`ConditionalCount`) works with standard correlated-subquery syntax. |

### ⚠️ 2.2 Features Requiring Workarounds

| Feature | SQL Server Behavior | MySQL Reality | Prescribed Framework Behavior |
|---------|--------------------|---------------|-------------------------------|
| **Identity return on INSERT** | `OUTPUT INSERTED.Id` | **No `RETURNING`** (Oracle MySQL; MariaDB has it, MySQL does not) | Read the auto-increment id via **`MySqlCommand.LastInsertedId`** after `ExecuteNonQuery`, or append `; SELECT LAST_INSERT_ID();` and `ExecuteScalar`. Primary strategy: `LastInsertedId`. See §3.2 / §5.5. |
| **GUID columns** | Native `UNIQUEIDENTIFIER` | No native UUID type | Store as `CHAR(36)`; set MySqlConnector `GuidFormat=Char36` so `Guid` round-trips transparently (no manual `MapEntity` conversion). `BINARY(16)` is a denser alternative (`GuidFormat=Binary16`) — defer unless storage matters. See §5.6. |
| **Boolean columns** | Native `BIT` | `BOOL`/`BOOLEAN` is an alias for `TINYINT(1)` | MySqlConnector maps `TINYINT(1)`⇄`bool` by default (`TreatTinyAsBoolean=true`). No manual conversion. |
| **`DateTimeOffset`** | Native `DATETIMEOFFSET` | No timezone-aware type (`TIMESTAMP` is stored UTC) | Store as `DATETIME(6)` (offset normalized to UTC) or `VARCHAR` ISO8601. Prescribe: persist UTC `DATETIME(6)`; document the offset is not preserved. Low priority — no current entity uses it. |
| **`AVG()` on integers** | Returns truncated `int` | Returns `DECIMAL`/`DOUBLE` | Wrap integer-typed `AVG` targets: `CAST(AVG(col) AS SIGNED)` (truncates toward zero, matching SQL Server). Mirror the SQLite plan's integer-AVG decision. |
| **Identifier case sensitivity (tables)** | Case-insensitive | Depends on `lower_case_table_names` + OS (case-**sensitive** on Linux by default, insensitive on Windows/macOS) | Document; recommend `lower_case_table_names=1` for the test DB, and rely on the existing `IgnoreUnderscoreAndCaseStringComparer` for column mapping. See §5.9. |
| **Default transaction isolation** | `READ COMMITTED` | `REPEATABLE READ` (InnoDB) | Accept MySQL default. Optionally allow callers to set isolation on `BeginTransaction`. Document the difference; tests must not depend on cross-transaction visibility semantics. |
| **`ONLY_FULL_GROUP_BY` / `sql_mode`** | n/a | Strict `sql_mode` can reject some generated SQL | Generated SQL is already standards-compliant (explicit columns, no bare aggregates). No action expected; note for risk. |

### ⛔ 2.3 Features That Genuinely Differ (none are hard blockers)

Unlike SQLite, MySQL has **no feature that must throw `NotSupportedException`** for the ORM's current surface area. The only meaningful gap is the `RETURNING` clause, handled via `LAST_INSERT_ID()`.

---

## 3. SQL Dialect Differences (SQL Server vs MySQL)

### 3.1 Identifier Quoting

| Feature | SQL Server | MySQL |
|---------|-----------|-------|
| Identifier delimiters | `[name]` | `` `name` `` (backtick) |
| Reserved word handling | `[Order]` | `` `Order` `` |
| Schema-qualified | `[schema].[table]` | `` `database`.`table` `` |

**Action**: `MySqlDialect.EncloseIdentifier()` wraps with backticks. (ANSI double-quote works only when `sql_mode=ANSI_QUOTES`; do **not** rely on it — use backticks.)

### 3.2 INSERT with Identity Return

| Feature | SQL Server | MySQL |
|---------|-----------|-------|
| Identity return | `OUTPUT INSERTED.Id` | `LAST_INSERT_ID()` (no `RETURNING`) |
| Auto-increment | `IDENTITY(1,1)` | `AUTO_INCREMENT` |

**Action**: `MySqlDialect.BuildInsertCommand()` emits a plain `INSERT`. The provider reads the new id from `MySqlCommand.LastInsertedId` (preferred) immediately after `ExecuteNonQuery`. For async paths use `ExecuteNonQueryAsync` then `LastInsertedId`. Non-identity PKs (Guid/string) follow the existing "PK supplied by caller" path — no id retrieval.

### 3.3 SELECT / Paging

| Feature | SQL Server | MySQL |
|---------|-----------|-------|
| Top N | `SELECT TOP N ...` | `... LIMIT N` (at end) |
| Paging | `OFFSET x ROWS FETCH NEXT y ROWS ONLY` | `LIMIT y OFFSET x` (or `LIMIT x, y`) |

**Action**: `MySqlLinqQueryProvider` appends `LIMIT`/`OFFSET` at the end (identical strategy to PostgreSQL/SQLite). Keep the `ORDER BY id` fallback for deterministic paging. Note: MySQL requires a `LIMIT` when `OFFSET` is present — emit `LIMIT 18446744073709551615 OFFSET x` if `Skip` is used without `Take` (the canonical MySQL "offset to end" idiom).

### 3.4 Data Types

| Concept | SQL Server | MySQL |
|---------|-----------|-------|
| Integer auto-inc | `INT IDENTITY(1,1)` | `INT AUTO_INCREMENT` |
| Big integer | `BIGINT` | `BIGINT` |
| GUID | `UNIQUEIDENTIFIER` | `CHAR(36)` (via `GuidFormat`) |
| Boolean | `BIT` | `TINYINT(1)` |
| DateTime | `DATETIME2` | `DATETIME(6)` |
| DateTimeOffset | `DATETIMEOFFSET` | `DATETIME(6)` (UTC) |
| String (Unicode) | `NVARCHAR(N)` | `VARCHAR(N)` charset `utf8mb4` |
| Text | `NVARCHAR(MAX)` | `TEXT` / `LONGTEXT` |
| Decimal | `DECIMAL(p,s)` | `DECIMAL(p,s)` (exact) |
| Binary | `VARBINARY(N)` | `VARBINARY(N)` / `BLOB` |
| JSON | `NVARCHAR(MAX)` + `JSON_VALUE` | native `JSON` |

### 3.5 Parameters

| Feature | SQL Server | MySQL |
|---------|-----------|-------|
| Parameter prefix | `@paramName` | `@paramName` (same) |
| Parameter type | `SqlParameter` / `SqlDbType` | `MySqlParameter` / `MySqlDbType` |
| Null handling | `DBNull.Value` | `DBNull.Value` (same) |

**Action**: All visitors and the parameter generator use `MySqlConnector.MySqlParameter` and `MySqlConnector.MySqlDbType`. Parameter naming (`@p__linq__0`, …) is unchanged.

### 3.6 Aggregates

`COUNT(*)`, `MIN`, `MAX`, `SUM`, `CASE WHEN EXISTS` are identical. **`AVG()` on integer targets**: wrap with `CAST(AVG(col) AS SIGNED)` to mimic SQL Server's truncating integer behavior (§2.2).

### 3.7 String Functions / Expression Translation

| Operation | SQL Server | MySQL |
|-----------|-----------|-------|
| Concatenation | `a + b` / `CONCAT` | `CONCAT(a, b)` (the `+` operator does numeric addition in MySQL — **must** use `CONCAT`) |
| Substring | `SUBSTRING(x, a, b)` | `SUBSTRING(x, a, b)` (same) |
| Length | `LEN(x)` | `CHAR_LENGTH(x)` |
| Upper/Lower | `UPPER/LOWER` | `UPPER/LOWER` (same) |
| `StartsWith` | `LIKE 'p%'` | `LIKE 'p%'` (same) |
| `Contains` (string) | `LIKE '%v%'` | `LIKE '%v%'` (same) |
| Collection `Contains` | `IN (...)` | `IN (...)` (same) |
| Date part | `DATEPART`/`YEAR()` | `YEAR()`, `MONTH()`, `EXTRACT(... FROM ...)` |

**Action**: `MySqlExpressionTranslator` must emit `CONCAT(...)` for string `+`, `CHAR_LENGTH` for `.Length`, and MySQL date functions. Mirror `PostgreSqlExpressionTranslator` (which already uses `||`/`CONCAT`-style overrides) — the Postgres translator is the closest template.

### 3.8 JSON Support (Native)

| Feature | SQL Server | MySQL |
|---------|-----------|-------|
| Scalar path | `JSON_VALUE(col,'$.p')` | `JSON_UNQUOTE(JSON_EXTRACT(col,'$.p'))` ≡ `col ->> '$.p'` |
| Typed extraction | `CAST(JSON_VALUE(...) AS int)` | `CAST(col ->> '$.p' AS SIGNED)` / `AS DECIMAL(p,s)` / `AS CHAR` |
| Array projection | `FOR JSON PATH` | `JSON_ARRAYAGG(JSON_OBJECT('k', col, …))` |

**Action**: Implement `ISqlDialect.BuildJsonValueExpression(qualifiedColumn, jsonPath, castType)` for MySQL as `JSON_UNQUOTE(JSON_EXTRACT(col, '$.path'))` with optional `CAST(... AS <mysqlType>)`. Implement `BuildJsonCollectionSubquery()` with `JSON_ARRAYAGG`/`JSON_OBJECT`. This gives `[JsonPath]` (including the 3.5.1 WHERE-predicate behavior) and `[JsonCollection]` full parity.

### 3.9 `[SqlExpression]` Provider Override

The dual-expression `[SqlExpression]` attribute currently recognizes `mssql:` and `postgresql:` prefixes. **Action**: extend the override parser to recognize a **`mysql:`** prefix, and set `MySqlDialect.ProviderName = "mysql"` so provider-specific expressions resolve correctly. (Verify the prefix-matching site in `Core` / `SqlExpressionTranslator`.)

### 3.10 Schema Discovery

| Feature | SQL Server | MySQL |
|---------|-----------|-------|
| Schema query | `SELECT * ... SchemaOnly` | `SELECT * FROM tbl LIMIT 0` with `CommandBehavior.SchemaOnly` |
| Column schema API | `reader.GetColumnSchema()` | `reader.GetColumnSchema()` (MySqlConnector supports it) / `GetSchemaTable()` |

**Action**: `DiscoverColumns<T>()` uses `SELECT * FROM tablename LIMIT 0` + `GetColumnSchema()`/`GetSchemaTable()`, mapping names via `IgnoreUnderscoreAndCaseStringComparer`.

### 3.11 Error Handling

| Condition | SQL Server (number) | MySQL (`MySqlException.Number`) |
|-----------|--------------------|-------------------------------|
| Table not found | 208 | **1146** |
| Unique violation | 2627 / 2601 | **1062** (ER_DUP_ENTRY) |
| FK violation (insert/update child) | 547 | **1452** |
| FK violation (delete/update parent) | 547 | **1451** |
| Unknown column | 207 | **1054** |
| No default for field | 515 | **1364** |

**Action**: `HandleSqlException<T>` catches `MySqlConnector.MySqlException`, switches on `.Number` (and optionally `.SqlState`), and maps to the same friendly exceptions the other providers raise.

### 3.12 Reserved Words

MySQL keyword set differs from SQL Server. Common overlaps with entity/column names: `ORDER`, `GROUP`, `SELECT`, `KEY`, `DEFAULT`, `RANK`, `READ`, `USAGE`, `LEAD`, `LAG`, `SYSTEM`, `CALL`. **Action**: populate `MySqlDialect._reservedWords` from the [MySQL keyword list](https://dev.mysql.com/doc/refman/8.0/en/keywords.html) (use the 8.0 reserved subset).

### 3.13 String Comparison & Case Sensitivity

| Feature | SQL Server | MySQL |
|---------|-----------|-------|
| Default `=` on strings | Case-insensitive (collation) | **Case-insensitive** (e.g. `utf8mb4_0900_ai_ci`, `utf8mb4_general_ci`) |
| Default `LIKE` | Case-insensitive | Case-insensitive (for `_ci` collations) |

**Decision**: MySQL's default behavior already matches SQL Server — **no case-folding workaround required by default** (a notable simplification vs the PostgreSQL provider). Provide a `MySqlStringComparison` enum for symmetry: `CaseInsensitive` (default, native) and `CaseSensitive` (append `COLLATE utf8mb4_bin` to string comparisons). See §5.7.

---

## 4. Shared vs Provider-Specific Code

### 4.1 Code That Can Be Reused Directly (Copy + namespace change)

| Component | Source | Notes |
|-----------|--------|-------|
| `Funcular.Data.Orm.Core` (entire project) | `Funcular.Data.Orm.Core\` | No changes |
| `RemotePathResolver.cs` | `PostgreSql\PostgreSql\RemotePathResolver.cs` | BFS path resolution is DB-agnostic |
| `RemoteKeyExceptions.cs` | `PostgreSql\Exceptions\RemoteKeyExceptions.cs` | Exceptions are DB-agnostic |
| `BaseExpressionVisitor<T>` | `PostgreSql\Visitors\BaseExpressionVisitor.cs` | No parameter-type dependency |
| `MySqlQueryable<T>` | `PostgreSql\PostgreSql\PostgreSqlQueryable.cs` | Generic `IOrderedQueryable<T>` wrapper |
| `OrderByClauseVisitor` | `PostgreSql\Visitors\PostgreSqlOrderByClauseVisitor.cs` | Review; likely identical |

### 4.2 Code That Requires Adaptation

| Component | Postgres Dependency | MySQL Change |
|-----------|---------------------|--------------|
| `PostgreSqlOrmDataProvider` | `NpgsqlConnection/Command/DataReader/Parameter` | → `MySqlConnection/Command/DataReader/Parameter`; identity via `LastInsertedId` |
| `PostgreSqlDialect` | `"double-quote"`, `RETURNING`, `||` | → backticks, `LAST_INSERT_ID()`, `CONCAT`, MySQL JSON funcs |
| `WhereClauseVisitor<T>` | `NpgsqlParameter` | → `MySqlParameter`; optional `COLLATE utf8mb4_bin` |
| `ExpressionTranslator` | Npgsql syntax | → `MySqlParameter`; `CONCAT`, `CHAR_LENGTH`, `JSON_EXTRACT`, date funcs |
| `ParameterGenerator` | `NpgsqlDbType` | → `MySqlDbType` (replicate **all** type branches incl. enum/unsigned) |
| `SelectClauseVisitor<T>` | `NpgsqlParameter` | → `MySqlParameter`; integer-`AVG` `CAST(... AS SIGNED)` |
| `QueryComponents` / `*QueryComponents<T>` | `List<NpgsqlParameter>` | → `List<MySqlParameter>` |
| `LinqQueryProvider<T>` | `Postgres provider`, `LIMIT/OFFSET` | → `MySqlOrmDataProvider`; `LIMIT/OFFSET` (offset-to-end idiom) |

### 4.3 Refactoring Opportunity (Future — Out of Scope)

The fourth copy-and-adapt provider is the strongest signal yet to extract shared visitor/parameter abstractions into `Core` (generic `IParameterGenerator`, `IDbDataParameter`-based visitors, `RemotePathResolver`/`RemoteKeyExceptions` into Core). **Defer** to keep 3.6.0 a parallel implementation consistent with the existing three providers; capture as a 4.0 refactor candidate.

---

## 5. Key Design Decisions

### 5.1 Driver: **MySqlConnector** (not Oracle `MySql.Data`)
**Decision**: Use **[MySqlConnector](https://mysqlconnector.net/)** (`MySqlConnector` NuGet, **MIT-licensed**, async-first, high-performance, ADO.NET-compliant). Oracle's `MySql.Data` is GPL-2.0-with-FOSS-exception, which is a poor fit for this **MIT** package and has a heavier/async-weaker history. MySqlConnector multi-targets `netstandard2.0` and `net8.0`, so a single `PackageReference` covers both TFMs (simpler than Npgsql's conditional-version dance).

### 5.2 External Database Requirement
**Decision**: Like SQL Server (LocalDB / real instance) and PostgreSQL (Docker), MySQL requires a running server. Local dev uses the developer's installed MySQL (this workstation has MySQL + Workbench). CI uses a **MySQL service container** (§9). Connection resolved from env var **`FUNKY_MYSQL_CONNECTION`**, falling back to a localhost default — identical pattern to `FUNKY_CONNECTION` (SQL Server) and the Postgres tests.

### 5.3 NuGet Package Structure
**Decision**: Match the established "Option D" architecture. The single published `Funcular.Data.Orm` package (built from the SqlServer project) bundles the provider assemblies. Add `Funcular.Data.Orm.MySql` as a project reference from the SqlServer packable project (alongside PostgreSql and Sqlite), conditioned off `net48` like the others, so `net8.0`/`netstandard2.0` consumers get the MySQL provider in the same package. The MySql project itself is `GeneratePackageOnBuild=False` (bundled, not separately published) — mirror `Funcular.Data.Orm.PostgreSql.csproj` exactly. **Confirm with maintainer** whether MySQL should ship in the unified package or as a standalone `Funcular.Data.Orm.MySql` package (see Open Items §18).

### 5.4 `RemotePathResolver` Location
**Decision**: Copy into the MySql project (consistent with the other three providers). Do not move to Core in this release.

### 5.5 Identity Retrieval (`LAST_INSERT_ID()`)
**Decision**: After an identity `INSERT`, read `MySqlCommand.LastInsertedId` (a `long`) and assign it to the entity's key property (with the existing key-type coercion). This avoids a second round trip. Fallback/alternative for clarity in generated SQL logs: append `; SELECT LAST_INSERT_ID();`. Use `LastInsertedId` as the implementation; document the SQL equivalent.

### 5.6 GUID Storage
**Decision**: Store `Guid` keys/columns as `CHAR(36)` and add `GuidFormat=Char36` to the connection string so MySqlConnector transparently maps `Guid`⇄`CHAR(36)`. This keeps `MapEntity<T>` free of manual Guid conversion and keeps values human-readable in Workbench. `BINARY(16)` (`GuidFormat=Binary16`) is a denser future option — out of scope for 3.6.0.

### 5.7 Case Sensitivity — `MySqlStringComparison` Enum
**Decision**: Default `CaseInsensitive` (native `_ci` collation; matches SQL Server — **no SQL workaround emitted**). Provide `MySqlStringComparison.CaseSensitive` to append `COLLATE utf8mb4_bin` to string equality/comparison expressions for callers who need it. Stored as a field on the provider and threaded into the visitors, mirroring `SqliteStringComparison`.

### 5.8 Schema (= Database) Support
**Decision**: Map `[Table(Schema = "x")]` to `` `x`.`table` ``. If no schema is specified, use the database from the connection string (no qualifier emitted). This is more capable than SQLite (no schemas) and on par with SQL Server/PostgreSQL.

### 5.9 Table-Name Case Sensitivity
**Decision**: Out of FunkyORM's control (server `lower_case_table_names`). Recommend the **test database** run with `lower_case_table_names=1` (the Windows default) for portability, and document that on case-sensitive servers (typical Linux), entity/table casing must match the physical tables. Column mapping continues to use `IgnoreUnderscoreAndCaseStringComparer`.

### 5.10 Integer Aggregate Truncation
**Decision**: For integer-typed `AVG`, emit `CAST(AVG(col) AS SIGNED)` so `Query<T>().Average(x => x.IntProp)` matches SQL Server's truncated-integer result across providers.

### 5.11 Transaction Isolation
**Decision**: Accept MySQL's InnoDB default (`REPEATABLE READ`). Do not force `READ COMMITTED`. Tests must not rely on cross-transaction visibility differences. Optionally expose an isolation-level parameter on `BeginTransaction` if the base contract already does.

### 5.12 Connection Management
**Decision**: Reuse the 3.2.2 concurrency-safe model — each non-transactional operation uses its own pooled `MySqlConnection`; transactional operations guard against concurrent use. MySqlConnector pools by default. `EnsureConnectionOpen`/`CloseConnectionIfNoTransaction`/`ConnectionScope` mirror the Postgres provider.

---

## 6. Implementation Steps

### Phase 1: Project Setup
1. **`Funcular.Data.Orm.MySql.csproj`** — `TargetFrameworks=net8.0;netstandard2.0`; `PackageReference MySqlConnector`; project reference to `Core`; `InternalsVisibleTo` for the test project; `GeneratePackageOnBuild=False`. Copy `Funcular.Data.Orm.PostgreSql.csproj` and adapt id/title/tags/version (`3.6.0`).
2. **`Funcular.Data.Orm.MySql.Tests.csproj`** — `net8.0`; MSTest packages; references to MySql + Core.
3. **Add both projects to `FunkyORM.sln`** (`dotnet sln add`).
4. **Wire into the bundled package**: add a `net48`-excluded `ProjectReference` to `Funcular.Data.Orm.MySql` in `Funcular.Data.Orm.SqlServer.csproj` (pending §18 packaging decision).
5. **`Database/MySql/`**: `integration_test_db.sql`, `integration_test_data.sql`, `docker-compose.yml`; update `Database/README.md`.

### Phase 2: Core Provider Implementation
1. `MySqlStringComparison` enum (§5.7).
2. `MySqlDialect : ISqlDialect` — backtick `EncloseIdentifier`; reserved words; `BuildInsertCommand` (plain INSERT, identity via provider); standard UPDATE/DELETE/SELECT; `ProviderName="mysql"`; `BuildJsonValueExpression` (JSON_EXTRACT); `BuildScalarSubquery`/`BuildJsonCollectionSubquery` (JSON_ARRAYAGG). 
3. `MySqlParameterGenerator` — CLR→`MySqlDbType` mapping; replicate **every** branch from `ParameterGenerator.GetSqlDbType()` (int/long/short/byte/sbyte/ushort/uint/ulong/enum/string/double/float/decimal/bool/DateTime/DateTimeOffset/Guid/byte[]).
4. `MySqlExpressionTranslator` — `CONCAT` for string `+`, `CHAR_LENGTH`, date funcs, `JSON_EXTRACT`, optional `COLLATE utf8mb4_bin`.
5. Visitors — `MySqlWhereClauseVisitor` (incl. the 3.5.1 `_remotePropertyMap` lookup in `VisitMethodCall`), `MySqlOrderByClauseVisitor`, `MySqlSelectClauseVisitor` (integer-AVG `CAST AS SIGNED`).
6. `MySqlQueryComponents<T>` / internal `QueryComponents` with `List<MySqlParameter>`.
7. `MySqlQueryable<T>`.
8. `MySqlLinqQueryProvider<T>` — `LIMIT`/`OFFSET` (offset-to-end idiom); ensure aggregate paths (`Any`/`All`/`Count`) resolve remote joins (the 3.5.1 fix).
9. `MySqlOrmDataProvider : OrmDataProvider, ISqlOrmProvider` — constructor (connectionString + optional `MySqlStringComparison`/connection/transaction/dialect); connection mgmt; `DiscoverColumns`; full CRUD (sync+async) with `LastInsertedId` on insert; `MapEntity<T>` (mostly native — minimal special handling thanks to MySqlConnector); transactions; `HandleSqlException` (`MySqlException.Number`); remote-property support; all internal members the LINQ provider needs (caches, `GenerateWhereClause`, `CreateGetOneOrSelectCommandText`, `ExecuteReaderList/Single` sync+async, `GetTableNameInternal`, `ConnectionScope`).
10. Copy `RemotePathResolver` + `RemoteKeyExceptions` (namespace change).

---

## 7. Database Scripts

### 7.1 MySQL DDL (`Database/MySql/integration_test_db.sql`)

```sql
-- MySQL DDL for FunkyORM integration tests (MySQL 8.0+, InnoDB, utf8mb4)
-- Usage: mysql -u root -p < integration_test_db.sql
CREATE DATABASE IF NOT EXISTS funky_db CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
USE funky_db;

CREATE TABLE IF NOT EXISTS country (
    id   INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(100) NOT NULL
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS address (
    id               INT AUTO_INCREMENT PRIMARY KEY,
    line_1           VARCHAR(200) NOT NULL,
    line_2           VARCHAR(200) NULL,
    city             VARCHAR(100) NOT NULL,
    state_code       VARCHAR(10)  NOT NULL,
    postal_code      VARCHAR(20)  NOT NULL,
    country_id       INT NULL,
    dateutc_created  DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    dateutc_modified DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    CONSTRAINT fk_address_country FOREIGN KEY (country_id) REFERENCES country(id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS organization (
    id                       INT AUTO_INCREMENT PRIMARY KEY,
    name                     VARCHAR(100) NOT NULL,
    headquarters_address_id  INT NULL,
    row_version              TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT fk_org_address FOREIGN KEY (headquarters_address_id) REFERENCES address(id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS person (
    id               INT AUTO_INCREMENT PRIMARY KEY,
    first_name       VARCHAR(100) NOT NULL,
    middle_initial   VARCHAR(5)   NULL,
    last_name        VARCHAR(100) NOT NULL,
    birthdate        DATE NULL,
    gender           VARCHAR(10)  NULL,
    uniqueid         CHAR(36)     NULL,
    employer_id      INT NULL,
    dateutc_created  DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    dateutc_modified DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    CONSTRAINT fk_person_org FOREIGN KEY (employer_id) REFERENCES organization(id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS person_address (
    id                 INT AUTO_INCREMENT PRIMARY KEY,
    person_id          INT NOT NULL,
    address_id         INT NOT NULL,
    is_primary         TINYINT(1) NOT NULL DEFAULT 0,
    address_type_value INT NOT NULL DEFAULT 0,
    dateutc_created    DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    dateutc_modified   DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    CONSTRAINT fk_pa_person  FOREIGN KEY (person_id)  REFERENCES person(id),
    CONSTRAINT fk_pa_address FOREIGN KEY (address_id) REFERENCES address(id)
) ENGINE=InnoDB;
CREATE INDEX ix_person_address_person  ON person_address(person_id);
CREATE INDEX ix_person_address_address ON person_address(address_id);

-- JSON + computed-attribute test tables (parity with SqlServer/PostgreSql JsonPath tests)
CREATE TABLE IF NOT EXISTS project_category (
    id   INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    code VARCHAR(50)  NOT NULL
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS project (
    id               INT AUTO_INCREMENT PRIMARY KEY,
    name             VARCHAR(200) NOT NULL,
    organization_id  INT NOT NULL,
    lead_id          INT NULL,
    category_id      INT NULL,
    budget           DECIMAL(12,2) NULL,
    score            INT NULL,
    metadata         JSON NULL,
    dateutc_created  DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    dateutc_modified DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6))
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS project_milestone (
    id             INT AUTO_INCREMENT PRIMARY KEY,
    project_id     INT NOT NULL,
    title          VARCHAR(200) NOT NULL,
    status         VARCHAR(50)  NOT NULL DEFAULT 'pending',
    due_date       DATE NULL,
    completed_date DATE NULL
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS project_note (
    id              INT AUTO_INCREMENT PRIMARY KEY,
    project_id      INT NOT NULL,
    author_id       INT NULL,
    content         TEXT NOT NULL,
    category        VARCHAR(50) NOT NULL DEFAULT 'general',
    dateutc_created DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6))
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS non_identity_guid_entity (
    id   CHAR(36) PRIMARY KEY,
    name VARCHAR(100) NOT NULL
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS non_identity_string_entity (
    id   VARCHAR(50) PRIMARY KEY,
    name VARCHAR(100) NOT NULL
) ENGINE=InnoDB;

-- Reserved-word table (tests backtick quoting)
CREATE TABLE IF NOT EXISTS `user` (
    `key`    INT AUTO_INCREMENT PRIMARY KEY,
    `name`   VARCHAR(100) NOT NULL,
    `order`  INT NOT NULL,
    `select` INT NOT NULL DEFAULT 0
) ENGINE=InnoDB;
```

### 7.2 Seed Data Strategy
Two complementary approaches (mirroring SQLite):
1. **`Database/MySql/integration_test_data.sql`** — small representative dataset as flat `INSERT`s for manual testing/Workbench.
2. **`MySqlTestFixture` C# seeding** — the fixture's `EnsureSchema()` + seed helpers generate the full dataset via the provider's own `Insert<T>()` (validates insert logic, avoids a huge SQL file). Mirror `SqlServerTestFixture`/`JsonPathIntegrationTests` setup pattern.

---

## 8. Test Implementation

### 8.1 Test Infrastructure (mirrors SqlServerTestFixture)

```csharp
[TestInitialize]
public void Setup()
{
    _sb.Clear();
    _connectionString = Environment.GetEnvironmentVariable("FUNKY_MYSQL_CONNECTION") ??
        "Server=localhost;Port=3306;Database=funky_db;User ID=funky;Password=funky;" +
        "GuidFormat=Char36;AllowUserVariables=true;";
    EnsureSchema();          // CREATE TABLE IF NOT EXISTS ... (idempotent)
    SeedTestData();          // via _provider.Insert<T>()
    _provider = new MySqlOrmDataProvider(_connectionString)
    {
        Log = s => { Debug.WriteLine(s); _sb.AppendLine(s); }
    };
}

[TestCleanup]
public void Cleanup()
{
    CleanupTestData();
    _provider?.Dispose();
}
```

`EnsureSchema()` runs the §7.1 DDL with `CREATE TABLE IF NOT EXISTS` so it is safe to run before every test class, exactly like the SQL Server fixture.

### 8.2 Test Class Mapping

| SQL Server / PostgreSql Test | MySQL Test | Coverage |
|------------------------------|------------|----------|
| `SqlDataProviderIntegrationTests` | `MySqlDataProviderIntegrationTests` | Core CRUD |
| `SqlDataProviderIntegrationAsyncTests` | `MySqlDataProviderIntegrationAsyncTests` | Async CRUD |
| `RemoteFeaturesTests` | `MySqlRemoteFeaturesTests` | Remote property/key population |
| `RemoteKeyIntegrationTests` / `…WhereTests` / `…ReverseTests` | `MySql…` equivalents | Remote key chains + filtering |
| `RichRelationshipTests` | `MySqlRichRelationshipTests` | Many-to-many / link tables |
| `JsonPathIntegrationTests` | `MySqlJsonPathIntegrationTests` | `[JsonPath]` incl. WHERE predicates (3.5.1) |
| Computed-attribute tests | `MySqlComputedAttributeTests` | `[SqlExpression]`/`[SubqueryAggregate]`/`[JsonCollection]` |
| `DocumentationGapTests` | `MySqlDocumentationGapTests` | Docs examples |
| Performance tests | `MySqlDataProviderPerformanceTests` | Benchmarks (excluded from CI filter) |

### 8.3 Domain Entity Reuse
Copy domain entities from the PostgreSql/SqlServer test project with **namespace change only**. Same `[Table]`, `[Column]`, `[Key]`, `[RemoteKey]`, `[RemoteProperty]`, `[RemoteLink]`, `[JsonPath]`, `[SqlExpression]`, `[SubqueryAggregate]`, `[JsonCollection]` attributes work unchanged — validating provider-agnostic entities.

---

## 9. CI Pipeline Integration

### 9.1 GitHub Actions — MySQL Service Container (mirrors the PostgreSQL workflow)

```yaml
# .github/workflows/build-and-test-mysql.yml
name: MySQL Integration Tests
on:
  push:
    branches: [ main, master, 'development/**' ]
  pull_request:
    branches: [ main, master, 'development/**' ]

jobs:
  test-mysql:
    runs-on: ubuntu-latest
    services:
      mysql:
        image: mysql:8.0
        env:
          MYSQL_ROOT_PASSWORD: root
          MYSQL_DATABASE: funky_db
        ports: [ "3306:3306" ]
        options: >-
          --health-cmd="mysqladmin ping -h 127.0.0.1 -uroot -proot"
          --health-interval=10s --health-timeout=5s --health-retries=10
    env:
      FUNKY_MYSQL_CONNECTION: "Server=127.0.0.1;Port=3306;Database=funky_db;User ID=root;Password=root;GuidFormat=Char36;AllowUserVariables=true;"
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - name: Initialize schema
        run: mysql -h 127.0.0.1 -uroot -proot funky_db < Database/MySql/integration_test_db.sql
      - run: dotnet restore
      - run: dotnet build Funcular.Data.Orm.MySql.Tests/Funcular.Data.Orm.MySql.Tests.csproj -c Release --no-restore
      - name: Run MySQL integration tests
        run: >
          dotnet test Funcular.Data.Orm.MySql.Tests/Funcular.Data.Orm.MySql.Tests.csproj
          -c Release --no-build
          --filter "FullyQualifiedName!~PerformanceTests & FullyQualifiedName!~EntityFramework"
```

### 9.2 CI Comparison

| Aspect | SQL Server | PostgreSQL | SQLite | **MySQL** |
|--------|-----------|------------|--------|-----------|
| Runner | windows-latest | ubuntu-latest | any | **ubuntu-latest** |
| External dep | LocalDB | Docker service | none | **Docker service (mysql:8)** |
| Startup | ~30–60s | ~5–10s | 0s | **~15–25s (health-gated)** |

---

## 10. Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|-----------|
| `RETURNING` absence → wrong/empty identity | **High** | Use `MySqlCommand.LastInsertedId`; cover with insert-id tests for identity + non-identity (Guid/string) PKs |
| String `+` translated as numeric add | **High** | `MySqlExpressionTranslator` must emit `CONCAT`; explicit concat tests |
| Table-name case sensitivity on Linux CI vs Windows dev | **Medium** | Lowercase physical table names in DDL; `lower_case_table_names` note; CI uses lowercase tables |
| GUID mapping (CHAR(36)) mismatch | **Medium** | `GuidFormat=Char36` in connection string; round-trip tests for Guid PK |
| Strict `sql_mode`/`ONLY_FULL_GROUP_BY` rejecting SQL | **Medium** | Generated SQL is explicit-column; validate against default 8.0 `sql_mode` |
| `MySqlConnector` netstandard2.0 support | **Low** | MySqlConnector supports ns2.0 + net8.0; single PackageReference |
| `[SqlExpression]` `mysql:` prefix unrecognized | **Medium** | Extend the dual-expression parser + set `ProviderName="mysql"`; test provider-override expressions |
| Default REPEATABLE READ vs READ COMMITTED | **Low** | Document; tests avoid cross-transaction visibility assumptions |
| `DATETIME(6)` precision / `UTC_TIMESTAMP` defaults | **Low** | Use microsecond precision; map via native `DateTime` |

---

## 11. Estimated Scope

| Component | Files | Complexity |
|-----------|-------|-----------|
| `Funcular.Data.Orm.MySql` project | ~16 | Medium |
| `Funcular.Data.Orm.MySql.Tests` project | ~22 | Medium |
| `Database/MySql/` scripts + docker-compose | ~3 | Low |
| CI workflow | ~1 | Low |
| **Total** | **~42 files** | **Medium** |

### Approximate New LOC

| Component | LOC |
|-----------|-----|
| Provider (`MySqlOrmDataProvider`) | ~1,400 (mostly native types — less than SQLite) |
| Dialect (`MySqlDialect`) | ~160 (backticks, LAST_INSERT_ID, JSON, reserved words) |
| Visitors (Where/OrderBy/Select/Translator) | ~650 (CONCAT, CHAR_LENGTH, JSON_EXTRACT, AVG cast) |
| Parameter Generator | ~100 |
| Query Components + Queryable | ~200 |
| RemotePathResolver + Exceptions (copies) | ~200 |
| `MySqlStringComparison` enum | ~20 |
| Tests (all classes) | ~1,700 |
| Database scripts (DDL + seed) | ~200 |
| CI workflow YAML | ~40 |
| **Total** | **~4,670 LOC** |

---

## 12. Dependencies

| Package | Project | Version | Notes |
|---------|---------|---------|-------|
| `MySqlConnector` | `Funcular.Data.Orm.MySql` | latest stable (2.x) | MIT-licensed ADO.NET driver; multi-targets ns2.0 + net8.0 |
| `Microsoft.NET.Test.Sdk` | `…MySql.Tests` | 17.x | Test infra |
| `MSTest.TestAdapter` / `MSTest.TestFramework` | `…MySql.Tests` | 3.x | Test framework |
| `coverlet.collector` | `…MySql.Tests` | 6.x | Coverage |

**Project references**: MySql → Core; MySql.Tests → MySql + Core; (packaging) SqlServer → MySql (net48-excluded).

---

## 13. Success Criteria

1. **Build**: `Funcular.Data.Orm.MySql` compiles for `net8.0` + `netstandard2.0`; tests compile for `net8.0`.
2. **CRUD parity**: `Get`/`Query`/`GetList`/`Insert`/`Update`/`Delete` (sync + async) behave identically to SQL Server/PostgreSQL, including identity retrieval via `LAST_INSERT_ID()`.
3. **LINQ translation**: `WHERE`, `ORDER BY`, paging (`Skip`/`Take`), aggregates (`Count`/`Any`/`All`/`Min`/`Max`/`Average`/`Sum`) translate to valid MySQL.
4. **Remote features**: `[RemoteKey]`/`[RemoteProperty]`/`[RemoteLink]` generate correct `LEFT JOIN`s; reverse + WHERE filtering pass.
5. **Computed/JSON attributes**: `[JsonPath]` (incl. WHERE predicates, the 3.5.1 fix), `[SqlExpression]` (with `mysql:` override), `[SubqueryAggregate]`, `[JsonCollection]` pass.
6. **Type round-trip**: int/long/short/unsigned/string/bool/Guid/DateTime/decimal/enum/byte[] round-trip correctly.
7. **Safety**: Delete transaction mandate + predicate guard identical to other providers.
8. **Reserved words**: backtick quoting verified via the `` `user` `` table.
9. **Case insensitivity**: default string comparison matches SQL Server; `MySqlStringComparison.CaseSensitive` opt-in works.
10. **CI**: green against a MySQL 8.0 service container on `ubuntu-latest`.
11. **Convention compliance**: Id/table/column conventions + `IgnoreUnderscoreAndCaseStringComparer` work unchanged.

---

## 14. Implementation Order (Recommended)

1. `Database/MySql/` DDL + seed + docker-compose; update `Database/README.md`.
2. Create projects, add to solution, wire bundled package reference.
3. `MySqlStringComparison` enum.
4. `MySqlDialect` (foundation for all SQL gen).
5. `MySqlParameterGenerator`.
6. `MySqlExpressionTranslator` + visitor classes.
7. `MySqlQueryComponents` + `MySqlQueryable`.
8. `MySqlOrmDataProvider` (largest piece; identity-return + error mapping are the focus areas).
9. `MySqlLinqQueryProvider`.
10. Copy `RemotePathResolver` + `RemoteKeyExceptions`.
11. Test domain entities (copy + namespace).
12. `MySqlTestFixture` (schema + seed).
13. Integration tests (sync), then async.
14. Remote-feature tests; JsonPath + computed-attribute tests.
15. RichRelationship / DocumentationGap / Performance.
16. CI workflow `build-and-test-mysql.yml`.
17. Build + validate locally and in CI.
18. Changelog + README; version bump to `3.6.0-beta1`.

---

## 15. Appendix: Project File Templates

### `Funcular.Data.Orm.MySql.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;netstandard2.0</TargetFrameworks>
    <Nullable>disable</Nullable>
    <NoWarn>MSB3277;MSB3247;MSB3268;NU1701</NoWarn>
    <Copyright>Funcular Labs, Inc.</Copyright>
    <Description>MySQL provider for FunkyORM, a lightweight micro-ORM designed for simplicity and speed. Full LINQ-to-SQL, remote keys/properties, transactions, JSON querying, and reserved word handling. Entity classes and query code are portable between providers. Uses the MIT-licensed MySqlConnector driver. Supports .NET 8 and .NET Standard 2.0.</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/FuncularLabs/Funcular.FunkyOrm</PackageProjectUrl>
    <RepositoryUrl>https://github.com/FuncularLabs/Funcular.FunkyOrm.git</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageTags>csharp;sql;orm;mysql;mariadb;entity;lambda;query</PackageTags>
    <Title>Funcular.FunkyORM.MySql</Title>
    <Version>3.6.0</Version>
    <AssemblyVersion>3.6.0.0</AssemblyVersion>
    <FileVersion>3.6.0.0</FileVersion>
    <InformationalVersion>3.6.0</InformationalVersion>
    <GeneratePackageOnBuild>False</GeneratePackageOnBuild>
    <SignAssembly>False</SignAssembly>
  </PropertyGroup>

  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
      <_Parameter1>Funcular.Data.Orm.MySql.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="MySqlConnector" Version="2.*" />
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.0'">
    <PackageReference Include="System.ComponentModel.Annotations" Version="5.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Funcular.Data.Orm.Core\Funcular.Data.Orm.Core.csproj">
      <PrivateAssets>all</PrivateAssets>
    </ProjectReference>
  </ItemGroup>
</Project>
```

### `Funcular.Data.Orm.MySql.Tests.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <NoWarn>CS0618</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="MSTest.TestAdapter" Version="3.1.1" />
    <PackageReference Include="MSTest.TestFramework" Version="3.1.1" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Funcular.Data.Orm.MySql\Funcular.Data.Orm.MySql.csproj" />
    <ProjectReference Include="..\Funcular.Data.Orm.Core\Funcular.Data.Orm.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />
  </ItemGroup>
</Project>
```

---

## 16. Comparison: All Four Providers

| Aspect | SQL Server | PostgreSQL | SQLite | **MySQL** |
|--------|-----------|------------|--------|-----------|
| Deployment | Server | Server | Embedded | **Server** |
| Driver | Microsoft.Data.SqlClient | Npgsql | Microsoft.Data.Sqlite | **MySqlConnector (MIT)** |
| Type system | Rich native | Rich native | Type affinity | **Rich native** |
| Entity-mapping complexity | Low | Low | High | **Low** |
| Identifier quoting | `[brackets]` | `"quotes"` | `"quotes"` | **`` `backticks` ``** |
| Identity return | `OUTPUT INSERTED` | `RETURNING` | `RETURNING` | **`LAST_INSERT_ID()`** |
| Paging | `OFFSET…FETCH` | `LIMIT/OFFSET` | `LIMIT/OFFSET` | **`LIMIT/OFFSET`** |
| Boolean | `BIT` | `BOOLEAN` | `INTEGER` | **`TINYINT(1)`** |
| GUID | `UNIQUEIDENTIFIER` | `UUID` | `TEXT` | **`CHAR(36)`** |
| DateTime | `DATETIME2` | `TIMESTAMP` | `TEXT` | **`DATETIME(6)`** |
| Decimal | exact | exact | REAL (lossy) | **exact** |
| JSON | `JSON_VALUE`/`OPENJSON` | `jsonb` | `json_extract` (basic) | **native `JSON`/`JSON_EXTRACT`** |
| String concat | `+`/`CONCAT` | `\|\|` | `\|\|` | **`CONCAT` only** |
| Default string comparison | case-insensitive | case-sensitive | case-insensitive | **case-insensitive (matches MSSQL)** |
| Stored procedures | yes | yes | no | **yes** |
| Schemas | yes | yes | no | **yes (= database)** |
| Default isolation | READ COMMITTED | READ COMMITTED | serializable | **REPEATABLE READ** |
| CI strategy | Windows + LocalDB | Linux + Docker | any | **Linux + Docker** |
| Est. LOC | (existing) | ~4,935 | ~4,780 | **~4,670** |

---

## 17. Summary

The MySQL provider is **ready to implement**, and is among the easier of the four because MySQL is a full server with rich native types and native JSON — sidestepping SQLite's type-affinity burden and PostgreSQL's case-folding strategy.

**Resolved design choices**:
- Driver: **MySqlConnector** (MIT) — not Oracle `MySql.Data`.
- Identity: `MySqlCommand.LastInsertedId` (no `RETURNING`).
- GUID: `CHAR(36)` + `GuidFormat=Char36`.
- Case sensitivity: native case-insensitive default (matches SQL Server); `MySqlStringComparison.CaseSensitive` opt-in.
- Quoting: backticks. Concatenation: `CONCAT`. Integer `AVG`: `CAST(... AS SIGNED)`.
- JSON: native `JSON_EXTRACT`/`JSON_ARRAYAGG` for `[JsonPath]`/`[JsonCollection]`.
- Schema: maps to MySQL database.

**Primary technical focus areas**: identity retrieval without `RETURNING`, `CONCAT`-based string translation, and MySQL error-code mapping.

**Nothing requires `NotSupportedException`** for the ORM's current surface.

---

## 18. Open Items — What We Need From the Maintainer

These are the decisions/inputs the implementation needs; none block writing provider code, but they block **running tests** and the final **packaging** choice.

1. **MySQL connection details** (blocks all integration tests). Provide a `FUNKY_MYSQL_CONNECTION` environment variable (Machine scope, mirroring `FUNKY_CONNECTION`), e.g.
   `Server=localhost;Port=3306;Database=funky_db;User ID=funky;Password=…;GuidFormat=Char36;AllowUserVariables=true;`
   and confirm: server running? port (default 3306)? a login the provider may use (ideally a dedicated `funky` user with rights on `funky_db`, not root).
2. **Create the test database**: `funky_db` (the implementation can run `Database/MySql/integration_test_db.sql` once creds exist).
3. **Packaging decision** (§5.3): bundle the MySQL provider inside the single `Funcular.Data.Orm` package (consistent with SQLite/PostgreSQL) — or ship a standalone `Funcular.Data.Orm.MySql` package? Default assumption: **bundle**.
4. **Driver confirmation**: OK to standardize on **MySqlConnector** (MIT)? (Recommended; avoids Oracle `MySql.Data` GPL licensing.)
5. **MariaDB scope**: target MySQL 8.0 only, or also validate MariaDB? (MariaDB *does* support `RETURNING`, but we'll standardize on the `LAST_INSERT_ID()` path that works on both.) Default assumption: **MySQL 8.0 primary; no MariaDB-specific work**.
