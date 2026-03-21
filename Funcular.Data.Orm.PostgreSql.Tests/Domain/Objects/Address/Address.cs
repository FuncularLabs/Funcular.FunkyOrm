using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Address;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Objects.Address
{
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
