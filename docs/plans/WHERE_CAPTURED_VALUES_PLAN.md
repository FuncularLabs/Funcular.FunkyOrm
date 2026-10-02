# WHERE method calls: captured items, receivers and values — Implementation Plan

> **Goal:** make the WHERE translator's method calls read captured values the way C# does and never mistake one for a
> column, on all four providers, for queries and deletes, sync and async.
> - Ships in **3.10.0, before the beta PR** (owner decision 2026-10-02).
> - Branch `fix/where-captured-values`, cut from `development/3.10` at `35c6477`. It merges into `development/3.10`
>   after `fix/delete-guard-trivial-predicates` (D11).
> - Started from the delete-guard review's finding I1-6 (`DELETE_GUARD_PLAN.md` §6, rev 9). The probe (§1) found a
>   second defect and three siblings; the owner folded them in (D2, D3).

> **Status (2026-10-02):** rev 1, written test-first. Task 0 (the test-plan review) is next. D8 waits for the owner.

---

## 1. Verified premises

### 1.1 The probe (executed 2026-10-02 against `35c6477`)

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
`this`).

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
| S16 | `x.Id == 2 \|\| absent.Ids.Contains(5)` | {2} | server error (`t.ids LIKE …`) | |
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

So, on every provider and path:
- **I1-6** (S01–S17, S28): a collection `Contains` whose item is a member is translated as the entity's column of
  that member's name, whatever the member's root. Deletes remove rows C# doesn't select (S01, S02, S05, S06, S10,
  S12–S14, S17, S28) or keep rows it does (S04, S11).
- **Siblings, same root cause** (S23, S24, T5): `StartsWith`/`EndsWith` translate any member receiver as the entity's
  column. T2 drops a member.
- **A second root cause** (U1, U3): a captured value reached through a property, not a field, is read as null.
  U1's delete removes every row whose `Name` isn't null.
- N1 and N2 are separate defects, out of scope (§8).

### 1.2 Code facts the design relies on

- **The item (I1-6).** `SqlExpressionTranslator.cs:163-170` (PostgreSQL and MySQL `:144`, SQLite `:147`): any
  `MemberExpression` item whose member is a property goes to `getColumnName`; nothing checks that the member's
  expression is the lambda parameter. A closure field fails at `:166-168`. A constant item leaves the branch and
  reaches `:189`.
- **The receivers.** `:189-194` (PostgreSQL and MySQL `:170-172`, SQLite `:173-175`): `node.Object as
  MemberExpression` goes to `getColumnName` with no root check. A closure field passes `null` (S09, S26).
- **The `Contains` pattern in that block is unreachable.** A collection `Contains` with a member-of-the-parameter
  receiver always throws earlier, when its collection is compiled (`:125-132`). A static call has no receiver. So
  `:230-233` (SQLite's `else // Contains`) never runs.
- **Captured values read as fields only.** The string `Contains` value (`:70-72`), the `StartsWith`/`EndsWith`
  argument (`:204-206`) and `TranslateExpression` (`:313-319`) read `(Member as FieldInfo)?.GetValue(...)`, so a
  property reads null. `TranslateExpression` recurses into any other member's inner expression (`:320-323`), dropping
  the outer member (T2), or sending the captured object itself as a parameter (T1, S25).
- **The visitor reads correctly.** `WhereClauseVisitor.cs:228-257` reads fields and properties, and `:269-286`
  evaluates nested chains, so `x.Code == absent.Code` works. Only the translator's paths are wrong. The other three
  providers' visitors are copies.
- **No WHERE text is cached.** `GenerateWhereClause` runs a new visitor on every call
  (`SqlServerOrmDataProvider.cs:1424-1445`; the LINQ provider calls it per `Where`/terminal, `SqlLinqQueryProvider.cs:276,
  319`). A literal computed from captured values can't go stale.
- **Delete validation on `35c6477`.** Each provider rejects a WHERE containing `1=1`, `1<2`, `1>0` or `true` (spaces
  removed); SQL Server's self-reference regex is anchored (`SqlServerOrmDataProvider.cs:851`); PostgreSQL, MySQL and
  SQLite use the unanchored `\b(\w+)\s*=\s*\1\b` (`PostgreSqlOrmDataProvider.cs:892`, `MySqlOrmDataProvider.cs:870`,
  `SqliteOrmDataProvider.cs:818`), which matches `1=1` and `0=0`; then the column check.
- **The delete-guard rewrite** (`fix/delete-guard-trivial-predicates`, rev 9). Its D2 classifies a collection
  `Contains` call as Unknown. Its D3 rule 1 rejects `1=1` anywhere, and rule 2 folds runs of `<number> <op> <number>`
  under `NOT`/`AND`/`OR`: `(t.id = @p OR NOT 1=0)` is rejected and `(t.parent_id = @p AND NOT 1=0)` accepted (its
  examples). It keeps the regexes (its D4). Its tripwire `UnevaluatedParameterFreeParts_AreNotTranslated` asserts
  only that the delete throws, logs no DELETE and keeps every row; its doc comment names a `NullReferenceException`
  for `StartsWith`/`EndsWith`. No test there pins I1-6's behaviour.
- **C# 14's array `Contains`** is `MemoryExtensions.Contains(ReadOnlySpan<T>.op_Implicit(arr), item)`, with the
  conversion as a `MethodCallExpression`. `Compile()` evaluates it; `Compile(preferInterpretation: true)` throws
  `ArgumentException` (a span can't be an interpreter result). Executed on net8.0, 2026-10-02.
- **Language versions.** SqlServer.Tests, PostgreSql.Tests and MySql.Tests use net8.0's default (C# 12): arrays bind
  to `Enumerable.Contains`. Sqlite.Tests and Core set `LangVersion` latest (C# 14 with the 10.0.301 SDK): arrays
  bind to `MemoryExtensions.Contains`.
- **Visibility.** The translators, their parameter generators and `TranslateMethodCall`/`TranslateDateMember` are
  public. Core grants `InternalsVisibleTo` to the SQL Server test assemblies (`AssemblyInfo.cs`). Core's public
  query-translation helpers (`ScalarProjectionGuard`, `QueryOperatorPolicy`) live in `Funcular.Data.Orm.Linq`.
- **Baseline at `35c6477`.** All tests pass: SQL Server 885, SQLite 788, PostgreSQL 760, MySQL 712. Translator line
  coverage (cobertura): SQL Server 63.6 %, SQLite 58.5 %, PostgreSQL 49.1 %, MySQL 36.8 %.

---

## 2. Decisions

- **D1 (owner, 2026-10-02):** 3.10.0, before the beta; its own branch from `development/3.10`.
- **D2 (owner, 2026-10-02):** the captured-property read (U1, U3) is fixed on this branch.
- **D3 (owner, 2026-10-02):** the siblings (S23, S24, T2, T5) are fixed here, failing closed: a receiver that isn't a
  member of the lambda parameter throws, and isn't evaluated.
- **D4 — A collection `Contains` that doesn't read the row is answered in C# and sent as a constant.** The call is
  evaluated as written (its own `Contains`, its comparer) and sent as `1=0` when false and `NOT 1=0` when true.
  - Why evaluate, not send `@p IN (…)`: the collection's comparer can't be expressed in SQL (S12), an `IN` list drops
    nulls (S11), a server collation differs from C#'s ordinal equality (S10), and a parameter comparison is opaque to
    both delete guards, so an always-true `OR` would delete every row whichever guard runs.
  - Why these literals: `1=0` is today's emission for an empty collection. `NOT 1=0` is what `!emptyIds.Contains(x.Id)`
    already sends, and the rewritten guard folds it by position (rejected in an `OR`, accepted in an `AND`). `1=1` is
    rejected anywhere by the rewritten guard's rule 1 and by today's substring list; `0=0` and `1=1` match the
    PostgreSQL/MySQL/SQLite self-reference regex. Either would reject `x.Id == id && allowed.Contains(user.Role)`.
  - A row-independent call is answered before the collection is evaluated for an `IN` list, so captured values are
    read once.
- **D5 — A collection `Contains` item that reads the row must be a member of the lambda parameter** (`x.Prop`,
  translated as today). Any other such item throws `NotSupportedException` naming it. The item is checked before the
  empty-collection short-circuit, so the outcome doesn't depend on what the collection holds; `.Value` keeps its
  `NullableExpressionException` and is checked first.
- **D6 — `StartsWith`/`EndsWith` receivers** must be a member of the lambda parameter, else `NotSupportedException`.
  The unreachable `Contains` pattern in that block (§1.2) is deleted, so the block handles `StartsWith`/`EndsWith`
  only and every other call reaches the final "not supported".
- **D7 — Captured values.** Wherever the translator needs a value — string `Contains`', `StartsWith`'s and
  `EndsWith`'s argument, and a captured receiver of `ToString()` or of string `Contains` (`TranslateExpression`) — a
  constant, or a chain of field and property reads rooted at a constant or a static member, is read as C# reads it.
  Anything else (a method call, a conversion, a chain that reads the row) is not supported, as today. The comparison
  stays in SQL, as for `x.Code == captured` and for today's captured-local receivers.
- **D8 (proposed; owner to confirm in Task 0) — A null value for string `Contains`** throws `NotSupportedException`
  instead of sending `'%%'`. C# throws `ArgumentNullException` for `Contains(null)`, and `StartsWith`/`EndsWith`
  already reject null. Behaviour change: `x.Name.Contains(filter)` with `filter == null` returns (and deletes) every
  row whose column isn't null today.
- **D9 — Evaluation failures.** A read or a `Contains` that throws (a null captured object, a throwing getter) is
  reported as `NotSupportedException("Could not evaluate expression: <node>")`, the visitor's message
  (`WhereClauseVisitor.cs:279`), with the original exception as `InnerException` (unwrapped from
  `TargetInvocationException`), before any command is sent.
- **D10 — One public Core helper**, `Funcular.Data.Orm.Linq.RowIndependentEvaluator`, which the four translators call
  (precedents: `ScalarProjectionGuard`, `QueryOperatorPolicy`). No `InternalsVisibleTo` is added. §5.1 gives its
  members. The Changelog lists it under "New public API".
- **D11 — Merge order.** This branch merges into `development/3.10` after the delete-guard rewrite. Until then its
  delete rows use only shapes whose outcome is the same under both guards (§4.1). Task 6 merges the guard in, adds the
  AC9 rows, and updates the guard's tripwire comment and its plan's §6 (I1-6 fixed; `StartsWith`/`EndsWith` now throw
  `NotSupportedException`).

---

## 3. Acceptance criteria (3.10.0)

- **AC1 (I1-6).** A collection `Contains` whose item doesn't read the row — a captured object's property, a nested
  one, a local, a constant, a static member, a captured `Nullable<T>.Value`, with a collection that may itself be a
  captured object's property — is answered as C# answers it, on all four providers.
  - `Query<T>(predicate)`, `QueryAsync<T>(predicate)` and `Query<T>().Where(predicate)` return the rows LINQ to
    objects returns over the same rows.
  - `Delete`/`DeleteAsync` delete those rows (AC9 covers predicates that become always true).
  - Its item is never translated as a column.
- **AC2.** The answer is the call as written: the collection's own `Contains` and comparer (a case-insensitive
  `HashSet<string>`, `Enumerable.Contains` with a comparer, a user collection's `Contains`), ordinal string equality,
  a null item, and C# 14's `MemoryExtensions.Contains` over an array, also where expressions are interpreted.
- **AC3.** Such a call is sent as `1=0` when C# finds it false and `NOT 1=0` when true. Neither the item nor the
  collection's values are sent as parameters.
- **AC4.** A collection `Contains` whose item reads the row is translated only when the item is a member of the
  lambda parameter (`x.Prop`, as today). Another item that reads the row (`x.Child.Id`, `x.Name.Length`, `(long)x.Id`)
  throws `NotSupportedException` naming it before any command is sent, whatever the collection holds. `.Value` keeps
  its `NullableExpressionException`, also with an empty collection.
- **AC5.** `StartsWith` and `EndsWith` translate their receiver as a column only when it is a member of the lambda
  parameter. A receiver that doesn't read the row (`request.Name`, a local) or reads it through another member
  (`x.Child.Name`) throws `NotSupportedException` before any command is sent.
- **AC6.** A value given to string `Contains`, `StartsWith` or `EndsWith`, and a captured receiver of `ToString()` or
  of string `Contains`, is read as C# reads it: a property of a captured `this`, a nested property
  (`request.Name`), a static member. `x => x.Id == 2 || x.Name.Contains(Filter)` returns and deletes row 2 only;
  `x => x.Code == Code.ToString()` returns row 2.
- **AC7.** A receiver of `ToString()` or of string `Contains` that reads the row through another member
  (`x.Name.Length`, `x.Child.Name`) throws `NotSupportedException` instead of losing a member.
- **AC8.** Before any command is sent:
  - a null value for string `Contains` throws `NotSupportedException` (D8), as `StartsWith`/`EndsWith` already do;
  - a read or a `Contains` that throws while the predicate is translated (a null captured object) throws
    `NotSupportedException` whose `InnerException` is the original exception (D9). Nothing is deleted.
- **AC9 (Task 6, with the delete-guard rewrite merged).** A delete whose evaluated `Contains` makes the predicate
  always true (`x.Id == 2 || ids.Contains(present.Id)`, `x.Id == 2 || !ids.Contains(absent.Id)`) is rejected with
  "Delete operation requires a non-trivial WHERE clause." and deletes nothing. In an `AND` it deletes what the rest
  selects. Alone (`x => ids.Contains(absent.Id)`) it's rejected with the column-reference message.
- **AC10. Unchanged:** `ids.Contains(x.Id)`, `!ids.Contains(x.Id)`, an `int?` list with an `int?` member, an empty
  collection with a member item (`1=0`), a collection holding nulls with a member item (N1, as today), `x.Name.StartsWith("al")`,
  `x.Name.Contains("a")`, a captured local as a string `Contains` receiver (`@p LIKE …`), and C# 14's
  `arr.Contains(x.Id)` translate and return as before.
- **AC11.** `RowIndependentEvaluator`'s contract (§5.1): a null argument throws `ArgumentNullException`;
  `TryTranslateContains` given a call that isn't a `bool`-returning `Contains` throws `ArgumentException`, and returns
  `false` for a call that reads the row without evaluating anything; `TryReadValue` returns `false` for anything but a
  constant or a rooted chain of field and property reads.
- **AC12.** The Changelog's `[3.10.0-beta1]` section and `Usage.md` describe the new behaviour (Task 4).

---

## 4. Test plan (written and reviewed first — Task 0)

### 4.1 Harnesses

Three test files carry the work. Each shared file lives in SqlServer.Tests and is linked (`<Compile Include Link>`)
into the other three provider test projects, with one small derived `[TestClass]` per provider, as the delete guard's
harness is.

1. **`RowIndependentEvaluatorTests`** (SqlServer.Tests `Linq/`; also linked into
   `Funcular.Data.Orm.SqlServer.Tests.NetFramework`, net48, as `QueryOperatorPolicyTests` is). DB-free unit tests of
   the Core helper. Tests that build `MemoryExtensions` calls are under `#if NET`.
2. **`TranslatorDirectHarness`** (SqlServer.Tests `CapturedValues/`). DB-free: each test builds a lambda over a test
   entity, takes its body's call, and passes it to the provider's `TranslateMethodCall` (or `TranslateDateMember`)
   with a fresh parameter generator and `getColumnName = p => "c." + p.Name`. It asserts the SQL text, the parameter
   values, or the exception type and message prefix. The derived class supplies the translator call and the
   dialect's patterns (`LIKE` concatenation, date parts). It covers the branches the integration rows can't reach.
3. **`CapturedValueHarness`** (SqlServer.Tests `CapturedValues/`). Integration rows against each provider's test
   database.
   - **Table** `zz_cv_row` (`id` PK, `code`, `name`, `nullable_id`), rows (1, 'A', 'alpha', 1), (2, 'b', 'beta',
     NULL), (3, 'c', 'gamma', 3). Each test drops it if it exists, creates and seeds it, and drops it in `finally`
     (SQLite: a temp file per test, deleted). Suites run serially (no `Parallelize` in any test assembly).
   - **Query rows** run three paths (`query`, `queryAsync`, `linq`) and compare ids with the DataRow's expected ids;
     each row also asserts that LINQ to objects over the same rows gives those ids, so a typo in a DataRow fails.
   - **Delete rows** run `sync` and `async`, each in a transaction rolled back after the remaining ids are read
     through the provider, and compare the deleted ids.
   - **Rejection rows** (a `NotSupportedException` expected) assert the exception, that no `SELECT`/`DELETE` was
     logged, and that all three rows remain.
   - **SQL rows** assert the logged WHERE: the literal D4 sends, no ` IN (`, and the parameter count.
   - Before a delete, the MySQL class runs one query on the type: `development/3.10`'s MySQL delete fails on a cold
     column cache (fixed on `fix/provider-scoped-caches`).
   - **Delete shapes before Task 6** are those whose outcome is the same under both guards (D11): every `OR` here is
     false after evaluation, every `AND` passes both guards' checks (`(t.id = @p AND NOT 1=0)` matches none of the
     old patterns or regexes), and the shapes the old guard rejects are rejected by the new one with the same message.

Test entity for the integration rows (`zz_cv_row`): `CvRow { int Id; string Code; string Name; int? NullableId;
[NotMapped] CvInner Child }`, `CvInner { int Id; string Name }`. The direct harness's entity adds a nullable date
(`DateTime? Birth`), a `[NotMapped] List<int> Tags`, a `[NotMapped] CvRange Window` (a non-enumerable type with
`bool Contains(int)`) and a public field.

Captured values, shared: `ids = {1, 2}`, `arr = new[] {1, 2}`, `absent` (Id 5, Code "a", Name "zeta", NullableId 5,
Inner.Id 5, Ids {1, 2}), `present` (Id 1), `nullHolder` (NullableId null), `nullRequest = null`, `local = 5`,
`codes = {"A"}`, `ci` (`HashSet`, OrdinalIgnoreCase, `{"A"}`), `nulls = {null}`, `oneAndNull = {1, null}`,
`lengths = {4}`, `longs = {1L, 2L}`, `emptyIds = {}`, `s = "zeta"`, `needle` (Name "mm"), `bee` (Code "b"), and an
instance `Holder` (`Filter = "zz"`, `Code = "b"`, `NullFilter = null`) whose instance methods return the
predicates that read them (a captured `this`).

### 4.2 AC → test matrix

Integration rows are `CapturedValueHarness` methods, run on all four providers (SQL Server and SQLite in CI;
PostgreSQL and MySQL opt-in, §4.6). "Q" = `query`, `queryAsync`, `linq`; "D" = `sync`, `async`.

| AC | Test | Rows (key → expected) |
|---|---|---|
| AC1 | `Query_ReturnsWhatCSharpSelects` (Q) | `orAbsent` {2}; `orPresent` {1,2,3}; `andPresent` {2}; `andAbsent` {}; `aloneAbsent` {}; `orNotAbsent` {1,2,3}; `andNotPresent` {}; `andNotAbsent` {2}; `orLocal` {2}; `orConstant` {2}; `orNested` {2}; `orStatic` {2}; `orValue` {2}; `orCollectionProperty` (`absent.Ids.Contains(5)`) {2}; `orBothCaptured` (`absent.Ids.Contains(absent.Id)`) {2} |
| AC1 | `Delete_DeletesWhatCSharpSelects` (D) | `orAbsent` {2}; `andPresent` {2}; `andAbsent` {}; `andNotPresent` {}; `andNotAbsent` {2}; `orLocal`, `orConstant`, `orNested`, `orStatic`, `orValue`, `orCollectionProperty`, `orBothCaptured` {2} |
| AC1, AC9 | `Delete_RowIndependentAlone_IsRejected` (D) | `aloneAbsent`, `aloneNotAbsent`: the column-reference message, nothing deleted |
| AC2 | `Query_ReturnsWhatCSharpSelects` (Q) | `orCodesCase` {2}; `orNullItem` {1,2,3}; `orComparerSet` {1,2,3}; `andComparerSet` {2}; `orArrayAbsent` {2}; `andArrayPresent` {2} (on SQLite these bind to `MemoryExtensions`) |
| AC2 | `Delete_DeletesWhatCSharpSelects` (D) | `orCodesCase` {2}; `andNullItem` {2}; `andComparerSet` {2}; `orArrayAbsent` {2}; `andArrayPresent` {2} |
| AC2 | `RowIndependentEvaluatorTests` | `TryTranslateContains_UsesTheCollectionsComparer`, `_EnumerableContainsWithComparer`, `_OrdinalStringEquality`, `_NullItem`, `_UserCollectionContains`, `_SpanBound_Evaluates`, `RewriteForEvaluation_SpanBound_InterpretsWithoutSpan` |
| AC3 | `Sql_RowIndependentContains_IsALiteral` (`query`) | `orAbsent` → `1=0`; `andPresent` → `NOT 1=0`; `andNotPresent` → `NOT NOT 1=0`; one parameter (`x.Id == 2`'s) each |
| AC3 | `TranslatorDirectHarness` | `CollectionContains_RowIndependentItem_FalseSql`, `_TrueSql` (no parameter added); `CollectionContains_RowIndependent_ReadsCapturedValuesOnce` |
| AC4 | `Rejected_BeforeAnyCommand` (Q, D) | `nestedParamItem` (`ids.Contains(x.Child.Id)`); `lengthItem`; `convertItem` (`longs.Contains(x.Id)`); `emptyNestedParamItem` (`emptyIds.Contains(x.Child.Id)`) |
| AC4 | `TranslatorDirectHarness` | `CollectionContains_NestedParamItem_NotSupported`, `_NestedParamItemEmptyCollection_NotSupported`, `_ConvertedItem_NotSupported`, `_ValueUnwrap_NullableExpressionException`, `_ValueUnwrapEmptyCollection_NullableExpressionException`, `_MemberItem_In`, `_FieldItem_InvalidOperation` |
| AC5 | `Rejected_BeforeAnyCommand` (Q, D) | `startsWithCapturedReceiver` (S23); `endsWithCapturedReceiver` (S24); `startsWithLocalReceiver` (S26); `startsWithNestedParamReceiver` (T5) |
| AC5 | `TranslatorDirectHarness` | `StartsWith_CapturedReceiver_NotSupported`, `_LocalReceiver_NotSupported`, `_NestedParamReceiver_NotSupported`, `_MethodCallReceiver_NotSupported`, `CollectionContains_NullCollection_NotSupported`, `CollectionContains_NonEnumerableReceiver_NotSupported` |
| AC6 | `Query_ReturnsWhatCSharpSelects` (Q) | `containsThisFilter` (U1) {2}; `codeEqualsThisCodeToString` (U3) {2}; `containsNestedCaptured` (T3's shape: `x.Name.Contains(needle.Name)`, `"mm"`) {3}; `startsWithThisFilter` (U2) {2}; `orCapturedReceiverContains` (`x.Id == 2 \|\| absent.Name.Contains("lph")`, S25) {2}; `codeEqualsNestedToString` (T1's shape: `x.Code == bee.Code.ToString()`, `"b"`) {2}. Values are chosen so no collation can change the answer. |
| AC6 | `Delete_DeletesWhatCSharpSelects` (D) | `containsThisFilter` {2}; `startsWithThisFilter` {2} |
| AC6 | `TranslatorDirectHarness` | `StringContains_CapturedThisPropertyValue`, `_NestedCapturedValue`, `_StaticValue`, `_CapturedLocalValue`; `StartsWith_CapturedThisPropertyArgument`, `_NestedCapturedArgument`; `ToString_CapturedThisProperty`, `_NestedCaptured`, `_Static`; `StringContains_CapturedThisPropertyReceiver` |
| AC7 | `Rejected_BeforeAnyCommand` (Q) | `codeEqualsLengthToString` (T2); `childNameContains` (T4) |
| AC7 | `TranslatorDirectHarness` | `ToString_MemberOfMember_NotSupported`, `StringContains_ReceiverMemberOfMember_NotSupported`, `ToString_FieldOfParameter_NotSupported`, `ToString_OtherExpression_NotSupported` |
| AC8 | `Rejected_BeforeAnyCommand` (Q, D) | `containsNullFilter` (D8); `throwingItem` (S17: inner `NullReferenceException`) |
| AC8 | `TranslatorDirectHarness`, `RowIndependentEvaluatorTests` | `StringContains_NullValue_NotSupported` (constant and captured null), `StartsWith_NullArgument_NotSupported`; `TryTranslateContains_Throwing_WrapsInNotSupported`, `TryReadValue_NullIntermediate_WrapsInNotSupported`, `TryReadValue_ThrowingGetter_InnerIsTheOriginal` |
| AC9 | Task 6 rows in `CapturedValueHarness` | `Delete_AlwaysTrueAfterEvaluation_IsRejected` (D): `orPresent`, `orNotAbsent`, `orNullItem`, `orComparerSet` → the guard's message, nothing deleted; `Delete_DeletesWhatCSharpSelects` gains `andPresent` under the new guard (already a row) |
| AC10 | `Query_ReturnsWhatCSharpSelects` (Q), `Delete_DeletesWhatCSharpSelects` (D) | `idsX` {1,2}; `notIdsX` {3}; `nullableX` {1}; `emptyX` {} (Q only: the guards reject `1=0` alone); `oneAndNullX` {1} (N1, as today); `startsWithX` {1}; `containsX` (`x.Name.Contains("mm")`) {3}; `arrayX` {1,2} |
| AC10 | `TranslatorDirectHarness` | `CollectionContains_EmptyCollection_OneEqualsZero`, `_NullValuesSkipped`, `_ConstantCollection_In`, `_ImplicitUnary_In`, `_ImplicitCall_In`, `_CollectionReadsRow_NotSupported`; `StringContains_ConstantValue`, `_CapturedLocalReceiver_Parameter`; `StartsWith_Column`, `EndsWith_Column`; `ToString_Column`, `_CapturedLocal`, `_Constant`, `_NullConstant`; `DateMember_YearMonthDay`, `_OtherPart_NotSupported`, `_NotAValueChain_NotSupported`; `UnsupportedMethod_NotSupported` |
| AC11 | `RowIndependentEvaluatorTests` | `IsRowIndependent_Null_Throws`, `TryTranslateContains_Null_Throws`, `TryTranslateContains_NotABoolContains_Throws` (`string.IsNullOrEmpty`; a user `int Contains`), `TryTranslateContains_ReadsTheRow_ReturnsFalseWithoutEvaluating`, `TryReadValue_Null_Throws`, `TryReadValue_NotAChain_ReturnsFalse` (method call, a chain on the parameter, a conversion) |
| AC12 | Task 4, checked in the diff by the reviewer | — |

### 4.3 Interface coverage

| Member | Tests |
|---|---|
| `RowIndependentEvaluator.IsRowIndependent(Expression)` | `IsRowIndependent_ConstantAndCapturedChains_True`, `_ReadsTheParameter_False`, `_NestedLambdaParameter_True`, `_ParameterOutsideItsLambda_False`, `_Null_Throws` |
| `RowIndependentEvaluator.TryTranslateContains(MethodCallExpression, out string)` | the AC2/AC3/AC8/AC11 rows above, plus `_CapturedItemAbsent_FalseSql`, `_CapturedItemPresent_TrueSql` |
| `RowIndependentEvaluator.TryReadValue(Expression, out object)` | `TryReadValue_Constant`, `_NullConstant`, `_ClosureField`, `_CapturedThisProperty`, `_NestedProperty`, `_StaticProperty`, `_StaticField`, `_NotAChain_ReturnsFalse`, `_NullIntermediate_WrapsInNotSupported`, `_ThrowingGetter_InnerIsTheOriginal`, `_Null_Throws` |
| `RowIndependentEvaluator.TrueSql`, `FalseSql` (constants) | asserted by value in `_CapturedItemPresent_TrueSql`, `_CapturedItemAbsent_FalseSql` |
| `RowIndependentEvaluator.RewriteForEvaluation` (internal) | `RewriteForEvaluation_SpanBound_InterpretsWithoutSpan`, `_UserImplicitConversion_NotRewritten`, `_NotSpanBound_Unchanged` |
| Each provider's `TranslateMethodCall` (changed) | `TranslatorDirectHarness` and `CapturedValueHarness` on that provider |
| Each provider's `TranslateDateMember` (unchanged; in a touched file) | `DateMember_*` |

### 4.4 Mutations each key test must kill (run them; record the result in §5)

| # | Mutation | Killed by |
|---|---|---|
| M1 | A translator doesn't call `TryTranslateContains` (each provider in turn) | `Query_ReturnsWhatCSharpSelects(orAbsent)`, `Delete_DeletesWhatCSharpSelects(orAbsent)` on that provider; `CollectionContains_RowIndependentItem_FalseSql` |
| M2 | `TrueSql = "1=1"` | `Sql_RowIndependentContains_IsALiteral(andPresent)`; `Delete_DeletesWhatCSharpSelects(andPresent)` (the old guard rejects `1=1`) |
| M3 | `TrueSql` and `FalseSql` swapped | `Query_…(orAbsent)`, `Query_…(andPresent)` |
| M4 | Evaluate by `collection.Cast<object>().Contains(item)` instead of the call | `TryTranslateContains_UsesTheCollectionsComparer`, `_UserCollectionContains`; `Query_…(orComparerSet)` |
| M5 | `IsRowIndependent` treats a nested lambda's parameter as free | `IsRowIndependent_NestedLambdaParameter_True` |
| M6 | `IsRowIndependent` returns true for everything | `IsRowIndependent_ReadsTheParameter_False`; `Query_…(idsX)` |
| M7 | No span rewrite | `RewriteForEvaluation_SpanBound_InterpretsWithoutSpan` |
| M8 | The rewrite ignores the declaring type | `RewriteForEvaluation_UserImplicitConversion_NotRewritten` |
| M9 | No item check (back to falling through) | `Rejected_BeforeAnyCommand(nestedParamItem)`; `CollectionContains_NestedParamItem_NotSupported` |
| M10 | The item check after the empty short-circuit | `CollectionContains_NestedParamItemEmptyCollection_NotSupported`, `_ValueUnwrapEmptyCollection_…`; `Rejected_…(emptyNestedParamItem)` |
| M11 | No receiver check in `StartsWith`/`EndsWith` | `Rejected_…(startsWithCapturedReceiver)`, `(startsWithNestedParamReceiver)`; `StartsWith_CapturedReceiver_NotSupported` |
| M12 | `TryReadValue` reads fields only | `TryReadValue_CapturedThisProperty`; `Query_…(containsThisFilter)`; `StringContains_CapturedThisPropertyValue` |
| M13 | `TryReadValue` keeps the `TargetInvocationException` | `TryReadValue_ThrowingGetter_InnerIsTheOriginal` |
| M14 | `TranslateExpression` recurses again | `ToString_MemberOfMember_NotSupported`; `Rejected_…(codeEqualsLengthToString)` |
| M15 | A null string `Contains` value sends `'%%'` again | `StringContains_NullValue_NotSupported`; `Rejected_…(containsNullFilter)` |
| M16 | Evaluation exceptions not wrapped | `TryTranslateContains_Throwing_WrapsInNotSupported`; `Rejected_…(throwingItem)` |
| M17 | `TryTranslateContains` called after the collection is evaluated | `CollectionContains_RowIndependent_ReadsCapturedValuesOnce` |

M1, M9, M11, M14 and M15 are run on each provider; the others on SQL Server, with M2 and M3 also on SQLite (its C# 14
array binding).

### 4.5 Coverage

Touched files must reach **≥ 85 %** line coverage (cobertura `line-rate`, coverlet already referenced by all four
test projects): the four `*ExpressionTranslator.cs` (baseline §1.2: 63.6 / 58.5 / 49.1 / 36.8 %) and Core's
`RowIndependentEvaluator.cs` (new; measured from SqlServer.Tests). Each translator is measured from its own
provider's test project, with that suite's database available. Per-file numbers go into §5 and the PR.

### 4.6 Opt-in suites and frameworks

- **SQL Server:** `FUNKY_CONNECTION`. **MySQL:** `FUNKY_MYSQL_CONNECTION`. **PostgreSQL:** the suite's fallback
  (`postgres_user`/`postgres_pwd` to the local service). **SQLite:** file-backed. All four run locally for every task
  and for coverage; database runs are serial.
- **net48:** `RowIndependentEvaluatorTests` runs in `Funcular.Data.Orm.SqlServer.Tests.NetFramework` (Core's
  netstandard2.0 build on .NET Framework), without the `#if NET` span tests.

---

## 5. Design

### 5.1 Core: `Funcular.Data.Orm.Linq.RowIndependentEvaluator` (public static class)

- `public const string TrueSql = "NOT 1=0";` and `public const string FalseSql = "1=0";`
- `public static bool IsRowIndependent(Expression expression)` — true when the expression reads no
  `ParameterExpression` it doesn't declare itself (a nested lambda's own parameters are declared). An
  `ExpressionVisitor` collects lambda parameters as it enters each lambda and flags any other parameter.
- `public static bool TryTranslateContains(MethodCallExpression call, out string sql)` — `call` must be a
  `bool`-returning method named `Contains` (`ArgumentException` otherwise). When `IsRowIndependent(call)`, evaluates
  `RewriteForEvaluation(call)` through `Expression.Lambda<Func<bool>>(…).Compile()` and returns `TrueSql`/`FalseSql`;
  otherwise returns `false` with `sql = ""`. Exceptions are wrapped per D9.
- `public static bool TryReadValue(Expression expression, out object? value)` — a `ConstantExpression`, or a chain of
  `MemberExpression`s over fields and properties ending at a `ConstantExpression` or a static member, is read by
  reflection (no compilation). Anything else returns `false`. A read that throws is wrapped per D9 (unwrapping
  `TargetInvocationException`); a null link in the chain throws `NullReferenceException` inside, as C# would.
- `internal static Expression RewriteForEvaluation(MethodCallExpression call)` — when `call.Method.DeclaringType` is
  `System.MemoryExtensions` (compared by full name: netstandard2.0 has no `MemoryExtensions` without System.Memory),
  the call is static with two arguments, and the first is an `op_Implicit` conversion (a `MethodCallExpression` or a
  `UnaryExpression`, the two forms the translators already unwrap) of an `IEnumerable<T>`, returns
  `Enumerable.Contains<T>(source, item)`; otherwise returns `call`. Same answer (both use
  `EqualityComparer<T>.Default`), and it can be interpreted where code can't be generated.

### 5.2 Each translator (four copies, identical apart from dialect)

1. **`ToString`** — unchanged; its receiver goes through `TranslateExpression` (item 5).
2. **String `Contains`** — the receiver through `TranslateExpression`. The value: `TryReadValue` (D7), else
   `NotSupportedException` (today's message, "Unsupported expression in string Contains: …"); a null value throws
   (D8); otherwise `'%' + value + '%'` as one parameter, as today.
3. **Collection `Contains`** — after the collection expression is found (`:90-115`, unchanged):
   `TryTranslateContains` first (D4). Then the collection is evaluated as today. Then the item: the `.Value` check;
   then a member of the lambda parameter or `NotSupportedException` naming the item (D5); then the empty short-circuit
   and the `IN` list as today.
4. **`StartsWith`/`EndsWith`** — `"Contains"` leaves the method-name check and its pattern branch is deleted (D6). The
   receiver must be a `MemberExpression` whose expression is a `ParameterExpression`, else
   `NotSupportedException`. The argument: `TryReadValue` (D7), else today's "Unsupported …" message; null throws as
   today, with a message that names the null.
5. **`TranslateExpression`**, member case — a member of the lambda parameter that is a property is its column; one
   that isn't a property throws (today it appends nothing). Otherwise `TryReadValue` gives a parameter (D7). Anything
   else throws `NotSupportedException` naming the member (AC7) instead of recursing.

---

## 6. Tasks

- **Task 0 — Test-plan review** (non-author): this plan's ACs, matrix, mutations and §1 claims, before any test or
  code. Owner confirms D8. Rounds until CLEAN; dispositions in §9.
- **Task 1 — Core helper, red → green.** `RowIndependentEvaluator` as a stub (members throw
  `NotImplementedException`) and `RowIndependentEvaluatorTests`; record the red run; implement; green on net8 and
  net48. Mutations M4–M8, M12, M13, M16 (helper side).
- **Task 2 — Translator harnesses, red.** `TranslatorDirectHarness` and `CapturedValueHarness`, their four derived
  classes, and the csproj links. Record the red run on all four providers: every row that §4.2 marks as changed
  fails, and every AC10 row passes.
- **Task 3 — Translators, green.** §5.2 in all four providers. All four suites green; coverage ≥ 85 % per touched
  file (§4.5); mutations M1–M17 executed and recorded.
- **Task 4 — Documents.** Changelog `[3.10.0-beta1]`: Fixed (I1-6; captured properties; the receivers and the dropped
  member), Changed (the shapes that now throw `NotSupportedException`, the null `Contains` value), New public API
  (`RowIndependentEvaluator`). `Usage.md`: the `IN` clause (a value that isn't a column is answered before the query)
  and the `LIKE` clause (what a search value can be; receivers must be entity properties).
- **Task 5 — Hostile review** of `35c6477..HEAD` by a non-author, then fix-verification of every fix layer until
  CLEAN. The push sentinel is written only after a CLEAN pass over the exact sha.
- **Task 6 — The delete guard (D11).** When `development/3.10` holds the guard rewrite: merge it in; add the AC9
  rows; update the guard harness's tripwire comment and `DELETE_GUARD_PLAN.md` §6 (I1-6 and the `StartsWith`/
  `EndsWith` exception type); run all four suites; fix-verification of the merge.

---

## 7. Risks

- **The same translator files are touched by the date-part fix** (owner decision 2026-10-02, plan not yet written).
  Whichever branch lands second resolves the conflict and re-runs both harnesses.
- **A double read with the delete guard.** The rewritten guard reads captured properties for its own classification
  (its §6). A collection `Contains` is Unknown there, so its values are read once, by the translator.
- **Compilation cost.** A row-independent collection `Contains` compiles one small lambda per translation, as the
  translators already do for every collection.
- **.NET 10's `MemoryExtensions.Contains(span, value, comparer)`** isn't rewritten; on a platform that can only
  interpret expressions it reports "Could not evaluate expression". The test projects target net8.0.

---

## 8. Out of scope (recorded, not fixed here)

- **LIKE metacharacters in a search value aren't escaped** (N2): `x.Name.Contains("%")` sends `'%%%'`, and a delete
  with it removes every row (SQL Server and SQLite, executed). `_` and SQL Server's `[` behave alike. A separate
  data-loss defect; flagged to the owner.
- **A collection holding null loses its nulls** (N1): `{1, null}.Contains(x.NullableId)` sends `IN (@p0)`, so rows
  whose column is null are missed (C# includes them). Fewer rows, not more; flagged to the owner.
- **A row-independent string comparison or string `Contains` is compared by the server**, with its collation
  (`x.Id == 2 || s == "s"`, `|| s.Contains("s")`), as `DELETE_GUARD_PLAN.md` §6 records. D7 extends today's
  captured-local behaviour to properties; it doesn't move the comparison into C#.
- **Evaluating row-independent `StartsWith`/`EndsWith`/`IsNullOrEmpty`** — D3 chose failing closed; a possible
  follow-up, which the delete guard's tripwire would then need to see.
- **A converted member as a `Contains` item** (`longs.Contains(x.Id)`, `{1}.Contains((int?)x.Id)`) now throws
  `NotSupportedException` (was `NullReferenceException`); supporting it is a feature.
- **A null receiver of `ToString()` or string `Contains`** is sent as NULL rather than throwing as C# would.
- **A `[NotMapped]` member of the parameter** as an item or receiver is still sent as a column (a server error).
- **`string.Contains(string, StringComparison)`** ignores the comparison; **`Enumerable.Contains(list, x.Code,
  comparer)`** ignores the comparer (the server's `IN` decides).
- **A non-enumerable `Contains` on a member of the parameter** (`x.Window.Contains(5)`) fails with the "parameter
  references" message, as today.
- **Date parts on non-nullable members** — the date-part plan.

---

## 9. Review dispositions

(none yet)
