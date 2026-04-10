# FunkyORM AI Agent Instructions

> **Target Audience**: AI Agents (GitHub Copilot, Cursor, Gemini, GPT, Claude, etc.) assisting developers who use FunkyORM.
>
> **Package**: `Funcular.Data.Orm` (includes SQL Server and PostgreSQL providers)

---

## AI Agent Instructions: READ THIS FIRST

You are assisting a developer using **Funcular.FunkyOrm**. This framework follows strict conventions that differ from Entity Framework or Dapper. **Do not infer usage patterns from other ORMs.**

FunkyORM supports **SQL Server** and **PostgreSQL**. Entity classes and LINQ query code are portable between providers -- only the provider class and connection string change.

---

## CRITICAL: What NOT To Do (The "Heresies")

1.  **NO `ExecuteNonQuery`**: Do not look for or suggest `ExecuteNonQuery`. It does not exist in the public API.
    *   *Correct*: Use `.Insert()`, `.Update()`, `.Delete()`.
    *   *Correct*: If raw SQL is absolutely required (rare), access `.Connection` directly -- but this signals "leaving the safety of the framework."

2.  **NO `DbContext`**: There is no Unit of Work or Context pattern.
    *   *Correct*: Use `SqlServerOrmDataProvider` or `PostgreSqlOrmDataProvider` as a singleton or transient service.

3.  **NO Unnecessary Attributes**: Do not add `[Key]`, `[Table]`, or `[Column]` if the names match conventions.
    *   *Convention*: `Id`, `ClassNameId` = PK. `ClassName` = Table. `PropName` = Column.

4.  **NO `[Remote...]` on Canonical Entities**: Adding remote attributes to base entities forces JOINs on every query.
    *   *Correct*: Use inheritance (`PersonDetail : Person`) for rich data.

5.  **NO `.Value` or `.HasValue` on Nullable Properties in LINQ**: The ORM automatically unwraps nullable types. Using `.Value` or `.HasValue` generates invalid SQL (e.g., column name `HospitalId.Value`).
    *   *Correct*: Use the nullable property directly: `p => p.HospitalId == 5`
    *   *Wrong*: `p => p.HospitalId.Value == 5` or `p => p.HospitalId.HasValue`
    *   *Contains with nullable*: Cast the list to match the nullable type, not the property:
        ```csharp
        var hospitalIds = new List<int> { 1, 2, 3 };
        // Correct: Cast the list to match the nullable property type
        var nullable = hospitalIds.Cast<int?>().ToList();
        provider.Query<Rep>().Where(r => nullable.Contains(r.HospitalId)).ToList();
        // Wrong: Unwrapping the property to match the list
        provider.Query<Rep>().Where(r => hospitalIds.Contains(r.HospitalId.Value)).ToList();
        ```

---

## Provider Selection

Choose the provider that matches your database:

**SQL Server:**
```csharp
using Funcular.Data.Orm.SqlServer;
var provider = new SqlServerOrmDataProvider(connectionString);
```

**PostgreSQL:**
```csharp
using Funcular.Data.Orm.PostgreSql;
var provider = new PostgreSqlOrmDataProvider(connectionString);
```

All subsequent CRUD, Query, and Remote Attribute examples work identically with either provider. The generated SQL differs automatically:

| Feature | SQL Server | PostgreSQL |
|---------|-----------|------------|
| Identifier quoting | `[brackets]` | `"double-quotes"` |
| Insert return | `OUTPUT INSERTED.id` | `RETURNING id` |
| Paging | `OFFSET...FETCH NEXT` | `LIMIT...OFFSET` |
| String concat | `+` | `\|\|` |
| Date parts | `YEAR()`, `MONTH()` | `EXTRACT(YEAR FROM ...)` |
| Boolean type | `BIT` (0/1) | Native `BOOLEAN` |

### PostgreSQL-Specific Caveats

- **Case sensitivity**: PostgreSQL folds unquoted identifiers to lowercase. FunkyORM quotes reserved words automatically (`"User"`, `"Order"`). Non-reserved names remain unquoted and must match the lowercase column name in the database.
- **Npgsql versions**: Npgsql 9.x for `net8.0`; Npgsql 8.x for `netstandard2.0`.
- **Timestamps**: The provider sets `Npgsql.EnableLegacyTimestampBehavior = true` to handle `DateTime` values without requiring `timestamptz` conversions.

---

## Code Generation Requirements

When generating code for the user:

1.  **Explain Attributes**: If you add an attribute, add an XML comment explaining why.
    ```csharp
    /// <summary>Table name 'tbl_Users' deviates from convention.</summary>
    [Table("tbl_Users")]
    public class User { ... }
    ```

2.  **Explain Omissions**: If you rely on convention, add a comment.
    ```csharp
    public int Id { get; set; } // No [Key] needed; 'Id' is automatically detected
    ```

---

## The "Happy Path" Patterns

### 1. Modeling Data (The "Detail" Pattern)

*   **Canonical Entity**: Create one clean class per table. **No remote attributes here.**
    ```csharp
    // Maps to 'Person' table automatically
    public class Person 
    { 
        public int Id { get; set; } // Auto-detected PK
        public string FirstName { get; set; }
        public int? EmployerId { get; set; }
    }
    ```

*   **Rich Data (Inheritance)**: Inherit when you need most/all columns plus related data.
    ```csharp
    public class PersonDetail : Person 
    { 
        [RemoteProperty(typeof(Organization), nameof(EmployerId), nameof(Organization.Name))]
        public string EmployerName { get; set; } 
    }
    ```

*   **Wide Tables (DTOs)**: Use composition/DTOs for column subsets. **Do not inherit.**
    ```csharp
    [Table("Person")] // Required: class name differs from table
    public class PersonSummary 
    { 
        public int Id { get; set; } 
        public string FirstName { get; set; } 
    }
    ```

### 2. Safe Deletes (Transaction Mandate)

*   **Requirement**: ALL deletes (by ID or Predicate) **MUST** be performed within an active transaction.
*   **Requirement**: Deletes by Predicate **MUST** have a non-trivial WHERE clause (no `x => true`).
*   **Anti-Pattern**: Do NOT use raw ADO.NET connections to bypass these checks.

**Delete by ID:**
```csharp
provider.BeginTransaction();
try 
{
    bool deleted = provider.Delete<Person>(5); // Returns true if found and deleted
    provider.CommitTransaction();
}
catch 
{
    provider.RollbackTransaction();
    throw;
}
```

**Delete by Predicate:**
```csharp
provider.BeginTransaction();
try 
{
    // MUST have a safe predicate (e.g., not "x => true")
    int count = provider.Delete<Person>(p => p.IsActive == false && p.LastLogin < DateTime.Now.AddYears(-1));
    provider.CommitTransaction();
}
catch 
{
    provider.RollbackTransaction();
    throw;
}
```

**Async Delete (also requires transaction):**
```csharp
provider.BeginTransaction();
try 
{
    await provider.DeleteAsync<Person>(p => p.Id == 1);
    provider.CommitTransaction();
}
catch 
{
    provider.RollbackTransaction();
    throw;
}
```

### 3. Querying

*   **Chaining**: Use chained `.Where()` clauses (combined with AND).
    ```csharp
    var results = provider.Query<Person>()
        .Where(p => p.Age > 18)
        .Where(p => p.IsActive)
        .ToList();
    ```

*   **IN Clauses**: Use `list.Contains(item.Prop)`.
    ```csharp
    var ids = new[] { 1, 2, 3 };
    var results = provider.Query<Person>().Where(p => ids.Contains(p.Id)).ToList();
    ```

*   **Nullable Properties in Queries**: The ORM automatically unwraps nullables. Do NOT use `.Value` or `.HasValue` -- use the property directly. When using `list.Contains()` with a nullable entity property, cast the list to the nullable type (see Critical Rule #5 above).

*   **LIKE Clauses**: Use `.StartsWith()`, `.EndsWith()`, or `.Contains()` on strings.

*   **Complexity Warning**: Do not put complex in-memory logic (e.g., `x.Name.GetHashCode()`) inside `.Where()`.

*   **Async Support**: Use `GetAsync`, `GetListAsync`, `QueryAsync`, `InsertAsync`, `UpdateAsync`, `DeleteAsync`.

*   **Aggregates**: Chain `Count()`, `Any()`, `Max()` directly off `.Query<T>()` to execute in SQL.
    ```csharp
    // GOOD: Executes COUNT in SQL
    int count = provider.Query<Person>().Where(p => p.IsActive).Count();
    
    // BAD: Loads all rows into memory first
    int count = provider.Query<Person>().Where(p => p.IsActive).ToList().Count();
    ```

*   **Ternary Operators**: Supported in Select/Where/OrderBy (translates to SQL `CASE`).

*   **Deprecation**: `Query<T>(predicate)` is obsolete. Use `Query<T>().Where(predicate)`.

---

## Remote Attributes (The "Superpower")

FunkyORM allows mapping properties on an entity (or DTO) directly to columns in related tables using attributes. This avoids manual JOIN syntax.

### Attribute Reference

| Attribute | Purpose | Signature |
|-----------|---------|-----------|
| `[RemoteKey]` | Maps to the **Primary Key** (`Id`) of a remote entity | `[RemoteKey(typeof(RemoteEntity), params string[] path)]` |
| `[RemoteProperty]` | Maps to a **specific column** of a remote entity | `[RemoteProperty(typeof(RemoteEntity), params string[] path)]` |
| `[RemoteLink]` | Explicitly defines the **target type** of a Foreign Key property | `[RemoteLink(typeof(TargetEntity))]` |

### When to Use `[RemoteLink]`

**REQUIRED** when:
*   The FK property name doesn't match `[EntityName]Id` convention
*   The target entity is in a **different assembly** (e.g., DTO -> Domain Entity)

### Path Resolution Logic

When resolving `[RemoteProperty(typeof(Target), "Prop1", "Prop2", "TargetProp")]`:

1.  **Step 1**: Look at `Prop1` on the Source class.
2.  **Step 2**: Determine the target type of `Prop1`:
    *   **Priority 1**: If `Prop1` has `[RemoteLink(typeof(T))]`, use `T`.
    *   **Priority 2**: If `Prop1` is named `[Name]Id`, look for class `[Name]` in the **same assembly**.
3.  **Step 3**: Repeat for `Prop2` on the type found in Step 2.
4.  **Validation**: Ensure the final type matches `typeof(Target)`.

**Rule of Thumb**: If your relationship crosses assembly boundaries, ALWAYS use `[RemoteLink]`.

### Example: Multi-Hop Path

```csharp
public class PersonDetail : Person
{
    // Path: Person -> Organization (via EmployerId) -> Address -> Country
    [RemoteLink(typeof(Organization))] // Required if EmployerId doesn't auto-resolve
    public int? EmployerId { get; set; }
    
    [RemoteProperty(typeof(Country), 
        nameof(EmployerId), 
        nameof(Organization.HeadquartersAddressId), 
        nameof(Address.CountryId), 
        nameof(Country.Name))]
    public string EmployerCountryName { get; set; }
}
```

### Explicit Collection Population

Use `[RemoteKey]` on the child to point back to the parent, then query the child:

```csharp
// Child Detail class
public class CountryDetail : Country 
{
    [RemoteKey(typeof(Person), nameof(Person.Id))] 
    public int PersonId { get; set; }
}

// Usage: Populate person's countries
var countries = provider.Query<CountryDetail>()
    .Where(c => c.PersonId == person.Id)
    .ToList();
person.Countries.AddRange(countries);
```

### Rich Many-to-Many

Map the Join Table as an entity to access its columns (e.g., `IsPrimary`, `DateAdded`) plus remote data.

---

## Common Errors & Fixes

### PathNotFoundException (Type Mismatch)

**Error**: `Explicit path ended at Department, expected Department`

**Cause**: The resolver found a type with the same **name** but different **assembly/namespace** (e.g., `MyApp.Api.Department` vs `MyApp.Domain.Department`).

**This typically happens when**:
1.  Using `RemoteKey` on a DTO that references another DTO, but the attribute specifies the Domain Entity.
2.  The resolver defaults to the **same assembly** and finds the wrong type.

**Fix**: Add `[RemoteLink]` to the FK property to explicitly specify the target type.

#### Incorrect (Causes Error)
```csharp
// In Assembly: MyApp.Api (DTOs)
public class EmployeeDto
{
    // Resolver guesses this points to MyApp.Api.Department (wrong!)
    public int DepartmentId { get; set; } 

    // ERROR: Path ends at MyApp.Api.Department, expected MyApp.Domain.Department
    [RemoteProperty(typeof(MyApp.Domain.Department), "DepartmentId", "Name")]
    public string DepartmentName { get; set; }
}
```

#### Correct (Fixed)
```csharp
// In Assembly: MyApp.Api (DTOs)
using MyApp.Domain;

public class EmployeeDto
{
    [RemoteLink(typeof(Department))] // Explicitly specify Domain entity
    public int DepartmentId { get; set; } 

    [RemoteProperty(typeof(Department), "DepartmentId", "Name")]
    public string DepartmentName { get; set; }
}
```

### AmbiguousMatchException

**Cause**: Implicit resolution found multiple valid paths to the target (e.g., `BillingAddress` and `ShippingAddress` both lead to `Country`).

**Fix**: Switch to **Explicit Mode** by providing the full property path.

```csharp
// Ambiguous: Could go via BillingAddress or ShippingAddress
[RemoteProperty(typeof(Country), "Name")] 

// Fixed: Explicit path
[RemoteProperty(typeof(Country), "BillingAddressId", "CountryId", "Name")]
```

---

## Naming Conventions

### Automatic Mapping

| Type | Convention | Examples |
|------|------------|----------|
| **Tables** | Class name (case-insensitive) | `Person` -> `Person`, `person`, `PERSON` |
| **Columns** | Property name (case-insensitive, snake_case supported) | `FirstName` -> `FirstName`, `first_name`, `FIRST_NAME` |
| **Primary Keys** | `Id`, `{ClassName}Id`, `{ClassName}_Id` | `Id`, `PersonId`, `Person_Id` |

### The "Duplicate Class Name" Problem

FunkyORM's inference engine relies on class names. **NEVER** reuse class names across namespaces for ORM-queried types.

| Bad | Good |
|-----|------|
| `Domain.Department` AND `Api.Department` | `Domain.Department` (Canonical) |
| | `Api.DepartmentDto` |
| | `Api.DepartmentViewModel` |
| | `Api.DepartmentDetail` |
