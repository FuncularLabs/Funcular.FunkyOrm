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

            var documented = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var line in text.Substring(begin, end - begin).Split('\n'))
            {
                var row = line.Trim();
                if (!row.StartsWith("|", StringComparison.Ordinal) || row.StartsWith("|---", StringComparison.Ordinal))
                    continue;
                var firstCell = row.Split('|')[1];
                foreach (Match name in Regex.Matches(firstCell, @"`(\w+)`"))
                    documented.Add(name.Groups[1].Value);
            }

            var supported = new SortedSet<string>(QueryOperatorPolicy.SupportedOperators.Select(m => m.Name), StringComparer.Ordinal);
            CollectionAssert.AreEqual(supported.ToList(), documented.ToList(),
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
