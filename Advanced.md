# FunkyORM — Advanced Usage: What Works, What Doesn't

FunkyORM translates a deliberately **bounded** slice of LINQ to SQL. That boundary is what keeps it fast and
predictable — but it means some constructs that compile in C# aren't translated, and a few translate only in
specific shapes. This guide is the honest map of that boundary for five areas that trip people up:
**projections**, **computed (view-replacing) attributes**, **aggregates**, **remote properties**, and **which LINQ
operators are translated at all** (§5).

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

**Scalar projection works too (v3.9).** A top-level `Select(x => x.Member)` returns `List<memberType>` — it
emits the narrow `SELECT` for that one member, materializes the entity, and projects it in memory:
```csharp
var ids = provider.Query<Person>()
    .Where(p => p.EmployerCountryName != null)     // filter by a [RemoteProperty]
    .OrderByDescending(p => p.EmployerCountryName)  // order by it
    .Skip(0).Take(25)
    .Select(p => p.Id)                             // ✅ project only the key → List<int>
    .ToList();
```
The member can be an own column or a self-contained computed attr (`[JsonPath]`/`[SqlExpression]`/
`[SubqueryAggregate]`).

**The scalar `Select` must be the *outermost* operator.** Filter, order, and page **before** it (as above); a
lambda-bearing operator applied **after** it (`.Where(x => …)`, `.OrderBy(x => …)`, `.All`/`.Any(pred)`/
`.Count(pred)`/`.First(pred)`, a chained `.Select`) throws a clear `NotSupportedException` — the projected
sequence is no longer the entity, so it isn't translated. Reducing terminals (`.Count()`, `.First()`, `.Sum()`,
`.ElementAt`, …) after it likewise throw. Do those in memory after `.ToList()`, or aggregate off the base query.
Constant-arg operators (`.Take`/`.Skip`/`.Distinct`) after the scalar `Select` still compose.

**Still doesn't work** — these throw `NotSupportedException`:
```csharp
provider.Query<Person>().Select(p => new { p.FirstName }).ToList(); // ❌ anonymous type
provider.Query<Person>().Select(p => new PersonDto { ... }).ToList(); // ❌ different DTO
provider.Query<Person>().Select(p => p).ToList();                  // ❌ identity projection (just drop the Select)
provider.Query<Person>().Select(p => p.EmployerCountryName).ToList(); // ❌ scalar of a [RemoteProperty] VALUE
```
Reshape into an anonymous type or another DTO by materializing first (`.ToList()`) and projecting in memory, or
by querying a dedicated `[Table]` DTO. A **`[RemoteProperty]` value can't be projected** (own the join column
in the WHERE/ORDER BY, but project the key) — it resolves to a joined `alias.column` a projection's `FROM`
doesn't carry.

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

**Works:** all seven aggregates, plus `LongCount` (3.10), which returns a `long` (SQL Server computes it with
`COUNT_BIG(*)`); filtering an aggregate by a **forward** remote attribute (the required join is injected
automatically); filtering by a computed attribute.

```csharp
var count = provider.Query<Person>().Count(p => p.Gender == "Female");
var n = provider.Query<Person>().Where(p => p.EmployerCountryName == "USA").Count(); // forward-remote filter → JOIN injected
```

**Doesn't work:**

- Filtering **`Count` / `LongCount` / `All` / `Sum` / `Average`** by a **reverse** (one-to-many) remote attribute throws
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
| `Count` / `LongCount` / `All` / `Sum` / `Average` with a **reverse filter** | ❌ `NotSupportedException` — aggregate in memory |

The reverse-aggregate guard is deliberately conservative and **entity-wide**: if an entity declares *any*
reverse remote link, filtering `Count`/`LongCount`/`All`/`Sum`/`Average` by any remote column on it — even a forward one —
throws. Keep forward and reverse remote attributes on separate detail entities if you need forward-remote
aggregates.

**Never:** a `[RemoteProperty]` / `[RemoteKey]` inside a custom `Select` (`NotSupportedException`) — query the
whole entity, or move the attribute onto a detail entity you query directly.

---

## 5. Supported LINQ operators (v3.10)

Before building any SQL, FunkyORM checks the query's chain of operators against the list below. An operator
that isn't listed, or a listed one in an unsupported position, throws `NotSupportedException` naming the
operator, and **no command runs**. (Up to 3.9, several of these were silently ignored or mistranslated — see the
3.10.0 Changelog.)

<!-- funky:supported-operators:begin -->
| Operator | Translated as | Notes |
|---|---|---|
| `Where` | `WHERE` (several are combined with `AND`) | A predicate over the queried entity. |
| `Select` | a narrow `SELECT` | A column subset of the same entity (`new T { … }`) or a single member (`x => x.Member`); see §1. |
| `OrderBy`, `OrderByDescending` | `ORDER BY` | **One per query** (below). Own columns are table-qualified when the entity has remote joins. A ternary becomes a `CASE`, with its values sent as parameters (strings, chars, `Guid`s and dates; numbers, booleans and enums stay inline); a comparison with `null` in it (a literal, a variable holding null, or any value computed without reading the row) becomes `IS [NOT] NULL` (on PostgreSQL, a value compared with `null` is decided before the query is sent). |
| `ThenBy`, `ThenByDescending` | further `ORDER BY` keys | A key repeated later in the chain is dropped: it can never break a tie. |
| `Skip`, `Take` | `OFFSET … FETCH` (SQL Server), `LIMIT … OFFSET` (others) | See the paging rule below. Without an `OrderBy`, pages are ordered by `id` (SQLite: `rowid`). `Skip(n < 0)` acts as `Skip(0)`; `Take(n ≤ 0)` returns an empty result without a query. |
| `Distinct` | `SELECT DISTINCT` | With a custom projection, every ordering key must be projected. Not combined with an aggregate. |
| `First`, `FirstOrDefault` | the first row of the order | With or without a predicate. |
| `Single`, `SingleOrDefault` | at most two rows (`TOP (2)` / `LIMIT 2`), then LINQ's checks | Two or more matches throw; `Single` also throws on none. |
| `Last`, `LastOrDefault` | the first row of the **inverted** order (`TOP (1)` / `LIMIT 1`) | Every ordering key is inverted. With no `OrderBy`, the order is `Id DESC`. |
| `Count`, `LongCount` | `COUNT(*)`; `COUNT_BIG(*)` for `LongCount` on SQL Server | `LongCount` returns a `long`. |
| `Any`, `All` | `EXISTS` | `All` requires a predicate. |
| `Sum`, `Average`, `Min`, `Max` | the SQL aggregate over one mapped column | Selector overloads only. Some result types and empty-set cases are wrong in 3.10.0; fixes are planned for 3.10.1 (Changelog, Known issues). |
| `Cast`, `OfType` | nothing: the rows are already of that type | `Cast<T>()` to the row type itself or a reference conversion (`Cast<object>()`, `Cast<BaseClass>()`); `OfType<T>()` to the row type only, and not over a nullable or reference member (it would drop nulls). |
<!-- funky:supported-operators:end -->

**The paging rule.** After `Skip`/`Take`, only `Select`, `Cast`/`OfType`, one `Take` after a `Skip`, and a
parameterless `First*`/`Single*` are translated. Anything else — `Where`, `OrderBy`, `Distinct`, an aggregate,
`Last`, `First(pred)`, a second `Skip` — throws, because SQL would apply it *before* the page, not after. Apply it
before `Skip`/`Take`, or materialize the page first: `query.Skip(n).Take(k).ToList().Where(...)`. This includes
`Skip(0)`, so a `Skip(page * size)` helper hits it on page 1 too.

**One `OrderBy`.** A second `OrderBy`/`OrderByDescending` anywhere after an earlier ordering throws. In LINQ the
later ordering becomes the primary key and the earlier one only breaks ties; write that as one chain:
`query.OrderBy(later).ThenBy(earlier)`, keeping each earlier key's direction (`ThenByDescending` for a descending one).

**NULLs in an ordering.** The database decides where NULLs sort: SQL Server, MySQL and SQLite put them first in
ascending order, as LINQ does; PostgreSQL puts them last. `First`, `Last` and `ToList` all follow the database's
order, so on PostgreSQL `OrderBy(x => x.Nullable).Last()` can be a row whose key is NULL.

**`Last` and `Distinct`.** `Last`/`LastOrDefault` after `Distinct()` with a custom projection need an explicit
`OrderBy` on a projected key, even when `Id` is projected: distinct projected rows have no default order, so it
throws.

**Base-type and interface views.** Over `IQueryable<BaseClass>` or `IQueryable<IInterface>` (by assignment or
`Cast`), enumeration, paging and parameterless terminals work, but a **predicate** written against the base type
throws a clear message. Apply it to the concrete `IQueryable<T>`, or write the helper as a generic method
constrained to a base class (`where TEntity : BaseClass`). (A helper constrained to an *interface* fails in the
WHERE translator; that's a known limitation.)

**Everything else** throws, naming the operator: `Reverse`, `TakeWhile`, `SkipWhile`, `TakeLast`, `SkipLast`,
`ElementAt`, `Concat`, `Union`, `Join`, `GroupBy`, `SelectMany`, `Contains`, `Aggregate`, `DefaultIfEmpty`,
`DistinctBy`, `MinBy`/`MaxBy`, and the indexed, comparer and default-value overloads of supported operators.
Lambdas *inside* a supported operator aren't affected: `Where(p => ids.Contains(p.Id))` still works.

---

## The one rule that covers most of this

If a construct isn't translated, **`.ToList()` and do it in memory.** FunkyORM does the heavy,
set-based work in the database (filtering, sorting, joining, aggregating over indexed columns); the moment you
need a shape or an operation it doesn't translate, materialize and let LINQ-to-objects finish the job.
