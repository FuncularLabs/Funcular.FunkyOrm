using System;

namespace Funcular.Data.Orm
{
    /// <summary>
    /// A single named session-context key/value primed onto the database connection for the duration of a
    /// command's connection. The <see cref="Value"/> is treated as an opaque string — FunkyORM does not
    /// interpret it. The application's Row-Level Security predicate / audit logic consumes it.
    /// </summary>
    public sealed class SessionContextEntry
    {
        /// <summary>The context key (e.g. an application-defined name read back by an RLS predicate).</summary>
        public string Key { get; }

        /// <summary>The opaque value primed for <see cref="Key"/>.</summary>
        public string Value { get; }

        /// <summary>
        /// When true (the default), the key is set immutable for the connection's session lifetime where the
        /// provider supports it (SQL Server <c>@read_only=1</c>). Emulated or ignored on other providers.
        /// </summary>
        public bool ReadOnly { get; }

        /// <summary>Initializes a session-context entry.</summary>
        public SessionContextEntry(string key, string value, bool readOnly = true)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Value = value ?? throw new ArgumentNullException(nameof(value));
            ReadOnly = readOnly;
        }
    }
}
