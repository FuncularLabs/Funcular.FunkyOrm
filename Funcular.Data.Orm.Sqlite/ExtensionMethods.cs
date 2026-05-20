using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace Funcular.Data.Orm.Sqlite
{
    public static class ExtensionMethods
    {
        public static IEnumerable<SqliteParameter> AsEnumerable(this SqliteParameterCollection parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters), "The SqliteParameterCollection cannot be null.");

            foreach (SqliteParameter parameter in parameters)
            {
                yield return parameter;
            }
        }
    }
}
