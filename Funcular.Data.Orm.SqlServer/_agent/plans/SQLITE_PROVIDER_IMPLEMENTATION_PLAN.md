# SQLite Provider Implementation Plan

> **Goal**: Create a `Funcular.Data.Orm.Sqlite` provider with maximum practical feature parity to `Funcular.Data.Orm.SqlServer`, including a complete test suite. This document identifies features that cannot be supported in SQLite, prescribes framework behavior for those situations, and serves as a ready-to-implement specification.

---

## 1. Architecture Overview

The SQLite provider will follow the same architecture as the SQL Server provider:

```
Funcular.Data.Orm.Core              (existing, shared)
    │ IOrmDataProvider               – Core CRUD contract
    │ ISqlOrmProvider                – ADO.NET connection/transaction contract
    │ ISqlDialect                    – Dialect-specific SQL generation
    │ OrmDataProvider                – Abstract base with reflection caching

Funcular.Data.Orm.Sqlite            (NEW project)
    │ Sqlite/
    │   │ SqliteOrmDataProvider.cs         – Main provider (mirrors SqlServerOrmDataProvider)
    │   │ SqliteDialect.cs                 – ISqlDialect for SQLite syntax
    │   │ SqliteQueryComponents.cs         – Typed query component container (SqliteParameter)
    │   │ QueryComponents.cs               – Internal query components (paging, aggregates)
    │   │ SqliteLinqQueryProvider.cs       – IQueryProvider for LINQ-to-SQL translation
    │   │ SqliteQueryable.cs              – IOrderedQueryable<T> wrapper
    │   │ RemotePathResolver.cs            – Copy from SqlServer (BFS is DB-agnostic)
    │ Visitors/
    │   │ SqliteWhereClauseVisitor.cs      – Adapted for SqliteParameter
    │   │ SqliteExpressionTranslator.cs    – Adapted for SQLite syntax (includes json_extract support)
    │   │ SqliteParameterGenerator.cs      – Uses SqliteParameter instead of SqlParameter
    │   │ SqliteOrderByClauseVisitor.cs    – Likely minimal changes from SqlServer
    │   │ SqliteSelectClauseVisitor.cs     – Adapted for SQLite CASE/aggregate syntax
    │   │ BaseExpressionVisitor.cs         – Copy from SqlServer (parameter-type-agnostic)
    │ Exceptions/
    │   │ RemoteKeyExceptions.cs           – Copy from SqlServer (DB-agnostic)
    │ SqliteStringComparison.cs            – Enum for constructor: CaseInsensitive (default) / CaseSensitive
    │ ExtensionMethods.cs

Funcular.Data.Orm.Sqlite.Tests      (NEW test project)
    │ Domain/                               – Mirror SqlServer test entities
    │   │ Entities/
    │   │   │ Person/
    │   │   │   │ PersonEntity.cs
    │   │   │   │ PersonDetailEntity.cs
    │   │   │   │ PersonAddressEntity.cs
    │   │   │   │ PersonAddressDetailEntity.cs
    │   │   │ Address/
    │   │   │   │ AddressEntity.cs
    │   │   │   │ AddressDetailEntity.cs
    │   │   │ Organization/
    │   │   │   │ OrganizationEntity.cs
    │   │   │   │ OrganizationDetailEntity.cs
    │   │   │ Country/
    │   │   │   │ CountryEntity.cs
    │   │   │ PersistenceStateEntity.cs
    │   │ Objects/
    │   │   │ Person/Person.cs
    │   │   │ Person/PersonAddress.cs
    │   │   │ Address/Address.cs
    │   │   │ User/User.cs
    │   │   │ NonIdentityGuidEntity.cs
    │   │   │ NonIdentityStringEntity.cs
    │   │ Enums/
    │       │ AddressType.cs
    │ TestDataSeeder.cs                     – C# helper to seed test data via provider Insert<T>()
    │ SqliteDataProviderIntegrationTests.cs
    │ SqliteDataProviderIntegrationAsyncTests.cs
    │ SqliteDataProviderPerformanceTests.cs
    │ RemoteFeaturesTests.cs
    │ RemoteKeyIntegrationTests.cs
    │ RemoteKeyWhereTests.cs
    │ RemoteKeyReverseTests.cs
    │ RichRelationshipTests.cs
    │ DocumentationGapTests.cs

Database/                                   (EXTENDED – adds Sqlite subfolder)
    │ README.md                              – Updated with SQLite instructions
    │ SqlServer/
    │   │ integration_test_db.sql            – Existing DDL
    │   │ integration_test_data.sql          – Existing seed data
    │ Sqlite/
        │ integration_test_db.sql            – NEW: SQLite-compatible DDL
        │ integration_test_data.sql          – NEW: Small representative seed dataset (flat INSERTs)
```

---

## 2. Unsupported Features & Framework Behavior

### ⛔ 2.1 Features That CANNOT Be Supported

| Feature | SQL Server Behavior | SQLite Limitation | Prescribed Framework Behavior |
|---------|-------------------|-------------------|-------------------------------|
| **Stored Procedures** | `ExecuteStoredProcedure()` if ever added | SQLite has no stored procedure support whatsoever | Throw `NotSupportedException("SQLite does not support stored procedures.")` |
| **Multiple Concurrent Writers** | Full multi-user concurrency with row-level locking | SQLite uses file-level locking; only one writer at a time (WAL mode allows concurrent reads) | Document limitation; not a concern for SQLite's intended single-user use cases |
| **Schemas** (`dbo.TableName`, `sales.Orders`) | Multi-schema support with `[Table(Schema = "sales")]` | SQLite has no schema concept | Ignore any `Schema` property on `[Table]` attribute; log a warning if `Log` action is set |
| **Full-Text Search** (native) | `CONTAINS`, `FREETEXT` | SQLite FTS5 is an extension with different syntax | Out of scope; throw `NotSupportedException` if FTS expressions are detected |

### ⚠️ 2.2 Features Requiring Workarounds

| Feature | SQL Server Behavior | SQLite Limitation | Workaround |
|---------|-------------------|-------------------|------------|
| **JSON Querying** | `JSON_VALUE`, `OPENJSON`, `FOR JSON` | SQLite has `json_extract()` since 3.38+ but no `OPENJSON` equivalent | Implement **basic `json_extract()` support** for simple property access patterns (e.g., `x => x.JsonData.SomeField == "value"` → `json_extract(json_data, '$.SomeField') = @p0`). Complex JSON operations (arrays, path wildcards) throw `NotSupportedException`. |
| **DateTime columns** | Native `DATETIME`/`DATETIME2` types; `reader.GetDateTime()` works directly | SQLite stores dates as `TEXT` (ISO8601) | In `MapEntity<T>`, detect `DateTime` target type and use `DateTime.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)`. For writes, serialize as ISO8601 via `value.ToString("O")`. |
| **GUID columns** | Native `UNIQUEIDENTIFIER`; `reader.GetGuid()` works directly | SQLite stores GUIDs as `TEXT` | In `MapEntity<T>`, detect `Guid` target type and use `Guid.Parse(reader.GetString(ordinal))`. For writes, serialize via `value.ToString()`. |
| **Boolean columns** | Native `BIT`; `reader.GetBoolean()` works directly | SQLite stores booleans as `INTEGER` (0 or 1) | In `MapEntity<T>`, detect `bool` target type and use `reader.GetInt64(ordinal) != 0`. Verify whether `Microsoft.Data.Sqlite`'s `GetBoolean()` handles this transparently — if so, prefer that. |
| **Decimal precision** | `DECIMAL(p,s)` with exact precision | SQLite stores all non-integer numerics as `REAL` (8-byte IEEE 754 float) | **Accept precision loss.** Use `reader.GetDouble(ordinal)` and cast to `decimal`. Document that `decimal` properties may lose precision beyond ~15 significant digits. |
| **Foreign Key Enforcement** | Always enforced | Disabled by default | Execute `PRAGMA foreign_keys = ON;` immediately after every connection open in `EnsureConnectionOpen()` |
| **`RETURNING` clause** | `OUTPUT INSERTED.Id` | `RETURNING` supported since SQLite 3.35+ (bundled with `Microsoft.Data.Sqlite` 6.0+) | Use `RETURNING` as primary strategy. |
| **Case-sensitive string comparisons** | Case-insensitive by default (SQL Server collation) | `=` is case-sensitive by default; `LIKE` is case-insensitive for ASCII | **Default to case-insensitive** (matching SQL Server) by applying `COLLATE NOCASE` to string comparisons. Provide a `SqliteStringComparison` enum for the constructor to opt into case-sensitive mode (uses default `=` behavior). See Section 5.5. |
| **Connection lifecycle (in-memory DB)** | Connection pooling; DB persists independently | In-memory DBs (`:memory:`) are destroyed when last connection closes | For tests: keep a single `SqliteConnection` open for the test lifetime and pass it to the provider. For production: use file-based SQLite. |
| **`DateTimeOffset` columns** | Native `DATETIMEOFFSET` type | No native support; store as TEXT | Serialize as ISO8601 with offset (`value.ToString("O")`); parse with `DateTimeOffset.Parse()`. |
| **`AVG()` on integers** | Returns `int` (truncated) | Returns `REAL` (float) | **Round the result** to mimic SQL Server's truncating integer behavior. Apply `CAST(ROUND(AVG(...), 0) AS INTEGER)` for integer-typed aggregate targets. |
| **Isolation Levels** (full range) | All standard levels + Snapshot | Only `Serializable` and `ReadUncommitted` | Not a concern for SQLite's single-user use cases. Accept whatever `Microsoft.Data.Sqlite` supports; do not add special handling. |

---

## 3. SQL Dialect Differences (SQL Server vs SQLite)

### 3.1 Identifier Quoting

| Feature | SQL Server | SQLite |
|---------|-----------|--------|
| Identifier delimiters | `[name]` | `"name"` |
| Reserved word handling | `[Order]` | `"Order"` |
| Case sensitivity | Case-insensitive identifiers | Case-insensitive for table/column names |

**Action**: `SqliteDialect.EncloseIdentifier()` must use double-quote (`"name"`) instead of square brackets.

### 3.2 INSERT with Identity Return

| Feature | SQL Server | SQLite |
|---------|-----------|--------|
| Identity return | `OUTPUT INSERTED.Id` | `RETURNING id` (SQLite 3.35+) |
| Auto-increment | `IDENTITY(1,1)` | `INTEGER PRIMARY KEY AUTOINCREMENT` |

**Action**: `SqliteDialect.BuildInsertCommand()` must use `RETURNING` clause.

### 3.3 SELECT / Paging

| Feature | SQL Server | SQLite |
|---------|-----------|--------|
| Top N | `SELECT TOP N ...` | `SELECT ... LIMIT N` (at end) |
| Paging | `OFFSET x ROWS FETCH NEXT y ROWS ONLY` | `LIMIT y OFFSET x` |

**Action**: `SqliteLinqQueryProvider` must generate `LIMIT`/`OFFSET` at the end of the query.

### 3.4 Data Types

| Feature | SQL Server | SQLite |
|---------|-----------|--------|
| GUID storage | `UNIQUEIDENTIFIER` | `TEXT` (stored as string) |
| Boolean | `BIT` | `INTEGER` (0/1) |
| DateTime | `DATETIME`/`DATETIME2` | `TEXT` (ISO8601 string) |
| DateTimeOffset | `DATETIMEOFFSET` | `TEXT` (ISO8601 with offset) |
| String types | `NVARCHAR(N)` | `TEXT` |
| Fixed char | `CHAR(N)` | `TEXT` |
| Integer auto-inc | `INT IDENTITY(1,1)` | `INTEGER PRIMARY KEY AUTOINCREMENT` |
| Decimal | `DECIMAL(p,s)` | `REAL` (accept precision loss) |
| Binary | `VARBINARY(N)` | `BLOB` |

### 3.5 Parameters

| Feature | SQL Server | SQLite |
|---------|-----------|--------|
| Parameter prefix | `@paramName` | `@paramName` (same) |
| Parameter type | `SqlParameter` with `SqlDbType` | `SqliteParameter` with `SqliteType` |
| Null handling | `DBNull.Value` | `DBNull.Value` (same) |

**Action**: All visitor classes and `ParameterGenerator` must use `Microsoft.Data.Sqlite.SqliteParameter` and `Microsoft.Data.Sqlite.SqliteType`.

### 3.6 Aggregates

No significant differences for `COUNT(*)`, `MIN`, `MAX`, `SUM`, `CASE WHEN EXISTS`.

**`AVG()` on integers**: SQLite returns `REAL`; SQL Server returns truncated `int`. **Action**: Wrap integer `AVG()` with `CAST(ROUND(AVG(...), 0) AS INTEGER)` to mimic SQL Server behavior.

### 3.7 Schema Discovery

| Feature | SQL Server | SQLite |
|---------|-----------|--------|
| Schema query | `SELECT * FROM table` with `SchemaOnly` | `SELECT * FROM table LIMIT 0` or `PRAGMA table_info(table)` |
| Column schema API | `reader.GetColumnSchema()` | `reader.GetSchemaTable()` |

**Action**: `DiscoverColumns<T>()` should use `SELECT * FROM tablename LIMIT 0` with `CommandBehavior.SchemaOnly` and `reader.GetSchemaTable()`.

### 3.8 Error Handling

| Feature | SQL Server | SQLite |
|---------|-----------|--------|
| Exception type | `SqlException` with error numbers | `SqliteException` with SQLite error codes |
| Table not found | Error number 208 | Error code 1 (`SQLITE_ERROR`) + message "no such table: xxx" |
| Unique violation | Error number 2627 | Error code 19 (`SQLITE_CONSTRAINT`) + "UNIQUE constraint failed" |
| FK violation | Error number 547 | Error code 19 (`SQLITE_CONSTRAINT`) + "FOREIGN KEY constraint failed" |

**Action**: `HandleSqlException<T>` must catch `SqliteException` and parse both `SqliteErrorCode` and message text.

### 3.9 Reserved Words

SQLite has a smaller set of reserved words than SQL Server. Key ones overlapping with entity/column names: `ORDER`, `GROUP`, `SELECT`, `TABLE`, `INDEX`, `KEY`, `DEFAULT`, `CHECK`, `REFERENCES`.

**Action**: Populate `SqliteDialect._reservedWords` from the [official SQLite documentation](https://www.sqlite.org/lang_keywords.html).

### 3.10 String Comparisons & Case Sensitivity

| Feature | SQL Server | SQLite |
|---------|-----------|--------|
| Default `=` | Case-insensitive (typical collation) | Case-sensitive |
| Default `LIKE` | Case-insensitive (typical collation) | Case-insensitive for ASCII |

**Decision**: Default the SQLite provider to case-insensitive behavior (matching SQL Server) by applying `COLLATE NOCASE` to string equality comparisons in generated SQL. When the user opts into case-sensitive mode via the `SqliteStringComparison.CaseSensitive` enum value, use the default `=` operator without collation override. See Section 5.5 for full design.

### 3.11 JSON Support (Basic `json_extract`)

| Feature | SQL Server | SQLite |
|---------|-----------|--------|
| JSON path access | `JSON_VALUE(col, '$.path')` | `json_extract(col, '$.path')` |
| JSON array queries | `OPENJSON(col)` | Not supported |
| JSON indexing | Computed column + index | Not supported |

**Action**: In `SqliteExpressionTranslator`, detect property access patterns on JSON-typed properties and emit `json_extract(column_name, '$.PropertyName')`. Complex scenarios (array iteration, wildcards) throw `NotSupportedException("Complex JSON querying is not supported by the SQLite provider. Only simple property access patterns are supported (e.g., x.JsonData.Field == value).")`.

---

## 4. Shared vs Provider-Specific Code

### 4.1 Code That Can Be Reused Directly (Copy)

| Component | Source Location | Notes |
|-----------|----------------|-------|
| `Funcular.Data.Orm.Core` (entire project) | `Funcular.Data.Orm.Core\` | All interfaces, base class, attributes – no changes needed |
| `RemotePathResolver.cs` | `SqlServer\SqlServer\RemotePathResolver.cs` | BFS resolution is database-agnostic; copy with namespace change |
| `RemoteKeyExceptions.cs` | `SqlServer\Exceptions\RemoteKeyExceptions.cs` | Exception types are database-agnostic; copy with namespace change |
| `BaseExpressionVisitor<T>` | `SqlServer\Visitors\BaseExpressionVisitor.cs` | Base class has no parameter-type dependencies |
| `SqlQueryable<T>` | `SqlServer\SqlServer\SqlQueryable.cs` | Generic `IOrderedQueryable<T>` wrapper – copy as `SqliteQueryable<T>` |

### 4.2 Code That Requires Adaptation

| Component | SQL Server Dependency | SQLite Change Required |
|-----------|----------------------|------------------------|
| `SqlServerOrmDataProvider` | `SqlConnection`, `SqlCommand`, `SqlDataReader`, `SqlParameter` | Replace with `SqliteConnection`, `SqliteCommand`, `SqliteDataReader`, `SqliteParameter` |
| `SqlServerDialect` | `SqlParameter`, `[bracket]` quoting, `OUTPUT INSERTED` | Replace with `SqliteParameter`, `"double-quote"` quoting, `RETURNING` |
| `WhereClauseVisitor<T>` | `List<SqlParameter>`, `SqlParameter` | Replace with `List<SqliteParameter>`, `SqliteParameter`; add `COLLATE NOCASE` logic |
| `SqlExpressionTranslator` | `SqlParameter` | Replace with `SqliteParameter`; add `json_extract()` support |
| `ParameterGenerator` | `SqlParameter`, `SqlDbType` | Replace with `SqliteParameter`, `SqliteType` |
| `OrderByClauseVisitor<T>` | No SQL Server types used directly | Review; likely works as-is |
| `SelectClauseVisitor<T>` | `SqlParameter` | Replace with `SqliteParameter`; add integer `AVG` rounding |
| `SqlQueryComponents<T>` | `List<SqlParameter>` | Replace with `List<SqliteParameter>` |
| `QueryComponents` | `List<SqlParameter>` | Replace with `List<SqliteParameter>` |
| `SqlLinqQueryProvider<T>` | `SqlServerOrmDataProvider`, `SqlParameter`, `TOP`/`OFFSET...FETCH` | Replace with `SqliteOrmDataProvider`, `SqliteParameter`, `LIMIT`/`OFFSET` |
| `ExtensionMethods` | `SqlParameterCollection` | Replace with SQLite equivalents or omit |
| `ConnectionScope` | `SqlServerOrmDataProvider` reference | Replace with `SqliteOrmDataProvider` reference |

### 4.3 Refactoring Opportunity (Future — Out of Scope)

Consider extracting shared abstractions to `Funcular.Data.Orm.Core` in the future:
- Generic `IParameterGenerator` interface
- Generic visitor base classes that take `IDbDataParameter` instead of `SqlParameter`
- Move `RemotePathResolver` and `RemoteKeyExceptions` to Core

The SQLite provider will be a parallel implementation (copy + adapt), consistent with the current SqlServer approach.

---

## 5. Key Design Decisions

### 5.1 No External Database Required (Key Advantage)
**Decision**: Unlike SQL Server (LocalDB) and PostgreSQL (Docker), SQLite tests are fully self-contained. No external server, no Docker, no CI service containers. This makes SQLite the easiest provider to test and develop against.

### 5.2 File-Based vs In-Memory Testing
**Decision**: Use **file-based temp databases** as the default test strategy. SQLite in-memory databases are scoped to a single connection and destroyed on close — incompatible with the provider's `ConnectionScope` pattern that opens/closes connections.

**Alternative for performance-sensitive tests**: Pass a pre-opened `SqliteConnection` to the provider constructor (kept open for test duration). Support both patterns:
1. Connection string mode (file-based) — production pattern
2. Pre-opened connection mode (in-memory) — fast test pattern

### 5.3 NuGet Package Structure
**Decision**: Separate NuGet package `Funcular.Data.Orm.Sqlite` parallel to SqlServer and PostgreSQL packages, sharing only `Funcular.Data.Orm.Core`.

### 5.4 `RETURNING` Clause
**Decision**: Use `RETURNING` for INSERT statements. `Microsoft.Data.Sqlite` bundles SQLite 3.35+ which supports `RETURNING`. This is clean and matches the SQL Server `OUTPUT INSERTED` pattern semantically.

### 5.5 Case Sensitivity — `SqliteStringComparison` Enum

**Decision**: Provide a `SqliteStringComparison` enum that can be passed to the `SqliteOrmDataProvider` constructor:

```csharp
/// <summary>
/// Controls how string comparisons are performed in generated SQL.
/// </summary>
public enum SqliteStringComparison
{
    /// <summary>
    /// Case-insensitive comparisons (default). Matches SQL Server default behavior.
    /// Applies COLLATE NOCASE to string equality and comparison operations.
    /// </summary>
    CaseInsensitive = 0,

    /// <summary>
    /// Case-sensitive comparisons. Uses SQLite's native case-sensitive = operator.
    /// </summary>
    CaseSensitive = 1
}
```

**Implementation**:
- **Default (`CaseInsensitive`)**: String equality comparisons in WHERE clauses emit `column LIKE @p0 COLLATE NOCASE` (for `==`) or `column COLLATE NOCASE = @p0 COLLATE NOCASE`. The simplest approach: append `COLLATE NOCASE` to all string column references in equality/comparison expressions.
- **`CaseSensitive`**: Use standard `=` operator with no collation override (SQLite's native behavior).

The `SqliteStringComparison` value is stored as a field on `SqliteOrmDataProvider` and passed to visitor classes during construction so they can conditionally emit the collation clause.

```csharp
public class SqliteOrmDataProvider : OrmDataProvider, ISqlOrmProvider
{
    internal SqliteStringComparison StringComparison { get; }

    public SqliteOrmDataProvider(string connectionString, 
        SqliteStringComparison stringComparison = SqliteStringComparison.CaseInsensitive)
    {
        StringComparison = stringComparison;
        // ...
    }
}
```

### 5.6 Foreign Key Enforcement
**Decision**: Execute `PRAGMA foreign_keys = ON;` immediately after opening every connection. Mandatory because the ORM's remote key features depend on referential integrity.

### 5.7 Type Affinity Strategy
**Decision**: Handle all type conversions in `MapEntity<T>` (read path) and `SqliteParameterGenerator` (write path). The provider is responsible for bridging CLR types to/from SQLite's limited type system. This is the primary technical challenge of the SQLite provider.

### 5.8 Stored Procedures
**Decision**: Any future `ExecuteStoredProcedure()` method on the SQLite provider must throw `NotSupportedException("SQLite does not support stored procedures. Consider using a different provider for applications requiring stored procedure support.")`.

### 5.9 JSON Support (Basic `json_extract`)
**Decision**: Implement basic `json_extract()` support for simple property access patterns. When a LINQ expression accesses a sub-property on a JSON-typed column (detected via a `[JsonColumn]` attribute or similar marker), the expression translator emits:

```sql
json_extract(column_name, '$.PropertyName') = @p0
```

**Scope**: Only simple dot-access paths are supported (e.g., `x.JsonData.Name`, `x.JsonData.Address.City`). Nested paths translate to `'$.Address.City'`.

**Not supported** (throw `NotSupportedException`):
- Array indexing (`x.JsonData.Items[0]`)
- Wildcard paths
- JSON array iteration / `OPENJSON` equivalent
- `FOR JSON` output formatting

### 5.10 Integer Aggregate Rounding
**Decision**: When the ORM detects that an aggregate (`AVG`) targets an integer-typed property, wrap the SQLite aggregate to mimic SQL Server's truncating behavior:

```sql
-- SQLite (mimicking SQL Server integer AVG):
CAST(ROUND(AVG(column_name), 0) AS INTEGER)
```

This ensures that `Query<T>().Average(x => x.IntegerProperty)` returns consistent results across providers.

### 5.11 Connection Pooling
**Decision**: `Microsoft.Data.Sqlite` manages connection pooling internally. For file-based databases, pooling works transparently. For `:memory:` databases, use shared-cache connection string (`Data Source=InMemoryTest;Mode=Memory;Cache=Shared`) when the DB must be accessed across multiple connection opens.

### 5.12 Transaction Isolation
**Decision**: Not a concern for SQLite's single-user use cases. Accept whatever `Microsoft.Data.Sqlite` provides natively. Do not add special handling or warnings for isolation level parameters.

---

## 6. Implementation Steps

### Phase 1: Project Setup

#### Step 1.1: Create `Funcular.Data.Orm.Sqlite` Project
- Target frameworks: `net8.0;netstandard2.0`
- NuGet dependency: `Microsoft.Data.Sqlite` (latest stable) — ships a bundled SQLite native library, no external dependency
- Note: If `Microsoft.Data.Sqlite` 9.x drops `netstandard2.0`, use conditional `PackageReference` (v8.x for ns2.0, v9.x for net8.0) — same pattern as PostgreSQL plan's Npgsql strategy
- Project reference: `Funcular.Data.Orm.Core`
- Add `InternalsVisibleTo` for `Funcular.Data.Orm.Sqlite.Tests`
- Root namespace: `Funcular.Data.Orm.Sqlite`

#### Step 1.2: Create `Funcular.Data.Orm.Sqlite.Tests` Project
- Target framework: `net8.0`
- NuGet dependencies: `Microsoft.NET.Test.Sdk`, `MSTest.TestAdapter`, `MSTest.TestFramework`
- Project references: `Funcular.Data.Orm.Sqlite`, `Funcular.Data.Orm.Core`
- Tests use file-based temp SQLite databases (or persistent connection for in-memory)
- **No external database server required** — tests are fully self-contained
- Include `TestDataSeeder.cs` — C# class that seeds test data using the provider's own `Insert<T>()`

#### Step 1.3: Create `Database/Sqlite/` Folder
- Create `Database/Sqlite/integration_test_db.sql` (SQLite DDL)
- Create `Database/Sqlite/integration_test_data.sql` (small flat INSERT dataset for manual testing)
- Update `Database/README.md` with SQLite instructions

### Phase 2: Core Provider Implementation

#### Step 2.1: Implement `SqliteStringComparison` Enum
Simple enum file (see Section 5.5).

#### Step 2.2: Implement `SqliteDialect : ISqlDialect`
- `EncloseIdentifier()`: Use `"double-quote"` delimiters
- `IsReservedWord()`: SQLite-specific reserved word list
- `BuildInsertCommand()`: Use `RETURNING` clause
- `BuildUpdateCommand()`: Standard SQL (identical to SqlServer)
- `BuildDeleteCommand()`: Standard SQL (identical to SqlServer)
- `BuildSelectCommand()`: Standard SQL (identical to SqlServer)
- Internal `CreateParameter()` helper: Use `SqliteParameter` and `SqliteType`

#### Step 2.3: Implement `SqliteParameterGenerator`
- Mirrors `ParameterGenerator` but creates `SqliteParameter` instances
- Maps CLR types to `SqliteType` enum values:
  - `int`, `long`, `short`, `byte`, `sbyte`, `ushort`, `uint`, `ulong` → `SqliteType.Integer`
  - `string` → `SqliteType.Text`
  - `double`, `float` → `SqliteType.Real`
  - `decimal` → `SqliteType.Real` (accept precision loss)
  - `byte[]` → `SqliteType.Blob`
  - `DateTime` → `SqliteType.Text` (serialize as ISO8601 via `.ToString("O")`)
  - `DateTimeOffset` → `SqliteType.Text` (serialize as ISO8601 with offset)
  - `Guid` → `SqliteType.Text` (serialize as string)
  - `bool` → `SqliteType.Integer` (0 or 1)
  - **`Enum`** → `SqliteType.Integer` (cast underlying value to `int`)
  - `null` → `SqliteType.Text` (fallback)

> **⚠️ Implementation Note**: The existing `ParameterGenerator.GetSqlDbType()` includes handling for `Enum`, `ushort`, `byte`, `sbyte`, `uint`, and `ulong`. The SQLite generator **must replicate all of these branches**. Reference the full `GetSqlDbType()` method in `Funcular.Data.Orm.SqlServer\Visitors\ParameterGenerator.cs`.

#### Step 2.4: Implement `SqliteExpressionTranslator`
- Mirrors `SqlExpressionTranslator` but uses `SqliteParameter`
- **Case sensitivity**: When `SqliteStringComparison.CaseInsensitive` (default), emit `COLLATE NOCASE` on string equality/comparison operations
- **JSON support**: Detect property access on `[JsonColumn]`-marked properties; emit `json_extract(col, '$.Path')` for simple dot-access patterns; throw `NotSupportedException` for complex patterns
- String `LIKE` operations: use standard `LIKE` (already case-insensitive for ASCII in SQLite)
- Collection `Contains` → `IN (...)` works the same
- `StartsWith`/`EndsWith` → `LIKE 'prefix%'` / `LIKE '%suffix'`

#### Step 2.5: Implement Visitor Classes
- `SqliteWhereClauseVisitor<T>`: Mirrors `WhereClauseVisitor<T>` with `SqliteParameter`; passes `SqliteStringComparison` from provider
- `SqliteOrderByClauseVisitor<T>`: Likely identical to SqlServer version; copy and review
- `SqliteSelectClauseVisitor<T>`: Mirrors `SelectClauseVisitor<T>` with `SqliteParameter`; implements integer `AVG` rounding (`CAST(ROUND(AVG(...), 0) AS INTEGER)`)

#### Step 2.6: Implement Query Components
- `SqliteQueryComponents<T>`: Mirrors `SqlQueryComponents<T>` with `List<SqliteParameter>`
- `QueryComponents` (internal): Mirrors `QueryComponents` with `List<SqliteParameter>`

#### Step 2.7: Implement `SqliteQueryable<T>`
- Exact copy of `SqlQueryable<T>` with namespace change

#### Step 2.8: Implement `SqliteLinqQueryProvider<T>`
- Mirrors `SqlLinqQueryProvider<T>` but references `SqliteOrmDataProvider`
- **Paging**: Replace `TOP`/`OFFSET...FETCH` with `LIMIT`/`OFFSET`:
  - `Take(n)` → append `LIMIT n` at end of query
  - `Skip(n)` → append `OFFSET n` at end of query
  - `Skip(x).Take(y)` → append `LIMIT y OFFSET x`

**Paging `ORDER BY` Fallback**: Keep the `ORDER BY id` fallback for deterministic paging behavior, matching the SqlServer provider's contract:
```csharp
if (!string.IsNullOrEmpty(components.OrderByClause) || components.Skip.HasValue || components.Take.HasValue)
    commandText += $"\r\n{(components.OrderByClause ?? "ORDER BY id")}";
if (components.Take.HasValue)
    commandText += $"\r\nLIMIT {components.Take.Value}";
if (components.Skip.HasValue)
    commandText += $"\r\nOFFSET {components.Skip.Value}";
```

#### Step 2.9: Implement `SqliteOrmDataProvider : OrmDataProvider, ISqlOrmProvider`

This is the largest component. Major sections:

**Constructor**:
- Accept `connectionString` (e.g., `"Data Source=mydb.sqlite"` or `"Data Source=:memory:"`)
- Accept optional `SqliteStringComparison` (default: `CaseInsensitive`)
- Accept optional `IDbConnection`, `IDbTransaction`, `ISqlDialect`
- Default dialect: `SqliteDialect`

**Connection Management**:
- `EnsureConnectionOpen()`: Create `SqliteConnection`; execute `PRAGMA foreign_keys = ON;` immediately after open
- `CloseConnectionIfNoTransaction()`: Same pattern — but **do NOT close** if using `:memory:` and connection was externally supplied
- `ConnectionScope`: Same disposable pattern referencing `SqliteOrmDataProvider`

**Column Discovery** (`DiscoverColumns<T>`):
- Use `SELECT * FROM tablename LIMIT 0` with `CommandBehavior.SchemaOnly`
- Use `reader.GetSchemaTable()` to discover column metadata
- Map column names using `IgnoreUnderscoreAndCaseStringComparer`

**CRUD Operations**:
- `Get<T>()`, `GetAsync<T>()`: Same pattern with `SqliteCommand`/`SqliteDataReader`
- `Query<T>(expression)`, `QueryAsync<T>()`: Same pattern
- `GetList<T>()`, `GetListAsync<T>()`: Same pattern
- `Insert<T>()`, `InsertAsync<T>()`: Use `RETURNING` clause
- `Update<T>()`, `UpdateAsync<T>()`: Same pattern
- `Delete<T>(predicate)`, `DeleteAsync<T>()`: Same safety validation (require transaction + predicate guard)

**Entity Mapping** (`MapEntity<T>`) — **THE MOST COMPLEX PART**:
- Same cached mapper approach with `BuildDataReaderMapper<T>`
- **Type affinity handling** (the primary technical challenge):
  - `GetInt32`/`GetInt64` for integer properties → straightforward
  - `GetString` for string properties → straightforward
  - **`bool` properties** → `reader.GetInt64(ordinal) != 0` (verify if `Microsoft.Data.Sqlite` `GetBoolean()` handles transparently)
  - **`Guid` properties** → `Guid.Parse(reader.GetString(ordinal))`
  - **`DateTime` properties** → `DateTime.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)`
  - **`DateTimeOffset` properties** → `DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)`
  - **`decimal` properties** → `(decimal)reader.GetDouble(ordinal)` (accept precision loss)
  - `GetDouble`/`GetFloat` for floating-point properties → straightforward
  - **`Enum` properties** → `(TEnum)(object)reader.GetInt32(ordinal)`

**Transaction Management**:
- `BeginTransaction()`: `SqliteConnection.BeginTransaction()` — accept whatever isolation level `Microsoft.Data.Sqlite` supports
- `CommitTransaction()`, `RollbackTransaction()`: Same pattern

**Error Handling**:
- Catch `Microsoft.Data.Sqlite.SqliteException`
- Check `SqliteErrorCode` and message text:
  - Code 1 (`SQLITE_ERROR`) + "no such table" → table not found
  - Code 19 (`SQLITE_CONSTRAINT`) + "UNIQUE constraint failed" → unique violation
  - Code 19 (`SQLITE_CONSTRAINT`) + "FOREIGN KEY constraint failed" → FK violation

**Remote Property Support**:
- Copy `RemotePathResolver` (same BFS logic)
- `ResolveRemoteJoins<T>()`: Same logic, generates `LEFT JOIN` clauses (standard SQL)
- Remote key/property population works identically

**Internal Members Required by `SqliteLinqQueryProvider`**:

All the same internal members as the SQL Server provider must exist on `SqliteOrmDataProvider`:
- `ColumnNamesCache`, `UnmappedPropertiesCache`, `_columnOrdinalsCache`, `_entityMappers` (static caches)
- `GenerateWhereClause<T>()`, `CreateGetOneOrSelectCommandText<T>()`, `CreateSelectQueryObject<T>()` (internal methods)
- `BuildSqlCommandObject()`, `ExecuteReaderList<T>()`, `ExecuteReaderSingle<T>()` (internal methods)
- `ExecuteReaderListAsync<T>()`, `ExecuteReaderSingleAsync<T>()` (internal async methods)
- `GetTableNameInternal<T>()`, `GetCachedColumnNameInternal()` (internal helpers)
- `InvokeLogAction()`, `ConnectionScope` (internal class)

---

## 7. Database Scripts

### 7.1 SQLite DDL Script (`Database/Sqlite/integration_test_db.sql`)

```sql
-- SQLite DDL for FunkyORM integration tests
-- Target: SQLite 3.35+ (bundled with Microsoft.Data.Sqlite 6.0+)
-- Usage: sqlite3 funky_db.sqlite < integration_test_db.sql

PRAGMA foreign_keys = ON;

-- Table: country
CREATE TABLE IF NOT EXISTS country (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL
);

-- Table: address
CREATE TABLE IF NOT EXISTS address (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    line_1 TEXT NOT NULL,
    line_2 TEXT,
    city TEXT NOT NULL,
    state_code TEXT NOT NULL,
    postal_code TEXT NOT NULL,
    country_id INTEGER,
    dateutc_created TEXT NOT NULL DEFAULT (datetime('now')),
    dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (country_id) REFERENCES country(id)
);

-- Table: organization
CREATE TABLE IF NOT EXISTS organization (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    headquarters_address_id INTEGER,
    FOREIGN KEY (headquarters_address_id) REFERENCES address(id)
);

-- Table: person
CREATE TABLE IF NOT EXISTS person (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    first_name TEXT NOT NULL,
    middle_initial TEXT,
    last_name TEXT NOT NULL,
    birthdate TEXT,
    gender TEXT,
    uniqueid TEXT,
    employer_id INTEGER,
    dateutc_created TEXT NOT NULL DEFAULT (datetime('now')),
    dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (employer_id) REFERENCES organization(id)
);

-- Table: person_address (many-to-many link table)
CREATE TABLE IF NOT EXISTS person_address (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    person_id INTEGER NOT NULL,
    address_id INTEGER NOT NULL,
    is_primary INTEGER NOT NULL DEFAULT 0,
    address_type_value INTEGER NOT NULL DEFAULT 0,
    dateutc_created TEXT NOT NULL DEFAULT (datetime('now')),
    dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (person_id) REFERENCES person(id),
    FOREIGN KEY (address_id) REFERENCES address(id)
);

-- Indexes for join performance
CREATE INDEX IF NOT EXISTS ix_person_address_person ON person_address(person_id);
CREATE INDEX IF NOT EXISTS ix_person_address_address ON person_address(address_id);

-- Table: non_identity_guid_entity (GUID primary key, no auto-increment)
CREATE TABLE IF NOT EXISTS non_identity_guid_entity (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL
);

-- Table: non_identity_string_entity (string primary key, no auto-increment)
CREATE TABLE IF NOT EXISTS non_identity_string_entity (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL
);

-- Table: "User" (Reserved Word Test — tests identifier quoting)
CREATE TABLE IF NOT EXISTS "User" (
    "Key" INTEGER PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Order" INTEGER NOT NULL,
    "Select" INTEGER NOT NULL DEFAULT 0
);

-- Trigger: person (update dateutc_modified on UPDATE)
CREATE TRIGGER IF NOT EXISTS trg_person_update
AFTER UPDATE ON person
BEGIN
    UPDATE person SET dateutc_modified = datetime('now')
    WHERE id = NEW.id;
END;

-- Trigger: address
CREATE TRIGGER IF NOT EXISTS trg_address_update
AFTER UPDATE ON address
BEGIN
    UPDATE address SET dateutc_modified = datetime('now')
    WHERE id = NEW.id;
END;

-- Trigger: person_address
CREATE TRIGGER IF NOT EXISTS trg_person_address_update
AFTER UPDATE ON person_address
BEGIN
    UPDATE person_address SET dateutc_modified = datetime('now')
    WHERE id = NEW.id;
END;
```

### 7.2 Seed Data Strategy

Since SQLite has **no procedural SQL**, two complementary approaches:

1. **`Database/Sqlite/integration_test_data.sql`**: Small representative dataset (~100 rows) as flat `INSERT` statements for manual testing and documentation.

2. **`TestDataSeeder.cs`** (C# in test project): Generates the full 5,000-row dataset using the provider's own `Insert<T>()`. This:
   - Validates the provider's insert logic as a side effect
   - Generates randomized, realistic data matching the SQL Server test dataset
   - Avoids a massive flat SQL file
   - Can be conditionally skipped if data already exists (for file-based DBs)

---

## 8. Test Implementation

### 8.1 Test Infrastructure

```csharp
[TestInitialize]
public void Setup()
{
    _sb.Clear();
    _dbPath = Path.Combine(Path.GetTempPath(), $"funky_test_{Guid.NewGuid():N}.sqlite");
    _connectionString = $"Data Source={_dbPath}";

    EnsureSchema();
    TestDataSeeder.SeedIfEmpty(_connectionString);

    _provider = new SqliteOrmDataProvider(_connectionString)
    {
        Log = s =>
        {
            Debug.WriteLine(s);
            Console.WriteLine(s);
            _sb.AppendLine(s);
        }
    };
}

[TestCleanup]
public void Cleanup()
{
    _provider?.Dispose();
    SqliteConnection.ClearAllPools(); // Release file lock
    if (File.Exists(_dbPath))
        File.Delete(_dbPath);
}

private void EnsureSchema()
{
    using var connection = new SqliteConnection(_connectionString);
    connection.Open();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "PRAGMA foreign_keys = ON;";
    cmd.ExecuteNonQuery();

    cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS country (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS address (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            line_1 TEXT NOT NULL,
            line_2 TEXT,
            city TEXT NOT NULL,
            state_code TEXT NOT NULL,
            postal_code TEXT NOT NULL,
            country_id INTEGER,
            dateutc_created TEXT NOT NULL DEFAULT (datetime('now')),
            dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')),
            FOREIGN KEY (country_id) REFERENCES country(id)
        );
        CREATE TABLE IF NOT EXISTS organization (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            headquarters_address_id INTEGER,
            FOREIGN KEY (headquarters_address_id) REFERENCES address(id)
        );
        CREATE TABLE IF NOT EXISTS person (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            first_name TEXT NOT NULL,
            middle_initial TEXT,
            last_name TEXT NOT NULL,
            birthdate TEXT,
            gender TEXT,
            uniqueid TEXT,
            employer_id INTEGER,
            dateutc_created TEXT NOT NULL DEFAULT (datetime('now')),
            dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')),
            FOREIGN KEY (employer_id) REFERENCES organization(id)
        );
        CREATE TABLE IF NOT EXISTS person_address (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            person_id INTEGER NOT NULL,
            address_id INTEGER NOT NULL,
            is_primary INTEGER NOT NULL DEFAULT 0,
            address_type_value INTEGER NOT NULL DEFAULT 0,
            dateutc_created TEXT NOT NULL DEFAULT (datetime('now')),
            dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')),
            FOREIGN KEY (person_id) REFERENCES person(id),
            FOREIGN KEY (address_id) REFERENCES address(id)
        );
        CREATE TABLE IF NOT EXISTS non_identity_guid_entity (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS non_identity_string_entity (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS ""User"" (
            ""Key"" INTEGER PRIMARY KEY AUTOINCREMENT,
            ""Name"" TEXT NOT NULL,
            ""Order"" INTEGER NOT NULL,
            ""Select"" INTEGER NOT NULL DEFAULT 0
        );
    ";
    cmd.ExecuteNonQuery();
}
```

### 8.2 Test Class Mapping

| SQL Server Test | SQLite Test | Notes |
|----------------|-------------|-------|
| `SqlDataProviderIntegrationTests` | `SqliteDataProviderIntegrationTests` | Core CRUD tests |
| `SqlDataProviderIntegrationAsyncTests` | `SqliteDataProviderIntegrationAsyncTests` | Async CRUD tests |
| `RemoteFeaturesTests` | `SqliteRemoteFeaturesTests` | Remote property/key population |
| `RemoteKeyIntegrationTests` | `SqliteRemoteKeyIntegrationTests` | Full chain remote key tests |
| `RemoteKeyWhereTests` | `SqliteRemoteKeyWhereTests` | Filtering by remote keys |
| `RemoteKeyReverseTests` | `SqliteRemoteKeyReverseTests` | Reverse remote key resolution |
| `RichRelationshipTests` | `SqliteRichRelationshipTests` | Many-to-many, link table tests |
| `DocumentationGapTests` | `SqliteDocumentationGapTests` | Verify documentation examples work |
| `SqlDataProviderPerformanceTests` | `SqliteDataProviderPerformanceTests` | Performance benchmarks |

### 8.3 Test-Specific Differences from SQL Server

| Aspect | SQL Server Tests | SQLite Tests |
|--------|-----------------|--------------|
| Connection | `(localdb)\MSSQLLocalDB` or localhost | Temp file `funky_test_{guid}.sqlite` |
| External dependency | SQL Server LocalDB | **None** |
| `EnsureSchema()` | `IF NOT EXISTS (SELECT * FROM sys.tables ...)` | `CREATE TABLE IF NOT EXISTS ...` |
| Auto-increment | `INT IDENTITY(1,1)` | `INTEGER PRIMARY KEY AUTOINCREMENT` |
| Boolean | `BIT` (native) | `INTEGER` (0/1) |
| GUID | `UNIQUEIDENTIFIER` (native) | `TEXT` (string) |
| DateTime | `DATETIME2` (native) | `TEXT` (ISO8601) |
| Exception type | `SqlException` | `SqliteException` |
| CI pipeline | Windows runner + LocalDB | **Any runner — no external DB** |

### 8.4 Domain Entity Reuse

Domain entities (`PersonEntity`, `AddressEntity`, etc.) should be **copied** from the SqlServer test project with only the namespace changed. No entity modifications needed — same `[Table]`, `[Column]`, `[RemoteKey]`, `[RemoteProperty]`, and `[RemoteLink]` attributes work identically. This validates that entity classes are provider-agnostic.

---

## 9. CI Pipeline Integration

### 9.1 GitHub Actions — No Service Container Needed

The **key advantage** of the SQLite provider for CI: no external database, no Docker, no service containers. Tests are fully self-contained.

```yaml
# .github/workflows/build-and-test-sqlite.yml
name: Build and Test (SQLite)

on:
  push:
    branches: [ main, 'development/**' ]
  pull_request:
    branches: [ main ]

jobs:
  test-sqlite:
    runs-on: ubuntu-latest  # Also works on windows-latest, macos-latest

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore dependencies
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore --configuration Release

      - name: Run SQLite integration tests
        run: >
          dotnet test
          Funcular.Data.Orm.Sqlite.Tests/Funcular.Data.Orm.Sqlite.Tests.csproj
          --no-build --configuration Release --verbosity normal
          --logger "trx;LogFileName=test-results.trx"

      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: sqlite-test-results
          path: '**/test-results.trx'
```

### 9.2 CI Comparison: All Providers

| Aspect | SQL Server CI | PostgreSQL CI | SQLite CI |
|--------|--------------|---------------|-----------|
| **Runner OS** | `windows-latest` | `ubuntu-latest` | **Any (cross-platform)** |
| **External dependency** | LocalDB | Docker PostgreSQL | **None** |
| **Startup time** | ~30–60s | ~5–10s | **0s** |
| **Runner cost** | High (Windows) | Medium (Linux) | **Low (any)** |
| **Reliability** | Occasional LocalDB failures | Very reliable | **100% reliable** |
| **Complexity** | Medium (install steps) | Low (service container) | **Minimal** |

---

## 10. Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|-----------|
| SQLite type affinity causing mapping errors (GUID as TEXT, bool as INTEGER, DateTime as TEXT) | **High** | Explicit type conversion in `MapEntity<T>` with comprehensive unit tests for each type; verify `Microsoft.Data.Sqlite` built-in conversions |
| In-memory database connection lifecycle incompatible with ConnectionScope | **High** | Default to file-based temp databases for tests; support persistent connection injection for in-memory mode |
| `PRAGMA foreign_keys = ON` not executed, breaking remote key tests | **High** | Execute pragma in `EnsureConnectionOpen()` — every connection, every time |
| `json_extract()` not available on older SQLite builds | **Low** | `Microsoft.Data.Sqlite` bundles 3.38+; not a concern |
| SQLite file locking on Windows preventing test cleanup | **Medium** | Call `SqliteConnection.ClearAllPools()` before file deletion in test cleanup |
| `COLLATE NOCASE` performance impact on large datasets | **Low** | Negligible for SQLite's intended use cases; document if profiling shows concern |
| `RETURNING` clause not available on very old SQLite versions | **Low** | `Microsoft.Data.Sqlite` bundles 3.35+; not a realistic concern |
| Seed data generation time for 5,000 rows via C# Insert<T>() | **Low** | SQLite is fast for single-connection writes; should complete in <2s |
| `Microsoft.Data.Sqlite` doesn't support `netstandard2.0` in latest versions | **Medium** | Verify TFM support; use conditional PackageReference if needed (same pattern as PostgreSQL Npgsql) |

---

## 11. Estimated Scope

| Component | Files | Complexity |
|-----------|-------|-----------|
| `Funcular.Data.Orm.Sqlite` project | ~16 files | Medium-High |
| `Funcular.Data.Orm.Sqlite.Tests` project | ~21 files | Medium |
| `Database/Sqlite/` scripts | ~2 files | Low |
| CI workflow file | ~1 file | Low |
| **Total** | **~40 files** | **Medium-High** |

### Approximate Lines of Code (New)

| Component | LOC |
|-----------|-----|
| Provider (`SqliteOrmDataProvider`) | ~1,600 (type conversion complexity) |
| Dialect (`SqliteDialect`) | ~130 |
| Visitors (Where, OrderBy, Select, Translator) | ~650 (includes COLLATE NOCASE + json_extract + AVG rounding) |
| Parameter Generator | ~90 |
| Query Components + Queryable | ~200 |
| RemotePathResolver + Exceptions (copies) | ~200 |
| `SqliteStringComparison` enum | ~20 |
| Tests (all test classes) | ~1,500 |
| TestDataSeeder | ~200 |
| Database scripts (DDL + seed INSERTs) | ~150 |
| CI workflow YAML | ~40 |
| **Total** | **~4,780 LOC** |

---

## 12. Dependencies

### NuGet Packages Required

| Package | Project | Version | Notes |
|---------|---------|---------|-------|
| `Microsoft.Data.Sqlite` | `Funcular.Data.Orm.Sqlite` | 8.x or 9.x | Verify netstandard2.0 support; conditional PackageReference if needed |
| `Microsoft.NET.Test.Sdk` | `Funcular.Data.Orm.Sqlite.Tests` | 17.8.0+ | Test infrastructure |
| `MSTest.TestAdapter` | `Funcular.Data.Orm.Sqlite.Tests` | 3.1.1+ | Test adapter |
| `MSTest.TestFramework` | `Funcular.Data.Orm.Sqlite.Tests` | 3.1.1+ | Test framework |
| `coverlet.collector` | `Funcular.Data.Orm.Sqlite.Tests` | 6.0.0 | Code coverage |

### Project References

| From | To |
|------|----|
| `Funcular.Data.Orm.Sqlite` | `Funcular.Data.Orm.Core` |
| `Funcular.Data.Orm.Sqlite.Tests` | `Funcular.Data.Orm.Sqlite` |
| `Funcular.Data.Orm.Sqlite.Tests` | `Funcular.Data.Orm.Core` |

### External Dependencies

**None.** The native SQLite library is bundled inside the `Microsoft.Data.Sqlite` NuGet package.

---

## 13. Success Criteria

1. **Build**: Both projects compile without errors targeting `net8.0` and `netstandard2.0`
2. **Feature Parity**: All CRUD operations (`Get`, `Query`, `Insert`, `Update`, `Delete` — sync and async) work identically to SQL Server
3. **LINQ Translation**: `WHERE`, `ORDER BY`, paging (`Skip`/`Take`), and aggregates (`Count`, `Any`, `All`, `Min`, `Max`, `Average`, `Sum`) translate correctly to SQLite SQL
4. **Remote Properties**: `[RemoteKey]`, `[RemoteProperty]`, `[RemoteLink]` attributes generate correct `LEFT JOIN` clauses
5. **Safety**: Delete transaction mandate and predicate guard work identically to SQL Server
6. **Test Coverage**: All test classes from SQL Server have SQLite equivalents that pass
7. **Self-Contained**: Tests require NO external database, Docker, or service containers
8. **Convention Compliance**: Entity conventions (Id detection, table name detection, column mapping with `IgnoreUnderscoreAndCaseStringComparer`) work identically
9. **Type Affinity**: All CLR types (int, string, bool, Guid, DateTime, DateTimeOffset, decimal, enum, byte[]) round-trip correctly through SQLite's type system
10. **Cross-Platform CI**: Tests pass on Linux, Windows, and macOS runners without modification
11. **Unsupported Features**: Stored procedures throw `NotSupportedException`; complex JSON throws `NotSupportedException`
12. **Case Insensitivity**: Default string comparison behavior matches SQL Server (case-insensitive); `SqliteStringComparison.CaseSensitive` opt-in works correctly
13. **Basic JSON**: Simple `json_extract()` property access patterns work in WHERE clauses
14. **Integer Aggregates**: `AVG()` on integer columns returns truncated integer matching SQL Server behavior

---

## 14. Implementation Order (Recommended)

1. Create `Database/Sqlite/` folder structure
2. Create `Database/Sqlite/integration_test_db.sql` (SQLite DDL)
3. Create `Database/Sqlite/integration_test_data.sql` (small flat INSERT seed)
4. Update `Database/README.md` with SQLite instructions
5. Create project files and add to solution (`Funcular.Data.Orm.Sqlite.csproj`, `Funcular.Data.Orm.Sqlite.Tests.csproj`)
6. Implement `SqliteStringComparison` enum
7. Implement `SqliteDialect` (foundation for all SQL generation)
8. Implement `SqliteParameterGenerator` (foundation for parameterized queries)
9. Implement `SqliteExpressionTranslator` (includes json_extract + COLLATE NOCASE) and visitor classes
10. Implement `SqliteQueryComponents` and `SqliteQueryable`
11. Implement `SqliteOrmDataProvider` (main provider — largest piece; type affinity handling is the hardest part)
12. Implement `SqliteLinqQueryProvider`
13. Copy `RemotePathResolver` and `RemoteKeyExceptions` with namespace change
14. Create test domain entities (copy from SqlServer tests with namespace change)
15. Create `TestDataSeeder.cs` (C# seed data generator using provider `Insert<T>()`)
16. Create test base class with schema setup (`EnsureSchema()`)
17. Implement integration tests (CRUD — sync)
18. Implement async integration tests
19. Implement remote feature tests (RemoteKey, RemoteProperty, RemoteLink)
20. Implement remaining test classes (RichRelationship, DocumentationGap, Performance)
21. Create GitHub Actions CI workflow (`.github/workflows/build-and-test-sqlite.yml`)
22. Build and validate all tests pass locally and in CI

---

## 15. Appendix: Project File Templates

### `Funcular.Data.Orm.Sqlite.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;netstandard2.0</TargetFrameworks>
    <Nullable>disable</Nullable>
    <NoWarn>MSB3277;MSB3247;MSB3268;NU1701</NoWarn>
    <Copyright>Funcular Labs, Inc.</Copyright>
    <Description>SQLite provider for FunkyORM, a lightweight micro-ORM designed for simplicity and speed.</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/FuncularLabs/Funcular.FunkyOrm</PackageProjectUrl>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <RepositoryUrl>https://github.com/FuncularLabs/Funcular.FunkyOrm.git</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageTags>csharp;sql;orm;sqlite;entity;lambda;query;embedded;lightweight</PackageTags>
    <Title>Funcular.FunkyORM.Sqlite</Title>
    <Version>3.1.0</Version>
    <AssemblyVersion>3.1.0.0</AssemblyVersion>
    <FileVersion>3.1.0.0</FileVersion>
    <InformationalVersion>3.1.0</InformationalVersion>
    <GeneratePackageOnBuild>True</GeneratePackageOnBuild>
    <SignAssembly>False</SignAssembly>
  </PropertyGroup>

  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
      <_Parameter1>Funcular.Data.Orm.Sqlite.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>

  <!-- If Microsoft.Data.Sqlite 9.x drops netstandard2.0, use conditional PackageReference -->
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="9.*" />
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

### `Funcular.Data.Orm.Sqlite.Tests.csproj`

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
    <ProjectReference Include="..\Funcular.Data.Orm.Sqlite\Funcular.Data.Orm.Sqlite.csproj" />
    <ProjectReference Include="..\Funcular.Data.Orm.Core\Funcular.Data.Orm.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />
  </ItemGroup>

</Project>
```

---

## 16. Comparison: SQLite vs PostgreSQL vs SQL Server

| Aspect | SQL Server | PostgreSQL | SQLite |
|--------|-----------|------------|--------|
| **Deployment** | Server (requires installation) | Server (requires Docker or install) | **Embedded (zero-install)** |
| **External dependency** | LocalDB or SQL Server | Docker or PostgreSQL server | **None** |
| **Type system** | Rich native types | Rich native types | **Type affinity (TEXT/INTEGER/REAL/BLOB)** |
| **Entity mapping complexity** | Low | Low | **High (manual type conversion)** |
| **Stored procedures** | Full support | Full support (PL/pgSQL) | **Not supported (throw NotSupportedException)** |
| **JSON querying** | Full (`OPENJSON`, `JSON_VALUE`) | Full (`jsonb`, operators) | **Basic only (`json_extract` for simple paths)** |
| **Concurrency** | Multi-user, row-level locking | Multi-user, MVCC | **Single-writer, file-level locking** |
| **Schemas** | Multiple schemas | Multiple schemas | **No schemas** |
| **String comparison default** | Case-insensitive | Case-sensitive | **Case-insensitive (via COLLATE NOCASE, matching SQL Server)** |
| **Integer AVG behavior** | Truncated integer | Float (numeric) | **Rounded to integer (mimicking SQL Server)** |
| **CI strategy** | Windows runner + LocalDB install | Linux runner + Docker service | **Any runner, no setup** |
| **CI reliability** | Medium | High | **100%** |
| **Ideal use case** | Production server workloads | Production server workloads | **Unit/integration tests, embedded apps, prototyping, mobile/desktop** |
| **Seed data scripts** | Procedural T-SQL (WHILE loops) | Procedural PL/pgSQL (DO blocks) | **Flat INSERTs + C# TestDataSeeder** |
| **Identifier quoting** | `[brackets]` | `"double-quotes"` | `"double-quotes"` |
| **INSERT identity return** | `OUTPUT INSERTED` | `RETURNING` | `RETURNING` (3.35+) |
| **Paging** | `OFFSET...FETCH` | `LIMIT`/`OFFSET` | `LIMIT`/`OFFSET` |
| **Boolean storage** | `BIT` (native) | `BOOLEAN` (native) | `INTEGER` (0/1) |
| **GUID storage** | `UNIQUEIDENTIFIER` (native) | `UUID` (native) | `TEXT` (string) |
| **DateTime storage** | `DATETIME2` (native) | `TIMESTAMP` (native) | `TEXT` (ISO8601 string) |
| **FK enforcement** | Always on | Always on | **Off by default (PRAGMA required)** |
| **Estimated LOC** | (existing) | ~4,935 | **~4,780** |

---

## 17. Summary

The SQLite provider implementation is **ready to implement** with the following resolved decisions:

**Key Design Choices (Resolved)**:
- **Case sensitivity**: Default case-insensitive via `COLLATE NOCASE`; opt-in `SqliteStringComparison.CaseSensitive` enum
- **JSON**: Basic `json_extract()` for simple property access patterns; complex patterns throw `NotSupportedException`
- **Decimal**: Accept REAL precision loss
- **Integer AVG**: Round to mimic SQL Server truncation
- **Transaction isolation**: Not a concern; accept defaults
- **Schema migration / DDL**: Not in scope (FunkyORM doesn't do DDL)
- **Triggers**: Not interacted with by FunkyORM; included in test scripts only for timestamp behavior

**Strengths**:
- Zero external dependencies — ideal for testing, CI, and embedded scenarios
- Cross-platform with no setup required
- Fast (no network I/O, no server startup)
- `RETURNING` clause and `LIMIT`/`OFFSET` syntax closely match PostgreSQL

**Primary Technical Challenge**:
- Type affinity handling in `MapEntity<T>` — bridging SQLite's limited type system to/from CLR types

**Unsupported (throw `NotSupportedException`)**:
- Stored procedures
- Complex JSON (array queries, wildcards, `OPENJSON` equivalent)
- Full-text search

No remaining ambiguities or unresolved questions. This plan is ready for implementation.
