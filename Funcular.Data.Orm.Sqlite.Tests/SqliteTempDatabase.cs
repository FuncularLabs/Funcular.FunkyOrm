using Microsoft.Data.Sqlite;

namespace Funcular.Data.Orm.Sqlite.Tests
{
    /// <summary>
    /// Shared temp-file database for SQLite tests that need the remote-join schema (plan §4.1): <c>country</c>,
    /// <c>organization</c>, <c>address</c>, <c>person</c> and <c>person_address</c>, with the same DDL as
    /// <see cref="SqliteRemoteFeaturesTests"/>, plus the <c>project</c> tables behind the computed members.
    /// </summary>
    public static class SqliteTempDatabase
    {
        public const string JoinTablesDdl = @"
CREATE TABLE IF NOT EXISTS country (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS organization (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, headquarters_address_id INTEGER);
CREATE TABLE IF NOT EXISTS address (id INTEGER PRIMARY KEY AUTOINCREMENT, line_1 TEXT, line_2 TEXT, city TEXT, state_code TEXT, postal_code TEXT, dateutc_created TEXT NOT NULL DEFAULT (datetime('now')), dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')), country_id INTEGER);
CREATE TABLE IF NOT EXISTS person (id INTEGER PRIMARY KEY AUTOINCREMENT, first_name TEXT, middle_initial TEXT, last_name TEXT, birthdate TEXT, gender TEXT, dateutc_created TEXT NOT NULL DEFAULT (datetime('now')), dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')), unique_id TEXT, employer_id INTEGER);
CREATE TABLE IF NOT EXISTS person_address (id INTEGER PRIMARY KEY AUTOINCREMENT, person_id INTEGER NOT NULL, address_id INTEGER NOT NULL, is_primary INTEGER NOT NULL DEFAULT 0, address_type_value INTEGER DEFAULT 0, dateutc_created TEXT NOT NULL DEFAULT (datetime('now')), dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')), FOREIGN KEY (person_id) REFERENCES person(id), FOREIGN KEY (address_id) REFERENCES address(id));";

        public const string ProjectTablesDdl = @"
CREATE TABLE IF NOT EXISTS project (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, organization_id INTEGER NOT NULL, lead_id INTEGER, category_id INTEGER, budget REAL, score INTEGER, metadata TEXT, dateutc_created TEXT NOT NULL DEFAULT (datetime('now')), dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')));
CREATE TABLE IF NOT EXISTS project_milestone (id INTEGER PRIMARY KEY AUTOINCREMENT, project_id INTEGER NOT NULL, title TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'pending', due_date TEXT, completed_date TEXT);
CREATE TABLE IF NOT EXISTS project_note (id INTEGER PRIMARY KEY AUTOINCREMENT, project_id INTEGER NOT NULL, author_id INTEGER, content TEXT NOT NULL, category TEXT NOT NULL DEFAULT 'general', dateutc_created TEXT NOT NULL DEFAULT (datetime('now')));";

        /// <summary>Creates a fresh temp database file with the schema above; returns its path.</summary>
        public static string Create(string prefix)
        {
            var path = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}.db");
            using var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = JoinTablesDdl + ProjectTablesDdl;
            command.ExecuteNonQuery();
            return path;
        }

        /// <summary>Deletes a database made by <see cref="Create"/>, releasing pooled handles first (best effort).</summary>
        public static void Delete(string path)
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
                // A handle is still open: leave the file. The transactional cleanup has already removed the seeded rows.
            }
        }
    }
}
