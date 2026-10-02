using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Funcular.Data.Orm
{
    /// <summary>
    /// One set of identifier caches (docs/plans/PROVIDER_SCOPED_CACHES_PLAN.md, D4): table names, column names, the
    /// unmapped-property set, the mapped-type set, procedure names and entity mappers. A provider instance reads the
    /// set of its scope (D1): its provider runtime type, its dialect runtime type and its connection identity.
    /// </summary>
    internal sealed class CacheScope
    {
        internal CacheScope(CacheScopeKey? key)
        {
            Key = key;
        }

        /// <summary>The registry key of this scope, or null for a per-instance scope the registry never holds (D3).</summary>
        internal CacheScopeKey? Key { get; }

        /// <summary>Entity type → resolved table name, as the provider's resolver returns it.</summary>
        internal ConcurrentDictionary<Type, string> TableNames { get; } = new ConcurrentDictionary<Type, string>();

        /// <summary>
        /// Property key (<see cref="GeneralExtensions.ToDictionaryKey"/>, the declaring type's full name and the property
        /// name) → column name. Ordinal (D5).
        /// </summary>
        internal ConcurrentDictionary<string, string> ColumnNames { get; } =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Entity type → the properties that have no column.</summary>
        internal ConcurrentDictionary<Type, ICollection<PropertyInfo>> UnmappedProperties { get; } =
            new ConcurrentDictionary<Type, ICollection<PropertyInfo>>();

        /// <summary>The types whose columns have been discovered. A concurrent set (D4).</summary>
        internal ICollection<Type> MappedTypes { get; } = new ConcurrentTypeSet();

        /// <summary>Entity type → resolved stored procedure name (SQL Server, MySQL).</summary>
        internal ConcurrentDictionary<Type, string> ProcedureNames { get; } = new ConcurrentDictionary<Type, string>();

        /// <summary>Entity type and result-set signature → compiled reader-to-entity mapper.</summary>
        internal ConcurrentDictionary<string, Delegate> EntityMappers { get; } = new ConcurrentDictionary<string, Delegate>();
    }

    /// <summary>A thread-safe set of types, for <see cref="CacheScope.MappedTypes"/>.</summary>
    internal sealed class ConcurrentTypeSet : ICollection<Type>
    {
        private readonly ConcurrentDictionary<Type, byte> _types = new ConcurrentDictionary<Type, byte>();

        public int Count => _types.Count;

        public bool IsReadOnly => false;

        public void Add(Type item) => _types.TryAdd(item, 0);

        public void Clear() => _types.Clear();

        public bool Contains(Type item) => item != null && _types.ContainsKey(item);

        public void CopyTo(Type[] array, int arrayIndex) => _types.Keys.ToArray().CopyTo(array, arrayIndex);

        public bool Remove(Type item) => item != null && _types.TryRemove(item, out _);

        public IEnumerator<Type> GetEnumerator() => _types.Keys.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
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

        /// <summary>The SHA-256 of the connection identity's UTF-8 bytes, as lowercase hexadecimal.</summary>
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
    /// Maps a scope key to its cache set for the life of the process, with no eviction (D1, D9): one scope per
    /// (provider runtime type, dialect runtime type, connection identity).
    /// </summary>
    internal static class CacheScopeRegistry
    {
        private static readonly ConcurrentDictionary<CacheScopeKey, CacheScope> _scopes =
            new ConcurrentDictionary<CacheScopeKey, CacheScope>();

        /// <summary>
        /// Returns the cache set for a provider instance's scope (D1, D3, D8). A null identity gets a new per-instance set
        /// that is never registered; any other identity, empty included, gets the registered set for its key, so
        /// resolution is idempotent and thread-safe.
        /// </summary>
        /// <param name="providerType">The provider's runtime type.</param>
        /// <param name="dialectType">The runtime type of the provider's dialect.</param>
        /// <param name="identity">The connection identity: null for a per-instance scope, empty for one scope per
        /// (provider type, dialect type). Only its SHA-256 is kept (D2).</param>
        internal static CacheScope GetOrAdd(Type providerType, Type? dialectType, string? identity)
        {
            if (providerType == null) throw new ArgumentNullException(nameof(providerType));
            if (identity == null)
                return new CacheScope(null);
            var key = new CacheScopeKey(providerType, dialectType, HashIdentity(identity));
            return _scopes.GetOrAdd(key, k => new CacheScope(k));
        }

        /// <summary>The keys of the registered scopes.</summary>
        internal static IReadOnlyCollection<CacheScopeKey> Keys => _scopes.Keys.ToArray();

        /// <summary>The SHA-256 of <paramref name="identity"/>'s UTF-8 bytes, as lowercase hexadecimal (D2).</summary>
        internal static string HashIdentity(string identity)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(identity ?? string.Empty));
                var hex = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes)
                    hex.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }
    }
}
