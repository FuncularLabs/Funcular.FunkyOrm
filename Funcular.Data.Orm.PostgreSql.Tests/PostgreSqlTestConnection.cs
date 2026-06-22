using System;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    /// <summary>
    /// Resolves the PostgreSQL connection string used by the integration tests, in priority order:
    /// <list type="number">
    ///   <item><c>FUNKY_PG_CONNECTION</c> — a full connection string (used by CI and for full control).</item>
    ///   <item><c>postgres_user</c> + <c>postgres_pwd</c> — when both are set, builds a connection to a
    ///     local instance (<c>Host=localhost;Port=5432;Database=funky_db</c>) with those credentials.
    ///     This is the convenient local-developer path: set the two variables, run a local PostgreSQL
    ///     with a <c>funky_db</c> database, and the integration tests run against it.</item>
    ///   <item>The legacy <c>funky_user</c>/<c>funky_password</c> default (the Docker compose credentials).</item>
    /// </list>
    /// Environment variables are read from the process first, then — on Windows — from the Machine and
    /// User registry scopes, so a value set while an IDE was already running is still picked up without
    /// a restart.
    /// </summary>
    public static class PostgreSqlTestConnection
    {
        private const string DefaultConnectionString =
            "Host=localhost;Port=5432;Database=funky_db;Username=funky_user;Password=funky_password";

        public static string Resolve()
        {
            var explicitConnection = GetEnvironmentVariable("FUNKY_PG_CONNECTION");
            if (!string.IsNullOrWhiteSpace(explicitConnection))
                return explicitConnection;

            var user = GetEnvironmentVariable("postgres_user");
            var password = GetEnvironmentVariable("postgres_pwd");
            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(password))
                return $"Host=localhost;Port=5432;Database=funky_db;Username={user};Password={password}";

            return DefaultConnectionString;
        }

        private static string GetEnvironmentVariable(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;

            if (OperatingSystem.IsWindows())
            {
                value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
                if (!string.IsNullOrWhiteSpace(value)) return value;
                value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
            }
            return value;
        }
    }
}
