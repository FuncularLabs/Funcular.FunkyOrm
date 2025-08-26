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
        public SqlParameter CreateParameter(object? value)
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
            var parameterName = $"@p{index}";
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
        public static SqlDbType GetSqlDbType(object? value) => value switch
        {
            null => SqlDbType.NVarChar,
            string => SqlDbType.NVarChar,
            int => SqlDbType.Int,
            long => SqlDbType.BigInt,
            bool => SqlDbType.Bit,
            DateTime => SqlDbType.DateTime2,
            Guid => SqlDbType.UniqueIdentifier,
            decimal => SqlDbType.Decimal,
            double => SqlDbType.Float,
            float => SqlDbType.Real,
            _ => throw new NotSupportedException($"Type {value.GetType()} is not supported for SQL parameters.")
        };
    }
}