using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Address;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    [TestClass]
    public class PostgreSqlRemoteKeyIntegrationTests : PostgreSqlDataProviderIntegrationTests
    {
        [TestMethod]
        public async Task RemoteKey_FullChainPopulation_ImplicitAndExplicit()
        {
            OutputTestMethodName();

            var country = new CountryEntity { Name = "TestCountry_" + Guid.NewGuid() };
            await _provider.InsertAsync(country);

            var address = new AddressEntity
            {
                Line1 = "123 HQ St", City = "Metropolis", StateCode = "NY", PostalCode = "10001",
                CountryId = country.Id
            };
            await _provider.InsertAsync(address);

            var org = new OrganizationEntity
            {
                Name = "TestOrg_" + Guid.NewGuid(),
                HeadquartersAddressId = address.Id
            };
            await _provider.InsertAsync(org);

            var person = new PersonEntity
            {
                FirstName = "John", LastName = "Doe", EmployerId = org.Id
            };
            await _provider.InsertAsync(person);

            var fetchedPerson = await _provider.GetAsync<PersonDetailEntity>(person.Id);

            Assert.IsNotNull(fetchedPerson);
            Assert.AreEqual(country.Name, fetchedPerson.EmployerHeadquartersCountryName, "Implicit mode failed");
            Assert.AreEqual(country.Name, fetchedPerson.ExplicitCountryName, "Explicit mode failed");
        }

        [TestMethod]
        public async Task RemoteKey_OuterJoin_NullHandling()
        {
            OutputTestMethodName();

            var org = new OrganizationEntity
            {
                Name = "TestOrg_NoAddr_" + Guid.NewGuid(),
                HeadquartersAddressId = null
            };
            await _provider.InsertAsync(org);

            var person = new PersonEntity
            {
                FirstName = "Jane", LastName = "Doe", EmployerId = org.Id
            };
            await _provider.InsertAsync(person);

            var fetchedPerson = await _provider.GetAsync<PersonDetailEntity>(person.Id);

            Assert.IsNotNull(fetchedPerson);
            Assert.IsNull(fetchedPerson.EmployerHeadquartersCountryName);
            Assert.IsNull(fetchedPerson.ExplicitCountryName);
        }
    }
}
