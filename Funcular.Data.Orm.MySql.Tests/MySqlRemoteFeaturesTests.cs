using System;
using System.Linq;
using Funcular.Data.Orm.MySql.Tests.Domain;

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>
    /// Validates [RemoteLink]/[RemoteProperty]/[RemoteKey] — automatic LEFT JOIN generation,
    /// remote value/key population, and WHERE filtering on remote properties (person -> organization).
    /// </summary>
    [TestClass]
    public class MySqlRemoteFeaturesTests : MySqlTestFixture
    {
        [TestInitialize]
        public void Setup() => InitProvider();

        [TestCleanup]
        public void Cleanup() => DisposeProvider();

        private (int orgId, int personId, string orgName) SeedPersonWithEmployer()
        {
            var orgName = "Org" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var org = new Organization { Name = orgName };
            _provider.Insert(org);

            var person = new PersonWithEmployer
            {
                FirstName = "Emp",
                LastName = "Loyee",
                EmployerId = org.Id,
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow
            };
            _provider.Insert(person);
            return (org.Id, person.Id, orgName);
        }

        [TestMethod]
        public void RemoteProperty_And_RemoteKey_PopulateFromJoin()
        {
            var (orgId, personId, orgName) = SeedPersonWithEmployer();

            var fetched = _provider.Get<PersonWithEmployer>(personId);
            Assert.IsNotNull(fetched);
            Assert.AreEqual(orgName, fetched.EmployerName, "RemoteProperty should be populated from the joined organization.");
            Assert.AreEqual(orgId, fetched.EmployerOrgId, "RemoteKey should be populated with the joined organization id.");
        }

        [TestMethod]
        public void RemoteProperty_WhereFilter_GeneratesJoinAndFilters()
        {
            var (_, _, orgName) = SeedPersonWithEmployer();

            var results = _provider.Query<PersonWithEmployer>()
                .Where(p => p.EmployerName == orgName)
                .ToList();

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(orgName, results[0].EmployerName);
        }

        [TestMethod]
        public void RemoteKey_WhereFilter_FiltersByJoinedId()
        {
            var (orgId, _, _) = SeedPersonWithEmployer();

            var results = _provider.Query<PersonWithEmployer>()
                .Where(p => p.EmployerOrgId == orgId)
                .ToList();

            Assert.IsTrue(results.Count >= 1);
            Assert.IsTrue(results.All(r => r.EmployerOrgId == orgId));
        }

        [TestMethod]
        public void RemoteProperty_IsNull_WhenNoForeignKey()
        {
            var person = new PersonWithEmployer
            {
                FirstName = "Orphan",
                LastName = "NoEmployer" + Guid.NewGuid().ToString("N").Substring(0, 8),
                EmployerId = null,
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow
            };
            _provider.Insert(person);

            var fetched = _provider.Get<PersonWithEmployer>(person.Id);
            Assert.IsNotNull(fetched);
            Assert.IsNull(fetched.EmployerName, "LEFT JOIN with no FK should yield a null remote property.");
        }

        [TestMethod]
        public void Select_ScalarProjection_ReturnsListOfScalar()
        {
            // v3.9: a top-level scalar projection Select(x => x.Member) now returns List<memberType>.
            var ids = _provider.Query<PersonWithEmployer>().Select(p => p.Id).ToList();
            Assert.IsInstanceOfType(ids, typeof(System.Collections.Generic.List<int>));
        }

        [TestMethod]
        public void Select_AnonymousProjection_ThrowsClearNotSupported()
        {
            Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<PersonWithEmployer>().Select(p => new { p.Id }).ToList());
        }

        [TestMethod]
        public void GroupBy_IsNotTranslated_ThrowsNotSupported()
        {
            var ex = Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<PersonWithEmployer>().GroupBy(p => p.Id).ToList());
            StringAssert.Contains(ex.Message, "GroupBy");
        }
    }
}
