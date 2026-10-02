# Delete guard: detect trivial predicates on the expression tree — Implementation Plan

> **Goal:** make the delete-by-predicate guard reject the predicates it claims to reject, on every provider, and stop
> it rejecting legitimate ones.
> - Ships in **3.10.0, before the beta PR** (owner decision 2026-10-02).
> - Branch `fix/delete-guard-trivial-predicates`, cut from `fix/provider-scoped-caches` at `be8de82`, which carries
>   the `GeneralExtensions.Contains` fix and `DeleteGuardCaseTests`. It merges into `development/3.10` after that
>   branch does, with `development/3.10` merged in first.
> - Started from a task the provider-scoped caches review filed (its §9.13 FVC-1 and §9.16).

> **Status (2026-10-02):** rev 2. Task 0 review of rev 1 (`49e535a`, §9.1) found 11 findings; rev 2 answers them and
> is re-reviewed next. Nothing is implemented.

> **Revision 2 — what changed (Task 0, T0-1…T0-11):**
> - D2 evaluates no node that carries a user-defined method (T0-6), and treats any parameter of the entity type as
>   the root (T0-7). Classify rows and harness rows cover every fold rule both ways, the De Morgan and static-read
>   data-loss shapes included (T0-1).
> - D3 flags a literal comparison only when it is a whole boolean term, with `NOT` counted, so `[SqlExpression]`
>   arithmetic isn't rejected (T0-2), and `OR NOT 1=0` is (T0-9).
> - A cold missing-table row proves the guard runs before discovery, and D7 scopes three documents that D5 changes
>   (T0-3).
> - §1 adds SQL Server's data-loss rows; D7 names the idioms that now throw (T0-4).
> - D1 grants `InternalsVisibleTo` to the providers' real assembly names (T0-5).
> - §4.4 adds the PostgreSQL and MySQL CI workflows and Linux table-name case (T0-8). §6 records the undetected
>   shapes and the double read (T0-6, T0-9). AC7 becomes a refactor note (T0-10). The AC6 list and one seam class are
>   corrected (T0-11).

## 1. Verified premises (author at `be8de82`; the Task 0 reviewer re-ran the probe and every citation, §9.1)

**The guard today.** Each provider's `Delete<T>(Expression<Func<T, bool>>)` and `DeleteAsync<T>(…)` check, in order:
the transaction, a null predicate, then `GenerateWhereClause(predicate)`, then four checks on the translated WHERE
text, then the DELETE.
- SQL Server does the four checks inline, twice: `SqlServerOrmDataProvider.cs:283-307` (async) and `:845-869` (sync).
  Its private `ValidateWhereClause<T>(string)` at `:1524` has no caller.
- PostgreSQL, MySQL and SQLite call a private `ValidateWhereClause<T>(string)`: PostgreSQL `:900`, MySQL `:878`,
  SQLite `:851`, from Delete (`:369`, `:353`, `:393`) and DeleteAsync (`:179`, `:169`, `:224`).
- The four checks:
  1. empty or whitespace text: "Delete operation requires a non-empty, valid WHERE clause.";
  2. the text, with spaces removed, contains any of `1=1`, `1 < 2`, `1 > 0`, `true`, `WHERE 1=1`, `WHERE 1 < 2`
     (`OrdinalIgnoreCase`): "Delete operation requires a non-trivial WHERE clause.";
  3. a self-reference regex: SQL Server `^(.+?)\s*(=|>=|<=)\s*\1$` on the whole trimmed text; the others
     `\b(\w+)\s*=\s*\1\b`: "Delete operation WHERE clause cannot be a self-referencing column expression.";
  4. no column name of `T` occurs in the text (`IndexOf`, `OrdinalIgnoreCase`): "Delete operation WHERE clause must
     reference at least one column from the target table."

**What the visitors emit.**
- Every non-null constant becomes a parameter (`VisitConstant`, SQL Server `WhereClauseVisitor.cs:308-320`; the
  others `:239-251`). So `x => true` is `WHERE @p__linq__0`, and check 2's `true` can only match identifiers.
- Members are table-qualified (`{table}.{column}`), except a nullable member's `.Value.Year/.Month/.Day` and remote
  members (their join alias).
- The only literal comparisons the visitors write in a WHERE clause: `1=1` from `VisitNew` with no arguments (SQL
  Server `:330`; the others `:256`), reached by `x => new Holder().Flag`; and `1=0` for `Contains` over an empty
  collection (SQL Server `SqlExpressionTranslator.cs:140`, PostgreSQL and MySQL `:122`, SQLite `:125`). Everything
  else is quoted or a parameter (the Task 0 reviewer's sweep).

**Probe (author at `be8de82`, extended by the Task 0 reviewer; net8.0, all four providers, sync and async; table
`(id, first_name, true_up)` with rows `(1,'a',1)`, `(2,'b',0)`, `(3,NULL,0)`; each call in a rolled-back
transaction):**

| Predicate | SQL Server | PostgreSQL, MySQL, SQLite |
|---|---|---|
| `x.FirstName == x.FirstName` | rejected (self-reference) | **deleted 2** (`WHERE t.first_name = t.first_name`) |
| `x.Id >= x.Id` | rejected (self-reference) | **deleted 3** |
| `x.Id == 2 \|\| true` | `SqlException`: non-boolean expression | **deleted 3** (`(t.id = @p0 OR @p1)`) |
| `x.Id == 2 \|\| includeAll` (captured `true`) | `SqlException` | **deleted 3** |
| `x.Id == 2 \|\| capturedA == capturedA` | **deleted 3** (`OR @p1 = @p2`) | **deleted 3** |
| `x.Id == 2 \|\| capturedNull == null` | **deleted 3** (`OR @p1 IS NULL`) | **deleted 3** |
| `!(x.Id != x.Id)` | **deleted 3** (`NOT t.id != t.id`) | **deleted 3** |
| `!(x.Id != x.Id && x.Id == 2)` | **deleted 3** | **deleted 3** |
| `!(x.Id != x.Id \|\| x.Id > x.Id)` | **deleted 3** | **deleted 3** |
| `x.Id == 2 \|\| StaticFlags.On` (static `true`) | `SqlException` | **deleted 3** |
| `x.Id == 2 \|\| new Holder().Flag` | rejected (trivial, through `1=1`) | rejected (trivial) |
| `x.TrueUp == 1` (column `true_up`) | **rejected as trivial** (false positive) | **rejected as trivial** |
| `x.Id == 2` | deleted 1 | deleted 1 |

So:
- **False negatives, with data loss, on every provider.** Check 3's `\b(\w+)\s*=\s*\1\b` never matches
  table-qualified text, so PostgreSQL, MySQL and SQLite accept every self-comparison. SQL Server's whole-text regex
  misses negated forms. A disjunction with a true operand reaches SQL as `OR @p`, `OR @p1 = @p2` or `OR @p IS NULL`;
  SQL Server rejects only the bare `OR @p` form.
- **The idioms this touches.** `filter == null || x.Col == filter` with `filter` null, and `isAdmin || x.OwnerId == me`
  with `isAdmin` true, delete every row today. After this change they throw.
- **False positives:** check 2 rejects any WHERE text containing `true` in any case, which is an identifier: a column
  `true_up`, a PascalCase `TrueUpAmount`, a table whose name contains `True`. Since 3.10.0's `Contains` fix this is
  case-insensitive on every build. The filed task also named `1<2`/`1>0` inside comparisons like `score1 < 20`; the
  probe shows they don't occur, because the right side is always a parameter.
- **The docs over-claim:** `Usage.md:1346-1348`, `README.md:41` and `docs/architecture/AI_ARCHITECTURE_AND_DESIGN.md:33`
  say `x.Id == x.Id` is blocked, and the architecture doc says the guard "analyzes the expression tree". Neither
  holds today on PostgreSQL, MySQL and SQLite.

**Assembly names (T0-5).** The SQL Server provider's assembly is `Funcular.Data.Orm`
(`Funcular.Data.Orm.SqlServer.csproj:7`); PostgreSQL, MySQL and SQLite use their project names. Core's
`InternalsVisibleTo("Funcular.Data.Orm.SqlServer")` (`Core/AssemblyInfo.cs:3`) names no assembly.

**Existing tests that pin the guard** (AC6):
- `SqlDataProviderIntegrationTests.Delete_TrivialWhereClause_ThrowsException` (`x => true` and `x => 1 < 2`: "must
  reference at least one column"; `x.FirstName == x.FirstName`: "self-referencing column expression"),
  `Delete_WhereClauseWithoutTableColumn_ThrowsException` (`"abc" == "abc"`),
  `Delete_WithEmptyWhereClause_ThrowsException` (sync, and async in `SqlDataProviderIntegrationAsyncTests:366`);
- SQLite's two (`SqliteDataProviderIntegrationTests:1281, 1310`);
- net48's `SqlDataProviderNetFrameworkIntegrationTests:1025`, `SqlDataProviderNetFrameworkIntegrationAsyncTests:342`
  (`DeleteAsync(x => true)`) and `DeleteGuardCaseTests`;
- the four suites' `ColdCacheDeleteTests`, which keep the cold-cache plan's D1 order for accepted predicates.

The C# compiler folds `1 < 2` and `1 == 1` to `true`, so those predicates reach the provider as a constant.
`EfSqlDataProviderIntegrationTests:1008` exercises a test-local EF Core wrapper, not FunkyORM's guard.

## 2. Decisions

- **D1 — An expression-tree guard in Core.** A new internal static class `DeletePredicateGuard`
  (`Funcular.Data.Orm.Core/DeletePredicateGuard.cs`) classifies the predicate before translation:
  `internal static DeletePredicateVerdict Classify(LambdaExpression predicate)`, with
  `DeletePredicateVerdict { Acceptable, NoColumn, SelfReference, AlwaysTrue }`, and
  `internal static void Validate(LambdaExpression predicate)`, which throws `InvalidOperationException` with the
  existing message for each rejection. Core gains `InternalsVisibleTo` for `Funcular.Data.Orm` (the SQL Server
  provider), `Funcular.Data.Orm.PostgreSql`, `Funcular.Data.Orm.MySql` and `Funcular.Data.Orm.Sqlite`.
- **D2 — Classification rules.** The **root** is any `ParameterExpression` whose type is the entity type, as the
  visitors accept (T0-7). A **member chain** is a run of member reads ending at a root, through `Convert`.
  1. **NoColumn** when the body has no member chain. Covers `x => true`, `x => 1 < 2`, `x => "abc" == "abc"`, a
     captured bool, and `x => false`: the same message as today.
  2. Otherwise fold the body to **True / False / Unknown**:
     - a `bool` constant is its value;
     - a parameter-free `bool` subtree built only from constants, field and property reads (rooted at a constant or
       static), `Convert`/`ConvertChecked`, `Not`, `AndAlso`/`OrElse`/`And`/`Or`, the six comparisons and
       `Conditional` is compiled and evaluated, provided that **no node in it carries a method** other than one
       declared in the core library's assembly (`typeof(object).Assembly`: `string`, `decimal`, `DateTime`, …).
       Any other parameter-free subtree is Unknown: the guard never invokes a user method, a user-defined operator or
       conversion, or a constructor (T0-6). An evaluation that throws is Unknown.
     - `Not` inverts; `AndAlso`/`And` on `bool` is False if either side is False, True if both are True, else
       Unknown; `OrElse`/`Or` on `bool` is True if either side is True, False if both are False, else Unknown;
     - `Conditional` folds to its known branch when the test is known, else to the branches' shared known value,
       else Unknown;
     - `Convert` on a `bool`/`bool?` folds its operand;
     - a comparison of two structurally equal member chains (same members in order; roots of the entity type) is
       True for `==`, `>=`, `<=` and False for `!=`, `>`, `<`;
     - everything else is Unknown.
  3. If the fold is True: **SelfReference** when the body's top node, after stripping `Not` and `Convert`, is such a
     self-comparison; otherwise **AlwaysTrue**. Messages: SelfReference "Delete operation WHERE clause cannot be a
     self-referencing column expression."; AlwaysTrue "Delete operation requires a non-trivial WHERE clause."
  4. Unknown or False: **Acceptable**. `x.Id == x.Id && x.Id == 2` is accepted (True ∧ Unknown), as it deletes what
     `x.Id == 2` does.
- **D3 — A token-aware SQL check replaces the substring list.** `internal static bool HasLiteralTautology(string
  whereClause)` in `DeletePredicateGuard`:
  - drop quoted segments (`'…'` with `''` escapes, `"…"`, `[…]`, `` `…` ``);
  - find comparisons of two numeric literals (`=`, `<>`, `!=`, `<`, `>`, `<=`, `>=`) that are a **whole boolean
    term**: preceded, ignoring whitespace, by the start of the text, `(`, `AND` or `OR`, with any number of `NOT`s
    between; followed by the end of the text, `)`, `AND` or `OR` (keywords case-insensitive, whole words) (T0-2);
  - return true if any such term holds after applying its `NOT`s (`1=1`, `1 < 2`, `NOT 1=0`), false otherwise
    (`1=0`, `NOT 1=1`).

  It is the backstop for parameter-free subtrees D2 doesn't evaluate (the visitor's `1=1`) and for a negated empty
  `Contains` (`OR NOT 1=0`). A literal next to arithmetic or inside `CASE WHEN … THEN` is not a whole term, so a
  `[SqlExpression]` like `CASE WHEN {Id} * 100 > 50 THEN 1 ELSE 0 END` isn't flagged. A true term anywhere is
  rejected, even inside `AND` (conservative, as today).
- **D4 — Kept as they are** (owner's brief): check 1 (empty), check 3 (both self-reference regexes) and check 4
  (column reference). D2 is the real self-reference detection; the regexes stay as a second line.
- **D5 — Order in each Delete/DeleteAsync by predicate:** transaction guard → null guard →
  `DeletePredicateGuard.Validate(predicate)` → `GenerateWhereClause` → empty check → `HasLiteralTautology` →
  self-reference regex → column check → DELETE. A predicate rejected by D2 never discovers `T` or reaches the
  database, so on a missing table it reports the guard's message, not discovery's error. Accepted predicates keep
  the cold-cache plan's D1 order.
- **D6 — SQL Server's two inline copies become one private `ValidateDeleteWhereClause<T>(string)`**, called from
  Delete and DeleteAsync, with SQL Server's own regex. The uncalled `ValidateWhereClause<T>` (`:1524`, with an
  `@p__linq__0` pattern) is deleted. This is a refactor checked in the diff, not an acceptance criterion (T0-10).
- **D7 — Documents.**
  - Changelog `[3.10.0-beta1]` Fixed: self-comparisons, their negations, and disjunctions with an always-true
    operand (a `true` literal or captured value, a parameter-free comparison that holds) deleted rows; they're now
    rejected before any SQL runs. Which provider deleted which shape is §1's table; the entry names the shapes, not
    a provider list. Predicates on tables or columns whose names contain `true` were rejected as trivial and are now
    accepted.
  - Changelog Changed: the idioms that now throw (`filter == null || x.Col == filter` with `filter` null;
    `isAdmin || x.OwnerId == me` with `isAdmin` true), each with the alternative (branch in C# and don't call Delete).
  - The 3.10 Changed bullet about the netstandard2.0/net48 guard becoming case-insensitive is deleted, with the
    Fixed line's pointer to it: the guard no longer calls `Contains`.
  - Scoped to "a predicate the guard accepts" (T0-3): the Changelog Changed bullet on the missing-table error
    (`Changelog.md:178-181`), SQL Server's XML doc on DeleteAsync/Delete (`SqlServerOrmDataProvider.cs:270-272` and its
    sync twin), and the cold-cache plan's D1 error-shape sentence.
  - SQL Server's XML doc comments on Delete/DeleteAsync, `Usage.md:314, 1346-1348`, `README.md:41` and
    `docs/architecture/AI_ARCHITECTURE_AND_DESIGN.md:33` describe what the guard rejects, and don't claim that every
    always-true predicate is (T0-9). `docs/ai-instructions/FUNKYORM_AI_INSTRUCTIONS.md:149` stays true.
  - The provider-scoped caches plan's §4.2 note on the guard's reach and its §4.4 net48 line get a pointer here.
- **D8 — `DeleteGuardCaseTests` (net48) is inverted:** its three rows delete exactly the matching row. They become
  net48's instance of AC3.

## 3. Acceptance criteria

- **AC1 — No trivial delete runs.** On all four providers, Delete and DeleteAsync by predicate reject, before any SQL
  is sent and before `T` is discovered, a predicate that D2 folds to True, with no row deleted:
  - a self-comparison of a member chain by `==`, `>=` or `<=`, also through `Convert`, and the negation of one by
    `!=`, `>` or `<` (SelfReference message);
  - any other body that folds to True: a disjunction with a `true` literal, a captured or static `true`, or a
    parameter-free comparison that holds; the De Morgan forms (`!(x.Id != x.Id && …)`,
    `!(x.Id != x.Id || x.Id > x.Id)`); a Conditional whose chosen or shared branch is True (AlwaysTrue message).
- **AC2 — Parameter-free predicates keep their message.** `x => true`, `x => 1 < 2`, `x => "abc" == "abc"`, a captured
  `true`, and `x => false` are rejected with "must reference at least one column…", on all four providers, sync and
  async.
- **AC3 — Identifiers containing `true` are accepted.** A predicate on a convention column `true_up`, or on a table
  named `zz_dg_TrueUp`, deletes exactly the matching rows on all four providers on net8.0, sync and async. On net48,
  SQL Server, a PascalCase column `TrueUpAmount` and a table named `zz_guard_TrueUp` do too (D8).
- **AC4 — Literal tautologies in the SQL are rejected.** A translated WHERE clause containing a true literal
  comparison that is a whole boolean term (D3) is rejected with the AlwaysTrue message; a false one, or one inside
  arithmetic or `CASE`, isn't. Integration: `x.Id == 2 || new Holder().Flag` (the visitor's `1=1`) and
  `x.Id == 2 || !emptyIds.Contains(x.Id)` (`OR NOT 1=0`) are rejected, and `emptyIds.Contains(x.Id) || x.Id == 2`
  (`1=0`) deletes exactly row 2, on all four providers, sync and async.
- **AC5 — Legitimate predicates are unaffected.** Equality, `&&`/`||` of column comparisons, a list `Contains`,
  `StartsWith`, `x.Id == x.Id && x.Id == 2`, and a `[SqlExpression]` member with arithmetic next to a literal
  comparison each delete exactly the matching rows, on all four providers, sync and async.
- **AC6 — The existing guard tests pass unchanged** (§1).
- **AC7 — The documents of D7 are true.**
- **AC8 — No regression:** SqlServer.Tests, PostgreSql.Tests, MySql.Tests, Sqlite.Tests, DotNet9 and net48 pass;
  `DeletePredicateGuard.cs` and every touched provider file reach 85 % line coverage, or stay at their baseline when
  already below it, with the baseline recorded.

## 4. Test plan

### 4.1 AC → test matrix

Each row is **Red** (fails at the seam, Task 1, for the stated reason) or a **Guard** (passes at the seam, with a
named killing mutation, §4.3). Every row is run alone at the seam and its outcome recorded.

**DB-free, `Funcular.Data.Orm.SqlServer.Tests/DeleteGuard/DeletePredicateGuardTests.cs`.** Expected verdicts:
SR = SelfReference, AT = AlwaysTrue, NC = NoColumn, OK = Acceptable.

| AC | Test | Class at the seam |
|---|---|---|
| AC1, AC2, AC5 | `Classify_ReturnsTheVerdict`, one DataRow per shape. Self-comparisons: `x.Id == x.Id` SR, `x.Id >= x.Id` SR, `x.Id <= x.Id` SR, `x.Id != x.Id` OK, `x.Id > x.Id` OK, `x.Id < x.Id` OK, `!(x.Id != x.Id)` SR, `!(x.Id > x.Id)` SR, `(long)x.Id == (long)x.Id` SR, `(int?)x.Id == (int?)x.Id` SR, `x.Name == x.Name` SR, `x.Id == x.Other` OK. Or: `x.Id == 2 \|\| true` AT, `\|\| capturedTrue` AT, `\|\| StaticFlags.On` AT, `\|\| DateTime.Now > DateTime.MinValue` AT, `\|\| capturedA == capturedA` AT, `\|\| capturedNull == null` AT, `\|\| capturedS == "s"` AT, `\|\| capturedFalse` OK, `x.Id == 2 \| true` AT (non-short-circuit). And: `x.Id == 2 && true` OK, `x.Id == x.Id && x.Id == 2` OK, `x.Id == x.Id \|\| x.Id == 2` AT, `x.Id == 2 & false` OK. De Morgan: `!(x.Id != x.Id && x.Id == 2)` AT, `!(x.Id != x.Id \|\| x.Id > x.Id)` AT, `!(x.Id == 2 && capturedFalse)` AT. Conditional: `capturedTrue ? x.Id == x.Id : x.Id == 2` AT, `capturedFalse ? x.Id == 2 : x.Id == x.Id` AT, `x.Flag ? x.Id == x.Id : x.Id >= x.Id` AT, `x.Flag ? x.Id == x.Id : x.Id == 2` OK. Not evaluated: `x.Id == 2 \|\| new Holder().Flag` OK, `\|\| Method()` OK, `\|\| (bool)capturedWrapper` OK (user `explicit operator bool`), `\|\| capturedW == capturedW2` OK (user `operator ==`), `\|\| capturedObj.Throws` OK (the read throws). Roots: a hand-built lambda whose body uses another parameter of the entity type, `x'.Id == 2` OK and `x'.Id == x'.Id` SR. Parameter-free: `true`, `1 < 2`, `"abc" == "abc"`, `capturedTrue`, `false` NC. | Red for the SR, AT and NC rows (the seam returns OK); Guard for the OK rows |
| AC1 | `Classify_NeverInvokesUserCode` (counters on `Method()`, the `Holder` constructor, the user `explicit operator bool` and the user `operator ==`, each in a disjunction; every count stays 0) | Guard |
| AC1, AC2 | `Validate_ThrowsTheMessageOfEachVerdict` [NoColumn, SelfReference, AlwaysTrue: exact message; Acceptable: no throw] | Red for the three rejections; the Acceptable row is a Guard |
| AC4 | `HasLiteralTautology_FindsOnlyTrueWholeTerms`. True: `1=1`, `1 = 1`, `(t.id = @p__linq__0 OR 1=1)`, `1 < 2`, `2 >= 1`, `1.5 > 1`, `1 != 2`, `t.id = @p OR NOT 1=0`, `NOT NOT 1=1`, `(1=1)`. False: `1=0`, `1 <> 1`, `NOT 1=1`, `t.col1 = 1`, `t.col1=1`, `@p__linq__1 = 1`, `$1 = 1`, `'1=1'`, `"1"=1`, `[1]=1`, `` `1`=1 ``, `t.c = 'a''1=1'`, `t.a * 100 > 50`, `t.a - 1 >= 0`, `5 < 10 * t.a`, `CASE WHEN t.a - 1 >= 0 THEN 1 ELSE 0 END = @p`, `CASE WHEN 1 = 1 THEN 1 END = @p` | Red for the true rows (the seam returns false); Guard for the false rows |

**Integration, a shared harness `Funcular.Data.Orm.SqlServer.Tests/DeleteGuard/DeleteGuardHarness.cs`, compiled into
SqlServer.Tests (SQL Server and SQLite classes), PostgreSql.Tests and MySql.Tests, as `LinqScopeHarness` is.**
- Each row creates `zz_dg_row (id, first_name, true_up, big)` with rows `(1,'a',1,…)`, `(2,'b',0,…)`, `(3,NULL,0,…)`,
  and `zz_dg_TrueUp (id, amount)` with `(1,5)`, `(2,7)`. The DDL, the `[Table]` names and the cleanup use identical
  case (MySQL on Linux CI has `lower_case_table_names=0`). The tables are dropped in `finally`. Each call runs in its
  own transaction.
- A rejected call asserts the exact message, that no `DELETE` was logged, and that all rows remain, counted through
  the provider inside the transaction before the rollback.
- An accepted call asserts the deleted count and the surviving ids.
- Every row has a `sync`/`async` DataRow.

| AC | Test | SQL Server | PostgreSQL | MySQL | SQLite | Class at the seam |
|---|---|---|---|---|---|---|
| AC1 | `SelfComparison_IsRejected` [`x.FirstName == x.FirstName`, `x.Id >= x.Id`, `x.Id <= x.Id`, `!(x.Id != x.Id)`, `(long)x.Id == (long)x.Id`] | ✓ | ✓ | ✓ | ✓ | Red on PostgreSQL, MySQL and SQLite (rows deleted); on SQL Server Red for `!(x.Id != x.Id)` only (its regex catches the others, `(long)` included, since the visitor drops `Convert`) |
| AC1 | `AlwaysTrue_IsRejected` [`x.Id == 2 \|\| true`, `\|\| capturedTrue`, `\|\| StaticFlags.On`, `\|\| capturedA == capturedA`, `\|\| capturedNull == null`, `!(x.Id != x.Id && x.Id == 2)`, `!(x.Id != x.Id \|\| x.Id > x.Id)`] | ✓ | ✓ | ✓ | ✓ | Red: rows deleted, or on SQL Server for the bare-parameter forms a `SqlException`, not the message |
| AC1 | `RejectedOnAMissingTable_ReportsTheGuardNotDiscovery` [`x.Id == x.Id`, `x.Id == 2 \|\| true`] on a cold type whose table doesn't exist (T0-3) | ✓ | ✓ | ✓ | ✓ | Red (discovery's error, or `SqliteException`, is thrown first) |
| AC2 | `ParameterFree_IsRejectedAsNoColumn` [`true`, `1 < 2`, `"abc" == "abc"`, `capturedTrue`, `false`] | ✓ | ✓ | ✓ | ✓ | Guard |
| AC3 | `IdentifierContainingTrue_IsAccepted` [`x.TrueUp == 1` (column `true_up`), the `zz_dg_TrueUp` table's `x.Amount == 5`] | ✓ | ✓ | ✓ | ✓ | Red (rejected as trivial) |
| AC4 | `LiteralTautologyInSql_IsRejected` [`x.Id == 2 \|\| new Holder().Flag`, `x.Id == 2 \|\| !emptyIds.Contains(x.Id)`] | ✓ | ✓ | ✓ | ✓ | Guard for `1=1` (the substring list catches it); Red for `NOT 1=0` (rows deleted) |
| AC4, AC5 | `NonTrivialPredicates_DeleteTheMatchingRows` [`x.Id == 2`; `x.Id == 2 && x.FirstName == "b"`; `x.Id == 1 \|\| x.Id == 2`; `ids.Contains(x.Id)`; `x.FirstName.StartsWith("b")`; `x.Id == x.Id && x.Id == 2`; `emptyIds.Contains(x.Id) \|\| x.Id == 2`; `x.Big == 1 && x.Id == 2` with `[SqlExpression("CASE WHEN {Id} * 100 > 50 THEN 1 ELSE 0 END")] Big`] | ✓ | ✓ | ✓ | ✓ | Guard |
| AC3 | `DeleteGuardCaseTests` (net48, D8): `TrueUpAmount == 5` sync and async, and the `zz_guard_TrueUp` table, each delete exactly row 1 | ✓ (net48) | — | — | — | Red (rejected as trivial) |
| AC6 | the existing tests of §1 | ✓ (and net48) | ✓ (cold-cache) | ✓ (cold-cache) | ✓ | Guard |
| AC8 | the suites, DotNet9, net48 | ✓ | ✓ | ✓ | ✓ | — |

`Holder`, `Method()`, `StaticFlags`, the wrappers and the captured values are members of the test classes;
`Holder.Flag` and `StaticFlags.On` return true, `capturedObj.Throws` throws, and the user operators count their calls.

### 4.2 Interface coverage

| Member | Tests |
|---|---|
| `DeletePredicateGuard.Classify` | `Classify_ReturnsTheVerdict`, `Classify_NeverInvokesUserCode` |
| `DeletePredicateGuard.Validate` | `Validate_ThrowsTheMessageOfEachVerdict`; every harness row |
| `DeletePredicateGuard.HasLiteralTautology` | `HasLiteralTautology_FindsOnlyTrueWholeTerms`; `LiteralTautologyInSql_IsRejected`; the `emptyIds` and `[SqlExpression]` rows |
| `DeletePredicateVerdict` | `Classify_ReturnsTheVerdict` |
| Each provider's `Delete<T>(Expression)` and `DeleteAsync<T>(Expression)` | the harness rows, `sync` and `async` |
| SQL Server `ValidateDeleteWhereClause<T>` (new) | the SQL Server harness rows; AC6 |
| PostgreSQL, MySQL and SQLite `ValidateWhereClause<T>` (changed) | their harness rows |

**Coverage.** Coverlet per project; a file's coverage is the distinct lines across every cobertura `<class filename>`
element for it, unioned across the four suites. Baselines at `be8de82` are recorded per touched file before Task 2.

### 4.3 Mutations each key test must kill

| Mutation | Killed by |
|---|---|
| `Validate` not called in Delete (per provider) | `AlwaysTrue_IsRejected` and `SelfComparison_IsRejected` (`!(x.Id != x.Id)`), `sync` |
| `Validate` not called in DeleteAsync (per provider) | the same rows, `async` |
| `Validate` after `GenerateWhereClause` (per provider) | `RejectedOnAMissingTable_ReportsTheGuardNotDiscovery` |
| `OrElse`/`Or` doesn't short-circuit a True side | `AlwaysTrue_IsRejected`; the Or rows of Classify |
| `OrElse` "both False → False" dropped | Classify `!(x.Id != x.Id \|\| x.Id > x.Id)`; the harness row |
| `AndAlso` "either False → False" dropped | Classify `!(x.Id != x.Id && x.Id == 2)` and `!(x.Id == 2 && capturedFalse)`; the harness row |
| `AndAlso` with one True side folds to True | `x.Id == x.Id && x.Id == 2` rows (Classify and harness) |
| `And`/`Or` (non-short-circuit) ignored | Classify `x.Id == 2 \| true` |
| `Not` doesn't invert | `!(x.Id != x.Id)` rows |
| Conditional with a False test ignored | Classify `capturedFalse ? x.Id == 2 : x.Id == x.Id` |
| Conditional's shared branch value dropped | Classify `x.Flag ? x.Id == x.Id : x.Id >= x.Id` |
| Self-comparison ignores `Convert` | `(long)x.Id == (long)x.Id` and `(int?)…` rows |
| `!=`/`>`/`<` self-comparison folds to True | Classify `x.Id != x.Id`, `x.Id > x.Id`, `x.Id < x.Id` (OK) |
| Root by parameter identity instead of type | Classify's hand-built lambda rows |
| Parameter-free evaluation limited to literals | `\|\| capturedTrue`, `\|\| capturedA == capturedA` rows |
| Static-rooted reads not evaluated | `\|\| StaticFlags.On` rows; Classify `DateTime.Now > DateTime.MinValue` |
| The method restriction removed, or limited to comparisons | `Classify_NeverInvokesUserCode` (the conversion and operator counters) |
| Parameter-free evaluation invokes methods or `new` | `Classify_NeverInvokesUserCode` |
| The NoColumn check after the fold, or removed | `ParameterFree_IsRejectedAsNoColumn` (message); AC6's SQL Server test |
| SelfReference and AlwaysTrue messages swapped | the message assertions of both rejection rows |
| `HasLiteralTautology` ignores the token boundary | its `t.col1 = 1` and `@p__linq__1 = 1` rows |
| `HasLiteralTautology` ignores the whole-term rule | its arithmetic and `CASE` rows; the `[SqlExpression]` harness row |
| `HasLiteralTautology` ignores `NOT` | its `NOT 1=1` and `t.id = @p OR NOT 1=0` rows; the `!emptyIds.Contains` harness row |
| `HasLiteralTautology` rejects every literal comparison | its `1=0` row; the `emptyIds` harness row |
| `HasLiteralTautology` keeps quoted segments | its `'1=1'` and `[1]=1` rows |
| `HasLiteralTautology` not called (per provider) | `LiteralTautologyInSql_IsRejected` (on PostgreSQL, MySQL and SQLite the kept regex reports SelfReference for `1=1`, which fails the message assertion; on SQL Server the delete runs) |
| The old substring list kept | `IdentifierContainingTrue_IsAccepted`; `DeleteGuardCaseTests` |

### 4.4 Where each tier runs

- **CI:** `ci.yml` runs SqlServer.Tests, with the DB-free rows and the SQL Server and SQLite harness classes.
  `build-and-test-postgresql.yml` and `build-and-test-mysql.yml` run PostgreSql.Tests and MySql.Tests, with their
  harness classes, on Linux on every push to `development/**` (T0-8). Linux table-name case is verified there only.
- **Local, recorded with sha, before the merge:** all four suites, DotNet9, and net48 (MSBuild, then vstest; CI doesn't
  run it).

## 5. Tasks

1. **Task 0 — Test-plan review** (non-author) to CLEAN.
2. **Task 1 — The seam and the red tests.** Commit `DeletePredicateGuard` with `Classify` returning Acceptable,
   `Validate` doing nothing and `HasLiteralTautology` returning false, the `InternalsVisibleTo` grants, and the
   tests of §4.1. Run each row alone and record its class and message. Record the coverage baselines.
3. **Task 2 — D1–D6 and D8.** Green; the §4.3 mutations, each run; suites; coverage.
4. **Task 3 — Documents (D7).**
5. **Task 4 — Hostile review and fix-verification** to CLEAN; then merge `development/3.10` (after
   `fix/provider-scoped-caches` lands there), re-run the suites, and merge into `development/3.10` before the beta PR.

## 6. Out of scope (recorded)

- **Always-true shapes D2 doesn't detect** (T0-9). These deleted every row at the seam on all four providers and still
  will:
  - P ∨ ¬P: `x.Id == 2 || x.Id != 2`, `x.Id == 2 || !(x.Id == 2)`, `x.Flag || !x.Flag`;
  - self-comparisons through arithmetic or a method call (`x.Id + 0 == x.Id`, `x.Name.Trim() == x.Name.Trim()`,
    `x.Id.Equals(x.Id)`): D2 compares member chains only.

  The documents (D7) don't claim that every always-true predicate is rejected.
- **The double read** (T0-6). A property on a captured object is read once by the guard and again by the visitor. If
  its value changes between the reads, an Acceptable verdict can still send `OR @p` with a true value; D3 doesn't see
  parameters.
- **`[SqlExpression]` text is the user's SQL.** A fragment that is always true (for example `TRUE`) isn't detected.
- **SQL NULL semantics.** `x.A == x.A` excludes rows where `A` is NULL; it's still rejected, as no one writes it as a
  filter.
- **A bare `bool` parameter in SQL Server's WHERE** (`x.Id == 2 || false`, `&& true`) is invalid SQL there
  (pre-existing translation limit); the guard doesn't change it.
- **The column-reference check** (check 4) stays substring-based and can over-accept.
- **The dead `InternalsVisibleTo("Funcular.Data.Orm.SqlServer")`** in Core is left as it is.
- **Date-part translation in WHERE** is a separate pre-existing defect (provider-scoped caches plan §6, OBS-1); the
  owner placed its fix in 3.10.0 (decision 2026-10-02, given in chat), and its plan isn't written yet.

## 9. Review dispositions

### 9.1 Task 0 review of rev 1 (`49e535a`; non-author; the probe re-run and extended on all four providers, sync and async; a throwaway prototype of D1–D5 on all eight paths; 7 D2 rule mutations; the existing guard and cold-cache tests on the prototype)

Verdict: NOT CLEAN. The §1 probe and every citation reproduced, and the seam classes held except one SQL Server row
(T0-4). The prototype met AC1–AC5 for every rev 1 row, found no false rejection among the reviewer's shapes, and
passed the existing guard and cold-cache tests on all four providers. Blame: TEST-GAP 3, AC-GAP 1, PLAN-GAP 7.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| T0-1 | high | TEST-GAP | Seven D2 rule mutations survived every Classify and harness row (AND/OR False rules, static reads, the Conditional's False test and shared value, the operator restriction, non-short-circuit `&`/`\|`). Two of the uncovered shapes delete every row today on all four providers (`!(x.Id != x.Id && x.Id == 2)`, `!(x.Id != x.Id \|\| x.Id > x.Id)`); a static `true` does on three. | Classify rows for each rule both ways; the data-loss shapes are harness rows; each mutation has a §4.3 row. |
| T0-2 | high | AC-GAP | D3 as worded rejected `[SqlExpression]` predicates with arithmetic next to a literal comparison (`CASE WHEN {Id} * 100 > 50 …`), which delete correctly today; AC5 permitted the regression. | D3 flags only a literal comparison that is a whole boolean term; HasLiteralTautology rows and an AC5 harness row for the arithmetic forms. |
| T0-3 | medium | TEST-GAP | No row proved D5's order: with `Validate` after `GenerateWhereClause` every row still passed. D5 also makes three documents false for rejected predicates. | A cold missing-table row on all four providers and its mutation; D7 scopes the three documents. |
| T0-4 | medium | PLAN-GAP | SQL Server also deletes every row for `\|\| capturedA == capturedA`, `\|\| capturedNull == null` and the De Morgan forms; §1, the seam note and D7's provider list understated it, and the idioms whose behaviour changes weren't named. | §1's table and summary, the seam classes, D7 (shapes, not a provider list; the idioms in Changed), a `capturedNull` harness row. |
| T0-5 | medium | PLAN-GAP | "SQL Server has it" was false: the provider's assembly is `Funcular.Data.Orm`, and Core's grant names `Funcular.Data.Orm.SqlServer`. | D1 grants the four real assembly names; §1 records the names; the dead grant is left (§6). |
| T0-6 | low | PLAN-GAP | The method restriction covered comparisons only, so a user `explicit operator bool` ran during classification; the never-invokes test couldn't see it. The double read was unrecorded. | No evaluated node may carry a user method; counting rows for a user conversion and a user `operator ==`; the double read in §6. |
| T0-7 | low | PLAN-GAP | "The parameter" was undefined; a body using another parameter instance of the entity type was misclassified (NoColumn for a valid delete; a missed self-comparison). | The root is any parameter of the entity type; two Classify rows. |
| T0-8 | low | PLAN-GAP | §4.4 omitted the PostgreSQL and MySQL CI workflows, where MySQL's table names are case-sensitive. | §4.4 corrected; the harness uses identical case. |
| T0-9 | low | PLAN-GAP | P ∨ ¬P, `OR NOT 1=0`, and arithmetic or `Equals` self-comparisons aren't detected and weren't recorded. | D3 counts `NOT` (so `OR NOT 1=0` is rejected); the rest in §6; D7's documents don't over-claim. |
| T0-10 | nit | TEST-GAP | AC7 (one SQL Server method) was structural; its named proof couldn't fail. | D6 is a refactor checked in the diff; the AC is removed. |
| T0-11 | nit | PLAN-GAP | The AC6 list named an EF wrapper test, missed net48's async test and the cold-cache classes; `Validate_ThrowsTheMessageOfEachVerdict`'s Acceptable row is a Guard. | Corrected. |
