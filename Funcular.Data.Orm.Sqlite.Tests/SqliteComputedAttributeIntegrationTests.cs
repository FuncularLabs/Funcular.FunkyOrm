using System.Diagnostics;
using System.Text;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Project;
using Microsoft.Data.Sqlite;

namespace Funcular.Data.Orm.Sqlite.Tests
{
    [TestClass]
    public class SqliteComputedAttributeIntegrationTests
    {
        private static string _dbPath;
        private SqliteOrmDataProvider _provider;
        private readonly StringBuilder _sb = new();
        private int _testProjectId;
        private int _emptyProjectId;

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"funky_sqlite_computed_{Guid.NewGuid():N}.db");
            var connStr = $"Data Source={_dbPath}";
            using var conn = new SqliteConnection(connStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS organization (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, headquarters_address_id INTEGER);
CREATE TABLE IF NOT EXISTS project (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, organization_id INTEGER NOT NULL, lead_id INTEGER, category_id INTEGER, budget REAL, score INTEGER, metadata TEXT, dateutc_created TEXT NOT NULL DEFAULT (datetime('now')), dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')));
CREATE TABLE IF NOT EXISTS project_milestone (id INTEGER PRIMARY KEY AUTOINCREMENT, project_id INTEGER NOT NULL, title TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'pending', due_date TEXT, completed_date TEXT);
CREATE TABLE IF NOT EXISTS project_note (id INTEGER PRIMARY KEY AUTOINCREMENT, project_id INTEGER NOT NULL, author_id INTEGER, content TEXT NOT NULL, category TEXT NOT NULL DEFAULT 'general', dateutc_created TEXT NOT NULL DEFAULT (datetime('now')));";
            cmd.ExecuteNonQuery();
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        }

        [TestInitialize]
        public void Setup()
        {
            _sb.Clear();
            var connStr = $"Data Source={_dbPath}";
            _provider = new SqliteOrmDataProvider(connStr)
            {
                Log = s => { Debug.WriteLine(s); _sb.AppendLine(s); }
            };
            SeedTestData();
        }

        [TestCleanup]
        public void Cleanup()
        {
            CleanupTestData();
            _provider?.Dispose();
        }

        private void SeedTestData()
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = "INSERT INTO organization (name) VALUES ('SqliteComputedTest Org'); SELECT last_insert_rowid();";
            var orgId = (long)cmd.ExecuteScalar();

            cmd.CommandText = @"INSERT INTO project (name, organization_id, budget, score, metadata)
                VALUES (@name, @orgId, 150000.00, 85, @metadata); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@name", "SqliteComputedTest Project");
            cmd.Parameters.AddWithValue("@orgId", orgId);
            cmd.Parameters.AddWithValue("@metadata", "{\"priority\":\"high\",\"client\":{\"name\":\"Acme Corp\",\"region\":\"NA\"},\"risk_level\":3}");
            _testProjectId = (int)(long)cmd.ExecuteScalar();

            cmd.Parameters.Clear();
            cmd.CommandText = "INSERT INTO project (name, organization_id, score) VALUES ('SqliteEmptyProject', @orgId, NULL); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@orgId", orgId);
            _emptyProjectId = (int)(long)cmd.ExecuteScalar();

            // 5 milestones: 2 completed, 1 overdue, 2 pending
            cmd.Parameters.Clear();
            cmd.CommandText = @"
INSERT INTO project_milestone (project_id, title, status, due_date, completed_date) VALUES
    (@pid, 'Requirements', 'completed', '2025-07-01', '2025-06-28'),
    (@pid, 'Design Review', 'completed', '2025-07-15', '2025-07-14'),
    (@pid, 'Development', 'overdue', '2025-06-15', NULL),
    (@pid, 'QA Testing', 'pending', '2025-08-15', NULL),
    (@pid, 'Deployment', 'pending', '2025-09-01', NULL);";
            cmd.Parameters.AddWithValue("@pid", _testProjectId);
            cmd.ExecuteNonQuery();

            // 4 notes: 2 general, 1 risk, 1 blocker
            cmd.Parameters.Clear();
            cmd.CommandText = @"
INSERT INTO project_note (project_id, content, category) VALUES
    (@pid, 'Initial planning notes', 'general'),
    (@pid, 'Budget approved', 'general'),
    (@pid, 'High dependency on external API', 'risk'),
    (@pid, 'Missing test environment', 'blocker');";
            cmd.Parameters.AddWithValue("@pid", _testProjectId);
            cmd.ExecuteNonQuery();
        }

        private void CleanupTestData()
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    DELETE FROM project_note WHERE project_id IN (SELECT id FROM project WHERE name IN ('SqliteComputedTest Project', 'SqliteEmptyProject'));
                    DELETE FROM project_milestone WHERE project_id IN (SELECT id FROM project WHERE name IN ('SqliteComputedTest Project', 'SqliteEmptyProject'));
                    DELETE FROM project WHERE name IN ('SqliteComputedTest Project', 'SqliteEmptyProject');
                    DELETE FROM organization WHERE name LIKE 'SqliteComputed%';";
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // Phase 2: [SqlExpression] Tests
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [TestMethod]
        public void SqlExpression_CoalesceScore_ReturnsActualScore()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(85, result.EffectiveScore);
        }

        [TestMethod]
        public void SqlExpression_CoalesceScore_ReturnsZeroWhenNull()
        {
            var result = _provider.Get<ProjectScorecardFull>(_emptyProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.EffectiveScore);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // Phase 3: [SubqueryAggregate] Tests
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [TestMethod]
        public void SubqueryAggregate_MilestoneCount_Returns5()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(5, result.MilestoneCount);
        }

        [TestMethod]
        public void SubqueryAggregate_MilestonesCompleted_Returns2()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.MilestonesCompleted);
        }

        [TestMethod]
        public void SubqueryAggregate_MilestonesOverdue_Returns1()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.MilestonesOverdue);
        }

        [TestMethod]
        public void SubqueryAggregate_MilestonesPending_Returns2()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.MilestonesPending);
        }

        [TestMethod]
        public void SubqueryAggregate_NoteCount_Returns4()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(4, result.NoteCount);
        }

        [TestMethod]
        public void SubqueryAggregate_RiskNoteCount_Returns1()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.RiskNoteCount);
        }

        [TestMethod]
        public void SubqueryAggregate_EmptyProject_ReturnsZeros()
        {
            var result = _provider.Get<ProjectScorecardFull>(_emptyProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.MilestoneCount);
            Assert.AreEqual(0, result.MilestonesCompleted);
            Assert.AreEqual(0, result.NoteCount);
            Assert.AreEqual(0, result.RiskNoteCount);
        }

        [TestMethod]
        public void SubqueryAggregate_QueryAll_ReturnsCorrectCounts()
        {
            var results = _provider.Query<ProjectScorecardFull>()
                .Where(p => p.Name == "SqliteComputedTest Project" || p.Name == "SqliteEmptyProject")
                .ToList();

            Assert.IsTrue(results.Count >= 2, $"Expected at least 2 results, got {results.Count}");

            var full = results.FirstOrDefault(r => r.Name == "SqliteComputedTest Project");
            Assert.IsNotNull(full);
            Assert.AreEqual(5, full.MilestoneCount);
            Assert.AreEqual(4, full.NoteCount);

            var empty = results.FirstOrDefault(r => r.Name == "SqliteEmptyProject");
            Assert.IsNotNull(empty);
            Assert.AreEqual(0, empty.MilestoneCount);
            Assert.AreEqual(0, empty.NoteCount);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // Phase 4: [JsonCollection] Tests
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [TestMethod]
        public void JsonCollection_MilestonesJson_ReturnsJsonArray()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.MilestonesJson, "MilestonesJson should not be null");
            Assert.IsTrue(result.MilestonesJson.StartsWith("["), $"Should be a JSON array, got: {result.MilestonesJson.Substring(0, Math.Min(50, result.MilestonesJson.Length))}");
            Assert.IsTrue(result.MilestonesJson.Contains("Requirements"));
            Assert.IsTrue(result.MilestonesJson.Contains("completed"));
            Assert.IsTrue(result.MilestonesJson.Contains("pending"));
        }

        [TestMethod]
        public void JsonCollection_NotesJson_ReturnsJsonArray()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.NotesJson, "NotesJson should not be null");
            Assert.IsTrue(result.NotesJson.StartsWith("["));
            Assert.IsTrue(result.NotesJson.Contains("Initial planning notes"));
            Assert.IsTrue(result.NotesJson.Contains("risk"));
        }

        [TestMethod]
        public void JsonCollection_EmptyProject_ReturnsEmptyArrayOrNull()
        {
            var result = _provider.Get<ProjectScorecardFull>(_emptyProjectId);

            Assert.IsNotNull(result);
            // SQLite's json_group_array() returns "[]" for empty sets rather than NULL
            Assert.IsTrue(result.MilestonesJson == null || result.MilestonesJson == "[]");
            Assert.IsTrue(result.NotesJson == null || result.NotesJson == "[]");
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // Combined: All attributes work together
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        [TestMethod]
        public void AllAttributes_CombinedOnSingleEntity_WorkTogether()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);

            // Base entity
            Assert.AreEqual("SqliteComputedTest Project", result.Name);
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
        }

        [TestMethod]
        public void AllAttributes_GetList_WorksAcrossMultipleRows()
        {
            var results = _provider.Query<ProjectScorecardFull>()
                .Where(p => p.Name == "SqliteComputedTest Project" || p.Name == "SqliteEmptyProject")
                .ToList();

            Assert.IsTrue(results.Count >= 2);

            foreach (var r in results)
            {
                Debug.WriteLine($"{r.Name}: Score={r.Score}, EffScore={r.EffectiveScore}, Milestones={r.MilestoneCount}, Notes={r.NoteCount}");
            }
        }
    }
}
