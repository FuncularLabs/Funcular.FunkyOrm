using Microsoft.Data.SqlClient;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

using System;
using System.Diagnostics;
using System.Linq;
using Funcular.Data.Orm;

/// <summary>
/// Visits an expression tree to generate SQL WHERE clauses from LINQ expressions.
/// </summary>
public class EntityExpressionVisitor<T> : ExpressionVisitor where T : class, new()
{
    private readonly StringBuilder _whereClause = new();
    private readonly List<SqlParameter> _parameters;
    private readonly ConcurrentDictionary<string, string> _columnNames;
    private readonly ImmutableArray<PropertyInfo> _unmappedProperties;
    private int _parameterCounter;
    private bool _isNegated;

    /// <summary>
    /// Gets the generated WHERE clause body.
    /// </summary>
    public string WhereClauseBody => _whereClause.ToString();

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
            _whereClause.Append("NOT ");
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
            _whereClause.Append("(");

        if (isNullComparison && (isEquality || isInequality))
        {
            // For null comparisons, use IS NULL or IS NOT NULL
            Expression nonNullSide = node.Left is ConstantExpression ? node.Right : node.Left;
            Visit(nonNullSide);
            _whereClause.Append(isEquality ? " IS NULL" : " IS NOT NULL");
        }
        else
        {
            Visit(node.Left);
            _whereClause.Append(GetOperator(node.NodeType));
            Visit(node.Right);
        }

        if (needsParentheses)
            _whereClause.Append(")");

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
                _whereClause.Append(columnName);
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
                        _whereClause.Append($"YEAR({columnName})");
                    }
                    else if (propertyName == "Month")
                    {
                        _whereClause.Append($"MONTH({columnName})");
                    }
                    else if (propertyName == "Day")
                    {
                        _whereClause.Append($"DAY({columnName})");
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
            _whereClause.Append(parameterName);
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
            _whereClause.Append("NULL");
        }
        else
        {
            var parameterName = $"@p__linq__{_parameterCounter++}";
            _parameters.Add(new SqlParameter(parameterName, node.Value));
            _whereClause.Append(parameterName);
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
            _whereClause.Append("1=1");
            return node;
        }

        _whereClause.Append("(");
        for (int i = 0; i < arguments.Count; i++)
        {
            Visit(arguments[i]);
            if (i < arguments.Count - 1)
            {
                _whereClause.Append(", ");
            }
        }
        _whereClause.Append(")");
        return node;
    }

    /// <summary>
    /// Visits a method call expression to handle string operations and collections.
    /// </summary>
    /// <summary>
    /// Visits a method call expression to handle string operations and collections.
    /// </summary>
    /// <param name="node">The method call expression to visit.</param>
    /// <returns>The visited expression.</returns>
    /// <exception cref="NotSupportedException">Thrown when the method or expression is not supported.</exception>
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.Name == "Contains" && node.Object?.Type == typeof(string))
        {
            _whereClause.Append("(");
            Visit(node.Object);
            _whereClause.Append(" LIKE '%' + ");
            Visit(node.Arguments[0]);
            _whereClause.Append(" + '%')");
        }
        else if (node.Method.Name == "StartsWith" && node.Object?.Type == typeof(string))
        {
            _whereClause.Append("(");
            Visit(node.Object);
            _whereClause.Append(" LIKE ");
            Visit(node.Arguments[0]);
            _whereClause.Append(" + '%')");
        }
        else if (node.Method.Name == "EndsWith" && node.Object?.Type == typeof(string))
        {
            _whereClause.Append("(");
            Visit(node.Object);
            _whereClause.Append(" LIKE '%' + ");
            Visit(node.Arguments[0]);
            _whereClause.Append(")");
        }
        else if (node.Method.Name == "ToLower" && node.Object?.Type == typeof(string))
        {
            _whereClause.Append("LOWER(");
            Visit(node.Object);
            _whereClause.Append(")");
        }
        else if (node.Method.Name == "ToUpper" && node.Object?.Type == typeof(string))
        {
            _whereClause.Append("UPPER(");
            Visit(node.Object);
            _whereClause.Append(")");
        }
        else if (node.Method.Name == "ToString")
        {
            if (node.Object?.Type == typeof(Guid))
            {
                _whereClause.Append("CAST(");
                Visit(node.Object);
                _whereClause.Append(" AS NVARCHAR(36))");
            }
            else
            {
                throw new NotSupportedException($"ToString on type {node.Object?.Type} is not supported.");
            }
        }
        else if (node.Method.Name == "Contains")
        {
            // Handle both collection.Contains(item) and instance collection Contains
            Expression collectionExpression;
            Expression itemExpression;

            if (node.Arguments.Count == 2)
            {
                // Static Contains (e.g., lastNames.Contains(p.LastName))
                collectionExpression = node.Arguments[0];
                itemExpression = node.Arguments[1];
            }
            else if (node.Arguments.Count == 1 && node.Object != null)
            {
                // Instance Contains (e.g., p.LastName.Contains("on"))
                collectionExpression = node.Object;
                itemExpression = node.Arguments[0];
            }
            else
            {
                throw new NotSupportedException($"Unsupported Contains method call with {node.Arguments.Count} arguments.");
            }

            // Evaluate the collection
            IEnumerable<object> collection = null;
            if (collectionExpression is ConstantExpression constExpr)
            {
                collection = constExpr.Value as IEnumerable<object>;
            }
            else if (collectionExpression is MemberExpression memberExpr && memberExpr.Expression is ConstantExpression constMemberExpr)
            {
                var value = (memberExpr.Member as FieldInfo)?.GetValue(constMemberExpr.Value);
                collection = value as IEnumerable<object>;
            }
            else
            {
                throw new NotSupportedException("Collection in Contains must be a constant or field value.");
            }

            if (collection == null || !collection.Any())
            {
                _whereClause.Append(_isNegated ? "1=1" : "1=0");
            }
            else
            {
                _whereClause.Append("(");
                Visit(itemExpression);
                _whereClause.Append(_isNegated ? " NOT IN (" : " IN (");
                var values = collection.ToList();
                for (int i = 0; i < values.Count; i++)
                {
                    var parameterName = $"@p__linq__{_parameterCounter++}";
                    _parameters.Add(new SqlParameter(parameterName, values[i] ?? DBNull.Value));
                    _whereClause.Append(parameterName);
                    if (i < values.Count - 1)
                        _whereClause.Append(", ");
                }
                _whereClause.Append("))");
                // Debug: Log the generated IN clause
                Debug.WriteLine($"Generated IN clause: {_whereClause}");
            }
        }
        else
        {
            throw new NotSupportedException($"Method {node.Method.Name} is not supported in LINQ expressions.");
        }
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