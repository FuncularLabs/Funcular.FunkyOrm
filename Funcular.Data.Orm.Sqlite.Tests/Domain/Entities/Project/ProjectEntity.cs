using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Project
{
    [Table("project")]
    public class ProjectEntity
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("name")]
        public string Name { get; set; }

        [Column("organization_id")]
        public int OrganizationId { get; set; }

        [Column("lead_id")]
        public int? LeadId { get; set; }

        [Column("category_id")]
        public int? CategoryId { get; set; }

        [Column("budget")]
        public decimal? Budget { get; set; }

        [Column("score")]
        public int? Score { get; set; }

        [Column("metadata")]
        public string Metadata { get; set; }

        [Column("dateutc_created")]
        public DateTime DateUtcCreated { get; set; }

        [Column("dateutc_modified")]
        public DateTime DateUtcModified { get; set; }
    }
}
