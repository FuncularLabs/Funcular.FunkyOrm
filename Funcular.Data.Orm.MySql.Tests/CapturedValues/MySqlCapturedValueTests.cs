using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Funcular.Data.Orm.MySql.Visitors;
using Funcular.Data.Orm.Tests.CapturedValues;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql.Tests.CapturedValues
{
    /// <summary>The direct translator rows on MySQL (docs/plans/WHERE_CAPTURED_VALUES_PLAN.md §4.1 item 2).</summary>
    [TestClass]
    public class MySqlTranslatorDirectTests : TranslatorDirectHarness
    {
        protected override Translation TranslateMethodCall(MethodCallExpression node, Func<PropertyInfo, string> getColumnName)
        {
            var sql = new StringBuilder();
            var parameters = new List<MySqlParameter>();
            new MySqlExpressionTranslator(new MySqlParameterGenerator()).TranslateMethodCall(node, sql, parameters, getColumnName);
            return new Translation(sql.ToString(),
                parameters.Select(p => new KeyValuePair<string, object>(p.ParameterName, p.Value)).ToList());
        }

        protected override Translation TranslateDateMember(MemberExpression node, Func<PropertyInfo, string> getColumnName)
        {
            var sql = new StringBuilder();
            var parameters = new List<MySqlParameter>();
            new MySqlExpressionTranslator(new MySqlParameterGenerator()).TranslateDateMember(node, sql, parameters, getColumnName);
            return new Translation(sql.ToString(),
                parameters.Select(p => new KeyValuePair<string, object>(p.ParameterName, p.Value)).ToList());
        }

        protected override Translation TranslateMemberOfMember(MemberExpression node, Func<PropertyInfo, string> getColumnName)
        {
            var sql = new StringBuilder();
            new MySqlExpressionTranslator(new MySqlParameterGenerator()).TranslateMemberOfMember(node, sql, getColumnName);
            return new Translation(sql.ToString(), new List<KeyValuePair<string, object>>());
        }

        protected override string LengthSql(string column) => "CHAR_LENGTH(" + column + ")";
        protected override string StartsWithPattern(string parameterName) => "CONCAT(" + parameterName + ", '%')";
        protected override string EndsWithPattern(string parameterName) => "CONCAT('%', " + parameterName + ")";
        protected override string YearSql(string column) => "EXTRACT(YEAR FROM " + column + ")";
        protected override string MonthSql(string column) => "EXTRACT(MONTH FROM " + column + ")";
        protected override string DaySql(string column) => "EXTRACT(DAY FROM " + column + ")";
    }

    /// <summary>The captured-value integration rows on MySQL (FUNKY_MYSQL_CONNECTION).</summary>
    [TestClass]
    public class MySqlCapturedValueTests : CapturedValueHarness
    {
        private static string ConnectionString
        {
            get
            {
                // As MySqlTestFixture resolves it: process, then Machine and User scope.
                var raw = Environment.GetEnvironmentVariable("FUNKY_MYSQL_CONNECTION");
                if (string.IsNullOrWhiteSpace(raw) && OperatingSystem.IsWindows())
                    raw = Environment.GetEnvironmentVariable("FUNKY_MYSQL_CONNECTION", EnvironmentVariableTarget.Machine);
                if (string.IsNullOrWhiteSpace(raw) && OperatingSystem.IsWindows())
                    raw = Environment.GetEnvironmentVariable("FUNKY_MYSQL_CONNECTION", EnvironmentVariableTarget.User);
                if (string.IsNullOrWhiteSpace(raw))
                    return null;
                var builder = new MySqlConnectionStringBuilder(raw) { GuidFormat = MySqlGuidFormat.Char36, AllowUserVariables = true };
                if (string.IsNullOrEmpty(builder.Database))
                    builder.Database = "funky_db";
                return builder.ConnectionString;
            }
        }

        protected override void RequireDatabase()
        {
            var connectionString = ConnectionString;
            if (connectionString == null)
                Assert.Inconclusive("FUNKY_MYSQL_CONNECTION is not set.");
            try
            {
                using (var connection = new MySqlConnection(connectionString))
                    connection.Open();
            }
            catch (Exception ex) when (ex is MySqlException || ex is InvalidOperationException)
            {
                Assert.Inconclusive("MySQL test database not available: " + ex.GetType().Name);
            }
        }

        private static void Execute(string sql)
        {
            using (var connection = new MySqlConnection(ConnectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }
            }
        }

        protected override void CreateTable() => Execute(
            $"DROP TABLE IF EXISTS {Table}; " +
            $"CREATE TABLE {Table} (id INT PRIMARY KEY, code VARCHAR(10) NULL, name VARCHAR(20) NULL, nullable_id INT NULL); " +
            $"INSERT INTO {Table} (id, code, name, nullable_id) VALUES (1, 'A', 'alpha', 1), (2, 'b', 'beta', NULL), (3, 'c', 'gamma', 3);");

        protected override void DropTable() => Execute($"DROP TABLE IF EXISTS {Table};");

        protected override OrmDataProvider CreateProvider() => new MySqlOrmDataProvider(ConnectionString);

        // development/3.10's MySQL delete fails on a cold column cache (fixed on fix/provider-scoped-caches).
        protected override void WarmUp(OrmDataProvider provider) => provider.Query<CvRow>().Where(x => x.Id > 0).ToList();
    }
}
