using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Objects.Person
{
    [Serializable]
    public partial class Person : PersonEntity
    {
        public override string ToString()
        {
            var birthdate = Birthdate != null ? Birthdate.Value.ToString("d") : string.Empty;
            return $"First: {FirstName}, Last: {LastName}, Gender: {Gender}, Birthdate: {birthdate}";
        }

        private readonly IList<PersonAddress> _personAddressJoins = new List<PersonAddress>();

        [NotMapped]
        public new IList<PersonAddress> Addresses
        {
            get => _personAddressJoins;
            set
            {
                _personAddressJoins.Clear();
                foreach (var personAddress in value)
                    _personAddressJoins.Add(personAddress);
                OnPropertyChanged();
            }
        }
    }
}
