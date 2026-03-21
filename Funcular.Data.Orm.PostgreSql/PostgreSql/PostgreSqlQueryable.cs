using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Funcular.Data.Orm.PostgreSql
{
    public class PostgreSqlQueryable<T> : IOrderedQueryable<T>
    {
        private readonly IQueryProvider _provider;
        private readonly Expression _expression;

        public PostgreSqlQueryable(IQueryProvider provider)
        {
            _provider = provider;
            _expression = Expression.Constant(this);
        }

        public PostgreSqlQueryable(IQueryProvider provider, Expression expression)
        {
            _provider = provider;
            _expression = expression;
        }

        public Type ElementType => typeof(T);
        public Expression Expression => _expression;
        public IQueryProvider Provider => _provider;

        public IEnumerator<T> GetEnumerator()
        {
            return _provider.Execute<IEnumerable<T>>(_expression).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
