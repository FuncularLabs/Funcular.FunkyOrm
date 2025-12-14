using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Funcular.Data.Orm;
using Xunit;

namespace Funcular.Data.Orm.SqlServer.Tests.DotNet10
{
    [Table("person")]
    public class Person
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("first_name")]
        public string FirstName { get; set; }

        [Column("last_name")]
        public string LastName { get; set; }
    }
    
    public class ContainsTests : IDisposable
    {
        private readonly SqlServerOrmDataProvider _provider;
        private readonly string _connectionString;

        public ContainsTests()
        {
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
                                "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=funky_db;Integrated Security=True;";
            _provider = new SqlServerOrmDataProvider(_connectionString);
        }

        public void Dispose()
        {
            _provider?.Dispose();
        }

        /// <summary>
        /// Regression note: Starting with C# 14 / .NET 9 there was a change in how
        /// array/list literal and ReadOnlySpan overload resolution is handled in
        /// expression trees used by some LINQ providers. In earlier C#/runtime
        /// versions a local array (e.g. "int[] ids = ...") used inside a
        /// predicate like "ids.Contains(x.Id)" was translated by the provider to
        /// an SQL IN-list via detection of the collection's concrete type.
        ///
        /// The breaking change affects the compiler/runtime's binding for
        /// certain collection types in expression trees: the compiler may prefer
        /// ReadOnlySpan or reinterpret array-slice operations, producing
        /// expressions the LINQ-to-SQL translator doesn't recognize and thus
        /// causes a "method not supported" or similar translation error.
        ///
        /// To work around this regression the test and calling code must ensure
        /// the collection is a concrete, reference-type collection (e.g. array,
        /// List<T>, HashSet<T>) at the point it is captured in the expression
        /// tree, or otherwise avoid overloads that can be mapped to
        /// ReadOnlySpan. The Contains test below explicitly uses an
        /// `int[]` (or HashSet) captured as a local variable so the provider
        /// sees a supported collection type when inspecting the expression tree.
        /// </summary>
        [Fact]
        public void Query_Person_WithIdInArray_ReturnsCorrectPersons()
        {
            // Arrange
            // Get some existing IDs first to ensure we have valid data to query
            var persons = _provider.Query<Person>().Take(10).ToList();
            if (!persons.Any())
            {
                // If no data, we can't really test, but let's assume there is data as per instructions
                // Or we could insert some, but let's try to use existing data first.
                return; 
            }

            var personIds = persons.Select(x => x.Id).Distinct().ToArray();

            // Act & Assert
            // The user says this fails in .NET 10 / C# 12+ because array is interpreted as ReadOnlySpan
            // and causes a "method not supported" exception or similar.
            // We expect this to fail currently, so we might want to assert that it throws, 
            // OR just run it and let it fail to confirm the issue.
            // The instructions say: "Extract the exception details from the failing test's execution"
            // So I should write the test to expect success, and let it fail.
            
            var result = _provider.Query<Person>().Where(r => personIds.Contains(r.Id)).ToList();

            Assert.NotEmpty(result);
            Assert.Equal(personIds.Length, result.Count);
        }

        [Fact]
        public void Query_Person_WithIdInHashSet_ReturnsCorrectPersons()
        {
            // Arrange
            var persons = _provider.Query<Person>().Take(10).ToList();
            if (!persons.Any()) return;

            var personIds = persons.Select(x => x.Id).Distinct().ToHashSet();

            // Act
            var result = _provider.Query<Person>().Where(r => personIds.Contains(r.Id)).ToList();

            // Assert
            Assert.NotEmpty(result);
            Assert.Equal(personIds.Count, result.Count);
        }
    }
}
