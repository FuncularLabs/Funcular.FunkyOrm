using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Funcular.Data.Orm.Sqlite
{
    /// <summary>
    /// Typed container for the fully built query parts and parameters used to execute a SQLite query.
    /// </summary>
    public class SqliteQueryComponents
    {
        public string SelectClause { get; set; } = string.Empty;
        public string WhereClause { get; set; } = string.Empty;
        public string JoinClause { get; set; } = string.Empty;
        public string OrderByClause { get; set; } = string.Empty;
        public string PagingClause { get; set; } = string.Empty;
        public string AggregateClause { get; set; } = string.Empty;
        public bool IsAggregate { get; set; }
        public List<SqliteParameter> SqlParameters { get; set; } = new List<SqliteParameter>();
    }
}
