using System;
using System.Linq;
using System.Text;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    /// <summary>
    /// §2 (3.8.5): the explicit `[RemoteProperty(typeof(Target), ...)]` target is authoritative on the final
    /// hop. A single-hop remote whose FK property name does NOT convention-match the target type (here `LookupId`
    /// → would infer "Lookup", not "B2Target") used to throw `PathNotFoundException` because `ResolveExplicit`
    /// resolved every hop by name-inference and only validated the explicit type at the end. Now the last hop
    /// honors the explicit `remoteType`, so the FK can be named anything.
    /// </summary>
    [TestClass]
    public class RemoteExplicitTargetAuthorityTests
    {
        private string _connectionString;
        private SqlServerOrmDataProvider _provider;
        private readonly StringBuilder _sb = new();

        [Table("funky_b2_target")]
        public class B2Target
        {
            public int Id { get; set; }
            public string TargetName { get; set; } // column: target_name
        }

        [Table("funky_b2_child")]
        public sealed class B2Child
        {
            public int Id { get; set; }
            public int? LookupId { get; set; } // FK → funky_b2_target.id; name "Lookup" does NOT match "B2Target"

            [RemoteProperty(typeof(B2Target), nameof(LookupId), nameof(B2Target.TargetName))]
            public string RemoteName { get; set; }
        }

        [TestInitialize]
        public void Setup()
        {
            _sb.Clear();
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
                                "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=funky_db;Integrated Security=True;";
            EnsureSchema();
            _provider = new SqlServerOrmDataProvider(_connectionString) { Log = s => _sb.AppendLine(s) };
        }

        [TestCleanup]
        public void Cleanup() => _provider?.Dispose();

        private void EnsureSchema()
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                var drop = conn.CreateCommand();
                drop.CommandText = @"
                    IF OBJECT_ID('funky_b2_child','U') IS NOT NULL DROP TABLE funky_b2_child;
                    IF OBJECT_ID('funky_b2_target','U') IS NOT NULL DROP TABLE funky_b2_target;";
                drop.ExecuteNonQuery();
                var create = conn.CreateCommand();
                create.CommandText = @"
                    CREATE TABLE funky_b2_target (id INT IDENTITY(1,1) PRIMARY KEY, target_name NVARCHAR(50) NULL);
                    CREATE TABLE funky_b2_child (id INT IDENTITY(1,1) PRIMARY KEY, lookup_id INT NULL);
                    INSERT INTO funky_b2_target (target_name) VALUES ('alpha'), ('bravo');
                    INSERT INTO funky_b2_child (lookup_id) VALUES (1), (2), (1);";
                create.ExecuteNonQuery();
            }
        }

        [TestMethod]
        public void ExplicitTarget_NonMatchingFkName_ResolvesViaExplicitType()
        {
            // Pre-3.8.5: threw PathNotFoundException "Could not determine target type for FK LookupId".
            var rows = _provider.Query<B2Child>()
                .Where(c => c.RemoteName != null)
                .OrderByDescending(c => c.RemoteName)
                .ToList();

            var sql = _sb.ToString();
            StringAssert.Contains(sql, "target_name", "remote value column must resolve via the explicit target");
            StringAssert.Contains(sql, "lookup_id", "join must use the FK column regardless of its name");
            Assert.AreEqual(3, rows.Count);
            Assert.IsTrue(rows.All(r => r.RemoteName == "alpha" || r.RemoteName == "bravo"));
        }
    }
}
