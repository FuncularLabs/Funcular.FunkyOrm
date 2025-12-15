using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Address;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Person;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    [TestClass]
    public class SqlDataProviderIntegrationAsyncTests
    {
        private string _connectionString;
        public required SqlServerOrmDataProvider _provider;
        private StringBuilder _sb = new();

        public void OutputTestMethodName([CallerMemberName] string callerMemberName = "")
        {
            Debug.WriteLine($"\r\nTest: {callerMemberName}");
        }

        [TestInitialize]
        public void Setup()
        {
            _sb.Clear();
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
                "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=funky_db;Integrated Security=True;";
            TestConnection();

            _provider = new SqlServerOrmDataProvider(_connectionString)
            {
                Log = s =>
                {
                    Debug.WriteLine(s);
                    _sb.AppendLine(s);
                }
            };
        }

        [TestCleanup]
        public void Cleanup()
        {
            _provider?.Dispose();
        }

        private void TestConnection()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                try
                {
                    connection.Open();
                    Debug.WriteLine("Connection successful.");
                }
                catch (SqlException ex)
                {
                    throw new ArgumentNullException("connectionString",
                        $"Connection failed. Ensure funky_db exists and configure FUNKY_CONNECTION environment variable if not using localhost.\r\n\r\n{ex}");
                }
            }
        }

        private async Task<int> InsertTestPersonAsync(string firstName, string middleInitial, string lastName, DateTime? birthdate, string gender, Guid uniqueId, DateTime? dateUtcCreated = null, DateTime? dateUtcModified = null)
        {
            if (gender?.Length > 10) gender = gender.Substring(0, 10);

            var person = new Person
            {
                FirstName = firstName,
                MiddleInitial = middleInitial,
                LastName = lastName,
                Birthdate = birthdate,
                Gender = gender,
                UniqueId = uniqueId,
                DateUtcCreated = dateUtcCreated ?? DateTime.UtcNow,
                DateUtcModified = dateUtcModified ?? DateTime.UtcNow
            };

            await _provider.InsertAsync(person);
            return person.Id;
        }

        private async Task<int> InsertTestAddressAsync(string line1, string line2, string city, string stateCode, string postalCode)
        {
            var address = new Address
            {
                Line1 = line1,
                Line2 = line2,
                City = city,
                StateCode = stateCode,
                PostalCode = postalCode
            };
            await _provider.InsertAsync(address);
            return address.Id;
        }

        private async Task InsertTestPersonAddressAsync(int personId, int addressId)
        {
            var link = new PersonAddress { PersonId = personId, AddressId = addressId };
            await _provider.InsertAsync(link);
        }

        [TestMethod]
        public async Task Warm_Up_Async()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            await InsertTestPersonAsync(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var list = await _provider.QueryAsync<Person>(p => p.FirstName != null);
            Assert.IsTrue(list.Count > 0);
        }

        [TestMethod]
        public async Task Get_WithExistingId_ReturnsPerson_Async()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = await InsertTestPersonAsync(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var person = await _provider.GetAsync<Person>(personId);
            Assert.IsNotNull(person);
            Assert.AreEqual(personId, person.Id);
        }

        [TestMethod]
        public async Task GetList_ReturnsAllPersonAddressLinks_Async()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = await InsertTestPersonAsync(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var addressId = await InsertTestAddressAsync("123 Main St", null, "Springfield", "IL", "62704");
            await InsertTestPersonAddressAsync(personId, addressId);
            var links = await _provider.GetListAsync<PersonAddress>();
            Assert.IsTrue(links.Any(l => l.PersonId == personId && l.AddressId == addressId));
        }

        [TestMethod]
        public async Task Insert_NewPerson_IncreasesCount_Async()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var initialCount = _provider.Query<Person>().Count();
            var newPerson = new Person
            {
                FirstName = guid,
                LastName = guid,
                Birthdate = DateTime.Today.Subtract(TimeSpan.FromDays(Random.Shared.Next(10, 30))),
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow,
                UniqueId = Guid.NewGuid()
            };
            await _provider.InsertAsync(newPerson);
            var updatedCount = _provider.Query<Person>().Count();
            Assert.AreEqual(initialCount + 1, updatedCount);
        }

        [TestMethod]
        public async Task Update_PersonUpdates_Async()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = await InsertTestPersonAsync(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var person = await _provider.GetAsync<Person>(personId);
            var originalName = person.FirstName;
            person.FirstName = $"Updated{guid}";
            await _provider.UpdateAsync(person);
            var updatedPerson = await _provider.GetAsync<Person>(personId);
            Assert.IsNotNull(updatedPerson);
            Assert.AreNotEqual(originalName, updatedPerson.FirstName);
        }

        [TestMethod]
        public async Task Query_WithExpression_ReturnsFilteredAddresses_Async()
        {
            OutputTestMethodName();
            const string stateCode = "IL";
            var addressId = await InsertTestAddressAsync($"123 Main St {Guid.NewGuid()}", null, "Springfield", stateCode, "62704");
            var addresses = await _provider.QueryAsync<Address>(a => a.StateCode == stateCode);
            Assert.IsTrue(addresses.Any(x => x.Id == addressId && x.StateCode == stateCode));
        }

        [TestMethod]
        public async Task Query_FirstOrDefaultWithPredicate_ReturnsFirstMatchingEntity_Async()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            await InsertTestPersonAsync(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var resultList = await _provider.QueryAsync<Person>(x => x.FirstName == guid);
            var result = resultList.FirstOrDefault();
            Assert.IsNotNull(result);
            Assert.AreEqual(guid, result.FirstName);
        }

        [TestMethod]
        public async Task Transaction_BeginCommit_Async()
        {
            OutputTestMethodName();
            _provider.BeginTransaction();

            var person = new Person
            {
                FirstName = Guid.NewGuid().ToString(),
                LastName = Guid.NewGuid().ToString(),
                Birthdate = null,
                UniqueId = Guid.NewGuid()
            };
            await _provider.InsertAsync(person);

            _provider.CommitTransaction();

            var committedPerson = await _provider.GetAsync<Person>(person.Id);
            Assert.IsNotNull(committedPerson);
        }

        [TestMethod]
        public async Task QueryAsync_CountWithPredicate_ReturnsCorrectCount()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            await InsertTestPersonAsync(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            await InsertTestPersonAsync(guid, "B", guid, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());

            var persons = await _provider.QueryAsync<Person>(x => x.FirstName == guid);
            var count = persons.Count;
            Assert.AreEqual(2, count);
        }

        [TestMethod]
        public async Task QueryAsync_MaxIdWithSelector_ReturnsMaximum()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var person1Id = await InsertTestPersonAsync(guid, "A", guid, new DateTime(1990, 1, 1), "Male", Guid.NewGuid());
            var person2Id = await InsertTestPersonAsync(guid, "B", guid, new DateTime(2000, 1, 1), "Female", Guid.NewGuid());

            var persons = await _provider.QueryAsync<Person>(x => x.FirstName == guid);
            var maxId = persons.Max(x => x.Id);
            Assert.AreEqual(person2Id, maxId);
            Assert.IsTrue(person2Id > person1Id);
        }

        [TestMethod]
        public async Task QueryAsync_MinWithSelector_ReturnsMinimum()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var birthdate1 = new DateTime(1990, 1, 1);
            var birthdate2 = new DateTime(2000, 1, 1);
            await InsertTestPersonAsync(guid, "A", guid, birthdate1, "Male", Guid.NewGuid());
            await InsertTestPersonAsync(guid, "B", guid, birthdate2, "Female", Guid.NewGuid());

            var persons = await _provider.QueryAsync<Person>(x => x.FirstName == guid);
            var minBirthdate = persons.Min(x => x.Birthdate);
            Assert.AreEqual(birthdate1, minBirthdate);
        }

        [TestMethod]
        public async Task QueryAsync_OrderBy_ReturnsOrderedResults()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            await InsertTestPersonAsync(guid, "A", "Zimmerman", DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            await InsertTestPersonAsync(guid, "B", "Adams", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());

            var persons = await _provider.QueryAsync<Person>(x => x.FirstName == guid);
            var ordered = persons.OrderBy(x => x.LastName).ToList();
            Assert.AreEqual(2, ordered.Count);
            Assert.AreEqual("Adams", ordered[0].LastName);
            Assert.AreEqual("Zimmerman", ordered[1].LastName);
        }

        [TestMethod]
        public async Task QueryAsync_OrderByDescending_ReturnsOrderedResults()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            await InsertTestPersonAsync(guid, "A", "Zimmerman", DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            await InsertTestPersonAsync(guid, "B", "Adams", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());

            var persons = await _provider.QueryAsync<Person>(x => x.FirstName == guid);
            var ordered = persons.OrderByDescending(x => x.LastName).ToList();
            Assert.AreEqual(2, ordered.Count);
            Assert.AreEqual("Zimmerman", ordered[0].LastName);
            Assert.AreEqual("Adams", ordered[1].LastName);
        }

        [TestMethod]
        public async Task DeleteAsync_WithValidWhereClauseAndTransaction_DeletesEntity()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = await InsertTestPersonAsync(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());

            _provider.BeginTransaction();
            var deleted = await _provider.DeleteAsync<Person>(x => x.Id == personId);
            _provider.CommitTransaction();

            Assert.AreEqual(1, deleted);
            var person = await _provider.GetAsync<Person>(personId);
            Assert.IsNull(person);
        }

        [TestMethod]
        public async Task DeleteAsync_WithoutTransaction_ThrowsException()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = await InsertTestPersonAsync(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
                await _provider.DeleteAsync<Person>(x => x.Id == personId)
            );
            // Cleanup
            _provider.BeginTransaction();
            await _provider.DeleteAsync<Person>(x => x.Id == personId);
            _provider.CommitTransaction();
        }

        [TestMethod]
        public async Task DeleteAsync_WithoutWhereClause_ThrowsException()
        {
            OutputTestMethodName();
            _provider.BeginTransaction();
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
                await _provider.DeleteAsync<Person>(null)
            );
            _provider.RollbackTransaction();
        }

        [TestMethod]
        public async Task DeleteAsync_WithEmptyWhereClause_ThrowsException()
        {
            OutputTestMethodName();
            _provider.BeginTransaction();
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
                await _provider.DeleteAsync<Person>(x => true)
            );
            _provider.RollbackTransaction();
        }

        [TestMethod]
        public async Task DeleteAsync_DoesNotAffectOtherEntities()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId1 = await InsertTestPersonAsync(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var personId2 = await InsertTestPersonAsync(guid, "B", guid, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());

            _provider.BeginTransaction();
            var deleted = await _provider.DeleteAsync<Person>(x => x.Id == personId1);
            _provider.CommitTransaction();

            Assert.AreEqual(1, deleted);
            var person1 = await _provider.GetAsync<Person>(personId1);
            var person2 = await _provider.GetAsync<Person>(personId2);
            Assert.IsNull(person1);
            Assert.IsNotNull(person2);
        }

        [TestMethod]
        public async Task DeleteAsync_ById_DeletesEntity()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = await InsertTestPersonAsync(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());

            _provider.BeginTransaction();
            var deleted = await _provider.DeleteAsync<Person>(personId);
            _provider.CommitTransaction();

            Assert.IsTrue(deleted);
            var person = await _provider.GetAsync<Person>(personId);
            Assert.IsNull(person);
        }
    }
}