using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Address;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Person;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    [TestClass]
    public class SqlDataProviderIntegrationTests
    {
        private string? _connectionString;
        public required FunkySqlDataProvider _provider;

        private static readonly string[] TablesToClear = { "person_address", "address", "person" };

        [TestInitialize]
        public void Setup()
        {
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION");
            if (string.IsNullOrEmpty(_connectionString))
                _connectionString = "Data Source=localhost;Initial Catalog=funky_db;Integrated Security=SSPI;TrustServerCertificate=true;";
            TestConnection();

            _provider = new FunkySqlDataProvider(_connectionString)
            {
                Log = s => { Debug.WriteLine(s); }
            };

            // Clear tables before each test to ensure isolation
            // ClearTables();
        }

        [TestCleanup]
        public void Cleanup()
        {
            // ClearTables();
            _provider?.Dispose();
        }

        private void TestConnection()
        {
            using var connection = new SqlConnection(_connectionString);
            try
            {
                connection.Open();
                Console.WriteLine("Connection successful.");
            }
            catch (SqlException ex)
            {
                throw new ArgumentNullException("connectionString",
                    "Neither localhost.funky_db server/database exists, nor Environment variable FUNKY_CONNECTION; please ensure the funky_db database is created and configure the connection string to point to it.\r\n\r\n" +
                    ex.ToString());
            }
        }

        private void ClearTables()
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();
            foreach (var table in TablesToClear)
            {
                using var command = new SqlCommand($"DELETE FROM {table}", connection);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Inserts the test person and returns the id of the inserted row.
        /// </summary>
        /// <param name="firstName">The first name.</param>
        /// <param name="middleInitial">The middle initial.</param>
        /// <param name="lastName">The last name.</param>
        /// <param name="birthdate">The birthdate.</param>
        /// <param name="gender">The gender.</param>
        /// <param name="uniqueId">The unique identifier.</param>
        /// <param name="dateUtcCreated">The date UTC created.</param>
        /// <param name="dateUtcModified">The date UTC modified.</param>
        /// <returns>int.</returns>
        private int InsertTestPerson(string firstName, string middleInitial, string lastName, DateTime? birthdate, string gender, Guid uniqueId, DateTime? dateUtcCreated = null, DateTime? dateUtcModified = null)
        {
            // Truncate gender to 10 characters to fit the nvarchar(10) column
            if (gender != null && gender.Length > 10)
            {
                gender = gender.Substring(0, 10);
            }

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

        private int InsertTestAddress(string line1, string? line2, string city, string stateCode, string postalCode)
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

        private void InsertTestPersonAddress(int personId, int addressId)
        {
            var link = new PersonAddress
            {
                PersonId = personId,
                AddressId = addressId
            };
            _provider.Insert(link);
        }

        [TestMethod]
        public void Warm_Up()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());

            // Act
            var count = _provider.Query<Person>().Count(x => x.FirstName != null);

            // Assert
            Assert.IsTrue(count > 0);
        }

        [TestMethod]
        public void Get_WithExistingId_ReturnsPerson()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());

            // Act
            var person = _provider.Get<Person>(personId);

            // Assert
            Assert.IsNotNull(person);
            Assert.AreEqual(personId, person.Id);
        }

        [TestMethod]
        public void GetList_ReturnsAllPersonAddressLinks()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var addressId = InsertTestAddress("123 Main St", null, "Springfield", "IL", "62704");
            InsertTestPersonAddress(personId, addressId);

            // Act
            var links = _provider.GetList<PersonAddress>().ToList();

            // Assert
            Assert.IsTrue(links.Count > 0, "No person-address links found.");
            Assert.IsTrue(links.Any(l => l.PersonId == personId && l.AddressId == addressId));
        }

        [TestMethod]
        public void Insert_NewPerson_IncreasesCount()
        {
            // Arrange
            var initialCount = _provider.Query<Person>().Count(); // todo: Count not being intercepted correctly
            var newPerson = new Person
            {
                FirstName = Guid.NewGuid().ToString(),
                LastName = Guid.NewGuid().ToString(),
                Birthdate = DateTime.Today.Subtract(TimeSpan.FromDays(Random.Shared.Next(10, 30))),
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow,
                UniqueId = Guid.NewGuid()
            };

            // Act
            _provider.Insert(newPerson);

            // Assert
            var updatedCount = _provider.Query<Person>().Count();
            Assert.AreEqual(initialCount + 1, updatedCount, "Person was not inserted.");
        }

        [TestMethod]
        public void Update_PersonUpdates()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var person = _provider.Get<Person>(personId);
            var originalName = person!.FirstName;
            person.FirstName = $"UpdatedName{DateTime.Now.Ticks}";

            // Act
            _provider.Update(person);

            // Assert
            var updatedPerson = _provider.Get<Person>(personId);
            Assert.IsNotNull(updatedPerson);
            Assert.AreNotEqual(originalName, updatedPerson.FirstName, "Update did not change the first name.");
        }

        [TestMethod]
        public void Query_WithExpression_ReturnsFilteredAddresses()
        {
            // Arrange
            const string stateCode = "IL";
            var addressId = InsertTestAddress("123 Main St", null, "Springfield", stateCode, "62704");

            // Act
            var addresses = _provider.Query<Address>().Where(a => a.StateCode == stateCode).ToList();

            // Assert
            Assert.IsTrue(addresses.Count > 0 && addresses.All(x => x.StateCode == stateCode), "No addresses found in IL.");
        }

        [TestMethod]
        public void Query_WhenExpressionMatches_ReturnsEntities()
        {
            // Arrange
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

            _provider.Insert(newPerson);

            // Act
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid && x.LastName == guid)
                .ToList();

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(newPerson.FirstName, result[0].FirstName);
            Assert.AreEqual(newPerson.LastName, result[0].LastName);

            var result2 = _provider.Query<Person>()
                .FirstOrDefault(x => x.FirstName == guid && x.LastName == guid);
            Assert.IsNotNull(result2);
            Assert.AreEqual(guid, result2.FirstName);
            Assert.AreEqual(guid, result2.LastName);
        }

        [TestMethod]
        public void Query_WhenExpressionDoesNotMatch_ReturnsEmpty()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var newPerson = new Person
            {
                FirstName = "Unique",
                LastName = "Person",
                Birthdate = DateTime.Today.Subtract(TimeSpan.FromDays(Random.Shared.Next(10, 30))),
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow,
                UniqueId = Guid.NewGuid()
            };

            _provider.Insert(newPerson);

            // Act
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid && x.LastName == guid)
                .ToList();

            // Assert
            Assert.IsFalse(result.Any());
        }

        [TestMethod]
        public void Query_FirstOrDefaultWithPredicate_ReturnsFirstMatchingEntity()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            // Act
            var result = _provider.Query<Person>()
                .FirstOrDefault(x => x.FirstName == guid && x.LastName == guid);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(guid, result.FirstName);
            Assert.AreEqual(guid, result.LastName);
        }

        [TestMethod]
        public void Query_FirstWithPredicate_ReturnsFirstMatchingEntity()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            // Act
            var result = _provider.Query<Person>()
                .First(x => x.FirstName == guid && x.LastName == guid);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(guid, result.FirstName);
            Assert.AreEqual(guid, result.LastName);
        }

        [TestMethod]
        public void Query_FirstWithPredicate_ThrowsIfNoMatch()
        {
            // Act & Assert
            Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Query<Person>()
                    .First(x => x.FirstName == "NonExistent")
            );
        }

        [TestMethod]
        public void Query_LastOrDefaultWithPredicate_ReturnsLastMatchingEntity()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personId1 = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            var personId2 = InsertTestPerson(guid, "B", guid, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            // Act
            var result = _provider.Query<Person>()
                .LastOrDefault(x => x.FirstName == guid && x.LastName == guid);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(personId2, result.Id); // Should return the last inserted person
            Assert.AreEqual(guid, result.FirstName);
            Assert.AreEqual(guid, result.LastName);
        }

        [TestMethod]
        public void Query_SkipTake_ReturnsCorrectSubset()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            for (int i = 0; i < 10; i++)
            {
                InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            }

            // Act
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid && x.LastName == guid)
                .Skip(5)
                .Take(3)
                .ToList();

            // Assert
            Assert.AreEqual(3, result.Count);
        }

        [TestMethod]
        public void Query_Person_WithLastNameInList_ReturnsCorrectPersons()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new Person { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" },
                new Person { LastName = "Johnson", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" },
                new Person { LastName = "Doe", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" }
            };

            personsToInsert.ForEach(p => _provider.Insert(p));

            var lastNames = new List<string> { "Smith", "Johnson" };

            // Act
            var result = _provider.Query<Person>()
                .Where(p => p.FirstName == guid && p.LastName != null && lastNames.Contains(p.LastName))
                .ToList();

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.All(p => p.LastName == "Smith" || p.LastName == "Johnson"));
            Assert.IsFalse(result.Any(p => p.LastName == "Doe"));
        }

        [TestMethod]
        public void Query_Person_WithLastNameInArray_ReturnsCorrectPersons()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new Person { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" },
                new Person { LastName = "Johnson", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" },
                new Person { LastName = "Doe", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" }
            };

            personsToInsert.ForEach(p => _provider.Insert(p));

            var lastNames = new[] { "Smith", "Johnson" };

            // Act
            var result = _provider.Query<Person>()
                .Where(p => p.FirstName == guid && lastNames.Contains(p.LastName))
                .ToList();

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.All(p => p.LastName == "Smith" || p.LastName == "Johnson"));
            Assert.IsFalse(result.Any(p => p.LastName == "Doe"));
        }

        [TestMethod]
        public void Query_Person_WithEmptyList_ReturnsEmptyResult()
        {
            // Arrange
            var lastNames = new string[] { };

            // Act
            var result = _provider.Query<Person>()
                .Where(p => lastNames.Contains(p.LastName))
                .ToList();

            // Assert
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void Query_Person_LastNameStartsWith_ReturnsCorrectPersons()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new Person { LastName = "Johnson", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" },
                new Person { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" }
            };

            personsToInsert.ForEach(p => _provider.Insert(p));

            // Act
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid && x.LastName!.StartsWith("J"))
                .ToList();

            // Assert
            Assert.IsTrue(result.Any() && result.All(x => x.LastName!.StartsWith("J", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public void Query_Person_LastNameEndsWith_ReturnsCorrectPersons()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new Person { LastName = "Jones", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" },
                new Person { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" }
            };

            personsToInsert.ForEach(p => _provider.Insert(p));

            // Act
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid && x.LastName!.EndsWith("s"))
                .ToList();

            // Assert
            Assert.IsTrue(result.Any() && result.All(x => x.LastName!.EndsWith("s", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public void Query_Person_LastNameContains_ReturnsCorrectPersons()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new Person { LastName = "Johnson", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" },
                new Person { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" }
            };

            personsToInsert.ForEach(p => _provider.Insert(p));

            // Act
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid && x.LastName!.Contains("on"))
                .ToList();

            // Assert
            Assert.IsTrue(result.Any() && result.All(x => x.LastName!.Contains("on", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public void Query_Person_LastNameStartsWith_NoMatch_ReturnsEmptyList()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new Person { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Male" }
            };
            personsToInsert.ForEach(p => _provider.Insert(p));

            // Act
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid && x.LastName!.StartsWith("ToString"))
                .ToList();

            // Assert
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void Query_Person_LastNameEndsWith_NoMatch_ReturnsEmptyList()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new Person { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Female" }
            };
            personsToInsert.ForEach(p => _provider.Insert(p));

            // Act
            var value = Guid.NewGuid().ToString();
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid && x.LastName!.EndsWith(value))
                .ToList();

            // Assert
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void Query_Person_LastNameContains_NoMatch_ReturnsEmptyList()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new Person { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Male" }
            };
            personsToInsert.ForEach(p => _provider.Insert(p));

            // Act
            var value = Guid.NewGuid().ToString();
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid && x.LastName!.Contains(value))
                .ToList();

            // Assert
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void Query_AnyWithPredicate_ReturnsTrueIfMatches()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var idOrCount = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            Console.WriteLine(idOrCount);


            // Act
            var result = _provider.Query<Person>().Any(x => x.FirstName == guid);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void Query_AllWithPredicate_ReturnsTrueIfAllMatch()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var insert1 = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            Console.WriteLine(insert1);
            var insert2 = InsertTestPerson(guid, "B", guid, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            Console.WriteLine(insert2);

            // Act
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .All(x => x.FirstName == guid);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void Query_CountWithPredicate_ReturnsCorrectCount()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "B", guid, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            // Act
            var count = _provider.Query<Person>().Count(x => x.FirstName == guid);

            // Assert
            Assert.AreEqual(2, count);
        }

        [TestMethod]
        public void Query_AverageWithSelector_ReturnsCorrectAverage()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var person1 = InsertTestPerson(guid, "A", guid, new DateTime(1990, 1, 1), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            var person2 = InsertTestPerson(guid, "B", guid, new DateTime(2000, 1, 1), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            var avg = (person1 + person2) / 2.0; // Use integer division to match SQL AVG's return type for ints

            // Act
            var avgId = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .Average(x => x.Id);

            // Assert
            Assert.AreEqual(avg, avgId);
        }

        [TestMethod]
        public void Query_MaxWithSelector_ReturnsMaximum()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var person1 = InsertTestPerson(guid, "A", guid, new DateTime(1990, 1, 1), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            var person2 = InsertTestPerson(guid, "B", guid, new DateTime(2000, 1, 1), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            // Act
            var maxId = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .Max(x => x.Id);

            // Assert
            Assert.AreEqual(person2, maxId); // person2 should have the higher Id (2)
        }

        [TestMethod]
        public void Query_MinWithSelector_ReturnsMinimum()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var person1 = InsertTestPerson(guid, "A", guid, new DateTime(1990, 1, 1), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            var person2 = InsertTestPerson(guid, "B", guid, new DateTime(2000, 1, 1), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            // Act
            var minId = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .Min(x => x.Id);

            // Assert
            Assert.AreEqual(person1, minId); // person1 should have the lower Id (1)
        }

        [TestMethod]
        public void Transaction_BeginCommit()
        {
            // Begin a transaction
            _provider.BeginTransaction();

            // Insert a temporary person within the transaction
            var person = new Person
            {
                FirstName = Guid.NewGuid().ToString(),
                LastName = Guid.NewGuid().ToString(),
                Birthdate = null,
                UniqueId = Guid.NewGuid()
            };
            _provider.Insert(person);

            // Commit the transaction
            _provider.CommitTransaction();

            // Check if the person was committed to the database
            var committedPerson = _provider.Get<Person>(person.Id);
            Assert.IsNotNull(committedPerson);
        }

        [TestMethod]
        public void Transaction_BeginRollback()
        {
            // Begin a transaction
            _provider.BeginTransaction();

            // Insert a temporary person within the transaction
            var person = new Person
            {
                FirstName = Guid.NewGuid().ToString(),
                LastName = Guid.NewGuid().ToString(),
                Birthdate = null,
                UniqueId = Guid.NewGuid()
            };
            _provider.Insert(person);

            // Check if the person was added in the transaction
            var addedPerson = _provider.Get<Person>(person.Id);
            Assert.IsNotNull(addedPerson);

            // Rollback the transaction
            _provider.RollbackTransaction();

            // After rollback, the person should not exist
            var rolledBackPerson = _provider.Get<Person>(person.Id);
            Assert.IsNull(rolledBackPerson);
        }

        [TestMethod]
        public void Transaction_MultipleOperations()
        {
            // Begin a transaction
            _provider.BeginTransaction();

            // Perform multiple operations
            var person = new Person
            {
                FirstName = Guid.NewGuid().ToString(),
                LastName = Guid.NewGuid().ToString(),
                Birthdate = null,
                UniqueId = Guid.NewGuid()
            };
            _provider.Insert(person);

            var address = new Address
            {
                Line1 = "Test St",
                City = "TestCity",
                StateCode = "TC",
                PostalCode = "12345"
            };
            _provider.Insert(address);

            // Link person to address
            var link = new PersonAddress { PersonId = person.Id, AddressId = address.Id };
            _provider.Insert(link);

            // Verify each operation in the transaction
            var insertedPerson = _provider.Get<Person>(person.Id);
            var insertedAddress = _provider.Get<Address>(address.Id);
            var insertedLink = _provider.Get<PersonAddress>(link.Id);

            Assert.IsNotNull(insertedPerson);
            Assert.IsNotNull(insertedAddress);
            Assert.IsNotNull(insertedLink);

            // Commit the transaction
            _provider.CommitTransaction();

            // Verify if all operations are committed
            var committedPerson = _provider.Get<Person>(person.Id);
            var committedAddress = _provider.Get<Address>(address.Id);
            var committedLink = _provider.Get<PersonAddress>(link.Id);

            Assert.IsNotNull(committedPerson);
            Assert.IsNotNull(committedAddress);
            Assert.IsNotNull(committedLink);
        }

        #region DateTime Tests

        [TestMethod]
        public void Query_Person_WithBirthdateInRange_ReturnsCorrectPersons()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new Person { LastName = "Smith", FirstName = guid, Birthdate = DateTime.Today.AddYears(-30), Gender = "Guid", UniqueId = Guid.NewGuid() },
                new Person { LastName = "Johnson", FirstName = guid, Birthdate = DateTime.Today.AddYears(-25), Gender = "Guid", UniqueId = Guid.NewGuid() },
                new Person { LastName = "Doe", FirstName = guid, Birthdate = DateTime.Today.AddYears(-40), Gender = "Guid", UniqueId = Guid.NewGuid() }
            };
            personsToInsert.ForEach(p => _provider.Insert(p));

            var fromDate = DateTime.Today.AddYears(-35);
            var toDate = DateTime.Today.AddYears(-20);

            // Act
            var result = _provider.Query<Person>()
                .Where(p => p.FirstName == guid && p.Birthdate >= fromDate && p.Birthdate <= toDate)
                .ToList();

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.All(x => x.LastName == "Smith" || x.LastName == "Johnson"));
        }

        [TestMethod]
        public void Query_Person_WithOrElse_Birthdates_ReturnsCorrectPersons()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var fromDate = DateTime.Today.AddYears(-100);
            var toDate = DateTime.Today.AddYears(100);

            var personsToInsert = new List<Person>
            {
                new Person { LastName = "Smith", FirstName = guid, Birthdate = DateTime.Today.AddYears(-101), Gender = "Female", UniqueId = Guid.NewGuid() },
                new Person { LastName = "Johnson", FirstName = guid, Birthdate = DateTime.Today.AddYears(101), Gender = "Male", UniqueId = Guid.NewGuid() },
            };
            personsToInsert.ForEach(p => _provider.Insert(p));

            // Act
            var result = _provider.Query<Person>()
                .Where(p => p.FirstName == guid && (p.Birthdate <= fromDate || p.Birthdate >= toDate))
                .ToList();

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.All(x => x.LastName == "Smith" || x.LastName == "Johnson"));
        }

        [TestMethod]
        public void Query_Person_WithNullBirthdate_HandlesNullCorrectly()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new Person { LastName = "NullDate", FirstName = guid, Birthdate = null, Gender = "Male", UniqueId = Guid.NewGuid() },
                new Person { LastName = "HasDate", FirstName = guid, Birthdate = DateTime.Today.AddYears(-30), Gender = "Female", UniqueId = Guid.NewGuid() }
            };
            personsToInsert.ForEach(p => _provider.Insert(p));

            // Act
            var nullBirthdate = _provider.Query<Person>()
                .Where(p => p.FirstName == guid && p.Birthdate == null)
                .ToList();

            var hasBirthdate = _provider.Query<Person>()
                .Where(p => p.FirstName == guid && p.Birthdate != null)
                .ToList();

            // Assert
            Assert.AreEqual(1, nullBirthdate.Count);
            Assert.AreEqual("NullDate", nullBirthdate[0].LastName);
            Assert.AreEqual(1, hasBirthdate.Count);
            Assert.AreEqual("HasDate", hasBirthdate[0].LastName);
        }

        #endregion

        #region GuidTests

        [TestMethod]
        public void Query_Person_WithSpecificGuid_ReturnsCorrectPerson()
        {
            // Arrange
            var uniqueGuid = Guid.NewGuid();
            var person = new Person
            {
                LastName = Guid.NewGuid().ToString(),
                FirstName = Guid.NewGuid().ToString(),
                UniqueId = uniqueGuid
            };
            _provider.Insert(person);

            // Act
            var result = _provider.Query<Person>()
                .Where(p => p.UniqueId == uniqueGuid)
                .ToList();

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(uniqueGuid, result[0].UniqueId);
        }

        [TestMethod]
        public void Query_Person_GuidInList_ReturnsCorrectPersons()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var guids = new List<Guid?> { Guid.NewGuid(), Guid.NewGuid() };
            var personsToInsert = new List<Person>
            {
                new Person { LastName = "GuidOne", FirstName = guid, UniqueId = guids[0], Gender = "Guid" },
                new Person { LastName = "GuidTwo", FirstName = guid, UniqueId = guids[1], Gender = "Guid" },
                new Person { LastName = "NoMatch", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" }
            };
            personsToInsert.ForEach(p => _provider.Insert(p));

            // Act
            var result = _provider.Query<Person>()
                .Where(p => p.FirstName == guid && guids.Contains(p.UniqueId))
                .ToList();

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.All(p => guids.Contains(p.UniqueId)));
        }

        #endregion
    }
}