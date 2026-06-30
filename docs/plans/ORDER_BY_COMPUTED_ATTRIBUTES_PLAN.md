# ORDER BY on Computed / Remote Attributes (+ DISTINCT) — Implementation Plan

> **Goal:** allow `OrderBy` / `OrderByDescending` / `ThenBy` / `ThenByDescending` to target the "view-replacing"
> and remote attributes — `[JsonPath]`, `[RemoteProperty]`/`[RemoteKey]`, `[SqlExpression]`, and
> `[SubqueryAggregate]` — so the database sorts by the resolved SQL fragment instead of erroring on a
> non-existent column. This is the ORDER BY analogue of the 3.5.1 WHERE-predicate fix. Folds in basic
> **`DISTINCT`** support in the same cut. Target: **v3.8.1-beta1** — positioned as *gap closure for
> predicates/ordering on view-replacing properties*, not a new feature tier (the absence was an oversight,
> not a design choice), hence a patch rather than a minor bump.

---

## 1. Current behavior (the gap)

`OrderByClauseVisitor<T>` is constructed with only the **plain column-name map** and the `[NotMapped]`-only
unmapped list (`SqlLinqQueryProvider.ParseExpression`, the OrderBy branch). It resolves each ordering key via
`GetColumnName(property)`, whose fallback for any non-discovered property is `property.Name.ToLower()`. Since
`[JsonPath]`/`[SqlExpression]`/`[SubqueryAggregate]` are plain attributes (not `[NotMapped]`) and
`[RemoteProperty]` maps to a joined table — none are base-table columns — ordering by them emits e.g.
`ORDER BY clientname`, a column that doesn't exist → **runtime "Invalid column name"**. No tests cover it.

## 2. Key facts that make this easy

- `ResolveRemoteJoins<T>(tableName)` already builds a complete `PropertyToColumnMap` keyed by `prop.Name`:
  - `[RemoteProperty]`/`[RemoteKey]` → `alias.column`
  - `[JsonPath]` → the dialect JSON value expression
  - `[SqlExpression]` → the resolved expression (tokens expanded)
  - `[SubqueryAggregate]` → a correlated scalar subquery
  - (`[JsonCollection]` produces a JSON-array subquery — **not** orderable; excluded.)
- The **base SELECT** (`CreateGetOneOrSelectCommandText<T>`) already calls `ResolveRemoteJoins<T>` and appends
  `JoinClauses` to the FROM + `ExtraColumns` to the SELECT for any entity that declares these attributes. So
  whenever you can order by such a property, **the necessary joins are already in the query**.
- Aliases are **deterministic** across calls (alias counters reset per call, stable property iteration order),
  so an ORDER BY that re-resolves `alias.column` references the same alias the SELECT emitted.

**Consequence:** the fix is essentially "give `OrderByClauseVisitor` the `PropertyToColumnMap` and check it
first." No separate join management is required for the primary `Query<T>().OrderBy(...)` path.

## 3. Design

### 3.1 Per provider (×4: SqlServer, PostgreSql, MySql, Sqlite)
1. **Query provider** (`SqlLinqQueryProvider.ParseExpression`): in both OrderBy spots — the explicit
   `OrderBy/ThenBy` branch and the implicit `LastOrDefault/Last` ordering — resolve
   `ResolveRemoteJoins<T>(tableName).PropertyToColumnMap` and pass it to `OrderByClauseVisitor<T>`.
2. **`OrderByClauseVisitor<T>`**: add a `Dictionary<string,string> propertyToColumnMap` constructor parameter
   (default empty for back-compat). In `VisitOrderingExpression` (and the `BuildValueSql`/`BuildTestSql`
   member paths used by the CASE/ternary branch), **resolve the ordering member through the map first**:
   `if (map.TryGetValue(property.Name, out var sql)) emit sql; else GetColumnName(property)`.
   A resolved fragment is emitted verbatim (already provider-correct and parameter-free).

### 3.2 What gets ordered by what
| Attribute | ORDER BY emits | Join needed |
|:--|:--|:--|
| `[JsonPath]` | `JSON_VALUE(...)` / `col #>> '{...}'` / `JSON_EXTRACT(...)` | no |
| `[SqlExpression]` | the resolved expression | no |
| `[SubqueryAggregate]` | the correlated scalar subquery | no |
| `[RemoteProperty]`/`[RemoteKey]` | `alias.column` | yes — already added by SELECT |
| `[JsonCollection]` | — (excluded; not orderable) | — |

### 3.3 Out of scope for v1 (documented limitations)
- **Custom `.Select()` projections that drop the joins**: `Query<T>().Select(x => new { x.Id }).OrderBy(x => x.RemoteProp)`
  may not include the remote join. v1 supports the standard `Query<T>()...OrderBy(...)` (full entity) path; the
  Select-projection interaction is a follow-up if it proves needed.
- SQLite has no JSON-collection RLS etc.; JsonPath/SqlExpression/SubqueryAggregate still apply where the entity
  uses them (SQLite supports `json_extract`, expressions, and correlated subqueries).

### 3.4 DISTINCT (folded into this cut)

A LINQ `Distinct()` in the chain sets `QueryComponents.IsDistinct`; the SELECT is rewritten from
`SELECT …` to `SELECT DISTINCT …`. It composes with computed attributes (the resolved JSON / expression /
subquery fragments are distinct-ed like any other column) and with `Where`/`Select`/`OrderBy`.

**Guards (clear errors, not silent surprises):**
- `Distinct().Count()` (any aggregate after `Distinct`) → **`NotSupportedException`**. `SELECT COUNT(DISTINCT …)`
  over a multi-column projection / computed grid is out of scope for this cut; the message tells the caller to
  count client-side or drop `Distinct`. (Postponed, not abandoned.)
- `Distinct()` + `OrderBy(x => x.NotProjected)` under a **custom `.Select(...)`** projection → **`InvalidOperationException`**.
  SQL requires every `ORDER BY` key to appear in a `SELECT DISTINCT` list; rather than emit invalid SQL we throw
  with the offending key named. (Full-entity `Distinct()` has no custom projection, so every column is present and
  this guard does not fire.)

**What works / what doesn't (these examples go in the docs):**

```csharp
// ✔ works
db.Query<Project>().Distinct();                                   // SELECT DISTINCT <all columns>
db.Query<Project>().Where(p => p.Active).Distinct();              // filter + distinct
db.Query<Project>().Select(p => new Project { Priority = p.Priority }).Distinct();           // distinct of one (computed) column
db.Query<Project>().Select(p => new Project { Priority = p.Priority }).Distinct()
                   .OrderBy(p => p.Priority);                     // order key IS in the projection
db.Query<Project>().OrderBy(p => p.EffectiveScore).Distinct();    // full-entity distinct + order by computed attr

// ✘ throws (clear, intentional)
db.Query<Project>().Select(p => new Project { Priority = p.Priority }).Distinct().Count();   // NotSupportedException
db.Query<Project>().Select(p => new Project { Priority = p.Priority }).Distinct()
                   .OrderBy(p => p.Name);                         // InvalidOperationException — 'name' not projected
```

## 4. Files touched (per provider)
- `…/Visitors/*OrderByClauseVisitor.cs` — new ctor param + `ResolveOrderColumn` (map-first) + `IsOrderableProperty`
  (map overrides the unmapped-column gate so computed attrs are orderable).
- `…/{SqlServer|PostgreSql|MySql|Sqlite}/*LinqQueryProvider.cs` — pass the map at the explicit-OrderBy call site;
  detect `Distinct()` → `IsDistinct`; inject `SELECT DISTINCT` + the two guards in `BuildQueryComponents`.
- `…/{provider}/QueryComponents.cs` — add `IsDistinct`.

## 5. Testing
Reuse the existing detail entities / `vw_project_scorecard`-style fixtures that already declare these
attributes. Per provider:
- `OrderBy([JsonPath])` asc/desc returns rows in JSON-value order.
- `OrderBy([SqlExpression])` and `ThenBy` composition.
- `OrderBy([SubqueryAggregate])` (e.g. child count).
- `OrderBy([RemoteProperty])` sorts by the joined column (asserts the join alias is referenced and results ordered).
- Regression: ordering by a plain column and by a `[NotMapped]`-excluded property is unchanged; full suites green.

## 6. Phasing
| Phase | Scope | Status |
|:--|:--|:--|
| 1 | SQL Server: visitor + query-provider wiring + order-by/distinct tests (lean proof, validate locally) | ✅ done — 200/200 green |
| 2 | PostgreSQL, MySQL, SQLite (mirror) + tests | in progress |
| 3 | Docs (README ORDER BY/DISTINCT note + the would/wouldn't examples), changelog `[3.8.1-beta1]`, version bump | in progress |

## 7. Decisions (resolved)
1. **Include `[SubqueryAggregate]`?** **Yes** — it's free (same `PropertyToColumnMap`) and orderable; a superset of
   the requested JsonPath/Remote/SqlExpression.
2. **Release sequencing.** Ship as **`3.8.1-beta1`** (not 3.9). 3.8.0 stays beta (RLS); this layers ordering/distinct
   on top as a patch. Marketed as *gap closure for predicates/ordering on view-replacing properties* — neither a bug
   apology nor a feature fanfare.
3. **`.Select()`-projection edge case.** Accepted as a **documented limitation**: the whole-entity
   `Query<T>()…OrderBy(…)` path is fully supported (the real use case); a custom `.Select()` that *drops* a
   `[RemoteProperty]`'s join and then orders by it is out of scope. JsonPath / SqlExpression / SubqueryAggregate are
   self-contained (no join), so they order correctly even under a custom projection.
4. **DISTINCT scope.** Basic `Distinct()` → `SELECT DISTINCT`. `Distinct().Count()` **postponed** (throws
   `NotSupportedException`). `Distinct()` + order-by-unprojected-column under a custom `.Select` **guarded**
   (`InvalidOperationException`). See §3.4.
