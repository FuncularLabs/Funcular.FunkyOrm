using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Person;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PersonObject = Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Person.Person;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    [TestClass]
    public class RemoteKeyReverseTests : SqlServerTestFixture
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
        public void Count_NoFilter_OnReverseEntity_MatchesBaseCount()
        {
            // A reverse [RemoteKey] entity with NO remote filter must not append the fan-out join — the aggregate
            // stays on the base table. (Appending it unconditionally over-counted; this guards that regression.)
            var baseCount = _provider.Query<CountryEntity>().Count();
            var reverseCount = _provider.Query<CountryReverseDetailEntity>().Count();
            Assert.AreEqual(baseCount, reverseCount);
        }

        [TestMethod]
        public void Count_FilteredByReverseRemoteKey_ThrowsNotSupported()
        {
            // Choice (A): filtering Count by a reverse (one-to-many) [RemoteKey] would fan out → clear exception.
            var ex = Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<CountryReverseDetailEntity>()
                    .Where(c => c.PersonId == 1)
                    .Count());
            StringAssert.Contains(ex.Message, "reverse");
            StringAssert.Contains(ex.Message, "ToList");
        }

        [TestMethod]
        public void Sum_FilteredByReverseRemoteKey_ThrowsNotSupported()
        {
            Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<CountryReverseDetailEntity>()
                    .Where(c => c.PersonId == 1)
                    .Sum(c => c.Id));
        }

        [TestMethod]
        public void Any_FilteredByReverseRemoteKey_Executes()
        {
            // Any is fan-out-safe (EXISTS), so it is ALLOWED over a reverse join — must execute, not throw.
            var result = _provider.Query<CountryReverseDetailEntity>()
                .Where(c => c.PersonId == 1)
                .Any();
            Assert.IsTrue(result || !result); // executed without throwing
        }

        [TestMethod]
        public void Min_FilteredByReverseRemoteKey_IsAllowed()
        {
            // Min is fan-out-safe → must NOT be rejected as a reverse join. (Empty sequence is acceptable; the
            // point is it isn't a NotSupportedException.)
            try { var _ = _provider.Query<CountryReverseDetailEntity>().Where(c => c.PersonId == 1).Min(c => c.Id); }
            catch (NotSupportedException) { Assert.Fail("Min over a reverse join must be allowed (fan-out-safe)."); }
            catch (InvalidOperationException) { /* no matching rows — fine, not a rejection */ }
        }

        [TestMethod]
        public void Max_FilteredByReverseRemoteKey_IsAllowed()
        {
            try { var _ = _provider.Query<CountryReverseDetailEntity>().Where(c => c.PersonId == 1).Max(c => c.Id); }
            catch (NotSupportedException) { Assert.Fail("Max over a reverse join must be allowed (fan-out-safe)."); }
            catch (InvalidOperationException) { /* no matching rows — fine, not a rejection */ }
        }

        [TestMethod]
        public void All_FilteredByReverseRemoteKey_ThrowsNotSupported()
        {
            Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<CountryReverseDetailEntity>().All(c => c.PersonId == 1));
        }

        [TestMethod]
        public void Average_FilteredByReverseRemoteKey_ThrowsNotSupported()
        {
            Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<CountryReverseDetailEntity>().Where(c => c.PersonId == 1).Average(c => c.Id));
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

            // Link Person to Address — using PersonAddress to test convention-based table name resolution
            var personAddress = new PersonAddress
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
