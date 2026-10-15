using System;
using System.Globalization;

namespace Funcular.Data.Orm.Linq
{
    /// <summary>
    /// LINQ to objects' rules for <c>Sum</c>, <c>Average</c>, <c>Min</c> and <c>Max</c>, which the four providers' LINQ
    /// query providers share (3.10.2, FD-04; docs/plans/QUERY_OPERATOR_CORRECTNESS_PLAN.md §10):
    /// <list type="bullet">
    /// <item><see cref="EnsureMinMaxSupported"/>: the selector types the database can't order as .NET does are rejected
    /// before any command is sent (an enum, <see cref="TimeSpan"/>, a <see cref="Guid"/> unless the provider's
    /// <see cref="QueryOptions.GuidOrdering"/> opts in, a <see cref="DateTimeOffset"/> on a provider without an
    /// offset-aware type).</item>
    /// <item><see cref="Average"/>: computed as <c>SUM / COUNT</c> in C# from the two values the provider reads, in the
    /// arithmetic LINQ uses for the selector's type (<c>long</c> sums for integers, <c>double</c> for <c>float</c> and
    /// <c>double</c>, <c>decimal</c> for <c>decimal</c>), so nothing is truncated or rounded by the database.</item>
    /// <item><see cref="ConvertResult"/>: an empty set gives <c>Sum</c> 0 (a nullable selector too), a nullable or
    /// reference <c>Min</c>/<c>Max</c> <c>null</c>, and a non-nullable one <see cref="InvalidOperationException"/>, as
    /// LINQ does; a value is converted to the operator's result type.</item>
    /// </list>
    /// </summary>
    public static class AggregateSemantics
    {
        /// <summary>
        /// Throws <see cref="NotSupportedException"/> when <c>Min</c>/<c>Max</c> over <paramref name="selectorType"/> can't
        /// be answered as .NET would answer it; returns otherwise.
        /// </summary>
        /// <param name="method"><c>Min</c> or <c>Max</c>.</param>
        /// <param name="selectorType">The selected property's type, nullable or not.</param>
        /// <param name="propertyName">The selected property's name, for the message.</param>
        /// <param name="guidOrdering">The provider's <see cref="QueryOptions.GuidOrdering"/>.</param>
        /// <param name="dateTimeOffsetOrdersByInstant">Whether the provider stores a <see cref="DateTimeOffset"/> in a type
        /// that orders by instant (SQL Server's <c>datetimeoffset</c>, PostgreSQL's <c>timestamptz</c>).</param>
        /// <param name="guidAggregatesSupported">Whether the database has <c>MIN</c>/<c>MAX</c> over its <see cref="Guid"/>
        /// type at all. PostgreSQL has none over <c>uuid</c> (error 42883), so <paramref name="guidOrdering"/> can't help
        /// there and a <see cref="Guid"/> is rejected whatever it says.</param>
        /// <exception cref="ArgumentNullException"><paramref name="selectorType"/> is null.</exception>
        /// <exception cref="NotSupportedException">The type isn't ordered as .NET orders it.</exception>
        public static void EnsureMinMaxSupported(string method, Type selectorType, string propertyName, GuidOrdering guidOrdering,
            bool dateTimeOffsetOrdersByInstant, bool guidAggregatesSupported = true)
        {
            if (selectorType == null)
                throw new ArgumentNullException(nameof(selectorType));
            var type = Nullable.GetUnderlyingType(selectorType) ?? selectorType;
            if (type == typeof(Guid) && !guidAggregatesSupported)
                throw new NotSupportedException(
                    $"{method} over the Guid property '{propertyName}' isn't supported on this provider: the database has no MIN/MAX over " +
                    $"its uuid type (PostgreSQL error 42883), so QueryOptions.GuidOrdering can't apply. Order by the property and read " +
                    $"the first row's value instead: query.OrderBy(x => x.{propertyName}).Take(1).Select(x => x.{propertyName}).ToList() " +
                    "(OrderByDescending for the maximum).");
            if (type.IsEnum)
                throw new NotSupportedException(
                    $"{method} over the enum property '{propertyName}' isn't supported: the database holds its number, so aggregate " +
                    $"that and convert the result: {method}(x => (int)x.{propertyName}).");
            if (type == typeof(TimeSpan))
                throw new NotSupportedException(
                    $"{method} over the TimeSpan property '{propertyName}' isn't supported: time types differ by provider (SQL Server " +
                    "and PostgreSQL time hold values under 24 hours, MySQL TIME spans ±838 hours, Npgsql maps TimeSpan to an " +
                    "unbounded interval). Store the duration as an integer (ticks, seconds or milliseconds) and aggregate that property.");
            if (type == typeof(Guid) && guidOrdering != GuidOrdering.Database)
                throw new NotSupportedException(
                    $"{method} over the Guid property '{propertyName}' isn't supported by default: the database's ordering of a Guid " +
                    "differs from .NET's Guid.CompareTo (SQL Server compares byte groups 10-15 first; MySQL and SQLite depend on the " +
                    "storage format). To take the database's ordering, set QueryOptions.GuidOrdering = GuidOrdering.Database on the provider.");
            if (type == typeof(DateTimeOffset) && !dateTimeOffsetOrdersByInstant)
                throw new NotSupportedException(
                    $"{method} over the DateTimeOffset property '{propertyName}' isn't supported on this provider: it has no offset-aware " +
                    "type, so stored values don't order by instant. Store a UTC DateTime instead.");
        }

        /// <summary>
        /// Converts the scalar a <c>Min</c>, <c>Max</c> or <c>Sum</c> read to <paramref name="resultType"/>, with LINQ's answers
        /// for an empty set: <c>Sum</c> gives 0, a nullable or reference <c>Min</c>/<c>Max</c> gives null, a non-nullable one
        /// throws <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <param name="method"><c>Min</c>, <c>Max</c> or <c>Sum</c>.</param>
        /// <param name="raw">The value the database returned; null or <see cref="DBNull"/> for an empty set.</param>
        /// <param name="resultType">The operator's result type (<c>TResult</c>), nullable or not.</param>
        /// <exception cref="ArgumentNullException"><paramref name="resultType"/> is null.</exception>
        /// <exception cref="InvalidOperationException">A non-nullable <c>Min</c>/<c>Max</c> over no elements.</exception>
        public static object ConvertResult(string method, object raw, Type resultType)
        {
            if (resultType == null)
                throw new ArgumentNullException(nameof(resultType));
            var target = Nullable.GetUnderlyingType(resultType) ?? resultType;
            var nullableResult = resultType != target || !resultType.IsValueType;
            if (raw == null || raw is DBNull)
            {
                if (method == "Sum")
                    return Convert.ChangeType(0, target, CultureInfo.InvariantCulture);
                if (nullableResult)
                    return null;
                throw new InvalidOperationException($"Sequence contains no elements for {method}.");
            }
            if (target == typeof(Guid))
            {
                if (raw is Guid guid) return guid;
                if (raw is byte[] bytes) return new Guid(bytes);
                return Guid.Parse(Convert.ToString(raw, CultureInfo.InvariantCulture));
            }
            if (target == typeof(DateTimeOffset))
            {
                if (raw is DateTimeOffset offset) return offset;
                if (raw is DateTime instant)
                    return new DateTimeOffset(instant.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(instant, DateTimeKind.Utc) : instant);
                return DateTimeOffset.Parse(Convert.ToString(raw, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            }
            if (target == typeof(DateTime))
                return raw is DateTime dateTime ? dateTime : Convert.ToDateTime(raw);
            if (target == typeof(string))
                return Convert.ToString(raw, CultureInfo.InvariantCulture);
            if (target.IsEnum)
                return Enum.ToObject(target, Convert.ChangeType(raw, Enum.GetUnderlyingType(target), CultureInfo.InvariantCulture));
            return Convert.ChangeType(raw, target, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// LINQ's <c>Average</c> from the <c>SUM</c> and <c>COUNT</c> a provider reads: integers are summed as <c>long</c>
        /// and divided as <c>double</c>, <c>float</c> and <c>double</c> as <c>double</c> (a <c>float</c> result is then
        /// narrowed), <c>decimal</c> as <c>decimal</c>. An empty set gives null for a nullable result and
        /// <see cref="InvalidOperationException"/> otherwise.
        /// </summary>
        /// <param name="sum">The <c>SUM</c> the database returned; null or <see cref="DBNull"/> when no value was summed.</param>
        /// <param name="count">The <c>COUNT</c> of the summed values.</param>
        /// <param name="selectorType">The selected property's type, nullable or not.</param>
        /// <param name="resultType">The operator's result type (<c>TResult</c>), nullable or not.</param>
        /// <exception cref="ArgumentNullException"><paramref name="selectorType"/> or <paramref name="resultType"/> is null.</exception>
        /// <exception cref="InvalidOperationException">A non-nullable <c>Average</c> over no elements.</exception>
        public static object Average(object sum, long count, Type selectorType, Type resultType)
        {
            if (selectorType == null)
                throw new ArgumentNullException(nameof(selectorType));
            if (resultType == null)
                throw new ArgumentNullException(nameof(resultType));
            var selector = Nullable.GetUnderlyingType(selectorType) ?? selectorType;
            var target = Nullable.GetUnderlyingType(resultType) ?? resultType;
            if (count == 0 || sum == null || sum is DBNull)
            {
                if (resultType != target)
                    return null;
                throw new InvalidOperationException("Sequence contains no elements for Average.");
            }

            object average;
            if (selector == typeof(decimal))
                average = Convert.ToDecimal(sum, CultureInfo.InvariantCulture) / count;
            else if (selector == typeof(float))
                average = (float)(Convert.ToDouble(sum, CultureInfo.InvariantCulture) / count);
            else if (selector == typeof(double))
                average = Convert.ToDouble(sum, CultureInfo.InvariantCulture) / count;
            else
                average = (double)Convert.ToInt64(sum, CultureInfo.InvariantCulture) / count;
            return Convert.ChangeType(average, target, CultureInfo.InvariantCulture);
        }
    }
}
