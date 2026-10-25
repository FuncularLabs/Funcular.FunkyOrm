using System;
using System.Collections.Generic;
using Funcular.Data.Orm.Tests.NullSemantics;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Tests.NullSemantics
{
    /// <summary>The null-semantics rows (3.12.0, docs/plans/NULL_SEMANTICS_PLAN.md) on PostgreSQL.</summary>
    [TestClass]
    public class PostgreSqlNullSemanticsTests : NullSemanticsHarness
    {
        private static string ConnectionString => PostgreSqlTestConnection.Resolve();

        protected override void RequireDatabase()
        {
            try
            {
                using (var connection = new NpgsqlConnection(ConnectionString))
                    connection.Open();
            }
            catch (Exception ex) when (ex is NpgsqlException || ex is InvalidOperationException || ex is System.Net.Sockets.SocketException)
            {
                Assert.Inconclusive("PostgreSQL test database not available: " + ex.GetType().Name);
            }
        }

        private static void Execute(string sql)
        {
            using (var connection = new NpgsqlConnection(ConnectionString))
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
                case "text": return "VARCHAR(40)";
                case "bool": return "BOOLEAN";
                case "datetime": return "TIMESTAMP";
                case "decimal": return "NUMERIC(10,2)";
                case "json": return "JSONB";
                default: throw new ArgumentException(logical);
            }
        }

        private const string Drops = "DROP TABLE IF EXISTS " + RowTable + "; DROP TABLE IF EXISTS " + ChildTable + "; DROP TABLE IF EXISTS " + ParentTable + "; ";

        protected override void CreateTables() => Execute(Drops + SeedSql(Type, v => Literal(v, b => b ? "TRUE" : "FALSE")));

        protected override void DropTables() => Execute(Drops);

        protected override OrmDataProvider CreateProvider() => new PostgreSqlOrmDataProvider(ConnectionString);

        // Npgsql can't write a CLR enum as a parameter ("Writing values of '…Color' is not supported"): backlog
        // Appendix A #5, pre-existing and outside this plan. The keys that bind an enum value throw before and after.
        protected override Type RejectedByTheProvider(string key) =>
            key == "eNeRed" || key == "colorNeCapturedColor" || key == "eNeGreenLiteral" ? typeof(InvalidCastException) : null;

        /// <summary>The N11 pins: each key's WHERE text and parameters as 3.11.0 (25956e0) sends them, captured in the red run.</summary>
        private static readonly Dictionary<string, string> Sql3110 = new Dictionary<string, string>
        {
            ["capturedNEqV"] = "zz_ns_row.n = @p__linq__0 || @p__linq__0: 5",
            ["codeNeCapturedShort"] = "zz_ns_row.code != @p__linq__0 || @p__linq__0: 5",
            ["condNe"] = "CASE WHEN zz_ns_row.n > @p__linq__0 THEN @p__linq__1 ELSE @p__linq__2 END != @p__linq__3 || @p__linq__0: 3 | @p__linq__1: 1 | @p__linq__2: 0 | @p__linq__3: 1",
            ["doubleNot"] = "NOT NOT zz_ns_row.n = @p__linq__0 || @p__linq__0: 5",
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
