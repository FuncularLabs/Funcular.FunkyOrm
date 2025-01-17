using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer
{
    public class WhereClauseElements<T>
    {
        public Expression<Func<T, bool>> OriginalExpression { get; set; }
        public string WhereClause { get; set; }
        public List<SqlParameter> SqlParameters { get; set; }
        public string? SelectClause { get; set; }

        public WhereClauseElements(Expression<Func<T, bool>> expression, string whereClause, List<SqlParameter> parameters)
        {
            OriginalExpression = expression;
            WhereClause = whereClause;
            SqlParameters = parameters;
        }
    }
}