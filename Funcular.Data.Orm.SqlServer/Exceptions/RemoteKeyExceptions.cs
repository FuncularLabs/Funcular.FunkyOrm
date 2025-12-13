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
}
