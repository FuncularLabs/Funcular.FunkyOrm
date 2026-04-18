using System.Diagnostics;
using System.Text;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Project;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    /// <summary>
    /// Integration tests for Phases 2–4: [SqlExpression], [SubqueryAggregate], [JsonCollection] on PostgreSQL.
    /// Mirrors <c>ComputedAttributeIntegrationTests</c> from the SQL Server test project.
    /// </summary>
    [TestClass]
    public class PostgreSqlComputedAttributeIntegrationTests
    {
        private string _connectionString;
        private PostgreSqlOrmDataProvider _provider;
        private readonly StringBuilder _sb = new();
        private int _testProjectId;
        private int _emptyProjectId;

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
                    END $$;

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
                    SELECT 'PgComputedTest Org'
                    WHERE NOT EXISTS (SELECT 1 FROM organization WHERE name = 'PgComputedTest Org');";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "SELECT id FROM organization WHERE name = 'PgComputedTest Org';";
                var orgId = (int)cmd.ExecuteScalar();

                // Project with score and metadata
                cmd.CommandText = @"
                    INSERT INTO project (name, organization_id, budget, score, metadata)
                    VALUES (
                        'PgComputedTest Project', @orgId, 150000.00, 85,
                        '{""priority"":""high"",""client"":{""name"":""Acme Corp"",""region"":""NA""},""risk_level"":3}'
                    )
                    RETURNING id;";
                cmd.Parameters.AddWithValue("@orgId", orgId);
                _testProjectId = (int)cmd.ExecuteScalar();

                // Empty project (no milestones/notes, null score)
                cmd.Parameters.Clear();
                cmd.CommandText = @"
                    INSERT INTO project (name, organization_id, score)
                    VALUES ('PgEmptyProject', @orgId, NULL)
                    RETURNING id;";
                cmd.Parameters.AddWithValue("@orgId", orgId);
                _emptyProjectId = (int)cmd.ExecuteScalar();

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
        }

        private void CleanupTestData()
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    DELETE FROM project_note WHERE project_id IN (SELECT id FROM project WHERE name IN ('PgComputedTest Project', 'PgEmptyProject'));
                    DELETE FROM project_milestone WHERE project_id IN (SELECT id FROM project WHERE name IN ('PgComputedTest Project', 'PgEmptyProject'));
                    DELETE FROM project WHERE name IN ('PgComputedTest Project', 'PgEmptyProject');";
                cmd.ExecuteNonQuery();
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
            Assert.AreEqual(85, result.EffectiveScore);
        }

        [TestMethod]
        public void SqlExpression_CoalesceScore_ReturnsZeroWhenNull()
        {
            var result = _provider.Get<ProjectScorecardFull>(_emptyProjectId);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.EffectiveScore);
        }

        // ???????????????????????????????????????????????????????????
        // Phase 3: [SubqueryAggregate] Tests
        // ???????????????????????????????????????????????????????????

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
                .Where(p => p.Name == "PgComputedTest Project" || p.Name == "PgEmptyProject")
                .ToList();

            Assert.IsTrue(results.Count >= 2, $"Expected at least 2 results, got {results.Count}");

            var full = results.FirstOrDefault(r => r.Name == "PgComputedTest Project");
            Assert.IsNotNull(full);
            Assert.AreEqual(5, full.MilestoneCount);
            Assert.AreEqual(4, full.NoteCount);

            var empty = results.FirstOrDefault(r => r.Name == "PgEmptyProject");
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
            Assert.IsTrue(result.MilestonesJson.Contains("Requirements"));
            Assert.IsTrue(result.MilestonesJson.Contains("completed"));
            Assert.IsTrue(result.MilestonesJson.Contains("pending"));

            Debug.WriteLine($"MilestonesJson: {result.MilestonesJson}");
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

            Debug.WriteLine($"NotesJson: {result.NotesJson}");
        }

        [TestMethod]
        public void JsonCollection_EmptyProject_ReturnsNull()
        {
            var result = _provider.Get<ProjectScorecardFull>(_emptyProjectId);

            Assert.IsNotNull(result);
            Assert.IsNull(result.MilestonesJson);
            Assert.IsNull(result.NotesJson);
        }

        // ???????????????????????????????????????????????????????????
        // Combined: All attributes work together
        // ???????????????????????????????????????????????????????????

        [TestMethod]
        public void AllAttributes_CombinedOnSingleEntity_WorkTogether()
        {
            var result = _provider.Get<ProjectScorecardFull>(_testProjectId);

            Assert.IsNotNull(result);

            // Base entity
            Assert.AreEqual("PgComputedTest Project", result.Name);
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
                .Where(p => p.Name == "PgComputedTest Project" || p.Name == "PgEmptyProject")
                .ToList();

            Assert.IsTrue(results.Count >= 2);

            foreach (var r in results)
            {
                Debug.WriteLine($"{r.Name}: Score={r.Score}, EffScore={r.EffectiveScore}, Milestones={r.MilestoneCount}, Notes={r.NoteCount}");
            }
        }

        #region DatabaseGenerated / Computed Column Tests

        [TestMethod]
        public void Insert_EntityWithComputedColumn_Succeeds()
        {
            var org = new OrganizationEntity
            {
                Name = $"ComputedTest-{Guid.NewGuid():N}"
            };

            var result = _provider.Insert(org);

            Assert.IsNotNull(result);
            Assert.IsTrue((int)result > 0);

            var reloaded = _provider.Get<OrganizationEntity>((int)result);
            Assert.IsNotNull(reloaded);
            Assert.AreEqual(org.Name, reloaded.Name);
            Assert.IsNotNull(reloaded.NameLength, "Computed NameLength should be populated by the database.");
            Assert.AreEqual(org.Name.Length, reloaded.NameLength.Value);
        }

        [TestMethod]
        public void Update_EntityWithComputedColumn_Succeeds()
        {
            var org = new OrganizationEntity
            {
                Name = $"ComputedUpdate-{Guid.NewGuid():N}"
            };
            var insertedId = (int)_provider.Insert(org);

            var loaded = _provider.Get<OrganizationEntity>(insertedId);
            Assert.IsNotNull(loaded);
            var originalNameLength = loaded.NameLength;

            var newName = "Short";
            loaded.Name = newName;
            var updated = _provider.Update(loaded);

            Assert.IsNotNull(updated);

            var reloaded = _provider.Get<OrganizationEntity>(insertedId);
            Assert.IsNotNull(reloaded);
            Assert.AreEqual(newName, reloaded.Name);
            Assert.AreEqual(newName.Length, reloaded.NameLength.Value);
            Assert.AreNotEqual(originalNameLength, reloaded.NameLength, "Computed column should reflect updated name.");
        }

        [TestMethod]
        public async Task InsertAsync_EntityWithComputedColumn_Succeeds()
        {
            var org = new OrganizationEntity
            {
                Name = $"ComputedAsyncTest-{Guid.NewGuid():N}"
            };

            var result = await _provider.InsertAsync(org);

            Assert.IsNotNull(result);
            Assert.IsTrue((int)result > 0);

            var reloaded = await _provider.GetAsync<OrganizationEntity>((int)result);
            Assert.IsNotNull(reloaded);
            Assert.AreEqual(org.Name, reloaded.Name);
            Assert.IsNotNull(reloaded.NameLength);
        }

        #endregion
    }
}
