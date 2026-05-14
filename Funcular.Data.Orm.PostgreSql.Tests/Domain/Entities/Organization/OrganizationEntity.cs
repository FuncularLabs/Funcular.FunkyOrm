using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Organization
{
    /// <summary>
    /// Canonical entity for the <c>organization</c> table.
    /// <para>
    /// <b>Table Name Resolution:</b> Requires <c>[Table]</c> because <c>OrganizationEntity</c> normalizes to
    /// <c>organizationentity</c>, which does not match <c>organization</c>.
    /// </para>
    /// </summary>
    [Table("organization")]
    public class OrganizationEntity : PersistenceStateEntity
    {
        public int Id { get; set; }
        public string Name { get; set; }

        [Column("headquarters_address_id")]
        public int? HeadquartersAddressId { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        [Column("name_length")]
        public int? NameLength { get; set; }
    }
}
