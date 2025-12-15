using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Funcular.Data.Orm.SqlServer
{
    public static class ExtensionMethods
    {
        /// <summary>
        /// Converts a <see cref="SqlParameterCollection"/> to an enumerable sequence of <see cref="SqlParameter"/>.
        /// This method allows flexible conversion to various collection types through LINQ operations.
        /// </summary>
        /// <param name="parameters">The source <see cref="SqlParameterCollection"/> to convert.</param>
        /// <returns>An <see cref="IEnumerable{SqlParameter}"/> containing all parameters from the collection.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="parameters"/> is null.</exception>
        /// <example>
        /// <code>
        /// SqlCommand command = new SqlCommand();
        /// command.Parameters.AddWithValue("@Id", 1);
        /// command.Parameters.AddWithValue("@Name", "John");
        /// 
        /// // As IEnumerable
        /// IEnumerable&lt;SqlParameter&gt; enumerableParams = command.Parameters.AsEnumerable();
        /// 
        /// // As Array
        /// SqlParameter[] arrayParams = command.Parameters.AsEnumerable().ToArray();
        /// 
        /// // As List
        /// List&lt;SqlParameter&gt; listParams = command.Parameters.AsEnumerable().ToList();
        /// </code>
        /// </example>
        public static IEnumerable<SqlParameter> AsEnumerable(this SqlParameterCollection parameters)
        {
            // Validate input to prevent NullReferenceException
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters), "The SqlParameterCollection cannot be null.");

            // Use yield return for lazy evaluation and efficient memory usage
            foreach (SqlParameter parameter in parameters)
            {
                yield return parameter;
            }
        }
    }
}
