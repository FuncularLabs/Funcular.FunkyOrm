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

        [TestInitialize]
        public void Setup()
        {
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
                "Data Source=localhost;Initial Catalog=funky_db;Integrated Security=SSPI;TrustServerCertificate=true;";
            TestConnection();

            _provider = new FunkySqlDataProvider(_connectionString)
            {
                Log = s => { Debug.WriteLine(s); }
            };
        }

        [TestCleanup]
        public void Cleanup()
        {
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
                    "Connection failed. Ensure funky_db exists and configure FUNKY_CONNECTION environment variable if not using localhost.\r\n\r\n" + ex);
            }
        }

        private int InsertTestPerson(string firstName, string middleInitial, string lastName, DateTime? birthdate, string gender, Guid uniqueId, DateTime? dateUtcCreated = null, DateTime? dateUtcModified = null)
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

        private int InsertTestAddress(string line1, string line2, string city, string stateCode, string postalCode)
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
            var link = new PersonAddress { PersonId = personId, AddressId = addressId };
            _provider.Insert(link);
        }

        [TestMethod]
        public void Warm_Up()
        {
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var count = _provider.Query<Person>().Count(x => x.FirstName != null);
            Assert.IsTrue(count > 0);
        }

        [TestMethod]
        public void Get_WithExistingId_ReturnsPerson()
        {
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var person = _provider.Get<Person>(personId);
            Assert.IsNotNull(person);
            Assert.AreEqual(personId, person.Id);
        }

        [TestMethod]
        public void GetList_ReturnsAllPersonAddressLinks()
        {
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var addressId = InsertTestAddress("123 Main St", null, "Springfield", "IL", "62704");
            InsertTestPersonAddress(personId, addressId);
            var links = _provider.GetList<PersonAddress>().ToList();
            Assert.IsTrue(links.Any(l => l.PersonId == personId && l.AddressId == addressId));
        }

        [TestMethod]
        public void Insert_NewPerson_IncreasesCount()
        {
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
        public void Update_PersonUpdates()
        {
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var person = _provider.Get<Person>(personId)!;
            var originalName = person.FirstName;
            person.FirstName = $"Updated{guid}";
            _provider.Update(person);
            var updatedPerson = _provider.Get<Person>(personId);
            Assert.IsNotNull(updatedPerson);
            Assert.AreNotEqual(originalName, updatedPerson.FirstName);
        }

        [TestMethod]
        public void Query_WithExpression_ReturnsFilteredAddresses()
        {
            const string stateCode = "IL";
            var addressId = InsertTestAddress($"123 Main St {Guid.NewGuid()}", null, "Springfield", stateCode, "62704");
            var addresses = _provider.Query<Address>().Where(a => a.StateCode == stateCode).ToList();
            Assert.IsTrue(addresses.Any(x => x.Id == addressId && x.StateCode == stateCode));
        }

        [TestMethod]
        public void Query_WhenExpressionMatches_ReturnsEntities()
        {
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var result = _provider.Query<Person>().Where(x => x.FirstName == guid && x.LastName == guid).ToList();
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(guid, result[0].FirstName);
        }

        [TestMethod]
        public void Query_FirstOrDefaultWithPredicate_ReturnsFirstMatchingEntity()
        {
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var result = _provider.Query<Person>().FirstOrDefault(x => x.FirstName == guid);
            Assert.IsNotNull(result);
            Assert.AreEqual(guid, result.FirstName);
        }

        [TestMethod]
        public void Query_OrderBy_ReturnsOrderedResults()
        {
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", "Zimmerman", DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            InsertTestPerson(guid, "B", "Adams", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .OrderBy(x => x.LastName)
                .ToList();
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("Adams", result[0].LastName);
            Assert.AreEqual("Zimmerman", result[1].LastName);
        }

        [TestMethod]
        public void Query_OrderByDescending_ReturnsOrderedResults()
        {
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", "Zimmerman", DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            InsertTestPerson(guid, "B", "Adams", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .OrderByDescending(x => x.LastName)
                .ToList();
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("Zimmerman", result[0].LastName);
            Assert.AreEqual("Adams", result[1].LastName);
        }

        /// <summary>
        /// Tests that the ThenBy method returns correctly ordered results after an OrderBy.
        /// </summary>
        /// <exception cref="AssertFailedException">Thrown if the assertion fails.</exception>
        [TestMethod]
        public void Query_ThenBy_ReturnsOrderedResults()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "B", "Smith", DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "A", "Smith", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            // Act
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .OrderBy(x => x.LastName)
                .ThenBy(x => x.MiddleInitial)
                .ToList();

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("A", result[0].MiddleInitial);
            Assert.AreEqual("B", result[1].MiddleInitial);
        }

        /// <summary>
        /// Tests that the ThenByDescending method returns correctly ordered results after an OrderBy.
        /// </summary>
        /// <exception cref="AssertFailedException">Thrown if the assertion fails.</exception>
        [TestMethod]
        public void Query_ThenByDescending_ReturnsOrderedResults()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "B", "Smith", DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "A", "Smith", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            // Act
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .OrderBy(x => x.LastName)
                .ThenByDescending(x => x.MiddleInitial)
                .ToList();

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("B", result[0].MiddleInitial);
            Assert.AreEqual("A", result[1].MiddleInitial);
        }

        [TestMethod]
        public void Query_OrderByWithSkipTake_ReturnsCorrectSubset()
        {
            var guid = Guid.NewGuid().ToString();
            var names = new[] { "Adams", "Baker", "Carter", "Davis" };
            foreach (var name in names)
            {
                InsertTestPerson(guid, "A", name, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            }
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .OrderBy(x => x.LastName)
                .Skip(1)
                .Take(2)
                .ToList();
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("Baker", result[0].LastName);
            Assert.AreEqual("Carter", result[1].LastName);
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
            var newGuid = Guid.NewGuid().ToString();
            // Act & Assert
            Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Query<Person>()
                    .First(x => x.FirstName == newGuid)
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

        /// <summary>
        /// Tests that the All method returns true when all records match the predicate.
        /// </summary>
        /// <exception cref="AssertFailedException">Thrown if the assertion fails.</exception>
        [TestMethod]
        public void Query_AllWithPredicate_ReturnsTrueIfAllMatch()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var insert1 = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            Console.WriteLine($"Inserted person 1 with ID: {insert1}");
            var insert2 = InsertTestPerson(guid, "B", guid, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            Console.WriteLine($"Inserted person 2 with ID: {insert2}");

            // Act
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .All(x => x.FirstName == guid);

            // Assert
            Assert.IsTrue(result, "All records should match the predicate x.FirstName == guid.");
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
            InsertTestPerson(guid, "A", guid, new DateTime(1990, 1, 1), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "B", guid, new DateTime(2000, 1, 1), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            // Act
            Assert.ThrowsException<NotSupportedException>(() =>
                    _provider.Query<Person>()
                        .Where(x => x.FirstName == guid)
                        .Average(x => x.Birthdate!.Value.Year),
                "Aggregate function Average does not support expression evaluation; aggregates are only supported on column selectors.");
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
            InsertTestPerson(guid, "A", guid, new DateTime(1990, 1, 1), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "B", guid, new DateTime(2000, 1, 1), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            // Act
            var minBirthdate = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .Min(x => x.Birthdate);

            // Assert
            Assert.AreEqual(new DateTime(1990, 1, 1), minBirthdate);
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

        /// <summary>
        /// Tests that querying with a list of GUIDs returns the correct persons.
        /// </summary>
        /// <exception cref="AssertFailedException">Thrown if the assertion fails.</exception>
        [TestMethod]
        public void Query_Person_GuidInList_ReturnsCorrectPersons()
        {
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var guids = new List<Guid?> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            var personsToInsert = new List<Person>
            {
                new Person { LastName = "GuidOne", FirstName = guid, UniqueId = guids[0], Gender = "Guid1" },
                new Person { LastName = "GuidTwo", FirstName = guid, UniqueId = guids[1], Gender = "Guid2" },
                new Person { LastName = "GuidThree", FirstName = guid, UniqueId = guids[2], Gender = "Guid3" },
                new Person { LastName = "NoMatch", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "NoMatch" }
            };
            personsToInsert.ForEach(p => _provider.Insert(p));

            // Act
            var result = _provider.Query<Person>()
                .Where(p => p.FirstName == guid && guids.Contains(p.UniqueId))
                .ToList();

            // Assert
            Assert.AreEqual(3, result.Count);
            Assert.IsTrue(result.All(p => guids.Contains(p.UniqueId)));
        }

        #endregion
    }
}