using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Funcular.Data.Orm.SqlServer.Tests.Domain.Objects.Person;
using Microsoft.Data.SqlClient;
using OfficeOpenXml;
using System.IO;

namespace Funcular.Data.Orm.SqlServer.Tests
{
    [TestClass]
    public class SqlDataProviderPerformanceTests
    {
        private string _connectionString;
        public required SqlServerOrmDataProvider _provider;
        private readonly StringBuilder _sb = new();
        private static ExcelPackage _workbook;
        private static ExcelWorksheet _worksheet;
        private static int _excelRow = 2;

        public void OutputTestMethodName([CallerMemberName] string callerMemberName = "")
        {
            Console.WriteLine($"\r\nTest: {callerMemberName}");
        }

        [ClassInitialize]
        public static void ClassSetup(TestContext context)
        {
            ExcelPackage.License.SetNonCommercialOrganization("Funcular Labs - Open Source Projects");
            _workbook = new ExcelPackage();
            _worksheet = _workbook.Workbook.Worksheets.Add("Performance");
            _worksheet.Cells["B1"].Value = "test";
            _worksheet.Cells["C1"].Value = "count";
            _worksheet.Cells["D1"].Value = "elapsed per instance (ms)";
            _worksheet.Cells["E1"].Value = "instances per second";
            _excelRow = 2;
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            var projectDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            var perfDir = Path.Combine(projectDir, "PerformanceTests");
            Directory.CreateDirectory(perfDir);
            var filePath = Path.Combine(perfDir, $"PerformanceResults_{DateTime.Now:yyyyMMdd_HHmmssfff}.xlsx");
            using (var stream = File.Create(filePath))
            {
                _workbook?.SaveAs(stream);
            }
            _workbook?.Dispose();
        }

        [TestInitialize]
        public void Setup()
        {
            _sb.Clear();
            _connectionString = Environment.GetEnvironmentVariable("FUNKY_CONNECTION") ??
                                "Data Source=localhost;Initial Catalog=funky_db;Integrated Security=SSPI;TrustServerCertificate=true;";
            TestConnection();

            _provider = new SqlServerOrmDataProvider(_connectionString)
            {
                Log = s =>
                {
                    // Uncomment to see the generated SQL for each operation in the window of your choice:
                    // Debug.WriteLine(s);
                    Console.WriteLine(s);
                    // _sb.AppendLine(s);
                }
            };
        }

        [TestCleanup]
        public void Cleanup()
        {
            _provider?.Dispose();
        }

        private void CleanupTestRecords(string firstName, string lastName)
        {
            using (var sqlConnection = new SqlConnection(_connectionString))
            {
                sqlConnection.Open();
                using (var command =
                       new SqlCommand("DELETE FROM person WHERE first_name = @firstName AND last_name = @lastName",
                           sqlConnection))
                {
                    command.Parameters.AddWithValue("@firstName", firstName);
                    command.Parameters.AddWithValue("@lastName", lastName);
                    command.ExecuteNonQuery();
                    Console.WriteLine("Cleaned up rows.");
                }
            }
        }

        private void TestConnection()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                try
                {
                    connection.Open();
                    Debug.WriteLine("Connection successful.");
                }
                catch (SqlException ex)
                {
                    throw new InvalidOperationException(
                        $"Connection failed. Ensure funky_db exists and configure FUNKY_CONNECTION environment variable if not using localhost.\r\n\r\n{ex}");
                }
            }
        }

        [DataRow(1)]
        [DataRow(100)]
        [DataRow(1000)]
        [DataRow(10000)]
        [TestMethod]
        public void Test_Insert_Performance(int count)
        {
            OutputTestMethodName();
            var firstName = Guid.NewGuid().ToString();
            var lastName = Guid.NewGuid().ToString();
            var persons = new List<Person>(count);
            for (int i = 0; i < count; i++)
            {
                var person = new Person
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Birthdate = DateTime.UtcNow.AddYears(-20).AddDays(i)
                };
                persons.Add(person);
            }
            var sw = Stopwatch.StartNew();
            for (int j = 0; j < count; j++)
            {
                _provider.Insert(persons[j]);
            }
            sw.Stop();
            var format = $"Inserted {count} records in {sw.Elapsed} ms ({(double)sw.ElapsedMilliseconds / count} ms/record | {(double)count / ((double)sw.ElapsedMilliseconds / 1000D)} rows/second)";
            _sb.Append(format);
            _worksheet!.Cells[_excelRow, 2].Value = "Insert";
            _worksheet.Cells[_excelRow, 3].Value = count;
            _worksheet.Cells[_excelRow, 4].Value = (double)sw.ElapsedMilliseconds / count;
            _worksheet.Cells[_excelRow, 5].Value = Math.Round((double)count / sw.Elapsed.TotalSeconds);
            _excelRow++;
            CleanupTestRecords(firstName, lastName);
            Console.WriteLine("\r\n\r\nResults:");
            Console.WriteLine(_sb.ToString());
        }

        [DataRow(1)]
        [DataRow(100)]
        [DataRow(1000)]
        [DataRow(10000)]
        [TestMethod]
        public void Test_Select_Performance(int count)
        {
            OutputTestMethodName();
            var firstName = Guid.NewGuid().ToString();
            var lastName = Guid.NewGuid().ToString();
            for (int i = 0; i < count; i++)
            {
                var person = new Person
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Birthdate = DateTime.UtcNow.AddYears(-20).AddDays(i)
                };
                _provider.Insert(person);
            }
            var sw = Stopwatch.StartNew();
            var results = _provider.Query<Person>().OrderBy(x => x.Id).Take(count).ToList();
            sw.Stop();
            var returned = results.Count;
            var format = $"Filtered {returned} records in {sw.ElapsedMilliseconds} ms ({(double)sw.ElapsedMilliseconds / returned} ms/record | {(double)returned / sw.Elapsed.TotalSeconds} rows/second)";
            _sb.AppendLine(format);
            _worksheet!.Cells[_excelRow, 2].Value = "Select";
            _worksheet.Cells[_excelRow, 3].Value = count;
            _worksheet.Cells[_excelRow, 4].Value = (double)sw.ElapsedMilliseconds / returned;
            _worksheet.Cells[_excelRow, 5].Value = Math.Round((double)returned / sw.Elapsed.TotalSeconds);
            _excelRow++;
            CleanupTestRecords(firstName, lastName);
            Console.WriteLine("\r\n\r\nResults:");
            Console.WriteLine(_sb.ToString());
        }

        [DataRow(1)]
        [DataRow(100)]
        [DataRow(1000)]
        [DataRow(10000)]
        [TestMethod]
        public void Test_Filter_Performance(int count)
        {
            OutputTestMethodName();
            var firstName = Guid.NewGuid().ToString();
            var lastName = Guid.NewGuid().ToString();
            for (int i = 0; i < count; i++)
            {
                var person = new Person
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Birthdate = DateTime.UtcNow.AddYears(-20).AddDays(i)
                };
                _provider.Insert(person);
            }
            var sw = Stopwatch.StartNew();
            var results = _provider.Query<Person>(p => p.FirstName == firstName && p.LastName == lastName);
            sw.Stop();
            var returned = results.Count;
            var format = $"Filtered {returned} records in {sw.ElapsedMilliseconds} ms ({(double)sw.ElapsedMilliseconds / returned} ms/record | {(double)returned / sw.Elapsed.TotalSeconds} rows/second)";
            _sb.AppendLine(format);
            _worksheet!.Cells[_excelRow, 2].Value = "Filter";
            _worksheet.Cells[_excelRow, 3].Value = count;
            _worksheet.Cells[_excelRow, 4].Value = (double)sw.ElapsedMilliseconds / returned;
            _worksheet.Cells[_excelRow, 5].Value = Math.Round((double)returned / sw.Elapsed.TotalSeconds);
            _excelRow++;

            CleanupTestRecords(firstName, lastName);

            Console.WriteLine("\r\n\r\nResults:");
            Console.WriteLine(_sb.ToString());
        }

        [DataRow(100)]
        [DataRow(1000)]
        [DataRow(10000)]
        [TestMethod]
        public void Test_Pagination_Performance(int count)
        {
            var firstName = Guid.NewGuid().ToString();
            var lastName = Guid.NewGuid().ToString();
            for (int i = 0; i < count; i++)
            {
                var person = new Person
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Birthdate = DateTime.UtcNow.AddYears(-20).AddDays(i)
                };
                _provider.Insert(person);
            }
            int pageSize = count / 10;
            var overallSw = Stopwatch.StartNew();
            long totalReturned = 0;
            for (int page = 0; page < 10; page++)
            {
                var pageSw = Stopwatch.StartNew();
                var results = _provider.Query<Person>()
                    .Where(p => p.FirstName == firstName && p.LastName == lastName)
                    .Skip(page * pageSize)
                    .Take(pageSize)
                    .ToList();
                pageSw.Stop();
                var recordsInPage = results.Count;
                totalReturned += recordsInPage;
                var pageFormat = $"Page {page + 1}: Paginated {recordsInPage} records in {pageSw.ElapsedMilliseconds} ms ({(double)pageSw.ElapsedMilliseconds / recordsInPage} ms/record | {(double)recordsInPage / pageSw.Elapsed.TotalSeconds} rows/second)";
                _sb.AppendLine(pageFormat);
            }
            overallSw.Stop();
            var overallFormat = $"Overall: Paginated {totalReturned} records in {overallSw.ElapsedMilliseconds} ms ({(double)overallSw.ElapsedMilliseconds / totalReturned} ms/record | {(double)totalReturned / overallSw.Elapsed.TotalSeconds} rows/second)";
            _sb.AppendLine(overallFormat);
            _worksheet!.Cells[_excelRow, 2].Value = "Pagination";
            _worksheet.Cells[_excelRow, 3].Value = count;
            _worksheet.Cells[_excelRow, 4].Value = (double)overallSw.ElapsedMilliseconds / totalReturned;
            _worksheet.Cells[_excelRow, 5].Value = Math.Round((double)totalReturned / overallSw.Elapsed.TotalSeconds);
            _excelRow++;
            CleanupTestRecords(firstName, lastName);
            Console.WriteLine("\r\n\r\nResults:");
            Console.WriteLine(_sb.ToString());
        }

        [DataRow(1)]
        [DataRow(100)]
        [DataRow(1000)]
        [DataRow(10000)]
        [TestMethod]
        public void Test_Aggregate_Performance(int count)
        {
            var firstName = Guid.NewGuid().ToString();
            var lastName = Guid.NewGuid().ToString();
            for (int i = 0; i < count; i++)
            {
                var person = new Person
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Birthdate = DateTime.UtcNow.AddYears(-20).AddDays(i)
                };
                _provider.Insert(person);
            }
            var sw = Stopwatch.StartNew();
            var result = _provider.Query<Person>().Count(p => p.FirstName == firstName && p.LastName == lastName);
            sw.Stop();
            Console.WriteLine(result);
            var format = $"Aggregated COUNT over {count} records in {sw.ElapsedMilliseconds} ms ({(double)sw.ElapsedMilliseconds / count} ms/record)";
            _sb.AppendLine(format);
            _worksheet!.Cells[_excelRow, 2].Value = "Aggregate";
            _worksheet.Cells[_excelRow, 3].Value = count;
            _worksheet.Cells[_excelRow, 4].Value = (double)sw.ElapsedMilliseconds / count;
            _worksheet.Cells[_excelRow, 5].Value = Math.Round((double)count / sw.Elapsed.TotalSeconds);
            _excelRow++;
            CleanupTestRecords(firstName, lastName);
            Console.WriteLine("\r\n\r\nResults:");
            Console.WriteLine(_sb.ToString());
        }

        [DataRow(1)]
        [DataRow(100)]
        [DataRow(1000)]
        [DataRow(10000)]
        [TestMethod]
        public void Test_Update_Performance(int count)
        {
            OutputTestMethodName();
            var firstName = Guid.NewGuid().ToString();
            var lastName = Guid.NewGuid().ToString();
            for (int i = 0; i < count; i++)
            {
                var person = new Person
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Birthdate = DateTime.UtcNow.AddYears(-20).AddDays(i)
                };
                _provider.Insert(person);
            }
            var personsToUpdate = _provider.Query<Person>(p => p.FirstName == firstName && p.LastName == lastName).ToList();
            var sw = Stopwatch.StartNew();
            for (int j = 0; j < count; j++)
            {
                personsToUpdate[j].MiddleInitial = "U";
                _provider.Update(personsToUpdate[j]);
            }
            sw.Stop();
            var updated = count;
            var format = $"Updated {updated} records in {sw.ElapsedMilliseconds} ms ({(double)sw.ElapsedMilliseconds / updated} ms/record | {(double)updated / sw.Elapsed.TotalSeconds} rows/second)";
            _sb.AppendLine(format);
            _worksheet!.Cells[_excelRow, 2].Value = "Update";
            _worksheet.Cells[_excelRow, 3].Value = count;
            _worksheet.Cells[_excelRow, 4].Value = (double)sw.ElapsedMilliseconds / updated;
            _worksheet.Cells[_excelRow, 5].Value = Math.Round((double)updated / sw.Elapsed.TotalSeconds);
            _excelRow++;
            CleanupTestRecords(firstName, lastName);
            Console.WriteLine("\r\n\r\nResults:");
            Console.WriteLine(_sb.ToString());
        }
    }
}