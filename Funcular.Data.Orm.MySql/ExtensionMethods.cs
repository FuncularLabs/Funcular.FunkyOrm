using MySqlConnector;
using System;
using System.Collections.Generic;

namespace Funcular.Data.Orm.MySql
{
    public static class ExtensionMethods
    {
        public static IEnumerable<MySqlParameter> AsEnumerable(this MySqlParameterCollection parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters), "The MySqlParameterCollection cannot be null.");

            foreach (MySqlParameter parameter in parameters)
            {
                yield return parameter;
            }
        }
    }
}
