using System;
using Funcular.Data.Orm.Attributes;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.StoredProcedure
{
    /// <summary>Maps sp_get_persons_by_gender / sp_get_person_by_id / sp_search_persons result sets.</summary>
    public class PersonProcResult
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Gender { get; set; }
        public DateTime? Birthdate { get; set; }
    }

    /// <summary>Maps sp_get_projects_by_org — includes the JSON metadata column (mapped as string).</summary>
    public class ProjectProcResult
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int OrganizationId { get; set; }
        public int? LeadId { get; set; }
        public int? CategoryId { get; set; }
        public decimal? Budget { get; set; }
        public int? Score { get; set; }
        public string Metadata { get; set; }
    }

    /// <summary>[Procedure] attribute demo — the class name does not match any procedure.</summary>
    [Procedure("sp_get_persons_by_gender")]
    public class GenderFilteredPerson
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Gender { get; set; }
    }

    /// <summary>Convention inference demo — SpGetPersonById normalizes to sp_get_person_by_id.</summary>
    public class SpGetPersonById
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Gender { get; set; }
        public DateTime? Birthdate { get; set; }
    }

    /// <summary>
    /// Typed parameter class for class-based parameter passing. Property names match the procedure's
    /// parameter names directly (no snake_case conversion is applied to parameters).
    /// </summary>
    public class GenderParam
    {
        public string gender { get; set; }
    }
}
