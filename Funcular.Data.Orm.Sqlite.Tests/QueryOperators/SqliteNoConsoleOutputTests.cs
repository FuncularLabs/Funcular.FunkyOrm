using System.IO;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Person;

namespace Funcular.Data.Orm.Sqlite.Tests.QueryOperators
{
    /// <summary>
    /// The library writes nothing to the console during a query (docs/plans/DECISIONS-3.10.2.md FD-07): an ordered
    /// query, a predicate query and a paged query run with <see cref="Console.Out"/> captured, and the capture stays
    /// empty. Before 3.11.0 the ORDER BY visitors wrote "Generated ORDER BY clause: …" on every ordered query. The
    /// class isn't parallelized, because the capture swaps the process-wide <see cref="Console.Out"/>.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class SqliteNoConsoleOutputTests : SqliteQueryOperatorTestBase
    {
        [ClassCleanup]
        public static void DeleteDatabase() => DeleteClassDatabase(typeof(SqliteNoConsoleOutputTests));

        [TestMethod]
        public void OrderedPagedAndPredicateQueries_WriteNothingToTheConsole()
        {
            var (marker, ids) = SeedAbc();
            var original = Console.Out;
            string captured;
            using (var writer = new StringWriter())
            {
                Console.SetOut(writer);
                try
                {
                    var ordered = People(marker).OrderBy(p => p.Id).ThenByDescending(p => p.FirstName).ToList();
                    Assert.AreEqual(ids.Count, ordered.Count, "the ordered query returned no rows, so the capture proves nothing");
                    var paged = People(marker).OrderByDescending(p => p.Id).Skip(1).Take(1).ToList();
                    Assert.AreEqual(1, paged.Count);
                    var byPredicate = _provider.Query<PersonEntity>(p => p.LastName == marker && p.Id > 0);
                    Assert.AreEqual(ids.Count, byPredicate.Count);
                    Assert.AreEqual(ids.Count, _provider.Query<PersonEntity>().Count(p => p.LastName == marker));
                }
                finally
                {
                    Console.SetOut(original);
                }
                captured = writer.ToString();
            }
            Assert.AreEqual(string.Empty, captured, "the library wrote to the console during a query");
        }
    }
}
