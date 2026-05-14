using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Address;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Organization
{
    /// <summary>
    /// Detail projection of <see cref="OrganizationEntity"/> with remote-linked address.
    /// <para>
    /// <b>Table Name Resolution:</b> No <c>[Table]</c> attribute is needed. This class inherits
    /// <c>[Table("organization")]</c> from <see cref="OrganizationEntity"/> via <c>inherit: true</c>.
    /// </para>
    /// </summary>
    public class OrganizationDetailEntity : OrganizationEntity
    {
        [Column("headquarters_address_id")]
        [RemoteLink(typeof(AddressDetailEntity))]
        public new int? HeadquartersAddressId { get { return base.HeadquartersAddressId; } set { base.HeadquartersAddressId = value; } }
    }
}
