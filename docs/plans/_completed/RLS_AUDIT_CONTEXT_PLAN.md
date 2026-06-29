# Session Context for Row-Level Security & Audit Attribution — Implementation Plan

> **Goal:** let an application that authenticates to the database as a single identity (e.g. a managed
> identity) attach the *end-user's* identity to every command FunkyORM sends, so that (1) a Row-Level
> Security predicate can filter by it and (2) the audit trail can attribute statements to the human.
> The capability is **generic**: the application supplies an arbitrary set of named session-context
> keys; FunkyORM primes them onto the exact connection each command runs on. Target release: **v3.8.0**.

> **Revision (2026-06-29):** scoped against requester answers — all providers to their capability level;
> session-context keys are **caller-defined** (not hardcoded to any `UserId`/`TeamIds`/`RequestId`
> semantics); keys default to write-once/immutable; PHI paths are mostly *not* stored procedures;
> unauthenticated (non-PHI) requests exist, so fail-closed is **opt-in per provider**, not global.

---

## 0. Provider Capability Matrix

| Capability | SQL Server | PostgreSQL | MySQL | SQLite |
|:--|:--|:--|:--|:--|
| Prime named keys onto the connection | ✅ `sp_set_session_context` | ✅ `set_config()` | ✅ session user-vars | ❌ no-op |
| RLS **filtering** can consume them | ✅ `SESSION_CONTEXT(key)` | ✅ `current_setting(key)` | ❌ no native RLS | ❌ N/A |
| **Audit attribution** | ✅ | ✅ | ✅ | ❌ |
| Per-key immutability (`read_only`) | ✅ native | ⚠️ emulated (tx-local) | ❌ (best-effort) | — |
| Self-attributing audit comment (text commands) | ✅ | ✅ | ✅ | n/a |

"To their level of capability" means: SQL Server + PostgreSQL get filtering **and** attribution; MySQL gets attribution only; SQLite is a no-op (single-file, no session/RLS model).

---

## 1. Design Principles

1. **Generic, not opinionated.** FunkyORM knows nothing about "users" or "teams." The app provides a
   `FunkyAuditContext` = a set of `(Key, Value, ReadOnly)` entries plus two optional opaque identifiers
   for the audit comment. Names like `UserId`/`TeamIds`/`RequestId` are *examples in docs*, never types.
2. **Prime on the connection the command will use.** The only place that reliably knows that connection
   is the provider's `ConnectionScope`. Priming happens there.
3. **Prime once per connection lifetime, not per command** (see §3 — forced by `read_only` semantics and
   by `CommandType.StoredProcedure` commands that can't carry a prefix batch).
4. **Opportunistic prime, opt-in fail-closed.** If a context is present, prime it. Throwing when it's
   *absent* is governed by a per-provider `RequireAuditContext` flag, because the same app has legitimate
   unauthenticated paths (app settings, health checks).
5. **FunkyORM never owns the `AsyncLocal`.** It consumes an `IAuditContextAccessor` the app implements.

---

## 2. API Design (Core)

```csharp
namespace Funcular.Data.Orm;

/// One session-context key primed onto the connection. Value is treated as an opaque string.
public sealed class SessionContextEntry
{
    public string Key { get; }
    public string Value { get; }
    public bool ReadOnly { get; }   // SQL Server @read_only=1; emulated/ignored elsewhere
    public SessionContextEntry(string key, string value, bool readOnly = true)
    { Key = key; Value = value; ReadOnly = readOnly; }
}

/// The app's per-request context. All fields optional; an empty/absent context primes nothing.
public sealed class FunkyAuditContext
{
    /// Caller-defined keys primed onto the connection (consumed by the app's RLS predicate / audit).
    public IReadOnlyList<SessionContextEntry> Entries { get; init; } = Array.Empty<SessionContextEntry>();

    /// Optional opaque identifiers embedded in the self-attributing audit comment. MUST NOT contain PII.
    public string? AuditSubjectId { get; init; }       // e.g. an Entra object id
    public string? AuditCorrelationId { get; init; }   // e.g. a request/correlation id
}

/// Implemented by the app over its own AsyncLocal. Returns null when no context is established.
public interface IAuditContextAccessor
{
    FunkyAuditContext? Current { get; }
}

/// Provider-level configuration.
public sealed class AuditContextOptions
{
    public IAuditContextAccessor? Accessor { get; set; }       // null ⇒ feature disabled
    public bool RequireAuditContext { get; set; }              // fail-closed when true AND context absent
    public bool EmitAuditComment { get; set; } = true;         // leading /* funky:audit ... */ on text cmds
    public bool Enabled => Accessor != null;
}
```

The options are attached to a provider the same way `Log`/`Dialect` are today (settable property +
constructor overload). The app's ORM factory stamps them onto each provider it creates (§6).

---

## 3. The Priming Mechanism

### 3.1 Hook point: `ConnectionScope` connection acquisition
Every CRUD/query/sproc path runs inside a `ConnectionScope`; it is the single place that knows whether
the connection is **owned** (fresh, non-transactional) or the **shared transactional** connection, and it
even covers the one metadata command (`ResolveTableName`) that bypasses `BuildSqlCommandObject`.

### 3.2 Prime-once-per-connection-lifetime
A naive "prefix the batch on every command" is wrong twice:
- **`read_only` keys cannot be re-set** on the same session — a second command errors *"the key has
  already been set."* (All requester keys are write-once, so this *will* fire.)
- **`CommandType.StoredProcedure` commands** (our 3.7.0 `Exec*`) have no text to prepend to.

Therefore:
- **Owned (non-transactional) connection** — prime immediately after `Open()`. ADO.NET runs
  `sp_reset_connection` on pool checkout, so the server session starts clean; one prime is correct and
  covers multi-command ops (e.g. `Update`'s read-then-write on the same scope connection).
- **Shared transactional connection** — prime **once** when the transaction's connection is established;
  track with a `_transactionPrimed` flag reset on Begin/Commit/Rollback. Later scopes must not re-prime.

> Track by *logical connection lifetime*, not the physical `SqlConnection` instance (pooled instances are
> reused after reset, so a per-instance flag would wrongly report "already primed").

### 3.3 Prime on the scope's existing connection — never a nested scope
The primer executes against `scope.Connection`. It must not open its own `ConnectionScope`; inside a
transaction that would trip the transactional-concurrency guard (the 3.6.1 defect class).

### 3.4 Sync/async
`ConnectionScope` opens connections **synchronously** even on async paths today, so a synchronous prime
`ExecuteNonQuery` is consistent (one extra round trip per connection; zero for the audit comment). An
async prime can follow if/when the open becomes async.

### 3.5 Fail-closed (opt-in)
When `RequireAuditContext == true` **and** `Accessor.Current == null`, throw `InvalidOperationException`
at prime time, before any command runs. When `RequireAuditContext == false`, a null context simply primes
nothing (opportunistic). This is the mechanism that lets PHI repositories be strict while app-settings /
health-check repositories stay open (see §6 + §7).

---

## 4. Self-Attributing Audit Comment

For **text** commands only, `BuildSqlCommandObject` prepends (when context present and `EmitAuditComment`):
```
/* funky:audit sub=<AuditSubjectId> corr=<AuditCorrelationId> */
<existing command text>
```
- Identifiers only — **no PII** (the app chooses what goes here; docs warn loudly).
- **Injection-proofed:** values are validated against a safe charset (`^[A-Za-z0-9._:\-]{1,128}$`) and the
  sequence `*/` is rejected; anything else throws at context construction (fail-fast, not silent strip).
- Sproc commands (`CommandType.StoredProcedure`) get no comment.

---

## 5. Per-Provider Mechanism

### 5.1 SQL Server (full)
Per entry, one parameterized batch (one round trip):
```sql
EXEC sys.sp_set_session_context @key=N'<Key>', @value=@__v0, @read_only=<0|1>;
-- ...one EXEC per entry...
```
RLS predicate (app-authored) reads `SESSION_CONTEXT(N'<Key>')`; CSV values are split with `STRING_SPLIT`.

### 5.2 PostgreSQL (full filtering; emulated immutability)
```sql
SELECT set_config('<Key>', @__v0, false) /*, ... per entry ... */;
```
`is_local=false` = session-scoped; **validate** Npgsql clears custom GUCs on pool return (else issue an
explicit reset on owned-connection dispose). Inside a transaction, `is_local=true` is preferred (cannot
leak). RLS policy reads `current_setting('<Key>', true)`. Immutability is emulated (no read-only GUC).

### 5.3 MySQL (attribution only)
```sql
SET @<Key> := @__v0 /*, ... per entry ... */;
```
No native RLS — supports triggers/views/audit attribution, not filtering. MySqlConnector resets session
vars on connection reset; validate. `read_only` is best-effort (not enforceable).

### 5.4 SQLite
No-op. If `RequireAuditContext == true`, treat as a configuration error at provider construction (there is
no isolation model to enforce), rather than silently passing — decision: **throw at construction** so a PHI
config can't be pointed at SQLite by accident.

---

## 6. DI Integration (consumer-side shape we target)

Matches a standard layered registration (extension methods; repositories inject an ORM factory):

```csharp
// 1. The app implements the accessor over its own AsyncLocal.
services.AddSingleton<IAuditContextAccessor, AsyncLocalAuditContextAccessor>();

// 2. Auth middleware sets the context per request (authenticated routes only).
app.Use(async (ctx, next) => {
    if (ctx.User?.Identity?.IsAuthenticated == true)
        accessor.Set(new FunkyAuditContext {
            Entries = new [] {
                new SessionContextEntry("UserId",   objectId.ToString()),
                new SessionContextEntry("TeamIds",  string.Join(",", teamKeys)),
                new SessionContextEntry("RequestId", ctx.TraceIdentifier),
            },
            AuditSubjectId = objectId.ToString("D"),
            AuditCorrelationId = ctx.TraceIdentifier,
        });
    await next();
});

// 3. The ORM factory stamps AuditContextOptions onto every provider it builds.
//    PHI repositories request a strict provider; non-PHI a lenient one.
ormFactory.CreateProvider(requireAuditContext: true);   // PHI repos
ormFactory.CreateProvider(requireAuditContext: false);  // app-settings / health repos
```

**Recommendation for the unauthenticated/non-PHI boundary:** put it at the **provider/repository** level
(factory produces strict vs lenient providers). Priming is opportunistic in both; only the throw differs.
This fits the layered DI cleanly and avoids per-call opt-outs. (An ambient `SystemOperationScope` escape
hatch can be added later if a single repository genuinely mixes PHI and anonymous reads.)

---

## 7. Fail-Closed & Bootstrap / Metadata Queries

`DiscoverColumns<T>` (schema probe) and `ResolveTableName` (catalog lookup; bypasses the command seam) run
lazily and may run outside a request (warmup, migrations, health checks). Decision: run them under an
internal **system context** that bypasses fail-closed and primes no identity (metadata is not PHI;
`INFORMATION_SCHEMA` is not RLS-filtered). Implement as an internal `using (SystemContextScope())` around
those two call sites so a strict PHI provider doesn't throw during bootstrap.

---

## 8. Edge Cases (consolidated)

| # | Issue | Resolution |
|:--|:--|:--|
| 1 | `read_only` re-set on reused connection | Prime once per connection lifetime (§3.2) |
| 2 | `CommandType.StoredProcedure` can't carry a prefix batch | Prime the connection, not the statement |
| 3 | Priming via a nested scope trips the 3.6.1 guard | Prime on `scope.Connection` directly |
| 4 | Pooled-instance reuse defeats per-instance "primed" flags | Key tracking to logical lifetime |
| 5 | Comment injection | Safe-charset validation, fail-fast at context construction |
| 6 | Metadata/bootstrap vs fail-closed | System-context bypass (§7) |
| 7 | PG/MySQL pool reset clearing context | Validate per driver; explicit reset on dispose if needed |
| 8 | SQLite + `RequireAuditContext` | Throw at construction (no isolation to enforce) |

---

## 9. Testing Strategy

- **Unit (no DB):** accessor wiring; fail-closed throws only when required+absent; opportunistic prime when
  lenient+absent; comment is safe-charset-validated; sproc commands get no comment; system-context bypass.
- **SQL Server integration (needs new RLS test objects):** `SESSION_CONTEXT(key)` returns primed values in
  a query; `read_only` immutability (second op cannot overwrite); value survives a multi-command op
  (`Update` read+write) and across commands in one transaction; **does not leak** across two sequential
  non-transactional ops (pool reset); an end-to-end RLS table filters rows by the primed key.
- **PostgreSQL integration:** `current_setting()` parity; transaction-local vs session; RLS policy filters.
- **MySQL:** session vars present for attribution; explicitly assert no filtering claim.
- **SQLite:** no-op asserted; `RequireAuditContext` throws at construction.
- **Concurrency:** parallel requests with different contexts don't bleed.

> **Open for sign-off (see check-in):** the SQL Server / PostgreSQL integration tests require adding a small
> RLS demo table + security policy to `Database/**/integration_test_db.sql` and the CI containers. Confirm
> scope before authoring.

---

## 10. Decisions (resolved)

- **Fail-closed boundary — DECIDED (2026-06-29):** `RequireAuditContext` is set **per provider** via the
  app's ORM factory (strict providers for PHI repositories, lenient for non-PHI/unauthenticated paths).
  Priming is opportunistic in both; only the throw differs. FunkyORM's internal bootstrap queries run under
  a system context exempt from fail-closed (§7). No per-call opt-out / ambient suppress in v1.
- Remaining items are implementation-time validations, not design decisions: Npgsql custom-GUC reset on
  pool return; MySQL session-var reset; async-prime ergonomics.

---

## 11. Phasing

| Phase | Scope | Notes |
|:--|:--|:--|
| 0 | Core types (`SessionContextEntry`, `FunkyAuditContext`, `IAuditContextAccessor`, `AuditContextOptions`), comment builder + validation, `SystemContextScope`, unit tests | No DB |
| 1 | `ConnectionScope` prime hook: owned + transactional lifetime tracking, fail-closed, system bypass | |
| 2 | SQL Server primer + `BuildSqlCommandObject` comment; integration tests incl. RLS demo | Reference impl |
| 3 | PostgreSQL primer (session / tx-local) + tests | |
| 4 | MySQL attribution-only primer + tests; SQLite no-op + construction guard | |
| 5 | Docs (this plan → completed; runbook; README summary), changelog, version bump to 3.8.0 | |
