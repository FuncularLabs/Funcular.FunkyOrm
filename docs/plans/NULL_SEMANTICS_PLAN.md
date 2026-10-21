# FunkyORM 3.12.0 — null comparisons in WHERE that answer as EF Core does (plus riders)

> **Status:** rev 2, 2026-10-20. Task 0 round 1 (non-author) was NOT CLEAN, with 15 findings, T0-1..T0-15; §11 has
> their dispositions. Task 0 round 2 is next.
> **Branch:** `development/3.12.0`, cut from master `25956e0` (3.11.0).
> **Owner directive (Paul, 2026-10-20):** "for #2, attempt to match EF behavior". Item #2 covers two things: nullable
> `!=` and a comparison with a null variable, and the `is IOrderedQueryable` sort helper (#16).
> **Riders requested in the same message:**
> - the `[SubqueryAggregate]` conditional-count examples that don't compile;
> - PostgreSQL and SQLite in CI;
> - withdrawing the never-built `[ReadModel]` proposal.
>
> **Order of work.** R1–R4 were implemented, test-first, while Task 0 round 1 was running, ahead of the task order in
> §9: R2 `923137a`, R3 `2a9223b`, R4 `8929612`, R1 `0e941c4`. Their red runs are recorded in their commit messages.
> Task 0's findings on them (T0-15) are answered in rev 2. The null-semantics code waits for a CLEAN Task 0.
>
> **Version:** 3.12.0, a minor release. Queries return more rows, and deletes remove more (BACKLOG WP-H; the 3.11.0
> audit's FD-08).

## 1. The problem

FunkyORM 3.11.0 translates a comparison into SQL as written. SQL's three-valued logic then drops rows that C# and EF
Core keep.

These figures are from the published 3.11.0, run on **SQL Server** against the tutorial sample's 100,000 orders, of
which 2,960 have no ship-to address:

| Predicate | FunkyORM 3.11.0 | C# / EF Core |
|---|---|---|
| `o.ShipToAddressId != a` | 97,016 rows (`ship_to <> @p`; the NULL rows are dropped) | 99,976 |
| `o.ShipToAddressId == none` (a variable holding null) | 0 rows (`= @p` with NULL) | 2,960 |
| `o.ShipToAddressId == null` (a literal) | 2,960 | 2,960 |

The same holds under every `!`. `!(x.N > 5)`, `!ids.Contains(x.N)` and `!x.Name.Contains("a")` all drop the NULL
rows, and so does `All`, which 3.11.0 lists as a Known issue. A collection holding null drops the null from
`IN (…)` (backlog Appendix A #18). A comparison inside a conditional's test drops NULL rows too.

On **PostgreSQL**, a null parameter is typed `text` (`PostgreSqlParameterGenerator`). So 3.11.0's `n = @p` with a
null `int?` most likely failed with `42883` instead of returning no row. That's UNVERIFIED until Task 1's red run at
`25956e0` (§6.5) records it.

## 2. The oracle: what EF Core sends

The reference is EF Core 10.0.12, with its default C# null semantics (`UseRelationalNulls(false)`). It runs through
`ToQueryString()`, with no database. The probe is committed as `tools/ef-null-oracle` (rev 2, T0-15), so the oracle
lives in the repo and anyone can rerun it. The SQL Server and SQLite translations carry the same semantics.

In the tables, `N` and `M` are `int?`, `Name` is a nullable `string`, `Req` is a required `string`, `Id` is an `int`,
`E` is a `Color?`, `D` is a `DateTime?`, `v` holds 5, `none` holds null, and `a` holds `"a"`.

| C# | EF Core's WHERE |
|---|---|
| `N != v`, `N != 5` | `N <> @v OR N IS NULL` |
| `N == v` | `N = @v` |
| `N == none` / `N != none` | `N IS NULL` / `N IS NOT NULL` |
| **`Id == none` / `Id != none`** (column can't be null) | **`0 = 1` / no WHERE (every row)** |
| `N == M` | `N = M OR (N IS NULL AND M IS NULL)` |
| `N != M` | `(N <> M OR N IS NULL OR M IS NULL) AND (N IS NOT NULL OR M IS NOT NULL)` |
| `N == Id` / `N != Id` | `N = Id` / `N <> Id OR N IS NULL` |
| `Name.Length == Name.Length` (unfolded self-comparison) | `LEN(Name) = LEN(Name) OR Name IS NULL` |
| `!(N == v)` / `!(N != v)` | `N <> @v OR N IS NULL` / `N = @v` |
| `!(N > v)`, `!(N >= v)`, `!(N < M)` | `CASE WHEN N > @v THEN 0 ELSE 1 END = 1` (true when the comparison is false or unknown) |
| `N > v`, `N > M` | as written |
| `!(N == 5 && Name == a)` | `N <> 5 OR N IS NULL OR Name <> @a OR Name IS NULL` (De Morgan, then the rules above) |
| `!(N == 5 \|\| Name == a)` | `(N <> 5 OR N IS NULL) AND (Name <> @a OR Name IS NULL)` |
| `ids.Contains(N)` / `!ids.Contains(N)` | `N IN (…)` / `N NOT IN (…) OR N IS NULL` |
| `{1, null}.Contains(N)` / negated | `N IS NULL OR N = 1` / `N IS NOT NULL AND N <> 1` |
| `Id = 2 && {1, null}.Contains(N)` | `Id = 2 AND (N IS NULL OR N = @p)`: the null-item form is parenthesized |
| `!intIds.Contains(Id)` | `Id NOT IN (…)` |
| `Name != a` / `Req != a` | `Name <> @a OR Name IS NULL` / `Req <> @a` |
| `Name == noName` / `!=` | `Name IS NULL` / `Name IS NOT NULL` |
| `Flag != true` (`bool?`) | `Flag <> 1 OR Flag IS NULL` |
| `E != Color.Red` (`Color?`) / `E == noColor` | `E <> 0 OR E IS NULL` / `E IS NULL` |
| `!Name.Contains(a)`, `!Name.StartsWith(a)` | `Name NOT LIKE … OR Name IS NULL` |
| `Name.Contains(a) == false`, `!(Name.Contains(a) == true)`, `Name.Contains(a) != true` | `Name NOT LIKE … OR Name IS NULL` |
| `(N == 5) == (M == 5)` | the two sides as two-valued truths, compared: `CASE WHEN N = 5 AND N IS NOT NULL … END = CASE WHEN M = 5 … END` |
| `Name.Contains(noName)` / `!Name.Contains(noName)` (also `StartsWith`, `EndsWith`) | `0 = 1` / no WHERE: every row |
| `D.Value.Year != 2020`, `Name.Length != 3` | `DATEPART(year, D) <> 2020 OR D IS NULL`, `LEN(Name) <> 3 OR Name IS NULL` |
| `N.ToString() != "5"` / `Name.ToString() != a` | `COALESCE(CONVERT(…, N), '') <> '5'` (never null) / `Name <> @a OR Name IS NULL` |
| `(N != 5 ? 1 : 0) == 1` | `CASE WHEN N <> 5 OR N IS NULL THEN 1 ELSE 0 END = 1`: the test is compensated |
| `N > 3 ? M != 5 : Name == a` | the branches are compensated: `CASE WHEN N > 3 THEN (M <> 5 OR M IS NULL) … WHEN Name = @a …` |
| `!(N > 3 ? M != 5 : Name == a)` | the negation reaches the branches: `… WHEN Name = @a AND Name IS NOT NULL … = 0` |
| `(N > 3 ? 1 : 0) != 1`, `(N ?? 0) != 5` | as written (the operand is never null) |
| `All(N > 0)` | `NOT EXISTS (… WHERE CASE WHEN N > 0 THEN 0 ELSE 1 END = 1)` |
| `Parent.Code != 5` (a LEFT JOIN to a NOT NULL column) / `Parent.Code == 0` | `<> 5 OR … IS NULL` / `= 0`: a missing parent is a NULL, not a 0 |

EF Core also translates `N.HasValue` (`IS NOT NULL`) and `N.Value` (the column). FunkyORM rejects both on purpose,
with `NullableExpressionException` (§3).

**#16:** EF Core behaves as FunkyORM does.
- `context.Rows.Where(…)` is an `EntityQueryable<T>`, which implements `IOrderedQueryable<T>`.
- So `q is IOrderedQueryable<T> o ? o.ThenBy(k) : q.OrderBy(k)` throws System.Linq.Expressions' `ArgumentException`.
- EF Core's root `DbSet<T>` isn't ordered; FunkyORM's root is, and its `ThenBy` there works.

Pinned in R1.

## 3. Scope

**In scope:**
- Every WHERE that goes through the shared translation: `Query(predicate)`, `QueryAsync`, `Query<T>().Where`,
  `Count`/`LongCount`/`Any`/`All(predicate)`, and `Delete`/`DeleteAsync`.
- All four providers.

**Out of scope, and why:**
- **SELECT projections and ORDER BY keys.** They have their own visitors and need a design of their own; it's a
  follow-up. The positive null-item IN form does reach them, because the translator is shared (AC-N6).
- **`.HasValue` and `.Value` on a nullable property.** FunkyORM rejects them by design, with a message naming
  `== null` / `!= null`. It's a loud difference, not a wrong result. Unchanged.
- **Arithmetic, `??`, and the bitwise or non-short-circuit `&` / `|`.** None is translated in WHERE today; all
  throw. Unchanged, and the rewrite leaves `&` / `|` alone (no De Morgan).
- **A collection `Contains` whose item is a cast column,** such as `nullableIds.Contains((int?)x.Code)`. The
  translator rejects that today (`NotSupportedException`, "Collection Contains is translated only as …"); unchanged,
  and pinned (T0-6).
- **Which string columns are nullable.** Every reference-type column counts as possibly null. A NOT NULL string
  column therefore gets an `OR col IS NULL` that EF leaves out: the same rows, more SQL. And a null value against
  it is `IS NOT NULL` (every row), not a fold (AC-N2).
- **The read-back of a non-nullable property over a SQL NULL.** A missing LEFT JOIN parent, an absent JSON key, or
  an empty aggregate reads back as `default(T)`, because the materializer skips `DBNull`. WHERE compares the SQL
  value, as EF does (§2's last row). It's a Known issue (AC-N12), unchanged.
- **MySQL's JSON null.** `JSON_UNQUOTE(JSON_EXTRACT(…))` gives the text `'null'` for a JSON `null`, where the other
  three give SQL NULL. A Known issue, unchanged.
- **The SQLite `Guid`/`DateTime` binding (FD-11).** Unchanged.

## 4. Acceptance criteria

**"Can be null"** means the operand's SQL value can be NULL. The expression-level classifier
`NullSemantics.Classify` (§5.1) decides it:
- a property of `Nullable<T>` or reference type;
- whatever its CLR type: a `[RemoteProperty]` or `[RemoteKey]` (LEFT JOIN), a `[JsonPath]`, a `[SqlExpression]`,
  or a `[SubqueryAggregate]` other than `Count`/`ConditionalCount`;
- a date part or `Length` of a property that can be null (the witness is that property);
- `ToString()` of a string property (not of an integer property: `COALESCE(…, '')`);
- a conditional with a branch that can be null (the witness is the conditional);
- a `Convert` of any of these.

**"Doesn't read the row"** means the shared evaluator reads the value before the query: a constant, a captured,
static or nested member.

| AC | Criterion |
|---|---|
| **AC-N1** | `x.P != v`, with P able to be null and v a non-null value that doesn't read the row, also selects the rows where P is null. Either operand order. |
| **AC-N2** | `x.P == v` / `x.P != v`, with v null and not reading the row: `P IS NULL` / `P IS NOT NULL` when P can be null. **When P can't be null:** `==` is `1=0` and `!=` is `NOT 1=0` (EF's `0 = 1` / every row), so a delete of `!=` alone is refused as always true (T0-1). The literal `x.Id != null` folds the same way. |
| **AC-N3** | Column against column: both able to be null, `==` also matches two NULLs and `!=` is EF's form. One able to be null: `!=` also selects that one's NULL rows; `==` unchanged. It also applies to an unfolded self-comparison (`x.Name.Length == x.Name.Length`). A witness appearing twice is tested once. |
| **AC-N4** | `!` reaches the comparisons: De Morgan over `&&` and `\|\|`, `!(a == b)` as `a != b`, `!(a != b)` as `a == b`, `!!p` as `p`, and through a `bool` conditional into its branches; then AC-N1..N3. A negated `>`, `>=`, `<` or `<=` with an operand that can be null also selects the rows where it is null. One with a null value that doesn't read the row selects every row. |
| **AC-N5** | A `>`, `>=`, `<` or `<=` with a null value that doesn't read the row is `1=0` (no row, C#'s lifted comparison). The rows are those of 3.11.0 on SQL Server, MySQL and SQLite. On PostgreSQL 3.11.0 failed with 42883 (to be confirmed by the red run, §6.5). |
| **AC-N6** | Collection `Contains` on a property that can be null. A null item also selects the NULL rows, parenthesized: `(P IN (…) OR P IS NULL)`; with only nulls, `P IS NULL`. Negated: with no null item, `(NOT P IN (…) OR P IS NULL)`; with a null item, `(P IS NOT NULL AND NOT P IN (…))`; with only nulls, `P IS NOT NULL`. An empty collection stays `1=0` / `NOT 1=0`. On a property that can't be null, the null items are dropped as before, and the negated form is `NOT P IN (…)` as in 3.11.0. |
| **AC-N7** | `!x.P.Contains(v)`, `!x.P.StartsWith(v)` and `!x.P.EndsWith(v)` are `(NOT P LIKE … OR P IS NULL)`. |
| **AC-N8** | A null search value: `x.P.Contains(null)` selects no row, as in 3.11.0 (`P LIKE NULL`). **Negated, it selects every row**, as EF does: `NOT 1=0`, so a delete of it alone is refused as always true. Same for `StartsWith` and `EndsWith`. This amends 3.11.0's D8 per the owner's directive. |
| **AC-N9** | `All(p)` is true when no row satisfies `!p` under AC-N1..N8. |
| **AC-N10** | One path. For one predicate, Query, QueryAsync, Where and Count send the same WHERE text and parameters, and Delete and DeleteAsync send byte-identical text. All select, count or delete the oracle's rows on all four providers; Any and All answer as the oracle. **The guard's verdicts change exactly here (T0-3):** see the table below. |
| **AC-N11** | No compensation where C# and SQL already agree: a positive `==` against a non-null value, a positive relational comparison, a positive LIKE, IN without a null item, a literal null on a column that can be null, and every shape whose operands can't be null. Those send **exactly 3.11.0's WHERE text and parameters**, `NOT` included where 3.11.0 had one. This is pinned literally per provider, and the pins are green on `25956e0` (T0-2). |
| **AC-N12** | The documents say what the code does: Changelog `[3.12.0]` (a Behavior change listing every shape that now selects or deletes more rows, the verdict changes, Fixed, Known issues: the read-back of a non-nullable property, MySQL's JSON null, PostgreSQL's NULLs-last), `Usage.md`, `Advanced.md` §5, and the AI instructions. |
| **AC-N13** | **Read once (T0-9).** The rewrite reads each value it classifies once, and the visitor uses that value (a constant), so the SQL's form and the bound value agree. It reads a value only when the other operand is a shape the translators accept (§5.1), so a predicate the translator rejects runs no caller code first. |
| **AC-N14** | **Predicates compared with predicates (T0-12).** `p == true` and `p != false` are `p`; `p == false` and `p != true` are `!p`. For two predicates, `p == q` is `(p && q) \|\| (!p && !q)` and `p != q` is `(p && !q) \|\| (!p && q)`, rewritten under AC-N1..N8. Here p and q are a comparison, a logical operator, `!`, a method call, or a `bool` conditional, not a member. A `bool`/`bool?` member compared with a value is AC-N1/N2. |

**AC-N10: the delete verdicts that change.** Everything else stays as in 3.11.0.

| Shape | 3.11.0 | 3.12.0 | Why |
|---|---|---|---|
| `!x.Name.Contains(null)` (also `StartsWith`/`EndsWith`), `x.Id == 2 \|\| !x.Name.Contains(null)`, `!(x.N == 5 && x.Name.Contains(null))` | accepted (deleted nothing) | refused, always true | AC-N8: every row |
| `!(x.N >= none)` and the other negated relational comparisons with a null value | accepted (nothing) | refused, always true | AC-N4: every row |
| `x.Id != none`, `x.Id != null` (a column that can't be null) | `x.Id != none` accepted (nothing); `x.Id != null` accepted (every row: `id IS NOT NULL`) | refused, always true | AC-N2 fold (T0-1) |
| `!onlyNulls.Contains(x.N)`, `x.Id == 2 \|\| !onlyNulls.Contains(x.N)` | refused (the null was dropped: `NOT 1=0`) | accepted: `n IS NOT NULL` | AC-N6 |
| A predicate whose only remote reference folds away (`x.ReverseRemoteInt > none` → `1=0`) in `Count`/`All` over a reverse join | refused (fan-out) | accepted: no join is needed, and `1=0` gives C#'s answer | the WHERE no longer names the remote alias (T0-3) |
| `CapturedValueHarness` `notContainsNullValue` (`x.Id == 2 \|\| !x.Name.Contains(null)`) as a delete | accepted (row 2) | refused, always true | AC-N8 |

## 5. Design

### 5.1 One rewrite in Core: `Funcular.Data.Orm.Linq.NullSemantics`

`NullSemantics` is a public static class, like `RowIndependentEvaluator`, `LikePattern` and `AggregateSemantics`.

**`Expression Rewrite(LambdaExpression predicate, IDictionary<Expression, bool?> memo)`** returns a body with C#'s
(EF's) value for every row, built only from shapes the four visitors already translate. The recursion carries a
polarity, positive or negated, and works on **truth positions**: the body, and the operands of `&&`, `||`, `!`, a
`bool` conditional's branches, and AC-N14's operands.
- **Folding first.** `WherePredicateGuard.Fold` runs with the visitor's memo. A known truth becomes a `bool`
  constant (negated under negation), so every fold 3.11.0 sent stays a fold.
- **`!e`:** `e` with the polarity flipped.
- **`&&` / `||`:** the rewritten operands, with the operator swapped under negation. `&` and `|` are left alone.
- **A `bool` conditional `c ? a : b` in a truth position:** `Test` is rewritten positive (true exactly when C# is
  true; an unknown test takes the ELSE branch, which is C#'s false); `a` and `b` carry the polarity.
- **AC-N14 shapes:** rewritten into `&&`/`||`/`!` first, then as above.
- **`==` / `!=`:** under negation the operator flips (`Expression.Equal`/`NotEqual` re-resolve the operator method for
  `string`, `decimal`, `DateTime` and the lifted forms). Then `Classify` looks at each operand. The result is one of:
  - **row-independent:** the value is read once and replaced by a `ConstantExpression` of the operand's type
    (AC-N13);
  - **can be null:** with its witness, the expression whose `IS NULL` says so (the property, or the conditional);
  - **can't be null;**
  - **unclassifiable:** a shape the translators don't accept (a member of a `[NotMapped]` object, a cast receiver).
    The comparison is left untouched, and no value is read.

  The rules, in order:
  1. Both operands row-independent: left as is (`Fold` saw them).
  2. A null value against a row operand that can be null: `row == null` / `row != null` (AC-N2).
  3. A null value against a row operand that can't be null: `false` / `true` (AC-N2, T0-1).
  4. A non-null value against a row operand that can be null: `!=` becomes `(cmp) || W == null`; `==` is unchanged
     (AC-N1).
  5. Both operands read the row: EF's forms (AC-N3), with the witnesses de-duplicated.

  `W == null` is `Expression.Equal(W, Constant(null, T?))`, with a `Convert` to `Nullable<T>` when W is a
  non-nullable value type (a remote `int`).
- **A comparison in a conditional's `Test`, or anywhere below a truth position that isn't a truth position** (the
  operands of a non-predicate `==`): the conditional's `Test` is a truth position and is rewritten positive. Other
  value positions are left as they are.
- **`>`, `>=`, `<`, `<=`:**
  - A null value that doesn't read the row (read once): `false`, or `true` under negation (AC-N4/N5).
  - Under negation: `!(cmp) || W1 == null [|| W2 == null]`, with each witness once.
  - Otherwise the original node.
- **Other leaves** (a method call, a `bool` member): unchanged, wrapped in `!` under negation. The translator answers
  a negated method call (§5.2).

**AC-N11 holds by construction.** A subtree is rebuilt only when a leaf under it is compensated or folded.
Otherwise the original node is returned, `!` included. A value that was read but needs no compensation is still
replaced by its constant. The visitor's SQL for a constant equals its SQL for the captured member (both go through
`_parameterGenerator.CreateParameter(value)`), and the N11 pins check that.

**`Classify` is public**, as `NullSemantics.Classify(Expression operand, ParameterExpression row)`, returning
`(kind, witness, value)`. Alongside it, `NullSemantics.CanBeNull(PropertyInfo)`, the property-level rule, is public
for the translators.

### 5.2 The translators answer a negated method call

Each `*ExpressionTranslator.TranslateMethodCall` gains an overload with `bool negated`. The existing signature
calls it with `false`.

- **String `Contains`/`StartsWith`/`EndsWith` on a column** (each has its own branch; the null value is checked in
  each):

  | Case | SQL |
  |---|---|
  | negated | `(NOT col LIKE … ESCAPE '!' OR col IS NULL)` |
  | negated, null value | `NOT 1=0` |
  | positive (unchanged) | `col LIKE …` |
  | positive, null value (unchanged) | `col LIKE NULL` |

- **Collection `Contains`:** the collection is read once, as today, with a flag for a null item. AC-N6's SQL,
  parenthesized, when the property can be null (`NullSemantics.CanBeNull`). Otherwise exactly 3.11.0's SQL (nulls
  dropped; negated: `NOT ` + `P IN (…)`).
- **A row-independent `Contains`** (`TryTranslateContains`): `NOT ` + the literal, as today.

Each WHERE visitor's `VisitUnary(Not)` sends a `bool` method-call operand to the overload with `negated: true`.
Every other operand gets `NOT ` + the operand, as today.

### 5.3 The visitors and `All`

Each WHERE visitor's `Visit(lambda)` visits `NullSemantics.Rewrite(lambda, _truths)`. Each LINQ provider's `All`
translates `x => !p`, a new lambda over the same parameter, through the visitor, and drops its `NOT (…)` text
wrapper. `ResolveAggregateJoins` still scans the WHERE text: the witnesses reuse the remote alias, and only T0-3's
fold changes which joins are added.

### 5.4 What doesn't change

`WherePredicateGuard`'s classification, messages and memo semantics. The parity harness's assertions.

## 6. Test plan

### 6.1 The AC → test matrix

Every harness key is labeled **proof** (its ids, verdict or SQL differ between `25956e0` and the AC, and Task 1's
red run must show it failing) or **pin** (it must pass at `25956e0` and after). A proof key that passes at `25956e0`
is a TEST-GAP, fixed before Task 2 starts (§6.5).

| AC | Proof keys (`NullSemanticsHarness.EveryPath_SelectsTheOraclesRows` unless named) | Pin keys | Projects |
|---|---|---|---|
| N1 | `nNeV`, `nNe5`, `vNeN`, `nameNeA`, `aNeName`, `flagNeTrue`, `flagNeFalse`, `dYearNe2020`, `nameLengthNe1`, `eNeRed`, `condNullableNe` (`(x.Id > 2 ? x.N : x.M) != 5`) | `reqNeA` (rows equal; the SQL gains `OR req IS NULL`, pinned as text) | all four |
| N1, the attributes (T0-8) | `RemoteKinds_SelectTheOraclesRows`: `parentLabelNeP`, `parentCodeNe5` (`[RemoteProperty] int` over a NOT NULL parent column), `parentKeyNe1` (`[RemoteKey]`), `docKNe5` (`[JsonPath]` string), `nPlusOneNe6` (`[SqlExpression("{N} + 1")] int?`), `childSumNe10` (`[SubqueryAggregate(Sum)] decimal?`), `childCountNe1` (Count: can't be null, pin) | `childCountNe1`, `parentCodeEq0` (EF's answer: a missing parent isn't 0) | all four; MySQL `docKEqNone` differs (JSON null, Known issue) |
| N2 | `nEqNone`, `nNeNone`, `noneNeN`, `nameEqNoName`, `nameNeNoName`, `eEqNoColor`, `idNeNoneInt` (every row; delete refused), `idEq2OrIdNeNoneInt`, `idNeNullLiteral` | `idEqNoneInt` (no row either way; SQL `1=0` pinned) | all four |
| N3 | `nEqM`, `nNeM`, `nNeCode`, `nameLengthSelf` | `nEqCode` | all four |
| N4 | `notNEqV`, `notNGtV`, `notNGeNone`, `notAndEq`, `notOrEq`, `nNe5OrNameEqA`, `notNameLengthGt1`, `condTestNe` (`(x.N != 5 ? 1 : 0) == 1`), `notBoolCond` (`!(x.N > 3 ? x.M != 5 : x.Name == a)`; SQL Server's dialect rejects a `bool` CASE in WHERE, so its reads are `ReadRejectedByTheDialect`), `boolCondBranches` | `notNNeV`, `doubleNot` (`!!(x.N == 5)`: rows equal; SQL pinned) | all four |
| N5 | — (no row differs on SQL Server, MySQL, SQLite) | `nGtNone`, `nGtV`; the SQL `1=0` pinned; PostgreSQL's red run decides proof or pin there | all four |
| N6 | `idsWithNullContainsN`, `onlyNullContainsN`, `notIdsContainsN`, `notOnlyNullContainsN`, `id2AndIdsWithNullContainsN` (C# {}; the unparenthesized form gives {3,4}), `id2AndNotIdsContainsN` (C# {2}; unparenthesized {2,3,4}); `TranslatorDirectHarness.CollectionContains_NullItems_Parenthesized`, `…_Negated_*` | `idsContainsN`, `notIdsWithNullContainsN`, `notIntIdsContainsId`; `CastItemContains_IsRejected` (T0-6) | all four |
| N7 | `notNameContainsA`, `notNameStartsWithA`, `notNameEndsWithA`; `TranslatorDirectHarness.StringMethod_Negated_*` | — | all four |
| N8 | `notNameContainsNull`, `notNameStartsWithNull`, `notNameEndsWithNull` (every row; delete refused), `idOrNotNameContainsNull` (refused), `idAndNotNameContainsNull` (row 2; delete deletes it), `notAndContainsNull`; `TranslatorDirectHarness.StringMethod_NullValue_Negated_SendsTrue` | `nameContainsNull` (no row either way) | all four |
| N9 | `All_AnswersAsTheOracle`: `allNGt0` (false; 3.11.0 true), `allNameContainsNull` (false; 3.11.0 true), `allNIn517` (`All(x => x.N == 5 \|\| x.N == 7 \|\| x.N == 1)`: false; 3.11.0 true), `allNotNameContainsA` (false; 3.11.0 true) | `allNNe5` (false both), `allReqNeZz` (true both), `allNotNameContainsNull` (true both) | all four |
| N10 | every key: identical WHERE text and parameters on Query, QueryAsync, Where and Count, byte-identical on Delete and DeleteAsync; deleted ids = oracle ids, or the guard's message per the §4 verdict table; `VerdictChanges_AsListed` (one row per verdict-table line, including the reverse-join fold) | the existing `WhereParityHarness` and `DeleteGuardHarness` rows not listed in §7 | all four |
| N11 | `NonNullableShapes_SendTheSqlOf3110`: literal WHERE text and parameters per provider, for `idNe5`, `idEq5`, `notIdEq5`, `notCodeGt5`, `notIntIdsContainsId`, `nameEqA`, `nEq5`, `nameContainsA`, `nEqNullLiteral`, `nGt5`, `condNe` (`(x.N > 3 ? 1 : 0) != 1`), `capturedNEqV` (a constant read once gives the captured member's SQL). The expected strings are captured from `25956e0` and are green there. | (all pins) | all four |
| N12 | `DocumentationTruthTests.NullComparisons_DocumentedAsCSharp`: Usage.md, Advanced.md and the AI instructions no longer say a captured null or `!=` drops NULL rows; Changelog `[3.12.0]` has the Behavior change, the verdict table and the Known issues. | — | SqlServer.Tests |
| N13 | `ReadOnce_CountingGetter`: `x.N != c.Five` reads `c.Five` once and sends its value; `x.Child.Id != c.Five` (rejected by the translator) reads it 0 times; `c.Flip` (a getter that alternates null and 5) gives a WHERE consistent with the bound value | — | SqlServer.Tests, Sqlite.Tests |
| N14 | `nameContainsAEqFalse`, `notNameContainsAEqTrue`, `nameContainsANeTrue`, `nEq5EqMEq5`, `nEq5NeMEq5` | `nameContainsAEqTrue` (rows equal; on SQL Server it reaches the database at `25956e0`, which rejects it, and runs after) | all four |
| (core) | `NullSemanticsTests` (DB-free): `Rewrite_KeepsCSharpsValue` (each key whose C# doesn't throw: the rewritten body compiled over the seed equals the original), `Rewrite_ReturnsTheOriginalNodeWhenNothingIsCompensated`, `Classify_ByOperandKind` (every row of §4's list, including date parts, `Length`, `ToString` int / int? / string, conditional, `Convert`, the six attributes), `CanBeNull_ByPropertyKind`, `Rewrite_FoldedPartsStayConstants`, `Rewrite_LeavesBitwiseAndOrAlone`, argument-null checks | — | SqlServer.Tests, linked into the NetFramework test project (Core runs on net48) |
| R1 | `SortHelperTests` (committed in `0e941c4`), plus `HelperAfterSkip` (the expression check takes `OrderBy` after `Skip`; FunkyORM rejects an `OrderBy` after `Skip`, by design, with its `NotSupportedException`; EF Core builds it) | (all pins) | SqlServer.Tests |
| R2 | `DocumentationTruthTests.AttributeExamples_NameRealParametersAndProperties` (committed) | — | SqlServer.Tests |
| R3 | `CiWorkflowTests` (committed), plus: no `continue-on-error`, no `\|\| true`, no `--filter` beyond the existing PerformanceTests/EntityFramework exclusion, no job-level `if:` on a test job; `MapInconclusiveToFailed=true` on test-sqlite, test-postgresql and test-mysql; test-net8 fails when its TRX counts more than 1 inconclusive (its one by-design inconclusive is `Projection_BoolValue_GetsCSharpsValue`) | — | SqlServer.Tests |
| R5 | `PackagingTests.VersionCarriers_MatchTheChangelog`: the five csprojs (all four version fields) and `tools/consumer-smoke`'s two defaults equal the Changelog's top `[x.y.z]` | — | SqlServer.Tests |

### 6.2 The interface-coverage table (T0-13)

| Member | Tests that call it on purpose |
|---|---|
| `NullSemantics.Rewrite(LambdaExpression, IDictionary)` | `NullSemanticsTests.*`; every harness key through the visitors |
| `NullSemantics.Classify(Expression, ParameterExpression)` | `NullSemanticsTests.Classify_ByOperandKind` |
| `NullSemantics.CanBeNull(PropertyInfo)` | `NullSemanticsTests.CanBeNull_ByPropertyKind`; the N6 translator rows |
| `SqlExpressionTranslator.TranslateMethodCall(…, bool negated)`, and the PostgreSQL, MySQL and SQLite ones | `TranslatorDirectHarness` negated rows (linked into all four) |
| The WHERE visitors' `Visit(lambda)` / `VisitUnary(Not)` (four) | every harness key |
| The four LINQ providers' `All` | `All_AnswersAsTheOracle`; `LinqScopeHarness` All row (§7) |

### 6.3 The `NullSemanticsHarness` (linked into all four test projects)

**Tables** (created and dropped per test, as `WhereParityHarness` does):
- `zz_ns_row`: id, n, m, name, req NOT NULL, flag, d, code NOT NULL, e, parent_id, doc (JSON: SQL Server
  `NVARCHAR(200)`, PostgreSQL `JSONB`, MySQL `JSON`, SQLite `TEXT`).
- `zz_ns_parent`: id, label, pcode NOT NULL.
- `zz_ns_child`: id, row_id, amount.

**Seed:**

| id | n | m | name | req | flag | d | code | e | parent_id | doc |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 5 | 5 | 'a' | 'a' | true | 2020-06-01 | 5 | 0 (Red) | 1 | `{"k":"5"}` |
| 2 | 5 | NULL | NULL | 'b' | false | NULL | 6 | NULL | 2 | `{"k":null}` |
| 3 | NULL | 5 | 'b' | 'a' | NULL | 2021-01-01 | 7 | 1 | NULL | `{}` |
| 4 | NULL | NULL | NULL | 'c' | NULL | NULL | 8 | NULL | 1 | NULL |
| 5 | 7 | 8 | 'a' | 'd' | true | 2020-01-01 | 5 | 0 | 2 | `{"k":"7"}` |
| 6 | 1 | 1 | 'ca' | 'a' | false | 2022-03-03 | 1 | 1 | NULL | `{"k":"1"}` |

- Parents: `(1, 'p', 5)`, `(2, NULL, 7)`.
- Children: `(1, 1, 10)`, `(2, 1, 5)`, `(3, 5, 10)`. So row 1 sums to 15, row 5 to 10, and the rest have no
  children.

**Oracle.** Each key has a predicate, and an oracle where C# would throw or materializes `default(T)`. The oracle is
EF's answer, lifted to nullable: `!(x.Name != null && x.Name.Contains("a"))`, the parent joined in C# with a missing
parent as null, the child sum as null over no rows, the JSON key as null when absent or JSON null (except MySQL's
JSON-null row). Expected ids are computed by LINQ to objects, never typed by hand.

**Assertions:** `WhereParityHarness`'s, plus `Any`. `ExpectedDeleteRejection` follows the §4 verdict table.
Remote-only keys run the reads (a DELETE has no JOIN, a 3.11.0 Known issue), plus one delete per remote kind that
asserts the database's unknown-column error, unchanged.

### 6.4 The mutations each proof must kill

Each is run once against the finished code, then reverted. The handoff states which were killed.

| M | Mutation | Killed by |
|---|---|---|
| M-N1 | Drop `\|\| W == null` from `!=` | N1 proofs |
| M-N2 | Don't rewrite a null value | N2 proofs |
| M-N2b | No fold for a column that can't be null (`IS NOT NULL` instead) | `idNeNoneInt` (delete no longer refused) |
| M-N3 | No De Morgan | `notAndEq`, `notOrEq` |
| M-N4 | No compensation for a negated relational comparison | `notNGtV`, `notNameLengthGt1`, `allNGt0` |
| M-N4b | Don't recurse into a conditional's test or branches | `condTestNe`, `boolCondBranches` |
| M-N5 | Drop null items from `IN` | `idsWithNullContainsN`, `onlyNullContainsN` |
| M-N5b | Drop the parentheses around the null-item forms | `id2AndIdsWithNullContainsN`, `id2AndNotIdsContainsN` |
| M-N6 | Negated LIKE without `OR col IS NULL` | N7 proofs |
| M-N7 | A negated null search selects no row (D8) | `notNameContainsNull`, `allNameContainsNull` |
| M-N8 | `All` wraps the SQL in `NOT (…)` again | `allNGt0`, `allNIn517` |
| M-N9 | Compensate operands that can't be null | N11 pins |
| M-N10 | `CanBeNull` false for reference types | `nameNeA`, `notNameContainsA` |
| M-N11 | Classify by CLR type only (ignore the attributes) | `parentCodeNe5`, `nPlusOneNe6` (as `int?` it's still nullable: `docKNe5`), `childSumNe10` |
| M-N12 | Both-null `==` without `(a IS NULL AND b IS NULL)` | `nEqM` |
| M-N13 | The visitor reads the captured value again | `ReadOnce_CountingGetter` |
| M-N14 | `p == false` left as is | `nameContainsAEqFalse` |
| M-R2 | Revert one fixed doc line | `AttributeExamples_…` (killed: the red run, 17 failures) |
| M-R3 | Drop `test-postgresql` from `publish.needs` | `CiWorkflowTests` (killed, `2a9223b`) |

### 6.5 Suites, red run, coverage, inconclusive

- **Red run at `25956e0` (Task 1).** All new tests are run with the production code unchanged. Recorded per
  provider:
  - every proof key fails and every pin passes, including the N11 literal pins;
  - PostgreSQL's 3.11.0 answer for the null-parameter keys (42883 or rows), which decides N5's label there.
- **Suites:** SQL Server with the CI filter (`.\SQL2019` through `FUNKY_CONNECTION`); PostgreSQL (the local service
  through the suite's fallback); MySQL (`FUNKY_MYSQL_CONNECTION`); SQLite (temp files). Run serially, with `zz_`
  tables only. **An Inconclusive is NOT RUN:** the handoff gives passed, failed and inconclusive counts per suite,
  and a suite with an unexpected inconclusive isn't green.
- **net48:** VS MSBuild plus vstest, with `NullSemanticsTests` linked in. It's run if the toolchain is present; if
  not, it's stated as not run.
- **Coverage:** coverlet per touched file, at least 85% of lines, measured as the union of the suites that run it.
  Covers `NullSemantics.cs`, the four visitors, the four translators and the four LINQ providers. Baselines for all
  twelve provider files are recorded in Task 1, from the union at `25956e0`. Any touched file under 85% gets tests
  until it's at or over. There's no waiver in this plan; one would need the owner.

## 7. Pins expected to flip

Each flip below is the AC's answer replacing the old one, listed in the handoff with its old and new value. Any
other pin that breaks is a regression, to be explained rather than re-pinned.

- **`CapturedValueHarness`:**
  - `notContainsNullValue`: rows `2` → every row; SQL fragment `OR NOT … LIKE NULL)` → `OR NOT 1=0)`; its delete row
    moves from `Delete_DeletesWhatCSharpSelects` to the always-true rejection test.
  - `allContainsNull`: true → false.
  - `allOrContainsNull`: true → false. C# throws ArgumentNullException; EF says false. The D8 tag stays, with its
    text amended.
  - `allNullableGreaterThanZero`: true → false. The KnownIssue tag goes, and so does the KnownIssue machinery if
    nothing else uses it.
  - The comments at CapturedValueHarness.cs:813–818 are rewritten. `All_UnknownCountsAsSatisfied` is renamed to
    `All_AnswersAsCSharp`.
- **`TranslatorDirectHarness`:** `CollectionContains_NullValuesSkipped` becomes `CollectionContains_NullItems_…`,
  parenthesized. The comment at line 272 is rewritten.
- **`LinqScopeHarness`** lines 325–326, four classes: the `All(x => x.FirstName == "x")` fragment
  `WHERE NOT ({col} = ` → `WHERE ({col} != `.
- **`DeleteGuardHarness`** `orFirstNameLengthSelf`: `selectedByQuery` 2 → 3 (EF:
  `LEN = LEN OR FirstName IS NULL`). The comment at lines 262–266 is rewritten.
- **`WhereParityHarness`:** the SQL Server `ReadRejectedByTheDialect` keys `nameContainsEmptyEqTrue` and
  `nameContainsEmptyNeFalse` now run (AC-N14 sends `name LIKE …`). Their reads return C#'s rows, the override is
  removed, and the delete verdicts are unchanged (UnboundedSearch).
- **The Changelog 3.11.0 Known issues** for `All` and for the null search inside `All` move to `[3.12.0]` Fixed.

## 8. Riders

- **R1 (#16),** `0e941c4`: the pins, and notes in `Advanced.md` §5 and `FUNKYORM_AI_ADVANCED.md`. Rev 2 adds the
  `Skip` row.
  - The message comes from System.Linq.Expressions, so it doesn't depend on EF Core's version.
  - FunkyORM's side runs on a SQLite file, because `Query<T>()` discovers columns.
  - Closing #16 needs the owner: the issue asks for a fix, and the owner's directive was to match EF instead.
- **R2,** `923137a`: 17 attribute examples fixed in user-facing Markdown, which is not all packed (`Usage.md`
  isn't).
  - Which examples: the `[SubqueryAggregate]` conditional counts; every `[JsonCollection]` example (`columns:`,
    `orderBy:`); a phantom `joins:` option and its parameter-table row.
  - Files: `Usage.md`, `FUNKYORM_AI_INSTRUCTIONS.md` (lines 715–960), `FUNKYORM_AI_INSTRUCTIONS_POSTGRESQL.md:153`.
  - What the reflection check doesn't do: check positional argument types or order, enum member names, or the
    parameter tables' prose.
- **R3,** `2a9223b`, and in rev 2:
  - test-sqlite runs on **windows-latest**, where the suite is run locally; it's not ubuntu.
  - Triggers: push to `main`, `master`, `development/**`, `release/**`; pull requests to `main`, `master`,
    `development/**`.
  - Rev 2 adds the inconclusive gates (R3 row in §6.1).
- **R4,** `8929612`: `[ReadModel]` is withdrawn in the two plans, and `UpdateWhere` stays a proposal.
- **R5:** version and release notes last, `tools/consumer-smoke` included.

## 9. Tasks

| Task | What | Turns green |
|---|---|---|
| 0 | Task 0 round 2 on rev 2 (non-author) | — |
| 1 | All new tests written. The red run at `25956e0` recorded per §6.5. The N11 pins captured there. Coverage baselines. | pins |
| 2 | `NullSemantics` (Core) and the visitors' `Visit(lambda)` | N1–N5, N11, N13, N14, N10 partly |
| 3 | The translators' `negated` overload, the null items and their parentheses; the visitors' `VisitUnary(Not)` | N6–N8 |
| 4 | `All` through the visitor in the four LINQ providers | N9 |
| 5 | §7 flips; all four suites; mutations; coverage | N10 |
| 6 | R1 `Skip` row; R3 inconclusive gates and the extra `CiWorkflowTests` checks | R1, R3 |
| 7 | Changelog, Usage.md, Advanced.md, the AI instructions; version 3.12.0 everywhere | N12, R5 |
| 8 | Adversarial review by two fresh non-author lenses over `25956e0..HEAD`, until CLEAN | — |

Pushing, the PR, merging and publishing are the owner's calls. R3's first real run needs the push.

## 10. Risks and decisions

- **More rows, wider deletes (AC-N1..N9, N14).** For example, `Delete(x => x.Status != "Archived")` now also deletes
  the rows whose status is null. `Delete(x => x.Name != filter)` with a null filter now deletes every row with a
  name: C#'s and EF's answer, but 3.11.0 deleted nothing. The Changelog's Behavior change lists every shape, so
  this is a minor version.
- **D8 amended (AC-N8),** per the owner's "match EF".
- **Verdict changes (AC-N10 table).** Five refuse more; one accepts more.
- **SQL text grows** for `!=` and the negated forms on columns that can be null, and every string column counts.
  Equality, the commonest shape, doesn't change.
- **The guard's after-translation check.** Task 0 verified that `HasLiteralTautology` and the self-reference pattern
  don't misread the new forms. The harness's delete rows and `DeleteGuardHarness` keep it so.

## 11. Review record

### Task 0 round 1 (non-author Opus, over `152d0ba`; 2026-10-20 19:37): NOT CLEAN, 15 findings

| # | Sev | Blame | Finding | Disposition in rev 2 |
|---|---|---|---|---|
| T0-1 | Blocking | AC-GAP | `x.Id != none` on a column that can't be null became `IS NOT NULL`: an all-rows delete the guard passes | AC-N2 folds it (`1=0` / `NOT 1=0`, EF's answer); the literal `x.Id != null` too; verdict table; keys `idNeNoneInt`, `idEq2OrIdNeNoneInt`, `idNeNullLiteral`; M-N2b |
| T0-2 | Blocking | TEST-GAP | The `Disabled` seam was circular and didn't compile in three projects; AC-N11's wording was narrower than its rows | Seam dropped; literal per-provider pins captured at `25956e0` (green there); AC-N11 widened |
| T0-3 | Major | AC-GAP | "Verdicts unchanged" contradicted both ways, including the aggregate join fold | AC-N10's verdict table; `VerdictChanges_AsListed`; §7 |
| T0-4 | Major | AC-GAP | Comparisons in a conditional's test and branches weren't compensated (EF does) | §5.1 recursion: test positive, branches with polarity; oracle rows; keys `condTestNe`, `notBoolCond`, `boolCondBranches`; `condNe` and `condNullableNe` defined; M-N4b |
| T0-5 | Major | TEST-GAP | The IN null-item forms weren't parenthesized | AC-N6 parenthesized; keys `id2AndIdsWithNullContainsN`, `id2AndNotIdsContainsN`; M-N5b |
| T0-6 | Major | PLAN-GAP | A cast item is rejected by the translator; "a null item vs a column that can't be null" had no translatable shape | Out of scope (§3), pinned as rejected; AC-N6's last clause restated for a property that can't be null |
| T0-7 | Major | TEST-GAP | Many keys didn't distinguish 3.11.0 from the AC; M-N7's killer was wrong; M-N11 survived | Proof/pin labels; the red run checks them (§6.5); `allNIn517`, `allNotNameContainsA`; M-N7 → `allNameContainsNull`; non-nullable remote and attribute keys for M-N11; `doubleNot` defined |
| T0-8 | Major | TEST-GAP | No harness column for the remote/computed attributes; the oracle undefined there; MySQL's JSON null | Columns, seed and `RemoteKinds_…` keys; EF's lifted-nullable oracle; read-back and MySQL JSON null as Known issues |
| T0-9 | Major | HOUSE-RULE | The rewrite read a captured value and the visitor read it again | AC-N13: read once, substituted as a constant, only when the other side is accepted; `ReadOnce_CountingGetter`; M-N13 |
| T0-10 | Major | HOUSE-RULE | An unreachable database reads as a pass (Inconclusive) in CI and locally | R3 gates (`MapInconclusiveToFailed`, the TRX check for SQL Server); §6.5 counts Inconclusive as NOT RUN |
| T0-11 | Major | PLAN-GAP | Unlisted pin flips (LinqScopeHarness All, DeleteGuardHarness, the comments) | §7 extended; AC-N3 covers an unfolded self-comparison, with the witness de-duplicated |
| T0-12 | Major | AC-GAP | Predicates compared with bools or predicates, ToString, the nullable enum, negated null `StartsWith`/`EndsWith`: undefined | AC-N14; §4's classifier list (ToString); keys `eNeRed`, `eEqNoColor`, `notNameStartsWithNull`, `notNameEndsWithNull`; `&`/`\|` left alone; a negated `bool` member is wrapped in `!`, as EF's `= 0` |
| T0-13 | Minor | HOUSE-RULE | No interface-coverage table; `CanBeNull` can't do expression-level rules; a coverage waiver; net48 | §6.2; `Classify` named; no waiver; NetFramework link |
| T0-14 | Minor | PLAN-GAP | PostgreSQL's null parameter typing; §1 figures are SQL Server's | §1 and AC-N5 say so; the red run records it |
| T0-15 | Minor | PLAN-GAP | Riders landed out of order; R2/R3/R1/R5 details; the probe path | Status note; §8 corrected; R1 `Skip` row; R3 extra checks; R5 carriers; probe committed as `tools/ef-null-oracle` |

Blame counts: AC-GAP 4, TEST-GAP 5, HOUSE-RULE 3, PLAN-GAP 3, OTHER 0.
