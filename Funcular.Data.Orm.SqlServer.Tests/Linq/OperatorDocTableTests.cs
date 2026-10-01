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

            // A list, not a set: an operator listed twice (perhaps with contradicting notes) must fail, and so must a
            // data row that names no operator.
            var documented = new List<string>();
            var pastHeader = false;
            foreach (var line in text.Substring(begin, end - begin).Split('\n'))
            {
                var row = line.Trim();
                if (!row.StartsWith("|", StringComparison.Ordinal))
                    continue;
                if (row.StartsWith("|---", StringComparison.Ordinal))
                {
                    pastHeader = true;
                    continue;
                }
                if (!pastHeader)
                    continue;
                var names = Regex.Matches(row.Split('|')[1], @"`(\w+)`").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
                Assert.IsTrue(names.Count > 0, $"{relativePath}: a table row names no operator: {row}");
                documented.AddRange(names);
            }

            var duplicates = documented.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.AreEqual(0, duplicates.Count, $"{relativePath}: listed more than once: {string.Join(", ", duplicates)}");
            var supported = new SortedSet<string>(QueryOperatorPolicy.SupportedOperators.Select(m => m.Name), StringComparer.Ordinal);
            CollectionAssert.AreEqual(supported.ToList(), documented.OrderBy(n => n, StringComparer.Ordinal).ToList(),
                $"{relativePath}: documented operators differ.\nMissing: {string.Join(", ", supported.Except(documented))}" +
                $"\nExtra: {string.Join(", ", documented.Except(supported))}");
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
