# FunkyORM AI Agent Instructions (PostgreSQL)

> **Target Audience**: AI Agents (GitHub Copilot, Cursor, Gemini, GPT, Claude, etc.) assisting developers who use the PostgreSQL provider in the `Funcular.Data.Orm` package.
>
> **Package**: `Funcular.Data.Orm` | **Namespace**: `Funcular.Data.Orm.PostgreSql`
>
> This file supplements the shared [FUNKYORM_AI_INSTRUCTIONS.md](../Funcular.Data.Orm.SqlServer/FUNKYORM_AI_INSTRUCTIONS.md) with PostgreSQL-specific guidance. All rules in the shared instructions apply here. **Read the shared instructions first.**

---

## Provider Initialization

```csharp
using Funcular.Data.Orm.PostgreSql;

var connectionString = "Host=localhost;Port=5432;Database=mydb;Username=myuser;Password=mypassword";
var provider = new PostgreSqlOrmDataProvider(connectionString)
{
    Log = s => Console.WriteLine(s) // Optional: see generated SQL
};
```

---

## PostgreSQL-Specific Behavior

### Identifier Quoting

PostgreSQL uses `"double-quotes"` for identifiers (SQL Server uses `[brackets]`). FunkyORM handles this automatically:

- **Reserved words** (`User`, `Order`, `Select`, `Key`, etc.) are automatically quoted: `"User"`, `"Order"`
- **Non-reserved identifiers** are left unquoted and folded to lowercase by PostgreSQL
- You do NOT need to change your entity classes or `[Table]`/`[Column]` attributes when switching from SQL Server

**Caveat**: If your PostgreSQL schema was created with quoted mixed-case column names (e.g., `CREATE TABLE foo ("Name" TEXT)`), the column becomes case-sensitive. FunkyORM only quotes reserved words, so `Name` (non-reserved) will be sent unquoted and lowercased to `name`, which won't match `"Name"`. To avoid this, create PostgreSQL columns unquoted (the PostgreSQL convention) or only quote reserved words in your DDL.

### Paging

SQL Server uses `OFFSET...FETCH NEXT`; PostgreSQL uses `LIMIT...OFFSET`. FunkyORM generates the correct syntax automatically:

```csharp
// Same C# code for both providers
var page = provider.Query<Person>()
    .OrderBy(p => p.Id)
    .Skip(10).Take(10)
    .ToList();
```

**PostgreSQL SQL:** `SELECT ... FROM person ORDER BY id LIMIT 10 OFFSET 10`

### Insert Return Values

SQL Server uses `OUTPUT INSERTED.id`; PostgreSQL uses `RETURNING id`. Both work transparently:

```csharp
var id = provider.Insert<Person, int>(person); // Works on both providers
```

### Timestamps

The PostgreSQL provider sets `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)` at startup. This ensures `DateTime` values are handled as `timestamp without time zone`, avoiding `InvalidCastException` errors from Npgsql's default `timestamptz` behavior. If your application requires `DateTimeOffset` / `timestamptz` semantics, you may need to override this.

### Boolean Columns

PostgreSQL uses native `BOOLEAN` (not `BIT`). FunkyORM maps C# `bool` properties correctly to both:

```csharp
public class User
{
    [Column("Select")]  // Reserved word -- automatically quoted as "Select"
    public bool Select { get; set; }  // Maps to BOOLEAN in PostgreSQL, BIT in SQL Server
}
```

### Npgsql Version Matrix

| Target Framework | Npgsql Version | Notes |
|-----------------|---------------|-------|
| `net8.0` | 9.x | Latest features |
| `netstandard2.0` | 8.0.x | Last version with netstandard2.0 support |

---

## JSON & Computed Column Attributes (PostgreSQL Dialect)

FunkyORM's JSON and computed column attributes (v3.2+) work identically on PostgreSQL — the same C# attributes produce PostgreSQL-native SQL via `ISqlDialect`. See the shared [FUNKYORM_AI_INSTRUCTIONS.md](../Funcular.Data.Orm.SqlServer/FUNKYORM_AI_INSTRUCTIONS.md) for the full attribute taxonomy, parameter tables, and examples.

### Phase 1: `[JsonPath]` — Dialect Differences (? Implemented)

| Feature | SQL Server | PostgreSQL |
|:---|:---|:---|
| **String extraction** | `JSON_VALUE(col, '$.path')` | `col #>> '{path}'` |
| **Nested path** | `JSON_VALUE(col, '$.client.name')` | `col #>> '{client,name}'` |
| **Typed extraction** | `CAST(JSON_VALUE(col, '$.path') AS int)` | `(col #>> '{path}')::int` |

The path syntax conversion (`$.client.name` ? `{client,name}`) is handled automatically by the PostgreSQL dialect. You use the same `$.dot.notation` in your C# attribute regardless of provider.

```csharp
// Same C# code for both SQL Server and PostgreSQL
[JsonPath("metadata", "$.client.name")]
public string ClientName { get; set; }

[JsonPath("metadata", "$.risk_level", SqlType = "int")]
public int? RiskLevel { get; set; }
```

> **Tip:** PostgreSQL `jsonb` columns offer better performance for JSON operations than `text`/`json` columns. If you plan to filter frequently on JSON values, consider using `jsonb` column type in your PostgreSQL schema.

### Phase 2: `[SqlExpression]` — Dialect Differences (? Implemented)

Key SQL syntax differences that the `mssql:` / `postgresql:` overrides address:

| Operation | SQL Server | PostgreSQL |
|:---|:---|:---|
| **String concatenation** | `+` or `CONCAT()` | `\|\|` |
| **Null-safe concat** | `CONCAT({A}, ' ', {B})` (CONCAT ignores NULLs) | `{A} \|\| COALESCE(' ' \|\| {B}, '')` |
| **Boolean expressions** | `CASE WHEN ... THEN 1 ELSE 0 END` | Native `BOOLEAN` |

When expressions differ between providers, use the dual-expression constructor:

```csharp
[SqlExpression(
    mssql: "CONCAT({FirstName}, CASE WHEN {LastName} IS NOT NULL THEN ' ' + {LastName} ELSE '' END)",
    postgresql: "{FirstName} || COALESCE(' ' || {LastName}, '')")]
public string LeadName { get; set; }
```

### Phase 3: `[SubqueryAggregate]` — Dialect Differences (? Implemented)

Correlated scalar subqueries are syntactically identical on both providers. No dialect-specific overrides needed:

```sql
-- Identical on both SQL Server and PostgreSQL:
(SELECT COUNT(*) FROM project_milestone ms WHERE ms.project_id = project.id)
```

Future optimization: SQL Server's `OUTER APPLY` may be used when multiple aggregates share the same source; PostgreSQL's equivalent is `LATERAL JOIN`.

### Phase 4: `[JsonCollection]` — Dialect Differences (? Implemented)

This is where the dialects differ most significantly:

| Feature | SQL Server | PostgreSQL |
|:---|:---|:---|
| **JSON array** | `FOR JSON PATH` | `json_agg(row_to_json(sub))` |
| **Full expression** | `(SELECT ... FOR JSON PATH)` | `(SELECT json_agg(row_to_json(sub)) FROM (...) sub)` |

The `ISqlDialect.BuildJsonCollectionSubquery()` method handles this difference transparently. The C# attribute is identical on both providers:

```csharp
// Same C# code produces different SQL per provider
[JsonCollection(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
    columns: new[] { "Title", "Status", "DueDate" },
    orderBy: "DueDate")]
public string MilestonesJson { get; set; }
```

---

## All Other Rules

All shared rules apply without modification:

- The "Detail" entity pattern for remote attributes, `[JsonPath]`, and all computed column attributes
- Safe delete transaction mandate
- No `.Value`/`.HasValue` in LINQ
- Naming conventions and auto-mapping
- Remote attribute path resolution
- JSON & computed column querying (see shared instructions for full attribute taxonomy, examples, and agent rules)
- The "Duplicate Class Name" problem

See the shared [FUNKYORM_AI_INSTRUCTIONS.md](../Funcular.Data.Orm.SqlServer/FUNKYORM_AI_INSTRUCTIONS.md) for complete details.
