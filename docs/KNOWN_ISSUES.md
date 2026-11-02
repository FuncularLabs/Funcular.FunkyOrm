# Known issues as of 3.14.0

This is the one list. It replaces the per-release "Known issues" sections of the [Changelog](../Changelog.md) from
3.10.0 to 3.12.1, which now point here; each release's Changelog records what it fixed and what it added to this
file. The list was consolidated on 2026-11-02 for 3.14.0 from the Changelog, the backlog's Appendix A, Usage.md,
Advanced.md and three internal quality reports (2026-10-20, 2026-10-25 and 2026-10-27).

Each item says what you see, the release that first recorded it, and what to do (a workaround, or the reason it is
by design). **Not re-verified since 3.10** marks an item recorded against 3.10.0 that nobody has re-run on a later
release; it is kept rather than dropped because dropping it would claim a fix that was never made. Items fixed in
3.14.0 are in the Changelog's `[3.14.0]` Fixed section, not here.

## Query translation

- **`Length` counts what the database counts**: code points rather than C#'s UTF-16 units (an emoji is one shorter in
  SQL than in C#, except on SQL Server under a non-`_SC` collation), and a `CHAR(n)` column's padding. Trailing
  spaces count on every provider, as in C#. *Since 3.11.0.* Use `VARCHAR`, and compare lengths in memory where
  text outside the Basic Multilingual Plane matters.
- **A conditional compared in its branches nests one `CASE` deeper than in 3.11.0** (a computed integer's
  `ToString()` adds a `COALESCE`); SQL Server fails at 11 nested levels (Msg 125). *Since 3.12.0.* Flatten the
  conditional, or compute the value before the query.
- **A nested conditional in EF's form repeats its SQL and parameters about three times per level**: past SQL
  Server's 2,100-parameter limit from about 6 levels, SQLite's 32,766 from about 8. *Since 3.12.0.* Same remedy.
- **A long `||`/`&&` chain overflows the stack sooner than in 3.11.0**: about 2,430 operands on a 1.5 MB stack,
  about 760 on IIS's 512 KB. *Since 3.12.0; kept by the owner's ruling.* Use `ids.Contains(x.Id)`.
- **A lazy collection inside a conditional the SQL repeats is bound once per copy**, and a class with its own
  `Contains` is enumerated once per copy. *Since 3.12.0.* Materialize the collection first.
- **A predicate over an interface-typed source (`IQueryable<IFoo>`) throws**, because interface members have no
  mapped column. *Recorded 3.10; not re-verified since 3.10.* Query the concrete type (Advanced.md §5).
- **A generic helper constrained to an interface (`where T : IHasId`) fails in every WHERE translator**
  (`Convert(x, IHasId).Id`). *Since 3.10.0; documented in Advanced.md §5.* Constrain to a base class.
- **ORDER BY ternary gaps**: a captured `.HasValue` is read as a column; a comparison the compiler wraps in
  `ConvertChecked` throws; a constant test that selects a constant branch is a SQL Server error (408/1008).
  *Recorded 3.10; not re-verified since 3.10.* Order by a plain member and sort in memory for the rest.
- **Unordered paging without an `OrderBy` sorts by a column literally named `id`.** A table whose key has another
  name gets a wrong or failing order key. *Since 3.10.0; only the `id` default is documented (Advanced.md §5).*
  Always add an `OrderBy` before `Skip`/`Take`.
- **An ORDER BY ternary's comparison keeps SQL's three-valued logic**: a NULL takes the `ELSE` branch. C#-style null
  semantics (3.12.0) were scoped to WHERE. *Since 3.12.0.* Compare nullable members in WHERE, or sort in memory.
- **`Contains(string, StringComparison)` and comparer overloads are ignored**; the column's collation decides.
  *Recorded 3.10; not re-verified since 3.10.* Use the collation you need on the column.
- **A null string's `Contains` compared with a `bool` keeps 3.11.0's unparenthesised SQL**, which SQL Server rejects.
  *Found in the 3.12.0 review.* Null-check the string before the query.
- **`OrderBy` over a `Guid` follows the database's ordering** (SQL Server compares byte groups 10–15 first); only
  `Min`/`Max` over a `Guid` are guarded (`GuidOrdering`). *Since 3.11.0; Advanced.md §5.* Order by another column.
- **`.Value`/`.HasValue` on a nullable property, arithmetic (`x.Total * 2`), `??`, and bitwise `&`/`|` are not
  translated in WHERE**: they throw before any query (`.Value`/`.HasValue` throw `NullableExpressionException`).
  *Since 3.10.0, by design; Usage.md pitfall 4.* Write `x.M != null`, and compute arithmetic before the query.
- **`[SubqueryAggregate(ConditionValue = …)]` is inlined into the SQL unescaped.** It is a developer-written
  constant, so this is a robustness limit, not an injection path. *Recorded 2026-10-25.* Keep it a plain literal.

## Null semantics

- **A non-nullable property over a SQL NULL reads back as its default (`0`) while WHERE and ORDER BY compare the
  NULL**: a missing parent's `[RemoteProperty] int`, an absent JSON key, a `[SubqueryAggregate]` `Sum`/`Avg` over no
  child rows. A filter on such a property can disagree with the rows you got back. *Since 3.12.0; Usage.md.*
  Declare the property nullable (`int?`, `decimal?`). The EF-style `COALESCE` for non-nullable `Sum`/`Avg` is a
  recorded follow-up.
- **A string `Contains` that doesn't read the row, with a null string or null search value, is `NULL LIKE NULL`**:
  it matches no row, negated or not, and `All` counts it satisfied where C# would throw. *Since 3.11.0; kept in
  3.12.0.* Null-check captured strings before the query.
- **Every string column is treated as nullable** (the provider doesn't read `NOT NULL`), so `!= "x"` on a required
  column gains `OR col IS NULL`, and `x.Req != noName` with a null `noName` matches (and deletes) every row.
  *Since 3.12.0; Usage.md.* Null-check captured values; the delete guard refuses the plain always-true shapes only.
- **Null semantics are always C#'s.** There is no `UseRelationalNulls`-style switch. *By design.*

## SQL Server

- **A `bool` conditional in a truth position, or `!` of a `bit` value used as a `CASE` result or on a comparison's
  right side, is a syntax error**, as in 3.11.0 (a bare `bool` column in a condition was fixed in 3.12.1; the
  remaining forms have not been re-verified since that fix). *Since 3.11.0.* Compare the `bool` explicitly
  (`x.Flag == true`).
- **A `DateTime` under a day-first session language is read day-first.** *Recorded 3.10; not re-verified since
  3.10.* Keep the session language's date order, or bind dates as parameters (the default for WHERE).
- **A supplementary-plane character in a `LIKE` pattern matches every row under `SQL_Latin1_General_CP1_CI_AS`.**
  *Recorded 3.10.* A collation issue, not FunkyORM's; use a `_SC` collation.
- **`CHAR(n)` padding breaks `EndsWith`** (SQL Server and PostgreSQL); SQL Server `LIKE` patterns are limited to
  8,000 bytes, and escaping can double a value's length. *Recorded 3.10.* Use `VARCHAR`, or compare an
  `[SqlExpression]` with `RTRIM`.
- **`OrderBy(computedSubquery).Select(…).Distinct()` fails** ("ORDER BY items must appear in the select list").
  *Recorded 3.10; not re-verified since 3.10.* Order by a projected column.
- **`[SubqueryAggregate]` `Avg` over an `int` column read into a `double` property fails on read**: SQL Server's
  `AVG(int)` is an `int`, and the reader's typed getter doesn't convert it. *Found in the 3.12.0 review.* Average a
  `decimal` column into a `decimal` property. Do not declare the property `int?` to make the read succeed: SQL
  Server's integer average is already truncated, so that reads a wrong value silently. A cast in the SQL is a
  recorded follow-up.
- **`Update` of an entity whose key is an unsigned integer (SQL Server, PostgreSQL) or an enum (PostgreSQL) fails**:
  the UPDATE's key parameter takes the raw value, unlike `Get`, which normalizes it since 3.10.1. *Since 3.10.1.*
  Use a signed integral, `Guid` or string key; the fix is a recorded follow-up.

## PostgreSQL

- **NULLs sort last ascending** (the other providers sort them first), so `OrderBy(x => x.Nullable).Last()` can be a
  NULL row. *Since 3.10.0; Advanced.md §5.* Filter nulls first, or order descending. `NULLS FIRST` is a candidate.
- **An enum parameter in WHERE throws `InvalidCastException`** (reads work). *Recorded 3.10; not re-verified since
  3.10.* Compare the underlying integer (`(int)x.Status == 2`), or store the enum as its number.
- **WHERE timestamps depend on whether Npgsql or FunkyORM set `EnableLegacyTimestampBehavior` first.** *Recorded
  3.10; not re-verified since 3.10.* Set it once, before the first provider is created.
- **Materializing a `DateTimeOffset` property over `timestamptz` throws `InvalidCastException`** (the mapper doesn't
  convert the UTC `DateTime` Npgsql returns; the aggregate path does). *Recorded in the 3.11.0 review.* Declare the
  property `DateTime` (UTC).
- **No `MIN`/`MAX` over `uuid`**: a `Guid` `Min`/`Max` is rejected whatever `GuidOrdering` says. *Since 3.11.0; by
  design.*
- **`[JsonCollection]` with `Distinct()` is rejected by the engine** (42883: no equality for `json`). *Since the
  attribute's first release, 3.2.0; Advanced.md §2.* Use `jsonb`, or `Distinct` in memory.

## MySQL

- **A JSON null reads as the text `'null'`**, so `x.DocK == null` doesn't select a row whose key holds a JSON null.
  *Since 3.12.0.* Test for the text, or store SQL NULL.
- **`MySqlStringComparison` and `SqliteStringComparison` never had an effect**; both are `[Obsolete]` since 3.11.0
  and go in 4.0. *Since 3.11.0.* Remove the setting; the column's collation decides, so choose a `_bin`/`_cs`
  collation (MySQL) or `COLLATE NOCASE` (SQLite) on the column for the comparison you want.
- **The audit-context key check accepts non-ASCII letters** (`char.IsLetterOrDigit`) where the runbook says
  `[A-Za-z0-9_]`, and the runbook blames MySQL's naming for rejecting dotted keys: MySQL allows `.` and `$`;
  FunkyORM's own check is the limit. *Found in the 3.13.0 review.* Use ASCII letters, digits and `_`.

## SQLite

- **WHERE binds `Guid` and `DateTime` values in Microsoft.Data.Sqlite's format, not the stored text**: `x.Id == id`
  misses a Guid stored with hex letters, a date with `.500` milliseconds finds no row. *Since 3.11.0; Usage.md; the
  owner's call, because existing databases hold the stored text.* Read such rows by key with `Get<T>(id)`, which
  works, rather than by a WHERE on the `Guid`; compare dates in memory.
- **A `decimal` is bound as text**, so against an expression with no affinity (`[SubqueryAggregate]`,
  `[SqlExpression]`) it compares as text: `x.ChildSum != 10m` selects a sum of 10. *Since 3.12.0.* Compare as
  `double`, or cast in the expression.
- **No `decimal` type**: `Sum`/`Average`/`Min`/`Max` over a `decimal` read a `REAL` and can differ in the last
  digits. *Since 3.11.0; a SQLite storage limit.* Aggregate in memory where the last digits matter.
- **Dates are written and read in the current culture** (fi-FI `03.04.05`; ar-SA Hijri), and a `DateTimeOffset`
  can't be read back. *Recorded 3.10; not re-verified since 3.10.* Run under an invariant culture.
- **Provider state isn't safe for concurrent execution from one `Query<T>()` root.** *Recorded 3.10; not
  re-verified since 3.10; may overlap the sequential-reuse fix in 3.10.0.* One provider per thread.
- **A table without a `rowid` plus exactly one remote join: unordered paging fails with `no such column`.** *Since
  3.10.0.* Add an `OrderBy`.

## Deletes and the guard

- **The guard proves only the plain always-true shapes.** `x.Id == 2 || x.Id != 2`, `x.N == 5 || x.N != 5` and
  `x.Req != noName` (with `noName` null) are accepted as deletes, and the last two delete every row since 3.12.0.
  *Since 3.11.0; extended 3.12.0; Usage.md.* Delete by key or by a predicate you have run as a query first. A
  complementary-literal check is a recorded follow-up.
- **The twin check asks the provider's SQL only for parts of at most 64 nodes**; a larger twin counts as the same
  operand only where its shape is. *Since 3.12.0; a bound on the guard's work.* Keep delete predicates small, and
  run them as a query first.
- **Quoted text is case-sensitive to the verdict but not to the translated-clause check**: case-only twins in a
  nested conditional are accepted (every row deleted); a JSON path against its other-case twin is refused though
  the values differ. *Since 3.12.0.* Delete by key, or by a predicate you have run as a query first.
- **Under a cast through `object` the verdict reads no captured value**, so a shape that is always true,
  self-comparing or empty-searching only once that value is read is accepted. *Since 3.12.0.* Don't cast captured
  values through `object` in a delete predicate.
- **A delete whose predicate reads only a `[RemoteProperty]`/`[RemoteKey]` fails with the database's
  unknown-column error** (a DELETE has no JOIN); 3.10.0 refused it with a message. *Since 3.11.0; Usage.md.* Delete
  by an own column.

## Packaging and platform

- **The `net48` build carries the SQL Server provider only.** The PostgreSQL, MySQL and SQLite providers are bundled
  for net8.0 and netstandard2.0. *Since 3.11.0; README; by design.*
- **A direct reference below a 3.12.1 patched minimum fails restore** (NU1605; NU1109 under central package
  management), and a central `PackageReference` that keeps an older `System.Formats.Asn1` throws
  `FileNotFoundException` at run time. *Since 3.12.1; a consequence of the advisory pins.* Reference the patched
  versions or none.
- **`IOrmDataProvider` isn't `IDisposable`** (the providers are); adding the interface breaks implementers, so it
  waits for 4.0. *Recorded in the 2026-10-20 quality report.* Dispose the concrete provider.
- **No async LINQ chain**: `QueryAsync(predicate)`, `GetAsync`, `GetListAsync` and the writes are async; a composed
  `Query<T>()` chain is synchronous. *By design.*
- **.NET Framework: `s.Contains('a')` with a `char` throws `NotSupportedException`** (it binds to
  `Enumerable.Contains<char>`). *Found in the 3.12.0 review.* Pass a string: `Contains("a")`.
