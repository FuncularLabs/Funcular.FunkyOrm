using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person
{
    /// <summary>
    /// Extended person entity with remote-linked properties for JOINs to related tables.
    /// Use this class when you need the related data (employer, country, addresses) in a single query.
    /// <para>
    /// <b>Table Name Resolution:</b> No <c>[Table]</c> attribute is needed here because this class
    /// inherits from <see cref="PersonEntity"/>, which declares <c>[Table("person")]</c>.
    /// The ORM finds the parent's attribute via <c>GetCustomAttribute&lt;TableAttribute&gt;(inherit: true)</c>.
    /// </para>
    /// </summary>
    [Serializable]
    public class PersonDetailEntity : PersonEntity
    {
        [Column("employer_id")]
        [RemoteLink(typeof(OrganizationDetailEntity))]
        public new int? EmployerId { get { return base.EmployerId; } set { base.EmployerId = value; } }

        [NotMapped]
        public new ICollection<PersonAddressDetailEntity> Addresses { get; set; } = new List<PersonAddressDetailEntity>();

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
