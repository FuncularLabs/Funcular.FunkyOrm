using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Funcular.Data.Orm;
using Funcular.Data.Orm.Attributes;
using Funcular.Data.Orm.MySql.Tests.Domain;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>DTO for sp_get_persons_by_gender / sp_get_person_by_id result sets.</summary>
    public class PersonProcResult
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Gender { get; set; }
        public DateTime? Birthdate { get; set; }
    }

    /// <summary>[Procedure] attribute demo — class name does not match the procedure.</summary>
    [Procedure("sp_get_persons_by_gender")]
    public class GenderFilteredPerson
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Gender { get; set; }
    }

    /// <summary>Convention inference — SpGetPersonById normalizes to sp_get_person_by_id.</summary>
    public class SpGetPersonById
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Gender { get; set; }
    }

    /// <summary>Typed param class; property name matches the procedure parameter (p_gender).</summary>
    public class GenderParam
    {
        public string p_gender { get; set; }
    }

    /// <summary>
    /// Integration tests for MySQL stored procedure execution (v3.7.0). Runs against FUNKY_MYSQL_CONNECTION,
    /// whose schema includes the sp_* procedures from Database/MySql/integration_test_db.sql. MySQL has full
    /// support (parity with SQL Server). Procedure parameters use a p_ prefix.
    /// </summary>
    [TestClass]
    public class MySqlStoredProcedureTests : MySqlTestFixture
    {
        [TestInitialize]
        public void Setup() => InitProvider();

        [TestCleanup]
        public void Cleanup() => DisposeProvider();

        private static string UniqueGender() => "g" + Guid.NewGuid().ToString("N").Substring(0, 8);

        private int InsertPerson(string first, string last, string gender, DateTime? birthdate = null)
        {
            var person = new Person
            {
                FirstName = first,
                LastName = last,
                Gender = gender,
                Birthdate = birthdate,
                UniqueId = Guid.NewGuid(),
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow
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
            var results = _provider.ExecProcedure<PersonProcResult>("sp_get_persons_by_gender", new { p_gender = g });
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(id, results.Single().Id);
            Assert.AreEqual("Jane", results.Single().FirstName);
        }

        [TestMethod]
        public void ExecProcedure_ResultSet_TypedClass()
        {
            var g = UniqueGender();
            InsertPerson("Tina", "Typed", g);
            var results = _provider.ExecProcedure<PersonProcResult>("sp_get_persons_by_gender", new GenderParam { p_gender = g });
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Tina", results.Single().FirstName);
        }

        [TestMethod]
        public void ExecProcedure_ResultSet_TupleSyntax()
        {
            var g = UniqueGender();
            InsertPerson("Tilly", "Tuple", g);
            var results = _provider.ExecProcedure<PersonProcResult>("sp_get_persons_by_gender", ("@p_gender", g));
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Tilly", results.Single().FirstName);
        }

        [TestMethod]
        public void ExecProcedure_ResultSet_SqlParams()
        {
            var g = UniqueGender();
            InsertPerson("Sam", "Param", g);
            var results = _provider.ExecProcedure<PersonProcResult>("sp_get_persons_by_gender", new SqlParam("@p_gender", g));
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Sam", results.Single().FirstName);
        }

        [TestMethod]
        public void ExecProcedure_NoParams_ReturnsRows()
        {
            InsertPerson("Nora", "NoParams", UniqueGender());
            var results = _provider.ExecProcedure<PersonProcResult>("sp_get_all_persons");
            Assert.IsTrue(results.Count >= 1);
        }

        [TestMethod]
        public void ExecProcedure_SingleRow()
        {
            var id = InsertPerson("Solo", "Row", UniqueGender());
            var results = _provider.ExecProcedure<PersonProcResult>("sp_get_person_by_id", new { p_person_id = id });
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Solo", results.Single().FirstName);
        }

        [TestMethod]
        public void ExecProcedure_EmptyResultSet()
        {
            var results = _provider.ExecProcedure<PersonProcResult>("sp_get_persons_by_gender", new { p_gender = UniqueGender() });
            Assert.AreEqual(0, results.Count);
        }

        [TestMethod]
        public void ExecProcedure_ConventionName()
        {
            var id = InsertPerson("Conny", "Vention", UniqueGender());
            var results = _provider.ExecProcedure<SpGetPersonById>(new { p_person_id = id });
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Conny", results.Single().FirstName);
        }

        [TestMethod]
        public void ExecProcedure_AttributeName()
        {
            var g = UniqueGender();
            InsertPerson("Atti", "Bute", g);
            var results = _provider.ExecProcedure<GenderFilteredPerson>(new { p_gender = g });
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
            var count = _provider.ExecScalar<int>("sp_count_persons_by_gender", ("@p_gender", g));
            Assert.AreEqual(2, count);
        }

        [TestMethod]
        public void ExecScalar_String()
        {
            var id = InsertPerson("Full", "Name", UniqueGender());
            var fullName = _provider.ExecScalar<string>("sp_get_person_full_name", new { p_person_id = id });
            Assert.AreEqual("Full Name", fullName);
        }

        [TestMethod]
        public void ExecScalar_NullableInt()
        {
            var g = UniqueGender();
            InsertPerson("Nullable", "Count", g);
            var count = _provider.ExecScalar<int?>("sp_count_persons_by_gender", ("@p_gender", g));
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
                var rows = _provider.ExecNonQuery("sp_insert_organization", new { p_name = orgName, p_headquarters_address_id = (int?)null });
                Assert.AreEqual(1, rows);
                var org = _provider.Query<Organization>().FirstOrDefault(o => o.Name == orgName);
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
            var id = InsertPerson("Up", "Date", UniqueGender());
            var newG = UniqueGender();
            var rows = _provider.ExecNonQuery("sp_update_person_gender", new { p_person_id = id, p_new_gender = newG });
            Assert.AreEqual(1, rows);
            Assert.AreEqual(newG, _provider.Get<Person>(id).Gender);
        }

        [TestMethod]
        public void ExecNonQuery_Noop_DoesNotThrow()
        {
            _provider.ExecNonQuery("sp_noop");
        }

        // ── Output parameter ──

        [TestMethod]
        public void ExecProcedure_OutputParam()
        {
            InsertPerson("Out", "Param", UniqueGender());
            var totalCount = new SqlParam("@p_total_count", null, ParameterDirection.Output) { DbType = DbType.Int32 };
            var results = _provider.ExecProcedure<PersonProcResult>(
                "sp_get_persons_paged",
                new SqlParam("@p_page", 1),
                new SqlParam("@p_page_size", 10),
                totalCount);
            Assert.IsTrue(results.Count <= 10);
            Assert.IsNotNull(totalCount.Value);
            Assert.IsTrue(Convert.ToInt32(totalCount.Value) > 0);
        }

        // ── Transaction ──

        [TestMethod]
        public void ExecProcedure_WithinTransaction()
        {
            _provider.BeginTransaction();
            try
            {
                var id = InsertPerson("Trans", "Action", UniqueGender());
                var results = _provider.ExecProcedure<PersonProcResult>("sp_get_person_by_id", new { p_person_id = id });
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
            var results = _provider.ExecProcedure<PersonProcResult>("sp_search_persons", new
            {
                p_first_name = (string)null,
                p_last_name = (string)null,
                p_gender = (string)null,
                p_min_birthdate = (DateTime?)null,
                p_max_birthdate = (DateTime?)null
            });
            Assert.IsTrue(results.Count >= 1);
        }

        // ── Invalid procedure ──

        [TestMethod]
        public void ExecProcedure_InvalidProcName_Throws()
        {
            Assert.ThrowsException<MySqlException>(() =>
                _provider.ExecProcedure<PersonProcResult>("sp_does_not_exist_xyz", new { p_gender = "x" }));
        }

        // ── Async ──

        [TestMethod]
        public async Task ExecProcedureAsync_ResultSet()
        {
            var g = UniqueGender();
            InsertPerson("Async", "Proc", g);
            var results = await _provider.ExecProcedureAsync<PersonProcResult>("sp_get_persons_by_gender", new { p_gender = g });
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Async", results.Single().FirstName);
        }

        [TestMethod]
        public async Task ExecScalarAsync_Int()
        {
            var g = UniqueGender();
            InsertPerson("Async", "Scalar", g);
            var count = await _provider.ExecScalarAsync<int>("sp_count_persons_by_gender", ("@p_gender", g));
            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public async Task ExecNonQueryAsync_Update()
        {
            var id = InsertPerson("Async", "Update", UniqueGender());
            var newG = UniqueGender();
            var rows = await _provider.ExecNonQueryAsync("sp_update_person_gender", new { p_person_id = id, p_new_gender = newG });
            Assert.AreEqual(1, rows);
            Assert.AreEqual(newG, _provider.Get<Person>(id).Gender);
        }
    }
}
