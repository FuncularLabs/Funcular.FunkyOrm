using System.Collections.Generic;
using System.Linq.Expressions;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql
{
    /// <summary>
    /// Represents the components of a SQL query extracted from a LINQ expression for MySQL.
    /// </summary>
    internal class QueryComponents
    {
        private readonly List<MySqlParameter> _parameters = new List<MySqlParameter>();

        public string WhereClause { get; set; }
        public string JoinClause { get; set; }
        public List<string> JoinClausesList { get; set; } = new List<string>();

        public List<MySqlParameter> Parameters
        {
            get => _parameters;
            set
            {
                _parameters.Clear();
                _parameters.AddRange(value);
            }
        }

        public string OrderByClause { get; set; }
        public int? Skip { get; set; }
        public int? Take { get; set; }
        public string SelectClause { get; set; }
        public string AggregateClause { get; set; }
        public bool IsAggregate { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the query should emit SELECT DISTINCT.
        /// </summary>
        public bool IsDistinct { get; set; }

        public MethodCallExpression OuterMethodCall { get; set; }

        /// <summary>
        /// For a top-level scalar projection <c>Select(x =&gt; x.Member)</c>: the selector lambda. When set, the
        /// engine emits the narrow SELECT for the projected member, materializes the entity, then applies this
        /// selector in memory to yield <c>List&lt;<see cref="ScalarMemberType"/>&gt;</c>.
        /// </summary>
        public LambdaExpression ScalarSelector { get; set; }

        /// <summary>
        /// The projected member's type for a scalar projection (the element type of the returned list).
        /// </summary>
        public System.Type ScalarMemberType { get; set; }
    }
}
