# Query Operator Correctness (#12, #13) — Implementation Plan

> **Goal**: Fix the two query-translation defects logged after 3.9.0 — [#12](https://github.com/FuncularLabs/Funcular.FunkyOrm/issues/12)
> (unqualified own-column `ORDER BY` on remote-join entities) and [#13](https://github.com/FuncularLabs/Funcular.FunkyOrm/issues/13)
> (LINQ operators silently ignored or mistranslated) — across all four providers, test-first.
> - **v3.10.0** (branch `development/3.10`) ships the critical fixes. §1–§9 cover it.
> - **v3.10.1** ships aggregate correctness (D9). Its design is still being settled with the owner (§10).
>
> This is a **prerequisite for the sponsored-tutorial program**. A tutorial audience coming from EF will reach
> for `SingleOrDefault` and the narrow-projection paging idiom on day one.

> **Status (2026-10-01)**:
> - All decisions D1–D12 are made by the owner, and the owner has answered the three §10 questions.
> - **Task 0 is complete:** fix-verification r16 of rev 16 (`ed772fb`) is CLEAN, with nits only (§9.16).
>   The §3 ACs are posted to #12/#13, and the rev 21–29 amendments to #13.
> - **Tasks 1–10 are complete** on all four providers (§6 statuses). Task 11's gauntlet ran through the first push:
>   `development/3.10` is at `66bde1e` on origin, the sha the push gate's sentinel holds. Its release steps remain.
> - **Task 12** (owner decision 2026-10-01: ORDER BY values as parameters, before the beta) is in its review loop
>   (§9.33 onward).
> - **Then:** the PR, `3.10.0-beta1`, the Sentinel smoke test, and `3.10.0`.

> **Revision 44 (fix-verification of `66bde1e..4b7ab2b`, 2026-10-01) — what changed:**
>   - The MySQL harness discovers `Person` before each test, so its cleanup works when a test runs alone (N1).
>   - The `DateOnly`/`TimeOnly` test also runs under th-TH (N2).
>   - §4.3 scope (N3); the §9.45 count (N4); the Task 12 status and §9.46.

> **Revision 43 (fix-verification of `66bde1e..8eaa0d0`, 2026-10-01) — what changed:**
>   - The MySQL `DateTimeOffset` test seeds through the harness, so it passes when run alone (M1).
>   - `ParameterMode_DateOnlyAndTimeOnly_AreIsoText` runs under fi-FI (M4).
>   - AC12-10's PostgreSQL sentence (M2); the Changelog list made tight again (M3).
>   - §4.3 rows for the parameter members (M5); wording (M6); the Task 12 status and §9.45.

> **Revision 42 (fix-verification of `66bde1e..9f2d531`, 2026-10-01) — what changed:** docs only.
>   - The Changelog's formats list is scoped to values sent as parameters (L1).
>   - The PostgreSQL null-test wording in Advanced.md and the AI reference (L2).
>   - The AC12-10 matrix row and mutation table are brought up to revs 39–41 (L3).
>   - Bookkeeping (L4); the Task 12 status and §9.44.

> **Revision 41 (fix-verification of `66bde1e..6f503a9`, 2026-10-01) — what changed:** pins for all seven
>   `TimeOnly` digits (K3) and for the eleven inline numeric types, with `nint`/`nuint`/`Half` as parameters (K4).
>   Prose (K1, K2, K2b, K5–K7):
>   - the Changelog's Security, API and formatting bullets, and the AC12-10 lead-in, claim no more than is sent;
>   - AC12-10's lazy continuation;
>   - the four visitors' comments, Advanced.md and the AI reference;
>   - the Task 12 status and §9.43.

> **Revision 40 (fix-verification of `66bde1e..9ce9e86`, 2026-10-01) — what changed:** a `TimeOnly` fraction pin
>   and oracle row (F1). Prose (F2–F5):
>   - the Changelog's Security, Changed and "Other changes:" text;
>   - the numeric types listed in the Changelog, Advanced.md and AC12-10;
>   - a §8 entry;
>   - the Task 12 status and §9.42.

> **Revision 39 (fix-verification of `66bde1e..26abd27`, 2026-10-01) — what changed:** code and prose (J1–J6).
>   - `DateOnly`/`TimeOnly` in ORDER BY are ISO text (J3).
>   - A MySQL `DateTimeOffset` is its UTC time (J2).
>   - AC12-10's per-position `DateTimeOffset` claims are removed (J1, J5), and the numeric types named (J4).
>   - The Changelog regrouped (J6).
>   - §9.40's H1 row annotated, the Task 12 status, and §9.41.

> **Revision 38 (fix-verification of `66bde1e..89383ff`, 2026-10-01) — what changed:** plan prose (H1–H4), and
>   the Changelog's `DateTimeOffset` bullet.
>   - AC12-10: the culture-invariant text sentence and the `DateTimeOffset` result.
>   - §8 and §9.37 provenance; §9.39's verdict line.
>   - The Task 12 status and §9.40.

> **Revision 37 (fix-verification of `66bde1e..141654d`, 2026-10-01) — what changed:** plan prose only (G1–G5).
>   - §8: the SQL Server constant-test entry; a new pre-existing SQLite `DateTimeOffset` entry.
>   - AC12-10's result sentence.
>   - §4.2/§4.4 rows for the rev 33 literal-mode test.
>   - Rev 36's header, the Task 12 status, and §9.39.

> **Revision 36 (fix-verification of `66bde1e..1458516`, 2026-10-01) — what changed:** plan prose only (F1–F5).
>   - §8: the SQL Server constant-test entry, and the SQLite culture and WHERE entries.
>   - The status block, rev 35's header, and the Task 12 status.
>   - §9.38, and §9.37's O1 line.

> **Revision 35 (fix-verification of `66bde1e..0568a3f`, 2026-10-01) — what changed:** prose only (N1–N4).
>   - The status block above.
>   - Two §8 SQLite entries.
>   - A new §8 entry for a pre-existing SQL Server constant-ORDER BY case.
>   - §9.37 and the Task 12 status (verification layer 4).
>   - Outside the plan: the PostgreSQL null-test sentence in both operator tables.

> **Revision 34 (fix-verification of `66bde1e..4e9030f`, 2026-10-01) — what changed:** no product code changed.
>   - §8: the SQLite culture entry now names the write path too (F1).
>   - AC12-10 wording (F2).
>   - The status block above is brought up to date.
>   - §9.36 and the Task 12 status.
>   - Outside the plan: the operator-table wording (F3), and the probe table becomes a fixture table (F4).

> **Revision 33 (fix-verification of `66bde1e..403597b`, 2026-10-01) — what changed:** literal mode no longer
>   looks for placeholders (R1). The AC12-10 LINQ-parity sentence is narrowed to the test position (R3). Also
>   changed: §8 (literal-mode wording, an ar-SA entry), §9.35, the Task 12 status, and outside the plan the
>   Changelog (equality under `nvarchar`, R2) and the operator-table wording (nit A).

> **Revision 32 (fix-verification of `66bde1e..7d9b65b`, 2026-10-01) — what changed:** AC12-10 amended again.
>   Each occurrence of a value is its own parameter, as each literal was its own literal; rev 31's shared
>   parameters were typed only at their first use on PostgreSQL. A PostgreSQL value compared with `null` is decided
>   in .NET. Also changed: §4.2/§4.4 rows, the Task 12 status (with corrected red counts), §8 (one entry removed),
>   and §9.34.

> **Revision 31 (Task 12 hostile review of `66bde1e..e104aad`, 2026-10-01) — what changed:** AC12-10 amended. Each
>   value is bound as the text 3.9.0 quoted and typed as the literal was, not "like a WHERE parameter" (§9.33).
>   Also changed: the §4.2/§4.4 rows, the Task 12 status, and five §8 entries.

> **Revision 30 (Task 12, owner decision 2026-10-01: parameterize ORDER BY values before the beta) — what changed:**
>   AC12-10, Task 12, §4.2/§4.4 rows. 3.11.0 confirmed by the owner for `UpdateWhere` and `[ReadModel]` (separate plan).

> **Revision 29 (verification v7 of `de9bf1e..6d64733`, 2026-10-01) — what changed:** every Changelog sentence
>   was confirmed by execution. One plan ledger line was corrected (§9.32), and a pre-existing `.HasValue` defect was
>   recorded in §8.

> **Revision 28 (verification v6 of `3f38bb6..de9bf1e`, 2026-10-01) — what changed:** 3 prose nits (§9.31). Claims
>   that execution contradicted were removed rather than qualified.

> **Revision 27 (verification v5 of `92bedba..3f38bb6`, 2026-10-01) — what changed:** 5 nits, no code defect (§9.30).
>   The ELSE branch is pinned. Changelog and docs wording on the null, enum and captured-`this` changes is corrected
>   (refined in rev 28). Comment and §8 wording fixed.

> **Revision 26 (verification v4 of `2720c28..92bedba`, 2026-10-01) — what changed:** 2 minor and 4 nits (§9.29).
> - **AC12-8:** only `Convert` is read through; `ConvertChecked` is evaluated, as in 3.9.0. Branch values share the
>   test's operand path, so a captured instance property reads the same in both positions (was `NULL`).
> - §9.27/§9.28 bookkeeping corrected.
> - §8: PostgreSQL enum parameters; `char` comparisons in checked projects.

> **Revision 25 (verification v3 of `3457e61..2720c28`, 2026-10-01) — what changed:** 1 regression and 3 partials
> closed, plus 3 nits (§9.28).
> - **AC12-8:** operands are read through conversions, as before 2720c28, so a captured `char` stays `'x'`.
>   Enum constants are their underlying number.
> - An operand is evaluated once, also when it throws. Block and catch variables count as declared.
> - The gx-F5 pin on MySQL.
> - §9.27's equivalence claim is narrowed.
> - §8: the MySQL cold-cache `Delete`, and the corrected SQLite `rowid` scope.

> **Revision 24 (Task 11 gauntlet: full-branch pass and fix-verification on `3457e61`, 2026-10-01) — what changed:**
> 9 findings (§9.26, §9.27).
> - **AC12-8:** the null may be any operand that reads no parameter of the ordering lambda, including one with a lambda
>   of its own. Each operand is evaluated once.
> - **§9.24:** the r67 "equivalent mutant" claim is corrected.
> - **§4.2:** pins for `Single` after `Take(k > 2)`, a duplicate `CASE` key, own columns on a computed-attribute entity
>   without joins, and the 3.9.0 visitor constructor.
> - **Version 3.10.0-beta1** (the Task 11 bump).
> - **§8:** five pre-existing items found by the reviewers.

> **Revision 23 (hostile review r810 of `1d35177..194b917`, 2026-10-01) — what changed:** 10 findings (§9.25).
> - **AC13-5:** the `Skip(0)` example is qualified for SQLite (r810-F2).
> - **§4.2:** `LongCount(pred)` over forward and reverse remote columns (F1). The doc test rejects duplicate and
>   operator-less rows (F5).
> - **Docs:** 3.9.0 claims corrected (F2, F3, N4); "fixed in 3.10.1" → "planned for 3.10.1" (F4); the
>   `Last`/`Distinct` reason (N1); README known issues (N2); `LongCount` in the reverse-join rows, "five areas" (N5).
> - **§8:** SQL Server `Average` truncation is marked confirmed (N3).

> **Revision 22 (hostile review r67 of `abb41ad..1d35177`, 2026-10-01) — what changed:** 7 findings (§9.24).
> - **AC12-8:** a null held in a variable counts as null (r67-1).
> - **AC13-2:** `Last` follows the provider's own NULL placement (r67-4). The `Distinct` guard names any
>   `Last*` terminal and states a reason that holds when the key is projected (r67-3/5). The no-`Id` error
>   comes after the guards (r67-7).
> - **AC13-8:** a subset `Select` keeps D5/I3 (r67-2).
> - **§5.2:** the D10 advice keeps each earlier key's direction (r67-6).
> - **§8:** PostgreSQL NULL placement; WHERE with a captured null.
> - §4.2/§4.4 rows; §9.24.

> **Revision 21 (Task 7, and the Task 4 hostile review, 2026-10-01) — what changed:**
> - **AC13-2 amended (T7-1, AC-GAP):** `Last*` reads one row. `Last_ReadsOneRow_EmitsRowLimit` was added (§9.22).
> - **Mutation evidence corrected (T7-2):** one baseline-aware runner. The Task 4–7 batches were re-run: all
>   kills hold (§6).
> - **AC13-5:** the `Skip(0)` and second-ordering examples were added (T4R-3).
> - **§5.2 messages:** the D10 text and the I1 advice changed; `Cast`/`OfType` after an unsupported `Select` are
>   left to the parse loop (T4R-1/2/6).
> - **§8:** the interface-constrained generic helper (T4R-1).

> **Revision 20 (Task 1 fix-verification FV2, 2026-10-01) — what changed:** FV2 found 1 minor issue and 2 nits
> (§9.19).
> - `All` and `Any` keep both their true and false oracle rows.
> - The FV-5 premise is corrected.
> - The red `Allowed` rows are mapped to Tasks 5/7/8.
> - The §6 counts are updated.

> **Revision 19 (Task 1 fix-verification FV1, 2026-10-01) — what changed:** FV1 found 2 minor issues and 3 nits
> (§9.18).
> - §5.2's `OfType` message spec now covers reference scalars and non-identity `OfType`.
> - §4.4 gets a "`Last*` drops the ORDER BY" row; the Log row names the new paths.
> - §6 counts: one `Last` oracle row per provider turns red, as intended.

> **Revision 18 (Task 1 test review, 2026-10-01) — what changed:** two non-author reviewers found 18 test
> gaps. Disposition is in §9.17.
> - **AC13-2 amended (P1-3):** inverted own terms stay table-qualified on join entities.
> - **§4.2:** new tests:
>   - `OfType_Identity_OverReferenceScalar_Rejected`;
>   - `Last_AfterComputedOrderBy_InvertsComputedTerm`;
>   - two precedence tests;
>   - the harness positive control;
>   - the direct computed-fragment test;
>   - message asserts on every rejection row.
> - **§4.4:** the "allow any `Cast`" row is corrected, and nine mutation rows are added.
> - **§4.1:** the root `Single` exemption is stated.
> - **§4.2 provider-scope note:** now names the SQLite Skip-only rows.

> **Revision 17 (Task 1 red run, 2026-10-01) — what changed:** records the red run, with no design change.
> - §1.5 gets the t1 evidence rows. "Still unexecuted" shrinks to the 3.10 behaviors and the non-gating
>   `Average` comparison.
> - §4.1:
>   - names the AC12 own column (`Id`) and why;
>   - adds the `requireSuccess` rule for "same as concrete" rows;
>   - adds the last-`ORDER BY` rule.
> - §4.2 records SQLite's provider-scope exceptions to "green on 3.9.0".
> - §6 gets the Task 1 status. The duplicated Task 0 sentence is fixed.

> **Revision 16 (Task 0 fix-verification r15, 2026-09-30) — what changed:** r15 found 1 minor issue and 2
> nits at `ef994af`. All three are in sentences rev 15 added. Disposition is in §9.15. Each fix **removes**
> text rather than rewording it:
> - **R15-1:** the AC13-10 sentence "Other terminals after `Take` get D8's message first" is deleted;
>   precedence is AC13-8's job. The scalar `Take(0)` sentence now says "parameterless".
> - **R15-2:** §8's spelling sentence is replaced by a pointer to r11-C2.
> - **R15-3:** r11-C2's SQL Server `id` claim is scoped to a single-provider process.

> **Revision 15 (Task 0 fix-verification r14, 2026-09-30) — what changed:** r14 found 1 minor issue and 4
> nits at `0ce6cf1`, plus 1 optional draft edit. It said "Drafts: OK to post; ACs safe to post: yes; Task 1
> blocked: no". Disposition is in §9.14.
> - **R14-1:** the shared column-cache entry is now stated once, as a code read covering all four
>   providers (r11-C2: Core's static `_columnNames`, with the four aggregate-path lines). §8 and "still
>   unexecuted" point to it instead of restating it. On PostgreSQL and MySQL, the red run covers only the
>   cache order the suite produces; that limit is recorded.
> - **Nits:**
>   - §5.2.1 cites r13-L1;
>   - "only the interface `Id` rows" now says "the interface shapes of r12-L3 and r13-L1";
>   - AC13-10's scalar `Take(0)` sentence names `First*`/`Single*`;
>   - a new `Take0_ImplicitObject_Terminal_NoQuery` covers the implicit-conversion spelling.
> - **Draft:** the AC13-6 `OfType` message parenthetical is dropped, since §3 doesn't promise it.

> **Revision 14 (Task 0 fix-verification r13, 2026-09-30) — what changed:** r13 found 1 minor issue in the
> plan, 1 must-fix in the public AC13-4 draft, and 3 nits, at `d2d3386`. It confirmed R12-1/2/3 resolved
> and that the collapse orphans no test, mutation or disposition. It said "ACs safe to post: yes; Task 1
> blocked: no". Disposition is in §9.13.
> - **R13-1:** "still unexecuted" lists SQLite again; only its interface `Id` rows are executed.
> - **R13-2 (draft):** the AC13-4 draft now mirrors the collapsed plan text.
> - **R13-3:** `Take0_CastObject_Terminal_NoQuery` covers all four terminals.
> - **R13-4:** a new r13-L1 row records that SQLite's interface `Id` shapes equal the concrete query. Other
>   changes: AC13-4 cites §1.4; the shared-cache spelling is noted in r11-C2 and §8; §8's "loud" is
>   labelled SQL Server; the §1.5 heading is updated.

> **Revision 13 (Task 0 fix-verification r12, 2026-09-30) — what changed:** r12 found 3 minor issues and 2
> nits at `b846f9a`, all text or test-spec. It said "Drafts: OK to post; ACs safe to post: yes; Task 1
> blocked: no". Disposition is in §9.12.
> - **Redesign, not another rewording.** The interface-source paragraph produced a fix-introduced finding in
>   r10, r11 and r12. Each time, a more detailed description of pre-existing, out-of-scope behavior outran
>   its probes. §1.4, AC13-4 and §5.2.1 now say only what the tests rely on: "unchanged from 3.9.0; only
>   `Id` is pinned, and it equals the concrete query". The per-member, per-provider detail lives in §8
>   alone, each claim labelled with the provider it was executed on.
> - **R12-1:** the claim "a string selector aggregate is unsupported on any source" is removed; it's false
>   on SQLite (r12-L3). String `Min`/`Max` is D9's result-type issue (§8, §10).
> - **R12-2:** r11-C2 no longer says "`Id` → `id` on all four". FunkyORM emits `id` or `Id`, depending on
>   path and cache order (r12-L3). Binding on PostgreSQL and MySQL is left to Task 1's red run.
> - **R12-3:** AC13-10's `Take(0)` outcomes are pinned. A new `ScalarProjection_Take0_Terminal_ThrowsScalarGuard_NoQuery`
>   runs over all four terminals (renamed from `…_First_…`), and `Take0_SubsetProjection_Terminal_NoQuery`
>   covers the subset projection. The mutation row covers a `*OrDefault` short-circuit before the guard.
> - **Nits:** Task 1's executed-vs-code-read sentence; the §9.11 totals.

> **Revision 12 (Task 0 fix-verification r11, 2026-09-30) — what changed:** r11 found 2 minor issues and 3
> nits at `019e9b4`, all text. It said "ACs safe to post: no (AC13-4's interface sentence); Task 1 blocked:
> no". A separate fidelity check of the public AC drafts found one more plan defect (AC13-10). Disposition is
> in §9.11.
> - **R11-1:** the interface wording (§1.4, AC13-4, §5.2.1) no longer claims `Gender` works in selector
>   aggregates. A string selector is unsupported on any source (new probe r11-A1). It restores the
>   settable-`Select` case and names the fallback column-name mechanism (new code read r11-C2).
> - **R11-2:** "still unexecuted" no longer calls the PostgreSQL/MySQL inserts code-read only. MySQL's
>   existing coverage already uses `AUTO_INCREMENT`; only PostgreSQL's `IDENTITY` spelling and SQL Server
>   remain. SQLite `Query` is now executed (r11-L2).
> - **R11-3:** §1.5's rule admits labelled code reads, for claims about FunkyORM's own output only.
> - **R11-4:** §8 and "still unexecuted" describe the per-provider fallback accurately.
> - **R11-5:** AC13-6 cites r9-I2. The Task 0 checklist's duplicate line is removed.
> - **AC13-10:** after `Take(0)`, `*OrDefault` returns `null` only on the entity path. Over a scalar
>   projection, the scalar guard throws, as §5.2.6 and `ScalarProjection_Take0_First_ThrowsScalarGuard_NoQuery`
>   already specify.

> **Revision 11 (Task 0 fix-verification r10, 2026-09-30) — what changed:** r10 found 1 minor issue
> (R10-1) plus 6 nits at `830b707`, all text. It said "ACs safe to post: yes; Task 1 blocked: no".
> Disposition is in §9.10.
> - **R10-1:** the r9 premises that rev 10 cited as bare "r9" are now recorded in a §1.5 r9/r10 table
>   (`r9-I1`, `r9-I2`, `r9-L1`, `r9-C1`, `r10-S1`, `r10-L1`). The SQL Server `single_probe` identity
>   insert and SQLite `Query` are added to "still unexecuted".
> - **Nits:** the interface wording now separates "lowercased column name" from the get-only `Select`
>   rejection (N1); the matrix rationale (N2); the evidence ranges (N3); the stale legend (N4); the
>   "therefore" (N5); and the AC13-6 example now uses `IHasPersonId` (N6).

> **Revision 10 (Task 0 fix-verification r9, 2026-09-30) — what changed:** r9 found 4 minor issues at
> `8d5ac7a`, all text or test-spec. Disposition is in §9.9.
> - **F1:** the interface wording is now "unchanged", not "correct". A member resolves to its lowercased
>   name: `Id`/`Gender` work; others fail loudly (invalid column, or the `Select`-shape exception for
>   get-only members). Fixed in three places.
> - **F2:** `single_probe` uses an identity key per provider, because INSERT omits `int` keys. SQLite needs
>   `INTEGER PRIMARY KEY` exactly.
> - **F3:** the `Cast` rows are spelled per provider (`IHasPersonId`, `PersonBase` on MySQL), and
>   `IHasPersonId` is trimmed to `Id`.
> - **F4:** r8 probe IDs renamed (`r8-PG1`/`r8-MY1`/`r8-MY3`/`r8-SL1`), and AC citations prefixed `r7`.

> **Revision 9 (Task 0 fix-verification r8, 2026-09-30) — what changed:**
> The r8 reviewer found 9 issues at `3091544`: 0 blocker, 1 major, 8 minor. All are text or test-spec fixes;
> no redesign, no owner decision. Disposition is in §9.8.
> - **#1 (major):** the "interface source works" claim held only for `Id`; other interface members hit a
>   pre-existing "invalid column" bug (J21, S8/S9). Narrowed, and the bug is recorded in §8.
> - **#3:** `Single*(pred)` over a converted source silently returns an unrelated row in 3.9.0 rather than
>   throwing. Reworded. These were the two AC13-4 sentences to fix before the ACs are posted.
> - **#2:** "left alone" rows compare with the concrete query, not with in-memory LINQ, because `Average`
>   differs.
> - **#4:** I1 rows for `Last*`/`FirstOrDefault`/`SingleOrDefault(pred)`; predicate operators for the
>   composition row.
> - **#5:** an `IHasPersonId` interface, and MySQL `PersonBase`.
> - **#7:** the provider-neutral "no ORDER BY" assert is the SQLite killer.
> - **#8:** non-generic `Execute` unwraps `TargetInvocationException`.
> - **#9:** ‡ excludes subset-`Select` `Distinct` → `Last`.
> - **#6, superseded by the author:** the reviewer proposed recreating the local `dbo.[User]`. But
>   `DocumentationGapTests` **drops and recreates `[User]` with an `Id` column on every setup**, so `User`
>   can't be the no-`id` entity on SQL Server at all. Rev 8's "verified" claim had only read the scripts. The
>   AC13-1 no-`id` test now owns its own `single_probe` table (§4.1), with no schema-script changes.

> **Revision 8 (Task 0 fix-verification r7, 2026-09-30) — what changed:**
> The r7 reviewer found 6 issues at `765792a`: 0 blocker, 1 major, 5 minor. Blame: AC-GAP 1, TEST-GAP 3,
> HOUSE-RULE 1, PLAN-GAP 1. Disposition is in §9.7. **No silent wrong result** got past I1–I3.
> - **Major (#1):** rev 7's "every lambda over a converted source fails in 3.9.0" was false. My I1 probes
>   only covered `object`. Over a **base-class/interface** source, `OrderBy*`, selector aggregates and scalar
>   `Select` are correct today (r7 J1–J20, executed). **Owner decision: reject only where 3.9.0 fails.**
>   I1 now covers only the predicate operators that hard-cast to `Func<T,…>` (`Where`; `First*`/`Single*`/
>   `Last*`/`Any`/`All`/`Count`/`LongCount` with a predicate); everything else is left alone.
> - **#5 (house rule):** the non-generic `IQueryProvider.Execute` returns the whole list for a `First`
>   (K1). It now dispatches by expression shape (I2), as new **AC13-15**.
> - **#2:** I1 defers to the parse loop after any non-subset `Select`. The scalar/subset predicates are
>   identical to the parse loop's.
> - **#3:** ‡ defined per operator.
> - **#4:** the composition-message assertion strengthened.
> - **#6:** type-filter wording.
> - **Task 1 simplification (author, verified):** all four schemas already have a `User` table with no `id`
>   column, so the no-`id` entity needs **no schema changes**.

> **Revision 7 (Task 0 fix-verification r6, 2026-09-30) — what changed:**
> The r6 reviewer found 8 issues at `44e78b4`: 0 blocker, 1 major, 7 minor. Blame: AC-GAP 1, TEST-GAP 7.
> Disposition is in §9.6.
> - **The major (F1) was the 4th consecutive round of fix-introduced findings on the covariance rule.** A
>   converted scalar source followed by an allowed `Skip`/`Take`/`Distinct` and then a terminal slipped past
>   the rev 6 "effective element type" check, and 3.9.0 returns the whole list there (executed, Q1a–e).
> - **Redesign (not a patch), per the house rule.** The per-node covariance type-flow and cell table are
>   **removed**. Three local invariants (§5.2.1) replace them, each premise executed:
>   - **I1:** every lambda must bind to the entity type. Every violating shape fails in 3.9.0 (I1a–h).
>   - **I2:** collection vs terminal is decided by expression shape. It closes the scalar "whole list" class
>     on every path and fixes P14/F3.
>   - **I3:** `Cast`/`OfType` are judged against a row type `R` that only a scalar `Select` changes.
>
>   Nothing tracks a conversion through the chain any more, so there's nothing left to lose.
> - **Also:**
>   - entity covariant `Skip`/`Take`/`Distinct` are allowed, as they're correct in 3.9.0 (F2);
>   - `TResult`-free empty-`Take` (F3);
>   - base-class and interface `Cast` rows, judged against `R` (F4);
>   - scalar `Cast` after paging (F5);
>   - cross-pass precedence stated and pinned (F6);
>   - `ThenBy_OnRoot` uses `ThenByDescending` (F7);
>   - stale text and bookkeeping (F8).
> - `OfType` stays identity-only (the owner's amendment covered `Cast` only).

> **Revision 6 (Task 0 fix-verification r5, 2026-09-30) — what changed:**
> The r5 reviewer found 8 issues at `dfeae2e`: 0 blocker, 2 major, 6 minor. All 8 were introduced by rev 5.
> Blame: AC-GAP 1, TEST-GAP 7. Disposition is in §9.5. Both majors rested on **System.Linq premises nobody had
> executed**:
> - **`Queryable.Cast` always creates a node**, even `Cast<T>()`. My §1.4 bullet confused it with
>   `Enumerable.Cast`.
> - **`ThenBy*` after any non-ordering call throws `ArgumentException` inside System.Linq** before the
>   provider sees it. That's r3 N2's premise, never run.
>
> **Process fix: every shape premise now cites an executed probe (new §1.5)**, run on the real providers on
> 2026-09-30. Changes:
> - **D10 clause 2 deleted (R5-2).** A separated `ThenBy` is unreachable, so its message, five rows and two
>   mutation rows are gone. D10 is just "second `OrderBy*` rejected". The sort-helper crash this exposed is
>   pre-existing and filed as [#16](https://github.com/FuncularLabs/Funcular.FunkyOrm/issues/16) (owner:
>   follow-up, not 3.10.0).
> - **D5 amended by the owner (R5-1).** A **reference-conversion `Cast<TBase>()`** behaves like an implicit
>   conversion. It's transparent, and the operators after it go through the covariance table using the
>   *effective* element type.
>   - `q.Cast<object>().Count()`/`.First()` keep working; they're correct in 3.9.0 (P5).
>   - Enumerating it now works too, through a generalized `isCollection` in `Execute`. In 3.9.0 it throws
>     `InvalidCastException` (P14).
>   - `Select(p => p.FirstName).Cast<object>().First()` is still rejected; in 3.9.0 it returns the whole list
>     (P6).
> - **The scalar covariance row is corrected against new probes.** Covariant `Skip`/`Take`/`Distinct` and
>   enumeration over a scalar projection return correct results (P7b–P7e), so they stay allowed. Only
>   terminals and lambda-bearing operators are rejected.
> - **Covariance rows** are spelled both ways: the no-node conversion `IQueryable<object> o = …` and
>   `Cast<object>()`.
> - **Second-layer scalar guard (R5-3)** can't be reached through `Queryable`. It becomes a Core helper,
>   `ScalarProjectionGuard`, tested directly.
> - **Rule order is pinned at each node (R5-5):** allow-list → D8 → D10 → covariance/D5. The pre-pass runs in
>   two passes so the root element type is known.
> - **Messages (R5-6):** a scalar-source covariant variant; `Cast<…>` dropped from the advice; an "other
>   projected type" source defers to the existing `Select`-shape guard.
> - **Wording (R5-4, R5-7):** AC13-5 restated (the rules reject some shapes that 3.9.0 happened to answer
>   correctly; the Changelog lists them), and AC12-3 exceptions widened.
> - **Formatting (R5-8):** §4.2 table repaired.
> - Signing deferred by the owner (§8).
> - The spine root must be the queryable's own root (`((IQueryable)c.Value).Expression == c`).

> **Revision 5 (Task 0 fix-verification r4, 2026-09-30) — what changed:**
> The r4 reviewer found 7 issues at `7e97e93`: 0 blocker, 1 major, 6 minor. 5 were introduced by rev 4. Blame:
> AC-GAP 2, TEST-GAP 3, PLAN-GAP 2. Disposition is in §9.4.
> - **Covariance rule redesigned (F1, major).** This was the second fix-introduced finding in a row on this
>   rule. Rev 4's exemption let `Select(p => p.FirstName).Cast<object>().First()` through, and it would return
>   the entire `List<string>` as the "first" element. Following the house rule, the rule is now an explicit
>   **cell table** (§5.2.1), and every cell has a test row. The exemption applies **only when the source
>   element type is still the entity type**.
>
>   A second layer is added too: `ExecuteScalarProjection` rejects any **terminal** call, not only a terminal
>   whose result type fails to match, so `TResult = object` can't slip past it.
> - **D8 refined (F6a).** `Skip(n).Select(…).Take(k)` returns correct rows in 3.9.0, because projection
>   commutes with `Take`. So `Take` may follow `Skip` when only `Select`/identity `OfType` sit between them.
>   D8's promise to reject nothing that works today holds again.
> - **Messages (F2).** A separated `ThenBy` gets its own D10 message ("move ThenBy before {Op}"). A covariant
>   mismatch gets its own message ("apply {Op} before converting the element type"). D8 is evaluated before
>   D10 at the same node (F3), and the rows pin both.
> - **Tests.**
>   - SQL-shape asserts normalize whitespace (F4). PostgreSQL and SQLite put `LIMIT`/`OFFSET` on separate
>     lines.
>   - AC12-1/AC12-5 state that ordering and paging come *before* the projection. The "no predicate or ordering
>     after a subset `Select`" rule now binds all shapes, not just oracle rows (F5).
>   - AC12-3 is narrowed to the ORDER BY translation (F6b).
> - **Bookkeeping (F7).** A stale test name in §4.3, the §5.2.4 `Id DESC` wording, the §9.3 arithmetic, and
>   task mapping for the covariant rows.

> **Revision 4 (Task 0 fix-verification r3, 2026-09-30) — what changed:**
> The r3 reviewer found 10 issues at `e48f2b1`: 0 blocker, 1 major, 9 minor. Blame: AC-GAP 3, TEST-GAP 4,
> PLAN-GAP 3. Disposition is in §9.3.
> - **D10 extended (N2, major).** A `ThenBy*` separated from an earlier ordering by a non-ordering call is
>   rejected too. Every FunkyORM queryable is an `IOrderedQueryable`, so
>   `((IOrderedQueryable<T>)q.OrderBy(a).Where(w)).ThenBy(b)` compiles, and it silently drops `a` today. Same
>   class, same rule.
> - **Covariance (N1).** The `TSource` check no longer rejects parameterless terminals that work today, such as
>   `IQueryable<object> q = db.Query<T>(); q.Count()`. It still rejects lambda-bearing and sequence operators.
> - **Test-framework legs (N3).** The policy literal-set test has an **xUnit twin** in the net9 project (which is
>   xUnit). The net48 leg is named: `Funcular.Data.Orm.SqlServer.Tests.NetFramework`.
> - **Test fixes (N4–N6).** `Skip`-only `Single` asserts its SQL shape (`LIMIT 2 OFFSET n` / `FETCH NEXT 2`).
>   The empty-`Take` short-circuit runs after the scalar guard. The D10 rows use a subset `Select`. Every
>   rejection row asserts its specific message.
> - **Oracle rules (N7, N8).** Oracle predicates reference only non-null members, and never follow a subset
>   `Select`. The two pre-existing divergences (SQL NULL semantics; a `Where` after a subset `Select` reading
>   base columns) are recorded in §8 as follow-ups.
> - **§10 (N9 + owner answers).** The `time`-range wording is corrected. The Guid opt-in is a provider-wide
>   option, with PostgreSQL allowed by default once verified. `OrderBy` on Guid isn't guarded. String
>   `Min`/`Max` use database collation.
> - **Bookkeeping (N10).** §9.2 said 16 of 21 were fix-introduced; it's 19. Also: `OfType` after paging aligned
>   with D5, the `Id DESC` qualification wording, the §8 `Single` wording, and the §1.4 premise.
> - `QueryOperatorPolicy.IsAllowed` is **public**, so the test seam doesn't depend on `InternalsVisibleTo`.
>   That dependency would break if the assemblies are strong-named later.

> **Revision 3 (Task 0 fix-verification r2, 2026-09-30) — what changed:**
> The r2 reviewer found 21 issues at `e19a7ae`: 0 blocker, 6 major, 15 minor. 19 were introduced by
> Revision 2. Blame: AC-GAP 5, TEST-GAP 7, HOUSE-RULE 1, PLAN-GAP 8. Following the house rule after repeated
> fix-introduced findings, the two unsound designs were **redesigned rather than patched**:
> - **D10 (second `OrderBy`) is now "reject"** (owner decision). This removes the stable-sort stitching, the
>   tie-seeded tests and the repeated-column problem.
> - **D9 (aggregates) moves to v3.10.1** (owner decision D12). The r2 findings about `Average` precision
>   (SQLite `ROUND`, MySQL scale 4), `ChangeType` failures, and missing test-schema columns become inputs to
>   §10.
>
> The remaining findings are folded in:
> - identity `OfType` over nullable elements is rejected, because LINQ drops nulls;
> - the spine walk is fully specified (element-type resolution, root terminal, `TSource` check);
> - SQLite `Skip` without `Take` gets its own fix (AC13-14);
> - negative `Skip`/`Take` handling;
> - the `Take(0)` short-circuit is pinned;
> - #12 tests assert SQL text and use projections that omit the key, so they can't pass by alias binding;
> - SQLite resets all per-parse state;
> - one literal pinned set across frameworks;
> - net48 compile constraints;
> - the dead scalar-aggregate guard and its stale comment are removed;
> - ternary null checks in either operand order;
> - duplicate `ThenBy` keys are deduplicated.
>
> The disposition is in §9.2. **3.10.0 effort: ~6 working days.**
>
> **Earlier revisions:**
> - **Revision 2** — Task 0 test-plan review r1: 25 findings, disposition in §9.1.
> - **Revision 1** — cross-provider premise verification.

---

## 1. Verified premises

Before planning, throwaway probes ran each shape against live databases on all four providers, then were
deleted. Their cases become the red tests in §4.

### 1.1 #13 is broader and more severe than filed (SQL Server shown; §1.3 for the others)

| Query (SQL Server, 3.9.0; 8,369 `person` rows, ids 1…8673) | Correct result | FunkyORM 3.9.0 returns | SQL actually sent |
|---|---|---|---|
| `Single(p => p.Id == 8673)` | row 8673 | **row 1** | `SELECT … FROM person` (predicate dropped) |
| `SingleOrDefault(p => p.Id == -1)` | `null` | **row 1** | predicate dropped |
| `Single()` over 8,369 rows | throws "more than one element" | **row 1** | no cardinality check |
| `Where(p => p.Id == -1).Single()` | throws "no elements" | **`null`** | no cardinality check |
| `Last()` / `OrderBy(Id).Last()` | row 8673 | **row 1** | `ORDER BY id ASC`, first row read |
| `OrderBy(Id).Reverse().ToList()` | descending | ascending | `Reverse` dropped |
| `TakeWhile(p => p.Id < 0).ToList()` | 0 rows | **8,369 rows** | dropped |
| `SkipWhile(p => p.Id > 0).ToList()` | 0 rows | **8,369 rows** | dropped |
| `OrderBy(Id).TakeLast(1).ToList()` | 1 row | **8,369 rows** | dropped |
| `OrderBy(Id).ElementAt(1)` | 2nd row | **row 1** | dropped |
| `Where(Id==-1).DefaultIfEmpty().ToList()` | 1 element (`null`) | 0 elements | dropped |
| `q1.Concat(q2).ToList()` | 2 rows | 1 row | second query dropped |
| `LongCount()` | `8369L` | `InvalidCastException` | full `SELECT *` |
| *(sanity)* `First()`, `FirstOrDefault(pred)`, `Min(sel)`, `AsQueryable()` | — | correct | — |

**Root cause:**
- `ParseExpression` translates a fixed set of operator **names** and silently skips every other call in the
  chain.
- `ExecuteQuery` then reads the first row for any non-collection `TResult`.
- `Single*` is never matched.
- The `First/Last` predicate branch requires `Arguments.Count == 2`, so parameterless `Last()` falls through
  to "read first row".

**Severity: critical (silent wrong row).** `Query<User>().SingleOrDefault(u => u.Email == email)` returns
an arbitrary *different* user. In a consuming app that is an authorization flaw.

**Affected versions:** every NuGet release from `0.1.1-alpha.2` (the first published version) through
`3.9.0`. `Single*` has never been matched in any version of the LINQ provider. An earlier `IQueryProvider`
at `1a96b3d` (2025-03-30) predates every published package.

**Also confirmed from source by the Task 0 reviews:**
- Aggregates ignore paging (`BuildAggregateClause` never reads `Skip`/`Take`).
- `Where` and predicate terminals after `Skip`/`Take` are ANDed into `WHERE`, so they run before paging.
- Repeated `Skip`/`Take` overwrite each other.
- `OrderBy(a).OrderBy(b)` emits `ORDER BY a, b`.
- SQLite `_lastSelectProjection`/`_lastSelectParameters` are never reset.
- An ORDER BY ternary `x.M == null` emits `col = NULL`.
- SQLite emits `OFFSET` without `LIMIT` for `Skip`-only queries, which SQLite's grammar rejects
  (*Task 1 red test*).
- Aggregate result conversion is wrong for several overloads; that's 3.10.1 (§10).

### 1.2 #12 is narrower than filed on SQL Server, PostgreSQL and MySQL (broader on SQLite — see §1.3)

| Query on `PersonDetailEntity` (joins `organization`, `address`, `country` — all have `id`) | 3.9.0 result |
|---|---|
| `OrderBy(p => p.Id).Take(3).ToList()` (full entity) | ✅ works — `ORDER BY id` binds to the single `id` column in the SELECT list |
| `OrderBy(p => p.Id).Take(3).Select(p => p.MiddleInitial)` (scalar projection) | ❌ `Ambiguous column name 'id'` |
| `OrderBy(p => p.Id).Take(3).Select(p => new PersonDetailEntity { FirstName = p.FirstName })` | ❌ `Ambiguous column name 'id'` |
| `OrderBy(p => p.EmployerHeadquartersCountryName).ThenBy(p => p.Id)` + subset projection | ❌ `Ambiguous column name 'id'` (`ORDER BY [country_0].name ASC, id ASC`) |
| `OrderBy(p => p.FirstName)` + `Select(p => p.Id)` | ✅ works — only because no joined table has `first_name` |
| `Where(p => p.Id > 0)` + default paging | ✅ WHERE emits `person.id`; default order is `person.id` (3.9 round-1 fix) |

**Real trigger:** an own-column `OrderBy`/`ThenBy` combined with a **narrow projection that doesn't include
the key** on a remote-join entity, when any joined table has a column of the same name. When the key *is*
projected, the unqualified name binds to the select alias and works by accident.

**Consumer exposure:** Sentinel.MVP `main` (pinned to 3.9.0-beta1) uses this exact shape in `CallListOrder.Apply`.
- It works only because `CallId` maps to `call_id` and no joined table has that name.
- No Sentinel code uses a #13 operator on a FunkyORM query or composes an operator after `Skip`/`Take`.
- Its `OrderBy` chains contain one `OrderBy` each, so D10 doesn't affect it.

### 1.3 Cross-provider verification (Task 0, 2026-09-30)

Same shapes on PostgreSQL (local PG 18), MySQL and SQLite. Each probe seeded three marker-tagged rows
(target = the third) and was deleted afterwards. The join entity is `PersonDetailEntity` (PG, SQLite) or
`PersonWithEmployer` (MySQL); every joined table has an `id` column.

| Shape | SQL Server | PostgreSQL | MySQL | SQLite |
|---|---|---|---|---|
| `Single(pred → 3rd row)` | row 1 of table | row 1 of table | an unrelated row (22) | row 1 |
| `SingleOrDefault(no match)` | a row | a row | a row | a row |
| `Where(3 rows).Single()` | a row, no throw | a row | a row | a row |
| `Last()` / `OrderBy(Id).Last()` / `OrderBy(Id).LastOrDefault(pred)` | first row | first row | first row | first row |
| `OrderBy(Id).Reverse()` | ignored | ignored | ignored | ignored |
| `LongCount()` | `InvalidCastException` | `InvalidCastException` | `InvalidCastException` | `InvalidCastException` |
| #12 full entity `OrderBy(Id)` | ✅ | ✅ | ✅ | ❌ `ambiguous column name: id` |
| #12 subset / scalar projection + `OrderBy(Id)` | ❌ | ❌ `42702` | ❌ | ❌ |
| #12 remote `OrderBy` + `ThenBy(Id)` + subset | ❌ | ❌ | ❌ | ❌ |
| Default paging (`Take`) on join entity | ✅ `person.id` | ✅ `person.id` | ✅ `person.id` | ❌ `ambiguous column name: rowid` |

**Consequence for SQLite:** on 3.9.0, any `Skip`/`Take` on an entity with remote attributes fails, and
`Skip` without `Take` fails on any entity.

### 1.4 Code facts the design relies on (verified by author and reviewers)

- **Order-by visitors.**
  - Each provider has its own. All four resolve own columns only through a private `ResolveOrderColumn` →
    bare `GetColumnName(property)`; every ORDER BY path goes through it.
  - Each term is already stored as `(ColumnName, IsDescending)`, so inverting terms needs no text parsing.
  - The visitor recurses into a prior ordering call, including a prior `OrderBy`, and stops at
    non-ordering calls (OrderByClauseVisitor.cs:117-134).
- **WHERE and SELECT already qualify.** All four WHERE visitors qualify own columns as `{_tableName}.{column}`;
  `SelectClauseVisitor` emits `person.id AS Id`.
- **Two visitor call sites per provider.** The `OrderBy/ThenBy` branch passes the remote map; the
  `Last/LastOrDefault(pred)` synthesized order passes none. `GenerateOrderByClause` (three providers) is dead
  code.
- **"Has joins" test.** `ResolvedRemoteJoinInfo.IndividualJoinClauses` has the same name in all four and is
  non-empty exactly when joins are emitted.
- **Eager schema discovery.** `Query<T>()` discovers schema eagerly with raw commands that **don't log**. So
  "no SQL executed" is observable only as "no *query or aggregate* command logged after the `IQueryable` is
  obtained".
- **The query root** is `Expression.Constant(this)`, typed as the provider's queryable class, e.g.
  `SqlQueryable<T>` (SqlQueryable.cs:17). After `OrderBy` the node type is `IOrderedQueryable<T>`. Element
  types must therefore be resolved through the `IEnumerable<>` interface.
- **`Skip`/`Take` arguments** are always a `ConstantExpression`, so their values are known during parsing.
- **Default paging order.** SQLite's is a bare `ORDER BY rowid`; the other three qualify `id` with a
  `" JOIN "` text check. It hard-codes the column name `id` (pre-existing, §8).
- **SQLite paging SQL** emits `LIMIT n` and `OFFSET m` separately (SqliteLinqQueryProvider.cs:592-595), so a
  `Skip`-only query emits `OFFSET` with no `LIMIT`.
- **Aggregates (unchanged in 3.10.0 except `LongCount`).**
  - `BuildAggregateClause` ignores `Skip`/`Take`.
  - SQL Server, PostgreSQL and MySQL convert by a selector-type switch; SQLite switches on `TResult` with a
    `ChangeType` fallback (:623-634).
  - `Average` returns `Convert.ToDouble` everywhere.
  - SQLite wraps `AVG` in `ROUND(…, 10)` (:461-462).
- **SQLite per-parse state.** `_lastOrderByClause` (overwritten after each loop), plus `_lastSelectProjection`
  and `_lastSelectParameters` (set only in the Select branch, never reset). The early return for an
  operator-free expression (:119) happens before any assignment.
- **Scalar-projection guards.** The guard for a parameterless numeric aggregate after a scalar projection
  (SqlLinqQueryProvider.cs:339-346; the same in the siblings) becomes dead once the pre-pass rejects
  non-generic `Sum()`/`Average()` and parameterless generic `Min<T>()`/`Max<T>()` (the "with selector" rule).
  The comment at :128-129 then becomes false.
- **Every FunkyORM queryable implements `IOrderedQueryable<T>`** (SqlQueryable.cs:9 and the siblings). But
  `Queryable.ThenBy` checks the source **expression's** type: after any non-ordering call it is
  `IQueryable<T>`, so System.Linq throws `ArgumentException` before the provider sees it (§1.5 P3/P3b). Only
  the root (typed as the concrete queryable) and ordering calls can precede `ThenBy*` (P4). One consequence
  is pre-existing and out of scope: `is IOrderedQueryable` sort helpers crash on composed queries (P4b; issue
  #16).
- **Covariance.** `IQueryable<object> q = db.Query<T>()` creates **no node** (P2b), so `q.Count()` is
  `Count<object>(root)`.
  - Over an **entity** source, in 3.9.0:
    - `Count`/`Any`/`First*` are correct (P5, P5b, P13, P13b, r6 Q9a/b).
    - `LongCount`/`Single*`/`Last*` are broken for every source (§1.1).
    - Lambda-free sequence operators followed by a terminal are correct (r6 Q3a–e).
    - **Predicate** lambdas fail:
      - `Where`, `First*`/`Last*`/`Any`/`All`/`Count` with a predicate throw `InvalidCastException`
        (§1.5 I1a/b/d/e/f/g, J5/J6/J14/J15, S4–S7b);
      - `Single*(pred)` **silently returns an unrelated row**, because the predicate is dropped (S1–S3, the
        §1.1 class).
    - **Non-predicate** lambdas (`OrderBy*`, selector aggregates, scalar `Select`):
      - over a **base-class** source, they return the same results as the concrete query (r7 J1–J4, J7, J9,
        J10, J12, J13, J16–J18);
      - over an **interface** source, they behave as in 3.9.0, which only partly works: a member doesn't
        resolve to the entity's mapped column (pre-existing; details in §8). The plan relies on one shape
        only: `OrderByDescending(x => x.Id).First()` and `Max(x => x.Id)` equal the concrete query (SQL
        Server: S9c, r9-I2, r10-S1; SQLite: r13-L1). 3.10.0 doesn't change any of this;
      - over `object`, an `OrderBy` on a cast member works (J8), and an unsupported body fails cleanly (I1c).
  - Over a **scalar** source, a row-returning terminal returns the whole projected list. That happens directly
    (P6, P7) and after covariant `Skip`/`Take`/`Distinct` (r6 Q1a–e): a silent wrong result.
- **`Queryable.Cast<TResult>` always creates a node**, even `Cast<T>()` on an `IQueryable<T>` (P1, P2). The
  short-circuit belongs to `Enumerable.Cast`, not `Queryable.Cast`. The 3.9.0 parse loop has no `Cast`
  branch, so the node is ignored: `q.Cast<object>().Count()` is correct (P5), and
  `Select(scalar).Cast<object>().First()` is the scalar bug (P6).
- **`OfType`** always creates a node and, like `Enumerable.OfType`, **drops nulls**.
- **Core `InternalsVisibleTo`** already covers `SqlServer.Tests`, `.DotNet9` and `.NetFramework`.
- **The net48 test project** is an old-style csproj compiled as C# 7.3. net48's `Queryable` lacks
  `Chunk`/`DistinctBy`/`MinBy`/`Order`/`Take(Range)`/the default-value overloads.

### 1.5 Executed premise probes (2026-09-30)

**Rule (rev 6):** every premise about how a shape behaves (System.Linq or a database) cites a probe that was
actually run. Reasoning alone doesn't count. Two plan rounds were spent on premises nobody had executed.
*(Rev 12)* A row marked "code read", with file:line, may support a claim about what FunkyORM's own code
emits (e.g. an INSERT column list). It never supports a claim about how System.Linq or a database behaves.

Throwaway probe tests ran on the real providers (SQL Server `FUNKY_CONNECTION`; local PostgreSQL 18; MySQL;
SQLite temp DB) and were deleted afterwards. The r5 reviewer separately confirmed P1–P4 on net48, net8 and
net9 with a mimic provider. Results are for 3.9.0.

| # | Shape | Result |
|---|---|---|
| P1 | `q.Cast<T>()` (identity) | **New node** (`…SqlQueryable.Cast()`); not the same instance |
| P2 | `q.Cast<object>()` | New node |
| P2b | `IQueryable<object> o = q` | **No node** (same instance) |
| P3 | `((IOrderedQueryable<T>)q.Where(w)).ThenBy(k)` | `ArgumentException` from System.Linq (`IQueryable` where `IOrderedQueryable` expected) |
| P3b | `((IOrderedQueryable<T>)q.Skip(1)).ThenBy(k)` | `ArgumentException` (same) |
| P4 | `((IOrderedQueryable<T>)root).ThenBy(k)` | Works: `ORDER BY id ASC` |
| P4b | helper `w is IOrderedQueryable<T> o ? o.ThenBy(k) : w.OrderBy(k)` over `q.Where(…)` | `ArgumentException` (pre-existing; #16) |
| P5 | `q.Cast<object>().Count()` | Correct (`SELECT COUNT(*)`) |
| P5b | `q.OrderBy(Id).Cast<object>().First()` | Correct (row Id=1) |
| P6 | `q.Select(p => p.FirstName).Cast<object>().First()` | **Wrong:** returns the whole `List<string>` (8,546 items) |
| P7 | `IQueryable<object> s = q.Select(p => p.FirstName); s.First()` | **Wrong:** returns the whole list |
| P7b | same `s`; `s.Take(3).ToList()` | Correct (3 items) |
| P7c | `IQueryable<object> s = q.OrderBy(Id).Select(p => p.FirstName); s.Skip(2).Take(3).ToList()` | Correct (matches in-memory `Skip(2).Take(3)`) |
| P7d | `IQueryable<object> s = q.Select(p => p.FirstName); s.Distinct().ToList()` | Correct (1,503 = in-memory distinct count) |
| P7e | `q.Select(p => p.FirstName).Cast<object>().ToList()` | Correct (8,546 items) |
| P7f | `q.Select(p => p.FirstName).Cast<object>().Count()` | Clean `NotSupportedException` (scalar result-type guard) |
| P14 | `q.Where(Id <= 3).Cast<object>().ToList()` (entity, enumerated) | **`InvalidCastException`**: `Execute` treats `IEnumerable<object>` as a single row, because `isCollection` tests assignability to `IEnumerable<T>` (SqlLinqQueryProvider.cs:99 and the sibling equivalents at :58) |
| P14b | `IQueryable<object> c = q.Where(Id <= 3); c.ToList()` | Correct (no node, so it enumerates as `IEnumerable<T>`) |
| P14c | `q.Where(Id <= 3).Cast<PersonEntity>().ToList()` (identity) | Correct |
| P14d | `q.Where(Id <= 3).OrderBy(Id).Cast<object>().First()` | Correct |
| P8 | `OrderBy(Id).ThenBy(Id)` / `.ThenByDescending(Id)` | SQL Server: `SqlException` ("column specified more than once in the order by list"). PostgreSQL / MySQL / SQLite: execute. |
| P9 | join entity `OrderBy(Id).Take(2).Select(new T { Id, FirstName })` (key projected) | Works on all 4: bare `ORDER BY id` binds to the `Id` alias |
| P10 | `Skip(1).ToList()` / `OrderBy(Id).Skip(1).First()` (no `Take`) | SQLite: **syntax error near `OFFSET`**. PostgreSQL: `OFFSET 1`, OK. MySQL: `LIMIT 18446744073709551615 OFFSET 1`, OK. SQL Server: OK. |
| P11 | `Take(3).Distinct()` over the full (keyed) entity | Correct on all 4 (rows already distinct) |
| P13 | `IQueryable<object> c = q; c.Count()` | Correct |
| P13b | `IQueryable<object> c = q.OrderBy(Id).Skip(1); c.First()` | Correct (row Id=2) |

**Author probes for I1 (rev 7, SQL Server)**: lambdas over an entity source converted to **`object`**
(`o` = `IQueryable<object>` of the entity). *Rev 8 correction:* these only covered `object` and the hard-cast
operators. The r7 J-probes below show that non-predicate lambdas over a **base-class or interface** source
work, so I1 was narrowed.

| # | Shape | 3.9.0 result |
|---|---|---|
| I1a | `o.Where(x => x != null).Take(2).ToList()` (no node) | `InvalidCastException` (`Func<Object,Boolean>` → `Func<PersonEntity,Boolean>`) |
| I1b | `q.Cast<object>().Where(x => x != null)…` | `InvalidCastException` |
| I1c | `o.OrderBy(x => x.GetHashCode()).First()` | `NotSupportedException` (OrderBy visitor: unsupported expression) |
| I1d / I1e / I1f | `o.Any(pred)` / `o.Count(pred)` / `o.First(pred)` | `InvalidCastException` (all three) |
| I1g | `IQueryable<object> s = q.Select(new T { FirstName }); s.Where(pred)` | `InvalidCastException` |
| I1h | `o.Select(x => x.ToString())` | `NotSupportedException` (`Select`-shape guard) |
| I1i | `PersonEntity` implements `INotifyPropertyChanged` | true (usable for interface-cast rows) |

**r6 reviewer probes (SQL Server, executed, cited by ID):**

| ID | Shape | 3.9.0 result |
|---|---|---|
| Q1a–Q1e | scalar `Select(FirstName)` converted (no node or `Cast<object>`), then `Take(3).First()`, `Distinct().FirstOrDefault()`, `Skip(1).Take(2).Single()`, `Distinct().Last()` | **Whole list returned** as the element (3 / 1,503 / 2 / 1,503 items) |
| Q2 | `TSource` of a `First<object>` after `Take<object>` equals its source's element type (`object`) | So a per-node `TSource` check can't see the conversion after one sequence operator (why rev 7 drops it) |
| Q3a–Q3e | entity converted, then `Take(5).First()`, `Skip(1).First()`, `Cast<object>().Distinct().First()` | Correct. Enumeration (Q3d) throws `InvalidCastException` (P14 class) |
| Q4a / Q4b | `Cast<object>().Cast<T>().ToList()` / `Cast<object>().OfType<T>().ToList()` | Correct |
| Q4c / Q4d | `Cast<BaseClass>().ToList()` / `Cast<INotifyPropertyChanged>().Count()` | `InvalidCastException` / correct (3) |
| Q5a–Q5c | `OfType` creates a node; 3.9.0 ignores it and keeps nulls | 8,546 rows vs 5,047 in-memory (null-dropping) |
| Q6 | root `((IQueryable)c.Value).Expression == c`; composed queryable and `List.AsQueryable()` fail it | as stated |
| Q7 | `OrderBy(Id).Skip(1).Take(3).Select(FirstName).Cast<object>().ToList()` | Correct |
| Q8 | `Select(FirstName).Cast<object>()` then `Skip`/`Take`/`Distinct`, enumerated | Correct |
| Q9a–Q9c | entity `Cast<object>().Any()`, no-node `FirstOrDefault()` / scalar `Cast<object>().Any()` | Correct / scalar-guard `NotSupported` |
| Q10a–Q10c | unordered ids `1..5`; root `ThenBy(Id)` → `1..5`; root `ThenByDescending(Id)` → `5..1` | `ThenBy(Id)` is indistinguishable from no order; use `ThenByDescending` |
| Q11 | boxing `Select(p => p.Id).Cast<object>()…` | `NotSupportedException` already |
| Q12 | rev 6's type-based `isCollection` rule, transcribed | Misclassifies `T : IEnumerable<object>` terminals (why I2 uses the expression shape) |

**r7 reviewer probes (SQL Server, executed, cited by ID).** Here `b` = `IQueryable<PersonEntity>` over
`db.Query<Person>()` (`Person : PersonEntity`), `d` = `IQueryable<PersonEntity>` over
`db.Query<PersonDetailEntity>()`, and `i` = an interface-typed source.

| ID | Shape | 3.9.0 result |
|---|---|---|
| J1 | `b.OrderByDescending(p => p.Id).First()` | Correct (max id) |
| J2, J3, J3b, J13 | `b.Max/Min/Sum/Average(p => p.Id)` | Correct |
| J4 | `b.Select(p => p.FirstName).ToList()` | Correct (sequence-equal to the concrete query) |
| J7 | `db.Query<Person>().Cast<PersonEntity>().OrderByDescending(Id).First()` | Correct |
| J8 | `o.OrderByDescending(x => ((PersonEntity)x).Id).First()` | Correct |
| J9 | `b.OrderBy(LastName).ThenByDescending(Id).Skip(1).First()` | Correct |
| J10, J12 | other base-typed `OrderBy`/aggregate shapes | Correct |
| J16–J18 | `d.OrderBy(…)` / `d.Max(…)` (join entity via base type) | Correct |
| J19, J20 | `i.Max(x => x.Id)` / `i.OrderByDescending(x => x.Id).First()` | Correct |
| J5, J6, J14, J15 | base-typed `Where` / predicate terminals | `InvalidCastException` |
| J11 | `object`-param subset `Select(new T { … })` | Throws |
| K1 | non-generic `IQueryProvider.Execute(<First expr>)` | **Whole `List<T>` (8,546) returned** as the "first" element |
| K2 | non-generic `Execute(<collection expr>)` | Correct |
| L1 | `Select(p => new Dto { … }).Where(d => …)` | `Select`-shape guard message |
| M1–M3 | entity/scalar `Distinct()` then `Count`/`Any` | "Distinct() combined with an aggregate" `NotSupported` |
| N1, N2 | `o.Distinct().Where(…)`, `q.Cast<object>().Distinct().Where(…)` | `InvalidCastException` |
| N3 | base-typed `Take(5).ToList()` (`IEnumerable<PersonEntity>` over `Person`) | `InvalidCastException` (P14 class; fixed by I2) |
| N4 | scalar-guard message text | Matches the 3.9.0 message the plan reuses |

**r8 reviewer probes (executed; S/X/K = SQL Server; the `r8-PG*`/`r8-MY*`/`r8-SL*` IDs = PostgreSQL / MySQL /
SQLite):**

| ID | Shape | 3.9.0 result |
|---|---|---|
| S1–S3 | base-class `Single(p => p.Id == max)`, `SingleOrDefault(p => p.Id == -1)`, `o.Single(x => x != null)` | **Row Id=1 returned**: predicate dropped (`SELECT … FROM person`), silent wrong row |
| S4–S6, S7b | base-class `Last(pred)`, `LastOrDefault(pred)`, `All(pred)`, `FirstOrDefault(pred)` | `InvalidCastException` |
| S7 | base-class `LongCount(pred)` | Throws (result cast) |
| S8, S9, S9b / J21 | interface `i.Select(x => x.FirstName)`, `i.OrderBy(x => x.LastName).First()`, J9-shaped chain | SqlException "Invalid column name 'firstname'/'lastname'": interface members resolve to the lowercased property name |
| S9c, S9d | interface `Max(x => x.Id)`, `OrderByDescending(x => x.Id).First()` | Correct (`id` happens to be the lowercased name) |
| S10 / r8-MY3 | base-class `Average(p => p.Id)` vs in-memory LINQ | 4340 vs 4340.40… (SQL Server); 1055.4961 vs 1055.49605… (MySQL): the D9 class, equal to the concrete query |
| K3, K4 | non-generic `Execute`, dispatched via `MakeGenericMethod`: `First` → `T`, `Count` → `int` | Correct |
| X1, K5 | `MethodInfo.Invoke` dispatch with `First` on an empty set | `TargetInvocationException` wrapping `InvalidOperationException` (direct call K5b: unwrapped) |
| live SQL Server `dbo.User` | columns `Id, Order, Key`; `ORDER BY id` succeeds | **Has `id`**: owned and recreated by `DocumentationGapTests` (DocumentationGapTests.cs:21-39) |
| r8-PG1, r8-MY1 | live PostgreSQL/MySQL `User` and `SELECT … ORDER BY id` | Match their scripts; `ORDER BY id` errors (42703 / "Unknown column 'id'") |
| r8-SL1 | SQLite `"User"` (INTEGER PRIMARY KEY) `ORDER BY rowid LIMIT 2` | Works (rowid alias) |
| incidental | same entity type used with SQL Server, then PostgreSQL, in one process | PostgreSQL emitted `FROM [User]` (42601): static identifier caches shared across providers (§8) |

**r9–r13 reviewer probes (executed unless marked "code read"):**

| ID | Shape | Result |
|---|---|---|
| r9-I1 | interface source, settable `Gender`: `Select(x => x.Gender)`, `OrderByDescending(x => x.Gender).First()` (SQL Server) | Correct: the lowercased name `gender` matches the column |
| r9-I2 | interface source, get-only `Id`: `Select(x => x.Id)` / `Max(x => x.Id)`, `OrderByDescending(x => x.Id).First()`, `Cast<IGetOnly>().Count()` (SQL Server) | `Select`-shape `NotSupportedException` (the scalar test requires `CanWrite`) / the other three equal the concrete query |
| r9-L1 | SQLite `INT PRIMARY KEY` vs `INTEGER PRIMARY KEY`, key omitted on INSERT | `INT`: key stored NULL (not a rowid alias). `INTEGER`: rowid alias, keys 1..n |
| r9-C1 | *(code read)* INSERT column list in all four dialects | Omits an `int`/`long` key: SqlServerDialect.cs:46-58, PostgreSqlDialect.cs:60-72, MySqlDialect.cs:86-98, SqliteDialect.cs:62-74 |
| r10-S1 | SQL Server interface source: `Max(x => x.Id)`, `OrderByDescending(x => x.Id).First()`, `Select`/`OrderBy` on `Gender`, `OrderBy(x => x.LastName)` | 8866 / 8866 / correct / `SqlException` "Invalid column name 'lastname'" |
| r10-L1 | SQLite temp DB mimicking FunkyORM's insert (`last_insert_rowid()`); `ORDER BY rowid` / `ORDER BY id` | `INTEGER PRIMARY KEY`: Get-by-key works; `INT PRIMARY KEY`: Get-by-key finds nothing. `ORDER BY rowid` runs on both; `ORDER BY id`: "no such column" |
| r11-A1 | SQL Server interface source: `Max(x => x.EmployerId)`, `Min(x => x.DateUtcCreated)` (with and without a prior read, before and after an `OrderBy`); `Max(x => x.Gender)` on the interface and the concrete query | "Invalid column name 'employerid'/'dateutccreated'" in every order (not cache-order-dependent); `Max(Gender)`: `NotSupportedException` "Unsupported selector type System.String" on both sources |
| r11-L2 | real FunkyORM SQLite provider, non-`id` `[Key]`: `INTEGER PRIMARY KEY` vs `INT PRIMARY KEY`, Insert / Get / `Query().Where(…).ToList()` | `INTEGER`: Insert returns keys 1–3, Get and `Query` work, INSERT omits the key. `INT`: Insert reports 1–3 but stores NULL; Get returns null; `Query` returns key 0 |
| r11-C2 | *(code read)* fallback column name for a member the cache doesn't hold | Cache key is `DeclaringType.Name.Prop` (GeneralExtensions.cs:52-56). Visitors, all four providers: `[Column]` ?? `Name.ToLower()`, then cached (e.g. SqlServer BaseExpressionVisitor.cs:50-52). Aggregate path: SQL Server lowercases (SqlServerOrmDataProvider.cs:2314-2320); PostgreSQL/MySQL use `Name`, enclosed if reserved (PostgreSqlOrmDataProvider.cs:1555-1563, MySqlOrmDataProvider.cs:1590-1598); SQLite uses Core's `Name` (OrmDataProvider.cs:381-388). Both paths `GetOrAdd` the same entry of Core's single static `_columnNames` (OrmDataProvider.cs:32), keyed by `ToDictionaryKey()` (aggregate path: SqlLinqQueryProvider.cs:600, PostgreSqlLinqQueryProvider.cs:434, MySqlLinqQueryProvider.cs:436, SqliteLinqQueryProvider.cs:449), so on every provider whichever path writes first fixes the emitted spelling. On SQL Server both paths lowercase, so `Id` is emitted as `id` (in a single-provider process; the static cache is also shared across providers, §8). On PostgreSQL, MySQL and SQLite the aggregate path keeps `Name`, so `Id` is emitted as `id` or `Id` depending on which path wrote first. SQL Server's bare-name lookup (:2318) is filled only by `GetColumnOrdinals` (:2263), which has no callers |
| r12-L3 | *(r12 reviewer)* real FunkyORM SQLite provider, temp DB: `Min`/`Max(x => x.Gender)` on the concrete and interface sources; an interface aggregate followed by a visitor `OrderByDescending(x => x.Id)` | `Max(Gender)` returns a value on both sources (`SELECT MAX(person.Gender)`). The visitor then emits `ORDER BY Id DESC`: the two paths share one cache entry, so the first writer fixes the spelling. `Id` binds to column `id` |
| r13-L1 | *(r13 reviewer)* real FunkyORM 3.9.0 SQLite provider, temp DB: interface `Max(x => x.Id)` and `OrderByDescending(x => x.Id).First()` vs the concrete query, in both cache orders | Equal to the concrete query (4 and 4) in both orders. SQL is `MAX(person.Id)` / `ORDER BY Id DESC` when the aggregate runs first, `id` when the visitor runs first |

**Task 1 red run (2026-10-01; 3.9.0 behavior with the stubs; the four suites plus throwaway probes):**

| ID | Shape | Result |
|---|---|---|
| t1-C1 | the 52 pinned literal-set signatures vs public `Queryable` methods (the sweep's presence check) on net8, net48 and net9 | All present on all three; net9 adds `AggregateBy`/`CountBy`/`Index`, which the set excludes |
| t1-S1 | SQL Server `single_probe` (`INT IDENTITY`) Insert/Get/Query | Works: keys 1–3 returned and read back |
| t1-S2 | SQL Server join entity, narrow projection, `ORDER BY first_name` vs `ORDER BY id`; ternary `x.M == null` in ORDER BY | `first_name` binds (no other joined table has it); `id` is ambiguous. `= NULL` fails: "A constant expression was encountered in the ORDER BY list" |
| t1-P1 | PostgreSQL `single_probe` (`GENERATED BY DEFAULT AS IDENTITY`) Insert/Get/Query | Works (`INSERT … RETURNING single_probe_key`) |
| t1-P2 | PostgreSQL interface `OrderByDescending(x => x.Id).First()`, `Max(x => x.Id)`, both cache orders, each in its own process | `id` and `Id` both fold to `id`; equal to the concrete query |
| t1-M1 | MySQL: the same interface shapes, both cache orders; `single_probe` (`AUTO_INCREMENT`); Skip-only SQL | Equal to the concrete query (case-insensitive columns); `single_probe` works; Skip-only emits `LIMIT 18446744073709551615 OFFSET n` |
| t1-L1 | SQLite join entity: full-entity `OrderBy(p => p.Id)`; default paging; `Skip` alone | `ambiguous column name: id` even for the full entity; `ambiguous column name: rowid`; `near "OFFSET": syntax error` |
| t1-L2 | SQLite root reused after a parameterized projection (AC13-13) | The leaked parameters fail loudly: Microsoft.Data.Sqlite reports duplicate/missing parameter values |

**Still unexecuted:**
- the new 3.10 behaviors themselves (Tasks 2–9);
- `Average` vs in-memory LINQ on PostgreSQL and SQLite. It doesn't gate 3.10.0, because the
  `UnchangedFromConcrete` rows compare with the concrete query, not with in-memory LINQ.

---

## 2. Decisions (all made by the owner, 2026-09-30)

| # | Decision | Outcome |
|---|---|---|
| **D1** | `Single`/`SingleOrDefault`/`Last`/`LastOrDefault`/`LongCount` | **Implement.** `Single*` uses a row limit, not the paging path (§5.2.3). |
| **D2** | Allow-list form | **Exact-overload allow-list in Core** (`QueryOperatorPolicy`), run as a **pre-pass over the method spine** before translation. It guarantees that no query or aggregate command runs for a rejected chain; schema discovery at `Query<T>()` is unaffected. |
| **D3** | Version / branch | **3.10.0** on `development/3.10`; beta first, stable after a Sentinel call-list smoke test. |
| **D4** | Local PostgreSQL | Native PostgreSQL 18 via the suite's fallback connection; CI uses `postgres:17`. |
| **D5** | `Cast`/`OfType` | **Identity**, plus *(owner amendment, rev 6)* **reference-conversion `Cast`**. Since rev 7 both are judged against the **row type `R`** (§5.2.1): the entity `T`, or the member type after a scalar `Select`.<br>• `Cast<X>` with `X == R`: a no-op.<br>• `Cast<TBase>` where `R` is a **reference type assignable to `TBase`** (e.g. `object`, an interface, a base class): **transparent**, like the implicit conversion `IQueryable<TBase> b = q` (P2b, P5). Later operators are governed by invariants I1/I2. A second cast back, such as `Cast<object>().Cast<T>()`, is judged against `R` too, so it's allowed (r6 Q4a).<br>• `OfType<X>`: a no-op only when `X == R` and `R` is the entity `T` (rows are never null) or a non-nullable value type.<br>• Everything else is rejected: boxing `Cast` (`Select(p => p.Id).Cast<object>()`, already `NotSupported` in 3.9.0, r6 Q11), unrelated-type `Cast`, non-identity `OfType`, and `OfType` over a nullable or reference scalar (it would have to drop nulls; the message says to use `Where(x => x.M != null)` before the projection). |
| **D6** | `Last*`/`ElementAt*` with paging | Reject both. |
| **D7** | Disclosure | Changelog + README "upgrade strongly recommended"; the owner decides on a GitHub Security Advisory. |
| **D8** | Operators after `Skip`/`Take` | **Reject by position** in the Core pre-pass. Allowed after the first `Skip`/`Take`:<br>• one `Take` after a `Skip`, when only `Select`/an allowed D5 `Cast`/`OfType` sit between them (projection and conversion commute with `Take`);<br>• `Select`;<br>• parameterless `First*`/`Single*`;<br>• an allowed D5 `Cast`/`OfType`.<br>Everything else is rejected before any query runs. Some rejected shapes happen to be correct in 3.9.0, e.g. `Take(3).Distinct()` over a keyed entity (P11); the Changelog lists them.<br>`Take(n ≤ 0)` returns an empty sequence without SQL. `Skip(n < 0)` is treated as `Skip(0)`. |
| **D9** | Aggregate correctness | **Moved to 3.10.1** (D12). The owner accepts the recommended direction with changes; details and open questions are in §10. In 3.10.0, aggregates keep their 3.9 behavior, except `LongCount` (new) and rejection of the non-generic `Sum()`/`Average()` overloads, which already threw in 3.9. |
| **D10** | A second `OrderBy*` after an earlier `OrderBy*`/`ThenBy*` anywhere on the spine | **Reject**, in the Core pre-pass. The message says to use `ThenBy`, or put the primary key first. *(Rev 4 added a second clause for a `ThenBy*` separated from its ordering. Rev 6 removed it: System.Linq throws `ArgumentException` for that shape before the provider sees it (§1.5 P3), so it can't reach the policy. A `ThenBy*` directly on the root works as the primary ordering (P4).)* |
| **D11** | SQLite state and ternary nulls | **Fix in scope.**<br>• SQLite resets **every** per-parse field (including the new `OrderByTerms`) at the top of every parse, before the early return.<br>• ORDER BY ternary null tests emit `IS NULL`/`IS NOT NULL`, with `null` on either side of the comparison. |
| **D12** | Release sequencing | **Split.** 3.10.0 ships everything in this document except D9; 3.10.1 ships D9 (§10). |

---

## 3. Acceptance criteria (v3.10.0)

These will be posted to #12 and #13 once this revision is verified clean. Each AC holds for **all four
providers** unless stated.

### #12 — own-column ORDER BY on remote-join entities

- **AC12-1** On an entity with remote joins, every combination below emits own columns as
  `{baseTable}.{column}` (asserted on the SQL text) and returns rows in the requested order:
  - operator: `OrderBy`/`OrderByDescending`, or `ThenBy`/`ThenByDescending` after a remote-member `OrderBy`,
    over an own column;
  - shape: full entity, subset projection that **omits the key**, or scalar projection of a different member;
  - paging: with or without `Skip`/`Take`.

  Ordering and paging always come **before** the projection: `OrderBy…[ThenBy…][Skip/Take].Select(…)`.
  Ordering *after* a subset `Select` is the §8 divergence, and is never used as a passing shape.
- **AC12-2** Ordering by a remote/computed member emits exactly its resolved fragment, with no base-table
  prefix, unchanged from 3.9.0, except SQLite's discovered spelling of convention-mapped columns after the
  provider-scoped caches plan's D5.
- **AC12-3** For an entity without remote joins, the **ORDER BY translation** of
  `OrderBy*`/`ThenBy*` chains consumed by enumeration or `First*` is byte-identical to 3.9.0. Other ACs change
  ordering SQL on purpose, and are excluded:
  - AC12-8: ternary nulls;
  - AC12-9: duplicate keys;
  - AC13-2: `Last*` inverts or synthesizes the order;
  - AC13-12: a second `OrderBy` is rejected.

  Other ACs also change non-ORDER-BY SQL on purpose: the `Single*` row limit, SQLite `LIMIT -1`.
- **AC12-4** A ternary/`CASE` ordering over own columns on a join entity is qualified inside the `CASE`.
- **AC12-5** The ORDER BY synthesized for `Last*` (AC13-2) is qualified on join entities, including with a
  narrow projection that omits the key (no `Distinct`). The shape is `Select(…).Last()` with no ordering
  after the projection.
- **AC12-6** On the join entity, `Distinct()` + custom projection + own-column `OrderBy`, spelled
  ordering-first (`OrderBy(k).Select(…).Distinct()`):
  - executes when the key is in the projection;
  - still throws the existing `InvalidOperationException` when it isn't.
- **AC12-7** *(SQLite)* `Skip`/`Take` on a join entity with no explicit order executes, for the full entity
  and for a subset projection.
- **AC12-8** An ORDER BY ternary whose test compares a member with `null`, with `null` on either side
  (`x.M == null`, `null == x.M`, `x.M != null`, `null != x.M`), orders rows the way LINQ-to-objects does. The
  `null` may be a literal or a value held in a variable (a captured `string` or `int?`) *(rev 22, r67-1)*: any operand
  that reads no parameter of the ordering lambda and evaluates to null, including one with a lambda of its own
  (`names.FirstOrDefault(n => …)`). Each such operand is evaluated once *(rev 24, NF-1/NF-2)*, also when it throws.
  It's read through `Convert` (not `ConvertChecked`, which is part of the value), so a captured `char` stays a char
  literal, and an enum value is its underlying number *(rev 25; rev 26)*. Branch values take the same path, so a
  value reads the same in the test and in a branch *(rev 26)*.
- **AC12-10** *(rev 30, owner decision 2026-10-01)* In an ORDER BY ternary, booleans, enums, `NULL` and values of
  type `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double` and `decimal` stay
  inline. No other value is written into the command text: each is sent as a command parameter where the SQL uses
  it. *(Rev 31–32; lead-in restated in rev 41)*
  - **What a parameter carries:** its value's text (below), typed as the literal was:
    - untyped on PostgreSQL;
    - text on MySQL and SQLite;
    - `varchar` on SQL Server, except strings and chars, which are `nvarchar` like WHERE's.

    A `DateTime`, `DateTimeOffset`, `DateOnly` or `TimeOnly` has a fixed format (Changelog; a `DateTime`'s is
    3.9.0's). Any other value's text is `Convert.ToString` with the invariant culture. On MySQL a `DateTimeOffset`
    is its UTC time, as WHERE sends it *(rev 39)*.
  - **One parameter per occurrence:** each occurrence of a value is its own parameter, as each literal was its own
    literal, so the database types each one where it's used.
  - **On PostgreSQL**, a value that would be a parameter, compared with `null`, is decided in .NET: `IS NULL`
    can't give an untyped parameter a type. An inline value is sent (`5 IS NULL`) *(rev 43, M2)*.
  - **Result:** a `Guid` or `DateTime` value compares and sorts as it did in 3.9.0, on every provider.
  - **A `DateTimeOffset`, `DateOnly` or `TimeOnly`:** its text is fixed (above). How a database compares that text
    with a column is the database's rule, and this AC asserts none. *(Rev 39, J1: three rounds of per-position claims
    were each broader than the execution.)*
    - Tested: the `DateOnly` and `TimeOnly` branch values sort as LINQ-to-objects does.
    - Tested: on MySQL, ORDER BY and WHERE pick the same rows for a `DateTimeOffset`.
  - **Duplicate terms are still dropped (AC12-9).** Terms are compared with each value written as its kind and
    text, and a dropped term binds nothing.
  - **Tested on every provider:** a text value with quote or backslash characters, compared in a ternary's test,
    orders rows as LINQ-to-objects does *(rev 33: narrowed, R3)*, and no quoted value appears in the command text,
    in any position *(rev 34, F2)*.
  - A command carries only the parameters it uses: an aggregate drops the ORDER BY and its parameters.
  - The visitors' 3.9.0 constructors have no generator and still inline values; MySQL's inline literal also
    escapes backslashes.
- **AC12-9** A duplicate ordering key (`OrderBy(a).ThenBy(a)`) executes. Later duplicate fragments are
  dropped; they can never break a tie, so the order is unchanged.

### #13 — operator correctness

- **AC13-1** `Single`/`SingleOrDefault` match LINQ-to-objects:
  - The predicate, if any, is applied as `WHERE`.
  - Zero rows: `Single` throws `InvalidOperationException`; `SingleOrDefault` returns `null`.
  - Two or more rows: both throw `InvalidOperationException`.
  - With no user `Skip`/`Take`, it emits `TOP 2` / `LIMIT 2` and **no synthesized `ORDER BY`**. It works on
    an entity whose key column isn't `id`, and after `Distinct()` + custom projection.
  - **Parameterless** `Single*` after user paging reads `min(Take ?? 2, 2)` rows: `Skip(n)` only,
    `Skip(n).Take(k)`, and `Take(1)` (never throws "more than one").
- **AC13-2** `Last`/`LastOrDefault` (with and without a predicate) match LINQ-to-objects:
  - **Explicit order.** Every term of the `OrderBy`+`ThenBy` chain is inverted: own, remote/computed and
    `CASE` terms. Inverted own terms stay table-qualified on join entities, as in AC12-1 *(rev 18)*.
  - **No order.** They default to `Id DESC`, table-qualified when the entity has joins (as in AC12-3/§5.1).
    The existing "no `Id` property" error is kept. It's reported after the scalar-projection and `Distinct`
    guards, so it never masks them *(rev 22, r67-7)*.
  - **Empty.** `Last` throws; `LastOrDefault` returns `null`.
  - **One row read.** It emits `TOP (1)` / `LIMIT 1` with that ORDER BY *(rev 21)*.
  - **After `Skip`/`Take`.** Rejected (D6/D8).
  - **After `Distinct()` + custom projection with no explicit order.** Throws `NotSupportedException` naming
    the terminal (`Last` or `LastOrDefault`, with or without a predicate), with a reason that holds whether or
    not the key is projected *(rev 22, r67-3/5)*. With an explicit order on a projected key, it works.
  - **NULL placement** *(rev 22, r67-4)*. `Last` is the last row of the provider's own order: it equals
    `OrderBy(k).ToList().Last()`. Where the database sorts NULLs differently from LINQ-to-objects (PostgreSQL:
    last when ascending), `Last` follows the database, as `First` and `ToList` do (§8).
- **AC13-3** `LongCount()` and `LongCount(pred)` return an `Int64` equal to `Count`. SQL Server emits
  `COUNT_BIG(*)`. A predicate over a reverse remote key is rejected like `Count`.
- **AC13-4** Every spine node that isn't an allowed `Queryable` overload throws `NotSupportedException`
  naming the operator, and **no query or aggregate command is executed**: `Log` receives nothing after the
  `IQueryable` is obtained. That includes:
  - non-`Queryable` methods;
  - non-generic `Queryable` overloads;
  - non-call, non-root nodes.

  **Covariance follows the three §5.2.1 invariants (rev 7).** A conversion (the no-node
  `IQueryable<TBase> b = q`, or a reference-conversion `Cast<TBase>()`) never changes the row type `R`.
  - **I1 (rev 8: narrowed by the owner).** A **predicate** lambda (`Where`; `First*`/`Single*`/`Last*`/
    `Any`/`All`/`Count`/`LongCount` with a predicate) whose parameter isn't `T`, and which isn't outer to a
    non-subset `Select`, is rejected before any query with the I1 message. Every such shape fails in 3.9.0:
    - `Where`, `First*`/`Last*`/`Any`/`All`/`Count` with a predicate throw `InvalidCastException` (§1.5
      I1a/b/d/e/f/g, J5/J6/J14/J15, N1/N2, S4–S6, S7b);
    - `LongCount(pred)` throws on its result cast (S7);
    - `Single*(pred)` **silently returns an unrelated row** (S1–S3).
  - After a scalar `Select`, the existing composition guard rejects all lambdas with its own message
    (AC13-8). After any other non-subset `Select`, the `Select`-shape guard rejects the `Select` (r7 L1, I1h).
  - **I2.**
    - Over a scalar projection, every terminal **that reaches execution** is rejected with the scalar-guard
      message. That covers terminals reached directly (P6, P7) or after a covariant `Distinct` (Q1c/Q1e)
      or after `Skip`/`Take` where D8 allows the terminal (`First*`/`Single*`; Q1a/b/d). In 3.9.0 those
      return the whole list.
    - Over an entity source, enumeration of a converted source returns the entity rows. This fixes P14 and
      base-typed paging (N3).
  - **Left alone** (correct in 3.9.0, and still correct):
    - parameterless terminals over a converted entity source (`Count`/`Any`/`First*`; P5, P13, Q9);
      `LongCount`/`Single*`/`Last*` get the AC13-1/2/3 semantics;
    - lambda-free `Skip`/`Take`/`Distinct` over any converted source (Q3a–e, P7b–P7e);
    - enumeration of a converted scalar source (P7e);
    - **non-predicate lambdas** (`OrderBy*`/`ThenBy*`, selector aggregates, scalar `Select`) over a
      **base-class** source: unchanged from the concrete query (J1–J4, J7, J9, J10, J12, J13, J16–J18).

    **Unchanged, not "correct":** over an **interface** source, non-predicate lambdas behave as in 3.9.0,
    where a member doesn't resolve to the entity's mapped column (pre-existing, §8). The tests pin only `Id`
    (`OrderByDescending(x => x.Id).First()`, `Max(x => x.Id)`), which equals the concrete query (§1.4). 3.10.0
    doesn't change this.

    After `Skip`/`Take`, D8 still governs (e.g. covariant `Count` after `Take` gets the D8 message). After
    `Distinct`, the existing "Distinct() combined with an aggregate" guard governs `Count`/`Any` (r7 M1–M3).

  Lambdas nested inside allowed operators aren't inspected, so `Where(p => ids.Contains(p.Id))` still works.
  The rejection table covers, at minimum:
  - **Ordering and slicing:** `Reverse`, `TakeWhile`, `SkipWhile`, `TakeLast`, `SkipLast`, `ElementAt`,
    `ElementAtOrDefault`, `Order`, `OrderDescending`.
  - **Set, join and sequence:** `DefaultIfEmpty`, `Concat`, `Union`, `Intersect`, `Except`, `Zip`,
    `SelectMany`, `Join`, `GroupJoin`, `Append`, `Prepend`, `SequenceEqual`, `Chunk`.
  - **Reducing:** `Contains`, `Aggregate`, `DistinctBy`, `MinBy`, `MaxBy`, and parameterless `Min()`/`Max()`
    on an entity.
  - **Type filters:** `Cast`/`OfType` outside D5/I3: boxing or unrelated-type `Cast`; non-identity `OfType`;
    `OfType` over a nullable or reference scalar.
  - **Unsupported overloads of allowed operators:** indexed `Where`/`Select`; comparer overloads;
    default-value overloads; `Take(Range)`; non-generic `Sum()`/`Average()`.
- **AC13-5** Every shape built only from allowed operators, in allowed positions, that returned correct
  results in 3.9.0 still works identically.
  - The pre-pass rejects by **rule** (allow-list, D5, D8, D10, covariance). Most rejected shapes returned wrong
    results or threw obscurely in 3.9.0. A few happened to be correct: `Take(k).Distinct()` over a keyed entity
    (P11), `Take(k≥1).Any()`, `Take(10).Take(5)`, `OrderBy(k).ElementAt(0)`, `DefaultIfEmpty()` over a
    non-empty set. Also *(rev 21, T4R-3; executed against 3.9.0)*:
    - `Skip(0)` followed by anything but `Take`, `Select`, `Cast`/`OfType` or a parameterless `First*`/`Single*`
      (D8): `Skip(0).Where(w)`, `Skip(0).Count()`. `Skip(0)` is page 1 of every `Skip(page * size)` helper. On
      SQLite only `Skip(0).Count()` worked: `Skip` without `Take` was invalid SQL there *(rev 23, r810-F2)*.
    - A second ordering whose result happened to match (D10): `OrderBy(a).Where(w).OrderByDescending(k).First()`.
  - The Changelog **Changed** section lists each rule with such examples.
  - All four existing suites stay green, and there's one oracle row per allowed family. Aggregate rows are limited to shapes correct in
  3.9.0 on all four providers: `Count`, `Any`/`All`, and `Sum`/`Min`/`Max` over `int`, on non-empty sets. No
  `Average` rows: SQLite's `ROUND(…, 10)` alters even `double` averages. The rest is 3.10.1. Oracle predicates
  reference only members seeded non-null, and oracle shapes never place a predicate or ordering after a
  subset `Select` (§4.1, §8).
- **AC13-6** *(D5/I3: judged against the row type `R`)*
  - Identity `OfType<R>()` is a no-op on the entity (at the root, after `OrderBy`, after `Cast<object>()`) and
    on a non-nullable scalar (`Select(p => p.Id).OfType<int>()`).
  - Identity `Cast<R>()` is a no-op, including back to the entity after a transparent cast
    (`Cast<object>().Cast<T>()`, r6 Q4a).
  - A reference-conversion `Cast<TBase>()` is transparent for `TBase` = `object`, an interface, or a base
    class. `q.Cast<object>().Count()`/`.First()` and a cast to an implemented interface (e.g.
    `Cast<IHasPersonId>().Count()`) match 3.9.0
    (P5, P5b, Q4d, r9-I2), and later operators follow AC13-4.
  - **Enumerating** a reference-conversion `Cast` over the entity (`q.Where(…).Cast<object>().ToList()`, or to
    a base class) returns the entity rows, the same as the implicit conversion (P14b). In 3.9.0 it throws
    `InvalidCastException` (P14, Q4c); fixed by I2.
  - These throw: `OfType` over a nullable or reference scalar; non-identity `OfType`; boxing `Cast`
    (`Select(p => p.Id).Cast<object>()`); unrelated-type `Cast` (`Cast<AddressEntity>()` on a person query).
- **AC13-7** One **literal** pinned set of allowed overloads (count + signatures) is asserted identically on
  three runtimes:
  - net8, in `SqlServer.Tests` (MSTest);
  - net48, in `Funcular.Data.Orm.SqlServer.Tests.NetFramework` (MSTest, linked file);
  - net9, in `SqlServer.Tests.DotNet9` (an **xUnit twin**: that project is xUnit).

  On each runtime, a sweep drives the public classifier (`IsAllowed`) over every public `Queryable` method and
  compares the result with that literal set. Consumers run the net8 build on newer runtimes, whose
  `Queryable` gains methods, so this matters.
- **AC13-8** Allowed operators keep their specific messages: the post-scalar-projection composition message,
  the scalar result-type guard, and `GroupBy`'s dedicated text. Rejected operators get the policy message,
  even after a scalar `Select`. Precedence:
  - **pre-pass 1**: the allow-list over the whole spine, outer→inner (the outermost failure wins; `GroupBy`
    is special-cased);
  - **pre-pass 2**: inner→outer, per node, D8 → D10 → D5/I3 → I1;
  - then parse-loop guards;
  - then execute-time guards (I2, `ScalarProjectionGuard`).

  For example, `q.Take(5).Where(w).Reverse()` gets the `Reverse` allow-list message, not D8's.

  *(Rev 21, T4R-2.)* After an unsupported `Select` (neither a column subset nor a scalar member), D5/I3 and I1
  don't judge the operators outer to it: the parse loop's `Select`-shape message reports it. A **subset**
  `Select` keeps the entity as the row type, so D5/I3 still judge a later `Cast`/`OfType` *(rev 22, r67-2)*.
  `Select(p => new Dto { … }).Cast<Dto>()` gets the `Select`-shape message, not D5's.
- **AC13-9** `Advanced.md` and `FUNKYORM_AI_ADVANCED.md` carry the operator table, including the paging rule,
  the second-`OrderBy` rule and the `Last`/`Distinct` note. A doc test checks the table against
  `SupportedOperators`. Changelog and README record the fixes, the behavior changes, and the aggregate known
  issues deferred to 3.10.1.
- **AC13-10** *(D8)* After `Skip`/`Take`:
  - **Allowed and correct:**
    - `Skip(n).Take(k)`, and `Skip(n).X.Take(k)` where `X` is only `Select` and/or an allowed D5
      `Cast`/`OfType`, e.g. `Skip(n).Select(…).Take(k)`, `Skip(n).OfType<T>().Take(k)`,
      `Skip(n).Cast<T>().Take(k)`;
    - `Select` (subset or scalar);
    - parameterless `First*`/`Single*`;
    - an allowed D5 `Cast`/`OfType` (judged against `R`: e.g. `Skip.Take.Select(p => p.FirstName).Cast<object>()`
      enumerated is correct, r6 Q7).
  - **Rejected before any query**, each asserting the "after Skip/Take" message: every aggregate, `Where`,
    predicate-bearing `First*`/`Single*`, `Last*`, `OrderBy*`, `Distinct`, a second `Skip`/`Take`, and
    `Take(n).Skip(m)`. Any other operator placed between `Skip` and `Take` is itself rejected at its own node.
    (`ThenBy*` can't follow `Skip`/`Take` at all; System.Linq throws first, P3b.)
  - **Precedence.** When D8 and D10 both apply at a node (e.g. `OrderBy(a).Skip(n).OrderBy(b)`), the D8
    message wins.
  - **Empty results without SQL:** `Take(n ≤ 0)` returns an empty sequence, whether it's the full entity,
    a subset projection, a scalar projection, or `Skip(n).Take(0)`. On the entity path (the entity, a subset
    projection, or a converted entity source), parameterless `First*`/`Single*` then act as on an empty
    sequence: `First`/`Single` throw and `*OrDefault` returns `null`.
    Over a scalar projection, parameterless `First*`/`Single*`, `*OrDefault` included, throw the scalar
    guard's `NotSupportedException` instead (I2; §5.2.6).
  - `Skip(n < 0)` behaves as `Skip(0)`.
- **AC13-11** *(deferred to 3.10.1 — §10.)*
- **AC13-12** *(D10)* `OrderBy*` after any earlier `OrderBy*`/`ThenBy*` on the spine throws
  `NotSupportedException` with the D10 message before any query runs, including across
  `Where`/`Select`(subset)/`Distinct` in between.
  - A `ThenBy*` applied directly to the root (`((IOrderedQueryable<T>)q).ThenBy(k)`) still works as the primary
    ordering (P4).
  - A `ThenBy*` after any other call can't be built: System.Linq throws `ArgumentException` (P3). That also
    breaks `is IOrderedQueryable` sort helpers on composed queries (P4b), a pre-existing bug tracked in #16
    and not changed here.
- **AC13-13** *(D11, SQLite)* A query doesn't inherit a projection, parameters, ordering or `OrderByTerms`
  from an earlier execution on the same `Query<T>()` root, including when the later query is the bare root.
- **AC13-14** *(SQLite)* `Skip(n)` without `Take` executes: it emits `LIMIT -1 OFFSET n`. That covers
  `Skip(n).ToList()` and `Skip(n).First()`. (`Skip(n).Single()` is capped by AC13-1, so it emits
  `LIMIT 2 OFFSET n`.)
- **AC13-15** *(I2, rev 8)* The non-generic `IQueryProvider.Execute(expression)` returns the single
  element (or throws, as LINQ would) for a terminal expression such as `First`, and the list for a
  collection expression. In 3.9.0 it returns the whole list for `First` (r7 K1).

---

## 4. Test plan (written and reviewed first — Task 0)

### 4.1 Harness

- **Discriminating seed data.**
  - Each semantics test inserts its own marker-tagged rows and scopes every query with
    `Where(p => p.LastName == marker)`.
  - The target row is neither the first nor the last by id. For `Last`, the order key is not `Id`, and its
    last row isn't the max id.
  - Each test states which wrong answer it rules out.
  - Counts are > 0.
- **#12 ordering assertions.**
  - The own column ordered by is `Id`. Every joined table has an `id`, so the unqualified form is ambiguous
    under a narrow projection, while `first_name` isn't (t1-S2).
  - Rows are seeded so that `FirstName` order equals `Id` order (`"a"`, `"b"`, `"c"`).
  - AC12-1/AC12-5 subset rows project `FirstName` only, never the key, and assert the `FirstName` sequence.
    On 3.9.0 these fail at runtime (ambiguous column), not just on the SQL text.
  - Every #12 test **also** asserts the qualified SQL fragment.
- **Oracle assertion.** `AssertMatchesLinqToObjects(shape)` runs the shape against a **fresh**
  `Query<T>()` and against `provider.Query<T>().Where(marker).ToList().OrderBy(p => p.Id).AsQueryable()`.
  It compares by id, by value, or by exception type.
- **Oracle determinism rules.**
  - Order-dependent shapes have a total order on the **composite key tuple**.
  - Null keys and collation-vs-ordinal string order are never oracle-compared; they get explicit
    per-provider tests.
  - Remote LEFT-JOIN keys are seeded non-null.
  - **Oracle predicates reference only members seeded non-null.** SQL three-valued logic makes `!=` and `All`
    diverge from LINQ when NULLs are present (§8).
  - **No predicate or ordering after a subset `Select`**, in **any** passing shape, oracle or not. FunkyORM
    evaluates it against base columns, while LINQ sees the projected defaults (§8). The only exceptions are
    rejection rows, where the shape is rejected for another reason (D8/D10).
- **"Same as concrete" rows require the concrete shape to succeed** (`requireSuccess`); the root `Single` rows, which
  throw by design over the whole table, assert `InvalidOperationException` on both sides instead. Otherwise a row passes
  when both sides throw the same exception. The PostgreSQL port found one: `ScalarSelect` ordered by `Id` threw
  #12's ambiguity on both sides.
- **The top-level ORDER BY is the last one in the command.** Computed members carry their own inside
  SELECT-list subqueries.
- **Fresh root per shape.** One `Query<T>()` root is never reused, except by the AC13-13 tests, which do it
  on purpose.
- **Covariance rows are spelled both ways:** the no-node `IQueryable<object> o = …` (P2b) and
  `.Cast<object>()` (a node, P2). They also include the "**after an allowed sequence operator**" variant
  (`…Take(3).First()`; r6 Q1), since that's where rev 6's design leaked.
- **Shape premises cite §1.5.** A test whose premise about System.Linq or a database hasn't been executed is
  flagged UNVERIFIED in the matrix and must go red for the right reason in Task 1.
- **SQL capture.** For "nothing executed": obtain the `IQueryable`, clear `Log`'s builder, run the shape,
  then assert the builder is empty. **SQL-shape asserts normalize whitespace first** (collapse `\s+` to one
  space), because PostgreSQL and SQLite emit `LIMIT`/`OFFSET` on separate lines and MySQL on one.
- **Test entities.**
  - Join entity: `PersonDetailEntity` (SQL Server/PG/SQLite) or `PersonWithEmployer` (MySQL); every joined
    table has an `id` column.
  - **No-`id` entity: a test-owned table (rev 9; replaces rev 8's `User` choice).** Rev 8 picked `User`
    after reading the scripts only. Live, SQL Server's `DocumentationGapTests` **drops and recreates `[User]`
    with an `Id` column on every setup** (DocumentationGapTests.cs:21-39), and the local `dbo.User` already
    shows that shape (r8). So `User`'s columns depend on test order.
    - Instead, the AC13-1 no-`id` test class creates its own table in its own setup with idempotent DDL.
      `single_probe` has an **identity** key `single_probe_key` and a `name` column, and **no `id` column**.
      The identity is required because FunkyORM's INSERT omits an `int`/`long` key (SqlServerDialect.cs:46-58
      and the sibling dialects; r9-C1). Per provider:
      - SQL Server: `IF OBJECT_ID('single_probe') IS NULL CREATE TABLE single_probe (single_probe_key INT
        IDENTITY(1,1) PRIMARY KEY, name NVARCHAR(100) NULL)`;
      - PostgreSQL: `CREATE TABLE IF NOT EXISTS single_probe (single_probe_key INT GENERATED BY DEFAULT AS
        IDENTITY PRIMARY KEY, name VARCHAR(100))`;
      - MySQL: `CREATE TABLE IF NOT EXISTS single_probe (single_probe_key INT AUTO_INCREMENT PRIMARY KEY,
        name VARCHAR(100))`;
      - SQLite: `CREATE TABLE IF NOT EXISTS single_probe (single_probe_key INTEGER PRIMARY KEY, name TEXT)`.

      The entity maps the key with `[Key]`. Existing coverage of non-`id` `[Key]` identity entities:
      - MySQL: Insert/Get/Query (MySqlReservedWordTests.cs:24-45);
      - PostgreSQL: Insert/Get/Query (PostgreSqlDocumentationGapTests.cs:24-37);
      - SQLite: Insert/Get only (SqliteDataProviderIntegrationTests.cs:636-640);
      - SQL Server: none.

      SQL Server, and PostgreSQL's `GENERATED BY DEFAULT AS IDENTITY` spelling, are confirmed by Task 1's red
      run (listed in §1.5's "still unexecuted"). SQLite's `Query` is executed (r11-L2).
    - This follows the same pattern as `DocumentationGapTests` and the SQLite suites. No schema-script
      changes; independent of local drift and test order.
    - On SQLite the key must be spelled exactly `INTEGER PRIMARY KEY`. That makes it a rowid alias that
      receives the generated key; `INT PRIMARY KEY` doesn't, and stores NULL (r9-L1, r10-L1).
    - Separately, the paging mutation's `ORDER BY rowid` runs on any rowid table (r10-L1). So on SQLite the
      §4.4 row's SQL-shape assert carries the kill, alongside `Single_AfterDistinctProjection_Works`.
  - **Base-class and interface sources (r8 #5).** Task 1 adds these to the test domains:
    - In all four test projects, an interface `IHasPersonId { int Id { get; } }`, implemented by the person
      entity. This is for the interface rows, which are limited to `Id`; a marker-only interface can't
      support `p => p.Id`.
    - In MySQL, which has no person base class, a `PersonBase { Id, FirstName, LastName }` as the base of
      `Person` and `PersonWithEmployer`, so `IQueryable<PersonBase>` over `Query<PersonWithEmployer>()` is
      the MySQL base-class source.
    - The other three use `IQueryable<PersonEntity>` over `Query<PersonDetailEntity>()`.
- **Cleanup** deletes run in a transaction. **Subset projections** don't populate `Id`.
- **SQLite** gets a shared temp-DB schema helper.
- **DB-free tests don't inherit a DB fixture.**
- **net48-linked tests** compile as C# 7.3, and refer to post-net48 `Queryable` members by name (reflection)
  or inside `#if NET6_0_OR_GREATER`.

### 4.2 AC → test matrix

**Test classes:**
- Per provider (prefixed in the siblings): `OrderByQualificationTests`, `QueryOperatorSemanticsTests`,
  `QueryOperatorRejectionTests`, and `OrderByVisitorDirectTests` (DB-free).
- `QueryOperatorPolicyTests` (Core, DB-free, MSTest) lives in `SqlServer.Tests` and is linked into
  `Funcular.Data.Orm.SqlServer.Tests.NetFramework` (net48, MSTest).
- `SqlServer.Tests.DotNet9` (net9, xUnit) gets `QueryOperatorPolicyLiteralSetTests`, an xUnit twin asserting
  the same literal set and running the same sweep. The literal set lives in one shared source file of
  signatures, linked into all three projects, so the twins can't drift.

| AC | Test(s) | Project(s) |
|---|---|---|
| AC12-1 | `[DataTestMethod] OwnColumnOrdering_OnJoinEntity_QualifiedSql_ExecutesInOrder` — {OrderBy, OrderByDesc, ThenBy-after-remote, ThenByDesc-after-remote} × {full, subset-without-key, scalar-of-FirstName} × {paged, unpaged} (24 rows); every row is `OrderBy…[Skip/Take].Select(…)`, with ordering and paging before the projection | all 4 |
| AC12-2 | `RemoteMemberOrderBy_EmitsExactResolvedFragment_NoBasePrefix` (SQLite re-pinned after D5), `ComputedMemberOrderBy_EmitsExpression_Unchanged`; direct, with the qualifier set: `MapHit_ComputedFragment_NeverPrefixed` | all 4 |
| AC12-3 | `SingleTableEntity_OrderBy_SqlByteIdenticalTo390`; *(rev 24)* `ComputedAttributeEntityWithoutJoins_OwnColumnOrder_Unqualified` (all 4; MySQL's uses `ProjectScorecard`, rev 25) | all 4 |
| AC12-4 | `TernaryOrderBy_OwnColumns_OnJoinEntity_QualifiedInsideCase` | all 4 |
| AC12-5 | `Last_OnJoinEntity_ProjectionWithoutKey_SynthesizedOrderQualified` (asserts `{table}.id DESC` and the returned `FirstName`) | all 4 |
| AC12-6 | `Distinct_Projection_JoinEntity_OrderByKeyInProjection_Executes`, `Distinct_Projection_JoinEntity_OrderByKeyNotInProjection_ThrowsExisting` | all 4 |
| AC12-7 | `DefaultPaging_OnJoinEntity_Executes`, `DefaultPaging_OnJoinEntity_SubsetProjection_Executes` | SQLite (regression rows in the other 3) |
| AC12-8 | `[DataTestMethod] TernaryOrderBy_NullComparison_MatchesOracle` over {`x.M == null`, `null == x.M`, `x.M != null`, `null != x.M`}; *(rev 22)* rows 4–7 (a captured null, both operand orders, `==`/`!=`); direct `Ternary_Branch_BuildsCase[captured null ==, captured null !=, reversed, captured int? null, captured int? null, reversed, captured value]`; *(rev 24)* row 8 (a null computed by a nested lambda); direct `Ternary_Branch_BuildsCase[null computed by a nested lambda]`, `TernaryOperand_EvaluatedOnce_NullCheckAndSqlAgree`; *(rev 25)* `TernaryOperand_ThatThrows_EvaluatedOnce_Rejected`, `CapturedCharAndEnum_FormatAsTheirValues`, `BlockOperand_DeclaredVariable_DoesNotReadTheRow`; *(rev 26)* `CheckedConversion_IsEvaluated_NotUnwrapped`, `CapturedInstanceProperty_SameValueInTestAndBranch`, `TernaryOperand_ThatThrows_KeepsThe390Message`; non-int enums and a catch variable in the existing direct tests | all 4 |
| AC12-9 | `ThenBy_SameKeyTwice_Executes` (SQL-text asserts one occurrence); *(rev 24)* `ThenBy_SameTernaryKeyTwice_Executes` (a `CASE` key listed twice; all 4) | all 4 |
| AC12-10 *(rev 30)* | `TernaryOrderBy_TextWithQuotesOrBackslashes_IsAParameter_MatchesOracle` [3 values], `Last_AfterTextTernaryOrderBy_InvertedOrderKeepsItsParameters`, `TextTernaryOrderBy_WithWhereParameters_MatchesOracle`, `Count_AfterTextTernaryOrderBy_Works`, `ScalarProjection_AfterTextTernaryOrderBy_BindsTheParameters`, `TextTernaryOrderings_SendOnlyTheParametersTheCommandUses`; *(rev 31)* `TernaryOrderBy_GuidValue_MatchesOracle`, `TernaryOrderBy_DateTimeValue_MatchesOracle` [6 rows: `==`/`>` × whole/half second × unspecified/UTC kind], `TernaryOrderBy_GuidBranchValues_MatchesOracle`, `TernaryOrderBy_DateTimeOffsetBranchValues_MatchesOracle`, `TernaryOrderBy_NonAsciiText_MatchesOracle`; *(rev 32)* `TernaryOrderBy_CapturedValueComparedWithNull_MatchesOracle` [4 shapes], `TernaryOrderBy_OneValueAgainstADateThenATimestamp_MatchesOracle` [2], `TernaryOrderBy_ValueAsBranchThenCompared_MatchesOracle` [Guid, date], SQL Server `TernaryOrderBy_DateTimeValue_OnALegacyDatetimeColumn_ComparesAs390Did`; direct `ParameterMode_*` (10 tests; 11 on PostgreSQL), incl. *(rev 31)* `ParameterMode_GuidsAndDates_AreBoundAsTheirLiteralText` under fi-FI and `ParameterMode_Char_IsText`, *(rev 32)* `ParameterMode_DuplicateTerm_SendsItsParametersOnce`, `ParameterMode_EachOccurrence_IsItsOwnParameter`, `ParameterMode_TermsDifferingOnlyInAValuesKind_AreBothKept`, `ParameterMode_TermKey_KeepsTermsWhoseValuesDiffer_WhateverTheirText` and PostgreSQL `ParameterMode_ValueComparedWithNull_IsDecidedHere`; `LiteralMode_ValueWithNullText_IsEmptyText`, *(rev 33)* `LiteralMode_PlaceholderShapedText_IsInlined`; MySQL `LiteralMode_Backslash_IsEscaped`; *(rev 39)* `ParameterMode_DateOnlyAndTimeOnly_AreIsoText` (seven-digit `TimeOnly` pin since rev 41; under fi-FI since rev 43, and th-TH since rev 44), `TernaryOrderBy_DateOnlyAndTimeOnlyBranchValues_MatchesOracle` (a sub-second assertion since rev 40), MySQL `TernaryOrderBy_DateTimeOffsetValue_PicksTheRowsWhereDoes`; *(rev 41)* `ParameterMode_TheElevenNumericTypes_StayInline_OtherNumbersAreParameters` (fi-FI). Harness: `CommandTexts()`, `AssertEveryParameterReferenced()` (rev 31: per log entry; self-test `AssertEveryParameterReferenced_ChecksEachParameterAgainstItsOwnCommand`) | all 4 |
| AC13-1 | `Single_Predicate_ReturnsTargetNotFirst`, `SingleOrDefault_Predicate_NoMatch_ReturnsNull`, `Single_NoMatch_Throws`, `Single_TwoMatches_Throws`, `SingleOrDefault_TwoMatches_Throws`, `Single_NoUserOrder_EmitsRowLimit_NoIdOrder` (SQL shape), `Single_OnEntityWithoutIdColumn_Works`, `Single_AfterDistinctProjection_Works`, `Single_AfterTake1_OverManyRows_ReturnsRow`, `Single_AfterSkipOnly_OverManyRows_Throws` (also asserts the cap in SQL: `FETCH NEXT 2 ROWS` / `LIMIT 2 OFFSET n`), `Single_AfterSkipTake_Parameterless_MatchesOracle`; *(rev 24)* `Single_AfterTakeGreaterThanTwo_ReadsTwoRows` (all 4) | all 4 |
| AC13-2 | `Last_Parameterless_Unordered_ReturnsMaxId`, `Last_ReadsOneRow_EmitsRowLimit` *(rev 21)*, `Last_AfterOrderByNonIdKey_ReturnsLastInOrder`, `Last_AfterOrderByThenByDescending_InvertsEveryTerm`, `Last_AfterRemoteOrderBy_ReturnsLastInOrder`, `Last_AfterTernaryOrderBy_InvertsCaseTerm`, `Last_AfterComputedOrderBy_InvertsComputedTerm` (scores 9/null/5: the expected row is the min id), `LastOrDefault_Predicate_WithExplicitOrderBy_MatchesOracle`, `Last_Empty_Throws`, `LastOrDefault_Empty_ReturnsNull`, `Last_EntityWithoutIdProperty_ThrowsExistingInvalidOperation`, `Last_AfterDistinctProjection_NoOrder_ThrowsNamingLast`, `Last_AfterDistinctProjection_WithProjectedOrder_Works`; existing PG `LastOrDefault(x => …guid…)` stays green; *(rev 22)* `LastFamily_AfterDistinctProjection_NoOrder_ThrowsNamingTerminal` (5 rows), `Last_NullableKey_EqualsTheProvidersOwnOrder`, `Last_EntityWithoutId_ScalarProjection_ScalarGuardWins`, `Last_EntityWithoutId_DistinctProjection_DistinctGuardWins` | all 4 |
| AC13-3 | `LongCount_EqualsCount_ReturnsInt64`, `LongCount_Predicate_EqualsCountPredicate`, `LongCount_FilteredByReverseRemoteKey_ThrowsNotSupported`; SQL Server only: `LongCount_EmitsCountBig`; *(rev 23)* `LongCount_PredicateOnForwardRemoteColumn_InjectsJoin`, `LongCount_PredicateOnReverseRemoteKey_ThrowsNotSupported` (all 4) | all 4 |
| AC13-4 | `[DataTestMethod] Rejected_Operator_ThrowsNotSupported_NamesOperator_NoQueryExecuted` (one row per AC13-4 allow-list shape, including `ScalarProjection_ParameterlessSum`; covariant shapes are in the invariant rows below — note `((IQueryable<object>)q).Take(5)` is now **allowed**, F2), `Allowed_PredicateWithCollectionContains_NotRejected` (`List<T>.Contains` and `Enumerable.Contains`), `Harness_LogObservesEveryExecutionPath` (positive control for "no query executed"), `NonQueryableSpineMethod_Rejected`, `NonCallNonRootSpineNode_Rejected` (DB-free, hand-built `Convert` node), `ForeignQueryableConstantRoot_Rejected` (DB-free: a constant whose value is a composed queryable)<br>**Covariance invariants (§5.2.1, rev 7).** Each row is spelled both ways (no-node `IQueryable<object> o = …` and `.Cast<object>()`) and, where marked ‡, also "after an allowed sequence operator". **‡ is defined per operator (rev 8):** after `Take`/`Skip`, only `First*`/`Single*` (D8 rejects the rest; those cells are D8 rows); after `Distinct`, only `First*`/`Single*`/`Last*` (`Count`/`Any` hit the existing Distinct+aggregate guard, r7 M1–M3, which is a regression row), **except `Last*` after `Distinct` at the subset-`Select` position**. AC13-2 makes that shape throw, naming `Last`, and it's covered by `Last_AfterDistinctProjection_NoOrder_ThrowsNamingLast` (r8 #9).<br>• **I1 (predicate lambdas):** `Covariant_PredicateLambda_Rejected_I1Message_NoQuery` over {`Where`, `First(pred)`, `FirstOrDefault(pred)`, `Single(pred)`, `SingleOrDefault(pred)`, `Last(pred)`, `LastOrDefault(pred)`, `Any(pred)`, `All`, `Count(pred)`, `LongCount(pred)`, subset-`Select`-then-`Where`, **`Distinct()`-then-`Where`**} × {`object` source, **base-class source** (`IQueryable<PersonEntity>` over `Query<PersonDetailEntity>()`; MySQL: `IQueryable<PersonBase>` over `Query<PersonWithEmployer>()`)}, asserting the I1 message. Red on 3.9.0: `InvalidCastException` for the hard-cast operators (I1a/b/d/e/f/g, J5/J6/J14/J15, N1/N2, S4–S7b); a **silently unrelated row** for `Single*(pred)` (S1–S3). The `Distinct` row separates a lambda-parameter check from a `TSource` check.<br>• **I1 out of scope, left alone:** `Covariant_BaseClassSource_NonPredicateLambda_UnchangedFromConcrete`, comparing each shape with **the same shape over the concrete, unconverted query** (not the in-memory oracle, so it's independent of the D9 aggregate issues; `Average` differs from in-memory LINQ today, r8 S10/r8-MY3). Over {`OrderBy`→First, `OrderByDescending`→First, `OrderBy.ThenByDescending.Skip(1).First()`, `Max`/`Min`/`Sum`/`Average`(selector), scalar `Select` enumerated} × base class (J1–J4, J9, J16–J18), and over {`OrderByDescending(x => x.Id).First()`, `Max(x => x.Id)`} × interface `IHasPersonId` (J19/J20; `Id` only, since `IHasPersonId` exposes only `Id`. Interface behavior for other members depends on column naming, §8), plus `o.OrderByDescending(x => ((PersonEntity)x).Id).First()` (J8). Green on 3.9.0 and must stay green.<br>• `Covariant_BaseClassSource_PagedEnumerated_MatchesOracle` (`b.Take(5).ToList()`; red on 3.9.0, N3; fixed by I2).<br>• `Covariant_ScalarSource_Lambda_KeepsCompositionMessage` over {`Where`, `Count(pred)`}: asserts the substring **"must be the outermost query operator"**, on both converted and non-converted shapes. Only predicate operators can kill "I1 applied outer to a scalar Select", because I1 doesn't cover `OrderBy`.<br>• **I2 (scalar terminals):** `Covariant_ScalarSource_Terminal_Rejected_NoQuery` ‡ over {First, FirstOrDefault, Single, SingleOrDefault, Last, LastOrDefault}, asserting the scalar-guard message. Red on 3.9.0: the whole list is returned (P6, P7, Q1a–e). `Covariant_ScalarSource_CountAny_Regression` (non-‡; **green** on 3.9.0 with the same message, P7f/Q9c/N4: a regression row, not red). `ScalarDistinctLast_ScalarGuardWins` (`s.Distinct().Last()`: the Last/Distinct check lives in `BuildQueryComponents`, which runs after `ScalarProjectionGuard`, so the scalar-guard message wins).<br>• **I2 (entity enumeration):** `Covariant_EntitySource_Enumerated_MatchesOracle` ‡ (P14, Q3d red).<br>• **Left alone:** `Covariant_EntitySource_ParameterlessTerminal_MatchesOracle` ‡ over {Count, LongCount, Any, First, FirstOrDefault, Single (pre-filtered to one row), Last}, at the root, after `Where`/`OrderBy`/subset `Select` (Q3, Q9, P5, P13); `Covariant_ScalarSource_SequenceOrEnumeration_MatchesOracle` over {`Skip.Take`, `Take`, `Distinct`, `Cast<object>()` enumerated} (P7b–e, Q8).<br>• **D8 interplay:** `Covariant_EntitySource_AfterPaging_D8Governs` over {`Skip(1)`→First: allowed; `Take(5)`→Count: D8 message; `Take(5)`→Where: D8 message (D8 precedes I1)}.<br>• `Covariant_OtherProjectedSource_SelectShapeGuardMessage`: `q.Select(p => new Dto { … }).Where(…)` gets the `Select`-shape message, not I1's (r7 L1).<br>**`ScalarProjectionGuard`**, DB-free and direct: `ScalarProjectionGuard_Terminal_Throws` (object, string, `First<object>(…)`), `ScalarProjectionGuard_Collection_Passes` (`IEnumerable<object>`, string, `Select(…)`). It's also pinned end to end by the I2 rows above.; *(rev 21, T4R-1)* `Covariant_PredicateLambda_Rejected_I1Message_NoQuery` gains `interface` rows and per-source advice asserts; `GenericHelper_ConstrainedToBaseClass_MatchesOracle` (the advised helper runs) | all 4 + Core |
| AC13-5 | `[DataTestMethod] Allowed_Operator_MatchesOracle` (one row per allowed family, with its expected outcome); the four existing suites | all 4 |
| AC13-6 | `OfType_Identity_AtRoot_IsNoOp`, `OfType_Identity_AfterOrderBy_IsNoOp`, `OfType_Identity_AfterScalarProjection_NonNullable_IsNoOp`, `OfType_Identity_OverNullableScalar_Rejected` (seeded nulls), `OfType_Identity_OverReferenceScalar_Rejected` (`OfType<string>`), `OfType_NonIdentity_Throws`, `Cast_Identity_IsNoOp` (`q.Cast<PersonEntity>().ToList()`; P1 shows it's a real node), `Cast_ReferenceConversion_Count_MatchesOracle` and `Cast_ReferenceConversion_OrderedFirst_MatchesOracle` (P5, P5b), `Cast_ReferenceConversion_Enumerated_MatchesOracle` (`q.Where(marker).Cast<object>().ToList()`; red on 3.9.0, P14), `Cast_ReferenceConversion_BaseClass_Enumerated_MatchesOracle` (per provider: `Query<PersonDetailEntity>().Cast<PersonEntity>()` on SQL Server/PG/SQLite, `Query<PersonWithEmployer>().Cast<PersonBase>()` on MySQL; red on 3.9.0, the Q4c/P14 class), `Cast_ReferenceConversion_Interface_Count_MatchesOracle` (`Cast<IHasPersonId>()` on all four, the Q4d shape; `IHasPersonId` is added to all four in Task 1), `Cast_BackToEntityAfterTransparentCast_IsNoOp` (Q4a), `OfType_Entity_AfterTransparentCast_IsNoOp` (Q4b), `Cast_Boxing_Rejected` (`Select(p => p.Id).Cast<object>()`), `Cast_NonIdentity_UnrelatedType_Throws` (`Cast<AddressEntity>()` on a person query) | all 4 + Core |
| AC13-7 | `SupportedOperators_ExactLiteralSetPinned`, `ClassifierSweep_EveryQueryableMethod_MatchesLiteralSet`, `NonQueryableOverload_IsRejected` (MSTest: `SqlServer.Tests` net8, `SqlServer.Tests.NetFramework` net48); xUnit twin `QueryOperatorPolicyLiteralSetTests` (`SqlServer.Tests.DotNet9` net9) | Core, 3 runtimes |
| AC13-8 | `GroupBy_Rejected_KeepsDedicatedMessage`; existing scalar tests; new in the siblings: `ScalarProjection_WithReducingTerminals_ThrowNotSupported`; `ScalarProjection_WithSingleOrLast_ThrowsNotSupported`; `Rejected_OperatorOuterToFailingInnerOperator_PolicyMessageWins` (`Select(p => p.Id).Where(x => x > 0).Reverse()`); `Rejected_AllowListFailureBeatsPass2Failure` (`q.Take(5).Where(w).Reverse()` → the `Reverse` message, not D8's); `Rejected_OutermostAllowListFailureWins` (`Reverse().TakeWhile(…)` → `TakeWhile`); `Rejected_Pass2InnerFailureWins` (`Cast<Unrelated>().Take(1).Count()` → the D5 message, not D8's). Every allow-list rejection row asserts the policy message, and the type-filter rows assert the D5/`OfType` text, so an older guard that merely names the operator can't satisfy them; *(rev 21, T4R-2)* `OtherSelect_ThenCastOrOfType_SelectShapeMessage` [Cast, OfType]; *(rev 22)* `SubsetSelect_ThenUnrelatedCastOrOfType_D5Message` [Cast, OfType] | all 4 |
| AC13-9 | `OperatorDocTable_MatchesSupportedOperators` (reads the table from both docs); prose reviewed in the gauntlet; *(rev 23)* the doc test rejects an operator listed twice and a data row that names none; *(rev 24)* `TableParser_AcceptsEverySeparatorForm`, `TableParser_WithoutSeparator_Fails` | SqlServer.Tests |
| AC13-10 | `[DataTestMethod] Operator_AfterPaging_Rejected_BeforeAnyQuery` over {Count, LongCount, Any, All, Sum, Average, Min, Max, Where, First(pred), Single(pred), Last, OrderBy, OrderByDescending, OrderBy(a).Skip(n).OrderBy(b) [D8 wins over D10], Distinct, Skip-after-Skip, Take-after-Take, Take-then-Skip} — each row asserts the "after Skip/Take" message; `[DataTestMethod] Operator_AfterPaging_Allowed_MatchesOracle` over {Skip.Take, `Skip.Select(subset).Take`, `Skip.Select(scalar).Take`, `Skip.OfType<T>().Take`, `Skip.Cast<T>().Take`, `Take.Cast<object>()` enumerated, `Skip.Take.Select(p => p.FirstName).Cast<object>()` enumerated (Q7), Select subset, Select scalar, First(), FirstOrDefault(), Single(), SingleOrDefault(), OfType-identity entity, `Skip(n).Select(p => p.Id).OfType<int>()`, Skip-only.First()}; `[DataTestMethod] TakeNonPositive_ReturnsEmpty_NoQuery` over {Take(0) full, Take(0) subset, Take(0) scalar, Skip(2).Take(0), Take(-1), `Take(0).Cast<object>()` enumerated}; `Take0_First_Throws_NoQuery`, `Take0_FirstOrDefault_ReturnsNull_NoQuery`, `Take0_Single_Throws_NoQuery`, `Take0_SingleOrDefault_ReturnsNull_NoQuery`, `[DataTestMethod] Take0_CastObject_Terminal_NoQuery` and `[DataTestMethod] Take0_ImplicitObject_Terminal_NoQuery` (`IQueryable<object> o = q.Take(0)`), each over {First throws, FirstOrDefault → null, Single throws, SingleOrDefault → null} (the converted entity source in both spellings; I2: not an empty list as "first"); `[DataTestMethod] Take0_SubsetProjection_Terminal_NoQuery` over {First throws, FirstOrDefault → null, Single throws, SingleOrDefault → null} (the entity path); `[DataTestMethod] ScalarProjection_Take0_Terminal_ThrowsScalarGuard_NoQuery` over {First, FirstOrDefault, Single, SingleOrDefault} (`ScalarProjectionGuard` wins over the empty short-circuit for every terminal); `SkipNegative_BehavesAsSkipZero` | all 4 |
| AC13-12 | `[DataTestMethod] Ordering_AfterEarlierOrdering_Rejected_BeforeAnyQuery` over {OrderBy.OrderBy, OrderBy.ThenBy.OrderByDescending, OrderBy.Where.OrderBy, OrderBy.Select(**subset**).OrderBy, OrderBy.Distinct.OrderBy}, each asserting the D10 message; `ThenBy_OnRoot_IsPrimaryOrder` (`((IOrderedQueryable<T>)q).ThenByDescending(p => p.Id).Where(marker)` returns rows in **descending** id order, the same as `OrderByDescending(p => p.Id).Where(marker)`; ascending would be indistinguishable from no order, r6 Q10); *(rev 21, T4R-6)* the same rows assert the second operator's name and the `query.{Op}(later).ThenBy(earlier)` advice; *(rev 22)* the rows also assert the advice keeps each earlier key's direction | all 4 |
| AC13-13 | `SqliteRoot_ReusedAfterProjection_BareRootNotNarrowed`, `SqliteRoot_ReusedAfterOrderedQuery_BareRootNoInheritedOrder`, `SqliteRoot_ReusedAfterOrderedQuery_ThenLast_UsesIdDesc`, `SqliteRoot_ReusedAfterParameterizedProjection_NoDuplicateParameters` | SQLite |
| AC13-14 | `SkipOnly_ToList_Executes`, `SkipOnly_First_ReturnsExpectedRow` (SQLite red; regression rows in the others); SQLite only: `SkipOnly_EmitsLimitMinusOneOffset` (SQL shape) | all 4 |
| AC13-15 | `NonGenericExecute_First_ReturnsSingleRow` (red on 3.9.0, K1: whole list), `NonGenericExecute_FirstOnEmpty_Throws` (asserts exactly `InvalidOperationException`, not wrapped), `NonGenericExecute_RejectedOperator_ThrowsNotSupported` (asserts exactly `NotSupportedException`, not `TargetInvocationException`), `NonGenericExecute_Count_ReturnsInt` (K4), `NonGenericExecute_Collection_Unchanged` (K2) | all 4 |

**Provider scope (Task 1, t1-L1).** On SQLite, 3.9.0's `ORDER BY id` is ambiguous even for the full join entity,
and `Skip` without `Take` is a syntax error. Some rows above are described as "green" or "correct" on 3.9.0:
`UnchangedFromConcrete` J8/J9 and interface `OrderByDescending`, `D8Governs[Skip(1)→First]`, the `afterSkip`
rows, and the Skip-only rows of `Operator_AfterPaging_Allowed_MatchesOracle`. Where those order by `Id` or use `Skip` alone, they're red on SQLite until Tasks 2–3. On the other three
providers they're green. SQLite's #13 rows that must execute order by `FirstName` instead of `Id`.

### 4.3 Interface coverage (new/changed members → tests)

| Member | Tests that call it on purpose |
|---|---|
| Core `QueryOperatorPolicy.EnsureSupported(Expression expression)` *(new, public, `void`)* | `QueryOperatorPolicyTests.*`; every `Rejected_*`/`Allowed_*`/`Operator_AfterPaging_*`/`Ordering_AfterEarlierOrdering_*`/`Covariant_*` row |
| Core `ScalarProjectionGuard.EnsureCollectionResult(Expression expression, Type resultType, Type memberType)` *(new, public; I2)* | `ScalarProjectionGuard_Terminal_Throws`, `ScalarProjectionGuard_Collection_Passes` (direct); end to end through `Covariant_ScalarSource_Terminal_Rejected_NoQuery` and every existing scalar-projection test |
| `*LinqQueryProvider.Execute(Expression)` (non-generic) — dispatch by expression shape *(I2, rev 8)* | `NonGenericExecute_First_ReturnsSingleRow`, `NonGenericExecute_FirstOnEmpty_Throws`, `NonGenericExecute_Collection_Unchanged` |
| `*LinqQueryProvider.Execute` — `isCollection` = `IQueryable` expression shape *(I2)* | `Cast_ReferenceConversion_Enumerated_MatchesOracle`, `Covariant_EntitySource_Enumerated_MatchesOracle`; all existing enumeration tests |
| Core `QueryOperatorPolicy.IsAllowed(MethodInfo)` *(new, **public** — no `InternalsVisibleTo` dependency, which would break if the assemblies are strong-named later)* | `ClassifierSweep_*`, `NonQueryableOverload_IsRejected`, the net9 xUnit twin |
| Core `QueryOperatorPolicy.SupportedOperators` *(new, public read-only)* | `SupportedOperators_ExactLiteralSetPinned`, `OperatorDocTable_MatchesSupportedOperators`, `SupportedOperators_IsReadOnlyView` *(rev 21, T4R-4)* |
| `*OrderByClauseVisitor` ctor — optional `tableQualifier` | AC12 tests; `OrderByVisitorDirectTests` |
| `*OrderByClauseVisitor` 3.9.0 constructor (kept) and the qualifier overload *(rev 24)* | `Constructor_390Signature_IsKept`; every provider call site |
| `*OrderByClauseVisitor.OrderByTerms` *(new)* + duplicate removal | AC13-2 inversion tests; `ThenBy_SameKeyTwice_Executes`; direct tests |
| `*OrderByClauseVisitor` ternary null handling | AC12-8; direct tests |
| `*OrderByClauseVisitor` constructor taking a parameter generator *(new, rev 30)* | `ParameterMode_*`; every LINQ ORDER BY path (the AC12-10 oracle rows) |
| `*OrderByClauseVisitor.Parameters` *(new, rev 30)* | `ParameterMode_*`; `TextTernaryOrderings_SendOnlyTheParametersTheCommandUses` |
| `QueryComponents.OrderByParameters` *(new, rev 30; all four providers)* | `Count_AfterTextTernaryOrderBy_Works`, `ScalarProjection_AfterTextTernaryOrderBy_BindsTheParameters`, `TextTernaryOrderings_SendOnlyTheParametersTheCommandUses` |
| `QueryComponents.Terminal`, `.RowLimit`, `.OrderByTerms`, `.IsEmptyByTake` *(new)* | AC13-1, AC13-2, AC13-10 |
| `*LinqQueryProvider.ParseExpression` (pre-pass call; Single/Last/LongCount branches; negative `Skip` clamp; empty-`Take` flag) | AC13-* |
| `*LinqQueryProvider.Execute` / `ExecuteScalarProjection` (empty-`Take` short-circuit) | AC13-10 `Take0_*`, `TakeNonPositive_*`, `ScalarProjection_Take0_Terminal_*` |
| `*LinqQueryProvider.BuildQueryComponents` (row limit; SQLite `rowid` qualification; SQLite `LIMIT -1 OFFSET`) | AC13-1, AC12-7, AC13-14 |
| `*LinqQueryProvider.ExecuteQuery` (cardinality) | AC13-1 |
| `*LinqQueryProvider.BuildAggregateClause` / `HandleAggregateQuery` (`LongCount` only) | AC13-3 |
| `SqliteLinqQueryProvider` state reset | AC13-13 |

### 4.4 Mutations each key test must kill (run them; record the result in the handoff)

| Mutation | Test that must fail |
|---|---|
| Revert `ResolveOrderColumn` to bare `GetColumnName` | AC12-1 subset-without-key and scalar rows (runtime ambiguous column) |
| Apply the qualifier in the map-hit branch too | `RemoteMemberOrderBy_EmitsExactResolvedFragment_NoBasePrefix` |
| Qualify unconditionally | `SingleTableEntity_OrderBy_SqlByteIdenticalTo390` |
| Don't pass the qualifier at the `Last*` site | `Last_OnJoinEntity_ProjectionWithoutKey_SynthesizedOrderQualified` (runtime + SQL text) |
| SQLite default order back to bare `rowid` | `DefaultPaging_OnJoinEntity_Executes` |
| SQLite `OFFSET` without `LIMIT` | `SkipOnly_ToList_Executes` (SQLite) |
| Ternary null test back to `= NULL` / only one operand order handled | `TernaryOrderBy_NullComparison_MatchesOracle` rows |
| No duplicate-key removal | `ThenBy_SameKeyTwice_Executes` (SQL Server; red on 3.9.0, §1.5 P8) |
| `Single*`: drop predicate→WHERE | `Single_Predicate_ReturnsTargetNotFirst` |
| `Single*`: limit 1 instead of 2 | `Single_TwoMatches_Throws`, `SingleOrDefault_TwoMatches_Throws` |
| `Single*`: ignore a user `Take(1)` | `Single_AfterTake1_OverManyRows_ReturnsRow` (`Take ?? 2` and `min(Take, 2)` are equivalent for `Take ≥ 2`; noted) |
| `Single*`: no cap after `Skip`-only | `Single_AfterSkipOnly_OverManyRows_Throws` via its SQL-shape assert (`FETCH NEXT 2 ROWS` / `LIMIT 2 OFFSET n`). The throw alone can't kill it. |
| `Single*`: route through the paging path (injects `ORDER BY id`) | `Single_NoUserOrder_EmitsRowLimit_NoIdOrder` (asserts **no `ORDER BY` at all**: provider-neutral, and the SQL-shape killer on SQLite, where the default is `ORDER BY rowid` and runs on any rowid table, r8 #7/r10-L1), `Single_OnEntityWithoutIdColumn_Works` (kills on SQL Server/PG/MySQL: no `id` column), `Single_AfterDistinctProjection_Works` (all 4) |
| `SingleOrDefault` throws on empty | `SingleOrDefault_Predicate_NoMatch_ReturnsNull` |
| `Last*`: don't invert / ignore explicit order | `Last_AfterOrderByNonIdKey_ReturnsLastInOrder` |
| `Last*`: invert only the first term | `Last_AfterOrderByThenByDescending_InvertsEveryTerm` |
| `Last*`: invert own-column terms only | `Last_AfterTernaryOrderBy_InvertsCaseTerm`, `Last_AfterRemoteOrderBy_ReturnsLastInOrder` |
| `LongCount` missing from the `OuterMethodCall` list (returns `0L`) | `LongCount_EqualsCount_ReturnsInt64` |
| `LongCount` boxed as `Int32` / `COUNT(*)` on SQL Server | `LongCount_EqualsCount_ReturnsInt64` / `LongCount_EmitsCountBig` |
| Add `Reverse` to the allow-list | `Rejected_…[Reverse]`, `SupportedOperators_ExactLiteralSetPinned` |
| Match by name instead of overload | `Rejected_…[IndexedWhere]`, `[OrderByWithComparer]`, `[DistinctWithComparer]`, `[TakeRange]` |
| `GetGenericMethodDefinition()` without `IsGenericMethod` | `NonQueryableOverload_IsRejected`, `Rejected_…[ScalarProjection_ParameterlessSum]` |
| I1 removed (predicate lambda parameter type not checked) | `Covariant_PredicateLambda_Rejected_I1Message_NoQuery` rows (`InvalidCastException`, not the I1 message) |
| I1 applied to **every** lambda operator (the rev 7 strict rule) | `Covariant_BaseClassSource_NonPredicateLambda_UnchangedFromConcrete` rows (the base-class and `Id`-interface shapes would be rejected) |
| Non-generic `Execute` lets `TargetInvocationException` escape | `NonGenericExecute_FirstOnEmpty_Throws`, `NonGenericExecute_RejectedOperator_ThrowsNotSupported` (assert the exact exception type) |
| I1 omits one hard-cast operator (e.g. `LongCount(pred)`, `All`, `Single(pred)`) | that operator's `Covariant_PredicateLambda_Rejected_I1Message_NoQuery` row (`InvalidCastException`) |
| I1 applied outer to a scalar `Select` too (masks the composition message) | `Covariant_ScalarSource_Lambda_KeepsCompositionMessage` (asserts "must be the outermost query operator"; the existing `ScalarProjection_ThenComposingLambdaOperators_*` tests only assert the operator name, which the I1 message also contains, so they can't kill this) |
| I1 applied outer to a non-subset (DTO) `Select` (masks the `Select`-shape message) | `Covariant_OtherProjectedSource_SelectShapeGuardMessage` |
| Non-generic `Execute(Expression)` still pins `IEnumerable<T>` | `NonGenericExecute_First_ReturnsSingleRow` (whole list, or `InvalidCastException` under I2-only) |
| I1 compares `TSource` with the source element type instead of checking the lambda parameter type (the rev 3–6 approach) | `Covariant_PredicateLambda_Rejected_I1Message_NoQuery[o.Distinct().Where(…)]`: after a covariant `Distinct<object>`, `TSource == source` (`object`), so a `TSource` check lets it through to the `InvalidCastException` (r6 Q2 mechanism; executed N1/N2). Not `Take`/`Skip`, where D8 rejects first. |
| `ScalarProjectionGuard` decides by `TResult` assignability (not expression shape) | `Covariant_ScalarSource_Terminal_Rejected_NoQuery[First/Single/Last, both spellings, ‡]` (whole list returned, Q1a–e/P6/P7) and `ScalarProjectionGuard_Terminal_Throws` |
| `ScalarProjectionGuard` call removed or bypassed in one provider (call-site mutation) | that provider's `Covariant_ScalarSource_Terminal_Rejected_NoQuery` rows (end to end) |
| Guard rejects valid enumeration | `ScalarProjectionGuard_Collection_Passes`, `Covariant_ScalarSource_SequenceOrEnumeration_MatchesOracle` |
| Lambda-free sequence operators over a converted source rejected | `Covariant_ScalarSource_SequenceOrEnumeration_MatchesOracle`, `Covariant_EntitySource_ParameterlessTerminal_MatchesOracle[‡]` |
| I1 evaluated before D8 | `Covariant_EntitySource_AfterPaging_D8Governs[Take(5)→Where]` (expects the D8 message) |
| `R` changes at a `Cast` (D5/I3 judged against the cast type) | `Cast_BackToEntityAfterTransparentCast_IsNoOp`, `OfType_Entity_AfterTransparentCast_IsNoOp` (rejected as unrelated/non-identity) |
| Reference-conversion `Cast` limited to `object` | `Cast_ReferenceConversion_Interface_Count_MatchesOracle`, `Cast_ReferenceConversion_BaseClass_Enumerated_MatchesOracle` |
| Treat any non-call node as the root | `NonCallNonRootSpineNode_Rejected` |
| Accept any `IQueryable` constant as the root (not only the queryable's own root) | `ForeignQueryableConstantRoot_Rejected` |
| `isCollection` unchanged (still `IEnumerable<T>` only) | `Cast_ReferenceConversion_Enumerated_MatchesOracle`, `Covariant_EntitySource_Enumerated_MatchesOracle` (`InvalidCastException`, P14) |
| Empty-`Take` short-circuit decides by `TResult` assignability | `Take0_CastObject_Terminal_NoQuery[First]`, `Take0_ImplicitObject_Terminal_NoQuery[First]` (empty list returned as "first") |
| Pass-2 failure checked before the whole-spine allow-list | `Rejected_AllowListFailureBeatsPass2Failure` |
| Policy visits the whole tree (incl. `Enumerable` calls nested in lambdas) | `Allowed_PredicateWithCollectionContains_NotRejected` (both rows) |
| Pass 1 reports the innermost failure | `Rejected_OutermostAllowListFailureWins` |
| Pass 2 walks outer→inner | `Rejected_Pass2InnerFailureWins` |
| `Last*` builds its inverted ORDER BY without the table qualifier | `Last_AfterRemoteOrderBy_ReturnsLastInOrder` (asserts `{table}.id DESC`) |
| `Last*` falls back to `Id DESC` despite explicit terms | `Last_AfterRemoteOrderBy_ReturnsLastInOrder`, `LastOrDefault_Predicate_WithExplicitOrderBy_MatchesOracle`, `Allowed_Operator_MatchesOracle[Last*]` (expected rows aren't the max id) |
| `Last*` inverts by splitting the ORDER BY text on commas | `Last_AfterComputedOrderBy_InvertsComputedTerm` (`COALESCE(project.score, 0)` inverted whole) |
| No default `Id DESC` for unordered `Last` (masked by heap order on PostgreSQL) | `Last_Parameterless_Unordered_ReturnsMaxId` (SQL assert) |
| A new execution path skips `Log` ("no query" asserts turn vacuous) | `Harness_LogObservesEveryExecutionPath` (incl. the `Single*` row-limit, `Last*` and `LongCount` paths) |
| `Last*` drops the ORDER BY (takes the first matching row) | `LastOrDefault_Predicate_WithExplicitOrderBy_MatchesOracle`, `Last_AfterOrderByNonIdKey_ReturnsLastInOrder`, `Allowed_Operator_MatchesOracle[Last*]` (each asserts the inverted ORDER BY; a seed alone can't rule it out) |
| `Last*` without the row limit (reads every row, keeps the first: same entity) *(rev 21)* | `Last_ReadsOneRow_EmitsRowLimit` (SQL assert) |
| I1 advice suggests a generic constraint for an interface or `object` source *(rev 21)* | `Covariant_PredicateLambda_Rejected_I1Message_NoQuery[interface/object rows]` |
| I1 advice never suggests the generic helper *(rev 21)* | `Covariant_PredicateLambda_Rejected_I1Message_NoQuery[base rows]` |
| `Cast`/`OfType` after an unsupported `Select` judged against the stale row type; or D5/I3 also suspended after a scalar `Select` *(rev 21)* | `OtherSelect_ThenCastOrOfType_SelectShapeMessage`; `OfType_Identity_OverNullableScalar_Rejected`, `OfType_Identity_OverReferenceScalar_Rejected`, `Cast_Boxing_Rejected` |
| `SupportedOperators` returns the backing array *(rev 21)* | `SupportedOperators_IsReadOnlyView` |
| D10 message names `OrderBy` for every operator, or advises adding a key to the existing order *(rev 21)* | `Ordering_AfterEarlierOrdering_Rejected_BeforeAnyQuery` |
| A null operand recognized only as a literal (a captured null emits `= NULL`) *(rev 22)* | `TernaryOrderBy_NullComparison_MatchesOracle[captured rows]`, `Ternary_Branch_BuildsCase[captured null …]` |
| Any parameter-free operand treated as null *(rev 22)* | `Ternary_Branch_BuildsCase[captured value]` |
| A subset `Select` also suspends D5/I3 *(rev 22)* | `SubsetSelect_ThenUnrelatedCastOrOfType_D5Message` |
| The `Distinct` guard checks only `Last`, or blames `Id DESC` *(rev 22)* | `LastFamily_AfterDistinctProjection_NoOrder_ThrowsNamingTerminal` |
| Default `Last` order resolved before the scalar/`Distinct` guards *(rev 22)* | `Last_EntityWithoutId_ScalarProjection_ScalarGuardWins`, `Last_EntityWithoutId_DistinctProjection_DistinctGuardWins` |
| D10 advice drops the earlier key's direction *(rev 22)* | `Ordering_AfterEarlierOrdering_Rejected_BeforeAnyQuery` |
| `LongCount`'s own predicate left out of the aggregate's join resolution *(rev 23)* | `LongCount_PredicateOnForwardRemoteColumn_InjectsJoin`, `LongCount_PredicateOnReverseRemoteKey_ThrowsNotSupported` |
| Doc table lists an operator twice, or has a row naming none *(rev 23)* | `OperatorDocTable_MatchesSupportedOperators` |
| `Single*` after user paging reads `Take` rows when `Take > 2` *(rev 24)* | `Single_AfterTakeGreaterThanTwo_ReadsTwoRows` |
| A duplicate `CASE` key not dropped *(rev 24)* | `ThenBy_SameTernaryKeyTwice_Executes` (SQL Server error 169) |
| Own columns qualified whenever the resolution map isn't empty (instead of when there are joins) *(rev 24)* | `ComputedAttributeEntityWithoutJoins_OwnColumnOrder_Unqualified` |
| The null check flags a parameter that a nested lambda declares *(rev 24)* | `TernaryOrderBy_NullComparison_MatchesOracle[nested lambda]`, `Ternary_Branch_BuildsCase[nested lambda]` |
| A value operand evaluated more than once *(rev 24)* | `TernaryOperand_EvaluatedOnce_NullCheckAndSqlAgree` |
| The 3.9.0 visitor constructor removed or qualifying *(rev 24)* | `Constructor_390Signature_IsKept` (removal also breaks the build: internal callers use it) |
| The doc-table parser misses a valid separator form *(rev 24)* | `TableParser_AcceptsEverySeparatorForm` |
| Operand evaluated without unwrapping `Convert` (a captured `char` as its code point) *(rev 25)* | `CapturedCharAndEnum_FormatAsTheirValues` |
| An evaluation failure retried through `BuildValueSql` *(rev 25)* | `TernaryOperand_ThatThrows_EvaluatedOnce_Rejected` |
| Block variables treated as free parameters *(rev 25)* | `BlockOperand_DeclaredVariable_DoesNotReadTheRow` |
| Enum constants formatted by name *(rev 25)* | `CapturedCharAndEnum_FormatAsTheirValues` |
| `ConvertChecked` read through (a checked `(int)2.7` emitted as `2.7`) *(rev 26)* | `CheckedConversion_IsEvaluated_NotUnwrapped` |
| Branch values via `BuildValueSql` (a captured instance property `NULL` in a branch) *(rev 26)* | `CapturedInstanceProperty_SameValueInTestAndBranch` |
| Catch variables treated as free *(rev 26)* | `BlockOperand_DeclaredVariable_DoesNotReadTheRow` (try/catch operand) |
| Throw-path message text changed *(rev 26)* | `TernaryOperand_ThatThrows_KeepsThe390Message` |
| Enums formatted through `Int32` (overflows for a `long` enum) *(rev 26)* | `CapturedCharAndEnum_FormatAsTheirValues` (`byte`/`long` enums) |
| The ELSE branch value via `BuildValueSql` *(rev 27)* | `CapturedInstanceProperty_SameValueInTestAndBranch` (ELSE row) |
| ORDER BY text values inlined (no generator / always literal) *(rev 30)* | `TernaryOrderBy_TextWithQuotesOrBackslashes_IsAParameter_MatchesOracle` (MySQL: wrong rows; all: SQL-text assert), `ParameterMode_*`, `Last_AfterTextTernaryOrderBy_…` |
| Duplicate terms not dropped *(rev 30; by term key since rev 32)* | `ParameterMode_DuplicateTerm_SendsItsParametersOnce`, `ThenBy_SameTernaryKeyTwice_Executes` |
| Numbers parameterized too *(rev 30)* | `ParameterMode_TextBranchValues_AreParameters_NumbersAndNullStayInline` |
| ORDER BY parameters not bound to the command *(rev 30)* | the AC12-10 oracle rows |
| ORDER BY parameters bound to aggregates; earlier visitors' parameters kept *(rev 30)* | `Count_AfterTextTernaryOrderBy_Works`, `TextTernaryOrderings_SendOnlyTheParametersTheCommandUses` (`AssertEveryParameterReferenced`) |
| Values bound raw and typed by the generator (the e104aad binding) *(rev 31)* | SQL Server `TernaryOrderBy_GuidBranchValues_…`, `…DateTimeOffsetBranchValues_…`; PostgreSQL `TernaryOrderBy_DateTimeValue_…` (UTC rows), `…DateTimeOffsetBranchValues_…`; SQLite `TernaryOrderBy_GuidValue_…`, `TernaryOrderBy_DateTimeValue_…`; MySQL: the direct tests only (its database rows pass with raw values too) |
| SQL Server: Guids and dates `nvarchar` / strings `varchar` *(rev 31)* | `ParameterMode_GuidsAndDates_AreBoundAsTheirLiteralText` / `TernaryOrderBy_NonAsciiText_MatchesOracle`, `ParameterMode_Char_IsText` |
| PostgreSQL: values typed by the generator (not `Unknown`) *(rev 31)* | 9 of 130 failed, e.g. `TernaryOrderBy_DateTimeValue_…` (`==`, whole second, unspecified kind) |
| Equal values share a parameter again (rev 31's binding) *(rev 32)* | PostgreSQL: the N1/N2 oracle rows; all: `ParameterMode_EachOccurrence_IsItsOwnParameter` |
| Term key without the value's kind / without its length prefix / without values *(rev 32)* | `ParameterMode_TermsDifferingOnlyInAValuesKind_AreBothKept` / `ParameterMode_TermKey_KeepsTermsWhoseValuesDiffer_WhateverTheirText` / `TextTernaryOrderings_SendOnlyTheParametersTheCommandUses` |
| A dropped term's values bound anyway *(rev 32)* | `ParameterMode_DuplicateTerm_SendsItsParametersOnce` |
| PostgreSQL: a value's null test sent as `IS NULL` / every null test decided in .NET *(rev 32)* | `TernaryOrderBy_CapturedValueComparedWithNull_MatchesOracle` / the AC12-8 rows |
| SQL Server: dates bound as `datetime2` *(rev 32)* | `TernaryOrderBy_DateTimeValue_OnALegacyDatetimeColumn_ComparesAs390Did` |
| Date text in the current culture; `DateTimeOffset` as `o` *(rev 31)* | `ParameterMode_GuidsAndDates_AreBoundAsTheirLiteralText` (fi-FI) |
| Literal mode drifts from `LiteralText` (3.9.0's `ToString()` default) *(rev 31)* | the literal-parity assert in `ParameterMode_GuidsAndDates_…` |
| A value whose text is null not emptied *(rev 31)* | `LiteralMode_ValueWithNullText_IsEmptyText` |
| Placeholders substituted without a parameter generator (key, SQL or both) *(rev 33)* | `LiteralMode_PlaceholderShapedText_IsInlined` |
| `DateOnly`/`TimeOnly` in the current culture's format (3.9.0's `ToString()`) *(rev 39)* | `ParameterMode_DateOnlyAndTimeOnly_AreIsoText`, `TernaryOrderBy_DateOnlyAndTimeOnlyBranchValues_MatchesOracle` |
| `TimeOnly` with fewer fraction digits (`HH:mm:ss`, `.F`, `.FFF`, `.FFFFFF`) *(rev 40–41)* | `ParameterMode_DateOnlyAndTimeOnly_AreIsoText` (`10:00:30.1234567`); `HH:mm:ss` also the sub-second oracle assertion |
| MySQL: a `DateTimeOffset` not as its UTC time, or with fewer digits *(rev 39)* | MySQL `ParameterMode_GuidsAndDates_AreBoundAsTheirLiteralText`; the UTC case also `TernaryOrderBy_DateTimeOffsetValue_PicksTheRowsWhereDoes` |
| `IsNumber` without one of the eleven types, or with `nint`/`nuint`/`Half` *(rev 41)* | `ParameterMode_TheElevenNumericTypes_StayInline_OtherNumbersAreParameters` |
| `DateOnly`/`TimeOnly` with the current culture as the format provider (format kept) *(rev 44)* | `ParameterMode_DateOnlyAndTimeOnly_AreIsoText`: th-TH kills `DateOnly`'s, fi-FI kills `TimeOnly`'s |
| An inline number, or `LiteralText`'s default, in the current culture *(rev 41)* | `ParameterMode_TheElevenNumericTypes_StayInline_OtherNumbersAreParameters` (fi-FI) |
| `AssertEveryParameterReferenced` parsing the joined log by line (the rev 30 helper) *(rev 31)* | `AssertEveryParameterReferenced_ChecksEachParameterAgainstItsOwnCommand` |
| MySQL literal mode without backslash escaping *(rev 30)* | `LiteralMode_Backslash_IsEscaped` |
| Allow non-`Queryable` spine methods | `NonQueryableSpineMethod_Rejected` |
| Classifier allow-by-default | `ClassifierSweep_EveryQueryableMethod_MatchesLiteralSet` |
| Per-TFM computed expectation instead of the literal set | `SupportedOperators_ExactLiteralSetPinned` (literal count/signatures) |
| Policy inside the loop instead of a pre-pass | `Rejected_OperatorOuterToFailingInnerOperator_PolicyMessageWins` |
| Drop the `GroupBy` message | `GroupBy_Rejected_KeepsDedicatedMessage` |
| Identity check against the entity type `T` instead of the row type `R` | `OfType_Identity_AfterScalarProjection_NonNullable_IsNoOp` |
| Identity `OfType` allowed over a nullable scalar | `OfType_Identity_OverNullableScalar_Rejected` |
| Reject all `Cast`/`OfType` | `OfType_Identity_AtRoot_IsNoOp`, `Cast_Identity_IsNoOp` |
| Reject reference-conversion `Cast` (D5 before the owner's amendment) | `Cast_ReferenceConversion_Count_MatchesOracle`, `Cast_ReferenceConversion_OrderedFirst_MatchesOracle` |
| Allow any `Cast`/`OfType` | `OfType_NonIdentity_Throws`, `Cast_NonIdentity_UnrelatedType_Throws`, `Cast_Boxing_Rejected` (asserts the D5 text; the `ScalarProjectionGuard` backstop that would then catch the boxing `Cast` has a different message) |
| I3 drops "`R` is a reference type" (boxing `Cast` allowed) | `Cast_Boxing_Rejected` (D5 text asserted) |
| I3 rejects only `Nullable<>` in `OfType` (a reference scalar passes) | `OfType_Identity_OverReferenceScalar_Rejected` |
| Drop the positional guard | `Operator_AfterPaging_Rejected_BeforeAnyQuery` rows |
| Positional guard rejects `Skip.Take` or `Skip`-only `First` | `Operator_AfterPaging_Allowed_MatchesOracle` rows |
| `Take(0)` sent to the DB | `TakeNonPositive_ReturnsEmpty_NoQuery` rows |
| Empty-`Take` short-circuit only on the entity path | `TakeNonPositive_…[Take(0) scalar]` |
| Empty-`Take` short-circuit placed before the scalar dispatch (bypasses the result-type guard), including one that returns `default` for `*OrDefault` before the guard | `ScalarProjection_Take0_Terminal_ThrowsScalarGuard_NoQuery` (all four rows; the `*OrDefault` rows kill the `default` variant) |
| No negative-`Skip` clamp | `SkipNegative_BehavesAsSkipZero` |
| Allow a second `OrderBy` / only check adjacent calls | `Ordering_AfterEarlierOrdering_Rejected_BeforeAnyQuery` rows (incl. across `Where`/`Select`/`Distinct`) |
| D10 evaluated before D8 at the same node | `Operator_AfterPaging_Rejected_…[OrderBy(a).Skip(n).OrderBy(b)]` (expects the D8 message) |
| D8 rejects `Take` after `Skip` even with only `Select`/D5 conversions between | `Operator_AfterPaging_Allowed_…[Skip.Select(subset).Take]`, `[Skip.Select(scalar).Take]`, `[Skip.OfType<T>().Take]`, `[Skip.Cast<T>().Take]` |
| Policy rejects a `ThenBy*` directly on the root | `ThenBy_OnRoot_IsPrimaryOrder` |
| Root-level `ThenBy*` dropped by the parse loop or visitor (e.g. `OrderByTerms` seeded only at `OrderBy`) | `ThenBy_OnRoot_IsPrimaryOrder` (`ThenByDescending`: ascending would mask it, Q10) |
| SQLite reset placed after the early return | `SqliteRoot_ReusedAfterProjection_BareRootNotNarrowed`, `SqliteRoot_ReusedAfterOrderedQuery_BareRootNoInheritedOrder` |
| SQLite reset omits `OrderByTerms` | `SqliteRoot_ReusedAfterOrderedQuery_ThenLast_UsesIdDesc` |

### 4.5 Coverage (coverlet already referenced in all four test projects)

Baseline measured 2026-09-30 (cobertura `line-rate`):

| Touched file | SQL Server | SQLite | MySQL | PostgreSQL |
|---|---|---|---|---|
| `*LinqQueryProvider.cs` | 87.4% | 89.6% | **82.0%** | 88.6% |
| `*OrderByClauseVisitor.cs` | **58.2%** | **59.1%** | **35.6%** | **35.6%** |
| `QueryComponents.cs` / `*QueryComponents.cs` | 100% / 84.2% | 100% / 87.5% | 100% / 100% | 100% / 100% |
| Core `QueryOperatorPolicy.cs` | new | | | |

All touched files must reach **≥ 85%**. Visitor branches that can't be reached through `ParseExpression` are
covered by the DB-free direct visitor tests. Per-file numbers go into the PR.

### 4.6 Opt-in suites and frameworks

- **SQL Server:** `FUNKY_CONNECTION`. **MySQL:** `FUNKY_MYSQL_CONNECTION`. Both are set locally.
- **SQLite:** file-backed.
- **PostgreSQL:** the fallback connection reaches the native PostgreSQL 18 service. CI runs `postgres:17`
  only on push to `development/**`.
- **net48:** `QueryOperatorPolicyTests` (linked, MSTest) and the existing suite run locally in
  `Funcular.Data.Orm.SqlServer.Tests.NetFramework`, the net48 project in `FunkyORM.sln`.
  `Funcular.Data.Orm.NetFramework.Tests` is a separate, older project and isn't in the gate.
- **net9:** `SqlServer.Tests.DotNet9` is an xUnit project (global `using Xunit`, nullable on) containing only
  `ContainsTests`. It gets the xUnit twin `QueryOperatorPolicyLiteralSetTests`. The existing MSTest suite
  isn't run there.
- The CI net48 job is commented out (ci.yml:89-93), so these local runs are the gate; results go in the PR.

---

## 5. Design

### 5.1 #12 — qualify own columns in ORDER BY when joins exist

1. **Visitor.** Each provider's order-by visitor gains an optional `string tableQualifier = null`.
   `ResolveOrderColumn` resolves:
   - map hit → resolved fragment, unchanged and never prefixed;
   - otherwise, if `tableQualifier != null` → `{tableQualifier}.{column}`;
   - otherwise → the bare column.
2. **Call sites.** Both sites resolve `ResolveRemoteJoins<T>(table)` once and pass the map, plus
   `tableQualifier = IndividualJoinClauses?.Count > 0 ? table : null`.
3. **SQLite default order.** SQLite's default `ORDER BY rowid` becomes `ORDER BY {table}.rowid` when the
   command has joins.
4. **D11 ternary.** A comparison between a member and `null`, in either operand order, emits `{col} IS NULL` /
   `IS NOT NULL`.
5. **Duplicate keys.** A later term whose fragment equals an earlier term's is dropped (AC12-9).

### 5.2 #13 — Core pre-pass, then correct `Single*`/`Last*`/`LongCount`

1. **Core `QueryOperatorPolicy`** (`Funcular.Data.Orm.Core/Linq/QueryOperatorPolicy.cs`).
   - **Spine walk, two passes.**
     - **Pass 1 (outer→inner):** follow `MethodCallExpression.Arguments[0]` to the root, classifying each node
       with the allow-list **before** reading its `Arguments[0]`.
       - **Terminal:** a `ConstantExpression` whose value is an `IQueryable` **whose `Expression` is that same
         constant**, i.e. the queryable's own root. A constant wrapping a composed queryable would otherwise
         silently drop that queryable's operators.
       - Any other non-call node → reject.
       - Lambdas and other arguments are never visited.
     - **Pass 2 (inner→outer):** at each node, apply D8, D10, D5 and I1 in that order. Pass 2 tracks two
       things:
       - the root element type `T` (known from pass 1);
       - the **row type** `R`.
   - **Element type** of a node = the `T` in the `IEnumerable<T>` interface of its `Type`. That works for
     `SqlQueryable<T>`, `IQueryable<T>` and `IOrderedQueryable<T>` alike.
   - **Row type `R` (rev 7; replaces rev 6's "effective element type").** `R` is what the provider actually
     materializes per row. It starts as `T`, becomes the member type at a scalar `Select(x => x.M)`, and
     changes **nowhere else**. A subset `Select(x => new T { … })` keeps `T`; any other `Select` body is
     rejected by the parse loop's `Select`-shape guard. Conversions (the no-node `IQueryable<TBase>`, a
     `Cast`) and sequence operators never change `R`. So `R` can't be lost along the chain, which was the
     failure class of rounds r3–r6.
   - **Classification.** `IsAllowed(MethodInfo m)` uses the key
     `m.IsGenericMethod ? m.GetGenericMethodDefinition() : m`. It rejects non-`Queryable` declaring types, and
     keys outside a static allowed set built from `typeof(Queryable).GetMethods()` by name plus shape:
     - one `Expression<Func<TSource, …>>` lambda;
     - `Skip`/`Take` take `int`;
     - no comparer, default-value or `Range` parameters.

     Non-generic overloads are never in the set.
   - **Covariance: three invariants (rev 7 redesign, after four consecutive rounds of fix-introduced
     findings on the rev 3–6 cell table).** Each invariant is local and independent of how a conversion was
     spelled, so there's no type-flow to lose.
     - **I1 — predicate lambdas bind to the entity (pre-pass). Narrowed in rev 8 by the owner: reject
       only where 3.9.0 fails.**
       - **Scope:** the predicate operators, i.e. those whose predicate the translator hard-casts to
         `Expression<Func<T,…>>`. Those are `Where`; `First*`/`Last*` with a predicate; and
         `Any`/`All`/`Count` with a predicate (SqlLinqQueryProvider.cs:223/:261/:475; siblings
         PG :173/:199/:361, MySql :173/:199/:363, Sqlite :175/:200/:376). `Single*`/`LongCount` with a
         predicate are in scope too: 3.9.0 doesn't match them at all, and Tasks 5/8 route them through the
         same hard-cast branches.
       - For these, `lambda.Parameters[0].Type` must be `T`; otherwise they're rejected with the I1 message.
       - *Evidence:* every such shape over a converted source fails in 3.9.0. It either throws
         `InvalidCastException` (§1.5 I1a/b/d/e/f/g, r7 J5/J6/J14/J15, N1/N2, r8 S4–S7b) or, for
         `Single*(pred)`, silently returns an unrelated row (r8 S1–S3).
       - **Out of scope, and left alone:** `OrderBy*`/`ThenBy*`, selector aggregates (`Sum`/`Min`/`Max`/
         `Average`) and `Select`. They resolve by member or parameter generically.
         - Over a **base-class** source they return the same results as the concrete query in 3.9.0 (r7
           J1–J4, J7, J9, J10, J12, J13, J16–J18, e.g.
           `IQueryable<PersonEntity> b = db.Query<PersonDetailEntity>(); b.OrderBy(…)`).
         - Over an **interface** source they behave as in 3.9.0, where a member doesn't resolve to the
           entity's mapped column (pre-existing, §8). Only `Id` is relied on (S9c, r9-I2, r10-S1, r13-L1).
         - Otherwise they already fail cleanly (I1c's visitor message, I1h's `Select`-shape message).
         - I1 doesn't change any of this.
       - **Exception:** operators outer to any **non-subset `Select`** are left to the parse loop. After a
         scalar `Select`, the composition guard rejects every lambda with its existing message. After any
         other non-subset `Select`, the `Select`-shape guard rejects the `Select` itself. The pre-pass uses
         the parse loop's exact predicates:
         - **scalar:** body is a `MemberExpression` on the lambda's `ParameterExpression`, a `PropertyInfo`,
           `CanWrite` (SqlLinqQueryProvider.cs ~364-374);
         - **subset:** body is a `MemberInitExpression` of type `T`;
         - **anything else** is "other".
     - **I2 — collection vs single row is decided from the expression, not `TResult` (execution).**
       "Collection" means `typeof(IQueryable).IsAssignableFrom(expression.Type)`; otherwise it's a
       **terminal**. Used in three places:
       - in `Execute`, replacing `isCollection` (fixes P14 and the r6 F3 cases);
       - in the empty-`Take` short-circuit;
       - in `ScalarProjectionGuard`, where a scalar row type (`R` ≠ `T`) supports **collections only**, so
         **every terminal is rejected** whatever `TResult` is.

       That closes the scalar "whole list as First" class on every path: direct (P6, P7) and through
       covariant `Skip`/`Take`/`Distinct` (r6 Q1a–e). The guard is the primary mechanism, and these LINQ
       shapes reach it.
     - **I3 — `Cast`/`OfType` are judged against `R` (D5).**
       - `Cast<X>` is allowed iff `X == R`, or `R` is a reference type and `X.IsAssignableFrom(R)`. It's
         ignored by translation either way.
       - `OfType<X>` is allowed iff `X == R` (identity; the owner's amendment covered only `Cast`) and `R` is
         `T` or a non-nullable value type, so no element can be dropped.
       - Everything else is rejected.
     - **Consequences, all executed:**
       - Parameterless terminals and lambda-free sequence operators (`Skip`/`Take`/`Distinct`) over any
         converted source are left alone: they never bind a lambda, and translation is type-agnostic.
       - Over an entity source they're correct (P5, P5b, P13, P13b, r6 Q3a–e, Q9a/b, Q4d).
       - Over a scalar source, sequence operators are correct (P7b–P7e, Q8), and terminals are rejected by I2.
   - `IsAllowed` is **public**.
   - **Allowed operators:**
     - `Where`, `Select`;
     - `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`;
     - `Skip`, `Take`, `Distinct`;
     - `First*`, `Single*`, `Last*` (with and without predicate);
     - `Any`, `All`, `Count`, `LongCount`;
     - `Sum`, `Average`, `Min`, `Max` (generic, with selector; behavior unchanged until 3.10.1);
     - `Cast`/`OfType` per D5/I3.
   - **Pass-2 rules, in this order at each node:**
     1. D8, after the first `Skip`/`Take`. A `Take` is allowed after the `Skip` when only `Select` and/or
        allowed D5 `Cast`/`OfType` sit between them.
     2. D10: an `OrderBy`/`OrderByDescending` after any earlier ordering call is rejected.
     3. D5/I3 (`Cast`/`OfType` against `R`).
     4. I1 (lambda parameter type).

     Pass 1's allow-list runs over the **whole spine first** (outer→inner), so an allow-list failure anywhere
     wins over any pass-2 failure. Within pass 1, the outermost failure wins.

     *(Rev 4's separated-`ThenBy*` clause was removed in rev 6: the shape can't be built, P3.)*
   - **Messages.**
     - `GroupBy` keeps its dedicated message.
     - Other rejected operators: `"{Op}(...) is not translated to SQL in this version. Materialize first and
       apply it in memory: query.ToList().{Op}(...)."`
     - Positional (D8): `"{Op}(...) after Skip/Take is not translated …"`.
     - D10, second `OrderBy*` *(rev 21, T4R-6)*: `"{Op}(...) after an earlier ordering is not translated. In
       LINQ the later ordering becomes the primary key and the earlier keys only break ties; write that as one
       chain, primary key first: query.{Op}(later).ThenBy(earlier)."`
     - I1: `"{Op}(...) takes a predicate over {ParamType}, but the query's rows are {T}. Apply {Op} before
       converting the element type (IQueryable<…>/Cast<…>), "` followed by *(rev 21, T4R-1)*:
       - for a class other than `object`: `"make the helper generic (where TEntity : {ParamType}), or query the
         concrete type."`;
       - for an interface or `object`: `"or apply it to the concrete IQueryable<{T}>."`
     - After an unsupported `Select` (neither a subset nor a scalar member) the row type is unknown, so D5/I3
       don't judge a later `Cast`/`OfType`: the parse loop rejects the `Select` with the `Select`-shape message
       *(rev 21, T4R-2)*.
     - I2, scalar terminal: the existing scalar-guard message, which names the operator and says scalar
       projections support enumeration only (materialize first).
     - Boxing or unrelated-type `Cast` (D5): `"Cast<{X}>() is not translated; FunkyORM supports only
       identity and reference-conversion casts. Materialize first: query.ToList().Cast<{X}>()."`
     - `OfType` over a nullable or reference scalar: the message names `OfType<X>()` and points to
       `Where(x => x.M != null)` before the projection *(rev 19: reference scalars included)*.
     - Non-identity `OfType`: the message names `OfType<X>()` *(rev 19)*.
   - `EnsureSupported` returns `void`. It only throws.
2. **Wiring.**
   - Each provider's `ParseExpression` calls `QueryOperatorPolicy.EnsureSupported(expression)` first. For
     SQLite, that's after the state reset (D11) and before the early return.
   - Remove the now-dead inline `GroupBy` check and the parameterless-aggregate-after-scalar guard
     (SqlLinqQueryProvider.cs:339-346 and the sibling copies).
   - Fix the stale comment at :128-129 and the sibling copies.
   - The scalar composition and result-type guards stay (precedence per AC13-8).
   - **I2 in `Execute` (rev 7).** In each provider, `isCollection` becomes
     `typeof(IQueryable).IsAssignableFrom(expression.Type)` (SqlLinqQueryProvider.cs:99; siblings :58).
     - Collections return the `List<T>` read from the database, typed as `TResult` through covariance (it's
       always an `IEnumerable<X>` with `X` assignable from `T`, because the expression's element type is `T`
       or a supertype).
     - This fixes P14 (`Cast<object>()` enumerated), and it's exact for pathological types such as
       `T : IEnumerable<object>` (r6 F3a). Paths that work today are unchanged: enumeration always has an
       `IQueryable` expression, and terminals never do.
   - **I2 in the non-generic `IQueryProvider.Execute(Expression)` (rev 8).** Today it pins
     `TResult = IEnumerable<T>` (SqlLinqQueryProvider.cs:70-73; siblings :38-41), so a terminal expression
     returns the whole list as its "result" (r7 K1, a silent wrong result).
     - It dispatches on the expression shape instead: an `IQueryable`-typed expression goes to
       `Execute<IEnumerable<elementType>>`; anything else goes to `Execute<expression.Type>`, via
       `MakeGenericMethod`.
     - **Exceptions must not arrive wrapped (r8 #8).** `MethodInfo.Invoke` wraps everything in
       `TargetInvocationException` (r8 X1, K5), and netstandard2.0/net48 lack `DoNotWrapExceptions`. So the
       dispatcher catches `TargetInvocationException` and rethrows the inner exception with
       `ExceptionDispatchInfo.Capture(inner).Throw()`, preserving its type and stack trace. (A cached typed
       delegate is an equivalent alternative.)
     - Collection calls are unchanged (K2); value-type terminals return correctly (K4).
   - **I2 in `ScalarProjectionGuard` (rev 5; reworked in rev 7).** A Core public helper,
     `ScalarProjectionGuard.EnsureCollectionResult(Expression expression, Type resultType, Type memberType)`,
     is called first by `ExecuteScalarProjection` in all four providers.
     - It throws the existing scalar-guard message (naming the outermost operator) when the expression is a
       **terminal** (I2).
     - It keeps the existing `resultType` ⊇ `List<memberType>` check as a backstop.
     - **It is the primary mechanism for scalar terminals and is reached through LINQ**: P6, P7, r6 Q1a–e. So
       the §4.2 rows pin it end to end, and direct DB-free tests cover its branches.
3. **`Single*` (row limit).**
   - The First/Last predicate branch generalizes to `First*`/`Single*`/`Last*` × {with, without predicate}.
     It routes the predicate to WHERE and sets `components.Terminal`.
   - **No user `Skip`/`Take`:** `RowLimit = 2`. SQL Server emits `SELECT TOP (2)` / `SELECT DISTINCT TOP (2)`;
     the others emit `LIMIT 2` after any ORDER BY. No ORDER BY is synthesized.
   - **User `Skip` or `Take`:** `Take = min(Take ?? 2, 2)` on the existing paging path.
   - It reads up to two rows through the list path, then applies LINQ cardinality.
4. **`Last*`.**
   - The visitor exposes `OrderByTerms` (`(fragment, isDescending)`, after duplicate removal).
   - `Last*` inverts every term. With no terms it uses `Id DESC`, table-qualified when the entity has joins
     (the §5.1.2 call site passes the qualifier).
   - It uses `RowLimit = 1` with that ORDER BY. `Last` throws on empty; `LastOrDefault` returns `null`.
   - With `Distinct` + custom projection and no explicit order, it throws, naming `Last`. This check lives in
     `BuildQueryComponents`, next to the existing `Distinct` guards. On the scalar path,
     `ScalarProjectionGuard` runs first and wins (`ScalarDistinctLast_ScalarGuardWins`).
   - This replaces the current `LastOrDefault(pred)` block. D10 (no second `OrderBy*`) plus System.Linq
     (a `ThenBy*` can only follow the root or an ordering call, §1.5 P3/P4) guarantee the ordering terms form
     one contiguous chain, so the inversion is well defined.
5. **`LongCount`.** Added to the `OuterMethodCall` list and `BuildAggregateClause`, and handled like `Count`
   (including the reverse-key rejection). SQL Server emits `COUNT_BIG(*)`. The result converts to `Int64`.
6. **`Skip`/`Take` values.**
   - `Skip(n < 0)` is stored as 0.
   - `Take(n ≤ 0)` sets `components.IsEmptyByTake`.
   - Two checks, in this order of precedence:
     - `Execute` checks the flag **after** the scalar dispatch (SqlLinqQueryProvider.cs:94-97), so it only
       handles the entity path (`R = T`).
     - `ExecuteScalarProjection` checks it **after** `ScalarProjectionGuard` (I2), with `R` = the scalar
       member type. `Select(p => p.Id).Take(0).First()` therefore throws the scalar guard's
       `NotSupportedException`, not "no elements". So do `FirstOrDefault`/`Single`/`SingleOrDefault`.
   - Outcomes, decided by **I2** (expression shape), never by `TResult` assignability:
     - collection → an empty `List<R>`, returned as `TResult` through covariance;
     - terminal `First`/`Single` → throw "no elements";
     - terminal `*OrDefault` → `default(TResult)`.

     So `q.Take(0).Cast<object>().First()` throws, rather than returning an empty list as "first" (r6 F3b). No
     command is built.
7. **SQLite `Skip` without `Take`** emits `LIMIT -1 OFFSET n` (AC13-14).
8. **D11 SQLite reset.** Clear `_lastSelectProjection`, `_lastSelectParameters`, `_lastOrderByClause` and the
   new `OrderByTerms` field at the top of every `ParseExpression`, before the early return.

---

## 6. Tasks (v3.10.0)

Each task lists the tests it turns green. Every implementation task starts with them present and **red**.
**AC13-5 (regression) is a gate on every task:** all four existing suites stay green after each one.

- **Task 0 — Test-plan review gate.**
  - ✅ Branch; D1–D12 decided.
  - ✅ Premises re-verified.
  - ✅ Baselines recorded.
  - ✅ Issues retitled, evidence posted.
  - ✅ Review r1: 25 findings (§9.1). Fix-verification r2: 21 (§9.2), r3: 10 (§9.3), r4: 7 (§9.4),
    r5: 8 (§9.5), r6: 8 (§9.6).
  - ✅ Owner answered the §10 questions (3.10.1), amended D5 (reference-conversion `Cast`), filed the
    sort-helper crash as follow-up #16, and deferred signing.
  - ✅ Executed premise probes recorded (§1.5), including the r6 reviewer's and the I1 probes.
  - ✅ r7: 6 findings (§9.7). The owner narrowed I1 to predicate lambdas ("reject only where 3.9.0 fails").
  - ✅ r8: 9 findings (§9.8), r9: 4 (§9.9), r10: 1 + 6 nits (§9.10), r11: 2 + 3 nits (§9.11), r12: 3 + 2
    nits (§9.12), r13: 1 + 1 draft + 3 nits (§9.13), r14: 1 + 4 nits (§9.14), r15: 1 + 2 nits (§9.15). Text and test-spec only.
  - ✅ r16 of `ed772fb`: **CLEAN**, nits only (§9.16). The §3 ACs, including AC13-15, were posted to #12/#13
    on 2026-09-30.
- **Task 1 — Stubs, schema, harness, red tests.**
  - Compile-only stubs so the red run fails at runtime: policy members throw `NotImplementedException`; plus
    the visitor `tableQualifier`, `OrderByTerms`, and the `QueryComponents` members.
  - **Schema:** no script changes.
    - The no-`id` rows use a test-owned `single_probe` table, created by the test class itself (§4.1).
    - The SQLite temp-DB helper creates the join tables (`country`, `organization`, `address`, `person`,
      `person_address`), as `SqliteRemoteFeaturesTests` does today.
  - Harness, SQLite helper, and the net48/net9 links.
  - Add `IHasPersonId` (`int Id { get; }` only) to the person entities in all four test projects, and
    MySQL's `PersonBase` (§4.1). That supports the base-class/interface rows and the D5 base-class/interface
    `Cast` rows. Then run the four existing suites to confirm the entity change is behavior-neutral.
  - Write every §4.2 test and record red (or expected-green for regression rows).
  - Confirm §1.5's "still unexecuted" items through that red run. Everything else in §1.5 is executed (e.g.
    P8 error 169, P9 alias binding, P10 SQLite `OFFSET`, P1 `Cast` nodes), or is a labelled code read about
    FunkyORM's own output (r9-C1, r11-C2).
  - **Status (2026-10-01): written and red-run.** Commits `600319f` (stubs, `IHasPersonId`/`PersonBase`),
    `f17bc65` (Core tests, literal set), `6542796` (SQL Server suite) and `97e7d56` (PostgreSQL/MySQL/SQLite
    ports, harness `requireSuccess`).
    - The existing suites are unchanged after the entity change and with the new tests: 250, 167, 143, 94.
    - New rows, green / red on the stubs (after the §9.17–§9.19 fixes):

      | Suite | Green | Red | Rows |
      |---|---|---|---|
      | SQL Server (incl. Core net8 9 rows and the 2 doc-table rows) | 171 | 311 | 482 |
      | SQLite | 149 | 327 | 476 |
      | PostgreSQL | 172 | 299 | 471 |
      | MySQL | 172 | 299 | 471 |
      | Core on net48 / net9 (separate projects) | 0 | 7 / 3 | 7 / 3 |

    - Every red message was read and fails for its 3.9.0 reason. The greens are regression pins.
      - On PostgreSQL, 3.9.0's unordered reads follow heap order, so some `Last` rows' value checks pass by
        chance in some runs. Their SQL asserts keep them red (5 runs, stable; §9.17 P1-2).
      - SQLite's provider-scope exceptions are in §4.2.
    - Evidence: the t1 rows in §1.5.
    - ✅ Test review by two non-author reviewers: 18 findings, folded in (§9.17).
    - ✅ Fix-verification FV1 of the review fixes: 2 minor + 3 nits, all folded in (§9.18).
    - ✅ Fix-verification FV2: 1 minor + 2 nits, folded in (§9.19).
    - ✅ Fix-verification FV3 of `bbede92`: **CLEAN** (1 comment nit, fixed). Task 1 is complete; Task 2 may start.
- **Task 2 — #12 qualifier + duplicate removal** (4 providers).
  → AC12-1…AC12-4, AC12-6, AC12-9.
  - **Status (2026-10-01): done.**
    - Each provider's visitor qualifies own columns (`{table}.{column}`) when the `OrderBy` call site passes
      `tableQualifier` (the entity has joins); map hits are never prefixed.
    - A later duplicate fragment is dropped.
    - The `Last*` site is Task 7's.
    - Red → green on every provider: 30 rows (24 AC12-1, AC12-4, AC12-9, four direct visitor tests). SQLite also
      greens the 4 `UnchangedFromConcrete` rows its #12 ambiguity blocked (§4.2 provider-scope note).
    - No green → red anywhere; the existing suites are unchanged.
    - `DuplicateKey_LaterFragmentDropped` stays red until Task 6 (`OrderByTerms`).
    - **Mutations run (SQL Server), all killed:**
      - bare `GetColumnName` → 24/24 AC12-1 rows;
      - qualifier in the map-hit branch → `RemoteMemberOrderBy_EmitsExactResolvedFragment_NoBasePrefix`;
      - qualify unconditionally → `SingleTableEntity_OrderBy_SqlByteIdenticalTo390`;
      - no duplicate removal → `ThenBy_SameKeyTwice_Executes`.
- **Task 3 — SQLite `rowid` qualification + `LIMIT -1 OFFSET`.**
  → AC12-7, AC13-14.
  - **Status (2026-10-01): done.**
    - The default paging order is `ORDER BY {table}.rowid` when the command has joins (remote or
      WHERE-introduced) and `ORDER BY rowid` otherwise. A `Skip` without `Take` emits `LIMIT -1 OFFSET n`.
    - 21 SQLite rows red → green, no regressions:
      - AC12-7 (both);
      - AC13-14 (all three);
      - the five Skip-only `Operator_AfterPaging_Allowed` rows;
      - the D8Governs `Skip(1)→First` rows, the `afterSkip` oracle rows, and two `UnchangedFromConcrete` rows
        (§4.2 provider-scope note).
    - `SkipNegative_BehavesAsSkipZero` is green on SQLite already: SQLite treats a negative OFFSET as 0. The
      other providers still need Task 5's clamp.
    - **Mutations run, both killed:** bare `rowid` → `DefaultPaging_OnJoinEntity_Executes`; OFFSET without
      LIMIT → `SkipOnly_ToList_Executes`.
- **Task 4 — Core policy (two-pass, own-root check, row type `R`) + D8, D10 + D5/I3 + I1 + I2 (`isCollection`
  by expression shape; Core `ScalarProjectionGuard`) + wiring + dead-guard removal** (4 providers).
  → AC13-4 (except the entity-source covariant `LongCount`/`Single`/`Last` rows), AC13-6, AC13-7, AC13-8,
  AC13-10 (rejection rows, and the allowed `Take.Cast<object>()`-enumerated and Q7 rows, which depend on
  I2), AC13-12, AC13-15 (non-generic `Execute` dispatch).
  - **Status (2026-10-01): done.**
    - Core `QueryOperatorPolicy`:
      - the allow-list is built from `Queryable` by name and shape and matches the pinned 52;
      - pass 1 runs outer→inner with the own-root check;
      - pass 2 runs inner→outer with D8 → D10 → D5/I3 → I1 and the row type `R`.
    - Core `ScalarProjectionGuard` (I2).
    - Each provider:
      - calls the policy first in `ParseExpression`;
      - decides `isCollection` by expression shape;
      - runs the guard first in `ExecuteScalarProjection`;
      - dispatches the non-generic `Execute` by shape, unwrapping `TargetInvocationException`;
      - drops the inline `GroupBy` and parameterless-scalar-aggregate guards.
    - Every Task 4 row is green on all four providers. No green → red anywhere; the existing suites are unchanged.
      The remaining reds belong to Tasks 5–8 and 10:

      | Suite | Red |
      |---|---|
      | SQL Server | 85 |
      | SQLite | 85 |
      | PostgreSQL | 82 |
      | MySQL | 82 |

    - The net48 twin is 7/7 and the net9 twin 3/3.
    - **Mutations run: 42, all killed.** One ("pass 2 walks outer→inner") first survived; §9.21 has the
      test fix. Re-run after Task 7 with the baseline-aware runner (§9.22 T7-2): 42 killed, each by a
      baseline-passing test. The PostgreSQL row now builds the PostgreSQL provider (40/40).
- **Task 5 — `Single*` row limit, `Skip`/`Take` values, empty-`Take` short-circuit (I2-based)** (4 providers).
  → AC13-1, AC13-10 (remaining allowed rows, and empty rows), entity-source covariant `Single` row of AC13-4,
  AC13-5's `Allowed[SinglePredicate]`/`[SingleOrDefaultPredicate]` rows.
  - **Status (2026-10-01): done (4 providers).**
    - `Single*`: predicates route to WHERE; the terminal is recorded.
      - No user paging: `RowLimit = 2` (SQL Server `SELECT [DISTINCT] TOP (2)`, the others `LIMIT 2`), with no
        ORDER BY synthesized.
      - User paging: `Take = min(Take ?? 2, 2)`.
      - Reads go through the list path, then LINQ's cardinality rules.
    - `Skip(n < 0)` clamps to 0. `Take(n <= 0)` sets `IsEmptyByTake`, which is answered without a command:
      - on the entity path after the scalar dispatch (empty list / "no elements" / `default`, by expression shape);
      - on the scalar path after `ScalarProjectionGuard`.
    - Red → green on every provider: every AC13-1 test, every `Take0_*`/`TakeNonPositive` row, `SkipNegative`
      (already green on SQLite), the two `Allowed[Single*Predicate]` rows, and the root `Single` covariant rows.
    - No green → red; the existing suites are unchanged.
    - **Mutations run (SQL Server): 13, all killed.** One was first written in the wrong direction ("decides by
      `TResult` assignability" must mean `TResult ⊇ List<T>`); corrected and killed. Re-run after Task 7 with
      the baseline-aware runner (§9.22 T7-2): 13 killed.
- **Task 6 — Visitor `OrderByTerms` + ternary null** (4 providers).
  → AC12-8.
  - **Status (2026-10-01): done (4 providers).**
    - `OrderByTerms` exposes the visitor's terms `(fragment, isDescending)` after duplicate removal.
    - A ternary test `x.M == null` / `null == x.M` (and `!=`) emits `{operand} IS NULL` / `IS NOT NULL`, where the
      operand is qualified like any ORDER BY column. Before, it emitted `= NULL`, which is never true.
    - Red → green on every provider: the three direct `OrderByTerms`/`DuplicateKey` tests, the four
      `TernaryNullTest` rows, and the four AC12-8 `TernaryOrderBy_NullComparison` rows.
    - No green → red; the existing suites are unchanged.
    - **Mutations run (SQL Server): 11; 10 killed.** The survivor, "don't unwrap a `Convert` around the null
      constant", exposed dead code. A probe showed the compiler types a null literal as the other operand's type
      (`string`, `int?`, `object`) and never wraps it, so the unwrap was deleted rather than tested. Re-run
      after Task 7 with the baseline-aware runner (§9.22 T7-2): the other 10 killed.
- **Task 7 — `Last*`** (4 providers).
  → AC13-2, AC12-5, covariant `Last` row of AC13-4, AC13-5's `Allowed[Last*]` rows.
  - **Status (2026-10-01): done (4 providers).**
    - The ordering branch records `OrderByTerms`. At the end of the parse, `Last*` records its terminal and sets
      `RowLimit = 1` with an ORDER BY that either:
      - inverts every term whole (own, remote/computed and `CASE` fragments; own terms stay qualified); or
      - with no terms, is `Id DESC`, qualified when the entity has joins (`DefaultLastOrderBy`, which keeps
        each provider's "no `Id` property" message).
    - The old `Last(pred)`-only block is gone. It synthesized an unqualified `Id DESC`, and parameterless
      `Last` had no order at all.
    - `Distinct` + custom projection + no explicit order throws `NotSupportedException` naming the terminal.
      The check sits in `BuildQueryComponents`, after `ScalarProjectionGuard`.
    - Red → green on every provider: every AC13-2 test, AC12-5, the 4 `Allowed[Last*]` rows, and 8
      `Covariant_EntitySource_ParameterlessTerminal` rows. No green → red.
    - `Last_ReadsOneRow_EmitsRowLimit` was added after a surviving mutation (§9.22 T7-1).
    - **Mutations run (SQL Server, baseline-aware runner): 14, all killed.** "No row limit" is killed only by
      the T7-1 test.
    - Remaining reds: `LongCount` (Task 8), the SQLite reuse rows (Task 9), and the doc-table test (Task 10).
- **Task 8 — `LongCount`** (4 providers).
  → AC13-3, covariant `LongCount` row of AC13-4, AC13-5's `Allowed[LongCount*]` rows.
  - **Status (2026-10-01): done (4 providers).**
    - `LongCount` is recorded as the outer aggregate and built like `Count`: same WHERE, predicate and
      reverse-key rejection. SQL Server emits `COUNT_BIG(*)`; the result converts to `Int64`.
    - Red → green on every provider: the four `LongCount_*` tests (`LongCount_EmitsCountBig` is SQL Server
      only), `Allowed[LongCount, LongCountPredicate]`, and 8 covariant `LongCount` rows. No green → red.
    - PostgreSQL and MySQL are now fully green. Remaining reds: the four SQLite reuse rows (Task 9) and the
      SQL Server doc-table test (Task 10).
    - **Mutations run (SQL Server, baseline-aware runner): 6, all killed.** They cover: not the outer aggregate;
      not routed to the builder; `COUNT(*)`; an `Int32` result; reverse key allowed; predicate ignored.
- **Task 9 — SQLite state reset; coverage lift.**
  - State reset → AC13-13.
  - Direct visitor tests.
  - MySQL provider to ≥85%.
  → §4.5.
  - **Status (2026-10-01): done.**
    - **D11:** SQLite clears `_lastSelectProjection`, `_lastSelectParameters` and `_lastOrderByClause` at the top
      of every parse, before the bare-root return. (`OrderByTerms` lives on the per-parse `QueryComponents`, so
      there's no field to reset.) The four `SqliteRoot_Reused*` tests went red → green, and the SQLite suite is
      fully green.
      - **Mutations: 5, 4 killed.** The survivor, "omit the parameters reset", is **equivalent**: the parameters
        are read only under a non-empty projection, which is reset alongside. The reset is kept, as §5.2.8 lists it.
    - **Coverage:** in each suite's `*OrderByVisitorDirectTests`, `Ternary_Branch_BuildsCase` (17 rows),
      `UnsupportedShape_ThrowsNotSupported` (13 rows), `NoOrdering_EmptyClauseAndNoTerms`,
      `LambdaNode_VisitsItsBody` and `ConvertedMemberKey_OrdersByItsColumn`. Also
      `ScalarProjectionGuard_NullArgument_ThrowsArgumentNull`.
      - Expected column fragments come from the visitor itself, so the rows pin the shape each branch builds.
      - **Mutations: 13, all killed** (SQL Server visitor and Core guard).
    - Coverage after Task 9 (cobertura `line-rate`; the Core files are taken from the suite that tests them
      directly):

      | Touched file | SQL Server | SQLite | MySQL | PostgreSQL |
      |---|---|---|---|---|
      | `*LinqQueryProvider.cs` | 90.9% | 94.2% | 93.1% | 93.1% |
      | `*OrderByClauseVisitor.cs` | 100% | 100% | 100% | 100% |
      | `QueryComponents.cs` | 100% | 100% | 100% | 100% |
      | Core `QueryOperatorPolicy.cs` | 99.5% | | | |
      | Core `ScalarProjectionGuard.cs` / `OrderByTerm.cs` | 100% / 100% | | | |

      The `*QueryComponents.cs` prefixed files aren't touched by this work.
    - The only red left is the SQL Server doc-table test (Task 10).
- **Task 10 — Docs.**
  - Operator table + doc test.
  - Changelog 3.10.0: **Fixed**, **Changed** (untranslated operators, operators after paging, and a second
    `OrderBy` now throw), and **Known issues** (aggregates, fixes planned for 3.10.1).
  - README "upgrade strongly recommended".
  - Usage.md: `LongCount`.
  → AC13-9.
  - **Status (2026-10-01): done.**
    - `Advanced.md` §5 and `FUNKYORM_AI_ADVANCED.md` §5 carry the operator table between the markers. They also
      give the paging rule, the one-`OrderBy` rule, the `Last`/`Distinct` note, the base-type/interface rule, and
      the unlisted operators. Both docs mention `LongCount` in their aggregate sections. The AI doc gains quick
      rules 9–12.
    - Changelog `[3.10.0] - Unreleased` has **Fixed**, **Changed** and **Known issues**. Changed lists every rule
      with the shapes that happened to be correct in 3.9.0 (AC13-5, rev 21); Known issues follows §8. Each
      claim was checked against §1 and the AC text. SQL Server's whole-number `Average` truncation is stated as
      fact, because S10 executed it (§8's "to be confirmed" is resolved).
    - README: a 3.10.0 "upgrade strongly recommended" line. Usage.md: `LongCount` and the one-`OrderBy` rule,
      both linking to `Advanced.md` §5.
    - `OperatorDocTable_MatchesSupportedOperators` went red → green on both docs, so **every suite is green**.
      **Mutations: 3, all killed** (a missing operator, an extra operator, a missing marker).
- **Task 12 — ORDER BY values as parameters** *(rev 30; owner decision 2026-10-01, before the beta)* → AC12-10.
  - **Status (2026-10-01): done (4 providers).**
    - The visitors gain a constructor overload with the provider's parameter generator and a `Parameters` property.
      `ValueSql` sends quoted-type values as parameters, reusing one per equal value (since rev 32: one per
      occurrence).
    - The providers pass their generator to the ordering visitor, keep the outermost visitor's parameters
      (`QueryComponents.OrderByParameters`), and bind them unless the command is an aggregate.
    - MySQL's literal mode escapes backslashes.
    - Tests first: 8 red per suite, 9 on MySQL, including wrong rows on MySQL for a value containing a backslash and
      a quote. Then green. The AC12-4 test's value position was loosened to a parameter.
    - **Mutations (baseline-aware runner): 11.** 8 were killed outright. The `char` case was equivalent to the
      default branch and was deleted. Two survived because drivers ignore unused parameters (aggregates; earlier
      visitors); `AssertEveryParameterReferenced` now kills both.
    - All suites green: SqlServer 856, Sqlite 760, PostgreSql 731, MySql 683; net48 76/76; net9 5/5.
  - **Review remediation (rev 31, §9.33).** The hostile review found that values typed like WHERE parameters
    brought in WHERE's type bugs:
    - SQLite: Guids and dates in the driver's formats, not FunkyORM's storage formats.
    - PostgreSQL: typed `timestamp`/`timestamptz` throwing for UTC and offset values.
    - SQL Server: `uniqueidentifier` ordering, and `datetime2` against legacy `datetime` columns.

    Each value is now bound as `LiteralText(value)`, the text `FormatConstant` quotes, so literal mode and
    parameter mode can't drift apart.
    - Tests first, red on `e104aad`: SQL Server 2, PostgreSQL 5, MySQL 1, SQLite 8. *(Corrected in rev 32, N4.)*
      `LiteralMode_ValueWithNullText_IsEmptyText` was written after the code. It also fails there, making the counts
      3/6/2/9: on `e104aad`, a value whose `ToString()` is null crashed the parameter dictionary
      (`ArgumentNullException`).
      - The first SQLite Guid row passed: its digit-only Guids read the same in either case, so the fixture now
        uses hex letters.
      - The new helper's self-test and the separate-parameter test were green by design; mutations prove them.
    - **Mutations (14, all killed: 4 SQL Server, 4 PostgreSQL, 2 MySQL, 3 SQLite, plus the test helper).**
    - All suites green: SqlServer 870, Sqlite 774, PostgreSql 745, MySql 697; net48 76/76; net9 5/5.
  - **Verification layer (rev 32, §9.34).** Rev 31 shared one untyped parameter between equal values. PostgreSQL
    types a parameter once, at its first use, so later uses compared wrongly or failed (N2). A parameter that met
    only `IS NULL` couldn't be typed at all (N1).
    - Each value occurrence is now its own parameter: a placeholder until its term is added, then bound unless
      the term is a duplicate.
    - Terms compare by a key that writes each value as its kind and length-prefixed text.
    - On PostgreSQL, a parameter's null test is decided in .NET.
    - Tests first, red on `7d9b65b`: SQL Server 2, PostgreSQL 11, MySQL 2, SQLite 2. The SQL Server legacy
      `datetime` test (N5) was green there by design; a mutation proves it.
    - **Mutations (10, all killed: 3 PostgreSQL, 3 SQL Server, 2 MySQL, 2 SQLite).**
    - All suites green: SqlServer 881, Sqlite 784, PostgreSql 756, MySql 707; net48 76/76; net9 5/5.
  - **Verification layer 2 (rev 33, §9.35).**
    - R1: `AddOrderByClause` substitutes placeholders only with a parameter generator.
    - Nit B: the term key is recorded once the term is added.
    - Red first: `LiteralMode_PlaceholderShapedText_IsInlined` threw `IndexOutOfRangeException` on all 4 providers.
    - Mutations: 4. 3 were killed. The fourth reverts nit B and survived as an equivalent mutant: `Bind` can't
      throw for a non-null string, so the order is unobservable. The reorder is defensive only.
    - Suites: SqlServer 882, Sqlite 785, PostgreSql 757, MySql 708; net48 76/76; net9 5/5.
  - **Verification layer 3 (rev 34, §9.36).** Docs and test infrastructure only; no product code changed.
    - The probe table is a fixture table, so concurrent runs no longer race to drop it.
    - Six concurrent pairs of the probe test: 12 of 12 passed, and no rows were left behind.
    - Suites: SqlServer 882, Sqlite 785, PostgreSql 757, MySql 708; net48 76/76; net9 5/5.
  - **Verification layer 4 (rev 35, §9.37).** Prose only (N1–N4); no code or test changed.
  - **Verification layer 5 (rev 36, §9.38).** Plan prose only (F1–F5).
  - **Verification layer 6 (rev 37, §9.39).** Plan prose only (G1–G5).
  - **Verification layer 7 (rev 38, §9.40).** Plan prose and one Changelog bullet (H1–H4).
  - **Verification layer 8 (rev 39, §9.41).**
    - Code: the four visitors' `LiteralText` writes a `DateOnly` as `yyyy-MM-dd` and a `TimeOnly` as
      `HH:mm:ss.FFFFFFF`. Both are matched by type name, since netstandard2.0 has neither type.
    - Code: MySQL's writes a `DateTimeOffset` as its UTC time.
    - Tests first, red on `26abd27`: SQL Server 2, PostgreSQL 2, MySQL 4, SQLite 2.
      - Direct ISO text pins.
      - `DateOnly` and `TimeOnly` branch-value oracle rows.
      - On MySQL: WHERE and ORDER BY agree for a `DateTimeOffset`, and the updated UTC text pin.
    - Mutations (4, all killed).
    - Suites: SqlServer 884, Sqlite 787, PostgreSql 759, MySql 711; net48 76/76; net9 5/5.
  - **Verification layer 9 (rev 40, §9.42).** A `TimeOnly` fraction pin and oracle row on all four providers. These
    are guards: the implementation was already right, and the `"HH:mm:ss"` mutant now fails.
    - Mutations (4, all killed (TimeOnly without its fraction, per provider)).
    - Prose F2–F5.
    - Suites: SqlServer 884, Sqlite 787, PostgreSql 759, MySql 711; net48 76/76; net9 5/5.
  - **Verification layer 10 (rev 41, §9.43).** One new test and one added pin per provider; both are guards (the
    code was already right):
    - a seven-digit `TimeOnly` pin, added to `ParameterMode_DateOnlyAndTimeOnly_AreIsoText`;
    - a new test: the eleven numeric types inline under fi-FI, and `nint`/`nuint`/`Half` as parameters.
    - Mutations (28 of 28 killed, 7 per provider: `TimeOnly` as `HH:mm:ss.F` and `.FFF`; `IsNumber` without `decimal`, `sbyte` or `ulong`; `IsNumber` with `nint`; an inline number in the current culture).
    - Prose K1, K2, K2b, K5–K7.
    - Suites: SQL Server 885, PostgreSQL 760, MySQL 712, SQLite 788; net48 76/76; net9 5/5.
  - **Verification layer 11 (rev 42, §9.44).** Docs only: L1–L4. No code or test change, so the suites and
    mutations of layer 10 stand (the verifier re-ran them: suites reproduced, 70 of 70 mutants killed).
  - **Verification layer 12 (rev 43, §9.45).**
    - Two test changes, both guards (the code is unchanged):
      - the MySQL `DateTimeOffset` test seeds through the harness;
      - `ParameterMode_DateOnlyAndTimeOnly_AreIsoText` runs under fi-FI.
    - Mutations (8 of 8 killed by `ParameterMode_DateOnlyAndTimeOnly_AreIsoText` alone: `DateOnly` and `TimeOnly` in the current culture, per provider; the MySQL `DateTimeOffset` test passes run alone).
    - Docs M2, M3, M5, M6.
    - Suites: SQL Server 885, PostgreSQL 760, MySQL 712, SQLite 788; net48 76/76; net9 5/5.
  - **Verification layer 13 (rev 44, §9.46).**
    - Test changes only, both guards:
      - the MySQL harness discovers `Person` before each test;
      - the `DateOnly`/`TimeOnly` test also runs under th-TH.
    - Mutations (8 of 8 killed by `ParameterMode_DateOnlyAndTimeOnly_AreIsoText` alone: `DateOnly` and `TimeOnly` with the current culture as format provider, per provider; the three N1 tests and the rev 39 `DateTimeOffset` test each pass run alone).
    - Docs N3, N4.
    - Suites: SQL Server 885, PostgreSQL 760, MySQL 712, SQLite 788; net48 76/76; net9 5/5.
- **Task 11 — Gauntlet and release.**
  - *(Rev 24, gx F4)* Bump the five shipping csprojs to `3.10.0-beta1` before the PR (done in the gauntlet round),
    then to `3.10.0` for the release. CI packs and publishes from `master`.
  - Mutation runs recorded; net48/net9 runs.
  - A fresh non-author adversarial pass, then fix-verification.
  - Sentinel file for the exact clean sha.
  - Push `development/3.10` (CI + MySQL + PostgreSQL) → PR → merge → `3.10.0-beta1`.
  - Sentinel smoke test → `3.10.0`.
  - Close #12. Keep #13 open for the 3.10.1 aggregate work, or split it (owner's call).
  - Security Advisory decision (D7).

**Rough effort (3.10.0):**

| Work | Estimate |
|---|---|
| Task 1 (stubs, schema, ~120 rows × 4) | ~1.5 days |
| #12 + SQLite paging (Tasks 2–3) | ~0.75 day |
| Policy + `Single`/`Last`/`LongCount` (Tasks 4–8) | ~2.25 days |
| Coverage + docs + gauntlet (Tasks 9–11) | ~1.5 days |
| **Total** | **~6 working days** |

---

## 7. Risks

- **Callers relying on silently ignored or mistranslated operators.** That code now throws or returns a
  different value. **Mitigation:** minor version, prominent Changelog lists, and messages that say what to
  write instead.
- **Over-rejection.** **Mitigation:** the allowed set mirrors what the loop translates; oracle rows per family;
  existing suites as the gate. No FunkyORM test or Sentinel query composes after paging or uses a second
  `OrderBy`.
- **Mirror drift.** **Mitigation:** policy and positional rules live in Core; per-provider changes are proved
  by identical test names.
- **Row-limit syntax.** `DISTINCT TOP (n)` on SQL Server; `LIMIT` after ORDER BY elsewhere. **Mitigation:**
  covered by tests.
- **net48.** The allowed set is identical across frameworks: the overloads that differ are all excluded
  anyway. **Mitigation:** one literal set, swept on every TFM.

## 8. Out of scope (recorded, not fixed here)

- **Aggregates (D9) → 3.10.1 (§10).** Documented as known issues in the 3.10.0 changelog:
  - `Average` of whole numbers truncates on SQL Server (confirmed: S10, and r810's `AVG` over (1),(2) = 1) and
    loses precision on MySQL and
    SQLite;
  - `decimal`/`float` `Average` throws;
  - nullable `Min`/`Max`/`Average` on an empty set throws;
  - some `Min`/`Max` result types throw after the round-trip.
- `GenerateOrderByClause` (three providers) is dead code.
- **The visitors' `OrderByClause` getter writes "Generated ORDER BY clause" to stdout** on every ordered query
  (pre-existing, all four; gx). Remove in a follow-up.
- **SQL Server: `OrderBy(computedSubquery).Select(…).Distinct()`** fails ("ORDER BY items must appear in the select
  list"), for `ToList` as for `Last` (pre-existing; gx).
- **net48 twin from a clean checkout:** `dotnet build FunkyORM.sln` doesn't copy the old-style csproj's transitive
  DLLs (System.Memory and others), so 68 of 76 fail until they're present in `bin` (fv). The local gate passes
  because `bin` is already populated. Convert the project to SDK style in a follow-up.
- **SQLite base without a `rowid` (a view or a `WITHOUT ROWID` table) with exactly one remote join:** unordered
  paging now fails (`no such column`) instead of paging by the joined table's `rowid`, a meaningless order. With
  no joins or with two or more, it already failed in 3.9.0 (gx; scope corrected by v3 N3). Recorded in the
  Changelog.
- **PostgreSQL enum parameters (pre-existing; v4).** `Where(x => x.Kind == k)` throws `InvalidCastException`
  ("Writing values of '…' is not supported for parameters having NpgsqlDbType 'Integer'") on 3.9.0 and on this
  branch. Reading enum columns works. Follow-up issue.
- **`.HasValue` on a value not read from the row (pre-existing; v7, executed on SQLite; same on 3.9.0).** In an
  ORDER BY ternary, `X.HasValue ? … : …` where `X` is a captured or static property is translated as a column of
  the entity named after `X`. The order is silently wrong when such a column exists, and the query fails when it
  doesn't. Candidate fix: take the `HasValue` branch only when the member is read from the lambda parameter.
  Follow-up issue.
- **Checked-arithmetic projects (pre-existing; v4–v6):** in an ORDER BY ternary, a comparison whose column side the
  compiler converts with checked arithmetic throws: `char`, `byte`, `short`, and `int` against `long` (executed).
  The column side becomes `ConvertChecked`, which `BuildValueSql` doesn't unwrap. Same on 3.9.0.
- **MySQL `Delete<T>(predicate)` on a cold column cache** throws "Expression type Parameter is not supported" for
  an inherited member (`PersonBase.LastName`). The same happens on `master`, so it's pre-existing (v3). Effect on
  this branch: `MySqlOrderByQualificationTests`' two `ProjectScorecard` tests fail when run alone, in cleanup; they
  pass with their class. *(Fixed: `fix/mysql-delete-cold-cache`, `docs/plans/COLD_CACHE_DELETE_PLAN.md`, merged into
  `fix/provider-scoped-caches` at `f73823c`.)*
  - *(Rev 44, N1)* A MySQL harness warm-up of `Person` was added as a workaround. The merge removed it, so the three
    tests it was added for again exercise the fix: each passes alone, and fails alone with the fix's D1 removed.
- SQLite provider state isn't safe for concurrent execution from one root. Sequential reuse is fixed by D11.
- Unordered default paging hard-codes `id`, which is wrong for entities whose key column isn't `id`.
  `Single*` without user `Skip`/`Take` no longer routes through it (row limit). With user paging it still
  does, like any paged query. A resolved-PK default order is a follow-up issue.
- **NULL placement in ORDER BY is the database's (pre-existing; documented, rev 22).** SQL Server, MySQL and SQLite
  sort NULLs first ascending, as LINQ-to-objects does; PostgreSQL sorts them last. `First`, `Last` and `ToList`
  all follow the database (AC13-2), so on PostgreSQL `OrderBy(nullable).Last()` can be a NULL-keyed row where LINQ
  gives another. Emitting `NULLS FIRST`/`LAST` on PostgreSQL is a follow-up candidate. Documented in `Advanced.md` §5.
- **SQL NULL semantics differ from C# (pre-existing; follow-up issue).** *(Rev 24, fv: this also applies to ORDER BY
  ternaries. `x.M != "A" ? 0 : 1` sends NULL rows to ELSE where LINQ sends them to THEN; executed, same on 3.9.0.)*
  - `x.M != v` emits `col <> @p`, which excludes NULL rows; C# includes them.
  - `All(pred)` is `NOT EXISTS(… WHERE NOT pred)`, which never counts a NULL row as a violation; C# does.
  - A WHERE comparison with a null held in a variable (`string none = null; Where(p => p.M == none)`) emits
    `= @p` and matches no row; C# matches the NULL rows (r67, executed). The ORDER BY ternary handles this since
    rev 22; the WHERE translator doesn't.

  EF Core compensates for this by default. FunkyORM doesn't, and the tutorial audience will expect it to.
  **Recommended for 3.11:** emit C#-style null semantics (`col <> @p OR col IS NULL`), behind an opt-out.
- **A predicate or ordering after a subset `Select` reads base columns (pre-existing; follow-up issue).** In
  `Select(p => new T { FirstName = p.FirstName }).Where(p => p.Id > 5)`, FunkyORM filters on `person.id`,
  while LINQ sees the projected default `Id = 0`. This only matters when the predicate reads a mapped member
  the projection leaves out. Candidate fix: reject lambda-bearing operators after a subset `Select` when the
  lambda reads an unprojected member.
- **Strong naming: deferred past 3.10.0 by the owner (2026-09-30).** Shipping assemblies stay
  `SignAssembly=False`. When signing is scheduled, the `InternalsVisibleTo` friends must be signed and their
  entries public-key-qualified. The new `IsAllowed` and `ScalarProjectionGuard` seams are public, so they
  add no `InternalsVisibleTo` dependency.
- **Interface-member column resolution (pre-existing).** Over an interface-typed source
  (`IQueryable<IFoo>`), a lambda referencing an interface member doesn't get the entity's mapped column.
  The column cache is keyed by `DeclaringType.Name.Prop` (GeneralExtensions.cs:52-56), so the member gets a
  fallback derived from its property name (spelling: r11-C2).
  - **SQL Server** (executed): members whose column is named differently fail with "invalid column"
    (`FirstName`, `LastName`, `EmployerId`, `DateUtcCreated`; J21, S8/S9, r11-A1). `Id` and `Gender` work
    in `OrderBy*` and a settable scalar `Select` (J19/J20, r9-I1, r10-S1). A scalar `Select` of a get-only
    member is rejected by the `Select`-shape guard (r9-I2).
  - **SQLite** (executed): both spellings occur, depending on order. `Id` gives results equal to the
    concrete query in both orders (r12-L3, r13-L1).
  - **PostgreSQL, MySQL:** not probed beyond Task 1's `Id` rows, which cover whichever cache order the
    suite produces.

  On SQL Server it's loud, not silent, and it affects only interface-typed queries. String `Min`/`Max` selectors are the
  separate D9 result-type issue (above), on any source. Follow-up issue candidate; not in 3.10.0.
- **A generic helper constrained to an interface fails in every WHERE translator (pre-existing; T4R-1).**
  `static IQueryable<T> UpTo<T>(this IQueryable<T> q, int max) where T : IHasId => q.Where(x => x.Id <= max)`
  compiles to `Convert(x, IHasId).Id`. All four WHERE visitors expect a bare parameter there and throw
  `NotSupportedException: Expression type Parameter is not supported`; 3.9.0 behaves the same (executed). A
  base-class constraint works, so the I1 message gives the generic-helper advice for a base class only. Real
  generic repositories hit this. Follow-up issue candidate: strip a reference `Convert` of the lambda parameter
  in member resolution. Not in 3.10.0.
- **Static identifier caches are shared across providers (pre-existing; r8 incidental).** `_tableNames`,
  `_columnNames` and `_mappedTypes` are `static` on the Core `OrmDataProvider`. Using the same entity type
  with two providers in one process makes the second emit the first provider's quoting (PostgreSQL emitted
  `FROM [User]`). It matters for multi-provider apps. *(Fixed in 3.10.0: `docs/plans/PROVIDER_SCOPED_CACHES_PLAN.md`.)*
- **`is IOrderedQueryable` sort helpers crash on composed queries** (pre-existing; §1.5 P4b). Tracked as
  [#16](https://github.com/FuncularLabs/Funcular.FunkyOrm/issues/16) (owner: follow-up, not 3.10.0).
  Candidate fix: `CreateQuery` returns an ordered queryable only when `expression.Type` is
  `IOrderedQueryable<>`.
- **SQLite binds WHERE `Guid` and `DateTime` values in the driver's formats (pre-existing; Task 12 review F1).**
  FunkyORM stores a Guid as lower-case `D` text and a date as `yyyy-MM-dd HH:mm:ss.fff`
  (`SqliteDialect.CreateParameter`). The WHERE generator binds the raw value, which Microsoft.Data.Sqlite writes in
  its own formats.
  - Executed by the reviewer and again at rev 31, with the WHERE path unchanged from `master`:
    - `Where(p => p.UniqueId == g)` finds no row when `g` has hex letters; a digit-only Guid matches.
    - `Where(p => p.DateUtcCreated == d)` at `.500` finds none.
    - `> d` includes the row equal to `d`.
  - Owner's call: fix before 3.10.0 or follow up. A follow-up would apply the dialect's conversions in
    `SqliteParameterGenerator`.
  - ORDER BY values (AC12-10) are bound in the storage format: `D` Guids, invariant
    `yyyy-MM-dd HH:mm:ss.fff` `DateTime`s, and `yyyy-MM-dd HH:mm:ss.fffffffK` `DateTimeOffset`s. That matches rows
    written under a Gregorian calendar with `:` time separators
    (see the culture entry below).
- **PostgreSQL WHERE timestamps depend on startup order (pre-existing; F2).** `PostgreSqlOrmDataProvider`'s static
  constructor sets `Npgsql.EnableLegacyTimestampBehavior`. Npgsql reads that switch once, at its own first use.
  - When an app uses Npgsql first (the test fixture's `TestConnection()` does), typed WHERE parameters reject a UTC
    `DateTime` (for `timestamp`) and a non-UTC `DateTimeOffset`.
  - ORDER BY values are untyped and unaffected.
  - Follow-up candidate: document that the switch must be set before Npgsql's first use, or set it earlier.
- **ORDER BY dates carry milliseconds (3.9.0's literal text, kept by rev 31).** A `DateTime` with sub-millisecond
  ticks compares with a `datetime2`/`timestamp`/`DATETIME(6)` column as its truncated value, as in 3.9.0; WHERE
  parameters keep the full value.
- **Literal mode (the visitors' 3.9.0 constructors, no generator; F6, report only).** No provider path passes values
  through it: `DefaultLastOrderBy` builds one, for `Id` only.
  - PostgreSQL with `standard_conforming_strings=off` treats a backslash in a literal as an escape, which is
    MySQL's problem.
  - MySQL with `NO_BACKSLASH_ESCAPES` keeps the doubled backslash, which changes the value but can't be exploited.
- **SQLite writes and reads dates in the current culture (pre-existing in 3.9.0; rev 33/34 reviewers, executed;
  the author confirmed the code by reading).** `SqliteDialect.CreateParameter` formats a `DateTime` as
  `yyyy-MM-dd HH:mm:ss.fff`, and a `DateTimeOffset` as `yyyy-MM-dd HH:mm:ss.fffffffK`, with no culture
  (`SqliteDialect.cs:241, 243`).
  - Under fi-FI, a date is stored as `2026-01-02 03.04.05.000`. Read back under en-US, it throws
    `FormatException`.
  - Under ar-SA, the stored year is Hijri. Read back under en-US, it silently becomes a year-1447 date. Read back
    under ar-SA it is correct, but a date written under en-US or fi-FI throws there.
  - WHERE and ORDER BY comparisons against such rows mismatch silently (same at `66bde1e` and HEAD).
  - Follow-up candidate, owner's call (before 3.10.0 or in 3.10.1): write *and* parse with `InvariantCulture`.
    Fixing the parse alone would leave the stored values wrong. Databases already written under such cultures
    hold culture-formatted text, so the fix needs a tolerant read or a migration note. Three docs say dates are
    stored as ISO 8601: `README.md` (SQLite type affinity), `FUNKYORM_AI_INSTRUCTIONS_SQLITE.md` and
    `FUNKYORM_AI_INSTRUCTIONS.md` (date storage). That is true only under a Gregorian calendar with `:` time
    separators; correct them with the fix.
- **SQL Server: a ternary whose test reads no row and isn't a parameter is a constant test (pre-existing; rev 34–36
  reviewers, executed on `66bde1e` and HEAD; restated in revs 36–37).** What decides the outcome is the branch the
  constant test selects. The executed shapes, with `n = 5` and `five = 5`:
  - **A row-reading branch is selected, and it works:** `n == null ? p.LastName : p.FirstName`.
  - **A constant branch is selected, and it fails with error 408** ("A constant expression was encountered in the
    ORDER BY list") on both shas: `n == null ? p.Id : 0`, `five < 3 ? p.Id : 0`, `five > 3 ? 0 : 1`.
  - **A quoted branch is selected** (`five > 3 ? "a" : "b"`, `n == null ? p.FirstName : "z"`). It is a parameter at
    HEAD and fails with error 1008 instead of 408.
  - **A quoted value compared with `null`:** a non-null one works at HEAD (`s == null ? "a" : "b"`).
  - **A variable holding `null`** still emits `NULL IS NULL`, a constant test, so the selected branch decides
    (with `sn = null`):
    - `sn == null ? "a" : "b"` fails with 1008 at HEAD and with 408 on `66bde1e`.
    - `sn == null ? 0 : 1` fails with 408 on both.
    - `sn == null ? p.FirstName : "z"` works on both.

  No shape that worked on `66bde1e` fails at HEAD.

  **Follow-up candidate:** decide a test that reads no row in .NET, then:
  - if the selected branch reads the row, emit it;
  - otherwise drop the term (a constant key never changes the order).

  Emitting a constant branch would not help, executed: `ORDER BY 0` is error 108, `ORDER BY N'a'` is 408, and
  `ORDER BY @p` is 1008. An integer constant before a `ThenBy` sorts by a select-list position instead.
- **SQLite can't read back a `DateTimeOffset` property (pre-existing).** The rev 36 reviewer, executed at HEAD:
  `InvalidCastException` from text to `DateTimeOffset` in the reader's `Convert.ChangeType`
  (`SqliteOrmDataProvider.cs`). That file is unchanged since `master` (author, `git diff`). Follow-up candidate,
  with the culture entry above.
- **SQL Server `DateTime` under a day-first session (pre-existing; rev 39 reviewer, executed).**
  - The scenario: `yyyy-MM-dd HH:mm:ss.fff` sent as `varchar` against a `datetime` column, under
    `Current Language=British English`.
  - It is read day-first: WHERE picked rows c,d and ORDER BY picked a,b for the same comparison.
  - The same happens at `66bde1e`, so AC12-10's "as 3.9.0" holds.
- Sentinel.MVP pins `3.9.0-beta1`; the upgrade is the D3 smoke test.

---

## 9. Review dispositions

Blame classes are evaluated in order; the first that applies wins.

### 9.1 Task 0 test-plan review r1 of `30d3a6d`

Totals: AC-GAP 10, TEST-GAP 11, HOUSE-RULE 0, PLAN-GAP 4, OTHER 0.

| # | Sev | Blame | Finding (short) | Disposition |
|---|---|---|---|---|
| 1 | BLOCKER | AC-GAP | Operators after `Skip`/`Take` mistranslated | D8, AC13-10, positional guard (Task 4). |
| 2 | MAJOR | AC-GAP | `Single(pred)` after `Skip` can't meet AC13-1 | Parameterless-only composition; predicate form rejected (D8). |
| 3 | MAJOR | AC-GAP | Second `OrderBy` reverses priority | D10 → reject (rev 3 redesign), AC13-12. |
| 4 | MAJOR | TEST-GAP | Unlogged schema discovery; "no SQL" unobservable | AC13-4 reworded; D2 corrected; killer replaced. |
| 5 | MAJOR | AC-GAP | `Single*` via paging injects `ORDER BY id` | Row-limit design; no-`id` entity test. |
| 6 | MAJOR | AC-GAP | `Single*`/`Last*` + `Distinct` projection | Row limit; `Last` rule in AC13-2. |
| 7 | MAJOR | AC-GAP | Aggregate overload bugs | D9 → 3.10.1 (§10); known issues documented. |
| 8 | MAJOR | AC-GAP | SQLite projection-state leak | D11 full reset, AC13-13. |
| 9 | MAJOR | TEST-GAP | `Cast` tests toothless; identity used the entity type | Source-element identity; hand-built node and unrelated-type tests. |
| 10 | MINOR | PLAN-GAP | Non-generic overloads | `IsGenericMethod` guard + tests. |
| 11 | MINOR | TEST-GAP | AC13-8 unprovable | Restated with precedence; distinctive-text asserts; sibling tests. |
| 12 | MINOR | TEST-GAP | `Single*`/`Last*` after scalar | Test added. |
| 13 | MINOR | TEST-GAP | Missing mutation rows | Added. |
| 14 | MINOR | TEST-GAP | Whole-tree walk ambiguity | Spine-only walk specified; tests. |
| 15 | MINOR | TEST-GAP | Oracle determinism | Composite-key rule; exclusions. |
| 16 | MINOR | TEST-GAP | Tautological sweep | `IsAllowed` seam vs literal set. |
| 17 | MINOR | AC-GAP | net48/net9 not gated | AC13-7 on three TFMs. |
| 18 | MINOR | PLAN-GAP | Red tests won't compile | Stubs in Task 1. |
| 19 | MINOR | PLAN-GAP | Unreachable visitor branches | DB-free direct tests. |
| 20 | MINOR | AC-GAP | Ternary `= NULL` | D11, AC12-8. |
| 21 | MINOR | TEST-GAP | No-`Id` `Last` untested | Test added. |
| 22 | MINOR | TEST-GAP | `LongCount` reverse-key / `COUNT_BIG` | Tests added. |
| 23 | MINOR | TEST-GAP | AC12-1 combinations; AC12-6 entity | 24-row test; join entity. |
| 24 | MINOR | AC-GAP | `Take(0)` + `Single*` | Empty-`Take` short-circuit. |
| 25 | MINOR | PLAN-GAP | Affected-version wording | Wording fixed; range verified. |

### 9.2 Task 0 fix-verification r2 of `e19a7ae`

Totals: AC-GAP 5, TEST-GAP 7, HOUSE-RULE 1, PLAN-GAP 8, OTHER 0. 19 of 21 were introduced by rev 2; only F11
and F19 weren't. Rev 3 originally said 16; corrected in rev 4 (N10a).

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| F1 | MAJOR | AC-GAP | yes | D10 stable-sort rule wrong; ordering dropped across `Where` | **Redesigned:** D10 = reject (owner). AC13-12 covers the across-`Where` shapes. |
| F2 | MAJOR | TEST-GAP | yes | D10 tests blind to tie-breakers | Moot after the redesign; rejection tests have no tie semantics. Composite-key oracle rule kept. |
| F3 | MINOR | PLAN-GAP | yes | NULL-result rule order (`Sum` → 0; `string` → null) | → 3.10.1 input (§10). |
| F4 | MAJOR | AC-GAP | yes | `ChangeType` fails for enum/Guid/DateTimeOffset/TimeSpan | → 3.10.1; owner direction recorded (§10). |
| F5 | MAJOR | TEST-GAP | yes | `Average` precision (SQLite `ROUND`, MySQL scale 4, decimal scales); {1,2} undiscriminating | → 3.10.1 input: SUM/COUNT design, {1,1,2} rows. |
| F6 | MAJOR | PLAN-GAP | yes | Aggregate columns missing from schemas; effort undercounted | → 3.10.1 (new test table). 3.10.0 Task 1 names the scripts for the no-`id` entity; effort re-estimated. |
| F7 | MAJOR | AC-GAP | yes | Identity `OfType` over nullable elements keeps nulls | D5 refined; `OfType_Identity_OverNullableScalar_Rejected`. |
| F8 | MINOR | PLAN-GAP | yes | Spine walk underspecified | §5.2.1: `IEnumerable<>` element type, root terminal, classify-first, `TSource` check; tests. |
| F9 | MINOR | TEST-GAP | yes | #12 tests pass by alias binding when the key is projected | Subset rows omit the key; SQL-text asserts; AC12-5 reworded. |
| F10 | MINOR | PLAN-GAP | yes | `Single*` after `Skip`-only; SQLite `OFFSET` without `LIMIT` | `min(Take ?? 2, 2)`; AC13-14 `LIMIT -1 OFFSET`. |
| F11 | MINOR | AC-GAP | no | Negative `Take`/`Skip` | D8: `Take(n ≤ 0)` empty; `Skip(n < 0)` → 0; tests. |
| F12 | MINOR | TEST-GAP | yes | `Take(0)` location ambiguous; scalar path | Flag + check in `Execute` and `ExecuteScalarProjection`; rows for subset, scalar, `Skip.Take(0)`, OrDefault, Single. |
| F13 | MINOR | TEST-GAP | yes | "ThenBy after paging" row unbuildable; missing allowed rows; D8/AC misaligned | Row → `OrderByDescending`; `OfType` + `Skip`-only `First` rows; aligned on `OfType`. |
| F14 | MINOR | TEST-GAP | yes | Inherited order leaks only on the bare root; reset list incomplete | Bare-root tests; `ThenLast_UsesIdDesc`; full reset; mutation row. |
| F15 | MINOR | TEST-GAP | yes | String `Min`/`Max` rows vs collation rule; float tolerance | → 3.10.1 (aggregate rows moved). |
| F16 | MINOR | PLAN-GAP | yes | net48 C# 7.3 and missing APIs | §4.1 rule (by-name reflection / `#if`). |
| F17 | MINOR | PLAN-GAP | yes | "Framework-aware" pinned set reintroduces the tautology | One literal set on every TFM; §7 fixed. |
| F18 | MINOR | HOUSE-RULE | yes | Dead scalar-aggregate guard and stale comment after the pre-pass | Removed and fixed in Task 4. Already covered by taxonomy pattern 16 (comment/contract drift), so no new house-test row. |
| F19 | MINOR | PLAN-GAP | no | Repeated ORDER BY column (SQL Server 169) | Duplicate removal, AC12-9. |
| F20 | MINOR | PLAN-GAP | yes | Consistency (Task 0 text, AC12-5 mapping, AC13-5 unmapped, AC13-9 executable, `void` pre-pass, §1.4 SQLite facts) | All corrected. AC13-5 is a gate on every task. |
| F21 | MINOR | AC-GAP | yes | `null == x.M` operand order | AC12-8 covers both orders. |

### 9.3 Task 0 fix-verification r3 of `e48f2b1`

Totals: AC-GAP 3, TEST-GAP 4, HOUSE-RULE 0, PLAN-GAP 3, OTHER 0. 8 of 10 were introduced by rev 3; only N3 and
N8 weren't. The r3 reviewer also confirmed that all §9.1 dispositions still hold, apart from the net9 leg of
§9.1 #17 (now N3). It marked F1–F21 as 17 RESOLVED / 4 PARTIAL. The PARTIALs are F8, F10, F12 and F13, now
N1, N4, N5 and N10b. *(Rev 3 read "16 / 5"; corrected in rev 5, F7c.)*

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| N1 | MINOR | AC-GAP | yes | `TSource` check rejects covariant parameterless terminals that work in 3.9.0 | Exempted parameterless terminals (§5.2.1); `Covariant_ParameterlessTerminal_NotRejected_MatchesOracle`; `Covariant_Take` rejection row; two mutation rows. |
| N2 | MAJOR | AC-GAP | yes (claim) / pre-existing behavior | A `ThenBy*` cast past a `Where` drops the earlier ordering | D10 extended (same class); AC13-12 rows including the cast shapes; `ThenBy_WithNoEarlierOrdering_IsPrimaryOrder`; two mutation rows. **Superseded in rev 6 (R5-2):** the premise was never executed, and the shape throws `ArgumentException` in System.Linq (§1.5 P3), so it was reverted. |
| N3 | MINOR | PLAN-GAP | no | net9 project is xUnit; net48 project unnamed | xUnit twin plus a shared literal-set source file; net48 project named; §4.6 corrected. |
| N4 | MINOR | TEST-GAP | yes | AC13-14 misstated `Single`; the cap mutation survived | AC13-14 reworded; the cap is asserted in SQL; mutation row fixed; SQLite `SkipOnly_EmitsLimitMinusOneOffset`. |
| N5 | MINOR | TEST-GAP | yes | Empty-`Take` vs scalar guard order ambiguous | §5.2.6 pins the order; `ScalarProjection_Take0_First_ThrowsScalarGuard_NoQuery`; mutation row. |
| N6 | MINOR | TEST-GAP | yes | D10 `Select` row vacuous if scalar; precedence unpinned | Rows use a subset `Select`; D10 and "after Skip/Take" messages asserted per row. |
| N7 | MINOR | TEST-GAP | yes | Oracle rows break on NULL semantics | Oracle rule: non-null predicate members. Divergence recorded in §8 with a 3.11 recommendation. |
| N8 | MINOR | AC-GAP | no | Predicate after subset `Select` reads base columns | Oracle rule: none after a subset `Select`. Recorded in §8 as a follow-up with a candidate fix. |
| N9 | MINOR | PLAN-GAP | yes | §10 `time`-range claim wrong | Reworded per provider (§10). |
| N10 | MINOR | PLAN-GAP | yes | Bookkeeping: 16→19; `OfType` after paging; §8 `Single` wording; `Id DESC` qualification; §1.4 premise | All corrected. `Skip(n).Select(p => p.Id).OfType<int>()` row added. |

### 9.4 Task 0 fix-verification r4 of `7e97e93`

Totals: AC-GAP 2, TEST-GAP 3, HOUSE-RULE 0, PLAN-GAP 2, OTHER 0. 5 of 7 were introduced by rev 4; only F5 and
F6 weren't. The reviewer judged that none blocks Task 1, on one condition: the covariance rule is redesigned,
not patched, before the AC13-4 rows are written. It marked N1–N10 as 8 RESOLVED (some with follow-on findings)
and 2 PARTIAL (N8 → F5, N10 → F7b).

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| F1 | MAJOR | AC-GAP | yes (2nd in a row on this rule) | Covariance exemption lets `Select(scalar).Cast<object>().First()` return the whole list | **Redesigned:** cell table (§5.2.1), with the exemption limited to entity-typed sources. Second-layer terminal guard in `ExecuteScalarProjection`. One row per cell, including `Covariant_ScalarSource_Rejected_NoQuery` (red on 3.9.0). Mutation rows; AC13-4 restated. **Revised in rev 6 (R5-1, R5-3):** rows re-spelled, the scalar sequence cell corrected, and the guard moved to a testable Core helper. |
| F2 | MINOR | AC-GAP | yes | D10 and `TSource` messages wrong for the shapes routed to them | Separated-`ThenBy` and covariant-mismatch message variants; rows assert the distinctive text; mutation row. **Revised in rev 6:** the separated-`ThenBy` message was removed as unreachable (P3); the covariant message was split into entity and scalar variants (R5-6). |
| F3 | MINOR | TEST-GAP | yes | No `ThenBy`-after-paging rows; D8/D10 precedence unpinned | Rows added (with and without an earlier ordering; combined `OrderBy.Skip.OrderBy`/`ThenBy`). D8 is evaluated before D10 (§5.2.1). Mutation row. **Revised in rev 6:** the `ThenBy`-after-paging rows were removed because they can't be built (P3b); `OrderBy.Skip.OrderBy` is kept as the precedence killer. |
| F4 | MINOR | TEST-GAP | yes | Literal `LIMIT 2 OFFSET n` fails on PG/SQLite line breaks | §4.1: normalize whitespace before SQL-shape asserts. |
| F5 | MINOR | TEST-GAP | no | AC12-1/AC12-5 subset rows don't fix operator order | AC12-1/AC12-5 require ordering and paging before the projection. The §4.1 rule binds all passing shapes. |
| F6 | MINOR | PLAN-GAP | no | AC13-5 over-claims (D8 rejected the correct `Skip.Select.Take`); AC12-3 over-claims byte-identity | **D8 refined:** `Take` after `Skip` with only `Select`/identity `OfType` between is allowed, with new allowed rows and a mutation. AC13-5 restated (rejected shapes were all wrong in 3.9.0; Changelog enumerates them). AC12-3 narrowed to the ORDER BY translation. |
| F7 | MINOR | PLAN-GAP | yes | Stale name in §4.3; §5.2.4 `Id DESC` wording; §9.3 arithmetic; covariant rows mapped to Task 4 | All fixed. Covariant `Single`/`Last`/`LongCount` rows mapped to Tasks 5/7/8. |

Author's own check while applying rev 5: a drafted mutation row, "D8 allows `Take` after `Skip` with any
operator between", was **vacuous and was removed**. Any operator other than `Select`/`OfType` after `Skip` is
rejected at its own node first, so that path can't be reached.

### 9.5 Task 0 fix-verification r5 of `dfeae2e`

Totals: AC-GAP 1, TEST-GAP 7, HOUSE-RULE 0, PLAN-GAP 0, OTHER 0. All 8 were introduced by rev 5. Both majors
rested on unexecuted System.Linq premises: my §1.4 `Cast` claim, and r3 N2's `ThenBy` claim. **Process fix:**
§1.5 executed-probe evidence; every shape premise must cite it. The reviewer judged F1–F7 as 3 RESOLVED / 4
PARTIAL (F1, F2, F3, F6).

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| R5-1 | MAJOR | TEST-GAP | yes | `Queryable.Cast` always creates a node (§1.4 was false): scalar-cell rows unreachable as spelled; AC13-4 vs D5 conflict; D8 over-rejects identity `Cast` | §1.4 corrected with P1/P2/P2b. **Owner amended D5:** reference-conversion `Cast` is transparent, judged on the effective element type; generalized `isCollection` (P14 fix). Covariance rows spelled both ways; killers re-pointed. D8 lists include D5 casts. |
| R5-2 | MAJOR | TEST-GAP | yes | `ThenBy*` after any non-ordering call throws `ArgumentException` in System.Linq: D10 clause 2, its message, 5 rows and 2 mutations unreachable | Executed (P3, P3b, P4, P4b). D10 clause 2, the message, the rows and the mutations were deleted. `ThenBy_OnRoot_IsPrimaryOrder` re-spelled on the root. AC13-12 helper sentence fixed. The helper crash is filed as #16 (owner: follow-up). §9.3 N2 marked superseded. |
| R5-3 | MINOR | TEST-GAP | yes | The second-layer guard can't be reached through `Queryable`; its test was vacuous | Moved into the Core public `ScalarProjectionGuard`, tested directly (throw and pass cases). §4.3 row added. The plan states the layer is unreachable via LINQ. |
| R5-4 | MINOR | TEST-GAP | yes | Rules reject some shapes 3.9.0 answered correctly; AC13-5 / "holds again" over-claimed | AC13-5 restated: rejection is by rule, the Changelog lists the examples. Scalar covariant `Skip`/`Take`/`Distinct` are **kept allowed** after executing P7c/P7d. "Holds again" dropped. |
| R5-5 | MINOR | TEST-GAP | yes | Covariance after paging untested; rule order unpinned; root element type needs a second pass | AC13-4: "after `Skip`/`Take`, D8 governs". `Covariant_EntitySource_AfterPaging_D8Governs` rows. Pass-2 order pinned (allow-list → D8 → D10 → D5/covariance). Two-pass walk specified. Mutation row. |
| R5-6 | MINOR | AC-GAP | yes | Covariant message gives wrong advice for scalar/other sources; no "other projected type" row | Entity and scalar message variants; `Cast<…>` dropped from the advice; "other projected type" defers to the `Select`-shape guard, with a row. |
| R5-7 | MINOR | TEST-GAP | yes | AC12-3 still over-claimed | Exceptions now include AC13-2 and AC13-12; scoped to `OrderBy*/ThenBy*` chains consumed by enumeration or `First*`. |
| R5-8 | MINOR | TEST-GAP | yes | §4.2 table broken by a blank line | The AC13-4 cell is one line with `<br>`. |

**Also from r5 (not counted):**
- The spine root must be the queryable's own root (adopted: `ForeignQueryableConstantRoot_Rejected`).
- F5 residual: AC12-6 and the `Last`/`Distinct` tests are spelled ordering-first (adopted).
- F7 residual: Task 4 mapping says "entity-source" (adopted).

**Author's own probes while applying rev 6** (cited in §1.5): P7c–P7f corrected the scalar covariance cell
before it was written; P14 showed that the owner's "like implicit conversion" choice needs the `isCollection`
generalization to hold for enumeration.

### 9.6 Task 0 fix-verification r6 of `44e78b4`

Totals: AC-GAP 1, TEST-GAP 7, HOUSE-RULE 0, PLAN-GAP 0, OTHER 0. All 8 were introduced by rev 6 (F3 partly
pre-existing). The r6 reviewer executed its premises (Q1–Q12, SQL Server), which are now cited in §1.5. It
marked R5-1..R5-8 as 4 RESOLVED / 4 PARTIAL (R5-2 → F7/F8a; R5-3 → F1; R5-4 → F2; R5-5 → F6).

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| F1 | MAJOR | TEST-GAP | yes (4th consecutive round on the covariance rule) | A converted scalar source plus an allowed `Skip`/`Take`/`Distinct` plus a terminal escapes the "effective element type" check; 3.9.0 returns the whole list (Q1a–e) | **Redesigned**: the covariance type-flow and cell table are removed; invariants I1–I3 (§5.2.1). I2 (expression-shape terminal detection in `ScalarProjectionGuard`) rejects every scalar terminal on every path and is pinned end to end by ‡ rows plus direct tests. Call-site mutation row added. |
| F2 | minor | AC-GAP | yes | Entity covariant `Skip`/`Take`/`Distinct` rejected though correct in 3.9.0 (Q3) | Preferred option taken: allowed. They're lambda-free, so I1 doesn't apply; translation is type-agnostic. Enumeration is fixed by I2. Rows flipped to oracle rows. |
| F3 | minor | TEST-GAP | yes (partly pre-existing) | Type-based `isCollection` misclassifies `T : IEnumerable<X>` terminals; empty-`Take` collection branch could return an empty list as "first" | I2 uses `typeof(IQueryable).IsAssignableFrom(expression.Type)` in `Execute`, the empty-`Take` branch and the guard. `Take0_CastObject_*` rows plus a mutation row. |
| F4 | minor | TEST-GAP | yes | Only `Cast<object>` rows; D5 static vs effective ambiguity | D5 judged against the row type `R` (I3). Base-class (Q4c), interface (Q4d), cast-back (Q4a) and `OfType`-after-cast (Q4b) rows. MySQL marker interface in Task 1. Mutation rows. |
| F5 | minor | TEST-GAP | yes | AC13-10 excluded scalar reference `Cast` after paging | Wording judged against `R`; Q7 allowed row. |
| F6 | minor | TEST-GAP | yes | AC13-8 per-node order contradicted the two-pass allow-list | AC13-8 restated (pass 1 whole spine, outermost wins; pass 2 per node). `Rejected_AllowListFailureBeatsPass2Failure` plus a mutation row. |
| F7 | minor | TEST-GAP | yes | `ThenBy_OnRoot` couldn't tell primary order from no order (Q10) | Uses `ThenByDescending(p => p.Id)`; mutation row "root `ThenBy` dropped". |
| F8 | minor | TEST-GAP | yes | Stale text: §5.2.4 rev 4 `ThenBy` attribution; Task 1 UNVERIFIED list; §4.4 "169 to be confirmed"; "(whole list)" parentheticals; Task mapping; §1.4 over-broad; uncited premises | (a) attributed to D10 + P3/P4; (b) Task 1 defers to §1.5's list; (c) cites P8; (d) the old covariance mutation rows were replaced and all rows assert messages; (e) Q7/`Take.Cast` rows mapped to Task 4; (f) §1.4 precise; (g) r6 Q-probes added to §1.5. |

**Author's own probes while applying rev 7** (§1.5 I1a–i) established I1's premise before it was written. One
self-correction: the first draft of I3 widened `OfType` to reference conversions, beyond the owner's
amendment, and was narrowed back to identity before commit. Another: a drafted I1 mutation killer used
`Take(3)`, where D8 preempts, and was replaced by the `Distinct()` row.

### 9.7 Task 0 fix-verification r7 of `765792a`

Totals: AC-GAP 1, TEST-GAP 3, HOUSE-RULE 1, PLAN-GAP 1, OTHER 0.
- **Fix-introduced:** #1 (the claim was new; the rejection itself came from the rev 3–6 rule), #2, #3, #4.
- **Pre-existing:** #5 and #6.

The reviewer executed J/K/L/M/N probes (SQL Server), now in §1.5. F1–F8 of r6: all RESOLVED.

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| 1 | MAJOR | AC-GAP | yes (claim) | "Every lambda over a converted source fails in 3.9.0" is false: base-class/interface `OrderBy*`/selector aggregates/scalar `Select` are correct (J1–J20) | **Owner: reject only where 3.9.0 fails.** I1 narrowed to the hard-cast predicate operators. False statements corrected (§1.4, §1.5, §5.2.1, AC13-4). Rows: base-class non-predicate (must stay green), base-class and `object` predicate (I1 message), base-typed paging (N3 → I2). Mutation rows for "I1 strict" and "I1 omits an operator". The I1 message suggests a generic helper. |
| 2 | minor | TEST-GAP | yes | I1 masked the `Select`-shape message after a DTO `Select`; I1h was used by two rows with different expectations; the scalar predicate was unspecified | I1 defers after any non-subset `Select`; predicates are identical to the parse loop's. `OtherProjectedSource` row re-spelled as L1. I1h no longer in the I1 row (`Select` is out of I1's scope). |
| 3 | minor | TEST-GAP | yes | ‡ cross-product contradicted D8 and the Distinct+aggregate guard; scalar `Count`/`Any` mislabeled red | ‡ defined per operator. `Count`/`Any` scalar rows are regression rows. AC13-4 I2 says "every terminal that reaches execution". `s.Distinct().Last()` precedence pinned (scalar guard wins; the Last/Distinct check is in `BuildQueryComponents`). |
| 4 | minor | TEST-GAP | yes | Existing composition tests can't kill "I1 outer to scalar Select" | The killer asserts "must be the outermost query operator"; existing tests are dropped from the killer list. |
| 5 | minor | HOUSE-RULE (pattern 4) | no (pre-existing; I2's "every path" claim new) | Non-generic `Execute(Expression)` returns the whole list for `First` (K1) | Fixed: dispatch by expression shape. AC13-15 + 3 rows + a mutation row; mapped to Task 4. |
| 6 | minor | PLAN-GAP | no | Type-filter wording contradicted D5/I3 | Reworded; mutation says "instead of `R`". |

**Author self-check while applying rev 8:** a drafted Task 1 line had the SQLite helper load only
`integration_test_schema.sql`, which lacks `organization`/`country`. It was corrected to combine that script
with the existing join-table DDL before commit.

### 9.8 Task 0 fix-verification r8 of `3091544`

Totals: AC-GAP 2, TEST-GAP 7, HOUSE-RULE 0, PLAN-GAP 0, OTHER 0. 8 of 9 were introduced by rev 8; #7 was
pre-existing. r7 #1–#6: 4 RESOLVED, 2 PARTIAL (#1 → new #1/#3). The reviewer found no I1-scope operator that
works over a base-class source (S1–S7 executed), so the narrowing has no over-rejection.

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| 1 | MAJOR | AC-GAP | yes | "Non-predicate lambdas over an interface source are correct" holds only for `Id`; other members fail on a pre-existing column bug | Claims narrowed (§1.4, AC13-4, §5.2.1); interface rows limited to `Id`; J21/S8/S9 in §1.5; bug recorded in §8. Citations corrected (J11 and J5/6/14/15 removed from the "correct" lists). |
| 2 | minor | TEST-GAP | yes | `Average` in an oracle row contradicts AC13-5 and is red | "Left alone" rows compare with the concrete query (`UnchangedFromConcrete`), independent of D9. |
| 3 | minor | AC-GAP | yes | `Single*(pred)` silently returns an unrelated row (not `InvalidCastException`); `LongCount` fails on the result cast | AC13-4, §5.2.1 and the row red-reasons reworded; S1–S7 in §1.5. |
| 4 | minor | TEST-GAP | yes | I1 rows missing `Last*`/`FirstOrDefault`/`SingleOrDefault(pred)`; composition row operator unspecified | Rows added; composition row uses `Where`/`Count(pred)`. |
| 5 | minor | TEST-GAP | yes | Base-class/interface rows unbuildable (no member interface; MySQL has no base class) | `IHasPersonId` (`Id`, `LastName`) in all four; MySQL `PersonBase`; MySQL source named in the matrix; behavior-neutral check in Task 1. |
| 6 | minor | TEST-GAP | yes | Local `dbo.User` has `id` | **Superseded by the author:** `DocumentationGapTests` owns and recreates `[User]` with `Id` on every setup, so recreating it is futile. The no-`id` test owns a `single_probe` table. |
| 7 | minor | TEST-GAP | no | On SQLite, `ORDER BY rowid` survives the paging mutation | "No ORDER BY" SQL-shape assert named as the SQLite killer; mutation row annotated per provider. |
| 8 | minor | TEST-GAP | yes | Non-generic dispatch via `MethodInfo.Invoke` wraps exceptions | `ExceptionDispatchInfo` rethrow of the inner exception; rows assert exact types (`InvalidOperationException`, `NotSupportedException`); `Count` → `int` row; mutation row. |
| 9 | minor | TEST-GAP | yes | ‡ produces subset-`Select` `Distinct().Last()`, which AC13-2 makes throw | Excluded from ‡; covered by `Last_AfterDistinctProjection_NoOrder_ThrowsNamingLast`. |

**Recorded follow-ups from r8** (§8): interface-member column resolution; static identifier caches shared
across providers (the reviewer filed a suggested-task chip for this one; fixed in 3.10.0 by the provider-scoped caches
plan).

### 9.9 Task 0 fix-verification r9 of `8d5ac7a`

Totals: AC-GAP 0, TEST-GAP 2, HOUSE-RULE 0, PLAN-GAP 2, OTHER 0. 3 of 4 were introduced by rev 9; F3 was
pre-existing. r8 #1–#9: 7 RESOLVED, 2 PARTIAL (#1 → F1, #5 → F3). #6 superseded, with its rationale
verified. The reviewer re-read every §3 AC and found them accurate apart from F1 and F4.

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| F1 | minor | PLAN-GAP | yes | Interface wording false both ways: `Gender` works; get-only members fail with the `Select`-shape exception, not a DB error | Reworded in §1.4, AC13-4 and §5.2.1 as "unchanged", with r9 evidence. |
| F2 | minor | TEST-GAP | yes | `single_probe` DDL not identity (INSERT omits `int` keys); SQLite `INT PRIMARY KEY` isn't a rowid alias; the "only killer" under-claim | Identity DDL per provider; SQLite `INTEGER PRIMARY KEY`; kill note corrected (`Single_AfterDistinctProjection_Works` also kills). |
| F3 | minor | TEST-GAP | no | `Cast<INotifyPropertyChanged>` row unbuildable on MySQL | Spelled per provider (`Cast<IHasPersonId>`; `Cast<PersonBase>` on MySQL). |
| F4 | minor | PLAN-GAP | yes | r8 probe IDs collided with r7 IDs (M1, M3, L1) | r8 IDs renamed; AC citations prefixed `r7`. |

### 9.10 Task 0 fix-verification r10 of `830b707`

Totals: AC-GAP 0, TEST-GAP 0, HOUSE-RULE 0, PLAN-GAP 1, OTHER 0 (plus 6 non-blocking nits). F1–F4 of r9 are
all RESOLVED. The reviewer executed the new rev-10 premises (SQL Server interface probes; SQLite key
behavior) and found them true. It re-read every §3 AC: "ACs safe to post: yes; Task 1 blocked: no".

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| R10-1 | minor | PLAN-GAP | yes | Rev-10 premises cited a bare "r9" not recorded in §1.5; "r9 P1" collided with P1; SQL Server `single_probe` insert missing from "still unexecuted" | §1.5 r9/r10 table with unique IDs; citations replaced; "still unexecuted" extended. |
| N1–N6 | nit | — | — | Interface wording (get-only is separate from column naming); matrix rationale; evidence ranges; stale legend; "therefore"; AC13-6 example | All applied. |

### 9.11 Task 0 fix-verification r11 of `019e9b4`, plus the public-AC fidelity check

**Totals:** AC-GAP 1, TEST-GAP 0, HOUSE-RULE 1, PLAN-GAP 1, OTHER 0, plus 3 nits (all PLAN-GAP).

r11 replayed r9-I2 and r10-S1 on SQL Server and the SQLite key probes with the real provider; all held. It
confirmed that every r9/r10 ID resolves to exactly one §1.5 row and that no matrix or mutation row drifted.
Verdict: "ACs safe to post: no; Task 1 blocked: no".

The fidelity check compared the public AC drafts with §3. Its two must-fix items concerned the drafts only,
and both were applied. It also surfaced FC-5 below, which is a defect in §3 itself.

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| R11-1 | minor | HOUSE-RULE | yes | Interface sentence claimed `Gender` works in selector aggregates (`Max(Gender)` is unsupported on any source), dropped the settable-`Select` case, and cited a `Select` probe for "invalid column" in an `OrderBy`/aggregate sentence | Rewritten in all three places; r11-A1 and r11-C2 recorded. The house rule (§1.5) already covers it: the wording outran its probes. |
| R11-2 | minor | PLAN-GAP | yes | "(r10)" was a bare round citation, and "code-read only" contradicted §4.1's executed coverage | Rewritten with script citations and r9-C1; only SQL Server and PostgreSQL's `IDENTITY` spelling remain unexecuted. |
| R11-3 | nit | PLAN-GAP | yes | "Everything else is executed" was false once §1.5 had a code-read row, and the rule didn't admit code reads | Rule and Task 1 text amended. |
| R11-4 | nit | PLAN-GAP | no (extended) | The fallback isn't lowercased in every path, and "never filled" was wrong (the visitor caches the fallback) | §8 and "still unexecuted" corrected (r11-C2). |
| R11-5 | nit | PLAN-GAP | no | AC13-6 cited only Q4d; duplicate Task 0 line | r9-I2 added; duplicate removed. |
| FC-5 | minor | AC-GAP | no (rev 3) | AC13-10: "`*OrDefault` returns `null`" after `Take(0)`, read literally, covers a scalar projection, but §5.2.6 and the matrix make the scalar guard throw | AC scoped to the entity path; the scalar case points to I2/§5.2.6. The test already existed. |

### 9.12 Task 0 fix-verification r12 of `b846f9a`, plus the public-AC drafts

**Totals:** AC-GAP 0, TEST-GAP 1, HOUSE-RULE 2, PLAN-GAP 0, OTHER 0, plus 2 nits (PLAN-GAP).

r12 confirmed the following as RESOLVED:
- R11-2, with schema citations and coverage tests checked;
- R11-5;
- every new evidence ID.

It re-ran r11-L2 (SQLite) and r11-A1, r9-I2, r10-S1 (SQL Server). The drafts had no must-fix items; three
optional edits were applied.

**Process note.** The interface paragraph has had a fix-introduced finding in three consecutive rounds:
r10 N1, R11-1, and now R12-1/R12-2. Per the test-first rule "two fix-introduced findings in a row: stop,
redesign", rev 13 removes those claims instead of refining them. See the revision note.

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| R12-1 | minor | HOUSE-RULE | yes | "A string selector aggregate is unsupported on any source" generalized a SQL Server probe; false on SQLite. AC13-4/§5.2.1 still grouped aggregates with `Gender` | Paragraph collapsed to the `Id`-only claim. String `Min`/`Max` stays D9 (§8). r12-L3 recorded. |
| R12-2 | minor | HOUSE-RULE | yes | r11-C2's "`Id` → `id` on all four" was wrong (`Id` is emitted on PG/MySQL/SQLite aggregates, and the spelling is order-dependent) and was a database claim backed only by a code read | Row corrected. PG/MySQL binding goes to Task 1's red run. The `GetColumnOrdinals` sub-nit was verified (no callers) and recorded. |
| R12-3 | minor | TEST-GAP | partly (FC-5) | AC13-10's `Take(0)` subset-projection outcomes and scalar `*OrDefault`/`Single*` outcomes had no named test | `ScalarProjection_Take0_Terminal_ThrowsScalarGuard_NoQuery` (4 rows, renamed from `…_First_…`); `Take0_SubsetProjection_Terminal_NoQuery`; mutation row widened. |
| N1, N2 | nit | PLAN-GAP | yes | Task 1's examples read as code reads; the §9.11 totals were miscounted | Both fixed. |

### 9.13 Task 0 fix-verification r13 of `d2d3386`, plus the public-AC drafts

**Totals:** AC-GAP 0, TEST-GAP 0, HOUSE-RULE 2, PLAN-GAP 0, OTHER 0. One of the two HOUSE-RULE findings is
in the draft, not the plan. Plus 3 nits: TEST-GAP 1, HOUSE-RULE 1, and one optional draft edit.

r13 verified R12-1/2/3 and the nits RESOLVED. It also confirmed:
- the `GetColumnOrdinals` sentence (no callers; every other `_columnNames` key is dotted);
- that the collapse orphans nothing.

It ran one SQLite probe (r13-L1).

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| R13-1 | minor | HOUSE-RULE | yes | "Still unexecuted" dropped SQLite, implying the r6–r11 premises ran there | SQLite restored; only its interface `Id` rows are executed (r12-L3, r13-L1). |
| R13-2 | minor (draft, must-fix) | HOUSE-RULE | yes | The AC13-4 draft reintroduced per-member detail removed in rev 13 ("invalid column" is SQL Server-only, and a get-only `Select` gets the `Select`-shape exception) | Draft now mirrors the collapsed text. |
| R13-3 | nit | TEST-GAP | no | AC13-10's converted-entity `Take(0)` outcomes are pinned only for `First`/`FirstOrDefault` | `Take0_CastObject_Terminal_NoQuery` over all four terminals; mutation row updated. |
| R13-4 | nit | HOUSE-RULE | yes | Citation precision: SQLite "equal" vs "binds"; AC13-4 citation; shared-cache spelling; unlabelled "loud"; heading | r13-L1 added; all five applied. |
| D-1 | optional (draft) | — | — | "Every terminal" over-broad: `Last*`/aggregates after `Take` get the paging message | Draft says `First*`/`Single*`. |

### 9.14 Task 0 fix-verification r14 of `0ce6cf1`, plus the public-AC drafts

**Totals:** AC-GAP 0, TEST-GAP 0, HOUSE-RULE 1, PLAN-GAP 0, OTHER 0, plus 4 nits (HOUSE-RULE 2, PLAN-GAP
1, TEST-GAP 1).

r14 verified R13-1/2/3 and D-1 RESOLVED, and R13-4 PARTIAL. Every cited ID resolves, and no database
claim rests on a code read alone.

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| R14-1 | minor | HOUSE-RULE | yes | The shared cache entry was described as SQLite-only. The aggregate path's `GetOrAdd(ToDictionaryKey())` on Core's static `_columnNames` shares it on all four, so the PostgreSQL/MySQL red run covers one cache order | Stated once in r11-C2 for all four (verified: OrmDataProvider.cs:32 plus the four aggregate lines). §8 and "still unexecuted" reference it; the one-order limit is recorded. |
| R14-2 | nit | HOUSE-RULE | no (missed sibling) | §5.2.1 still cited r12-L3 (binds) for "relied on" (equals) | Cites r13-L1. |
| R14-3 | nit | HOUSE-RULE | yes | "Only the interface `Id` rows are executed" on SQLite, but r12-L3 also ran `Min`/`Max(Gender)` | Reworded to "the interface shapes of r12-L3 and r13-L1". |
| R14-4 | nit | PLAN-GAP | no | AC13-10 said scalar "terminals throw"; only `First*`/`Single*` reach that point | §3 now names `First*`/`Single*`, `*OrDefault` included. |
| R14-5 | nit | TEST-GAP | no | `Take(0)` converted-entity outcomes pinned only for the `Cast<object>` spelling | `Take0_ImplicitObject_Terminal_NoQuery` added; mutation row uses `Name[row]`. |
| R14-D1 | optional (draft) | — | — | AC13-6 draft promised an `OfType` message text that §3 doesn't | Parenthetical dropped. |

### 9.15 Task 0 fix-verification r15 of `ef994af`

**Totals:** AC-GAP 1, TEST-GAP 0, HOUSE-RULE 0, PLAN-GAP 0, OTHER 0, plus 2 nits (HOUSE-RULE).

r15 verified R14-1, R14-2, R14-3, R14-5 and R14-D1 RESOLVED, and R14-4 PARTIAL. It re-checked every r11-C2
file:line. It confirmed that the cache is a single Core static, that the visitors receive it through
their constructors, that `DiscoverColumns` writes only concrete-type keys, and that `GetColumnOrdinals`
has no callers, tests included.

**Process note.** For four rounds, each new finding has come from a sentence the previous fix *added*.
Rev 16 therefore fixes by deleting text.

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| R15-1 | minor | AC-GAP | yes | AC13-10's "Other terminals after `Take` get D8's message first" contradicts AC13-8: allow-list rejections come first. Predicate `First*`/`Single*` get D8's message, not the scalar guard's | Sentence deleted; "parameterless" added. Precedence stays in AC13-8. |
| R15-2 | nit | HOUSE-RULE | yes | §8 claimed path- and order-dependence, which doesn't apply on SQL Server | Replaced with a pointer to r11-C2. |
| R15-3 | nit | HOUSE-RULE | yes (ef994af) | "Always `id`" ignores the cross-provider static cache | Scoped to a single-provider process, with a cross-reference to §8. |

### 9.16 Task 0 fix-verification r16 of `ed772fb`: CLEAN

r16 verified R15-1, R15-2 and R15-3 RESOLVED. It found 3 nits and nothing blocking. Verdict: "Plan: CLEAN;
Drafts: OK to post; ACs safe to post: yes; Task 1 blocked: no". The nits were applied in the reviewer's
own wording, in the commit after `ed772fb`. That commit isn't separately verified; Task 11's pre-push
adversarial pass covers it.

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| R16-1 | nit | AC-GAP | partly | AC13-10's entity-path `Take(0)` sentence lacked "parameterless", so it read as covering `FirstOrDefault(pred)`, which D8 rejects | "Parameterless `First*`/`Single*` then act as on an empty sequence". |
| R16-2 | nit | AC-GAP | no | The AC13-4 draft's after-`Skip`/`Take` scalar clause lacked "parameterless" | Draft fixed. |
| R16-3 | nit | HOUSE-RULE | yes | §9.15 said "solely in AC13-8", but AC13-10 keeps its D8-over-D10 bullet | "Solely" removed. |

### 9.17 Task 1 test review (two non-author reviewers) at `fee1a0d`

Reviewer A covered the SQL Server suite, the Core tests and plan rev 17. Reviewer B covered the PostgreSQL,
MySQL and SQLite ports. Both verdicts: NOT CLEAN; Task 2 may not start.

**Totals:** AC-GAP 1, TEST-GAP 12, PLAN-GAP 3, OTHER 2.

Folded in:
- SQL Server: `87f2372`.
- Ports: the commit after it.

| # | Sev | Blame | Finding (short) | Disposition |
|---|---|---|---|---|
| T1-1 | major | TEST-GAP | No test for `OfType` over a *reference* scalar (AC13-6) | `OfType_Identity_OverReferenceScalar_Rejected`; §4.4 row. |
| T1-2 | major | TEST-GAP | `Last` tests whose expected row was also the max id (an `Id DESC` fallback passed); no computed-term inversion test | Reseeded; `Last_AfterComputedOrderBy_InvertsComputedTerm`; §4.4 rows. |
| T1-3 | major | TEST-GAP | Three rejection rows green on 3.9.0 via older guards (they only checked the operator name); §4.4 "allow any Cast" row overstated | Policy/D5/`OfType` message asserts; §4.4 rows corrected and added. |
| T1-4 | minor | TEST-GAP | `Contains` test used `List<T>.Contains`, not `Enumerable.Contains` | Both rows. |
| T1-5 | minor | TEST-GAP | Computed fragment never ordered on a join entity with the qualifier | Direct `MapHit_ComputedFragment_NeverPrefixed`. |
| T1-6 | minor | TEST-GAP | AC13-8's "outermost pass-1 failure" and pass-2 inner→outer untested | Two precedence tests; §4.4 rows. |
| T1-7 / P1-1 | major | TEST-GAP | MySQL/SQLite lacked `ScalarProjection_WithReducingTerminals_ThrowNotSupported` | Ported. |
| T1-8 | nit | PLAN-GAP | Root `Single` exemption unstated; §6 counts overlap | §4.1 and §6 text. |
| T1-9 | nit | TEST-GAP | Vacuous rows: `Distinct` over unique rows; ascending orders equal to id order; `IsInstanceOfType` on a boxed long | Reseeded b,a,c; scalar `Distinct`; assert removed. My own reseed briefly made two `Single*` predicate rows vacuous (target = first row); retargeted to "a" before commit. |
| T1-10 | nit | OTHER | `AssertNoQuery` assumes every path logs | `Harness_LogObservesEveryExecutionPath`. |
| P1-2 | minor | TEST-GAP | PostgreSQL unordered `Last` on 3.9.0 follows heap order: rows flip between runs | SQL assert in `Last_Parameterless_Unordered_ReturnsMaxId`; §6 note. |
| P1-3 | minor | AC-GAP | No AC or test that an *explicit* own term inverted by `Last*` stays qualified | AC13-2 amended; remote `Last` test asserts `{table}.id DESC`; SQLite restores `ThenBy(Id)`. |
| P1-4 | minor | TEST-GAP | MySQL's new reverse-key entity rested on an unexecuted premise | `Count(pred)` precondition assert. |
| P1-5 | minor | TEST-GAP | MySQL `Contains("LIMIT 2")` matches `LIMIT 2147483647` | `\bLIMIT 2\b`. |
| P1-6 | nit | TEST-GAP | Root `Single` row passed when both sides threw the same wrong exception | Asserts `InvalidOperationException` on both sides. |
| P1-7 | nit | OTHER | SQLite cleanup deleted every registered DB; narrow exception filter | Per-class delete; tolerant filter. |
| P1-8 | nit | TEST-GAP | SQLite parameter-uniqueness check vacuous on an empty name list | Non-empty assert. |
| P1-9 | nit | PLAN-GAP | Provider-scope note missed the SQLite Skip-only allowed rows; MySQL AC12-8 null remote key uncommented | Note extended; comment added. |

### 9.18 Fix-verification FV1 of `fee1a0d..6fb304c`

**Totals:** TEST-GAP 3, HOUSE-RULE 1, PLAN-GAP 1. Four of the five are fix-introduced.

FV1 confirmed:
- every §9.17 finding RESOLVED except T1-10 (PARTIAL), across the four providers;
- the §6 counts, by fresh runs;
- the nine new §4.4 rows, except the Log row.

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| FV-1 | minor | TEST-GAP | yes | The retargeted `Last`-with-predicate rows expected the lowest id among the matches, so a `Last` that drops the ORDER BY passed; the fix traded one blind spot for another | No reseed. The dedicated tests and the `Allowed[Last*]` families assert the inverted ORDER BY (a seed alone can't rule out both wrong answers). `Allowed[LastOrDefaultPredicate]` turns red, as intended. §4.4 row. |
| FV-2 | minor | PLAN-GAP | yes | The new `OfType` message asserts went beyond §5.2 (null hint promised only for `Nullable<>`; nothing for non-identity `OfType`) | §5.2 message spec amended to match the tests. |
| FV-3 | nit | TEST-GAP | yes | Harness positive control skipped the `Single*`/`Last*`/`LongCount` paths | Added. |
| FV-4 | nit | HOUSE-RULE | yes | The reseeded `LastOrDefault` predicate test seeded a null remote LEFT-JOIN key in an oracle row | Seeds an employer. |
| FV-5 | nit | TEST-GAP | no | `Allowed[AnyPredicate]` was true with or without the predicate | `== "zzz"` (false). The `All` half rested on a false premise: dropping `All`'s predicate already throws on 3.9.0. See FV2-1. |

### 9.19 Fix-verification FV2 of `6fb304c..774a640`

**Totals:** TEST-GAP 1, HOUSE-RULE 1, PLAN-GAP 1.

FV2 confirmed:
- FV-1 to FV-4 RESOLVED on all four providers;
- the `OrderByList()` asserts pin the shape's top-level ORDER BY, and the oracle read emits none;
- the expected inverted text matches a correct 3.10 implementation;
- the counts.

| # | Sev | Blame | Fix-introduced | Finding (short) | Disposition |
|---|---|---|---|---|---|
| FV2-1 | minor | TEST-GAP | yes | FV-5 swapped `All`'s all-true row for an all-false one. The all-true row was MySQL's only kill of "drop the NOT in NOT EXISTS" | Both kept on all four: `All` (false) and `AllTrue`; `AnyPredicate` (false) and `AnyPredicateTrue`. |
| FV2-2 | nit | PLAN-GAP | yes | §6 counts label stale; red `Allowed` rows unmapped to tasks | Relabelled; mapped to Tasks 5/7/8. |
| FV2-3 | nit | HOUSE-RULE | no | FV-4's class not propagated: two more `Last` oracle tests seeded null employer keys (SQL Server, PostgreSQL, SQLite) | Employer seeded. |

### 9.20 Fix-verification FV3 of `774a640..bbede92`: CLEAN

FV2-1/2/3 RESOLVED on every applicable provider. The `All`/`Any` kill set is the union of the pre- and
post-FV-5 rows. Employer seeding doesn't change what the two `Last` tests rule out. Task mappings are complete.
All four counts were reproduced. One nit (FV3-1, TEST-GAP): the `AnyPredicateTrue` comment overstated its
kill. The comment was corrected and the predicate left unchanged, as the reviewer advised.

### 9.21 Test gaps found during Task 4 (author-found; no reviewer had flagged them)

| # | Blame | Finding | Disposition |
|---|---|---|---|
| T4-1 | TEST-GAP | `Covariant_ScalarSource_SequenceOrEnumeration_MatchesOracle` (§4.2 AC13-4, "left alone") was never written in any suite. Two §4.4 mutations named it as their killer. The Task 1 reviews checked name parity *between* suites, not against the matrix. | Added to all four: {`Skip.Take`, `Take`, `Distinct`, enumerated} × both spellings. Green pins; they now kill "guard rejects valid enumeration" and "lambda-free operators over a converted source rejected". |
| T4-2 | TEST-GAP | `Rejected_Pass2InnerFailureWins` survived a naive outer→inner pass 2. The outer D8 failure depends on inner paging state, so a reversed walk never raised it. | Second shape added: `Cast<Unrelated>().Where(a => a.Id > 0)` (outer I1 failure independent of inner state) must report D5. The mutation is now killed. |

### 9.22 Gaps found during Task 7 (author-found; no reviewer had flagged them)

| # | Blame | Finding | Disposition |
|---|---|---|---|
| T7-1 | AC-GAP | "`Last*` without the row limit" survived. Reading every row and keeping the first returns the same entity, and AC13-2 didn't require one row to be read. | AC13-2 amended *(rev 21)*: "one row read" (`TOP (1)` / `LIMIT 1`). `Last_ReadsOneRow_EmitsRowLimit` added to all four suites; §4.2/§4.4 rows. The mutation is now killed by that test alone (1 of 117 baseline-passing tests). |
| T7-2 | HOUSE-RULE | The mutation runners counted **any** failing test as a kill. A filter that also selected still-red tests (owned by a later task) reported "killed" whatever the mutation did: T7-1's survivor was first reported killed by 10 `LongCount` reds. The Task 4 runner also rebuilt only the SQL Server provider, so its PostgreSQL mutation ran against an unmutated build. The first corrected runner then took each baseline after reverting the previous mutation, without a rebuild (a revert doesn't rebuild). | One runner for all tasks: every baseline runs on a clean build before any mutation, a mutation is killed only when a **baseline-passing** test fails, each mutation builds its own provider, and a journal restores sources after an interrupted run. The Task 4–7 batches were re-run with it (§6 Task 4/5/6/7 counts). A taxonomy row is proposed: "mutation kill measured against the wrong baseline". |

### 9.23 Task 4 hostile review of `23e8518` (non-author; probes executed against 3.9.0 and `23e8518`)

Verdict: NOT CLEAN, no behavioral defect; Task 5 could build on it. 6 findings: 3 minor, 3 nits. Classified by the
blame order; the reviewer's own labels were PLAN-GAP ×3 and OTHER ×3. AC-GAP 2, TEST-GAP 0, HOUSE-RULE 4,
PLAN-GAP 0, OTHER 0.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| T4R-1 | minor | HOUSE-RULE (message advice must work for the shape it rejects) | The I1 advice "make the helper generic (`where TEntity : {ParamType}`)" fails for an interface (the pre-existing `Convert(x, I).M` WHERE bug, executed) and doesn't compile for `object` (CS0702). | Generic-helper advice for a class base type only; interface/`object` sources are told to apply the predicate to the concrete `IQueryable<T>`. The I1 test gains `interface` rows (26 per suite) and per-source advice asserts; `GenericHelper_ConstrainedToBaseClass_MatchesOracle` runs the advised helper. §8 records the interface bug. §5.2 updated. |
| T4R-2 | minor | AC-GAP | `Cast`/`OfType` after an unsupported `Select` were judged against a stale row type, giving a false reason (executed). AC13-8's precedence put D5/I3 before the parse loop, so it required that message. | AC13-8 amended. The policy tracks an unsupported `Select` and leaves later `Cast`/`OfType` to the parse loop. `OtherSelect_ThenCastOrOfType_SelectShapeMessage` [Cast, OfType] in all four suites. |
| T4R-3 | minor | AC-GAP | AC13-5's list of rejected-but-correct-in-3.9.0 shapes missed `Skip(0).X` (D8) and a coinciding second ordering (D10), both executed. | AC13-5 amended; the Task 10 Changelog **Changed** list takes both. Repost to #13 pending the owner. |
| T4R-4 | nit | HOUSE-RULE (no mutable internals behind a read-only type) | `SupportedOperators` returned the backing array: a cast and a write changed the public view. | `Array.AsReadOnly` view; `SupportedOperators_IsReadOnlyView`. |
| T4R-5 | nit | HOUSE-RULE (comment truth) | The four parse loops cite "the result-type and GroupBy guards", which have left the loop. | Reworded. |
| T4R-6 | nit | HOUSE-RULE (message advice must work) | The D10 message doesn't name `OrderByDescending`, and "use ThenBy to add a key to the existing order" reverses LINQ's priority for `OrderBy(a).OrderBy(b)`. | The message names the operator and says the later ordering is primary: `query.{Op}(later).ThenBy(earlier)`. The D10 test asserts both. §5.2 updated. |

Mutations for the layer (baseline-aware runner): 8, all killed. They cover:
- the advice: always generic, generic for `object`, never generic;
- `Cast`/`OfType` after an unsupported `Select`: not suspended, and suspended after a scalar `Select` too;
- the backing array returned;
- the D10 message: a fixed name, and the reversed advice.

### 9.24 Hostile review r67 of `abb41ad..1d35177` (Tasks 6–7, T4R layer; non-author; probes executed)

Verdict: NOT CLEAN. 7 findings: 1 major, 3 minor, 3 nits. The reviewer re-ran the T6, T7 and T4R mutation batches
(10, 14 and 8 killed) plus 11 of its own, 2 of which survived (r67-2, r67-3). T4R-1/3/4/5/6 were confirmed;
T4R-2's fix was confirmed but its boundary was untested (r67-2). AC-GAP 3, TEST-GAP 3, HOUSE-RULE 1, PLAN-GAP 0,
OTHER 0. Two findings were introduced by the T4R fix layer (r67-2, r67-6). Both fix tests pinned the case they
fixed, not the case they must leave alone.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| r67-1 | major | AC-GAP | A null held in a variable (`string none = null; p.M == none ? …`) emitted `= NULL`. SQL Server threw; the other three ordered wrongly, and `Last` inherited it. AC12-8's examples were literals only. | AC12-8 amended. The visitors treat an operand as null when it's a null literal or reads no lambda parameter and evaluates to null. Rows 4–7 in all four suites; 5 direct rows, including a captured non-null value that must stay `=`. §8 records the same gap in WHERE. |
| r67-2 | minor | TEST-GAP (fix-introduced) | Making a **subset** `Select` also suspend D5/I3 survived every test. | `SubsetSelect_ThenUnrelatedCastOrOfType_D5Message` [Cast, OfType]; AC13-8 states the boundary. |
| r67-3 | minor | TEST-GAP | The `Distinct` guard was tested with `Last()` only; "checks only `Last`" survived (`LastOrDefault` gave a misleading key error). | `LastFamily_AfterDistinctProjection_NoOrder_ThrowsNamingTerminal` over {Last, LastOrDefault, Last(pred), LastOrDefault(pred)}. |
| r67-4 | minor | AC-GAP | On PostgreSQL, `Last` over a nullable key differs from LINQ-to-objects (NULLs sort last), as `First` already did. AC13-2 promised LINQ parity. | AC13-2 amended: `Last` is the last row of the provider's own order. `Last_NullableKey_EqualsTheProvidersOwnOrder` in all four suites; §8 and `Advanced.md` §5 document the placement. |
| r67-5 | nit | HOUSE-RULE (message truth) | The guard said the default Id DESC order isn't in the projection, false when `Id` is projected; the comment said the same. | Reworded: distinct projected rows have no default order. The `projectId` row asserts the message doesn't mention `Id DESC`. |
| r67-6 | nit | TEST-GAP (fix-introduced) | The T4R-6 advice `ThenBy(earlier)` dropped a descending earlier key's direction. | The advice adds "keeping each earlier key's direction (ThenByDescending for a descending one)", and the D10 rows assert it. Docs updated. |
| r67-7 | nit | AC-GAP | On an entity without `Id`, the no-`Id` error (worked out in the parse) masked the scalar and `Distinct` guards. AC13-8's precedence put the parse first. | `DefaultLastOrderBy` is resolved in `BuildQueryComponents`, after both guards. Two `Last_EntityWithoutId_*` tests; AC13-2 amended. |

*(Corrected in rev 24, NF-1: the claim below was false. With the r67 finder, "never finds" made the nested-lambda
shape work, so the finder was wrong, not redundant. §9.27 has the fix.)*

Mutations for the layer (baseline-aware runner): 7 run, 6 killed. The survivor, "the parameter finder never
finds", is **equivalent**: an operand that reads the lambda parameter can't be compiled on its own, so its
evaluation throws and is caught as not-null either way. The finder stays, because it spares that exception for
every member operand.

### 9.25 Hostile review r810 of `1d35177..194b917` (Tasks 8–10; non-author; probes executed)

Verdict: NOT CLEAN. 5 minor findings and 5 nits; no behavioral defect in the range. Every suite was green at
`194b917`, the §6 Task 9 coverage table was reproduced from cobertura, and the T8/T9/T9-coverage/T10 batches
re-ran: 6/6, 4/5 (the documented equivalent mutant), 13/13 and 3/3. The 3.9.0 claims were executed on SQLite,
plus raw SQL on SQL Server. Totals: AC-GAP 1, TEST-GAP 5, HOUSE-RULE 2, PLAN-GAP 1, OTHER 1.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| F1 | minor | TEST-GAP | No test of `LongCount(pred)` over a remote column. Leaving `LongCount`'s predicate out of the join resolution survived all 714 SQLite tests (`no such column: country_0.Name`). | `LongCount_PredicateOnForwardRemoteColumn_InjectsJoin` and `LongCount_PredicateOnReverseRemoteKey_ThrowsNotSupported` in all four suites. The mutant is killed on SQL Server and SQLite. |
| F2 | minor | AC-GAP | Two "happened to be correct" examples were false: SQLite `Skip(0).Where(w)` was invalid SQL in 3.9.0, and `Take(0).Any()` returned `true` (the Changelog dropped `k ≥ 1`). | AC13-5 amended (SQLite: only `Skip(0).Count()`). The Changelog says `Take(k ≥ 1).Any()` and qualifies SQLite. |
| F3 | minor | TEST-GAP | The Changelog said a second `OrderBy` had the wrong priority even across `Where`. Executed: chained gave `ORDER BY a, b`; across `Where` the earlier key was dropped (§9.2 F1). | Reworded to state both 3.9.0 behaviors and when the across-`Where` form happened to be right. |
| F4 | minor | HOUSE-RULE (release-branch alignment: future work marked as future) | "Known issues (fixed in 3.10.1)" and "are fixed in 3.10.1" described an unreleased fix as shipped. | "Fixes planned for 3.10.1" in the Changelog, `Advanced.md`, the AI doc and §6. |
| F5 | minor | TEST-GAP | The doc test collected names into a set: a duplicate or contradicting row passed. | A list. It asserts no operator is listed twice and every data row names one. Two mutations killed. |
| N1 | nit | TEST-GAP | `Advanced.md` gave the `Last`/`Distinct` reason "the default `Id DESC` isn't in the projection", false when `Id` is projected (the message was fixed in r67-5). | "Even when `Id` is projected: distinct projected rows have no default order." |
| N2 | nit | TEST-GAP | AC13-9: the README must record the aggregate known issues; it didn't. | The README's 3.10.0 line names them and points to Known issues. |
| N3 | nit | PLAN-GAP | §8 still said "(to be confirmed)" for SQL Server's `Average` truncation. | Marked confirmed (S10; r810: `AVG` over (1),(2) = 1). |
| N4 | nit | OTHER (sub-class: incomplete 3.9.0 characterization) | `LongCount` on an empty set threw `NullReferenceException` in 3.9.0, not `InvalidCastException`. | The Changelog names both. |
| N5 | nit | HOUSE-RULE (doc truth) | `Advanced.md` still said "four areas"; the reverse-join rows omitted `LongCount`, which is rejected too. | "Five areas", listing §5; `LongCount` added to the reverse-join rows in both docs. |

**Tooling (outside the range):** `mutrun.py` kept one journal beside the script, with relative paths. Two runs
from different roots could restore each other's files. Now each working tree gets its own journal (keyed by the
cwd), holding absolute paths.

Mutations for the layer (baseline-aware runner): 4, all killed (F1 on SQL Server and SQLite; F5 ×2).

### 9.26 Task 11 full-branch adversarial pass of `master..3457e61` (non-author; executed)

Verdict: NOT CLEAN. 4 minor, 1 nit; no behavioral defect in the query semantics. Every suite and both twins were
reproduced green. The Task 5 batch re-ran 13/13; Tasks 2 and 3 were re-run on the baseline-aware runner, 4/4 and
2/2. Of the reviewer's 11 mutations, 3 survived (F1, F2, F5). Totals: AC-GAP 0, TEST-GAP 3, HOUSE-RULE 1,
PLAN-GAP 1, OTHER 0.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| gx-F1 | minor | TEST-GAP | AC13-1's `min(Take ?? 2, 2)` wasn't pinned for `k > 2`; `Take = Take ?? 2` survived the whole suite. | `Single_AfterTakeGreaterThanTwo_ReadsTwoRows` (all 4: throws, and the SQL reads 2 rows). |
| gx-F2 | minor | TEST-GAP | Duplicate removal wasn't tested for `CASE` keys (SQL Server error 169 if they weren't dropped). | `ThenBy_SameTernaryKeyTwice_Executes` (oracle + one `CASE` in the ORDER BY; all 4). |
| gx-F3 | minor | PLAN-GAP | §5.1.1's optional `tableQualifier` parameter removed the 3.9.0 3-parameter constructor: a 3.9.0-compiled consumer hit `MissingMethodException` (executed). The Changelog's API list also omitted `OrderByTerm`/`OrderByTerms`. | The 3.9.0 constructor is restored, delegating to a 4-parameter overload with no defaults (no overload ambiguity). `Constructor_390Signature_IsKept`. Changelog lists the API. |
| gx-F4 | minor | HOUSE-RULE (release-branch alignment) | All five csprojs still said 3.9.0, while the Changelog and README announced 3.10.0. Merging would make CI try to republish 3.9.0. | `3.10.0-beta1` (Version/InformationalVersion; Assembly/File `3.10.0.0`, the b5401b2 pattern). Changelog header `[3.10.0-beta1]`, README label. §6 Task 11 step. |
| gx-F5 | nit | TEST-GAP | AC12-3 was tested only on a plain entity; "qualify when the map isn't empty" survived. | `ComputedAttributeEntityWithoutJoins_OwnColumnOrder_Unqualified` (3 suites; MySQL has no computed-attribute entity). |

Pre-existing items the reviewer found (stdout write, Distinct + computed-subquery order, the SQLite view-backed
`rowid` change) are in §8 and the Changelog.

### 9.27 Task 11 fix-verification of `194b917..3457e61` (r67/r810 fix layers; non-author; executed)

Verdict: NOT CLEAN. r67-2..7 and every r810 item RESOLVED (red-before-green confirmed on all four for r67; 3.9.0
claims re-executed on all four). r67-1 and N5 PARTIAL. 4 new findings: 1 minor, 3 nits. Totals: AC-GAP 1,
TEST-GAP 2, HOUSE-RULE 1, PLAN-GAP 0, OTHER 0.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| NF-1 | minor | AC-GAP | The r67 finder flagged any parameter, including one declared by a lambda inside the operand: `p.M == names.FirstOrDefault(n => …)` still emitted `= NULL` (SQL Server threw). The §9.24 equivalence claim was false. | `FreeParameterFinder` records lambda-declared parameters and flags only free ones. AC12-8 widened. Oracle row 8 and a direct row; §9.24 corrected. |
| NF-2 | nit | TEST-GAP (fix-introduced) | Operands were evaluated 2–3 times. The `when` guard and the body could disagree: `NullOnFirstCall() == p.M` emitted `'x' IS NULL`. | One operand path: `OperandSql` evaluates once (fast path for a captured or static field) and formats the value, and the null branch reuses it. `TernaryOperand_EvaluatedOnce_NullCheckAndSqlAgree` (counter = 1; column kept). |
| NF-3 | nit | TEST-GAP (fix-introduced) | The r810 doc-test rewrite only recognized `|---` separators: `| --- |` and `|:---|` failed. | A separator regex; a separator is required. `DocumentedOperators` is factored out, with `TableParser_AcceptsEverySeparatorForm` (4 forms) and `TableParser_WithoutSeparator_Fails`. |
| NF-4 | nit | HOUSE-RULE (sibling drift, pattern 16) | N5 fixed the rows but not the neighbouring sentences and code comments, which still omitted `LongCount`. | `LongCount` added in `Advanced.md`, the AI docs, README, Usage.md and the four providers' comments. |

Mutations for the round (baseline-aware runner, SQL Server visitor): 6 run. 4 killed. "3.9.0 constructor removed"
doesn't compile, because internal callers use it; the reflection test pins it for compiled consumers. "Finder never
finds" was **equivalent for C#-compiled lambdas** at `2720c28` *(narrowed in rev 25; obsolete since rev 25, v4 N-3:
1a0204c removed the fallback, so the mutant is now killed, 31/56)*. A parameterless lambda whose body reads an
undeclared parameter can't be compiled, so `OperandSql` falls back either way. The original "with proof" was too
broad: a hand-built block declares variables the finder didn't register (v3 N2); it does now. The finder only spares that exception for
every column operand.

### 9.28 Verification v3 of `3457e61..2720c28` (non-author; executed, 34 mutants of its own)

Verdict: NOT CLEAN. gx-F1..F4, NF-1 and NF-3 RESOLVED; gx-F5, NF-2 and NF-4 PARTIAL; 1 new minor (a regression in
2720c28) and 3 nits. Totals: AC-GAP 0, TEST-GAP 3, HOUSE-RULE 3, PLAN-GAP 0, OTHER 0 (perf, N4, is with N1;
HOUSE-RULE corrected from 2 in rev 26).

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| gx-F5 (partial) | — | TEST-GAP | MySQL *has* a computed-attribute entity (`ProjectScorecard`); the mutant survived there. | Pin added to `MySqlOrderByQualificationTests`. The mutant is killed (run with its class; see §8 on run-alone cleanup). |
| NF-2 (partial) | — | TEST-GAP | On an evaluation failure, the `catch` retried through `BuildValueSql`. `ThrowOnce()` ran twice and emitted `= NULL`. | The failure is reported, not retried, with the same message text. `TernaryOperand_ThatThrows_EvaluatedOnce_Rejected`. |
| NF-4 (partial) | — | HOUSE-RULE (sibling drift) | `Usage.md:456` still omitted `LongCount`. | Added. |
| v3-N1 | minor | TEST-GAP (fix-introduced) | `OperandSql` evaluated the `Convert` node that `BuildValueSql` unwraps: a captured `char` became its code point (`initial = 120`, SQL Server Msg 245; SQLite misordered). Captured enums changed `'B'` → `2`, unpinned. | Conversions are unwrapped before evaluating, which also restores the field fast path (v3-N4). `FormatConstant` formats an enum as its underlying number (test and branch positions; Changelog **Fixed**). `CapturedCharAndEnum_FormatAsTheirValues`. |
| v3-N2 | nit | HOUSE-RULE (prose truth) | §9.27's "equivalent, with proof" was false for hand-built blocks: block and catch variables weren't registered as declared. | Registered. `BlockOperand_DeclaredVariable_DoesNotReadTheRow`; the claim is narrowed to C#-compiled lambdas. |
| v3-N3 | nit | HOUSE-RULE (doc truth) | The Changelog's SQLite `rowid` note was wrong in two cases (executed): a `WITHOUT ROWID` base with one join paged in 3.9.0, and a view with two or more joins already failed. | Rescoped to "a base without a `rowid` and exactly one remote join" in the Changelog and §8. |
| v3-N4 | nit | (with N1) | A captured value inside `Convert` still compiled a delegate on every visit (2000 visits: 13 ms on 3.9.0, 314 ms on 2720c28). | Fixed by N1's unwrap: `Convert(field)` takes the field fast path. |

Pre-existing, recorded in §8: MySQL `Delete<T>(predicate)` on a cold column cache.

Mutations (baseline-aware runner): 5, all killed. They cover: no `Convert` unwrap; failure retried; block variables
unregistered; enum by name; and the MySQL gx-F5 mutant.

### 9.29 Verification v4 of `2720c28..92bedba` (non-author; a ~150-shape output diff against 3.9.0, three cultures, net48)

Verdict: NOT CLEAN. gx-F5, NF-2, NF-4, v3-N3 and v3-N4 RESOLVED; v3-N1 and v3-N2 PARTIAL. The Changelog's enum and
SQLite `rowid` claims were executed on 3.9.0 and on HEAD, and are true. 2 minor and 4 nits. Totals: AC-GAP 2,
TEST-GAP 1, HOUSE-RULE 3, PLAN-GAP 0, OTHER 0.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| v4 N-1 | minor | AC-GAP (fix-introduced) | The unwrap included `ConvertChecked`, which 3.9.0's `BuildValueSql` never read through. In a checked context, `x.Age == (int)capturedDouble` emitted `2.7` (3.9.0: `2`), and an overflow emitted the raw number (3.9.0: rejected). | Only `Convert` is read through. AC12-8 says so. `CheckedConversion_IsEvaluated_NotUnwrapped`. |
| v4 N-2 | minor | AC-GAP | Since 2720c28, a captured instance property (`this.X`) read its value in the test (3.9.0: `NULL`) but still `NULL` as a branch value. The change was unlisted, and the two positions disagreed. | Branch values go through `OperandSql` too: one path. Changelog **Fixed**. `CapturedInstanceProperty_SameValueInTestAndBranch`. |
| v4 N-3 | nit | HOUSE-RULE (prose truth) | §9.27's narrowed equivalence claim was obsolete: 1a0204c removed the fallback, so the mutant is killed. | Restated. |
| v4 N-4 | nit | TEST-GAP | Untested: catch-variable registration, the throw-path message text, and non-`int` enums. All three mutants survived. | Try/catch operand row; `TernaryOperand_ThatThrows_KeepsThe390Message`; `byte`/`long` enum rows. All killed. |
| v4 N-5 | nit | HOUSE-RULE (bookkeeping) | §9.28 totals said HOUSE-RULE 2; the table has 3. | Corrected. |
| v4 N-6 | nit | HOUSE-RULE (doc truth) | The Changelog's enum parenthetical understated the change: any enum value, in either position. It also claimed "how FunkyORM stores enums", but PostgreSQL enum parameters fail (pre-existing). | "A literal or a computed value, in the test or a branch"; the storage claim was dropped. §8 records the PostgreSQL parameter failure. |

Pre-existing items found, now in §8: PostgreSQL enum parameters; `char` comparisons in checked projects.

Mutations (baseline-aware runner): 6, all killed. They cover: `ConvertChecked` read through; branches via
`BuildValueSql`; catch variable unregistered; both message texts; enum via `Int32`.

### 9.30 Verification v5 of `92bedba..3f38bb6` (non-author; a 266-shape parity matrix: 4 providers × 2 cultures × net8, plus net48)

Verdict: NOT CLEAN, on nits only. N-1, N-3, N-4 and N-5 RESOLVED; N-2 and N-6 PARTIAL. Every output difference from
3.9.0 (70 shapes) falls in one of four classes: `IS [NOT] NULL`, enum numbers, a captured `this` property's value, and
a throwing `this` getter. Evaluation counts match 3.9.0, apart from the `this` getter. Totals: TEST-GAP 1, HOUSE-RULE 4.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| v5-1 (N-2 residual) | nit | TEST-GAP (fix-introduced) | Only THEN was pinned: "ELSE via `BuildValueSql`" survived all four suites. | ELSE row added. Mutant killed on all four. |
| v5-2 (N-6 residual) | nit | HOUSE-RULE (doc truth) | "A literal … in the test" was false: the compiler folds an enum literal to a number, which 3.9.0 already emitted. | "A captured or computed value in the test, or any enum value in a branch", with a note on folded literals. |
| v5-3 | nit | HOUSE-RULE (unlisted difference) | A throwing `this` getter (3.9.0: never called, `NULL`) now throws `NotSupportedException`, and the getter runs once per translation. | Added to the Changelog's captured-`this` entry. |
| v5-4 | nit | HOUSE-RULE (understatement) | The null scope said "literal or variable"; computed nulls changed too. | "Or computed without reading the row" in the Changelog, `Advanced.md` and the AI doc. |
| v5-5 | nit | HOUSE-RULE (prose drift) | The `OperandSql` summary named only the test, not branches. §8's checked-project entry named only `char`; `byte` throws too. | Both reworded. |

Advisory, not done: a captured `this` property compiles a delegate per visit (≈85 µs; 3.9.0 was fast only because it
emitted `NULL`). A `PropertyInfo` fast path is a possible follow-up. One pre-existing, out-of-scope finding was
reported to the owner directly.

Mutations: the ELSE-branch mutant, killed on all four providers.

### 9.31 Verification v6 of `3f38bb6..de9bf1e` (non-author; a 34-shape harness on master and HEAD, plus SQLite end-to-end getter counts)

Verdict: NOT CLEAN, on 3 prose nits, all introduced by the v5 doc fixes. The production diff was comments only. v5-1,
v5-4 and v5-5 RESOLVED; v5-2 and v5-3 PARTIAL. No whole-branch defect. Totals: HOUSE-RULE 3.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| v6 N-1 | nit | HOUSE-RULE (doc truth) | "An enum literal in the test was already a number" is false for a nullable enum column (`Convert` to `int?` keeps the constant an enum; 3.9.0 emitted `'B'`). | The scope list and the note were removed: "could be emitted as its name … now always the underlying number". |
| v6 N-2 | nit | HOUSE-RULE (doc truth) | "The getter now runs once per translation" is false: the count depends on the ordering chain (SQLite end-to-end). | "Called while the query is translated (3.9.0 never called it)"; the count was dropped. |
| v6 N-3 | nit | HOUSE-RULE (prose) | §8's "any comparison whose column side is converted" was too broad: enum comparisons compile to `Convert` and work. | Limited to the executed cases: `char`, `byte`, `short`, `int` against `long`. |

Lesson (ledger): each prose fix that adds a qualification creates a new claim to verify. Remove claims that execution
doesn't support; don't qualify them further.

### 9.32 Verification v7 of `de9bf1e..6d64733` (non-author; four harnesses, master and HEAD × unchecked and checked, about 70 shapes, SQLite end-to-end)

Verdict: NOT CLEAN on one plan-internal line. All three changed Changelog/§8 sentences are TRUE by execution:
- enum: names on 3.9.0 in every non-folded case, numbers everywhere at HEAD;
- getter: never called on 3.9.0; called during translation at HEAD; a throwing getter raises `NotSupportedException`;
- checked list: correct.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| v7 N-1 | nit | HOUSE-RULE (doc truth) | §9.31's row v6 N-2 still gave a count ("one call per ordering call: 1/2/3"), as did 6d64733's commit message. Execution: 2 calls for one ordering with the getter in the test and THEN; 1 call when the getter is in a later `ThenBy`. | The count was removed from §9.31. The commit message is history; this row supersedes it. |

Pre-existing, recorded in §8: `.HasValue` on a value not read from the row (v7 P-1).

### 9.33 Task 12 hostile review of `66bde1e..e104aad` (non-author; 17 shapes per provider against LINQ-to-objects on both shas)

Verdict: NOT CLEAN. The injection is closed and the plumbing is sound (suites green; red before green 5/5/5/4;
10 of 11 claimed mutations re-killed). But values typed "like WHERE parameters" changed how 3.9.0's literals
compared and sorted.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| F1 | major | AC-GAP | SQLite: a Guid or date value bound in the driver's format, not FunkyORM's stored text, so Guid equality never matched and an equal date sorted with the greater ones. | Bound as the literal's text. Oracle rows on all four providers: Guid equality (target not first, hex letters), date `==`/`>` at whole and half seconds, both kinds. |
| F2 | major | AC-GAP | PostgreSQL: typed parameters threw for a UTC `DateTime` and a non-UTC `DateTimeOffset` when Npgsql initialized first. Text-typed values broke comparisons with `inet`/`citext`. | Untyped (`NpgsqlDbType.Unknown`) with the literal's text. UTC rows and an offset-branch row added. The WHERE side is in §8. |
| F3 | minor | AC-GAP | SQL Server: Guid branches sorted as `uniqueidentifier`; `datetime2` against legacy `datetime`; `DateTimeOffset` as `o` text; strings `nvarchar`, with a collation change and a non-ASCII fix, neither in the Changelog. | Guids and dates `varchar` with the literal's text. Strings stay `nvarchar` (like WHERE's), with the collation change and the non-ASCII fix in the Changelog. The comment went with `BindableValue`. |
| F4 | nit | OTHER (tooling) | `main_mutations_t12.py` still had the deleted `char` entry. | Removed. |
| F5 | nit | TEST-GAP | `AssertEveryParameterReferenced` split the joined log by line, so a parameterless command merged into the next, and a value with a line break ended its command. | Reads one entry per `Log` call. Self-test added; the old helper is its mutation. |
| F6 | nit | OTHER | Literal-mode notes: PostgreSQL `standard_conforming_strings=off`; MySQL `NO_BACKSLASH_ESCAPES`. | Report only; §8. |

Pre-existing, recorded in §8: SQLite WHERE Guid/date binding (F1); PostgreSQL WHERE timestamps and startup order (F2).

### 9.34 Fix-verification of `66bde1e..7d9b65b` (non-author; 76 shapes × 3 cultures × 4 providers on three shas, raw Npgsql 8/9)

Verdict: NOT CLEAN. F1 and F3–F6 resolved; F2 partly. Rev 31's PostgreSQL fix (untyped parameters), combined with
parameter sharing, regressed two shapes against 3.9.0.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| N1 | major | AC-GAP | PostgreSQL: a captured non-null value compared with `null` (`s == null ? …`, `s != null ? x.A : x.B`, Guid/date too) became `$n IS NULL` with an untyped parameter: error 42P18. 3.9.0 and `e104aad` ordered correctly. | On PostgreSQL, a parameter's null test is decided in .NET (it holds a non-null value read from no row). Oracle rows: 4 shapes. |
| N2 | major | AC-GAP | PostgreSQL: a shared untyped parameter took its type from its first use, so later uses inherited it. Silent wrong orders (a date-typed value against a timestamp; `char(n)` padding) and 42883 errors (a Guid or date as branch values, then against a column). `citext` behaviour depended on the first use. | One parameter per occurrence, on all four providers. AC12-9 dropping is kept by a term key (each value as kind plus length-prefixed text); a dropped term binds nothing. Oracle rows: date then timestamp [2], branch then column [Guid, date]. The §8 residual entry is removed. |
| N3 | minor | HOUSE-RULE (comment truth) | The visitors' `parameterGenerator` doc still said "created the same way as WHERE parameters … equal values share one". AC12-10's "the text 3.9.0 quoted" is false for a `DateTimeOffset`. | Doc rewritten in all four visitors. AC12-10 says the text is culture-invariant and differs from 3.9.0's for a `DateTimeOffset`. |
| N4 | nit | OTHER (bookkeeping) | Red counts were 3/6/2/9 with the null-text test, not 2/5/1/8. That test's `e104aad` crash (`ArgumentNullException` in the parameter dictionary) wasn't recorded. | Task 12 status corrected and the crash recorded. Rev 32 removes the dictionary. |
| N5 | nit | TEST-GAP | No test schema has a legacy `datetime` column, so the `datetime2`-versus-`datetime` claim had no executing test. | The SQL Server test creates `legacy_datetime_probe` and pins 3.9.0's order. LINQ-to-objects reads `.003` back as `.0033333`, so it can't be the oracle. Mutation: dates bound as `datetime2`. |

Lesson (AC-GAP twice in a row on the same AC): a parameter that replaces a literal must reproduce the literal's
typing per occurrence, not per value. Sharing one parameter between equal values is an optimisation the literal
never had.

### 9.35 Fix-verification of `66bde1e..403597b` (non-author; 538 shapes × 3 cultures × 4 providers × 3 shas, about 19k executions; 22 mutants of its own, all killed)

Verdict: NOT CLEAN on minor items. N1–N5 resolved:
- PostgreSQL: 186 rows that errored on `7d9b65b` now run.
- No HEAD row is worse than `66bde1e` on any provider.
- No placeholder leaked, and no command sent an unused parameter.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| R1 | minor | TEST-GAP | Literal mode (the 3.9.0 constructors) threw `IndexOutOfRangeException` on a value whose text looks like a placeholder: the placeholder regex ran without a generator. | Substitution only with a generator. `LiteralMode_PlaceholderShapedText_IsInlined` on all 4 providers was red first. |
| R2 | minor | HOUSE-RULE (doc truth) | SQL Server `nvarchar` also changes equality: under a `SQL_*` collation, `"ss"` matches a `varchar` `ß` (as WHERE already did). The Changelog named only sorting and the non-ASCII fix. | Changelog sentence added. |
| R3 | minor | HOUSE-RULE (doc truth) | AC12-10's "a text value with quote or backslash characters orders rows as LINQ-to-objects does" was proved only in the test position. Branch values sort by collation (SQL Server word sort ignores the apostrophe). | Narrowed to the test position. |
| A | nit | HOUSE-RULE | The `parameterGenerator` doc and the operator tables said every / text values become parameters. A PostgreSQL null test and a dropped term bind none, and Guids, dates and chars are parameters too. | Wording fixed in the four visitors, Advanced.md and the AI doc. |
| B | nit | OTHER (latent) | The term key was recorded before binding, so a bind that threw would make a retried `Visit` drop the term. | Recorded after the term is added. The revert mutant is equivalent (no throwing bind exists). |
| C | nit | OTHER | The N5 test left `legacy_datetime_probe` in the shared database. | The test drops it in `finally`. |

Also recorded in §8: the literal-mode wording (`DefaultLastOrderBy` builds one), and SQLite under ar-SA (pre-existing).

### 9.36 Fix-verification of `66bde1e..4e9030f` (non-author; 56 shapes × 4 providers × 2 cultures × 3 shas; 17 mutants of its own, all killed)

Verdict: NOT CLEAN on one plan item; the code layer is clean.
- R1, R2, R3, B and C are resolved, and A partly.
- Nit B's surviving mutant is confirmed equivalent: `Bind` is non-virtual and cannot throw.
- HEAD equals `403597b` on all 448 provider rows, and is never worse than `66bde1e`.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| F1 | minor | PLAN-GAP | §8's SQLite culture entry named only the read. The write path (`SqliteDialect.cs:241, 243`) is culture-dependent too: fi-FI stores `.` time separators, and ar-SA stores a Hijri year. Comparisons mismatch silently. Pre-existing in 3.9.0. | §8 entry rewritten; the follow-up is now "write and parse with `InvariantCulture`". Raised to the owner. |
| F2 | nit | HOUSE-RULE | AC12-10's "no value appears in the command text" contradicted "numbers … stay inline". | Now "no quoted value". |
| F3 | nit | HOUSE-RULE | The AI doc omitted chars. Neither operator table said that PostgreSQL decides a value's null test. | Both fixed. |
| F4 | nit | OTHER | Dropping the probe table in `finally` raced across concurrent runs sharing `funky_db`: 4 failures in 20 pairs. | `legacy_datetime_probe` is now a fixture table (`SqlServerTestFixture.EnsureSchema`, like `non_identity_*`); the test deletes only its own rows. |

Also fixed: the status block at the top was stale. Tooling: two anchors in `main_mutations_t12v.py` (#6, #7) were
re-pointed to the rev 33 code; the reviewer had ported both and they are killed.

### 9.37 Fix-verification of `66bde1e..0568a3f` (non-author; 31 shapes × 4 providers × 2 shas; concurrency harness checked against both shas)

Verdict: NOT CLEAN on prose nits; the code layer is clean.
- F2 and F4 are resolved. The harness failed 4 of 24 runs on `4e9030f` and passed 24 of 24, plus 48 of 48 starts
  from a fresh table, at HEAD.
- F1 and F3 are partly resolved; see N1–N3.

Incident during the review: the reviewer wrote a mutant into the repo working tree for about 12 minutes, then
restored it. The author checked that the tree's blob equals `HEAD`; nothing was built or tested from the repo in
that window.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| N1 | nit | HOUSE-RULE | The operator tables said PostgreSQL decides "a value's" null test. Only quoted values are decided: `5 IS NULL` and `NULL IS NULL` are still sent. | Both tables now say a string, char, `Guid` or date value. |
| N2 | nit | HOUSE-RULE | §8: "reading any date under ar-SA throws". A date written under ar-SA reads back correctly there. | Corrected: dates written under en-US or fi-FI throw under ar-SA. |
| N3 | nit | HOUSE-RULE | §8's SQLite WHERE entry said ORDER BY values are bound "as the stored text", which the culture entry contradicts. | Now says the storage format, which matches rows written under a Gregorian calendar with `:` separators. |
| N4 | nit | HOUSE-RULE | The status block said Tasks 1–11 are complete and cited a CLEAN verification with no §9 row. | Now Tasks 1–10 complete, Task 11 through the first push (the push gate's sentinel holds `66bde1e`), and its release steps remaining. |

Observations recorded:
- In §8: SQL Server's constant test in an ORDER BY ternary (O1, pre-existing; restated in revs 36–37).
- In §8: the README and SQLite AI doc's ISO 8601 claim, and the migration need (O2).
- Not recorded: O3, AC12-10 not posted to #12 (an outward action, so the owner's call). O4 (the `EnsureSchema`
  first-creation race) is the same pattern as the other fixture tables. O5 is the orphan `q310_` rows from an
  earlier interrupted run.

### 9.38 Fix-verification of `66bde1e..1458516` (non-author; prose claims executed on 66bde1e and HEAD; suites re-run)

Verdict: NOT CLEAN on plan prose. N1–N4 are resolved, and the shipped docs (`Advanced.md`, the AI doc) are true by
execution. Suites at HEAD: SqlServer 882, Sqlite 785, PostgreSql 757, MySql 708.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| F1 | minor | HOUSE-RULE | §8's new SQL Server entry gave the wrong condition ("no branch reads the row", and specific to `null`). It was also too broad ("quoted values no longer hit it"). What matters is the branch the constant test selects. At HEAD, a selected quoted branch fails with error 1008, not 408. | Restated using only the executed shapes, each with its result. |
| F2 | nit | HOUSE-RULE | "true only under such cultures" referred to the wrong set of cultures. | Now "a Gregorian calendar with `:` time separators". |
| F3 | nit | PLAN-GAP | The list of docs to correct missed `FUNKYORM_AI_INSTRUCTIONS.md`. | Added. |
| F4 | nit | HOUSE-RULE | The WHERE entry gave only the `DateTime` storage format; a `DateTimeOffset` is bound as `fffffffK`. | Both formats named. |
| F5 | nit | HOUSE-RULE | The status block cited §9.33–§9.36, although §9.37 exists. Rev 35's header omitted its Task 12 status change. | Now "§9.33 onward"; header completed. |

### 9.39 Fix-verification of `66bde1e..141654d` (non-author; 27 SQL Server shapes on both shas; SQLite formats across 12 cultures; suites re-run)

Verdict: NOT CLEAN on plan prose. F2–F5 are resolved and F1 partly; every shaped outcome and error number in the restated
entry was confirmed. Suites at HEAD: SqlServer 882, Sqlite 785, PostgreSql 757, MySql 708.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| G1 | minor | HOUSE-RULE | "A variable holding `null` … fails with 408" was unshaped and wrong: the selected branch decides, and a quoted branch fails with 1008 at HEAD. | Restated as three executed shapes. |
| G2 | minor | PLAN-GAP | The follow-up ("emit the selected branch") fixes nothing: every failing shape selects a constant or a parameter, which SQL Server rejects too, or misreads as a select-list position. | Follow-up is now: emit a row-reading branch, otherwise drop the term. The executed errors are cited. |
| G3 | minor | HOUSE-RULE | AC12-10's "a Guid or date value … as in 3.9.0" was false for a `DateTimeOffset`. | Narrowed to Guid and `DateTime`; the `DateTimeOffset` difference is stated. |
| G4 | nit | HOUSE-RULE | Rev 36's header omitted its §9.37 edit. | Added. |
| G5 | nit | HOUSE-RULE | Rev 33's literal-mode test and its mutation were missing from §4.2/§4.4. | Added. |

Observation recorded in §8: SQLite can't read back a `DateTimeOffset` (pre-existing).

### 9.40 Fix-verification of `66bde1e..89383ff` (non-author; G3 probed against typed `DateTimeOffset` columns on 4 providers × 2 cultures, `66bde1e` and HEAD; suites re-run)

Verdict: NOT CLEAN on plan prose. G1, G2, G4 and G5 are resolved, as is the new §8 SQLite entry; G3 is partly
resolved. Suites at HEAD: SqlServer 882, PostgreSql 757, MySql 708, Sqlite 785.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| H1 | minor | TEST-GAP | AC12-10's "a `DateTimeOffset` is compared as its invariant text" was false against a typed column. No test has a `DateTimeOffset` column. | Restated per position. *(Rev 39: that restatement was itself too broad, J1, and the position claims were removed; §9.41.)* |
| H2 | nit | HOUSE-RULE | "For a `DateTimeOffset` it therefore differs" read as if only that type's text changed; `DateOnly`, `TimeOnly` and any value 3.9.0 wrote with `ToString()` changed too (executed). | Named in AC12-10 and in the Changelog. |
| H3 | nit | HOUSE-RULE | §9.39's "every outcome … confirmed" contradicted its own G1 row. | Now "every shaped outcome". |
| H4 | nit | HOUSE-RULE | The §8 entry's provenance didn't credit the rev 36 reviewer or the rev 37 restatement. | Updated, with §9.37's O1 line. |

### 9.41 Fix-verification of `66bde1e..26abd27` (non-author; `Query<T>()` probes on 4 providers × 6 cultures, typed date/time columns, `66bde1e` and HEAD)

Verdict: NOT CLEAN. H3 and H4 are resolved; H1 and H2 partly.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| J1 | minor | TEST-GAP | The per-position `DateTimeOffset` claims were too broad. Only SQL Server `datetimeoffset` and PostgreSQL `timestamptz` compare instants; `datetime2`, `timestamp`, `date` and every MySQL type drop the offset; SQL Server `datetime` fails with 241. A branch beside a `datetimeoffset`/`timestamptz` column sorts by instant. | The claims were removed from AC12-10 and the Changelog: the text format is stated, and comparison is the database's rule. The two shapes now pinned by tests are named. |
| J2 | minor | AC-GAP | MySQL: WHERE sends a `DateTimeOffset` as UTC, while ORDER BY sent offset text that MySQL drops, so the two disagreed. 3.9.0 failed there with 1525. | A MySQL `DateTimeOffset` is its UTC time; a test checks that WHERE and ORDER BY pick the same rows. |
| J3 | minor | AC-GAP | `DateOnly` became invariant `MM/dd/yyyy`: MySQL rejects it, a dmy session misreads it, and it doesn't sort across years. `TimeOnly` `HH:mm` dropped seconds. | ISO `yyyy-MM-dd` and `HH:mm:ss.FFFFFFF`; direct pins and branch-value oracle rows on all four. |
| J4 | nit | HOUSE-RULE | "Other value written with `ToString()`" and "Numbers stay inline" were too broad (`TimeSpan`'s text is unchanged; `Half` is a parameter). | The Changelog and AC12-10 name the three date and time types, and "C#'s built-in numeric types". |
| J5 | nit | HOUSE-RULE | "Failed under some cultures" omitted the day/month swap and MySQL failing under en-US. | Folded into the Changelog's "reject … or read with day and month swapped"; the per-provider detail was removed with J1. |
| J6 | nit | HOUSE-RULE | The Changed intro ("These shapes now throw") headed bullets that don't throw. | "Other changes:" now separates them. |

### 9.42 Fix-verification of `66bde1e..9ce9e86` (non-author; counts reproduced; 10 own mutants; 97.7 % visitor coverage; netstandard2.0 on .NET 6 for MySQL, PostgreSQL and SQLite)

Verdict: NOT CLEAN. J1, J2 and J5 are resolved; J3 and J4 partly; J6 not.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| F1 | minor | TEST-GAP | The `TimeOnly` fraction (`HH:mm:ss.FFFFFFF`) wasn't pinned; the `"HH:mm:ss"` mutant survived on all four. | A direct pin (`10:00:30.5`) and a sub-second oracle row on all four; the mutant re-run. |
| F2 | minor | HOUSE-RULE | The Security bullet's "carrying the literal's text, so the database converts them as it converted the literal" contradicted the date/time format change. | Restated: typed as the literal was, carrying its text except for date and time values. |
| F3 | nit | HOUSE-RULE | "Other changes:" needed a blank line before it to separate the lists (J6 not fixed). | Blank line added. |
| F4 | nit | HOUSE-RULE | `nint`/`nuint` are built-in numeric types but are parameters; Advanced.md still said "numbers … stay inline". | The eleven inline numeric types are listed in the Changelog, Advanced.md and AC12-10. |
| F5 | nit | HOUSE-RULE | The date/time bullet's harms didn't cover `TimeOnly`'s lost seconds. | Added. |

Recorded in §8: SQL Server `DateTime` under a day-first session (pre-existing).

### 9.43 Fix-verification of `66bde1e..6f503a9` (non-author; suites reproduced; own mutants per provider; pandoc render checks)

Verdict: NOT CLEAN.
- **Resolved:** F3 and F5.
- **Resolved as scoped, each leaving a test gap:** F1 and F4.
- **Partial:** F2. Its rewrite added two over-claims.

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| K1 | minor | HOUSE-RULE | "Every value that 3.9.0 quoted … is now a parameter": 3.9.0 quoted an enum as its name, and HEAD inlines its number. | The Security bullet and the AC12-10 lead-in say only what stays inline and that no other value is written into the SQL. |
| K2 | minor | HOUSE-RULE | "It carries the literal's text, except date and time values": a `Half` (`1,5` → `1.5`) and a negative `nint`, `BigInteger` or `Int128` (U+2212 → `-`) changed too. | The text claim is deleted from Security. The formatting bullet covers every value sent as a parameter (scoped so in rev 42, L1): the invariant culture, and fixed formats for dates and times. |
| K2b | nit | HOUSE-RULE | "No literal's text depends on the current culture": a type with neither `IConvertible` nor `IFormattable` is its own `ToString()`. | Deleted from the API bullet. The formatting bullet says `Convert.ToString` with the invariant culture and names that exception; AC12-10 says the same for values without a fixed format. A `DateTime` keeps 3.9.0's fixed format, now listed. |
| K3 | minor | TEST-GAP | F1's pin covered one fractional digit: `HH:mm:ss.F` survived on 4/4, and `.FFF` survived on SQL Server. | A `10:00:30.1234567` pin per provider. |
| K4 | minor | TEST-GAP | The eleven inline types were unpinned: dropping `decimal` from `IsNumber` survived every suite. | A direct test per provider: the eleven types inline with invariant text under fi-FI, and `nint`/`nuint`/`Half` as parameters. |
| K5 | nit | HOUSE-RULE | AC12-10's closing paragraph rendered inside the AC12-9 sub-bullet (lazy continuation). | Split into sub-bullets, and the duplicated inline sentence deleted. |
| K6 | nit | HOUSE-RULE | The visitors' doc comments still said "non-numeric … Numbers … stay inline" and "converts it as it converted 3.9.0's literal". | Restated in all four. |
| K7 | nit | HOUSE-RULE | Advanced.md's PostgreSQL parenthetical listed only strings, chars, `Guid`s and dates. The AI reference still said "(strings, chars, `Guid`s, dates)". | Both restated. |

### 9.44 Fix-verification of `66bde1e..9f2d531` (non-author; suites reproduced twice; 70 mutants, 42 its own, all killed; 97.7 % visitor coverage; pandoc)

Verdict: NOT CLEAN on documents only. The code is verified.
- **Resolved:** K1, K2b, K3, K4, K5, K6.
- **Resolved as worded, each leaving a new nit:** K2 (L1), K7 (L2).

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| L3 | minor | HOUSE-RULE | The §4.2 AC12-10 row and the §4.4 mutation table stopped at rev 33: the rev 39–41 tests and mutants weren't listed, and the direct-test count was stale. | Row and count updated; five mutation rows added. |
| L1 | nit | HOUSE-RULE | The formats list's "any other value" read as covering enums, booleans and `NULL`, which are inline. | The list is scoped to values sent as parameters (or quoted without a generator), and the inline set is restated. |
| L2 | nit | HOUSE-RULE | "Such a value compared with `null`" read as covering inline values; PostgreSQL still sends `5 IS NULL`. | Scoped to a would-be parameter, with the inline example. |
| L4 | nit | HOUSE-RULE | The rev 41 lists omitted K2b; "two guard tests" was one new test and one added pin. | Corrected. |

Incident: the verifier wrote a probe into the main checkout's PostgreSQL direct-test file for about three minutes,
then restored it. No build or test ran there. Checked afterwards: the file's blob equals HEAD's, and
`git diff HEAD` is empty.

### 9.45 Fix-verification of `66bde1e..8eaa0d0` (non-author; 26 mutants (23 per provider plus 3 MySQL-only), 95 provider-mutants, all killed by the named tests; fi-FI value probe in both modes; PostgreSQL null-test probe, 10 shapes)

Verdict: NOT CLEAN.
- **Resolved:** L1, L3 and L4.
- **Partial:** L2 (its AC12-10 sibling sentence was not updated: M2).

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| M1 | minor | TEST-GAP | Run alone, the MySQL `DateTimeOffset` test (rev 39) failed at cleanup and left its three rows. It seeded with raw SQL, so `Person` was cold when the cleanup ran `Delete<Person>(predicate)`, which hit the cold-cache Delete defect. The verifier deleted the 21 rows its runs leaked. | Seeded through `SeedTypedPerson`, which warms `Person`; run alone, it passes and leaves no row. |
| M2 | nit | HOUSE-RULE | AC12-10 said "on PostgreSQL, a value compared with `null` is decided in .NET"; an inline value is still sent (`5 IS NULL`, executed). | Scoped to a would-be parameter. |
| M3 | nit | HOUSE-RULE | Fix-introduced: the blank line before the inline sentence made the whole "Other changes:" list loose. | The sentence is folded into the lead-in. |
| M4 | nit | TEST-GAP | `ParameterMode_DateOnlyAndTimeOnly_AreIsoText` ran in the machine's culture. Under sv-SE, `DateOnly.ToString()` is already ISO, so the current-culture mutant survived there (executed). | Runs under fi-FI on all four. |
| M5 | nit | PLAN-GAP | §4.3 had no rows for the generator constructor, `Parameters` or `QueryComponents.OrderByParameters`. | Rows added. |
| M6 | nit | HOUSE-RULE | "Sub-second rows" described one assertion. | Reworded. |

### 9.46 Fix-verification of `66bde1e..4b7ab2b` (non-author; every QueryOperators method run alone on four providers; culture-forced runs under sv-SE and th-TH; M1/M4/M5 mutants)

Verdict: NOT CLEAN.
- **Resolved:** M2, M3 and M6.
- **Resolved for the named items, each with a residual:** M1 (N1), M4 (N2), M5 (N3).

| # | Sev | Blame | Finding | Disposition |
|---|---|---|---|---|
| N1 | minor | TEST-GAP | Three more MySQL tests failed when run alone, for the same cold-cleanup reason as M1, and one leaked rows: `ComputedMemberOrderBy_EmitsExpression_Unchanged`, `ComputedAttributeEntityWithoutJoins_OwnColumnOrder_Unqualified` and `Last_AfterComputedOrderBy_InvertsComputedTerm`. They predate Task 12. | The MySQL harness discovers `Person` in `TestInitialize` (the verifier's validated fix); each of the three passes run alone. Consequence recorded in §8: the cold-cache plan's AC4 no longer reproduces the defect on this branch. |
| N2 | nit | TEST-GAP | `DateOnly` with the current culture as format provider survived fi-FI, which is Gregorian with `-` as a literal; under th-TH it would send `2569-01-02`. | The test runs under fi-FI and th-TH; both provider mutants are killed. |
| N3 | nit | HOUSE-RULE | Fix-introduced: the §4.3 row said "PostgreSQL's equivalent", but PostgreSQL has the same member. | "All four providers". |
| N4 | nit | HOUSE-RULE | The §9.45 count: 26 × 4 ≠ 95. | 23 per provider plus 3 MySQL-only. |

---

## 10. v3.10.1 — aggregate correctness (D9): owner direction and open questions

**Owner direction (2026-09-30):** go with the recommended approach, with the refinements below.

- **`Average`:** computed exactly as `SUM(wide) / COUNT` client-side (LINQ semantics); SQLite's `ROUND` is
  removed.
- **Empty and nullable results** follow LINQ:
  - `Sum` → 0, even for nullable selectors;
  - nullable or reference `Min`/`Max`/`Average` → `null`;
  - non-nullable → throws "no elements".
- **`Min`/`Max` result types:**
  - **Enums:** reject. The message suggests aggregating the underlying number
    (`Max(x => (int)x.Status)`); confirm that FunkyORM translates the cast.
  - **`TimeSpan`:** reject, with guidance. Developers should store durations as `bigint`/`int` (ticks,
    seconds or ms) and aggregate that property. The message explains that time types differ by provider:
    - SQL Server `time` and PostgreSQL `time` hold values under 24 hours;
    - MySQL `TIME` spans ±838 hours;
    - Npgsql maps `TimeSpan` to PostgreSQL `interval`, which is unbounded.
  - **`Guid`:** reject by default, with a message explaining that the database's ordering differs from
    .NET's. Developers can **opt in** to server-side ordering (mechanism open — below).
  - **`DateTimeOffset`:**
    - SQL Server `datetimeoffset` and PostgreSQL `timestamptz` both order by UTC instant, as .NET does, so
      allow them there. PostgreSQL returns a UTC `DateTime` that gets converted.
    - MySQL has no offset type, and SQLite stores text that sorts wrongly across offsets. Reject on both,
      with a message suggesting UTC `DateTime` storage. *To verify.*

**Guid ordering by provider** (from documentation and reasoning; verify in the 3.10.1 Task 0):

| Provider | Storage | Order vs .NET `Guid.CompareTo` |
|---|---|---|
| SQL Server | `uniqueidentifier` | **Differs.** Compares byte groups 10–15, then 8–9, 6–7, 4–5, 0–3. |
| PostgreSQL | `uuid` | **Should match.** Both compare in canonical string order. |
| MySQL | `CHAR(36)` / `BINARY(16)` | Text matches if the case is consistent; binary depends on MySqlConnector's `GuidFormat`. |
| SQLite | `TEXT` / `BLOB` | Text matches if the case is consistent; a blob in .NET byte order differs. |

**Owner answers (2026-09-30):**
1. **Guid opt-in: a provider-wide option**, set at startup/DI, e.g.
   `new SqlServerOrmDataProvider(conn) { QueryOptions = { GuidOrdering = GuidOrdering.Database } }`.
   - **Provider-aware default:** PostgreSQL defaults to *allowed*; SQL Server, MySQL and SQLite default to
     *rejected*.
   - **Hard prerequisite:** the 3.10.1 Task 0 must verify that PostgreSQL `uuid` `MIN`/`MAX` matches .NET
     `Guid.CompareTo` on discriminating data. If it doesn't, the PostgreSQL default falls back to *rejected*,
     and the owner is told.
   - (b) a per-property attribute and (c) a per-query marker weren't chosen; they can be added later if
     targeted control is needed.
2. **The Guid guard does not cover `OrderBy`/`ThenBy`** on Guid columns. The ordering difference is
   documented instead; `OrderBy(Guid)` is usually just a stable tie-breaker.
3. **String `Min`/`Max` use the database's collation**, documented, since they can't match .NET's
   culture-sensitive comparison anyway. Numeric results target exact LINQ-to-objects semantics.

**Inputs from the r2 fix-verification (to fold into the 3.10.1 plan):**
- **F3:** NULL-result rule order.
- **F4:** type failures.
- **F5:** `Average` precision.
  - SQLite `ROUND(…, 10)`.
  - MySQL `AVG` scale 4.
  - Decimal scales on SQL Server / MySQL / PostgreSQL.
  - Key the widening on the column type, not the selector type.
  - Seed {1, 1, 2} rows for `int` and `decimal` on every provider.
- **F6:** a new aggregate test table in all five schema scripts, with `int`/`long`/`float`/`double`/`decimal`
  columns in nullable and non-null forms, plus `DateTime`, `string`, `Guid`, `DateTimeOffset`, `TimeSpan` and
  an enum.
- **F15:** collation-neutral strings and a floating-point tolerance.
