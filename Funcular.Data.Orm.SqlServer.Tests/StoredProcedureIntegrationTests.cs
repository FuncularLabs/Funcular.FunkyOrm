using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Funcular.Data.Orm;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Person;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.StoredProcedure;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    /// <summary>
    /// Integration tests for SQL Server stored procedure execution (v3.7.0). Runs against the FUNKY_CONNECTION
    /// database (LocalDB / .\SQL2019), whose schema includes the sp_* procedures from Database/integration_test_db.sql.
    /// </summary>
    [TestClass]
    public class StoredProcedureIntegrationTests
    {
        private string _connectionString;
        private SqlServerOrmDataProvider _provider;

        [TestInitialize]
        public void Setup()
        {
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
                "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=funky_db;Integrated Security=True;";
            using (var conn = new SqlConnection(_connectionString))
            {
                try { conn.Open(); }
                catch (SqlException ex) { Assert.Inconclusive($"SQL Server not available.\n{ex.Message}"); }
            }
            _provider = new SqlServerOrmDataProvider(_connectionString);
        }

        [TestCleanup]
        public void Cleanup() => _provider?.Dispose();

        /// <summary>A gender token unique to this test run; fits the gender NVARCHAR(10) column.</summary>
        private static string UniqueGender() => "g" + Guid.NewGuid().ToString("N").Substring(0, 8);

        private int InsertPerson(string first, string last, string gender, DateTime? birthdate = null)
        {
            var person = new Person
            {
                FirstName = first,
                LastName = last,
                Gender = gender,
                Birthdate = birthdate,
                UniqueId = Guid.NewGuid()
            };
            _provider.Insert(person);
            return person.Id;
        }

        // ── Result set ──

        [TestMethod]
        public void ExecProcedure_ResultSet_AnonymousObject()
        {
            var g = UniqueGender();
            var id = InsertPerson("Jane", "Doe", g);

            var results = _provider.ExecProcedure<PersonProcResult>("sp_get_persons_by_gender", new { gender = g });

            Assert.AreEqual(1, results.Count);
            var jane = results.Single();
            Assert.AreEqual(id, jane.Id);
            Assert.AreEqual("Jane", jane.FirstName);   // first_name -> FirstName column mapping
            Assert.AreEqual("Doe", jane.LastName);
        }

        [TestMethod]
        public void ExecProcedure_ResultSet_TypedClass()
        {
            var g = UniqueGender();
            InsertPerson("Tina", "Typed", g);

            var results = _provider.ExecProcedure<PersonProcResult>("sp_get_persons_by_gender", new GenderParam { gender = g });

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Tina", results.Single().FirstName);
        }

        [TestMethod]
        public void ExecProcedure_ResultSet_TupleSyntax()
        {
            var g = UniqueGender();
            InsertPerson("Tilly", "Tuple", g);

            var results = _provider.ExecProcedure<PersonProcResult>("sp_get_persons_by_gender", ("@gender", g));

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Tilly", results.Single().FirstName);
        }

        [TestMethod]
        public void ExecProcedure_ResultSet_SqlParams()
        {
            var g = UniqueGender();
            InsertPerson("Sam", "Param", g);

            var results = _provider.ExecProcedure<PersonProcResult>("sp_get_persons_by_gender", new SqlParam("@gender", g));

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Sam", results.Single().FirstName);
        }

        [TestMethod]
        public void ExecProcedure_NoParams_ReturnsRows()
        {
            var g = UniqueGender();
            InsertPerson("Nora", "NoParams", g);

            // sp_search_persons has all-optional parameters; no args returns all persons.
            var results = _provider.ExecProcedure<PersonProcResult>("sp_search_persons");

            Assert.IsTrue(results.Count >= 1);
        }

        [TestMethod]
        public void ExecProcedure_SingleRow()
        {
            var g = UniqueGender();
            var id = InsertPerson("Solo", "Row", g);

            var results = _provider.ExecProcedure<PersonProcResult>("sp_get_person_by_id", new { person_id = id });

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Solo", results.Single().FirstName);
        }

        [TestMethod]
        public void ExecProcedure_EmptyResultSet()
        {
            var results = _provider.ExecProcedure<PersonProcResult>("sp_get_persons_by_gender", new { gender = UniqueGender() });
            Assert.AreEqual(0, results.Count);
        }

        [TestMethod]
        public void ExecProcedure_ConventionName()
        {
            var g = UniqueGender();
            var id = InsertPerson("Conny", "Vention", g);

            // No procedure name passed; inferred from SpGetPersonById -> sp_get_person_by_id.
            var results = _provider.ExecProcedure<SpGetPersonById>(new { person_id = id });

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Conny", results.Single().FirstName);
        }

        [TestMethod]
        public void ExecProcedure_AttributeName()
        {
            var g = UniqueGender();
            InsertPerson("Atti", "Bute", g);

            // Name comes from [Procedure("sp_get_persons_by_gender")] on GenderFilteredPerson.
            var results = _provider.ExecProcedure<GenderFilteredPerson>(new { gender = g });

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Atti", results.Single().FirstName);
        }

        // ── Scalar ──

        [TestMethod]
        public void ExecScalar_Int()
        {
            var g = UniqueGender();
            InsertPerson("Count", "One", g);
            InsertPerson("Count", "Two", g);

            var count = _provider.ExecScalar<int>("sp_count_persons_by_gender", ("@gender", g));

            Assert.AreEqual(2, count);
        }

        [TestMethod]
        public void ExecScalar_String()
        {
            var g = UniqueGender();
            var id = InsertPerson("Full", "Name", g);

            var fullName = _provider.ExecScalar<string>("sp_get_person_full_name", new { person_id = id });

            Assert.AreEqual("Full Name", fullName);
        }

        [TestMethod]
        public void ExecScalar_NullableInt()
        {
            var g = UniqueGender();
            InsertPerson("Nullable", "Count", g);

            var count = _provider.ExecScalar<int?>("sp_count_persons_by_gender", ("@gender", g));

            Assert.IsTrue(count.HasValue && count.Value == 1);
        }

        // ── Non-query ──

        [TestMethod]
        public void ExecNonQuery_Insert()
        {
            var orgName = "Org-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _provider.BeginTransaction();
            try
            {
                var rows = _provider.ExecNonQuery("sp_insert_organization", new { name = orgName });
                Assert.AreEqual(1, rows);

                var org = _provider.Query<OrganizationEntity>().FirstOrDefault(o => o.Name == orgName);
                Assert.IsNotNull(org);
            }
            finally
            {
                _provider.RollbackTransaction();
            }
        }

        [TestMethod]
        public void ExecNonQuery_Update()
        {
            var g = UniqueGender();
            var newG = UniqueGender();
            var id = InsertPerson("Up", "Date", g);

            var rows = _provider.ExecNonQuery("sp_update_person_gender", new { person_id = id, new_gender = newG });

            // person has an update trigger (dateutc_modified), so DB-reported rows-affected may exceed 1.
            Assert.IsTrue(rows >= 1);
            var fetched = _provider.Get<Person>(id);
            Assert.AreEqual(newG, fetched.Gender);
        }

        [TestMethod]
        public void ExecNonQuery_Noop_DoesNotThrow()
        {
            // SET NOCOUNT ON means ExecuteNonQuery returns -1; must not be treated as an error.
            var rows = _provider.ExecNonQuery("sp_noop");
            Assert.IsTrue(rows <= 0);
        }

        // ── Output parameters ──

        [TestMethod]
        public void ExecProcedure_OutputParam()
        {
            var g = UniqueGender();
            InsertPerson("Out", "Param", g);

            var totalCount = new SqlParam("@total_count", null, ParameterDirection.Output) { DbType = DbType.Int32 };
            var results = _provider.ExecProcedure<PersonProcResult>(
                "sp_get_persons_paged",
                new SqlParam("@page", 1),
                new SqlParam("@page_size", 10),
                totalCount);

            Assert.IsTrue(results.Count <= 10);
            Assert.IsNotNull(totalCount.Value);
            Assert.IsTrue((int)totalCount.Value > 0);
        }

        // ── Transaction ──

        [TestMethod]
        public void ExecProcedure_WithinTransaction()
        {
            var g = UniqueGender();
            _provider.BeginTransaction();
            try
            {
                var id = InsertPerson("Trans", "Action", g);
                var results = _provider.ExecProcedure<PersonProcResult>("sp_get_person_by_id", new { person_id = id });
                Assert.AreEqual(1, results.Count);
                Assert.AreEqual("Trans", results.Single().FirstName);
            }
            finally
            {
                _provider.RollbackTransaction();
            }
        }

        // ── Null parameter ──

        [TestMethod]
        public void ExecProcedure_NullParameter()
        {
            InsertPerson("Null", "Param", UniqueGender());
            // gender null -> the optional filter is skipped; should not error.
            var results = _provider.ExecProcedure<PersonProcResult>("sp_search_persons", new { gender = (string)null });
            Assert.IsTrue(results.Count >= 1);
        }

        // ── Invalid procedure ──

        [TestMethod]
        public void ExecProcedure_InvalidProcName_Throws()
        {
            Assert.ThrowsException<SqlException>(() =>
                _provider.ExecProcedure<PersonProcResult>("sp_does_not_exist_xyz", new { gender = "x" }));
        }

        // ── Async ──

        [TestMethod]
        public async Task ExecProcedureAsync_ResultSet()
        {
            var g = UniqueGender();
            InsertPerson("Async", "Proc", g);

            var results = await _provider.ExecProcedureAsync<PersonProcResult>("sp_get_persons_by_gender", new { gender = g });

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Async", results.Single().FirstName);
        }

        [TestMethod]
        public async Task ExecScalarAsync_Int()
        {
            var g = UniqueGender();
            InsertPerson("Async", "Scalar", g);

            var count = await _provider.ExecScalarAsync<int>("sp_count_persons_by_gender", ("@gender", g));

            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public async Task ExecNonQueryAsync_Update()
        {
            var g = UniqueGender();
            var newG = UniqueGender();
            var id = InsertPerson("Async", "Update", g);

            var rows = await _provider.ExecNonQueryAsync("sp_update_person_gender", new { person_id = id, new_gender = newG });

            // person has an update trigger (dateutc_modified), so DB-reported rows-affected may exceed 1.
            Assert.IsTrue(rows >= 1);
            Assert.AreEqual(newG, _provider.Get<Person>(id).Gender);
        }
    }
}
