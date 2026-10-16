using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.Visitors;

namespace Funcular.Data.Orm.SqlServer.Tests.Diagnostics
{
    /// <summary>
    /// The library writes nothing to the console (docs/plans/DECISIONS-3.10.2.md FD-07): the four ORDER BY visitors
    /// wrote "Generated ORDER BY clause: …" to <see cref="Console.Out"/> on every read of <c>OrderByClause</c>, which
    /// is a library's caller's stream. DB-free: each visitor is built and read with the console captured. The class
    /// isn't parallelized, because the capture swaps the process-wide <see cref="Console.Out"/>.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class NoConsoleOutputTests
    {
        private static readonly IQueryable<PersonDetailEntity> Source = new List<PersonDetailEntity>().AsQueryable();

        private static Expression Ordering => Source.OrderBy(p => p.Id).ThenByDescending(p => p.FirstName).Expression;

        private static readonly ConcurrentDictionary<string, string> NoColumns = new ConcurrentDictionary<string, string>();
        private static readonly List<PropertyInfo> NoUnmapped = new List<PropertyInfo>();

        [DataTestMethod]
        [DataRow("SqlServer")]
        [DataRow("PostgreSql")]
        [DataRow("MySql")]
        [DataRow("Sqlite")]
        public void OrderByVisitor_WritesNothingToTheConsole(string provider)
        {
            var captured = CaptureConsole(() =>
            {
                string clause;
                switch (provider)
                {
                    case "SqlServer":
                    {
                        var visitor = new OrderByClauseVisitor<PersonDetailEntity>(NoColumns, NoUnmapped, null, "person");
                        visitor.Visit(Ordering);
                        clause = visitor.OrderByClause;
                        break;
                    }
                    case "PostgreSql":
                    {
                        var visitor = new Funcular.Data.Orm.PostgreSql.Visitors.PostgreSqlOrderByClauseVisitor<PersonDetailEntity>(NoColumns, NoUnmapped, null, "person");
                        visitor.Visit(Ordering);
                        clause = visitor.OrderByClause;
                        break;
                    }
                    case "MySql":
                    {
                        var visitor = new Funcular.Data.Orm.MySql.Visitors.MySqlOrderByClauseVisitor<PersonDetailEntity>(NoColumns, NoUnmapped, null, "person");
                        visitor.Visit(Ordering);
                        clause = visitor.OrderByClause;
                        break;
                    }
                    default:
                    {
                        var visitor = new Funcular.Data.Orm.Sqlite.Visitors.SqliteOrderByClauseVisitor<PersonDetailEntity>(NoColumns, NoUnmapped, null, "person");
                        visitor.Visit(Ordering);
                        clause = visitor.OrderByClause;
                        break;
                    }
                }
                StringAssert.StartsWith(clause, "ORDER BY ", provider + ": the visitor produced no clause, so the capture proves nothing");
            });
            Assert.AreEqual(string.Empty, captured, provider + ": the ORDER BY visitor wrote to the console");
        }

        /// <summary>Runs <paramref name="action"/> with <see cref="Console.Out"/> redirected; returns what was written.</summary>
        public static string CaptureConsole(Action action)
        {
            var original = Console.Out;
            using (var writer = new StringWriter())
            {
                Console.SetOut(writer);
                try
                {
                    action();
                }
                finally
                {
                    Console.SetOut(original);
                }
                return writer.ToString();
            }
        }
    }
}
