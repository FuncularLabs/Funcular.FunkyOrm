using System.ComponentModel.DataAnnotations;

namespace Funcular.Data.Orm.PostgreSql.Tests.Domain.Objects
{
    /// <summary>
    /// Test entity with a non-identity GUID primary key — maps to <c>non_identity_guid_entity</c>.
    /// <para>
    /// <b>Table Name Resolution:</b> No <c>[Table]</c> attribute is needed. Convention-based resolution
    /// matches <c>NonIdentityGuidEntity</c> to <c>non_identity_guid_entity</c> via
    /// <see cref="Funcular.Data.Orm.Core.Utilities.IgnoreUnderscoreAndCaseStringComparer"/>.
    /// </para>
    /// </summary>
    public class NonIdentityGuidEntity
    {
        [Key]
        public Guid Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Test entity with a non-identity string primary key — maps to <c>non_identity_string_entity</c>.
    /// <para>
    /// <b>Table Name Resolution:</b> No <c>[Table]</c> attribute is needed. Convention-based resolution
    /// matches <c>NonIdentityStringEntity</c> to <c>non_identity_string_entity</c> via
    /// <see cref="Funcular.Data.Orm.Core.Utilities.IgnoreUnderscoreAndCaseStringComparer"/>.
    /// </para>
    /// </summary>
    public class NonIdentityStringEntity
    {
        [Key]
        public string Id { get; set; }
        public string Name { get; set; }
    }
}
