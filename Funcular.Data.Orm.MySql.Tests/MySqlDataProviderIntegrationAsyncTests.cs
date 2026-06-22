using System;
using System.Linq;
using System.Threading.Tasks;
using Funcular.Data.Orm.MySql.Tests.Domain;

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>
    /// Async CRUD integration tests for the MySQL provider against a live MySQL server.
    /// </summary>
    [TestClass]
    public class MySqlDataProviderIntegrationAsyncTests : MySqlTestFixture
    {
        [TestInitialize]
        public void Setup() => InitProvider();

        [TestCleanup]
        public void Cleanup() => DisposeProvider();

        private static Person NewPerson(string first, string last) => new Person
        {
            FirstName = first,
            LastName = last,
            Gender = "X",
            DateUtcCreated = DateTime.UtcNow,
            DateUtcModified = DateTime.UtcNow
        };

        [TestMethod]
        public async Task UpdateAsync_WithinTransaction_Persists()
        {
            // Regression (3.6.1): UpdateAsync inside a BeginTransaction scope must not trip the
            // transactional-concurrency guard via a nested read scope.
            _provider.BeginTransaction();
            try
            {
                var person = NewPerson("Before", "Tx-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                await _provider.InsertAsync(person);

                person.FirstName = "After";
                await _provider.UpdateAsync(person);

                var fetched = await _provider.GetAsync<Person>(person.Id);
                Assert.IsNotNull(fetched);
                Assert.AreEqual("After", fetched.FirstName);
            }
            finally
            {
                _provider.RollbackTransaction();
            }
        }

        [TestMethod]
        public async Task InsertAsync_AssignsIdentity_AndGetAsyncRoundTrips()
        {
            var person = NewPerson("Async", "Inserter");
            await _provider.InsertAsync(person);
            Assert.IsTrue(person.Id > 0);

            var fetched = await _provider.GetAsync<Person>(person.Id);
            Assert.IsNotNull(fetched);
            Assert.AreEqual("Async", fetched.FirstName);
        }

        [TestMethod]
        public async Task QueryAsync_FiltersByColumn()
        {
            var marker = "AQ" + Guid.NewGuid().ToString("N").Substring(0, 8);
            await _provider.InsertAsync(NewPerson("One", marker));
            await _provider.InsertAsync(NewPerson("Two", marker));

            var results = await _provider.QueryAsync<Person>(p => p.LastName == marker);
            Assert.AreEqual(2, results.Count);
        }

        [TestMethod]
        public async Task UpdateAsync_PersistsChanges()
        {
            var person = NewPerson("Before", "AsyncUpd");
            await _provider.InsertAsync(person);

            person.FirstName = "After";
            await _provider.UpdateAsync(person);

            var fetched = await _provider.GetAsync<Person>(person.Id);
            Assert.AreEqual("After", fetched.FirstName);
        }

        [TestMethod]
        public async Task GetListAsync_ReturnsRows()
        {
            await _provider.InsertAsync(NewPerson("List", "AsyncList"));
            var all = await _provider.GetListAsync<Person>();
            Assert.IsTrue(all.Count >= 1);
        }

        [TestMethod]
        public async Task DeleteAsync_InTransaction_RemovesRow()
        {
            var marker = "AD" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var person = NewPerson("ToDeleteAsync", marker);
            await _provider.InsertAsync(person);

            _provider.BeginTransaction();
            var affected = await _provider.DeleteAsync<Person>(p => p.LastName == marker);
            _provider.CommitTransaction();

            Assert.IsTrue(affected >= 1);
            Assert.IsNull(await _provider.GetAsync<Person>(person.Id));
        }
    }
}
