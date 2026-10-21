# FunkyORM 3.12.0 — null comparisons in WHERE that answer as EF Core does (plus riders)

> **Status:** rev 4, 2026-10-20.
> - Task 0 round 1 (15 findings), round 2 (11) and round 3 (10) were all NOT CLEAN; §11 has every disposition.
> - Rev 3 rejected predicate comparisons on a false premise (R3-4). Rev 4 rewrites them as two-valued truths
>   instead, as EF does, so **nothing is newly rejected**.
> - Rev 4 restores the rewrite of a conditional's test (R3-1). It replaces a null value whole (R3-2), re-sites the
>   Convert pins on nullable columns (R3-3), and makes the read rule exact (R3-5).
> - The harness is written, and its red run at `25956e0` is recorded (§6.5).
> - Task 0 round 4 is a focused delta review of rev 4. The null-semantics code (Task 2 on) is written alongside it,
>   in the working tree. **Nothing is committed until round 4 is CLEAN.** This is recorded as a deviation from
>   "Task 0 first" (§11).
>
> **Branch:** `development/3.12.0`, cut from master `25956e0` (3.11.0).
>
> **Owner directive (Paul, 2026-10-20):** "for #2, attempt to match EF behavior". Item #2 covers two things: nullable
> `!=` and a comparison with a null variable, and the `is IOrderedQueryable` sort helper (#16). The riders requested
> in the same message are the `[SubqueryAggregate]` examples, PostgreSQL and SQLite in CI, and withdrawing the
> `[ReadModel]` proposal.
>
> **Order of work:** R1–R4 were implemented, test-first, while Task 0 round 1 ran: R2 `923137a`, R3 `2a9223b`,
> R4 `8929612`, R1 `0e941c4`. Their red runs are in their commit messages. The null-semantics code waits for a CLEAN
> Task 0.
>
> **Version:** 3.12.0, a minor release. More rows are returned, and more are deleted (BACKLOG WP-H; 3.11.0's audit
> FD-08).
>
> **The owner's companion rule applies:** don't make a shape that returned correct rows throw. So rev 4 adds no
> rejection.

## 1. The problem

FunkyORM 3.11.0 translates a comparison into SQL as written. SQL's three-valued logic then drops rows that C# and EF
Core keep.

The figures are from the published 3.11.0, run on **SQL Server** against the tutorial sample's 100,000 orders, of
which 2,960 have no ship-to address:

| Predicate | FunkyORM 3.11.0 | C# / EF Core |
|---|---|---|
| `o.ShipToAddressId != a` | 97,016 rows (`ship_to <> @p`; the NULL rows are dropped) | 99,976 |
| `o.ShipToAddressId == none` (a variable holding null) | 0 rows (`= @p` with NULL) | 2,960 |
| `o.ShipToAddressId == null` (a literal) | 2,960 | 2,960 |

The same holds:
- under every `!`: `!(x.N > 5)`, `!ids.Contains(x.N)`, `!x.Name.Contains("a")`;
- in `All` (a 3.11.0 Known issue);
- in a collection holding null, whose null 3.11.0 drops from `IN (…)` (backlog Appendix A #18);
- in a comparison inside a conditional.

**PostgreSQL:** a null parameter is typed `text`. So 3.11.0's `n = @p` with a null `int?` most likely failed with
42883. Task 1's red run at `25956e0` records what it actually does (§6.5).

## 2. The oracle: what EF Core sends

**Source:** EF Core 10.0.12 with its default C# null semantics, through `ToQueryString()`; no database is needed.
The probe is committed as `tools/ef-null-oracle`. Every row below comes from it, and SQL Server and SQLite carry the
same semantics.

**Names used in the table:**

| Name | Type or value |
|---|---|
| `N`, `M` | `int?` |
| `Name` | a nullable `string` |
| `Req` | a required `string` |
| `Id` | `int` |
| `E` | `Color?` |
| `D` | `DateTime?` |
| `Parent` | a navigation (EF) / LEFT JOIN (FunkyORM) to a row with `Label` (nullable string) and a NOT NULL `Code` |
| `v` | 5 |
| `none` | null |
| `a` | `"a"` |

| C# | EF Core's WHERE |
|---|---|
| `N != v`, `N != 5` | `N <> @v OR N IS NULL` |
| `N == v` | `N = @v` |
| `N == none` / `N != none` | `N IS NULL` / `N IS NOT NULL` |
| `Id == none` / `Id != none` (can't be null) | `0 = 1` / no WHERE |
| `Req != noName` (required string) | no WHERE (FunkyORM: `req IS NOT NULL`, the same rows; §3) |
| `N == M` | `N = M OR (N IS NULL AND M IS NULL)` |
| `N != M` | `(N <> M OR N IS NULL OR M IS NULL) AND (N IS NOT NULL OR M IS NOT NULL)` |
| `N == Id` / `N != Id` | `N = Id` / `N <> Id OR N IS NULL` |
| `Name.Length == Name.Length` | `LEN(Name) = LEN(Name) OR Name IS NULL` |
| `N >= N` / `!(N >= N)` | as written / `CASE WHEN N >= N THEN 0 ELSE 1 END = 1` |
| `!(N == v)` / `!(N != v)` | `N <> @v OR N IS NULL` / `N = @v` |
| `!(N > v)`, `!(N < M)` | `CASE WHEN N > @v THEN 0 ELSE 1 END = 1` (true when false or unknown) |
| `N > v`, `N > M` | as written |
| `!(N == 5 && Name == a)` | `N <> 5 OR N IS NULL OR Name <> @a OR Name IS NULL` |
| `!(N == 5 \|\| Name == a)` | `(N <> 5 OR N IS NULL) AND (Name <> @a OR Name IS NULL)` |
| `ids.Contains(N)` / `!ids.Contains(N)` | `N IN (…)` / `N NOT IN (…) OR N IS NULL` |
| `{1, null}.Contains(N)` / negated | `N IS NULL OR N = 1` / `N IS NOT NULL AND N <> 1` |
| `{null}.Contains(N)` / negated | `N IS NULL` / `N IS NOT NULL` |
| `Id == 2 && {1, null}.Contains(N)` | `Id = 2 AND (N IS NULL OR N = @p)` |
| `!intIds.Contains(Id)` | `Id NOT IN (…)` |
| `Name != a` / `Req != a` | `Name <> @a OR Name IS NULL` / `Req <> @a` |
| `Name == noName` / `!=` | `Name IS NULL` / `Name IS NOT NULL` |
| `Flag != true`, `Flag != false` (`bool?`) | `Flag <> 1 OR Flag IS NULL` / `Flag <> 0 OR Flag IS NULL` |
| `E != Color.Red` / `E == noColor` | `E <> 0 OR E IS NULL` / `E IS NULL` |
| `!Name.Contains(a)`, `!Name.StartsWith(a)`, `!Name.EndsWith(a)` | `Name NOT LIKE … OR Name IS NULL` |
| `Name.Contains(a) == false`, `!(Name.Contains(a) == true)`, `Name.Contains(a) != true` | `Name NOT LIKE … OR Name IS NULL` |
| `Name.Contains(noName)` (and `StartsWith`, `EndsWith`) / negated | `0 = 1` / no WHERE |
| `D.Value.Year != 2020`, `Name.Length != 3` | `DATEPART(year, D) <> 2020 OR D IS NULL`, `LEN(Name) <> 3 OR Name IS NULL` |
| `N.ToString() != "5"` / `Name.ToString() != a` | `COALESCE(…, '') <> '5'` / `Name <> @a OR Name IS NULL` |
| `(N != 5 ? 1 : 0) == 1` | `CASE WHEN N <> 5 OR N IS NULL THEN 1 ELSE 0 END = 1` |
| `(Id > 3 ? N : M) != 5` | `CASE … END <> 5 OR CASE … END IS NULL` |
| `N > 3 ? M != 5 : Name == a` | the branches compensated |
| `!(N > 3 ? M != 5 : Name == a)` | the negation reaches the branches |
| `(N > 3 ? 1 : 0) != 1`, `(N ?? 0) != 5` | as written (never null) |
| `Parent.Label != a` / `Parent.Code != 5` / `Parent.Code == 0` | `Label <> @a OR Label IS NULL` / `Code <> 5 OR Code IS NULL` / `Code = 0` (a missing parent is NULL) |
| `!Parent.B` (bool) | `B = 0` (a missing parent is excluded) |
| `(N == 5) == (M == 5)`, `(N == 5) != (M == 5)` | the two sides as two-valued truths, compared: `CASE WHEN N = 5 AND N IS NOT NULL THEN 1 ELSE 0 END = CASE WHEN M = 5 … END` |
| `Parent.Code.ToString() != "5"` (`[RemoteProperty] int`) | a bare cast compared, plus `OR … IS NULL` (a missing parent is NULL) |
| `All(N > 0)` | `NOT EXISTS (… WHERE CASE WHEN N > 0 THEN 0 ELSE 1 END = 1)` |

EF Core also translates `N.HasValue` and `N.Value`. FunkyORM rejects both by design (§3).

**#16:** EF Core behaves as FunkyORM does. `Where(…)` returns an `EntityQueryable<T>`, which is an
`IOrderedQueryable<T>`, and the `is IOrderedQueryable` helper throws System.Linq.Expressions' `ArgumentException`.
Pinned in R1.

## 3. Scope

**In scope:**
- Every WHERE through the shared translation: `Query(predicate)`, `QueryAsync`, `Query<T>().Where`,
  `Count`/`LongCount`/`Any`/`All(predicate)`, and `Delete`/`DeleteAsync`.
- All four providers.

**Nothing is newly rejected (rev 4, R3-4).** A `bool` compared with a predicate, as in
`(x.N == 5) == (x.M == 5)`, `x.Flag == (x.N > 5)` or `x.Name.Contains(s) == include`, is rewritten as two-valued
truths compared (AC-N14).

3.11.0 sent `a = b = c = d` for these. That's a syntax error on SQL Server, and it's correct by precedence only for
some operators on SQLite (`<`, `>` bind tighter than `=`) and PostgreSQL (`LIKE` and `IN` bind tighter than `=`).
Even there, it was wrong when an operand is NULL. Rev 3's premise ("wrong on every provider") was false, so rejecting
these would make shapes that returned correct rows throw.

**Out of scope, and why:**
- **SELECT projections and ORDER BY keys.** They have their own visitors. The positive null-item `IN` form does reach
  them, because the translator is shared (AC-N6).
- **`.HasValue` and `.Value` on a nullable property.** Rejected by design; unchanged.
- **Arithmetic, `??`, and bitwise `&` / `|`.** None is translated in WHERE today. Unchanged; the rewrite leaves `&` /
  `|` alone.
- **A collection `Contains` whose item is a cast column** (`nullable.Contains((int?)x.Code)`). Rejected today;
  unchanged, and pinned.
- **Which string columns are nullable.** Every reference-type column counts as possibly null: a NOT NULL string
  gets an `OR col IS NULL` EF omits (the same rows, more SQL). A null value against it is `IS NOT NULL`, so
  `Delete(x => x.Req != noName)` deletes every row, and the guard can't know that. That's C#'s answer and EF's rows;
  it's listed in the Changelog's Behavior change and keyed (`reqNeNoName`).
- **Deletes that are always true in C# only once NULLs count** (R3-6). Examples: `x.N == 5 || x.N != 5`,
  `x.Req != noName`, a conditional compared with itself. The guard proves only the plain shapes (a 3.11.0 Known
  issue), so these were accepted in 3.11.0 and are accepted now. 3.11.0 deleted the non-null rows; 3.12.0 deletes
  every row. They're listed under Behavior change, keyed (`nEq5OrNNe5`), and the Known issue's text is extended.
- **The read-back of a non-nullable property over a SQL NULL.** A missing LEFT JOIN parent, an absent JSON key, or
  an empty `Sum` reads back as `default(T)`, because the materializer skips `DBNull`. WHERE compares the SQL value,
  as EF does. A Known issue; unchanged.
- **MySQL's JSON null.** `JSON_UNQUOTE(JSON_EXTRACT(…))` gives `'null'` text. A Known issue; unchanged.
- **The SQLite `Guid`/`DateTime` binding (FD-11).** Unchanged.

## 4. Acceptance criteria

### 4.1 Definitions

**Accepted row operand:** a row operand the visitors translate. This is the visitors' own list, from round 2's code
read:
- a mapped property, or a property in the remote map;
- a `Convert` of an accepted operand;
- `.Year`/`.Month`/`.Day` of a `DateTime` property (also through `.Value`), and `.Length` of a `string` property;
- `ToString()` with no argument on an integer or `string` property;
- a conditional;
- a string `Contains`/`StartsWith`/`EndsWith` or a collection `Contains` (as a predicate).

Everything else is rejected by the visitor or translator: fields, `[NotMapped]` and discovery-unmapped properties,
nested receivers, other date or `DateTimeOffset` parts, other `ToString`s, arithmetic, `??`, other methods,
`.HasValue`/`.Value`. The rewrite gets the provider's unmapped set and remote map, so it decides the same way.

**Can be null** (`NullSemantics.Classify`) applies to an accepted operand whose SQL value can be NULL:
- a property of `Nullable<T>` or reference type;
- whatever its CLR type: a `[RemoteProperty]`, a `[RemoteKey]`, a `[JsonPath]`, a `[SqlExpression]`, or a
  `[SubqueryAggregate]` other than `Count`/`ConditionalCount`;
- a date part or `Length` of such a property. The witness, the expression tested with `IS NULL`, is that property;
- `ToString()` of a string property, or of an integer property that can be null and isn't `Nullable<T>` (a
  `[RemoteProperty] int`: the translator sends a bare `CAST`). A `Nullable<T>` integer's `ToString()` is
  `COALESCE(…, '')`, so it is never null;
- a conditional with a branch that can be null. Its witness is the conditional as rewritten;
- a `Convert` of any of these.

**Value:** an operand the shared evaluator reads before the query, such as a constant or a captured, static or
nested member.

### 4.2 Criteria

| AC | Criterion |
|---|---|
| **AC-N1** | `x.P != v`, with P able to be null and v a non-null value: also selects the rows where P is null. Either operand order. |
| **AC-N2** | `x.P == v` / `!=` with v null: `P IS NULL` / `P IS NOT NULL` when P can be null. **The whole value operand** is replaced by `Constant(null, operand.Type)`, so `Convert(x.E, int?) == Convert(noColor, int?)` reaches the visitors' bare-null test (R3-2). When P can't be null, `1=0` / `NOT 1=0`, so a delete of `!=` alone is refused. That includes the literal `x.Id == null` / `!= null` (an AC-N11 exception: `id IS NULL` → `1=0`). |
| **AC-N3** | Column against column:<br>• both able to be null: `==` also matches two NULLs, and `!=` takes EF's form;<br>• one able to be null: `!=` also selects that one's NULLs, and `==` is unchanged.<br>This applies to an unfolded self-comparison too. A repeated witness is tested once, asserted in the SQL text. |
| **AC-N4** | `!` reaches the comparisons. De Morgan over `&&` and `\|\|`; `!(a == b)` becomes `a != b` and `!(a != b)` becomes `a == b`; `!` passes through a `bool` conditional into its branches; then AC-N1..N3 apply. A negated `>`, `>=`, `<` or `<=` also selects the rows where an operand that can be null is null. With a null value, it selects every row. **Structure is kept where nothing is compensated:** a negated `==`/`!=` whose flipped form needs no compensation is sent as `NOT (original)`, with any value substituted, so `!(x.N != v)` stays `NOT n != @p`. That's 3.11.0's text, and C#-correct, since NOT unknown is unknown and C# says false. Likewise `!!p` stays `NOT NOT p` and `!(a && b)` stays `NOT (a AND b)` unless a leaf under them is compensated. |
| **AC-N5** | A `>`, `>=`, `<` or `<=` with a null value is `1=0`. The rows are 3.11.0's on SQL Server, MySQL and SQLite. On PostgreSQL the red run decides (§6.5). |
| **AC-N6** | Collection `Contains` on a property that can be null:<br>• a null item also selects the NULL rows, parenthesized: `(P IN (…) OR P IS NULL)`; with only nulls, `P IS NULL`;<br>• negated with no null item: `(NOT P IN (…) OR P IS NULL)`;<br>• negated with a null item: `(P IS NOT NULL AND NOT P IN (…))`; with only nulls, `P IS NOT NULL`;<br>• empty: `1=0` / `NOT 1=0`.<br>On a property that can't be null, 3.11.0's SQL. |
| **AC-N7** | `!x.P.Contains(v)` / `StartsWith` / `EndsWith`: `(NOT P LIKE … OR P IS NULL)`. |
| **AC-N8** | A null search value: positive, no row (`P LIKE NULL`, as in 3.11.0); **negated, every row** (`NOT 1=0`), so a delete of it alone is refused. The same for `StartsWith` and `EndsWith`. This amends D8, per the owner. |
| **AC-N9** | `All(p)` is true when no row satisfies `!p` under AC-N1..N8. |
| **AC-N10** | One path. For one predicate:<br>• Query, QueryAsync, Where and Count send the same WHERE text and parameters, and Delete/DeleteAsync send byte-identical text;<br>• every path selects, counts or deletes the oracle's rows on all four providers, and Any and All answer as the oracle;<br>• the guard's delete verdicts change **exactly** as listed in §4.3. |
| **AC-N11** | No change where C# and SQL already agree: a positive `==` against a non-null value, a positive relational comparison, a positive LIKE, IN without a null item, a literal null on a column that can be null, and every shape whose operands can't be null. These send **exactly 3.11.0's WHERE text and parameters**, `NOT` included, including when a captured value is wrapped in a `Convert`. Pinned literally per provider, and the pins are green at `25956e0` (recorded, §6.5).<br>**Listed exceptions** (R3-8; each keyed, and each in the Changelog):<br>(a) a conditional's test, or a `bool` conditional's branch, that `Fold` knows is now a literal (`NOT 1=0` / `1=0`) where 3.11.0 sent parameters. Two strings there now compare as C# does (ordinal), not under the column's collation: `condCapturedTest`, `condCollationTest`;<br>(b) AC-N2's literal null on a column that can't be null: `id IS NULL` → `1=0`;<br>(c) `All`'s text: `NOT (p)` becomes the translation of `!p`, e.g. `NOT t.id > @p`, the same rows where nothing can be null. |
| **AC-N12** | The documents say what the code does. That covers Changelog `[3.12.0]`:<br>• Behavior change: every wider shape, the verdict table, `reqNeNoName` and the always-true-once-NULLs-count class, the D8 amendment, and AC-N11's exceptions (a)–(c);<br>• Changed: PostgreSQL's comparisons with a null variable, which 3.11.0 failed with 42883, now run; SQL Server's `bool` compared with a predicate, which was a syntax error, now runs;<br>• Fixed;<br>• Known issues: the read-back of a non-nullable property, MySQL's JSON null, PostgreSQL's NULLs-last.<br>It also covers `Usage.md`, `Advanced.md` §5 and the AI instructions. |
| **AC-N13** | Reads (R3-5). For a predicate that translates, the rewrite never causes more reads of a captured value than 3.11.0 does.<br>• **Every value it reads is substituted, and bound from that read.** A non-null value replaces the innermost captured member (`Constant(value, member.Type)`, the `Convert` chain kept). A null value replaces the whole operand (AC-N2).<br>• When no rule applies, the comparison keeps its operator and any `!`; only the member is swapped.<br>• It reads a comparison's value only when the comparison's row operand is accepted.<br>• A conditional's captured members are substituted before the conditional is duplicated as a witness.<br>A predicate the translator rejects at an earlier operand may read a later value once (e.g. `x.Child.Id == 1 \|\| x.N != c.Five`); that's outside "translates". |
| **AC-N14** | A `bool` compared with a `bool`, when one side is a predicate (a comparison, a logical operator, `!`, a method call, or a `bool` conditional), is rewritten as two-valued truths compared. Each side is folded first, so a side that doesn't read the row is a value (R3-4):<br>• **a predicate against a `bool` value** v (read once): true → p; false → !p; a null `bool?` → `false` for `==` and `true` for `!=` (C#'s lifted comparison);<br>• **a predicate against a predicate:** `p == q` is `(p && q) \|\| (!p && !q)`, and `p != q` is its negation, pushed down;<br>• **a `bool`/`bool?` column m against a predicate p:** `(m == true && p) \|\| (m == false && !p)` for `==`, and its negation for `!=`, which with AC-N1 selects the rows where m is null.<br>Captured values in p and q are substituted before duplication (one read). |

### 4.3 The delete verdicts that change (AC-N10)

| Shape | 3.11.0 (corrected per R3-6) | 3.12.0 | Why |
|---|---|---|---|
| `!x.Name.Contains(null)` (also `StartsWith`/`EndsWith`) | accepted, deleted nothing | refused: always true | AC-N8: every row |
| `x.Id == 2 \|\| !x.Name.Contains(null)` | accepted, deleted {2} | refused: always true | AC-N8 |
| `!(x.N == 5 && x.Name.Contains(null))` | accepted, deleted {5,6} | refused: always true | AC-N8, De Morgan |
| `x.Name.Contains(none) == false`, `!= true`, `!(… == true)` | accepted, nothing (SQLite, PostgreSQL, MySQL); a SqlException on SQL Server | refused: always true | AC-N14, AC-N8 |
| `!(x.N >= none)` and the other negated relational comparisons with a null value | accepted, nothing (PostgreSQL: 42883) | refused: always true | AC-N4 |
| `x.Id == 2 \|\| !(x.N >= none)` | accepted, deleted {2} (PostgreSQL: 42883) | refused: always true | AC-N4 |
| `x.Id != none`; `!(x.Id == none)` | accepted, nothing (PostgreSQL: 42883) | refused: always true | AC-N2 fold |
| `x.Id != null`; `!(x.Id == null)` (literal) | accepted, **every row** (`id IS NOT NULL`, `NOT id IS NULL`) | refused: always true | AC-N2 fold |
| Any shape whose only literal was a dropped null's `1=0` or `NOT 1=0`: `!onlyNull.Contains(x.N)`, `x.Id == 2 \|\| !onlyNull.Contains(x.N)`, `!(x.Id == 2 && onlyNull.Contains(x.N))` | refused (`NOT 1=0`, or `NOT (t.id = @p AND 1=0)`, which the literal check calls true) | accepted (`n IS NOT NULL`, …) | AC-N6 |
| Count/All over a reverse join whose only remote reference folds away (`x.ReverseInt > none`) | refused (fan-out) | accepted (`1=0`; no join needed) | the WHERE no longer names the alias |
| `x.Req != noName`; `x.N == 5 \|\| x.N != 5`; any delete always true once NULLs count | accepted, the non-null rows | **accepted, every row** | §3: C#'s answer, which the guard can't see |

## 5. Design

### 5.1 One rewrite in Core: `Funcular.Data.Orm.Linq.NullSemantics`

A public static class.

**`Expression Rewrite(LambdaExpression predicate, IDictionary<Expression, bool?> memo, Func<PropertyInfo, bool> isMapped, IReadOnlyCollection<string> remoteProperties)`**
returns a body with C#'s (EF's) value for every row, built only from shapes the visitors translate.
- It recurses over **truth positions**, carrying a polarity. These are:
  - the body;
  - the operands of `&&`, `||` and `!`;
  - a `bool` conditional's branches;
  - the **test of every conditional the rewrite reaches** (R3-1), whether the conditional is in a truth position or
    is a comparison operand, as in `(x.N != 5 ? 1 : 0) == 1`. The test is rewritten positive. A non-`bool`
    conditional's branches stay values.
- Each step returns either "unchanged" (the node as it is, or wrapped in `!` under negation) or a new expression.
  Unchanged propagates up, so a subtree with nothing to compensate keeps 3.11.0's structure (AC-N4, AC-N11).

**Rules at a truth position:**
1. `WherePredicateGuard.Fold` runs with the visitor's memo, and a known truth becomes a `bool` constant (negated
   under negation). The visitors send a constant in every truth position as a literal (§5.3).
2. `!e` flips the polarity.
3. `&&` / `||` take the rewritten operands, with the operator swapped under negation (De Morgan) only if an operand
   changed; otherwise unchanged. `&` and `|` are untouched.
4. A `bool` conditional: its test is rewritten positive, and its branches carry the polarity.
5. AC-N14: a `bool` against a `bool` with a predicate side:
   1. Each side is folded first, so a side that doesn't read the row becomes a value.
   2. A predicate against a value: true → p; false → !p; null (`bool?`) → false for `==`, true for `!=`.
   3. A predicate against a predicate or a `bool` column: the two-valued expansion. The column m becomes
      `m == true` / `m == false`.
   4. The captured values on both sides are substituted first, so duplication reads nothing twice.
   5. The expansion is then rewritten under the current polarity.
6. `==` / `!=`:
   - Under negation the operator flips. `Expression.Equal`/`NotEqual` re-resolve operator methods (`string`,
     `decimal`, `DateTime`, lifted forms).
   - Each operand is classified as a value, an accepted row operand that can be null (with its witness), one that
     can't be null, or not accepted.
   - In order:
     1. Both values: left as is.
     2. Any operand not accepted: left as is, and no value is read (AC-N13).
     3. A null value against an operand that can be null: `row == null` / `row != null`.
     4. A null value against an operand that can't be null: `false` / `true`.
     5. A non-null value against an operand that can be null: `!=` becomes `(cmp) || W == null`; `==` is unchanged.
     6. Both row operands: EF's forms, with each witness once.
   - **Substitution (R3-2, R3-5).** Every value read is substituted:
     - **A non-null value** replaces the innermost captured member with `Constant(value, member.Type)`, keeping the
       `Convert` chain. The visitors drop a `Convert` and bind the member's value, so the binding is 3.11.0's.
     - **A null value** replaces the whole value operand, with `Constant(null, operand.Type)` in step 3 or a fold in
       step 4, so the visitors' bare-null test sends `IS NULL`.
     - A value is read whenever the row operand is accepted, including a non-null value against an operand that
       can't be null, where no step applies.
   - **When no step changes the comparison**, it keeps its operator, and under negation it is `!(original)`, with
     the member swapped. So `!(x.Id == v)` stays `NOT t.id = @p`.
   - **A conditional used as a witness** (`CASE … END IS NULL`) is the rewritten conditional, with its captured
     members substituted before it is duplicated.
   - `W == null` is `Equal(W, Constant(null, T?))`, through `Convert` to `Nullable<T>` for a non-nullable value
     type.
   - **Classification by type only, no read:** a conditional's value branch can be null when its type is nullable
     (`x.Id > 2 ? x.Code : none`). A `Convert` with a user-defined operator method (not a core-library one) makes
     the operand not accepted, as `Fold` treats it.
7. `>`, `>=`, `<`, `<=`:
   - a null value (read once): `false`, or `true` under negation;
   - under negation: `!(cmp) || W1 == null [|| W2 == null]`;
   - otherwise the original node.
8. Other leaves, such as a method call or a `bool` member: unchanged, wrapped in `!` under negation. The translator
   answers a negated method call (§5.2).

**AC-N11 holds by construction.** A subtree is rebuilt only where a leaf is compensated or folded. A substitution
alone keeps the structure and the binding. AC-N11's exceptions (a)–(c) are the only other text changes.

**AC-N13 holds by construction, for predicates that translate.** Values are read at most once per comparison, and
only when the row operand is accepted. `Fold`'s own reads of a captured `bool` (WherePredicateGuard.cs:507) are as
in 3.11.0, and the memo makes a second `Fold` of the same node a lookup.

**The unmapped set and the remote map** are the visitor's own fields; it passes them to `Rewrite`. No
`OrmDataProvider` file changes.

**Public API:**
- `NullSemantics.Classify(Expression operand, ParameterExpression row, Func<PropertyInfo, bool> isMapped, IReadOnlyCollection<string> remoteProperties)`
  returns `(kind, witness)`.
- `NullSemantics.CanBeNull(PropertyInfo)` is the property-level rule the translators use.

### 5.2 The translators answer a negated method call

Each `*ExpressionTranslator.TranslateMethodCall` gains an overload with `bool negated`. The old signature calls it
with `false`.

| Shape | Negated | Negated, null value | Positive |
|---|---|---|---|
| String `Contains`, `StartsWith`, `EndsWith` (three branches, each checks for a null value) | `(NOT col LIKE … ESCAPE '!' OR col IS NULL)` | `NOT 1=0` | unchanged: `col LIKE …`, and `col LIKE NULL` for a null value |

**Collection `Contains`:** the collection is read once, with a null-item flag. The SQL follows AC-N6, parenthesized,
when `CanBeNull(property)`. Otherwise it is 3.11.0's SQL.

**Row-independent `Contains`:** `NOT ` + the literal.

### 5.3 The visitors and `All`

**`Visit(lambda)`** visits `NullSemantics.Rewrite(…)`. The provider passes its unmapped set and remote map.

**`VisitUnary(Not)`** sends a `bool` method-call operand to the translator with `negated: true`. Every other operand
is written as `NOT ` + the operand.

**`VisitConditional`** visits the `Test` through `VisitPredicate`, and also both branches when the conditional is
`bool`. So a constant there is `NOT 1=0` / `1=0`, not a parameter. A non-bool branch is visited as before. This is
AC-N11's one exception.

**`All`:** each LINQ provider translates `x => !p` through the visitor, and drops its `NOT (…)` wrapper.

## 6. Test plan

### 6.1 Harness entities and captured values (§6.3 has the tables)

**Entities:**

| Entity | Purpose | Delete paths |
|---|---|---|
| `NsRow` (`zz_ns_row`) | plain columns only; holds every key of N1–N11 and N14 | yes |
| `NsComputed` (`zz_ns_row`) | `[JsonPath]`, `[SqlExpression]`, `[SubqueryAggregate]` columns: inline SQL, so deletes run; its WHERE is read by a parenthesis-aware outer-WHERE scanner | yes |
| `NsRemote` (`zz_ns_row`) | `[RemoteProperty]` / `[RemoteKey]` through `parent_id` | reads only, plus one delete per kind asserting the database's unknown-column error (unchanged) |
| `NsReverse` (`zz_ns_parent`) | one reverse property (the inferred one-argument form) | the verdict-table row only |

**Captured values:**

| Name | Value |
|---|---|
| `v` | `(int?)5` |
| `none` | `(int?)null` |
| `a` | `"a"` |
| `noName` | `(string)null` |
| `noColor` | `(Color?)null` |
| `ids` | `int?[] {1, 2}` |
| `idsWithNull` | `int?[] {1, null}` |
| `onlyNull` | `int?[] {null}` |
| `intIds` | `int[] {1, 2}` |
| `names` | `string[] {"a", "b"}` |
| `parentKeys` | `int[] {1}` (an `int?[]` would make the item a cast, which §3 leaves rejected) |
| `shortNull` | `(short?)null` |
| `include` | `false` (a captured `bool`) |
| `c` | a `Holder` with counted getters: `Five` (int 5), `ShortVal` (short 5), `IntVal` (int 5), `ColorVal` (`Color.Green`), `NullableFive` (`int?` 5), `Dt` (a `DateTime`), `Flag` (`true`), `Flip` (alternates null and 5) |

### 6.2 The AC → test matrix

Each key is labeled per provider:
- **proof:** its rows, verdict or SQL differ between `25956e0` and the AC, and the red run must fail it with the
  asserted rows, SQL or verdict, not with an exception;
- **pin:** it passes at `25956e0` and after;
- **rows-pin / SQL-proof:** the rows are equal, and the SQL is asserted and differs;
- **dialect-rejected:** the database rejects it on that provider both before and after;
- **provider-rejected:** the driver rejects it before and after, for a reason outside this plan (PostgreSQL's CLR
  enum parameters, backlog Appendix A #5);
- **red by the database's own rejection** (R3-7): a proof whose 3.11.0 run fails with that database's error:
  PostgreSQL's 42883 for every null-parameter key, and SQL Server's syntax error for every AC-N14 key. 3.12.0
  runs them.

A proof that passes at `25956e0` is a TEST-GAP, fixed before Task 2.

| AC | Proofs | Pins and rows-pin/SQL-proof | Where |
|---|---|---|---|
| N1 | `nNeV`, `nNe5`, `vNeN`, `nameNeA`, `aNeName`, `flagNeTrue`, `flagNeFalse`, `dYearNe2020`, `nameLengthNe1`, `eNeRed` and `eNeGreenLiteral` (provider-rejected on PostgreSQL), `condNullableNe`. The **Convert twins on nullable columns** (R3-3), each asserting 3.11.0's exact comparison and parameters plus ` OR col IS NULL`: `nNeCapturedShort` (`x.N != c.ShortVal`), `nLongNeCapturedInt`, `nPriceNeCapturedInt`, `colorNeCapturedColor` and `eNeGreenLiteral` (provider-rejected on PostgreSQL) | `reqNeA` rows-pin/SQL-proof | `NsRow`, all four |
| N1, attributes | `parentLabelNeP`, `parentCodeNe5` (`[RemoteProperty] int`), `parentKeyNe1` (`[RemoteKey] int`), `docKNe5` (`[JsonPath]` string), `nPlusOneIntNe6` (`[SqlExpression("{N} + 1")] int`), `childSumDNe10` (`[SubqueryAggregate(Sum)] decimal`), `parentCodeToStringNe5` (bare CAST) | `childCountNe1`, `parentCodeEq0`; MySQL `docKEqNone` (JSON null, Known issue) | `NsComputed`, `NsRemote`, all four |
| N2 | `nEqNone`, `nNeNone`, `noneNeN`, `nameEqNoName`, `nameNeNoName`, `eEqNoColor` and `nEqShortNull` (a null under a `Convert`, R3-2: C# {3,4}; 3.11.0 {}), `idNeNoneInt`, `idEq2OrIdNeNoneInt`, `idNeNullLiteral`, `notIdEqNullLiteral`, `notIdEqNone`, `condFoldedTest` (`(x.Id == none ? x.N : x.M) != 5`: rows {5,6} → {2,4,5,6}), `nEq5OrNNe5` (every row; the delete accepted, §4.3) | `idEqNoneInt` and `idEqNullLiteral`: rows-pin/SQL-proof (`1=0`); `reqNeNoName` (rows equal; the delete verdict pinned as accepted, every row) | `NsRow` |
| N3 | `nEqM`, `nNeM`, `nNeCode`, `nameLengthSelf` (the de-duplicated text asserted) | `nEqCode` | `NsRow` |
| N4 | `notNEqV`, `notNGtV`, `notNGeNone`, `notAndEq`, `notOrEq`, `nNe5OrNameEqA`, `notNameLengthGt1`, `condTestNe` (the test of a non-`bool` conditional, R3-1), `idOrNotNGeNone` | `notNNeV` (`NOT n != @p`, kept: the flipped `==` needs nothing); `doubleNot` (`NOT NOT …`); `notBoolCond`, `boolCondBranches`: dialect-rejected on SQL Server (reads and delete), proofs elsewhere | `NsRow` |
| N5 | `nGtNone` on PostgreSQL (red by 42883) | `nGtNone` rows-pin/SQL-proof (`1=0`) elsewhere; `nGtV` a plain pin | `NsRow` |
| N6 | `idsWithNullContainsN`, `onlyNullContainsN`, `notIdsContainsN`, `notOnlyNullContainsN`, `id3AndIdsWithNullContainsN` (C# {3}; 3.11.0 {}; without parentheses {3,4}), `id3AndNotIdsContainsN` (C# {3}; 3.11.0 {}; without parentheses {3,4}), `notNamesContainsName` (C# {2,4,6}; 3.11.0 {6}), `notParentKeysContainsParentKey` (`NsRemote`) | `idsContainsN`, `notIntIdsContainsId`; `notIdsWithNullContainsN` rows-pin/SQL-proof (`(n IS NOT NULL AND NOT n IN …)`); `CastItemContains_IsRejected` | `NsRow`, `TranslatorDirectHarness` |
| N7 | `notNameContainsA`, `notNameStartsWithA`, `notNameEndsWithA` | — | `NsRow`, `TranslatorDirectHarness` |
| N8 | `notNameContainsNull`, `notNameStartsWithNull`, `notNameEndsWithNull`, `idOrNotNameContainsNull`, `idAndNotNameContainsNull`, `notAndContainsNull` | `nameContainsNull` | `NsRow`, `TranslatorDirectHarness` |
| N9 | `All_AnswersAsTheOracle`: `allNGt0`, `allNameContainsNull`, `allNIn517`, `firstTwoAllNameContainsA` (`Where(x => x.Id <= 2).All(x => x.Name.Contains("a"))`: EF false, 3.11.0 true) | `allNNe5`, `allReqNeZz`, `allNotNameContainsNull` | all four |
| N10 | every key's parity assertions, and the §4.3 table in `VerdictChanges_AsListed` (one row per line) | the existing `WhereParityHarness`/`DeleteGuardHarness` rows outside §7 | all four |
| N11 | — | `NonNullableShapes_SendTheSqlOf3110`, literal WHERE plus parameters, per provider, captured at `25956e0`: `idNe5`, `idEq5`, `notIdEq5`, `notCodeGt5`, `notIntIdsContainsId`, `nameEqA`, `nEq5`, `nameContainsA`, `nEqNullLiteral`, `nGt5`, `condNe`, `capturedNEqV`, `codeNeCapturedShort`, `longNeCapturedInt`, `priceNeCapturedInt`, `notIdEqCapturedV`, `idNeCapturedNullableFive` (a non-null value against a column that can't be null: read, substituted, unchanged), `doubleNot`, `notNNeV`, `nEqCode`, `idsContainsN`. Exceptions (a): `condCapturedTest` (SQL-proof), `condCollationTest` (`(s == "A" ? x.Id : 0) > 0` with `s = "a"`: rows differ on SQL Server and MySQL's case-insensitive collations). (b): `idEqNullLiteral`. (c): `allNGt5Text` | all four |
| N12 | `DocumentationTruthTests.NullComparisons_DocumentedAsCSharp` | — | SqlServer.Tests |
| N13 | `ReadOnce_CountingGetter`. Pins that kill M-N13: `x.N != c.Five` reads 1, `c.Five != x.Child.Id` reads 1 (as 3.11.0), `x.Child.Id != c.Five` reads 0, `c.Flag != x.Flag` reads ≤ 3.11.0's count, `(x.N == c.Five) && x.M == 5` reads 1. Proof: `x.N != c.Flip` (the WHERE form agrees with the bound value). Also: `x.Id != c.NullableFive` reads 1 (no step applies; SQL pinned); `(x.Id > 2 ? x.N : c.Flip) != 5` reads 1 (the conditional is substituted before it's duplicated) | — | all four |
| N14 | `nameContainsAEqFalse`, `notNameContainsAEqTrue`, `nameContainsANeTrue`, `nameContainsAEqInclude` (`include` false → `!p`), `nEq5EqMEq5`, `nEq5NeMEq5`, `flagEqNGt5`, `flagNeNGt5` (the expansion; on SQL Server red by its syntax error, elsewhere by rows where an operand is null) | `nameContainsAEqTrue`: rows-pin on SQLite, PostgreSQL, MySQL; a SQL Server proof. `flagEqNGt5` on SQLite: rows-pin/SQL-proof (SQLite reads `flag = n > @p` as `flag = (n > @p)` and no seed row has a null `n` with a false `flag`; the WHERE must carry `n IS NULL`) | all four |
| core | `NullSemanticsTests` (DB-free, C# 7.3): `Rewrite_KeepsCSharpsValue`, `Rewrite_ReturnsTheOriginalNode…`, `Classify_ByOperandKind` (every §4.1 row), `CanBeNull_ByPropertyKind`, `Rewrite_FoldedPartsStayConstants`, `Rewrite_LeavesBitwiseAlone`, `Substitution_KeepsTheConvertChain` (short→int?, int→long?, int→decimal?, `Color`→`int?`, `DateTime`→`DateTimeOffset?`: the constant has the member's type and the `Convert` chain is unchanged), `NullValue_ReplacesTheWholeOperand`, `Expansion_SubstitutesBeforeDuplicating`, argument-null checks | — | SqlServer.Tests, linked into `Funcular.Data.Orm.SqlServer.Tests.NetFramework` |
| R1 | `SortHelperTests` (committed) plus `HelperAfterSkip` | pins | SqlServer.Tests |
| R2 | `AttributeExamples_…` (committed) | — | SqlServer.Tests |
| R3 | `CiWorkflowTests` (committed), plus: every test job runs the TRX gate (§8 R3); no `continue-on-error`; no `\|\| true`; no `--filter` other than the PerformanceTests/EntityFramework exclusion, or none; no job-level `if:` (exactly four spaces) on a test job. The gate script runs on fixture TRX files: clean; an allowed inconclusive; an unexpected one; one method name in two classes, one allowed (R3-9); no results | — | SqlServer.Tests |
| R5 | `PackagingTests.VersionCarriers_MatchTheChangelog`: the five csprojs (all four fields), and `tools/consumer-smoke`'s three defaults (Run-ConsumerSmoke.ps1, run-consumer-smoke.sh, ConsumerSmoke.csproj) | — | SqlServer.Tests |

### 6.3 Tables and seed

**`zz_ns_row`:**
- `id`, `n` int, `m` int, `name`, `req` NOT NULL, `flag`, `d` datetime, `code` NOT NULL, `e` int, `long_col` bigint
  NOT NULL, `price` decimal(10,2) NOT NULL, `n_long` bigint, `n_price` decimal(10,2), `parent_id` int, `doc`.
- `n_long` and `n_price` are `n` widened (NULL where `n` is): the nullable twins of the `Convert` pins (R3-3). The
  `DateTime`→`DateTimeOffset` substitution is proved DB-free (`Substitution_KeepsTheConvertChain`), so no `dto`
  column (R3-7).
- `doc` is JSON: NVARCHAR(200) on SQL Server, JSONB on PostgreSQL, JSON on MySQL, TEXT on SQLite.
- Every property has an explicit `[Column]`.

**`zz_ns_parent`:** `id`, `label`, `pcode` NOT NULL.

**`zz_ns_child`:** `id`, `row_id` (`[Column("row_id")]`), `amount` DECIMAL(10,2).

| id | n | m | name | req | flag | d | code | e | long_col | price | n_long | n_price | parent_id | doc |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 5 | 5 | 'a' | 'a' | true | 2020-06-01 | 5 | 0 | 5 | 5.00 | 5 | 5.00 | 1 | `{"k":"5"}` |
| 2 | 5 | NULL | NULL | 'b' | false | NULL | 6 | NULL | 6 | 6.00 | 5 | 5.00 | 2 | `{"k":null}` |
| 3 | NULL | 5 | 'b' | 'a' | NULL | 2021-01-01 | 7 | 1 | 7 | 7.00 | NULL | NULL | NULL | `{}` |
| 4 | NULL | NULL | NULL | 'c' | NULL | NULL | 8 | NULL | 8 | 8.00 | NULL | NULL | 1 | NULL |
| 5 | 7 | 8 | 'a' | 'd' | true | 2020-01-01 | 5 | 0 | 5 | 5.00 | 7 | 7.00 | 2 | `{"k":"7"}` |
| 6 | 1 | 1 | 'ca' | 'a' | false | 2022-03-03 | 1 | 1 | 1 | 1.00 | 1 | 1.00 | NULL | `{"k":"1"}` |

**Parents:** `(1, 'p', 5)`, `(2, NULL, 7)`.

**Children:** `(1, 1, 10)`, `(2, 1, 5)`, `(3, 5, 10)`. Row 1 sums to 15 and row 5 to 10; the others have none.

**JSON path:** `"$.k"`, which every dialect accepts.

**Oracle:** EF's answer, lifted to nullable:
- C#'s value where C# doesn't throw;
- a null-safe form where it would, such as `!(x.Name != null && x.Name.Contains("a"))`;
- the parent joined in C#, with a missing parent as null;
- the sum over no children as null;
- the JSON key as null when it's absent or a JSON null (not on MySQL, Known issue).

Expected ids come from LINQ to objects.

**Assertions:** those of `WhereParityHarness`, plus `Any`.
- `ExpectedDeleteRejection` follows §4.3.
- A **dialect-rejected** path asserts a `DbException` on the reads and on the delete, then rolls back.
- After any failed DELETE, the transaction is rolled back before `Remaining()`, because PostgreSQL aborts it.

### 6.4 Mutations (each run once against the finished code, then reverted)

| M | Mutation | Killed by |
|---|---|---|
| M-N1 | Drop `\|\| W == null` from `!=` | N1 proofs |
| M-N2 | Don't rewrite a null value | N2 proofs |
| M-N2b | No fold for an operand that can't be null | `idNeNoneInt`, `notIdEqNullLiteral` |
| M-N2c | A null value substituted at the member under its `Convert` (R3-2) | `eEqNoColor`, `nEqShortNull` |
| M-N3 | No De Morgan | `notAndEq`, `notOrEq` |
| M-N4 | No negated-relational compensation | `notNGtV`, `notNameLengthGt1`, `allNGt0` |
| M-N4b | No recursion into a conditional | `condTestNe`, `boolCondBranches` (not SQL Server) |
| M-N4c | `VisitConditional` back to `Visit` for the test | `condCapturedTest` (its literal text, all four) |
| M-N4d | No rewrite of a non-`bool` conditional's test (rev 3's gap, R3-1) | `condTestNe` |
| M-N5 | Drop null items from `IN` | `idsWithNullContainsN`, `onlyNullContainsN` |
| M-N5b | Drop the parentheses | `id3AndIdsWithNullContainsN`, `id3AndNotIdsContainsN` |
| M-N6 | Negated LIKE without `OR col IS NULL` | N7 proofs |
| M-N7 | A negated null search selects no row | `notNameContainsNull`, `allNameContainsNull` |
| M-N8 | `All` wraps the SQL in `NOT (…)` | `allNGt0`, `allNIn517` |
| M-N9 | Compensate operands that can't be null | N11 pins |
| M-N10 | `CanBeNull` false for reference types | `nameNeA`, `notNamesContainsName` |
| M-N11 | Classify by CLR type only | `parentCodeNe5`, `parentKeyNe1`, `nPlusOneIntNe6`, `childSumDNe10` |
| M-N12 | Both-null `==` without `(a IS NULL AND b IS NULL)` | `nEqM` |
| M-N13 | Substitute at the operand (drop the `Convert`), or read again in the visitor | `nNeCapturedShort`, `nPriceNeCapturedInt`, `nLongNeCapturedInt` (exact text and values), `Substitution_KeepsTheConvertChain` (the constant's type), `ReadOnce_*` |
| M-N14b | No two-valued expansion (3.11.0's `a = b = c = d`) | `nEq5EqMEq5`, `flagEqNGt5` |
| M-N14 | `p == false` left as is | `nameContainsAEqFalse` |
| M-N15 | Repeated witness not de-duplicated | `nameLengthSelf` (text) |
| M-R2 | (killed: R2's red run, 17 failures) | |
| M-R3 | (killed, `2a9223b`) | |

### 6.5 Red run, suites, coverage, inconclusive

- **Red run at `25956e0` (Task 1).** Every new test runs with the production code unchanged, on all four providers.
  Recorded per key:
  - pass or fail;
  - for failures, the assertion message, which must be the asserted rows, SQL or verdict;
  - PostgreSQL's 3.11.0 answer for the null-parameter keys (42883 or rows).
- **The N11 pins** are captured in this run. They must be green on it.
- **Recorded (rev 4 harness, 2026-10-20; the TRX files are kept outside the repo):** 132 tests per provider; failed
  / passed: SQLite 97 / 35, SQL Server 96 / 36, PostgreSQL 92 / 40, MySQL 97 / 35.
  - Every failure is the asserted rows, SQL or verdict, or the database's own rejection: PostgreSQL's 42883 for a
    null parameter (and 42804 for one in a `CASE` branch, `ReadOnce`), and SQL Server's and PostgreSQL's syntax
    error for the AC-N14 keys.
  - The passes are exactly the pins: the 21 N11 rows; `doubleNot`, `notNNeV`, `nEqCode`, `idsContainsN`,
    `notIntIdsContainsId`, `nGtV`, `nameContainsNull`; `allNNe5`, `allNotNameContainsNull`, `allReqNeZz`;
    `childCountNe1`, `parentCodeEq0`, `castItemContains`; `nameContainsAEqTrue` on SQLite, PostgreSQL and MySQL. The
    dialect- and provider-rejected keys pass where they're rejected: `notBoolCond`, `boolCondBranches` on SQL Server;
    `eNeRed`, `eNeGreenLiteral`, `colorNeCapturedColor` (rows and Convert twin) on PostgreSQL.
  - The Convert twins' 3.11.0 text is captured beside the N11 text (26 entries per provider, 24 on PostgreSQL).
- **Suites:** SQL Server with the CI filter (`.\SQL2019`), PostgreSQL (local service), MySQL
  (`FUNKY_MYSQL_CONNECTION`) and SQLite. Run serially, with `zz_` tables only.
- **Inconclusive counts as NOT RUN.** The handoff gives passed, failed and inconclusive per suite. Each inconclusive
  must be on that suite's allowlist (§8 R3); any other makes the suite not green.
- **net48:** VS MSBuild plus vstest, with `NullSemanticsTests` linked in (C# 7.3, MSTest 2.2.10). If the toolchain
  is missing, it's stated as not run.
- **Coverage:** coverlet per touched file, at least 85% of lines, as the union of the suites that run it. That's
  `NullSemantics.cs` plus the four visitors (`VisitConditional` included), the four translators and the four LINQ
  providers. Baselines for all twelve provider files are recorded in Task 1. There's no waiver.

## 7. Pins expected to flip

Every flip is listed in the handoff with its old and new value. Any other broken pin is a regression.

- **`CapturedValueHarness`:**
  - `notContainsNullValue`: rows `2` → every row. The fragment `OR NOT … LIKE NULL)` becomes `OR NOT 1=0)`. Its
    delete row moves to the always-true rejection.
  - `allContainsNull`: true → false.
  - `allOrContainsNull`: true → false. The D8 tag's text is amended.
  - `allNullableGreaterThanZero`: true → false. The KnownIssue tag and its machinery go.
  - The comments at lines 813–818 are rewritten, and `All_UnknownCountsAsSatisfied` is renamed `All_AnswersAsCSharp`.
- **`TranslatorDirectHarness`:** `CollectionContains_NullValuesSkipped` → `CollectionContains_NullItems_…`,
  parenthesized. The comment at line 272 is rewritten.
- **`LinqScopeHarness`** lines 325–326, four classes: `WHERE NOT ({col} = ` → `WHERE ({col} != `.
- **`DeleteGuardHarness`** `orFirstNameLengthSelf`: `selectedByQuery` 2 → 3. The comment at lines 262–266 is
  rewritten.
- **`WhereParityHarness`:** the SQL Server `ReadRejectedByTheDialect` keys `nameContainsEmptyEqTrue` and
  `nameContainsEmptyNeFalse` now run. Their override goes, and their delete verdicts are unchanged.
  `idOrNameLengthSelf`'s text changes, with the same rows and still refused.
- **The Changelog's 3.11.0 Known issues** for `All` move to `[3.12.0]` Fixed.

## 8. Riders

- **R1 (#16),** `0e941c4`:
  - The pins, plus the `Advanced.md` §5 and `FUNKYORM_AI_ADVANCED.md` notes; rev 3 adds the `Skip` row.
  - The message comes from System.Linq.Expressions, so it doesn't depend on EF Core's version.
  - FunkyORM's side runs on SQLite, because `Query<T>()` discovers columns.
  - Closing #16 needs the owner.
- **R2,** `923137a`: 17 examples in user-facing Markdown (`Usage.md` isn't packed). The reflection check doesn't
  cover positional types or order, enum names, or the parameter tables' prose.
- **R3,** `2a9223b`:
  - test-sqlite runs on windows-latest.
  - Triggers: pushes to `main`, `master`, `development/**`, `release/**`; pull requests to `main`, `master`,
    `development/**`.
  - Rev 3 adds the **TRX gate** in each test job: `--logger "trx;LogFileName=test-results.trx"`, then a pwsh step
    running `.github/scripts/Assert-TestsRan.ps1 -Trx <path> -Allow <names>`.
  - The gate fails on any result with `outcome="NotExecuted"` (how MSTest records an inconclusive) whose
    `ClassName.MethodName` isn't on the job's allowlist (R3-9). `testName` alone is the bare method name, so a
    result's `testId` is joined to `TestDefinitions/UnitTest/TestMethod@className`. `[Ignore]` also gives NotExecuted;
    none exists today.
  - Allowlists, from CI runs 39382293318 (25956e0) and 38335691262 (the old PostgreSQL workflow, 8ec6b25). 3.11.0's
    PostgreSQL suite has never run in CI, so **the first test-postgresql run re-derives its list**. The handoff
    reports it:
    - **test-net8:** `Projection_BoolValue_GetsCSharpsValue` (by design), `SqlServerThenPostgreSql_SameEntity_BothQueriesRun`
      (no PostgreSQL on a Windows runner), `RemoteProperty_IsPopulated_OnQuery`.
    - **test-postgresql:** the three `PostgreSqlRemoteFeaturesTests` at lines 33, 46 and 57.
    - **test-sqlite, test-mysql:** none.
  - The four data-dependent tests seed their own chain instead (as `SqliteRemoteFeaturesTests.cs:131–141` does) and
    leave the allowlist, if the change is test-only and small. Otherwise they stay listed, and the handoff says so.
  - `CiWorkflowTests` runs the gate script (through pwsh) on fixture TRX files: a clean run, an allowed inconclusive,
    an unexpected one, and a run with no results.
- **R4,** `8929612`.
- **R5,** last. The `tools/consumer-smoke` defaults and two doc comments go with it.

## 9. Tasks

| Task | What | Turns green |
|---|---|---|
| 0 | Task 0 round 3 on rev 3 | — |
| 1 | All new tests; the red run at `25956e0` (§6.5); the N11 pins captured; coverage baselines | pins |
| 2 | `NullSemantics`; the visitors' `Visit(lambda)` and `VisitConditional` | N1–N5, N11, N13, N14 |
| 3 | The translators' `negated` overload, null items, parentheses; the visitors' `VisitUnary(Not)` | N6–N8 |
| 4 | `All` through the visitor (four providers) | N9 |
| 5 | §7 flips; four suites; mutations; coverage | N10 |
| 6 | R1 `Skip` row; R3 TRX gate and checks | R1, R3 |
| 7 | Changelog, Usage.md, Advanced.md, the AI instructions; version 3.12.0 | N12, R5 |
| 8 | Adversarial review by two fresh non-author lenses over `25956e0..HEAD`, until CLEAN | — |

Pushing, the PR, merging and publishing are the owner's calls.

## 10. Risks and decisions

- **Wider rows and deletes, and the verdict changes (§4.3).** This is the reason for a minor version and a prominent
  Behavior change.
- **New rejections (§3).** Each shape gave wrong SQL on every provider before.
- **D8 amended (AC-N8).**
- **SQL text grows** for `!=` and the negated forms on columns that can be null, every string column included.
  Equality doesn't change.
- **The conditional literal (AC-N11's exception).** It applies only where a conditional's test or `bool` branch
  doesn't read the row.

## 11. Review record

### Task 0 round 1 (non-author, over `152d0ba`, 2026-10-20 19:37): NOT CLEAN, 15 findings

Answered in rev 2 (`add1f1e`). The rows' dispositions are unchanged except where round 2 reopened them, in the next
table.

| # | Sev | Blame | Finding | Rev 2 disposition |
|---|---|---|---|---|
| T0-1 | Blocking | AC-GAP | A column that can't be null vs a null value became `IS NOT NULL`, an all-rows delete | AC-N2 fold; keys; M-N2b |
| T0-2 | Blocking | TEST-GAP | The `Disabled` seam was circular and didn't compile in three projects | Literal pins captured at `25956e0`; AC-N11 widened |
| T0-3 | Major | AC-GAP | "Verdicts unchanged" was false | The §4.3 table |
| T0-4 | Major | AC-GAP | Conditionals weren't compensated | Recursion; keys |
| T0-5 | Major | TEST-GAP | The IN null-item forms weren't parenthesized | Parentheses; keys |
| T0-6 | Major | PLAN-GAP | A cast item is rejected | Out of scope, pinned |
| T0-7 | Major | TEST-GAP | Keys didn't distinguish; mutation killers wrong | Proof/pin labels; red run |
| T0-8 | Major | TEST-GAP | No attribute kinds in the harness | Entities, keys, oracle |
| T0-9 | Major | HOUSE-RULE | Double read | AC-N13 |
| T0-10 | Major | HOUSE-RULE | An inconclusive reads as a pass | Gates |
| T0-11 | Major | PLAN-GAP | Unlisted flips | §7 |
| T0-12 | Major | AC-GAP | Predicate comparisons, ToString, enum undefined | AC-N14; classifier |
| T0-13 | Minor | HOUSE-RULE | Interface table, `Classify`, waiver, net48 | §6 |
| T0-14 | Minor | PLAN-GAP | PostgreSQL null typing | §1, AC-N5 |
| T0-15 | Minor | PLAN-GAP | Rider details, probe path | §8; `tools/ef-null-oracle` |

### Task 0 round 2 (non-author, over `add1f1e`, 2026-10-20 20:22): NOT CLEAN, 11 findings

| # | Sev | Blame | Finding | Rev 3 disposition |
|---|---|---|---|---|
| R2-1 | Major | TEST-GAP | Folded constants in a conditional's test reach SQL Server as a bit parameter, so shapes that work in 3.11.0 regress (**fix-introduced**) | `VisitConditional` sends the test and `bool` branches through `VisitPredicate` (§5.3); AC-N11's listed exception; `condCapturedTest`/`condFoldedTest`; M-N4c; `VisitConditional` in the coverage list |
| R2-2 | Major | TEST-GAP | Substituting at the operand's type changes the binding, or throws, under a `Convert` (**fix-introduced**) | Substitute at the member, keeping the `Convert` chain (§5.1 rule 6); seven literal pins; M-N13 |
| R2-3 | Major | PLAN-GAP | The inconclusive gates fail healthy CI (3 by-design or data-dependent on SQL Server, 3 on PostgreSQL), and TRX's inconclusive counter is always 0 | A TRX gate script with per-job allowlists on `NotExecuted`; the data-dependent tests seed themselves where small; fixture-TRX tests of the gate |
| R2-4 | Major | TEST-GAP | Labels contradicted the definition (proofs passing at `25956e0`; pins failing there) | `id3And…` keys; `firstTwoAllNameContainsA`; labels per provider incl. rows-pin/SQL-proof and dialect-rejected; collections defined; the red run records the failure messages |
| R2-5 | Major | TEST-GAP | No non-nullable inline attribute key; M-N10 untested on the translator path; de-dup unasserted | `nPlusOneIntNe6`, `childSumDNe10`; `notNamesContainsName`, `notParentKeysContainsParentKey`; `nameLengthSelf` text; M-N11's killers corrected; M-N15 |
| R2-6 | Major | PLAN-GAP | Harness mechanics: WhereOf under a SELECT subquery, the reverse property, inline-attribute deletes, PostgreSQL aborts, the SQL Server JSON path, column mapping, the SQL Server bool CASE | Four entities (§6.1); the outer-WHERE scanner; deletes on `NsComputed`; rollback; `"$.k"`; `[Column]` everywhere, `row_id`, DECIMAL; a dialect-rejected path |
| R2-7 | Minor | PLAN-GAP | "Accepted" undefined; `Classify` can't see the unmapped set; the ReadOnce rows were pins; `Fold` reads first | §4.1's accepted list; `isMapped` and the remote map passed in; AC-N13 restated as "never more than 3.11.0"; the counted rows labeled as pins that kill M-N13, plus the `c.Flip` proof; predicate-vs-predicate now rejected, so AC-N14 duplicates nothing |
| R2-8 | Minor | AC-GAP | The verdict table missed rows | §4.3 extended (`!(x.Id == null)`, `!(x.Id == none)`, AC-N14 with null, the disjunction); `reqNeNoName`; `idEqNullLiteral` |
| R2-9 | Minor | AC-GAP | Member vs predicate undefined; `ToString` of a non-`Nullable<T>` integer that can be null | Rejected (§3); §4.1's `ToString` rule; `parentCodeToStringNe5` |
| R2-10 | Minor | PLAN-GAP | The oracle missed rows | Ported: Parent rows, null `StartsWith`/`EndsWith`, the parenthesized IN, `{null}`, `Req != noName` |
| R2-11 | Nit | PLAN-GAP | Three consumer-smoke carriers; the net48 project is C# 7.3 | R5; §6.5 |

### Task 0 round 3 (non-author, over `29f4c21`, 2026-10-20 20:59): NOT CLEAN, 3 Major, 6 Minor, nits

| # | Sev | Blame | Finding | Rev 4 disposition |
|---|---|---|---|---|
| R3-1 | Major | AC-GAP | Rev 3 dropped the rewrite of a non-`bool` conditional's test (**fix-introduced**); `condTestNe` couldn't pass; M-N4c's killer didn't kill | Every reached conditional's test is a positive truth position (§5.1); M-N4c → `condCapturedTest`; M-N4d |
| R3-2 | Minor | TEST-GAP | A null value substituted at the member under a `Convert` sends `= NULL` (**fix-introduced** by R2-2) | AC-N2 / §5.1: a null value replaces the whole operand; `nEqShortNull`; M-N2c |
| R3-3 | Major | TEST-GAP | Two "N11 pins" were on a nullable column; the other five didn't exercise the substitution (**fix-introduced** by R2-2) | `eNeGreenLiteral`, `colorNeCapturedColor` → N1 proofs; nullable twins `n_long`/`n_price`/`x.N != c.ShortVal` with exact text; M-N13's killers |
| R3-4 | Major | AC-GAP | "Wrong on every provider" was false (SQLite and PostgreSQL precedence), so the rejection broke working shapes | No rejection: AC-N14's two-valued expansion, as EF does; fold each side first; a predicate vs a `bool` value defined |
| R3-5 | Minor | TEST-GAP | The read rule was ambiguous; a duplicated conditional re-read; "never" overclaimed | AC-N13: every read substituted, structure kept when no step applies, the conditional substituted before duplication, scoped to predicates that translate; two ReadOnce rows |
| R3-6 | Minor | AC-GAP | The verdict table missed a row, had wrong 3.11.0 cells, and missed the always-true-once-NULLs-count class | §4.3 corrected and generalized; §3 class; `nEq5OrNNe5` |
| R3-7 | Minor | TEST-GAP | Label and data errors (`names`, `parentKeys`, `condFoldedTest`, `nGtV`, red-by-exception, no `dto` seed) | Fixed in §6.1–6.3; the "red by the database's own rejection" label; `dto` dropped (core test) |
| R3-8 | Minor | AC-GAP | AC-N11 had more exceptions than the one listed, and the conditional literal can change rows | AC-N11 (a)–(c), in the Changelog; `condCollationTest` |
| R3-9 | Minor | PLAN-GAP | The TRX gate's name isn't class-qualified; the PostgreSQL allowlist predates 3.11.0 | Class-qualified via `testId`; the first run re-derives it; a fixture with one name in two classes |
| R3-10 | Nit | OTHER | Value-branch nullability, user-defined Convert, the unmapped set's source, a missing oracle row, the harness pins | §5.1 (type only; user-defined → not accepted); the visitor holds the sets; §2 row; the harness pins already corrected (`notIdsWithNullContainsN` removed; `notNNeV` kept by AC-N4's structure rule) |

**Deviation (recorded):** after three rounds, rev 4 goes to a focused round-4 delta review. The code (Task 2 on) is
written in parallel, in the working tree, against the recorded red run. Nothing is committed until round 4 is
CLEAN, and any round-4 finding is answered in the plan and the code before the first commit.

**Blame counts:** round 1: AC-GAP 4, TEST-GAP 5, HOUSE-RULE 3, PLAN-GAP 3. Round 2: TEST-GAP 4, PLAN-GAP 4, AC-GAP 3.
Round 3: AC-GAP 4, TEST-GAP 4, PLAN-GAP 1, OTHER 1.
**Two fix-introduced findings in round 2 (R2-1, R2-2):** per the house rule, rev 3 narrows the design instead of
patching.
