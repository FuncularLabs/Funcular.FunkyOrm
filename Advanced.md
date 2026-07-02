# FunkyORM — Advanced Usage: What Works, What Doesn't

FunkyORM translates a deliberately **bounded** slice of LINQ to SQL. That boundary is what keeps it fast and
predictable — but it means some constructs that compile in C# aren't translated, and a few translate only in
specific shapes. This guide is the honest map of that boundary for four areas that trip people up:
**projections**, **computed (view-replacing) attributes**, **aggregates**, and **remote properties**.

These behaviors are covered by integration tests. Everything marked ❌ fails at a specific, named point —
usually a clear `NotSupportedException` that tells you the alternative. When in doubt, the universal escape
hatch is: **materialize with `.ToList()`, then use ordinary LINQ-to-objects in memory.**

> **Terms.** *Computed (view-replacing) attributes* — `[JsonPath]`, `[SqlExpression]`, `[SubqueryAggregate]`,
> `[JsonCollection]` — are self-contained: each becomes an inline SQL fragment and needs no join. *Remote
> attributes* — `[RemoteProperty]` / `[RemoteKey]` — pull a value from a related table through a `LEFT JOIN`.
> A *forward* remote link is many-to-one (join on the target's key); a *reverse* link is one-to-many (join on
> a child's foreign key, so each row fans out one-per-child).

---

## 1. Projections (`Select`)

FunkyORM **materializes the entity type you query**. So a `Select` is translated to SQL only when it projects
back **into that same entity** — picking a subset of its columns, or folding in a self-contained computed
attribute. Reshaping into anything else (a scalar, an anonymous type, a different DTO) is a job for
LINQ-to-objects, after you've materialized.

**Works:**
```csharp
// Whole entity — every mapped column, including computed and remote ones.
var people = provider.Query<Person>().ToList();

// Same-entity column subset (and folding in self-contained computed attributes).
var slim = provider.Query<ProjectScorecard>()
    .Select(p => new ProjectScorecard { Name = p.Name, Priority = p.Priority })  // Priority is [JsonPath]
    .ToList();

// Reshape into anything you like — in memory, after materializing.
var names = provider.Query<Person>()
    .Where(p => p.LastName.StartsWith("Sm"))
    .ToList()
    .Select(p => new { p.FirstName, p.LastName });

// Want the database to return only certain columns? Map a [Table] DTO to the same table with just
// those properties, and query that type directly.
var summaries = provider.Query<PersonSummary>().ToList();   // PersonSummary : [Table("person")] with a few props
```

**The performance idiom.** `Select(x => new T { Key = x.Key })` doesn't just work — it emits a **narrow
`SELECT`** of only the projected column(s) and composes with `Where` + `OrderBy` (including a `[RemoteProperty]`
join column) + `Skip`/`Take`. On a wide entity with many `[JsonPath]`/`[SqlExpression]`/`[RemoteProperty]`
members, that **defers computing the unprojected columns until after the Top-N**. When you filter/order/page by
a computed or joined column but only need a key back, this is dramatically faster than materializing the whole
entity — and it's the supported way to do it. (You get back a `T` with only the projected members populated.)

**Doesn't work** — a *top-level* `Select` that projects to anything other than the queried entity throws
`NotSupportedException` (with a message pointing you to the fix):
```csharp
provider.Query<Person>().Select(p => p.Id).ToList();               // ❌ scalar projection
provider.Query<Person>().Select(p => new { p.FirstName }).ToList(); // ❌ anonymous type
provider.Query<Person>().Select(p => new PersonDto { ... }).ToList(); // ❌ different DTO
provider.Query<Person>().Select(p => p).ToList();                  // ❌ identity projection (just drop the Select)
```
Fix any of these by materializing first (`.ToList()`) and projecting in memory, or by querying a dedicated
`[Table]` DTO. Also: a **`[RemoteProperty]` cannot be referenced inside a custom `Select`** — it resolves to a
joined `alias.column` that a projection's `FROM` doesn't carry, so it throws `NotSupportedException`. Query the
whole entity instead.

---

## 2. Computed (view-replacing) attributes

`[JsonPath]`, `[SqlExpression]`, `[SubqueryAggregate]`, and `[JsonCollection]` are first-class almost
everywhere — because each resolves to an inline SQL fragment, they behave like real columns:

**Works:** reading them in a whole-entity query; filtering (`Where`); sorting (`OrderBy`/`ThenBy`); folding a
self-contained one into a same-entity `Select`; and `Distinct()` on a whole entity that declares them.

```csharp
provider.Query<ProjectScorecard>()
    .Where(p => p.Priority == "high")          // [JsonPath] in WHERE
    .OrderByDescending(p => p.EffectiveScore)  // [SqlExpression] in ORDER BY
    .ToList();
```

**Doesn't work:**

- **PostgreSQL + `[JsonCollection]` + `Distinct()`.** A `[JsonCollection]` emits `json_agg(...)`, which is
  PostgreSQL's `json` type, and PostgreSQL has no equality operator for `json` — so `SELECT DISTINCT *` is
  rejected by the engine (`42883`). FunkyORM's SQL is correct; the database can't compare it. Project a column
  subset that excludes the collection column, then `Distinct()`. **SQL Server, MySQL, and SQLite are fine.**
- **`Distinct().Count()`** (any aggregate after `Distinct()`) throws `NotSupportedException` — count in memory.
- **`Distinct()` with a custom `Select` and an `OrderBy` on a column you didn't project** throws
  `InvalidOperationException` (SQL requires every `ORDER BY` key to be in the `SELECT DISTINCT` list).
- **`Distinct()` with a custom `Select` and paging (`Skip`/`Take`) but no `OrderBy`** throws
  `InvalidOperationException` — paging a `DISTINCT` projection needs a deterministic `OrderBy` over a projected
  column.

---

## 3. Aggregates

Chain `Count` / `Any` / `All` / `Sum` / `Min` / `Max` / `Average` **directly off `Query<T>()`** so the database
does the counting — don't `.ToList()` first just to `.Count()` it.

**Works:** all seven aggregates; filtering an aggregate by a **forward** remote attribute (the required join is
injected automatically); filtering by a computed attribute.

```csharp
var count = provider.Query<Person>().Count(p => p.Gender == "Female");
var n = provider.Query<Person>().Where(p => p.EmployerCountryName == "USA").Count(); // forward-remote filter → JOIN injected
```

**Doesn't work:**

- Filtering **`Count` / `All` / `Sum` / `Average`** by a **reverse** (one-to-many) remote attribute throws
  `NotSupportedException` — the reverse join fans rows out and would inflate the number. Materialize and
  aggregate in memory: `query.Where(...).ToList().Count()`. (`Any` / `Min` / `Max` are fan-out-safe and *do*
  work over a reverse join.)
- `Distinct().Count()` — count in memory.
- An aggregate selector that isn't a simple column (`.Sum(x => x.A + x.B)`) — sum a mapped column, or in memory.
- `GroupBy(...)` is **not translated** — it throws a clear `NotSupportedException`. Materialize and group in
  memory: `query.ToList().GroupBy(...)`.

> **A word on `SUM` and overflow.** `Sum(x => x.SomeIntColumn)` returns an `int`, and the database computes it
> as an `int`. Over a large table the running total can exceed `int.MaxValue` and the database raises an
> overflow error — this is normal SQL behavior, not a FunkyORM limitation. If a total can get that big, sum a
> wider column or aggregate in memory.

---

## 4. Remote properties — the detailed rules

`[RemoteProperty]` and `[RemoteKey]` pull a value from a related table over a `LEFT JOIN`. They're powerful but
carry the most rules, so they get their own section.

**Declaring them.** Point at a single target property (inference mode) and the resolver finds the foreign-key
path, or spell out the full chain (explicit mode) for multi-hop or disambiguation:
```csharp
[RemoteProperty(typeof(Country), nameof(Country.Name))]                 // inference
public string CountryName { get; set; }

[RemoteProperty(typeof(Country),
    nameof(OrganizationId), nameof(Organization.HeadquartersAddressId),
    nameof(Address.CountryId), nameof(Country.Name))]                   // explicit, multi-hop
public string HeadquartersCountryName { get; set; }
```
Each forward foreign-key property in the path resolves to its target type **by name** — `ParentId` → `Parent`,
including suffix matches (`EmployerOrganizationId` → `Organization`) and an `Entity` suffix on the type
(`CountryId` → `CountryEntity`). **As of v3.8.5, the foreign key on the *final* hop — the one that lands on the
target — can be named anything;** the explicit `typeof(...)` you pass to `[RemoteProperty]` is authoritative for
it. Only *intermediate* foreign keys in a multi-hop path must convention-match, or carry
`[RemoteLink(typeof(TargetType))]` to name their target explicitly (that's how, for example, an intermediate
`EmployerId` links to an `Organization`). An unresolvable *intermediate* foreign key throws
`PathNotFoundException`.

**Forward links (many-to-one) are fully supported:** reading in a whole-entity query, `Where`, `OrderBy`,
forward-remote-filtered aggregates, and multi-hop paths all work. Remote reads inside a `BeginTransaction`
(including `Get`/`GetList`/`Update` and their async forms) are safe as of v3.8.3.

**Reverse links (one-to-many) are partial.** Because a reverse join fans each row out one-per-child:

| You want to… | Reverse link |
|---|---|
| Query and filter the whole entity (`.ToList()`) | ✅ works |
| `Any` / `Min` / `Max` with a reverse filter | ✅ works (fan-out-safe) |
| Aggregate with **no** remote filter | ✅ works (stays on the base table) |
| `Count` / `All` / `Sum` / `Average` with a **reverse filter** | ❌ `NotSupportedException` — aggregate in memory |

The reverse-aggregate guard is deliberately conservative and **entity-wide**: if an entity declares *any*
reverse remote link, filtering `Count`/`All`/`Sum`/`Average` by any remote column on it — even a forward one —
throws. Keep forward and reverse remote attributes on separate detail entities if you need forward-remote
aggregates.

**Never:** a `[RemoteProperty]` / `[RemoteKey]` inside a custom `Select` (`NotSupportedException`) — query the
whole entity, or move the attribute onto a detail entity you query directly.

---

## The one rule that covers most of this

If a construct isn't translated, **`.ToList()` and do it in memory.** FunkyORM does the heavy,
set-based work in the database (filtering, sorting, joining, aggregating over indexed columns); the moment you
need a shape or an operation it doesn't translate, materialize and let LINQ-to-objects finish the job.
