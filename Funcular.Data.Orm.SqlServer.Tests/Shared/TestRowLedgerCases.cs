// Linked into the PostgreSQL and MySQL test projects. Each provider's TestRowLedgerTests class calls these cases.
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Funcular.Data.Orm.Tests.Shared
{
    /// <summary>
    /// The <see cref="TestRowLedger"/> contract, run against one provider's database. Every case deletes, in
    /// <c>finally</c>, every row it inserted, newest first.
    /// </summary>
    public sealed class TestRowLedgerCases
    {
        private readonly Func<DbConnection> _newConnection;
        private readonly IReadOnlyList<string> _tables;
        private readonly Func<string, object> _insertOrganization;
        private readonly Func<string, string, int?, object> _insertPerson;

        /// <param name="newConnection">Returns a new, unopened connection to the test database.</param>
        /// <param name="tables">The provider's guarded tables.</param>
        /// <param name="insertOrganization">Inserts an organization with this name; returns the entity.</param>
        /// <param name="insertPerson">Inserts a person (first name, last name, employer id); returns the entity.</param>
        public TestRowLedgerCases(Func<DbConnection> newConnection, IReadOnlyList<string> tables,
            Func<string, object> insertOrganization, Func<string, string, int?, object> insertPerson)
        {
            _newConnection = newConnection;
            _tables = tables;
            _insertOrganization = insertOrganization;
            _insertPerson = insertPerson;
        }

        public void Finish_DeletesTrackedRows_NewestFirst()
        {
            var inserted = new List<KeyValuePair<string, int>>();
            try
            {
                var ledger = TestRowLedger.Begin(_newConnection, _tables);
                var marker = NewMarker();
                var organization = Insert(inserted, "organization", _insertOrganization(marker));
                ledger.Track(organization);
                var person = Insert(inserted, "person", _insertPerson(marker, marker, IdOf(organization)));
                ledger.Track(person);

                ledger.Finish();

                Assert.IsFalse(Exists("person", IdOf(person)), "The tracked person is still there.");
                Assert.IsFalse(Exists("organization", IdOf(organization)), "The tracked organization is still there.");
            }
            finally
            {
                DeleteAll(inserted);
            }
        }

        public void Finish_Fails_WhenAnUntrackedCopyIsLeft_AndLeavesIt()
        {
            var inserted = new List<KeyValuePair<string, int>>();
            try
            {
                var ledger = TestRowLedger.Begin(_newConnection, _tables);
                var marker = NewMarker();
                var tracked = Insert(inserted, "person", _insertPerson(marker, marker, null));
                ledger.Track(tracked);
                var copy = Insert(inserted, "person", _insertPerson(marker, marker, null));

                var failure = Assert.ThrowsException<AssertFailedException>(() => ledger.Finish());

                var counts = Regex.Match(failure.Message, @"  person: (\d+) -> (\d+)");
                Assert.IsTrue(counts.Success, "The message doesn't give person's counts: " + failure.Message);
                Assert.AreEqual(long.Parse(counts.Groups[1].Value) + 1, long.Parse(counts.Groups[2].Value),
                    failure.Message);
                Assert.IsFalse(Exists("person", IdOf(tracked)), "The tracked person is still there.");
                Assert.IsTrue(Exists("person", IdOf(copy)), "Finish deleted a row it didn't track.");
            }
            finally
            {
                DeleteAll(inserted);
            }
        }

        public void Finish_AttemptsEveryDelete_AndReportsTheOnesThatFail()
        {
            var inserted = new List<KeyValuePair<string, int>>();
            try
            {
                var ledger = TestRowLedger.Begin(_newConnection, _tables);
                var marker = NewMarker();
                var earlier = Insert(inserted, "person", _insertPerson(marker, marker, null));
                ledger.Track(earlier);
                var organization = Insert(inserted, "organization", _insertOrganization(marker));
                ledger.Track(organization);
                Insert(inserted, "person", _insertPerson(marker, marker, IdOf(organization)));

                var failure = Assert.ThrowsException<AssertFailedException>(() => ledger.Finish());

                StringAssert.Contains(failure.Message, "  organization id " + IdOf(organization) + ": ");
                StringAssert.Contains(failure.Message, "  person: ");
                Assert.IsFalse(Exists("person", IdOf(earlier)), "A failed delete stopped the later ones.");
            }
            finally
            {
                DeleteAll(inserted);
            }
        }

        public void Finish_AttemptsEveryDelete_WhenADeleteThrowsANonDatabaseError()
        {
            var inserted = new List<KeyValuePair<string, int>>();
            try
            {
                var ledger = TestRowLedger.Begin(_newConnection, _tables);
                var marker = NewMarker();
                var earlier = Insert(inserted, "person", _insertPerson(marker, marker, null));
                ledger.Track(earlier);
                // Deleted first; no driver can bind an object as @id, so the delete throws outside DbException.
                ledger.Track(new PersonWithUnbindableId());

                var failure = Assert.ThrowsException<AssertFailedException>(() => ledger.Finish());

                StringAssert.Contains(failure.Message, "These tracked rows could not be deleted:");
                Assert.IsFalse(Exists("person", IdOf(earlier)), "A failed delete stopped the later ones.");
            }
            finally
            {
                DeleteAll(inserted);
            }
        }

        public void Finish_Passes_WhenATrackedRowIsAlreadyGone()
        {
            var inserted = new List<KeyValuePair<string, int>>();
            try
            {
                var ledger = TestRowLedger.Begin(_newConnection, _tables);
                var marker = NewMarker();
                var person = Insert(inserted, "person", _insertPerson(marker, marker, null));
                ledger.Track(person);
                TestRowLedger.DeleteRow(_newConnection, "person", IdOf(person));

                ledger.Finish();
            }
            finally
            {
                DeleteAll(inserted);
            }
        }

        public void Track_Rejects_AnEntityOutsideTheGuardedSet()
        {
            var ledger = TestRowLedger.Begin(_newConnection, _tables);

            Assert.ThrowsException<ArgumentException>(() => ledger.Track(new NotGuarded()));
            Assert.ThrowsException<ArgumentException>(() => ledger.Track(new PersonWithoutId()));

            ledger.Finish();
        }

        [Table("not_guarded")]
        private sealed class NotGuarded
        {
            public int Id { get; set; }
        }

        [Table("person")]
        private sealed class PersonWithoutId
        {
            public string FirstName { get; set; }
        }

        [Table("person")]
        private sealed class PersonWithUnbindableId
        {
            public object Id { get; set; } = new object();
        }

        private static string NewMarker() => "ledger_" + Guid.NewGuid().ToString("N").Substring(0, 12);

        private static int IdOf(object entity) => (int)entity.GetType().GetProperty("Id").GetValue(entity);

        private static object Insert(List<KeyValuePair<string, int>> inserted, string table, object entity)
        {
            inserted.Add(new KeyValuePair<string, int>(table, IdOf(entity)));
            return entity;
        }

        private bool Exists(string table, int id)
        {
            using (var connection = _newConnection())
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM " + table + " WHERE id = @id";
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@id";
                    parameter.Value = id;
                    command.Parameters.Add(parameter);
                    return Convert.ToInt64(command.ExecuteScalar()) > 0;
                }
            }
        }

        private void DeleteAll(List<KeyValuePair<string, int>> inserted)
        {
            for (var i = inserted.Count - 1; i >= 0; i--)
                TestRowLedger.DeleteRow(_newConnection, inserted[i].Key, inserted[i].Value);
        }
    }
}
