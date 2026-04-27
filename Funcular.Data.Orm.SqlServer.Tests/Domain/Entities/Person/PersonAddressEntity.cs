using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Enums;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person
{
    /// <summary>
    /// The Person Address Entity — maps to the <c>dbo.person_address</c> table.
    /// <para>
    /// <b>Table Name Resolution:</b> This class requires a <c>[Table]</c> attribute because its class name
    /// (<c>PersonAddressEntity</c>) does not match the database table name (<c>person_address</c>) under
    /// convention-based resolution. The <see cref="IgnoreUnderscoreAndCaseStringComparer"/> strips underscores
    /// and compares case-insensitively, so <c>PersonAddressEntity</c> normalizes to <c>personaddressentity</c>
    /// while <c>person_address</c> normalizes to <c>personaddress</c> — these do not match.
    /// </para>
    /// <para>
    /// If you only ever query through the subclass <see cref="Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Person.PersonAddress"/>
    /// (whose name <c>PersonAddress</c> normalizes to <c>personaddress</c>, matching <c>person_address</c>),
    /// the attribute on this base class is still needed because <c>PersonAddress</c> inherits it via
    /// <c>GetCustomAttribute&lt;TableAttribute&gt;(inherit: true)</c>.
    /// </para>
    /// </summary>
    [Table("person_address")]
    [Serializable]
    public class PersonAddressEntity : PersistenceStateEntity
    {

        #region Members
        private Int32 _id;
        private Int32 _personId;
        private Int32 _addressId;
        private bool _isPrimary;
        private int? _addressTypeValue = 0;
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
        /// Person Id - Int32
        /// Required
        /// </summary>
        [Column("person_id")]
        public virtual Int32 PersonId
        {
            get => _personId;
            set => SetProperty(ref _personId, value);
        }
        /// <summary>
        /// Address Id - Int32
        /// Required
        /// </summary>
        [Column("address_id")]
        public virtual Int32 AddressId
        {
            get => _addressId;
            set => SetProperty(ref _addressId, value);
        }

        [Column("is_primary")]
        public bool IsPrimary
        {
            get => _isPrimary;
            set => SetProperty(ref _isPrimary, value);
        }

        [Column("address_type_value")]
        public int? AddressTypeValue
        {
            get => _addressTypeValue;
            set => SetProperty(ref _addressTypeValue, value);
        }

        public string AddressTypeLabel 
        {
            get 
            {
                var type = (AddressType)(AddressTypeValue ?? 0);
                return type.ToString().Replace(", ", ","); 
            }
        }

        public string Line1 { get; set; }

        public string City { get; set; }

        public string StateCode { get; set; }

        public string PostalCode { get; set; }

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
        #endregion

    }
}
