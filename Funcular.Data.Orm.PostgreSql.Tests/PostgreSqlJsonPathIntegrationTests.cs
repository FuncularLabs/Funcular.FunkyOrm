using System.Diagnostics;
using System.Text;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Project;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    /// <summary>
    /// Integration tests for Phase 1: [JsonPath] on PostgreSQL.
    /// Mirrors <c>JsonPathIntegrationTests</c> from the SQL Server test project.
    /// </summary>
    [TestClass]
    public class PostgreSqlJsonPathIntegrationTests
    {
        private string _connectionString;
        private PostgreSqlOrmDataProvider _provider;
        private readonly StringBuilder _sb = new();
        private int _testProjectId;

        [TestInitialize]
        public void Setup()
        {
            _sb.Clear();
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_PG_CONNECTION") ??
                "Host=localhost;Port=5432;Database=funky_db;Username=funky_user;Password=funky_password";

            TestConnection();
            EnsureSchema();
            SeedTestData();

            _provider = new PostgreSqlOrmDataProvider(_connectionString)
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

        private void TestConnection()
        {
            using (var connection = new NpgsqlConnection(_connectionString))
            {
                try
                {
                    connection.Open();
                }
                catch (NpgsqlException ex)
                {
                    Assert.Inconclusive(
                        $"PostgreSQL not available. Start Docker: docker compose -f Database/PostgreSql/docker-compose.yml up -d\n{ex.Message}");
                }
            }
        }

        private void EnsureSchema()
        {
            using (var connection = new NpgsqlConnection(_connectionString))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS project_category (
                        id SERIAL PRIMARY KEY,
                        name VARCHAR(100) NOT NULL,
                        code VARCHAR(50) NOT NULL
                    );

                    INSERT INTO project_category (name, code)
                    SELECT 'Internal Tooling', 'internal'
                    WHERE NOT EXISTS (SELECT 1 FROM project_category WHERE code = 'internal');

                    CREATE TABLE IF NOT EXISTS project (
                        id SERIAL PRIMARY KEY,
                        name VARCHAR(200) NOT NULL,
                        organization_id INT NOT NULL,
                        lead_id INT NULL,
                        category_id INT NULL,
                        budget NUMERIC(12,2) NULL,
                        score INT NULL,
                        metadata JSONB NULL,
                        dateutc_created TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
                        dateutc_modified TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc')
                    );

                    CREATE TABLE IF NOT EXISTS project_milestone (
                        id SERIAL PRIMARY KEY,
                        project_id INT NOT NULL,
                        title VARCHAR(200) NOT NULL,
                        status VARCHAR(50) NOT NULL DEFAULT 'pending',
                        due_date DATE NULL,
                        completed_date DATE NULL
                    );

                    CREATE TABLE IF NOT EXISTS project_note (
                        id SERIAL PRIMARY KEY,
                        project_id INT NOT NULL,
                        author_id INT NULL,
                        content TEXT NOT NULL,
                        category VARCHAR(50) NOT NULL DEFAULT 'general',
                        dateutc_created TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc')
                    );

                    -- Ensure metadata column is JSONB (may have been created as TEXT in earlier schema)
                    DO $$ BEGIN
                        ALTER TABLE project ALTER COLUMN metadata TYPE JSONB USING metadata::jsonb;
                    EXCEPTION WHEN others THEN NULL;
                    END $$;

                    CREATE TABLE IF NOT EXISTS organization (
                        id SERIAL PRIMARY KEY,
                        name VARCHAR(100) NOT NULL,
                        headquarters_address_id INT NULL,
                        name_length INT GENERATED ALWAYS AS (LENGTH(name)) STORED
                    );

                    -- Add generated column to existing tables that lack it
                    DO $$ BEGIN
                        ALTER TABLE organization ADD COLUMN name_length INT GENERATED ALWAYS AS (LENGTH(name)) STORED;
                    EXCEPTION WHEN duplicate_column THEN NULL;
                    END $$;";
                cmd.ExecuteNonQuery();
            }
        }

        private void SeedTestData()
        {
            using (var connection = new NpgsqlConnection(_connectionString))
            {
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO organization (name)
                    SELECT 'PgJsonTest Org'
                    WHERE NOT EXISTS (SELECT 1 FROM organization WHERE name = 'PgJsonTest Org');";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "SELECT id FROM organization WHERE name = 'PgJsonTest Org';";
                var orgId = (int)cmd.ExecuteScalar();

                cmd.CommandText = @"
                    INSERT INTO project (name, organization_id, budget, score, metadata)
                    VALUES (
                        @name, @orgId, @budget, @score,
                        '{""priority"":""high"",""tags"":[""api"",""backend""],""client"":{""name"":""Acme Corp"",""region"":""NA""},""risk_level"":3}'
                    )
                    RETURNING id;";
                cmd.Parameters.AddWithValue("@name", "PgJsonPath Test Project");
                cmd.Parameters.AddWithValue("@orgId", orgId);
                cmd.Parameters.AddWithValue("@budget", 150000.00m);
                cmd.Parameters.AddWithValue("@score", 85);
                _testProjectId = (int)cmd.ExecuteScalar();

                cmd.Parameters.Clear();
                cmd.CommandText = @"
                    INSERT INTO project (name, organization_id, budget, score, metadata)
                    VALUES ('PgNullMetadata Project', @orgId, 50000.00, 60, NULL);";
                cmd.Parameters.AddWithValue("@orgId", orgId);
                cmd.ExecuteNonQuery();

                cmd.Parameters.Clear();
                cmd.CommandText = @"
                    INSERT INTO project (name, organization_id, budget, score, metadata)
                    VALUES (
                        'PgSecond Test Project', @orgId, 200000.00, NULL,
                        '{""priority"":""low"",""tags"":[""frontend""],""client"":{""name"":""Globex Inc"",""region"":""EMEA""},""risk_level"":1}'
                    );";
                cmd.Parameters.AddWithValue("@orgId", orgId);
                cmd.ExecuteNonQuery();
            }
        }

        private void CleanupTestData()
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    DELETE FROM project_note WHERE project_id IN (SELECT id FROM project WHERE name LIKE 'Pg%');
                    DELETE FROM project_milestone WHERE project_id IN (SELECT id FROM project WHERE name LIKE 'Pg%');
                    DELETE FROM project WHERE name LIKE 'Pg%';";
                cmd.ExecuteNonQuery();
            }
            catch { /* best-effort cleanup */ }
        }

        [TestMethod]
        public void JsonPath_GetById_ExtractsStringValues()
        {
            var result = _provider.Get<ProjectScorecard>(_testProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual("PgJsonPath Test Project", result.Name);
            Assert.AreEqual("high", result.Priority);
            Assert.AreEqual("Acme Corp", result.ClientName);
            Assert.AreEqual("NA", result.ClientRegion);

            Debug.WriteLine($"Priority={result.Priority}, ClientName={result.ClientName}, Region={result.ClientRegion}, RiskLevel={result.RiskLevel}");
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
                .Where(p => p.Name == "PgNullMetadata Project")
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
                .Where(p => p.Name == "PgJsonPath Test Project" || p.Name == "PgSecond Test Project")
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
