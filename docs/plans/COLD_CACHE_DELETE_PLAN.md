# Delete by predicate on a cold column cache — Implementation Plan

> **Goal:** make `Delete<T>(predicate)` and `DeleteAsync<T>(predicate)` work when type `T` hasn't been mapped yet in
> the process. Today they throw `NotSupportedException: Expression type Parameter is not supported`.
> - Branch `fix/mysql-delete-cold-cache`, from `master` (`fae4472`, 3.9.0), per release-branch alignment; it doesn't
>   target `development/3.10`.
> - Recorded in the 3.10 plan's §8, "MySQL `Delete<T>(predicate)` on a cold column cache".

> **Status (2026-10-01):** rev 1, the test plan (Task 0). Nothing is implemented yet.

## 1. Verified premises (author, by reading `fae4472`)

- **Unmapped-property cache.**
  - `GenerateWhereClause<T>` builds the WHERE visitor from `_unmappedPropertiesCache.GetOrAdd(typeof(T),
    GetUnmappedProperties<T>)`. The four providers do this at `MySqlOrmDataProvider.cs:822`,
    `PostgreSqlOrmDataProvider.cs:844`, `SqlServerOrmDataProvider.cs:1440` and `SqliteOrmDataProvider.cs:784`.
  - `GetUnmappedProperties<T>` counts as "implicitly unmapped" every property with no `[Column]` and no entry in the
    column cache (`MySqlOrmDataProvider.cs:1486-1493`, and the same in the other three).
  - On a cold cache, every convention-mapped property is therefore "unmapped", and `GetOrAdd` keeps that answer for
    the rest of the process.
- **How the visitor fails.** It treats an unmapped member as no column and visits the member's object, the lambda
  parameter. `MySqlWhereClauseVisitor.VisitMember` → `VisitExpression` then throws "Expression type Parameter is not
  supported".
- **The gap is in all four providers.** No `GenerateWhereClause` caller runs `DiscoverColumns<T>()` first:
  `Delete<T>(predicate)`, `DeleteAsync<T>(predicate)` and `CreateSelectQueryObject<T>`, in every provider.
  - `Query`, `Get`, `Insert` and `Update` all discover first, so the defect shows only when a predicate delete is
    the first use of a type. The MySQL qualification tests' cleanup does exactly that when the class runs alone.
- **Declaring-type keys.** Column keys use the declaring type (`ToDictionaryKey`). So a type is "cold" only if the
  type that declares its members has never been discovered either; an inherited member is cold if its base class is.

## 2. Decisions

- **D1 — Discover at the chokepoint.** `GenerateWhereClause<T>` calls `DiscoverColumns<T>()` before it reads the
  unmapped cache, in all four providers.
  - This covers every caller, now and future: deletes, `CreateSelectQueryObject`, and the LINQ providers, where it
    is a no-op because `Query<T>()` already discovered.
  - Discovery under an open transaction borrows the transactional connection (existing behaviour), so a delete
    inside `BeginTransaction` works.
- **D2 — Ordering is the cure for poisoning.** Discovery runs first, so the unmapped set is computed from discovered
  columns.
  - If discovery throws (missing table, unreachable database), `GetOrAdd` is never reached, so nothing is cached
    and the next call retries.
  - `_mappedTypes` is still added only after a successful read (existing code).
- **D3 — No change to the visitor.** The visitor's handling of a member it doesn't know is out of scope; the root
  cause is the missing discovery.

## 3. Acceptance criteria

- **AC1:** On a cold cache, `Delete<T>(predicate)` on a convention-mapped inherited member deletes exactly the
  matching rows, on all four providers.
- **AC2:** The same for `DeleteAsync<T>(predicate)`.
- **AC3:** A cold delete leaves the type usable. A following `Query<T>()` on the same type returns the surviving row,
  with its inherited and declared convention-mapped members populated.
- **AC4:** The two MySQL tests named in the 3.10 plan's §8 pass when run alone:
  - `ComputedMemberOrderBy_EmitsExpression_Unchanged`;
  - `ComputedAttributeEntityWithoutJoins_OwnColumnOrder_Unqualified`.

  Note: they exist on `development/3.10` only; this is verified after the merge forward.
- **AC5:** All four provider suites stay green.

## 4. Test plan

### 4.1 AC → test matrix

| AC | Test | Projects |
|---|---|---|
| AC1, AC3 | `Delete_ByInheritedMember_OnAColdCache_DeletesTheRow_AndTheTypeStaysQueryable` | all 4 (`ColdCacheDeleteTests`) |
| AC2, AC3 | `DeleteAsync_ByInheritedMember_OnAColdCache_DeletesTheRow_AndTheTypeStaysQueryable` | all 4 |
| AC4 | the two named tests, each run alone with a `--filter` | MySql (on `development/3.10` after the merge) |
| AC5 | the four full suites | all 4 |

Each test is cold by construction:
- It uses its own entity types: `ColdDeletePerson` and `ColdDeleteAsyncPerson`, each over its own abstract base
  (`ColdDeleteBase` declares `Id` and `LastName`; the derived class declares `FirstName`), mapped to `person` by
  `[Table]` only.
- No other test uses these types or their bases.
- It seeds two rows (`gone`, `kept`) with the suite's existing person entity, which warms only that entity's keys.
- It deletes `gone` by `LastName == marker && FirstName == "gone"` inside a transaction, then queries the type.
- Its cleanup deletes the seeded rows through the warm entity.

### 4.2 Mutations each key test must kill

| Mutation | Killed by |
|---|---|
| No `DiscoverColumns<T>()` in `GenerateWhereClause<T>` (per provider) | AC1/AC2 rows of that provider |
| Discovery moved after the unmapped `GetOrAdd` (poisoned set) | AC1/AC2 rows (the delete throws) |

### 4.3 Coverage

`GenerateWhereClause<T>` is called by every test in §4.1. The touched provider files are large, and their existing
line coverage is reported as measured, not claimed.

## 5. Tasks

1. **Task 0** — test-plan review (non-author).
2. **Task 1** — red tests, run on `fae4472`, with the counts recorded.
3. **Task 2** — D1 in all four providers; green; mutations; the four suites.
4. **Task 3** — Changelog (Fixed); hostile review and fix-verification to CLEAN; then the push and the merge forward
   per the owner.

## 6. Out of scope (recorded)

- **The LINQ providers' own unmapped lambdas.** They also fill `UnmappedPropertiesCache` with their own lambdas
  (`[NotMapped]` only). Whichever caller fills the entry first decides it for the process. This is pre-existing,
  and unrelated to cold deletes.
- **Static caches.** Identifier caches are static and shared across providers. That is being fixed separately on
  `development/3.10` (`PROVIDER_SCOPED_CACHES_PLAN.md`).
