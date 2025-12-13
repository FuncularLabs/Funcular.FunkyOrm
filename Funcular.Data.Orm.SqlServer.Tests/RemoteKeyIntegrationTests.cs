using System;
using System.Linq;
using System.Threading.Tasks;
using Funcular.Data.Orm.Exceptions;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    [TestClass]
    public class RemoteKeyIntegrationTests : SqlDataProviderIntegrationTests
    {
        [TestMethod]
        public async Task RemoteKey_FullChainPopulation_ImplicitAndExplicit()
        {
            OutputTestMethodName();
            
            // Arrange
            // Create Country
            var country = new CountryEntity { Name = "TestCountry_" + Guid.NewGuid() };
            await _provider.InsertAsync(country);
            
            // Create Address
            var address = new AddressEntity 
            { 
                Line1 = "123 HQ St", 
                City = "Metropolis", 
                StateCode = "NY", 
                PostalCode = "10001",
                CountryId = country.Id 
            };
            await _provider.InsertAsync(address);
            
            // Create Organization
            var org = new OrganizationEntity 
            { 
                Name = "TestOrg_" + Guid.NewGuid(),
                HeadquartersAddressId = address.Id
            };
            await _provider.InsertAsync(org);
            
            // Create Person
            var person = new PersonEntity 
            { 
                FirstName = "John", 
                LastName = "Doe", 
                EmployerId = org.Id 
            };
            await _provider.InsertAsync(person);
            
            // Act
            var fetchedPerson = await _provider.GetAsync<PersonEntity>(person.Id);
            
            // Assert
            Assert.IsNotNull(fetchedPerson);
            Assert.AreEqual(country.Name, fetchedPerson.EmployerHeadquartersCountryName, "Implicit mode failed");
            Assert.AreEqual(country.Name, fetchedPerson.ExplicitCountryName, "Explicit mode failed");
        }

        [TestMethod]
        public async Task RemoteKey_OuterJoin_NullHandling()
        {
            OutputTestMethodName();
            
            // Arrange
            // Create Organization WITHOUT Address
            var org = new OrganizationEntity 
            { 
                Name = "TestOrg_NoAddr_" + Guid.NewGuid(),
                HeadquartersAddressId = null
            };
            await _provider.InsertAsync(org);
            
            // Create Person
            var person = new PersonEntity 
            { 
                FirstName = "Jane", 
                LastName = "Doe", 
                EmployerId = org.Id 
            };
            await _provider.InsertAsync(person);
            
            // Act
            var fetchedPerson = await _provider.GetAsync<PersonEntity>(person.Id);
            
            // Assert
            Assert.IsNotNull(fetchedPerson);
            Assert.IsNull(fetchedPerson.EmployerHeadquartersCountryName);
            Assert.IsNull(fetchedPerson.ExplicitCountryName);
        }
    }
}
