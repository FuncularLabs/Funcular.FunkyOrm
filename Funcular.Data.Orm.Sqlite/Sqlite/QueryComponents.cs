using System.Collections.Generic;
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
        public string OuterMethodCall { get; set; }
    }
}
