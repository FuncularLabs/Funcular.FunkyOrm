using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Address 
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
		#region Relationship Properties
		private readonly IList<Person.PersonAddress> _persons = new List<Person.PersonAddress>();

		/// <summary>
		/// Gets or sets the collection of persons.
		/// Never null, not required.
		/// </summary>
		[NotMapped]
		public IList<Person.PersonAddress> Persons
		{
			get => _persons;
			set
			{
				_persons.Clear();
				_persons.AddRange(value);
				OnPropertyChanged();
			}
		}

		#endregion
	}
}
