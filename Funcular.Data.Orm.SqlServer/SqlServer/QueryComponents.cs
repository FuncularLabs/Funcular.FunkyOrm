using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer
{
    /// <summary>
    /// Represents the components of a SQL query extracted from a LINQ expression.
    /// Includes WHERE, ORDER BY, paging, and aggregate information.
    /// </summary>
    internal class QueryComponents
    {
        private readonly List<SqlParameter> _parameters = new List<SqlParameter>();

        /// <summary>
        /// Gets or sets the SQL WHERE clause.
        /// </summary>
        public string WhereClause { get; set; }

        /// <summary>
        /// Gets or sets the SQL JOIN clauses.
        /// </summary>
        public string JoinClause { get; set; }

        /// <summary>
        /// Gets or sets the list of individual SQL JOIN clauses.
        /// </summary>
        public List<string> JoinClausesList { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the list of SQL parameters for the query.
        /// </summary>
        public List<SqlParameter> Parameters
        {
            get => _parameters;
            set
            {
                _parameters.Clear();
                _parameters.AddRange(value);
            }
        }

        /// <summary>
        /// Gets or sets the SQL ORDER BY clause.
        /// </summary>
        public string OrderByClause { get; set; }

        /// <summary>
        /// Gets or sets the number of rows to skip (for paging).
        /// </summary>
        public int? Skip { get; set; }

        /// <summary>
        /// Gets or sets the number of rows to take (for paging).
        /// </summary>
        public int? Take { get; set; }

        /// <summary>
        /// Gets or sets the SQL SELECT clause.
        /// </summary>
        public string SelectClause { get; set; }


        /// <summary>
        /// Gets or sets the SQL clause for aggregate operations (Any, All, Count, Average, Min, Max).
        /// </summary>
        public string AggregateClause { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the query is an aggregate query.
        /// </summary>
        public bool IsAggregate { get; set; }

        /// <summary>
        /// Gets or sets the outermost method call expression (e.g., Any, All, Count, etc.).
        /// </summary>
        public MethodCallExpression OuterMethodCall { get; set; }
    }
}