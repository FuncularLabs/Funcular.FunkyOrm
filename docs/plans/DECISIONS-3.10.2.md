# FunkyORM 3.10.2 — Decisions log

Append-only. Every decision gets an ID, a date/time (CT), who decided, the decision, and its rationale. Agents must read this file before changing anything and must append a row for every decision they make.
Baseline: `origin/master` @ `8c4648d` (3.10.1). Working branch: `release/3.10.2`. Deadline: Sunday 2026-10-18.
Source: Grok Bot audit 2026-10-14 (`funkyorm-defects-mvp.md`; the full backlog is in `BACKLOG_AFTER_3.10.0.md`).

## Binding rules
| ID | When (CT) | By | Decision |
|---|---|---|---|
| D-001 | 2026-10-14 18:42 | Paul | **WHERE parity.** A WHERE condition must be evaluated identically whether it sits under SELECT, UPDATE or DELETE. There is ONE shared predicate/parameter translation path, with no separate branches per operation. The FD-01 and FD-02 fixes must *remove* any divergent delete/update path rather than patch it. They must also add parity tests that run the same predicates through Select, Update and Delete and assert matching row sets. If merging `fix/where-captured-values` would keep a separate path, rework it instead. |
| D-002 | 2026-10-14 18:39 | Paul | Scope for 3.10.2 is the fix-before-push list below. Work goes on `release/3.10.2` only: no new branches or worktrees. Test-first. One commit per fix. Run the full SQLite suite after each fix. Nothing is pushed without Paul's go. |
| D-003 | 2026-10-14 18:45 | Grok Bot (needs Paul's confirmation) | **Open question on D-001 vs the 3.10.0 delete guard (D9).** `DeletePredicateGuard` rejects some shapes in Delete only (for example a LIKE wildcard): the probe shows Select returns 4 rows while Delete throws. Proposal: translation stays single-path, and any rejection applies to Select, Update and Delete alike, so parity tests also assert identical exceptions. Alternative: keep D9 as a delete-only safety check *before* translation and exclude it from parity. **Paul to decide.** Until then, Claude does not modify D9 and records its parity results. |

## Fix before push
| FD | Title | Plan | Existing work |
|---|---|---|---|
| FD-02 | Captured object value in `Contains` read as a column (deletes widen) | Bring over the `fix/where-captured-values` design (RowIndependentEvaluator), reworked to D-001. Add parity tests. | `fix/where-captured-values` @0b19ef2 (CLEAN, Task 6 open) |
| FD-01 | `x.D.Year` / `x.Name.Length` silently dropped (Delete wiped 4/4) | Translate date parts and Length, or throw `NotSupportedException` for unhandled member-of-member shapes, in the shared visitor. Add parity tests. | none |
| FD-03 | LIKE wildcards `% _ [` not escaped | Bring over `fix/like-escaping-tostring` (LikePattern, `ESCAPE '!'`). | `fix/like-escaping-tostring` @75e172f (CLEAN) |
| FD-04 | Aggregates (Average truncation/precision, decimal/float throw, empty nullable sets, Min/Max types) | Per QUERY_OPERATOR_CORRECTNESS_PLAN A10. | none |
| FD-05 | NuGet doesn't declare PG/MySQL/SQLite drivers; versions float | Pin minimums, document per provider, add a consumer smoke test. | none |
| FD-06 | Docs describe non-existent `MySqlStringComparison`/`SqliteStringComparison`; NuGet description omits MySQL | Docs fix; mark the enums `[Obsolete]` or remove them. | none |
| FD-07 | `Console.WriteLine("Generated ORDER BY clause…")` in 4 visitors | Remove. | none |
| FD-13 | GH #12/#13 open though fixed | **DONE 2026-10-14 18:42 CT**. Closed with evidence: SQLite suite 800/800 on 8c4648d; SqliteOrderByQualificationTests, SqliteQueryOperatorSemanticsTests, SqliteQueryOperatorRejectionTests. | — |

Phase-2 order (Claude): FD-02 → FD-01 → FD-03 → FD-04, then stop. FD-05/06/07 come in a later phase.

## Disclose or defer
| FD | Title | Decision |
|---|---|---|
| FD-08 | Null semantics (`!= v` skips nulls; captured null `==`) | Disclose in Known issues; fix in 3.11 (it widens deletes). |
| FD-09 | `is IOrderedQueryable` sort helper crashes (#16) | Fix if capacity allows, else disclose. |
| FD-10 | Update with enum/unsigned key fails | Already disclosed; 3.10.2 if cheap. |
| FD-11 | SQLite Guid/DateTime/culture | Disclose; defer (OD-12). |
| FD-12 | Remaining captured-value shapes (StartsWith receiver, captured `this`) | Comes in with FD-02 if it falls out of the shared path; otherwise 3.11. |

## Branch decisions (relative to origin/master 8c4648d)
| Branch | Head | Status | Decision | FD |
|---|---|---|---|---|
| fix/where-captured-values | 0b19ef2 | 14 commits not on master; 81 behind | **Merge, reworked to D-001** | FD-02, FD-12 |
| fix/like-escaping-tostring | 75e172f | 14 commits not on master; 15 behind | **Merge** (check parity with D9 per D-003) | FD-03 |
| fix/test-row-hygiene | 9c37ac0 | 13 commits not on master; test-only | Merge after FD-01..04 (needs OD-4..6) | — (WP-D) |
| fix/delete-guard-trivial-predicates | 867d6c1 | Merged (3.10.0) | Obsolete; delete after 3.10.2 ships | context for D-003 |
| fix/provider-scoped-caches | 4652a3e | Merged (3.10.0) | Obsolete; delete later | — |
| fix/mysql-delete-cold-cache | 150078d | Merged (3.10.0) | Obsolete; delete later | — |
| claude/epic-dirac-742683, claude/epic-khayyam-5af96c, claude/festive-sammet-7b0e05, claude/infallible-maxwell-477798, claude/tofco-ag-presales-research-5c7dfc | fae4472 | Ancestor of master; no unique commits | Obsolete; remove the branches and the khayyam/festive-sammet worktrees (with Paul's OK) | — |

## Log (append below)
| ID | When (CT) | By | Decision / rationale |
|---|---|---|---|
