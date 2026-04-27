using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Organization
{
    /// <summary>
    /// Canonical entity for the <c>dbo.organization</c> table.
    /// <para>
    /// <b>Table Name Resolution:</b> Requires <c>[Table]</c> because <c>OrganizationEntity</c> normalizes to
    /// <c>organizationentity</c>, which does not match <c>organization</c>.
    /// </para>
    /// </summary>
    [Table("organization")]
    public class OrganizationEntity
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
