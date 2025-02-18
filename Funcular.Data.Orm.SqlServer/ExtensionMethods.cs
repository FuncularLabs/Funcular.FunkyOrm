﻿using System;
using System.Collections.Generic;
using System.Reflection;

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
        /// Converts to a dictionary key, using the type name of the object and the name of the property.
        /// </summary>
        /// <param name="propertyInfo">The property information.</param>
        /// <returns>System.String.</returns>
        /// <exception cref="System.ArgumentNullException">propertyInfo</exception>
        internal static string ToDictionaryKey(this PropertyInfo? propertyInfo)
        {
            if(propertyInfo == null)
                throw new ArgumentNullException(nameof(propertyInfo)); 
            return propertyInfo.DeclaringType?.Name + propertyInfo.Name;
        }


        /// <summary>
        /// Ensures that the input string starts with the specified prefix.
        /// </summary>
        /// <param name="value">The string to check and potentially modify.</param>
        /// <param name="prefix">The prefix to ensure the string starts with.</param>
        /// <returns>A new string that starts with the given prefix if it did not already.</returns>
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
        public static string EnsureEndsWith(this string value, string suffix)
        {
            return value.EndsWith(suffix) ? value : $"{value}{suffix}";
        }
    }
}
