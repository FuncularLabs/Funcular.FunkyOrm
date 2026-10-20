using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Funcular.Data.Orm.Tests.CapturedValues;
using Funcular.Data.Orm.Visitors;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer.Tests.CapturedValues
{
    /// <summary>The direct translator rows on SQL Server (docs/plans/WHERE_CAPTURED_VALUES_PLAN.md §4.1 item 2).</summary>
    [TestClass]
    public class SqlServerTranslatorDirectTests : TranslatorDirectHarness
    {
        protected override Translation TranslateMethodCall(MethodCallExpression node, Func<PropertyInfo, string> getColumnName)
        {
            var sql = new StringBuilder();
            var parameters = new List<SqlParameter>();
            new SqlExpressionTranslator(new ParameterGenerator()).TranslateMethodCall(node, sql, parameters, getColumnName);
            return new Translation(sql.ToString(),
                parameters.Select(p => new KeyValuePair<string, object>(p.ParameterName, p.Value)).ToList());
        }

        protected override Translation TranslateDateMember(MemberExpression node, Func<PropertyInfo, string> getColumnName)
        {
            var sql = new StringBuilder();
            var parameters = new List<SqlParameter>();
            new SqlExpressionTranslator(new ParameterGenerator()).TranslateDateMember(node, sql, parameters, getColumnName);
            return new Translation(sql.ToString(),
                parameters.Select(p => new KeyValuePair<string, object>(p.ParameterName, p.Value)).ToList());
        }

        protected override Translation TranslateMemberOfMember(MemberExpression node, Func<PropertyInfo, string> getColumnName)
        {
            var sql = new StringBuilder();
            new SqlExpressionTranslator(new ParameterGenerator()).TranslateMemberOfMember(node, sql, getColumnName);
            return new Translation(sql.ToString(), new List<KeyValuePair<string, object>>());
        }

        protected override string LengthSql(string column) => "(LEN(" + column + " + N'x') - 1)";
        protected override string CastToTextSql(string column) => "CAST(" + column + " AS NVARCHAR(20))";
        protected override string StartsWithPattern(string parameterName) => parameterName + " + '%'";
        protected override string EndsWithPattern(string parameterName) => "'%' + " + parameterName;
        protected override string YearSql(string column) => "YEAR(" + column + ")";
        protected override string MonthSql(string column) => "MONTH(" + column + ")";
        protected override string DaySql(string column) => "DAY(" + column + ")";
    }

    /// <summary>The captured-value integration rows on SQL Server (FUNKY_CONNECTION).</summary>
    [TestClass]
    public class SqlServerCapturedValueTests : CapturedValueHarness
    {
        private static string ConnectionString =>
            Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
            "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=funky_db;Integrated Security=True;";

        protected override void RequireDatabase()
        {
            try
            {
                using (var connection = new SqlConnection(ConnectionString))
                    connection.Open();
            }
            catch (Exception ex) when (ex is SqlException || ex is InvalidOperationException)
            {
                Assert.Inconclusive("SQL Server test database not available: " + ex.GetType().Name);
            }
        }

        private static void Execute(string sql)
        {
            using (var connection = new SqlConnection(ConnectionString))
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
            $"IF OBJECT_ID(N'dbo.{Table}', N'U') IS NOT NULL DROP TABLE dbo.{Table}; " +
            $"CREATE TABLE dbo.{Table} (id INT PRIMARY KEY, code NVARCHAR(10) NULL, name NVARCHAR(20) NULL, nullable_id INT NULL); " +
            $"INSERT INTO dbo.{Table} (id, code, name, nullable_id) VALUES (1, 'A', 'alpha', 1), (2, 'b', 'beta', NULL), (3, 'c', 'gamma', 3);");

        protected override void DropTable() => Execute($"IF OBJECT_ID(N'dbo.{Table}', N'U') IS NOT NULL DROP TABLE dbo.{Table};");

        protected override OrmDataProvider CreateProvider() => new SqlServerOrmDataProvider(ConnectionString);

        // SQL Server has no boolean value type: a predicate in a SELECT list is a syntax error (plan §8).
        protected override bool SupportsPredicateAsValue => false;
    }
}
