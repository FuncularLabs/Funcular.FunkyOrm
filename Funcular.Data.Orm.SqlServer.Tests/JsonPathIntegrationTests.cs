using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Project;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    [TestClass]
    public class JsonPathIntegrationTests
    {
        private string _connectionString;
        private SqlServerOrmDataProvider _provider;
        private readonly StringBuilder _sb = new();
        private int _testProjectId;

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
                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'project_category')
                    BEGIN
                        CREATE TABLE project_category (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            name NVARCHAR(100) NOT NULL,
                            code NVARCHAR(50) NOT NULL
                        );
                        INSERT INTO project_category (name, code) VALUES
                            ('Internal Tooling', 'internal'),
                            ('Client Deliverable', 'client'),
                            ('Research & Development', 'rnd');
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
                    END

                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'organization')
                    BEGIN
                        CREATE TABLE organization (
                            id INT IDENTITY(1,1) PRIMARY KEY,
                            name NVARCHAR(100) NOT NULL,
                            headquarters_address_id INT NULL
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

                // Ensure we have an organization
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    IF NOT EXISTS (SELECT 1 FROM organization WHERE name = 'JsonTest Org')
                        INSERT INTO organization (name) VALUES ('JsonTest Org');
                    SELECT id FROM organization WHERE name = 'JsonTest Org';";
                var orgId = (int)cmd.ExecuteScalar();

                // Insert test project with rich JSON metadata
                cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO project (name, organization_id, budget, score, metadata)
                    VALUES (
                        @name, @orgId, @budget, @score,
                        N'{""priority"":""high"",""tags"":[""api"",""backend""],""client"":{""name"":""Acme Corp"",""region"":""NA""},""risk_level"":3}'
                    );
                    SELECT SCOPE_IDENTITY();";
                cmd.Parameters.AddWithValue("@name", "JsonPath Test Project");
                cmd.Parameters.AddWithValue("@orgId", orgId);
                cmd.Parameters.AddWithValue("@budget", 150000.00m);
                cmd.Parameters.AddWithValue("@score", 85);
                _testProjectId = Convert.ToInt32(cmd.ExecuteScalar());

                // Insert a project with NULL metadata
                cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO project (name, organization_id, budget, score, metadata)
                    VALUES ('NullMetadata Project', @orgId, 50000.00, 60, NULL);";
                cmd.Parameters.AddWithValue("@orgId", orgId);
                cmd.ExecuteNonQuery();

                // Insert a project with different JSON values
                cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO project (name, organization_id, budget, score, metadata)
                    VALUES (
                        'Second Test Project', @orgId, 200000.00, NULL,
                        N'{""priority"":""low"",""tags"":[""frontend""],""client"":{""name"":""Globex Inc"",""region"":""EMEA""},""risk_level"":1}'
                    );";
                cmd.Parameters.AddWithValue("@orgId", orgId);
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
                        DELETE FROM project_note WHERE project_id IN (SELECT id FROM project WHERE name IN ('JsonPath Test Project', 'NullMetadata Project', 'Second Test Project'));
                        DELETE FROM project_milestone WHERE project_id IN (SELECT id FROM project WHERE name IN ('JsonPath Test Project', 'NullMetadata Project', 'Second Test Project'));
                        DELETE FROM project WHERE name IN ('JsonPath Test Project', 'NullMetadata Project', 'Second Test Project');";
                    cmd.ExecuteNonQuery();
                }
            }
            catch { /* best-effort cleanup */ }
        }

        // ?? Query Tests ??

        [TestMethod]
        public void JsonPath_GetById_ExtractsStringValues()
        {
            // Act — Get a ProjectScorecard by ID (uses CreateGetOneOrSelectCommandText)
            var result = _provider.Get<ProjectScorecard>(_testProjectId);

            // Assert — JSON values are extracted
            Assert.IsNotNull(result, "ProjectScorecard should not be null");
            Assert.AreEqual("JsonPath Test Project", result.Name);
            Assert.AreEqual("high", result.Priority);
            Assert.AreEqual("Acme Corp", result.ClientName);
            Assert.AreEqual("NA", result.ClientRegion);

            Debug.WriteLine($"Priority={result.Priority}, ClientName={result.ClientName}, Region={result.ClientRegion}, RiskLevel={result.RiskLevel}");
        }

        [TestMethod]
        public void JsonPath_GetById_ExtractsTypedInt()
        {
            // Act
            var result = _provider.Get<ProjectScorecard>(_testProjectId);

            // Assert — risk_level extracted and cast to int
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.RiskLevel, "RiskLevel should not be null");
            Assert.AreEqual(3, result.RiskLevel.Value);
        }

        [TestMethod]
        public void JsonPath_NullMetadata_ReturnsNullProperties()
        {
            // Act — query for the project with NULL metadata
            var results = _provider.Query<ProjectScorecard>()
                .Where(p => p.Name == "NullMetadata Project")
                .ToList();

            // Assert
            Assert.AreEqual(1, results.Count);
            var result = results[0];
            Assert.IsNull(result.Priority, "Priority should be null when metadata is NULL");
            Assert.IsNull(result.ClientName, "ClientName should be null when metadata is NULL");
            Assert.IsNull(result.ClientRegion, "ClientRegion should be null when metadata is NULL");
            Assert.IsNull(result.RiskLevel, "RiskLevel should be null when metadata is NULL");
        }

        [TestMethod]
        public void JsonPath_QueryAll_ReturnsMultipleWithExtractedValues()
        {
            // Act — get all ProjectScorecard records
            var results = _provider.Query<ProjectScorecard>()
                .Where(p => p.Name == "JsonPath Test Project" || p.Name == "Second Test Project")
                .ToList();

            // Assert
            Assert.IsTrue(results.Count >= 2, $"Expected at least 2 results, got {results.Count}");

            var acme = results.FirstOrDefault(r => r.ClientName == "Acme Corp");
            Assert.IsNotNull(acme, "Should find a project with ClientName 'Acme Corp'");
            Assert.AreEqual("high", acme.Priority);
            Assert.AreEqual(3, acme.RiskLevel);

            var globex = results.FirstOrDefault(r => r.ClientName == "Globex Inc");
            Assert.IsNotNull(globex, "Should find a project with ClientName 'Globex Inc'");
            Assert.AreEqual("low", globex.Priority);
            Assert.AreEqual("EMEA", globex.ClientRegion);
            Assert.AreEqual(1, globex.RiskLevel);
        }

        [TestMethod]
        public void JsonPath_WhereClause_FiltersOnJsonValue()
        {
            // Act — filter using a [JsonPath] property in a Where clause
            var results = _provider.Query<ProjectScorecard>()
                .Where(p => p.Priority == "high")
                .ToList();

            // Assert — at least our test record should match
            Assert.IsTrue(results.Count >= 1, "Should find at least 1 project with priority 'high'");
            Assert.IsTrue(results.All(r => r.Priority == "high"), "All results should have priority 'high'");
        }

        [TestMethod]
        public void JsonPath_WhereClause_FiltersOnNestedJsonValue()
        {
            // Act — filter on a nested JSON value
            var results = _provider.Query<ProjectScorecard>()
                .Where(p => p.ClientRegion == "EMEA")
                .ToList();

            // Assert
            Assert.IsTrue(results.Count >= 1, "Should find at least 1 project with region 'EMEA'");
            var globex = results.FirstOrDefault(r => r.ClientName == "Globex Inc");
            Assert.IsNotNull(globex, "Globex Inc should be in the EMEA results");
        }

        [TestMethod]
        public void JsonPath_BaseProperties_StillWork()
        {
            // Act — verify that base entity properties (from ProjectEntity) still work
            var result = _provider.Get<ProjectScorecard>(_testProjectId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(_testProjectId, result.Id);
            Assert.AreEqual(150000.00m, result.Budget);
            Assert.AreEqual(85, result.Score);
            Assert.IsTrue(result.OrganizationId > 0);
        }

        [TestMethod]
        public void JsonPath_GetList_ReturnsAllWithJsonValues()
        {
            // Act
            var results = _provider.GetList<ProjectScorecard>();

            // Assert — should include our seeded data
            Assert.IsNotNull(results);
            Assert.IsTrue(results.Count >= 3, $"Expected at least 3 results, got {results.Count}");

            // Verify at least one has extracted JSON values
            var withJson = results.FirstOrDefault(r => r.Priority != null);
            Assert.IsNotNull(withJson, "At least one result should have a non-null Priority");
        }
    }
}
