using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Objects.Person
{
    /// <inheritdoc cref="PersonAddressEntity"/>
    /// <remarks>
    /// <b>Table Name Resolution:</b> This class inherits <c>[Table("person_address")]</c> from
    /// <see cref="PersonAddressEntity"/> (resolved via <c>inherit: true</c>). Even without the
    /// inherited attribute, <c>PersonAddress</c> would match <c>person_address</c> by convention.
    /// </remarks>
    [Serializable]
    public class PersonAddress : PersonAddressEntity
    {
        [NotMapped]
        public Person Person { get; set; }

        [NotMapped]
        public Address.Address Address { get; set; }
    }
}
