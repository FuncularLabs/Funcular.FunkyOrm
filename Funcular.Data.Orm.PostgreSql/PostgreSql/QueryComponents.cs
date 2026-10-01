using System.Collections.Generic;
using System.Linq.Expressions;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql
{
    /// <summary>
    /// Represents the components of a SQL query extracted from a LINQ expression for PostgreSQL.
    /// </summary>
    internal class QueryComponents
    {
        private readonly List<NpgsqlParameter> _parameters = new List<NpgsqlParameter>();

        public string WhereClause { get; set; }
        public string JoinClause { get; set; }
        public List<string> JoinClausesList { get; set; } = new List<string>();

        public List<NpgsqlParameter> Parameters
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

        /// <summary>
        /// The single-row terminal operator (<c>First*</c>, <c>Single*</c>, <c>Last*</c>), or <c>null</c>.
        /// </summary>
        public string Terminal { get; set; }

        /// <summary>
        /// The row limit for a single-row terminal without user paging (<c>LIMIT</c>), or <c>null</c>.
        /// </summary>
        public int? RowLimit { get; set; }

        /// <summary>
        /// The translated ordering terms, used to invert the order for <c>Last*</c>.
        /// </summary>
        public List<Funcular.Data.Orm.Linq.OrderByTerm> OrderByTerms { get; set; } = new List<Funcular.Data.Orm.Linq.OrderByTerm>();

        /// <summary>
        /// Whether a <c>Take(n &lt;= 0)</c> makes the result empty without querying.
        /// </summary>
        public bool IsEmptyByTake { get; set; }
    }
}
