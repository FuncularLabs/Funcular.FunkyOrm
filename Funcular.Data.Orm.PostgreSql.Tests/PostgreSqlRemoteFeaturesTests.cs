using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    [TestClass]
    public class PostgreSqlRemoteFeaturesTests : PostgreSqlDataProviderIntegrationTests
    {
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
    }
}
