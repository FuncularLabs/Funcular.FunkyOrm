using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Funcular.Data.Orm.Interfaces;
using Funcular.Data.Orm.Sqlite;
using static Funcular.Data.Orm.SqlServer.Tests.Caching.CacheScopeTestSupport;

namespace Funcular.Data.Orm.SqlServer.Tests.Caching
{
    /// <summary>
    /// The SQLite temp-file rows of the provider-scoped caches plan (docs/plans/PROVIDER_SCOPED_CACHES_PLAN.md,
    /// §4.1, D10). Each test creates its own database files in the temp folder and deletes them afterwards; every
    /// entity type here is used by one test only.
    /// </summary>
    [TestClass]
    public class ProviderScopedCacheSqliteTests
    {
        private readonly List<string> _databases = new List<string>();

        [TestCleanup]
        public void DeleteDatabases()
        {
            foreach (var path in _databases)
                DeleteSqliteDatabase(path);
        }

        private string NewDatabase(string script)
        {
            var path = CreateSqliteDatabase(script);
            _databases.Add(path);
            return path;
        }

        private static SqliteOrmDataProvider Provider(string path, ISqlDialect dialect = null) =>
            new SqliteOrmDataProvider($"Data Source={path}", dialect: dialect);

        #region AC2 — database isolation

        /// <summary>No [Table] or [Column]: the table and the Label column are discovered in each database.</summary>
        public class ScopedWidget
        {
            [Key] public int Id { get; set; }
            public string Label { get; set; }
        }

        [TestMethod]
        public void TwoSqliteDatabases_SameEntity_DiscoverTheirOwnTableAndColumns()
        {
            var first = NewDatabase("CREATE TABLE scoped_widget (id INTEGER PRIMARY KEY, label TEXT); INSERT INTO scoped_widget VALUES (1, 'one');");
            var second = NewDatabase("CREATE TABLE scopedwidget (id INTEGER PRIMARY KEY, la_bel TEXT); INSERT INTO scopedwidget VALUES (1, 'two');");

            using (var provider = Provider(first))
                Assert.AreEqual("one", provider.Query<ScopedWidget>().Single().Label, "first database");
            using (var provider = Provider(second))
                Assert.AreEqual("two", provider.Query<ScopedWidget>().Single().Label, "second database");
            using (var provider = Provider(first))
                Assert.AreEqual("one", provider.Query<ScopedWidget>().Single().Label, "the first database keeps its own names");
        }

        [Table("zz_psc_nick_holder")]
        public class PscNickHolderSync
        {
            [Key] public int Id { get; set; }
            public string Nick { get; set; }
        }

        [Table("zz_psc_nick_holder")]
        public class PscNickHolderAsync
        {
            [Key] public int Id { get; set; }
            public string Nick { get; set; }
        }

        [DataTestMethod]
        [DataRow("sync Query")]
        [DataRow("async GetListAsync")]
        public async Task TwoSqliteDatabases_ColumnMissingInFirst_IsReadInSecond(string read)
        {
            var withoutNick = NewDatabase("CREATE TABLE zz_psc_nick_holder (id INTEGER PRIMARY KEY); INSERT INTO zz_psc_nick_holder VALUES (1);");
            var withNick = NewDatabase("CREATE TABLE zz_psc_nick_holder (id INTEGER PRIMARY KEY, nick TEXT); INSERT INTO zz_psc_nick_holder VALUES (1, 'nick');");

            string nick;
            if (read == "sync Query")
            {
                using (var provider = Provider(withoutNick))
                    Assert.AreEqual(1, provider.Query<PscNickHolderSync>().ToList().Single().Id, "first database");
                using (var provider = Provider(withNick))
                    nick = provider.Query<PscNickHolderSync>().ToList().Single().Nick;
            }
            else
            {
                using (var provider = Provider(withoutNick))
                    Assert.AreEqual(1, (await provider.GetListAsync<PscNickHolderAsync>()).Single().Id, "first database");
                using (var provider = Provider(withNick))
                    nick = (await provider.GetListAsync<PscNickHolderAsync>()).Single().Nick;
            }

            Assert.AreEqual("nick", nick, $"{read}: the column missing in the first database is read in the second");
        }

        #endregion

        #region AC3 — type-name isolation

        public static class PscSqliteOuterA
        {
            [Table("zz_psc_gadget_a")]
            public class PscGadget
            {
                [Key] public int Id { get; set; }
                [Column("alpha_caption")] public string Caption { get; set; }
            }
        }

        public static class PscSqliteOuterB
        {
            [Table("zz_psc_gadget_b")]
            public class PscGadget
            {
                [Key] public int Id { get; set; }
                [Column("beta_caption")] public string Caption { get; set; }
            }
        }

        [TestMethod]
        public void SameSimpleTypeName_SqliteQueryOfTheFirstTypeAfterTheSecondsDiscovery()
        {
            var path = NewDatabase(
                "CREATE TABLE zz_psc_gadget_a (id INTEGER PRIMARY KEY, alpha_caption TEXT); INSERT INTO zz_psc_gadget_a VALUES (1, 'a');" +
                "CREATE TABLE zz_psc_gadget_b (id INTEGER PRIMARY KEY, beta_caption TEXT); INSERT INTO zz_psc_gadget_b VALUES (1, 'b');");

            using var provider = Provider(path);
            Assert.AreEqual("a", provider.GetList<PscSqliteOuterA.PscGadget>().Single().Caption, "A discovered");
            Assert.AreEqual("b", provider.GetList<PscSqliteOuterB.PscGadget>().Single().Caption, "B discovered");

            var rows = provider.Query<PscSqliteOuterA.PscGadget>().Where(x => x.Caption == "a").ToList();
            Assert.AreEqual(1, rows.Count, "A queried with Where after B's discovery");
            Assert.AreEqual("a", rows[0].Caption);
        }

        #endregion

        #region AC5 — the SQLite mapper with a non-default dialect and a reserved word

        /// <summary>
        /// Encloses every identifier in one quote pair and leaves an enclosed name unchanged (E(E(x)) = E(x)); otherwise
        /// SQLite's dialect.
        /// </summary>
        private sealed class AlwaysEncloseSqliteDialect : ISqlDialect
        {
            private readonly SqliteDialect _inner = new SqliteDialect();
            private readonly string _open;
            private readonly string _close;

            public AlwaysEncloseSqliteDialect(string open, string close)
            {
                _open = open;
                _close = close;
            }

            public string EncloseIdentifier(string identifier) =>
                string.IsNullOrWhiteSpace(identifier) || (identifier.StartsWith(_open) && identifier.EndsWith(_close))
                    ? identifier
                    : _open + identifier + _close;

            public bool IsReservedWord(string word) => _inner.IsReservedWord(word);

            public (string CommandText, IEnumerable<IDbDataParameter> Parameters) BuildInsertCommand<T>(T entity, string tableName,
                PropertyInfo primaryKey, Func<PropertyInfo, string> getColumnName, Func<Type, object> getDefaultValue,
                IEnumerable<PropertyInfo> properties) where T : class =>
                _inner.BuildInsertCommand(entity, tableName, primaryKey, getColumnName, getDefaultValue, properties);

            public (string CommandText, IEnumerable<IDbDataParameter> Parameters) BuildUpdateCommand<T>(T entity, T existing,
                string tableName, PropertyInfo primaryKey, Func<PropertyInfo, string> getColumnName,
                IEnumerable<PropertyInfo> properties) where T : class =>
                _inner.BuildUpdateCommand(entity, existing, tableName, primaryKey, getColumnName, properties);

            public string BuildDeleteCommand(string tableName, string whereClause) => _inner.BuildDeleteCommand(tableName, whereClause);

            public string BuildSelectCommand(string tableName, string columnNames, string whereClause, string joinClauses = null) =>
                _inner.BuildSelectCommand(tableName, columnNames, whereClause, joinClauses);

            public string BuildJsonValueExpression(string qualifiedColumn, string jsonPath, string castType = null) =>
                _inner.BuildJsonValueExpression(qualifiedColumn, jsonPath, castType);

            public string ProviderName => _inner.ProviderName;

            public string BuildScalarSubquery(string childTableName, string childFkColumn, string parentPkExpression,
                Funcular.Data.Orm.Attributes.AggregateFunction function, string aggregateColumn = null,
                string conditionColumn = null, string conditionValue = null) =>
                _inner.BuildScalarSubquery(childTableName, childFkColumn, parentPkExpression, function, aggregateColumn,
                    conditionColumn, conditionValue);

            public string BuildJsonCollectionSubquery(string childTableName, string childFkColumn, string parentPkExpression,
                IList<string> columnExpressions, string orderByColumn = null) =>
                _inner.BuildJsonCollectionSubquery(childTableName, childFkColumn, parentPkExpression, columnExpressions,
                    orderByColumn);
        }

        // Property-named table and columns (R6-4), one type per dialect row.
        [Table("zz_psc_bracket_row")]
        public class PscBracketRow
        {
            [Key] public int Id { get; set; }
            public string Label { get; set; }
        }

        [Table("zz_psc_backtick_row")]
        public class PscBacktickRow
        {
            [Key] public int Id { get; set; }
            public string Label { get; set; }
        }

        [DataTestMethod]
        [DataRow("[", "]")]
        [DataRow("`", "`")]
        public void SqliteBracketDialect_ReadsItsOwnRows(string open, string close)
        {
            var dialect = new AlwaysEncloseSqliteDialect(open, close);
            if (open == "[")
            {
                var path = NewDatabase("CREATE TABLE zz_psc_bracket_row (Id INTEGER PRIMARY KEY, Label TEXT); INSERT INTO zz_psc_bracket_row VALUES (5, 'Ann');");
                using var provider = Provider(path, dialect);
                var row = provider.Query<PscBracketRow>().ToList().Single();
                Assert.AreEqual("5:Ann", $"{row.Id}:{row.Label}", "an always-bracket SQLite dialect reads its own rows");
            }
            else
            {
                var path = NewDatabase("CREATE TABLE zz_psc_backtick_row (Id INTEGER PRIMARY KEY, Label TEXT); INSERT INTO zz_psc_backtick_row VALUES (5, 'Ann');");
                using var provider = Provider(path, dialect);
                var row = provider.Query<PscBacktickRow>().ToList().Single();
                Assert.AreEqual("5:Ann", $"{row.Id}:{row.Label}", "an always-backtick SQLite dialect reads its own rows");
            }
        }

        [Table("zz_psc_reserved_row")]
        public class PscReservedRow
        {
            [Key] public int Id { get; set; }
            public int Order { get; set; }
        }

        [TestMethod]
        public void SqliteDefaultDialect_ReadsAReservedWordColumn()
        {
            var path = NewDatabase("CREATE TABLE zz_psc_reserved_row (Id INTEGER PRIMARY KEY, \"Order\" INTEGER); INSERT INTO zz_psc_reserved_row VALUES (1, 42);");
            using var provider = Provider(path);

            Assert.AreEqual(42, provider.Query<PscReservedRow>().ToList().Single().Order,
                "the default dialect reads a column named Order");
        }

        [Table("person")]
        public class PscSqliteMapperPerson
        {
            [Key] public int Id { get; set; }
            [Column("first_name")] public string FirstName { get; set; }
        }

        [TestMethod]
        public void MapperCache_IsScoped_ThroughARealRead()
        {
            const string person = "CREATE TABLE person (id INTEGER PRIMARY KEY, first_name TEXT); INSERT INTO person VALUES (1, 'zz_psc');";
            var pathA = NewDatabase(person);
            var pathB = NewDatabase(person);
            using var a = Provider(pathA);
            using var b = Provider(pathB);
            var prefix = typeof(PscSqliteMapperPerson).FullName + "|";
            var mappersA = CachesOf(a).Mappers;
            var mappersB = CachesOf(b).Mappers;

            Assert.AreEqual("zz_psc", a.Query<PscSqliteMapperPerson>().ToList().Single().FirstName, "P_A reads person");
            Assert.IsTrue(mappersA.Keys.Any(k => k.StartsWith(prefix)), "A's mapper cache has no entry after P_A's read");
            Assert.IsFalse(mappersB.Keys.Any(k => k.StartsWith(prefix)), "B's mapper cache has an entry before P_B has read");

            Assert.AreEqual("zz_psc", b.Query<PscSqliteMapperPerson>().ToList().Single().FirstName, "P_B reads person");
            var entryA = mappersA.Single(e => e.Key.StartsWith(prefix)).Value;
            var entryB = mappersB.Single(e => e.Key.StartsWith(prefix)).Value;
            Assert.AreNotSame(entryA, entryB, "B has its own mapper");
        }

        #endregion

        #region AC8 — SQLite uses discovered names

        [Table("zz_psc_underscore_label")]
        public class PscUnderscoreLabel
        {
            [Key] public int Id { get; set; }
            public string Label { get; set; }
        }

        [TestMethod]
        public void SqliteEntity_DiscoveredUnderscoreColumn_IsQueryable()
        {
            var path = NewDatabase("CREATE TABLE zz_psc_underscore_label (id INTEGER PRIMARY KEY, la_bel TEXT); INSERT INTO zz_psc_underscore_label VALUES (1, 'discovered');");
            using var provider = Provider(path);

            Assert.AreEqual("discovered", provider.Query<PscUnderscoreLabel>().ToList().Single().Label);
        }

        #endregion
    }
}
