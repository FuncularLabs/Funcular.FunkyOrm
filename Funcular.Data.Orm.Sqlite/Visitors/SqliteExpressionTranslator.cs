using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Microsoft.Data.Sqlite;
using Funcular.Data.Orm.Linq;

namespace Funcular.Data.Orm.Sqlite.Visitors
{
    /// <summary>
    /// Translates specific LINQ expressions and method calls into SQLite SQL syntax.
    /// </summary>
    public class SqliteExpressionTranslator
    {
        private readonly SqliteParameterGenerator _parameterGenerator;
#pragma warning disable CS0618 // The obsolete option is accepted so a 3.x caller still compiles; nothing reads it (FD-06).
        private readonly SqliteStringComparison _stringComparison;

        /// <param name="parameterGenerator">Names and creates the command parameters.</param>
        /// <param name="stringComparison">Has no effect (obsolete, 3.11.0 FD-06): stored and never read; no <c>COLLATE</c> is emitted.</param>
        public SqliteExpressionTranslator(SqliteParameterGenerator parameterGenerator, SqliteStringComparison stringComparison = SqliteStringComparison.CaseInsensitive)
        {
            _parameterGenerator = parameterGenerator ?? throw new ArgumentNullException(nameof(parameterGenerator));
            _stringComparison = stringComparison;
        }
#pragma warning restore CS0618

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
            List<SqliteParameter> parameters,
            Func<PropertyInfo, string> getColumnName) =>
            TranslateMethodCall(node, commandTextBuilder, parameters, getColumnName, false);

        /// <summary>
        /// <see cref="TranslateMethodCall(MethodCallExpression, StringBuilder, List{SqliteParameter}, Func{PropertyInfo, string})"/>,
        /// negated when <paramref name="negated"/> is true (docs/plans/NULL_SEMANTICS_PLAN.md §5.2): a string
        /// <c>Contains</c>, <c>StartsWith</c> or <c>EndsWith</c> is <c>(NOT col LIKE … OR col IS NULL)</c>, and every row
        /// (<c>NOT 1=0</c>) for a null search value (AC-N7, AC-N8); a collection <c>Contains</c> follows AC-N6 on a column
        /// that can be null, parenthesized, and is 3.11.0's SQL on one that can't; a <c>Contains</c> that doesn't read
        /// the row is <c>NOT</c> and its literal. Positive, a null item in the collection also selects the NULLs.
        /// </summary>
        /// <param name="node">The method call expression to translate.</param>
        /// <param name="commandTextBuilder">The StringBuilder to append the SQL to.</param>
        /// <param name="parameters">The list to add generated SQL parameters to.</param>
        /// <param name="getColumnName">A function to map properties to column names.</param>
        /// <param name="negated">Whether the call is negated (<c>!x.Name.Contains(v)</c>).</param>
        public void TranslateMethodCall(
            MethodCallExpression node,
            StringBuilder commandTextBuilder,
            List<SqliteParameter> parameters,
            Func<PropertyInfo, string> getColumnName,
            bool negated)
        {
            if (node.Method.Name == "ToString")
            {
                // A property's own ToString(): an integer column as its invariant text, a string column as itself, any
                // other type or any argument rejected (3.11.0, FD-03, D4); x.N.Value.ToString() throws
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
                    commandTextBuilder.Append(negated ? "NOT " + literal : literal);
                    return;
                }

                var searchValueExpression = node.Arguments[0];
                if (!RowIndependentEvaluator.TryReadValue(searchValueExpression, out var searchValue))
                    throw new NotSupportedException($"Unsupported expression in string Contains: {searchValueExpression}");
                var receiverProperty = ReceiverProperty(node.Object, node, ContainsReceiverMessage);
                var receiverColumn = getColumnName(receiverProperty);
                if (searchValue == null)
                {
                    // A null search value matches no row (D8), and its negation every row (AC-N8).
                    commandTextBuilder.Append(negated ? RowIndependentEvaluator.TrueSql : receiverColumn + " LIKE NULL");
                    return;
                }
                // LIKE metacharacters in the value are matched literally (3.11.0, FD-03): LikePattern escapes them
                // with '!' and the pattern names it with ESCAPE '!'.
                var searchParameter = CreateParameter($"%{LikePattern.Escape(searchValue.ToString())}%", parameters);
                AppendLike($"{receiverColumn} LIKE {searchParameter}{LikePattern.EscapeClause}", receiverColumn, receiverProperty, negated,
                    commandTextBuilder);
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
                        commandTextBuilder.Append(negated ? "NOT " + literal : literal);
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
                    List<object> items;
                    try
                    {
                        items = collection.Cast<object>().ToList();
                    }
                    catch (Exception ex) when (!(ex is NotSupportedException)
                        && !(ex is System.Data.Common.DbException) && !(ex.InnerException is System.Data.Common.DbException))
                    {
                        throw new NotSupportedException("Could not evaluate expression: " + collectionExpression, ex);
                    }
                    // A null item is C#'s null: it selects the column's NULLs (AC-N6), where 3.11.0 dropped it.
                    var values = items.Where(v => v != null).ToList();
                    var hasNull = values.Count < items.Count;
                    var memberName = getColumnName(propertyInfo);
                    string inList = null;
                    if (values.Any())
                    {
                        var list = new StringBuilder($"{memberName} IN (");
                        var first = true;
                        int index = parameters.Count;
                        foreach (var value in values)
                        {
                            if (!first) list.Append(",");
                            first = false;
                            var param = _parameterGenerator.CreateParameterForInClause(value, index++);
                            parameters.Add(param);
                            list.Append(param.ParameterName);
                        }
                        list.Append(")");
                        inList = list.ToString();
                    }
                    commandTextBuilder.Append(InPredicate(memberName, inList, hasNull, NullSemantics.CanBeNull(propertyInfo), negated));
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
                    // A null value matches no row (D8), and its negation every row (AC-N8).
                    commandTextBuilder.Append(negated ? RowIndependentEvaluator.TrueSql : $"{memberName} LIKE NULL");
                    return;
                }
                var param = _parameterGenerator.CreateParameter(LikePattern.Escape(argument.ToString()));
                parameters.Add(param);
                var pattern = node.Method.Name == "StartsWith"
                    ? $"{param.ParameterName} || '%'"
                    : $"'%' || {param.ParameterName}";
                AppendLike($"{memberName} LIKE {pattern}{LikePattern.EscapeClause}", memberName, receiverProperty, negated, commandTextBuilder);
                return;
            }

            throw new NotSupportedException($"Method call {node.Method.Name} is not supported. Expression: {node}");
        }

        /// <summary>
        /// Translates <c>ToString</c> on a property of the entity (3.11.0, FD-03; docs/plans/LIKE_ESCAPING_TOSTRING_PLAN.md
        /// D4): an integer column (<c>sbyte</c> through <c>ulong</c>) is its text in the invariant form, one whose value
        /// can be NULL (a <c>Nullable&lt;T&gt;</c>, or a <c>[RemoteProperty]</c> over a missing parent) <c>''</c> for NULL, as
        /// <c>int?.ToString()</c> gives and EF Core sends (3.12.0, docs/plans/NULL_SEMANTICS_PLAN.md R4-10), a string
        /// column is itself; any other column type, and any
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
                var cast = $"CAST({getColumnName(property)} AS TEXT)";
                commandTextBuilder.Append(NullSemantics.CanBeNull(property) ? $"COALESCE({cast}, '')" : cast);
                return;
            }

            throw new NotSupportedException(ToStringMessage + node);
        }

        private static bool IsIntegerType(Type type) =>
            type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);

        /// <summary>
        /// Translates date member access (Year, Month, Day) into SQLite strftime syntax.
        /// </summary>
        public void TranslateDateMember(
            MemberExpression node,
            StringBuilder commandTextBuilder,
            List<SqliteParameter> parameters,
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
        /// Translates a member of a member of the row (3.11.0, FD-01): <c>x.D.Year</c>, <c>.Month</c> or <c>.Day</c> on a
        /// <see cref="DateTime"/> property (also through <c>.Value</c> on a nullable one) as the dialect's date part, and
        /// <c>x.Name.Length</c> on a <see cref="string"/> property as its character length, both over the column
        /// <paramref name="getColumnName"/> gives (the visitor's table-qualified, remote-aware name). Any other member of
        /// a member — another date part, a <see cref="DateTimeOffset"/> part, a member of a nested object, a member of a
        /// computed value — throws <see cref="NotSupportedException"/> naming the member, before any command is sent;
        /// the outer member is never dropped. The WHERE visitor routes every such shape here, whatever the operation
        /// (SELECT, COUNT, DELETE), so the rule is one.
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
                    commandTextBuilder.Append($"LENGTH({getColumnName(property)})");
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

        /// <summary>The dialect's date part: SQLite reads it with <c>strftime</c>, cast to an integer.</summary>
        private static void AppendDatePart(string part, string columnName, StringBuilder commandTextBuilder)
        {
            var format = part == "Year" ? "%Y" : part == "Month" ? "%m" : "%d";
            commandTextBuilder.Append($"CAST(strftime('{format}', {columnName}) AS INTEGER)");
        }

        /// <summary>Adds a parameter holding <paramref name="value"/>; returns its name.</summary>
        private string CreateParameter(object value, List<SqliteParameter> parameters)
        {
            var param = _parameterGenerator.CreateParameter(value);
            parameters.Add(param);
            return param.ParameterName;
        }

        /// <summary>
        /// The property of the lambda parameter that <paramref name="receiver"/> reads; anything else (a member of a
        /// member, a field, a captured value the evaluator didn't take) is rejected with <paramref name="message"/>,
        /// never translated by dropping part of it (AC7).
        /// </summary>
        private static PropertyInfo ReceiverProperty(Expression receiver, MethodCallExpression node, string message)
        {
            if (receiver is MemberExpression member && member.Expression is ParameterExpression && member.Member is PropertyInfo property)
                return property;
            throw new NotSupportedException(message + node);
        }

        /// <summary>
        /// Appends <paramref name="like"/>, a LIKE on <paramref name="column"/>; negated, as <c>NOT</c> and the LIKE, with
        /// <c>OR column IS NULL</c> when the column can be null (AC-N7), parenthesized.
        /// </summary>
        private static void AppendLike(string like, string column, PropertyInfo property, bool negated, StringBuilder commandTextBuilder) =>
            commandTextBuilder.Append(!negated ? like
                : NullSemantics.CanBeNull(property) ? $"(NOT {like} OR {column} IS NULL)"
                : "NOT " + like);

        /// <summary>
        /// A collection <c>Contains</c> on <paramref name="column"/> (AC-N6), where <paramref name="inList"/> is the
        /// <c>IN (…)</c> over the non-null items, or null when there are none. On a column that can't be null, 3.11.0's
        /// SQL. On one that can: a null item also selects the NULLs, and the negation selects them when no item is null
        /// and excludes them when one is; parenthesized.
        /// </summary>
        private static string InPredicate(string column, string inList, bool hasNull, bool canBeNull, bool negated)
        {
            if (!canBeNull)
            {
                var positive = inList ?? RowIndependentEvaluator.FalseSql;
                return negated ? "NOT " + positive : positive;
            }
            if (!negated)
                return inList == null
                    ? (hasNull ? $"{column} IS NULL" : RowIndependentEvaluator.FalseSql)
                    : (hasNull ? $"({inList} OR {column} IS NULL)" : inList);
            return inList == null
                ? (hasNull ? $"{column} IS NOT NULL" : RowIndependentEvaluator.TrueSql)
                : (hasNull ? $"({column} IS NOT NULL AND NOT {inList})" : $"(NOT {inList} OR {column} IS NULL)");
        }
    }
}
