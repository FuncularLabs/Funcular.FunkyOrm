using System;
using System.ComponentModel.DataAnnotations;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Objects
{
    /// <summary>
    /// Test entity with a non-identity GUID primary key — maps to <c>dbo.non_identity_guid_entity</c>.
    /// <para>
    /// <b>Table Name Resolution:</b> No <c>[Table]</c> attribute is needed. Convention-based resolution
    /// normalizes <c>NonIdentityGuidEntity</c> → <c>nonidentityguidentiity</c> and
    /// <c>non_identity_guid_entity</c> → <c>nonidentityguidentiity</c> — these match via
    /// <see cref="Funcular.Data.Orm.Core.Utilities.IgnoreUnderscoreAndCaseStringComparer"/>.
    /// </para>
    /// </summary>
    public class NonIdentityGuidEntity
    {
        [Key]
        public Guid Id { get; set; }
        public string Name { get; set; }
    }
}
