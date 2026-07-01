using System.Linq;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    [TestClass]
    public class RemoteFeaturesTests : SqlServerTestFixture
    {
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
            var expected = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null).ToList().Any();
            var actual = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null).Any();
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void All_WithRemotePropertyPredicate_MatchesMaterialized()
        {
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
            var expected = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null).ToList().Sum(p => p.Id);
            var actual = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null).Sum(p => p.Id);
            Assert.AreEqual(expected, actual);
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
