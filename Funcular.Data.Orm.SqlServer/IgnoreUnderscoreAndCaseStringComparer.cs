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
    }
}