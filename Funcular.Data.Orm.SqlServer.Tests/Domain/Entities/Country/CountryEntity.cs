using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Country
{
    [Table("country")]
    public class CountryEntity : PersistenceStateEntity
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
