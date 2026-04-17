using System.Diagnostics;
using System.Text;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Project;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    /// <summary>
    /// Integration tests for Phases 2–4: [SqlExpression], [SubqueryAggregate], [JsonCollection].
    /// Uses the <see cref="ProjectScorecardFull"/> detail class.
    /// </summary>
    [TestClass]
    public class ComputedAttributeIntegrationTests
    {
        private string _connectionString;
        private SqlServerOrmDataProvider _provider;
        private readonly StringBuilder _sb = new();
        private int _testProjectId;
        private int _emptyProjectId;

        [TestInitialize]
        public void Setup()
        {
            _sb.Clear();
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
                                "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=funky_db;Integrated Security=True;";

            EnsureSchema();
            SeedTestData();

            _provider = new SqlServerOrmDataProvider(_connectionString)
            {
                Log = s =>
                {
                    Debug.WriteLine(s);
                    _sb.AppendLine(s);
                }
            };
        }

        [TestCleanup]
        public void Cleanup()
        {
            CleanupTestData();
            _provider?.Dispose();
        }

        private void EnsureSchema()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'organization')
                    BEGIN
                        CREATE TABLE organization (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            name NVARCHAR(100) NOT NULL,
                            headquarters_address_id INT NULL
                        );
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'project')
                    BEGIN
                        CREATE TABLE project (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            name NVARCHAR(200) NOT NULL,
                            organization_id INT NOT NULL,
                            lead_id INT NULL,
                            category_id INT NULL,
                            budget DECIMAL(12,2) NULL,
                            score INT NULL,
                            metadata NVARCHAR(MAX) NULL,
                            dateutc_created DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                            dateutc_modified DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                        );
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'project_milestone')
                    BEGIN
                        CREATE TABLE project_milestone (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            project_id INT NOT NULL,
                            title NVARCHAR(200) NOT NULL,
                            status NVARCHAR(50) NOT NULL DEFAULT 'pending',
                            due_date DATE NULL,
                            completed_date DATE NULL
                        );
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'project_note')
                    BEGIN
                        CREATE TABLE project_note (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            project_id INT NOT NULL,
                            author_id INT NULL,
                            content NVARCHAR(MAX) NOT NULL,
                            category NVARCHAR(50) NOT NULL DEFAULT 'general',
                            dateutc_created DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                        );
                    END";
                cmd.ExecuteNonQuery();
            }
        }

        private void SeedTestData()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();

                // Ensure org
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    IF NOT EXISTS (SELECT 1 FROM organization WHERE name = 'ComputedTest Org')
                        INSERT INTO organization (name) VALUES ('ComputedTest Org');
                    SELECT id FROM organization WHERE name = 'ComputedTest Org';";
                var orgId = (int)cmd.ExecuteScalar();

                // Project with score and metadata
                cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO project (name, organization_id, budget, score, metadata)
                    VALUES (
                        'ComputedTest Project', @orgId, 150000.00, 85,
                        N'{""priority"":""high"",""client"":{""name"":""Acme Corp"",""region"":""NA""},""risk_level"":3}'
                    );
                    SELECT SCOPE_IDENTITY();";
                cmd.Parameters.AddWithValue("@orgId", orgId);
                _testProjectId = Convert.ToInt32(cmd.ExecuteScalar());

                // Project with no milestones/notes (for zero-count tests)
                cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO project (name, organization_id, score)
                    VALUES ('EmptyProject', @orgId, NULL);
                    SELECT SCOPE_IDENTITY();";
                cmd.Parameters.AddWithValue("@orgId", orgId);
                _emptyProjectId = Convert.ToInt32(cmd.ExecuteScalar());

                // Milestones: 2 completed, 1 overdue, 2 pending = 5 total
                cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO project_milestone (project_id, title, status, due_date, completed_date) VALUES
                        (@pid, 'Requirements', 'completed', '2025-07-01', '2025-06-28'),
                        (@pid, 'Design Review', 'completed', '2025-07-15', '2025-07-14'),
                        (@pid, 'Development', 'overdue', '2025-06-15', NULL),
                        (@pid, 'QA Testing', 'pending', '2025-08-15', NULL),
                        (@pid, 'Deployment', 'pending', '2025-09-01', NULL);";
                cmd.Parameters.AddWithValue("@pid", _testProjectId);
                cmd.ExecuteNonQuery();

                // Notes: 2 general, 1 risk, 1 blocker = 4 total, 1 risk
                cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO project_note (project_id, content, category) VALUES
                        (@pid, 'Initial planning notes', 'general'),
                        (@pid, 'Budget approved', 'general'),
                        (@pid, 'High dependency on external API', 'risk'),
                        (@pid, 'Missing test environment', 'blocker');";
                cmd.Parameters.AddWithValue("@pid", _testProjectId);
                cmd.ExecuteNonQuery();
            }
        }

        private void CleanupTestData()
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
                        DELETE FROM project_note WHERE project_id IN (SELECT id FROM project WHERE name IN ('ComputedTest Project', 'EmptyProject'));
                        DELETE FROM project_milestone WHERE project_id IN (SELECT id FROM project WHERE name IN ('ComputedTest Project', 'EmptyProject'));
                        DELETE FROM project WHERE name IN ('ComputedTest Project', 'EmptyProject');";
                    cmd.ExecuteNonQuery();
                }
            }
            catch { /* best-effort cleanup */ }
        }

        // ???????????????????????????????????????????????????????????
        // Phase 2: [SqlExpression] Tests
        // ???????????????????????????????????????????????????????????

        [TestMethod]
        public void SqlExpression_CoalesceScore_ReturnsActualScore()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(85, result.EffectiveScore, "COALESCE(score, 0) should return 85 when score is 85");
        }

        [TestMethod]
        public void SqlExpression_CoalesceScore_ReturnsZeroWhenNull()
        {
            var result = _provider.Get<ProjectScorecardFull>(_emptyProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.EffectiveScore, "COALESCE(score, 0) should return 0 when score is NULL");
        }

        // ???????????????????????????????????????????????????????????
        // Phase 3: [SubqueryAggregate] Tests
        // ???????????????????????????????????????????????????????????

        [TestMethod]
        public void SubqueryAggregate_MilestoneCount_Returns5()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(5, result.MilestoneCount, "Should have 5 milestones");
        }

        [TestMethod]
        public void SubqueryAggregate_MilestonesCompleted_Returns2()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.MilestonesCompleted, "Should have 2 completed milestones");
        }

        [TestMethod]
        public void SubqueryAggregate_MilestonesOverdue_Returns1()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.MilestonesOverdue, "Should have 1 overdue milestone");
        }

        [TestMethod]
        public void SubqueryAggregate_MilestonesPending_Returns2()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.MilestonesPending, "Should have 2 pending milestones");
        }

        [TestMethod]
        public void SubqueryAggregate_NoteCount_Returns4()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(4, result.NoteCount, "Should have 4 notes total");
        }

        [TestMethod]
        public void SubqueryAggregate_RiskNoteCount_Returns1()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.RiskNoteCount, "Should have 1 risk note");
        }

        [TestMethod]
        public void SubqueryAggregate_EmptyProject_ReturnsZeros()
        {
            var result = _provider.Get<ProjectScorecardFull>(_emptyProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.MilestoneCount, "Empty project should have 0 milestones");
            Assert.AreEqual(0, result.MilestonesCompleted, "Empty project should have 0 completed");
            Assert.AreEqual(0, result.NoteCount, "Empty project should have 0 notes");
            Assert.AreEqual(0, result.RiskNoteCount, "Empty project should have 0 risk notes");
        }

        [TestMethod]
        public void SubqueryAggregate_QueryAll_ReturnsCorrectCounts()
        {
            var results = _provider.Query<ProjectScorecardFull>()
                .Where(p => p.Name == "ComputedTest Project" || p.Name == "EmptyProject")
                .ToList();

            Assert.IsTrue(results.Count >= 2, $"Expected at least 2 results, got {results.Count}");

            var full = results.FirstOrDefault(r => r.Name == "ComputedTest Project");
            Assert.IsNotNull(full);
            Assert.AreEqual(5, full.MilestoneCount);
            Assert.AreEqual(4, full.NoteCount);

            var empty = results.FirstOrDefault(r => r.Name == "EmptyProject");
            Assert.IsNotNull(empty);
            Assert.AreEqual(0, empty.MilestoneCount);
            Assert.AreEqual(0, empty.NoteCount);
        }

        // ???????????????????????????????????????????????????????????
        // Phase 4: [JsonCollection] Tests
        // ???????????????????????????????????????????????????????????

        [TestMethod]
        public void JsonCollection_MilestonesJson_ReturnsJsonArray()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.MilestonesJson, "MilestonesJson should not be null");
            Assert.IsTrue(result.MilestonesJson.StartsWith("["), $"Should be a JSON array, got: {result.MilestonesJson.Substring(0, Math.Min(50, result.MilestonesJson.Length))}");
            Assert.IsTrue(result.MilestonesJson.Contains("Requirements"), "Should contain milestone title 'Requirements'");
            Assert.IsTrue(result.MilestonesJson.Contains("completed"), "Should contain status 'completed'");
            Assert.IsTrue(result.MilestonesJson.Contains("pending"), "Should contain status 'pending'");

            Debug.WriteLine($"MilestonesJson: {result.MilestonesJson}");
        }

        [TestMethod]
        public void JsonCollection_NotesJson_ReturnsJsonArray()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.NotesJson, "NotesJson should not be null");
            Assert.IsTrue(result.NotesJson.StartsWith("["), "Should be a JSON array");
            Assert.IsTrue(result.NotesJson.Contains("Initial planning notes"), "Should contain note content");
            Assert.IsTrue(result.NotesJson.Contains("risk"), "Should contain category 'risk'");

            Debug.WriteLine($"NotesJson: {result.NotesJson}");
        }

        [TestMethod]
        public void JsonCollection_EmptyProject_ReturnsNull()
        {
            var result = _provider.Get<ProjectScorecardFull>(_emptyProjectId);

            Assert.IsNotNull(result);
            Assert.IsNull(result.MilestonesJson, "Empty project should have null MilestonesJson");
            Assert.IsNull(result.NotesJson, "Empty project should have null NotesJson");
        }

        // ???????????????????????????????????????????????????????????
        // Combined: All attributes work together
        // ???????????????????????????????????????????????????????????

        [TestMethod]
        public void AllAttributes_CombinedOnSingleEntity_WorkTogether()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);

            // Base entity properties
            Assert.AreEqual("ComputedTest Project", result.Name);
            Assert.AreEqual(85, result.Score);

            // Phase 1: JsonPath (inherited)
            Assert.AreEqual("high", result.Priority);
            Assert.AreEqual("Acme Corp", result.ClientName);
            Assert.AreEqual(3, result.RiskLevel);

            // Phase 2: SqlExpression
            Assert.AreEqual(85, result.EffectiveScore);

            // Phase 3: SubqueryAggregate
            Assert.AreEqual(5, result.MilestoneCount);
            Assert.AreEqual(2, result.MilestonesCompleted);
            Assert.AreEqual(1, result.MilestonesOverdue);
            Assert.AreEqual(4, result.NoteCount);
            Assert.AreEqual(1, result.RiskNoteCount);

            // Phase 4: JsonCollection
            Assert.IsNotNull(result.MilestonesJson);
            Assert.IsNotNull(result.NotesJson);

            Debug.WriteLine($"All attributes on one entity:");
            Debug.WriteLine($"  Priority={result.Priority}, ClientName={result.ClientName}, RiskLevel={result.RiskLevel}");
            Debug.WriteLine($"  EffectiveScore={result.EffectiveScore}");
            Debug.WriteLine($"  Milestones: {result.MilestoneCount} total, {result.MilestonesCompleted} completed, {result.MilestonesOverdue} overdue");
            Debug.WriteLine($"  Notes: {result.NoteCount} total, {result.RiskNoteCount} risk");
            Debug.WriteLine($"  MilestonesJson length: {result.MilestonesJson?.Length}");
            Debug.WriteLine($"  NotesJson length: {result.NotesJson?.Length}");
        }

        [TestMethod]
        public void AllAttributes_GetList_WorksAcrossMultipleRows()
        {
            var results = _provider.Query<ProjectScorecardFull>()
                .Where(p => p.Name == "ComputedTest Project" || p.Name == "EmptyProject")
                .ToList();

            Assert.IsTrue(results.Count >= 2);

            foreach (var r in results)
            {
                // All properties should be populated without error
                Debug.WriteLine($"{r.Name}: Score={r.Score}, EffScore={r.EffectiveScore}, Milestones={r.MilestoneCount}, Notes={r.NoteCount}");
            }
        }
    }
}
