using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Funcular.Data.Orm;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Objects.Person;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    /// <summary>
    /// Integration tests for PostgreSQL stored procedure execution (v3.7.0). PostgreSQL support is partial:
    /// ExecNonQuery/ExecScalar work via CALL (INOUT parameters returned as the result row); ExecProcedure&lt;T&gt;
    /// throws with guidance because CALL does not return result sets. Procedures come from
    /// Database/PostgreSql/integration_test_db.sql. Runs against FUNKY_PG_CONNECTION or postgres_user/postgres_pwd.
    /// </summary>
    [TestClass]
    public class PostgreSqlStoredProcedureTests : PostgreSqlTestFixture
    {
        private static string UniqueGender() => "g" + Guid.NewGuid().ToString("N").Substring(0, 8);

        [TestMethod]
        public void ExecProcedure_Throws_WithFunctionGuidance()
        {
            var ex = Assert.ThrowsException<NotSupportedException>(() =>
                _provider.ExecProcedure<Person>("sp_noop"));
            StringAssert.Contains(ex.Message, "FUNCTION");
        }

        [TestMethod]
        public void ExecNonQuery_Insert()
        {
            var orgName = "Org-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _provider.BeginTransaction();
            try
            {
                _provider.ExecNonQuery("sp_insert_organization", new { p_name = orgName, p_headquarters_address_id = (int?)null });
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
            var id = InsertTestPerson("Up", "A", "Date", null, g, Guid.NewGuid());

            _provider.ExecNonQuery("sp_update_person_gender", new { p_person_id = id, p_new_gender = newG });

            Assert.AreEqual(newG, _provider.Get<Person>(id).Gender);
        }

        [TestMethod]
        public void ExecScalar_ViaInoutCall()
        {
            var g = UniqueGender();
            InsertTestPerson("C", "A", "One", null, g, Guid.NewGuid());
            InsertTestPerson("C", "A", "Two", null, g, Guid.NewGuid());

            // INOUT p_total is returned as the CALL result row; ExecScalar reads its first column.
            var count = _provider.ExecScalar<int>("sp_count_persons_by_gender", ("@p_gender", g), ("@p_total", 0));

            Assert.AreEqual(2, count);
        }

        [TestMethod]
        public void ExecNonQuery_OutputParam_BackPopulated()
        {
            var g = UniqueGender();
            InsertTestPerson("O", "A", "Param", null, g, Guid.NewGuid());

            var total = new SqlParam("@p_total", 0, ParameterDirection.InputOutput) { DbType = DbType.Int32 };
            _provider.ExecNonQuery("sp_count_persons_by_gender", new SqlParam("@p_gender", g), total);

            Assert.IsNotNull(total.Value);
            Assert.AreEqual(1, Convert.ToInt32(total.Value));
        }

        [TestMethod]
        public async Task ExecScalarAsync_ViaInoutCall()
        {
            var g = UniqueGender();
            InsertTestPerson("AS", "A", "One", null, g, Guid.NewGuid());

            var count = await _provider.ExecScalarAsync<int>("sp_count_persons_by_gender", ("@p_gender", g), ("@p_total", 0));

            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public async Task ExecNonQueryAsync_Update()
        {
            var g = UniqueGender();
            var newG = UniqueGender();
            var id = InsertTestPerson("AU", "A", "Date", null, g, Guid.NewGuid());

            await _provider.ExecNonQueryAsync("sp_update_person_gender", new { p_person_id = id, p_new_gender = newG });

            Assert.AreEqual(newG, _provider.Get<Person>(id).Gender);
        }
    }
}
