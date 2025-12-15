using System.Linq;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    [TestClass]
    public class RemoteKeyWhereTests : SqlDataProviderIntegrationTests
    {
        [TestMethod]
        public void CanFilterByRemoteKey()
        {
            // Arrange
            // We need a person with a known Employer Country ID.
            // Assuming the integration test data has some people.
            // Let's fetch one first to get a valid ID.
            var person = _provider.Query<PersonDetailEntity>().FirstOrDefault(p => p.EmployerHeadquartersCountryId != null);
            
            if (person == null)
            {
                Assert.Inconclusive("No person with EmployerHeadquartersCountryId found in test database.");
            }

            int targetCountryId = person.EmployerHeadquartersCountryId.Value;

            // Act
            // Filter by the Remote Key
            var results = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryId == targetCountryId)
                .ToList();

            // Assert
            Assert.IsTrue(results.Count > 0);
            Assert.IsTrue(results.All(p => p.EmployerHeadquartersCountryId == targetCountryId));
        }

        [TestMethod]
        public void CanFilterByRemoteProperty()
        {
            // Arrange
            var person = _provider.Query<PersonDetailEntity>().FirstOrDefault(p => p.EmployerHeadquartersCountryName != null);
            
            if (person == null)
            {
                Assert.Inconclusive("No person with EmployerHeadquartersCountryName found in test database.");
            }

            string targetCountryName = person.EmployerHeadquartersCountryName;

            // Act
            var results = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName == targetCountryName)
                .ToList();

            // Assert
            Assert.IsTrue(results.Count > 0);
            Assert.IsTrue(results.All(p => p.EmployerHeadquartersCountryName == targetCountryName));
        }
    }
}
