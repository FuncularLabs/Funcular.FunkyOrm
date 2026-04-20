> **Recent Changes**
> * **v3.2.1**: 🧩 **All 4 view-replacing attributes implemented!** `[JsonPath]` (JSON scalars), `[SqlExpression]` (COALESCE/CONCAT/CASE), `[SubqueryAggregate]` (COUNT/SUM), and `[JsonCollection]` (child records as JSON arrays). Eliminate SQL views entirely in code. Auto-excludes `[Timestamp]` and `[DatabaseGenerated]` columns from INSERT/UPDATE. See [JSON & Computed Column Attributes](#6-json--computed-column-attributes).
> * **v3.1.0**: 🐘 **PostgreSQL Support!** FunkyORM now supports PostgreSQL with a full `PostgreSqlOrmDataProvider` — included in the `Funcular.Data.Orm` package. Full LINQ-to-SQL, remote keys/properties, transactions, and reserved word handling — everything you know from the MSSQL provider, now on Postgres. See [Database Provider Differences](#database-provider-differences) for details.
> * **v3.0.1**: Introduced `ISqlDialect` for multi-database support. Added `[RemoteKey]` and `[RemoteProperty]` attributes, `Guid`/`String` primary keys, generic `Insert<T, TKey>` overloads, and non-identity key handling.


# Funcular / Funky ORM: a speedy, lambda-powered .NET micro-ORM for MSSQL & PostgreSQL
![Funcular logo](https://raw.githubusercontent.com/FuncularLabs/Funcular.FunkyOrm/master/funky-orm-lineart-256x256.png)

[![NuGet](https://img.shields.io/nuget/v/Funcular.Data.Orm.svg)](https://www.nuget.org/packages/Funcular.Data.Orm)
[![Downloads](https://img.shields.io/nuget/dt/Funcular.Data.Orm.svg)](https://www.nuget.org/packages/Funcular.Data.Orm)
[![CI status](https://img.shields.io/github/actions/workflow/status/FuncularLabs/Funcular.FunkyOrm/ci.yml?branch=master&label=CI)](https://github.com/FuncularLabs/Funcular.FunkyOrm/actions)
[![Tests](https://img.shields.io/github/actions/workflow/status/FuncularLabs/Funcular.FunkyOrm/ci.yml?branch=master&label=Tests)](https://github.com/FuncularLabs/Funcular.FunkyOrm/actions)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

> **For AI Agents**: Please refer to [FUNKYORM_AI_INSTRUCTIONS.md](Funcular.Data.Orm.SqlServer/FUNKYORM_AI_INSTRUCTIONS.md) for strict coding guidelines and "Happy Path" patterns. This file is included in the NuGet package. A PostgreSQL-specific supplement is at [FUNKYORM_AI_INSTRUCTIONS_POSTGRESQL.md](Funcular.Data.Orm.PostgreSql/FUNKYORM_AI_INSTRUCTIONS_POSTGRESQL.md).
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
*   **JSON & Computed Column Attributes**: Four attribute types that eliminate SQL views entirely in code — `[JsonPath]`, `[SqlExpression]`, `[SubqueryAggregate]`, and `[JsonCollection]`. Works on both SQL Server and PostgreSQL.
*   **Explicit Collection Population**: Leverage `RemoteKey` properties to easily populate related collections without the overhead of massive object graphs or N+1 queries.
*   **Cached Reflection**: Funcular ORM caches reflection results to minimize overhead and maximize performance.
*   **Nullable-Friendly**: Nullable properties work seamlessly in LINQ queries—no need for `.Value` or `.HasValue`. The ORM handles the unwrapping for you.

    
## Getting Started

### 1. Installation

Install the FunkyORM package — both SQL Server and PostgreSQL providers are included:

```bash
dotnet add package Funcular.Data.Orm
```

### 2. Initialization

Create a provider instance. You can register it as a singleton in your DI container, or create it as needed.

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

> **Note:** Both providers implement the same base class and support the same LINQ query API, CRUD operations, remote keys/properties, and transactions. Your entity classes and query code are fully portable between providers.

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

> **New in v3.2.1**: All four attribute types below are fully implemented, tested, and stable across both SQL Server and PostgreSQL.

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

## Database Provider Differences

FunkyORM generates database-specific SQL through its `ISqlDialect` abstraction. Your entity classes and LINQ queries are portable, but the generated SQL differs to match each platform's conventions.

| Feature | SQL Server | PostgreSQL |
| :--- | :--- | :--- |
| **Provider Class** | `SqlServerOrmDataProvider` | `PostgreSqlOrmDataProvider` |
| **Identifier Quoting** | `[brackets]` | `"double-quotes"` |
| **Insert Return** | `OUTPUT INSERTED.id` | `RETURNING id` |
| **Paging** | `OFFSET…FETCH NEXT` | `LIMIT…OFFSET` |
| **String Concat** | `+` | `‖` |
| **Date Parts** | `YEAR()`, `MONTH()`, `DAY()` | `EXTRACT(YEAR FROM …)` |
| **Boolean Type** | `BIT` (0/1) | Native `BOOLEAN` |
| **Target Frameworks** | `net8.0`, `netstandard2.0`, `net48` | `net8.0`, `netstandard2.0` |
| **ADO.NET Driver** | `Microsoft.Data.SqlClient` | `Npgsql` |
| **JSON Extraction** | `JSON_VALUE(col, '$.path')` | `col #>> '{path}'` |

### PostgreSQL-Specific Notes

- **Naming convention**: PostgreSQL is case-sensitive for quoted identifiers. Unquoted identifiers are folded to lowercase. FunkyORM quotes reserved words automatically (e.g., `"User"`, `"Order"`), and leaves non-reserved names unquoted (matching PostgreSQL's lowercase convention).
- **Npgsql versions**: The PostgreSQL provider uses Npgsql 9.x for `net8.0` and Npgsql 8.x for `netstandard2.0` (last version with netstandard support).
- **Timestamps**: The provider sets `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)` to ensure `DateTime` values are handled consistently without requiring `timestamptz` conversions.

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
