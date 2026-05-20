using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Address;

namespace Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Person
{
    public class PersonAddressDetailEntity : PersonAddressEntity
    {
        [Column("address_id")]
        [RemoteLink(typeof(AddressDetailEntity))]
        public new int AddressId { get { return base.AddressId; } set { base.AddressId = value; } }

        [RemoteProperty(typeof(AddressDetailEntity), nameof(AddressId), nameof(AddressDetailEntity.Line1))]
        public string Line1 { get; set; }

        [RemoteProperty(typeof(AddressDetailEntity), nameof(AddressId), nameof(AddressDetailEntity.City))]
        public string City { get; set; }

        [RemoteProperty(typeof(AddressDetailEntity), nameof(AddressId), nameof(AddressDetailEntity.StateCode))]
        public string StateCode { get; set; }

        [RemoteProperty(typeof(AddressDetailEntity), nameof(AddressId), nameof(AddressDetailEntity.PostalCode))]
        public string PostalCode { get; set; }
    }
}
