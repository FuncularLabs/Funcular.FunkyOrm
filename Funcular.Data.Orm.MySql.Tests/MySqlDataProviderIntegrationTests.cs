using System;
using System.Collections.Generic;
using System.Linq;
using Funcular.Data.Orm.MySql.Tests.Domain;

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>
    /// Core CRUD + LINQ integration tests for the MySQL provider, run against a live MySQL server
    /// (FUNKY_MYSQL_CONNECTION). Tests isolate themselves with unique markers (the fixture does not
    /// wrap each test in a transaction); transaction-specific tests manage their own scope.
    /// </summary>
    [TestClass]
    public class MySqlDataProviderIntegrationTests : MySqlTestFixture
    {
        [TestInitialize]
        public void Setup() => InitProvider();

        [TestCleanup]
        public void Cleanup() => DisposeProvider();

        private Person NewPerson(string first, string last, string gender = "X") => new Person
        {
            FirstName = first,
            LastName = last,
            Gender = gender,
            DateUtcCreated = DateTime.UtcNow,
            DateUtcModified = DateTime.UtcNow
        };

        [TestMethod]
        public void Update_WithinTransaction_Persists()
        {
            // Regression (3.6.1): Update inside a BeginTransaction scope must not trip the
            // transactional-concurrency guard. Previously the read-before-write opened a nested
            // ConnectionScope and threw "A concurrent operation is already using the transactional connection."
            _provider.BeginTransaction();
            try
            {
                var person = NewPerson("Before", "Tx-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                _provider.Insert(person);

                person.FirstName = "After";
                _provider.Update(person);

                var fetched = _provider.Get<Person>(person.Id);
                Assert.IsNotNull(fetched);
                Assert.AreEqual("After", fetched.FirstName);
            }
            finally
            {
                _provider.RollbackTransaction();
            }
        }

        [TestMethod]
        public void Insert_AssignsIdentity_AndGetRoundTrips()
        {
            var person = NewPerson("Ada", "Lovelace", "F");
            _provider.Insert(person);

            Assert.IsTrue(person.Id > 0, "Identity id should be assigned via LAST_INSERT_ID().");

            var fetched = _provider.Get<Person>(person.Id);
            Assert.IsNotNull(fetched);
            Assert.AreEqual("Ada", fetched.FirstName);
            Assert.AreEqual("Lovelace", fetched.LastName);
        }

        [TestMethod]
        public void Update_PersistsChanges()
        {
            var person = NewPerson("Grace", "Hopper", "F");
            _provider.Insert(person);

            person.LastName = "Hopper-Murray";
            _provider.Update(person);

            var fetched = _provider.Get<Person>(person.Id);
            Assert.AreEqual("Hopper-Murray", fetched.LastName);
        }

        [TestMethod]
        public void Query_Where_FiltersByColumn()
        {
            var marker = "Q" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _provider.Insert(NewPerson("Alan", marker));
            _provider.Insert(NewPerson("Edsger", marker));
            _provider.Insert(NewPerson("Donald", "Other" + marker));

            var results = _provider.Query<Person>().Where(p => p.LastName == marker).ToList();
            Assert.AreEqual(2, results.Count);
        }

        [TestMethod]
        public void Query_StartsWith_TranslatesToLikeConcat()
        {
            var marker = "S" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _provider.Insert(NewPerson(marker + "andra", "Test"));
            _provider.Insert(NewPerson(marker + "amuel", "Test"));
            _provider.Insert(NewPerson("Nope", "Test"));

            var results = _provider.Query<Person>().Where(p => p.FirstName.StartsWith(marker)).ToList();
            Assert.AreEqual(2, results.Count);
        }

        [TestMethod]
        public void Query_CollectionContains_TranslatesToInClause()
        {
            var marker = "I" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _provider.Insert(NewPerson("InA", marker));
            _provider.Insert(NewPerson("InB", marker));
            _provider.Insert(NewPerson("InC", marker));

            var wanted = new[] { "InA", "InC" };
            var results = _provider.Query<Person>()
                .Where(p => p.LastName == marker && wanted.Contains(p.FirstName)).ToList();
            Assert.AreEqual(2, results.Count);
        }

        [TestMethod]
        public void Query_Paging_SkipTake_IsDeterministic()
        {
            var marker = "P" + Guid.NewGuid().ToString("N").Substring(0, 8);
            for (int i = 0; i < 5; i++)
                _provider.Insert(NewPerson($"Page{i}", marker));

            var page = _provider.Query<Person>()
                .Where(p => p.LastName == marker)
                .OrderBy(p => p.Id)
                .Skip(1).Take(2)
                .ToList();

            Assert.AreEqual(2, page.Count);
            Assert.AreEqual("Page1", page[0].FirstName);
            Assert.AreEqual("Page2", page[1].FirstName);
        }

        [TestMethod]
        public void Query_Count_Aggregate()
        {
            var marker = "C" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _provider.Insert(NewPerson("CountA", marker));
            _provider.Insert(NewPerson("CountB", marker));

            var count = _provider.Query<Person>().Count(p => p.LastName == marker);
            Assert.AreEqual(2, count);
        }

        [TestMethod]
        public void Query_Any_Aggregate()
        {
            var marker = "A" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _provider.Insert(NewPerson("AnyA", marker));

            var absent = marker + "zzz";
            Assert.IsTrue(_provider.Query<Person>().Any(p => p.LastName == marker));
            Assert.IsFalse(_provider.Query<Person>().Any(p => p.LastName == absent));
        }

        [TestMethod]
        public void Delete_WithinTransaction_RemovesRow()
        {
            var marker = "D" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var person = NewPerson("ToDelete", marker);
            _provider.Insert(person);

            // Delete requires an active transaction (safety guard).
            _provider.BeginTransaction();
            var affected = _provider.Delete<Person>(p => p.LastName == marker);
            _provider.CommitTransaction();

            Assert.IsTrue(affected >= 1);
            Assert.IsNull(_provider.Get<Person>(person.Id));
        }

        [TestMethod]
        public void Insert_NonIdentityGuidEntity_RoundTrips()
        {
            var entity = new NonIdentityGuidEntity { Id = Guid.NewGuid(), Name = "guid-pk" };
            _provider.Insert(entity);

            var fetched = _provider.Get<NonIdentityGuidEntity>(entity.Id);
            Assert.IsNotNull(fetched);
            Assert.AreEqual(entity.Id, fetched.Id);
            Assert.AreEqual("guid-pk", fetched.Name);
        }

        [TestMethod]
        public void Insert_NullableGuidColumn_RoundTrips()
        {
            var person = NewPerson("Guid", "Holder");
            person.UniqueId = Guid.NewGuid();
            _provider.Insert(person);

            var fetched = _provider.Get<Person>(person.Id);
            Assert.AreEqual(person.UniqueId, fetched.UniqueId);
        }
    }
}
