using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Objects.Person
{
    /// <inheritdoc cref="PersonEntity"/>
    /// <remarks>
    /// <b>Table Name Resolution:</b> This class inherits <c>[Table("person")]</c> from
    /// <see cref="PersonEntity"/> (resolved via <c>inherit: true</c>). Even without the
    /// inherited attribute, <c>Person</c> would match <c>person</c> by exact convention.
    /// </remarks>
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
