using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests.NetFramework
{
    /// <summary>
    /// The delete guard's trivial-pattern check on .NET Framework 4.8. Here its <c>Contains(…, OrdinalIgnoreCase)</c>
    /// binds to <c>GeneralExtensions.Contains</c>, because <c>string</c> has no such overload before .NET Core 2.1.
    /// Since 3.10.0 that extension uses the comparison it is given, so the check matches case-insensitively, as on
    /// .NET 8: a WHERE clause containing <c>TRUE</c> in any letter case is rejected (provider-scoped caches plan
    /// §9.13 FVC-1, §9.14 FVD-2). For these plain-member predicates the WHERE clause names the table as well as the
    /// column, so both a PascalCase column <c>TrueUpAmount</c> and a table named <c>zz_guard_TrueUp</c> trip it.
    /// Each row counts the rows through the provider inside its transaction, before the rollback, so a delete that
    /// ran before the guard threw shows.
    /// </summary>
    [TestClass]
    public class DeleteGuardCaseTests
    {
        private const string ColumnTable = "zz_guard_case";
        private const string NamedTable = "zz_guard_TrueUp";
        private const string TrivialMessage = "Delete operation requires a non-trivial WHERE clause.";

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
        public void Delete_WhereClauseNamingTrueInAnyCase_IsRejectedAsTrivial()
        {
            using (var provider = new SqlServerOrmDataProvider(_connectionString))
            {
                provider.BeginTransaction();
                try
                {
                    var exception = Assert.ThrowsException<InvalidOperationException>(
                        () => provider.Delete<GuardCaseRow>(x => x.TrueUpAmount == 5));
                    Assert.AreEqual(TrivialMessage, exception.Message);
                    Assert.AreEqual(2, provider.GetList<GuardCaseRow>().Count, "the rejected delete removed rows");
                }
                finally
                {
                    provider.RollbackTransaction();
                }
            }
        }

        [TestMethod]
        public async Task DeleteAsync_WhereClauseNamingTrueInAnyCase_IsRejectedAsTrivial()
        {
            using (var provider = new SqlServerOrmDataProvider(_connectionString))
            {
                provider.BeginTransaction();
                try
                {
                    var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                        () => provider.DeleteAsync<GuardCaseRow>(x => x.TrueUpAmount == 5));
                    Assert.AreEqual(TrivialMessage, exception.Message);
                    Assert.AreEqual(2, provider.GetList<GuardCaseRow>().Count, "the rejected delete removed rows");
                }
                finally
                {
                    provider.RollbackTransaction();
                }
            }
        }

        [TestMethod]
        public void Delete_OnATableNamedWithTrue_IsRejectedAsTrivial()
        {
            using (var provider = new SqlServerOrmDataProvider(_connectionString))
            {
                provider.BeginTransaction();
                try
                {
                    var exception = Assert.ThrowsException<InvalidOperationException>(
                        () => provider.Delete<GuardNamedTableRow>(x => x.Amount == 5));
                    Assert.AreEqual(TrivialMessage, exception.Message);
                    Assert.AreEqual(2, provider.GetList<GuardNamedTableRow>().Count, "the rejected delete removed rows");
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
