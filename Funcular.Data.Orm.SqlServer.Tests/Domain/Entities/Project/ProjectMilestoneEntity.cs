using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Project
{
    /// <summary>
    /// Canonical entity for the <c>dbo.project_milestone</c> table.
    /// <para>
    /// <b>Table Name Resolution:</b> Requires <c>[Table]</c> because <c>ProjectMilestoneEntity</c> normalizes
    /// to <c>projectmilestoneentity</c>, which does not match <c>projectmilestone</c>.
    /// </para>
    /// </summary>
    [Table("project_milestone")]
    public class ProjectMilestoneEntity
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("project_id")]
        public int ProjectId { get; set; }

        [Column("title")]
        public string Title { get; set; }

        [Column("status")]
        public string Status { get; set; }

        [Column("due_date")]
        public DateTime? DueDate { get; set; }

        [Column("completed_date")]
        public DateTime? CompletedDate { get; set; }
    }
}
