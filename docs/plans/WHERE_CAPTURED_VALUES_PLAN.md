# WHERE method calls: captured items, receivers and values — Implementation Plan

> **Goal:** make the translator's method calls read captured values the way C# does and never mistake one for a
> column, on all four providers: WHERE for queries, counts and deletes, sync and async, and the SELECT projections
> that share the translator.
> - Ships in **3.10.0, before the beta PR** (owner decision 2026-10-02).
> - Branch `fix/where-captured-values`, cut from `development/3.10` at `35c6477`. It merges into `development/3.10`
>   only after `fix/delete-guard-trivial-predicates` has, and after its Task 6 (D11).
> - Started from the delete-guard review's finding I1-6 (`DELETE_GUARD_PLAN.md` §6, rev 9). The probe (§1) found a
>   second defect and its siblings; the owner folded them in (D2, D3).

> **Status (2026-10-03):** rev 13. **Task 0 is CLEAN** at rev 7 (`33b96cf`, §9.5). **Tasks 1–4 are done** (§6.1).
> Task 5's review of `35c6477..d37f719` found A1–A6, answered in `6d8842d` (rev 10); FV1 found B1–B4, answered in
> `b4d614b` (rev 11); FV2 found C1–C4, answered in `54aed0d` (rev 12); FV3 found D1 and three nits, answered in this
> revision's commit (documents only); FV4 by a non-author is next. Task 6 waits for the delete guard.
>
> **Revision 13 — what changed (FV3, D1–D4; no production code):** the pass-through sentence (Changelog, D9, AC8) is
> narrowed to a collection whose item is an entity property; a row-independent `Contains`' collection read is wrapped
> like any value (D1). "(unless noted)" on the Changed lead-in (D2); a test comment (D3); D4 folded into D1's wording.
>
> **Revision 12 — what changed (FV2, C1–C4):** the Changelog, D9 and AC8 say a database error or a
> `NotSupportedException` from a collection's enumeration passes through (C1); a row pins that any other exception,
> inner exception or not, is still wrapped, with M48/M49 (C2); the Known issues heading no longer promises 3.10.1 for
> the unscheduled `All` entry (C3); A1's record points to FV1-B1 (C4).
>
> **Revision 11 — what changed (FV1, B1–B4, N1–N3):** rev 10's `All` (`CASE WHEN (p) THEN 1 ELSE 0 END = 0`) made
> `All(x => x.NullableId != 5)` false where C# and 3.9.0 say true (B1): no one negation is right for both kinds of
> null. `All` is back to 3.9.0's `NOT (p)`; its divergence is a Known issue, pinned in both directions, and the fix
> (null-aware comparisons) goes to the main session, as the owner allowed. A database error or a `NotSupportedException`
> from a collection's enumeration passes through D9's wrap (B3). A `Where(…).All(…)` row (B4); the Changelog's enum
> claim corrected (B2); M44's row and nits (N1–N3).
>
> **Revision 10 — what changed (Task 5, A1–A6; the owner's directive D16):** a `ToString()` of anything but an entity
> property is now rejected, never evaluated (D16 replaces D7's `ToString` part and D14); `TryEvaluateToString` and
> `CoreEvaluability` are deleted, and Task 6 loses its "one `ToString` rule" step. `All` counts a row whose predicate
> is unknown as failing it (A1). A lazy collection's enumeration is wrapped per D9 (A3). Rows and mutations M40–M43
> for each; §4.3's and §4.4's retired entries marked; Changelog history corrected (A5); counts corrected (A6).
>
> Task 0's history: the review of rev 1 (`8e2a392`, §9.1) found 15 findings, 6 blocking; rev 2
> (`3ea7234`) answered them with the owner's four decisions (D7, D8, D12, D13). Its re-review (§9.2) found 7, 2
> blocking, answered in rev 3 (`1e990f3`). Its re-review (§9.3) found 6, 1 blocking, answered in rev 4 (`0755b99`).
> Its re-review (§9.4) found 4, 2 blocking, answered in rev 5 (`6d06445`) with the owner's D14 and D15. Its re-review
> (§9.5) found 4, none blocking, answered in rev 6 (`fa7ed72`); its fix-verification found two wording nits, fixed in
> rev 7, whose verification is CLEAN.

> **Revision 6 — what changed (Task 0 re-review, R5-1…R5-4):** §5.3's `ToString` message names what D14 rejects
> (R5-1); D14 discloses a third shape that works today, an interface-typed value holding a string, with a row (R5-2);
> `CoreEvaluability.cs` joins §4.5's list (R5-3); cast-receiver rows (R5-4).

> **Revision 5 — what changed (Task 0 re-review, R4-1…R4-4; a redesign, not a patch):** three rounds in a row found
> defects in the previous round's `ToString` rule, so rev 5 replaces the rule with one predicate: a captured `ToString`
> is evaluated exactly when the delete guard's own `IsEvaluable` holds for the whole call, receiver and arguments
> included. The predicate is copied verbatim into Core now (`CoreEvaluability`), and the guard adopts it at Task 6
> (R4-1). Everything else that doesn't read the row fails closed, which the owner chose knowing two shapes that work
> today break (D14, R4-2). Rows for each condition of the predicate (R4-3); wording and test notes (R4-4).
> - Owner decisions of 2026-10-03 on the work split with `fix/like-escaping-tostring` (D15): `ToString` on a column
>   belongs to that branch, so rev 3's column-`ToString(format)` rule, its row and M29 leave this plan; the delete
>   guard's updates are split by cause (Task 6). D8 stands ("null matches no row").

> **Revision 4 — what changed (Task 0 re-review, R3-1…R3-6):**
> - A `ToString` is evaluated as a whole call, so a nullable with no value gives `""` and a null string throws per D9
>   (R3-1); only the calls the delete guard evaluates are evaluated, so no comparison the guard can't see is sent
>   (R3-5); a static `ToString` fails closed instead of leaking `ArgumentException` (R3-3); the current culture is
>   pinned (R3-4).
> - Every evaluation goes through one settable `Compiler`, with a counting test per member and a source scan (R3-2).
> - The field-item check moves before the collection read; a non-enumerable value and each receiver get their own
>   message (R3-6).

> **Revision 3 — what changed (Task 0 re-review, R2-1…R2-7):**
> - A `ToString(...)` that doesn't read the row is evaluated in C#, arguments included, and sent as the string C#
>   produces; a column receiver with arguments fails closed (R2-1; D7, AC6, AC7).
> - The delete guard is cited at rev 12 (`ca82741`); Task 6 handles its `IsContainsOfNull` rule, tests and documents,
>   which D8 makes false (R2-2).
> - `x.Tags.Contains(x.Id)` replaces `x.Tags.Contains(5)`; the rejection rows assert §5.3's message prefixes, so
>   `nullCollection` is red at the baseline (R2-3, R2-4).
> - Interpretation is used where the runtime has it (.NET Framework 4.7.1+, .NET Core), else `Compile()` (R2-5).
> - A negated null value matches no row, pinned (R2-6); `SupportsPredicateAsValue` is a harness virtual (R2-7).

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
| N7† | `x.Id == 2 \|\| localInt.ToString() == "05"` (`localInt = 5`, a captured local) | {2} | SQL Server and MySQL {1,2,3} (Delete too); PostgreSQL server error; SQLite {2} | `… OR @p1 = @p2` with `@p1 = 5` (an int) |
| N8† | `x.Id == 2 \|\| absent.Id.ToString() == "05"` | {2} | `NotSupportedException`/`InvalidOperationException` (the captured object as a parameter) | — |
| N4† | `x.Code == localInt.ToString("D2")` | {} | the format is dropped: SQL Server and PostgreSQL server errors; MySQL and SQLite wrong rows | `t.code = @p0`, `@p0 = 5` |

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
- A captured `ToString()` receiver is sent as its raw value, not the string C# produces, and its arguments are
  dropped (N7, N4): `localInt.ToString() == "05"` deletes every row on SQL Server and MySQL.
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
- **`ToString` drops itself and its arguments.** `:40-48` translates only the receiver (`TranslateExpression`), so a
  captured value is sent as its raw value (N7: an `int`), and `ToString("D2")`'s format is lost (N4).
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
- **The delete-guard rewrite** (`fix/delete-guard-trivial-predicates`, rev 12 at `ca82741`). Its D2 classifies a
  collection `Contains` call as Unknown and evaluates string `Contains` and a core type's `ToString()` (formats
  included, rev 10) as C# does. Rev 10's `IsContainsOfNull` (`DeletePredicateGuard.cs:405-412`) folds a
  parameter-free `s.Contains(null)` on a non-null receiver as **true**, "because every provider sends `LIKE '%%'`";
  its harness row `orContainsNull`, its `DeletePredicateGuardTests` rows, its Changelog entries and `Usage.md`
  Warning describe that and the collation case as sent today. Its D3 rule 1 rejects `1=1` anywhere, and
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
- **Interpretation is available on most runtimes Core reaches.** `Compile(preferInterpretation: true)` called from
  a netstandard2.0 library runs on net48 and net8.0, and a null dereference inside surfaces as
  `NullReferenceException` (executed 2026-10-02). Interpreted on net8.0, a null `Nullable<T>.Value` throws
  `InvalidOperationException`, a throwing getter its own exception, `"zeta".Contains(null)` `ArgumentNullException`
  (Task 0 reviewer, executed). But `LambdaExpression.Compile(bool)` is missing from .NET Framework 4.6.1–4.7.0
  (present in the 4.7.1, 4.7.2 and 4.8 reference `System.Core.dll`), where a netstandard2.0 consumer would get
  `MissingMethodException` (Task 0 reviewer, reference assemblies scanned).
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
  parameter throws, and isn't evaluated. (Rev 3 also made a column `ToString(format)` throw; rev 5 hands that to
  `fix/like-escaping-tostring`, D15.)
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
- **D7 (owner, 2026-10-02, T0-1; extended in rev 3, R2-1) — Calls and values that don't read the row are evaluated
  in C#.**
  - A string `Contains` whose receiver and arguments don't read the row is D4's: answered in C# and sent as the
    literal. When its receiver or value is null it is sent as `NULL LIKE NULL`, which matches no row (D8). (Today a
    null receiver sends `NULL LIKE …`, which also matches no row; a null value sends `LIKE '%%'`, which matches every
    non-null receiver.)
  - **Superseded by D16 (rev 10):** the two `ToString` bullets below are rev 5–9's rule, kept as the record. No
    `ToString` is evaluated any more.
  - A `ToString(...)` that doesn't read the row is evaluated in C# — the whole call, by interpretation — and sent as
    the string C# produces, as one parameter, **exactly when the delete guard would evaluate it** (rev 5, R4-1):
    when `CoreEvaluability.IsEvaluable(call)` holds. That predicate is the guard's `IsEvaluable` and
    `IsEvaluableCall` (`DeletePredicateGuard.cs:297-360` at `ca82741`), copied verbatim: the call is an instance
    `ToString` with no argument or a format string, on a receiver whose static type (`call.Object.Type`, after any
    conversion) is an enum or a non-generic, sealed or value type of the core library, or a nullable one of those;
    and the receiver and every argument are themselves built only from constants, field and property reads,
    core-library casts, `!`, the logical operators, comparisons, the conditional operator and such calls. Why
    `ToString` needs the mirror and `Contains` doesn't: `ToString`'s result is sent as a parameter that a comparison
    then uses, which neither guard can fold; a `Contains` is sent as D4's literal, which the rewritten guard folds. So `localInt.ToString() == "05"` compares
    `'5'` with `'05'` (N7), `ToString("D2")` keeps its format (N4), a nullable with no value gives `""`, as C# does,
    and a null string receiver throws per D9, as C# does (R3-1). It is evaluated in the current culture, as C#
    evaluates it when the query is translated (R3-4).
  - Any other `ToString` that doesn't read the row — on a user type, on a receiver declared as `object` or an
    interface, with an `IFormatProvider`, static (`Convert.ToString(…)`), or with a receiver or argument built by
    arithmetic, an indexer, a method call or `??` — isn't evaluated: the guard couldn't see an always-true comparison
    it makes (R3-5, R4-1). It goes to §5.2 item 5's column-or-throw rule and throws `NotSupportedException` (D3, D14),
    whose message says to compute the string before the query. Most of these fail or send a wrong value today;
    three return correct rows today and now throw (D14): `x.Code == objStr.ToString()` with a receiver declared as
    `object` or an interface holding a string, and `x.Code == s.ToString(CultureInfo.InvariantCulture)`, in WHERE and
    in projections (Task 0 reviewer, executed).
  - A `ToString(...)` on a column is unchanged here (the column, its arguments dropped, as today): it belongs to
    `fix/like-escaping-tostring` (D15), which makes `ToString()` the dialect's cast to text and `ToString(format)`
    throw.
  - Where the translator needs a value for a column receiver — string `Contains`', `StartsWith`'s and `EndsWith`'s
    argument — a constant, or a chain of field and property reads rooted at a constant or a static member, is read as
    C# reads it and sent as a parameter. Anything else (a method call, a conversion, a chain that reads the row) is
    not supported, as today.
  - The comparison a `ToString()` value takes part in stays the visitor's, in SQL, as `absent.Code == "A"` is today
    (§8).
- **D8 (owner, 2026-10-02, T0-2; replaces rev 1's "throw") — A null search value matches no row.** String
  `Contains`, `StartsWith` and `EndsWith` with a column receiver and a null value send `<column> LIKE NULL`. So the
  optional-filter idiom `filter == null || x.Name.Contains(filter)` keeps returning every row (O1), the same idiom
  with `StartsWith` stops throwing (O4), and a bare `x.Name.Contains(null)` returns and deletes nothing instead of
  every non-null row (O5). C# would throw for a reached `Contains(null)`; the translator can't see the guard, and
  "no row" is the answer that is right for the guarded idiom and safe for the unguarded one. The value is SQL's
  UNKNOWN, so a negated one (`!x.Name.Contains(filter)`) matches no row either (R2-6).
- **D9 — Evaluation failures.** A read or a call that throws while the translator evaluates it (a null captured
  object, a throwing getter, a throwing collection) is reported as
  `NotSupportedException("Could not evaluate expression: <node>")` — the visitor's message (`WhereClauseVisitor.cs:279`)
  — with the original exception as `InnerException` (unwrapped from `TargetInvocationException`), before any command
  is sent. A guarded shape (`req == null || ids.Contains(req.Id)` with `req` null) throws too, as the visitor's
  `x.Id == filterId.Value` does today (§8). (Rev 11, FV1-B3: on the `<property> IN (…)` path, a `DbException`, an
  exception with one inside, and a `NotSupportedException` from the collection's enumeration pass through unchanged:
  a query used as the collection runs there, as in 3.9.0. FV2-C2 pins that anything else is still wrapped. Rev 13,
  FV3-D1: a collection that `RowIndependentEvaluator` reads for a row-independent `Contains` is wrapped like any
  value, database errors included, as `TryTranslateContains_Throwing_WrapsInNotSupported` pins.)
- **D10 — One public Core helper**, `Funcular.Data.Orm.Linq.RowIndependentEvaluator`, which the four translators call
  (precedents: `ScalarProjectionGuard`, `QueryOperatorPolicy`). It evaluates by interpretation
  (`Compile(preferInterpretation: true)`) where the runtime has it, so the span rewrite runs on every platform and a
  one-shot evaluation generates no code. The method is found once by reflection; on .NET Framework 4.6.1–4.7.0, which
  lack it, it falls back to `Compile()` (R2-5). No `InternalsVisibleTo` is added. §5.1 gives its members. The
  Changelog lists it under "New public API".
- **D11 — Merge gate.** This branch doesn't merge into `development/3.10` until the delete-guard rewrite is there and
  Task 6 is CLEAN: D4 against the old guard would let `x.Id == 2 || ids.Contains(present.Id)` delete every row.
  Before Task 6 the delete rows use only shapes whose outcome is the same under both guards (§4.1). The PR carries
  the stacked-PR warning block (merge order, do not auto-complete). Pushing the branch itself (after Task 5) is
  allowed.
- **D12 (owner, 2026-10-02, T0-4) — SELECT projections are covered.** They share `TranslateMethodCall`, so D4–D9
  apply there; AC13 and its rows prove the `CASE WHEN` shape and a rejection on every provider.
- **D13 (owner, 2026-10-02, T0-5) — The visitor's member-dropping** (V1: `x.Name.Length != 4`) goes to the date-part
  plan, which rewrites the same `VisitMember` lines. This plan records it in §8 and hands it over.
- **D14 (owner, 2026-10-03, R4-2; superseded by D16) — `ToString` is strict.** Only what the delete guard evaluates is evaluated (D7);
  every other row-independent `ToString` throws, including the shapes that work today (a receiver declared as
  `object` or an interface holding a string; a `string` with an `IFormatProvider`). They are listed under Changed, with the workaround: compute the
  string before the query. No comparison the guard can't see is sent.
- **D15 (owner, 2026-10-03) — The split with `fix/like-escaping-tostring`** (worktree `.claude/worktrees/like-tostring`,
  stacked on the delete guard at `ca82741`):
  - that branch owns LIKE escaping (§8's N2), `ToString()` on a column (the dialect's cast to text) and
    `ToString(format)` on a column (throws); this branch owns captured values (D2, D7, D8, D14) and the receivers (D6);
  - each branch updates the delete guard for what its own changes break (Task 6 lists this branch's share);
  - D8 stands as "a null search value matches no row" (the owner, asked again 2026-10-03 after the other session
    reported `ArgumentNullException`);
  - the four translators will conflict; whichever lands second merges the other and re-runs both harnesses.
- **D16 (owner directive, 2026-10-03; applied to A1 and A2) — Require plain values; reject what needs evaluating.**
  The owner, to the sibling session and then to this one: stop accommodating edge-case parameter shapes; reject an
  argument that needs an operation or evaluation with `NotSupportedException` and require a primitive value; "not
  opposed to rejecting `.ToString` universally". So:
  - a `ToString()` of anything but a property of the entity — a local, a constant, a captured object's or a static
    property, with or without a format or a provider, `Convert.ToString`, a null string — throws
    `NotSupportedException` with §5.3's message, before anything is read; nothing is evaluated. The caller computes
    the string before the query. This replaces D7's `ToString` bullets and D14, and answers A2 (an evaluated string
    compared with a column's own `ToString` changed type: a server error, or MySQL's wrong row);
  - shapes that returned the right rows in 3.9.0 and now throw are disclosed under Changed: a receiver declared as
    `object` or an interface holding a string, `s.ToString(CultureInfo.InvariantCulture)`, and a captured value's
    `ToString()` compared with a column's (`x.Id.ToString() == id.ToString()`, also with an enum, Task 5 reviewer,
    executed);
  - `RowIndependentEvaluator.TryEvaluateToString` and `CoreEvaluability` are deleted (unshipped, so no public API is
    lost); Task 6 no longer shares a `ToString` predicate with the guard;
  - `All` (A1): rev 10 translated it as `NOT EXISTS(… WHERE CASE WHEN (p) THEN 1 ELSE 0 END = 0)`, which FV1 found
    wrong for the other kind of null (B1: `All(x => x.NullableId != 5)`, C# true, became false). An unknown row is a
    C# true for `!=` and a negated comparison and a C# false for `>` or `==`, so no wrapper is right for both. Rev 11
    keeps 3.9.0's `NOT (p)`, records the divergence under Known issues, pins both directions, and hands the real fix
    (comparisons that are C#-faithful about null) to the main session, the owner's alternative ("fix All's
    translation, or tell the main session to"); that session filed it as a task for the owner to start;
  - for later edge-case findings on this branch the default is to reject the shape, without asking the owner again,
    and to disclose what it breaks.

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
- **AC6.** A value given to string `Contains`, `StartsWith` or `EndsWith` with a column receiver is read as C# reads
  it: a property of a captured `this`, a nested property (`request.Name`), a static member.
  `x => x.Id == 2 || x.Name.Contains(Filter)` returns and deletes row 2 only. (Rev 10, D16:) a `ToString(...)` that
  doesn't read the row — `Code.ToString()`, `localInt.ToString()`, `five.ToString("D2")`, `n.ToString()` with no
  value, `objStr.ToString()`, `s.ToString(CultureInfo.InvariantCulture)`, also compared with a column's
  `ToString()` — throws `NotSupportedException` with §5.3's message before any command is sent, and nothing is
  evaluated.
- **AC7.** A receiver of `ToString()` or of string `Contains` that reads the row through another member
  (`x.Name.Length`, `x.Child.Name`) throws `NotSupportedException` instead of losing a member. (A `ToString` on a
  column is `fix/like-escaping-tostring`'s, D15.)
- **AC8.** Nulls and failures:
  - a null value for string `Contains`, `StartsWith` or `EndsWith` with a column receiver matches no row, negated or
    not; the guarded idiom (`filter == null || x.Name.Contains(filter)`, also with `StartsWith`) returns every row; a
    string `Contains` that doesn't read the row, with a null receiver or value, matches no row (D7, D8). Inside `All`
    an unknown row counts as satisfying the predicate, as in 3.9.0 (rev 11, B1: a Known issue, pinned);
  - a read or a call that throws while the predicate is translated (a null captured object, a throwing collection,
    a lazy collection that throws while it is enumerated) throws `NotSupportedException` whose `InnerException` is
    the original exception, before any command is sent (D9), except a database error or a `NotSupportedException`
    from the enumeration of a collection whose item is an entity property, which passes through unchanged (rev 11,
    narrowed in rev 13). Nothing is deleted. (A `ToString()` on a null string is
    D16's rejection, with no inner exception.)
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
  `false` for a call that reads the row without evaluating anything; `TryReadValue` returns `false` for anything but
  a constant or a rooted chain of field and property reads (rev 10: `TryEvaluateToString` is deleted, D16); where
  the runtime lacks `Compile(bool)` the evaluator compiles instead.
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
     exception, its message prefix (§5.3), that no command was logged after the call began, and that all three rows
     remain. The log is captured from after the MySQL warm-up below.
   - **SQL rows** assert the logged WHERE: the literal D4 sends, no ` IN (`/` LIKE ` for the evaluated call, and the
     parameter count.
   - **Projection rows** (AC13) run `Query<T>().Select(x => new CvRow { Id = x.Id, Label = … })` and compare
     `Id:Label` pairs; the bare `bool` row runs where the derived test class's `SupportsPredicateAsValue` (a harness
     virtual, not provider API) is true: every provider but SQL Server.
   - **Binding assertion.** Each array row asserts the declaring type of its `Contains` call: `MemoryExtensions` in
     the SQLite class, `Enumerable` in the others (a virtual on the derived class). If Sqlite.Tests ever stops
     compiling as C# 14, the SQLite rows fail rather than silently testing the C# 12 shape.
   - Before a delete, the MySQL class runs one query on the type: `development/3.10`'s MySQL delete fails on a cold
     column cache (fixed on `fix/provider-scoped-caches`).
   - **Delete shapes before Task 6** are those whose outcome is the same under both guards (D11): every `OR` here is
     false or unknown after evaluation (D8's `LIKE NULL` rows are unknown; both guards accept them — Task 0 reviewer,
     executed), every `AND` passes both guards' checks (`(t.id = @p AND NOT 1=0)` matches none of the old patterns or
     regexes), and the shapes the old guard rejects are rejected by the new one with the same message.

Test entity for the integration rows (`zz_cv_row`): `CvRow { int Id; string Code; string Name; int? NullableId;
[NotMapped] CvInner Child; [NotMapped] string Label; [NotMapped] bool Flag }`, `CvInner { int Id; string Name }`. The
direct harness's entity adds a nullable date (`DateTime? Birth`), a `[NotMapped] List<int> Tags`, a
`[NotMapped] CvRange Window` (a non-enumerable type with `bool Contains(int)`) and a public field.

Captured values, shared: `ids = {1, 2}`, `arr = new[] {1, 2}`, `absent` (Id 5, Code "a", Name "zeta", NullableId 5,
Inner.Id 5, Ids {1, 2}), `present` (Id 1), `nullHolder` (NullableId null), `nullRequest = null`,
`nullIdsHolder` (Ids null), `local = 5`, `localInt = 5`, `five = 5`, `n = (int?)null`, `boxed = (object)5`, `settings` (a non-sealed user class whose `ToString()` is `"on"`), `sealedSettings` (a sealed one), `userStruct` and `nullableUserStruct` (a user struct, `ToString()` `"on"`), `pair = new KeyValuePair<int,int>(1, 2)`, `objStr = (object)"b"`, `ifaceStr = (IComparable)"b"`, `sB = "b"`, `flag = true`, `digits = "2"`, a static `Seven()` returning 7, `codes = {"A"}`, `ci` (`HashSet`, OrdinalIgnoreCase, `{"A"}`),
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
| AC2 | `Delete_DeletesWhatCSharpSelects` (D) | `orCodesCase` {2}; `andNullItem` {2}; `andComparerSet` {2}; `orArrayAbsent` {2}; `orCapturedReceiverCase` {2}; `orLocalReceiverCase` {2} | red (C1 deletes {1,2,3}); `orLocalReceiverCase` on PostgreSQL: luck — red: `Sql_…(orLocalReceiverCase)` |
| AC2 | `RowIndependentEvaluatorTests` | `TryTranslateContains_UsesTheCollectionsComparer`, `_EnumerableContainsWithComparer`, `_OrdinalStringEquality`, `_NullItem`, `_UserCollectionContains`, `_SpanBound_Evaluates`, `_StringReceiver_HonorsStringComparison` (`#if NET`), `RewriteForEvaluation_SpanBound_InterpretsWithoutSpan` | new |
| AC3, AC8 | `Sql_RowIndependentContains_IsALiteral` (`query`) | `orAbsent` → `1=0`; `andPresent` → `NOT 1=0`; `andNotPresent` → `NOT NOT 1=0`; `andArrayPresent` → `NOT 1=0`; `orLocalReceiverCase` → `1=0`, one parameter (`x.Id == 2`'s) each; `optionalFilterNull` → `t.name LIKE NULL`; `optionalReceiverNull` → `NULL LIKE NULL` (one parameter: the `IS NULL` test's); `notContainsNullValue` → `NOT t.name LIKE NULL`; `orCapturedToString05` → a `string` parameter `"5"`; `orFormattedToString` → a `string` parameter `"05"` | red |
| AC3 | `TranslatorDirectHarness` | `CollectionContains_RowIndependentItem_FalseSql`, `_TrueSql` (no parameter added); `CollectionContains_RowIndependent_ReadsCapturedValuesOnce`; `StringContains_RowIndependent_FalseSql`, `_TrueSql` | new |
| AC4 | `Rejected_BeforeAnyCommand` (Q, D) | `nestedParamItem` (`ids.Contains(x.Child.Id)`); `lengthItem`; `convertItem` (`longs.Contains(x.Id)`); `emptyNestedParamItem` (`emptyIds.Contains(x.Child.Id)`); `nullCollection` (`nullIdsHolder.Ids.Contains(x.Id)`, D5) | red (S28 wrong rows; S21 server error; S22 NRE; `1=0` returns {}; `nullCollection` throws `NotSupportedException` today, "Unsupported member expression in Contains: x.Id" — red because the row asserts §5.3's prefix) |
| AC4 | `TranslatorDirectHarness` | `CollectionContains_NestedParamItem_NotSupported`, `_NestedParamItemEmptyCollection_NotSupported`, `_ConvertedItem_NotSupported`, `_NestedParamItem_CollectionNotRead` (a counting getter stays at 0), `_ValueUnwrap_NullableExpressionException`, `_ValueUnwrapEmptyCollection_NullableExpressionException`, `_MemberItem_In`, `_FieldItem_InvalidOperationBeforeCollectionRead`, `_CollectionReadsRow_ParameterReferences` (`x.Tags.Contains(x.Id)`, message as today), `_RowIndependentItemRowCollection_NotSupported` (`x.Tags.Contains(5)`: D5's message) | new |
| AC5 | `Rejected_BeforeAnyCommand` (Q, D) | `startsWithCapturedReceiver` (S23); `endsWithCapturedReceiver` (S24); `startsWithLocalReceiver` (S26); `startsWithStaticReceiver` (`StaticHolder.Name.StartsWith("al")`); `startsWithNestedParamReceiver` (T5) | red (wrong rows, NRE) |
| AC5 | `TranslatorDirectHarness` | `StartsWith_CapturedReceiver_NotSupported`, `_LocalReceiver_NotSupported`, `_StaticReceiver_NotSupported`, `_NestedParamReceiver_NotSupported`, `_MethodCallReceiver_NotSupported`, `CollectionContains_NullCollection_NotSupported`, `CollectionContains_NonEnumerableReceiver_NotSupported`, `CollectionContains_NonBoolCall_NotSupported` (no `ArgumentException` leaks) | new |
| AC6 | `Query_ReturnsWhatCSharpSelects` (Q) | `containsThisFilter` (U1) {2}; `containsNestedCaptured` (`x.Name.Contains(needle.Name)`) {3}; `startsWithThisFilter` (U2) {2}. No collation can change these answers. (Rev 10: `codeEqualsThisCodeToString` (U3) and `codeEqualsNestedToString` (T1) move to D16's rejection row.) | red (U1, T3, U2) |
| AC6, D16 | (rev 10) the evaluated-`ToString` rows `orCapturedToString05` (N7), `orPropertyToString05` (N8), `orFormattedToString`, `orNullableToStringIsNull`, `orNullableToStringIsEmpty` leave `Query_…`/`Delete_…` for D16's rejection row below | were red at `35c6477` (wrong rows); red again at `d37f719` (evaluated) |
| AC6 | `Delete_DeletesWhatCSharpSelects` (D) | `containsThisFilter` {2}; `startsWithThisFilter` {2} | red (as the Query rows) |
| AC6 | `TranslatorDirectHarness` | `StringContains_CapturedThisPropertyValue`, `_NestedCapturedValue`, `_StaticValue`, `_CapturedLocalValue`; `StartsWith_CapturedThisPropertyArgument`, `_NestedCapturedArgument`; (rev 10, D16:) `ToString_CapturedLocal_NotSupported`, `_Constant_NotSupported`, `_Formatted_NotSupported`, `_CapturedThisProperty_NotSupported`, `_NestedCaptured_NotSupported`, `_StaticMemberReceiver_NotSupported`, `_NullableNoValue_NotSupported`, `_Enum_NotSupported`, `_NonStringReturn_NotSupported` (A4: a user `new object ToString()`), `ToString_Captured_NothingRead` (a counting getter stays at 0: nothing is evaluated) | new; rev 10 red at `d37f719` (evaluated) |
| AC7 | `Rejected_BeforeAnyCommand` (Q) | `codeEqualsLengthToString` (T2); `childNameContains` (T4) | red (T2 sends `t.code = t.name`; T4 server error) |
| AC6, D7 → D16 | `Rejected_BeforeAnyCommand` (Q, D) | `userTypeToString` (`x.Id == 2 \|\| settings.ToString() == "on"`); `objectDeclaredToString` (`\|\| boxed.ToString() == "5"`); `providerToString` (`\|\| five.ToString(CultureInfo.InvariantCulture) == "5"`); `staticConvertToString` (`x.Code == Convert.ToString(localInt)`); `orArithmeticToString` (`x.Id == 2 \|\| (five + 1).ToString() == "6"`); `orIndexToString` (`\|\| arr[0].ToString() == "1"`); `orMethodToString` (`\|\| Seven().ToString() == "7"`); `orCoalesceToString` (`\|\| (n ?? 7).ToString() == "7"`); `orStructToString` (a user struct); `orNullableStructToString`; `orSealedClassToString` (a sealed user class); `orPairToString` (`KeyValuePair<int,int>`) — each C# true, so a translation would delete every row | red (user type: today a parameter-type error, not §5.3's prefix; `object` and provider: `5 = '5'`, wrong rows on SQL Server and MySQL; static: server syntax error; arithmetic, indexer, method, `??`: today's "Expression type … is not supported", not §5.3's prefix) |
| D14 → D16 | `Rejected_BeforeAnyCommand` (Q, D) | `objectHoldingStringToString` (`x.Code == objStr.ToString()`, `objStr = (object)"b"`); `providerStringToString` (`x.Code == sB.ToString(CultureInfo.InvariantCulture)`, `sB = "b"`); `interfaceHoldingStringToString` (`x.Code == ifaceStr.ToString()`, `IComparable ifaceStr = "b"`) | today {2}, C#'s answer — red because the owner chose to reject them (D14) |
| D14 → D16 | `Rejected_BeforeAnyCommand` (`query`) | `labelObjectToString` (`Label = objStr.ToString()`); `labelProviderToString`; (rev 10) `labelFormatted` (`Label = five.ToString("D2")`) | today b, b, b — red (D14); `labelFormatted` red at `d37f719` (05) |
| D16 | `Rejected_BeforeAnyCommand` (Q, D) | (rev 10) `codeEqualsThisCodeToString`, `codeEqualsNestedToString`, `orCapturedToString05`, `orPropertyToString05`, `orFormattedToString`, `orNullableToStringIsNull`, `orNullableToStringIsEmpty`, `intColumnToStringEqCaptured` (A2: `x.Id.ToString() == localInt.ToString()`), all with §5.3's `ToString` message and no inner exception | red at `d37f719` (evaluated; A2's row returned a type error or the wrong row there) |
| AC7 | `TranslatorDirectHarness` | `ToString_MemberOfMember_NotSupported`, `StringContains_ReceiverMemberOfMember_NotSupported`, `ToString_FieldOfParameter_NotSupported`, `ToString_OtherExpression_NotSupported`, `ToString_RowArgument_NotSupported` (`five.ToString(x.Code)`), `ToString_UserType_NotSupported`, `_ObjectDeclared_NotSupported`, `_FormatProvider_NotSupported`, `_Static_NotSupported` (no `ArgumentException` leaks), `_ConcatenatedFormat_NotSupported` (`five.ToString("D" + digits)`), `_ArithmeticReceiver_NotSupported`, `_ConditionalReceiver_NotSupported` (`(flag ? 1 : 2).ToString()`; rev 5–9 evaluated it) | new |
| AC8 | `Query_ReturnsWhatCSharpSelects` (Q) | `optionalFilterNull` (O1) {1,2,3}; `optionalThisFilterNull` (O1 with `this.NullFilter`) {1,2,3}; `optionalStartsWithNull` (O4) {1,2,3}; `optionalReceiverNull` (`nullFilter == null \|\| nullFilter.Contains("a")`) {1,2,3}; `containsNullValue` (`x.Id == 2 \|\| x.Name.Contains(nullFilter)`, knownDivergence D8) {2}; `startsWithNullValue` (knownDivergence D8) {2}; `notContainsNullValue` (`x.Id == 2 \|\| !x.Name.Contains(nullFilter)`, knownDivergence D8) {2} (luck today, `NOT t.name LIKE '%%'` — red: `Sql_…(notContainsNullValue)`) | `optionalFilterNull`, `optionalThisFilterNull`, `optionalReceiverNull`: luck — red: `Sql_…(optionalFilterNull)`, `Sql_…(optionalReceiverNull)`; `optionalStartsWithNull` red (O4 throws); `containsNullValue` red (O5's `'%%'`); `startsWithNullValue` red (throws) |
| AC8 | `Delete_DeletesWhatCSharpSelects` (D) | `containsNullValue` {2}; `startsWithNullValue` {2} | red |
| AC8 | `Rejected_BeforeAnyCommand` (Q, D) | `throwingItem` (S17; inner `NullReferenceException`); `throwingCollection` (R1: `nullRequest.Ids.Contains(x.Id)`; inner `NullReferenceException`); `nullStringToString` (`x.Code == nullFilter.ToString()`; rev 10: D16's message, no inner exception) | red (S17 wrong rows; R1 `TargetInvocationException`; `t.code = NULL` returns {}) |
| AC8 | `All_UnknownCountsAsSatisfied` (`query`, rev 11, A1/B1) | `allIdPositive` true; `allNotIdsX` false; `allNullableNotEqual` (`x.NullableId != 5`) true; `allNotNullableEquals` (`!(x.NullableId == 5)`) true — C#'s answers; `allNullableGreaterThanZero` true (Known issue: C# false; the row asserts it differs from C#); `allContainsNull`, `allNotContainsNull`, `allOrContainsNull`, `allRowIndependentNull` true (D8: C# throws) | red at `6d8842d` (rev 10's `CASE`): the last seven false |
| AC8 | `WhereThenAll_MatchesCSharp` (`query`, rev 11, B4) | `Where(x => x.Id > 1)` then `allNullableNotEqual` true, `allIdPositive` true, `allNotIdsX` false | red at `6d8842d`: `allNullableNotEqual` false |
| AC8 | `TranslatorDirectHarness`, `RowIndependentEvaluatorTests` | `StringContains_NullValue_LikeNull` (constant and captured null), `StartsWith_NullArgument_LikeNull`, `EndsWith_NullArgument_LikeNull`, `StringContains_RowIndependentNullReceiver_Unknown`, `_RowIndependentNullValue_Unknown`, `CollectionContains_ThrowingCollection_NotSupportedWithInner`, `_LazyThrowingCollection_NotSupportedWithInner` (rev 10, A3: `ids.Select(i => i / zero)`; inner `DivideByZeroException`), `_LazyDbException_PassesThrough`, `_LazyExceptionWithDbInner_PassesThrough`, `_LazyNotSupported_PassesThrough` (rev 11, B3), `_LazyExceptionWithNonDbInner_NotSupportedWithInner` (rev 12, C2), `ToString_NullStringReceiver_NotSupported` (rev 10: D16's message); `TryTranslateContains_Throwing_WrapsInNotSupported`, `TryReadValue_NullIntermediate_InnerIsNullReference`, `TryReadValue_NullNullableValue_InnerIsInvalidOperation`, `TryReadValue_ThrowingGetter_InnerIsTheOriginal` | new |
| AC9 | Task 6 rows in `CapturedValueHarness` | `Delete_AlwaysTrueAfterEvaluation_IsRejected` (D): `orPresent`, `orNotAbsent`, `orNullItem`, `orComparerSet` → the guard's message, nothing deleted; `andPresent` stays in `Delete_DeletesWhatCSharpSelects`; `orCapturedContainsNull` (`x.Id == 2 \|\| s.Contains(nullFilter)`) → deletes {2} once Task 6 has handled `IsContainsOfNull` | Task 6 |
| AC10 | `Query_ReturnsWhatCSharpSelects` (Q), `Delete_DeletesWhatCSharpSelects` (D) | `idsX` {1,2}; `notIdsX` {3}; `nullableX` {1}; `emptyX` {} (Q only: the guards reject `1=0` alone); `startsWithX` {1}; `containsX` (`x.Name.Contains("mm")`) {3}; `arrayX` {1,2} | green, must stay green |
| AC10 | `TranslatorDirectHarness` | `CollectionContains_EmptyCollection_OneEqualsZero`, `_NullValuesSkipped` (N1's `IN` list), `_ConstantCollection_In`, `_ImplicitUnary_In`, `_ImplicitCall_In`; `StringContains_ConstantValue`; `StartsWith_Column`, `EndsWith_Column`; `ToString_Column`; `DateMember_YearMonthDay`, `_OtherPart_NotSupported`, `_NotAValueChain_NotSupported`; `UnsupportedMethod_NotSupported` | new (pin today's output) |
| AC11 | `RowIndependentEvaluatorTests` | `IsRowIndependent_Null_Throws`, `TryTranslateContains_Null_Throws`, `TryTranslateContains_NotABoolContains_Throws` (`string.IsNullOrEmpty`; a user `int Contains`), `TryTranslateContains_ReadsTheRow_ReturnsFalseWithoutEvaluating`, `TryReadValue_Null_Throws`, `TryReadValue_NotAChain_ReturnsFalse` (a method call, a chain on the parameter, a conversion), `Compile_WithoutInterpretation_Compiles`, `TryTranslateContains_UsesTheCompiler`, `TryReadValue_UsesTheCompiler` (a counting `Compiler`; a member chain, not a constant), `Compile_OnlyInTheSeam` (a source scan of `RowIndependentEvaluator.cs`, found from `FunkyORM.sln` as `OperatorDocTableTests.RepoRoot()` does; it asserts the file was found and that the seam's own `.Compile(` occurs exactly once). Rev 10 deleted the 22 `TryEvaluateToString_*` tests and `CoreEvaluability_MatchesTheGuardsTable` with their code (D16). | new |
| AC12 | Task 4, checked in the diff by the reviewer | — | — |
| AC13 | `Projection_GetsCSharpsValue` (`query`) | `labelAbsent` (`ids.Contains(absent.Id) ? "in" : "out"`) → 1:out, 2:out, 3:out; `labelX` (`ids.Contains(x.Id) ? …`) → 1:in, 2:in, 3:out; `flagAbsent` (`Flag = ids.Contains(absent.Id)`, where `SupportsPredicateAsValue`) → false ×3 | red (P1, P2); `labelX` green |
| AC13 | `Rejected_BeforeAnyCommand` (`query`) | `labelNestedParam` (`ids.Contains(x.Child.Id) ? …`) | red (wrong labels) |

### 4.3 Interface coverage

| Member | Tests |
|---|---|
| `RowIndependentEvaluator.IsRowIndependent(Expression)` | `IsRowIndependent_ConstantAndCapturedChains_True`, `_ReadsTheParameter_False`, `_NestedLambdaParameter_True`, `_ParameterOutsideItsLambda_False`, `_Null_Throws` |
| `RowIndependentEvaluator.TryTranslateContains(MethodCallExpression, out string)` | the AC2/AC3/AC8/AC11 rows above, plus `_CapturedItemAbsent_FalseSql`, `_CapturedItemPresent_TrueSql`, `_StringReceiverNull_UnknownSql`, `_StringValueNull_UnknownSql` |
| `RowIndependentEvaluator.TryReadValue(Expression, out object)` | `TryReadValue_Constant`, `_NullConstant`, `_ClosureField`, `_CapturedThisProperty`, `_NestedProperty`, `_StaticProperty`, `_StaticField`, `_NotAChain_ReturnsFalse`, `_NullIntermediate_InnerIsNullReference`, `_NullNullableValue_InnerIsInvalidOperation`, `_ThrowingGetter_InnerIsTheOriginal`, `_Null_Throws` |
| ~~`RowIndependentEvaluator.TryEvaluateToString`~~ | deleted in rev 10 (D16), with its tests |
| `RowIndependentEvaluator.TrueSql`, `FalseSql`, `UnknownSql` (constants) | asserted by value in `_CapturedItemPresent_TrueSql`, `_CapturedItemAbsent_FalseSql`, `_StringReceiverNull_UnknownSql` |
| `RowIndependentEvaluator.RewriteForEvaluation` (internal) | `RewriteForEvaluation_SpanBound_InterpretsWithoutSpan`, `_UserImplicitConversion_NotRewritten`, `_NotSpanBound_Unchanged` |
| `RowIndependentEvaluator.Compile(LambdaExpression, MethodInfo)` (internal: the interpreting `Compile(bool)`, or null) | `Compile_WithInterpretation_Interprets` (the span call evaluates only after the rewrite), `Compile_WithoutInterpretation_Compiles` (null: `Compile()`) |
| ~~`CoreEvaluability`~~ (internal) | deleted in rev 10 (D16: no caller left), with `CoreEvaluability_MatchesTheGuardsTable` |
| `RowIndependentEvaluator.Compiler` (internal, settable) | `TryTranslateContains_UsesTheCompiler`, `TryReadValue_UsesTheCompiler`, `Compile_OnlyInTheSeam` |
| Each provider's `TranslateMethodCall` (changed) | `TranslatorDirectHarness` and `CapturedValueHarness` on that provider |
| Each provider's `TranslateDateMember` (unchanged; in a touched file) | `DateMember_*` |
| Each provider's LINQ query provider, `All` | (rev 11: unchanged from `35c6477` again; `All_UnknownCountsAsSatisfied` and `WhereThenAll_MatchesCSharp` pin it) |

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
| M25 | ~~A row-independent `ToString` evaluated without its arguments~~ | retired in rev 10: D16 deleted evaluation |
| M26 | A row-independent `ToString` sent as its raw value (no evaluation) | rev 10: as M41 (`Rejected_…(orCapturedToString05)`; `ToString_CapturedLocal_NotSupported`) |
| M27 | A null search value sent as `1=0` instead of `LIKE NULL` | `Query_…(notContainsNullValue)`; `Sql_…(notContainsNullValue)` |
| M28 | The compiler selection ignores a missing `Compile(bool)` | `Compile_WithoutInterpretation_Compiles` |
| M29 | (withdrawn in rev 5: column `ToString` belongs to `fix/like-escaping-tostring`, D15) | — |
| M30 | ~~`ToString` evaluated for any receiver (no guard-mirroring check)~~ | retired in rev 10: D16 deleted the guard-mirroring check |
| M31 | ~~A null receiver sent as `NULL` (rev 3's rule)~~ | retired in rev 10: D16 deleted evaluation |
| M32 | ~~`ToString` evaluated in the invariant culture~~ | retired in rev 10: D16 deleted evaluation |
| M33 | One evaluator member calls `lambda.Compile()` (or `Compile(true)`) directly | that member's `_UsesTheCompiler`; `Compile_OnlyInTheSeam` |
| M34 | The field-item check after the collection read | `CollectionContains_FieldItem_InvalidOperationBeforeCollectionRead` |
| M35 | ~~`TryEvaluateToString` checks only the call (`IsEvaluableCall`), not its receiver and arguments~~ | retired in rev 10: D16 deleted `TryEvaluateToString` |
| M36 | ~~The core-library check dropped~~ | retired in rev 10: D16 deleted `CoreEvaluability` |
| M37 | ~~The `!IsGenericType` check dropped~~ | retired in rev 10: D16 deleted `CoreEvaluability` |
| M38 | A captured `ToString` sent as today's raw value (the compatible policy D14 rejected; rev 10: as M41) | `Rejected_…(objectHoldingStringToString)`, `(providerStringToString)`, `(interfaceHoldingStringToString)` |
| M39 | ~~`CoreEvaluability` drops `Convert` (or treats a user conversion as core)~~ | retired in rev 10: D16 deleted `CoreEvaluability` |

| M40 | ~~`All` translated as `NOT (p)` again~~ | retired in rev 11: `NOT (p)` is the translation again (B1) |
| M41 | A captured `ToString` receiver sent as its raw value (3.9.0's shape) | `Rejected_…(orCapturedToString05)`, `(codeEqualsNestedToString)`, `(intColumnToStringEqCaptured)`; `ToString_CapturedLocal_NotSupported`, `_StaticMemberReceiver_NotSupported`, `_NonStringReturn_NotSupported` |
| M42 | A row-independent `ToString` evaluated as C# does (rev 9's D7, without the guard's predicate) | `Rejected_…(orFormattedToString)`, `(orNullableToStringIsEmpty)`; `Projection_Rejected_…(labelFormatted)`; `ToString_Formatted_NotSupported`, `_Enum_NotSupported`, `_Constant_NotSupported` |
| M43 | The collection's enumeration outside D9's wrapper again | `CollectionContains_LazyThrowingCollection_NotSupportedWithInner` |
| M44 | The receiver read (evaluated), then rejected anyway | `ToString_Captured_NothingRead` |
| M40r | `All` as rev 10's `CASE WHEN (p) THEN 1 ELSE 0 END = 0` | `All_…(allNullableNotEqual)`, `(allNotNullableEquals)`; `WhereThenAll_…(allNullableNotEqual)` |
| M47 | `CASE` only when a `Where` precedes `All` (FV1's OWN-1, inverted) | `WhereThenAll_…(allNullableNotEqual)` |
| M45 | The enumeration wrap catches everything (no pass-through) | `CollectionContains_LazyDbException_PassesThrough`, `_LazyExceptionWithDbInner_PassesThrough`, `_LazyNotSupported_PassesThrough` |
| M46 | The pass-through misses a `DbException` inside another exception | `_LazyExceptionWithDbInner_PassesThrough` |
| M48 | Every `InvalidOperationException` passes through (FV2's OWN-10) | `_LazyExceptionWithNonDbInner_NotSupportedWithInner` |
| M49 | Any exception with an inner exception passes through (FV2's OWN-11) | `_LazyExceptionWithNonDbInner_NotSupportedWithInner` |

M1, M9, M11, M14, M15, M20, M26, M30, M31, M35, M38, (rev 10) M41–M44 and (rev 11) M40r, M43, M45–M47 and (rev 12) M48, M49 are run on each
provider; the others on SQL Server, with M2, M3 and M7 also on
SQLite (its C# 14 binding).

### 4.5 Coverage

Touched files must reach **≥ 85 %** line coverage (cobertura `line-rate`, coverlet already referenced by all four
test projects): the four `*ExpressionTranslator.cs` (baseline §1.2: 63.6 / 58.5 / 49.1 / 36.8 %), Core's `RowIndependentEvaluator.cs` (new; measured from SqlServer.Tests;
`CoreEvaluability.cs` until rev 10 deleted it). Each translator is measured from its own
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

- (Rev 10, D16: `CoreEvaluability`, the guard's predicate copied for D7's `ToString`, and `TryEvaluateToString`
  are deleted. Rev 9's text for them is in git history at `d37f719`.)
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
  - the result gives `TrueSql` or `FalseSql`. Evaluation goes through `Compile` below; failures are wrapped per D9.

  Otherwise it returns `false` with `sql = ""`, having evaluated nothing.
- `internal static Func<LambdaExpression, Delegate> Compiler` (settable; tests swap in a counting one and restore it)
  — every evaluation in this class goes through it. Its default is `Compile(lambda, InterpretingCompile)`.
- `internal static Delegate Compile(LambdaExpression lambda, MethodInfo interpretingCompile)` — the selection:
  `interpretingCompile` is `LambdaExpression.Compile(bool)`, found once by reflection
  (`typeof(LambdaExpression).GetMethod("Compile", new[] { typeof(bool) })`) into `InterpretingCompile`, and invoked
  with `true`; when it is null (.NET Framework 4.6.1–4.7.0), `lambda.Compile()`. No other line in the class calls
  `Compile` (a source-scan test pins it, R3-2).
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

1. **`ToString`** (rev 10, D16) — the receiver goes to `AppendColumn`: a property of the lambda parameter is its
   column (its arguments dropped, as today: D15); anything else, a static call included, throws §5.3's `ToString`
   message. Nothing is evaluated.
2. **String `Contains`** — first `TryTranslateContains` (D4, D7): a call that doesn't read the row is sent as its
   literal. Otherwise the value is checked first: `TryReadValue` (D7), else `NotSupportedException`
   ("Unsupported expression in string Contains: …", which replaces today's two texts, "Unsupported member expression
   …" and "Unsupported expression type …"). Then the receiver goes through `TranslateExpression`.
   A null value sends `<receiver> LIKE NULL` (D8); otherwise `'%' + value + '%'` as one parameter, as today.
3. **Collection `Contains`** — after the collection expression is found (`:90-115`, unchanged), in D5's order:
   `TryTranslateContains` for a `bool` call; the `.Value` check; the item check (a member of the lambda parameter
   that isn't a property throws today's `InvalidOperationException` here, before the collection is read, R3-6a); the
   collection read (a compile failure keeps "parameter references"; an invocation failure, and from rev 10 (A3) an
   exception while the collection is enumerated, is wrapped per D9, except a `DbException`, one with a `DbException`
   inside, and a `NotSupportedException`, which pass through: a query used as the collection runs here, rev 11, B3); a
   null
   collection, and a value that isn't enumerable (a captured non-collection type with its own `Contains`, R3-6b),
   each with its own message; the empty short-circuit and the `IN` list as today.
4. **`StartsWith`/`EndsWith`** — `"Contains"` leaves the method-name check and its pattern branch is deleted (D6). The
   receiver must be a `MemberExpression` whose expression is a `ParameterExpression`, else
   `NotSupportedException`. The argument: `TryReadValue` (D7), else today's "Unsupported …" message; a null value
   sends `<column> LIKE NULL` (D8).
5. **`TranslateExpression`** — after items 1 and 2 it only ever sees the receiver of a call that reads the row: a
   member of the lambda parameter that is a property is its column; anything else (a member that isn't a property,
   which today appends nothing; a member of a member; any other node) throws `NotSupportedException` (AC7) instead of
   recursing. Its constant and captured-member branches become unreachable and are removed.
6. **`All`** — rev 10's `CASE` form is withdrawn (rev 11, B1); each LINQ provider's `All` is as at `35c6477`.

### 5.3 Messages (the rejection rows assert these prefixes)

| Where | Message |
|---|---|
| D5: an item that reads the row other than through a property of the entity, or a row-independent item with a collection that reads the row | `Collection Contains is translated only as <property> IN (<values>), with a property of the entity as its item and a collection that doesn't read the row: <call>` |
| D5: a null collection | `The collection of Contains is null: <call>` |
| D5: a value that isn't enumerable | `Contains is translated only on a collection: <call>` |
| D6: a `StartsWith`/`EndsWith` receiver | `<Method>() is translated only on a property of the entity: <call>` |
| AC7: a string `Contains` receiver | `Contains() is translated only on a property of the entity, or on a value that doesn't read the row: <call>` |
| AC7, D16: a `ToString` that isn't a call on a property of the entity | `ToString() is translated only on a property of the entity; for a value that doesn't read the row, compute the string before the query: <call>` (rev 5–9: "… or on a captured value declared as an enum or a non-generic core type, read directly …") |
| D9 | `Could not evaluate expression: <node>` (with `InnerException`) |
| unchanged | a collection that reads the row with a property item: `Collection Contains with parameter references is not supported: …`; `.Value`: today's `NullableExpressionException` text |

---

## 6. Tasks

- **Task 0 — Test-plan review** (non-author): this plan's ACs, matrix, mutations and §1 claims, before any test or
  code. Rounds until CLEAN; dispositions in §9.
- **Task 1 — Core helper, red → green.** `CoreEvaluability` (the guard's predicate, copied verbatim from `ca82741`,
  with its source and sha cited in a comment) and `RowIndependentEvaluator` as a stub (members throw
  `NotImplementedException`), with `RowIndependentEvaluatorTests`; record the red run; implement; green on net8 and
  net48. Mutations M4–M8, M12, M13, M16, M23, M25, M28, M30–M33, M35–M37 and M39 (helper side).
- **Task 2 — Translator harnesses, red.** `TranslatorDirectHarness` and `CapturedValueHarness`, their four derived
  classes in their own projects, and the csproj links. Record the red run on all four providers against §4.2's
  "red at `35c6477`" column: every red row fails, every luck and AC10 row passes.
- **Task 3 — Translators, green.** §5.2 in all four providers. All four suites green, net48 included; coverage
  ≥ 85 % per touched file (§4.5); mutations M1–M39 executed and recorded.
- **Task 4 — Documents.** Changelog `[3.10.0-beta1]`: Fixed (I1-6 in WHERE, counts and projections; captured
  properties; the receivers and the dropped member; a row-independent string `Contains` answered as C# answers it;
  a captured `ToString(...)` sent as the string C# produces, format included; `x.Name.Contains(null)` no longer
  matches every row), Changed (the shapes that now throw `NotSupportedException`, a `ToString` the guard doesn't evaluate
  among them, with the three that work today (D14: an `object` or interface receiver holding a string, a `string` with
  an `IFormatProvider`) and the workaround; a captured `ToString()` evaluated in the current culture; a null search value matches no row, and `StartsWith`/`EndsWith` with one no longer throw; the
  evaluator interprets where the runtime can), New public API
  (`RowIndependentEvaluator`). `Usage.md`: the `IN` clause (a value that isn't a column is answered before the
  query) and the `LIKE` clause (what a search value can be, a null one, receivers must be entity properties).
  (Rev 10: the `ToString` entries are rewritten for D16, an `All` entry added, A5's corrections made; §6.1, Task 5.)
- **Task 5 — Hostile review** of `35c6477..HEAD` by a non-author, then fix-verification of every fix layer until
  CLEAN. The push sentinel is written only after a CLEAN pass over the exact sha.
- **Task 6 — The delete guard (D11).** When `development/3.10` holds the guard rewrite (rev 12 or later): merge it
  in, then reconcile the guard with this branch's translation — only what this branch's changes break; LIKE escaping and
  column `ToString` are `fix/like-escaping-tostring`'s (D15) — with the owner's sign-off on each behaviour change:
  - **`IsContainsOfNull`** (`DeletePredicateGuard.cs:405-412`) and its doc comment: its premise ("every provider sends
    `LIKE '%%'`") is false after D7/D8, which send `NULL LIKE NULL`. Proposed: remove it (the call folds as
    Unknown), so `x.Id == 2 || s.Contains(nullFilter)` deletes row 2 (AC9's `orCapturedContainsNull`). Its
    `DeletePredicateGuardTests` rows and the harness row `orContainsNull` move to the non-trivial set (survivors
    "1,3"), unless the owner keeps the rejection, in which case they stay with a corrected reason.
  - **The guard's documents**: its Changelog entries and `Usage.md` Warning that describe a captured string's
    `Contains` as sent as a `LIKE` between two parameters (the collation case), a null search value as `LIKE '%%'`,
    and a `ToString()` value as sent raw; `DELETE_GUARD_PLAN.md` §6 (I1-6 fixed; the collation bullet fixed for
    `Contains`); the tripwire's doc comment (`StartsWith`/`EndsWith` now throw `NotSupportedException`).
  - (Rev 10: the "one `ToString` rule" step is withdrawn. D16 rejects every captured `ToString` in the
    translator, so the guard keeps its own predicate and nothing is shared.)
  - Add the AC9 rows; run all four
    suites; fix-verification of the merge. Only then may the branch merge into `development/3.10`.

### 6.1 Task records

**Task 1 — Core helper (2026-10-03).** Files: `Funcular.Data.Orm.Core/Linq/CoreEvaluability.cs` (the guard's predicate,
copied verbatim from `ca82741`), `Funcular.Data.Orm.Core/Linq/RowIndependentEvaluator.cs`,
`Funcular.Data.Orm.SqlServer.Tests/Linq/RowIndependentEvaluatorTests.cs` (linked into the net48 project).
- **Red** (the members stubbed with `NotImplementedException`): 63 of 65 failed. The two that passed are expected:
  `Compile_InterpretingCompile_IsFound` (a field, not a stub) and `CoreEvaluability_MatchesTheGuardsTable` (the copied
  predicate, already in place).
- **Green:** 68 of 68 on net8.0; 61 of 61 on net48 (the `#if NET` span and `StringComparison` tests excluded), with
  the existing linked `QueryOperatorPolicyTests` (69 in all). The whole SqlServer.Tests suite: 953 passed (885 + 68).
- **Added while implementing** (author-found): `TryTranslateContains_StringInvokeThrows_InnerIsTheOriginal`, because
  M13 would otherwise survive (the interpreter surfaces a getter's exception unwrapped, so only the reflection `Invoke`
  path wraps); `RewriteForEvaluation_UnaryImplicitForm_Rewritten`, `_SpanNotFromAConversion_Unchanged` and a
  `CoreEvaluability` row for another instance call, for coverage.
- **Mutations** (each applied alone, the class's tests run, the source restored and checked): M4, M5, M5b (the
  nested lambda's parameters left declared after it), M6, M7, M8, M12, M13, M16, M23, M25, M28, M30, M31, M32, M33,
  M35, M36, M37, M39a (`Convert` dropped), M39b (a user conversion treated as core) — all 21 killed, each by the tests
  §4.4 names.
- **Coverage** (cobertura, SqlServer.Tests): `RowIndependentEvaluator.cs` 131/131, `CoreEvaluability.cs` 49/49.
- **Not verified:** .NET Framework 4.6.1–4.7.0 (no runtime here), where `InterpretingCompile` is null and the
  `Compile()` path runs; that path is covered on net8 by `Compile_WithoutInterpretation_Compiles`.

**Task 2 — Translator harnesses, red (2026-10-03).** Files: `Funcular.Data.Orm.SqlServer.Tests/CapturedValues/
TranslatorDirectHarness.cs` and `CapturedValueHarness.cs` (linked into PostgreSql.Tests, MySql.Tests and
Sqlite.Tests), and one `<Provider>CapturedValueTests.cs` per provider in its own project, each holding the direct
and the integration class. Per the house rule the red tests land in the same commit as Task 3's change.
- **Red** (Task 1 present, translators as at `35c6477`), failed / passed of 201 per provider: SQL Server 158 / 42
  (and the bare-`bool` projection inconclusive), SQLite 155 / 46, PostgreSQL 157 / 44, MySQL 159 / 42 (rev 10, A6:
  rev 9 said "of 200" and one fewer passed; `CollectionContains_StringAsCharSequence_NotSupported`, added for
  coverage, passes at red). Every test
  that passed is an AC10 pin or a row §4.2 marks luck: `andPresent`, `andNotPresent`, `andArrayPresent`,
  `notContainsNullValue`, `optionalFilterNull`, `optionalThisFilterNull`, `optionalReceiverNull` everywhere;
  `orCapturedToString05` and `orFormattedToString` on SQLite; `orLocalReceiverCase` on PostgreSQL. Every failure is
  the defect (wrong rows, a column such as `value` or `ids`, a raw `int` where a string belongs, `'%%'`, today's
  messages), none a harness error.
- **Deviation from §4.2:** the two `Sql_…` rows for `orCapturedToString05` and `orFormattedToString` aren't written: a
  logged parameter shows its value, not its type, so they couldn't fail. The red rows for SQLite's two luck rows are
  the direct harness's `ToString_CapturedLocal_EvaluatedString` and `_Formatted_EvaluatedString`, which assert a
  `string` parameter on every provider.
- **Found while writing it** (a defect on `development/3.10`, not this branch's): SQLite stores discovered column names
  under the type's short name (`ToDictionaryKey`) and reads them under its full name (Core's `GetCachedColumnName`),
  so a namespaced entity's un-attributed `NullableId` never finds `nullable_id` (`no such column`). SQLite's own test
  entities name every column, which hides it; `fix/provider-scoped-caches` fixes it (its Core lookup uses
  `ToDictionaryKey`). The harness entity names its columns, so it runs on either base.
- Added beyond §4.2 for coverage: `CollectionContains_LengthItem_NotSupported`,
  `CollectionContains_StringAsCharSequence_NotSupported` (the exit when no collection is found).

**Task 3 — Translators, green (2026-10-03).** The four `TranslateMethodCall`s are generated from one template, so they
differ only in the parameter type and the `LIKE` concatenation; `TranslateExpression` is replaced by `AppendColumn`
(column or throw) and `AppendParameter`.
- **Green:** the direct harness 75 / 75 and the integration harness 126 / 126 on each provider (SQL Server's
  bare-`bool` projection inconclusive by design). Whole suites: SQL Server 1153 passed and 1 inconclusive (885 at the
  baseline, 68 from Task 1, 201 from the harnesses), SQLite 989 (788 + 201), PostgreSQL 961 (760 + 201), MySQL 913
  (712 + 201). (Rev 10, A6: rev 9 and `d37f719`'s message said 1152/988/960/912, from 200 harness tests.)
- **Coverage** (cobertura, each provider's own project): SQL Server 185/186, SQLite 186/187, PostgreSQL 185/186,
  MySQL 185/186 lines (99.5 %, from 63.6 / 58.5 / 49.1 / 36.8 %); before `_StringAsCharSequence_` the one missed
  line was the no-collection exit. Core, from SqlServer.Tests: 131/131 and 49/49.
- **Mutations** (each applied alone, the provider's `CapturedValues` tests run, the failing tests read from a .trx so
  a data row is named with its arguments, the source restored and checked): on every provider M1, M9, M11, M14,
  M15, M20 (as M20v: the string branch's evaluation removed, since the parameter path it would fall back to no longer
  exists), M26, M30, M31, M35 and M38; on SQL Server also M2, M3, M10a (the empty short-circuit first), M10b = M34
  (the collection read before the item check), M17, M19, M21, M22 and M27; on SQLite also M2 and M24 (`LangVersion`
  removed from Sqlite.Tests). All 55 killed, and every row §4.4 names as a killer failed under its mutation. (M29 is
  withdrawn, D15.)

**Task 4 — Documents (2026-10-03).** Changelog `[3.10.0-beta1]`: six Fixed entries, a Changed block (the shapes that
now throw, the three that work today, the guarded-evaluation note) and `RowIndependentEvaluator` in the New public
API entry. `Usage.md`: the `IN` clause (an item that isn't a column, with an example; items that throw), the `LIKE`
clause (what a search value can be, the null rule and the optional-filter idiom, receivers, captured `ToString`), and
troubleshooting item 2 ("Could not evaluate expression").

**Task 5 — Hostile review and its fix layer (2026-10-03).** The non-author review of `35c6477..d37f719` found A1–A6
(§9.6): NOT CLEAN, A1 and A2 blocking. The owner's directive (D16) settled both. The fix layer, red first:
- **Red** (harness edits only, production as at `d37f719`): 55 of 208 failed on each provider, every failure
  the intended one: the evaluated `ToString` rows threw nothing, the five `All` rows returned true, the lazy
  collection leaked `DivideByZeroException`, and the rest failed only on §5.3's new message. `allIdPositive` and
  `allNotIdsX` passed, as pins should.
- **Green:** `CapturedValues` 208 of 208 on each provider (SQL Server's bare-`bool` projection inconclusive), then
  `ToString_Captured_NothingRead` added (209). `RowIndependentEvaluatorTests` 45 on net8.0 and 38 on net48 (68 and
  61 less the 23 deleted tests). Whole suites, before that last test: SQL Server 1137 passed and 1 skipped
  (1153 + 7 − 23), SQLite 996, PostgreSQL 968, MySQL 920 (+7 each); with it, SQL Server 1138 + 1 skipped,
  SQLite 997, PostgreSQL 969, MySQL 921.
- **Mutations**, each on all four providers, failing tests read from a .trx: M40 (`All` as `NOT (p)`), M41
  (3.9.0's raw value), M42 (rev 9's evaluation), M43 (enumeration unwrapped), M44 (the receiver read, then
  rejected) — all 20 killed, every killer §4.4 names among the failures.
- **Coverage** (cobertura): the four translators 187/187, 188/188, 187/187, 187/187 (100 %); the four LINQ
  providers 91.0 / 94.2 / 93.2 / 93.2 % (SQL Server, SQLite, PostgreSQL, MySQL); `RowIndependentEvaluator.cs`
  120/120 from SqlServer.Tests.

**FV1 — fix-verification of `6d8842d` and its fix layer (2026-10-03).** The non-author pass (§9.6) found B1–B4 and
N1–N3; `All`'s `CASE` form was B1, a fix-introduced regression. The layer, red first:
- **Red** (harness edits only, production as at `6d8842d`): 11 of 217 `CapturedValues` tests failed on each
  provider: the seven `All` rows `CASE` gets wrong, `WhereThenAll_…(allNullableNotEqual)`, and the three
  pass-through rows.
- **Green:** 217 of 217 on each provider (SQL Server's bare-`bool` projection inconclusive). The four LINQ
  providers are byte-identical to `d37f719` (and `35c6477`) again.
- **Whole suites:** SQL Server 1146 passed and 1 skipped, SQLite 1005, PostgreSQL 977, MySQL 929 (+8 each: two
  `All` rows net, three `WhereThenAll_…`, three pass-through). Coverage: the four translators 188/188, 189/189, 188/188,
  188/188; `RowIndependentEvaluator.cs` 120/120.
- **Mutations**, each on all four providers: M40r, M43, M45, M46, M47 — 20 of 20 killed, each by the rows §4.4 names.

**FV2 — fix-verification of `b4d614b` (2026-10-03).** B1–B4 and N1–N3 held (`All` at `b4d614b` equals `35c6477` on
every probed shape; the Known issue and its workaround executed on all four). C1–C4 answered in rev 12:
`CollectionContains_LazyExceptionWithNonDbInner_NotSupportedWithInner` (green at `b4d614b`: it pins behaviour a
mutation could lose); M48 and M49 each killed on all four providers by that row alone. The Changelog, D9 and AC8
name the pass-through.

**FV3 — fix-verification of `54aed0d`, and the cumulative branch (2026-10-03).** C2–C4 held; C1 held for the case it
named but overstated the rest (D1). No production code changed in `54aed0d` or in rev 13's commit; rev 13 is documents
and one test comment. FV3 found the cumulative branch otherwise clean (code, tests, Usage).

---

## 7. Risks

- **Expected conflicts** with `fix/delete-guard-trivial-predicates`, `fix/provider-scoped-caches`,
  `fix/like-escaping-tostring` (the four translators' `Contains`/`StartsWith`/`EndsWith`/`ToString` code) and the
  date-part branch: the `<ItemGroup>` link blocks in the MySql.Tests, PostgreSql.Tests, Sqlite.Tests, SqlServer.Tests and
  NetFramework csproj files; Changelog `[3.10.0-beta1]`; `Usage.md`; and (date part) the four translators'
  `TranslateDateMember`/visitor lines. Whichever lands second resolves them and re-runs both harnesses.
- **A double read with the delete guard.** The rewritten guard reads captured properties for its own classification
  (its §6), and evaluates string `Contains` and `ToString()`; D4 and D7 then evaluate the `Contains` again in the
  translator (a captured `ToString()` is rejected there from rev 10, D16).
  They agree unless a getter changes between the reads (the guard's §6 records the double read), with one exception
  until Task 6: the guard folds `s.Contains(null)` as true, while D7/D8 send it as UNKNOWN, so such a delete is
  rejected although its SQL would remove only what the rest of the predicate selects (it fails closed).
- **Evaluation cost.** A row-independent `Contains` and each captured value are interpreted once per translation; no
  code is generated.
- (Rev 10's `All` plan risk is gone with its `CASE` form, rev 11.)
---

## 8. Out of scope (recorded, not fixed here)

- **LIKE metacharacters in a search value aren't escaped** (N2): `x.Name.Contains("%")` sends `'%%%'`, and a delete
  with it removes every row (SQL Server and SQLite, executed). `_` and SQL Server's `[` behave alike. A separate
  data-loss defect: the owner placed its fix in 3.10.0 on its own branch and plan (decision 2026-10-02).
- **The visitor drops a member of a member of the parameter** (V1: `x.Name.Length != 4` sends `t.name != @p0`; SQLite
  deletes every row). The date-part plan owns it (D13): it rewrites the same `VisitMember` lines.
- **A collection holding null loses its nulls** (N1): `{1, null}.Contains(x.NullableId)` sends `IN (@p0)`, so rows
  whose column is null are missed (C# includes them). Fewer rows, not more; flagged to the owner.
- **A row-independent comparison is compared by the server**, with its collation (`x.Id == 2 || s == "s"`),
  as `DELETE_GUARD_PLAN.md` §6 records. D7 moves `Contains` into C# but leaves the comparison to the server; a
  captured `ToString()` is rejected (D16).
- **`ToString` on a column** is sent as the column, arguments dropped (`x.Id.ToString() == "05"` compares `t.id`
  with `'05'`); `fix/like-escaping-tostring` owns it (D15).
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
- **A captured `ToString()`** of any kind throws `NotSupportedException` (D16); evaluating some of them was rev
  5–9's D7. Shapes that returned the right rows in 3.9.0 break and are disclosed under Changed.
- **A `[NotMapped]` member of the parameter** as an item or receiver is still sent as a column (a server error).
- **`string.Contains(string, StringComparison)` with a column receiver** ignores the comparison; **`Enumerable.Contains(list,
  x.Code, comparer)`** ignores the comparer (the server's `IN` decides).
- **A non-enumerable `Contains` on a member of the parameter** (`x.Window.Contains(5)`) isn't translated; it now
  fails with D5's message (§5.3) instead of today's "parameter references".
- **A `bool` projection of a predicate on SQL Server** (`Flag = ids.Contains(x.Id)`, or any comparison) is a syntax
  error there, as today.
- **`All` over a predicate SQL finds unknown** (rev 11, B1): counted as satisfied, as in 3.9.0 — C#'s answer for `!=`
  and a negated comparison on a null, not for `>`/`==` (`All(x => x.NullableId > 0)` is true with a null, C# false)
  nor for D8's null search value (C# throws). The fix is C#-faithful null handling in the comparisons (`<>` on a
  nullable as `(c <> v OR c IS NULL)`, and the same under `NOT`), which also fixes WHERE's
  `Count(x => x.NullableId != 5)` (3 where C# says 4). Handed to the main session, which filed it as a task for the
  owner to start (new scope in the four `WhereClauseVisitor`s); a Changelog Known issue.
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

### 9.2 Task 0 re-review of rev 2 (`3ea7234`; non-author; the guard's source at `ca82741` executed on the new shapes, the UNKNOWN and `CASE WHEN` literals executed on all four servers, interpretation semantics executed, .NET Framework reference assemblies scanned)

Verdict: NOT CLEAN, 2 blocking. Rev 1's dispositions held except T0-1 (the `ToString` half), T0-10 and T0-12
(partly). New: AC-GAP 2, TEST-GAP 1, PLAN-GAP 4.

| # | Sev | Blame | Finding | Disposition (rev 3) |
|---|---|---|---|---|
| R2-1 | high | AC-GAP | D7 sent a captured `ToString()` receiver's raw value: `x.Id == 2 \|\| localInt.ToString() == "05"` deletes every row on SQL Server and MySQL today (N7), the property form would join it, and `ToString("D2")` drops its format (N4). | D7 (within the owner's T0-1 rule): a row-independent `ToString(...)` is evaluated in C#, arguments included, and sent as its string; a column `ToString` with arguments fails closed (D3). `TryEvaluateToString`; rows `orCapturedToString05`, `orPropertyToString05`, `orFormattedToString`, `columnToStringFormatted`; M25, M26, M29; §8's example corrected. |
| R2-2 | medium | TEST-GAP | The guard is at rev 12; its `IsContainsOfNull` folds `s.Contains(null)` as true on the premise `LIKE '%%'`, which D7/D8 make false; its documents describe the old translation; Task 6 didn't cover them. | §1.2 cites rev 12; Task 6 lists `IsContainsOfNull`, its tests, `orContainsNull` and the documents, with the owner's sign-off; AC9 row `orCapturedContainsNull`; §7 and D7's sentence corrected. |
| R2-3 | low | PLAN-GAP | D5's new order rejects `x.Tags.Contains(5)` with D5's message, contradicting the test that expected today's, and §8's Window bullet. | The test uses `x.Tags.Contains(x.Id)`; `_RowIndependentItemRowCollection_NotSupported` pins D5's message; §8 corrected. |
| R2-4 | low | PLAN-GAP | `nullCollection` already throws `NotSupportedException` today (luck); PostgreSQL's `orLocalReceiverCase` delete is luck too. | Rejection rows assert §5.3's prefixes, so `nullCollection` is red; the PostgreSQL delete is marked luck with its red row. |
| R2-5 | low | PLAN-GAP | `Compile(bool)` is missing on .NET Framework 4.6.1–4.7.0. | D10: found by reflection, `Compile()` otherwise; the `Compile` seam and its two tests; M28. |
| R2-6 | low | AC-GAP | A negated null value wasn't pinned. | D8 and AC8 state it; `notContainsNullValue` and its SQL row; M27. |
| R2-7 | nit | PLAN-GAP | (a) `SupportsPredicateAsValue` read as provider API; (b) D8's delete rows are unknown, not false. | (a) a harness virtual, §4.1; (b) §4.1's wording. |

### 9.3 Task 0 re-review of rev 3 (`1e990f3`; non-author; string-parameter comparisons executed on all four servers, interpreted `Nullable` `ToString`, de-DE culture and `Convert.ToString` shapes executed, today's behaviour of the new rows executed on all four × five paths)

Verdict: NOT CLEAN, 1 blocking. All seven R2 dispositions held. New: TEST-GAP 3, PLAN-GAP 3.

| # | Sev | Blame | Finding | Disposition (rev 4) |
|---|---|---|---|---|
| R3-1 | medium | TEST-GAP | A null `ToString()` receiver sent as `NULL` contradicts C#: `((int?)null).ToString()` is `""`, a null string throws; `x.Id == 2 \|\| n.ToString() == null` selects and deletes every row (executed). | D7: the whole call is evaluated, so `""` and D9; the null-receiver tests replaced; rows `orNullableToStringIsNull`, `orNullableToStringIsEmpty`, `nullStringToString`; M31; §8 corrected. |
| R3-2 | low | TEST-GAP | The interpreting seam was tested alone; a call site bypassing it passed every test (pattern 21). | The settable `Compiler`; `_UsesTheCompiler` per member; `Compile_OnlyInTheSeam`; M33. |
| R3-3 | low | PLAN-GAP | A static `ToString` (`Convert.ToString`) would leak `ArgumentException` or reach `TranslateExpression(null)`. | `TryEvaluateToString` returns `false` for it; the translator throws §5.3's message; `staticConvertToString`, `ToString_Static_NotSupported`, `TryEvaluateToString_Static_ReturnsFalse`. |
| R3-4 | low | TEST-GAP | "The string C# produces" depends on the culture; nothing pinned it. | D7 states it; `TryEvaluateToString_CurrentCulture`; M32; Task 4's `Usage.md` states it. |
| R3-5 | low | PLAN-GAP | Evaluating a user type's `ToString` sends a comparison neither guard folds: a delete-every-row path. | D7 evaluates only what the guard evaluates (its `IsEvaluableCall`); the rest fails closed; rows `userTypeToString`, `objectDeclaredToString`, `providerToString`; M30; Task 6 shares one predicate; §8. |
| R3-6 | nit | PLAN-GAP | (a) a field item threw after the collection read; (b) a captured non-enumerable got "is null"; (c) the receiver message said "without arguments" for `Contains`; (d) "as today" for a changed message. | (a) §5.2 item 3, `_FieldItem_InvalidOperationBeforeCollectionRead`, M34; (b) and (c) §5.3; (d) §5.2 item 2. |

### 9.4 Task 0 re-review of rev 4 (`0755b99`; non-author; `ToString` tree shapes and rev-12 `Classify` on arithmetic, indexer, method-call, `??`, int and `object` receivers executed; today's behaviour of object/provider `ToString` in WHERE and projections executed on all four × five paths)

Verdict: NOT CLEAN, 2 blocking. R3-1…R3-4 and R3-6 held; R3-5 partly. New: AC-GAP 1, TEST-GAP 2, PLAN-GAP 1.
Third fix-introduced round on `ToString` in a row: rev 5 redesigns the rule instead of patching it (test-first-delivery).

| # | Sev | Blame | Finding | Disposition (rev 5) |
|---|---|---|---|---|
| R4-1 | medium | TEST-GAP | D7 copied the guard's call check but not its recursive check of the receiver and arguments: `x.Id == 2 \|\| (five + 1).ToString() == "6"` (and the indexer, method-call, `??` forms) would delete every row, and the guard returns Acceptable (executed). | One predicate: `CoreEvaluability`, the guard's `IsEvaluable` copied verbatim; the guard adopts it at Task 6. Rows `orArithmeticToString`, `orIndexToString`, `orMethodToString`, `orCoalesceToString`, `_ConcatenatedFormat_*`; M35. |
| R4-2 | medium | AC-GAP | Failing closed regresses `objStr.ToString()` and `s.ToString(CultureInfo.InvariantCulture)`, which return correct rows today in WHERE and projections. | Owner: strict (D14). Listed under Changed with the workaround; D7's "today each fails" corrected; rows `objectHoldingStringToString`, `providerStringToString`, `labelObjectToString`, `labelProviderToString`; M38. |
| R4-3 | low | TEST-GAP | No row isolated the core-library, `!IsGenericType` or nullable-struct conditions. | `orStructToString`, `orNullableStructToString`, `orSealedClassToString`, `orPairToString` and their `TryEvaluateToString_*` rows; M36, M37. |
| R4-4 | nit | PLAN-GAP | (a) "declared type" means `call.Object.Type`; (b) the source scan must prove it found the file and the seam; (c) `TryReadValue_UsesTheCompiler` needs a member chain. | (a) D7; (b) and (c) §4.2. |

Also in rev 5, outside the review: D15 (the owner's split with `fix/like-escaping-tostring`) withdraws rev 3's column-
`ToString(format)` rule (D3, AC7, `columnToStringFormatted`, `ToString_ColumnWithArguments_NotSupported`, M29).

### 9.5 Task 0 re-review of rev 5 (`6d06445`; non-author; rev-12 `Classify` on conditional, cast, enum-format, interface, pair and struct receivers executed; today's behaviour of the R4 rows and an interface-held string executed on all four × five paths)

Verdict: NOT CLEAN, none blocking. R4-1…R4-4 and D15's withdrawal verified. New: TEST-GAP 1, PLAN-GAP 3.

| # | Sev | Blame | Finding | Disposition (rev 6) |
|---|---|---|---|---|
| R5-1 | low | PLAN-GAP | §5.3's `ToString` message ("a value of a core or enum type that doesn't read the row") reads as supporting what D14 rejects. | Reworded. |
| R5-2 | low | PLAN-GAP | A third shape working today breaks under D14: an interface-typed receiver holding a string. | Disclosed in D7, D14, AC6 and Task 4; row `interfaceHoldingStringToString`; M38. |
| R5-3 | nit | PLAN-GAP | `CoreEvaluability.cs` missing from §4.5. | Added. |
| R5-4 | nit | TEST-GAP | No evaluated cast receiver; dropping `Convert` from the copy would pass. | `TryEvaluateToString_CastReceiver_Evaluated`, `_UserConversionReceiver_ReturnsFalse`; M39. |

Fix-verification of rev 6 (`fa7ed72`; non-author): R5-2…R5-4 closed, R5-1 mostly; two wording nits, F6-1 (§8 said
"two" shapes) and F6-2 (§5.3's message didn't say "declared as" or "non-generic"), fixed in rev 7. Its verification of
rev 7 (`33b96cf`; non-author): **CLEAN** — Task 0 is complete.

### 9.6 Task 5 hostile review of `35c6477..d37f719` (non-author; old-vs-new probe on all four providers × Query, Count, Any, All, projection and Delete; mutations re-executed on a `git archive` copy against the DB-backed suites)

Verdict: NOT CLEAN, A1 and A2 blocking (A5 too, as the push gate counts user docs). Blame: AC-GAP 1, TEST-GAP 4,
OTHER 1. The core fix (I1-6, captured properties, receivers, D4/D5/D7/D9) was verified on all four.

| # | Sev | Blame | Finding | Disposition (rev 10) |
|---|---|---|---|---|
| A1 | medium | TEST-GAP | `All` is `NOT EXISTS(… WHERE NOT (p))`, so a row whose `p` is UNKNOWN counts as satisfying it: `All(x => !x.Name.Contains(nullFilter))` went from false to true; the D8 shapes return true against AC8 ("no row, negated or not"). No `All` row. | Fixed (D16, owner: fix `All`): `CASE WHEN (p) THEN 1 ELSE 0 END = 0` in the four LINQ providers, which also fixes `All` over a nullable column. `All_TreatsUnknownAsFailing` (7 rows); M40. Changelog Fixed entry. (Superseded: FV1-B1, rev 11 — reverted to `NOT (p)`; a Known issue.) |
| A2 | medium | AC-GAP | An evaluated captured `ToString()` compared with a column's raw `ToString()` changed type: `x.Kind.ToString() == one.ToString()` went from {1} to a SQL Server conversion error, MySQL's wrong row {4}, SQLite {}; PostgreSQL `x.Id.ToString() == n.ToString()` went from {1} to 42883. | Rejected (D16): no captured `ToString` is evaluated; the shape throws `NotSupportedException` before anything is sent. AC6 amended. Rows `intColumnToStringEqCaptured`, the moved evaluated rows, `labelFormatted`; M41, M42, M44. The {1} that 3.9.0 returned is disclosed under Changed. |
| A3 | low | TEST-GAP | A lazy collection that throws while enumerated (`ids.Select(i => i / zero)`) leaked a bare `DivideByZeroException`: the enumeration was outside D9's wrapper. | Fixed: enumeration wrapped per D9. `CollectionContains_LazyThrowingCollection_NotSupportedWithInner`; M43. |
| A4 | low | TEST-GAP | Removing `node.Type == typeof(string) &&` from the `ToString` branch survived: a user `new object ToString()` would leak `ArgumentException`. | The guard went with the evaluation (D16). `ToString_NonStringReturn_NotSupported` pins the rejection; M41 kills it. |
| A5 | low | TEST-GAP (AC12) | Docs said things the code doesn't: (a) "declared as an enum or a non-generic core type" omits sealed/value; (b) locals and constants were said to be "translated as the entity's column" (they threw); (c) a null collection "was `NullReferenceException`" (for a property of a captured object it was `NotSupportedException`); (d) `emptyIds.Contains(x.NullableId.Value)` going from {} to `NullableExpressionException` was missing; (e) Usage's "as C# would" for a row-independent `Contains` hides that a null receiver gives no row where C# throws. | (a) moot: the rule and its message are gone (D16). (b), (c) corrected in Changelog; (d) added under Changed; (e) added to Usage, and to the Changelog's string-`Contains` entry. |
| A6 | nit | OTHER (record) | §6.1 and `d37f719`'s message said "of 200" and 1152/988/960/912; executed: 201 per provider and 1153 + 1 / 989 / 961 / 913; red passed 42/46/44/42. | Corrected in §6.1 (the commit message stays as written). |

Propagation: A1 and A3 are their rows; A2's class (an AC that permitted a type change) is closed by D16's rule rather
than a narrower AC; A5's corrections are in the documents.

FV1 — fix-verification of `d37f719..6d8842d` (non-author; `All` probed at `35c6477`, `d37f719` and `6d8842d` on all
four providers against LINQ to objects, with null `code`, `nullable_id` and a LEFT JOIN miss; the rejection, the
lazy wrap and the Changelog's 3.9.0 claims executed; M40–M44 re-run on three providers, plus its own mutations).
Verdict: NOT CLEAN, B1 and B2 blocking. Blame: AC-GAP 2, TEST-GAP 2, PLAN-GAP 1 (N1), OTHER 2 (N2, N3).

| # | Sev | Blame | Finding | Disposition (rev 11) |
|---|---|---|---|---|
| B1 | medium | AC-GAP (fix-introduced, A1's) | `CASE WHEN (p) … = 0` made `All` false where C# and 3.9.0 say true when a null makes `p` true in C# but unknown in SQL: `x.NullableId != 5`, `x.Code != "zz"`, `!(x.Code == "zz")`, `!(x.NullableId > 100)`, a ternary `!=`, a `[RemoteProperty]` `!=` over a LEFT JOIN miss — all four providers. | Reverted to `NOT (p)` (3.9.0); the other direction (`> 0`, D8) is a Known issue, handed to the main session (§8, D16). `All_UnknownCountsAsSatisfied` pins both directions; M40r. |
| B2 | low | TEST-GAP (AC12) | Changelog: "also with an enum" — at `35c6477` PostgreSQL threw (`InvalidCastException`) on the enum form. | "also with an enum, except on PostgreSQL, where that threw". |
| B3 | low | AC-GAP (fix-introduced, A3's) | The enumeration wrap also wrapped a FunkyORM `IQueryable` collection's server error (`DbException`, or SQL Server's `InvalidOperationException` around one) and its own `NotSupportedException`, hiding them from retry policies and callers. | A `DbException`, an exception with one inside, and a `NotSupportedException` pass through; three direct rows; M45, M46. |
| B4 | low | TEST-GAP | FV1's mutation (`NOT (p)` only when a `Where` precedes) survived the whole SQL Server suite: no `Where(…).All(…)` row. | `WhereThenAll_MatchesCSharp`; M47 (the inverse, now that `NOT (p)` is the translation). |
| N1 | nit | PLAN-GAP | §4.4 had no M44 row; §4.2 didn't name `ToString_Captured_NothingRead`. | Added. |
| N2 | nit | OTHER (comment) | `TranslateMethodCall`'s doc comment said every row-independent part is evaluated. | Reworded (×4): a `Contains` or a value; a captured `ToString()` is rejected. |
| N3 | nit | OTHER (doc) | Usage's `ToString` sentence read as global; ORDER BY ternaries evaluate a captured `ToString()` in their own visitor (correctly). | Scoped to "in a predicate or a projection". |

FV2 — fix-verification of `6d8842d..b4d614b` (non-author; FV1's `All` shapes re-run at `b4d614b` against
`35c6477` on all four; the Known issue and its workaround executed; a dropped and a cold-missing table under a query
used as the collection; M40r, M43, M45–M47 re-run on SQL Server and MySQL plus OWN-10…13). Verdict: NOT CLEAN, C1 and
C2 blocking (small). Blame: AC-GAP 1, TEST-GAP 1, PLAN-GAP 1, OTHER 1.

| # | Sev | Blame | Finding | Disposition (rev 12) |
|---|---|---|---|---|
| C1 | low | AC-GAP (fix-introduced, B3's) | The Changelog (and D9, AC8) still said a throwing lazy collection always gives "Could not evaluate expression"; at `b4d614b` a query collection's database error and a `NotSupportedException` pass through. | One clause in the Changelog; D9 and AC8 amended. |
| C2 | low | TEST-GAP (fix-introduced, B3's) | OWN-10 (every `InvalidOperationException` passes) and OWN-11 (anything with an inner exception passes) survived: nothing pinned that a non-database exception with an inner one is still wrapped. | `CollectionContains_LazyExceptionWithNonDbInner_NotSupportedWithInner`; M48, M49. |
| C3 | nit | OTHER (doc) | The unscheduled `All` entry sat under "Known issues (fixes planned for 3.10.1)". | Heading "Known issues"; "(fixes planned for 3.10.1)" moved to the aggregates lead-in (the sibling branch's form). |
| C4 | nit | PLAN-GAP (record) | §9.6's A1 row still read "Fixed". | Points to FV1-B1. |

FV3 — fix-verification of `b4d614b..54aed0d`, with a final pass over `35c6477..54aed0d` (non-author; the pass-through
sentence probed on both `Contains` paths with a query collection on a dropped table, all four providers; M48 and M49
re-run on three providers; suites re-run). Verdict: NOT CLEAN, D1 blocking (documents); cumulative otherwise clean.
Blame: AC-GAP 1, OTHER 3.

| # | Sev | Blame | Finding | Disposition (rev 13) |
|---|---|---|---|---|
| D1 | low | AC-GAP (fix-introduced, C1's wording, which FV2 proposed) | "A database error … from a query used as the collection passes through unchanged, as in 3.9.0" is false for a row-independent `Contains` (`q.AsEnumerable().Contains(5)`): `RowIndependentEvaluator` reads the collection and wraps it, where 3.9.0 threw it raw. | Documents narrowed (Changelog, D9, AC8): the pass-through holds when the item is an entity property; a collection read for a row-independent `Contains` is wrapped like any value. No code change (D16: no new shape accommodated). |
| D2 | nit | OTHER (doc) | The Changed lead-in said every bullet throws `NotSupportedException`; the `.Value` bullet throws `NullableExpressionException`. | "(unless noted)". |
| D3 | nit | OTHER (comment) | The C2 row's comment said only a database error passes through. | "a database error or a NotSupportedException". |
| D4 | nit | OTHER (doc) | A user lazy collection's own `NotSupportedException` comes out raw; the main sentence implied a wrap. | Folded into D1's sentence ("a `NotSupportedException` thrown while the collection is enumerated"). |
