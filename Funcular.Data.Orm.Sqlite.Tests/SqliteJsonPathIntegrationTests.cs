using System.Diagnostics;
using System.Text;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Project;
using Microsoft.Data.Sqlite;

namespace Funcular.Data.Orm.Sqlite.Tests
{
    [TestClass]
    public class SqliteJsonPathIntegrationTests
    {
        private static string _dbPath;
        private SqliteOrmDataProvider _provider;
        private readonly StringBuilder _sb = new();
        private int _testProjectId;

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"funky_sqlite_jsonpath_{Guid.NewGuid():N}.db");
            var connStr = $"Data Source={_dbPath}";
            using var conn = new SqliteConnection(connStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS organization (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, headquarters_address_id INTEGER);
CREATE TABLE IF NOT EXISTS project_category (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, code TEXT NOT NULL);
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

            cmd.CommandText = "INSERT INTO organization (name) VALUES ('SqliteJsonTest Org'); SELECT last_insert_rowid();";
            var orgId = (long)cmd.ExecuteScalar();

            cmd.CommandText = @"INSERT INTO project (name, organization_id, budget, score, metadata)
                VALUES (@name, @orgId, @budget, @score, @metadata); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@name", "SqliteJsonPath Test Project");
            cmd.Parameters.AddWithValue("@orgId", orgId);
            cmd.Parameters.AddWithValue("@budget", 150000.00);
            cmd.Parameters.AddWithValue("@score", 85);
            cmd.Parameters.AddWithValue("@metadata", "{\"priority\":\"high\",\"tags\":[\"api\",\"backend\"],\"client\":{\"name\":\"Acme Corp\",\"region\":\"NA\"},\"risk_level\":3}");
            _testProjectId = (int)(long)cmd.ExecuteScalar();

            cmd.Parameters.Clear();
            cmd.CommandText = "INSERT INTO project (name, organization_id, budget, score, metadata) VALUES ('SqliteNullMetadata Project', @orgId, 50000.00, 60, NULL);";
            cmd.Parameters.AddWithValue("@orgId", orgId);
            cmd.ExecuteNonQuery();

            cmd.Parameters.Clear();
            cmd.CommandText = @"INSERT INTO project (name, organization_id, budget, score, metadata)
                VALUES ('SqliteSecond Test Project', @orgId, 200000.00, NULL, @metadata);";
            cmd.Parameters.AddWithValue("@orgId", orgId);
            cmd.Parameters.AddWithValue("@metadata", "{\"priority\":\"low\",\"tags\":[\"frontend\"],\"client\":{\"name\":\"Globex Inc\",\"region\":\"EMEA\"},\"risk_level\":1}");
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
                    DELETE FROM project_note WHERE project_id IN (SELECT id FROM project WHERE name LIKE 'Sqlite%');
                    DELETE FROM project_milestone WHERE project_id IN (SELECT id FROM project WHERE name LIKE 'Sqlite%');
                    DELETE FROM project WHERE name LIKE 'Sqlite%';
                    DELETE FROM organization WHERE name LIKE 'Sqlite%';";
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        [TestMethod]
        public void JsonPath_GetById_ExtractsStringValues()
        {
            var result = _provider.Get<ProjectScorecard>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual("SqliteJsonPath Test Project", result.Name);
            Assert.AreEqual("high", result.Priority);
            Assert.AreEqual("Acme Corp", result.ClientName);
            Assert.AreEqual("NA", result.ClientRegion);
        }

        [TestMethod]
        public void JsonPath_GetById_ExtractsTypedInt()
        {
            var result = _provider.Get<ProjectScorecard>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.RiskLevel);
            Assert.AreEqual(3, result.RiskLevel.Value);
        }

        [TestMethod]
        public void JsonPath_NullMetadata_ReturnsNullProperties()
        {
            var results = _provider.Query<ProjectScorecard>()
                .Where(p => p.Name == "SqliteNullMetadata Project")
                .ToList();

            Assert.AreEqual(1, results.Count);
            var result = results[0];
            Assert.IsNull(result.Priority);
            Assert.IsNull(result.ClientName);
            Assert.IsNull(result.ClientRegion);
            Assert.IsNull(result.RiskLevel);
        }

        [TestMethod]
        public void JsonPath_QueryAll_ReturnsMultipleWithExtractedValues()
        {
            var results = _provider.Query<ProjectScorecard>()
                .Where(p => p.Name == "SqliteJsonPath Test Project" || p.Name == "SqliteSecond Test Project")
                .ToList();

            Assert.IsTrue(results.Count >= 2, $"Expected at least 2 results, got {results.Count}");

            var acme = results.FirstOrDefault(r => r.ClientName == "Acme Corp");
            Assert.IsNotNull(acme);
            Assert.AreEqual("high", acme.Priority);
            Assert.AreEqual(3, acme.RiskLevel);

            var globex = results.FirstOrDefault(r => r.ClientName == "Globex Inc");
            Assert.IsNotNull(globex);
            Assert.AreEqual("low", globex.Priority);
            Assert.AreEqual("EMEA", globex.ClientRegion);
            Assert.AreEqual(1, globex.RiskLevel);
        }

        [TestMethod]
        public void JsonPath_WhereClause_FiltersOnJsonValue()
        {
            var results = _provider.Query<ProjectScorecard>()
                .Where(p => p.Priority == "high")
                .ToList();

            Assert.IsTrue(results.Count >= 1);
            Assert.IsTrue(results.All(r => r.Priority == "high"));
        }

        [TestMethod]
        public void JsonPath_WhereClause_FiltersOnNestedJsonValue()
        {
            var results = _provider.Query<ProjectScorecard>()
                .Where(p => p.ClientRegion == "EMEA")
                .ToList();

            Assert.IsTrue(results.Count >= 1);
            var globex = results.FirstOrDefault(r => r.ClientName == "Globex Inc");
            Assert.IsNotNull(globex);
        }

        [TestMethod]
        public void JsonPath_BaseProperties_StillWork()
        {
            var result = _provider.Get<ProjectScorecard>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(_testProjectId, result.Id);
            Assert.AreEqual(150000.00m, result.Budget);
            Assert.AreEqual(85, result.Score);
            Assert.IsTrue(result.OrganizationId > 0);
        }

        [TestMethod]
        public void JsonPath_GetList_ReturnsAllWithJsonValues()
        {
            var results = _provider.GetList<ProjectScorecard>();

            Assert.IsNotNull(results);
            Assert.IsTrue(results.Count >= 3, $"Expected at least 3 results, got {results.Count}");

            var withJson = results.FirstOrDefault(r => r.Priority != null);
            Assert.IsNotNull(withJson);
        }
    }
}
