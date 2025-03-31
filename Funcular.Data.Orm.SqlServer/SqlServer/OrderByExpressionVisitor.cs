using Microsoft.Data.SqlClient;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Reflection;

using System;
using System.Linq;
using Funcular.Data.Orm;

/// <summary>
/// Visits an expression tree to generate SQL ORDER BY clauses from LINQ ordering methods.
/// </summary>
public class OrderByExpressionVisitor<T> : ExpressionVisitor where T : class, new()
{
    private readonly List<SqlParameter> _parameters;
    private readonly ConcurrentDictionary<string, string> _columnNames;
    private readonly ImmutableArray<PropertyInfo> _unmappedProperties;
    private readonly List<(string ColumnName, bool IsDescending)> _orderByClauses;
    private int _parameterCounter;

    /// <summary>
    /// Gets the generated ORDER BY clause.
    /// </summary>
    public string OrderByClause => _orderByClauses.Any()
        ? "ORDER BY " + string.Join(", ", _orderByClauses.Select(c => $"{c.ColumnName} {(c.IsDescending ? "DESC" : "ASC")}"))
        : string.Empty;

    /// <summary>
    /// Gets the list of SQL parameters generated during the visit.
    /// </summary>
    public List<SqlParameter> Parameters => _parameters;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderByExpressionVisitor{T}"/> class.
    /// </summary>
    /// <param name="parameters">The list to store SQL parameters.</param>
    /// <param name="columnNames">Cached column name mappings.</param>
    /// <param name="unmappedProperties">Cached unmapped properties.</param>
    /// <param name="parameterCounter">Reference to the parameter counter for unique parameter names.</param>
    public OrderByExpressionVisitor(List<SqlParameter> parameters, ConcurrentDictionary<string, string> columnNames,
        ImmutableArray<PropertyInfo> unmappedProperties, ref int parameterCounter)
    {
        _parameters = parameters;
        _columnNames = columnNames;
        _unmappedProperties = unmappedProperties;
        _orderByClauses = new List<(string, bool)>();
        _parameterCounter = parameterCounter;
    }

    /// <summary>
    /// Visits a method call expression to process OrderBy/ThenBy methods.
    /// </summary>
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.Name is "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending")
        {
            var lambda = (LambdaExpression)((UnaryExpression)node.Arguments[1]).Operand;
            var isDescending = node.Method.Name is "OrderByDescending" or "ThenByDescending";
            VisitOrderingExpression(lambda.Body, isDescending);
            // Continue traversing the chain to handle ThenBy
            if (node.Arguments[0] is MethodCallExpression)
            {
                Visit(node.Arguments[0]);
            }
        }
        return node;
    }

    private void VisitOrderingExpression(Expression expression, bool isDescending)
    {
        if (expression is MemberExpression memberExpression)
        {
            var property = memberExpression.Member as PropertyInfo;
            if (property != null && !_unmappedProperties.Any(p => p.Name == property.Name))
            {
                var columnName = _columnNames.GetOrAdd(property.ToDictionaryKey(), p => GetColumnName(property));
                _orderByClauses.Add((columnName, isDescending));
            }
        }
        else
        {
            throw new NotSupportedException("Only simple member access is supported in OrderBy expressions.");
        }
    }

    private string GetColumnName(PropertyInfo property)
    {
        return property.GetCustomAttribute<ColumnAttribute>()?.Name ?? property.Name.ToLower();
    }
}