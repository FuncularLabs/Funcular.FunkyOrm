using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Address;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Person;
using Funcular.Data.Orm.Tests.Shared;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    /// <summary>
    /// Shared test fixture providing connection setup, schema initialization, cleanup,
    /// and helper methods for SQL Server integration tests. This is NOT a [TestClass]
    /// to prevent inherited test method duplication in MSTest.
    /// </summary>
    /// <remarks>
    /// Each test runs with a <see cref="TestRowLedger"/>: a test that inserts into person or a related table passes
    /// each inserted entity to <see cref="Track"/> (the Insert helpers here do), and <see cref="Cleanup"/> deletes
    /// those rows and fails the test if it left the guarded tables' row counts changed.
    /// </remarks>
    public abstract class SqlServerTestFixture
    {
        protected string _connectionString;
        public required SqlServerOrmDataProvider _provider;
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
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
                                "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=funky_db;Integrated Security=True;";
            TestConnection();
            EnsureSchema();

            _provider = new SqlServerOrmDataProvider(_connectionString)
            {
                Log = s =>
                {
                    Debug.WriteLine(s);
                    Console.WriteLine(s);
                    _sb.AppendLine(s);
                }
            };
            _rows = TestRowLedger.Begin(() => new SqlConnection(_connectionString), TestRowLedger.SqlServerTables);
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

        private void EnsureSchema()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'country')
                    BEGIN
                        CREATE TABLE country (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            name NVARCHAR(100) NOT NULL
                        );
                    END

                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[address]') AND name = 'country_id') 
                    BEGIN 
                        ALTER TABLE address ADD country_id INT NULL; 
                        ALTER TABLE address ADD CONSTRAINT FK_address_country FOREIGN KEY (country_id) REFERENCES country(id);
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'organization')
                    BEGIN
                        CREATE TABLE organization (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            name NVARCHAR(100) NOT NULL,
                            headquarters_address_id INT NULL,
                            row_version ROWVERSION NOT NULL,
                            CONSTRAINT FK_organization_address FOREIGN KEY (headquarters_address_id) REFERENCES address(id)
                        );
                    END

                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[organization]') AND name = 'row_version')
                    BEGIN
                        ALTER TABLE organization ADD row_version ROWVERSION NOT NULL;
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'non_identity_guid_entity')
                    BEGIN
                        CREATE TABLE non_identity_guid_entity (
                            id UNIQUEIDENTIFIER PRIMARY KEY,
                            name NVARCHAR(100) NOT NULL
                        );
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'non_identity_string_entity')
                    BEGIN
                        CREATE TABLE non_identity_string_entity (
                            id NVARCHAR(50) PRIMARY KEY,
                            name NVARCHAR(100) NOT NULL
                        );
                    END

                    -- A legacy datetime (not datetime2) column, for the 3.10 ORDER BY value tests.
                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'legacy_datetime_probe')
                    BEGIN
                        CREATE TABLE legacy_datetime_probe (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            label NVARCHAR(50) NOT NULL,
                            stamp DATETIME NOT NULL
                        );
                    END

                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[person]') AND name = 'employer_id') 
                    BEGIN 
                        ALTER TABLE person ADD employer_id INT NULL; 
                        ALTER TABLE person ADD CONSTRAINT FK_person_organization FOREIGN KEY (employer_id) REFERENCES organization(id);
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'project_category')
                    BEGIN
                        CREATE TABLE project_category (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            name NVARCHAR(100) NOT NULL,
                            code NVARCHAR(50) NOT NULL
                        );
                        INSERT INTO project_category (name, code) VALUES
                            ('Internal Tooling', 'internal'),
                            ('Client Deliverable', 'client'),
                            ('Research & Development', 'rnd');
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'project')
                    BEGIN
                        CREATE TABLE project (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            name NVARCHAR(200) NOT NULL,
                            organization_id INT NOT NULL,
                            lead_id INT NULL,
                            category_id INT NULL,
                            budget DECIMAL(12,2) NULL,
                            score INT NULL,
                            metadata NVARCHAR(MAX) NULL,
                            dateutc_created DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            dateutc_modified DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                        );
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'project_milestone')
                    BEGIN
                        CREATE TABLE project_milestone (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            project_id INT NOT NULL,
                            title NVARCHAR(200) NOT NULL,
                            status NVARCHAR(50) NOT NULL DEFAULT 'pending',
                            due_date DATE NULL,
                            completed_date DATE NULL
                        );
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'project_note')
                    BEGIN
                        CREATE TABLE project_note (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            project_id INT NOT NULL,
                            author_id INT NULL,
                            content NVARCHAR(MAX) NOT NULL,
                            category NVARCHAR(50) NOT NULL DEFAULT 'general',
                            dateutc_created DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                        );
                    END";
                cmd.ExecuteNonQuery();
            }
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
