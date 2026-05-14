using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Address;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Objects.Address
{
    /// <inheritdoc cref="AddressEntity"/>
    /// <remarks>
    /// <b>Table Name Resolution:</b> This class inherits <c>[Table("address")]</c> from
    /// <see cref="AddressEntity"/> (resolved via <c>inherit: true</c>). Even without the
    /// inherited attribute, <c>Address</c> would match <c>address</c> by exact convention.
    /// </remarks>
    [Serializable]
    public class Address : AddressEntity
    {
        private readonly IList<Person.PersonAddress> _persons = new List<Person.PersonAddress>();

        [NotMapped]
        public IList<Person.PersonAddress> Persons
        {
            get => _persons;
            set
            {
                _persons.Clear();
                foreach (var pa in value) _persons.Add(pa);
                OnPropertyChanged();
            }
        }
    }
}
