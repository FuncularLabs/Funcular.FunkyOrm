using System;
using System.Text;
using MySqlConnector;
using Funcular.Data.Orm.MySql;

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
    public abstract class MySqlTestFixture
    {
        protected string _connectionString;
        protected MySqlOrmDataProvider _provider;
        protected readonly StringBuilder _sb = new StringBuilder();

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

        protected void DisposeProvider() => _provider?.Dispose();
    }
}
