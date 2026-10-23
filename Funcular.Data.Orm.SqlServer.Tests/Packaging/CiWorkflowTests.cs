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

        private static readonly string[] TestJobs = { "test-net8", "test-sqlite", "test-postgresql", "test-mysql" };

        /// <summary>
        /// Each test job writes a TRX and runs the gate on it (§8 R3): an inconclusive is NotExecuted in the TRX while
        /// <c>dotnet test</c> exits 0, so a suite whose database was missing would read as green.
        /// </summary>
        [DataTestMethod]
        [DataRow("test-net8")]
        [DataRow("test-sqlite")]
        [DataRow("test-postgresql")]
        [DataRow("test-mysql")]
        public void EveryTestJob_RunsTheTrxGate(string jobName)
        {
            var job = Jobs(CiText)[jobName];
            StringAssert.Contains(job, "--logger \"trx;LogFileName=test-results.trx\"", jobName + " writes no TRX");
            StringAssert.Contains(job, "--results-directory TestResults", jobName + " writes its TRX elsewhere than the gate reads it");
            StringAssert.Contains(job, "./.github/scripts/Assert-TestsRan.ps1 -Trx TestResults/test-results.trx", jobName + " doesn't run the TRX gate");
        }

        /// <summary>
        /// Task 8 (T8B-1): each test job's gate step runs as written, as GitHub's pwsh shell runs it (the step's text in a
        /// file, under <c>$ErrorActionPreference = 'stop'</c>, dot-sourced from the repository root), on a TRX in which every
        /// test passed. A step PowerShell can't parse, such as one with an extra parenthesis, fails its job on every run
        /// whatever the tests do, and <c>publish</c>, which needs every test job, never runs; so does a step without
        /// <c>shell: pwsh</c>, which an ubuntu runner hands to bash (T8C-4).
        /// </summary>
        [DataTestMethod]
        [DataRow("test-net8")]
        [DataRow("test-sqlite")]
        [DataRow("test-postgresql")]
        [DataRow("test-mysql")]
        public void EveryTestJob_GateStepRunsAsWritten(string jobName)
        {
            var lines = Jobs(CiText)[jobName].Split('\n').ToList();
            var name = lines.FindIndex(l => l.Trim() == "- name: Every test ran (the TRX gate)");
            Assert.IsTrue(name >= 0, jobName + " has no gate step");
            var run = lines.FindIndex(name, l => l.Trim() == "run: |");
            Assert.IsTrue(run > name, jobName + "'s gate step has no run block");
            // Task 8 (T8C-4): the step runs under pwsh only because it says so; on an ubuntu runner bash would run it.
            Assert.IsTrue(lines.Skip(name + 1).Take(run - name - 1).Any(l => l.Trim() == "shell: pwsh"),
                jobName + "'s gate step doesn't declare shell: pwsh");
            var indent = lines[run].Length - lines[run].TrimStart().Length;
            var block = lines.Skip(run + 1).TakeWhile(l => l.Trim().Length == 0 || l.Length - l.TrimStart().Length > indent).ToList();
            var margin = block.Where(l => l.Trim().Length > 0).Min(l => l.Length - l.TrimStart().Length);
            var step = string.Join("\n", block.Select(l => l.Length >= margin ? l.Substring(margin) : l.TrimStart()));
            var directory = Path.Combine(Path.GetTempPath(), "funky_gate_step_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(directory, ".github", "scripts"));
                Directory.CreateDirectory(Path.Combine(directory, "TestResults"));
                File.Copy(GateScript, Path.Combine(directory, ".github", "scripts", "Assert-TestsRan.ps1"));
                File.WriteAllText(Path.Combine(directory, "TestResults", "test-results.trx"), Trx(new[] { "Ns.A", "M", "Passed" }));
                File.WriteAllText(Path.Combine(directory, "step.ps1"), "$ErrorActionPreference = 'stop'\n" + step +
                    "\nif ((Test-Path -LiteralPath variable:\\LASTEXITCODE)) { exit $LASTEXITCODE }\n");
                var info = new System.Diagnostics.ProcessStartInfo("pwsh", "-NoProfile -NonInteractive -Command \". './step.ps1'\"")
                {
                    WorkingDirectory = directory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                using (var process = System.Diagnostics.Process.Start(info))
                {
                    var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    Assert.AreEqual(0, process.ExitCode, jobName + "'s gate step fails on a run where every test passed:\n" + step + "\n" + output);
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>No test job can pass while its tests fail or don't run: no continue-on-error, no `|| true`, no job-level if.</summary>
        [TestMethod]
        public void TestJobs_NeverHideAFailure()
        {
            var jobs = Jobs(CiText);
            foreach (var name in TestJobs)
            {
                var job = jobs[name];
                Assert.IsFalse(job.Contains("continue-on-error"), name + " has continue-on-error");
                Assert.IsFalse(job.Contains("|| true"), name + " swallows a failure with || true");
                Assert.IsFalse(Regex.IsMatch(job, @"^    if:", RegexOptions.Multiline), name + " has a job-level if: it could be skipped");
            }
        }

        /// <summary>The one filter a test job may have excludes the performance and Entity Framework comparison suites.</summary>
        [TestMethod]
        public void TestJobs_FilterOnlyThePerformanceAndEntityFrameworkSuites()
        {
            var jobs = Jobs(CiText);
            foreach (var name in TestJobs)
            {
                foreach (Match filter in Regex.Matches(jobs[name], @"--filter\s+""([^""]*)"""))
                    Assert.AreEqual("FullyQualifiedName!~PerformanceTests & FullyQualifiedName!~EntityFramework", filter.Groups[1].Value,
                        name + " filters out other tests");
            }
        }

        #region The TRX gate (.github/scripts/Assert-TestsRan.ps1)

        private static string GateScript => Path.Combine(PackagingTests.RepositoryRoot(), ".github", "scripts", "Assert-TestsRan.ps1");

        /// <summary>A TRX with one result per (class, method, outcome), in MSTest's shape: results joined to definitions by testId.</summary>
        private static string Trx(params string[][] results)
        {
            var definitions = new System.Text.StringBuilder();
            var entries = new System.Text.StringBuilder();
            foreach (var result in results)
            {
                var id = Guid.NewGuid().ToString();
                entries.Append($"<UnitTestResult testId=\"{id}\" testName=\"{result[1]} (\"x\")\" outcome=\"{result[2]}\" />");
                definitions.Append($"<UnitTest id=\"{id}\" name=\"{result[1]} (\"x\")\"><TestMethod className=\"{result[0]}\" name=\"{result[1]}\" /></UnitTest>");
            }
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\">" +
                   "<Results>" + entries.ToString().Replace("(\"x\")", "(&quot;x&quot;)") + "</Results>" +
                   "<TestDefinitions>" + definitions.ToString().Replace("(\"x\")", "(&quot;x&quot;)") + "</TestDefinitions></TestRun>";
        }

        /// <summary>Runs the gate on <paramref name="trx"/> (null: no file) with <paramref name="allow"/>; returns its exit code and output.</summary>
        private static KeyValuePair<int, string> RunGate(string trx, string allow)
        {
            var directory = Path.Combine(Path.GetTempPath(), "funky_trx_gate_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var path = Path.Combine(directory, "test-results.trx");
                if (trx != null)
                    File.WriteAllText(path, trx);
                var info = new System.Diagnostics.ProcessStartInfo("pwsh",
                    $"-NoProfile -NonInteractive -File \"{GateScript}\" -Trx \"{path}\"" + (allow == null ? "" : $" -Allow \"{allow}\""))
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                using (var process = System.Diagnostics.Process.Start(info))
                {
                    var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    return new KeyValuePair<int, string>(process.ExitCode, output);
                }
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        public void TrxGate_PassesARunWhereEveryTestRan()
        {
            var result = RunGate(Trx(new[] { "Ns.A", "M", "Passed" }, new[] { "Ns.A", "N", "Failed" }), null);
            Assert.AreEqual(0, result.Key, result.Value);
        }

        [TestMethod]
        public void TrxGate_PassesAnAllowedInconclusive()
        {
            var result = RunGate(Trx(new[] { "Ns.A", "M", "Passed" }, new[] { "Ns.A", "Skip", "NotExecuted" }), "Ns.Other.X,Ns.A.Skip");
            Assert.AreEqual(0, result.Key, result.Value);
        }

        [TestMethod]
        public void TrxGate_FailsAnInconclusiveNotAllowed()
        {
            var result = RunGate(Trx(new[] { "Ns.A", "M", "Passed" }, new[] { "Ns.A", "Skip", "NotExecuted" }), "Ns.A.M");
            Assert.AreEqual(1, result.Key, result.Value);
            StringAssert.Contains(result.Value, "Ns.A.Skip");
        }

        /// <summary>R3-9 / R4-7: the entry names the runtime class, so one method name declared in two classes is two entries.</summary>
        [TestMethod]
        public void TrxGate_KeysOnTheClass_NotTheMethodAlone()
        {
            var result = RunGate(Trx(new[] { "Ns.A", "Shared", "NotExecuted" }, new[] { "Ns.B", "Shared", "NotExecuted" }), "Ns.A.Shared");
            Assert.AreEqual(1, result.Key, result.Value);
            StringAssert.Contains(result.Value, "Ns.B.Shared");
            Assert.IsFalse(result.Value.Contains("Ns.A.Shared ("), "the allowed class was reported: " + result.Value);
        }

        [TestMethod]
        public void TrxGate_FailsARunWithNoResults_OrNoFile()
        {
            Assert.AreEqual(1, RunGate(Trx(), null).Key, "no results");
            Assert.AreEqual(1, RunGate(null, null).Key, "no file");
        }

        #endregion

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
