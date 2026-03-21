using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Enums;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person
{
    [Table("person_address")]
    [Serializable]
    public class PersonAddressEntity : PersistenceStateEntity
    {
        private int _id;
        private int _personId;
        private int _addressId;
        private bool _isPrimary;
        private int? _addressTypeValue = 0;
        private DateTime _dateUtcCreated = DateTime.UtcNow;
        private DateTime _dateUtcModified = DateTime.UtcNow;

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public virtual int Id { get => _id; set => SetProperty(ref _id, value); }

        [Column("person_id")]
        public virtual int PersonId { get => _personId; set => SetProperty(ref _personId, value); }

        [Column("address_id")]
        public virtual int AddressId { get => _addressId; set => SetProperty(ref _addressId, value); }

        [Column("is_primary")]
        public bool IsPrimary { get => _isPrimary; set => SetProperty(ref _isPrimary, value); }

        [Column("address_type_value")]
        public int? AddressTypeValue { get => _addressTypeValue; set => SetProperty(ref _addressTypeValue, value); }

        [Column("dateutc_created")]
        public virtual DateTime DateUtcCreated { get => _dateUtcCreated; set => SetProperty(ref _dateUtcCreated, value); }

        [Column("dateutc_modified")]
        public virtual DateTime DateUtcModified { get => _dateUtcModified; set => SetProperty(ref _dateUtcModified, value); }
    }
}
