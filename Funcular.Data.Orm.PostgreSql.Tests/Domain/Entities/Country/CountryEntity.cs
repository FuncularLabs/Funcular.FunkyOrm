using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Country
{
    /// <summary>
    /// Canonical entity for the <c>country</c> table.
    /// <para>
    /// <b>Table Name Resolution:</b> Requires <c>[Table]</c> because <c>CountryEntity</c> normalizes to
    /// <c>countryentity</c>, which does not match <c>country</c>.
    /// </para>
    /// </summary>
    [Table("country")]
    public class CountryEntity : PersistenceStateEntity
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
