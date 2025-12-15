using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Person
{
    ///<inheritdoc cref="PersonEntity"/>
    [Serializable]
    public partial class Person : PersonEntity
    {
        #region Overrides of Object

        /// <summary>Returns a string that represents the current object.</summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString()
        {
            var birthdate = Birthdate != null ? Birthdate.Value.ToString("d") : string.Empty;
            ;
            return $"First: {FirstName}, Last: {LastName}, Gender: {Gender}, Birthdate: {birthdate}";
        }

        #endregion

        #region Relationship Properties

        // readonly to eliminate the possibility of null reference exceptions
        private readonly IList<PersonAddress> _personAddressJoins = new List<PersonAddress>();

        /// <summary>
        /// Gets or sets the collection of addresses.
        /// Never null, not required.
        /// </summary>
        [NotMapped]
        public new IList<PersonAddress> Addresses
        {
            get => _personAddressJoins;
            set
            {
                _personAddressJoins.Clear();
                foreach (var personAddress in value)
                {
                    _personAddressJoins.Add(personAddress);
                }

                OnPropertyChanged();
            }
        }

        #endregion
    }
}