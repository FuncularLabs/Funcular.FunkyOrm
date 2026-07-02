using System.Linq;
using System.Text;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    /// <summary>
    /// Locks the blessed narrow-projection idiom (3.8.5): `Select(x => new T { Key = x.Key })` on a wide entity
    /// emits a NARROW `SELECT` of only the projected column(s) — deferring the unprojected computed/remote columns
    /// until after the Top-N — and composes with `WHERE` + `ORDER BY` (a `[RemoteProperty]` join column) + paging.
    /// This is the hot-path idiom for "filter/order/page by any mapped column, read back only the key".
    /// </summary>
    [TestClass]
    public class NarrowProjectionTests : SqlServerTestFixture
    {
        [TestMethod]
        public void SameEntitySubset_OrderedByRemote_Paged_EmitsNarrowSelect()
        {
            var sb = new StringBuilder();
            _provider.Log = s => sb.AppendLine(s);

            var rows = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null)
                .OrderByDescending(p => p.EmployerHeadquartersCountryName)
                .Skip(0).Take(25)
                .Select(p => new PersonDetailEntity { Id = p.Id })
                .ToList();

            var sql = sb.ToString();
            // Narrow: only the projected key column is selected.
            StringAssert.Contains(sql, "person.id AS Id");
            // Unprojected wide columns must NOT be in the SELECT list.
            Assert.IsFalse(sql.Contains("first_name"), "projection must be narrow — no unprojected own columns");
            Assert.IsFalse(sql.Contains("birthdate"), "projection must be narrow — no unprojected own columns");
            // Composes with the full clause stack: WHERE + ORDER BY (remote join column) + OFFSET/FETCH.
            StringAssert.Contains(sql, "ORDER BY");
            StringAssert.Contains(sql, "OFFSET");
            StringAssert.Contains(sql, "FETCH NEXT");
            Assert.IsTrue(rows.All(r => r.Id > 0));
        }
    }
}
