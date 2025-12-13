using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    [TestClass]
    public class DocumentationGapTests : SqlDataProviderIntegrationTests
    {
        [TestInitialize]
        public new void Setup()
        {
            base.Setup();
            EnsureReservedWordSchema();
        }

        private void EnsureReservedWordSchema()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                // Use brackets for reserved words to ensure table creation succeeds
                cmd.CommandText = @"
                    IF EXISTS (SELECT * FROM sys.tables WHERE name = 'User')
                        DROP TABLE [User];

                    CREATE TABLE [User] (
                        [Id] INT IDENTITY(1,1) PRIMARY KEY,
                        [Order] INT NOT NULL,
                        [Key] NVARCHAR(100) NULL
                    );";
                cmd.ExecuteNonQuery();
            }
        }

        [TestMethod]
        public void ReservedWords_AreHandledCorrectly()
        {
            OutputTestMethodName();
            
            var entity = new ReservedWordEntity
            {
                Order = 123,
                Key = "SecretKey"
            };

            // Insert
            _provider.Insert(entity);
            Assert.IsTrue(entity.Id > 0, "Entity should have been inserted and ID populated.");

            // Get
            var fetched = _provider.Get<ReservedWordEntity>(entity.Id);
            Assert.IsNotNull(fetched, "Should be able to retrieve entity with reserved word table/columns.");
            Assert.AreEqual(123, fetched.Order);
            Assert.AreEqual("SecretKey", fetched.Key);

            // Query
            var queried = _provider.Query<ReservedWordEntity>()
                .Where(x => x.Order == 123)
                .FirstOrDefault();
            Assert.IsNotNull(queried, "Should be able to query using reserved word column.");
            Assert.AreEqual(entity.Id, queried.Id);
        }

        [TestMethod]
        public void Query_Sum_ReturnsCorrectSum()
        {
            OutputTestMethodName();
            
            // Arrange
            var guid = Guid.NewGuid().ToString();
            InsertTestPerson(guid, "A", "Sum1", null, "M", Guid.NewGuid());
            InsertTestPerson(guid, "B", "Sum2", null, "F", Guid.NewGuid());
            InsertTestPerson(guid, "C", "Sum3", null, "M", Guid.NewGuid());

            // Get the IDs to calculate expected sum
            var people = _provider.Query<PersonEntity>().Where(p => p.FirstName == guid).ToList();
            var expectedSum = people.Sum(p => p.Id);

            // Act
            var actualSum = _provider.Query<PersonEntity>()
                .Where(p => p.FirstName == guid)
                .Sum(p => p.Id);

            // Assert
            Assert.AreEqual(expectedSum, actualSum);
        }

        [TestMethod]
        public void Query_WithUnsupportedExpression_ThrowsException()
        {
            OutputTestMethodName();
            
            // Act & Assert
            // We expect an exception because GetHashCode() cannot be translated to SQL
            var ex = Assert.ThrowsException<NotSupportedException>(() =>
            {
                var result = _provider.Query<PersonEntity>()
                    .Where(p => p.FirstName.GetHashCode() == 123)
                    .ToList();
            });

            // Verify the message is somewhat informative (optional, but good practice)
            // The exact message depends on the implementation, but usually mentions "supported" or "translate"
            Console.WriteLine($"Caught expected exception: {ex.Message}");
        }
    }

    [Table("User")]
    public class ReservedWordEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("Id")]
        public int Id { get; set; }

        [Column("Order")]
        public int Order { get; set; }

        [Column("Key")]
        public string Key { get; set; }
    }
}
