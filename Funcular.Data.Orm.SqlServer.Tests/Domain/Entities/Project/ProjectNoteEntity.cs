using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Project
{
    /// <summary>
    /// Canonical entity for the project_note table.
    /// </summary>
    [Table("project_note")]
    public class ProjectNoteEntity
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("project_id")]
        public int ProjectId { get; set; }

        [Column("author_id")]
        public int? AuthorId { get; set; }

        [Column("content")]
        public string Content { get; set; }

        [Column("category")]
        public string Category { get; set; }

        [Column("dateutc_created")]
        public DateTime DateUtcCreated { get; set; }
    }
}
