using System;
using System.Data;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.Visitors
{
    /// <summary>
    /// Manages the creation of unique SQL parameters and their naming for parameterized queries.
    /// </summary>
    public class ParameterGenerator
    {
        private int _parameterCounter;
            
        /// <summary>
        /// Initializes a new instance of the <see cref="ParameterGenerator"/> class.
        /// </summary>
        /// <param name="initialCounter">The initial value for the parameter counter.</param>
        public ParameterGenerator(int initialCounter = 0)
        {
            _parameterCounter = initialCounter;
        }

        /// <summary>
        /// Creates a new SQL parameter with a unique name and the appropriate SQL data type.
        /// </summary>
        /// <param name="value">The value to parameterize.</param>
        /// <returns>A new <see cref="SqlParameter"/> with a unique name.</returns>
        public SqlParameter CreateParameter(object value)
        {
            var parameterName = $"@p__linq__{_parameterCounter++}";
            var sqlType = GetSqlDbType(value);
            return new SqlParameter(parameterName, value ?? DBNull.Value)
            {
                SqlDbType = sqlType
            };
        }

        // ReSharper disable once GrammarMistakeInComment
        /// <summary>
        /// Creates a new SQL parameter with a unique name for use in IN clauses.
        /// </summary>
        /// <param name="value">The value to parameterize.</param>
        /// <param name="index">The index to use in the parameter name for IN clauses.</param>
        /// <returns>A new <see cref="SqlParameter"/> with a unique name.</returns>
        public SqlParameter CreateParameterForInClause(object value, int index)
        {
            var parameterName = $"@p__linq__{_parameterCounter++}";
            var sqlType = GetSqlDbType(value);
            return new SqlParameter(parameterName, value)
            {
                SqlDbType = sqlType
            };
        }


        /// <summary>
        /// Determines the SQL data type for a given value.
        /// </summary>
        /// <param name="value">The value to determine the SQL data type for.</param>
        /// <returns>The corresponding <see cref="SqlDbType"/>.</returns>
        public static SqlDbType GetSqlDbType(object value)
        {
            if (value == null || value is string)
                return SqlDbType.NVarChar;
            if (value is int)
                return SqlDbType.Int;
            if (value is long)
                return SqlDbType.BigInt;
            if (value is bool)
                return SqlDbType.Bit;
            if (value is DateTime)
                return SqlDbType.DateTime2;
            if (value is Guid)
                return SqlDbType.UniqueIdentifier;
            if (value is decimal)
                return SqlDbType.Decimal;
            if (value is double)
                return SqlDbType.Float;
            if (value is float)
                return SqlDbType.Real;
            if (value is Enum) 
                return SqlDbType.Int; // or map underlying type
            if (value is short || value is ushort || value is byte || value is sbyte) 
                return SqlDbType.SmallInt;
            if (value is uint || value is ulong) 
                return SqlDbType.BigInt;
            throw new NotSupportedException($"Type {value.GetType()} is not supported for SQL parameters.");
        }
    }
}