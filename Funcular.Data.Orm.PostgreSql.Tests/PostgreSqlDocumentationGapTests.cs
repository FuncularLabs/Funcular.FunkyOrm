using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Objects.User;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    [TestClass]
    public class PostgreSqlDocumentationGapTests : PostgreSqlDataProviderIntegrationTests
    {
        [TestMethod]
        public void ReservedWords_AreHandledCorrectly()
        {
            OutputTestMethodName();

            var uniqueName = $"TestUser_{Guid.NewGuid():N}";
            var entity = new User
            {
                Name = uniqueName,
                Order = 123,
                Select = true
            };

            _provider.Insert(entity);
            Assert.IsTrue(entity.Key > 0, "Entity should have been inserted and Key populated.");

            var fetched = _provider.Get<User>(entity.Key);
            Assert.IsNotNull(fetched, "Should be able to retrieve entity with reserved word table/columns.");
            Assert.AreEqual(123, fetched.Order);
            Assert.AreEqual(uniqueName, fetched.Name);
            Assert.AreEqual(true, fetched.Select);

            var queried = _provider.Query<User>()
                .Where(x => x.Name == uniqueName)
                .FirstOrDefault();
            Assert.IsNotNull(queried, "Should be able to query using reserved word column.");
            Assert.AreEqual(entity.Key, queried.Key);

            // Cleanup
            _provider.BeginTransaction();
            _provider.Delete<User>(x => x.Key == entity.Key);
            _provider.CommitTransaction();
        }
    }
}
