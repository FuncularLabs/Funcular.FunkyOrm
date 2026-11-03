using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    [TestClass]
    public class PostgreSqlRemoteKeyWhereTests : PostgreSqlTestFixture
    {
        [TestMethod]
        public void CanFilterByRemoteKey()
        {
            var seeded = SeedEmployerChain("WhereKey_");
            var person = _provider.Query<PersonDetailEntity>()
                .FirstOrDefault(p => p.Id == seeded.PersonId && p.EmployerHeadquartersCountryId != null);
            Assert.IsNotNull(person, "The seeded person's EmployerHeadquartersCountryId should be populated.");
            Assert.AreEqual(seeded.CountryId, person.EmployerHeadquartersCountryId);

            int targetCountryId = person.EmployerHeadquartersCountryId.Value;

            var results = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryId == targetCountryId)
                .ToList();

            Assert.IsTrue(results.Any(p => p.Id == seeded.PersonId));
            Assert.IsTrue(results.All(p => p.EmployerHeadquartersCountryId == targetCountryId));
        }

        [TestMethod]
        public void CanFilterByRemoteProperty()
        {
            var seeded = SeedEmployerChain("WhereProperty_");
            var person = _provider.Query<PersonDetailEntity>()
                .FirstOrDefault(p => p.Id == seeded.PersonId && p.EmployerHeadquartersCountryName != null);
            Assert.IsNotNull(person, "The seeded person's EmployerHeadquartersCountryName should be populated.");
            Assert.AreEqual(seeded.CountryName, person.EmployerHeadquartersCountryName);

            string targetCountryName = person.EmployerHeadquartersCountryName;

            var results = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName == targetCountryName)
                .ToList();

            Assert.IsTrue(results.Any(p => p.Id == seeded.PersonId));
            Assert.IsTrue(results.All(p => p.EmployerHeadquartersCountryName == targetCountryName));
        }
    }
}
