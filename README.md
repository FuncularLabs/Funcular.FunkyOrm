> **Recent Changes**
> * **v3.8.1-beta1**: 🔃 **View-replacing attributes in `OrderBy`, `Distinct()`, and projections** — order by `[JsonPath]`/`[SqlExpression]`/`[SubqueryAggregate]`/`[RemoteProperty]` (sorts by the resolved SQL, not a missing column), `Distinct()` → `SELECT DISTINCT`, and project the self-contained computed attributes in a custom `.Select(...)`. Gap closure across all four providers. See [JSON & Computed Column Attributes](#6-json--computed-column-attributes).
> * **v3.8.0-beta1**: 🔐 **Row-Level Security & audit context (beta)** — prime per-request end-user identity onto each connection (even when the app connects as one identity) for RLS filtering and audit attribution. Full on SQL Server & PostgreSQL; attribution-only on MySQL. Shipping as a **beta** feature. See [Row-Level Security & Audit Context](#row-level-security--audit-context-v38) and the [Audit Context Runbook](docs/guides/AUDIT_CONTEXT_RUNBOOK.md).
> * **v3.7.0**: ⚙️ **Stored procedure execution** — `ExecProcedure<T>` / `ExecScalar` / `ExecNonQuery` (+ async), with output parameters and `[Procedure]`/convention name resolution. Full on SQL Server & MySQL; `CALL`-based scalar/non-query on PostgreSQL; not supported on SQLite. See [Database Provider Differences](#database-provider-differences).
> * **v3.6.0**: 🐬 **MySQL support** — a full `MySqlOrmDataProvider` (MIT-licensed MySqlConnector), bundled in the same `Funcular.Data.Orm` package with feature parity across providers.
> * **v3.5.0**: 🗃️ **SQLite support** — a full file-backed, zero-config `SqliteOrmDataProvider`, bundled in the same package.
> * **v3.2.1**: 🧩 **All four view-replacing attributes** — `[JsonPath]`, `[SqlExpression]`, `[SubqueryAggregate]`, `[JsonCollection]`.


# Funcular / Funky ORM: a speedy, lambda-powered .NET micro-ORM for MSSQL, PostgreSQL, MySQL & SQLite
![Funcular logo](https://raw.githubusercontent.com/FuncularLabs/Funcular.FunkyOrm/master/funky-orm-lineart-256x256.png)

[![NuGet](https://img.shields.io/nuget/v/Funcular.Data.Orm.svg)](https://www.nuget.org/packages/Funcular.Data.Orm)
[![Downloads](https://img.shields.io/nuget/dt/Funcular.Data.Orm.svg)](https://www.nuget.org/packages/Funcular.Data.Orm)
[![CI status](https://img.shields.io/github/actions/workflow/status/FuncularLabs/Funcular.FunkyOrm/ci.yml?branch=master&label=CI)](https://github.com/FuncularLabs/Funcular.FunkyOrm/actions)
[![Tests](https://img.shields.io/github/actions/workflow/status/FuncularLabs/Funcular.FunkyOrm/ci.yml?branch=master&label=Tests)](https://github.com/FuncularLabs/Funcular.FunkyOrm/actions)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

> **For AI Agents**: Please refer to [FUNKYORM_AI_INSTRUCTIONS.md](Funcular.Data.Orm.SqlServer/FUNKYORM_AI_INSTRUCTIONS.md) for strict coding guidelines and "Happy Path" patterns. This file is included in the NuGet package. A PostgreSQL-specific supplement is at [FUNKYORM_AI_INSTRUCTIONS_POSTGRESQL.md](Funcular.Data.Orm.PostgreSql/FUNKYORM_AI_INSTRUCTIONS_POSTGRESQL.md). A SQLite-specific supplement is at [FUNKYORM_AI_INSTRUCTIONS_SQLITE.md](Funcular.Data.Orm.Sqlite/FUNKYORM_AI_INSTRUCTIONS_SQLITE.md). A MySQL-specific supplement is at [FUNKYORM_AI_INSTRUCTIONS_MYSQL.md](docs/ai-instructions/FUNKYORM_AI_INSTRUCTIONS_MYSQL.md).
>
> **Tip for Consumers**: To help AI agents (Copilot, Cursor, etc.) generate correct FunkyORM code in your project, copy `FUNKYORM_AI_INSTRUCTIONS.md` from the NuGet package to your project root or `.github/` folder. The product-specific filename avoids collisions with instructions from other packages.

## Overview

Welcome to **Funcular ORM** (aka FunkyORM), the micro-ORM designed for developers who want the **speed** of a micro-ORM with the **simplicity** and **type safety** of LINQ.

If you're tired of wrestling with raw SQL strings (Dapper) or debugging generated queries from a heavy framework (Entity Framework), FunkyORM is your sweet spot.

### Why FunkyORM?

*   **Instant Lambda Queries**: Write C# lambda expressions, get optimized SQL.
*   **Performance**: Outperforms EF Core in single-row writes and matches it in reads. (See our [Usage Guide](Usage.md) for benchmarks).
*   **Zero Configuration**: No `DbContext`, no mapping files. Just POCOs and a connection string.
*   **Safe**: All queries are parameterized to prevent SQL injection.
*   **Mass Delete Prevention**: Includes safeguards against accidental "delete all" operations (e.g., blocking `1=1`), though this does not guarantee prevention of all crafty circumventions.
*   **Convention over Configuration**: Sensible defaults for primary key naming conventions (like `id`, `tablename_id`, or `TableNameId`) mean less boilerplate and more productivity.
*   **Remote Keys & Properties**: Flatten your object graph by mapping properties directly to columns in related tables (e.g., `Person.EmployerCountryName`) without writing joins. The ORM handles the graph traversal for you.
*   **JSON & Computed Column Attributes**: Four attribute types that eliminate SQL views entirely in code — `[JsonPath]`, `[SqlExpression]`, `[SubqueryAggregate]`, and `[JsonCollection]`. Works on SQL Server, PostgreSQL, and SQLite.
*   **Explicit Collection Population**: Leverage `RemoteKey` properties to easily populate related collections without the overhead of massive object graphs or N+1 queries.
*   **Cached Reflection**: Funcular ORM caches reflection results to minimize overhead and maximize performance.
*   **Nullable-Friendly**: Nullable properties work seamlessly in LINQ queries—no need for `.Value` or `.HasValue`. The ORM handles the unwrapping for you.

    
## Getting Started

### 1. Installation

Install the FunkyORM package — SQL Server, PostgreSQL, and SQLite providers are all included:

```bash
dotnet add package Funcular.Data.Orm
```

### 2. Initialization

Create a provider instance. You can register it in your DI container or create it as needed. See [Concurrency & Connection Management](#concurrency--connection-management) for lifetime guidance in concurrent environments like Blazor Server.

**SQL Server:**
```csharp
using Funcular.Data.Orm.SqlServer;

var connectionString = "Server=.;Database=MyDb;Integrated Security=True;TrustServerCertificate=True;";
var provider = new SqlServerOrmDataProvider(connectionString);
```

**PostgreSQL:**
```csharp
using Funcular.Data.Orm.PostgreSql;

var connectionString = "Host=localhost;Port=5432;Database=mydb;Username=myuser;Password=mypassword";
var provider = new PostgreSqlOrmDataProvider(connectionString);
```

**SQLite:**
```csharp
using Funcular.Data.Orm.Sqlite;

var connectionString = "Data Source=myapp.db";
var provider = new SqliteOrmDataProvider(connectionString);
```

**MySQL:**
```csharp
using Funcular.Data.Orm.MySql;

var connectionString = "Server=localhost;Port=3306;Database=mydb;User ID=myuser;Password=mypassword;GuidFormat=Char36";
var provider = new MySqlOrmDataProvider(connectionString);
```

> **Note:** All four providers implement the same base class and support the same LINQ query API, CRUD operations, remote keys/properties, and transactions. Your entity classes and query code are fully portable between providers.

### 3. Define Your Data Models

FunkyORM is designed to keep your code clean. **You usually don't need attributes.**

By default, we map the **intersection** of your class properties and the database table columns.
*   If your class has a `FullName` property but the table doesn't, we ignore it. No `[NotMapped]` needed.
*   If the table has a `CreatedDate` column but your class doesn't, we ignore it. No errors.

We also infer table names and primary keys automatically.

```csharp
// No attributes needed!
// Maps to table 'Person' or 'PERSON' (case-insensitive)
public class Person
{
    // Automatically detected as Primary Key
    public int Id { get; set; }
    
    // Maps to column 'FirstName', 'first_name', etc.
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public int Age { get; set; }
    
    // Ignored automatically if no matching column exists
    public string FullName => $"{FirstName} {LastName}";
}
```

If you need to deviate from conventions (e.g., mapping `Person` class to `tbl_Users`), you can still use standard `System.ComponentModel.DataAnnotations`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("tbl_Users")]
public class Person
{
    [Key]
    [Column("user_id")]
    public int Id { get; set; }
    // ...
}
```

### 4. Start Querying

```csharp
// Insert a new record and get the ID
var newPerson = new Person { FirstName = "Jane", LastName = "Doe", Age = 25 };
var newId = provider.Insert<Person, int>(newPerson);

// Get by ID
var person = provider.Get<Person>(newId);

// Complex Querying with LINQ
var adults = provider.Query<Person>()
    .Where(p => p.Age >= 18)
    .Where(p => p.LastName.StartsWith("D"))
    .OrderByDescending(p => p.Age)
    .Take(10)
    .ToList();
```

### 5. Superpowers: Remote Keys & Properties

This is where FunkyORM shines. You can map properties in your entity to columns in *related* tables without writing joins or loading the entire object graph.

*   **`[RemoteProperty]`**: Fetch a value from a related table (e.g., `Employer.Name`) directly into your `Person` object.
*   **`[RemoteKey]`**: Fetch the ID of a related entity (e.g., `Employer.CountryId`) directly.

```csharp
public class Person
{
    // ... standard properties ...

    // Link to the Organization table
    [RemoteLink(targetType: typeof(Organization))]
    public int? EmployerId { get; set; }

    // SUPERPOWER 1: Get the Employer's Name without loading the Organization object
    [RemoteProperty(remoteEntityType: typeof(Organization), keyPath: new[] { nameof(EmployerId), nameof(Organization.Name) })]
    public string EmployerName { get; set; }

    // SUPERPOWER 2: Get the Employer's Country ID (2 hops away!)
    // Person -> Organization -> Address -> Country
    [RemoteKey(remoteEntityType: typeof(Country), keyPath: new[] {
        nameof(EmployerId), 
        nameof(Organization.HeadquartersAddressId), 
        nameof(Address.CountryId), 
        nameof(Country.Id) })]
    public int? EmployerCountryId { get; set; }
}

// Now you can query and filter by these remote properties as if they were local!
var usEmployees = provider.Query<Person>()
    .Where(p => p.EmployerCountryId == 1) // Filters by joined table!
    .ToList();
```

### The "Superpower" Advantage

To achieve this **without** FunkyORM, you'd typically have to do this:

**Entity Framework Core**:
```csharp
// Requires loading the entire graph or creating a custom DTO
var person = context.People
    .Include(p => p.Employer)
    .ThenInclude(e => e.Address)
    .ThenInclude(a => a.Country) // Heavy!
    .FirstOrDefault();
// Access: person.Employer.Address.Country.Name
```

**Dapper**:
```csharp
// Requires writing raw SQL joins and manual mapping
var sql = @"SELECT p.*, c.Name as CountryName 
            FROM Person p 
            JOIN Organization o ON p.EmployerId = o.Id ..."; // ... and so on
```

**FunkyORM**:
```csharp
// Just add the attribute. We handle the joins.
// Path: Person -> Organization (via EmployerId) -> Address -> Country
[RemoteProperty(remoteEntityType: typeof(Country), keyPath: new[] {
    nameof(EmployerId),
    nameof(Organization.HeadquartersAddressId),
    nameof(Address.CountryId),
    nameof(Country.Name) })]
public string EmployerCountryName { get; set; }
```

### 6. JSON & Computed Column Attributes

> **New in v3.2.1**: All four attribute types below are fully implemented, tested, and stable across SQL Server, PostgreSQL, and SQLite. Together they let you replace read-only SQL views with decorated detail entities: extract scalars from JSON columns, compute expressions across sibling columns, aggregate child tables (counts, sums, conditional counts), and project child records as inline JSON arrays — all queryable and filterable via standard LINQ, with no raw SQL required.

Many modern databases store semi-structured data in JSON columns. FunkyORM's `[JsonPath]` attribute lets you extract and query these values without creating SQL views.

```csharp
// Your table has a JSON column: metadata NVARCHAR(MAX)
// Example value: {"priority":"high","client":{"name":"Acme Corp","region":"NA"},"risk_level":3}

// Canonical entity — no JSON attributes here
[Table("project")]
public class Project
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Metadata { get; set; }  // Raw JSON column
}

// Detail class — extracts JSON scalars into typed properties
[Table("project")]
public class ProjectScorecard : Project
{
    [JsonPath("metadata", "$.priority")]
    public string Priority { get; set; }

    [JsonPath("metadata", "$.client.name")]
    public string ClientName { get; set; }

    [JsonPath("metadata", "$.risk_level", SqlType = "int")]
    public int? RiskLevel { get; set; }
}

// Query and filter on JSON values with standard LINQ!
var highPriority = provider.Query<ProjectScorecard>()
    .Where(p => p.Priority == "high")
    .ToList();
// Generated SQL (MSSQL): WHERE JSON_VALUE(project.metadata, '$.priority') = @p0
// Generated SQL (Postgres): WHERE project.metadata #>> '{priority}' = @p0
```

Like remote properties, `[JsonPath]` attributes belong on **Detail classes**, not canonical entities.

#### View-Replacing Attribute Family

FunkyORM provides four attribute types designed to eliminate SQL views entirely in code:

| Attribute | What It Does | Status |
|:---|:---|:---|
| `[JsonPath]` | Extract scalars from JSON columns | ✅ Implemented |
| `[SqlExpression]` | Computed columns — `COALESCE`, `CONCAT`, `CASE` | ✅ Implemented |
| `[SubqueryAggregate]` | Correlated counts/sums — replaces `OUTER APPLY` | ✅ Implemented |
| `[JsonCollection]` | Project child records as JSON arrays | ✅ Implemented |

```csharp
// A complete Detail class using all four attribute types
[Table("project")]
public class ProjectScorecard : ProjectEntity
{
    // Phase 1: JSON scalar extraction
    [JsonPath("metadata", "$.priority")]
    public string Priority { get; set; }

    // Phase 2: Computed expression
    [SqlExpression("COALESCE({Score}, 0)")]
    public int EffectiveScore { get; set; }

    // Phase 3: Subquery aggregate
    [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
        AggregateFunction.Count)]
    public int MilestoneCount { get; set; }

    // Phase 3: Conditional subquery aggregate
    [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
        AggregateFunction.ConditionalCount,
        ConditionColumn = nameof(ProjectMilestoneEntity.Status), ConditionValue = "completed")]
    public int MilestonesCompleted { get; set; }

    // Phase 4: JSON collection projection
    [JsonCollection(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
        Columns = new[] { "Title", "Status", "DueDate" }, OrderBy = "DueDate")]
    public string MilestonesJson { get; set; }
}
```

All four work in `Get<T>`, `Query<T>`, `GetList<T>`, and **WHERE clauses**. See the [Usage Guide](Usage.md) for detailed documentation, generated SQL, and parameter tables.

#### `OrderBy`, `Distinct()`, and projecting computed/remote attributes (v3.8.1)

`OrderBy` / `OrderByDescending` / `ThenBy` / `ThenByDescending` can target `[JsonPath]`, `[SqlExpression]`,
`[SubqueryAggregate]`, and `[RemoteProperty]`/`[RemoteKey]` — FunkyORM sorts by the *resolved* SQL (the JSON
accessor, the expression, the correlated subquery, or the joined `alias.column`), not a non-existent column.
`Distinct()` emits `SELECT DISTINCT`. And the **self-contained** computed attributes (`[JsonPath]`,
`[SqlExpression]`, `[SubqueryAggregate]`) can be projected in a custom `.Select(...)` — they materialize back
onto the property. All on the whole-entity `Query<T>()…` path across all four providers.

```csharp
// Order by computed attributes (whole-entity query)
db.Query<ProjectScorecard>().OrderByDescending(p => p.MilestoneCount).ThenBy(p => p.EffectiveScore);
db.Query<ProjectScorecard>().OrderBy(p => p.Priority);              // sorts by the JSON_VALUE / json_extract

// Project computed attributes into a custom shape (they materialize back onto the property)
db.Query<ProjectScorecard>().Select(p => new ProjectScorecard {    // SELECT JSON_VALUE(...) AS Priority,
    Priority = p.Priority,                                          //        COALESCE(score,0) AS EffectiveScore,
    EffectiveScore = p.EffectiveScore,                             //        (SELECT COUNT(*) ...) AS MilestoneCount
    MilestoneCount = p.MilestoneCount });

// DISTINCT
db.Query<ProjectScorecard>().Distinct();                            // SELECT DISTINCT <all columns>  (¹ PG json caveat)
db.Query<ProjectScorecard>().OrderBy(p => p.EffectiveScore).Distinct();   // full-entity distinct + order by a computed attr  (¹)
db.Query<ProjectScorecard>().Select(p => new ProjectScorecard { Name = p.Name }).Distinct();
db.Query<ProjectScorecard>().Select(p => new ProjectScorecard { Name = p.Name })
                            .Distinct().OrderBy(p => p.Name);       // order key is in the projection — OK
```

**Documented limits (clear errors, by design):**
- ¹ **PostgreSQL + full-entity `Distinct()` on an entity that declares `[JsonCollection]`:** `[JsonCollection]` emits
  `json_agg(row_to_json(...))`, which returns the **`json`** type — and PostgreSQL has no equality operator for `json`
  (only `jsonb`), so `SELECT DISTINCT *` can't compare it and errors at the engine (`42883`). (`[JsonPath]` and a `jsonb`
  source column are *not* the trigger — it's the `[JsonCollection]` aggregate.) Remedy: `Distinct()` a column projection
  that excludes the `[JsonCollection]` columns, or don't full-entity-`Distinct()` an entity that declares one.
  **SQL Server, MySQL, and SQLite are unaffected.**
- **`[RemoteProperty]`/`[RemoteKey]` can't be projected in a custom `.Select(...)`** — they resolve to a joined
  `alias.column` that a custom projection's `FROM` doesn't carry, so projecting one throws `NotSupportedException`
  with a clear message. Query the whole entity, or use a detail class that declares it. (The self-contained
  `[JsonPath]`/`[SqlExpression]`/`[SubqueryAggregate]` project fine — see above.)
- `Distinct().Count()` throws `NotSupportedException` (count client-side, or drop `Distinct`) — postponed to a later cut.
- Under a custom `.Select(...)`, `Distinct()` + `OrderBy` by a column *not* in the projection throws
  `InvalidOperationException` (SQL requires the order key in the `DISTINCT` list).

## Concurrency & Connection Management

FunkyORM is fully safe for concurrent use in environments like **Blazor Server**, **parallel async workflows**, and **multi-threaded services**.

### How It Works

| Scenario | Connection Strategy | Concurrent-Safe? |
|:---|:---|:---|
| **Normal operations** (no transaction) | Each operation gets its own dedicated connection from the ADO.NET connection pool | ✅ Yes — fully concurrent |
| **Within a transaction** | All operations share one connection (required by ADO.NET) | ⚠️ Sequential only |

### Provider Lifetime Guidance

| Environment | Recommended Lifetime | Why |
|:---|:---|:---|
| **Console / background service** | Singleton or transient | No concurrency concerns |
| **ASP.NET Core (controllers)** | Scoped or transient | Request-scoped avoids cross-request transaction leaks |
| **Blazor Server** | **Scoped** (per-circuit) | Each circuit gets its own provider; concurrent operations within a circuit are safe |
| **Parallel `Task.WhenAll`** | One provider per task, or no transaction | Non-transactional ops are safe on a shared provider |

### ⚠️ Transaction Concurrency Rule

All operations within a transaction **must be awaited sequentially**:

```csharp
// ✅ CORRECT — sequential awaits within a transaction
provider.BeginTransaction();
try
{
    await provider.DeleteAsync<Person>(p => p.Id == 1);
    await provider.InsertAsync<Person, int>(newPerson);  // waits for delete to finish
    provider.CommitTransaction();
}
catch
{
    provider.RollbackTransaction();
    throw;
}

// ❌ WRONG — concurrent operations within a transaction will throw InvalidOperationException
provider.BeginTransaction();
await Task.WhenAll(
    provider.DeleteAsync<Person>(p => p.Id == 1),
    provider.InsertAsync<Person, int>(newPerson)  // THROWS: concurrent transactional usage detected
);
```

This is an ADO.NET limitation — a single `IDbConnection` cannot execute multiple commands simultaneously. FunkyORM detects this misuse and throws a clear `InvalidOperationException` instead of the cryptic ADO.NET "reader already associated" error.

**Outside of a transaction**, concurrent operations are fully supported because each operation automatically receives its own pooled connection:

```csharp
// ✅ CORRECT — concurrent operations without a transaction
var tasks = new[]
{
    provider.GetAsync<Person>(1),
    provider.GetAsync<Person>(2),
    provider.GetAsync<Person>(3)
};
var results = await Task.WhenAll(tasks); // Each gets its own pooled connection
```

## Row-Level Security & Audit Context (v3.8+)

When your app authenticates to the database as a single identity (a managed identity, service account, or shared login) but you need the **end-user's** identity to ride along on every query — for **Row-Level Security** filtering or **audit attribution** — FunkyORM can prime per-request session context onto the exact connection each command uses.

You supply a per-request `FunkyAuditContext` of **caller-defined** session-context keys (FunkyORM is agnostic about their names/meaning); FunkyORM primes them onto each connection, and your RLS predicate reads them back. It also prepends an optional self-attributing audit comment (opaque identifiers only — never PII) so captured statement text is attributable.

```csharp
accessor.Set(new FunkyAuditContext
{
    Entries = new[]
    {
        new SessionContextEntry("myapp.user_id",  userId),   // dot-namespace keys for PostgreSQL
        new SessionContextEntry("myapp.group_ids", string.Join(",", groupIds)),
    },
    AuditSubjectId = userId,             // opaque id only, no name/email/PII
});
// PHI providers can be configured fail-closed (throw when no context is present).
```

Capability is per provider: **SQL Server & PostgreSQL** get RLS filtering *and* attribution; **MySQL** gets attribution only (no native RLS); **SQLite** is a no-op.

> **Full setup, RLS policy examples (SQL Server + PostgreSQL), security notes, and troubleshooting:** see the **[Audit Context Runbook](docs/guides/AUDIT_CONTEXT_RUNBOOK.md)**.

## Database Provider Differences

FunkyORM generates database-specific SQL through its `ISqlDialect` abstraction. Your entity classes and LINQ queries are portable, but the generated SQL differs to match each platform's conventions.

| Feature | SQL Server | PostgreSQL | MySQL | SQLite |
| :--- | :--- | :--- | :--- | :--- |
| **Provider Class** | `SqlServerOrmDataProvider` | `PostgreSqlOrmDataProvider` | `MySqlOrmDataProvider` | `SqliteOrmDataProvider` |
| **Identifier Quoting** | `[brackets]` | `"double-quotes"` | `` `backticks` `` | `"double-quotes"` (reserved words only) |
| **Insert Return** | `OUTPUT INSERTED.id` | `RETURNING id` | `LAST_INSERT_ID()` | `SELECT last_insert_rowid()` |
| **Paging** | `OFFSET…FETCH NEXT` | `LIMIT…OFFSET` | `LIMIT…OFFSET` | `LIMIT…OFFSET` |
| **String Concat** | `+` | `‖` | `CONCAT()` | `‖` |
| **Date Parts** | `YEAR()`, `MONTH()`, `DAY()` | `EXTRACT(YEAR FROM …)` | `EXTRACT(YEAR FROM …)` | `strftime('%Y', …)` |
| **Boolean Type** | `BIT` (0/1) | Native `BOOLEAN` | `TINYINT(1)` (0/1) | `INTEGER` (0/1) |
| **Target Frameworks** | `net8.0`, `netstandard2.0`, `net48` | `net8.0`, `netstandard2.0` | `net8.0`, `netstandard2.0` | `net8.0`, `netstandard2.0` |
| **ADO.NET Driver** | `Microsoft.Data.SqlClient` | `Npgsql` | `MySqlConnector` (MIT) | `Microsoft.Data.Sqlite` |
| **JSON Extraction** | `JSON_VALUE(col, '$.path')` | `col #>> '{path}'` | `JSON_EXTRACT(col, '$.path')` | `json_extract(col, '$.path')` |
| **Stored Procedure Execution** | ✅ Full (result set, scalar, non-query, output params) | ⚠️ Scalar / non-query via `CALL` (use a `FUNCTION RETURNS TABLE` for result sets) | ✅ Full (result set, scalar, non-query, output params) | ❌ N/A (SQLite has no stored procedures) |
| **JSON Collection** | `FOR JSON PATH` | `json_agg(row_to_json(…))` | `JSON_ARRAYAGG(JSON_OBJECT(…))` | `json_group_array(json_object(…))` |

### PostgreSQL-Specific Notes

- **Naming convention**: PostgreSQL is case-sensitive for quoted identifiers. Unquoted identifiers are folded to lowercase. FunkyORM quotes reserved words automatically (e.g., `"User"`, `"Order"`), and leaves non-reserved names unquoted (matching PostgreSQL's lowercase convention).
- **Npgsql versions**: The PostgreSQL provider uses Npgsql 9.x for `net8.0` and Npgsql 8.x for `netstandard2.0` (last version with netstandard support).
- **Timestamps**: The provider sets `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)` to ensure `DateTime` values are handled consistently without requiring `timestamptz` conversions.

### MySQL-Specific Notes

- **Driver**: Uses the MIT-licensed `MySqlConnector` (not Oracle's GPL `MySql.Data`), multi-targeting `net8.0` and `netstandard2.0`.
- **Identity**: MySQL has no `RETURNING` clause; the provider retrieves `AUTO_INCREMENT` ids via `MySqlCommand.LastInsertedId` after insert. Non-identity (Guid/string) keys are supplied by the caller.
- **GUIDs**: Stored as `CHAR(36)`. Add `GuidFormat=Char36` to the connection string so `Guid` properties round-trip transparently (`BINARY(16)` is also supported via `GuidFormat=Binary16`).
- **String building**: MySQL's `+` operator performs numeric addition, so string concatenation translates to `CONCAT()`.
- **Case sensitivity**: Default `_ci` collations make string comparisons case-insensitive (matching SQL Server) — no case-folding workaround is emitted. Use the `MySqlStringComparison.CaseSensitive` constructor option to apply `COLLATE utf8mb4_bin`. Column names are always case-insensitive; **table-name** case sensitivity follows the server's `lower_case_table_names` (case-sensitive on Linux by default), so lowercase table names are recommended for cross-platform portability.
- **JSON**: Native `JSON` type. `[JsonPath]` uses `JSON_UNQUOTE(JSON_EXTRACT(col, '$.path'))` (with `CAST` for typed extraction), and `[JsonCollection]` uses `JSON_ARRAYAGG(JSON_OBJECT(...))`.
- **Reserved word quoting**: Uses backtick quoting, applied to identifiers matching MySQL's reserved-word list.
- **Schemas**: A `[Table(Schema = "x")]` maps to `` `x`.`table` `` (in MySQL, a "schema" is a database).

### SQLite-Specific Notes

- **File-backed and in-memory**: SQLite databases are single-file or in-memory. Connection strings use `Data Source=path/to/file.db` or `Data Source=:memory:` for transient in-memory databases.
- **Connection-string filename resolution**: The provider resolves relative filenames against the application's base directory and supports paths containing environment variables.
- **Type affinity**: SQLite has no strict type system. `DateTime` values are stored as `TEXT` (ISO-8601) or `REAL`/`INTEGER` and are parsed automatically. `BOOLEAN` is stored as `INTEGER` (0/1). `DECIMAL` is stored as `REAL`.
- **No stored procedures**: SQLite does not support stored procedures or functions. Calling stored-procedure methods will throw `NotSupportedException`.
- **JSON support**: Requires SQLite 3.38+ (bundled with `Microsoft.Data.Sqlite`). Uses `json_extract()` for scalar extraction and `json_group_array(json_object(...))` for collection projection.
- **Concurrency**: SQLite supports WAL mode for concurrent reads, but only one writer at a time. The provider handles this transparently, but high-write-concurrency scenarios may experience `SQLITE_BUSY` under heavy load.
- **Reserved word quoting**: Like PostgreSQL, SQLite uses double-quote quoting, but only applies it to identifiers that match SQLite's reserved word list.

## Documentation

For detailed usage examples, performance benchmarks, and a comparison with other ORMs, please see our **[Usage Guide](Usage.md)**.

### Comparison: FunkyORM vs. The World

| Feature | Entity Framework | Dapper | FunkyORM |
| :--- | :--- | :--- | :--- |
| **Setup** | Heavy (DbContext, Config) | Light | **Lightest** |
| **Query Style** | LINQ | SQL Strings | **LINQ** |
| **Performance** | Good (if tuned) | Excellent | **Excellent** |
| **Mapping** | Strict (needs config) | Manual/Strict | **Forgiving/Auto** |
| **SQL Injection** | Protected | Manual Parameterization | **Protected** |
| **Vibe** | Enterprise Java | Hardcore Metal | **Cool Jazz** |
