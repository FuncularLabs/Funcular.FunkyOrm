# FunkyORM AI Agent Instructions (MySQL)

> **Target Audience**: AI Agents (GitHub Copilot, Cursor, Gemini, GPT, Claude, etc.) assisting developers who use the MySQL provider in the `Funcular.Data.Orm` package.
>
> **Package**: `Funcular.Data.Orm` | **Namespace**: `Funcular.Data.Orm.MySql`
>
> This file supplements the shared [FUNKYORM_AI_INSTRUCTIONS.md](FUNKYORM_AI_INSTRUCTIONS.md) with MySQL-specific guidance. All rules in the shared instructions apply here. **Read the shared instructions first.**

---

## Provider Initialization

```csharp
using Funcular.Data.Orm.MySql;

var connectionString = "Server=localhost;Port=3306;Database=mydb;User ID=myuser;Password=mypassword;GuidFormat=Char36";
var provider = new MySqlOrmDataProvider(connectionString)
{
    Log = s => Console.WriteLine(s) // Optional: see generated SQL
};
```

The provider is built on the MIT-licensed **MySqlConnector** ADO.NET driver (not Oracle's GPL `MySql.Data`).

### Recommended Connection-String Options

| Option | Why |
|:---|:---|
| `GuidFormat=Char36` | Stores/reads `Guid` values as `CHAR(36)` so `Guid` properties round-trip transparently. **Recommended whenever entities use `Guid` keys/columns.** |
| `AllowUserVariables=true` | Enables `@variable` usage; harmless and recommended for compatibility. |

`Server` may be `localhost` or an IP; default `Port` is `3306`.

---

## MySQL-Specific Behavior

### Identifier Quoting

MySQL uses **backticks** (`` `name` ``) for identifiers. FunkyORM handles this automatically:

- **Reserved words** (`order`, `select`, `key`, `user`, etc.) are automatically quoted: `` `order` ``, `` `user` ``
- **Non-reserved identifiers** are left unquoted
- You do NOT need to change your entity classes or `[Table]`/`[Column]` attributes when switching from SQL Server, PostgreSQL, or SQLite

> Do **not** rely on ANSI double-quotes for identifiers — by default MySQL treats `"..."` as a string literal.

### Type Mapping

MySQL has rich native types, so little conversion is required:

| CLR Type | MySQL Storage | Notes |
|:---|:---|:---|
| `int`, `long` | `INT`, `BIGINT` | Native |
| `bool` | `TINYINT(1)` | Mapped automatically (`BOOL`/`BOOLEAN` are aliases) |
| `DateTime` | `DATETIME(6)` | Native; microsecond precision |
| `Guid` | `CHAR(36)` | Use `GuidFormat=Char36` in the connection string |
| `decimal` | `DECIMAL(p,s)` | Exact — no precision loss |
| `double`, `float` | `DOUBLE`, `FLOAT` | Native |
| `string` | `VARCHAR(n)`, `TEXT` | Use charset `utf8mb4` |
| `byte[]` | `VARBINARY`, `BLOB` | Native |

### Paging

MySQL uses `LIMIT...OFFSET`, same as PostgreSQL and SQLite:

```csharp
var page = provider.Query<Person>()
    .OrderBy(p => p.Id)
    .Skip(10).Take(10)
    .ToList();
```

**MySQL SQL:** `SELECT ... FROM person ORDER BY id LIMIT 10 OFFSET 10`

> MySQL requires a `LIMIT` whenever `OFFSET` is present. When you call `.Skip(n)` without `.Take(...)`, FunkyORM emits the canonical `LIMIT 18446744073709551615 OFFSET n` ("offset to end") idiom for you.

### Insert Return Values

MySQL has **no `RETURNING` clause**. For `AUTO_INCREMENT` identity columns, the provider reads the generated id via `MySqlCommand.LastInsertedId` after the insert. For non-identity primary keys (`Guid`, `string`), the caller supplies the value.

```csharp
var id = provider.Insert<Person, int>(person); // Works on all providers
```

### Boolean Columns

MySQL stores booleans as `TINYINT(1)`. FunkyORM (via MySqlConnector's `TreatTinyAsBoolean`) maps C# `bool` properties correctly:

```csharp
public class Feature
{
    public bool IsEnabled { get; set; }  // Maps to TINYINT(1) 0/1
}
```

### String Concatenation

In MySQL the `+` operator performs **numeric addition**, so string building translates to `CONCAT()`. This matters for `[SqlExpression]`:

```csharp
// Portable single-expression form (CONCAT works on SQL Server and MySQL):
[SqlExpression("CONCAT({FirstName}, ' ', {LastName})")]
public string FullName { get; set; }
```

### String Comparison

MySQL's default collations (e.g., `utf8mb4_0900_ai_ci`) are **case-insensitive**, matching SQL Server's default behavior — so no case-folding workaround is emitted. `Contains`/`StartsWith`/`EndsWith` use `LIKE` with `CONCAT()` patterns.

If you need case-sensitive comparisons, construct the provider with `MySqlStringComparison.CaseSensitive` (applies `COLLATE utf8mb4_bin`):

```csharp
var provider = new MySqlOrmDataProvider(connectionString, MySqlStringComparison.CaseSensitive);
```

### Identifier Case Sensitivity

- **Column names** are always case-insensitive in MySQL.
- **Table names** follow the server's `lower_case_table_names` setting (case-sensitive on Linux by default, case-insensitive on Windows/macOS). For portable schemas, **use lowercase table names**.

### Date/Time Handling

`DateTime` maps to `DATETIME(6)`. Date-part extraction uses `EXTRACT()` (same as PostgreSQL):

| C# Expression | MySQL SQL |
|:---|:---|
| `p.Birthdate.Value.Year` | `EXTRACT(YEAR FROM birthdate)` |
| `p.Birthdate.Value.Month` | `EXTRACT(MONTH FROM birthdate)` |
| `p.Birthdate.Value.Day` | `EXTRACT(DAY FROM birthdate)` |

---

## View-Replacing Attributes (Fully Supported)

MySQL has a native `JSON` type, so all four attributes are supported with full parity:

| Attribute | MySQL Support | Generated SQL |
|:---|:---|:---|
| `[JsonPath]` | ✅ Supported | `JSON_UNQUOTE(JSON_EXTRACT(column, '$.path'))` (with `CAST(... AS SIGNED/DECIMAL/CHAR)` for typed extraction) |
| `[SqlExpression]` | ✅ Supported | Use MySQL-compatible SQL (`CONCAT`, `COALESCE`, `CASE`) |
| `[SubqueryAggregate]` | ✅ Supported | Correlated `(SELECT COUNT/SUM/AVG ... )` subqueries |
| `[JsonCollection]` | ✅ Supported | `JSON_ARRAYAGG(JSON_OBJECT(...))` |

`[JsonPath]`-mapped properties also work in `WHERE` predicates, including method-call predicates (`Contains`, `StartsWith`, `EndsWith`, and collection `IN`).

```csharp
[Table("project")]
public class ProjectScorecard
{
    public int Id { get; set; }

    [JsonPath("metadata", "$.risk_level")]
    public string RiskLevel { get; set; }            // SELECT + WHERE supported

    [SubqueryAggregate(typeof(ProjectMilestone), nameof(ProjectMilestone.ProjectId),
        AggregateFunction.ConditionalCount,
        ConditionColumn = nameof(ProjectMilestone.Status), ConditionValue = "completed")]
    public int CompletedMilestones { get; set; }
}
```

> **Tip:** Child entities referenced only by `[SubqueryAggregate]`/`[JsonCollection]` are not column-discovered, and MySQL column names are case-insensitive but **not** underscore-insensitive. Put explicit `[Column("snake_case")]` attributes on those child entities' columns (e.g., `[Column("project_id")]`).

---

## Error Handling

The provider maps `MySqlConnector.MySqlException.Number` to friendly errors, e.g.:

| Number | Meaning |
|:---|:---|
| 1146 | Table doesn't exist |
| 1062 | Duplicate entry (unique constraint) |
| 1452 / 1451 | Foreign-key constraint failure (child / parent) |
| 1054 | Unknown column |

---

## Notes & Limitations

- **Stored procedures**: MySQL supports them, but FunkyORM does not currently expose stored-procedure execution (consistent with the other providers).
- **`DateTimeOffset`**: MySQL has no timezone-aware type; values are persisted as UTC `DATETIME(6)` and the offset is not preserved.
- **Transaction isolation**: InnoDB defaults to `REPEATABLE READ` (SQL Server defaults to `READ COMMITTED`). Do not rely on cross-transaction visibility semantics in portable code.
- **Schemas**: `[Table(Schema = "x")]` maps to `` `x`.`table` `` — in MySQL a "schema" is a database.

---

## Migration Tips

### From SQL Server / PostgreSQL / SQLite to MySQL

1. **Connection string**: `Server=...;Port=3306;Database=...;User ID=...;Password=...;GuidFormat=Char36`
2. **Provider class**: Use `MySqlOrmDataProvider`
3. **Entity classes**: No changes needed — they are fully portable
4. **Schema**: Recreate tables using MySQL DDL — `INT AUTO_INCREMENT PRIMARY KEY` for identity, `CHAR(36)` for `Guid`, native `JSON` for JSON columns, `utf8mb4` charset
5. **`[SqlExpression]`**: Ensure expressions use `CONCAT()` rather than `+` for string building

### Common Use Cases for MySQL

- **Web applications** — the most common LAMP/cloud relational backend
- **Cross-platform server workloads** — Linux-first deployments
- **Existing MySQL/MariaDB estates** — drop-in provider with portable entities
