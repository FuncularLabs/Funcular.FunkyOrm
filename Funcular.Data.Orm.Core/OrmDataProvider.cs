using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Funcular.Data.Orm.Attributes;

namespace Funcular.Data.Orm
{
    /// <summary>
    /// Abstract base class for ORM data providers.
    /// Handles reflection caching, entity mapping discovery, and common utilities.
    /// </summary>
    public abstract class OrmDataProvider : IOrmDataProvider, IDisposable
    {
        #region Fields

        /// <summary>
        /// Cache mapping entity types to their resolved database table names.
        /// </summary>
        protected static readonly ConcurrentDictionary<Type, string> _tableNames = new ConcurrentDictionary<Type, string>();

        /// <summary>
        /// Cache mapping property dictionary keys (type + property) to actual database column names.
        /// Uses a comparer that ignores underscores and case.
        /// </summary>
        protected static readonly ConcurrentDictionary<string, string> _columnNames = new ConcurrentDictionary<string, string>(new IgnoreUnderscoreAndCaseStringComparer());

        /// <summary>
        /// Cache mapping entity types to their primary key <see cref="PropertyInfo"/>.
        /// </summary>
        protected static readonly ConcurrentDictionary<Type, PropertyInfo> _primaryKeys = new ConcurrentDictionary<Type, PropertyInfo>();

        /// <summary>
        /// Cache mapping entity types to the reflection <see cref="PropertyInfo"/> collection representing properties.
        /// </summary>
        protected static readonly ConcurrentDictionary<Type, ICollection<PropertyInfo>> _propertiesCache = new ConcurrentDictionary<Type, ICollection<PropertyInfo>>();

        /// <summary>
        /// Cache mapping entity types to properties marked with <see cref="NotMappedAttribute"/>.
        /// </summary>
        protected internal static readonly ConcurrentDictionary<Type, ICollection<PropertyInfo>> _unmappedPropertiesCache = new ConcurrentDictionary<Type, ICollection<PropertyInfo>>();

        /// <summary>
        /// Tracks which types have had their mappings discovered (to avoid repeated database schema calls).
        /// </summary>
        protected static readonly HashSet<Type> _mappedTypes = new HashSet<Type> { };

        /// <summary>
        /// Cache mapping property types to their corresponding value setters.
        /// </summary>
        protected static readonly ConcurrentDictionary<PropertyInfo, Action<object, object>> _propertySetters = new ConcurrentDictionary<PropertyInfo, Action<object, object>>();

        #endregion

        #region Properties

        /// <summary>
        /// Action used to write diagnostic or SQL log messages.
        /// Can be set by callers to hook logging (e.g. Console.WriteLine).
        /// </summary>
        public Action<string> Log { get; set; }

        #endregion

        #region Abstract Methods

        // Abstract methods for CRUD operations that specific providers must implement
        public abstract T Get<T>(dynamic key = null!) where T : class, new();
        public abstract IQueryable<T> Query<T>() where T : class, new();
        public abstract ICollection<T> Query<T>(System.Linq.Expressions.Expression<Func<T, bool>> expression) where T : class, new();
        public abstract ICollection<T> GetList<T>() where T : class, new();
        public abstract object Insert<T>(T entity) where T : class, new();
        public abstract TKey Insert<T, TKey>(T entity) where T : class, new();
        public abstract T Update<T>(T entity) where T : class, new();
        public abstract Task<T> GetAsync<T>(dynamic key = null!) where T : class, new();
        public abstract Task<ICollection<T>> QueryAsync<T>(System.Linq.Expressions.Expression<Func<T, bool>> expression) where T : class, new();
        public abstract Task<ICollection<T>> GetListAsync<T>() where T : class, new();
        public abstract Task<object> InsertAsync<T>(T entity) where T : class, new();
        public abstract Task<TKey> InsertAsync<T, TKey>(T entity) where T : class, new();
        public abstract Task<T> UpdateAsync<T>(T entity) where T : class, new();
        public abstract Task<int> DeleteAsync<T>(System.Linq.Expressions.Expression<Func<T, bool>> predicate) where T : class, new();
        public abstract int Delete<T>(System.Linq.Expressions.Expression<Func<T, bool>> predicate) where T : class, new();
        public abstract bool Delete<T>(long id) where T : class, new();
        public abstract void Dispose();

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets the table name for the specified type.
        /// </summary>
        /// <typeparam name="T">The type.</typeparam>
        /// <returns>System.String.</returns>
        protected internal virtual string GetTableName<T>()
        {
            return _tableNames.GetOrAdd(typeof(T), t =>
            {
                var tableAttribute = t.GetCustomAttribute<TableAttribute>();
                return tableAttribute != null ? tableAttribute.Name : t.Name;
            });
        }

        /// <summary>
        /// Gets the cached primary key property for the specified type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>PropertyInfo.</returns>
        protected internal PropertyInfo GetCachedPrimaryKey<T>()
        {
            return _primaryKeys.GetOrAdd(typeof(T), t =>
            {
                var props = t.GetProperties();
                var key = props.FirstOrDefault(p => p.GetCustomAttribute<KeyAttribute>() != null)
                          ?? props.FirstOrDefault(p => p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))
                          ?? props.FirstOrDefault(p => p.Name.Equals($"{t.Name}Id", StringComparison.OrdinalIgnoreCase));
                
                if (key == null)
                    throw new InvalidOperationException($"No primary key found for type {t.Name}");
                
                return key;
            });
        }

        /// <summary>
        /// Gets the cached column name for the specified property.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <returns>System.String.</returns>
        protected internal virtual string GetCachedColumnName(PropertyInfo property)
        {
            var key = $"{property.DeclaringType?.FullName}.{property.Name}";
            return _columnNames.GetOrAdd(key, k =>
            {
                var columnAttribute = property.GetCustomAttribute<ColumnAttribute>();
                return columnAttribute != null ? columnAttribute.Name : property.Name;
            });
        }

        /// <summary>
        /// Gets the unmapped properties for the specified type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>ICollection&lt;PropertyInfo&gt;.</returns>
        protected ICollection<PropertyInfo> GetUnmappedProperties<T>()
        {
            return _unmappedPropertiesCache.GetOrAdd(typeof(T), t =>
            {
                return t.GetProperties()
                    .Where(p => p.GetCustomAttribute<NotMappedAttribute>() != null)
                    .ToList();
            });
        }

        /// <summary>
        /// Gets or creates a setter delegate for the specified property.
        /// </summary>
        /// <param name="propertyInfo">The property information.</param>
        /// <returns>Action&lt;System.Object, System.Object&gt;.</returns>
        protected Action<object, object> GetOrCreateSetter(PropertyInfo propertyInfo)
        {
            return _propertySetters.GetOrAdd(propertyInfo, p =>
            {
                var target = System.Linq.Expressions.Expression.Parameter(typeof(object), "target");
                var value = System.Linq.Expressions.Expression.Parameter(typeof(object), "value");
                var castTarget = System.Linq.Expressions.Expression.Convert(target, p.DeclaringType);
                var castValue = System.Linq.Expressions.Expression.Convert(value, p.PropertyType);
                var assign = System.Linq.Expressions.Expression.Assign(
                    System.Linq.Expressions.Expression.Property(castTarget, p),
                    castValue);
                return System.Linq.Expressions.Expression.Lambda<Action<object, object>>(assign, target, value).Compile();
            });
        }

        /// <summary>
        /// Invokes the log action if it is not null.
        /// </summary>
        /// <param name="message">The message.</param>
        protected void InvokeLog(string message)
        {
            Log?.Invoke(message);
        }

        #endregion
    }
}
