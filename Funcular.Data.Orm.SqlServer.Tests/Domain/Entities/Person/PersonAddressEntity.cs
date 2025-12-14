using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Enums;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person
{
    /// <summary>
    /// The Person Address Entity
    /// Maps to dbo.person_address table
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
        [RemoteLink(typeof(AddressEntity))]
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

        [RemoteProperty(typeof(AddressEntity), nameof(AddressId), nameof(AddressEntity.Line1))]
        public string Line1 { get; set; }

        [RemoteProperty(typeof(AddressEntity), nameof(AddressId), nameof(AddressEntity.City))]
        public string City { get; set; }

        [RemoteProperty(typeof(AddressEntity), nameof(AddressId), nameof(AddressEntity.StateCode))]
        public string StateCode { get; set; }

        [RemoteProperty(typeof(AddressEntity), nameof(AddressId), nameof(AddressEntity.PostalCode))]
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
