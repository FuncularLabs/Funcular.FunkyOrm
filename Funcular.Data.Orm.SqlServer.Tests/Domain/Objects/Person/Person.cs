using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Person 
{
	///<inheritdoc cref="PersonEntity"/>
	[Serializable]
	public class Person : PersonEntity
	{
		#region Relationship Properties
		// readonly to eliminate the possibility of null reference exceptions
        private readonly IList<PersonAddress> _personAddressJoins = new List<PersonAddress>();

		/// <summary>
		/// Gets or sets the collection of addresses.
		/// Never null, not required.
		/// </summary>
		[NotMapped]
		public IList<PersonAddress> Addresses
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
