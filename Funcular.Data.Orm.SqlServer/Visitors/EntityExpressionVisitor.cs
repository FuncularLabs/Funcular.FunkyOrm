﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.Visitors;

/// <summary>
/// Visits an expression tree to generate SQL WHERE clauses from LINQ expressions.
/// </summary>
public class EntityExpressionVisitor<T> : ExpressionVisitor where T : class, new()
{
    private readonly StringBuilder _commandTextBuilder = new();
    private readonly List<SqlParameter> _parameters;
    private readonly ConcurrentDictionary<string, string> _columnNames;
    private readonly ImmutableArray<PropertyInfo> _unmappedProperties;
    private readonly List<(string ColumnName, bool IsDescending)> _orderByClauses = new List<(string, bool)>(); 
    private int _parameterCounter;
    private bool _isNegated;

    /// <summary>
    /// Gets the generated WHERE clause body.
    /// </summary>
    public string WhereClauseBody => _commandTextBuilder.ToString();

    /// <summary>
    /// Gets the list of SQL parameters generated during the visit.
    /// </summary>
    public List<SqlParameter> Parameters => _parameters;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityExpressionVisitor{T}"/> class.
    /// </summary>
    /// <param name="parameters">The list to store SQL parameters.</param>
    /// <param name="columnNames">Cached column name mappings.</param>
    /// <param name="unmappedProperties">Cached unmapped properties.</param>
    /// <param name="parameterCounter">Reference to the parameter counter for unique parameter names.</param>
    public EntityExpressionVisitor(List<SqlParameter> parameters, ConcurrentDictionary<string, string> columnNames,
        ImmutableArray<PropertyInfo> unmappedProperties, ref int parameterCounter)
    {
        _parameters = parameters;
        _columnNames = columnNames;
        _unmappedProperties = unmappedProperties;
        _parameterCounter = parameterCounter;
    }

    /// <summary>
    /// Visits the expression tree, starting with the lambda body if present.
    /// </summary>
    public override Expression Visit(Expression? node)
    {
        if (node == null) return node!;
        if (node is LambdaExpression lambda)
        {
            return Visit(lambda.Body);
        }
        return base.Visit(node);
    }

    /// <summary>
    /// Visits a unary expression, handling negation and type conversions.
    /// </summary>
    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Not)
        {
            _commandTextBuilder.Append("NOT ");
            _isNegated = true;
            Visit(node.Operand);
            _isNegated = false;
        }
        else if (node.NodeType == ExpressionType.Convert)
        {
            Visit(node.Operand);
        }
        else
        {
            throw new NotSupportedException($"Unary operator {node.NodeType} is not supported.");
        }
        return node;
    }

    /// <summary>
    /// Visits a binary expression to construct SQL conditions.
    /// </summary>
    protected override Expression VisitBinary(BinaryExpression node)
    {
        bool needsParentheses = node.NodeType == ExpressionType.AndAlso || node.NodeType == ExpressionType.OrElse;

        // Check for null comparisons (e.g., x.FirstName == null or x.FirstName != null)
        bool isNullComparison = (node.Left is ConstantExpression leftConst && leftConst.Value == null) ||
                                (node.Right is ConstantExpression rightConst && rightConst.Value == null);
        bool isEquality = node.NodeType == ExpressionType.Equal;
        bool isInequality = node.NodeType == ExpressionType.NotEqual;

        if (needsParentheses)
            _commandTextBuilder.Append("(");

        if (isNullComparison && (isEquality || isInequality))
        {
            // For null comparisons, use IS NULL or IS NOT NULL
            Expression nonNullSide = node.Left is ConstantExpression ? node.Right : node.Left;
            Visit(nonNullSide);
            _commandTextBuilder.Append(isEquality ? " IS NULL" : " IS NOT NULL");
        }
        else
        {
            Visit(node.Left);
            _commandTextBuilder.Append(GetOperator(node.NodeType));
            Visit(node.Right);
        }

        if (needsParentheses)
            _commandTextBuilder.Append(")");

        return node;
    }

    /// <summary>
    /// Visits a member expression to map properties to column names.
    /// </summary>
    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression is ParameterExpression)
        {
            var property = node.Member as PropertyInfo;
            if (property != null && !_unmappedProperties.Any(p => p.Name == property.Name))
            {
                var columnName = _columnNames.GetOrAdd(property.ToDictionaryKey(), p => GetColumnName(property));
                _commandTextBuilder.Append(columnName);
            }
        }
        else if (node.Expression is MemberExpression memberExpression)
        {
            if (memberExpression.Expression is ParameterExpression && memberExpression.Member.Name == "Value")
            {
                var property = memberExpression.Member as PropertyInfo;
                if (property != null && !_unmappedProperties.Any(p => p.Name == property.Name))
                {
                    var columnName = _columnNames.GetOrAdd(property.ToDictionaryKey(), p => GetColumnName(property));
                    var propertyName = node.Member.Name;
                    if (propertyName == "Year")
                    {
                        _commandTextBuilder.Append($"YEAR({columnName})");
                    }
                    else if (propertyName == "Month")
                    {
                        _commandTextBuilder.Append($"MONTH({columnName})");
                    }
                    else if (propertyName == "Day")
                    {
                        _commandTextBuilder.Append($"DAY({columnName})");
                    }
                    else
                    {
                        throw new NotSupportedException($"Unsupported property {propertyName} in nested expression.");
                    }
                }
            }
            else
            {
                Visit(node.Expression);
            }
        }
        else if (node.Expression is ConstantExpression constantExpression)
        {
            var value = (node.Member as FieldInfo)?.GetValue(constantExpression.Value);
            var parameterName = $"@p__linq__{_parameterCounter++}";
            _parameters.Add(new SqlParameter(parameterName, value ?? DBNull.Value));
            _commandTextBuilder.Append(parameterName);
        }
        else if (node.Expression != null)
        {
            Visit(node.Expression);
        }
        return node;
    }

    /// <summary>
    /// Visits a constant expression to create SQL parameters.
    /// </summary>
    protected override Expression VisitConstant(ConstantExpression node)
    {
        if (node.Value == null)
        {
            _commandTextBuilder.Append("NULL");
        }
        else
        {
            var parameterName = $"@p__linq__{_parameterCounter++}";
            _parameters.Add(new SqlParameter(parameterName, node.Value));
            _commandTextBuilder.Append(parameterName);
        }
        return node;
    }

    /// <summary>
    /// Visits a new expression, typically for anonymous types in Contains clauses.
    /// </summary>
    protected override Expression VisitNew(NewExpression node)
    {
        var arguments = node.Arguments;
        if (arguments.Count == 0)
        {
            _commandTextBuilder.Append("1=1");
            return node;
        }

        _commandTextBuilder.Append("(");
        for (int i = 0; i < arguments.Count; i++)
        {
            Visit(arguments[i]);
            if (i < arguments.Count - 1)
            {
                _commandTextBuilder.Append(", ");
            }
        }
        _commandTextBuilder.Append(")");
        return node;
    }

    /// <summary>
    /// Visits a method call expression to handle string operations and collections.
    /// </summary>
    /// <param name="node">The method call expression to visit.</param>
    /// <returns>The visited expression.</returns>
    /// <exception cref="NotSupportedException">Thrown when the method or expression is not supported.</exception>
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.Name == "ToString")
        {
            Visit(node.Object);
            return node;
        }

        if (node.Method.Name == "OrderBy" || node.Method.Name == "OrderByDescending" ||
            node.Method.Name == "ThenBy" || node.Method.Name == "ThenByDescending")
        {
            // Visit the source expression (e.g., the query before OrderBy)
            Visit(node.Arguments[0]);

            // The second argument is the lambda expression (e.g., p => p.LastName)
            var lambda = (LambdaExpression)node.Arguments[1];
            var memberExpression = lambda.Body as MemberExpression;
            if (memberExpression != null)
            {
                var propertyInfo = memberExpression.Member as PropertyInfo;
                if (propertyInfo == null)
                {
                    throw new InvalidOperationException($"Member {memberExpression.Member.Name} is not a property.");
                }
                var columnName = GetColumnName(propertyInfo);
                var isDescending = node.Method.Name == "OrderByDescending" || node.Method.Name == "ThenByDescending";
                _orderByClauses.Add((columnName, isDescending));
            }

            return node;
        }

        if (node.Method.Name == "Contains")
        {
            var collectionExpression = node.Object ?? node.Arguments.FirstOrDefault(a => a.Type.GetInterfaces().Contains(typeof(IEnumerable)));
            if (collectionExpression != null)
            {
                IEnumerable collection = null;
                if (collectionExpression.NodeType == ExpressionType.Constant)
                {
                    collection = ((ConstantExpression)collectionExpression).Value as IEnumerable;
                }
                else
                {
                    collection = Expression.Lambda(collectionExpression).Compile().DynamicInvoke() as IEnumerable;
                }

                if (collection != null)
                {
                    var values = collection.Cast<object>().Where(v => v != null).ToList();
                    if (!values.Any())
                    {
                        _commandTextBuilder.Append("1=0");
                        return node;
                    }
                    var memberExpression = (node.Object != null ? node.Arguments[0] : node.Arguments[1]) as MemberExpression;
                    if (memberExpression != null)
                    {
                        var propertyInfo = memberExpression.Member as PropertyInfo;
                        if (propertyInfo == null)
                        {
                            throw new InvalidOperationException($"Member {memberExpression.Member.Name} is not a property.");
                        }
                        var memberName = GetColumnName(propertyInfo);
                        _commandTextBuilder.Append($"{memberName} IN (");
                        var first = true;
                        foreach (var value in values)
                        {
                            if (!first) _commandTextBuilder.Append(",");
                            first = false;
                            var paramName = $"@p{_parameters.Count}";
                            _parameters.Add(new SqlParameter(paramName, value));
                            _commandTextBuilder.Append(paramName);
                        }
                        _commandTextBuilder.Append(")");
                        return node;
                    }
                }
            }
        }

        if (node.Method.Name == "StartsWith" || node.Method.Name == "EndsWith" || node.Method.Name == "Contains")
        {
            var memberExpression = node.Object as MemberExpression;
            if (memberExpression != null)
            {
                var memberName = GetColumnName(memberExpression.Member as PropertyInfo);
                var constantExpression = node.Arguments[0] as ConstantExpression;
                if (constantExpression != null)
                {
                    var value = constantExpression.Value?.ToString();
                    if (value != null)
                    {
                        var paramName = $"@p{_parameters.Count}";
                        if (node.Method.Name == "StartsWith")
                        {
                            _commandTextBuilder.Append($"{memberName} LIKE {paramName} + '%'");
                            _parameters.Add(new SqlParameter(paramName, value));
                        }
                        else if (node.Method.Name == "EndsWith")
                        {
                            _commandTextBuilder.Append($"{memberName} LIKE '%' + {paramName}");
                            _parameters.Add(new SqlParameter(paramName, value));
                        }
                        else if (node.Method.Name == "Contains")
                        {
                            _commandTextBuilder.Append($"{memberName} LIKE '%' + {paramName} + '%'");
                            _parameters.Add(new SqlParameter(paramName, value));
                        }
                        return node;
                    }
                }
            }
        }

        _commandTextBuilder.Append("1=0");
        return node;
    }
    private string GetOperator(ExpressionType nodeType) => nodeType switch
    {
        ExpressionType.Equal => " = ",
        ExpressionType.NotEqual => " != ",
        ExpressionType.GreaterThan => " > ",
        ExpressionType.GreaterThanOrEqual => " >= ",
        ExpressionType.LessThan => " < ",
        ExpressionType.LessThanOrEqual => " <= ",
        ExpressionType.AndAlso => " AND ",
        ExpressionType.OrElse => " OR ",
        _ => throw new NotSupportedException($"Operator {nodeType} is not supported.")
    };

    /// <summary>
    /// Determines the SQL data type for a given value.
    /// </summary>
    public static SqlDbType GetSqlDbType(object? value) => value switch
    {
        null => SqlDbType.NVarChar,
        string => SqlDbType.NVarChar,
        int => SqlDbType.Int,
        long => SqlDbType.BigInt,
        bool => SqlDbType.Bit,
        DateTime => SqlDbType.DateTime2,
        Guid => SqlDbType.UniqueIdentifier,
        decimal => SqlDbType.Decimal,
        double => SqlDbType.Float,
        float => SqlDbType.Real,
        _ => throw new NotSupportedException($"Type {value.GetType()} is not supported for SQL parameters.")
    };

    private string GetColumnName(PropertyInfo property)
    {
        return property.GetCustomAttribute<ColumnAttribute>()?.Name ?? property.Name.ToLower();
    }
}