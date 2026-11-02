using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Funcular.Data.Orm.MySql;
using Funcular.Data.Orm.PostgreSql;
using Funcular.Data.Orm.Sqlite;

namespace Funcular.Data.Orm.SqlServer.Tests.Packaging
{
    /// <summary>
    /// DB-free pins for the 3.14.0 move of <c>PathNotFoundException</c>, <c>AmbiguousMatchException</c> and
    /// <c>NullableExpressionException</c> to Core (docs/plans/RELEASE_3.14.0_PLAN.md, stream E). Before 3.14.0 each
    /// of the four bundled provider assemblies declared its own copy, so a consumer could not name them (CS0433).
    /// </summary>
    [TestClass]
    public class ExceptionTypeTests
    {
        private static readonly string[] TypeNames =
        {
            "Funcular.Data.Orm.Exceptions.PathNotFoundException",
            "Funcular.Data.Orm.Exceptions.AmbiguousMatchException",
            "Funcular.Data.Orm.Exceptions.NullableExpressionException",
        };

        private static readonly Assembly Core = typeof(OrmDataProvider).Assembly;

        private static readonly Assembly[] Providers =
        {
            typeof(SqlServerOrmDataProvider).Assembly,   // Funcular.Data.Orm
            typeof(PostgreSqlOrmDataProvider).Assembly,
            typeof(MySqlOrmDataProvider).Assembly,
            typeof(SqliteOrmDataProvider).Assembly,
        };

        [TestMethod]
        public void ExceptionTypes_AreDefinedOnceInCore_AndForwardedByEveryProvider()
        {
            foreach (var name in TypeNames)
            {
                var inCore = Core.GetType(name, throwOnError: false);
                Assert.IsNotNull(inCore, name + " is not defined in " + Core.GetName().Name);
                Assert.AreEqual(Core, inCore.Assembly, name + " must be defined in Core");

                foreach (var provider in Providers)
                {
                    var providerName = provider.GetName().Name;
                    Assert.IsFalse(provider.DefinedTypes.Any(t => t.FullName == name),
                        providerName + " still defines its own " + name);
                    Assert.IsTrue(provider.GetForwardedTypes().Any(t => t == inCore),
                        providerName + " does not forward " + name + " to Core");

                    // A reference compiled against the provider assembly resolves through the forwarder.
                    var resolved = Type.GetType(name + ", " + providerName, throwOnError: false);
                    Assert.AreEqual(inCore, resolved, name + " qualified with " + providerName + " must resolve to Core's type");
                }
            }
        }

        [TestMethod]
        public void ExceptionTypes_KeepTheirBaseTypesAndConstructors()
        {
            AssertShape("Funcular.Data.Orm.Exceptions.PathNotFoundException", typeof(Exception));
            AssertShape("Funcular.Data.Orm.Exceptions.AmbiguousMatchException", typeof(Exception));
            AssertShape("Funcular.Data.Orm.Exceptions.NullableExpressionException", typeof(InvalidOperationException));
        }

        private static void AssertShape(string name, Type expectedBase)
        {
            var type = Core.GetType(name, throwOnError: true);
            Assert.AreEqual(expectedBase, type.BaseType, name + " base type");
            Assert.IsTrue(type.IsPublic && type.IsClass && !type.IsAbstract, name + " must be a public concrete class");

            var constructors = type.GetConstructors();
            Assert.AreEqual(1, constructors.Length, name + " has exactly one public constructor");
            var parameters = constructors[0].GetParameters();
            Assert.AreEqual(1, parameters.Length, name + "'s constructor takes one parameter");
            Assert.AreEqual(typeof(string), parameters[0].ParameterType, name + "'s constructor takes the message");

            var instance = (Exception)constructors[0].Invoke(new object[] { "the message" });
            Assert.AreEqual("the message", instance.Message, name + " must pass the message to its base");
        }

        /// <summary>
        /// A source scan beside the runtime pin above (the scan alone would not prove behaviour): no provider project
        /// declares the shared exception classes, and Core declares each once.
        /// </summary>
        [TestMethod]
        public void NoProviderDefinesTheSharedExceptionTypes()
        {
            var root = PackagingTests.RepositoryRoot();
            var declaration = new Regex(@"\bclass\s+(PathNotFoundException|AmbiguousMatchException|NullableExpressionException)\b");
            var providerDirectories = new[]
            {
                "Funcular.Data.Orm.SqlServer", "Funcular.Data.Orm.PostgreSql", "Funcular.Data.Orm.MySql", "Funcular.Data.Orm.Sqlite",
            };
            foreach (var directory in providerDirectories)
            {
                var offenders = Directory.EnumerateFiles(Path.Combine(root, directory), "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                                && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                    .Where(f => declaration.IsMatch(File.ReadAllText(f)))
                    .Select(f => f.Substring(root.Length + 1))
                    .ToList();
                Assert.AreEqual(0, offenders.Count, directory + " declares a shared exception type: " + string.Join(", ", offenders));
            }

            var coreFiles = Directory.EnumerateFiles(Path.Combine(root, "Funcular.Data.Orm.Core"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .Select(File.ReadAllText)
                .ToList();
            foreach (var typeName in new[] { "PathNotFoundException", "AmbiguousMatchException", "NullableExpressionException" })
            {
                var count = coreFiles.Sum(text => Regex.Matches(text, @"\bclass\s+" + typeName + @"\b").Count);
                Assert.AreEqual(1, count, "Core must declare " + typeName + " exactly once");
            }
        }
    }
}
