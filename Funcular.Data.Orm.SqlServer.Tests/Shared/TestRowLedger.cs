// Linked into the PostgreSQL, MySQL and net48 test projects: keep it C# 7.3, with explicit usings.
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.Tests.Shared
{
    /// <summary>
    /// The rows one integration test inserts into the shared database. <see cref="Begin"/> reads the row count of each
    /// guarded table; <see cref="Track"/> records a row the test inserted; <see cref="Finish"/> deletes the tracked rows,
    /// newest first, and fails the test if a delete failed or a guarded table's count is not what <see cref="Begin"/>
    /// read. A test that inserts into a guarded table tracks every row it inserts, committed or not.
    /// </summary>
    /// <remarks>
    /// Rows are deleted by primary key only, each on its own connection, never by name, marker or range: a row this
    /// ledger didn't track is left alone (and fails the check). Another process writing to these tables while a test
    /// runs changes the counts too, so database work against the shared databases runs serially.
    /// </remarks>
    public sealed class TestRowLedger
    {
        /// <summary>The guarded tables of the SQL Server schema (<c>Database/integration_test_db.sql</c>).</summary>
        public static readonly string[] SqlServerTables =
            { "person", "address", "person_address", "organization", "country", "project", "project_milestone", "project_note" };

        /// <summary>The guarded tables of the PostgreSQL schema, which has no project tables.</summary>
        public static readonly string[] PostgreSqlTables =
            { "person", "address", "person_address", "organization", "country" };

        /// <summary>The guarded tables of the MySQL schema.</summary>
        public static readonly string[] MySqlTables =
            { "person", "address", "person_address", "organization", "country", "project", "project_milestone", "project_note" };

        private readonly Func<DbConnection> _newConnection;
        private readonly string[] _tables;
        private readonly long[] _counts;
        private readonly List<KeyValuePair<string, object>> _rows = new List<KeyValuePair<string, object>>();

        private TestRowLedger(Func<DbConnection> newConnection, string[] tables)
        {
            _newConnection = newConnection;
            _tables = tables;
            _counts = ReadCounts(newConnection, tables);
        }

        /// <summary>Reads the row count of each of <paramref name="tables"/>, in one statement.</summary>
        /// <param name="newConnection">Returns a new, unopened connection to the test database.</param>
        /// <param name="tables">The guarded tables: one of the provider lists above.</param>
        public static TestRowLedger Begin(Func<DbConnection> newConnection, IReadOnlyList<string> tables)
        {
            return new TestRowLedger(newConnection, tables.ToArray());
        }

        /// <summary>
        /// Records a row the test inserted: the table named by the entity's <see cref="TableAttribute"/> (inherited
        /// included) and its <c>Id</c>. Throws <see cref="ArgumentException"/> if that table isn't guarded or the
        /// entity has no <c>Id</c>.
        /// </summary>
        public void Track(object entity)
        {
            var type = entity.GetType();
            var mapped = type.GetCustomAttribute<TableAttribute>(true);
            var table = mapped == null
                ? null
                : _tables.FirstOrDefault(t => string.Equals(t, mapped.Name, StringComparison.OrdinalIgnoreCase));
            if (table == null)
                throw new ArgumentException(
                    type.Name + " maps to " + (mapped == null ? "no table" : mapped.Name) +
                    ", which isn't one of the guarded tables (" + string.Join(", ", _tables) + ").", "entity");
            var id = type.GetProperty("Id");
            if (id == null)
                throw new ArgumentException(type.Name + " has no Id property.", "entity");
            _rows.Add(new KeyValuePair<string, object>(table, id.GetValue(entity)));
        }

        /// <summary>
        /// Deletes the tracked rows, newest first, attempting every one; then fails the test if a delete failed or a
        /// guarded table's count changed. A tracked row that is already gone is not a failure.
        /// </summary>
        public void Finish()
        {
            var failedDeletes = new List<string>();
            for (var i = _rows.Count - 1; i >= 0; i--)
            {
                try
                {
                    DeleteRow(_newConnection, _rows[i].Key, _rows[i].Value);
                }
                catch (DbException ex)
                {
                    failedDeletes.Add("  " + _rows[i].Key + " id " + _rows[i].Value + ": " + ex.Message);
                }
            }

            var after = ReadCounts(_newConnection, _tables);
            var changedCounts = new List<string>();
            for (var i = 0; i < _tables.Length; i++)
            {
                if (after[i] != _counts[i])
                    changedCounts.Add("  " + _tables[i] + ": " + _counts[i] + " -> " + after[i]);
            }

            if (failedDeletes.Count == 0 && changedCounts.Count == 0)
                return;

            var message = new StringBuilder();
            if (changedCounts.Count > 0)
            {
                message.AppendLine("The test left these shared tables with different row counts (before -> after). " +
                                   "Track every row it inserts. (Another process writing to these tables during " +
                                   "the test changes them too.)");
                foreach (var line in changedCounts) message.AppendLine(line);
            }
            if (failedDeletes.Count > 0)
            {
                message.AppendLine("These tracked rows could not be deleted:");
                foreach (var line in failedDeletes) message.AppendLine(line);
            }
            Assert.Fail(message.ToString());
        }

        /// <summary>
        /// Deletes one row by primary key, on a new connection: for a test that deliberately leaves a row untracked
        /// and removes it itself.
        /// </summary>
        public static void DeleteRow(Func<DbConnection> newConnection, string table, object id)
        {
            using (var connection = newConnection())
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "DELETE FROM " + table + " WHERE id = @id";
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@id";
                    parameter.Value = id;
                    command.Parameters.Add(parameter);
                    command.ExecuteNonQuery();
                }
            }
        }

        private static long[] ReadCounts(Func<DbConnection> newConnection, string[] tables)
        {
            using (var connection = newConnection())
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT " +
                                          string.Join(", ", tables.Select(t => "(SELECT COUNT(*) FROM " + t + ")"));
                    using (var reader = command.ExecuteReader())
                    {
                        reader.Read();
                        var counts = new long[tables.Length];
                        for (var i = 0; i < counts.Length; i++)
                            counts[i] = Convert.ToInt64(reader.GetValue(i));
                        return counts;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Proves a test class's own hooks run the <see cref="TestRowLedger"/> check: called from a test, after it
    /// inserted a row it deliberately didn't track.
    /// </summary>
    public static class TestRowLedgerProbe
    {
        /// <summary>
        /// Calls <paramref name="cleanup"/> and expects it to fail naming <paramref name="table"/>; then deletes the
        /// untracked row and calls <paramref name="setup"/>, so the framework's own cleanup finds a fresh ledger.
        /// </summary>
        public static void AssertCleanupFails(Action cleanup, Action setup, Func<DbConnection> newConnection,
            string table, object id)
        {
            string message = null;
            try
            {
                try
                {
                    cleanup();
                }
                catch (AssertFailedException ex)
                {
                    message = ex.Message;
                }
            }
            finally
            {
                TestRowLedger.DeleteRow(newConnection, table, id);
                setup();
            }
            Assert.IsNotNull(message, "Cleanup passed although the test left a row in " + table + ".");
            StringAssert.Contains(message, "  " + table + ": ");
        }

        /// <summary>Calls <paramref name="cleanup"/> and expects it to pass; then calls <paramref name="setup"/>.</summary>
        public static void AssertCleanupPasses(Action cleanup, Action setup)
        {
            try
            {
                cleanup();
            }
            finally
            {
                setup();
            }
        }
    }
}
