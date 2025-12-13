using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    [TestClass]
    public class RemoteCollectionTests : SqlDataProviderIntegrationTests
    {
        [TestMethod]
        public void CanPopulateRemoteCollectionImperatively()
        {
            // 1. Setup Data
            // Create a Country
            var country = new CountryEntity { Name = "TestCountry_" + Guid.NewGuid().ToString().Substring(0, 8) };
            _provider.Insert(country);

            // Create an Address in that Country
            var address = new AddressEntity
            {
                Line1 = "123 Test St",
                City = "Test City",
                StateCode = "TS",
                PostalCode = "12345",
                CountryId = country.Id
            };
            _provider.Insert(address);

            // Create an Organization with that Address
            var org = new OrganizationEntity
            {
                Name = "Test Org " + Guid.NewGuid(),
                HeadquartersAddressId = address.Id
            };
            _provider.Insert(org);

            // Create a Person employed by that Organization
            var person = new PersonEntity
            {
                FirstName = "Remote",
                LastName = "Collection",
                EmployerId = org.Id
            };
            _provider.Insert(person);

            // 2. Fetch the Person
            // The [RemoteKey] EmployerHeadquartersCountryId should be populated automatically
            var fetchedPerson = _provider.Query<PersonEntity>()
                .Where(p => p.Id == person.Id)
                .FirstOrDefault();

            Assert.IsNotNull(fetchedPerson);
            Assert.IsNotNull(fetchedPerson.EmployerHeadquartersCountryId, "Remote Key should be populated");
            Assert.AreEqual(country.Id, fetchedPerson.EmployerHeadquartersCountryId.Value);

            // 3. Imperatively Populate the Collection
            // "I want to populate the AssociatedCountries collection with the country of their employer."
            // In a real scenario, we might combine this with other keys (HomeCountryId, etc.)
            
            if (fetchedPerson.EmployerHeadquartersCountryId.HasValue)
            {
                var employerCountry = _provider.Query<CountryEntity>()
                    .FirstOrDefault(c => c.Id == fetchedPerson.EmployerHeadquartersCountryId.Value);
                
                if (employerCountry != null)
                {
                    fetchedPerson.AssociatedCountries.Add(employerCountry);
                }
            }

            // 4. Verify
            Assert.AreEqual(1, fetchedPerson.AssociatedCountries.Count);
            Assert.AreEqual(country.Name, fetchedPerson.AssociatedCountries.First().Name);
        }
    }
}
