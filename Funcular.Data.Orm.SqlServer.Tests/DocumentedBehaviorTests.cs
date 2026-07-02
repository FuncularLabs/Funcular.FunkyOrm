using System;
using System.Linq;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Project;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    /// <summary>
    /// Backs the claims in Advanced.md / FUNKYORM_AI_ADVANCED.md that aren't already covered by a named test.
    /// Every ✅ construct documented there must execute; every ❌ must throw as documented. If a construct's
    /// behavior changes, this fails and the docs must be updated in lockstep.
    /// </summary>
    [TestClass]
    public class DocumentedBehaviorTests : SqlServerTestFixture
    {
        private sealed class SomeDto { public int Id { get; set; } }

        // §1 Projections — ✅ same-entity column subset
        [TestMethod]
        public void Projection_SameEntitySubset_Works()
        {
            var rows = _provider.Query<ProjectScorecard>()
                .Select(p => new ProjectScorecard { Name = p.Name })
                .ToList();
            Assert.IsNotNull(rows);
        }

        // §1 Projections — ✅ folding a self-contained computed attr into the same entity
        [TestMethod]
        public void Projection_FoldedComputedAttribute_Works()
        {
            var rows = _provider.Query<ProjectScorecard>()
                .Select(p => new ProjectScorecard { Name = p.Name, Priority = p.Priority })
                .ToList();
            Assert.IsNotNull(rows);
        }

        // §1 Projections — ❌ top-level projection to a different DTO
        [TestMethod]
        public void Projection_OtherDto_ThrowsNotSupported()
        {
            Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<PersonEntity>().Select(p => new SomeDto { Id = p.Id }).ToList());
        }

        // §1 Projections — ❌ identity projection
        [TestMethod]
        public void Projection_Identity_ThrowsNotSupported()
        {
            Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<PersonEntity>().Select(p => p).ToList());
        }

        // §2 Computed — ✅ [JsonPath] used in a WHERE predicate
        [TestMethod]
        public void Computed_JsonPath_InWhere_Executes()
        {
            var rows = _provider.Query<ProjectScorecard>()
                .Where(p => p.Priority == "high")
                .ToList();
            Assert.IsNotNull(rows); // executes without throwing
        }

        // §3 Aggregates — ❌ GroupBy is not translated (backs the docs' GroupBy row).
        // It currently surfaces at materialization as InvalidCastException; group in memory instead.
        [TestMethod]
        public void GroupBy_IsNotTranslated_Throws()
        {
            Assert.ThrowsException<InvalidCastException>(() =>
                _provider.Query<PersonEntity>().GroupBy(p => p.Gender).ToList());
        }

        // §3 Aggregates — ❌ non-simple-member selector
        [TestMethod]
        public void Aggregate_ExpressionSelector_ThrowsNotSupported()
        {
            Assert.ThrowsException<NotSupportedException>(() =>
                _provider.Query<PersonEntity>().Sum(p => p.Id + 1));
        }
    }
}
