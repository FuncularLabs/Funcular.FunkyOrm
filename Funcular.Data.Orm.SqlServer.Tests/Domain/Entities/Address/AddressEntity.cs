using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address 
{
	/// <summary>
	/// The Address Entity
	/// Maps to dbo.address table
	/// </summary>
	//The data annotation is unnecessary because 
	//[Table("address")]
	[Serializable]
	public class AddressEntity : PersistenceStateEntity
	{

		#region Members
		private int _id;
		private string? _line1;
		private string? _line2;
		private string? _city;
		private string? _stateCode;
		private string? _postalCode;
        private bool _isPrimary;
        private DateTime _dateUtcCreated = DateTime.UtcNow;
		private DateTime _dateUtcModified = DateTime.UtcNow;
        #endregion

        #region Properties
        /// <summary>
        /// Id - Int32
        /// Required
        /// </summary>
        [Key] // optional because the property name matches the column name
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[Column("id")] // optional because the property name matches the column name

        public virtual int Id
		{
			get => _id;
			set => SetProperty(ref _id, value);
		}
		/// <summary>
		/// Line 1 - String
		/// Required
		/// Maximum length - 255.
		/// </summary>
		[Column("line_1")]
		[StringLength(255, MinimumLength = 1, ErrorMessage = "Max length of 255 characters exceeded.")]
		public virtual string? Line1
		{
			get => _line1;
			set => SetProperty(ref _line1, value);
		}
		/// <summary>
		/// Line 2 - String
		/// Nulls allowed
		/// Maximum length - 255.
		/// </summary>
		[Column("line_2")] // optional because the property name matches the column name (case-insensitive, ignore underscores)
        [StringLength(255, ErrorMessage = "Max length of 255 characters exceeded.")]
		public virtual string? Line2
		{
			get => _line2;
			set => SetProperty(ref _line2, value);
		}
		/// <summary>
		/// City - String
		/// Required
		/// Maximum length - 100.
		/// </summary>
		[Column("city")] // optional because the property name matches the column name
        [StringLength(100, MinimumLength = 1, ErrorMessage = "Max length of 100 characters exceeded.")]
		public virtual string? City
		{
			get => _city;
			set => SetProperty(ref _city, value);
		}
		/// <summary>
		/// State Code - String
		/// Required
		/// Maximum length - 2.
		/// </summary>
		[Column("state_code")]
		[StringLength(2, MinimumLength = 1, ErrorMessage = "Max length of 2 characters exceeded.")]
		public virtual string? StateCode
		{
			get => _stateCode;
			set => SetProperty(ref _stateCode, value);
		}
		/// <summary>
		/// Postal Code - String
		/// Required
		/// Maximum length - 20.
		/// </summary>
		[Column("postal_code")]
		[StringLength(20, MinimumLength = 1, ErrorMessage = "Max length of 20 characters exceeded.")]
		public virtual string? PostalCode
		{
			get => _postalCode;
			set => SetProperty(ref _postalCode, value);
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
		/// Gets or sets a value indicating whether this instance is primary.
		/// </summary>
		/// <value><c>null</c> if [is primary] contains no value, <c>true</c> if [is primary]; otherwise, <c>false</c>.</value>
		// removed attribute to test automatic column inference on column names with underscores:
		// [Column("is_primary", TypeName = "bit")]
        public bool IsPrimary
        {
            get => _isPrimary;
            set => _isPrimary = value;
        }

        #endregion

	}
}
