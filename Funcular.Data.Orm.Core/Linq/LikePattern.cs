using System.Text;

namespace Funcular.Data.Orm.Linq
{
    /// <summary>
    /// The escaping of a value that string <c>Contains</c>, <c>StartsWith</c> and <c>EndsWith</c> match literally in a
    /// <c>LIKE</c> pattern. Every provider sends the escaped value followed by <see cref="EscapeClause"/>, so one escape
    /// character serves all four dialects; naming it overrides the backslash PostgreSQL and MySQL escape with by default.
    /// </summary>
    public static class LikePattern
    {
        /// <summary>The escape character every provider names in <see cref="EscapeClause"/>.</summary>
        public static readonly char EscapeCharacter = '!';

        /// <summary>The clause each provider appends after an escaped pattern: <c> ESCAPE '!'</c>.</summary>
        public static readonly string EscapeClause = " ESCAPE '" + EscapeCharacter + "'";

        /// <summary>
        /// Returns <paramref name="value"/> with each <c>LIKE</c> metacharacter (<c>%</c>, <c>_</c>, and SQL Server's
        /// <c>[</c>) and the escape character itself prefixed with <see cref="EscapeCharacter"/>; null for null.
        /// </summary>
        public static string? Escape(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            var builder = new StringBuilder(value!.Length + 4);
            foreach (var c in value)
            {
                if (c == EscapeCharacter || c == '%' || c == '_' || c == '[')
                    builder.Append(EscapeCharacter);
                builder.Append(c);
            }
            return builder.ToString();
        }
    }
}
