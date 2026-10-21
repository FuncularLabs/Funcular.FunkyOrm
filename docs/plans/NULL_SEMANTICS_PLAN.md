# FunkyORM 3.12.0 — null comparisons in WHERE that answer as EF Core does (plus riders)

> **Status:** rev 1, 2026-10-20. Task 0, the review of this plan's tests before any code, is next.
> **Branch:** `development/3.12.0`, cut from master `25956e0` (3.11.0).
> **Owner directive (Paul, 2026-10-20):** "for #2, attempt to match EF behavior". Item #2 covers two things: nullable
> `!=` and a comparison with a null variable, and the `is IOrderedQueryable` sort helper (#16).
> **Riders requested in the same message:**
> - the `[SubqueryAggregate]` conditional-count examples that don't compile;
> - PostgreSQL and SQLite in CI;
> - withdrawing the never-built `[ReadModel]` proposal.
>
> **Version:** 3.12.0, a minor release. The fix makes some queries return more rows, and some deletes remove more
> rows (BACKLOG WP-H; the 3.11.0 audit's FD-08: "fix in a minor version, because the fix makes deletes wider").

## 1. The problem

FunkyORM 3.11.0 translates a comparison into SQL as written. SQL's three-valued logic then drops rows that C# and EF
Core keep.

These figures are from the published 3.11.0, run on SQL Server against the tutorial sample's 100,000 orders, of which
2,960 have no ship-to address:

| Predicate | FunkyORM 3.11.0 | C# / EF Core |
|---|---|---|
| `o.ShipToAddressId != a` | 97,016 rows (`ship_to <> @p`; the NULL rows are dropped) | 99,976 |
| `o.ShipToAddressId == none` (a variable holding null) | 0 rows (`= @p` with NULL) | 2,960 |
| `o.ShipToAddressId == null` (a literal) | 2,960 | 2,960 |

The same holds under every `!`. `!(x.N > 5)`, `!ids.Contains(x.N)` and `!x.Name.Contains("a")` all drop the NULL
rows, and so does `All`, which 3.11.0 lists as a Known issue. A collection holding null drops the null from
`IN (…)` (backlog Appendix A #18).

A `Delete` with any of these shapes deletes fewer rows than C# says it should.

## 2. The oracle: what EF Core sends

The reference is EF Core 10.0.12, with its default C# null semantics (`UseRelationalNulls(false)`). The probe used
`ToQueryString()`, so no database was needed. The SQL Server and SQLite translations carry the same semantics. The
probe source is outside the repo (`Documents\FunkyORM\quality-reports\2026-10-20-evidence\ef-null-semantics-probe-Program.cs`).

In the table, `N` and `M` are `int?`, `Name` is a nullable `string`, `Req` is a required `string`, `Id` is an `int`,
`v` holds 5, `none` holds null, and `a` holds `"a"`.

| C# | EF Core's WHERE |
|---|---|
| `N != v`, `N != 5` | `N <> @v OR N IS NULL` |
| `N == v` | `N = @v` |
| `N == none` / `N != none` | `N IS NULL` / `N IS NOT NULL` |
| `N == M` | `N = M OR (N IS NULL AND M IS NULL)` |
| `N != M` | `(N <> M OR N IS NULL OR M IS NULL) AND (N IS NOT NULL OR M IS NOT NULL)` |
| `N == Id` / `N != Id` | `N = Id` / `N <> Id OR N IS NULL` |
| `!(N == v)` / `!(N != v)` | `N <> @v OR N IS NULL` / `N = @v` |
| `!(N > v)`, `!(N >= v)`, `!(N < M)` | `CASE WHEN N > @v THEN 0 ELSE 1 END = 1` (true when the comparison is false or unknown) |
| `N > v`, `N > M` | as written |
| `!(N == 5 && Name == a)` | `N <> 5 OR N IS NULL OR Name <> @a OR Name IS NULL` (De Morgan, then the rules above) |
| `!(N == 5 \|\| Name == a)` | `(N <> 5 OR N IS NULL) AND (Name <> @a OR Name IS NULL)` |
| `ids.Contains(N)` / `!ids.Contains(N)` | `N IN (…)` / `N NOT IN (…) OR N IS NULL` |
| `{1, null}.Contains(N)` / negated | `N IS NULL OR N = 1` / `N IS NOT NULL AND N <> 1` |
| `!intIds.Contains(Id)` | `Id NOT IN (…)` |
| `Name != a` / `Req != a` | `Name <> @a OR Name IS NULL` / `Req <> @a` |
| `Name == noName` / `!=` | `Name IS NULL` / `Name IS NOT NULL` |
| `Flag != true` (`bool?`) | `Flag <> 1 OR Flag IS NULL` |
| `!Name.Contains(a)`, `!Name.StartsWith(a)` | `Name NOT LIKE … OR Name IS NULL` |
| `Name.Contains(noName)` / `!Name.Contains(noName)` | `0 = 1` / no WHERE: every row |
| `D.Value.Year != 2020`, `Name.Length != 3` | `DATEPART(year, D) <> 2020 OR D IS NULL`, `LEN(Name) <> 3 OR Name IS NULL` |
| `(N > 3 ? 1 : 0) != 1`, `(N ?? 0) != 5` | as written (the operand is never null) |
| `All(N > 0)` | `NOT EXISTS (… WHERE CASE WHEN N > 0 THEN 0 ELSE 1 END = 1)` |

EF Core also translates `N.HasValue` (`IS NOT NULL`) and `N.Value` (the column). FunkyORM rejects both on purpose,
with `NullableExpressionException` (§3).

**#16:** EF Core behaves as FunkyORM does.
- `context.Rows.Where(…)` is an `EntityQueryable<T>`, which implements `IOrderedQueryable<T>`.
- So the helper `q is IOrderedQueryable<T> o ? o.ThenBy(k) : q.OrderBy(k)` throws System.Linq's
  `ArgumentException` ("Expression of type 'System.Linq.IQueryable`1[Row]' cannot be used for parameter of type
  'System.Linq.IOrderedQueryable`1[Row]' …").
- EF Core's root `DbSet<T>` isn't ordered, so the helper takes `OrderBy` there. FunkyORM's root is ordered and takes
  `ThenBy`, which also works.

So "match EF" for #16 means keeping today's behavior and pinning it (R1).

## 3. Scope

**In scope:**
- Every WHERE that goes through the shared translation: `Query(predicate)`, `QueryAsync`, `Query<T>().Where`,
  `Count`/`LongCount`/`Any`/`All(predicate)`, and `Delete`/`DeleteAsync`.
- All four providers.

**Out of scope, and why:**
- **SELECT projections and ORDER BY keys.** They translate comparisons through other visitors. EF compensates there
  too, but a predicate used as a value or a sort key needs its own design, so it's a follow-up. Positive collection
  `Contains` with a null item does change there too (R-C1), because the translator is shared.
- **`.HasValue` and `.Value` on a nullable property.** FunkyORM rejects them by design, with a message naming
  `== null` / `!= null`. It's a loud difference from EF, not a wrong result. Unchanged.
- **Arithmetic and `??` in WHERE.** Neither is translated today; both throw `NotSupportedException`. Unchanged.
- **Which string columns are nullable.** FunkyORM has no model. Every reference-type column (a `string`, a `byte[]`)
  counts as possibly null. A required string column therefore gets an `OR col IS NULL` that EF would leave out: the
  same rows, more SQL text. Reading nullable-reference-type metadata differs by target (no `NullabilityInfoContext`
  on netstandard2.0/net48), so that refinement is out.
- **The SQLite `Guid`/`DateTime` binding (FD-11).** Unchanged.
- **The all-rows guard's verdicts.** `WherePredicateGuard` judges the predicate as written, before translation;
  unchanged. Its after-translation literal check now also sees the `NOT 1=0` that AC-N8 sends.

## 4. Acceptance criteria

"Can be null" means the operand's SQL value can be NULL:
- a property of `Nullable<T>` or reference type;
- a `[RemoteProperty]` or `[RemoteKey]` (a LEFT JOIN), or a `[JsonPath]`, `[SqlExpression]` or `[SubqueryAggregate]`
  that isn't a `Count`/`ConditionalCount`, whatever its CLR type;
- a date part or `Length` of such a property;
- a conditional with a branch that can be null;
- a `Convert` of any of these.

A value "doesn't read the row" when the shared evaluator can read it before the query (`RowIndependentEvaluator`):
a constant, a captured or static member, a nested member.

| AC | Criterion |
|---|---|
| **AC-N1** | `x.P != v`, with P able to be null and v a non-null value that doesn't read the row, also selects the rows where P is null. Either operand order. The IS NULL test is on P itself, or on the underlying property for a date part or `Length`. |
| **AC-N2** | `x.P == v` / `x.P != v`, with v null at translation (a captured, nested, static or constant null), sends `P IS NULL` / `P IS NOT NULL`. Either operand order. |
| **AC-N3** | Column against column: both able to be null, `==` also matches two NULLs and `!=` is EF's form (§2). Only one able to be null: `!=` also selects that one's NULL rows, and `==` is unchanged. |
| **AC-N4** | `!` reaches the comparisons: De Morgan over `&&` and `\|\|`, `!(a == b)` as `a != b`, `!(a != b)` as `a == b`, `!!p` as `p`, then AC-N1..N3. A negated `>`, `>=`, `<` or `<=` with an operand that can be null also selects the rows where it is null. One with a null value that doesn't read the row selects every row. |
| **AC-N5** | A `>`, `>=`, `<` or `<=` with a null value that doesn't read the row selects no row, as C#'s lifted comparison does. Same rows as 3.11.0. |
| **AC-N6** | Collection `Contains`: a null item also selects the NULL rows (`P IN (…) OR P IS NULL`; with only nulls, `P IS NULL`). Negated, with no null item and P able to be null, it also selects the NULL rows; with a null item it excludes them (`P IS NOT NULL`). An empty collection stays `1=0` / `NOT 1=0`. A null item can't match a column that can't be null. |
| **AC-N7** | `!x.P.Contains(v)`, `!x.P.StartsWith(v)` and `!x.P.EndsWith(v)` also select the rows where P is null. |
| **AC-N8** | A null search value: `x.P.Contains(null)` selects no row, as in 3.11.0. **Negated, it selects every row**, as EF does, sent as `NOT 1=0`, so a delete of it alone is refused as always true. This amends 3.11.0's D8 ("no row, negated or not") per the owner's directive. Same for `StartsWith` and `EndsWith`. |
| **AC-N9** | `All(p)` is true when no row satisfies `!p` under AC-N1..N8. `All(x => x.N > 0)` is false when an `N` is null (the 3.11.0 Known issue, fixed). `All(x => x.Name.Contains(null))` over a non-empty table is false. |
| **AC-N10** | One path. For one predicate, Query, QueryAsync, Where, Count and Delete send the same WHERE text and parameters, and select, count or delete the oracle's rows (§2), on all four providers. Any and All answer as the oracle. A delete the all-rows guard refused in 3.11.0 is refused with the same message, and a delete it accepted is accepted. |
| **AC-N11** | No change where nothing can be null. A predicate whose operands can't be null in SQL sends exactly 3.11.0's SQL: no `IS NULL`, no added `OR`, and a `NOT` kept where 3.11.0 sent one. Pinned on representative shapes (§6.2). |
| **AC-N12** | The documents say what the code does: Changelog `[3.12.0]` (a Behavior change listing every shape that now selects or deletes more rows, Fixed, Known issues), `Usage.md`, `Advanced.md` §5, and the AI instructions. |
| **AC-R1** | #16 matches EF Core. A composed FunkyORM query is an `IOrderedQueryable<T>`, as EF Core's is. The `is IOrderedQueryable` helper throws the same `ArgumentException` (type, and message apart from the type names). The documented check `q.Expression.Type == typeof(IOrderedQueryable<T>)` picks `OrderBy` on the root and after `Where`, and `ThenBy` after `OrderBy`. Pinned and documented. No code change. |
| **AC-R2** | Every FunkyORM attribute usage in the shipped Markdown names only real things. `name:` arguments are constructor parameters, and `Name =` assignments are settable properties. The `[SubqueryAggregate]` examples in `Usage.md` and the AI instructions are fixed to `ConditionColumn = …, ConditionValue = …`. |
| **AC-R3** | CI runs the SQL Server, SQLite, PostgreSQL and MySQL suites on pushes to `master`, `development/**` and `release/**`, and on pull requests to `master`. The NuGet `publish` job needs all four to pass. |
| **AC-R4** | No FunkyORM document or plan offers a `[ReadModel]` attribute. Two plan lines are amended. |
| **AC-R5** | Version 3.12.0 in the five library csprojs (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`). Changelog `[3.12.0]`, and the README banner. |

## 5. Design

### 5.1 One rewrite in Core: `Funcular.Data.Orm.Linq.NullSemantics`

`NullSemantics` is a public static class, like `RowIndependentEvaluator`, `LikePattern` and `AggregateSemantics`. A
custom provider can call it.

**`Rewrite(LambdaExpression predicate, IDictionary<Expression, bool?> memo)`** returns a body that has C#'s value for
every row, built only from shapes the four visitors already translate: `==`/`!=` with a literal `null` (IS
[NOT] NULL), `&&`, `||`, `!`, and the original leaves. The recursion carries a polarity:
- **A truth position that folds.** `WherePredicateGuard.Fold` runs first, with the visitor's memo. A known truth
  becomes a `bool` constant (negated under `!`). Every fold 3.11.0 sent stays a fold.
- **`!e`:** rewrite `e` with the polarity flipped.
- **`&&` / `||`:** combine the rewritten operands, swapping the operator under negation (De Morgan).
- **`==` / `!=`:** under negation the operator flips. `Expression.Equal`/`NotEqual` re-resolve the operator method
  for `string`, `decimal` and `DateTime`. Then the operands are classified (§4's definitions):
  - Both sides don't read the row: left as is (`Fold` saw them).
  - A null that doesn't read the row: `row == null` or `row != null`, with a `Convert` to `Nullable<T>` when the
    row side is a non-nullable value type (AC-N2).
  - A non-null value that doesn't read the row, and the row side can be null: `!=` becomes `(cmp) || W == null`,
    where W is the witness (the property under a date part, `Length` or `Convert`; the conditional itself).
    `==` is unchanged (AC-N1).
  - Both sides read the row: EF's forms (AC-N3).
  - A side whose value can't be read: left as is, so the visitor raises its usual error.
  - A side that is itself a predicate, such as `x.Name.Contains("a") == true`: left as is (pre-existing dialect
    behavior).
- **`>`, `>=`, `<`, `<=`:**
  - A null that doesn't read the row: the constant `false`, or `true` under negation (AC-N4, AC-N5).
  - Under negation: `!(cmp) || W1 == null [|| W2 == null]`, one test for each operand that can be null.
  - Otherwise unchanged.
- **Other leaves** (a method call, a `bool` member, a conditional): unchanged, wrapped in `!` under negation. A
  negated method call is answered by the translator (§5.2).

**AC-N11 by construction.** A subtree is rewritten only when a leaf under it needs compensation. Otherwise the
original node is returned, `!` included, so the visitor sends 3.11.0's SQL.

**`CanBeNull(PropertyInfo)`** is public and shared by the rewrite and the translators. It implements §4's definition.

### 5.2 The translators answer a negated method call

Each `*ExpressionTranslator.TranslateMethodCall` gains an overload with `bool negated`. The existing signature
calls it with `false`, so nothing breaks.
- **String `Contains`/`StartsWith`/`EndsWith` on a column:**
  - negated: `(NOT col LIKE … ESCAPE '!' OR col IS NULL)`;
  - negated with a null value: `NOT 1=0`;
  - positive, unchanged: `col LIKE …`, and `col LIKE NULL` for a null value (D8's positive half).
- **Collection `Contains`:** the collection is read once, as today, keeping a flag for a null item. The SQL follows
  AC-N6.
- **A row-independent `Contains`** (`TryTranslateContains`): `NOT ` + the literal.

Each WHERE visitor's `VisitUnary(Not)` sends a `bool` method-call operand to the overload with `negated: true`,
instead of writing `NOT ` before it.

### 5.3 The visitors and `All`

Each WHERE visitor's `Visit(lambda)` visits `NullSemantics.Rewrite(lambda, _truths)` instead of `lambda.Body`.
Each LINQ provider's `All` translates `x => !p`, a new lambda over the same parameter, through the visitor, and no
longer wraps the SQL in `NOT (…)`.

### 5.4 What doesn't change

`WherePredicateGuard` keeps its classification, messages and memo semantics. The parity harness's assertions are
unchanged; the predicates it runs grow. Parameter names change only where a null value used to become a parameter.

## 6. Test plan

### 6.1 The AC → test matrix

| AC | Test (class.method / row keys) | Project(s) |
|---|---|---|
| N1 | `NullSemanticsHarness.EveryPath_SelectsTheOraclesRows`: `nNeV`, `nNe5`, `vNeN` (reversed), `nameNeA`, `aNeName`, `reqNeA`, `flagNeTrue`, `dYearNe2020`, `nameLengthNe1`, `condNullableNe` | all four (linked harness) |
| N1 (remote) | `NullSemanticsHarness.RemoteProperty_ReadsSelectTheOraclesRows`: `parentLabelNeP`, `parentLabelEqNone`, `notParentLabelEqP` | all four |
| N2 | `…EveryPath…`: `nEqNone`, `nNeNone`, `noneNeN`, `nameEqNoName`, `nameNeNoName`, `idEqNoneInt` | all four |
| N3 | `…EveryPath…`: `nEqM`, `nNeM`, `nEqCode`, `nNeCode` | all four |
| N4 | `…EveryPath…`: `notNEqV`, `notNNeV`, `notNGtV`, `notNGeNone`, `notAndEq`, `notOrEq`, `nNe5OrNameEqA`, `doubleNot`, `notNameLengthGt1` | all four |
| N5 | `…EveryPath…`: `nGtNone`, `nGtV` | all four |
| N6 | `…EveryPath…`: `idsContainsN`, `notIdsContainsN`, `idsWithNullContainsN`, `notIdsWithNullContainsN`, `onlyNullContainsN`, `notOnlyNullContainsN`, `onlyNullContainsCode`; `TranslatorDirectHarness.CollectionContains_NullItems_*`, `…_Negated_*` | all four |
| N7 | `…EveryPath…`: `notNameContainsA`, `notNameStartsWithA`, `notNameEndsWithA`; `TranslatorDirectHarness.StringMethod_Negated_*` | all four |
| N8 | `…EveryPath…`: `nameContainsNull` (no row), `notNameContainsNull` (every row; delete refused AlwaysTrue), `idOrNotNameContainsNull` (refused), `idAndNotNameContainsNull` (row 2; delete deletes it); `TranslatorDirectHarness.StringMethod_NullValue_Negated_SendsTrue` | all four |
| N9 | `NullSemanticsHarness.All_AnswersAsTheOracle`: `allNGt0` (false), `allNNe5` (false), `allReqNeZz` (true), `allNotNameContainsNull` (true), `allNameContainsNull` (false); `CapturedValueHarness.All_*` rows flipped (§7) | all four |
| N10 | `…EveryPath…`, every row: the WHERE text and parameters are identical across Query, QueryAsync, Where, Count, Delete and DeleteAsync; deleted ids = oracle ids, or the guard's message. The existing `WhereParityHarness` and `DeleteGuardHarness` stay green. | all four |
| N11 | `NullSemanticsHarness.NonNullableShapes_GetNoCompensation`: `idNe5`, `idEq5`, `notIdEq5`, `notCodeGt5`, `notIntIdsContainsId`, `nameEqA`, `nEq5`, `nameContainsA`, `nEqNullLiteral`, `nGt5`, `condNe`. The WHERE has no `IS NULL`/`IS NOT NULL`, the `NOT` is kept where the source has one, and the text equals the visitor's translation with `NullSemantics` bypassed (a test seam: an `internal` switch, `NullSemantics.Disabled`, for this comparison only). The existing SQL-text pins (`CapturedValueHarness`, `LikeToStringHarness`, `DeleteGuardHarness`) also stay green. | all four |
| N12 | `DocumentationTruthTests.NullComparisons_DocumentedAsCSharp`: Usage.md, Advanced.md and the AI instructions no longer say a captured null or `!=` drops NULL rows, and Changelog `[3.12.0]` has a Behavior change entry. | SqlServer.Tests |
| — | `NullSemanticsTests` (DB-free, Core): `Rewrite_KeepsCSharpsValue` (for each harness key whose C# doesn't throw, the rewritten body compiled over the seed gives the same result as the original), `Rewrite_ReturnsTheOriginalNodeWhenNothingCanBeNull`, `CanBeNull_ByPropertyKind` (int, int?, string, byte[], [RemoteProperty] int, [SubqueryAggregate] Count and Sum, [JsonPath], [SqlExpression]), `Rewrite_FoldedPartsStayConstants`, argument-null checks | SqlServer.Tests |
| R1 | `SortHelperTests` (DB-free): `ComposedQuery_IsOrderedQueryable_AsEfCore`, `IsOrderedQueryableHelper_ThrowsAsEfCore` (SqlServer.Tests references EF Core 9.0.8, so the test runs the same helper over an EF Core `DbContext` built without a database, and asserts the same exception type and the same message apart from the type names; Sqlite.Tests pins the message only), `ExpressionTypeCheck_PicksOrderByThenBy` | SqlServer.Tests, Sqlite.Tests |
| R2 | `DocumentationTruthTests.AttributeExamples_NameRealParametersAndProperties`: every `[RemoteLink(`, `[RemoteProperty(`, `[RemoteKey(`, `[SubqueryAggregate(`, `[SqlExpression(`, `[JsonPath(`, `[JsonCollection(` block in the shipped .md files. Each `ident:` is checked against the attribute's constructor parameter names, and each `Ident =` against its settable properties, by reflection. | SqlServer.Tests |
| R3 | `CiWorkflowTests`: ci.yml has jobs for the four suites; push triggers include `master`, `development/**`, `release/**`; pull_request includes `master`; `publish.needs` lists all four; no other workflow runs a suite on push (no double runs). Proved for real by the first CI run of the pushed branch (UNVERIFIED until then). | SqlServer.Tests |
| R4 | A review check (grep): no `[ReadModel]` offered in docs/ or the shipped Markdown. | review |
| R5 | `PackagingTests` (existing) plus a version row: the five csprojs carry 3.12.0 in all four version fields. | SqlServer.Tests |

### 6.2 The `NullSemanticsHarness` (linked into all four test projects)

**Tables:** `zz_ns_row` (id, n, m, name, req NOT NULL, flag, d, code NOT NULL, parent_id) and `zz_ns_parent`
(id, label). Both are created and dropped per test, as `WhereParityHarness` does, so the shared databases stay as
found.

**Seed:**

| id | n | m | name | req | flag | d | code | parent_id |
|---|---|---|---|---|---|---|---|---|
| 1 | 5 | 5 | 'a' | 'a' | true | 2020-06-01 | 5 | 1 |
| 2 | 5 | NULL | NULL | 'b' | false | NULL | 6 | 2 |
| 3 | NULL | 5 | 'b' | 'a' | NULL | 2021-01-01 | 7 | NULL |
| 4 | NULL | NULL | NULL | 'c' | NULL | NULL | 8 | 1 |
| 5 | 7 | 8 | 'a' | 'd' | true | 2020-01-01 | 5 | 2 |
| 6 | 1 | 1 | 'ca' | 'a' | false | 2022-03-03 | 1 | NULL |

Parents: `(1, 'p')`, `(2, NULL)`.

**Oracle.** Each key has a predicate and an optional oracle predicate, used where C# would throw on a null receiver
(string methods, `.Value.Year`, `.Length`). The oracle states EF's answer null-safely, for example
`!(x.Name != null && x.Name.Contains("a"))`. Expected ids come from LINQ to objects over the seed, never typed by
hand. The remote rows join the seed in C#.

**Assertions:** those of `WhereParityHarness.SelectCountAndDelete_AgreeWithLinqToObjects`, plus `Any`.
`ExpectedDeleteRejection` lists the keys the guard refuses, with their messages: the folded-true null searches and
`notNGeNone`. Remote keys run only the reads: a DELETE has no JOIN (a 3.11.0 Known issue, unchanged).

### 6.3 The mutations each key test must kill

Each mutation is run once against the finished code and reverted. The handoff states which ones were killed.

| M | Mutation | Killed by |
|---|---|---|
| M-N1 | Drop `\|\| W == null` from `!=` | N1 rows |
| M-N2 | Don't rewrite a null value (send `= @p`) | N2 rows |
| M-N3 | No De Morgan (keep `NOT (…)` over `&&`/`\|\|`) | `notAndEq`, `notOrEq` |
| M-N4 | No compensation for a negated relational comparison | `notNGtV`, `notNameLengthGt1`, `allNGt0` |
| M-N5 | Drop null items from `IN` (3.11.0) | `idsWithNullContainsN`, `onlyNullContainsN` |
| M-N6 | Negated LIKE without `OR col IS NULL` | N7 rows |
| M-N7 | A negated null search selects no row (D8) | `notNameContainsNull`, `allNotNameContainsNull` |
| M-N8 | `All` wraps the SQL in `NOT (…)` again | `allNGt0` |
| M-N9 | Compensate operands that can't be null | N11 rows |
| M-N10 | `CanBeNull` false for reference types | `nameNeA`, `notNameContainsA` |
| M-N11 | `CanBeNull` false for remote properties | remote rows |
| M-N12 | Both-null `==` without `(a IS NULL AND b IS NULL)` | `nEqM` |
| M-R2 | Revert one fixed doc line | `AttributeExamples_…` |
| M-R3 | Drop `test-postgresql` from `publish.needs` | `CiWorkflowTests` |

### 6.4 Suites, coverage, opt-in

- **Suites:** SQL Server with the CI filter, local `.\SQL2019` through `FUNKY_CONNECTION`; PostgreSQL, the local
  service through the suite's fallback; MySQL, `FUNKY_MYSQL_CONNECTION`; SQLite, temp files. Run serially, with
  `zz_` tables only.
- **net48:** needs VS MSBuild plus vstest. It's run if the toolchain is present; if not, that's stated as not run.
- **Coverage:** coverlet per touched file, at least 85% of lines, measured as the union of the suites that run the
  file. `NullSemantics.cs`, the four visitors, the four translators and the four LINQ providers.
- **Baseline today (SQLite suite alone):** `SqliteWhereClauseVisitor.cs` 73.6%, translator 99.0%, LINQ provider
  95.6%. The visitor is measured with SqlServer.Tests' SQLite classes added. If it's still under 85%, the uncovered
  lines are listed. Each one gets a test, or is put to the owner as pre-existing (the `.HasValue`/`.Value`
  messages).

## 7. Pins expected to flip

Each flip below is the AC's answer replacing the old one. Every flip is listed in the handoff with its old and new
value. Any other pin that breaks is a regression to explain, not to re-pin.

- **`CapturedValueHarness`, D8 rows:**
  - `notContainsNullValue` (rows `2` → every row; the SQL fragment `OR NOT … LIKE NULL)` → `OR NOT 1=0)`);
  - `allNotContainsNull` (true → true, the oracle);
  - `allContainsNull` (true → false);
  - `allOrContainsNull` (true → C#'s answer for `Id == 2 \|\| …`: false, because rows other than 2 fail).
- **`CapturedValueHarness`, `All` rows:** `allNullableGreaterThanZero` (KnownIssue true → false); the KnownIssue
  machinery is deleted if nothing else uses it.
- **`TranslatorDirectHarness`:** `CollectionContains_NullValuesSkipped` (the null is dropped → `OR col IS NULL`). It
  is renamed.
- **The Changelog 3.11.0 Known issues** for `All` and the null search inside `All` move to `[3.12.0]` Fixed.

## 8. Riders

- **R1 (#16):** tests and an `Advanced.md` §5 note only. Closing #16 on GitHub, with the EF evidence, is an outward
  action: ask the owner.
- **R2:** `Usage.md` lines 1360–1361 and 1367–1368, and the 11 lines of `docs/ai-instructions/FUNKYORM_AI_INSTRUCTIONS.md`
  (719–720, 772–789, 940–950). The packaged copies change with the release.
- **R3:** move the PostgreSQL and MySQL jobs into `ci.yml` as `test-postgresql` and `test-mysql`, with the same
  services, scripts and filters, and delete the two separate workflow files so nothing runs twice. Add
  `test-sqlite` (ubuntu-latest; the suite needs no service). Then `publish.needs: [test-net8, test-sqlite,
  test-postgresql, test-mysql]`. Triggers: push to `main`, `master`, `development/**`, `release/**`; pull requests
  to `main`, `master`, `development/**`. `master` has no branch protection or rulesets (checked 2026-10-20), so
  renaming the checks breaks nothing.
- **R4:** amend `QUERY_OPERATOR_CORRECTNESS_PLAN.md` line 108 and `BACKLOG_AFTER_3.10.0.md` line 658 to say that
  `[ReadModel]` was withdrawn on 2026-10-20 (owner: an agent proposal, not framework design).
- **R5:** version and release notes, last.

## 9. Tasks

| Task | What | Turns green |
|---|---|---|
| 0 | This plan's tests reviewed by a non-author. Findings → rev 2. | — |
| 1 | `NullSemanticsHarness`, four derived classes, `NullSemanticsTests`, the `TranslatorDirectHarness` rows, `SortHelperTests`, the docs/CI tests: all written and **red** (or green-on-arrival pins, named as such) | — |
| 2 | `NullSemantics` (Core) and the visitors' `Visit(lambda)` | N1–N5, N10, N11, N9 partly |
| 3 | The translators' `negated` overload and null items; the visitors' `VisitUnary(Not)` | N6–N8 |
| 4 | `All` through the visitor in the four LINQ providers | N9 |
| 5 | §7 flips; all four suites; mutations M-N1..M-N12; coverage | — |
| 6 | Riders R1–R4 (docs, CI, plan lines) | R1–R4 |
| 7 | Changelog, Usage.md, Advanced.md, the AI instructions; version 3.12.0 | N12, R5 |
| 8 | Adversarial review by two fresh non-author lenses over `25956e0..HEAD`, until CLEAN | — |

Pushing, opening the PR, merging and publishing are the owner's calls. CI's first real run of R3 needs the push.

## 10. Risks and decisions

- **More rows, wider deletes (AC-N1..N9).** For example, `Delete(x => x.Status != "Archived")` now also deletes
  the rows whose status is null, which is C#'s and EF's answer. The Changelog's Behavior change lists each shape.
  That makes this a minor version.
- **D8 amended (AC-N8).** 3.11.0 sent "no row, negated or not" for a null search value. The owner's "match EF"
  makes the negated half "every row".
- **SQL text grows** for `!=` and for negated LIKE and IN on columns that can be null, and every string counts.
  Index use on `<>` is rare anyway, and the planners prune `IS NULL` on NOT NULL columns. Equality, the commonest
  shape, doesn't change.
- **The guard's after-translation check sees new SQL.** `HasLiteralTautology` and the self-reference pattern must
  not misread `(a <> b OR a IS NULL)`. They're covered by the harness's delete rows and by `DeleteGuardHarness`.

## 11. Review record

(Task 0 findings and their dispositions go here.)
