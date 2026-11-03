using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Funcular.Data.Orm.SqlServer.Tests.Packaging
{
    /// <summary>
    /// DB-free pins for the one consolidated Known-issues list (docs/plans/RELEASE_3.14.0_PLAN.md, stream K): the file
    /// says which release it describes and that release is the package's, so a release cannot ship without touching
    /// it; and every older per-release Known issues section of the Changelog points at it.
    /// </summary>
    [TestClass]
    public class KnownIssuesTests
    {
        private static string Root => PackagingTests.RepositoryRoot();

        [TestMethod]
        public void KnownIssues_AsOfVersion_MatchesThePackageVersion()
        {
            var text = File.ReadAllText(Path.Combine(Root, "docs", "KNOWN_ISSUES.md"));
            var heading = Regex.Match(text, @"^# Known issues as of (\d+\.\d+\.\d+)\s*$", RegexOptions.Multiline);
            Assert.IsTrue(heading.Success, "docs/KNOWN_ISSUES.md must start with '# Known issues as of x.y.z'");

            var csproj = XDocument.Load(Path.Combine(Root, "Funcular.Data.Orm.SqlServer", "Funcular.Data.Orm.SqlServer.csproj"));
            var version = csproj.Descendants("Version").Single().Value.Trim();
            Assert.AreEqual(version, heading.Groups[1].Value,
                "the Known-issues list must be revised (or re-dated) for the release the package carries");
        }

        [TestMethod]
        public void OlderKnownIssuesSections_PointAtTheConsolidatedList()
        {
            var lines = File.ReadAllLines(Path.Combine(Root, "Changelog.md"));
            var release = new Regex(@"^## \[(\d+)\.(\d+)\.(\d+)[^\]]*\]");
            var consolidatedAt = new Version(3, 14, 0);
            Version current = null;
            var sectionsChecked = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                var r = release.Match(lines[i]);
                if (r.Success)
                {
                    current = new Version(int.Parse(r.Groups[1].Value), int.Parse(r.Groups[2].Value), int.Parse(r.Groups[3].Value));
                    continue;
                }
                if (current == null || current >= consolidatedAt || !lines[i].StartsWith("### Known issues", StringComparison.Ordinal))
                    continue;
                if (current < new Version(3, 10, 0))
                    continue; // the consolidation starts at 3.10.0, where the per-release sections began
                sectionsChecked++;
                var window = string.Join("\n", lines.Skip(i + 1).Take(3));
                StringAssert.Contains(window, "docs/KNOWN_ISSUES.md",
                    $"the [{current}] Known issues section must point at the consolidated list directly under its heading");
            }
            Assert.AreEqual(5, sectionsChecked, "the per-release Known issues sections of 3.10.0, 3.10.1, 3.11.0, 3.12.0 and 3.12.1");
        }
    }
}
