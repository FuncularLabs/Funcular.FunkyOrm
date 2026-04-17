using System;

namespace Funcular.Data.Orm.Attributes
{
    /// <summary>
    /// Decorates a property to indicate its value is computed from a raw SQL expression.
    /// Supports <c>COALESCE</c>, <c>CONCAT</c>, <c>CASE</c>, and any other SQL expression.
    /// <para>
    /// Use <c>{PropertyName}</c> tokens inside the expression to reference columns. The framework
    /// resolves them to fully qualified column references (with correct naming conventions,
    /// table aliases, and identifier quoting) at query time.
    /// </para>
    /// <para>
    /// For provider-specific expressions (e.g., string concatenation differs between SQL Server and PostgreSQL),
    /// use the dual-expression constructor with <c>mssql:</c> and <c>postgresql:</c> parameters.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// // Single expression (works on both providers if SQL is portable):
    /// [SqlExpression("COALESCE({ComputedScore}, {Score})")]
    /// public int? EffectiveScore { get; set; }
    ///
    /// // Provider-specific overrides:
    /// [SqlExpression(
    ///     mssql: "CONCAT({FirstName}, ' ', {LastName})",
    ///     postgresql: "{FirstName} || ' ' || {LastName}")]
    /// public string FullName { get; set; }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class SqlExpressionAttribute : Attribute
    {
        /// <summary>
        /// Gets the SQL expression (shared across providers, or SQL Server-specific when <see cref="PostgreSql"/> is also set).
        /// Contains <c>{PropertyName}</c> tokens that are resolved to column references at query time.
        /// </summary>
        public string Expression { get; }

        /// <summary>
        /// Gets the SQL Server-specific expression, or null if using the shared <see cref="Expression"/>.
        /// </summary>
        public string MsSql { get; }

        /// <summary>
        /// Gets the PostgreSQL-specific expression, or null if using the shared <see cref="Expression"/>.
        /// </summary>
        public string PostgreSql { get; }

        /// <summary>
        /// Initializes a new instance with a single expression shared across all providers.
        /// </summary>
        /// <param name="expression">The SQL expression with <c>{PropertyName}</c> tokens.</param>
        public SqlExpressionAttribute(string expression)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        }

        /// <summary>
        /// Initializes a new instance with provider-specific expressions.
        /// </summary>
        /// <param name="mssql">The SQL Server expression.</param>
        /// <param name="postgresql">The PostgreSQL expression.</param>
        public SqlExpressionAttribute(string mssql, string postgresql)
        {
            MsSql = mssql ?? throw new ArgumentNullException(nameof(mssql));
            PostgreSql = postgresql ?? throw new ArgumentNullException(nameof(postgresql));
        }

        /// <summary>
        /// Returns the appropriate expression for the given provider name.
        /// Falls back to <see cref="Expression"/> if provider-specific expressions are not set.
        /// </summary>
        /// <param name="providerName">Either "mssql" or "postgresql" (case-insensitive).</param>
        public string GetExpression(string providerName)
        {
            if (!string.IsNullOrEmpty(Expression))
                return Expression;

            if (providerName != null && providerName.IndexOf("postgres", StringComparison.OrdinalIgnoreCase) >= 0)
                return PostgreSql ?? Expression;

            return MsSql ?? Expression;
        }
    }
}
