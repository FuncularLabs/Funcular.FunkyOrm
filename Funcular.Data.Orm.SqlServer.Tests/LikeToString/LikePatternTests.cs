using Funcular.Data.Orm.Linq;

namespace Funcular.Data.Orm.SqlServer.Tests.LikeToString
{
    /// <summary>
    /// The DB-free rows of the LIKE-escaping plan (docs/plans/LIKE_ESCAPING_TOSTRING_PLAN.md, §4.1, AC5):
    /// <see cref="LikePattern"/>'s escaping and its escape character and clause.
    /// </summary>
    [TestClass]
    public class LikePatternTests
    {
        [DataTestMethod]
        [DataRow("_", "!_")]
        [DataRow("%", "!%")]
        [DataRow("[", "![")]
        [DataRow("!", "!!")]
        [DataRow("a_b%c[d!e", "a!_b!%c![d!!e")]
        [DataRow("]", "]")]
        [DataRow("\\", "\\")]
        [DataRow("abc", "abc")]
        [DataRow("^", "^")]
        [DataRow("-", "-")]
        [DataRow("", "")]
        public void Escape_EscapesTheMetacharacters(string value, string expected) =>
            Assert.AreEqual(expected, LikePattern.Escape(value));

        [TestMethod]
        public void Escape_Null_ReturnsNull() => Assert.IsNull(LikePattern.Escape(null));

        [TestMethod]
        public void Constants_AreTheEscapeCharacterAndClause()
        {
            Assert.AreEqual('!', LikePattern.EscapeCharacter);
            Assert.AreEqual(" ESCAPE '!'", LikePattern.EscapeClause);
        }
    }
}
