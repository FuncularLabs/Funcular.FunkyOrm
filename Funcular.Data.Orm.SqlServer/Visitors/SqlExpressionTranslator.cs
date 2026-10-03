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
        /// Translates a method call expression into SQL, appending the result to the provided StringBuilder. A part
        /// that doesn't read the row is evaluated as C# does (<see cref="RowIndependentEvaluator"/>); a receiver or a
        /// collection item is a column only when it is a property of the lambda parameter
        /// (docs/plans/WHERE_CAPTURED_VALUES_PLAN.md, §5.2).
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
                // Only a property's own ToString is translated: the column (D15). A ToString of anything else is
                // rejected, never evaluated: the caller computes the string before the query (D16).
                AppendColumn(node.Object, node, ToStringMessage, commandTextBuilder, getColumnName);
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
                AppendParameter($"%{searchValue}%", commandTextBuilder, parameters);
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

                    // A lazy collection runs the caller's code here; what it throws is reported as the read is (D9).
                    List<object> values;
                    try
                    {
                        values = collection.Cast<object>().Where(v => v != null).ToList();
                    }
                    catch (Exception ex)
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
                var param = _parameterGenerator.CreateParameter(argument.ToString());
                parameters.Add(param);
                var pattern = node.Method.Name == "StartsWith"
                    ? $"{param.ParameterName} + '%'"
                    : $"'%' + {param.ParameterName}";
                commandTextBuilder.Append($"{memberName} LIKE {pattern}");
                return;
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
