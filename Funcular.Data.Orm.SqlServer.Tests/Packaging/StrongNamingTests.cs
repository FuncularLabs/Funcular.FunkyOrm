using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Funcular.Data.Orm.MySql;
using Funcular.Data.Orm.PostgreSql;
using Funcular.Data.Orm.Sqlite;

namespace Funcular.Data.Orm.SqlServer.Tests.Packaging
{
    /// <summary>
    /// DB-free pins for strong naming (3.14.0, docs/plans/RELEASE_3.14.0_PLAN.md stream N): every production assembly
    /// carries the Funcular Labs identity; every InternalsVisibleTo grant names that key; the committed public key is
    /// the right one; and no private key is in the repository. Whether the PUBLISHED assemblies are fully signed (not
    /// public-signed) is checked by tools/signing/Verify-StrongNames.ps1 in the publish job, which this test host
    /// cannot see; here the identity is what is pinned, as the test host loads the net8.0 builds.
    /// </summary>
    [TestClass]
    public class StrongNamingTests
    {
        public const string FuncularLabsToken = "0650eba294fc9e3f";

        private static readonly Assembly[] ProductionAssemblies =
        {
            typeof(OrmDataProvider).Assembly,            // Funcular.Data.Orm.Core
            typeof(SqlServerOrmDataProvider).Assembly,   // Funcular.Data.Orm
            typeof(PostgreSqlOrmDataProvider).Assembly,
            typeof(MySqlOrmDataProvider).Assembly,
            typeof(SqliteOrmDataProvider).Assembly,
        };

        private static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));

        private static string Root => PackagingTests.RepositoryRoot();

        private static byte[] CommittedPublicKey => File.ReadAllBytes(Path.Combine(Root, "build", "FuncularLabs.publickey"));

        [TestMethod]
        public void EveryProductionAssembly_HasTheFuncularLabsPublicKeyToken()
        {
            foreach (var assembly in ProductionAssemblies)
            {
                var token = assembly.GetName().GetPublicKeyToken();
                Assert.IsNotNull(token, assembly.GetName().Name + " has no public key token (not strong-named)");
                Assert.AreEqual(FuncularLabsToken, Hex(token), assembly.GetName().Name + "'s public key token");
            }
        }

        /// <summary>The token is the reversed low 8 bytes of the SHA-1 of the public key blob: the committed file is the Funcular Labs key.</summary>
        [TestMethod]
        public void TheCommittedPublicKey_IsTheFuncularLabsKey()
        {
            var blob = CommittedPublicKey;
            Assert.AreEqual(288, blob.Length, "a 2048-bit RSA strong-name public key blob is 288 bytes (12-byte header, 20-byte RSAPUBKEY, 256-byte modulus)");
            byte[] hash;
            using (var sha1 = SHA1.Create())
                hash = sha1.ComputeHash(blob);
            var token = hash.Skip(hash.Length - 8).Reverse().ToArray();
            Assert.AreEqual(FuncularLabsToken, Hex(token), "build/FuncularLabs.publickey is not the Funcular Labs key");
            // And the loaded assemblies were signed with exactly this key, not merely one with the same token.
            foreach (var assembly in ProductionAssemblies)
                Assert.AreEqual(Hex(blob), Hex(assembly.GetName().GetPublicKey()), assembly.GetName().Name + "'s public key");
        }

        [TestMethod]
        public void EveryInternalsVisibleToGrant_CarriesTheFuncularLabsPublicKey()
        {
            var publicKey = ", PublicKey=" + Hex(CommittedPublicKey);
            foreach (var assembly in ProductionAssemblies)
            {
                var grants = assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName).ToList();
                foreach (var grant in grants)
                {
                    Assert.IsTrue(grant.EndsWith(publicKey, StringComparison.OrdinalIgnoreCase),
                        assembly.GetName().Name + " grants internals to '" + grant + "' without the Funcular Labs public key");
                    Assert.IsFalse(grant.StartsWith("Funcular.Data.Orm.SqlServer,", StringComparison.Ordinal),
                        assembly.GetName().Name + " still carries the dead grant to Funcular.Data.Orm.SqlServer (no assembly has that name)");
                }
            }
        }

        /// <summary>
        /// Review round 1, finding 3: the signing mode is a compiler input, so an incremental build with the key after a
        /// public-signed build recompiles instead of leaving public-signed DLLs behind. The mechanism is proven by the
        /// rehearsal (plan §7.9); this pins that it stays in the props.
        /// </summary>
        [TestMethod]
        public void TheSigningModeStamp_IsACompilerInput()
        {
            var props = File.ReadAllText(Path.Combine(Root, "Directory.Build.props"));
            StringAssert.Contains(props, "<Target Name=\"FunkyOrmSigningModeStamp\" BeforeTargets=\"CoreCompile\">", "the stamp target is missing or runs at the wrong time");
            StringAssert.Contains(props, "WriteOnlyWhenDifferent=\"true\"", "the stamp must be rewritten only when the mode changes, or every build recompiles");
            StringAssert.Contains(props, "Lines=\"PublicSign=$(PublicSign);KeyFile=$(AssemblyOriginatorKeyFile)\"", "the stamp must record the mode and the key path");
            StringAssert.Contains(props, "<CustomAdditionalCompileInputs Include=\"$(_FunkyOrmSigningStamp)\" />", "the stamp is not among the compiler's inputs");
        }

        [TestMethod]
        public void NoPrivateKeyIsTracked()
        {
            var snk = Directory.EnumerateFiles(Root, "*.snk", SearchOption.AllDirectories)
                .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part == "bin" || part == "obj" || part == ".git"))
                .Select(f => f.Substring(Root.Length + 1))
                .ToList();
            Assert.AreEqual(0, snk.Count, "a strong-name key pair is in the repository tree: " + string.Join(", ", snk));
            var ignored = File.ReadAllLines(Path.Combine(Root, ".gitignore")).Select(l => l.Trim());
            Assert.IsTrue(ignored.Contains("*.snk"), ".gitignore must ignore *.snk");
        }
    }
}
