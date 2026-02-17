using System;

namespace Funcular.Data.Orm.Exceptions
{
    public class PathNotFoundException : Exception
    {
        public PathNotFoundException(string message) : base(message) { }
    }

    public class AmbiguousMatchException : Exception
    {
        public AmbiguousMatchException(string message) : base(message) { }
    }

    /// <summary>
    /// Thrown when a LINQ expression uses .Value, .HasValue, or an incompatible
    /// collection type on a nullable entity property. FunkyORM automatically
    /// unwraps nullable types during SQL translation; using these members
    /// produces invalid SQL column references.
    /// </summary>
    public class NullableExpressionException : InvalidOperationException
    {
        public NullableExpressionException(string message) : base(message) { }
    }
}
