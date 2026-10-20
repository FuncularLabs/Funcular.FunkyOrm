using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Funcular.Data.Orm.Sqlite.Visitors;
using Funcular.Data.Orm.Tests.CapturedValues;
using Microsoft.Data.Sqlite;

namespace Funcular.Data.Orm.Sqlite.Tests.CapturedValues
{
    /// <summary>The direct translator rows on SQLite (docs/plans/WHERE_CAPTURED_VALUES_PLAN.md §4.1 item 2).</summary>
    [TestClass]
    public class SqliteTranslatorDirectTests : TranslatorDirectHarness
    {
        protected override Translation TranslateMethodCall(MethodCallExpression node, Func<PropertyInfo, string> getColumnName)
        {
            var sql = new StringBuilder();
            var parameters = new List<SqliteParameter>();
            new SqliteExpressionTranslator(new SqliteParameterGenerator()).TranslateMethodCall(node, sql, parameters, getColumnName);
            return new Translation(sql.ToString(),
                parameters.Select(p => new KeyValuePair<string, object>(p.ParameterName, p.Value)).ToList());
        }

        protected override Translation TranslateDateMember(MemberExpression node, Func<PropertyInfo, string> getColumnName)
        {
            var sql = new StringBuilder();
            var parameters = new List<SqliteParameter>();
            new SqliteExpressionTranslator(new SqliteParameterGenerator()).TranslateDateMember(node, sql, parameters, getColumnName);
            return new Translation(sql.ToString(),
                parameters.Select(p => new KeyValuePair<string, object>(p.ParameterName, p.Value)).ToList());
        }

        protected override Translation TranslateMemberOfMember(MemberExpression node, Func<PropertyInfo, string> getColumnName)
        {
            var sql = new StringBuilder();
            new SqliteExpressionTranslator(new SqliteParameterGenerator()).TranslateMemberOfMember(node, sql, getColumnName);
            return new Translation(sql.ToString(), new List<KeyValuePair<string, object>>());
        }

        protected override string LengthSql(string column) => "LENGTH(" + column + ")";
        protected override string CastToTextSql(string column) => "CAST(" + column + " AS TEXT)";
        protected override string StartsWithPattern(string parameterName) => parameterName + " || '%'";
        protected override string EndsWithPattern(string parameterName) => "'%' || " + parameterName;
        protected override string YearSql(string column) => "CAST(strftime('%Y', " + column + ") AS INTEGER)";
        protected override string MonthSql(string column) => "CAST(strftime('%m', " + column + ") AS INTEGER)";
        protected override string DaySql(string column) => "CAST(strftime('%d', " + column + ") AS INTEGER)";
    }

    /// <summary>
    /// The captured-value integration rows on SQLite, a temp file per test. Sqlite.Tests compiles as C# 14, so its
    /// arrays bind to <see cref="MemoryExtensions"/> (plan §1.2), which the array rows assert.
    /// </summary>
    [TestClass]
    public class SqliteCapturedValueTests : CapturedValueHarness
    {
        private string _path;

        protected override void CreateTable()
        {
            DropTable();
            _path = Path.Combine(Path.GetTempPath(), "zz_cv_" + Guid.NewGuid().ToString("N") + ".db");
            using (var connection = new SqliteConnection("Data Source=" + _path))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        $"CREATE TABLE {Table} (id INTEGER PRIMARY KEY, code TEXT NULL, name TEXT NULL, nullable_id INTEGER NULL); " +
                        $"INSERT INTO {Table} (id, code, name, nullable_id) VALUES (1, 'A', 'alpha', 1), (2, 'b', 'beta', NULL), (3, 'c', 'gamma', 3);";
                    command.ExecuteNonQuery();
                }
            }
        }

        protected override void DropTable()
        {
            if (_path == null)
                return;
            SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(_path))
                    File.Delete(_path);
            }
            catch (IOException)
            {
                // A pooled handle still holds it; the file is a uniquely named temp file.
            }
            _path = null;
        }

        protected override OrmDataProvider CreateProvider() => new SqliteOrmDataProvider("Data Source=" + _path);

        protected override Type ArrayContainsDeclaringType => typeof(MemoryExtensions);
    }
}
