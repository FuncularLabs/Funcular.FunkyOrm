using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Project
{
    /// <summary>
    /// Detail class demonstrating [JsonPath] attribute usage.
    /// Extracts scalar values from the project.metadata JSON column.
    /// Maps to the same "project" table as <see cref="ProjectEntity"/>.
    /// </summary>
    [Table("project")]
    public class ProjectScorecard : ProjectEntity
    {
        // ?? Phase 1: JSON path extraction ??

        /// <summary>
        /// Extract priority from the project metadata JSON column.
        /// JSON_VALUE(metadata, '$.priority') ? "high", "medium", "low"
        /// </summary>
        [JsonPath("metadata", "$.priority")]
        public string Priority { get; set; }

        /// <summary>
        /// Extract client name from a nested JSON object.
        /// JSON_VALUE(metadata, '$.client.name') ? "Acme Corp"
        /// </summary>
        [JsonPath("metadata", "$.client.name")]
        public string ClientName { get; set; }

        /// <summary>
        /// Extract client region from a nested JSON object.
        /// JSON_VALUE(metadata, '$.client.region') ? "NA", "EMEA", etc.
        /// </summary>
        [JsonPath("metadata", "$.client.region")]
        public string ClientRegion { get; set; }

        /// <summary>
        /// Extract risk level as a typed integer from JSON.
        /// CAST(JSON_VALUE(metadata, '$.risk_level') AS int)
        /// </summary>
        [JsonPath("metadata", "$.risk_level", SqlType = "int")]
        public int? RiskLevel { get; set; }
    }
}
