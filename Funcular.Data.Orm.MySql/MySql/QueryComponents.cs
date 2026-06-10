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
        public MethodCallExpression OuterMethodCall { get; set; }
    }
}
