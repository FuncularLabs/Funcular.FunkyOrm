# Delete guard: detect trivial predicates on the expression tree — Implementation Plan

> **Goal:** make the delete-by-predicate guard reject the predicates it claims to reject, on every provider, and stop
> it rejecting legitimate ones.
> - Ships in **3.10.0, before the beta PR** (owner decision 2026-10-02).
> - Branch `fix/delete-guard-trivial-predicates`, cut from `fix/provider-scoped-caches` at `be8de82`, which carries
>   the `GeneralExtensions.Contains` fix and `DeleteGuardCaseTests`. It merges into `development/3.10` after that
>   branch does, with `development/3.10` merged in first.
> - Started from a task the provider-scoped caches review filed (its §9.13 FVC-1 and §9.16).

> **Status (2026-10-02):** rev 11. Task 0 review of rev 1 (`49e535a`, §9.1) found 11 findings, answered in rev 2
> (`79069dd`). Its re-review (§9.2) found 9, answered in rev 3 (`edfc072`). Its re-review (§9.3) found 6, answered in
> rev 4 (`fa06956`). Its re-review (§9.4) found 4 blocking, answered in rev 5 (`5845883`). Its re-review (§9.5) found
> 4 blocking, answered in rev 6 (`58efaca`). Its re-review (§9.6) is CLEAN. Task 1 is `0167f63` (§5.1), Task 2
> `bcd254f` (§5.2), and Task 3 `3ec5ba9` (§5.3). Task 4's review of `58efaca..3ec5ba9` (§9.7) found 5 blocking
> findings, answered in rev 9 (`09033fa`, §5.4). Its fix-verification (§9.8) found 4 blocking, answered in rev 10
> (`fcc560c`, §5.5). Its re-verification (§9.9) found 2 blocking, in prose; the commit that carries rev 11 answers
> them (§5.6) and is re-verified next.

> **Revision 11 — what changed (fix-verification FV2-1, FV2-2):** §6, the Changelog and Usage name the collation a
> captured string's `Contains` compares under; `HasLiteralTautology`'s XML doc describes the large-stack parse.

> **Revision 10 — what changed (fix-verification FV1-1…FV1-4):** D2 evaluates `ToString` with a format string, and
> counts a `Contains` with a null search value as True (FV1-1, FV1-2). D3 parses a clause too deep for the calling
> thread again on a 64 MB stack, and matches parentheses and `CASE … END` in one pass (FV1-3). Rows and mutations for
> each, and for two rules that keep user code from running (FV1-4). §6 discloses where the database disagrees with C#
> (FV1-2). §5.5 and §9.8 record the round.

> **Revision 9 — what changed (Task 4 review, I1-1…I1-6):** D2 evaluates string's `Contains` and a core type's
> `ToString()` (the owner's decision of 2026-10-02), with AC1, rows and mutations (I1-1). D3 treats a clause nested too
> deeply to parse as one that doesn't parse (I1-4). Two D3 rows and mutations (I1-5). §6 is corrected and extended
> (I1-1, I1-4, I1-6). §5.4 and §9.7 record the round.

> **Revision 8 — what changed (Task 3):** §5.3 records Task 3.

> **Revision 7 — what changed (Tasks 1 and 2):** §5.1 and §5.2 record Tasks 1 and 2; the seam commit holds the stub
> alone, and the tests land with Task 2 (§5.1). D3 treats a doubled closing quote as an escape in each quoting form.
> §4.1 adds rows for a cast through `object`, arithmetic on a captured value, six clauses that don't parse, an escaped
> `]`, and the shapes of §6's new bullet; §4.3 adds two mutations and a killer for the NoColumn one. §6 records the
> operators and method calls D2 doesn't evaluate. §9.6 records Task 0's verdict on rev 6.

> **Revision 6 — what changed (Task 0 re-review, T4-1…T4-4):** rows pin D5's remaining order edges and the message of
> `!emptyIds.Contains(x.Id)` alone (T4-1); D3's rule 1 doesn't mask after an unclosed quote, with a row (T4-2); §6's
> three shapes corrected (T4-3); the CASE mutation names both killers (T4-4).

> **Revision 5 — what changed (Task 0 re-review, T3-1…T3-4):** §4.3's killers corrected and new rows added for the
> mutations that survived; D3 checks rule 1 before parsing; D1's zero-parameter case has a row (T3-1). D7's Changelog
> entries name D3's data-loss fix, the third idiom, a message change and the new API's namespace (T3-2). AC6 excludes
> `DeleteGuardCaseTests` (T3-3). §6 widens two bullets (T3-4).

> **Revision 4 — what changed (Task 0 re-review, T2-1…T2-6):** D3's grammar parses the SQL the visitors emit (runs
> absorb parentheses and `CASE … END`; a `NOT` inside a run belongs to it; an unparseable clause isn't a tautology),
> with rows for each shape (T2-1). Rows and mutations for D3's operators and keywords, and a standalone
> `[SqlExpression]` row (T2-2). §6 records four always-true shapes the guard doesn't detect (T2-3). D1 states the
> public members' argument contract, with a row (T2-4). AC8 drops its coverage waiver (T2-5). D7 leaves the
> historical provider plans unedited (T2-6). The owner's decision to reject the two idioms is recorded in D7.

> **Revision 3 — what changed (Task 0 re-review, T1-1…T1-9):**
> - D3 is a three-valued fold over the WHERE clause's tokens, so a true literal term rejects only when it makes the
>   whole clause true: `c.ParentId == p && !keepIds.Contains(c.Id)` with `keepIds` empty is accepted again, and
>   `NOT (1=0 AND …)` is rejected. Any `1=1` outside quotes stays rejected, as today (T1-1, T1-4).
> - D1: `DeletePredicateGuard` is public, like `ScalarProjectionGuard`; no `InternalsVisibleTo` (T1-2).
> - D2 evaluates property reads from any assembly, as the visitor does, and never calls a method, a user-defined
>   operator or conversion, or a constructor; rows for an instance-property flag (T1-3).
> - Mutation rows and their killers corrected (T1-5); a row for the `Convert` rule (T1-6); a documents row (T1-7);
>   citations and the missing-table row's assertions corrected (T1-8, T1-9).
> - From rev 3 the review records (§9) and revision notes are history and aren't edited in place; corrections go in
>   the latest record (§9.2 corrects the rev 2 note).

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
- **The docs over-claim:** `Usage.md:1346-1348` and `docs/architecture/AI_ARCHITECTURE_AND_DESIGN.md:33` say
  `x.Id == x.Id` is blocked, and the architecture doc says the guard "analyzes the expression tree". Neither holds
  today on PostgreSQL, MySQL and SQLite. `README.md:41` says the guard blocks `1=1`.

**Assembly names (T0-5).** The SQL Server provider's assembly is `Funcular.Data.Orm`
(`Funcular.Data.Orm.SqlServer.csproj:7`); PostgreSQL, MySQL and SQLite use their project names. Core's
`InternalsVisibleTo("Funcular.Data.Orm.SqlServer")` (`Core/AssemblyInfo.cs:3`) names no assembly. Granting
`InternalsVisibleTo` to the real names breaks the build: the providers' `protected override`s of Core's
`protected internal virtual` members fail with CS0507 (seven sites, and one in `ProviderScopedCacheTests`; §9.2 T1-2).

**Existing tests that pin the guard** (AC6):
- `SqlDataProviderIntegrationTests.Delete_TrivialWhereClause_ThrowsException` (`x => true` and `x => 1 < 2`: "must
  reference at least one column"; `x.FirstName == x.FirstName`: "self-referencing column expression"),
  `Delete_WhereClauseWithoutTableColumn_ThrowsException` (`"abc" == "abc"`),
  `Delete_WithEmptyWhereClause_ThrowsException` (sync, and async in `SqlDataProviderIntegrationAsyncTests:366`);
- SQLite's two (`SqliteDataProviderIntegrationTests:1281, 1310`);
- net48's `SqlDataProviderNetFrameworkIntegrationTests:1025`, `SqlDataProviderNetFrameworkIntegrationAsyncTests:342`
  (`DeleteAsync(x => true)`) and `DeleteGuardCaseTests` (which D8 inverts, so it isn't part of AC6);
- the four suites' `ColdCacheDeleteTests`, which keep the cold-cache plan's D1 order for accepted predicates.

The C# compiler folds `1 < 2` and `1 == 1` to `true`, so those predicates reach the provider as a constant.
`EfSqlDataProviderIntegrationTests:1008` exercises a test-local EF Core wrapper, not FunkyORM's guard.

## 2. Decisions

- **D1 — A public expression-tree guard in Core** (rev 3, T1-2). A new public static class `DeletePredicateGuard`
  (`Funcular.Data.Orm.Core/DeletePredicateGuard.cs`, namespace `Funcular.Data.Orm`), like the existing public
  `ScalarProjectionGuard` and `QueryOperatorPolicy`, classifies the predicate before translation:
  `public static DeletePredicateVerdict Classify(LambdaExpression predicate)`, with the public enum
  `DeletePredicateVerdict { Acceptable, NoColumn, SelfReference, AlwaysTrue }`;
  `public static void Validate(LambdaExpression predicate)`, which throws `InvalidOperationException` with the
  existing message for each rejection; and `public static bool HasLiteralTautology(string whereClause)` (D3). A
  custom provider can call them. No `InternalsVisibleTo` is added. The Changelog lists them under "New public API".
  **Contract (rev 4, T2-4):** a `null` argument throws `ArgumentNullException`, as `ScalarProjectionGuard` does; a
  lambda without exactly one parameter, or whose body isn't `bool`, throws `ArgumentException`. The entity type is
  that parameter's type.
- **D2 — Classification rules.** The **root** is any `ParameterExpression` whose type is the entity type, as the
  visitors accept (T0-7). A **member chain** is a run of member reads ending at a root, through `Convert`.
  1. **NoColumn** when the body has no member chain. Covers `x => true`, `x => 1 < 2`, `x => "abc" == "abc"`, a
     captured bool, and `x => false`: the same message as today.
  2. Otherwise fold the body to **True / False / Unknown**:
     - a `bool` constant is its value;
     - a parameter-free `bool` subtree built only from constants, field and property reads (rooted at a constant or
       static; a property of any assembly), `Convert`/`ConvertChecked`, `Not`, `AndAlso`/`OrElse`/`And`/`Or`, the
       six comparisons and `Conditional` is compiled and evaluated, provided that **no operator or conversion node in
       it carries a method** other than one declared in the core library's assembly (`typeof(object).Assembly`:
       `string`, `decimal`, `DateTime`, …). A subtree with a method call or a constructor is Unknown. So the guard
       reads properties, as the visitor does, and never calls a method, a user-defined operator or conversion, or a
       constructor (T0-6; reworded in rev 3, T1-3). An evaluation that throws is Unknown.
     - **Rev 9 (I1-1; the owner's decision of 2026-10-02):** two kinds of call are evaluable too, because every
       provider translates them into SQL with only parameters when their operands are parameter-free: `string`'s
       `Contains` (any overload), and a parameterless `ToString()` whose receiver is an enum or a non-generic, sealed
       or value type of the core library, or a nullable one of those, so no override outside the core library runs.
       Any other method call stays
       Unknown, and no code outside the core library runs except property getters.
     - **Rev 10 (FV1-1, FV1-2):** `ToString` with a format string (every parameter a `string`) is evaluable too, on the
       same receivers; an `IFormatProvider` argument isn't, as it could be the caller's own code. A parameter-free
       subtree whose evaluation throws, or reads null, is folded by its parts, and a `string.Contains` whose search
       value is null, on a receiver that isn't, is True: C# throws, but every provider sends `LIKE '%%'`.
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
- **D3 — A token-aware SQL check replaces the substring list** (rewritten in rev 3, T1-1, T1-4).
  `HasLiteralTautology(string whereClause)` returns true when either holds:
  1. **A `1=1` outside quotes:** the tokens `1`, `=`, `1` (whitespace allowed between them), not glued to a word
     character, `@`, `$`, `.` or `:`. This is the visitor's emission for a parameter-free `new` (`VisitNew`), and is
     rejected wherever it sits, as today, including `1=1 = @p` and `CASE WHEN 1=1 THEN`. Rule 1 is checked first, on
     the text with closed quoted segments masked, so it holds in a clause that doesn't parse: `1=1 AND (` is rejected
     (rev 5, T3-1). An unclosed quote masks nothing, so rule 1 still sees a `1=1` after it (rev 6, T4-2).
  2. **The clause folds to True.** Quoted segments (`'…'`, `"…"`, `[…]`, `` `…` ``, where a doubled closing quote
     is an escape, as in each dialect; rev 7) become opaque tokens. The text is parsed as `or := and (OR and)*`,
     `and := not (AND not)*`, `not := NOT not | primary`, `primary := group | run`, with keywords case-insensitive and
     whole words (rev 4, T2-1):
     - a **group** is `(` `or` `)` that stands as a whole term: the token after its closing `)` is `AND`, `OR`, a
       `)` or the end;
     - a **run** is a maximal sequence of other tokens up to an `AND` or `OR` at its own nesting level, a `)` that
       closes an enclosing group, or the end. A run absorbs balanced parentheses (`IN (…)`, function calls,
       `(…)::int`, subqueries) and `CASE … END` whole, nesting included; a `NOT` that isn't a run's first token (as in
       `IS NOT NULL`, `NOT IN`, `NOT LIKE`) belongs to the run;
     - a run that is exactly `<number> <op> <number>` (`=`, `<>`, `!=`, `<`, `>`, `<=`, `>=`) is True or False; any
       other run is Unknown. `NOT`, `AND` and `OR` use three-valued logic;
     - a clause that doesn't parse (unbalanced parentheses or quotes) is not a tautology: the method returns false,
       and the other checks still apply. A clause nested too deeply for the calling thread's stack is parsed again on
       a thread with a 64 MB stack; one too deep even for that is a clause that doesn't parse (rev 9, I1-4; rev 10,
       FV1-3). Parentheses and `CASE … END` are matched in one pass over the tokens (rev 10).

  So `(t.id = @p OR NOT 1=0)`, `NOT (1=0 AND t.id = @p)`, `(t.first_name IS NOT NULL OR NOT 1=0)` and
  `(t.id IN (@p0, @p1) OR NOT 1=0)` are rejected, while `(t.parent_id = @p AND NOT 1=0)` (the empty `keepIds` idiom),
  `(t.id NOT IN (@p0) AND NOT 1=0)`, `(1=0 OR t.id = @p)` and `CASE WHEN {Id} * 100 > 50 THEN 1 ELSE 0 END = @p` are
  not.
- **D4 — Kept as they are** (owner's brief): check 1 (empty), check 3 (both self-reference regexes) and check 4
  (column reference). D2 is the real self-reference detection; the regexes stay as a second line.
- **D5 — Order in each Delete/DeleteAsync by predicate:** transaction guard → null guard →
  `DeletePredicateGuard.Validate(predicate)` → `GenerateWhereClause` → empty check → `HasLiteralTautology` →
  self-reference regex → column check → DELETE. A predicate rejected by D2 never discovers `T` or reaches the
  database, so on a missing table it reports the guard's message, not discovery's error. Accepted predicates keep
  the cold-cache plan's D1 order. The transaction and null guards keep their messages ("Delete operations must be
  performed within an active transaction."; "A WHERE clause (predicate) is required for deletes.") and run before
  `Validate`, and `HasLiteralTautology` runs before the column check; rows pin each edge (rev 6, T4-1).
- **D6 — SQL Server's two inline copies become one private `ValidateDeleteWhereClause<T>(string)`**, called from
  Delete and DeleteAsync, with SQL Server's own regex. The uncalled `ValidateWhereClause<T>` (`:1524`, with an
  `@p__linq__0` pattern) is deleted. This is a refactor checked in the diff, not an acceptance criterion (T0-10).
- **D7 — Documents.**
  - Changelog `[3.10.0-beta1]` Fixed: self-comparisons, their negations, and disjunctions with an always-true
    operand (a `true` literal or captured value, a parameter-free comparison that holds) deleted rows; they're now
    rejected before any SQL runs. A disjunction with a negated `Contains` over an empty collection
    (`x.Id == 2 || !emptyIds.Contains(x.Id)`, its De Morgan form, and the same after `IS NOT NULL` or `IN (…)`)
    deleted every row on all four providers; it's now rejected once the WHERE clause is built, before the DELETE
    runs (rev 5, T3-2). Which provider deleted which shape is §1's table and the §4.1 seam column; the entry names the
    shapes, not a provider list. Predicates on tables or columns whose names contain `true` were rejected as trivial
    and are now accepted.
  - Changelog Changed: the idioms that now throw (`filter == null || x.Col == filter` with `filter` null;
    `isAdmin || x.OwnerId == me` with `isAdmin` true; `x.Archived || !keepIds.Contains(x.Id)` with `keepIds` empty),
    each with the alternative (branch in C# and don't call Delete). Rejecting them is the owner's decision of
    2026-10-02. `x => !emptyIds.Contains(x.Id)` is still rejected, now with "requires a non-trivial WHERE clause"
    instead of "must reference at least one column" (rev 5, T3-2).
  - Changelog New public API: `DeletePredicateGuard` and `DeletePredicateVerdict` in `Funcular.Data.Orm`, as their
    own bullet, not under the existing `Funcular.Data.Orm.Linq` one (rev 5, T3-2).
  - The 3.10 Changed bullet about the netstandard2.0/net48 guard becoming case-insensitive is deleted, with the
    Fixed line's pointer to it: the guard no longer calls `Contains`.
  - Scoped to "a predicate the guard accepts" (T0-3): the Changelog Changed bullet on the missing-table error
    (`Changelog.md:178-181`), SQL Server's XML doc on DeleteAsync (`SqlServerOrmDataProvider.cs:270-272`; the sync
    Delete's doc has no such sentence, T1-8), and the cold-cache plan's D1 error-shape sentence.
  - SQL Server's XML doc comments on Delete/DeleteAsync, `Usage.md:314, 1346-1348`, `README.md:41` and
    `docs/architecture/AI_ARCHITECTURE_AND_DESIGN.md:33` describe what the guard rejects, and don't claim that every
    always-true predicate is (T0-9). `docs/ai-instructions/FUNKYORM_AI_INSTRUCTIONS.md:149` stays true. The
    historical provider plans (`docs/plans/{SQLITE,POSTGRESQL,MYSQL}_PROVIDER_IMPLEMENTATION_PLAN.md` and their copies
    under `Funcular.Data.Orm.SqlServer/_agent/plans/`) describe the guard as it was then and stay unedited (T2-6).
  - The provider-scoped caches plan's §4.2 note on the guard's reach and its §4.4 net48 line get a pointer here.
- **D8 — `DeleteGuardCaseTests` (net48) is inverted:** its three rows delete exactly the matching row, and its class
  summary, which describes the old rejection, is rewritten (T1-8). They become net48's instance of AC3.

## 3. Acceptance criteria

- **AC1 — No trivial delete runs.** On all four providers, Delete and DeleteAsync by predicate reject, before any SQL
  is sent and before `T` is discovered, a predicate that D2 folds to True, with no row deleted:
  - a self-comparison of a member chain by `==`, `>=` or `<=`, also through `Convert`, and the negation of one by
    `!=`, `>` or `<` (SelfReference message);
  - any other body that folds to True: a disjunction with a `true` literal, a captured or static `true`, a `true`
    property of a captured object (`request.IncludeAll`), a parameter-free comparison that holds, or a `Contains` or
    `ToString()` that D2 evaluates and that holds (`roles.Contains("admin") || …`; rev 9, I1-1), including a
    `ToString` with a format string and a `Contains` with a null search value (rev 10, FV1-1, FV1-2); the De Morgan
    forms (`!(x.Id != x.Id && …)`, `!(x.Id != x.Id || x.Id > x.Id)`); a Conditional whose chosen or shared branch is
    True (AlwaysTrue message).
- **AC2 — Parameter-free predicates keep their message.** `x => true`, `x => 1 < 2`, `x => "abc" == "abc"`, a captured
  `true`, and `x => false` are rejected with "must reference at least one column…", on all four providers, sync and
  async.
- **AC3 — Identifiers containing `true` are accepted.** A predicate on a convention column `true_up`, or on a table
  named `zz_dg_TrueUp`, deletes exactly the matching rows on all four providers on net8.0, sync and async. On net48,
  SQL Server, a PascalCase column `TrueUpAmount` and a table named `zz_guard_TrueUp` do too (D8).
- **AC4 — Literal tautologies in the SQL are rejected.** A translated WHERE clause that contains a `1=1` outside
  quotes, or that folds to True (D3), is rejected with the AlwaysTrue message; one that folds to False or Unknown
  isn't. Integration, on all four providers, sync and async:
  - rejected: `x.Id == 2 || new Holder().Flag` and `x.Id == 2 || new Holder().Flag == true` (the visitor's `1=1`);
    `x.Id == 2 || !emptyIds.Contains(x.Id)` (`OR NOT 1=0`); `!(emptyIds.Contains(x.Id) && x.Id == 2)`
    (`NOT (1=0 AND …)`); `x.FirstName != null || !emptyIds.Contains(x.Id)` (`IS NOT NULL OR NOT 1=0`);
    `ids.Contains(x.Id) || !emptyIds.Contains(x.Id)` (`IN (…) OR NOT 1=0`) (rev 4, T2-1);
    `x.Archived || !emptyIds.Contains(x.Id)` on a `bool` column (rev 7);
  - accepted, deleting exactly row 2: `emptyIds.Contains(x.Id) || x.Id == 2` (`1=0 OR …`).
- **AC5 — Legitimate predicates are unaffected.** Equality, `&&`/`||` of column comparisons, a list `Contains`,
  `StartsWith`, `x.Id == x.Id && x.Id == 2`, a `[SqlExpression]` member with arithmetic next to a literal
  comparison (alone, and in a conjunction), the empty-exclusion idiom `!emptyIds.Contains(x.Id) && x.Id == 2` (either
  order; `AND NOT 1=0`), `x.Id == 2 && !otherIds.Contains(x.Id)` and `x.FirstName != null && !emptyIds.Contains(x.Id)`
  each delete exactly the matching rows, on all four providers, sync and async.
- **AC6 — The existing guard tests pass unchanged** (§1), except `DeleteGuardCaseTests`, which D8 inverts (rev 5,
  T3-3).
- **AC7 — The documents of D7 are true.**
- **AC8 — No regression:** SqlServer.Tests, PostgreSql.Tests, MySql.Tests, Sqlite.Tests, DotNet9 and net48 pass;
  `DeletePredicateGuard.cs` and every touched provider file reach 85 % line coverage, with the baseline at `be8de82`
  recorded (no pre-arranged waiver; rev 4, T2-5).

## 4. Test plan

### 4.1 AC → test matrix

Each row is **Red** (fails at the seam, Task 1, for the stated reason) or a **Guard** (passes at the seam). The
mutations of §4.3 name the rows that kill them; a Guard row that no mutation names is a coverage row (rev 5, T3-1).
Every row is run alone at the seam and its outcome recorded.

**DB-free, `Funcular.Data.Orm.SqlServer.Tests/DeleteGuard/DeletePredicateGuardTests.cs`.** Expected verdicts:
SR = SelfReference, AT = AlwaysTrue, NC = NoColumn, OK = Acceptable.

| AC | Test | Class at the seam |
|---|---|---|
| AC1, AC2, AC5 | `Classify_ReturnsTheVerdict`, one DataRow per shape. Self-comparisons: `x.Id == x.Id` SR, `x.Id >= x.Id` SR, `x.Id <= x.Id` SR, `x.Id != x.Id` OK, `x.Id > x.Id` OK, `x.Id < x.Id` OK, `!(x.Id != x.Id)` SR, `!(x.Id > x.Id)` SR, `(long)x.Id == (long)x.Id` SR, `(int?)x.Id == (int?)x.Id` SR, `x.Name == x.Name` SR, `x.Id == x.Other` OK. Or: `x.Id == 2 \|\| true` AT, `\|\| capturedTrue` AT, `\|\| StaticFlags.On` AT, `\|\| DateTime.Now > DateTime.MinValue` AT, `\|\| capturedA == capturedA` AT, `\|\| capturedNull == null` AT, `\|\| capturedS == "s"` AT, `\|\| capturedFalse` OK, `x.Id == 2 \| true` AT (non-short-circuit). And: `x.Id == 2 && true` OK, `x.Id == x.Id && x.Id == 2` OK, `x.Id == x.Id \|\| x.Id == 2` AT, `x.Id == 2 & false` OK. De Morgan: `!(x.Id != x.Id && x.Id == 2)` AT, `!(x.Id != x.Id \|\| x.Id > x.Id)` AT, `!(x.Id == 2 && capturedFalse)` AT. Property reads: `x.Id == 2 \|\| request.IncludeAll` AT (an instance property of a captured object). Convert: a hand-built `x.Id == 2 \|\| Convert(Convert(x.Id == x.Id, bool?), bool)` AT. Conditional: `capturedTrue ? x.Id == x.Id : x.Id == 2` AT, `capturedFalse ? x.Id == 2 : x.Id == x.Id` AT, `x.Flag ? x.Id == x.Id : x.Id >= x.Id` AT, `x.Flag ? x.Id == x.Id : x.Id == 2` OK. Not evaluated: `x.Id == 2 \|\| new Holder().Flag` OK, `\|\| Method()` OK, `\|\| (bool)capturedWrapper` OK (user `explicit operator bool`), `\|\| capturedW == capturedW2` OK (user `operator ==`), `\|\| capturedObj.Throws` OK (the read throws). Roots: a hand-built lambda whose body uses another parameter of the entity type, `x'.Id == 2` OK and `x'.Id == x'.Id` SR. Parameter-free: `true`, `1 < 2`, `"abc" == "abc"`, `capturedTrue`, `false` NC. Not folded (rev 7): `x.Id == 2 \|\| (bool)(object)x.Flag` OK (a cast through `object`), `x.Id == 2 \|\| capturedA + 1 == 8` OK (§6). Calls (rev 9, I1-1): `x.Id == 2 \|\| capturedS.Contains("s")`, `\|\| capturedS.Contains('s')`, `\|\| capturedS.Contains("s", StringComparison.Ordinal)`, `\|\| "abc".Contains("b")`, `roles.Contains("admin") \|\| x.Id == 2`, `\|\| capturedS.ToString() == "s"`, `\|\| capturedA.ToString() == "7"`, `\|\| capturedDay.ToString() == "Monday"`, `\|\| capturedNullableA.ToString() == "7"` (an `int?`) AT; `\|\| capturedS.Contains("z")`, `\|\| capturedS.StartsWith("s")`, `\|\| capturedNamed.ToString() == "x"` (an `object` holding a class with an override), `\|\| capturedNamedValue.ToString() == "x"` (a struct with an override), `\|\| capturedNullableNamedValue.ToString() == "x"` (a nullable one), `x.Name.Contains("a")` and `x.Id == 2 \|\| capturedS.Contains(x.Name)` (a column as the receiver or the argument) OK. Rev 10 (FV1-1, FV1-2, FV1-4): `x.Id == 2 \|\| capturedA.ToString("D2") == "07"`, `\|\| capturedDate.ToString("yyyy-MM-dd") == "2020-01-05"`, `\|\| capturedS.Contains(capturedNull)`, `\|\| capturedS.Contains(capturedNull, StringComparison.Ordinal)` AT; `\|\| capturedNull.Contains(capturedNull)`, `\|\| capturedS.Contains("_")` and `\|\| capturedA.ToString() == "07"` (both §6), `\|\| capturedDate.ToString(capturedProvider) == "x"` (a user `IFormatProvider`, which `DateTime` always consults), `\|\| capturedBag.Contains("admin")` (a user class's `Contains`), `\|\| capturedPair.ToString() == "x"` (a `KeyValuePair<string, Named>`) OK. | Red for the SR, AT and NC rows (the seam returns OK); Guard for the OK rows |
| AC1 | `Classify_NeverCallsMethodsOperatorsOrConstructors` (counters on `Method()`, the `Holder` constructor, the user `explicit operator bool` and the user `operator ==`, each in a disjunction; and (rev 9) the `ToString()` overrides of a class held in an `object`, of a struct and of a nullable struct; and (rev 10, FV1-4) a user class's `Contains`, a user `IFormatProvider` and the `Named` inside a `KeyValuePair`; every count stays 0) | Guard |
| AC1, AC2 | `Validate_ThrowsTheMessageOfEachVerdict` [NoColumn, SelfReference, AlwaysTrue: exact message; Acceptable: no throw] | Red for the three rejections; the Acceptable row is a Guard |
| D1 contract | `PublicMembers_RejectBadArguments` [`Classify(null)`, `Validate(null)`, `HasLiteralTautology(null)`: `ArgumentNullException`; a zero-parameter lambda `() => true` (rev 5, T3-1), a two-parameter lambda and a lambda with an `int` body: `ArgumentException`] (rev 4, T2-4) | Red (the seam stub doesn't check) |
| AC4 | `HasLiteralTautology_ReturnsTheRuleOrTheFold`. True by the `1=1` rule: `1=1`, `1 = 1`, `(t.id = @p__linq__0 AND 1=1)`, `1=1 = @p`, `CASE WHEN 1 = 1 THEN 1 END = @p`. True by the fold: `1 < 2`, `2 >= 1`, `1.5 > 1`, `1 != 2`, `1 <> 2`, `1 <= 1`, `(2 > 1)`, `NOT NOT 2 > 1`, `t.id = @p OR NOT 1=0`, `t.a = @p or not 1=0` (lower case), `NOT (1=0 AND t.id = @p)`, and in an `OR` with `NOT 1=0` (rev 4, T2-1): `t.first_name IS NOT NULL`, `t.id IN (@p0, @p1)`, `t.id NOT IN (@p0)`, `COALESCE(t.a, 0) = @p`, `CAST(strftime('%Y', t.d) AS INTEGER) = @p`, `t.name LIKE CONCAT(@p, '%')`, `(t.a)::int = @p`, `(SELECT COUNT(*) FROM c WHERE c.pid = t.id) > @p`, `CASE WHEN t.a > 0 THEN 1 ELSE 0 END = @p`, `t.notes = @p`. Also true (rev 5, T3-1): `1=1 AND (` (rule 1 in a clause that doesn't parse), `ordinal = @p OR NOT 1=0`; and (rev 6, T4-2) `t.c = 'abc 1=1` (an unclosed quote masks nothing). False: `1=0`, `1 <> 1`, `2 <= 1`, `NOT 2 > 1`, each of the ten shapes just listed in an `AND` with `NOT 1=0`, and (rev 5, T3-1) `(t.id = @p OR NOT 1=0` (unbalanced), `t.c = 'abc OR NOT 1=0` (an unclosed quote), `CASE WHEN t.a = 0 OR NOT 1 = 0 OR t.b = 0 THEN 0 ELSE 1 END = @p`, `t.a = @p XOR NOT 1=0`, `(1=0 OR t.id = @p)`, `(t.parent_id = @p AND NOT 1=0)`, `(NOT 1=0 AND t.x = @p)`, `2 > 1 AND t.id = @p`, `t.col1=1`, `t.col1 = 1`, `@p__linq__1=1`, `$1=1`, `'1=1'`, `t.c = 'x OR 1=1 OR y'`, `[a OR 1=1 OR b] = @p`, `"1"=1`, `` `1`=1 ``, `t.c = 'a''1=1'`, `t.a * 100 > 50`, `t.a - 1 >= 0`, `5 < 10 * t.a`, `CASE WHEN t.a - 1 >= 0 THEN 1 ELSE 0 END = @p`; and (rev 7) the clauses that don't parse `NOT 1=0 OR`, `NOT 1=0 AND`, `t.id IN (@p OR NOT 1=0`, `CASE WHEN t.a = 1 THEN 1 OR NOT 1=0`, `() OR NOT 1=0` and `t.a = @p) OR NOT 1=0`, and `[a]]1=1] = @p` (an escaped `]`); and (rev 9, I1-5) `NOT 1=0) AND t.x = @p` and `CASE WHEN CASE WHEN t.a = 1 THEN 1 END = 1 OR NOT 1=0 OR t.b = 0 THEN 0 END = @p` | Red for the true rows (the seam returns false); Guard for the false rows |
| D3 | `HasLiteralTautology_DeeplyNested_StillFolds`: 20,000 nested groups around `t.id = @p OR NOT 1=0` (true) and `… AND NOT 1=0` (false), on a 1 MB thread; `HasLiteralTautology_NestedBeyondTheLargeStack_ReturnsFalse`: 300,000 groups give false without a stack overflow (rev 9, I1-4; rev 10, FV1-3) | Red at the seam for the `OR` row, Guard for the others; at `3ec5ba9` the test host dies |

**Integration, a shared harness `Funcular.Data.Orm.SqlServer.Tests/DeleteGuard/DeleteGuardHarness.cs`, compiled into
SqlServer.Tests (SQL Server and SQLite classes), PostgreSql.Tests and MySql.Tests, as `LinqScopeHarness` is.**
- Each row creates `zz_dg_row (id, first_name, true_up, big, archived)` with rows `(1,'a',1,…)`, `(2,'b',0,…)`,
  `(3,NULL,0,…)`, and `zz_dg_TrueUp (id, amount)` with `(1,5)`, `(2,7)`. The DDL, the `[Table]` names and the
  cleanup use identical case (MySQL on Linux CI has `lower_case_table_names=0`). The tables are dropped in `finally`.
  Each call runs in its own transaction.
- A rejected call asserts the exact message, that no `DELETE` was logged, and that all rows remain, counted through
  the provider inside the transaction before the rollback. The missing-table row can't count rows: it drops its table
  if it exists first, and asserts the message, that no `DELETE` was logged, and that the type is still undiscovered
  (T1-9).
- An accepted call asserts the deleted count and the surviving ids.
- Every row has a `sync`/`async` DataRow.

| AC | Test | SQL Server | PostgreSQL | MySQL | SQLite | Class at the seam |
|---|---|---|---|---|---|---|
| AC1 | `SelfComparison_IsRejected` [`x.FirstName == x.FirstName`, `x.Id >= x.Id`, `x.Id <= x.Id`, `!(x.Id != x.Id)`, `(long)x.Id == (long)x.Id`] | ✓ | ✓ | ✓ | ✓ | Red on PostgreSQL, MySQL and SQLite (rows deleted); on SQL Server Red for `!(x.Id != x.Id)` only (its regex catches the others, `(long)` included, since the visitor drops `Convert`) |
| AC1 | `AlwaysTrue_IsRejected` [`x.Id == 2 \|\| true`, `\|\| capturedTrue`, `\|\| StaticFlags.On`, `\|\| request.IncludeAll`, `\|\| capturedA == capturedA`, `\|\| capturedNull == null`, `!(x.Id != x.Id && x.Id == 2)`, `!(x.Id != x.Id \|\| x.Id > x.Id)`, and (rev 9, I1-1) `x.Id == 2 \|\| capturedS.Contains("s")`, `roles.Contains("admin") \|\| x.Id == 2`, `x.Id == 2 \|\| capturedS.ToString() == "s"`, and (rev 10) `x.Id == 2 \|\| capturedA.ToString("D2") == "07"` and `x.Id == 2 \|\| capturedS.Contains(capturedNull)`] | ✓ | ✓ | ✓ | ✓ | Red: rows deleted, or on SQL Server for the bare-parameter forms a `SqlException`, not the message; the rev 9 rows delete every row at `3ec5ba9`; at `09033fa` the rev 10 rows delete every row, except `ToString("D2")` on PostgreSQL (error 42883) and SQLite (row 2) |
| AC1 | `RejectedOnAMissingTable_ReportsTheGuardNotDiscovery` [`x.Id == x.Id`, `x.Id == 2 \|\| true`] on a cold type whose table doesn't exist (T0-3) | ✓ | ✓ | ✓ | ✓ | Red (discovery's error, or `SqliteException`, is thrown first) |
| AC2 | `ParameterFree_IsRejectedAsNoColumn` [`true`, `1 < 2`, `"abc" == "abc"`, `capturedTrue`, `false`] | ✓ | ✓ | ✓ | ✓ | Guard |
| AC3 | `IdentifierContainingTrue_IsAccepted` [`x.TrueUp == 1` (column `true_up`), the `zz_dg_TrueUp` table's `x.Amount == 5`] | ✓ | ✓ | ✓ | ✓ | Red (rejected as trivial) |
| AC4 | `LiteralTautologyInSql_IsRejected` [`x.Id == 2 \|\| new Holder().Flag`, `x.Id == 2 \|\| new Holder().Flag == true`, `x.Id == 2 \|\| !emptyIds.Contains(x.Id)`, `!(emptyIds.Contains(x.Id) && x.Id == 2)`, `x.FirstName != null \|\| !emptyIds.Contains(x.Id)`, `ids.Contains(x.Id) \|\| !emptyIds.Contains(x.Id)`, `!emptyIds.Contains(x.Id)` alone (rev 6, T4-1), `x.Archived \|\| !emptyIds.Contains(x.Id)` (a `bool` column `archived`, rev 7)] | ✓ | ✓ | ✓ | ✓ | Guard for the two `1=1` rows (the substring list catches them); Red for the four `NOT` rows (rows deleted), for `!emptyIds.Contains(x.Id)` alone (the "must reference" message, not AlwaysTrue) and for the `x.Archived` row (rows deleted; on SQL Server a `SqlException`) |
| D5 | `NullPredicate_KeepsItsMessage` (`Delete<T>(null)`) and `RejectedPredicateWithoutTransaction_KeepsTheTransactionMessage` (`x.Id == x.Id` with no transaction open) (rev 6, T4-1) | ✓ | ✓ | ✓ | ✓ | Guard |
| AC4, AC5 | `NonTrivialPredicates_DeleteTheMatchingRows` [`x.Id == 2`; `x.Id == 2 && x.FirstName == "b"`; `x.Id == 1 \|\| x.Id == 2`; `ids.Contains(x.Id)`; `x.FirstName.StartsWith("b")`; `x.Id == x.Id && x.Id == 2`; `emptyIds.Contains(x.Id) \|\| x.Id == 2`; `!emptyIds.Contains(x.Id) && x.Id == 2`; `x.Id == 2 && !emptyIds.Contains(x.Id)`; `x.Id == 2 && !otherIds.Contains(x.Id)` (row 2); `x.FirstName != null && !emptyIds.Contains(x.Id)` (rows 1 and 2); `x.Big == 1 && x.Id == 2` with `[SqlExpression("CASE WHEN {Id} * 100 > 50 THEN 1 ELSE 0 END")] Big`; `x.Big2 == 1` alone with `[SqlExpression("CASE WHEN {Id} - 1 >= 1 THEN 1 ELSE 0 END")] Big2` (rows 2 and 3; rev 4, T2-2); `x.Id == 2 \|\| capturedS.Contains("z")` and `x.FirstName.Contains("b")` (rows 1 and 3; rev 9)] | ✓ | ✓ | ✓ | ✓ | Guard |
| §6 | `UnevaluatedParameterFreeParts_AreNotTranslated` [`x.Id == 2 \|\| capturedA + 1 == 8`, `\|\| -capturedA == -7`, `\|\| emptyArray.Length == 0`, `\|\| capturedArray[0] == 5`, `\|\| (capturedNullBool ?? true)`, `string.IsNullOrEmpty(capturedNull) \|\| x.FirstName == capturedNull`, and (rev 9) `x.Id == 2 \|\| capturedS.StartsWith("s")` and `\|\| capturedS.EndsWith("s")`]: the verdict is Acceptable, the provider throws (`NotSupportedException`; for `StartsWith`/`EndsWith` a `NullReferenceException`, rev 9), no `DELETE` is logged and every row remains (rev 7) | ✓ | ✓ | ✓ | ✓ | Guard |
| AC3 | `DeleteGuardCaseTests` (net48, D8): `TrueUpAmount == 5` sync and async, and the `zz_guard_TrueUp` table, each delete exactly row 1 | ✓ (net48) | — | — | — | Red (rejected as trivial) |
| AC6 | the existing tests of §1, except `DeleteGuardCaseTests` (D8) | ✓ (and net48) | ✓ (cold-cache) | ✓ (cold-cache) | ✓ | Guard |
| AC7 | Task 4's review checks Task 3's diff against D7's list and a grep of every document for descriptions of the guard (T1-7) | — | — | — | — | — |
| AC8 | the suites, DotNet9, net48 | ✓ | ✓ | ✓ | ✓ | — |

`Holder`, `Method()`, `StaticFlags`, `request`, the wrappers and the captured values (`ids` = {1, 2},
`otherIds` = {1, 3}, `emptyIds` empty) are members of the test classes.
`Holder.Flag`, the static property `StaticFlags.On` and the instance property `request.IncludeAll` return true;
`capturedObj.Throws` throws; the user operators count their calls.

### 4.2 Interface coverage

| Member | Tests |
|---|---|
| `DeletePredicateGuard.Classify` | `Classify_ReturnsTheVerdict`, `Classify_NeverCallsMethodsOperatorsOrConstructors` |
| `DeletePredicateGuard.Validate` | `Validate_ThrowsTheMessageOfEachVerdict`; every harness row |
| `DeletePredicateGuard.HasLiteralTautology` | `HasLiteralTautology_ReturnsTheRuleOrTheFold`; `LiteralTautologyInSql_IsRejected`; the `emptyIds` and `[SqlExpression]` rows |
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
| Property getters treated as methods (not evaluated) | `\|\| request.IncludeAll` rows (Classify and harness); `\|\| StaticFlags.On` rows |
| `Convert` on `bool`/`bool?` not folded | Classify's hand-built `Convert(Convert(…, bool?), bool)` row |
| The method restriction removed, or limited to comparisons | `Classify_NeverCallsMethodsOperatorsOrConstructors` (the conversion and operator counters) |
| Parameter-free evaluation invokes methods or `new` | `Classify_NeverCallsMethodsOperatorsOrConstructors` |
| The NoColumn check after the fold, or removed | `ParameterFree_IsRejectedAsNoColumn` (message); AC6's SQL Server test; Classify's `x => false` row, which alone kills the check moved between the fold and the verdict, where the provider's column check still rejects `false` with the same message (rev 7) |
| SelfReference and AlwaysTrue messages swapped | the message assertions of both rejection rows |
| D3's `1=1` rule dropped | its `1=1 = @p`, `CASE WHEN 1 = 1 …` and `(… AND 1=1)` rows; the `new Holder().Flag == true` harness row |
| D3's `1=1` rule ignores the token boundary | its `t.col1=1`, `@p__linq__1=1` and `$1=1` rows |
| D3 keeps quoted segments | its `t.c = 'x OR 1=1 OR y'`, `[a OR 1=1 OR b] = @p` and `'1=1'` rows |
| D3 rejects any true term, not the fold (true inside `AND` rejects) | its `(t.parent_id = @p AND NOT 1=0)`, `(NOT 1=0 AND t.x = @p)` and `2 > 1 AND t.id = @p` rows; the `!emptyIds.Contains(x.Id) && x.Id == 2` harness rows |
| D3's fold ignores `NOT` | its `NOT 2 > 1` and `t.id = @p OR NOT 1=0` rows; the `\|\| !emptyIds.Contains` harness row |
| D3's `NOT` doesn't apply to a parenthesised group | its `NOT (1=0 AND t.id = @p)` row; the `!(emptyIds.Contains(x.Id) && x.Id == 2)` harness row |
| D3 counts a literal next to arithmetic as a term | its `t.a * 100 > 50` and `5 < 10 * t.a` rows; the standalone `x.Big2 == 1` harness row (rev 4, T2-2: the `x.Big == 1 && …` row can't kill it) |
| D3's run doesn't absorb parentheses (rev 4, T2-1; killers corrected in rev 5, T3-1) | its `IN`, `NOT IN`, `COALESCE`, `CAST`, `CONCAT` and subquery rows in the `OR` position; the `ids.Contains(x.Id) \|\| !emptyIds.Contains(x.Id)` harness row |
| D3 takes a group where it isn't a whole term | its `(t.a)::int = @p` and subquery rows in the `OR` position |
| D3's run doesn't absorb `CASE … END` (as an ordinary token, or ending the run) | its `CASE WHEN t.a = 0 OR NOT 1 = 0 OR t.b = 0 …` row (rev 5, T3-1) and its `CASE WHEN t.a > 0 …` row in the `OR` position (rev 6, T4-4) |
| D3's rule 1 masks an unclosed quote to the end | its `t.c = 'abc 1=1` row (rev 6, T4-2) |
| `HasLiteralTautology` after the column check | the `!emptyIds.Contains(x.Id)` alone harness row (rev 6, T4-1) |
| `Validate` before the null guard (per provider) | `NullPredicate_KeepsItsMessage`, sync and async |
| `Validate` before the transaction guard (per provider) | `RejectedPredicateWithoutTransaction_KeepsTheTransactionMessage`, sync and async |
| D3 treats a `NOT` inside a run as logical | its `IS NOT NULL` and `NOT IN` rows in the `OR` position; the `x.FirstName != null \|\| …` harness row |
| D3 treats a clause it can't parse as a tautology | its `(t.id = @p OR NOT 1=0` and `t.c = 'abc OR NOT 1=0` rows (rev 5, T3-1) |
| D3 checks rule 1 only after a successful parse | its `1=1 AND (` row (rev 5, T3-1) |
| D3 doesn't recognise `<>` or `<=`, or evaluates `<=` as `<` | its `1 <> 2` and `1 <= 1` rows |
| D3 matches keywords case-sensitively | its `t.a = @p or not 1=0` row |
| D3 matches keywords inside identifiers | its `t.a = @p XOR NOT 1=0` and `ordinal = @p OR NOT 1=0` rows (rev 5, T3-1) |
| A null argument accepted (each public member) | `PublicMembers_RejectBadArguments`' null rows (rev 5, T3-1) |
| The parameter-count check weakened to "more than one" | `PublicMembers_RejectBadArguments`' `() => true` row |
| The body-type check removed | `PublicMembers_RejectBadArguments`' `int`-body row |
| D3 rejects every literal comparison | its `1=0` and `(1=0 OR t.id = @p)` rows; the `emptyIds.Contains(x.Id) \|\| x.Id == 2` harness row |
| `HasLiteralTautology` not called (per provider) | `LiteralTautologyInSql_IsRejected` (on PostgreSQL, MySQL and SQLite the kept regex reports SelfReference for `1=1`, which fails the message assertion; on SQL Server the delete runs) |
| The old substring list kept | `IdentifierContainingTrue_IsAccepted`; `DeleteGuardCaseTests` |
| A doubled closing quote isn't an escape (rev 7) | its `[a]]1=1] = @p` row |
| An evaluation that throws isn't Unknown (rev 7) | Classify `\|\| capturedObj.Throws` |
| `Contains` not evaluated (rev 9) | Classify's `Contains` rows; the harness `capturedS.Contains("s")` and `roles.Contains("admin")` rows |
| `ToString()` not evaluated (rev 9) | Classify's `ToString()` rows; the harness `capturedS.ToString()` row |
| Every core-library method evaluated (rev 9) | Classify `\|\| capturedS.StartsWith("s")` (OK) |
| `ToString()` evaluated whatever its receiver (rev 9) | `Classify_NeverCallsMethodsOperatorsOrConstructors` (the `ToString()` counters) |
| A nullable receiver not unwrapped (rev 9) | Classify `\|\| capturedNullableA.ToString() == "7"` |
| Any nullable receiver accepted, whatever its underlying type (rev 9) | `Classify_NeverCallsMethodsOperatorsOrConstructors` (the nullable struct's counter) |
| D3's stack check removed (rev 9) | `HasLiteralTautology_DeeplyNested_StillFolds` (the test host dies) |
| D3's `InsufficientExecutionStackException` not caught (rev 9) | `HasLiteralTautology_DeeplyNested_StillFolds` |
| No second parse on a large stack (rev 10) | `HasLiteralTautology_DeeplyNested_StillFolds` (the `OR` row) |
| The large-stack thread's own stack check not caught (rev 10) | `HasLiteralTautology_NestedBeyondTheLargeStack_ReturnsFalse` (the test host dies) |
| `ToString` with a format string not evaluated (rev 10) | Classify `capturedA.ToString("D2")` and `capturedDate.ToString(…)`; the harness `ToString("D2")` row |
| `ToString` evaluated whatever its argument types (rev 10) | `Classify_NeverCallsMethodsOperatorsOrConstructors` (the `IFormatProvider` counter) |
| `Contains` evaluated whatever its receiver type (rev 10, FV1-4) | `Classify_NeverCallsMethodsOperatorsOrConstructors` (the user `Contains` counter) |
| A generic receiver's `ToString()` evaluated (rev 10, FV1-4) | `Classify_NeverCallsMethodsOperatorsOrConstructors` (the `KeyValuePair` row) |
| A `Contains` of null not True (rev 10) | Classify's two `Contains(capturedNull…)` rows; the harness `Contains(capturedNull)` row |
| A `Contains` of null True on a null receiver too (rev 10) | Classify `capturedNull.Contains(capturedNull)` (OK) |
| A failed evaluation not folded by its parts (rev 10) | Classify's two `Contains(capturedNull…)` rows |
| D3 ignores tokens after the top-level `OR` (rev 9, I1-5) | its `NOT 1=0) AND t.x = @p` row |
| D3's `CASE … END` matching ignores a nested `CASE` (rev 9, I1-5) | its nested `CASE` row |

### 4.4 Where each tier runs

- **CI:** `ci.yml` runs SqlServer.Tests, with the DB-free rows and the SQL Server and SQLite harness classes.
  `build-and-test-postgresql.yml` and `build-and-test-mysql.yml` run PostgreSql.Tests and MySql.Tests, with their
  harness classes, on Linux on every push to `development/**` (T0-8). Linux table-name case is verified there only.
- **Local, recorded with sha, before the merge:** all four suites, DotNet9, and net48 (MSBuild, then vstest; CI doesn't
  run it).

## 5. Tasks

1. **Task 0 — Test-plan review** (non-author) to CLEAN.
2. **Task 1 — The seam and the red tests.** Commit the public `DeletePredicateGuard` with `Classify` returning
   Acceptable, `Validate` doing nothing and `HasLiteralTautology` returning false, and the tests of §4.1. Run each row
   alone and record its class and message. Record the coverage baselines.
3. **Task 2 — D1–D6 and D8.** Green; the §4.3 mutations, each run; suites; coverage.
4. **Task 3 — Documents (D7).**
5. **Task 4 — Hostile review and fix-verification** to CLEAN; then merge `development/3.10` (after
   `fix/provider-scoped-caches` lands there), re-run the suites, and merge into `development/3.10` before the beta PR.

### 5.1 Task 1 (`0167f63`)

- The seam commit holds `DeletePredicateGuard` alone. The §4.1 tests land with Task 2, the change that turns them
  green, so each commit is green on its own, as the cold-cache plan's Task 1 did (`dd121c5`).
- Each §4.1 row ran alone at the seam, one test process per DataRow: 474 runs, 122 DB-free and 88 in each harness
  class (SQL Server, SQLite, PostgreSQL, MySQL). Each matched its class, and each Red row failed for its stated
  reason. net48's three D8 rows were Red with "Delete operation requires a non-trivial WHERE clause.".
- Baselines at `be8de82`, the four suites under coverlet (§4.2):

| File | Lines covered |
|---|---|
| `SqlServerOrmDataProvider.cs` | 1110 / 1269 (87.47 %) |
| `PostgreSqlOrmDataProvider.cs` | 976 / 1094 (89.21 %) |
| `MySqlOrmDataProvider.cs` | 977 / 1116 (87.54 %) |
| `SqliteOrmDataProvider.cs` | 902 / 978 (92.23 %) |

### 5.2 Task 2 (the commit that carries rev 7)

- D1–D6 and D8 as written, with rev 7's D3 escape rule.
- The rows rev 7 adds ran at the seam: the DB-free rows and `UnevaluatedParameterFreeParts_AreNotTranslated` are
  Guard, and the `x.Archived` row is Red (rows deleted; on SQL Server a `SqlException`).
- Every §4.1 row passes: 131 DB-free, 102 in each harness class (SQL Server, SQLite, PostgreSQL, MySQL), and net48's
  three D8 rows.
- Suites, all passing: SqlServer.Tests 1351, PostgreSql.Tests 887, MySql.Tests 840, Sqlite.Tests 800, DotNet9 5,
  net48 79.
- Coverage, the four suites under coverlet (§4.2):

| File | Lines covered | Baseline (§5.1) |
|---|---|---|
| `DeletePredicateGuard.cs` | 378 / 378 (100 %) | new |
| `SqlServerOrmDataProvider.cs` | 1101 / 1237 (89.01 %) | 87.47 % |
| `PostgreSqlOrmDataProvider.cs` | 978 / 1095 (89.32 %) | 89.21 % |
| `MySqlOrmDataProvider.cs` | 979 / 1117 (87.65 %) | 87.54 % |
| `SqliteOrmDataProvider.cs` | 903 / 979 (92.24 %) | 92.23 % |

- Mutations: 91 mutants for the §4.3 rows (a row may be several variants; the provider rows run per provider, and
  per path where §4.3 says so), each against `DeletePredicateGuardTests` or the harness class it names. Each was
  killed by the rows §4.3 names for it, with the NoColumn row's killer as rev 7 corrects it. They ran before the
  `x.Archived` and §6 rows were added, which add rows only. "The old substring list kept" also fails net48's three
  D8 rows.

### 5.3 Task 3 (the commit that carries rev 8)

- D7's documents: the Changelog's Fixed entry, its Changed entry for the three idioms (each proven by a harness row:
  `orCapturedNullIsNull`, `orCapturedTrue`, `x.Archived`), the scoped missing-table entry, the New public API entry,
  and the netstandard2.0/net48 entry and its Fixed pointer removed; `Usage.md`'s Delete section and troubleshooting
  item 4; `README.md`; the architecture document; SQL Server's XML docs on Delete and DeleteAsync; the cold-cache
  plan's D1 sentence. `FUNKYORM_AI_INSTRUCTIONS.md` stays true unchanged.
- The provider-scoped caches plan's pointers (its §4.2 note and §4.4 net48 line) are added when `development/3.10`
  is merged in (Task 4): that branch rewrapped the §4.2 note after `be8de82`, so an edit here would conflict.

### 5.4 Task 4, round 1 (the commit that carries rev 9)

- I1-1 to I1-5 are answered as §9.7 records; I1-6 is recorded in §6 and filed as a separate task.
- Red first: at `3ec5ba9` the rev 9 Classify rows that expect AlwaysTrue failed, the harness `Contains` and
  `ToString()` rows deleted every row on all four providers, and `HasLiteralTautology_TooDeeplyNested_ReturnsFalse`
  killed the test host. The I1-5 rows pass there and fail under their mutations.
- The rows rev 9 adds ran at the seam and matched their §4.1 class.
- Every §4.1 row passes: 150 DB-free, 116 in each harness class (SQL Server, SQLite, PostgreSQL, MySQL). Suites, all
  passing: SqlServer.Tests 1398, PostgreSql.Tests 901, MySql.Tests 854, Sqlite.Tests 800, DotNet9 5, net48 79.
- Coverage: `DeletePredicateGuard.cs` 407 / 407 (100 %); the provider files are unchanged from §5.2.
- Mutations: the 47 guard mutants ran on this layer's tests before the nullable rows were added, and the 10 of rev 9
  on the final tests; each was killed by the rows §4.3 names (the NoColumn row's killer as rev 7 corrects it). The 44
  provider mutants didn't run again: the providers are unchanged since `bcd254f`, and Task 4's reviewer killed all 44
  at `3ec5ba9`.
- The reviewer's 1,500-term nested `OR`, rerun with the fix on a 1 MB thread: SQL Server and SQLite reject the
  statement, and PostgreSQL and MySQL delete the two matching rows (rolled back), as at `be8de82`.

### 5.5 Task 4, fix-verification 1 (the commit that carries rev 10)

- FV1-1 to FV1-4 are answered as §9.8 records; the translator defects behind FV1-2 are filed as a separate task.
- Red first: at `09033fa` the rev 10 Classify rows that expect AlwaysTrue failed, the harness `ToString("D2")` and
  `Contains(capturedNull)` rows failed on all four providers, and `HasLiteralTautology_DeeplyNested_StillFolds`'s
  `OR` row returned false.
- The rows rev 10 adds ran at the seam and matched their §4.1 class.
- A 1,000-, 1,300- and 1,500-term nested `OR` ending in `|| !emptyIds.Contains(x.Id)`, on a 1 MB thread: rejected
  on all four providers. Without the tautology, at 1,500 terms: as at `be8de82`.
- Every §4.1 row passes: 162 DB-free, 120 in each harness class. Suites, all passing: SqlServer.Tests 1418,
  PostgreSql.Tests 905, MySql.Tests 858, Sqlite.Tests 800, DotNet9 5, net48 79.
- Coverage: `DeletePredicateGuard.cs` 446 / 454 (98.24 %); the uncovered lines are the large-stack thread's
  start-failure path and its re-raise of an unexpected exception. The provider files are unchanged from §5.2.
- Mutations: the 66 guard mutants (0–65 in `dg_mut.py`) ran on these tests, and each was killed by the rows §4.3
  names (the NoColumn row's killer as rev 7 corrects it). The `IFormatProvider` row first used `int.ToString`, which
  doesn't consult a provider for a non-negative value, so its counter couldn't see the mutant; it uses `DateTime`, and
  the five mutants it and the re-raise touch ran again on the final code. The 44 provider mutants didn't run again.

### 5.6 Task 4, fix-verification 2 (the commit that carries rev 11)

- FV2-1 and FV2-2 are answered as §9.9 records: prose in the Changelog, `Usage.md`, §6 and
  `HasLiteralTautology`'s XML doc. No code or test changed.

## 6. Out of scope (recorded)

- **Always-true shapes D2 doesn't detect** (T0-9). These deleted every row at the seam on all four providers and still
  will:
  - P ∨ ¬P: `x.Id == 2 || x.Id != 2`, `x.Id == 2 || !(x.Id == 2)`, `x.Flag || !x.Flag`;
  - self-comparisons through arithmetic or a method call (`x.Id + 0 == x.Id`, `x.Name.Trim() == x.Name.Trim()`,
    `x.Id.Equals(x.Id)`): D2 compares member chains only.

  The documents (D7) don't claim that every always-true predicate is rejected.
- **Always-true shapes the Task 0 re-review found and the guard doesn't detect** (rev 4, T2-3). Each deleted every row
  in that review's run:
  - a parameter-free string comparison that C# finds false and the server's collation finds true:
    `x.Id == 2 || s == "s"` with `s = "S"` on SQL Server and MySQL, and with `s = "s "` on SQL Server;
  - a comparison of a comparison: `(x.Id == x.Id) == true` (`t.id = t.id = @p`) on MySQL and SQLite; and (rev 5,
    T3-4; corrected in rev 6, T4-3) `capturedTrue == (x.Id == 2 || !emptyIds.Contains(x.Id))` (`@p = (… OR NOT 1=0)`)
    on PostgreSQL, MySQL and SQLite, and `x.Id == 2 || emptyIds.Contains(x.Id) == false` (`OR 1=0 = @p`) and
    `x.Id == 2 || !(emptyIds.Contains(x.Id) == true)` (`OR NOT 1=0 = @p`) on MySQL and SQLite;
  - a Conditional that chooses an empty-list branch: `capturedFalse ? x.Id == 2 : !emptyIds.Contains(x.Id)`
    (`CASE … ELSE NOT 1=0 END`) on PostgreSQL, MySQL and SQLite.
- **The double read** (T0-6). A property on a captured object is read once by the guard and again by the visitor. If
  its value changes between the reads, an Acceptable verdict can still send `OR @p` with a true value; D3 doesn't see
  parameters.
- **A core-library property can run user code.** D2 reads properties; `Lazy<bool>.Value` on a captured `Lazy` runs
  the user's factory during classification (the Task 0 re-review, §9.2).
- **`[SqlExpression]` text is the user's SQL.** A fragment that is always true (for example `TRUE`) isn't detected;
  one containing `1=1` is rejected by D3's rule, as today.
- **D3's parser doesn't know `BETWEEN`, SQL comments or MySQL's backslash escapes.** `x BETWEEN 1 AND 2` reads its
  `AND` as a logical one, a `--` comment isn't skipped, and `'it\'s'` is read as an unclosed quote, so the fold can't
  parse the clause and returns false (rev 5, T3-4; corrected in rev 6, T4-3). On MySQL,
  `x.BsQ == 1 || !emptyIds.Contains(x.Id)` with `[SqlExpression("CASE WHEN {FirstName} = 'it\\'s' THEN 1 ELSE 0 END")]`
  on `BsQ` deletes every row, as it does today. FunkyORM emits none of these; a `[SqlExpression]` could.
- **SQL NULL semantics.** `x.A == x.A` excludes rows where `A` is NULL; it's still rejected, as no one writes it as a
  filter.
- **A bare `bool` parameter in SQL Server's WHERE** (`x.Id == 2 || false`, `&& true`) is invalid SQL there
  (pre-existing translation limit); the guard doesn't change it.
- **The column-reference check** (check 4) stays substring-based and can over-accept.
- **The dead `InternalsVisibleTo("Funcular.Data.Orm.SqlServer")`** in Core is left as it is; D1 adds no grants.
- **Operators and method calls in a parameter-free part aren't evaluated** (rev 7; corrected in rev 9, I1-1). D2
  evaluates only the nodes it lists, so the guard accepts `x.Id == 2 || capturedA + 1 == 8`, `|| -capturedA == -7`,
  `|| emptyArray.Length == 0`, `|| capturedArray[0] == 5`, `|| (capturedNullBool ?? true)`,
  `string.IsNullOrEmpty(filter) || x.Col == filter`, and `|| s.StartsWith("s")` or `EndsWith`. Today every provider
  fails to translate or run each of them, sync and async (`NotSupportedException`; for `StartsWith`/`EndsWith` on a
  captured string a `NullReferenceException`), so nothing is deleted; `UnevaluatedParameterFreeParts_AreNotTranslated`
  fails if a provider starts running one. Rev 7 said every method call failed so; `string`'s `Contains` and
  `ToString()` ran and deleted every row, and D2 evaluates them since rev 9.
- **A `Contains` or `ToString` that C# finds false and the database finds true** (rev 9, I1-1; widened in rev 10,
  FV1-2; corrected in rev 11, FV2-1). D2 evaluates them as C# does; the providers send a captured string's `Contains`
  as a `LIKE` between two parameters, which compares under the database's default collation (on MySQL, the
  connection's), not a column's, and doesn't escape `%`, `_` or `[`; SQLite's `LIKE` ignores the case of ASCII
  letters; and they don't send `ToString` at all. Each of these deleted every row in the reviews, the first also with
  every column case-sensitive:
  `x.Id == 2 || s.Contains("s")` with `s = "S"` on SQL Server, MySQL and SQLite; `x.Id == 2 || s.Contains("_")` on
  all four; `x.Id == 2 || capturedA.ToString() == "07"` with `capturedA` 7 on SQL Server and MySQL. Translator
  defects, pre-existing; filed as a separate task.
- **A clause nested too deeply for D3's parser** (rev 9, I1-4; rev 10, FV1-3) is parsed again on a 64 MB stack, so
  a 1,500-term nested `OR` ending in `|| !emptyIds.Contains(x.Id)` is rejected on all four providers; without the
  tautology it runs as at `be8de82` (PostgreSQL and MySQL delete the matching rows; SQL Server and SQLite reject the
  statement). A clause too deep even for the 64 MB stack, or a platform that can't start the thread, isn't folded
  (rule 1 still applies).
- **A collection `Contains` whose item is a captured object's property** (rev 9, I1-6) is translated as the entity's
  column of the same name: `x.Id == 2 || ids.Contains(req.Id)` sends `t.id IN (…)`, and deleted rows 1 and 2 on all
  four providers in Task 4's review. A pre-existing translator defect outside the guard; filed as a separate task.
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

### 9.2 Task 0 re-review of rev 2 (`79069dd`; non-author; a seam export and a prototype of rev 2's D1–D6 on all eight paths with mutation switches; every §4.1 row on all four providers, sync and async, at both; the §4.3 mutations)

T0-1, T0-2, T0-3, T0-4, T0-7, T0-8, T0-10 and T0-11 are resolved; T0-5, T0-6 and T0-9 are partial. At the seam, 76 of
76 DB-free rows and every harness class matched the matrix; on the prototype every row passed; 28 of 30 mutations were
killed by the named rows. Verdict: NOT CLEAN. Blame: AC-GAP 2, TEST-GAP 3, HOUSE-RULE 1, PLAN-GAP 3.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| T1-1 | high | AC-GAP | Rev 2's D3 rejected any true literal term, so `c.ParentId == p && !keepIds.Contains(c.Id)` with `keepIds` empty (`AND NOT 1=0`), which deletes the right rows today, was rejected on all four providers. | D3 folds the whole clause; AC5 and harness rows for the idiom in both orders; HasLiteralTautology rows; a mutation row. |
| T1-2 | medium | PLAN-GAP | D1's `InternalsVisibleTo` grants fail to compile: CS0507 at seven provider overrides and one test override. | `DeletePredicateGuard` is public; no grants. |
| T1-3 | medium | TEST-GAP | D2 didn't say whether property getters count as methods, and no row could tell the readings apart; under one reading `\|\| request.IncludeAll` deleted every row. | D2 reads properties and calls no method, user operator or conversion, or constructor; `request.IncludeAll` rows; `StaticFlags.On` is a property; a mutation row; the counter test is renamed. |
| T1-4 | low | AC-GAP | `NOT (1=0 AND t.id = @p)` deleted every row past rev 2's D3, and the visitor's `1=1` in non-term positions (`1=1 = @p`, `CASE WHEN 1=1`) slipped past it. | D3's fold applies `NOT` to groups, and its `1=1` rule rejects `1=1` anywhere, as today; rows for both. |
| T1-5 | low | TEST-GAP | The quoted-segments and token-boundary mutation rows named killers that can't kill them. | New killer rows for each. |
| T1-6 | nit | TEST-GAP | D2's `Convert` rule had no row. | A hand-built Classify row. |
| T1-7 | nit | HOUSE-RULE | AC7 (documents) had no matrix row. The rev 2 note's "AC7 becomes a refactor note" means rev 1's AC7. | A Task 4 review row. |
| T1-8 | nit | PLAN-GAP | D7 cited a missing-table sentence in the sync Delete's doc that doesn't exist; §1 grouped `README.md:41` with documents about `x.Id == x.Id`; D8 left the test class summary unchanged. | Corrected. |
| T1-9 | nit | PLAN-GAP | The missing-table row couldn't count rows, and didn't drop its table first. | Its assertions are stated; it drops the table first. |

The reviewer also wrote to the author's worktree by mistake: a relative path added a UTF-8 BOM to
`Funcular.Data.Orm.SqlServer/SqlServer/SqlServerOrmDataProvider.cs` there, and nothing else. It isn't part of any
commit.

### 9.3 Task 0 re-review of rev 3 (`edfc072`)

Verdict: NOT CLEAN. T1-1 to T1-9 resolved (T1-1 for its shape). Blame: AC-GAP 1, TEST-GAP 1, HOUSE-RULE 1, PLAN-GAP 3.

| # | Sev | Blame | Location | Disposition |
|---|---|---|---|---|
| T2-1 | high | AC-GAP | D3's grammar: a run stopped at `(` and `NOT`, so `IN (…)`, `IS NOT NULL`, function calls and `NOT IN` didn't parse, and the plan didn't say what a parse failure does. | D3's grammar rewritten; parse failure returns false; HasLiteralTautology, AC4, AC5 and harness rows; mutation rows. |
| T2-2 | low | TEST-GAP | §4.3: the `[SqlExpression]` conjunction row can't kill "literal next to arithmetic"; no row for `<>`, `<=`, keyword case or keywords inside identifiers. | A standalone `[SqlExpression]` row; HasLiteralTautology rows; mutation rows. |
| T2-3 | low | PLAN-GAP | §6: four always-true shapes the guard doesn't detect weren't recorded. | Recorded in §6. |
| T2-4 | nit | PLAN-GAP | D1: the public members' argument contract was unstated. | Stated; `PublicMembers_RejectBadArguments`. |
| T2-5 | nit | HOUSE-RULE | AC8's waiver for files below the floor. | Removed. |
| T2-6 | nit | PLAN-GAP | D7: the historical provider plans describe the guard as it was. | They stay unedited, as history. |

### 9.4 Task 0 re-review of rev 4 (`fa06956`)

Verdict: NOT CLEAN; four blocking findings, two non-blocking nits in §9 and the revision notes. T2-3, T2-5 and T2-6
resolved; T2-1, T2-2 and T2-4 partial.

| # | Sev | Blame | Location | Disposition |
|---|---|---|---|---|
| T3-1 | medium | TEST-GAP | §4.1 and §4.3: mutations that survived the named rows (CASE absorption, parse failure, keywords inside identifiers, the zero-parameter case), wrong killer names, and §4.1's claim that every Guard row has a killing mutation. | Rows added; killers corrected; D3 checks rule 1 first; §4.1's intro reworded. |
| T3-2 | low | PLAN-GAP | D7's Changelog entries. | They name D3's data-loss fix, the third idiom, the message change and the namespace. |
| T3-3 | low | AC-GAP | AC6 and D8 contradicted each other on `DeleteGuardCaseTests`. | AC6 excludes it. |
| T3-4 | nit | PLAN-GAP | §6's comparison-of-a-comparison and parser bullets. | Widened. |

The two non-blocking nits are carried, not answered.

### 9.5 Task 0 re-review of rev 5 (`5845883`)

Verdict: NOT CLEAN; four blocking findings, two non-blocking nits in §9.4. T3-2 and T3-3 resolved; T3-1 and T3-4
partial. Blame: TEST-GAP 3, PLAN-GAP 1.

| # | Sev | Blame | Location | Disposition |
|---|---|---|---|---|
| T4-1 | low | TEST-GAP | D5's order: `HasLiteralTautology` before the column check, and the null and transaction guards before `Validate`, had no row. | Rows for each edge; §4.3 rows. |
| T4-2 | low | TEST-GAP | D3 rule 1's unclosed-quote behaviour had no row. | Rule 1 masks nothing after an unclosed quote; a row; a §4.3 row. |
| T4-3 | low | PLAN-GAP | §6's comparison-of-a-comparison and backslash bullets. | Corrected. |
| T4-4 | nit | TEST-GAP | §4.3's CASE mutation named one killer of two forms. | Both killers named. |

### 9.6 Task 0 re-review of rev 6 (`58efaca`)

Verdict: CLEAN. T4-1 to T4-4 resolved. One non-blocking nit, T5-NB1: §9.5 doesn't say whether §9.4's two nits are
answered. They are carried, not answered.

### 9.7 Task 4 review of `58efaca..3ec5ba9` (non-author)

Verdict: NOT CLEAN; five blocking findings and one non-blocking. Blame: TEST-GAP 1, PLAN-GAP 1, OTHER 4.

| # | Sev | Blame | Location | Disposition |
|---|---|---|---|---|
| I1-1 | medium | PLAN-GAP | D2; §6's premise for method calls | Owner decision 2026-10-02: D2 evaluates `string`'s `Contains` and a core type's `ToString()`; AC1, rows and mutations; §6 corrected; the collation case recorded. |
| I1-2 | low | OTHER | Changelog: the Changed lead, the Fixed comparison clause, the letter-case sentence | Reworded. |
| I1-3 | low | OTHER | `Usage.md` troubleshooting item 4 | "Before the DELETE is sent", and when SQL runs first; method calls and collation in the Warning. |
| I1-4 | medium | OTHER | D3's parser recursion | A stack check; a clause too deep to parse returns false; a DB-free row on a 1 MB thread; mutations. |
| I1-5 | low | TEST-GAP | D3: tokens after the top-level `OR`; nested `CASE` | Two rows; two mutations. |
| I1-6 | medium | OTHER (non-blocking) | the translators: a captured property as a collection `Contains` item | §6; filed as a separate task. |

### 9.8 Task 4 fix-verification of `3ec5ba9..09033fa` (non-author)

Verdict: NOT CLEAN; four blocking findings. I1-3, I1-5 and I1-6 resolved; I1-4 resolved for the crash; I1-1 and I1-2
partial. Blame: AC-GAP 2, TEST-GAP 2.

| # | Sev | Blame | Location | Disposition |
|---|---|---|---|---|
| FV1-1 | medium | AC-GAP | D2: `ToString` with a format string; the Changelog's `ToString` sentence; `Classify`'s XML doc | D2 evaluates a format-string `ToString`; AC1, rows and mutations; the Changelog and XML doc corrected. |
| FV1-2 | medium | AC-GAP | D2 evaluates as C# does; the database disagrees (wildcards, a null search value, `ToString` not sent) | A `Contains` of null is True, with rows and mutations; the rest disclosed in §6, the Changelog and Usage, and filed as a separate task. |
| FV1-3 | low | TEST-GAP | D3's stack check failed open below the visitors' depth | A second parse on a 64 MB stack; one-pass matching; rows; mutations; §6. |
| FV1-4 | low | TEST-GAP | rev 9's receiver rules had no failing row | Counters for a user `Contains`, a user `IFormatProvider` and a `KeyValuePair`; mutations. |

### 9.9 Task 4 fix-verification of `09033fa..fcc560c` (non-author)

Verdict: NOT CLEAN; two blocking findings, in prose, and one non-blocking nit. FV1-1, FV1-3 and FV1-4 resolved; FV1-2
partial (FV2-1). Blame: TEST-GAP 2.

| # | Sev | Blame | Location | Disposition |
|---|---|---|---|---|
| FV2-1 | low | TEST-GAP | The Changelog, `Usage.md` and §6: the collation a captured string's `Contains` compares under | Named: the database's default (MySQL: the connection's), not a column's; SQLite's `LIKE` ignores ASCII case. |
| FV2-2 | low | TEST-GAP | `HasLiteralTautology`'s XML doc on a clause too deep to parse | Describes the large-stack parse and the re-raise. |
| FV2-NB1 | nit | — | §9.8's blame classes differ from the review's, without saying so | Carried. |
