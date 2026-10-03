# LIKE escaping and column ToString in WHERE — Implementation Plan

> **Goal:** make `Contains`, `StartsWith` and `EndsWith` match their search value literally, as C# does, and make a
> column's `ToString()` compare as text, on every provider.
> - Ships in **3.10.0, before the beta PR** (owner decision 2026-10-02).
> - Branch `fix/like-escaping-tostring`, cut from `fix/delete-guard-trivial-predicates` at `ca82741`. It merges into
>   `development/3.10` after that branch does, and after `fix/where-captured-values` (I1-6) where both touch the four
>   expression translators; whichever lands second merges the other.
> - Started from a task the delete guard's fix-verification filed (its §6, FV1-2).

> **Status (2026-10-03):** rev 1, written for its Task 0 review. Nothing is implemented.

## 1. Verified premises (author, at `ca82741`)

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

**Code facts** (the four translators are copies apart from the dialect):
- `ToString` is a no-op that translates its receiver: SQL Server `SqlExpressionTranslator.cs:40-48`; PostgreSQL and
  MySQL `…ExpressionTranslator.cs:29-37`; SQLite `:32-40`.
- String `Contains` builds `{receiver} LIKE @p` with `@p = '%' + value + '%'` (SQL Server `:50-87`, the others
  `:39-73` and `:42-76`). Its receiver goes through `TranslateExpression`: a column, or a captured field as a
  parameter.
- `StartsWith`/`EndsWith` (and an unreachable `Contains`) build `{column} LIKE {pattern}` with `@p` concatenated to
  `'%'`: SQL Server `+` (`:189-242`), PostgreSQL and SQLite `||`, MySQL `CONCAT(…)` (`:170-225`, `:173-228`).
- The dialects (`ISqlDialect`) are public; a custom dialect implements the interface, so a new member would break it.

**The other branch (I1-6, `fix/where-captured-values`, plan `WHERE_CAPTURED_VALUES_PLAN.md` rev 5 D15).** The owner
split the work (2026-10-03): that branch owns captured values — a null search value (its D8: matches no row, sent as
`LIKE NULL`), a captured `ToString()` evaluated in C# (its D7, D14), a string `Contains` that doesn't read the row
evaluated in C# (`1=0`/`NOT 1=0`), `StartsWith`/`EndsWith` on a receiver that isn't a column (its D6:
`NotSupportedException`), and the delete guard's captured-value prose, `IsContainsOfNull` and the tripwire comment.
This branch owns LIKE escaping, a column's `ToString()`, and the guard prose about wildcards.

**Existing tests that touch these paths** (AC6): `SqlDataProviderIntegrationTests:436, 459, 482, 503`;
`SqliteDataProviderIntegrationTests:354, 365, 376, 802`; `JsonPathIntegrationTests:315`;
`SqliteJsonPathIntegrationTests:217`; the four suites' `ColdCacheDeleteTests` (a `StartsWith` on a missing table);
`EfSqlDataProviderIntegrationTests:510-577` exercise a test-local EF wrapper, not FunkyORM. No test pins the `LIKE`
text.

## 2. Decisions

- **D1 — A public `LikePattern` helper in Core** (`Funcular.Data.Orm.Core/Linq/LikePattern.cs`, namespace
  `Funcular.Data.Orm.Linq`, like `ScalarProjectionGuard`): `public const char EscapeCharacter = '!'`,
  `public const string EscapeClause = " ESCAPE '!'"` and `public static string Escape(string value)`, which returns
  `value` with each `!`, `%`, `_` and `[` preceded by `!`, and null for null. No `InternalsVisibleTo` (the delete guard
  plan's §1 records why that fails). The Changelog lists it under "New public API".
- **D2 — One escape character on every dialect.** `!` isn't special in a string literal in any of the four (a
  backslash is, in MySQL), so `ESCAPE '!'` is the same text everywhere, and an explicit `ESCAPE` replaces PostgreSQL's
  and MySQL's default backslash escape, so a backslash in a value is literal. `[` is escaped on every dialect (it is
  special only on SQL Server; an escaped ordinary character is that character on all four).
- **D3 — Where escaping applies.** In each translator, a string `Contains` sends `'%' + Escape(value) + '%'` and
  appends `EscapeClause`; `StartsWith`/`EndsWith` send `Escape(value)` and append `EscapeClause` after the pattern.
  This holds for a column receiver and for a captured receiver (`@p1 LIKE @p2 ESCAPE '!'`); the receiver itself is a
  value, not a pattern, and isn't escaped. A null value keeps today's behaviour here (I1-6 owns it; at the merge its
  null branch runs before escaping).
- **D4 — A column's `ToString()`.** The receiver is a column when it is a member of the lambda's parameter (through a
  nullable's `.Value`).
  - An integer column (`sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`) is sent as a cast to text:
    SQL Server `CAST({c} AS NVARCHAR(20))`, PostgreSQL `CAST({c} AS TEXT)`, MySQL `CAST({c} AS CHAR)`, SQLite
    `CAST({c} AS TEXT)`; a nullable one as `COALESCE(<cast>, '')`, as `int?.ToString()` gives `""` for null. The text
    is the invariant form (`-5`); .NET's current culture can differ (a culture whose negative sign isn't `-`).
  - A `string` column is sent as itself, as today.
  - Any other column type (`decimal`, `double`, `float`, `bool`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`,
    `Guid`, an enum, `char`, …) throws `NotSupportedException`: the database's text for it isn't .NET's (`1.50` vs a
    culture's `1,50`; `1` vs `True`; an enum's number vs its name). The message says to compare the column's value.
    *(Refines the owner's "cast to text" for the types a cast can't match; for the owner to confirm in Task 0.)*
  - `ToString` with any argument on a column (a format, a provider) throws `NotSupportedException`.
  - A receiver that isn't a column keeps today's translation here (I1-6 owns it).
  - The cast text lives in each translator, not in `ISqlDialect` (D1's note on public interfaces).
- **D5 — The delete guard.** Its code doesn't change here. Its prose about wildcards changes (D6); its captured-value
  prose, `IsContainsOfNull` and the tripwire comment are I1-6's.
- **D6 — Documents.**
  - Changelog `[3.10.0-beta1]` Fixed: the wildcard, bracket and backslash shapes of §1, now matched literally on all
    four providers, for a column and a captured string; a column's `ToString()` dropped (wrong rows on three
    providers, 42883 on PostgreSQL), now a cast for integer columns.
  - Changelog Changed: `ToString()` on a column of any other type, and `ToString` with an argument on any column, now
    throw `NotSupportedException`; the SQL text carries `ESCAPE '!'`.
  - Changelog New public API: `LikePattern` in `Funcular.Data.Orm.Linq`.
  - `Usage.md:517` (the string-methods section) says the value is matched literally, wildcards included; the
    troubleshooting item 2 list stays true.
  - The delete guard's caveat (Changelog Fixed, `Usage.md` item 4 Warning, `DELETE_GUARD_PLAN.md` §6) drops "doesn't
    escape `%` or `_`" and the `s.Contains("_")` example; at the merge with I1-6, whichever lands second reconciles
    the shared sentences.

## 3. Acceptance criteria

- **AC1 — Literal matching for a column.** On all four providers, `x.Name.Contains(v)`, `StartsWith(v)` and
  `EndsWith(v)` select exactly the rows C# selects for each `v` containing `_`, `%`, `[`, `]`, `!`, `\` or `[x]`, and
  for plain `v` (`"x"`, `"a"`, `""`), by `Query` (sync and async) and by `Delete` (sync and async).
- **AC2 — Literal matching for a captured string.** `x => x.Id == 1 || s.Contains(v)` with `s = "abc"` selects row 1
  for `v` = `_`, `%`, `[x]`, by `Query` and `Delete`, sync and async, on all four, and every row for `v = "b"` by
  `Query` (a `Delete` of that one is the delete guard's to reject: it's always true).
- **AC3 — An integer column's `ToString()`.** `x.Code.ToString() == "07"` selects nothing and `== "7"` selects row 1;
  `x.Big.ToString() == "-5"` selects row 1; `x.NCode.ToString() == ""` selects the rows whose `ncode` is null and
  `== "7"` row 1; `x.Name.ToString() == "a_b"` selects row 1; by `Query`, sync and async, and `Delete`, on all four
  (PostgreSQL included, with no 42883).
- **AC4 — Other `ToString()` on a column is rejected.** `x.Amount.ToString() == "1.50"` (decimal),
  `x.Flag.ToString() == "True"` (bool) and `x.Code.ToString("D2") == "07"` throw `NotSupportedException` before any
  command is sent, by `Query` and `Delete`, sync and async, on all four.
- **AC5 — `LikePattern`.** `Escape` escapes exactly `!`, `%`, `_` and `[`, leaves every other character, and returns
  null for null and `""` for `""`; `EscapeCharacter` and `EscapeClause` are as D1 states.
- **AC6 — No regression.** The tests of §1 pass unchanged; the four suites, DotNet9 and net48 pass.
- **AC7 — The documents of D6 are true.**
- **AC8 — Coverage.** `LikePattern.cs` and each touched translator reach 85 % line coverage, with the baseline at
  `ca82741` recorded.

## 4. Test plan

### 4.1 AC → test matrix

Each row is **Red** (fails at `ca82741` for the stated reason) or a **Guard** (passes there).

**DB-free, `Funcular.Data.Orm.SqlServer.Tests/LikeToString/LikePatternTests.cs`.**

Each row's class is at the seam (Task 1: `LikePattern` with `Escape` returning its input and `EscapeClause` `""`).

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
  `(7,'ab]c',3,3,3,7.00,false)`, drops it in `finally`, and computes the expected ids by running the same predicate
  with LINQ-to-objects over those rows under the invariant culture (so no expectation is hand-written, and
  `StartsWith`'s culture can't change it). Values are lower case, so no collation changes the answer.
- A `Delete` row runs in a transaction, asserts the deleted count and the surviving ids through the provider, and
  rolls back.
- Every row has `sync`/`async` DataRows: `Query<T>().Where(p).ToList()` and `QueryAsync(p)`; `Delete` and
  `DeleteAsync`.

| AC | Test | SQL Server | PostgreSQL | MySQL | SQLite | Class at the seam (as at `ca82741`) |
|---|---|---|---|---|---|---|
| AC1 | `ColumnStringMethods_QuerySelectsWhatCSharpSelects` [`Contains`: `_`, `%`, `[`, `]`, `!`, `\`, `[x]`, `a_`, `x`, `""`; `StartsWith`: `a_`, `a%`, `a[`, `a!`, `a\`, `a`; `EndsWith`: `_b`, `%b`, `[b`, `!b`, `\b`, `b`] | ✓ | ✓ | ✓ | ✓ | Red for each row §1 shows wrong on that provider; Guard for the rest |
| AC1 | `ColumnStringMethods_DeleteDeletesWhatCSharpSelects` [`Contains("_")`, `StartsWith("a%")`, `EndsWith("\\b")`, `Contains("x")`] | ✓ | ✓ | ✓ | ✓ | Red as above (`Contains("x")` Guard) |
| AC2 | `CapturedStringContains_SelectsWhatCSharpSelects` [`_`, `%`, `[x]`, and `b` by Query only], Query and Delete | ✓ | ✓ | ✓ | ✓ | Red for `_` and `%` on all four and `[x]` on SQL Server; Guard for the rest |
| AC3 | `IntegerColumnToString_ComparesAsText` [`code07`, `code7`, `bigMinus5`, `ncodeEmpty`, `ncode7`, `nameAb`], Query and Delete | ✓ | ✓ | ✓ | ✓ | Red: `code07` and `ncodeEmpty` everywhere but PostgreSQL; every integer row on PostgreSQL (42883); `nameAb` Guard |
| AC4 | `OtherColumnToString_IsRejected` [`amount`, `flag`, `codeD2`], Query and Delete | ✓ | ✓ | ✓ | ✓ | Red (no exception, or 42883 on PostgreSQL) |
| AC6 | the existing tests of §1 | ✓ (and net48) | ✓ | ✓ | ✓ | Guard |
| AC7 | Task 4's review checks Task 3's diff against D6 and greps every document for the string methods and `ToString` | — | — | — | — | — |
| AC8 | the suites, DotNet9, net48; coverlet | ✓ | ✓ | ✓ | ✓ | — |

DDL per provider: `flag` is `BIT` (SQL Server), `BOOLEAN` (PostgreSQL), `TINYINT(1)` (MySQL), `INTEGER` (SQLite);
`amount` is `DECIMAL(10,2)` (`NUMERIC` on SQLite); row 6's backslash is written so each dialect stores one backslash
(MySQL `CONCAT('a', CHAR(92), 'b')`).

### 4.2 Interface coverage

| Member | Tests |
|---|---|
| `LikePattern.Escape`, `EscapeCharacter`, `EscapeClause` | `LikePatternTests` |
| Each translator's `TranslateMethodCall` (string `Contains`, `StartsWith`, `EndsWith`, `ToString` branches) | the harness rows on that provider |

**Coverage.** Coverlet per project; a file's coverage is the distinct lines across every cobertura `<class filename>`
element for it, unioned across the four suites. Baselines at `ca82741` are recorded before Task 2.

### 4.3 Mutations each key test must kill

| Mutation | Killed by |
|---|---|
| `Escape` leaves `_` / `%` / `[` / `!` (each) | `LikePatternTests`; the harness `Contains("_")`, `Contains("%")`, `Contains("[")` (SQL Server), `Contains("!")` rows |
| `EscapeClause` not appended to string `Contains` (per provider) | `Contains("!")` (`a!!b` then matches nothing); on PostgreSQL and MySQL `Contains("\\")` |
| `EscapeClause` not appended to `StartsWith`/`EndsWith` (per provider) | `StartsWith("a!")`, `EndsWith("!b")`; on PostgreSQL and MySQL `EndsWith("\\b")` |
| `Escape` not called in string `Contains` / `StartsWith` / `EndsWith` (per provider, each) | that method's `_` and `%` rows |
| A captured receiver's `Contains` not escaped (per provider) | `CapturedStringContains_…` [`_`] |
| The integer cast dropped (per provider) | `code07`; on PostgreSQL every integer row |
| `COALESCE` dropped for a nullable integer (per provider) | `ncodeEmpty` |
| A non-integer column cast instead of rejected (per provider) | `OtherColumnToString_IsRejected` [`amount`, `flag`] |
| `ToString` with an argument on a column not rejected (per provider) | `OtherColumnToString_IsRejected` [`codeD2`] |
| The column test accepts a captured member | AC2's and AC6's rows (a captured receiver must keep today's translation) |

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
   and I1-6 land there), re-run the suites, and merge into `development/3.10` before the beta PR.

## 6. Out of scope (recorded)

- **Captured values** (I1-6, by the owner's split): a null search value, a captured `ToString()`, a string `Contains`
  that doesn't read the row, `StartsWith`/`EndsWith` on a receiver that isn't a column.
- **Collation.** A column's `LIKE` still follows its collation (`x.Name.Contains("A")` matches `a` under a
  case-insensitive collation), as an equality does; C#'s `Contains` is ordinal.
- **`ToString()` inside another method** (`x.Code.ToString().StartsWith("7")`, `x.Code.ToString().Contains("7")`):
  rejected today (`NotSupportedException`), unchanged.
- **ORDER BY.** `ToString` in an ordering is the ORDER BY visitors' and isn't changed.
- **Date-part translation in WHERE** is a separate task (the owner placed it in 3.10.0).
