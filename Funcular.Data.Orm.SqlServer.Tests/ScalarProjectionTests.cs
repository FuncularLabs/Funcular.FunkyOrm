using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    /// <summary>
    /// Top-level scalar projection (v3.9): `Select(x => x.Member)` returns `List&lt;memberType&gt;` via a narrow
    /// SELECT of just that member + in-memory projection. Composes with WHERE/ORDER BY(remote)/paging.
    /// </summary>
    [TestClass]
    public class ScalarProjectionTests : SqlServerTestFixture
    {
        [TestMethod]
        public void ScalarProjection_OwnColumn_ReturnsListOfScalar_NarrowSelect()
        {
            var sb = new StringBuilder();
            _provider.Log = s => sb.AppendLine(s);

            List<int> ids = _provider.Query<PersonDetailEntity>()
                .Where(p => p.Id > 0)
                .Select(p => p.Id)
                .ToList();

            var sql = sb.ToString();
            StringAssert.Contains(sql, "person.id AS Id");
            Assert.IsFalse(sql.Contains("first_name"), "scalar projection must emit a narrow SELECT");
            Assert.IsInstanceOfType(ids, typeof(List<int>));
            Assert.IsTrue(ids.All(i => i > 0));
        }

        [TestMethod]
        public void ScalarProjection_RemoteColumn_Ordered_Paged_ReturnsListOfKey()
        {
            // The motivating case: filter + order by a joined column, page, project only the key.
            var sb = new StringBuilder();
            _provider.Log = s => sb.AppendLine(s);

            List<int> ids = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null)
                .OrderByDescending(p => p.EmployerHeadquartersCountryName)
                .Skip(0).Take(25)
                .Select(p => p.Id)
                .ToList();

            var sql = sb.ToString();
            StringAssert.Contains(sql, "person.id AS Id");
            StringAssert.Contains(sql, "ORDER BY");
            StringAssert.Contains(sql, "OFFSET");
            Assert.IsInstanceOfType(ids, typeof(List<int>));
        }

        [TestMethod]
        public void ScalarProjection_ComputedColumn_ReturnsListOfScalar()
        {
            // Self-contained computed attrs ([JsonPath] here) project fine — same as the same-entity subset form.
            var priorities = _provider.Query<Domain.Entities.Project.ProjectScorecard>()
                .Select(p => p.Priority)   // [JsonPath("metadata", "$.priority")]
                .ToList();
            Assert.IsInstanceOfType(priorities, typeof(List<string>));
        }

        [TestMethod]
        public void ScalarProjection_RemoteColumn_ThrowsNotSupported()
        {
            // Projecting a [RemoteProperty] VALUE isn't supported (it needs a join a projection's FROM can't
            // carry) — consistent with the same-entity subset limitation. Order/filter by it and project the key.
            Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<PersonDetailEntity>()
                    .Select(p => p.EmployerHeadquartersCountryName)
                    .ToList());
        }

        [TestMethod]
        public void AnonymousProjection_StillThrows()
        {
            Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<PersonDetailEntity>().Select(p => new { p.Id }).ToList());
        }
    }
}
