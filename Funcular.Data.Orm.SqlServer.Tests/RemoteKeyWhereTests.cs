using System.Linq;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    [TestClass]
    public class RemoteKeyWhereTests : SqlServerTestFixture
    {
        [TestMethod]
        public void CanFilterByRemoteKey()
        {
            // Arrange
            var seeded = SeedEmployerChain("WhereKey_");
            var person = _provider.Query<PersonDetailEntity>()
                .FirstOrDefault(p => p.Id == seeded.PersonId && p.EmployerHeadquartersCountryId != null);
            Assert.IsNotNull(person, "The seeded person's EmployerHeadquartersCountryId should be populated.");
            Assert.AreEqual(seeded.CountryId, person.EmployerHeadquartersCountryId);

            int targetCountryId = person.EmployerHeadquartersCountryId.Value;

            // Act
            // Filter by the Remote Key
            var results = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryId == targetCountryId)
                .ToList();

            // Assert
            Assert.IsTrue(results.Any(p => p.Id == seeded.PersonId));
            Assert.IsTrue(results.All(p => p.EmployerHeadquartersCountryId == targetCountryId));
        }

        [TestMethod]
        public void CanFilterByRemoteProperty()
        {
            // Arrange
            var seeded = SeedEmployerChain("WhereProperty_");
            var person = _provider.Query<PersonDetailEntity>()
                .FirstOrDefault(p => p.Id == seeded.PersonId && p.EmployerHeadquartersCountryName != null);
            Assert.IsNotNull(person, "The seeded person's EmployerHeadquartersCountryName should be populated.");
            Assert.AreEqual(seeded.CountryName, person.EmployerHeadquartersCountryName);

            string targetCountryName = person.EmployerHeadquartersCountryName;

            // Act
            var results = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName == targetCountryName)
                .ToList();

            // Assert
            Assert.IsTrue(results.Any(p => p.Id == seeded.PersonId));
            Assert.IsTrue(results.All(p => p.EmployerHeadquartersCountryName == targetCountryName));
        }
    }
}
