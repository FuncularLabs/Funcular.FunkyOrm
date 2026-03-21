using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Address;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person
{
    [Table("person")]
    [Serializable]
    public class PersonDetailEntity : PersonEntity
    {
        [Column("employer_id")]
        [RemoteLink(typeof(OrganizationDetailEntity))]
        public new int? EmployerId { get { return base.EmployerId; } set { base.EmployerId = value; } }

        [NotMapped]
        public new ICollection<PersonAddressDetailEntity> Addresses { get; set; } = new List<PersonAddressDetailEntity>();

        [RemoteProperty(typeof(CountryEntity),
            nameof(EmployerId),
            nameof(OrganizationEntity.HeadquartersAddressId),
            nameof(AddressEntity.CountryId),
            nameof(CountryEntity.Name))]
        public string EmployerHeadquartersCountryName { get; set; }

        [RemoteProperty(typeof(CountryEntity),
            nameof(EmployerId),
            nameof(OrganizationEntity.HeadquartersAddressId),
            nameof(AddressEntity.CountryId),
            nameof(CountryEntity.Name))]
        public string ExplicitCountryName { get; set; }

        [RemoteKey(typeof(CountryEntity),
            nameof(EmployerId),
            nameof(OrganizationEntity.HeadquartersAddressId),
            nameof(AddressEntity.CountryId),
            nameof(CountryEntity.Id))]
        public int? EmployerHeadquartersCountryId { get; set; }
    }
}
