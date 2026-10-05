using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Funcular.Data.Orm.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests.Linq
{
    /// <summary>
    /// AC13-9: the operator table in <c>Advanced.md</c> and <c>docs/ai-instructions/FUNKYORM_AI_ADVANCED.md</c> lists
    /// exactly the operators in <see cref="QueryOperatorPolicy.SupportedOperators"/>. The table sits between the
    /// markers below; each row's first cell names one or more operators in backticks.
    /// </summary>
    [TestClass]
    public class OperatorDocTableTests
    {
        private const string BeginMarker = "<!-- funky:supported-operators:begin -->";
        private const string EndMarker = "<!-- funky:supported-operators:end -->";

        [DataTestMethod]
        [DataRow("Advanced.md")]
        [DataRow("docs/ai-instructions/FUNKYORM_AI_ADVANCED.md")]
        public void OperatorDocTable_MatchesSupportedOperators(string relativePath)
        {
            var path = Path.Combine(RepoRoot(), relativePath);
            Assert.IsTrue(File.Exists(path), "doc not found: " + path);
            var text = File.ReadAllText(path);

            var begin = text.IndexOf(BeginMarker, StringComparison.Ordinal);
            var end = text.IndexOf(EndMarker, StringComparison.Ordinal);
            Assert.IsTrue(begin >= 0 && end > begin, $"{relativePath}: operator table markers not found");

            var documented = DocumentedOperators(text.Substring(begin, end - begin), relativePath);
            var supported = new SortedSet<string>(QueryOperatorPolicy.SupportedOperators.Select(m => m.Name), StringComparer.Ordinal);
            CollectionAssert.AreEqual(supported.ToList(), documented.OrderBy(n => n, StringComparer.Ordinal).ToList(),
                $"{relativePath}: documented operators differ.\nMissing: {string.Join(", ", supported.Except(documented))}" +
                $"\nExtra: {string.Join(", ", documented.Except(supported))}");
        }

        private static readonly Regex SeparatorRow = new Regex(@"^\|(\s*:?-+:?\s*\|)+$");

        /// <summary>
        /// The operator names in a table's data rows (first cell, in backticks). Fails on a table without a header
        /// separator row, a data row that names no operator, or an operator listed twice.
        /// </summary>
        internal static List<string> DocumentedOperators(string table, string label)
        {
            var documented = new List<string>();
            var pastHeader = false;
            foreach (var line in table.Split('\n'))
            {
                var row = line.Trim();
                if (!row.StartsWith("|", StringComparison.Ordinal))
                    continue;
                if (SeparatorRow.IsMatch(row))
                {
                    pastHeader = true;
                    continue;
                }
                if (!pastHeader)
                    continue;
                var names = Regex.Matches(row.Split('|')[1], @"`(\w+)`").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
                Assert.IsTrue(names.Count > 0, $"{label}: a table row names no operator: {row}");
                documented.AddRange(names);
            }

            Assert.IsTrue(pastHeader, $"{label}: the operator table has no header separator row");
            var duplicates = documented.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.AreEqual(0, duplicates.Count, $"{label}: listed more than once: {string.Join(", ", duplicates)}");
            return documented;
        }

        [DataTestMethod]
        [DataRow("|---|---|")]
        [DataRow("| --- | --- |")]
        [DataRow("|:---|:---:|")]
        [DataRow("| :- | -: |")]
        public void TableParser_AcceptsEverySeparatorForm(string separator)
        {
            var names = DocumentedOperators($"| Operator | Notes |\n{separator}\n| `Where`, `Take` | n |\n", "sample");

            CollectionAssert.AreEqual(new[] { "Where", "Take" }, names);
        }

        [TestMethod]
        public void TableParser_WithoutSeparator_Fails()
        {
            Assert.ThrowsException<AssertFailedException>(() => DocumentedOperators("| Operator | Notes |\n| `Where` | n |\n", "sample"));
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FunkyORM.sln")))
                dir = dir.Parent;
            Assert.IsNotNull(dir, "repository root (FunkyORM.sln) not found above " + AppContext.BaseDirectory);
            return dir.FullName;
        }
    }
}
