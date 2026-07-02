using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.Data.Sqlite;

namespace Funcular.Data.Orm.Sqlite
{
    /// <summary>
    /// Mutable query state used during LINQ expression parsing.
    /// </summary>
    internal class QueryComponents
    {
        public string WhereClause { get; set; } = string.Empty;
        public string JoinClause { get; set; } = string.Empty;
        public List<string> JoinClausesList { get; set; } = new List<string>();
        public List<SqliteParameter> Parameters { get; set; } = new List<SqliteParameter>();
        public int? Skip { get; set; }
        public int? Take { get; set; }
        public string AggregateClause { get; set; }
        public bool IsAggregate { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the query should emit SELECT DISTINCT.
        /// </summary>
        public bool IsDistinct { get; set; }
        public string OuterMethodCall { get; set; }

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
