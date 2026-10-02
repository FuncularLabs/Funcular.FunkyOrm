using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests.Caching
{
    /// <summary>
    /// The scope's own types, each member called on purpose (provider-scoped caches plan, §4.2; review HRB-8): the
    /// mapped-type set's collection members and the registry key's equality. DB-free.
    /// </summary>
    [TestClass]
    public class CacheScopeTypesTests
    {
        [TestMethod]
        public void MappedTypeSet_CollectionMembers_BehaveAsASet()
        {
            ICollection<Type> set = new ConcurrentTypeSet();
            Assert.IsFalse(set.IsReadOnly);

            set.Add(typeof(string));
            set.Add(typeof(int));
            set.Add(typeof(string));
            Assert.AreEqual(2, set.Count, "a set holds each type once");
            Assert.IsTrue(set.Contains(typeof(int)));
            Assert.IsFalse(set.Contains(null));

            var copy = new Type[3];
            set.CopyTo(copy, 1);
            Assert.IsNull(copy[0], "CopyTo starts at the index given");
            CollectionAssert.AreEquivalent(new[] { typeof(string), typeof(int) }, copy.Skip(1).ToArray());

            CollectionAssert.AreEquivalent(new[] { typeof(string), typeof(int) }, set.ToList());
            var nonGeneric = new List<object>();
            foreach (var item in (IEnumerable)set)
                nonGeneric.Add(item);
            CollectionAssert.AreEquivalent(new object[] { typeof(string), typeof(int) }, nonGeneric);

            Assert.IsTrue(set.Remove(typeof(int)));
            Assert.IsFalse(set.Remove(typeof(int)));
            Assert.IsFalse(set.Remove(null));
            set.Clear();
            Assert.AreEqual(0, set.Count);
        }

        [TestMethod]
        public void CacheScopeKey_Equality_UsesAllThreeParts()
        {
            var key = new CacheScopeKey(typeof(string), typeof(int), "abc");
            var same = new CacheScopeKey(typeof(string), typeof(int), "abc");

            Assert.IsTrue(key.Equals((object)same));
            Assert.AreEqual(key.GetHashCode(), same.GetHashCode());
            Assert.IsFalse(key.Equals((object)new CacheScopeKey(typeof(object), typeof(int), "abc")), "provider type");
            Assert.IsFalse(key.Equals((object)new CacheScopeKey(typeof(string), null, "abc")), "dialect type");
            Assert.IsFalse(key.Equals((object)new CacheScopeKey(typeof(string), typeof(int), "ABC")), "identity hash, ordinal");
            Assert.IsFalse(key.Equals((object)"abc"), "another type of object");
            Assert.AreEqual($"{typeof(string).FullName}|{typeof(int).FullName}|abc", key.ToString());
        }
    }
}
