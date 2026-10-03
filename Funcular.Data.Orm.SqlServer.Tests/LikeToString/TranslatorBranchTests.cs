using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Funcular.Data.Orm.MySql;
using Funcular.Data.Orm.PostgreSql;
using Funcular.Data.Orm.Sqlite;

namespace Funcular.Data.Orm.SqlServer.Tests.LikeToString
{
    /// <summary>
    /// DB-free rows for the translator branches the integration harness doesn't reach (docs/plans/
    /// LIKE_ESCAPING_TOSTRING_PLAN.md, AC8): each provider's <c>TranslateMethodCall</c> and <c>TranslateDateMember</c>,
    /// called directly with a column-name function that writes <c>c.Name</c>. Each row pins today's translation or
    /// rejection of a shape; the provider-specific SQL comes from the derived class.
    /// </summary>
    public abstract class TranslatorBranchHarness
    {
        public class TbChild
        {
            public string Name { get; set; }
        }

        public class TbRow
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Other { get; set; }
            public int? NCode { get; set; }
            public DateTime? When { get; set; }
            public List<int> Tags { get; set; }
            public TbChild Child { get; set; }
            public int Field;
        }

        /// <summary>The SQL a call appended and the parameters it added.</summary>
        protected sealed class Result
        {
            public Result(string sql, IEnumerable<DbParameter> parameters)
            {
                Sql = sql;
                var list = parameters.ToList();
                Names = list.Select(p => p.ParameterName).ToArray();
                Values = list.Select(p => p.Value).ToArray();
            }

            public string Sql { get; }
            public string[] Names { get; }
            public object[] Values { get; }
        }

        private readonly List<int> ids = new List<int> { 1, 2 };

        #region Provider seam

        protected abstract Result TranslateCall(MethodCallExpression node);

        protected abstract Result TranslateDate(MemberExpression node);

        /// <summary>The provider's SQL for a date part (<c>Year</c>, <c>Month</c>, <c>Day</c>) of <paramref name="column"/>.</summary>
        protected abstract string DatePart(string part, string column);

        protected abstract Assembly ProviderAssembly { get; }

        protected static string Column(PropertyInfo property) => "c." + property.Name;

        #endregion

        private static MethodCallExpression CallOf<TResult>(Expression<Func<TbRow, TResult>> lambda) => (MethodCallExpression)lambda.Body;

        private static MemberExpression MemberOf<TResult>(Expression<Func<TbRow, TResult>> lambda) => (MemberExpression)lambda.Body;

        private static TException Throws<TException>(Action action, string messagePrefix) where TException : Exception
        {
            Exception thrown = null;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
            Assert.IsNotNull(thrown, "no exception");
            Assert.AreEqual(typeof(TException), thrown.GetType(), thrown.ToString());
            StringAssert.StartsWith(thrown.Message, messagePrefix);
            return (TException)thrown;
        }

        #region String methods

        [TestMethod]
        public void StringContains_ConstantReceiverAndColumnArgument_SendsTheReceiverThenRejectsTheArgument() =>
            Throws<NotSupportedException>(() => TranslateCall(CallOf(x => "abc".Contains(x.Name))),
                "Unsupported member expression in string Contains: x.Name");

        [TestMethod]
        public void StringContains_ComputedArgument_IsRejected() =>
            Throws<NotSupportedException>(() => TranslateCall(CallOf(x => x.Name.Contains(x.Other.Trim()))),
                "Unsupported expression type in string Contains: Call");

        [TestMethod]
        public void StartsWith_ColumnArgument_IsRejected() =>
            Throws<NotSupportedException>(() => TranslateCall(CallOf(x => x.Name.StartsWith(x.Other))),
                "Unsupported member expression in StartsWith: x.Other");

        [TestMethod]
        public void StartsWith_ComputedArgument_IsRejected() =>
            Throws<NotSupportedException>(() => TranslateCall(CallOf(x => x.Name.StartsWith(x.Other.Trim()))),
                "Unsupported expression type in StartsWith: Call");

        [TestMethod]
        public void StartsWith_NullValue_IsRejected() =>
            Throws<NotSupportedException>(() => TranslateCall(CallOf(x => x.Name.StartsWith((string)null))),
                "Method call StartsWith is not supported.");

        [TestMethod]
        public void EnumerableContainsOverAString_IsRejected()
        {
            var contains = typeof(Enumerable).GetMethods().Single(m => m.Name == "Contains" && m.GetParameters().Length == 2)
                .MakeGenericMethod(typeof(char));
            Throws<NotSupportedException>(() => TranslateCall(Expression.Call(contains, Expression.Constant("abc"), Expression.Constant('b'))),
                "Method call Contains is not supported.");
        }

        #endregion

        #region Collection Contains

        [TestMethod]
        public void CollectionContains_ConstantCollection_IsAnInClause()
        {
            var x = Expression.Parameter(typeof(TbRow), "x");
            var list = new List<int> { 1, 2 };
            var result = TranslateCall(Expression.Call(Expression.Constant(list), typeof(List<int>).GetMethod("Contains"),
                Expression.Property(x, nameof(TbRow.Id))));
            Assert.AreEqual($"c.Id IN ({result.Names[0]},{result.Names[1]})", result.Sql);
            CollectionAssert.AreEqual(new object[] { 1, 2 }, result.Values);
        }

        [TestMethod]
        public void CollectionContains_ColumnCollection_IsRejected() =>
            Throws<NotSupportedException>(() => TranslateCall(CallOf(x => x.Tags.Contains(5))),
                "Collection Contains with parameter references is not supported: x.Tags");

        [TestMethod]
        public void CollectionContains_ValueUnwrap_ThrowsTheProvidersNullableExpressionException()
        {
            Exception thrown = null;
            try
            {
                TranslateCall(CallOf(x => ids.Contains(x.NCode.Value)));
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
            Assert.IsNotNull(thrown, "no exception");
            Assert.AreEqual("Funcular.Data.Orm.Exceptions.NullableExpressionException", thrown.GetType().FullName, thrown.ToString());
            Assert.AreEqual(ProviderAssembly, thrown.GetType().Assembly);
            StringAssert.StartsWith(thrown.Message, "Do not use '.Value' to unwrap nullable property 'NCode' inside Contains().");
        }

        [TestMethod]
        public void CollectionContains_FieldItem_IsRejected() =>
            Throws<InvalidOperationException>(() => TranslateCall(CallOf(x => ids.Contains(x.Field))), "Member Field is not a property.");

        #endregion

        #region ToString receivers that aren't columns

        [TestMethod]
        public void ToString_NestedMemberReceiver_KeepsTodaysTranslation() =>
            Assert.AreEqual("c.Child", TranslateCall(CallOf(x => x.Child.Name.ToString())).Sql);

        [TestMethod]
        public void ToString_ComputedReceiver_IsRejected() =>
            Throws<NotSupportedException>(() => TranslateCall(CallOf(x => (x.Id + 1).ToString())),
                "Expression type Add is not supported for translation.");

        [TestMethod]
        public void ToString_NullConstantReceiver_IsNull()
        {
            var call = Expression.Call(Expression.Constant(null, typeof(string)), typeof(string).GetMethod("ToString", Type.EmptyTypes));
            Assert.AreEqual("NULL", TranslateCall(call).Sql);
        }

        #endregion

        #region Date members

        [DataTestMethod]
        [DataRow("Year")]
        [DataRow("Month")]
        [DataRow("Day")]
        public void DateMember_IsTheProvidersDatePart(string part)
        {
            var x = Expression.Parameter(typeof(TbRow), "x");
            var value = Expression.Property(Expression.Property(x, nameof(TbRow.When)), "Value");
            Assert.AreEqual(DatePart(part, "c.When"), TranslateDate(Expression.Property(value, part)).Sql);
        }

        [TestMethod]
        public void DateMember_OtherPart_IsRejected() =>
            Throws<NotSupportedException>(() => TranslateDate(MemberOf(x => x.When.Value.Hour)),
                "Unsupported property Hour in nested expression.");

        [TestMethod]
        public void DateMember_OtherShape_IsRejected() =>
            Throws<NotSupportedException>(() => TranslateDate(MemberOf(x => x.Name.Length)),
                "Unsupported date member expression: x.Name.Length");

        #endregion
    }

    [TestClass]
    public class SqlServerTranslatorBranchTests : TranslatorBranchHarness
    {
        protected override Result TranslateCall(MethodCallExpression node)
        {
            var sql = new StringBuilder();
            var parameters = new List<Microsoft.Data.SqlClient.SqlParameter>();
            new Funcular.Data.Orm.Visitors.SqlExpressionTranslator(new Funcular.Data.Orm.Visitors.ParameterGenerator())
                .TranslateMethodCall(node, sql, parameters, Column);
            return new Result(sql.ToString(), parameters);
        }

        protected override Result TranslateDate(MemberExpression node)
        {
            var sql = new StringBuilder();
            var parameters = new List<Microsoft.Data.SqlClient.SqlParameter>();
            new Funcular.Data.Orm.Visitors.SqlExpressionTranslator(new Funcular.Data.Orm.Visitors.ParameterGenerator())
                .TranslateDateMember(node, sql, parameters, Column);
            return new Result(sql.ToString(), parameters);
        }

        protected override string DatePart(string part, string column) => $"{part.ToUpperInvariant()}({column})";

        protected override Assembly ProviderAssembly => typeof(SqlServerOrmDataProvider).Assembly;
    }

    [TestClass]
    public class PostgreSqlTranslatorBranchTests : TranslatorBranchHarness
    {
        protected override Result TranslateCall(MethodCallExpression node)
        {
            var sql = new StringBuilder();
            var parameters = new List<Npgsql.NpgsqlParameter>();
            new Funcular.Data.Orm.PostgreSql.Visitors.PostgreSqlExpressionTranslator(new Funcular.Data.Orm.PostgreSql.Visitors.PostgreSqlParameterGenerator())
                .TranslateMethodCall(node, sql, parameters, Column);
            return new Result(sql.ToString(), parameters);
        }

        protected override Result TranslateDate(MemberExpression node)
        {
            var sql = new StringBuilder();
            var parameters = new List<Npgsql.NpgsqlParameter>();
            new Funcular.Data.Orm.PostgreSql.Visitors.PostgreSqlExpressionTranslator(new Funcular.Data.Orm.PostgreSql.Visitors.PostgreSqlParameterGenerator())
                .TranslateDateMember(node, sql, parameters, Column);
            return new Result(sql.ToString(), parameters);
        }

        protected override string DatePart(string part, string column) => $"EXTRACT({part.ToUpperInvariant()} FROM {column})";

        protected override Assembly ProviderAssembly => typeof(PostgreSqlOrmDataProvider).Assembly;
    }

    [TestClass]
    public class MySqlTranslatorBranchTests : TranslatorBranchHarness
    {
        protected override Result TranslateCall(MethodCallExpression node)
        {
            var sql = new StringBuilder();
            var parameters = new List<MySqlConnector.MySqlParameter>();
            new Funcular.Data.Orm.MySql.Visitors.MySqlExpressionTranslator(new Funcular.Data.Orm.MySql.Visitors.MySqlParameterGenerator())
                .TranslateMethodCall(node, sql, parameters, Column);
            return new Result(sql.ToString(), parameters);
        }

        protected override Result TranslateDate(MemberExpression node)
        {
            var sql = new StringBuilder();
            var parameters = new List<MySqlConnector.MySqlParameter>();
            new Funcular.Data.Orm.MySql.Visitors.MySqlExpressionTranslator(new Funcular.Data.Orm.MySql.Visitors.MySqlParameterGenerator())
                .TranslateDateMember(node, sql, parameters, Column);
            return new Result(sql.ToString(), parameters);
        }

        protected override string DatePart(string part, string column) => $"EXTRACT({part.ToUpperInvariant()} FROM {column})";

        protected override Assembly ProviderAssembly => typeof(MySqlOrmDataProvider).Assembly;
    }

    [TestClass]
    public class SqliteTranslatorBranchTests : TranslatorBranchHarness
    {
        protected override Result TranslateCall(MethodCallExpression node)
        {
            var sql = new StringBuilder();
            var parameters = new List<Microsoft.Data.Sqlite.SqliteParameter>();
            new Funcular.Data.Orm.Sqlite.Visitors.SqliteExpressionTranslator(new Funcular.Data.Orm.Sqlite.Visitors.SqliteParameterGenerator())
                .TranslateMethodCall(node, sql, parameters, Column);
            return new Result(sql.ToString(), parameters);
        }

        protected override Result TranslateDate(MemberExpression node)
        {
            var sql = new StringBuilder();
            var parameters = new List<Microsoft.Data.Sqlite.SqliteParameter>();
            new Funcular.Data.Orm.Sqlite.Visitors.SqliteExpressionTranslator(new Funcular.Data.Orm.Sqlite.Visitors.SqliteParameterGenerator())
                .TranslateDateMember(node, sql, parameters, Column);
            return new Result(sql.ToString(), parameters);
        }

        protected override string DatePart(string part, string column) =>
            $"CAST(strftime('%{(part == "Year" ? "Y" : part == "Month" ? "m" : "d")}', {column}) AS INTEGER)";

        protected override Assembly ProviderAssembly => typeof(SqliteOrmDataProvider).Assembly;
    }
}
