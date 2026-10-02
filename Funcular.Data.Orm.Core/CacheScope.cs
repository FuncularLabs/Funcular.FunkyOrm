using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace Funcular.Data.Orm
{
    /// <summary>
    /// One set of identifier caches (docs/plans/PROVIDER_SCOPED_CACHES_PLAN.md, D4): table names, column names, the
    /// unmapped-property set, the mapped-type set, procedure names and entity mappers. A provider instance reads the
    /// set of its scope (D1): its provider runtime type, its dialect runtime type and its connection identity.
    /// </summary>
    /// <remarks>
    /// SEAM (Task 1a): every provider instance still resolves to <see cref="CacheScopeRegistry.ProcessWide"/>, the one
    /// set that 3.9.0's statics are, so nothing is scoped yet. Task 2 makes the registry hand out real scopes.
    /// </remarks>
    internal sealed class CacheScope
    {
        internal CacheScope(CacheScopeKey? key, ICollection<Type> mappedTypes)
        {
            Key = key;
            MappedTypes = mappedTypes ?? throw new ArgumentNullException(nameof(mappedTypes));
        }

        /// <summary>The registry key of this scope, or null for a scope the registry doesn't hold.</summary>
        internal CacheScopeKey? Key { get; }

        /// <summary>Entity type → resolved, dialect-enclosed table name.</summary>
        internal ConcurrentDictionary<Type, string> TableNames { get; } = new ConcurrentDictionary<Type, string>();

        /// <summary>Property key → column name. The comparer ignores underscores and case (3.9.0 behaviour).</summary>
        internal ConcurrentDictionary<string, string> ColumnNames { get; } =
            new ConcurrentDictionary<string, string>(new IgnoreUnderscoreAndCaseStringComparer());

        /// <summary>Entity type → the properties that have no column.</summary>
        internal ConcurrentDictionary<Type, ICollection<PropertyInfo>> UnmappedProperties { get; } =
            new ConcurrentDictionary<Type, ICollection<PropertyInfo>>();

        /// <summary>The types whose columns have been discovered.</summary>
        internal ICollection<Type> MappedTypes { get; }

        /// <summary>Entity type → resolved stored procedure name (SQL Server, MySQL).</summary>
        internal ConcurrentDictionary<Type, string> ProcedureNames { get; } = new ConcurrentDictionary<Type, string>();

        /// <summary>Entity type and result-set signature → compiled reader-to-entity mapper.</summary>
        internal ConcurrentDictionary<string, Delegate> EntityMappers { get; } = new ConcurrentDictionary<string, Delegate>();
    }

    /// <summary>
    /// The registry key of a scope (D1, D2): the provider runtime type, the dialect runtime type and the SHA-256 of the
    /// connection identity. The identity itself is never kept.
    /// </summary>
    internal readonly struct CacheScopeKey : IEquatable<CacheScopeKey>
    {
        internal CacheScopeKey(Type providerType, Type? dialectType, string identityHash)
        {
            ProviderType = providerType ?? throw new ArgumentNullException(nameof(providerType));
            DialectType = dialectType;
            IdentityHash = identityHash ?? throw new ArgumentNullException(nameof(identityHash));
        }

        internal Type ProviderType { get; }

        internal Type? DialectType { get; }

        /// <summary>The SHA-256 of the connection identity, as hexadecimal.</summary>
        internal string IdentityHash { get; }

        public bool Equals(CacheScopeKey other) =>
            ProviderType == other.ProviderType && DialectType == other.DialectType &&
            string.Equals(IdentityHash, other.IdentityHash, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is CacheScopeKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ProviderType.GetHashCode();
                hash = (hash * 397) ^ (DialectType?.GetHashCode() ?? 0);
                return (hash * 397) ^ StringComparer.Ordinal.GetHashCode(IdentityHash);
            }
        }

        public override string ToString() => $"{ProviderType.FullName}|{DialectType?.FullName}|{IdentityHash}";
    }

    /// <summary>
    /// Maps a scope key to its cache set for the life of the process (D1, D9).
    /// </summary>
    internal static class CacheScopeRegistry
    {
        /// <summary>
        /// SEAM (Task 1a): the mapped-type set of <see cref="ProcessWide"/>, typed as the 3.9.0
        /// <c>OrmDataProvider._mappedTypes</c> static that aliases it. Removed with that static in Task 3.
        /// </summary>
        internal static readonly HashSet<Type> ProcessWideMappedTypes = new HashSet<Type>();

        /// <summary>
        /// SEAM (Task 1a): the one process-wide set that <see cref="OrmDataProvider"/>'s 3.9.0 statics alias.
        /// Removed with those statics in Task 3.
        /// </summary>
        internal static readonly CacheScope ProcessWide = new CacheScope(null, ProcessWideMappedTypes);

        /// <summary>
        /// Returns the cache set for a provider instance's scope (D1, D3, D8).
        /// </summary>
        /// <param name="providerType">The provider's runtime type.</param>
        /// <param name="dialectType">The runtime type of the provider's dialect.</param>
        /// <param name="identity">The connection identity: null for a per-instance scope, empty for one scope per
        /// (provider type, dialect type).</param>
        /// <remarks>SEAM (Task 1a): always <see cref="ProcessWide"/>; nothing is registered yet.</remarks>
        internal static CacheScope GetOrAdd(Type providerType, Type? dialectType, string? identity)
        {
            if (providerType == null) throw new ArgumentNullException(nameof(providerType));
            return ProcessWide;
        }

        /// <summary>The keys of the registered scopes. SEAM (Task 1a): none.</summary>
        internal static IReadOnlyCollection<CacheScopeKey> Keys => Array.Empty<CacheScopeKey>();
    }
}
