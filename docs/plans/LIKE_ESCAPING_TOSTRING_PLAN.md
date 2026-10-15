# LIKE escaping and column ToString in WHERE — Implementation Plan

> **Goal:** make `Contains`, `StartsWith` and `EndsWith` match their search value literally, as C# does, and make a
> column's `ToString()` compare as text, on every provider, in a WHERE clause and in a projection.
> - Ships in **3.10.0, before the beta PR** (owner decision 2026-10-02).
> - Branch `fix/like-escaping-tostring`, cut from `fix/delete-guard-trivial-predicates` at `ca82741`. It merges into
>   `development/3.10` after that branch does, and after `fix/where-captured-values` (I1-6) where both touch the four
>   expression translators; whichever lands second merges the other.
> - Started from a task the delete guard's fix-verification filed (its §6, FV1-2).

> **Status (2026-10-14, 3.10.2):** rev 12. Landed on `release/3.10.2` as FD-03 (docs/plans/DECISIONS-3.10.2.md), on top
> of the captured-values fix (FD-02) and the member-of-member fix (FD-01), following §5 Task 4's merge notes: the
> merged `ToString` order is the `.Value` check (`NullableExpressionException`), D4's cast or rejection for a property
> of the entity, then FD-02's rejection for anything else; FD-02's `TranslatorDirectHarness` LIKE pins carry
> `ESCAPE '!'` and its `ToString_Column` pin is the cast; AC10's `d.ToString()` row and the two merge-note rows are
> rejection rows (FD-02's message). AC-G13 and OD-9 are Paul's. `TranslatorBranchTests` is not brought over: FD-02's
> direct harness pins the same branches with FD-02's messages. The branch `fix/like-escaping-tostring` is superseded
> by that landing.
>
> **D-003 as amended by D-128 (2026-10-14; release 3.11.0 per D-004):** the delete guard's wildcard rejection is
> gone with the escaping that made it moot; the guard (`WherePredicateGuard`) refuses an all-rows delete only, and
> every path evaluates the parts of a predicate that don't read the row in C#. The harness rows that pinned
> delete-only rejections (`ColumnStringMethods_DeleteIsRejectedByGuard`, `CapturedStringContains_DeleteIsRejectedByGuard`)
> are replaced: every literal and captured value deletes what it selects (`ColumnDeleteRows` = the SELECT rows);
> `"abc".Contains("b")` selects every row and its delete is refused (`CapturedStringContains_AlwaysTrue_DeleteIsRefused`);
> a self-comparison through `ToString()` selects every row and its delete is refused
> (`ToStringSelfComparison_QuerySelectsEveryRow`, `ToStringSelfComparison_DeleteIsRefused`); AC10's
> `d.ToString() == d.ToString()` is evaluated in C# (true) and `x.Id == 2 && …` selects and deletes row 2 on every path
> (`NonColumnReceiverToString_IsEvaluated`).
>
> **Status (2026-10-03):** rev 10. Task 0 is CLEAN at rev 5 (`288cda3`, §9.5). Task 1's seam is `049e45e` (§5.1),
> Task 2 carries rev 6 (§5.2), Task 3 carries rev 7 (§5.3). Task 4's review of `ca82741..921c995` (§9.6) found 7,
> answered by `6014d8c` (rev 8, §5.4); fix-verification 1 (§9.7) found 2, answered by `e36374e` (rev 9, §5.5);
> fix-verification 2 (§9.8) found 4, answered by `5ec8ca8` (rev 10, §5.6); fix-verification 3 (§9.9) found 1,
> answered by this revision's layer; fix-verification 4 is next.

> **Owner directive (2026-10-03, answering T4-1 and T4-3):** stop accommodating edge-case parameters; reject a shape
> that needs an operation or evaluation with `NotSupportedException` and require a plain value, whenever that
> shortens the work. When a shape "worked" before, ask whether that meant it didn't throw or that it prevented a
> wrong delete; for the first, reject it and say so in the Changelog.

> **Revision 11 — what changed (Task 4 fix-verification 3, FV3-1):** the Changelog's Known issues heading drops
> "(fixes planned for 3.10.1)", which now leads the aggregates paragraph alone, so it doesn't cover the unscheduled
> SQLite item.

> **Revision 10 — what changed (Task 4 fix-verification 2, FV2-1…FV2-4):** the `Guid` advice parses before the
> query (FV2-1); the Changelog's Known issues disclose SQLite's `Guid`/`DateTime` WHERE binding, which the operator
> plan's §8 records as the owner's open call (FV2-2); the decimal example is limited to SQL Server and MySQL, and a
> projection on SQLite "could give" text unlike .NET's (FV2-3); §5.4's failure record (FV2-4).

> **Revision 9 — what changed (Task 4 fix-verification 1, FV1-1, FV1-2):** the Changelog names the shapes that worked
> before and now throw as examples, not a complete list, Guid among them, with the `Guid.Parse` advice there and in
> Usage; D4 says its reason is what a cast would give (FV1-1). §5.4's coverage is re-measured on fresh builds
> (FV1-2).

> **Revision 8 — what changed (Task 4 round 1, T4-1…T4-7):** D5: the delete guard reads a self-comparison through
> a no-argument `ToString()` (owner, T4-1), AC12 and rows. D4: `.Value.ToString()`'s message gives D4's rule where the
> property's own `ToString()` isn't translated either (T4-2), AC11 rows. The Changelog says what worked before and now
> throws: `char`, and an argument on a `string` property, both kept rejected under the directive (T4-3); a row for
> the latter (T4-4). Two branch rows (T4-5). The cast can't use an index, disclosed (T4-6). §5.2's `person` record
> (T4-7). Task 4's merge notes follow I1-6's withdrawal of captured-`ToString` evaluation.

> **Revision 7 — what changed (Task 3):** §5.3 records the documents. D6's edit to the delete guard plan's §6 keeps a
> sentence naming the wildcard shape and this plan, so the record of what the reviews saw stays.

> **Revision 6 — what changed (Task 2):** AC8 needs a DB-free class: the harness rows reach 73 % of the SQL Server
> translator and 68–69 % of the others, the rest being branches this change doesn't touch, so
> `TranslatorBranchTests` pins them (18 rows per provider) and §4.1, §4.2 and Task 4 name it. A projection row writes
> a null label as `(null)`, so it can't pass for `""` (found at the seam, §5.1). Row 6 is inserted through a
> parameter. §5.1, §5.2 and §9.5 record Tasks 0–2.

> **Revision 5 — what changed (Task 0 re-review, L3-1, L3-2):** Task 4 lists the five I1-6 tests that pin the
> unescaped `LIKE` text and change at the merge, and §1 says no test on this branch pins it (L3-1); AC1's seam class
> limits `[` to SQL Server (L3-2).

> **Revision 4 — what changed (Task 0 re-review, L2-1…L2-3):** Task 4's merge notes: the column-test mutation stays
> reachable after the merge and is killed by I1-6's rejection rows, so D4's message differs from I1-6's (L2-1); a
> column's invariant text against a captured value's current-culture text is disclosed and pinned (L2-2); AC1's seam
> class is the general rule (L2-3); I1-6's `AppendColumn` and `ToString_Column` test at the merge (from the I1-6
> session); AC11 names the SQL Server provider's assembly (the reviewer's note).

> **Revision 3 — what changed (Task 0 re-review, L1-1…L1-7):** AC10's row runs on all four providers with a shape
> that holds on each (L1-1); Task 4's merge notes follow I1-6 as implemented, with the merged check order and the
> rows that move (L1-2); AC11's assertion names the provider's exception type (L1-3); the fi-FI row is a pin, not a
> killer (L1-4); seam classes corrected (L1-5); §6's `CHAR(n)` bullet names its providers (L1-6); D6 says
> `.Value.ToString()` worked before (L1-7).

> **Revision 2 — what changed (Task 0, L0-1…L0-15):** projections share the translator and get an AC, rows and
> Changelog lines (L0-1); `.Value.ToString()` throws `NullableExpressionException` and every `ToString` argument on a
> column throws, both the owner's decisions of 2026-10-03 (L0-2, L0-7); a non-column receiver keeps today's
> translation, with a row (L0-3); rows name the literal and captured value shapes, and Delete rows cover every
> metacharacter (L0-4); every integer type at its bounds and a long string (L0-5); AC2 uses `[a]` (L0-6); AC4's
> wording (L0-8); the merge interactions (L0-9); the invariant text is disclosed, with a fi-FI row (L0-10); §6
> corrected and extended (L0-11, L0-12); AC6's list completed (L0-13); `LikePattern`'s values are `static readonly`
> (L0-14); D6's quotes and the Usage SQL comments (L0-15).

## 1. Verified premises (author at `ca82741`; re-run by the Task 0 reviewer, §9.1)

**The probe.** Table `zz_lt_row (id, name, code)` with rows `(1,'a_b',7)`, `(2,'axb',70)`, `(3,'a%b',5)`,
`(4,'a[b',0)`, `(5,'a!b',1)`, `(6,'a\b',2)`; `Query<T>().Where(…)` on each provider; "C#" is what LINQ-to-objects
selects from those rows.

| Predicate | C# | SQL Server | PostgreSQL | MySQL | SQLite |
|---|---|---|---|---|---|
| `x.Name.Contains("_")` | 1 | 1–6 | 1–6 | 1–6 | 1–6 |
| `x.Name.Contains("%")` | 3 | 1–6 | 1–6 | 1–6 | 1–6 |
| `x.Name.Contains("[")` | 4 | none | 4 | 4 | 4 |
| `x.Name.Contains("[x]")` | none | 2 | none | none | none |
| `x.Name.Contains("\\")` | 6 | 6 | none | none | 6 |
| `x.Name.StartsWith("a_")`, `EndsWith("_b")` | 1 | 1–6 | 1–6 | 1–6 | 1–6 |
| `x.Name.EndsWith("\\b")` | 6 | 6 | 1–6 | 1–6 | 6 |
| `x.Id == 1 \|\| s.Contains("_")`, `s = "abc"` | 1 | 1–6 | 1–6 | 1–6 | 1–6 |
| `x.Code.ToString() == "07"` | none | 1 | 42883 | 1 | 1 |
| `x.Code.ToString() == "7"` | 1 | 1 | 42883 | 1 | 1 |
| `x.Code.ToString("D2") == "07"` | 1 | 1 | 42883 | 1 | 1 |
| `x.Name.ToString() == "a_b"` | 1 | 1 | 1 | 1 | 1 |

So:
- `%` and `_` in a search value are wildcards on every provider; on SQL Server `[` opens a character class; on
  PostgreSQL and MySQL a backslash escapes the next character (their default `LIKE` escape). Each gives wrong rows to
  queries and deletes, and a captured string's `Contains` (`@p1 LIKE @p2`) can make a delete's WHERE clause true for
  every row.
- A column's `ToString()` is dropped: the column is compared with the text, so the database's conversion decides
  (`7 = '07'` holds on SQL Server, MySQL and SQLite); PostgreSQL rejects `integer = text`. A format is dropped too.
- **Projections** (Task 0 reviewer, L0-1): each SELECT visitor calls the same `TranslateMethodCall` (SQL Server
  `SelectClauseVisitor.cs:290`, PostgreSQL `:174`, MySQL `:172`, SQLite `:173`). In
  `Select(x => new LRow { Id = x.Id, Label = … })`, `x.Code.ToString()` throws `InvalidCastException` on SQL Server,
  PostgreSQL and MySQL (an integer read into a string), and on SQLite returns the number's text; `x.Name.Contains("_")
  ? "y" : "n"` says "y" for every row; `x.Amount.ToString()`, `x.Flag.ToString()` and `x.Code.ToString("D2")` throw
  `InvalidCastException` on three providers and give .NET-unlike text on SQLite (`1.5`, `1`/`0`, `7`).

**Code facts** (the four translators are copies apart from the dialect):
- `ToString` is a no-op that translates its receiver: SQL Server `SqlExpressionTranslator.cs:40-48`; PostgreSQL and
  MySQL `…ExpressionTranslator.cs:29-37`; SQLite `:32-40`.
- String `Contains` builds `{receiver} LIKE @p` with `@p = '%' + value + '%'` from two value sites: a constant
  (SQL Server `:60-66`) and a captured field (`:67-81`); the others at the matching lines. Its receiver goes through
  `TranslateExpression`: a column, or a captured field as a parameter.
- `StartsWith`/`EndsWith` (and an unreachable `Contains`) read the value (constant or captured field) into one
  variable and build `{column} LIKE {pattern}` with `@p` concatenated to `'%'`: SQL Server `+` (`:189-242`),
  PostgreSQL and SQLite `||`, MySQL `CONCAT(…)`.
- `x.N.Value == 7` throws `NullableExpressionException` (`WhereClauseVisitor.cs:181-192`), and `Usage.md:120` says not
  to use `.Value`.
- On a cold type, `GenerateWhereClause` discovers the columns (`SELECT * FROM table`, schema only) before the
  translator runs (`SqlServerOrmDataProvider.cs:1395-1397`, `:1606-1631`).
- The dialects (`ISqlDialect`) are public; a custom dialect implements the interface, so a new member would break it.

**The other branch (I1-6, `fix/where-captured-values`, plan `WHERE_CAPTURED_VALUES_PLAN.md`, its D15).** The owner
split the work (2026-10-03): that branch owns captured values — a null search value (its D8: matches no row, sent as
`LIKE NULL`), a captured `ToString()` evaluated in C# (its D7, D14), a string `Contains` that doesn't read the row
evaluated in C# (`1=0`/`NOT 1=0`, its D4), `StartsWith`/`EndsWith` on a receiver that isn't a column (its D6:
`NotSupportedException`), and the delete guard's captured-value prose, `IsContainsOfNull` and the tripwire comment.
This branch owns LIKE escaping, a column's `ToString()`, and the guard prose about wildcards.

**Existing tests that touch these paths** (AC6): `SqlDataProviderIntegrationTests:436, 459, 482, 503`;
`SqliteDataProviderIntegrationTests:354, 365, 376, 802`; `PostgreSqlDataProviderIntegrationTests:248, 259, 270`;
`MySqlDataProviderIntegrationTests:103`; `JsonPathIntegrationTests:315`; `SqliteJsonPathIntegrationTests:217`;
`PostgreSqlJsonPathIntegrationTests:308`; the four suites' `ColdCacheDeleteTests` (a `StartsWith` delete on a
missing table, the `StartsWith(goneLast)` delete and the `EndsWith(marker)` query); the net48 copies
(`Funcular.Data.Orm.NetFramework.Tests:512, 535, 580`; `Funcular.Data.Orm.SqlServer.Tests.NetFramework:530, 553, 576,
597`); the `DeleteGuardHarness` string rows. `EfSqlDataProviderIntegrationTests:510-577` exercise a test-local EF
wrapper, not FunkyORM. No test on this branch pins the `LIKE` text (I1-6's do, §5 Task 4). The `ColdCacheDeleteTests`
marker comment ("No underscore: the method-call row uses it inside LIKE patterns", four copies) describes the
workaround this change removes.

## 2. Decisions

- **D1 — A public `LikePattern` helper in Core** (`Funcular.Data.Orm.Core/Linq/LikePattern.cs`, namespace
  `Funcular.Data.Orm.Linq`, like `ScalarProjectionGuard`): `public static readonly char EscapeCharacter = '!'`,
  `public static readonly string EscapeClause = " ESCAPE '!'"` and `public static string Escape(string value)`, which
  returns `value` with each `!`, `%`, `_` and `[` preceded by `!`, and null for null. `static readonly`, not `const`,
  so a caller compiled against 3.10 reads the value at run time (L0-14). No `InternalsVisibleTo` (the delete guard
  plan's §1 records why that fails). The Changelog lists it under "New public API".
- **D2 — One escape character on every dialect.** `!` isn't special in a string literal in any of the four (a
  backslash is, in MySQL), so `ESCAPE '!'` is the same text everywhere, and an explicit `ESCAPE` replaces PostgreSQL's
  and MySQL's default backslash escape, so a backslash in a value is literal. `[` is escaped on every dialect (it is
  special only on SQL Server; an escaped ordinary character is that character on all four). The Task 0 reviewer ran
  this on all four servers (§9.1).
- **D3 — Where escaping applies.** In each translator, a string `Contains` sends `'%' + Escape(value) + '%'` at both
  of its value sites (a constant, a captured field) and appends `EscapeClause`; `StartsWith`/`EndsWith` send
  `Escape(value)` and append `EscapeClause` after the pattern. This holds for a column receiver and for a captured
  receiver (`@p1 LIKE @p2 ESCAPE '!'`); the receiver itself is a value, not a pattern, and isn't escaped. A null
  value keeps today's behaviour here (I1-6 owns it; at the merge its null branch runs before escaping, and
  `LIKE NULL ESCAPE '!'` is still unknown on all four, §9.1). The same translation serves WHERE clauses and
  projections.
- **D4 — A column's `ToString()`.** The receiver is a column when it is a member of the lambda's parameter.
  - An integer column (`sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`) is sent as a cast to text:
    SQL Server `CAST({c} AS NVARCHAR(20))` (the longest is `-9223372036854775808`), PostgreSQL `CAST({c} AS TEXT)`,
    MySQL `CAST({c} AS CHAR)`, SQLite `CAST({c} AS TEXT)`; a nullable one as `COALESCE(<cast>, '')`, as
    `int?.ToString()` gives `""` for null. The text is the invariant form (`-5`); .NET's current culture can differ
    (U+2212 `−5` under fi-FI, sv-SE, nb-NO, eu-ES; a bidi mark under fa-IR, ar-SA, he-IL), and the documents say so.
  - A `string` column is sent as itself, as today.
  - Any other column type (`decimal`, `double`, `float`, `bool`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`,
    `Guid`, an enum, `char`, …) throws `NotSupportedException`: a cast to text wouldn't give .NET's text (`1.50` vs a
    culture's `1,50` or a scale's `1.5`; `1` vs `True`; an upper-case Guid on SQL Server; an enum's number vs its
    name). The message starts "ToString() on a column of type" and says to compare the column's value; it differs
    from I1-6's rejection ("ToString() is translated only on a property of the entity…"), which the merged rows tell
    apart (§5, §9.3). The owner's "cast to text", narrowed to the types a cast matches; the Task 0 reviewer confirmed
    the integer types at their bounds on all four (§9.1).
  - `ToString` with any argument on a column (a format string, an `IFormatProvider`, `CultureInfo.InvariantCulture`)
    throws `NotSupportedException` (owner decision 2026-10-03, L0-7). `x.Code.ToString(CultureInfo.InvariantCulture)`
    worked on SQL Server, MySQL and SQLite and now throws; the Changelog says so.
  - `x.N.Value.ToString()` on a nullable column throws `NullableExpressionException` (owner decision 2026-10-03,
    L0-2): with the guidance of `x.N.Value == 7` on an integer column with no argument, and otherwise with the rule
    above that rejects the property's own `ToString()` (rev 8, T4-2).
  - A `char` column and a `string` column with an argument are rejected with the rest (D4 above). Before, the dropped
    `ToString()` let the database compare the column's value with the text, and some of the shapes D4 rejects
    returned C#'s rows: those two on all four, a `Guid` compared with its lower-case text and
    `ToString(CultureInfo.InvariantCulture)` on an integer on SQL Server, MySQL and SQLite, a `decimal` at its
    column's scale on SQL Server and MySQL (SQLite's `NUMERIC` keeps no scale), a `bool?` on SQL Server (the Task 4
    reviewer's probes at `ca82741`, §9.6–§9.8). Under the owner's directive they stay rejected; the
    Changelog names them as examples, not a complete list (rev 8, T4-3; rev 9, FV1-1).
  - A receiver that isn't a column keeps today's translation here (I1-6 owns it).
  - The same rules hold in a projection.
  - The cast text lives in each translator, not in `ISqlDialect` (D1's note on public interfaces).
- **D5 — The delete guard.** One change to its code (owner decision 2026-10-03, T4-1): its member chain reads
  through an outermost `ToString()` with no argument, so `x.Code.ToString() == x.Code.ToString()` and
  `x.Name.ToString() == x.Name` are self-comparisons (SelfReference) on all four providers. Before, only SQL Server's
  SQL-text regex caught them, and PostgreSQL, MySQL and SQLite deleted every row. Its prose about wildcards changes
  (D6); its captured-value prose, `IsContainsOfNull` and the tripwire comment are I1-6's. Its `HasLiteralTautology`
  reads the new fragments
  (`… ESCAPE '!' OR NOT 1=0`, `CAST(…) … OR NOT 1=0`) correctly, and its C# evaluation of a captured `Contains`
  agrees with the escaped SQL for every metacharacter (§9.1).
- **D6 — Documents.**
  - Changelog `[3.10.0-beta1]` Fixed: the wildcard, bracket and backslash shapes of §1, now matched literally on all
    four providers, for a column and a captured string, in a WHERE clause and a projection; a column's `ToString()`
    dropped (wrong rows on three providers, 42883 on PostgreSQL, `InvalidCastException` in a projection on three),
    now a cast for integer columns, as invariant text.
  - Changelog Changed: `ToString()` on a column of any other type, and `ToString` with any argument on any column
    (including `CultureInfo.InvariantCulture`, which worked on SQL Server, MySQL and SQLite), now throw
    `NotSupportedException`; in a projection they threw `InvalidCastException` on three providers and gave .NET-unlike
    text on SQLite; `x.N.Value.ToString()`, which selected the right rows on SQL Server, MySQL and SQLite, throws
    `NullableExpressionException`; the SQL text carries `ESCAPE '!'`.
  - Changelog New public API: `LikePattern` in `Funcular.Data.Orm.Linq`.
  - `Usage.md` "The `LIKE` Clause" (`:517-530`): the value is matched literally, wildcards included; the SQL comments
    show `ESCAPE '!'`; a column's `ToString()` is an integer column's text in the invariant form, so text it's
    compared with is built with `CultureInfo.InvariantCulture` before the query (the merge with I1-6 adds that a
    captured `n.ToString()` is formatted in the current culture, as C# does); the troubleshooting item 2 list stays
    true; `Usage.md:120`'s `.Value` rule now also covers `ToString()`.
  - The delete guard's caveat (Changelog Fixed, `Usage.md` item 4 Warning, `DELETE_GUARD_PLAN.md` §6) drops the
    unescaped-wildcard clause ("doesn't escape `%` or `_`"; §6's "`%`, `_` or `[`") and the `s.Contains("_")`
    example; at the merge with I1-6, whichever lands second reconciles the shared sentences.
  - The four `ColdCacheDeleteTests` marker comments say underscores are fine now (the workaround's reason is gone).

## 3. Acceptance criteria

- **AC1 — Literal matching for a column.** On all four providers, `x.Name.Contains(v)`, `StartsWith(v)` and
  `EndsWith(v)` select exactly the rows C# selects for each `v` containing `_`, `%`, `[`, `]`, `!`, `\` or `[x]`, and
  for plain `v` (`"x"`, `"a"`, `""`), with `v` written as a literal and as a captured variable, by `Query` (sync and
  async) and by `Delete` (sync and async).
- **AC2 — Literal matching for a captured string.** `x => x.Id == 1 || s.Contains(v)` with `s = "abc"` selects row 1
  for `v` = `_`, `%`, `[a]`, by `Query` and `Delete`, sync and async, on all four, and every row for `v = "b"` by
  `Query` (a `Delete` of that one is the delete guard's to reject: it's always true).
- **AC3 — An integer column's `ToString()`.** For every integer type at its minimum and maximum (SQLite's `ulong`
  up to `long.MaxValue`), `x.T.ToString() == text` selects exactly the rows C# selects under the invariant culture;
  `x.Code.ToString() == "07"` selects nothing and `== "7"` row 1; a nullable integer's `ToString() == ""` selects its
  null rows; a `string` column longer than 20 characters compares as itself; by `Query`, sync and async, and `Delete`,
  on all four (PostgreSQL included, with no 42883).
- **AC4 — Other `ToString()` on a column is rejected.** `x.Amount.ToString() == "1.50"` (decimal),
  `x.Flag.ToString() == "True"` (bool), `x.Code.ToString("D2") == "07"` and
  `x.Code.ToString(CultureInfo.InvariantCulture) == "7"` and `x.Name.ToString(CultureInfo.InvariantCulture) == "a_b"`
  (rev 8) throw `NotSupportedException` before the SELECT or DELETE is
  sent (a delete logs no DELETE and leaves every row), by `Query` and `Delete`, sync and async, on all four.
- **AC5 — `LikePattern`.** `Escape` escapes exactly `!`, `%`, `_` and `[`, leaves every other character, and returns
  null for null and `""` for `""`; `EscapeCharacter` and `EscapeClause` are as D1 states.
- **AC6 — No regression.** The tests of §1 pass unchanged; the four suites, DotNet9 and net48 pass.
- **AC7 — The documents of D6 are true.**
- **AC8 — Coverage.** `LikePattern.cs` and each touched translator reach 85 % line coverage, with the baseline at
  `ca82741` recorded.
- **AC9 — Projections.** In `Select(x => new LRow { Id = x.Id, Label = … })`, `x.Code.ToString()`,
  `x.NCode.ToString()`, `x.Name.Contains("_") ? "y" : "n"` and `x.Name.StartsWith("a%") ? "y" : "n"` give C#'s value
  for every row, and `x.Amount.ToString()` and `x.Code.ToString("D2")` throw `NotSupportedException`, on all four.
- **AC10 — A non-column receiver keeps today's translation.** `x => x.Id == 2 && d.ToString() == d.ToString()` with
  `decimal d = 1.5m` selects row 2 on all four, as today (the D4 column rules don't apply to `d`).
- **AC11 — `.Value` and culture.** `x.NCode.Value.ToString() == "7"` throws the provider's
  `NullableExpressionException` (each provider assembly defines its own, SQL Server's being `Funcular.Data.Orm`: the
  row asserts the exception's full name, that its assembly is the provider's, and its message; a base-type assertion
  would also accept the delete guard's `InvalidOperationException`); `x.NAmount.Value.ToString() == "1.50"` (a
  `decimal?`) and `x.NCode.Value.ToString("D2") == "07"` throw it too, with D4's rule in the message (rev 8); under the
  fi-FI current culture `x.Big.ToString() == "-5"` still selects the `-5` row (the invariant text), on all four.
- **AC12 — A self-comparison through `ToString()` deletes nothing** (rev 8, T4-1). `Delete` and `DeleteAsync` of
  `x.Code.ToString() == x.Code.ToString()`, `x.NCode.ToString() == x.NCode.ToString()` and
  `x.Name.ToString() == x.Name` throw the SelfReference message, log no DELETE and leave every row, on all four; the
  guard classifies `x.Id.ToString() != x.Id.ToString()`, `x.Id.ToString() == x.Other.ToString()` and
  `x.Id.ToString("D2") == x.Id.ToString("D2")` as Acceptable.

## 4. Test plan

### 4.1 AC → test matrix

Each row's class is at the seam (Task 1: `LikePattern` with `Escape` returning its input and `EscapeClause` `""`,
unused by the translators, so the integration rows behave as at `ca82741`). **Red** fails there for the stated
reason; a **Guard** passes there.

**DB-free, `Funcular.Data.Orm.SqlServer.Tests/LikeToString/LikePatternTests.cs`.**

| AC | Test | Class at the seam |
|---|---|---|
| AC5 | `Escape_EscapesTheMetacharacters` [`"_"`→`"!_"`, `"%"`→`"!%"`, `"["`→`"!["`, `"!"`→`"!!"`, `"a_b%c[d!e"`→`"a!_b!%c![d!!e"`; unchanged: `"]"`, `"\\"`, `"abc"`, `"^"`, `"-"`, `""`] | Red for the escaping rows; Guard for the unchanged ones |
| AC5 | `Escape_Null_ReturnsNull` | Guard |
| AC5 | `Constants_AreTheEscapeCharacterAndClause` (`'!'`, `" ESCAPE '!'"`) | Red (the stub's clause is empty) |

**Integration, a shared harness `Funcular.Data.Orm.SqlServer.Tests/LikeToString/LikeToStringHarness.cs`, compiled into
SqlServer.Tests (SQL Server and SQLite classes), PostgreSql.Tests and MySql.Tests, as `DeleteGuardHarness` is.**
- Each row creates `zz_lt_row (id, name, code, ncode, big, amount, flag)` with rows
  `(1,'a_b',7,7,-5,1.50,true)`, `(2,'axb',70,NULL,5,2.00,false)`, `(3,'a%b',5,NULL,10,3.00,false)`,
  `(4,'a[b',0,0,0,4.00,false)`, `(5,'a!b',1,1,1,5.00,false)`, `(6,'a\b',2,2,2,6.00,false)`,
  `(7,'ab]c',3,3,3,7.00,false)`, `(8,'abcdefghijklmnopqrstuvwxyz',4,4,4,8.00,false)`, and `zz_lt_num (id, i8, u8,
  i16, u16, i32, u32, i64, u64)` with a row of each type's minimum and a row of its maximum, drops both in `finally`,
  and computes the expected ids (or projected values) by running the same expression with LINQ-to-objects over those
  rows under the invariant culture (no expectation is hand-written; `StartsWith`'s culture can't change it). Values
  are lower case, so no collation changes the answer.
- A row's captured values (`s`, `v`, `d`) live in one closure scope (a nested scope makes the translator send the
  closure object; §9.1).
- A `Delete` row runs in a transaction, asserts the deleted count and the surviving ids through the provider, and
  rolls back. A rejected `Delete` asserts the exception, that no DELETE was logged, and that every row remains.
- Every WHERE row has `sync`/`async` DataRows: `Query<T>().Where(p).ToList()` and `QueryAsync(p)`; `Delete` and
  `DeleteAsync`. Projection rows are sync (`Query<T>().Select(…).ToList()`).

| AC | Test | SQL Server | PostgreSQL | MySQL | SQLite | Class at the seam |
|---|---|---|---|---|---|---|
| AC1 | `ColumnStringMethods_QuerySelectsWhatCSharpSelects` [`Contains`: `_`, `%`, `[`, `]`, `!`, a backslash, `[x]`, `a_`, `x`, `""`; `StartsWith`: `a_`, `a%`, `a[`, `a!`, `a` and a backslash, `a`; `EndsWith`: `_b`, `%b`, `[b`, `!b`, a backslash and `b`, `b`; each as a literal (`…Lit`) and, for `_`, `%`, `[`, `!` and a backslash, as a captured variable (`…Cap`)] | ✓ | ✓ | ✓ | ✓ | Red where the seam's rows differ from C#'s (every row with `_` or `%`, on SQL Server a row with `[`, and on PostgreSQL and MySQL a row with a backslash, as §1 shows; `!` rows are Guard); Guard for the rest. Task 1 records each row's class |
| AC1 | `ColumnStringMethods_DeleteDeletesWhatCSharpSelects` [`Contains` `_`, `%`, `[`, `!`, a backslash; `StartsWith("a%")`; `EndsWith` a backslash and `b`; `Contains("x")`; literal and captured] | ✓ | ✓ | ✓ | ✓ | Red as above |
| AC2 | `CapturedStringContains_SelectsWhatCSharpSelects` [`_`, `%`, `[a]`, and `b` by Query only], Query and Delete | ✓ | ✓ | ✓ | ✓ | Red for `_` and `%` on all four and `[a]` on SQL Server; Guard for the rest |
| AC3 | `IntegerColumnToString_ComparesAsText` [each of `i8`…`u64` at its minimum and maximum; `code07`, `code7`, `ncodeEmpty`, `ncode7`, `longName`], Query and Delete | ✓ | ✓ | ✓ | ✓ | Red: every integer row on PostgreSQL (42883); elsewhere `code07`, `ncodeEmpty` and each row whose text the database's conversion matches differently; `longName` Guard |
| AC4 | `OtherColumnToString_IsRejected` [`amount`, `flag`, `codeD2`, `codeInvariant`, `nameInvariant` (rev 8)], Query and Delete | ✓ | ✓ | ✓ | ✓ | Red (no exception, or 42883 on PostgreSQL); `nameInvariant` Guard at Task 2 |
| AC9 | `Projection_GivesCSharpsValue` [`codeToString`, `ncodeToString`, `containsUnderscore`, `startsWithAPercent`] and `Projection_OtherColumnToString_IsRejected` [`amount`, `codeD2`] | ✓ | ✓ | ✓ | ✓ | Red (`InvalidCastException` on three providers; on SQLite wrong values, except `codeToString`, a Guard there) |
| AC10 | `NonColumnReceiver_KeepsTodaysTranslation` [`x.Id == 2 && d.ToString() == d.ToString()`], Query | ✓ | ✓ | ✓ | ✓ | Guard |
| AC11 | `NullableValueToString_IsRejected` [`ncodeValue`; `namountValue`, `ncodeValueD2` (rev 8)] (Query and Delete) and `IntegerToString_IsInvariantUnderFiFi` (Query; a culture-invariance pin, not a mutation killer: the cast happens in SQL) | ✓ | ✓ | ✓ | ✓ | Red (`.Value` is dropped today); the fi-FI row is Red on PostgreSQL only; the rev 8 rows Red at Task 2 (the message) |
| AC12 | `ToStringSelfComparison_DeleteIsRejected` [`codeSelf`, `ncodeSelf`, `nameSelf`], Delete; and `Classify_ReturnsTheVerdict` [`idToStringSelf`, `nameToStringSelf` SR; `idToStringSelfNe`, `idToStringOther`, `idToStringFormatSelf` OK] (rev 8) | ✓ | ✓ | ✓ | ✓ | Red at Task 2 on PostgreSQL, MySQL and SQLite (the delete ran) and for the two SR rows; Guard on SQL Server and for the OK rows |
| AC6 | the existing tests of §1 | ✓ (and net48) | ✓ | ✓ | ✓ | Guard |
| AC7 | Task 4's review checks Task 3's diff against D6 and greps every document for the string methods and `ToString` | — | — | — | — | — |
| AC8 | the suites, DotNet9, net48; coverlet; and `TranslatorBranchTests` (DB-free, in SqlServer.Tests, one class per provider calling its translator directly): collection `Contains` errors, rejected string-method arguments, `ToString` on a nested, computed or null receiver, and the date members (rev 6); a child's property named `Value`, and a property of the enclosing object (rev 8, T4-5) | ✓ | ✓ | ✓ | ✓ | Guard: the branch rows pin today's translation |

DDL per provider: `flag` is `BIT` (SQL Server), `BOOLEAN` (PostgreSQL), `TINYINT(1)` (MySQL), `INTEGER` (SQLite);
`amount` is `DECIMAL(10,2)` (`NUMERIC` on SQLite); `name` is 40 characters wide; every row is inserted through
parameters, so each dialect stores row 6's one backslash as given. `zz_lt_num`'s columns use each dialect's type that
holds the .NET type's range (an unsigned type wider where the dialect has none; SQLite `INTEGER`, `u64` up to
`long.MaxValue`).

### 4.2 Interface coverage

| Member | Tests |
|---|---|
| `LikePattern.Escape`, `EscapeCharacter`, `EscapeClause` | `LikePatternTests` |
| Each translator's `TranslateMethodCall` (string `Contains` at both value sites, `StartsWith`, `EndsWith`, `ToString`) | the harness rows on that provider, WHERE and projection |
| Each translator's other `TranslateMethodCall` branches and `TranslateDateMember` (untouched; AC8) | `TranslatorBranchTests` (rev 6) |

**Coverage.** Coverlet per project; a file's coverage is the distinct lines across every cobertura `<class filename>`
element for it, unioned across the four suites. Baselines at `ca82741` are recorded before Task 2.

### 4.3 Mutations each key test must kill

| Mutation | Killed by |
|---|---|
| `Escape` leaves `_` / `%` / `[` / `!` (each) | `LikePatternTests`; the harness `Contains` `_`, `%`, `[` (SQL Server), `!` rows |
| `Escape` not called at string `Contains`' constant site (per provider) | the `…Lit` `Contains` `_` and `%` rows |
| `Escape` not called at string `Contains`' captured site (per provider) | the `…Cap` `Contains` `_` and `%` rows |
| `Escape` not called in `StartsWith`/`EndsWith` (per provider) | the `StartsWith("a_")`, `EndsWith("_b")` rows |
| `EscapeClause` not appended to string `Contains` (per provider) | `Contains("!")` (`a!!b` then matches nothing); on PostgreSQL and MySQL `Contains("\\")` |
| `EscapeClause` not appended to `StartsWith`/`EndsWith` (per provider) | `StartsWith("a!")`, `EndsWith("!b")`; on PostgreSQL and MySQL `EndsWith("\\b")` |
| A captured receiver's `Contains` not escaped (per provider) | `CapturedStringContains_…` [`_`] |
| The integer cast dropped (per provider) | `code07`; on PostgreSQL every integer row |
| The SQL Server cast narrower than 20 (e.g. `NVARCHAR(10)`) | the `i64` and `i32` minimum rows |
| An integer type left out of the cast set (each) | that type's minimum/maximum rows |
| A `string` column cast (`NVARCHAR(20)`) instead of sent as itself | `longName` |
| `COALESCE` dropped for a nullable integer (per provider) | `ncodeEmpty` |
| A non-integer column cast instead of rejected (per provider) | `OtherColumnToString_IsRejected` [`amount`, `flag`]; `Projection_OtherColumnToString_IsRejected` |
| `ToString` with a format string accepted on a column (per provider) | [`codeD2`] |
| `ToString` with an `IFormatProvider` accepted on a column (per provider) | [`codeInvariant`] |
| `.Value.ToString()` accepted (per provider) | `NullableValueToString_IsRejected` |
| The column test accepts a captured member (per provider) | `NonColumnReceiver_KeepsTodaysTranslation` (a closure field) |
| The column test accepts a property of a constant, keeping the property test (per provider; rev 8) | `ToString_CapturedPropertyReceiver_IsAParameterNotAColumn` |
| An argument accepted on a `string` column (per provider; rev 8) | [`nameInvariant`] |
| The `.Value` rule without its `Nullable` test (per provider; rev 8) | `ToString_ChildPropertyNamedValue_IsNotTheNullableRule` |
| The `.Value` message ignores an argument / a non-integer type (per provider; rev 8) | [`ncodeValueD2`] / [`namountValue`] |
| The guard doesn't read a self-comparison through `ToString()` (rev 8) | `Classify` [`idToStringSelf`, `nameToStringSelf`]; `ToStringSelfComparison_DeleteIsRejected` |
| The guard reads through `ToString` with an argument too (rev 8) | `Classify` [`idToStringFormatSelf`] |

### 4.4 Where each tier runs

- **CI:** `ci.yml` runs SqlServer.Tests (the DB-free class, the SQL Server and SQLite harness classes);
  `build-and-test-postgresql.yml` and `build-and-test-mysql.yml` run PostgreSql.Tests and MySql.Tests with their
  harness classes on Linux.
- **Local, recorded with sha:** all four suites, DotNet9, and net48 (MSBuild, then vstest).

## 5. Tasks

1. **Task 0 — Test-plan review** (non-author) to CLEAN.
2. **Task 1 — The seam and the red tests.** Commit `LikePattern` with `Escape` returning its input and `EscapeClause`
   `""` (behaviour-neutral, unused). The tests of §4.1 land with Task 2, the change that turns them green (each
   commit green on its own); each row runs alone at the seam with them and its class is recorded. Record the
   coverage baselines.
3. **Task 2 — D1–D4.** Green; the §4.3 mutations, each run; suites; coverage.
4. **Task 3 — Documents (D6).**
5. **Task 4 — Hostile review and fix-verification** to CLEAN; then merge `development/3.10` (after the delete guard
   and I1-6 land there), re-run the suites, and merge into `development/3.10` before the beta PR. At that merge:
   - I1-6's D4 evaluates a captured string `Contains` in C#, so D3's captured-receiver escaping becomes unreachable;
     AC2's rows then pass through that path, and §4.3's "captured receiver not escaped" row is retired.
   - I1-6's Task 5 fix layer (reported by its session on 2026-10-03, under the owner's directive; re-check against its
     verified sha at the merge) withdraws its evaluation of a captured `ToString()`: a `ToString()` that doesn't read
     the row throws `NotSupportedException` ("ToString() is translated only on a property of the entity; for a value
     that doesn't read the row, compute the string before the query: "), and its `ToString` branch is its
     `AppendColumn` of the receiver. The merged order is: this branch's `.Value` check (`NullableExpressionException`,
     the owner's decision); D4's cast or rejection for a column (inside, or in place of, `AppendColumn` for a property
     receiver); I1-6's rejection for anything else. AC11's `.Value` rows pin the order. I1-6's `ToString_Column`
     test, which pins `x.Id.ToString()` as the bare column, changes to the cast in the merge commit.
   - I1-6's `TranslatorDirectHarness` (as implemented at `d37f719`) pins the unescaped `LIKE` text in five more tests:
     `StringContains_ConstantValue`, `StringContains_CapturedThisPropertyValue`, `StartsWith_Column`,
     `EndsWith_Column` and `StartsWith_CapturedThisPropertyArgument`. The merge commit appends `ESCAPE '!'` to their
     expected SQL; their values have no metacharacters, so the parameters don't change. The harness is compiled into
     all four test projects, so with `ToString_Column` the merge commit changes six tests on each provider (§9.4).
     I1-6's `LIKE NULL` tests don't change: its null branch runs before escaping.
   - After the merge, AC10's `d.ToString()` throws I1-6's `NotSupportedException`, so AC10's row becomes a rejection
     row in the merge commit. Every captured receiver reaches I1-6's rejection, so §4.3's "the column test accepts a
     captured member" rows are killed by I1-6's `Rejected_BeforeAnyCommand` rows, which assert I1-6's message prefix;
     D4's message stays distinct from it (§9.3).
   - I1-6 reads the `Contains` value at one site, so §4.3's two per-site rows become one.
   - The merge commit adds rejection rows to this branch's harness for `x.Code.ToString() == n.ToString()` with a
     captured `int n`, and for `x.Big.ToString() == negFive.ToString()` with `long negFive = -5` under the fi-FI
     culture (Query, all four): each throws I1-6's `NotSupportedException`. The culture difference of §9.3 then can't
     arise, so D6's merge-time sentence becomes "a captured value's `ToString()` is rejected: compute the string before
     the query", and §6's culture bullet is retired.
   - I1-6's D8 null branch runs before escaping.
   - `TranslatorBranchTests` (rev 6) pins today's rejection messages for string `Contains`/`StartsWith` arguments and
     today's translation of a `ToString` receiver that isn't a column (nested, computed, null, captured property, a
     child's `Value`); where I1-6 translates or rejects those shapes with its own messages, the merge commit updates
     the row, and the merged suites re-measure AC8.
   - The guard change of D5 (T4-1) is in `MemberChain`; I1-6's guard edits are elsewhere in the file.

### 5.1 Task 1 (`049e45e`)

- The seam commit holds `LikePattern` alone (`Escape` returns its value, `EscapeClause` is `""`). The §4.1 tests land
  with Task 2, the change that turns them green, so each commit is green on its own.
- Each §4.1 row ran alone at the seam, one test process per row: 949 runs, 13 DB-free and 234 in each harness class
  (SQL Server, SQLite, PostgreSQL, MySQL), with no filter error. Each matched its class in §4.1, and each Red row
  failed for its stated reason: wrong rows selected or deleted (the wildcard rows; `code07` and `ncodeEmpty`),
  `42883` on PostgreSQL (every integer row, the fi-FI row, AC4 and AC11), `InvalidCastException` in a projection on
  SQL Server, PostgreSQL and MySQL, and no rejection before the SQL ran (AC4, AC11). The integer rows §4.1 leaves to
  be recorded (each type at its bounds, `code7`, `ncode7`) are Guard on SQL Server, MySQL and SQLite: those databases
  compare the text with the integer as a number.
- One row was corrected before it was classed: `Projection_GivesCSharpsValue` wrote a null label and `""` alike, so
  `ncodeToString` passed on SQLite at the seam; it now writes `(null)` and is Red there, as §4.1 says.
- Baselines at `ca82741`, the four suites under coverlet (§4.2), every suite passing (1418, 905, 858, 800):

| File | Lines covered |
|---|---|
| `SqlExpressionTranslator.cs` | 154 / 231 (66.67 %) |
| `PostgreSqlExpressionTranslator.cs` | 127 / 228 (55.70 %) |
| `MySqlExpressionTranslator.cs` | 127 / 228 (55.70 %) |
| `SqliteExpressionTranslator.cs` | 142 / 229 (62.01 %) |

### 5.2 Task 2 (the commit that carries rev 6)

- D1–D4 as written.
- `TranslatorBranchTests` (rev 6) ran alone at the seam: 72 runs, all Guard.
- Every §4.1 row passes: 13 DB-free, 234 in each harness class, and 72 branch rows.
- Suites, all passing: SqlServer.Tests 1971, PostgreSql.Tests 1139, MySql.Tests 1092, Sqlite.Tests 800, DotNet9 5,
  net48 79.
- Coverage, the four suites under coverlet (§4.2):

| File | Lines covered | Baseline (§5.1) |
|---|---|---|
| `LikePattern.cs` | 14 / 14 (100 %) | new |
| `SqlExpressionTranslator.cs` | 251 / 265 (94.72 %) | 66.67 % |
| `PostgreSqlExpressionTranslator.cs` | 251 / 262 (95.80 %) | 55.70 % |
| `MySqlExpressionTranslator.cs` | 251 / 262 (95.80 %) | 55.70 % |
| `SqliteExpressionTranslator.cs` | 255 / 263 (96.96 %) | 62.01 % |

  Without `TranslatorBranchTests` the translators were at 73.21, 67.94, 67.94 and 69.20 %.
- Mutations: 90 mutants for the §4.3 rows (each provider row on each provider; the integer-type row for each type on
  each provider), each run against its provider's harness class (the `Escape` rows also against
  `LikePatternTests`). Every one was killed, and in each the rows §4.3 names for it failed.
- The existing suites leave rows in `person` each run, at `ca82741` too (SQL Server 177, PostgreSQL 87, MySQL 59, as
  the Task 4 reviewer measured; T4-7); the rows this plan adds write only `zz_lt_row` and `zz_lt_num`, which each
  row drops.

### 5.3 Task 3 (the commit that carries rev 7)

- D6's documents:
  - the Changelog: two Fixed entries (the wildcard shapes; a property's `ToString()`), the delete guard's caveat without
    the unescaped-wildcard clause and its `s.Contains("_")` example, three Changed entries (`ToString()` on other
    properties or with an argument; `x.N.Value.ToString()`; the `ESCAPE '!'` text), and `LikePattern` in the
    `Funcular.Data.Orm.Linq` API entry;
  - `Usage.md`: "The `LIKE` Clause" (literal matching, `ESCAPE '!'` in the SQL comments, an underscore example, and a
    property's `ToString()` with the invariant-text advice), the `.Value` item (`ToString()` too), and troubleshooting
    item 4's Warning without the unescaped-wildcard clause and example; troubleshooting item 2 stays true;
  - the delete guard plan's §6: the wildcard clause leaves the list of what the database does differently, and a
    sentence names `s.Contains("_")` as the reviews' third shape and this plan as its fix;
  - the four `ColdCacheDeleteTests` marker comments.
- The other documents that mention the string methods (`README.md`, `Advanced.md`, `docs/ai-instructions/`) stay
  true. The MySQL instructions' "use `LIKE` with `CONCAT()` patterns" was already loose for `Contains`, whose pattern
  is built in the parameter; it is unchanged.
- The sentence that a captured `n.ToString()` is formatted in the current culture is the merge's (D6, Task 4).

### 5.4 Task 4, round 1 (the commit that carries rev 8)

- Each finding's rows were written first and run against Task 2's code: T4-2's AC11 rows failed on the message
  (`namountValue`, `ncodeValueD2`, all paths, all four); T4-1's `Classify` SR rows returned Acceptable, and AC12's
  Delete rows deleted on PostgreSQL, MySQL and SQLite (Guard on SQL Server); T4-4's and T4-5's rows pass there, as they
  pin rejections and translations Task 2 already has, and are the killers of the reviewer's surviving mutants.
- Mutations: the reviewer's survivors and the new rows, 22 mutants (§4.3's rev 8 rows: five per provider, two in the
  guard), each killed by the rows §4.3 names.
- Suites, all passing: SqlServer.Tests 2020, PostgreSql.Tests 1157, MySql.Tests 1110, Sqlite.Tests 800, DotNet9 5,
  net48 79.
- Coverage, the four suites under coverlet, every test project rebuilt first (rev 9, FV1-2: the first measurement
  ran Sqlite.Tests on Task 2's binaries and the others on a build older than the commit, and the union inflated two
  totals): `LikePattern.cs` 14 / 14; `DeletePredicateGuard.cs` 449 / 457 (98.25 %); the translators 259 / 273
  (94.87 %), 259 / 270 (95.93 %), 259 / 270 (95.93 %) and 263 / 271 (97.05 %). In that run SqlServer.Tests had 3
  failures, unnamed (the coverage run keeps no trx) and unexplained; they didn't recur in three later runs (one by
  the author, two with trx by the Task 4 reviewer), each of which added exactly the suite's 177 `person` rows. The
  harnesses use fixed table names (`zz_lt_row`, `zz_lt_num`; the delete guard's `zz_dg_row`), so two sessions running
  them against the same databases at once could collide (rev 10, FV2-4).

### 5.5 Task 4, fix-verification 1 (the commit that carries rev 9)

- FV1-1 is a document finding: the Changelog's "Changed" entry for `ToString()`, `Usage.md`'s advice and D4. FV1-2
  corrects §5.4's record. No code or test changes.

### 5.6 Task 4, fix-verification 2 (the commit that carries rev 10)

- Documents only. The parse-first `Guid` form was executed by the Task 4 reviewer on SQL Server, PostgreSQL and
  MySQL (one row, C#'s answer); on SQLite it finds no row FunkyORM wrote, the binding the Known issues entry names.

## 6. Out of scope (recorded)

- **Captured values** (I1-6, by the owner's split): a null search value, a captured `ToString()`, a string `Contains`
  that doesn't read the row, `StartsWith`/`EndsWith` on a receiver that isn't a column.
- **Collation and text rules the databases apply** (pre-existing; the Task 0 reviewer's probes, §9.1):
  - a column's `LIKE` on SQL Server, PostgreSQL and MySQL follows the column's collation (`x.Name.Contains("A")`
    matches `a` under a case-insensitive one), as an equality does; SQLite's `LIKE` ignores the column's collation and
    is ASCII case-insensitive unless `PRAGMA case_sensitive_like` is on; C#'s `Contains` is ordinal;
  - SQL Server ignores trailing spaces in `=` (`N'7' = N'7 '` holds), so `x.I32.ToString() == "7 "` matches a row;
  - a `CHAR(n)` column's padding breaks `EndsWith` on SQL Server and PostgreSQL (MySQL and SQLite match);
  - under the `SQL_Latin1_General_CP1_CI_AS` collation a supplementary-plane character in a pattern matches every row
    (`Contains("😀")` returned all rows), so `x.Id == 2 || s.Contains("😀")` gets past the delete guard until I1-6's
    D4 evaluates a captured `Contains` in C#;
  - the `StringComparison` overloads of `Contains`/`StartsWith`/`EndsWith` are translated with their comparison
    dropped.
- **A column's `ToString()` against a captured value's culture.** After the merge, a comparison of a column's
  `ToString()` (invariant text, D4) with a captured value's `ToString()` (current culture, I1-6's D7) differs from C#
  under a culture whose number text isn't invariant (§5, §9.3). I1-6 rejects `n.ToString(CultureInfo.InvariantCulture)`
  (its D14), so the text is built before the query; D6 says so.
- **A long pattern on SQL Server.** `LIKE` patterns are limited to 8,000 bytes there; escaping can double a value's
  length, so a value of many metacharacters (`Contains(new string('_', 3000))`) that worked at the seam fails with
  "String or binary data would be truncated" (§9.1).
- **`ToString()` inside another method** (`x.Code.ToString().StartsWith("7")`, `x.Code.ToString().Contains("7")`):
  rejected today (`NotSupportedException`), unchanged.
- **ORDER BY.** `ToString` in an ordering is the ORDER BY visitors', which don't call the translator.
- **Date-part translation in WHERE** is a separate task (the owner placed it in 3.10.0).

## 9. Review dispositions

### 9.1 Task 0 review of rev 1 (`6c83810`; non-author; the probe re-run and extended on all four providers, sync, async and Delete; a prototype of D1–D4 with mutation switches; 158 cells per provider; 17 mutations per provider)

Verdict: NOT CLEAN; 14 blocking findings and one nit. The design held on all four providers (`ESCAPE '!'`, the
integer casts at their bounds, the delete guard's reading of the new SQL). Blame: AC-GAP 7, TEST-GAP 1, HOUSE-RULE 1,
PLAN-GAP 6.

| # | Sev | Blame | Location | Disposition |
|---|---|---|---|---|
| L0-1 | medium | AC-GAP | D3, D4, §3, §4.1: projections share the translator | AC9, rows, Changelog lines. |
| L0-2 | medium | AC-GAP | D4: `.Value.ToString()` | Owner decision 2026-10-03: `NullableExpressionException`; AC11, a row, a mutation. |
| L0-3 | medium | AC-GAP | §4.3: "the column test accepts a captured member" survived | AC10 and its row. |
| L0-4 | medium | TEST-GAP | §4.1: the two `Contains` value sites; Delete coverage | Literal and captured rows; per-site mutations; Delete rows for every metacharacter. |
| L0-5 | medium | AC-GAP | AC3: cast width, type set, string cast | `zz_lt_num` at the bounds; a 26-character name; mutations. |
| L0-6 | low | AC-GAP | AC2's `[x]` was a Guard | `[a]`. |
| L0-7 | low | AC-GAP | D4: `ToString(CultureInfo.InvariantCulture)` | Owner decision 2026-10-03: rejected; a row; the Changelog names the change. |
| L0-8 | low | AC-GAP | AC4: "before any command" | "Before the SELECT or DELETE"; the delete-guard harness's assertions. |
| L0-9 | low | PLAN-GAP | §5: the merge with I1-6 | Recorded in Task 4. |
| L0-10 | low | HOUSE-RULE | D4: invariant text | Disclosed in D6; the fi-FI row. |
| L0-11 | low | PLAN-GAP | §6: collation and text rules | Corrected and extended. |
| L0-12 | low | PLAN-GAP | §6: SQL Server's 8,000-byte pattern | Recorded. |
| L0-13 | low | PLAN-GAP | §1: AC6's list; the cold-cache comments | Completed; D6 updates the comments. |
| L0-14 | low | PLAN-GAP | D1: `const` | `static readonly`. |
| L0-15 | nit | PLAN-GAP | D6's quotes; `Usage.md`'s SQL comments | Folded in. |

### 9.2 Task 0 re-review of rev 2 (`b58aea7`; non-author; a new prototype of rev 2 with 29 mutation switches; 243 cells per provider at the seam and on the prototype)

Verdict: NOT CLEAN; five blocking findings and two nits. L0-1, L0-2, L0-4…L0-8 and L0-10…L0-15 resolved; L0-3 and
L0-9 partial. Blame: AC-GAP 1, TEST-GAP 2, PLAN-GAP 4.

| # | Sev | Blame | Location | Disposition |
|---|---|---|---|---|
| L1-1 | medium | AC-GAP | AC10 left PostgreSQL out, so the column-test mutation survived there | `d.ToString() == d.ToString()` on all four. |
| L1-2 | medium | PLAN-GAP | §5 Task 4 against I1-6 as implemented (`d37f719`) | The merged check order; the rows that retire or merge; the mixed row added at the merge. |
| L1-3 | low | TEST-GAP | AC11: four `NullableExpressionException` types | The assertion names the provider's type. |
| L1-4 | low | TEST-GAP | §4.3: the fi-FI row killed nothing new | A pin; its §4.3 row removed. |
| L1-5 | low | PLAN-GAP | §4.1 seam classes | Corrected. |
| L1-6 | nit | PLAN-GAP | §6: `CHAR(n)` | Providers named. |
| L1-7 | nit | PLAN-GAP | D6: `.Value.ToString()` | Said to have worked before. |

### 9.3 Task 0 re-review of rev 3 (`acb28e1`; non-author; the column-test mutation on all four; a merged prototype of `fix/where-captured-values` at `d37f719` plus rev 3's D1–D4)

Verdict: NOT CLEAN; two blocking findings and one nit. L1-1 and L1-3…L1-7 resolved; L1-2 partial. Blame: AC-GAP 1,
PLAN-GAP 2.

| # | Sev | Blame | Location | Disposition |
|---|---|---|---|---|
| L2-1 | low | PLAN-GAP | §5 Task 4: the column-test row "retired" after the merge | Killed by I1-6's rejection rows; D4's message stays distinct; "every receiver the delete guard evaluates". |
| L2-2 | low | AC-GAP | The merged column-versus-captured `ToString()` under a non-invariant culture | Disclosed in D6 and §6; a fi-FI merge-time row. |
| L2-3 | nit | PLAN-GAP | §4.1: AC1's seam class | The general rule. |

### 9.4 Task 0 re-review of rev 4 (`ead9710`; non-author; a merged prototype of `d37f719` plus rev 4's D1–D4, with I1-6's SQLite test classes run on it unmutated and under the column-test mutation; 15 merge rows on all four, merged, mutated and on this branch alone)

Verdict: NOT CLEAN; one blocking finding and one nit. L2-1 and L2-2 resolved; L2-3 partial (L3-2). Blame: PLAN-GAP 2
(L3-2 introduced by L2-3's fix).

| # | Sev | Blame | Location | Disposition |
|---|---|---|---|---|
| L3-1 | low | PLAN-GAP | §5 Task 4 named one of the six I1-6 tests the merge changes; §1 "No test pins the `LIKE` text" | The five `LIKE` tests named; §1 qualified. |
| L3-2 | nit | PLAN-GAP | §4.1: AC1's seam class put `[` rows Red on all four | `[` on SQL Server only. |

### 9.5 Task 0 re-review of rev 5 (`288cda3`; non-author; a fresh merged prototype of `d37f719` plus rev 5's D1–D4; I1-6's `TranslatorDirectTests` on all four, as committed and with the described edit; the AC1 rule against 424 seam cells)

Verdict: CLEAN. L3-1 and L3-2 resolved. One informational note: at the merge, `ToString_Column`'s new expectation
differs per provider (the cast text), so the shared harness needs a per-provider hook for it.

### 9.6 Task 4 review of `ca82741..921c995` (non-author; suites re-run; coverage recomputed; 17 §4.3 mutant kinds and 4 of its own; probes of every entry point, modes and collations on all four)

Verdict: NOT CLEAN; five blocking findings and two nits. The core change held everywhere it probed. Blame: AC-GAP 1,
TEST-GAP 2, PLAN-GAP 3, OTHER 1.

| # | Sev | Blame | Location | Disposition |
|---|---|---|---|---|
| T4-1 | low | PLAN-GAP | D5: a self-comparison through `ToString()` passed the guard on PostgreSQL, MySQL and SQLite | Owner: reject it as a self-comparison. The guard reads through a no-argument `ToString()`; AC12; rows; mutations; the Changelog and Usage. |
| T4-2 | low | AC-GAP | D4: `.Value.ToString()`'s advice where the property's `ToString()` isn't translated | The message gives D4's rule there; AC11 rows; the Changelog qualified. |
| T4-3 | low | PLAN-GAP | The Changelog's "Before" sentence; `char` and a `string` with an argument worked on all four | Owner's directive: both stay rejected; the Changelog says they worked and now throw. |
| T4-4 | low | TEST-GAP | An argument on a `string` column unpinned | `nameInvariant`; mutation. |
| T4-5 | nit | TEST-GAP | Two §4.3 variants survived | Two branch rows; mutations. |
| T4-6 | low | PLAN-GAP | The cast can't use an index | Disclosed in the Changelog and Usage. |
| T4-7 | nit | OTHER | §5.2: the PostgreSQL and MySQL `person` rows | Recorded. |

### 9.7 Task 4 fix-verification 1 of `921c995..6014d8c` (non-author; suites rebuilt and re-run; coverage re-measured; 10 mutants of its own; the full probe set before and after the layer, and the "worked before" shapes at `ca82741`)

Verdict: NOT CLEAN; one blocking finding and one record nit. T4-1, T4-2 and T4-4…T4-7 resolved; T4-3 partial
(FV1-1). Blame: PLAN-GAP 1 (introduced by T4-3's fix), OTHER 1.

| # | Sev | Blame | Location | Disposition |
|---|---|---|---|---|
| FV1-1 | low | PLAN-GAP | The Changelog's list of shapes that worked before; D4's Guid reason | Examples, not a complete list, Guid named, with `Guid.Parse` advice; D4's reason is the cast's text. |
| FV1-2 | nit | OTHER | §5.4's coverage, from stale builds | Re-measured on rebuilt projects. |

### 9.8 Task 4 fix-verification 2 of `6014d8c..e36374e` (non-author; every changed claim run at `ca82741` and at the current code on all four; SQLite's `Guid` storage and binding probed; SqlServer.Tests re-run twice with trx)

Verdict: NOT CLEAN; three blocking findings and one record nit. FV1-2 resolved; FV1-1 partial. Blame: HOUSE-RULE 1
(introduced by FV1-1's fix), PLAN-GAP 1, OTHER 2.

| # | Sev | Blame | Location | Disposition |
|---|---|---|---|---|
| FV2-1 | medium | HOUSE-RULE | The Changelog's and Usage's `x.Id == Guid.Parse(idText)`: a method call, which throws on all four | Parse before the query. |
| FV2-2 | medium | OTHER | SQLite binds a WHERE `Guid` in the driver's format (pre-existing; the operator plan's §8, the owner's open call) | Disclosed in the Changelog's Known issues; not fixed here, under the owner's directive. |
| FV2-3 | low | PLAN-GAP | Two SQLite statements: projection text, the decimal example | "Could give"; the decimal example limited to SQL Server and MySQL; D4 mirrored. |
| FV2-4 | nit | OTHER | §5.4 attributed the 3 failures to another process | Unexplained, not reproduced; the fixed table names noted. |

### 9.9 Task 4 fix-verification 3 of `e36374e..5ec8ca8` (non-author; every changed claim run on SQLite at the current code and at `ca82741`, with a `Guid` key FunkyORM wrote)

Verdict: NOT CLEAN; one blocking finding. FV2-1…FV2-4 resolved (FV2-2 as a disclosure). Blame: PLAN-GAP 1
(introduced by FV2-2's fix).

| # | Sev | Blame | Location | Disposition |
|---|---|---|---|---|
| FV3-1 | low | PLAN-GAP | The Changelog's "Known issues (fixes planned for 3.10.1)" heading over the unscheduled SQLite paragraph | Plain heading; "(fixes planned for 3.10.1)" on the aggregates lead-in. |
