using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.Visitors
{
    /// <summary>
    /// Visits LINQ expressions to generate SQL WHERE clauses with parameterized queries.
    /// </summary>
    /// <typeparam name="T">The type of entity being queried.</typeparam>
    public class WhereClauseVisitor<T> : BaseExpressionVisitor<T> where T : class, new()
    {
        private readonly StringBuilder _commandTextBuilder = new StringBuilder();
        private readonly List<SqlParameter> _parameters = [];
        private readonly ParameterGenerator _parameterGenerator;
        private readonly SqlExpressionTranslator _translator;

        /// <summary>
        /// Gets the generated SQL WHERE clause body.
        /// </summary>
        public string WhereClauseBody => _commandTextBuilder.ToString();

        /// <summary>
        /// Gets the list of SQL parameters generated during the visit.
        /// </summary>
        public List<SqlParameter> Parameters => _parameters;

        /// <summary>
        /// Initializes a new instance of the <see cref="WhereClauseVisitor{T}"/> class.
        /// </summary>
        /// <param name="columnNames">Cached column name mappings from property keys to SQL column names.</param>
        /// <param name="unmappedProperties">Cached unmapped properties (marked with NotMappedAttribute).</param>
        /// <param name="parameterGenerator">The parameter generator for creating SQL parameters.</param>
        /// <param name="translator">The translator for converting method calls to SQL.</param>
        public WhereClauseVisitor(
            ConcurrentDictionary<string, string> columnNames,
            ImmutableArray<PropertyInfo> unmappedProperties,
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
                default:
                    throw new NotSupportedException($"Expression type {node.NodeType} is not supported.");
            }
        }

        /// <summary>
        /// Visits a unary expression, handling negation and type conversions.
        /// </summary>
        private void VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Not)
            {
                _commandTextBuilder.Append("NOT ");
                Visit(node.Operand);
            }
            else if (node.NodeType == ExpressionType.Convert)
            {
                Visit(node.Operand);
            }
            else
            {
                throw new NotSupportedException($"Unary operator {node.NodeType} is not supported.");
            }
        }

        /// <summary>
        /// Visits a binary expression to construct SQL conditions.
        /// </summary>
        private void VisitBinary(BinaryExpression node)
        {
            bool needsParentheses = node.NodeType == ExpressionType.AndAlso || node.NodeType == ExpressionType.OrElse;
            bool isNullComparison = (node.Left is ConstantExpression leftConst && leftConst.Value == null) ||
                                    (node.Right is ConstantExpression rightConst && rightConst.Value == null);
            bool isEquality = node.NodeType == ExpressionType.Equal;
            bool isInequality = node.NodeType == ExpressionType.NotEqual;

            if (needsParentheses)
                _commandTextBuilder.Append("(");

            if (isNullComparison && (isEquality || isInequality))
            {
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
        }

        /// <summary>
        /// Visits a member expression to map properties to column names or handle date properties.
        /// </summary>
        private void VisitMember(MemberExpression node)
        {
            if (node.Expression is ParameterExpression)
            {
                var property = node.Member as PropertyInfo;
                if (property != null && !IsUnmappedProperty(property))
                {
                    var columnName = GetColumnName(property);
                    _commandTextBuilder.Append(columnName);
                }
            }
            else if (node.Expression is MemberExpression memberExpression &&
                     memberExpression.Expression is ParameterExpression &&
                     memberExpression.Member.Name == "Value")
            {
                _translator.TranslateDateMember(node, _commandTextBuilder, _parameters, GetColumnName);
            }
            else if (node.Expression is ConstantExpression constantExpression)
            {
                var value = (node.Member as FieldInfo)?.GetValue(constantExpression.Value);
                var param = _parameterGenerator.CreateParameter(value);
                _parameters.Add(param);
                _commandTextBuilder.Append(param.ParameterName);
            }
            else if (node.Expression != null)
            {
                Visit(node.Expression);
            }
        }

        /// <summary>
        /// Visits a constant expression to create SQL parameters.
        /// </summary>
        private void VisitConstant(ConstantExpression node)
        {
            if (node.Value == null)
            {
                _commandTextBuilder.Append("NULL");
            }
            else
            {
                var param = _parameterGenerator.CreateParameter(node.Value);
                _parameters.Add(param);
                _commandTextBuilder.Append(param.ParameterName);
            }
        }

        /// <summary>
        /// Visits a new expression, typically for anonymous types in Contains clauses.
        /// </summary>
        private void VisitNew(NewExpression node)
        {
            var arguments = node.Arguments;
            if (arguments.Count == 0)
            {
                _commandTextBuilder.Append("1=1");
                return;
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
        }

        /// <summary>
        /// Visits a method call expression, delegating to the translator.
        /// </summary>
        private void VisitMethodCall(MethodCallExpression node)
        {
            _translator.TranslateMethodCall(node, _commandTextBuilder, _parameters, GetColumnName);
        }

        /// <summary>
        /// Maps a C# expression operator to its SQL equivalent.
        /// </summary>
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
    }
}