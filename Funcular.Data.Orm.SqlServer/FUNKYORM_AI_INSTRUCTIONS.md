# FunkyORM AI Agent Instructions

> **Target Audience**: AI Agents (GitHub Copilot, Cursor, Gemini, GPT, Claude, etc.) assisting developers who use FunkyORM.
>
> **Package**: `Funcular.Data.Orm` (includes SQL Server and PostgreSQL providers)

----

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

## JSON & Computed Column Attributes (v3.2+)

FunkyORM provides a family of attributes that eliminate the need for SQL views in the most common scenarios: JSON extraction, computed expressions, subquery aggregates, and JSON collection projections. All four are implemented as of v3.2.0-beta2.

### Attribute Taxonomy

```
Attribute
├── RemoteAttributeBase
│   ├── RemoteKeyAttribute          (existing — JOINs)
│   └── RemotePropertyAttribute     (existing — JOINs)
├── JsonPathAttribute               (Phase 1 — ✅ Implemented)
├── SqlExpressionAttribute          (Phase 2 — ✅ Implemented)
├── SubqueryAggregateAttribute      (Phase 3 — ✅ Implemented)
└── JsonCollectionAttribute         (Phase 4 — ✅ Implemented)
```

### Attribute Reference

| Attribute | Purpose | Phase | Status | Adds JOINs? |
|:---|:---|:---|:---|:---|
| `[JsonPath]` | Extract a scalar from a JSON column on the **same** table | 1 | ? Implemented | No |
| `[SqlExpression]` | Declare a raw SQL expression (COALESCE, CONCAT, CASE) | 2 | ? Implemented | No (but may reference joined columns) |
| `[SubqueryAggregate]` | Correlated scalar subquery (COUNT, SUM, conditional) | 3 | ? Implemented | No (generates subquery in SELECT) |
| `[JsonCollection]` | Project child records as a JSON array (FOR JSON PATH) | 4 | ? Implemented | No (generates subquery in SELECT) |

All four follow the same "Detail class" pattern — they belong on inherited Detail classes, never on canonical entities.

---

### Phase 1: `[JsonPath]` — JSON Scalar Extraction ?

**Status: Implemented in v3.2.0-beta1.**

Extracts a single value from a JSON column on the same table using a JSON path expression.

#### Signature

```csharp
[JsonPath("columnName", "$.json.path")]
[JsonPath("columnName", "$.json.path", SqlType = "sqlType")]
```

#### Parameters

| Parameter | Required | Description | Example |
|:---|:---|:---|:---|
| `columnName` | Yes | The JSON column name on the entity's table | `"metadata"` |
| `path` | Yes | JSON path expression using `$.` dot notation | `"$.client.name"`, `"$.risk_level"` |
| `SqlType` | No | SQL type to CAST the extracted value to | `"int"`, `"decimal(10,2)"`, `"bit"` |

#### Generated SQL

| Provider | Without Cast | With `SqlType = "int"` |
|:---|:---|:---|
| **SQL Server** | `JSON_VALUE(project.metadata, '$.client.name')` | `CAST(JSON_VALUE(project.metadata, '$.risk_level') AS int)` |
| **PostgreSQL** | `project.metadata #>> '{client,name}'` | `(project.metadata #>> '{risk_level}')::int` |

#### Complete Example

Given a `project` table with a `metadata` column containing JSON like:

```json
{
  "priority": "high",
  "tags": ["api", "backend"],
  "client": {
    "name": "Acme Corp",
    "region": "NA"
  },
  "risk_level": 3
}
```

**Canonical entity (no JSON attributes):**

```csharp
[Table("project")]
public class ProjectEntity
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int OrganizationId { get; set; }
    public int? LeadId { get; set; }
    public int? CategoryId { get; set; }
    public decimal? Budget { get; set; }
    public int? Score { get; set; }
    public string Metadata { get; set; }  // JSON column
    public DateTime DateUtcCreated { get; set; }
    public DateTime DateUtcModified { get; set; }
}
```

**Detail class with `[JsonPath]`:**

```csharp
[Table("project")]
public class ProjectScorecard : ProjectEntity
{
    /// <summary>Extract priority from the project metadata JSON column.</summary>
    [JsonPath("metadata", "$.priority")]
    public string Priority { get; set; }

    /// <summary>Extract client name from nested JSON object.</summary>
    [JsonPath("metadata", "$.client.name")]
    public string ClientName { get; set; }

    /// <summary>Extract client region from nested JSON object.</summary>
    [JsonPath("metadata", "$.client.region")]
    public string ClientRegion { get; set; }

    /// <summary>Extract risk level as an integer from JSON.</summary>
    [JsonPath("metadata", "$.risk_level", SqlType = "int")]
    public int? RiskLevel { get; set; }
}
```

**Querying and filtering (works in Get, Query, GetList, and WHERE clauses):**

```csharp
// Get by ID — JSON values extracted automatically
var project = provider.Get<ProjectScorecard>(42);
// project.Priority    ? "high"
// project.ClientName  ? "Acme Corp"
// project.RiskLevel   ? 3

// Filter on JSON values using standard LINQ
var highPriority = provider.Query<ProjectScorecard>()
    .Where(p => p.Priority == "high")
    .ToList();

// Combine JSON filters with regular column filters
var riskyExpensive = provider.Query<ProjectScorecard>()
    .Where(p => p.RiskLevel >= 3)
    .Where(p => p.Budget > 100000)
    .ToList();

// Filter on nested JSON values
var emeaProjects = provider.Query<ProjectScorecard>()
    .Where(p => p.ClientRegion == "EMEA")
    .ToList();
```

**Combining `[JsonPath]` with `[RemoteProperty]` on the same Detail class:**

```csharp
[Table("project")]
public class ProjectScorecard : ProjectEntity
{
    // ── Remote properties: JOIN to related tables ──
    [RemoteProperty(typeof(OrganizationEntity), nameof(OrganizationId), nameof(OrganizationEntity.Name))]
    public string OrganizationName { get; set; }

    [RemoteProperty(typeof(ProjectCategoryEntity), nameof(CategoryId), nameof(ProjectCategoryEntity.Name))]
    public string CategoryName { get; set; }

    // ── JSON path: extract from metadata column on same table ──
    [JsonPath("metadata", "$.priority")]
    public string Priority { get; set; }

    [JsonPath("metadata", "$.client.name")]
    public string ClientName { get; set; }

    [JsonPath("metadata", "$.risk_level", SqlType = "int")]
    public int? RiskLevel { get; set; }
}

// Both work simultaneously in queries:
var results = provider.Query<ProjectScorecard>()
    .Where(p => p.OrganizationName == "Acme Corp")  // Filter on joined column
    .Where(p => p.Priority == "high")                // Filter on JSON value
    .ToList();
```

**Null metadata handling:**

```csharp
// When metadata column is NULL, all [JsonPath] properties resolve to null
var project = provider.Get<ProjectScorecard>(99); // metadata is NULL
// project.Priority   ? null
// project.RiskLevel  ? null (int? ? null, not 0)
```

---

### Phase 2: `[SqlExpression]` — Computed/Expression Columns ?

**Status: Implemented in v3.2.0-beta2.**

Declares a raw SQL expression for a property, enabling `COALESCE`, `CONCAT`, `CASE`, and any other SQL expression. Uses `{PropertyName}` tokens that the framework resolves to fully qualified column references at query time.

#### Signature

```csharp
// Single expression (dialect-aware function translation)
[SqlExpression("COALESCE({ComputedScore}, {Score})")]

// Provider-specific overrides (when SQL syntax differs)
[SqlExpression(
    mssql: "CONCAT({FirstName}, CASE WHEN {LastName} IS NOT NULL THEN ' ' + {LastName} ELSE '' END)",
    postgresql: "{FirstName} || COALESCE(' ' || {LastName}, '')")]
```

#### Parameters

| Parameter | Required | Description | Example |
|:---|:---|:---|:---|
| `expression` | Yes (single-provider) | SQL expression with `{PropertyName}` tokens | `"COALESCE({ComputedScore}, {Score})"` |
| `mssql` | Yes (multi-provider) | SQL Server-specific expression | `"CONCAT({FirstName}, ' ', {LastName})"` |
| `postgresql` | Yes (multi-provider) | PostgreSQL-specific expression | `"{FirstName} \|\| ' ' \|\| {LastName}"` |

#### `{Braces}` Token Resolution

Curly braces denote **C# property name tokens** that the framework resolves into fully qualified SQL column references:

| Token | What the Framework Does |
|:---|:---|
| `{Score}` | Looks up `Score` property ? maps to column `score` ? emits `[project].[score]` (MSSQL) or `project.score` (Postgres), including table alias. |
| `{ComputedScore}` | Same lookup; if the property is backed by another attribute (e.g., `[SubqueryAggregate]`), its resolved SQL expression is substituted inline. |

**Why braces exist:** Developers write PascalCase property names; the framework converts them to the correct `snake_case` column names, applies `[Column]` overrides, and adds table aliases. This is the same pipeline that powers `[RemoteProperty]` and `WhereClauseVisitor`.

**Can braces be omitted?** Technically yes — text outside braces is emitted verbatim. `COALESCE(computed_score, score)` would work if the developer gets the exact column name and quoting right. However, this is **strongly discouraged** because:
1. It bypasses naming-convention resolution (PascalCase ? snake_case, `[Column]` overrides).
2. It breaks when JOINs add table aliases (bare column names become ambiguous).
3. It's not portable across providers (`[score]` fails on PostgreSQL; `"score"` fails on MSSQL).

**Rule of thumb:** Use `{PropertyName}` for column references. Use bare SQL only for literals, operators, and functions.

#### Examples

```csharp
[Table("project")]
public class ProjectScorecard : ProjectEntity
{
    /// <summary>Falls back to stored score when computed milestone score is null.</summary>
    [SqlExpression("COALESCE({ComputedScore}, {Score})")]
    public int? EffectiveScore { get; set; }

    /// <summary>Lead person full name via expression across joined person columns.</summary>
    [SqlExpression(
        mssql: "CONCAT({LeadFirstName}, CASE WHEN {LeadLastName} IS NOT NULL THEN ' ' + {LeadLastName} ELSE '' END)",
        postgresql: "{LeadFirstName} || COALESCE(' ' || {LeadLastName}, '')")]
    public string LeadName { get; set; }
}
```

#### Generated SQL

**SQL Server:**
```sql
SELECT project.*,
       COALESCE([project].[computed_score], [project].[score]) AS [EffectiveScore],
       CONCAT([lead_0].[first_name], CASE WHEN [lead_0].[last_name] IS NOT NULL
              THEN ' ' + [lead_0].[last_name] ELSE '' END) AS [LeadName]
FROM project
LEFT JOIN person [lead_0] ON project.lead_id = [lead_0].id
```

---

### Phase 3: `[SubqueryAggregate]` — Correlated Scalar Subqueries ?

**Status: Implemented in v3.2.0-beta2.**

Replaces `OUTER APPLY` / correlated subqueries with an attribute-driven approach. Generates a correlated scalar subquery in the SELECT list.

#### Signature

```csharp
[SubqueryAggregate(
    sourceType: typeof(ChildEntity),
    foreignKey: nameof(ChildEntity.ParentId),
    function: AggregateFunction.Count)]

// Conditional aggregate:
[SubqueryAggregate(
    sourceType: typeof(ChildEntity),
    foreignKey: nameof(ChildEntity.ParentId),
    function: AggregateFunction.ConditionalCount,
    conditionColumn: nameof(ChildEntity.Status),
    conditionValue: "overdue")]
```

#### Parameters

| Parameter | Required | Description | Example |
|:---|:---|:---|:---|
| `sourceType` | Yes | The child entity type to aggregate over | `typeof(ProjectMilestoneEntity)` |
| `foreignKey` | Yes | Property name on the child that references the parent PK | `nameof(ProjectMilestoneEntity.ProjectId)` |
| `function` | Yes | Aggregate function to apply | `AggregateFunction.Count`, `.Sum`, `.Avg`, `.ConditionalCount` |
| `conditionColumn` | No | Column to filter on (for conditional aggregates) | `nameof(ProjectMilestoneEntity.Status)` |
| `conditionValue` | No | Value to match in the condition | `"overdue"`, `"completed"` |

#### `AggregateFunction` Enum

```csharp
public enum AggregateFunction
{
    Count,              // COUNT(*)
    Sum,                // SUM(column)
    Avg,                // AVG(column)
    ConditionalCount    // COUNT(*) with WHERE condition
}
```

#### Examples

Given `project_milestone` child rows for a single project:

```json
[
  { "title": "Requirements Gathering", "status": "completed", "due_date": "2025-07-01" },
  { "title": "Design Review",          "status": "completed", "due_date": "2025-07-15" },
  { "title": "Development Sprint",     "status": "in_progress", "due_date": "2025-08-01" },
  { "title": "QA & Testing",           "status": "pending",   "due_date": "2025-08-15" },
  { "title": "Deployment",             "status": "overdue",   "due_date": "2025-06-15" }
]
```

```csharp
[Table("project")]
public class ProjectScorecard : ProjectEntity
{
    /// <summary>Total milestones for this project.</summary>
    [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
        AggregateFunction.Count)]
    public int MilestoneCount { get; set; }
    // ? 5

    /// <summary>Completed milestone count (conditional).</summary>
    [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
        AggregateFunction.ConditionalCount,
        conditionColumn: nameof(ProjectMilestoneEntity.Status),
        conditionValue: "completed")]
    public int MilestonesCompleted { get; set; }
    // ? 2

    /// <summary>Overdue milestone count (conditional).</summary>
    [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
        AggregateFunction.ConditionalCount,
        conditionColumn: nameof(ProjectMilestoneEntity.Status),
        conditionValue: "overdue")]
    public int MilestonesOverdue { get; set; }
    // ? 1

    /// <summary>Risk-flagged note count (different child table).</summary>
    [SubqueryAggregate(typeof(ProjectNoteEntity), nameof(ProjectNoteEntity.ProjectId),
        AggregateFunction.ConditionalCount,
        conditionColumn: nameof(ProjectNoteEntity.Category),
        conditionValue: "risk")]
    public int RiskNoteCount { get; set; }
}
```

#### Generated SQL (MSSQL)

```sql
SELECT project.*,
  (SELECT COUNT(*) FROM project_milestone ms
   WHERE ms.project_id = project.id) AS [MilestoneCount],
  (SELECT COUNT(*) FROM project_milestone ms
   WHERE ms.project_id = project.id AND ms.status = 'completed') AS [MilestonesCompleted],
  (SELECT COUNT(*) FROM project_milestone ms
   WHERE ms.project_id = project.id AND ms.status = 'overdue') AS [MilestonesOverdue],
  (SELECT COUNT(*) FROM project_note pn
   WHERE pn.project_id = project.id AND pn.category = 'risk') AS [RiskNoteCount]
FROM project
```

---

### Phase 4: `[JsonCollection]` — JSON Array Projection ?

**Status: Implemented in v3.2.0-beta2.**

Projects child records into a JSON array in a single column, replacing `FOR JSON PATH` (MSSQL) or `json_agg` (PostgreSQL) subqueries.

#### Signature

```csharp
[JsonCollection(
    sourceType: typeof(ChildEntity),
    foreignKey: nameof(ChildEntity.ParentId),
    columns: new[] { "Column1", "Column2" },
    orderBy: "Column1")]
```

#### Parameters

| Parameter | Required | Description | Example |
|:---|:---|:---|:---|
| `sourceType` | Yes | Child entity type to project | `typeof(ProjectMilestoneEntity)` |
| `foreignKey` | Yes | FK property linking child to parent | `nameof(ProjectMilestoneEntity.ProjectId)` |
| `columns` | Yes | Property names to include in the JSON objects | `new[] { "Title", "Status", "DueDate" }` |
| `orderBy` | No | Property name to order results by | `"DueDate"` |
| `joins` | No | Additional entity types to JOIN within the subquery | `new[] { typeof(PersonEntity) }` |

#### Examples

```csharp
[Table("project")]
public class ProjectScorecard : ProjectEntity
{
    /// <summary>Milestones projected as a JSON array.</summary>
    [JsonCollection(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
        columns: new[] { "Title", "Status", "DueDate", "CompletedDate" },
        orderBy: "DueDate")]
    public string MilestonesJson { get; set; }

    /// <summary>Notes projected as a JSON array with joined author name.</summary>
    [JsonCollection(typeof(ProjectNoteEntity), nameof(ProjectNoteEntity.ProjectId),
        columns: new[] { "Content", "Category", "AuthorName", "DateUtcCreated" },
        joins: new[] { typeof(PersonEntity) },
        orderBy: "DateUtcCreated")]
    public string NotesJson { get; set; }
}
```

#### Output Value (MilestonesJson)

```json
[
  {
    "title": "Requirements Gathering",
    "status": "completed",
    "due_date": "2025-07-01",
    "completed_date": "2025-06-28"
  },
  {
    "title": "Design Review",
    "status": "completed",
    "due_date": "2025-07-15",
    "completed_date": "2025-07-14"
  },
  {
    "title": "Development Sprint",
    "status": "in_progress",
    "due_date": "2025-08-01",
    "completed_date": null
  }
]
```

#### Generated SQL

| Provider | SQL |
|:---|:---|
| **SQL Server** | `(SELECT ms.title, ms.status, ms.due_date, ms.completed_date FROM project_milestone ms WHERE ms.project_id = project.id ORDER BY ms.due_date FOR JSON PATH) AS [MilestonesJson]` |
| **PostgreSQL** | `(SELECT json_agg(row_to_json(sub)) FROM (SELECT ms.title, ms.status, ms.due_date, ms.completed_date FROM project_milestone ms WHERE ms.project_id = project.id ORDER BY ms.due_date) sub) AS "MilestonesJson"` |

---

### Complete Example: All Attributes Together

This shows how all four attribute types combine on a single Detail class to replace an entire SQL view:

```csharp
[Table("project")]
public class ProjectScorecard : ProjectEntity
{
    // ── Existing: Remote properties (JOINs to related tables) ──

    [RemoteProperty(typeof(OrganizationEntity), nameof(OrganizationId), nameof(OrganizationEntity.Name))]
    public string OrganizationName { get; set; }

    [RemoteProperty(typeof(ProjectCategoryEntity), nameof(CategoryId), nameof(ProjectCategoryEntity.Name))]
    public string CategoryName { get; set; }

    // ── Phase 1: JSON path extraction (✅) ──

    [JsonPath("metadata", "$.priority")]
    public string Priority { get; set; }

    [JsonPath("metadata", "$.client.name")]
    public string ClientName { get; set; }

    [JsonPath("metadata", "$.client.region")]
    public string ClientRegion { get; set; }

    [JsonPath("metadata", "$.risk_level", SqlType = "int")]
    public int? RiskLevel { get; set; }

    // ── Phase 2: SQL expression (✅) ──

    [SqlExpression("COALESCE({ComputedScore}, {Score})")]
    public int? EffectiveScore { get; set; }

    [SqlExpression(
        mssql: "CONCAT({LeadFirstName}, CASE WHEN {LeadLastName} IS NOT NULL THEN ' ' + {LeadLastName} ELSE '' END)",
        postgresql: "{LeadFirstName} || COALESCE(' ' || {LeadLastName}, '')")]
    public string LeadName { get; set; }

    // ── Phase 3: Subquery aggregate (✅) ──

    [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
        AggregateFunction.Count)]
    public int MilestoneCount { get; set; }

    [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
        AggregateFunction.ConditionalCount,
        conditionColumn: nameof(ProjectMilestoneEntity.Status), conditionValue: "completed")]
    public int MilestonesCompleted { get; set; }

    [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
        AggregateFunction.ConditionalCount,
        conditionColumn: nameof(ProjectMilestoneEntity.Status), conditionValue: "overdue")]
    public int MilestonesOverdue { get; set; }

    [SubqueryAggregate(typeof(ProjectNoteEntity), nameof(ProjectNoteEntity.ProjectId),
        AggregateFunction.ConditionalCount,
        conditionColumn: nameof(ProjectNoteEntity.Category), conditionValue: "risk")]
    public int RiskNoteCount { get; set; }

    // ── Phase 4: JSON collection (✅) ──

    [JsonCollection(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
        columns: new[] { "Title", "Status", "DueDate", "CompletedDate" },
        orderBy: "DueDate")]
    public string MilestonesJson { get; set; }

    [JsonCollection(typeof(ProjectNoteEntity), nameof(ProjectNoteEntity.ProjectId),
        columns: new[] { "Content", "Category", "AuthorName", "DateUtcCreated" },
        joins: new[] { typeof(PersonEntity) },
        orderBy: "DateUtcCreated")]
    public string NotesJson { get; set; }
}
```

### Key Rules for AI Agents

1.  **Do NOT add any of these attributes to canonical entities** — same rule as `[RemoteProperty]`. They belong on Detail classes only.
2.  **Use `SqlType` on `[JsonPath]` for non-string comparisons** — without it, `JSON_VALUE` returns `nvarchar` and numeric comparisons may fail.
3.  **Null metadata is safe** — when the JSON column is NULL, `[JsonPath]` properties resolve to null.
4.  **No `.Value` on nullable properties** — same rule as all nullable properties in LINQ.
5.  **Use `{PropertyName}` tokens in `[SqlExpression]`** — never hard-code column names. The framework handles naming conventions, aliases, and provider differences.
6.  **All four attributes are implemented** — use `[JsonPath]`, `[SqlExpression]`, `[SubqueryAggregate]`, and `[JsonCollection]` freely on Detail classes. All work in `Get<T>`, `Query<T>`, `GetList<T>`, and WHERE clauses on both SQL Server and PostgreSQL.
7.  **When a user asks to eliminate a SQL view**, evaluate which attributes from the taxonomy can replace each column in the view. A combination of `[RemoteProperty]`, `[JsonPath]`, `[SqlExpression]`, `[SubqueryAggregate]`, and `[JsonCollection]` can typically replace the entire view.

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
