using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Person;

public partial class Person
{
    [NotMapped] public bool IsTwentyOneOrOver { get; set; }
    [NotMapped] public string Salutation { get; set; }
}