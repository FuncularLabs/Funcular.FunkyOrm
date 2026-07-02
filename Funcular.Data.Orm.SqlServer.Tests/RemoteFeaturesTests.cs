using System;
using System.Linq;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    [TestClass]
    public class RemoteFeaturesTests : SqlServerTestFixture
    {
        // Seed one full forward chain (Country → Address → Organization → Person) so a person's
        // [RemoteProperty] EmployerHeadquartersCountryName is non-null — keeps the aggregate tests below
        // non-vacuous (they'd otherwise compare 0 == 0 when run without other seeders).
        private string SeedForwardRemoteChain()
        {
            var country = new CountryEntity { Name = "AggCountry_" + Guid.NewGuid() };
            _provider.Insert(country);
            var address = new AddressEntity { Line1 = "1 Agg St", City = "AggCity", StateCode = "NY", PostalCode = "10001", CountryId = country.Id };
            _provider.Insert(address);
            var org = new OrganizationEntity { Name = "AggOrg_" + Guid.NewGuid(), HeadquartersAddressId = address.Id };
            _provider.Insert(org);
            var person = new PersonEntity { FirstName = "Agg", LastName = "Test_" + Guid.NewGuid().ToString().Substring(0, 8), EmployerId = org.Id };
            _provider.Insert(person);
            return country.Name; // unique marker so tests can filter to just this seeded chain
        }

        [TestMethod]
        public void RemoteProperty_IsPopulated_OnQuery()
        {
            // Arrange
            // Find a person who should have an employer with a headquarters address and country.
            // We rely on existing test data.
            
            // Act
            var person = _provider.Query<PersonDetailEntity>()
                .FirstOrDefault(p => p.EmployerHeadquartersCountryName != null);

            // Assert
            if (person == null)
            {
                Assert.Inconclusive("Could not find a person with a populated EmployerHeadquartersCountryName in the test database. Please ensure test data is seeded.");
            }

            Assert.IsNotNull(person.EmployerHeadquartersCountryName, "Remote property should be populated.");
            Assert.IsTrue(person.EmployerHeadquartersCountryName.Length > 0, "Remote property should not be empty.");
        }

        [TestMethod]
        public void OrderBy_RemoteProperty_OrdersByJoinedColumn()
        {
            // Ordering by a [RemoteProperty] sorts by the joined alias.column (the join is already in the SELECT).
            // Core guarantee (order-independent): the query produces VALID, executable SQL — a broken resolver would
            // emit an unjoined alias and ToList() would throw. This is asserted always, regardless of seeded rows.
            var asc = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null)
                .OrderBy(p => p.EmployerHeadquartersCountryName)
                .ToList();
            var desc = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null)
                .OrderByDescending(p => p.EmployerHeadquartersCountryName)
                .ToList();
            Assert.IsNotNull(asc);
            Assert.IsNotNull(desc);
            Assert.AreEqual(asc.Count, desc.Count);
            if (asc.Count >= 2)
            {
                // DESC ordering of the remote column must be the exact reverse of ASC — checks the WHOLE order
                // (not just endpoints) and is collation- and tie-agnostic (equal names are interchangeable).
                var ascNames = asc.Select(r => r.EmployerHeadquartersCountryName).ToList();
                var descNames = desc.Select(r => r.EmployerHeadquartersCountryName).ToList();
                ascNames.Reverse();
                CollectionAssert.AreEqual(ascNames, descNames,
                    "DESC ordering of a [RemoteProperty] should be the exact reverse of ASC.");
            }
        }

        [TestMethod]
        public void Count_FilteredByRemoteProperty_MatchesMaterialized()
        {
            SeedForwardRemoteChain();
            // Regression: aggregate over a [RemoteProperty] filter previously omitted the JOIN → SqlException 4104.
            // Compare to the materialized count: proves the join is emitted (no 4104) AND is 1:1 (correct count).
            var expected = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null).ToList().Count;
            var actual = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null).Count();
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Count_WithRemotePropertyPredicate_MatchesMaterialized()
        {
            SeedForwardRemoteChain();
            // The predicate-inside-aggregate form: Count(p => remoteProp ...).
            var expected = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null).ToList().Count;
            var actual = _provider.Query<PersonDetailEntity>()
                .Count(p => p.EmployerHeadquartersCountryName != null);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Any_FilteredByRemoteProperty_MatchesMaterialized()
        {
            SeedForwardRemoteChain();
            var expected = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null).ToList().Any();
            var actual = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null).Any();
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void All_WithRemotePropertyPredicate_MatchesMaterialized()
        {
            SeedForwardRemoteChain();
            var expected = _provider.Query<PersonDetailEntity>().ToList()
                .All(p => p.EmployerHeadquartersCountryName != null);
            var actual = _provider.Query<PersonDetailEntity>()
                .All(p => p.EmployerHeadquartersCountryName != null);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Sum_FilteredByRemoteProperty_MatchesMaterialized()
        {
            // Numeric-aggregate branch, filtered by a [RemoteProperty]: the JOIN must be present for the WHERE.
            // Filter to THIS seeded chain's unique country so SUM(id) stays bounded (summing all matching rows
            // over a large/accumulated person table would overflow int32 — a test artifact, not a code issue).
            var country = SeedForwardRemoteChain();
            var expected = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName == country).ToList().Sum(p => p.Id);
            var actual = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName == country).Sum(p => p.Id);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Select_ScalarProjection_ThrowsClearNotSupported()
        {
            // BUG B (3.8.3): a top-level projection to a scalar isn't materialized. Fail with a clear message
            // rather than the old obscure InvalidCastException.
            var ex = Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<PersonDetailEntity>().Select(p => p.Id).ToList());
            StringAssert.Contains(ex.Message, "top-level Select");
            StringAssert.Contains(ex.Message, "ToList");
        }

        [TestMethod]
        public void Select_AnonymousProjection_ThrowsClearNotSupported()
        {
            Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<PersonDetailEntity>().Select(p => new { p.Id }).ToList());
        }

        [TestMethod]
        public void Select_RemoteProperty_InCustomProjection_Throws()
        {
            // A [RemoteProperty] resolves to alias.column and needs a JOIN a custom projection's FROM doesn't
            // carry, so projecting it is rejected with a clear error (unlike self-contained computed attrs).
            Assert.ThrowsException<System.NotSupportedException>(() =>
                _provider.Query<PersonDetailEntity>()
                    .Select(p => new PersonDetailEntity { EmployerHeadquartersCountryName = p.EmployerHeadquartersCountryName })
                    .ToList());
        }

        [TestMethod]
        public void RemoteKey_IsPopulated_OnQuery()
        {
            // Arrange
            
            // Act
            var person = _provider.Query<PersonDetailEntity>()
                .FirstOrDefault(p => p.EmployerHeadquartersCountryId != null);

            // Assert
            if (person == null)
            {
                Assert.Inconclusive("Could not find a person with a populated EmployerHeadquartersCountryId in the test database.");
            }

            Assert.IsNotNull(person.EmployerHeadquartersCountryId, "Remote key should be populated.");
            Assert.IsTrue(person.EmployerHeadquartersCountryId > 0, "Remote key should be valid.");
        }

        [TestMethod]
        public void CanFilterBy_RemoteProperty()
        {
            // Arrange
            var person = _provider.Query<PersonDetailEntity>().FirstOrDefault(p => p.EmployerHeadquartersCountryName != null);
            if (person == null) Assert.Inconclusive("No test data.");
            var targetName = person.EmployerHeadquartersCountryName;

            // Act
            var results = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName == targetName)
                .ToList();

            // Assert
            Assert.IsTrue(results.Count > 0);
            Assert.IsTrue(results.All(p => p.EmployerHeadquartersCountryName == targetName));
        }
    }
}
