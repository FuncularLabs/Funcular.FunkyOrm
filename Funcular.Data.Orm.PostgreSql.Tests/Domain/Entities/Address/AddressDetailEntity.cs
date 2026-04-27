using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Country;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Address
{
    /// <summary>
    /// Detail projection of <see cref="AddressEntity"/> with remote-linked country.
    /// <para>
    /// <b>Table Name Resolution:</b> No <c>[Table]</c> attribute is needed. This class inherits
    /// <c>[Table("address")]</c> from <see cref="AddressEntity"/> via <c>inherit: true</c>.
    /// </para>
    /// </summary>
    public class AddressDetailEntity : AddressEntity
    {
        [Column("country_id")]
        [RemoteLink(typeof(CountryEntity))]
        public new int? CountryId { get { return base.CountryId; } set { base.CountryId = value; } }
    }
}
