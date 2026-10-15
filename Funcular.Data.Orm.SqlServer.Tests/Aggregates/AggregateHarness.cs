using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Funcular.Data.Orm.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.Tests.Aggregates
{
    /// <summary>
    /// Aggregate correctness (3.10.2, FD-04; docs/plans/QUERY_OPERATOR_CORRECTNESS_PLAN.md §10; the 3.10.0 Changelog's
    /// Known issues): <c>Average</c> gives LINQ to objects' value for every numeric type, computed as <c>SUM / COUNT</c>
    /// in C#; an empty set gives <c>Sum</c> 0, a nullable or reference <c>Min</c>/<c>Max</c>/<c>Average</c> null and a
    /// non-nullable one <see cref="InvalidOperationException"/>; <c>Min</c>/<c>Max</c> result types convert after the
    /// round-trip, and the types the database can't order as .NET does are rejected before any command. Every
    /// expectation is LINQ to objects over the same seed. This file is compiled into SqlServer.Tests and linked into
    /// PostgreSql.Tests, MySql.Tests and Sqlite.Tests; one derived [TestClass] per provider supplies the table and
    /// the provider's capabilities.
    /// </summary>
    public abstract class AggregateHarness
    {
        public const string Table = "zz_ag_row";

        public enum AgStatus { Low = 1, Mid = 2, High = 3 }

        [Table(Table)]
        public class AgRow
        {
            [Key, Column("id")] public int Id { get; set; }
            [Column("i32")] public int I32 { get; set; }
            [Column("i32n")] public int? I32N { get; set; }
            [Column("i64")] public long I64 { get; set; }
            [Column("i64n")] public long? I64N { get; set; }
            [Column("f32")] public float F32 { get; set; }
            [Column("f32n")] public float? F32N { get; set; }
            [Column("f64")] public double F64 { get; set; }
            [Column("f64n")] public double? F64N { get; set; }
            // "dec" is a MySQL reserved word (a DECIMAL synonym), hence "amount".
            [Column("amount")] public decimal Dec { get; set; }
            [Column("amountn")] public decimal? DecN { get; set; }
            [Column("posted")] public DateTime Posted { get; set; }
            [Column("postedn")] public DateTime? PostedN { get; set; }
            [Column("name")] public string Name { get; set; }
            [Column("uid")] public Guid Uid { get; set; }
            [Column("stamp")] public DateTimeOffset Stamp { get; set; }
            [Column("span")] public TimeSpan Span { get; set; }
            [Column("status")] public AgStatus Status { get; set; }
        }

        // Guids whose .NET order (Max = G2: the first group wins) differs from SQL Server's (Max = G1: byte groups 10-15 first).
        public static readonly Guid G1 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        public static readonly Guid G2 = Guid.Parse("00000001-0000-0000-0000-000000000001");
        public static readonly Guid G3 = Guid.Parse("00000000-0000-0000-0000-000000000001");

        // Stamps whose order by instant (Max = S2, 09:00Z) differs from their order by local clock (S1, 10:00).
        public static readonly DateTimeOffset S1 = new DateTimeOffset(2020, 1, 1, 10, 0, 0, TimeSpan.FromHours(2));
        public static readonly DateTimeOffset S2 = new DateTimeOffset(2020, 1, 1, 9, 0, 0, TimeSpan.Zero);
        public static readonly DateTimeOffset S3 = new DateTimeOffset(2020, 1, 1, 3, 0, 0, TimeSpan.FromHours(-5));

        /// <summary>{1, 1, 2} integers (an average of 1.333…, which AVG over integers truncates to 1 on SQL Server), {1.5, 1.5, 2.25} reals (1.75 exactly), one row with nulls.</summary>
        public static readonly AgRow[] Rows =
        {
            new AgRow { Id = 1, I32 = 1, I32N = 1, I64 = 1, I64N = 1, F32 = 1.5f, F32N = 1.5f, F64 = 1.5, F64N = 1.5, Dec = 1.50m, DecN = 1.50m,
                Posted = new DateTime(2020, 1, 1), PostedN = new DateTime(2020, 1, 1), Name = "b", Uid = G1, Stamp = S1, Span = TimeSpan.FromHours(1), Status = AgStatus.Low },
            new AgRow { Id = 2, I32 = 1, I32N = null, I64 = 1, I64N = null, F32 = 1.5f, F32N = null, F64 = 1.5, F64N = null, Dec = 1.50m, DecN = null,
                Posted = new DateTime(2021, 6, 15), PostedN = null, Name = "a", Uid = G2, Stamp = S2, Span = TimeSpan.FromHours(2), Status = AgStatus.High },
            new AgRow { Id = 3, I32 = 2, I32N = 2, I64 = 2, I64N = 2, F32 = 2.25f, F32N = 2.25f, F64 = 2.25, F64N = 2.25, Dec = 2.25m, DecN = 2.25m,
                Posted = new DateTime(2019, 3, 3), PostedN = new DateTime(2019, 3, 3), Name = "c", Uid = G3, Stamp = S3, Span = TimeSpan.FromHours(3), Status = AgStatus.Mid },
        };

        private static readonly string[] Columns =
            { "id", "i32", "i32n", "i64", "i64n", "f32", "f32n", "f64", "f64n", "amount", "amountn", "posted", "postedn", "name", "uid", "stamp", "span", "status" };

        #region Provider seam

        /// <summary>Marks the test inconclusive when the provider's test database is unavailable.</summary>
        protected virtual void RequireDatabase() { }

        /// <summary>Runs before the table is created (SQLite creates its temp file here).</summary>
        protected virtual void PrepareDatabase() { }

        /// <summary>An open connection to the test database.</summary>
        protected abstract DbConnection OpenConnection();

        /// <summary>The <c>CREATE TABLE</c> for <c>zz_ag_row</c>, with the columns above in the dialect's types.</summary>
        protected abstract string Ddl { get; }

        /// <summary>Drops the table (SQLite deletes its temp file).</summary>
        protected virtual void DropTable()
        {
            using (var connection = OpenConnection())
                Execute(connection, $"DROP TABLE IF EXISTS {Table}");
        }

        /// <summary>The value to bind for <paramref name="column"/>, in the form the dialect stores.</summary>
        protected virtual object ToDb(string column, object value) => value;

        protected abstract OrmDataProvider CreateProvider();

        /// <summary>Whether the provider stores a <see cref="DateTimeOffset"/> in a type that orders by instant.</summary>
        protected abstract bool DateTimeOffsetOrdersByInstant { get; }

        /// <summary>Whether the database has <c>MIN</c>/<c>MAX</c> over its <see cref="Guid"/> type at all (PostgreSQL has none over <c>uuid</c>: 42883).</summary>
        protected virtual bool GuidAggregateSupportedByDatabase => true;

        /// <summary>Whether the database's <c>Guid</c> ordering is .NET's on this provider's storage (lower-case text on MySQL and SQLite).</summary>
        protected virtual bool GuidDatabaseOrderMatchesLinq => false;

        #endregion

        #region Plumbing

        protected static void Execute(DbConnection connection, string sql, params (string Name, object Value)[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                foreach (var (name, value) in parameters)
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = name;
                    parameter.Value = value ?? DBNull.Value;
                    command.Parameters.Add(parameter);
                }
                command.ExecuteNonQuery();
            }
        }

        private void CreateTable()
        {
            PrepareDatabase();
            using (var connection = OpenConnection())
            {
                Execute(connection, $"DROP TABLE IF EXISTS {Table}");
                Execute(connection, Ddl);
                var names = string.Join(", ", Columns);
                var placeholders = string.Join(", ", Columns.Select(c => "@" + c));
                foreach (var row in Rows)
                {
                    var values = new (string, object)[]
                    {
                        ("@id", row.Id), ("@i32", row.I32), ("@i32n", row.I32N), ("@i64", row.I64), ("@i64n", row.I64N),
                        ("@f32", row.F32), ("@f32n", row.F32N), ("@f64", row.F64), ("@f64n", row.F64N), ("@amount", row.Dec), ("@amountn", row.DecN),
                        ("@posted", ToDb("posted", row.Posted)), ("@postedn", row.PostedN.HasValue ? ToDb("posted", row.PostedN.Value) : null),
                        ("@name", row.Name), ("@uid", ToDb("uid", row.Uid)), ("@stamp", ToDb("stamp", row.Stamp)),
                        ("@span", ToDb("span", row.Span)), ("@status", (int)row.Status),
                    };
                    Execute(connection, $"INSERT INTO {Table} ({names}) VALUES ({placeholders})", values);
                }
            }
        }

        /// <summary>Creates and seeds the table, runs <paramref name="body"/> with a provider whose log is captured, drops the table.</summary>
        private void WithTable(Action<OrmDataProvider, List<string>> body)
        {
            RequireDatabase();
            try
            {
                CreateTable();
                using (var provider = CreateProvider())
                {
                    var log = new List<string>();
                    provider.Log = entry => log.Add(entry);
                    body(provider, log);
                }
            }
            finally
            {
                DropTable();
            }
        }

        private static string Commands(List<string> log) =>
            string.Join(" | ", log.Where(e => e != null && Regex.IsMatch(e, @"^\s*SELECT", RegexOptions.IgnoreCase)).Select(e => Regex.Replace(e, @"\s+", " ")));

        private static void AssertAverageSql(List<string> log, string context)
        {
            var commands = Commands(log);
            Assert.IsTrue(commands.IndexOf("SUM(", StringComparison.OrdinalIgnoreCase) >= 0 && commands.IndexOf("COUNT(", StringComparison.OrdinalIgnoreCase) >= 0,
                context + ": Average must be read as SUM and COUNT: " + commands);
            Assert.IsTrue(commands.IndexOf("AVG(", StringComparison.OrdinalIgnoreCase) < 0, context + ": AVG is still sent: " + commands);
            Assert.IsTrue(commands.IndexOf("ROUND(", StringComparison.OrdinalIgnoreCase) < 0, context + ": ROUND is still sent: " + commands);
        }

        private static void AssertNothingSent(List<string> log, string context) =>
            Assert.AreEqual(string.Empty, Commands(log), context + ": a command was sent");

        private static IQueryable<AgRow> Empty(OrmDataProvider provider) => provider.Query<AgRow>().Where(x => x.Id > 100);

        #endregion

        #region AC-C1: Average is LINQ's value

        [DataTestMethod]
        [DataRow("i32")] [DataRow("i32n")] [DataRow("i64")] [DataRow("i64n")]
        [DataRow("f32")] [DataRow("f32n")] [DataRow("f64")] [DataRow("f64n")]
        [DataRow("dec")] [DataRow("decn")]
        public void Average_IsLinqToObjects(string column)
        {
            WithTable((provider, log) =>
            {
                var q = provider.Query<AgRow>();
                switch (column)
                {
                    case "i32": Assert.AreEqual(Rows.Average(r => r.I32), q.Average(x => x.I32), 1e-12, column); break;
                    case "i32n": Assert.AreEqual(Rows.Average(r => r.I32N).Value, q.Average(x => x.I32N).Value, 1e-12, column); break;
                    case "i64": Assert.AreEqual(Rows.Average(r => r.I64), q.Average(x => x.I64), 1e-12, column); break;
                    case "i64n": Assert.AreEqual(Rows.Average(r => r.I64N).Value, q.Average(x => x.I64N).Value, 1e-12, column); break;
                    case "f32": Assert.AreEqual(Rows.Average(r => r.F32), q.Average(x => x.F32), 1e-6f, column); break;
                    case "f32n": Assert.AreEqual(Rows.Average(r => r.F32N).Value, q.Average(x => x.F32N).Value, 1e-6f, column); break;
                    case "f64": Assert.AreEqual(Rows.Average(r => r.F64), q.Average(x => x.F64), 1e-12, column); break;
                    case "f64n": Assert.AreEqual(Rows.Average(r => r.F64N).Value, q.Average(x => x.F64N).Value, 1e-12, column); break;
                    case "dec": Assert.AreEqual(Rows.Average(r => r.Dec), q.Average(x => x.Dec), column); break;
                    case "decn": Assert.AreEqual(Rows.Average(r => r.DecN).Value, q.Average(x => x.DecN).Value, column); break;
                    default: throw new ArgumentException(column);
                }
                AssertAverageSql(log, column);
            });
        }

        [TestMethod]
        public void Average_OverAFilteredSet_IsLinqToObjects()
        {
            WithTable((provider, log) =>
            {
                var expected = Rows.Where(r => r.Id != 3).Average(r => r.I32);
                Assert.AreEqual(expected, provider.Query<AgRow>().Where(x => x.Id != 3).Average(x => x.I32), 1e-12);
                Assert.AreEqual(1.0, expected, 1e-12, "the filtered set is {1, 1}");
            });
        }

        #endregion

        #region AC-C2: empty and nullable inputs

        [TestMethod]
        public void Sum_OverNoRows_IsZero()
        {
            WithTable((provider, log) =>
            {
                Assert.AreEqual(0, Empty(provider).Sum(x => x.I32));
                Assert.AreEqual(0L, Empty(provider).Sum(x => x.I64));
                Assert.AreEqual(0f, Empty(provider).Sum(x => x.F32));
                Assert.AreEqual(0.0, Empty(provider).Sum(x => x.F64));
                Assert.AreEqual(0m, Empty(provider).Sum(x => x.Dec));
                Assert.AreEqual((int?)0, Empty(provider).Sum(x => x.I32N), "LINQ's Sum over a nullable selector is 0, not null");
                Assert.AreEqual((decimal?)0m, Empty(provider).Sum(x => x.DecN));
            });
        }

        [TestMethod]
        public void Sum_IsLinqToObjects()
        {
            WithTable((provider, log) =>
            {
                var q = provider.Query<AgRow>();
                Assert.AreEqual(Rows.Sum(r => r.I32), q.Sum(x => x.I32));
                Assert.AreEqual(Rows.Sum(r => r.I32N), q.Sum(x => x.I32N));
                Assert.AreEqual(Rows.Sum(r => r.F32), q.Sum(x => x.F32), 1e-6f);
                Assert.AreEqual(Rows.Sum(r => r.Dec), q.Sum(x => x.Dec));
            });
        }

        [TestMethod]
        public void NullableMinMaxAverage_OverNoRows_IsNull()
        {
            WithTable((provider, log) =>
            {
                Assert.IsNull(Empty(provider).Min(x => x.I32N), "Min int?");
                Assert.IsNull(Empty(provider).Max(x => x.I32N), "Max int?");
                Assert.IsNull(Empty(provider).Average(x => x.I32N), "Average int?");
                Assert.IsNull(Empty(provider).Average(x => x.DecN), "Average decimal?");
                Assert.IsNull(Empty(provider).Min(x => x.PostedN), "Min DateTime?");
                Assert.IsNull(Empty(provider).Max(x => x.Name), "Max string");
            });
        }

        [TestMethod]
        public void NullableMinMaxAverage_OverOnlyNulls_IsNull()
        {
            WithTable((provider, log) =>
            {
                // Row 2 holds the nulls.
                var q = provider.Query<AgRow>().Where(x => x.Id == 2);
                Assert.IsNull(q.Min(x => x.I32N), "Min");
                Assert.IsNull(q.Max(x => x.DecN), "Max");
                Assert.IsNull(q.Average(x => x.F64N), "Average");
                Assert.IsNull(q.Min(x => x.PostedN), "Min DateTime?");
            });
        }

        [TestMethod]
        public void NonNullableMinMaxAverage_OverNoRows_Throws()
        {
            WithTable((provider, log) =>
            {
                Assert.ThrowsException<InvalidOperationException>(() => Empty(provider).Min(x => x.I32), "Min");
                Assert.ThrowsException<InvalidOperationException>(() => Empty(provider).Max(x => x.Dec), "Max");
                Assert.ThrowsException<InvalidOperationException>(() => Empty(provider).Average(x => x.I32), "Average");
                Assert.ThrowsException<InvalidOperationException>(() => Empty(provider).Min(x => x.Posted), "Min DateTime");
            });
        }

        #endregion

        #region AC-C3: Min/Max by result type

        [TestMethod]
        public void MinMax_ByType_IsLinqToObjects()
        {
            WithTable((provider, log) =>
            {
                var q = provider.Query<AgRow>();
                Assert.AreEqual(Rows.Min(r => r.I32), q.Min(x => x.I32));
                Assert.AreEqual(Rows.Max(r => r.I32N), q.Max(x => x.I32N));
                Assert.AreEqual(Rows.Max(r => r.I64), q.Max(x => x.I64));
                Assert.AreEqual(Rows.Min(r => r.F32), q.Min(x => x.F32), 1e-6f);
                Assert.AreEqual(Rows.Max(r => r.F64), q.Max(x => x.F64), 1e-12);
                Assert.AreEqual(Rows.Max(r => r.Dec), q.Max(x => x.Dec));
                Assert.AreEqual(Rows.Min(r => r.DecN), q.Min(x => x.DecN));
                Assert.AreEqual(Rows.Max(r => r.Posted), q.Max(x => x.Posted));
                Assert.AreEqual(Rows.Min(r => r.PostedN), q.Min(x => x.PostedN));
                // Collation-neutral: distinct lower-case ASCII letters order the same everywhere.
                Assert.AreEqual(Rows.Min(r => r.Name), q.Min(x => x.Name));
                Assert.AreEqual(Rows.Max(r => r.Name), q.Max(x => x.Name));
            });
        }

        [TestMethod]
        public void MinMax_Enum_IsRejectedBeforeAnyCommand()
        {
            WithTable((provider, log) =>
            {
                log.Clear();
                var ex = Assert.ThrowsException<NotSupportedException>(() => provider.Query<AgRow>().Max(x => x.Status));
                StringAssert.Contains(ex.Message, "(int)x.Status", "the message suggests aggregating the underlying number");
                AssertNothingSent(log, "Max enum");
                Assert.ThrowsException<NotSupportedException>(() => provider.Query<AgRow>().Min(x => x.Status));
            });
        }

        [TestMethod]
        public void MinMax_EnumCastToInt_Works()
        {
            WithTable((provider, log) =>
            {
                Assert.AreEqual((int)AgStatus.High, provider.Query<AgRow>().Max(x => (int)x.Status));
                Assert.AreEqual((int)AgStatus.Low, provider.Query<AgRow>().Min(x => (int)x.Status));
            });
        }

        [TestMethod]
        public void MinMax_TimeSpan_IsRejectedBeforeAnyCommand()
        {
            WithTable((provider, log) =>
            {
                log.Clear();
                var ex = Assert.ThrowsException<NotSupportedException>(() => provider.Query<AgRow>().Max(x => x.Span));
                StringAssert.Contains(ex.Message, "TimeSpan");
                StringAssert.Contains(ex.Message, "integer");
                AssertNothingSent(log, "Max TimeSpan");
            });
        }

        /// <summary>
        /// The owner's default (operator plan §10): rejected everywhere. PostgreSQL was to default to the database's
        /// ordering if its <c>uuid</c> MIN/MAX matched .NET's; it has no MIN/MAX over <c>uuid</c> at all (42883), so the
        /// prerequisite failed and it rejects with a message naming the alternative (D-122).
        /// </summary>
        [TestMethod]
        public void MinMax_Guid_IsRejectedByDefault()
        {
            WithTable((provider, log) =>
            {
                Assert.AreEqual(GuidOrdering.Rejected, provider.QueryOptions.GuidOrdering, "the provider's default");
                log.Clear();
                var ex = Assert.ThrowsException<NotSupportedException>(() => provider.Query<AgRow>().Max(x => x.Uid));
                StringAssert.Contains(ex.Message, GuidAggregateSupportedByDatabase ? "GuidOrdering.Database" : "no MIN/MAX over its uuid type");
                AssertNothingSent(log, "Max Guid");
                Assert.ThrowsException<NotSupportedException>(() => provider.Query<AgRow>().Min(x => x.Uid));
            });
        }

        [TestMethod]
        public void MinMax_Guid_OptIn_UsesTheDatabasesOrdering()
        {
            WithTable((provider, log) =>
            {
                provider.QueryOptions.GuidOrdering = GuidOrdering.Database;
                if (!GuidAggregateSupportedByDatabase)
                {
                    // The option can't make PostgreSQL grow a MIN(uuid): still rejected, before any command, naming the alternative.
                    log.Clear();
                    var ex = Assert.ThrowsException<NotSupportedException>(() => provider.Query<AgRow>().Max(x => x.Uid));
                    StringAssert.Contains(ex.Message, "OrderBy");
                    AssertNothingSent(log, "Max Guid with the opt-in");
                    // The alternative the message names works and gives .NET's answer on uuid: the ordered query's first row's
                    // value through a scalar Select (outermost, as the scalar-Select rule requires), reading only that column.
                    Assert.AreEqual(Rows.Max(r => r.Uid), provider.Query<AgRow>().OrderByDescending(x => x.Uid).Take(1).Select(x => x.Uid).ToList().Single());
                    Assert.AreEqual(Rows.Min(r => r.Uid), provider.Query<AgRow>().OrderBy(x => x.Uid).Take(1).Select(x => x.Uid).ToList().Single());
                    return;
                }
                var max = provider.Query<AgRow>().Max(x => x.Uid);
                CollectionAssert.Contains(Rows.Select(r => r.Uid).ToList(), max, "a seeded Guid comes back as a Guid");
                if (GuidDatabaseOrderMatchesLinq)
                    Assert.AreEqual(Rows.Max(r => r.Uid), max);
                else
                    Assert.AreEqual(G1, max, "the database's ordering (byte groups 10-15 first on SQL Server) picks G1, where .NET picks G2");
            });
        }

        [TestMethod]
        public void MinMax_DateTimeOffset_ByInstantOrRejected()
        {
            WithTable((provider, log) =>
            {
                if (DateTimeOffsetOrdersByInstant)
                {
                    Assert.AreEqual(Rows.Max(r => r.Stamp), provider.Query<AgRow>().Max(x => x.Stamp), "Max by instant (S2, 09:00Z), not by local clock (S1)");
                    Assert.AreEqual(Rows.Min(r => r.Stamp), provider.Query<AgRow>().Min(x => x.Stamp));
                    return;
                }
                log.Clear();
                var ex = Assert.ThrowsException<NotSupportedException>(() => provider.Query<AgRow>().Max(x => x.Stamp));
                StringAssert.Contains(ex.Message, "UTC DateTime");
                AssertNothingSent(log, "Max DateTimeOffset");
            });
        }

        #endregion
    }
}
