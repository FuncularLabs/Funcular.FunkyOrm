using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests.NetFramework
{
    /// <summary>
    /// The delete guard on .NET Framework 4.8 accepts a predicate whose WHERE clause names a column or a table containing
    /// <c>true</c> in any letter case: a PascalCase column <c>TrueUpAmount</c>, and a table named <c>zz_guard_TrueUp</c>
    /// (docs/plans/DELETE_GUARD_PLAN.md, AC3 and D8). Each row deletes exactly row 1 inside its transaction, checks the
    /// surviving ids through the provider, and rolls back.
    /// </summary>
    [TestClass]
    public class DeleteGuardCaseTests
    {
        private const string ColumnTable = "zz_guard_case";
        private const string NamedTable = "zz_guard_TrueUp";

        [Table(ColumnTable)]
        public class GuardCaseRow
        {
            [Key] public int Id { get; set; }
            public int TrueUpAmount { get; set; }
        }

        [Table(NamedTable)]
        public class GuardNamedTableRow
        {
            [Key] public int Id { get; set; }
            public int Amount { get; set; }
        }

        private string _connectionString;

        [TestInitialize]
        public void Setup()
        {
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
                                "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=funky_db;Integrated Security=True;";
            Execute($"DROP TABLE IF EXISTS dbo.{ColumnTable}; " +
                    $"CREATE TABLE dbo.{ColumnTable} (Id INT PRIMARY KEY, TrueUpAmount INT NOT NULL); " +
                    $"INSERT INTO dbo.{ColumnTable} (Id, TrueUpAmount) VALUES (1, 5), (2, 7); " +
                    $"DROP TABLE IF EXISTS dbo.{NamedTable}; " +
                    $"CREATE TABLE dbo.{NamedTable} (Id INT PRIMARY KEY, Amount INT NOT NULL); " +
                    $"INSERT INTO dbo.{NamedTable} (Id, Amount) VALUES (1, 5), (2, 7);");
        }

        [TestCleanup]
        public void Cleanup()
        {
            Execute($"DROP TABLE IF EXISTS dbo.{ColumnTable}; DROP TABLE IF EXISTS dbo.{NamedTable};");
        }

        [TestMethod]
        public void Delete_WhereClauseNamingTrueInAnyCase_DeletesTheMatchingRow()
        {
            using (var provider = new SqlServerOrmDataProvider(_connectionString))
            {
                provider.BeginTransaction();
                try
                {
                    Assert.AreEqual(1, provider.Delete<GuardCaseRow>(x => x.TrueUpAmount == 5));
                    CollectionAssert.AreEqual(new[] { 2 }, provider.GetList<GuardCaseRow>().Select(r => r.Id).ToArray());
                }
                finally
                {
                    provider.RollbackTransaction();
                }
            }
        }

        [TestMethod]
        public async Task DeleteAsync_WhereClauseNamingTrueInAnyCase_DeletesTheMatchingRow()
        {
            using (var provider = new SqlServerOrmDataProvider(_connectionString))
            {
                provider.BeginTransaction();
                try
                {
                    Assert.AreEqual(1, await provider.DeleteAsync<GuardCaseRow>(x => x.TrueUpAmount == 5));
                    CollectionAssert.AreEqual(new[] { 2 }, provider.GetList<GuardCaseRow>().Select(r => r.Id).ToArray());
                }
                finally
                {
                    provider.RollbackTransaction();
                }
            }
        }

        [TestMethod]
        public void Delete_OnATableNamedWithTrue_DeletesTheMatchingRow()
        {
            using (var provider = new SqlServerOrmDataProvider(_connectionString))
            {
                provider.BeginTransaction();
                try
                {
                    Assert.AreEqual(1, provider.Delete<GuardNamedTableRow>(x => x.Amount == 5));
                    CollectionAssert.AreEqual(new[] { 2 }, provider.GetList<GuardNamedTableRow>().Select(r => r.Id).ToArray());
                }
                finally
                {
                    provider.RollbackTransaction();
                }
            }
        }

        private void Execute(string sql)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand(sql, connection))
                    command.ExecuteNonQuery();
            }
        }
    }
}
