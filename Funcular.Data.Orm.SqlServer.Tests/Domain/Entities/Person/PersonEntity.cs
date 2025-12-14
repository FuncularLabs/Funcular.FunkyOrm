using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person 
{
	/// <summary>
	/// The Person Entity
	/// Maps to dbo.person table
	/// </summary>
	[Table("person")]
	[Serializable]
	public class PersonEntity : PersistenceStateEntity
	{

		#region Members
		private Int32 _id;
		private String _firstName;
		private String _middleInitial;
		private String _lastName;
		private DateTime? _birthdate;
		private String _gender;
		private DateTime _dateUtcCreated = DateTime.UtcNow;
        private DateTime _dateUtcModified = DateTime.UtcNow;
        #endregion

        #region Properties
        /// <summary>
        /// Id - Int32
        /// Required
        /// </summary>
        [Key]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[Column("id")]
		public virtual Int32 Id
		{
			get => _id;
			set => SetProperty(ref _id, value);
		}
		/// <summary>
		/// First Name - String
		/// Required
		/// Maximum length - 100.
		/// </summary>
		[Column("first_name")]
		[StringLength(100, MinimumLength = 1, ErrorMessage = "Max length of 100 characters exceeded.")]
		public virtual string FirstName
		{
			get => _firstName;
			set => SetProperty(ref _firstName, value);
		}
		/// <summary>
		/// Middle Initial - String
		/// Nulls allowed
		/// Maximum length - 1.
		/// </summary>
		[Column("middle_initial")]
		[StringLength(1, ErrorMessage = "Max length of 1 characters exceeded.")]
		public virtual String MiddleInitial
		{
			get => _middleInitial;
			set => SetProperty(ref _middleInitial, value);
		}
		/// <summary>
		/// Last Name - String
		/// Required
		/// Maximum length - 100.
		/// </summary>
		[Column("last_name")]
		[StringLength(100, MinimumLength = 1, ErrorMessage = "Max length of 100 characters exceeded.")]
		public virtual String LastName
		{
			get => _lastName;
			set => SetProperty(ref _lastName, value);
		}
		/// <summary>
		/// Birthdate - DateTime
		/// Nulls allowed
		/// </summary>
		[Column("birthdate")]
		public virtual DateTime? Birthdate
		{
			get => _birthdate;
			set => SetProperty(ref _birthdate, value);
		}
		/// <summary>
		/// Gender - String
		/// Nulls allowed
		/// Maximum length - 10.
		/// </summary>
		[Column("gender")]
		[StringLength(10, ErrorMessage = "Max length of 10 characters exceeded.")]
		public virtual string Gender
		{
			get => _gender;
			set => SetProperty(ref _gender, value);
		}
		/// <summary>
		/// DateUtc Created - DateTime
		/// Required
		/// </summary>
		[Column("dateutc_created")]
		public virtual DateTime DateUtcCreated
		{
			get => _dateUtcCreated;
			set => SetProperty(ref _dateUtcCreated, value);
		}
		/// <summary>
		/// DateUtc Modified - DateTime
		/// Required
		/// </summary>
		[Column("dateutc_modified")]
		public virtual DateTime DateUtcModified
		{
			get => _dateUtcModified;
			set => SetProperty(ref _dateUtcModified, value);
		}

        /// <summary>
        /// Gets or sets the unique identifier.
        /// </summary>
        /// <value>The unique identifier.</value>
        public Guid? UniqueId { get; set; }

        [Column("employer_id")]
        [RemoteLink(typeof(OrganizationEntity))]
        public int? EmployerId { get; set; }

        [NotMapped]
        public ICollection<CountryEntity> AssociatedCountries { get; set; } = new List<CountryEntity>();

        [NotMapped]
        public ICollection<PersonAddressEntity> Addresses { get; set; } = new List<PersonAddressEntity>();

        #endregion

	}
}
