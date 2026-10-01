using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Address;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Country;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.Sqlite.Tests.Domain.Entities.Project;

namespace Funcular.Data.Orm.Sqlite.Tests.QueryOperators
{
    /// <summary>
    /// Harness for the 3.10 query-operator tests (#12/#13) on SQLite, per the plan's §4.1:
    /// <list type="bullet">
    /// <item>each test class gets a fresh temp database from <see cref="SqliteTempDatabase"/>, deleted at class cleanup;</item>
    /// <item>each test seeds its own marker-tagged rows (<c>LastName == marker</c>) and scopes every query by it;</item>
    /// <item>seeded <c>FirstName</c> order equals <c>Id</c> order unless a test says otherwise;</item>
    /// <item>"nothing executed" means the log stays empty after the <see cref="IQueryable"/> is obtained;</item>
    /// <item>SQL-shape asserts normalize whitespace (SQLite emits <c>LIMIT</c>/<c>OFFSET</c> on separate lines);</item>
    /// <item>cleanup deletes run in a transaction.</item>
    /// </list>
    /// </summary>
    public abstract class SqliteQueryOperatorTestBase
    {
        private static readonly ConcurrentDictionary<Type, string> DatabasePaths = new ConcurrentDictionary<Type, string>();

        private readonly List<string> _markers = new List<string>();
        private readonly List<(int CountryId, int AddressId, int OrganizationId)> _employers = new List<(int, int, int)>();

        protected string _connectionString;
        protected SqliteOrmDataProvider _provider;
        protected readonly StringBuilder _sb = new StringBuilder();

        /// <summary>The base table of the person entities, used in qualified-SQL asserts.</summary>
        protected const string PersonTable = "person";

        [TestInitialize]
        public void Setup()
        {
            _sb.Clear();
            var path = DatabasePaths.GetOrAdd(GetType(), t => SqliteTempDatabase.Create("funky_sqlite_q310_" + t.Name));
            _connectionString = $"Data Source={path}";
            _provider = new SqliteOrmDataProvider(_connectionString)
            {
                Log = s =>
                {
                    Debug.WriteLine(s);
                    _sb.AppendLine(s);
                }
            };
        }

        /// <summary>
        /// Deletes only <paramref name="testClass"/>'s database: another class may still be using its own. Each derived
        /// class calls this from its own <c>[ClassCleanup]</c> with <c>typeof</c> itself. An inherited cleanup can't tell
        /// the classes apart: MSTest runs the inherited cleanups together at the end of the assembly, all with the last
        /// test's <see cref="TestContext"/>.
        /// </summary>
        protected static void DeleteClassDatabase(Type testClass)
        {
            if (DatabasePaths.TryRemove(testClass, out var path))
                SqliteTempDatabase.Delete(path);
        }

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

        /// <summary>Seeds a project (for computed members such as <c>EffectiveScore = COALESCE(score, 0)</c>).</summary>
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
            try
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
            finally
            {
                _provider?.Dispose();
            }
        }

        #region SQL capture

        protected static string Normalize(string sql) => Regex.Replace(sql ?? string.Empty, @"\s+", " ").Trim();

        /// <summary>The normalized SQL logged since the last <see cref="ClearLog"/>.</summary>
        protected string Sql => Normalize(_sb.ToString());

        protected void ClearLog() => _sb.Clear();
        /// <summary>
        /// Asserts every parameter logged since the last <see cref="ClearLog"/> is referenced by the command it was sent
        /// with: no command carries parameters it doesn't use.
        /// </summary>
        protected void AssertEveryParameterReferenced()
        {
            var command = new StringBuilder();
            var inParameters = false;
            foreach (var line in _sb.ToString().Split('\n'))
            {
                var parameter = Regex.Match(line.TrimStart(), @"^(@p__linq__\d+): ");
                if (!parameter.Success)
                {
                    if (inParameters)
                    {
                        command.Clear();
                        inParameters = false;
                    }
                    command.AppendLine(line);
                    continue;
                }
                inParameters = true;
                var name = parameter.Groups[1].Value;
                Assert.IsTrue(Regex.IsMatch(command.ToString(), Regex.Escape(name) + @"(?!\d)"),
                    $"{name} was sent with a command that doesn't use it: {Normalize(command.ToString())}");
            }
        }

        /// <summary>The logged command texts since the last <see cref="ClearLog"/>, without the parameter-value lines.</summary>
        protected string CommandTexts() => Normalize(string.Join("\n", _sb.ToString().Split('\n')
            .Where(line => !Regex.IsMatch(line.TrimStart(), @"^@p__linq__\d+: "))));

        /// <summary>
        /// The top-level ORDER BY list of the last logged command (normalized, without the keyword), or null. Takes
        /// the LAST <c>ORDER BY</c>: computed members can carry their own inside subqueries in the SELECT list.
        /// </summary>
        protected string OrderByList()
        {
            var sql = Sql;
            var at = sql.LastIndexOf("ORDER BY ", StringComparison.Ordinal);
            if (at < 0)
                return null;
            var match = Regex.Match(sql.Substring(at), @"^ORDER BY (.+?)(?: LIMIT | OFFSET | @p__linq__\d+: |$)");
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
