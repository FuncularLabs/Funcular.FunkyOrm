using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Person;

namespace Funcular.Data.Orm.Sqlite.Tests.Domain.Objects.Person
{
    [Serializable]
    public class PersonAddress : PersonAddressEntity
    {
        [NotMapped]
        public Person Person { get; set; }

        [NotMapped]
        public Address.Address Address { get; set; }
    }
}
