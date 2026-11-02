using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Address;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person;
using PersonObject = Funcular.Data.Orm.PostgreSql.Tests.Domain.Objects.Person.Person;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    [TestClass]
    public class PostgreSqlRemoteKeyReverseTests : PostgreSqlTestFixture
    {
        [Table("country")]
        public class CountryReverseDetailEntity : CountryEntity
        {
            // Path: Country <- Address (via CountryId) <- PersonAddress (via AddressId) -> Person (via PersonId)
            // The resolver should find this reverse path automatically from a pointer to Person.Id.
            [RemoteKey(typeof(PersonObject), keyPath: new[] { nameof(PersonObject.Id) })]
            public int PersonId { get; set; }
        }

        [TestMethod]
        public void Count_NoFilter_OnReverseEntity_MatchesBaseCount()
        {
            // A reverse [RemoteKey] entity with NO remote filter must not append the fan-out join — the aggregate
            // stays on the base table. (Appending it unconditionally over-counted; this guards that regression.)
            // One country whose address two people share, so a fan-out join would count it twice.
            var country = new CountryEntity { Name = "FanOut_" + Guid.NewGuid() };
            _provider.Insert(country);
            Track(country);
            var address = new AddressEntity { Line1 = "1 FanOut St", City = "FanOut City", StateCode = "FO", PostalCode = "11111", CountryId = country.Id };
            _provider.Insert(address);
            Track(address);
            for (var i = 0; i < 2; i++)
            {
                var person = new PersonEntity { FirstName = "FanOut", LastName = "Test_" + Guid.NewGuid().ToString().Substring(0, 8) };
                _provider.Insert(person);
                Track(person);
                var link = new PersonAddressEntity { PersonId = person.Id, AddressId = address.Id };
                _provider.Insert(link);
                Track(link);
            }

            var baseCount = _provider.Query<CountryEntity>().Count();
            var reverseCount = _provider.Query<CountryReverseDetailEntity>().Count();
            Assert.AreEqual(baseCount, reverseCount);
            Assert.AreEqual(1, _provider.Query<CountryReverseDetailEntity>().Where(c => c.Id == country.Id).Count());
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
    }
}
