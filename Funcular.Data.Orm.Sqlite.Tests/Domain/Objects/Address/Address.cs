using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Address;

namespace Funcular.Data.Orm.Sqlite.Tests.Domain.Objects.Address
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
