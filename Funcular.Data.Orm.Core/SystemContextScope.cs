using System;
using System.Threading;

namespace Funcular.Data.Orm
{
    /// <summary>
    /// Marks an internal/system operation (schema discovery, table-name resolution) so that audit-context
    /// priming and fail-closed enforcement are bypassed for the duration. Such queries are not PHI and must
    /// not be blocked by <c>RequireAuditContext</c> at application warmup. Used as
    /// <c>using (SystemContextScope.Enter()) { ... }</c>; supports nesting.
    /// </summary>
    public sealed class SystemContextScope : IDisposable
    {
        private static readonly AsyncLocal<int> _depth = new AsyncLocal<int>();

        /// <summary>True when execution is inside a system-context scope.</summary>
        public static bool IsActive => _depth.Value > 0;

        private bool _disposed;

        private SystemContextScope() { }

        /// <summary>Enters a system-context scope. Dispose to exit.</summary>
        public static SystemContextScope Enter()
        {
            _depth.Value++;
            return new SystemContextScope();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _depth.Value--;
        }
    }
}
