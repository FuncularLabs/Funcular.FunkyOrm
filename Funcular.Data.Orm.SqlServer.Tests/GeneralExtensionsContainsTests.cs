using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    /// <summary>
    /// <see cref="GeneralExtensions.Contains"/> applies the comparison it is given (owner decision 2026-10-02, provider-
    /// scoped caches plan §4.2). Called as a static method: on .NET 8 a string's instance
    /// <c>Contains(string, StringComparison)</c> would otherwise be chosen. DB-free.
    /// </summary>
    [TestClass]
    public class GeneralExtensionsContainsTests
    {
        [TestMethod]
        public void Contains_IgnoreCaseComparison_MatchesAcrossCase()
        {
            Assert.IsTrue(GeneralExtensions.Contains("WHERE 1=1 AND TRUE", "true", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void Contains_DefaultComparison_IsOrdinalIgnoreCase()
        {
            Assert.IsTrue(GeneralExtensions.Contains("Hello", "HELLO"));
        }

        [TestMethod]
        public void Contains_OrdinalComparison_IsCaseSensitive()
        {
            Assert.IsFalse(GeneralExtensions.Contains("Hello", "HELLO", StringComparison.Ordinal));
            Assert.IsTrue(GeneralExtensions.Contains("Hello", "ell", StringComparison.Ordinal));
        }

        [TestMethod]
        public void Contains_NullSource_IsFalse()
        {
            Assert.IsFalse(GeneralExtensions.Contains(null, "x", StringComparison.Ordinal));
        }
    }
}
