using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Project
{
    /// <summary>
    /// Canonical entity for the <c>project_category</c> lookup table.
    /// <para>
    /// <b>Table Name Resolution:</b> Requires <c>[Table]</c> because <c>ProjectCategoryEntity</c> normalizes
    /// to <c>projectcategoryentity</c>, which does not match <c>projectcategory</c>.
    /// </para>
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
