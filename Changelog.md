# Changelog

All notable changes to this project will be documented in this file.

## [3.10.0-beta1] - Unreleased

Query-operator correctness ([#12](https://github.com/FuncularLabs/Funcular.FunkyOrm/issues/12),
[#13](https://github.com/FuncularLabs/Funcular.FunkyOrm/issues/13)). **Upgrade strongly recommended:** several
operators returned wrong results without an error in 3.9.0 and earlier. All four providers. Also: identifier caches
are scoped per provider, dialect and connection (see Fixed and Changed), which is binary-breaking for `OrmDataProvider`
subclasses.

### Security
- **Values in an ORDER BY ternary are now sent as command parameters.** Before, a text value (`x.Name == input ? 0 :
  1`) was written into the SQL as a quoted literal, with only its quotes doubled. On MySQL, whose default mode treats
  a backslash as an escape character, a crafted value could change the query. Now booleans, enums, `NULL` and
  values of type `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double` and
  `decimal` stay inline, and no other value (a string, char, `Guid`, date or time, and so on) is written into the
  SQL: each is sent as a parameter where the SQL uses it. A parameter is typed as 3.9.0's literal
  was (untyped on PostgreSQL, `varchar` on SQL Server), except that on SQL Server strings and chars are
  `nvarchar`, like WHERE's. Upgrade if you order by a ternary over values you don't control.

### Fixed
- **`Single`/`SingleOrDefault` dropped their predicate and never checked cardinality.**
  `Single(p => p.Id == 42)` returned the *first row of the table*, and `Single()` over many rows returned one of
  them. Both now apply the predicate as `WHERE`, read at most two rows (`TOP (2)` / `LIMIT 2`, with no ORDER BY
  added), and follow LINQ: two or more matches throw, and `Single` throws on none.
- **`Last`/`LastOrDefault` returned the first row.** They now invert every ordering key (own, remote, computed
  and `CASE` keys) and read one row; with no `OrderBy` they use `Id DESC`. Like `First` and `ToList`, they follow
  the database's NULL placement (PostgreSQL sorts NULLs last when ascending; LINQ-to-objects sorts them first).
- **`LongCount` threw** (`InvalidCastException`; `NullReferenceException` on an empty set). It now returns a `long`, built like `Count` (SQL Server uses
  `COUNT_BIG(*)`).
- **Ordering on entities with remote joins** ([#12](https://github.com/FuncularLabs/Funcular.FunkyOrm/issues/12)):
  own columns in ORDER BY are now table-qualified (`person.id`). Ordering by an own column that a joined table
  also has (typically `id`), with a narrow projection that leaves that column out, failed with "Ambiguous column
  name". On SQLite, unordered paging on such an
  entity orders by `{table}.rowid`, and `Skip(n)` without `Take` emits `LIMIT -1 OFFSET n` instead of invalid SQL.
- **A ternary ORDER BY key comparing a member with `null`** (`p.M == null ? 0 : 1`, either operand order, the null
  written as a literal, held in a variable, or computed without reading the row) emitted
  `= NULL`, which SQL Server rejected and the other providers evaluated wrongly. It now emits `IS NULL` /
  `IS NOT NULL`.
- **A repeated ordering key** (`OrderBy(a).ThenBy(a)`) failed on SQL Server (error 169). The later duplicate is
  dropped; it can't change the order.
- **The non-generic `IQueryProvider.Execute(expression)`** returned the whole list for a terminal such as
  `First`. It now returns the single element (or throws, as LINQ does).
- **SQLite: reusing a `Query<T>()` root** let a later query inherit an earlier one's projection, parameters or
  ordering. Each query now starts clean.
- **Queries over a base-class or interface view:** enumerating a `Cast<Base>()` query, or paging an
  `IQueryable<Base>` view, threw `InvalidCastException`. Both now return the entity rows.
- **A terminal over a scalar projection seen as `IQueryable<object>`** (`Select(x => x.Name).Cast<object>().First()`)
  returned the whole list as its "first element". It now throws the scalar-projection message.
- `Take(n ≤ 0)` returns an empty result without sending a query; `Skip(n < 0)` acts as `Skip(0)`.
- **An enum value in an ORDER BY ternary** could be emitted as its name (`'B'`): a test then compared an integer
  column with text, and branch values sorted by name. It's now always the underlying number, as LINQ orders them.
- **A property of the enclosing object in an ORDER BY ternary** (captured `this`, e.g. `x.Name == CurrentName ? 0 : 1`
  in an instance method) was emitted as `NULL`. It's now the property's value, in the test and in the branches.
  The getter is now called while the query is translated (3.9.0 never called it), and one that throws is reported
  as `NotSupportedException`.
- **Identifier caches were process-wide.** Table names, column names and the discovered and unmapped sets were
  `static` on `OrmDataProvider`, shared by all four providers. Procedure names (SQL Server, MySQL) and compiled row
  mappers were `static` per provider type. As a result:
  - after SQL Server ran `Query<User>()`, PostgreSQL sent `[Key]`/`[User]` and failed with `42601`;
  - for example, on SQLite, a second database of one provider type read the first one's table and column names, and
    a column missing from the first came back `null` from the second;
  - providers of one type with different dialect types shared names and row mappers: after a provider whose dialect
    quotes every name built the mapper, the default provider read `Id = 0`;
  - a procedure name resolved against one database answered for another.

  Each provider instance now reads its own scope's caches: its runtime type, its dialect's runtime type and its
  connection identity.
- **Two entity types with the same simple name shared column-name cache entries.** The key was `TypeName.Property`.
  It now uses the declaring type's full name, and keys compare ordinally, so `Outer_X.Thing` and `OuterX.Thing` no
  longer collide.
- **`GeneralExtensions.Contains(string, string, StringComparison)` ignored its `comparison` argument** and compared
  case-sensitively in the current culture. It now uses the comparison it is given. Its effect on deletes is under
  Changed.
- **SQLite now uses discovered column names.** Its SELECT list and row mapper use the column discovery found, so a
  property whose column differs by underscores (`Label` → `la_bel`) is queryable.
- **`Delete<T>` and `DeleteAsync<T>`, by predicate or by id, as a type's first use in the process** could run before
  the type's columns were discovered. For example, `x => x.LastName == value` on a member mapped by convention could throw
  `NotSupportedException: Expression type Parameter is not supported`. They now discover the type's columns first.
  All four providers.
- **`ExecProcedure<T>` as a type's first use** no longer leaves `Query<T>()` and `Delete<T>(predicate)` failing for
  that type afterwards. SQL Server and MySQL.

### Changed
These shapes now throw `NotSupportedException` before any query runs, naming the operator. Most of them returned
wrong results silently in 3.9.0; a few happened to be correct, and are listed so you can find them before
upgrading. The full rules are in [Advanced.md §5](Advanced.md#5-supported-linq-operators-v310).
- **Operators FunkyORM doesn't translate** were silently dropped or mistranslated: `Reverse`, `TakeWhile`,
  `SkipWhile`, `TakeLast`, `SkipLast` and `ElementAt` were ignored (`TakeWhile(p => false)` returned every row).
  Every operator not on the supported list now throws, including set and join operators, `Contains`,
  `Aggregate`, `DefaultIfEmpty`, `MinBy`/`MaxBy`, and indexed, comparer and default-value overloads.
  Happened to be correct before: `OrderBy(k).ElementAt(0)`; `DefaultIfEmpty()` over a non-empty set.
- **Operators after `Skip`/`Take`** were applied *before* the page (`Take(5).Count()` counted the whole table).
  Only `Select`, `Cast`/`OfType`, one `Take` after a `Skip`, and a parameterless `First*`/`Single*` are allowed
  after paging now. Happened to be correct before: `Take(k).Distinct()` over a keyed entity, `Take(k ≥ 1).Any()`
  (`Take(0).Any()` returned `true`), `Take(10).Take(5)`, and `Skip(0)` followed by `Where`/`Count`/etc., page 1 of
  a `Skip(page * size)` helper. (On SQLite only an aggregate after `Skip(0)` worked: `Skip` without `Take` was
  invalid SQL there.)
- **A second `OrderBy`/`OrderByDescending`** was mistranslated. Chained directly (`OrderBy(a).OrderBy(b)`), it
  emitted `ORDER BY a, b`: the wrong priority. Across `Where`/`Select`/`Distinct`, the earlier key was dropped,
  so the primary key was right but its ties weren't broken. It now throws; write
  `query.OrderBy(later).ThenBy(earlier)`, keeping each earlier key's direction. Happened to be correct before:
  the across-`Where` form whenever the later key had no ties, e.g. `OrderBy(a).Where(w).OrderByDescending(k).First()`
  on a unique `k`.
- **A predicate written against a base type or interface** over a converted query threw
  `InvalidCastException`, or for `Single*` returned an unrelated row. It now throws a message telling you to
  apply it to the concrete `IQueryable<T>` (or a generic helper constrained to a base class).
- **`Cast`/`OfType`** other than an identity cast or a reference conversion (`Cast<object>()`,
  `Cast<BaseClass>()`) now throw. So does an identity `OfType` over a nullable or reference member, which
  would drop nulls.
- **`Last`/`LastOrDefault` after `Distinct()` with a custom projection** need an explicit `OrderBy` on a
  projected key.

Other changes:

- **New public API in `Funcular.Data.Orm.Linq`:** `QueryOperatorPolicy` (`SupportedOperators`, `IsAllowed`,
  `EnsureSupported`), `ScalarProjectionGuard` and `OrderByTerm`. The four order-by visitors gain an `OrderByTerms`
  property, constructor overloads that take a table qualifier and a parameter generator, and a `Parameters`
  property. Their 3.9.0 constructor is unchanged, so code compiled against 3.9.0 keeps binding. Without a generator
  a visitor still inlines values; MySQL's inline literal now also escapes backslashes.
- **SQL Server: text in an ORDER BY ternary is now `nvarchar`**, like a WHERE string parameter; 3.9.0's literal was
  `varchar`. Text outside the database's code page now matches (`x.Name == "Ωmega" ? 0 : 1` matched no row before).
  When both branches are text they sort by the collation's Unicode rules, so under a `SQL_*` collation punctuation
  can sort differently than in 3.9.0 (`"a-c"` and `"ab"` swap places). Equality follows the same rules, as it
  already did in WHERE: under a `SQL_*` collation, `x.Code == "ss" ? 0 : 1` now also matches a `varchar` value `ß`.
- **The text of an ORDER BY ternary's parameters is formatted with the invariant culture, and dates and times have
  a fixed format.** 3.9.0 wrote a `DateTimeOffset`, `DateOnly` or `TimeOnly` in the current culture's format, and
  some other values in the current culture's text (a `Half` as `1,5`, a negative `nint` with the culture's minus
  sign). A database could reject that text, or, under a day-first culture, read a date with day and month swapped.
  A `TimeOnly` lost its seconds (`10:00 AM`), so times in the same minute compared as equal. The text of a value
  sent as a parameter (or quoted, by a visitor without a generator) is now as below; booleans, enums, `NULL` and
  the eleven numeric types are inline (see Security).
  - `DateTime`: `yyyy-MM-dd HH:mm:ss.fff`, as in 3.9.0.
  - `DateTimeOffset`: `yyyy-MM-dd HH:mm:ss.fffffffK`. On MySQL it is its UTC time, `yyyy-MM-dd HH:mm:ss.ffffff`, as
    WHERE sends it.
  - `DateOnly`: `yyyy-MM-dd`.
  - `TimeOnly`: `HH:mm:ss.FFFFFFF`.
  - any other such value: `Convert.ToString` with the invariant culture (a type that is neither `IConvertible` nor
    `IFormattable` is its own `ToString()`).
- **SQLite: unordered paging on an entity whose base has no `rowid`** (a view or a `WITHOUT ROWID` table) **and
  exactly one remote join** now orders by the base's `rowid` and fails with `no such column`. In 3.9.0 it paged by
  the joined table's `rowid`, a meaningless order. Add an explicit `OrderBy`. (With no joins, or with two or more,
  such an entity already failed in 3.9.0.)
- **Cache scopes.** The identity is the constructor's connection string (or, when that is empty, the supplied
  connection's string) as the provider's builder writes it, with `Password` removed. So:
  - strings that differ only in the password, under `Password` or a synonym the builder maps to it, share a scope;
  - other differences (server, database, user, `Search Path`, `Options`, `Application Name`, a timeout) make another
    scope;
  - a string the builder rejects is used as given.

  The registry stores the identity as a SHA-256 of its UTF-8 bytes and never removes a scope.
- **Each new scope starts cold:** it discovers its tables and columns and builds its row mappers on first use. To vary
  a session value without a new scope, use `AuditContext` (SQL Server, PostgreSQL, MySQL). Keep settings that change
  name resolution, such as `search_path`, in the connection string: after a `SET`, the scope would hold another
  schema's names. Each distinct `Application Name` or timeout value is its own scope.
- **SQLite:** `:memory:`, `Mode=Memory` (shared cache included) and an empty data source get an unregistered scope per
  provider instance. A provider created per operation on such a database repeats discovery each time.
- **First cache use of each new built-in provider instance** parses its connection string with the provider's builder
  and, unless the scope is per-instance, computes a SHA-256.
- **SQLite's SQL uses the database's spelling of convention-mapped columns** (`"country_0".name`, where 3.9.0 emitted
  `.Name`).
- **A custom SQLite dialect's `EncloseIdentifier` must leave an already-enclosed name unchanged** (`E(E(x)) = E(x)`).
  SQLite passes cached, enclosed names through it again; a dialect that wraps unconditionally emits `[[Id]]` and fails.
  SQLite's mapper strips a matching `"…"`, `[…]` or backtick pair from a cached name.
- **Binary-breaking for `OrmDataProvider` subclasses:**
  - removed: `_tableNames`, `_columnNames`, `_mappedTypes`, `_unmappedPropertiesCache`, and SQL Server's
    `GetColumnOrdinals`;
  - added: protected `TableNameCache`, `ColumnNameCache`, `UnmappedPropertyCache`, `MappedTypes`,
    `EntityMapperCache` and `ProcedureNameCache`, plus `protected virtual` `CacheScopeIdentity` and
    `CacheScopeDialectType`;
  - providers' `GetUnmappedProperties<T>(Type)` are instance methods.

  A direct subclass that doesn't override `CacheScopeIdentity` gets one scope per provider type.
- **`ToDictionaryKey()`** returns `{DeclaringType.FullName}.{Name}`.
- PostgreSql, MySql and Sqlite grant `InternalsVisibleTo` to `Funcular.Data.Orm.SqlServer.Tests`.
- On SQL Server, MySQL and PostgreSQL, when a delete inside a transaction is a type's first use and the type's table
  doesn't exist, the provider's missing-table error (SQL Server 208, MySQL 1146, PostgreSQL 42P01) is now the
  `InnerException` of an `InvalidOperationException`. Some of these calls threw the provider's exception directly
  before.
- **On the netstandard2.0 and .NET Framework 4.8 builds, `Delete`/`DeleteAsync` by predicate check the WHERE clause
  for trivial conditions case-insensitively**, as the .NET 8 build already did. This follows from the
  `GeneralExtensions.Contains` fix. A WHERE clause containing `true` in any letter case is rejected with "Delete
  operation requires a non-trivial WHERE clause."; on those builds it was rejected before only in lower case. That
  includes a predicate on a column such as `TrueUpAmount`. The WHERE clause also names the table for most members
  (not inside a date part such as `.Year`), so most predicate deletes on a table such as `TrueUpLedger` are rejected
  too.

### Known issues (fixes planned for 3.10.1)
Aggregates keep their 3.9 behavior in 3.10.0:
- `Average` of whole numbers truncates on SQL Server (`AVG` over an `int` column), and loses precision on MySQL
  and SQLite.
- `Average` over a `decimal` or `float` column throws.
- `Min`/`Max`/`Average` over a nullable column on an empty set throw instead of returning `null`.
- Some `Min`/`Max` result types throw after the round-trip.

## [3.9.0] - 2026-07-06

### Added
- **Top-level scalar projection: `Query<T>().Select(x => x.Member)` now returns `List<memberType>`.** Previously only `Select(x => new T { … })` (a same-entity column subset) was translated; the natural scalar spelling threw `NotSupportedException`. It now emits the **narrow `SELECT`** for that single member (as if `new T { Member = x.Member }`), materializes `T`, then projects the member in memory — so `Select(x => x.Id)` yields `List<int>`. Composes with `Where` + `OrderBy` (including a `[RemoteProperty]` join column) + `Skip`/`Take`, deferring the unprojected wide columns until after the Top-N (the performant call-list pattern: filter/order/page by a joined column, read back only the key). The member may be an own column or a self-contained computed attribute (`[JsonPath]`/`[SqlExpression]`/`[SubqueryAggregate]`). Applied in all four providers.
- **The scalar `Select` must be the outermost operator.** A lambda-bearing operator applied **after** it (`Where`/`OrderBy`/`ThenBy`/`All`/`Any(pred)`/`Count(pred)`/`First(pred)`/chained `Select`) — or a reducing terminal (`Count`/`First`/`Sum`/`ElementAt`/…) — throws a clear `NotSupportedException`; the projected sequence is no longer the entity. Filter/order/page **before** the projection, or materialize and compose in memory. Constant-arg operators (`Take`/`Skip`/`Distinct`) after it still compose.
- **Still not translated** (clear `NotSupportedException`): top-level projection to an **anonymous type** or a **different DTO**, an **identity** `Select(x => x)`, a **computed expression**, and a scalar of a **`[RemoteProperty]` value** (order/filter by the remote column and project the key instead). Reshape those in memory: `query.ToList().Select(...)`.

## [3.8.5-beta1] - 2026-07-02

### Changed
- **The explicit `[RemoteProperty(typeof(X), …)]` target is now authoritative on the final hop.** In an *explicit* remote path, `ResolveExplicit` resolved every foreign-key hop by name inference and only *validated* the declared target type at the end — so a single-hop remote whose FK property name didn't convention-match the target (e.g. `[RemoteProperty(typeof(Call), nameof(CallRefId), nameof(Call.Uid))]`) threw `PathNotFoundException`. Now the FK that lands on the target may be named anything; the explicit `typeof(...)` wins for that final hop. Only *intermediate* FKs in a multi-hop path still convention-match or carry `[RemoteLink]`. Backward-compatible by construction — existing working paths (whose last hop already equals the declared target) are byte-for-byte unchanged. All four providers.

### Documentation
- **Documented the narrow-projection idiom.** `Query<T>().Select(x => new T { Key = x.Key })` emits a **narrow `SELECT`** of just the projected column(s) and composes with `Where` + `OrderBy` (including a `[RemoteProperty]` join column) + `Skip`/`Take` — deferring the unprojected computed/remote columns until after the Top-N. This is the supported, performant way to project a column subset while filtering/ordering/paging by *any* mapped column (e.g. a call-list page ordered by a joined column, returning only the id). Added to `Advanced.md` / `FUNKYORM_AI_ADVANCED.md` and locked with a regression test. (Top-level `Select(x => x.Scalar)` / anonymous / other-DTO remain unsupported — see the projection section.)

## [3.8.4-beta1] - 2026-07-02

### Fixed
- **Regression from 3.8.3: a `[RemoteProperty]`/`[RemoteKey]` whose remote *value* column is declared on a base class of the target entity threw `Invalid object name '<basetype>'`.** 3.8.3's cold-cache fix discovered the remote target's schema eagerly at join-resolution time, but keyed that discovery off the value property's `DeclaringType`. When the value column is inherited (e.g. `Uid` declared on `EntityBase`, with `Call : EntityBase`), `DeclaringType` is the base class — which has no `[Table]` — so discovery ran `SELECT * FROM entitybase` and failed at `Query<T>()`/`FindAsync<T>()` time (deterministic; not helped by priming). Fixed in all four providers by discovering the concrete `[Table]`-annotated target entity type (the type passed to the attribute) instead of the value property's declaring type — the target table already contains inherited columns physically. Join-key resolution was never affected (only the value-column discovery step). Inherited-remote-value-column regression test added per provider.

## [3.8.3-beta1] - 2026-07-01

### Fixed
- **Cold-cache: a `[RemoteProperty]`/`[RemoteKey]` target column resolved to a naive, unqualified property name until that target type was materialized elsewhere in the process.** Resolving a remote join fell back to `property.Name.ToLowerInvariant()` (e.g. `displayname`, `calldate`) — no snake_case conversion — for the remote target's column whenever that target type's schema hadn't yet been discovered. This surfaced in **every** clause the remote column appears in (SELECT / WHERE / ORDER BY / aggregate): from a *cold* process, `Query<WideDto>().ToList()` (and the filter/aggregate paths) threw `Invalid column name 'displayname'`; priming the target type first (`Query<Target>().Take(1).ToList()`) masked it. Root cause: the target type's column mapping was only populated on first materialization of that type. Fixed by discovering the schema of every table in the remote path (and the target property's declaring type) **deterministically at join-resolution time** in `ResolveRemoteJoins`, before columns are resolved. That single point feeds SELECT, WHERE, ORDER BY, and the aggregate path, so all clauses now emit the real `alias.snake_case_column` regardless of cache-priming order. Affected the three providers that infer remote-column names from the DB schema — **SQL Server, PostgreSQL, and MySQL**. **SQLite** resolves columns from explicit `[Column]` attributes (no schema-inference fallback) and was not affected; the same discovery step is applied there for cross-provider consistency (a no-op).

### Changed
- **`GroupBy(...)` now throws a clear `NotSupportedException`** instead of an obscure `InvalidCastException` at materialization. `GroupBy` is not translated to SQL; the guard points you to group in memory (`query.ToList().GroupBy(...)`). Applied in all four providers (same class of fix as the top-level `Select` guard below).
- **A top-level `Select(...)` projecting to a type other than the queried entity now throws a clear `NotSupportedException`.** `Query<T>().Select(x => x.ScalarColumn)` — or a projection to an anonymous type / a different DTO — previously materialized `T` and then failed at the result path with an obscure `InvalidCastException` ("Unable to cast `T` to `IEnumerable<…>`"). FunkyORM materializes the source entity `T`; only `Select(x => new T { … })` (the same entity, a column subset) is supported. The guard fails fast with a message pointing you to either project to the same entity, or materialize first and project in memory (`query.ToList().Select(...)`). Applied in all four providers.

## [3.8.2-beta1] - 2026-07-01

### Fixed
- **Aggregates (`Count`/`Any`/`All`/`Sum`/`Min`/`Max`/`Average`) filtered by a `[RemoteProperty]`/`[RemoteKey]` failed at the engine.** `Query<T>().Where(r => r.RemoteProp == x).Count()` (or `Count(r => r.RemoteProp == x)`) built `SELECT COUNT(*) FROM {table}` (and the `EXISTS`/numeric equivalents) **without** the `LEFT JOIN`s the WHERE needs, so the predicate referenced an undefined join alias (SQL Server `4104 "multi-part identifier … could not be bound"`; PostgreSQL "missing FROM-clause entry"). `BuildAggregateClause` resolved the remote joins but kept only their column map and discarded `JoinClauses`. Fixed in all four providers by injecting the remote joins into the aggregate's `FROM` **when the WHERE references a remote column**. Pre-existing defect (predates 3.8.x). `[JsonPath]`/`[SqlExpression]`/`[SubqueryAggregate]` filters were unaffected (self-contained, no join).
- **Numeric-aggregate selector is now base-table-qualified** (`SUM(person.id)` rather than `SUM(id)`). Once remote joins are present, a bare column is ambiguous against joined tables that share the name — SQL Server errored (`Ambiguous column name`) and SQLite silently bound to the wrong table. No effect on join-free entities.

### Limitations
- **Reverse (one-to-many) `[RemoteKey]`/`[RemoteProperty]` filters on `Count`/`All`/`Sum`/`Average` throw `NotSupportedException`.** A *reverse* remote join (joining on a child's foreign key rather than the target's primary key — e.g. `Country ← Address ← PersonAddress → Person`) fans the base rows out one-per-child, which would inflate those aggregates. Rather than return a silently-wrong number, FunkyORM throws a clear exception directing you to **materialize and aggregate in memory** (`query.Where(...).ToList().Count()` / `.Sum(...)`). Unaffected: forward (many-to-one) remote filters; `Any`/`Min`/`Max` over reverse joins (fan-out-safe); and any aggregate that doesn't filter on a remote column.

## [3.8.1-beta1] - 2026-06-30

> **Gap closure.** Ordering and `DISTINCT` were the remaining places where the "view-replacing" / remote attributes weren't yet first-class. This rounds out the query surface that already supported them in `Where(...)` predicates (3.5.1) and projections. Ships beta while 3.8.0's RLS is still in beta.

### Added
- **`OrderBy` / `OrderByDescending` / `ThenBy` / `ThenByDescending` on computed & remote attributes.** Ordering by a `[JsonPath]`, `[SqlExpression]`, `[SubqueryAggregate]`, or `[RemoteProperty]`/`[RemoteKey]` property now sorts by the *resolved* SQL fragment (the JSON accessor, the expression, the correlated subquery, or the joined `alias.column`) instead of erroring on a non-existent base-table column. The whole-entity `Query<T>()…OrderBy(…)` path is fully supported across all four providers. The required joins are already emitted by the SELECT for any entity declaring these attributes, so ordering reuses them (aliases are deterministic).
- **`Distinct()` support.** A `Distinct()` in the LINQ chain emits `SELECT DISTINCT …`, and composes with `Where`, `Select`, computed attributes, and `OrderBy`.
- **Projecting self-contained computed attributes in a custom `.Select(...)`.** `[JsonPath]`, `[SqlExpression]`, and `[SubqueryAggregate]` can now be referenced in a custom projection — `Select(p => new T { Computed = p.Computed })` emits the resolved SQL aliased as the property and materializes it back onto the entity. (`[RemoteProperty]`/`[RemoteKey]` remain unsupported in projections — see Limitations.)

### Changed
- The per-provider `OrderByClauseVisitor` now resolves each ordering member through the remote-join `PropertyToColumnMap` first (falling back to the plain column), and treats a map-resolvable property as orderable even though it is not a base-table column.

### Limitations / guards (intentional, with clear errors)
- **`Distinct().Count()`** (any aggregate after `Distinct`) throws `NotSupportedException` — postponed to a later cut; count client-side or drop `Distinct` for now.
- **`Distinct()` + `OrderBy(x => x.NotProjected)` under a custom `.Select(...)`** throws `InvalidOperationException` naming the offending key — SQL requires every `ORDER BY` key to be in the `SELECT DISTINCT` list. (Full-entity `Distinct()` is unaffected.)
- **`[RemoteProperty]`/`[RemoteKey]` cannot be projected in a custom `.Select(...)`** — they resolve to a joined `alias.column` that a custom projection's `FROM` doesn't carry, so projecting one throws `NotSupportedException` with a clear message. Query the whole entity, or use a detail class that declares it. (The self-contained `[JsonPath]`/`[SqlExpression]`/`[SubqueryAggregate]` project fine — see Added.)
- **PostgreSQL + full-entity `Distinct()` on an entity declaring `[JsonCollection]`** errors at the engine (`42883: could not identify an equality operator for type json`). `[JsonCollection]` emits `json_agg(row_to_json(...))`, which is `json`-typed, and PostgreSQL has no equality for `json` (only `jsonb`) — so `SELECT DISTINCT *` can't compare it. FunkyORM emits correct SQL; the engine rejects it. (`[JsonPath]`/`jsonb` columns are not the trigger.) Remedy: `Distinct()` a column projection excluding the `[JsonCollection]` columns. SQL Server, MySQL, and SQLite are unaffected.

## [3.8.0-beta1] - 2026-06-30

> **Beta:** Row-Level Security & audit context ship as a **beta** feature in this prerelease. The rest of the library is stable; this API may be refined before the 3.8.0 GA based on feedback.

### Added
- **Per-request session context for Row-Level Security & audit attribution.** When an app authenticates to the database as a single identity (e.g. a managed identity), FunkyORM can attach the *end-user's* identity to every command by priming caller-defined session-context keys onto the exact connection each command uses — so an RLS predicate can filter by it and audit logs can attribute it. New Core types: `FunkyAuditContext`, `SessionContextEntry`, `IAuditContextAccessor`, `AuditContextOptions`; the app implements the accessor over its own `AsyncLocal`, and the provider's `AuditContext` option (typically set per provider by an ORM factory) controls it. The capability is **generic** — FunkyORM is agnostic about key names/meaning.
  - **Prime-once-per-connection**: primed on each pooled connection at open (and once per transaction), so immutable (`read_only`) keys are never re-set; runs on the scope's own connection (no nested scope).
  - **Opt-in fail-closed**: `RequireAuditContext = true` throws when no context is present (for PHI providers); lenient providers prime opportunistically. Internal bootstrap queries (schema discovery, table-name resolution) run under a system context exempt from fail-closed.
  - **Self-attributing audit comment**: an optional `/* funky:audit sub=… corr=… */` prefix on text commands (opaque identifiers only; validated to a safe charset — no PII, no comment escape).
- **Capability-based per provider:**
  - **SQL Server** — `sp_set_session_context` (+ `@read_only`); RLS via `SESSION_CONTEXT(key)`.
  - **PostgreSQL** — `set_config`; RLS via `current_setting(key)`. Keys must be dot-namespaced (PostgreSQL requirement) — FunkyORM passes keys through verbatim and throws a clear error otherwise (it does not impose a namespace). Superusers bypass RLS.
  - **MySQL** — session user variables (`SET @Key`) for **attribution only** (no native RLS); requires `AllowUserVariables=true`; keys must be `[A-Za-z0-9_]`.
  - **SQLite** — no-op; a `RequireAuditContext` provider throws (no isolation model to enforce).
- Integration + unit tests across all providers (SQL Server incl. transaction prime-once and concurrency no-bleed; PostgreSQL RLS enforced via a non-superuser role; MySQL attribution; SQLite guard) plus Core unit tests; `rls_demo` table/policy and probe DDL added to the test databases.

### Changed
- `OrmDataProvider` gained an `AuditContext` option and audit-resolution helpers; each provider's `ConnectionScope` primes the connection. No behavior change when the feature is disabled (the default): priming, the comment, and the guards are all no-ops.

## [3.7.0] - 2026-06-23

### Added
- **Stored procedure execution.** New `ExecProcedure<T>`, `ExecScalar<TResult>`, and `ExecNonQuery` methods (each with async counterparts) on `IOrmDataProvider`, for result-set, scalar, and non-query procedures. Parameters may be passed as an anonymous/typed object (input-only) or as `params SqlParam[]` (supports output/INOUT parameters; `(name, value)` tuples convert implicitly to `SqlParam`). Result sets map through the same `Query<T>` mapping pipeline (column/convention/`[Column]` aware), and procedure names resolve by explicit argument, the new `[Procedure]` attribute, or convention (cached).
- **Capability-based per provider:**
  - **SQL Server** — full support (`CommandType.StoredProcedure`; `OUTPUT` parameters; `sys.procedures` convention lookup).
  - **MySQL** — full support (`CALL` via `CommandType.StoredProcedure`; `OUT`/`INOUT` parameters; `information_schema.routines` lookup).
  - **PostgreSQL** — partial: `ExecNonQuery`/`ExecScalar` via `CALL` (INOUT parameters returned as the result row and back-populated); `ExecProcedure<T>` throws `NotSupportedException` with guidance (use a `FUNCTION RETURNS TABLE`), because `CALL` does not return result sets.
  - **SQLite** — not supported; every `Exec*` throws `NotSupportedException` (SQLite has no stored procedures).
- `SqlParam` type and `[Procedure]` attribute in `Funcular.Data.Orm.Core`.
- Integration tests across all four providers (SQL Server 22, MySQL 22, PostgreSQL 7, SQLite 6 negative) plus 17 overload-resolution/guard unit tests; procedure DDL added to each provider's `Database/**/integration_test_db.sql`.

### Changed
- **`IOrmDataProvider` gained the `Exec*` members.** `OrmDataProvider` supplies virtual implementations (throwing by default; providers override), so code deriving from `OrmDataProvider` is unaffected. This is a **source-breaking change only for external code that implements `IOrmDataProvider` directly** — such implementors must add the new members. Consistent with prior interface evolution (`ISqlDialect` in 3.2.1).
- The transactional-concurrency guard message (3.6.1) is unchanged here.

## [3.6.1] - 2026-06-22

### Fixed
- **`Update` / `UpdateAsync` inside a transaction** no longer throw *"A concurrent operation is already using the transactional connection."* The read-before-write step opened a second `ConnectionScope`, which the per-connection transactional guard rejected as concurrent access even though the read is strictly sequential. The existing row is now read on the transaction's own open connection, so updates work normally inside a `BeginTransaction()` scope. Fixed identically across all four providers (SQL Server, PostgreSQL, SQLite, MySQL). `Insert`/`InsertAsync`/`Delete` were never affected.

### Added
- Regression tests `Update_WithinTransaction_*` / `UpdateAsync_WithinTransaction_*` in every provider's integration suite, covering update inside a transaction for both the sync and async paths.

### Changed
- The transactional-concurrency guard's exception message now also notes that the error can arise from re-entrant (nested) single-threaded use — not only from `Task.WhenAll`/concurrent access — so that class of cause is discoverable from the message alone. Additive wording; the original guidance is unchanged.

## [3.6.0] - 2026-06-10

### Added
- **🐬 MySQL Provider**: New `MySqlOrmDataProvider` (project `Funcular.Data.Orm.MySql`), bundled into the single `Funcular.Data.Orm` NuGet package alongside the SQL Server, PostgreSQL, and SQLite providers (`net8.0` + `netstandard2.0`).
  - Built on the MIT-licensed **MySqlConnector** driver (chosen over Oracle's GPL `MySql.Data`).
  - Full LINQ-to-SQL translation with MySQL syntax: backtick identifier quoting, `AUTO_INCREMENT` identity retrieved via `LAST_INSERT_ID()` (MySQL has no `RETURNING` clause), `LIMIT`/`OFFSET` paging, `CONCAT`-based string operations, and `EXTRACT()` date parts.
  - Complete `[RemoteKey]` / `[RemoteProperty]` / `[RemoteLink]` support with automatic `LEFT JOIN` generation, plus all four "view-replacing" attributes against native MySQL `JSON`: `[JsonPath]` (`JSON_UNQUOTE(JSON_EXTRACT(...))`, including WHERE predicates and the 3.5.1 method-call fix), `[SqlExpression]`, `[SubqueryAggregate]`, and `[JsonCollection]` (`JSON_ARRAYAGG` / `JSON_OBJECT`).
  - Reserved-word quoting, MySQL error-code mapping (`MySqlException.Number`, e.g. 1146/1062/1452), Guid storage as `CHAR(36)` (`GuidFormat=Char36`), and the concurrency-safe connection model shared with the other providers.
  - `MySqlStringComparison` enum — default `CaseInsensitive` (matching MySQL's default `_ci` collations and SQL Server); opt-in `CaseSensitive` applies `COLLATE utf8mb4_bin`.
  - **37 dialect + integration tests** (CRUD sync/async, LINQ translation, paging, aggregates, remote keys/properties, JsonPath SELECT/WHERE, computed attributes, Guid/non-identity PKs, reserved-word quoting) — green against MySQL 8.0 both locally and in CI.
  - GitHub Actions CI workflow using a MySQL 8.0 service container; `Database/MySql/` DDL + `docker-compose.yml` for local setup.

### Changed
- **Version**: All projects promoted from `3.6.0-beta1` to `3.6.0`.
- **`ISqlDialect`**: Added the MySQL implementation (`MySqlDialect`) with `ProviderName = "mysql"`, backtick `EncloseIdentifier`, the `LAST_INSERT_ID()` insert strategy, and MySQL JSON value/collection builders.

### Notes
- MySQL's default `_ci` collations make string comparisons case-insensitive (matching SQL Server), so no case-folding workaround is emitted. Table-name case sensitivity follows the server's `lower_case_table_names`; the test DDL uses lowercase table names for cross-platform portability.

## [3.5.1] - 2026-06-09

### Fixed
- **`[JsonPath]` honored in WHERE predicates for method-call expressions**: The `WhereClauseVisitor.VisitMethodCall` delegate (used for `Contains`, `StartsWith`, `EndsWith`, and `IN` clauses) bypassed the remote-property map and emitted plain column names instead of JSON accessor expressions. Fixed across all three providers (SQL Server, PostgreSQL, SQLite) so JSON-extracted properties filter correctly in these predicates, including underscore-separated paths (e.g. `$.risk_level`).
- **Aggregate query paths resolve remote joins**: `SqlLinqQueryProvider` aggregate operators (`Any`/`All`/`Count`) created a `WhereClauseVisitor` without resolving remote joins, so `[JsonPath]`/remote properties were not honored inside aggregate predicates. These paths now resolve remote joins before visiting the predicate.

### Added
- Integration tests for JsonPath WHERE predicates across all three providers: equality, inequality, comparison, `IS NULL`, string `Contains`, and collection `Contains` — including underscore-separated JSON paths.

### Changed
- **Documentation reorganized** into a `docs/` hierarchy (`docs/plans`, `docs/ai-instructions`, `docs/architecture`). Provider `.csproj` files now reference the canonical `docs/ai-instructions/` location for NuGet `contentFiles`.
- **Version**: All projects promoted from `3.5.1-beta1` to `3.5.1`.

## [3.5.0] - 2026-06-18

### Added
- **SQLite Provider**: Full SQLite support including LINQ query translation, CASE/conditional projections, paging, identity and non-identity inserts, transactions, async operations, reserved-word handling, and file-path resolution for connection strings.
- **77 SQLite integration tests** with parity coverage matching SQL Server where supported.
- **AI instruction documents**: `FUNKYORM_AI_INSTRUCTIONS_SQLITE.md` for SQLite-specific guidance.

### Changed
- **Package architecture (Option D)**: `Funcular.Data.Orm` remains the single published NuGet package and now bundles SQL Server, PostgreSQL, and SQLite provider assemblies for `net8.0` and `netstandard2.0` targets.
- **Project rename**: The SQL Server provider project file has been renamed from `Funcular.Data.Orm.csproj` to `Funcular.Data.Orm.SqlServer.csproj` to align with the naming conventions of the PostgreSQL and SQLite provider projects. The published package identity (`Funcular.Data.Orm`), assembly name, and root namespace are unchanged — this is a source-level organizational change only and does not affect consumers.
- **SQLite CASE projection fix**: The `SqliteLinqQueryProvider` now correctly wires the `SqliteSelectClauseVisitor` output into the final query, enabling `Select()` projections with conditional/ternary expressions.
- **SQLite null comparison fix**: `SqliteSelectClauseVisitor.VisitBinary` now emits `IS NULL` / `IS NOT NULL` instead of `= NULL` / `!= NULL`.
- **Version**: All projects bumped to `3.5.0`.

### Fixed
- Two previously-ignored SQLite tests (`Query_SelectWithSalutationProjection_GeneratesCaseStatement`, `Query_SelectWithIsTwentyOneOrOverProjection_GeneratesCaseStatement`) now pass and are no longer skipped.

## [3.2.1] - 2026-06-11

This release completes the suite of "view-replacing" attributes, allowing users to build rich, read-only detail entities entirely through property-level decoration — no SQL views, stored procedures, or raw SQL needed. With `[JsonPath]`, `[SqlExpression]`, `[SubqueryAggregate]`, and `[JsonCollection]` now fully operational alongside the existing `[RemoteKey]` and `[RemoteProperty]` attributes, a single detail entity can simultaneously: extract typed scalars from JSON columns, compute expressions across peer columns (with provider-specific overrides), aggregate child tables via correlated subqueries (counts, sums, averages, and conditional counts), and project child record sets as inline JSON arrays. All of these attribute-driven columns participate in standard LINQ queries, including WHERE-clause filtering, enabling complex reporting projections without leaving the type-safe LINQ surface.

### Added
- **Timestamp / DatabaseGenerated column exclusion**: Properties decorated with `[Timestamp]` or `[DatabaseGenerated(DatabaseGeneratedOption.Computed)]` / `[DatabaseGenerated(DatabaseGeneratedOption.Identity)]` are now automatically excluded from INSERT and UPDATE statements. Fixes `Cannot insert an explicit value into a timestamp column` errors for entities with `rowversion` columns.
- **`[SqlExpression]` Attribute (Phase 2)**: Declare raw SQL expressions for computed properties using `{PropertyName}` tokens.
  - Tokens are resolved to fully qualified column references at query time, respecting naming conventions, `[Column]` overrides, and table aliases.
  - Supports provider-specific overrides via dual-expression constructor (`mssql:`, `postgresql:`).
  - Works in `Get<T>`, `Query<T>`, `GetList<T>`, and WHERE clauses.
- **`[SubqueryAggregate]` Attribute (Phase 3)**: Generates correlated scalar subqueries in the SELECT list.
  - Supports `AggregateFunction.Count`, `.Sum`, `.Avg`, and `.ConditionalCount`.
  - Conditional aggregates accept `ConditionColumn` and `ConditionValue` for `WHERE column = 'value'` filters within the subquery.
  - Portable across SQL Server and PostgreSQL (same correlated subquery syntax).
- **`[JsonCollection]` Attribute (Phase 4)**: Projects child records as JSON arrays.
  - **SQL Server**: `(SELECT ... FOR JSON PATH)`.
  - **PostgreSQL**: `(SELECT json_agg(row_to_json(sub)) FROM (...) sub)`.
  - Accepts `Columns` (property names to include), `OrderBy`, and resolves all names to database column names.
- **`AggregateFunction` Enum**: `Count`, `Sum`, `Avg`, `ConditionalCount` — used by `[SubqueryAggregate]`.
- **52 new integration tests** — full parity across both providers:
  - **SQL Server**: 8 JsonPath + 15 Computed Attribute + 3 Timestamp/RowVersion tests (26 total).
  - **PostgreSQL**: 8 JsonPath + 15 Computed Attribute + 3 DatabaseGenerated/Computed tests (26 total).
  - Covers: string/int/nested JSON extraction, NULL metadata, WHERE clauses on JSON values, `COALESCE` expressions, `COUNT`/`ConditionalCount` aggregates, zero-count edge cases, `FOR JSON PATH`/`json_agg` collection projection, combined all-attributes-on-one-entity, multi-row queries, and Timestamp/DatabaseGenerated column exclusion from INSERT/UPDATE.

### Improved
- **Primary key error message**: When no primary key is found for an entity, the exception now provides actionable guidance including the expected attribute (`[Key]` from `System.ComponentModel.DataAnnotations`) and naming conventions.
- **Documentation**: Added explicit guidance in Usage.md that all mapping attributes must come from `System.ComponentModel.DataAnnotations` / `System.ComponentModel.DataAnnotations.Schema` namespaces. Added Timestamp/RowVersion column handling section.

### Changed
- **Version**: All projects bumped to `3.2.1`.
- **`ISqlDialect`**: Added `ProviderName` property, `BuildScalarSubquery()`, and `BuildJsonCollectionSubquery()` methods.
- **`ResolveRemoteJoins<T>`** (both providers): Now detects `[SqlExpression]`, `[SubqueryAggregate]`, and `[JsonCollection]` attributes in addition to `[JsonPath]` and `[RemoteProperty]`/`[RemoteKey]`.
- **`BuildQueryComponents`** (both `SqlLinqQueryProvider` and `PostgreSqlLinqQueryProvider`): Fixed SQL corruption when SELECT list contains subqueries (e.g., `(SELECT COUNT(*) FROM ...)`). The `FROM`/`WHERE` keyword splitting now uses parenthesis-depth tracking (`FindOuterKeywordIndex`) to find only outer-level keywords, preventing subquery expressions from being incorrectly split.

## [3.2.0-beta1] - 2026-04-15

### Added
- **JSON Column Querying**: New `[JsonPath]` attribute enables extracting scalar values from JSON columns without SQL views.
  - Decorate a property with `[JsonPath("column", "$.path")]` to extract a value from a JSON column on the same table.
  - Supports nested paths (e.g., `$.client.name`) and typed extraction via optional `SqlType` property (e.g., `SqlType = "int"`).
  - Extracted values work in `Get<T>`, `Query<T>`, `GetList<T>`, and **WHERE clauses** — filter on JSON values using standard LINQ.
  - Full SQL Server and PostgreSQL support via `ISqlDialect.BuildJsonValueExpression()`:
    - **SQL Server**: `JSON_VALUE(column, '$.path')` with `CAST()` for typed extraction.
    - **PostgreSQL**: `column #>> '{path}' ` with `::type` casting.
  - Follows the established "Detail" entity pattern — add `[JsonPath]` to inherited Detail classes, not canonical entities.
- **Integration Test Schema**: Added `project`, `project_category`, `project_milestone`, and `project_note` tables with JSON metadata column for integration testing.
- **`vw_project_scorecard`**: Demonstration SQL view exercising all planned JSON capability categories.
- **Attribute Roadmap Documented**: Full design and examples for three additional planned attributes:
  - `[SqlExpression]` — computed/expression columns (`COALESCE`, `CONCAT`, `CASE`) with `{PropertyName}` token resolution.
  - `[SubqueryAggregate]` — correlated scalar subqueries (`COUNT`, `SUM`, conditional aggregates) replacing `OUTER APPLY`.
  - `[JsonCollection]` — project child records as JSON arrays (`FOR JSON PATH` / `json_agg`).
  - All four attributes documented in FUNKYORM_AI_INSTRUCTIONS.md, Usage.md, README.md, and AI_ARCHITECTURE_AND_DESIGN.md.

### Changed
- **Version**: All projects bumped to `3.2.0-beta1`.
- **`ISqlDialect`**: Added `BuildJsonValueExpression(qualifiedColumn, jsonPath, castType)` method.
- **`ResolveRemoteJoins<T>`**: Now detects `[JsonPath]` attributes alongside `[RemoteProperty]`/`[RemoteKey]`, appending JSON extraction expressions to `ExtraColumns` and `PropertyToColumnMap`.
- **`CreateGetOneOrSelectCommandText<T>`**: Updated to handle extra columns (from JSON extraction) even when no JOINs are present.
- **AI Instructions Renamed**: `COPILOT_INSTRUCTIONS.md` renamed to `FUNKYORM_AI_INSTRUCTIONS.md` (SQL Server) and `FUNKYORM_AI_INSTRUCTIONS_POSTGRESQL.md` (PostgreSQL) for agent-agnostic naming and collision avoidance when shared across projects. Now packed into both NuGet packages.

## [3.1.0] - 2026-04-09

### Added
- **🐘 PostgreSQL Support**: New `PostgreSqlOrmDataProvider` providing a full PostgreSQL provider with feature parity to the SQL Server provider. Both providers are included in the `Funcular.Data.Orm` package.
  - Full LINQ-to-SQL translation with PostgreSQL syntax (`LIMIT`/`OFFSET`, `RETURNING`, `EXTRACT()`, `||` string concat, `"double-quote"` identifier quoting).
  - Complete `[RemoteKey]`, `[RemoteProperty]`, and `[RemoteLink]` attribute support with BFS path resolution and automatic `LEFT JOIN` generation.
  - Npgsql 9.x for `net8.0`, Npgsql 8.x for `netstandard2.0`.
  - Reserved word quoting for PostgreSQL keywords (tables and columns).
  - 232 integration tests covering all CRUD, query, aggregate, transaction, remote property, and reserved word scenarios.
  - Docker Compose setup and CI workflow for PostgreSQL integration tests.

### Changed
- **Multi-Provider Architecture**: Both SQL Server and PostgreSQL providers now inherit from the shared `OrmDataProvider` base class via `Funcular.Data.Orm.Core`. Entity classes and LINQ query code are fully portable between providers.
- **Documentation**: README.md, Usage.md, and COPILOT_INSTRUCTIONS updated to cover both SQL Server and PostgreSQL providers.

### Fixed
- **Column Name Cache Key Mismatch**: Fixed a bug where `DiscoverColumns` and `GetCachedColumnName` used different dictionary key formats (`TypeName.Property` vs `FullTypeName.Property`), causing discovered column names (including reserved word quoting) to be ignored in favor of raw attribute values.
- **GetTableName Quoting**: The PostgreSQL provider now overrides `GetTableName<T>()` to apply `EncloseIdentifier` for reserved word table names.

## [2.3.1] - 2025-12-08

### Fixed
- **Parameter Naming in Chained Queries**: Resolved an issue where chained `Where` clauses with array `Contains` would reuse parameter names (e.g., `@p0`), causing SQL Server errors. Parameters now use unique names like `@p__linq__0`, `@p__linq__1`, etc., ensuring correct execution of complex queries.

## [2.2.0] - 2025-12-05

### Fixed
- **Span<T>.Contains Support**: Fixed an issue where `array.Contains(item)` in LINQ queries would fail due to C# 12's preference for `Span<T>.Contains` over `IEnumerable<T>.Contains`. The ORM now properly handles implicit conversions to `Span<T>` and supports both instance and extension method calls.

---

## [1.6.0]

### Fixed
- Fixed privately reported issue with predicates captured in closures not being translated correctly in some cases.

### Added
- Added CI build and integration tests using MSSQL instance locally and LocalDB in Azure.

---

## [1.5.2]

### Fixed
- Fixed #3: Unmapped properties lacking `[NotMapped]` attribute were included in some SQL statements, causing errors.

---

## [1.5.0]

### Added
- **Ternary Operator Support**: Added support for the ternary operator (`?:`) in `WHERE` clauses, projections (`SELECT`), and `ORDER BY` clauses. These are translated to SQL `CASE` statements.

---

## [1.1.2]

### Documentation
- Explicitly documented immediate vs. deferred execution behaviors of different `Query<T>` overloads.

### Changed
- Centralized Excel logging in performance tests.

---

## [1.1.1]

### Changed
- Minor improvements to integration tests, performance tests, and README.

---

## [1.1.0]

### Added
- **Delete by ID**: Added `Delete<T>(id)` method.
- **Delete Guardrails**: Added additional safety checks to `Delete<T>(predicate)` method.
- **Performance Tests**: Added Entity Framework performance comparisons and charts.

---

## [1.0.0]

### Added
- **RTM Release**: First major release.
- **Delete Methods**: Initial support for delete operations.
- **Performance Enhancements**: General performance tuning.
- **Integration Testing**: Expanded integration test coverage.

---

## [0.9.3]

### Added
- **Async Support**: Added async implementations of public methods (`GetAsync`, `InsertAsync`, etc.) with corresponding test cases.

### Changed
- Reorganized provider members for better code structure.

---

## [0.9.2] - RC-1

### Added
- **Framework Support**: Added support for .NET 4.8.
- **Feature Complete**: Declared feature complete for .NET 8 and NETSTANDARD 2.0.

### Changed
- Code cleanup and dependency compatibility updates.

---

## [0.9.1]

### Added
- **NET Standard Support**: Implemented support for .NET Standard 2.0.
- **Performance Testing**: Added performance testing for update operations.
