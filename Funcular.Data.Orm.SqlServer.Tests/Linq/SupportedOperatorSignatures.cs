// Shared by three test projects (linked): SqlServer.Tests (net8, MSTest), SqlServer.Tests.NetFramework (net48,
// MSTest, C# 7.3) and SqlServer.Tests.DotNet9 (net9, xUnit). Keep it C# 7.3-compatible.
#if NET5_0_OR_GREATER
#nullable disable
#endif
using System;
using System.Linq;
using System.Reflection;

namespace Funcular.Data.Orm.Tests.Shared
{
    /// <summary>
    /// The literal, pinned set of <see cref="Queryable"/> overloads FunkyORM 3.10.0 translates (AC13-7), written
    /// out by hand from the plan's §5.2 rules: the allowed operator names; one <c>Expression&lt;Func&lt;TSource, …&gt;&gt;</c>
    /// lambda; <c>Skip</c>/<c>Take</c> take <c>Int32</c>; no comparer, default-value or <c>Range</c> parameters;
    /// generic overloads only; <c>Min</c>/<c>Max</c> only with a selector. Not computed from the runtime, so a
    /// newer runtime's <c>Queryable</c> can't widen it silently.
    /// </summary>
    internal static class SupportedOperatorSignatures
    {
        public static readonly string[] All =
        {
            "Where<TSource>(IQueryable<TSource>, Expression<Func<TSource, Boolean>>)",
            "Select<TSource, TResult>(IQueryable<TSource>, Expression<Func<TSource, TResult>>)",
            "OrderBy<TSource, TKey>(IQueryable<TSource>, Expression<Func<TSource, TKey>>)",
            "OrderByDescending<TSource, TKey>(IQueryable<TSource>, Expression<Func<TSource, TKey>>)",
            "ThenBy<TSource, TKey>(IOrderedQueryable<TSource>, Expression<Func<TSource, TKey>>)",
            "ThenByDescending<TSource, TKey>(IOrderedQueryable<TSource>, Expression<Func<TSource, TKey>>)",
            "Skip<TSource>(IQueryable<TSource>, Int32)",
            "Take<TSource>(IQueryable<TSource>, Int32)",
            "Distinct<TSource>(IQueryable<TSource>)",
            "First<TSource>(IQueryable<TSource>)",
            "First<TSource>(IQueryable<TSource>, Expression<Func<TSource, Boolean>>)",
            "FirstOrDefault<TSource>(IQueryable<TSource>)",
            "FirstOrDefault<TSource>(IQueryable<TSource>, Expression<Func<TSource, Boolean>>)",
            "Single<TSource>(IQueryable<TSource>)",
            "Single<TSource>(IQueryable<TSource>, Expression<Func<TSource, Boolean>>)",
            "SingleOrDefault<TSource>(IQueryable<TSource>)",
            "SingleOrDefault<TSource>(IQueryable<TSource>, Expression<Func<TSource, Boolean>>)",
            "Last<TSource>(IQueryable<TSource>)",
            "Last<TSource>(IQueryable<TSource>, Expression<Func<TSource, Boolean>>)",
            "LastOrDefault<TSource>(IQueryable<TSource>)",
            "LastOrDefault<TSource>(IQueryable<TSource>, Expression<Func<TSource, Boolean>>)",
            "Any<TSource>(IQueryable<TSource>)",
            "Any<TSource>(IQueryable<TSource>, Expression<Func<TSource, Boolean>>)",
            "All<TSource>(IQueryable<TSource>, Expression<Func<TSource, Boolean>>)",
            "Count<TSource>(IQueryable<TSource>)",
            "Count<TSource>(IQueryable<TSource>, Expression<Func<TSource, Boolean>>)",
            "LongCount<TSource>(IQueryable<TSource>)",
            "LongCount<TSource>(IQueryable<TSource>, Expression<Func<TSource, Boolean>>)",
            "Sum<TSource>(IQueryable<TSource>, Expression<Func<TSource, Int32>>)",
            "Sum<TSource>(IQueryable<TSource>, Expression<Func<TSource, Nullable<Int32>>>)",
            "Sum<TSource>(IQueryable<TSource>, Expression<Func<TSource, Int64>>)",
            "Sum<TSource>(IQueryable<TSource>, Expression<Func<TSource, Nullable<Int64>>>)",
            "Sum<TSource>(IQueryable<TSource>, Expression<Func<TSource, Single>>)",
            "Sum<TSource>(IQueryable<TSource>, Expression<Func<TSource, Nullable<Single>>>)",
            "Sum<TSource>(IQueryable<TSource>, Expression<Func<TSource, Double>>)",
            "Sum<TSource>(IQueryable<TSource>, Expression<Func<TSource, Nullable<Double>>>)",
            "Sum<TSource>(IQueryable<TSource>, Expression<Func<TSource, Decimal>>)",
            "Sum<TSource>(IQueryable<TSource>, Expression<Func<TSource, Nullable<Decimal>>>)",
            "Average<TSource>(IQueryable<TSource>, Expression<Func<TSource, Int32>>)",
            "Average<TSource>(IQueryable<TSource>, Expression<Func<TSource, Nullable<Int32>>>)",
            "Average<TSource>(IQueryable<TSource>, Expression<Func<TSource, Int64>>)",
            "Average<TSource>(IQueryable<TSource>, Expression<Func<TSource, Nullable<Int64>>>)",
            "Average<TSource>(IQueryable<TSource>, Expression<Func<TSource, Single>>)",
            "Average<TSource>(IQueryable<TSource>, Expression<Func<TSource, Nullable<Single>>>)",
            "Average<TSource>(IQueryable<TSource>, Expression<Func<TSource, Double>>)",
            "Average<TSource>(IQueryable<TSource>, Expression<Func<TSource, Nullable<Double>>>)",
            "Average<TSource>(IQueryable<TSource>, Expression<Func<TSource, Decimal>>)",
            "Average<TSource>(IQueryable<TSource>, Expression<Func<TSource, Nullable<Decimal>>>)",
            "Min<TSource, TResult>(IQueryable<TSource>, Expression<Func<TSource, TResult>>)",
            "Max<TSource, TResult>(IQueryable<TSource>, Expression<Func<TSource, TResult>>)",
            "Cast<TResult>(IQueryable)",
            "OfType<TResult>(IQueryable)",
        };

        /// <summary>The pinned size of <see cref="All"/>.</summary>
        public const int Count = 52;

        /// <summary>
        /// Renders a method as <c>Name&lt;TArgs&gt;(ParamTypes)</c> from its generic method definition, with short
        /// type names. Deterministic across runtimes, unlike <see cref="MethodInfo.ToString"/>.
        /// </summary>
        public static string Format(MethodInfo method)
        {
            var m = method.IsGenericMethod ? method.GetGenericMethodDefinition() : method;
            var generics = m.IsGenericMethodDefinition
                ? "<" + string.Join(", ", m.GetGenericArguments().Select(a => a.Name)) + ">"
                : "";
            return m.Name + generics + "(" + string.Join(", ", m.GetParameters().Select(p => FormatType(p.ParameterType))) + ")";
        }

        private static string FormatType(Type type)
        {
            if (type.IsGenericParameter)
                return type.Name;
            if (type.IsArray)
                return FormatType(type.GetElementType()) + "[]";
            if (type.IsGenericType)
            {
                var name = type.Name;
                var tick = name.IndexOf('`');
                if (tick >= 0)
                    name = name.Substring(0, tick);
                return name + "<" + string.Join(", ", type.GetGenericArguments().Select(FormatType)) + ">";
            }
            return type.Name;
        }
    }
}
