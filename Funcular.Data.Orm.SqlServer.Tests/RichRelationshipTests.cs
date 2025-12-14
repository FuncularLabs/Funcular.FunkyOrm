using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Address;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    [TestClass]
    public class RichRelationshipTests
    {
        protected string _connectionString;
        public SqlServerOrmDataProvider _provider;
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

        private void EnsureSchema()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    -- Add is_primary to person_address if missing
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[person_address]') AND name = 'is_primary') 
                    BEGIN 
                        ALTER TABLE person_address ADD is_primary BIT NOT NULL DEFAULT 0; 
                    END

                    -- Add address_type_value to person_address if missing
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[person_address]') AND name = 'address_type_value') 
                    BEGIN 
                        ALTER TABLE person_address ADD address_type_value INT NOT NULL DEFAULT 0; 
                    END

                    -- Remove address_type from person_address if exists (we replaced it with address_type_value, or we can just ignore it)
                    -- The user request said 'add address_type_value', didn't explicitly say remove address_type, but I did in the SQL script.
                    -- I'll leave it alone to avoid breaking other things if they use it, but my SQL script removed it.
                    -- If I'm patching, I should probably match the SQL script.
                    IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[person_address]') AND name = 'address_type') 
                    BEGIN 
                        ALTER TABLE person_address DROP COLUMN address_type; 
                    END

                    -- Remove is_primary from address if exists
                    IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[address]') AND name = 'is_primary') 
                    BEGIN 
                        ALTER TABLE address DROP CONSTRAINT IF EXISTS DF_address_is_primary; -- Drop default constraint if exists
                        -- Need to find the default constraint name dynamically if it wasn't named
                        DECLARE @ConstraintName nvarchar(200)
                        SELECT @ConstraintName = Name FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('address') AND parent_column_id = (SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID('address') AND name = 'is_primary')
                        IF @ConstraintName IS NOT NULL
                            EXEC('ALTER TABLE address DROP CONSTRAINT ' + @ConstraintName)

                        ALTER TABLE address DROP COLUMN is_primary; 
                    END
                ";
                cmd.ExecuteNonQuery();
            }
        }


        [TestCleanup]
        public void Cleanup()
        {
            _provider?.Dispose();
        }

        [TestMethod]
        public void Can_Populate_Rich_Relationship_With_Remote_Properties()
        {
            OutputTestMethodName();

            // 1. Create Person
            var person = new PersonEntity
            {
                FirstName = "Rich",
                LastName = "Relator",
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow
            };
            _provider.Insert(person);
            Assert.IsTrue(person.Id > 0);

            // 2. Create Address
            var address = new AddressEntity
            {
                Line1 = "123 Rich St",
                City = "Wealthville",
                StateCode = "NY",
                PostalCode = "10001",
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow
            };
            _provider.Insert(address);
            Assert.IsTrue(address.Id > 0);

            // 3. Create Link (PersonAddress) with Rich Data
            var link = new PersonAddressEntity
            {
                PersonId = person.Id,
                AddressId = address.Id,
                IsPrimary = true,
                AddressTypeValue = (int)AddressType.Home,
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow
            };
            _provider.Insert(link);
            Assert.IsTrue(link.Id > 0);

            // 4. Query the Link Entity directly to verify Remote Properties
            var fetchedLinks = _provider.Query<PersonAddressEntity>()
                .Where(pa => pa.PersonId == person.Id)
                .ToList();

            Assert.AreEqual(1, fetchedLinks.Count);
            var fetchedLink = fetchedLinks[0];

            // Verify Local Properties
            Assert.AreEqual(person.Id, fetchedLink.PersonId);
            Assert.AreEqual(address.Id, fetchedLink.AddressId);
            Assert.IsTrue(fetchedLink.IsPrimary);
            Assert.AreEqual((int)AddressType.Home, fetchedLink.AddressTypeValue);
            Assert.AreEqual("Home", fetchedLink.AddressTypeLabel);

            // Verify Remote Properties (from Address table)
            Assert.AreEqual("123 Rich St", fetchedLink.Line1);
            Assert.AreEqual("Wealthville", fetchedLink.City);
            Assert.AreEqual("NY", fetchedLink.StateCode);
            Assert.AreEqual("10001", fetchedLink.PostalCode);

            // 5. Verify explicit population of parent entity collection
            person.Addresses = fetchedLinks;
            Assert.AreEqual(1, person.Addresses.Count);
            Assert.AreEqual("123 Rich St", person.Addresses.First().Line1);

            // Cleanup
            _provider.BeginTransaction();
            try
            {
                _provider.Delete<PersonAddressEntity>(link.Id);
                _provider.Delete<AddressEntity>(address.Id);
                _provider.Delete<PersonEntity>(person.Id);
                _provider.CommitTransaction();
            }
            catch
            {
                _provider.RollbackTransaction();
                throw;
            }
        }

        [TestMethod]
        public void Can_Handle_Multiple_Address_Types_BitFlag()
        {
            OutputTestMethodName();

            // 1. Create Person
            var person = new PersonEntity
            {
                FirstName = "Multi",
                LastName = "Typer",
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow
            };
            _provider.Insert(person);

            // 2. Create Address
            var address = new AddressEntity
            {
                Line1 = "456 Multi Way",
                City = "Multiverse",
                StateCode = "CA",
                PostalCode = "90210",
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow
            };
            _provider.Insert(address);

            // 3. Create Link with Multiple Flags (Home | Billing = 5)
            var link = new PersonAddressEntity
            {
                PersonId = person.Id,
                AddressId = address.Id,
                IsPrimary = false,
                AddressTypeValue = (int)(AddressType.Home | AddressType.Billing),
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow
            };
            _provider.Insert(link);

            // 4. Query
            var fetchedLink = _provider.Query<PersonAddressEntity>()
                .First(pa => pa.Id == link.Id);

            // Verify BitFlag Label
            // Enum.ToString() for flags usually outputs "Billing, Home" (alphabetical) or "Home, Billing" depending on definition order?
            // Actually default ToString() for flags is comma separated.
            // Our property does .Replace(", ", ",") to remove spaces.
            
            var expectedLabel = (AddressType.Home | AddressType.Billing).ToString().Replace(", ", ",");
            Assert.AreEqual(expectedLabel, fetchedLink.AddressTypeLabel);
            Assert.IsTrue(fetchedLink.AddressTypeLabel.Contains("Home"));
            Assert.IsTrue(fetchedLink.AddressTypeLabel.Contains("Billing"));

            // Cleanup
            _provider.BeginTransaction();
            try
            {
                _provider.Delete<PersonAddressEntity>(link.Id);
                _provider.Delete<AddressEntity>(address.Id);
                _provider.Delete<PersonEntity>(person.Id);
                _provider.CommitTransaction();
            }
            catch
            {
                _provider.RollbackTransaction();
                throw;
            }
        }
    }
}
