using System.ComponentModel.DataAnnotations;

namespace Funcular.Data.Orm.Sqlite.Tests.Domain.Objects
{
    public class NonIdentityGuidEntity
    {
        [Key]
        public Guid Id { get; set; }
        public string Name { get; set; }
    }

    public class NonIdentityStringEntity
    {
        [Key]
        public string Id { get; set; }
        public string Name { get; set; }
    }
}
