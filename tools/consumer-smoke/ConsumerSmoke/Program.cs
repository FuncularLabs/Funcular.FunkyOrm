using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Reflection;
using Funcular.Data.Orm;
using Funcular.Data.Orm.SqlServer;
#if !NET48
using Funcular.Data.Orm.MySql;
using Funcular.Data.Orm.PostgreSql;
using Funcular.Data.Orm.Sqlite;
using Microsoft.Data.Sqlite;
#endif

namespace ConsumerSmoke
{
    /// <summary>
    /// Loads every provider the package carries for this target framework and checks that each driver the package
    /// declares is present at the expected minimum version (docs/plans/DECISIONS-3.10.2.md FD-05). A provider is
    /// "loaded" when a query through it reaches the driver: the connection attempt against an unreachable address
    /// fails, which is expected, but a missing or mismatched driver assembly would fail earlier, with
    /// FileNotFoundException or FileLoadException. SQLite runs a real round trip on a temp file.
    /// Arguments, one per driver, keyed by the driver's ASSEMBLY name: Microsoft.Data.SqlClient=6.0.0 Npgsql=9.0.5
    /// MySqlConnector=2.6.2 Microsoft.Data.Sqlite=8.0.32 (name=expected informational version). A probed driver with no
    /// expected version is a failure, so a misspelt key can't skip a check. Exit code 0 when every check passes.
    /// </summary>
    public static class Program
    {
        [Table("smoke_row")]
        public class SmokeRow
        {
            [Key, Column("id")] public int Id { get; set; }
            [Column("name")] public string Name { get; set; }
        }

        private static readonly List<string> Failures = new List<string>();

        public static int Main(string[] args)
        {
            var expected = args.Select(a => a.Split('=')).Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
            Console.WriteLine("ConsumerSmoke on " + RuntimeDescription());
            Console.WriteLine("Funcular.Data.Orm " + Informational(typeof(OrmDataProvider).Assembly) + " / " + Informational(typeof(SqlServerOrmDataProvider).Assembly));

            Probe("SQL Server", "Microsoft.Data.SqlClient", expected, () =>
                new SqlServerOrmDataProvider("Server=127.0.0.1,1;Database=smoke;User Id=x;Password=x;Encrypt=False;Connect Timeout=1;"));
#if !NET48
            Probe("PostgreSQL", "Npgsql", expected, () =>
                new PostgreSqlOrmDataProvider("Host=127.0.0.1;Port=1;Database=smoke;Username=x;Password=x;Timeout=1;Pooling=false"));
            Probe("MySQL", "MySqlConnector", expected, () =>
                new MySqlOrmDataProvider("Server=127.0.0.1;Port=1;Database=smoke;User=x;Password=x;ConnectionTimeout=1;Pooling=false"));
            SqliteRoundTrip(expected);
#else
            Console.WriteLine("net48: the package carries the SQL Server provider only; PostgreSQL, MySQL and SQLite are not probed.");
#endif

            if (Failures.Count == 0)
            {
                Console.WriteLine("PASS: every provider loaded its driver at the expected minimum.");
                return 0;
            }
            Console.WriteLine("FAIL:");
            foreach (var failure in Failures)
                Console.WriteLine("  - " + failure);
            return 1;
        }

        /// <summary>Constructs the provider and runs a query, which reaches the driver; then checks the driver assembly.</summary>
        private static void Probe(string provider, string driver, IDictionary<string, string> expected, Func<OrmDataProvider> create)
        {
            try
            {
                var instance = create();
                try
                {
                    instance.Query<SmokeRow>(x => x.Id == 1);
                    Failures.Add(provider + ": the query against an unreachable address succeeded, so the probe proves nothing");
                }
                catch (FileNotFoundException ex) { Failures.Add(provider + ": a driver assembly is missing: " + ex.Message); }
                catch (FileLoadException ex) { Failures.Add(provider + ": a driver assembly failed to load: " + ex.Message); }
                catch (TypeLoadException ex) { Failures.Add(provider + ": a type failed to load: " + ex.Message); }
                catch (Exception ex)
                {
                    // The connection attempt failed, as expected: the driver was reached.
                    Console.WriteLine($"{provider}: query reached the driver and failed to connect as expected ({Root(ex).GetType().Name}).");
                }
                if (instance is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception ex)
            {
                Failures.Add(provider + ": constructing the provider threw " + ex.GetType().Name + ": " + ex.Message);
            }
            CheckDriver(provider, driver, expected);
        }

        private static void CheckDriver(string provider, string driver, IDictionary<string, string> expected)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == driver);
            if (assembly == null)
            {
                Failures.Add($"{provider}: the driver assembly {driver} was never loaded");
                return;
            }
            var version = Informational(assembly);
            Console.WriteLine($"{provider}: {driver} {version} (assembly {assembly.GetName().Version})");
            if (!expected.TryGetValue(driver, out var want))
                Failures.Add($"{provider}: no expected version was given for {driver} (pass {driver}=<version>), so its minimum wasn't checked");
            else if (!version.Equals(want, StringComparison.OrdinalIgnoreCase))
                Failures.Add($"{provider}: {driver} is {version}; the package's declared minimum is {want}, and a consumer that installs the package alone must get exactly that");
        }

#if !NET48
        private static void SqliteRoundTrip(IDictionary<string, string> expected)
        {
            var path = Path.Combine(Path.GetTempPath(), "funky-smoke-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var connection = new SqliteConnection("Data Source=" + path))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "CREATE TABLE smoke_row (id INTEGER PRIMARY KEY, name TEXT); INSERT INTO smoke_row (id, name) VALUES (1, 'a'), (2, 'b');";
                        command.ExecuteNonQuery();
                    }
                }
                var provider = new SqliteOrmDataProvider("Data Source=" + path);
                var rows = provider.Query<SmokeRow>(x => x.Name == "a").ToList();
                if (rows.Count != 1 || rows[0].Id != 1)
                    Failures.Add("SQLite: the round trip returned " + rows.Count + " row(s) where 1 was expected");
                else
                    Console.WriteLine("SQLite: round trip selected row 1.");
                provider.Dispose();
            }
            catch (Exception ex)
            {
                Failures.Add("SQLite: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
            }
            CheckDriver("SQLite", "Microsoft.Data.Sqlite", expected);
        }
#endif

        private static string Informational(Assembly assembly)
        {
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                                ?? assembly.GetName().Version?.ToString() ?? "?";
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational.Substring(0, plus) : informational;
        }

        private static Exception Root(Exception exception)
        {
            while (exception.InnerException != null)
                exception = exception.InnerException;
            return exception;
        }

        private static string RuntimeDescription()
        {
#if NET48
            return ".NET Framework " + Environment.Version;
#else
            return System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
#endif
        }
    }
}
