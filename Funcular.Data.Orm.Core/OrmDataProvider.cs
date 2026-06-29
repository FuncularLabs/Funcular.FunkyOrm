using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Globalization;
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

        /// <summary>
        /// Per-provider session-context / audit configuration. Disabled by default (no accessor). Typically
        /// set by the application's ORM factory. See docs/guides/AUDIT_CONTEXT_RUNBOOK.md.
        /// </summary>
        public AuditContextOptions AuditContext { get; set; } = new AuditContextOptions();

        #endregion

        #region Audit / Session Context

        /// <summary>
        /// Resolves the ambient audit context to prime onto a connection, or returns null to prime nothing.
        /// Respects <see cref="SystemContextScope"/> (internal/bootstrap operations prime nothing and never
        /// fail-closed). Throws when <see cref="AuditContextOptions.RequireAuditContext"/> is set and no
        /// context is present. Called by each provider's connection-priming path.
        /// </summary>
        protected internal FunkyAuditContext? ResolveAuditContextForPriming()
        {
            var options = AuditContext;
            if (options == null || !options.Enabled) return null;
            if (SystemContextScope.IsActive) return null;

            var context = options.Accessor!.Current;
            if (context == null)
            {
                if (options.RequireAuditContext)
                    throw new InvalidOperationException(
                        "An audit context is required for this provider (RequireAuditContext = true) but none " +
                        "is present. Ensure the request is authenticated and the audit context is set before " +
                        "data access, or use a provider configured without RequireAuditContext for " +
                        "unauthenticated/non-PHI paths.");
                return null;
            }
            return context;
        }

        /// <summary>
        /// Returns the self-attributing audit comment to prepend to a text command, or null when none applies
        /// (feature disabled, comment disabled, system scope, no context, or no identifiers). The returned
        /// string has no trailing newline.
        /// </summary>
        protected internal string? GetAuditCommentPrefix()
        {
            var options = AuditContext;
            if (options == null || !options.Enabled || !options.EmitAuditComment) return null;
            if (SystemContextScope.IsActive) return null;

            var context = options.Accessor!.Current;
            if (context == null) return null;
            return AuditComment.Build(context);
        }

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

        #region Stored Procedure Support (virtual throwing defaults + shared helpers)

        // Default implementations throw NotSupportedException. Providers that support stored procedures
        // (SQL Server and MySQL fully; PostgreSQL partially) override these. SQLite inherits the throwing
        // defaults, which is its documented behavior.

        /// <inheritdoc />
        public virtual ICollection<T> ExecProcedure<T>(object? parameters = null) where T : class, new()
            => throw NewProcedureNotSupportedException();

        /// <inheritdoc />
        public virtual ICollection<T> ExecProcedure<T>(string procedureName, object? parameters = null) where T : class, new()
            => throw NewProcedureNotSupportedException();

        /// <inheritdoc />
        public virtual ICollection<T> ExecProcedure<T>(string procedureName, params SqlParam[] parameters) where T : class, new()
            => throw NewProcedureNotSupportedException();

        /// <inheritdoc />
        public virtual TResult ExecScalar<TResult>(string procedureName, object? parameters = null)
            => throw NewProcedureNotSupportedException();

        /// <inheritdoc />
        public virtual TResult ExecScalar<TResult>(string procedureName, params SqlParam[] parameters)
            => throw NewProcedureNotSupportedException();

        /// <inheritdoc />
        public virtual int ExecNonQuery(string procedureName, object? parameters = null)
            => throw NewProcedureNotSupportedException();

        /// <inheritdoc />
        public virtual int ExecNonQuery(string procedureName, params SqlParam[] parameters)
            => throw NewProcedureNotSupportedException();

        /// <inheritdoc />
        public virtual Task<ICollection<T>> ExecProcedureAsync<T>(object? parameters = null) where T : class, new()
            => throw NewProcedureNotSupportedException();

        /// <inheritdoc />
        public virtual Task<ICollection<T>> ExecProcedureAsync<T>(string procedureName, object? parameters = null) where T : class, new()
            => throw NewProcedureNotSupportedException();

        /// <inheritdoc />
        public virtual Task<ICollection<T>> ExecProcedureAsync<T>(string procedureName, params SqlParam[] parameters) where T : class, new()
            => throw NewProcedureNotSupportedException();

        /// <inheritdoc />
        public virtual Task<TResult> ExecScalarAsync<TResult>(string procedureName, object? parameters = null)
            => throw NewProcedureNotSupportedException();

        /// <inheritdoc />
        public virtual Task<TResult> ExecScalarAsync<TResult>(string procedureName, params SqlParam[] parameters)
            => throw NewProcedureNotSupportedException();

        /// <inheritdoc />
        public virtual Task<int> ExecNonQueryAsync(string procedureName, object? parameters = null)
            => throw NewProcedureNotSupportedException();

        /// <inheritdoc />
        public virtual Task<int> ExecNonQueryAsync(string procedureName, params SqlParam[] parameters)
            => throw NewProcedureNotSupportedException();

        /// <summary>
        /// Builds the standard "not supported" exception for stored procedure operations on providers
        /// (or specific operations) that do not support them.
        /// </summary>
        protected NotSupportedException NewProcedureNotSupportedException() =>
            new NotSupportedException(
                $"{GetType().Name} does not support this stored procedure operation. " +
                "SQL Server and MySQL support stored procedures fully; PostgreSQL supports " +
                "ExecNonQuery/ExecScalar via CALL (use a FUNCTION RETURNS TABLE for result sets); " +
                "SQLite has no stored procedures.");

        /// <summary>
        /// A provider-agnostic normalized stored-procedure parameter produced by
        /// <see cref="NormalizeParameters(object)"/> / <see cref="NormalizeParameters(SqlParam[])"/>.
        /// Each provider converts these to its native parameter type and, after execution,
        /// back-populates <see cref="Source"/> for output / input-output directions.
        /// </summary>
        protected sealed class NormalizedParameter
        {
            /// <summary>Canonical parameter name with any leading <c>@</c> stripped; providers add their own prefix.</summary>
            public string Name { get; set; } = string.Empty;
            public object? Value { get; set; }
            public ParameterDirection Direction { get; set; } = ParameterDirection.Input;
            public DbType? DbType { get; set; }
            public int? Size { get; set; }
            /// <summary>The originating <see cref="SqlParam"/> (when applicable), for output back-population.</summary>
            public SqlParam? Source { get; set; }
        }

        /// <summary>
        /// Normalizes an anonymous/typed parameters object (input-only) into a provider-agnostic list.
        /// Reflects over public readable instance properties, one parameter each; null values become
        /// <see cref="DBNull"/>. A bare string, or a <see cref="SqlParam"/>/<see cref="SqlParam"/> sequence
        /// passed through the object overload, are handled explicitly rather than reflected over.
        /// </summary>
        protected static IReadOnlyList<NormalizedParameter> NormalizeParameters(object? parameters)
        {
            if (parameters == null)
                return Array.Empty<NormalizedParameter>();

            if (parameters is string)
                throw new ArgumentException(
                    "A string was passed as the parameters object; did you mean the (procedureName, parameters) overload?",
                    nameof(parameters));

            if (parameters is SqlParam single)
                return NormalizeParameters(new[] { single });

            if (parameters is IEnumerable<SqlParam> sequence)
                return NormalizeParameters(sequence.ToArray());

            var list = new List<NormalizedParameter>();
            foreach (var prop in parameters.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                    continue;
                list.Add(new NormalizedParameter
                {
                    Name = StripParameterPrefix(prop.Name),
                    Value = prop.GetValue(parameters) ?? (object)DBNull.Value,
                    Direction = ParameterDirection.Input
                });
            }
            return list;
        }

        /// <summary>
        /// Normalizes a <see cref="SqlParam"/> array (supports output parameters) into a provider-agnostic list.
        /// A null or empty array means "no parameters".
        /// </summary>
        protected static IReadOnlyList<NormalizedParameter> NormalizeParameters(SqlParam[]? parameters)
        {
            if (parameters == null || parameters.Length == 0)
                return Array.Empty<NormalizedParameter>();

            var list = new List<NormalizedParameter>(parameters.Length);
            foreach (var p in parameters)
            {
                list.Add(new NormalizedParameter
                {
                    Name = StripParameterPrefix(p.Name),
                    Value = p.Value ?? (object)DBNull.Value,
                    Direction = p.Direction,
                    DbType = p.DbType,
                    Size = p.Size,
                    Source = p
                });
            }
            return list;
        }

        /// <summary>Removes a single leading <c>@</c> from a parameter name, yielding a provider-neutral canonical name.</summary>
        protected static string StripParameterPrefix(string name)
            => !string.IsNullOrEmpty(name) && name[0] == '@' ? name.Substring(1) : name;

        /// <summary>
        /// Converts a scalar value returned by a stored procedure to <typeparamref name="TResult"/>, handling the
        /// cases plain <see cref="Convert.ChangeType(object, Type)"/> cannot: DBNull/null (including Nullable&lt;T&gt;),
        /// enums, and <see cref="Guid"/>.
        /// </summary>
        protected static TResult ConvertScalar<TResult>(object? value)
        {
            if (value == null || value is DBNull)
                return default!;

            var target = Nullable.GetUnderlyingType(typeof(TResult)) ?? typeof(TResult);
            if (target.IsInstanceOfType(value))
                return (TResult)value;
            if (target.IsEnum)
                return (TResult)Enum.ToObject(target, value);
            if (target == typeof(Guid))
                return (TResult)(object)Guid.Parse(value.ToString()!);
            return (TResult)Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        }

        /// <summary>Returns the explicit <c>[Procedure]</c> name for <typeparamref name="T"/>, or null when the attribute is absent.</summary>
        protected internal static string? GetProcedureNameFromAttribute<T>()
            => typeof(T).GetCustomAttribute<ProcedureAttribute>()?.Name;

        /// <summary>
        /// Normalizes a name for catalog lookup by removing underscores and lowercasing, matching the
        /// <c>REPLACE(LOWER(name), '_', '')</c> form used in the provider catalog queries.
        /// </summary>
        protected static string NormalizeProcedureNameForLookup(string name)
            => name.Replace("_", string.Empty).ToLowerInvariant();

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
                    throw new InvalidOperationException(
                        $"No primary key found for type '{t.Name}'. " +
                        $"Apply [Key] from System.ComponentModel.DataAnnotations to your primary key property, " +
                        $"or name it 'Id' or '{t.Name}Id'.");
                
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
        /// Returns true if the property is database-generated and should be excluded from INSERT and UPDATE statements.
        /// Matches properties decorated with <see cref="TimestampAttribute"/> or 
        /// <see cref="DatabaseGeneratedAttribute"/> with <see cref="DatabaseGeneratedOption.Computed"/> or <see cref="DatabaseGeneratedOption.Identity"/>.
        /// </summary>
        protected internal static bool IsDatabaseGenerated(PropertyInfo property)
        {
            if (property.GetCustomAttribute<TimestampAttribute>() != null)
                return true;

            var dbGenerated = property.GetCustomAttribute<DatabaseGeneratedAttribute>();
            if (dbGenerated != null && 
                (dbGenerated.DatabaseGeneratedOption == DatabaseGeneratedOption.Computed ||
                 dbGenerated.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity))
                return true;

            return false;
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
