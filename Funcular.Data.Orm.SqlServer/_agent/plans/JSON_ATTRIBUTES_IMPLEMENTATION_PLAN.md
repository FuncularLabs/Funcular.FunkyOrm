# JSON Query Attributes — Implementation Plan

> **Goal**: Enable developers to use C# attributes to query, extract, aggregate, and project JSON data — eliminating the need to create SQL Views for the most common JSON scenarios. Start with MSSQL; design for PostgreSQL parity via `ISqlDialect`.

---

## 1. Analysis of the Motivating SQL View

The following view (`vw_project_scorecard` in `Database/integration_test_db.sql`) uses the integration test schema to illustrate every category of work the new attributes must address:

```sql
CREATE VIEW vw_project_scorecard AS
SELECT
    p.id,
    p.name,
    p.budget,
    p.organization_id,

    -- (A) Scalar JOIN — already handled by [RemoteProperty]
    o.name                              AS organization_name,

    -- (A) Scalar JOIN — lead person
    p.lead_id,

    -- (B) Expression JOIN — CONCAT across joined person columns
    CONCAT(lead.first_name,
           CASE WHEN lead.last_name IS NOT NULL
                THEN ' ' + lead.last_name ELSE '' END)
                                        AS lead_name,

    -- (A) Scalar JOIN — category lookup
    p.category_id,
    pc.name                             AS category_name,

    -- (Phase 1) JSON scalar extraction — JSON_VALUE on the same table
    JSON_VALUE(p.metadata, '$.priority')            AS priority,
    JSON_VALUE(p.metadata, '$.client.name')          AS client_name,
    JSON_VALUE(p.metadata, '$.client.region')        AS client_region,
    CAST(JSON_VALUE(p.metadata, '$.risk_level') AS INT) AS risk_level,

    -- (E) Coalesce / Fallback — computed value with column fallback
    COALESCE(ms_stats.computed_score, p.score)      AS effective_score,

    -- (D) Subquery Aggregate — total milestone count
    COALESCE(ms_stats.milestone_count, 0)           AS milestone_count,

    -- (D) Subquery Aggregate — conditional counts by status
    COALESCE(ms_stats.milestones_completed, 0)      AS milestones_completed,
    COALESCE(ms_stats.milestones_overdue, 0)        AS milestones_overdue,
    COALESCE(ms_stats.milestones_pending, 0)        AS milestones_pending,

    -- (D) Subquery Aggregate — note counts by category
    COALESCE(note_stats.note_count, 0)              AS note_count,
    COALESCE(note_stats.risk_note_count, 0)         AS risk_note_count,
    COALESCE(note_stats.blocker_note_count, 0)      AS blocker_note_count,

    -- (C) JSON Projection — milestones as JSON array
    (
        SELECT
            ms.title,
            ms.status,
            ms.due_date,
            ms.completed_date
        FROM project_milestone ms
        WHERE ms.project_id = p.id
        ORDER BY ms.due_date
        FOR JSON PATH
    )                                   AS milestones_json,

    -- (C) JSON Projection — notes as JSON array (with joined author name)
    (
        SELECT
            pn.content,
            pn.category,
            CONCAT(author.first_name,
                   CASE WHEN author.last_name IS NOT NULL
                        THEN ' ' + author.last_name ELSE '' END)
                                        AS author_name,
            pn.dateutc_created          AS created
        FROM project_note pn
        LEFT JOIN person author ON pn.author_id = author.id
        WHERE pn.project_id = p.id
        ORDER BY pn.dateutc_created
        FOR JSON PATH
    )                                   AS notes_json

FROM
    project p
    LEFT JOIN organization o        ON p.organization_id = o.id
    LEFT JOIN person lead           ON p.lead_id = lead.id
    LEFT JOIN project_category pc   ON p.category_id = pc.id
    OUTER APPLY (
        SELECT
            COUNT(*)                                                        AS milestone_count,
            SUM(CASE WHEN ms.status = 'completed'   THEN 1 ELSE 0 END)     AS milestones_completed,
            SUM(CASE WHEN ms.status = 'overdue'      THEN 1 ELSE 0 END)    AS milestones_overdue,
            SUM(CASE WHEN ms.status = 'pending'      THEN 1 ELSE 0 END)    AS milestones_pending,
            CAST(ROUND(
                SUM(CASE WHEN ms.status = 'completed' THEN 1.0 ELSE 0 END)
                / NULLIF(COUNT(*), 0) * 100, 0
            ) AS INT) AS computed_score
        FROM project_milestone ms
        WHERE ms.project_id = p.id
    ) ms_stats
    OUTER APPLY (
        SELECT
            COUNT(*)                                                        AS note_count,
            SUM(CASE WHEN pn.category = 'risk'    THEN 1 ELSE 0 END)       AS risk_note_count,
            SUM(CASE WHEN pn.category = 'blocker'  THEN 1 ELSE 0 END)      AS blocker_note_count
        FROM project_note pn
        WHERE pn.project_id = p.id
    ) note_stats;
```

### Capability Categories

| Category | SQL Technique | Example Column | Already in FunkyORM? |
|:---|:---|:---|:---|
| **A. Scalar JOIN** | `LEFT JOIN` + column | `organization_name`, `category_name` | ✅ `[RemoteProperty]` |
| **B. Expression JOIN** | `LEFT JOIN` + `CONCAT`/`CASE` | `lead_name` | ❌ |
| **JSON Scalar** | `JSON_VALUE` on same table | `priority`, `client_name`, `risk_level` | ❌ |
| **C. JSON Projection** | Subquery + `FOR JSON PATH` | `milestones_json`, `notes_json` | ❌ |
| **D. Subquery Aggregate** | `OUTER APPLY` + `COUNT`/`SUM(CASE…)` | `milestone_count`, `milestones_completed`, `risk_note_count` | ❌ |
| **E. Coalesce/Fallback** | `COALESCE(computed, column)` | `effective_score` | ❌ |

Category A is already solved. The remaining four are the gap.

---

## 2. EF Core's Approach & Applicability

### EF Core JSON Columns (EF Core 7+)

EF Core maps CLR types to JSON columns via Fluent API:

```csharp
// EF Core Fluent API (not attributes)
modelBuilder.Entity<Customer>()
    .OwnsOne(c => c.Address, b => b.ToJson());

// Then query into it:
context.Customers.Where(c => c.Address.City == "London");
```

Under the hood this generates `JSON_VALUE(Address, '$.City')` (MSSQL) or `Address->>'City'` (PostgreSQL).

**Assessment for FunkyORM:**

- ❌ **Fluent API doesn't fit.** FunkyORM is attribute-first, no `DbContext` ceremony.
- ❌ **`OwnsOne`/`OwnsMany` is tightly coupled** to EF's change tracker and ownership model.
- ✅ **The JSON path navigation concept is excellent.** Querying JSON via `$.path` syntax is the right abstraction.
- ✅ **The SQL functions are portable.** `JSON_VALUE` (MSSQL) and `->>` (Postgres) are well-matched.

### EF Core Keyless Entities / `ToSqlQuery`

```csharp
modelBuilder.Entity<ProjectScorecard>().HasNoKey().ToSqlQuery("SELECT ... FROM project ...");
```

This is essentially "map a class to a raw SQL string." FunkyORM already supports this pattern via `[Table("view_name")]`. Not helpful for eliminating the view itself.

### EF Core Computed Columns

```csharp
modelBuilder.Entity<Person>()
    .Property(p => p.FullName)
    .HasComputedColumnSql("[FirstName] + ' ' + [LastName]");
```

Again Fluent, but the **concept** of declaring a SQL expression for a property is directly applicable.

**Bottom line:** EF Core's *concepts* (JSON path navigation, computed SQL expressions) are excellent, but their *mechanism* (Fluent API, ownership model) is not appropriate for FunkyORM. The attribute-based approach is the right fit.

---

## 3. Proposed Attribute Taxonomy

Following the existing `RemoteAttributeBase` inheritance pattern and FunkyORM's "Detail class" convention:

```
Attribute
├── RemoteAttributeBase
│   ├── RemoteKeyAttribute          (existing)
│   └── RemotePropertyAttribute     (existing)
├── JsonPathAttribute               (Phase 1 — NEW)
├── SqlExpressionAttribute          (Phase 2 — NEW)
├── SubqueryAggregateAttribute      (Phase 3 — NEW)
└── JsonCollectionAttribute         (Phase 4 — NEW)
```

---

## 4. Phased Implementation

### Phase 1: `[JsonPath]` — Scalar extraction from a JSON column

**Highest value, lowest complexity.**

**Purpose:** Extract a single value from a JSON column on the **same** table. This is the "80% use case" for JSON columns.

**Example column value** — the `metadata` column targeted by the examples in this section:

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

```csharp
/// <summary>Column 'metadata' contains JSON; extract $.client.name as a string.</summary>
[JsonPath("metadata", "$.client.name")]
public string ClientName { get; set; }
```

**Generated SQL:**

| Provider | SQL |
|:---|:---|
| **MSSQL** | `JSON_VALUE([project].[metadata], '$.client.name') AS [ClientName]` |
| **PostgreSQL** | `project.metadata #>> '{client,name}' AS "ClientName"` |

**Architecture touch-points:**

1. New `JsonPathAttribute` class in `Funcular.Data.Orm.Core/Attributes/`.
2. `ISqlDialect` gets new method: `string BuildJsonValueExpression(string columnExpr, string jsonPath, string castType = null)`.
3. `ResolveRemoteJoins<T>` (or a new parallel method `ResolveComputedColumns<T>`) detects `[JsonPath]` properties and appends them to `ExtraColumns`.
4. `WhereClauseVisitor` maps `[JsonPath]` properties to their SQL expression so `.Where(p => p.ClientName == "Acme Corp")` generates `WHERE JSON_VALUE(metadata, '$.client.name') = @p0`.

**Filtering support:** This is critical. `JSON_VALUE` returns `nvarchar` by default in MSSQL. For typed comparisons, the attribute should accept an optional `SqlType` hint:

```csharp
[JsonPath("metadata", "$.risk_level", SqlType = "int")]
public int RiskLevel { get; set; }
// MSSQL: CAST(JSON_VALUE([metadata], '$.risk_level') AS int)
// Postgres: (metadata->>'risk_level')::int
```

**PostgreSQL feasibility:** ✅ Full parity. PostgreSQL's `->>`/`#>>` operators and `::type` casting are well-supported and arguably cleaner than MSSQL's `JSON_VALUE`.

---

### Phase 2: `[SqlExpression]` — Computed/Expression columns (Categories B & E)

**Purpose:** Declare a raw SQL expression for a property. Handles `COALESCE`, `CONCAT`, `CASE`, and any other expression.

**Example row context** — the columns referenced by the expressions in this section:

```json
{
  "_comment": "Relevant columns from project + joined person (lead)",
  "score": 72,
  "computed_score": null,
  "lead_first_name": "Jane",
  "lead_last_name": "Rodriguez"
}
```

```csharp
/// <summary>Falls back to stored score when computed milestone score is null.</summary>
[SqlExpression("COALESCE({ComputedScore}, {Score})")]
public int? EffectiveScore { get; set; }
```

#### `{Braces}` in `[SqlExpression]` — What They Mean

Curly braces denote **C# property name tokens** that the framework resolves into fully qualified SQL column references at query-build time.

| Expression text | What the framework does |
|:---|:---|
| `{Score}` | Looks up the `Score` property → discovers it maps to column `score` → emits `[project].[score]` (MSSQL) or `project.score` (Postgres), including the table alias if JOINs are present. |
| `{ComputedScore}` | Same lookup; if the property is itself backed by another attribute (e.g., a `[SubqueryAggregate]`), its resolved SQL expression is substituted inline. |

**Why braces exist:** The developer writes C# property names (which follow PascalCase conventions), and the framework is responsible for converting them to the correct database column names (which may be `snake_case`, quoted, aliased, or even full sub-expressions). This is the same mapping pipeline that already powers `[RemoteProperty]` and `WhereClauseVisitor`.

**Could a developer omit the braces and write the column name directly?** Technically yes — any text outside braces is emitted verbatim into the SQL. So `[SqlExpression("COALESCE(computed_score, score)")]` would produce valid SQL *if* the developer gets the column name, casing, and quoting exactly right, and *if* no table alias is needed. However, this is **strongly discouraged** because:

1. **It bypasses naming-convention resolution** — the framework won't translate `PascalCase` ↔ `snake_case` or apply `[Column]` overrides.
2. **It breaks when JOINs add table aliases** — bare column names become ambiguous (e.g., two tables both have a `name` column).
3. **It's not portable across providers** — a hard-coded `[score]` (brackets) fails on PostgreSQL, and a hard-coded `"score"` (double-quotes) fails on MSSQL.

**Rule of thumb:** Use `{PropertyName}` for any column reference. Reserve bare SQL for literals, operators, and SQL functions (`COALESCE`, `CASE`, `+`, `||`, etc.).

For cross-provider expressions, support provider-specific overrides:

```csharp
[SqlExpression(
    mssql: "CONCAT({FirstName}, CASE WHEN {LastName} IS NOT NULL THEN ' ' + {LastName} ELSE '' END)",
    postgresql: "{FirstName} || COALESCE(' ' || {LastName}, '')")]
public string LeadName { get; set; }
```

Or, more elegantly, add expression helpers to `ISqlDialect`:

```csharp
// Single expression with dialect-aware token replacement
[SqlExpression("CONCAT({FirstName}, ' ', {LastName})")]
public string LeadName { get; set; }
// ISqlDialect.TranslateConcat() handles the + vs || difference
```

**Architecture touch-points:**

1. New `SqlExpressionAttribute` in `Funcular.Data.Orm.Core/Attributes/`.
2. Token parser that resolves `{PropertyName}` → qualified column name (reuses existing column name cache).
3. `ISqlDialect` gets expression translation methods for common functions (`CONCAT`, `COALESCE`, `ISNULL`).
4. Expression properties are added to `ExtraColumns` like remote properties.
5. WHERE clause support: expression properties are mapped to their full SQL expression in the `remotePropertyMap`.

**PostgreSQL feasibility:** ✅ With either provider-specific overrides or dialect-aware function translation.

---

### Phase 3: `[SubqueryAggregate]` — Aggregated subqueries (Category D)

**Purpose:** Replace `OUTER APPLY` with attribute-driven correlated subqueries.

**Example child rows** — `project_milestone` rows for a single project that the aggregates operate on:

```json
[
  { "title": "Requirements Gathering", "status": "completed", "due_date": "2025-07-01", "completed_date": "2025-06-28" },
  { "title": "Design Review",          "status": "completed", "due_date": "2025-07-15", "completed_date": "2025-07-14" },
  { "title": "Development Sprint",     "status": "in_progress", "due_date": "2025-08-01", "completed_date": null },
  { "title": "QA & Testing",           "status": "pending",   "due_date": "2025-08-15", "completed_date": null },
  { "title": "Deployment",             "status": "overdue",   "due_date": "2025-06-15", "completed_date": null }
]
```

From these rows the attributes below would produce: `MilestoneCount = 5`, `MilestonesOverdue = 1`.

```csharp
/// <summary>Total number of milestones for this project.</summary>
[SubqueryAggregate(
    sourceType: typeof(ProjectMilestoneEntity),
    foreignKey: nameof(ProjectMilestoneEntity.ProjectId),
    function: AggregateFunction.Count)]
public int MilestoneCount { get; set; }

/// <summary>Count of overdue milestones (conditional aggregate).</summary>
[SubqueryAggregate(
    sourceType: typeof(ProjectMilestoneEntity),
    foreignKey: nameof(ProjectMilestoneEntity.ProjectId),
    function: AggregateFunction.ConditionalCount,
    conditionColumn: nameof(ProjectMilestoneEntity.Status),
    conditionValue: "overdue")]
public int MilestonesOverdue { get; set; }
```

**Generated SQL (MSSQL):**

```sql
SELECT p.*,
  (SELECT COUNT(*) FROM project_milestone ms
   WHERE ms.project_id = p.id) AS [MilestoneCount],
  (SELECT COUNT(*) FROM project_milestone ms
   WHERE ms.project_id = p.id AND ms.status = 'overdue') AS [MilestonesOverdue]
FROM project p
```

> **Design decision:** Correlated scalar subqueries in the SELECT list vs. OUTER APPLY. Scalar subqueries are simpler to generate and are portable across MSSQL and PostgreSQL. OUTER APPLY is MSSQL-only (`LATERAL JOIN` in Postgres), but offers better performance when you need multiple aggregates from the same subquery. Recommendation: **start with scalar subqueries** (portable) and offer an `OUTER APPLY` / `LATERAL` optimization as a future enhancement when multiple `[SubqueryAggregate]` attributes share the same `sourceType` and `foreignKey`.

**PostgreSQL feasibility:** ✅ Correlated scalar subqueries work identically. LATERAL JOIN (Postgres equivalent of OUTER APPLY) could be a future optimization.

---

### Phase 4: `[JsonCollection]` — Project child records as JSON array (Category C)

**Purpose:** Replace `FOR JSON PATH` subqueries with an attribute.

**Example output value** — what the `MilestonesJson` column contains for a single project row after the `FOR JSON PATH` / `json_agg` projection:

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

```csharp
/// <summary>Milestones projected as a JSON array.</summary>
[JsonCollection(
    sourceType: typeof(ProjectMilestoneEntity),
    foreignKey: nameof(ProjectMilestoneEntity.ProjectId),
    columns: new[] { nameof(ProjectMilestoneEntity.Title), nameof(ProjectMilestoneEntity.Status),
                     nameof(ProjectMilestoneEntity.DueDate), nameof(ProjectMilestoneEntity.CompletedDate) },
    orderBy: nameof(ProjectMilestoneEntity.DueDate))]
public string MilestonesJson { get; set; }
```

**Generated SQL:**

| Provider | SQL |
|:---|:---|
| **MSSQL** | `(SELECT … FROM project_milestone WHERE project_id = p.id ORDER BY due_date FOR JSON PATH) AS [MilestonesJson]` |
| **PostgreSQL** | `(SELECT json_agg(row_to_json(sub)) FROM (SELECT … FROM project_milestone WHERE project_id = p.id ORDER BY due_date) sub) AS "MilestonesJson"` |

**This is the most complex attribute** because it involves:

- A correlated subquery
- Optional joins within the subquery
- Column selection within the subquery
- Provider-specific JSON serialization syntax

**PostgreSQL feasibility:** ✅ `json_agg(row_to_json(...))` is the direct equivalent, though the syntax differs significantly. This is where `ISqlDialect` earns its keep.

---

## 5. ISqlDialect Extensions

```csharp
public interface ISqlDialect
{
    // ... existing methods ...

    /// Phase 1: JSON scalar extraction
    string BuildJsonValueExpression(string columnExpr, string jsonPath, string castType = null);

    /// Phase 2: Translate common SQL functions (CONCAT, COALESCE) to dialect
    string TranslateFunction(string functionName, params string[] args);

    /// Phase 3: Correlated scalar subquery in SELECT list
    string BuildScalarSubquery(string innerSelect, string alias);

    /// Phase 4: JSON collection projection
    string BuildJsonCollectionSubquery(string innerSelect, string alias);
}
```

**MSSQL implementations:**

```
BuildJsonValueExpression("metadata", "$.name")        → JSON_VALUE([metadata], '$.name')
BuildJsonValueExpression("metadata", "$.name", "int") → CAST(JSON_VALUE([metadata], '$.name') AS int)
BuildJsonCollectionSubquery("SELECT … FOR JSON PATH") → (SELECT … FOR JSON PATH)
```

**PostgreSQL implementations:**

```
BuildJsonValueExpression("metadata", "$.name")          → metadata->>'name'
BuildJsonValueExpression("metadata", "$.name", "int")   → (metadata->>'name')::int
BuildJsonCollectionSubquery("SELECT …")                 → (SELECT json_agg(row_to_json(sub)) FROM (…) sub)
```

---

## 6. WHERE Clause Support (Critical for Querying)

Each new attribute type must integrate with `WhereClauseVisitor` via the existing `remotePropertyMap` pattern. When the visitor encounters a property with a JSON/computed attribute, it should substitute the column reference with the full SQL expression:

```csharp
// User writes:
provider.Query<ProjectScorecard>()
    .Where(p => p.ClientName == "Acme Corp")
    .ToList();

// WhereClauseVisitor resolves ClientName → JSON_VALUE([metadata], '$.client.name')
// Generated WHERE: JSON_VALUE([metadata], '$.client.name') = @p0
```

This mirrors exactly how `[RemoteProperty]` already maps property names to aliased column references like `[organization_0].[name]` in the `PropertyToColumnMap`.

---

## 7. Developer Experience — Following the "Detail Pattern"

Per FunkyORM conventions, these attributes belong on **inherited Detail classes**, not canonical entities.

**Example `metadata` column value** stored in the `project` table (this single JSON document feeds every `[JsonPath]` attribute below):

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

```csharp
// Canonical entity — clean, no computed attributes
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

// Detail class — replaces vw_project_scorecard entirely
[Table("project")]
public class ProjectScorecard : ProjectEntity
{
    // ── Category A: Remote properties (already works today) ──

    [RemoteProperty(typeof(OrganizationEntity), nameof(OrganizationId), nameof(OrganizationEntity.Name))]
    public string OrganizationName { get; set; }

    [RemoteProperty(typeof(ProjectCategoryEntity), nameof(CategoryId), nameof(ProjectCategoryEntity.Name))]
    public string CategoryName { get; set; }

    // ── Phase 1: JSON path extraction ──

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

    // ── Phase 2: SQL expression ──

    /// <summary>Falls back to stored score when computed milestone score is null.</summary>
    [SqlExpression("COALESCE({ComputedScore}, {Score})")]
    public int? EffectiveScore { get; set; }

    /// <summary>Lead person full name via expression across joined person columns.</summary>
    [SqlExpression(
        mssql: "CONCAT({LeadFirstName}, CASE WHEN {LeadLastName} IS NOT NULL THEN ' ' + {LeadLastName} ELSE '' END)",
        postgresql: "{LeadFirstName} || COALESCE(' ' || {LeadLastName}, '')")]
    public string LeadName { get; set; }

    // ── Phase 3: Subquery aggregate ──

    /// <summary>Total milestones for this project.</summary>
    [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
        AggregateFunction.Count)]
    public int MilestoneCount { get; set; }

    /// <summary>Completed milestone count (conditional).</summary>
    [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
        AggregateFunction.ConditionalCount,
        conditionColumn: nameof(ProjectMilestoneEntity.Status),
        conditionValue: "completed")]
    public int MilestonesCompleted { get; set; }

    /// <summary>Overdue milestone count (conditional).</summary>
    [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
        AggregateFunction.ConditionalCount,
        conditionColumn: nameof(ProjectMilestoneEntity.Status),
        conditionValue: "overdue")]
    public int MilestonesOverdue { get; set; }

    /// <summary>Risk-flagged note count.</summary>
    [SubqueryAggregate(typeof(ProjectNoteEntity), nameof(ProjectNoteEntity.ProjectId),
        AggregateFunction.ConditionalCount,
        conditionColumn: nameof(ProjectNoteEntity.Category),
        conditionValue: "risk")]
    public int RiskNoteCount { get; set; }

    // ── Phase 4: JSON collection ──

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

---

## 8. Feasibility Matrix

| View Feature | Attribute Feasible? | Phase | Notes |
|:---|:---|:---|:---|
| Simple column JOINs | ✅ Already done | — | `[RemoteProperty]` |
| `JSON_VALUE` / `->>` extraction | ✅ | 1 | Clean fit |
| `COALESCE`, `CONCAT`, `CASE` | ✅ | 2 | `[SqlExpression]` |
| `COUNT(*)` subquery | ✅ | 3 | `[SubqueryAggregate]` |
| Conditional `SUM(CASE…)` | ⚠️ | 3 | Requires `condition` param; raw SQL fragment or convention-based equality |
| `FOR JSON PATH` / `json_agg` | ✅ | 4 | `[JsonCollection]` |
| Multi-table subquery JOINs | ⚠️ | 4 | e.g., `project_note JOIN person` inside JSON subquery — needs `joins` param |
| `CAST(ROUND(…))` inside subquery | ⚠️ | — | Expressions within subquery columns — may need raw SQL escape hatch |

### Conditional Aggregates

Conditional aggregates (e.g., `SUM(CASE WHEN ms.status = 'overdue'…)`) can be expressed cleanly when the condition is a simple equality on a column in the source table. When it requires a join to a related table, two approaches are available:

**Option A — Convention-based equality (preferred for simple cases):**

```csharp
[SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
    AggregateFunction.ConditionalCount,
    conditionColumn: nameof(ProjectMilestoneEntity.Status),
    conditionValue: "overdue")]
public int MilestonesOverdue { get; set; }
```

This keeps SQL out of the C# and covers the majority of cases where the condition is on a column of the child table itself.

**Option B — Raw SQL condition (escape hatch for complex cases):**

When the condition involves a joined lookup table (e.g., filtering `project_note` by a category code from a related `note_type` table), an explicit SQL fragment is needed:

```csharp
[SubqueryAggregate(typeof(ProjectNoteEntity), nameof(ProjectNoteEntity.ProjectId),
    AggregateFunction.Count,
    condition: "note_type.code = 'risk'",
    joins: new[] { "LEFT JOIN note_type ON project_note.note_type_id = note_type.id" })]
public int RiskNoteCount { get; set; }
```

Recommendation: Support **both**. Option A for the common case, Option B for anything more complex.

---

## 9. Recommended Priority

| Phase | Attribute | Value | Complexity | Recommendation |
|:---|:---|:---|:---|:---|
| **1** | `[JsonPath]` | 🔥 Highest | Low | **Start here.** Smallest delta, biggest impact. |
| **2** | `[SqlExpression]` | High | Medium | Unlocks `COALESCE`, `CONCAT`, `CASE`. |
| **3** | `[SubqueryAggregate]` | High | Medium-High | Eliminates `OUTER APPLY` for counts/sums. |
| **4** | `[JsonCollection]` | Medium | High | `FOR JSON PATH` / `json_agg` replacement. |

Phase 1 alone would let developers extract and **filter on** JSON fields without views. Combined with Phase 2, the majority of "flatten JSON into queryable columns" scenarios that currently require views are covered.

---

## 10. File Inventory (Estimated)

### New Files

| Path | Purpose |
|:---|:---|
| `Funcular.Data.Orm.Core/Attributes/JsonPathAttribute.cs` | Phase 1 attribute |
| `Funcular.Data.Orm.Core/Attributes/SqlExpressionAttribute.cs` | Phase 2 attribute |
| `Funcular.Data.Orm.Core/Attributes/SubqueryAggregateAttribute.cs` | Phase 3 attribute |
| `Funcular.Data.Orm.Core/Attributes/AggregateFunction.cs` | Phase 3 enum (`Count`, `Sum`, `Avg`, `ConditionalCount`) |
| `Funcular.Data.Orm.Core/Attributes/JsonCollectionAttribute.cs` | Phase 4 attribute |

### Modified Files

| Path | Change |
|:---|:---|
| `Funcular.Data.Orm.Core/Interfaces/ISqlDialect.cs` | Add `BuildJsonValueExpression`, `TranslateFunction`, `BuildScalarSubquery`, `BuildJsonCollectionSubquery` |
| `Funcular.Data.Orm.SqlServer/SqlServer/SqlServerDialect.cs` | Implement new `ISqlDialect` methods for MSSQL |
| `Funcular.Data.Orm.SqlServer/SqlServer/SqlServerOrmDataProvider.cs` | Extend `ResolveRemoteJoins<T>` (or add `ResolveComputedColumns<T>`) to detect new attributes and append to `ExtraColumns` / `PropertyToColumnMap` |
| `Funcular.Data.Orm.SqlServer/Visitors/WhereClauseVisitor.cs` | Ensure `remotePropertyMap` lookups cover new attribute-driven expressions |
| `Funcular.Data.Orm.PostgreSql/PostgreSql/PostgreSqlDialect.cs` | Implement new `ISqlDialect` methods for PostgreSQL |
| `Funcular.Data.Orm.PostgreSql/PostgreSql/PostgreSqlOrmDataProvider.cs` | Mirror MSSQL computed column resolution |
