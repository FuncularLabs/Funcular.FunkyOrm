namespace Funcular.Data.Orm
{
    /// <summary>
    /// Supplies the ambient <see cref="FunkyAuditContext"/> for the current logical operation. Implemented by
    /// the application — typically over an <c>AsyncLocal&lt;FunkyAuditContext&gt;</c> set by auth middleware.
    /// Returns <c>null</c> when no context is established (e.g. an unauthenticated request).
    /// </summary>
    public interface IAuditContextAccessor
    {
        /// <summary>The current ambient audit context, or <c>null</c> when none is established.</summary>
        FunkyAuditContext? Current { get; }
    }
}
