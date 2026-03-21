using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Funcular.Data.Orm.PostgreSql.Visitors
{
    /// <summary>
    /// Abstract base class for expression visitors that translate LINQ expressions into SQL components.
    /// </summary>
    /// <typeparam name="T">The type of entity being queried.</typeparam>
    public abstract class BaseExpressionVisitor<T> where T : class, new()
    {
        protected readonly ConcurrentDictionary<string, string> _columnNames;
        protected readonly ICollection<PropertyInfo> _unmappedProperties;

        protected BaseExpressionVisitor(
            ConcurrentDictionary<string, string> columnNames,
            ICollection<PropertyInfo> unmappedProperties)
        {
            _columnNames = columnNames ?? throw new ArgumentNullException(nameof(columnNames));
            _unmappedProperties = unmappedProperties;
        }

        public abstract void Visit(Expression expression);

        protected string GetColumnName(PropertyInfo property)
        {
            if (property == null)
                throw new ArgumentNullException(nameof(property));

            return _columnNames.GetOrAdd(
                property.ToDictionaryKey(),
                _ => property.GetCustomAttribute<ColumnAttribute>()?.Name ?? property.Name.ToLower());
        }

        protected bool IsUnmappedProperty(PropertyInfo property)
        {
            return _unmappedProperties.Any(p => p.Name == property.Name);
        }
    }
}
