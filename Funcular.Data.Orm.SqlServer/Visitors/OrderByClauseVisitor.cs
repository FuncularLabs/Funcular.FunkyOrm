using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Funcular.Data.Orm.Visitors
{
    using OrderByClause = (string ColumnName, bool IsDescending);

    /// <summary>
    /// Visits LINQ expressions to generate SQL ORDER BY clauses from ordering methods.
    /// </summary>
    /// <typeparam name="T">The type of entity being queried.</typeparam>
    public class OrderByClauseVisitor<T> : BaseExpressionVisitor<T> where T : class, new()
    {
        private readonly ICollection<OrderByClause> _orderByClauses = new List<(string, bool)>();

        /// <summary>
        /// Gets the generated ORDER BY clause.
        /// </summary>
        public string OrderByClause
        {
            get
            {
                if (!_orderByClauses.Any())
                    return string.Empty;

                var clause = "ORDER BY " + string.Join(", ", _orderByClauses.Select(c => $"{c.ColumnName} {(c.IsDescending ? "DESC" : "ASC")}"));
                Console.WriteLine($"Generated ORDER BY clause: {clause}");
                return clause;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OrderByClauseVisitor{T}"/> class.
        /// </summary>
        /// <param name="columnNames">Cached column name mappings from property keys to SQL column names.</param>
        /// <param name="unmappedProperties">Cached unmapped properties (marked with NotMappedAttribute).</param>
        public OrderByClauseVisitor(
            System.Collections.Concurrent.ConcurrentDictionary<string, string> columnNames,
            System.Collections.Immutable.ImmutableArray<PropertyInfo> unmappedProperties)
            : base(columnNames, unmappedProperties)
        {
        }

        /// <summary>
        /// Visits the specified expression to generate an ORDER BY clause.
        /// </summary>
        /// <param name="expression">The LINQ expression to visit.</param>
        public override void Visit(Expression expression)
        {
            VisitExpression(expression);
        }

        /// <summary>
        /// Visits an expression and dispatches to the appropriate method based on its type.
        /// </summary>
        private void VisitExpression(Expression node)
        {
            switch (node)
            {
                case MethodCallExpression methodCall:
                    VisitMethodCall(methodCall);
                    break;
                case LambdaExpression lambda:
                    Visit(lambda.Body);
                    break;
                default:
                    throw new NotSupportedException($"Expression type {node.NodeType} is not supported for ORDER BY clauses.");
            }
        }

        /// <summary>
        /// Visits a method call expression to process OrderBy/ThenBy methods.
        /// </summary>
        private void VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name is "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending")
            {
                // First, traverse to the beginning of the chain to process in correct order
                if (node.Arguments[0] is MethodCallExpression previousCall)
                {
                    // Only continue if the previous call is also an ordering method
                    if (previousCall.Method.Name is "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending")
                    {
                        Visit(previousCall);
                    }
                    // If it's not an ordering method (like Where), we stop here as we've processed all ordering operations
                }

                // Then process the current method call
                var lambda = (LambdaExpression)((UnaryExpression)node.Arguments[1]).Operand;
                var isDescending = node.Method.Name is "OrderByDescending" or "ThenByDescending";
                VisitOrderingExpression(lambda.Body, isDescending);
            }
            else
            {
                throw new NotSupportedException($"Method {node.Method.Name} is not supported in ORDER BY expressions.");
            }
        }

        /// <summary>
        /// Visits an ordering expression to extract the column name and direction.
        /// </summary>
        private void VisitOrderingExpression(Expression expression, bool isDescending)
        {
            if (expression is MemberExpression memberExpression)
            {
                var property = memberExpression.Member as PropertyInfo;
                if (property != null && !IsUnmappedProperty(property))
                {
                    var columnName = GetColumnName(property);
                    _orderByClauses.Add((columnName, isDescending));
                }
            }
            else
            {
                throw new NotSupportedException("Only simple member access is supported in OrderBy expressions.");
            }
        }
    }
}