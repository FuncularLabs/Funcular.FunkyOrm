using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Organization
{
    [Table("organization")]
    public class OrganizationDetailEntity : OrganizationEntity
    {
        [Column("headquarters_address_id")]
        [RemoteLink(typeof(AddressDetailEntity))]
        public new int? HeadquartersAddressId { get { return base.HeadquartersAddressId; } set { base.HeadquartersAddressId = value; } }
    }
}
