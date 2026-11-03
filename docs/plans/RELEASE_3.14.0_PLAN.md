# Plan: FunkyORM 3.14.0 — exception types to Core, the subset-Select guard, strong naming, one Known-issues list, net48 in CI, test-row hygiene

Status: rev 6, 2026-11-02 (rev 1: §3.H, H and §6 pending surveys; rev 2: those filled; rev 3: §3.K, the K criteria and
tests filled from the inventory; rev 4: Task 0 dispositions applied, §7.1; rev 5: round-2 dispositions, §7.2; rev 6:
round-3 dispositions, §7.3). Owner instruction (Paul, 2026-11-02 ~12:50 CT): "Start a fix effort for these: 1. Move the
duplicated exception types to Core 2. Throw on the silent wrong-answer path 3. Fix the §6.8 overclaim 4. Create the
consolidated known issues list (this may be 3.14) 5. These quality and polish efforts will precede the call for human
reviews … Let's implement signing … What's your recommendation for net48 in CI? … Clean up the person row leaks."
Item 3 is the Slim Domain paper (rc10, cut 2026-11-02; cascaded in the slim-domain repository) and is not in this plan.

Branch `development/3.14.0`, cut from master `10c1c6b` (3.13.0), in the worktree `.claude/worktrees/release-3.14.0`.
Version **3.14.0** (a new rejection, a packaging change and a new assembly identity are each more than a patch).
Review exit rule: as 3.13.0, **no Major left** (Major = wrong behaviour, a missed rejection, a throw where it should
work, false docs, or a test that can't fail).

Sections: 1 defects · 2 acceptance criteria · 3 design · 4 test plan · 5 tasks · 6 suggestions for the
episode-blocking items (not in this release) · 7 record.

Recommendation on net48 in CI (the owner's question): add the job and make `publish` need it; reasons in §3.C. The
job's commands ran green here on 2026-11-02 (VS MSBuild `-restore -t:Rebuild`, then vstest.console: 170/170 in 5 s on
SQL Server 2019, before signing). Open until C2 and the first CI run: a public-signed build loading under vstest on
net48, and the runner's toolchain.

## 1. The defects

**E. Four copies of three exception types.** Each provider project declares public
`Funcular.Data.Orm.Exceptions.{PathNotFoundException, AmbiguousMatchException, NullableExpressionException}` in its own
`Exceptions/RemoteKeyExceptions.cs` (SqlServer, PostgreSql, MySql, Sqlite; identical code, SqlServer's
`NullableExpressionException` summary has two extra sentences). The package bundles all four assemblies for net8.0 and
netstandard2.0, so a consumer that writes `catch (NullableExpressionException)` gets **error CS0433** ("exists in both
Funcular.Data.Orm.MySql and Funcular.Data.Orm.PostgreSql"). Reproduced 2026-11-02 against nuget.org 3.13.0 with a
net8.0 console app (scratchpad `cs0433red/`: two errors, lines 5 and 6). The publish build shows the same thing as 40
CS0436 warnings. Pre-existing since the providers were bundled; unfiled. Usage.md:134, Advanced.md:175 and the AI
instructions name the types, so readers will try to catch them.

**S. A lambda after a column-subset `Select` reads base columns.** `Query<T>().Select(x => new T { A = x.A })` followed
by `Where(t => t.B == 5)` (or `OrderBy`, `ThenBy`, `First(pred)`, `Count(pred)`, `Sum(sel)`, a chained `Select`…)
runs without complaint and filters on the base table's `B`. LINQ to objects would see `B` at its default. The cause is
in Core: `QueryOperatorPolicy.EnsureSupported` pass 2 (`Funcular.Data.Orm.Core/Linq/QueryOperatorPolicy.cs:110-217`)
sets no flag for a subset `MemberInit` of the entity type, so a later lambda passes every check and each provider's
parse loop translates it against the base table. Recorded as Backlog Appendix A row 10 and in
`QUERY_OPERATOR_CORRECTNESS_PLAN.md` §8, never fixed. A member is **identity-bound** when the projection assigns it
from the same member of the Select's own parameter (`P = x.P`); a lambda that reads only identity-bound members is
equal in LINQ and SQL. The divergence is a read of any other member: one the projection left out (the primary key
included unless bound), one bound from a different member (`FirstName = p.LastName`, which the SQL Server/PostgreSQL/
MySQL visitors reject only for members declared on `T` itself, not inherited ones, and the SQLite visitor never
rejects), or one bound to a constant.

**N. Nothing is strong-named.** Every production csproj has `SignAssembly` false or unset; every
`InternalsVisibleTo` grant is unqualified; the owner's key exists (`D:\func\keys\FuncularLabs.snk`, 2048-bit RSA,
public-key token `0650eba294fc9e3f`, public key file `FuncularLabs.publickey`, extracted and cross-checked 2026-11-02
with `sn -p` / `sn -tp`). A consumer that is itself strong-named gets warning CS8002 on every .NET Framework build
for the unsigned reference (checked 2026-11-02: the compiler emits CS8002 for a net48 target, fully signed or
public-signed, and not for a net8.0 target), cannot put the package in the GAC, and fails under a host that enforces
strong names; and adding the name later is an identity change for every such consumer, so the cost only grows.

**K. Known issues are a chain.** `Changelog.md` [3.13.0] has no Known issues; [3.12.1] says the 3.12.0 list applies;
[3.12.0] ends "the 3.11.0 Known issues not listed as fixed above still apply"; [3.11.0], [3.10.1] and [3.10.0] have
their own. Backlog Appendix A has had no status pass since 3.11.0. A reader has to reconcile four releases.

**C. net48 is not tested in CI.** The `test-netframework` job is commented out (`.github/workflows/ci.yml:239-253`) and
would not run as written: the DLL path is `bin\Release\` not `bin\Release\net48\`, there is no LocalDB step, no
`-restore`, and `vstest.console` has no `/Playlist` switch (checked 2026-11-02: it has `/Tests:` and
`/TestCaseFilter:`). The net48 project needs VS MSBuild (`dotnet build` omits System.Memory; BACKLOG_AFTER_3.10.0.md:95).
3.13.0's net48 suite ran by hand only. The package's net48 group carries the SQL Server provider only.

**H. Integration suites leak `person` rows** (SQL Server +177 per run under the CI filter, PostgreSQL +87, MySQL +59,
net48 +101; audit 2026-10-03). The fix, `fix/test-row-hygiene` (CLEAN at `9c37ac0`, never pushed, 13 commits, 34
files, +1,877/−207, test projects only), was cut from development/3.10 and predates 3.10.1 through 3.13.0. A dry-run
merge onto master (2026-11-02, throwaway worktree, removed) conflicted in two one-hunk adjacent inserts only
(`Changelog.md`; the net48 csproj's `Compile Include` list); the three fixtures are byte-identical on master and at the
branch base; the merged net8 test projects build. Merged 2026-11-02 as `48dd2d7`, follow-up `1b7b84a`. One master test
still depended on a leaked row:
`MySqlProviderScopedCacheTests.MapperCache_IsScoped_ThroughARealRead` (`Take(1)` must return one person; the MySQL
schema script seeds none).

## 2. Acceptance criteria

**E — exception types**
- E1. The three types are defined once, in `Funcular.Data.Orm.Core`, namespace `Funcular.Data.Orm.Exceptions`, same
  base types (`Exception`, `Exception`, `InvalidOperationException`) and constructors; the SqlServer doc comment is
  the one kept. No provider project defines them.
- E2. Each of the four provider assemblies forwards the three types (`TypeForwardedTo`), pinned by reflection. No
  binary-compatibility claim is made: on net8.0 no 3.13.0 consumer could compile a reference to them (CS0433), and
  3.14.0's strong name is a new identity for net48 consumers anyway; the forwarders are the standard courtesy when a
  type moves, and cost one line each.
- E3. A consumer of the package that catches each of the three by type compiles and runs on net8.0 and net48 (the
  consumer smoke); the same source fails against 3.13.0 on net8.0 with CS0433 (the red run, recorded; on net48 the
  3.13.0 package carries only the SQL Server provider, so it compiles there).
- E4. The package build produces no CS0436 warning.
- E5. Every existing full-name assertion stays green; `LikeToStringHarness` asserts the exception's assembly is Core's;
  the three comments that say each provider defines its own (`TranslatorDirectHarness.cs:195`,
  `WhereVisitorDirectTests.cs:289`, `LikeToStringHarness.cs:131`) say where the types live now.
- E6. Changelog [3.14.0] records the move and says plainly that there is no binary compatibility with 3.13.0 (the
  strong name is a new identity: recompile); Usage.md/Advanced.md keep the namespace.

**S — the subset-Select guard**
- S1. After a column-subset `Select` (a `MemberInit` of the entity type), an operator whose lambda reads a member of
  its parameter that is not **identity-bound** by that projection (left out, bound from another member, or bound to a
  constant) throws `NotSupportedException` **before any SQL is sent**, on all four providers. Covered operators:
  `Where`, `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`, `First`/`FirstOrDefault`/`Single`/
  `SingleOrDefault`/`Last`/`LastOrDefault` with a predicate, `Count`/`LongCount`/`Any`/`All` with a predicate,
  `Sum`/`Min`/`Max`/`Average` with a selector, and a later `Select` of either kind: a later subset `Select`'s own
  lambda is checked against the earlier projection, then its bindings become the set; a later scalar `Select`
  (`Select(subset{FirstName}).Select(t => t.LastName)`, today a wrong answer: the second SELECT reads base
  `last_name`, LINQ gives null) is checked against the set first, then clears it.
- S2. The message names the operator and the member and gives the rewrite (apply the operator before the `Select`, or
  bind the member from itself).
- S3. A lambda that reads only identity-bound members is accepted and returns what LINQ to objects returns over the
  same rows; reads are matched by member name (so a covariant `IQueryable<IHasId>` view over the projection still
  passes), through a `Convert`/cast of the parameter, and only on the Select's own parameter (a nested lambda's
  parameter or a captured object of type `T` is not a row read). `Distinct`, `Skip`, `Take`, `Cast`, `OfType`
  between the `Select` and the reading operator neither reset nor widen the set. `Distinct`, `Skip`, `Take`, `Cast`,
  `OfType` and parameterless terminals after a subset `Select` are unchanged (`Select(subset).Distinct().Count()`
  stays rejected by the parse loop as before). A `Select` after `Distinct` is the one exception: S9.
- S4. The primary key is not bound unless the projection binds it from itself. A `MemberListBinding` or
  `MemberMemberBinding` (which the visitors skip) is not a binding either, and causes no exception of another type.
- S5. Precedence is unchanged: after paging (D8), after an earlier ordering (D10) and the predicate-type rule (I1)
  keep their messages; the existing rejection tests pass without edits. A scalar `Select` or an unsupported `Select`
  after the subset clears the set, so `Select(subset).Select(t => t.Name).OrderBy(s => s.Length)` still gets the
  parse loop's scalar-outermost message, not the subset one.
- S6. net48 gets the same rule (the policy is Core; a DB-free test is linked into the net48 project).
- S7. The four `Distinct_OrderByUnprojectedColumn_Throws` tests expect the policy's `NotSupportedException`; the
  Distinct-key check (`SqlLinqQueryProvider.cs:814-831` and siblings) stays covered by the
  `OrderBy(unbound).Select(subset).Distinct()` form.
- S9 (review round 1). A `Select` of either kind after `Distinct()` throws `NotSupportedException` before any SQL on
  all four providers, whether or not its reads are identity-bound: SQL applies DISTINCT to the final column list,
  LINQ makes the rows distinct first. `Select(...).Distinct()` is unchanged; `Distinct().Where/OrderBy/Skip/Take`
  are unchanged. The message names `Select(...)` and says to apply `Distinct` after the `Select` for distinct
  projections, or to materialize first to keep LINQ's result. Documented in Advanced.md §2 and §5, README, the AI
  files' tables and checklist, and the Changelog.
- S8. Docs state the rule: Advanced.md (the subset idiom at :49-54, the positional rules at :225-233), the AI
  instruction files (FUNKYORM_AI_ADVANCED.md :37-42, :216-222, :245-246; FUNKYORM_AI_INSTRUCTIONS.md :574-578),
  README.md:348-349 (the `Select(subset).Distinct().OrderBy(bound)` example is still OK and says why), Changelog.
  `OperatorDocTableTests` still passes.

**N — strong naming**
- N1. Every assembly in the package (Core, Funcular.Data.Orm, PostgreSql, MySql, Sqlite; all TFM groups) is
  strong-named with public-key token `0650eba294fc9e3f`; the published package's DLLs are fully signed (`sn -vf`
  passes), not public-signed.
- N2. A build without the private key public-signs with the committed public key (same identity), so PRs, forks and
  local builds work; a `FunkyOrmKeyFile` that is set but does not exist is a build error, never a silent fallback;
  the publish job fails before packing if the secret is empty; the key reaches the build step, which produces what
  pack packs (with `GeneratePackageOnBuild`, `dotnet pack` does not build), and the pack step too, so a
  pack-triggered build would sign as well; a change of signing mode forces a recompile (a mode stamp among the
  compiler's inputs), so an incremental build with the key never leaves public-signed DLLs behind; the
  verification step finds `sn.exe` by its SDK path and fails if it is absent; the four `<SignAssembly>False</SignAssembly>`
  lines are deleted (a project property overrides the props).
- N3. Every `InternalsVisibleTo` grant carries the FuncularLabs public key (the hex itself is asserted); every friend
  test assembly is signed with the same key; the dead `Funcular.Data.Orm.SqlServer` grant in `Core/AssemblyInfo.cs`
  is removed.
- N4. No private key is in the repository (`*.snk` ignored; a test fails if one is tracked).
- N5. The consumer smoke passes on net8.0 and net48, run both ways: public-signed (no key, as CI's test jobs build)
  and full-signed (`Run-ConsumerSmoke.ps1 -KeyFile <path>`, passed through as `-p:FunkyOrmKeyFile`), plus a
  strong-named-consumer mode (`-StrongNamedConsumer`: the smoke app itself public-signs with the committed
  `build/FuncularLabs.publickey`, which is enough to make it a strong-named referrer, and builds with
  `WarningsAsErrors=CS8002`). The proof lives on the smoke's net48 target, where the compiler emits CS8002 (the net8.0
  target never warns, so it cannot fail this way). Green on 3.14.0 through the script; the red half on nuget.org
  3.13.0 is a scratch run on net48, recorded as E3's is, since the script packs the local tree.
- N6. Changelog [3.14.0] explains the identity change for .NET Framework consumers (CS8002 and GAC/strong-name hosts,
  not "cannot reference"); README names the token.
- N7. The publish job's signing path can be exercised before the release merge: `workflow_dispatch` runs the same
  job (build, pack with the key, verify) and never pushes. The **job-level** condition is
  `github.event_name == 'workflow_dispatch' || (github.event_name == 'push' && github.ref == 'refs/heads/master')`
  (otherwise a manual run would skip the job, show grey, and a dry run could be recorded that never ran); the
  **push step** runs only when `github.event_name == 'push' && github.ref == 'refs/heads/master'`; a manual run on
  any branch, whatever its inputs, cannot publish (an unreviewed push to nuget.org cannot be undone, and the later
  master publish would then finish green under `--skip-duplicate` having published nothing). The `dry_run` input
  defaults to `true` and is informational; `CiWorkflowTests` pins both conditions and the default
  (`Publish_NeedsEveryProviderSuite_AndRunsOnlyOnMaster` is renamed `…_AndPushesOnlyFromMaster`), and §7 records the
  dry run's publish-job conclusion with the verify step's output.

**K — one Known-issues list** (inventory: 73 rows across the Changelog chain, Backlog Appendix A, Usage/Advanced, the
2026-10-25 and 10-27 quality reports; nine fixed in 3.10.0–3.12.1 and one in 3.13.0, 2 planned here, 10 not re-verified since 3.10, the
rest documented or open; scratchpad `known_issues_inventory.md`)
- K1. `docs/KNOWN_ISSUES.md` is the single consolidated list, headed "as of 3.14.0", grouped (query translation; null
  semantics; SQL Server; PostgreSQL; MySQL; SQLite; deletes and the guard; packaging and platform). Each entry: the
  behaviour in user wording, the release it was first recorded in, the workaround or the by-design reason (every
  entry has one or the other), and the mark **not re-verified since 3.10** where that is so. Items 3.14.0 fixes (the
  exception types; the subset-Select read; signing; net48 in CI; the row leaks) are not in it. Internal engineering
  items (dead code, old-style csprojs, comment nits, the stale-DLL pack hazard, the published package's
  DLL-version check (K-67), the dead EF comparison harness) go to the
  backlog, not the user list. A workaround must not itself produce a wrong answer (the AVG(int) entry says "into a
  `decimal` property", not "declare it `int?`", which would read a truncated average).
- K2. Changelog [3.14.0] "### Known issues" is a pointer plus the delta only (fixed this release; new since 3.13.0);
  every older `### Known issues` heading ([3.10.0] through [3.12.1]) gets one pointer line **directly under the
  heading** (the sections are long; the test looks within three lines), and the [3.10.1] line that still says the
  aggregate fixes are "planned for 3.11.0" gets its "shipped in 3.11.0" note.
- K3. README gets a "Known issues" pointer to the file (the README ships in the package).
- K4. Backlog Appendix A: a status mark per row, dated 2026-11-02, with the evidence (the Changelog Fixed entry, the
  3.14.0 plan, or "open"/"not re-verified"); the engineering rows the user list excludes (K-54, K-67 to K-72 of the
  inventory) are listed under a new "Engineering, as of 3.14.0" heading in the backlog with the same marks.
- K5. The cheap doc-drift items the inventory found are fixed here: Usage.md/AI `.Value`/`.HasValue` wording (they
  throw `NullableExpressionException`, not an invalid column); the AI instructions' "Provider Selection" omits MySQL;
  four `?` phase headings in the main AI file; a Changelog note that `--skip-duplicate` landed in 3.13.0 (commit
  7c4e6ac) without an entry. The rest of the inventory's §3 conflicts are recorded in the backlog with a probe each.

**C — net48 in CI**
- C1. A `test-net48` job on windows-latest builds the net48 test project with VS MSBuild (`-restore -t:Rebuild`,
  public-signed as every test job is) and runs it with `vstest.console.exe` against LocalDB prepared as `test-net8`
  prepares it, with `/TestCaseFilter:"FullyQualifiedName!~PerformanceTests"` (the performance class is listed in the
  project but compiled only under `#if PERFORMANCE_TESTS`; the 2026-11-02 baseline discovered none of its tests; the
  filter keeps it out the day that symbol is defined, as every other job's filter does), `/Logger:"trx;LogFileName=test-results.trx"
  /ResultsDirectory:TestResults` so the TRX gate reads it, and an empty allow-list; `publish` needs it;
  `CiWorkflowTests` pins all of that (the `needs` list, the filter in vstest spelling, the gate step, the logger).
- C2. The job's steps run green locally the same way before the push (MSBuild 17.14 + vstest 17.14 on this machine;
  the rehearsal database is `.\SQL2019` via `FUNKY_CONNECTION` because this machine's LocalDB rejects logins; the
  public-signed build is the one rehearsed).

**H — test-row hygiene**
- H1. The branch's design lands unchanged in substance: `TestRowLedger` (Begin counts the guarded tables, Track records
  each inserted row, Finish deletes tracked rows newest-first on a fresh autocommit connection after the provider is
  disposed, re-counts, and fails the test naming table/before/after). Guarded tables: person, address, person_address,
  organization, country (+ project, project_milestone, project_note on SQL Server and MySQL).
- H2. Every hook site the branch wired (the three fixtures and the own-hook classes: SQL Server
  `SqlDataProviderIntegrationAsyncTests`, `StoredProcedureIntegrationTests`; PostgreSQL `…IntegrationAsyncTests`,
  `…ComputedAttributeIntegrationTests`; net48 both classes) keeps its wiring probe `Cleanup_Fails_WhenTheTestLeavesAPersonRow`
  (13). OD-6 outcome: the SQL Server `EntityFramework/EfSqlDataProviderIntegrationTests` class keeps its carve-out,
  because it is compiled under `PERFORMANCE_TESTS` only (a guard from 3.0.1-beta1, commit 98eefef) and under that
  symbol its `EfSqlDataProvider` fails to compile with 14 × CS0535: the `ExecProcedure`, `ExecScalar` and `ExecNonQuery`
  members, sync and async, that `IOrmDataProvider` gained in 3.7.0 (commit 50825ac, 2026-06-23), after the harness
  was written (562133e, 2025-09-04); found 2026-11-02 when the wiring was compile-checked. Dead code cannot leak; the wiring and a
  `Track(table, id)` overload written for it were withdrawn as unverifiable; reviving or deleting the harness is a
  backlog item (K4's engineering heading).
- H3. Each suite (SQL Server under the CI filter, PostgreSQL, MySQL, net48) leaves the guarded tables' row counts as
  it found them, measured before and after one full run and recorded, **on a throwaway database per provider** named
  `funky_db_h3`, created from copies of each provider's schema **and data** scripts with the database name
  substituted in both (the SQL Server data script's own `USE funky_db;` would otherwise override `sqlcmd -d`;
  SQL Server and PostgreSQL seeded by their data scripts, MySQL by its schema only, as CI seeds them),
  reached through `FUNKY_CONNECTION` / `FUNKY_PG_CONNECTION` / `FUNKY_MYSQL_CONNECTION` for those runs (every
  fallback is conditional on the variable). The shared `funky_db` databases are never dropped or re-created (CI's
  `DROP DATABASE` step and the scripts' `CREATE DATABASE IF NOT EXISTS` / `USE` would either destroy the rows H5
  protects or silently reuse the old database); their guarded-table counts are recorded before and after and must
  be unchanged. A test that silently needed "some row" fails on the throwaway database, which is how H4's case was
  found.
- H4. `MapperCache_IsScoped_ThroughARealRead` seeds and removes its own person row (green on an empty MySQL database).
- H5. Pre-existing leftover rows (the ~150 recorded in TEST_ROW_HYGIENE_PLAN.md §5/§9) are never deleted (owner rule).
- H6. OD-4: the net48 test project stays without coverlet (old-style project, run by vstest; `TestRowLedger.cs` is
  measured at 92–99 % through the net8 suites). OD-5: `MySqlTestFixture.cs` at 68.2 % is accepted (every uncovered
  line is pre-existing environment fallback). Both recorded, both reversible by the owner.

## 3. Design

**E.** Move `Exceptions/RemoteKeyExceptions.cs` to `Funcular.Data.Orm.Core/Exceptions/RemoteKeyExceptions.cs` (the
SqlServer text). Delete the four provider copies. In each provider add `Exceptions/TypeForwards.cs` with three
`[assembly: TypeForwardedTo(typeof(…))]`. The throw sites are unchanged (same namespace; `RemotePathResolver.cs` already
has the `using`). Why forwarders: the standard courtesy when a public type moves between assemblies of one package,
so that anything compiled against a provider assembly's TypeRef (a direct file reference, an `extern alias`) keeps
resolving; no 3.13.0 binary is claimed to bind to 3.14.0 (E2: the strong name is a new identity, and on net8.0 no
3.13.0 consumer could compile the reference). The consumer smoke gains a `catch` of each type (DB-free; the catch
never fires) so the pack is compiled against by a real consumer on both TFMs.

**S.** In `QueryOperatorPolicy.EnsureSupported` pass 2: a new `boundMembers` set of member **names** (null = no
subset Select in force). The Select arm sets it when the body is a `MemberInit` of `entityType` (the implicit else of
line 211): a binding counts only if it is a `MemberAssignment` whose value, after unwrapping `Convert`, is a
`MemberExpression` on that lambda's own parameter with the **same member name** as the target (identity copy); any
other binding kind or value leaves the member unbound. A scalar `Select` or an unsupported `Select` sets the set back
to null (finding 5). After the I1 check and before the `name == "Select"` arm, when `boundMembers != null` and the
call carries a quoted lambda (the same test as the scalar guard), walk the lambda with an `ExpressionVisitor` that
collects every `MemberExpression` whose expression, after unwrapping `Convert`, is **that lambda's parameter** (not a
nested lambda's parameter, not a captured object); the first member name not in `boundMembers` throws
`NotSupportedException` with the operator and member named. A later subset `Select` is checked the same way against
the earlier set first, then its own bindings become the set. `Distinct`, `Skip`, `Take`, `Cast`, `OfType` leave the
set alone. Why only non-identity-bound members (the narrow rule from QUERY_OPERATOR_CORRECTNESS_PLAN §8) and not
"nothing after Select": identity-bound reads are equal in LINQ and SQL, the documented
`Select(subset).Distinct().OrderBy(bound)` idiom keeps working, and the rejection matches the defect exactly. Why a
policy rule and not a visitor change: one site serves all four providers and net48 (the policy is Core, the parse
loops are duplicated four times). Message template:
`"{op}(...) after a column-subset Select reads {Member}, which the Select did not copy from itself. LINQ would see the projected value; SQL would read the base column; the results would differ. Apply {op}(...) before the Select, or project {Member} = x.{Member}."`

**N.** A root `Directory.Build.props` (none exists; legacy csprojs import it through Microsoft.Common.props too):
`SignAssembly=true`; if `$(FunkyOrmKeyFile)` is set, `AssemblyOriginatorKeyFile=$(FunkyOrmKeyFile)` and a target that
runs before build errors if the file does not exist (no silent fallback); otherwise
`AssemblyOriginatorKeyFile=$(MSBuildThisFileDirectory)build/FuncularLabs.publickey` with `PublicSign=true`. The four
`<SignAssembly>False</SignAssembly>` lines are deleted. The public key file is committed (safe to share, per the key
folder's README); `.gitignore` gets `*.snk`. Every IVT grant (Core `AssemblyInfo.cs`, the four provider csproj
`AssemblyAttribute` items) gets `, PublicKey=<full hex>`. Test projects inherit the props, so friends are signed. CI:
build/test jobs public-sign (no secret; a public-signed assembly loads under .NET 8 and, by the strong-name bypass
that .NET Framework has applied to full-trust assemblies since 3.5 SP1, under vstest on net48: the local rehearsal
first, then the test-net48 job proves it on every run); `publish` decodes `secrets.FUNKYORM_SNK_BASE64` to a temp file, fails if the secret is empty, passes
`-p:FunkyOrmKeyFile=…` to **both** build and pack (the build produces the DLLs; with `GeneratePackageOnBuild=True`
`dotnet pack` does not build, it packs what the build left in `bin`, so the key on the build step is the one that
matters and the key on pack covers a pack-triggered build),
then runs `tools/signing/Verify-StrongNames.ps1`, which extracts every DLL from the nupkg and runs `sn.exe -vf` on
each, locating `sn.exe` by the glob `${env:ProgramFiles(x86)}\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8*Tools\sn.exe`
(this machine has both `NETFX 4.8 Tools` and `NETFX 4.8.1 Tools`; windows-latest ships the .NET Framework 4.8 SDK) and
failing if none is found or any DLL fails; only then `nuget push`, and only on a `push` event to `master` (N7: a
`workflow_dispatch` run from any branch runs the whole job and never pushes). Local rehearsal: the script run on a
public-signed pack must fail and on a full-signed pack must pass (recorded in §7). The secret is the owner's to add:
`gh secret set FUNKYORM_SNK_BASE64 --repo FuncularLabs/Funcular.FunkyOrm --body "$([Convert]::ToBase64String([IO.File]::ReadAllBytes('D:\func\keys\FuncularLabs.snk')))"`.
Dependencies are strong-named (checked 2026-11-02 in the NuGet cache: Microsoft.Data.SqlClient 23ec7fc2d6eaa4a5,
Npgsql 5d8b90d52f46fda7, MySqlConnector d33d3e53aa5f8c92, Microsoft.Data.Sqlite adb9793829ddae60, EPPlus
ea159fdaa78159a1). The test-only `Spittoon.Core` 0.1.5.1 (and its `Spittoon.Validation`) are **not** strong-named,
so the signed `SqlServer.Tests` assembly gets warning CS8002 for them: a warning in an SDK project without
`TreatWarningsAsErrors`, harmless at run time on .NET 8 (no strong-name validation), and not referenced by the
net48 projects. Record the warning count in §7 rather than suppress it. The identity change: a .NET Framework
project that referenced 3.13.0 recompiles against 3.14.0 (a package upgrade is a recompile); a binding redirect cannot
bridge an unsigned 3.13.0 to a signed 3.14.0, and the Changelog says so.

**C.** Recommendation: **add the job and make `publish` need it** (rather than label net48 best-effort). Reasons: the
package ships a net48 group, the paper and tutorial point .NET Framework shops at it, and 3.14.0's signing change is
exactly the kind of change that needs the net48 build and bind exercised on every push. Cost: one windows job (~3
minutes; the 2026-11-02 baseline ran 170 tests in 5 s). Shape: checkout; `microsoft/setup-msbuild@v2`; the LocalDB
steps copied from `test-net8`; `msbuild Funcular.Data.Orm.SqlServer.Tests.NetFramework\…csproj -restore -t:Rebuild
-p:Configuration=Release`; vstest located by `vswhere` (`-find Common7\IDE\Extensions\TestPlatform\vstest.console.exe`)
run on `bin\Release\…NetFramework.dll` with `/TestCaseFilter:"FullyQualifiedName!~PerformanceTests"`,
`/Logger:"trx;LogFileName=test-results.trx"`, `/ResultsDirectory:TestResults`, then the TRX gate; artifact
`net48-test-results`. The playlist is not used (vstest has no such switch). `CiWorkflowTests` gains the job in its
`needs`/gate/filter pins, with the vstest spelling of the filter taught to `TestJobs_FilterOnlyThePerformanceAndEntityFrameworkSuites`.

**H.** `git merge --no-ff 9c37ac0` into development/3.14.0 (not a hand re-apply: the fixtures didn't change shape,
the two conflicts are trivial, and the merge keeps the reviewed CLEAN-at-9c37ac0 provenance; the 13 "(3.10)"-titled
commits in history are the price). Resolve: keep both sides of both hunks, then move the branch's Changelog entry from
under [3.10.0] to [3.14.0]. Follow-up commit (`1b7b84a`): the MySQL scoped-cache test seeding its own row (H4),
TEST_ROW_HYGIENE_PLAN.md rev 14 ("ships in 3.14.0; cut from 3.10; re-verified on master", OD-4/5/6 recorded; OD-6 =
the carve-out stays, see H2). Compatibility with 3.13.0 checked: the ledger never touches the provider (raw `DbConnection`s, autocommit),
so `ConnectionScope` and the priming marker are untouched; no guarded table is under RLS (only `rls_demo` is, and it
is out of scope), so the ledger's deletes need no audit context; `Dispose()` still rolls back an open transaction
before `Finish()` runs.

**K.** A living file rather than a Changelog section, because a per-release section recreates the chain at the next
release: the file is updated in the release that changes it, the Changelog records the delta, and a DB-free test pins
the file's "as of" version to the package version so a release cannot forget it. Rows that no one has re-run since
3.10 are kept and marked, not dropped (dropping would assert they are fixed) and not re-verified here (ten probes are
a separate task; each gets a backlog line). Membership, from the inventory's status column with K1's exclusions:
FIXED rows are out (they live in the Fixed entries; nine were fixed in 3.10.0–3.12.1 and one, `--skip-duplicate`, in
3.13.0); PLANNED-3.14.0 rows are out; the three rows 3.14.0 closes (K-63 signing, K-64 net48 in CI, K-66 the row
leaks) are out; the seven engineering rows (K-54, K-67 to K-72) go to the backlog's new "Engineering, as of 3.14.0"
heading (K4), joined by the dead EF comparison harness found in stream H; everything else is in: 73 − 10 − 2 − 3 − 7 =
51 rows, plus the unverified `TargetInvocationException` half of Appendix A #21 added in review round 1: 52.

## 4. Test plan

Mutations run by hand (edit, build, run the named test, revert); each is recorded in §7 with its result.

| AC | Test | Project | Mutation it must kill |
|:--|:--|:--|:--|
| E1, E2 | `ExceptionTypes_AreDefinedOnceInCore_AndForwardedByEveryProvider` (DB-free: `Assembly.DefinedTypes` of each provider has none of the three; `GetForwardedTypes()` of each has all three; `Type.GetType("…NullableExpressionException, Funcular.Data.Orm")` returns Core's type) and `ExceptionTypes_KeepTheirBaseTypesAndConstructors` (`BaseType` is `Exception`/`Exception`/`InvalidOperationException`; each has exactly the `(string)` constructor; each is constructed and its `Message` read, which also covers the moved file) | SqlServer.Tests/Packaging | M-E1: delete one forwarder; M-E2: leave the MySql copy in place (CS0729 or line 516 red); M-E3: change a base type |
| E3 | consumer smoke `Program.cs` catches each type; `Run-ConsumerSmoke.ps1` net8.0 + net48 | tools/consumer-smoke | red: the same source on 3.13.0 → CS0433 on net8.0 (recorded; net48 compiles on 3.13.0) |
| E4 | publish-configuration build log, 0 × CS0436 (record) + `NoProviderDefinesTheSharedExceptionTypes` (source scan, pattern-21 caveat: paired with E1's runtime pin) | SqlServer.Tests/Packaging | M-E2 |
| E5 | `LikeToStringHarness` line 516 → Core's assembly; all four providers' LikeToString tests; `TranslatorDirectHarness`, `WhereParityHarness` full-name rows | all four + net48 | behaviour pins |
| S1, S2, S4 | `AfterSubsetSelect_NonIdentityBoundRead_RejectedBeforeAnyQuery` ([DynamicData] table: each operator in S1 × a left-out member; the `Id` row; a cross binding `FirstName = p.LastName` then `Where(t => t.FirstName …)` (an inherited member, so the visitors pass it); a constant binding then a read; a chained subset `Select` reading a member the first left out; a chained subset `Select` that rebinds a cross-bound member) via `AssertThrowsNoQuery<NotSupportedException>`; message names operator and member | the four `*QueryOperatorRejectionTests` | M-S1: never set `boundMembers`; M-S2: treat `Id` as bound; M-S3: check `Where` only; M-S4: message without the member; **M-S7: bound = every binding** (the cross/constant/chained rows must fail) |
| S1, S3, S4, S5, S6 | DB-free rows in `QueryOperatorPolicyTests` (C# 7.3, `AsQueryable` root): left-out read rejected; identity-bound read passes; `((PersonBase)x).LastName` (Convert unwrap) rejected when unbound and passes when bound; a covariant `IQueryable<IHasId>` view over `subset{Id}` with `Max(x => x.Id)` passes (match by name); a nested lambda `o => o.LastName == x.FirstName` where only the outer parameter's reads count (passes when `FirstName` is bound) **and** its companion `o => o.LastName == x.LastName` with `LastName` unbound (rejected: the outer read inside the nested lambda counts); a captured `other.Id` of type `T` is not a read; `Distinct`/`Cast<T>`/`OfType<T>` between the Select and the read neither reset nor widen; a later **scalar** Select of an unbound member `Select(subset{FirstName}).Select(t => t.LastName)` is rejected, and of a bound one clears the set (then `OrderBy(s => s.Length)` gets no subset message); a `MemberListBinding`/`MemberMemberBinding` is unbound and throws nothing else | SqlServer.Tests/Linq/QueryOperatorPolicyTests (linked into net48) | M-S1, M-S2, M-S7; M-S8: no Convert unwrap; M-S9: compare `MemberInfo` instead of name; M-S10: match any parameter of type T; M-S11: reset at Distinct/Cast; M-S12: never clear at a scalar Select; M-S13: clear at a scalar Select before checking its lambda; M-S14: don't descend into nested lambdas |
| S3 | `AfterSubsetSelect_IdentityBoundRead_MatchesOracle` (Where/OrderBy/Count(pred)/Sum(sel) on identity-bound members vs LINQ to objects over the seeded rows) and `AfterSubsetSelect_ConstantOperators_Unchanged` (Distinct, Skip/Take, Cast/OfType, parameterless terminals; `Select(subset).Distinct().Count()` still rejected by the existing `Distinct_WithCount_ThrowsNotSupported`) | the four `*QueryOperatorSemanticsTests` | M-S5: reject every lambda after a subset Select (the bound rows must fail) |
| S9 | `Select_AfterDistinct_Rejected_SubsetOrScalar_BoundOrNot` + `Select_ThenDistinct_StillPasses` (DB-free, linked into net48); `Select_AfterDistinct_RejectedBeforeAnyQuery` (four rows: subset→Distinct→scalar, subset→Distinct→subset, Distinct→subset, Distinct→scalar) in each provider rejection suite | QueryOperatorPolicyTests; the four `*QueryOperatorRejectionTests` | M-S15: drop the Distinct check (killed on all four providers and DB-free, review round 2) |
| S5 | existing `Operator_AfterPaging_Rejected_BeforeAnyQuery`, `Ordering_AfterEarlierOrdering_Rejected_BeforeAnyQuery`, `SubsetSelectThenWhere` I1 rows, precedence tests, and a new row `Select(subset).Select(t => t.Name).OrderBy(s => s.Length)` expecting the parse loop's scalar-outermost message | the four rejection files | unchanged rows must stay green; the new row kills M-S12 |
| S7 | `Distinct_OrderByUnprojectedColumn_Throws` (now `NotSupportedException`, the policy message) + new `OrderByUnbound_ThenSubsetSelect_Distinct_ThrowsDistinctKeyCheck` | the four Computed/Json test files | M-S6: remove the Distinct-key check |
| S8 | `OperatorDocTableTests` (existing); doc text reviewed in the hostile pass | SqlServer.Tests | — |
| N1 | `EveryProductionAssembly_HasTheFuncularLabsPublicKeyToken` (token `0650eba294fc9e3f` on Core, Funcular.Data.Orm, PostgreSql, MySql, Sqlite as loaded by the net8.0 test host) and `StrongNamingNetFrameworkTests.ProductionAssemblies_HaveTheFuncularLabsPublicKeyToken` (Core and Funcular.Data.Orm as loaded by the net48 host; the netstandard2.0 builds are covered only by the publish verification); `TheSigningModeStamp_IsACompilerInput` (the props carry the stamp target and feed it to `CustomAdditionalCompileInputs`) | SqlServer.Tests/Packaging; SqlServer.Tests.NetFramework | M-N1: `SignAssembly=false` on one project; M-N7: drop the stamp from the compile inputs |
| N1, N2, N7 | `CiWorkflowTests` pins on `publish`: empty-secret check before pack; `-p:FunkyOrmKeyFile` on the build **and** the pack step; the verify script step before `nuget push`; the job-level `if` admits `workflow_dispatch` and `push` to master; the push step's `if` requires `github.event_name == 'push'` and `github.ref == 'refs/heads/master'`; the `workflow_dispatch` `dry_run` input defaults to `true` | SqlServer.Tests/Packaging | M-N4: drop the key from the pack step; M-N5: drop the ref/event check from the push step; M-N6: job-level `if` back to master-only (the dry run would skip) |
| N1 | `tools/signing/Verify-StrongNames.ps1` on a full-signed pack passes and on a public-signed pack fails (local rehearsal, recorded in §7; the script's own `sn.exe`-absent branch exits non-zero) | tools/signing | the public-signed pack is the mutation |
| N2 | `Directory.Build.props`: a clean build without `FunkyOrmKeyFile` succeeds and the token test passes (record); a build with `-p:FunkyOrmKeyFile=C:\nope.snk` fails with the props' own error (record) | build | — |
| N3 | `EveryInternalsVisibleToGrant_CarriesTheFuncularLabsPublicKey` (reflection over Core's and each provider's `InternalsVisibleToAttribute`s; each ends with `PublicKey=` + the FuncularLabs hex; none named `Funcular.Data.Orm.SqlServer`) | SqlServer.Tests/Packaging | M-N2: one grant with another key's hex (an unqualified grant is already CS1726) |
| N4 | `NoPrivateKeyIsTracked` (walks the repo tree excluding bin/obj/.git for `*.snk`; `.gitignore` contains `*.snk`) | SqlServer.Tests/Packaging | M-N3: drop the ignore line |
| N5 | consumer smoke net8.0 + net48, public-signed and `-KeyFile` full-signed; `-StrongNamedConsumer` (the smoke app public-signed with the committed public key, `WarningsAsErrors=CS8002`) green on 3.14.0 through the script; red on nuget.org 3.13.0 as a scratch run (CS8002 as error) (record) | tools/consumer-smoke | — |
| C1 | `CiWorkflowTests`: `Publish_NeedsEveryProviderSuite…` includes `test-net48`; the `TestJobs` rows (filter, TRX gate step, gate never hides a failure, logger file name `test-results.trx` in `TestResults`) include it, with the vstest spelling of the filter | SqlServer.Tests/Packaging | M-C1: drop `test-net48` from `needs`; M-C2: bare `/Logger:trx` |
| C2 | the job's commands run locally (MSBuild public-signed + vstest with the filter + the TRX gate script) and recorded with the TRX class counts; the job itself on the branch's first CI run | ci.yml | — |
| K1 | `KnownIssues_AsOfVersion_MatchesThePackageVersion` (DB-free: the file's "as of x.y.z" equals the SqlServer csproj `Version`) | SqlServer.Tests/Packaging | M-K1: bump the csproj version only |
| K2 | `OlderKnownIssuesSections_PointAtTheConsolidatedList` (source scan of Changelog.md: every `### Known issues` heading under a version below 3.14.0 has the pointer line directly under it, within three lines, matching K2's placement; pattern-21 caveat: it pins text, not behaviour, which is all there is to pin) | SqlServer.Tests/Packaging | M-K2: drop one pointer line |
| K1-K5 | reviewer reads the file row by row against `known_issues_inventory.md` (membership per §3.K) and the Appendix A marks against the Changelog Fixed entries | — | — |
| H1, H2 | the 13 wiring probes `Cleanup_Fails_WhenTheTestLeavesAPersonRow` (branch); `SqlServerTestRowLedgerTests` / `PostgreSqlTestRowLedgerTests` / `MySqlTestRowLedgerTests` (the ledger contract: unguarded table, missing Id, newest-first, failed delete reported; 7/7, 7/7, 7/7 on 2026-11-02 after the merge) | the three fixtures' projects + net48 | M-H1: `Finish()` skips the re-count; M-H2: a hook site without `Begin` (its probe must fail) |
| H3 | row counts of the guarded tables before/after one full run per suite on the throwaway `funky_db_h3` databases (script copies with the name substituted; SQL Server + data script, PostgreSQL + data script, MySQL schema only), reached through the three connection variables; `funky_db`'s counts recorded before and after, unchanged (record; the branch's §4.4 method) | all suites | — |
| H4 | `MapperCache_IsScoped_ThroughARealRead` seeds and deletes its own row (green 2026-11-02 on the local database; the H3 run on the empty throwaway database is its proof against the original premise) | MySql.Tests | red on master against an empty `person` table |
| H5 | reviewer checks no `DELETE` without a tracked Id exists in the ledger (`TestRowLedgerCases` pins it) | — | — |

Interface coverage: no public interface changes (the exception types keep their members; the policy's public surface is
`EnsureSupported(Expression)`, already covered by `QueryOperatorPolicyTests`). Coverage: cobertura per touched file
from the SqlServer suite run with `--collect:"XPlat Code Coverage"`; `QueryOperatorPolicy.cs` and the new Core file
reported in §7.

Opt-in suites: SQL Server via `FUNKY_CONNECTION` (.\SQL2019 here), PostgreSQL via the suite default local connection,
MySQL via `FUNKY_MYSQL_CONNECTION`, SQLite in-process, net48 by VS MSBuild + vstest. All five run here before review.

## 5. Tasks

0. This plan's test design reviewed by a non-author agent (Task 0) once §3.H, §3.K and §6 are filled (rev 2).
1. E: red tests (E1/E2 pin, E5 flip, smoke catches) → move the types, add forwarders → green on all suites.
2. S: red tests (policy rows, the four rejection tables, the four semantics rows, S7) → the policy rule → green.
3. N: `Directory.Build.props`, public key, IVT keys, tests N1/N3/N4, ci.yml publish changes; full-sign locally with
   `-p:FunkyOrmKeyFile=D:\func\keys\FuncularLabs.snk`; consumer smoke both TFMs; `sn -vf` dry run.
4. C: the `test-net48` job; its commands run locally and recorded.
5. H: the merge, the two resolutions, the follow-up commit (§3.H); the four suites on the throwaway databases; counts.
6. K: `docs/KNOWN_ISSUES.md`, the Changelog pointers and delta, the README pointer, the Appendix A marks, the K5 doc
   fixes, the two tests.
7. Docs, Changelog [3.14.0], versions 3.14.0 in the five csprojs, the smoke scripts' default version.
8. Full suites (four providers + net48), coverage, mutations, hostile review rounds under the exit rule.

## 6. Suggestions for the episode-blocking items (not in this release)

Code-grounded (2026-11-02 survey of master 10c1c6b); each is a candidate for 3.14.x or 3.15.0, none is started.

**6.1 Lab 04 — `[SubqueryAggregate]` `Avg` over an `int` column into a `double` (SQL Server fails to read).**
Mechanism: `SqlServerDialect.BuildScalarSubquery` (:171) emits `AVG(col)` with no cast; SQL Server's `AVG(int)` is an
`int`; the materializer picks the typed getter by property type (`GetDouble`, `SqlServerOrmDataProvider.cs:2021`) and
SqlClient's typed getters don't convert. The same applies to `SUM(int)` into `long`, any int into `decimal`, and
`SUM(real)` into `float`. PostgreSQL is unaffected (Npgsql converts); SQLite already widens (:1218-1231). Fix: (i) in
the provider loop (`ResolveRemoteJoins`, :1303-1314, where both `prop` and `aggProp` are known) wrap the column for
`Avg` on SQL Server: `CAST(col AS FLOAT)` for a `double` target (plus an outer `CAST(... AS REAL)` for `float`),
`CAST(col AS DECIMAL(38,6))` for a `decimal` target over an integral source (scale is a decision); precedent
`SqlLinqQueryProvider.cs:703-707`. Not in the dialect (`ISqlDialect.BuildScalarSubquery` is public and two test probes
implement it). (ii) As a safety net, widen in `ComposeReaderMapper` only when `reader.GetFieldType` differs from the
property type, widening conversions only (a narrowing one throws with a clear message); the mapper is cached per schema
signature so there is no per-row cost. (ii) alone would return a silently truncated average (the FD-04 bug 3.11.0
removed from LINQ `Average`), so (i) is the fix and (ii) the net. Tests: none exist for Sum/Avg subqueries on SQL
Server; `NsComputed.ChildAvgD` deliberately averages a decimal to dodge this.

**6.2 Lab 04 — a non-nullable `Sum`/`Avg` over no child rows.** The fragment returned at `SqlServerOrmDataProvider.cs:1314`
feeds both the SELECT (:1318) and `PropertyToColumnMap` (:1317), which WHERE, ORDER BY, the delete guard and custom
Select read; so wrapping once at that call site applies everywhere. Condition: `(attr.Function == Sum || Avg) &&
!NullSemantics.CanBeNull(prop)` (public static, `NullSemantics.cs:137-142`). SQL Server: `ISNULL((subquery), 0)`
(not `COALESCE`, which expands to a CASE that runs the subquery twice and adds a CASE level the null-semantics design
says these hold none of); PostgreSQL/MySQL/SQLite: `COALESCE((subquery), 0)` (an untyped 0 takes the aggregate's
type). Effects: closes the Known issue at `Changelog.md:310-321`; ten pinned harness rows flip (their oracles' `!= null &&`
guards become `?? 0`, `NullSemanticsHarness.cs:1567-1589`, `1642-1677`); `Delete(x => x.Total == 0)` then deletes
parents with no children (matches the value read back; the Changelog must say so).

**6.3 `Update` with an enum (PostgreSQL) or unsigned (SQL Server, PostgreSQL) key.** The UPDATE's key parameter is
created from the raw property value at `SqlServerDialect.cs:117-118` / `PostgreSqlDialect.cs:131-132`, bypassing
`NormalizeKeyForBinding` (`OrmDataProvider.cs:629-641`, which `CreateGetCommand` uses). Smallest fix: in each provider's
`BuildUpdateCommand<T>` wrapper set the key parameter's `Value = NormalizeKeyForBinding(Value)` (the parameter has no
explicit type, so the driver re-infers it). Cleaner: a public static Core normalizer the dialects call. Gap a key-only
fix leaves: the SET clause and INSERT bind raw values too, so a changed `uint` property still fails on both and any
enum property on PostgreSQL; normalizing inside each dialect's `CreateParameter` fixes all three write paths. Tests
to mirror: `KeyParameterHarness` `Get_EnumKey_ReturnsThatRow` / `Get_UnsignedKey_ReturnsThatRow` (:452-504) as
`Update_EnumKey_UpdatesThatRow` / `Update_UnsignedKey_UpdatesThatRow`, sync and async.

**6.4 PostgreSQL enum parameters in `Where`; `DateTimeOffset` over `timestamptz`.** Enum: `PostgreSqlParameterGenerator`
(:40-71) types an enum as `Integer` but leaves the enum object as the value, and Npgsql 8/9 don't coerce (pinned as
rejected at `PostgreSqlNullSemanticsTests.cs:63-66`); `uint`/`ulong`/`ushort`/`sbyte` very likely fail the same way.
Fix: convert an enum to its underlying number first and type from that (also fixes long-backed enums, now forced to
Integer); share one normalizer with 6.3. `DateTimeOffset` read: `ComposeReaderMapper` has no branch, so
`Convert.ChangeType(DateTime, DateTimeOffset)` throws; add a branch that treats a `DateTime` as UTC (mirror
`AggregateSemantics.cs:101-106`). Write: `dto.ToUniversalTime()` in both generator methods and in
`PostgreSqlDialect.CreateParameter` (Npgsql requires offset 0). The three `RejectedByTheProvider` keys then run and
must leave that list. Side finding: SQL Server's `ParameterGenerator.GetSqlDbType` (:61-88) has no `DateTimeOffset`
case either, so a `Where` on one there appears to throw `NotSupportedException` (worth one test).

**6.5 Lab 06 — the guard admits `x.Id == 2 || x.Id != 2`.** `FoldLogical` (`WherePredicateGuard.cs:921-931`) keeps
`Or(Unknown, Unknown)` Unknown. A cheap extension under the verdict rules only (`rules != FoldRules.Sql`, so no SQL
text changes): return True for an `||` whose two direct operands are comparisons with complementary operators
(`==`/`!=`, `<`/`>=`, `>`/`<=`) and pairwise-equal operands (also commuted), via the existing `SameValue`/`SameShape`/
`SameConstant` helpers; only the `nEq5OrNNe5` pin (`NullSemanticsHarness.cs:1886`) and the "is accepted" docs flip.
It would still not catch complements that aren't direct operands of one `||`, the same value written two ways, negated
forms, or range/set tautologies (`x.Id < 5 || x.Id > 4`), which need real tautology reasoning. Recommendation: do it
and say exactly what it does and doesn't prove, as the Changelog already does for the plain shapes.

**6.6 SQLite fidelity (Guid hex letters, `DateTime` equality, `decimal` as text, culture dates).** Owner's call (WP-I,
OD-12 in the backlog), because existing databases hold culture-formatted text. Not scoped here.

## 7. Record

### 7.1 Task 0 (rev 2) dispositions, applied in rev 4

Non-author review of rev 2 (§3.K, filled in rev 3, was outside that round's scope and was first reviewed in round 2):
**NOT CLEAN, 3 Major, 9 Minor, 5 Nit.** Every finding accepted; blame classes as the reviewer assigned them unless noted.

| # | Sev | Blame | Disposition |
|:--|:--|:--|:--|
| 1 | Major | AC-GAP | "Bound" is now **identity-bound** (same member name, from the Select's own parameter, Convert unwrapped); cross, constant and chained bindings are unbound and their reads rejected; rows added on all four providers (the cross binding uses an inherited member, which the visitors pass); mutation M-S7 "bound = every binding". §1.S, S1–S4, §3.S, §4. |
| 2 | Major | PLAN-GAP | The net48 job filters `FullyQualifiedName!~PerformanceTests`. Premise checked 2026-11-02: the performance class is listed in the csproj (line 77) but its whole file sits under `#if PERFORMANCE_TESTS`, so it is not in the assembly unless that symbol is defined, and the whole-DLL baseline discovered none of its tests (TRX: 170 tests in six classes). The filter is adopted as the guard the other jobs have, with the `CiWorkflowTests` spelling. C1, §3.C. |
| 3 | Major | AC-GAP | N2 and N7 rewritten: the props error on a missing key file; the key reaches build and pack; `Verify-StrongNames.ps1` finds `sn.exe` by SDK path and fails if absent; `CiWorkflowTests` pins each step; `workflow_dispatch dry_run` exercises the job without the push; the script is rehearsed on a full-signed and a public-signed pack. |
| 4 | Minor | TEST-GAP | `test-net48` added to the `needs`/TRX/filter pins; logger `trx;LogFileName=test-results.trx` + `/ResultsDirectory:TestResults`. C1, §4. |
| 5 | Minor | PLAN-GAP | A scalar or unsupported Select clears the set; the `Select(subset).Select(t => t.Name).OrderBy(s => s.Length)` row expects the scalar-outermost message; S5 no longer calls that rule I1. |
| 6 | Minor | TEST-GAP | DB-free rows (a)–(f) added with mutations M-S8..M-S12. |
| 7 | Minor | PLAN-GAP | The four `<SignAssembly>False</SignAssembly>` lines are deleted (N2, §3.N). |
| 8 | Minor | PLAN-GAP | Test jobs and the C2 rehearsal are public-signed; the smoke runs both ways via `-KeyFile`; the bypass premise is proven by the net48 job itself (N5, §3.N, C2). |
| 9 | Minor | AC-GAP | "Cannot reference" corrected to CS8002 / GAC / strong-name hosts; the strong-named net48 smoke variant with `WarningsAsErrors=CS8002` is the red/green proof (§1.N, N5, N6). |
| 10 | Minor | AC-GAP | E2 restated: forwarders present, pinned; no binary-compatibility claim. |
| 11 | Minor | AC-GAP | `ExceptionTypes_KeepTheirBaseTypesAndConstructors` added; all three constructed (coverage of the moved file). |
| 12 | Minor | PLAN-GAP | H3 measured on fresh CI-shaped databases, not the local ones with leftovers. |
| 13 | Nit | OTHER | S7 cites `SqlLinqQueryProvider.cs:814-831`. |
| 14 | Nit | PLAN-GAP | The three "each provider defines its own" comments listed under E5. |
| 15 | Nit | PLAN-GAP | E3 says the red run is net8.0 only; S3 cites `Distinct_WithCount_ThrowsNotSupported`. |
| 16 | Nit | PLAN-GAP | C2 names `.\SQL2019` as the rehearsal database; the net48 allow-list is empty. |
| 17 | Nit | TEST-GAP | N3 asserts the FuncularLabs hex; N1's TFM scope stated. |

Reviewer's "not verified" items carried into the work: `sn.exe` on windows-latest (the verify script fails closed if
absent, and the dry run answers it before the release); `setup-msbuild@v2` + `vswhere` (the first CI run);
public-signed DLLs under vstest on net48 (the net48 job proves it on every run; the local rehearsal first); .NET 8
binding a null-token reference to a signed assembly (not relied on: consumers recompile); VS MSBuild honouring
`PublicSign` in a legacy project (the local rehearsal); the 40 × CS0436 warnings (E4's build-log record).

### 7.2 Task 0 round 2 (rev 4) dispositions, applied in rev 5

Fix-verification of rev 4: 13 of 17 resolved, 4 partial (3, 6, 10, 12), and **NOT CLEAN, 3 Major, 5 Minor, 5 Nit**
new. The reviewer withdrew the premise of round-1 finding 2 (the performance class is under `#if PERFORMANCE_TESTS`);
the filter stays as a guard. Every new finding accepted.

| # | Sev | Blame | Disposition |
|:--|:--|:--|:--|
| A | Major | AC-GAP | The push step runs only on a `push` event to `master`; a manual run from any branch never pushes; `dry_run` defaults to `true`; `CiWorkflowTests` pins the condition and the default; mutation M-N5. (N7, §3.N, §4.) |
| B | Major | AC-GAP | H3 measures on throwaway `funky_db_h3` databases built from name-substituted script copies and reached through the connection variables; `funky_db` is never dropped or re-created and its counts are recorded unchanged. (H3, §4.) |
| C | Major | OTHER | The AVG(int) entry's workaround is "into a `decimal` property" (the `int?` advice would read a truncated average); the `SUM(int)` into `long` claim is dropped as unverified; K1 now says a workaround must not itself be a wrong answer. (K1, the draft.) |
| D | Minor | PLAN-GAP | §3.K states K1's exclusions (3 closed-by-3.14.0 rows, 7 engineering rows) and the count, 47; K4 homes the engineering rows under a backlog heading. |
| E | Minor | TEST-GAP | The pointer line goes directly under each heading; the test's "within three lines" now matches K2. |
| F | Minor | TEST-GAP | S1 covers a later scalar Select of an unbound member (rejected before the set is cleared); rows and mutations M-S13 (clear before check) and M-S14 (don't descend into nested lambdas) added. |
| G | Minor | PLAN-GAP | §3.E's rationale no longer claims a 3.13.0 binary binds; E6 says "no binary compatibility: recompile". |
| H | Minor | OTHER | Draft K-01: trailing spaces count as in C# (removed from the divergence), the remedy is in memory, not a `Trim` the translators lack; K-40's remedy is `Get<T>(id)`. |
| I | Nit | OTHER | Draft: K-42/47/48/49 get a workaround or by-design reason; the mark reads "not re-verified since 3.10"; K-55 cites the 2026-10-20 report; K-24 carries "re-verify after 3.12.1". |
| J | Nit | OTHER | The K header reads nine fixed in 3.10.0–3.12.1 and one in 3.13.0. |
| K | Nit | PLAN-GAP | Bypass since 3.5 SP1; `sn.exe` found by the `NETFX 4.8*Tools` glob; the header no longer calls the toolchain the only open question. |
| L | Nit | OTHER | §7.1's opening line corrected. |
| M | Nit | PLAN-GAP | `Run-ConsumerSmoke.ps1 -KeyFile` and `-StrongNamedConsumer` (public-signed with the committed key) specified; the 3.13.0 red half is a scratch run. |

### 7.3 Task 0 round 3 (rev 5) dispositions, applied in rev 6

Fix-verification of rev 5: **NOT CLEAN, 0 Major, 5 Minor, 2 Nit.** H2 and the hygiene plan's rev 14 paragraph were
verified accurate (the EF harness guard dates from 98eefef; the 14 members arrived in 3.7.0). Every finding accepted.

| # | Sev | Blame | Disposition |
|:--|:--|:--|:--|
| 1 | Minor | OTHER | The count is 51 (73 − 10 FIXED − 2 PLANNED − 3 closed by 3.14.0 − 7 engineering), shown as arithmetic in §3.K. |
| 2 | Minor | PLAN-GAP | The EF class removed from H3, §3.H and Task 5. |
| 3 | Minor | PLAN-GAP | TEST_ROW_HYGIENE_PLAN.md §1.1 and §6 passages marked superseded by rev 14; the Changelog line says the class doesn't compile and isn't run. |
| 4 | Minor | AC-GAP | H3 says schema **and data** script copies, naming the `USE funky_db;` override; the measurement script already substituted both. |
| 5 | Minor | TEST-GAP | The job-level `if` admits `workflow_dispatch`; the pin test renamed `…_AndPushesOnlyFromMaster`; mutation M-N6; §7 will record the dry run's publish-job conclusion. |
| 6 | Nit | OTHER | The K header line corrected (nine in 3.10.0–3.12.1, one in 3.13.0). |
| 7 | Nit | OTHER | Status line rev 6; K1 names K-67 as the published-package DLL-version check; H2 names 3.7.0 and the 14 members. |

Carried for the final review: `1b7b84a`'s new test code and `48dd2d7`'s two conflict resolutions are not covered by
the CLEAN pass at 9c37ac0 and get their own adversarial pass before any push.

### 7.4 Stream H record (2026-11-02)

- Merge `48dd2d7`; follow-up `1b7b84a`; supersession notes `371e9b8`. The ledger contract tests pass on SQL Server,
  PostgreSQL and MySQL (7/7 each) after the merge; the MySQL scoped-cache read test passes on the local database.
- **H3 on throwaway databases** (`funky_db_h3` on `.\SQL2019`, PostgreSQL 18 and MySQL 8, created from
  name-substituted copies of the schema and data scripts; `funky_db` never dropped, its counts recorded before and
  after on all three servers and unchanged; evidence in scratchpad `h3/`). Suites ran from copies of the test build
  outputs so later rebuilds could not disturb them, which makes the repository-relative DB-free tests (the packaging
  and CI pins, `Compile_OnlyInTheSeam`) fail in those runs: 49 such on SQL Server and 1 on net48, none database
  related.
  - MySQL (schema only, all guarded tables empty): 1996 passed, 0 failed; counts unchanged.
  - SQL Server and PostgreSQL, first run on the empty databases: `organization` 0 → 2 on both, quoted from the
    terminal at the time, like the pass counts beside it (SQL Server 3431 passed, 76 failed = 27 stream-E red pins
    at that moment + 49 repository-relative; PostgreSQL 2051 passed, 0 failed; net48 171/172). No file holds the
    first run: the rerun script wrote the same count-file names (`h3/*-h3-before.txt`/`-after.txt`, which now show
    2 → 2) and its own logs (`ss2.log`/`pg2.log`/`net48-2.log`); the `ss.log`/`pg.log`/`net48.log` left in `h3/`
    are an earlier attempt that failed to reach the throwaway databases (762 failed / 1863 skipped / 73 failed)
    and prove nothing.
    The two rows are `ComputedTest Org` / `JsonTest Org` (PostgreSQL: `PgComputedTest Org` / `PgJsonTest Org`),
    created once by the computed-attribute and JSON-path classes' `SeedTestData` with raw `IF NOT EXISTS … INSERT`
    SQL before the ledger's count, as reference data; on the shared databases they have existed for months, which is
    why neither the branch's measurement nor the local runs could see them.
  - SQL Server, second run: 3457 passed (50 = the 49 + 1 repository-relative); PostgreSQL 2050 passed; net48
    171/172; **every guarded count unchanged** on both. Per-run growth is zero; the one-time reference seed is
    recorded as a follow-up in TEST_ROW_HYGIENE_PLAN.md rev 14 (seed it from the data scripts, or delete it in a
    `ClassCleanup`), not fixed here.
  - Also found: the SQL Server data script's 50 `project` inserts fail their FK to `organization` (Msg 547) on a fresh
    database, so the project tables are empty there; CI runs the script without `-b` and ignores it. Backlog.
### 7.5 Streams E and S record (2026-11-02)

- **E** (`c259032`): red run against 3.13.0's layout: the three pins failed for the right reasons (type not in Core;
  base-type pin; SqlServer still declaring the type) and 24 `NullableValueToString_IsRejected` rows failed on the
  assembly assertion. Green after the move: pins 3/3; the exception-related subsets SQL Server 856, PostgreSQL 504,
  MySQL 497, SQLite 184; net48 builds; the package project's Release build reports **0 × CS0436** (40 before); the
  solution's warning count fell from 251 to 175. Consumer-smoke `catch` clauses compile against the local pack (the
  smoke runs are in §7.6). The CS0433 red proof against nuget.org 3.13.0 is scratchpad `cs0433red/`.
- **S** (`01172b0`): red run with the unchanged policy: SQL Server 39 of 49 new tests failed, PostgreSQL 28/35, MySQL
  29/35, SQLite 28/35, net48 9/14 (every rejection row, the four Distinct flips, the DB-free rows); two test defects
  found in that run and fixed before green (the oracle rows compared `"A"` against the seeded `"a"`, which only a
  case-insensitive collation matched; the hand-built `Max` spine picked the one-generic-argument overload). Green:
  SQL Server 49/49, PostgreSQL 35/35, MySQL 35/35, SQLite 35/35, net48 14/14. Full regression on the E+S build:
  SQL Server 3551 passed + 1 skip + 1 expected failure (`VersionCarriers_MatchTheChangelog`, the csprojs said 3.13.0
  until the bump), PostgreSQL 2083/2083, MySQL 2026/2026, SQLite 1534/1534, net48 186/186.

### 7.6 Streams N and C record (2026-11-02)

Public-signed solution build: 0 errors, 246 warnings, **0 × CS8002** (the test-only Spittoon packages did not raise
it after all: the compiler emits CS8002 for .NET Framework targets only, see below) and 0 × CS0436; net48 builds under
VS MSBuild; every production assembly, the net8.0 test assembly and the net48 test assembly carry token
`0650eba294fc9e3f` at 3.14.0.0 (tests keep their own versions). DB-free pins: 71/72 then 72/72 (one pin's quoting fixed).
Rehearsal (scratchpad `rehearsal.sh`, logs in `n/`):
1. `-p:FunkyOrmKeyFile=C:/nope/FuncularLabs.snk` fails with the props' own message. ✓
2. Fully signed build + pack with the owner's key; `Verify-StrongNames.ps1`: PASS, 12 DLLs, all token `0650eba294fc9e3f`. ✓
3. Public-signed pack; `Verify-StrongNames.ps1`: FAIL, exit 1, all 12 DLLs "Strong name validation failed" (the mutation). ✓
4. Consumer smoke: public-signed PASS (net8.0 + net48); `-KeyFile` PASS; `-StrongNamedConsumer -KeyFile` PASS. ✓
5. The strong-named consumer's red half against nuget.org 3.13.0: on **net8.0 it built clean** (no CS8002: the compiler
   does not emit it for .NET targets, fully signed or public-signed, checked both ways with a throwaway key); on
   **net48 it fails with `error CS8002` for `Funcular.Data.Orm` and `Funcular.Data.Orm.Core`** (`sn_red/`, exit 1). So
   the smoke's `-StrongNamedConsumer` mode proves its point on the net48 target only, and the Changelog says "a
   strong-named .NET Framework consumer". ✓ (wording corrected)
6. The net48 CI job rehearsed with its exact commands on a public-signed build: MSBuild `-restore -t:Rebuild` exit 0,
   vstest with the filter 186/186, the TRX gate exit 0 ("186 results; every test that didn't run is on the allowlist"),
   the loaded `Funcular.Data.Orm.dll` is 3.14.0.0 with the token: public-signed assemblies load under vstest on
   .NET Framework (the bypass premise holds). ✓
Open for the first CI run: `sn.exe` on windows-latest (the verify script fails closed if absent), `setup-msbuild@v2`
plus `vswhere` resolving vstest on the runner, and the `FUNKYORM_SNK_BASE64` secret, which the owner must add
before the release merge (a manual `workflow_dispatch` run from the branch exercises the publish job without pushing).

### 7.7 Mutations and coverage (2026-11-02)

Mutations executed by the non-author reviewer in a detached worktree at `aa6d8a9` (review round 1), each reverted:

| Mutation | Tests run | Result |
|:--|:--|:--|
| M-S1 never set `boundMembers` | DB-free policy rows; SQLite | killed: 10 DB-free rows; 28 SQLite rows |
| M-S7 every binding counts as bound | DB-free; SQLite; SQL Server | killed: 2 DB-free; the cross, constant and chained-cross rows on each provider |
| M-S12 never clear at a scalar Select | same | killed: `…ClearsTheSet`; `…GetsTheScalarOutermostMessage` on both providers |
| M-S13 clear at a scalar Select before checking its lambda | same | killed: the DB-free row; the "Select(scalar, unbound)" row on both providers |
| M-S14 don't descend into nested lambdas | same | killed by the DB-free nested-lambda row (the provider tables have none; the policy is shared) |
| M-E1 drop the MySql `NullableExpressionException` forwarder | Packaging | killed (`ExceptionTypes_AreDefinedOnceInCore_AndForwardedByEveryProvider`) |
| M-K1 SqlServer csproj `Version` → 3.14.1 only | Packaging | killed (the as-of pin and `VersionCarriers_MatchTheChangelog`) |
| M-N1 `SignAssembly=false` on Sqlite | Packaging | killed at compile (CS8102: PublicSign with no key); the `PublicSign=false` variant killed by the token and public-key tests |
| M-N5a delete the push step's `if`; M-N5b keep only the ref check | Packaging | both killed |
| M-C1 drop `test-net48` from publish's `needs` | Packaging | killed |

Baselines before mutating: DB-free 87/87; SQL Server S subset 504/504; SQLite S subset 510/510. The round-1 fix
layer's own red/green: the Select-after-Distinct rows failed against the unchanged policy (SQL Server 5 of 6, the
sixth being the "Select then Distinct still passes" row; SQLite 4 of 4) and passed after it, with the operator,
packaging, LikeToString, computed and scoped-cache subsets green on SQL Server (1,427), PostgreSQL (1,003), MySQL
(1,000), SQLite (685) and net48 (189, the new token pin included).

Coverage (cobertura, `dotnet test … --collect:"XPlat Code Coverage"` over the SQL Server suite's operator, packaging,
LikeToString, remote-key, parity and translator subsets, 1,577 tests; scratchpad `r1/cov/`): line rate
`QueryOperatorPolicy.cs` 0.995 (the class) and 1.000 (`UnboundReadFinder`); `Core/Exceptions/RemoteKeyExceptions.cs`
1.000 for each of the three classes; the four `TypeForwards.cs` files hold attributes only. The reviewer's independent
measurement at `aa6d8a9` agreed (99.6 % / 100 % / 100 %).

### 7.8 Review round 1 (`10c1c6b..aa6d8a9`) dispositions

Non-author hostile review: **NOT CLEAN, 2 Major, 4 Minor, 4 Nit.** Every finding accepted.

| # | Sev | Blame | Disposition |
|:--|:--|:--|:--|
| 1 | Major | AC-GAP | A `Select` after `Distinct()` is rejected by the policy (`NotSupportedException`, "apply Distinct after the Select"); DB-free rows and a four-row rejection table in each provider suite, red before the policy change and green after; the Changelog's Changed entry, Advanced.md, the AI files and README say so. `Select(...).Distinct()` is unchanged (the oracle rows cover it). |
| 2 | Major | AC-GAP | README.md, Advanced.md, FUNKYORM_AI_ADVANCED.md and FUNKYORM_AI_INSTRUCTIONS.md now distinguish an ordering after the `Select` (the policy's `NotSupportedException`, 3.14.0) from one before it (`InvalidOperationException`, the DISTINCT key check). |
| 3 | Minor | PLAN-GAP | `Directory.Build.props` stamps the signing mode into the compiler's inputs (`CustomAdditionalCompileInputs`, written only when different), so an incremental build with the key recompiles; `Run-ConsumerSmoke.ps1 -KeyFile` builds `--no-incremental` with the key before packing and verifies the pack; the "pack rebuilds" wording corrected in §3.N, N2 and the CI pin; N5 re-recorded in §7.9. |
| 4 | Minor | OTHER | §7.7 added (mutations, coverage); §7.4 rewritten to cite the surviving logs and say which were lost. |
| 5 | Minor | TEST-GAP | `StrongNamingNetFrameworkTests` added to the net48 project (Core and Funcular.Data.Orm carry the token as loaded by the .NET Framework host); the N1 row names it. |
| 6 | Minor | OTHER | The unverified `TargetInvocationException` half of Appendix A #21 is a marked entry in docs/KNOWN_ISSUES.md (52 entries) and #21 is in the backlog's probe list. |
| 7 | Nit | OTHER | The moved summary says "would produce … so the translator throws this instead". |
| 8 | Nit | OTHER | `ProviderAssembly` and its four overrides deleted. |
| 9 | Nit | OTHER | `Exceptions/TypeForwards.cs`; §7 reordered; the Changelog says the items recorded against 3.10 that nobody re-ran are marked. |
| 10 | Nit | OTHER | The MySQL scoped-cache test keeps its own failure visible when the cleanup fails too (`AggregateException`). |

### 7.10 Review round 2 (fix-verification of `fee05c4`) dispositions

**NOT CLEAN, 0 Major, 3 Minor, 2 Nit**; #1, #2, #6, #7, #8, #10 verified resolved; #3, #4, #5, #9 partial because
round 1's documentation script applied two edit batches to the plan file and the second overwrote the first (the
script now refuses two batches on one file).

| # | Sev | Blame | Disposition |
|:--|:--|:--|:--|
| R2-1 | Minor | OTHER | The five lost edits made: the ci.yml comment, N2, §3.N, the N1 row (which now also names the stamp pin), §3.E's path. |
| R2-2 | Minor | OTHER | §7.4 says the first run's 0 → 2 comes from the terminal at the time and that no file holds it. |
| R2-3 | Minor | AC-GAP | S9 added with its §4 row and mutation M-S15; S3 names the exception; Advanced.md §5's `Distinct` row and positional paragraph, the AI positional table and checklist item 14 carry the rule; §3.K says 52. |
| R2-4 | Nit | OTHER | The smoke builds before packing in both branches; the verify script runs under whichever PowerShell is running the smoke; `.PARAMETER KeyFile` says Windows only (sn.exe). |
| R2-5 | Nit | OTHER | The #21 entry names the documented `NotSupportedException`; the policy message offers both rewrites (distinct projections via `Select(...).Distinct()`, LINQ's result via `Distinct().ToList().Select(...)`); the remarks paragraph reflowed; `TheSigningModeStamp_IsACompilerInput` pins the stamp in the props. |

### 7.9 N5 re-recorded after the signing-mode stamp (2026-11-02, scratchpad `r1/`)

1. A fresh public-signed build of the package project, then an **incremental** build with `-p:FunkyOrmKeyFile`, then
   pack and `Verify-StrongNames.ps1`: **PASS, 12 DLLs** fully signed with the token (before the stamp this left
   public-signed DLLs and failed on all 12).
2. An incremental build back without the key, pack, verify: **FAIL, exit 1** on every DLL ("Strong name validation
   failed"), as a public-signed pack must.
3. `Run-ConsumerSmoke.ps1 -KeyFile`: builds `--no-incremental` with the key, packs, verifies the pack, runs: PASS on
   net8.0 and net48. `-KeyFile -StrongNamedConsumer`: PASS on both (the net48 target is the one that can fail, §7.6).
4. The pins after the CI-pin wording change: 72/72.

