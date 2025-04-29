using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Funcular.Data.Orm.Visitors
{
    /// <summary>
    /// Abstract base class for expression visitors that translate LINQ expressions into SQL components.
    /// Provides common functionality for mapping properties to column names and managing metadata.
    /// </summary>
    /// <typeparam name="T">The type of entity being queried.</typeparam>
    public abstract class BaseExpressionVisitor<T> where T : class, new()
    {
        protected readonly ConcurrentDictionary<string, string> _columnNames;
        protected readonly ImmutableArray<PropertyInfo> _unmappedProperties;

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseExpressionVisitor{T}"/> class.
        /// </summary>
        /// <param name="columnNames">Cached column name mappings from property keys to SQL column names.</param>
        /// <param name="unmappedProperties">Cached unmapped properties (marked with NotMappedAttribute).</param>
        protected BaseExpressionVisitor(
            ConcurrentDictionary<string, string> columnNames,
            ImmutableArray<PropertyInfo> unmappedProperties)
        {
            _columnNames = columnNames ?? throw new ArgumentNullException(nameof(columnNames));
            _unmappedProperties = unmappedProperties;
        }

        /// <summary>
        /// Visits the specified expression and generates the corresponding SQL component.
        /// </summary>
        /// <param name="expression">The LINQ expression to visit.</param>
        public abstract void Visit(Expression expression);

        /// <summary>
        /// Gets the SQL column name for a given property, using cached metadata or custom attributes.
        /// </summary>
        /// <param name="property">The property to map to a column name.</param>
        /// <returns>The SQL column name.</returns>
        protected string GetColumnName(PropertyInfo property)
        {
            if (property == null)
                throw new ArgumentNullException(nameof(property));

            return _columnNames.GetOrAdd(
                property.ToDictionaryKey(),
                _ => property.GetCustomAttribute<ColumnAttribute>()?.Name ?? property.Name.ToLower());
        }

        /// <summary>
        /// Checks if a property is unmapped (marked with NotMappedAttribute).
        /// </summary>
        /// <param name="property">The property to check.</param>
        /// <returns>True if the property is unmapped; otherwise, false.</returns>
        protected bool IsUnmappedProperty(PropertyInfo property)
        {
            return _unmappedProperties.Any(p => p.Name == property.Name);
        }
    }
}