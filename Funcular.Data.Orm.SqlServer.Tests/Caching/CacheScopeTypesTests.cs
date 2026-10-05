using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests.Caching
{
    /// <summary>
    /// The scope's own types, each member called on purpose (provider-scoped caches plan, §4.2; review HRB-8): the
    /// mapped-type set's collection members and the registry key's equality; from review HRA-4 and HRA-5, copying the
    /// set while it grows and reading a resolved scope; and, from review FVA-2, racing first reads of a scope. DB-free.
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

        /// <summary>
        /// Review HRA-4: <c>ToArray</c>, <c>ToList</c> and <c>new List&lt;Type&gt;(set)</c> size their array from
        /// <c>Count</c> and then call <c>CopyTo</c>; copying the set while another thread adds to it never throws.
        /// </summary>
        [TestMethod]
        public void MappedTypeSet_CopiedWhileAnotherThreadAdds_NeverThrows()
        {
            var types = typeof(object).Assembly.GetTypes();
            var failures = new ConcurrentQueue<Exception>();
            var deadline = DateTime.UtcNow.AddSeconds(2);
            var rounds = 0;
            while (failures.IsEmpty && DateTime.UtcNow < deadline)
            {
                ICollection<Type> set = new ConcurrentTypeSet();
                var done = 0;
                var writer = Task.Run(() =>
                {
                    foreach (var type in types) set.Add(type);
                    Volatile.Write(ref done, 1);
                });
                var reader = Task.Run(() =>
                {
                    while (Volatile.Read(ref done) == 0)
                    {
                        try
                        {
                            _ = set.ToArray();
                            _ = set.ToList();
                            _ = new List<Type>(set);
                        }
                        catch (Exception ex)
                        {
                            failures.Enqueue(ex);
                            return;
                        }
                    }
                });
                Task.WaitAll(writer, reader);
                rounds++;
            }

            Assert.IsTrue(failures.IsEmpty, failures.IsEmpty ? null :
                $"copying the set while it grows threw in round {rounds}: {failures.First().GetType().Name}: {failures.First().Message}");
        }

        /// <summary>Review HRA-5: once an instance's scope is resolved, reading it again allocates nothing.</summary>
        [TestMethod]
        public void CacheScope_AfterTheFirstAccess_AllocatesNothing()
        {
            using var provider = new DirectProviderWithIdentity(null);
            var scope = provider.CacheScope;

            var before = GC.GetAllocatedBytesForCurrentThread();
            var same = 0;
            for (var i = 0; i < 1000; i++)
                if (ReferenceEquals(scope, provider.CacheScope)) same++;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(1000, same, "every read returns the resolved scope");
            Assert.AreEqual(0L, allocated, "1000 reads of the resolved scope allocated this many bytes");
        }

        /// <summary>
        /// D8 (review FVA-2): threads racing to make an instance's first scope read all get the one scope that is
        /// published. A per-instance (null-identity) scope is the shape where two runs of the factory yield different
        /// objects, so a publication that isn't atomic shows here.
        /// </summary>
        [TestMethod]
        public void CacheScope_RacingFirstReads_AllGetThePublishedScope()
        {
            const int threads = 8, rounds = 500;
            var providers = Enumerable.Range(0, rounds).Select(_ => new DirectProviderWithIdentity(null)).ToArray();
            try
            {
                var seen = new CacheScope[rounds, threads];
                var failures = new ConcurrentQueue<Exception>();
                using (var barrier = new Barrier(threads))
                {
                    var workers = Enumerable.Range(0, threads).Select(t => new Thread(() =>
                    {
                        for (var r = 0; r < rounds; r++)
                        {
                            barrier.SignalAndWait();
                            try { seen[r, t] = providers[r].CacheScope; }
                            catch (Exception ex) { failures.Enqueue(ex); }
                        }
                    })).ToList();
                    workers.ForEach(w => w.Start());
                    workers.ForEach(w => w.Join());
                }

                Assert.IsTrue(failures.IsEmpty, failures.IsEmpty ? null :
                    $"a racing first read threw: {failures.First().GetType().Name}: {failures.First().Message}");
                var mismatches = 0;
                for (var r = 0; r < rounds; r++)
                    for (var t = 0; t < threads; t++)
                        if (!ReferenceEquals(seen[r, t], providers[r].CacheScope)) mismatches++;
                Assert.AreEqual(0, mismatches,
                    $"of {rounds * threads} racing first reads, this many got a scope other than the one published");
            }
            finally
            {
                foreach (var provider in providers) provider.Dispose();
            }
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
