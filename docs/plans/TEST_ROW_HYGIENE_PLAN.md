# Test row hygiene: integration tests leave the shared databases as they found them — Implementation Plan

> **Goal:** every integration test that inserts rows into `person` or a related table deletes them, and every such
> test checks, as it finishes, that it left those tables' row counts as it found them.
> - Ships in **3.10.0** (owner decision 2026-10-03). Test projects and documents only; no library code changes.
> - Branch `fix/test-row-hygiene`, cut from `origin/development/3.10` at `35c6477`. It merges into `development/3.10`
>   on its own: every class that leaks is already on `development/3.10` (§1.1), and the classes the in-flight 3.10
>   branches add are clean (§1.2).
> - Scope (owner decision 2026-10-03): `person` and its related tables (D5). `rls_demo`, `non_identity_*` and
>   MySQL's `user` also leak; they are recorded in §6 for a follow-up.
> - Owner directive for 3.10 (2026-10-03): keep it simple, no edge-case accommodation.

> **Status (2026-10-03):** rev 1. Task 0 (non-author review of this plan) is next.

## 1. Verified premises (author, against `ca82741` and `6014d8c`; the Task 0 reviewer re-checks them)

### 1.1 Which classes leave rows, per passing run

A static audit of every test class in each suite, read in full, accounts for the measured growth exactly. The
measurements (owner, 2026-10-03, at `ca82741` and `fix/like-escaping-tostring`) were SQL Server +177 `person` rows
per run, PostgreSQL +87 and MySQL +59.

| Suite | Class | person | Other tables |
|---|---|---|---|
| SQL Server | `SqlDataProviderIntegrationTests` | 130 | address 4, person_address 3, organization 3 |
| | `SqlDataProviderIntegrationAsyncTests` (own hooks, not the fixture) | 18 | address 2, person_address 1 |
| | `StoredProcedureIntegrationTests` (own hooks) | 18 | – |
| | `RemoteFeaturesTests` | 5 | country 5, address 5, organization 5 |
| | `DocumentationGapTests` | 3 | – |
| | `RemoteKeyIntegrationTests` | 2 | country 1, address 1, organization 2 |
| | `RemoteKeyReverseTests` | 1 | country 1, address 1, person_address 1 |
| | **Total** | **177** | matches the measurement |
| PostgreSQL | `PostgreSqlDataProviderIntegrationTests` | 65 | address 3, person_address 2 |
| | `PostgreSqlDataProviderIntegrationAsyncTests` (own hooks) | 9 | address 2, person_address 1 |
| | `PostgreSqlRemoteFeaturesTests` | 5 | country 5, address 5, organization 5 |
| | `PostgreSqlRemoteKeyIntegrationTests` | 2 | country 1, address 1, organization 2 |
| | `PostgreSqlStoredProcedureTests` | 6 | – |
| | `PostgreSqlComputedAttributeIntegrationTests` (own hooks) | 0 | organization 3 |
| | **Total** | **87** | matches the measurement |
| MySQL | `MySqlDataProviderIntegrationTests` | 20 | – |
| | `MySqlDataProviderIntegrationAsyncTests` | 5 | – |
| | `MySqlRemoteFeaturesTests` | 4 | organization 3 |
| | `MySqlJsonAndComputedTests` | 12 | organization 34, project 54, project_milestone 81 |
| | `MySqlStoredProcedureTests` | 18 | – |
| | **Total** | **59** | matches the measurement |
| net48 (`SqlServer.Tests.NetFramework`) | `SqlDataProviderNetFrameworkIntegrationTests` (own hooks) | 83 | address 3, person_address 2 |
| | `SqlDataProviderNetFrameworkIntegrationAsyncTests` (own hooks) | 18 | address 2, person_address 1 |
| | **Total** | **101** | not measured before; the two classes `ContinuousIntegration.playlist` runs |

- Nearly every leak is an insert with no delete at all. A few tests delete only one of the rows they insert
  (`Delete_DoesNotAffectOtherEntities` and its async twin), or commit their own transaction and never delete
  (`Transaction_BeginCommit`, `Transaction_MultipleOperations`).
- Every other class nets zero on a passing run: QueryOperators (about 1,600 `person` rows inserted and deleted per
  SQL Server run), ColdCacheDelete, ComputedAttribute and JsonPath on SQL Server, the own-table harnesses
  (`zz_*`, `funky_*`), and the delete-guard, LIKE and provider-scoped-cache tests.
- The affected files are identical on `development/3.10` and `ca82741` (`git diff --stat` lists none of them).

### 1.2 What isn't affected

- **DotNet9** (`SqlServer.Tests.DotNet9`) is xUnit and inserts nothing: `ContainsTests` reads, the policy tests use
  no database. The "DotNet9 adds some too" observation isn't explained by its code; Task 5 measures it once.
- **SQLite** uses a temp file or `:memory:` per class; no shared database.
- **`Funcular.Data.Orm.NetFramework.Tests`** (the legacy project) isn't in `FunkyORM.sln` and doesn't build (its
  project reference names a file that doesn't exist).
- The in-flight branches add only clean classes: ColdCacheDelete, provider-scoped caches, delete guard,
  LIKE/ToString, captured values. One test there depends on leaked rows (§6).

### 1.3 Facts the design rests on

- **MSTest 3.1.1** (SQL Server, PostgreSQL and MySQL suites), probed in a throwaway project:
  - Order: base `[TestInitialize]`, derived `[TestInitialize]`, test, derived `[TestCleanup]`, base `[TestCleanup]`.
  - An exception from `[TestCleanup]` fails that test.
  - `[ClassCleanup]` runs at the end of the assembly by default, and its failure is reported on whichever test ran
    last, in another class. With `ClassCleanupBehavior.EndOfClass` a failing class cleanup ran twice. Class-level
    checks are therefore unusable here (D4).
  - An `[AssemblyCleanup]` failure fails the run, reported on the last test.
- **net48** uses MSTest 2.2.10, C# 7.3 (no `LangVersion`), and an explicit `<Compile Include>` list.
  `Funcular.Data.Orm.Tests.Shared` files are already linked into it and into DotNet9 (`SupportedOperatorSignatures.cs`).
- **No parallelism**: no `[Parallelize]` attribute or runsettings in any suite.
- **No class- or assembly-level seeding**: no `[ClassInitialize]` or `[AssemblyInitialize]` exists in the affected
  classes, so a check per test covers each class.
- **Provider transactions don't nest.** `BeginTransaction` throws "Transaction already in progress." when one is open
  (`SqlServerOrmDataProvider.cs:938`, `PostgreSqlOrmDataProvider.cs:414`, `MySqlOrmDataProvider.cs:389`). The suites
  call `BeginTransaction()` at 35 sites (SQL Server, CI filter), 20 (PostgreSQL) and 9 (MySQL); four tests assert
  that `Delete` throws when no transaction is open. Every provider `Delete` overload requires an open transaction.
- **Foreign keys don't cascade** in any schema script. The insert order of a seeded chain (country → address →
  organization → person → person_address) is a valid delete order when reversed.
- **Guarded tables exist in the schema scripts:** SQL Server and MySQL create all eight of D5's tables; PostgreSQL's
  script creates no project tables (the ComputedAttribute and JsonPath tests create them).
- **Entities carry `[Table]`** (`System.ComponentModel.DataAnnotations.Schema`), inherited by the `Domain.Objects`
  types, and an `Id` property, in all four suites.
- **Tests that pass only because of leaked rows** (`integration_test_data.sql` seeds no organization or country, and
  no `employer_id`). They go Inconclusive on a fresh database once the leaks stop:
  - SQL Server `RemoteFeaturesTests` `RemoteProperty_IsPopulated_OnQuery` (31), `RemoteKey_IsPopulated_OnQuery`
    (168), `CanFilterBy_RemoteProperty` (187), and `OrderBy_RemoteProperty_OrdersByJoinedColumn` (51), whose order
    check runs only with two or more rows. `RemoteKeyWhereTests` `CanFilterByRemoteKey` (11) and
    `CanFilterByRemoteProperty` (38).
  - PostgreSQL `PostgreSqlRemoteFeaturesTests` `RemoteProperty_IsPopulated_OnQuery` (27),
    `RemoteKey_IsPopulated_OnQuery` (40), `CanFilterBy_RemoteProperty` (53) and the conditional
    `OrderBy_RemoteProperty_OrdersByJoinedColumn` (69); `PostgreSqlRemoteKeyWhereTests` `CanFilterByRemoteKey` (9) and
    `CanFilterByRemoteProperty` (28).
  - Found by the audit and by `grep Assert.Inconclusive` over the four suites; no other data-dependent
    Inconclusive remains.

## 2. Decisions

- **D1 — Delete what was inserted; don't wrap tests in a rolled-back transaction.** A wrap can't hold the tests
  that open their own transaction or the four that need none open (§1.3); PostgreSQL aborts the whole
  transaction on any engine error, which several tests provoke; several classes use second providers, logins or raw
  connections. Tracking each inserted row and deleting it after the test has none of these problems, and
  QueryOperators and ColdCacheDelete already do it.
- **D2 — One shared helper, `TestRowLedger`,** in `Funcular.Data.Orm.SqlServer.Tests/Shared/TestRowLedger.cs`,
  namespace `Funcular.Data.Orm.Tests.Shared`, linked into the PostgreSQL, MySQL and net48 test projects. Written in
  C# 7.3 with explicit `using`s (net48 has neither implicit usings nor a newer compiler). Members:
  - `static TestRowLedger Begin(Func<DbConnection> newConnection, IReadOnlyList<string> tables)` reads the row count
    of each table in one statement and keeps it.
  - `void Track(object entity)` records the entity's table (from its `[Table]`, inherited) and its `Id`. It throws
    `ArgumentException` for an entity whose table isn't in the ledger's list, or with no `Id`.
  - `void Finish()` deletes the tracked rows (D3), reads the counts again, and fails (D4) if a delete failed or a
    count changed.
  - `static readonly string[] SqlServerTables, PostgreSqlTables, MySqlTables` (D5).
- **D3 — How rows are deleted.** Newest first, one `DELETE FROM {table} WHERE id = @id` per tracked row, on a new
  connection in autocommit mode (not the provider under test, which needs a transaction to delete and runs the
  delete guard). Every delete is attempted; a failure is recorded and the next is tried. A delete that removes no
  row (the test deleted it, or rolled it back) is fine. The hooks dispose the provider *before* `Finish`, so a
  transaction a failing test left open is rolled back and its locks released first. Nothing else is deleted: no
  name, marker, predicate or id-range deletes, and rows earlier runs left stay (owner rule, §6).
- **D4 — The check is per test.** `Begin` runs in `[TestInitialize]`; `Finish` runs in `[TestCleanup]`, after the
  derived class's own cleanup (MSTest order, §1.3). A failure is `Assert.Fail` with the table, the count before and
  after, and any delete that failed. Because no affected class seeds at class level, a test-level check is a
  class-level check. Class- and assembly-level hooks were rejected (§1.3).
  - **Disclosed limitation:** another process writing to these tables while a test runs also changes the counts and
    fails the check. That matches the house rule that database work runs serially; the failure message says so.
    No opt-out (owner directive).
- **D5 — Guarded tables.** `person`, `address`, `person_address`, `organization`, `country`, plus `project`,
  `project_milestone`, `project_note` where the provider's schema script creates them (SQL Server, MySQL; not
  PostgreSQL). Out of scope: `rls_demo`, `non_identity_*`, `user` (§6).
- **D6 — Where the hooks go.**
  - `SqlServerTestFixture` and `PostgreSqlTestFixture`: `Setup` calls `Begin` after it builds the provider;
    `Cleanup` disposes the provider, then calls `_rows?.Finish()`. A `protected void Track(object entity)` forwards
    to the ledger. Every fixture subclass gets the check, not only the leaking ones.
  - `MySqlTestFixture`: `InitProvider` calls `Begin`; `DisposeProvider` disposes, then calls `Finish`. Every fixture
    subclass already calls these two from its own hooks.
  - Classes with their own hooks do the same in their `Setup`/`Cleanup`: SQL Server
    `SqlDataProviderIntegrationAsyncTests`, `StoredProcedureIntegrationTests`; PostgreSQL
    `PostgreSqlDataProviderIntegrationAsyncTests`, `PostgreSqlComputedAttributeIntegrationTests`; net48
    `SqlDataProviderNetFrameworkIntegrationTests` and its async twin.
  - `DocumentationGapTests` calls `base.Setup()` a second time from its own `Setup` (pre-existing). The second
    `Begin` replaces the first ledger, which has tracked nothing yet; no change needed.
- **D7 — Every insert into a guarded table is followed by `Track`.** The fixture helpers (`InsertTestPerson`,
  `InsertTestAddress`, `InsertTestPersonAddress` and their async copies) track; each test that calls
  `_provider.Insert` directly gets `Track(entity)` on the next line, including inserts inside the test's own
  transaction, committed or rolled back. No test's assertions change.
- **D8 — Tests that read leaked rows seed their own (§1.3).** A fixture helper `SeedEmployerChain()` (SQL Server
  and PostgreSQL) inserts and tracks country → address → organization → person and returns the person's id and the
  country's id and name. `RemoteFeaturesTests.SeedForwardRemoteChain` is replaced by it. Each listed test seeds a
  chain, filters to its own person, and asserts the remote values equal what it seeded. The `Assert.Inconclusive`
  branches go; the ordering test seeds two chains so its order check always runs.
- **D9 — Documents.** The Changelog gets one line under 3.10.0-beta1 › Changed ("tests only"). `TestRowLedger` and
  each fixture's XML doc state the rule (track every insert into a guarded table). No contributor guide covers
  integration tests today; none is added.

## 3. Acceptance criteria

- **AC1** — After any test in an affected class (§1.1) passes, the provider's guarded tables (D5) have the row
  counts they had before it ran. So a run of each affected class leaves `person` as it found it.
- **AC2** — A run of each suite, with nothing else using the database, leaves the guarded tables' counts unchanged:
  SQL Server (the CI filter), PostgreSQL, MySQL, and net48's two playlist classes.
- **AC3** — Cleanup deletes only rows the test inserted, by tracked primary key; a row the test didn't track
  survives `Finish`.
- **AC4** — A test that leaves a row in a guarded table fails, and the message names the table and the counts.
- **AC5** — Cleanup runs whether the test passes or fails, and one failed delete doesn't stop the others.
- **AC6** — The tests in §1.3 that read leaked rows seed their own and assert against them, so they pass on a
  database no other test has written to.
- **AC7** — No library code changes, and no existing assertion is weakened (AC6's tests are strengthened).

## 4. Test plan

### 4.1 AC → test matrix

| AC | Test (project) | What it proves |
|---|---|---|
| AC1 | Every test in each §1.1 class, through the D4 check (SQL Server, PostgreSQL, MySQL, net48) | The check runs after each test and fails it on a changed count |
| AC1 | `TestRowLedgerContract.Finish_DeletesTrackedRows_NewestFirst` (SQL Server, PostgreSQL, MySQL) | Tracked person, address and link rows are all gone; the link goes first |
| AC1 | Per-class measurement, §4.4 (each §1.1 class, alone) | `person` and the other guarded counts unchanged around one run of the class |
| AC2 | Per-suite measurement, §4.4 | Counts unchanged around one run of each suite |
| AC3 | `TestRowLedgerContract.Finish_Fails_WhenAnUntrackedRowIsLeft_AndLeavesThatRow` | The untracked row still exists after `Finish` |
| AC3 | `TestRowLedgerContract.Track_Rejects_ATableOutsideTheGuardedSet` | Only guarded tables can be tracked |
| AC4 | `TestRowLedgerContract.Finish_Fails_WhenAnUntrackedRowIsLeft_AndLeavesThatRow` | Fails; message names `person` and both counts |
| AC4 | `Cleanup_Fails_WhenTheTestLeavesAPersonRow` in each class that owns hooks, and in one fixture subclass per fixture (9 classes, §4.2) | Calls the class's own `Cleanup` after an untracked insert and expects the failure; proves each hook is wired |
| AC4 | `TestRowLedgerContract.Tables_AreTheProvidersGuardedSet` | Pins D5's list per provider |
| AC5 | `TestRowLedgerContract.Finish_AttemptsEveryDelete_AndReportsTheOnesThatFail` | A blocked delete is reported and a later one still runs; message names the blocked row and the `address` count |
| AC5 | `TestRowLedgerContract.Finish_Passes_WhenATrackedRowIsAlreadyGone` | A row the test deleted itself isn't a failure |
| AC5 | Placement: `Finish` in `[TestCleanup]` (§1.3: MSTest runs it after a failed test) | Cleanup on failure |
| AC6 | SQL Server `RemoteFeaturesTests` (4 tests) and `RemoteKeyWhereTests` (2); PostgreSQL equivalents (4 + 2) | Each asserts its own seeded values; fails when the seed is removed (§4.3 M9) |
| AC7 | Review check: `git diff --stat origin/development/3.10` touches only `*.Tests*` projects, `docs/`, `Changelog.md` | No library change |

`TestRowLedgerContract` is an abstract class in `Funcular.Data.Orm.SqlServer.Tests/Shared/`, linked into the
PostgreSQL and MySQL projects, with one `[TestClass]` per provider supplying a connection factory and three insert
helpers (person, address, link) through that provider's ORM. Each test cleans up the rows it deliberately leaves
untracked, in `finally`.

### 4.2 Interface coverage

| Member | Tests |
|---|---|
| `TestRowLedger.Begin` | every contract test; every guarded test |
| `TestRowLedger.Track` | `Finish_DeletesTrackedRows_NewestFirst`, `Finish_AttemptsEveryDelete…`, `Finish_Passes_WhenATrackedRowIsAlreadyGone`, `Track_Rejects_ATableOutsideTheGuardedSet` |
| `TestRowLedger.Finish` | every contract test except `Track_Rejects…` and `Tables_Are…`; the 9 wiring probes |
| `SqlServerTables`, `PostgreSqlTables`, `MySqlTables` | `Tables_AreTheProvidersGuardedSet` (per provider) |
| Fixture `Track`, `SeedEmployerChain` (SQL Server, PostgreSQL) | AC6 tests; every helper insert |

Wiring probes (`Cleanup_Fails_WhenTheTestLeavesAPersonRow`), 9: SQL Server `SqlDataProviderIntegrationTests`
(fixture), `SqlDataProviderIntegrationAsyncTests`, `StoredProcedureIntegrationTests`; PostgreSQL
`PostgreSqlDataProviderIntegrationTests` (fixture), `PostgreSqlDataProviderIntegrationAsyncTests`,
`PostgreSqlComputedAttributeIntegrationTests`; MySQL `MySqlDataProviderIntegrationTests` (the fixture's
`InitProvider`/`DisposeProvider`, which every MySQL class's hooks call); net48 both classes. Each inserts an
untracked person, calls the class's own `Cleanup`, expects `AssertFailedException`, deletes its row, and calls
`Setup` again so the framework's cleanup passes.

Coverage: `TestRowLedger.cs` and the touched fixtures at ≥ 85 % line rate, from
`dotnet test Funcular.Data.Orm.SqlServer.Tests --collect:"XPlat Code Coverage" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.IncludeTestAssembly=true`
(the ledger lives in the test assembly, which coverlet skips by default). Per-file `line-rate` from the cobertura
file goes in §5.

### 4.3 Mutations each key test must kill

| # | Mutation | Killed by |
|---|---|---|
| M1 | `Finish` skips the count comparison | `Finish_Fails_WhenAnUntrackedRowIsLeft…` (each provider) |
| M2 | `Finish` deletes oldest first | `Finish_DeletesTrackedRows_NewestFirst` (the person delete hits the link's foreign key) |
| M3 | `Finish` stops at the first failed delete | `Finish_AttemptsEveryDelete…` (the later row is still there) |
| M4 | `Finish` runs its deletes in one transaction | `Finish_AttemptsEveryDelete…` on PostgreSQL (the aborted transaction fails the later delete) |
| M5 | A delete that removes no row counts as a failure | `Finish_Passes_WhenATrackedRowIsAlreadyGone` |
| M6 | `Track` accepts any table | `Track_Rejects_ATableOutsideTheGuardedSet` |
| M7 | `Finish` also deletes rows with an id above the one `Begin` saw | `Finish_Fails_WhenAnUntrackedRowIsLeft_AndLeavesThatRow` (the row must survive) |
| M8 | A table removed from a provider's list | `Tables_AreTheProvidersGuardedSet` |
| M9 | An AC6 test's seed call removed | that test (it filters to its own person, so rows other runs left don't rescue it) |
| M10 | `Finish` removed from a class's `Cleanup` (each of the 9 hook sites) | that class's wiring probe |
| M11 | A `Track` call removed: from `InsertTestPerson` (SQL Server, PostgreSQL fixtures); after the direct insert in `MySqlDataProviderIntegrationTests.Insert_AssignsIdentity_AndGetRoundTrips` (MySQL has no helper) | the D4 check fails `Get_WithExistingId_ReturnsPerson` (SQL Server, PostgreSQL) and `Insert_AssignsIdentity_AndGetRoundTrips` (MySQL) |

Mutation runs that leave rows (M10, M11) run one named test each, so each leaves at most one row (§4.4).

### 4.4 Where each tier runs, and how counts are measured

- Contract tests and the D4 check run in the normal suites: SQL Server (the CI filter
  `FullyQualifiedName!~PerformanceTests&FullyQualifiedName!~EntityFramework`), PostgreSQL, MySQL; net48 through
  `ContinuousIntegration.playlist`'s two classes (its CI job is commented out). The GitHub workflows run the first
  three on fresh databases; AC6 is what keeps them non-Inconclusive there.
- **Measurement procedure** (AC1 per class, AC2 per suite), serial, one database at a time:
  1. Confirm no other test host is running (`Get-Process testhost, vstest.console -ErrorAction SilentlyContinue` is
     empty), and that no other session reports database work.
  2. Read the guarded tables' counts with a scratch counter (`scratchpad/rowcounts`, outside the repo). It reads the
     same environment variables as the fixtures and prints only table names and counts.
  3. Run the class or suite.
  4. Read the counts again; record both in §5. Any difference is investigated before the next run.
- **Red runs leave rows by design.** Task 2-5's red step runs one leaking test per affected class with the check
  wired and tracking not yet added; each leaves 1 to 4 rows. Those rows, and the at most one row per M10/M11
  mutation run, are counted and recorded in §5. They are not deleted afterwards (D3).

## 5. Tasks

Each task names the tests it turns green. Commits stay local until the push gate (non-author adversarial pass over
the exact sha).

- **Task 0 — plan review (non-author).** Re-check §1 against the code, the matrix against the ACs, and the
  mutations. Revisions get a "what changed" note.
- **Task 1 — `TestRowLedger` and its contract tests.**
  - Red: `TestRowLedger` as a stub (each member throws `NotImplementedException`), the contract and the three
    provider classes; the contract tests fail.
  - Green: implement D2/D3/D4; the contract tests pass on SQL Server, PostgreSQL and MySQL. Run M1–M8.
  - No existing test changes behaviour (nothing calls the ledger yet).
- **Task 2 — SQL Server suite.**
  - Red: wire D6 hooks (fixture, `SqlDataProviderIntegrationAsyncTests`, `StoredProcedureIntegrationTests`) and the
    three wiring probes; run the probes and one leaking test from each of the seven §1.1 classes, and record each
    failure.
  - Green: D7 `Track` calls, D8 `SeedEmployerChain` and the six AC6 tests. Run the suite; measure each §1.1 class
    and the suite (§4.4). Run M9–M11 for SQL Server.
- **Task 3 — PostgreSQL suite.** The same steps for its six classes, four AC6 tests and two RemoteKeyWhere tests.
- **Task 4 — MySQL suite.** The same steps for its five classes (no AC6 tests on `development/3.10`).
- **Task 5 — net48.** Link `TestRowLedger.cs` in the csproj `<Compile>` list; hooks, `Track` calls and probes in the
  two classes; run the playlist classes and measure. Measure DotNet9 once (§1.2).
- **Task 6 — documents.** D9's Changelog line and XML docs; this plan's §5 record.
- **Task 7 — adversarial review (non-author)** of the cumulative diff, then fix-verification of each fix layer, then
  the push gate.

## 6. Out of scope (recorded)

- **Other tables that leak** (owner decision 2026-10-03, follow-up task): `rls_demo` (SQL Server
  `SqlServerAuditContextIntegrationTests` 15 per run, PostgreSQL `PostgreSqlAuditContextIntegrationTests` 4; RLS
  filters the delete, so cleanup needs each owner's session context or a role that bypasses RLS);
  `non_identity_guid_entity` / `non_identity_string_entity` (SQL Server 1 each, MySQL 1); MySQL `user` (2).
- **Rows earlier runs left** stay. No sweep by name or marker (owner rule: don't delete rows you didn't create).
  `Query_MaxDateWithSelector` reads the table's maximum birthdate, which those rows have pushed forward; it stops
  moving once runs stop leaking.
- **A test on `fix/provider-scoped-caches` needs a leaked row:** `MySqlProviderScopedCacheTests.MapperCache_IsScoped_ThroughARealRead`
  asserts `Take(1)` returns a person, and MySQL's schema seeds none. Whichever of that branch and this one merges
  into `development/3.10` second must make that test seed and track its own row (one `Track` once the ledger is
  there). Noted for that branch's owner; this branch doesn't edit it.
- **Failure-path leaks in classes that are clean on a passing run:** QueryOperators `SeedEmployer` records ids only
  after its third insert; `RichRelationshipTests` deletes only on the success path; `DeleteSeededRows` rolls back
  everything if one delete fails.
- **`DocumentationGapTests`** drops and re-creates `[User]` with a different schema in every `Setup`, and builds a
  second provider it never disposes.
- **SQLite temp files:** eight classes delete their temp `.db` without clearing the connection pool first, and
  `SqliteAuditContextTests` never deletes its two.
- **Fixed-name test tables** (`zz_*`, `funky_*`, `single_probe`) collide if two runs share a database.

## 9. Review dispositions

None yet.
