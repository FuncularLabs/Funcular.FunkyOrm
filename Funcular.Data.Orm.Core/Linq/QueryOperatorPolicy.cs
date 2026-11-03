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
    /// <c>OrderBy</c>; <c>Cast</c>/<c>OfType</c> against the row type; predicate lambdas that don't bind to the
    /// entity; and, after a column-subset <c>Select(x =&gt; new T { … })</c>, a lambda that reads a member of its
    /// parameter the projection did not copy from itself (<c>M = x.M</c>): LINQ would see the projected value, SQL
    /// would read the base column, so the read is rejected before any query (3.14.0); and a <c>Select</c> after
    /// <c>Distinct()</c>, because SQL applies DISTINCT to the final column list where LINQ makes the rows distinct
    /// first (3.14.0). The row type starts as the entity type and changes only at a scalar
    /// <c>Select(x =&gt; x.Member)</c>; after any other unsupported
    /// <c>Select</c> it is unknown, and the parse loop rejects that <c>Select</c>.</para>
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
            HashSet<string> boundMembers = null; // after a column-subset Select: the members it copied from itself
            var distinctSeen = false;            // a Select after Distinct would project before DISTINCT (3.14.0)
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
                            $"query.{name}(later).ThenBy(earlier), keeping each earlier key's direction (ThenByDescending for " +
                            "a descending one).");
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

                // S (3.14.0): after a column-subset Select, a lambda may read only the members the projection copied
                // from itself. Checked after D8/D10/I1 so their messages keep precedence, and before the Select arm so
                // a later Select's own lambda is judged against the earlier projection.
                if (boundMembers != null && TryGetLambda(call, out var lambdaAfterSubset))
                {
                    var unbound = FirstUnboundRead(lambdaAfterSubset, boundMembers);
                    if (unbound != null)
                        throw AfterSubsetSelect(name, unbound);
                }

                if (name == "Distinct")
                    distinctSeen = true;

                if (name == "Select")
                {
                    // SQL applies DISTINCT to the final SELECT list wherever Distinct sits in the chain, so a Select after
                    // Distinct makes the projection distinct where LINQ makes the rows distinct first: a different answer.
                    if (distinctSeen)
                        throw new NotSupportedException(
                            "Select(...) after Distinct() is not translated: SQL would apply DISTINCT to the projected columns, " +
                            "where LINQ makes the rows distinct first, so the results would differ. For distinct projections " +
                            "apply Distinct after the Select: query.Select(...).Distinct(); to keep LINQ's result, materialize " +
                            "first: query.Distinct().ToList().Select(...).");
                    var lambda = StripQuotes(call.Arguments[1]);
                    if (IsScalarSelect(lambda))
                    {
                        rowType = lambda.Body.Type;
                        afterNonSubsetSelect = true;
                        boundMembers = null;
                    }
                    else if (!(lambda.Body is MemberInitExpression memberInit && memberInit.Type == entityType))
                    {
                        afterNonSubsetSelect = true;
                        afterOtherSelect = true;
                        boundMembers = null;
                    }
                    else
                    {
                        boundMembers = IdentityBoundMembers(memberInit, lambda.Parameters[0]);
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

        /// <summary>The quoted unary lambda of an operator such as Where/OrderBy/Select/Sum; false for Skip/Take/Distinct/Cast.</summary>
        private static bool TryGetLambda(MethodCallExpression call, out LambdaExpression lambda)
        {
            lambda = null;
            if (call.Arguments.Count != 2)
                return false;
            var argument = call.Arguments[1];
            while (argument is UnaryExpression unary && unary.NodeType == ExpressionType.Quote)
                argument = unary.Operand;
            lambda = argument as LambdaExpression;
            return lambda != null && lambda.Parameters.Count == 1;
        }

        private static Expression Unwrap(Expression expression)
        {
            while (expression is UnaryExpression unary
                   && (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked
                       || unary.NodeType == ExpressionType.TypeAs))
                expression = unary.Operand;
            return expression;
        }

        /// <summary>
        /// The members a column-subset projection copies from itself: a <see cref="MemberAssignment"/> whose value,
        /// after unwrapping a cast, is the same-named member of the lambda's own parameter. A binding from another member,
        /// from a constant or a captured object, or a list/member binding, leaves its member unbound; names are compared,
        /// so a member read through a base-class or interface view of the row still matches.
        /// </summary>
        private static HashSet<string> IdentityBoundMembers(MemberInitExpression init, ParameterExpression parameter)
        {
            var bound = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in init.Bindings)
            {
                if (!(binding is MemberAssignment assignment))
                    continue;
                if (Unwrap(assignment.Expression) is MemberExpression value
                    && Unwrap(value.Expression) == parameter
                    && value.Member.Name == assignment.Member.Name)
                    bound.Add(assignment.Member.Name);
            }
            return bound;
        }

        /// <summary>The first member of the lambda's own parameter that <paramref name="lambda"/> reads and <paramref name="bound"/> lacks, or null.</summary>
        private static string FirstUnboundRead(LambdaExpression lambda, HashSet<string> bound)
        {
            var finder = new UnboundReadFinder(lambda.Parameters[0], bound);
            finder.Visit(lambda.Body);
            return finder.Found;
        }

        private sealed class UnboundReadFinder : ExpressionVisitor
        {
            private readonly ParameterExpression _parameter;
            private readonly HashSet<string> _bound;

            public UnboundReadFinder(ParameterExpression parameter, HashSet<string> bound)
            {
                _parameter = parameter;
                _bound = bound;
            }

            public string Found { get; private set; }

            protected override Expression VisitMember(MemberExpression node)
            {
                // Only reads of THIS lambda's parameter count: a nested lambda's own parameter, or a captured object of
                // the row type, is not a row read. A cast of the parameter is still the parameter.
                if (Found == null && node.Expression != null && Unwrap(node.Expression) == _parameter
                    && !_bound.Contains(node.Member.Name))
                    Found = node.Member.Name;
                return Found != null ? node : base.VisitMember(node);
            }
        }

        private static NotSupportedException AfterSubsetSelect(string name, string member) =>
            new NotSupportedException(
                $"{name}(...) after a column-subset Select reads {member}, which the Select did not copy from itself. LINQ " +
                "would see the projected value; SQL would read the base column; the results would differ. Apply " +
                $"{name}(...) before the Select, or project {member} = x.{member}.");

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
