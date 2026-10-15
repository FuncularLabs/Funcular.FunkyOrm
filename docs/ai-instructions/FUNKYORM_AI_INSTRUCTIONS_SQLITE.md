# FunkyORM AI Agent Instructions (SQLite)

> **Target Audience**: AI Agents (GitHub Copilot, Cursor, Gemini, GPT, Claude, etc.) assisting developers who use the SQLite provider in the `Funcular.Data.Orm` package.
>
> **Package**: `Funcular.Data.Orm` | **Namespace**: `Funcular.Data.Orm.Sqlite`
>
> This file supplements the shared [FUNKYORM_AI_INSTRUCTIONS.md](../Funcular.Data.Orm.SqlServer/FUNKYORM_AI_INSTRUCTIONS.md) with SQLite-specific guidance. All rules in the shared instructions apply here. **Read the shared instructions first.**

---

## Provider Initialization

```csharp
using Funcular.Data.Orm.Sqlite;

// File-backed database (most common)
var connectionString = "Data Source=myapp.db";
var provider = new SqliteOrmDataProvider(connectionString)
{
    Log = s => Console.WriteLine(s) // Optional: see generated SQL
};

// In-memory database (for testing)
var inMemoryProvider = new SqliteOrmDataProvider("Data Source=:memory:");
```

### Connection String Filename Resolution

The SQLite provider automatically resolves filenames in the `Data Source` parameter:

- **Relative paths** are resolved relative to the current working directory
- **Environment variables** (e.g., `%APPDATA%\myapp.db`) are expanded
- **Absolute paths** are used as-is

```csharp
// All of these work:
var provider = new SqliteOrmDataProvider("Data Source=myapp.db");
var provider = new SqliteOrmDataProvider("Data Source=C:\\Data\\myapp.db");
var provider = new SqliteOrmDataProvider("Data Source=%LOCALAPPDATA%\\MyApp\\data.db");
```

---

## SQLite-Specific Behavior

### Identifier Quoting

SQLite uses `"double-quotes"` for identifiers. FunkyORM handles this automatically:

- **Reserved words** (`User`, `Order`, `Select`, `Key`, etc.) are automatically quoted: `"User"`, `"Order"`
- **Non-reserved identifiers** are left unquoted
- You do NOT need to change your entity classes or `[Table]`/`[Column]` attributes when switching from SQL Server or PostgreSQL

### Type Affinity

SQLite uses dynamic typing with type affinity. FunkyORM maps CLR types as follows:

| CLR Type | SQLite Storage | Notes |
|:---|:---|:---|
| `int`, `long` | INTEGER | Native |
| `bool` | INTEGER | Stored as 0/1, mapped automatically |
| `DateTime` | TEXT | Stored as ISO 8601 strings, parsed on read |
| `Guid` | TEXT | Stored as string representation |
| `decimal`, `double`, `float` | REAL | Standard numeric affinity |
| `string` | TEXT | Native |
| `byte[]` | BLOB | Native |

### Paging

SQLite uses `LIMIT...OFFSET`, same as PostgreSQL:

```csharp
// Same C# code for all providers
var page = provider.Query<Person>()
    .OrderBy(p => p.Id)
    .Skip(10).Take(10)
    .ToList();
```

**SQLite SQL:** `SELECT ... FROM person ORDER BY id LIMIT 10 OFFSET 10`

### Insert Return Values

SQLite uses `last_insert_rowid()` for identity (AUTOINCREMENT) columns. For non-identity primary keys (Guid, String), the provider uses a `SELECT` after insert:

```csharp
var id = provider.Insert<Person, int>(person); // Works on all providers
```

### Boolean Columns

SQLite stores booleans as INTEGER (0 or 1). FunkyORM maps C# `bool` properties correctly:

```csharp
public class User
{
    [Column("Select")]  // Reserved word — automatically quoted as "Select"
    public bool Select { get; set; }  // Maps to INTEGER 0/1 in SQLite
}
```

### String Comparison

FunkyORM emits no `COLLATE` of its own on SQLite; comparisons follow SQLite's rules:

- `Contains`, `StartsWith`, `EndsWith` use `LIKE`, which folds ASCII case whatever the column's collation (case-insensitive for ASCII; the value's `%`, `_` and `\` are escaped and matched literally, and the pattern ends with `ESCAPE '!'`).
- `==`, `!=`, `<`, `>` follow the column's collation: SQLite's default `BINARY` is **case-sensitive**. For case-insensitive equality, declare the column `COLLATE NOCASE` in the table (`name TEXT COLLATE NOCASE`).

Do **not** suggest the `SqliteStringComparison` constructor parameter (or `SqliteOrmDataProvider.StringComparison`): it has no effect — the provider stores it and never reads it — and is `[Obsolete]` since 3.11.0.

### Date/Time Handling

SQLite stores dates as TEXT (ISO 8601 format). FunkyORM handles parsing automatically. For date-part extraction in queries, SQLite uses `strftime()` on the table-qualified column (a nullable property through `.Value`, a non-nullable one directly; any other member of a member, such as `.Hour` or `.Date`, throws `NotSupportedException`):

| C# Expression | SQLite SQL |
|:---|:---|
| `p.Birthdate.Value.Year`, `p.PostedAt.Year` | `CAST(strftime('%Y', person.birthdate) AS INTEGER)` |
| `p.Birthdate.Value.Month` | `CAST(strftime('%m', person.birthdate) AS INTEGER)` |
| `p.Birthdate.Value.Day` | `CAST(strftime('%d', person.birthdate) AS INTEGER)` |
| `p.LastName.Length` | `LENGTH(person.last_name)` |

---

## Features Not Supported in SQLite

### Stored Procedures

SQLite does not support stored procedures. Calling `ExecuteStoredProcedure` will throw `NotSupportedException`.

### ROWVERSION / Timestamp Columns

SQLite has no equivalent to SQL Server's `ROWVERSION` or PostgreSQL's `xmin`. Optimistic concurrency via timestamp columns is not supported by this provider.

### Full JSON Querying

SQLite supports basic JSON extraction via `json_extract()` for `[JsonPath]` attributes. However, complex JSON operations (array filtering, JSON modification) are limited compared to SQL Server and PostgreSQL.

| Attribute | SQLite Support | Notes |
|:---|:---|:---|
| `[JsonPath]` | ✅ Supported | Uses `json_extract(column, '$.path')` |
| `[SqlExpression]` | ✅ Supported | Use SQLite-compatible SQL syntax |
| `[SubqueryAggregate]` | ✅ Supported | Standard correlated subqueries |
| `[JsonCollection]` | ✅ Supported | Uses `json_group_array(json_object(...))` |

### Concurrent Write Access

SQLite uses file-level locking. While FunkyORM handles connection management correctly, heavy concurrent write workloads may experience `SQLITE_BUSY` errors. For high-concurrency scenarios, consider SQL Server or PostgreSQL.

---

## Migration Tips

### From SQL Server or PostgreSQL to SQLite

1. **Connection string**: Change to `Data Source=filename.db`
2. **Provider class**: Use `SqliteOrmDataProvider` instead of `SqlServerOrmDataProvider` or `PostgreSqlOrmDataProvider`
3. **Entity classes**: No changes needed — they are fully portable
4. **Schema**: Recreate tables using SQLite DDL (no `IDENTITY`, use `INTEGER PRIMARY KEY AUTOINCREMENT`)
5. **Stored procedures**: Replace with application logic or raw SQL execution
6. **ROWVERSION**: Remove or replace with application-managed version tracking

### Common Use Cases for SQLite

- **Desktop/mobile applications** — embedded database, no server required
- **Integration testing** — fast, isolated test databases per test run
- **Prototyping** — zero-config development database
- **Edge/IoT devices** — lightweight, file-based storage
- **Read-heavy workloads** — excellent read performance with minimal overhead
