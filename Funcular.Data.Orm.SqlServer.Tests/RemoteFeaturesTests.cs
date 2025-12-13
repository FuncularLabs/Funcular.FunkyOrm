using System.Linq;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    [TestClass]
    public class RemoteFeaturesTests : SqlDataProviderIntegrationTests
    {
        [TestMethod]
        public void RemoteProperty_IsPopulated_OnQuery()
        {
            // Arrange
            // Find a person who should have an employer with a headquarters address and country.
            // We rely on existing test data.
            
            // Act
            var person = _provider.Query<PersonEntity>()
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
        public void RemoteKey_IsPopulated_OnQuery()
        {
            // Arrange
            
            // Act
            var person = _provider.Query<PersonEntity>()
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
            var person = _provider.Query<PersonEntity>().FirstOrDefault(p => p.EmployerHeadquartersCountryName != null);
            if (person == null) Assert.Inconclusive("No test data.");
            var targetName = person.EmployerHeadquartersCountryName;

            // Act
            var results = _provider.Query<PersonEntity>()
                .Where(p => p.EmployerHeadquartersCountryName == targetName)
                .ToList();

            // Assert
            Assert.IsTrue(results.Count > 0);
            Assert.IsTrue(results.All(p => p.EmployerHeadquartersCountryName == targetName));
        }
    }
}
