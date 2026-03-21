using System;
using System.Data;
using Npgsql;
using NpgsqlTypes;

namespace Funcular.Data.Orm.PostgreSql.Visitors
{
    /// <summary>
    /// Manages the creation of unique SQL parameters and their naming for parameterized PostgreSQL queries.
    /// </summary>
    public class PostgreSqlParameterGenerator
    {
        private int _parameterCounter;

        public PostgreSqlParameterGenerator(int initialCounter = 0)
        {
            _parameterCounter = initialCounter;
        }

        public NpgsqlParameter CreateParameter(object value)
        {
            var parameterName = $"@p__linq__{_parameterCounter++}";
            var npgsqlType = GetNpgsqlDbType(value);
            return new NpgsqlParameter(parameterName, value ?? DBNull.Value)
            {
                NpgsqlDbType = npgsqlType
            };
        }

        public NpgsqlParameter CreateParameterForInClause(object value, int index)
        {
            var parameterName = $"@p__linq__{_parameterCounter++}";
            var npgsqlType = GetNpgsqlDbType(value);
            return new NpgsqlParameter(parameterName, value)
            {
                NpgsqlDbType = npgsqlType
            };
        }

        public static NpgsqlDbType GetNpgsqlDbType(object value)
        {
            if (value == null || value is string)
                return NpgsqlDbType.Text;
            if (value is int)
                return NpgsqlDbType.Integer;
            if (value is long)
                return NpgsqlDbType.Bigint;
            if (value is bool)
                return NpgsqlDbType.Boolean;
            if (value is DateTime)
                return NpgsqlDbType.Timestamp;
            if (value is DateTimeOffset)
                return NpgsqlDbType.TimestampTz;
            if (value is Guid)
                return NpgsqlDbType.Uuid;
            if (value is decimal)
                return NpgsqlDbType.Numeric;
            if (value is double)
                return NpgsqlDbType.Double;
            if (value is float)
                return NpgsqlDbType.Real;
            if (value is Enum)
                return NpgsqlDbType.Integer;
            if (value is short || value is ushort || value is byte || value is sbyte)
                return NpgsqlDbType.Smallint;
            if (value is uint || value is ulong)
                return NpgsqlDbType.Bigint;
            if (value is byte[])
                return NpgsqlDbType.Bytea;
            throw new NotSupportedException($"Type {value.GetType()} is not supported for PostgreSQL parameters.");
        }
    }
}
