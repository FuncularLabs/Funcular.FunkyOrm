using Funcular.Data.Orm.Attributes;

namespace Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Project
{
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
