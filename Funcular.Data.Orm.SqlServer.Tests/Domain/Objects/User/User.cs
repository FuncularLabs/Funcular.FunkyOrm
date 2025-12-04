using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.User
{
    [Table("User")]
    public class User
    {
        [Key]
        [Column("Key")]
        public int Key { get; set; }

        [Column("Name")]
        public string Name { get; set; }

        [Column("Order")]
        public int Order { get; set; }

        [Column("Select")]
        public bool Select { get; set; }
    }
}
