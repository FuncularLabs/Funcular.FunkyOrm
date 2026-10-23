// EF Core 10 reference: how EF translates null-sensitive predicates (default: relational nulls OFF = C# semantics),
// and whether EF's queryables advertise IOrderedQueryable. ToQueryString needs no database connection.
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

int? v = 5;
int? none = null;
string? noName = null;
Color? noColor = null;
string a = "a";
var ids = new int?[] { 1, 2 };
var idsWithNull = new int?[] { 1, null };
var onlyNull = new int?[] { null };
var intIds = new[] { 1, 2 };
bool? nullBool = null;
var t = true;

var cases = new (string Name, Expression<Func<Row, bool>> Pred)[]
{
    ("N != v (captured 5)",           x => x.N != v),
    ("N == v (captured 5)",           x => x.N == v),
    ("N != 5 (literal)",              x => x.N != 5),
    ("N == none (captured null)",     x => x.N == none),
    ("N != none (captured null)",     x => x.N != none),
    ("N == null (literal)",           x => x.N == null),
    ("N == M (both nullable)",        x => x.N == x.M),
    ("N != M (both nullable)",        x => x.N != x.M),
    ("N == Id (nullable vs non)",     x => x.N == x.Id),
    ("N != Id (nullable vs non)",     x => x.N != x.Id),
    ("!(N == v)",                     x => !(x.N == v)),
    ("!(N != v)",                     x => !(x.N != v)),
    ("!(N > v)",                      x => !(x.N > v)),
    ("!(N > 5)",                      x => !(x.N > 5)),
    ("N > v",                         x => x.N > v),
    ("!(N >= v)",                     x => !(x.N >= v)),
    ("!(N == 5 && Name == a)",        x => !(x.N == 5 && x.Name == a)),
    ("!(N == 5 || Name == a)",        x => !(x.N == 5 || x.Name == a)),
    ("N != 5 || Name == a",           x => x.N != 5 || x.Name == a),
    ("!(!(N == 5))",                  x => !!(x.N == 5)),
    ("ids.Contains(N)",               x => ids.Contains(x.N)),
    ("!ids.Contains(N)",              x => !ids.Contains(x.N)),
    ("idsWithNull.Contains(N)",       x => idsWithNull.Contains(x.N)),
    ("!idsWithNull.Contains(N)",      x => !idsWithNull.Contains(x.N)),
    ("!intIds.Contains(Id)",          x => !intIds.Contains(x.Id)),
    ("Name != a",                     x => x.Name != a),
    ("Name == noName (captured null)",x => x.Name == noName),
    ("Name != noName (captured null)",x => x.Name != noName),
    ("!(Name == a)",                  x => !(x.Name == a)),
    ("Req != a (non-nullable string col)", x => x.Req != a),
    ("Flag == true (bool?)",          x => x.Flag == true),
    ("Flag != true (bool?)",          x => x.Flag != true),
    ("!(Flag == true)",               x => !(x.Flag == true)),
    ("B (bool)",                      x => x.B),
    ("!B (bool)",                     x => !x.B),
    ("Name.Contains(a)",              x => x.Name!.Contains(a)),
    ("!Name.Contains(a)",             x => !x.Name!.Contains(a)),
    ("!Name.StartsWith(a)",           x => !x.Name!.StartsWith(a)),
    ("(N != v) == true",              x => (x.N != v) == true),
    ("(N == v) != true",              x => (x.N == v) != true),
    ("Name.Contains(noName)",         x => x.Name!.Contains(noName!)),
    ("!Name.Contains(noName)",        x => !x.Name!.Contains(noName!)),
    ("N + 1 != 5",                    x => x.N + 1 != 5),
    ("!(N + 1 == 5)",                 x => !(x.N + 1 == 5)),
    ("D.Value.Year != 2020",          x => x.D!.Value.Year != 2020),
    ("D.Value.Year == 2020",          x => x.D!.Value.Year == 2020),
    ("Name.Length != 3",              x => x.Name!.Length != 3),
    ("N.HasValue",                    x => x.N.HasValue),
    ("!N.HasValue",                   x => !x.N.HasValue),
    ("N.Value == 5",                  x => x.N!.Value == 5),
    ("N.Value != 5",                  x => x.N!.Value != 5),
    ("(N ?? 0) != 5",                 x => (x.N ?? 0) != 5),
    ("!(N < M)",                      x => !(x.N < x.M)),
    ("N > M",                         x => x.N > x.M),
    ("(N > 3 ? 1 : 0) != 1",          x => (x.N > 3 ? 1 : 0) != 1),
    ("(Flag ?? false)",               x => x.Flag ?? false),
    ("Name == a (nullable)",          x => x.Name == a),
    ("a != Name (reversed)",          x => a != x.Name),
    ("none != N (reversed null)",     x => none != x.N),
    ("Id != 5 && N != 5",             x => x.Id != 5 && x.N != 5),
    ("(N != 5 ? 1 : 0) == 1",         x => (x.N != 5 ? 1 : 0) == 1),
    ("N > 3 ? M != 5 : Name == a",    x => x.N > 3 ? x.M != 5 : x.Name == a),
    ("!(N > 3 ? M != 5 : Name == a)", x => !(x.N > 3 ? x.M != 5 : x.Name == a)),
    ("Name.Contains(a) == false",     x => x.Name!.Contains(a) == false),
    ("!(Name.Contains(a) == true)",   x => !(x.Name!.Contains(a) == true)),
    ("Name.Contains(a) != true",      x => x.Name!.Contains(a) != true),
    ("(N == 5) == (M == 5)",          x => (x.N == 5) == (x.M == 5)),
    ("(N == 5) != (M == 5)",          x => (x.N == 5) != (x.M == 5)),
    ("N.ToString() != \"5\"",          x => x.N.ToString() != "5"),
    ("Name.ToString() != a",          x => x.Name!.ToString() != a),
    ("E != Color.Red (enum?)",        x => x.E != Color.Red),
    ("E == noColor (captured null)",  x => x.E == noColor),
    ("Id != none (non-null col)",     x => x.Id != none),
    ("Id == none (non-null col)",     x => x.Id == none),
    ("Name.Length == Name.Length",    x => x.Name!.Length == x.Name.Length),
    ("Name.StartsWith(noName)",       x => x.Name!.StartsWith(noName!)),
    ("!Name.StartsWith(noName)",      x => !x.Name!.StartsWith(noName!)),
    ("!Name.EndsWith(noName)",        x => !x.Name!.EndsWith(noName!)),
    ("Id == 2 && idsWithNull.Contains(N)", x => x.Id == 2 && idsWithNull.Contains(x.N)),
    ("onlyNull.Contains(N)",          x => onlyNull.Contains(x.N)),
    ("!onlyNull.Contains(N)",         x => !onlyNull.Contains(x.N)),
    ("Req != noName",                 x => x.Req != noName),
    ("Flag == false (bool?)",         x => x.Flag == false),
    ("Flag != false (bool?)",         x => x.Flag != false),
    ("N >= N",                        x => x.N >= x.N),
    ("!(N >= N)",                     x => !(x.N >= x.N)),
    ("(Id > 3 ? N : M) != 5",         x => (x.Id > 3 ? x.N : x.M) != 5),
    ("Parent.Label != a",             x => x.Parent!.Label != a),
    ("Parent.Code != 5 (int)",        x => x.Parent!.Code != 5),
    ("Parent.Code == 0 (int)",        x => x.Parent!.Code == 0),
    ("!Parent.B (bool)",              x => !x.Parent!.B),
    // Task 0 round 4 (3.12.0 plan rev 5)
    ("Parent.Code.ToString() != \"5\"", x => x.Parent!.Code.ToString() != "5"),
    ("Parent.Code.ToString() == \"\"",  x => x.Parent!.Code.ToString() == ""),
    ("!(Id == 2 && Name.Contains(a))", x => !(x.Id == 2 && x.Name!.Contains(a))),
    ("!(Id == 2 || ids.Contains(N))",  x => !(x.Id == 2 || ids.Contains(x.N))),
    ("!(Id == 2 && Name.Contains(noName))", x => !(x.Id == 2 && x.Name!.Contains(noName!))),
    ("(N != 5 ? Id : 0) > 0",          x => (x.N != 5 ? x.Id : 0) > 0),
    ("(N != 5 ? 5 : 0) == v",          x => (x.N != 5 ? 5 : 0) == v),
    ("(Id > 2 ? (N != 5 ? 1 : 0) : 0) == 1", x => (x.Id > 2 ? (x.N != 5 ? 1 : 0) : 0) == 1),
    ("Name.Contains(a) != nullBool",   x => x.Name!.Contains(a) != nullBool),
    ("Name.Contains(a) == nullBool",   x => x.Name!.Contains(a) == nullBool),
    ("(N == 5) == (N == 5)",           x => (x.N == 5) == (x.N == 5)),
    ("Flag == (N > 5)",                x => x.Flag == (x.N > 5)),
    // Round 6: `!` of a column, a side that reads no row, a conditional's predicate branch.
    ("(N == v) == !B",                 x => (x.N == v) == !x.B),
    ("!B == (M > 3)",                  x => !x.B == (x.M > 3)),
    ("(N == v) == !t (captured)",      x => (x.N == v) == !t),
    ("Flag != (bool?)!t",              x => x.Flag != (bool?)!t),
    ("!Flag != true",                  x => !x.Flag != true),
    ("!Flag == null",                  x => !x.Flag == null),
    ("Flag == !Flag2",                 x => x.Flag == !x.Flag2),
    ("!(Id > 2 ? B : false) == true",  x => !(x.Id > 2 ? x.B : false) == true),
    ("(Id > 2 ? (bool?)(N == 5) : Flag) != true", x => (x.Id > 2 ? (bool?)(x.N == 5) : x.Flag) != true),
    ("(Id > 2 ? N == 5 : false) == B", x => (x.Id > 2 ? x.N == 5 : false) == x.B),
};

foreach (var (label, factory) in new (string, Func<Ctx>)[] { ("SqlServer", () => new Ctx(true)), ("Sqlite", () => new Ctx(false)) })
{
    Console.WriteLine($"==================== {label}");
    using var ctx = factory();
    foreach (var (name, pred) in cases)
    {
        string sql;
        try { sql = ctx.Rows.Where(pred).ToQueryString(); }
        catch (Exception ex) { sql = "THREW " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]; }
        var where = sql.Contains("WHERE") ? sql[(sql.IndexOf("WHERE", StringComparison.Ordinal) + 6)..].Replace("\r", "").Replace("\n", " ") : sql.Replace("\r", "").Replace("\n", " ");
        var decls = string.Join("; ", sql.Split('\n').Where(l => l.StartsWith("DECLARE") || l.StartsWith(".param")).Select(l => l.Trim()));
        Console.WriteLine($"{name,-34} | {where}" + (decls.Length > 0 ? $"   [{decls}]" : ""));
    }
    // All(...) — EF translates to NOT EXISTS; show the inner predicate.
    try { Console.WriteLine("All(N > 0) ->                     | " + ctx.Rows.Where(r => ctx.Rows.All(x => x.N > 0)).ToQueryString().Replace("\r", "").Replace("\n", " ")); }
    catch (Exception ex) { Console.WriteLine("All(N > 0) -> THREW " + ex.Message.Split('\n')[0]); }
    // #16: does EF advertise IOrderedQueryable on unordered queries, and does the helper crash?
    IQueryable<Row> w = ctx.Rows.Where(x => x.Id > 0);
    var claims = w is IOrderedQueryable<Row>;
    string helper;
    try { var s = w is IOrderedQueryable<Row> o ? o.ThenBy(x => x.Id) : w.OrderBy(x => x.Id); helper = "ok: " + s.ToQueryString().Replace("\r", "").Replace("\n", " "); }
    catch (Exception ex) { helper = "THREW " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]; }
    Console.WriteLine($"#16 root is IOrderedQueryable: {ctx.Rows is IOrderedQueryable<Row>}; Where(...) is IOrderedQueryable: {claims}; helper: {helper}");
    Console.WriteLine($"#16 runtime type of Where(...): {w.GetType().FullName}");
}

public class Row
{
    public int Id { get; set; }
    public int? N { get; set; }
    public int? M { get; set; }
    public string? Name { get; set; }
    public string Req { get; set; } = "";
    public bool? Flag { get; set; }
    public bool? Flag2 { get; set; }
    public bool B { get; set; }
    public DateTime? D { get; set; }
    public Color? E { get; set; }
    public int? ParentId { get; set; }
    public Parent? Parent { get; set; }
}
public class Parent
{
    public int Id { get; set; }
    public string? Label { get; set; }
    public int Code { get; set; }
    public bool B { get; set; }
}
public enum Color { Red, Green }

public class Ctx(bool sqlServer) : DbContext
{
    public DbSet<Row> Rows => Set<Row>();
    protected override void OnConfiguring(DbContextOptionsBuilder o)
    {
        if (sqlServer) o.UseSqlServer("Server=.;Database=efnull_probe_never_opened;Integrated Security=True;TrustServerCertificate=True");
        else o.UseSqlite("Data Source=:memory:");
    }
    protected override void OnModelCreating(ModelBuilder b) => b.Entity<Row>().Property(r => r.Req).IsRequired();
}
