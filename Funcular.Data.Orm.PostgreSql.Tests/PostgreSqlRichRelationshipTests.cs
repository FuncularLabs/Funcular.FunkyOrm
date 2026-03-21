using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Address;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Enums;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    [TestClass]
    public class PostgreSqlRichRelationshipTests
    {
        protected string _connectionString;
        public PostgreSqlOrmDataProvider _provider;
        protected readonly StringBuilder _sb = new();

        public void OutputTestMethodName([CallerMemberName] string callerMemberName = "")
            => Debug.WriteLine($"\r\nTest: {callerMemberName}");

        [TestInitialize]
        public void Setup()
        {
            _sb.Clear();
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_PG_CONNECTION") ??
                "Host=localhost;Port=5432;Database=funky_db;Username=funky_user;Password=funky_password";
            TestConnection();
            _provider = new PostgreSqlOrmDataProvider(_connectionString)
            {
                Log = s => { Debug.WriteLine(s); Console.WriteLine(s); _sb.AppendLine(s); }
            };
        }

        [TestCleanup]
        public void Cleanup() => _provider?.Dispose();

        private void TestConnection()
        {
            using var connection = new NpgsqlConnection(_connectionString);
            try { connection.Open(); }
            catch (NpgsqlException ex) { Assert.Inconclusive($"PostgreSQL not available.\n{ex.Message}"); }
        }

        [TestMethod]
        public void Can_Populate_Rich_Relationship_With_Remote_Properties()
        {
            OutputTestMethodName();

            var person = new PersonEntity { FirstName = "Rich", LastName = "Relator", DateUtcCreated = DateTime.UtcNow, DateUtcModified = DateTime.UtcNow };
            _provider.Insert(person);
            Assert.IsTrue(person.Id > 0);

            var address = new AddressEntity { Line1 = "123 Rich St", City = "Wealthville", StateCode = "NY", PostalCode = "10001", DateUtcCreated = DateTime.UtcNow, DateUtcModified = DateTime.UtcNow };
            _provider.Insert(address);
            Assert.IsTrue(address.Id > 0);

            var link = new PersonAddressEntity
            {
                PersonId = person.Id, AddressId = address.Id, IsPrimary = true,
                AddressTypeValue = (int)AddressType.Home,
                DateUtcCreated = DateTime.UtcNow, DateUtcModified = DateTime.UtcNow
            };
            _provider.Insert(link);
            Assert.IsTrue(link.Id > 0);

            var fetchedLinks = _provider.Query<PersonAddressDetailEntity>()
                .Where(pa => pa.PersonId == person.Id).ToList();

            Assert.AreEqual(1, fetchedLinks.Count);
            var fetchedLink = fetchedLinks[0];
            Assert.AreEqual(person.Id, fetchedLink.PersonId);
            Assert.AreEqual(address.Id, fetchedLink.AddressId);
            Assert.IsTrue(fetchedLink.IsPrimary);
            Assert.AreEqual((int)AddressType.Home, fetchedLink.AddressTypeValue);
            Assert.AreEqual("123 Rich St", fetchedLink.Line1);
            Assert.AreEqual("Wealthville", fetchedLink.City);
            Assert.AreEqual("NY", fetchedLink.StateCode);
            Assert.AreEqual("10001", fetchedLink.PostalCode);

            // Cleanup
            _provider.BeginTransaction();
            try
            {
                _provider.Delete<PersonAddressEntity>(link.Id);
                _provider.Delete<AddressEntity>(address.Id);
                _provider.Delete<PersonEntity>(person.Id);
                _provider.CommitTransaction();
            }
            catch { _provider.RollbackTransaction(); throw; }
        }

        [TestMethod]
        public void Can_Handle_Multiple_Address_Types_BitFlag()
        {
            OutputTestMethodName();

            var person = new PersonEntity { FirstName = "Multi", LastName = "Typer", DateUtcCreated = DateTime.UtcNow, DateUtcModified = DateTime.UtcNow };
            _provider.Insert(person);

            var address = new AddressEntity { Line1 = "456 Multi Way", City = "Multiverse", StateCode = "CA", PostalCode = "90210", DateUtcCreated = DateTime.UtcNow, DateUtcModified = DateTime.UtcNow };
            _provider.Insert(address);

            var link = new PersonAddressEntity
            {
                PersonId = person.Id, AddressId = address.Id, IsPrimary = false,
                AddressTypeValue = (int)(AddressType.Home | AddressType.Shipping),
                DateUtcCreated = DateTime.UtcNow, DateUtcModified = DateTime.UtcNow
            };
            _provider.Insert(link);

            var fetched = _provider.Get<PersonAddressEntity>(link.Id);
            Assert.IsNotNull(fetched);
            Assert.AreEqual((int)(AddressType.Home | AddressType.Shipping), fetched.AddressTypeValue);

            var addressTypeEnum = (AddressType)(fetched.AddressTypeValue ?? 0);
            Assert.IsTrue(addressTypeEnum.HasFlag(AddressType.Home));
            Assert.IsTrue(addressTypeEnum.HasFlag(AddressType.Shipping));
            Assert.IsFalse(addressTypeEnum.HasFlag(AddressType.Billing));

            // Cleanup
            _provider.BeginTransaction();
            try
            {
                _provider.Delete<PersonAddressEntity>(link.Id);
                _provider.Delete<AddressEntity>(address.Id);
                _provider.Delete<PersonEntity>(person.Id);
                _provider.CommitTransaction();
            }
            catch { _provider.RollbackTransaction(); throw; }
        }
    }
}
