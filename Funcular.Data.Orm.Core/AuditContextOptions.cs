namespace Funcular.Data.Orm
{
    /// <summary>
    /// Per-provider configuration for session-context priming and audit attribution. Attach to a provider the
    /// same way <c>Log</c> is set; the application's ORM factory typically stamps it onto each provider it
    /// creates (strict for PHI repositories, lenient for non-PHI / unauthenticated paths).
    /// </summary>
    public sealed class AuditContextOptions
    {
        /// <summary>The accessor supplying the ambient context. When null, the feature is disabled for this provider.</summary>
        public IAuditContextAccessor? Accessor { get; set; }

        /// <summary>
        /// When true, the provider throws if a command would run with no ambient context (fail-closed). Use on
        /// PHI providers. When false, a missing context simply primes nothing (opportunistic).
        /// </summary>
        public bool RequireAuditContext { get; set; }

        /// <summary>When true (default), a self-attributing <c>/* funky:audit ... */</c> comment is prepended to text commands.</summary>
        public bool EmitAuditComment { get; set; } = true;

        /// <summary>True when an accessor is configured.</summary>
        public bool Enabled => Accessor != null;
    }
}
