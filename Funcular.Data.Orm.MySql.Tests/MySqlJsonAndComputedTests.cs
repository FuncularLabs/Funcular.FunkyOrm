using System;
using System.Linq;
using Funcular.Data.Orm.MySql.Tests.Domain;

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>
    /// Validates the four "view-replacing" attributes against MySQL: [JsonPath] (SELECT + WHERE
    /// predicates, including the 3.5.1 method-call fix), [SqlExpression], [SubqueryAggregate],
    /// and [JsonCollection].
    /// </summary>
    [TestClass]
    public class MySqlJsonAndComputedTests : MySqlTestFixture
    {
        private string _projectName;
        private int _projectId;
        private string _emptyProjectName;
        private int _emptyProjectId;

        [TestInitialize]
        public void Setup()
        {
            InitProvider();
            Seed();
        }

        [TestCleanup]
        public void Cleanup() => DisposeProvider();

        private void Seed()
        {
            var orgName = "Org" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var org = new Organization { Name = orgName };
            _provider.Insert(org);

            _projectName = "Apollo" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var project = new Project
            {
                Name = _projectName,
                OrganizationId = org.Id,
                Score = 80,
                Budget = 1000.50m,
                Metadata = "{\"client_name\":\"Acme Corp\",\"risk_level\":\"high\",\"priority\":3}",
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow
            };
            _provider.Insert(project);
            _projectId = project.Id;

            foreach (var (title, status) in new[] { ("M1", "completed"), ("M2", "completed"), ("M3", "pending") })
            {
                _provider.Insert(new ProjectMilestone { ProjectId = _projectId, Title = title, Status = status });
            }

            // "Empty" comparison project: NULL score, no JSON priority, no milestones.
            _emptyProjectName = "Empty" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var emptyProject = new Project
            {
                Name = _emptyProjectName,
                OrganizationId = org.Id,
                Score = null,
                Budget = null,
                Metadata = "{}",
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow
            };
            _provider.Insert(emptyProject);
            _emptyProjectId = emptyProject.Id;
        }

        /// <summary>Filters to exactly the two seeded projects (seeded + empty) by Name.</summary>
        private System.Linq.IQueryable<ProjectScorecard> SeededScorecards() =>
            _provider.Query<ProjectScorecard>()
                .Where(p => p.Name == _projectName || p.Name == _emptyProjectName);

        [TestMethod]
        public void JsonPath_Select_ExtractsScalars()
        {
            var card = _provider.Get<ProjectScorecard>(_projectId);
            Assert.IsNotNull(card);
            Assert.AreEqual("Acme Corp", card.ClientName);
            Assert.AreEqual("high", card.RiskLevel);
            Assert.AreEqual(3, card.Priority);
        }

        [TestMethod]
        public void JsonPath_Where_Equality_OnExtractedValue()
        {
            var results = _provider.Query<ProjectScorecard>()
                .Where(p => p.Name == _projectName && p.RiskLevel == "high")
                .ToList();
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void JsonPath_Where_CollectionContains_HonorsJsonAccessor()
        {
            // Exercises the 3.5.1 fix: method-call (IN) predicates must use the JSON accessor, not a plain column.
            var levels = new[] { "high", "critical" };
            var results = _provider.Query<ProjectScorecard>()
                .Where(p => p.Name == _projectName && levels.Contains(p.RiskLevel))
                .ToList();
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("high", results[0].RiskLevel);
        }

        [TestMethod]
        public void SqlExpression_ComputesLabel()
        {
            var card = _provider.Get<ProjectScorecard>(_projectId);
            Assert.AreEqual($"{_projectName} (score=80)", card.Label);
        }

        [TestMethod]
        public void SubqueryAggregate_Count_CountsChildren()
        {
            var card = _provider.Get<ProjectScorecard>(_projectId);
            Assert.AreEqual(3, card.MilestoneCount);
        }

        [TestMethod]
        public void SubqueryAggregate_ConditionalCount_FiltersByStatus()
        {
            var card = _provider.Get<ProjectScorecard>(_projectId);
            Assert.AreEqual(2, card.CompletedMilestones);
        }

        [TestMethod]
        public void JsonCollection_ProjectsChildrenAsJsonArray()
        {
            var card = _provider.Get<ProjectScorecard>(_projectId);
            Assert.IsFalse(string.IsNullOrEmpty(card.MilestonesJson));
            StringAssert.Contains(card.MilestonesJson, "M1");
            StringAssert.Contains(card.MilestonesJson, "completed");
        }

        // ─────────────────────────────────────────────────────────────
        // ORDER BY / DISTINCT on computed (view-replacing) attributes
        // ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void OrderBy_SqlExpression_OrdersByCoalescedScore()
        {
            // [SqlExpression] COALESCE(score, 0): empty (0) before seeded (80). Portable, no NULLs.
            var rows = SeededScorecards().OrderBy(p => p.EffectiveScore).ToList();
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(_emptyProjectName, rows.First().Name);
            Assert.AreEqual(_projectName, rows.Last().Name);
        }

        [TestMethod]
        public void OrderByDescending_SubqueryAggregate_OrdersByMilestoneCount()
        {
            // [SubqueryAggregate] count: seeded (3) before empty (0).
            var rows = SeededScorecards().OrderByDescending(p => p.MilestoneCount).ToList();
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(_projectName, rows.First().Name);
            Assert.AreEqual(_emptyProjectName, rows.Last().Name);
        }

        [TestMethod]
        public void OrderBy_ThenBy_ComputedAttributes_Composes()
        {
            var rows = SeededScorecards()
                .OrderBy(p => p.EffectiveScore)
                .ThenByDescending(p => p.MilestoneCount)
                .ToList();
            Assert.AreEqual(2, rows.Count); // composes + executes
        }

        [TestMethod]
        public void OrderBy_JsonPath_ExecutesAndOrders()
        {
            // [JsonPath] JSON_VALUE(metadata,'$.priority'). A broken resolver would emit ORDER BY priority
            // (a non-existent column) → SQL error. We only assert it executes & returns both rows; MySQL
            // NULL ordering (NULLs first ASC / last DESC) makes positional assertions non-portable.
            var rows = SeededScorecards().OrderByDescending(p => p.Priority).ToList();
            Assert.AreEqual(2, rows.Count);
        }

        [TestMethod]
        public void Distinct_OnProjection_EmitsSelectDistinct()
        {
            _sb.Clear();
            var rows = SeededScorecards()
                .Select(p => new ProjectScorecard { Name = p.Name })
                .Distinct()
                .ToList();
            StringAssert.Contains(_sb.ToString().ToUpperInvariant(), "SELECT DISTINCT");
            Assert.AreEqual(2, rows.Count); // two distinct names
        }

        [TestMethod]
        public void Distinct_WithCount_ThrowsNotSupported()
        {
            Assert.ThrowsException<NotSupportedException>(() =>
                SeededScorecards()
                    .Select(p => new ProjectScorecard { Name = p.Name })
                    .Distinct()
                    .Count());
        }

        [TestMethod]
        public void Distinct_OrderByUnprojectedColumn_Throws()
        {
            Assert.ThrowsException<InvalidOperationException>(() =>
                SeededScorecards()
                    .Select(p => new ProjectScorecard { Name = p.Name })
                    .Distinct()
                    .OrderBy(p => p.Score) // Score is not in the projection
                    .ToList());
        }

        [TestMethod]
        public void Distinct_FullEntity_EmitsSelectDistinct()
        {
            _sb.Clear();
            var rows = SeededScorecards().Distinct().ToList();
            StringAssert.Contains(_sb.ToString().ToUpperInvariant(), "SELECT DISTINCT");
            Assert.AreEqual(2, rows.Count);
        }

        [TestMethod]
        public void Distinct_OrderByProjectedColumn_Executes()
        {
            var rows = SeededScorecards()
                .Select(p => new ProjectScorecard { Name = p.Name })
                .Distinct()
                .OrderBy(p => p.Name)
                .ToList();
            Assert.AreEqual(2, rows.Count);
        }

        [TestMethod]
        public void Distinct_FullEntity_OrderByComputed_Executes()
        {
            _sb.Clear();
            var rows = SeededScorecards().OrderBy(p => p.EffectiveScore).Distinct().ToList();
            StringAssert.Contains(_sb.ToString().ToUpperInvariant(), "SELECT DISTINCT");
            Assert.AreEqual(2, rows.Count);
        }

        // ─────────────────────────────────────────────────────────────
        // Custom .Select(...) projection of self-contained computed
        // (view-replacing) attributes: [JsonPath], [SqlExpression],
        // [SubqueryAggregate] now project and materialize.
        // ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void Select_JsonPath_Projects_AndMaterializes()
        {
            var rows = _provider.Query<ProjectScorecard>()
                .Where(p => p.Name == _projectName)
                .Select(p => new ProjectScorecard { Priority = p.Priority })
                .ToList();
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(3, rows[0].Priority); // seeded metadata '$.priority' == 3 (SqlType=int)
        }

        [TestMethod]
        public void Select_SqlExpressionAndSubquery_Project_AndMaterialize()
        {
            var rows = _provider.Query<ProjectScorecard>()
                .Where(p => p.Name == _projectName)
                .Select(p => new ProjectScorecard { EffectiveScore = p.EffectiveScore, MilestoneCount = p.MilestoneCount })
                .ToList();
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(80, rows[0].EffectiveScore);   // COALESCE(score, 0) over seeded score 80
            Assert.AreEqual(3, rows[0].MilestoneCount);    // seeded 3 milestones
        }

        [TestMethod]
        public void Select_RemoteProperty_InCustomProjection_Throws()
        {
            Assert.ThrowsException<System.NotSupportedException>(() =>
                _provider.Query<PersonWithEmployer>()
                    .Select(p => new PersonWithEmployer { EmployerName = p.EmployerName })
                    .ToList());
        }
    }
}
