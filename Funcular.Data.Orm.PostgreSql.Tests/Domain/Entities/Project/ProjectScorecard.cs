using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Project
{
    /// <summary>
    /// Detail class demonstrating <c>[JsonPath]</c> attribute usage on PostgreSQL.
    /// Extracts scalar values from the <c>project.metadata</c> JSON column.
    /// <para>
    /// <b>Table Name Resolution:</b> No <c>[Table]</c> attribute is needed. This class inherits
    /// <c>[Table("project")]</c> from <see cref="ProjectEntity"/> via <c>inherit: true</c>.
    /// </para>
    /// </summary>
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
