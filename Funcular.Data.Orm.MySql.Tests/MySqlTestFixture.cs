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
            var raw = Environment.GetEnvironmentVariable("FUNKY_MYSQL_CONNECTION")
                ?? "Server=localhost;Port=3306;Database=funky_db;User ID=root;Password=root;";
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

        protected void DisposeProvider() => _provider?.Dispose();
    }
}
