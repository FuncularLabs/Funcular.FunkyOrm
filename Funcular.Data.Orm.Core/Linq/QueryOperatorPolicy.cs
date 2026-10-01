using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Funcular.Data.Orm.Linq
{
    /// <summary>
    /// Exact-overload allow-list for the LINQ operators FunkyORM translates to SQL, applied as a pre-pass over
    /// the method spine of a query before any provider translates it. A rejected chain throws
    /// <see cref="NotSupportedException"/> naming the operator, and no query or aggregate command is executed.
    /// </summary>
    /// <remarks>
    /// <para>Pass 1 walks the spine outer→inner and classifies every node with the allow-list before reading its
    /// source, so the outermost unsupported operator is reported. The spine must end at a queryable's own root.</para>
    /// <para>Pass 2 walks inner→outer and applies, at each node: operators after <c>Skip</c>/<c>Take</c>; a second
    /// <c>OrderBy</c>; <c>Cast</c>/<c>OfType</c> against the row type; and predicate lambdas that don't bind to the
    /// entity. The row type starts as the entity type and changes only at a scalar <c>Select(x =&gt; x.Member)</c>;
    /// after any other unsupported <c>Select</c> it is unknown, and the parse loop rejects that <c>Select</c>.</para>
    /// </remarks>
    public static class QueryOperatorPolicy
    {
        private static readonly HashSet<string> OperatorNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Where", "Select",
            "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
            "Skip", "Take", "Distinct",
            "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault",
            "Any", "All", "Count", "LongCount",
            "Sum", "Average", "Min", "Max",
            "Cast", "OfType",
        };

        // Operators that also have a supported overload without a lambda.
        private static readonly HashSet<string> ParameterlessOperators = new HashSet<string>(StringComparer.Ordinal)
        {
            "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault", "Any", "Count", "LongCount",
        };

        // Operators whose lambda is a predicate the translator binds to the entity type.
        private static readonly HashSet<string> PredicateOperators = new HashSet<string>(StringComparer.Ordinal)
        {
            "Where", "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault",
            "Any", "All", "Count", "LongCount",
        };

        private static readonly MethodInfo[] Supported = typeof(Queryable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(IsSupportedShape)
            .ToArray();

        private static readonly HashSet<int> SupportedTokens = new HashSet<int>(Supported.Select(m => m.MetadataToken));

        private static readonly IReadOnlyCollection<MethodInfo> SupportedView = Array.AsReadOnly(Supported);

        /// <summary>
        /// The supported <see cref="Queryable"/> overloads, as generic method definitions.
        /// </summary>
        public static IReadOnlyCollection<MethodInfo> SupportedOperators => SupportedView;

        /// <summary>
        /// Returns whether <paramref name="method"/> (or its generic method definition) is a supported
        /// <see cref="Queryable"/> overload. Non-<c>Queryable</c> methods and non-generic overloads are never
        /// supported.
        /// </summary>
        public static bool IsAllowed(MethodInfo method)
        {
            if (method == null || method.DeclaringType != typeof(Queryable) || !method.IsGenericMethod)
                return false;
            var definition = method.IsGenericMethodDefinition ? method : method.GetGenericMethodDefinition();
            return SupportedTokens.Contains(definition.MetadataToken);
        }

        /// <summary>
        /// Throws <see cref="NotSupportedException"/> when the query's method spine contains an unsupported
        /// operator, or a supported operator in an unsupported position.
        /// </summary>
        public static void EnsureSupported(Expression expression)
        {
            if (expression == null)
                throw new ArgumentNullException(nameof(expression));

            // Pass 1 (outer→inner): the allow-list over the whole spine, then the root.
            var spine = new List<MethodCallExpression>();
            var node = expression;
            Type entityType;
            while (true)
            {
                if (node is MethodCallExpression call)
                {
                    if (!IsAllowed(call.Method))
                        throw Rejected(call.Method);
                    spine.Add(call);
                    node = call.Arguments[0];
                    continue;
                }
                if (node is ConstantExpression constant && constant.Value is IQueryable root
                    && ReferenceEquals(root.Expression, constant))
                {
                    entityType = root.ElementType;
                    break;
                }
                throw new NotSupportedException(
                    $"The query contains a {node.NodeType} node of type {FriendlyName(node.Type)} that isn't a supported " +
                    "LINQ operator over the queryable's own root; it is not translated to SQL. Compose the query from " +
                    "Query<T>() with the supported operators only.");
            }

            // Pass 2 (inner→outer), per node: D8 → D10 → D5/I3 → I1.
            var rowType = entityType;
            var pagingSeen = false;
            var skipOpen = false;           // after a Skip, with only Select/Cast/OfType since: one Take may follow
            var orderingSeen = false;
            var afterNonSubsetSelect = false;
            var afterOtherSelect = false;   // an unsupported Select: the row type is unknown from here
            for (var i = spine.Count - 1; i >= 0; i--)
            {
                var call = spine[i];
                var name = call.Method.Name;

                // D8: after the first Skip/Take, only Select, Cast/OfType (judged below), a parameterless
                // First*/Single*, and one Take after a Skip are translated.
                if (name == "Skip")
                {
                    if (pagingSeen)
                        throw AfterPaging(name);
                    pagingSeen = true;
                    skipOpen = true;
                }
                else if (name == "Take")
                {
                    if (pagingSeen && !skipOpen)
                        throw AfterPaging(name);
                    pagingSeen = true;
                    skipOpen = false;
                }
                else if (pagingSeen && !(name == "Select" || name == "Cast" || name == "OfType"
                                         || (call.Arguments.Count == 1 && IsFirstOrSingle(name))))
                {
                    throw AfterPaging(name);
                }

                // D10: a second OrderBy anywhere after an earlier ordering.
                if (name == "OrderBy" || name == "OrderByDescending")
                {
                    if (orderingSeen)
                        throw new NotSupportedException(
                            $"{name}(...) after an earlier ordering is not translated. In LINQ the later ordering becomes the " +
                            "primary key and the earlier keys only break ties; write that as one chain, primary key first: " +
                            $"query.{name}(later).ThenBy(earlier).");
                    orderingSeen = true;
                }
                else if (name == "ThenBy" || name == "ThenByDescending")
                {
                    orderingSeen = true;
                }

                // D5/I3: Cast/OfType are judged against the row type, which they never change. After an unsupported
                // Select the row type is unknown: the parse loop rejects that Select with the accurate message.
                if (!afterOtherSelect && name == "Cast")
                {
                    var target = call.Method.GetGenericArguments()[0];
                    if (target != rowType && (rowType.IsValueType || !target.IsAssignableFrom(rowType)))
                        throw new NotSupportedException(
                            $"Cast<{FriendlyName(target)}>() is not translated; FunkyORM supports only identity and " +
                            $"reference-conversion casts. Materialize first: query.ToList().Cast<{FriendlyName(target)}>().");
                }
                else if (!afterOtherSelect && name == "OfType")
                {
                    var target = call.Method.GetGenericArguments()[0];
                    if (target != rowType)
                        throw new NotSupportedException(
                            $"OfType<{FriendlyName(target)}>() is not translated; FunkyORM supports only an identity " +
                            $"OfType. Materialize first: query.ToList().OfType<{FriendlyName(target)}>().");
                    if (rowType != entityType && (!rowType.IsValueType || Nullable.GetUnderlyingType(rowType) != null))
                        throw new NotSupportedException(
                            $"OfType<{FriendlyName(target)}>() over a nullable or reference member would drop nulls, and " +
                            "is not translated. Filter before the projection instead: " +
                            "query.Where(x => x.M != null).Select(x => x.M).");
                }

                // I1: a predicate lambda must bind to the entity type. Operators outer to a non-subset Select are
                // left to the parse loop's own guards.
                if (!afterNonSubsetSelect && PredicateOperators.Contains(name) && call.Arguments.Count == 2)
                {
                    var parameterType = StripQuotes(call.Arguments[1]).Parameters[0].Type;
                    if (parameterType != entityType)
                    {
                        // `where TEntity : Object` doesn't compile, and a helper constrained to an interface fails in
                        // the WHERE translator (Convert(x, I).M): only a base class gets the generic-helper advice.
                        var advice = parameterType.IsClass && parameterType != typeof(object)
                            ? $"make the helper generic (where TEntity : {FriendlyName(parameterType)}), or query the concrete type."
                            : $"or apply it to the concrete IQueryable<{FriendlyName(entityType)}>.";
                        throw new NotSupportedException(
                            $"{name}(...) takes a predicate over {FriendlyName(parameterType)}, but the query's rows are " +
                            $"{FriendlyName(entityType)}. Apply {name} before converting the element type " +
                            "(IQueryable<…>/Cast<…>), " + advice);
                    }
                }

                if (name == "Select")
                {
                    var lambda = StripQuotes(call.Arguments[1]);
                    if (IsScalarSelect(lambda))
                    {
                        rowType = lambda.Body.Type;
                        afterNonSubsetSelect = true;
                    }
                    else if (!(lambda.Body is MemberInitExpression memberInit && memberInit.Type == entityType))
                    {
                        afterNonSubsetSelect = true;
                        afterOtherSelect = true;
                    }
                }
            }
        }

        private static bool IsSupportedShape(MethodInfo method)
        {
            if (!method.IsGenericMethodDefinition || !OperatorNames.Contains(method.Name))
                return false;
            var parameters = method.GetParameters();
            switch (method.Name)
            {
                case "Cast":
                case "OfType":
                case "Distinct":
                    return parameters.Length == 1;
                case "Skip":
                case "Take":
                    return parameters.Length == 2 && parameters[1].ParameterType == typeof(int);
            }
            if (parameters.Length == 1)
                return ParameterlessOperators.Contains(method.Name);
            return parameters.Length == 2 && IsUnaryLambdaType(parameters[1].ParameterType);
        }

        // Expression<Func<TSource, TResult>>: exactly one lambda parameter (rules out indexed overloads).
        private static bool IsUnaryLambdaType(Type type)
        {
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Expression<>))
                return false;
            var delegateType = type.GetGenericArguments()[0];
            return delegateType.IsGenericType && delegateType.GetGenericTypeDefinition() == typeof(Func<,>);
        }

        // The parse loop's scalar projection: Select(x => x.Member) over a writable property of the lambda parameter.
        private static bool IsScalarSelect(LambdaExpression lambda) =>
            lambda.Body is MemberExpression member
            && member.Expression is ParameterExpression
            && member.Member is PropertyInfo property
            && property.CanWrite;

        private static bool IsFirstOrSingle(string name) =>
            name == "First" || name == "FirstOrDefault" || name == "Single" || name == "SingleOrDefault";

        private static LambdaExpression StripQuotes(Expression expression)
        {
            while (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Quote)
                expression = unary.Operand;
            return (LambdaExpression)expression;
        }

        private static NotSupportedException Rejected(MethodInfo method)
        {
            if (method.DeclaringType == typeof(Queryable) && method.Name == "GroupBy")
                return new NotSupportedException(
                    "GroupBy is not supported in this version — it is not translated to SQL. Materialize " +
                    "first and group in memory: query.ToList().GroupBy(...).");
            return new NotSupportedException(
                $"{method.Name}(...) is not translated to SQL in this version. Materialize first and apply it in memory: " +
                $"query.ToList().{method.Name}(...).");
        }

        private static NotSupportedException AfterPaging(string name) =>
            new NotSupportedException(
                $"{name}(...) after Skip/Take is not translated to SQL in this version. Apply it before Skip/Take, or " +
                $"materialize the page first: query.Skip(n).Take(k).ToList().{name}(...).");

        private static string FriendlyName(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
                return FriendlyName(underlying) + "?";
            if (!type.IsGenericType)
                return type.Name;
            var name = type.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0)
                name = name.Substring(0, tick);
            return name + "<" + string.Join(", ", type.GetGenericArguments().Select(FriendlyName)) + ">";
        }
    }
}
