using System;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person
{
    /// <summary>
    /// Extended person-address entity with remote-linked properties for JOINs to the address table.
    /// <para>
    /// <b>Table Name Resolution:</b> No <c>[Table]</c> attribute is needed here because this class
    /// inherits from <see cref="PersonAddressEntity"/>, which declares <c>[Table("person_address")]</c>.
    /// The ORM finds the parent's attribute via <c>GetCustomAttribute&lt;TableAttribute&gt;(inherit: true)</c>.
    /// </para>
    /// </summary>
    public class PersonAddressDetailEntity : PersonAddressEntity
    {
        [Column("address_id")]
        [RemoteLink(typeof(AddressDetailEntity))]
        public new int AddressId { get { return base.AddressId; } set { base.AddressId = value; } }

        [RemoteProperty(typeof(AddressDetailEntity), nameof(AddressId), nameof(AddressDetailEntity.Line1))]
        public new string Line1 { get { return base.Line1; } set { base.Line1 = value; } }

        [RemoteProperty(typeof(AddressDetailEntity), nameof(AddressId), nameof(AddressDetailEntity.City))]
        public new string City { get { return base.City; } set { base.City = value; } }

        [RemoteProperty(typeof(AddressDetailEntity), nameof(AddressId), nameof(AddressDetailEntity.StateCode))]
        public new string StateCode { get { return base.StateCode; } set { base.StateCode = value; } }

        [RemoteProperty(typeof(AddressDetailEntity), nameof(AddressId), nameof(AddressDetailEntity.PostalCode))]
        public new string PostalCode { get { return base.PostalCode; } set { base.PostalCode = value; } }
    }
}
