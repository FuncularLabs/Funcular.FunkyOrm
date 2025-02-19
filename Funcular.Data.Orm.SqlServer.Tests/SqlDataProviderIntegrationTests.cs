using System.Diagnostics;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Person;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Address;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    [TestClass]
    public class SqlDataProviderIntegrationTests
    {
        protected string? _connectionString;
        protected FunkySqlDataProvider? _provider;

        [TestInitialize]
        public void Setup()
        {
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION");
            if (string.IsNullOrEmpty(_connectionString))
                _connectionString ??= "Data Source=localhost;Initial Catalog=funky_db;Integrated Security=SSPI;TrustServerCertificate=true;";
            TestConnection();

            _provider ??= new FunkySqlDataProvider(_connectionString)
            {
                // Optionally log SQL commands:
                Log = s =>
                {
                    Debug.WriteLine(s);
                }
            };
        }

        public void TestConnection()
        {
            SqlConnection? connection = null;
            try
            {
                // Attempt to open the connection
                connection = new SqlConnection(_connectionString);
                connection.Open();

                // If this point is reached, connection succeeded
                Console.WriteLine("Connection successful.");
            }
            catch (SqlException ex)
            {
                // If connection fails, throw an exception with a specific message
                throw new ArgumentNullException("connectionString", "Neither localhost.funky_db server/database exists, nor Environment variable FUNKY_CONNECTION; please ensure the funky_db database is created and configure the connection string to point to it.\r\n\r\n" + ex.ToString());
            }
            finally
            {
                // Always attempt to close the connection if it was opened
                if (connection != null)
                {
                    connection.Close();
                    connection.Dispose();
                }
            }
        }

        [TestMethod]
        public void Warm_Up()
        {
            var count = _provider?.Query<Person>(x => x.FirstName != null).Count;
            Assert.IsTrue(count > 0);
        }

        [TestMethod]
        public void Get_WithExistingId_ReturnsPerson()
        {
            var person = _provider?.Get<Person>(1);
            Assert.IsNotNull(person);
            Assert.AreEqual(1, person.Id);
        }

        [TestMethod]
        public void Query_WithExpression_ReturnsFilteredAddresses()
        {
            const string stateCode = "IL";
            var addresses = _provider?.Query<Address>(a => a.StateCode == stateCode);
            Assert.IsTrue(addresses?.Count > 0 == true && addresses?.All(x => x.StateCode == stateCode) == true, "No addresses found in IL.");
        }

        [TestMethod]
        public void GetList_ReturnsAllPersonAddressLinks()
        {
            var links = _provider?.GetList<PersonAddress>();
            Assert.IsTrue(links?.Count > 0, "No person-address links found.");
        }

        [TestMethod]
        public void Insert_NewPerson_IncreasesCount()
        {
            var initialCount = _provider?.GetList<Person>().Count;
            var newPerson = new Person { FirstName = "Test", LastName = "User", Birthdate = DateTime.Today.Subtract(TimeSpan.FromDays(Random.Shared.Next(10, 30))), DateUtcCreated = DateTime.UtcNow, DateUtcModified = DateTime.UtcNow };

            _provider?.Insert(newPerson);

            var updatedCount = _provider?.GetList<Person>().Count;
            Assert.AreEqual(initialCount + 1, updatedCount, "Person was not inserted.");
        }

        [TestMethod]
        public void Update_PersonUpdates()
        {
            var person = _provider?.Get<Person>(1);
            if (person != null)
            {
                var originalName = person.FirstName;
                person.FirstName = $"UpdatedName{DateTime.Now.Ticks}";

                _provider?.Update(person);

                var updatedPerson = _provider?.Get<Person>(1);
                Assert.IsNotNull(updatedPerson);
                Assert.AreNotEqual(originalName, updatedPerson.FirstName, "Update did not change the first name.");
            }
            else
            {
                Assert.Fail("No person with ID 1 found to test update.");
            }
        }

        [TestMethod]
        public void Query_Person_WithLastNameInList_ReturnsCorrectPersons()
        {
            // Arrange
            var personsToInsert = new List<Person>
           {
               new Person { LastName = "Smith", FirstName = "Test" },
               new Person { LastName = "Johnson", FirstName = "Test"  },
               new Person { LastName = "Doe", FirstName = "Test"  }
           };

            // Insert test data
            personsToInsert.ForEach(p => _provider?.Insert(p));

            var lastNames = new List<string> { "Smith", "Johnson" };

            // Act
            var result = _provider?.Query<Person>(p => p.LastName != null && lastNames.Contains(p.LastName)).ToList();

            // Assert
            Assert.IsTrue(result?.Count > 0);
            Assert.IsTrue(result.All(p => p.LastName == "Smith" || p.LastName == "Johnson"));
            Assert.IsFalse(result.Any(p => p.LastName == "Doe")); // Doe should not be in the result
        }

        [TestMethod]
        public void Query_Person_WithLastNameInArray_ReturnsCorrectPersons()
        {
            // Arrange
            var personsToInsert = new List<Person>
            {
                new Person { LastName = "Smith", FirstName = "Test" },
                new Person { LastName = "Johnson", FirstName = "Test"  },
                new Person { LastName = "Doe", FirstName = "Test"  }
            };

            // Insert test data
            personsToInsert.ForEach(p => _provider?.Insert(p));

            var lastNames = new[] { "Smith", "Johnson" };

            // Act
            var result = _provider?.Query<Person>(p => lastNames.Contains(p.LastName)).ToList();

            // Assert
            Assert.IsTrue(result?.Count >= 2);
            Assert.IsTrue(result.All(p => p.LastName == "Smith" || p.LastName == "Johnson"));
            Assert.IsFalse(result.Any(p => p.LastName == "Doe")); // Doe should not be in the result
        }

        [TestMethod]
        public void Query_Person_WithEmptyList_ReturnsEmptyResult()
        {
            // Arrange
            var lastNames = new string[] { };

            // Act
            var result = _provider?.Query<Person>(p => lastNames.Contains(p.LastName)).ToList() ?? [];

            // Assert
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void Query_Person_LastNameStartsWith_ReturnsCorrectPersons()
        {
            // Act
            var result = _provider?.Query<Person>(x => x.LastName!.StartsWith("J")).ToList() ?? [];

            // Assert
            Assert.IsTrue(result.Any() && result.All(x => x.LastName!.StartsWith("J", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public void Query_Person_LastNameEndsWith_ReturnsCorrectPersons()
        {
            // Act
            var result = _provider?.Query<Person>(x => x.LastName!.EndsWith("s")).ToList() ?? [];

            // Assert
            Assert.IsTrue(result.Any() && result.All(x => x.LastName!.EndsWith("s", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public void Query_Person_LastNameContains_ReturnsCorrectPersons()
        {
            // Act
            var result = _provider?.Query<Person>(x => x.LastName!.Contains("on")).ToList() ?? [];

            // Assert
            Assert.IsTrue(result.Any() && result.All(x => x.LastName!.Contains("on", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public void Query_Person_LastNameStartsWith_NoMatch_ReturnsEmptyList()
        {
            var value = "ToString";
            // Act
            var result = _provider?.Query<Person>(x => x.LastName!.StartsWith(value)).ToList() ?? [];

            // Assert
            Assert.IsTrue(result.Count == 0);
        }

        [TestMethod]
        public void Query_Person_LastNameEndsWith_NoMatch_ReturnsEmptyList()
        {
            // Act
            var value = Guid.NewGuid().ToString();
            var result = _provider?.Query<Person>(x => x.LastName!.EndsWith(value)).ToList() ?? [];

            // Assert
            Assert.IsTrue(result.Count == 0);
        }

        [TestMethod]
        public void Query_Person_LastNameContains_NoMatch_ReturnsEmptyList()
        {
            var value = Guid.NewGuid().ToString();
            // Act
            var result = _provider?.Query<Person>(x => x.LastName!.Contains(value)).ToList() ?? [];

            // Assert
            Assert.IsTrue(result.Count == 0);
        }

        [TestMethod]
        public void Transaction_BeginCommit()
        {
            Debug.Assert(_provider != null, nameof(_provider) + " != null");

            // Begin a transaction
            _provider.BeginTransaction();

            // Insert a temporary person within the transaction
            var person = new Person { FirstName = "CommitTest", LastName = "User", Birthdate = null };
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
            Debug.Assert(_provider != null, nameof(_provider) + " != null");

            // Begin a transaction
            _provider.BeginTransaction();

            // Insert a temporary person within the transaction
            var person = new Person { FirstName = "TransactionTest", LastName = "User", Birthdate = null };
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
            Debug.Assert(_provider != null, nameof(_provider) + " != null");

            // Begin a transaction
            _provider.BeginTransaction();

            // Perform multiple operations
            var person = new Person { FirstName = "MultiOps", LastName = "User", Birthdate = null };
            _provider.Insert(person);

            var address = new Address { Line1 = "Test St", City = "TestCity", StateCode = "TC", PostalCode = "12345" };
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
            // Create a unique trace value to assign to the Gender; we will use that later to ensure we ignore others' test data:
            var uniqueString = Guid.NewGuid().ToString().Substring(28);

            // Arrange
            var personsToInsert = new List<Person>
            {
                new Person { LastName = "Smith", FirstName = "Test", Birthdate = DateTime.Today.AddYears(-30), Gender = uniqueString },
                new Person { LastName = "Johnson", FirstName = "Test", Birthdate = DateTime.Today.AddYears(-25), Gender = uniqueString },
                new Person { LastName = "Doe", FirstName = "Test", Birthdate = DateTime.Today.AddYears(-40), Gender = uniqueString }
            };
            personsToInsert.ForEach(p => _provider?.Insert(p));

            var fromDate = DateTime.Today.AddYears(-35);
            var toDate = DateTime.Today.AddYears(-20);

            // Act
            var persons = _provider?.Query<Person>(p => p.Birthdate >= fromDate && p.Birthdate <= toDate).ToList();
            // After the SQL query executes, ensure we are only paying attention to the rows we just inserted:
            var result = persons?.Where(x => x.Gender == uniqueString).ToList();

            // Assert
            Assert.IsTrue(result?.Count == 2);
            Assert.IsTrue(result.All(x => x.LastName == "Smith" || x.LastName == "Johnson"));
        }


        [TestMethod]
        public void Query_Person_WithOrElse_Birthdates_ReturnsCorrectPersons()
        {
            // Create a unique trace value to assign to the Gender; we will use that later to ensure we ignore others' test data:
            var uniqueString = Guid.NewGuid().ToString().Substring(startIndex: 28);

            var fromDate = DateTime.Today.AddYears(-100);
            var toDate = DateTime.Today.AddYears(100);

            // Arrange
            var personsToInsert = new List<Person>
            {
                new Person { LastName = "Smith", FirstName = "Test", Birthdate = DateTime.Today.AddYears(-101), Gender = uniqueString },
                new Person { LastName = "Johnson", FirstName = "Test", Birthdate = DateTime.Today.AddYears(101), Gender = uniqueString },
            };
            personsToInsert.ForEach(p => _provider?.Insert(p));

            
            // Act
            var persons = _provider?.Query<Person>(p => p.Birthdate <= fromDate || p.Birthdate >= toDate).ToList();
            // After the SQL query executes, ensure we are only paying attention to the rows we just inserted:
            var result = persons?.Where(x => x.Gender == uniqueString).ToList();

            // Assert
            Assert.IsTrue(result?.Count == 2);
            Assert.IsTrue(result.All(x => x.LastName == "Smith" || x.LastName == "Johnson"));
        }

        [TestMethod]
        public void Query_Person_WithNullBirthdate_HandlesNullCorrectly()
        {
            // Arrange
            // Create a unique trace value to assign to the Gender; we will use that later to ensure we ignore others' test data:
            var uniqueString = Guid.NewGuid().ToString().Substring(28);
            var personsToInsert = new List<Person>
            {
                new Person { LastName = "NullDate", FirstName = "Test", Birthdate = null, Gender = uniqueString},
                new Person { LastName = "HasDate", FirstName = "Test", Birthdate = DateTime.Today.AddYears(-30), Gender = uniqueString }
            };
            personsToInsert.ForEach(p => _provider?.Insert(p));

            // Act
            var nullBirthdate = _provider?.Query<Person>(p => p.Birthdate == null).ToList()
                // After the SQL query executes, ensure we are only paying attention to the rows we just inserted:
                .Where(x => x.Gender == uniqueString).ToList();
            
            var hasBirthdate = _provider?.Query<Person>(p => p.Birthdate != null).ToList()
            // After the SQL query executes, ensure we are only paying attention to the rows we just inserted:
                .Where(x => x.Gender == uniqueString).ToList();

            // Assert
            Assert.IsTrue(nullBirthdate?.Count > 0 && nullBirthdate[0].LastName == "NullDate");
            Assert.IsTrue(hasBirthdate?.Count > 0 && hasBirthdate[0].LastName == "HasDate");
        }

        #endregion


        #region GuidTests

        [TestMethod]
        public void Query_Person_WithSpecificGuid_ReturnsCorrectPerson()
        {
            // Arrange
            var uniqueGuid = Guid.NewGuid();
            var person = new Person { LastName = "GuidTest", FirstName = "Test", UniqueId = uniqueGuid };
            _provider?.Insert(person);

            // Act
            var result = _provider?.Query<Person>(p => p.UniqueId == uniqueGuid).ToList();

            // Assert
            Assert.IsTrue(result?.Count == 1);
            Assert.AreEqual(uniqueGuid, result[0].UniqueId);
        }

        [TestMethod]
        public void Query_Person_GuidInList_ReturnsCorrectPersons()
        {
            // Arrange
            var guids = new List<Guid?> { Guid.NewGuid(), Guid.NewGuid() };
            var personsToInsert = new List<Person>
            {
                new Person { LastName = "GuidOne", FirstName = "Test", UniqueId = guids[0] },
                new Person { LastName = "GuidTwo", FirstName = "Test", UniqueId = guids[1] },
                new Person { LastName = "NoMatch", FirstName = "Test", UniqueId = Guid.NewGuid() }
            };
            personsToInsert.ForEach(p => _provider?.Insert(p));

            // Act
            var result = _provider?.Query<Person>(p => guids.Contains(p.UniqueId)).ToList();

            // Assert
            Assert.IsTrue(result?.Count == 2);
            Assert.IsTrue(result.All(p => guids.Contains(p.UniqueId)));
        }

        #endregion

        [TestCleanup]
        public void Cleanup()
        {
            _provider?.Dispose();
        }
    }
}