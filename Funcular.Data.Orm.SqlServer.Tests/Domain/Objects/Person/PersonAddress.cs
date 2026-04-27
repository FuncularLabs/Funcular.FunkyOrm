using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Person 
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
		#region Relationship Properties
		/// <summary>
		/// Gets or sets the person object.
		/// Can be null, required.
		/// </summary>
		[NotMapped]
		public Person Person { get; set; }

		/// <summary>
		/// Gets or sets the address object.
		/// Can be null, required.
		/// </summary>
		[NotMapped]
		public Address.Address Address { get; set; }

		#endregion
	}
}
