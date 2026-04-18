using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Project
{
    /// <summary>
    /// Full detail class demonstrating all four JSON/computed attribute types on PostgreSQL.
    /// </summary>
    [Table("project")]
    public class ProjectScorecardFull : ProjectScorecard
    {
        // ?? Phase 2: SQL expression ??

        [SqlExpression("COALESCE({Score}, 0)")]
        public int EffectiveScore { get; set; }

        // ?? Phase 3: Subquery aggregate ??

        [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
            AggregateFunction.Count)]
        public int MilestoneCount { get; set; }

        [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
            AggregateFunction.ConditionalCount,
            ConditionColumn = nameof(ProjectMilestoneEntity.Status),
            ConditionValue = "completed")]
        public int MilestonesCompleted { get; set; }

        [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
            AggregateFunction.ConditionalCount,
            ConditionColumn = nameof(ProjectMilestoneEntity.Status),
            ConditionValue = "overdue")]
        public int MilestonesOverdue { get; set; }

        [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
            AggregateFunction.ConditionalCount,
            ConditionColumn = nameof(ProjectMilestoneEntity.Status),
            ConditionValue = "pending")]
        public int MilestonesPending { get; set; }

        [SubqueryAggregate(typeof(ProjectNoteEntity), nameof(ProjectNoteEntity.ProjectId),
            AggregateFunction.Count)]
        public int NoteCount { get; set; }

        [SubqueryAggregate(typeof(ProjectNoteEntity), nameof(ProjectNoteEntity.ProjectId),
            AggregateFunction.ConditionalCount,
            ConditionColumn = nameof(ProjectNoteEntity.Category),
            ConditionValue = "risk")]
        public int RiskNoteCount { get; set; }

        // ?? Phase 4: JSON collection ??

        [JsonCollection(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
            Columns = new[] { "Title", "Status" },
            OrderBy = "DueDate")]
        public string MilestonesJson { get; set; }

        [JsonCollection(typeof(ProjectNoteEntity), nameof(ProjectNoteEntity.ProjectId),
            Columns = new[] { "Content", "Category" },
            OrderBy = "DateUtcCreated")]
        public string NotesJson { get; set; }
    }
}
