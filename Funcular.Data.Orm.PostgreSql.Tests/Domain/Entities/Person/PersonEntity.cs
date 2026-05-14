using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person
{
    /// <summary>
    /// Canonical entity for the <c>person</c> table.
    /// <para>
    /// <b>Table Name Resolution:</b> Requires <c>[Table]</c> because <c>PersonEntity</c> normalizes to
    /// <c>personentity</c>, which does not match <c>person</c>.
    /// </para>
    /// </summary>
    [Table("person")]
    [Serializable]
    public class PersonEntity : PersistenceStateEntity
    {
        private int _id;
        private string _firstName;
        private string _middleInitial;
        private string _lastName;
        private DateTime? _birthdate;
        private string _gender;
        private DateTime _dateUtcCreated = DateTime.UtcNow;
        private DateTime _dateUtcModified = DateTime.UtcNow;

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public virtual int Id { get => _id; set => SetProperty(ref _id, value); }

        [Column("first_name")]
        [StringLength(100, MinimumLength = 1)]
        public virtual string FirstName { get => _firstName; set => SetProperty(ref _firstName, value); }

        [Column("middle_initial")]
        [StringLength(1)]
        public virtual string MiddleInitial { get => _middleInitial; set => SetProperty(ref _middleInitial, value); }

        [Column("last_name")]
        [StringLength(100, MinimumLength = 1)]
        public virtual string LastName { get => _lastName; set => SetProperty(ref _lastName, value); }

        [Column("birthdate")]
        public virtual DateTime? Birthdate { get => _birthdate; set => SetProperty(ref _birthdate, value); }

        [Column("gender")]
        [StringLength(10)]
        public virtual string Gender { get => _gender; set => SetProperty(ref _gender, value); }

        [Column("dateutc_created")]
        public virtual DateTime DateUtcCreated { get => _dateUtcCreated; set => SetProperty(ref _dateUtcCreated, value); }

        [Column("dateutc_modified")]
        public virtual DateTime DateUtcModified { get => _dateUtcModified; set => SetProperty(ref _dateUtcModified, value); }

        public Guid? UniqueId { get; set; }

        [Column("employer_id")]
        public int? EmployerId { get; set; }

        [NotMapped]
        public ICollection<Country.CountryEntity> AssociatedCountries { get; set; } = new List<Country.CountryEntity>();

        [NotMapped]
        public ICollection<PersonAddressEntity> Addresses { get; set; } = new List<PersonAddressEntity>();
    }
}
