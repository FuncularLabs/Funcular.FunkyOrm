using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Project
{
    /// <summary>
    /// Canonical entity for the <c>project</c> table.
    /// Contains a JSON metadata column for <c>[JsonPath]</c> attribute testing.
    /// <para>
    /// <b>Table Name Resolution:</b> Requires <c>[Table]</c> because <c>ProjectEntity</c> normalizes to
    /// <c>projectentity</c>, which does not match <c>project</c>.
    /// </para>
    /// </summary>
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

        /// <summary>
        /// JSON column holding semi-structured metadata.
        /// </summary>
        [Column("metadata")]
        public string Metadata { get; set; }

        [Column("dateutc_created")]
        public DateTime DateUtcCreated { get; set; }

        [Column("dateutc_modified")]
        public DateTime DateUtcModified { get; set; }
    }
}
