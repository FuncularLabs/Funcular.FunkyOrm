using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Objects.Address;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Objects.Person;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Objects;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    [TestClass]
    public class PostgreSqlDataProviderIntegrationTests
    {
        protected string _connectionString;
        public PostgreSqlOrmDataProvider _provider;
        protected readonly StringBuilder _sb = new();

        public void OutputTestMethodName([CallerMemberName] string callerMemberName = "")
        {
            Debug.WriteLine($"\r\nTest: {callerMemberName}");
        }

        [TestInitialize]
        public void Setup()
        {
            _sb.Clear();
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_PG_CONNECTION") ??
                "Host=localhost;Port=5432;Database=funky_db;Username=funky_user;Password=funky_password";
            TestConnection();

            _provider = new PostgreSqlOrmDataProvider(_connectionString)
            {
                Log = s =>
                {
                    Debug.WriteLine(s);
                    Console.WriteLine(s);
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
            using (var connection = new NpgsqlConnection(_connectionString))
            {
                try
                {
                    connection.Open();
                    Debug.WriteLine("PostgreSQL connection successful.");
                }
                catch (NpgsqlException ex)
                {
                    Assert.Inconclusive(
                        $"PostgreSQL not available. Start Docker: docker compose -f Database/PostgreSql/docker-compose.yml up -d\n{ex.Message}");
                }
            }
        }

        protected int InsertTestPerson(string firstName, string middleInitial, string lastName, DateTime? birthdate, string gender, Guid uniqueId, DateTime? dateUtcCreated = null, DateTime? dateUtcModified = null)
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
            _provider.Insert(person);
            return person.Id;
        }

        protected int InsertTestAddress(string line1, string line2, string city, string stateCode, string postalCode)
        {
            var address = new Address
            {
                Line1 = line1,
                Line2 = line2,
                City = city,
                StateCode = stateCode,
                PostalCode = postalCode
            };
            _provider.Insert(address);
            return address.Id;
        }

        protected void InsertTestPersonAddress(int personId, int addressId)
        {
            var link = new PersonAddress { PersonId = personId, AddressId = addressId };
            _provider.Insert(link);
        }

        #region Get Tests

        [TestMethod]
        public void Get_WithExistingId_ReturnsPerson()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var person = _provider.Get<Person>(personId);
            Assert.IsNotNull(person);
            Assert.AreEqual(personId, person.Id);
        }

        [TestMethod]
        public void Get_ById_ReturnsNull_WhenNotFound()
        {
            OutputTestMethodName();
            var result = _provider.Get<Person>(int.MaxValue);
            Assert.IsNull(result);
        }

        #endregion

        #region GetList Tests

        [TestMethod]
        public void GetList_ReturnsAllPersonAddressLinks()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var addressId = InsertTestAddress("123 Main St", null, "Springfield", "IL", "62704");
            InsertTestPersonAddress(personId, addressId);
            var links = _provider.GetList<PersonAddress>().ToList();
            Assert.IsTrue(links.Any(l => l.PersonId == personId && l.AddressId == addressId));
        }

        [TestMethod]
        public void GetList_ReturnsAllPersons()
        {
            OutputTestMethodName();
            var results = _provider.GetList<Person>();
            Assert.IsNotNull(results);
            Assert.IsTrue(results.Count > 0);
        }

        [TestMethod]
        public void GetList_ReturnsAllAddresses()
        {
            OutputTestMethodName();
            var results = _provider.GetList<Address>();
            Assert.IsNotNull(results);
            Assert.IsTrue(results.Count > 0);
        }

        #endregion

        #region Insert Tests

        [TestMethod]
        public void Insert_NewPerson_IncreasesCount()
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
            _provider.Insert(newPerson);
            var updatedCount = _provider.Query<Person>().Count();
            Assert.AreEqual(initialCount + 1, updatedCount);
        }

        [TestMethod]
        public void Insert_ReturnsPrimaryKey()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var newPerson = new Person
            {
                FirstName = guid,
                LastName = guid,
                Birthdate = DateTime.Today.Subtract(TimeSpan.FromDays(Random.Shared.Next(10, 30))),
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow,
                UniqueId = Guid.NewGuid()
            };
            var result = _provider.Insert(newPerson);
            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result, typeof(int));
            Assert.AreEqual(newPerson.Id, (int)result);
            Assert.IsTrue((int)result > 0);
        }

        [TestMethod]
        public void Insert_GuidPrimaryKey_Works()
        {
            OutputTestMethodName();
            var entity = new NonIdentityGuidEntity { Id = Guid.NewGuid(), Name = "GuidTest" };
            var result = _provider.Insert(entity);
            Assert.IsNotNull(result);

            var reloaded = _provider.Get<NonIdentityGuidEntity>(entity.Id);
            Assert.IsNotNull(reloaded);
            Assert.AreEqual("GuidTest", reloaded.Name);

            _provider.BeginTransaction();
            _provider.Delete<NonIdentityGuidEntity>(e => e.Id == entity.Id);
            _provider.CommitTransaction();
        }

        [TestMethod]
        public void Insert_StringPrimaryKey_Works()
        {
            OutputTestMethodName();
            var entity = new NonIdentityStringEntity { Id = $"test-{Guid.NewGuid():N}", Name = "StringTest" };
            var result = _provider.Insert(entity);
            Assert.IsNotNull(result);

            var reloaded = _provider.Get<NonIdentityStringEntity>(entity.Id);
            Assert.IsNotNull(reloaded);
            Assert.AreEqual("StringTest", reloaded.Name);

            _provider.BeginTransaction();
            _provider.Delete<NonIdentityStringEntity>(e => e.Id == entity.Id);
            _provider.CommitTransaction();
        }

        #endregion

        #region Update Tests

        [TestMethod]
        public void Update_PersonUpdates()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var person = _provider.Get<Person>(personId);
            var originalName = person.FirstName;
            person.FirstName = $"Updated{guid}";
            _provider.Update(person);
            var updatedPerson = _provider.Get<Person>(personId);
            Assert.IsNotNull(updatedPerson);
            Assert.AreNotEqual(originalName, updatedPerson.FirstName);
        }

        #endregion

        #region Query Where Tests

        [TestMethod]
        public void Query_WithExpression_ReturnsFilteredAddresses()
        {
            OutputTestMethodName();
            const string stateCode = "IL";
            var addressId = InsertTestAddress($"123 Main St {Guid.NewGuid()}", null, "Springfield", stateCode, "62704");
            var addresses = _provider.Query<Address>().Where(a => a.StateCode == stateCode).ToList();
            Assert.IsTrue(addresses.Any(x => x.Id == addressId && x.StateCode == stateCode));
        }

        [TestMethod]
        public void Query_WhenExpressionMatches_ReturnsEntities()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var result = _provider.Query<Person>().Where(x => x.FirstName == guid && x.LastName == guid).ToList();
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(guid, result[0].FirstName);
        }

        [TestMethod]
        public void Query_WhenExpressionDoesNotMatch_ReturnsEmpty()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            _provider.Insert(new Person { FirstName = "Unique", LastName = "Person", UniqueId = Guid.NewGuid() });
            var result = _provider.Query<Person>().Where(x => x.FirstName == guid && x.LastName == guid).ToList();
            Assert.IsFalse(result.Any());
        }

        [TestMethod]
        public void Query_WithContainsList_ReturnsMatchingEntities()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            _provider.Insert(new Person { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" });
            _provider.Insert(new Person { LastName = "Johnson", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" });
            _provider.Insert(new Person { LastName = "Doe", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" });

            var lastNames = new List<string> { "Smith", "Johnson" };
            var result = _provider.Query<Person>()
                .Where(p => p.FirstName == guid && p.LastName != null && lastNames.Contains(p.LastName))
                .ToList();
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.All(p => p.LastName == "Smith" || p.LastName == "Johnson"));
        }

        [TestMethod]
        public void Query_WithContainsArray_ReturnsMatchingEntities()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            _provider.Insert(new Person { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" });
            _provider.Insert(new Person { LastName = "Johnson", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" });
            _provider.Insert(new Person { LastName = "Doe", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" });

            var lastNames = new[] { "Smith", "Johnson" };
            var result = _provider.Query<Person>()
                .Where(p => p.FirstName == guid && lastNames.Contains(p.LastName))
                .ToList();
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void Query_WithEmptyContainsList_ReturnsEmpty()
        {
            OutputTestMethodName();
            var lastNames = new string[] { };
            var result = _provider.Query<Person>().Where(p => lastNames.Contains(p.LastName)).ToList();
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void Query_LastNameStartsWith_ReturnsCorrectPersons()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            _provider.Insert(new Person { LastName = "Johnson", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" });
            _provider.Insert(new Person { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" });
            var result = _provider.Query<Person>().Where(x => x.FirstName == guid && x.LastName.StartsWith("J")).ToList();
            Assert.IsTrue(result.Any() && result.All(x => x.LastName.StartsWith("J")));
        }

        [TestMethod]
        public void Query_LastNameEndsWith_ReturnsCorrectPersons()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            _provider.Insert(new Person { LastName = "Jones", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" });
            _provider.Insert(new Person { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" });
            var result = _provider.Query<Person>().Where(x => x.FirstName == guid && x.LastName.EndsWith("s")).ToList();
            Assert.IsTrue(result.Any() && result.All(x => x.LastName.EndsWith("s")));
        }

        [TestMethod]
        public void Query_LastNameContains_ReturnsCorrectPersons()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            _provider.Insert(new Person { LastName = "Johnson", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" });
            _provider.Insert(new Person { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" });
            var result = _provider.Query<Person>().Where(x => x.FirstName == guid && x.LastName.Contains("on")).ToList();
            Assert.IsTrue(result.Any() && result.All(x => x.LastName.Contains("on")));
        }

        #endregion

        #region Query FirstOrDefault / First / LastOrDefault Tests

        [TestMethod]
        public void Query_FirstOrDefaultWithPredicate_ReturnsFirstMatchingEntity()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var result = _provider.Query<Person>().FirstOrDefault(x => x.FirstName == guid);
            Assert.IsNotNull(result);
            Assert.AreEqual(guid, result.FirstName);
        }

        [TestMethod]
        public void Query_FirstWithPredicate_ReturnsFirstMatchingEntity()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var result = _provider.Query<Person>().First(x => x.FirstName == guid && x.LastName == guid);
            Assert.IsNotNull(result);
            Assert.AreEqual(guid, result.FirstName);
        }

        [TestMethod]
        public void Query_FirstWithPredicate_ThrowsIfNoMatch()
        {
            OutputTestMethodName();
            var newGuid = Guid.NewGuid().ToString();
            Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Query<Person>().First(x => x.FirstName == newGuid));
        }

        [TestMethod]
        public void Query_LastOrDefaultWithPredicate_ReturnsLastMatchingEntity()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId1 = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            var personId2 = InsertTestPerson(guid, "B", guid, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            var result = _provider.Query<Person>().LastOrDefault(x => x.FirstName == guid && x.LastName == guid);
            Assert.IsNotNull(result);
            Assert.AreEqual(personId2, result.Id);
        }

        #endregion

        #region OrderBy Tests

        [TestMethod]
        public void Query_OrderBy_ReturnsOrderedResults()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", "Zimmerman", DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            InsertTestPerson(guid, "B", "AdAMS", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());
            var result = _provider.Query<Person>().Where(x => x.FirstName == guid).OrderBy(x => x.LastName).ToList();
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("AdAMS", result[0].LastName);
            Assert.AreEqual("Zimmerman", result[1].LastName);
        }

        [TestMethod]
        public void Query_OrderByDescending_ReturnsOrderedResults()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", "Zimmerman", DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            InsertTestPerson(guid, "B", "AdAMS", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());
            var result = _provider.Query<Person>().Where(x => x.FirstName == guid).OrderByDescending(x => x.LastName).ToList();
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("Zimmerman", result[0].LastName);
            Assert.AreEqual("AdAMS", result[1].LastName);
        }

        [TestMethod]
        public void Query_ThenBy_ReturnsOrderedResults()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "D", "AdAMS", DateTime.Now.AddYears(-40), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "C", "AdAMS", DateTime.Now.AddYears(-35), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "B", "Jones", DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "A", "Jones", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .OrderBy(x => x.LastName).ThenBy(x => x.MiddleInitial)
                .ToList();
            Assert.AreEqual(4, result.Count);
            Assert.AreEqual("C", result[0].MiddleInitial);
            Assert.AreEqual("D", result[1].MiddleInitial);
            Assert.AreEqual("A", result[2].MiddleInitial);
            Assert.AreEqual("B", result[3].MiddleInitial);
            Assert.IsTrue(_sb.ToString().Contains("ORDER BY", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void Query_ThenByDescending_ReturnsOrderedResults()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "D", "AdAMS", DateTime.Now.AddYears(-40), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "C", "AdAMS", DateTime.Now.AddYears(-35), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "B", "Jones", DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "A", "Jones", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .OrderBy(x => x.LastName).ThenByDescending(x => x.MiddleInitial)
                .ToList();
            Assert.AreEqual(4, result.Count);
            Assert.AreEqual("D", result[0].MiddleInitial);
            Assert.AreEqual("C", result[1].MiddleInitial);
            Assert.AreEqual("B", result[2].MiddleInitial);
            Assert.AreEqual("A", result[3].MiddleInitial);
            Assert.IsTrue(_sb.ToString().Contains("DESC", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void Query_OrderByWithSkipTake_ReturnsCorrectSubset()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            foreach (var name in new[] { "Adams", "Baker", "Carter", "Davis" })
                InsertTestPerson(guid, "A", name, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());

            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .OrderBy(x => x.LastName).Skip(1).Take(2)
                .ToList();
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("Baker", result[0].LastName);
            Assert.AreEqual("Carter", result[1].LastName);
        }

        [TestMethod]
        public void Query_SkipTake_ReturnsCorrectSubset()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            for (int i = 0; i < 10; i++)
                InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid && x.LastName == guid)
                .Skip(5).Take(3)
                .ToList();
            Assert.AreEqual(3, result.Count);
        }

        #endregion

        #region Aggregate Tests

        [TestMethod]
        public void Query_AnyWithPredicate_ReturnsTrueIfMatches()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            var result = _provider.Query<Person>().Any(x => x.FirstName == guid);
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void Query_AllWithPredicate_ReturnsTrueIfAllMatch()
        {
            OutputTestMethodName();
            var firstNameGuid = Guid.NewGuid().ToString();
            var lastNameGuid = Guid.NewGuid().ToString();
            InsertTestPerson(firstNameGuid, "A", lastNameGuid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(firstNameGuid, "B", lastNameGuid, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            var result = _provider.Query<Person>().Where(x => x.FirstName == firstNameGuid).All(x => x.LastName == lastNameGuid);
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void Query_AllWithPredicate_ReturnsFalseIfNotAllMatch()
        {
            OutputTestMethodName();
            var firstNameGuid = Guid.NewGuid().ToString();
            var lastNameGuid = Guid.NewGuid().ToString();
            InsertTestPerson(firstNameGuid, "A", lastNameGuid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(firstNameGuid, "B", "DifferentLastName", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            var result = _provider.Query<Person>().Where(x => x.FirstName == firstNameGuid).All(x => x.LastName == lastNameGuid);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void Query_CountWithPredicate_ReturnsCorrectCount()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "B", guid, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            var count = _provider.Query<Person>().Count(x => x.FirstName == guid);
            Assert.AreEqual(2, count);
        }

        [TestMethod]
        public void Query_MaxIdWithSelector_ReturnsMaximum()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var person1Id = InsertTestPerson(guid, "A", guid, new DateTime(1990, 1, 1), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            var person2Id = InsertTestPerson(guid, "B", guid, new DateTime(2000, 1, 1), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            var maxId = _provider.Query<Person>().Where(x => x.FirstName == guid).Max(x => x.Id);
            Assert.AreEqual(person2Id, maxId);
            Assert.IsTrue(person2Id > person1Id);
        }

        [TestMethod]
        public void Query_MinWithSelector_ReturnsMinimum()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var birthdate1 = new DateTime(1990, 1, 1);
            var birthdate2 = new DateTime(2000, 1, 1);
            InsertTestPerson(guid, "A", guid, birthdate1, "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "B", guid, birthdate2, "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            var minBirthdate = _provider.Query<Person>().Where(x => x.FirstName == guid).Min(x => x.Birthdate);
            Assert.AreEqual(birthdate1, minBirthdate);
        }

        [TestMethod]
        public void Query_Sum_ReturnsCorrectSum()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", "Sum1", null, "Male", Guid.NewGuid());
            InsertTestPerson(guid, "B", "Sum2", null, "Female", Guid.NewGuid());
            InsertTestPerson(guid, "C", "Sum3", null, "Male", Guid.NewGuid());
            var people = _provider.Query<PersonEntity>().Where(p => p.FirstName == guid).ToList();
            var expectedSum = people.Sum(p => p.Id);
            var actualSum = _provider.Query<PersonEntity>().Where(p => p.FirstName == guid).Sum(p => p.Id);
            Assert.AreEqual(expectedSum, actualSum);
        }

        #endregion

        #region Transaction Tests

        [TestMethod]
        public void Transaction_BeginCommit()
        {
            OutputTestMethodName();
            _provider.BeginTransaction();
            var person = new Person { FirstName = Guid.NewGuid().ToString(), LastName = Guid.NewGuid().ToString(), UniqueId = Guid.NewGuid() };
            _provider.Insert(person);
            _provider.CommitTransaction();
            var committedPerson = _provider.Get<Person>(person.Id);
            Assert.IsNotNull(committedPerson);
        }

        [TestMethod]
        public void Transaction_BeginRollback()
        {
            OutputTestMethodName();
            _provider.BeginTransaction();
            var person = new Person { FirstName = Guid.NewGuid().ToString(), LastName = Guid.NewGuid().ToString(), UniqueId = Guid.NewGuid() };
            _provider.Insert(person);
            var addedPerson = _provider.Get<Person>(person.Id);
            Assert.IsNotNull(addedPerson);
            _provider.RollbackTransaction();
            var rolledBackPerson = _provider.Get<Person>(person.Id);
            Assert.IsNull(rolledBackPerson);
        }

        [TestMethod]
        public void Transaction_MultipleOperations()
        {
            OutputTestMethodName();
            _provider.BeginTransaction();
            var person = new Person { FirstName = Guid.NewGuid().ToString(), LastName = Guid.NewGuid().ToString(), UniqueId = Guid.NewGuid() };
            _provider.Insert(person);
            var address = new Address { Line1 = "Test St", City = "TestCity", StateCode = "TC", PostalCode = "12345" };
            _provider.Insert(address);
            var link = new PersonAddress { PersonId = person.Id, AddressId = address.Id };
            _provider.Insert(link);
            Assert.IsNotNull(_provider.Get<Person>(person.Id));
            Assert.IsNotNull(_provider.Get<Address>(address.Id));
            Assert.IsNotNull(_provider.Get<PersonAddress>(link.Id));
            _provider.CommitTransaction();
            Assert.IsNotNull(_provider.Get<Person>(person.Id));
            Assert.IsNotNull(_provider.Get<Address>(address.Id));
            Assert.IsNotNull(_provider.Get<PersonAddress>(link.Id));
        }

        #endregion

        #region Delete Tests

        [TestMethod]
        public void Delete_WithValidWhereClauseAndTransaction_DeletesEntity()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            _provider.BeginTransaction();
            var deleted = _provider.Delete<Person>(x => x.Id == personId);
            _provider.CommitTransaction();
            Assert.AreEqual(1, deleted);
            Assert.IsNull(_provider.Get<Person>(personId));
        }

        [TestMethod]
        public void Delete_WithoutTransaction_ThrowsException()
        {
            OutputTestMethodName();
            Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Delete<Person>(p => p.Id == -1));
        }

        [TestMethod]
        public void Query_WithUnsupportedExpression_ThrowsException()
        {
            OutputTestMethodName();
            Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<PersonEntity>().Where(p => p.FirstName.GetHashCode() == 123).ToList());
        }

        #endregion
    }
}
