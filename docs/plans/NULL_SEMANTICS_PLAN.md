# FunkyORM 3.12.0 — null comparisons in WHERE that answer as EF Core does (plus riders)

> **Status:** rev 23, 2026-10-23. **Task 0 round 23 is CLEAN** (0 Major; its one Minor and one Nit fixed after it, §11).
> - Task 0 rounds 1–22 (15, 11, 10, 14, 10, 9, 8, 5, 5, 5, 6, 4, 5, 4, 7, 7, 7, 8, 5, 7, 7 and 4 findings) were all NOT CLEAN;
>   §11 has every disposition.
> - Task 8 (the final review, lenses A–F over `25956e0..HEAD`, §11): one CI defect (the TRX gate's steps didn't parse),
>   false statements in the user documents fixed lens by lens, and one row defect: a non-nullable `[SubqueryAggregate]`
>   `Sum` compensated for its NULL, so `x.Total != 0` deleted the parents with no children. **The owner's ruling
>   (2026-10-23): "Keep 3.11.0's SQL"**: a non-nullable `Sum` or `Avg` is compared as 3.11.0 compared it; `x.Total == 0`
>   leaving out such a parent is a Known issue.
> - Round 22 found no wrong rows, no over-deletion, no new throw and no twin miss; its one Major was a false comment: an
>   override of the setter alone was still two members (R22-1, fix-introduced). Rev 23 finds a property's first
>   declaration from either accessor, adds the literal arm's neighbours (R22-3), and states the long-chain limit per
>   shape, measured through the real providers: PostgreSQL and MySQL only (R22-2); the owner kept it a Known issue (Paul, 2026-10-23: "A, keep it as a Known issue").
> - Round 21 found no wrong rows, no over-deletion, no new throw and no twin miss; its three Majors were false comments:
>   the under-cast rules never counted an empty search value, though their comment said a literal is still seen
>   (R21-1, fix-introduced), `SelfReferenceMessage`'s doc still said "before and after translation" (R21-3), and the
>   memo and `SameMember` saw an overridden property as two members (R21-4). Rev 22 counts a search value written as a
>   literal under the cast, keys a member by its original declaration, and states what the translated clause's check
>   refuses (R21-2). Self-found on the way (§6.5): a long `||` chain overflows the stack at about 15% fewer operands
>   than in 3.11.0, in every provider's WHERE visitor; a Known issue (owner's ruling: rev 23's bullet).
> - Round 20, like round 19, found no wrong rows, no over-deletion and no twin miss; its three Majors were false
>   statements: a delete read captured values more often than 3.11.0 around a cast through `object` (R20-1), the memo
>   asked a base class's property twice (R20-2), and seven delete docs said the guard checks before translation (R20-3).
>   Rev 21 folds a cast through `object` that reads the row without evaluating under it, keys the memo as `SameMember`
>   compares members, and states every read claim as measured (`delreads`, adopted). So 15 of the fuzzers' deletes that
>   rev 20 refused are accepted, as 3.11.0 accepted each, with C#'s rows (9 of them every row): a Known issue (§6.5).
> - Round 19 found no wrong rows, no over-deletion and no twin miss; its two Majors were false comments: the providers
>   gave the guard's verdict twice, so a delete read a captured value more often than 3.11.0 (R19-1), and the memo, the
>   lambda check and an old arithmetic example didn't do what their comments said (R19-2). Rev 20 gives the verdict once,
>   after translation, keys the memo by member, refuses any lambda, and keeps quoted text's letter case (R19-3); the
>   twin fuzzer gains `<=`, `!(<)` and `!(>)` (`fuzz19t`).
> - Round 18 found two Majors. The verdict missed a twin that only the provider's SQL shows (a template that is the
>   dialect's own text for a column's part, two templates that differ only in letter case, two `[JsonPath]` properties on
>   one path): inside a nested conditional the copy is distributed, so 3.11.0's refusal became a delete of every row
>   (R18-1). And the verdict table's new rows, and five XML docs, stated verdicts and rules the code doesn't give (R18-2,
>   fix-introduced). The guard's class for the third round running, so the test was redesigned first: round 18's twin
>   fuzzer `fuzz18t` joins the self-check (2,613 misses at rev 18 over 24,000 shapes). Rev 19's providers pass their SQL
>   for a part to the guard, which compares two operands part by part and asks that SQL where two small parts differ in
>   shape (§5.3); `fuzz18t` finds no miss. The new refusals are recorded in §4.3 and §6.5.
> - Round 17 found two Majors in the delete guard, both deletes that 3.11.0 refused and rev 17 ran: the text check's
>   two-witness pattern split an operand whose own SQL held `IS NULL AND` in the wrong place (R17-1, fix-introduced), and a
>   nested conditional compared with its own copy or its alias's was distributed, so the whole-clause text check never saw
>   the copy (R17-2). The second fix-introduced round in a row, so the test was redesigned first: `fuzz18s` compares an
>   operand with a copy of itself under every wrapper, through the real SQLite provider, against 3.11.0's verdict (733
>   misses at rev 17 over its 24,000 shapes; 373 over the first 12,000). Rev 18 decides a self-comparison from the
>   expression, for any type, an alias seen as its column, and parses the text check's form in linear time; `fuzz18s`
>   finds no miss. The verdict now refuses more always-true deletes
>   (recorded in §4.3 and §6.5), and `>=`/`<=` self-comparisons of values that can be NULL, as 3.10's rule refuses `x.N >= x.N`.
> - Round 16, judged by the owner's exit rule, found two Majors: an always-true delete accepted where a computed
>   property whose template is a column's `Length` or date part met that part of the column, EF's form then testing two
>   different witnesses (R16-1; 3.11.0 refused it), and a false statement in the documents, that every kind of remote
>   property "can hold CASEs" (R16-2, fix-introduced: only an `[SqlExpression]` template can). Rev 17 lets the guard's
>   two-witness form take two witnesses the operand reads, withdraws rev 16's refusal for any remote read, and counts an
>   `[SqlExpression]` template's own levels in the depth count instead, so an alias without one distributes as a column
>   does and its chains grow linearly again (R16-3). The Minors and the Nit restate the + 2 of a remote integer's
>   `ToString()` (R16-4), list two refused always-true delete classes (R16-5), and pin a string's `ToString()` (R16-6).
> - Round 15 found three Majors. Two nested sides were distributed left first, so a chain whose earlier level sat on the
>   right of a shallow nested conditional repeated that level threefold per level inside the bound (R15-1,
>   fix-introduced: 2,421 parameters at 5 levels; 3.11.0: 22). An always-true delete was accepted where a column's
>   `Length` or date part met the same part of its `[SqlExpression]` alias, since EF's form tests the column, not the
>   operand (R15-2). And an `[SqlExpression]` holding a `CASE` escaped the bound, which counted conditionals, not CASEs
>   (R15-3, fix-introduced: 3.11.0's 9 → 11, Msg 125). By the owner's ruling (Paul, 2026-10-22: "A"), rev 16 fixes all
>   seven findings, and round 16 runs under an exit rule (§11). Rev 16 distributes the larger side first again, keeps
>   EF's forms for a comparison that reads a property of the remote map, and lets the guard's null-safe pattern take a
>   witness only where the operand reads it. Its self-check found one more hidden level: SQL Server counts a `COALESCE`
>   (a nullable integer's `ToString()`) as a `CASE` (measured), so the depth counter counts it too. Every fuzzer is
>   clean on rev 16, by SQL Server's own count of the nesting (§6.5).
> - Round 14 found rev 14's fallback to EF's forms multiplying chains (R14-1, Major, fix-introduced): where every level
>   compares a nested conditional with a conditional, predicates under 1,000 characters passed SQL Server's 2,100
>   parameters at 6 levels and SQLite's 32,766 at 8 (rev 13: 82 and 110; 3.11.0: 19 and 25, C#'s rows); and, since the
>   both-nullable `==`, an always-true delete that 3.11.0 refused as a self-reference accepted, every row deleted: a
>   column compared with its `[SqlExpression]` alias, or a copy under a cast through `object` (R14-3, Major). By the
>   owner's ruling (Paul, 2026-10-22: "Go with the bound-checked distribution approach"), rev 15 distributes against
>   a conditional too wherever that nests at most one CASE more than the comparison as written, and keeps EF's forms
>   only where it would nest deeper (AC-N17); the chains grow linearly again. The guard's text check refuses EF's form of
>   a self-equality as 3.11.0 refused `n = n`. Every fuzzer, rounds 12–14's chains included, is clean on rev 15 (§6.5).
> - Round 13 found rev 13's bound false where the other operand holds a conditional (R13-1, Major, fix-introduced):
>   each distributed branch repeated it below the distributed CASE levels, so the nesting went past 3.11.0's + 1 (a
>   nested conditional against a 9-deep one: 9 → 11, Msg 125 on SQL Server, measured) and two nested sides grew
>   quadratically. By the owner's ruling (Paul, 2026-10-21: "EF's form there"), rev 14 distributes only against an
>   operand that holds no conditional and keeps EF's forms otherwise, so the CASE nesting is at most 3.11.0's + 1
>   (AC-N17, checked over every fuzzer's text). Its Minors: R12-1's verdict record (R13-2), a distributed leaf's ordinal
>   string comparison (R13-3, documented by the owner's ruling, "Document it"), and no killer for the distribution
>   under negation (R13-4). Every fuzzer, round 13's `fuzz13r` included, is clean on rev 14 (§6.5). Round 14 found the
>   fallback multiplying chains (R14-1, above).
> - Round 12 found no wrong rows in rev 12's forms. Its Minors were a delete-guard gap (R12-1: a `bool` compared with a
>   copy of itself under casts was accepted and deleted every row, where 3.11.0's text check had refused it) and a
>   challenge to the both-nullable Known issue (R12-2: distributing the comparison into the conditional's branches
>   sends it once). Its UNVERIFIED list named SQL Server's 10-level CASE limit, which the author then measured: rev 12's
>   single CASE doubled the nesting of conditionals compared within conditionals' tests, so from 6 levels SQL Server
>   failed with Msg 125 where 3.11.0 and EF ran them (R12-5, Major under the owner's rule 2).
> - By the owner's ruling (Paul, 2026-10-21: "Distribute into branches"), rev 13 replaces R11-5's single CASE. A
>   compensation over a flat conditional takes EF's form; one over a nested conditional (holding an earlier comparison
>   over a conditional) is distributed into its branches. The SQL grows linearly with the nesting in every form, two
>   operands that can both be null included (the Known issue is closed), and the CASE nesting is the source's, 3.11.0's,
>   where the depth is in the tests, or one level more where a branch is as deep as its test (a Known issue).
>   The guard compares a `bool`'s two sides through casts and a string's `ToString()` (R12-1). Every fuzzer is clean on
>   rev 13 (§6.5). Round 13 found the bound false against a conditional (R13-1, above).
> - Round 11 found rev 11's cast assertion false for a property whose CLR type can't hold null but whose SQL value can
>   (R11-1: `(int)(int?)x.ParentCode != 5` lost the missing parents, which EF keeps), the re-decided form reading a
>   captured value twice (R11-2), and the Known issue's account of nested `bool` comparisons false (R11-5: EF sends each
>   operand once). Rev 12 drops the assertion (a cast keeps the operand's kind, as EF's does, and a null test under an
>   odd number of `!`s tests the operand under them) and sends the substituted operands from the re-decided form.
> - By the owner's two rulings (Paul, 2026-10-21: "Adopt EF's linear form", then "Go beyond EF"), rev 12 sends each
>   operand once: AC-N14's sides are their two-valued `CASE`s, as EF's are, and a compensation over a conditional is
>   `CASE WHEN complement THEN 0 ELSE 1 END = 1`, where EF repeats the conditional for its `IS NULL`. The SQL grows
>   linearly with the nesting, except where two operands that can both be null meet (EF's forms; §3). Rev 13 withdraws
>   that single CASE (above).
> - Round 11 was the third round in a row with fix-introduced findings, so rev 12 took the test first again: round 11's
>   `fuzz11r` (twins whose SQL value IS NULL) is a standing fuzzer, and `grow11` measures the SQL per nesting level of
>   every comparison form. The self-check found two defects in the new forms' first cut, fixed before review (§11).
>   Every fuzzer is clean on rev 12 (§6.5).
> - Round 10 found the cast class at a fifth position, a cast back to `bool` over a `bool?` conditional with a branch
>   C# types as nullable (R10-1), and R9-1's exception one level down, a witness whose only nullable branch folds
>   (R10-2, fix-introduced). A second fix-introduced finding in a row, so rev 11 again changed the test first:
>   `fuzz11` adds wrappers whose `bool?` can hold null by type but never does, and `[SqlExpression]` twins of the
>   `bool` columns (nullable as far as FunkyORM knows, never NULL). At rev 10 it found 5,548 wrong-row predicates (and
>   5 variable-limit errors; R11-6), 45
>   exceptions and 549 over-reads beyond AC-N13's bound in 84,000, including a third class: a cast to a
>   non-nullable type classified as able to be null, so `!` under it met `IS NULL` (`NOT … IS NULL`, the complement).
> - Rev 11's code (§5.1): a cast back to `bool` over a conditional the cast pass leaves is a row operand in a truth
>   position (rule 9); a cast to a type that can't hold null is C#'s assertion of a value (withdrawn in rev 12,
>   R11-1); a compensated comparison
>   decides its form again after the substitution; and the fold asks a small captured operand first, without walking
>   the chain (R10-4). Every fuzzer is clean on rev 11 (§6.5).
>   Round 9 found rev 9's substitution rule throwing where a witness sat below the node it kept (R9-1,
>   fix-introduced), and R8-1's class at a fourth position, a cast back to `bool` over a `bool?` conditional (R9-2).
> - That made two fix-introduced findings in a row (R8-4, R9-1), so rev 10 redesigned the test before the code (the
>   house rule). A metamorphic fuzzer (`fuzz10`, §6.5) wraps any `bool` or `bool?` part of the earlier fuzzers'
>   predicates in a form C# evaluates the same: casts through `bool?` and `object`, `!!`, conditionals with equal
>   branches, comparisons with `true`. A class closed at one position is then checked at every position. At rev 9 it
>   found the class wider than round 9's examples: 3,056 wrong-row predicates, 121 exceptions and 207 over-reads in
>   48,000.
> - Rev 10 answers it with three changes (§5.1): the `LiftNormalizer` also lifts a `bool?` conditional of lifts and
>   takes out a cast through `object`; a row operand's substitution keeps the chain down to its witness; and the
>   fold stops at an operand of `&&`/`||` that decides it, asking one that reads no row first. On rev 10, every
>   fuzzer is clean (§6.5). The one bound that moves is AC-N13's, for a cast through `object`.
> - Round 8 held rev 8's fix layer; its fuzzer found R7-1's shape at a third position (R8-1), and rev 9's
>   `LiftNormalizer` put every lift outermost. Round 7
>   confirmed rev 7's redesign on round 6's fuzzers. Its own fuzzer (160,000 predicates) found one shape the model
>   didn't name, `!` over a lifted predicate (R7-1), and a visitor rule wider than R6-1 needed (R7-2); rev 8 answers
>   both, and the branches no test killed. All four fuzzers, 228,000 predicates on rev 8: no wrong rows, no read
>   beyond 3.11.0's, no T-SQL regression.
> - Rev 7 answered round 6. Round 6 found
>   that R5-1's change left `!` of a column, `!` of a captured `bool` and a conditional of values uncompensated beside
>   a predicate, plus a SQL Server path under `!` and a `NullReferenceException`. Seven of its nine findings were
>   fix-introduced, so rev 7 redesigns instead of patching (the house rule): one model for a `bool` compared with a
>   `bool` (§5.1 rules 5 and 6, AC-N14 to AC-N16). A side is a value, a predicate, or a row operand under its `!`s, and
>   a conditional's predicate branch is its two-valued truth.
> - Rev 7's code was fuzzed before review with round 6's two fuzzers: 68,000 predicates on SQLite, with SQL Server's
>   text parsed offline. The result: no wrong rows, no read beyond 3.11.0's, and no T-SQL that parsed at 3.11.0 and
>   doesn't now. The four defects the fuzzers found in rev 7's first cut are recorded in §11.
> - Round 5 was the first to review the code. Rev 6 answered it: its Major (R5-1) was SQL Server shapes the expansion
>   and the branch literal broke, kept as 3.11.0 sent them.
> - Rev 5 answers round 4. Its design Majors were plan text the code already went beyond: a negated `LIKE`/`IN` is a
>   change, so De Morgan reaches it (R4-1, rule 8); and every row operand is descended, so a conditional's test is
>   rewritten whatever the comparison around it (R4-2). Both now have proofs.
> - Two decisions are new in rev 5, both confirmed by the owner (Paul, 2026-10-20 ~22:55 CT: "keep both of those
>   decisions"). The delete guard calls a predicate compared with itself a self-reference (R4-4). An integer's
>   `ToString()` is `COALESCE(…, '')` wherever its value can be NULL, as EF sends it (R4-10).
> - The code (Tasks 2–5) and the riders are written in the working tree and green on all four providers (the harness
>   288 rows each). The red run of every addition at `25956e0` is recorded (§6.5).
> - **Nothing is committed until Task 0 is CLEAN** (the recorded deviation, §11). Round 16 was a delta review of rev 16
>   and of the code since round 15's snapshot, under the owner's exit rule (§11). Round 17 reviews rev 17 since round
>   16's snapshot, under the same rule. Round 18 reviewed rev 18 since round 17's snapshot, under the same rule; round 19 reviewed rev 19 since round 18's; round 20 reviewed rev 20 since round 19's; round 21 reviews rev 21 since round 20's.
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
> **The owner's companion rule applies:** don't make a shape that returned correct rows throw. So nothing is newly
> rejected, and SQL Server keeps every `bool` shape it accepted (R5-1). The delete verdicts that change are listed in
> §4.3. An always-true delete is refused by design, including some that 3.11.0 ran and that deleted every row, which
> was C#'s answer (R5-9).

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

**PostgreSQL:** a null parameter is typed `text`, so 3.11.0's `n = @p` with a null `int?` fails with 42883 (the red
run, §6.5). It runs in 3.12.0.

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
| `Parent.Code.ToString() != "5"` / `== ""` (`[RemoteProperty] int`) | `COALESCE(CONVERT(varchar(11), Code), '') <> '5'` / `… = ''` (a missing parent's text is `''`; R4-10) |
| `!(Id == 2 && Name.Contains(a))` | `Id <> 2 OR Name NOT LIKE … OR Name IS NULL` (R4-1) |
| `!(Id == 2 \|\| ids.Contains(N))` | `Id <> 2 AND (N NOT IN (…) OR N IS NULL)` |
| `!(Id == 2 && Name.Contains(noName))` | no WHERE |
| `(N != 5 ? Id : 0) > 0`, `(N != 5 ? 5 : 0) == v` | `CASE WHEN N <> 5 OR N IS NULL THEN … END > 0`, `… = @v` (R4-2) |
| `(Id > 2 ? (N != 5 ? 1 : 0) : 0) == 1` | `CASE WHEN Id > 2 AND (N <> 5 OR N IS NULL) THEN 1 ELSE 0 END = 1` |
| `Name.Contains(a) != nullBool` / `== nullBool` (`bool?` null) | no WHERE / `0 = 1` (R4-4) |
| `(N == 5) == (N == 5)` | no WHERE |
| `Flag == (N > 5)` | `Flag = CASE WHEN N > 5 THEN 1 ELSE 0 END` (the rows of AC-N14's expansion) |
| `(N == v) == !B`, `!B == (M > 3)` | the predicate's two-valued truth `<> B` (the rows of AC-N14's expansion on `B`; R6-2) |
| `Flag != (bool?)!t` (`t` captured, true) | `Flag <> @p OR Flag IS NULL`, `@p` false |
| `!Flag != true` / `!Flag == null` | `Flag = 1 OR Flag IS NULL` / `Flag IS NULL` (FunkyORM: `flag <> @p OR flag IS NULL` with `@p` false, the same rows; R6-4) |
| `Flag == !Flag2` | `Flag <> Flag2 OR (Flag IS NULL AND Flag2 IS NULL)` (FunkyORM adds `Flag IS NOT NULL AND Flag2 IS NOT NULL` beside the `<>`, so its rewritten body is C#'s too) |
| `!(Id > 2 ? B : false) == true` | `CASE WHEN Id > 2 THEN B ELSE 0 END = 0` (FunkyORM keeps 3.11.0's `NOT CASE … END = @p`, the same rows; R6-1) |
| `(Id > 2 ? (bool?)(N == 5) : Flag) != true` | `CASE WHEN Id > 2 THEN CASE WHEN N = 5 AND N IS NOT NULL THEN 1 ELSE 0 END ELSE Flag END <> 1 OR CASE … END IS NULL` (R6-4) |
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
`(x.N == 5) == (x.M == 5)`, `x.Flag == (x.N > 5)` or `x.Name.Contains(s) == include`, is rewritten as its sides'
two-valued values compared, EF's form (AC-N14).

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
  issue), so these were accepted in 3.11.0 and are accepted now. 3.11.0 deleted the non-null rows `{1,2,5,6}` for
  `x.N == 5 || x.N != 5`, and nothing for `x.Req != noName` (its parameter was NULL; R4-5); 3.12.0 deletes every row.
  They're listed under Behavior change, keyed (`nEq5OrNNe5`, `reqNeNoName`), and the Known issue's text is extended.
  The one exception is a `bool` compared with the same operand, `(x.N == 5) == (x.N == 5)`, `!x.Active == !x.Active`
  or a conditional with itself: the verdict calls it a self-reference, as it calls `x.Id == x.Id` one (R4-4, R7-5).
  3.11.0 deleted nothing for the predicate, every row for the others, or failed.
- **A lazy collection in an operand the SQL repeats** (R5-8, R6-7, R6-8, R13-5): a conditional that EF's forms test
  for NULL beside the comparison, twice against a value and three times against an operand that can also be null
  (since rev 12, AC-N14 sends a predicate once): `(lazy.Contains(x.N) ? x.N : none) != 5` binds 5 parameters, 3.11.0
  4. It is enumerated once and its items are bound per copy. It is materialized as an array, or an in-memory queryable for an `IQueryable<T>`, of the variable's
  type, or else of the type it is passed or cast as (`Enumerable.Contains`'s `IEnumerable<T>`). Only a receiver whose
  own concrete type takes neither, a custom lazy class with an instance `Contains`, is still enumerated once per copy.
- **A cast to a type that can't hold null, over a value that can** (`(bool)x.Flag`, `(bool)(x.Id > 2 ? (bool?)x.Active : x.Flag)`,
  `(int)x.N`, `(int)(int?)x.ParentCode`; R9-2, R10-1, R11-1). The cast keeps the operand's nullability, as EF's does:
  `(int)x.N != 5` also selects the NULL rows, and `(int)(int?)x.ParentCode != 5` the missing parents, as
  `x.ParentCode != 5` does. C# fails the first cast on a NULL row it sees, so EF's rows are the answer there; a
  property that is NULL only in SQL (a missing parent) reaches C# as `default(T)`, so C# doesn't fail on it. A
  conditional under a cast back to `bool` is still a row operand, its test rewritten (§5.1 rule 9). Every other cast
  between `bool` and `bool?`, through `object` too, is seen through.
- **A conditional compared where it can be null, nested** (R11-5, R12-2, R12-5). EF repeats the conditional for its
  NULL tests beside the comparison, so nesting such comparisons multiplies its SQL. The rewrite repeats only a flat
  conditional (no conditional in its test, and only flat conditionals in its branches), a bounded copy that adds no CASE
  level. It distributes the comparison into a nested one's branches, `(t ? (a op v) : (b op v)) == true`, so the test,
  which holds the earlier level, is sent once (§5.1). Each branch repeats the other operand below the distributed
  levels, so a conditional there, about as deep as the nested side or deeper, would nest the CASEs past the source's
  + 1 (R13-1). The step-6 and rule-7 call sites therefore keep the distribution only where its CASE nesting is at most
  the comparison's as written + 1, or no more than EF's form's (`DistributeWithinTheBound`; the owner's ruling,
  2026-10-22, the bound-checked distribution, R14-1), and EF's forms otherwise. Rev 14 kept EF's forms against any
  conditional (the owner's ruling, 2026-10-21, "EF's form there"), and a chain of such comparisons then multiplied its
  SQL and parameters, threefold per level (R14-1). The bound counts an `[SqlExpression]`'s template too: its CASEs,
  COALESCEs, IIFs and NULLIFs, in any provider's variant, through a `{Token}` that names another template (R15-3,
  R16-2); the other properties of the remote map are a joined column, a JSON path or a subquery aggregate, which hold
  none. (Rev 16 kept EF's forms for any comparison that read the remote map, on the false premise that all of them could
  hold CASEs, and an alias in a chain's innermost test then multiplied every level above it; R16-2, R16-3.) Where EF's
  forms are kept, nesting such comparisons multiplies the SQL and its
  parameters, about threefold per level: EF's text repeats the conditional as well, but binds each value once, and
  FunkyORM binds each copy. From about 6 levels that passes SQL Server's 2,100 parameters and from about 8 SQLite's
  32,766, in predicates of 1,000–2,000 characters whose rows 3.11.0 got right (round 15's `efchain`: 5,079 and 45,897;
  R15-6). A Known issue. Two nested sides are distributed the larger first, so the leaves repeat the smaller side,
  never a chain's earlier levels (R15-1). A chain of comparisons in conditionals' tests against flat conditionals,
  values never NULL, predicates, or a shallow nested conditional, on either side, is distributed and grows linearly
  (`Rewrite_NestedConditionalsAgainstConditionals_GrowLinearly`, round 14's chain probes and round 15's `order`, §6.5),
  as do two operands that can both be null (`grow11`). The CASE nesting is at most 3.11.0's + 1 (AC-N17), counted as
  SQL Server counts it, a `COALESCE` included (measured: SQL Server counts `COALESCE`, `IIF` and `NULLIF` as CASEs, not
  `ISNULL`; FunkyORM sends one for a nullable integer's `ToString()`). It is the source's where the depth is in the
  tests, as nesting comparisons puts it (`depth12`), and one level more, once, where the predicate's deepest part is a
  leaf, since a leaf is a two-valued CASE: a distributed branch that is a conditional at least as deep as the test
  (`Rewrite_ADistributedBranchAsDeepAsItsTest_AddsOneCaseLevel`; R14-4: "at least"), or another conditional one level
  shallower than the nested side (R15-5; `nestedCondChainNN8`). A remote or computed integer's `ToString()` adds one
  more where it is the deepest part, since R4-10 made it a `COALESCE` (3.11.0: a bare `CAST`): + 1 on its own, + 2 below
  a distributed leaf, at step 5, which distributes without the bound, and at step 6 and rule 7 too, whose bound counts
  that `COALESCE` in the comparison as written (R16-4, R17-3). SQL Server nests at most 10 CASEs, and `Any()`
  and `All()` send their query inside one (`CASE WHEN EXISTS …`), so such a predicate 10 levels deep, or 9 under `Any()`
  or `All()`, fails with Msg 125 where 3.11.0 and EF ran it: a Known issue. No fuzzer's predicate nests above 3.11.0's + 1, or past 10 or 2,100
  parameters where 3.11.0's text was within them (`depthplus.py`, `sslimits.py`, and since rev 16 `ssdepth16.py`,
  which counts as SQL Server does; §6.5). The fuzzers' largest predicates can pass SQLite's 32,766-parameter limit
  where 3.11.0 ran them: 1 of `fuzz11`'s 84,000 (rev 14: 2; rev 13: 11 of `fuzz11`'s and `fuzz10b`'s), seed 131 #239,
  1.1 million characters whose SQL runs to 2.7 million, which 3.11.0 answered with wrong rows. A Known issue.
- **A conditional in a value position whose test is known sends both branches** (3.11.0's form; R5-1). So a branch the
  translator refuses (`ToString()` of a cast, a `Contains` with a cast item) is refused even where the test makes it
  unreachable, as 3.11.0 refused it. AC-N14's `CASE` and a distributed comparison's branches put a predicate side in such a position: 5 of
  `fuzz11r`'s 72,000, which 3.11.0 refused and rev 11 translated, are refused again (§6.5).
- **A `bool` conditional that a cast back to `bool` kept from a truth position, on SQL Server** (R9-2). Seen through, it
  is a `bool` `CASE` in a truth position, which T-SQL rejects, as it rejected the cast's `CASE` in 3.11.0
  (`castCondActiveFalse`, `castCondNNe5Active`, `castNotCondActiveFalse` and `castLiftedCondKnownBranch` are
  dialect-rejected there). The other providers run it with C#'s rows.
- **`!` of a `bit` value where SQL Server can't take one** (R6-2). `!x.Other` as a `CASE` result
  (`x.Id > 3 ? x.Active : !x.Other`), or on a comparison's right side (`x.Other == !x.Active`), is `NOT t.other`
  there, which T-SQL rejects, as it did in 3.11.0. The other providers run it. Unchanged; `nEq5EqCondNotOther` is
  dialect-rejected on SQL Server, and so is `nullEqLiftedCastNotCond`, whose null test is its conditional's since
  R11-1, so its `!x.Other` branch is sent as 3.11.0 sent it (rev 11's cast assertion had folded it to `1=0`). A bit
  column as a conditional's test (`x.Active ? … : …`) is rejected there too, as in 3.11.0.
- **The read-back of a non-nullable property over a SQL NULL.** A missing LEFT JOIN parent, an absent JSON key, or
  an empty `Sum` reads back as `default(T)`, because the materializer skips `DBNull`. WHERE compares the SQL value,
  as EF does for a missing parent (for a `Sum`, EF sends `COALESCE(SUM(…), 0)`; a non-nullable `Sum` or `Avg` counts
  as unable to be NULL, compared with a value as 3.11.0 compared it, by the owner's ruling on Task 8's T8E2-4; under
  a two-valued CASE its unknown is false, by the owner's second ruling). A Known issue.
- **MySQL's JSON null.** `JSON_UNQUOTE(JSON_EXTRACT(…))` gives `'null'` text. A Known issue; unchanged.
- **The SQLite `Guid`/`DateTime` binding (FD-11).** Unchanged. Its class also covers `decimal`: Microsoft.Data.Sqlite
  binds a `decimal` as TEXT, so a comparison with an expression that has no column affinity (a `[SubqueryAggregate]`,
  a `[SqlExpression]`) compares as text, and `x.ChildSumD != 10m` selects the row whose sum is 10. That was so in
  3.11.0 (the green run found it: rows `{1,5}` before, `{1,…,6}` after). `childSumDNe99` compares with a value no
  row's sum has, so it proves AC-N1 on every provider; the binding is a backlog item.

## 4. Acceptance criteria

### 4.1 Definitions

**Accepted row operand:** a row operand the visitors translate. This is the visitors' own list, from round 2's code
read:
- a mapped property, or a property in the remote map;
- a `Convert` of an accepted operand;
- `.Year`/`.Month`/`.Day` of a `DateTime` property (also through `.Value`), and `.Length` of a `string` property;
- `ToString()` with no argument on an integer or `string` property;
- `!` of an accepted `bool` operand, with that operand's kind and witness (R6-2, R6-4);
- a conditional, a predicate branch included, which is sent as its two-valued truth (AC-N16);
- a string `Contains`/`StartsWith`/`EndsWith` or a collection `Contains` (as a predicate).

Everything else is rejected by the visitor or translator: fields, `[NotMapped]` and discovery-unmapped properties,
nested receivers, other date or `DateTimeOffset` parts, other `ToString`s, arithmetic, `??`, other methods,
`.HasValue`/`.Value`. The rewrite gets the provider's unmapped set and remote map, so it decides the same way.

**Can be null** (`NullSemantics.Classify`) applies to an accepted operand whose SQL value can be NULL:
- a property of `Nullable<T>` or reference type;
- whatever its CLR type: a `[RemoteProperty]`, a `[RemoteKey]`, a `[JsonPath]`, a `[SqlExpression]`, or a
  `[SubqueryAggregate]` `Sum` or `Avg` only where declared nullable: one declared non-nullable is compared as 3.11.0
  compared it, without the NULL's compensation (the owner's ruling on Task 8's T8E2-4, 2026-10-23: "Keep 3.11.0's
  SQL"; a Known issue);
- a date part or `Length` of such a property. The witness, the expression tested with `IS NULL`, is that property;
- `ToString()` of a string property. An integer's `ToString()` is `COALESCE(…, '')` wherever its value can be NULL
  (a `Nullable<T>`, or a `[RemoteProperty] int` over a missing parent), as EF sends it, so it is never null (R4-10;
  3.11.0 sent a bare `CAST` for the second, NULL for a missing parent);
- a conditional with a branch that can be null. Its witness is the conditional as rewritten;
- `!` or a `Convert` of any of these.

**Value:** an operand the shared evaluator reads before the query, such as a constant or a captured, static or
nested member, or `!` of one. As a side of a `bool` comparison, or a conditional's branch, a `bool` that reads no row
(a comparison or conditional of captured values) is a value too, read as one (R6-2, R6-5). `Classify` itself stays by
shape, so an operand the rewrite folds down to constants keeps its kind and its witness (§11, the self-check).

### 4.2 Criteria

| AC | Criterion |
|---|---|
| **AC-N1** | `x.P != v`, with P able to be null and v a non-null value: also selects the rows where P is null. Either operand order. |
| **AC-N2** | `x.P == v` / `!=` with v null: `P IS NULL` / `P IS NOT NULL` when P can be null. **The whole value operand** is replaced by `Constant(null, operand.Type)`, so `Convert(x.E, int?) == Convert(noColor, int?)` reaches the visitors' bare-null test (R3-2). When P can't be null, `1=0` / `NOT 1=0`, so a delete of `!=` alone is refused. That includes the literal `x.Id == null` / `!= null` (an AC-N11 exception: `id IS NULL` → `1=0`). |
| **AC-N3** | Column against column:<br>• both able to be null: `==` also matches two NULLs, and `!=` takes EF's form;<br>• one able to be null: `!=` also selects that one's NULLs, and `==` is unchanged.<br>This applies to an unfolded self-comparison too. A repeated witness is tested once, asserted in the SQL text. |
| **AC-N4** | `!` reaches the comparisons. De Morgan over `&&` and `\|\|`; `!(a == b)` becomes `a != b` and `!(a != b)` becomes `a == b`; `!` passes through a `bool` conditional into its branches; then AC-N1..N3 apply. A negated `>`, `>=`, `<` or `<=` also selects the rows where an operand that can be null is null. With a null value, it selects every row. **Structure is kept where nothing is compensated:** a negated `==`/`!=` whose flipped form needs no compensation is sent as `NOT (original)`, with any value substituted, so `!(x.N != v)` stays `NOT n != @p`. That's 3.11.0's text, and C#-correct, since NOT unknown is unknown and C# says false. Likewise `!!p` stays `NOT NOT p` and `!(a && b)` stays `NOT (a AND b)` unless a leaf under them is compensated. A negated method call that the translator compensates (a `LIKE` on a column, an `IN` on a column that can be null, a null search value) **is** such a leaf, so `!(x.Id == 2 && x.Name.Contains(a))` becomes `NOT id = @p OR (NOT name LIKE … OR name IS NULL)` (R4-1). |
| **AC-N5** | A `>`, `>=`, `<` or `<=` with a null value is `1=0`. The rows are 3.11.0's on SQL Server, MySQL and SQLite; PostgreSQL failed it with 42883 (§6.5). |
| **AC-N6** | Collection `Contains` on a property that can be null:<br>• a null item also selects the NULL rows, parenthesized: `(P IN (…) OR P IS NULL)`; with only nulls, `P IS NULL`;<br>• negated with no null item: `(NOT P IN (…) OR P IS NULL)`;<br>• negated with a null item: `(P IS NOT NULL AND NOT P IN (…))`; with only nulls, `P IS NOT NULL`;<br>• empty: `1=0` / `NOT 1=0`.<br>On a property that can't be null, 3.11.0's SQL. |
| **AC-N7** | `!x.P.Contains(v)` / `StartsWith` / `EndsWith`: `(NOT P LIKE … OR P IS NULL)`. |
| **AC-N8** | A null search value: positive, no row (`P LIKE NULL`, as in 3.11.0); **negated, every row** (`NOT 1=0`), so a delete of it alone is refused. The same for `StartsWith` and `EndsWith`. This amends D8, per the owner. |
| **AC-N9** | `All(p)` is true when no row satisfies `!p` under AC-N1..N8. |
| **AC-N10** | One path. For one predicate:<br>• Query, QueryAsync, Where and Count send the same WHERE text and parameters, and Delete/DeleteAsync send byte-identical text;<br>• every path selects, counts or deletes the oracle's rows on all four providers, and Any and All answer as the oracle;<br>• the guard's delete verdicts change **exactly** as listed in §4.3. |
| **AC-N11** | No change where C# and SQL already agree: a positive `==` against a non-null value, a positive relational comparison, a positive LIKE, IN without a null item, a literal null on a column that can be null, and every shape whose operands can't be null. These send **exactly 3.11.0's WHERE text and parameters**, `NOT` included, including when a captured value is wrapped in a `Convert`. Pinned literally per provider, and the pins are green at `25956e0` (recorded, §6.5).<br>**Listed exceptions** (R3-8; each keyed, and each in the Changelog):<br>(a) a conditional's test, or a branch of a `bool` conditional in a truth position, that `Fold` knows is now a literal (`NOT 1=0` / `1=0`) where 3.11.0 sent parameters. Two strings there now compare as C# does (ordinal), not under the column's collation: `condCapturedTest`, `condCollationTest`. So does a distributed comparison's leaf that reads no row (R13-3; the owner's ruling, 2026-10-21, "Document it"): `nestedCondUpperANeA`, `(t ? "A" : x.Name) != "a"` with t holding a conditional, sends `@p` (true) for `"A" != "a"`, where 3.11.0 and the flat form compare the two strings under the collation. Any part a cast hid from the fold is seen now: a conditional that a cast back to `bool` kept in a value position is in a truth position, and a part under a cast through `object` is folded, so their known parts are literals too, and (d), (h) and (i) apply under such a cast: `castLiftedCondKnownBranch` (`ELSE @p1` → `ELSE NOT 1=0`; R9-4), `objCodeSelfAndIdEq2` (`code = code` → `NOT 1=0`; R10-3);<br>(b) AC-N2's null on a column that can't be null, literal or captured: `id IS NULL` / `id = @p` → `1=0`, and `id IS NOT NULL` / `id != @p` → `NOT 1=0`;<br>(c) `All`'s text: `NOT (p)` becomes the translation of `!p`, e.g. `NOT t.id > @p`, the same rows where nothing can be null;<br>(d) AC-N14's text where the rows already agreed: `name LIKE @p ESCAPE '!' = @p1` loses its `= @p1` (`nameContainsAEqTrue` on SQLite, PostgreSQL and MySQL), and `flag = n > @p` is `flag = CASE WHEN n > @p THEN @t ELSE @f END` (`flagEqNGt5` on SQLite);<br>(e) an integer's `ToString()` over a value that can be NULL is `COALESCE(…, '')` (R4-10);<br>(f) AC-N5's relational comparison with a null value: `n > @p` / `n > NULL` → `1=0` (R5-6);<br>(g) a captured value in a conditional's value branch is read and substituted, so a null one is the literal `NULL`, not a parameter: PostgreSQL can type it (42804 before; R5-4);<br>(h) a `bool` operand that doesn't read the row, or whose truth `Fold` knows, is one parameter holding its value: `x.Flag == !include` (was `flag = NOT 1=0` with `include` false, a literal SQL Server rejects there), `(x.Name.StartsWith(noName) ? true : true) == x.Flag` (R6-2, R6-3);<br>(i) a conditional's predicate branch is `CASE WHEN p THEN @t ELSE @f END`, and a `bool` branch whose truth `Fold` knows is that value (R6-4).<br>"A positive relational comparison" excludes one whose operand holds a conditional whose test is compensated: the test is rewritten (R4-2). |
| **AC-N12** | The documents say what the code does. That covers Changelog `[3.12.0]`:<br>• Behavior change: every wider shape (AC-N15 and AC-N16's included), the verdict table, `reqNeNoName` and the always-true-once-NULLs-count class, the D8 amendment, and AC-N11's exceptions (a)–(i);<br>• Changed: PostgreSQL's comparisons with a null variable, which 3.11.0 failed with 42883, now run, as do its predicate comparisons (42601) and a null captured value in a `CASE` branch (42804); SQL Server's `bool` compared with a predicate, a captured `bool` as a conditional's test, a predicate as a conditional's branch, `!` of a captured `bool` in a comparison, and a predicate under a cast through `object` compared with a value, which were syntax errors, now run; an integer's `ToString()` over a missing parent is `''`, as EF sends;<br>• Fixed;<br>• Known issues: the read-back of a non-nullable property, MySQL's JSON null, PostgreSQL's NULLs-last.<br>It also covers `Usage.md`, `Advanced.md` §5 and the AI instructions. |
| **AC-N13** | Reads (R3-5). For a predicate that translates, the rewrite never causes more reads of a captured value than 3.11.0 does, and a delete reads one no more often than 3.11.0's delete, except around a cast through `object`: the guard gives its verdict once (R19-1), the provider's SQL for a part reads nothing the caller wrote, and the verdict folds a part under a cast through `object` that reads the row without reading under it (R20-1), but what that fold learns can lead it to read a value beside the cast that 3.11.0's verdict didn't reach, and the rewrite reads under such a cast within the cast bound below (`delreads`, through SQLite's real provider: 9 of 12,000 deletes read more than 3.11.0's, each around a cast through `object`, and 4,555 fewer; `Delete_ReadsACapturedValue_NoMoreOftenThan3110`'s seventeen shapes on all four providers).<br>• **Every value it reads is substituted, and bound from that read.** A non-null value replaces the innermost captured member (`Constant(value, member.Type)`, the `Convert` chain kept). A null value replaces the whole operand (AC-N2).<br>• When no rule applies, the comparison keeps its operator and any `!`; only the member is swapped.<br>• It reads a comparison's value only when the comparison's row operand is accepted.<br>• A conditional's captured members are substituted before the conditional is duplicated as a witness, and a captured value branch is substituted always (AC-N11 (g)).<br>• A fold of a comparison of captured `bool`s reads each once (R5-3), and a literal null against a conditional keeps the conditional's substituted test (R5-2).<br>• A value the fold read that its truths can't hold (a null `bool?`) is recorded beside the memo and reused, not read again (R6-5). A `bool` branch whose truth the fold knows is that value, so nothing under it is read. AC-N14 sends each side once, so each is read once (R11-5); an operand that EF's forms repeat as a witness is substituted before it is repeated (R6-5).<br>• A lazy collection is enumerated once (§3).<br>A predicate the translator rejects at an earlier operand may read a later value once (e.g. `x.Child.Id == 1 \|\| x.N != c.Five`); that's outside "translates".<br>• **The fold stops at an operand of `&&`, `\|\|`, `&` or `\|` that decides it** (as C# does for `&&` and `\|\|`; for `&` and `\|` the truth is the same), and asks a small right operand that reads no row (at most 32 nodes, `IsEvaluable`) before a left one that isn't (R9-3, R10-4). 3.11.0's fold folded both.<br>• **A cast through `object`** between `bool` and `bool?` is bounded by 3.11.0's reads of the predicate written without the cast (R9-3). 3.11.0's fold read such a cast as one value: unknown when it reads the row, so nothing under it was read; evaluated whole when it doesn't. The rewrite sees through it (§5.1), so where another operand decides a part, the fold can read under the cast what 3.11.0 reads in the same predicate without the cast, and C# reads it too. `ReadOnce_CountingGetter` pins the common case (a captured operand decides: nothing under the cast is read); `fuzz10` checks the bound (§6.5). |
| **AC-N14** | A `bool` compared with a `bool`. Each side is, in order: a **value**, when it reads no row (read once) or the fold knows its truth (R3-4, R6-2, R6-3); a **predicate** (a comparison, a logical operator, a method call, `!` of a predicate, or a `Convert` of one to `bool?`; `!` over a lifted predicate, `!(bool?)p`, is `!p`, R7-1); a **row operand**, under any `!`s over it (R6-2); or not accepted, and then the comparison is left as it is. A `bool` member, `!` of one, and a conditional are row operands, never predicates: SQL Server accepts a `bit` only as a value (R5-1, R6-6). With a predicate side, each predicate is its two-valued value and the values are compared; otherwise it is rule 6's (AC-N15), against a value too (R4-13):<br>• **a predicate against a `bool` value** v: true → p; false → !p; a null `bool?` → `false` for `==` and `true` for `!=` (C#'s lifted comparison);<br>• **a predicate against a predicate:** `p == q` is `(p ? true : false) == (q ? true : false)`, EF's `CASE WHEN p THEN 1 ELSE 0 END = CASE WHEN q THEN 1 ELSE 0 END`, and `p != q` the same with `!=`;<br>• **a row operand m against a predicate p:** m against p's two-valued value, by rule 6, so `!=` also selects the rows where m is null; `!m == p` is `m == (p ? false : true)`, C#'s lifted `!`, so no NOT is sent on a bit value;<br>• **two values** compare as C# lifts them (reached where the fold knows one side and the other is a null `bool?`, which the fold's truths can't hold).<br>Each side is sent once (R11-5; rev 11's `(p && q) \|\| (!p && !q)` sent each twice, so nesting multiplied the SQL), so it is read once, and a lazy collection is enumerated once (§3). |
| **AC-N15** | `!` over a row operand in a comparison is C#'s lifted `!` (R6-2, R6-4). `!m == v` is `m == !v`, so a null value tests m (`!x.Flag == null` is `flag IS NULL`) and a `!=` gains m's NULLs. Between two row operands with one `!`, `!a == b` is EF's `==` around `a != b`, guarded by `a IS NOT NULL AND b IS NOT NULL` where both can be null, and `!a != b` is EF's `!=` around `a == b`. A compensated form has no NOT on a value. Where a witness is a nested conditional, the comparison is first distributed into its branches where that keeps AC-N17's bound (§5.1), and each branch's comparison takes these forms (R12-2, R13-1, R14-1). Where nothing is compensated the comparison is as written, 3.11.0's text (`NOT t.active = @p`), and the visitor keeps a conditional under that NOT a value (§5.3, R6-1). |
| **AC-N17** | (R13-1, R14-1, R15-1, R15-3; the owner's rulings, 2026-10-21: "Distribute into branches", 2026-10-22: the bound-checked distribution.) Where 3.11.0's SQL Server text is valid, the rewrite's CASE nesting, counted as SQL Server counts it (a `COALESCE`, which FunkyORM sends for a nullable integer's `ToString()`, is a level), is at most 3.11.0's + 1: a distributed comparison's leaf, a two-valued CASE, adds one level below a branch, and nothing else adds one but R4-10's `COALESCE` for a remote or computed integer's `ToString()`, where 3.11.0 sent a bare `CAST` (+ 1 on its own, + 2 below a distributed leaf: at step 5, unchecked, and at step 6 and rule 7, whose bound counts that `COALESCE` as written; R16-4, R17-3). At step 6 and rule 7 a nested conditional is distributed where the result nests at most one CASE more than the comparison as written, or no more than EF's form (whose operands nest deeper than written only through AC-N16's predicate branches, a shape 3.11.0's SQL Server text rejected), the count reading an `[SqlExpression]`'s template levels (R15-3, R16-2); otherwise EF's forms, which add no level, are kept. Two nested sides are distributed the larger first, so a chain's earlier levels aren't repeated (R15-1). Each level that keeps the bound adds at most one CASE over its operands as written, so the predicate does too. Proved at each call site (step 6 with one or two operands that can be null, `==`, rule 7, both orders, a conditional nested through its branches, two nested sides, an operand just deep enough to pass the bound) by `Rewrite_ANestedConditionalAgainstAConditional_TakesEFsForm`; for the chains the bound lets through, by `Rewrite_NestedConditionalsAgainstConditionals_GrowLinearly` (ten families, both sides, two nested sides in both orders); against a computed property by `Rewrite_AComputedPropertyOperand_CountsItsTemplatesLevels` and `nestedCondNeFlat8CodeCase` (Msg 125 at rev 15), and against an alias in a chain's innermost test by `nxChainInTest7` (SQL Server's 2,100 parameters at rev 16); for a `COALESCE` by `Rewrite_ANullableIntegersToString_CountsAsACase` and `nestedStrNeFlat8NToString` (Msg 125 before the counter counted it); on the databases by `nestedCondNeFlat9` (Msg 125 at rev 13 on SQL Server), `nestedCondChainNN8`, `nestedCondChainNNRight8` (SQL Server's 2,100 parameters and SQLite's limit at rev 14) and `nestedCondChainNestedLeft7` (SQL Server's 2,100 parameters at rev 15); and over every fuzzer's SQL Server text by `depthplus.py` and `ssdepth16.py` (§6.5). Step 5's other operand is a value read before the query, which holds no conditional, and its leaves move only the branches, one level, so its distribution keeps the bound unchecked. |
| **AC-N16** | A conditional's predicate branch is its two-valued truth, `p ? true : false` (`CASE WHEN p THEN 1 ELSE 0 END`, as EF sends it), with p rewritten positive. So `(x.Id > 2 ? (bool?)(x.N == 5) : x.Flag) != true` counts a NULL `N` as `false`, as C# does, and SQL Server, which rejected a predicate as a `CASE` result, runs it (R6-4, R6-6). |

### 4.3 The delete verdicts that change (AC-N10)

| Shape | 3.11.0 (corrected per R3-6) | 3.12.0 | Why |
|---|---|---|---|
| `!x.Name.Contains(null)` (also `StartsWith`/`EndsWith`) | accepted, deleted nothing | refused: always true | AC-N8: every row |
| `x.Id == 2 \|\| !x.Name.Contains(null)` | accepted, deleted {2} | refused: always true | AC-N8 |
| `!(x.N == 5 && x.Name.Contains(null))` | accepted, deleted {5,6} | refused: always true | AC-N8, De Morgan |
| `!(x.Id == 2 && x.Name.Contains(null))` (a sibling that needs nothing; R4-1) | accepted, deleted {1,3,4,5,6} | refused: always true | AC-N8, rule 8 |
| `x.Name.Contains(none) == false`, `!= true`, `!(… == true)` | accepted, nothing (SQLite, PostgreSQL, MySQL); a SqlException on SQL Server | refused: always true | AC-N14, AC-N8 |
| `!(x.N >= none)` and the other negated relational comparisons with a null value | accepted, nothing (PostgreSQL: 42883) | refused: always true | AC-N4 |
| `x.Id == 2 \|\| !(x.N >= none)` | accepted, deleted {2} (PostgreSQL: 42883) | refused: always true | AC-N4 |
| `x.Id != none`; `!(x.Id == none)`; `x.Id == 2 \|\| x.Id != none` (R5-9) | accepted, nothing / {2} (PostgreSQL: 42883) | refused: always true | AC-N2 fold |
| A predicate always true once a null value's comparison folds, also in a conditional's test: `x.N > none ? x.Code == 5 : true` (R5-9), and under a cast back to `bool`: `(bool)!(bool?)(bool)((x.Code != none) ? (bool?)(none <= 1) : (bool?)onlyNull.Contains(x.M))` (`castNotLiftedCondHybrid`, R9-2) | accepted, every row (SQLite, MySQL; SQL Server: an error; PostgreSQL: 42883) | refused: always true | AC-N5 fold; an always-true delete is refused by design |
| `x.Id != null`; `!(x.Id == null)` (literal) | accepted, **every row** (`id IS NOT NULL`, `NOT id IS NULL`) | refused: always true | AC-N2 fold |
| `!(x.Flag == !x.Flag)` (R8-2) | refused (its text, `NOT flag = NOT flag`, read as a self-comparison) | accepted: the rows with a flag, C#'s and EF's (`(NOT 1=0 AND flag IS NOT NULL)`) | AC-N15 |
| Any shape whose only literal was a dropped null's `1=0` or `NOT 1=0`: `!onlyNull.Contains(x.N)`, `x.Id == 2 \|\| !onlyNull.Contains(x.N)`, `!(x.Id == 2 && onlyNull.Contains(x.N))` | refused (`NOT 1=0`, or `NOT (t.id = @p AND 1=0)`, which the literal check calls true) | accepted (`n IS NOT NULL`, …) | AC-N6 |
| Count/All over a reverse join whose only remote reference folds away (`x.ReverseInt > none`) | refused (fan-out) | accepted (`1=0`; no join needed) | the WHERE no longer names the alias |
| `x.Req != noName` | accepted, deleted nothing (R4-5) | **accepted, every row** | §3: C#'s answer, which the guard can't see |
| `x.N == 5 \|\| x.N != 5`; any delete always true once NULLs count | accepted, the non-null rows | **accepted, every row** | §3 |
| `x.Name.Contains(a) != nullBool` (a `bool?` null; R4-4) | accepted, nothing (SQLite, MySQL); a syntax error (SQL Server); 42883 (PostgreSQL) | refused: always true (`NOT 1=0`) | AC-N14: a predicate is never null |
| A cast through `object` around a part that is always true by its own columns and literals, or compares a column with itself: `x.Id == 2 \|\| (bool)(object)(x.Code == x.Code)` (`idEq2OrObjCodeSelf`), `holder.False != (bool)(object)(x.Name.Length <= x.Name.Length)` (`falseNeObjNameLengthSelfLe`; R10-3) | accepted: every row for the first, the rows with a name for the second (SQL Server: a syntax error there) | refused: always true, or self-referencing (`LENGTH(name) <= LENGTH(name)`), as the same shape without the cast was in 3.11.0; a part that is so only once a captured value under a cast that reads the row is read is the translated clause's check's to refuse, which it does where the rewrite sends the value as a literal it reads (`objOrCapturedTrue`'s delete, SQLite's `R20-1` row), and is accepted otherwise, as in 3.11.0 (rev 21, R20-1; R21-2: `(bool)(object)(on ? (bool?)(x.Active == x.ActiveX) : nb)` deletes every row; 15 of the fuzzers' deletes, §6.5; a Known issue) | the cast no longer hides the part from the fold (§5.1) |
| An empty search value under a cast through `object`, written as `""` or `string.Empty` (rev 22, R21-1): `(bool)(object)x.Name.Contains("")` (`objNameContainsEmpty`), `x.Id == 2 \|\| (bool)(object)x.Name.StartsWith("")`, `(bool)(object)(x.Name.EndsWith(string.Empty) \|\| x.Id == 2)`; SQLite's `R21-1` and `R21-1b` deletes | accepted: the rows with a name (rev 20 refused them, rev 21 accepted them) | refused: an empty search value (`UnboundedSearch`); a captured `""` under the cast isn't read, so `(bool)(object)x.Name.Contains(empty)` is accepted, as in 3.11.0 (`objNameContainsCapturedEmpty`, read 0 times; a Known issue) | the under-cast rules count a search value written as a literal, which reads nothing the caller wrote |
| A part under a cast through `object` that a captured value makes a self-comparison or a constant, which the rewrite then sends as a literal the text check reads (R21-2): `(bool)(object)(c.T ? x.Id == x.Id : x.Id == 2)`, `(bool)(object)(c.T ? x.Code == x.Code : x.Id == 2)`, `(bool)(object)(c.T ? true : x.Id == 2)`, `(bool)(object)(c.T ? (bool?)(x.Code == x.Code) : c.NB)`, `(bool?)(object)(x.Id == 2 \|\| c.T) == true` (round 21's B9–B11, B13, B20) | accepted, every row | refused: always true (`AlwaysTrueMessage`, the check of the translated clause; rev 20's verdict refused them too) | rev 21's verdict reads no captured value under the cast; the rewrite does, as a query's does |
| A predicate compared with a `bool` value whose SQL compares a column with its own `[SqlExpression]` alias: `(x.Active == x.ActiveX) != false`, `ActiveX` being `{Active}` (`activeEqActiveXNeFalse`; R12-1) | accepted, every row (its text, `… != @p`, hid the column compared with itself; SQL Server, PostgreSQL: a syntax error) | refused: always true (since rev 18, R18-5; rev 17's text check said self-referencing, `active = active`) | the verdict sees the alias as its column (rev 18), and `p != false` is p, whose text the check also reads |
| A column, or its `Length`, date part or `ToString()`, compared with its own `[SqlExpression]` alias's where a `!`, or a comparison with a `bool` value, makes it always true in C# (R15-4, R16-5): under the `!`, `!(x.N != x.NX)` (`notNNeNX`), `!!(x.N == x.NX)`, `!(x.Flag != x.FlagX)`, `!(x.Name.Length != x.NameX.Length)` (`notNameLengthNeNameXLength`; 3.11.0 deleted the rows with a name); against the alias's `!` by `!=`, `x.Active != !x.ActiveX` (AC-N15 moves the `!`); compared with a `bool` value, `(x.N == x.NX) == true`, `(x.Name != x.NameX) != true`; `NX` being `{N}` | accepted: the rows that aren't NULL (`NOT n != n`), or every row on SQLite (`active != NOT active`) | refused: self-referencing under the `!` and against the alias's `!`; compared with a `bool` value, always true (since rev 18, R18-5); 217 of the fuzzers' (§6.5) | since rev 18 the verdict, which sees the alias as its column, before translation; the guard's text check (R14-3), which refuses EF's form of a self-equality (`(n = n OR (n IS NULL AND n IS NULL))`), stays as the backstop (its one-witness form only where the operand reads the witness) |
| An always-true comparison of a column's part with itself beside a NULL test of the column it reads (R16-5): `x.N.ToString() == x.NX.ToString() \|\| x.N == null` (`nToStringEqNXToStringOrNNull`) | accepted, every row (its text, `(COALESCE(…n…) = COALESCE(…n…) OR n IS NULL)`, is 3.11.0's own) | refused: always true (since rev 18, R18-5; rev 17's text check said self-referencing) | since rev 18 the verdict (the self-comparison under the `\|\|`); the one-witness form refuses a witness the operand reads (R15-4's narrowing keeps 3.11.0's verdict only where it doesn't) |
| Two templates that differ only in a string literal's or a quoted name's letter case, compared inside a nested conditional (rev 20, R19-3): `CASE WHEN {Name} = 'a' …` against `CASE WHEN {Name} = 'A' …`; two `[JsonPath]` keys that differ in letter case, `$.k` against `$.K`, also flat in an `\|\|` (`TheGuard_ComparesQuotedTextWithItsLetterCase_InEveryProvider`, `Delete_OfANestedTwin_IsRefused`'s JSON neighbour) | refused where its text had no parameter (the whole-clause check ignored letter case); the JSON keys in an `\|\|`: accepted, its own rows (the NULL keys dropped) | accepted, nested or in an `\|\|`: C#'s rows on SQLite and PostgreSQL, and for JSON keys on every provider (rev 19 refused them); where the database matches the two as one (a literal on a case-insensitive collation, SQL Server's and MySQL's defaults; a quoted name on SQLite and MySQL, and on SQL Server under a case-insensitive collation) that delete is always true and accepted, a Known issue (R20-5). Alone or under a `!` (`x.K == x.KUpper`, `!(x.K != x.KUpper)`), the text check, which reads quoted text letter case aside as 3.11.0's did, refuses it as self-referencing (3.11.0 refused the first, accepted the second: R20-4) | `OperandText` writes quoted text as the hexadecimal of its characters, so the comparison keeps its letter case |
| A computed property whose template is the dialect's own SQL for a column's part, compared with that part under a `!` (R17-5, R18-3): `!(x.NameLen != x.Name.Length)`, `NameLen` being `LENGTH({Name})` (`SqliteComputedPartSelfReferenceTests`) | accepted: the rows with a name (`NOT LENGTH(name) != LENGTH(name)`) | refused: self-referencing | rev 17's two-witness text check; since rev 19 the provider's SQL for the operands (R18-1), after translation and before the text check (R19-5) |
| Any operand that reads the row compared with a copy of itself (rev 18, R17-2, R17-1; the self-comparison fuzzer `fuzz18s`, §6.5): a conditional, a call or a template, literal or through an `[SqlExpression]` alias (a template that is one `{Token}`, seen as the property it names) or a template of the same text, by `==`, `>=` or `<=`, under a `!` of `!=`, `<` or `>`, through a cast through `object`, or inside an `\|\|`: `(x.Id > 2 ? x.N : x.M) == (x.Id > 2 ? x.NX : x.M)` (`condAliasCopy`, `nestedCondAliasCopy`), `x.N == x.NX`, `x.NBoth == x.NBothX`, `x.Active == x.ActiveX \|\| x.Flag == null` (`activeEqActiveXOrFlagNull`). Since rev 19 (R18-1; the twin fuzzer `fuzz18t`) also a twin only the provider's SQL shows, compared part by part: a template that is the dialect's own text for a column's part (`LENGTH({Name})` against `x.Name.Length`), two templates that differ only in letter case, two `[JsonPath]` properties on one path, inside a nested conditional (`nestedNAbsUpperEqNAbsLower`, `nestedDocKEqDocK2`, `Delete_OfANestedTwin_IsRefused`), and a second property on the same column with another CLR type (`[Column("n")] int NNonNull` against `NX`, `x.NNonNull == x.NX \|\| x.Flag == null`: round 15's probe; 3.11.0 deleted C#'s 4 of 5, as C# reads a NULL `n` as 0 there, a Known issue). Arithmetic is no example: no provider translates it, so its delete throws `NotSupportedException` first, as in 3.11.0 (R18-2) | accepted where its text didn't show the copy (its constants as two parameters, or an alias): the rows whose operand isn't NULL (`A = A`; for `>=` and `<=` likewise), or every row (`(active = active OR flag IS NULL)`); refused where it did (a parameter-free copy, twin or not; `(bool)(object)((x.N == null) == (x.N == null))`, `objBoolSelf`, among them) | refused: self-referencing, or always true inside another operator (the alias and twin forms that the text check refused as self-referencing now say always true where they sit under one, `(x.Active == x.ActiveX) != false` among them) | the verdict decides a self-comparison from the expression for any type, as 3.10's rule did for a column; distribution can hide the copy from the text check |
| `(x.N == 5) == (x.N == 5)` (R4-4); any `bool` compared with the same operand, `!x.Active == !x.Active` or a conditional with itself, alone or inside an `\|\|` (R7-5); since R12-1 also a copy that differs by built-in casts, a string's `ToString()` or a widened constant, `(x.Id > 2) == ((int?)x.Id > (int?)2)` (`idGt2EqLiftedIdGt2`, R13-2) | accepted: nothing for an equality predicate (SQLite, MySQL), every row for a relational one on SQLite (MySQL: nothing) and for the others; a syntax error (SQL Server, PostgreSQL 42601) for the predicate and `!` forms; a copy whose text matched its original's (no parameters in it) was refused | refused: a self-reference, or always true inside an `\|\|` | the verdict counts a predicate compared with itself, as `x.Id == x.Id` |

## 5. Design

### 5.1 One rewrite in Core: `Funcular.Data.Orm.Linq.NullSemantics`

A public static class.

**`Expression Rewrite(LambdaExpression predicate, IDictionary<Expression, bool?> memo, Func<PropertyInfo, bool> isMapped, IReadOnlyCollection<string> remoteProperties)`**
returns a body with C#'s (EF's) value for every row, built only from shapes the visitors translate.
- It recurses over **truth positions**, carrying a polarity. These are:
  - the body;
  - the operands of `&&`, `||` and `!`;
  - a `bool` conditional's branches;
  - the **test of every conditional in a row operand** (R3-1, R4-2): every accepted row operand of every comparison
    is descended, through its casts and into conditionals nested in value branches, whether or not the comparison
    itself is compensated, as in `(x.N != 5 ? x.Id : 0) > 0`. The test is rewritten positive. A non-`bool`
    conditional's branches stay values.
- Each step returns either "unchanged" (the node as it is, or wrapped in `!` under negation) or a new expression.
  Unchanged propagates up, so a subtree with nothing to compensate keeps 3.11.0's structure (AC-N4, AC-N11).

**First, the casts between `bool` and `bool?`** (R7-1, R8-1, R9-2). One pass, bottom-up, puts every lift to `bool?`
outermost before the rules run:
- `!(bool?)p` is `(bool?)!p` (C#'s lifted `!` of a value that is never null);
- a `bool?` conditional whose branches are each a lift, or a constant that isn't null, is the lift of the `bool`
  conditional;
- a cast back to `bool` over a lift is the form lifted (`(bool)(bool?)e` is `e`). With the rule above,
  `(bool)(t ? (bool?)a : (bool?)b)` is `t ? a : b`, through `!` and nested conditionals;
- a cast through `object` between `bool` and `bool?` is the cast without it (a box and an unbox).

A cast back to `bool` over a `bool?` that can be null stays (§3; rule 9 rewrites a conditional under it). So the rules
below see a predicate, `!` of one, a row operand or a conditional the same wherever the casts were, and a body with no
such chain is the same node. A part
that stays in a value position sends the same SQL, since the visitors drop a cast. A conditional that a cast back to
`bool` kept in a value position is in a truth position now, so its known parts are literals (AC-N11 (a); R9-4).

**Rules at a truth position:**
1. `WherePredicateGuard.Fold` runs with the visitor's memo, and a known truth becomes a `bool` constant. It counts as
   unchanged: the visitors send it as the literal they send for the node itself (§5.3), so the structure around it
   is kept. A left-deep chain of one logical operator is walked iteratively (a 1,500-term disjunction would
   otherwise recurse once per term).
2. `!e` flips the polarity.
3. `&&` / `||` take the rewritten operands, with the operator swapped under negation (De Morgan) only if an operand
   changed; otherwise unchanged. `&` and `|` are untouched.
4. A `bool` conditional: its test is rewritten positive, and its branches carry the polarity.
5. A `bool` against a `bool` (AC-N14, AC-N15):
   1. Each side is, in order: a value (it reads no row, and is read once, through `!` and a conditional's test; or
      `Fold` knows its truth; R6-2, R6-3); a predicate, under its lifts, `!(bool?)p` read as `!p` (R7-1); a row
      operand under its `!`s and lifts (R6-2); or not accepted, and then the comparison is left as it is. A
      conditional's branch is classified the same way.
   2. Two values: C#'s lifted comparison.
   3. A predicate against a value: true → p; false → !p; null (`bool?`) → false for `==`, true for `!=`.
   4. A predicate against a predicate or a row operand: each predicate is its two-valued value, `p ? true : false`
      (EF's `CASE WHEN p THEN 1 ELSE 0 END`), and the comparison is rule 6's between values (R11-5, the owner's
      ruling: EF's form). A row operand m is compared without its `!`s: `!m == p` is `m == (p ? false : true)`, C#'s
      lifted `!`, so no NOT is sent on a bit value.
   5. Each side is sent once, so it is read once. Where rule 6 repeats an operand as a witness, it substitutes it
      first (R6-5), and a lazy collection is materialized there, once (§3).
   6. The comparison is then rewritten under the current polarity.
   7. Otherwise (a value or a row operand against a row operand) it is rule 6, each side's `!`s counted (AC-N15).
6. `==` / `!=`:
   - Under negation the operator flips. `Expression.Equal`/`NotEqual` re-resolve operator methods (`string`,
     `decimal`, `DateTime`, lifted forms).
   - Each operand is classified as a value, an accepted row operand that can be null (with its witness), one that
     can't be null, or not accepted.
   - In order:
     1. Both values: left as is.
     2. Any operand not accepted: left as is, and no value is read (AC-N13).
     3. A null value against an operand that can be null: `row == null` / `row != null`. An odd number of `!`s
        under the operand's casts (ones the moved `!`s didn't reach) is tested under them, since NOT NULL is NULL and
        SQL reads `NOT x IS NULL` as the complement (R11-1).
     4. A null value against an operand that can't be null: `false` / `true`.
     5. A non-null value against an operand that can be null: `!=` becomes `(cmp) || W == null`; `==` is unchanged.
        With a nested conditional witness, it is distributed (below).
     6. Both row operands: EF's forms, with each witness once; with a nested conditional witness, distributed.
   - **Distribution (R12-2, R12-5; the owner's ruling, 2026-10-21).** A witness is a *nested* conditional when its test
     holds a conditional, or a branch does other than a nested flat conditional: it holds an earlier comparison's form,
     and EF's repeat of it would compound. A *flat* one takes EF's forms, a bounded copy that adds no CASE level. A
     compensation over a nested one is `(t ? (a op v) : (b op v)) == true`: the comparison is pushed through the casts
     and `!`s over the conditional into its branches, and into the nested conditionals in them, on either side, the
     larger side first where both are nested, so the leaves repeat the smaller one (R15-1). Each branch repeats the
     other operand below the distributed levels, so at step 6 and rule 7 the distribution is kept only where its CASE
     nesting, counted as SQL Server counts it (a nullable integer's `ToString()` is a `COALESCE`, one level), is at
     most the comparison's as written + 1, or no more than EF's form's (`DistributeWithinTheBound`; R14-1, the owner's
     ruling, 2026-10-22: the bound-checked distribution), the count reading an `[SqlExpression]`'s template levels
     (R15-3, R16-2); EF's forms otherwise (R13-1). Each leaf comparison is compensated by these
     rules, as its two-valued value; a flat conditional left in a leaf is rewritten already and isn't rewritten again.
     The test is sent once and each conditional's CASE stays where it was; a leaf's two-valued CASE adds one level
     below a branch, so the CASE nesting is at most the source's + 1, where a branch is as deep as the test (AC-N17,
     §3). C# evaluates only the branch the test takes, so it is C#'s answer too. Rule 7 substitutes the operand that
     can't be null as well before it is repeated (AC-N13), and EF's form, where the bound keeps it, sends that operand
     substituted too. R11-5's single CASE around the comparison (`CASE WHEN complement THEN 0 ELSE 1 END = 1`) is withdrawn:
     nested, it doubled the CASE nesting.
   - **Substitution (R3-2, R3-5).** Every value read is substituted:
     - **A non-null value** replaces the innermost captured member with `Constant(value, member.Type)`, keeping the
       `Convert` chain. The visitors drop a `Convert` and bind the member's value, so the binding is 3.11.0's.
     - **A null value** replaces the whole value operand, with `Constant(null, operand.Type)` in step 3 or a fold in
       step 4, so the visitors' bare-null test sends `IS NULL`.
     - A value is read whenever the row operand is accepted, including a non-null value against an operand that
       can't be null, where no step applies.
   - **When no step changes the comparison**, it keeps its operator, and under negation it is `!(original)`, with
     the member swapped. So `!(x.Id == v)` stays `NOT t.id = @p`.
   - **`!`s over a row operand** (from rule 5) are moved to the comparison (AC-N15): `!m == v` is compared as
     `m == !v`, and two row operands with one `!` between them compare `a != b` inside `==`'s form (`a == b` inside
     `!=`'s), with `a IS NOT NULL AND b IS NOT NULL` beside it where both can be null, which keeps the rewritten body
     C#'s too. Where nothing is compensated, the comparison is as written.
   - **Substitution never replaces the node it's given** (R6-3). For a row operand that is duplicated as its witness,
     it also keeps the casts and `!`s down to the witness, and the witness, so the rewritten operand still has one
     when the fold knows it once rewritten (R9-1). Below them, a `bool` part whose truth the fold knows, from the
     memo or now, is that truth, so nothing under it is read (R8-3). A witness's branches can fold too, so its kind
     is taken again from the substituted operand: one whose only branch that can be null folded can't be null as
     sent, and step 5 adds no `IS NULL` for it, and step 6 takes the form for what can still be null (R10-2) and
     sends the substituted operands, so nothing is read again (R11-2).
   - **A conditional's value branch:** a value is read and substituted (AC-N11 (g)); a `bool` branch whose truth the
     fold knows is that value; a predicate branch is `p ? true : false` (AC-N16); a nested conditional is rewritten.
     A row operand is descended through its casts and its `!`s.
   - **A conditional used as a witness** (`CASE … END IS NULL`) is the rewritten conditional, with its captured
     members substituted before it is duplicated.
   - `W == null` is `Equal(W, Constant(null, T?))`, through `Convert` to `Nullable<T>` for a non-nullable value
     type.
   - **Classification by type only, no read:** a conditional's value branch can be null when its type is nullable
     (`x.Id > 2 ? x.Code : none`). A `Convert` with a user-defined operator method (not a core-library one) makes
     the operand not accepted, as `Fold` treats it. A cast keeps the operand's kind, as EF's does, even to a type
     that can't hold null: a property can be NULL in SQL whatever its CLR type (R11-1; rev 11's assertion is
     withdrawn). `!` under such a cast meets `IS NULL` only through step 3, which tests the operand under the `!`s.
7. `>`, `>=`, `<`, `<=`:
   - a null value (read once): `false`, or `true` under negation;
   - under negation: `!(cmp) || W1 == null [|| W2 == null]`; with a nested conditional witness, distributed (R12-2);
   - otherwise the original node.
8. Other leaves, such as a method call or a `bool` member: unchanged, wrapped in `!` under negation. The translator
   answers a negated method call (§5.2). **A negated call the translator compensates is a change** (R4-1), so a
   negated parent pushes `!` down to it: a string `Contains`/`StartsWith`/`EndsWith` on a property of the row, or a
   collection `Contains` whose item is a property that can be null. A literal null (`x.N == null`) is already rule
   6's step 3, and keeps its node.
9. A cast back to `bool` over a conditional the cast pass leaves (a branch C# types as able to be null, such as a
   captured `bool?`): a row operand, as in a comparison. Its test is rewritten (R4-2), its branches stay values, and
   under negation it is `!(cast)` (R10-1).

**AC-N11 holds by construction.** A subtree is rebuilt only where a leaf is compensated. A substitution, or a fold,
alone keeps the structure and the binding. AC-N11's exceptions (a)–(i) are the only other text changes.

**AC-N13 holds by construction, for predicates that translate.** Values are read at most once per comparison, and
only when the row operand is accepted. `Fold`'s own reads of a captured `bool` are 3.11.0's or fewer: it stops at an
operand of `&&`/`||`/`&`/`|` that decides it, and asks a small one that reads no row first (R9-3, R10-4), where 3.11.0
folded both. The memo
makes a second `Fold` of the same node a lookup. The one bound that differs is a cast through `object` (AC-N13).

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
is written as `NOT ` + the operand, visited in the NOT's own position (R6-1). In a truth position it is a predicate.
As a comparison's operand (a value position), a conditional, alone or under more `!`s and casts, keeps its value's
form whether or not its truth is known, and its branches stay values, since a literal there isn't a value T-SQL
takes. Any other known truth there is the literal, from the memo, as 3.11.0 sent it (R7-2).

**`VisitConditional`** visits the `Test` through `VisitPredicate`, and also both branches when the conditional is
itself in a truth position (`VisitPredicate` passes `truthPosition: true`). So a constant there is `NOT 1=0` /
`1=0`, not a parameter. A conditional in a value position (a comparison's operand) keeps its branches as values, as
3.11.0 sent them, `bool` or `bool?`: T-SQL has no boolean value there (R5-1). A non-bool branch is visited as before. This is
AC-N11's exception (a).

**The translators' `ToString()`** of an integer column is `COALESCE(CAST(…), '')` wherever
`NullSemantics.CanBeNull(property)` (R4-10); a plain `int` column is the bare cast, as before.

**The fold** (`WherePredicateGuard.FoldNode`) evaluates a `bool` comparison that doesn't read the row once, whole,
instead of each side and then the whole (R5-3). With a memo, it records a captured value it read that a truth can't
hold (a null `bool?`) beside the memo, in a `ConditionalWeakTable`, and the rewrite reuses the read (R6-5).

**The verdict** (`WherePredicateGuard.IsSelfComparison`, the broad rule only) counts any operand that reads the row
compared with the same operand or a copy, whatever the type and the operator, by a structural comparison of the two trees
(`SameValue`: through built-in casts, a string's `ToString()` and widened constants; an `[SqlExpression]` alias, a
template that is one `{Token}` in every variant, is the property it names; two templates of the same text in every
variant are the same) (R4-4, R7-5, R12-1, R17-2). It sees through a cast through `object` to the `bool` it boxes
(rev 18): a cast that reads no row is evaluated whole, as 3.11.0's verdict evaluated it; one that reads the row is folded
inside under rules that evaluate no leaf (`VerdictUnderACast`, rev 21, R20-1), so a self-comparison or a literal there
counts and no captured value under it is read, as 3.11.0's verdict read none; an empty search value written as `""`
or `string.Empty` counts there too (rev 22, R21-1). A part always true, or self-referencing, only through such a value
is the translated clause's check's to refuse, where the rewrite sends the value as a literal, and accepted otherwise,
as 3.11.0 accepted it (R21-2; the Changelog's Known issue). A member is the module and metadata token of its original
declaration, a property's being the property that declares its accessor's base definition (the getter's, or the
setter's where an override has only that), so an override is the property it overrides (R21-4, R22-1).

**The provider's SQL for a part** (rev 19, R18-1; rev 20, R19-1, R19-3). Each provider's shared WHERE translation checks
the argument contract first (a read's `Validate`), and once the predicate has translated calls
`WherePredicateGuard.Validate(predicate, operation, operandSql)` in place of `Validate(predicate, operation)`, so the
verdict is given once (rev 19 gave it twice, reading a captured value twice: R19-1; the reads are AC-N13's). It passes
a fresh visitor and parameter generator for each part asked; `OperandText` writes the part's parameters as their values
(type and hexadecimal text) and its quoted text (a string literal, a JSON path, a quoted name) as the hexadecimal of its
characters, so a letter-case-blind comparison keeps their case (R19-3). Its `SameSqlUnifier` rewrites each comparison
whose two operands are the same by `SameValue`, where any two parts that differ in shape are also the same when the
provider gives both the same SQL, letter case aside outside quoted text, into a comparison of its left operand with
itself; then the verdict decides as for a literal copy. Part by part, not whole operand against whole
operand, because a visitor folds a literal copy (`x.N == x.N` is `NOT 1=0`) where its twin (`x.N == x.NAlias`, or a
template that is the dialect's own text) stays `n = n`: a nested conditional holding one and its copy holding the other
would differ as wholes. A part's SQL is asked once per node, and a member of the row's once per member, by its original
declaration's module and metadata token as `SameMember` compares members (the verdict sees an alias as a new node for its
target each time: R19-2; a base class's property found on the derived type is the compiler's: R20-2; an override is the
property it overrides: R21-4; a part written twice is two nodes, each asked), only for parts of
at most 64 nodes on both sides (so
a chain whose every level compares a conditional with a column costs a bounded amount per level), and never for a part
that reads a captured value, calls a method or constructor on no row, or holds a lambda (with parameters or none) or an
array: the provider's SQL for a part evaluates nothing the caller wrote (AC-N13), and such a part is compared as written
(the same captured member is the same value). A throw from
the provider's SQL counts as no SQL. A read asks nothing. A custom provider that calls only `Validate(predicate,
operation)` and `ValidateTranslation` keeps the whole-clause text check.

**`All`:** each LINQ provider translates `x => !p` through the visitor, and drops its `NOT (…)` wrapper.

## 6. Test plan

### 6.1 Harness entities and captured values (§6.3 has the tables)

**Entities:**

| Entity | Purpose | Delete paths |
|---|---|---|
| `NsRow` (`zz_ns_row`) | plain columns only; holds every key of N1–N11 and N14 | yes |
| `NsComputed` (`zz_ns_row`) | `[JsonPath]`, `[SqlExpression]`, `[SubqueryAggregate]` columns: inline SQL, so deletes run; its WHERE is read by a parenthesis-aware outer-WHERE scanner | yes |
| `NsRemote` (`zz_ns_row`) | `[RemoteProperty]` / `[RemoteKey]` through `parent_id` | reads only, plus one delete per kind asserting the database's unknown-column error (unchanged) |
| `NsReverse : NsParent` (`zz_ns_parent`) | one reverse key, `[RemoteKey(typeof(NsRemote), nameof(NsRemote.Id))]` | `ReverseJoin_AFilterThatFoldsAway_NeedsNoJoin`, §4.3's reverse-join row |

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
| `nullBool` | `(bool?)null` (R4-4) |
| `c` | a `Holder` with counted getters: `Five` (int 5), `ShortVal` (short 5), `IntVal` (int 5), `ColorVal` (`Color.Green`), `NullableFive` (`int?` 5), `Flag` (`true`), `Flip` (alternates null and 5). The `DateTime` is the core test's (R4-3) |

### 6.2 The AC → test matrix

Each key is labeled per provider:
- **proof:** its rows, verdict or SQL differ between `25956e0` and the AC, and the red run must fail it with the
  asserted rows, SQL or verdict, not with an exception;
- **pin:** it passes at `25956e0` and after;
- **rows-pin / SQL-proof:** the rows are equal, and the SQL is asserted and differs;
- **dialect-rejected:** the database rejects it on that provider both before and after;
- **provider-rejected:** the driver rejects it before and after, for a reason outside this plan (PostgreSQL's CLR
  enum parameters, backlog Appendix A #5);
- **red by the database's own rejection** (R3-7, R4-9): a proof whose 3.11.0 run fails with that database's error:
  PostgreSQL's 42883 for every null-parameter key, its 42601 for the AC-N14 comparison chains and its 42804 for a
  null in a `CASE` branch; SQL Server's syntax error for every AC-N14 key and for a captured `bool` as a
  conditional's test. 3.12.0 runs them.

A proof that passes at `25956e0` is a TEST-GAP, fixed before Task 2.

| AC | Proofs | Pins and rows-pin/SQL-proof | Where |
|---|---|---|---|
| N1 | `nNeV`, `nNe5`, `vNeN`, `nameNeA`, `aNeName`, `flagNeTrue`, `flagNeFalse`, `dYearNe2020`, `nameLengthNe1`, `eNeRed` and `eNeGreenLiteral` (provider-rejected on PostgreSQL), `condNullableNe`. The **Convert twins on nullable columns** (R3-3), each asserting 3.11.0's exact comparison and parameters plus ` OR col IS NULL`: `nNeCapturedShort` (`x.N != c.ShortVal`), `nLongNeCapturedInt`, `nPriceNeCapturedInt`, `colorNeCapturedColor` and `eNeGreenLiteral` (provider-rejected on PostgreSQL) | `reqNeA` rows-pin/SQL-proof | `NsRow`, all four |
| N1, attributes | `parentLabelNeP`, `parentCodeNe5` (`[RemoteProperty] int`), `parentKeyNe1` (`[RemoteKey] int`), `docKNe5` (`[JsonPath]` string), `docKEqNone` (MySQL's expected rows are its own: a JSON null is the text `'null'`, a Known issue), `nPlusOneIntNe6` (`[SqlExpression("{N} + 1")] int`), `childSumDNe99` (`[SubqueryAggregate(Sum)] decimal`: uncompensated since the owner's ruling on T8E2-4), `childIdSumNe0`, `childIdSumEq0` (a non-nullable `int` Sum against 0: 3.11.0's rows), `childIdSumEq3` (parents 1 and 5), `activeNeChildIdSumEq0Pin`, `condChildIdSumEq0EqFalsePin` (the Known issue's rows under the two-valued CASE), `childAvgDNe0` (a non-nullable `decimal` Avg), `childIdSumNNe0` (a nullable Sum, compensated), `parentCodeToStringNe5`, `parentCodeToStringEqEmpty` (COALESCE; R4-10) | `childCountNe1`, `parentCodeEq0` | `NsComputed`, `NsRemote`, all four |
| N2 | `nEqNone`, `nNeNone`, `noneNeN`, `nameEqNoName`, `nameNeNoName`, `eEqNoColor` and `nEqShortNull` (a null under a `Convert`, R3-2: C# {3,4}; 3.11.0 {}), `idNeNoneInt`, `idEq2OrIdNeNoneInt`, `idNeNullLiteral`, `notIdEqNullLiteral`, `notIdEqNone`, `condFoldedTest` (`(x.Id == none ? x.N : x.M) != 5`: rows {5,6} → {2,4,5,6}), `nEq5OrNNe5` (every row; the delete accepted, §4.3) | `idEqNoneInt` and `idEqNullLiteral`: rows-pin/SQL-proof (`1=0`). `reqNeNoName` is a proof (R4-5: 3.11.0 selected and deleted nothing; the delete is accepted, every row) | `NsRow` |
| N3 | `nEqM`, `nNeM`, `nNeCode`, `nameLengthSelf` (the de-duplicated text asserted) | `nEqCode` | `NsRow` |
| N4 | `notNEqV`, `notNGtV`, `notNGeNone`, `notNGtNone`, `notNLtNone`, `notNLeNone`, `notAndEq`, `notOrEq`, `nNe5OrNameEqA`, `notNameLengthGt1`, `condTestNe` (the test of a non-`bool` conditional, R3-1), `condTestNeGt0`, `condTestNeEqV` and `nestedCondTestNe` (a test inside `>`, under a `Convert`, and nested; R4-2), `idOrNotNGeNone`; a negated call beside a sibling that needs nothing (R4-1): `notIdAndNameContainsA`, `notIdOrIdsContainsN`, `notIdAndNameContainsNull`; `notCondTestNeEq1` (a negated comparison that needs nothing around a compensated test; R5-5), `notNGtM` (two witnesses; R5-5) | `notNNeV` (`NOT n != @p`, kept: the flipped `==` needs nothing); `doubleNot` (`NOT NOT …`); `notIdAndCodeEq5` (`NOT (id = @p AND code = @p)`, the structure rule's own evidence, an N11 pin; R4-1); `notBoolCond`, `boolCondBranches`: dialect-rejected on SQL Server (reads and delete), proofs elsewhere | `NsRow` |
| N5 | `nGtNone` on PostgreSQL (red by 42883) | `nGtNone` rows-pin/SQL-proof (`1=0`) elsewhere; `nGtV` a plain pin | `NsRow` |
| N6 | `idsWithNullContainsN`, `onlyNullContainsN`, `notIdsContainsN`, `notOnlyNullContainsN`, `id3AndIdsWithNullContainsN` (C# {3}; 3.11.0 {}; without parentheses {3,4}), `id3AndNotIdsContainsN` (C# {3}; 3.11.0 {}; without parentheses {3,4}), `notNamesContainsName` (C# {2,4,6}; 3.11.0 {6}), `notParentKeysContainsParentKey` (`NsRemote`), `idOrNotOnlyNullContainsN` and `notIdAndOnlyNullContainsN` (§4.3 row 9's other shapes; R4-3) | `idsContainsN`, `notIntIdsContainsId`; `notIdsWithNullContainsN` rows-pin/SQL-proof (`(n IS NOT NULL AND NOT n IN …)`); `CastItemContains_IsRejected` | `NsRow`, `TranslatorDirectHarness` |
| N7 | `notNameContainsA`, `notNameStartsWithA`, `notNameEndsWithA` | — | `NsRow`, `TranslatorDirectHarness` |
| N8 | `notNameContainsNull`, `notNameStartsWithNull`, `notNameEndsWithNull`, `idOrNotNameContainsNull`, `idAndNotNameContainsNull`, `notAndContainsNull`, `nameContainsNullNeTrue`, `notNameContainsNullEqTrue` (§4.3 row 4's other shapes; R4-3) | `nameContainsNull` | `NsRow`, `TranslatorDirectHarness` |
| N9 | `All_AnswersAsTheOracle`: `allNGt0`, `allNameContainsNull`, `allNIn517`, `firstTwoAllNameContainsA` (`Where(x => x.Id <= 2).All(x => x.Name.Contains("a"))`: EF false, 3.11.0 true) | `allNNe5`, `allReqNeZz`, `allNotNameContainsNull` | all four |
| N10 | every key's parity assertions (`EveryPath`, now also over `NsComputed`: R4-12); the §4.3 table in `VerdictChanges_AsListed` (27 shapes, with `idEq2OrIdNeNoneInt` and `nGtNoneCondCode`: R5-9); `ReverseJoin_AFilterThatFoldsAway_NeedsNoJoin` (refused by fan-out at `25956e0`) | the existing `WhereParityHarness`/`DeleteGuardHarness` rows outside §7; `Remote_Delete_IsTheDatabasesUnknownColumnError` (one per kind, unchanged; §6.1) | all four |
| N11, (f) and (g) | `condNullBranchEq5`, `condNullBranchGt3` (`ELSE NULL END`: red on PostgreSQL by 42804, a SQL-proof elsewhere; R5-4) | `nGtNone`, `idEqNoneInt` (rows-pin/SQL-proof `1=0`) | all four |
| N11 | — | `NonNullableShapes_SendTheSqlOf3110`, literal WHERE plus parameters, per provider, captured at `25956e0`: `idNe5`, `idEq5`, `notIdEq5`, `notCodeGt5`, `notIntIdsContainsId`, `nameEqA`, `nEq5`, `nameContainsA`, `nEqNullLiteral`, `nGt5`, `condNe`, `capturedNEqV`, `codeNeCapturedShort`, `longNeCapturedInt`, `priceNeCapturedInt`, `notIdEqCapturedV`, `idNeCapturedNullableFive` (a non-null value against a column that can't be null: read, substituted, unchanged), `doubleNot`, `notNNeV`, `nEqCode`, `idsContainsN`. `notIdAndCodeEq5`. Exceptions (a): `condCapturedTest` (SQL-proof), `condCollationTest` (`(s == "A" ? x.Id : 0) > 0` with `s = "a"`: rows differ on SQL Server and MySQL's case-insensitive collations), `condCapturedBoolTest` (a captured `bool` test: red by SQL Server's error, a SQL-proof elsewhere), `boolBranchCapturedLiteral` (`THEN NOT 1=0`: a `bool` branch; dialect-rejected on SQL Server; R4-11). (b): `idEqNullLiteral`, `idNeNullLiteral`. (c): `All_SendsTheTranslationOfNotP`. (d): `nameContainsAEqTrue`, `flagEqNGt5`. (e): `parentCodeToStringEqEmpty` | all four |
| N12 | `DocumentationTruthTests.NullComparisons_DocumentedAsCSharp` | — | SqlServer.Tests |
| N13 | `ReadOnce_CountingGetter`. Pins that kill M-N13: `x.N != c.Five` reads 1, `c.Five != x.Child.Id` reads 1 (as 3.11.0), `x.Child.Id != c.Five` reads 0, `c.Flag != x.Flag` reads ≤ 3.11.0's count, `(x.N == c.Five) && x.M == 5` reads 1. Proof: `x.N != c.Flip` (the WHERE form agrees with the bound value). Also (R5-2, R5-3): `(x.Id > c.Five ? x.N : x.M) == null` reads 1; `(((bool?)c.Flag == c.NullFlag) ? x.Id : x.Code) > 5` reads each once; `Reads_ALiteralNullAgainstAConditional_Once`, `Reads_AFoldedComparisonOfCapturedBools_Once`; `ALazyCollection_InARepeatedOperand_IsEnumeratedOnce` (R5-8; since rev 12 a conditional EF's forms repeat three times, as AC-N14 sends a predicate once). Round 6 (R6-5): `(x.Id > c.Five ? x.Active : x.Other) == (x.N == 5)` reads 1, a lazy collection in that test is enumerated once, `(c.Five > 3 ? true : false) == (x.N == 5)` reads 1 (3.11.0: 2), `(x.N == 5) == !c.Flag` reads 1, a branch the fold knows reads nothing under it, and a null `bool?` the fold read inside a predicate side is read once; `Reads_ANullBoolTheFoldRead_Once`, `Reads_NothingUnderABranchTheFoldKnows`; `ALazyQueryable_InARepeatedOperand_IsExecutedOnce`, `ALazyCollection_OfAConcreteType_IsEnumeratedOnce_AndOneThatThrows_IsLeftForTheTranslator`. Also: `x.Id != c.NullableFive` reads 1 (no step applies; SQL pinned); `(x.Id > 2 ? x.N : c.Flip) != 5` reads 1 (the conditional is substituted before it's duplicated); `c.Flag != x.Flag` reads 1 (3.11.0: 2); the expansion's `(x.N == c.Five) == (x.M == 5)` and `x.Name.Contains(a) == c.Flag` read 1, and `(x.N == c.Flip) == (x.M == 5)` selects the rows of its bound null (R4-8) | — | all four |
| N14, N15 (R6-1, R6-2, R6-4) | `notBoolCondEqTrue` (red at rev 6 on SQL Server by its syntax error, a SQL-proof elsewhere: no literal in a branch), `notNboolCondLiteralNeFalse`, `nEq5EqNotActive`, `notActiveEqNNe5`, `nEq5EqNotCapturedFlag`, `flagNeNotCapturedFlag`, `nEq5EqCondNotOther` (dialect-rejected on SQL Server, §3), `notFlagNeTrue`, `notFlagEqNull` (3.11.0 and rev 6 deleted the complement), `flagEqNotFlag`, `notCondFoldedTestNeOther` (the self-check); `Rewrite_NotOfAColumn_MovesToTheComparison`, `Rewrite_ASideThatReadsNoRow_IsAValue`, `ANotInAValuePosition_…` (two) | `notNEq5EqActive`: a proof (its rows at `25956e0`; R7-8), M-N42's killer: `!` of a predicate (R6-6) | all four |
| N14, N16 (R7-1, R7-2) | `flagNeNotLiftedNGt3`, `notLiftedNGt3EqNull` (3.11.0 and rev 7: `NOT n > @p IS NULL`, an accepted delete of the rows C# keeps), `notLiftedNameContainsANeFalse`, `condNotLiftedBranchNeTrue`; `Rewrite_NotOfALiftedPredicate_IsAPredicate`; `ANotInAValuePosition_OverAKnownTruth_IsItsLiteral` (the visitor's own, as 3.11.0); a ReadOnce row (`!(bool?)(c.NullableFive == none)` in a branch: once) | `condSelfOrCode5` (R7-5: a conditional compared with itself inside an `\|\|`, refused as always true; 3.11.0 deleted every row), and the guard rows `notFlagSelfEq`, `notFlagSelfOrId2` | all four |
| N14, N15, N16 (R7-4: the redesign's branches) | `notNotCondFoldedTestNeOther` (two NOTs over a folded conditional); `Rewrite_ValuesUnderNegation_AndAConditionalsFalseBranch`; `!a == b` over two `bool?` columns in `Rewrite_KeepsCSharpsValue` (both guards) | `nboolCondNePredicateBranchNeTrue` (AC-N16's p rewritten positive; a pin except on SQL Server, a proof there) and `nboolCondNotCondBranchNeTrue` (descent through `!`; dialect-rejected on SQL Server, §3): pins at `25956e0` that kill Y15 and Y22 (R8-5) | all four |
| N14 (R10-1..R10-4) | `castCondCapturedBranch` (C# {1,2,3,5}), `notCastCondCapturedBranch` ({4,6}), `idGt2AndCastCondCapturedBranch` ({3,5}), `castCondCapturedBranchAsTest` ({1,2,3,5}): a cast back to `bool` over a conditional with a captured `bool?` branch, as the predicate, under `!`, under `&&` and as a conditional's test, each with its delete; dialect-rejected on SQL Server (§3). `nullEqLiftedCastNotCond` ({}; a cast to a non-nullable type compared with null: 3.11.0 and rev 10 sent `NOT … IS NULL`, every row; dialect-rejected on SQL Server since rev 12, §3). `computedCondWitnessNeTrue` (`NsComputed`, a `[SqlExpression] bool` branch that folds: rev 10 threw; 3.11.0's rows on SQLite, SQL Server and MySQL, 42883 on PostgreSQL). `Rewrite_ACastBackToBool_OverAConditionalItLeaves_IsARowOperand`, `Rewrite_AWitnessWhoseNullableBranchFolds_IsNotTestedForNull` (rev 11's `Classify_ACastToANonNullableType_AssertsAValue` went with the assertion, R11-1), eleven `Rewrite_KeepsCSharpsValue` rows; `Fold_OfALongChainOfCapturedOperands_StaysLinear` (a timing proof, best of five) | `objCodeSelfAndIdEq2`: an AC-N11 (a) SQL pin (`NOT 1=0`; the rows agree before and after); `idEq2OrObjCodeSelf`, `falseNeObjNameLengthSelfLe`: verdict pins (§4.3; refused already at rev 10, accepted at `25956e0`) | all four |
| N14 (R11-5: no level repeats an earlier one) | `nestedCondNeTrue`, `nestedPredicateEqPredicate`, `nestedFlagEqPredicate`, `nestedCondNeOther`, `nestedNotCondNeOther`, `notCondGtPredicateValue`: nested comparisons, each with C#'s rows on every path, `m` sent at most three times and a `CASE` per level (rev 11 sent `m` 8 or 12 times; at `25956e0` they fail by rows, by SQL shape, or by the database's rejection); `condNeFlagBothNull`; `Rewrite_NestedComparisons_GrowLinearly` (ten forms, four and eight levels down: `x.M` as often at both, at most three times, and C#'s value; two nested sides take EF's forms since R13-1), 17 `Rewrite_KeepsCSharpsValue` rows | `flagEqNGt5`: its SQL-proof is now the `CASE` (`CASE WHEN … n > `) | all four |
| N10, N13, N17 (R14-1..R14-3) | `nestedCondChainNN8`, `nestedCondChainNNRight8` (a nested conditional compared with a conditional never NULL by value at every level, eight deep, either side: at most 9 CASEs and 200 parameters; rev 14: SQL Server's "maximum of 2100 parameters", SQLite's "too many SQL variables"), `nEqNX`, `notNNeNX` (`NsComputed`'s `NX`, `{N}`), `objNameLengthSelfEq` (a cast through `object`): refused as self-references, `nEqNX` and `objNameLengthSelfEq` as 3.11.0 refused `n = n`, `notNNeNX` where 3.11.0 accepted `NOT n != n` and deleted the rows that aren't NULL (§4.3, R15-4) (rev 14 deleted every row; at `25956e0` rows); `Rewrite_NestedConditionalsAgainstConditionals_GrowLinearly` (eight families, four and eight levels down, both sides: at most three times the size, the source's CASE nesting + 1, C#'s value), `Distribution_ReadsTheOtherOperandOnce_…` (back to rev 13's assertion: the distributed conditional operand substituted), `Rewrite_ANestedConditionalAgainstAConditional_TakesEFsForm`'s `nested != flat(3)`; the guard's text rows for EF's self-equality, both forms, and four neighbours | the chain keys: pins at `25956e0` (3.11.0's 25 parameters and C#'s rows), proofs against rev 14 | all four |
| N10, N13, N17 (R15-1..R15-6; the self-check's `COALESCE`) | `nestedCondChainNestedLeft7` (two nested sides at every level, a shallow nested conditional on the left, seven deep: at most 9 CASEs and 200 parameters; rev 15: past SQL Server's 2,100), `nestedCondNeFlat8CodeCase` (`NsComputed.CodeCase`, a template with its own CASE, ending a flat conditional eight deep: 9 CASEs; rev 15: 11, Msg 125), `nestedStrNeFlat8NToString` (the same with `x.N.ToString()`'s `COALESCE`: 9 CASEs; before the counter counted it, Msg 125), `nameLengthEqNameXLength`, `nameXLengthEqNameLength`, `dYearEqDXYear` (`NsComputed`'s `NameX`, `DX`: refused as self-references, as 3.11.0 refused them; rev 15 deleted every row), `activeEqActiveXOrFlagNull` (accepted, every row, as 3.11.0; rev 15 refused it); `Rewrite_NestedConditionalsAgainstConditionals_GrowLinearly`'s two R15-1 families (`(S(j) != L) ? Code : 5` and its mirror: rev 15 3,616 nodes four levels down and 297,376 eight down), `Rewrite_AComputedPropertyOperand_KeepsEFsForm` (step 6 and rule 7; the same shape over a column is distributed; rev 17's `…_CountsItsTemplatesLevels`), `Rewrite_ANullableIntegersToString_CountsAsACase` (a non-nullable integer's is distributed); the guard's text rows: the two-witness form over a column the operand reads (four, one bracket-quoted), and three that 3.11.0's check accepted (a column the operand doesn't read, twice; a name that begins another's) | `nestedCondChainNestedLeft7`, `nestedCondNeFlat8CodeCase`, `activeEqActiveXOrFlagNull`: pins at `25956e0` (3.11.0's rows and verdict), proofs against rev 15; `nestedStrNeFlat8NToString` and the R15-2 keys: proofs at both (rows at `25956e0`) | all four |
| N10, N17 (R16-1..R16-6) | `TheGuard_RefusesAComputedPartAgainstTheColumnsPart_InEveryProvidersText` (`WhereVisitorDirectTests`: each provider's visitor text for seven computed-part shapes, refused as 3.11.0 refused them; rev 16 accepted 8 of 8 in round 16's probe), `SqliteComputedPartSelfReferenceTests` (SQLite's delete of six such shapes: refused, the rows kept; rev 16 deleted 5 of 5), `nxChainInTest7` (an alias in a chain's innermost test, seven deep: at most 9 CASEs and 200 parameters; rev 16 past SQL Server's 2,100), `notNameLengthNeNameXLength`, `nToStringEqNXToStringOrNNull` (R16-5's refused always-true deletes; 3.11.0 accepted them); `Rewrite_AComputedPropertyOperand_CountsItsTemplatesLevels` (a template's CASE, IIF in the SQL Server variant, COALESCE, NULLIF and a `{Token}` reference counted; quoted text, a cycle, siblings and an alias without a level not; rule 7; an alias in the test), `Rewrite_ANullableIntegersToString_CountsAsACase` (a string's `ToString()`; a template's levels inside the COALESCE), the growth test's `NX` family; guard rows for the two-witness form over a column's part (four) and a neighbour | the R16-5 keys: proofs at `25956e0` (rows; the delete accepted), pins against rev 16 | all four |
| N10, N13 (R20-1..R20-7) | `Delete_ReadsACapturedValue_NoMoreOftenThan3110` (the harness, all four providers: round 19's eleven shapes and six casts through `object`, 3.11.0's counts as a bound), `Query_OfANullPredicate_ThrowsArgumentNull` (the harness), `Validate_WithTheOperandSql_AsksEachMemberOnce`'s base-class and two-`Length` rows, `OperandText_WritesEachParameterAsItsValue`'s exact row, the `Classify` rows `orObjCapturedTrue`, `objOrCapturedTrue`, `objOrCapturedTrueEqTrue`, `objSelfOrCapturedTrue`, the SQLite delete's `R20-1` and `R20-4` rows; `delreads` | — | all four (the reads and the null predicate on each) |
| N10, N13 (R21-1..R21-7) | `Classify_ReturnsTheVerdict`'s `objNameContainsEmpty`, `idOrObjNameStartsWithEmpty`, `objNameEndsWithStringEmptyOrId` (an empty search value written as a literal under a cast through `object`), `objNameContainsCapturedEmpty`, `objObjOrCapturedTrue` and `objOrObjCapturedTrue` (nothing captured read under the cast, a second cast included); `Classify_UnderACastThroughObject_ReadsNoCapturedValue` (0 reads); `Classify_SeesAnAliasOfAnOverriddenColumnAsTheColumn`; `Validate_WithTheOperandSql_AsksEachMemberOnce`'s overridden-column row; the SQLite delete's `R21-1` and `R21-1b` rows | — | SQLite (the delete); the rest DB-free |
| N10, N13 (R22-1..R22-4) | `Classify_SeesAnAliasOfAnOverriddenColumnAsTheColumn`'s setter-only override and `Validate_WithTheOperandSql_AsksEachMemberOnce`'s setter-only row (R22-1); `Classify_ReturnsTheVerdict`'s `objNameContainsA`, `objNameContainsOtherEmpty` (R22-3) and `objNameContainsConstEmpty` (R22-4); the long-chain Known issue's numbers, `depth22` (R22-2) | — | DB-free; the chain limits measured on all four |
| N10, N13 (R19-1..R19-5) | `Delete_ReadsACapturedValue_AsOftenAs3110` (eleven shapes, 3.11.0's counts; moved into the harness as `Delete_ReadsACapturedValue_NoMoreOftenThan3110` in rev 21), `Query_OfANullPredicate_ThrowsArgumentNull`, `Validate_WithTheOperandSql_AsksEachMemberOnce`, `Validate_WithTheOperandSql_GivesTheVerdict`, the rows `twinLe`, `twinNotLt`, `twinNotGt` and `parameterlessLambda`, the timing test's column chains, `OperandText_WritesEachParameterAsItsValue`'s quoted rows, `TheGuard_ComparesQuotedTextWithItsLetterCase_InEveryProvider`, `Delete_OfANestedTwin_IsRefused`'s JSON neighbour; `fuzz19t` | — | all four (the guard rows DB-free; the reads on SQLite) |
| N10 (R18-1..R18-8; the twin fuzzer) | `Validate_WithTheOperandSql_RefusesTwoOperandsOfTheSameSql` (a twin flat, in a conditional, nested, under `!`, by `>=`, through a cast through `object`, in an `\|\|`, inside a `bool` comparison, with an alias in the tests, in another letter case, and with the same captured member; neighbours: another test, another column, another captured member, a call that reads no row; an operand SQL that is null or throws), `Validate_WithTheOperandSql_ForARead_AsksNothing`, `Validate_WithTheOperandSql_OfLongPredicates_StaysLinear`, `Validate_WithTheOperandSql_NeverSendsAPartThatReadsOrRunsAnythingElse` (a static member, another parameter, a constructor, an initializer, an array, an invocation, a nested lambda), `OperandText_WritesEachParameterAsItsValue`, `TheGuard_RefusesANestedComputedTwin_WithEveryProvidersOperandSql` (each provider's real visitor: T1–T5, T8, L2, L5, K1, T7; neighbours; a captured value never read), `Delete_OfANestedTwin_IsRefused` (SQLite's real delete: T1–T3, T5, T7, T8, L2, L5, K1, J2, J3; arithmetic throws first; neighbours N1, N2), `PublicMembers_RejectBadArguments`' four new rows; the `Classify` rows `aliasOfAliasSelf`, `dualTemplatesSame`, `nDualOtherNotAlias`, `otherDualOtherNotAlias`, `dualTemplatesDifferPostgreSql`, `dualTemplatesDifferMsSql` (R18-4), `notNAliasNe`, `nAliasEqTrue`, `nameAliasNeNeTrue` (R18-2's examples) | `nestedNAbsUpperEqNAbsLower`, `nestedDocKEqDocK2` (a nested conditional against its twin: refused, self-referencing; rev 18 deleted every row), the neighbour `nestedNAbsUpperEqCodeCase` | all four |
| N10 (R17-1, R17-2, R17-5; the self-comparison fuzzer) | `nestedCondSelfCopy`, `nestedCondAliasCopy` (a nested conditional against its copy and its alias's: refused, self-referencing; rev 17 deleted every row), `nBothEqNBothX` (a template holding `IS NULL AND` against its alias); the verdict rows `condSelfCopy`, `condAliasCopy`, `nestedCondAliasCopy`, `notCondAliasCopyNe`, `condAliasCopyOrId2`, `arithmeticSelf`, `nAliasSelf`, `boolAliasSelf`, `templateTwinSelf`, `templateAliasSelf`, `objBoolSelf`, `objAliasSelf`, `condSelfGeNullable`, `condSelfGeNotNull`, `notCondSelfLtNullable`, and the neighbours `nNotAnAlias`, `condOtherBranch`, `condOtherTest`; guard text rows (an operand holding `IS NULL AND`, three; a quoted parenthesis, two; an embedded form); `ValidateTranslation_OfLongClauses_StaysLinear`; `SqliteComputedPartSelfReferenceTests`' `!(x.NameLen != x.Name.Length)` (R17-5); `Rewrite_AComputedPropertyOperand_CountsItsTemplatesLevels`' two-level rows (R17-4, R17-6) | the moved verdicts: `activeEqActiveXNeFalse`, `nToStringEqNXToStringOrNNull`, `falseNeObjNameLengthSelfLe` now refused as always true, `activeEqActiveXOrFlagNull` refused (accepted at 3.11.0) | all four |
| N1, N3, N10, N13, N17 (R13-1..R13-4) | `nestedCondNeFlat9` (a nested conditional against a 9-deep conditional: 9 CASEs; rev 13: 11, Msg 125 on SQL Server), `notNestedCondEqV`, `notNestedCondEqM`, `notNestedBoolCondEqFlag` (the distribution under negation, steps 5 and 6: C#'s rows; `25956e0`: rows, and PostgreSQL's 42804 for the last), `idGt2EqLiftedIdGt2` (refused as a self-reference; 3.11.0 accepted it, every row on SQLite), `nestedCondUpperANeA` (the leaf `"A" != "a"` is C#'s, true: AC-N11 (a); `25956e0`: rows, `3` on SQL Server and MySQL); `Rewrite_ANestedConditionalAgainstAConditional_TakesEFsForm` (nine shapes, every call site: at most the source's CASE nesting + 1, EF's forms, C#'s value), `Distribution_ReadsTheOtherOperandOnce_…` (EF's form sends a conditional operand once); the guard row `boolOtherUnderUserCast` (a user-defined cast isn't seen through) | `nestedCondNeFlat9`: a pin at `25956e0` (its rows agree there), a proof against rev 13; the R13-4 keys, `idGt2EqLiftedIdGt2`, `nestedCondUpperANeA` and the guard row: pins against rev 13, the killers of D19–D21; `Distribution_…`'s mirrored case kills D22 | all four |
| N1, N3, N4, N10 (R12-1, R12-2, R12-5) | `nestedCondTestNe8`, `nestedCondTestNotGt8`: conditionals compared within conditionals' tests, eight levels down, eight CASEs (rev 12: sixteen, Msg 125 on SQL Server; `25956e0`: wrong rows where n or m is NULL); `nestedCondBothNullNe` (rev 12: the outer conditional three times; `25956e0`: rows); `Rewrite_NestedConditionalComparisons_KeepTheirCaseNesting` (three forms nine levels down in tests, and value branches: nine CASEs), `Rewrite_ADistributedBranchAsDeepAsItsTest_AddsOneCaseLevel` (a branch as deep as its test, eight levels: one CASE more, once), `Rewrite_ACompensationOverAFlatConditional_TakesEFsForm`, `Rewrite_ACompensationOverANestedConditional_IsDistributed`, `Distribution_ReadsTheOtherOperandOnce_AndDoesntRewriteALeafAgain`, ten `Rewrite_KeepsCSharpsValue` rows; R12-1: `nIsNullEqCastNIsNull`, `idGtCodeEqLongCasts`, `nameIsNullEqToStringIsNull` (refused as self-references; rev 12 accepted and deleted every row) and five `Classify_ReturnsTheVerdict` rows, two neighbours acceptable | `nestedCondBothNullEq`: a pin against rev 12, a proof against `25956e0` (rows); `idGtCodeEqLongCasts` on SQLite at `25956e0` (3.11.0's rows and refusal); `activeEqActiveXNeFalse`: §4.3's twin row (accepted at `25956e0`, every row) | all four |
| N1, N2, N4, N13 (R11-1, R11-2, R11-4) | `parentCodeRoundTripNe5`, `parentKeyRoundTripNe1`, `notParentCodeRoundTripGt3` (`NsRemote`), `nPlusOneIntRoundTripNe6`, `nPlusOneIntObjectNe6`, `childSumDRoundTripNe99` (`NsComputed`): a round trip through a nullable type keeps the NULL rows (rev 11 and 3.11.0 lost them); `castNotCondCapturedBranch` (C# {4,6}), `idGt4AndCastNotCondCapturedBranch` (rule 9 through a `!`; dialect-rejected on SQL Server); `flagEqCondWitnessFolds` (a re-decided `==` with one side that can be null); `Classify_ACastKeepsTheOperandsKind`, `Rewrite_ANullTest_UnderAnOddNumberOfNots_TestsTheOperandUnderThem`; `ReadOnce_CountingGetter`'s R11-2 rows (the re-decided `==`, a two-sided Kept, a lazy `Contains`: one read, one enumeration); `Fold_AsksASmallOperandFirst_UpToItsBound` (32 nodes against 33) | `idGt4AndCastNotCondCapturedBranch` (all four) and `flagEqCondWitnessFolds` (SQLite, SQL Server, MySQL; PostgreSQL 42883) pass at `25956e0`: pins that kill W11-2 and W11-5 | all four |
| N14 (R9-1..R9-4) | `notNotCondFoldedFlagNeTrue`, `notActiveNeCastCondFolded` (a witness under `!`s or a cast: rev 9 threw; 3.11.0's rows on SQLite, SQL Server and MySQL, 42883 on PostgreSQL); `castCondActiveFalse` (C# {3,5}), `castCondNNe5Active` ({1,3,4,5,6}), `castNotCondActiveFalse` ({1,2,4,6}): dialect-rejected on SQL Server (§3); `castNotLiftedCondHybrid` (every row: refused as always true, §4.3; rev 9's rows were wrong); `objLiftedNotNGt3EqTrue` ({3,4,6}); `Rewrite_ACastBackToBool_OverAConditionalOfLifts_IsTheConditional`, `Rewrite_ACastThroughObject_IsTheCastWithoutIt`, `Rewrite_AWitnessUnderNotsOrCasts_IsKept`, ten `Rewrite_KeepsCSharpsValue` rows; `ReadOnce_CountingGetter`'s R9-3 rows (the three shapes read 3.11.0's counts, the first two dialect-rejected on SQL Server, where their reads still count; `c.Five` isn't read after a false `&&` operand); `Fold_StopsAtAnOperandThatDecides` | `nullEqNotObjNotFlag` ({3,4}): 3.11.0's rows, a pin that rev 9 failed; `castLiftedCondKnownBranch`: an AC-N11 (a) SQL pin (`ELSE NOT 1=0`; the rows agree before and after; dialect-rejected on SQL Server); `ReadOnce`'s cast-through-`object` row (nothing under the cast is read where a captured operand decides: 3.11.0's count, Z27's killer) | all four |
| N14 (R8-1..R8-4) | `castNotLiftedNGt3` (C# {3,4,6}), `castNotLiftedMGtNoneAndOnlyNull` (every row: refused as always true), `castLiftedNGt3EqActive` (a cast as a side); `Rewrite_ACastBackToBool_IsTheFormItself`; `Reads_NothingTheFoldSkips` and a ReadOnce row (`c.A` under an `\|\|` that is true: not read); `ANotInAValuePosition_OverACastOverAConditional_KeepsItsForm` (the visitor's own: a cast between the `!` and a folded conditional, Z6/Z6s's killer); `!(x.Flag == !x.Flag)` (`notFlagEqNotFlag`, accepted, §4.3) | — | all four |
| N14 (R6-3) | `condFoldedEqFlag` (rev 6: a `NullReferenceException`); `Rewrite_ASideTheFoldKnows_IsAValue`; two values in `Rewrite_ABoolComparison_WithASideThatIsAValue` | — | all four |
| N16 (R6-4, R6-6) | `nboolCondPredicateBranchNeTrue`, `condPredicateBranchEqActive` (red at rev 6 by rows or the `CASE` it lacked; on SQL Server, a predicate as a `CASE` result was a syntax error); `Rewrite_APredicateBranch_IsItsTwoValuedTruth` | — | all four |
| N14, values (R5-1) | — | Pins at `25956e0` on all four, red at rev 5 on SQL Server (its syntax and "non-boolean type" errors): `boolCondEqTrue`, `colEqBoolCond`, `notActiveEqTrue`, `notActiveEqOther`, `nboolCondLiteralBranchEqTrue` (two NOT NULL `bit` columns, `active` and `other`, join the seed); `Rewrite_ABoolValue_StaysAValue`, `AValuePositionConditional_KeepsItsBranchesAsValues` | all four |
| N14 | `nameContainsAEqFalse`, `notNameContainsAEqTrue`, `nameContainsANeTrue`, `nameContainsAEqInclude` (`include` false → `!p`), `nameContainsNullEqFalse`, `nEq5EqMEq5`, `nEq5NeMEq5`, `flagEqNGt5`, `flagNeNGt5` (the expansion; on SQL Server red by its syntax error, on PostgreSQL by 42601, elsewhere by rows where an operand is null), `nameContainsANeNullBool` (every row; refused), `nEq5EqNEq5` (every row; refused as a self-reference; R4-4) | `nameContainsAEqTrue`: a plain pin on SQLite, PostgreSQL, MySQL (no SQL assertion); a SQL Server proof. `nameContainsAEqNullBool`: a pin on SQLite and MySQL (3.11.0 selected nothing), a proof on SQL Server and PostgreSQL. `flagEqNGt5` on SQLite: rows-pin/SQL-proof (SQLite reads `flag = n > @p` as `flag = (n > @p)` and no seed row has a null `n` with a false `flag`; the WHERE must carry the predicate's `CASE`, R11-5) | all four |
| core | `NullSemanticsTests` (DB-free, C# 7.3; 53 tests, green on net8.0 and net48), and `WhereVisitorDirectTests` (12, every provider's visitor; R6-1, R6-8, R6-9). The core tests include: `Rewrite_KeepsCSharpsValue` (the rewrite is an equivalence in C#), `Rewrite_ReturnsTheOriginalNode_WhereNothingIsCompensated`, `Rewrite_PushesNotDown_OnlyWhereAnOperandChanges`, the compensation shapes, `Classify_ByOperandKind` (every §4.1 row), `CanBeNull_ByPropertyKind`, `Reads_AValueOnce_AndOnlyWhenTheRowOperandIsAccepted`, `Rewrite_FoldedPartsStayConstants`, `Rewrite_LeavesBitwiseAlone`, `Substitution_KeepsTheConvertChain` (short→int?, int→long?, int→decimal?, `Color`→`int?`, `DateTime`→`DateTimeOffset?`: the constant has the member's type and the `Convert` chain is unchanged), `NullValue_ReplacesTheWholeOperand`, `Expansion_SubstitutesBeforeDuplicating`, argument-null checks | — | SqlServer.Tests, linked into `Funcular.Data.Orm.SqlServer.Tests.NetFramework` |
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
| M-N3b | A negated call counted as unchanged (rev 4's rule 8; R4-1) | `notIdAndNameContainsA`, `notIdOrIdsContainsN`, `notIdAndNameContainsNull`, `Rewrite_PushesNotDown_OnlyWhereAnOperandChanges` |
| M-N4 | No negated-relational compensation | `notNGtV`, `notNameLengthGt1`, `allNGt0` |
| M-N4b | No recursion into a conditional | `condTestNe`, `boolCondBranches` (not SQL Server) |
| M-N4c | `VisitConditional` back to `Visit` for the test | `condCapturedTest` (its literal text, all four) |
| M-N4d | No rewrite of a non-`bool` conditional's test (rev 3's gap, R3-1) | `condTestNe` |
| M-N4e | No descent into a row operand whose comparison isn't compensated (R4-2) | `condTestNeGt0`, `condTestNeEqV`, `nestedCondTestNe`, `Rewrite_ConditionalTests_AreRewritten_WhereverTheConditionalIs` |
| M-N5 | Drop null items from `IN` | `idsWithNullContainsN`, `onlyNullContainsN` |
| M-N5b | Drop the parentheses | `id3AndIdsWithNullContainsN`, `id3AndNotIdsContainsN` |
| M-N6 | Negated LIKE without `OR col IS NULL` | N7 proofs |
| M-N7 | A negated null search selects no row | `notNameContainsNull`, `allNameContainsNull` |
| M-N8 | `All` wraps the SQL in `NOT (…)` | `allNGt0`, `allNIn517` |
| M-N9 | Compensate operands that can't be null | N11 pins |
| M-N10 | `CanBeNull` false for reference types | `nameNeA`, `notNamesContainsName` |
| M-N11 | Classify by CLR type only | `parentCodeNe5`, `parentKeyNe1`, `nPlusOneIntNe6`, `childSumDNe99` |
| M-N12 | Both-null `==` without `(a IS NULL AND b IS NULL)` | `nEqM` |
| M-N13 | Substitute at the operand (drop the `Convert`), or read again in the visitor | dropping the `Convert`: `Substitution_KeepsTheConvertChain` (the constant's type) and `colorNeCapturedColor`'s Convert twin (`Green` vs `1`, on SQLite, SQL Server and MySQL); the log prints a value, not its type, so the numeric twins can't tell (R4-6). Reading again: `ReadOnce_CountingGetter`, `Reads_AValueOnce_…` |
| M-N14b | No two-valued comparison (3.11.0's `a = b = c = d`; re-anchored in rev 12) | `nEq5EqMEq5`, `flagEqNGt5` |
| M-N14 | `p == false` left as is | `nameContainsAEqFalse` |
| M-N15 | Repeated witness not de-duplicated | `nameLengthSelf` (text), `Rewrite_ARepeatedWitness_IsTestedOnce` |
| M-N16 | The verdict's `p == p` self-reference removed (R4-4; re-anchored in rev 18 on the any-type rule) | `nEq5EqNEq5` (EveryPath and `VerdictChanges_AsListed`), the verdict rows |
| M-N17 | An integer's `ToString()` back to the bare `CAST` over a remote value (R4-10) | `parentCodeToStringEqEmpty` |
| M-N4f | `Kept(negated, rebuilt)` drops its `Not` (R5-5) | `notCondTestNeEq1` |
| M-N4g | A negated relational over two witnesses drops the second `IS NULL` (R5-5) | `notNGtM`, `Rewrite_NegatedRelationalOfTwoColumns_…` |
| M-N19 | A `bool` value counted as a predicate again (R5-1 a; rev 7: `!` of a bool member) | `Rewrite_ABoolValue_StaysAValue`, `Rewrite_NotOfAColumn_…`; the R5-1 keys on SQL Server |
| M-N20 | A value-position conditional's branches sent as literals (R5-1 b) | `boolCondEqTrue`, `nboolCondLiteralBranchEqTrue` (SQLite's text; SQL Server's error), `AValuePositionConditional_…` |
| M-N21 | The literal-null return discards the rewritten operand (R5-2) | `ReadOnce_CountingGetter`, `Reads_ALiteralNullAgainstAConditional_Once` |
| M-N22 | The fold reads each side, then the whole (R5-3; re-anchored in rev 21) | `ReadOnce_CountingGetter`, `Reads_AFoldedComparisonOfCapturedBools_Once` |
| M-N23 | A value branch not substituted (R5-4) | `condNullBranchEq5`, `condNullBranchGt3` |
| M-N24 | A lazy collection not materialized (R5-8) | `ALazyCollection_InARepeatedOperand_IsEnumeratedOnce`, `ALazyQueryable_…`, `ALazyCollection_OfAConcreteType_…` (rev 12: repointed at an operand the SQL repeats; on AC-N14's predicate, now sent once, M-N24, M-N38, M-N39, M-N45 and Y1 survived) |
| M-N18 | A literal null rebuilt instead of kept | `Rewrite_ReturnsTheOriginalNode_…` (`x.N == null`, `!(x.N == null)`) |
| M-N25 | `!` over a row operand not moved to the comparison (R6-2, R6-4) | `notFlagNeTrue`, `notFlagEqNull`, `flagEqNotFlag` (their SQL shape: no NOT on a value), `Rewrite_NotOfAColumn_…` |
| M-N26 | `ValueEquality` ignores the `!` it was handed (R6-4) | `notFlagNeTrue`, `notFlagEqNull` (rows: the complement) |
| M-N27 | `RowEquality`'s comparison not flipped for one `!` (R6-4) | `flagEqNotFlag`, `Rewrite_NotOfAColumn_…` |
| M-N28 | AC-N14's row operand compared without its `!` (R6-2; re-anchored in rev 12) | `nEq5EqNotActive`, `notActiveEqNNe5`, `Rewrite_NotOfAColumn_…` |
| M-N29 | A `bool` side that reads no row isn't a value unless `Classify` says so (R6-2) | `Rewrite_ASideThatReadsNoRow_IsAValue` (a conditional of captured values holding null). The rows are the same, the SQL longer, so no harness key fails |
| M-N30 | A side whose truth the fold knows isn't a value (R6-3) | `condFoldedEqFlag` (the `NullReferenceException`), `Rewrite_ASideTheFoldKnows_IsAValue` |
| M-N31 | `!` of an operand not accepted (R6-2) | `nEq5EqCondNotOther`, `ReadOnce_CountingGetter`, `Classify_ByOperandKind` |
| M-N32 | A conditional's predicate branch not sent as its two-valued truth (R6-4, R6-6) | `nboolCondPredicateBranchNeTrue`, `condPredicateBranchEqActive`, `Rewrite_APredicateBranch_…` |
| M-N33, M-N33s | A value-position `!` visits its operand in a truth position (R6-1), SQLite's and SQL Server's visitor | `notBoolCondEqTrue`, `notCondFoldedTestNeOther`; `ANotInAValuePosition_…` (both) |
| M-N34 | A value-position conditional under `!` folded to a literal (the self-check) | `notCondFoldedTestNeOther` |
| M-N35 | A branch whose truth the fold knows isn't replaced by it (the self-check) | `ReadOnce_CountingGetter`, `Reads_NothingUnderABranchTheFoldKnows` |
| M-N36 | The fold's reads not reused (R6-5) | `ReadOnce_CountingGetter`, `Reads_ANullBoolTheFoldRead_Once`, `Rewrite_ABoolComparison_WithASideThatIsAValue` |
| M-N37 | (withdrawn in rev 12: AC-N14 no longer repeats the row operand; rule 6 substitutes where it repeats one, M-N13's and Z37's killers) | |
| M-N38 | A lazy argument not materialized against the parameter's type (R6-8 c) | `ALazyCollection_OfAConcreteType_…` |
| M-N39 | An `IQueryable<T>` variable not materialized (R6-7 c) | `ALazyQueryable_…` |
| M-N40 | `!a == b`'s non-null guards dropped (the self-check) | `Rewrite_KeepsCSharpsValue` |
| M-N41 | `Classify` calls a `bool` that reads no row a value (the self-check) | `Rewrite_KeepsCSharpsValue` (the fuzz's four shapes), `Classify_ByOperandKind` |
| M-N42 | `!` of a predicate isn't a predicate (R6-6) | `notNEq5EqActive` |
| M-N43 | `SameValue`'s call case never the same (R6-8 d; rev 13 renamed it from `SameTree`) | `containsSelfEq`, `staticContainsSelfEq` |
| M-N44 | `SameValue`'s conditional case never the same (R6-8 d) | `condPredicateSelfEq` |
| M-N45 | A throwing lazy collection not left for the translator (R6-8 c) | `ALazyCollection_OfAConcreteType_…` (the translator's `NotSupportedException` and the caller's exception) |
| M-N46 | Two values not compared as C# does (R6-8 b) | `Rewrite_ABoolComparison_WithASideThatIsAValue`, `Rewrite_KeepsCSharpsValue` |
| M-N47 | A conditional that reads no row not read through its test (R6-2) | `Rewrite_ASideThatReadsNoRow_IsAValue` |
| M-N48 | `!` of a value not read through its operand (R6-2) | `nEq5EqNotCapturedFlag`, `flagNeNotCapturedFlag` |
| M-N49 | `!(bool?)p` not normalized to `(bool?)!p` (R7-1; rev 9 moved it into the `LiftNormalizer`) | `flagNeNotLiftedNGt3`, `notLiftedNGt3EqNull`, `notLiftedNameContainsANeFalse`, `condNotLiftedBranchNeTrue`, `Rewrite_NotOfALiftedPredicate_…` |
| M-N51 | A cast back to `bool` over a lift not dropped (R8-1) | `castNotLiftedNGt3`, `castNotLiftedMGtNoneAndOnlyNull`, `castLiftedNGt3EqActive`, `Rewrite_ACastBackToBool_IsTheFormItself` |
| M-N52 | The `Substituter` asks only the memo, not the fold (R8-3) | `ReadOnce_CountingGetter` (`c.A`), `Reads_NothingTheFoldSkips` |
| M-N53 | The `Substituter` may replace its root (R6-3, R8-3) | `Rewrite_KeepsCSharpsValue` (a row operand the fold knows once rewritten: the `NullReferenceException`) |
| Z6, Z6s | `EndsAtConditional` strips only `!`s (R8-4), SQLite's and SQL Server's visitor | `ANotInAValuePosition_OverACastOverAConditional_KeepsItsForm` (every provider's visitor) |
| Z20 | A row operand's substitution keeps only its root (R9-1; re-anchored in rev 12 on the `Substituter`'s loop) | `Rewrite_AWitnessUnderNotsOrCasts_IsKept` (the two operands that can both be null). Against a value, rev 12's form needs no witness, so the harness keys keep C#'s rows under it |
| Z21 | The chain kept stops one step below the root (R9-1; re-anchored in rev 12) | `Rewrite_AWitnessUnderNotsOrCasts_IsKept`, as Z20 |
| Z22 | A `bool?` conditional of lifts not lifted (R9-2) | `castCondActiveFalse`, `castCondNNe5Active`, `castNotCondActiveFalse`, `castNotLiftedCondHybrid`, `Rewrite_ACastBackToBool_OverAConditionalOfLifts_…` |
| Z23 | A constant branch that isn't null not unlifted (R9-2) | `Rewrite_ACastBackToBool_OverAConditionalOfLifts_…` (a hand-built `bool?` constant) |
| Z24 | A cast through `object` not taken out (R9-2) | `objLiftedNotNGt3EqTrue`, `Rewrite_ACastThroughObject_IsTheCastWithoutIt` |
| Z25 | A cast through `object` to the boxed type kept as a cast (R9-2) | `nullEqNotObjNotFlag` (`NOT flag IS NULL`, the complement) |
| Z26 | The fold doesn't stop at an operand that decides (R9-3) | `Fold_StopsAtAnOperandThatDecides`, `ReadOnce_CountingGetter` (`c.Five`) |
| Z27 | The fold asks the left operand first, always (R9-3) | `Fold_StopsAtAnOperandThatDecides`, `ReadOnce_CountingGetter` (under a cast through `object`) |
| Z28 | The fold asks a captured right operand first even after a captured left (R9-3) | `Fold_StopsAtAnOperandThatDecides` (`false && c.Flag`) |
| Z29 | The deciding truth swapped (R9-3) | `Fold_StopsAtAnOperandThatDecides`, the harness |
| Z30 | A cast back to `bool` over a conditional the cast pass leaves stays a leaf (R10-1) | the four `castCond…CapturedBranch…` keys, `Rewrite_ACastBackToBool_OverAConditionalItLeaves_IsARowOperand` |
| Z31 | (withdrawn in rev 12 with the cast assertion, R11-1) | |
| Z32 | Step 5 tests the witness taken before the substitution (R10-2) | `computedCondWitnessNeTrue`, `Rewrite_AWitnessWhoseNullableBranchFolds_…`, `Rewrite_KeepsCSharpsValue` |
| Z33 | Step 6 keeps the kinds taken before the substitution (R10-2) | `Rewrite_KeepsCSharpsValue` (two row operands, one whose nullable branch folds) |
| Z34 | The operand asked first is any that reads no row, whatever its size (R10-4) | `Fold_OfALongChainOfCapturedOperands_StaysLinear` |
| Z35 | No operand is small (R10-4) | `Fold_StopsAtAnOperandThatDecides`, `ReadOnce_CountingGetter` (under a cast through `object`) |
| Z36 | A null test under an odd number of `!`s not taken under them (R11-1) | `nullEqLiftedCastNotCond`, `Rewrite_ANullTest_UnderAnOddNumberOfNots_…` |
| Z37 | The re-decided Kept sends the operands from before the substitution (R11-2) | `ReadOnce_CountingGetter` (the R11-2 rows) |
| W11-2 | Rule 9 only for a conditional directly under the cast (round 11's survivor) | `castNotCondCapturedBranch`, `idGt4AndCastNotCondCapturedBranch` |
| W11-5 | The re-decision ignores `==` with one side that can be null (round 11's survivor) | `flagEqCondWitnessFolds` |
| W11-6 | The small-operand bound at 33 nodes (round 11's survivor) | `Fold_AsksASmallOperandFirst_UpToItsBound` |
| Z38–Z46, Z49, Z53–Z55 | (withdrawn in rev 13 with R11-5's single CASE, R12-5) | |
| Z48 | Two predicates' values compared with the operator flipped (AC-N14) | `nEq5EqMEq5`, `nestedPredicateEqPredicate`, the core tests |
| Z50 | A predicate's two-valued branches swapped (AC-N14) | `flagEqNGt5`, `nEq5EqNotActive`, the core tests |
| Z51 | A column against a predicate's value with the operator flipped (AC-N14) | `flagEqNGt5`, `nEq5EqNotActive`, the core tests |
| D1 | Step 5: a nested conditional takes EF's form, not distributed (R12-2) | `nestedCondNeTrue`, `Rewrite_NestedComparisons_GrowLinearly`, `Rewrite_ADistributedBranchAsDeepAsItsTest_…`, `Rewrite_ACompensationOverANestedConditional_…` |
| D2 | Step 6: a nested conditional takes EF's forms, not distributed (re-anchored in rev 15) | `nestedCondNeOther`, `nestedNotCondNeOther`, `nestedCondBothNullNe`, `nestedCondBothNullEq`, `Rewrite_NestedComparisons_GrowLinearly`, `Rewrite_ACompensationOverANestedConditional_…` |
| D3 | Rule 7: a nested conditional takes EF's form, not distributed | `Rewrite_ACompensationOverANestedConditional_…`, `Distribution_ReadsTheOtherOperandOnce_…` |
| D4 | Rule 7: the operand that can't be null not substituted before it is repeated (AC-N13; withdrawn as equivalent in rev 14, live again in rev 15) | `Distribution_ReadsTheOtherOperandOnce_…` |
| D5 | Two nested sides: the left first, as rev 15 (withdrawn in rev 14 with `IsLarger`, back in rev 16: R15-1) | `Rewrite_NestedConditionalsAgainstConditionals_GrowLinearly` (`(S(j) != L) ? Code : 5`), `nestedCondChainNestedLeft7` |
| D6 | A leaf's flat conditional rewritten again | `Distribution_…_AndDoesntRewriteALeafAgain` |
| D7 | Every conditional flat: EF's forms everywhere | five nested keys (`nestedCondNeTrue`, `nestedCondNeOther`, `nestedNotCondNeOther`, `nestedCondBothNullNe`, …), four core tests (`Rewrite_NestedComparisons_GrowLinearly`, `Rewrite_ADistributedBranchAsDeepAsItsTest_…`, …) |
| D8 | Every conditional nested: distributed everywhere | `nestedCondTestNe8`, `nestedCondTestNotGt8` (the CASE nesting), eight core tests (`Expansion_SubstitutesBeforeDuplicating`, `Rewrite_AWitnessUnderNotsOrCasts_IsKept`, `Rewrite_NestedConditionalComparisons_KeepTheirCaseNesting`, …) |
| D9 | A nested value conditional in a branch makes the conditional nested | `Rewrite_NestedConditionalComparisons_…` (value branches), `Rewrite_ADistributedBranchAsDeepAsItsTest_…`, `Rewrite_ACompensationOverAFlatConditional_…` |
| D10 | A leaf sent as its predicate, not its two-valued value | `Rewrite_ACompensationOverANestedConditional_IsDistributed`, `Rewrite_ADistributedBranchAsDeepAsItsTest_AddsOneCaseLevel` |
| D11 | The distributed value compared with false | the nested keys' rows, `Rewrite_KeepsCSharpsValue` |
| D12 | A distributed conditional's branches swapped | the nested keys' rows, `Rewrite_KeepsCSharpsValue` |
| D13, D14, D15 | The guard's `bool` rule blind to casts, to a string's `ToString()`, or to a widened constant (R12-1) | the five `boolSelfUnder…`/`boolSelfConstantWidened` guard rows |
| D16 | The bound never refuses a distribution (R14-1, R13-1; rev 14's D16–D18 went with `Distributes`) | `nestedCondNeFlat9`, `Rewrite_ANestedConditionalAgainstAConditional_…` |
| D17 | The bound is the comparison's CASE nesting, not + 1 (R14-1) | `Rewrite_ACompensationOverANestedConditional_IsDistributed`'s R14-1 shapes (a nested conditional against a flat one, steps 6 and rule 7: one CASE past written; added after the first full run left D17 alive, `mutations-r15-d17.md`) |
| D18 | The bound is the comparison's CASE nesting + 2 (R14-1) | `nestedCondNeFlat9`, `Rewrite_ANestedConditionalAgainstAConditional_…` (`nested != flat(3)`) |
| D19 | Step 5 distributes under its own negation again (R13-4, round 13's V13-6) | `notNestedCondEqV` |
| D20 | Step 6 distributes without its negation (R13-4, V13-4) | `notNestedCondEqM`, `notNestedBoolCondEqFlag` |
| D21 | The guard's `bool` rule sees through a user-defined cast (R13-4, V13-8) | `boolOtherUnderUserCast` |
| D22 | The right operand's flat conditional in a leaf rewritten again (the coverage pass) | `Distribution_…_AndDoesntRewriteALeafAgain` (`5 != nested`) |
| D23 | The bound without EF's form's own nesting (R14-1; AC-N16's predicate branches) | `nestedCondNeOther`, `nestedNotCondNeOther`, `Rewrite_NestedComparisons_GrowLinearly` |
| D24 | Step 6: a nested witness on the right not distributed (R14-2, round 14's V14-1) | `nestedCondChainNNRight8`, `Rewrite_NestedConditionalsAgainstConditionals_GrowLinearly` |
| D25 | Rule 7: a nested witness on the right not distributed (R14-2, V14-2) | `Rewrite_NestedConditionalsAgainstConditionals_GrowLinearly` (the rule-7 families on the right) |
| D26 | The CASE-nesting counter never steps back out (R14-1) | six nested keys (`nestedCondNeOther`, `nestedNotCondNeOther`, `nestedCondBothNullNe`, …), three core tests |
| D27 | The guard's text check misses EF's form of a self-equality (R14-3; re-anchored in rev 16) | the guard text rows, `objNameLengthSelfEq`, `nEqNX`, `notNNeNX`, the R15-2 keys |
| D28 | The guard's text check misses the one-witness form (R14-3; re-anchored in rev 16, and on the parser in rev 18) | the two one-witness guard text rows, `objNameLengthSelfEq` |
| D29 | The CASE-nesting counter counts a lambda's conditionals (the coverage pass; re-anchored in revs 16 and 17) | `Rewrite_ACompensationOverANestedConditional_IsDistributed`'s lambda shape |
| D30 | Two nested sides: the smaller first (R15-1) | `Rewrite_NestedConditionalsAgainstConditionals_GrowLinearly` (the R15-1 families), `nestedCondChainNestedLeft7` |
| D32 | (withdrawn in rev 17 with `ReadsRemote`: R16-2) | |
| D34 | The text check refuses EF's form whatever its witnesses (R15-4; re-anchored in revs 17 and 18) | the guard rows `(t.active = t.active OR t.flag IS NULL)`, `activeEqActiveXOrFlagNull` |
| D35 | The witness check finds a token inside another (`t.n` in `t.name`; R15-4) | the guard row `(t.name = t.name OR t.n IS NULL)` |
| D36 | The CASE-nesting counter skips a nullable integer's `ToString()` `COALESCE` (the self-check; re-anchored in rev 17) | `Rewrite_ANullableIntegersToString_CountsAsACase`, `nestedStrNeFlat8NToString` |
| D37 | The counter counts every integer's `ToString()`, NULL or not (the self-check) | `Rewrite_ANullableIntegersToString_CountsAsACase` (`x.Id.ToString()`, distributed) |
| D38 | (withdrawn in rev 18 with the pattern; the parser's mutants are D28, D34, D39, D40, D52–D54) The two-witness form requires the operand itself under both `IS NULL`s again (R15-2; re-anchored in rev 17) | the R15-2 and R16-1 guard rows, `nameLengthEqNameXLength`, `nameXLengthEqNameLength`, `dYearEqDXYear` |
| D39 | The second witness not checked (R16-1; re-anchored in rev 18) | the guard row with another column's `IS NULL` beside the computed part's |
| D40 | The second witness must be the first again, as rev 16 (R16-1; re-anchored in rev 18) | the R16-1 guard rows, `TheGuard_RefusesAComputedPartAgainstTheColumnsPart_…`, `SqliteComputedPartSelfReferenceTests` |
| D41 | A template's levels not counted (R16-2; re-anchored in rev 18) | `Rewrite_AComputedPropertyOperand_CountsItsTemplatesLevels`, `nestedCondNeFlat8CodeCase` |
| D42 | A `{Token}` naming another template not followed (R16-2; re-anchored in rev 18) | `Rewrite_AComputedPropertyOperand_CountsItsTemplatesLevels` (`NCaseRef`) |
| D43 | Only the shared template read, not the providers' variants (R16-2) | `Rewrite_AComputedPropertyOperand_CountsItsTemplatesLevels` (`NIif`) |
| D44 | Quoted strings in a template counted (R16-2; re-anchored in rev 18) | `Rewrite_AComputedPropertyOperand_CountsItsTemplatesLevels` (`NQuoted`) |
| D45 | A counted function's closing parenthesis keeps its level (R16-2) | `Rewrite_AComputedPropertyOperand_CountsItsTemplatesLevels` (`NTwoCoalesces`, seven deep) |
| D46 | END keeps its CASE's level (R16-2) | `Rewrite_AComputedPropertyOperand_CountsItsTemplatesLevels` (`NTwoCases`, seven deep) |
| D47 | A template's levels counted beside its `ToString()`'s COALESCE, not inside it (the self-check) | `Rewrite_ANullableIntegersToString_CountsAsACase` (`NCase.ToString()`, seven deep) |
| D48 | The verdict's self-comparison for bools only again (R17-2) | `condSelfCopy`, `nestedCondSelfCopy`, `arithmeticSelf`, … |
| D49 | An `[SqlExpression]` alias not seen as its column (R17-2) | `nAliasSelf`, `condAliasCopy`, `nestedCondAliasCopy`, `nBothEqNBothX` |
| D50 | Two templates with the same text not the same value (R17-1, R17-2) | `templateTwinSelf` |
| D51 | The verdict stops at a cast through `object` (`fuzz18s`) | `objBoolSelf`, `objAliasSelf` |
| D52 | The parser tries only the first split of two witnesses, as rev 17's pattern (R17-1) | the guard rows with an operand holding `IS NULL AND` |
| D53 | The parser reads quoted text as SQL (R17-1) | the guard row with a quoted parenthesis in the operand |
| D54 | The parser tries every split, however long (quadratic) | `ValidateTranslation_OfLongClauses_StaysLinear` |
| D55 | The template count reads quoted identifiers and comments as SQL (R17-4) | `Rewrite_AComputedPropertyOperand_CountsItsTemplatesLevels` (`Case2Bracket`, …) |
| D56 | Template tokens resolved against the declaring type, as rev 17 (R17-4) | the same test (`BaseRef`) |
| D57 | A template named twice counted once (R17-6) | the same test (`NTwice`) |
| D58 | No cycle guard in the template count (R17-6) | the same test (`CycleA`: a stack overflow, recorded as a kill) |
| D59 | The guard doesn't ask the provider's SQL (R18-1) | `Validate_WithTheOperandSql_RefusesTwoOperandsOfTheSameSql`, `TheGuard_RefusesANestedComputedTwin_WithEveryProvidersOperandSql`, `Delete_OfANestedTwin_IsRefused`, `nestedNAbsUpperEqNAbsLower`, `nestedDocKEqDocK2` |
| D60 | The provider's SQL compared with letter case | the `lower` row, `L2`, `L5`, `nestedNAbsUpperEqNAbsLower` |
| D61 | `OperandText` drops the parameters' values | `OperandText_WritesEachParameterAsItsValue`, the direct test's neighbour with another constant |
| D62 | `OperandText` replaces the shortest name first | `OperandText_…` (`@p__linq__10`) |
| D63 | The row-only check lets a captured value through (AC-N13) | `twinCapturedOtherTest` (its whole operand asked) |
| D64 | A throw from the provider's SQL escapes | the `throws` row |
| D65 | The provider's SQL not asked inside a conditional's parts | `twinWithAliasInTest` |
| D66 | The provider's SQL asked for parts of any size | `Validate_WithTheOperandSql_OfLongPredicates_StaysLinear` |
| D67 | `OperandText` ignores the value's type | `OperandText_…` (`2L`) |
| D68 | `OperandText` writes the value as plain text | `OperandText_…` (`'a'` against `'A'`) |
| D69 | A read asks the provider's SQL too | `Validate_WithTheOperandSql_ForARead_AsksNothing` |
| D70 | A call that reads no row is sent for its SQL (AC-N13) | `twinRowFreeCall` |
| D71 | The unifier sees only the predicate's top comparison | `twinOrId2`, `T7` |
| D72 | No part's SQL asked | every twin row |
| D73s, D73m, D73p, D73y | SQLite's, SQL Server's, PostgreSQL's or MySQL's WHERE translation doesn't pass its operand SQL | that provider's `nestedNAbsUpperEqNAbsLower`, `nestedDocKEqDocK2` (and SQLite's delete test) |
| D74 | An alias's variants needn't name one token (round 18's X1, R18-4) | `nDualOtherNotAlias`, `otherDualOtherNotAlias` |
| D75 | Two templates compared by their shared text only (X2) | `dualTemplatesDifferPostgreSql`, `dualTemplatesDifferMsSql` |
| D76 | An alias followed one hop only (X9) | `aliasOfAliasSelf` |
| D81, D82 | The 64-node bound on the right part only, or the left only (round 19's X1, X1b) | `Validate_WithTheOperandSql_OfLongPredicates_StaysLinear` (the chains compared with a column on either side) |
| D83, D84 | `<=`, or `<` and `>`, not comparisons to the unifier (X9, X9b) | `twinLe`, `twinNotLt`, `twinNotGt` |
| D85 | The overload gives no verdict where no part was unified (X14) | `Validate_WithTheOperandSql_GivesTheVerdict`, every provider's guard keys |
| D86 | The memo by node only (R19-2; re-anchored in rev 21) | `Validate_WithTheOperandSql_AsksEachMemberOnce` |
| D87 | The row-only check lets a lambda through (R19-2) | the `parameterlessLambda` row |
| D88 | `OperandText` reads quoted text letter-case-blind (R19-3) | `OperandText_…`'s quoted rows, `TheGuard_ComparesQuotedTextWithItsLetterCase_InEveryProvider`, the SQLite JSON neighbour |
| D89 | SQLite's WHERE translation gives the verdict twice again (R19-1) | `Delete_ReadsACapturedValue_AsOftenAs3110`; since rev 21 the harness's `Delete_ReadsACapturedValue_NoMoreOftenThan3110` on SQLite |
| D90 | SQLite's WHERE translation checks no contract before translating (R19-1) | `Query_OfANullPredicate_ThrowsArgumentNull` (the harness's since rev 21) |
| D91 | Under a cast through `object` the verdict evaluates as elsewhere (R20-1) | `objOrCapturedTrue` |
| D92 | A cast through `object` that reads no row is folded inside, not evaluated whole (R20-1) | `orObjCapturedTrue` |
| D93 | The member memo keyed by the member's instance (R20-2) | `Validate_WithTheOperandSql_AsksEachMemberOnce`'s base-class row |
| D94 | A `bool` comparison that reads no row evaluated under a cast through `object` (R20-1) | `objOrCapturedTrueEqTrue` |
| D95 | `QuotedTextAsHex` drops the character after a quoted run (round 20's X20-1) | `OperandText_WritesEachParameterAsItsValue`'s exact row |
| D96 | The memo keys any member access, not only the row's (X20-2) | `Validate_WithTheOperandSql_AsksEachMemberOnce`'s two columns' `Length` |
| D97m, D97p, D97y | SQL Server's, PostgreSQL's or MySQL's WHERE translation gives the verdict twice again (X20-3) | that provider's `Delete_ReadsACapturedValue_NoMoreOftenThan3110` |
| D98m, D98p, D98y | The same provider checks no contract before translating (X20-4) | that provider's `Query_OfANullPredicate_ThrowsArgumentNull` |
| D99 | The empty search under a cast through `object` isn't counted (R21-1) | `objNameContainsEmpty`, `idOrObjNameStartsWithEmpty`, `objNameEndsWithStringEmptyOrId`; SQLite's `R21-1` |
| D100 | Under a cast through `object` a captured search value is read (R21-1, AC-N13) | `objNameContainsCapturedEmpty`, `Classify_UnderACastThroughObject_ReadsNoCapturedValue` |
| D101 | Under a cast through `object` `string.Empty` isn't a literal empty value (R21-1) | `objNameEndsWithStringEmptyOrId`; SQLite's `R21-1b` |
| D102 | A member is its own declaration, so an override isn't the property it overrides (R21-4) | `Classify_SeesAnAliasOfAnOverriddenColumnAsTheColumn`, `Validate_WithTheOperandSql_AsksEachMemberOnce`'s overridden-column row |
| D103 | A member keyed by its module alone (R21-4; round 21's X21-4) | `Classify_SeesAnAliasOfAnOverriddenColumnAsTheColumn`'s two columns, and 15 more `Classify` rows |
| D104 | A cast through `object` under another one folds under the verdict's rules again (round 21's X21-2) | `objObjOrCapturedTrue`, the reads test's `nested` |
| D105 | A cast that reads no row, under one that reads the row, is evaluated (round 21's X21-3) | `objOrObjCapturedTrue`, the reads test's `nestedRowFree` |
| D106 | The two under-cast rule sets merged (round 21's X21-1, equivalent at rev 21) | `objNameContainsEmpty`, `idOrObjNameStartsWithEmpty`, `objNameEndsWithStringEmptyOrId` |
| D107 | A captured search value is never read, outside a cast too (R21-1) | `nameContainsCapturedEmpty` |
| D108 | A property's first declaration from its getter alone, so a setter-only override is two members (the counterpart on rev 23's code of round 22's X22-4, which dropped the fallback and threw) | the setter-only rows of `Classify_SeesAnAliasOfAnOverriddenColumnAsTheColumn` and `Validate_WithTheOperandSql_AsksEachMemberOnce` |
| D109 | Under a cast through `object` any string literal is the empty value (round 22's X22-1) | `objNameContainsA` |
| D110 | Under a cast through `object` any static member named `Empty` is the empty value (round 22's X22-5) | `objNameContainsOtherEmpty` |
| D111 | A property is its own declaration, an override never the one it overrides (R21-4, R22-1; D102's twin, the same two failures: round 23's R23-2) | `Classify_SeesAnAliasOfAnOverriddenColumnAsTheColumn`, `Validate_WithTheOperandSql_AsksEachMemberOnce` |
| X23-2, X23-3 | `FirstDeclaration`'s loop matches a candidate by its getter alone, or its setter alone (round 23's; run by its reviewer, not in `mutate26.py`) | the same two tests (`mutations-r23-review.md`) |
| D77–D80 | The row-only check lets a static member, another parameter (a nested lambda's), a constructor (an initializer's) or an array through (AC-N13; its invocation, lambda and initializer overrides were withdrawn as redundant: a delegate is a constant that isn't a plain value, a nested lambda's parameter is another parameter, an initializer holds a constructor) | `Validate_WithTheOperandSql_NeverSendsAPartThatReadsOrRunsAnythingElse` |
| M-N50, M-N50s | A value-position `!` over a known truth not folded, rev 7's rule (R7-2), SQLite's and SQL Server's visitor | `ANotInAValuePosition_OverAKnownTruth_IsItsLiteral` (every provider's visitor; no harness shape reaches it now that R7-1 is closed) |
| Y1–Y27 | Round 7's mutants, re-anchored: the cast materialization, `StripNegation`'s lifts, the moved `!`'s value, each `!a == b` guard (Y6a, Y6b), two values under negation (Y7), a conditional's false branch (Y8), a nested conditional branch (Y9), the visitor's `!` chain (Y10, Y10s), the memo in `TryReadValue` and the `Substituter` (Y13, Y14), AC-N16's positive p (Y15), `BranchKind` (Y16), descent through `!` (Y22), a null `bool?` under negation (Y25), the literal-null keep under a moved `!` (Y27) | Each by its row in §6.2 (R7-4) or an earlier key: Y6a and Y6b `Rewrite_KeepsCSharpsValue`; Y7, Y8, Y25 `Rewrite_ValuesUnderNegation_…`; Y10, Y10s `notNotCondFoldedTestNeOther`, `ANotInAValuePosition_OverAKnownTruth_…`; Y15 `nboolCondNePredicateBranchNeTrue`; Y22 `nboolCondNotCondBranchNeTrue`. Y2 (a null read for a non-nullable `bool` side) is equivalent: the same rows |
| M-R2 | (killed: R2's red run, 17 failures) | |

Rev 7 moved the code of M-N1, M-N2, M-N2b, M-N2c, M-N4d, M-N12, M-N14, M-N14b, M-N15, M-N18, M-N19, M-N20, M-N21,
M-N22, M-N23 and M-N24; each is re-anchored to the moved code and rerun. M-N19's conditional half went with
`IsPredicate`'s conditional case (R6-6).
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
- **Recorded (rev 5 additions):** run in a worktree at `19e7441` (production code `25956e0`) with only the harness
  copied in: 181 tests per provider; failed / passed: SQLite 142 / 39, SQL Server 141 / 40, PostgreSQL 138 / 43,
  MySQL 142 / 39. Every rev 5 proof fails for an allowed reason: the rows, SQL or verdict; SQL Server's syntax error
  and "an expression of non-boolean type"; PostgreSQL's 42883 and 42601; the fan-out refusal (`ReverseJoin_…`). The
  new passes are pins: `notIdAndCodeEq5` (N11), the two `Remote_Delete_…` rows, `nameContainsAEqNullBool` on SQLite
  and MySQL, and `boolBranchCapturedLiteral` on SQL Server (dialect-rejected). 27 captured entries per provider, 25
  on PostgreSQL.
- **Mutations (rev 5, the working tree):** all 33 of §6.4's are killed, each applied once, its killers run, and the
  file restored byte for byte. Core mutations run the SQLite harness and the DB-free core tests; mutations of a
  provider's copy (visitor, translator, LINQ provider) were made in SQLite's, since the four copies are one template
  and each provider's harness run covers its own. Each named killer failed except where the table below §6.4 says
  otherwise. M-N13a (drop the `Convert`) fails only `colorNeCapturedColor`'s twin and `Substitution_KeepsTheConvertChain`,
  as R4-6 predicted. M-N11's first run was itself wrong (an `&&`/`||` precedence slip disabled only `[RemoteKey]`);
  rerun, it fails all four named keys and two core tests.
- **Coverage (rev 5):** the four full suites with coverlet, unioned per file: `NullSemantics.cs` 91.7%,
  `WherePredicateGuard.cs` 95.8%, `RowIndependentEvaluator.cs` 100%, the four translators 99.6%, the four LINQ
  providers 94.4–95.5%. The four WHERE visitors were 82–84%: the rewrite now substitutes the captured values they used
  to read, and their remaining defensive paths had no test. `WhereVisitorDirectTests` (DB-free, every provider's
  visitor) covers `.HasValue`, an operand no visitor translates, a `new` operand, and a captured value the rewrite
  leaves to them, which brings them to 94.8–95.6%. The lines left are unreachable (a member that is neither a field
  nor a property). Then the rewriter's own uncovered rules got tests: `!=` of one witness with itself, a negated `>`
  of two columns, a column against a side that folds, two values compared, and the operands it must leave alone
  (five core tests, and the harness keys `nameLengthSelfNe` (SQL-proof), `notNGtM`, `flagEqFoldedPredicate` and
  `flagNeFoldedPredicate`, red at `25956e0` by rows, SQL shape or the syntax error). `SameWitness` lost a loop no
  witness could reach.
- **Rev 6 (round 5's answers):** each new test was red first. At the rev 5 code, on SQL Server, the five R5-1 keys
  failed with T-SQL's syntax and "non-boolean type" errors, and the read, branch and lazy-collection tests failed
  (13 in all). At `25956e0`, in the worktree, the R5-1 keys are pins on all four providers, and the rest fail for an
  allowed reason (the SQL shape, the rows, the verdict, PostgreSQL's 42804/42883, SQL Server's syntax error). The nine
  round-5 mutations (M-N4f, M-N4g, M-N19–M-N24) are all killed.
- **Rev 7 (round 6's answers).** Each new test was red first, at the rev 6 code. At the first red run the DB-free
  classes failed 9 (with rev 7's final tests, round 7 counted 12 and 14 SQLite keys; R7-8), each for its stated reason: the moved `!`, a side that reads no row, R6-3's `NullReferenceException` (with the visitor's
  memo), the predicate branch, the fold's read, `Classify`'s new kinds, the value-position `!` (`ELSE 1=0`), and the
  `IQueryable<T>` run twice. The SQLite harness failed 13 keys and `ReadOnce` by rows or SQL shape. `notNEq5EqActive`
  passed, as the pin it is.
- **The rev 7 harness at `25956e0`** (the worktree at `19e7441`, only the harness copied in): 211 tests per provider.
  Failed / passed: SQLite 165 / 46, SQL Server 163 / 48, PostgreSQL 161 / 50, MySQL 165 / 46. Round 6's keys:
  - pins on all four: `notBoolCondEqTrue` (3.11.0's text ran, with C#'s rows; R6-1) and `condFoldedEqFlag` (R6-3);
  - `nEq5EqCondNotOther` is dialect-rejected on SQL Server before and after;
  - the rest fail by rows, by SQL shape, or by the database's own rejection: SQL Server's syntax errors ("near '='",
    "near the keyword 'NOT'", "near '!'"), PostgreSQL's 42601, 42883 and 42804, and MySQL's syntax error near `NOT`.
  - `ReadOnce_CountingGetter` stops at its 3.11.0 row `c.Flag != x.Flag` (2 reads; PostgreSQL: 42804). The new read
    rows are pins against 3.11.0's counts, from round 6's probe and fuzz: 1, 1, 2 (rev 7: 1), 1 and 1, and nothing
    under a branch the fold knows (3.11.0's visitor folded that branch away).
- **The self-check fuzz (rev 7, before review).** Round 6's two fuzzers, against a lifted-C# oracle on SQLite, each
  SQL Server text parsed offline (Microsoft's T-SQL parser):
  - round 6's extended fuzzer, 32,000 predicates: no wrong rows, no read beyond 3.11.0's, no T-SQL regression. 7,024
    predicates that 3.11.0 sent as invalid T-SQL now parse. 46 newly refused deletes, each always true in C#.
  - round 5's, 36,000: no wrong rows (rev 6: 32), no read beyond 3.11.0's, and 59 newly refused deletes, each always
    true in C#.
  - Rev 7's first cut had four defects there, now fixed (§11). The run predates rev 7's last two coverage edits (the
    `Substituter`'s cast override and a `TryReadValue` guard); round 7 reran both fuzzers on the frozen tree and got
    the same TSVs byte for byte (R7-8).
- **Mutations (rev 7):** 66, each applied once, its killers built and run, and the tree restored byte for byte
  (checksummed). All killed: round 6's M-N25 to M-N48, the 16 re-anchored, and every unchanged earlier one but
  M-R3a (the CI gate's, killed in rev 5; `ci.yml` is unchanged since).
- **net48 (rev 7): not green.** The "32 / 32" recorded here was a stale binary: the build's errors were filtered out
  of view and vstest ran the earlier DLL. Round 7 measured 31 / 32 on the frozen tree (R7-3); rev 8's run is below.
- **Rev 8 (round 7's answers).** Each new test was red first, at the rev 7 code: the core and visitor tests by
  `!(bool?)p` left alone and by `NOT @p = @p` with its values read again (2); the SQLite harness by the four R7-1 keys'
  rows and `ReadOnce`'s extra read (5). The R7-4 rows pass at rev 7 and fail under their mutants.
- **The rev 8 harness at `25956e0`** (the worktree, only the harness copied in): 220 tests per provider. Failed /
  passed: SQLite 172 / 48, SQL Server 171 / 49, PostgreSQL 168 / 52, MySQL 172 / 48. Round 7's keys: the four R7-1
  keys fail on all four, by rows or the database's own rejection (SQL Server's syntax errors, MySQL's); 
  `notNotCondFoldedTestNeOther` by SQL shape (PostgreSQL: 42883); `condSelfOrCode5` because 3.11.0 accepted the delete
  (every row). `nboolCondNePredicateBranchNeTrue` is a pin on SQLite, PostgreSQL and MySQL (3.11.0's three-valued CASE
  happens to give C#'s rows there) and a proof on SQL Server (its syntax error); `nboolCondNotCondBranchNeTrue` is a
  pin on all four (dialect-rejected on SQL Server). Both pins exist to kill Y15 and Y22.
- **Rev 9 (round 8's answers).** Each new test was red first, at the rev 8 code: the DB-free core tests by the cast left
  alone and the extra read (2); the SQLite harness by the three R8-1 keys' rows and `ReadOnce`'s `c.A` read (4).
  `notFlagEqNotFlag` and the R8-4 visitor row pass at rev 8 (a verdict that changed against 3.11.0, and Z6's killer).
- **The rev 9 harness at `25956e0`:** 225 tests per provider. Failed / passed: SQLite 176 / 49, SQL Server 175 / 50,
  PostgreSQL 173 / 52, MySQL 176 / 49. Round 8's keys: `castNotLiftedNGt3` and `castLiftedNGt3EqActive` fail on all
  four, by rows or the database's rejection; `castNotLiftedMGtNoneAndOnlyNull` is a pin on SQLite, SQL Server and
  MySQL (3.11.0 selected every row and refused the delete; rev 8 regressed it, R8-1) and fails on PostgreSQL by
  42883; `notFlagEqNotFlag` fails because 3.11.0 refused the delete as a self-reference (SQL Server and MySQL: their
  syntax error first).
- **Mutations (rev 9):** 91, each applied once, its killers built and run, and the tree restored byte for byte. All
  killed: rev 8's 86 (Y14 re-anchored to the `Substituter`'s new fold), M-N51 to M-N53, and Z6/Z6s.
- **net48 (rev 9):** MSBuild exit 0, no errors, the DLL written during that build (its time checked against the
  build's start); `NullSemanticsTests` 36 / 36.
- **Rev 23 (round 22's answers).** Each new test was red first, at rev 22's code (the working tree before the code
  changed, its code files rev 22's tested ones): **the guard class failed 2 of 454** (`red-rev22-guard.txt`):
  `Classify_SeesAnAliasOfAnOverriddenColumnAsTheColumn` (the setter-only override's `x.N == x.NX` Acceptable) and
  `Validate_WithTheOperandSql_AsksEachMemberOnce` (its `x.N` asked twice). The rows that pass at rev 22 pin what it
  already gave, and kill D109 and D110: `objNameContainsA`, `objNameContainsOtherEmpty`, and `objNameContainsConstEmpty`
  (a `const` is written in place, R22-4).
- **The long-chain limit through the real providers (rev 23, R22-2; `depth22\bisectall.py`, `depth23-real.tsv`).**
  Each length in its own process from a console app's main thread on Windows (the default 1.5 MB stack, round 22's
  reviewer measured it for the main thread, a new thread and the thread pool alike), Release builds, 3.11.0 against
  rev 23, a query and, right-deep, a delete:
  - PostgreSQL: `x.Id == 0 || x.Id == 1 || …` 2,882 against 2,437; `&&` the same; right-deep `a || (b || (…))`
    2,913 against 1,812 (query and delete alike); MySQL: 2,867 against 2,421, `&&` the same, right-deep 2,913 against
    1,812. Past each the process ends (`0xC00000FD`).
  - SQL Server refuses every one of these shapes before either limit, in both versions alike: "Some part of your SQL
    statement is nested too deeply" from 1,007 operands left-deep (`||` and `&&`) and 343 right-deep, so round 22's
    SQL Server window (right-deep chains of about 1,830–2,100 operands) doesn't occur. SQLite's cap is 1,000 (rev 22).
  - The right-deep limit is the null-semantics rewrite's (`Rewriter.Logical` walks a left-deep chain without
    recursing and recurses once per operand of a right-deep one; round 22's trace), the left-deep one the visitors'
    (rev 22). Round 22's reviewer once saw a run near the right-deep limit spin for 15 minutes before its timeout,
    inside the expression compiler (`LambdaExpression.Compile`, reading a captured value); not reproduced. The Changelog
    states the limit per shape, for PostgreSQL and MySQL; the owner kept it a Known issue (Paul, 2026-10-23: "A, keep it as a Known issue").
  - **Smaller stacks (R23-1, after round 23; `depth22\phase512.py`, `depth23-512.tsv`, `depth23-768.tsv`):** each
    visitor on a thread of the given size, one process per length. On 512 KB: SQL Server's visitor 761 against
    3.11.0's 898, right-deep 593 against 952; SQLite's 777 against 909, right-deep 593 against 952 (round 22's
    reviewer's numbers exactly), below both databases' own limits (SQL Server's 1,007 and 343; SQLite's about 1,000
    and, right-deep, 831 by round 23's measurement). On 640 KB SQL Server's visitor stops at 968, on 672 KB at 1,073;
    on 768 KB rev 23 is past every limit of both (1,179 left-deep, 902 right-deep). The Changelog states the 1.5 MB
    case for PostgreSQL and MySQL and the smaller-stack case (below about 768 KB) for every provider.
- **Mutations (rev 23):** 227, each applied once to the final tree, its killers built and run, and the code files restored
  byte for byte (`code-md5-*.txt`, `mutations-r23.md`; the tested code is the restored code, `code-md5-final.txt`). All
  killed: rev 22's 223 (D102 re-anchored on `FirstDeclaration`) and D108–D111 (D108–D110 are round 22's X22-4, X22-1
  and X22-5). D58 still ends the test host with a stack overflow, recorded as a kill.
- **net48 (rev 23):** MSBuild exit 0, no errors, the DLL written during that build; `NullSemanticsTests` 53 / 53.
- **The self-check fuzz (rev 23).** Every fuzzer on rev 23 (`run-rev23.sh`; labels `rev23` and `src`, rev 22's kept as
  `rev22`), against 3.11.0's baselines: every TSV of fuzz5–fuzz13r, `fuzz18s`, `fuzz18t`, `fuzz19t` and `delreads` is
  byte-identical to rev 22's; `ssdepth16.py`'s output too; rounds 14–21's probes are rev 22's outputs but the timings.
  Round 22's (its `R22.cs` against rev 23 in `probe-src22`, `r22-*-rev23.txt`): `verdicts` is rev 22's; `memo` gives
  the setter-only override's M1–M1e their verdicts (self-referencing, or always true beside an `||` or compared with
  `false`) and asks its `x.N` once, where rev 22 asked it twice; M2–M6 as at rev 22.
- **Green (the working tree, rev 23, 2026-10-23 13:37 CT):** the four full suites with coverlet, on the final code, after
  the mutation run and a full rebuild: SQLite 1,479 / 1,479, SQL Server 3,434 / 3,435 (the one NotExecuted is
  `Projection_BoolValue_GetsCSharpsValue`, allowed), PostgreSQL 2,013 / 2,013, MySQL 1,955 / 1,955; the TRX gate passes on
  each (`nullsem-final23`). The harness 315 / 315 per provider (317 on SQLite with the delete tests); the guard class
  454; the core tests 53 / 53 on net8.0 and net48. Coverage per touched file: `NullSemantics.cs` 99.9%,
  `WherePredicateGuard.cs` 98.8% (950 of 962: every rev 23 line has hits but `FirstDeclaration`'s fallback for an
  accessor no property declares; the rest are the large-stack fallback's eight and rev 22's two older lines). After
  it the Changelog's heading was dated, `## [3.12.0] - 2026-10-23` (the owner's "publish as v3.12", 2026-10-23), and
  the packaging and documentation tests (44) ran again on it, green.
- **Rev 22 (round 21's answers).** Each new test was red first, at rev 21's code (the working tree before the code
  changed, its code files rev 21's tested ones):
  - **the guard class failed 5 of 451** (`red-rev21-guard.txt`): `Classify_ReturnsTheVerdict`'s `objNameContainsEmpty`,
    `idOrObjNameStartsWithEmpty` and `objNameEndsWithStringEmptyOrId` (Acceptable: the under-cast rules counted no
    empty search value), `Classify_SeesAnAliasOfAnOverriddenColumnAsTheColumn` (`x.N == x.NX` Acceptable on the
    overriding entity) and `Validate_WithTheOperandSql_AsksEachMemberOnce` (the overridden `x.N` asked twice);
  - **SQLite's `Delete_OfANestedTwin_IsRefused` failed** (`red-rev21-sqlite.txt`: its `R21-1` row deleted).
  The rows that pass at rev 21 pin what it already gave, and kill D100, D104 and D105: `objNameContainsCapturedEmpty`,
  `objObjOrCapturedTrue`, `objOrObjCapturedTrue` and `Classify_UnderACastThroughObject_ReadsNoCapturedValue` (D107 by
  the older `nameContainsCapturedEmpty`).
- **A stack overflow on the way (self-found).** The first run of the guard class on the R21-1 fix aborted after 315 of
  451 tests: `Fold_OfALongChainOfCapturedOperands_StaysLinear` (1,500 `||` operands, a Debug build) overflowed the test
  host's stack. The `Call` case's new arm, inside `FoldNode`, grew the frame that the chain repeats at every level; as
  `FoldCall` it runs green (451 / 451). The chain test is the killer of a frame that grows.
- **The long-`||` limit (self-found, `r10-selfcheck\depth22`).** Left-deep `x.Id == 0 || x.Id == 1 || …` chains, each
  length in its own process (a stack overflow ends it), bisected on 3.11.0 (`base`), rev 21 (the frozen tree) and rev 22
  (`src`), Release builds:
  - every provider's WHERE visitor on a 1 MB thread (`Phase.cs`, `depth22-phase.txt`): 3.11.0 1,890 operands, rev 22
    1,593–1,609 (SQL Server 1,601, SQLite 1,609, PostgreSQL 1,609, MySQL 1,593); the guard's `Fold` 2,327 against
    2,031, `Classify` 2,343 against 2,015, the null-semantics rewrite 2,031;
  - the real providers from a console app's main thread on Windows (`depth22-pg.txt`, `depth22-my.txt`): PostgreSQL's
    query and delete 2,875 against 2,437, MySQL's 2,859 against 2,421; SQLite's stop at its own cap in both (`Expression
    tree is too large (maximum depth 1000)` at 1,000 operands, `depth22.txt`); `Classify` there 3,531 against 3,046, the
    same at rev 21 (`rev21`), so round 21's fix doesn't move it;
  - a chain whose operands are captured values stops sooner at 3.11.0 (3,468) than at rev 22 (15,750), whose fold
    stops at an operand that decides.
  The visitors recurse through a larger `VisitPredicate` at every `||` operand (it answers a conditional and a `!` now).
  The Changelog states the limit as a Known issue; whether to remove it (a visitor that walks a chain of `||` or `&&`
  without recursing, which would put every path past 3.11.0's, the fold and the rewrite being the next limits) was the
  owner's call: kept as a Known issue (2026-10-23, rev 23).
- **Mutations (rev 22):** 223, each applied once to the final tree, its killers built and run, and the code files restored
  byte for byte (`code-md5-*.txt`, `mutations-r22.md`; the tested code is the restored code, `code-md5-final.txt`). All
  killed: rev 21's 214 (D86, D93 and D96 re-anchored on `MemberKey`) and D99–D107 (D104–D106 are round 21's X21-2,
  X21-3 and X21-1; X21-1 was equivalent at rev 21 and is killed by R21-1's rows). The twelve new or re-anchored ones ran
  first on their own (`mutations-r22-new.md`), with the same result. D58 still ends the test host with a stack
  overflow, recorded as a kill.
- **net48 (rev 22):** MSBuild exit 0, no errors, the DLL written during that build; `NullSemanticsTests` 53 / 53.
- **The self-check fuzz (rev 22).** Every fuzzer on rev 22 (`run-rev22.sh`; labels `rev22` and `src`, rev 21's kept as
  `rev21`), against 3.11.0's baselines: every TSV of fuzz5–fuzz13r, `fuzz18s`, `fuzz18t`, `fuzz19t` and `delreads` is
  byte-identical to rev 21's (no fuzzer writes an empty search value or an overriding entity), so rev 21's record stands
  (the 15 deletes accepted again and 1,632 moved to the check of the translated clause, each as 3.11.0 or refused with
  the same message). `delreads`: 9 deletes read more than 3.11.0's, 8 of them in the guard's part (the delete's reads
  less the query's) and 1 in the rewrite's, within AC-N13's cast bound; 4,555 read fewer; one query reads more (R21-6).
  `ssdepth16.py` over every fuzzer: rev 21's output. Rounds 14–20's probes are rev 21's outputs but the timings. Round
  21's (its `R21.cs`, `R21b.cs` against rev 22 in `probe-src21`, `r21-*-rev22.txt`): `verdicts` refuses A1–A3 and A5–A9
  as an empty search value with no read (rev 21 deleted them), keeps A4 and B28 (a captured `""`) accepted with 3.11.0's
  rows, and gives every B row rev 21's outcome; `memo2` asks the overridden `x.N` once (rev 21: twice) and classifies
  `x.N == x.NX` there as self-referencing; `hide` gives the same; `offreads` is rev 21's.
- **Green (the working tree, rev 22, 2026-10-23 11:23 CT):** the four full suites with coverlet, on the final code, after
  the mutation run and a full rebuild: SQLite 1,479 / 1,479, SQL Server 3,431 / 3,432 (the one NotExecuted is
  `Projection_BoolValue_GetsCSharpsValue`, allowed), PostgreSQL 2,013 / 2,013, MySQL 1,955 / 1,955; the TRX gate passes on
  each (`nullsem-final22`). The harness 315 / 315 per provider (317 on SQLite with the delete tests); the guard class
  451; the core tests 53 / 53 on net8.0 and net48. Coverage per touched file: `NullSemantics.cs` 99.9% (rev 4's guard
  line left), `WherePredicateGuard.cs` 98.9% (939 of 949: every rev 22 line has hits; the 10 left are the large-stack
  fallback's eight and two older lines: a string method other than the three searches, and the member chain's `broad`
  return, which the structural comparison above it reaches first), the providers' data files 89.9–95.1%, the visitors 95.5–95.8%, the translators 99.6%, the LINQ providers
  94.4–95.5%, `RowIndependentEvaluator.cs` 100%.
- **Rev 21 (round 20's answers).** Each new test was red first, at rev 20's code: round 20's frozen tree with rev 21's
  test files copied in (`rev20-redcopy`, `run-red.sh`, `red-rev20-*.txt`):
  - **the DB-free classes failed 3 of 511:** `Classify_ReturnsTheVerdict`'s `objOrCapturedTrue` and
    `objOrCapturedTrueEqTrue` (rev 20 evaluated the captured value under the cast and gave AlwaysTrue) and
    `Validate_WithTheOperandSql_AsksEachMemberOnce` (the base class's `x.N` asked twice);
  - **the SQLite run failed 2 of 317, and the SQL Server harness 2 of 315:** `Delete_ReadsACapturedValue_NoMoreOftenThan3110`'s
    `objNBEqFlag` and `objNBNeFlagOrIdEq2` (5 reads against 3.11.0's 4: a boxed null read twice).
  The rows that pass at rev 20 pin what it already gave, and kill D92, D95, D96 and the providers' D97, D98: the other
  fifteen read rows, `Query_OfANullPredicate_ThrowsArgumentNull`, the exact `OperandText` row, the two-`Length` asks row,
  `orObjCapturedTrue`, `objSelfOrCapturedTrue`, and SQLite's `R20-1` and `R20-4` delete rows.
- **At `25956e0`** (the red worktree, the harness and SQLite's computed-part tests copied in; `red-25956e0-reads-*.txt`):
  the harness's read and null-predicate tests pass on SQLite and MySQL (18 of 18), so their counts are 3.11.0's; on SQL
  Server 3 and on PostgreSQL 4 of them fail by the database's error, rows 3.11.0 sent as invalid SQL (`(c.T ? x.N : x.M) ==
  5`, `(x.N == 5) == c.T`, a `bool?` against text), which the Changelog lists among the shapes that run now.
- **The delete-reads fuzzer (rev 21, adopted from round 20).** `delreads` (round 20's `R20.cs`, `an-delreads.py`,
  `run-delreads.sh`; fuzz10's predicates through SQLite's real provider, seeds 71–74 and 100–103, 12,000 shapes; 3.11.0
  through round 20's own `probe-base` binary, rev 19 through `probe-r19x20`), counting a captured value's reads by the
  delete and the query: at rev 20, 646 deletes read more than 3.11.0's (round 20's count); after the boxed-null fix alone,
  631 (the author's); at rev 21, 9, each around a cast through `object`, the verdict's fold under the cast deciding a
  side beside it that 3.11.0's didn't reach; 4,555 read fewer; one query reads more (AC-N13's cast bound).
- **Mutations (rev 21):** 214, each applied once to the final tree, its killers built and run, and the code files restored
  byte for byte (`code-md5-*.txt`, `mutations-r21.md`; the tested code is the restored code, `code-md5-final.txt`). All
  killed: rev 20's 202 (M-N22 and D86 re-anchored on rev 21's code) and D91–D98 (D95–D98 are round 20's X20-1..X20-4;
  D97 and D98 on SQL Server, PostgreSQL and MySQL by that provider's harness).
- **net48 (rev 21):** MSBuild exit 0, no errors, the DLL written during that build; `NullSemanticsTests` 53 / 53.
- **The self-check fuzz (rev 21).** Every other fuzzer on rev 21 (`run-rev21.sh`; labels `rev21` and `src`, rev 20's kept
  as `rev20`), against 3.11.0's baselines: fuzz5–fuzz9's TSVs and `fuzz18s`', `fuzz18t`'s and `fuzz19t`'s are
  byte-identical to rev 20's. In fuzz10, fuzz10b, fuzz11, fuzz11r, fuzz12r and fuzz13r only the delete column moves
  (`cmp_cols.py`): 1,632 deletes that rev 20's verdict refused as always true are refused at rev 21 by the check of the
  translated clause, with the same exception and message (`AlwaysTrueMessage`), and 15 that rev 20 refused are accepted
  (`cmp_accepted_base.py`), each as 3.11.0 accepted it and each with C#'s rows (9 of them every row): a part under a cast
  through `object` that reads the row, always true or self-referencing only once a captured value under the cast is
  read (`(bool)(object)(h.T ? (bool?)(x.Active == x.ActiveX) : h.NB)`), now the Changelog's Known issue and §4.3's
  cast row (`objOrCapturedTrue` pins the verdict). No wrong rows, no exception, no T-SQL regression; reads above
  3.11.0's only through a cast through `object` (the delete-reads fuzzer above). `ssdepth16.py` over every fuzzer:
  nothing above 3.11.0's + 1, past 10 levels or past 2,100 parameters where 3.11.0 was within. Rounds 14–19's probes
  are rev 20's outputs but the timings; round 20's (`R20.cs` against rev 21, `r20-*-rev21.txt`): `quotedel`, `order`
  and `quote` are rev 20's, and `memo` asks a base class's member once (`x.N` once where rev 20 asked it twice). The
  Changelog's and §4.3's cast rows, and the Known issue, were narrowed after the green run below (documents only);
  `DocumentationTruthTests` and `PackagingTests` ran again on them, 26 / 26.
- **Green (the working tree, rev 21, 2026-10-23 08:40 CT):** the four full suites with coverlet, on the final code, after
  the mutation run and a full rebuild: SQLite 1,479 / 1,479, SQL Server 3,423 / 3,424 (the one NotExecuted is
  `Projection_BoolValue_GetsCSharpsValue`, allowed), PostgreSQL 2,013 / 2,013, MySQL 1,955 / 1,955; the TRX gate passes on
  each (`nullsem-final21`). The harness 315 / 315 per provider (317 on SQLite with the delete tests); the core tests 53 /
  53 on net8.0 and net48. Coverage per touched file: `NullSemantics.cs` 99.9% (rev 4's guard line left),
  `WherePredicateGuard.cs` 98.9% (every rev 21 line has hits; the 10 left are the large-stack fallback's), the providers'
  data files 89.9–95.1%, the visitors 95.5–95.8%, the translators 99.6%, the LINQ providers 94.4–95.5%,
  `RowIndependentEvaluator.cs` 100%. **A slip:** while deriving rev 21's scripts the author re-ran rev 20's suite script
  by mistake (08:33–08:36), which ran rev 21's code and overwrote `nullsem-final20` (green too: the same counts as above);
  rev 20's own run stands as recorded below, and that folder's `README-OVERWRITTEN.md` says so.
- **Rev 20 (round 19's answers).** Each new test was red first, at rev 19's code: round 19's frozen tree with rev 20's four
  test files copied in (`rev19-redcopy`, `run-red-rev19.sh`, `red-rev19-final-*.txt`; the first run, in the working tree
  before the code changed, is `red-rev19-*.txt`, the same failures):
  - **the DB-free classes failed 4 of 507:** `TheGuard_ComparesQuotedTextWithItsLetterCase_InEveryProvider` (templates that
    differ in a literal's letter case refused as always true), the `parameterlessLambda` row (asked), `Validate_WithTheOperandSql_AsksEachMemberOnce`
    (`x.N` asked three times, `x.Other` twice) and `OperandText_WritesEachParameterAsItsValue` (the quoted rows);
  - **the SQLite run failed 5 of 311:** four rows of `Delete_ReadsACapturedValue_AsOftenAs3110` (5, 5, 5 and 4 reads against
    3.11.0's 3) and `Delete_OfANestedTwin_IsRefused` (the JSON-key neighbour refused).
  The rows that pass at rev 19 pin what it already gave, and kill D81–D85 and D90: `twinLe`, `twinNotLt`, `twinNotGt`, the
  timing test's column chains, `Validate_WithTheOperandSql_GivesTheVerdict`, `Query_OfANullPredicate_ThrowsArgumentNull`.
- **At `25956e0`** (the red worktree, the two SQLite classes copied in; `red-25956e0-Sqlite.txt`): all twelve of
  `SqliteDeleteReadsTests` pass (the read counts are 3.11.0's: a query 1 or 2, a delete 3 or 1, as round 19 measured), and
  `SqliteComputedPartSelfReferenceTests`' two fail as they did there before (3.11.0's rows, and its acceptance of T5). The
  harness is rev 19's (`red22-*.trx`).
- **Mutations (rev 20):** 202, each applied once to the tree, its killers built and run, and the code files restored
  byte for byte (`code-md5-*.txt`, `mutations-r20.md`). All killed: rev 19's 192 (no anchor moved) and D81–D90 (D81–D85 are
  round 19's X1, X1b, X9, X9b and X14; D89 and D90 by SQLite's read and null-predicate tests).
- **net48 (rev 20):** MSBuild exit 0, no errors, the DLL written during that build; `NullSemanticsTests` 53 / 53.
- **The twin fuzzer with three more wrappers (rev 20, R19-4).** `fuzz19t` (`Fuzz19T.cs`, `an19t.py`, `run-fuzz19t-all.sh`:
  `fuzz18t` with `<=`, `!(<)` and `!(>)`; seeds 701–716, 24,000 shapes), on 3.11.0, rev 19 (`probe-rev19`, over the frozen
  rev 19 sources) and rev 20: no miss and no wrong rows at rev 19 or rev 20, whose TSVs are byte-identical (rev 19 already
  refused the three forms; the fuzzer now pins them). 14,009 deletes that 3.11.0 ran are refused: 13,260 that delete every
  row in C#, and 749 that don't, each a `>=` or `<=` of an operand and its twin over values that can be NULL (3.10's rule).
- **The self-check fuzz (rev 20).** Every other fuzzer on rev 20 (`run-rev20.sh`; labels `rev20` and `src`, rev 19's kept
  as `rev19`), against 3.11.0's baselines: fuzz5–fuzz9, fuzz12r and fuzz13r's TSVs, and `fuzz18s`'s and `fuzz18t`'s, are
  byte-identical to rev 19's; fuzz10, fuzz10b, fuzz11 and fuzz11r keep rev 19's counts, class by class: no wrong rows, no
  exception, no T-SQL regression, reads above 3.11.0's only through a cast through `object`. `ssdepth16.py` over every
  fuzzer: nothing above 3.11.0's + 1, past 10 levels or past 2,100 parameters where 3.11.0 was within. Rounds 14–18's
  probes are rev 19's outputs but the timings. Round 19's probes (`R19.cs`, `R19b.cs` against rev 20, `r19-*-rev20.txt`):
  `reads` gives 3.11.0's counts (3, 3, 3 and 3 where rev 19 read 5, 5, 5 and 4); `case` and `provcase` run the JSON-key
  and literal-case pairs with C#'s rows (rev 19 refused them); `asks` asks a member of the row once (`x.N` once where rev
  19 asked it four times) and no lambda part, and `x.Name.Length` written twice, two nodes, twice, as the comments now
  say; `ops` and `bound` are rev 19's. The run synced the sources before the last change, which was comments and a test's
  name only (the four suites, the gate and net48 ran again after it).
- **Green (the working tree, rev 20, 2026-10-23 06:22 CT):** the four full suites with coverlet, on the final code, after
  the mutation run and a full rebuild: SQLite 1,473 / 1,473, SQL Server 3,401 / 3,402 (the one NotExecuted is
  `Projection_BoolValue_GetsCSharpsValue`, allowed), PostgreSQL 1,995 / 1,995, MySQL 1,937 / 1,937; the TRX gate passes on
  each (`nullsem-final20`). The harness 297 / 297 per provider (311 on SQLite with the delete and read tests); the core
  tests 53 / 53 on net8.0 and net48. Coverage per touched file: `NullSemantics.cs` 99.9% (rev 4's guard line left),
  `WherePredicateGuard.cs` 98.9% (every rev 20 line has hits; the 10 left are the large-stack fallback's), the providers'
  data files 89.9–95.1%, the visitors 95.5–95.8%, the translators 99.6%, the LINQ providers 94.4–95.5%,
  `RowIndependentEvaluator.cs` 100%.
- **Rev 19 (round 18's answers; the test redesigned first).** Each new test was red first, at rev 18's code: round 18's
  frozen tree with rev 19's four test files copied in and the new API as a stub that asks nothing, so the tests compile
  and fail by behaviour (`rev18-redcopy`, `run-red-rev18.sh`, `red-rev18-final-*.txt`; the first run, before the later
  rows, is `red-rev18-*.txt`: 14 of 481, 3 and 2):
  - **the DB-free classes failed 17 of 488:** 14 rows of `Validate_WithTheOperandSql_RefusesTwoOperandsOfTheSameSql`
    (every twin, with the alias in the tests, in another letter case, with the same captured member: accepted; the
    `null` and `throws` rows: nothing asked), `Validate_WithTheOperandSql_OfLongPredicates_StaysLinear` (accepted),
    `OperandText_WritesEachParameterAsItsValue` (the stub returns the text unchanged) and
    `TheGuard_RefusesANestedComputedTwin_WithEveryProvidersOperandSql` (SQL Server's T1 accepted first);
  - **the SQLite run failed 3 of 299:** `Delete_OfANestedTwin_IsRefused` (T1 deleted first) and the keys
    `nestedNAbsUpperEqNAbsLower` and `nestedDocKEqDocK2` by their deletes (accepted, every row);
  - **the SQL Server harness failed the same two keys of 297.**
  The rows that pass at rev 18 pin what it already gave: the neighbours (another test, another column, another captured
  member, a call that reads no row), `idEq2`, the read, the argument checks, the R18-4 `Classify` rows (they kill D74–D76,
  round 18's surviving X1, X2, X9) and R18-2's examples (`notNAliasNe`, `nAliasEqTrue`, `nameAliasNeNeTrue`), and the
  neighbour key `nestedNAbsUpperEqCodeCase`. The eight rows of
  `Validate_WithTheOperandSql_NeverSendsAPartThatReadsOrRunsAnythingElse`, added with the coverage pass after this run,
  would pass at rev 18 as well (the stub asks nothing): they are there to kill D77–D80.
- **The rev 19 harness at `25956e0`** (the worktree, the harness and `SqliteComputedPartSelfReferenceTests` copied in,
  `run-red22.sh`): 297 tests per provider (299 on SQLite). Failed / passed: SQLite 237 / 62, SQL Server 225 / 72,
  PostgreSQL 237 / 60, MySQL 236 / 61 (`red22-*.trx`). Round 18's keys fail on all four by their queries' rows (3.11.0
  dropped the NULL rows); the SQLite delete test fails at T5 (3.11.0 accepted `!(… != …)`, 4 of 6; it refused T1–T3, as
  rev 19 does).
- **Mutations (rev 19):** 188, each applied once to the tree, its killers built and run, and the code files restored
  byte for byte (`code-md5-*.txt`, `mutations-r19.md`). All killed: rev 18's 167 (no anchor moved) and D59–D76 (D73p and
  D73y by PostgreSQL's and MySQL's harness; D74–D76 are round 18's surviving X1, X2 and X9, killed by the R18-4 rows). The
  coverage pass then found the row-only check's other branches untested, and their first mutants (D77–D82) killed 1 of 6:
  the test's parts read no row, so nothing was asked whatever the check said, and three overrides were redundant. The
  parts now read the row; the invocation, lambda and initializer overrides are withdrawn (a delegate is a constant that
  isn't a plain value, a nested lambda's parameter is another parameter, an initializer holds a constructor); D77–D80 are
  killed (`mutations-r19-d77.md`). On that final code the 50 mutants that edit the guard ran again, each killed, the code
  files restored byte for byte (`mutations-r19-final-guard.md`, `code-md5-guard-*.txt`).
- **net48 (rev 19):** MSBuild exit 0, no errors, the DLL written during that build; `NullSemanticsTests` 53 / 53.
- **The twin fuzzer (rev 19, adopted from round 18).** `fuzz18t` (round 18's `Fuzz18T.cs` and `an18t.py`, `run-fuzz18t.sh`;
  seeds 601–616, 24,000 shapes; its first eight seeds' TSVs byte-identical to the reviewer's at 3.11.0 and rev 18) swaps
  a part of an operand's copy for a twin only the provider's SQL shows (SQLite's own text for a column's `Length` or year,
  a template in another letter case, an alias), under each of `fuzz18s`'s wrappers, through the real SQLite provider. At
  rev 18: 2,613 deletes that 3.11.0 refused ran (2,433 of every row). At rev 19: none, and no wrong rows. Two first cuts
  missed: comparing whole operands' SQL, 83 (a twin in a test that the bottom-up rewrite had made a literal copy, which
  the visitor folds); comparing the operands as written, 116 (a literal copy on one side, folded, against its alias on
  the other). Rev 19 refuses 13,350 deletes that 3.11.0 ran: 12,803 that delete every row in C#, and 547 that don't, each
  a `>=` of an operand and its twin over values that can be NULL, as 3.10 refuses `x.N >= x.N`.
- **The self-check fuzz (rev 19).** Every other fuzzer on rev 19 (`run-rev19.sh`; labels `rev19` and `src`, rev 18's kept
  as `rev18`), against 3.11.0's baselines: fuzz5–fuzz9, fuzz12r and fuzz13r's TSVs byte-identical to rev 18's; fuzz10,
  fuzz10b, fuzz11, fuzz11r and `fuzz18s` with rev 18's counts, class by class: no wrong rows, no exception, no T-SQL
  regression, reads above 3.11.0's only through a cast through `object`. `ssdepth16.py` over every fuzzer: nothing above
  3.11.0's + 1, past 10 levels or past 2,100 parameters where 3.11.0 was within. Rounds 14–17's probes are rev 18's
  outputs but two delete probes and the timings: round 15's `x.NNonNull == x.NX || x.Flag == null` (`NNonNull` an `int` on
  the same column as `N`; 3.11.0 deleted C#'s 4 of 5) and round 16's `x.NameLen == x.Name.Length || x.Id == 2` (always
  true; 3.11.0 deleted every row) are refused as always true (§4.3). Round 18's probes (`R18.cs` against rev 19,
  `r18-*-rev19.txt`): `twins`, `json` and `msgs` (the SQLite provider's real delete) refuse every shape 3.11.0 refused and
  every one rev 18 refused, and the two neighbours delete C#'s rows; `provtext` calls `Classify` and the text check
  without a provider, so T1, T3 and L2 stay accepted in all four dialects there: the providers' deletes ask the SQL
  (`TheGuard_RefusesANestedComputedTwin_WithEveryProvidersOperandSql` with each provider's visitor; the keys on all four).
- **Green (the working tree, rev 19, 2026-10-23 04:10 CT):** the four full suites with coverlet, on the final code, after
  the guard's mutation rerun and a full rebuild: SQLite 1,461 / 1,461, SQL Server 3,390 / 3,391 (the one NotExecuted is
  `Projection_BoolValue_GetsCSharpsValue`, allowed), PostgreSQL 1,995 / 1,995, MySQL 1,937 / 1,937; the TRX gate passes on
  each (`nullsem-final19`). The harness 297 / 297 per provider (299 on SQLite with the delete tests); the core tests 53 /
  53 on net8.0 and net48; the guard class 429 / 429. On the same code, `fuzz18s` keeps rev 18's counts and `fuzz18t`
  finds no miss. Coverage per touched file: `NullSemantics.cs` 99.9% (rev 4's guard line left), `WherePredicateGuard.cs`
  98.9% (every rev 19 line has hits; the 10 left are the large-stack fallback's), the providers' data files 89.9–95.2%
  (each `OperandSql` covered), the visitors 95.5–95.8%, the translators 99.6%, the LINQ providers 94.4–95.5%,
  `RowIndependentEvaluator.cs` 100%.
- **Rev 18 (round 17's answers; the test redesigned first).** Each new test was red first, at rev 17's code: round 17's
  frozen tree with rev 18's test files copied in (`rev17-redcopy`, `red-rev17-final-*.txt`; the first run, before the
  self-check's rows, is `red-rev17-*.txt`: 14 of 440, 3 and 3):
  - **the DB-free classes failed 20 of 446:** 16 `Classify_ReturnsTheVerdict` rows, which rev 17's verdict accepted
    (`condSelfCopy`, `condAliasCopy`, `nestedCondAliasCopy`, `notCondAliasCopyNe`, `condAliasCopyOrId2`, `arithmeticSelf`,
    `nAliasSelf`, `boolAliasSelf`, `templateTwinSelf`, `templateAliasSelf`, `objBoolSelf`, `objAliasSelf`,
    `condSelfGeNullable`, `condSelfGeNotNull`, `notCondSelfLtNullable`, `idToStringFormatSelf`),
    `Rewrite_AComputedPropertyOperand_CountsItsTemplatesLevels` (the R17-4 and R17-6 rows),
    `ValidateTranslation_OfLongClauses_StaysLinear` (52 s against its 2 s: rev 17's pattern was quadratic), and the two
    guard rows whose operand holds `IS NULL AND` (accepted);
  - **the SQLite harness failed 8 of 295:** `nestedCondSelfCopy`, `nestedCondAliasCopy` and `nBothEqNBothX` by their
    deletes (accepted, every row), `activeEqActiveXOrFlagNull` by its delete (accepted), and `activeEqActiveXNeFalse`,
    `nToStringEqNXToStringOrNNull` and `falseNeObjNameLengthSelfLe` (twice, with `VerdictChanges_AsListed`) by their
    messages (self-referencing at rev 17, always true now);
  - **the SQL Server harness failed the same 8 of 294.**
  The six parser rows added last (a padded clause, `'x''y'` under the `IS NULL`, an unbalanced clause, a doubled group,
  an `OR` of a comparison, a bare group) pass at rev 17 too (`red-rev17-final2-guard.txt`: the guard class fails the
  same 19 of 386), because rev 17's pattern gave the same answers: they pin the parser's paths, not a finding.
  `SqliteComputedPartSelfReferenceTests`' R17-5 row passes at rev 17, which refused it already.
- **The rev 18 harness at `25956e0`** (the worktree, the harness and `SqliteComputedPartSelfReferenceTests` copied in):
  294 tests per provider (295 on SQLite). Failed / passed: SQLite 233 / 62, SQL Server 222 / 72, PostgreSQL 234 / 60,
  MySQL 233 / 61 (`red21-*.trx`). Round 17's keys: `nestedCondSelfCopy` and `nestedCondAliasCopy` fail on all four by
  their deletes, which 3.11.0 accepted: its text gives each copy's constants their own parameters, so the two sides'
  texts differ (§4.3's any-operand row: accepted where the text didn't show the copy); `nBothEqNBothX` passes (3.11.0
  refused it: its text has no parameter); `activeEqActiveXOrFlagNull` fails by its delete (accepted at 3.11.0, refused
  now, §4.3); the moved messages fail as 3.11.0 failed them (accepted on SQLite and MySQL; SQL Server's and
  PostgreSQL's syntax errors for `activeEqActiveXNeFalse` and `falseNeObjNameLengthSelfLe`); the SQLite delete test
  fails by its query's rows (3 of 5).
- **Mutations (rev 18):** 167, each applied once to the tree, its killers built and run, and the code files restored
  byte for byte (`code-md5-*.txt`). All killed: rev 17's 156 that stay (D38 withdrawn with the pattern; D28, D34, D39,
  D40, D41, D42, D44 and M-N16 re-anchored) and D48–D58. D54 survived the first run, because the timing test's long
  clauses never reached the witness loop; a 64,000-witness clause was added, and D54 was killed in its own run
  (`mutations-r18-d54.md`). D58 (no cycle guard) crashes the test host with a stack overflow, recorded as a kill: the
  runner records any aborted run as one (R17-7). The run came before the last code change, the removal of
  `SelfEqualityOperand`'s unreachable trailing-whitespace loop (no behaviour changes). Every anchor still matches the
  final code (`check_anchors21.py`), and the 29 mutants that edit the guard ran again on the final code, each killed, the code files restored byte for byte (`mutations-r18-final-guard.md`, `code-md5-guard-*.txt`).
- **net48 (rev 18):** MSBuild exit 0, no errors, the DLL written during that build; `NullSemanticsTests` 53 / 53.
- **The self-comparison fuzzer (rev 18, new).** `fuzz18s` (`Fuzz18S.cs`, `an18s.py`; seeds 501–516, 24,000 shapes)
  compares an operand with a copy of itself, literal or through aliases, under each of its wrappers, through the real
  SQLite provider's query and its delete (rolled back), against 3.11.0's. At rev 17: 733 deletes that 3.11.0 refused
  ran (373 over seeds 501–508; R18-7). At rev 18: none, and no wrong rows; its 574 query errors and 574 delete errors are 3.11.0's too. Rev 18 refuses
  10,413 deletes 3.11.0 ran: 10,315 that delete every row in C# (always true, R5-9), and 98 that don't, each a `>=` or
  `<=` of an operand and its copy over values that can be NULL (C# skips the NULLs), refused as 3.10 refuses
  `x.N >= x.N` (§4.3's any-operand row). 113 refusals change their message.
- **The self-check fuzz (rev 18).** Every other fuzzer on rev 18 (`run-rev18.sh`; labels `rev18` and `src`, rev 17's
  kept as `rev17`), against 3.11.0's baselines: no wrong rows, no exception, no T-SQL regression, and reads above
  3.11.0's only through a cast through `object`, as at rev 17. `ssdepth16.py` over every fuzzer: nothing above 3.11.0's
  + 1, past 10 levels or past 2,100 parameters where 3.11.0 was within. Against rev 17, the new refusals: more always
  true deletes (`fuzz10` 2, `fuzz10b` 3, `fuzz11` 16, `fuzz11r` 15 and 16), and deletes where C# doesn't select every
  row (`fuzz10` 1, `fuzz10b` 2, `fuzz11` 5, `fuzz11r` 4 and 3), each holding a `>=` or `<=` of an operand and itself or
  its alias (`x.N >= x.N`, `x.N <= x.NX`, `x.Name.Length <= x.Name.Length`), refused as always true because the
  self-comparison sits under another operator (§4.3). Round 15's and 16's delete probes move the same way:
  `x.Active == x.ActiveX || x.Flag == null` and `x.Code == x.CodeX || x.N == null` (3.11.0: every row deleted) are
  refused as always true, `nested(N) == nested(NX)` with parameter-free tests (rev 17: every row deleted) as
  self-referencing, and `x.N.ToString() == x.NX.ToString() || x.N == null`'s message moves to always true. Rounds 14–16's
  other probes are byte-identical to rev 17's, except the timings (`regex`: rev 18's parser is faster). Round 17's
  probes (`R17.cs`, `R17b.cs` against rev 18, `r17-*-rev18.txt`): `guard` (the SQLite provider's real delete) refuses
  every shape 3.11.0 refused, and 5 it ran, and its four neighbours (`K3`, `K7`, `N1`, `N2`: not self-comparisons)
  delete C#'s rows (3.11.0: fewer, for three); `verdicts` refuses 12 more, its neighbour deleting C#'s rows; `text`'s 8
  rows that 3.11.0's text check accepted are refused. `provtext` reads the text check alone, and accepts K1 and K2 (a nested conditional against its copy and its
  alias's) in all four dialects: the verdict refuses them before translation (`nestedCondSelfCopy` and
  `nestedCondAliasCopy` refuse on all four providers), so no delete reaches that text.
- **Green (the working tree, rev 18, 2026-10-23 01:03 CT):** the four full suites with coverlet, on the final code,
  after the mutation run and a full rebuild: SQLite 1,457 / 1,457, SQL Server 3,343 / 3,344 (the one NotExecuted is
  `Projection_BoolValue_GetsCSharpsValue`, allowed), PostgreSQL 1,992 / 1,992, MySQL 1,934 / 1,934; the TRX gate passes
  on each (`nullsem-final18`). The harness 294 / 294 per provider (295 on SQLite with the delete test); the core tests
  53 / 53 on net8.0 and net48; the guard class 386 / 386. Coverage per touched file: `NullSemantics.cs` 99.9% (rev 4's
  guard line left), `WherePredicateGuard.cs` 98.7%, the visitors 95.5–95.8%, the translators 99.6%, the LINQ providers
  94.4–95.5%, `RowIndependentEvaluator.cs` 100%.
- **Rev 17 (round 16's answers).** Each new test was red first, at rev 16's code: round 16's frozen tree with rev 17's
  five test files copied in (`rev16-redcopy`, `red-rev16-final-*.txt`; the first run is `red-rev16-*.txt`):
  - **the DB-free classes failed 7 of 421:** the growth test (`NX` in the innermost test: 4,063 nodes four levels down,
    331,303 eight down), `Rewrite_AComputedPropertyOperand_CountsItsTemplatesLevels` (rev 16 kept EF's form against
    every computed property, `NPlusOne`'s first), `TheGuard_RefusesAComputedPartAgainstTheColumnsPart_InEveryProvidersText`
    (SQL Server's `x.NameLen == x.Name.Length` accepted first), and the four R16-1 guard rows (accepted);
  - **the SQLite run failed 2 of 292:** `SqliteComputedPartSelfReferenceTests` (the delete accepted; its first form ran
    the delete outside a transaction, which FunkyORM refuses, and was corrected before this run) and `nxChainInTest7`
    (16,398 parameters);
  - **the SQL Server harness failed 1 of 291,** `nxChainInTest7`, with "a maximum of 2100 parameters".
  The rows added after the first run pass at rev 16, which reached their answers another way: the string and seven-deep
  rows of `Rewrite_ANullableIntegersToString_CountsAsACase` and the sibling templates (killers of D45–D47), and the
  R16-5 keys (rev 16 refuses them already).
- **The rev 17 harness at `25956e0`** (the worktree, the harness and `SqliteComputedPartSelfReferenceTests` copied in):
  291 tests per provider (292 on SQLite). Failed / passed: SQLite 230 / 62, SQL Server 219 / 72, PostgreSQL 231 / 60,
  MySQL 230 / 61. Round 16's keys fail on all four: `nxChainInTest7` and `notNameLengthNeNameXLength` by rows (3.11.0
  dropped the NULL rows), `nToStringEqNXToStringOrNNull` by its delete (accepted, every row), and the SQLite delete test
  by its query's rows (3 of 5) (`red20-*.trx`).
- **Mutations (rev 17):** 157, each applied once to the final tree, its killers built and run, and the code files
  restored byte for byte (`code-md5-*.txt`). All killed: rev 16's 144 that stay, D29, D34, D36 and D38 re-anchored, D32
  withdrawn with `ReadsRemote`, D39 and D40 (R16-1's second witness), D41–D46 (the template count), D47 (the COALESCE
  holding its operand). D29, D42–D47 are killed by the core tests alone, D39 by the guard rows alone, D41 by the core
  tests and `nestedCondNeFlat8CodeCase`, D38 and D40 by the guard rows, the provider texts and the SQLite delete.
- **net48 (rev 17):** MSBuild exit 0, no errors, the DLL written during that build; `NullSemanticsTests` 53 / 53.
- **The self-check fuzz (rev 17).** Every fuzzer on rev 17 (`run-rev17.sh`; labels `rev17` and `src`, rev 16's kept as
  `rev16`), against 3.11.0's baselines: no wrong rows, no exception, no T-SQL regression; reads above 3.11.0's only
  through a cast through `object` (fuzz10 4, fuzz10b 22, fuzz11 121, fuzz12r 1). `ssdepth16.py` over every fuzzer:
  nothing above 3.11.0's + 1, past 10 levels or past 2,100 parameters where 3.11.0 was within. Against rev 16, the alias
  twins distribute again, so `fuzz11`, `fuzz11r`, `fuzz12r` and `fuzz13r` return to rev 15's verdicts and texts: the 22
  always-true deletes rev 16 accepted are refused by their literal fold again (8 of `fuzz11`'s, 14 of `fuzz13r`'s; 3.11.0
  accepted them, §4.3's always-true class), `fuzz11r`'s one translates again (`1=0`, C#'s rows), and 11 of `fuzz13r`'s
  that 3.11.0 and rev 15 sent as invalid T-SQL (`NOT` on a `bit`, §3) are invalid again. R14-3's text check refuses the
  same 219 deletes that 3.11.0 accepted, all always true in C#. Round 14's chain probes and round 15's but `regex`
  (timings) and `hidden` (its plain-member rows now distribute, at 3.11.0's nesting) are byte-identical to rev 16's.
  Round 16's probes (`R16.cs` against rev 17, `r16-*-rev17.txt`): `del` and `provtext` refuse every R16-1 shape in every
  provider's text, as 3.11.0 did; `chain` is linear (157 parameters at k = 8; rev 16 101,685; 3.11.0 43), at 3.11.0's
  CASE nesting + 1; `plus2`'s Q5 is the + 2 of R16-4 (3.11.0 4, rev 17 6).
- **Green (the working tree, rev 17, 2026-10-22 22:44 CT):** the four full suites with coverlet, after the mutation run
  and a full rebuild: SQLite 1,454 / 1,454, SQL Server 3,309 / 3,310 (the one NotExecuted is
  `Projection_BoolValue_GetsCSharpsValue`, allowed), PostgreSQL 1,989 / 1,989, MySQL 1,931 / 1,931; the TRX gate passes
  on each. The harness 291 / 291 per provider (292 on SQLite with the delete test); the core tests 53 / 53 on net8.0 and
  net48. Coverage per touched file: `NullSemantics.cs` 99.9% (rev 4's guard line left; every rev 17 line has hits),
  `WherePredicateGuard.cs` 98.6%, the visitors 95.5–95.8%, the translators 99.6%, the LINQ providers 94.4–95.5%,
  `RowIndependentEvaluator.cs` 100%.
- **Rev 16 (round 15's answers and the owner's "A").** Each new test was red first, at rev 15's code. The final record
  is round 15's frozen tree with rev 16's three test files copied in (`rev15-redcopy`, `red-rev15-final-*.txt`):
  - **the DB-free classes failed 9 of 415:** the growth test (`(S(j) != L) ? Code : 5`: 3,616 nodes four levels down,
    297,376 eight down), `Rewrite_AComputedPropertyOperand_KeepsEFsForm` (distributed against `NPlusOne`),
    `Rewrite_ANullableIntegersToString_CountsAsACase` (distributed against `x.N.ToString()`), and six guard text rows:
    the two-witness form over a column the operand reads (four: accepted), `(t.active = t.active OR t.flag IS NULL)` and
    `(t.name = t.name OR t.n IS NULL)` (refused);
  - **the SQLite harness failed 7 of 288:** `nestedCondChainNestedLeft7` by its parameters (25,140), `nestedStrNeFlat8NToString`
    and `nestedCondNeFlat8CodeCase` by their CASE nesting, `nameLengthEqNameXLength`, `nameXLengthEqNameLength` and
    `dYearEqDXYear` by their deletes (accepted, every row), `activeEqActiveXOrFlagNull` by its delete (refused);
  - **the SQL Server harness failed the same 7,** the chain with "a maximum of 2100 parameters" and the two nesting
    keys with Msg 125 (`Case expressions may only be nested to level 10`).
  On the way: the growth test's first run at rev 15 crashed the test host with a stack overflow, in the compiled
  delegate of that rewrite (the rewrite itself returned), so the test now checks the size before it compiles anything;
  the `COALESCE` count came after the self-check found it (below), its tests red first at rev 16's code without it
  (`red-tostring-*.txt`: the core test, SQLite's CASE nesting 10 against 9, SQL Server's Msg 125); and R15-1's key, first
  eight levels deep, failed SQL Server's `Any()` path at the ruled + 1 (below), so it is seven.
- **SQL Server's own count** (measured on `.\SQL2019`, constant SELECTs, `coalesce-limit.ps1`): k CASEs nested in
  `THEN` around an innermost value run to k = 10 and fail at 11; with a `COALESCE`, `IIF` or `NULLIF` innermost they
  fail at 10 (`COALESCE(COALESCE(…))` at 9), with `ISNULL` at 11. The probe `dbg16`'s texts, run as WHERE clauses over
  a one-row derived table (`run-where.ps1`): the ToString shape (9 nested conditionals against an 8-deep flat one ending
  in `x.N.ToString()`) runs at 3.11.0 and failed with Msg 125 at rev 16 before the count, runs after it; R15-1's chain
  runs at k = 8 (10 CASEs; 3.11.0 9) and fails at k = 9 (11; 3.11.0's 10 runs), the ruled + 1. The harness's `Any()`
  path (`CASE WHEN EXISTS (…)`) failed the eight-level key with Msg 125 at the ruled + 1, so the key is seven levels deep.
- **The rev 16 harness at `25956e0`:** the worktree at `19e7441`, only the harness copied in: 288 tests per provider. Failed / passed: SQLite 226 / 62,
  SQL Server 216 / 72, PostgreSQL 228 / 60, MySQL 227 / 61. Round 15's keys: `nestedCondChainNestedLeft7`,
  `nestedCondNeFlat8CodeCase` and `activeEqActiveXOrFlagNull` pass on all four (3.11.0's rows, nesting and delete
  verdict: pins, red at rev 15); `nestedStrNeFlat8NToString`, `nameLengthEqNameXLength`, `nameXLengthEqNameLength`
  and `dYearEqDXYear` fail by rows on all four (3.11.0 dropped the NULL rows: `1,3` against C#'s `1,2`, and
  `1,3,5,6` against every row) (`red19-*.trx`).
- **Mutations (rev 16):** 149, each applied once to the final tree, its killers built and run, and the code files restored byte for
  byte (`code-md5-*.txt`). All killed: rev 15's 138 that stay, D27–D29 re-anchored (the guard's witness check, the
  counter's new method), D5 back with D30 (R15-1's order), D32 (R15-3), D34, D35 and D38 (R15-2 and R15-4's witness
  check), D36 and D37 (the `COALESCE` count). D36's first form left an `if` without its statement and didn't
  compile; rewritten (the count's level dropped), it ran alone on the same code (`mutations-r16-d36.md`) and its two
  tests failed. D29, D35 and D37 are killed by the DB-free tests alone (D35 by the near-miss guard row), D5, D30, D32
  and D36 by a core test and one harness key each, D34 and D38 by the guard rows and the harness.
- **net48 (rev 16):** MSBuild exit 0, no errors, the DLL written during that build; `NullSemanticsTests` 53 / 53.
- **The self-check fuzz (rev 16).** Every fuzzer on rev 16 (`run-rev16.sh`; labels `rev16` and `src`, rev 15's kept as
  `rev15`), against 3.11.0's baselines: no wrong rows, no exception, no T-SQL regression; reads above 3.11.0's only
  through a cast through `object`, within the bound (fuzz10 4, fuzz10b 22, fuzz11 121, fuzz12r 1). `ssdepth16.py`,
  which counts the nesting as SQL Server does (a `COALESCE`, `IIF` or `NULLIF` is a level), over every fuzzer at rev 16
  and rev 15: nothing above 3.11.0's + 1, past 10 levels or past 2,100 parameters where 3.11.0's text was valid and
  within. Against rev 15, 24 delete verdicts move, each to 3.11.0's verdict or to a refusal of an always-true delete:
  8 of `fuzz11`'s and 14 of `fuzz13r`'s always-true deletes that rev 15's distribution let the literal fold refuse
  (`NOT 1=0`) are accepted again, as 3.11.0 accepted them, and now delete every row, C#'s answer (§4.3's
  always-true-once-NULLs-count class): they read an `[SqlExpression]` twin, so EF's form is kept; 1 of `fuzz8`'s is the
  other way (always true, now refused by the fold); and 1 of `fuzz11r`'s is refused by the translator again, as 3.11.0
  refused it (§3's both-branches bullet). 65 of `fuzz12r`'s and `fuzz13r`'s that 3.11.0 sent as invalid T-SQL (`NOT`
  on a `bit`) and rev 15 as valid are invalid again, for the same reason. R14-3's text check still refuses 219 deletes
  that 3.11.0 accepted, all always true in C#: 217 a column, or a part of one, against its alias under a `!` or compared
  with a `bool` value (§4.3's new row; R16-7: 213 under a `!`, 4 compared with `true`), 2 R10-3's
  cast through `object`. R15-4's narrowing moves no fuzzer verdict. Round 14's chain probes are byte-identical to rev
  15's. Round 15's probes (`R15.cs` against rev 16, `r15-*-rev16.txt`): `order` linear in both orders (k = 5: 89
  parameters, rev 15 2,421; k = 8: 140, rev 15 65,601), `hidden` at 3.11.0's CASE nesting at every depth (rev 15: + 2),
  `del` with 3.11.0's verdict on every alias shape but the negated ones (refused, §4.3), `text` refusing the R15-2 forms
  in SQLite's, SQL Server's and MySQL's text and accepting 3.11.0's own one-witness texts, `regex` at 72–657 ms on
  clauses of 0.12–0.62 million characters, `efchain` as rev 15 (the Known issue), `marks` and `n16` with C#'s rows.
- **Rev 15 (round 14's answers and the owner's ruling, the bound-checked distribution).** Each new test was red first,
  at rev 14's code (the round 14 frozen tree, rev 15's three test files copied in):
  - **the DB-free classes failed 5** (6 with rev 15's final test files, whose R14-1 shapes were added after this run;
    R15-7): `Rewrite_NestedConditionalsAgainstConditionals_GrowLinearly` (`(L != flat(j)) ? N
    : M`: 1,957 nodes four levels down, 160,717 eight down), `Distribution_ReadsTheOtherOperandOnce_…` (rev 14 kept EF's
    form against the conditional operand, unsubstituted), and the three guard text rows for EF's self-equality;
  - **the SQLite harness failed 5:** `nestedCondChainNN8` and `nestedCondChainNNRight8` with "too many SQL variables",
    and `nEqNX`, `notNNeNX`, `objNameLengthSelfEq` by their deletes (accepted, every row);
  - **the SQL Server harness failed 5:** the same keys, the chains with "The incoming request has too many parameters.
    The server supports a maximum of 2100 parameters." (round 14's UNVERIFIED #1, measured).
  The bound's EF-depth term was first left out, as possibly unreachable; `Rewrite_NestedComparisons_GrowLinearly`,
  `nestedCondNeOther` and `nestedNotCondNeOther` failed without it (AC-N16's predicate branches nest each level's
  operands deeper than written), so it stays, and D23 is its mutant.
- **The rev 15 harness at `25956e0`:** 281 tests per provider. Failed / passed: SQLite 222 / 59, SQL Server 212 / 69,
  PostgreSQL 224 / 57, MySQL 223 / 58. `nEqNX`, `notNNeNX` and `objNameLengthSelfEq` fail by rows on all four (3.11.0
  dropped the NULL rows); the two chain keys pass on all four (3.11.0's 25 parameters and C#'s rows).
- **Mutations (rev 15):** 141, each applied once to the final tree, its killers built and run, and the code and test
  files restored byte for byte (`code-md5-*.txt`). All killed: rev 14's 127 that stay, D2, D3 and D20 re-anchored on
  `DistributeWithinTheBound`, D4 back (rule 7's substitution is live again), D16–D18 for the bound, D23–D28 for its
  EF-depth term, the right-nested witnesses, the depth counter and R14-3's text check, and D29 from the coverage pass.
  The first full run (140) left D17 alive: the bound without its + 1 sends only the first nested level of a chain to
  EF's form (its operands carry no + 1 yet; every later level then distributes at its written depth), and nothing
  pinned that level's form. Two single-level shapes in `Rewrite_ACompensationOverANestedConditional_IsDistributed` (a
  nested conditional against a flat one, step 6 and rule 7, one CASE past written) now kill it (`mutations-r15-d17.md`);
  D29 has its own run (`mutations-r15-d29.md`). D3, D4, D17, D25 and D29 are killed by the core tests alone, D19 and D20
  by the harness alone, D27 and D28 by the guard rows and the harness. The timing proof failed only under Z34.
- **net48 (rev 15):** MSBuild exit 0, no errors, the DLL written during that build; `NullSemanticsTests` 51 / 51.
- **The self-check fuzz (rev 15).** Every fuzzer on rev 15 (labels `rev15` and `src`; rev 14's kept as `rev14`),
  against 3.11.0's baselines: no wrong rows, no exception, no T-SQL regression; reads above 3.11.0's only through a cast
  through `object`, within the bound (fuzz10 4, fuzz10b 22, fuzz11 121, fuzz12r 1). `depthplus.py`: nothing above
  3.11.0's + 1 in any fuzzer; `sslimits.py`: nothing past 2,100 parameters or 10 CASEs where 3.11.0 was within both.
  SQLite's variable limit: `fuzz11`'s seed 131 #239 (3.11.0: wrong rows) and 2 that pass it at 3.11.0 too; `fuzz10b`:
  none. The always-true deletes rev 14 accepted that 3.11.0 refused (`fuzz12r` 139, `fuzz13r` 141, R14-3) are refused.
  Round 14's chain probes (its `R14.cs`, built against rev 15 as `probe-src14`): `chain`, `chainnn`, `predchain` and the
  right-nested `x.M != L` and `!(x.M > L)` are linear, with C#'s rows and 3.11.0's CASE nesting + 1 (`chain` 116
  parameters and `chainnn` 152 at 11 levels, as rev 13; rev 14: 442,866 and 974,304).
- **Rev 14 (round 13's answers and the owner's rulings, "EF's form there" and "Document it").** Each new test was red
  first, at rev 13's code (the round 13 frozen tree, rev 14's three test files copied in):
  - **the DB-free classes failed 2:** `Rewrite_ANestedConditionalAgainstAConditional_TakesEFsForm` (`nested != flat(9)`:
    11 CASEs against the source's 9) and `Distribution_ReadsTheOtherOperandOnce_…` (rev 13 distributed against the
    conditional operand, so `intIds` sat in each branch, substituted, where EF's form sends it once);
  - **the SQLite harness failed 1,** `nestedCondNeFlat9` by its CASE nesting (11 against 9), and **SQL Server's 1,** the
    same key with Msg 125 (`Case expressions may only be nested to level 10`; round 13's UNVERIFIED #2, measured);
  - the R13-4 keys (`notNestedCondEqV`, `notNestedCondEqM`, `notNestedBoolCondEqFlag`), `idGt2EqLiftedIdGt2`,
    `nestedCondUpperANeA` and the guard row `boolOtherUnderUserCast` pass at rev 13: pins, and the killers of D19–D21
    (round 13's surviving V13-6, V13-4 and V13-8).
- **The rev 14 harness at `25956e0`** (the worktree, only the harness copied in): 276 tests per provider. Failed /
  passed: SQLite 219 / 57, SQL Server 209 / 67, PostgreSQL 221 / 55, MySQL 220 / 56. Round 13's keys:
  - the three R13-4 keys fail by rows on all four, and `notNestedBoolCondEqFlag` with 42804 on PostgreSQL;
  - `nestedCondUpperANeA` fails by rows on all four: 3.11.0 dropped the NULL names, and SQL Server's and MySQL's
    case-insensitive collations took `"A"` for `"a"` (rows `3`, against SQLite's and PostgreSQL's `3,5,6`; C#: `2,3,4,5,6`);
  - `idGt2EqLiftedIdGt2` fails by its delete on SQLite (accepted, every row), by rows on MySQL (none), and by syntax
    errors on SQL Server and PostgreSQL (42601);
  - `nestedCondNeFlat9` passes on all four: 3.11.0's rows agree on the seed, and its 9 CASEs are 3.11.0's.
- **Mutations (rev 14):** 133, each applied once to the final tree, its killers built and run, and the code and test
  files restored byte for byte (every `.cs`, `.csproj`, `.props` and `.targets` checksummed before and after,
  `code-md5-*.txt`; the documents were edited during the run). All killed: rev 13's 126 that stay (D2 and D3
  re-anchored on `Distributes`; D4 and D5 withdrawn, §6.4), D16–D21 for R13-1's rule and round 13's surviving V13-6,
  V13-4 and V13-8, and D22, written with the coverage pass's test (below; `mutations-r14-d22.md`). D3, D6, D9, D10,
  D17 and D22 are killed by the core tests alone, D19 and D20 by the harness alone, D13–D15 and D21 by the guard rows.
  The timing proof failed only under Z34, its mutant; in a D-only run made while the fuzzers loaded the machine it also
  failed under D14 (a timing flake there, not a kill: the guard rows killed D14).
- **net48 (rev 14):** MSBuild exit 0, no errors, the DLL written during that build; `NullSemanticsTests` 50 / 50.
- **The CASE nesting above 3.11.0's + 1 (AC-N17).** The round 13 reviewer's `depthplus.py` over every fuzzer's SQL Server
  text where 3.11.0's was valid T-SQL: none, in fuzz6 to fuzz11r, `vis8`, and round 12's `fuzz12r` (64,000) and round 13's
  `fuzz13r` (160,000; rev 13: 414, at +2 and +3), both now standing fuzzers (the reviewer's probe, built against rev
  14 as `probe-src13`). `sslimits.py`: nothing over 2,100 parameters or 10 CASEs where 3.11.0 was within both.
- **The self-check fuzz (rev 14).** Every fuzzer on rev 14 (labels `rev14` and `src`), against 3.11.0's baselines:
  - no wrong rows, no exception, no T-SQL regression against 3.11.0; reads above 3.11.0's only through a cast through
    `object`, within the bound (fuzz10 4, fuzz10b 22, fuzz11 121, fuzz12r 1);
  - SQLite's variable limit: `fuzz11`'s seeds 131 #239 and 143 #1108 (C# predicates of 1.1 and 1.0 million
    characters, SQL of 2.3 and 2.6 million), which 3.11.0 ran with wrong rows, and 2 that pass it at 3.11.0 too;
    `fuzz10b`: none (rev 13: `fuzz11` 9 and `fuzz10b` 2, R13-1's copied operands);
  - `fuzz11r` against `rev12a`: the same 5 refusals as rev 13;
  - against rev 13, every change is a return to 3.11.0's behaviour in a shape 3.11.0 also refused or rejected: 137
    SQL Server texts that rev 13's distribution had made valid are invalid again, as at 3.11.0 (EF's form keeps a NOT
    on a `bit`, §3), and 10 the other way; 39 deletes that rev 13 refused by their text are accepted again, as 3.11.0
    accepted them, each always true in C# once NULLs count (the guard's Known issue, §4.3's last row); one translation
    is refused again, as at 3.11.0 (`fuzz11r` m0-208 #1890, a `Contains` with a cast item); and one `fuzz11` predicate
    passes SQLite's limit (143 #1108) that rev 13 ran. These counts cover fuzz6 to `fuzz11r`; with `fuzz12r` and
    `fuzz13r` (whose rev 13 TSVs the round 14 reviewer made) they are 177, 13 and 52, and the `bit` NOT explains 159 of
    the 177 (R14-4).
- **Rev 13 (round 12's answers and the owner's ruling, "Distribute into branches").** Each new test was red first, at
  rev 12's code (the round 12 frozen tree, the new tests copied in):
  - **the DB-free classes failed 10:** `Rewrite_NestedComparisons_GrowLinearly` (the both-nullable families multiply),
    `Rewrite_NestedConditionalComparisons_KeepTheirCaseNesting` (the single CASE doubles the nesting),
    `Rewrite_ACompensationOverAFlatConditional_TakesEFsForm` and `Rewrite_ACompensationOverANestedConditional_IsDistributed`
    (the single CASE), `Rewrite_AWitnessUnderNotsOrCasts_IsKept` (its `IS NULL` finder, which the single CASE has none
    for), and the five R12-1 guard rows (`Acceptable` at rev 12). With rev 13's final tests, 12 (the round 13
    reviewer's run, R13-5): also `Distribution_ReadsTheOtherOperandOnce_…`, written after the self-check's over-read,
    and `Rewrite_ADistributedBranchAsDeepAsItsTest_AddsOneCaseLevel`, written after the self-review (§3; 3 CASEs
    against 2 at the first level);
  - **the SQLite harness failed 7:** the three R12-1 keys by their deletes (accepted, every row), `nestedCondTestNe8` and
    `nestedCondTestNotGt8` by their CASE nesting (16 against 8), and the two both-nullable keys. `nestedCondBothNullEq`
    failed only the first cut of its shape check, which counted `m`, the compared column; under the check as it stands
    (the inner level's count) it passes at rev 12, a pin, so 6;
  - **the SQL Server harness failed 6:** the three R12-1 keys, `nestedCondTestNe8` and `nestedCondTestNotGt8` with
    Msg 125 (`Case expressions may only be nested to level 10`), and `nestedCondBothNullNe` by its repeated inner level;
  - `activeEqActiveXNeFalse` passes at rev 12, refused as since rev 3: a pin of §4.3's twin row.
- **The rev 13 harness at `25956e0`** (the worktree, only the harness copied in): 270 tests per provider. Failed /
  passed: SQLite 214 / 56, SQL Server 204 / 66, PostgreSQL 216 / 54, MySQL 215 / 55. Round 12's keys:
  - `nestedCondTestNe8`, `nestedCondTestNotGt8`, `nestedCondBothNullNe` and `nestedCondBothNullEq` fail by rows on all
    four (3.11.0 dropped the NULL rows);
  - the three R12-1 keys fail by rows on SQLite and MySQL, by syntax errors on SQL Server, and on PostgreSQL with 42883
    (two) and 42601 (one) (R13-5), except
    `idGtCodeEqLongCasts` on SQLite, which passes (3.11.0's rows and its refusal: a pin there);
  - `activeEqActiveXNeFalse` fails by its delete on SQLite and MySQL (accepted, every row) and by syntax errors on SQL
    Server (`Incorrect syntax near '!'`) and PostgreSQL (42601).
- **Mutations (rev 13):** 128, each applied once to the final tree, its killers built and run, and the tree restored byte
  for byte (every file of the working tree checksummed before and after, `tree-md5-*.txt`). All killed: rev 12's 113
  that stay (Y22 re-anchored; Z48, Z50 and Z51 on AC-N14's two-valued code; M-N43 and M-N44 on `SameValue`) and D1–D15
  for the distribution and R12-1's guard rule; the 14 on rev 12's single CASE (Z38–Z46, Z43s, Z49, Z53–Z55) are
  withdrawn with it. D3 to D6, D9 and D10 are killed by the core tests alone, D13 to D15 by the guard rows. After the
  run only `Distribute`'s summary comment changed (the CASE nesting's +1, `after-mutation-run.diff`).
- **net48 (rev 13):** MSBuild exit 0, no errors, the DLL written during that build; `NullSemanticsTests` 49 / 49.
- **The SQL per nesting level and the CASE nesting (rev 13).** `grow11`'s eight forms, eight levels deep, SQLite's text
  length per level, rev 12 → rev 13: the two both-nullable forms (§3) 405 … 1,366,511 and 256 … 63,680 → 405 … 2,763
  and 256 … 2,275, linear now; a conditional against a value 182 … 1,376 → 239 … 1,027; against a column that can't be
  null, and `!a != b`, 178 … 1,336 and 241 … 1,864 → 235 … 1,890 and 234 … 1,889; a negated relational 174 … 1,304 →
  171 … 1,256; the AC-N14 forms unchanged (76 … 496, 135 … 992). `Rewrite_NestedComparisons_GrowLinearly`'s eleven
  forms, two nested sides included, send `x.M` as often four and eight levels down. `depth12` (Probe.exe): families H,
  I and J nest the source's CASEs, 3.11.0's, at every depth (rev 12: twice as many). The `dbg13` probe found the one
  shape that nests one more: a distributed branch that is a conditional as deep as the test
  (`((… != 5) ? (x.Id > 3 ? (x.Id > 4 ? x.N : x.M) : x.M) : x.M) != 5`: 4 against 3.11.0's 3), once, not per level
  (`Rewrite_ADistributedBranchAsDeepAsItsTest_…`).
- **The self-check fuzz (rev 13).** Every fuzzer on rev 13 (label `rev13`), against 3.11.0's baselines. Its TSVs equal
  those of the prototype before the cleanup (`hyb3`, which had rev 12's guard) but for delete verdicts: 87 lines over
  every fuzzer (R13-5; the 16 first counted here were `fuzz10b`'s, `fuzz11`'s and `fuzz11r`'s), 26 from accepted to
  refused, each of a delete 3.11.0 accepted and C# makes of every row, which R12-1's `SameValue` refuses, and 61 a
  refusal's reason only:
  - fuzz5 to fuzz9 and `vis8`, `fuzz10`'s 48,000: no wrong rows, no exception, no T-SQL regression; reads as rev 12;
  - `fuzz11`'s and `fuzz10b`'s 84,000 each: no wrong rows, no exception, no T-SQL regression; reads above 3.11.0's only
    through a cast through `object`, within the bound (121 and 22). 11 predicates pass SQLite's variable limit where
    3.11.0 ran them (rev 12: 1; the pure distribution's first cut, which repeated every leaf: 23), C# predicates of
    55,662 to 1,143,328 characters whose SQL runs to 1.69 to 20.2 million (R13-5): `fuzz11`'s 9, 8 of which 3.11.0 answered with wrong rows and one, seed 122 (193,654 characters), with
    C#'s; and `fuzz10b`'s 2, both wrong at 3.11.0. 2 more pass it at 3.11.0 too. The one delete newly refused that C#
    doesn't make of every row is §4.3's class, a column compared with itself under a cast through `object`
    (`LENGTH(name) <= LENGTH(name)`), as at rev 12;
  - `fuzz11r`, 36,000 per mode: no wrong rows. Against the `rev12a` build (rev 11), 5 predicates it translated with
    C#'s rows are refused, each with 3.11.0's `NotSupportedException` (a round trip through `object` in `ToString()` or
    a `Contains` item, in a value-position conditional whose known test still sends both branches, §3); rev 12: 7;
  - `sslimits.py` and `casedepth.py` over every fuzzer's SQL Server text: no predicate passes 2,100 parameters or 10
    CASE levels where 3.11.0's text was valid and within both;
  - `dbg13b`: a nested conditional under an operator or a call (`+ 1`, `??`, `.Length`, `.ToString()`, string `+`):
    C#'s value, and each refused by the visitor as 3.11.0 refused it (its "bad 1" is the probe scoring two C#
    `NullReferenceException`s as a difference, R13-5).
- **Rev 12 (round 11's answers and the owner's rulings).** Each new test was red first:
  - **R11-1, R11-2, R11-4, R11-6, at the rev 11 code:** the DB-free classes failed 2 (`Classify_ACastKeepsTheOperandsKind`,
    `Rewrite_ANullTest_UnderAnOddNumberOfNots_…`), and the SQLite harness 7 (the six round-trip keys by their rows,
    and `ReadOnce` by the re-decided form's second read). `castNotCondCapturedBranch`,
    `idGt4AndCastNotCondCapturedBranch` and `flagEqCondWitnessFolds` pass at rev 11 and fail under W11-2 and W11-5.
  - **R11-5, at that code (EF's forms, the compensation repeated):** the DB-free classes failed 2
    (`Rewrite_NestedComparisons_SendTheInnermostOperandOnce`: `x.M` sent 64 times six levels down;
    `Rewrite_ACompensationOverAConditional_SendsItOnce`: `… != 5 OrElse … == null`), and the SQLite harness 6 (the five
    nested keys by `m`'s count, 8 or 12 against 1, and `flagEqNGt5` by its missing `CASE`); with the six expansion tests
    updated, 9 DB-free tests fail at that code (R12-4). `notCondGtPredicateValue`
    and `condNeFlagBothNull` pass there: rule 7's repeated witness holds no nested level, and the second is EF's form.
    The lazy-collection tests were repointed at an operand the SQL still repeats (M-N24, M-N38, M-N39, M-N45 and Y1 survived the old tests, whose predicate is now sent once, and fail the new ones).
- **The rev 12 harness at `25956e0`** (the worktree, only the harness copied in): 262 tests per provider. Failed /
  passed: SQLite 207 / 55, SQL Server 196 / 66, PostgreSQL 208 / 54, MySQL 207 / 55. Round 11's keys:
  - the six round-trip keys fail by rows on all four (3.11.0 dropped the NULL rows);
  - `castNotCondCapturedBranch` fails by rows on SQLite, PostgreSQL and MySQL and is dialect-rejected on SQL Server;
    `idGt4AndCastNotCondCapturedBranch` passes on all four, and `flagEqCondWitnessFolds` on SQLite, SQL Server and MySQL
    (PostgreSQL: 42883): pins;
  - the seven R11-5 keys and `flagEqNGt5` fail on all four, by rows, by SQL shape (`m` once, a `CASE` per level), or by
    SQL Server's syntax errors and PostgreSQL's 42601/42883;
  - `nullEqLiftedCastNotCond` passes on SQL Server, dialect-rejected before and after (§3);
  - `ReadOnce_CountingGetter` stops at its 3.11.0 row `c.Flag != x.Flag` (PostgreSQL: 42804).
- **Mutations (rev 12):** 126, each applied once to the final tree, its killers built and run, and the tree restored byte
  for byte (the code files checksummed before and after). All killed: 110 of round 11's 111 (M-N14b, M-N28,
  Z20 and Z21 re-anchored; M-N37 withdrawn) and Z38–Z55 (Z41–Z44 re-anchored after `!a != b`'s type check went).
  Z43 also ran against the SQL Server harness (Z43s): `nestedNotCondNeOther` and `notActiveNeCastCondFolded` fail by
  T-SQL's rejection. The first full run, before the lazy-collection tests moved, left M-N24, M-N38, M-N39, M-N45 and
  Y1 alive (§11). The timing proof failed only under Z34, its mutant.
- **net48 (rev 12):** MSBuild exit 0, no errors, the DLL written during that build; `NullSemanticsTests` 45 / 45.
- **The SQL per nesting level (`grow11`, R11-5).** Each comparison form nested eight levels deep around `x.M > 3`,
  SQLite's text length per level, rev 11 → rev 12:
  - a conditional against a value (`(a ? (bool?)e : null) != true`): 239, 692, 1,604 … 60,110 → 182, 350, 521 … 1,376;
  - a column against a predicate, a predicate against a predicate: 117 … 30,245 and 130 … 28,796 → 76 … 496 and
    135 … 992;
  - a conditional against a column that can't be null, and `!a != b`: 235 … 58,325 and 234 … 58,070 → 178 … 1,336 and
    241 … 1,864;
  - a negated relational over a conditional: linear in both (171 … 1,256 → 174 … 1,304);
  - two operands that can both be null (§3): unchanged, 405 … 1,366,511 for `!=` and 256 … 63,680 for `==`, as EF's
    forms are.
- **The self-check fuzz (rev 12).** Every fuzzer on rev 12, against 3.11.0's baselines:
  - fuzz5 to fuzz9 and `vis8`, `fuzz10`'s 48,000 and `fuzz10b`'s 84,000: no wrong rows, no exception, no T-SQL
    regression; reads as rev 11 (fuzz10 4, fuzz10b 22, each through a cast through `object` and within the bound);
  - `fuzz11`'s 84,000: no wrong rows, no exception, no T-SQL regression; 121 reads above 3.11.0's, each through a cast
    through `object` and within the bound. 3 predicates pass SQLite's variable limit (rev 11: 10): 2 that did at
    3.11.0, and seed 131 #239 (1.1 million characters), which 3.11.0 ran with wrong rows (48 of C#'s 96), now with
    40,123 parameters against its 23,975. None of rev 11's 3 whose rows 3.11.0 answered right is left: 125 #2185 sends
    19,030 parameters (rev 11: 48,914; 3.11.0: 17,576), 140 #2901 8,146 (6,649), 143 #1264 7,066 (4,491);
  - round 11's `fuzz11r`, 36,000 per mode (control and mixed): no wrong rows. Every read above 3.11.0's with no cast
    through `object` is a predicate 3.11.0 refused (its count stopped where it threw). Against the `rev12a` build (rev 11
    with round 11's other answers, before R11-5; against round 11's frozen tree the counts are the reviewer's 17, 122 and
    175, R12-4): no read above
    it; 7 predicates rev 11 translated with C#'s rows are refused, as 3.11.0 refused them (§3: a branch the translator
    refuses, in a value-position conditional whose test is known), and 2 that rev 11 refused now translate, with C#'s
    rows. One delete that C# doesn't delete every row for is newly refused against 3.11.0, as it was before R11-5:
    `(x.N >= x.NX) != false`, whose twin's SQL is `{N}`, so its text is `n >= n`, which the guard's text check reads as
    a column compared with itself (3.11.0 accepted it).
  - every TSV of the final tree equals the prototype's byte for byte (fuzz5 to `fuzz11r`, the prototype being the code
    before the last cleanup: the dead `Substitute`, the `spine` flag, the type check in `!a != b`).
- **Rev 11 (round 10's answers).** Each new test was red first, at the rev 10 code. The DB-free classes failed 6: the
  `NullReferenceException` (`Rewrite_KeepsCSharpsValue`, `Rewrite_AWitnessWhoseNullableBranchFolds_…`), the cast leaf
  left as it was (two tests), `(bool)x.Flag` classified as able to be null, and the fold's walk (81 ms against 2.2 ms
  for the same chain of row operands). The SQLite harness failed 6: the four R10-1 keys and `nullEqLiftedCastNotCond`
  by their rows, and `computedCondWitnessNeTrue` by the exception. The R10-3 keys pass at rev 10, which already saw
  through the cast: they are pins against 3.11.0.
- **The rev 11 harness at `25956e0`** (the worktree, only the harness copied in): 246 tests per provider. Failed /
  passed: SQLite 193 / 53, SQL Server 184 / 62, PostgreSQL 193 / 53, MySQL 193 / 53. Round 10's keys:
  - the four R10-1 keys fail by rows on SQLite, PostgreSQL and MySQL (3.11.0's test unrewritten: row 4 in, or out under
    `!`), and are dialect-rejected on SQL Server;
  - `nullEqLiftedCastNotCond` fails by rows (every row; `NOT CASE … IS NULL`), SQL Server's syntax error and
    PostgreSQL's 42804;
  - `objCodeSelfAndIdEq2` fails by SQL shape (`code = code`), and `idEq2OrObjCodeSelf` and `falseNeObjNameLengthSelfLe`
    because 3.11.0 accepted their deletes (SQL Server and PostgreSQL: their errors for the second);
  - `computedCondWitnessNeTrue` passes on SQLite, SQL Server and MySQL (3.11.0's rows, which rev 10 lost to the
    exception) and fails on PostgreSQL by 42883.
- **Mutations (rev 11):** 107, each applied once, its killers built and run, and the tree restored byte for byte. All
  killed: rev 10's 101 (M-N1, Z27 and Z28 re-anchored) and Z30–Z35. Z20, Z21 and Z22 are now killed by the core tests
  only: under them the harness keys keep C#'s rows, since rule 9 and the re-decided form answer those shapes without
  the step each mutant removes.
- **net48 (rev 11):** MSBuild exit 0, no errors, the DLL written during that build; `NullSemanticsTests` 42 / 42.
- **The redesigned fuzzer (rev 11, `fuzz11`).** `fuzz10`'s wrappers, plus wrappers whose `bool?` can hold null by type
  but never does (`h.T ? q : h.NB`, `h.F ? h.NB : q`, and round 10's `(bool)(t ? (bool?)e : (h.T ? (bool?)e : h.NB))`),
  and `[SqlExpression]` twins of `Active` and `Other` (`ActiveX`, `OtherX`: the columns' values, sent through the
  visitors' remote map, nullable as far as FunkyORM knows). 84,000 cases (24 seeds, half with wrappers inside
  wrappers):
  - **at rev 10:** 5,548 wrong-row predicates (45 that 3.11.0 answered right) and 5 SQLite variable-limit errors (1
    that 3.11.0 answered right; restated on rev 11's basis, R11-6), 45 `NullReferenceException`s, 886
    predicates reading more than 3.11.0 (549 beyond AC-N13's bound), 1 newly refused delete C# doesn't delete every row
    for;
  - **at rev 11:** no wrong rows, no exception, no T-SQL regression. 121 predicates read more than 3.11.0, every one
    through a cast through `object` and within AC-N13's bound. The newly refused deletes (2,947) are always true in
    C#, but one: `false != (bool)(object)(x.Name.Length <= x.Name.Length)`, a column compared with itself, as 3.11.0
    refused the shape without the cast (§4.3). The newly accepted (40) select C#'s rows. 10 cases are predicates of
    77,000 to 2,000,000 characters that pass SQLite's 32,766-parameter limit (8 that 3.11.0 ran; §3).
- **The self-check fuzz (rev 11).** Every earlier fuzzer on rev 11, against 3.11.0's baselines: fuzz5 to fuzz9 and
  `vis8` as in rev 10, `fuzz10`'s 48,000 and `fuzz10b`'s 84,000. No wrong rows, no exception, no T-SQL regression; the
  same reads as rev 10 (fuzz10 4, fuzz10b 22, each through a cast through `object` and within the bound). The reviewer's
  round 10 probes (`probe10r`, `probe10rg`, `probe10rd`) give C#'s rows; through the provider, `Query` is {1,2,3} and
  `Delete` leaves row 4.
- **The fold's cost (R10-4):** a left-deep `||` of `x.Id == 1` and captured `h.F`s, warmed: 1,500 terms fold in 4.6 ms
  and classify in 10.9 ms (rev 9: 2.9 and 5.8; rev 10: 38 and 136), and 2,400 terms in 8.6 and 11.9 ms: linear.
- **Rev 10 (round 9's answers).** Each new test was red first, at the rev 9 code. The DB-free classes failed 5: the
  `NullReferenceException` (`Rewrite_KeepsCSharpsValue`, `Rewrite_AWitnessUnderNotsOrCasts_IsKept`), the cast over
  a conditional and the cast through `object` left as they were, and the fold's read of the right operand of a
  false `&&`. The SQLite harness failed 10: two keys by the `NullReferenceException`, six by their rows, the hybrid's
  delete accepted, and `ReadOnce` by `c.Five`'s second read. `castLiftedCondKnownBranch` passes at rev 9, which
  already sent `ELSE NOT 1=0` (R9-4): it is a pin against 3.11.0's text.
- **The rev 10 harness at `25956e0`** (the worktree, only the harness copied in): 235 tests per provider. Failed /
  passed: SQLite 183 / 52, SQL Server 178 / 57, PostgreSQL 182 / 53, MySQL 183 / 52. Round 9's keys:
  - `notNotCondFoldedFlagNeTrue`, `notActiveNeCastCondFolded` and `nullEqNotObjNotFlag` pass on SQLite, SQL Server and
    MySQL: 3.11.0's rows, which rev 9 lost (the first two threw, the third selected the complement). The first two
    fail on PostgreSQL by 42883;
  - `castCondActiveFalse`, `castCondNNe5Active` and `castNotCondActiveFalse` fail by rows on SQLite, PostgreSQL and
    MySQL, and are dialect-rejected on SQL Server;
  - `castNotLiftedCondHybrid` fails because 3.11.0 accepted the delete (SQLite, MySQL; it deleted every row), and by
    SQL Server's "near '<'" and PostgreSQL's 42883;
  - `objLiftedNotNGt3EqTrue` fails by rows (SQLite, MySQL), SQL Server's "near '='" and PostgreSQL's 42601;
  - `castLiftedCondKnownBranch` fails by SQL shape (`ELSE @p__linq__1`), dialect-rejected on SQL Server;
  - `ReadOnce_CountingGetter` stops at its 3.11.0 row `c.Flag != x.Flag` (PostgreSQL: 42804).
- **Mutations (rev 10):** 101, each applied once, its killers built and run, and the tree restored byte for byte
  (checksummed: only the two documents edited meanwhile differ). All killed: rev 9's 91 (M-N37, M-N51, M-N53 and Y14
  re-anchored to the moved code) and Z20–Z29.
- **net48 (rev 10):** MSBuild exit 0, no errors, the DLL written during that build; `NullSemanticsTests` 39 / 39.
- **The metamorphic fuzzer (rev 10, `fuzz10`; the test redesign).** Each case is a predicate from one of the earlier
  generators (fuzz5's, `Pred6`, `Pred7`, `Top8`, `Top9`) with random `bool` and `bool?` parts wrapped in a form C#
  evaluates the same: `(bool)(bool?)e`, `!!e`, `(bool)!(bool?)!e`, `(bool)(object)e`, `(bool)(t ? (bool?)e : (bool?)e)`,
  `t ? e : e`, `e ? true : false`, `e == true`, `(bool?)e == true`, `e || false` and `true && e` with captured values,
  `!(e == false)`, nested conditionals; for `bool?`, `!!q`, `(bool?)(object)q` and conditionals with equal branches.
  The tests `t` read the row, read a captured value, or fold once rewritten. The wrapped predicate's C# value is
  checked against the unwrapped one's (no difference in any run). 48,000 cases (12 seeds), and a wider sweep of
  84,000 (24 seeds, half with wrappers inside wrappers):
  - **at rev 9:** 3,056 wrong-row predicates (22 that 3.11.0 answered right), 121 `NullReferenceException`s, 207
    predicates reading more than 3.11.0 (43 with no cast through `object`), and 10 T-SQL regressions;
  - **at rev 10:** no wrong rows, no exception, no T-SQL regression. Reads: 4 of the 48,000 and 22 of the 84,000 read
    more than 3.11.0, every one through a cast through `object`, and none more than 3.11.0 reads for the same predicate
    with its casts through `object` taken out (AC-N13's bound). Every newly refused delete (1,497 and 2,779) is always
    true in C#; every newly accepted one (30 and 64) selects C#'s rows.
- **The self-check fuzz (rev 10).** Every earlier fuzzer on rev 10, against 3.11.0's baselines: round 5's 36,000,
  round 6's 32,000, round 7's 96,000 and 64,000, round 8's `fuzz8` 96,000 and `vis8` 20,000, and round 9's `fuzz9`
  60,000 (rev 9: 353 exceptions, 308 wrong-row predicates, 14 over-reads). No wrong rows, no read beyond 3.11.0's, no
  T-SQL that parsed at 3.11.0 and doesn't now. AC-N11 over `fuzz9`'s 12,028 shapes whose operands can't be null:
  9,978 send 3.11.0's text, and the rest differ only by (a), (d), (h) and (i), with C#'s rows.
- **Delete verdicts on one basis (R9-5).** Every refusal counted, over the six earlier fuzzers (fuzz5 to fuzz9,
  384,000 predicates): newly refused against 3.11.0, 13,541 at rev 9 and 13,966 at rev 10, each always true in C#.
  Rev 9 → rev 10: 336 accepted → refused, each always true in C# (casts now seen through, whose null value folds);
  264 refused or untranslated → accepted, each with C#'s rows (most were rev 9's `NullReferenceException`). Rev 8's
  "894 (900)" counted only refusals where 3.11.0's rows were right; on this basis round 9 counted 11,212 for rev 8's
  five fuzzers.
- **The self-check fuzz (rev 9).** Every fuzzer so far on rev 9, against round 8's 3.11.0 baselines (fuzz5's complete
  this time; R8-5): round 5's 36,000, round 6's 32,000, round 7's 96,000 and 64,000, round 8's `fuzz8` 96,000, and
  `vis8`'s 20,000 visitor texts. No wrong rows, no read beyond 3.11.0's, no T-SQL that parsed at 3.11.0 and doesn't now.
  Every newly refused delete is always true in C#: 11,667, nearly all folded to `NOT 1=0` by the rewrite, the rest a
  `bool` compared with the same operand (R7-5). Every newly accepted delete selects C#'s rows: the dropped-null class
  (§4.3) and R8-2's `!(x.Flag == !x.Flag)` class. 8 of them delete every row, C#'s answer, being disjunctions the
  guard can't prove always true (§3).
- **The self-check fuzz (rev 8).** All four fuzzers on rev 8, against 3.11.0's baselines (round 5's 36,000, round 6's
  32,000, and round 7's two, 96,000 and 64,000): no wrong rows, no read beyond 3.11.0's, no T-SQL that parsed at
  3.11.0 and doesn't now. 894 newly refused deletes (900 with fuzz5's full baseline; R8-5), each always true in C#: 770 that the rewrite folds to
  `NOT 1=0` (a null value against a column that can't be null, a predicate against a null `bool?`), and the rest
  a `bool` compared with the same operand (R7-5).
- **Mutations (rev 8):** 86, each applied once, its killers built and run, and the tree restored byte for byte. All
  killed: rev 7's 66, round 7's mutants and M-N49/M-N50. M-N50 needed its killer named: the SQLite harness reaches
  no shape for it, and `WhereVisitorDirectTests` (all four visitors) kills it.
- **net48 (rev 8):** MSBuild exit 0, no errors, the DLL written by that build; `NullSemanticsTests` 34 / 34.
- **Green (the working tree, rev 16, 2026-10-22 20:57 CT):** the four full suites with coverlet, on the final tree after
  the mutation run and a full rebuild: SQLite 1,450 / 1,450, SQL Server 3,300 / 3,301 (the one NotExecuted is
  `Projection_BoolValue_GetsCSharpsValue`, allowed), PostgreSQL 1,986 / 1,986, MySQL 1,928 / 1,928; the TRX gate passes on
  each. The harness 288 / 288 per provider; the core tests 53 / 53 on net8.0 and net48. Coverage per touched file:
  `NullSemantics.cs` 99.9% (the line left is rev 4's non-`bool` guard in `IsCompensatedWhenNegated`; every rev 16 line
  has hits), `WherePredicateGuard.cs` 98.6%, the visitors 95.5–95.8%, the translators 99.6%, the LINQ providers
  94.4–95.5%, `RowIndependentEvaluator.cs` 100%.
- **Green (the working tree, rev 15, 2026-10-22 18:46 CT):** the four full suites with coverlet, on the final tree:
  SQLite 1,443 / 1,443, SQL Server 3,284 / 3,285 (the one NotExecuted is `Projection_BoolValue_GetsCSharpsValue`,
  allowed), PostgreSQL 1,979 / 1,979, MySQL 1,921 / 1,921; the TRX gate passes on each. The harness 281 / 281 per
  provider; the core tests 51 / 51 on net8.0 and net48. Coverage per touched file: `NullSemantics.cs` 99.9% (the line
  left is rev 4's non-`bool` guard in `IsCompensatedWhenNegated`; every rev 15 line has hits), `WherePredicateGuard.cs`
  98.6%, the visitors 95.5–95.8%, the translators 99.6%, the LINQ providers 94.4–95.5%, `RowIndependentEvaluator.cs`
  100%. The first SQL Server run (under `first-run\`) left the depth counter's lambda skip unrun: the distribution test
  gained a shape with a lambda in the other operand, which D29 fails, and the SQL Server suite (where the core tests
  run) and net48 were rerun green on it, after a full rebuild (the D29 run had left its mutant's build in `bin`).
- **Green (the working tree, rev 14, 2026-10-22 00:41 CT):** the four full suites with coverlet, on the final tree:
  SQLite 1,438 / 1,438, SQL Server 3,271 / 3,272 (the one NotExecuted is `Projection_BoolValue_GetsCSharpsValue`,
  allowed), PostgreSQL 1,974 / 1,974, MySQL 1,916 / 1,916; the TRX gate passes on each. The harness 276 / 276 per
  provider; the core tests 50 / 50 on net8.0 and net48. Coverage per touched file: `NullSemantics.cs` 99.9% (the line
  left is rev 4's non-`bool` guard in `IsCompensatedWhenNegated`; every rev 14 line has hits), `WherePredicateGuard.cs`
  98.6%, the visitors 95.5–95.8%, the translators 99.6%, the LINQ providers 94.4–95.5%, `RowIndependentEvaluator.cs`
  100%. The first run left one line no test ran, the right operand's flat conditional marked in a leaf (only the left
  was, since R13-1 made two nested sides EF's): `Distribution_…_AndDoesntRewriteALeafAgain` gained the mirrored case
  (`5 != nested`), which D22 fails; the SQL Server suite (where the core tests run), net48 and the SQLite harness were
  rerun green on it (the first run is under `first-run\`).
- **Green (the working tree, rev 13, 2026-10-21 21:12 CT):** the four full suites with coverlet, on the final tree:
  SQLite 1,432 / 1,432, SQL Server 3,263 / 3,264 (the one NotExecuted is `Projection_BoolValue_GetsCSharpsValue`,
  allowed), PostgreSQL 1,968 / 1,968, MySQL 1,910 / 1,910; the TRX gate passes on each. The harness 270 / 270 per
  provider; the core tests 49 / 49 on net8.0 and net48. Coverage per touched file: `NullSemantics.cs` 99.9% (the line
  left is rev 4's non-`bool` guard in `IsCompensatedWhenNegated`; every rev 13 line has hits), `WherePredicateGuard.cs`
  98.6% (every `SameValue` line has hits), the visitors 95.5–95.8%, the translators 99.6%, the LINQ providers
  94.4–95.5%, `RowIndependentEvaluator.cs` 100%. After the run, four test comments changed (the harness's R11-5 comment and three in
  `NullSemanticsTests`, which still described the single CASE); the DB-free classes (396) and the SQLite harness (270)
  were rerun green on them.
- **Green (the working tree, rev 12, 2026-10-21 17:26 CT):** the four full suites with coverlet, on the final tree:
  SQLite 1,424 / 1,424, SQL Server 3,244 / 3,245 (the one NotExecuted is `Projection_BoolValue_GetsCSharpsValue`,
  allowed), PostgreSQL 1,960 / 1,960, MySQL 1,902 / 1,902; the TRX gate passes on each. The harness 262 / 262 per
  provider; the core tests 45 / 45 on net8.0 and net48. Coverage per touched file: `NullSemantics.cs` 99.9% (the line
  left is rev 4's non-`bool` guard in `IsCompensatedWhenNegated`), `WherePredicateGuard.cs` 98.5%, the visitors
  95.5–95.8%, the translators 99.6%, the LINQ providers 94.4–95.5%, `RowIndependentEvaluator.cs` 100%. Two runs came
  first: one failed six SQL Server keys, and the next, green, left `NullSemantics.cs` at 99.1% (both in §11).
- **Green (the working tree, rev 11, 2026-10-21 07:20 CT):** the four full suites with coverlet, on the final tree:
  SQLite 1,408 / 1,408, SQL Server 3,224 / 3,225 (the one NotExecuted is `Projection_BoolValue_GetsCSharpsValue`,
  allowed), PostgreSQL 1,944 / 1,944, MySQL 1,886 / 1,886; the TRX gate passes on each. The harness 246 / 246 per
  provider; the core tests 42 / 42 on net8.0 and net48. Coverage per touched file: `NullSemantics.cs` 99.9% (the line
  left is rev 4's non-`bool` guard in `IsCompensatedWhenNegated`; every rev 11 line has hits), `WherePredicateGuard.cs`
  98.5% (every rev 11 line has hits), the visitors 95.5–95.8%, `RowIndependentEvaluator.cs` 100%. After the run, two
  documentation comments on `Classify` changed; the DB-free classes and the SQLite harness were rerun green on them.
- **Green (the working tree, rev 10, 2026-10-21 05:30 CT):** the four full suites with coverlet, on the final tree:
  SQLite 1,397 / 1,397, SQL Server 3,209 / 3,210 (the one NotExecuted is `Projection_BoolValue_GetsCSharpsValue`,
  allowed), PostgreSQL 1,933 / 1,933, MySQL 1,875 / 1,875; the TRX gate passes on each. The harness 235 / 235 per
  provider; the core tests 39 / 39 on net8.0 and net48. The first run failed one SQL Server test,
  `ReadOnce_CountingGetter`: two of its R9-3 rows are shapes T-SQL rejects before and after (a `bool` `CASE` as a
  condition, a predicate as a `CASE` result), and the row ran the query outright. They now expect the database's
  rejection where `RejectedByTheDialect` names them (`readsCastCondCaptured`, `readsCondTestCastCond`), and their reads,
  made in the translation, still count. Coverage per touched file: `NullSemantics.cs` 99.8% (the line left is rev 4's
  non-`bool` guard in `IsCompensatedWhenNegated`; every rev 10 line has hits), `WherePredicateGuard.cs` 98.5%, the
  visitors 95.5–95.8%, the translators 99.6%, the LINQ providers 94.4–95.5%, `RowIndependentEvaluator.cs` 100%.
- **Green (the working tree, rev 9, 2026-10-21 03:42 CT):** the four full suites with coverlet, on the final tree:
  SQLite 1,387 / 1,387, SQL Server 3,195 / 3,196 (the one NotExecuted is `Projection_BoolValue_GetsCSharpsValue`,
  allowed), PostgreSQL 1,923 / 1,923, MySQL 1,865 / 1,865; the TRX gate passes on each. The harness 225 / 225 per
  provider; the core tests 36 / 36 on net8.0 and net48. Coverage per touched file: `NullSemantics.cs` 99.8% (the line
  left is rev 4's non-`bool` guard in `IsCompensatedWhenNegated`), `WherePredicateGuard.cs` 98.5%, the visitors
  95.0–95.8%, the translators 99.6%, the LINQ providers 94.4–95.5%, `RowIndependentEvaluator.cs` 100%.
- **Green (the working tree, rev 8, 2026-10-21 02:32 CT):** the four full suites with coverlet, on the final tree:
  SQLite 1,382 / 1,382, SQL Server 3,187 / 3,188 (the one NotExecuted is `Projection_BoolValue_GetsCSharpsValue`,
  allowed), PostgreSQL 1,918 / 1,918, MySQL 1,860 / 1,860; the TRX gate passes on each. The harness 220 / 220 per
  provider; the core tests 34 / 34 on net8.0 and net48. Coverage per touched file: `NullSemantics.cs` 99.7% (the two
  lines left are rev 4's and rev 5's non-`bool` guards in `IsCompensatedWhenNegated` and `IsPredicate`, which their
  callers now make unreachable), `WherePredicateGuard.cs` 98.5%, the visitors 95.0–95.8%, the translators 99.6%, the
  LINQ providers 94.4–95.5%, `RowIndependentEvaluator.cs` 100%.
- **Green (the working tree, rev 7, 2026-10-21 01:12 CT):** the four full suites with coverlet, on the final tree:
  SQLite 1,373 / 1,373, SQL Server 3,173 / 3,174 (the one NotExecuted is `Projection_BoolValue_GetsCSharpsValue`,
  allowed), PostgreSQL 1,909 / 1,909, MySQL 1,851 / 1,851; the TRX gate passes on each. The harness 211 / 211 per
  provider. Coverage per touched file: `NullSemantics.cs` 99.8% (the line left is `IsCompensatedWhenNegated`'s
  non-`bool` guard, from rev 4), `WherePredicateGuard.cs` 98.5%, the visitors 94.9–95.7%, the translators 99.6%, the
  LINQ providers 94.4–95.5%, `RowIndependentEvaluator.cs` 100%.
- **Green (the working tree, rev 6, 23:15 CT; R5-7):** the four full suites with coverlet: SQLite 1,358 / 1,358, SQL
  Server 3,141 / 3,142 (the one NotExecuted is `Projection_BoolValue_GetsCSharpsValue`, allowed), PostgreSQL
  1,894 / 1,894, MySQL 1,836 / 1,836, and the TRX gate script passes on each. The harness 196 / 196 per provider;
  the core tests 26 / 26 on net8.0 and net48. Coverage per touched file: `NullSemantics.cs` 97.4%,
  `WherePredicateGuard.cs` 95.8%, the visitors 94.0%, the translators 99.6%, the LINQ providers 94.4–95.5%,
  `RowIndependentEvaluator.cs` 100%.
- **Green (the working tree, rev 5):** the harness 185 / 185 on all four providers (the core tests 23 / 23); the core tests 18 / 18 on net8.0
  and net48; the four full suites green (SQL Server's one inconclusive is `Projection_BoolValue_GetsCSharpsValue`, on
  the allowlist).
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

Every flip is listed in the handoff with its old and new value. Any other broken pin is a regression. **Rev 5:** the
four full suites broke exactly these pins, and each is flipped as listed; `TranslatorDirectHarness` also gains the
negated rows (a `TranslateNegatedMethodCall` seam per provider).

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
    `ClassName.MethodName` isn't on the job's allowlist (R3-9). A result's `testId` is joined to
    `TestDefinitions/UnitTest/TestMethod`: `@className` is the **runtime class's full name** (an inherited harness
    test carries the derived class) and `@name` the method, without data-row arguments, whose format differs between
    MSTest versions (R4-7). So every allowlist entry is `<derived class full name>.<method>`. `[Ignore]` also gives
    NotExecuted; none exists today.
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
| 0 | Task 0, round 5 on rev 5 and the code | — |
| 1 | All new tests; the red run at `25956e0` (§6.5); the N11 pins captured; coverage baselines | pins |
| 2 | `NullSemantics`; the visitors' `Visit(lambda)` and `VisitConditional` | N1–N5, N11, N13, N14 |
| 3 | The translators' `negated` overload, null items, parentheses; the visitors' `VisitUnary(Not)` | N6–N8 |
| 4 | `All` through the visitor (four providers) | N9 |
| 5 | §7 flips; four suites; mutations; coverage | N10 |
| 6 | R1 `Skip` row; R3 TRX gate and checks | R1, R3 |
| 7 | Changelog, Usage.md, Advanced.md, the AI instructions; version 3.12.0 | N12, R5 |
| 8 | Adversarial review by two fresh non-author lenses over `25956e0..HEAD`, until CLEAN | — |

Pushing, the PR, merging and publishing are the owner's calls. The owner, 2026-10-23: "when finished & reviewed
clean, publish as v3.12": once Task 0 and Task 8 are CLEAN on the exact sha, the branch is pushed, the PR merged to
master (which publishes to NuGet once all four suites pass there) and tagged `v3.12.0`.

## 10. Risks and decisions

- **Wider rows and deletes, and the verdict changes (§4.3).** This is the reason for a minor version and a prominent
  Behavior change.
- **Nothing is newly rejected** (R3-4). The verdicts that change are §4.3's; the one new refusal kind is
  `p == p` as a self-reference (R4-4; owner-confirmed 2026-10-20).
- **D8 amended (AC-N8).**
- **SQL text grows** for `!=` and the negated forms on columns that can be null, every string column included.
  Equality doesn't change. No form repeats an earlier level but EF's, where distributing a nested conditional's
  comparison would pass AC-N17's bound (R13-1, R14-1, R16-2), so
  nesting grows the SQL linearly except there, where it multiplies about threefold per level: past SQL Server's 2,100
  parameters from about 6 levels (§3, R15-6).
- **A compensation over a nested conditional departs from EF's text** (distributed into its branches, not
  `… <> @p OR … IS NULL`), with EF's rows: the owner's ruling (Paul, 2026-10-21, "Distribute into branches"), so the
  SQL of nested conditionals grows linearly and keeps its CASE nesting, or one level more where a branch is as deep
  as its test (§3). Over a flat conditional, and where the bound refuses the distribution (R13-1, R14-1), the text is EF's.
- **AC-N11's exceptions (a)–(i).** The conditional literal applies only where a conditional's test, or a branch of a
  `bool` conditional in a truth position, doesn't read the row.
- **An integer's `ToString()` follows EF over a missing parent** (`''`, R4-10; owner-confirmed 2026-10-20), where C#
  would give the non-nullable property's default (`"0"`). The read-back Known issue (§3) is the same divergence.

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
| R2-5 | Major | TEST-GAP | No non-nullable inline attribute key; M-N10 untested on the translator path; de-dup unasserted | `nPlusOneIntNe6`, `childSumDNe99`; `notNamesContainsName`, `notParentKeysContainsParentKey`; `nameLengthSelf` text; M-N11's killers corrected; M-N15 |
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

### Task 0 round 4 (non-author, over `19e7441`, the harness and the red run, 2026-10-20 21:42): NOT CLEAN, 3 Major, 9 Minor, 2 Nit

13 of the 14 are wholly or partly fix-introduced. The reviewer read the plan; the code was in the working tree but
not part of the review. R4-1 and R4-2 describe the plan's text: the code already pushed `!` to a compensated call and
descended every row operand, so their dispositions bring the plan to the code and add the proofs.

| # | Sev | Blame | Finding | Rev 5 disposition |
|---|---|---|---|---|
| R4-1 | Major | TEST-GAP | A negated call beside a sibling that needs nothing stayed `NOT (a AND call)` (**fix-introduced** by rev 4's structure rule) | Rule 8: a negated call the translator compensates is a change (AC-N4); `notIdAndNameContainsA`, `notIdOrIdsContainsN`, `notIdAndNameContainsNull` (§4.3), the pin `notIdAndCodeEq5`; M-N3b. Red at `25956e0`: {1,3,4,5,6}, {1,5}, {1,3,4,5,6} |
| R4-2 | Major | AC-GAP | A conditional's test wasn't rewritten inside an uncompensated comparison, a `Convert` or a nested conditional (**fix-introduced** by R3-1's "reached") | §5.1: every row operand is descended; AC-N11's relational clause carved; `condTestNeGt0`, `condTestNeEqV`, `nestedCondTestNe` (red: {5,6}); M-N4e |
| R4-3 | Major | TEST-GAP | Named tests and entities were missing (`VerdictChanges_AsListed`, `NsReverse`, §4.3 rows 4, 5 and 9, `docKEqNone`, the remote delete pins, the `c.Flag` read, `Holder.Dt`) | All added and red-run; `Holder.Dt` dropped from §6.1 (the core test has the `DateTime`) |
| R4-4 | Minor | AC-GAP | AC-N14 made two unlisted verdict changes: a predicate against a null `bool?`, and `p == p` (**fix-introduced**) | Both in §4.3 and §3; `nameContainsANeNullBool`, `nameContainsAEqNullBool`, `nEq5EqNEq5`; the verdict calls `p == p` a self-reference (a new structural check); M-N16 |
| R4-5 | Minor | PLAN-GAP | §4.3 and §3 said `x.Req != noName` deleted the non-null rows; it deleted nothing; `reqNeNoName` mislabeled a pin (**fix-introduced** by R3-6) | Rows split; `reqNeNoName` a proof |
| R4-6 | Minor | PLAN-GAP | M-N13's DB killers can't see a dropped `Convert`: the log prints values, not types (**fix-introduced** by R3-3) | The real killers cited: `Substitution_KeepsTheConvertChain` and `colorNeCapturedColor`'s twin |
| R4-7 | Minor | PLAN-GAP | The allowlists were bare method names; TRX's `className` is the runtime class (**fix-introduced** by R3-9) | §8: entries are `<derived class full name>.<method>`, joined on `TestMethod@name` |
| R4-8 | Minor | TEST-GAP | The expansion's read rule had no DB row (**fix-introduced**) | Three ReadOnce rows, one with Flip |
| R4-9 | Minor | PLAN-GAP | AC-N12 "Changed" and the label omitted 42601, 42804 and SQL Server's captured-bool test | Extended; `condCapturedBoolTest` |
| R4-10 | Minor | PLAN-GAP | §2's `Parent.Code.ToString()` row wasn't EF's (**fix-introduced** by R3-10) | Followed EF: `COALESCE(…, '')` wherever the value can be NULL (4 translators, `Classify`); the probe gains the row; `parentCodeToStringEqEmpty` (red: {} vs {3,6}); M-N17 |
| R4-11 | Minor | TEST-GAP | AC-N11 (a)'s `bool`-branch literal had no key (**fix-introduced** by R3-8) | `boolBranchCapturedLiteral` (`THEN NOT 1=0`) |
| R4-12 | Minor | TEST-GAP | The attribute entities skipped paths | `EveryPath` is generic; `NsComputed` takes every path; the remote reads gain QueryAsync |
| R4-13 | Nit | AC-GAP | AC-N14's predicate list left out a `Convert` of one; a folded side against a column was undefined | AC-N14 amended (the code already stripped the lift and fell back to rule 6) |
| R4-14 | Nit | PLAN-GAP | Stale text (§10's "new rejections", §9's round, "one exception", AC-N5, §1, `allNGt5Text`, (b)'s `!= null`, (a)–(c), `nameContainsAEqTrue`'s label, `nameContainsNullEqFalse`'s row) | Each fixed |

### Task 0 round 5 (non-author, over rev 5 and the code, 2026-10-20 23:00): NOT CLEAN, 1 Major, 8 Minor, 1 Nit

All 10 are wholly or partly fix-introduced. The reviewer fuzzed SQLite with 36,000 predicates against a lifted-C#
oracle: no wrong rows at rev 5 (3.11.0 is wrong on about a third).

| # | Sev | Blame | Finding | Rev 6 disposition |
|---|---|---|---|---|
| R5-1 | Major | AC-GAP | On SQL Server, AC-N14 moved a `bool` value (a conditional of columns or values, `!` of a bare `bool`) into a truth position, and `VisitConditional` sent a value-position branch as `NOT 1=0`: invalid T-SQL for shapes 3.11.0 ran | `IsPredicate` counts those as values (rule 6, 3.11.0's text); a branch is a truth position only when its conditional is; `active`/`other` NOT NULL bits and five keys, red at rev 5 on SQL Server; M-N19, M-N20 |
| R5-2 | Minor | TEST-GAP | A literal null against a conditional dropped the substituted test, so the visitor read again (**fix-introduced** by M-N18's change) | The return keeps the rewritten operand; a ReadOnce row and a core test; M-N21 |
| R5-3 | Minor | TEST-GAP | The new fold positions read a comparison of captured `bool`s twice | `FoldNode` evaluates an evaluable `bool` comparison once; ReadOnce and core rows; M-N22 |
| R5-4 | Minor | PLAN-GAP | "42804 now runs" held only where the comparison was compensated | Fixed, not narrowed: a value branch is substituted always (AC-N11 (g)); `condNullBranchEq5`/`Gt3`; M-N23 |
| R5-5 | Minor | TEST-GAP | Two branches had no killer (`Kept` dropping its `Not`; the second witness's `IS NULL`) | `notCondTestNeEq1`; `notNGtM` (added after the snapshot) and a core test; M-N4f, M-N4g |
| R5-6 | Minor | PLAN-GAP | AC-N11's exceptions and the Changelog missed `x.N > none` → `1=0` and a captured null on a column that can't be null; AC-N12 said (a)–(c) | (b) widened; (f) and (g); AC-N12 says (a)–(g); the Changelog lists them |
| R5-7 | Minor | HOUSE-RULE | No full run postdated the rev 5 code | The four full suites rerun on the rev 6 tree (§6.5) |
| R5-8 | Minor | TEST-GAP | The expansion enumerated a lazy collection per copy | Materialized once in `Substitute` where its declared type takes an array; the concrete-type limit in §3; M-N24 |
| R5-9 | Minor | AC-GAP | §4.3 missed a conditional whose test folds and `x.Id == 2 \|\| x.Id != none`; the "none refuses C#'s rows" claim was false | Rows added (`nGtNoneCondCode`, `idEq2OrIdNeNoneInt` in `VerdictChanges_AsListed`); the status line reworded |
| R5-10 | Nit | PLAN-GAP | "`==` against a value is unchanged" | "against a non-null value" |

**Found while implementing, before review:** a 1,500-term disjunction overflowed the stack (the existing
`EveryVisitor_FoldsThroughItsMemo` crashed the SQL Server test host, which hid 500 tests from one run): a left-deep
chain is now walked iteratively. SQLite's `decimal` binding (§3, `childSumDNe99`). A literal null rebuilt needlessly
(M-N18).

### Task 0 round 6 (non-author, over rev 6 and the code, 2026-10-20 23:56): NOT CLEAN, 4 Major, 4 Minor, 1 Nit

Seven of the nine are wholly or partly fix-introduced, five of them by R5-1's change, which made `!m`, `!c` and a
conditional of values "values" that nothing downstream handled. Per the house rule (two fix-introduced findings in a
row: redesign, then code), rev 7 doesn't patch each one. It gives a `bool` compared with a `bool` one model (§5.1
rules 5 and 6; AC-N14 to AC-N16), and fuzzed it with the reviewer's own tools before asking for round 7.

| # | Sev | Blame | Finding | Rev 7 disposition |
|---|---|---|---|---|
| R6-1 | Major | TEST-GAP | On SQL Server, `!` over a value-position conditional sent its branches as literals: invalid T-SQL for a shape 3.11.0 ran (**partly fix-introduced**) | The visitor visits a `!`'s operand in the `!`'s own position, and in a value position doesn't fold a conditional or a `!` to a literal (§5.3); `notBoolCondEqTrue`, `notNboolCondLiteralNeFalse`, `ANotInAValuePosition_KeepsTheConditionalsBranchesValues` (the exact text, through the rewrite and the visitor's own); M-N33, M-N33s, M-N34 |
| R6-2 | Major | AC-GAP | Beside a predicate, `!m`, `!c` and a conditional with such a branch were "not accepted", so 3.11.0's text went out: wrong rows and deletes, and invalid T-SQL (**fix-introduced**) | Rule 5: a side that reads no row, or that the fold knows, is a value; `!`s over a row operand are moved (AC-N15); `!` of an operand has the operand's kind (§4.1); `nEq5EqNotActive`, `notActiveEqNNe5`, `nEq5EqNotCapturedFlag`, `flagNeNotCapturedFlag`, `nEq5EqCondNotOther`; M-N25, M-N28, M-N29, M-N31, M-N47, M-N48 |
| R6-3 | Major | TEST-GAP | A conditional whose truth the fold knew was substituted to a constant before its witness was taken: a `NullReferenceException` for a shape 3.11.0 ran (**fix-introduced**) | A side the fold knows is a value (rule 5.1), so no root becomes a constant under `Substitute`; `condFoldedEqFlag`, `Rewrite_ASideTheFoldKnows_IsAValue` (with the visitor's memo, as the NRE needed); M-N30 |
| R6-4 | Major | AC-GAP | `!` of a `bool?` column, and a `bool?` conditional's predicate branch, weren't compensated; `!x.Flag == null` deleted the complement (3.11.0's rows) | Fixed, not scoped out: AC-N15 and AC-N16, as EF sends them (§2); `notFlagNeTrue`, `notFlagEqNull`, `flagEqNotFlag`, `nboolCondPredicateBranchNeTrue`; M-N26, M-N27, M-N32, M-N40 |
| R6-5 | Minor | TEST-GAP | AC-N14's conditional column side was duplicated without substitution, a conditional that reads no row was a column, and a null `bool?` the fold read was read again (**fix-introduced**, plus a rev 5 residue) | The row operand is substituted (rule 5.5); a `bool` that reads no row is a value; the fold records its reads beside the memo (§5.3); four ReadOnce rows and `Reads_ANullBoolTheFoldRead_Once`; M-N36, M-N37 |
| R6-6 | Minor | TEST-GAP | `IsPredicate`'s new branches had no killer (**fix-introduced**) | `!` of a predicate: `notNEq5EqActive` (M-N42). The conditional case is gone: a conditional is never a predicate, and its predicate branches are AC-N16's (`condPredicateBranchEqActive`, M-N32) |
| R6-7 | Minor | PLAN-GAP | Usage.md and the AI instructions said "`==` against a value doesn't change"; the Changelog's literal branch had no position; an `IQueryable<T>` variable ran twice (**partly fix-introduced**) | "a non-null value" in both; the Changelog qualifies (a) and lists (h) and (i); an `IQueryable<T>` variable is materialized as an in-memory queryable (`ALazyQueryable_…`, M-N39), and the Known issue says so |
| R6-8 | Minor | TEST-GAP | Tests whose premise no longer held, and fix-layer lines no suite ran (**fix-introduced**) | (a) the visitor's own read is tested on a body, and the rewrite's binding compared with it; (b) two values are reachable where the fold knows one side and the other is a null `bool?`, and the test proves it (M-N46); (c) the concrete-type limit is gone (materialized against `Contains`'s parameter, or the cast's type), and the throwing collection is pinned (M-N38, M-N45); (d) `SameTree`'s call, static-call, mismatch, conditional and other-node cases have verdict rows (M-N43, M-N44) |
| R6-9 | Nit | OTHER | `ANewOperand_…` pinned an invalid `1=1 = 1=1`; §6.2 said 18 core tests (**fix-introduced**) | The empty case is gone; §6.2 says 32 |

**Found while implementing, before review (the self-check).** Round 6's two fuzzers, run on rev 7's first cut, found
four defects, each fixed with a test first:
1. `Classify` calling a `bool` that reads no row a value made an operand the rewrite folded to constants lose its
   witness: 4 wrong-row predicates, and deletes accepted with them. `Classify` stays by shape; the rule moved to the
   sides and branches (M-N41; four shapes in `Rewrite_KeepsCSharpsValue`).
2. `!a == b`'s `a <> b`, true in C# for one null, made the rewritten body differ from C#'s, though SQL's rows were
   right. Guards were added (M-N40).
3. `!` over a conditional whose test the rewrite folded sent `NOT 1=0` in a value position: 2 T-SQL regressions
   (`notCondFoldedTestNeOther`, M-N34).
4. A branch the fold knew was substituted, which read captured values the visitor would have folded away: 7
   predicates read once more than 3.11.0 (M-N35).

### Task 0 round 7 (non-author, over rev 7 and the code, 2026-10-21 02:03): NOT CLEAN, 2 Major, 4 Minor, 2 Nit

Round 6's fuzzers on the frozen tree reproduced the author's results byte for byte. The reviewer's own fuzzer for the
redesign's new shapes (160,000 predicates) found both Majors; they share one root.

| # | Sev | Blame | Finding | Rev 8 disposition |
|---|---|---|---|---|
| R7-1 | Major | TEST-GAP | `!` over a lifted predicate (`!(bool?)p`) was nothing: as a side, 3.11.0's text and rows (`!(bool?)(x.N > 3) == null` an accepted delete of the rows C# keeps); as a branch, the conditional not accepted | `AsPredicate`: a side or a branch is a predicate under its lifts, `!(bool?)p` read as `!p` (rule 5.1, AC-N14); `flagNeNotLiftedNGt3`, `notLiftedNGt3EqNull`, `notLiftedNameContainsANeFalse`, `condNotLiftedBranchNeTrue`, `Rewrite_NotOfALiftedPredicate_IsAPredicate`; M-N49 |
| R7-2 | Major | TEST-GAP | A value-position `!` skipped the fold over any unary node, so a known truth went out as parameters, read again (**fix-introduced** by R6-1's rule) | The fold is skipped only for a conditional under `!`s and casts (`EndsAtConditional`, §5.3); `ANotInAValuePosition_OverAKnownTruth_IsItsLiteral`, a ReadOnce row; M-N50, M-N50s |
| R7-3 | Minor | HOUSE-RULE | `Rewrite_NotOfAColumn_…` pinned `ToString()`, which .NET Framework prints without a cast's type; §6.5's "net48 32 / 32" was a stale binary (**fix-introduced**) | `Text()` drops the type; net48 rerun with the build's exit code and errors checked and the DLL's write time read (34 / 34). The house rule: a green claim names the build that made the binary |
| R7-4 | Minor | TEST-GAP | Seven redesign branches had no killer (**fix-introduced**) | One test each (§6.2's R7-4 row, and the core tests); Y6a, Y7, Y8, Y10, Y10s, Y15, Y22 and Y25 killed in §6.4 |
| R7-5 | Minor | AC-GAP | The self-reference verdict counts any `bool` compared with the same operand, not only a predicate (**partly fix-introduced**) | §3, §4.3, §5.3, the Changelog and the guard's comment say so; `condSelfOrCode5` in `VerdictChanges_AsListed`; the guard rows `notFlagSelfEq`, `notFlagSelfOrId2`. The code is rev 5's: each such delete is always true in C# |
| R7-6 | Minor | PLAN-GAP | The Changelog's Known issue dropped §3's limit (a lazy class with its own `Contains`) (**fix-introduced**) | Restored |
| R7-7 | Nit | OTHER | "takes EF's form" for `x.Flag == !x.Flag2` omitted the guards | "…with non-null guards beside it (the same rows)" |
| R7-8 | Nit | OTHER | Record items: §5.1's "(a)–(e)", `notNEq5EqActive` called a pin, §6.5's red counts, the self-check's cut | Each corrected |

### Task 0 round 8 (non-author, over rev 8 and the code, 2026-10-21 03:11): NOT CLEAN, 1 Major, 3 Minor, 1 Nit

The fix layer held where round 7 asked; every round 7 fuzzer reproduced the author's TSVs byte for byte. Round 8's own
fuzzer (`fuzz8`, 96,000) found R7-1's shape in a third position, under a cast back to `bool`.

| # | Sev | Blame | Finding | Rev 9 disposition |
|---|---|---|---|---|
| R8-1 | Major | AC-GAP | A cast back to `bool` over a lifted form (`(bool)!(bool?)p`, `(bool)(bool?)p` as a side) was a leaf: 3.11.0's wrong rows and deletes, and, with the translators' newer `Contains`, some rows 3.11.0 had right | Closed as a class: the `LiftNormalizer` (§5.1) puts every lift outermost before the rewrite, so the form is seen wherever it is; `AsPredicate` loses its special case. `castNotLiftedNGt3`, `castNotLiftedMGtNoneAndOnlyNull`, `castLiftedNGt3EqActive`, `Rewrite_ACastBackToBool_IsTheFormItself`; M-N49 (moved), M-N51 |
| R8-2 | Minor | AC-GAP | `!(x.Flag == !x.Flag)`: refused by 3.11.0, accepted now with C#'s rows, unlisted | §4.3 and the Changelog's table; `notFlagEqNotFlag` in `VerdictChanges_AsListed` (accepted) |
| R8-3 | Minor | TEST-GAP | `Substitute` read captured values the fold never reaches (under an `\|\|` it knows is true), and called a throwing getter (partly fix-introduced) | Below its root, the `Substituter` asks the fold first; its root is never replaced (R6-3). `Reads_NothingTheFoldSkips` (the throwing getter), a ReadOnce row (`c.A`), two root rows in `Rewrite_KeepsCSharpsValue`; M-N52, M-N53 |
| R8-4 | Minor | TEST-GAP | `EndsAtConditional`'s cast half had no killer (**fix-introduced**) | `ANotInAValuePosition_OverACastOverAConditional_KeepsItsForm` (the visitor's own, all four); Z6, Z6s |
| R8-5 | Nit | OTHER | Record items: fuzz5's 33,000-line baseline, two R7-4 keys labeled proofs, AC-N11 (h)'s 3.11.0 text | Each corrected |

### Task 0 round 9 (non-author, over rev 9 and the code, 2026-10-21 04:15): NOT CLEAN, 2 Major, 2 Minor, 1 Nit

Round 8's dispositions held except two. R8-1 was partial: a cast back to `bool` over a `bool?` conditional was still a
leaf. R8-3's fold-first introduced R9-1. Round 9's fuzzer (`fuzz9`, 60,000) found both. That made two fix-introduced
findings in a row (R8-4, R9-1), so rev 10 redesigned the test first: `fuzz10` checks every position of the cast class
(§6.5). Then it changed the code.

| # | Sev | Blame | Finding | Rev 10 disposition |
|---|---|---|---|---|
| R9-1 | Major | TEST-GAP | The rewrite threw `NullReferenceException` where a compensated comparison's witness sat under `!`s or a cast below the node the `Substituter` kept, and the fold knew it once rewritten (**fix-introduced**: R8-3's fold-first) | A row operand's substitution keeps the chain down to its witness, and the witness (`SubstituteOperand`, §5.1). `notNotCondFoldedFlagNeTrue`, `notActiveNeCastCondFolded`, `Rewrite_AWitnessUnderNotsOrCasts_IsKept`, four `Rewrite_KeepsCSharpsValue` rows (`!!w`, a cast over w, a lift over w, a nested branch); Z20, Z21 |
| R9-2 | Major | AC-GAP | A cast back to `bool` over a `bool?` conditional of lifts (or over `!` of one) was a leaf, and so was a cast through `object`: 3.11.0's wrong rows and deletes, and one shape 3.11.0 answered right regressed | The `LiftNormalizer` lifts a `bool?` conditional of lifts and takes out a cast through `object` (§5.1). A cast over a `bool?` that can be null stays (§3). `castCondActiveFalse`, `castCondNNe5Active`, `castNotCondActiveFalse`, `castNotLiftedCondHybrid` (§4.3), `objLiftedNotNGt3EqTrue`, `nullEqNotObjNotFlag`, two core tests; Z22–Z25. The status line and the Changelog's sentence are corrected |
| R9-3 | Minor | TEST-GAP | Reads beyond 3.11.0 (AC-N13) under a cast over a conditional of captured values (partly fix-introduced) | R9-2's lowering puts the branch in a truth position, so the three shapes read 3.11.0's counts. The fold stops at an operand of `&&`/`\|\|` that decides it, and asks one that reads no row first. AC-N13 states the one bound that differs, a cast through `object` (3.11.0's reads of the predicate without the cast). Three `ReadOnce` rows, two more for the fold, `Fold_StopsAtAnOperandThatDecides`; Z26–Z29 |
| R9-4 | Minor | AC-GAP | "The SQL is the same, since the visitors drop a cast" was false: the cast's conditional moved into a truth position (**fix-introduced**) | The `LiftNormalizer`'s comment and §5.1 are corrected; AC-N11 (a) and the Changelog name the case; `castLiftedCondKnownBranch` pins `ELSE NOT 1=0` (red at `25956e0`) |
| R9-5 | Nit | OTHER | §6.5's rev 8 and rev 9 refusal counts were on different bases | Rev 10's record states its basis (every refusal counted) and gives the rev 9 → rev 10 delta |

### Task 0 round 10 (non-author, over rev 10 and the code, 2026-10-21 06:32): NOT CLEAN, 2 Major, 2 Minor, 1 Nit

Round 9's R9-3 and R9-5 held; R9-1 and R9-2 were partial. Round 10's reviewer extended `fuzz10` by one wrapper and
probed a `[SqlExpression] bool`, and found both Majors, with tested prototypes. R10-2 was the second fix-introduced
finding in a row (after R9-1), so rev 11 changed the test before the code again (`fuzz11`, §6.5), and `fuzz11` found a
third class the round didn't name (a cast to a non-nullable type classified as able to be null).

| # | Sev | Blame | Finding | Rev 11 disposition |
|---|---|---|---|---|
| R10-1 | Major | AC-GAP | A cast back to `bool` over a `bool?` conditional with a branch that is neither a lift nor a non-null constant (a captured `bool?`) was a leaf in a truth position: its test wasn't rewritten, 3.11.0's wrong rows and deletes | §5.1 rule 9: such a cast is a row operand, its test rewritten, its branches values (the reviewer's prototype 3). And a cast to a non-nullable type asserts a value (§5.1, `fuzz11`'s third class). §3, the Changelog and AC-N13 corrected. Four harness keys with their deletes, `nullEqLiftedCastNotCond`, two core tests, `Rewrite_KeepsCSharpsValue` rows; Z30, Z31 |
| R10-2 | Major | TEST-GAP | A witness whose only branch that can be null (a `[SqlExpression]` bool) folds once rewritten threw `NullReferenceException` (**fix-introduced**: R9-2's conditional lift) | The kind is taken again after the substitution: step 5 drops the `IS NULL`, step 6 re-decides its form (the reviewer's prototype 4, without its unused locals). `computedCondWitnessNeTrue` (`NsComputed` gains `Active` and `ActiveX`), `Rewrite_AWitnessWhoseNullableBranchFolds_…`, `Rewrite_KeepsCSharpsValue` rows; Z32, Z33 |
| R10-3 | Minor | AC-GAP | Seeing through a cast through `object` exposed known and self-comparing parts outside a conditional: text changes outside (a)–(i), and unlisted refusals (**fix-introduced**) | AC-N11 (a) generalized to any part a cast hid; a §4.3 and Changelog row (refused as the shape without the cast was in 3.11.0). `objCodeSelfAndIdEq2` (text pin), `idEq2OrObjCodeSelf`, `falseNeObjNameLengthSelfLe` (verdict pins) |
| R10-4 | Minor | TEST-GAP | `FoldLogical`'s `IsEvaluable(Left)` walked the whole left spine at every level: quadratic on a chain of captured operands (**fix-introduced**) | The operand asked first is a small one that reads no row (32 nodes at most, counted only up to the bound), so each level costs a bounded amount. `Fold_OfALongChainOfCapturedOperands_StaysLinear` (red at rev 10: 81 ms against 2.2 ms); Z34, Z35 |
| R10-5 | Nit | OTHER | The rev 10 `fuzz10` TSVs were labeled `protoB`; AC-N13's "never read under it"; `&`/`\|` and "reads no row" unstated | `r10-selfcheck/fuzz10/rev10-*` copies; AC-N13 and §5.1 reworded |

### Task 0 round 11 (non-author, over rev 11 and the code, 2026-10-21 08:10): NOT CLEAN, 1 Major, 4 Minor, 1 Nit

Round 10's R10-3, R10-4 and R10-5 held; R10-1 and R10-2 were partial (R11-1, R11-2, R11-4). The reviewer's `fuzz11r`
replaced the nullable columns of `fuzz11`'s predicates with EF-equivalent round trips over `[SqlExpression]` twins
whose SQL value IS NULL, which `fuzz11`'s twins never were, and found R11-1. Round 11 was the third round in a row
whose fix layer introduced findings (R9-1, R10-2, R11-1 and R11-2), so rev 12 took the test first again: `fuzz11r` is
a standing fuzzer, and `grow11` measures every comparison form's SQL per nesting level (§6.5). R11-5 went to the
owner, who ruled twice (Paul, 2026-10-21): **"Adopt EF's linear form"**, and, when EF's form left the conditionals'
`IS NULL` repeats, **"Go beyond EF"**.

| # | Sev | Blame | Finding | Rev 12 disposition |
|---|---|---|---|---|
| R11-1 | Major | AC-GAP | Rev 11's cast assertion was false for a property whose CLR type can't hold null but whose SQL value can: `(int)(int?)x.ParentCode != 5` lost the missing parents, `!((int)(int?)x.ParentCode > 3)` its NULL rows, and EF keeps both (**fix-introduced**: rev 11's answer to `fuzz11`'s third class) | Dropped, the reviewer's option 1 (EF's): a cast keeps the operand's kind, and step 3 tests the operand under an odd number of `!`s (NOT NULL is NULL), the root of `fuzz11`'s third class. §3, §4.1, §5.1 and the Changelog corrected. Six round-trip keys (`NsRemote`, `NsComputed`), `Classify_ACastKeepsTheOperandsKind`, `Rewrite_ANullTest_UnderAnOddNumberOfNots_…`; Z36. Z31 withdrawn with the assertion |
| R11-2 | Minor | TEST-GAP | The re-decided Kept sent the operands from before the substitution: a captured value read twice, a lazy sequence enumerated twice (**fix-introduced**: R10-2) | It sends the substituted operands (the reviewer's P2). `ReadOnce` rows (the re-decided `==`, a two-sided Kept, a lazy `Contains`); Z37 |
| R11-3 | Minor | AC-GAP | The assertion's verdict and row changes were unlisted, and departed from EF (`(int?)(int)x.N != none` refused; **fix-introduced**) | Resolved with R11-1: with the assertion gone, the rows are EF's and the verdicts 3.11.0's |
| R11-4 | Minor | TEST-GAP | Rule 9 through a `!` (W11-2) and the re-decided `==` with one side that can be null (W11-5) had no killer (**fix-introduced**) | `castNotCondCapturedBranch`, `idGt4AndCastNotCondCapturedBranch` (with their deletes), `flagEqCondWitnessFolds`; W11-2 and W11-5 killed |
| R11-5 | Minor | AC-GAP | The Known issue's account was false: 3 of `fuzz11`'s 10 oversized cases had C#'s rows at 3.11.0, and EF sends each operand once (partly **fix-introduced**: rule 9 exposed the expansions) | The owner's rulings: AC-N14 compares two-valued `CASE`s (EF's form), and a compensation over a conditional is `CASE WHEN complement THEN 0 ELSE 1 END = 1` (rules 6 and 7, beyond EF's form). `grow11`: every form linear but two operands that can both be null (EF's forms, the narrowed Known issue). `fuzz11`: 3 oversized cases left, none with C#'s rows at 3.11.0. Seven harness keys, two core tests, 17 `Rewrite_KeepsCSharpsValue` rows; Z38–Z55 |
| R11-6 | Nit | OTHER | §6.5's rev 10 count mixed bases; §4.1 contradicted the assertion; W11-6 survived | Restated on one basis (§6.5); §4.1 is true again with R11-1; `Fold_AsksASmallOperandFirst_UpToItsBound` (32 against 33 nodes) kills W11-6 |

**The self-check's findings in rev 12's first cut** (before review, fixed): the compensation for `!a != b` took
`a != b` as its complement, which is SQL's answer but not C#'s where a is null, and the visitor folds a part that reads
no row in C# (one wrong-row case in `fuzz11`'s 84,000, seed 131 #2015, whose shrunk form rewrites to constants the
visitor folds in C#; Z42); and `!b` as `b ? false : true` put a bit in a `CASE` test, which
T-SQL rejects (one regression in `fuzz11`'s SQL Server texts; Z43). Both are pinned. The mutation run then found
the lazy-collection tests unable to fail (M-N24, M-N38, M-N39, M-N45 and Y1 survived): their predicate is sent once
now, so nothing repeats it. They are repointed at a conditional that EF's forms repeat, and the five are killed. The
`Substitute` that only the old expansion called, and the `Substituter`'s flag it set, are removed. The full SQL Server
suite then failed six keys: five R11-5 keys whose own conditionals tested a bit column (`x.Active ? … : …`, which T-SQL
rejects whatever the rewrite does), so they now test a comparison and run there; and `nullEqLiftedCastNotCond`, now
dialect-rejected there (§3). Coverage then showed the `Substituter`'s throwing-read catch and its nested-lambda rule
reached by nothing but the old expansion: two `Expansion_…` assertions now reach them through a repeated witness. And
`!a != b`'s branch for a `b` that isn't a `bool` was dead (a `bool?` that can't be null is a lift), so `b == false` is
compared in b's own type and the branch is gone (Z41–Z44 re-anchored).

### Task 0 round 12 (non-author, over rev 12 and the code, 2026-10-21 18:49): NOT CLEAN, 0 Major, 2 Minor, 2 Nit

All six of round 11's dispositions held. The reviewer reran every fuzzer (692,000 predicates per build, its TSVs the
author's byte for byte), added `fuzz12r` (64,000 over `long?`, `short`, `decimal?`, `DateTime?`, enums, strings and
twins) and 72 hand shapes, and found no wrong rows, no exception and no T-SQL regression. Its UNVERIFIED list named
SQL Server's CASE-nesting limit, which no fuzzer checks (ScriptDom parses only); the author measured it (R12-5). R12-5
and R12-2 went to the owner, who ruled (Paul, 2026-10-21): **"Distribute into branches"**.

| # | Sev | Blame | Finding | Rev 13 disposition |
|---|---|---|---|---|
| R12-5 | Major | TEST-GAP | (The author's, from the reviewer's UNVERIFIED #2.) SQL Server nests at most 10 CASEs. Rev 12's single CASE around a compensation over a conditional doubled the nesting of conditionals compared within conditionals' tests: from 6 levels, Msg 125, where 3.11.0 and EF ran them (**fix-introduced**: R11-5). The fuzzers' T-SQL check parses, and their valid shapes nest no CASE that deep | The owner's ruling: a nested conditional is distributed into its branches, a flat one takes EF's form, so the CASE nesting is the source's where the depth is in the tests, and one level more, once, where a branch is as deep as its test (the ruling's "3.11.0's + 1"; a Known issue). `nestedCondTestNe8`, `nestedCondTestNotGt8` (Msg 125 at rev 12 on SQL Server; red at `25956e0` by rows), `Rewrite_NestedConditionalComparisons_KeepTheirCaseNesting`, `Rewrite_ADistributedBranchAsDeepAsItsTest_AddsOneCaseLevel`; D1, D3, D7–D12. The self-check's `sslimits.py` now counts SQL Server's 2,100 parameters and 10 CASE levels over every fuzzer's text |
| R12-1 | Minor | AC-GAP | §4.3 wasn't exact. A delete of a `bool` compared with a copy of itself that differs by casts or a string's `ToString()` (`(x.N == null) == ((long?)x.N == null)`) was accepted, deleting every row, where 3.11.0's text check refused it; and the twin's refusal (`(x.N >= x.NX) != false`) was recorded only in §6.5 (not fix-introduced: since rev 3) | The guard's `bool` rule compares the two sides through built-in casts and a string's `ToString()`, numeric constants by value (`SameValue`), as the member-chain rule sees through a cast: refused as a self-reference, as 3.11.0 refused it. Five guard rows and two neighbours, three harness keys with their deletes (`nIsNullEqCastNIsNull`, `idGtCodeEqLongCasts`, `nameIsNullEqToStringIsNull`); the twin's class in §4.3 with `activeEqActiveXNeFalse`; D13–D15 |
| R12-2 | Minor | PLAN-GAP | §3's premise was false: distributing the comparison into the conditional's branches sends each part once, so two operands that can both be null needn't multiply the SQL | Adopted with R12-5: the Known issue is closed. `nestedCondBothNullNe`, `nestedCondBothNullEq`, three growth families, `Rewrite_ACompensationOverANestedConditional_IsDistributed`; D2, D5 |
| R12-3 | Nit | OTHER | Stale wording on the withdrawn assertion (harness and test comments), AC-N15 without the conditional's form, the Changelog's text-change list without `flag = n > @p`, and `(CASE … END)` parentheses the SQL doesn't send | Each corrected |
| R12-4 | Nit | OTHER | Record: "against rev 11" meant the `rev12a` build; at the `rev12a` code 9 DB-free tests fail, not 2; "the 111" is 110; `NullSemantics.cs` was written at 17:52, after the suites | Restated in §6.5. The 17:52 write is the mutation runner restoring the file's own bytes: the code files' checksums before and after its run are equal, and the frozen tree's were taken after it |

**The self-check's findings in rev 13's first cut** (before review, fixed): distributing every conditional on both sides
multiplied the leaves (left × right), so 23 of `fuzz11`'s 84,000 passed SQLite's parameter limit; only nested
conditionals are distributed now, the larger first. Rule 7 didn't substitute the operand that can't be null, which each
branch repeats, so a `Contains` in it was read per copy (one over-read in `fuzz11`; `Distribution_ReadsTheOtherOperandOnce_…`,
D4). The larger-first rule had no killer (D5 survived) until a growth family put the earlier level on the right.
The self-review of the fix layer, after the plan's first rev 13 text, found that text's claim false: the CASE nesting is
3.11.0's only where the depth is in the tests, and one level more, once, where a distributed branch is a conditional as
deep as its test (the leaf's two-valued CASE; the ruling's "3.11.0's + 1"). §3, §5.1, §10, the Changelog and the code's
comment now say so; `Rewrite_ADistributedBranchAsDeepAsItsTest_AddsOneCaseLevel` pins it (red at rev 12); and it is a
Known issue, since such a predicate 10 levels deep fails on SQL Server where 3.11.0 ran it. The self-review also probed
nested conditionals under operators and calls for a rewrite that never ends (`dbg13b`): none; each is C#'s value and
refused by the visitor as 3.11.0 refused it.

### Task 0 round 13 (non-author, over rev 13 and the code, 2026-10-21 21:55): NOT CLEAN, 1 Major, 3 Minor, 1 Nit

The reviewer reran every fuzzer (692,000 predicates, its 140 TSVs the author's byte for byte), round 12's `fuzz12r`, a
new `fuzz13r` (160,000 nested conditionals in tests and branches, both orders, negation, two nested sides, every
operand type) and 50 hand shapes, and found no wrong rows, no exception and no T-SQL regression against 3.11.0. All
five round 12 dispositions held but R12-5, which held only where the other operand holds no conditional (R13-1).
**Incident:** the reviewer ran the SQL Server harness once, against its brief (21:36 CT, a filter that matched
`SqlServerNullSemanticsTests`): 270 / 270 passed; the harness drops and recreates its own `zz_ns_*` tables, and no other
run used them then (the author's suites finished at 21:12). R13-1 went to the owner, who ruled (Paul, 2026-10-21):
**"EF's form there"**, and R13-3: **"Document it"**.

| # | Sev | Blame | Finding | Rev 14 disposition |
|---|---|---|---|---|
| R13-1 | Major | AC-GAP | The ruling's bound (3.11.0's CASE nesting + 1, linear size) failed where the other operand holds a conditional: each distributed branch repeated it below the distributed levels (k + 1 more; two sides nested in their branches 2k + 1, size quadratic). No AC carried the bound (**fix-introduced**: rev 13's distribution) | The owner's ruling: distribute only against an operand that holds no conditional (`Distributes`), EF's forms otherwise. AC-N17 carries the bound. `Rewrite_ANestedConditionalAgainstAConditional_TakesEFsForm` (every call site), `nestedCondNeFlat9` (Msg 125 at rev 13 on SQL Server), `depthplus.py` over every fuzzer, `fuzz12r` and `fuzz13r` standing; D16–D18; D5 withdrawn with `IsLarger`, D4 equivalent |
| R13-2 | Minor | AC-GAP | AC-N10's record wasn't exact for R12-1's rule: §4.3 and the Changelog didn't name casts, `ToString()` or widened constants, and 3.11.0 accepted the copies whose text held parameters (every row on SQLite for a relational one) (**fix-introduced**: R12-1) | §4.3's row and the Changelog's verdict row amended; `idGt2EqLiftedIdGt2` (accepted at `25956e0`, every row on SQLite) |
| R13-3 | Minor | AC-GAP | A distributed leaf that reads no row is folded in C#, so two strings there compare ordinally, where 3.11.0 and the flat form use the collation; not in AC-N11 (a) (**fix-introduced**) | The owner's ruling: documented under AC-N11 (a) and in the Changelog's text-change list; `nestedCondUpperANeA` (C#'s rows on all four; at `25956e0` `3` on SQL Server and MySQL) |
| R13-4 | Minor | TEST-GAP | No test killed the distribution under negation in step 5 (V13-6) or step 6 (V13-4), each a wrong-row mutant; V13-8 (the guard's user-defined cast) survived too (**fix-introduced**) | `notNestedCondEqV`, `notNestedCondEqM`, `notNestedBoolCondEqFlag` (red at `25956e0` by rows), the guard row `boolOtherUnderUserCast`; D19–D21 |
| R13-5 | Nit | OTHER | A stale test message ("sent once" for a flat conditional now sent twice); the lazy bullet named only both-nullable operands; record counts (12 DB-free red at rev 12, 87 verdict lines, PostgreSQL's 42883/42601, C# lengths for SQL's, `dbg13b`'s "bad 1") | Each corrected (§3, §6.5, the Changelog, `NullSemanticsTests`) |

**Four of five fix-introduced, the fourth round running:** per the house rule the test changed first. AC-N17 is a new
criterion with a test at each call site, and the depth check (`depthplus.py`) and round 13's fuzzers are standing. The
self-check of rev 14 (§6.5) found only returns to 3.11.0's behaviour against rev 13, recorded there.

### Task 0 round 14 (non-author, over rev 14 and the code, 2026-10-22 01:23): NOT CLEAN, 2 Major, 1 Minor, 1 Nit

The reviewer reran every fuzzer (its 196 TSVs the author's byte for byte), round 13's fuzzers and new chain probes, and
an EF Core 10.0.12 probe on SQLite, and found no wrong rows, no exception and no T-SQL regression against 3.11.0; every
change against rev 13 a return to 3.11.0's behaviour. It ran no SQL Server, PostgreSQL or MySQL command. R14-1 went to
the owner, who ruled (Paul, 2026-10-22): **"Go with the bound-checked distribution approach."**

| # | Sev | Blame | Finding | Rev 15 disposition |
|---|---|---|---|---|
| R14-1 | Major | AC-GAP | EF's form against a conditional multiplied the SQL and its parameters: a chain comparing a nested conditional with a conditional at each level passed SQL Server's 2,100 parameters at 6 levels and SQLite's limit at 8 (predicates under 1,000 characters; 3.11.0: 19 and 25 parameters, C#'s rows; EF: one parameter). "Grows as EF's does" was false there (**fix-introduced**: rev 14's fallback) | The owner's ruling: distribute wherever the CASE nesting stays within the comparison's + 1 (or EF's form's own), EF's forms only beyond it (`DistributeWithinTheBound`; AC-N17 restated). `Rewrite_NestedConditionalsAgainstConditionals_GrowLinearly`, `nestedCondChainNN8`, `nestedCondChainNNRight8` (2,100 parameters at rev 14 on SQL Server, measured), round 14's chain probes standing; D16–D18, D23, D26 |
| R14-3 | Major | AC-GAP | An always-true delete that 3.11.0 refused as a self-reference was accepted and deleted every row: a column that can be null compared with its `[SqlExpression]` alias, or a copy under a cast through `object`, whose EF form `(n = n OR (n IS NULL AND n IS NULL))` passed the whole-clause check (not fix-introduced: since the both-nullable `==`) | The guard's text check also refuses EF's form of a self-equality, with two witnesses or one (`WholeClauseNullSafeSelfEquality`), whole-clause only, as 3.11.0's check was. `nEqNX`, `notNNeNX`, `objNameLengthSelfEq` (`NsComputed` gains `NX`), seven guard text rows; D27, D28 |
| R14-2 | Minor | TEST-GAP | Nothing pinned distribution with the nested conditional on the right: V14-1 (step 6) and V14-2 (rule 7) survived every test (**fix-introduced**) | Right-nested families in the growth test (`flat(j) != L`, `!(flat(j) > L)`, `x.M != L`, `!(x.M > L)`) and `nestedCondChainNNRight8`; D24, D25 |
| R14-4 | Nit | OTHER | §6.5's rev 13 comparison counts covered fuzz6 to `fuzz11r` only (177, 13 and 52 over every fuzzer); "as deep as the test" is "at least as deep" | Restated (§6.5, §3, the Changelog) |

**The self-check's findings in rev 15's first cut** (before review): the bound without EF's form's own nesting
refused the chains whose branches are predicates (AC-N16), whose operands nest deeper than written in either form; the
growth test and two nested keys failed, so the term stays (D23). Distribution against a conditional operand that the
bound lets through repeats it, so rule 7 substitutes it again (D4 is live, not equivalent). The mutation run then left
D17 alive (the bound without its + 1) and coverage left the depth counter's lambda skip unrun; each now has a killing
shape in `Rewrite_ACompensationOverANestedConditional_IsDistributed` (§6.5), and the instrumented `dbg15b` probe shows
why D17 hid: only a chain's first nested level depends on the + 1.

### Task 0 round 15 (non-author, over rev 15 and the code, 2026-10-22 19:36): NOT CLEAN, 3 Major, 3 Minor, 1 Nit

The reviewer reran every fuzzer (its 196 TSVs the author's byte for byte), round 14's chain probes, its own probes
(`R15.cs`) and a sample of 40 mutations with 9 of its own, and found no wrong rows, no exception and no T-SQL
regression against 3.11.0 in the fuzzers. It ran no SQL Server, PostgreSQL or MySQL command. The owner answered
(Paul, 2026-10-22): **"A"**, fix all seven, then run round 16 under an **exit rule**: a finding blocks only for wrong
rows, a delete of rows it shouldn't delete, a throw or invalid SQL in a realistic predicate, or a false statement in
the documents; a size or depth limit met only by predicates nesting conditionals several levels deep is a Known issue,
Minor at most.

| # | Sev | Blame | Finding | Rev 16 disposition |
|---|---|---|---|---|
| R15-1 | Major | AC-GAP | Two nested sides were distributed left first, so a chain whose earlier level sat on the right of a shallow nested conditional repeated it threefold per level inside the bound: 2,421 parameters at 5 levels and 65,601 at 8 (3.11.0: 22 and 34, C#'s rows); four statements said it couldn't happen (**fix-introduced**: rev 15 brought two nested sides back without rev 13's `IsLarger`) | The larger side first again (`IsLarger`, `NodeCounter`): round 15's `order` probe is linear in both orders (89 parameters at 5 levels, 140 at 8). The growth test's two R15-1 families (rev 15: 3,616 nodes four levels down, 297,376 eight down), `nestedCondChainNestedLeft7`; D5 back, D30 |
| R15-2 | Major | TEST-GAP | R14-3's pattern missed a derived operand of an alias: EF's form tests the column, not the operand (`(LENGTH(name) = LENGTH(name) OR (name IS NULL AND name IS NULL))`), so `x.Name.Length == x.NameX.Length` and the date parts were accepted and deleted every row, where 3.11.0 refused them (not fix-introduced: since the both-nullable `==`) | The two-witness branch takes any witness W, and both branches refuse only where the operand A holds W as a token (`HoldsToken`). `nameLengthEqNameXLength`, `nameXLengthEqNameLength`, `dYearEqDXYear`, four guard rows (SQLite's, SQL Server's, bracket-quoted, a date part); D38 |
| R15-3 | Major | TEST-GAP | The bound counted conditionals, not CASEs, so a `[SqlExpression]` whose template holds a CASE, ending a flat conditional, sat below the leaf's CASE: 3.11.0's 9 → 11 (Msg 125) (**fix-introduced** against rev 14) | A comparison that reads any property of the remote map keeps EF's forms (`ReadsRemote`): round 15's `hidden` probe nests 3.11.0's own depth at every d. `Rewrite_AComputedPropertyOperand_KeepsEFsForm`, `nestedCondNeFlat8CodeCase`; D32. The self-check then found the same class in FunkyORM's own SQL (below) |
| R15-4 | Minor | PLAN-GAP | R14-3's pattern refused deletes that 3.11.0 accepted, unrecorded: the negated alias forms (`!(x.N != x.NX)`: 3.11.0 deleted the rows that aren't NULL), and its one-witness branch matched 3.11.0's own text for `A == A \|\| C == null`; 219 fuzzer deletes newly refused (**fix-introduced**: R14-3's pattern) | The one-witness branch refuses only a witness the operand reads, so `(active = active OR flag IS NULL)` is accepted as 3.11.0 accepted it (`activeEqActiveXOrFlagNull`). No fuzzer's verdict moves with it: the 219 refusals stay, all always true in C#, 217 of them a column against its alias under a `!` and 2 R10-3's cast through `object`. The negated alias forms stay refused, being always true in C#: §4.3's and the Changelog's new row; the key comment, §6.2, the pattern's comment and Usage.md corrected; D34, D35 |
| R15-5 | Minor | PLAN-GAP | A source of the + 1 the documents didn't name: another conditional one level shallower than the nested side (`nestedCondChainNN8` itself; 3.11.0's 10 → 11 at 10 levels) | Known issue 1 and §3 restated: the + 1 comes wherever the predicate's deepest part is a leaf, either source named |
| R15-6 | Minor | PLAN-GAP | The EF-form growth the ruling keeps passes SQL Server's 2,100 parameters from about 6 levels and SQLite's limit from about 8, in predicates of 1,000–2,000 characters whose rows 3.11.0 got right; the Known issues didn't say so | Known issue 2, §3 and §10 restated, with "as EF's does" qualified (EF binds each value once) |
| R15-7 | Nit | OTHER | Two records: rev 15's red run counted 5 DB-free failures where its final test files fail 6; the status block's "under 700 characters" (the 8-level chain is 915) | Restated (§6.5, the status block) |

**The self-check's findings in rev 16's first cut** (before review):
- **SQL Server counts `COALESCE` as a `CASE`** (measured: `COALESCE`, `IIF` and `NULLIF` are counted, `ISNULL` is not;
  scratchpad `coalesce-limit.ps1`), and FunkyORM sends one for a nullable integer's `ToString()`. R15-3's shape with
  that `ToString()` ending the flat conditional went from 3.11.0's 9 to 11 on SQL Server (Msg 125, measured with the
  probe `dbg16`). The depth counter now counts that `COALESCE` (`Rewrite_ANullableIntegersToString_CountsAsACase`,
  `nestedStrNeFlat8NToString`; D36, D37), and `ssdepth16.py` reads every fuzzer's text as SQL Server counts it.
- **SQL Server's `Any()` sends its query inside a CASE** (`CASE WHEN EXISTS …`), so a WHERE nesting 10 CASEs fails
  there at 9. R15-1's key first ran eight levels deep, where the ruled + 1 makes 10 (3.11.0: 9) and `Any()` 11
  (Msg 125). The key runs seven levels deep, and Known issue 1 names `Any()`.
- **The growth test overflowed the test host's stack at rev 15's code:** the eight-level R15-1 rewrite (297,376 nodes)
  compiled to a delegate too deep to run. The rewrite itself returned; the test now checks the size before it compiles
  anything, so rev 15 fails it by the size.
- The comparison now refused against a remote property sends EF's form where rev 15 distributed, so some predicates
  return to 3.11.0's behaviour: 65 of `fuzz12r`'s and `fuzz13r`'s that 3.11.0 sent as invalid T-SQL (SQL Server's
  `NOT` on a `bit`, §3) and rev 15 sent as valid are invalid again, and one of `fuzz11r`'s is refused by the translator
  as 3.11.0 refused it (§3's both-branches bullet). None was valid or translated at 3.11.0.

### Task 0 round 16 (non-author, over rev 16 and the code, 2026-10-22 21:45): NOT CLEAN, 2 Major, 4 Minor, 1 Nit

Judged by the owner's exit rule (round 15's record). The reviewer reran every fuzzer (its 392 TSVs, rev 16's and
3.11.0's, the author's byte for byte; no wrong rows, exception or T-SQL regression against 3.11.0), the red runs (as
§6.5 records them), the DB-free classes and the SQLite harness (green), D5, D27–D30, D32 and D34–D38 (killed), and its
own probes (`R16.cs`). It ran no SQL Server, PostgreSQL or MySQL command.

| # | Sev | Blame | Finding | Rev 17 disposition |
|---|---|---|---|---|
| R16-1 | Major | TEST-GAP | R15-2's class was open where the two witnesses differ: a computed property whose template is the provider's own text for a member's `Length` or date part, against that member, is EF's `(A = A OR (A IS NULL AND col IS NULL))`, which neither branch of the pattern matched; 3.11.0 refused all seven shapes (and 8 of 8 provider texts), rev 15 and rev 16 deleted every row (not fix-introduced: since the both-nullable `==`) | The two-witness branch takes two witnesses, W and V, each one the operand holds as a token (`HoldsToken`). Guard rows for SQLite's, SQL Server's, MySQL's and a date part's text and a neighbour accepted; `TheGuard_RefusesAComputedPartAgainstTheColumnsPart_InEveryProvidersText` (each provider's own visitor text, seven shapes); `SqliteComputedPartSelfReferenceTests` (SQLite's real delete, rolled back); D39, D40 |
| R16-2 | Major | PLAN-GAP | A false statement in the documents: the Changelog, Known issue 2, AC-N17 and the code said a `[RemoteProperty]`, `[RemoteKey]`, `[JsonPath]` or `[SubqueryAggregate]` "can hold CASEs"; only an `[SqlExpression]` template can (**fix-introduced**: rev 16's text) | `ReadsRemote` withdrawn. The depth count reads an `[SqlExpression]`'s template as SQL Server counts it: its CASEs, COALESCEs, IIFs and NULLIFs nested, outside quoted strings, in any provider's variant, a `{Token}` that names another template adding that template's levels (`TemplateLevels`). The documents say so. `Rewrite_AComputedPropertyOperand_CountsItsTemplatesLevels`; D41–D46 |
| R16-3 | Minor | PLAN-GAP | `ReadsRemote` refused distribution for a remote read anywhere in the comparison, so one alias in a chain's innermost test sent every level above it in EF's form, threefold per level (55,761 parameters eight down; rev 15: 172) (**fix-introduced**) | Gone with `ReadsRemote` (R16-2): an alias without a level of its own counts nothing. The growth test's `NX` family, `nxChainInTest7` (SQL Server's 2,100 parameters at rev 16); round 16's `chain` probe linear (157 parameters at k = 8; rev 16: 101,685) |
| R16-4 | Minor | PLAN-GAP | Known issue 1's "and never more" and AC-N17's "nothing else adds one" were false: a remote or computed integer's `ToString()` is a `COALESCE` since R4-10 (3.11.0: a bare `CAST`), + 1 on its own, + 2 below a leaf of step 5's unchecked distribution (3.11.0's 9 → 11); and `All()` wraps its query in a CASE as `Any()` does (not fix-introduced; rev 16 restated the claims) | Known issue 1, AC-N17 and §3 restated with both, `All()` named |
| R16-5 | Minor | PLAN-GAP | Two classes of always-true delete that 3.11.0 accepted are refused and weren't listed: a column's part against its alias's under a `!` (`!(x.Name.Length != x.NameX.Length)`), and 3.11.0's own one-witness text where the operand reads the witness (`x.N.ToString() == x.NX.ToString() \|\| x.N == null`) | §4.3's and the Changelog's rows widened and added; `notNameLengthNeNameXLength`, `nToStringEqNXToStringOrNNull` |
| R16-6 | Minor | TEST-GAP | Nothing killed counting a string's `ToString()` as a COALESCE (V16-5 survived) (**fix-introduced**) | `Rewrite_ANullableIntegersToString_CountsAsACase` gains the string case |
| R16-7 | Nit | OTHER | "217 under a `!`" (one is `(x.N == x.NX) == T`); two harness comments said 3.11.0 sent a bare `CAST` for `int? N` (it sent the `COALESCE`); `CaseNesting`'s summary true only because `ReadsRemote` screened its call site; Usage.md's "tests it for NULL" | Each restated: 217 under a `!` or compared with a `bool` value (213 and 4), the comments, `CaseNesting` now counts the templates itself, Usage.md |

**The self-check's findings in rev 17's first cut** (before review): the template count read a computed integer's
levels beside its `ToString()`'s `COALESCE`, not inside it, one level short at the bound's edge; the counter now steps
into the `COALESCE` (the 7-deep `NCase.ToString()` shape; D47). And the R16-1 delete test first ran without a
transaction, which FunkyORM requires for a delete; it runs each delete in one, rolled back.

### Task 0 round 17 (non-author, over rev 17 and the code, 2026-10-22 23:33): NOT CLEAN, 2 Major, 4 Minor, 1 Nit

Judged by the owner's exit rule. The reviewer reran every fuzzer (588 TSVs, the author's and round 16's reviewer's byte
for byte; no wrong rows, exception, T-SQL regression or depth past + 1 against 3.11.0), the red, green and `25956e0`
counts (as §6.5 records them), 19 of the mutations (killed), and its own probes (`R17.cs`, `R17b.cs`; 53 shapes moved
from EF's form to distribution, run through the real SQLite provider: C#'s rows). It ran no SQL Server, PostgreSQL or
MySQL command.

| # | Sev | Blame | Finding | Rev 18 disposition |
|---|---|---|---|---|
| R17-1 | Major | TEST-GAP | Rev 17's two-witness pattern took the first split at `IS NULL AND`, so where the operand's own SQL held one (a conditional whose test is a both-nullable `==` or `x.N == null && x.M == null`, or a template holding either) the clause was accepted: refused at 3.11.0 and rev 16, every row deleted at rev 17, in all four providers' texts; and the pattern was quadratic (105 s at 256 K characters) (**fix-introduced**) | The pattern is replaced by a parse (`IsNullSafeSelfEquality`): the outer parentheses must enclose the clause (quoted text aside), the NULL test is the trailing group or one bare witness, `A = A` splits at its middle, and every split of two witnesses short enough to be tokens of A is tried, in linear time. Most such shapes are now refused before translation (R17-2's verdict). Guard rows (an A holding `IS NULL AND`, a quoted parenthesis), `nBothEqNBothX`, `ValidateTranslation_OfLongClauses_StaysLinear`; D28, D34, D39, D40 redone on the parser, D52–D54 |
| R17-2 | Major | TEST-GAP | A nested conditional compared by `==` with its own copy, or its alias's, is distributed, and the whole-clause text check never sees the self-comparison: refused at 3.11.0, every row deleted at rev 17 (the alias part **fix-introduced** with `ReadsRemote`'s withdrawal; the literal copy since rev 13) | **The test redesigned first** (the third time; two fix-introduced rounds running): `fuzz18s` compares an operand with a copy of itself, literal or through aliases, under every wrapper, through the real SQLite provider's query and delete, against 3.11.0's verdict; at rev 17 it found 733 deletes 3.11.0 refused, rev 17 ran (373 over seeds 501–508, as first recorded; R18-7). Then the code: the verdict decides a self-comparison from the expression for any type, as 3.10's rule did for a column (`IsSelfComparison`'s `SameValue` rule no longer bool-only), an `[SqlExpression]` alias (a template that is one `{Token}`) is the property it names, two templates of the same text are the same value, and the verdict sees through a cast through `object`. `fuzz18s` at rev 18: no miss. Verdict rows (`condSelfCopy`, `condAliasCopy`, `nestedCondAliasCopy`, …), `nestedCondSelfCopy`, `nestedCondAliasCopy`; M-N16 re-anchored, D48–D51 |
| R17-3 | Minor | PLAN-GAP | A computed integer's `ToString()` reaches 3.11.0's + 2 at step 6 too, not only at step 5, as AC-N17 and §3 said | AC-N17 and §3 restated (Known issue 1 was right) |
| R17-4 | Minor | TEST-GAP | `TemplateLevels` undercounted an `END` inside a quoted identifier or a comment, and a base-class template naming a derived one (it resolved against the declaring type) (**fix-introduced**) | Quoted identifiers (`[…]`, `"…"`, `` `…` ``) and comments are skipped; tokens resolve by exact name against the row's type, as the providers resolve them. Six two-level rows seven deep; D55, D56 |
| R17-5 | Minor | PLAN-GAP | `!(x.NameLen != x.Name.Length)` is always true, accepted by 3.11.0, refused by rev 17, and unrecorded (**fix-introduced**: R16-1) | §4.3's computed-part row; `SqliteComputedPartSelfReferenceTests` pins it |
| R17-6 | Minor | TEST-GAP | Nothing killed dropping `visiting.Remove` (a template named twice then counted once) (**fix-introduced**) | `NTwice` (seven deep); D57, and D58 for the cycle guard (a stack overflow) |
| R17-7 | Nit | OTHER | `TemplateLevels`' summary left `[JsonCollection]` out; the mutation runner recorded a crashed test host as "0 failed" | Both fixed: the runner records an aborted run as a kill |

**The self-check's findings in rev 18's first cut** (before review): the new fuzzer found the verdict blind to a
self-comparison under a cast through `object` (127 misses), fixed in the verdict's fold (verdict rules only, so the
SQL's text is 3.11.0's: AC-N11); a version that left `>=`/`<=` of values that can be NULL to the text check missed 3 that
3.11.0 refused (a copy whose text the rewrite's fold changed), so the verdict counts every operator, as 3.10's rule
counts a column's (§4.3, the new refusals recorded in §6.5); and `idToStringFormatSelf` (`x.Id.ToString("D2") ==
x.Id.ToString("D2")`) is a self-reference to the verdict now (a delete still throws the translator's
`NotSupportedException` for the format first).

### Task 0 round 18 (non-author, over rev 18 and the code, 2026-10-23 02:03): NOT CLEAN, 2 Major, 2 Minor, 4 Nit

Judged by the owner's exit rule. The reviewer reran every fuzzer (392 TSVs and the 32 of `fuzz18s` byte-identical to
the author's), rounds 15–17's probes (the author's outputs), the DB-free classes (590) and the SQLite classes (295), 19
of the mutations (killed, D58 by its crash), and its own probes (`R18.cs`; the twin fuzzer `fuzz18t`, `Fuzz18T.cs` and
`an18t.py`). It ran no SQL Server, PostgreSQL or MySQL command.

| # | Sev | Blame | Finding | Rev 19 disposition |
|---|---|---|---|---|
| R18-1 | Major | TEST-GAP | The verdict misses twins only the provider's SQL shows: a template that is the dialect's own text for a column's part (`LENGTH({Name})` against `x.Name.Length`, a date part), two templates that differ only in letter case, two `[JsonPath]` properties on one path. Inside a nested conditional the copy is distributed, so the text check doesn't see it either: refused at 3.11.0, every row deleted at rev 18 (and at rev 17), also under `!(… != …)` and in an `\|\|`. `fuzz18t`: 1,343 of 12,000 | **The test redesigned first** (the guard's class, the third round running): `fuzz18t` adopted into the self-check (`probe-shared\Fuzz18T.cs`, `an18t.py`, `run-fuzz18t.sh`; seeds 601–616, 24,000 shapes): 2,613 misses at rev 18, its first eight seeds byte-identical to the reviewer's. Then the code: the providers pass their SQL for a part through the new `Validate(predicate, operation, operandSql)` once the predicate has translated, and a comparison whose operands are the same part by part, a part's SQL asked where two small parts differ in shape (letter case aside, the parameters as their values: `OperandText`), is a comparison of an operand with itself to the verdict (§5.3). A first cut compared whole operands' SQL, and missed 83, then 116: a visitor folds a literal copy inside an operand (`x.N == x.N` is `NOT 1=0`) and not its twin. `fuzz18t` at rev 19: no miss, no wrong rows. Tests: `Validate_WithTheOperandSql_RefusesTwoOperandsOfTheSameSql`, `OperandText_WritesEachParameterAsItsValue`, `TheGuard_RefusesANestedComputedTwin_WithEveryProvidersOperandSql`, `Delete_OfANestedTwin_IsRefused`, `nestedNAbsUpperEqNAbsLower`, `nestedDocKEqDocK2`; D59–D73 |
| R18-2 | Major | TEST-GAP | False statements: the any-operand verdict row gave arithmetic and `x.Id + 1 == x.Id + 1` as "accepted… every row" at 3.11.0 (no provider translates arithmetic: the delete throws `NotSupportedException`), said "every row" for its lead example (3.11.0 deleted the rows whose conditional isn't NULL), and listed `(bool)(object)((x.N == null) == (x.N == null))`, which 3.11.0 refused; the alias rows gave `(x.N == x.NX) == true` and `(x.Name != x.NameX) != true` as self-referencing (rev 18: always true); five XML docs defined a self-reference as a member chain compared with itself (**fix-introduced**) | The Changelog's, Usage.md's and §4.3's rows restated from round 18's probes (`r18-msgs`, `r18-twins`, `r18-arith`): arithmetic is no example (its delete throws first, pinned in `Delete_OfANestedTwin_IsRefused`), the 3.11.0 cell gives the rows whose operand isn't NULL, `objBoolSelf` sits with the copies 3.11.0 refused; the alias rows' bool-value forms say always true (`Classify` rows `notNAliasNe`, `nAliasEqTrue`, `nameAliasNeNeTrue`); the verdict enum's, the class summary's, `Classify`'s, `FoldRules`' and `IsSelfComparison`'s XML docs say any operand, a copy, an alias, a same-text template, and the provider's SQL |
| R18-3 | Minor | PLAN-GAP | R17-5's disposition named "§4.3's computed-part row", which didn't exist; `!(x.NameLen != x.Name.Length)` (3.11.0 deleted the rows with a name, refused now) was in neither §4.3 nor the Changelog | Both rows added |
| R18-4 | Minor | TEST-GAP | Three of the reviewer's mutants of rev 18's verdict survived every allowed killer: X1 (an alias's variants needn't name one token), X2 (`SameTemplate` compares only `Expression`), X9 (`ValueOf` follows one alias only) (**fix-introduced**) | `Classify` rows `nDualOtherNotAlias`, `otherDualOtherNotAlias`, `dualTemplatesDifferPostgreSql`, `dualTemplatesDifferMsSql`, `dualTemplatesSame`, `aliasOfAliasSelf`; the three are D74–D76 |
| R18-5 | Nit | OTHER | §4.3's rows for `activeEqActiveXNeFalse`, `nToStringEqNXToStringOrNNull` and the alias row's comparisons with a `bool` value said "self-referencing"; rev 18 says always true (**fix-introduced**) | The three restated |
| R18-6 | Nit | OTHER | The harness comment said 3.11.0 refused `nestedCondSelfCopy` and `nestedCondAliasCopy`; it accepted both (each copy's constants got their own parameters) (**fix-introduced**) | The comment fixed |
| R18-7 | Nit | OTHER | "373 at rev 17" counted seeds 501–508 only; over 501–516, where rev 18 is measured, rev 17 has 733 misses (**fix-introduced**) | Restated in the status block, §6.5 and the R17-2 row |
| R18-8 | Nit | OTHER | Usage.md said the text-check backstop refuses `!(x.N != x.NAlias)`; rev 18's verdict refuses it first (**fix-introduced**) | The example removed; the backstop's form stays described |

**The self-check's findings in rev 19's first cuts** (before review): comparing whole operands' SQL missed 83 of
`fuzz18t`'s shapes (a twin inside a test that the bottom-up rewrite made a literal copy, which a visitor then folds),
and comparing the operands as written missed 116 (a literal copy on one side, folded, against its alias on the other):
so the comparison is part by part, and a part's SQL is asked only where shapes part. Asking it for every enclosing part
cost the square of a chain's depth, so it is asked only for parts of at most 64 nodes. A captured value's part is
compared as written, so `(x.Id > two ? x.NameLen : …) == (x.Id > two ? x.Name.Length : …)` is refused (3.11.0 accepted
it: the two reads of `two` were two parameters), with no read of `two`. The harness keys first used `x.Active ?` as a
test, which SQL Server rejects (a `bit` as a `CASE` test, §3), and a template with a `CASE`, which the depth bound keeps
in EF's form; both were replaced (`x.Id > 2 ?`; `ABS({N})` against `abs({N})`).

### Task 0 round 19 (non-author, over rev 19 and the code, 2026-10-23 04:53): NOT CLEAN, 2 Major, 2 Minor, 1 Nit

Judged by the owner's exit rule. Both Majors are false statements in the product code's comments; the reviewer found no
wrong rows, no delete of more rows than C# selects, and no twin that 3.11.0 refused and rev 19 runs. It reran every
fuzzer (byte-identical to the author's), round 18's probes, the DB-free (634) and SQLite (299) classes, the author's
D59–D72, D73s and D74–D80 (killed), and its own probes (`R19.cs`, `R19b.cs`) and mutants. It ran no SQL Server, PostgreSQL
or MySQL command.

| # | Sev | Blame | Finding | Rev 20 disposition |
|---|---|---|---|---|
| R19-1 | Major | AC-GAP | A delete read a captured getter more often than 3.11.0 and rev 18: each provider called `Validate(expression, operation)` before translation and the new overload after it, which classifies again (`x.Id == 5 && c.T`: 3 / 3 / 5 reads at 3.11.0 / rev 18 / rev 19); the overload's XML doc and §5.3 said "nothing the caller wrote is evaluated again" (**fix-introduced**) | The providers check the argument contract first (a read's `Validate`) and give the verdict once, through the overload, after translation; AC-N13 now bounds a delete's reads too. `Delete_ReadsACapturedValue_AsOftenAs3110` (SQLite's real delete, round 19's eleven shapes: 3.11.0's counts, red at rev 19 for four, green at `25956e0`), `Query_OfANullPredicate_ThrowsArgumentNull` (the contract first); D89, D90 |
| R19-2 | Major | TEST-GAP | (a) "Each part's SQL is asked once" was false for an alias's target, a new node each time; (b) `IsSelfComparison`'s comment still named arithmetic (R18-2 (e) not carried out); (c) a part holding a lambda with no parameter was asked (**fix-introduced**: (a), (c)) | (a) the memo is keyed by member for a member of the row, and the comments say a part written twice is two nodes, each asked (`Validate_WithTheOperandSql_AsksEachMemberOnce`; D86); (b) the comment says a call; (c) the row-only check refuses any lambda again (the `parameterlessLambda` row; D87) |
| R19-3 | Minor | PLAN-GAP | "Letter case aside" refused deletes whose operands differ: JSON keys, a template's string literal or a `ConditionalCount` value that differ only in letter case (3.11.0 and rev 18 ran them, C#'s rows) (**fix-introduced**) | `OperandText` writes quoted text as the hexadecimal of its characters, so the letter-case-blind comparison keeps a literal's, a JSON path's and a quoted name's case (`OperandText_…`'s quoted rows, `TheGuard_ComparesQuotedTextWithItsLetterCase_InEveryProvider`, `Delete_OfANestedTwin_IsRefused`'s JSON neighbour; D88). The residue on a case-insensitive collation is a Known issue and a §4.3 row |
| R19-4 | Minor | TEST-GAP | Five rev 19 branches had no killer: the 64-node bound on one side only (X1, X1b), `<=` or `<`/`>` not comparisons (X9, X9b), the overload's own verdict on an unchanged predicate (X14) (**fix-introduced**) | Rows `twinLe`, `twinNotLt`, `twinNotGt`; the timing test's chains compared with a column on either side; `Validate_WithTheOperandSql_GivesTheVerdict`; `fuzz19t` (`fuzz18t` with `<=`, `!(<)` and `!(>)`); D81–D85 |
| R19-5 | Nit | OTHER | §4.3's computed-part row said the operand SQL runs "before translation" | "after translation and before the text check" |

**The self-check's findings in rev 20** (before review): the JSON-key neighbour first counted 1 row, because the test ran
it in the transaction its sibling deletes had already thinned (the query and C# give rows 2, 3, 4; `dbg20`); it runs in
its own transaction now. A contract check that nothing tested would survive its mutant, so `Query(null)` pins it.

### Task 0 round 20 (non-author, over rev 20 and the code, 2026-10-23 07:04): NOT CLEAN, 3 Major, 3 Minor, 1 Nit

Judged by the owner's exit rule. All three Majors are false statements in the Changelog or the product code's comments;
the reviewer found no wrong rows, no delete of more rows than C# selects, no new throw, and no twin that 3.11.0 refused
and rev 20 runs. It reran every fuzzer (byte-identical), rounds 14–19's probes, the DB-free (645) and SQLite (311)
classes, D59–D90 (killed), and its own probes (`R20.cs`) and fuzzer (`delreads`, 12,000 deletes through SQLite's real
provider, counting the captured reads). It ran no SQL Server, PostgreSQL or MySQL command.

| # | Sev | Blame | Finding | Rev 21 disposition |
|---|---|---|---|---|
| R20-1 | Major | TEST-GAP | "A delete reads a captured value as often as 3.11.0's" (the Changelog, the overload's XML doc, the four providers' comments, AC-N13, §5.3) was false around a cast through `object`: the verdict's see-through (rev 18) evaluated captured values under a cast that reads the row, where 3.11.0's verdict read nothing, and read a boxed null `bool?` twice (`delreads`: 646 deletes read more than 3.11.0's) (**fix-introduced**: the claims) | The verdict evaluates a cast through `object` that reads no row whole, as 3.11.0 did, and folds one that reads the row under rules that evaluate no leaf (`VerdictUnderACast`), so its self-comparisons and literals still count and no captured value under it is read. `delreads` (adopted, `run-delreads.sh`): 9 of 12,000 deletes read more than 3.11.0's (4,555 fewer): what the verdict learns under the cast can lead it to read a value beside it that 3.11.0's didn't reach. The five places now say that: "no more often than 3.11.0's, except around a cast through `object`". `Delete_ReadsACapturedValue_NoMoreOftenThan3110` (moved into the harness, all four providers, with six cast rows: red at rev 20 for two), `Classify` rows `orObjCapturedTrue`, `objOrCapturedTrue`, `objOrCapturedTrueEqTrue`, `objSelfOrCapturedTrue`, and the SQLite delete's `R20-1` row (the text check still refuses a captured true under such a cast); D91, D92, D94. The fuzzers' 15 deletes that rev 20 refused and rev 21 accepts are 3.11.0's verdicts, each with C#'s rows (§6.5), stated as a Known issue |
| R20-2 | Major | TEST-GAP | "A member of the row's [SQL is asked] once per member" was false for a property declared on a base class: the memo was keyed by `MemberInfo`, and the alias's target, found on the derived type, isn't the compiler's instance (**fix-introduced**) | The memo is keyed by module and metadata token, as `SameMember` compares members; `Validate_WithTheOperandSql_AsksEachMemberOnce`'s base-class row (red at rev 20); D93 |
| R20-3 | Major | HOUSE-RULE | Seven delete XML docs said the guard checks "before and after translation" or "before it is translated" (**fix-introduced**) | "once the predicate has translated, and on its WHERE clause" in the six SQLite, PostgreSQL and MySQL docs and SQL Server's `DeleteAsync`; `Classify`'s and `Validate`'s summaries say "without translating it"; the two stale test comments too |
| R20-4 | Minor | TEST-GAP | The text check still reads quoted text letter case aside, so `!(x.DocK != x.DocLowerK)` and `!(x.IsUpperA != x.IsLowerA)` are refused as self-referencing though their values differ; §4.3 said JSON keys are accepted on every provider | Recorded, not changed (3.11.0's check read letter case aside too): §4.3's R19-3 row and a Changelog Known issue; pinned by the SQLite delete's `R20-4` row |
| R20-5 | Minor | PLAN-GAP | The quoted-text residue is wider than the Known issue said (quoted names on SQLite and MySQL match without letter case whatever the collation), and PostgreSQL's `$$…$$`, `E'…'` and MySQL's backslash escapes aren't parsed as quoted (**fix-introduced**) | The Known issue and the "Changed" entry name the quoted forms read (`'…'` with `''`, `"…"`, `[…]`, `` `…` ``) and where the database matches two as one |
| R20-6 | Minor | TEST-GAP | Four rev 20 branches had no killer: `QuotedTextAsHex` without its `i--` (X20-1), the memo keying any member access (X20-2), SQL Server's single verdict and contract check (X20-3, X20-4) (**fix-introduced**) | An exact `OperandText` row; the asks test's two columns' `Length`; the read and null-predicate tests moved into the harness, so all four providers run them; D95, D96, D97m/p/y, D98m/p/y |
| R20-7 | Nit | OTHER | The mutation and red records named `AsksEachPartOnce`, renamed after the mutation run, and the guard changed after it | §6.5 notes the rename; rev 21's mutation run is on its final code |

**The self-check's findings in rev 21:** the boxed-null double read was one of two sources; the see-through itself was the
other (631 deletes still read more after the first fix alone), so the cast that reads the row is folded without
evaluating. At `25956e0` the harness's read test passes on SQLite and MySQL; on SQL Server and PostgreSQL the rows 3.11.0
sent as invalid SQL fail by the database's error there (3 and 4), as the Changelog's "Shapes that failed now run" says.

### Task 0 round 21 (non-author, over rev 21 and the code, 2026-10-23 09:35): NOT CLEAN, 3 Major, 2 Minor, 2 Nit

Judged by the owner's exit rule. All three Majors are false statements in the product code's comments; the reviewer found
no wrong rows, no delete of more rows than C# selects, no new throw and no twin miss. It reran every fuzzer on rev 21,
rev 20 and 3.11.0 (byte-identical to the author's), `delreads` on all three, and a new offline one (`offdel`: delreads'
12,000 predicates through each of the four providers' visitors), rounds 17–20's probes, its own (`R21.cs`, `R21b.cs`),
the DB-free (511) and SQLite (1,479) tests, D89–D96 and six mutants of its own (X21-1..X21-6). It ran no SQL Server,
PostgreSQL or MySQL command.

| # | Sev | Blame | Finding | Rev 22 disposition |
|---|---|---|---|---|
| R21-1 | Major | TEST-GAP | The under-cast rule sets never counted an empty search value (the `Call` case asked for `VerdictWithUnboundedSearch` only), though `FoldRules`' summary said a literal is still seen there, and the class summary, the delete docs and Usage.md say such a delete is refused: `(bool)(object)x.Name.Contains("")` was accepted (rev 20 refused it, 3.11.0 accepted it) (**fix-introduced**) | Under a cast through `object` the empty search counts where its value is written as `""` or `string.Empty`, which reads nothing the caller wrote; a captured `""` there isn't read (3.11.0's verdict read none), a Known issue. `Classify` rows `objNameContainsEmpty`, `idOrObjNameStartsWithEmpty`, `objNameEndsWithStringEmptyOrId` (red at rev 21), `objNameContainsCapturedEmpty`; SQLite's `R21-1` and `R21-1b` deletes (red at rev 21); §4.3's new row and the Changelog's; D99–D101, D106, D107 |
| R21-2 | Minor | PLAN-GAP | The documents said a delete always true or self-referencing only through a captured value under such a cast "is accepted"; the translated clause's check refuses most of them, where the rewrite sends the value as a literal, five of them (B9–B11, B13, B20) newly against 3.11.0; six delete docs lacked SQL Server's "don't catch every predicate" hedge (**fix-introduced**) | The Changelog's Known issue, §4.3's cast row and §5.3 say what the check refuses and what it accepts (the B7 shape as the accepted example); B9–B11, B13 and B20 recorded in §4.3 and the Changelog's verdict table; the six docs carry the hedge |
| R21-3 | Major | HOUSE-RULE | `SelfReferenceMessage`'s doc still said "before and after translation" (R20-3's sweep missed it) (**fix-introduced**) | "The message of `SelfReference`, and of a translated clause that compares an operand with itself"; the test comment "Always true before translation" too |
| R21-4 | Major | TEST-GAP | "Once per member" was false, and `SameMember` saw two members, for an entity that overrides a virtual property an alias names: the compiler writes the base declaration, the alias's target is the override (**fix-introduced**: the claim) | A member is its original declaration's module and token, a property's being its getter's `GetBaseDefinition()` (`MemberKey`), for the memo and `SameMember` alike; `Classify_SeesAnAliasOfAnOverriddenColumnAsTheColumn` and the asks test's overridden-column row (red at rev 21); D102, D103 |
| R21-5 | Minor | TEST-GAP | Two rev 21 branches had no killer: a cast through `object` under another one folded under the verdict's rules again (X21-2), and a row-free cast under a row-reading one evaluated (X21-3) (**fix-introduced**) | `Classify` rows `objObjOrCapturedTrue`, `objOrObjCapturedTrue` and `Classify_UnderACastThroughObject_ReadsNoCapturedValue` (0 reads); D104, D105 |
| R21-6 | Nit | OTHER | The records attributed all 9 over-reads to the verdict (one is the rewrite's, within the cast bound), and "passes, so the counts are 3.11.0's" doesn't follow from a `<=` assertion | §6.5's rev 22 bullet corrects both, and the harness's summary cites round 21's offline count for the equality |
| R21-7 | Nit | OTHER | The harness's new summaries sat on the wrong members | Each back on its own member (`Holder`, `ReadCounter`, `ReadOnce_CountingGetter`) |

**The self-check's findings in rev 22:** the first run of the guard tests on the R21-1 fix aborted with a stack overflow
in `Fold_OfALongChainOfCapturedOperands_StaysLinear` (a 1,500-operand `||` chain, a Debug build): the `Call` case's new
arm grew `FoldNode`'s frame, which the chain repeats at every level, so the arm moved into `FoldCall`. That led to
measuring the chain limit itself (§6.5, `depth22`): every provider's WHERE visitor takes about 15% more stack per `||`
operand than 3.11.0's, at rev 21 as at rev 22, so a chain of about 1,600 to 1,890 operands on a 1 MB stack (about 2,420
to 2,860 on PostgreSQL and MySQL from a console app's main thread) that 3.11.0 translated now ends the process. Recorded as
a Known issue; whether to remove it (a visitor that walks a chain of `||` or `&&` without recursing) was the owner's call:
kept as a Known issue (2026-10-23, rev 23).

### Task 0 round 22 (non-author, over rev 22 and the code, 2026-10-23 12:04): NOT CLEAN, 1 Major, 2 Minor, 1 Nit

Judged by the owner's exit rule. The one Major is a false statement in the product code's comments; the reviewer found
no wrong rows, no delete of more rows than C# selects, no new throw and no twin miss. Every fuzzer and `delreads` TSV on
rev 22 (252 files) was byte-identical to rev 21's. It reran the red run (5 of 451 and SQLite's `R21-1`, as recorded),
D99–D107 and seven older mutants (killed) and seven of its own (X22-1..X22-7), its own probes (`r22 memo|verdicts`),
and measured the chain limit per shape on every provider's visitor offline, at 1 MB and 1.5 MB stacks. It ran no SQL
Server, PostgreSQL or MySQL command.

| # | Sev | Blame | Finding | Rev 23 disposition |
|---|---|---|---|---|
| R22-1 | Major | TEST-GAP | An override of the setter alone was still two members: `MemberKey` keyed a property by its getter's base definition, and the override found on the entity has no getter, so `Classify(x => x.N == x.NX)` was Acceptable there and `x.N` was asked twice; `MemberKey`'s, `SqlOf`'s, the overload's and the unifier's comments, and §5.3, were false for it (**fix-introduced**: R21-4's fix) | A property is the property that declares its getter's or, where it overrides only the setter, its setter's base definition (`FirstDeclaration`, found by the accessor); the setter-only rows of `Classify_SeesAnAliasOfAnOverriddenColumnAsTheColumn` and the asks test (red at rev 22); D108, D111 |
| R22-2 | Minor | TEST-GAP | The long-chain Known issue stated one shape's limit: `&&` chains have it too, a right-deep chain `a \|\| (b \|\| (…))` stops at about 38% fewer operands (the null-semantics rewrite walks only the left spine without recursing), "15% more stack per operand" is about 18%, and the default stack on Windows .NET 8 is 1.5 MB for the main thread, a new thread and the thread pool alike, so SQL Server's right-deep chains of about 1,830–2,100 operands that 3.11.0 ran now end the process (**fix-introduced**: the text) | The Known issue restated per shape at the default stack, from the author's own measurements through the real providers (`depth23-real.tsv`) and round 22's on every visitor: PostgreSQL and MySQL only, since SQL Server refuses such a chain first in both versions ("nested too deeply" at about 1,000 operands, 340 nested to the right, measured: the SQL Server window the finding names doesn't occur) and SQLite at 1,000; judged not Major by the reviewer as by the author (a chain of over 1,200 operands is machine-built; `Contains` is the idiom); the owner kept it a Known issue (Paul, 2026-10-23: "A, keep it as a Known issue") |
| R22-3 | Minor | TEST-GAP | The literal arm under a cast had no neighbour: any string literal counting (X22-1) or any static member named `Empty` (X22-5) survived (**fix-introduced**) | `objNameContainsA` and `objNameContainsOtherEmpty` (Acceptable); D109, D110 |
| R22-4 | Nit | OTHER | "not one held in a variable, field or property": a `const` holding `""` is seen; the verdict row's 3.11.0 rows missed the other operand's | The Known issue names the `const` (pinned by `objNameContainsConstEmpty`); the row says "and, for the second, the other operand's" |

### Task 0 round 23 (non-author, over rev 23 and the code, 2026-10-23 14:17): CLEAN (0 Major, 1 Minor, 1 Nit)

Judged by the owner's exit rule: no wrong rows, no delete of more rows than C# selects, no new throw, no twin miss and no
false product-code comment. The reviewer checked R22-1's fix on fifteen entity types (setter-only and getter-only
overrides at several levels, an abstract base, generic bases and derived generics, `new virtual`, an interface,
explicit implementations, statics, an alias on the base, records' `init`, sealed overrides), reproduced the red run (2
of 454), found all 252 fuzz and `delreads` TSVs byte-identical to rev 22's, reran D99–D111 (killed) and six mutants of
its own, and measured the chain limit on every visitor at 256 KB, 512 KB and 1 MB. It ran no SQL Server, PostgreSQL or
MySQL command. The Minor and the Nit are fixed without another round, as the exit rule allows:

| # | Sev | Blame | Finding | Disposition (after the CLEAN round) |
|---|---|---|---|---|
| R23-1 | Minor | TEST-GAP | The long-chain Known issue exempted SQL Server and SQLite, which holds only on a large enough stack: on a 512 KB thread every visitor stops at about 760–780 operands (3.11.0: about 900–910), 593 nested to the right (3.11.0: 952), before both databases' own limits; SQLite's right-deep limit is 831 ("Recursion limit"), not 1,000; "Windows' default 1.5 MB" is the .NET apphost's (`w3wp.exe` declares 512 KB) (**fix-introduced**: rev 23's text) | The author's own measurement (`depth22\phase512.py`, `depth23-512.tsv`, `depth23-768.tsv`) reproduces the 512 KB numbers exactly (SQL Server's visitor 761 against 898, right-deep 593 against 952; SQLite's 777 against 909) and finds rev 23 past every SQL Server and SQLite limit from 768 KB (1,179 left-deep, 902 right-deep); the Known issue now states the 1.5 MB case for PostgreSQL and MySQL, SQL Server's and SQLite's limits there, and the smaller-stack case for every provider. The limit itself stays (the owner's ruling) |
| R23-2 | Nit | OTHER | §6.4's D111 is D102's twin (the same two failures), D108 is labelled X22-4 though X22-4 dropped the fallback and threw, and the loop's getter and setter arms had no mutant (round 23's X23-2 and X23-3, killed) | §6.4: D111 noted as D102's twin, D108 relabelled, X23-2 and X23-3 recorded as round 23's, killed by the two R22-1 tests in its run |

### Task 8 (two fresh non-author lenses over `25956e0..f9b2b25`, 2026-10-23 15:03): lens A CLEAN, lens B NOT CLEAN (3 Major)

Judged by the owner's exit rule, with the release's own blocking classes (a package or CI defect, a private detail in a
public file, a commit that fails its own tests). **Lens A** (the code, commit by commit): CLEAN, 0 Major. `f9b2b25`'s
code is the frozen rev 23 tree's (line endings aside); each commit builds and passes its DB-free and SQLite tests
(`046232c` 552 and 1,479, `f9b2b25` 566 and 1,479, net48 53 at both); its own fuzzer ran 58,665 predicates through
SQLite's real provider on 3.11.0 and 3.12.0 (every path, deletes rolled back) and the other three visitors offline:
no wrong rows, no over-deletion, no new exception, every new refusal always true in C#, every new acceptance
`!onlyNull.Contains(…)` with C#'s rows; a fresh pack holds 12 DLLs (2 for net48, 5 for net8.0, 5 for netstandard2.0),
all 3.12.0 (lens C's count; lens A said 11). **Lens B** (the release): the package
right (versions, dependencies, nothing private), but one CI line and two user-document statements false.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| T8B-1 | Major | TEST-GAP | The TRX-gate steps of `test-net8` and `test-postgresql` ended their allow list with one `)` too many: PowerShell can't parse them, so both jobs fail on every run and `publish`, which needs them, never runs; `CiWorkflowTests` read the step's text only, and the author's gate runs called the script directly | The extra `)` removed; `CiWorkflowTests.EveryTestJob_GateStepRunsAsWritten` runs each job's gate step as GitHub's pwsh shell does (its text in a file, `$ErrorActionPreference = 'stop'`, dot-sourced from a copy of the repository root) on a TRX where every test passed: red on exactly `test-net8` and `test-postgresql` (`ParserError … Unexpected token ')'`, `red-t8-gate.txt`), green after |
| T8B-2 | Major | TEST-GAP | Usage.md ("A delete that this makes always true is refused") and the AI instructions ("refused if that makes it always true") promised what the Changelog's Known issue denies: `p.HospitalId == 5 \|\| p.HospitalId != 5` is accepted and deletes every row | Both say the guard refuses only the plain all-rows shapes, keep the refused `!Contains(null)` example, and name the accepted one, pointing to the Known issue |
| T8B-3 | Major | TEST-GAP | "Every comparison whose operands can't be null is unchanged" and "wrong rows elsewhere when an operand was null" were false for a `bool` compared with a predicate: 3.11.0 sent `a = b = c` without parentheses, which SQLite and MySQL read as `(a = b) = c`, other rows than C#'s with no NULL anywhere (lens B: `(x.Id == 1) == (x.Code == 5)` over NOT NULL columns, 3.11.0 `[]`, C# and 3.12.0 `[1,2,6]`) | The Changelog's opening sentence and the `bool` bullet, and Usage.md's "What to expect" bullet, now say it changed whether or not an operand is null, and why (§3's precedence caveat restored) |
| T8B-4 | Nit | OTHER | ci.yml's comments: the PostgreSQL allowlist "re-derived on this job's first run" (it has never run), the moved jobs' commands "unchanged" (they gained `--results-directory`) | Reworded |
| T8B-5 | Nit | OTHER | The Changelog's Fixed entry named only the `[SubqueryAggregate]` examples; R2 corrected the `[JsonCollection]` ones too | The entry names both |
| T8A-1 | Minor | TEST-GAP | `Fold_OfALongChainOfCapturedOperands_StaysLinear`'s bound (four times the row chain plus 15 ms) failed once by 0.8 ms under load: the linear fold takes six to eight times the row chain's time, so a slow CI runner could block `publish` | The bound is ten times plus 50 ms. Z34 (the quadratic walk it guards) still fails it in Debug, the mutation runner's build (`mutations-t8-z34.md`: 2 failed, this test and `Fold_AsksASmallOperandFirst_UpToItsBound`); in Release, which `test-net8` runs, the 50 ms swamps Z34's slowdown and the test passes under Z34, which `Fold_AsksASmallOperandFirst_UpToItsBound` still kills there (lens C, T8C-3): the timing test is a coarse guard, the deterministic one the proof |
| T8A-2 | Nit | OTHER | `FUNKYORM_AI_INSTRUCTIONS_POSTGRESQL.md`, which the package ships, held five cp1252 dashes (`0x97`), invalid UTF-8 (since before 3.11.0) | Each repaired as the em dash it was; the file decodes as UTF-8 |

The fix layer is one commit on `f9b2b25`; a fresh non-author pass over it, and over the whole range for the release's
classes, gives the push its verdict.

**Lens C** (a fresh non-author pass over the fix layer `f9b2b25..cd07f8a` and the release at `cd07f8a`, 15:35): NOT
CLEAN, 2 Major, 2 Minor, 1 Nit. T8B-1, T8B-2, T8B-4, T8B-5 and T8A-2 resolved (T8B-1 red with the `)` put back, on
exactly the two jobs; each gate step replayed as GitHub runs it, green on the author's TRX files and red on one
unlisted inconclusive); every commit builds and passes its DB-free and SQLite tests; the package's 12 DLLs all
3.12.0, its 12 documents valid UTF-8.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| T8C-1 | Major | TEST-GAP | T8B-3's new text said `(a = b) = c` selects other rows "unless each side's operator binds tighter than `=`": only the right-hand operand matters; a predicate on the left compared with a column or a value read as C#'s (`(x.Code == 5) == x.Flag`, `x.Name.Contains(a) == false`), and `LIKE` binds tighter than `=` on PostgreSQL (**fix-introduced**) | The bullet restated from lens C's SQLite measurements: SQL Server rejected `a = b = c`, PostgreSQL for most shapes (`LIKE`, `IN` bind tighter); SQLite and MySQL split a predicate on the right (on SQLite unless `<`, `<=`, `>`, `>=`), `x.Flag == (x.Code == 5)` and `(x.Id < 3) == (x.Code == 5)` selecting no row where C# selects four or five; a predicate on the left wrong only with a null operand. Usage.md's sentence says "splitting the predicate on the right" |
| T8C-2 | Major | TEST-GAP | The Changelog filed R4-10 (a computed or remote integer's `ToString()` as `COALESCE(…, '')`) under "SQL text that changes where the rows don't", but `ToString() == ""` now selects, and deletes, the rows whose value is NULL (3.11.0: none); missed by lenses A and B | The sub-list is "SQL text that changes, where the rows don't or as stated", and the bullet states the row change (`int?` columns already sent the `COALESCE`) |
| T8C-3 | Minor | TEST-GAP | T8A-1's "the mutant it guards still fails it" holds in Debug only: in Release the 50 ms swamps Z34's slowdown | The record says so: Z34 fails the timing test in Debug and is killed in Release by `Fold_AsksASmallOperandFirst_UpToItsBound`; the timing test is a coarse guard |
| T8C-4 | Minor | TEST-GAP | `EveryTestJob_GateStepRunsAsWritten` ran each step under pwsh whatever its `shell:`, so a step without `shell: pwsh`, which an ubuntu runner hands to bash, would stay green | The test asserts each gate step declares `shell: pwsh`: red with it removed from `test-postgresql` (`red-t8c4.txt`), green restored |
| T8C-5 | Nit | OTHER | The record said 11 DLLs; the package holds 12 | Corrected |

The second fix layer is one commit on `cd07f8a`; a fresh non-author pass over it gives the push its verdict.

**Lens D** (a fresh non-author pass over `cd07f8a..e68b2e4` and the release at `e68b2e4`, 16:05): NOT CLEAN, 1 Major,
1 Minor, 1 Nit. The release verified clean (the build, DB-free 693 and SQLite 1,479, every gate step replayed green and
red on a planted inconclusive, the package 3.12.0 throughout, the commits, nothing private); T8C-3..T8C-5 resolved,
T8C-1 and T8C-2 partial.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| T8D-1 | Major | TEST-GAP | "`==` against a non-null value is unchanged" (the Changelog's Behaviour change opener, Usage.md's "What to expect", the shipped AI instructions) is false for a conditional whose test changes, `(x.N != 5 ? 1 : 0) == 1` (SQLite: 3.11.0 [2,5,6,7], 3.12.0 and C# [2,3,4,5,6,7]), and a computed or remote integer's `ToString() == ""`, both documented elsewhere in the same Changelog; there since `f9b2b25` | All three say "a column compared by `==` with a value that isn't null (`x.N == 5`) is unchanged" and point to the Changelog's list (Usage.md names the families, these two among them). The author's sweep of the section for other blanket claims found one more: "queries return more rows" (the release notes, README's 3.12.0 line), false for a complement shape (`!x.Flag == null`, was `NOT flag IS NULL`); both now say C#'s rows, most often more |
| T8D-2 | Minor | TEST-GAP | The restated `a = b = c` rule was imprecise: 3.11.0 kept parentheses around `&&`/`\|\|`, SQLite reads a `!` on the right as a prefix, MySQL's grammar takes a whole `LIKE`/`IN` predicate on the right, and PostgreSQL ran a null test on the right with other rows (**fix-introduced**: T8C-1's text) | Shortened to what is certain: 3.11.0 kept parentheses only around `&&` and `\|\|`; SQL Server rejected the comparison, PostgreSQL two comparisons in a row; on SQLite and MySQL some shapes selected other rows with no NULL (the two measured examples), others where an operand was null. `e68b2e4`'s commit message keeps the longer rule (a record) |
| T8D-3 | Nit | OTHER | The timing test's comment still called it a timing proof | It is a smoke check; the comment names `Fold_AsksASmallOperandFirst_UpToItsBound` as Z34's pin |

The third fix layer is one commit on `e68b2e4`. Each lens so far found statements in the user documents that the one
before it missed, so the next pass reads every sentence of the 3.12.0 user text, not only the fix layer.

**Lenses E1 and E2** (in parallel over `91dfc8c`, every sentence of the 3.12.0 user text given a verdict, 16:39 and
16:54): E1 NOT CLEAN, 2 Major, 2 Minor, 3 Nit (the Changelog's release notes, Behaviour change and verdict table; 219
SQLite query cases with C#'s rows at 3.12.0, 111 accepted deletes with the query's rows); E2 NOT CLEAN, 4 Major, 2
Minor, 3 Nit (the rest of the Changelog, Usage, Advanced, README, the AI instructions, the fix layer and the release: the
release clean). One of E2's Majors is a code defect, the only one Task 8 found:

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| T8E2-4 | Major | AC-GAP | A `[SubqueryAggregate]` `Sum` declared non-nullable counted as able to be NULL (§4.1's "whatever its CLR type"), so `x.Total != 0` gained `OR SUM IS NULL` and selected, and deleted, every row with no children, where C# over the rows read back (0), EF Core (`COALESCE(SUM(…), 0)`) and 3.11.0 all exclude them; `== 0` missed them in both versions. The Known issue's "as EF Core does" was false for a `Sum` | **Code:** the providers send a non-nullable `Sum` as `COALESCE(SUM(…), 0)`, EF's form, and `NullSemantics.CanBeNull` counts it as unable to be NULL; a `Sum` declared nullable keeps the NULL. Red first: the harness's `childIdSumNe0` and `childIdSumEq0` (an `int` Sum against 0; SQLite failed both, `red-t8e2-sum-sqlite.txt`), `childIdSumNNe0` (the nullable one, green before and after), and the core tests' `ChildSum`, `ChildSumN`, `ChildAvg` rows; D112, D113l/m/p/y, D114. **Documents:** a Behaviour change entry, the `!=` bullet's list (`Avg`, a nullable `Sum`), the Known issue (a missing parent, an absent JSON key, an `Avg`), §4.1 |
| T8E1-1, T8E2-1 | Major | TEST-GAP, AC-GAP | A `bool` column compared with a `bool` worked out from captured values (`x.Active == (mode == "archived")`) changes its rows and deletes with no NULL anywhere (3.11.0 sent `active = @p0 = @p1`: SQLite [] where C# selects four; a delete of the `!=` form deleted every row where C# deletes three), but the Changelog's opener, its "SQL text" item, Usage.md and the AI instructions said a column's `==` against a non-null value is unchanged (**fix-introduced**: `91dfc8c`'s rewording) | The lenses' texts: the opener names "a constant or a captured variable" and the family; the bullet says how 3.11.0 sent it and what it selected; the SQL-text item and Usage.md's and the AI instructions' sentences likewise (the harness already pins the shape: `flagEqFoldedPredicate`, red at `25956e0`) |
| T8E1-2 | Major | TEST-GAP | The verdict table's row 3 said every negated comparison with a null value is refused; `!(x.N == none)` (`n IS NOT NULL`) and `!(x.N != none)` are accepted (the plan says "relational") | The row names the negated `>`, `>=`, `<`, `<=`, and its 3.11.0 cell says what an `\|\|` deleted |
| T8E2-2 | Major | TEST-GAP | Usage.md: "Before 3.12.0 these shapes dropped the NULL rows"; for some the page lists, 3.11.0 selected no one or other rows | E2's text: most dropped the NULL rows, some selected other rows (three named) |
| T8E2-3 | Major | TEST-GAP | README, the AI instructions and AI_ADVANCED said `!` and a negated `IN` keep, or select, the NULL rows; several such shapes rightly don't (`!(x != 5)`, `!(x == none)`, a negated `IN` with a null item) | E2's texts, which name the shapes that do and those that don't |
| T8E1-3, T8E1-4 | Minor | TEST-GAP, OTHER | 3.11.0's rows in the verdict table's rows 3 and 10 and the SQL-text item 7 (MySQL reads `flag = n > @p` as `(flag = n) > @p`); `All` over a null search value is EF's `false`, C# throws | E1's texts (item 7 says SQLite's rows differ where `N` is NULL, which E1's proposal left out) |
| T8E2-5, T8E2-6 | Minor | TEST-GAP, OTHER | Advanced.md §5 and AI_ADVANCED said the object test throws on an unordered query, root included (it throws after a `Where`); 42601 was named for every `bool` compared with a predicate (a `LIKE` or `IN` on the left ran) | E2's texts |
| T8E1-5..T8E1-7, T8E2-7..T8E2-9 | Nit | OTHER | "D8" undefined; `NOT 1=0` or `NOT NOT 1=0`; the database's collation, not a column's; the timing test's "six to eight"; Msg 125's depth; "every attribute example" | Each reworded as the lens gave it |

**The fourth fix layer's runs** (`nullsem-green24`, `nullsem-final24`): the Sum keys red first on SQLite
(`childIdSumNe0`, `childIdSumEq0`); 233 mutants (`mutate27.py`: rev 23's 226, M-N11 re-anchored on the new rule,
D112, D113l/m/p/y, D114), all killed, the code restored byte for byte; the four full suites with coverlet: SQLite
1,482, SQL Server 3,441 / 3,442 (the allowed NotExecuted), PostgreSQL 2,016, MySQL 1,958, the TRX gate on each,
net48 53 / 53; each provider's two new lines covered. Rounds 17's and 19's probes (the only ones with a
`[SubqueryAggregate]`) gave rev 23's outputs; no fuzzer's entity has one. (This layer's COALESCE was withdrawn by the
owner's ruling below.)

**Lens F** (a fresh non-author pass over `91dfc8c..5563d43`, the release at `5563d43`, 19:23): NOT CLEAN, 1 Major, 5
Minor, 3 Nit. The COALESCE fix gave C#'s rows on every SQLite path and EF Core 10's; the release clean (DB-free 693,
SQLite 1,482, every gate step replayed, 12 DLLs all 3.12.0, D112–D114 and M-N11 killed with the author's counts). But the
COALESCE was a CASE level SQL Server counts and nothing in FunkyORM did (T8F-2), SQL Server evaluates a subquery under a
`COALESCE` twice (T8F-5), PostgreSQL can't type `COALESCE(SUM(money_or_interval), 0)` (its UNVERIFIED 2: every read of
such an entity would fail where 3.11.0 worked), and a non-nullable `Avg` kept the compensation the Sum lost (T8F-3).
**The owner's ruling (Paul, 2026-10-23, asked with these risks): "Keep 3.11.0's SQL"**: a non-nullable
`[SubqueryAggregate]` `Sum` or `Avg` is the bare `SUM`/`AVG`, not counted as able to be NULL, so its rows are exactly
3.11.0's; `x.Total == 0` leaving out a parent with no children is a Known issue; EF's COALESCE is a follow-up with a typed
zero per provider.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| T8F-1 | Major | AC-GAP | Usage.md's and the AI instructions' "a column compared by `==` with a constant or a variable that isn't null doesn't change" was false for the COALESCE'd Sum (`x.Total == 0` selected the parents with no children; 3.11.0 didn't) (**fix-introduced**) | Moot under the ruling: a non-nullable Sum's `==` is 3.11.0's again, so the sentences are true; the Known issue states the Sum's and Avg's rows |
| T8F-2, T8F-5 | Minor | PLAN-GAP, OTHER | The COALESCE's uncounted CASE level on SQL Server; its double evaluation there; "as EF Core sends" described EF's text, not FunkyORM's | Moot under the ruling: no COALESCE is sent |
| T8F-3 | Minor | TEST-GAP | Nothing pinned the Avg's rows (a mutant COALESCE-ing it, and one counting it as unable to be NULL, survived the SQLite suite) | Under the ruling a non-nullable Avg is uncompensated like the Sum; the harness's `childAvgDNe0` (a `decimal` Avg; red at `5563d43` as `childIdAvgNe0`, a `double` one, which compensated it) and the core tests' `ChildAvg`/`ChildAvgN` rows pin it; D115 |
| T8F-4, T8F-8 | Minor, Nit | OTHER | A `decimal` Sum on SQLite compares as text with COALESCE's integer 0; a Sum whose children's values are all NULL | Moot under the ruling (the bare SUM is 3.11.0's; the decimal Known issue stands as it was) |
| T8F-6 | Minor | TEST-GAP | The verdict table's "A `bool` compared with the same operand" 3.11.0 cell said SQL Server and PostgreSQL rejected the `!` forms; 3.11.0's SQLite accepted `x.Id == 2 \|\| !x.Active == !x.Active` and deleted every row | The cell says so for SQLite, SQL Server's rejection of both forms, PostgreSQL's of the predicate forms; it doesn't claim PostgreSQL's or MySQL's reading of the `!` form, which no one ran |
| T8F-7 | Nit | OTHER | On MySQL literals and parameters take the connection's collation | "(on MySQL, the connection's)" |
| T8F-9 | Nit | OTHER | The runs paragraph held a broken machine path (a TAB and a line feed) | Folder names only |

The ruling's layer: the harness's `childSumDNe99`, `childSumDRoundTripNe99`, `childIdSumEq0`, `childIdAvgNe0` (now `childAvgDNe0`) and the core
tests' `Classify_ByOperandKind`, `CanBeNull_ByPropertyKind` red at `5563d43` (`red-ruling-sqlite.txt`: 4 of 319;
`red-ruling-core.txt`: 2 of 53), green after; the four providers' files are `91dfc8c`'s again, byte for byte. Its runs
(`nullsem-green25`, `nullsem-final25`): 229 mutants (`mutate28.py`: rev 23's 226, M-N11 re-anchored, D112, D115), all
killed, the code restored byte for byte; the first four-suite run failed SQL Server's whole harness (319) on the
harness's own new column, a `double` Avg over an `int`: SQL Server's AVG of an int is an int, which FunkyORM doesn't
read into a `double` (a pre-existing limit, for the backlog), so the key is a `decimal` Avg over the amount
(`childAvgDNe0`), and D112, D115 and M-N11 ran again on it, killed; then SQLite 1,483, SQL Server 3,442 / 3,443 (the
allowed NotExecuted), PostgreSQL 2,017, MySQL 1,959, the TRX gate on each, net48 53 / 53. The Changelog's heading is
dated 2026-10-24.

**Lens G** (a fresh non-author pass over `5563d43..f9c135e` and the release at `f9c135e`, 2026-10-24 02:22): NOT CLEAN, 1
Major, 1 Minor, 4 Nit. The ruling's code checked: the four providers' files `91dfc8c`'s, their SQL per property 3.11.0's,
301 of 357 non-nullable cases on SQLite 3.11.0's in rows, SQL and verdict (the rest the two classes below or C#'s rows for
a nullable aggregate); D112, D115, M-N11 and four mutants of its own killed; the release clean (DB-free 693, SQLite 1,483,
every gate step, 12 DLLs all 3.12.0, the heading dated 2026-10-24, nothing private, no user text describing the
withdrawn COALESCE).

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| T8G-1 | Major | AC-GAP | Where 3.12.0 sends a predicate as its two-valued `CASE` (a `bool` compared with a predicate, AC-N14; a conditional's predicate branch compared with a value, AC-N16), a predicate over a non-nullable `Sum`/`Avg`, NULL for a parent with no child rows, is false, so `(x.Id > 2 ? x.Total == 0 : x.Flag) == false` selects, and deletes, such parents, which C# and 3.11.0 leave out (SQLite: C# and 3.11.0 [2,6,8], 3.12.0 [2,3,4,6,7,8]) | **The owner's call (Paul, 2026-10-24): "Known issue, ship"**, EF's `COALESCE` with a typed zero per provider the follow-up that removes it and the ruling's Known issue. The Known issue states the two shapes and their rows (lens G's text, but "which 3.11.0 didn't select where its text ran": for `x.Paid != (x.Total == 0)` 3.11.0's text was `a = b = c`, which PostgreSQL rejects); `CanBeNull`'s summary names it |
| T8G-2 | Minor | OTHER | "compared as 3.11.0 compared it" was broader than true: such an aggregate counts as a column that can't be null, so a null value is `1=0` / `NOT 1=0` (`x.Total != none` selects every row and its delete is refused; 3.11.0 selected nothing) | The `!=` bullet, the Known issue and `CanBeNull` say it counts as unable to be NULL, as 3.11.0 compared it with a value, and a null value is `1=0` / `NOT 1=0` |
| T8G-3 | Nit | OTHER | Child values all NULL read back 0 too, and ORDER BY sorts the SQL NULL | The Known issue says both |
| T8G-4 | Nit | OTHER | `CanBeNull` opened "Whether the SQL value … can be NULL", and left the integer `ToString()` out of its users | Reworded as lens G gave it |
| T8G-5 | Nit | OTHER | The plan's §3 said WHERE compares the SQL value "as EF does" (EF sends COALESCE for a Sum), and named `childIdAvgNe0` (a `double`) for the `decimal` `childAvgDNe0`; `f9c135e`'s message stated lens F's unverified PostgreSQL claim as fact | The plan corrected; the commit message carried (a record) |
| T8G-6 | Nit | TEST-GAP | `childIdSumEq0` expects no row, which a translation selecting nothing passes | `childIdSumEq3` (parents 1 and 5; red under lens H's "select nothing" mutant H-M1) |

The lens G layer changes documents, `CanBeNull`'s summary and one harness key, so no mutant moved; its runs
(`nullsem-green26`, `nullsem-final26`): SQLite 1,484, SQL Server 3,443 / 3,444 (the allowed NotExecuted), PostgreSQL
2,018, MySQL 1,960, the TRX gate on each, net48 53 / 53.

**Lens H** (a fresh non-author pass over `f9c135e..5e2f810` and the release at `5e2f810`, 2026-10-24 16:00): **CLEAN**, 0
Major, 4 Minor, 1 Nit, fixed after it as the exit rule allows. Every sentence the layer added true (on SQLite on both
builds, EF Core 10 on SQLite); `childIdSumEq3` correct; the code `f9c135e`'s but the comment; the release clean (DB-free
693 + 94, SQLite 1,484, `final26` this commit's code, every gate step, 12 DLLs all 3.12.0, the heading, 16 commits with
the trailer, nothing private).

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| T8H-1 | Minor | AC-GAP | The Known issue named fewer two-valued shapes than the code sends: a conditional with the predicate as a branch is two-valued compared with a column or a predicate too (`(x.Id > 2 ? x.Total == 0 : x.Paid) == x.Other`) | The same mechanism, so under the owner's "Known issue, ship"; the Known issue names it (lens H's text) |
| T8H-2 | Minor | AC-GAP | A conditional whose test the aggregate's NULL leaves unknown takes its ELSE branch, as 3.11.0's did, so `(x.Total == 0 ? 0 : 1) == 1` selects, and deletes, a parent with no child rows | The Known issue says so (3.11.0's rows, which the ruling keeps) |
| T8H-3 | Minor | TEST-GAP | No test pinned the Known issue's two-valued rows (lens H's H-M2, counting the NULL as true, survived) | `activeNeChildIdSumEq0Pin`, `condChildIdSumEq0EqFalsePin` (lens H's keys, red under H-M2 on its copy) |
| T8H-4 | Minor | OTHER | "the predicate is false for a parent with no child rows" over-warned (an operand that decides it keeps C#'s rows) and left out the all-NULL child values | "false wherever the aggregate's NULL leaves it unknown, for a parent with no child rows or whose child values are all NULL"; `CanBeNull` likewise |
| T8H-5 | Nit | OTHER | The plan's N1 row and §3, and `5e2f810`'s message | The plan corrected; the commit message carried |

**Blame counts:** round 1: AC-GAP 4, TEST-GAP 5, HOUSE-RULE 3, PLAN-GAP 3. Round 2: TEST-GAP 4, PLAN-GAP 4, AC-GAP 3.
Round 3: AC-GAP 4, TEST-GAP 4, PLAN-GAP 1, OTHER 1. Round 4: TEST-GAP 5, AC-GAP 3, PLAN-GAP 6. Round 5: AC-GAP 2,
TEST-GAP 4, HOUSE-RULE 1, PLAN-GAP 3. Round 6: AC-GAP 2, TEST-GAP 5, PLAN-GAP 1, OTHER 1. Round 7: TEST-GAP 3,
HOUSE-RULE 1, AC-GAP 1, PLAN-GAP 1, OTHER 2. Round 8: AC-GAP 2, TEST-GAP 2, OTHER 1. Round 9: TEST-GAP 2, AC-GAP 2,
OTHER 1. Round 10: AC-GAP 2, TEST-GAP 2, OTHER 1. Round 11: AC-GAP 3, TEST-GAP 2, OTHER 1. Round 12: TEST-GAP 1
(R12-5, the author's), AC-GAP 1, PLAN-GAP 1, OTHER 2. Round 13: AC-GAP 3, TEST-GAP 1, OTHER 1. Round 14: AC-GAP 2,
TEST-GAP 1, OTHER 1. Round 15: AC-GAP 1, TEST-GAP 2, PLAN-GAP 3, OTHER 1. Round 16: TEST-GAP 2, PLAN-GAP 4, OTHER 1. Round 17: TEST-GAP 4, PLAN-GAP 2, OTHER 1. Round 18: TEST-GAP 3, PLAN-GAP 1, OTHER 4. Round 19: AC-GAP 1, TEST-GAP 2, PLAN-GAP 1, OTHER 1. Round 20: TEST-GAP 4, HOUSE-RULE 1, PLAN-GAP 1, OTHER 1. Round 21: TEST-GAP 3, HOUSE-RULE 1, PLAN-GAP 1, OTHER 2. Round 22: TEST-GAP 3, OTHER 1. Round 23: TEST-GAP 1, OTHER 1. Task 8: TEST-GAP 4, OTHER 3; lens C: TEST-GAP 4, OTHER 1; lens D: TEST-GAP 2, OTHER 1; lenses E1 and E2: AC-GAP 2, TEST-GAP 6, OTHER 8; lens F: AC-GAP 1, TEST-GAP 2, PLAN-GAP 1, OTHER 5; lens G: AC-GAP 1, TEST-GAP 1, OTHER 4; lens H (CLEAN): AC-GAP 2, TEST-GAP 1, OTHER 2.
**Two fix-introduced findings in round 2 (R2-1, R2-2):** per the house rule, rev 3 narrows the design instead of
patching.
