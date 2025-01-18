using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Funcular.Data.Orm.SqlServer
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

        public static string ToDictionaryKey(this PropertyInfo? propertyInfo)
        {
            if(propertyInfo == null)
                throw new ArgumentNullException(nameof(propertyInfo)); 
            return propertyInfo.DeclaringType?.Name + propertyInfo.Name;
        }
    }
}
