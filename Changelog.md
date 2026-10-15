# Changelog

All notable changes to this project will be documented in this file.

## [3.11.0] - Unreleased

WHERE translation fixes for every provider (SQL Server, PostgreSQL, MySQL, SQLite). **Upgrade recommended:** the
shapes under Fixed selected, and deleted, other rows than C# selects, without an error. One translation path serves
`Query`, `Query<T>().Where`, `Count`/`Any`/`All`, SELECT projections and `Delete`; the parity tests
(`WhereParityHarness`) assert that a predicate sends the same WHERE text and parameters under SELECT and DELETE, or
throws the same exception on every path.

**Behaviour change — one guard for every WHERE clause.** The checks that 3.10.0 ran only on `Delete(predicate)`
(`DeletePredicateGuard`) now run on every WHERE predicate, before any command is sent: `Query(predicate)`,
`Query<T>().Where(...)`, `Count`/`LongCount`/`Any`/`All` with a predicate, `Delete` and `DeleteAsync` all throw the
same exception with the same message for the same predicate. So `Query` and `Count` now reject, with
`InvalidOperationException`, shapes that returned rows in 3.10.x:
- a predicate that reads no column of the entity: `x => true`, `x => 1 < 2`, `x => someFlag`,
  `x => ids.Contains(request.Id)` ("The WHERE clause must reference at least one column of the entity.");
- a comparison of a column with itself: `x => x.Id == x.Id`, `x => !(x.Id != x.Id)`, `(long)x.Id == (long)x.Id`,
  `x.Code.ToString() == x.Code.ToString()` ("The WHERE clause cannot be a self-referencing column expression.");
- a predicate that always holds: an `||` with `true`, a captured or static `true`, a comparison of captured values
  that holds, or a `Contains` on a captured string or collection that holds — **including the optional-filter idiom
  `x => filter == null || x.Name.Contains(filter)` when `filter` is null, and the admin idiom
  `x => x.OwnerId == me || roles.Contains("admin")` for an admin** — and a negated `Contains` over an empty
  collection in an `||` (`x => x.Id == id || !ids.Contains(x.Id)` with `ids` empty) ("The WHERE clause is trivially
  true; a non-trivial WHERE clause is required."). Decide in C# and compose the query:
  `var q = provider.Query<Person>(); if (filter != null) q = q.Where(x => x.Name.Contains(filter));`;
- and, with `NotSupportedException`, a comparison (`==`/`!=`) of two strings neither of which reads the row
  (`x => x.Id == 2 || role == "admin"`): the database would compare them under its collation where C# compares them
  ordinally; compute it before the query.
The messages changed too (they no longer say "Delete operation"); code that matched the 3.10.0 text must match the new
one. The other 3.10.0 delete-only rejections (D9) are gone, because the translation they guarded is fixed in this
release (see Fixed): a search value that is a LIKE wildcard, null, empty or a property of an object you hold, and a
string `Contains` on a value that doesn't read the row, now delete exactly the rows `Query` selects; a `StartsWith`,
`EndsWith` or `ToString()` on a value that doesn't read the row is rejected by the translator with its own message,
on every path. The rationale is in docs/plans/DECISIONS-3.10.2.md (D-001, D-003, D-004).

### Fixed
- **The package now declares the drivers of every provider it bundles, at pinned minimums.** `Funcular.Data.Orm`
  ships the PostgreSQL, MySQL and SQLite providers but declared only `Microsoft.Data.SqlClient`, and the providers'
  own driver references floated (`Npgsql 9.*` / `8.0.*`, `MySqlConnector 2.*`, `Microsoft.Data.Sqlite 8.0.*`), so
  what a consumer needed was whatever had resolved when the package was built, and a consumer on an older driver got
  `FileNotFoundException: Npgsql, Version=9.0.5.0` when it first used the PostgreSQL provider; one that referenced no
  driver couldn't compile against `Microsoft.Data.Sqlite` at all. The nuspec now declares, for `net8.0` and
  `netstandard2.0`, `Npgsql` 9.0.5 (`net8.0`) / 8.0.9 (`netstandard2.0`), `MySqlConnector` 2.6.2 and
  `Microsoft.Data.Sqlite` 8.0.32, next to `Microsoft.Data.SqlClient` 6.0.0; `net48` declares `Microsoft.Data.SqlClient`
  only, as it carries SQL Server only. Each minimum is the version the provider is compiled against (the README's
  provider table lists them); none floats. `tools/consumer-smoke/` holds a script that packs the package into a local
  feed and installs it into a throwaway console app that loads every provider at exactly those minimums.
- **Aggregates follow LINQ to objects** (the 3.10.0 Known issues, promised for this release):
  - `Average` is computed as `SUM / COUNT` in C#, in the arithmetic LINQ uses for the selector's type, from the two
    values the provider reads. It no longer truncates whole numbers on SQL Server (`AVG` over an `int` column gave `1`
    for `{1, 1, 2}`), rounds to four decimals on MySQL, or rounds to ten on SQLite (`ROUND(AVG(…), 10)` is gone). SQL
    Server widens an integer sum to `BIGINT` and a `float` sum to `FLOAT`; PostgreSQL widens a `float` sum to `DOUBLE
    PRECISION`. `Average` over a `decimal`, `float` or `double` column no longer throws, and returns `decimal`,
    `float` or `double` as LINQ does.
  - An empty set gives `Sum` 0 (a nullable selector included), a nullable or reference `Min`/`Max`/`Average` `null`
    (also when every value is null), and a non-nullable one `InvalidOperationException`, as LINQ does; before, a
    nullable `Min`/`Max`/`Average` over no rows threw.
  - `Min`/`Max` results convert to the operator's result type after the round-trip (`DateTime`, `DateTime?`,
    `decimal?`, `string`, `float`, `Guid`, `DateTimeOffset`); before, some threw `InvalidCastException` or
    `NotSupportedException` ("Unsupported selector type").
- **String `Contains`, `StartsWith` and `EndsWith` treated `%`, `_` and `[` in the value as wildcards.** The value
  went into the `LIKE` pattern unescaped, so `x.Name.Contains("_")` matched every name of one character or more,
  `x.Name.Contains("%")` every name, and on SQL Server `x.Name.Contains("[x]")` every name containing `x`; on
  PostgreSQL and MySQL a backslash escaped the character after it, so `x.Name.EndsWith("\\b")` matched every name
  ending in `b`. The value is now matched literally on all four providers, written in the predicate or captured, in a
  WHERE clause and in a projection: the pattern escapes `%`, `_`, `[` and `!` with `!` and ends with `ESCAPE '!'`.
  A delete with such a value deletes the rows the query selects (3.10.0 rejected it).
- **A property's `ToString()` was dropped.** `x.Code.ToString() == "07"` compared the integer column with the text,
  which matched `7` on SQL Server, MySQL and SQLite and failed on PostgreSQL (`42883`); in a projection
  (`Label = x.Code.ToString()`) it threw `InvalidCastException` on SQL Server, PostgreSQL and MySQL. On an integer
  property (`sbyte` through `ulong`, nullable or not) it's now the column's text (`CAST(c AS NVARCHAR(20))` on SQL
  Server, `CAST(c AS TEXT)` on PostgreSQL and SQLite, `CAST(c AS CHAR)` on MySQL), `""` for null as in C#; on a
  `string` property it's the column. The text is the invariant form (`-5`), which can differ from .NET's current
  culture (`−5`, with U+2212, under fi-FI): build text you compare it with using `CultureInfo.InvariantCulture`. The
  comparison is on the cast text, so it can't use an index on the column; where that matters, parse the text in C#
  and compare the integer. Other `ToString()` calls on a property now throw (see Changed).
- **A member of a member of the row was silently dropped from the WHERE clause.** `x.PostedAt.Year > 2020` was sent
  as `posted_at > @p` (with `2020` as the value), `x.Name.Length > 3` as `name > @p`: `Delete(x => x.Name.Length > 3)`
  removed every row. `Year`, `Month` and `Day` of a `DateTime` property (nullable or not; `x.D.Year` and `x.D.Value.Year`
  alike) are now the dialect's date part, and `Length` of a `string` property its character length: `YEAR(c)` /
  `LEN(c + N'x') - 1` on SQL Server, `EXTRACT(YEAR FROM c)` / `LENGTH(c)` on PostgreSQL, `EXTRACT(YEAR FROM c)` /
  `CHAR_LENGTH(c)` on MySQL, `CAST(strftime('%Y', c) AS INTEGER)` / `LENGTH(c)` on SQLite. Every other member of a
  member throws (see Changed). In `Where`, `Count`/`Any`/`All` and `Delete`, on all four providers.
- **A collection `Contains` whose item doesn't come from the row** (`ids.Contains(request.Id)`, a nested or a
  static member) was translated as the entity's column of the same name: `x => x.Id == 3 || ids.Contains(request.Id)`
  with `ids = {1, 2}` selected, and deleted, three rows where C# selects one. A local or a constant item threw
  (`InvalidOperationException`, `NullReferenceException`). Each is now answered before the query, as C# answers it
  (with the collection's own `Contains` and comparer, and with C# 14's span-based `array.Contains`), and sent as
  `1=0` (false) or `NOT 1=0` (true). In `Where`, `Count`/`Any`/`All`, `Delete` and SELECT projections.
- **A property of a captured object used as a search value** — the enclosing object's (`this`), a nested one, a
  static one — was read as `null` or not at all: `x => x.Id == 2 || x.Name.Contains(Filter)` in an instance method
  sent `LIKE '%%'` and selected every row with a non-null name. Search values of `Contains`, `StartsWith` and
  `EndsWith` are now read as C# reads them, in a delete too (3.10.0 rejected the shape there).
- **`StartsWith`/`EndsWith` on something other than an entity property** (`request.Name.StartsWith("al")`,
  `x.Child.Name.StartsWith("a")`) were translated as the entity's `name` column, selecting the wrong rows. They now
  throw (see Changed).
- **A captured `ToString()` was sent as its raw value, without its format:** `x.Id == 2 || id.ToString() == "05"`
  compared the number 5 with `'05'` and, on SQL Server and MySQL, selected every row; `ToString("D2")` lost its
  format. `x.Code == x.Name.Length.ToString()` dropped `.Length` (`code = name`). A `ToString()` of anything but an
  entity property now throws (see Changed).
- **A string `Contains` that doesn't read the row** (`s.Contains("ZETA")`) was compared by the server, whose
  collation could find it true where C# finds it false (every row on SQL Server, MySQL and SQLite). It's now
  answered as C# answers it, except that a null string or search value matches no row (C# would throw).
- **A null search value** (`x.Name.Contains(filter)` with `filter == null`) sent `LIKE '%%'`, which matched every row
  with a non-null value. A null value for `Contains`, `StartsWith` or `EndsWith` now matches no row, negated or not
  (inside `All`, see Known issues). The optional-filter idiom `filter == null || x.Name.Contains(filter)` with a
  null `filter` is rejected as always true (see Behaviour change): build the query conditionally instead.

### Changed
- **Every WHERE predicate is checked by the shared guard** (`Funcular.Data.Orm.WherePredicateGuard`; see Behaviour
  change above for the shapes and messages). The check runs before the entity's columns are discovered, on
  `Query`/`QueryAsync`/`Delete`/`DeleteAsync` and on a LINQ `Where`, so a rejected predicate on a missing table
  reports the guard, not discovery; `Count`/`Any`/`All` run it on their predicate too. `Delete` no longer checks that
  the translated clause names a column of the table by text (a predicate on a remote property only is accepted,
  as it is in a query). `DeletePredicateGuard` and `DeletePredicateVerdict` are `[Obsolete]` and forward to the new
  class, with its messages.
- **`Min`/`Max` over a type the database can't order as .NET does throw `NotSupportedException` before any command is
  sent:** an enum (aggregate its number and convert: `Max(x => (int)x.Status)`, which translates); `TimeSpan` (time
  types differ by provider: SQL Server and PostgreSQL `time` hold values under 24 hours, MySQL `TIME` spans ±838
  hours, Npgsql maps `TimeSpan` to an unbounded `interval`; store durations as integers and aggregate those); a `Guid`
  unless the provider's `QueryOptions.GuidOrdering` is `GuidOrdering.Database` (the database's ordering differs from
  `Guid.CompareTo` on SQL Server, which compares byte groups 10–15 first, and depends on the storage format on MySQL
  and SQLite); on PostgreSQL a `Guid` is rejected whatever the option says, because PostgreSQL has no `MIN`/`MAX` over
  `uuid` (error 42883) — order by the property and read the first row's value instead
  (`OrderByDescending(x => x.Uid).Take(1).Select(x => x.Uid).ToList()`), which does follow .NET's ordering there; a
  `DateTimeOffset` on MySQL and SQLite, which have no offset-aware type (store a UTC `DateTime`); on SQL Server and
  PostgreSQL a `DateTimeOffset` orders by instant, as .NET does. The `Guid` guard doesn't cover `OrderBy`/`ThenBy`,
  whose ordering follows the database. String `Min`/`Max` follow the database's collation.
- **`ToString()` on a property is translated only on an integer or `string` property, with no argument** (see
  Fixed). On a property of any other type (`decimal`, `bool`, a date, `Guid`, an enum, `char`, …), and with any
  argument (a format string or a format provider, `CultureInfo.InvariantCulture` included, on a `string` property
  too), it now throws `NotSupportedException`: a cast to text wouldn't give .NET's text (`1.50` for a
  `DECIMAL(10,2)` holding `1.5m`, `1` for `true`). Compare the property's value instead, computed before the query:
  for a `Guid` key, `var id = Guid.Parse(idText);` and then `x => x.Id == id` (on SQLite see Known issues). Before,
  the `ToString()` was dropped and the column's value compared with the text, and some of these shapes returned C#'s
  rows; they now throw too. Among them (not a complete list): on all four providers, `ToString()` on a `char`
  property and `ToString` with an argument on a `string` property; on SQL Server, MySQL and SQLite,
  `x.Id.ToString() == idText` on a `Guid` key compared with its lower-case text, and
  `x.Code.ToString(CultureInfo.InvariantCulture) == "7"`; on SQL Server and MySQL, `x.Amount.ToString() == "1.50"`
  on a `decimal` at the column's scale; on SQL Server, `x.Flag.ToString() == "True"` on a `bool?`. On PostgreSQL a
  column that isn't text failed with `42883`; in a projection such a column threw `InvalidCastException` on SQL
  Server, PostgreSQL and MySQL, and on SQLite could give text unlike .NET's.
- **`x.N.Value.ToString()` on a nullable property throws `NullableExpressionException`**, as `x.N.Value == 7` does;
  SQL Server, MySQL and SQLite sent it as the column before. On an integer property the message says to write
  `x.N.ToString()`; on any other type, or with an argument, it says that `ToString()` isn't translated there either.
- **The SQL of a string `Contains`, `StartsWith` or `EndsWith` ends with `ESCAPE '!'`**, and its value has `%`, `_`,
  `[` and `!` prefixed by `!`. Code that reads the SQL text, such as a `Log` callback, sees the new form.
- **Any member of a member of the row other than those date parts and `Length`** now throws `NotSupportedException`
  naming the member, before any command is sent, by `Query`, `Count` and `Delete` alike: `x.D.Hour`, `x.D.Date`,
  `x.D.DayOfWeek`, a `DateTimeOffset` or `DateOnly` part, `x.Tags.Count` on a list, a member of a nested object
  (`x.Child.Id`), a member of a computed value (`(cond ? x.Name : "zz").Length`), a member of a newly constructed
  object (`new Holder().Flag`, formerly sent as `1=1` whatever the flag). Before, the outer member was dropped and
  the inner column compared instead, which returned the right rows only by coincidence (for example `x.D.Date == d`
  when every stored time is midnight). Compute the value before the query, or compare the property itself.
  `x.D.Value.Hour` keeps its earlier message ("Unsupported property Hour in nested expression."). A property of the
  entity that has no column (`[NotMapped]`) is rejected with a message naming it, where before the visitor failed with
  "Expression type Parameter is not supported".
- **Date parts are now qualified with the table name** (`YEAR(person.birthdate)` instead of `YEAR(birthdate)`), as
  every other column in a WHERE is, so a predicate on a date part no longer fails with an ambiguous column when the
  entity has remote joins. The SQL text of an existing `x.D.Value.Year` predicate changes accordingly.

Method calls in a predicate or a projection that can't be answered correctly now throw `NotSupportedException`
(unless noted) before any query runs ([Usage.md](Usage.md#the-in-clause)). Most of them failed or returned wrong rows
before:
- **A collection `Contains` whose item reads the row other than as an entity property**: `ids.Contains(x.Child.Id)`
  (3.10.1 silently used `x.Id`), `lengths.Contains(x.Name.Length)`, `longIds.Contains(x.Id)` with a `List<long>`.
  With an empty collection these returned no row; they now throw whatever the collection holds. A null collection
  and a captured non-collection type with its own `Contains` throw too (a null collection threw before as well:
  `NullReferenceException` for a local, a differently worded `NotSupportedException` for a property of an object you
  hold).
- **`.Value` on a nullable entity property inside `Contains`** (`ids.Contains(x.NullableId.Value)`) threw
  `NullableExpressionException` only when the collection had values; with an empty collection it returned no row.
  It now throws `NullableExpressionException` whatever the collection holds.
- **`StartsWith`/`EndsWith` whose receiver isn't an entity property** (a captured value, a local, a static member,
  `x.Child.Name`).
- **A `ToString()` of anything but an entity property**: a local, a constant, a property of an object you hold
  (including `this`) or a static one, with or without a format or an `IFormatProvider`; `Convert.ToString(...)`; a
  `ToString()` that reads the row through another member (`x.Name.Length.ToString()`). Before, a captured value was
  sent unconverted, which returned the right rows only when the value already was the text compared (an `object`
  or interface variable holding a string, `s.ToString(CultureInfo.InvariantCulture)`) or when it was compared with
  a column's own `ToString()` (`x.Id.ToString() == id.ToString()`; also with an enum, except on PostgreSQL, where
  that threw). Those throw now too. Compute the string before the query: `var code = id.ToString("D2");`, then
  `x.Code == code`.
- **A value that throws while the predicate is translated** (a member of a null captured object,
  `ids.Contains(request.Id)` with `request == null`; a lazy collection that throws while it is read) throws
  `NotSupportedException("Could not evaluate expression: …")` with the original exception inside. A null check in
  the same predicate (`request == null || …`) doesn't prevent it: both sides of `||` are translated. When the item
  is an entity property (`ids.Contains(x.Id)`), a database error or a `NotSupportedException` thrown while the
  collection is enumerated (a query used as the collection, say) passes through unchanged, as before. A collection
  read to answer a `Contains` before the query (an item that doesn't come from the row) is wrapped like any value.

### New public API
- `Funcular.Data.Orm.WherePredicateGuard` (`Classify`, `Validate`, `ValidateTranslation`, `HasLiteralTautology`, and
  its message constants) with `WherePredicateVerdict`: the guard every provider runs on every WHERE predicate, which
  a custom provider can call too. `DeletePredicateGuard` and `DeletePredicateVerdict` (3.10.0) are `[Obsolete]`
  forwarders.
- `Funcular.Data.Orm.Linq.RowIndependentEvaluator` (`IsRowIndependent`, `TryTranslateContains`, `TryReadValue`, and
  the SQL it sends: `TrueSql`, `FalseSql`, `UnknownSql`), which the four translators use and a custom provider can
  too. It evaluates by interpretation where the runtime has it (.NET Framework 4.7.1 and later, .NET Core) and
  compiles otherwise.
- `Funcular.Data.Orm.Linq.LikePattern` (`Escape`, `EscapeCharacter`, `EscapeClause`): the escaping every provider
  applies to a string `Contains`, `StartsWith` or `EndsWith` value.
- `OrmDataProvider.QueryOptions` (`Funcular.Data.Orm.Linq.QueryOptions`, with `GuidOrdering` of type
  `Funcular.Data.Orm.Linq.GuidOrdering`: `Rejected` or `Database`), a provider-wide option set at startup:
  `new SqlServerOrmDataProvider(conn) { QueryOptions = { GuidOrdering = GuidOrdering.Database } }`.
- `Funcular.Data.Orm.Linq.AggregateSemantics` (`EnsureMinMaxSupported`, `Average`, `ConvertResult`): LINQ to objects'
  aggregate rules, which the four LINQ query providers share and a custom provider can use.
- Each provider's expression translator gains `TranslateMemberOfMember(MemberExpression, StringBuilder,
  Func<PropertyInfo, string>)`, which the WHERE visitors call for every member of a member; `TranslateDateMember`
  is unchanged and shares its date-part SQL.

### Known issues
- **`Length` counts what the database counts.** All four providers count characters (code points), where C#'s
  `Length` counts UTF-16 code units, so a string holding a character outside the Basic Multilingual Plane (an emoji)
  is one shorter in SQL than in C# (SQL Server under a non-`_SC` collation counts as C# does). A `CHAR(n)` column's
  padding is counted or not as the database decides. Trailing spaces are counted on every provider (SQL Server's
  `LEN` would drop them; FunkyORM sends `LEN(c + N'x') - 1`).
- **The guard proves only the plain shapes.** `x => x.Id == 2 || x.Id != 2` is accepted on every path, though it
  holds for every row; a `Contains("")` on a column matches every non-null value, as in C#, and is accepted.
- **`All` counts a row whose predicate SQL finds null (unknown) as satisfying it**, as before. That's C#'s answer
  when the null makes the predicate true (`All(x => x.NullableId != 5)`), but not when it makes it false:
  `All(x => x.NullableId > 0)` returns `true` when a `NullableId` is null, where C# returns `false`. `All` over a null
  search value (`All(x => x.Name.Contains(filter))` with `filter == null`) returns `true`, where C# throws. Test the
  null yourself: `All(x => x.NullableId != null && x.NullableId > 0)`.
- **SQLite binds a `Guid` or `DateTime` in a WHERE clause in Microsoft.Data.Sqlite's format**, not the text FunkyORM
  stores (a lower-case `Guid`; `yyyy-MM-dd HH:mm:ss.fff`). So `x => x.Id == id` finds no row FunkyORM wrote when `id`
  has hex letters, and a date comparison can be off. `Get<T>(id)` finds the row. Before, `x.Id.ToString() == idText`
  happened to match on SQLite; it now throws (see Changed). Pre-existing, not yet scheduled.
- **SQLite has no `decimal` type**, so `Sum`, `Average`, `Min` and `Max` over a `decimal` property read the `REAL`
  SQLite stores and can differ from LINQ to objects in the last digits.
- The 3.10.1 Known issue (`Update` with an enum or unsigned key) still applies. The aggregate issues it listed are fixed above.

## [3.10.1] - 2026-10-12

Security release for every provider. **Upgrade recommended** from any earlier version.

### Security
- **`Get`, `GetAsync`, `Update` and `UpdateAsync` send the primary key as a command parameter.** Before, the key's
  value was written into the SQL text: unquoted on SQL Server, and quoted without escaping on PostgreSQL, MySQL and
  SQLite (`Update` reads the existing row by its key first). A key taken from user input could change the query.
  Every earlier version is affected. `Delete<T>(id)` and the LINQ paths already sent their values as parameters.

### Fixed
- **A key containing a quote** (`it's`) failed in `Get` and `Update` with a syntax error, and on MySQL a key
  containing a backslash found no row. Both now find their row, on every provider.
- **SQL Server: `Get` and `Update` with a `Guid` key, or a string key that isn't a number,** sent the key unquoted and
  failed. They now find their row.
- **An enum key given as the enum itself** (`Get<T>(Status.Active)`) was written as the enum's name and failed. It now
  finds its row, as a number or its text already did.

### Changed
- **The key is converted to the primary key's type before it is sent.** An integer key given as a `long`, a `short` or
  its text, an enum key given as a number or its text, and a `Guid` key given as its text, still find their row. A key
  that can't be converted without loss now throws `ArgumentException` (`ParamName` "key") before any query runs, and
  the exception doesn't repeat the key: for example `"two"` or `2.5` for an integer key, or text that isn't a `Guid`
  for a `Guid` key. Before, depending on the provider, such a key failed in the database or found no row. Some keys
  that found their row in 3.10.0 now throw too, for example a `char` digit (`'2'`) or text with a decimal point
  (`"2.0"`) for an integer key; pass the key as its own type.
- **A key that was wrapped in quotes** to work around this (`"'abc'"`) is now compared as written: a string key with
  its quotes finds no row, and a `Guid` key with them throws `ArgumentException`.
- **PostgreSQL:** a string key, or any key given as text, is sent untyped, as 3.10.0's literal was, so PostgreSQL still
  reads it as the key column's type: a `citext` key column compares without case, and a `timestamp` key given as text
  with a zone (`"2026-01-02T00:00:00Z"`) matches as before. Any other key is a typed parameter, as values in LINQ
  queries already are, so a `Guid` key over a `varchar` column now fails in the WHERE; reading such a row already
  failed in 3.10.0.
- **SQLite:** a `Guid` key is matched as FunkyORM stores it (lower-case text), whatever the case of the text it's
  given.
- **Provider subclasses:** `CreateGetOneOrSelectCommandText<T>(key)` is obsolete (warning CS0618): given a key it
  throws `NotSupportedException`, and given null it still builds the unfiltered SELECT, which a new parameterless
  overload also builds. `GetWhereClause<T>(key)` is obsolete and always throws. `CreateGetCommand<T>(key)` returns the
  SELECT and its key parameter, and `ConvertKeyToPrimaryKeyType` and `NormalizeKeyForBinding` (protected, on
  `OrmDataProvider`) prepare the key.

### Known issues
- The aggregate fixes listed under 3.10.0's Known issues are now planned for the next release (3.11.0).
- `Update` of an entity whose key is an enum (PostgreSQL) or an unsigned integer (SQL Server and PostgreSQL) still
  fails, as in 3.10.0: the UPDATE statement's own key parameter isn't converted yet.

## [3.10.0] - 2026-10-05

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
  case-sensitively in the current culture. It now uses the comparison it is given.
- **SQLite now uses discovered column names.** Its SELECT list and row mapper use the column discovery found, so a
  property whose column differs by underscores (`Label` → `la_bel`) is queryable.
- **`Delete<T>` and `DeleteAsync<T>`, by predicate or by id, as a type's first use in the process** could run before
  the type's columns were discovered. For example, `x => x.LastName == value` on a member mapped by convention could throw
  `NotSupportedException: Expression type Parameter is not supported`. They now discover the type's columns first.
  All four providers.
- **`ExecProcedure<T>` as a type's first use** no longer leaves `Query<T>()` and `Delete<T>(predicate)` failing for
  that type afterwards. SQL Server and MySQL.
- **`Delete`/`DeleteAsync` by predicate accepted predicates that match every row, and rejected some that don't.**
  All four providers:
  - self-comparisons (`x.FirstName == x.FirstName`, `x.Id >= x.Id`, also through `ToString()`:
    `x.Code.ToString() == x.Code.ToString()`), their negations (`!(x.Id != x.Id)`), and
    disjunctions with an always-true operand (`x.Id == 2 || true`, a captured or static `true`, a `true` property of
    a captured object, a comparison of constants and captured values that holds, such as `filter == null` with
    `filter` null, or a captured string's `Contains`, or the `ToString()` (with no argument or a format string) of a
    captured value declared as a string, number, enum or other non-generic sealed or value type of .NET's core
    library, in a comparison, that holds, such as `roles.Contains("admin")`, or a `Contains` with a null search
    value, which every provider sends as `LIKE '%%'`) could delete every row. They're now rejected before any SQL runs;
  - a disjunction with a negated `Contains` over an empty collection (`x.Id == 2 || !emptyIds.Contains(x.Id)`, its
    De Morgan form `!(emptyIds.Contains(x.Id) && x.Id == 2)`, and the same after `IS NOT NULL` or `IN (…)`) deleted
    every row. It's now rejected once the WHERE clause is built, before the DELETE runs;
  - a predicate on a table or column whose name contains `true` (a column `true_up` or `TrueUpAmount`, a table
    `TrueUpLedger`) was rejected as trivial: in any letter case on the .NET 8 build, in lower case on the
    netstandard2.0 and .NET Framework 4.8 builds. It's now accepted.

  The checks evaluate field and property reads, casts, the logical operators, comparisons, string's `Contains` and a
  core type's `ToString()`, but no arithmetic and no other method call, and they catch these shapes, not every
  predicate that is true for every row: `x.Id == 2 || x.Id != 2` is still accepted. They evaluate as C# does, and the
  database can disagree; a predicate they accept throws `NotSupportedException` for the shapes listed under Changed,
  not for every such disagreement.

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
- **New public API in `Funcular.Data.Orm`:** `DeletePredicateGuard` (`Classify`, `Validate`, `HasLiteralTautology`)
  and the `DeletePredicateVerdict` enum, the checks each provider's delete by predicate runs. A custom provider can
  call them.
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
  `InnerException` of an `InvalidOperationException`. That holds for a delete by id, and for a delete by a predicate
  the delete guard doesn't reject before translating it. Some of these calls threw the provider's exception
  directly before. A predicate rejected before translation (see Fixed) reports the guard's message instead.
- **`Delete`/`DeleteAsync` by predicate reject these idioms when a captured value makes them always true.** When
  the condition held, they deleted every row (on SQL Server, the second and third failed with a SQL error instead);
  they now throw "Delete operation requires a non-trivial WHERE clause.":
  - `filter == null || x.Col == filter` with `filter` null;
  - `isAdmin || x.OwnerId == me` with `isAdmin` true;
  - `x.Archived || !keepIds.Contains(x.Id)` with `keepIds` empty;
  - `roles.Contains("admin") || x.OwnerId == me` with `roles` containing `admin` (without it, the captured `Contains`
    throws `NotSupportedException`; see the next entry).

  Test the condition in C# and pass `Delete` only the column condition. `x => !emptyIds.Contains(x.Id)` is still
  rejected, now with that message instead of "…must reference at least one column from the target table."
- **`Delete`/`DeleteAsync` by predicate throw `NotSupportedException` for shapes the providers can send so that they
  match other rows than C# selects, often every row** (all four providers, before the DELETE is sent; queries aren't
  affected):
  - a string `Contains`, `StartsWith` or `EndsWith`, or a `ToString()`, on a value that doesn't read the row
    (`x.Id == 2 || s.Contains("z")`, `x.Name == n.ToString()`): C# and the database can disagree (collation, a
    `ToString()` that isn't sent), so compute it before the query;
  - a comparison of two strings neither of which reads the row, unless one is a `null` literal
    (`x.Id == 2 || role == "admin"` with `role` "Admin" deleted every row on SQL Server and MySQL, which compare under
    a case-insensitive collation); `filter == null || x.Col == filter` is still accepted;
  - a string `Contains`, `StartsWith` or `EndsWith` on a column whose search value is null or empty (a null
    `Contains` or an empty search matches every non-null row), contains `%`, `_`, `[` or a backslash (sent unescaped,
    some providers read them as wildcards or escapes), or is a property of a captured object, such as
    `x.Name.Contains(Filter)` in an instance method (the translation reads it as null).

  Some of these deleted only the rows C# selects before (`x.Id == 2 || s.Contains("z")`; `x.Name.Contains("[")` on
  PostgreSQL, MySQL and SQLite; a backslash on SQL Server and SQLite; `x.Id == 2 || role == "admin"` with `role`
  "Admin" on PostgreSQL and SQLite); they now throw too. Comparisons that read a column are never rejected for case or
  collation; the database's collation decides them, as before.

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
