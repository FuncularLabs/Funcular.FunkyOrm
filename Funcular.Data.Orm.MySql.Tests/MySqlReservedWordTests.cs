using System;
using System.Linq;
using Funcular.Data.Orm.MySql.Tests.Domain;

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>
    /// Validates backtick identifier quoting via the reserved-word `user` table
    /// (columns `key`, `order`, `select` are MySQL reserved words).
    /// </summary>
    [TestClass]
    public class MySqlReservedWordTests : MySqlTestFixture
    {
        [TestInitialize]
        public void Setup() => InitProvider();

        [TestCleanup]
        public void Cleanup() => DisposeProvider();

        [TestMethod]
        public void Insert_Get_Update_OnReservedWordTable()
        {
            var name = "rw" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var user = new User { Name = name, Order = 7, Select = 1 };
            _provider.Insert(user);

            Assert.IsTrue(user.Key > 0, "Identity `key` should be assigned.");

            var fetched = _provider.Get<User>(user.Key);
            Assert.IsNotNull(fetched);
            Assert.AreEqual(7, fetched.Order);
            Assert.AreEqual(1, fetched.Select);

            user.Order = 9;
            _provider.Update(user);
            Assert.AreEqual(9, _provider.Get<User>(user.Key).Order);
        }

        [TestMethod]
        public void Query_WhereOnReservedWordColumn()
        {
            var name = "rw" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _provider.Insert(new User { Name = name, Order = 42, Select = 0 });

            var results = _provider.Query<User>().Where(u => u.Name == name && u.Order == 42).ToList();
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(42, results[0].Order);
        }
    }
}
