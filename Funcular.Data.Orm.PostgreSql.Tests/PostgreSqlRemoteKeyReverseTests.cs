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
    }
}
