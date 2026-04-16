using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Project
{
    /// <summary>
    /// Canonical entity for the project_category lookup table.
    /// </summary>
    [Table("project_category")]
    public class ProjectCategoryEntity
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("name")]
        public string Name { get; set; }

        [Column("code")]
        public string Code { get; set; }
    }
}
