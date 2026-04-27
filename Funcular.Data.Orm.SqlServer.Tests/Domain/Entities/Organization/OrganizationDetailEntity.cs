using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Organization
{
    /// <summary>
    /// Extended organization entity with remote-linked properties for JOINs.
    /// <para>
    /// <b>Table Name Resolution:</b> No <c>[Table]</c> attribute is needed here because this class
    /// inherits from <see cref="OrganizationEntity"/>, which declares <c>[Table("organization")]</c>.
    /// The ORM finds the parent's attribute via <c>GetCustomAttribute&lt;TableAttribute&gt;(inherit: true)</c>.
    /// </para>
    /// </summary>
    public class OrganizationDetailEntity : OrganizationEntity
    {
        [Column("headquarters_address_id")]
        [RemoteLink(typeof(AddressDetailEntity))]
        public new int? HeadquartersAddressId { get { return base.HeadquartersAddressId; } set { base.HeadquartersAddressId = value; } }
    }
}
