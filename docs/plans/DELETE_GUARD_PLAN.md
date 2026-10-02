# Delete guard: detect trivial predicates on the expression tree — Implementation Plan

> **Goal:** make the delete-by-predicate guard reject the predicates it claims to reject, on every provider, and stop
> it rejecting legitimate ones.
> - Ships in **3.10.0, before the beta PR** (owner decision 2026-10-02).
> - Branch `fix/delete-guard-trivial-predicates`, cut from `fix/provider-scoped-caches` at `be8de82`, which carries
>   the `GeneralExtensions.Contains` fix and `DeleteGuardCaseTests`. It merges into `development/3.10` after that
>   branch does, with `development/3.10` merged in first.
> - Started from a task the provider-scoped caches review filed (its §9.13 FVC-1 and §9.16).

> **Status (2026-10-02):** rev 1, Task 0 (test-plan review) next. Nothing is implemented.

## 1. Verified premises (author at `be8de82`; the probe is executed, the rest read from source)

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
- The only literal comparisons the visitors write: `1=1` from `VisitNew` with no arguments (SQL Server `:330`; the
  others `:256`), reached by `x => new Holder().Flag`; and `1=0` for `Contains` over an empty collection (SQL Server
  `SqlExpressionTranslator.cs:140`, PostgreSQL and MySQL `:122`, SQLite `:125`).

**Probe (executed at `be8de82`, net8.0, all four providers; table `zz_dgp_row (id, first_name, true_up)`, rows
`(1,'a',1)`, `(2,'b',0)`, `(3,NULL,0)`; each call in a rolled-back transaction):**

| Predicate | SQL Server | PostgreSQL, MySQL, SQLite |
|---|---|---|
| `x.FirstName == x.FirstName` | rejected (self-reference) | **deleted 2** (`WHERE t.first_name = t.first_name`) |
| `x.Id >= x.Id` | rejected (self-reference) | **deleted 3** |
| `x.Id == 2 \|\| true` | `SqlException`: non-boolean expression | **deleted 3** (`(t.id = @p0 OR @p1)`) |
| `x.Id == 2 \|\| includeAll` (captured `true`) | `SqlException` | **deleted 3** |
| `!(x.Id != x.Id)` | **deleted 3** (`NOT t.id != t.id`) | **deleted 3** |
| `x.Id == 2 \|\| new Holder().Flag` | rejected (trivial, through `1=1`) | rejected (trivial) |
| `x.TrueUp == 1` (column `true_up`) | **rejected as trivial** (false positive) | **rejected as trivial** |
| `x.Id == 2` | deleted 1 | deleted 1 |

So:
- **False negatives, with data loss:** check 3's `\b(\w+)\s*=\s*\1\b` never matches table-qualified text, so
  PostgreSQL, MySQL and SQLite accept every self-comparison. A disjunction with a true operand reaches SQL as
  `OR @p`, which those three evaluate. SQL Server's whole-text regex misses `NOT t.id != t.id`.
- **False positives:** check 2 rejects any WHERE text containing `true` in any case, which is an identifier: a column
  `true_up`, a PascalCase `TrueUpAmount`, a table whose name contains `True`. Since 3.10.0's `Contains` fix this is
  case-insensitive on every build. The filed task also named `1<2`/`1>0` inside comparisons like `score1 < 20`; the
  probe shows they don't occur, because the right side is always a parameter.
- **The docs over-claim:** `Usage.md:1346-1348`, `README.md:41` and `docs/architecture/AI_ARCHITECTURE_AND_DESIGN.md:33`
  say `x.Id == x.Id` is blocked, and the architecture doc says the guard "analyzes the expression tree". Neither
  holds today on PostgreSQL, MySQL and SQLite.

**Existing tests that pin the guard** (AC6): `SqlDataProviderIntegrationTests.Delete_TrivialWhereClause_ThrowsException`
(`x => true` and `x => 1 < 2`: "must reference at least one column"; `x.FirstName == x.FirstName`: "self-referencing
column expression"), `Delete_WhereClauseWithoutTableColumn_ThrowsException` (`"abc" == "abc"`),
`Delete_WithEmptyWhereClause_ThrowsException` (sync, and async in `SqlDataProviderIntegrationAsyncTests:366`), the
EF variant (`EfSqlDataProviderIntegrationTests:1008`), SQLite's two (`SqliteDataProviderIntegrationTests:1281, 1310`),
net48's `SqlDataProviderNetFrameworkIntegrationTests:1025` and `DeleteGuardCaseTests`. The C# compiler folds `1 < 2`
and `1 == 1` to `true`, so those predicates reach the provider as a constant.

## 2. Decisions

- **D1 — An expression-tree guard in Core.** A new internal static class `DeletePredicateGuard`
  (`Funcular.Data.Orm.Core/DeletePredicateGuard.cs`) classifies the predicate before translation:
  `internal static DeletePredicateVerdict Classify(LambdaExpression predicate)`, with
  `DeletePredicateVerdict { Acceptable, NoColumn, SelfReference, AlwaysTrue }`, and
  `internal static void Validate(LambdaExpression predicate)`, which throws `InvalidOperationException` with the
  existing message for each rejection. Core gains `InternalsVisibleTo` for `Funcular.Data.Orm.PostgreSql`, `.MySql`
  and `.Sqlite` (SQL Server has it).
- **D2 — Classification rules.**
  1. **NoColumn** when the body references no member of the lambda's parameter (a member chain whose root, through
     `Convert`, is the parameter). Covers `x => true`, `x => 1 < 2`, `x => "abc" == "abc"`, a captured bool, and
     `x => false`: the same message as today.
  2. Otherwise fold the body to **True / False / Unknown**:
     - a `bool` constant is its value;
     - a parameter-free `bool` subtree built only from constants, field and property reads (rooted at a constant or
       static), `Convert`/`ConvertChecked`, `Not`, the logical operators, the six comparisons and `Conditional` is
       compiled and evaluated. A comparison's operator method, if any, must be declared in the core library's
       assembly (`string`, `decimal`, `DateTime`, …). Any other parameter-free subtree is Unknown: the guard never
       invokes a method, a user-defined operator or a constructor. An evaluation that throws is Unknown. (A property
       read on a captured object therefore happens twice, in the guard and in the visitor, which reads it too.)
     - `Not` inverts; `AndAlso`/`And` on `bool` is False if either side is False, True if both are True, else
       Unknown; `OrElse`/`Or` on `bool` is True if either side is True, False if both are False, else Unknown;
     - `Conditional` folds to its known branch, or to the branches' shared known value, else Unknown;
     - `Convert` on a `bool`/`bool?` folds its operand;
     - a comparison of two structurally equal member chains rooted at the parameter (compared after stripping
       `Convert`) is True for `==`, `>=`, `<=` and False for `!=`, `>`, `<`;
     - everything else is Unknown.
  3. If the fold is True: **SelfReference** when the body's top node, after stripping `Not` and `Convert`, is such a
     self-comparison; otherwise **AlwaysTrue**. Messages: SelfReference "Delete operation WHERE clause cannot be a
     self-referencing column expression."; AlwaysTrue "Delete operation requires a non-trivial WHERE clause."
  4. Unknown or False: **Acceptable**. `x.Id == x.Id && x.Id == 2` is accepted (True ∧ Unknown), as it deletes what
     `x.Id == 2` does.
- **D3 — A token-aware SQL check replaces the substring list.** `internal static bool HasLiteralTautology(string
  whereClause)` in `DeletePredicateGuard`:
  - drop quoted segments (`'…'` with `''` escapes, `"…"`, `[…]`, `` `…` ``);
  - find comparisons of two numeric literals that are standalone tokens: not preceded by a word character, `@`, `$`,
    `.` or `:`, and not followed by a word character or `.`; operators `=`, `<>`, `!=`, `<`, `>`, `<=`, `>=`;
  - return true if any such comparison holds (`1=1`, `1 < 2`, `2 >= 1`), false otherwise (`1=0`).

  It is the backstop for parameter-free subtrees D2 doesn't evaluate (the `VisitNew` `1=1`). A true literal comparison
  anywhere is rejected, even inside `AND` (conservative, as today).
- **D4 — Kept as they are** (owner's brief): check 1 (empty), check 3 (both self-reference regexes) and check 4
  (column reference). D2 is the real self-reference detection; the regexes stay as a second line.
- **D5 — Order in each Delete/DeleteAsync by predicate:** transaction guard → null guard →
  `DeletePredicateGuard.Validate(predicate)` → `GenerateWhereClause` → empty check → `HasLiteralTautology` →
  self-reference regex → column check → DELETE. A predicate rejected by D2 never discovers `T` or reaches the
  database. Accepted predicates keep the cold-cache plan's D1 order.
- **D6 — SQL Server's two inline copies become one private `ValidateDeleteWhereClause<T>(string)`**, called from
  Delete and DeleteAsync, with SQL Server's own regex. The uncalled `ValidateWhereClause<T>` (`:1524`, with an
  `@p__linq__0` pattern) is deleted.
- **D7 — Documents.**
  - Changelog `[3.10.0-beta1]` Fixed: on PostgreSQL, MySQL and SQLite, self-comparisons and disjunctions with an
    always-true operand deleted rows, and on every provider `!(x.A != x.A)` did; they're now rejected before any SQL
    runs. On SQL Server, `x.A == 1 || true` failed with a SQL error and now gets the guard's message. Predicates on
    tables or columns whose names contain `true` were rejected as trivial and are now accepted.
  - The 3.10 Changed bullet about the netstandard2.0/net48 guard becoming case-insensitive is deleted, with the
    Fixed line's pointer to it: the guard no longer calls `Contains`.
  - SQL Server's XML doc comments on Delete/DeleteAsync, `Usage.md:314, 1346-1348`, `README.md:41` and
    `docs/architecture/AI_ARCHITECTURE_AND_DESIGN.md:33` describe what the guard rejects.
  - The provider-scoped caches plan's §4.2 note on the guard's reach and its §4.4 net48 line get a pointer here.
- **D8 — `DeleteGuardCaseTests` (net48) is inverted:** its three rows delete exactly the matching row. They become
  net48's instance of AC3.

## 3. Acceptance criteria

- **AC1 — No trivial delete runs.** On all four providers, Delete and DeleteAsync by predicate reject, before any SQL
  is sent, a predicate that D2 folds to True, with no row deleted:
  - a self-comparison of a member chain by `==`, `>=` or `<=`, also through `Convert`, and the negation of one by
    `!=`, `>` or `<` (SelfReference message);
  - a disjunction with an operand that is a `true` literal, a captured `true`, or a parameter-free comparison that
    holds (AlwaysTrue message).
- **AC2 — Parameter-free predicates keep their message.** `x => true`, `x => 1 < 2`, `x => "abc" == "abc"`, a captured
  `true`, and `x => false` are rejected with "must reference at least one column…", on all four providers, sync and
  async.
- **AC3 — Identifiers containing `true` are accepted.** A predicate on a convention column `true_up`, or on a table
  named `zz_dg_TrueUp`, deletes exactly the matching rows on all four providers on net8.0, sync and async. On net48,
  SQL Server, a PascalCase column `TrueUpAmount` and a table named `zz_guard_TrueUp` do too (D8).
- **AC4 — Literal tautologies in the SQL are rejected.** A translated WHERE clause containing a true comparison of two
  standalone numeric literals is rejected with the AlwaysTrue message; a false one isn't. Integration:
  `x.Id == 2 || new Holder().Flag` (the visitor's `1=1`) is rejected, and `emptyIds.Contains(x.Id) || x.Id == 2` (the
  translator's `1=0`) deletes exactly row 2, on all four providers, sync and async.
- **AC5 — Legitimate predicates are unaffected.** Equality, `&&`/`||` of column comparisons, a list `Contains`,
  `StartsWith`, and `x.Id == x.Id && x.Id == 2` each delete exactly the matching rows, on all four providers, sync and
  async.
- **AC6 — The existing guard tests pass unchanged** (§1).
- **AC7 — SQL Server has one set of SQL checks** (D6): Delete and DeleteAsync call `ValidateDeleteWhereClause<T>`, and
  the uncalled method is gone.
- **AC8 — The documents of D7 are true.**
- **AC9 — No regression:** SqlServer.Tests, PostgreSql.Tests, MySql.Tests, Sqlite.Tests, DotNet9 and net48 pass;
  `DeletePredicateGuard.cs` and every touched provider file reach 85 % line coverage, or stay at their baseline when
  already below it, with the baseline recorded.

## 4. Test plan

### 4.1 AC → test matrix

Each row is **Red** (fails at the seam, Task 1, for the stated reason) or a **Guard** (passes at the seam, with a
named killing mutation, §4.3). Every row is run alone at the seam and its outcome recorded.

**DB-free, `Funcular.Data.Orm.SqlServer.Tests/DeleteGuard/DeletePredicateGuardTests.cs`:**

| AC | Test | Class at the seam |
|---|---|---|
| AC1, AC2, AC5 | `Classify_ReturnsTheVerdict` [DataRows: each D2 rule, both truth values where it has two: `x.Id == x.Id`, `x.Id >= x.Id`, `x.Id <= x.Id`, `x.Id != x.Id` (Acceptable), `x.Id > x.Id` (Acceptable), `!(x.Id != x.Id)`, `!(x.Id > x.Id)`, `(long)x.Id == (long)x.Id`, `x.Name == x.Name`, `x.Id == x.Other` (Acceptable), `x.Id == 2 \|\| true`, `x.Id == 2 \|\| capturedTrue`, `x.Id == 2 \|\| capturedA == capturedA`, `x.Id == 2 \|\| capturedFalse` (Acceptable), `x.Id == 2 && true` (Acceptable), `x.Id == x.Id && x.Id == 2` (Acceptable), `x.Id == x.Id \|\| x.Id == 2` (AlwaysTrue), `capturedTrue ? x.Id == x.Id : x.Id == 2` (AlwaysTrue: the top node is a Conditional), `x.Id == 2 \|\| capturedS == "s"` (AlwaysTrue: `string.op_Equality` is a core-library operator), `x.Id == 2 \|\| capturedObj.Throws` (Acceptable: the read throws), `x.Id == 2 \|\| new Holder().Flag` (Acceptable: not evaluated), `x.Id == 2 \|\| Method()` (Acceptable: not invoked), `true`, `1 < 2`, `"abc" == "abc"`, `capturedTrue`, `false` (NoColumn)] | Red (the seam returns Acceptable) except the Acceptable rows (Guards) |
| AC1 | `Classify_NeverInvokesMethodsOrConstructors` (a counting `Method()` and a counting `Holder` constructor in a disjunction; the count stays 0) | Guard |
| AC1, AC2 | `Validate_ThrowsTheMessageOfEachVerdict` [NoColumn, SelfReference, AlwaysTrue: exact message; Acceptable: no throw] | Red |
| AC4 | `HasLiteralTautology_FindsOnlyStandaloneTrueLiteralComparisons` [`1=1`, `1 = 1`, `(t.id = @p__linq__0 OR 1=1)`, `1 < 2`, `2 >= 1`, `1.5 > 1`, `1 != 2` → true; `1=0`, `1 <> 1`, `t.col1 = 1`, `t.col1=1`, `@p__linq__1 = 1`, `$1 = 1`, `'1=1'`, `"1"=1`, `[1]=1`, `` `1`=1 ``, `x.1=1`, `t.c = 'a''1=1'` → false] | Red for the true rows (the seam returns false) |

**Integration, a shared harness `Funcular.Data.Orm.SqlServer.Tests/DeleteGuard/DeleteGuardHarness.cs`, compiled into
SqlServer.Tests (SQL Server and SQLite classes), PostgreSql.Tests and MySql.Tests, as `LinqScopeHarness` is.** Each
row creates `zz_dg_row (id, first_name, true_up)` with rows `(1,'a',1)`, `(2,'b',0)`, `(3,NULL,0)`, and
`zz_dg_TrueUp (id, amount)` with `(1,5)`, `(2,7)`, drops them in `finally`, and runs each call in its own transaction.
A rejected call asserts the exact message, that no `DELETE` was logged, and that all rows remain, counted through the
provider inside the transaction before the rollback. An accepted call asserts the deleted count and the surviving ids.
Every row has a `sync`/`async` DataRow.

| AC | Test | SQL Server | PostgreSQL | MySQL | SQLite | Class at the seam |
|---|---|---|---|---|---|---|
| AC1 | `SelfComparison_IsRejected` [`x.FirstName == x.FirstName`, `x.Id >= x.Id`, `x.Id <= x.Id`, `!(x.Id != x.Id)`, `(long)x.Id == (long)x.Id`] | ✓ | ✓ | ✓ | ✓ | Red on PostgreSQL, MySQL and SQLite (rows deleted); on SQL Server Red for `!(x.Id != x.Id)` only (its regex catches the others, `(long)` included, since the visitor drops `Convert`) |
| AC1 | `AlwaysTrueDisjunction_IsRejected` [`x.Id == 2 \|\| true`, `\|\| capturedTrue`, `\|\| capturedA == capturedA`] | ✓ | ✓ | ✓ | ✓ | Red (rows deleted; on SQL Server a `SqlException`, not the message) |
| AC2 | `ParameterFree_IsRejectedAsNoColumn` [`true`, `1 < 2`, `"abc" == "abc"`, `capturedTrue`, `false`] | ✓ | ✓ | ✓ | ✓ | Guard |
| AC3 | `IdentifierContainingTrue_IsAccepted` [`x.TrueUp == 1` (column `true_up`), the `zz_dg_TrueUp` table's `x.Amount == 5`] | ✓ | ✓ | ✓ | ✓ | Red (rejected as trivial) |
| AC4 | `LiteralTautologyInSql_IsRejected` [`x.Id == 2 \|\| new Holder().Flag`] | ✓ | ✓ | ✓ | ✓ | Guard (the substring list catches `1=1`) |
| AC4, AC5 | `NonTrivialPredicates_DeleteTheMatchingRows` [`x.Id == 2`; `x.Id == 2 && x.FirstName == "b"`; `x.Id == 1 \|\| x.Id == 2`; `ids.Contains(x.Id)`; `x.FirstName.StartsWith("b")`; `x.Id == x.Id && x.Id == 2`; `emptyIds.Contains(x.Id) \|\| x.Id == 2`] | ✓ | ✓ | ✓ | ✓ | Guard |
| AC3 | `DeleteGuardCaseTests` (net48, D8): `TrueUpAmount == 5` sync and async, and the `zz_guard_TrueUp` table, each delete exactly row 1 | ✓ (net48) | — | — | — | Red (rejected as trivial) |
| AC6 | the existing tests of §1 | ✓ | — | — | ✓ | Guard |
| AC7 | a source scan is no proof (taxonomy 21): the AC7 rows are the SQL Server harness rows, both paths | ✓ | — | — | — | as above |
| AC9 | the suites, DotNet9, net48 | ✓ | ✓ | ✓ | ✓ | — |

`Holder`, `Method()`, `capturedTrue`, `capturedFalse`, `capturedA`, `capturedS`, `capturedObj`, `ids` and `emptyIds`
are fields or locals of the test classes; `Holder.Flag` returns true and `capturedObj.Throws` throws.

### 4.2 Interface coverage

| Member | Tests |
|---|---|
| `DeletePredicateGuard.Classify` | `Classify_ReturnsTheVerdict`, `Classify_NeverInvokesMethodsOrConstructors` |
| `DeletePredicateGuard.Validate` | `Validate_ThrowsTheMessageOfEachVerdict`; every harness row |
| `DeletePredicateGuard.HasLiteralTautology` | `HasLiteralTautology_FindsOnlyStandaloneTrueLiteralComparisons`; `LiteralTautologyInSql_IsRejected`; the `emptyIds` row |
| `DeletePredicateVerdict` | `Classify_ReturnsTheVerdict` |
| Each provider's `Delete<T>(Expression)` and `DeleteAsync<T>(Expression)` | the harness rows, `sync` and `async` |
| SQL Server `ValidateDeleteWhereClause<T>` (new) | the SQL Server harness rows; AC6 |
| PostgreSQL, MySQL and SQLite `ValidateWhereClause<T>` (changed) | their harness rows |

**Coverage.** Coverlet per project; a file's coverage is the distinct lines across every cobertura `<class filename>`
element for it, unioned across the four suites. Baselines at `be8de82` are recorded per touched file before Task 2.

### 4.3 Mutations each key test must kill

| Mutation | Killed by |
|---|---|
| `Validate` not called in Delete (per provider) | `SelfComparison_IsRejected` and `AlwaysTrueDisjunction_IsRejected`, `sync` |
| `Validate` not called in DeleteAsync (per provider) | the same rows, `async` |
| `OrElse` doesn't short-circuit a True side | `AlwaysTrueDisjunction_IsRejected`; Classify rows |
| `Not` doesn't invert | `!(x.Id != x.Id)` rows |
| Self-comparison ignores `Convert` | `(long)x.Id == (long)x.Id` rows |
| `!=`/`>`/`<` self-comparison folds to True | Classify `x.Id != x.Id` and `x.Id > x.Id` rows (Acceptable) |
| `AndAlso` with one True side folds to True | `x.Id == x.Id && x.Id == 2` rows (Classify and harness) |
| Parameter-free evaluation limited to literals | `\|\| capturedTrue`, `\|\| capturedA == capturedA` rows |
| Parameter-free evaluation also invokes methods and `new` | `Classify_NeverInvokesMethodsOrConstructors` |
| The NoColumn check after the fold, or removed | `ParameterFree_IsRejectedAsNoColumn` (message); AC6's SQL Server test |
| SelfReference and AlwaysTrue messages swapped | the message assertions of both rejection rows |
| `HasLiteralTautology` ignores the token boundary | its `t.col1 = 1` and `@p__linq__1 = 1` rows |
| `HasLiteralTautology` rejects every literal comparison | its `1=0` row; the `emptyIds` harness row |
| `HasLiteralTautology` keeps quoted segments | its `'1=1'` and `[1]=1` rows |
| `HasLiteralTautology` not called (per provider) | `LiteralTautologyInSql_IsRejected` |
| The old substring list kept | `IdentifierContainingTrue_IsAccepted`; `DeleteGuardCaseTests` |

### 4.4 Where each tier runs

- **CI (`ci.yml`):** SqlServer.Tests, with the DB-free rows and the SQL Server and SQLite harness classes.
- **Local, recorded with sha, before the merge:** PostgreSql.Tests and MySql.Tests (their harness classes),
  Sqlite.Tests, DotNet9, and net48 (MSBuild, then vstest; CI doesn't run it).

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

- **`[SqlExpression]` text is the user's SQL.** A fragment that is always true (for example `TRUE`) isn't detected.
- **Self-comparison through method calls** (`x.Name.Trim() == x.Name.Trim()`) isn't detected: D2 compares member
  chains only.
- **SQL NULL semantics.** `x.A == x.A` excludes rows where `A` is NULL; it's still rejected, as no one writes it as a
  filter.
- **A bare `bool` parameter in SQL Server's WHERE** (`x.Id == 2 || false`, `&& true`) is invalid SQL there
  (pre-existing translation limit); the guard doesn't change it.
- **The column-reference check** (check 4) stays substring-based and can over-accept.
- **Non-nullable date parts in WHERE** are a separate defect with its own plan (provider-scoped caches plan §6, OBS-1).

## 9. Review dispositions

(None yet.)
