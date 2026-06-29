using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Funcular.Data.Orm;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    /// <summary>
    /// Phase 0 (no database): locks in the stored-procedure overload-resolution table (plan §1.3) and the
    /// <c>NormalizeParameters</c> guard behavior, by binding against a recording stub derived from
    /// <see cref="OrmDataProvider"/>. These convert compiler-behavior assumptions into regression-tested facts.
    /// </summary>
    [TestClass]
    public class StoredProcOverloadResolutionTests
    {
        private sealed class Foo { public int Id { get; set; } }

        private static RecordingProvider New() => new RecordingProvider();

        // ── Overload resolution: ExecProcedure ──

        [TestMethod]
        public void ExecProcedure_NoArgs_BindsConventionObject()
        {
            var p = New();
            p.ExecProcedure<Foo>();
            Assert.AreEqual("Proc(object)", p.LastOverload);
        }

        [TestMethod]
        public void ExecProcedure_AnonObject_BindsConventionObject()
        {
            var p = New();
            p.ExecProcedure<Foo>(new { a = 1 });
            Assert.AreEqual("Proc(object)", p.LastOverload);
        }

        [TestMethod]
        public void ExecProcedure_NameOnly_BindsStringObject()
        {
            var p = New();
            p.ExecProcedure<Foo>("proc");
            Assert.AreEqual("Proc(string,object)", p.LastOverload);
        }

        [TestMethod]
        public void ExecProcedure_NameAndAnon_BindsStringObject()
        {
            var p = New();
            p.ExecProcedure<Foo>("proc", new { a = 1 });
            Assert.AreEqual("Proc(string,object)", p.LastOverload);
        }

        [TestMethod]
        public void ExecProcedure_NameAndTuple_BindsStringSqlParams()
        {
            var p = New();
            p.ExecProcedure<Foo>("proc", ("@a", 1));
            Assert.AreEqual("Proc(string,SqlParam[])", p.LastOverload);
        }

        [TestMethod]
        public void ExecProcedure_NameAndNull_BindsStringSqlParams()
        {
            var p = New();
            p.ExecProcedure<Foo>("proc", null);
            Assert.AreEqual("Proc(string,SqlParam[])", p.LastOverload);
        }

        // ── Overload resolution: ExecScalar / ExecNonQuery ──

        [TestMethod]
        public void ExecScalar_NameAndAnon_BindsStringObject()
        {
            var p = New();
            p.ExecScalar<int>("proc", new { a = 1 });
            Assert.AreEqual("Scalar(string,object)", p.LastOverload);
        }

        [TestMethod]
        public void ExecScalar_NameAndTuple_BindsStringSqlParams()
        {
            var p = New();
            p.ExecScalar<int>("proc", ("@a", 1));
            Assert.AreEqual("Scalar(string,SqlParam[])", p.LastOverload);
        }

        [TestMethod]
        public void ExecNonQuery_NameAndAnon_BindsStringObject()
        {
            var p = New();
            p.ExecNonQuery("proc", new { a = 1 });
            Assert.AreEqual("NonQuery(string,object)", p.LastOverload);
        }

        [TestMethod]
        public void ExecNonQuery_NameAndTuple_BindsStringSqlParams()
        {
            var p = New();
            p.ExecNonQuery("proc", ("@a", 1));
            Assert.AreEqual("NonQuery(string,SqlParam[])", p.LastOverload);
        }

        // ── Implicit tuple → SqlParam conversion ──

        [TestMethod]
        public void Tuple_ConvertsImplicitlyToSqlParam()
        {
            SqlParam p = ("@gender", (object?)"Male");
            Assert.AreEqual("@gender", p.Name);
            Assert.AreEqual("Male", p.Value);
            Assert.AreEqual(ParameterDirection.Input, p.Direction);
        }

        // ── NormalizeParameters guards ──

        [TestMethod]
        public void Normalize_StringAsParametersObject_Throws()
        {
            var p = New();
            Assert.ThrowsException<ArgumentException>(() => p.NormalizeObject("a bare string"));
        }

        [TestMethod]
        public void Normalize_SqlParamAsObject_HandledNatively()
        {
            var p = New();
            var result = p.NormalizeObject(new SqlParam("@x", 5));
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("x", result[0].Name);
            Assert.AreEqual(5, result[0].Value);
        }

        [TestMethod]
        public void Normalize_SqlParamSequenceAsObject_HandledNatively()
        {
            var p = New();
            var result = p.NormalizeObject(new[] { new SqlParam("@x", 5), new SqlParam("@y", 6) });
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("x", result[0].Name);
            Assert.AreEqual("y", result[1].Name);
        }

        [TestMethod]
        public void Normalize_NullProperty_BecomesDbNull()
        {
            var p = New();
            var result = p.NormalizeObject(new { a = (string?)null });
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("a", result[0].Name);
            Assert.AreSame(DBNull.Value, result[0].Value);
        }

        [TestMethod]
        public void Normalize_SqlParam_StripsAtPrefix()
        {
            var p = New();
            var result = p.NormalizeParams(new[] { new SqlParam("@gender", "M") });
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("gender", result[0].Name);
        }

        [TestMethod]
        public void Normalize_NullOrEmpty_YieldsNoParameters()
        {
            var p = New();
            Assert.AreEqual(0, p.NormalizeObject(null).Length);
            Assert.AreEqual(0, p.NormalizeParams(null).Length);
            Assert.AreEqual(0, p.NormalizeParams(Array.Empty<SqlParam>()).Length);
        }

        /// <summary>
        /// Recording stub: overrides the sync Exec* overloads to capture which one bound, and exposes the
        /// protected NormalizeParameters helpers projected to primitive tuples (so the protected
        /// NormalizedParameter type is not leaked through a public signature). All CRUD abstracts throw.
        /// </summary>
        private sealed class RecordingProvider : OrmDataProvider
        {
            public string? LastOverload { get; private set; }

            // Exec* overrides record the bound overload.
            public override ICollection<T> ExecProcedure<T>(object? parameters = null) { LastOverload = "Proc(object)"; return new List<T>(); }
            public override ICollection<T> ExecProcedure<T>(string procedureName, object? parameters = null) { LastOverload = "Proc(string,object)"; return new List<T>(); }
            public override ICollection<T> ExecProcedure<T>(string procedureName, params SqlParam[] parameters) { LastOverload = "Proc(string,SqlParam[])"; return new List<T>(); }
            public override TResult ExecScalar<TResult>(string procedureName, object? parameters = null) { LastOverload = "Scalar(string,object)"; return default!; }
            public override TResult ExecScalar<TResult>(string procedureName, params SqlParam[] parameters) { LastOverload = "Scalar(string,SqlParam[])"; return default!; }
            public override int ExecNonQuery(string procedureName, object? parameters = null) { LastOverload = "NonQuery(string,object)"; return 0; }
            public override int ExecNonQuery(string procedureName, params SqlParam[] parameters) { LastOverload = "NonQuery(string,SqlParam[])"; return 0; }

            // Project protected NormalizeParameters output to primitive tuples for assertions.
            public (string Name, object? Value, ParameterDirection Direction)[] NormalizeObject(object? p)
                => NormalizeParameters(p).Select(n => (n.Name, n.Value, n.Direction)).ToArray();
            public (string Name, object? Value, ParameterDirection Direction)[] NormalizeParams(SqlParam[]? p)
                => NormalizeParameters(p).Select(n => (n.Name, n.Value, n.Direction)).ToArray();

            // CRUD abstracts — not under test.
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
