using System;
using System.Data;
using Microsoft.Data.Sqlite;

namespace Funcular.Data.Orm.Sqlite.Visitors
{
    /// <summary>
    /// Manages the creation of unique SQL parameters and their naming for parameterized SQLite queries.
    /// </summary>
    public class SqliteParameterGenerator
    {
        private int _parameterCounter;

        public SqliteParameterGenerator(int initialCounter = 0)
        {
            _parameterCounter = initialCounter;
        }

        public SqliteParameter CreateParameter(object value)
        {
            var parameterName = $"@p__linq__{_parameterCounter++}";
            return new SqliteParameter(parameterName, value ?? DBNull.Value);
        }

        public SqliteParameter CreateParameterForInClause(object value, int index)
        {
            var parameterName = $"@p__linq__{_parameterCounter++}";
            return new SqliteParameter(parameterName, value);
        }

        public static DbType GetDbType(object value)
        {
            if (value == null || value is string)
                return DbType.String;
            if (value is int)
                return DbType.Int32;
            if (value is long)
                return DbType.Int64;
            if (value is bool)
                return DbType.Int32; // SQLite stores booleans as integers
            if (value is DateTime)
                return DbType.String; // SQLite stores dates as TEXT
            if (value is DateTimeOffset)
                return DbType.String;
            if (value is Guid)
                return DbType.String;
            if (value is decimal)
                return DbType.Decimal;
            if (value is double)
                return DbType.Double;
            if (value is float)
                return DbType.Single;
            if (value is Enum)
                return DbType.Int32;
            if (value is short || value is ushort || value is byte || value is sbyte)
                return DbType.Int16;
            if (value is uint || value is ulong)
                return DbType.Int64;
            if (value is byte[])
                return DbType.Binary;
            return DbType.String;
        }
    }
}
