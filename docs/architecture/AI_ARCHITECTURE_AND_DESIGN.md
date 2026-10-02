# Funcular.FunkyOrm Architecture & Design Philosophy

> **Target Audience**: Internal Developers & AI Agents maintaining this repository.

## The Core Mission: "The Manager's Best Friend"

The highest value of this framework is to be a tool that allows **managers to onboard less-experienced developers quickly**.

We want junior developers to become productive immediately with a **very low likelihood** of:
1.  **Causing Damage**: Accidental mass deletes, SQL injection, or schema corruption.
2.  **Creating Bottlenecks**: N+1 queries, massive memory loading, or locking issues.

**The Strategy**: Make the "Happy Path" (the safe, high-performance path) the one they choose naturally out of laziness.

## The "Heresy" of Raw SQL

In many ORMs, dropping down to raw SQL (e.g., `ExecuteNonQuery`) is considered a "power user" feature. **In FunkyORM, it is considered failure.**

*   **ExecuteNonQuery is Heresy**: We have deliberately removed/hidden `ExecuteNonQuery` from the public API.
*   **Why?** It bypasses our safety checks (transaction requirements, WHERE clause analysis).
*   **Agent Instruction**: NEVER re-introduce public raw SQL execution methods. If a developer needs raw SQL, they must explicitly access the underlying `Connection` property, signaling "I am leaving the safety of the framework."

## Delete Safety Philosophy

We believe that deleting data is the most dangerous operation a developer can perform. Therefore, we have intentionally added friction to the process.

### 1. The Transaction Mandate
*   **Rule**: All `Delete` operations (by ID or Predicate) throw an exception if no transaction is active.
*   **Reason**: This forces the developer to explicitly opt-in to the destruction of data. It prevents "accidental" one-line deletes in the console or immediate window.
*   **Agent Instruction**: Always wrap generated delete code in `BeginTransaction()` / `CommitTransaction()` blocks.

### 2. The Predicate Guard
*   **Rule**: `Delete(predicate)` analyzes the expression tree before translating it (`DeletePredicateGuard`) and rejects a predicate that reads no column (`x => true`), compares a column with itself (`x.Id == x.Id`), or is always true through literals or captured values (`x.Id == 2 || true`). It then rejects a WHERE clause that is true by its literals (`1=1`, or `OR NOT 1=0` from a negated `Contains` over an empty list). It doesn't catch every predicate that is true for every row.
*   **Reason**: To prevent accidental table truncation.
*   **Agent Instruction**: Do not try to bypass this with raw SQL. If a user asks to "delete all", explain the safety mechanism and suggest they use a raw SQL command on the `Connection` object if they truly mean it.

## Modeling Philosophy

### 1. Canonical Entities are Sacred
A "Canonical Entity" (e.g., `Person`) represents the table structure exactly.
*   **Rule**: Do NOT add `[Remote...]` attributes to Canonical Entities.
*   **Reason**: If a junior dev queries `provider.GetList<Person>()`, and `Person` has 5 remote properties, they just triggered a massive JOIN query without realizing it.
*   **Pattern**: Keep Canonical Entities pure. Use inheritance (`PersonDetail`) for rich graphs.

### 2. Inheritance vs. Composition (The "Wide Table" Rule)
*   **Scenario**: A table has 50 columns. The user needs a dropdown of `Id` and `Name`.
*   **Anti-Pattern**: Inheriting from the Entity (`public class Dropdown : Person`). This risks accidental usage of the full entity logic.
*   **Pro-Pattern**: Composition/DTO (`[Table("Person")] public class PersonDropdown { Id, Name }`).
*   **Agent Instruction**: When creating DTOs for wide tables, prefer **duplication with attributes** over inheritance.

### 3. Advanced Relationship Patterns
*   **Explicit Collection Population**: We prefer explicit queries over lazy loading.
    *   **Pattern**: Child DTO has a `[RemoteKey]` pointing back to Parent. Query Child where `RemoteKey == ParentId`.
*   **Rich Many-to-Many**: We prefer mapping the Join Table as a first-class entity.
    *   **Reason**: Allows access to link table columns (e.g., `IsPrimary`, `DateAdded`) which are lost in standard "skip-the-middleman" M:N mappings.

### 4. Remote Attributes & Path Resolution
*   **Purpose**: To flatten object graphs and prevent N+1 queries by generating efficient `LEFT JOIN`s.
*   **Mechanism**: The `RemotePathResolver` uses Breadth-First Search (BFS) to find the shortest path between entities.
*   **Ambiguity**: If multiple paths exist, the resolver throws `AmbiguousMatchException`. Agents must then specify the full path using `nameof()` chains.
*   **Cross-Assembly**: The resolver defaults to the same assembly. Use `[RemoteLink]` to bridge assembly boundaries (e.g., DTO -> Domain Entity).

### 5. JSON & Computed Column Attribute Philosophy

FunkyORM provides a family of attributes (v3.2+) designed to eliminate SQL views by expressing JSON extraction, computed expressions, aggregate subqueries, and JSON projections declaratively in C#:

| Attribute | Phase | Status | SQL Technique | Adds JOINs? |
|:---|:---|:---|:---|:---|
| `[JsonPath]` | 1 | ? Implemented | `JSON_VALUE` / `#>>` | No |
| `[SqlExpression]` | 2 | ? Implemented | Raw expression with `{PropertyName}` tokens | No (may reference joined columns) |
| `[SubqueryAggregate]` | 3 | ? Implemented | Correlated scalar subquery in SELECT | No |
| `[JsonCollection]` | 4 | ? Implemented | `FOR JSON PATH` / `json_agg` subquery | No |

**Design principles common to all four:**

*   **Same-table or subquery**: Unlike `[RemoteProperty]` which generates `LEFT JOIN`s to other tables, these attributes either operate on a column of the same table (`[JsonPath]`, `[SqlExpression]`) or generate correlated subqueries (`[SubqueryAggregate]`, `[JsonCollection]`). They never add JOINs themselves.
*   **Detail Pattern Rule**: Like all enrichment attributes, these must be placed on Detail classes, never on Canonical Entities. A `[JsonPath]` on a Canonical Entity forces JSON extraction on every query of that type — same risk as `[RemoteProperty]` on a canonical entity.
*   **`ISqlDialect` portability**: Each attribute's SQL generation is routed through `ISqlDialect` methods, ensuring MSSQL and PostgreSQL produce correct, idiomatic SQL from the same C# code.
*   **`ResolveRemoteJoins<T>` integration**: All attributes are resolved in `ResolveRemoteJoins<T>` alongside remote properties. Extracted expressions are added to `ExtraColumns` (for SELECT) and `PropertyToColumnMap` (for WHERE clause resolution).
*   **Composability**: All attribute types work together on a single Detail class. A `ProjectScorecard` can combine `[RemoteProperty]` (JOINs), `[JsonPath]` (JSON extraction), `[SqlExpression]` (computed columns), `[SubqueryAggregate]` (counts/sums), and `[JsonCollection]` (child projections) to replace an entire SQL view.

**`[JsonPath]` specifics (Phase 1 — implemented):**
*   Generates `JSON_VALUE(col, '$.path')` (SQL Server) or `col #>> '{path}'` (PostgreSQL) via `ISqlDialect.BuildJsonValueExpression()`.
*   Optional `SqlType` wraps the expression in `CAST()` / `::type` for typed comparisons.
*   Agent Instruction: When a user asks to "query a JSON column" or "extract from JSON," use `[JsonPath]` on a Detail class. Do NOT suggest raw `JSON_VALUE` SQL or create SQL views.

**`[SqlExpression]` specifics (Phase 2 — implemented):**
*   Uses `{PropertyName}` tokens resolved to qualified column names at query time. Text outside braces is emitted verbatim.
*   Supports dual-expression constructor (`mssql:` / `postgresql:`) for provider-specific SQL syntax.
*   Agent Instruction: Always use `{PropertyName}` for column references; never hard-code column names. Use `nameof()` in documentation examples.

**`[SubqueryAggregate]` specifics (Phase 3 — implemented):**
*   Generates correlated scalar subqueries in the SELECT list. Portable across MSSQL and PostgreSQL.
*   Supports conditional aggregates via `conditionColumn` / `conditionValue` for `SUM(CASE WHEN ...)` patterns.
*   Agent Instruction: Prefer convention-based equality conditions; use raw SQL `condition:` param only when the condition involves JOINs within the subquery.

**`[JsonCollection]` specifics (Phase 4 — implemented):**
*   Generates `FOR JSON PATH` (MSSQL) or `json_agg(row_to_json(...))` (PostgreSQL) subqueries.
*   This is the most complex attribute — involves correlated subquery, optional JOINs within the subquery, column selection, and provider-specific JSON serialization.
*   Agent Instruction: This is for projecting child *records* as JSON arrays. For extracting *scalars* from an existing JSON column, use `[JsonPath]` instead.

### 6. Connection Management
*   **Pattern**: One instance per connection string.
*   **Lifecycle**: Singleton or Transient. No `DbContext` state tracking.
*   **Philosophy**: Lightweight, stateless, fast.

### 6. Reserved Word Strategy
*   **Strategy**: We automatically detect and bracket reserved words (e.g., `[User]`, `[Order]`) in the SQL generation layer.
*   **Agent Instruction**: Do not manually escape table or column names in code or documentation unless writing raw SQL (which you shouldn't be doing).

### 7. Nullable Property Handling
*   **Behavior**: The ORM automatically unwraps nullable types during SQL translation. It treats `int?` the same as `int` in generated SQL.
*   **Rule**: Do NOT use `.Value` or `.HasValue` on nullable properties in LINQ expressions. They are translated literally as SQL column names (e.g., `HospitalId.Value`), producing invalid SQL.
*   **Rule**: When using `List<T>.Contains()` with a nullable entity property, cast the list to `List<T?>` rather than unwrapping the property with `.Value`.
*   **Agent Instruction**: Always use nullable properties directly in predicates (e.g., `p => p.HospitalId == 5`, not `p => p.HospitalId.Value == 5`).

## Code Generation Rules for Agents

1.  **Comments are Mandatory**: When applying attributes, you must explain *why*.
    *   `[Table("Users")] // Legacy schema name`
2.  **Comments for Omission**: When skipping attributes, explain *why*.
    *   `public int Id { get; set; } // Convention: Auto-detected PK`
3.  **No Magic Strings**: Use `nameof()` for all remote paths.
