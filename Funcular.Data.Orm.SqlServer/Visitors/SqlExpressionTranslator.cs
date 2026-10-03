using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Funcular.Data.Orm.Linq;

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
            // Inert change to trigger build
            if (node.Method.Name == "ToString")
            {
                TranslateToString(node, commandTextBuilder, parameters, getColumnName);
                return;
            }

            if (node.Method.Name == "Contains")
            {
                // Handle string.Contains(substring) differently from collection.Contains(item)
                if (node.Object != null && node.Object.Type == typeof(string))
                {
                    // String Contains - a LIKE clause that matches the value literally
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

                // If it's an extension method (Object is null), the first argument is the collection
                if (collectionExpression == null && node.Arguments.Count > 0)
                {
                    collectionExpression = node.Arguments[0];
                }

                // Unwrap implicit conversions (e.g. Array -> ReadOnlySpan in .NET 10+)
                if (collectionExpression is UnaryExpression unary && unary.Method?.Name == "op_Implicit")
                {
                    collectionExpression = unary.Operand;
                }
                else if (collectionExpression is MethodCallExpression mc && mc.Method.Name == "op_Implicit")
                {
                    collectionExpression = mc.Arguments[0];
                }

                // Verify it is IEnumerable (and not string, though string should be handled above)
                if (collectionExpression != null &&
                    (!typeof(System.Collections.IEnumerable).IsAssignableFrom(collectionExpression.Type) || collectionExpression.Type == typeof(string)))
                {
                    // Fallback to searching arguments for IEnumerable if the above didn't work
                    // This preserves original behavior for edge cases
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

                        // Detect .Value unwrap inside Contains (e.g., list.Contains(p.HospitalId.Value))
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
                        string pattern;
                        if (node.Method.Name == "StartsWith")
                        {
                            pattern = $"{param.ParameterName} + '%'";
                        }
                        else if (node.Method.Name == "EndsWith")
                        {
                            pattern = $"'%' + {param.ParameterName}";
                        }
                        else if (node.Method.Name == "Contains")
                        {
                            pattern = $"'%' + {param.ParameterName} + '%'";
                        }
                        else
                        {
                            throw new NotSupportedException($"Unexpected method {node.Method.Name}");
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
        /// .NET's. <c>x.N.Value.ToString()</c> throws <see cref="Funcular.Data.Orm.Exceptions.NullableExpressionException"/>,
        /// whose message gives the rule above where the property's own <c>ToString()</c> isn't translated either.
        /// Any other receiver is translated as itself.
        /// </summary>
        private void TranslateToString(
            MethodCallExpression node,
            StringBuilder commandTextBuilder,
            List<Microsoft.Data.SqlClient.SqlParameter> parameters,
            Func<PropertyInfo, string> getColumnName)
        {
            if (node.Object is MemberExpression valueMember && valueMember.Member.Name == "Value" &&
                valueMember.Expression is MemberExpression nullableMember && nullableMember.Expression is ParameterExpression &&
                Nullable.GetUnderlyingType(nullableMember.Type) != null)
            {
                var propName = nullableMember.Member.Name;
                var underlying = Nullable.GetUnderlyingType(nullableMember.Type);
                // The advice holds only where D4 translates the property's ToString(): an integer, with no argument.
                var notTranslated = node.Arguments.Count > 0
                    ? $"ToString() on a column of type {underlying.Name} isn't translated with an argument (a format or a format provider)"
                    : !IsIntegerType(underlying)
                        ? $"ToString() on a column of type {underlying.Name} isn't translated, because the database's text for it isn't .NET's"
                        : null;
                throw new Funcular.Data.Orm.Exceptions.NullableExpressionException(notTranslated == null
                    ? $"Do not use '.Value' on nullable property '{propName}' in LINQ expressions. " +
                      $"FunkyORM automatically unwraps nullable types. " +
                      $"Use '{propName}' directly (e.g., p.{propName}.ToString() instead of p.{propName}.Value.ToString())."
                    : $"Do not use '.Value' on nullable property '{propName}' in LINQ expressions. " +
                      $"FunkyORM automatically unwraps nullable types, but {notTranslated}; compare the column's value instead: {node}");
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
                // Twenty characters hold every integer's text (the longest is -9223372036854775808).
                var columnName = getColumnName(property);
                var cast = $"CAST({columnName} AS NVARCHAR(20))";
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
