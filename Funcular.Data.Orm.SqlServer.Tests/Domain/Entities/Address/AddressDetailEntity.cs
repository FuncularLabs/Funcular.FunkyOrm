using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Country;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address
{
    /// <summary>
    /// Extended address entity that includes remote-linked properties for JOINs.
    /// <para>
    /// <b>Table Name Resolution:</b> No <c>[Table]</c> attribute is needed here because this class
    /// inherits from <see cref="AddressEntity"/>, which declares <c>[Table("address")]</c>.
    /// The ORM resolves the table name via <c>GetCustomAttribute&lt;TableAttribute&gt;(inherit: true)</c>,
    /// which walks the inheritance chain and finds the parent's attribute.
    /// </para>
    /// </summary>
    public class AddressDetailEntity : AddressEntity
    {
        [Column("country_id")]
        [RemoteLink(typeof(CountryEntity))]
        public new int? CountryId { get { return base.CountryId; } set { base.CountryId = value; } }
    }
}
