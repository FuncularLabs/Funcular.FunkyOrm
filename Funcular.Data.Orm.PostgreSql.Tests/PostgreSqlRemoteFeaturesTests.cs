using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Address;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    [TestClass]
    public class PostgreSqlRemoteFeaturesTests : PostgreSqlTestFixture
    {
        // Seed one full forward chain (Country → Address → Organization → Person) so a person's
        // [RemoteProperty] EmployerHeadquartersCountryName is non-null — keeps the aggregate tests below
        // non-vacuous (they'd otherwise compare 0 == 0 when run without other seeders).
        private void SeedForwardRemoteChain()
        {
            var country = new CountryEntity { Name = "AggCountry_" + Guid.NewGuid() };
            _provider.Insert(country);
            var address = new AddressEntity { Line1 = "1 Agg St", City = "AggCity", StateCode = "NY", PostalCode = "10001", CountryId = country.Id };
            _provider.Insert(address);
            var org = new OrganizationEntity { Name = "AggOrg_" + Guid.NewGuid(), HeadquartersAddressId = address.Id };
            _provider.Insert(org);
            var person = new PersonEntity { FirstName = "Agg", LastName = "Test_" + Guid.NewGuid().ToString().Substring(0, 8), EmployerId = org.Id };
            _provider.Insert(person);
        }

        [TestMethod]
        public void RemoteProperty_IsPopulated_OnQuery()
        {
            var person = _provider.Query<PersonDetailEntity>()
                .FirstOrDefault(p => p.EmployerHeadquartersCountryName != null);

            if (person == null)
                Assert.Inconclusive("Could not find a person with a populated EmployerHeadquartersCountryName. Ensure test data is seeded.");

            Assert.IsNotNull(person.EmployerHeadquartersCountryName);
            Assert.IsTrue(person.EmployerHeadquartersCountryName.Length > 0);
        }

        [TestMethod]
        public void RemoteKey_IsPopulated_OnQuery()
        {
            var person = _provider.Query<PersonDetailEntity>()
                .FirstOrDefault(p => p.EmployerHeadquartersCountryId != null);

            if (person == null)
                Assert.Inconclusive("Could not find a person with a populated EmployerHeadquartersCountryId.");

            Assert.IsNotNull(person.EmployerHeadquartersCountryId);
            Assert.IsTrue(person.EmployerHeadquartersCountryId > 0);
        }

        [TestMethod]
        public void CanFilterBy_RemoteProperty()
        {
            var person = _provider.Query<PersonDetailEntity>()
                .FirstOrDefault(p => p.EmployerHeadquartersCountryName != null);
            if (person == null) Assert.Inconclusive("No test data.");
            var targetName = person.EmployerHeadquartersCountryName;

            var results = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName == targetName)
                .ToList();

            Assert.IsTrue(results.Count > 0);
            Assert.IsTrue(results.All(p => p.EmployerHeadquartersCountryName == targetName));
        }

        [TestMethod]
        public void OrderBy_RemoteProperty_OrdersByJoinedColumn()
        {
            var asc = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null)
                .OrderBy(p => p.EmployerHeadquartersCountryName).ToList();
            var desc = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null)
                .OrderByDescending(p => p.EmployerHeadquartersCountryName).ToList();
            Assert.IsNotNull(asc);
            Assert.IsNotNull(desc);
            Assert.AreEqual(asc.Count, desc.Count);
            if (asc.Count >= 2)
            {
                var ascNames = asc.Select(r => r.EmployerHeadquartersCountryName).ToList();
                var descNames = desc.Select(r => r.EmployerHeadquartersCountryName).ToList();
                ascNames.Reverse();
                CollectionAssert.AreEqual(ascNames, descNames,
                    "DESC ordering of a [RemoteProperty] should be the exact reverse of ASC.");
            }
        }

        [TestMethod]
        public void Select_RemoteProperty_InCustomProjection_Throws()
        {
            // A [RemoteProperty] requires a join a custom projection's FROM does not carry — reject clearly.
            Assert.ThrowsException<System.NotSupportedException>(() =>
                _provider.Query<PersonDetailEntity>()
                    .Select(p => new PersonDetailEntity { EmployerHeadquartersCountryName = p.EmployerHeadquartersCountryName })
                    .ToList());
        }

        [TestMethod]
        public void Count_FilteredByRemoteProperty_MatchesMaterialized()
        {
            SeedForwardRemoteChain();
            var expected = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).ToList().Count;
            var actual = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).Count();
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Count_WithRemotePropertyPredicate_MatchesMaterialized()
        {
            SeedForwardRemoteChain();
            var expected = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).ToList().Count;
            var actual = _provider.Query<PersonDetailEntity>().Count(p => p.EmployerHeadquartersCountryName != null);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Any_FilteredByRemoteProperty_MatchesMaterialized()
        {
            SeedForwardRemoteChain();
            var expected = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).ToList().Any();
            var actual = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).Any();
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void All_WithRemotePropertyPredicate_MatchesMaterialized()
        {
            SeedForwardRemoteChain();
            var expected = _provider.Query<PersonDetailEntity>().ToList().All(p => p.EmployerHeadquartersCountryName != null);
            var actual = _provider.Query<PersonDetailEntity>().All(p => p.EmployerHeadquartersCountryName != null);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Sum_FilteredByRemoteProperty_MatchesMaterialized()
        {
            SeedForwardRemoteChain();
            var expected = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).ToList().Sum(p => p.Id);
            var actual = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).Sum(p => p.Id);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Select_ScalarProjection_ThrowsClearNotSupported()
        {
            // BUG B: a top-level projection to a scalar isn't materialized. Fail with a clear message
            // rather than the old obscure InvalidCastException.
            var ex = Assert.ThrowsException<System.NotSupportedException>(() =>
                _provider.Query<PersonDetailEntity>().Select(p => p.Id).ToList());
            StringAssert.Contains(ex.Message, "top-level Select");
            StringAssert.Contains(ex.Message, "ToList");
        }

        [TestMethod]
        public void Select_AnonymousProjection_ThrowsClearNotSupported()
        {
            Assert.ThrowsException<System.NotSupportedException>(() =>
                _provider.Query<PersonDetailEntity>().Select(p => new { p.Id }).ToList());
        }
    }
}
