using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    [TestClass]
    public class PostgreSqlRemoteFeaturesTests : PostgreSqlTestFixture
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
            var seeded = SeedEmployerChain("FilterBy_");
            var person = _provider.Query<PersonDetailEntity>()
                .FirstOrDefault(p => p.Id == seeded.PersonId && p.EmployerHeadquartersCountryName != null);
            Assert.IsNotNull(person, "The seeded person's EmployerHeadquartersCountryName should be populated.");
            var targetName = person.EmployerHeadquartersCountryName;

            var results = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName == targetName)
                .ToList();

            Assert.IsTrue(results.Any(p => p.Id == seeded.PersonId));
            Assert.IsTrue(results.All(p => p.EmployerHeadquartersCountryName == targetName));
        }

        [TestMethod]
        public void OrderBy_RemoteProperty_OrdersByJoinedColumn()
        {
            // Two seeded people whose country names sort opposite to their ids: the first seeded sorts last.
            var first = SeedEmployerChain("B_");
            var second = SeedEmployerChain("A_");
            int a = first.PersonId, b = second.PersonId;

            var asc = _provider.Query<PersonDetailEntity>()
                .Where(p => (p.Id == a || p.Id == b) && p.EmployerHeadquartersCountryName != null)
                .OrderBy(p => p.EmployerHeadquartersCountryName).ToList();
            var desc = _provider.Query<PersonDetailEntity>()
                .Where(p => (p.Id == a || p.Id == b) && p.EmployerHeadquartersCountryName != null)
                .OrderByDescending(p => p.EmployerHeadquartersCountryName).ToList();

            Assert.AreEqual(2, asc.Count);
            Assert.AreEqual(2, desc.Count);
            var ascNames = asc.Select(r => r.EmployerHeadquartersCountryName).ToList();
            CollectionAssert.AreEqual(new[] { second.CountryName, first.CountryName }, ascNames);
            ascNames.Reverse();
            CollectionAssert.AreEqual(ascNames, desc.Select(r => r.EmployerHeadquartersCountryName).ToList(),
                "DESC ordering of a [RemoteProperty] should be the exact reverse of ASC.");
        }

        [TestMethod]
        public void Select_RemoteProperty_InCustomProjection_Throws()
        {
            // A [RemoteProperty] requires a join a custom projection's FROM does not carry — reject clearly.
            Assert.ThrowsException<System.NotSupportedException>(() =>
                _provider.Query<PersonDetailEntity>()
                    .Select(p => new PersonDetailEntity { EmployerHeadquartersCountryName = p.EmployerHeadquartersCountryName })
                    .ToList());
        }

        [TestMethod]
        public void Count_FilteredByRemoteProperty_MatchesMaterialized()
        {
            SeedEmployerChain("Agg_");
            var expected = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).ToList().Count;
            var actual = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).Count();
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Count_WithRemotePropertyPredicate_MatchesMaterialized()
        {
            SeedEmployerChain("Agg_");
            var expected = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).ToList().Count;
            var actual = _provider.Query<PersonDetailEntity>().Count(p => p.EmployerHeadquartersCountryName != null);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Any_FilteredByRemoteProperty_MatchesMaterialized()
        {
            SeedEmployerChain("Agg_");
            var expected = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).ToList().Any();
            var actual = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).Any();
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void All_WithRemotePropertyPredicate_MatchesMaterialized()
        {
            SeedEmployerChain("Agg_");
            var expected = _provider.Query<PersonDetailEntity>().ToList().All(p => p.EmployerHeadquartersCountryName != null);
            var actual = _provider.Query<PersonDetailEntity>().All(p => p.EmployerHeadquartersCountryName != null);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Sum_FilteredByRemoteProperty_MatchesMaterialized()
        {
            SeedEmployerChain("Agg_");
            var expected = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).ToList().Sum(p => p.Id);
            var actual = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).Sum(p => p.Id);
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
            Assert.ThrowsException<System.NotSupportedException>(() =>
                _provider.Query<PersonDetailEntity>().Select(p => new { p.Id }).ToList());
        }

        [TestMethod]
        public void Select_ScalarProjection_ThenComposingLambdaOperators_ThrowClearNotSupported()
        {
            // A lambda-bearing operator applied AFTER a scalar projection is rejected at parse with a clean
            // NotSupportedException (not an InvalidCastException from a Func<T,...> hard-cast).
            var where = Assert.ThrowsException<System.NotSupportedException>(() =>
                _provider.Query<PersonDetailEntity>().Select(p => p.Id).Where(x => x > 0).ToList());
            StringAssert.Contains(where.Message, "Where");
            var all = Assert.ThrowsException<System.NotSupportedException>(() =>
                _provider.Query<PersonDetailEntity>().Select(p => p.Id).All(x => x > 0));
            StringAssert.Contains(all.Message, "All");
            var orderBy = Assert.ThrowsException<System.NotSupportedException>(() =>
                _provider.Query<PersonDetailEntity>().Select(p => p.Id).OrderBy(x => x).ToList());
            StringAssert.Contains(orderBy.Message, "OrderBy");
            var chained = Assert.ThrowsException<System.NotSupportedException>(() =>
                _provider.Query<PersonDetailEntity>().Select(p => p.Id).Select(x => x + 1).ToList());
            StringAssert.Contains(chained.Message, "Select");
        }

        [TestMethod]
        public void GroupBy_IsNotTranslated_ThrowsNotSupported()
        {
            var ex = Assert.ThrowsException<System.NotSupportedException>(() =>
                _provider.Query<PersonDetailEntity>().GroupBy(p => p.Id).ToList());
            StringAssert.Contains(ex.Message, "GroupBy");
        }
    }
}
