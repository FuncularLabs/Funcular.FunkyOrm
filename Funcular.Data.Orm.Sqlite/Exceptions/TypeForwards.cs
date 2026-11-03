// The three exception types moved to Funcular.Data.Orm.Core in 3.14.0 (docs/plans/RELEASE_3.14.0_PLAN.md, stream E).
// These forwarders keep a reference compiled against this assembly's TypeRef resolving; the namespace is unchanged.
using System.Runtime.CompilerServices;

[assembly: TypeForwardedTo(typeof(Funcular.Data.Orm.Exceptions.PathNotFoundException))]
[assembly: TypeForwardedTo(typeof(Funcular.Data.Orm.Exceptions.AmbiguousMatchException))]
[assembly: TypeForwardedTo(typeof(Funcular.Data.Orm.Exceptions.NullableExpressionException))]
