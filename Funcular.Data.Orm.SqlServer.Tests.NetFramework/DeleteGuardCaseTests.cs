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
    /// §9.13, FVC-1). The table has a PascalCase column, so the WHERE clause names <c>TrueUpAmount</c>.
    /// </summary>
    [TestClass]
    public class DeleteGuardCaseTests
    {
        private const string TableName = "zz_guard_case";
        private const string TrivialMessage = "Delete operation requires a non-trivial WHERE clause.";

        [Table(TableName)]
        public class GuardCaseRow
        {
            [Key] public int Id { get; set; }
            public int TrueUpAmount { get; set; }
        }

        private string _connectionString;

        [TestInitialize]
        public void Setup()
        {
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
                                "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=funky_db;Integrated Security=True;";
            Execute($"DROP TABLE IF EXISTS dbo.{TableName}; " +
                    $"CREATE TABLE dbo.{TableName} (Id INT PRIMARY KEY, TrueUpAmount INT NOT NULL); " +
                    $"INSERT INTO dbo.{TableName} (Id, TrueUpAmount) VALUES (1, 5), (2, 7);");
        }

        [TestCleanup]
        public void Cleanup()
        {
            Execute($"DROP TABLE IF EXISTS dbo.{TableName};");
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
                }
                finally
                {
                    provider.RollbackTransaction();
                }
            }
            Assert.AreEqual(2, CountRows(), "the rejected delete removed rows");
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
                }
                finally
                {
                    provider.RollbackTransaction();
                }
            }
            Assert.AreEqual(2, CountRows(), "the rejected delete removed rows");
        }

        private int CountRows()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand($"SELECT COUNT(*) FROM dbo.{TableName};", connection))
                    return (int)command.ExecuteScalar();
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
