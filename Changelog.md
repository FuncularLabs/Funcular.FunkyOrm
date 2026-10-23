# Changelog

All notable changes to this project will be documented in this file.

## [3.12.0] - 2026-10-23

**Release notes.** A WHERE predicate now selects the rows C# selects when a value or a column is null, as EF Core
does by default (`UseRelationalNulls(false)`), on every provider and every path: `Query(predicate)`, `QueryAsync`,
`Query<T>().Where(...)`, `Count`/`LongCount`/`Any`/`All`, and `Delete`/`DeleteAsync`. In SQL a comparison with NULL
is unknown and an unknown row isn't selected, so 3.11.0 dropped the NULL rows from `x.N != 5`, found no row for
`x.N == none` with `none` null, and answered `All` as if a NULL satisfied it. **Upgrade note:** wherever these shapes
appear, queries return C#'s rows, most often more than before (the NULL rows), and deletes remove them, which is
why this is a minor release. Usage.md's
"Null comparisons" section shows the SQL. The design and its tests are in docs/plans/NULL_SEMANTICS_PLAN.md.

### Behaviour change

The rows each shape selects, and deletes, are C#'s (EF Core's). A column compared by `==` with a constant or a captured
variable that isn't null (`x.N == 5`, `x.Flag == include`) is unchanged; the shapes below change, among them a `bool`
column compared with a `bool` worked out from captured values (`x.Archived == (mode == "archived")`).
- **`!=` also selects the rows where the column is NULL:** `x.N != 5` sends `(n != @p OR n IS NULL)`. So does a
  `bool?` (`x.Flag != true`), an enum (`x.E != Color.Red`), a date part or `Length` of a column that can be null, a
  conditional with a branch that can be null, a `[RemoteProperty]`, `[RemoteKey]`, `[JsonPath]`, `[SqlExpression]`
  or `[SubqueryAggregate]` `Avg` whatever its CLR type, and a `[SubqueryAggregate]` `Sum` declared nullable, since a
  LEFT JOIN, a JSON key or an aggregate over no rows can be NULL. **Every string column counts as able to be null**
  (FunkyORM doesn't read `NOT NULL`), so `x.Req != "a"` gains `OR req IS NULL`: the same rows, more SQL. A
  conditional takes EF's form, sent again for its `IS NULL`: `(x.Id > 2 ? x.N : x.M) != 5` is
  `(CASE … END != @p OR CASE … END IS NULL)`. A conditional whose test holds another, as nesting such comparisons
  does, or whose branch holds one inside an expression, is compared in its branches instead, as C# evaluates it:
  `(t ? a : b) != 5` is `CASE WHEN t THEN CASE WHEN (a != @p OR a IS NULL) THEN @t ELSE @f END ELSE … END = @t`.
  So the test is sent once and nesting doesn't multiply the SQL. Each branch repeats the other operand, so EF's form
  is kept, which repeats the nested conditional instead, where that would nest more than one `CASE` beyond 3.11.0's
  (against a conditional about as deep as the nested one, or deeper). The count is SQL Server's: a `COALESCE`, `IIF`
  or `NULLIF` is a level, and so is each one in an `[SqlExpression]`'s template (see Known issues).
- **A comparison with a value that is null is `IS NULL` / `IS NOT NULL`**, whether the null is a literal or a
  variable (`x.N == none`). Against a column that can't be null it is `1=0` / `NOT 1=0`: `x.Id == none` selects
  nothing, and `x.Id != none` every row.
- **Two columns:** `x.N == x.M` also matches two NULLs; `x.N != x.M` takes EF's form; `x.N != x.Id` also selects the
  NULL `N`s.
- **`!` reaches the comparisons** (De Morgan): `!(x.N == 5)` is `x.N != 5` above; `!(x.N > 5)` also selects the NULL
  rows; `!(x.N >= none)` selects every row. A negation with nothing to compensate keeps 3.11.0's text
  (`!(x.Id == 5)` is still `NOT id = @p`).
- **A negated `Contains`, `StartsWith` or `EndsWith` on a column** also selects its NULL rows:
  `(NOT name LIKE … OR name IS NULL)`. **A negated null search value selects every row** (`NOT 1=0`): a null search
  value matches no row (since 3.11.0), so its negation matches every row. This amends 3.11.0's "matches no row,
  negated or not".
- **`IN` with a null item:** `idsWithNull.Contains(x.N)` is `(n IN (…) OR n IS NULL)`; `!ids.Contains(x.N)` is
  `(NOT n IN (…) OR n IS NULL)`; with a null item, `(n IS NOT NULL AND NOT n IN (…))`. 3.11.0 dropped the null item.
- **A `bool` compared with a predicate** compares the predicate's two-valued value, as EF does:
  `x.Name.Contains(a) == false` is `!x.Name.Contains(a)`; `(x.N == 5) == (x.M == 5)` is
  `CASE WHEN n = @p THEN @t ELSE @f END = CASE WHEN m = @p THEN @t ELSE @f END`, and `x.Flag == (x.N > 5)` is
  `flag = CASE WHEN n > @p THEN @t ELSE @f END`, each operand sent once. 3.11.0 sent such a comparison without
  parentheses (`a = b = c`; it kept them only around `&&` and `||`), which each database parses by its own precedence:
  SQL Server rejected it, PostgreSQL rejected two comparisons in a row, and on SQLite and MySQL some shapes selected
  other rows than C#'s with no NULL anywhere (over NOT NULL columns, `x.Flag == (x.Code == 5)` and
  `(x.Id < 3) == (x.Code == 5)` selected no row of a SQLite test table where C# selects four or five), and others
  where an operand was null. A `bool`
  column, `!` of one, and a conditional are values, not predicates: `!x.Active == true` and
  `(x.Id > 2 ? x.Active : false) == true` are compared as they were, which SQL Server requires.
- **`!` of a column in a comparison** is compared as C# lifts it: `!x.Flag != true` also selects the NULL flags,
  `!x.Flag == null` is `flag IS NULL` (3.11.0 sent `NOT flag IS NULL`, the complement, so a delete removed exactly the
  other rows), `x.Flag == !x.Flag2` takes EF's form with non-null guards beside it (the same rows), and beside a
  predicate, `(x.N == 5) == !x.Active` is `active = CASE WHEN n = @p THEN @f ELSE @t END`, no NOT on the column. A
  `bool` that doesn't read the row (`!include`, a comparison or conditional of captured values) is one value, read
  before the query. 3.11.0 sent a comparison of captured values there without parentheses, `flag = @p0 = @p1`: SQL
  Server and PostgreSQL rejected it, and SQLite and MySQL read it as `(flag = @p0) = @p1`, other rows than C#'s with
  no NULL anywhere (on a SQLite test table `x.Active == (mode == "archived")` selected no row where C# selects four,
  and a delete of `x.Active == (mode != "archived")` deleted every row where C# deletes three).
- **A cast between `bool` and `bool?` doesn't hide a shape.** `!` over a lifted predicate, `!(bool?)(x.N > 3)`, is the
  predicate `!(x.N > 3)`; a cast back to `bool` over a lift is the form lifted (`(bool)!(bool?)(x.N > 3)` is
  `!(x.N > 3)`); a cast back to `bool` over a `bool?` conditional whose branches can't be null is the `bool`
  conditional, so `(bool)(x.N != 5 ? (bool?)x.Active : (bool?)false)` counts a NULL `N` as `!= 5`, as C# does; over
  any other `bool?` conditional, `(bool)(x.N != 5 ? (bool?)x.Active : nt)` with `nt` a `bool?`, the conditional's
  test is compensated and its branches stay values; and a cast through `object`, `(bool?)(object)!(x.N > 3)`, is the
  cast without it. 3.11.0 read each as one value, so its test and its predicates went uncompensated.
- **A cast keeps a value's nullability**, as EF's does: `(int)x.N != 5` and `(int)(int?)x.ParentCode != 5` also select
  the NULL rows, as `x.N != 5` and `x.ParentCode != 5` do.
- **A conditional's predicate branch** is its two-valued truth, `CASE WHEN p THEN 1 ELSE 0 END`, as EF sends it:
  `(x.Id > 2 ? (bool?)(x.N == 5) : x.Flag) != true` counts a NULL `N` as `false`, as C# does.
- **A conditional's test** is rewritten wherever the conditional is: `(x.N != 5 ? 1 : 0) == 1` also counts the NULL
  rows as `!= 5`.
- **`All(p)` answers as C# does:** it is true when no row satisfies `!p`. `All(x => x.N > 0)` is `false` when an
  `N` is null (3.11.0's Known issue), and `All` over a null search value is `false`, EF Core's answer (C# throws).
- **Delete verdicts that change.** A delete selects the rows the query selects, so these deletes change:

  | Shape | 3.11.0 | 3.12.0 |
  |---|---|---|
  | `!x.Name.Contains(null)` (and `StartsWith`, `EndsWith`), alone or in `x.Id == 2 \|\| …`, `!(x.N == 5 && …)`, `!(x.Id == 2 && …)` | accepted, deleted some rows or none | refused: always true |
  | `x.Name.Contains(none) == false`, `!= true`, `!(… == true)` | accepted (SQL Server: an error) | refused: always true |
  | `!(x.N >= none)` and the other negated `>`, `>=`, `<` and `<=` with a null value, alone or in an `\|\|` (`x.Id == 2 \|\| !(x.N >= none)`) | accepted: nothing alone, the other operand's rows in an `\|\|` (PostgreSQL: 42883) | refused: always true |
  | `x.Id != none`, `!(x.Id == none)`, `x.Id != null`, `!(x.Id == null)` (a column that can't be null), alone or in an `\|\|` such as `x.Id == 2 \|\| x.Id != none` | accepted (the literal forms deleted every row) | refused: always true |
  | A predicate that a null value's comparison makes always true, also in a conditional's test: `x.N > none ? x.Code == 5 : true`, and under a cast back to `bool` | accepted, every row (SQLite, MySQL; SQL Server: an error) | refused: always true |
  | `x.Name.Contains(a) != nullBool` (a `bool?` holding null) | accepted, nothing (SQL Server, PostgreSQL: an error) | refused: always true |
  | A cast through `object` around a part that is always true by its own columns and literals, or compares a column with itself: `x.Id == 2 \|\| (bool)(object)(x.Code == x.Code)`, `fls != (bool)(object)(x.Name.Length <= x.Name.Length)` with `fls` false | accepted (the cast hid the part from the guard; SQL Server: an error for the second) | refused: always true, or self-referencing, as the same shape without the cast was in 3.11.0 (a captured value under the cast isn't read: see Known issues) |
  | An empty search value under a cast through `object`, written as `""` or `string.Empty`: `(bool)(object)x.Name.Contains("")`, `x.Id == 2 \|\| (bool)(object)x.Name.StartsWith(string.Empty)` | accepted: the rows with a name (and, for the second, the other operand's) | refused: an empty search value |
  | A part under a cast through `object` that a captured value makes a self-comparison or a constant, which the WHERE clause then shows: `(bool)(object)(on ? x.Id == x.Id : x.Id == 2)`, `(bool)(object)(on ? true : x.Id == 2)`, `(bool?)(object)(x.Id == 2 \|\| on) == true`, with `on` true | accepted, every row | refused: always true (the check of the translated clause) |
  | A `bool` compared with the same operand: `(x.N == 5) == (x.N == 5)`, `!x.Active == !x.Active`, a conditional with itself, or a copy that differs by casts, a string's `ToString()` or a widened constant, `(x.Id > 2) == ((int?)x.Id > (int?)2)`; inside an `\|\|` too | accepted: nothing for an equality predicate; for a relational one on SQLite, the rows whose operand isn't NULL (every row over columns that can't be null; MySQL: nothing); every row for a conditional compared with itself; SQL Server and PostgreSQL rejected the predicate and `!` forms; refused where a copy's text matched its original's | refused: self-referencing, or always true |
  | A predicate that compares a column with its own `[SqlExpression]` alias, compared with a `bool` value: `(x.Active == x.ActiveX) != false`, `ActiveX` being `{Active}` | accepted, every row (SQL Server, PostgreSQL: an error) | refused: always true |
  | A column, or its `Length`, date part or `ToString()`, compared with its own `[SqlExpression]` alias's where a `!`, or a comparison with a `bool` value, makes it always true: `!(x.N != x.NX)`, `!!(x.N == x.NX)`, `!(x.Name.Length != x.NameX.Length)`, `x.Active != !x.ActiveX`; `(x.N == x.NX) == true`, `(x.Name != x.NameX) != true`; `NX` being `{N}` | accepted: the rows that aren't NULL, or every row | refused: self-referencing; compared with a `bool` value, always true |
  | A computed property whose template is the dialect's own SQL for a column's part, compared with that part under a `!`: `!(x.NameLen != x.Name.Length)`, `NameLen` being `LENGTH({Name})` | accepted: the rows with a name (`NOT LENGTH(name) != LENGTH(name)`) | refused: self-referencing |
  | An always-true comparison of a column's part with itself beside a NULL test of that column: `x.N.ToString() == x.NX.ToString() \|\| x.N == null` | accepted, every row (3.11.0's text is the same) | refused: always true |
  | Any operand that reads the row compared with a copy of itself, literal or through an `[SqlExpression]` alias (a template that is one `{Token}`), a template of the same text, or a part the provider sends as the same SQL, letter case aside outside quoted text (a template that is the dialect's own text for a column's part, `LENGTH({Name})` against `x.Name.Length`; two templates that differ only in SQL's own words' letter case; two `[JsonPath]` properties on one path; two properties on one column): a conditional or a call, by `==`, `>=` or `<=`, under a `!` of `!=`, `<` or `>`, through a cast through `object`, or inside an `\|\|`: `(x.Id > 2 ? x.N : x.M) == (x.Id > 2 ? x.NX : x.M)`, a nested conditional whose branch is `x.NameLen` against its copy over `x.Name.Length`, `x.Active == x.ActiveX \|\| x.Flag == null` | accepted where its text didn't show the copy (its constants as two parameters, or an alias): the rows whose operand isn't NULL (`A = A`), or every row (`… OR flag IS NULL`); refused where it did | refused: self-referencing, or always true inside another operator, as a column compared with itself has been since 3.10 (`>=` and `<=` too) |
  | `!onlyNull.Contains(x.N)`, `x.Id == 2 \|\| !onlyNull.Contains(x.N)`, `!(x.Id == 2 && onlyNull.Contains(x.N))` | refused | accepted (`n IS NOT NULL`, …) |
  | `!(x.Flag == !x.Flag)` | refused (its text, `NOT flag = NOT flag`, read as a self-comparison) | accepted: the rows with a flag (EF's) |
  | `Count`/`All` over a reverse join whose only remote comparison is with a null value (`x.ReverseKey > none`) | refused (fan-out) | runs: `1=0` needs no join |
  | `x.Req != noName`, `x.N == 5 \|\| x.N != 5`: always true once NULLs count | accepted, deleted nothing / the non-null rows | **accepted, every row** |

  The guard still proves only the plain always-true shapes (see Known issues), so a delete that is always true only
  once NULLs count is accepted and now deletes every row.
- **A `[SubqueryAggregate]` `Sum` declared non-nullable is `COALESCE(SUM(…), 0)`,** as EF Core sends a `Sum`: a row
  with no child rows sums to the 0 the property reads back, in WHERE, ORDER BY and the SELECT alike. So
  `x.Total == 0` selects it and `x.Total != 0` doesn't, as C# answers over the rows read back (3.11.0 compared a SQL
  NULL, so `x.Total == 0` missed those rows), and it sorts as 0. A `Sum` declared nullable keeps the SQL NULL, which
  it reads back as null.
- **SQL text that changes, where the rows don't or as stated:**
  - a conditional's test, or a branch of a `bool` conditional in a truth position (the predicate itself, or an
    operand of `&&`, `||` or `!` there), that doesn't read the row is sent as `NOT 1=0` / `1=0`, not as parameters.
    That includes a part a cast hid: a conditional that a cast back to `bool` kept in a value position
    (`(bool)(bool?)(x.Id > 3 ? x.Active : true)` sends `ELSE NOT 1=0`, was `ELSE @p`), and a part under a cast through
    `object` (`(bool)(object)(x.Code == x.Code) && x.Id == 2` sends `NOT 1=0`, was `code = code`). Two strings there
    now compare as C# does (ordinal), not under the database's collation. So does a branch's comparison that reads no
    row where a conditional is compared in its branches: with `t` holding a conditional, `(t ? "A" : x.Name) != "a"`
    sends `@p` (true) for `"A" != "a"`, where 3.11.0 and the flat form compare the two under the collation;
  - a `bool` operand of a comparison that doesn't read the row, or whose truth is known, is one parameter holding its
    value (`x.Flag == !include`: 3.11.0 sent `NOT 1=0` or `NOT NOT 1=0` there, which SQL Server rejects; a comparison
    of captured values changes its rows, as stated above), and a conditional's predicate branch is a `CASE`;
  - a null on a column that can't be null is `1=0` / `NOT 1=0`: `x.Id == null` (was `id IS NULL`), `x.Id == none` (was
    `id = @p`), and their `!=`;
  - a `>`, `>=`, `<` or `<=` with a null value is `1=0` (was `n > @p` or `n > NULL`);
  - a captured value in a conditional's value branch is read before the query: a null one is the literal `NULL`, not a
    parameter, which lets PostgreSQL type it;
  - `All`'s WHERE is the translation of `!p`, not `NOT (p)`;
  - a `bool` compared with a predicate loses its `= @p1` where the rows already agreed (SQLite, PostgreSQL, MySQL),
    and a `bool` column compared with one compares its `CASE` (`x.Flag == (x.N > 5)` is
    `flag = CASE WHEN n > @p THEN @t ELSE @f END`, was `flag = n > @p`: an error on SQL Server and PostgreSQL, other
    rows on MySQL, which reads it as `(flag = n) > @p`, and on SQLite other rows where `N` is NULL, as the `bool`
    bullet above says);
  - an integer's `ToString()` over a value that can be NULL is `COALESCE(…, '')`, as EF sends, so a missing
    parent's `[RemoteProperty] int`, or an `[SqlExpression]` integer whose SQL is NULL, compares as `''` (3.11.0: a
    bare `CAST`, NULL; an `int?` column already sent the `COALESCE`). **The rows change:** `ToString() == ""` now
    selects those rows, where 3.11.0 selected none, and a delete over a computed integer deletes them.

### Changed
- **Shapes that failed now run.** On PostgreSQL a comparison with a variable holding null (`x.N == none`) failed
  with 42883 (`operator does not exist: integer = text`), a `bool` compared with a comparison
  (`(x.N == 5) == (x.M == 5)`: two comparisons in a row) with 42601, and a null
  captured value in a `CASE` branch with 42804. On SQL Server a `bool` compared with a predicate, a captured `bool`
  as a conditional's test, a predicate as a conditional's branch, `!` of a captured `bool` in a comparison, and a
  predicate under a cast through `object` compared with a value were syntax errors.
- **The delete guard reads a comparison's operands as the provider sends them.** Two operands whose parts the
  provider translates to the same SQL, letter case aside outside quoted text (a string literal, a JSON path or a quoted
  name keeps its case: text in single quotes, `''` escaped, in double quotes, in brackets or in backticks; PostgreSQL's
  `$$…$$` isn't read as quoted, nor a backslash escape inside PostgreSQL's `E'…'` or MySQL's strings), count as one
  operand compared with itself: a
  template that is the dialect's own text for a
  column's part, two templates that differ only in SQL's own words' letter case, two `[JsonPath]`
  properties on one path (see the delete verdicts above). The four providers pass their SQL through the new
  `WherePredicateGuard.Validate(predicate, operation, operandSql)` in place of `Validate(predicate, operation)`, once
  the predicate has translated, so the verdict is given once; `WherePredicateGuard.OperandText` writes an operand's
  parameters as their values and its quoted text so that its letter case counts. A delete reads a captured value no more
  often than 3.11.0's did, except around a cast through `object` (`(bool)(object)…`): the guard folds the part under
  such a cast without reading under it, but what that tells it can lead it to read a value beside the cast that 3.11.0's
  guard didn't reach, and the rewrite reads under the cast as a query does. A custom provider that calls only `Validate(predicate, operation)` and `ValidateTranslation` keeps the
  check of the whole clause's text, which a conditional compared in its branches can hide. A captured value is compared
  as written, never translated a second time.
- **CI runs all four provider suites** on pushes to `main`, `master`, `development/**` and `release/**`, and NuGet
  publishing waits for every one. Before, nothing ran the SQLite suite, the PostgreSQL workflow never ran on `master`,
  and publishing waited for SQL Server alone. Each test job fails when a test didn't run (an inconclusive) unless the
  job allows it by name (`.github/scripts/Assert-TestsRan.ps1`).

### Fixed
- **`All` counted a row whose predicate SQL was unknown as satisfying it** (3.11.0's Known issue): see Behaviour
  change.
- **The `[SubqueryAggregate]` conditional-count examples** in Usage.md and the AI instructions named constructor
  parameters that don't exist, and didn't compile. They use the `ConditionColumn`/`ConditionValue` properties. So
  did the `[JsonCollection]` examples: `Columns` and `OrderBy` are properties, and the `joins:` option they showed
  doesn't exist. A test now checks every FunkyORM attribute example in the user documents (those without an
  ellipsis placeholder) against the attributes' constructors and properties.
- **A sort helper that tests `query is IOrderedQueryable<T>` throws** on a composed query (GitHub #16). EF Core
  behaves the same way, so the behaviour is kept; Advanced.md §5 explains why and gives the check that works
  (`query.Expression.Type == typeof(IOrderedQueryable<T>)`).
- The never-built `[ReadModel]` proposal is withdrawn from the plans; there is no read-only entity attribute.

### Known issues
- **A conditional compared in its branches can nest one `CASE` more than 3.11.0:** each branch's comparison is a
  two-valued `CASE`, which adds a level where the predicate's deepest part is a branch or the other operand rather
  than a test (a branch that is a conditional at least as deep as the test, or another conditional one level shallower
  than the nested one). A remote or computed integer's `ToString()` can add one more, wherever it sits: it is now a
  `COALESCE`, where 3.11.0 sent a bare `CAST`. SQL Server nests at most 10 `CASE`s, counting a `COALESCE`, `IIF` or
  `NULLIF` as one, and `Any()` and `All()` send their query inside one (`CASE WHEN EXISTS …`), so a predicate that
  3.11.0 sent 10 levels deep (9 under `Any()` or `All()`) is 11 deep in 3.12.0 and fails there with Msg 125, where
  3.11.0 ran it.
- **A nested conditional takes EF's form where comparing it in its branches would nest more than one `CASE` past
  3.11.0's**, as against another conditional about as deep or deeper (an `[SqlExpression]`'s levels counted), which
  would sit below every branch's `CASE`. EF's form repeats the nested conditional for its `IS NULL`, as EF's SQL does, though EF binds each value
  once and FunkyORM binds each copy. So a chain of such comparisons multiplies the SQL and its parameters, about
  threefold per level: from about 6 levels it passes SQL Server's 2,100 parameters, and from about 8 SQLite's 32,766,
  in predicates of 1,000 to 2,000 characters whose rows 3.11.0 got right. A chain of comparisons in conditionals' tests
  against flat conditionals, columns or predicates, on either side, is compared in the branches and grows linearly.
- **A predicate of a million characters nesting dozens of compared conditionals can pass SQLite's 32,766-parameter
  limit** where 3.11.0 ran it with wrong rows, since the branches' comparisons bind values again; its SQL runs to
  millions of characters.
- **The guard proves only the plain always-true shapes.** `x.Id == 2 || x.Id != 2`, and since 3.12.0
  `x.N == 5 || x.N != 5` and `x.Req != noName`, are accepted as deletes, though they hold for every row (the last two
  now delete every row). It asks the provider's SQL only for parts of at most 64 expression nodes, so a twin larger
  than that is the same operand only where its shape is. It reads quoted text with its letter case (a JSON path is
  case-sensitive on every provider), so two templates that differ only in letter case inside quotes count as two values
  where the database matches them as one: a string literal on a case-insensitive collation (SQL Server's and MySQL's
  defaults), a quoted name on SQLite and MySQL whatever the collation, and on SQL Server under a case-insensitive one.
  Compared inside a nested conditional, such a delete is accepted and deletes every row (3.11.0 refused the
  parameter-free form, and ran one whose conditionals hold constants with its own rows). The check of the translated clause, on the other hand, reads quoted text without letter case,
  as 3.11.0's did, so a JSON path compared with the same path in another letter case, alone or under a `!`
  (`!(x.K != x.KUpper)`), is refused as self-referencing though the two hold different values (3.11.0 refused the first
  and accepted the second). Under a cast through `object` around a part that reads the row, the guard's verdict reads
  no captured value, as 3.11.0's read none there. A part that is always true, compares a column with itself or
  searches for an empty value only once a captured value under the cast is read is refused where the translated
  clause shows it (the value sent as a literal: `(bool)(object)(x.Id == 2 || on)`, with `on` true, is always true),
  and accepted otherwise, as 3.11.0 accepted it: `(bool)(object)(on ? (bool?)(x.Active == x.ActiveX) : nullBool)`,
  with `on` true and `ActiveX` being `{Active}`, deletes every row on SQLite, and `(bool)(object)x.Name.Contains(empty)`,
  with `empty` a captured `""`, the rows with a name (there only a search value written in place is seen: `""`,
  `string.Empty`, or a `const` that holds `""`; not one held in a variable, a field or a property).
- **A long chain of `||` or `&&` overflows the stack sooner than in 3.11.0.** Translating it takes more stack per
  operand. On the 1.5 MB stack a .NET 8 app's threads get on Windows (a Release build), the longest chain a query or a
  delete can take on PostgreSQL and MySQL is about 2,430 operands written `x.Id == 1 || x.Id == 2 || …`, where 3.11.0's
  was about 2,870, and about 1,810 nested to the right, `a || (b || (c || …))`, where 3.11.0's was about 2,910 (the
  null-semantics rewrite recurses at each such operand); there SQL Server refuses such a chain first ("nested too
  deeply": about 1,000 operands, or 340 nested to the right), and SQLite too (about 1,000, or 830 nested to the
  right), in both versions alike. A thread with less stack stops sooner in both versions, and below about 768 KB on
  every provider: on a 512 KB stack (what IIS's worker process, `w3wp.exe`, declares) at about 760 operands, where
  3.11.0 took about 900, and about 590 nested to the right, where it took about 950. Past the limit the process ends
  with a stack overflow, which can't be caught, as in 3.11.0 past its own. For a long list of values use `Contains`
  on a collection, `ids.Contains(x.Id)`, which sends one `IN` list.
- **A non-nullable property over a SQL NULL reads back as its default.** A missing parent's `[RemoteProperty] int`,
  an absent JSON key, or a `[SubqueryAggregate]` `Avg` over no rows reads back as `0`, while WHERE compares the SQL
  NULL, as EF Core does for a missing parent. Declare the property nullable to see the null. (A `Sum` declared
  non-nullable is `COALESCE(SUM(…), 0)` since 3.12.0: see Behaviour change.)
- **MySQL reads a JSON null as the text `'null'`**, so `x.DocK == null` doesn't select a row whose key holds a JSON
  null there.
- **PostgreSQL sorts NULLs last ascending**, the other providers first.
- **SQLite binds a `decimal` as text**, as it does a `Guid` or `DateTime` (3.11.0's Known issue), so a comparison of
  a `decimal` value with an expression that has no column affinity (`[SubqueryAggregate]`, `[SqlExpression]`)
  compares as text: `x.ChildSum != 10m` selects a row whose sum is 10. Pre-existing.
- **A lazy collection in a conditional that the SQL repeats** (EF's form for a conditional that can be null:
  `(lazy.Contains(x.N) ? x.N : none) != 5` sends the conditional twice, and `!= x.N` three times) is enumerated once
  (a query held in an `IQueryable<T>` runs once), and its values are bound per copy. A lazy class with its own
  `Contains` method is enumerated once per copy; call `ToList()` first.
- The 3.11.0 Known issues not listed as fixed above still apply.

## [3.11.0] - 2026-10-19

**Release notes.** WHERE predicates are evaluated through one shared code path for SELECT and DELETE, and for any
update by predicate a later release adds (D-003): `Query(predicate)`, `Query<T>().Where(...)`, `Count`/`Any`/`All`,
SELECT projections and `Delete`/`DeleteAsync` translate and evaluate a predicate identically, so a predicate selects
exactly the rows it would delete. When a WHERE matches every row, only a mass mutation (`Delete`/`DeleteAsync` by
predicate, and any update by predicate) is refused; a SELECT runs normally and returns every row (D-128). The shapes,
the messages and the upgrade notes are under **Behaviour change** below; the decisions are recorded in
docs/plans/DECISIONS-3.10.2.md.

WHERE translation fixes for every provider (SQL Server, PostgreSQL, MySQL, SQLite). **Upgrade recommended:** the
shapes under Fixed selected, and deleted, other rows than C# selects, without an error. One translation path serves
`Query`, `Query<T>().Where`, `Count`/`Any`/`All`, SELECT projections and `Delete`; the parity tests
(`WhereParityHarness`) assert that a predicate sends the same WHERE text and parameters under SELECT and DELETE, or
throws the same exception on every path.

**Behaviour change — shared evaluation; only `Delete` (and any update by predicate) refuses an all-rows predicate.**
Translation and evaluation of a WHERE predicate are one path for `Query(predicate)`, `Query<T>().Where(...)`,
`Count`/`LongCount`/`Any`/`All` and `Delete`/`DeleteAsync`, with one flag (`WhereOperation.Read` or
`WhereOperation.MassMutation`) that decides whether the all-rows guard applies, so a predicate selects exactly the
rows it would delete. For the optional-filter idiom, the admin idiom and an empty `NOT IN`, queries return every row
as they did in 3.10.1; the all-rows guard refuses nothing a query accepted in 3.10.1 (what a query now throws for is
untranslatable shapes, listed under Changed, which threw or returned wrong rows before). Two things change for callers:
- **Parts of a predicate that don't read the row are evaluated in C# and sent as a literal** (`NOT 1=0` or `1=0`):
  a constant (`x => true` now selects every row; before 3.11.0 it was sent as a parameter, a SQL error on SQL
  Server), a captured or static `bool`, a comparison of captured values (`filter == null`), a captured string's
  `Contains`, a core type's `ToString()` with no argument or a format string compared with another value that doesn't
  read the row (`id.ToString() == "05"`; a user type's, an `IFormatProvider`'s or an arithmetic result's `ToString()`,
  and any `ToString()` compared with a column, are still rejected, see Changed), a `bool` unboxed from a captured
  `object`, a `bool` compared with a `bool` whose two sides are known (`(x.Id == x.Id) == true`), and a comparison of
  a column with itself where C# answers the same for every row (`x.Id == x.Id` is true for every row, null included,
  as in C#; a nullable's `x.N < x.N` or `x.N > x.N` is false for every row; a nullable's `x.N >= x.N` or `x.N <= x.N`,
  a `float`/`double`-typed comparison such as `x.Amount == x.Amount`, and a member of a column compared with itself,
  `x.Name.Length == x.Name.Length`, are translated as written, because C#'s lifted `>=`/`<=` is false for null and
  true otherwise, `NaN` isn't equal to itself, and the member is the translator's to accept or reject). Two strings that don't read the row are
  therefore compared as C# compares them (ordinal), never under the database's collation. The optional-filter idiom
  `x => filter == null || x.Name.Contains(filter)` with a null `filter`, the admin idiom
  `x => x.OwnerId == me || roles.Contains("admin")` for an admin, and `x => !emptyIds.Contains(x.Id)` select every
  row, as C# does.
- **`Delete(predicate)` and `DeleteAsync` refuse an all-rows predicate** — one that reads no column of the entity
  ("The WHERE clause must reference at least one column of the entity."), compares a column, or a member of one, with
  itself by `==`, `>=` or `<=`, or negates a `!=`, `<` or `>` of one, whatever the type (`x.Id == x.Id`, `x.N >= x.N`,
  `x.Amount == x.Amount`, `x.Name.Length == x.Name.Length`, `!(x.N < x.N)`: "The WHERE clause cannot be a
  self-referencing column expression."; a delete of a nullable's `>=` or a `double`'s `==` would touch every row but
  the nulls or NaNs, so the guard counts it as every row although the query translates it as written; `x.Id != x.Id`
  or `x.N < x.N` is false for every row, deletes nothing and is accepted), always holds, before translation or by a
  literal `NOT 1=0` after it ("The WHERE clause is trivially true; a non-trivial WHERE clause is required.";
  `x.Id == 2 || x.Name.Length == x.Name.Length` and `(x.Id == x.Id) == true` included), or is a
  `Contains`/`StartsWith`/`EndsWith` on a column with an empty search value that nothing else bounds, alone, in an
  `||`, or compared with a `bool` (`x.Name.Contains("") == true`; "The WHERE clause's Contains, StartsWith or EndsWith
  has an empty search value, which matches every non-null row; a non-trivial WHERE clause is required.") — with
  `InvalidOperationException`, before the DELETE is built,
  where a query runs the same predicate and returns every row (every non-null row, for the empty search value). A
  shape no path translates (`x => x.Child.Id == x.Child.Id`, `x => x.Child.Name.Contains("")`) is the translator's
  `NotSupportedException` on a delete as on a query, before the SELECT or DELETE is built, even where the guard would
  also refuse it; and on a type's first use its columns are discovered before the predicate is translated, so a
  delete on a missing table reports that error, as a query does, refused predicate or not.
  3.10.0 refused the first three shapes with the same verdicts (except `(x.Id == x.Id) == true` and the like, a
  comparison used as a value, which 3.10.x sent to the database (deleting every row on SQLite and MySQL) and 3.11.0
  refuses) and refused every empty search value in a
  delete, bounded or not, with `NotSupportedException`; the messages changed (they no longer say "Delete operation"),
  so code that matched the 3.10.0 text must match the new one, the empty value is now `InvalidOperationException`,
  and the bounded shape `x => x.Id == id && x.Name.Contains("")` is accepted. Decide the delete in C#:
  `if (!string.IsNullOrEmpty(filter)) provider.Delete<Person>(x => x.Name.Contains(filter));`.
The other 3.10.0 delete-only rejections (D9) are gone, because the translation they guarded is fixed in this release
(see Fixed): a search value that is a LIKE wildcard, null or a property of an object you hold, a string `Contains` on
a value that doesn't read the row, and two strings that don't read the row, now delete exactly the rows `Query`
selects; a `StartsWith`, `EndsWith` or `ToString()` the shared evaluation can't answer is rejected by the translator
with its own message, on every path. Any future update by predicate takes the same path and the same guard. The
rationale is in docs/plans/DECISIONS-3.10.2.md (D-001, D-003 as amended by D-128, D-004).

### Fixed
- **The ORDER BY visitors no longer write to the console.** Every ordered query on every provider wrote
  `Generated ORDER BY clause: …` to `Console.Out` (a `Console.WriteLine` left in each `*OrderByClauseVisitor`), on
  each read of the clause. The library now writes nothing to the console; SQL goes only to the provider's `Log`
  callback. (The LINQ providers' `Debug.WriteLine` calls remain: they are compiled out of Release builds and go to
  the debugger, not the console.) Pinned by tests that capture `Console.Out` around the visitors and around ordered,
  paged and predicate queries on SQLite.
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
  format. `x.Code == x.Name.Length.ToString()` dropped `.Length` (`code = name`). A core type's `ToString()` compared
  with another value that doesn't read the row is now evaluated in C# (see Behaviour change); compared with a column,
  or on a value the evaluation can't run, it throws (see Changed).
- **A string `Contains` that doesn't read the row** (`s.Contains("ZETA")`) was compared by the server, whose
  collation could find it true where C# finds it false (every row on SQL Server, MySQL and SQLite). It's now
  answered as C# answers it, except that a null string or search value matches no row (C# would throw).
- **A null search value** (`x.Name.Contains(filter)` with `filter == null`) sent `LIKE '%%'`, which matched every row
  with a non-null value. A null value for `Contains`, `StartsWith` or `EndsWith` now matches no row, negated or not
  (inside `All`, see Known issues). The optional-filter idiom `filter == null || x.Name.Contains(filter)` with a
  null `filter` selects every row (`filter == null` is evaluated in C#, see Behaviour change), and the same idiom
  with `StartsWith`/`EndsWith` no longer throws; a delete of it is refused as an all-rows delete.

### Changed
- **`MySqlStringComparison` and `SqliteStringComparison` are `[Obsolete]`, with a message saying they have no effect.**
  The 3.6.0 documents told MySQL users to pass `MySqlStringComparison.CaseSensitive` to the constructor (no effect:
  no constructor accepts the enum and nothing reads it, so no `COLLATE utf8mb4_bin` was ever emitted); the SQLite provider accepts
  `SqliteStringComparison` and stores it (also as `SqliteOrmDataProvider.StringComparison`, now obsolete too) but it is never read,
  and no `COLLATE NOCASE` is emitted. Neither type is removed in 3.11.0 (a 3.x caller still compiles, with a
  warning); the removal is planned for 4.0. What the code does: MySQL compares under the column's collation
  (case-insensitive under the default `_ci` collations; use a `_bin`/`_cs` column collation for case-sensitive
  comparisons); SQLite compares `=` under the column's collation (`BINARY`, case-sensitive, unless the column is
  declared `COLLATE NOCASE`) and `LIKE` folds ASCII case whatever the collation. The README, the 3.6.0 Changelog entry
  and the MySQL and SQLite AI instructions are corrected; a test pins that no document shows the switches as working.
- **The package description names all four providers** (SQL Server, PostgreSQL, MySQL, SQLite) and says the `net48`
  build carries the SQL Server provider only; it had left out MySQL since 3.6.0.
- **The all-rows guard is `Funcular.Data.Orm.WherePredicateGuard`** (see Behaviour change above for the shapes and
  messages). The shared WHERE translation runs it for every operation with a `WhereOperation` flag; it refuses only a
  `MassMutation` (`Delete`/`DeleteAsync`, any update by predicate), before the mutation is built, and on the translated
  clause after. The translator has the first word (D-005): a refused predicate's type is discovered and the predicate
  translated exactly as for a query, so a missing table reports discovery's error and a shape no path translates
  throws the translator's exception (`NotSupportedException`; `NullableExpressionException` for `.Value` or
  `.HasValue` on a nullable column) on a delete exactly as on a query, and the guard's
  `InvalidOperationException` is reserved for predicates that translate. `Delete` no longer checks that
  the translated clause names a column of the table by text (the check would also have refused a legitimate
  predicate; a delete by a remote property alone isn't supported either way, since the DELETE has no JOIN, and now
  fails with the database's unknown-column error instead of the old guard message: see Known issues). The WHERE visitors of all four providers evaluate the parts of a predicate that don't read the row
  through the same class (`WherePredicateGuard.Fold`), so every path sends the same SQL; the guard's verdict uses the
  same evaluation but counts every self-comparison as holding (see Behaviour change).
  `DeletePredicateGuard` and `DeletePredicateVerdict` are `[Obsolete]` and forward to the new class, with its messages;
  the enum gains `UnboundedSearch`, so the verdict `Classify` reports is always the one whose message `Validate` throws.
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
  naming the member, before the SELECT or DELETE is built (on a type's first use its columns are discovered first),
  by `Query`, `Count` and `Delete` alike: `x.D.Hour`, `x.D.Date`,
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
- **A `ToString()` of anything but an entity property, unless the shared evaluation can answer it**: compared with
  a column (`x.Code == id.ToString("D2")`, `x.Id.ToString() == id.ToString()`), in a projection, or on a value the
  evaluation doesn't run — a receiver declared as `object` or an interface, your own type, an `IFormatProvider`
  argument, an arithmetic, indexer or method result, a generic type such as `KeyValuePair`; `Convert.ToString(...)`;
  a `ToString()` that reads the row through another member (`x.Name.Length.ToString()`). A core type's `ToString()`
  with no argument or a format string, compared with a constant or another captured value, is evaluated in C# instead
  (see Behaviour change). Before, a captured value was sent unconverted, which returned the right rows only when the
  value already was the text compared (an `object` or interface variable holding a string,
  `s.ToString(CultureInfo.InvariantCulture)`) or when it was compared with a column's own `ToString()`
  (`x.Id.ToString() == id.ToString()`; also with an enum, except on PostgreSQL, where that threw). Those throw now
  too. Compute the string before the query: `var code = id.ToString("D2");`, then `x.Code == code`.
- **A value that throws while the predicate is translated** (a member of a null captured object,
  `ids.Contains(request.Id)` with `request == null`; a lazy collection that throws while it is read) throws
  `NotSupportedException("Could not evaluate expression: …")` with the original exception inside. A null check on
  the object in the same predicate (`request == null || ids.Contains(request.Id)`) does prevent it: `request == null`
  is evaluated in C# first, and a disjunction with a true side is sent as `NOT 1=0` without the other side being
  read, so the query returns every row, as C# would (a delete of it is refused: it reads no column); check the object
  before building the query when you want the rows filtered. When the item
  is an entity property (`ids.Contains(x.Id)`), a database error or a `NotSupportedException` thrown while the
  collection is enumerated (a query used as the collection, say) passes through unchanged, as before. A collection
  read to answer a `Contains` before the query (an item that doesn't come from the row) is wrapped like any value.

### New public API
- `Funcular.Data.Orm.WherePredicateGuard` (`Classify`, `Validate`, `ValidateTranslation`, `HasLiteralTautology`,
  `Fold`, and its message constants) with `WherePredicateVerdict` and `WhereOperation` (`Read`, `MassMutation`): the
  all-rows guard every provider's shared WHERE translation runs with the operation flag (refusing only for a mass
  mutation), and the evaluation of row-independent parts every provider's WHERE visitor uses, which a custom provider
  can call too. `DeletePredicateGuard` and `DeletePredicateVerdict` (3.10.0) are `[Obsolete]` forwarders; the enum
  gains `UnboundedSearch`.
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
- **The guard proves only the plain shapes.** `x => x.Id == 2 || x.Id != 2` is accepted as a delete, though it
  holds for every row.
- **A delete by a remote property alone isn't supported.** The DELETE statement has no JOIN, so a predicate that
  reads only a `[RemoteProperty]`/`[RemoteKey]` column fails with the database's unknown-column error; 3.10.0 refused
  it with the "must reference at least one column" message. Delete by a column of the entity's own table.
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
  - `MySqlStringComparison` enum — default `CaseInsensitive` (matching MySQL's default `_ci` collations and SQL Server); opt-in `CaseSensitive` applies `COLLATE utf8mb4_bin`. *(Correction, 3.11.0: this never worked — no constructor accepted the enum and nothing read it, so it has no effect; it is obsolete since 3.11.0.)*
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
