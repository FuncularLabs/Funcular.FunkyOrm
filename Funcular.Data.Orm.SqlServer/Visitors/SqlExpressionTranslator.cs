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

        private const string ItemMessage =
            "Collection Contains is translated only as <property> IN (<values>), with a property of the entity as its item and a collection that doesn't read the row: ";
        private const string NullCollectionMessage = "The collection of Contains is null: ";
        private const string NotACollectionMessage = "Contains is translated only on a collection: ";
        private const string ContainsReceiverMessage =
            "Contains() is translated only on a property of the entity, or on a value that doesn't read the row: ";
        private const string ToStringMessage =
            "ToString() is translated only on a property of the entity; for a value that doesn't read the row, compute the string before the query: ";

        /// <summary>
        /// Translates a method call expression into SQL, appending the result to the provided StringBuilder. A
        /// <c>Contains</c> or a value that doesn't read the row is evaluated as C# does
        /// (<see cref="RowIndependentEvaluator"/>); a captured <c>ToString()</c> is rejected; a receiver or a collection
        /// item is a column only when it is a property of the lambda parameter (docs/plans/WHERE_CAPTURED_VALUES_PLAN.md,
        /// §5.2).
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
                // A property's own ToString(): an integer column as its invariant text, a string column as itself, any
                // other type or any argument rejected (3.10.2, FD-03, D4); x.N.Value.ToString() throws
                // NullableExpressionException. A ToString of anything else is rejected, never evaluated: the caller
                // computes the string before the query (FD-02, D16).
                TranslateToString(node, commandTextBuilder, getColumnName);
                return;
            }

            if (node.Method.Name == "Contains" && node.Object != null && node.Object.Type == typeof(string))
            {
                // string's Contains that doesn't read the row is answered as C# answers it (D4, D7).
                if (RowIndependentEvaluator.TryTranslateContains(node, out var literal))
                {
                    commandTextBuilder.Append(literal);
                    return;
                }

                var searchValueExpression = node.Arguments[0];
                if (!RowIndependentEvaluator.TryReadValue(searchValueExpression, out var searchValue))
                    throw new NotSupportedException($"Unsupported expression in string Contains: {searchValueExpression}");
                AppendColumn(node.Object, node, ContainsReceiverMessage, commandTextBuilder, getColumnName);
                if (searchValue == null)
                {
                    // A null search value matches no row (D8).
                    commandTextBuilder.Append(" LIKE NULL");
                    return;
                }
                commandTextBuilder.Append(" LIKE ");
                // LIKE metacharacters in the value are matched literally (3.10.2, FD-03): LikePattern escapes them
                // with '!' and the pattern names it with ESCAPE '!'.
                AppendParameter($"%{LikePattern.Escape(searchValue.ToString())}%", commandTextBuilder, parameters);
                commandTextBuilder.Append(LikePattern.EscapeClause);
                return;
            }

            if (node.Method.Name == "Contains" && node.Type == typeof(bool))
            {
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
                    // A Contains that doesn't read the row is answered as C# answers it, before anything else is read (D4).
                    if (RowIndependentEvaluator.TryTranslateContains(node, out var literal))
                    {
                        commandTextBuilder.Append(literal);
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

                    // The item is checked before the collection is read, whatever the collection holds (D5).
                    if (!(itemArgument is MemberExpression memberExpression) || !(memberExpression.Expression is ParameterExpression))
                    {
                        throw new NotSupportedException(ItemMessage + node);
                    }
                    var propertyInfo = memberExpression.Member as PropertyInfo;
                    if (propertyInfo == null)
                    {
                        throw new InvalidOperationException($"Member {memberExpression.Member.Name} is not a property.");
                    }

                    object collectionValue;
                    if (collectionExpression.NodeType == ExpressionType.Constant)
                    {
                        collectionValue = ((ConstantExpression)collectionExpression).Value;
                    }
                    else
                    {
                        Delegate read;
                        try
                        {
                            read = Expression.Lambda(collectionExpression).Compile();
                        }
                        catch (InvalidOperationException)
                        {
                            throw new NotSupportedException($"Collection Contains with parameter references is not supported: {collectionExpression}");
                        }
                        try
                        {
                            collectionValue = read.DynamicInvoke();
                        }
                        catch (TargetInvocationException ex)
                        {
                            throw new NotSupportedException("Could not evaluate expression: " + collectionExpression, ex.InnerException ?? ex);
                        }
                    }

                    if (collectionValue == null)
                    {
                        throw new NotSupportedException(NullCollectionMessage + node);
                    }
                    if (!(collectionValue is System.Collections.IEnumerable collection))
                    {
                        throw new NotSupportedException(NotACollectionMessage + node);
                    }

                    // A lazy collection runs the caller's code here; what it throws is reported as the read is (D9). A query
                    // used as the collection runs a query here: a database error, or the query's own NotSupportedException,
                    // passes through unchanged.
                    List<object> values;
                    try
                    {
                        values = collection.Cast<object>().Where(v => v != null).ToList();
                    }
                    catch (Exception ex) when (!(ex is NotSupportedException)
                        && !(ex is System.Data.Common.DbException) && !(ex.InnerException is System.Data.Common.DbException))
                    {
                        throw new NotSupportedException("Could not evaluate expression: " + collectionExpression, ex);
                    }
                    if (!values.Any())
                    {
                        commandTextBuilder.Append("1=0");
                        return;
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

            if (node.Method.Name == "StartsWith" || node.Method.Name == "EndsWith")
            {
                // The receiver is a column only when it is a property of the lambda parameter (D6).
                if (!(node.Object is MemberExpression receiver) || !(receiver.Expression is ParameterExpression) ||
                    !(receiver.Member is PropertyInfo receiverProperty))
                {
                    throw new NotSupportedException($"{node.Method.Name}() is translated only on a property of the entity: {node}");
                }
                var argExpr = node.Arguments[0];
                if (!RowIndependentEvaluator.TryReadValue(argExpr, out var argument))
                {
                    throw new NotSupportedException($"Unsupported expression in {node.Method.Name}: {argExpr}");
                }
                var memberName = getColumnName(receiverProperty);
                if (argument == null)
                {
                    // A null value matches no row (D8).
                    commandTextBuilder.Append($"{memberName} LIKE NULL");
                    return;
                }
                var param = _parameterGenerator.CreateParameter(LikePattern.Escape(argument.ToString()));
                parameters.Add(param);
                var pattern = node.Method.Name == "StartsWith"
                    ? $"{param.ParameterName} + '%'"
                    : $"'%' + {param.ParameterName}";
                commandTextBuilder.Append($"{memberName} LIKE {pattern}{LikePattern.EscapeClause}");
                return;
            }

            throw new NotSupportedException($"Method call {node.Method.Name} is not supported. Expression: {node}");
        }

        /// <summary>
        /// Translates <c>ToString</c> on a property of the entity (3.10.2, FD-03; docs/plans/LIKE_ESCAPING_TOSTRING_PLAN.md
        /// D4): an integer column (<c>sbyte</c> through <c>ulong</c>) is its text in the invariant form
        /// (<c>NVARCHAR(20)</c> holds every integer's text; the longest is <c>-9223372036854775808</c>), a nullable one
        /// <c>''</c> for null as <c>int?.ToString()</c> gives, a string column is itself; any other column type, and any
        /// argument (a format or a format provider), throws <see cref="NotSupportedException"/>, because the database's
        /// text isn't .NET's. <c>x.N.Value.ToString()</c> throws <see cref="Funcular.Data.Orm.Exceptions.NullableExpressionException"/>,
        /// whose message gives the rule above where the property's own <c>ToString()</c> isn't translated either. Any
        /// other receiver is rejected (FD-02, D16), never evaluated.
        /// </summary>
        private void TranslateToString(MethodCallExpression node, StringBuilder commandTextBuilder, Func<PropertyInfo, string> getColumnName)
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
                var cast = $"CAST({getColumnName(property)} AS NVARCHAR(20))";
                commandTextBuilder.Append(member.Type == type ? cast : $"COALESCE({cast}, '')");
                return;
            }

            throw new NotSupportedException(ToStringMessage + node);
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
                    var propertyName = node.Member.Name;
                    if (propertyName != "Year" && propertyName != "Month" && propertyName != "Day")
                        throw new NotSupportedException($"Unsupported property {propertyName} in nested expression.");
                    AppendDatePart(propertyName, getColumnName(property), commandTextBuilder);
                }
            }
            else
            {
                throw new NotSupportedException($"Unsupported date member expression: {node}");
            }
        }

        /// <summary>
        /// Translates a member of a member of the row (3.10.2, FD-01): <c>x.D.Year</c>, <c>.Month</c> or <c>.Day</c> on a
        /// <see cref="DateTime"/> property (also through <c>.Value</c> on a nullable one) as the dialect's date part, and
        /// <c>x.Name.Length</c> on a <see cref="string"/> property as its character length, both over the column
        /// <paramref name="getColumnName"/> gives (the visitor's table-qualified, remote-aware name). Any other member of
        /// a member — another date part, a <see cref="DateTimeOffset"/> part, a member of a nested object, a member of a
        /// computed value — throws <see cref="NotSupportedException"/> naming the member, before any command is sent;
        /// the outer member is never dropped. The WHERE visitor routes every such shape here, whatever the operation
        /// (SELECT, COUNT, DELETE), so the rule is one.
        /// <para>The length is <c>LEN(column + N'x') - 1</c>: <c>LEN</c> alone ignores trailing spaces, which C#'s
        /// <c>Length</c> counts, so <c>x.Name.Length &lt; 3</c> would select (and delete) <c>'ab '</c>.</para>
        /// </summary>
        /// <param name="node">The member access whose receiver is itself a member access.</param>
        /// <param name="commandTextBuilder">The StringBuilder to append the SQL to.</param>
        /// <param name="getColumnName">A function to map properties to column names.</param>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        /// <exception cref="NotSupportedException">The shape has no translation.</exception>
        public void TranslateMemberOfMember(MemberExpression node, StringBuilder commandTextBuilder, Func<PropertyInfo, string> getColumnName)
        {
            if (node == null)
                throw new ArgumentNullException(nameof(node));
            if (TryGetRowProperty(node.Expression, out var property))
            {
                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                var member = node.Member.Name;
                if (type == typeof(DateTime) && (member == "Year" || member == "Month" || member == "Day"))
                {
                    AppendDatePart(member, getColumnName(property), commandTextBuilder);
                    return;
                }
                if (type == typeof(string) && member == "Length")
                {
                    commandTextBuilder.Append($"(LEN({getColumnName(property)} + N'x') - 1)");
                    return;
                }
                throw new NotSupportedException(
                    $"Member '{member}' of '{property.Name}' isn't translated in a WHERE clause: only Year, Month and Day of a DateTime " +
                    $"property and Length of a string property are; compare the property itself, or compute the value before the query: {node}");
            }
            throw new NotSupportedException(
                $"Member '{node.Member.Name}' of '{node.Expression}' isn't translated in a WHERE clause; compare a property of the " +
                $"entity, or compute the value before the query: {node}");
        }

        /// <summary>The property of the lambda parameter that <paramref name="receiver"/> reads: <c>x.P</c>, or <c>x.P.Value</c> on a nullable <c>P</c>.</summary>
        private static bool TryGetRowProperty(Expression receiver, out PropertyInfo property)
        {
            property = null;
            if (receiver is MemberExpression value && value.Member.Name == "Value" && value.Expression is MemberExpression inner
                && Nullable.GetUnderlyingType(inner.Type) != null)
                receiver = inner;
            if (receiver is MemberExpression member && member.Expression is ParameterExpression && member.Member is PropertyInfo found)
            {
                property = found;
                return true;
            }
            return false;
        }

        /// <summary>The dialect's date part: <c>YEAR()</c>, <c>MONTH()</c>, <c>DAY()</c>.</summary>
        private static void AppendDatePart(string part, string columnName, StringBuilder commandTextBuilder) =>
            commandTextBuilder.Append($"{part.ToUpperInvariant()}({columnName})");

        /// <summary>Appends a parameter holding <paramref name="value"/>.</summary>
        private void AppendParameter(object value, StringBuilder commandTextBuilder, List<Microsoft.Data.SqlClient.SqlParameter> parameters)
        {
            var param = _parameterGenerator.CreateParameter(value);
            parameters.Add(param);
            commandTextBuilder.Append(param.ParameterName);
        }

        /// <summary>
        /// Appends the column of <paramref name="receiver"/>, which must be a property of the lambda parameter; anything
        /// else (a member of a member, a field, a captured value the evaluator didn't take) is rejected with
        /// <paramref name="message"/>, never translated by dropping part of it (AC7).
        /// </summary>
        private static void AppendColumn(
            Expression receiver,
            MethodCallExpression node,
            string message,
            StringBuilder commandTextBuilder,
            Func<PropertyInfo, string> getColumnName)
        {
            if (receiver is MemberExpression member && member.Expression is ParameterExpression && member.Member is PropertyInfo property)
            {
                commandTextBuilder.Append(getColumnName(property));
                return;
            }
            throw new NotSupportedException(message + node);
        }
    }
}
