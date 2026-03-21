# FunkyORM AI Agent Instructions (PostgreSQL)

> **Target Audience**: AI Agents (GitHub Copilot, Cursor, Gemini, GPT, Claude, etc.) assisting developers who use the `Funcular.Data.Orm.PostgreSql` NuGet package.
>
> **Package**: `Funcular.Data.Orm.PostgreSql` | **Namespace**: `Funcular.Data.Orm.PostgreSql`
>
> This file supplements the shared [COPILOT_INSTRUCTIONS.md](../Funcular.Data.Orm.SqlServer/COPILOT_INSTRUCTIONS.md) with PostgreSQL-specific guidance. All rules in the shared instructions apply here. **Read the shared instructions first.**

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

## All Other Rules

All shared rules apply without modification:

- The "Detail" entity pattern for remote attributes
- Safe delete transaction mandate
- No `.Value`/`.HasValue` in LINQ
- Naming conventions and auto-mapping
- Remote attribute path resolution
- The "Duplicate Class Name" problem

See the shared [COPILOT_INSTRUCTIONS.md](../Funcular.Data.Orm.SqlServer/COPILOT_INSTRUCTIONS.md) for complete details.
