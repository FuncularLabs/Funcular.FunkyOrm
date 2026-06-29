using System;
using System.Threading.Tasks;
using Funcular.Data.Orm.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.Sqlite.Tests
{
    /// <summary>
    /// SQLite has no stored procedures, so every Exec* entry point must throw NotSupportedException
    /// (inherited from the OrmDataProvider virtual defaults). No database is required — the guard
    /// throws before any connection is opened.
    /// </summary>
    [TestClass]
    public class SqliteStoredProcedureTests
    {
        private SqliteOrmDataProvider _provider;

        [TestInitialize]
        public void Setup() => _provider = new SqliteOrmDataProvider("Data Source=:memory:");

        [TestCleanup]
        public void Cleanup() => _provider?.Dispose();

        private class Dummy { public int Id { get; set; } }

        [TestMethod]
        public void ExecProcedure_Throws_NotSupported()
            => Assert.ThrowsException<NotSupportedException>(() => _provider.ExecProcedure<Dummy>("any"));

        [TestMethod]
        public void ExecScalar_Throws_NotSupported()
            => Assert.ThrowsException<NotSupportedException>(() => _provider.ExecScalar<int>("any"));

        [TestMethod]
        public void ExecNonQuery_Throws_NotSupported()
            => Assert.ThrowsException<NotSupportedException>(() => _provider.ExecNonQuery("any"));

        [TestMethod]
        public async Task ExecProcedureAsync_Throws_NotSupported()
            => await Assert.ThrowsExceptionAsync<NotSupportedException>(() => _provider.ExecProcedureAsync<Dummy>("any"));

        [TestMethod]
        public async Task ExecScalarAsync_Throws_NotSupported()
            => await Assert.ThrowsExceptionAsync<NotSupportedException>(() => _provider.ExecScalarAsync<int>("any"));

        [TestMethod]
        public async Task ExecNonQueryAsync_Throws_NotSupported()
            => await Assert.ThrowsExceptionAsync<NotSupportedException>(() => _provider.ExecNonQueryAsync("any"));
    }
}
