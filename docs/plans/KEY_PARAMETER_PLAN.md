# Key parameters: Get and Update compare the primary key with a parameter — Implementation Plan

> **Goal:** `Get`, `GetAsync`, `Update` and `UpdateAsync` never write a primary-key value into the SQL text, on any
> provider. The key goes to the database as a parameter, as every other value FunkyORM sends already does.
> - Ships as **3.10.1**, a security release (owner decision 2026-10-12), on `development/3.10.1`, cut from `master`
>   at `6d2f3fa` (3.10.0).
> - The aggregate fixes the 3.10.0 Changelog planned for 3.10.1 move to 3.10.2.

> **Status (2026-10-12):** rev 3.
> - The Task 0 review of rev 1 and its tests (§9.1) found 3 major and 13 minor or nit findings, answered in rev 2
>   (`9fb3862`).
> - The non-author review of `9fb3862` (§9.2) found 1 major, 3 minor and 5 nits. They are answered in this revision
>   (§5). The commit that carries it gets the fix-verification next.

> **Revision 3 — what changed (the review of `9fb3862`, R-1…R-9):**
> - D2: an enum key is converted losslessly and accepts its number's text (R-2, R-3); a decimal, double, float or date
>   key given as text is parsed strictly (R-6).
> - D4: the converted key is normalized for the driver (`NormalizeKeyForBinding`: an enum becomes its number, and an
>   `sbyte` or unsigned integer is widened), because SqlClient and Npgsql reject those types untyped (R-1). On
>   PostgreSQL a string key is sent untyped, as 3.10.0's literal was, so a `citext` key column still compares without
>   case (R-4, executed).
> - §1 withdraws rev 2's claim that reading a PostgreSQL enum column fails: that probe created the type after
>   Npgsql had loaded the database's types.
> - The tests gain:
>   - enum-keyed and `uint`-keyed rows (R-1, R-2);
>   - a `citext` row in a throwaway database (R-4);
>   - conversion and normalization unit rows (R-3, R-6);
>   - the keyed overload with null on all four (R-8);
>   - a per-row key-text check (R-7).
> - The Changelog lists `Update` and the enum key under Fixed (R-5), and the obsolete overload under Changed (R-9).
> - §4.3 gains M10–M13.

> **Revision 2 — what changed (Task 0, T0-1…T0-16):**
> - D2 states the lossless rule the code implements (T0-1). D3 now says that no inner exception is attached and lists
>   what changes for callers (T0-6, T0-8).
> - D4 says "the built-in dialects" (T0-14). D5 adds the parameterless overload and marks the keyed one obsolete
>   (T0-15). D6 builds the qualified clause directly, with no `Replace` (T0-2).
> - The tests gain:
>   - positive evidence that the key is bound (T0-5);
>   - `Update` with a `Guid` key (T0-3), and a key column named `pk` (T0-2);
>   - a non-ASCII key (T0-13);
>   - the unquoted and stacked integer-key keys, and 2.5 (T0-9, T0-1);
>   - database-free unconvertible-key rows through each provider's `Get` (T0-7);
>   - conversion unit rows (T0-1).
> - Fixes to the existing tests: the "absent" `Guid` row asserts first (T0-4); the command filter skips parameter
>   lines (T0-12); a failed drop no longer hides the body's failure (T0-16); the probes use a closed loopback port and
>   made-up SQL credentials (T0-10).
> - §4.3 gains M7–M9, and the coverage is recorded (T0-11).

## 1. Verified premises (author, against `6d2f3fa` and the published 3.10.0 package)

- Each provider's `GetWhereClause<T>(key)` writes the key into the text of the SELECT. Its callers are `Get`,
  `GetAsync`, `Update` and `UpdateAsync` (which reads the existing row first), through
  `CreateGetOneOrSelectCommandText<T>(key)`.
  - **SQL Server** writes the key unquoted.
  - **PostgreSQL, MySQL and SQLite** quote a string or `Guid` key, but don't escape it. They write any other key
    unquoted.
- This has been on SQL Server since the first commit, and in each other provider since that provider was added.
- `Delete<T>(long id)` / `DeleteAsync<T>(long id)` already bind `@id`. The LINQ paths already bind parameters.
- With joins or computed columns, the clause then went through `Replace(pkColumn, table.pkColumn)`, which also
  rewrote the key's text.
- The red runs (§5) show the defect on all four providers:
  - a quote causes a syntax error, and a crafted key returns another row;
  - **SQL Server** writes `Guid` and text keys unquoted;
  - **MySQL** reads a backslash in a key as an escape;
  - **PostgreSQL** returns a row for a backslash and a quote;
  - **SQLite:** a stacked `DELETE` runs when the reader is disposed (seen by the Task 0 reviewer).
- **PostgreSQL schemas where a typed key parameter can't be compared already fail at the read.** Executed against
  the working tree:
  - A `Guid` property over a `varchar` column: the typed WHERE fails with 42883, and with an untyped parameter the
    read fails ("Reading as 'System.Guid' is not supported for fields having DataTypeName 'character varying'").
  - *(Withdrawn in rev 3.)* Rev 2 also reported that reading a string property over a PostgreSQL enum column fails.
    That probe created the enum type after its first connection, and Npgsql loads a database's types once per
    connection string, so the failure was the probe's.

  So a `Guid` over `varchar` didn't work in 3.10.0 either.
- **PostgreSQL `citext` (rev 3, executed in a throwaway database):** for a stored `abc`, a typed text parameter
  `'ABC'` finds no row; an untyped parameter and 3.10.0's literal both find it. Hence D4's untyped string key.
- **SQL Server and PostgreSQL can't bind an enum, an `sbyte` or an unsigned integer in a parameter that has no
  explicit type** (the review of `9fb3862`, R-1, executed on both). 3.10.0 wrote these keys as numbers, which worked.
  The LINQ parameter generators map them explicitly.

## 2. Decisions

- **D1 — One parameter.** The SELECT built by `Get`/`GetAsync`, and the read in `Update`/`UpdateAsync`, compare the
  key column with a parameter named `@pk`. The key's text never enters FunkyORM's SQL text. (MySqlConnector fills
  parameters in on the client, escaped, so on MySQL the value reaches the server as an escaped literal. That's the
  driver's own parameter handling.)
- **D2 — Convert the key to the primary key's type, without loss, before binding it.** The target is the property's
  type, or its underlying type when it's nullable.
  - A key already of that type is used as it is.
  - A `Guid` accepts text that `Guid.TryParse` reads.
  - A string accepts any value's invariant text.
  - An enum accepts an integral value.
  - An enum accepts its underlying number, given as a number or as text, converted losslessly, so 258 doesn't
    become a byte enum's 2 (rev 3).
  - Text or a number goes through `Convert.ChangeType` with the invariant culture. A number must convert back to
    itself, so 2.5 doesn't become 2. A decimal, double or float given as text is parsed as a plain number, with no
    thousands separator and no trailing sign, so `"2,5"` doesn't become 25. A date given as text keeps its kind
    (rev 3).
  - Anything else (a `bool`, a `char`, a date for an integer key, …) is rejected.

  This keeps the shapes that worked in 3.10.0: an integer key given as a `long`, a `short` or its text
  (`Get<T>("2")`). Without the conversion, PostgreSQL would compare a text parameter with an integer column and fail.
  The rule lives in `OrmDataProvider.ConvertKeyToPrimaryKeyType`, which is protected and static.
- **D3 — A key that can't be converted throws `ArgumentException`** (`ParamName` "key") before any connection is
  used.
  - The message names the entity, the key's type and the primary key's type, never the key's text.
  - No inner exception is attached, because .NET's `FormatException` quotes its input.
  - `ArgumentException` rather than `NotSupportedException`, because the argument is wrong, not the shape.
  - **What changes for callers** is disclosed in the Changelog's Changed section:
    - `"two"` and 2.5 for an integer key, and non-`Guid` text for a `Guid` key, failed in the database or found no
      row in 3.10.0;
    - a `char` digit and `"2.0"` for an integer key found their row in 3.10.0 (the Task 0 reviewer ran both on
      SQLite);
    - a key the caller had wrapped in quotes is now compared as written;
    - on SQLite, a `Guid` is matched in its lower-case storage form.
- **D4 — The parameter is created as the built-in dialect creates the UPDATE's primary-key parameter.** That is the
  dialect's `CreateParameter`, made `internal static` on SQL Server, PostgreSQL and MySQL (SQLite's already is).
  - So a SQLite `Guid` key is bound in FunkyORM's storage form (lower-case text), not the driver's.
  - **Before binding, the converted key is normalized** (`OrmDataProvider.NormalizeKeyForBinding`, protected static;
    rev 3). An enum becomes its underlying number. An `sbyte` becomes a `short`, a `ushort` an `int`, a `uint` a
    `long`, and a `ulong` a `long` (a `decimal` above `long.MaxValue`), as the LINQ generators map them.
  - **On PostgreSQL a string key is sent untyped** (`NpgsqlDbType.Unknown`, rev 3), as 3.10.0's quoted literal was
    and as the ORDER BY visitor sends its values. PostgreSQL resolves it against the column, so a `citext` column
    still compares without case. It's still a bound parameter.
  - The public `ISqlDialect` isn't changed. A custom dialect passed to a provider isn't consulted for this
    parameter; 3.10.0 didn't consult one for the inlined key either.
- **D5 — The text seams fail closed.**
  - `CreateGetOneOrSelectCommandText<T>()` (new, parameterless) builds the unfiltered SELECT. The LINQ providers call
    it.
  - `CreateGetOneOrSelectCommandText<T>(key)` is obsolete. It throws `NotSupportedException` when given a key, and
    builds the unfiltered SELECT when given null.
  - `GetWhereClause<T>(key)` is obsolete and always throws `NotSupportedException`.
  - A subclass gets a compile-time warning, and at run time an exception instead of an inlined key.
  - `CreateGetCommand<T>(key)` (new) returns the SELECT and its key parameter.
- **D6 — The key column is qualified as before, built directly.** With joins or computed columns, the WHERE clause is
  `{table}.{key column} = @pk`. There's no `Replace` over the clause, which would have rewritten the parameter when
  the key column is named `pk`.
- **D7 — Documents.**
  - The Changelog gets `## [3.10.1]`:
    - **Security:** the key is a parameter. Release-note level only, with no payloads.
    - **Fixed:** a key containing a quote; a MySQL key containing a backslash; SQL Server `Get` with a `Guid` key or
      a non-numeric text key.
    - **Changed:** D2, D3, D5, and the PostgreSQL typed parameter.
    - **Known issues:** the aggregate fixes move to 3.10.2.
  - The README banner.
  - A sentence in `Usage.md`'s "Get (by ID)".
  - `Advanced.md` and `FUNKYORM_AI_ADVANCED.md` say 3.10.2 for the aggregates.
  - Version, AssemblyVersion and FileVersion are 3.10.1 in the five library csprojs (as 3.6.1 did).

## 3. Acceptance criteria

- **AC1.** On all four providers, `Get` and `GetAsync` with a string key containing a quote, a hyphen, a backslash, a
  percent sign or non-ASCII letters return exactly that row.
- **AC2.** A key built to change an inlined statement matches no row and changes no other table. The rows cover:
  - a quote followed by `OR`;
  - an unquoted `OR`;
  - a stacked `DELETE` after a quote, and without one;
  - a doubled quote;
  - a backslash and a quote.
- **AC3.** In every row of AC1, AC2, AC4, AC5, AC6 (convertible) and AC7:
  - a logged command compares with `@pk`, and `@pk` is logged as bound;
  - except for AC6 and the key named `pk`, no logged command contains the key's text.
- **AC4.** `Update` and `UpdateAsync` of a row whose key contains a quote, or of a row with a `Guid` key, update that
  row and no other.
- **AC5.** A `Guid` key finds its row, whether given as a `Guid` (hex letters included), as its text, or as its
  upper-case text. An absent `Guid` finds none. This holds on all four providers, SQL Server included. On PostgreSQL a
  string key on a `citext` column finds its row whatever the case (rev 3).
- **AC6.** An integer key given as an `int`, a `long`, a `short` or its text finds its row, as in 3.10.0. So do an
  enum key given as the enum, a number or its text, and a `uint` key given as itself, an `int` or text, on all four
  providers (rev 3).
  - A key that can't be converted without loss (D2) throws `ArgumentException` with `ParamName` "key", sends no
    command, uses no connection, and changes nothing. Nothing the exception carries repeats the key.
  - The rows cover a quote and `OR`, an unquoted `OR`, a stacked `DELETE`, `"two"`, `""` and 2.5 for an integer key,
    and non-`Guid` text for a `Guid` key.
- **AC7.** On the table-qualified SELECT (an entity with a computed column), the key is bound as in AC1 and AC2. That
  includes a key column named `pk`, and a key whose text is `pk`.
- **AC8.** On all four providers, the keyed `CreateGetOneOrSelectCommandText<T>(key)` with a key, and
  `GetWhereClause<T>(key)`, throw `NotSupportedException` without opening a connection.
- **AC9.** The existing suites pass unchanged. `Get<T>(null)` keeps 3.10.0's unfiltered SELECT.
- **AC10.** The documents of D7 are true.

## 4. Test plan

### 4.1 AC → test matrix

The integration rows live in `KeyParameterHarness` (`Funcular.Data.Orm.SqlServer.Tests/KeyParameters/`). It is
linked into PostgreSql.Tests and MySql.Tests and run by `SqlServerKeyParameterTests`, `SqliteKeyParameterTests`,
`PostgreSqlKeyParameterTests` and `MySqlKeyParameterTests`. Each row runs sync and async, and creates and drops its
own `zz_kp_*` tables. The database-free rows are in `KeyParameterSeamTests`.

| AC | Test |
|---|---|
| AC1, AC3 | `Get_StringKey_ReturnsThatRow` (6 keys) |
| AC2, AC3 | `Get_KeyThatWouldAlterTheStatement_MatchesNoRow` (6 keys; `zz_kp_num` still has 2 rows) |
| AC4, AC3 | `Update_StringKeyWithQuote_UpdatesThatRow`, `Update_GuidKey_UpdatesThatRow` |
| AC5, AC3 | `Get_GuidKey_ReturnsThatRow` (`guid`, `text`, `upperText`, `absent`); `Get_CitextKey_ComparesWithoutCase` (PostgreSQL, throwaway database) |
| AC6, AC3 | `Get_IntegerKey_ConvertsToThePrimaryKeyType`, `Get_EnumKey_ReturnsThatRow`, `Get_UnsignedKey_ReturnsThatRow`, `Get_UnconvertibleKey_ThrowsBeforeAnyCommand` (8 keys), `Get_UnconvertibleKey_ThrowsBeforeTheConnection` (no database, 4 providers), `ConvertKey_ConvertsToThePrimaryKeyType`, `ConvertKey_RejectsWhatDoesNotConvert`, `NormalizeKey_GivesTheDriversTypesTheyBind` |
| AC7, AC3 | `Get_ComputedColumnEntity_BindsTheKey`, `Get_ComputedColumnEntity_KeyColumnNamedPk` |
| AC8 | `TextSeams_RefuseAKey` (no database) |
| AC9 | `Get_NullKey_KeepsTheUnfilteredSelect`; `KeyedOverload_WithNull_BuildsTheUnfilteredSelect` (each provider); the six suites |

### 4.2 Interface coverage

| Member | Tests |
|---|---|
| Each provider's `Get`, `GetAsync`, `Update`, `UpdateAsync` | the §4.1 rows |
| Each provider's `CreateGetCommand<T>(key)` (new, protected internal) | the same rows; `Get_NullKey_…` for a null key |
| Each provider's parameterless `CreateGetOneOrSelectCommandText<T>()` (new) | every LINQ test; `Get_NullKey_…` |
| The keyed `CreateGetOneOrSelectCommandText<T>(key)`, `GetWhereClause<T>(key)` (obsolete) | `TextSeams_RefuseAKey`; `KeyedOverload_WithNull_…` |
| `OrmDataProvider.ConvertKeyToPrimaryKeyType` (new, protected static) | `ConvertKey_…`, `Get_IntegerKey_…`, `Get_EnumKey_…`, `Get_GuidKey_…` (text forms), `Get_UnconvertibleKey_…` |
| `OrmDataProvider.NormalizeKeyForBinding` (new, protected static) | `NormalizeKey_GivesTheDriversTypesTheyBind`, `Get_EnumKey_…`, `Get_UnsignedKey_…` |
| The built-in dialects' `CreateParameter` (now internal static) | every keyed row; the UPDATE paths as before |

### 4.3 Mutations each key test must kill

| Mutation | Killed by (executed, §5) |
|---|---|
| M1 `Get` writes the key into the text again | `Get_StringKey_…`, `Get_KeyThatWould…` (all three projects) |
| M2 `Update`'s read writes the key into the text again | `Update_StringKeyWithQuote_…` (all three) |
| M3 No conversion: the raw key is bound | `Get_UnconvertibleKey_…`, `Get_IntegerKey_…` (all three) |
| M4 SQLite binds the driver's parameter in `Get` | `Get_GuidKey_…` on SQLite (on the other providers the dialect's parameter is the driver's, so the mutation is equivalent) |
| M5 The text seams build text again | `TextSeams_RefuseAKey` |
| M6 The qualified path loses the key condition | `Get_ComputedColumnEntity_BindsTheKey` (all three) |
| M7 The qualified path rewrites the clause with `Replace` | `Get_ComputedColumnEntity_KeyColumnNamedPk` (all three) |
| M8 SQLite's `Update` read binds the driver's parameter | `Update_GuidKey_UpdatesThatRow` on SQLite |
| M9 The conversion error chains the inner exception, which quotes the key | `ConvertKey_RejectsWhatDoesNotConvert`, `Get_UnconvertibleKey_…` (all three) |
| M10 The key isn't normalized for the driver (rev 3) | `Get_EnumKey_…`, `Get_UnsignedKey_…` on SQL Server and PostgreSQL (equivalent on MySQL and SQLite, whose drivers bind these types) |
| M11 An enum key isn't converted losslessly (rev 3) | `ConvertKey_…` |
| M12 Decimal text is parsed leniently (rev 3) | `ConvertKey_RejectsWhatDoesNotConvert` |
| M13 A PostgreSQL string key is a typed text parameter (rev 3) | `Get_CitextKey_ComparesWithoutCase` |

A partial M3 (convert, but bind the raw key) is equivalent on SQL Server and MySQL, which convert themselves. It's
killed on SQLite (`upperText`) and on PostgreSQL (integer `text`).

### 4.4 Where each tier runs

- The rows need each provider's test database (`FUNKY_CONNECTION`; `FUNKY_PG_CONNECTION` or `postgres_user` /
  `postgres_pwd`; `FUNKY_MYSQL_CONNECTION`).
- SQLite uses a temp file.
- The seam rows need no database. Their probes point at a closed loopback port with made-up SQL credentials.

## 5. Tasks

- **Task 1 (red).**
  - The rev 1 tests against `6d2f3fa`'s code: SqlServer.Tests 94 failed and 18 passed; PostgreSql.Tests and
    MySql.Tests 44 failed and 10 passed each.
  - Rev 2's harness against an export of `6d2f3fa` (without the seam file, which needs the new members):
    SqlServer.Tests 136 failed and 4 passed; PostgreSql.Tests and MySql.Tests 68 failed and 2 passed each. The only
    passes are `Get_NullKey_…`, which pins behavior that doesn't change.
- **Task 2 (green).**
  - D1–D6 in all four providers and Core.
  - The key-parameter rows: SqlServer.Tests 163, PostgreSql.Tests 70, MySql.Tests 70, all passing.
  - The suites, all passing: SqlServer.Tests 1587, PostgreSql.Tests 973, MySql.Tests 926, Sqlite.Tests 800,
    DotNet9 5, net48 79. These were measured before rev 2's added rows; the commit's counts are in its message.
  - M1–M9 were each killed, with the sources restored byte for byte.
  - Line coverage of the touched files is the union across the four suites, before rev 2's added rows:

    | File | Coverage |
    |---|---|
    | `OrmDataProvider.cs` | 87.3 % |
    | `SqlServerOrmDataProvider.cs` | 89.0 % |
    | `PostgreSqlOrmDataProvider.cs` | 89.3 % |
    | `MySqlOrmDataProvider.cs` | 87.6 % |
    | `SqliteOrmDataProvider.cs` | 92.2 % |
    | `SqlServerDialect.cs` | 93.8 % |
    | `PostgreSqlDialect.cs` | 92.7 % |
    | `MySqlDialect.cs` | 91.8 % |

    The added lines left uncovered then (the null-key path, the string and enum conversions) have rows now.
- **Task 3 (rev 3, the review of `9fb3862`).**
  - The enum-keyed and `uint`-keyed rows were red at `9fb3862` on SQL Server and PostgreSQL. That's the reviewer's
    probe and M10, which restores that state.
  - The `citext` row: typed text found no row (M13, and the throwaway-database probe).
  - The key-parameter rows now: SqlServer.Tests 199, PostgreSql.Tests 87, MySql.Tests 85, all passing.
  - The suites, all passing: SqlServer.Tests 1674, PostgreSql.Tests 1006, MySql.Tests 957, Sqlite.Tests 800,
    DotNet9 5, net48 79.
  - M10–M13 each killed where they apply, with the sources restored byte for byte.
  - Coverage of the touched files is 88.3–93.8 %, with `OrmDataProvider.cs` at 91.8 %. The one added line left
    uncovered is the `float` text branch of the conversion.
  - No `zz_kp` table or database is left on any server.
- **Task 4.** A non-author fix-verification of the commit that carries rev 3; the release.

## 6. Out of scope (recorded)

- `[SubqueryAggregate(ConditionValue = …)]` is written into the SQL unescaped. It's a constant the developer writes
  in an attribute, so it's a robustness issue, not an injection.
- *(Answered in rev 3: PostgreSQL `citext` keys stay case-insensitive, verified in a throwaway database.)*
- **The UPDATE statement's own `@Id`** is still created by the dialect without normalization. The review of `9fb3862`
  reported that `Update` of an entity with an enum or unsigned key fails at that statement on SQL Server and
  PostgreSQL, as it did in 3.10.0. That isn't verified here, and it isn't changed: it isn't an injection, and it isn't
  a regression.
- The downstream fix in Aperture (`AnnotationStore` uses `Get` and `Update` with file paths as keys) belongs to that
  repository, after 3.10.1 ships.

## 9. Review dispositions

### 9.1 Task 0 review of rev 1 and its tests (non-author; the tests run against the published 3.10.0 on SQLite; no shared database)

Verdict: NOT CLEAN; 3 major, 13 minor or nit. Blame: AC-GAP 3, TEST-GAP 8, PLAN-GAP 3, HOUSE-RULE 1, OTHER 1.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| T0-1 | major | AC-GAP | D2 as written (`Convert.ChangeType`) loses information; the plan didn't match the tests | D2 is lossless and states the rule; rows for 2.5m, 2.5f, `"two"` and an integration 2.5 |
| T0-2 | major | AC-GAP | D6's `Replace` would rewrite `@pk` for a key column named `pk` | Built directly; `KeyColumnNamedPk` rows; M7 |
| T0-3 | major | TEST-GAP | M4 is killed only on `Get`; no `Update` with a `Guid` key anywhere | `Update_GuidKey_UpdatesThatRow`; M8 |
| T0-4 | minor | TEST-GAP | The absent `Guid` row returned before the inlining check | Asserts first |
| T0-5 | minor | TEST-GAP | AC3 was only a negative substring check | `AssertKeyBound` on every row, AC6's included |
| T0-6 | minor | AC-GAP | "The message doesn't contain the key" wasn't asserted, and the inner `FormatException` quoted it | No inner exception; `ToString()` checked; M9 |
| T0-7 | minor | TEST-GAP | "Sends no command" was weaker than "no connection" | The key is converted before discovery; `ThrowsBeforeTheConnection` rows |
| T0-8 | minor | PLAN-GAP | The shapes that change for callers weren't listed; D7's "always failed" was untrue | Listed in D3 and the Changelog; the PostgreSQL schemas probed (§1) |
| T0-9 | minor | TEST-GAP | The unquoted and stacked keys weren't run against an integer key | Added to `Get_UnconvertibleKey_…` |
| T0-10 | minor | OTHER | The probes' `Integrated Security` against a made-up host could leak NTLM credentials | Closed loopback port, SQL credentials, 1 s timeout |
| T0-11 | minor | HOUSE-RULE | Coverage wasn't planned | Recorded per file (§5) |
| T0-12 | nit | TEST-GAP | `Commands()` counted parameter lines | Excluded |
| T0-13 | nit | TEST-GAP | No non-ASCII key | `naïve-日本` (utf8mb4 on MySQL) |
| T0-14 | nit | PLAN-GAP | D4 overstated "the dialect" | "The built-in dialect" |
| T0-15 | nit | PLAN-GAP | A subclass got no compile-time warning | Parameterless overload; the keyed one is obsolete |
| T0-16 | nit | TEST-GAP | A failed drop could hide the body's failure | The body's failure wins |

### 9.2 Review of `6d2f3fa..9fb3862` (non-author; the key-parameter rows and the six suites run from an export, red against `6d2f3fa`; an old-versus-new probe on all four databases; M1, M3, M7, M9 and two of its own mutants)

Verdict: NOT CLEAN; 1 major, 3 minor, 5 nits. The injection fix is complete. Every runtime value is bound, and the
counts, the red run and the coverage match. Blame: AC-GAP 3, TEST-GAP 3, OTHER 3.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| R-1 | major | AC-GAP | PostgreSQL enum and unsigned keys, and SQL Server `sbyte`/unsigned keys, threw: the drivers can't bind them untyped. 3.10.0 found the row | `NormalizeKeyForBinding`; enum and `uint` rows on all four; M10 |
| R-2 | minor | AC-GAP | An enum key given as its number's text threw | The enum branch accepts integral text; rows |
| R-3 | minor | TEST-GAP | `Enum.ToObject` truncated out-of-range numbers (258 became a byte enum's 2) | Lossless before `Enum.ToObject`; byte-enum rows; M11 |
| R-4 | minor | AC-GAP | PostgreSQL `citext` keys would compare case-sensitively | Executed: they would. String keys are untyped on PostgreSQL; a throwaway-database row; M13 |
| R-5 | nit | OTHER | Fixed understated: `Update` on SQL Server; an enum key given as the enum | Changelog Fixed |
| R-6 | nit | OTHER | Decimal and double text parsed leniently (`"2,5"` became 25); dates lost their kind | `NumberStyles.Float`; `RoundtripKind`; rows; M12 |
| R-7 | nit | TEST-GAP | The key-text check looked for "two" on every row | Each row's own key, in the message |
| R-8 | nit | TEST-GAP | The keyed overload with null was untested | `KeyedOverload_WithNull_…` on each provider |
| R-9 | nit | OTHER | The Changelog didn't say the keyed overload is obsolete (CS0618) or name the parameterless one | Changelog Changed |

Non-blocking observations, recorded for later:
- PostgreSQL `ExecScalar`/`ExecNonQuery` write the procedure and parameter names into `CALL …` (identifiers by
  contract; pre-existing).
- `Get<T>(null)` returns an arbitrary first row, which isn't documented.
- SQL Server binds a string key as `nvarchar`, which costs an index seek against a `varchar` key column; the UPDATE
  and LINQ already do the same.
