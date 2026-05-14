using System;
using System.ComponentModel.DataAnnotations;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Objects
{
    /// <summary>
    /// Test entity with a non-identity string primary key — maps to <c>dbo.non_identity_string_entity</c>.
    /// <para>
    /// <b>Table Name Resolution:</b> No <c>[Table]</c> attribute is needed. Convention-based resolution
    /// normalizes <c>NonIdentityStringEntity</c> and <c>non_identity_string_entity</c> to the same value
    /// via <see cref="Funcular.Data.Orm.Core.Utilities.IgnoreUnderscoreAndCaseStringComparer"/>.
    /// </para>
    /// </summary>
    public class NonIdentityStringEntity
    {
        [Key]
        public string Id { get; set; }
        public string Name { get; set; }
    }
}
