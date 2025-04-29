using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer
{
    /// <summary>
    /// Represents a set of elements used to construct a SQL command from a LINQ expression.
    /// This class encapsulates the original expression, the translated SQL clauses,
    /// and the associated SQL parameters.
    /// </summary>
    /// <typeparam name="T">The type of entity for which the command is being constructed.</typeparam>
    public class SqlQueryComponents<T>
    {
        /// <summary>
        /// Gets or sets the original LINQ expression used to construct the SQL command.
        /// </summary>
        public Expression<Func<T, bool>> OriginalExpression { get; set; }

        /// <summary>
        /// Gets or sets the SQL SELECT clause string.
        /// </summary>
        public string? SelectClause { get; set; }

        /// <summary>
        /// Gets or sets the SQL WHERE clause string derived from the original expression.
        /// </summary>
        public string WhereClause { get; set; }

        /// <summary>
        /// Gets or sets the SQL ORDER BY clause string.
        /// </summary>
        public string OrderByClause { get; set; }

        /// <summary>
        /// Gets or sets the list of SQL parameters corresponding to the placeholders in the clauses.
        /// </summary>
        public List<SqlParameter> SqlParameters { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlQueryComponents{T}"/> class.
        /// </summary>
        /// <param name="expression">The original LINQ expression defining the condition for the WHERE clause.</param>
        /// <param name="selectClause">The SQL SELECT clause string.</param>
        /// <param name="whereClause">The translated SQL WHERE clause string.</param>
        /// <param name="orderByClause">The translated SQL ORDER BY clause string.</param>
        /// <param name="parameters">A list of SQL parameters that correspond to the placeholders in the clauses.</param>
        public SqlQueryComponents(Expression<Func<T, bool>> expression, string? selectClause, string whereClause, string orderByClause, List<SqlParameter> parameters)
        {
            OriginalExpression = expression;
            SelectClause = selectClause;
            WhereClause = whereClause;
            OrderByClause = orderByClause;
            SqlParameters = parameters;
        }
    }
}