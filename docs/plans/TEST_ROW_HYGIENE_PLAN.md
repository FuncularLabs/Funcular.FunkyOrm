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

> **Status (2026-10-03):** rev 6. Task 0 review of rev 1 (`369c2cf`, §9.1) found 2 blocking and 11 non-blocking
> findings, answered in rev 2 (`307af68`). Its re-review (§9.2) found 1 blocking and 6 non-blocking, answered in
> rev 3 (`f4d33a0`). Its re-review (§9.3) is **CLEAN** with 2 non-blocking findings, folded into rev 4 (`b406abd`).
> Rev 5 records Task 1 (`a2c836d`), rev 6 Task 2 (§5); Task 3 is next.

> **Revision 6 — what changed (Task 2):** §5 records Task 2's red steps, green run, measurements and mutations.

> **Revision 5 — what changed (Task 1):** §5 records Task 1's red and green runs and its mutations.

> **Revision 4 — what changed (Task 0 re-review, T2-1, T2-2):** `SeedEmployerChain` takes a country-name prefix, so
> the ordering test controls name order (T2-1). M14 names both sites and the exact mutant, and is reverted with a
> `git status` check; Task 3 says which mutations PostgreSQL runs (T2-2).

> **Revision 3 — what changed (Task 0 re-review, T1-1…T1-7):**
> - D8's AC6 tests keep their original remote predicates and add the test's own ids to them; the ordering test also
>   checks the seeded names come back in name order, seeded so that name order differs from id order (T1-1).
> - `Count_NoFilter…` is green at Red 3; its killer is the library mutation M14, run locally and reverted (T1-2).
> - M13 depends on locking READ COMMITTED (§1.3); `Cleanup_Passes…` moves to Red 2 with M13 as its red evidence
>   (T1-3).
> - Contract rows use per-run names; M7 and M12 run only on the `*TestRowLedgerTests` classes (T1-4). The contract's
>   own entity types carry no `…Id` property but `Id` (T1-5).
> - net48's coverage exemption awaits the owner's approval (T1-6). Row budgets corrected; `MySqlTestRowLedgerTests`
>   has its own hooks (T1-7).

> **Revision 2 — what changed (Task 0, T0-1…T0-13):**
> - AC3's test leaves an untracked exact copy of a tracked row, inserted after it; new mutation M12 (a name or marker
>   delete) and a reworded M7 (T0-1).
> - §5's red steps run the probes before the hooks exist, and the AC6 tests against a stub seed (T0-2).
> - A probe proves the provider is disposed before `Finish` (M13) (T0-3). Coverage runs on PostgreSQL and MySQL too;
>   net48 is exempt with a reason (T0-4).
> - D8 says how the ordering test and the reverse-entity count test become self-contained (T0-5, T0-6).
> - AC2 holds from the second run on a fresh database; §6 records the one-time organizations (T0-7).
> - Contract and probe tests delete every row they insert; one shared probe helper; red-run row counts corrected
>   (T0-8).
> - §1.3 records three MSTest facts the hooks live with; D6 and §6 corrected on `DocumentationGapTests` (T0-9).
> - The contract runs inside each provider's fixture and uses organization → person, which every provider's test
>   domain has; MySQL's fixture gets `Track` (T0-10). D7 is limited to the §1.1 classes (T0-11).
> - `Track`'s no-`Id` branch has a case; M10 includes removing `Begin`; the pin test maps to D5 (T0-12). Counts and a
>   line number corrected (T0-13).

## 1. Verified premises (author, against `ca82741` and `6014d8c`; the Task 0 reviewer re-checked them at `35c6477`, §9.1)

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
- One exception, on a fresh database only: four classes get-or-create a named organization and never delete it
  (SQL Server `ComputedAttributeIntegrationTests.cs:115-118`, `JsonPathIntegrationTests.cs:124-127`; PostgreSQL
  `PostgreSqlComputedAttributeIntegrationTests.cs:132-135`, `PostgreSqlJsonPathIntegrationTests.cs`). That is one row
  each, ever, not growth (§6).
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

- **MSTest 3.1.1** (SQL Server, PostgreSQL and MySQL suites), probed in throwaway projects by the author and the
  Task 0 reviewer:
  - Order: base `[TestInitialize]`, derived `[TestInitialize]`, test, derived `[TestCleanup]`, base `[TestCleanup]`.
  - An exception from `[TestCleanup]` fails that test; the report shows the test's exception and the cleanup's.
  - `[TestCleanup]` runs after a failed test, a failed `[TestInitialize]`, and an `Inconclusive` thrown from it.
  - If a derived `[TestCleanup]` throws, the base `[TestCleanup]` doesn't run (at any depth).
  - A derived `[TestInitialize]` that hides the base one with `new` replaces it: MSTest calls only the derived one.
  - `[ClassCleanup]` runs at the end of the assembly by default, and its failure is reported on whichever test ran
    last, in another class; with `ClassCleanupBehavior.EndOfClass` a failing class cleanup ran twice. An
    `[AssemblyCleanup]` failure fails the run, reported on the last test. Class- and assembly-level checks are
    therefore unusable here (D4).
- **net48** uses MSTest 2.2.10, C# 7.3 (no `LangVersion`), and an explicit `<Compile Include>` list; it has no
  `coverlet.collector`. `Funcular.Data.Orm.Tests.Shared` files are already linked into it and into DotNet9
  (`SupportedOperatorSignatures.cs`). The net8 projects have `ImplicitUsings` on and a global MSTest using.
- **No parallelism**: no `[Parallelize]` attribute or parallel runsettings in any suite.
- **No class- or assembly-level seeding**: no `[ClassInitialize]` or `[AssemblyInitialize]` in any of the four
  suites, so a check per test covers each class.
- **Provider transactions don't nest.** `BeginTransaction` throws "Transaction already in progress." when one is open
  (`SqlServerOrmDataProvider.cs:938`, `PostgreSqlOrmDataProvider.cs:414`, `MySqlOrmDataProvider.cs:389`). The suites
  call `BeginTransaction()` at 35 sites (SQL Server, CI filter), 20 (PostgreSQL) and 9 (MySQL). Five net8 tests
  assert that `Delete` throws when no transaction is open (`SqlDataProviderIntegrationTests.cs:976`, `:1100`,
  `SqlDataProviderIntegrationAsyncTests.cs:334`, PostgreSQL `PostgreSqlDataProviderIntegrationTests.cs:608` and its
  async twin), and net48 has two more. Every provider `Delete` overload requires an open transaction. `Dispose`
  disposes the connection, which rolls back an open transaction.
- **Foreign keys don't cascade** in any schema script. The insert order of a seeded chain (country → address →
  organization → person → person_address) is a valid delete order when reversed.
- **Guarded tables exist in the schema scripts:** SQL Server and MySQL create all eight of D5's tables; PostgreSQL's
  script creates no project tables (the ComputedAttribute and JsonPath tests create them).
- **Entities carry `[Table]`** (`System.ComponentModel.DataAnnotations.Schema`), inherited by the `Domain.Objects`
  types, and one non-hidden `int Id`, in all four suites. Every provider's test domain has a person type with
  `EmployerId` and an organization type; MySQL's has no person_address or country type.
- **Locking READ COMMITTED on SQL Server.** A database built from `integration_test_db.sql` has
  `READ_COMMITTED_SNAPSHOT` off (the default; the script doesn't set it), so a statement on another connection waits
  on an open transaction's row locks. M13's kill depends on this; Task 2 reads `is_read_committed_snapshot_on` for
  the local database before running M13 (§4.3).
- **The ORM's remote-path resolver scans the test assembly** (`RemotePathResolver.cs:363`) and treats an
  `int`/`int?` property ending in `Id` as a foreign key (`:280-345`). Test-only types added by this plan therefore
  carry no such property except `Id`.
- **Identity values aren't reused** within a run: no suite reseeds (no `CHECKIDENT`, `TRUNCATE`, `setval` or
  `AUTO_INCREMENT=`), so a tracked id can't come to name another writer's row.
- **Tests that pass only because of leaked rows** (`integration_test_data.sql` seeds no organization or country, and
  no `employer_id`). They go Inconclusive or vacuous on a fresh database once the leaks stop:
  - SQL Server `RemoteFeaturesTests` `RemoteProperty_IsPopulated_OnQuery` (31), `RemoteKey_IsPopulated_OnQuery`
    (168), `CanFilterBy_RemoteProperty` (187), and `OrderBy_RemoteProperty_OrdersByJoinedColumn` (52), whose order
    check runs only with two or more rows (`if (asc.Count >= 2)`, line 68). `RemoteKeyWhereTests`
    `CanFilterByRemoteKey` (11) and `CanFilterByRemoteProperty` (38). `RemoteKeyReverseTests`
    `Count_NoFilter_OnReverseEntity_MatchesBaseCount` (28), which compares 0 with 0 when there are no countries.
  - PostgreSQL `PostgreSqlRemoteFeaturesTests` `RemoteProperty_IsPopulated_OnQuery` (27),
    `RemoteKey_IsPopulated_OnQuery` (40), `CanFilterBy_RemoteProperty` (53) and the conditional
    `OrderBy_RemoteProperty_OrdersByJoinedColumn` (69, condition at 80); `PostgreSqlRemoteKeyWhereTests`
    `CanFilterByRemoteKey` (9) and `CanFilterByRemoteProperty` (28); `PostgreSqlRemoteKeyReverseTests`
    `Count_NoFilter_OnReverseEntity_MatchesBaseCount` (23).
  - Found by the audit, `grep Assert.Inconclusive` over the four suites, and the Task 0 review; no other remains.

## 2. Decisions

- **D1 — Delete what was inserted; don't wrap tests in a rolled-back transaction.** A wrap can't hold the tests
  that open their own transaction or the five that need none open (§1.3); PostgreSQL aborts the whole transaction on
  any engine error, which several tests provoke; several classes use second providers, logins or raw connections.
  Tracking each inserted row and deleting it after the test has none of these problems, and QueryOperators and
  ColdCacheDelete already do it.
- **D2 — One shared helper file,** `Funcular.Data.Orm.SqlServer.Tests/Shared/TestRowLedger.cs`, namespace
  `Funcular.Data.Orm.Tests.Shared`, linked into the PostgreSQL, MySQL and net48 test projects. C# 7.3 with explicit
  `using`s (net48 has neither implicit usings nor a newer compiler). Two types:
  - `TestRowLedger`:
    - `static TestRowLedger Begin(Func<DbConnection> newConnection, IReadOnlyList<string> tables)` reads the row count
      of each table in one statement and keeps the counts. Nothing else.
    - `void Track(object entity)` records the entity's table (from its `[Table]`, inherited) and its `Id`. It throws
      `ArgumentException` for an entity whose table isn't in the ledger's list, or that has no `Id` property.
    - `void Finish()` deletes the tracked rows (D3), reads the counts again, and fails (D4) if a delete failed or a
      count changed.
    - `static void DeleteRow(Func<DbConnection> newConnection, string table, object id)` — the same single-row
      delete, for tests that deliberately leave a row untracked and remove it themselves.
    - `static readonly string[] SqlServerTables, PostgreSqlTables, MySqlTables` (D5).
  - `TestRowLedgerProbe`, for the wiring probes (§4.2):
    - `static void AssertCleanupFails(Action cleanup, Action setup, Func<DbConnection> newConnection, string table, object id)`
      calls `cleanup`, expects an `AssertFailedException` that names `table`, then (in `finally`) deletes the row
      and calls `setup` so the framework's own cleanup finds a fresh ledger.
    - `static void AssertCleanupPasses(Action cleanup, Action setup)` calls `cleanup`, expects no exception, then
      calls `setup`.
- **D3 — How rows are deleted.** Newest first, one `DELETE FROM {table} WHERE id = @id` per tracked row, each on a
  new connection in autocommit mode (not the provider under test, which needs a transaction to delete and runs the
  delete guard). Every delete is attempted; a failure is recorded and the next is tried. A delete that removes no
  row (the test deleted it, or rolled it back) is fine. The hooks dispose the provider *before* `Finish`, so a
  transaction a failing test left open is rolled back and its locks released first (M13). Nothing else is deleted:
  no name, marker, predicate or id-range deletes, and rows earlier runs left stay (owner rule, §6).
- **D4 — The check is per test.** `Begin` runs in `[TestInitialize]`; `Finish` runs in `[TestCleanup]`, after the
  derived class's own cleanup (MSTest order, §1.3). A failure is `Assert.Fail` with the table, the count before and
  after, and any delete that failed. Because no class seeds at class level, a test-level check is a class-level
  check. Class- and assembly-level hooks were rejected (§1.3).
  - **Disclosed limitation:** another process writing to these tables while a test runs also changes the counts and
    fails the check. That matches the house rule that database work runs serially; the failure message says so.
    No opt-out (owner directive).
- **D5 — Guarded tables.** `person`, `address`, `person_address`, `organization`, `country`, plus `project`,
  `project_milestone`, `project_note` where the provider's schema script creates them (SQL Server, MySQL; not
  PostgreSQL). Out of scope: `rls_demo`, `non_identity_*`, `user` (§6).
- **D6 — Where the hooks go.**
  - `SqlServerTestFixture` and `PostgreSqlTestFixture`: `Setup` calls `Begin` last, after it builds the provider;
    `Cleanup` disposes the provider, then calls `_rows?.Finish()`. A `protected void Track(object entity)` forwards
    to the ledger. Every fixture subclass gets the check, not only the leaking ones.
  - `MySqlTestFixture`: `InitProvider` calls `Begin` last; `DisposeProvider` disposes, then calls `_rows?.Finish()`;
    it gets the same `Track`. Every MySQL fixture subclass calls these two exactly once from its own hooks, and the
    query-operator base deletes its rows before `DisposeProvider`.
  - Classes with their own hooks do the same in their `Setup`/`Cleanup`: SQL Server
    `SqlDataProviderIntegrationAsyncTests`, `StoredProcedureIntegrationTests`; PostgreSQL
    `PostgreSqlDataProviderIntegrationAsyncTests`, `PostgreSqlComputedAttributeIntegrationTests` (`Begin` after its
    `SeedTestData`, so the one-time organization of a fresh database is in the starting count); net48
    `SqlDataProviderNetFrameworkIntegrationTests` and its async twin.
  - `DocumentationGapTests` hides `Setup` with `new` and calls `base.Setup()` once, so `Begin` runs once (§1.3).
  - Known limits, accepted without special handling (owner directive): if a derived `[TestCleanup]` throws, the
    base cleanup, and so dispose and `Finish`, don't run (the test has already failed). `MySqlQueryOperatorTestBase`
    calls `DisposeProvider` in a `finally`, so when its own delete throws, `Finish`'s failure (the rows it left)
    replaces that exception in the report.
- **D7 — In the §1.1 classes and the fixture helpers, every insert into a guarded table is followed by `Track`.**
  The fixture helpers (`InsertTestPerson`, `InsertTestAddress`, `InsertTestPersonAddress` and their async copies,
  `StoredProcedureIntegrationTests.InsertPerson`, `MySqlJsonAndComputedTests.Seed`) track; each test that calls
  `_provider.Insert` directly gets `Track(entity)` on the next line, including inserts inside the test's own
  transaction, committed or rolled back. Classes §1.1 calls clean keep their own cleanup and get only the check. No
  test's assertions change, apart from D8's.
- **D8 — Tests that read leaked rows seed their own (§1.3).** A fixture helper `SeedEmployerChain(string
  countryPrefix)` (SQL Server and PostgreSQL) inserts and tracks country (named `countryPrefix` + a guid) → address →
  organization → person and returns the person's id and the country's id and name.
  `RemoteFeaturesTests.SeedForwardRemoteChain` is replaced by it.
  - Each `IsPopulated` / `CanFilterBy` / `RemoteKeyWhere` test seeds a chain and keeps its original remote
    predicate, ANDed with its own person's id: for example
    `FirstOrDefault(p => p.Id == seeded.PersonId && p.EmployerHeadquartersCountryId != null)`. It asserts the remote
    values equal what it seeded, and its filter step then uses those values as before. These are the suites' only
    `[RemoteKey] != null` predicates and `FirstOrDefault` with a remote predicate, so they stay. The
    `Assert.Inconclusive` branches go.
  - `OrderBy_RemoteProperty_OrdersByJoinedColumn` seeds two chains whose country names sort opposite to their ids
    (the first with prefix `"B_"`, the second with `"A_"`, an order any collation keeps), filters with
    `(p.Id == a || p.Id == b) && p.EmployerHeadquartersCountryName != null`, asserts both orders return 2 rows, that
    ASC returns the two seeded names in name order, and that DESC is ASC reversed. The `if (asc.Count >= 2)` goes.
  - `Count_NoFilter_OnReverseEntity_MatchesBaseCount` seeds one country, one address and two people each linked to
    that address (all tracked). It keeps its whole-table equality, and adds that the reverse entity's count filtered
    to that country's id is 1. An own-column filter doesn't add the fan-out join (`SqlLinqQueryProvider.cs:734-746`
    matches only the remote member's aliased column), so 1 is right; an always-join regression makes it 2. It
    doesn't use `SeedEmployerChain`, so it is green at Red 3; M14 is its killer.
- **D9 — Documents.** The Changelog gets one line under 3.10.0-beta1 › Changed ("tests only"). `TestRowLedger` and
  each fixture's XML doc state the rule (track every insert into a guarded table). No contributor guide covers
  integration tests today; none is added.

## 3. Acceptance criteria

- **AC1** — After any test in an affected class (§1.1) passes, the provider's guarded tables (D5) have the row
  counts they had before it ran. So a run of each affected class leaves `person` as it found it.
- **AC2** — A run of each suite, with nothing else using the database, leaves the guarded tables' counts unchanged:
  SQL Server (the CI filter), PostgreSQL, MySQL, and net48's two playlist classes. On a fresh database this holds
  from the second run; the first creates the four one-time organizations of §1.1.
- **AC3** — Cleanup deletes only rows the test inserted, by tracked primary key; a row the test didn't track
  survives `Finish`, even when it is an exact copy of a tracked one.
- **AC4** — A test that leaves a row in a guarded table fails, and the message names the table and the counts.
- **AC5** — Cleanup runs whether the test passes or fails, after the provider is disposed, and one failed delete
  doesn't stop the others.
- **AC6** — The tests in §1.3 that read leaked rows seed their own and assert against them, so they pass on a
  database no other test has written to.
- **AC7** — No library code changes, and no existing assertion is weakened (AC6's tests are strengthened).

## 4. Test plan

### 4.1 AC → test matrix

The contract cases live in `Funcular.Data.Orm.SqlServer.Tests/Shared/TestRowLedgerCases.cs` (linked into the
PostgreSQL and MySQL projects). Each provider has a `[TestClass]` that derives from its fixture
(`SqlServerTestRowLedgerTests`, `PostgreSqlTestRowLedgerTests`, `MySqlTestRowLedgerTests`), so it gets the fixture's
connection string and provider, and the fixture's own check confirms that each case removed everything it inserted.
`MySqlTestRowLedgerTests` has its own `[TestInitialize]`/`[TestCleanup]` calling `InitProvider`/`DisposeProvider`,
like every MySQL class. Each `[TestMethod]` there is a one-line call into the cases, passing a connection factory and
two insert helpers through that provider's ORM: an organization, and a person with an optional employer. Every case
names its rows with a fresh guid marker and deletes, in `finally`, every row it inserted, by id, children first. The
cases' own entity types (`[Table("not_guarded")]` with `Id`; `[Table("person")]` with only `FirstName`) carry no
other `…Id` property (§1.3).

| AC | Test (project) | What it proves |
|---|---|---|
| AC1 | Every test in each §1.1 class, through the D4 check (SQL Server, PostgreSQL, MySQL, net48) | The check runs after each test and fails it on a changed count |
| AC1 | `Finish_DeletesTrackedRows_NewestFirst` (each provider) | A tracked organization and a person employed by it are both gone; the person went first |
| AC1, D5 | `Tables_AreTheProvidersGuardedSet` (each provider) | Pins D5's list per provider |
| AC1 | Per-class measurement, §4.4 (each §1.1 class, alone) | `person` and the other guarded counts unchanged around one run of the class |
| AC2 | Per-suite measurement, §4.4 | Counts unchanged around one run of each suite |
| AC3, AC4 | `Finish_Fails_WhenAnUntrackedCopyIsLeft_AndLeavesIt` (each provider) | Inserts person P1 (tracked), then P2 with the same first and last name (untracked, higher id). `Finish` fails naming `person` with both counts; P1 is gone and P2 still exists |
| AC3 | `Track_Rejects_AnEntityOutsideTheGuardedSet` (each provider) | `ArgumentException` for a `[Table("not_guarded")]` type and for a `[Table("person")]` type with no `Id` |
| AC4 | `Cleanup_Fails_WhenTheTestLeavesAPersonRow`, 9 wiring probes (§4.2) | Each class's own `Cleanup` fails after an untracked insert; proves `Begin` and `Finish` are wired at every hook site |
| AC5 | `Finish_AttemptsEveryDelete_AndReportsTheOnesThatFail` (each provider) | Tracks person Q, then organization O; an untracked person employed by O blocks O's delete. `Finish` fails naming O's id and the `person` count, and Q is gone |
| AC5 | `Finish_Passes_WhenATrackedRowIsAlreadyGone` (each provider) | A row the test deleted itself isn't a failure |
| AC5 | `Cleanup_Passes_WhenTheTestLeftATransactionOpen` (SQL Server fixture, in `SqlDataProviderIntegrationTests`) | Begins a transaction, inserts and tracks a person, calls `Cleanup`: it passes, because dispose rolls back before `Finish` reads |
| AC5 | Placement: `Finish` in `[TestCleanup]` (§1.3: MSTest runs it after a failed test) | Cleanup on failure |
| AC6 | SQL Server `RemoteFeaturesTests` (4 tests), `RemoteKeyWhereTests` (2), `RemoteKeyReverseTests.Count_NoFilter…` (1); PostgreSQL equivalents (4 + 2 + 1) | Each asserts its own seeded values with its original remote predicate; the six chain tests fail when the seed is removed or stubbed (M9), `Count_NoFilter…` under M14 |
| AC7 | Review check: `git diff --stat origin/development/3.10` touches only `*.Tests*` projects, `docs/`, `Changelog.md` | No library change |

### 4.2 Interface coverage

| Member | Tests |
|---|---|
| `TestRowLedger.Begin` | every contract case; every guarded test |
| `TestRowLedger.Track` | `Finish_DeletesTrackedRows_NewestFirst`, `Finish_Fails_WhenAnUntrackedCopy…`, `Finish_AttemptsEveryDelete…`, `Finish_Passes_WhenATrackedRowIsAlreadyGone`, `Track_Rejects…` (both branches) |
| `TestRowLedger.Finish` | every contract case except `Track_Rejects…` and `Tables_Are…`; the wiring probes |
| `TestRowLedger.DeleteRow` | every contract case's `finally`; `AssertCleanupFails` |
| `SqlServerTables`, `PostgreSqlTables`, `MySqlTables` | `Tables_AreTheProvidersGuardedSet` (per provider) |
| `TestRowLedgerProbe.AssertCleanupFails` | the 9 wiring probes |
| `TestRowLedgerProbe.AssertCleanupPasses` | `Cleanup_Passes_WhenTheTestLeftATransactionOpen` |
| Fixture `Track` (SQL Server, PostgreSQL, MySQL), `SeedEmployerChain(countryPrefix)` (SQL Server, PostgreSQL) | AC6 tests (the ordering test passes two prefixes); every helper insert |

Wiring probes (`Cleanup_Fails_WhenTheTestLeavesAPersonRow`), 9: SQL Server `SqlDataProviderIntegrationTests`
(fixture), `SqlDataProviderIntegrationAsyncTests`, `StoredProcedureIntegrationTests`; PostgreSQL
`PostgreSqlDataProviderIntegrationTests` (fixture), `PostgreSqlDataProviderIntegrationAsyncTests`,
`PostgreSqlComputedAttributeIntegrationTests`; MySQL `MySqlDataProviderIntegrationTests` (the fixture's
`InitProvider`/`DisposeProvider`); net48 both classes. Each inserts an untracked person through the class's
provider and calls `TestRowLedgerProbe.AssertCleanupFails(Cleanup, Setup, …)`.

Coverage: `TestRowLedger.cs`, `TestRowLedgerCases.cs` and the touched fixtures at ≥ 85 % line rate, from
`dotnet test <project> --collect:"XPlat Code Coverage" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.IncludeTestAssembly=true`
for SQL Server, PostgreSQL and MySQL (the helpers live in the test assemblies, which coverlet skips by default).
Per-file `line-rate` from each cobertura file goes in §5. net48: the house rule says a touched test project without
`coverlet.collector` gets it as part of the task. The plan proposes an exemption instead, **pending the owner's
approval** (asked with the Task 5 handoff): the old-style project has no coverlet, its suite is opt-in, the shared
files' coverage comes from the net8 runs, and its own edits are hook lines and `Track` calls whose execution the
probes and the check prove. If the owner declines, Task 5 adds `coverlet.collector` and collects through vstest.

### 4.3 Mutations each key test must kill

| # | Mutation | Killed by |
|---|---|---|
| M1 | `Finish` skips the count comparison | `Finish_Fails_WhenAnUntrackedCopyIsLeft_AndLeavesIt` (each provider) |
| M2 | `Finish` deletes oldest first | `Finish_DeletesTrackedRows_NewestFirst` (the organization delete hits the person's foreign key) |
| M3 | `Finish` stops at the first failed delete | `Finish_AttemptsEveryDelete…` (Q is still there) |
| M4 | `Finish` runs its deletes in one transaction | `Finish_AttemptsEveryDelete…` on PostgreSQL only (after O's failure the transaction is aborted, so Q's delete fails). SQL Server and MySQL continue a transaction after a statement error, so there it survives; recorded |
| M5 | A delete that removes no row counts as a failure | `Finish_Passes_WhenATrackedRowIsAlreadyGone` |
| M6 | `Track` accepts any table, or an entity with no `Id` | `Track_Rejects_AnEntityOutsideTheGuardedSet` (one case each) |
| M7 | `Begin` also records each table's `MAX(id)` and `Finish` deletes every id above it | `Finish_Fails_WhenAnUntrackedCopyIsLeft_AndLeavesIt` (P2 must survive) |
| M8 | A table removed from a provider's list | `Tables_AreTheProvidersGuardedSet` |
| M9 | An AC6 test's seed removed, or `SeedEmployerChain` not setting `EmployerId` | that test (it reads its own person, so rows other runs left don't rescue it) |
| M10 | `Finish` or `Begin` removed from a hook (each of the 9 hook sites) | that site's wiring probe |
| M11 | A `Track` call removed: from `InsertTestPerson` (SQL Server, PostgreSQL fixtures); after the direct insert in `MySqlDataProviderIntegrationTests.Insert_AssignsIdentity_AndGetRoundTrips` (MySQL has no helper) | the D4 check fails `Get_WithExistingId_ReturnsPerson` (SQL Server, PostgreSQL) and `Insert_AssignsIdentity_AndGetRoundTrips` (MySQL) |
| M12 | `Finish` also deletes rows whose first and last name match a tracked row's | `Finish_Fails_WhenAnUntrackedCopyIsLeft_AndLeavesIt` (P2 must survive) |
| M13 | The SQL Server fixture calls `Finish` before disposing the provider | `Cleanup_Passes_WhenTheTestLeftATransactionOpen` (the delete and the count wait on the open transaction's lock and time out). Needs locking READ COMMITTED (§1.3): if the local database has `READ_COMMITTED_SNAPSHOT` on, M13 runs on LocalDB, or is recorded as surviving locally |
| M14 | Library, local only and reverted: the body of `ResolveAggregateJoins` (`Funcular.Data.Orm.SqlServer/…/SqlLinqQueryProvider.cs:720-727`; `Funcular.Data.Orm.PostgreSql/PostgreSql/PostgreSqlLinqQueryProvider.cs:550-557`) becomes `return remoteInfo.JoinClauses ?? string.Empty;`. Revert, and check `git status` shows no library path before any commit | `Count_NoFilter_OnReverseEntity_MatchesBaseCount` (the filtered count becomes 2 and the whole-table counts differ) |

Mutation runs: M1–M8 and M12 leave nothing (each case's `finally` deletes all it inserted). M7 and M12 change what
`Finish` deletes, so they run only with a filter that selects the `*TestRowLedgerTests` classes, whose rows carry
per-run guid names: run anywhere else, M12 would delete rows earlier runs left. M9 to M11, M13 and M14 run one
named test each. M9, M10, M13 and M14 leave nothing (M9's rows are tracked, the probe deletes its row, M13's insert
is rolled back); M11 leaves the one row that test inserts (§4.4).

### 4.4 Where each tier runs, and how counts are measured

- Contract tests and the D4 check run in the normal suites: SQL Server (the CI filter
  `FullyQualifiedName!~PerformanceTests&FullyQualifiedName!~EntityFramework`), PostgreSQL, MySQL; net48 through
  `ContinuousIntegration.playlist`'s two classes (its CI job is commented out). The GitHub workflows run the first
  three on fresh databases; AC6 is what keeps them from going Inconclusive there.
- **Measurement procedure** (AC1 per class, AC2 per suite), serial, one database at a time:
  1. Confirm no test host of this repository is running (`Get-Process testhost, vstest.console`, and each one's
     command line), and that no other session reports database work.
  2. Read the guarded tables' counts with a scratch counter (`scratchpad/rowcounts`, outside the repo). It reads the
     same environment variables as the fixtures and prints only table names and counts.
  3. Run the class or suite.
  4. Read the counts again; record both in §5. Any difference is investigated before the next run.
- **Red runs leave rows by design.** Tasks 2–5's red step runs one leaking test per affected class with the check
  wired and tracking not yet added. The chosen test and the rows it leaves are recorded in §5: one to four for most
  classes, six for `MySqlJsonAndComputedTests` (its `Seed`; most of its tests add nothing more). Those rows, and the
  mutation-run rows of §4.3, are not deleted afterwards (D3).

## 5. Tasks

Each task names the tests it turns green. Commits stay local until the push gate (non-author adversarial pass over
the exact sha).

- **Task 0 — plan review (non-author).** Re-check §1 against the code, the matrix against the ACs, and the
  mutations. Revisions get a "what changed" note.
- **Task 1 — `TestRowLedger`, `TestRowLedgerProbe` and the contract cases.**
  - Red: both types as stubs (each member throws `NotImplementedException`), the cases and the three provider
    classes; every case fails.
  - Green: implement D2/D3/D4; the cases pass on SQL Server, PostgreSQL and MySQL. Run M1–M8 and M12.
  - No existing test changes behaviour: nothing else calls the ledger yet, and the provider classes' fixtures aren't
    wired yet.
  - **Record (the commit that carries rev 5):**
    - Red: with the stubs, 6 of 6 failed on each provider (18). Green: 6 of 6 passed on each, guarded counts
      unchanged around each run, and no other checkout's test host during the windows (§4.4). An earlier PostgreSQL
      green attempt lost 2 cases to another session's suite writing during the window (`person` +81); rerun clean.
    - Mutations, each in a window with unchanged counts and no other checkout's test host:

      | Mutant | Provider | Failed (the named killer first) |
      |---|---|---|
      | M1 | SQL Server, PostgreSQL, MySQL | `Finish_Fails_WhenAnUntrackedCopyIsLeft_AndLeavesIt`, `Finish_AttemptsEveryDelete…` |
      | M2 | SQL Server, PostgreSQL, MySQL | `Finish_DeletesTrackedRows_NewestFirst` |
      | M3 | SQL Server | `Finish_AttemptsEveryDelete…` |
      | M4 | PostgreSQL | `Finish_AttemptsEveryDelete…`; survives on SQL Server and MySQL, as §4.3 records |
      | M5 | SQL Server | `Finish_Passes_WhenATrackedRowIsAlreadyGone` (and the three cases whose `finally` deletes an already-deleted row) |
      | M6 (any table / no `Id`) | SQL Server | `Track_Rejects_AnEntityOutsideTheGuardedSet`, each |
      | M7 | SQL Server | `Finish_Fails_WhenAnUntrackedCopyIsLeft_AndLeavesIt`, `Finish_AttemptsEveryDelete…` |
      | M8 | SQL Server | `Tables_AreTheProvidersGuardedSet` |
      | M12 | SQL Server | `Finish_Fails_WhenAnUntrackedCopyIsLeft_AndLeavesIt`, `Finish_AttemptsEveryDelete…` |

      M7's and M12's mutant deletes were limited to `first_name LIKE 'ledger%'` (the cases' own markers), so a run
      couldn't touch another writer's rows.
- **Task 2 — SQL Server suite.**
  - Red 1: add the three wiring probes; run them. They fail (`Cleanup` doesn't fail yet).
  - Red 2: wire the D6 hooks (fixture, `SqlDataProviderIntegrationAsyncTests`, `StoredProcedureIntegrationTests`) and
    add `Cleanup_Passes_WhenTheTestLeftATransactionOpen` (it needs the fixture's `Track`; its red evidence is M13).
    The probes pass; one leaking test from each of the seven §1.1 classes fails in cleanup; record each.
  - Red 3: write the seven AC6 tests, the six chain tests against a `SeedEmployerChain` stub that inserts and tracks
    the chain but leaves `EmployerId` unset; those six fail. `Count_NoFilter…` is green here (M14 is its evidence).
  - Green: D7 `Track` calls and the real `SeedEmployerChain`. Run the suite; measure each §1.1 class and the suite
    (§4.4). Read `is_read_committed_snapshot_on`; run M9–M11, M13 and M14.
  - **Record (the commit that carries rev 6):**
    - Red 1: the three wiring probes failed; counts unchanged.
    - Red 2: the probes and `Cleanup_Passes…` passed (4 of 4); one test per §1.1 class failed in cleanup:
      `Get_WithExistingId_ReturnsPerson`, `Get_WithExistingId_ReturnsPerson_Async`,
      `ExecProcedure_NoParams_ReturnsRows`, `Count_FilteredByRemoteProperty_MatchesMaterialized`,
      `Query_Sum_ReturnsCorrectSum`, `RemoteKey_OuterJoin_NullHandling`, `CanFilterByReverseRemoteKey`. They left
      16 rows: person 9, address 2, person_address 1, organization 2, country 2.
    - Red 3: the six chain tests failed against the stub; `Count_NoFilter…` passed; counts unchanged (the failing
      tests' tracked rows were deleted).
    - Green: the suite (CI filter) passed, 895 of 895, every guarded count unchanged around the run (it added 177
      `person` rows before). Each §1.1 class and `RemoteKeyWhereTests`, run alone: all passed, counts unchanged
      (72, 21, 23, 12, 3, 2, 9, 2 tests).
    - `is_read_committed_snapshot_on` is false locally.
    - Mutations: M9 is Red 3. M10 (Begin or Finish removed at each of the three hook sites): each probe failed. M11:
      `Get_WithExistingId_ReturnsPerson` failed and left its 1 `person` row. M13: `Cleanup_Passes…` failed after
      2 m 30 s of lock-wait timeouts, run while every other FunkyORM session was idle (the mutant holds a `person`
      row lock until its test host exits). M14: `Count_NoFilter…` failed; the library file restored, `git status`
      clean for `Funcular.Data.Orm.SqlServer`.
- **Task 3 — PostgreSQL suite.** The same steps for its six classes, three probes and seven AC6 tests, without
  `Cleanup_Passes…`, the RCSI read and M13 (SQL Server only); it runs M9–M11 and M14.
- **Task 4 — MySQL suite.** The same steps for its five classes and one probe (no AC6 tests on `development/3.10`).
- **Task 5 — net48.** Link `TestRowLedger.cs` in the csproj `<Compile>` list; the two probes first (red), then the
  hooks and `Track` calls; run the playlist classes and measure. Measure DotNet9 once (§1.2).
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
- **One-time organizations on a fresh database** (§1.1): ComputedAttribute and JsonPath create a named
  organization if it's missing and keep it. One row each, ever; AC2 counts from the second run.
- **A test on `fix/provider-scoped-caches` needs a leaked row:** `MySqlProviderScopedCacheTests.MapperCache_IsScoped_ThroughARealRead`
  asserts `Take(1)` returns a person, and MySQL's schema seeds none. Whichever of that branch and this one merges
  into `development/3.10` second must make that test seed and track its own row (one `Track` once the ledger is
  there). Noted for that branch's owner; this branch doesn't edit it.
- **Failure-path leaks in classes that are clean on a passing run:** QueryOperators `SeedEmployer` records ids only
  after its third insert; `RichRelationshipTests` deletes only on the success path; `DeleteSeededRows` rolls back
  everything if one delete fails. And D6's known limits: a derived cleanup that throws skips the base cleanup.
- **`DocumentationGapTests`** drops and re-creates `[User]` with a different schema in every `Setup`.
- **SQLite temp files:** eight classes delete their temp `.db` without clearing the connection pool first, and
  `SqliteAuditContextTests` never deletes its two.
- **Fixed-name test tables** (`zz_*`, `funky_*`, `single_probe`) collide if two runs share a database.

## 9. Review dispositions

### 9.1 Task 0 review of rev 1 (`369c2cf`; non-author; read every §1.1 SQL Server and MySQL class in full and the PostgreSQL ones by grep; an MSTest 3.1.1 probe; no database)

Verdict: 2 blocking, 11 non-blocking. All accepted; rev 2 answers them.

| ID | Severity | Finding | Disposition | Blame |
|---|---|---|---|---|
| T0-1 | Blocking | AC3's test didn't fix what the untracked row holds, so a name/marker-deleting `Finish` could survive; M7 assumed state `Begin` doesn't keep | The untracked row is an exact copy of a tracked one, inserted after it; M12 added; M7 reworded (§4.1, §4.3) | TEST-GAP |
| T0-2 | Blocking | Red step wired the hooks and the probes together, so the probes were never red; AC6 tests had no red step | §5: probes before hooks; AC6 tests against a stub seed | HOUSE-RULE |
| T0-3 | Non-blocking | "Dispose before `Finish`" had no test | `Cleanup_Passes_WhenTheTestLeftATransactionOpen`, M13 | TEST-GAP |
| T0-4 | Non-blocking | Coverage command covered only SQL Server; net48 has no coverlet | PostgreSQL and MySQL runs added; net48 exempt with reason (§4.2) | HOUSE-RULE |
| T0-5 | Non-blocking | Ordering test kept `if (asc.Count >= 2)`, so M9 survived | D8: `Assert.AreEqual(2, …)`, condition removed | TEST-GAP |
| T0-6 | Non-blocking | `Count_NoFilter_OnReverseEntity_MatchesBaseCount` goes vacuous on a fresh database | Added to §1.3, D8, AC6 | AC-GAP |
| T0-7 | Non-blocking | Get-or-create organizations break AC2 on a fresh database's first run; PostgreSQL ComputedAttribute's `Begin` placement | AC2 qualified; §1.1, §6, D6 | AC-GAP |
| T0-8 | Non-blocking | Contract `finally` cleaned only untracked rows; probes had no `finally`; red-row counts wrong | Cases delete all they insert; `TestRowLedgerProbe`; counts corrected (§4.3, §4.4) | PLAN-GAP |
| T0-9 | Non-blocking | Missing MSTest facts (derived-cleanup throw skips base; `finally` masking in MySQL's query-operator base; `new Setup()` replaces base) | §1.3; D6 known limits; D6/§6 corrected | PLAN-GAP |
| T0-10 | Non-blocking | MySQL's test domain has no person_address/country type; MySQL fixture had no `Track`; contract's connection unspecified | Cases use organization → person; provider classes derive from the fixtures; MySQL `Track` (D6, §4.1) | PLAN-GAP |
| T0-11 | Non-blocking | D7 read as covering clean classes | D7 limited to §1.1 classes and helpers | PLAN-GAP |
| T0-12 | Non-blocking | No-`Id` branch untested; removing `Begin` not a mutation; pin test mapped to AC4 | `Track_Rejects…` covers both; M10 includes `Begin`; pin maps to AC1/D5 | TEST-GAP |
| T0-13 | Non-blocking | "Four" Delete-without-transaction tests (five, plus net48); `OrderBy…` at line 52 | Corrected (§1.3) | PLAN-GAP |

### 9.2 Task 0 re-review of rev 2 (`307af68`; non-author; the rev 1→2 diff and the library code behind each changed claim; no database)

Verdict: 1 blocking, 6 non-blocking; T0-1, T0-7…T0-13 confirmed resolved, T0-2…T0-6 partly (answered below). All
accepted; rev 3 answers them.

| ID | Severity | Finding | Disposition | Blame |
|---|---|---|---|---|
| T1-1 | Blocking | (a) Reading the seeded person by id dropped the suites' only `[RemoteKey] != null` predicates and `FirstOrDefault` with a remote predicate (a weakening from rev 2's fix). (b) The ordering test, filtered by ids alone, passed under the stub (both names NULL) | D8 keeps each original predicate ANDed with the own-id filter; the ordering test filters on the name too, checks the seeded names in name order, seeded against id order | TEST-GAP |
| T1-2 | Non-blocking | `Count_NoFilter…` design confirmed (an own-column filter doesn't join), but it is green at Red 3 and no mutation could fail it | Stated green at Red 3; library mutation M14, local and reverted | TEST-GAP |
| T1-3 | Non-blocking | M13 needs locking READ COMMITTED; `Cleanup_Passes…` couldn't be red at Red 1 | §1.3 premise; RCSI read before M13; the probe moves to Red 2 with M13 as its red evidence | TEST-GAP |
| T1-4 | Non-blocking | An M12 run could name-delete rows earlier runs left | Per-run guid names; M7 and M12 run only on the `*TestRowLedgerTests` classes | PLAN-GAP |
| T1-5 | Non-blocking | New test types in the test assemblies join the remote-path resolver's scan | §1.3 and §4.1: they carry no `…Id` property but `Id` | PLAN-GAP |
| T1-6 | Non-blocking | net48 coverage exemption contradicts the house rule without the owner's approval | Marked pending the owner's approval; coverlet added if declined (§4.2) | HOUSE-RULE |
| T1-7 | Non-blocking | M9/M13 row budget wrong; `MySqlJsonAndComputedTests` is six; MySQL's ledger tests need their own hooks | Corrected (§4.3, §4.4, §4.1) | PLAN-GAP |

### 9.3 Task 0 re-review of rev 3 (`f4d33a0`; non-author; the rev 2→3 diff, the query-operator policy, both WHERE visitors, both `ResolveAggregateJoins`, `RemotePathResolver`, and `fix/where-captured-values`; no database)

Verdict: **CLEAN**; T1-1…T1-7 confirmed resolved. D8's shapes translate on SQL Server and PostgreSQL (the policy
checks only the method chain; both visitors parenthesise `AndAlso`/`OrElse` and qualify own columns). Two
non-blocking findings, folded into rev 4.

| ID | Severity | Finding | Disposition | Blame |
|---|---|---|---|---|
| T2-1 | Non-blocking | `SeedEmployerChain()` generated its own names, so the ordering test couldn't control name order | Country-name prefix parameter; `"B_"` then `"A_"` (D8, §4.2) | PLAN-GAP |
| T2-2 | Non-blocking | M14 didn't name the PostgreSQL site or the exact mutant; its killer cell named something the test doesn't observe; Task 3 didn't say which mutations PostgreSQL runs | Both sites, the mutant, the revert check; killer cell rewritten; Task 3 lists M9–M11, M14 (§4.3, §5) | PLAN-GAP |
