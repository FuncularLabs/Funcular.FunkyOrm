using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.PostgreSql.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.Tests.Shared;
using Npgsql;

namespace Funcular.Data.Orm.PostgreSql.Tests
{
    /// <summary>The <see cref="TestRowLedger"/> contract on PostgreSQL (cases in <see cref="TestRowLedgerCases"/>).</summary>
    [TestClass]
    public class PostgreSqlTestRowLedgerTests : PostgreSqlTestFixture
    {
        private TestRowLedgerCases Cases => new TestRowLedgerCases(
            () => new NpgsqlConnection(_connectionString),
            TestRowLedger.PostgreSqlTables,
            name =>
            {
                var organization = new OrganizationEntity { Name = name };
                _provider.Insert(organization);
                return organization;
            },
            (firstName, lastName, employerId) =>
            {
                var person = new PersonEntity { FirstName = firstName, LastName = lastName, EmployerId = employerId };
                _provider.Insert(person);
                return person;
            });

        [TestMethod]
        public void Finish_DeletesTrackedRows_NewestFirst() => Cases.Finish_DeletesTrackedRows_NewestFirst();

        [TestMethod]
        public void Finish_Fails_WhenAnUntrackedCopyIsLeft_AndLeavesIt() =>
            Cases.Finish_Fails_WhenAnUntrackedCopyIsLeft_AndLeavesIt();

        [TestMethod]
        public void Finish_AttemptsEveryDelete_AndReportsTheOnesThatFail() =>
            Cases.Finish_AttemptsEveryDelete_AndReportsTheOnesThatFail();

        [TestMethod]
        public void Finish_AttemptsEveryDelete_WhenADeleteThrowsANonDatabaseError() =>
            Cases.Finish_AttemptsEveryDelete_WhenADeleteThrowsANonDatabaseError();

        [TestMethod]
        public void Finish_Passes_WhenATrackedRowIsAlreadyGone() => Cases.Finish_Passes_WhenATrackedRowIsAlreadyGone();

        [TestMethod]
        public void Track_Rejects_AnEntityOutsideTheGuardedSet() => Cases.Track_Rejects_AnEntityOutsideTheGuardedSet();

        [TestMethod]
        public void Tables_AreTheProvidersGuardedSet() =>
            CollectionAssert.AreEqual(
                new[] { "person", "address", "person_address", "organization", "country" },
                TestRowLedger.PostgreSqlTables);
    }
}
