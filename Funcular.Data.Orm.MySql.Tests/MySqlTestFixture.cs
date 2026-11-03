using System;
using System.Text;
using MySqlConnector;
using Funcular.Data.Orm.MySql;
using Funcular.Data.Orm.Tests.Shared;

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>
    /// Shared base for MySQL integration tests. Reads the connection from FUNKY_MYSQL_CONNECTION
    /// (forcing GuidFormat=Char36 + AllowUserVariables so Guid columns round-trip).
    /// Tests use unique markers for isolation rather than a wrapping transaction (the provider's
    /// Update internally opens a nested ConnectionScope, which the transactional-concurrency guard
    /// disallows). The schema is pre-loaded via Database/MySql/integration_test_db.sql (CI workflow
    /// does this; locally it is created once). NOT a [TestClass] to avoid inherited-test duplication.
    /// </summary>
    /// <remarks>
    /// Each test runs with a <see cref="TestRowLedger"/>, begun by <see cref="InitProvider"/>: a test that inserts
    /// into person or a related table passes each inserted entity to <see cref="Track"/>, and
    /// <see cref="DisposeProvider"/> deletes those rows and fails the test if it left the guarded tables' row counts
    /// changed.
    /// </remarks>
    public abstract class MySqlTestFixture
    {
        protected string _connectionString;
        protected MySqlOrmDataProvider _provider;
        protected readonly StringBuilder _sb = new StringBuilder();
        protected TestRowLedger _rows;

        protected void InitProvider()
        {
            var raw = ResolveConnectionString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                Assert.Inconclusive(
                    "FUNKY_MYSQL_CONNECTION is not set. Set it (Machine or User scope), e.g. " +
                    "\"Server=localhost;Port=3306;Database=funky_db;User ID=funky;Password=...;GuidFormat=Char36\". " +
                    "If you set a Machine-scoped variable while Visual Studio was open, the value is read " +
                    "from the registry here so a restart should not be required.");
            }

            var builder = new MySqlConnectionStringBuilder(raw)
            {
                GuidFormat = MySqlGuidFormat.Char36,
                AllowUserVariables = true
            };
            if (string.IsNullOrEmpty(builder.Database)) builder.Database = "funky_db";
            _connectionString = builder.ConnectionString;

            _provider = new MySqlOrmDataProvider(_connectionString)
            {
                Log = s => { _sb.AppendLine(s); }
            };
            _rows = TestRowLedger.Begin(() => new MySqlConnection(_connectionString), TestRowLedger.MySqlTables);
        }

        /// <summary>
        /// Resolves the connection string from the process environment first (CI job env / a freshly
        /// started shell), then — on Windows — from the Machine and User registry scopes. The registry
        /// read means a Visual Studio instance that was already running when a Machine-scoped
        /// FUNKY_MYSQL_CONNECTION was created still picks it up, without requiring a restart.
        /// Returns null/empty when the variable is not set anywhere.
        /// </summary>
        private static string ResolveConnectionString()
        {
            var value = Environment.GetEnvironmentVariable("FUNKY_MYSQL_CONNECTION");
            if (!string.IsNullOrWhiteSpace(value)) return value;

            if (OperatingSystem.IsWindows())
            {
                value = Environment.GetEnvironmentVariable("FUNKY_MYSQL_CONNECTION", EnvironmentVariableTarget.Machine);
                if (!string.IsNullOrWhiteSpace(value)) return value;
                value = Environment.GetEnvironmentVariable("FUNKY_MYSQL_CONNECTION", EnvironmentVariableTarget.User);
            }
            return value;
        }

        protected void DisposeProvider()
        {
            // Dispose first: it rolls back a transaction a failed test left open, so the deletes don't wait on it.
            _provider?.Dispose();
            _rows?.Finish();
        }

        /// <summary>Records a row this test inserted, for <see cref="DisposeProvider"/> to delete.</summary>
        protected void Track(object entity) => _rows.Track(entity);

        /// <summary>Inserts an entity built inline and tracks it.</summary>
        protected void InsertTracked<T>(T entity) where T : class, new()
        {
            _provider.Insert(entity);
            Track(entity);
        }
    }
}
