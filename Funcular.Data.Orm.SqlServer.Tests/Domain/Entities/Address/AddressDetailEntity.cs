using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Country;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address
{
    [Table("address")]
    public class AddressDetailEntity : AddressEntity
    {
        [Column("country_id")]
        [RemoteLink(typeof(CountryEntity))]
        public new int? CountryId { get { return base.CountryId; } set { base.CountryId = value; } }
    }
}
