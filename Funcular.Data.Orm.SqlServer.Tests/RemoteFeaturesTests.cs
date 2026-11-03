using System;
using System.Linq;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    [TestClass]
    public class RemoteFeaturesTests : SqlServerTestFixture
    {
        [TestMethod]
        public void RemoteProperty_IsPopulated_OnQuery()
        {
            var seeded = SeedEmployerChain("RemoteProperty_");

            var person = _provider.Query<PersonDetailEntity>()
                .FirstOrDefault(p => p.Id == seeded.PersonId && p.EmployerHeadquartersCountryName != null);

            Assert.IsNotNull(person, "The seeded person's EmployerHeadquartersCountryName should be populated.");
            Assert.AreEqual(seeded.CountryName, person.EmployerHeadquartersCountryName);
        }

        [TestMethod]
        public void OrderBy_RemoteProperty_OrdersByJoinedColumn()
        {
            // Ordering by a [RemoteProperty] sorts by the joined alias.column (the join is already in the SELECT). Two
            // seeded people whose country names sort opposite to their ids: the first seeded sorts last.
            var first = SeedEmployerChain("B_");
            var second = SeedEmployerChain("A_");
            int a = first.PersonId, b = second.PersonId;

            var asc = _provider.Query<PersonDetailEntity>()
                .Where(p => (p.Id == a || p.Id == b) && p.EmployerHeadquartersCountryName != null)
                .OrderBy(p => p.EmployerHeadquartersCountryName)
                .ToList();
            var desc = _provider.Query<PersonDetailEntity>()
                .Where(p => (p.Id == a || p.Id == b) && p.EmployerHeadquartersCountryName != null)
                .OrderByDescending(p => p.EmployerHeadquartersCountryName)
                .ToList();

            Assert.AreEqual(2, asc.Count);
            Assert.AreEqual(2, desc.Count);
            var ascNames = asc.Select(r => r.EmployerHeadquartersCountryName).ToList();
            CollectionAssert.AreEqual(new[] { second.CountryName, first.CountryName }, ascNames);
            ascNames.Reverse();
            CollectionAssert.AreEqual(ascNames, desc.Select(r => r.EmployerHeadquartersCountryName).ToList(),
                "DESC ordering of a [RemoteProperty] should be the exact reverse of ASC.");
        }

        [TestMethod]
        public void Count_FilteredByRemoteProperty_MatchesMaterialized()
        {
            SeedEmployerChain("Agg_");
            // Regression: aggregate over a [RemoteProperty] filter previously omitted the JOIN → SqlException 4104.
            // Compare to the materialized count: proves the join is emitted (no 4104) AND is 1:1 (correct count).
            var expected = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null).ToList().Count;
            var actual = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null).Count();
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Count_WithRemotePropertyPredicate_MatchesMaterialized()
        {
            SeedEmployerChain("Agg_");
            // The predicate-inside-aggregate form: Count(p => remoteProp ...).
            var expected = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null).ToList().Count;
            var actual = _provider.Query<PersonDetailEntity>()
                .Count(p => p.EmployerHeadquartersCountryName != null);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Any_FilteredByRemoteProperty_MatchesMaterialized()
        {
            SeedEmployerChain("Agg_");
            var expected = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null).ToList().Any();
            var actual = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null).Any();
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void All_WithRemotePropertyPredicate_MatchesMaterialized()
        {
            SeedEmployerChain("Agg_");
            var expected = _provider.Query<PersonDetailEntity>().ToList()
                .All(p => p.EmployerHeadquartersCountryName != null);
            var actual = _provider.Query<PersonDetailEntity>()
                .All(p => p.EmployerHeadquartersCountryName != null);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Sum_FilteredByRemoteProperty_MatchesMaterialized()
        {
            // Numeric-aggregate branch, filtered by a [RemoteProperty]: the JOIN must be present for the WHERE.
            // Filter to THIS seeded chain's unique country so SUM(id) stays bounded (summing all matching rows
            // over a large/accumulated person table would overflow int32 — a test artifact, not a code issue).
            var country = SeedEmployerChain("Agg_").CountryName;
            var expected = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName == country).ToList().Sum(p => p.Id);
            var actual = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName == country).Sum(p => p.Id);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Select_ScalarProjection_ReturnsListOfScalar()
        {
            // v3.9: a top-level scalar projection Select(x => x.Member) now returns List<memberType>.
            var ids = _provider.Query<PersonDetailEntity>().Select(p => p.Id).ToList();
            Assert.IsInstanceOfType(ids, typeof(System.Collections.Generic.List<int>));
        }

        [TestMethod]
        public void Select_AnonymousProjection_ThrowsClearNotSupported()
        {
            Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<PersonDetailEntity>().Select(p => new { p.Id }).ToList());
        }

        [TestMethod]
        public void Select_RemoteProperty_InCustomProjection_Throws()
        {
            // A [RemoteProperty] resolves to alias.column and needs a JOIN a custom projection's FROM doesn't
            // carry, so projecting it is rejected with a clear error (unlike self-contained computed attrs).
            Assert.ThrowsException<System.NotSupportedException>(() =>
                _provider.Query<PersonDetailEntity>()
                    .Select(p => new PersonDetailEntity { EmployerHeadquartersCountryName = p.EmployerHeadquartersCountryName })
                    .ToList());
        }

        [TestMethod]
        public void RemoteKey_IsPopulated_OnQuery()
        {
            var seeded = SeedEmployerChain("RemoteKey_");

            var person = _provider.Query<PersonDetailEntity>()
                .FirstOrDefault(p => p.Id == seeded.PersonId && p.EmployerHeadquartersCountryId != null);

            Assert.IsNotNull(person, "The seeded person's EmployerHeadquartersCountryId should be populated.");
            Assert.AreEqual(seeded.CountryId, person.EmployerHeadquartersCountryId);
        }

        [TestMethod]
        public void CanFilterBy_RemoteProperty()
        {
            // Arrange
            var seeded = SeedEmployerChain("FilterBy_");
            var person = _provider.Query<PersonDetailEntity>()
                .FirstOrDefault(p => p.Id == seeded.PersonId && p.EmployerHeadquartersCountryName != null);
            Assert.IsNotNull(person, "The seeded person's EmployerHeadquartersCountryName should be populated.");
            var targetName = person.EmployerHeadquartersCountryName;

            // Act
            var results = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName == targetName)
                .ToList();

            // Assert
            Assert.IsTrue(results.Any(p => p.Id == seeded.PersonId));
            Assert.IsTrue(results.All(p => p.EmployerHeadquartersCountryName == targetName));
        }
    }
}
