using Funcular.Data.Orm.MySql.Tests.Domain;
using Funcular.Data.Orm.Tests.Shared;
using MySqlConnector;

namespace Funcular.Data.Orm.MySql.Tests
{
    /// <summary>The <see cref="TestRowLedger"/> contract on MySQL (cases in <see cref="TestRowLedgerCases"/>).</summary>
    [TestClass]
    public class MySqlTestRowLedgerTests : MySqlTestFixture
    {
        [TestInitialize]
        public void Setup() => InitProvider();

        [TestCleanup]
        public void Cleanup() => DisposeProvider();

        private TestRowLedgerCases Cases => new TestRowLedgerCases(
            () => new MySqlConnection(_connectionString),
            TestRowLedger.MySqlTables,
            name =>
            {
                var organization = new Organization { Name = name };
                _provider.Insert(organization);
                return organization;
            },
            (firstName, lastName, employerId) =>
            {
                var person = new Person { FirstName = firstName, LastName = lastName, EmployerId = employerId };
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
        public void Finish_Passes_WhenATrackedRowIsAlreadyGone() => Cases.Finish_Passes_WhenATrackedRowIsAlreadyGone();

        [TestMethod]
        public void Track_Rejects_AnEntityOutsideTheGuardedSet() => Cases.Track_Rejects_AnEntityOutsideTheGuardedSet();

        [TestMethod]
        public void Tables_AreTheProvidersGuardedSet() =>
            CollectionAssert.AreEqual(
                new[] { "person", "address", "person_address", "organization", "country", "project", "project_milestone", "project_note" },
                TestRowLedger.MySqlTables);
    }
}
