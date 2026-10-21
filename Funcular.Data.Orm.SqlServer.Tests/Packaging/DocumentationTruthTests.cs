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

        /// <summary>
        /// 3.12.0, R2 (docs/plans/NULL_SEMANTICS_PLAN.md): every FunkyORM attribute shown in a document compiles as far
        /// as names go. A <c>name:</c> argument is a parameter of one of the attribute's constructors (with the
        /// positional arguments and the parameters left out fitting that constructor), and a <c>Name =</c> assignment is
        /// a public settable property. The <c>[SubqueryAggregate]</c> conditional-count examples used
        /// <c>conditionColumn:</c> and <c>conditionValue:</c>, which no constructor has (CS1739); they are properties.
        /// Blocks with an ellipsis (<c>...</c>) are prose placeholders and are skipped.
        /// </summary>
        [TestMethod]
        public void AttributeExamples_NameRealParametersAndProperties()
        {
            var problems = new List<string>();
            var checkedBlocks = 0;
            foreach (var relativePath in DocumentsWithExamples())
            {
                var text = File.ReadAllText(Path.Combine(Root, relativePath));
                foreach (Match match in AttributeUsage.Matches(text))
                {
                    var attributeType = typeof(Funcular.Data.Orm.Attributes.SubqueryAggregateAttribute).Assembly
                        .GetType("Funcular.Data.Orm.Attributes." + match.Groups["name"].Value + "Attribute", throwOnError: true);
                    var open = match.Index + match.Length;
                    var close = FindClosingParenthesis(text, open);
                    if (close < 0)
                        continue;
                    var body = text.Substring(open, close - open);
                    if (body.Contains("...") || body.Contains("\u2026"))
                        continue;
                    checkedBlocks++;
                    var where = relativePath + ":" + (text.Take(match.Index).Count(c => c == '\n') + 1) + " [" + match.Groups["name"].Value + "(" +
                                Regex.Replace(body, @"\s+", " ").Trim() + ")]";
                    var problem = CheckAttributeArguments(attributeType, SplitTopLevel(body));
                    if (problem != null)
                        problems.Add(where + ": " + problem);
                }
            }
            Assert.IsTrue(checkedBlocks > 20, "only " + checkedBlocks + " attribute examples found; the scan is broken");
            Assert.AreEqual(0, problems.Count, "attribute examples that don't compile:\n" + string.Join("\n", problems));
        }

        private static readonly Regex AttributeUsage = new Regex(
            @"\[\s*(?<name>RemoteLink|RemoteProperty|RemoteKey|SubqueryAggregate|SqlExpression|JsonPath|JsonCollection|Procedure)(?:Attribute)?\s*\(");

        /// <summary>The user-facing Markdown in the repository: everything tracked except plans, GitHub prompts and agent notes.</summary>
        private static IEnumerable<string> DocumentsWithExamples() =>
            Directory.EnumerateFiles(Root, "*.md", SearchOption.AllDirectories)
                .Select(p => p.Substring(Root.Length).TrimStart('\\', '/').Replace('\\', '/'))
                .Where(p => !Regex.IsMatch(p, @"(^|/)(docs/plans|\.github|\.claude|\.work|_agent|bin|obj|node_modules|TestResults)/"))
                .OrderBy(p => p, StringComparer.Ordinal);

        /// <summary>The index of the ')' that closes the argument list starting at <paramref name="start"/>, skipping strings; -1 if none.</summary>
        private static int FindClosingParenthesis(string text, int start)
        {
            var depth = 0;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '"')
                {
                    i++;
                    while (i < text.Length && text[i] != '"')
                        i += text[i] == '\\' ? 2 : 1;
                }
                else if (c == '(')
                    depth++;
                else if (c == ')')
                {
                    if (depth == 0)
                        return i;
                    depth--;
                }
            }
            return -1;
        }

        /// <summary>The arguments of an attribute's argument list, split on the commas outside parentheses and strings.</summary>
        private static List<string> SplitTopLevel(string body)
        {
            var parts = new List<string>();
            var depth = 0;
            var current = new StringBuilder();
            for (var i = 0; i < body.Length; i++)
            {
                var c = body[i];
                if (c == '"')
                {
                    var end = i + 1;
                    while (end < body.Length && body[end] != '"')
                        end += body[end] == '\\' ? 2 : 1;
                    current.Append(body, i, Math.Min(end, body.Length - 1) - i + 1);
                    i = end;
                    continue;
                }
                if (c == '(' || c == '<' || c == '{' || c == '[')
                    depth++;
                else if (c == ')' || c == '>' || c == '}' || c == ']')
                    depth--;
                if (c == ',' && depth == 0)
                {
                    parts.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                    current.Append(c);
            }
            if (current.ToString().Trim().Length > 0)
                parts.Add(current.ToString().Trim());
            return parts;
        }

        /// <summary>Null when some constructor of <paramref name="attributeType"/> accepts these arguments, else what is wrong.</summary>
        private static string CheckAttributeArguments(Type attributeType, List<string> arguments)
        {
            var positional = 0;
            var named = new List<string>();
            foreach (var argument in arguments)
            {
                var namedArgument = Regex.Match(argument, @"^([A-Za-z_]\w*)\s*:(?!:)");
                var assignment = Regex.Match(argument, @"^([A-Za-z_]\w*)\s*=(?!=)");
                if (namedArgument.Success)
                    named.Add(namedArgument.Groups[1].Value);
                else if (assignment.Success)
                {
                    var property = attributeType.GetProperty(assignment.Groups[1].Value);
                    if (property == null || property.SetMethod == null || !property.SetMethod.IsPublic)
                        return "'" + assignment.Groups[1].Value + " =' isn't a settable property of " + attributeType.Name;
                }
                else if (named.Count > 0)
                    return "a positional argument after a named one";
                else
                    positional++;
            }

            foreach (var constructor in attributeType.GetConstructors())
            {
                var parameters = constructor.GetParameters();
                var isParams = parameters.Length > 0 && parameters[parameters.Length - 1].IsDefined(typeof(ParamArrayAttribute), false);
                if (positional > parameters.Length && !isParams)
                    continue;
                var supplied = new HashSet<string>(parameters.Take(Math.Min(positional, parameters.Length)).Select(p => p.Name));
                if (named.Any(n => !parameters.Any(p => p.Name == n) || supplied.Contains(n)))
                    continue;
                supplied.UnionWith(named);
                if (parameters.All(p => supplied.Contains(p.Name) || p.HasDefaultValue || p.IsDefined(typeof(ParamArrayAttribute), false)))
                    return null;
            }
            return named.Count > 0
                ? "no constructor of " + attributeType.Name + " has the parameter(s) " + string.Join(", ", named.Select(n => n + ":")) +
                  " with " + positional + " positional argument(s); constructors: " +
                  string.Join(" | ", attributeType.GetConstructors().Select(c => "(" + string.Join(", ", c.GetParameters().Select(p => p.Name)) + ")"))
                : "no constructor of " + attributeType.Name + " takes " + positional + " positional argument(s)";
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
