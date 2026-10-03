using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Funcular.Data.Orm.Linq;
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
                TranslateToString(node, commandTextBuilder, parameters, getColumnName);
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
                        var param = _parameterGenerator.CreateParameter($"%{LikePattern.Escape(searchValue)}%");
                        parameters.Add(param);
                        commandTextBuilder.Append(param.ParameterName).Append(LikePattern.EscapeClause);
                    }
                    else if (searchValueExpression.NodeType == ExpressionType.MemberAccess)
                    {
                        var memberExpression = (MemberExpression)searchValueExpression;
                        if (memberExpression.Expression is ConstantExpression constantExpression)
                        {
                            var value = (memberExpression.Member as FieldInfo)?.GetValue(constantExpression.Value)?.ToString();
                            var param = _parameterGenerator.CreateParameter($"%{LikePattern.Escape(value)}%");
                            parameters.Add(param);
                            commandTextBuilder.Append(param.ParameterName).Append(LikePattern.EscapeClause);
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
                        var param = _parameterGenerator.CreateParameter(LikePattern.Escape(value));
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
                        commandTextBuilder.Append($"{memberName} LIKE {pattern}{LikePattern.EscapeClause}");
                        return;
                    }
                }
            }

            throw new NotSupportedException($"Method call {node.Method.Name} is not supported. Expression: {node}");
        }

        /// <summary>
        /// Translates <c>ToString</c> (docs/plans/LIKE_ESCAPING_TOSTRING_PLAN.md, D4). On a column (a property of the
        /// lambda's parameter), an integer column is sent as its text in the invariant form (a nullable one as <c>''</c>
        /// for null, as <c>int?.ToString()</c> gives), a string column as itself; any other column type, and any argument
        /// (a format or a format provider), throws <see cref="NotSupportedException"/>, because the database's text isn't
        /// .NET's. <c>x.N.Value.ToString()</c> throws <see cref="Funcular.Data.Orm.Exceptions.NullableExpressionException"/>.
        /// Any other receiver is translated as itself.
        /// </summary>
        private void TranslateToString(
            MethodCallExpression node,
            StringBuilder commandTextBuilder,
            List<NpgsqlParameter> parameters,
            Func<PropertyInfo, string> getColumnName)
        {
            if (node.Object is MemberExpression valueMember && valueMember.Member.Name == "Value" &&
                valueMember.Expression is MemberExpression nullableMember && nullableMember.Expression is ParameterExpression &&
                Nullable.GetUnderlyingType(nullableMember.Type) != null)
            {
                var propName = nullableMember.Member.Name;
                throw new Funcular.Data.Orm.Exceptions.NullableExpressionException(
                    $"Do not use '.Value' on nullable property '{propName}' in LINQ expressions. " +
                    $"FunkyORM automatically unwraps nullable types. " +
                    $"Use '{propName}' directly (e.g., p.{propName}.ToString() instead of p.{propName}.Value.ToString()).");
            }

            if (node.Object is MemberExpression member && member.Expression is ParameterExpression &&
                member.Member is PropertyInfo property)
            {
                var type = Nullable.GetUnderlyingType(member.Type) ?? member.Type;
                if (node.Arguments.Count > 0)
                    throw new NotSupportedException(
                        $"ToString() on a column of type {type.Name} isn't translated with an argument (a format or a format provider); " +
                        $"compare the column's value instead: {node}");
                if (type == typeof(string))
                {
                    commandTextBuilder.Append(getColumnName(property));
                    return;
                }
                if (!IsIntegerType(type))
                    throw new NotSupportedException(
                        $"ToString() on a column of type {type.Name} isn't translated, because the database's text for it isn't .NET's; " +
                        $"compare the column's value instead: {node}");
                var columnName = getColumnName(property);
                var cast = $"CAST({columnName} AS TEXT)";
                commandTextBuilder.Append(member.Type == type ? cast : $"COALESCE({cast}, '')");
                return;
            }

            if (node.Object != null)
            {
                TranslateExpression(node.Object, commandTextBuilder, parameters, getColumnName);
            }
        }

        private static bool IsIntegerType(Type type) =>
            type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);

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
