using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Funcular.Data.Orm;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    /// <summary>
    /// Phase 0 (no database): the Core audit-context plumbing — option gating, fail-closed vs opportunistic
    /// resolution, system-context bypass, and the validated self-attributing audit comment.
    /// </summary>
    [TestClass]
    public class AuditContextCoreTests
    {
        private sealed class StubAccessor : IAuditContextAccessor
        {
            public FunkyAuditContext? Current { get; set; }
        }

        private static StubProvider New(out StubAccessor accessor, bool require = false, bool emitComment = true, bool enabled = true)
        {
            accessor = new StubAccessor();
            var p = new StubProvider();
            p.AuditContext = new AuditContextOptions
            {
                Accessor = enabled ? accessor : null,
                RequireAuditContext = require,
                EmitAuditComment = emitComment
            };
            return p;
        }

        private static FunkyAuditContext Ctx(string? sub = null, string? corr = null, params SessionContextEntry[] entries)
            => new FunkyAuditContext { Entries = entries, AuditSubjectId = sub, AuditCorrelationId = corr };

        // ── Resolution / fail-closed ──

        [TestMethod]
        public void DisabledByDefault_ResolvesAndCommentNull()
        {
            var p = New(out _, enabled: false);
            Assert.IsNull(p.Resolve());
            Assert.IsNull(p.Comment());
        }

        [TestMethod]
        public void Enabled_WithContext_ResolvesIt()
        {
            var p = New(out var acc);
            acc.Current = Ctx(entries: new SessionContextEntry("UserId", "abc"));
            var resolved = p.Resolve();
            Assert.IsNotNull(resolved);
            Assert.AreEqual(1, resolved!.Entries.Count);
            Assert.AreEqual("UserId", resolved.Entries[0].Key);
        }

        [TestMethod]
        public void Required_NoContext_Throws()
        {
            var p = New(out _, require: true);
            Assert.ThrowsException<InvalidOperationException>(() => p.Resolve());
        }

        [TestMethod]
        public void Lenient_NoContext_ReturnsNull()
        {
            var p = New(out _, require: false);
            Assert.IsNull(p.Resolve());
        }

        [TestMethod]
        public void SystemScope_BypassesRequireAndComment()
        {
            var p = New(out var acc, require: true);
            acc.Current = Ctx(sub: "abc"); // present, but system scope should ignore it
            using (SystemContextScope.Enter())
            {
                Assert.IsNull(p.Resolve());   // no throw despite require=true
                Assert.IsNull(p.Comment());
            }
            // outside the scope, behavior resumes
            Assert.IsNotNull(p.Resolve());
        }

        [TestMethod]
        public void SystemScope_Nested_StaysActiveUntilOutermostDisposed()
        {
            Assert.IsFalse(SystemContextScope.IsActive);
            using (SystemContextScope.Enter())
            {
                using (SystemContextScope.Enter())
                    Assert.IsTrue(SystemContextScope.IsActive);
                Assert.IsTrue(SystemContextScope.IsActive); // inner dispose didn't clear
            }
            Assert.IsFalse(SystemContextScope.IsActive);
        }

        // ── Audit comment ──

        [TestMethod]
        public void Comment_SubjectAndCorrelation()
        {
            var p = New(out var acc);
            acc.Current = Ctx(sub: "0a1b2c3d-4e5f", corr: "0HN7:0001");
            Assert.AreEqual("/* funky:audit sub=0a1b2c3d-4e5f corr=0HN7:0001 */", p.Comment());
        }

        [TestMethod]
        public void Comment_SubjectOnly()
        {
            var p = New(out var acc);
            acc.Current = Ctx(sub: "abc123");
            Assert.AreEqual("/* funky:audit sub=abc123 */", p.Comment());
        }

        [TestMethod]
        public void Comment_NoIdentifiers_Null()
        {
            var p = New(out var acc);
            acc.Current = Ctx(entries: new SessionContextEntry("UserId", "abc"));
            Assert.IsNull(p.Comment());
        }

        [TestMethod]
        public void Comment_Disabled_Null()
        {
            var p = New(out var acc, emitComment: false);
            acc.Current = Ctx(sub: "abc");
            Assert.IsNull(p.Comment());
        }

        [TestMethod]
        public void Comment_UnsafeIdentifier_Throws()
        {
            var p = New(out var acc);
            acc.Current = Ctx(sub: "abc */ DROP"); // contains space + */
            Assert.ThrowsException<InvalidOperationException>(() => p.Comment());
        }

        [TestMethod]
        public void Comment_SlashIdentifier_Throws()
        {
            var p = New(out var acc);
            acc.Current = Ctx(corr: "a/b"); // '/' disallowed
            Assert.ThrowsException<InvalidOperationException>(() => p.Comment());
        }

        // ── SessionContextEntry ──

        [TestMethod]
        public void SessionContextEntry_Defaults_ReadOnly()
        {
            var e = new SessionContextEntry("k", "v");
            Assert.IsTrue(e.ReadOnly);
        }

        [TestMethod]
        public void SessionContextEntry_NullKeyOrValue_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new SessionContextEntry(null!, "v"));
            Assert.ThrowsException<ArgumentNullException>(() => new SessionContextEntry("k", null!));
        }

        /// <summary>Minimal stub exposing the protected audit helpers; CRUD abstracts are not under test.</summary>
        private sealed class StubProvider : OrmDataProvider
        {
            public FunkyAuditContext? Resolve() => ResolveAuditContextForPriming();
            public string? Comment() => GetAuditCommentPrefix();

            public override T Get<T>(dynamic key = null!) => throw new NotImplementedException();
            public override IQueryable<T> Query<T>() => throw new NotImplementedException();
            public override ICollection<T> Query<T>(Expression<Func<T, bool>> expression) => throw new NotImplementedException();
            public override ICollection<T> GetList<T>() => throw new NotImplementedException();
            public override object Insert<T>(T entity) => throw new NotImplementedException();
            public override TKey Insert<T, TKey>(T entity) => throw new NotImplementedException();
            public override T Update<T>(T entity) => throw new NotImplementedException();
            public override Task<T> GetAsync<T>(dynamic key = null!) => throw new NotImplementedException();
            public override Task<ICollection<T>> QueryAsync<T>(Expression<Func<T, bool>> expression) => throw new NotImplementedException();
            public override Task<ICollection<T>> GetListAsync<T>() => throw new NotImplementedException();
            public override Task<object> InsertAsync<T>(T entity) => throw new NotImplementedException();
            public override Task<TKey> InsertAsync<T, TKey>(T entity) => throw new NotImplementedException();
            public override Task<T> UpdateAsync<T>(T entity) => throw new NotImplementedException();
            public override Task<int> DeleteAsync<T>(Expression<Func<T, bool>> predicate) => throw new NotImplementedException();
            public override int Delete<T>(Expression<Func<T, bool>> predicate) => throw new NotImplementedException();
            public override bool Delete<T>(long id) => throw new NotImplementedException();
            public override void Dispose() { }
        }
    }
}
