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
*   **Rule**: `Delete(predicate)` analyzes the expression tree and blocks trivial clauses (`1=1`, `true`, `x.Id == x.Id`).
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

### 5. JSON Column Querying Philosophy
*   **Purpose**: To query into semi-structured JSON columns without creating SQL views or raw SQL.
*   **Mechanism**: The `[JsonPath]` attribute generates `JSON_VALUE` (SQL Server) or `#>>` (PostgreSQL) expressions via `ISqlDialect.BuildJsonValueExpression()`.
*   **Architecture**: JSON extraction is resolved in `ResolveRemoteJoins<T>` alongside remote properties. Extracted expressions are added to `ExtraColumns` and `PropertyToColumnMap` so they work in both SELECT and WHERE clauses.
*   **Typing**: By default, JSON extraction returns strings. The optional `SqlType` parameter wraps the expression in `CAST()` (SQL Server) or `::type` (PostgreSQL) for typed comparisons.
*   **Same-Table Only**: Unlike `[RemoteProperty]` which JOINs to other tables, `[JsonPath]` operates on a column of the *same* table. It adds no JOINs.
*   **Detail Pattern Rule**: Like all enrichment attributes, `[JsonPath]` must be placed on Detail classes, never on Canonical Entities. A `[JsonPath]` on a Canonical Entity forces JSON extraction on every query of that type.
*   **Agent Instruction**: When a user asks to "query a JSON column" or "extract from JSON," use `[JsonPath]` on a Detail class. Do NOT suggest raw `JSON_VALUE` SQL or create SQL views.

### 5. Connection Management
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
