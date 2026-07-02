# FunkyORM Advanced — Works / Doesn't-Work Reference (AI Agent Instructions)

> **Audience:** coding agents generating FunkyORM (`Funcular.Data.Orm*`) queries. This is the authoritative
> matrix of what the query engine **can** and **cannot** translate, with the exact failure mode of each
> unsupported construct so you can avoid it (or emit the clear alternative) instead of generating code that
> throws at runtime. Read alongside the reference doc `FUNKYORM_AI_INSTRUCTIONS.md`.
>
> **Ground rule:** these behaviors are covered by integration tests; every ❌ construct fails at the stated
> point with the stated exception. Do not "route around" a ❌ by guessing — use the documented alternative.
> Applies to all four providers (SQL Server, PostgreSQL, MySQL, SQLite) except where a row is tagged with a
> specific provider.
>
> **Vocabulary.** *View-replacing / computed attributes* = `[JsonPath]`, `[SqlExpression]`,
> `[SubqueryAggregate]`, `[JsonCollection]` — resolved inline, need no join. *Remote attributes* =
> `[RemoteProperty]` / `[RemoteKey]` — resolved via a `LEFT JOIN` to another table. *Forward* remote =
> many-to-one (join on the target's PK). *Reverse* remote = one-to-many (join on a child's FK; fans base rows
> out one-per-child).

---

## 1. Projections (`Select`)

**Core rule:** FunkyORM **materializes the entity type `T` you query**. A `Select` is only translated when it
projects **into that same `T`** (a column subset / computed-attribute fold). Any *other* shape is not
SQL-translated.

### ✅ Works
| Construct | Notes |
|---|---|
| `Query<T>()` (no `Select`) | Whole entity: all mapped columns **including** computed and remote attributes. |
| `Query<T>().Select(x => new T { A = x.A, B = x.B })` | Same-entity **column subset**. Materializes `T`. |
| `Query<T>().Select(x => new T { Computed = x.Computed })` | Self-contained computed attrs (`[JsonPath]` / `[SqlExpression]` / `[SubqueryAggregate]`) project into `T` — the resolved SQL is emitted `AS` the property (v3.8.1). |
| `Query<T>().ToList().Select(x => new { x.A, x.B })` | Reshape to anonymous / scalar / other DTO **in memory** (LINQ-to-objects) after materializing. |
| Define a `[Table("t")]` DTO with only the columns you need, then `Query<ThatDto>()` | DB-side column subset — you query the narrower type directly. |

### ❌ Doesn't work — and what to emit instead
| Construct | Fails with | Do this instead |
|---|---|---|
| `Query<T>().Select(x => x.ScalarColumn)` | `NotSupportedException` — "top-level Select projecting to a type other than `T`" (v3.8.3) | `Query<T>()...ToList().Select(x => x.ScalarColumn)` |
| `Query<T>().Select(x => new { x.A, x.B })` (anonymous) | `NotSupportedException` (v3.8.3) | `...ToList().Select(x => new { x.A, x.B })` |
| `Query<T>().Select(x => new OtherDto { ... })` (different DTO) | `NotSupportedException` (v3.8.3) | Query the DTO directly if it maps a table, or reshape in memory |
| `Query<T>().Select(x => x)` (identity) | `NotSupportedException` (body is not a `new T { }` initializer) | Drop the `Select` — `Query<T>()` already returns `T` |
| `Query<T>().Select(x => new T { R = x.RemoteProp })` — a **`[RemoteProperty]`/`[RemoteKey]`** in a custom projection | `NotSupportedException` — the remote value resolves to a joined `alias.column` a custom projection's `FROM` doesn't carry | Query the whole entity (`Query<T>()`), or put the remote attribute on a detail entity you query directly |

> **Why:** before v3.8.3 these silently emitted a full-entity `SELECT` and then threw `InvalidCastException`
> at materialization. The guard now fails fast with a message naming the supported alternatives.

---

## 2. View-replacing / computed attributes

`[JsonPath]`, `[SqlExpression]`, `[SubqueryAggregate]`, `[JsonCollection]` are **self-contained** — each
resolves to an inline SQL fragment (JSON accessor, expression, correlated subquery, `json_agg`) and needs
**no join**.

### ✅ Works
| Construct | Notes |
|---|---|
| Read in whole-entity `Query<T>()` | The fragment is emitted in `SELECT` and materialized onto the property. |
| `Where(x => x.Computed == v)` | Resolves inline in the predicate (v3.5.1). |
| `OrderBy(x => x.Computed)` / `ThenBy` | Sorts by the resolved fragment (v3.8.1). |
| `Select(x => new T { Computed = x.Computed })` | Folded projection (v3.8.1) — self-contained attrs only. |
| `Distinct()` on a whole entity declaring these | `SELECT DISTINCT …` (v3.8.1). **Exception: PostgreSQL + `[JsonCollection]` — see below.** |
| `[SqlExpression("COALESCE({Score}, 0)")]` | `{Token}` resolves to another mapped property's column; supports ternary → SQL `CASE`. |
| `[SubqueryAggregate(typeof(Child), nameof(Child.ParentId), AggregateFunction.Count)]` | Correlated scalar subquery; `ConditionalCount` with `ConditionColumn`/`ConditionValue`. |

### ❌ Doesn't work
| Construct | Fails with | Notes / alternative |
|---|---|---|
| **PostgreSQL only:** whole-entity `Distinct()` on an entity declaring `[JsonCollection]` | Engine error `42883: could not identify an equality operator for type json` | `[JsonCollection]` emits `json_agg(row_to_json(...))` (type `json`), and PostgreSQL has no equality for `json`. FunkyORM emits correct SQL; the engine rejects `SELECT DISTINCT`. Remedy: project a column subset excluding the `[JsonCollection]` column, then `Distinct()`. SQL Server / MySQL / SQLite are unaffected. `[JsonPath]` (jsonb-free scalar) is **not** the trigger. |
| `Distinct().Count()` (any aggregate after `Distinct`) | `NotSupportedException` | Postponed feature. Count client-side (`.ToList().Count`) or drop `Distinct`. |
| `Distinct()` + custom `Select` + `OrderBy(x => x.NotProjected)` | `InvalidOperationException` naming the key | SQL requires every `ORDER BY` key to be in the `SELECT DISTINCT` list. Whole-entity `Distinct()` is unaffected. |
| `Distinct()` + custom `Select` + paging (`Skip`/`Take`) with no `OrderBy` | `InvalidOperationException` | Paging a `DISTINCT` projection requires a deterministic `OrderBy`. Add an `OrderBy` over a projected column. |
| A `[RemoteProperty]` used **as if** self-contained in a custom `Select` | `NotSupportedException` | Remote ≠ self-contained; see §1 and §4. |

---

## 3. Aggregates (`Count` / `Any` / `All` / `Sum` / `Min` / `Max` / `Average`)

**Core rule:** chain the aggregate **directly off `Query<T>()`** so the database does the work
(`SELECT COUNT(*) …`). `Query<T>().Where(...).ToList().Count()` pulls every row into memory first.

### ✅ Works
| Construct | Notes |
|---|---|
| `Query<T>().Count(x => x.Col == v)` / `.Any(...)` / `.Sum(x => x.Num)` / `.Min` / `.Max` / `.Average` | Translated to a SQL aggregate. |
| Aggregate filtered by a **forward** `[RemoteProperty]`/`[RemoteKey]` — e.g. `Query<T>().Where(x => x.RemoteProp == v).Count()` | The required `LEFT JOIN` is injected into the aggregate `FROM` when the `WHERE` references a remote column (v3.8.2). |
| Aggregate filtered by a computed attribute (`[JsonPath]` etc.) | Self-contained — resolves inline, no join. |
| Numeric aggregate selector | Emitted base-table-qualified (`SUM(person.id)`) to stay unambiguous once joins are present (v3.8.2). |

### ❌ Doesn't work
| Construct | Fails with | Do this instead |
|---|---|---|
| `Count` / `All` / `Sum` / `Average` filtered by a **reverse** (one-to-many) `[RemoteKey]`/`[RemoteProperty]` | `NotSupportedException` (message contains "reverse" and "ToList") | The reverse join fans base rows out one-per-child, inflating the result. Materialize and aggregate in memory: `Query<T>().Where(...).ToList().Count()` / `.Sum(...)`. |
| `Distinct().Count()` | `NotSupportedException` | Count client-side. |
| Aggregate selector that is an expression, not a simple member — e.g. `.Sum(x => x.A + x.B)` | `NotSupportedException` — aggregate selectors must be a single mapped column, not an expression | Sum a mapped column, or compute in memory. |
| `GroupBy(...)` | `NotSupportedException` — not translated to SQL | Group in memory: `Query<T>().ToList().GroupBy(...)`. |

### ✅ Allowed over a reverse join (fan-out-safe)
`Any` (compiles to `EXISTS`), `Min`, `Max` — these are unaffected by fan-out and execute normally. A
reverse-remote entity with **no** remote filter also aggregates fine (the fan-out join is not appended).

> **Runtime caveat (not a FunkyORM limit):** `SUM(intColumn)` over a very large table can raise the DB's own
> integer-overflow error during aggregation even when the filtered result is small (e.g. SQL Server partial
> aggregation of an `int` IDENTITY). This is the engine's `int` `SUM` behavior — cast/aggregate a wider type
> or sum in memory if the running total can exceed `int.MaxValue`.

---

## 4. Remote properties (`[RemoteProperty]` / `[RemoteKey]`) — its own category

Remote attributes pull a column (or key) from a **related table** via a `LEFT JOIN`. This is the most
constraint-laden feature — read this section before generating any remote-attributed query.

### Declaring them
```csharp
// Inference mode: one target property → the resolver finds the FK path automatically.
[RemoteProperty(typeof(Country), nameof(Country.Name))] public string CountryName { get; set; }

// Explicit mode: the full FK chain, ending in the target column. Use for multi-hop or to disambiguate.
// Each FK hop resolves to its target type by name (see the convention below).
[RemoteProperty(typeof(Country),
    nameof(OrganizationId),                       // → Organization
    nameof(Organization.HeadquartersAddressId),   // → Address   (suffix match)
    nameof(Address.CountryId),                     // → Country
    nameof(Country.Name))]                         // target column
public string HeadquartersCountryName { get; set; }

[RemoteKey(typeof(Country), keyPath: new[] { nameof(Country.Id) })] public int CountryId { get; set; }
```

**Naming convention:** each **forward** FK property in the path resolves to its target type **by name** —
`ParentId` → `Parent`, including suffix matches (`EmployerOrganizationId` → `Organization`) and an `Entity`
suffix on the type (`CountryId` → `CountryEntity`). If a FK's name does **not** match its target type, put
`[RemoteLink(typeof(TargetType))]` on that FK property to name the target explicitly — that is how, for
example, an `EmployerId` FK is linked to an `Organization`. An unresolvable FK throws `PathNotFoundException`
("Could not determine target type for FK …"); duplicate simple type names across the assembly can throw
`AmbiguousMatchException`.

### Forward (many-to-one) — ✅ fully supported
| Construct | Notes |
|---|---|
| Whole-entity `Query<T>()` | Remote column joined and materialized; cold-cache resolution is deterministic (v3.8.3 — resolves the real snake_case column regardless of which types were materialized first). |
| `Where(x => x.RemoteProp == v)` | Join injected; filter on the joined column. |
| `OrderBy(x => x.RemoteProp)` | Sorts by the joined `alias.column` (v3.8.1). |
| Forward-remote-filtered aggregate | Join injected (v3.8.2) — see §3. |
| Multi-hop paths (e.g. Person → Organization → Address → Country) | Supported via explicit mode. |
| Remote read **inside a `BeginTransaction`** (eager `Get`/`GetList`/`Update`, async) | Safe — cold remote-target schema discovery borrows the ambient transactional connection (v3.8.3); earlier it threw a nested-connection error. |

### Reverse (one-to-many) — ⚠️ partial
A reverse path joins on a **child's** foreign key (e.g. `Country ← Address ← PersonAddress → Person`), so each
base row fans out one-per-child.
| Construct | Result |
|---|---|
| Whole-entity `Query<T>().Where(x => x.ReverseKey == v).ToList()` | ✅ Works — rows fan out, but `ToList()` handles it; filtering by the reverse key is supported. |
| `Any` / `Min` / `Max` over a reverse filter | ✅ Fan-out-safe — executes. |
| Reverse entity aggregate with **no** remote filter | ✅ Stays on the base table (no fan-out join appended). |
| `Count` / `All` / `Sum` / `Average` over a **reverse remote filter** | ❌ `NotSupportedException` — materialize and aggregate in memory. |

> **The reverse-aggregate guard is entity-wide (conservative/fail-safe).** It keys off whether the entity
> declares *any* reverse remote link — so if an entity mixes forward and reverse remotes, filtering
> `Count`/`All`/`Sum`/`Average` by *any* remote column on it (even the forward one) throws. Keep forward and
> reverse remotes on separate detail entities if you need forward-remote aggregates.

### Remote attributes — ❌ never
| Construct | Fails with |
|---|---|
| A `[RemoteProperty]`/`[RemoteKey]` in a custom `Select(x => new T { R = x.RemoteProp })` | `NotSupportedException` — the join isn't carried by a custom projection's `FROM`. Query the whole entity, or move the attribute to a detail entity you query directly. |

---

## Quick decision rules for agents

1. **Need a reshaped result (scalar / anonymous / other DTO)?** Materialize first: `Query<T>().ToList().Select(...)`. Never top-level `Select` to a non-`T` shape.
2. **Need only some DB columns?** Make a `[Table]` DTO with just those properties and query it directly.
3. **Filtering/sorting/aggregating by a computed or forward-remote attribute?** Fine — the join/fragment is injected automatically.
4. **Aggregating with a filter on a reverse (one-to-many) remote attribute?** Not allowed — `ToList()` then aggregate in memory.
5. **`Distinct()` then `Count()`?** Not allowed — count in memory.
6. **PostgreSQL + `[JsonCollection]` + `Distinct()`?** Not allowed — exclude the collection column from the distinct projection.
7. **A `[RemoteProperty]` inside a custom `Select`?** Never — query the whole entity instead.
8. **`GroupBy`?** Not translated — materialize then group in memory (`Query<T>().ToList().GroupBy(...)`).
