using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Funcular.Data.Orm.PostgreSql.Visitors;
using Funcular.Data.Orm.Tests.CapturedValues;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Tests.CapturedValues
{
    /// <summary>The direct translator rows on PostgreSQL (docs/plans/WHERE_CAPTURED_VALUES_PLAN.md §4.1 item 2).</summary>
    [TestClass]
    public class PostgreSqlTranslatorDirectTests : TranslatorDirectHarness
    {
        protected override Translation TranslateMethodCall(MethodCallExpression node, Func<PropertyInfo, string> getColumnName)
        {
            var sql = new StringBuilder();
            var parameters = new List<NpgsqlParameter>();
            new PostgreSqlExpressionTranslator(new PostgreSqlParameterGenerator()).TranslateMethodCall(node, sql, parameters, getColumnName);
            return new Translation(sql.ToString(),
                parameters.Select(p => new KeyValuePair<string, object>(p.ParameterName, p.Value)).ToList());
        }

        protected override Translation TranslateDateMember(MemberExpression node, Func<PropertyInfo, string> getColumnName)
        {
            var sql = new StringBuilder();
            var parameters = new List<NpgsqlParameter>();
            new PostgreSqlExpressionTranslator(new PostgreSqlParameterGenerator()).TranslateDateMember(node, sql, parameters, getColumnName);
            return new Translation(sql.ToString(),
                parameters.Select(p => new KeyValuePair<string, object>(p.ParameterName, p.Value)).ToList());
        }

        protected override Translation TranslateMemberOfMember(MemberExpression node, Func<PropertyInfo, string> getColumnName)
        {
            var sql = new StringBuilder();
            new PostgreSqlExpressionTranslator(new PostgreSqlParameterGenerator()).TranslateMemberOfMember(node, sql, getColumnName);
            return new Translation(sql.ToString(), new List<KeyValuePair<string, object>>());
        }

        protected override string LengthSql(string column) => "LENGTH(" + column + ")";
        protected override string StartsWithPattern(string parameterName) => parameterName + " || '%'";
        protected override string EndsWithPattern(string parameterName) => "'%' || " + parameterName;
        protected override string YearSql(string column) => "EXTRACT(YEAR FROM " + column + ")";
        protected override string MonthSql(string column) => "EXTRACT(MONTH FROM " + column + ")";
        protected override string DaySql(string column) => "EXTRACT(DAY FROM " + column + ")";
    }

    /// <summary>The captured-value integration rows on PostgreSQL (the suite's connection resolution).</summary>
    [TestClass]
    public class PostgreSqlCapturedValueTests : CapturedValueHarness
    {
        private static string ConnectionString => PostgreSqlTestConnection.Resolve();

        protected override void RequireDatabase()
        {
            try
            {
                using (var connection = new NpgsqlConnection(ConnectionString))
                    connection.Open();
            }
            catch (Exception ex) when (ex is NpgsqlException || ex is InvalidOperationException)
            {
                Assert.Inconclusive("PostgreSQL test database not available: " + ex.GetType().Name);
            }
        }

        private static void Execute(string sql)
        {
            using (var connection = new NpgsqlConnection(ConnectionString))
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

        protected override OrmDataProvider CreateProvider() => new PostgreSqlOrmDataProvider(ConnectionString);
    }
}
