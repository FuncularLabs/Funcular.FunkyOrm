
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Address;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Person;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    [TestClass]
    public class SqlDataProviderIntegrationTests
    {
        protected string _connectionString;
        public required SqlServerOrmDataProvider _provider;
        protected readonly StringBuilder _sb = new();

        public void OutputTestMethodName([CallerMemberName] string callerMemberName = "")
        {
            Debug.WriteLine($"\r\nTest: {callerMemberName}");
        }

        [TestInitialize]
        public void Setup()
        {
            _sb.Clear();
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
                "Data Source=localhost;Initial Catalog=funky_db;Integrated Security=SSPI;TrustServerCertificate=true;";
            TestConnection();

            _provider = new SqlServerOrmDataProvider(_connectionString)
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
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var count = _provider.Query<Person>().Count(x => x.FirstName != null);
            Assert.IsTrue(count > 0);
        }

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
        public void Query_OrderBy_ReturnsOrderedResults()
        {
            OutputTestMethodName();
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
            OutputTestMethodName();
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
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "D", "Adams", DateTime.Now.AddYears(-40), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "C", "AdAMS", DateTime.Now.AddYears(-35), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "B", "Jones", DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "A", "Jones", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            // Act
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .OrderBy(x => x.LastName)
                .ThenBy(x => x.MiddleInitial)
                .ToList();

            // Assert
            Assert.AreEqual(4, result.Count);
            Assert.AreEqual("C", result[0].MiddleInitial);
            Assert.AreEqual("D", result[1].MiddleInitial);
            Assert.AreEqual("A", result[2].MiddleInitial);
            Assert.AreEqual("B", result[3].MiddleInitial);
            Assert.IsTrue(_sb.ToString().Contains("ORDER BY", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Tests that the ThenByDescending method returns correctly ordered results after an OrderBy.
        /// </summary>
        /// <exception cref="AssertFailedException">Thrown if the assertion fails.</exception>
        [TestMethod]
        public void Query_ThenByDescending_ReturnsOrderedResults()
        {
            // Arrange
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "D", "Adams", DateTime.Now.AddYears(-40), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "C", "AdAMS", DateTime.Now.AddYears(-35), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "B", "Jones", DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "A", "Jones", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            // Act
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .OrderBy(x => x.LastName)
                .ThenByDescending(x => x.MiddleInitial)
                .ToList();

            // Assert
            Assert.AreEqual(4, result.Count);
            Assert.AreEqual("D", result[0].MiddleInitial);
            Assert.AreEqual("C", result[1].MiddleInitial);
            Assert.AreEqual("B", result[2].MiddleInitial);
            Assert.AreEqual("A", result[3].MiddleInitial);
            Assert.IsTrue(_sb.ToString().Contains("ORDER BY", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(_sb.ToString().Contains("DESC", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void Query_OrderByWithSkipTake_ReturnsCorrectSubset()
        {
            OutputTestMethodName();
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
            OutputTestMethodName();
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
            OutputTestMethodName();
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
            OutputTestMethodName();
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
            OutputTestMethodName();
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
            OutputTestMethodName();
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
            OutputTestMethodName();
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new() { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" },
                new() { LastName = "Johnson", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" },
                new() { LastName = "Doe", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" }
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
            OutputTestMethodName();
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new() { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" },
                new() { LastName = "Johnson", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" },
                new() { LastName = "Doe", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" }
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
            OutputTestMethodName();
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
            OutputTestMethodName();
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new() { LastName = "Johnson", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" },
                new() { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" }
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
            OutputTestMethodName();
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new() { LastName = "Jones", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" },
                new() { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" }
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
            OutputTestMethodName();
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new() { LastName = "Johnson", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" },
                new() { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Guid" }
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
            OutputTestMethodName();
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new() { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Male" }
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
            OutputTestMethodName();
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new() { LastName = "Smith", FirstName = guid, UniqueId = Guid.NewGuid(), Gender = "Female" }
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
            OutputTestMethodName();
            // Arrange
            var firstGuid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new() { LastName = "Smith", FirstName = firstGuid, UniqueId = Guid.NewGuid(), Gender = "Male" }
            };
            personsToInsert.ForEach(p => _provider.Insert(p));

            // Act
            var secondGuid = Guid.NewGuid().ToString();
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == firstGuid && x.LastName!.Contains(secondGuid))
                .ToList();

            // Assert
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void Query_AnyWithPredicate_ReturnsTrueIfMatches()
        {
            OutputTestMethodName();
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var idOrCount = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            Debug.WriteLine(idOrCount);


            // Act
            var result = _provider.Query<Person>().Any(x => x.FirstName == guid);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void Query_AllWithPredicate_ReturnsTrueIfAllMatch()
        {
            // Arrange
            var firstNameGuid = Guid.NewGuid().ToString();
            var lastNameGuid = Guid.NewGuid().ToString();
            var initialRecords = _provider.Query<Person>().Where(x => x.FirstName == firstNameGuid).ToList();
            // This should be zero:
            Debug.WriteLine($"Records with FirstName={firstNameGuid} before insertion: {initialRecords.Count}");
            foreach (var record in initialRecords)
            {
                Debug.WriteLine($"Initial record: FirstName={record.FirstName}, LastName={record.LastName}");
            }

            var insert1 = InsertTestPerson(firstNameGuid, "A", lastNameGuid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            Debug.WriteLine($"Inserted person 1 with ID: {insert1}, FirstName: {firstNameGuid}, LastName: {lastNameGuid}");
            var insert2 = InsertTestPerson(firstNameGuid, "B", lastNameGuid, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            Debug.WriteLine($"Inserted person 2 with ID: {insert2}, FirstName: {firstNameGuid}, LastName: {lastNameGuid}");

            // Act
            var query = _provider.Query<Person>()
                .Where(x => x.FirstName == firstNameGuid)
                .All(x => x.LastName == lastNameGuid);

            var peeps = _provider.Query<Person>()
                .Where(x => x.FirstName == firstNameGuid).ToArray();
            Debug.WriteLine($"Records after Where clause: {peeps.Length}");

            var result = query;
            Debug.WriteLine($"Query result: {result}");
            Assert.IsTrue(result, $"All records with FirstName={firstNameGuid} have LastName={lastNameGuid}.");
        }


        /// <summary>
        /// Tests that the 'All' method returns false when not all records matching the FirstName filter have a LastName matching the predicate.
        /// </summary>
        /// <exception cref="AssertFailedException">Thrown if the assertion fails.</exception>
        [TestMethod]
        public void Query_AllWithPredicate_ReturnsFalseIfNotAllMatch()
        {
            // Arrange
            var firstNameGuid = Guid.NewGuid().ToString();
            var lastNameGuid = Guid.NewGuid().ToString();
            // Debug: Check the database state before insertion
            var initialRecords = _provider.Query<Person>().Where(x => x.FirstName == firstNameGuid).ToList();
            Debug.WriteLine($"Records with FirstName={firstNameGuid} before insertion: {initialRecords.Count}");
            foreach (var record in initialRecords)
            {
                Debug.WriteLine($"Initial record: FirstName={record.FirstName}, LastName={record.LastName}");
            }

            // Insert two records with the same FirstName but different LastNames
            var insert1 = InsertTestPerson(firstNameGuid, "A", lastNameGuid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            Debug.WriteLine($"Inserted person 1 with ID: {insert1}, FirstName: {firstNameGuid}, LastName: {lastNameGuid}");
            var insert2 = InsertTestPerson(firstNameGuid, "B", "DifferentLastName", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            Debug.WriteLine($"Inserted person 2 with ID: {insert2}, FirstName: {firstNameGuid}, LastName: DifferentLastName");

            // Act
            var query = _provider.Query<Person>()
                .Where(x => x.FirstName == firstNameGuid)
                .All(x => x.LastName == lastNameGuid);

            var result = query;

            // Debug: Log the generated query
            var sql = query.ToString();
            Debug.WriteLine($"Generated query: {sql}");

            // Assert
            Assert.IsFalse(result, $"Not all records with FirstName={firstNameGuid} have LastName={lastNameGuid}.");
        }

        [TestMethod]
        public void Query_CountWithPredicate_ReturnsCorrectCount()
        {
            OutputTestMethodName();
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
            OutputTestMethodName();
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
        public void Query_MaxIdWithSelector_ReturnsMaximum()
        {
            OutputTestMethodName();
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var person1Id = InsertTestPerson(guid, "A", guid, new DateTime(1990, 1, 1), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            var person2Id = InsertTestPerson(guid, "B", guid, new DateTime(2000, 1, 1), "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            // Act
            var maxId = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .Max(x => x.Id);

            // Assert
            Assert.AreEqual(person2Id, maxId); // person2 should have the higher Id (2)
            Assert.IsTrue(person2Id > person1Id);
        }

        [TestMethod]
        public void Query_MaxDateWithSelector_ReturnsMaximum()
        {
            OutputTestMethodName();

            // Arrange

            var previousMaxBirthdate = _provider.Query<Person>()
                .Max(p => p.Birthdate);
            
            var newMaxBirthdate = new DateTime(previousMaxBirthdate.Value.Year + 1, 1, 1);

            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "B", guid, newMaxBirthdate, "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            // Act
            var testMaxBirthdate = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .Max(x => x.Birthdate);

            // Assert
            Assert.AreEqual(newMaxBirthdate, testMaxBirthdate); // person2 should have the higher Id (2)
        }

        /// <summary>
        /// Tests that the Min method with a selector returns the minimum value.
        /// </summary>
        /// <exception cref="AssertFailedException">Thrown if the assertion fails.</exception>
        [TestMethod]
        public void Query_MinWithSelector_ReturnsMinimum()
        {
            OutputTestMethodName();
            // Arrange
            var guid = Guid.NewGuid().ToString();
            // Debug: Check the database state before insertion
            var initialRecords = _provider.Query<Person>().Where(x => x.FirstName == guid).ToList();
            Debug.WriteLine($"Records with FirstName={guid} before insertion: {initialRecords.Count}");
            foreach (var record in initialRecords)
            {
                Debug.WriteLine($"Initial record: FirstName={record.FirstName}, Birthdate={record.Birthdate}");
            }

            var birthdate1 = new DateTime(1990, 1, 1);
            var birthdate2 = new DateTime(2000, 1, 1);
            InsertTestPerson(guid, "A", guid, birthdate1, "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "B", guid, birthdate2, "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "C", guid, birthdate2, "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
            InsertTestPerson(guid, "D", guid, birthdate2, "Female", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

            // Debug: Verify the inserted data
            var insertedRecords = _provider.Query<Person>().Where(x => x.FirstName == guid).ToList();
            Debug.WriteLine($"Inserted records: {insertedRecords.Count}");
            foreach (var record in insertedRecords)
            {
                Debug.WriteLine($"Inserted record: FirstName={record.FirstName}, Birthdate={record.Birthdate}");
            }

            // Act
            var query = _provider.Query<Person>()
                .Where(x => x.FirstName == guid);
            var minBirthdate = 
                query.Min(x => x.Birthdate);

            // Debug: Log the query and results
            var allRecords = query.ToList();
            Debug.WriteLine($"Records found: {allRecords.Count}");
            foreach (var record in allRecords)
            {
                Debug.WriteLine($"Filtered record: FirstName={record.FirstName}, Birthdate={record.Birthdate}, LastName={record.LastName}");
            }
            Debug.WriteLine($"Min Birthdate: {minBirthdate}");

            // Assert
            Assert.AreEqual(birthdate1, minBirthdate, "Expected minimum Birthdate to be 1990-01-01.");
        }

        [TestMethod]
        public void Transaction_BeginCommit()
        {
            OutputTestMethodName();
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
            OutputTestMethodName();
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
            OutputTestMethodName();
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
        public void Delete_WithoutTransaction_ThrowsException()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());

            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Delete<Person>(x => x.Id == personId)
            );
            StringAssert.Contains(ex.Message, "transaction");
            // Cleanup
            _provider.BeginTransaction();
            _provider.Delete<Person>(x => x.Id == personId);
            _provider.CommitTransaction();
        }

        [TestMethod]
        public void Delete_WithoutWhereClause_ThrowsException()
        {
            OutputTestMethodName();
            _provider.BeginTransaction();
            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Delete<Person>(null)
            );
            StringAssert.Contains(ex.Message, "WHERE clause");
            _provider.RollbackTransaction();
        }

        [TestMethod]
        public void Delete_WithEmptyWhereClause_ThrowsException()
        {
            OutputTestMethodName();
            _provider.BeginTransaction();
            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Delete<Person>(x => true)
            );
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
            var person1 = _provider.Get<Person>(personId1);
            var person2 = _provider.Get<Person>(personId2);
            Assert.IsNull(person1);
            Assert.IsNotNull(person2);
        }

        [TestMethod]
        public void Delete_TrivialWhereClause_ThrowsException()
        {
            OutputTestMethodName();
            _provider.BeginTransaction();
            // Trivial: x => true
            var ex1 = Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Delete<Person>(x => true));
            StringAssert.Contains(ex1.Message, "Delete operation WHERE clause must reference at least one column");

            // Trivial: 1 < 2
            var ex2 = Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Delete<Person>(x => 1 < 2));
            StringAssert.Contains(ex2.Message, "Delete operation WHERE clause must reference at least one column");

            // Trivial: self-referencing column
            var ex3 = Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Delete<Person>(x => x.FirstName == x.FirstName));
            StringAssert.Contains(ex3.Message, "self-referencing column expression");

            _provider.RollbackTransaction();
        }

        [TestMethod]
        public void Delete_WhereClauseWithoutTableColumn_ThrowsException()
        {
            OutputTestMethodName();
            _provider.BeginTransaction();
            // WHERE clause does not reference any table column
            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                _provider.Delete<Person>(x => "abc" == "abc"));
            StringAssert.Contains(ex.Message, "must reference at least one column");
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

            Assert.IsTrue(result, "Delete should return true for existing entity.");
            var deletedPerson = _provider.Get<Person>(personId);
            Assert.IsNull(deletedPerson, "Deleted entity should not be found.");
        }

        [TestMethod]
        public void Delete_ById_ReturnsFalseForNonExistentEntity()
        {
            OutputTestMethodName();
            var nonExistentId = -999999;

            _provider.BeginTransaction();
            var result = _provider.Delete<Person>(nonExistentId);
            _provider.CommitTransaction();

            Assert.IsFalse(result, "Delete should return false for non-existent entity.");
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

        #region DateTime Tests

        [TestMethod]
        public void Query_Person_WithBirthdateInRange_ReturnsCorrectPersons()
        {
            OutputTestMethodName();
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new() { LastName = "Smith", FirstName = guid, Birthdate = DateTime.Today.AddYears(-30), Gender = "Guid", UniqueId = Guid.NewGuid() },
                new() { LastName = "Johnson", FirstName = guid, Birthdate = DateTime.Today.AddYears(-25), Gender = "Guid", UniqueId = Guid.NewGuid() },
                new() { LastName = "Doe", FirstName = guid, Birthdate = DateTime.Today.AddYears(-40), Gender = "Guid", UniqueId = Guid.NewGuid() }
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
            OutputTestMethodName();
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var fromDate = DateTime.Today.AddYears(-100);
            var toDate = DateTime.Today.AddYears(100);

            var personsToInsert = new List<Person>
            {
                new() { LastName = "Smith", FirstName = guid, Birthdate = DateTime.Today.AddYears(-101), Gender = "Female", UniqueId = Guid.NewGuid() },
                new() { LastName = "Johnson", FirstName = guid, Birthdate = DateTime.Today.AddYears(101), Gender = "Male", UniqueId = Guid.NewGuid() },
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
            OutputTestMethodName();
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var personsToInsert = new List<Person>
            {
                new() { LastName = "NullDate", FirstName = guid, Birthdate = null, Gender = "Male", UniqueId = Guid.NewGuid() },
                new() { LastName = "HasDate", FirstName = guid, Birthdate = DateTime.Today.AddYears(-30), Gender = "Female", UniqueId = Guid.NewGuid() }
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
            OutputTestMethodName();
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
        public void Query_GuidList_ReturnsCorrectPersons()
        {
            // Arrange
            var firstNameGuidString = Guid.NewGuid().ToString();
            // Debug: Check the database state before insertion
            var initialRecords = _provider.Query<Person>().Where(p => p.FirstName == firstNameGuidString).ToList();
            Debug.WriteLine($"Records with FirstName={firstNameGuidString} before insertion: {initialRecords.Count}");
            foreach (var record in initialRecords)
            {
                Debug.WriteLine($"Initial record: FirstName={record.FirstName}, UniqueId={record.UniqueId}");
            }

            var guids = new List<Guid?> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            var personsToInsert = new List<Person>
            {
                new() { LastName = "GuidOne", FirstName = firstNameGuidString, UniqueId = guids[0], Gender = "Guid1" },
                new() { LastName = "GuidTwo", FirstName = firstNameGuidString, UniqueId = guids[1], Gender = "Guid2" },
                new() { LastName = "GuidThree", FirstName = firstNameGuidString, UniqueId = guids[2], Gender = "Guid3" },
                new() { LastName = "NoMatch", FirstName = firstNameGuidString, UniqueId = Guid.NewGuid(), Gender = "NoMatch" }
            };
            personsToInsert.ForEach(p => _provider.Insert(p));

            // Debug: Verify the inserted data
            var insertedRecords = _provider.Query<Person>().Where(p => p.FirstName == firstNameGuidString).ToList();
            Debug.WriteLine($"Inserted records: {insertedRecords.Count}");
            foreach (var record in insertedRecords)
            {
                Debug.WriteLine($"Inserted record: FirstName={record.FirstName}, UniqueId={record.UniqueId}");
            }

            Debug.WriteLine($"GUIDs to match: {string.Join(", ", guids)}");

            // Act
            var queryable = _provider.Query<Person>()
                .Where(p => p.FirstName == firstNameGuidString && guids.Contains(p.UniqueId));

            // Debug: Log the generated SQL and parameters
            var sql = queryable.Expression.ToString();
            Debug.WriteLine($"Generated query: {sql}");

            // Assert
            Assert.IsTrue(queryable.All(p => guids.Contains(p.UniqueId)));
            Assert.AreEqual(3, queryable.ToList().Count);
        }

        #endregion
    }
}