using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Funcular.Data.Orm.Attributes;

#pragma warning disable CS1718 // Comparisons of a member with itself are the point of these rows.

namespace Funcular.Data.Orm.Tests.DeleteGuard
{
    /// <summary>
    /// The integration rows of the delete-guard plan (docs/plans/DELETE_GUARD_PLAN.md, §4.1), run against each provider's
    /// Delete and DeleteAsync by predicate. One derived [TestClass] per provider supplies the DDL, the provider and its
    /// mapped-type accessor. This file is compiled into SqlServer.Tests (SQL Server, SQLite), PostgreSql.Tests and
    /// MySql.Tests.
    /// <para>
    /// Each row creates <c>zz_dg_row</c> and <c>zz_dg_TrueUp</c> (drop if exists, create, seed), runs each call in its own
    /// transaction and rolls it back, and drops both tables in <c>finally</c>. A rejected call must throw the expected
    /// message, log no DELETE, and leave every row, counted through the provider inside the transaction before the
    /// rollback.
    /// </para>
    /// </summary>
    public abstract class DeleteGuardHarness
    {
        public const string RowTable = "zz_dg_row";
        public const string TrueUpTable = "zz_dg_TrueUp";
        public const string MissingTable = "zz_dg_missing";

        public const string NoColumnMessage = "Delete operation WHERE clause must reference at least one column from the target table.";
        public const string SelfReferenceMessage = "Delete operation WHERE clause cannot be a self-referencing column expression.";
        public const string AlwaysTrueMessage = "Delete operation requires a non-trivial WHERE clause.";
        public const string NullPredicateMessage = "A WHERE clause (predicate) is required for deletes.";
        public const string TransactionMessage = "Delete operations must be performed within an active transaction.";

        /// <summary>Rows (1, 'a', 1), (2, 'b', 0), (3, NULL, 0).</summary>
        [Table(RowTable)]
        public class DgRow
        {
            [Key] public int Id { get; set; }
            public string FirstName { get; set; }
            public int TrueUp { get; set; }
            public bool Archived { get; set; }

            [SqlExpression("CASE WHEN {Id} * 100 > 50 THEN 1 ELSE 0 END")]
            public int Big { get; set; }

            [SqlExpression("CASE WHEN {Id} - 1 >= 1 THEN 1 ELSE 0 END")]
            public int Big2 { get; set; }
        }

        /// <summary>Rows (1, 5), (2, 7).</summary>
        [Table(TrueUpTable)]
        public class DgTrueUp
        {
            [Key] public int Id { get; set; }
            public int Amount { get; set; }
        }

        /// <summary>A type whose table never exists.</summary>
        [Table(MissingTable)]
        public class DgMissing
        {
            [Key] public int Id { get; set; }
            public string FirstName { get; set; }
        }

        public class Holder
        {
            public bool Flag => true;
        }

        public static class StaticFlags
        {
            public static bool On => true;
        }

        public class Request
        {
            public bool IncludeAll => true;
        }

        // Captured values, read through closures as a caller's locals would be.
        private readonly bool capturedTrue = true;
        private readonly int capturedA = 7;
        private readonly string capturedNull = null;
        private readonly Request request = new Request();
        private readonly List<int> ids = new List<int> { 1, 2 };
        private readonly List<int> otherIds = new List<int> { 1, 3 };
        private readonly List<int> emptyIds = new List<int>();
        private readonly int[] emptyArray = new int[0];
        private readonly int[] capturedArray = { 5 };
        private readonly bool? capturedNullBool = null;
        private readonly string capturedS = "s";
        private readonly string roles = "admin,user";
        private string FilterProperty { get; } = "a";
        private readonly string capturedUpperA = "A";

        #region Provider seam

        /// <summary>Marks the test inconclusive when the provider's test database is unavailable.</summary>
        protected virtual void RequireDatabase() { }

        /// <summary>Drops <c>zz_dg_row</c> and <c>zz_dg_TrueUp</c> if they exist, creates and seeds them.</summary>
        protected abstract void CreateTables();

        /// <summary>Drops <c>zz_dg_row</c> and <c>zz_dg_TrueUp</c> (or deletes the SQLite file).</summary>
        protected abstract void DropTables();

        /// <summary>Drops <c>zz_dg_missing</c> if it exists.</summary>
        protected abstract void DropMissingTable();

        protected abstract OrmDataProvider CreateProvider();

        /// <summary>Whether <paramref name="type"/> is in <paramref name="provider"/>'s mapped-type set.</summary>
        protected abstract bool IsDiscovered(OrmDataProvider provider, Type type);

        #endregion

        #region Predicates

        private Expression<Func<DgRow, bool>> Row(string key)
        {
            switch (key)
            {
                // Self-comparisons.
                case "firstNameSelf": return x => x.FirstName == x.FirstName;
                case "idGe": return x => x.Id >= x.Id;
                case "idLe": return x => x.Id <= x.Id;
                case "notNe": return x => !(x.Id != x.Id);
                case "longCast": return x => (long)x.Id == (long)x.Id;
                case "idSelf": return x => x.Id == x.Id;
                // Always true.
                case "orTrue": return x => x.Id == 2 || true;
                case "orCapturedTrue": return x => x.Id == 2 || capturedTrue;
                case "orStaticOn": return x => x.Id == 2 || StaticFlags.On;
                case "orIncludeAll": return x => x.Id == 2 || request.IncludeAll;
                case "orCapturedAEqA": return x => x.Id == 2 || capturedA == capturedA;
                case "orCapturedNullIsNull": return x => x.Id == 2 || capturedNull == null;
                case "deMorganAnd": return x => !(x.Id != x.Id && x.Id == 2);
                case "deMorganOr": return x => !(x.Id != x.Id || x.Id > x.Id);
                // Not evaluated by the guard and not translated by any visitor (plan section 6).
                case "orArithmetic": return x => x.Id == 2 || capturedA + 1 == 8;
                case "orNegate": return x => x.Id == 2 || -capturedA == -7;
                case "orEmptyArrayLength": return x => x.Id == 2 || emptyArray.Length == 0;
                case "orStartsWith": return x => x.Id == 2 || capturedS.StartsWith("s");
                case "orEndsWith": return x => x.Id == 2 || capturedS.EndsWith("s");
                // String Contains and ToString() on a captured value (rev 9, I1-1).
                case "orCapturedContains": return x => x.Id == 2 || capturedS.Contains("s");
                case "rolesContainsOr": return x => roles.Contains("admin") || x.Id == 2;
                case "orToString": return x => x.Id == 2 || capturedS.ToString() == "s";
                case "orContainsFalse": return x => x.Id == 2 || capturedS.Contains("z");
                case "orToStringFormat": return x => x.Id == 2 || capturedA.ToString("D2") == "07";
                case "orContainsNull": return x => x.Id == 2 || capturedS.Contains(capturedNull);
                case "firstNameContainsB": return x => x.FirstName.Contains("b");
                // D9 (owner 2026-10-05): shapes a delete can't send safely.
                case "firstNameContainsUnderscore": return x => x.FirstName.Contains("_");
                case "firstNameContainsNull": return x => x.FirstName.Contains(capturedNull);
                case "firstNameContainsEmpty": return x => x.FirstName.Contains("");
                case "firstNameContainsThisProperty": return x => x.FirstName.Contains(FilterProperty);
                case "idToStringSelf": return x => x.Id.ToString() == x.Id.ToString();
                case "firstNameToStringSelf": return x => x.FirstName.ToString() == x.FirstName;
                case "orCapturedUpperEqualsA": return x => x.Id == 2 || capturedUpperA == "a";
                case "orArrayIndex": return x => x.Id == 2 || capturedArray[0] == 5;
                case "orCoalesce": return x => x.Id == 2 || (capturedNullBool ?? true);
                case "orIsNullOrEmpty": return x => string.IsNullOrEmpty(capturedNull) || x.FirstName == capturedNull;
                // Parameter-free.
                case "true": return x => true;
                case "oneLtTwo": return x => 1 < 2;
                case "abcEqAbc": return x => "abc" == "abc";
                case "capturedTrue": return x => capturedTrue;
                case "false": return x => false;
                // Identifiers containing "true".
                case "trueUpColumn": return x => x.TrueUp == 1;
                // Literal tautologies in the SQL.
                case "orNewHolder": return x => x.Id == 2 || new Holder().Flag;
                case "orNewHolderEqTrue": return x => x.Id == 2 || new Holder().Flag == true;
                case "orNotEmptyContains": return x => x.Id == 2 || !emptyIds.Contains(x.Id);
                case "notEmptyContainsAndId2": return x => !(emptyIds.Contains(x.Id) && x.Id == 2);
                case "firstNameNotNullOrNotEmpty": return x => x.FirstName != null || !emptyIds.Contains(x.Id);
                case "idsContainsOrNotEmpty": return x => ids.Contains(x.Id) || !emptyIds.Contains(x.Id);
                case "notEmptyContainsAlone": return x => !emptyIds.Contains(x.Id);
                case "archivedOrNotEmpty": return x => x.Archived || !emptyIds.Contains(x.Id);
                // Non-trivial.
                case "id2": return x => x.Id == 2;
                case "id2AndName": return x => x.Id == 2 && x.FirstName == "b";
                case "id1Or2": return x => x.Id == 1 || x.Id == 2;
                case "idsContains": return x => ids.Contains(x.Id);
                case "startsWithB": return x => x.FirstName.StartsWith("b");
                case "selfAndId2": return x => x.Id == x.Id && x.Id == 2;
                case "emptyContainsOrId2": return x => emptyIds.Contains(x.Id) || x.Id == 2;
                case "notEmptyAndId2": return x => !emptyIds.Contains(x.Id) && x.Id == 2;
                case "id2AndNotEmpty": return x => x.Id == 2 && !emptyIds.Contains(x.Id);
                case "id2AndNotOther": return x => x.Id == 2 && !otherIds.Contains(x.Id);
                case "nameNotNullAndNotEmpty": return x => x.FirstName != null && !emptyIds.Contains(x.Id);
                case "bigAndId2": return x => x.Big == 1 && x.Id == 2;
                case "big2": return x => x.Big2 == 1;
                default: throw new ArgumentException($"Unknown predicate {key}.", nameof(key));
            }
        }

        private static Expression<Func<DgMissing, bool>> Missing(string key)
        {
            switch (key)
            {
                case "idSelf": return x => x.Id == x.Id;
                case "orTrue": return x => x.Id == 2 || true;
                default: throw new ArgumentException($"Unknown predicate {key}.", nameof(key));
            }
        }

        #endregion

        #region Rows

        [DataTestMethod]
        [DataRow("firstNameSelf", "sync")] [DataRow("firstNameSelf", "async")]
        [DataRow("idGe", "sync")] [DataRow("idGe", "async")]
        [DataRow("idLe", "sync")] [DataRow("idLe", "async")]
        [DataRow("notNe", "sync")] [DataRow("notNe", "async")]
        [DataRow("longCast", "sync")] [DataRow("longCast", "async")]
        [DataRow("idToStringSelf", "sync")] [DataRow("idToStringSelf", "async")]
        [DataRow("firstNameToStringSelf", "sync")] [DataRow("firstNameToStringSelf", "async")]
        public async Task SelfComparison_IsRejected(string key, string path) =>
            await AssertRejected(Row(key), path, SelfReferenceMessage, key);

        [DataTestMethod]
        [DataRow("orTrue", "sync")] [DataRow("orTrue", "async")]
        [DataRow("orCapturedTrue", "sync")] [DataRow("orCapturedTrue", "async")]
        [DataRow("orStaticOn", "sync")] [DataRow("orStaticOn", "async")]
        [DataRow("orIncludeAll", "sync")] [DataRow("orIncludeAll", "async")]
        [DataRow("orCapturedAEqA", "sync")] [DataRow("orCapturedAEqA", "async")]
        [DataRow("orCapturedNullIsNull", "sync")] [DataRow("orCapturedNullIsNull", "async")]
        [DataRow("deMorganAnd", "sync")] [DataRow("deMorganAnd", "async")]
        [DataRow("deMorganOr", "sync")] [DataRow("deMorganOr", "async")]
        [DataRow("orCapturedContains", "sync")] [DataRow("orCapturedContains", "async")]
        [DataRow("rolesContainsOr", "sync")] [DataRow("rolesContainsOr", "async")]
        [DataRow("orToString", "sync")] [DataRow("orToString", "async")]
        [DataRow("orToStringFormat", "sync")] [DataRow("orToStringFormat", "async")]
        [DataRow("orContainsNull", "sync")] [DataRow("orContainsNull", "async")]
        public async Task AlwaysTrue_IsRejected(string key, string path) =>
            await AssertRejected(Row(key), path, AlwaysTrueMessage, key);

        [DataTestMethod]
        [DataRow("idSelf", "sync")] [DataRow("idSelf", "async")]
        [DataRow("orTrue", "sync")] [DataRow("orTrue", "async")]
        public async Task RejectedOnAMissingTable_ReportsTheGuardNotDiscovery(string key, string path)
        {
            RequireDatabase();
            var expected = key == "idSelf" ? SelfReferenceMessage : AlwaysTrueMessage;
            try
            {
                CreateTables();
                DropMissingTable();
                using var provider = CreateProvider();
                var logged = Capture(provider);
                var transactional = (ISqlOrmProvider)provider;
                transactional.BeginTransaction();
                try
                {
                    var exception = await Throws(provider, Missing(key), path);
                    Assert.AreEqual(expected, exception.Message, $"{key} {path}: message");
                    AssertNoDeleteLogged(logged, key, path);
                    Assert.IsFalse(IsDiscovered(provider, typeof(DgMissing)), $"{key} {path}: the type was discovered");
                }
                finally
                {
                    transactional.RollbackTransaction();
                }
            }
            finally
            {
                DropTables();
            }
        }

        [DataTestMethod]
        [DataRow("true", "sync")] [DataRow("true", "async")]
        [DataRow("oneLtTwo", "sync")] [DataRow("oneLtTwo", "async")]
        [DataRow("abcEqAbc", "sync")] [DataRow("abcEqAbc", "async")]
        [DataRow("capturedTrue", "sync")] [DataRow("capturedTrue", "async")]
        [DataRow("false", "sync")] [DataRow("false", "async")]
        public async Task ParameterFree_IsRejectedAsNoColumn(string key, string path) =>
            await AssertRejected(Row(key), path, NoColumnMessage, key);

        [DataTestMethod]
        [DataRow("trueUpColumn", "sync")] [DataRow("trueUpColumn", "async")]
        [DataRow("trueUpTable", "sync")] [DataRow("trueUpTable", "async")]
        public async Task IdentifierContainingTrue_IsAccepted(string key, string path)
        {
            if (key == "trueUpTable")
                await AssertDeletes<DgTrueUp>(x => x.Amount == 5, path, new[] { 2 }, key);
            else
                await AssertDeletes(Row(key), path, new[] { 2, 3 }, key);
        }

        [DataTestMethod]
        [DataRow("orNewHolder", "sync")] [DataRow("orNewHolder", "async")]
        [DataRow("orNewHolderEqTrue", "sync")] [DataRow("orNewHolderEqTrue", "async")]
        [DataRow("orNotEmptyContains", "sync")] [DataRow("orNotEmptyContains", "async")]
        [DataRow("notEmptyContainsAndId2", "sync")] [DataRow("notEmptyContainsAndId2", "async")]
        [DataRow("firstNameNotNullOrNotEmpty", "sync")] [DataRow("firstNameNotNullOrNotEmpty", "async")]
        [DataRow("idsContainsOrNotEmpty", "sync")] [DataRow("idsContainsOrNotEmpty", "async")]
        [DataRow("notEmptyContainsAlone", "sync")] [DataRow("notEmptyContainsAlone", "async")]
        [DataRow("archivedOrNotEmpty", "sync")] [DataRow("archivedOrNotEmpty", "async")]
        public async Task LiteralTautologyInSql_IsRejected(string key, string path) =>
            await AssertRejected(Row(key), path, AlwaysTrueMessage, key);

        [DataTestMethod]
        [DataRow("sync")] [DataRow("async")]
        public async Task NullPredicate_KeepsItsMessage(string path)
        {
            RequireDatabase();
            try
            {
                CreateTables();
                using var provider = CreateProvider();
                var transactional = (ISqlOrmProvider)provider;
                transactional.BeginTransaction();
                try
                {
                    var exception = await Throws<DgRow>(provider, null, path);
                    Assert.AreEqual(NullPredicateMessage, exception.Message, $"null {path}: message");
                }
                finally
                {
                    transactional.RollbackTransaction();
                }
            }
            finally
            {
                DropTables();
            }
        }

        [DataTestMethod]
        [DataRow("sync")] [DataRow("async")]
        public async Task RejectedPredicateWithoutTransaction_KeepsTheTransactionMessage(string path)
        {
            RequireDatabase();
            try
            {
                CreateTables();
                using var provider = CreateProvider();
                var logged = Capture(provider);
                var exception = await Throws(provider, Row("idSelf"), path);
                Assert.AreEqual(TransactionMessage, exception.Message, $"no transaction {path}: message");
                AssertNoDeleteLogged(logged, "idSelf", path);
                Assert.AreEqual(3, provider.GetList<DgRow>().Count, $"no transaction {path}: rows removed");
            }
            finally
            {
                DropTables();
            }
        }

        /// <summary>
        /// The guard's verdict accepts these, because it evaluates no operator other than casts, <c>!</c>, the logical
        /// operators and comparisons, and no method call other than string's <c>Contains</c> and a core type's
        /// <c>ToString()</c> (plan section 6). Nothing is deleted: <c>StartsWith</c>/<c>EndsWith</c> on a captured string
        /// throw <c>NotSupportedException</c> from <see cref="DeletePredicateGuard.Validate"/> (D9), and every provider
        /// fails to translate the rest. If a provider learns to run one, this row fails, and the guard must learn to
        /// evaluate it first.
        /// </summary>
        [DataTestMethod]
        [DataRow("orArithmetic", "sync")] [DataRow("orArithmetic", "async")]
        [DataRow("orNegate", "sync")] [DataRow("orNegate", "async")]
        [DataRow("orEmptyArrayLength", "sync")] [DataRow("orEmptyArrayLength", "async")]
        [DataRow("orArrayIndex", "sync")] [DataRow("orArrayIndex", "async")]
        [DataRow("orCoalesce", "sync")] [DataRow("orCoalesce", "async")]
        [DataRow("orIsNullOrEmpty", "sync")] [DataRow("orIsNullOrEmpty", "async")]
        [DataRow("orStartsWith", "sync")] [DataRow("orStartsWith", "async")]
        [DataRow("orEndsWith", "sync")] [DataRow("orEndsWith", "async")]
        public async Task UnevaluatedParameterFreeParts_AreNotTranslated(string key, string path)
        {
            RequireDatabase();
            try
            {
                CreateTables();
                using var provider = CreateProvider();
                var logged = Capture(provider);
                var transactional = (ISqlOrmProvider)provider;
                transactional.BeginTransaction();
                try
                {
                    var predicate = Row(key);
                    Assert.AreEqual(DeletePredicateVerdict.Acceptable, DeletePredicateGuard.Classify(predicate), $"{key}: verdict");
                    Exception thrown = null;
                    try
                    {
                        if (path == "async")
                            await provider.DeleteAsync(predicate);
                        else
                            provider.Delete(predicate);
                    }
                    catch (Exception ex)
                    {
                        thrown = ex;
                    }
                    Assert.IsNotNull(thrown, $"{key} {path}: the provider ran the delete");
                    AssertNoDeleteLogged(logged, key, path);
                    Assert.AreEqual(3, provider.GetList<DgRow>().Count, $"{key} {path}: rows were deleted");
                }
                finally
                {
                    transactional.RollbackTransaction();
                }
            }
            finally
            {
                DropTables();
            }
        }

        [DataTestMethod]
        [DataRow("id2", "sync", "1,3")] [DataRow("id2", "async", "1,3")]
        [DataRow("id2AndName", "sync", "1,3")] [DataRow("id2AndName", "async", "1,3")]
        [DataRow("id1Or2", "sync", "3")] [DataRow("id1Or2", "async", "3")]
        [DataRow("idsContains", "sync", "3")] [DataRow("idsContains", "async", "3")]
        [DataRow("startsWithB", "sync", "1,3")] [DataRow("startsWithB", "async", "1,3")]
        [DataRow("selfAndId2", "sync", "1,3")] [DataRow("selfAndId2", "async", "1,3")]
        [DataRow("emptyContainsOrId2", "sync", "1,3")] [DataRow("emptyContainsOrId2", "async", "1,3")]
        [DataRow("notEmptyAndId2", "sync", "1,3")] [DataRow("notEmptyAndId2", "async", "1,3")]
        [DataRow("id2AndNotEmpty", "sync", "1,3")] [DataRow("id2AndNotEmpty", "async", "1,3")]
        [DataRow("id2AndNotOther", "sync", "1,3")] [DataRow("id2AndNotOther", "async", "1,3")]
        [DataRow("nameNotNullAndNotEmpty", "sync", "3")] [DataRow("nameNotNullAndNotEmpty", "async", "3")]
        [DataRow("bigAndId2", "sync", "1,3")] [DataRow("bigAndId2", "async", "1,3")]
        [DataRow("big2", "sync", "1")] [DataRow("big2", "async", "1")]
        [DataRow("firstNameContainsB", "sync", "1,3")] [DataRow("firstNameContainsB", "async", "1,3")]
        public async Task NonTrivialPredicates_DeleteTheMatchingRows(string key, string path, string survivors) =>
            await AssertDeletes(Row(key), path, survivors.Split(',').Select(int.Parse).ToArray(), key);

        /// <summary>
        /// D9 (owner decision 2026-10-05): shapes the guard accepts but the providers would widen to (nearly) every row
        /// throw <see cref="NotSupportedException"/> before the DELETE is sent, and every row remains.
        /// </summary>
        [DataTestMethod]
        [DataRow("firstNameContainsUnderscore", "sync", "Contains()'s search value contains a LIKE wildcard")]
        [DataRow("firstNameContainsUnderscore", "async", "Contains()'s search value contains a LIKE wildcard")]
        [DataRow("firstNameContainsNull", "sync", "Contains()'s search value is null or empty")]
        [DataRow("firstNameContainsNull", "async", "Contains()'s search value is null or empty")]
        [DataRow("firstNameContainsEmpty", "sync", "Contains()'s search value is null or empty")]
        [DataRow("firstNameContainsEmpty", "async", "Contains()'s search value is null or empty")]
        [DataRow("firstNameContainsThisProperty", "sync", "Contains()'s search value is a property of a captured object")]
        [DataRow("firstNameContainsThisProperty", "async", "Contains()'s search value is a property of a captured object")]
        [DataRow("orContainsFalse", "sync", "Contains() on a value that doesn't read the row isn't supported in a delete")]
        [DataRow("orContainsFalse", "async", "Contains() on a value that doesn't read the row isn't supported in a delete")]
        [DataRow("orCapturedUpperEqualsA", "sync", "Comparing strings that don't read the row isn't supported in a delete")]
        [DataRow("orCapturedUpperEqualsA", "async", "Comparing strings that don't read the row isn't supported in a delete")]
        public async Task UnsafeDeleteShapes_AreNotSupported(string key, string path, string expectedPrefix)
        {
            RequireDatabase();
            try
            {
                CreateTables();
                using var provider = CreateProvider();
                var logged = Capture(provider);
                var transactional = (ISqlOrmProvider)provider;
                transactional.BeginTransaction();
                try
                {
                    var predicate = Row(key);
                    NotSupportedException exception;
                    if (path == "async")
                        exception = await Assert.ThrowsExceptionAsync<NotSupportedException>(() => provider.DeleteAsync(predicate));
                    else
                        exception = Assert.ThrowsException<NotSupportedException>(() => provider.Delete(predicate));
                    StringAssert.StartsWith(exception.Message, expectedPrefix, $"{key} {path}");
                    AssertNoDeleteLogged(logged, key, path);
                    Assert.AreEqual(3, provider.GetList<DgRow>().Count, $"{key} {path}: rows were deleted");
                }
                finally
                {
                    transactional.RollbackTransaction();
                }
            }
            finally
            {
                DropTables();
            }
        }

        #endregion

        #region Helpers

        private async Task AssertRejected(Expression<Func<DgRow, bool>> predicate, string path, string expectedMessage,
            string key)
        {
            RequireDatabase();
            try
            {
                CreateTables();
                using var provider = CreateProvider();
                var logged = Capture(provider);
                var transactional = (ISqlOrmProvider)provider;
                transactional.BeginTransaction();
                try
                {
                    var exception = await Throws(provider, predicate, path);
                    Assert.AreEqual(expectedMessage, exception.Message, $"{key} {path}: message");
                    AssertNoDeleteLogged(logged, key, path);
                    Assert.AreEqual(3, provider.GetList<DgRow>().Count, $"{key} {path}: rows were deleted");
                }
                finally
                {
                    transactional.RollbackTransaction();
                }
            }
            finally
            {
                DropTables();
            }
        }

        private async Task AssertDeletes<T>(Expression<Func<T, bool>> predicate, string path, int[] survivors, string key)
            where T : class, new()
        {
            RequireDatabase();
            try
            {
                CreateTables();
                using var provider = CreateProvider();
                var transactional = (ISqlOrmProvider)provider;
                transactional.BeginTransaction();
                try
                {
                    var before = provider.GetList<T>().Count;
                    int deleted;
                    try
                    {
                        deleted = path == "async" ? await provider.DeleteAsync(predicate) : provider.Delete(predicate);
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail($"{key} {path}: the delete was rejected or failed: {ex.GetType().Name}: {ex.Message}");
                        return;
                    }
                    Assert.AreEqual(before - survivors.Length, deleted, $"{key} {path}: deleted count");
                    var remaining = provider.GetList<T>().Select(IdOf).OrderBy(i => i).ToArray();
                    CollectionAssert.AreEqual(survivors, remaining, $"{key} {path}: surviving ids");
                }
                finally
                {
                    transactional.RollbackTransaction();
                }
            }
            finally
            {
                DropTables();
            }
        }

        private static int IdOf(object row) => row switch
        {
            DgRow r => r.Id,
            DgTrueUp t => t.Id,
            _ => throw new ArgumentException(row?.GetType().Name)
        };

        private static async Task<InvalidOperationException> Throws<T>(OrmDataProvider provider,
            Expression<Func<T, bool>> predicate, string path) where T : class, new()
        {
            if (path == "async")
                return await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => provider.DeleteAsync(predicate));
            return Assert.ThrowsException<InvalidOperationException>(() => provider.Delete(predicate));
        }

        private static List<string> Capture(OrmDataProvider provider)
        {
            var logged = new List<string>();
            provider.Log = logged.Add;
            return logged;
        }

        private static void AssertNoDeleteLogged(List<string> logged, string key, string path)
        {
            var deletes = logged.Where(s => s != null && s.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.AreEqual(0, deletes.Count, $"{key} {path}: a DELETE was logged: {string.Join(" | ", deletes)}");
        }

        #endregion
    }
}
