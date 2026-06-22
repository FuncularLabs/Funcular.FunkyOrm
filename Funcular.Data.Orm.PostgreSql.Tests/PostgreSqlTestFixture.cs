using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Objects.Address;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Objects.Person;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    /// <summary>
    /// Shared test fixture providing connection setup, cleanup, and helper methods
    /// for PostgreSQL integration tests. This is NOT a [TestClass] to prevent
    /// inherited test method duplication in MSTest.
    /// </summary>
    public abstract class PostgreSqlTestFixture
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
            _connectionString = PostgreSqlTestConnection.Resolve();
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
    }
}
