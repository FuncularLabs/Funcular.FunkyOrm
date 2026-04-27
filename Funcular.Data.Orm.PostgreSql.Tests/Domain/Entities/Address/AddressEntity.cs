using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Address
{
    /// <summary>
    /// Canonical entity for the <c>address</c> table.
    /// <para>
    /// <b>Table Name Resolution:</b> Requires <c>[Table]</c> because <c>AddressEntity</c> normalizes to
    /// <c>addressentity</c>, which does not match <c>address</c>.
    /// </para>
    /// </summary>
    [Table("address")]
    [Serializable]
    public class AddressEntity : PersistenceStateEntity
    {
        private int _id;
        private string _line1;
        private string _line2;
        private string _city;
        private string _stateCode;
        private string _postalCode;
        private DateTime _dateUtcCreated = DateTime.UtcNow;
        private DateTime _dateUtcModified = DateTime.UtcNow;

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public virtual int Id { get => _id; set => SetProperty(ref _id, value); }

        [Column("line_1")]
        [StringLength(255, MinimumLength = 1)]
        public virtual string Line1 { get => _line1; set => SetProperty(ref _line1, value); }

        [Column("line_2")]
        [StringLength(255)]
        public virtual string Line2 { get => _line2; set => SetProperty(ref _line2, value); }

        [Column("city")]
        [StringLength(100, MinimumLength = 1)]
        public virtual string City { get => _city; set => SetProperty(ref _city, value); }

        [Column("state_code")]
        [StringLength(2, MinimumLength = 1)]
        public virtual string StateCode { get => _stateCode; set => SetProperty(ref _stateCode, value); }

        [Column("postal_code")]
        [StringLength(20, MinimumLength = 1)]
        public virtual string PostalCode { get => _postalCode; set => SetProperty(ref _postalCode, value); }

        [Column("dateutc_created")]
        public virtual DateTime DateUtcCreated { get => _dateUtcCreated; set => SetProperty(ref _dateUtcCreated, value); }

        [Column("dateutc_modified")]
        public virtual DateTime DateUtcModified { get => _dateUtcModified; set => SetProperty(ref _dateUtcModified, value); }

        [Column("country_id")]
        public int? CountryId { get; set; }
    }
}
