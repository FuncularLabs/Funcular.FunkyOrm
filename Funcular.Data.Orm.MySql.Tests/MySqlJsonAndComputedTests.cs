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
        }

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
    }
}
