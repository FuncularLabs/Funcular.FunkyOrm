using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql
{
    /// <summary>
    /// Represents a set of elements used to construct a SQL command from a LINQ expression for MySQL.
    /// </summary>
    public class MySqlQueryComponents<T>
    {
        public Expression<Func<T, bool>> OriginalExpression { get; set; }
        public string SelectClause { get; set; }
        public string WhereClause { get; set; }
        public string JoinClause { get; set; }
        public List<string> JoinClausesList { get; set; }
        public string OrderByClause { get; set; }
        public List<MySqlParameter> SqlParameters { get; set; }

        public MySqlQueryComponents(Expression<Func<T, bool>> expression, string selectClause, string whereClause, string joinClause, string orderByClause, List<MySqlParameter> parameters)
        {
            OriginalExpression = expression;
            SelectClause = selectClause;
            WhereClause = whereClause;
            JoinClause = joinClause;
            OrderByClause = orderByClause;
            SqlParameters = parameters;
        }
    }
}
