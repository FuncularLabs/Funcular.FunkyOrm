using System;
using System.Collections.Generic;
using System.IO;
using Funcular.Data.Orm.Tests.NullSemantics;
using Microsoft.Data.Sqlite;

namespace Funcular.Data.Orm.Sqlite.Tests.NullSemantics
{
    /// <summary>The null-semantics rows (3.12.0, docs/plans/NULL_SEMANTICS_PLAN.md) on SQLite, in a file of their own.</summary>
    [TestClass]
    public class SqliteNullSemanticsTests : NullSemanticsHarness
    {
        private string _path;

        private static string Type(string logical)
        {
            switch (logical)
            {
                case "int":
                case "bigint":
                case "bool": return "INTEGER";
                case "text":
                case "datetime":
                case "json": return "TEXT";
                case "decimal": return "NUMERIC";
                default: throw new ArgumentException(logical);
            }
        }

        protected override void CreateTables()
        {
            DropTables();
            _path = Path.Combine(Path.GetTempPath(), "zz_ns_" + Guid.NewGuid().ToString("N") + ".db");
            using (var connection = new SqliteConnection("Data Source=" + _path))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = SeedSql(Type, v => Literal(v, b => b ? "1" : "0"));
                    command.ExecuteNonQuery();
                }
            }
        }

        protected override void DropTables()
        {
            if (_path == null)
                return;
            SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(_path))
                    File.Delete(_path);
            }
            catch (IOException)
            {
                // A pooled handle still holds it; the file is a uniquely named temp file.
            }
            _path = null;
        }

        protected override OrmDataProvider CreateProvider() => new SqliteOrmDataProvider("Data Source=" + _path);

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
