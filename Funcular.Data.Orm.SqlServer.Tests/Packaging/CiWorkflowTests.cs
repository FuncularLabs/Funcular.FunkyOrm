using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Funcular.Data.Orm.SqlServer.Tests.Packaging
{
    /// <summary>
    /// 3.12.0, R3 (docs/plans/NULL_SEMANTICS_PLAN.md): CI runs all four provider suites, on the branches releases are
    /// made from, and the NuGet publish job waits for every one. Before 3.12.0 the PostgreSQL workflow triggered only on
    /// <c>main</c> and <c>development/**</c> (the repository's branch is <c>master</c>), nothing ran the SQLite suite, and
    /// <c>publish</c> needed only the SQL Server tests, so 3.11.0 was published without a PostgreSQL or SQLite CI run.
    /// These checks read the workflow text; the proof that the jobs pass is the first CI run of the pushed branch.
    /// </summary>
    [TestClass]
    public class CiWorkflowTests
    {
        private static string WorkflowDirectory => Path.Combine(PackagingTests.RepositoryRoot(), ".github", "workflows");

        private static string CiText => File.ReadAllText(Path.Combine(WorkflowDirectory, "ci.yml")).Replace("\r\n", "\n");

        /// <summary>The text of each job under <c>jobs:</c>, keyed by its name (two-space indent; commented lines dropped).</summary>
        private static Dictionary<string, string> Jobs(string workflow)
        {
            var lines = workflow.Split('\n').Where(l => !l.TrimStart().StartsWith("#", StringComparison.Ordinal)).ToList();
            var start = lines.FindIndex(l => Regex.IsMatch(l, @"^jobs:\s*$"));
            Assert.IsTrue(start >= 0, "ci.yml has no jobs: section");
            var jobs = new Dictionary<string, string>();
            string current = null;
            var body = new List<string>();
            foreach (var line in lines.Skip(start + 1))
            {
                var header = Regex.Match(line, @"^  ([A-Za-z0-9_-]+):\s*$");
                if (header.Success)
                {
                    if (current != null)
                        jobs[current] = string.Join("\n", body);
                    current = header.Groups[1].Value;
                    body.Clear();
                }
                else if (Regex.IsMatch(line, @"^\S"))
                    break;
                else if (current != null)
                    body.Add(line);
            }
            if (current != null)
                jobs[current] = string.Join("\n", body);
            return jobs;
        }

        /// <summary>The branches listed under <paramref name="trigger"/> (<c>push</c> or <c>pull_request</c>) in flow form.</summary>
        private static List<string> Branches(string workflow, string trigger)
        {
            var match = Regex.Match(workflow, @"^  " + trigger + @":\s*\n\s+branches:\s*\[([^\]]*)\]", RegexOptions.Multiline);
            Assert.IsTrue(match.Success, "ci.yml has no " + trigger + " branches list");
            return match.Groups[1].Value.Split(',').Select(b => b.Trim().Trim('\'', '"')).Where(b => b.Length > 0).ToList();
        }

        private static List<string> Needs(string job)
        {
            var match = Regex.Match(job, @"^    needs:\s*(\[(?<list>[^\]]*)\]|(?<one>\S+))\s*$", RegexOptions.Multiline);
            if (!match.Success)
                return new List<string>();
            return match.Groups["list"].Success
                ? match.Groups["list"].Value.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList()
                : new List<string> { match.Groups["one"].Value };
        }

        [TestMethod]
        public void Ci_RunsOnTheReleaseBranches()
        {
            var push = Branches(CiText, "push");
            foreach (var branch in new[] { "main", "master", "development/**", "release/**" })
                CollectionAssert.Contains(push, branch, "ci.yml doesn't run on pushes to " + branch);
            CollectionAssert.Contains(Branches(CiText, "pull_request"), "master", "ci.yml doesn't run on pull requests to master");
        }

        [DataTestMethod]
        [DataRow("test-net8", "Funcular.Data.Orm.SqlServer.Tests/Funcular.Data.Orm.SqlServer.Tests.csproj", null)]
        [DataRow("test-sqlite", "Funcular.Data.Orm.Sqlite.Tests/Funcular.Data.Orm.Sqlite.Tests.csproj", null)]
        [DataRow("test-postgresql", "Funcular.Data.Orm.PostgreSql.Tests/Funcular.Data.Orm.PostgreSql.Tests.csproj", "image: postgres:")]
        [DataRow("test-mysql", "Funcular.Data.Orm.MySql.Tests/Funcular.Data.Orm.MySql.Tests.csproj", "image: mysql:")]
        public void Ci_HasAJobForEachProviderSuite(string jobName, string project, string service)
        {
            var jobs = Jobs(CiText);
            Assert.IsTrue(jobs.ContainsKey(jobName), "ci.yml has no " + jobName + " job; jobs: " + string.Join(", ", jobs.Keys));
            var job = jobs[jobName];
            Assert.IsTrue(Regex.IsMatch(job, @"dotnet test\s+" + Regex.Escape(project)), jobName + " doesn't run dotnet test on " + project);
            if (service != null)
                StringAssert.Contains(job, service, jobName + " has no database service (" + service + ")");
        }

        [TestMethod]
        public void Publish_NeedsEveryProviderSuite_AndRunsOnlyOnMaster()
        {
            var jobs = Jobs(CiText);
            Assert.IsTrue(jobs.ContainsKey("publish"), "ci.yml has no publish job");
            var needs = Needs(jobs["publish"]);
            foreach (var suite in new[] { "test-net8", "test-sqlite", "test-postgresql", "test-mysql" })
                CollectionAssert.Contains(needs, suite, "publish doesn't wait for " + suite + "; needs: " + string.Join(", ", needs));
            StringAssert.Contains(jobs["publish"], "if: github.ref == 'refs/heads/master'", "publish isn't limited to master");
        }

        /// <summary>One run per suite: no other workflow runs a test project (the separate PostgreSQL and MySQL workflows moved into ci.yml).</summary>
        [TestMethod]
        public void NoOtherWorkflow_RunsATestSuite()
        {
            var others = Directory.EnumerateFiles(WorkflowDirectory, "*.yml").Concat(Directory.EnumerateFiles(WorkflowDirectory, "*.yaml"))
                .Where(f => !string.Equals(Path.GetFileName(f), "ci.yml", StringComparison.OrdinalIgnoreCase))
                .Where(f => File.ReadAllLines(f).Any(l => !l.TrimStart().StartsWith("#", StringComparison.Ordinal) && l.Contains("dotnet test")))
                .Select(Path.GetFileName)
                .ToList();
            Assert.AreEqual(0, others.Count, "workflows besides ci.yml run test suites (double runs): " + string.Join(", ", others));
        }
    }
}
