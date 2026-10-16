using System;
using System.Data.Common;
using System.Globalization;
using System.IO;
using Funcular.Data.Orm.Tests.Aggregates;
using Microsoft.Data.Sqlite;

namespace Funcular.Data.Orm.Sqlite.Tests.Aggregates
{
    /// <summary>The aggregate rows (FD-04) on SQLite, a temp file per test. SQLite has no offset-aware or decimal type.</summary>
    [TestClass]
    public class SqliteAggregateTests : AggregateHarness
    {
        private string _path;

        protected override void PrepareDatabase()
        {
            DropTable();
            _path = Path.Combine(Path.GetTempPath(), "zz_ag_" + Guid.NewGuid().ToString("N") + ".db");
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

        protected override DbConnection OpenConnection()
        {
            var connection = new SqliteConnection("Data Source=" + _path + ";Pooling=False");
            connection.Open();
            return connection;
        }

        protected override string Ddl =>
            $"CREATE TABLE {Table} (id INTEGER PRIMARY KEY, i32 INTEGER NOT NULL, i32n INTEGER NULL, i64 INTEGER NOT NULL, i64n INTEGER NULL, " +
            "f32 REAL NOT NULL, f32n REAL NULL, f64 REAL NOT NULL, f64n REAL NULL, amount NUMERIC NOT NULL, amountn NUMERIC NULL, " +
            "posted TEXT NOT NULL, postedn TEXT NULL, name TEXT NULL, uid TEXT NOT NULL, stamp TEXT NOT NULL, span TEXT NOT NULL, status INTEGER NOT NULL)";

        protected override object ToDb(string column, object value)
        {
            switch (column)
            {
                case "posted": return ((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                case "uid": return ((Guid)value).ToString("D");
                case "stamp": return ((DateTimeOffset)value).ToString("o", CultureInfo.InvariantCulture);
                case "span": return ((TimeSpan)value).ToString("c", CultureInfo.InvariantCulture);
                default: return value;
            }
        }

        protected override OrmDataProvider CreateProvider() => new SqliteOrmDataProvider("Data Source=" + _path);

        protected override bool DateTimeOffsetOrdersByInstant => false;

        // Lower-case Guid text orders as .NET does, but the owner's default is Rejected (storage-format dependent).
        protected override bool GuidDatabaseOrderMatchesLinq => true;
    }
}
