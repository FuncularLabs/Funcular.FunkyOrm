using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Visitors
{
    /// <summary>
    /// Translates specific LINQ expressions and method calls into PostgreSQL SQL syntax.
    /// </summary>
    public class PostgreSqlExpressionTranslator
    {
        private readonly PostgreSqlParameterGenerator _parameterGenerator;

        public PostgreSqlExpressionTranslator(PostgreSqlParameterGenerator parameterGenerator)
        {
            _parameterGenerator = parameterGenerator ?? throw new ArgumentNullException(nameof(parameterGenerator));
        }

        public void TranslateMethodCall(
            MethodCallExpression node,
            StringBuilder commandTextBuilder,
            List<NpgsqlParameter> parameters,
            Func<PropertyInfo, string> getColumnName)
        {
            if (node.Method.Name == "ToString")
            {
                if (node.Object != null)
                {
                    TranslateExpression(node.Object, commandTextBuilder, parameters, getColumnName);
                }
                return;
            }

            if (node.Method.Name == "Contains")
            {
                if (node.Object != null && node.Object.Type == typeof(string))
                {
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
                Expression collectionExpression = node.Object;

                if (collectionExpression == null && node.Arguments.Count > 0)
                {
                    collectionExpression = node.Arguments[0];
                }

                if (collectionExpression is UnaryExpression unary && unary.Method?.Name == "op_Implicit")
                {
                    collectionExpression = unary.Operand;
                }
                else if (collectionExpression is MethodCallExpression mc && mc.Method.Name == "op_Implicit")
                {
                    collectionExpression = mc.Arguments[0];
                }

                if (collectionExpression != null &&
                    (!typeof(System.Collections.IEnumerable).IsAssignableFrom(collectionExpression.Type) || collectionExpression.Type == typeof(string)))
                {
                    collectionExpression = node.Object ?? node.Arguments.FirstOrDefault(a => a.Type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(a.Type));
                }

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

                        var itemArgumentIndex = node.Object != null ? 0 : 1;
                        var itemArgument = node.Arguments[itemArgumentIndex];

                        if (itemArgument is MemberExpression valueUnwrap &&
                            valueUnwrap.Member.Name == "Value" &&
                            valueUnwrap.Expression is MemberExpression innerProp &&
                            innerProp.Expression is ParameterExpression &&
                            Nullable.GetUnderlyingType(innerProp.Type) != null)
                        {
                            var propName = innerProp.Member.Name;
                            var underlyingType = Nullable.GetUnderlyingType(innerProp.Type).Name;
                            throw new Funcular.Data.Orm.Exceptions.NullableExpressionException(
                                $"Do not use '.Value' to unwrap nullable property '{propName}' inside Contains(). " +
                                $"FunkyORM automatically unwraps nullable types. " +
                                $"Instead, cast your collection to match the nullable type: " +
                                $"myList.Cast<{underlyingType}?>().ToList().Contains(p.{propName})");
                        }

                        if (itemArgument is MemberExpression memberExpression)
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
                    var argExpr = node.Arguments[0];
                    string value = null;
                    if (argExpr.NodeType == ExpressionType.Constant)
                    {
                        value = ((ConstantExpression)argExpr).Value?.ToString();
                    }
                    else if (argExpr.NodeType == ExpressionType.MemberAccess)
                    {
                        var memberExpr = (MemberExpression)argExpr;
                        if (memberExpr.Expression is ConstantExpression constExpr)
                        {
                            value = (memberExpr.Member as FieldInfo)?.GetValue(constExpr.Value)?.ToString();
                        }
                        else
                        {
                            throw new NotSupportedException($"Unsupported member expression in {node.Method.Name}: {memberExpr}");
                        }
                    }
                    else
                    {
                        throw new NotSupportedException($"Unsupported expression type in {node.Method.Name}: {argExpr.NodeType}. Expression: {argExpr}");
                    }
                    if (value != null)
                    {
                        var param = _parameterGenerator.CreateParameter(value);
                        parameters.Add(param);
                        // PostgreSQL uses || for string concatenation instead of +
                        string pattern;
                        if (node.Method.Name == "StartsWith")
                        {
                            pattern = $"{param.ParameterName} || '%'";
                        }
                        else if (node.Method.Name == "EndsWith")
                        {
                            pattern = $"'%' || {param.ParameterName}";
                        }
                        else // Contains
                        {
                            pattern = $"'%' || {param.ParameterName} || '%'";
                        }
                        commandTextBuilder.Append($"{memberName} LIKE {pattern}");
                        return;
                    }
                }
            }

            throw new NotSupportedException($"Method call {node.Method.Name} is not supported. Expression: {node}");
        }

        /// <summary>
        /// Translates date member access (Year, Month, Day) into PostgreSQL EXTRACT syntax.
        /// </summary>
        public void TranslateDateMember(
            MemberExpression node,
            StringBuilder commandTextBuilder,
            List<NpgsqlParameter> parameters,
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
                    // PostgreSQL uses EXTRACT instead of YEAR()/MONTH()/DAY()
                    if (propertyName == "Year")
                    {
                        commandTextBuilder.Append($"EXTRACT(YEAR FROM {columnName})");
                    }
                    else if (propertyName == "Month")
                    {
                        commandTextBuilder.Append($"EXTRACT(MONTH FROM {columnName})");
                    }
                    else if (propertyName == "Day")
                    {
                        commandTextBuilder.Append($"EXTRACT(DAY FROM {columnName})");
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

        private void TranslateExpression(
            Expression node,
            StringBuilder commandTextBuilder,
            List<NpgsqlParameter> parameters,
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
