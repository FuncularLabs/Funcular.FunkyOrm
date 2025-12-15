using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PersonObject = Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Person.Person;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    [TestClass]
    public class RemoteKeyReverseTests : SqlDataProviderIntegrationTests
    {
        [Table("country")]
        public class CountryReverseDetailEntity : CountryEntity
        {
            // Path: Country <- Address (via CountryId) <- PersonAddress (via AddressId) -> Person (via PersonId)
            // The resolver should find this path automatically if we just point to Person.Id
            [RemoteKey(typeof(PersonObject), keyPath: new[] { nameof(PersonObject.Id) })]
            public int PersonId { get; set; }
        }

        [TestMethod]
        public void CanFilterByReverseRemoteKey()
        {
            // 1. Setup Data
            // Create a Person
            var person = new PersonEntity
            {
                FirstName = "Reverse",
                LastName = "KeyTest_" + Guid.NewGuid().ToString().Substring(0, 8)
            };
            _provider.Insert(person);

            // Create a Country
            var country = new CountryEntity { Name = "ReverseCountry_" + Guid.NewGuid().ToString().Substring(0, 8) };
            _provider.Insert(country);

            // Create an Address in that Country
            var address = new AddressEntity
            {
                Line1 = "123 Reverse St",
                City = "Reverse City",
                StateCode = "RC",
                PostalCode = "54321",
                CountryId = country.Id
            };
            _provider.Insert(address);

            // Link Person to Address
            var personAddress = new PersonAddressEntity
            {
                PersonId = person.Id,
                AddressId = address.Id,
                IsPrimary = true
            };
            _provider.Insert(personAddress);

            // 2. Act
            // Query CountryReverseDetailEntity where PersonId matches our person
            // This requires the ORM to join Country -> Address -> PersonAddress -> Person
            // And filter by Person.Id
            var results = _provider.Query<CountryReverseDetailEntity>()
                .Where(c => c.PersonId == person.Id)
                .ToList();

            // 3. Assert
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(country.Id, results[0].Id);
            Assert.AreEqual(country.Name, results[0].Name);
            Assert.AreEqual(person.Id, results[0].PersonId);
        }
    }
}
