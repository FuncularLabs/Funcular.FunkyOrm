using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Address;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person
{
    /// <summary>
    /// Detail projection of <see cref="PersonEntity"/> with remote-linked relationships.
    /// <para>
    /// <b>Table Name Resolution:</b> No <c>[Table]</c> attribute is needed. This class inherits
    /// <c>[Table("person")]</c> from <see cref="PersonEntity"/> via <c>inherit: true</c>.
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
