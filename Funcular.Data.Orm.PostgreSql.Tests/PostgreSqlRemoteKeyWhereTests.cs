using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    [TestClass]
    public class PostgreSqlRemoteKeyWhereTests : PostgreSqlDataProviderIntegrationTests
    {
        [TestMethod]
        public void CanFilterByRemoteKey()
        {
            var person = _provider.Query<PersonDetailEntity>()
                .FirstOrDefault(p => p.EmployerHeadquartersCountryId != null);

            if (person == null)
                Assert.Inconclusive("No person with EmployerHeadquartersCountryId found in test database.");

            int targetCountryId = person.EmployerHeadquartersCountryId.Value;

            var results = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryId == targetCountryId)
                .ToList();

            Assert.IsTrue(results.Count > 0);
            Assert.IsTrue(results.All(p => p.EmployerHeadquartersCountryId == targetCountryId));
        }

        [TestMethod]
        public void CanFilterByRemoteProperty()
        {
            var person = _provider.Query<PersonDetailEntity>()
                .FirstOrDefault(p => p.EmployerHeadquartersCountryName != null);

            if (person == null)
                Assert.Inconclusive("No person with EmployerHeadquartersCountryName found in test database.");

            string targetCountryName = person.EmployerHeadquartersCountryName;

            var results = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName == targetCountryName)
                .ToList();

            Assert.IsTrue(results.Count > 0);
            Assert.IsTrue(results.All(p => p.EmployerHeadquartersCountryName == targetCountryName));
        }
    }
}
