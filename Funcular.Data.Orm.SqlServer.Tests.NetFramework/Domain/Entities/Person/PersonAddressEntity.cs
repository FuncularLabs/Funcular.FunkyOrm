using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.SqlServer.Tests.NetFramework.Domain.Entities.Person
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
