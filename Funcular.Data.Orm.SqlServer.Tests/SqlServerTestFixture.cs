using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Address;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Person;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    /// <summary>
    /// Shared test fixture providing connection setup, schema initialization, cleanup,
    /// and helper methods for SQL Server integration tests. This is NOT a [TestClass]
    /// to prevent inherited test method duplication in MSTest.
    /// </summary>
    public abstract class SqlServerTestFixture
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
        }

        [TestCleanup]
        public void Cleanup()
        {
            _provider?.Dispose();
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
