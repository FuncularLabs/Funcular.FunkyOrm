using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Funcular.Data.Orm.Tests.Parity;
using Microsoft.Data.Sqlite;

namespace Funcular.Data.Orm.Sqlite.Tests.Parity
{
    /// <summary>The WHERE parity rows (D-001) on SQLite, a temp file per test.</summary>
    [TestClass]
    public class SqliteWhereParityTests : WhereParityHarness
    {
        private string _path;

        protected override void CreateTable()
        {
            DropTable();
            _path = Path.Combine(Path.GetTempPath(), "zz_wp_" + Guid.NewGuid().ToString("N") + ".db");
            using (var connection = new SqliteConnection("Data Source=" + _path))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        $"CREATE TABLE {Table} (id INTEGER PRIMARY KEY, name TEXT NULL, code INTEGER NOT NULL, posted_at TEXT NOT NULL, nullable_id INTEGER NULL); " +
                        $"INSERT INTO {Table} (id, name, code, posted_at, nullable_id) VALUES " +
                        string.Join(", ", Rows.Select(r =>
                            $"({r.Id}, '{r.Name}', {r.Code}, '{r.PostedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}', " +
                            (r.NullableId.HasValue ? r.NullableId.Value.ToString(CultureInfo.InvariantCulture) : "NULL") + ")")) + ";";
                    command.ExecuteNonQuery();
                }
            }
        }

        protected override void DropTable()
        {
            if (_path == null)
                return;
            SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(_path))
                    File.Delete(_path);
            }
            catch (IOException)
            {
                // A pooled handle still holds it; the file is a uniquely named temp file.
            }
            _path = null;
        }

        protected override OrmDataProvider CreateProvider() => new SqliteOrmDataProvider("Data Source=" + _path);

        protected override string DatePartSql(string part, string column) =>
            "CAST(strftime('%" + (part == "Year" ? "Y" : part == "Month" ? "m" : "d") + "', " + column + ") AS INTEGER)";

        protected override string LengthSql(string column) => "LENGTH(" + column + ")";

        protected override string CastToTextSql(string column) => "CAST(" + column + " AS TEXT)";
    }
}
