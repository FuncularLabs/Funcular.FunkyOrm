using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Address;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Project;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Tests.QueryOperators
{
    /// <summary>
    /// Harness for the 3.10 query-operator tests (#12/#13), per the plan's §4.1:
    /// <list type="bullet">
    /// <item>each test seeds its own marker-tagged rows (<c>LastName == marker</c>) and scopes every query by it;</item>
    /// <item>seeded <c>FirstName</c> order equals <c>Id</c> order unless a test says otherwise;</item>
    /// <item>"nothing executed" means the log stays empty after the <see cref="IQueryable"/> is obtained;</item>
    /// <item>SQL-shape asserts normalize whitespace (PostgreSQL emits <c>LIMIT</c>/<c>OFFSET</c> on separate lines);</item>
    /// <item>cleanup deletes run in a transaction (PostgreSQL's <c>Delete</c> requires one).</item>
    /// </list>
    /// </summary>
    public abstract class PostgreSqlQueryOperatorTestBase : PostgreSqlTestFixture
    {
        private readonly List<string> _markers = new List<string>();
        private readonly List<(int CountryId, int AddressId, int OrganizationId)> _employers = new List<(int, int, int)>();

        /// <summary>The base table of the person entities, used in qualified-SQL asserts.</summary>
        protected const string PersonTable = "person";

        protected string NewMarker()
        {
            var marker = "q310_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            _markers.Add(marker);
            return marker;
        }

        /// <summary>Seeds Country → Address → Organization; returns the organization id.</summary>
        protected int SeedEmployer(string countryName)
        {
            var country = new CountryEntity { Name = countryName };
            _provider.Insert(country);
            var address = new AddressEntity { Line1 = "1 Q310 St", City = "Q310", StateCode = "NY", PostalCode = "10001", CountryId = country.Id };
            _provider.Insert(address);
            var org = new OrganizationEntity { Name = "Q310Org_" + Guid.NewGuid().ToString("N").Substring(0, 8), HeadquartersAddressId = address.Id };
            _provider.Insert(org);
            _employers.Add((country.Id, address.Id, org.Id));
            return org.Id;
        }

        protected int SeedPerson(string marker, string firstName, int? employerId = null, string middleInitial = "M",
            string gender = "X")
        {
            var person = new PersonEntity
            {
                FirstName = firstName,
                LastName = marker,
                MiddleInitial = middleInitial,
                Gender = gender,
                EmployerId = employerId,
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow
            };
            _provider.Insert(person);
            return person.Id;
        }

        private readonly List<string> _projectMarkers = new List<string>();

        /// <summary>
        /// Seeds a project (for computed members such as <c>EffectiveScore = COALESCE(score, 0)</c>). Call
        /// <see cref="EnsureProjectTables"/> first.
        /// </summary>
        protected int SeedProject(string marker, int organizationId, int? score)
        {
            if (!_projectMarkers.Contains(marker))
                _projectMarkers.Add(marker);
            var project = new ProjectEntity
            {
                Name = marker,
                OrganizationId = organizationId,
                Score = score,
                DateUtcCreated = DateTime.UtcNow,
                DateUtcModified = DateTime.UtcNow
            };
            _provider.Insert(project);
            return project.Id;
        }

        /// <summary>
        /// The PostgreSQL fixture doesn't create the project tables (the SQL Server fixture does); mirror the idempotent
        /// DDL of <c>PostgreSqlComputedAttributeIntegrationTests</c> so these rows don't depend on test order.
        /// </summary>
        protected void EnsureProjectTables()
        {
            using (var connection = new NpgsqlConnection(_connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS project (
                            id SERIAL PRIMARY KEY, name VARCHAR(200) NOT NULL, organization_id INT NOT NULL, lead_id INT NULL,
                            category_id INT NULL, budget NUMERIC(12,2) NULL, score INT NULL, metadata JSONB NULL,
                            dateutc_created TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
                            dateutc_modified TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'));
                        CREATE TABLE IF NOT EXISTS project_milestone (
                            id SERIAL PRIMARY KEY, project_id INT NOT NULL, title VARCHAR(200) NOT NULL,
                            status VARCHAR(50) NOT NULL DEFAULT 'pending', due_date DATE NULL, completed_date DATE NULL);
                        CREATE TABLE IF NOT EXISTS project_note (
                            id SERIAL PRIMARY KEY, project_id INT NOT NULL, author_id INT NULL, content TEXT NOT NULL,
                            category VARCHAR(50) NOT NULL DEFAULT 'general',
                            dateutc_created TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'));";
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>Seeds people in order (ascending ids), all under one employer.</summary>
        protected List<int> SeedPeople(string marker, int? employerId, params string[] firstNames) =>
            firstNames.Select(f => SeedPerson(marker, f, employerId)).ToList();

        /// <summary>The standard #12 seed: one employer, people "a", "b", "c" in ascending id order.</summary>
        protected (string Marker, List<int> Ids) SeedAbc()
        {
            var marker = NewMarker();
            var employer = SeedEmployer("Q310Country_" + marker);
            return (marker, SeedPeople(marker, employer, "a", "b", "c"));
        }

        protected IQueryable<PersonDetailEntity> People(string marker) =>
            _provider.Query<PersonDetailEntity>().Where(p => p.LastName == marker);

        [TestCleanup]
        public void DeleteSeededRows()
        {
            if (_markers.Count == 0 && _employers.Count == 0 && _projectMarkers.Count == 0)
                return;
            _provider.BeginTransaction();
            try
            {
                foreach (var marker in _projectMarkers)
                    _provider.Delete<ProjectEntity>(p => p.Name == marker);
                foreach (var marker in _markers)
                    _provider.Delete<PersonEntity>(p => p.LastName == marker);
                foreach (var (countryId, addressId, organizationId) in _employers)
                {
                    _provider.Delete<OrganizationEntity>(organizationId);
                    _provider.Delete<AddressEntity>(addressId);
                    _provider.Delete<CountryEntity>(countryId);
                }
                _provider.CommitTransaction();
            }
            catch
            {
                _provider.RollbackTransaction();
                throw;
            }
        }

        #region SQL capture

        protected static string Normalize(string sql) => Regex.Replace(sql ?? string.Empty, @"\s+", " ").Trim();

        /// <summary>The normalized SQL logged since the last <see cref="ClearLog"/>.</summary>
        protected string Sql => Normalize(_sb.ToString());

        protected void ClearLog() => _sb.Clear();

        /// <summary>
        /// The top-level ORDER BY list of the last logged command (normalized, without the keyword), or null. Takes
        /// the LAST <c>ORDER BY</c>: computed members can carry their own inside subqueries in the SELECT list
        /// (PostgreSQL's <c>[JsonCollection]</c> subqueries do). Stops at <c>LIMIT</c>/<c>OFFSET</c> or the logged
        /// parameters.
        /// </summary>
        protected string OrderByList()
        {
            var sql = Sql;
            var at = sql.LastIndexOf("ORDER BY ", StringComparison.Ordinal);
            if (at < 0)
                return null;
            var match = Regex.Match(sql.Substring(at), @"^ORDER BY (.+?)(?: LIMIT | OFFSET | @p__linq__|$)");
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        /// <summary>Asserts <paramref name="act"/> executes no SQL. Obtain the IQueryable before calling.</summary>
        protected void AssertNoQuery(Action act)
        {
            ClearLog();
            act();
            Assert.AreEqual(string.Empty, Sql, "no query or aggregate command may be executed");
        }

        /// <summary>Asserts <paramref name="act"/> throws exactly <typeparamref name="TException"/> and executes no SQL.</summary>
        protected TException AssertThrowsNoQuery<TException>(Action act) where TException : Exception
        {
            ClearLog();
            var ex = Assert.ThrowsException<TException>(act);
            Assert.AreEqual(string.Empty, Sql, "no query or aggregate command may be executed before rejection");
            return ex;
        }

        #endregion

        #region Oracle

        /// <summary>
        /// Runs <paramref name="shape"/> against a fresh <c>Query&lt;PersonDetailEntity&gt;()</c> scoped to the marker, and
        /// against the marker's rows materialized and ordered by id (LINQ to objects), then compares by id/value, or by
        /// exception type. Shapes must have a total order where order matters (§4.1).
        /// </summary>
        protected void AssertMatchesOracle<TResult>(string marker, Func<IQueryable<PersonDetailEntity>, TResult> shape, string because = null)
        {
            var oracleRows = _provider.Query<PersonDetailEntity>().Where(p => p.LastName == marker).ToList();
            Assert.IsTrue(oracleRows.Count > 0, "oracle seed must not be empty");
            var oracleSource = oracleRows.OrderBy(p => p.Id).AsQueryable();

            var expected = Evaluate(() => shape(oracleSource));
            var actual = Evaluate(() => shape(_provider.Query<PersonDetailEntity>().Where(p => p.LastName == marker)));
            AssertSameOutcome(expected, actual, because);
        }

        /// <summary>
        /// Compares a shape over a converted source with the same shape over the concrete query (both against the
        /// database): for behavior 3.10.0 leaves alone, independent of the in-memory oracle (§4.2 AC13-4).
        /// </summary>
        /// <param name="requireSuccess">The concrete shape must not throw. Without it, a row is vacuously green when both
        /// sides throw the same exception (e.g. #12's ambiguous <c>id</c>, or an unimplemented operator).</param>
        protected void AssertSameOutcome<TResult>(Func<TResult> concrete, Func<TResult> converted, string because = null,
            bool requireSuccess = false)
        {
            var expected = Evaluate(concrete);
            if (requireSuccess && expected.Error != null)
                Assert.Fail($"{because}: the concrete shape must succeed, but threw {expected.Error.GetType().Name}: {expected.Error.Message}");
            AssertSameOutcome(expected, Evaluate(converted), because);
        }

        private static (string Value, Exception Error) Evaluate<TResult>(Func<TResult> run)
        {
            try
            {
                return (Canon(run()), null);
            }
            catch (Exception ex)
            {
                return (null, ex);
            }
        }

        private static void AssertSameOutcome((string Value, Exception Error) expected, (string Value, Exception Error) actual, string because)
        {
            var prefix = string.IsNullOrEmpty(because) ? "" : because + ": ";
            if (expected.Error != null)
            {
                Assert.IsNotNull(actual.Error, $"{prefix}expected {expected.Error.GetType().Name} ({expected.Error.Message}) but got {actual.Value}");
                Assert.AreEqual(expected.Error.GetType(), actual.Error.GetType(), $"{prefix}exception type ({actual.Error.Message})");
                return;
            }
            if (actual.Error != null)
                Assert.Fail($"{prefix}expected {expected.Value} but threw {actual.Error.GetType().Name}: {actual.Error.Message}");
            Assert.AreEqual(expected.Value, actual.Value, prefix + "result differs from the oracle");
        }

        /// <summary>Canonical text of a result: entities by id|first|last, sequences element-wise, scalars invariant.</summary>
        protected static string Canon(object value)
        {
            switch (value)
            {
                case null:
                    return "<null>";
                case PersonEntity e:
                    return $"{e.Id}|{e.FirstName}|{e.LastName}";
                case string s:
                    return "\"" + s + "\"";
                case IEnumerable sequence:
                    return "[" + string.Join(", ", sequence.Cast<object>().Select(Canon)) + "]";
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) + ":" + value.GetType().Name;
            }
        }

        #endregion
    }
}
