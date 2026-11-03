using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests.NetFramework
{
    /// <summary>
    /// 3.14.0 (RELEASE_3.14.0_PLAN.md stream N): the assemblies the .NET Framework test host loads carry the Funcular
    /// Labs identity. The net8.0 projects pin the same in StrongNamingTests; this is the net48 half.
    /// </summary>
    [TestClass]
    public class StrongNamingNetFrameworkTests
    {
        [TestMethod]
        public void ProductionAssemblies_HaveTheFuncularLabsPublicKeyToken()
        {
            var assemblies = new[] { typeof(OrmDataProvider).Assembly, typeof(SqlServerOrmDataProvider).Assembly };
            foreach (var assembly in assemblies)
            {
                var token = assembly.GetName().GetPublicKeyToken();
                Assert.IsNotNull(token, assembly.GetName().Name + " has no public key token");
                Assert.AreEqual("0650eba294fc9e3f", string.Concat(token.Select(b => b.ToString("x2"))), assembly.GetName().Name + "'s token");
            }
        }
    }
}
