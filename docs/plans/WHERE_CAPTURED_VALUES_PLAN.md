# WHERE method calls: captured items, receivers and values — Implementation Plan

> **Goal:** make the translator's method calls read captured values the way C# does and never mistake one for a
> column, on all four providers: WHERE for queries, counts and deletes, sync and async, and the SELECT projections
> that share the translator.
> - Ships in **3.10.0, before the beta PR** (owner decision 2026-10-02).
> - Branch `fix/where-captured-values`, cut from `development/3.10` at `35c6477`. It merges into `development/3.10`
>   only after `fix/delete-guard-trivial-predicates` has, and after its Task 6 (D11).
> - Started from the delete-guard review's finding I1-6 (`DELETE_GUARD_PLAN.md` §6, rev 9). The probe (§1) found a
>   second defect and its siblings; the owner folded them in (D2, D3).

> **Status (2026-10-02):** rev 2. Task 0 review of rev 1 (`8e2a392`, §9.1) found 15 findings, 6 blocking; rev 2
> answers them with the owner's four decisions (D7, D8, D12, D13). Task 0's re-review is next.

> **Revision 2 — what changed (Task 0, T0-1…T0-15):**
> - A string `Contains` that doesn't read the row is answered in C# and sent as D4's literal (owner, T0-1); a null
>   receiver or value there, and a null search value anywhere, matches no row (owner, T0-2): D7 and D8 rewritten.
> - SELECT projections are in scope (owner, T0-4: D12, AC13). The visitor's member-dropping goes to the date-part plan
>   (owner, T0-5: D13, §8).
> - The collection read is wrapped like every other evaluation (T0-3); D5's order is item → collection (T0-12).
> - The SQLite rows live in Sqlite.Tests and assert the C# 14 binding; CI facts corrected (T0-6).
> - The evaluator interprets, so the span rewrite is exercised on every platform (T0-7); null links throw C#'s own
>   exceptions (T0-13).
> - §1.2's reachability premise corrected (T0-8); `oneAndNullX` removed (T0-9); a "red at `35c6477`" column, with the
>   killer of each row that passes by luck (T0-10); a `count` path (T0-11).
> - Merge is gated on Task 6 (T0-14); expected conflicts, C# 7.3 for the net48 file, a static-receiver row, logs after
>   the MySQL warm-up, and the visitor's D9 sibling (T0-15).

---

## 1. Verified premises

### 1.1 The probe (executed 2026-10-02 against `35c6477`; re-executed in part by the Task 0 reviewer)

A throwaway console app (scratchpad, not committed; net8.0) references the four providers. Each server gets a table
`zz_cci_probe` with rows (1, 'A', 'alpha', 1), (2, 'b', 'beta', NULL), (3, 'c', 'gamma', 3) for
(`Id`, `Code`, `Name`, `NullableId`); SQLite gets a temp file. Each shape runs five ways: `Query<T>(predicate)`,
`QueryAsync<T>(predicate)`, `Query<T>().Where(predicate).ToList()`, and `Delete`/`DeleteAsync` inside a transaction
that is rolled back after the remaining rows are read through the provider. The oracle is LINQ to objects over the
same three rows. Each table was dropped (each SQLite file deleted) at the end.

The five paths agreed on every shape, and the four providers agreed except where a cell says otherwise. Captured
values: `ids = {1, 2}`; `absent` has `Id = 5`, `Code = "a"`, `Name = "zeta"`, `NullableId = 5`, `Inner.Id = 5`,
`Ids = {1, 2}`; `present` has `Id = 1`; `nullHolder.NullableId = null`; `local = 5`; `s = "zeta"`. `Child` is a
`[NotMapped]` property of the entity; `Filter` and `Code` in U1–U3 are properties of the enclosing object (a captured
`this`). Rows marked † were executed by the Task 0 reviewer (§9.1).

| # | Predicate | C# | All four providers | SQL Server's WHERE |
|---|---|---|---|---|
| S01 | `x.Id == 2 \|\| ids.Contains(absent.Id)` | {2} | {1,2} | `(t.id = @p0 OR t.id IN (@p1,@p2))` |
| S02 | `x.Id == 2 \|\| ids.Contains(present.Id)` | {1,2,3} | {1,2} | same |
| S03 | `x.Id == 2 && ids.Contains(present.Id)` | {2} | {2} (by luck) | `(t.id = @p0 AND t.id IN (…))` |
| S04 | `x.Id == 2 && ids.Contains(absent.Id)` | {} | {2} | same |
| S05 | `ids.Contains(absent.Id)` | {} | {1,2} | `t.id IN (@p0,@p1)` |
| S06 | `x.Id == 2 \|\| !ids.Contains(absent.Id)` | {1,2,3} | {2,3} | `… OR NOT t.id IN (…)` |
| S07 | `x.Id == 2 \|\| arr.Contains(absent.Id)` (`int[]`) | {2} | {1,2} — also under C# 14 on SQLite, where it binds to `MemoryExtensions.Contains` | as S01 |
| S08 | `x.Id == 2 \|\| ids.Contains(local)` | {2} | `InvalidOperationException` ("Member local is not a property.") | — |
| S09 | `x.Id == 2 \|\| ids.Contains(5)` | {2} | `NullReferenceException` | — |
| S10 | `x.Id == 2 \|\| codes.Contains(absent.Code)`, `codes = {"A"}` | {2} | {1,2} | |
| S11 | `x.Id == 2 \|\| nulls.Contains(nullHolder.NullableId)`, `nulls = {null}` | {1,2,3} | {2} | `… OR 1=0` |
| S12 | `x.Id == 2 \|\| ci.Contains(absent.Code)`, `ci` = `HashSet` (OrdinalIgnoreCase) `{"A"}` | {1,2,3} | {1,2} | |
| S13 | `x.Id == 2 \|\| ids.Contains(absent.Inner.Id)` | {2} | {1,2} | |
| S14 | `x.Id == 2 \|\| ids.Contains(StaticHolder.Id)` (5) | {2} | {1,2} | |
| S15 | `x.Id == 2 \|\| ids.Contains(absent.NullableId.Value)` | {2} | server error (column `value`) | |
| S16 | `x.Id == 2 \|\| absent.Ids.Contains(5)` | {2} | server error (`t.ids LIKE '%' + @p + '%'`; `\|\|` on PostgreSQL and SQLite, `CONCAT` on MySQL†) | |
| S17 | `x.Id == 2 \|\| ids.Contains(nullRequest.Id)` | throws `NullReferenceException` | {1,2} | |
| S18–S20 | `ids.Contains(x.Id)`, `!ids.Contains(x.Id)`, `{1}.Contains(x.NullableId)` (`int?` list) | {1,2}, {3}, {1} | correct | |
| S21 | `lengths.Contains(x.Name.Length)`, `lengths = {4}` | {2} | server error (`t.length`); Delete: the column check | |
| S22 | `longs.Contains(x.Id)` (`List<long>`: a `Convert` item) | {1,2} | `NullReferenceException` | |
| S23 | `x.Id == 2 \|\| absent.Name.StartsWith("al")` | {2} | {1,2} | `… OR t.name LIKE @p1 + '%'` |
| S24 | `x.Id == 2 \|\| absent.Name.EndsWith("ha")` | {2} | {1,2} | |
| S25 | `x.Id == 2 \|\| absent.Name.Contains("lph")` | {2} | `NotSupportedException`; SQLite `InvalidOperationException` | |
| S26 | `x.Id == 2 \|\| s.StartsWith("al")` | {2} | `NullReferenceException` | |
| S27 | `x.Name.StartsWith("al")` | {1} | correct | |
| S28 | `ids.Contains(x.Child.Id)` | throws (`Child` is null) | {1,2} | `t.id IN (…)` |
| T1 | `x.Code == absent.Code.ToString()` | {} | `NotSupportedException` ("Type Request is not supported …"); SQLite `InvalidOperationException` | |
| T2 | `x.Code == x.Name.Length.ToString()` | {} | {} — but the SQL drops `.Length` | `t.code = t.name` |
| T3 | `x.Name.Contains(absent.Name)` | {} | `NotSupportedException` | |
| T4 | `x.Child.Name.Contains("a")` | throws | server error (`t.child`) | |
| T5 | `x.Child.Name.StartsWith("a")` | throws | {1} | `t.name LIKE @p0 + '%'` |
| T6, T7 | `nullList.Contains(x.Id)`, `x.Id == 2 \|\| nullList.Contains(absent.Id)` | throws | `NullReferenceException` | |
| T8 | `{1}.Contains((int?)x.Id)` | {1} | `NullReferenceException` | |
| U1 | `x.Id == 2 \|\| x.Name.Contains(Filter)`, `Filter = "zz"` | {2} | **{1,2,3}** | `… OR t.name LIKE @p1`, `@p1 = '%%'` |
| U2 | `x.Id == 2 \|\| x.Name.StartsWith(Filter)` | {2} | `NotSupportedException` | |
| U3 | `x.Code == Code.ToString()`, `Code = "b"` | {2} | {} | `t.code = @p0`, `@p0` NULL |
| N1 | `{1, null}.Contains(x.NullableId)` | {1,2} | {1} (SQL Server, SQLite) | `t.nullable_id IN (@p0)` |
| N2 | `x.Name.Contains("%")` | {} | {1,2,3} (SQL Server, SQLite) | `@p0 = '%%%'` |
| C1† | `x.Id == 2 \|\| s.Contains("ZETA")` (a captured local receiver) | {2} | SQL Server, MySQL, SQLite {1,2,3} (Delete too); PostgreSQL {2} | `… OR @p1 LIKE @p2` |
| O1† | `filter == null \|\| x.Name.Contains(filter)`, `filter = null` (also as a property of `this`) | {1,2,3} | {1,2,3} | `(@p0 IS NULL OR t.name LIKE '%%')` |
| O4† | `filter == null \|\| x.Name.StartsWith(filter)`, `filter = null` | {1,2,3} | `NotSupportedException` | |
| O5† | `x.Name.Contains(null)` | throws | {1,2,3} (Delete too) | |
| R1† | `nullRequest.Ids.Contains(x.Id)` | throws | `TargetInvocationException` | |
| P1† | `Select(x => new T { Id = x.Id, Label = ids.Contains(absent.Id) ? "in" : "out" })` | out, out, out | in, in, out | `CASE WHEN t.id IN (…) …` |
| P2† | `Select(x => new T { Id = x.Id, Flag = ids.Contains(absent.Id) })` | false ×3 | wrong values on PostgreSQL, MySQL, SQLite; SQL Server syntax error | |
| K1† | `Count(x => x.Id == 2 \|\| ids.Contains(absent.Id))` | 1 | 2 | |
| V1† | `x.Name.Length != 4` (the visitor, not the translator) | {1,3} | SQLite {1,2,3} (Delete too); MySQL {1,2,3}; SQL Server, PostgreSQL server errors | `t.name != @p0` |

So, on every provider and path:
- **I1-6** (S01–S17, S28, P1, P2, K1): a collection `Contains` whose item is a member is translated as the entity's
  column of that member's name, whatever the member's root. Deletes remove rows C# doesn't select (S01, S02, S05,
  S06, S10, S12–S14, S17, S28) or keep rows it does (S04, S11).
- **Siblings, same root cause** (S23, S24, T5): `StartsWith`/`EndsWith` translate any member receiver as the entity's
  column. T2 drops a member in the translator; V1 does the same in the visitor (D13).
- **A second root cause** (U1, U3): a captured value reached through a property, not a field, is read as null.
  U1's delete removes every row whose `Name` isn't null.
- A row-independent string `Contains` is compared by the server's collation (C1), and a null search value sends
  `'%%'` (O5), so an unguarded null deletes every non-null row while the guarded idiom O1 works.
- N1 and N2 are separate defects (§8).

### 1.2 Code facts the design relies on

- **The item (I1-6).** `SqlExpressionTranslator.cs:163-170` (PostgreSQL and MySQL `:144`, SQLite `:147`): any
  `MemberExpression` item whose member is a property goes to `getColumnName`; nothing checks that the member's
  expression is the lambda parameter. A closure field fails at `:166-168`. Any other item (a constant) leaves the
  branch and reaches `:189`.
- **The receivers.** `:189-194` (PostgreSQL and MySQL `:170-172`, SQLite `:173-175`): `node.Object as
  MemberExpression` goes to `getColumnName` with no root check. A closure field passes `null` (S09, S26).
- **The `Contains` pattern in that block** (`:230-233`; SQLite's `else // Contains`) is reached today by a collection
  `Contains` whose collection evaluates and whose item isn't a member (S16). After D4 (a row-independent call never
  gets there) and D5 (an item that reads the row is checked first), no `Contains` reaches it.
- **Captured values read as fields only.** The string `Contains` value (`:70-72`), the `StartsWith`/`EndsWith`
  argument (`:204-206`) and `TranslateExpression` (`:313-319`) read `(Member as FieldInfo)?.GetValue(...)`, so a
  property reads null. `TranslateExpression` recurses into any other member's inner expression (`:320-323`), dropping
  the outer member (T2), or sending the captured object itself as a parameter (T1, S25).
- **The collection read** (`:125-132`) is `Compile().DynamicInvoke()`, catching only `InvalidOperationException`, so
  a getter that throws escapes as `TargetInvocationException` (R1).
- **The visitor.** `WhereClauseVisitor.cs:228-257` reads fields and properties, and `:269-286` evaluates nested
  chains, so `x.Code == absent.Code` works. But `:288-292` recurses into a member of a member of the parameter and
  drops the outer member (V1), as the date-part defect does; and `:277-280` drops the inner exception, while a
  closure property read (`:237`) lets a `TargetInvocationException` escape. The other three providers' visitors are
  copies.
- **Three consumers of `TranslateMethodCall`.** The WHERE visitor (`WhereClauseVisitor.cs:351`); the LINQ
  provider's `Count`/`LongCount`/`Any`/`All` with a predicate, which build their own `WhereClauseVisitor` with the
  query's translator (`SqlLinqQueryProvider.cs:551-560`, K1); and the SELECT projection visitors
  (`SelectClauseVisitor.cs:290`, `PostgreSqlSelectClauseVisitor.cs:174`, `MySqlSelectClauseVisitor.cs:172`,
  `SqliteSelectClauseVisitor.cs:173`; P1, P2).
- **No WHERE text is cached.** `GenerateWhereClause` runs a new visitor on every call
  (`SqlServerOrmDataProvider.cs:1424-1445`; the LINQ provider calls it per `Where`/terminal, `SqlLinqQueryProvider.cs:276,
  319`). A literal computed from captured values can't go stale.
- **Delete validation on `35c6477`.** Each provider rejects a WHERE containing `1=1`, `1<2`, `1>0` or `true` (spaces
  removed); SQL Server's self-reference regex is anchored (`SqlServerOrmDataProvider.cs:851`); PostgreSQL, MySQL and
  SQLite use the unanchored `\b(\w+)\s*=\s*\1\b` (`PostgreSqlOrmDataProvider.cs:892`, `MySqlOrmDataProvider.cs:870`,
  `SqliteOrmDataProvider.cs:818`), which matches `1=1` and `0=0`; then a substring column check.
- **The delete-guard rewrite** (`fix/delete-guard-trivial-predicates`, rev 9). Its D2 classifies a collection
  `Contains` call as Unknown and evaluates string `Contains` as C# does. Its D3 rule 1 rejects `1=1` anywhere, and
  rule 2 folds runs of `<number> <op> <number>` under `NOT`/`AND`/`OR`: `(t.id = @p OR NOT 1=0)` is rejected and
  `(t.parent_id = @p AND NOT 1=0)` accepted (its examples; the Task 0 reviewer re-ran them). It keeps the regexes
  (its D4). Its tripwire `UnevaluatedParameterFreeParts_AreNotTranslated` asserts only that the delete throws, logs no
  DELETE and keeps every row; its doc comment names a `NullReferenceException` for `StartsWith`/`EndsWith`. No test
  there pins I1-6's behaviour.
- **SQL.** `NOT 1=0`, `NOT NOT 1=0` and their `AND`/`OR` forms run and evaluate correctly on all four servers, MySQL
  also under `HIGH_NOT_PRECEDENCE` (Task 0 reviewer, executed). `NULL LIKE NULL`, `NOT NULL LIKE NULL`,
  `v LIKE NULL` and `NOT v LIKE NULL` select no row on all four, `(NOT 1=0 OR NULL LIKE NULL)` selects it and
  `(NOT 1=0 AND NOT NULL LIKE NULL)` doesn't: SQL's UNKNOWN (executed 2026-10-02).
- **C# 14's array `Contains`** is `MemoryExtensions.Contains(ReadOnlySpan<T>.op_Implicit(arr), item)`, with the
  conversion as a `MethodCallExpression`. `Compile()` evaluates it; `Compile(preferInterpretation: true)` throws
  `ArgumentException` (a span can't be an interpreter result); the `Enumerable.Contains` rewrite interprets. Executed
  on net8.0, 2026-10-02.
- **Interpretation is available everywhere Core runs.** `Compile(preferInterpretation: true)` called from a
  netstandard2.0 library runs on net48 and net8.0, and a null dereference inside surfaces as
  `NullReferenceException` (executed 2026-10-02).
- **Language versions.** SqlServer.Tests, PostgreSql.Tests and MySql.Tests use net8.0's default (C# 12): arrays bind
  to `Enumerable.Contains`. Sqlite.Tests and Core set `LangVersion` latest (C# 14 with the 10.0.301 SDK; there is no
  `global.json`): arrays bind to `MemoryExtensions.Contains`. The net48 project
  (`Funcular.Data.Orm.SqlServer.Tests.NetFramework`) is old-style with no `LangVersion`: C# 7.3.
- **CI.** `ci.yml` builds and tests SqlServer.Tests only, with SDK 8.0.x (C# 12). PostgreSQL and MySQL have their own
  workflows; no workflow runs Sqlite.Tests. The net48 job is commented out.
- **Visibility.** The translators, their parameter generators and `TranslateMethodCall`/`TranslateDateMember` are
  public. Core grants `InternalsVisibleTo` to the SQL Server test assemblies, the net48 one included
  (`AssemblyInfo.cs`). Core's public query-translation helpers (`ScalarProjectionGuard`, `QueryOperatorPolicy`) live
  in `Funcular.Data.Orm.Linq`.
- **Baseline at `35c6477`.** All tests pass: SQL Server 885, SQLite 788, PostgreSQL 760, MySQL 712. Translator line
  coverage (cobertura): SQL Server 63.6 %, SQLite 58.5 %, PostgreSQL 49.1 %, MySQL 36.8 %.

---

## 2. Decisions

- **D1 (owner, 2026-10-02):** 3.10.0, before the beta; its own branch from `development/3.10`.
- **D2 (owner, 2026-10-02):** the captured-property read (U1, U3) is fixed on this branch.
- **D3 (owner, 2026-10-02):** the receiver siblings (S23, S24, T2, T5) are fixed here, failing closed: a
  `StartsWith`/`EndsWith` receiver, or a `ToString()` receiver that reads the row, that isn't a member of the lambda
  parameter throws, and isn't evaluated.
- **D4 — A `Contains` that doesn't read the row is answered in C# and sent as a constant.** This covers a collection's
  `Contains` and string's `Contains` (D7). The call is evaluated as written (its own `Contains`, its comparer, its
  `StringComparison`) and sent as `1=0` when false and `NOT 1=0` when true.
  - Why evaluate, not send `@p IN (…)` or `@p LIKE …`: a collection's comparer can't be expressed in SQL (S12), an
    `IN` list drops nulls (S11), a server collation differs from C#'s ordinal equality (S10, C1), and a parameter
    comparison is opaque to both delete guards, so an always-true `OR` would delete every row whichever guard runs.
  - Why these literals: `1=0` is today's emission for an empty collection. `NOT 1=0` is what `!emptyIds.Contains(x.Id)`
    already sends, and the rewritten guard folds it by position (rejected in an `OR`, accepted in an `AND`). `1=1` is
    rejected anywhere by the rewritten guard's rule 1 and by today's substring list; `0=0` and `1=1` match the
    PostgreSQL/MySQL/SQLite self-reference regex. Either would reject `x.Id == id && allowed.Contains(user.Role)`.
  - A row-independent call is answered before anything else is evaluated, so captured values are read once.
- **D5 — A collection `Contains` that reads the row** is translated only as `member IN (…)`, with the member a
  property of the lambda parameter. Order: D4's evaluation; the `.Value` check (`NullableExpressionException`, as
  today); the item, which must be a member of the lambda parameter, else `NotSupportedException` naming the call;
  then the collection, read per D9 (a collection that reads the row keeps today's "parameter references" message);
  then a null collection, `NotSupportedException`; then the empty short-circuit and the `IN` list as today. So
  whether an item is rejected doesn't depend on what the collection holds, and no user code runs for a rejected item.
  The translator calls D4's evaluator only for a `bool`-returning call.
- **D6 — `StartsWith`/`EndsWith` receivers** must be a member of the lambda parameter, else `NotSupportedException`.
  The `Contains` pattern in that block, unreachable after D4 and D5 (§1.2), is deleted, so the block handles
  `StartsWith`/`EndsWith` only and every other call reaches the final "not supported".
- **D7 (owner, 2026-10-02, T0-1) — Captured values.**
  - A string `Contains` whose receiver and arguments don't read the row is D4's: answered in C# and sent as the
    literal. When its receiver or value is null it is sent as `NULL LIKE NULL`, which matches no row (D8), as
    today's `NULL LIKE …` does.
  - Wherever else the translator needs a value — string `Contains`', `StartsWith`'s and `EndsWith`'s argument when
    the receiver is a column, and a captured receiver of `ToString()` — a constant, or a chain of field and property
    reads rooted at a constant or a static member, is read as C# reads it and sent as a parameter. Anything else (a
    method call, a conversion, a chain that reads the row) is not supported, as today.
  - A `ToString()` value is compared by the visitor, in SQL, as `x.Code == absent.Code` is today (§8).
- **D8 (owner, 2026-10-02, T0-2; replaces rev 1's "throw") — A null search value matches no row.** String
  `Contains`, `StartsWith` and `EndsWith` with a column receiver and a null value send `<column> LIKE NULL`. So the
  optional-filter idiom `filter == null || x.Name.Contains(filter)` keeps returning every row (O1), the same idiom
  with `StartsWith` stops throwing (O4), and a bare `x.Name.Contains(null)` returns and deletes nothing instead of
  every non-null row (O5). C# would throw for a reached `Contains(null)`; the translator can't see the guard, and
  "no row" is the answer that is right for the guarded idiom and safe for the unguarded one.
- **D9 — Evaluation failures.** A read or a call that throws while the translator evaluates it (a null captured
  object, a throwing getter, a throwing collection) is reported as
  `NotSupportedException("Could not evaluate expression: <node>")` — the visitor's message (`WhereClauseVisitor.cs:279`)
  — with the original exception as `InnerException` (unwrapped from `TargetInvocationException`), before any command
  is sent. A guarded shape (`req == null || ids.Contains(req.Id)` with `req` null) throws too, as the visitor's
  `x.Id == filterId.Value` does today (§8).
- **D10 — One public Core helper**, `Funcular.Data.Orm.Linq.RowIndependentEvaluator`, which the four translators call
  (precedents: `ScalarProjectionGuard`, `QueryOperatorPolicy`). It evaluates by interpretation
  (`Compile(preferInterpretation: true)`), so the span rewrite runs on every platform and a one-shot evaluation
  generates no code. No `InternalsVisibleTo` is added. §5.1 gives its members. The Changelog lists it under "New
  public API".
- **D11 — Merge gate.** This branch doesn't merge into `development/3.10` until the delete-guard rewrite is there and
  Task 6 is CLEAN: D4 against the old guard would let `x.Id == 2 || ids.Contains(present.Id)` delete every row.
  Before Task 6 the delete rows use only shapes whose outcome is the same under both guards (§4.1). The PR carries
  the stacked-PR warning block (merge order, do not auto-complete). Pushing the branch itself (after Task 5) is
  allowed.
- **D12 (owner, 2026-10-02, T0-4) — SELECT projections are covered.** They share `TranslateMethodCall`, so D4–D9
  apply there; AC13 and its rows prove the `CASE WHEN` shape and a rejection on every provider.
- **D13 (owner, 2026-10-02, T0-5) — The visitor's member-dropping** (V1: `x.Name.Length != 4`) goes to the date-part
  plan, which rewrites the same `VisitMember` lines. This plan records it in §8 and hands it over.

---

## 3. Acceptance criteria (3.10.0)

- **AC1 (I1-6).** A collection `Contains` whose item doesn't read the row — a captured object's property, a nested
  one, a local, a constant, a static member, a captured `Nullable<T>.Value`, with a collection that may itself be a
  captured object's property — is answered as C# answers it, on all four providers.
  - `Query<T>(predicate)`, `QueryAsync<T>(predicate)`, `Query<T>().Where(predicate)` and
    `Query<T>().Count(predicate)` return (count) the rows LINQ to objects returns over the same rows.
  - `Delete`/`DeleteAsync` delete those rows (AC9 covers predicates that become always true).
  - Its item is never translated as a column.
- **AC2.** The answer is the call as written: the collection's own `Contains` and comparer (a case-insensitive
  `HashSet<string>`, `Enumerable.Contains` with a comparer, a user collection's `Contains`), ordinal string equality,
  a null item, and C# 14's `MemoryExtensions.Contains` over an array, evaluated by interpretation. A string
  `Contains` that doesn't read the row is answered the same way (ordinal, or the `StringComparison` it's given).
- **AC3.** Such a call is sent as `1=0` when C# finds it false and `NOT 1=0` when true. Neither its receiver, its item
  nor the collection's values are sent as parameters.
- **AC4.** A collection `Contains` whose item reads the row is translated only when the item is a member of the
  lambda parameter (`x.Prop`, as today). Another item that reads the row (`x.Child.Id`, `x.Name.Length`, `(long)x.Id`)
  throws `NotSupportedException` before any command is sent and before the collection is read, whatever the
  collection holds. `.Value` keeps its `NullableExpressionException`, also with an empty collection.
- **AC5.** `StartsWith` and `EndsWith` translate their receiver as a column only when it is a member of the lambda
  parameter. A receiver that doesn't read the row (`request.Name`, a local, a static member) or reads it through
  another member (`x.Child.Name`) throws `NotSupportedException` before any command is sent.
- **AC6.** A value given to string `Contains`, `StartsWith` or `EndsWith` with a column receiver, and a captured
  receiver of `ToString()`, is read as C# reads it: a property of a captured `this`, a nested property
  (`request.Name`), a static member. `x => x.Id == 2 || x.Name.Contains(Filter)` returns and deletes row 2 only;
  `x => x.Code == Code.ToString()` returns row 2.
- **AC7.** A receiver of `ToString()` or of string `Contains` that reads the row through another member
  (`x.Name.Length`, `x.Child.Name`) throws `NotSupportedException` instead of losing a member.
- **AC8.** Nulls and failures:
  - a null value for string `Contains`, `StartsWith` or `EndsWith` with a column receiver matches no row; the guarded
    idiom (`filter == null || x.Name.Contains(filter)`, also with `StartsWith`) returns every row; a string
    `Contains` that doesn't read the row, with a null receiver or value, matches no row (D7, D8);
  - a read or a call that throws while the predicate is translated (a null captured object, a throwing collection)
    throws `NotSupportedException` whose `InnerException` is the original exception, before any command is sent
    (D9). Nothing is deleted.
- **AC9 (Task 6, with the delete-guard rewrite merged).** A delete whose evaluated `Contains` makes the predicate
  always true (`x.Id == 2 || ids.Contains(present.Id)`, `x.Id == 2 || !ids.Contains(absent.Id)`) is rejected with
  "Delete operation requires a non-trivial WHERE clause." and deletes nothing. In an `AND` it deletes what the rest
  selects. Alone (`x => ids.Contains(absent.Id)`) it's rejected with the column-reference message.
- **AC10. Unchanged:** `ids.Contains(x.Id)`, `!ids.Contains(x.Id)`, an `int?` list with an `int?` member, an empty
  collection with a member item (`1=0`), a collection holding nulls with a member item (the `IN` list it sends, N1),
  `x.Name.StartsWith("al")`, `x.Name.Contains("mm")`, the guarded idiom O1, and C# 14's `arr.Contains(x.Id)`
  translate and return as before.
- **AC11.** `RowIndependentEvaluator`'s contract (§5.1): a null argument throws `ArgumentNullException`;
  `TryTranslateContains` given a call that isn't a `bool`-returning `Contains` throws `ArgumentException`, and returns
  `false` for a call that reads the row without evaluating anything; `TryReadValue` returns `false` for anything but a
  constant or a rooted chain of field and property reads.
- **AC12.** The Changelog's `[3.10.0-beta1]` section and `Usage.md` describe the new behaviour (Task 4).
- **AC13 (D12).** A SELECT projection that uses a row-independent `Contains`
  (`Label = ids.Contains(absent.Id) ? "in" : "out"`) gets C#'s value on every row, on all four providers; one whose
  item reads the row through another member (`Label = ids.Contains(x.Child.Id) ? …`) throws `NotSupportedException`
  before any command is sent. A bare `bool` projection (`Flag = ids.Contains(absent.Id)`) gets C#'s value on
  PostgreSQL, MySQL and SQLite; on SQL Server it stays a syntax error, as any predicate in a value position is (§8).

---

## 4. Test plan (written and reviewed first — Task 0)

### 4.1 Harnesses

Three test files carry the work. Each shared file lives in SqlServer.Tests and is linked (`<Compile Include Link>`)
into PostgreSql.Tests, MySql.Tests and Sqlite.Tests, with one small derived `[TestClass]` per provider **in that
provider's own test project**. The SQLite classes must live in Sqlite.Tests: it is the only project compiled as
C# 14, where arrays bind to `MemoryExtensions.Contains` (§1.2).

1. **`RowIndependentEvaluatorTests`** (SqlServer.Tests `Linq/`; also linked into
   `Funcular.Data.Orm.SqlServer.Tests.NetFramework`, net48, as `QueryOperatorPolicyTests` is). DB-free unit tests of
   the Core helper. The file compiles as C# 7.3 (no nullable annotations, `is not`, switch expressions or
   target-typed `new`). Tests that build `MemoryExtensions` calls are under `#if NET`.
2. **`TranslatorDirectHarness`** (SqlServer.Tests `CapturedValues/`). DB-free: each test builds a lambda over a test
   entity, takes its body's call, and passes it to the provider's `TranslateMethodCall` (or `TranslateDateMember`)
   with a fresh parameter generator and `getColumnName = p => "c." + p.Name`. It asserts the SQL text, the parameter
   values, or the exception type, inner type and message prefix. The derived class supplies the translator call and
   the dialect's patterns (`LIKE` concatenation, date parts). It covers the branches the integration rows can't reach.
3. **`CapturedValueHarness`** (SqlServer.Tests `CapturedValues/`). Integration rows against each provider's test
   database.
   - **Table** `zz_cv_row` (`id` PK, `code`, `name`, `nullable_id`), rows (1, 'A', 'alpha', 1), (2, 'b', 'beta',
     NULL), (3, 'c', 'gamma', 3). Each test drops it if it exists, creates and seeds it, and drops it in `finally`
     (SQLite: a temp file per test, deleted). Suites run serially (no `Parallelize` in any test assembly).
   - **Query rows** run four paths (`query`, `queryAsync`, `linq`, `count` = `Query<T>().Count(predicate)`) and compare
     ids (or the count) with the DataRow's expected ids. Each row also asserts that LINQ to objects over the same
     rows gives those ids, so a typo in a DataRow fails; a row whose expectation is an owner decision rather than C#
     (D8's "no row" where C# throws) says so in a `knownDivergence` argument and skips only that assertion.
   - **Delete rows** run `sync` and `async`, each in a transaction rolled back after the remaining ids are read
     through the provider, and compare the deleted ids.
   - **Rejection rows** (a `NotSupportedException` expected, with an inner type where AC8 names one) assert the
     exception, that no command was logged after the call began, and that all three rows remain. The log is captured
     from after the MySQL warm-up below.
   - **SQL rows** assert the logged WHERE: the literal D4 sends, no ` IN (`/` LIKE ` for the evaluated call, and the
     parameter count.
   - **Projection rows** (AC13) run `Query<T>().Select(x => new CvRow { Id = x.Id, Label = … })` and compare
     `Id:Label` pairs; the bare `bool` row runs where the provider's `SupportsPredicateAsValue` is true.
   - **Binding assertion.** Each array row asserts the declaring type of its `Contains` call: `MemoryExtensions` in
     the SQLite class, `Enumerable` in the others (a virtual on the derived class). If Sqlite.Tests ever stops
     compiling as C# 14, the SQLite rows fail rather than silently testing the C# 12 shape.
   - Before a delete, the MySQL class runs one query on the type: `development/3.10`'s MySQL delete fails on a cold
     column cache (fixed on `fix/provider-scoped-caches`).
   - **Delete shapes before Task 6** are those whose outcome is the same under both guards (D11): every `OR` here is
     false after evaluation, every `AND` passes both guards' checks (`(t.id = @p AND NOT 1=0)` matches none of the
     old patterns or regexes), and the shapes the old guard rejects are rejected by the new one with the same message.

Test entity for the integration rows (`zz_cv_row`): `CvRow { int Id; string Code; string Name; int? NullableId;
[NotMapped] CvInner Child; [NotMapped] string Label; [NotMapped] bool Flag }`, `CvInner { int Id; string Name }`. The
direct harness's entity adds a nullable date (`DateTime? Birth`), a `[NotMapped] List<int> Tags`, a
`[NotMapped] CvRange Window` (a non-enumerable type with `bool Contains(int)`) and a public field.

Captured values, shared: `ids = {1, 2}`, `arr = new[] {1, 2}`, `absent` (Id 5, Code "a", Name "zeta", NullableId 5,
Inner.Id 5, Ids {1, 2}), `present` (Id 1), `nullHolder` (NullableId null), `nullRequest = null`,
`nullIdsHolder` (Ids null), `local = 5`, `codes = {"A"}`, `ci` (`HashSet`, OrdinalIgnoreCase, `{"A"}`),
`nulls = {null}`, `lengths = {4}`, `longs = {1L, 2L}`, `emptyIds = {}`, `s = "zeta"`, `nullFilter = null`,
`needle` (Name "mm"), `bee` (Code "b"), `StaticHolder` (`Id = 5`, `Name = "alpha"`), and an instance `Holder`
(`Filter = "zz"`, `Code = "b"`, `NullFilter = null`) whose instance methods return the predicates that read them (a
captured `this`).

### 4.2 AC → test matrix

Integration rows are `CapturedValueHarness` methods, run on all four providers (§4.6). "Q" = `query`, `queryAsync`,
`linq`, `count`; "D" = `sync`, `async`. "Red at `35c6477`": **red** = fails on the baseline (probe row in brackets);
**luck** = passes on the baseline by coincidence, with the row that is red for it.

| AC | Test | Key → expected | Red at `35c6477` |
|---|---|---|---|
| AC1 | `Query_ReturnsWhatCSharpSelects` (Q) | `orAbsent` {2} | red (S01) |
| | | `orPresent` {1,2,3}; `aloneAbsent` {}; `orNotAbsent` {1,2,3}; `andAbsent` {}; `andNotAbsent` {2} | red (S02, S05, S06, S04; `AND NOT t.id IN (1,2)` gives {}) |
| | | `andPresent` {2}; `andNotPresent` {} | luck — red: `Sql_…(andPresent)`, `Sql_…(andNotPresent)` |
| | | `orLocal`, `orConstant`, `orNested`, `orStatic`, `orValue`, `orCollectionProperty` (`absent.Ids.Contains(5)`), `orBothCaptured` (`absent.Ids.Contains(absent.Id)`) {2} | red (S08, S09, S13, S14, S15, S16, S01's mechanism) |
| AC1 | `Delete_DeletesWhatCSharpSelects` (D) | `orAbsent` {2}; `andAbsent` {}; `andNotAbsent` {2}; `orLocal`, `orConstant`, `orNested`, `orStatic`, `orValue`, `orCollectionProperty`, `orBothCaptured` {2} | red |
| | | `andPresent` {2}; `andNotPresent` {} | luck — red: the `Sql_…` rows |
| AC1, AC9 | `Delete_RowIndependentAlone_IsRejected` (D) | `aloneAbsent`, `aloneNotAbsent`: the column-reference message, nothing deleted | red (S05 deletes {1,2}) |
| AC2 | `Query_ReturnsWhatCSharpSelects` (Q) | `orCodesCase` {2}; `orNullItem` {1,2,3}; `orComparerSet` {1,2,3}; `andComparerSet` {2}; `orArrayAbsent` {2}; `orCapturedReceiverCase` (`absent.Name.Contains("ZETA")`) {2}; `orLocalReceiverCase` (`s.Contains("ZETA")`) {2} | red (S10, S11, S12; `t.code IN ('A')` gives {}; S07; S25 throws; C1 on SQL Server, MySQL, SQLite) |
| | | `andArrayPresent` {2}; `orLocalReceiverCase` on PostgreSQL | luck — red: `Sql_…(andArrayPresent)`, `Sql_…(orLocalReceiverCase)` |
| AC2 | `Delete_DeletesWhatCSharpSelects` (D) | `orCodesCase` {2}; `andNullItem` {2}; `andComparerSet` {2}; `orArrayAbsent` {2}; `orCapturedReceiverCase` {2}; `orLocalReceiverCase` {2} | red (C1 deletes {1,2,3}) |
| AC2 | `RowIndependentEvaluatorTests` | `TryTranslateContains_UsesTheCollectionsComparer`, `_EnumerableContainsWithComparer`, `_OrdinalStringEquality`, `_NullItem`, `_UserCollectionContains`, `_SpanBound_Evaluates`, `_StringReceiver_HonorsStringComparison` (`#if NET`), `RewriteForEvaluation_SpanBound_InterpretsWithoutSpan` | new |
| AC3, AC8 | `Sql_RowIndependentContains_IsALiteral` (`query`) | `orAbsent` → `1=0`; `andPresent` → `NOT 1=0`; `andNotPresent` → `NOT NOT 1=0`; `andArrayPresent` → `NOT 1=0`; `orLocalReceiverCase` → `1=0`, one parameter (`x.Id == 2`'s) each; `optionalFilterNull` → `t.name LIKE NULL`; `optionalReceiverNull` → `NULL LIKE NULL` (one parameter: the `IS NULL` test's) | red |
| AC3 | `TranslatorDirectHarness` | `CollectionContains_RowIndependentItem_FalseSql`, `_TrueSql` (no parameter added); `CollectionContains_RowIndependent_ReadsCapturedValuesOnce`; `StringContains_RowIndependent_FalseSql`, `_TrueSql` | new |
| AC4 | `Rejected_BeforeAnyCommand` (Q, D) | `nestedParamItem` (`ids.Contains(x.Child.Id)`); `lengthItem`; `convertItem` (`longs.Contains(x.Id)`); `emptyNestedParamItem` (`emptyIds.Contains(x.Child.Id)`); `nullCollection` (`nullIdsHolder.Ids.Contains(x.Id)`, D5) | red (S28 wrong rows; S21 server error; S22 NRE; `1=0` returns {}; T6 NRE) |
| AC4 | `TranslatorDirectHarness` | `CollectionContains_NestedParamItem_NotSupported`, `_NestedParamItemEmptyCollection_NotSupported`, `_ConvertedItem_NotSupported`, `_NestedParamItem_CollectionNotRead` (a counting getter stays at 0), `_ValueUnwrap_NullableExpressionException`, `_ValueUnwrapEmptyCollection_NullableExpressionException`, `_MemberItem_In`, `_FieldItem_InvalidOperation`, `_CollectionReadsRow_ParameterReferences` (`x.Tags.Contains(5)`, message as today) | new |
| AC5 | `Rejected_BeforeAnyCommand` (Q, D) | `startsWithCapturedReceiver` (S23); `endsWithCapturedReceiver` (S24); `startsWithLocalReceiver` (S26); `startsWithStaticReceiver` (`StaticHolder.Name.StartsWith("al")`); `startsWithNestedParamReceiver` (T5) | red (wrong rows, NRE) |
| AC5 | `TranslatorDirectHarness` | `StartsWith_CapturedReceiver_NotSupported`, `_LocalReceiver_NotSupported`, `_StaticReceiver_NotSupported`, `_NestedParamReceiver_NotSupported`, `_MethodCallReceiver_NotSupported`, `CollectionContains_NullCollection_NotSupported`, `CollectionContains_NonEnumerableReceiver_NotSupported`, `CollectionContains_NonBoolCall_NotSupported` (no `ArgumentException` leaks) | new |
| AC6 | `Query_ReturnsWhatCSharpSelects` (Q) | `containsThisFilter` (U1) {2}; `codeEqualsThisCodeToString` (U3) {2}; `containsNestedCaptured` (`x.Name.Contains(needle.Name)`) {3}; `startsWithThisFilter` (U2) {2}; `codeEqualsNestedToString` (`x.Code == bee.Code.ToString()`) {2}. No collation can change these answers. | red (U1, U3, T3, U2, T1) |
| AC6 | `Delete_DeletesWhatCSharpSelects` (D) | `containsThisFilter` {2}; `startsWithThisFilter` {2} | red |
| AC6 | `TranslatorDirectHarness` | `StringContains_CapturedThisPropertyValue`, `_NestedCapturedValue`, `_StaticValue`, `_CapturedLocalValue`; `StartsWith_CapturedThisPropertyArgument`, `_NestedCapturedArgument`; `ToString_CapturedThisProperty`, `_NestedCaptured`, `_Static` | new |
| AC7 | `Rejected_BeforeAnyCommand` (Q) | `codeEqualsLengthToString` (T2); `childNameContains` (T4) | red (T2 sends `t.code = t.name`; T4 server error) |
| AC7 | `TranslatorDirectHarness` | `ToString_MemberOfMember_NotSupported`, `StringContains_ReceiverMemberOfMember_NotSupported`, `ToString_FieldOfParameter_NotSupported`, `ToString_OtherExpression_NotSupported` | new |
| AC8 | `Query_ReturnsWhatCSharpSelects` (Q) | `optionalFilterNull` (O1) {1,2,3}; `optionalThisFilterNull` (O1 with `this.NullFilter`) {1,2,3}; `optionalStartsWithNull` (O4) {1,2,3}; `optionalReceiverNull` (`nullFilter == null \|\| nullFilter.Contains("a")`) {1,2,3}; `containsNullValue` (`x.Id == 2 \|\| x.Name.Contains(nullFilter)`, knownDivergence D8) {2}; `startsWithNullValue` (knownDivergence D8) {2} | `optionalFilterNull`, `optionalThisFilterNull`, `optionalReceiverNull`: luck — red: `Sql_…(optionalFilterNull)`, `Sql_…(optionalReceiverNull)`; `optionalStartsWithNull` red (O4 throws); `containsNullValue` red (O5's `'%%'`); `startsWithNullValue` red (throws) |
| AC8 | `Delete_DeletesWhatCSharpSelects` (D) | `containsNullValue` {2}; `startsWithNullValue` {2} | red |
| AC8 | `Rejected_BeforeAnyCommand` (Q, D) | `throwingItem` (S17; inner `NullReferenceException`); `throwingCollection` (R1: `nullRequest.Ids.Contains(x.Id)`; inner `NullReferenceException`) | red (S17 wrong rows; R1 `TargetInvocationException`) |
| AC8 | `TranslatorDirectHarness`, `RowIndependentEvaluatorTests` | `StringContains_NullValue_LikeNull` (constant and captured null), `StartsWith_NullArgument_LikeNull`, `EndsWith_NullArgument_LikeNull`, `StringContains_RowIndependentNullReceiver_Unknown`, `_RowIndependentNullValue_Unknown`, `CollectionContains_ThrowingCollection_NotSupportedWithInner`; `TryTranslateContains_Throwing_WrapsInNotSupported`, `TryReadValue_NullIntermediate_InnerIsNullReference`, `TryReadValue_NullNullableValue_InnerIsInvalidOperation`, `TryReadValue_ThrowingGetter_InnerIsTheOriginal` | new |
| AC9 | Task 6 rows in `CapturedValueHarness` | `Delete_AlwaysTrueAfterEvaluation_IsRejected` (D): `orPresent`, `orNotAbsent`, `orNullItem`, `orComparerSet` → the guard's message, nothing deleted; `andPresent` stays in `Delete_DeletesWhatCSharpSelects` | Task 6 |
| AC10 | `Query_ReturnsWhatCSharpSelects` (Q), `Delete_DeletesWhatCSharpSelects` (D) | `idsX` {1,2}; `notIdsX` {3}; `nullableX` {1}; `emptyX` {} (Q only: the guards reject `1=0` alone); `startsWithX` {1}; `containsX` (`x.Name.Contains("mm")`) {3}; `arrayX` {1,2} | green, must stay green |
| AC10 | `TranslatorDirectHarness` | `CollectionContains_EmptyCollection_OneEqualsZero`, `_NullValuesSkipped` (N1's `IN` list), `_ConstantCollection_In`, `_ImplicitUnary_In`, `_ImplicitCall_In`; `StringContains_ConstantValue`; `StartsWith_Column`, `EndsWith_Column`; `ToString_Column`, `_CapturedLocal`, `_Constant`, `_NullConstant`; `DateMember_YearMonthDay`, `_OtherPart_NotSupported`, `_NotAValueChain_NotSupported`; `UnsupportedMethod_NotSupported` | new (pin today's output) |
| AC11 | `RowIndependentEvaluatorTests` | `IsRowIndependent_Null_Throws`, `TryTranslateContains_Null_Throws`, `TryTranslateContains_NotABoolContains_Throws` (`string.IsNullOrEmpty`; a user `int Contains`), `TryTranslateContains_ReadsTheRow_ReturnsFalseWithoutEvaluating`, `TryReadValue_Null_Throws`, `TryReadValue_NotAChain_ReturnsFalse` (a method call, a chain on the parameter, a conversion) | new |
| AC12 | Task 4, checked in the diff by the reviewer | — | — |
| AC13 | `Projection_GetsCSharpsValue` (`query`) | `labelAbsent` (`ids.Contains(absent.Id) ? "in" : "out"`) → 1:out, 2:out, 3:out; `labelX` (`ids.Contains(x.Id) ? …`) → 1:in, 2:in, 3:out; `flagAbsent` (`Flag = ids.Contains(absent.Id)`, where `SupportsPredicateAsValue`) → false ×3 | red (P1, P2); `labelX` green |
| AC13 | `Rejected_BeforeAnyCommand` (`query`) | `labelNestedParam` (`ids.Contains(x.Child.Id) ? …`) | red (wrong labels) |

### 4.3 Interface coverage

| Member | Tests |
|---|---|
| `RowIndependentEvaluator.IsRowIndependent(Expression)` | `IsRowIndependent_ConstantAndCapturedChains_True`, `_ReadsTheParameter_False`, `_NestedLambdaParameter_True`, `_ParameterOutsideItsLambda_False`, `_Null_Throws` |
| `RowIndependentEvaluator.TryTranslateContains(MethodCallExpression, out string)` | the AC2/AC3/AC8/AC11 rows above, plus `_CapturedItemAbsent_FalseSql`, `_CapturedItemPresent_TrueSql`, `_StringReceiverNull_UnknownSql`, `_StringValueNull_UnknownSql` |
| `RowIndependentEvaluator.TryReadValue(Expression, out object)` | `TryReadValue_Constant`, `_NullConstant`, `_ClosureField`, `_CapturedThisProperty`, `_NestedProperty`, `_StaticProperty`, `_StaticField`, `_NotAChain_ReturnsFalse`, `_NullIntermediate_InnerIsNullReference`, `_NullNullableValue_InnerIsInvalidOperation`, `_ThrowingGetter_InnerIsTheOriginal`, `_Null_Throws` |
| `RowIndependentEvaluator.TrueSql`, `FalseSql`, `UnknownSql` (constants) | asserted by value in `_CapturedItemPresent_TrueSql`, `_CapturedItemAbsent_FalseSql`, `_StringReceiverNull_UnknownSql` |
| `RowIndependentEvaluator.RewriteForEvaluation` (internal) | `RewriteForEvaluation_SpanBound_InterpretsWithoutSpan`, `_UserImplicitConversion_NotRewritten`, `_NotSpanBound_Unchanged` |
| Each provider's `TranslateMethodCall` (changed) | `TranslatorDirectHarness` and `CapturedValueHarness` on that provider |
| Each provider's `TranslateDateMember` (unchanged; in a touched file) | `DateMember_*` |

### 4.4 Mutations each key test must kill (run them; record the result in §5)

| # | Mutation | Killed by |
|---|---|---|
| M1 | A translator doesn't call `TryTranslateContains` for a collection (each provider in turn) | `Query_…(orAbsent)`, `Delete_…(orAbsent)` on that provider; `CollectionContains_RowIndependentItem_FalseSql` |
| M2 | `TrueSql = "1=1"` | `Sql_…(andPresent)`; `Delete_…(andPresent)` (the old guard rejects `1=1`) |
| M3 | `TrueSql` and `FalseSql` swapped | `Query_…(orAbsent)`, `Query_…(andPresent)` |
| M4 | Evaluate by `collection.Cast<object>().Contains(item)` instead of the call | `TryTranslateContains_UsesTheCollectionsComparer`, `_UserCollectionContains`; `Query_…(orComparerSet)` |
| M5 | `IsRowIndependent` treats a nested lambda's parameter as free | `IsRowIndependent_NestedLambdaParameter_True` |
| M6 | `IsRowIndependent` returns true for everything | `IsRowIndependent_ReadsTheParameter_False`; `Query_…(idsX)` |
| M7 | `RewriteForEvaluation` returns the call unchanged | `RewriteForEvaluation_SpanBound_InterpretsWithoutSpan`, `TryTranslateContains_SpanBound_Evaluates`; SQLite `Query_…(orArrayAbsent)` |
| M8 | The rewrite ignores the declaring type | `RewriteForEvaluation_UserImplicitConversion_NotRewritten` |
| M9 | No item check (back to falling through) | `Rejected_…(nestedParamItem)`; `CollectionContains_NestedParamItem_NotSupported` |
| M10 | The item check after the collection read or the empty short-circuit | `CollectionContains_NestedParamItemEmptyCollection_NotSupported`, `_NestedParamItem_CollectionNotRead`, `_ValueUnwrapEmptyCollection_…`; `Rejected_…(emptyNestedParamItem)` |
| M11 | No receiver check in `StartsWith`/`EndsWith` | `Rejected_…(startsWithCapturedReceiver)`, `(startsWithNestedParamReceiver)`; `StartsWith_CapturedReceiver_NotSupported` |
| M12 | `TryReadValue` reads fields only | `TryReadValue_CapturedThisProperty`; `Query_…(containsThisFilter)`; `StringContains_CapturedThisPropertyValue` |
| M13 | Evaluation exceptions keep their `TargetInvocationException` wrapper | `TryReadValue_ThrowingGetter_InnerIsTheOriginal`; `CollectionContains_ThrowingCollection_NotSupportedWithInner` |
| M14 | `TranslateExpression` recurses again | `ToString_MemberOfMember_NotSupported`; `Rejected_…(codeEqualsLengthToString)` |
| M15 | A null value sends `'%%'` again | `StringContains_NullValue_LikeNull`; `Query_…(containsNullValue)`, `Delete_…(containsNullValue)` |
| M16 | Evaluation exceptions not wrapped | `TryTranslateContains_Throwing_WrapsInNotSupported`; `Rejected_…(throwingItem)` |
| M17 | D4's evaluation after the collection is read | `CollectionContains_RowIndependent_ReadsCapturedValuesOnce` |
| M18 | A null value throws (rev 1's D8) | `Query_…(optionalFilterNull)`, `(optionalThisFilterNull)` |
| M19 | The collection read isn't wrapped (R1) | `Rejected_…(throwingCollection)`; `CollectionContains_ThrowingCollection_NotSupportedWithInner` |
| M20 | A row-independent string `Contains` is sent as parameters (`@p LIKE @p`) | `Query_…(orCapturedReceiverCase)`, `(orLocalReceiverCase)` on SQL Server, MySQL and SQLite; `Sql_…(orLocalReceiverCase)` on all four |
| M21 | The receiver check is "root isn't a `ConstantExpression`" | `Rejected_…(startsWithStaticReceiver)`; `StartsWith_StaticReceiver_NotSupported` |
| M22 | A null receiver of a row-independent string `Contains` throws | `Query_…(optionalReceiverNull)`; `StringContains_RowIndependentNullReceiver_Unknown` |
| M23 | The string-receiver evaluation drops the `StringComparison` argument | `TryTranslateContains_StringReceiver_HonorsStringComparison` |
| M24 | Sqlite.Tests compiled as C# 12 (`LangVersion` removed) | the SQLite binding assertion in every array row |

M1, M9, M11, M14, M15 and M20 are run on each provider; the others on SQL Server, with M2, M3 and M7 also on SQLite
(its C# 14 binding).

### 4.5 Coverage

Touched files must reach **≥ 85 %** line coverage (cobertura `line-rate`, coverlet already referenced by all four
test projects): the four `*ExpressionTranslator.cs` (baseline §1.2: 63.6 / 58.5 / 49.1 / 36.8 %) and Core's
`RowIndependentEvaluator.cs` (new; measured from SqlServer.Tests). Each translator is measured from its own
provider's test project, with that suite's database available. Per-file numbers go into §5 and the PR.

### 4.6 Opt-in suites and frameworks

- **SQL Server:** `FUNKY_CONNECTION`. **MySQL:** `FUNKY_MYSQL_CONNECTION`. **PostgreSQL:** the suite's fallback
  (`postgres_user`/`postgres_pwd` to the local service). **SQLite:** file-backed. All four run locally for every task
  and for coverage; database runs are serial.
- **CI** runs only SqlServer.Tests (`ci.yml`, SDK 8.0.x) and the PostgreSQL and MySQL workflows; Sqlite.Tests and
  net48 aren't in CI, so their local runs are the gate, with results in the PR.
- **net48:** `RowIndependentEvaluatorTests` runs in `Funcular.Data.Orm.SqlServer.Tests.NetFramework` (Core's
  netstandard2.0 build on .NET Framework), without the `#if NET` span tests.

---

## 5. Design

### 5.1 Core: `Funcular.Data.Orm.Linq.RowIndependentEvaluator` (public static class)

- `public const string TrueSql = "NOT 1=0";`, `public const string FalseSql = "1=0";`,
  `public const string UnknownSql = "NULL LIKE NULL";`
- `public static bool IsRowIndependent(Expression expression)` — true when the expression reads no
  `ParameterExpression` it doesn't declare itself (a nested lambda's own parameters are declared). An
  `ExpressionVisitor` collects lambda parameters as it enters each lambda and flags any other parameter.
- `public static bool TryTranslateContains(MethodCallExpression call, out string sql)` — `call` must be a
  `bool`-returning method named `Contains` (`ArgumentException` otherwise). When `IsRowIndependent(call)`:
  - a call on a `string` receiver: the receiver and each argument are evaluated; a null receiver or a null `string`
    argument gives `UnknownSql`; otherwise the method is invoked with them;
  - any other call: `RewriteForEvaluation(call)` is evaluated;
  - the result gives `TrueSql` or `FalseSql`. Evaluation is by `Compile(preferInterpretation: true)`; failures are
    wrapped per D9.

  Otherwise it returns `false` with `sql = ""`, having evaluated nothing.
- `public static bool TryReadValue(Expression expression, out object value)` — a `ConstantExpression`, or a chain of
  `MemberExpression`s over fields and properties ending at a `ConstantExpression` or a static member, is evaluated
  by interpretation, so a null link throws what C# throws (`NullReferenceException`; `InvalidOperationException` for
  `Nullable<T>.Value`). Anything else returns `false`. Failures are wrapped per D9.
- `internal static Expression RewriteForEvaluation(MethodCallExpression call)` — when `call.Method.DeclaringType` is
  `System.MemoryExtensions` (compared by full name: netstandard2.0 has no `MemoryExtensions` without System.Memory),
  the call is static with two arguments, and the first is an `op_Implicit` conversion (a `MethodCallExpression` or a
  `UnaryExpression`, the two forms the translators already unwrap) of an `IEnumerable<T>`, returns
  `Enumerable.Contains<T>(source, item)`; otherwise returns `call`. Same answer (both use
  `EqualityComparer<T>.Default`), and it can be interpreted.

### 5.2 Each translator (four copies, identical apart from dialect)

1. **`ToString`** — unchanged; its receiver goes through `TranslateExpression` (item 5).
2. **String `Contains`** — first `TryTranslateContains` (D4, D7): a call that doesn't read the row is sent as its
   literal. Otherwise the receiver goes through `TranslateExpression`, and the value through `TryReadValue` (D7),
   else `NotSupportedException` ("Unsupported expression in string Contains: …"). A null value sends
   `<receiver> LIKE NULL` (D8); otherwise `'%' + value + '%'` as one parameter, as today.
3. **Collection `Contains`** — after the collection expression is found (`:90-115`, unchanged), in D5's order:
   `TryTranslateContains` for a `bool` call; the `.Value` check; the item check; the collection read (a compile
   failure keeps "parameter references"; an invocation failure is wrapped per D9); a null collection; the empty
   short-circuit and the `IN` list as today.
4. **`StartsWith`/`EndsWith`** — `"Contains"` leaves the method-name check and its pattern branch is deleted (D6). The
   receiver must be a `MemberExpression` whose expression is a `ParameterExpression`, else
   `NotSupportedException`. The argument: `TryReadValue` (D7), else today's "Unsupported …" message; a null value
   sends `<column> LIKE NULL` (D8).
5. **`TranslateExpression`**, member case — a member of the lambda parameter that is a property is its column; one
   that isn't a property throws (today it appends nothing). Otherwise `TryReadValue` gives a parameter (D7).
   Anything else throws `NotSupportedException` naming the member (AC7) instead of recursing.

---

## 6. Tasks

- **Task 0 — Test-plan review** (non-author): this plan's ACs, matrix, mutations and §1 claims, before any test or
  code. Rounds until CLEAN; dispositions in §9.
- **Task 1 — Core helper, red → green.** `RowIndependentEvaluator` as a stub (members throw
  `NotImplementedException`) and `RowIndependentEvaluatorTests`; record the red run; implement; green on net8 and
  net48. Mutations M4–M8, M12, M13, M16, M23 (helper side).
- **Task 2 — Translator harnesses, red.** `TranslatorDirectHarness` and `CapturedValueHarness`, their four derived
  classes in their own projects, and the csproj links. Record the red run on all four providers against §4.2's
  "red at `35c6477`" column: every red row fails, every luck and AC10 row passes.
- **Task 3 — Translators, green.** §5.2 in all four providers. All four suites green, net48 included; coverage
  ≥ 85 % per touched file (§4.5); mutations M1–M24 executed and recorded.
- **Task 4 — Documents.** Changelog `[3.10.0-beta1]`: Fixed (I1-6 in WHERE, counts and projections; captured
  properties; the receivers and the dropped member; a row-independent string `Contains` answered as C# answers it;
  `x.Name.Contains(null)` no longer matches every row), Changed (the shapes that now throw `NotSupportedException`;
  a null search value matches no row, and `StartsWith`/`EndsWith` with one no longer throw), New public API
  (`RowIndependentEvaluator`). `Usage.md`: the `IN` clause (a value that isn't a column is answered before the
  query) and the `LIKE` clause (what a search value can be, a null one, receivers must be entity properties).
- **Task 5 — Hostile review** of `35c6477..HEAD` by a non-author, then fix-verification of every fix layer until
  CLEAN. The push sentinel is written only after a CLEAN pass over the exact sha.
- **Task 6 — The delete guard (D11).** When `development/3.10` holds the guard rewrite: merge it in; add the AC9
  rows; update the guard harness's tripwire comment and `DELETE_GUARD_PLAN.md` §6 (I1-6 fixed; the string `Contains`
  collation bullet fixed for `Contains`; `StartsWith`/`EndsWith` now throw `NotSupportedException`); run all four
  suites; fix-verification of the merge. Only then may the branch merge into `development/3.10`.

---

## 7. Risks

- **Expected conflicts** with `fix/delete-guard-trivial-predicates`, `fix/provider-scoped-caches` and the date-part
  branch: the `<ItemGroup>` link blocks in the MySql.Tests, PostgreSql.Tests, Sqlite.Tests, SqlServer.Tests and
  NetFramework csproj files; Changelog `[3.10.0-beta1]`; `Usage.md`; and (date part) the four translators'
  `TranslateDateMember`/visitor lines. Whichever lands second resolves them and re-runs both harnesses.
- **A double read with the delete guard.** The rewritten guard reads captured properties for its own classification
  (its §6), and evaluates string `Contains`; D4 then evaluates it again in the translator. Both answer as C# does, so
  they agree unless a getter changes between the reads (the guard's §6 records the double read).
- **Evaluation cost.** A row-independent `Contains` and each captured value are interpreted once per translation; no
  code is generated.
- **.NET 10's `MemoryExtensions.Contains(span, value, comparer)`** isn't rewritten; interpreted, it reports "Could
  not evaluate expression". The test projects target net8.0.

---

## 8. Out of scope (recorded, not fixed here)

- **LIKE metacharacters in a search value aren't escaped** (N2): `x.Name.Contains("%")` sends `'%%%'`, and a delete
  with it removes every row (SQL Server and SQLite, executed). `_` and SQL Server's `[` behave alike. A separate
  data-loss defect: the owner placed its fix in 3.10.0 on its own branch and plan (decision 2026-10-02).
- **The visitor drops a member of a member of the parameter** (V1: `x.Name.Length != 4` sends `t.name != @p0`; SQLite
  deletes every row). The date-part plan owns it (D13): it rewrites the same `VisitMember` lines.
- **A collection holding null loses its nulls** (N1): `{1, null}.Contains(x.NullableId)` sends `IN (@p0)`, so rows
  whose column is null are missed (C# includes them). Fewer rows, not more; flagged to the owner.
- **A row-independent comparison is compared by the server**, with its collation (`x.Id == 2 || s == "s"`,
  `x.Code == absent.Code.ToString()`), as `DELETE_GUARD_PLAN.md` §6 records. D7 moves only `Contains` into C#.
- **A guarded evaluation still throws** (D9): `req == null || ids.Contains(req.Id)` with `req` null, like the
  visitor's `x.Id == filterId.Value`; the translator evaluates both sides of `||`. Comparing a captured object with
  null (`req == null`) also sends the object as a parameter when it isn't null, which no provider accepts.
- **The delete guard's §6 literal shapes** (a comparison of a comparison, a `CASE` branch that is `NOT 1=0`) were
  reachable only through an empty collection; D4 makes them reachable through any row-independent `Contains`
  (`capturedTrue == (x.Id == 2 || ids.Contains(present.Id))`). The rewritten guard's §6 records them.
- **The visitor's D9 sibling:** `WhereClauseVisitor.cs:277-280` drops the inner exception, and a closure property read
  (`:237`) lets a `TargetInvocationException` escape.
- **Evaluating row-independent `StartsWith`/`EndsWith`/`IsNullOrEmpty`** — D3 chose failing closed; a possible
  follow-up, which the delete guard's tripwire would then need to see.
- **A converted member as a `Contains` item** (`longs.Contains(x.Id)`, `{1}.Contains((int?)x.Id)`) now throws
  `NotSupportedException` (was `NullReferenceException`); supporting it is a feature.
- **A null receiver of `ToString()`** is sent as NULL rather than throwing as C# would.
- **A `[NotMapped]` member of the parameter** as an item or receiver is still sent as a column (a server error).
- **`string.Contains(string, StringComparison)` with a column receiver** ignores the comparison; **`Enumerable.Contains(list,
  x.Code, comparer)`** ignores the comparer (the server's `IN` decides).
- **A non-enumerable `Contains` on a member of the parameter** (`x.Window.Contains(5)`) fails with the "parameter
  references" message, as today.
- **A `bool` projection of a predicate on SQL Server** (`Flag = ids.Contains(x.Id)`, or any comparison) is a syntax
  error there, as today.
- **Date parts on non-nullable members** — the date-part plan.

---

## 9. Review dispositions

### 9.1 Task 0 review of rev 1 (`8e2a392`; non-author; probe rows re-run on all four providers × five paths, both guards' checks executed on the literals, `NOT NOT 1=0` executed on all four servers)

Verdict: NOT CLEAN, 6 blocking. Blame: AC-GAP 3, TEST-GAP 5, PLAN-GAP 7.

| # | Sev | Blame | Finding | Disposition (rev 2) |
|---|---|---|---|---|
| T0-1 | high | AC-GAP | D7 turned a row-independent string `Contains` receiver (S25, which throws today) into `@p LIKE …`, compared by collation: `x.Id == 2 \|\| s.Contains("ZETA")` deletes every row on SQL Server, MySQL and SQLite (C1), and the rewritten guard accepts the property shape. | Owner: evaluate in C# (D4, D7). Rows `orCapturedReceiverCase`, `orLocalReceiverCase` (Q, D, SQL); M20; §8 bullet rewritten. |
| T0-2 | medium | AC-GAP | D8 (throw) broke the guarded idiom `filter == null \|\| x.Name.Contains(filter)`, which works today (O1). | Owner: a null value matches no row (D8 rewritten, `LIKE NULL`), also for `StartsWith`/`EndsWith` (O4) and a null receiver (D7). Rows `optionalFilterNull`, `optionalThisFilterNull`, `optionalStartsWithNull`, `optionalReceiverNull`, `containsNullValue`, `startsWithNullValue`; M18, M22. |
| T0-3 | medium | TEST-GAP | The collection read lets `TargetInvocationException` escape (R1); no row covered it. | D5/D9 wrap it; `throwingCollection` and `CollectionContains_ThrowingCollection_NotSupportedWithInner`; M19. |
| T0-4 | medium | AC-GAP | SELECT projections share `TranslateMethodCall`; I1-6 exists there (P1, P2) and the fix changes them, untested. | Owner: in scope (D12, AC13); `Projection_GetsCSharpsValue`, `labelNestedParam`; §8 for SQL Server's bool projection. |
| T0-5 | medium | PLAN-GAP | The visitor drops members too (V1; SQLite deletes every row); §1.2's "only the translator" was false. | Owner: the date-part plan owns it (D13); §1.2 corrected; §8. |
| T0-6 | medium | TEST-GAP | C# 14 coverage depended on where the SQLite class compiles (the delete guard's sits in SqlServer.Tests, C# 12); "SQLite in CI" was false. | §4.1: SQLite classes in Sqlite.Tests; the binding assertion; M24; CI facts in §1.2 and §4.6. |
| T0-7 | low | TEST-GAP | `TryTranslateContains` compiled, never interpreted, so skipping the rewrite survived. | D10: evaluation interprets (net48 and net8 executed, §1.2); M7 now also killed by `_SpanBound_Evaluates` and SQLite `orArrayAbsent`. |
| T0-8 | low | PLAN-GAP | The `Contains` pattern is reached today (S16). | §1.2 corrected; D6 holds after D4 and D5. |
| T0-9 | low | PLAN-GAP | `oneAndNullX` would fail its own LINQ-to-objects check. | Removed; N1's `IN` list is pinned by `_NullValuesSkipped`; `knownDivergence` defined for D8's rows. |
| T0-10 | low | PLAN-GAP | No "red at baseline" marking; several rows pass by luck. | §4.2's "Red at `35c6477`" column, with the red row for each luck row. |
| T0-11 | low | TEST-GAP | `Count`/`Any`/`All` build their own visitor (K1). | The `count` path on every Q row; AC1 names it. |
| T0-12 | low | PLAN-GAP | D5's order ran user code before rejecting an item; a null collection skipped the item check; a non-`bool` call could leak `ArgumentException`. | D5's order rewritten; `_NestedParamItem_CollectionNotRead`, `CollectionContains_NullCollection_NotSupported`, `_NonBoolCall_NotSupported`. |
| T0-13 | low | TEST-GAP | Reflection reads throw `TargetException`, not what C# throws. | `TryReadValue` interprets; tests assert inner types, including `Nullable<T>.Value` on null. |
| T0-14 | low | PLAN-GAP | Nothing enforced D11's order; D4 reaches the guard's §6 literal shapes through any `Contains`. | D11 is a merge gate; §8 bullet. |
| T0-15 | nit | PLAN-GAP | (a) expected conflicts; (b) C# 7.3 for the net48 file; (c) no static-receiver row; (d) logs after the MySQL warm-up; (e) the visitor's D9 sibling. | (a) §7; (b) §4.1; (c) `startsWithStaticReceiver`, M21; (d) §4.1; (e) §8. |
