using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Project
{
    /// <summary>
    /// Full detail class demonstrating all four JSON/computed attribute types.
    /// Extends <see cref="ProjectScorecard"/> which already has [JsonPath] attributes.
    /// <para>
    /// <b>Table Name Resolution:</b> No <c>[Table]</c> attribute is needed here because this class
    /// inherits from <see cref="ProjectScorecard"/> → <see cref="ProjectEntity"/>, which declares
    /// <c>[Table("project")]</c>. The ORM walks the full inheritance chain via
    /// <c>GetCustomAttribute&lt;TableAttribute&gt;(inherit: true)</c>.
    /// </para>
    /// </summary>
    public class ProjectScorecardFull : ProjectScorecard
    {
        // ?? Phase 2: SQL expression ??

        /// <summary>
        /// Falls back to stored score when no override is available.
        /// Uses a simple COALESCE expression with {PropertyName} tokens.
        /// </summary>
        [SqlExpression("COALESCE({Score}, 0)")]
        public int EffectiveScore { get; set; }

        // ?? Phase 3: Subquery aggregate ??

        /// <summary>Total milestones for this project.</summary>
        [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
            AggregateFunction.Count)]
        public int MilestoneCount { get; set; }

        /// <summary>Completed milestone count (conditional).</summary>
        [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
            AggregateFunction.ConditionalCount,
            ConditionColumn = nameof(ProjectMilestoneEntity.Status),
            ConditionValue = "completed")]
        public int MilestonesCompleted { get; set; }

        /// <summary>Overdue milestone count (conditional).</summary>
        [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
            AggregateFunction.ConditionalCount,
            ConditionColumn = nameof(ProjectMilestoneEntity.Status),
            ConditionValue = "overdue")]
        public int MilestonesOverdue { get; set; }

        /// <summary>Pending milestone count (conditional).</summary>
        [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
            AggregateFunction.ConditionalCount,
            ConditionColumn = nameof(ProjectMilestoneEntity.Status),
            ConditionValue = "pending")]
        public int MilestonesPending { get; set; }

        /// <summary>Total note count.</summary>
        [SubqueryAggregate(typeof(ProjectNoteEntity), nameof(ProjectNoteEntity.ProjectId),
            AggregateFunction.Count)]
        public int NoteCount { get; set; }

        /// <summary>Risk-flagged note count.</summary>
        [SubqueryAggregate(typeof(ProjectNoteEntity), nameof(ProjectNoteEntity.ProjectId),
            AggregateFunction.ConditionalCount,
            ConditionColumn = nameof(ProjectNoteEntity.Category),
            ConditionValue = "risk")]
        public int RiskNoteCount { get; set; }

        // ?? Phase 4: JSON collection ??

        /// <summary>Milestones projected as a JSON array.</summary>
        [JsonCollection(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
            Columns = new[] { "Title", "Status" },
            OrderBy = "DueDate")]
        public string MilestonesJson { get; set; }

        /// <summary>Notes projected as a JSON array.</summary>
        [JsonCollection(typeof(ProjectNoteEntity), nameof(ProjectNoteEntity.ProjectId),
            Columns = new[] { "Content", "Category" },
            OrderBy = "DateUtcCreated")]
        public string NotesJson { get; set; }
    }
}
