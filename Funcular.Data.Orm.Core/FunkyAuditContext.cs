using System;
using System.Collections.Generic;

namespace Funcular.Data.Orm
{
    /// <summary>
    /// The application's per-request context that FunkyORM primes onto each connection it uses. All members
    /// are optional; an empty context primes nothing. Built by the application (typically in auth middleware)
    /// and surfaced to FunkyORM through an <see cref="IAuditContextAccessor"/>.
    /// </summary>
    public sealed class FunkyAuditContext
    {
        /// <summary>
        /// Caller-defined session-context keys primed onto the connection. Consumed by the application's RLS
        /// predicate and/or audit logic. FunkyORM is agnostic about their names and meaning.
        /// </summary>
        public IReadOnlyList<SessionContextEntry> Entries { get; set; } = Array.Empty<SessionContextEntry>();

        /// <summary>
        /// Optional opaque identifier embedded in the self-attributing audit comment (e.g. a user object id).
        /// MUST NOT contain PII (email/UPN/name). Validated to a safe character set when the comment is built.
        /// </summary>
        public string? AuditSubjectId { get; set; }

        /// <summary>
        /// Optional opaque correlation identifier embedded in the audit comment (e.g. a request id). MUST NOT
        /// contain PII. Validated to a safe character set when the comment is built.
        /// </summary>
        public string? AuditCorrelationId { get; set; }
    }
}
