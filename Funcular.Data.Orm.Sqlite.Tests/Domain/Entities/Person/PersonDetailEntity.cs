using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Address;

namespace Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Person
{
    [Serializable]
    public class PersonDetailEntity : PersonEntity
    {
        [Column("employer_id")]
        [RemoteLink(typeof(OrganizationDetailEntity))]
        public new int? EmployerId { get { return base.EmployerId; } set { base.EmployerId = value; } }

        [RemoteProperty(typeof(CountryEntity),
            nameof(EmployerId),
            nameof(OrganizationEntity.HeadquartersAddressId),
            nameof(AddressEntity.CountryId),
            nameof(CountryEntity.Name))]
        public string EmployerHeadquartersCountryName { get; set; }

        [RemoteKey(typeof(CountryEntity),
            nameof(EmployerId),
            nameof(OrganizationEntity.HeadquartersAddressId),
            nameof(AddressEntity.CountryId),
            nameof(CountryEntity.Id))]
        public int? EmployerHeadquartersCountryId { get; set; }
    }
}
