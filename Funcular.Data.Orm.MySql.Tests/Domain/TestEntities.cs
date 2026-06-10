using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;

namespace Funcular.Data.Orm.MySql.Tests.Domain
{
    // Convention-mapped POCOs. FunkyORM's IgnoreUnderscoreAndCaseStringComparer maps
    // PascalCase property names to snake_case columns (FirstName -> first_name,
    // Line1 -> line_1, DateUtcCreated -> dateutc_created), so no [Column] attributes are needed.

    [Table("person")]
    public class Person
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string MiddleInitial { get; set; }
        public string LastName { get; set; }
        public DateTime? Birthdate { get; set; }
        public string Gender { get; set; }
        public Guid? UniqueId { get; set; }
        public int? EmployerId { get; set; }
        public DateTime DateUtcCreated { get; set; }
        public DateTime DateUtcModified { get; set; }
    }

    [Table("address")]
    public class Address
    {
        public int Id { get; set; }
        public string Line1 { get; set; }
        public string Line2 { get; set; }
        public string City { get; set; }
        public string StateCode { get; set; }
        public string PostalCode { get; set; }
        public int? CountryId { get; set; }
        public DateTime DateUtcCreated { get; set; }
        public DateTime DateUtcModified { get; set; }
    }

    [Table("organization")]
    public class Organization
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int? HeadquartersAddressId { get; set; }
        // row_version (TIMESTAMP, auto-managed) is intentionally not mapped.
    }

    [Table("non_identity_guid_entity")]
    public class NonIdentityGuidEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }

    [Table("non_identity_string_entity")]
    public class NonIdentityStringEntity
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Detail entity over the person table that projects the employer's name/id via remote
    /// joins. EmployerId carries [RemoteLink] so the resolver knows it points to Organization
    /// (the name "Employer" is not type-inferable). EmployerName/EmployerOrgId are unmapped and
    /// populated from a generated LEFT JOIN.
    /// </summary>
    [Table("person")]
    public class PersonWithEmployer
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }

        [RemoteLink(typeof(Organization))]
        public int? EmployerId { get; set; }

        [RemoteProperty(typeof(Organization), nameof(Organization.Name))]
        public string EmployerName { get; set; }

        [RemoteKey(typeof(Organization), nameof(Organization.Id))]
        public int? EmployerOrgId { get; set; }

        public DateTime DateUtcCreated { get; set; }
        public DateTime DateUtcModified { get; set; }
    }

    /// <summary>
    /// Maps the reserved-word table `user` with reserved columns (`key`, `order`, `select`).
    /// Exercises backtick identifier quoting. Convention maps Key->key, Order->order, Select->select.
    /// </summary>
    [Table("user")]
    public class User
    {
        [Key]
        public int Key { get; set; }
        public string Name { get; set; }
        public int Order { get; set; }
        public int Select { get; set; }
    }
}
