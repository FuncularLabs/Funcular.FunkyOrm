using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person
{
    /// <summary>
    /// The Person Detail Entity
    /// Maps to dbo.person table but includes remote properties that trigger joins.
    /// Use this only when you need the related data.
    /// </summary>
    [Table("person")]
    [Serializable]
    public class PersonDetailEntity : PersonEntity
    {
        // Implicit Mode - Converted to Explicit to avoid ambiguity in subclass Person
        [RemoteProperty(typeof(CountryEntity), 
            nameof(EmployerId), 
            nameof(OrganizationEntity.HeadquartersAddressId), 
            nameof(AddressEntity.CountryId), 
            nameof(CountryEntity.Name))]
        public string EmployerHeadquartersCountryName { get; set; }

        // Explicit Mode
        [RemoteProperty(typeof(CountryEntity),
            nameof(EmployerId),
            nameof(OrganizationEntity.HeadquartersAddressId),
            nameof(AddressEntity.CountryId),
            nameof(CountryEntity.Name)
        )]
        public string ExplicitCountryName { get; set; }

        // Remote Key Example (fetching ID) - Converted to Explicit to avoid ambiguity
        [RemoteKey(typeof(CountryEntity), 
            nameof(EmployerId), 
            nameof(OrganizationEntity.HeadquartersAddressId), 
            nameof(AddressEntity.CountryId), 
            nameof(CountryEntity.Id))]
        public int? EmployerHeadquartersCountryId { get; set; }
    }
}
