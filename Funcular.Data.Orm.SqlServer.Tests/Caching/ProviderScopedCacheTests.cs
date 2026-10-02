using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.IO;
using System.Linq;
using Funcular.Data.Orm.MySql;
using Funcular.Data.Orm.PostgreSql;
using Funcular.Data.Orm.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using static Funcular.Data.Orm.SqlServer.Tests.Caching.CacheScopeTestSupport;

namespace Funcular.Data.Orm.SqlServer.Tests.Caching
{
    /// <summary>
    /// The DB-free rows of the provider-scoped caches plan (docs/plans/PROVIDER_SCOPED_CACHES_PLAN.md, §4.1).
    /// <para>
    /// Every row uses its own entity types and unique fake connection strings, plants values with unique keys and
    /// removes them, and opens no connection: every entity type here carries <c>[Table]</c>, so no table name is looked
    /// up in a database. SQLite probe paths are checked after each test: none may exist.
    /// </para>
    /// </summary>
    [TestClass]
    public class ProviderScopedCacheTests
    {
        private readonly List<string> _sqliteProbePaths = new List<string>();
        private readonly List<IDisposable> _disposables = new List<IDisposable>();

        [TestCleanup]
        public void AssertNoSqliteProbeFileCreated()
        {
            foreach (var disposable in _disposables)
                disposable.Dispose();
            var created = _sqliteProbePaths.Where(File.Exists).ToList();
            foreach (var path in created)
                DeleteSqliteDatabase(path);
            Assert.AreEqual(0, created.Count, "a DB-free row created a SQLite file: " + string.Join(", ", created));
        }

        private string Server(string provider)
        {
            var server = FakeServer(provider);
            if (provider == SqliteKind) _sqliteProbePaths.Add(server);
            return server;
        }

        private T Track<T>(T disposable) where T : IDisposable
        {
            _disposables.Add(disposable);
            return disposable;
        }

        public static IEnumerable<object[]> AllProviders => new[]
        {
            new object[] { SqlServerKind }, new object[] { PostgreSqlKind }, new object[] { MySqlKind }, new object[] { SqliteKind }
        };

        #region AC1 — dialect isolation

        // One type per order, so the first provider of each order is the first to resolve its names.
        [Table("User")] public class PscReservedOrder1 { [Key, Column("Key")] public int Key { get; set; } [Column("Order")] public int Order { get; set; } }
        [Table("User")] public class PscReservedOrder2 { [Key, Column("Key")] public int Key { get; set; } [Column("Order")] public int Order { get; set; } }
        [Table("User")] public class PscReservedOrder3 { [Key, Column("Key")] public int Key { get; set; } [Column("Order")] public int Order { get; set; } }
        [Table("User")] public class PscReservedOrder4 { [Key, Column("Key")] public int Key { get; set; } [Column("Order")] public int Order { get; set; } }

        [DataTestMethod]
        [DataRow(1, "SqlServer,PostgreSql")]
        [DataRow(2, "PostgreSql,SqlServer")]
        [DataRow(3, "MySql,SqlServer,PostgreSql,Sqlite")]
        [DataRow(4, "Sqlite,PostgreSql,MySql,SqlServer")]
        public void CrossProvider_TableAndColumnNames_UseEachProvidersDialect(int order, string providers)
        {
            switch (order)
            {
                case 1: AssertEachProvidersDialect<PscReservedOrder1>(providers); break;
                case 2: AssertEachProvidersDialect<PscReservedOrder2>(providers); break;
                case 3: AssertEachProvidersDialect<PscReservedOrder3>(providers); break;
                default: AssertEachProvidersDialect<PscReservedOrder4>(providers); break;
            }
        }

        private void AssertEachProvidersDialect<T>(string providers) where T : class, new()
        {
            foreach (var provider in providers.Split(','))
            {
                var dialect = DefaultDialect(provider);
                using var instance = Create(provider, FakeConnectionString(provider, Server(provider)));
                Assert.AreEqual(dialect.EncloseIdentifier("User"), TableName<T>(instance), $"{providers}: {provider} table");
                if (provider == SqliteKind) continue; // SQLite's column names aren't dialect-quoted in the cache (plan §4.1).
                Assert.AreEqual(dialect.EncloseIdentifier("Key"), ColumnName(instance, Property<T>("Key")), $"{providers}: {provider} Key");
                Assert.AreEqual(dialect.EncloseIdentifier("Order"), ColumnName(instance, Property<T>("Order")), $"{providers}: {provider} Order");
            }
        }

        private static string TableName<T>(OrmDataProvider provider) where T : class, new()
        {
            switch (provider)
            {
                case SqlServerOrmDataProvider p: return p.GetTableNameInternal<T>();
                case PostgreSqlOrmDataProvider p: return p.GetTableNameInternal<T>();
                case MySqlOrmDataProvider p: return p.GetTableNameInternal<T>();
                case SqliteOrmDataProvider p: return p.GetTableNameInternal<T>();
                default: throw new ArgumentOutOfRangeException(nameof(provider));
            }
        }

        private static string ColumnName(OrmDataProvider provider, System.Reflection.PropertyInfo property)
        {
            switch (provider)
            {
                case SqlServerOrmDataProvider p: return p.GetCachedColumnNameInternal(property);
                case PostgreSqlOrmDataProvider p: return p.GetCachedColumnNameInternal(property);
                case MySqlOrmDataProvider p: return p.GetCachedColumnNameInternal(property);
                case SqliteOrmDataProvider p: return p.GetCachedColumnNameInternal(property);
                default: throw new ArgumentOutOfRangeException(nameof(provider));
            }
        }

        #endregion

        #region AC3 — type-name isolation

        public static class PscOuterA
        {
            [Table("zz_psc_thing_a")]
            public class PscThing
            {
                [Key] public int Id { get; set; }
                [Column("alpha_label")] public string Label { get; set; }
            }
        }

        public static class PscOuterB
        {
            [Table("zz_psc_thing_b")]
            public class PscThing
            {
                [Key] public int Id { get; set; }
                [Column("beta_label")] public string Label { get; set; }
            }
        }

        [TestMethod]
        public void SameSimpleTypeName_ColumnsDoNotCollide()
        {
            // Two types whose simple names are the same must not share a column-name key (SQL Server's key path).
            using var provider = new SqlServerOrmDataProvider(FakeConnectionString(SqlServerKind, Server(SqlServerKind)));

            Assert.AreEqual("alpha_label", provider.GetCachedColumnNameInternal(Property<PscOuterA.PscThing>("Label")));
            Assert.AreEqual("beta_label", provider.GetCachedColumnNameInternal(Property<PscOuterB.PscThing>("Label")));
        }

        // Full names that differ only by an underscore: PscOuter_X+PscXThing and PscOuterX+PscXThing.
        public static class PscOuter_X
        {
            [Table("zz_psc_thing_x")]
            public class PscXThing
            {
                [Key] public int Id { get; set; }
                [Column("x_label")] public string Label { get; set; }
            }
        }

        public static class PscOuterX
        {
            [Table("zz_psc_thing_y")]
            public class PscXThing
            {
                [Key] public int Id { get; set; }
                [Column("y_label")] public string Label { get; set; }
            }
        }

        [TestMethod]
        public void FullNamesDifferingOnlyByUnderscores_DoNotShareColumns()
        {
            // SQLite resolves columns through Core's base GetCachedColumnName (it has no override).
            using var provider = new SqliteOrmDataProvider(FakeConnectionString(SqliteKind, Server(SqliteKind)));

            Assert.AreEqual("x_label", provider.GetCachedColumnNameInternal(Property<PscOuter_X.PscXThing>("Label")));
            Assert.AreEqual("y_label", provider.GetCachedColumnNameInternal(Property<PscOuterX.PscXThing>("Label")));
        }

        #endregion

        #region AC4 — sharing

        [DataTestMethod]
        [DynamicData(nameof(AllProviders))]
        public void SameScope_ShareOneCacheSet(string provider)
        {
            var connectionString = FakeConnectionString(provider, Server(provider));
            using var a = ScopeProbes.Create(provider, connectionString);
            using var b = ScopeProbes.Create(provider, connectionString);

            AssertSameScope(a, b, $"{provider}: two probes on one connection string");
        }

        public static IEnumerable<object[]> PasswordSynonyms => new[]
        {
            new object[] { SqlServerKind, "Password" }, new object[] { SqlServerKind, "PWD" },
            new object[] { PostgreSqlKind, "Password" }, new object[] { PostgreSqlKind, "PSW" }, new object[] { PostgreSqlKind, "PWD" },
            new object[] { MySqlKind, "Password" }, new object[] { MySqlKind, "pwd" },
            new object[] { SqliteKind, "Password" },
        };

        [DataTestMethod]
        [DynamicData(nameof(PasswordSynonyms))]
        public void PasswordOnlyDifference_SharesAScope(string provider, string synonym)
        {
            var server = Server(provider);
            using var a = ScopeProbes.Create(provider, FakeConnectionString(provider, server, password: "zz-psc-pw-1"));
            using var b = ScopeProbes.Create(provider,
                FakeConnectionString(provider, server, passwordKeyword: synonym, password: "zz-psc-pw-2"));

            AssertSameScope(a, b, $"{provider}: password only, as '{synonym}'");
        }

        public static IEnumerable<object[]> OtherConnectionDifferences => new[]
        {
            new object[] { SqlServerKind, "server" }, new object[] { SqlServerKind, "database" }, new object[] { SqlServerKind, "user" },
            new object[] { PostgreSqlKind, "server" }, new object[] { PostgreSqlKind, "database" }, new object[] { PostgreSqlKind, "user" },
            new object[] { PostgreSqlKind, "Search Path" }, new object[] { PostgreSqlKind, "Options" },
            new object[] { MySqlKind, "server" }, new object[] { MySqlKind, "database" }, new object[] { MySqlKind, "user" },
            new object[] { SqliteKind, "database" },
        };

        [DataTestMethod]
        [DynamicData(nameof(OtherConnectionDifferences))]
        public void OtherConnectionDifference_IsAnotherScope(string provider, string component)
        {
            var server = Server(provider);
            string a, b;
            switch (component)
            {
                case "server":
                    a = FakeConnectionString(provider, server);
                    b = FakeConnectionString(provider, Server(provider));
                    break;
                case "database":
                    a = FakeConnectionString(provider, server, database: "zz_psc_db_a");
                    b = provider == SqliteKind
                        ? FakeConnectionString(provider, Server(provider)) // SQLite: the database is the file
                        : FakeConnectionString(provider, server, database: "zz_psc_db_b");
                    break;
                case "user":
                    a = FakeConnectionString(provider, server, user: "zz_psc_user_a");
                    b = FakeConnectionString(provider, server, user: "zz_psc_user_b");
                    break;
                case "Search Path":
                    a = FakeConnectionString(provider, server, extra: ";Search Path=zz_psc_a");
                    b = FakeConnectionString(provider, server, extra: ";Search Path=zz_psc_b");
                    break;
                case "Options":
                    a = FakeConnectionString(provider, server, extra: ";Options=-c search_path=zz_psc_a");
                    b = FakeConnectionString(provider, server, extra: ";Options=-c search_path=zz_psc_b");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(component), component, null);
            }

            using var first = Create(provider, a);
            using var second = Create(provider, b);
            AssertOtherScope(first, second, $"{provider}: {component} differs");
        }

        [Table("zz_psc_named")]
        public class PscNamed
        {
            [Key] public int Id { get; set; }
        }

        /// <summary>A SQL Server provider subclass with its own table naming (why D1 keys on the provider type).</summary>
        private sealed class RenamingSqlServerProvider : SqlServerOrmDataProvider
        {
            public RenamingSqlServerProvider(string connectionString) : base(connectionString) { }

            protected override string GetTableName<T>() =>
                ScopeTableNames.GetOrAdd(typeof(T), t => "renamed_" + t.Name.ToLowerInvariant());
        }

        [DataTestMethod]
        [DataRow("provider subclass")]
        [DataRow("dialect type")]
        public void ProviderTypeOrDialectTypeDifference_IsAnotherScope(string difference)
        {
            var connectionString = FakeConnectionString(SqlServerKind, Server(SqlServerKind));
            using var plain = new SqlServerOrmDataProvider(connectionString);
            if (difference == "provider subclass")
            {
                using var renaming = new RenamingSqlServerProvider(connectionString);
                Assert.AreEqual("zz_psc_named", plain.GetTableNameInternal<PscNamed>());
                Assert.AreEqual("renamed_pscnamed", renaming.GetTableNameInternal<PscNamed>(),
                    "the subclass must resolve with its own naming, not read the base provider's cached name");
                AssertOtherScope(plain, renaming, "SqlServer: provider subclass");
            }
            else
            {
                using var otherDialect = new SqlServerOrmDataProvider(connectionString,
                    dialect: new AlwaysQuoteDialect(new SqlServerDialect(), "\"", "\""));
                AssertOtherScope(plain, otherDialect, "SqlServer: dialect type");
            }
        }

        public static IEnumerable<object[]> UnparseableProviders => new[]
        {
            new object[] { PostgreSqlKind }, new object[] { MySqlKind }, new object[] { "SqlServer with an explicit connection" },
        };

        [DataTestMethod]
        [DynamicData(nameof(UnparseableProviders))]
        public void UnparseableConnectionString_IsHashed(string provider)
        {
            var garbage = $"zz-psc-unparseable {Unique()}";
            using var instance = provider == PostgreSqlKind ? new PostgreSqlOrmDataProvider(garbage)
                : provider == MySqlKind ? (OrmDataProvider)new MySqlOrmDataProvider(garbage)
                : new SqlServerOrmDataProvider(garbage, Track(new SqlConnection()));

            var key = instance.CacheScopeKey;
            Assert.IsTrue(key.HasValue, $"{provider}: no registry key (the identity is not computed)");
            Assert.AreEqual(Sha256Hex(garbage), key.Value.IdentityHash, ignoreCase: true,
                $"{provider}: a string the builder rejects is hashed as given");
            Assert.IsFalse(key.Value.ToString().Contains(garbage), $"{provider}: the registry key keeps the raw string");
        }

        [DataTestMethod]
        [DataRow(PostgreSqlKind)]
        [DataRow(MySqlKind)]
        [DataRow(SqlServerKind)]
        [DataRow(SqliteKind)]
        public void RegistryKey_RetainsNoSecret(string provider)
        {
            var server = Server(provider);
            var secret1 = $"zz-psc-secret1-{Unique()}";
            var secret2 = $"zz-psc-secret2-{Unique()}";
            string connectionString;
            switch (provider)
            {
                case PostgreSqlKind: // the password first, plus SSL Password
                    connectionString = $"Password={secret1};Host={server};Database=zz_psc_db;Username=zz_psc_user;SSL Password={secret2}";
                    break;
                case MySqlKind:
                    connectionString = $"Server={server};Database=zz_psc_db;User ID=zz_psc_user;Password={secret1};Certificate Password={secret2}";
                    break;
                default: // SQL Server and SQLite have no other secret keyword
                    connectionString = FakeConnectionString(provider, server, password: secret1);
                    break;
            }

            using var instance = ScopeProbes.Create(provider, connectionString);
            var key = instance.CacheScopeKey;
            Assert.IsTrue(key.HasValue, $"{provider}: no registry key");
            var keyText = key.Value.ToString();
            Assert.IsFalse(keyText.Contains(secret1), $"{provider}: the registry key retains the password");
            Assert.IsFalse(keyText.Contains(secret2), $"{provider}: the registry key retains the second secret");
            Assert.IsTrue(IsSha256Hex(key.Value.IdentityHash), $"{provider}: the identity part is not a SHA-256 (64 hex characters)");
            Assert.IsTrue(CacheScopeRegistry.Keys.Contains(key.Value), $"{provider}: the key is not registered");
            Assert.IsFalse(CacheScopeRegistry.Keys.Any(k => k.ToString().Contains(secret1) || k.ToString().Contains(secret2)),
                $"{provider}: a registered key retains a secret");
            if (provider == SqlServerKind || provider == SqliteKind)
            {
                var identity = ((IScopeProbe)instance).Identity;
                Assert.IsFalse(string.IsNullOrEmpty(identity), $"{provider}: the canonical identity is empty");
                Assert.AreEqual(Sha256Hex(identity), key.Value.IdentityHash, ignoreCase: true,
                    $"{provider}: the key part is the SHA-256 of the canonical identity");
            }
        }

        public static IEnumerable<object[]> SqliteMemoryAndTemporaryStrings => new[]
        {
            new object[] { "Data Source=:memory:", false },
            new object[] { "Filename=:memory:", false },
            new object[] { "Data Source=zz_psc_mem_{0};Mode=Memory", false },
            new object[] { "Data Source=zz_psc_mem_{0};Mode=Memory;Cache=Shared", false },
            new object[] { "", true },
            new object[] { "Data Source=", true },
        };

        [DataTestMethod]
        [DynamicData(nameof(SqliteMemoryAndTemporaryStrings))]
        public void SqliteMemoryAndTemporaryDatabases_NeverShare(string pattern, bool explicitConnection)
        {
            var unique = Unique();
            var connectionString = string.Format(pattern, unique);
            var memoryName = $"zz_psc_mem_{unique}";
            _sqliteProbePaths.Add(Path.GetFullPath(memoryName));
            _sqliteProbePaths.Add(Path.Combine(Path.GetTempPath(), memoryName));

            using var a = new SqliteOrmDataProvider(connectionString,
                explicitConnection ? Track(new SqliteConnection(connectionString)) : null);
            using var b = new SqliteOrmDataProvider(connectionString,
                explicitConnection ? Track(new SqliteConnection(connectionString)) : null);

            AssertOtherScope(a, b, $"SQLite '{connectionString}'{(explicitConnection ? " with an explicit connection" : "")}");
        }

        [TestMethod]
        public void EmptyIdentity_IsPerProviderType()
        {
            using var a1 = new DirectProviderA();
            using var a2 = new DirectProviderA();
            using var b = new DirectProviderB();
            var key = $"zz_psc.empty.{Unique()}";
            a1.Columns[key] = "zz_psc_planted";
            try
            {
                Assert.IsFalse(b.Columns.ContainsKey(key), "another direct subclass reads the same scope");
                Assert.IsTrue(a2.Columns.TryGetValue(key, out var seen) && seen == "zz_psc_planted",
                    "two instances of one direct subclass don't share a scope");
                Assert.AreSame(a1.Tables, a2.Tables, "one direct subclass: table-name cache");
                Assert.AreNotSame(a1.Tables, b.Tables, "two direct subclasses: table-name cache");
            }
            finally
            {
                a1.Columns.TryRemove(key, out _);
            }
        }

        [DataTestMethod]
        [DynamicData(nameof(AllProviders))]
        public void ExplicitConnection_SuppliesTheIdentity(string provider)
        {
            var server = Server(provider);
            var databaseB = provider == SqliteKind ? Server(provider) : server;
            var stringA = FakeConnectionString(provider, server, database: "zz_psc_db_a");
            var stringB = FakeConnectionString(provider, databaseB, database: "zz_psc_db_b");

            using var toA = Create(provider, string.Empty, Track(Connection(provider, stringA)));
            using var toB = Create(provider, string.Empty, Track(Connection(provider, stringB)));
            using var alsoToA = Create(provider, string.Empty, Track(Connection(provider, stringA)));

            AssertOtherScope(toA, toB, $"{provider}: explicit connections to different databases, empty constructor string");
            AssertSameScope(toA, alsoToA, $"{provider}: explicit connections to one database, empty constructor string");
        }

        [Table("zz_psc_mapped_marker")]
        public class PscMappedMarker
        {
            [Key] public int Id { get; set; }
        }

        [DataTestMethod]
        [DynamicData(nameof(AllProviders))]
        public void MappedSetAccessor_ReadsTheInstanceScope(string provider)
        {
            using var a = ScopeProbes.Create(provider, FakeConnectionString(provider, Server(provider)));
            using var b = ScopeProbes.Create(provider, FakeConnectionString(provider, Server(provider)));
            var planted = ((IScopeProbe)a).MappedTypesOfScope;
            planted.Add(typeof(PscMappedMarker));
            try
            {
                Assert.IsTrue(CachesOf(a).Mapped.Contains(typeof(PscMappedMarker)),
                    $"{provider}: A's mapped-set accessor doesn't show the type marked mapped in A's scope");
                Assert.IsFalse(CachesOf(b).Mapped.Contains(typeof(PscMappedMarker)),
                    $"{provider}: B's mapped-set accessor shows a type marked mapped in A's scope");
            }
            finally
            {
                planted.Remove(typeof(PscMappedMarker));
            }
        }

        #endregion

        #region AC5 — dialect-instance isolation (names)

        [Table("zz_psc_plain_names")]
        public class PscPlainNames
        {
            [Key] public int Id { get; set; }
            [Column("caption")] public string Caption { get; set; }
        }

        [DataTestMethod]
        [DynamicData(nameof(AllProviders))]
        public void SameProviderType_DifferentDialectType_DoNotShareNames(string provider)
        {
            var connectionString = FakeConnectionString(provider, Server(provider));
            var quote = provider == MySqlKind ? "`" : "\"";
            using var standard = Create(provider, connectionString);
            using var quoted = Create(provider, connectionString,
                dialect: new AlwaysQuoteDialect(DefaultDialect(provider), quote, quote));

            Assert.AreEqual("zz_psc_plain_names", TableName<PscPlainNames>(standard), $"{provider}: default dialect table");
            Assert.AreEqual($"{quote}zz_psc_plain_names{quote}", TableName<PscPlainNames>(quoted), $"{provider}: quoting dialect table");
            if (provider == SqliteKind) return; // SQLite's column names aren't dialect-quoted in the cache (Core's base method).
            Assert.AreEqual("caption", ColumnName(standard, Property<PscPlainNames>("Caption")), $"{provider}: default dialect column");
            Assert.AreEqual($"{quote}caption{quote}", ColumnName(quoted, Property<PscPlainNames>("Caption")), $"{provider}: quoting dialect column");
        }

        #endregion

        #region AC6 — no bare keys

        [Table("zz_psc_bare")]
        public class PscBare
        {
            [Key] public int Id { get; set; }
            public string ZzPscBareKey { get; set; }
        }

        [TestMethod]
        public void ComputeColumnName_IgnoresABareNameKey()
        {
            using var provider = new SqlServerOrmDataProvider(FakeConnectionString(SqlServerKind, Server(SqlServerKind)));
            var property = Property<PscBare>(nameof(PscBare.ZzPscBareKey));
            var bareKey = property.Name.ToLowerInvariant();
            var columns = CachesOf(provider).Columns;
            columns[bareKey] = "zz_psc_planted_bare";
            try
            {
                Assert.AreEqual("zzpscbarekey", provider.ComputeColumnName(property),
                    "ComputeColumnName used a cache entry keyed by the bare property name");
            }
            finally
            {
                columns.TryRemove(bareKey, out _);
            }
        }

        #endregion

        #region AC2 — the unmapped set

        [Table("zz_psc_unmapped")]
        public class PscUnmapped
        {
            [Key] public int Id { get; set; }
            public string X { get; set; }
        }

        [DataTestMethod]
        [DynamicData(nameof(AllProviders))]
        public void UnmappedSet_IsComputedFromTheInstanceScope(string provider)
        {
            using var a = ScopeProbes.Create(provider, FakeConnectionString(provider, Server(provider)));
            using var b = ScopeProbes.Create(provider, FakeConnectionString(provider, Server(provider)));
            var x = Property<PscUnmapped>(nameof(PscUnmapped.X));
            var key = x.ToDictionaryKey();
            var columnsA = CachesOf(a).Columns;
            columnsA[key] = "zz_psc_x";
            try
            {
                Assert.IsFalse(((IScopeProbe)a).UnmappedOf<PscUnmapped>().Any(p => p.Name == "X"),
                    $"{provider}: A has a column for X in its scope, so X is mapped for A");
                Assert.IsTrue(((IScopeProbe)b).UnmappedOf<PscUnmapped>().Any(p => p.Name == "X"),
                    $"{provider}: B's unmapped set was computed from A's planted column (X has no column in B's scope)");
            }
            finally
            {
                columnsA.TryRemove(key, out _);
            }
        }

        [Table("zz_psc_core_unmapped")]
        public class PscCoreUnmapped
        {
            [Key] public int Id { get; set; }
            public string Y { get; set; }
        }

        [TestMethod]
        public void CoreGetUnmappedProperties_ReadsTheInstanceScope()
        {
            using var probe = new CoreUnmappedProbe();
            var planted = new List<System.Reflection.PropertyInfo> { Property<PscCoreUnmapped>(nameof(PscCoreUnmapped.Y)) };
            probe.Unmapped[typeof(PscCoreUnmapped)] = planted;
            try
            {
                Assert.AreSame(planted, probe.CoreUnmappedOf<PscCoreUnmapped>(),
                    "Core's GetUnmappedProperties<T>() doesn't read the instance's scope");
            }
            finally
            {
                probe.Unmapped.TryRemove(typeof(PscCoreUnmapped), out _);
            }
        }

        #endregion

        #region AC9 — procedure names

        public class PscProcedureRow
        {
            public int Id { get; set; }
        }

        [DataTestMethod]
        [DataRow(SqlServerKind)]
        [DataRow(MySqlKind)]
        public void ProcedureName_IsScopedPerDatabase(string provider)
        {
            using var a = Create(provider, FakeConnectionString(provider, Server(provider)));
            using var b = Create(provider, FakeConnectionString(provider, Server(provider)));
            var procedureNamesA = CachesOf(a).Procedures;
            var procedureNamesB = CachesOf(b).Procedures;
            procedureNamesA.TryAdd(typeof(PscProcedureRow), "zz_psc_proc_a");
            procedureNamesB.TryAdd(typeof(PscProcedureRow), "zz_psc_proc_b");
            try
            {
                Assert.AreEqual("zz_psc_proc_a", ResolveProcedureName<PscProcedureRow>(a), $"{provider}: scope A");
                Assert.AreEqual("zz_psc_proc_b", ResolveProcedureName<PscProcedureRow>(b), $"{provider}: scope B");
            }
            finally
            {
                procedureNamesA.TryRemove(typeof(PscProcedureRow), out _);
                procedureNamesB.TryRemove(typeof(PscProcedureRow), out _);
            }
        }

        private static string ResolveProcedureName<T>(OrmDataProvider provider)
        {
            switch (provider)
            {
                case SqlServerOrmDataProvider p: return p.ResolveProcedureNameInternal<T>(null);
                case MySqlOrmDataProvider p: return p.ResolveProcedureNameInternal<T>(null);
                default: throw new ArgumentOutOfRangeException(nameof(provider));
            }
        }

        #endregion
    }
}
