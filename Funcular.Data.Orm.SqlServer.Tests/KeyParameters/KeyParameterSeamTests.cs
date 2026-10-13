using System;
using System.Threading.Tasks;
using Funcular.Data.Orm.MySql;
using Funcular.Data.Orm.PostgreSql;
using Funcular.Data.Orm.Sqlite;
using Funcular.Data.Orm.Tests.KeyParameters;

#pragma warning disable CS0618 // The keyed CreateGetOneOrSelectCommandText and GetWhereClause are obsolete; these rows pin that they fail closed.

namespace Funcular.Data.Orm.SqlServer.Tests.KeyParameters
{
    /// <summary>
    /// AC8 (docs/plans/KEY_PARAMETER_PLAN.md): the members that built a key's SQL as text, still reachable from a
    /// subclass, refuse a key instead of inlining it, on all four providers. No database is opened.
    /// </summary>
    [TestClass]
    public class KeyParameterSeamTests
    {
        // A closed loopback port, made-up SQL credentials and a short timeout: a regression that opens a connection
        // fails fast, sends no Windows credentials and looks no host up.
        private const string SqlServerNowhere =
            "Server=127.0.0.1,1;Database=zz_kp;User Id=zz_kp_none;Password=zz_kp_none;Connect Timeout=1;TrustServerCertificate=true";
        private const string PostgreSqlNowhere = "Host=127.0.0.1;Port=1;Database=zz_kp;Username=zz_kp_none;Password=zz_kp_none;Timeout=1";
        private const string MySqlNowhere = "Server=127.0.0.1;Port=1;Database=zz_kp;User Id=zz_kp_none;Password=zz_kp_none;Connection Timeout=1";

        private sealed class SqlServerProbe : SqlServerOrmDataProvider
        {
            public SqlServerProbe() : base(SqlServerNowhere) { }
            public string Select<T>(object key) where T : class, new() => CreateGetOneOrSelectCommandText<T>(key);
            public string Where<T>(object key) where T : class => GetWhereClause<T>(key);
        }

        private sealed class PostgreSqlProbe : PostgreSqlOrmDataProvider
        {
            public PostgreSqlProbe() : base(PostgreSqlNowhere) { }
            public string Select<T>(object key) where T : class, new() => CreateGetOneOrSelectCommandText<T>(key);
            public string Where<T>(object key) where T : class => GetWhereClause<T>(key);
        }

        private sealed class MySqlProbe : MySqlOrmDataProvider
        {
            public MySqlProbe() : base(MySqlNowhere) { }
            public string Select<T>(object key) where T : class, new() => CreateGetOneOrSelectCommandText<T>(key);
            public string Where<T>(object key) where T : class => GetWhereClause<T>(key);
        }

        private sealed class SqliteProbe : SqliteOrmDataProvider
        {
            public SqliteProbe() : base("Data Source=:memory:") { }
            public string Select<T>(object key) where T : class, new() => CreateGetOneOrSelectCommandText<T>(key);
            public string Where<T>(object key) where T : class => GetWhereClause<T>(key);
        }

        public enum Shade { Red = 1, Green = 2 }

        private sealed class StringKeyed { [System.ComponentModel.DataAnnotations.Key] public string Id { get; set; } }
        private sealed class IntKeyed { [System.ComponentModel.DataAnnotations.Key] public int Id { get; set; } }
        private sealed class NullableIntKeyed { [System.ComponentModel.DataAnnotations.Key] public int? Id { get; set; } }
        private sealed class ShadeKeyed { [System.ComponentModel.DataAnnotations.Key] public Shade Id { get; set; } }
        private sealed class GuidKeyed { [System.ComponentModel.DataAnnotations.Key] public Guid Id { get; set; } }

        /// <summary>Exposes the protected conversion, which every provider's Get and Update use.</summary>
        private sealed class ConversionProbe : SqliteOrmDataProvider
        {
            public ConversionProbe() : base("Data Source=:memory:") { }
            public static object Convert<T>(object key) =>
                ConvertKeyToPrimaryKeyType(key, typeof(T).GetProperty("Id"), typeof(T));
        }

        /// <summary>AC6, D2: what each key converts to (no database).</summary>
        [TestMethod]
        public void ConvertKey_ConvertsToThePrimaryKeyType()
        {
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                // A culture whose decimal separator isn't '.', so invariant formatting is observable.
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.AreEqual("it's", ConversionProbe.Convert<StringKeyed>("it's"), "string as it is");
                Assert.AreEqual("5", ConversionProbe.Convert<StringKeyed>(5), "int to string");
                Assert.AreEqual("1.5", ConversionProbe.Convert<StringKeyed>(1.5m), "decimal to invariant string");
                Assert.AreEqual(2, ConversionProbe.Convert<IntKeyed>(2L), "long to int");
                Assert.AreEqual(2, ConversionProbe.Convert<IntKeyed>("2"), "text to int");
                Assert.AreEqual(2, ConversionProbe.Convert<IntKeyed>(2.0), "whole double to int");
                Assert.AreEqual(2, ConversionProbe.Convert<NullableIntKeyed>(2L), "long to int?");
                Assert.AreEqual(Shade.Green, ConversionProbe.Convert<ShadeKeyed>(2), "int to enum");
                Assert.AreEqual(Shade.Green, ConversionProbe.Convert<ShadeKeyed>(Shade.Green), "enum as it is");
                var guid = new Guid("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d");
                Assert.AreEqual(guid, ConversionProbe.Convert<GuidKeyed>(guid.ToString("B").ToUpperInvariant()), "braced text to Guid");
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = culture;
            }
        }

        /// <summary>AC6, D3: what each rejected key throws (no database).</summary>
        [DataTestMethod]
        [DataRow("lossyDouble")] [DataRow("lossyDecimal")] [DataRow("lossyFloat")] [DataRow("overflow")] [DataRow("bool")]
        [DataRow("char")] [DataRow("textEnum")] [DataRow("dateForInt")] [DataRow("intForGuid")] [DataRow("textForInt")]
        public void ConvertKey_RejectsWhatDoesNotConvert(string form)
        {
            Func<object> call;
            switch (form)
            {
                case "lossyDouble": call = () => ConversionProbe.Convert<IntKeyed>(2.5); break;
                case "lossyDecimal": call = () => ConversionProbe.Convert<IntKeyed>(2.5m); break;
                case "lossyFloat": call = () => ConversionProbe.Convert<IntKeyed>(2.5f); break;
                case "textForInt": call = () => ConversionProbe.Convert<IntKeyed>("two"); break;
                case "overflow": call = () => ConversionProbe.Convert<IntKeyed>(long.MaxValue); break;
                case "bool": call = () => ConversionProbe.Convert<IntKeyed>(true); break;
                case "char": call = () => ConversionProbe.Convert<IntKeyed>('2'); break;
                case "textEnum": call = () => ConversionProbe.Convert<ShadeKeyed>("Green"); break;
                case "dateForInt": call = () => ConversionProbe.Convert<IntKeyed>(DateTime.Today); break;
                default: call = () => ConversionProbe.Convert<GuidKeyed>(5); break;
            }
            var exception = Assert.ThrowsException<ArgumentException>(call, form);
            Assert.AreEqual("key", exception.ParamName, $"{form}: parameter name");
            StringAssert.Contains(exception.Message, "can't be converted to", $"{form}: message");
            Assert.IsNull(exception.InnerException, $"{form}: inner exception (its message can quote the key)");
            Assert.IsFalse(exception.ToString().Contains("two"), $"{form}: the exception repeats the key");
            Assert.ThrowsException<ArgumentNullException>(() => ConversionProbe.Convert<IntKeyed>(null), "null");
        }

        /// <summary>
        /// AC6: an unconvertible key throws ArgumentException from Get and GetAsync before the provider touches its
        /// connection (these providers' connections can't open), on all four providers.
        /// </summary>
        [DataTestMethod]
        [DataRow("SqlServer", "sync")] [DataRow("SqlServer", "async")]
        [DataRow("PostgreSql", "sync")] [DataRow("PostgreSql", "async")]
        [DataRow("MySql", "sync")] [DataRow("MySql", "async")]
        [DataRow("Sqlite", "sync")] [DataRow("Sqlite", "async")]
        public async Task Get_UnconvertibleKey_ThrowsBeforeTheConnection(string provider, string path)
        {
            OrmDataProvider p;
            switch (provider)
            {
                case "SqlServer": p = new SqlServerProbe(); break;
                case "PostgreSql": p = new PostgreSqlProbe(); break;
                case "MySql": p = new MySqlProbe(); break;
                default: p = new SqliteProbe(); break;
            }
            using (p)
            {
                ArgumentException exception = null;
                try
                {
                    if (path == "async") await p.GetAsync<KeyParameterHarness.KpNum>("two");
                    else p.Get<KeyParameterHarness.KpNum>("two");
                }
                catch (ArgumentException ex)
                {
                    exception = ex;
                }
                Assert.IsNotNull(exception, $"{provider} {path}: no ArgumentException");
                Assert.AreEqual("key", exception.ParamName, $"{provider} {path}: parameter name");
            }
        }

        [DataTestMethod]
        [DataRow("SqlServer")] [DataRow("PostgreSql")] [DataRow("MySql")] [DataRow("Sqlite")]
        public void TextSeams_RefuseAKey(string provider)
        {
            Func<object, string> select, where;
            IDisposable disposable;
            switch (provider)
            {
                case "SqlServer": { var p = new SqlServerProbe(); select = k => p.Select<KeyParameterHarness.KpText>(k); where = k => p.Where<KeyParameterHarness.KpText>(k); disposable = p; break; }
                case "PostgreSql": { var p = new PostgreSqlProbe(); select = k => p.Select<KeyParameterHarness.KpText>(k); where = k => p.Where<KeyParameterHarness.KpText>(k); disposable = p; break; }
                case "MySql": { var p = new MySqlProbe(); select = k => p.Select<KeyParameterHarness.KpText>(k); where = k => p.Where<KeyParameterHarness.KpText>(k); disposable = p; break; }
                default: { var p = new SqliteProbe(); select = k => p.Select<KeyParameterHarness.KpText>(k); where = k => p.Where<KeyParameterHarness.KpText>(k); disposable = p; break; }
            }
            using (disposable)
            {
                Assert.ThrowsException<NotSupportedException>(() => select("it's"), $"{provider}: CreateGetOneOrSelectCommandText with a key");
                Assert.ThrowsException<NotSupportedException>(() => where("it's"), $"{provider}: GetWhereClause");
            }
        }
    }
}
