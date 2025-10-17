using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Person;

public partial class Person
{
    public bool IsTwentyOneOrOver { get; set; }
    public string Salutation { get; set; }
}