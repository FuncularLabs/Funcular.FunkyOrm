using System.Collections.Generic;

namespace Funcular.Data.Orm
{
    /// <summary>
    /// Class IgnoreUnderscoreAndCaseStringComparer, where
    /// ColumnName == column_name == columnName == COLUMN_NAME
    /// Implements the <see cref="IEqualityComparer`1" />
    /// </summary>
    /// <seealso cref="IEqualityComparer`1" />
    public class IgnoreUnderscoreAndCaseStringComparer : IEqualityComparer<string>
    {
        public bool Equals(string x, string y)
        {
            if (x == null && y == null)
                return true;
            if (x == null || y == null)
                return false;

            // Remove underscores and convert to lower case before comparison
            string xNormalized = x.Replace("_", "").ToLowerInvariant();
            string yNormalized = y.Replace("_", "").ToLowerInvariant();

            return xNormalized == yNormalized;
        }

        public int GetHashCode(string obj)
        {
            if (obj == null) return 0;

            // Remove underscores, convert to lower case, then get hash code
            string normalized = obj.Replace("_", "").ToLowerInvariant();
            return normalized.GetHashCode();
        }

        /// <summary>
        /// Converts a string from snake_case to PascalCase.
        /// For example, "person_address" becomes "PersonAddress".
        /// </summary>
        /// <param name="s">The input string in snake_case format.</param>
        /// <returns>The string converted to PascalCase, or null if the input is null.</returns>
        public static string ToPascalCase(string s)
        {
            if (s == null) return null;

            string[] parts = s.Split('_');
            for (int i = 0; i < parts.Length; i++)
            {
                if (!string.IsNullOrEmpty(parts[i]))
                {
                    parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1).ToLower();
                }
            }
            return string.Join("", parts);
        }

        /// <summary>
        /// Converts a string from PascalCase to lower snake_case.
        /// For example, "PersonAddress" becomes "person_address".
        /// </summary>
        /// <param name="s">The input string in PascalCase format.</param>
        /// <returns>The string converted to lower snake_case, or null if the input is null.</returns>
        public static string ToLowerSnakeCase(string s)
        {
            if (s == null) return null;

            string result = "";
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsUpper(s[i]) && i > 0)
                {
                    result += "_";
                }
                result += char.ToLower(s[i]);
            }
            return result;
        }
    }
}
