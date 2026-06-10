using System;
using System.Linq;
using System.Reflection;
using Funcular.Data.Orm.Interfaces;
using Funcular.Data.Orm.MySql;

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>
    /// Unit tests for <see cref="MySqlDialect"/> SQL generation. These do NOT require a database
    /// connection and validate the MySQL-specific dialect rules (backtick quoting, no RETURNING,
    /// JSON_EXTRACT, CAST mapping). Integration tests (which require a live MySQL server via
    /// FUNKY_MYSQL_CONNECTION) live in the *IntegrationTests classes.
    /// </summary>
    [TestClass]
    public class MySqlDialectTests
    {
        private readonly ISqlDialect _dialect = new MySqlDialect();

        private class Widget
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int Order { get; set; }
        }

        [TestMethod]
        public void ProviderName_IsMysql()
        {
            Assert.AreEqual("mysql", _dialect.ProviderName);
        }

        [TestMethod]
        public void EncloseIdentifier_ReservedWord_GetsBackticks()
        {
            Assert.AreEqual("`order`", _dialect.EncloseIdentifier("order"));
            Assert.AreEqual("`select`", _dialect.EncloseIdentifier("select"));
            Assert.AreEqual("`key`", _dialect.EncloseIdentifier("key"));
        }

        [TestMethod]
        public void EncloseIdentifier_NonReservedWord_Unquoted()
        {
            Assert.AreEqual("name", _dialect.EncloseIdentifier("name"));
            Assert.AreEqual("first_name", _dialect.EncloseIdentifier("first_name"));
        }

        [TestMethod]
        public void EncloseIdentifier_StripsSqlServerBrackets()
        {
            Assert.AreEqual("`order`", _dialect.EncloseIdentifier("[order]"));
        }

        [TestMethod]
        public void BuildInsertCommand_HasNoReturningClause()
        {
            var widget = new Widget { Name = "x", Order = 1 };
            var pk = typeof(Widget).GetProperty(nameof(Widget.Id));
            var props = typeof(Widget).GetProperties();
            Func<Type, object> getDefault = t => t.IsValueType ? Activator.CreateInstance(t) : null;

            var (sql, parameters) = _dialect.BuildInsertCommand(widget, "widget", pk,
                p => p.Name.ToLower(), getDefault, props);

            StringAssert.Contains(sql, "INSERT INTO widget");
            Assert.IsFalse(sql.ToUpperInvariant().Contains("RETURNING"),
                "MySQL INSERT must not contain a RETURNING clause; identity is read via LastInsertedId.");
            // Identity PK (int) must be excluded from the column list.
            Assert.IsFalse(parameters.Any(prm => prm.ParameterName == "@Id"));
        }

        [TestMethod]
        public void BuildJsonValueExpression_UsesJsonExtractAndUnquote()
        {
            var expr = _dialect.BuildJsonValueExpression("project.metadata", "$.client.name");
            StringAssert.Contains(expr, "JSON_EXTRACT");
            StringAssert.Contains(expr, "JSON_UNQUOTE");
            StringAssert.Contains(expr, "'$.client.name'");
        }

        [TestMethod]
        public void BuildJsonValueExpression_WithIntCast_MapsToSigned()
        {
            var expr = _dialect.BuildJsonValueExpression("project.metadata", "$.score", "int");
            StringAssert.Contains(expr, "CAST(");
            StringAssert.Contains(expr, "AS SIGNED");
        }

        [TestMethod]
        public void BuildSelectCommand_ComposesColumnsAndTable()
        {
            var sql = _dialect.BuildSelectCommand("person", "id, first_name", " WHERE id = 1");
            StringAssert.Contains(sql, "SELECT id, first_name FROM person");
            StringAssert.Contains(sql, "WHERE id = 1");
        }
    }
}
