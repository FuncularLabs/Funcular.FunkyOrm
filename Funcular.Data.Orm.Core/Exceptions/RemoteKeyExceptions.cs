// Since 3.14.0 these types live in Core, once, and every provider assembly forwards them (TypeForwards.cs).
// Before that each bundled provider declared its own copy, so a consumer could not name them: CS0433.
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
