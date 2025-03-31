using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer
{
    /// <summary>
    /// Represents a set of elements used to construct a SQL WHERE clause from a LINQ expression.
    /// This class encapsulates the original expression, the translated SQL where clause, 
    /// the associated SQL parameters, and optionally, a SELECT clause.
    /// </summary>
    /// <typeparam name="T">The type of entity for which the WHERE clause is being constructed.</typeparam>
    public class SqlCommandElements<T>
    {
        protected readonly List<SqlParameter> _sqlParameters = new List<SqlParameter>();

        /// <summary>
        /// Gets or sets the original LINQ expression used to construct the SQL where clause.
        /// </summary>
        public Expression<Func<T, bool>> OriginalExpression { get; set; }

        /// <summary>
        /// Gets or sets the SQL WHERE clause string derived from the original expression.
        /// </summary>
        public string WhereClause { get; set; }

        /// <summary>
        /// Gets or sets the list of SQL parameters corresponding to the placeholders in the WhereClause.
        /// </summary>
        public List<SqlParameter> SqlParameters
        {
            get => _sqlParameters;
            set
            {
                _sqlParameters.Clear();
                _sqlParameters.AddRange(value);
            }
        }

        /// <summary>
        /// Gets or sets an optional SELECT clause string that might be used in conjunction with this where clause.
        /// This can be null if no specific SELECT clause is needed or defined.
        /// </summary>
        public string? SelectClause { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlCommandElements{T}"/> class.
        /// </summary>
        /// <param name="expression">The original LINQ expression defining the condition for the WHERE clause.</param>
        /// <param name="whereClause">The translated SQL WHERE clause string.</param>
        /// <param name="parameters">A list of SQL parameters that correspond to the placeholders in the where clause.</param>
        /// <remarks>
        /// This constructor sets up the basic elements needed for constructing a SQL query from a LINQ expression.
        /// It does not include the SELECT clause as it might not always be required or might be set later.
        /// </remarks>
        public SqlCommandElements(Expression<Func<T, bool>> expression, string whereClause, List<SqlParameter> parameters)
        {
            // Store the original LINQ expression for potential later use or for error reporting
            OriginalExpression = expression;

            // Store the translated SQL WHERE clause
            WhereClause = whereClause;

            // Store the SQL parameters which will be used to prevent SQL injection and for query execution
            SqlParameters = parameters;

            // SelectClause is left uninitialized here as it's optional and might be set later
        }
    }
}