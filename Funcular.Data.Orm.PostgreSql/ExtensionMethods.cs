using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Funcular.Data.Orm.PostgreSql
{
    public static class ExtensionMethods
    {
        public static IEnumerable<NpgsqlParameter> AsEnumerable(this NpgsqlParameterCollection parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters), "The NpgsqlParameterCollection cannot be null.");

            foreach (NpgsqlParameter parameter in parameters)
            {
                yield return parameter;
            }
        }
    }
}
