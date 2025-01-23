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
        protected SqlDataProvider? _provider;

        [TestInitialize]
        public void Setup()
        {
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION");
            if (string.IsNullOrEmpty(_connectionString))
                _connectionString ??= "Data Source=localhost;Initial Catalog=funky_db;Integrated Security=SSPI;TrustServerCertificate=true;";
            TestConnection();
            
            _provider ??= new SqlDataProvider(_connectionString)
            {
                // Optionally log SQL commands:
                Log = s => Debug.WriteLine(s) 
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
            var newPerson = new Person { FirstName = "Test", LastName = "User", Birthdate = DateTime.Today.Subtract(TimeSpan.FromDays(Random.Shared.Next(10,30))), DateUtcCreated = DateTime.UtcNow, DateUtcModified = DateTime.UtcNow};

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

        /*[TestMethod]
        public void Transaction_BeginRollback()
        {
            // TODO: Transactions need work; circumvent until issues are resolved
            Assert.IsTrue(true);
            return;

            Debug.Assert(_provider != null, nameof(_provider) + " != null");

            _provider.BeginTransaction();
            var person = new Person { FirstName = "TransactionTest", LastName = "User", Birthdate = null };
            _provider.Insert(person);

            // Check if the person was added in the transaction
            var addedPerson = _provider.Get<Person>(person.Id);
            Assert.IsNotNull(addedPerson);

            _provider.RollbackTransaction();

            // After rollback, the person should not exist
            var rolledBackPerson = _provider.Get<Person>(person.Id);
            Assert.IsNull(rolledBackPerson);
        }

        [TestMethod]
        public void Transaction_BeginCommit()
        {
            // TODO: Transactions need work; circumvent until issues are resolved
            Assert.IsTrue(true);
            return;

            _provider?.BeginTransaction();
            var person = new Person { FirstName = "CommitTest", LastName = "User", Birthdate = null };
            _provider?.Insert(person);

            _provider?.CommitTransaction();

            // Check if the person was committed to the database
            var committedPerson = _provider?.Get<Person>(person.Id);
            Assert.IsNotNull(committedPerson);
        }*/

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

        [TestCleanup]
        public void Cleanup()
        {
            _provider?.Dispose();
        }
    }
}