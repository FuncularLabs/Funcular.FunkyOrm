using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Objects.User
{
    /// <summary>
    /// Test entity mapping to the <c>User</c> table.
    /// <para>
    /// <b>Table Name Resolution:</b> The <c>[Table]</c> attribute is kept here as a demonstration.
    /// Convention-based resolution would also match <c>User</c> → <c>User</c> by exact case-insensitive
    /// comparison. The table and column names (<c>Key</c>, <c>Order</c>, <c>Select</c>) are SQL reserved
    /// words, exercising quoted-identifier handling.
    /// </para>
    /// </summary>
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
