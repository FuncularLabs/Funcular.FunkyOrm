using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Objects.Person;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Objects.Address;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Objects;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Objects.User;
using Microsoft.Data.Sqlite;

namespace Funcular.Data.Orm.Sqlite.Tests
{
    [TestClass]
    public class SqliteDataProviderIntegrationTests
    {
        protected string _connectionString;
        public SqliteOrmDataProvider _provider;
        protected readonly StringBuilder _sb = new();
        private static string _dbPath;

        public void OutputTestMethodName([CallerMemberName] string callerMemberName = "")
        {
            Debug.WriteLine($"\r\nTest: {callerMemberName}");
        }

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"funky_sqlite_tests_{Guid.NewGuid():N}.db");
            var connStr = $"Data Source={_dbPath}";
            using var conn = new SqliteConnection(connStr);
            conn.Open();

            // Create schema
            var schemaPath = Path.Combine(AppContext.BaseDirectory, "Database", "integration_test_schema.sql");
            if (File.Exists(schemaPath))
            {
                var schemaSql = File.ReadAllText(schemaPath);
                using var cmd = new SqliteCommand(schemaSql, conn);
                cmd.ExecuteNonQuery();
            }
            else
            {
                // Inline fallback
                var sql = @"
CREATE TABLE IF NOT EXISTS person (id INTEGER PRIMARY KEY AUTOINCREMENT, first_name TEXT, middle_initial TEXT, last_name TEXT, birthdate TEXT, gender TEXT, dateutc_created TEXT NOT NULL DEFAULT (datetime('now')), dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')), unique_id TEXT, employer_id INTEGER);
CREATE TABLE IF NOT EXISTS address (id INTEGER PRIMARY KEY AUTOINCREMENT, line_1 TEXT, line_2 TEXT, city TEXT, state_code TEXT, postal_code TEXT, dateutc_created TEXT NOT NULL DEFAULT (datetime('now')), dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')), country_id INTEGER);
CREATE TABLE IF NOT EXISTS person_address (id INTEGER PRIMARY KEY AUTOINCREMENT, person_id INTEGER NOT NULL, address_id INTEGER NOT NULL, is_primary INTEGER NOT NULL DEFAULT 0, address_type_value INTEGER DEFAULT 0, dateutc_created TEXT NOT NULL DEFAULT (datetime('now')), dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')), FOREIGN KEY (person_id) REFERENCES person(id), FOREIGN KEY (address_id) REFERENCES address(id));
CREATE TABLE IF NOT EXISTS non_identity_guid_entity (id TEXT PRIMARY KEY, name TEXT);
CREATE TABLE IF NOT EXISTS non_identity_string_entity (id TEXT PRIMARY KEY, name TEXT);
CREATE TABLE IF NOT EXISTS ""User"" (""Key"" INTEGER PRIMARY KEY AUTOINCREMENT, ""Name"" TEXT, ""Order"" INTEGER NOT NULL DEFAULT 0, ""Select"" INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS country (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS organization (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, headquarters_address_id INTEGER);
CREATE TABLE IF NOT EXISTS project_category (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, code TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS project (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, organization_id INTEGER NOT NULL, lead_id INTEGER, category_id INTEGER, budget REAL, score INTEGER, metadata TEXT, dateutc_created TEXT NOT NULL DEFAULT (datetime('now')), dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')));
CREATE TABLE IF NOT EXISTS project_milestone (id INTEGER PRIMARY KEY AUTOINCREMENT, project_id INTEGER NOT NULL, title TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'pending', due_date TEXT, completed_date TEXT);
CREATE TABLE IF NOT EXISTS project_note (id INTEGER PRIMARY KEY AUTOINCREMENT, project_id INTEGER NOT NULL, author_id INTEGER, content TEXT NOT NULL, category TEXT NOT NULL DEFAULT 'general', dateutc_created TEXT NOT NULL DEFAULT (datetime('now')));";
                using var cmd = new SqliteCommand(sql, conn);
                cmd.ExecuteNonQuery();
            }

            // Seed data
            var dataPath = Path.Combine(AppContext.BaseDirectory, "Database", "integration_test_data.sql");
            if (File.Exists(dataPath))
            {
                var dataSql = File.ReadAllText(dataPath);
                using var cmd = new SqliteCommand(dataSql, conn);
                cmd.ExecuteNonQuery();
            }
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            if (_dbPath != null && File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { /* best effort */ }
            }
        }

        [TestInitialize]
        public void Setup()
        {
            _sb.Clear();
            _connectionString = $"Data Source={_dbPath}";

            _provider = new SqliteOrmDataProvider(_connectionString)
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
            InsertTestAddress("1 Test Ln", null, "TestCity", "TX", "75001");
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

        #region Query FirstOrDefault / First Tests

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
        public void Query_FirstWithPredicate_ThrowsIfNoMatch()
        {
            OutputTestMethodName();
            var newGuid = Guid.NewGuid().ToString();
            Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Query<Person>().First(x => x.FirstName == newGuid));
        }

        #endregion

        #region OrderBy Tests

        [TestMethod]
        public void Query_OrderBy_ReturnsOrderedResults()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", "Zimmerman", DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            InsertTestPerson(guid, "B", "Adams", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());
            var result = _provider.Query<Person>().Where(x => x.FirstName == guid).OrderBy(x => x.LastName).ToList();
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("Adams", result[0].LastName);
            Assert.AreEqual("Zimmerman", result[1].LastName);
        }

        [TestMethod]
        public void Query_OrderByDescending_ReturnsOrderedResults()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", "Zimmerman", DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            InsertTestPerson(guid, "B", "Adams", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());
            var result = _provider.Query<Person>().Where(x => x.FirstName == guid).OrderByDescending(x => x.LastName).ToList();
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("Zimmerman", result[0].LastName);
            Assert.AreEqual("Adams", result[1].LastName);
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

        #endregion

        #region Aggregate Tests

        [TestMethod]
        public void Query_Count_ReturnsCorrectCount()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            InsertTestPerson(guid, "B", guid, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());
            var count = _provider.Query<Person>().Where(x => x.FirstName == guid).Count();
            Assert.AreEqual(2, count);
        }

        [TestMethod]
        public void Query_Any_ReturnsTrueWhenMatches()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var any = _provider.Query<Person>().Where(x => x.FirstName == guid).Any();
            Assert.IsTrue(any);
        }

        [TestMethod]
        public void Query_Any_ReturnsFalseWhenNoMatches()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var any = _provider.Query<Person>().Where(x => x.FirstName == guid).Any();
            Assert.IsFalse(any);
        }

        #endregion

        #region Delete Tests

        [TestMethod]
        public void Delete_WithPredicate_RemovesEntity()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());

            _provider.BeginTransaction();
            var deleted = _provider.Delete<Person>(p => p.FirstName == guid);
            _provider.CommitTransaction();

            Assert.IsTrue(deleted > 0);
            var result = _provider.Query<Person>().Where(x => x.FirstName == guid).ToList();
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void Delete_RequiresTransaction()
        {
            OutputTestMethodName();
            Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Delete<Person>(p => p.FirstName == "anything"));
        }

        #endregion

        #region Transaction Tests

        [TestMethod]
        public void Update_WithinTransaction_DoesNotThrowAndPersists()
        {
            OutputTestMethodName();
            // Regression (3.6.1): Update inside a transaction must not trip the transactional-concurrency
            // guard via a nested read scope.
            _provider.BeginTransaction();
            try
            {
                var person = new Person { FirstName = Guid.NewGuid().ToString(), LastName = Guid.NewGuid().ToString(), UniqueId = Guid.NewGuid() };
                _provider.Insert(person);

                person.FirstName = "Updated-" + Guid.NewGuid();
                _provider.Update(person);

                var fetched = _provider.Get<Person>(person.Id);
                Assert.IsNotNull(fetched);
                Assert.AreEqual(person.FirstName, fetched.FirstName);
            }
            finally
            {
                _provider.RollbackTransaction();
            }
        }

        [TestMethod]
        public void Transaction_Rollback_RevertsChanges()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());

            _provider.BeginTransaction();
            _provider.Delete<Person>(p => p.Id == personId);
            _provider.RollbackTransaction();

            var person = _provider.Get<Person>(personId);
            Assert.IsNotNull(person);
        }

        #endregion

        #region Async Tests

        [TestMethod]
        public async Task GetAsync_WithExistingId_ReturnsPerson()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var person = await _provider.GetAsync<Person>(personId);
            Assert.IsNotNull(person);
            Assert.AreEqual(personId, person.Id);
        }

        [TestMethod]
        public async Task InsertAsync_ReturnsPrimaryKey()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var newPerson = new Person
            {
                FirstName = guid,
                LastName = guid,
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow,
                UniqueId = Guid.NewGuid()
            };
            var result = await _provider.InsertAsync(newPerson);
            Assert.IsNotNull(result);
            Assert.IsTrue((int)result > 0);
        }

        [TestMethod]
        public async Task UpdateAsync_WithinTransaction_DoesNotThrowAndPersists()
        {
            OutputTestMethodName();
            // Regression (3.6.1): UpdateAsync inside a transaction must not trip the
            // transactional-concurrency guard via a nested read scope.
            _provider.BeginTransaction();
            try
            {
                var person = new Person { FirstName = Guid.NewGuid().ToString(), LastName = Guid.NewGuid().ToString(), UniqueId = Guid.NewGuid() };
                await _provider.InsertAsync(person);

                person.FirstName = "Updated-" + Guid.NewGuid();
                await _provider.UpdateAsync(person);

                var fetched = await _provider.GetAsync<Person>(person.Id);
                Assert.IsNotNull(fetched);
                Assert.AreEqual(person.FirstName, fetched.FirstName);
            }
            finally
            {
                _provider.RollbackTransaction();
            }
        }

        [TestMethod]
        public async Task UpdateAsync_UpdatesEntity()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var person = await _provider.GetAsync<Person>(personId);
            person.FirstName = $"AsyncUpdated{guid}";
            await _provider.UpdateAsync(person);
            var updated = await _provider.GetAsync<Person>(personId);
            Assert.AreEqual($"AsyncUpdated{guid}", updated.FirstName);
        }

        #endregion

        #region Reserved Word Tests

        [TestMethod]
        public void ReservedWordTable_InsertAndQuery_Works()
        {
            OutputTestMethodName();
            var user = new User { Name = $"Test_{Guid.NewGuid():N}", Order = 42, Select = true };
            _provider.Insert(user);
            Assert.IsTrue(user.Key > 0);

            var loaded = _provider.Get<User>(user.Key);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(user.Name, loaded.Name);
            Assert.AreEqual(42, loaded.Order);
            Assert.AreEqual(true, loaded.Select);
        }

        #endregion

        #region Connection String Resolution Tests

        [TestMethod]
        public void ConnectionString_ResolvesRelativePath()
        {
            OutputTestMethodName();
            var relativePath = "test_relative.db";
            var provider = new SqliteOrmDataProvider($"Data Source={relativePath}");
            // Should not throw - validates that relative paths are resolved
            Assert.IsNotNull(provider);
            provider.Dispose();
        }

        #endregion

        #region Additional Query Tests

        [TestMethod]
        public void Query_ThenBy_ReturnsOrderedResults()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "D", "Adams", DateTime.Now.AddYears(-40), "Male", Guid.NewGuid());
            InsertTestPerson(guid, "C", "Adams", DateTime.Now.AddYears(-35), "Female", Guid.NewGuid());
            InsertTestPerson(guid, "B", "Jones", DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            InsertTestPerson(guid, "A", "Jones", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());

            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .OrderBy(x => x.LastName)
                .ThenBy(x => x.MiddleInitial)
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
            InsertTestPerson(guid, "D", "Adams", DateTime.Now.AddYears(-40), "Male", Guid.NewGuid());
            InsertTestPerson(guid, "C", "Adams", DateTime.Now.AddYears(-35), "Female", Guid.NewGuid());
            InsertTestPerson(guid, "B", "Jones", DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            InsertTestPerson(guid, "A", "Jones", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());

            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .OrderBy(x => x.LastName)
                .ThenByDescending(x => x.MiddleInitial)
                .ToList();

            Assert.AreEqual(4, result.Count);
            Assert.AreEqual("D", result[0].MiddleInitial);
            Assert.AreEqual("C", result[1].MiddleInitial);
            Assert.AreEqual("B", result[2].MiddleInitial);
            Assert.AreEqual("A", result[3].MiddleInitial);
            Assert.IsTrue(_sb.ToString().Contains("ORDER BY", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(_sb.ToString().Contains("DESC", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void Query_FirstWithPredicate_ReturnsFirstMatchingEntity()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());

            var result = _provider.Query<Person>()
                .First(x => x.FirstName == guid && x.LastName == guid);

            Assert.IsNotNull(result);
            Assert.AreEqual(guid, result.FirstName);
            Assert.AreEqual(guid, result.LastName);
        }

        [TestMethod]
        public void Query_LastOrDefaultWithPredicate_ReturnsLastMatchingEntity()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId1 = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var personId2 = InsertTestPerson(guid, "B", guid, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());

            var result = _provider.Query<Person>()
                .LastOrDefault(x => x.FirstName == guid && x.LastName == guid);

            Assert.IsNotNull(result);
            Assert.AreEqual(personId2, result.Id);
        }

        [TestMethod]
        public void Query_SkipTake_ReturnsCorrectSubset()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            for (int i = 0; i < 10; i++)
                InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());

            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid && x.LastName == guid)
                .Skip(5)
                .Take(3)
                .ToList();

            Assert.AreEqual(3, result.Count);
        }

        [TestMethod]
        public void Query_Person_WithLastNameInArray_ReturnsCorrectPersons()
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
            Assert.IsTrue(result.All(p => p.LastName == "Smith" || p.LastName == "Johnson"));
        }

        [TestMethod]
        public void Query_Person_WithEmptyList_ReturnsEmptyResult()
        {
            OutputTestMethodName();
            var lastNames = new string[] { };

            var result = _provider.Query<Person>()
                .Where(p => lastNames.Contains(p.LastName))
                .ToList();

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void Query_Person_LastNameStartsWith_NoMatch_ReturnsEmptyList()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            _provider.Insert(new Person { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Male" });

            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid && x.LastName.StartsWith("ToString"))
                .ToList();

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void Query_Person_LastNameEndsWith_NoMatch_ReturnsEmptyList()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            _provider.Insert(new Person { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Female" });

            var value = Guid.NewGuid().ToString();
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid && x.LastName.EndsWith(value))
                .ToList();

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void Query_Person_LastNameContains_NoMatch_ReturnsEmptyList()
        {
            OutputTestMethodName();
            var firstGuid = Guid.NewGuid().ToString();
            _provider.Insert(new Person { LastName = "Smith", FirstName = firstGuid, UniqueId = Guid.NewGuid(), Gender = "Male" });

            var secondGuid = Guid.NewGuid().ToString();
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == firstGuid && x.LastName.Contains(secondGuid))
                .ToList();

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void Query_AnyWithPredicate_ReturnsTrueIfMatches()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());

            var result = _provider.Query<Person>().Any(x => x.FirstName == guid);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void Query_AllWithPredicate_ReturnsTrueIfAllMatch()
        {
            OutputTestMethodName();
            var firstNameGuid = Guid.NewGuid().ToString();
            var lastNameGuid = Guid.NewGuid().ToString();
            InsertTestPerson(firstNameGuid, "A", lastNameGuid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            InsertTestPerson(firstNameGuid, "B", lastNameGuid, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());

            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == firstNameGuid)
                .All(x => x.LastName == lastNameGuid);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void Query_AllWithPredicate_ReturnsFalseIfNotAllMatch()
        {
            OutputTestMethodName();
            var firstNameGuid = Guid.NewGuid().ToString();
            var lastNameGuid = Guid.NewGuid().ToString();
            InsertTestPerson(firstNameGuid, "A", lastNameGuid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            InsertTestPerson(firstNameGuid, "B", "DifferentLastName", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());

            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == firstNameGuid)
                .All(x => x.LastName == lastNameGuid);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void Query_CountWithPredicate_ReturnsCorrectCount()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            InsertTestPerson(guid, "B", guid, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());

            var count = _provider.Query<Person>().Count(x => x.FirstName == guid);

            Assert.AreEqual(2, count);
        }

        [TestMethod]
        public void Query_MaxIdWithSelector_ReturnsMaximum()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var person1Id = InsertTestPerson(guid, "A", guid, new DateTime(1990, 1, 1), "Male", Guid.NewGuid());
            var person2Id = InsertTestPerson(guid, "B", guid, new DateTime(2000, 1, 1), "Female", Guid.NewGuid());

            var maxId = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .Max(x => x.Id);

            Assert.AreEqual(person2Id, maxId);
            Assert.IsTrue(person2Id > person1Id);
        }

        [TestMethod]
        public void Query_MaxDateWithSelector_ReturnsMaximum()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var date1 = new DateTime(1990, 1, 1);
            var date2 = new DateTime(2000, 1, 1);
            InsertTestPerson(guid, "A", guid, date1, "Male", Guid.NewGuid());
            InsertTestPerson(guid, "B", guid, date2, "Female", Guid.NewGuid());

            var maxBirthdate = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .Max(x => x.Birthdate);

            Assert.AreEqual(date2, maxBirthdate);
        }

        [TestMethod]
        public void Query_MinWithSelector_ReturnsMinimum()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var birthdate1 = new DateTime(1990, 1, 1);
            var birthdate2 = new DateTime(2000, 1, 1);
            InsertTestPerson(guid, "A", guid, birthdate1, "Male", Guid.NewGuid());
            InsertTestPerson(guid, "B", guid, birthdate2, "Female", Guid.NewGuid());

            var minBirthdate = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .Min(x => x.Birthdate);

            Assert.AreEqual(birthdate1, minBirthdate);
        }

        [TestMethod]
        public void Query_Person_WithBirthdateInRange_ReturnsCorrectPersons()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            _provider.Insert(new Person { LastName = "Smith", FirstName = guid, Birthdate = DateTime.Today.AddYears(-30), Gender = "Guid", UniqueId = Guid.NewGuid() });
            _provider.Insert(new Person { LastName = "Johnson", FirstName = guid, Birthdate = DateTime.Today.AddYears(-25), Gender = "Guid", UniqueId = Guid.NewGuid() });
            _provider.Insert(new Person { LastName = "Doe", FirstName = guid, Birthdate = DateTime.Today.AddYears(-40), Gender = "Guid", UniqueId = Guid.NewGuid() });

            var fromDate = DateTime.Today.AddYears(-35);
            var toDate = DateTime.Today.AddYears(-20);

            var result = _provider.Query<Person>()
                .Where(p => p.FirstName == guid && p.Birthdate >= fromDate && p.Birthdate <= toDate)
                .ToList();

            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.All(x => x.LastName == "Smith" || x.LastName == "Johnson"));
        }

        [TestMethod]
        public void Query_SelectWithIsTwentyOneOrOverProjection_GeneratesCaseStatement()
        {
            OutputTestMethodName();
            DateTime cutoff = DateTime.Now.AddYears(-21).Date;

            var guid = Guid.NewGuid().ToString();
            var personUnder21Id = InsertTestPerson("Under21", "U", guid, DateTime.Now.AddYears(-20).Date, "Male", Guid.NewGuid());
            var personOver21Id = InsertTestPerson("Over21", "O", guid, DateTime.Now.AddYears(-25).Date, "Female", Guid.NewGuid());
            var personExactly21Id = InsertTestPerson("Exactly21", "E", guid, cutoff.AddDays(-1), "Other", Guid.NewGuid());
            var personNullBirthdateId = InsertTestPerson("NullDOB", "N", guid, null, "Male", Guid.NewGuid());

            var results = _provider.Query<Person>()
                .Where(p => p.LastName == guid)
                .Select(p => new Person
                {
                    Id = p.Id,
                    FirstName = p.FirstName,
                    MiddleInitial = p.MiddleInitial,
                    LastName = p.LastName,
                    Birthdate = p.Birthdate,
                    Gender = p.Gender,
                    UniqueId = p.UniqueId,
                    DateUtcCreated = p.DateUtcCreated,
                    DateUtcModified = p.DateUtcModified,
                    IsTwentyOneOrOver = p.Birthdate != null && p.Birthdate <= cutoff ? true : false
                })
                .ToList();

            Assert.AreEqual(4, results.Count);

            var under21Result = results.First(r => r.Id == personUnder21Id);
            Assert.IsFalse(under21Result.IsTwentyOneOrOver);

            var over21Result = results.First(r => r.Id == personOver21Id);
            Assert.IsTrue(over21Result.IsTwentyOneOrOver);

            var exactly21Result = results.First(r => r.Id == personExactly21Id);
            Assert.IsTrue(exactly21Result.IsTwentyOneOrOver);

            var nullDobResult = results.First(r => r.Id == personNullBirthdateId);
            Assert.IsFalse(nullDobResult.IsTwentyOneOrOver);
        }

        [TestMethod]
        public void Query_SelectWithSalutationProjection_GeneratesCaseStatement()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var fredId = InsertTestPerson("Fred", "F", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var lisaId = InsertTestPerson("Lisa", "L", guid, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());
            var maudeId = InsertTestPerson("Maude", "M", guid, DateTime.Now.AddYears(-40), "Female", Guid.NewGuid());
            var otherId = InsertTestPerson("Other", "O", guid, DateTime.Now.AddYears(-35), "Male", Guid.NewGuid());

            var results = _provider.Query<Person>()
                .Where(p => p.LastName == guid)
                .Select(p => new Person
                {
                    Id = p.Id,
                    FirstName = p.FirstName,
                    MiddleInitial = p.MiddleInitial,
                    LastName = p.LastName,
                    Birthdate = p.Birthdate,
                    Gender = p.Gender,
                    UniqueId = p.UniqueId,
                    DateUtcCreated = p.DateUtcCreated,
                    DateUtcModified = p.DateUtcModified,
                    Salutation = p.FirstName == "Fred" ? "Mr." : p.FirstName == "Lisa" ? "Ms." : p.FirstName == "Maude" ? "Mrs." : null
                })
                .ToList();

            Assert.AreEqual(4, results.Count);
            Assert.AreEqual("Mr.", results.First(r => r.Id == fredId).Salutation);
            Assert.AreEqual("Ms.", results.First(r => r.Id == lisaId).Salutation);
            Assert.AreEqual("Mrs.", results.First(r => r.Id == maudeId).Salutation);
            Assert.IsNull(results.First(r => r.Id == otherId).Salutation);
        }

        [TestMethod]
        public void Query_OrderByWithTernaryExpression_GeneratesCaseAndOrdersCorrectly()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var maleLast = "ZLast_" + guid;
            var femaleLast = "ALast_" + guid;
            var otherLast = "BLast_" + guid;

            InsertTestPerson("John", "J", maleLast, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            InsertTestPerson("Jane", "J", femaleLast, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());
            InsertTestPerson("Alex", "A", otherLast, DateTime.Now.AddYears(-20), "Other", Guid.NewGuid());

            var result = _provider.Query<Person>()
                .Where(p => p.LastName == maleLast || p.LastName == femaleLast || p.LastName == otherLast)
                .OrderBy(p => p.Gender == "Male" ? p.LastName : p.FirstName)
                .ToList();

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("Alex", result[0].FirstName);
            Assert.AreEqual("Jane", result[1].FirstName);
            Assert.AreEqual("John", result[2].FirstName);
            Assert.IsTrue(_sb.ToString().Contains("CASE", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(_sb.ToString().Contains("ORDER BY", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void Query_OrderByDescendingWithTernaryExpression_GeneratesCaseAndOrdersCorrectly()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var maleLast = "ZLast_" + guid;
            var femaleLast = "ALast_" + guid;
            var otherLast = "BLast_" + guid;

            InsertTestPerson("John", "J", maleLast, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            InsertTestPerson("Jane", "J", femaleLast, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());
            InsertTestPerson("Alex", "A", otherLast, DateTime.Now.AddYears(-20), "Other", Guid.NewGuid());

            var result = _provider.Query<Person>()
                .Where(p => p.LastName == maleLast || p.LastName == femaleLast || p.LastName == otherLast)
                .OrderByDescending(p => p.Gender == "Male" ? p.LastName : p.FirstName)
                .ToList();

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("John", result[0].FirstName);
            Assert.AreEqual("Jane", result[1].FirstName);
            Assert.AreEqual("Alex", result[2].FirstName);
            Assert.IsTrue(_sb.ToString().Contains("CASE", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(_sb.ToString().Contains("DESC", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void Query_ThenByWithTernaryExpression_GeneratesCaseAndOrdersCorrectly()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var sameLast = "SameLast_" + guid;

            InsertTestPerson("John", "Z", sameLast, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            InsertTestPerson("Jane", "A", sameLast, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());
            InsertTestPerson("Alex", "B", sameLast, DateTime.Now.AddYears(-20), "Other", Guid.NewGuid());

            var result = _provider.Query<Person>()
                .Where(p => p.LastName == sameLast)
                .OrderBy(p => p.LastName)
                .ThenBy(p => p.Gender == "Male" ? p.MiddleInitial : p.FirstName)
                .ToList();

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("Alex", result[0].FirstName);
            Assert.AreEqual("Jane", result[1].FirstName);
            Assert.AreEqual("John", result[2].FirstName);
            Assert.IsTrue(_sb.ToString().Contains("CASE", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void Query_SelectDoesNotIncludeUnmappedProperties()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());

            _sb.Clear();
            var person = _provider.Get<Person>(personId);

            var sql = _sb.ToString();
            Assert.IsTrue(sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(sql.Contains("Salutation", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void Insert_DoesNotIncludeUnmappedProperties()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();

            _sb.Clear();
            var person = new Person
            {
                FirstName = guid,
                MiddleInitial = "A",
                LastName = guid,
                Birthdate = DateTime.Now.AddYears(-30),
                Gender = "Male",
                Salutation = "Mr.",
                IsTwentyOneOrOver = true,
                UniqueId = Guid.NewGuid(),
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow
            };
            _provider.Insert(person);

            var sql = _sb.ToString();
            Assert.IsTrue(sql.Contains("INSERT", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(sql.Contains("Salutation", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(sql.Contains("IsTwentyOneOrOver", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void Query_With_Closure_In_Predicate_Succeeds()
        {
            OutputTestMethodName();
            var uniqueId = Guid.NewGuid();
            var personId = InsertTestPerson("ClosureTest", "C", "User", DateTime.Now.AddYears(-25), "Male", uniqueId);
            var addressId = InsertTestAddress("123 Closure Ln", null, "TestCity", "NY", "10001");

            var pa = new PersonAddress { PersonId = personId, AddressId = addressId };
            _provider.Insert(pa);

            var existingPa = _provider.Query<PersonAddress>()
                .FirstOrDefault(x => x.PersonId == personId && x.AddressId == addressId);

            Assert.IsNotNull(existingPa);
            Assert.AreEqual(personId, existingPa.PersonId);
            Assert.AreEqual(addressId, existingPa.AddressId);
        }

        [TestMethod]
        public void Query_MissingTable_Throws_Informative_Exception()
        {
            OutputTestMethodName();
            var threw = false;
            try
            {
                _provider.Query<MissingTable>().ToList();
            }
            catch (Exception ex)
            {
                threw = true;
                // SQLite may throw SqliteException or InvalidOperationException depending on provider path
                Assert.IsTrue(ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase),
                    $"Expected informative error but got: {ex.Message}");
            }
            Assert.IsTrue(threw, "Expected an exception for missing table.");
        }

        [TestMethod]
        public void Query_ChainedWhereWithMultipleArrayContains_ReturnsCorrectPersons()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            _provider.Insert(new Person { FirstName = guid, LastName = "Smith", Gender = "Male", UniqueId = Guid.NewGuid() });
            _provider.Insert(new Person { FirstName = guid, LastName = "Johnson", Gender = "Female", UniqueId = Guid.NewGuid() });
            _provider.Insert(new Person { FirstName = guid, LastName = "Doe", Gender = "Male", UniqueId = Guid.NewGuid() });

            var lastNames = new[] { "Smith", "Johnson", "Doe" };
            var genders = new[] { "Male" };

            var result = _provider.Query<Person>()
                .Where(p => p.FirstName == guid && lastNames.Contains(p.LastName))
                .Where(p => genders.Contains(p.Gender))
                .ToList();

            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.All(p => p.Gender == "Male"));
        }

        [TestMethod]
        public void Query_ChainedWhereWithOrderBySkipTake_ReturnsCorrectPersons()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            foreach (var name in new[] { "Adams", "Baker", "Carter", "Davis", "Evans" })
                InsertTestPerson(guid, "A", name, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());

            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .Where(x => x.Gender == "Male")
                .OrderBy(x => x.LastName)
                .Skip(1)
                .Take(3)
                .ToList();

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("Baker", result[0].LastName);
            Assert.AreEqual("Carter", result[1].LastName);
            Assert.AreEqual("Davis", result[2].LastName);
        }

        #endregion

        #region Additional Delete Tests

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
            var person = _provider.Get<Person>(personId);
            Assert.IsNull(person);
        }

        [TestMethod]
        public void Delete_WithoutWhereClause_ThrowsException()
        {
            OutputTestMethodName();
            _provider.BeginTransaction();
            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Delete<Person>(null));
            StringAssert.Contains(ex.Message, "WHERE clause");
            _provider.RollbackTransaction();
        }

        [TestMethod]
        public void Delete_WithEmptyWhereClause_ThrowsException()
        {
            OutputTestMethodName();
            _provider.BeginTransaction();
            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Delete<Person>(x => true));
            StringAssert.Contains(ex.Message, "WHERE clause");
            _provider.RollbackTransaction();
        }

        [TestMethod]
        public void Delete_DoesNotAffectOtherEntities()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId1 = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var personId2 = InsertTestPerson(guid, "B", guid, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());

            _provider.BeginTransaction();
            var deleted = _provider.Delete<Person>(x => x.Id == personId1);
            _provider.CommitTransaction();

            Assert.AreEqual(1, deleted);
            Assert.IsNull(_provider.Get<Person>(personId1));
            Assert.IsNotNull(_provider.Get<Person>(personId2));
        }

        [TestMethod]
        public void Delete_TrivialWhereClause_ThrowsException()
        {
            OutputTestMethodName();
            _provider.BeginTransaction();

            var ex1 = Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Delete<Person>(x => true));
            StringAssert.Contains(ex1.Message, "WHERE clause");

            _provider.RollbackTransaction();
        }

        [TestMethod]
        public void Delete_ById_DeletesEntityAndReturnsTrue()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());

            _provider.BeginTransaction();
            var result = _provider.Delete<Person>(personId);
            _provider.CommitTransaction();

            Assert.IsTrue(result);
            Assert.IsNull(_provider.Get<Person>(personId));
        }

        [TestMethod]
        public void Delete_ById_ReturnsFalseForNonExistentEntity()
        {
            OutputTestMethodName();
            _provider.BeginTransaction();
            var result = _provider.Delete<Person>(-999999);
            _provider.CommitTransaction();

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void Delete_ById_ThrowsIfNoTransaction()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());

            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Delete<Person>(personId));
            StringAssert.Contains(ex.Message, "transaction");

            // Cleanup
            _provider.BeginTransaction();
            _provider.Delete<Person>(personId);
            _provider.CommitTransaction();
        }

        [TestMethod]
        public void Delete_ById_DeletesEntity()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());

            _provider.BeginTransaction();
            _provider.Delete<Person>(personId);
            _provider.CommitTransaction();

            var person = _provider.Get<Person>(personId);
            Assert.IsNull(person);
        }

        #endregion

        #region Additional Transaction Tests

        [TestMethod]
        public void Transaction_BeginCommit()
        {
            OutputTestMethodName();
            _provider.BeginTransaction();
            var person = new Person
            {
                FirstName = Guid.NewGuid().ToString(),
                LastName = Guid.NewGuid().ToString(),
                UniqueId = Guid.NewGuid()
            };
            _provider.Insert(person);
            _provider.CommitTransaction();

            var committedPerson = _provider.Get<Person>(person.Id);
            Assert.IsNotNull(committedPerson);
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

        #region Additional Async Tests

        [TestMethod]
        public async Task GetListAsync_ReturnsAllPersonAddressLinks()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var addressId = InsertTestAddress("123 Main St", null, "Springfield", "IL", "62704");
            InsertTestPersonAddress(personId, addressId);
            var links = await _provider.GetListAsync<PersonAddress>();
            Assert.IsTrue(links.Any(l => l.PersonId == personId && l.AddressId == addressId));
        }

        [TestMethod]
        public async Task QueryAsync_CountWithPredicate_ReturnsCorrectCount()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            InsertTestPerson(guid, "B", guid, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());

            var count = await Task.Run(() => _provider.Query<Person>().Count(x => x.FirstName == guid));
            Assert.AreEqual(2, count);
        }

        [TestMethod]
        public async Task DeleteAsync_WithValidWhereClauseAndTransaction_DeletesEntity()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());

            _provider.BeginTransaction();
            var deleted = await _provider.DeleteAsync<Person>(x => x.Id == personId);
            _provider.CommitTransaction();

            Assert.IsTrue(deleted > 0);
            var person = await _provider.GetAsync<Person>(personId);
            Assert.IsNull(person);
        }

        [TestMethod]
        public async Task DeleteAsync_WithoutTransaction_ThrowsException()
        {
            OutputTestMethodName();
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
                await _provider.DeleteAsync<Person>(x => x.FirstName == "anything"));
        }

        #endregion

        #region Parity Tests (from PostgreSQL suite)

        [TestMethod]
        public void Query_Sum_ReturnsCorrectSum()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", "Sum1", null, "Male", Guid.NewGuid());
            InsertTestPerson(guid, "B", "Sum2", null, "Female", Guid.NewGuid());
            InsertTestPerson(guid, "C", "Sum3", null, "Male", Guid.NewGuid());
            var people = _provider.Query<Person>().Where(p => p.FirstName == guid).ToList();
            var expectedSum = people.Sum(p => p.Id);
            var actualSum = _provider.Query<Person>().Where(p => p.FirstName == guid).Sum(p => p.Id);
            Assert.AreEqual(expectedSum, actualSum);
        }

        [TestMethod]
        public void Query_WithContainsArray_ReturnsMatchingEntities()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", "Smith", null, "Male", Guid.NewGuid());
            InsertTestPerson(guid, "B", "Johnson", null, "Female", Guid.NewGuid());
            InsertTestPerson(guid, "C", "Doe", null, "Male", Guid.NewGuid());

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
        public async Task Insert_NewPerson_IncreasesCount_Async()
        {
            OutputTestMethodName();
            var before = _provider.Query<Person>().Count();
            await _provider.InsertAsync(new Person
            {
                FirstName = "AsyncInsert",
                MiddleInitial = "A",
                LastName = Guid.NewGuid().ToString(),
                Gender = "Male",
                UniqueId = Guid.NewGuid(),
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow
            });
            var after = _provider.Query<Person>().Count();
            Assert.AreEqual(before + 1, after);
        }

        [TestMethod]
        public async Task Update_PersonUpdates_Async()
        {
            OutputTestMethodName();
            var id = InsertTestPerson("AsyncUpdate", "U", Guid.NewGuid().ToString(), null, "Male", Guid.NewGuid());
            var person = await _provider.GetAsync<Person>(id);
            Assert.IsNotNull(person);
            person.FirstName = "AsyncUpdated";
            await _provider.UpdateAsync(person);
            var reloaded = await _provider.GetAsync<Person>(id);
            Assert.AreEqual("AsyncUpdated", reloaded.FirstName);
        }

        [TestMethod]
        public async Task Query_WithExpression_ReturnsFilteredAddresses_Async()
        {
            OutputTestMethodName();
            var results = await _provider.QueryAsync<Address>(a => a.City != null);
            Assert.IsNotNull(results);
        }

        [TestMethod]
        public async Task Transaction_BeginCommit_Async()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            _provider.BeginTransaction();
            await _provider.InsertAsync(new Person
            {
                FirstName = "TxAsync",
                MiddleInitial = "T",
                LastName = guid,
                Gender = "Male",
                UniqueId = Guid.NewGuid(),
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow
            });
            _provider.CommitTransaction();
            var result = _provider.Query<Person>().Where(p => p.LastName == guid).ToList();
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public async Task DeleteAsync_DoesNotAffectOtherEntities()
        {
            OutputTestMethodName();
            var guid1 = Guid.NewGuid().ToString();
            var guid2 = Guid.NewGuid().ToString();
            InsertTestPerson("Keep", "K", guid1, null, "Male", Guid.NewGuid());
            InsertTestPerson("Delete", "D", guid2, null, "Female", Guid.NewGuid());

            _provider.BeginTransaction();
            await _provider.DeleteAsync<Person>(p => p.LastName == guid2);
            _provider.CommitTransaction();

            var kept = _provider.Query<Person>().Where(p => p.LastName == guid1).ToList();
            Assert.AreEqual(1, kept.Count);
        }

        [TestMethod]
        public async Task DeleteAsync_WithoutWhereClause_ThrowsException()
        {
            OutputTestMethodName();
            _provider.BeginTransaction();
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
                await _provider.DeleteAsync<Person>(null));
            _provider.RollbackTransaction();
        }

        [TestMethod]
        public void Transaction_BeginRollback()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            _provider.BeginTransaction();
            _provider.Insert(new Person
            {
                FirstName = "Rollback",
                MiddleInitial = "R",
                LastName = guid,
                Gender = "Male",
                UniqueId = Guid.NewGuid(),
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow
            });
            _provider.RollbackTransaction();
            var result = _provider.Query<Person>().Where(p => p.LastName == guid).ToList();
            Assert.AreEqual(0, result.Count);
        }

        #endregion
    }

    public class MissingTable
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
