# Plan: audit context on transactions FunkyORM didn't begin (3.13.0, issue #24)

Status: rev 2, 2026-10-27. Owner rulings (Paul, 2026-10-27 ~19:45 CT): **prime once, fail closed**; ship as **3.13.0**;
release overnight once review is CLEAN and the PR's CI is green; review exit rule **no Major left** (Major = wrong
behaviour, a missed fail-closed path, a throw where it should work, false docs, or a test that can't fail).

**Deviation, recorded:** implementation began while Task 0 was still under review (owner's overnight timeline). Task 0's
findings were applied before the first review round; see §6.

## 1. The defect

FunkyORM primes audit context (`AuditContextOptions`) in two places: each connection `ConnectionScope` opens for an
operation, and the connection of a transaction started with `BeginTransaction()`. A transaction the provider didn't begin
(passed to the constructor, or assigned to the public `Transaction` property) runs through `ConnectionScope`'s
transactional branch, which neither primes nor calls `ResolveAuditContextForPriming()`; and `Delete`/`DeleteAsync` (by
predicate and by key) built their command on `Connection` without any `ConnectionScope`. So:

- the keys are never set on that connection (an RLS policy reading them sees NULL);
- `RequireAuditContext = true` isn't enforced (a strict provider runs with no context);
- on SQLite, a strict provider's `NotSupportedException` guard isn't reached either.

Affected: SQL Server, PostgreSQL, MySQL (priming) and SQLite (strict guard), every release since 3.8.0.

## 2. Acceptance criteria

- **AC-1** A transaction the provider didn't begin, with a context present, is primed on the connection its commands run
  on, before the operation's own (non-bootstrap) commands of the first operation in that transaction; later commands in
  it see the keys (on PostgreSQL, unless the application rolls back to a savepoint taken before that first operation:
  the keys are set inside the transaction). Holds for a constructor-supplied transaction and for one assigned to
  `Transaction`, sync and async, `Delete`/`DeleteAsync` included.
- **AC-2** Primed once per provider per transaction: later operations in the same transaction send no further priming
  or reading commands, and a different transaction on the same provider is primed again. (Command counts are tested on
  SQL Server.)
- **AC-3** Strict provider, no context, transaction it didn't begin: `InvalidOperationException` (the existing
  message) before the operation's own commands are sent. The provider stays usable: the transactional-scope guard is
  released, and a later operation with a context primes and runs. Once the transaction is primed, later operations in
  it aren't checked again, as after `BeginTransaction()`.
- **AC-4** Keys already on that connection (another provider, `BeginTransaction`, or the application set them):
  - a key already holding the entry's value is not set again (no SQL Server Msg 15664 for read-only keys); it keeps
    whatever read-only setting it had (PostgreSQL: an empty entry value is always set, since `''` reads as unset);
  - a key holding a **different** value throws `InvalidOperationException` when the entry is `ReadOnly`; the message
    names the key and never a value;
  - a key holding a different value is overwritten when the entry is not `ReadOnly` (on SQL Server, unless the existing
    key was set read-only: SQL Server's Msg 15664);
  - an unset key is set (PostgreSQL: an empty string reads as unset, since a reset placeholder setting reads `''`).
- **AC-5** Lenient provider, no context, transaction it didn't begin: runs, primes nothing, throws nothing, and doesn't
  mark the transaction, so a later operation with a context primes it.
- **AC-6** Feature off (no accessor): no extra command on any path (unchanged).
- **AC-7** An operation under `SystemContextScope` (as FunkyORM's own bootstrap queries run) inside such a transaction
  primes nothing, isn't failed closed, and does not mark the transaction primed.
- **AC-8** `BeginTransaction()` is unchanged: it primes once before the transaction begins (as in 3.12.1), and its
  transaction is not read or primed again by later operations. (Tested on SQL Server.)
- **AC-9** SQLite: a strict provider throws its existing `NotSupportedException` on an operation in a transaction it
  didn't begin, `Delete` included, and the guard is released.
- **AC-10** Docs and release: the runbook (§1, §4, security note 6, fail-closed section, troubleshooting), the AI
  instructions, README recent changes, Changelog `[3.13.0]`, every version carrier at 3.13.0, and the XML comments on
  `SessionContextEntry.ReadOnly` and `AuditContextOptions.RequireAuditContext`; the PostgreSQL prime method's stale
  summary corrected.

## 3. Design

- **Every operation's commands run inside a `ConnectionScope`, `Delete` included.** The 16 delete methods (4 per
  provider) keep `GenerateWhereClause`, `DiscoverColumns` and `GetTableName` before the scope (so scopes never nest)
  and run the command on `scope.Connection`.
- **Per-provider marker:** each provider gets `private IDbTransaction _auditPrimedTransaction`. `BeginTransaction()`
  sets it to the new transaction after its existing prime (AC-8); `CleanupTransaction()` clears it. `ConnectionScope`'s
  transactional branch, after the concurrency guard increments, calls `PrimeTransactionWithAuditContext(GetConnection())`;
  on any exception the scope decrements the guard before rethrowing (AC-3).
- **`PrimeTransactionWithAuditContext()`:** returns when `ReferenceEquals(_auditPrimedTransaction, Transaction)`;
  otherwise `ResolveAuditContextForPriming()` (throws when strict and absent; null under `SystemContextScope`, when
  off, or lenient and absent). Null or no entries: return **without marking** (AC-5, AC-7). Otherwise validate keys
  (PostgreSQL, MySQL), read the entries' current values on that connection (one command), choose the entries to set
  with the core rule (AC-4), set them (one command, the provider's existing priming SQL; none if nothing to set), then
  mark.
- **Core rule:** `OrmDataProvider.SelectSessionContextEntriesToSet(entries, existingValues, emptyMeansUnset)`
  (`protected internal static`), shared by the three providers and unit-tested without a database.
- **Reads, per provider:** SQL Server `SELECT CONVERT(nvarchar(4000), SESSION_CONTEXT(@__sck0)), …`; PostgreSQL
  `SELECT current_setting(@__sck0, true), …` (`emptyMeansUnset: true`); MySQL `SELECT CAST(@key AS CHAR), …` (keys
  validated before the read). Each command enlists the transaction and goes through `InvokeLogAction` (keys only).
- **SQLite:** `GuardAuditContextUnsupported()` is also called in `ConnectionScope`'s transactional branch (AC-9),
  with the same guard release on throw.
- Not changed: the owned-connection path, `BeginTransaction()`'s prime, the audit comment.

## 4. Test plan

Tests for unchanged behaviour (BeginTransaction_LaterOps, FeatureOff, Lenient) pass before the fix; they are proven by
mutation instead of by a red run. Log assertions match ordinally: `SESSION_CONTEXT(` (the read) and
`sp_set_session_context` (the set) are distinct. Mutations run with `C:\temp\compare\r3130\mutate.py`.

| AC | Test | Project | Mutation it must kill |
|:--|:--|:--|:--|
| 4 | `SelectEntriesToSet_*` (unset, equal, read-only mismatch throws without values, writable mismatch, empty-means-unset both ways, mixed order, count mismatch) | SqlServer.Tests (core) | M5: overwrite a read-only mismatch; M5': throw on a writable one |
| 1 | `ExternalTransaction_ConstructorSupplied_PrimesAndRlsFilters` | SQL Server, PostgreSQL | M1: no prime in the transactional branch |
| 1 | `ExternalTransaction_ConstructorSupplied_Primes` | MySQL | MY-M1 |
| 1 | `ExternalTransaction_Assigned_PrimesAndRlsFilters`, `ExternalTransaction_Async_Primes` | SQL Server | M1 |
| 1, 3 | `ExternalTransaction_DeleteFirst_Primes`, `ExternalTransaction_Delete_Strict_NoContext_Throws`, `ExternalTransaction_DeleteAsync_Strict_NoContext_Throws` | SQL Server | M13a/b: deletes outside the scope |
| 1, 3 | `ExternalTransaction_Deletes_Strict_NoContext_Throw` (sync and async, predicate and key) | PostgreSQL, MySQL | PG-M13a/b, MY-M13a/b (round 1 m1) |
| 4 | `SelectEntriesToSet_ComparesOrdinally_CaseDifferenceIsAMismatch` | SqlServer.Tests (core) | M15: case-insensitive comparison (round 1 m6) |
| 2 | `ExternalTransaction_PrimesOnce_NoSecondReadOrSet` (exactly 1 read, 1 set over 3 operations) | SQL Server | M8: never mark |
| 2 | `ExternalTransaction_Reassigned_NewConnectionAndTransaction_PrimesAgain` | SQL Server | M14: once per provider |
| 8 | `BeginTransaction_LaterOps_DoNotReadOrRePrime` (0 reads, 1 set over 3 operations) | SQL Server | M6: BeginTransaction doesn't mark |
| 3 | `ExternalTransaction_Strict_NoContext_Throws_ThenRecovers` | SQL Server, PostgreSQL, MySQL | M2: mark before resolving; M3: no guard release |
| 4 | `SharedTransaction_SameContext_SecondProviderRuns` | SQL Server (PostgreSQL: behaviour pin only) | M4: always set (Msg 15664) |
| 4 | `SharedTransaction_DifferentReadOnlyValue_Throws` / `ExternalTransaction_DifferentReadOnlyValue_Throws` | SQL Server, PostgreSQL / MySQL | M5 |
| 4 | `ExternalTransaction_WritableKeyDifferentValue_Overwritten` | SQL Server | M5' |
| 4 | `ExternalTransaction_ResetSettingReadsEmpty_IsTreatedAsUnset` | PostgreSQL | PG-M7: `''` as a value |
| 4 | `ExternalTransaction_InvalidKey_ThrowsBeforeAnyCommand` | MySQL | MY-validate: no key check before the read |
| 5 | `ExternalTransaction_Lenient_NoContext_RunsUnprimed` | SQL Server | M9: resolve as strict |
| 5, 7 | `ExternalTransaction_Lenient_NoContextThenContext_Primes`, `ExternalTransaction_SystemScopeOperation_DoesNotMarkPrimed` | SQL Server | M12: mark when nothing was primed; M2 |
| 6 | `ExternalTransaction_FeatureOff_NoAuditCommands` | SQL Server | behaviour pin |
| 7 | `ExternalTransaction_ColdEntity_DiscoveryThenPrime` | SQL Server | smoke test (discovery, then prime) |
| 9 | `StrictProvider_OnSqlite_ExternalTransaction_ThrowsNotSupported` (GetList and all four deletes, then lenient works) | SQLite | M11: no guard; M13-SQLite; LITE-M13b; M3-SQLite |
| 10 | `VersionCarriers_MatchTheChangelog` (existing) | SqlServer.Tests | — |

Opt-in suites: the integration tests need FUNKY_CONNECTION (.\SQL2019), the PostgreSQL non-superuser role
`funky_rls_tester`, and the MySQL connection, as the existing audit tests do; they run in CI's four provider jobs.
Coverage: line coverage of the new and changed methods, and each touched file's line rate, are reported from the
cobertura runs; the provider files are large and their whole-file rate predates this change.

## 5. Tasks

0. This plan's test design reviewed by a non-author agent (Task 0): rev 1 NOT CLEAN (2 Major), applied in rev 2.
1. Core rule + its unit tests.
2. SQL Server: marker, `PrimeTransactionWithAuditContext`, guard release, scoped deletes; its integration tests.
3. PostgreSQL and MySQL: the same; their tests.
4. SQLite guard and scoped deletes; its test.
5. Docs, Changelog, versions.
6. Full suites (four providers, net48), coverage, mutations, hostile review rounds under the exit rule, then release.

## 6. Task 0 (rev 1) dispositions

| Finding | Severity | Blame | Disposition |
|:--|:--|:--|:--|
| M-A Delete/DeleteAsync bypass ConnectionScope (16 methods) | Major | PLAN-GAP | Fixed: deletes scoped (§3); M13 tests on SQL Server and SQLite |
| M-B AC-7 test can't fail; "return without marking" unproven | Major | TEST-GAP | Fixed: SystemScope and Lenient-then-context tests kill M12 (and M2); cold-entity test kept as a smoke test |
| 1 M4 not killable on PostgreSQL | Minor | TEST-GAP | Claim narrowed to SQL Server |
| 2 AC-2/AC-8 SQL Server only; per-transaction unproven | Minor | TEST-GAP | Reassigned test (M14); ACs say "tested on SQL Server" |
| 3 AC-4 overwrite false when the existing SQL Server key is read-only | Minor | AC-GAP | AC-4, runbook, Changelog narrowed |
| 4 PostgreSQL savepoint rollback undoes the keys | Minor | AC-GAP | Caveat in AC-1, runbook note 6, Changelog |
| 5 "before the first command" vs bootstrap | Minor | AC-GAP | Reworded |
| 6 fail-closed once per transaction; MySQL AllowUserVariables | Minor | HOUSE-RULE (comment truth) | Runbook, RequireAuditContext XML, Changelog |
| 7 MySQL invalid-key and SQLite guard-release tests missing | Minor | TEST-GAP | Added (MY-validate, M3-SQLite) |
| 8 coverage line inconsistent | Minor | PLAN-GAP | §4 states what is measured |
| Nits (M6 spec, red-first header, table rows, PG empty entry, marker on connection, cleanup) | Nit | — | Applied except recording the connection in the marker (SqlClient and MySqlConnector reject a transaction from another connection) |

Review rounds: round 1 (`1cef722..4c75864`) CLEAN, 6 Minor + 5 Nit, applied in `eee0e77`; round 2 (`1cef722..eee0e77`)
CLEAN, 2 Minor (wording) + 5 Nit, applied in the next commit (two optional nits left: "a context with entries", and the
PostgreSQL savepoint edge for a key unset before the savepoint, which already reads as unset). 26 mutations, all killed
(`mutate2.out`).
