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
            /*_connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
                "Data Source=localhost;Initial Catalog=funky_db;Integrated Security=SSPI;TrustServerCertificate=true;";*/
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
                                "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=funky_db;Integrated Security=True;";
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
            InsertTestPerson(guid, "B", "AdAMS", DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .OrderBy(x => x.LastName)
                .ToList();
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
            var result = _provider.Query<Person>()
                .Where(x => x.FirstName == guid)
                .OrderByDescending(x => x.LastName)
                .ToList();
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("Zimmerman", result[0].LastName);
            Assert.AreEqual("AdAMS", result[1].LastName);
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
            InsertTestPerson(guid, "D", "AdAMS", DateTime.Now.AddYears(-40), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
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
            InsertTestPerson(guid, "D", "AdAMS", DateTime.Now.AddYears(-40), "Male", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);
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
        public void Query_SelectWithIsTwentyOneOrOverProjection_GeneratesCaseStatement()
        {
            OutputTestMethodName();

            // Precalculate the cutoff date for age 21 (as of today)
            DateTime cutoff = DateTime.Now.AddYears(-21).Date;

            // Insert test persons with varying birthdates
            var guid = Guid.NewGuid().ToString();
            var personUnder21Id = InsertTestPerson("Under21", "U", guid, DateTime.Now.AddYears(-20).Date, "Male", Guid.NewGuid()); // Under 21
            var personOver21Id = InsertTestPerson("Over21", "O", guid, DateTime.Now.AddYears(-25).Date, "Female", Guid.NewGuid()); // Over 21
            var personExactly21Id = InsertTestPerson("Exactly21", "E", guid, cutoff, "NonBinary", Guid.NewGuid()); // Exactly 21 (edge case)
            var personNullBirthdateId = InsertTestPerson("NullDOB", "N", guid, null, "Male", Guid.NewGuid()); // Null birthdate

            // Query with projection using the ternary expression
            var results = _provider.Query<Person>()
                .Where(p => p.LastName == guid) // Filter to our test data
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
                    IsTwentyOneOrOver = p.Birthdate.HasValue && p.Birthdate.Value <= cutoff ? true : false
                })
                .ToList();

            // Assert the results
            Assert.AreEqual(4, results.Count);

            var under21Result = results.First(r => r.Id == personUnder21Id);
            Assert.IsFalse(under21Result.IsTwentyOneOrOver, "Person under 21 should have IsTwentyOneOrOver = false");

            var over21Result = results.First(r => r.Id == personOver21Id);
            Assert.IsTrue(over21Result.IsTwentyOneOrOver, "Person over 21 should have IsTwentyOneOrOver = true");

            var exactly21Result = results.First(r => r.Id == personExactly21Id);
            Assert.IsTrue(exactly21Result.IsTwentyOneOrOver, "Person exactly 21 should have IsTwentyOneOrOver = true (inclusive)");

            var nullDobResult = results.First(r => r.Id == personNullBirthdateId);
            Assert.IsFalse(nullDobResult.IsTwentyOneOrOver, "Person with null birthdate should have IsTwentyOneOrOver = false");
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

        [TestMethod]
        public void Query_CaseInWhere_ReturnsCorrectPersons()
        {
            OutputTestMethodName();
            // Arrange
            var guid = Guid.NewGuid().ToString();
            var adultId = InsertTestPerson(guid, "A", "Adult", DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var minorId = InsertTestPerson(guid, "B", "Minor", DateTime.Now.AddYears(-10), "Female", Guid.NewGuid());

            // Act
            var adults = _provider.Query<Person>()
                .Where(p => (p.Birthdate!.Value.Year < 2000 ? "Adult" : "Minor") == "Adult")
                .ToList();

            // Assert
            Assert.IsTrue(adults.Any(p => p.Id == adultId));
            Assert.IsFalse(adults.Any(p => p.Id == minorId));
        }

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

            var fredResult = results.First(r => r.Id == fredId);
            Assert.AreEqual("Mr.", fredResult.Salutation);

            var lisaResult = results.First(r => r.Id == lisaId);
            Assert.AreEqual("Ms.", lisaResult.Salutation);

            var maudeResult = results.First(r => r.Id == maudeId);
            Assert.AreEqual("Mrs.", maudeResult.Salutation);

            var otherResult = results.First(r => r.Id == otherId);
            Assert.IsNull(otherResult.Salutation);
        }

        [TestMethod]
        public void Query_OrderByWithTernaryExpression_GeneratesCaseAndOrdersCorrectly()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();

            // Use unique last-names per test run to avoid colliding with pre-existing data
            var maleLast = "ZLast_" + guid;
            var femaleLast = "ALast_" + guid;
            var otherLast = "BLast_" + guid;

            // Insert test persons with different genders and predictable names for ordering
            var maleId = InsertTestPerson("John", "J", maleLast, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var femaleId = InsertTestPerson("Jane", "J", femaleLast, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());
            var otherId = InsertTestPerson("Alex", "A", otherLast, DateTime.Now.AddYears(-20), "Other", Guid.NewGuid());

            // Act: Order by ternary expression - if Gender == "Male" then LastName, else FirstName
            var result = _provider.Query<Person>()
                .Where(p => p.LastName == maleLast || p.LastName == femaleLast || p.LastName == otherLast)
                .OrderBy(p => p.Gender == "Male" ? p.LastName : p.FirstName)
                .ToList();

            // Assert: Order should be Alex (FirstName), Jane (FirstName), John (LastName "ZLast")
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("Alex", result[0].FirstName);
            Assert.AreEqual("Jane", result[1].FirstName);
            Assert.AreEqual("John", result[2].FirstName);
            // Validate CASE statement is generated in ORDER BY
            Assert.IsTrue(_sb.ToString().Contains("CASE", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(_sb.ToString().Contains("ORDER BY", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void Query_OrderByDescendingWithTernaryExpression_GeneratesCaseAndOrdersCorrectly()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();

            // Use unique last-names per test run to avoid colliding with pre-existing data
            var maleLast = "ZLast_" + guid;
            var femaleLast = "ALast_" + guid;
            var otherLast = "BLast_" + guid;

            // Insert test persons with different genders and predictable names for ordering
            var maleId = InsertTestPerson("John", "J", maleLast, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            var femaleId = InsertTestPerson("Jane", "J", femaleLast, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());
            var otherId = InsertTestPerson("Alex", "A", otherLast, DateTime.Now.AddYears(-20), "Other", Guid.NewGuid());

            // Act: Order by descending ternary expression - if Gender == "Male" then LastName, else FirstName
            var result = _provider.Query<Person>()
                .Where(p => p.LastName == maleLast || p.LastName == femaleLast || p.LastName == otherLast)
                .OrderByDescending(p => p.Gender == "Male" ? p.LastName : p.FirstName)
                .ToList();

            // Assert: Reverse order - John (LastName "ZLast"), Jane (FirstName), Alex (FirstName)
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("John", result[0].FirstName);
            Assert.AreEqual("Jane", result[1].FirstName);
            Assert.AreEqual("Alex", result[2].FirstName);
            // Validate CASE statement is generated in ORDER BY with DESC
            Assert.IsTrue(_sb.ToString().Contains("CASE", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(_sb.ToString().Contains("ORDER BY", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(_sb.ToString().Contains("DESC", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void Query_ThenByWithTernaryExpression_GeneratesCaseAndOrdersCorrectly()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            // Use unique last-name to avoid collisions with other data
            var sameLast = "SameLast_" + guid;

            // Insert test persons with same LastName but different FirstNames and MiddleInitials
            InsertTestPerson("John", "Z", sameLast, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            InsertTestPerson("Jane", "A", sameLast, DateTime.Now.AddYears(-25), "Female", Guid.NewGuid());
            InsertTestPerson("Alex", "B", sameLast, DateTime.Now.AddYears(-20), "Other", Guid.NewGuid());

            // Act: Order by LastName, then by ternary - if Gender == "Male" then MiddleInitial, else FirstName
            var result = _provider.Query<Person>()
                .Where(p => p.LastName == sameLast)
                .OrderBy(p => p.LastName)
                .ThenBy(p => p.Gender == "Male" ? p.MiddleInitial : p.FirstName)
                .ToList();

            // Assert: All have same LastName, then ordered by ternary.
            // For the ternary CASE when gender == "Male" then middle_initial else first_name,
            // values will be: John -> "Z", Jane -> "Jane", Alex -> "Alex"
            // Lexical ascending order is: "Alex", "Jane", "Z" => Alex, Jane, John
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("Alex", result[0].FirstName);
            Assert.AreEqual("Jane", result[1].FirstName);
            Assert.AreEqual("John", result[2].FirstName);
            // Validate CASE statement is generated
            Assert.IsTrue(_sb.ToString().Contains("CASE", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(_sb.ToString().Contains("ORDER BY", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void Query_SelectDoesNotIncludeUnmappedProperties()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            var personId = InsertTestPerson(guid, "A", guid, DateTime.Now.AddYears(-30), "Male", Guid.NewGuid());
            
            // Clear previous logs
            _sb.Clear();
            
            // Query to trigger SELECT generation
            var person = _provider.Get<Person>(personId);
            
            // Assert that unmapped properties like 'uniqueid' are not in the SELECT
            var sql = _sb.ToString();
            Assert.IsTrue(sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(sql.Contains("Salutation", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void Insert_DoesNotIncludeUnmappedProperties()
        {
            OutputTestMethodName();
            var guid = Guid.NewGuid().ToString();
            
            // Clear previous logs
            _sb.Clear();
            
            var person = new Person
            {
                FirstName = guid,
                MiddleInitial = "A",
                LastName = guid,
                Birthdate = DateTime.Now.AddYears(-30),
                Gender = "Male",
                Salutation = "Mr.", // Unmapped
                IsTwentyOneOrOver = true,
                UniqueId = Guid.NewGuid(),
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow,
                
            };
            
            _provider.Insert(person);
            
            // Assert INSERT SQL does not include 'uniqueid'
            var sql = _sb.ToString();
            Assert.IsTrue(sql.Contains("INSERT", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(sql.Contains("Salutation", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(sql.Contains("IsTwentyOneOrOver", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// This test verifies that a query with a closure in the predicate works correctly, addressing
        /// a bug found in a consuming project.
        /// </summary>
        [TestMethod]
        public void Query_With_Closure_In_Predicate_Succeeds()
        {
            OutputTestMethodName();
            
            // Arrange
            var uniqueId = Guid.NewGuid();
            var personId = InsertTestPerson("ClosureTest", "C", "User", DateTime.Now.AddYears(-25), "Male", uniqueId);
            var addressId = InsertTestAddress("123 Closure Ln", null, "TestCity", "NY", "10001");
            
            var pa = new PersonAddress { PersonId = personId, AddressId = addressId };
            _provider.Insert(pa);

            // Act
            // The predicate uses local variables 'personId' and 'addressId', forcing the provider to handle closures.
            var existingPa = _provider.Query<PersonAddress>()
                .FirstOrDefault(x => x.PersonId == personId && x.AddressId == addressId);

            // Assert
            Assert.IsNotNull(existingPa, "Should find the PersonAddress record using closure variables.");
            Assert.AreEqual(personId, existingPa.PersonId);
            Assert.AreEqual(addressId, existingPa.AddressId);
        }

        [TestMethod]
        public void Query_With_Successive_Where_Clauses_Should_Use_Unique_Parameters()
        {
            OutputTestMethodName();
            
            // Arrange
            var uniqueId = Guid.NewGuid();
            var firstName = "TestMultiWhere_" + uniqueId.ToString().Substring(0, 8);
            var lastName = "Last_" + uniqueId.ToString().Substring(0, 8);
            
            // Person 1: Matches both
            InsertTestPerson(firstName, "M", lastName, DateTime.Now.AddYears(-30), "Male", uniqueId);
            
            // Person 2: Matches LastName only (should be excluded if AND works)
            var uniqueId2 = Guid.NewGuid();
            InsertTestPerson("DifferentFirst", "X", lastName, DateTime.Now.AddYears(-30), "Male", uniqueId2);

            // Act
            var query = _provider.Query<Person>();
            query = query.Where(x => x.FirstName.StartsWith("TestMultiWhere"));
            query = query.Where(x => x.LastName.StartsWith("Last_"));
            
            // This should not throw an exception about duplicate parameters
            var results = query.ToList();

            // Assert
            Assert.IsTrue(results.Any(x => x.UniqueId == uniqueId), "Should find the matching person");
            Assert.IsFalse(results.Any(x => x.UniqueId == uniqueId2), "Should NOT find the person matching only the second criteria");
        }

        [TestMethod]
        public void OrderBy_With_Unsupported_MethodCall_Throws_Informative_Exception()
        {
            OutputTestMethodName();
            
            var exception = Assert.ThrowsException<NotSupportedException>(() => 
            {
                _provider.Query<Person>().OrderBy(p => Guid.NewGuid()).ToList();
            });

            // The expression string for Guid.NewGuid() is usually "NewGuid()"
            StringAssert.Contains(exception.Message, "NewGuid()");
        }

        [TestMethod]
        public void OrderBy_With_Unsupported_Arithmetic_Throws_Informative_Exception()
        {
            OutputTestMethodName();
            
            var exception = Assert.ThrowsException<NotSupportedException>(() => 
            {
                _provider.Query<Person>().OrderBy(p => p.Id + 100).ToList();
            });

            // The expression string for p.Id + 100 usually contains "+" and "100"
            StringAssert.Contains(exception.Message, "+");
            StringAssert.Contains(exception.Message, "100");
        }

        [TestMethod]
        public void Where_With_Unsupported_MethodCall_Throws_Informative_Exception()
        {
            OutputTestMethodName();
            
            var exception = Assert.ThrowsException<NotSupportedException>(() => 
            {
                // ToString() on a property is not supported in SQL translation (except maybe inside other calls, but usually not directly as a boolean)
                // Actually ToString is often ignored or not supported. Let's use something definitely not supported like GetHashCode()
                _provider.Query<Person>().Where(p => p.FirstName.GetHashCode() == 123).ToList();
            });

            StringAssert.Contains(exception.Message, "GetHashCode");
        }

        [TestMethod]
        public void Query_MissingTable_Throws_Informative_Exception()
        {
            OutputTestMethodName();
            
            var exception = Assert.ThrowsException<InvalidOperationException>(() =>
            {
                _provider.Query<MissingTable>().ToList();
            });

            Console.WriteLine(exception.Message);
            StringAssert.Contains(exception.Message, "The table or view for entity 'MissingTable' was not found");
            StringAssert.Contains(exception.Message, "Expected table names: 'MissingTable' or 'missing_table'");
            StringAssert.Contains(exception.Message, "use the [Table(\"TableName\")] attribute");
        }
    }

    public class MissingTable
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}