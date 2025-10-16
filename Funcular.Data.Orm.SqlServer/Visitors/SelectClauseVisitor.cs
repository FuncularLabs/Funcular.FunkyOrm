using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Microsoft.Data.SqlClient;
using Funcular.Data.Orm.SqlServer;

namespace Funcular.Data.Orm.Visitors
{
    /// <summary>
    /// Visits LINQ expressions to generate SQL SELECT clauses with projections, including CASE statements.
    /// </summary>
    /// <typeparam name="T">The type of entity being queried.</typeparam>
    public class SelectClauseVisitor<T> : BaseExpressionVisitor<T> where T : class, new()
    {
        private readonly StringBuilder _selectBuilder = new StringBuilder();
        private readonly List<SqlParameter> _parameters = new List<SqlParameter> { };
        private readonly ParameterGenerator _parameterGenerator;
        private readonly SqlExpressionTranslator _translator;

        /// <summary>
        /// Gets the generated SQL SELECT clause.
        /// </summary>
        public string SelectClause => _selectBuilder.ToString();

        /// <summary>
        /// Gets the list of SQL parameters generated during the visit.
        /// </summary>
        public List<SqlParameter> Parameters => _parameters;

        /// <summary>
        /// Initializes a new instance of the <see cref="SelectClauseVisitor{T}"/> class.
        /// </summary>
        /// <param name="columnNames">Cached column name mappings from property keys to SQL column names.</param>
        /// <param name="unmappedProperties">Cached unmapped properties (marked with NotMappedAttribute).</param>
        /// <param name="parameterGenerator">The parameter generator for creating SQL parameters.</param>
        /// <param name="translator">The translator for converting method calls to SQL.</param>
        public SelectClauseVisitor(
            ConcurrentDictionary<string, string> columnNames,
            ICollection<PropertyInfo> unmappedProperties,
            ParameterGenerator parameterGenerator,
            SqlExpressionTranslator translator)
            : base(columnNames, unmappedProperties)
        {
            _parameterGenerator = parameterGenerator ?? throw new ArgumentNullException(nameof(parameterGenerator));
            _translator = translator ?? throw new ArgumentNullException(nameof(translator));
        }

        /// <summary>
        /// Visits the expression tree, starting with the lambda body if present.
        /// </summary>
        /// <param name="expression">The LINQ expression to visit.</param>
        public override void Visit(Expression expression)
        {
            if (expression is LambdaExpression lambda)
            {
                Visit(lambda.Body);
            }
            else
            {
                VisitExpression(expression);
            }
        }

        /// <summary>
        /// Visits an expression and dispatches to the appropriate method based on its type.
        /// </summary>
        private void VisitExpression(Expression node)
        {
            switch (node)
            {
                case UnaryExpression unary:
                    VisitUnary(unary);
                    break;
                case BinaryExpression binary:
                    VisitBinary(binary);
                    break;
                case MemberExpression member:
                    VisitMember(member);
                    break;
                case ConstantExpression constant:
                    VisitConstant(constant);
                    break;
                case MethodCallExpression methodCall:
                    VisitMethodCall(methodCall);
                    break;
                case NewExpression newExpr:
                    VisitNew(newExpr);
                    break;
                case ConditionalExpression conditional:
                    VisitConditional(conditional);
                    break;
                case MemberInitExpression memberInit:
                    VisitMemberInit(memberInit);
                    break;
                default:
                    throw new NotSupportedException($"Expression type {node.NodeType} is not supported in SELECT.");
            }
        }

        /// <summary>
        /// Visits a unary expression, handling negation and type conversions.
        /// </summary>
        private void VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Not)
            {
                _selectBuilder.Append("NOT ");
                Visit(node.Operand);
            }
            else if (node.NodeType == ExpressionType.Convert)
            {
                Visit(node.Operand);
            }
            else
            {
                throw new NotSupportedException($"Unary operator {node.NodeType} is not supported in SELECT.");
            }
        }

        /// <summary>
        /// Visits a binary expression to construct SQL conditions.
        /// </summary>
        private void VisitBinary(BinaryExpression node)
        {
            Visit(node.Left);
            _selectBuilder.Append(GetOperator(node.NodeType));
            Visit(node.Right);
        }

        /// <summary>
        /// Visits a member expression to map properties to column names.
        /// </summary>
        private void VisitMember(MemberExpression node)
        {
            // Handle Nullable<T>.HasValue -> column IS NOT NULL
            if (node.Member.MemberType == MemberTypes.Property && node.Member.Name == "HasValue"
                                                               && node.Expression is MemberExpression innerMember)
            {
                var property = innerMember.Member as PropertyInfo;
                if (property != null && !IsUnmappedProperty(property))
                {
                    var columnName = GetColumnName(property);
                    _selectBuilder.Append($"{columnName} IS NOT NULL");
                    return;
                }
            }

            if (node.Expression is ParameterExpression)
            {
                var property = node.Member as PropertyInfo;
                if (property != null && !IsUnmappedProperty(property))
                {
                    var columnName = GetColumnName(property);
                    _selectBuilder.Append(columnName);
                }
                else if (property != null && IsUnmappedProperty(property))
                {
                    // For unmapped properties, we might assign to them, but in SELECT, it's the source.
                    // This might not be used directly.
                    throw new NotSupportedException("Unmapped properties cannot be selected directly.");
                }
            }
            else if (node.Expression is ConstantExpression constantExpression)
            {
                var value = (node.Member as FieldInfo)?.GetValue(constantExpression.Value);
                var param = _parameterGenerator.CreateParameter(value);
                _parameters.Add(param);
                _selectBuilder.Append(param.ParameterName);
            }
            else if (node.Expression != null)
            {
                Visit(node.Expression);
            }
        }

        // ... existing code ...

        /// <summary>
        /// Visits a constant expression to create SQL parameters.
        /// </summary>
        private void VisitConstant(ConstantExpression node)
        {
            if (node.Value == null)
            {
                _selectBuilder.Append(" NULL ");
            }
            else if (node.Value is bool b)
            {
                // For boolean values in CASE statements, cast to BIT to ensure proper type
                _selectBuilder.Append(b ? " CAST(1 AS BIT) " : " CAST(0 AS BIT) ");
            }
            else
            {
                var param = _parameterGenerator.CreateParameter(node.Value);
                _parameters.Add(param);
                _selectBuilder.Append(param.ParameterName);
            }
        }

        /// <summary>
        /// Visits a new expression for anonymous types.
        /// </summary>
        private void VisitNew(NewExpression node)
        {
            // For anonymous types, generate SELECT with aliases.
            // But for simplicity, assume it's handled in MemberInit.
        }

        // ... existing code ...

        /// <summary>
        /// Visits a member init expression for object initialization.
        /// </summary>
        private void VisitMemberInit(MemberInitExpression node)
        {
            var bindings = node.Bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding is MemberAssignment assignment)
                {
                    var property = assignment.Member as PropertyInfo;
                    if (property != null && property.DeclaringType == typeof(T) && !IsUnmappedProperty(property))
                    {
                        if (!(assignment.Expression is MemberExpression memberExpr && memberExpr.Member == property && memberExpr.Expression is ParameterExpression))
                        {
                            throw new InvalidOperationException("Cannot assign computed values to mapped properties in SELECT projections.");
                        }
                    }
                    // For unmapped properties or simple copies, proceed to visit the expression (e.g., conditional for CASE)
                    Visit(assignment.Expression);
                    _selectBuilder.Append($" AS {assignment.Member.Name}");
                    if (i < bindings.Count - 1)
                        _selectBuilder.Append(", ");
                }
            }
        }

        /// <summary>
        /// Visits a method call expression, delegating to the translator.
        /// </summary>
        private void VisitMethodCall(MethodCallExpression node)
        {
            _translator.TranslateMethodCall(node, _selectBuilder, _parameters, GetColumnName);
        }

        /// <summary>
        /// Visits a conditional expression to generate a CASE WHEN ... THEN ... ELSE ... END statement.
        /// </summary>
        private void VisitConditional(ConditionalExpression node)
        {
            _selectBuilder.Append(" CASE WHEN ");
            Visit(node.Test);
            _selectBuilder.Append(" THEN ");
            Visit(node.IfTrue);
            _selectBuilder.Append(" ELSE ");
            Visit(node.IfFalse);
            _selectBuilder.Append(" END");
        }

        /// <summary>
        /// Maps a C# expression operator to its SQL equivalent.
        /// </summary>
        private string GetOperator(ExpressionType nodeType)
        {
            switch (nodeType)
            {
                case ExpressionType.Equal:
                    return " = ";
                case ExpressionType.NotEqual:
                    return " != ";
                case ExpressionType.GreaterThan:
                    return " > ";
                case ExpressionType.GreaterThanOrEqual:
                    return " >= ";
                case ExpressionType.LessThan:
                    return " < ";
                case ExpressionType.LessThanOrEqual:
                    return " <= ";
                case ExpressionType.AndAlso:
                    return " AND ";
                case ExpressionType.OrElse:
                    return " OR ";
                default:
                    throw new NotSupportedException($"Operator {nodeType} is not supported in SELECT.");
            }
        }
    }
}