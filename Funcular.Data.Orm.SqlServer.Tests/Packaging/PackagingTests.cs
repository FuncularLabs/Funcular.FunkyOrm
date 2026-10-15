using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Funcular.Data.Orm.SqlServer.Tests.Packaging
{
    /// <summary>
    /// DB-free checks of what the NuGet package declares (docs/plans/DECISIONS-3.10.2.md FD-05, backlog WP-E1): the
    /// four driver references are pinned to stated minimums (no floating versions), and the package project declares
    /// every driver the bundled providers need, so a consumer that installs <c>Funcular.Data.Orm</c> alone gets them.
    /// The consumer smoke check that installs the packed package is <c>tools/consumer-smoke/Run-ConsumerSmoke.ps1</c>.
    /// </summary>
    [TestClass]
    public class PackagingTests
    {
        private static readonly string[] Drivers = { "Microsoft.Data.SqlClient", "Npgsql", "MySqlConnector", "Microsoft.Data.Sqlite" };

        private static readonly string[] ProviderProjects =
        {
            "Funcular.Data.Orm.Core/Funcular.Data.Orm.Core.csproj",
            "Funcular.Data.Orm.SqlServer/Funcular.Data.Orm.SqlServer.csproj",
            "Funcular.Data.Orm.PostgreSql/Funcular.Data.Orm.PostgreSql.csproj",
            "Funcular.Data.Orm.MySql/Funcular.Data.Orm.MySql.csproj",
            "Funcular.Data.Orm.Sqlite/Funcular.Data.Orm.Sqlite.csproj",
        };

        /// <summary>The repository root: the directory holding <c>FunkyORM.sln</c>, found from the test assembly's directory.</summary>
        public static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "FunkyORM.sln")))
                directory = directory.Parent;
            Assert.IsNotNull(directory, "FunkyORM.sln not found above " + AppContext.BaseDirectory);
            return directory.FullName;
        }

        private static IEnumerable<XElement> PackageReferences(string project) =>
            XDocument.Load(Path.Combine(RepositoryRoot(), project)).Descendants("PackageReference");

        [TestMethod]
        public void DriverReferences_ArePinnedToAStatedMinimum_NoFloatingVersions()
        {
            var floating = new List<string>();
            foreach (var project in ProviderProjects)
            {
                foreach (var reference in PackageReferences(project))
                {
                    var version = (string)reference.Attribute("Version") ?? (string)reference.Element("Version");
                    if (string.IsNullOrEmpty(version) || version.Contains("*") || version.StartsWith("[") || version.StartsWith("("))
                        floating.Add($"{project}: {reference.Attribute("Include")?.Value} Version=\"{version}\"");
                }
            }
            Assert.AreEqual(0, floating.Count, "Floating or ranged versions:\n" + string.Join("\n", floating));
        }

        /// <summary>
        /// The package (<c>Funcular.Data.Orm</c>, built from the SqlServer project) bundles the PostgreSQL, MySQL and SQLite
        /// providers for net8.0 and netstandard2.0, so its nuspec must declare their drivers for those frameworks, at the
        /// same minimum the provider project compiles against; net48 carries SQL Server only.
        /// </summary>
        [DataTestMethod]
        [DataRow("Npgsql", "Funcular.Data.Orm.PostgreSql/Funcular.Data.Orm.PostgreSql.csproj")]
        [DataRow("MySqlConnector", "Funcular.Data.Orm.MySql/Funcular.Data.Orm.MySql.csproj")]
        [DataRow("Microsoft.Data.Sqlite", "Funcular.Data.Orm.Sqlite/Funcular.Data.Orm.Sqlite.csproj")]
        public void Package_DeclaresEachBundledDriver_AtTheProviderProjectsMinimum(string driver, string providerProject)
        {
            var declared = PackageReferences("Funcular.Data.Orm.SqlServer/Funcular.Data.Orm.SqlServer.csproj")
                .Where(r => r.Attribute("Include")?.Value == driver)
                .Select(r => new { Version = (string)r.Attribute("Version"), Condition = ConditionOf(r) })
                .ToList();
            var compiledAgainst = PackageReferences(providerProject)
                .Where(r => r.Attribute("Include")?.Value == driver)
                .Select(r => new { Version = (string)r.Attribute("Version"), Condition = ConditionOf(r) })
                .ToList();

            Assert.IsTrue(compiledAgainst.Count > 0, providerProject + " references no " + driver);
            Assert.AreEqual(compiledAgainst.Count, declared.Count,
                $"the package declares {driver} {declared.Count} time(s); the provider project references it {compiledAgainst.Count} time(s) (one per target framework condition)");
            foreach (var reference in compiledAgainst)
            {
                var match = declared.FirstOrDefault(d => d.Version == reference.Version);
                Assert.IsNotNull(match, $"the package doesn't declare {driver} {reference.Version} (the provider project's minimum{(reference.Condition == null ? "" : " for " + reference.Condition)})");
                Assert.IsTrue(match.Condition == null || !match.Condition.Contains("net48") || match.Condition.Contains("!="),
                    $"{driver} must not be declared for net48, which carries SQL Server only: {match.Condition}");
            }
        }

        [TestMethod]
        public void Package_DoesNotDeclareBundledDriversForNet48()
        {
            foreach (var reference in PackageReferences("Funcular.Data.Orm.SqlServer/Funcular.Data.Orm.SqlServer.csproj"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (include != "Npgsql" && include != "MySqlConnector" && include != "Microsoft.Data.Sqlite")
                    continue;
                var condition = ConditionOf(reference);
                Assert.IsNotNull(condition, include + " is declared for every target framework, net48 included");
                Assert.IsTrue(Regex.IsMatch(condition, @"!=\s*'net48'") || Regex.IsMatch(condition, @"==\s*'(net8\.0|netstandard2\.0)'"),
                    include + " is declared under a condition that doesn't exclude net48: " + condition);
            }
        }

        /// <summary>The README's provider table names each driver package and its minimum, as the csprojs pin it.</summary>
        [TestMethod]
        public void Readme_NamesEachDriverPackageAndMinimum()
        {
            var readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md"));
            var row = readme.Split('\n').FirstOrDefault(l => l.Contains("**Driver package (minimum)**"));
            Assert.IsNotNull(row, "README has no '**Driver package (minimum)**' row in the provider table");
            foreach (var project in ProviderProjects)
            {
                foreach (var reference in PackageReferences(project))
                {
                    var include = reference.Attribute("Include")?.Value;
                    if (!Drivers.Contains(include))
                        continue;
                    var version = (string)reference.Attribute("Version");
                    StringAssert.Contains(row, "`" + include + "`", "the README row doesn't name " + include);
                    StringAssert.Contains(row, version, $"the README row doesn't give {include}'s minimum {version}");
                }
            }
        }

        /// <summary>The condition on the reference or its ItemGroup, if any.</summary>
        private static string ConditionOf(XElement reference) =>
            (string)reference.Attribute("Condition") ?? (string)reference.Parent?.Attribute("Condition");
    }
}
