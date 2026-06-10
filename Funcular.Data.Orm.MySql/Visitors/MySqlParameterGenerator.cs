using System;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql.Visitors
{
    /// <summary>
    /// Manages the creation of unique SQL parameters and their naming for parameterized MySQL queries.
    /// </summary>
    public class MySqlParameterGenerator
    {
        private int _parameterCounter;

        public MySqlParameterGenerator(int initialCounter = 0)
        {
            _parameterCounter = initialCounter;
        }

        public MySqlParameter CreateParameter(object value)
        {
            var parameterName = $"@p__linq__{_parameterCounter++}";
            return BuildParameter(parameterName, value ?? DBNull.Value);
        }

        public MySqlParameter CreateParameterForInClause(object value, int index)
        {
            var parameterName = $"@p__linq__{_parameterCounter++}";
            return BuildParameter(parameterName, value);
        }

        private static MySqlParameter BuildParameter(string name, object value)
        {
            var param = new MySqlParameter(name, value ?? DBNull.Value);
            var mySqlType = GetMySqlDbType(value);
            if (mySqlType.HasValue)
                param.MySqlDbType = mySqlType.Value;
            return param;
        }

        /// <summary>
        /// Maps a CLR value to its MySqlDbType. Returns null for DBNull/null so the driver infers it.
        /// MySqlConnector infers types well from the value, so the explicit mapping is primarily a
        /// safety net for non-string reference values and unsigned integers.
        /// </summary>
        public static MySqlDbType? GetMySqlDbType(object value)
        {
            switch (value)
            {
                case null:
                    return null; // DBNull → let the driver send NULL with inferred/late-bound type
                case string _:
                    return MySqlDbType.VarChar;
                case bool _:
                    return MySqlDbType.Bool;
                case int _:
                    return MySqlDbType.Int32;
                case long _:
                    return MySqlDbType.Int64;
                case short _:
                case sbyte _:
                    return MySqlDbType.Int16;
                case byte _:
                case ushort _:
                    return MySqlDbType.UInt16;
                case uint _:
                    return MySqlDbType.UInt32;
                case ulong _:
                    return MySqlDbType.UInt64;
                case DateTime _:
                    return MySqlDbType.DateTime;
                case DateTimeOffset _:
                    return MySqlDbType.DateTime;
                case Guid _:
                    return MySqlDbType.Guid;
                case decimal _:
                    return MySqlDbType.Decimal;
                case double _:
                    return MySqlDbType.Double;
                case float _:
                    return MySqlDbType.Float;
                case Enum _:
                    return MySqlDbType.Int32;
                case byte[] _:
                    return MySqlDbType.Blob;
                default:
                    return null; // let the driver infer
            }
        }
    }
}
