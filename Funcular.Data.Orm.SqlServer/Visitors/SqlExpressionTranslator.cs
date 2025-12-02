using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace Funcular.Data.Orm.Visitors
{
    /// <summary>
    /// Translates specific LINQ expressions and method calls into SQL syntax, producing parameterized SQL.
    /// </summary>
    public class SqlExpressionTranslator
    {
        private readonly ParameterGenerator _parameterGenerator;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlExpressionTranslator"/> class.
        /// </summary>
        /// <param name="parameterGenerator">The parameter generator to use for creating SQL parameters.</param>
        public SqlExpressionTranslator(ParameterGenerator parameterGenerator)
        {
            _parameterGenerator = parameterGenerator ?? throw new ArgumentNullException(nameof(parameterGenerator));
        }

        /// <summary>
        /// Translates a method call expression into SQL, appending the result to the provided StringBuilder.
        /// </summary>
        /// <param name="node">The method call expression to translate.</param>
        /// <param name="commandTextBuilder">The StringBuilder to append the SQL to.</param>
        /// <param name="parameters">The list to add generated SQL parameters to.</param>
        /// <param name="getColumnName">A function to map properties to column names.</param>
        public void TranslateMethodCall(
            MethodCallExpression node,
            StringBuilder commandTextBuilder,
            List<Microsoft.Data.SqlClient.SqlParameter> parameters,
            Func<PropertyInfo, string> getColumnName)
        {
            if (node.Method.Name == "ToString")
            {
                // ToString is a no-op in SQL; just visit the object
                if (node.Object != null)
                {
                    TranslateExpression(node.Object, commandTextBuilder, parameters, getColumnName);
                }
                return;
            }

            if (node.Method.Name == "Contains")
            {
                // Handle string.Contains(substring) differently from collection.Contains(item)
                if (node.Object != null && node.Object.Type == typeof(string))
                {
                    // String Contains - generate LIKE clause
                    TranslateExpression(node.Object, commandTextBuilder, parameters, getColumnName);
                    commandTextBuilder.Append(" LIKE ");
                    
                    var searchValueExpression = node.Arguments[0];
                    if (searchValueExpression.NodeType == ExpressionType.Constant)
                    {
                        var searchValue = ((ConstantExpression)searchValueExpression).Value?.ToString();
                        var param = _parameterGenerator.CreateParameter($"%{searchValue}%");
                        parameters.Add(param);
                        commandTextBuilder.Append(param.ParameterName);
                    }
                    else if (searchValueExpression.NodeType == ExpressionType.MemberAccess)
                    {
                        var memberExpression = (MemberExpression)searchValueExpression;
                        if (memberExpression.Expression is ConstantExpression constantExpression)
                        {
                            var value = (memberExpression.Member as FieldInfo)?.GetValue(constantExpression.Value)?.ToString();
                            var param = _parameterGenerator.CreateParameter($"%{value}%");
                            parameters.Add(param);
                            commandTextBuilder.Append(param.ParameterName);
                        }
                        else
                        {
                            throw new NotSupportedException($"Unsupported member expression in string Contains: {memberExpression}");
                        }
                    }
                    else
                    {
                        throw new NotSupportedException($"Unsupported expression type in string Contains: {searchValueExpression.NodeType}. Expression: {searchValueExpression}");
                    }
                    return;
                }
                
                // Collection Contains - handle IN clause
                var collectionExpression = node.Object ?? node.Arguments.FirstOrDefault(a => a.Type.GetInterfaces().Contains(typeof(System.Collections.IEnumerable)));
                if (collectionExpression != null)
                {
                    System.Collections.IEnumerable collection = null;
                    if (collectionExpression.NodeType == ExpressionType.Constant)
                    {
                        collection = ((ConstantExpression)collectionExpression).Value as System.Collections.IEnumerable;
                    }
                    else
                    {
                        try
                        {
                            collection = Expression.Lambda(collectionExpression).Compile().DynamicInvoke() as System.Collections.IEnumerable;
                        }
                        catch (InvalidOperationException)
                        {
                            throw new NotSupportedException($"Collection Contains with parameter references is not supported: {collectionExpression}");
                        }
                    }

                    if (collection != null)
                    {
                        var values = collection.Cast<object>().Where(v => v != null).ToList();
                        if (!values.Any())
                        {
                            commandTextBuilder.Append("1=0");
                            return;
                        }

                        if ((node.Object != null ? node.Arguments[0] : node.Arguments[1]) is MemberExpression memberExpression)
                        {
                            var propertyInfo = memberExpression.Member as PropertyInfo;
                            if (propertyInfo == null)
                            {
                                throw new InvalidOperationException($"Member {memberExpression.Member.Name} is not a property.");
                            }
                            var memberName = getColumnName(propertyInfo);
                            commandTextBuilder.Append($"{memberName} IN (");
                            var first = true;
                            int index = parameters.Count;
                            foreach (var value in values)
                            {
                                if (!first) commandTextBuilder.Append(",");
                                first = false;
                                var param = _parameterGenerator.CreateParameterForInClause(value, index++);
                                parameters.Add(param);
                                commandTextBuilder.Append(param.ParameterName);
                            }
                            commandTextBuilder.Append(")");
                            return;
                        }
                    }
                }
            }

            if (node.Method.Name == "StartsWith" || node.Method.Name == "EndsWith" || node.Method.Name == "Contains")
            {
                var memberExpression = node.Object as MemberExpression;
                if (memberExpression != null)
                {
                    var memberName = getColumnName(memberExpression.Member as PropertyInfo);
                    var constantExpression = node.Arguments[0] as ConstantExpression;
                    if (constantExpression != null)
                    {
                        var value = constantExpression.Value?.ToString();
                        if (value != null)
                        {
                            var param = _parameterGenerator.CreateParameter(value);
                            parameters.Add(param);
                            if (node.Method.Name == "StartsWith")
                            {
                                commandTextBuilder.Append($"{memberName} LIKE {param.ParameterName} + '%'");
                            }
                            else if (node.Method.Name == "EndsWith")
                            {
                                commandTextBuilder.Append($"{memberName} LIKE '%' + {param.ParameterName}");
                            }
                            else if (node.Method.Name == "Contains")
                            {
                                commandTextBuilder.Append($"{memberName} LIKE '%' + {param.ParameterName} + '%'");
                            }
                            return;
                        }
                    }
                }
            }

            throw new NotSupportedException($"Method call {node.Method.Name} is not supported. Expression: {node}");
        }

        /// <summary>
        /// Translates a member expression for date properties (Year, Month, Day) into SQL.
        /// </summary>
        /// <param name="node">The member expression to translate.</param>
        /// <param name="commandTextBuilder">The StringBuilder to append the SQL to.</param>
        /// <param name="parameters">The list to add generated SQL parameters to.</param>
        /// <param name="getColumnName">A function to map properties to column names.</param>
        public void TranslateDateMember(
            MemberExpression node,
            StringBuilder commandTextBuilder,
            List<Microsoft.Data.SqlClient.SqlParameter> parameters,
            Func<PropertyInfo, string> getColumnName)
        {
            if (node.Expression is MemberExpression valueMember && valueMember.Member.Name == "Value" &&
                valueMember.Expression is MemberExpression dateMember && dateMember.Expression is ParameterExpression)
            {
                var property = dateMember.Member as PropertyInfo;
                if (property != null)
                {
                    var columnName = getColumnName(property);
                    var propertyName = node.Member.Name;
                    if (propertyName == "Year")
                    {
                        commandTextBuilder.Append($"YEAR({columnName})");
                    }
                    else if (propertyName == "Month")
                    {
                        commandTextBuilder.Append($"MONTH({columnName})");
                    }
                    else if (propertyName == "Day")
                    {
                        commandTextBuilder.Append($"DAY({columnName})");
                    }
                    else
                    {
                        throw new NotSupportedException($"Unsupported property {propertyName} in nested expression.");
                    }
                }
            }
            else
            {
                throw new NotSupportedException($"Unsupported date member expression: {node}");
            }
        }

        /// <summary>
        /// Translates a general expression, handling nested expressions as needed.
        /// </summary>
        private void TranslateExpression(
            Expression node,
            StringBuilder commandTextBuilder,
            List<Microsoft.Data.SqlClient.SqlParameter> parameters,
            Func<PropertyInfo, string> getColumnName)
        {
            switch (node)
            {
                case MemberExpression member:
                    if (member.Expression is ParameterExpression)
                    {
                        var property = member.Member as PropertyInfo;
                        if (property != null)
                        {
                            var columnName = getColumnName(property);
                            commandTextBuilder.Append(columnName);
                        }
                    }
                    else if (member.Expression is ConstantExpression constantExpression)
                    {
                        var value = (member.Member as FieldInfo)?.GetValue(constantExpression.Value);
                        var param = _parameterGenerator.CreateParameter(value);
                        parameters.Add(param);
                        commandTextBuilder.Append(param.ParameterName);
                    }
                    else if (member.Expression != null)
                    {
                        TranslateExpression(member.Expression, commandTextBuilder, parameters, getColumnName);
                    }
                    break;
                case ConstantExpression constant:
                    if (constant.Value == null)
                    {
                        commandTextBuilder.Append("NULL");
                    }
                    else
                    {
                        var param = _parameterGenerator.CreateParameter(constant.Value);
                        parameters.Add(param);
                        commandTextBuilder.Append(param.ParameterName);
                    }
                    break;
                default:
                    throw new NotSupportedException($"Expression type {node.NodeType} is not supported for translation. Expression: {node}");
            }
        }
    }
}
