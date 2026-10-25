using System;
using System.Collections.Generic;
using Funcular.Data.Orm.Tests.NullSemantics;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer.Tests.NullSemantics
{
    /// <summary>The null-semantics rows (3.12.0, docs/plans/NULL_SEMANTICS_PLAN.md) on SQL Server (FUNKY_CONNECTION).</summary>
    [TestClass]
    public class SqlServerNullSemanticsTests : NullSemanticsHarness
    {
        private static string ConnectionString =>
            Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
            "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=funky_db;Integrated Security=True;";

        protected override void RequireDatabase()
        {
            try
            {
                using (var connection = new SqlConnection(ConnectionString))
                    connection.Open();
            }
            catch (Exception ex) when (ex is SqlException || ex is InvalidOperationException)
            {
                Assert.Inconclusive("SQL Server test database not available: " + ex.GetType().Name);
            }
        }

        private static void Execute(string sql)
        {
            using (var connection = new SqlConnection(ConnectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }
            }
        }

        private static string Type(string logical)
        {
            switch (logical)
            {
                case "int": return "INT";
                case "bigint": return "BIGINT";
                case "text": return "NVARCHAR(40)";
                case "bool": return "BIT";
                case "datetime": return "DATETIME2";
                case "decimal": return "DECIMAL(10,2)";
                case "json": return "NVARCHAR(200)";
                default: throw new ArgumentException(logical);
            }
        }

        private static string Drop(string table) => $"IF OBJECT_ID(N'dbo.{table}', N'U') IS NOT NULL DROP TABLE dbo.{table}; ";

        protected override void CreateTables() =>
            Execute(Drop(RowTable) + Drop(ChildTable) + Drop(ParentTable) + SeedSql(Type, v => Literal(v, b => b ? "1" : "0")));

        protected override void DropTables() => Execute(Drop(RowTable) + Drop(ChildTable) + Drop(ParentTable));

        protected override OrmDataProvider CreateProvider() => new SqlServerOrmDataProvider(ConnectionString);

        // T-SQL has no boolean expression type: a bool CASE in a truth position is a syntax error, before and after 3.12.0.
        protected override bool RejectedByTheDialect(string key) =>
            key == "notBoolCond" || key == "boolCondBranches" || key == "boolBranchCapturedLiteral"
            // T-SQL has no NOT on a bit value: `!x.Other` or `!(conditional)` as a CASE result fails, as in 3.11.0 (R6-2);
            // nullEqLiftedCastNotCond's `!x.Other` branch too, since its null test is the conditional's (R11-1).
            || key == "nEq5EqCondNotOther" || key == "nboolCondNotCondBranchNeTrue" || key == "nullEqLiftedCastNotCond"
            // A cast back to bool hid a bool CASE in a truth position; seen through, it is still one (R9-2, R9-4).
            || key == "castCondActiveFalse" || key == "castCondNNe5Active" || key == "castNotCondActiveFalse"
            || key == "castLiftedCondKnownBranch"
            // Two of ReadOnce_CountingGetter's R9-3 rows: a bool CASE as a condition, and a predicate as a CASE result.
            || key == "readsCastCondCaptured" || key == "readsCondTestCastCond"
            // R10-1: the cast's conditional is a bool CASE in a truth position, or a CASE as a condition, as in 3.11.0.
            || key == "castCondCapturedBranch" || key == "notCastCondCapturedBranch" || key == "idGt2AndCastCondCapturedBranch"
            || key == "castCondCapturedBranchAsTest" || key == "castNotCondCapturedBranch" || key == "idGt4AndCastNotCondCapturedBranch";

        /// <summary>The N11 pins: each key's WHERE text and parameters as 3.11.0 (25956e0) sends them, captured in the red run.</summary>
        private static readonly Dictionary<string, string> Sql3110 = new Dictionary<string, string>
        {
            ["capturedNEqV"] = "zz_ns_row.n = @p__linq__0 || @p__linq__0: 5",
            ["codeNeCapturedShort"] = "zz_ns_row.code != @p__linq__0 || @p__linq__0: 5",
            ["colorNeCapturedColor"] = "zz_ns_row.e != @p__linq__0 || @p__linq__0: Green",
            ["condNe"] = "CASE WHEN zz_ns_row.n > @p__linq__0 THEN @p__linq__1 ELSE @p__linq__2 END != @p__linq__3 || @p__linq__0: 3 | @p__linq__1: 1 | @p__linq__2: 0 | @p__linq__3: 1",
            ["doubleNot"] = "NOT NOT zz_ns_row.n = @p__linq__0 || @p__linq__0: 5",
            ["eNeGreenLiteral"] = "zz_ns_row.e != @p__linq__0 || @p__linq__0: Green",
            ["idEq5"] = "zz_ns_row.id = @p__linq__0 || @p__linq__0: 5",
            ["idNe5"] = "zz_ns_row.id != @p__linq__0 || @p__linq__0: 5",
            ["idNeCapturedNullableFive"] = "zz_ns_row.id != @p__linq__0 || @p__linq__0: 5",
            ["idsContainsN"] = "zz_ns_row.n IN (@p__linq__0,@p__linq__1) || @p__linq__0: 1 | @p__linq__1: 2",
            ["longNeCapturedInt"] = "zz_ns_row.long_col != @p__linq__0 || @p__linq__0: 5",
            ["nEq5"] = "zz_ns_row.n = @p__linq__0 || @p__linq__0: 5",
            ["nEqCode"] = "zz_ns_row.n = zz_ns_row.code ||",
            ["nEqNullLiteral"] = "zz_ns_row.n IS NULL ||",
            ["nGt5"] = "zz_ns_row.n > @p__linq__0 || @p__linq__0: 5",
            ["nLongNeCapturedInt"] = "zz_ns_row.n_long != @p__linq__0 || @p__linq__0: 5",
            ["nNeCapturedShort"] = "zz_ns_row.n != @p__linq__0 || @p__linq__0: 5",
            ["nPriceNeCapturedInt"] = "zz_ns_row.n_price != @p__linq__0 || @p__linq__0: 5",
            ["nameContainsA"] = "zz_ns_row.name LIKE @p__linq__0 ESCAPE '!' || @p__linq__0: %a%",
            ["nameEqA"] = "zz_ns_row.name = @p__linq__0 || @p__linq__0: a",
            ["notCodeGt5"] = "NOT zz_ns_row.code > @p__linq__0 || @p__linq__0: 5",
            ["notIdAndCodeEq5"] = "NOT (zz_ns_row.id = @p__linq__0 AND zz_ns_row.code = @p__linq__1) || @p__linq__0: 2 | @p__linq__1: 5",
            ["notIdEq5"] = "NOT zz_ns_row.id = @p__linq__0 || @p__linq__0: 5",
            ["notIdEqCapturedV"] = "NOT zz_ns_row.id = @p__linq__0 || @p__linq__0: 5",
            ["notIntIdsContainsId"] = "NOT zz_ns_row.id IN (@p__linq__0,@p__linq__1) || @p__linq__0: 1 | @p__linq__1: 2",
            ["notNNeV"] = "NOT zz_ns_row.n != @p__linq__0 || @p__linq__0: 5",
            ["priceNeCapturedInt"] = "zz_ns_row.price != @p__linq__0 || @p__linq__0: 5",
        };

        protected override string SqlOf3110(string key) => Sql3110.TryGetValue(key, out var sql) ? sql : null;
    }
}
