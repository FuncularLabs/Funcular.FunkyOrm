using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Organization;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Entities.Person;
using Funcular.Data.Orm.Tests.Shared;
using Microsoft.Data.SqlClient;

namespace Funcular.Data.Orm.SqlServer.Tests.Shared
{
    /// <summary>The <see cref="TestRowLedger"/> contract on SQL Server (cases in <see cref="TestRowLedgerCases"/>).</summary>
    [TestClass]
    public class SqlServerTestRowLedgerTests : SqlServerTestFixture
    {
        private TestRowLedgerCases Cases => new TestRowLedgerCases(
            () => new SqlConnection(_connectionString),
            TestRowLedger.SqlServerTables,
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
                new[] { "person", "address", "person_address", "organization", "country", "project", "project_milestone", "project_note" },
                TestRowLedger.SqlServerTables);
    }
}
