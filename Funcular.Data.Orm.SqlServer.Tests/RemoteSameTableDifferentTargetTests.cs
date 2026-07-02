using System;
using System.Linq;
using System.ComponentModel.DataAnnotations.Schema;
using Funcular.Data.Orm.Attributes;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    /// <summary>
    /// Guards that two DTOs mapped to the SAME physical [Table] but declaring [RemoteProperty] to DIFFERENT
    /// target types both resolve correctly (a scenario a bug report suspected of colliding in a "table-keyed"
    /// resolver cache). The remote-path caches are keyed by (sourceType, remoteType, keyPath) — not by table —
    /// so distinct DTOs don't collide; this test locks that in.
    /// </summary>
    [TestClass]
    public class RemoteSameTableDifferentTargetTests
    {
        private string _cs;
        private SqlServerOrmDataProvider _p;

        [Table("funky_st_a")] public class StA { public int Id { get; set; } public string AName { get; set; } }
        [Table("funky_st_b")] public class StB { public int Id { get; set; } public string BName { get; set; } }

        [Table("funky_st_child")]
        public sealed class StDto1
        {
            public int Id { get; set; }
            public int? StAId { get; set; }
            [RemoteProperty(typeof(StA), nameof(StAId), nameof(StA.AName))] public string RemoteA { get; set; }
        }
        [Table("funky_st_child")]
        public sealed class StDto2
        {
            public int Id { get; set; }
            public int? StBId { get; set; }
            [RemoteProperty(typeof(StB), nameof(StBId), nameof(StB.BName))] public string RemoteB { get; set; }
        }

        [TestInitialize]
        public void Setup()
        {
            _cs = Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
                  "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=funky_db;Integrated Security=True;";
            using (var c = new SqlConnection(_cs))
            {
                c.Open();
                var d = c.CreateCommand();
                d.CommandText = @"
                    IF OBJECT_ID('funky_st_child','U') IS NOT NULL DROP TABLE funky_st_child;
                    IF OBJECT_ID('funky_st_a','U') IS NOT NULL DROP TABLE funky_st_a;
                    IF OBJECT_ID('funky_st_b','U') IS NOT NULL DROP TABLE funky_st_b;";
                d.ExecuteNonQuery();
                var cr = c.CreateCommand();
                cr.CommandText = @"
                    CREATE TABLE funky_st_a (id INT IDENTITY PRIMARY KEY, a_name NVARCHAR(50) NULL);
                    CREATE TABLE funky_st_b (id INT IDENTITY PRIMARY KEY, b_name NVARCHAR(50) NULL);
                    CREATE TABLE funky_st_child (id INT IDENTITY PRIMARY KEY, st_a_id INT NULL, st_b_id INT NULL);
                    INSERT INTO funky_st_a (a_name) VALUES ('A1');
                    INSERT INTO funky_st_b (b_name) VALUES ('B1');
                    INSERT INTO funky_st_child (st_a_id, st_b_id) VALUES (1, 1);";
                cr.ExecuteNonQuery();
            }
            _p = new SqlServerOrmDataProvider(_cs);
        }

        [TestCleanup] public void Cleanup() => _p?.Dispose();

        [TestMethod]
        public void TwoDtosSameTableDifferentTargets_BothResolve()
        {
            var r1 = _p.Query<StDto1>().ToList();   // DTO1 first
            var r2 = _p.Query<StDto2>().ToList();   // DTO2 second — same table, different target type
            Assert.AreEqual(1, r1.Count); Assert.AreEqual("A1", r1[0].RemoteA);
            Assert.AreEqual(1, r2.Count); Assert.AreEqual("B1", r2[0].RemoteB);
        }
    }
}
