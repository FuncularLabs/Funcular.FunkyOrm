using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer
{
    public class WhereClauseGenerator
    {
        // Dictionaries and caches assumed to be populated elsewhere

        /// <summary>
        /// Generates the where clause.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="expression">The expression.</param>
        /// <returns>WhereClauseElements&lt;T&gt;.</returns>
        public WhereClauseElements<T> GenerateWhereClause<T>(Expression<Func<T, bool>> expression) where T : class, new()
        {
            var parameters = new List<SqlParameter>();
            var whereClause = new StringBuilder();

            var parameterCounter = 0;
            var visitor = new ExpressionVisitor<T>(parameters, SqlDataProvider._columnNames, SqlDataProvider._unmappedPropertiesCache, ref parameterCounter);
            visitor.Visit(expression);

            whereClause.Append(visitor.WhereClauseBody);

            return new WhereClauseElements<T>(expression, whereClause.ToString(), parameters);
        }
    }
}