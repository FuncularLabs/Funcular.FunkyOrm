using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Funcular.Data.Orm.Tests.DeleteGuard;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    /// <summary>The delete-guard harness on PostgreSQL (docs/plans/DELETE_GUARD_PLAN.md, §4.1).</summary>
    [TestClass]
    public class PostgreSqlDeleteGuardTests : DeleteGuardHarness
    {
        private static string ConnectionString => PostgreSqlTestConnection.Resolve();

        protected override void RequireDatabase()
        {
            try
            {
                using var connection = new NpgsqlConnection(ConnectionString);
                connection.Open();
            }
            catch (Exception ex)
            {
                Assert.Inconclusive($"PostgreSQL test database is unreachable ({ex.GetType().Name}).");
            }
        }

        private static void Execute(string sql)
        {
            using var connection = new NpgsqlConnection(ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        protected override void CreateTables() => Execute(
            $"DROP TABLE IF EXISTS {RowTable}; DROP TABLE IF EXISTS {TrueUpTable}; " +
            $"CREATE TABLE {RowTable} (id INT PRIMARY KEY, first_name VARCHAR(20) NULL, true_up INT NOT NULL, big INT NULL, archived BOOLEAN NOT NULL DEFAULT FALSE); " +
            $"INSERT INTO {RowTable} (id, first_name, true_up) VALUES (1, 'a', 1), (2, 'b', 0), (3, NULL, 0); " +
            $"CREATE TABLE {TrueUpTable} (id INT PRIMARY KEY, amount INT NOT NULL); " +
            $"INSERT INTO {TrueUpTable} (id, amount) VALUES (1, 5), (2, 7);");

        protected override void DropTables() => Execute($"DROP TABLE IF EXISTS {RowTable}; DROP TABLE IF EXISTS {TrueUpTable};");

        protected override void DropMissingTable() => Execute($"DROP TABLE IF EXISTS {MissingTable};");

        protected override OrmDataProvider CreateProvider() => new PostgreSqlOrmDataProvider(ConnectionString);

        protected override bool IsDiscovered(OrmDataProvider provider, Type type) =>
            ((PostgreSqlOrmDataProvider)provider).ScopeMappedTypes.Contains(type);

        /// <summary>
        /// Review R-5 (A/N-1 = B/1), fixed as D-156's option (1) (D-G04): a delete of an undiscovered type whose table is
        /// missing fails with discovery's error, an <see cref="InvalidOperationException"/> carrying the
        /// <see cref="PostgresException"/> 42P01, exactly as a query of the same predicate does; that error is what tells
        /// the caller why the transaction is now aborted. The discovery SELECT ran on the caller's transaction, so the
        /// next statement fails with 25P02 after either path. Up to 7ef1b36 the delete reported the guard's refusal with
        /// no inner exception, and the next statement failed with 25P02 for no stated reason. Option (1) doesn't keep the
        /// transaction usable (D-G04 asked for that test; it was red before and after the fix, D-157): a server error
        /// inside a PostgreSQL transaction aborts it whichever path ran it; only a savepoint (option 2) or no discovery
        /// on the held path (option 3), both declined, would.
        /// </summary>
        [DataTestMethod]
        [DataRow("delete")] [DataRow("deleteAsync")] [DataRow("query")]
        public async Task FailedDeleteOfAMissingType_AbortsTheTransactionAsAQueryDoes_AndSaysWhy(string path)
        {
            RequireDatabase();
            try
            {
                CreateTables();
                DropMissingTable();
                using var provider = CreateProvider();
                var transactional = (ISqlOrmProvider)provider;
                Expression<Func<DgMissing, bool>> predicate = x => x.Id == x.Id;
                transactional.BeginTransaction();
                try
                {
                    Exception failure = null;
                    try
                    {
                        if (path == "delete") provider.Delete(predicate);
                        else if (path == "deleteAsync") await provider.DeleteAsync(predicate);
                        else provider.Query(predicate);
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                    Assert.IsNotNull(failure, $"{path}: ran against a missing table");
                    Assert.AreEqual(typeof(InvalidOperationException), failure.GetType(), $"{path}: {failure.GetType().Name}: {failure.Message}");
                    var inner = failure.InnerException as PostgresException;
                    Assert.IsNotNull(inner, $"{path}: discovery's error must carry the PostgresException; the message was \"{failure.Message}\" with inner {failure.InnerException?.GetType().Name ?? "null"}");
                    Assert.AreEqual("42P01", inner.SqlState, $"{path}: expected undefined_table; got {inner.SqlState}: {inner.Message}");

                    Exception next = null;
                    try { provider.GetList<DgRow>(); } catch (Exception ex) { next = ex; }
                    var aborted = next as PostgresException;
                    Assert.IsNotNull(aborted, $"{path}: the next statement must fail with the aborted-transaction error; got {next?.GetType().Name ?? "no exception"}: {next?.Message}");
                    Assert.AreEqual("25P02", aborted.SqlState, $"{path}: {aborted.SqlState}: {aborted.Message}");
                }
                finally
                {
                    transactional.RollbackTransaction();
                }
                Assert.AreEqual(3, provider.GetList<DgRow>().Count, $"{path}: after the rollback the connection works again");
            }
            finally
            {
                DropTables();
            }
        }
    }
}
