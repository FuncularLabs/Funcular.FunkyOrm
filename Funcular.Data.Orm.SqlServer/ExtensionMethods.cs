using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Funcular.Data.Orm
{
    public static class ExtensionMethods
    {
        /// <summary>
        /// Adds each item in <paramref name="adding"/> to <paramref name="original"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="original"></param>
        /// <param name="adding"></param>
        /// <returns></returns>
        public static ICollection<T> AddRange<T>(this ICollection<T> original, ICollection<T> adding)
        {
            foreach (var item in adding)
            {
                original.Add(item);
            }

            return original;
        }

        /// <summary>
        /// Returns true if <paramref name="s"/> contains <paramref name="other"/>.
        /// </summary>
        public static bool Contains(this string s, string other, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            return s?.IndexOf(other) > -1;
        }
#if NETSTANDARD2_0

#endif

        /// <summary>
        /// Converts to a dictionary key, using the type name of the object, a dot,
        /// and the name of the property.
        /// </summary>
        /// <param name="propertyInfo">The property information.</param>
        /// <returns>System.String.</returns>
        /// <exception cref="System.ArgumentNullException">propertyInfo</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static string ToDictionaryKey(this PropertyInfo propertyInfo)
        {
            if(propertyInfo == null)
                throw new ArgumentNullException(nameof(propertyInfo)); 
            return propertyInfo.DeclaringType?.Name + "." + propertyInfo.Name;
        }


        /// <summary>
        /// Ensures that the input string starts with the specified prefix.
        /// </summary>
        /// <param name="value">The string to check and potentially modify.</param>
        /// <param name="prefix">The prefix to ensure the string starts with.</param>
        /// <returns>A new string that starts with the given prefix if it did not already.</returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string EnsureStartsWith(this string value, string prefix)
        {
            return value.StartsWith(prefix) ? value : $"{prefix}{value}";
        }

        /// <summary>
        /// Ensures that the input string ends with the specified suffix.
        /// </summary>
        /// <param name="value">The string to check and potentially modify.</param>
        /// <param name="suffix">The suffix to ensure the string ends with.</param>
        /// <returns>A new string that ends with the given suffix if it did not already.</returns>
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string EnsureEndsWith(this string value, string suffix)
        {
            return value.EndsWith(suffix) ? value : $"{value}{suffix}";
        }

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