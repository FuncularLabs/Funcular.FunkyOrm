using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Funcular.Data.Orm.MySql;
using Funcular.Data.Orm.Sqlite;
using Funcular.Data.Orm.Sqlite.Visitors;
using Microsoft.Data.Sqlite;

#pragma warning disable CS0618 // The obsolete members are the subject.

namespace Funcular.Data.Orm.SqlServer.Tests.Packaging
{
    /// <summary>
    /// Documentation truthfulness (docs/plans/DECISIONS-3.10.2.md FD-06, backlog WP-E2/E3): no document claims an
    /// effect the code doesn't have. <c>MySqlStringComparison</c> and <c>SqliteStringComparison</c> are accepted (SQLite)
    /// or merely declared (MySQL) and never read, so both are <c>[Obsolete]</c> with a message saying so, the documents
    /// don't show them as constructor options, and the package description names every provider it bundles.
    /// </summary>
    [TestClass]
    public class DocumentationTruthTests
    {
        private static string Root => PackagingTests.RepositoryRoot();

        [DataTestMethod]
        [DataRow(typeof(MySqlStringComparison))]
        [DataRow(typeof(SqliteStringComparison))]
        public void StringComparisonEnums_AreObsolete_AndSayTheyHaveNoEffect(Type enumType)
        {
            var obsolete = enumType.GetCustomAttribute<ObsoleteAttribute>();
            Assert.IsNotNull(obsolete, enumType.Name + " isn't [Obsolete]");
            StringAssert.Contains(obsolete.Message, "no effect", enumType.Name + "'s message doesn't say it has no effect");
        }

        [TestMethod]
        public void SqliteProvider_StringComparisonProperty_IsObsolete()
        {
            var property = typeof(SqliteOrmDataProvider).GetProperty("StringComparison");
            Assert.IsNotNull(property, "SqliteOrmDataProvider.StringComparison is gone; update the test and the Changelog");
            var obsolete = property.GetCustomAttribute<ObsoleteAttribute>();
            Assert.IsNotNull(obsolete, "SqliteOrmDataProvider.StringComparison isn't [Obsolete]");
            StringAssert.Contains(obsolete.Message, "no effect");
        }

        /// <summary>No constructor ever took the MySQL enum, as the 3.6.0 documents claimed; pinned so a future one is a decision.</summary>
        [TestMethod]
        public void MySqlProvider_HasNoConstructorTakingAStringComparison()
        {
            var takers = typeof(MySqlOrmDataProvider).GetConstructors()
                .Where(c => c.GetParameters().Any(p => p.ParameterType == typeof(MySqlStringComparison)))
                .ToList();
            Assert.AreEqual(0, takers.Count, "a MySqlOrmDataProvider constructor takes MySqlStringComparison; implement it or keep the enum obsolete");
        }

        /// <summary>The SQLite translator stores the value and never reads it: both values translate every string method identically.</summary>
        [TestMethod]
        public void SqliteTranslator_TranslatesIdenticallyWhateverTheStringComparison()
        {
            Expression<Func<Row, bool>> contains = x => x.Name.Contains("a");
            Expression<Func<Row, bool>> startsWith = x => x.Name.StartsWith("A");
            Expression<Func<Row, bool>> endsWith = x => x.Name.EndsWith("Z");
            foreach (var predicate in new[] { contains, startsWith, endsWith })
            {
                var call = (MethodCallExpression)predicate.Body;
                Assert.AreEqual(Translate(call, SqliteStringComparison.CaseInsensitive), Translate(call, SqliteStringComparison.CaseSensitive),
                    "the SQLite translator honours SqliteStringComparison for " + call.Method.Name + "; it is documented as having no effect");
                StringAssert.DoesNotMatch(Translate(call, SqliteStringComparison.CaseSensitive), new Regex("COLLATE", RegexOptions.IgnoreCase));
            }
        }

        private static string Translate(MethodCallExpression call, SqliteStringComparison comparison)
        {
            var sql = new StringBuilder();
            var parameters = new List<SqliteParameter>();
            new SqliteExpressionTranslator(new SqliteParameterGenerator(), comparison).TranslateMethodCall(call, sql, parameters, p => "c." + p.Name);
            return sql.ToString();
        }

        public class Row
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        /// <summary>The NuGet listing (the SqlServer csproj's Description) names all four providers and the net48 scope.</summary>
        [TestMethod]
        public void PackageDescription_NamesAllFourProviders_AndSaysNet48CarriesSqlServerOnly()
        {
            var description = XDocument.Load(Path.Combine(Root, "Funcular.Data.Orm.SqlServer/Funcular.Data.Orm.SqlServer.csproj"))
                .Descendants("Description").Select(d => d.Value).FirstOrDefault();
            Assert.IsNotNull(description, "no <Description>");
            foreach (var provider in new[] { "SQL Server", "PostgreSQL", "MySQL", "SQLite" })
                StringAssert.Contains(description, provider, "the description doesn't name " + provider);
            Assert.IsTrue(Regex.IsMatch(description, @"(net48|\.NET Framework 4\.8)[^.]*SQL Server[^.]*only", RegexOptions.IgnoreCase),
                "the description doesn't say that net48 carries SQL Server only: " + description);
        }

        /// <summary>
        /// No document shows the string-comparison enums as a working constructor option, or claims the collation they
        /// were meant to emit. A mention is allowed only where the same line says they have no effect (or are never read).
        /// </summary>
        [DataTestMethod]
        [DataRow("README.md")]
        [DataRow("Usage.md")]
        [DataRow("Advanced.md")]
        [DataRow("Changelog.md")]
        [DataRow("docs/ai-instructions/FUNKYORM_AI_INSTRUCTIONS.md")]
        [DataRow("docs/ai-instructions/FUNKYORM_AI_INSTRUCTIONS_MYSQL.md")]
        [DataRow("docs/ai-instructions/FUNKYORM_AI_INSTRUCTIONS_SQLITE.md")]
        [DataRow("docs/ai-instructions/FUNKYORM_AI_INSTRUCTIONS_POSTGRESQL.md")]
        [DataRow("docs/ai-instructions/FUNKYORM_AI_ADVANCED.md")]
        public void Documents_DontClaimTheStringComparisonSwitchesWork(string relativePath)
        {
            var path = Path.Combine(Root, relativePath);
            if (!File.Exists(path))
                Assert.Inconclusive(relativePath + " doesn't exist");
            var offending = File.ReadAllLines(path)
                .Select((line, index) => new { line, number = index + 1 })
                .Where(l => Regex.IsMatch(l.line, @"(MySql|Sqlite)StringComparison") && !l.line.Contains("no effect") && !l.line.Contains("never read"))
                .ToList();
            Assert.AreEqual(0, offending.Count, relativePath + " mentions a string-comparison switch without saying it has no effect:\n" +
                string.Join("\n", offending.Select(l => l.number + ": " + l.line.Trim())));
        }

        /// <summary>The package's own README says which providers it includes.</summary>
        [TestMethod]
        public void Readme_InstallationSentence_NamesAllFourProviders()
        {
            var readme = File.ReadAllLines(Path.Combine(Root, "README.md"));
            var sentence = readme.FirstOrDefault(l => l.Contains("Install the FunkyORM package"));
            Assert.IsNotNull(sentence, "README's installation sentence is gone");
            foreach (var provider in new[] { "SQL Server", "PostgreSQL", "MySQL", "SQLite" })
                StringAssert.Contains(sentence, provider, "the installation sentence doesn't name " + provider);
        }
    }
}
