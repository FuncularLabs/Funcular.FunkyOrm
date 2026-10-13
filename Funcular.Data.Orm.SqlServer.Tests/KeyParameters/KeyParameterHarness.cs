using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Funcular.Data.Orm.Attributes;

namespace Funcular.Data.Orm.Tests.KeyParameters
{
    /// <summary>
    /// The integration rows of the key-parameter plan (docs/plans/KEY_PARAMETER_PLAN.md, §4.1): <c>Get</c>,
    /// <c>GetAsync</c>, <c>Update</c> and <c>UpdateAsync</c> compare the primary key with a parameter, never with the
    /// key's text. One derived [TestClass] per provider supplies the DDL and the provider. This file is compiled into
    /// SqlServer.Tests (SQL Server, SQLite), PostgreSql.Tests and MySql.Tests.
    /// <para>
    /// Each row drops and creates <c>zz_kp_text</c>, <c>zz_kp_guid</c>, <c>zz_kp_num</c> and <c>zz_kp_pk</c>, seeds
    /// them (the text, Guid and pk rows through the provider's <c>Insert</c>, the numbers in the DDL), runs its calls,
    /// and drops the tables in <c>finally</c>.
    /// </para>
    /// </summary>
    public abstract class KeyParameterHarness
    {
        public const string TextTable = "zz_kp_text";
        public const string GuidTable = "zz_kp_guid";
        public const string NumTable = "zz_kp_num";
        public const string PkTable = "zz_kp_pk";

        /// <summary>The parameter the providers bind the key to (<c>OrmDataProvider.KeyParameterName</c>).</summary>
        public const string KeyParameter = "@pk";

        /// <summary>A Guid with hex letters, which FunkyORM stores in lower case on SQLite and MySQL.</summary>
        public static readonly Guid LetteredGuid = new Guid("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d");

        [Table(TextTable)]
        public class KpText
        {
            [Key] public string Name { get; set; }
            public int Val { get; set; }
        }

        /// <summary>The same table with a computed column, which takes the table-qualified SELECT path.</summary>
        [Table(TextTable)]
        public class KpTextComputed
        {
            [Key] public string Name { get; set; }
            public int Val { get; set; }

            [SqlExpression("{Val} + 100")]
            public int ValPlus { get; set; }
        }

        [Table(GuidTable)]
        public class KpGuid
        {
            [Key] public Guid Id { get; set; }
            public int Val { get; set; }
        }

        [Table(NumTable)]
        public class KpNum
        {
            [Key] public int Id { get; set; }
            public string Label { get; set; }
        }

        /// <summary>A key column named <c>pk</c>, whose name occurs inside the parameter's name.</summary>
        [Table(PkTable)]
        public class KpPk
        {
            [Key] public string Pk { get; set; }
            public int Val { get; set; }
        }

        /// <summary>The <c>pk</c> table with a computed column (the table-qualified SELECT path).</summary>
        [Table(PkTable)]
        public class KpPkComputed
        {
            [Key] public string Pk { get; set; }
            public int Val { get; set; }

            [SqlExpression("{Val} + 100")]
            public int ValPlus { get; set; }
        }

        /// <summary>The seeded text keys and their values.</summary>
        private static readonly (string Name, int Val)[] TextRows =
        {
            ("alpha", 1),
            ("it's", 2),
            ("alpha-beta", 3),
            ("back\\slash", 4),
            ("50%", 5),
            ("naïve-日本", 6),
        };

        #region Provider seam

        /// <summary>Marks the test inconclusive when the provider's test database is unavailable.</summary>
        protected virtual void RequireDatabase() { }

        /// <summary>
        /// Drops the four tables if they exist and creates them: <c>zz_kp_text (name, val)</c>,
        /// <c>zz_kp_guid (id, val)</c>, <c>zz_kp_num (id, label)</c> holding (1, 'one') and (2, 'two'), and
        /// <c>zz_kp_pk (pk, val)</c>. Text columns hold Unicode.
        /// </summary>
        protected abstract void CreateTables();

        /// <summary>Drops the four tables (or deletes the SQLite file).</summary>
        protected abstract void DropTables();

        protected abstract OrmDataProvider CreateProvider();

        #endregion

        #region Helpers

        private void Run(Func<Task> body) => RunAsync(body).GetAwaiter().GetResult();

        private async Task RunAsync(Func<Task> body)
        {
            RequireDatabase();
            var bodyFailed = false;
            try
            {
                CreateTables();
                using (var seeder = CreateProvider())
                {
                    foreach (var (name, val) in TextRows)
                        seeder.Insert(new KpText { Name = name, Val = val });
                    seeder.Insert(new KpGuid { Id = LetteredGuid, Val = 7 });
                    seeder.Insert(new KpPk { Pk = "it's", Val = 2 });
                    seeder.Insert(new KpPk { Pk = "pk", Val = 9 });
                }
                await body();
            }
            catch
            {
                bodyFailed = true;
                throw;
            }
            finally
            {
                try
                {
                    DropTables();
                }
                catch when (bodyFailed)
                {
                    // The body's failure is the one to report.
                }
            }
        }

        private static List<string> Capture(OrmDataProvider provider)
        {
            var logged = new List<string>();
            provider.Log = message => { lock (logged) logged.Add(message); };
            return logged;
        }

        private static readonly Regex ParameterLine = new Regex(@"^@\w+: ", RegexOptions.CultureInvariant);

        /// <summary>The logged SQL commands: not the parameter lines (<c>@name: value</c>) nor other messages.</summary>
        private static List<string> Commands(List<string> logged) =>
            logged.Where(m => !ParameterLine.IsMatch(m)
                              && (m.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                                  || m.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                                  || m.IndexOf("\nSELECT", StringComparison.OrdinalIgnoreCase) >= 0
                                  || m.IndexOf("\nUPDATE", StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

        /// <summary>AC3, positive: a command compares with the key parameter, and the parameter's value is logged.</summary>
        private static void AssertKeyBound(List<string> logged, string context)
        {
            var commands = Commands(logged);
            Assert.IsTrue(commands.Any(c => c.Contains("= " + KeyParameter)), $"{context}: no command compares with {KeyParameter}");
            Assert.IsTrue(logged.Any(m => m.StartsWith(KeyParameter + ": ", StringComparison.Ordinal)),
                $"{context}: {KeyParameter} wasn't bound");
        }

        /// <summary>AC3: the key is bound, and no logged command contains the key's text.</summary>
        private static void AssertKeyNotInlined(List<string> logged, string key, string context)
        {
            AssertKeyBound(logged, context);
            foreach (var command in Commands(logged))
                Assert.IsFalse(command.Contains(key), $"{context}: the key's text is in the SQL: {command}");
        }

        private static async Task<T> Get<T>(OrmDataProvider provider, object key, string path) where T : class, new() =>
            path == "async" ? await provider.GetAsync<T>(key) : provider.Get<T>(key);

        private int CountNumRows()
        {
            using (var provider = CreateProvider())
                return provider.Query<KpNum>().Count();
        }

        #endregion

        /// <summary>
        /// AC1, AC3: a string key with a quote, a hyphen, a backslash, a percent sign or non-ASCII letters finds exactly
        /// its row.
        /// </summary>
        [DataTestMethod]
        [DataRow("alpha", 1, "sync")] [DataRow("alpha", 1, "async")]
        [DataRow("it's", 2, "sync")] [DataRow("it's", 2, "async")]
        [DataRow("alpha-beta", 3, "sync")] [DataRow("alpha-beta", 3, "async")]
        [DataRow("back\\slash", 4, "sync")] [DataRow("back\\slash", 4, "async")]
        [DataRow("50%", 5, "sync")] [DataRow("50%", 5, "async")]
        [DataRow("naïve-日本", 6, "sync")] [DataRow("naïve-日本", 6, "async")]
        public void Get_StringKey_ReturnsThatRow(string key, int val, string path) => Run(async () =>
        {
            using var provider = CreateProvider();
            var logged = Capture(provider);
            var row = await Get<KpText>(provider, key, path);
            Assert.IsNotNull(row, $"{key} {path}: no row");
            Assert.AreEqual(key, row.Name, $"{key} {path}: name");
            Assert.AreEqual(val, row.Val, $"{key} {path}: val");
            AssertKeyNotInlined(logged, key, $"{key} {path}");
        });

        /// <summary>
        /// AC2, AC3: a key that would change an inlined statement matches no row and changes nothing; it is only
        /// ever compared as text.
        /// </summary>
        [DataTestMethod]
        [DataRow("x' OR '1'='1", "sync")] [DataRow("x' OR '1'='1", "async")]
        [DataRow("0 OR 1=1", "sync")] [DataRow("0 OR 1=1", "async")]
        [DataRow("x'; DELETE FROM zz_kp_num; --", "sync")] [DataRow("x'; DELETE FROM zz_kp_num; --", "async")]
        [DataRow("0; DELETE FROM zz_kp_num", "sync")] [DataRow("0; DELETE FROM zz_kp_num", "async")]
        [DataRow("it''s", "sync")] [DataRow("it''s", "async")]
        [DataRow("x\\' OR 1=1 -- ", "sync")] [DataRow("x\\' OR 1=1 -- ", "async")]
        public void Get_KeyThatWouldAlterTheStatement_MatchesNoRow(string key, string path) => Run(async () =>
        {
            using (var provider = CreateProvider())
            {
                var logged = Capture(provider);
                var row = await Get<KpText>(provider, key, path);
                Assert.IsNull(row, $"{key} {path}: a row matched ({row?.Name})");
                AssertKeyNotInlined(logged, key, $"{key} {path}");
            }
            Assert.AreEqual(2, CountNumRows(), $"{key} {path}: {NumTable} changed");
        });

        /// <summary>AC4: Update and UpdateAsync of a row whose key holds a quote update that row and no other.</summary>
        [DataTestMethod]
        [DataRow("sync")] [DataRow("async")]
        public void Update_StringKeyWithQuote_UpdatesThatRow(string path) => Run(async () =>
        {
            using (var provider = CreateProvider())
            {
                var logged = Capture(provider);
                var entity = new KpText { Name = "it's", Val = 20 };
                if (path == "async") await provider.UpdateAsync(entity);
                else provider.Update(entity);
                AssertKeyNotInlined(logged, "it's", path);
            }
            using (var provider = CreateProvider())
            {
                Assert.AreEqual(20, provider.Get<KpText>("it's")?.Val, $"{path}: the row wasn't updated");
                Assert.AreEqual(1, provider.Get<KpText>("alpha")?.Val, $"{path}: another row changed");
                Assert.AreEqual(3, provider.Get<KpText>("alpha-beta")?.Val, $"{path}: another row changed");
            }
        });

        /// <summary>AC4, AC5: Update and UpdateAsync of a row with a Guid key (lower-case storage on SQLite and MySQL).</summary>
        [DataTestMethod]
        [DataRow("sync")] [DataRow("async")]
        public void Update_GuidKey_UpdatesThatRow(string path) => Run(async () =>
        {
            using (var provider = CreateProvider())
            {
                var logged = Capture(provider);
                var entity = new KpGuid { Id = LetteredGuid, Val = 70 };
                if (path == "async") await provider.UpdateAsync(entity);
                else provider.Update(entity);
                AssertKeyNotInlined(logged, LetteredGuid.ToString(), path);
            }
            using (var provider = CreateProvider())
                Assert.AreEqual(70, provider.Get<KpGuid>(LetteredGuid)?.Val, $"{path}: the row wasn't updated");
        });

        /// <summary>AC5: a Guid key, as a Guid or as its text in either case, finds its row; an absent one finds none.</summary>
        [DataTestMethod]
        [DataRow("guid", "sync")] [DataRow("guid", "async")]
        [DataRow("text", "sync")] [DataRow("text", "async")]
        [DataRow("upperText", "sync")] [DataRow("upperText", "async")]
        [DataRow("absent", "sync")] [DataRow("absent", "async")]
        public void Get_GuidKey_ReturnsThatRow(string form, string path) => Run(async () =>
        {
            object key;
            switch (form)
            {
                case "guid": key = LetteredGuid; break;
                case "text": key = LetteredGuid.ToString(); break;
                case "upperText": key = LetteredGuid.ToString().ToUpperInvariant(); break;
                default: key = new Guid("00000000-0000-4000-8000-000000000001"); break;
            }
            using var provider = CreateProvider();
            var logged = Capture(provider);
            var row = await Get<KpGuid>(provider, key, path);
            AssertKeyNotInlined(logged, key.ToString(), $"{form} {path}");
            if (form == "absent")
            {
                Assert.IsNull(row, $"{form} {path}: a row matched");
                return;
            }
            Assert.IsNotNull(row, $"{form} {path}: no row");
            Assert.AreEqual(LetteredGuid, row.Id, $"{form} {path}: id");
            Assert.AreEqual(7, row.Val, $"{form} {path}: val");
        });

        /// <summary>AC6: an integer key given as an int, a long, a short or its text finds its row, as in 3.10.0.</summary>
        [DataTestMethod]
        [DataRow("int", "sync")] [DataRow("int", "async")]
        [DataRow("long", "sync")] [DataRow("long", "async")]
        [DataRow("short", "sync")] [DataRow("short", "async")]
        [DataRow("text", "sync")] [DataRow("text", "async")]
        public void Get_IntegerKey_ConvertsToThePrimaryKeyType(string form, string path) => Run(async () =>
        {
            object key;
            switch (form)
            {
                case "int": key = 2; break;
                case "long": key = 2L; break;
                case "short": key = (short)2; break;
                default: key = "2"; break;
            }
            using var provider = CreateProvider();
            var logged = Capture(provider);
            var row = await Get<KpNum>(provider, key, path);
            Assert.IsNotNull(row, $"{form} {path}: no row");
            Assert.AreEqual(2, row.Id, $"{form} {path}: id");
            Assert.AreEqual("two", row.Label, $"{form} {path}: label");
            AssertKeyBound(logged, $"{form} {path}");
        });

        /// <summary>
        /// AC6: a key that can't be converted to the primary key's type throws ArgumentException before any command
        /// is sent, and nothing the exception carries repeats the key.
        /// </summary>
        [DataTestMethod]
        [DataRow("num", "0' OR '1'='1", "sync")] [DataRow("num", "0' OR '1'='1", "async")]
        [DataRow("num", "0 OR 1=1", "sync")] [DataRow("num", "0 OR 1=1", "async")]
        [DataRow("num", "0; DELETE FROM zz_kp_num", "sync")] [DataRow("num", "0; DELETE FROM zz_kp_num", "async")]
        [DataRow("num", "two", "sync")] [DataRow("num", "two", "async")]
        [DataRow("num", "", "sync")] [DataRow("num", "", "async")]
        [DataRow("num", 2.5, "sync")] [DataRow("num", 2.5, "async")]
        [DataRow("guid", "not-a-guid", "sync")] [DataRow("guid", "not-a-guid", "async")]
        [DataRow("guid", "x' OR '1'='1", "sync")] [DataRow("guid", "x' OR '1'='1", "async")]
        public void Get_UnconvertibleKey_ThrowsBeforeAnyCommand(string table, object key, string path) => Run(async () =>
        {
            using (var provider = CreateProvider())
            {
                var logged = Capture(provider);
                ArgumentException exception = null;
                try
                {
                    if (table == "num") await Get<KpNum>(provider, key, path);
                    else await Get<KpGuid>(provider, key, path);
                }
                catch (ArgumentException ex)
                {
                    exception = ex;
                }
                Assert.IsNotNull(exception, $"{table} '{key}' {path}: no ArgumentException");
                Assert.AreEqual("key", exception.ParamName, $"{table} '{key}' {path}: parameter name");
                Assert.AreEqual(0, Commands(logged).Count, $"{table} '{key}' {path}: a command was sent");
                if (key is string text && text.Length > 0)
                    Assert.IsFalse(exception.ToString().Contains(text), $"{table} '{key}' {path}: the exception repeats the key");
            }
            Assert.AreEqual(2, CountNumRows(), $"{table} '{key}' {path}: {NumTable} changed");
        });

        /// <summary>AC9: <c>Get</c> with a null key keeps 3.10.0's unfiltered SELECT.</summary>
        [DataTestMethod]
        [DataRow("sync")] [DataRow("async")]
        public void Get_NullKey_KeepsTheUnfilteredSelect(string path) => Run(async () =>
        {
            using var provider = CreateProvider();
            var logged = Capture(provider);
            var row = await Get<KpText>(provider, null, path);
            Assert.IsNotNull(row, $"{path}: no row");
            var commands = Commands(logged);
            Assert.IsTrue(commands.Count > 0, $"{path}: no command was logged");
            foreach (var command in commands)
                Assert.IsFalse(command.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase) >= 0, $"{path}: {command}");
        });

        /// <summary>AC7: the table-qualified SELECT (an entity with a computed column) binds the key too.</summary>
        [DataTestMethod]
        [DataRow("it's", 2, "sync")] [DataRow("it's", 2, "async")]
        [DataRow("x' OR '1'='1", 0, "sync")] [DataRow("x' OR '1'='1", 0, "async")]
        public void Get_ComputedColumnEntity_BindsTheKey(string key, int val, string path) => Run(async () =>
        {
            using var provider = CreateProvider();
            var logged = Capture(provider);
            var row = await Get<KpTextComputed>(provider, key, path);
            if (val == 0)
            {
                Assert.IsNull(row, $"{key} {path}: a row matched ({row?.Name})");
            }
            else
            {
                Assert.IsNotNull(row, $"{key} {path}: no row");
                Assert.AreEqual(val, row.Val, $"{key} {path}: val");
                Assert.AreEqual(val + 100, row.ValPlus, $"{key} {path}: computed column");
            }
            AssertKeyNotInlined(logged, key, $"{key} {path}");
        });

        /// <summary>
        /// AC7: on the table-qualified path, a key column named <c>pk</c> (a name inside the parameter's) still compares
        /// with the parameter, and a key whose text is the column's name is found as text.
        /// </summary>
        [DataTestMethod]
        [DataRow("it's", 2, "sync")] [DataRow("it's", 2, "async")]
        [DataRow("pk", 9, "sync")] [DataRow("pk", 9, "async")]
        public void Get_ComputedColumnEntity_KeyColumnNamedPk(string key, int val, string path) => Run(async () =>
        {
            using var provider = CreateProvider();
            var logged = Capture(provider);
            var row = await Get<KpPkComputed>(provider, key, path);
            Assert.IsNotNull(row, $"{key} {path}: no row");
            Assert.AreEqual(key, row.Pk, $"{key} {path}: pk");
            Assert.AreEqual(val + 100, row.ValPlus, $"{key} {path}: computed column");
            AssertKeyBound(logged, $"{key} {path}");
        });
    }
}
