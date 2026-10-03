using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Address;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Objects.Address;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Objects.Person;
using Funcular.Data.Orm.Tests.Shared;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    /// <summary>
    /// Shared test fixture providing connection setup, cleanup, and helper methods
    /// for PostgreSQL integration tests. This is NOT a [TestClass] to prevent
    /// inherited test method duplication in MSTest.
    /// </summary>
    /// <remarks>
    /// Each test runs with a <see cref="TestRowLedger"/>: a test that inserts into person or a related table passes
    /// each inserted entity to <see cref="Track"/> (the Insert helpers here do), and <see cref="Cleanup"/> deletes
    /// those rows and fails the test if it left the guarded tables' row counts changed.
    /// </remarks>
    public abstract class PostgreSqlTestFixture
    {
        protected string _connectionString;
        public PostgreSqlOrmDataProvider _provider;
        protected readonly StringBuilder _sb = new();
        protected TestRowLedger _rows;

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
            _rows = TestRowLedger.Begin(() => new NpgsqlConnection(_connectionString), TestRowLedger.PostgreSqlTables);
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Dispose first: it rolls back a transaction a failed test left open, so the deletes don't wait on it.
            _provider?.Dispose();
            _rows?.Finish();
        }

        /// <summary>Records a row this test inserted, for <see cref="Cleanup"/> to delete.</summary>
        protected void Track(object entity) => _rows.Track(entity);

        /// <summary>
        /// Inserts and tracks one forward remote chain: a country named <paramref name="countryPrefix"/> plus a guid,
        /// an address in it, an organization headquartered there, and a person it employs.
        /// </summary>
        protected (int PersonId, int CountryId, string CountryName) SeedEmployerChain(string countryPrefix)
        {
            var country = new CountryEntity { Name = countryPrefix + Guid.NewGuid() };
            _provider.Insert(country);
            Track(country);
            var address = new AddressEntity { Line1 = "1 Chain St", City = "ChainCity", StateCode = "NY", PostalCode = "10001", CountryId = country.Id };
            _provider.Insert(address);
            Track(address);
            var organization = new OrganizationEntity { Name = "ChainOrg_" + Guid.NewGuid(), HeadquartersAddressId = address.Id };
            _provider.Insert(organization);
            Track(organization);
            var person = new PersonEntity { FirstName = "Chain", LastName = "Test_" + Guid.NewGuid().ToString().Substring(0, 8), EmployerId = organization.Id };
            _provider.Insert(person);
            Track(person);
            return (person.Id, country.Id, country.Name);
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
            Track(person);
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
            Track(address);
            return address.Id;
        }

        protected void InsertTestPersonAddress(int personId, int addressId)
        {
            var link = new PersonAddress { PersonId = personId, AddressId = addressId };
            _provider.Insert(link);
            Track(link);
        }
    }
}
