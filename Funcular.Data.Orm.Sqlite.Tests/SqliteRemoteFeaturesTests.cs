using System.Diagnostics;
using System.Text;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Address;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Enums;
using Microsoft.Data.Sqlite;

namespace Funcular.Data.Orm.Sqlite.Tests
{
    [TestClass]
    public class SqliteRemoteFeaturesTests
    {
        private static string _dbPath;
        private SqliteOrmDataProvider _provider;
        private readonly StringBuilder _sb = new();

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"funky_sqlite_remote_{Guid.NewGuid():N}.db");
            var connStr = $"Data Source={_dbPath}";
            using var conn = new SqliteConnection(connStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS country (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS organization (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, headquarters_address_id INTEGER);
CREATE TABLE IF NOT EXISTS address (id INTEGER PRIMARY KEY AUTOINCREMENT, line_1 TEXT, line_2 TEXT, city TEXT, state_code TEXT, postal_code TEXT, dateutc_created TEXT NOT NULL DEFAULT (datetime('now')), dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')), country_id INTEGER);
CREATE TABLE IF NOT EXISTS person (id INTEGER PRIMARY KEY AUTOINCREMENT, first_name TEXT, middle_initial TEXT, last_name TEXT, birthdate TEXT, gender TEXT, dateutc_created TEXT NOT NULL DEFAULT (datetime('now')), dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')), unique_id TEXT, employer_id INTEGER);
CREATE TABLE IF NOT EXISTS person_address (id INTEGER PRIMARY KEY AUTOINCREMENT, person_id INTEGER NOT NULL, address_id INTEGER NOT NULL, is_primary INTEGER NOT NULL DEFAULT 0, address_type_value INTEGER DEFAULT 0, dateutc_created TEXT NOT NULL DEFAULT (datetime('now')), dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')), FOREIGN KEY (person_id) REFERENCES person(id), FOREIGN KEY (address_id) REFERENCES address(id));";
            cmd.ExecuteNonQuery();
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        }

        [TestInitialize]
        public void Setup()
        {
            _sb.Clear();
            _provider = new SqliteOrmDataProvider($"Data Source={_dbPath}")
            {
                Log = s => { Debug.WriteLine(s); _sb.AppendLine(s); }
            };
        }

        [TestCleanup]
        public void Cleanup()
        {
            _provider?.Dispose();
        }

        [TestMethod]
        public async Task RemoteKey_FullChainPopulation_ImplicitAndExplicit()
        {
            var country = new CountryEntity { Name = "TestCountry_" + Guid.NewGuid() };
            await _provider.InsertAsync(country);

            var address = new AddressEntity
            {
                Line1 = "123 HQ St", City = "Metropolis", StateCode = "NY", PostalCode = "10001",
                CountryId = country.Id
            };
            await _provider.InsertAsync(address);

            var org = new OrganizationEntity
            {
                Name = "TestOrg_" + Guid.NewGuid(),
                HeadquartersAddressId = address.Id
            };
            await _provider.InsertAsync(org);

            var person = new PersonEntity
            {
                FirstName = "John", LastName = "Doe", EmployerId = org.Id
            };
            await _provider.InsertAsync(person);

            var fetchedPerson = await _provider.GetAsync<PersonDetailEntity>(person.Id);

            Assert.IsNotNull(fetchedPerson);
            Assert.AreEqual(country.Name, fetchedPerson.EmployerHeadquartersCountryName);
            Assert.AreEqual(country.Id, fetchedPerson.EmployerHeadquartersCountryId);
        }

        [TestMethod]
        public async Task RemoteKey_OuterJoin_NullHandling()
        {
            var org = new OrganizationEntity
            {
                Name = "TestOrg_NoAddr_" + Guid.NewGuid(),
                HeadquartersAddressId = null
            };
            await _provider.InsertAsync(org);

            var person = new PersonEntity
            {
                FirstName = "Jane", LastName = "Doe", EmployerId = org.Id
            };
            await _provider.InsertAsync(person);

            var fetchedPerson = await _provider.GetAsync<PersonDetailEntity>(person.Id);

            Assert.IsNotNull(fetchedPerson);
            Assert.IsNull(fetchedPerson.EmployerHeadquartersCountryName);
            Assert.IsNull(fetchedPerson.EmployerHeadquartersCountryId);
        }

        [TestMethod]
        public void RemoteProperty_IsPopulated_OnQuery()
        {
            // Seed a full chain
            var country = new CountryEntity { Name = "QueryCountry_" + Guid.NewGuid() };
            _provider.Insert(country);
            var address = new AddressEntity { Line1 = "1 St", City = "C", StateCode = "TX", PostalCode = "00000", CountryId = country.Id };
            _provider.Insert(address);
            var org = new OrganizationEntity { Name = "QueryOrg_" + Guid.NewGuid(), HeadquartersAddressId = address.Id };
            _provider.Insert(org);
            var person = new PersonEntity { FirstName = "Query", LastName = "Test", EmployerId = org.Id };
            _provider.Insert(person);

            var fetched = _provider.Query<PersonDetailEntity>()
                .FirstOrDefault(p => p.Id == person.Id);

            Assert.IsNotNull(fetched);
            Assert.AreEqual(country.Name, fetched.EmployerHeadquartersCountryName);
        }

        [TestMethod]
        public void RemoteKey_IsPopulated_OnQuery()
        {
            var country = new CountryEntity { Name = "KeyCountry_" + Guid.NewGuid() };
            _provider.Insert(country);
            var address = new AddressEntity { Line1 = "2 St", City = "D", StateCode = "CA", PostalCode = "11111", CountryId = country.Id };
            _provider.Insert(address);
            var org = new OrganizationEntity { Name = "KeyOrg_" + Guid.NewGuid(), HeadquartersAddressId = address.Id };
            _provider.Insert(org);
            var person = new PersonEntity { FirstName = "Key", LastName = "Test", EmployerId = org.Id };
            _provider.Insert(person);

            var fetched = _provider.Query<PersonDetailEntity>()
                .FirstOrDefault(p => p.Id == person.Id);

            Assert.IsNotNull(fetched);
            Assert.AreEqual(country.Id, fetched.EmployerHeadquartersCountryId);
        }

        [TestMethod]
        public void OrderBy_RemoteProperty_OrdersByJoinedColumn()
        {
            var asc = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null)
                .OrderBy(p => p.EmployerHeadquartersCountryName).ToList();
            var desc = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName != null)
                .OrderByDescending(p => p.EmployerHeadquartersCountryName).ToList();
            Assert.IsNotNull(asc);
            Assert.IsNotNull(desc);
            Assert.AreEqual(asc.Count, desc.Count);
            if (asc.Count >= 2)
            {
                var ascNames = asc.Select(r => r.EmployerHeadquartersCountryName).ToList();
                var descNames = desc.Select(r => r.EmployerHeadquartersCountryName).ToList();
                ascNames.Reverse();
                CollectionAssert.AreEqual(ascNames, descNames,
                    "DESC ordering of a [RemoteProperty] should be the exact reverse of ASC.");
            }
        }

        [TestMethod]
        public void Select_RemoteProperty_InCustomProjection_Throws()
        {
            Assert.ThrowsException<System.NotSupportedException>(() =>
                _provider.Query<PersonDetailEntity>()
                    .Select(p => new PersonDetailEntity { EmployerHeadquartersCountryName = p.EmployerHeadquartersCountryName })
                    .ToList());
        }

        [TestMethod]
        public void CanFilterByRemoteProperty()
        {
            var country = new CountryEntity { Name = "FilterCountry_" + Guid.NewGuid() };
            _provider.Insert(country);
            var address = new AddressEntity { Line1 = "3 St", City = "E", StateCode = "WA", PostalCode = "22222", CountryId = country.Id };
            _provider.Insert(address);
            var org = new OrganizationEntity { Name = "FilterOrg_" + Guid.NewGuid(), HeadquartersAddressId = address.Id };
            _provider.Insert(org);
            var person = new PersonEntity { FirstName = "Filter", LastName = "Test", EmployerId = org.Id };
            _provider.Insert(person);

            var results = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryName == country.Name)
                .ToList();

            Assert.IsTrue(results.Count > 0);
            Assert.IsTrue(results.All(p => p.EmployerHeadquartersCountryName == country.Name));
        }

        [TestMethod]
        public void CanFilterByRemoteKey()
        {
            var country = new CountryEntity { Name = "FilterKeyCountry_" + Guid.NewGuid() };
            _provider.Insert(country);
            var address = new AddressEntity { Line1 = "4 St", City = "F", StateCode = "OR", PostalCode = "33333", CountryId = country.Id };
            _provider.Insert(address);
            var org = new OrganizationEntity { Name = "FilterKeyOrg_" + Guid.NewGuid(), HeadquartersAddressId = address.Id };
            _provider.Insert(org);
            var person = new PersonEntity { FirstName = "FilterKey", LastName = "Test", EmployerId = org.Id };
            _provider.Insert(person);

            var results = _provider.Query<PersonDetailEntity>()
                .Where(p => p.EmployerHeadquartersCountryId == country.Id)
                .ToList();

            Assert.IsTrue(results.Count > 0);
            Assert.IsTrue(results.All(p => p.EmployerHeadquartersCountryId == country.Id));
        }

        [TestMethod]
        public void Can_Populate_Rich_Relationship_With_Remote_Properties()
        {
            var person = new PersonEntity { FirstName = "Rich", LastName = "Relator", DateUtcCreated = DateTime.UtcNow, DateUtcModified = DateTime.UtcNow };
            _provider.Insert(person);

            var address = new AddressEntity { Line1 = "123 Rich St", City = "Wealthville", StateCode = "NY", PostalCode = "10001", DateUtcCreated = DateTime.UtcNow, DateUtcModified = DateTime.UtcNow };
            _provider.Insert(address);

            var link = new PersonAddressEntity
            {
                PersonId = person.Id, AddressId = address.Id, IsPrimary = true,
                AddressTypeValue = (int)AddressType.Home,
                DateUtcCreated = DateTime.UtcNow, DateUtcModified = DateTime.UtcNow
            };
            _provider.Insert(link);

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

        [TestMethod]
        public void Count_FilteredByRemoteProperty_MatchesMaterialized()
        {
            var expected = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).ToList().Count;
            var actual = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).Count();
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Count_WithRemotePropertyPredicate_MatchesMaterialized()
        {
            var expected = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).ToList().Count;
            var actual = _provider.Query<PersonDetailEntity>().Count(p => p.EmployerHeadquartersCountryName != null);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Any_FilteredByRemoteProperty_MatchesMaterialized()
        {
            var expected = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).ToList().Any();
            var actual = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).Any();
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void All_WithRemotePropertyPredicate_MatchesMaterialized()
        {
            var expected = _provider.Query<PersonDetailEntity>().ToList().All(p => p.EmployerHeadquartersCountryName != null);
            var actual = _provider.Query<PersonDetailEntity>().All(p => p.EmployerHeadquartersCountryName != null);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Sum_FilteredByRemoteProperty_MatchesMaterialized()
        {
            var expected = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).ToList().Sum(p => p.Id);
            var actual = _provider.Query<PersonDetailEntity>().Where(p => p.EmployerHeadquartersCountryName != null).Sum(p => p.Id);
            Assert.AreEqual(expected, actual);
        }
    }
}
