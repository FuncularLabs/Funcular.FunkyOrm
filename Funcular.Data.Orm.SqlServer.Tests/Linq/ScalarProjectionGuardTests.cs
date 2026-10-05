using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Funcular.Data.Orm.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests.Linq
{
    /// <summary>
    /// DB-free, direct tests of <see cref="ScalarProjectionGuard"/> (I2): a scalar projection supports collections
    /// only, decided by the expression's shape, never by the requested result type.
    /// </summary>
    [TestClass]
    public class ScalarProjectionGuardTests
    {
        private static readonly IQueryable<string> Names = new[] { "a", "b" }.AsQueryable();

        [TestMethod]
        public void ScalarProjectionGuard_Terminal_Throws()
        {
            Expression first = Expression.Call(typeof(Queryable), nameof(Queryable.First), new[] { typeof(string) }, Names.Expression);
            Expression firstOfObject = Expression.Call(typeof(Queryable), nameof(Queryable.First), new[] { typeof(object) },
                Names.Cast<object>().Expression);

            // Result types a List<string> would satisfy by assignability must still be rejected for a terminal.
            Assert.ThrowsException<NotSupportedException>(() =>
                ScalarProjectionGuard.EnsureCollectionResult(first, typeof(object), typeof(string)), "First -> object");
            Assert.ThrowsException<NotSupportedException>(() =>
                ScalarProjectionGuard.EnsureCollectionResult(first, typeof(string), typeof(string)), "First -> string");
            Assert.ThrowsException<NotSupportedException>(() =>
                ScalarProjectionGuard.EnsureCollectionResult(firstOfObject, typeof(object), typeof(string)), "First<object>");
        }

        [TestMethod]
        public void ScalarProjectionGuard_Collection_Passes()
        {
            Expression select = Names.Select(s => s).Expression;

            ScalarProjectionGuard.EnsureCollectionResult(select, typeof(IEnumerable<string>), typeof(string));
            ScalarProjectionGuard.EnsureCollectionResult(select, typeof(IEnumerable<object>), typeof(string));
            ScalarProjectionGuard.EnsureCollectionResult(Names.Expression, typeof(List<string>), typeof(string));
        }

        [TestMethod]
        public void ScalarProjectionGuard_NullArgument_ThrowsArgumentNull()
        {
            var ex = Assert.ThrowsException<ArgumentNullException>(() =>
                ScalarProjectionGuard.EnsureCollectionResult(null, typeof(List<string>), typeof(string)));
            Assert.AreEqual("expression", ex.ParamName);
            ex = Assert.ThrowsException<ArgumentNullException>(() =>
                ScalarProjectionGuard.EnsureCollectionResult(Names.Expression, null, typeof(string)));
            Assert.AreEqual("resultType", ex.ParamName);
            ex = Assert.ThrowsException<ArgumentNullException>(() =>
                ScalarProjectionGuard.EnsureCollectionResult(Names.Expression, typeof(List<string>), null));
            Assert.AreEqual("memberType", ex.ParamName);
        }
    }
}
