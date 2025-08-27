using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Address 
{
	///<inheritdoc cref="AddressEntity"/>
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
