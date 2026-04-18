using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Organization
{
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
