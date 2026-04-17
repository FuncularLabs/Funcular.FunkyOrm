using System;

namespace Funcular.Data.Orm.Attributes
{
    /// <summary>
    /// Decorates a <c>string</c> property to indicate its value is a JSON array projected from
    /// child records via a correlated subquery. Generates <c>FOR JSON PATH</c> (SQL Server)
    /// or <c>json_agg(row_to_json(...))</c> (PostgreSQL).
    /// </summary>
    /// <example>
    /// <code>
    /// [JsonCollection(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
    ///     Columns = new[] { "Title", "Status", "DueDate", "CompletedDate" },
    ///     OrderBy = "DueDate")]
    /// public string MilestonesJson { get; set; }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class JsonCollectionAttribute : Attribute
    {
        /// <summary>
        /// Gets the child entity type to project (e.g., <c>typeof(ProjectMilestoneEntity)</c>).
        /// </summary>
        public Type SourceType { get; }

        /// <summary>
        /// Gets the property name on the child entity that references the parent's primary key
        /// (e.g., <c>nameof(ProjectMilestoneEntity.ProjectId)</c>).
        /// </summary>
        public string ForeignKey { get; }

        /// <summary>
        /// Gets or sets the property names on the child entity to include in the JSON objects.
        /// These are resolved to column names at query time using the same naming conventions.
        /// </summary>
        public string[] Columns { get; set; }

        /// <summary>
        /// Gets or sets the property name to order the results by.
        /// Resolved to a column name at query time.
        /// </summary>
        public string OrderBy { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="JsonCollectionAttribute"/> class.
        /// </summary>
        /// <param name="sourceType">The child entity type.</param>
        /// <param name="foreignKey">The FK property name on the child referencing the parent PK.</param>
        public JsonCollectionAttribute(Type sourceType, string foreignKey)
        {
            SourceType = sourceType ?? throw new ArgumentNullException(nameof(sourceType));
            ForeignKey = foreignKey ?? throw new ArgumentNullException(nameof(foreignKey));
        }
    }
}
