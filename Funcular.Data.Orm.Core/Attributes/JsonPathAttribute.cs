using System;

namespace Funcular.Data.Orm.Attributes
{
    /// <summary>
    /// Decorates a property to indicate its value is extracted from a JSON column on the same table
    /// using a JSON path expression (e.g., <c>$.client.name</c>).
    /// <para>
    /// The ORM will generate the appropriate JSON extraction SQL for the current database dialect:
    /// <c>JSON_VALUE(column, '$.path')</c> for SQL Server, or <c>column #&gt;&gt; '{path}'</c> for PostgreSQL.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// [JsonPath("metadata", "$.client.name")]
    /// public string ClientName { get; set; }
    ///
    /// [JsonPath("metadata", "$.risk_level", SqlType = "int")]
    /// public int? RiskLevel { get; set; }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class JsonPathAttribute : Attribute
    {
        /// <summary>
        /// Gets the name of the JSON column on the entity's table.
        /// </summary>
        public string ColumnName { get; }

        /// <summary>
        /// Gets the JSON path expression (e.g., <c>$.client.name</c>).
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets or sets an optional SQL type to cast the extracted value to (e.g., <c>"int"</c>, <c>"decimal(10,2)"</c>).
        /// When set, the generated SQL wraps the extraction in a CAST (SQL Server) or <c>::type</c> (PostgreSQL).
        /// When null, the value is returned as the database's default string type.
        /// </summary>
        public string SqlType { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="JsonPathAttribute"/> class.
        /// </summary>
        /// <param name="columnName">The name of the JSON column on the entity's table.</param>
        /// <param name="path">The JSON path expression (e.g., <c>$.client.name</c>).</param>
        public JsonPathAttribute(string columnName, string path)
        {
            ColumnName = columnName ?? throw new ArgumentNullException(nameof(columnName));
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }
    }
}
