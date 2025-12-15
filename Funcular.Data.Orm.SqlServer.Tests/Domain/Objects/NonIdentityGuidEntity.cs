using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Objects
{
    [Table("non_identity_guid_entity")]
    public class NonIdentityGuidEntity
    {
        [Key]
        public Guid Id { get; set; }
        public string Name { get; set; }
    }
}
