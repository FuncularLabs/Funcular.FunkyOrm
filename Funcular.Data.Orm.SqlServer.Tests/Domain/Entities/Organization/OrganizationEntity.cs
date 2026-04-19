using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Organization
{
    [Table("organization")]
    public class OrganizationEntity : PersistenceStateEntity
    {
        public int Id { get; set; }
        public string Name { get; set; }
        
        [Column("headquarters_address_id")]
        public int? HeadquartersAddressId { get; set; }

        [Timestamp]
        [Column("row_version")]
        public byte[] RowVersion { get; set; }
    }
}
