using System;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;

namespace Funcular.Data.Orm.MySql.Tests.Domain
{
    /// <summary>Insertable mapping of the project table (includes the JSON metadata column).</summary>
    [Table("project")]
    public class Project
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int OrganizationId { get; set; }
        public int? LeadId { get; set; }
        public int? CategoryId { get; set; }
        public decimal? Budget { get; set; }
        public int? Score { get; set; }
        public string Metadata { get; set; }
        public DateTime DateUtcCreated { get; set; }
        public DateTime DateUtcModified { get; set; }
    }

    /// <summary>
    /// Child table. [Column] attributes are explicit because this entity is referenced by
    /// subquery/collection attributes without being directly queried (so its columns are not
    /// auto-discovered); MySQL column names are case-insensitive but not underscore-insensitive.
    /// </summary>
    [Table("project_milestone")]
    public class ProjectMilestone
    {
        public int Id { get; set; }
        [Column("project_id")] public int ProjectId { get; set; }
        [Column("title")] public string Title { get; set; }
        [Column("status")] public string Status { get; set; }
        [Column("due_date")] public DateTime? DueDate { get; set; }
        [Column("completed_date")] public DateTime? CompletedDate { get; set; }
    }

    /// <summary>
    /// Read model over the project table exercising all four "view-replacing" attributes:
    /// [JsonPath] (scalar extraction from the metadata JSON column), [SqlExpression] (computed),
    /// [SubqueryAggregate] (correlated counts), and [JsonCollection] (child rows as a JSON array).
    /// </summary>
    [Table("project")]
    public class ProjectScorecard
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int? Score { get; set; }

        [JsonPath("metadata", "$.client_name")]
        public string ClientName { get; set; }

        [JsonPath("metadata", "$.risk_level")]
        public string RiskLevel { get; set; }

        [JsonPath("metadata", "$.priority", SqlType = "int")]
        public int? Priority { get; set; }

        [SqlExpression("CONCAT({Name}, ' (score=', COALESCE({Score}, 0), ')')")]
        public string Label { get; set; }

        /// <summary>Numeric COALESCE expression used for portable ORDER BY tests (no NULLs).</summary>
        [SqlExpression("COALESCE({Score}, 0)")]
        public int EffectiveScore { get; set; }

        [SubqueryAggregate(typeof(ProjectMilestone), nameof(ProjectMilestone.ProjectId), AggregateFunction.Count)]
        public int MilestoneCount { get; set; }

        [SubqueryAggregate(typeof(ProjectMilestone), nameof(ProjectMilestone.ProjectId), AggregateFunction.ConditionalCount,
            ConditionColumn = nameof(ProjectMilestone.Status), ConditionValue = "completed")]
        public int CompletedMilestones { get; set; }

        [JsonCollection(typeof(ProjectMilestone), nameof(ProjectMilestone.ProjectId),
            Columns = new[] { nameof(ProjectMilestone.Title), nameof(ProjectMilestone.Status) },
            OrderBy = nameof(ProjectMilestone.Title))]
        public string MilestonesJson { get; set; }
    }
}
