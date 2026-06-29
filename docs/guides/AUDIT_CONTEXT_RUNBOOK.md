# Per-Request Session Context: Row-Level Security & Audit Attribution (v3.8.0+)

A how-to / runbook for attaching the **end-user's identity** to every database command FunkyORM sends,
even when your application authenticates to the database as a single identity (a managed identity, a
service account, a shared login). Two things become possible:

- **Row-Level Security (RLS) filtering** — your security predicate filters rows by the current user.
- **Audit attribution** — your audit trail (e.g. Azure SQL Auditing) is attributable to the human, not
  the shared identity.

> **Why FunkyORM has to do this.** Database session context is **per-connection** and is cleared when a
> connection is reset/returned to the pool. FunkyORM uses a fresh pooled connection for each
> non-transactional operation, so "set the context once per request" outside the ORM does not survive onto
> the connection your query actually runs on. FunkyORM primes the context onto the *same* connection the
> command is about to use.

---

## Capability by provider

| Provider | RLS filtering | Audit attribution | Per-key immutability |
|:--|:--|:--|:--|
| **SQL Server** | ✅ `SESSION_CONTEXT(key)` | ✅ | ✅ (`@read_only=1`) |
| **PostgreSQL** | ✅ `current_setting(key)` | ✅ | ⚠️ emulated (transaction-local) |
| **MySQL** | ❌ (no native RLS) | ✅ (session vars) | ❌ best-effort |
| **SQLite** | ❌ N/A | ❌ N/A | — |

If you point a **strict** (audit-required) provider at SQLite, FunkyORM throws at construction — there is
no isolation model to enforce, so silently doing nothing would be unsafe for a PHI configuration.

---

## The model in one paragraph

You define a `FunkyAuditContext` per request: a list of **caller-defined** `SessionContextEntry`
key/value pairs (FunkyORM treats values as opaque strings) plus two optional opaque identifiers for the
audit comment. FunkyORM primes those keys onto each connection it uses, once per connection. Your RLS
predicate (which you author) reads the keys back. **The key names and meanings are entirely yours** —
`UserId`/`TeamIds`/`RequestId` below are just an example.

---

## Quick start (SQL Server, ASP.NET Core)

### 1. Implement the accessor over your own `AsyncLocal`

```csharp
public sealed class AsyncLocalAuditContextAccessor : IAuditContextAccessor
{
    private static readonly AsyncLocal<FunkyAuditContext?> _ctx = new();
    public FunkyAuditContext? Current => _ctx.Value;
    public void Set(FunkyAuditContext ctx) => _ctx.Value = ctx;
}
```

### 2. Register it (singleton)

```csharp
services.AddSingleton<AsyncLocalAuditContextAccessor>();
services.AddSingleton<IAuditContextAccessor>(sp => sp.GetRequiredService<AsyncLocalAuditContextAccessor>());
```

### 3. Set the context per request, in middleware (authenticated routes only)

```csharp
app.Use(async (ctx, next) =>
{
    var accessor = ctx.RequestServices.GetRequiredService<AsyncLocalAuditContextAccessor>();
    if (ctx.User.Identity?.IsAuthenticated == true)
    {
        var objectId = ctx.User.FindFirst("oid")!.Value;                 // Entra object id
        var teamKeys = ctx.User.FindAll("team").Select(c => c.Value);    // immutable team keys, not names

        accessor.Set(new FunkyAuditContext
        {
            Entries = new[]
            {
                new SessionContextEntry("UserId",    objectId),                        // read_only by default
                new SessionContextEntry("TeamIds",   string.Join(",", teamKeys)),      // CSV, split with STRING_SPLIT
                new SessionContextEntry("RequestId", ctx.TraceIdentifier),
            },
            AuditSubjectId    = objectId,            // opaque id only — NO email/UPN/PII
            AuditCorrelationId = ctx.TraceIdentifier,
        });
    }
    await next();
});
```

### 4. Configure the provider (via your ORM factory)

Make **PHI** repositories use a strict provider; leave non-PHI (app settings, health checks) lenient:

```csharp
var options = new AuditContextOptions
{
    Accessor = accessor,
    RequireAuditContext = true,   // PHI: throw if no context is present
    EmitAuditComment    = true,
};
// stamp `options` onto each provider your factory creates for PHI repositories
```

### 5. Author the RLS security policy (your DDL, not FunkyORM's)

```sql
CREATE FUNCTION dbo.fn_rls_patient(@owner_id sql_variant)
RETURNS TABLE WITH SCHEMABINDING AS
RETURN
    SELECT 1 AS allowed
    WHERE CONVERT(nvarchar(64), @owner_id) = CONVERT(nvarchar(64), SESSION_CONTEXT(N'UserId'))
       OR EXISTS (
           SELECT 1 FROM STRING_SPLIT(CONVERT(nvarchar(max), SESSION_CONTEXT(N'TeamIds')), ',') t
           WHERE t.value = CONVERT(nvarchar(64), @owner_id)   -- or join to a team-membership column
       );

CREATE SECURITY POLICY dbo.patient_rls
    ADD FILTER PREDICATE dbo.fn_rls_patient(owner_id) ON dbo.patient,
    ADD BLOCK  PREDICATE dbo.fn_rls_patient(owner_id) ON dbo.patient
    WITH (STATE = ON);
```

That's it. Every FunkyORM query/insert/update/delete from a strict provider now carries `UserId`/`TeamIds`
on its connection, and the policy filters accordingly.

---

## PostgreSQL

Same `FunkyAuditContext`; FunkyORM primes via `set_config` and passes your keys through **verbatim**.

> **Key names must be dot-namespaced on PostgreSQL.** PostgreSQL requires custom settings to have a
> namespace (e.g. `app.UserId`, `myorg.user_id`). FunkyORM does **not** impose one — you choose it — and it
> throws a clear error if a key has no dot. A dotted key also works on SQL Server (which accepts any key),
> so a dotted namespace is the portable choice if you target both.

Your policy reads the keys with `current_setting`:

```sql
ALTER TABLE patient ENABLE ROW LEVEL SECURITY;
CREATE POLICY patient_isolation ON patient
    USING (owner_id = current_setting('app.UserId', true)
           OR owner_id = ANY (string_to_array(coalesce(current_setting('app.TeamIds', true), ''), ',')));
```

Notes: settings are session-scoped (`is_local=false`) and Npgsql clears them on pool return (verified by
FunkyORM's no-leak test). There is no read-only GUC, so immutability is *emulated* — rely on FunkyORM being
the only writer of these keys. PostgreSQL **superusers bypass RLS**, so author and test enforcement under a
non-superuser role.

---

## MySQL & SQLite

- **MySQL** primes session variables (`SET @UserId = …`) for **attribution** (use them in triggers / audit
  tables). MySQL has no native RLS, so it cannot *filter* — don't rely on it for isolation. Requires
  `AllowUserVariables=true` on the connection, and keys must be unqualified identifiers (`[A-Za-z0-9_]`).
  The dot-namespaced keys PostgreSQL needs are not valid MySQL variable names, so use provider-appropriate
  keys if you target both PostgreSQL and MySQL with the same workload.
- **SQLite** is a no-op (no session context or RLS). A strict (`RequireAuditContext = true`) provider
  **throws** on first use rather than silently running a PHI workload without isolation; a lenient provider
  ignores any context and works normally.

---

## Audit attribution & the self-attributing comment

For text commands, FunkyORM prepends a comment so the captured statement text is self-attributing even when
only parameters (not values) are logged:

```
/* funky:audit sub=<AuditSubjectId> corr=<AuditCorrelationId> */
SELECT ... FROM patient WHERE ...
```

- Pair with **Azure SQL Auditing** (or `pgaudit`, or MySQL's audit log) to capture attributable statements.
- **Only opaque identifiers** go in the comment. Never put email/UPN/name/PHI here — audit logs are widely
  readable. FunkyORM validates the identifiers against a safe charset and **rejects** anything that could
  break out of the comment (fails fast when you build the context).
- Stored-procedure calls (`ExecProcedure`/`ExecScalar`/`ExecNonQuery`) do not carry the comment (the call
  has no statement text to annotate); the session context is still primed on the connection.

---

## Fail-closed vs. unauthenticated requests

- `RequireAuditContext = true` → if **no** context is present when a command would run, FunkyORM throws
  *before* sending anything. Use this on PHI providers.
- `RequireAuditContext = false` → a missing context primes nothing (opportunistic). Use this for genuinely
  unauthenticated/non-PHI paths (app settings, health checks).
- **Recommended boundary:** set this per **provider** in your factory (strict providers for PHI
  repositories, lenient for the rest), rather than per call. Internal FunkyORM bootstrap queries (schema
  discovery, table-name resolution) run under a system context and are exempt from fail-closed.

---

## Security notes (read before production)

1. **Immutability is the point.** On SQL Server, keys are set `@read_only=1` so in-process code cannot
   overwrite the identity mid-session and spoof RLS. Keep your keys write-once.
2. **No PII in the audit comment** — identifiers only.
3. **RLS is yours to author and verify.** FunkyORM primes the context; it does not write or validate your
   predicates. Test the BLOCK predicate (writes), not just the FILTER (reads).
4. **Defense in depth, not sole control.** Session-context RLS assumes the app is the only writer of these
   keys on its connections. It complements, but does not replace, network/database-level controls.
5. **Connection pooling:** isolation depends on the pool resetting session state between logical opens.
   FunkyORM re-primes per connection lifetime; do not disable connection reset.

---

## Troubleshooting

| Symptom | Likely cause |
|:--|:--|
| `InvalidOperationException: audit context required` | Strict provider, no context set — request hit a PHI path without authentication, or middleware didn't run |
| RLS returns no rows for everyone | Predicate reads a key name that doesn't match the `SessionContextEntry.Key` you primed (case-sensitive) |
| "the key has already been set" | You're setting the same `read_only` key yourself outside FunkyORM on the same connection |
| Context bleeds between users | `AsyncLocal` not set per request, or connection reset disabled |
| Works for reads, not writes | RLS BLOCK predicate missing or different from the FILTER predicate |

---

*See also: the implementation plan at `docs/plans/RLS_AUDIT_CONTEXT_PLAN.md` for the internal mechanism.*
