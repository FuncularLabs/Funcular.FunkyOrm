using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Project
{
    /// <summary>
    /// Detail class demonstrating [JsonPath] attribute usage on PostgreSQL.
    /// Extracts scalar values from the project.metadata JSON column.
    /// </summary>
    [Table("project")]
    public class ProjectScorecard : ProjectEntity
    {
        [JsonPath("metadata", "$.priority")]
        public string Priority { get; set; }

        [JsonPath("metadata", "$.client.name")]
        public string ClientName { get; set; }

        [JsonPath("metadata", "$.client.region")]
        public string ClientRegion { get; set; }

        [JsonPath("metadata", "$.risk_level", SqlType = "int")]
        public int? RiskLevel { get; set; }
    }
}
