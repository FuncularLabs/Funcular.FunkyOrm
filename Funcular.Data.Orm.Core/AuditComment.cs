using System;
using System.Text.RegularExpressions;

namespace Funcular.Data.Orm
{
    /// <summary>
    /// Builds the self-attributing audit comment prepended to text commands, validating that the embedded
    /// identifiers cannot break out of the SQL comment. Identifiers must be opaque (no PII).
    /// </summary>
    internal static class AuditComment
    {
        // Conservative safe set: alphanumerics and a few id-friendly separators. Disallows whitespace, '*',
        // and '/', so the sequence "*/" cannot appear and the value cannot escape the comment.
        private static readonly Regex SafeIdentifier =
            new Regex(@"^[A-Za-z0-9._:\-]{1,128}$", RegexOptions.CultureInvariant);

        /// <summary>
        /// Returns the comment (without trailing newline), or null when neither identifier is present.
        /// Throws <see cref="InvalidOperationException"/> if a present identifier is not a safe opaque token.
        /// </summary>
        public static string? Build(FunkyAuditContext context)
        {
            var subject = context.AuditSubjectId;
            var correlation = context.AuditCorrelationId;

            var hasSubject = !string.IsNullOrEmpty(subject);
            var hasCorrelation = !string.IsNullOrEmpty(correlation);
            if (!hasSubject && !hasCorrelation)
                return null;

            if (hasSubject) Validate(subject!, nameof(FunkyAuditContext.AuditSubjectId));
            if (hasCorrelation) Validate(correlation!, nameof(FunkyAuditContext.AuditCorrelationId));

            var body = "funky:audit";
            if (hasSubject) body += " sub=" + subject;
            if (hasCorrelation) body += " corr=" + correlation;
            return "/* " + body + " */";
        }

        private static void Validate(string value, string name)
        {
            if (!SafeIdentifier.IsMatch(value))
                throw new InvalidOperationException(
                    $"{name} ('{value}') is not a safe audit identifier. Use an opaque token matching " +
                    "[A-Za-z0-9._:-] (max 128 chars) — never an email/UPN/name or any value containing spaces or '*/'.");
        }
    }
}
