# Funcular.FunkyOrm Usage Guide

Welcome to the **Funcular.FunkyOrm** usage guide! This document is your "Dummies" book for getting up and running with the framework. It covers everything from basic setup to advanced querying, error handling, and why you might prefer this over the big dogs like Entity Framework or the raw metal of Dapper.

> Looking for the exact boundary of what the query engine translates — which projections, computed-attribute, aggregate, and remote-property constructs work and which throw? See **[Advanced Usage: What Works, What Doesn't](Advanced.md)**.

## Why FunkyORM?

Look, we get it. You've got choices.
*   **Entity Framework**: Great, until you need to debug a generated query that looks like it was written by a caffeinated robot, or until you realize you've accidentally pulled down the entire database because of a lazy-loading mishap.
*   **Dapper**: Fast and furious, but you're back to writing raw SQL strings. If you wanted to write SQL, you'd be a DBA, right? (Just kidding, DBAs are cool).

**Funcular.FunkyOrm** sits in the sweet spot. You get the **speed** of a micro-ORM with the **type safety** and **developer joy** of LINQ.

### The "It Just Works" Philosophy
*   **One Instance Per Connection String**: You create a provider (`SqlServerOrmDataProvider` or `PostgreSqlOrmDataProvider`) with a connection string. That's it. No `DbContext` ceremony, no dependency injection containers required (though you can use them if you want). In concurrent environments like Blazor Server, register the provider as **Scoped** (per-circuit) to avoid cross-circuit transaction leaks.
*   **Auto-Inference**: Name your table `Person` and your class `Person`. We'll figure it out. Name your column `first_name` and your property `FirstName`. We'll figure that out too.
*   **Forgiving Mapping**: Got a property in your class that isn't in the database? We ignore it. Got a column in the database that isn't in your class? We ignore that too. No more crashing because you added a helper property to your view model.

### Modeling Data: The Funcular Way
We have strong opinions on how to model your data for maximum performance and minimal code.

1.  **Canonical Entities**: Create one "source of truth" class for each table (e.g., `Person`). Keep it clean. **Do not** add `[Remote...]` attributes to this class, as that forces JOINs on every query.
2.  **Inheritance for Extensions**: If you need the full `Person` record *plus* some related data (e.g., `EmployerName`), create a derived class (e.g., `PersonDetail : Person`) and add the attributes there.
3.  **DTOs for "Wide" Tables**: If you have a table with 50 columns but only need 3 for a dropdown list, **do not inherit**. Create a small DTO (e.g., `PersonSummary`), add `[Table("Person")]`, and include only the 3 properties you need. This keeps your queries lean.
4.  **Minimize Attributes**: Don't clutter your code with `[Column]` or `[Key]` unless our conventions fail you. If you do add them, please comment *why*.

### Naming Conventions & Canonical Entities
We strongly encourage following standard naming conventions. If you do, FunkyORM works with zero configuration.

**Crucial Rule**: Avoid reusing class names for different purposes (e.g., a `Department` entity and a `Department` DTO). This confuses the relationship inference engine.
*   **Canonical Entities**: Have one "source of truth" class for each table (e.g., `Department`).
*   **Suffixes**: For other models, use suffixes like `Dto`, `View`, `ViewModel`, `Summary` (e.g., `DepartmentDto`, `DepartmentView`).

*   **Tables**: We automatically match your class name to a table name.
    *   **Class**: `Person` -> **Table**: `Person`, `person`, `PERSON`
    *   **Class**: `PersonAddress` -> **Table**: `PersonAddress`, `person_address`, `PERSON_ADDRESS`
*   **Columns**: We automatically match your property name to a column name, ignoring case and underscores.
    *   **Property**: `FirstName` -> **Column**: `FirstName`, `first_name`, `FIRST_NAME`, `First_Name`
*   **Primary Keys**: We automatically detect your primary key if it follows one of these patterns (case-insensitive):
    *   `Id`
    *   `{ClassName}Id` (e.g., `PersonId`)
    *   `{ClassName}_Id` (e.g., `Person_Id`)
    *   Any property with the `[Key]` attribute.
    *   Any property with `[DatabaseGenerated(DatabaseGeneratedOption.Identity)]`.

If your database schema deviates from these conventions (e.g., legacy databases), you can easily override them using standard Data Annotations (`[Table]`, `[Column]`, `[Key]`).

> **Important**: All mapping attributes (`[Key]`, `[Table]`, `[Column]`, `[NotMapped]`, `[Timestamp]`, `[DatabaseGenerated]`) **must** come from `System.ComponentModel.DataAnnotations` or `System.ComponentModel.DataAnnotations.Schema`. Attributes with the same name from other frameworks (e.g., Entity Framework Core) are **not** recognized.

### Timestamp / RowVersion Columns
Properties decorated with `[Timestamp]` or `[DatabaseGenerated(DatabaseGeneratedOption.Computed)]` are automatically excluded from INSERT and UPDATE statements. This means SQL Server `rowversion`/`timestamp` columns work out of the box:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("MyEntity")]
public class MyEntity
{
    [Key]
    public long Id { get; set; }
    public string Name { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; }
}
```

### Reserved Words
FunkyORM automatically handles reserved words for both SQL Server and PostgreSQL. If you have a table named `User` or a column named `Order`, the ORM automatically encloses them in the appropriate syntax for your database. You don't need to do anything special in your code.

```csharp
// This works even if 'User' is a reserved word
var user = new User { Name = "Paul", Order = 1 };
provider.Insert(user);
```

**Generated SQL (SQL Server):**
```sql
INSERT INTO [User] ([Name], [Order]) VALUES (@p0, @p1);
SELECT SCOPE_IDENTITY();
```

**Generated SQL (PostgreSQL):**
```sql
INSERT INTO "User" (name, "Order") VALUES (@Name, @Order)
RETURNING "Key"
```

> **Note:** PostgreSQL is case-sensitive with quoted identifiers. Only reserved words are quoted; non-reserved column names remain unquoted (and are folded to lowercase by PostgreSQL). SQL Server uses `[brackets]`, PostgreSQL uses `"double-quotes"`.

### Comparison: FunkyORM vs. The World

| Feature | Entity Framework | Dapper | FunkyORM |
| :--- | :--- | :--- | :--- |
| **Setup** | Heavy (DbContext, Config) | Light | **Lightest** |
| **Query Style** | LINQ | SQL Strings | **LINQ** |
| **Performance** | Good (if tuned) | Excellent | **Excellent** |
| **Mapping** | Strict (needs config) | Manual/Strict | **Forgiving/Auto** |
| **SQL Injection** | Protected | Manual Parameterization | **Protected** |
| **Vibe** | Enterprise Java | Hardcore Metal | **Cool Jazz** |

### Performance vs. Entity Framework (rows/second)

Funcular ORM is designed to be fast. In our benchmarks, it performed significantly faster than Entity Framework Core 7 in single-row write operations, and on-par with EF in read operations. Below are some sample results from our benchmarking tests, showing rows per second for various operations.

![FunkyORM-Performance](https://raw.githubusercontent.com/FuncularLabs/Funcular.FunkyOrm/refs/heads/master/Funcular.Data.Orm.SqlServer/Images/funcular-orm-entity-framework-performance-comparison.png)

## Avoiding Pitfalls

While FunkyORM is designed to be forgiving, there are a few things to watch out for:

1.  **Case Sensitivity in Manual Mapping**: If you use `[Column("Name")]` attributes, ensure the name matches the database column exactly if your database collation is case-sensitive. While our auto-discovery is case-insensitive, explicit attributes are taken literally in some contexts.

2.  **Schema Changes**: If you change your database schema (e.g., rename a column), remember to restart your application. FunkyORM caches schema information at startup for performance. It won't know about schema changes until the application restarts or you manually clear the cache.

3.  **Complex LINQ Queries**: We support a subset of LINQ optimized for SQL generation. Highly complex in-memory operations (like custom method calls inside a `.Where()` clause) may not translate to SQL. Keep your predicates simple and focused on data filtering.

4.  **Nullable Properties in LINQ Expressions**: FunkyORM automatically unwraps nullable types during SQL translation. Do **not** use `.Value` or `.HasValue` in your LINQ expressions—the ORM translates them literally as SQL column names (e.g., `HospitalId.Value`), which produces invalid SQL. Use the nullable property directly instead.

    ```csharp
    // GOOD: Use the nullable property directly
    var results = provider.Query<Person>()
        .Where(p => p.HospitalId == 5)
        .ToList();

    // BAD: .Value generates invalid SQL column name "HospitalId.Value"
    var results = provider.Query<Person>()
        .Where(p => p.HospitalId.Value == 5)
        .ToList();
    ```

    The same goes for `ToString()`: `p.HospitalId.Value.ToString()` throws `NullableExpressionException`. On an integer property like this one, write `p.HospitalId.ToString()`, whose text is `""` when the value is null; on other types `ToString()` isn't translated (see [The `LIKE` Clause](#the-like-clause)).

    When using `List<int>.Contains()` with a nullable entity property, cast the list to match the nullable type rather than unwrapping the property:

    ```csharp
    var hospitalIds = new List<int> { 1, 2, 3 };

    // GOOD: Cast the list to match the nullable property type
    var nullableIds = hospitalIds.Cast<int?>().ToList();
    var results = provider.Query<Person>()
        .Where(p => nullableIds.Contains(p.HospitalId))
        .ToList();

    // BAD: Unwrapping the property to match the list
    var results = provider.Query<Person>()
        .Where(p => hospitalIds.Contains(p.HospitalId.Value))
        .ToList();
    ```

---

## Getting Started

### 1. Setup
Install the `Funcular.Data.Orm` NuGet package (both SQL Server and PostgreSQL providers are included), then instantiate the provider for your database.

**SQL Server:**
```csharp
using Funcular.Data.Orm.SqlServer;

var connectionString = "Data Source=localhost;Initial Catalog=funky_db;Integrated Security=True;TrustServerCertificate=True;";
var provider = new SqlServerOrmDataProvider(connectionString)
{
    Log = s => Console.WriteLine(s) 
};
```

**PostgreSQL:**
```csharp
using Funcular.Data.Orm.PostgreSql;

var connectionString = "Host=localhost;Port=5432;Database=funky_db;Username=myuser;Password=mypassword";
var provider = new PostgreSqlOrmDataProvider(connectionString)
{
    Log = s => Console.WriteLine(s) 
};
```

> **Tip:** Both providers support the same LINQ query API. Your entity classes and query code are portable between them.

### 2. Your Entities
Just plain old C# classes (POCOs).

```csharp
// Maps to table "Person" or "person" automatically
public class Person
{
    // Maps to "Id", "id", "PersonId", "person_id", etc.
    // No [Key] attribute needed - "Id" is standard convention.
    public int Id { get; set; } 
    
    // Maps to "FirstName", "first_name", "FIRST_NAME"
    public string FirstName { get; set; }
    
    public string LastName { get; set; }
    
    // Nullable types supported? You bet.
    public DateTime? Birthdate { get; set; }
    
    // Not in the DB? No problem. It's ignored automatically.
    public string FullName => $"{FirstName} {LastName}";
}

// Example of automatic snake_case to PascalCase mapping
// Table: "user_role" or "UserRole"
public class UserRole
{
    // Column: "user_role_id" or "UserRoleId"
    public int UserRoleId { get; set; }
    
    // Column: "role_name" or "RoleName"
    public string RoleName { get; set; }
}
```

If your names don't match our conventions, use attributes:
```csharp
/// <summary>
/// Table name 'tbl_Employees' does not match class name 'Employee'.
/// </summary>
[Table("tbl_Employees")]
public class Employee
{
    /// <summary>
    /// Column 'emp_id' does not match property 'EmployeeId' or standard conventions.
    /// </summary>
    [Column("emp_id")]
    [Key] // Explicitly mark the primary key if we can't guess it
    public int EmployeeId { get; set; }
}
```

---

## CRUD Operations

### Insert

FunkyORM supports inserting entities with various primary key types (Identity `int`/`long`, `Guid`, `string`).

#### Basic Insert (Identity Column)

When inserting an entity with an Identity column (auto-incrementing integer), the ORM automatically retrieves the new ID and populates the entity.

```csharp
var person = new Person { FirstName = "Jane", LastName = "Doe" };

// Option 1: Basic Insert (returns object)
var idObj = provider.Insert(person); 

// Option 2: Generic Insert (returns typed ID)
int newId = provider.Insert<Person, int>(person);

// The entity is also updated automatically:
Console.WriteLine(person.Id); // Outputs the new ID (e.g., 42)
```

#### Guid and String Primary Keys

FunkyORM supports non-identity primary keys, such as client-generated `Guid`s or `string` IDs.

```csharp
// Guid Primary Key
var log = new Log { Id = Guid.NewGuid(), Message = "System started" };
var logId = provider.Insert<Log, Guid>(log); // Returns the Guid

// String Primary Key
var config = new Config { Key = "Theme", Value = "Dark" };
var key = provider.Insert<Config, string>(config); // Returns "Theme"
```

#### Related Entities

For related entities, insert them separately and link them via foreign keys or junction tables:

```csharp
var address = new Address
{
    Line1 = "123 Main St",
    City = "Springfield",
    StateCode = "IL",
    PostalCode = "62704"
};
provider.Insert(address);

var link = new PersonAddress { PersonId = person.Id, AddressId = address.Id };
provider.Insert(link);
```

### Get (by ID)
```csharp
var jane = provider.Get<Person>(person.Id);
```
The key is sent as a parameter, converted to the type of the entity's key property first: an integer key can be given as an `int`, a `long` or its text, and a `Guid` key as a `Guid` or its text. A key that can't be converted throws `ArgumentException` before any query runs.

### Get All (List)
**Warning:** `GetList<T>()` retrieves *every* record in the table. This is great for small lookup tables (e.g., `OrderStatus`, `Country`), but a **very bad idea** for large tables (e.g., `Transaction`, `Log`). For large datasets, use `GetList<T>(predicate)` to filter your results.

```csharp
// Good for small tables
var allStatuses = provider.GetList<OrderStatus>();

// Bad for large tables - don't do this!
// var allLogs = provider.GetList<Log>(); 
```

### Update
```csharp
jane.LastName = "Smith";
provider.Update(jane);
```

### Delete
**Safety First!** Deletes require a transaction, and a delete by predicate is rejected when its predicate plainly matches every row (see [Troubleshooting](#4-a-where-clause-predicate-is-required-for-deletes)). We don't want you accidentally wiping the table.

**Delete by ID:**
```csharp
provider.BeginTransaction();
provider.Delete<Person>(jane.Id);
provider.CommitTransaction();
```

**Delete by Predicate:**
```csharp
provider.BeginTransaction();
try 
{
    provider.Delete<Person>(p => p.Id == jane.Id);
    provider.CommitTransaction();
}
catch 
{
    provider.RollbackTransaction();
    throw;
}
```

---


## Querying (The Fun Part)

This is where FunkyORM shines. You write C#, we write SQL.

### Basic Filtering (`Where`)
**New in v2.0.0**: You can now chain `.Where()` calls!

```csharp
// Simple
var adults = provider.Query<Person>()
    .Where(p => p.Birthdate < DateTime.Now.AddYears(-18))
    .ToList();

// Chained (AND logic)
var maleAdults = provider.Query<Person>()
    .Where(p => p.Birthdate < DateTime.Now.AddYears(-18))
    .Where(p => p.Gender == "Male")
    .ToList();

// Complex Expressions (AND / OR logic)
// You aren't limited to chaining! You can use standard C# operators.
var potentialRisks = provider.Query<Person>()
    .Where(p => p.LastName == "MobBoss" || p.Birthdate > DateTime.Now.AddYears(-18))
    .ToList();
```

### Null comparisons (v3.12.0)

A `Where` selects the rows C# would select, as EF Core does by default, when a value or a column is null. In SQL a
comparison with NULL is unknown, and an unknown row isn't selected, so FunkyORM adds what C# means. In this table,
`hospitalId` holds 5, `none` and `filter` hold null, and `ids` is `new int?[] { 1, 2 }`. The SQL is SQL Server's;
the other providers send the same shapes.

<!-- funky:null-semantics:begin -->
| C# | SQL | Selects |
|---|---|---|
| `p => p.HospitalId == 5` | `person.hospital_id = @p0` | as written |
| `p => p.HospitalId != hospitalId` | `(person.hospital_id != @p0 OR person.hospital_id IS NULL)` | also the people with no hospital |
| `p => p.HospitalId == none` | `person.hospital_id IS NULL` | the people with no hospital |
| `p => p.Id == none` | `1=0` | no one: `Id` is never null |
| `p => !(p.HospitalId == 5)` | `(person.hospital_id != @p0 OR person.hospital_id IS NULL)` | also the people with no hospital |
| `p => !(p.HospitalId > 5)` | `(NOT person.hospital_id > @p0 OR person.hospital_id IS NULL)` | also the people with no hospital |
| `p => p.HospitalId == p.ReferringHospitalId` | `(person.hospital_id = person.referring_hospital_id OR (person.hospital_id IS NULL AND person.referring_hospital_id IS NULL))` | also the people with neither |
| `p => !ids.Contains(p.HospitalId)` | `(NOT person.hospital_id IN (@p0,@p1) OR person.hospital_id IS NULL)` | also the people with no hospital |
| `p => !p.MiddleInitial.Contains("x")` | `(NOT person.middle_initial LIKE @p0 ESCAPE '!' OR person.middle_initial IS NULL)` | also the people with no middle initial |
| `p => p.MiddleInitial.Contains("x") == false` | `(NOT person.middle_initial LIKE @p0 ESCAPE '!' OR person.middle_initial IS NULL)` | the same |
| `p => !p.MiddleInitial.Contains(filter)` | `NOT 1=0` | everyone: a null search value matches no row, so its negation matches every row |
<!-- funky:null-semantics:end -->

What to expect:
- **A column compared by `==` with a constant or a variable that isn't null doesn't change** (`p.HospitalId == 5`,
  `p.HospitalId == hospitalId`). The shapes that change are listed in the Changelog's 3.12.0 entry: among them a `!=`, a `!` and a comparison with null where a
  column can be null, a conditional whose test is one of these, a remote or computed integer's `ToString()`, and a
  `bool` compared with a predicate, `(p.HospitalId == 5) == (p.Id == 1)`, whether or not a column can be null:
  3.11.0 sent this one as `a = b = c`, an error on SQL Server and PostgreSQL, which SQLite and MySQL read as
  `(a = b) = c`, splitting the predicate on the right. It sent a `bool` column compared with a comparison of
  captured values the same way (`x.Active == (status == "active")` as `active = @p0 = @p1`: an error on SQL Server
  and PostgreSQL, no row on SQLite); 3.12.0 sends the comparison's value as one parameter, and the rows change with
  no NULL anywhere.
- **Every string column counts as able to be null**, because FunkyORM doesn't read the schema's `NOT NULL`. So
  `p.LastName != "x"` gains `OR person.last_name IS NULL` even on a required column: the same rows, more SQL.
- **`All` answers as C# does.** `All(p => p.HospitalId > 0)` is `false` when someone has no hospital. For a
  non-nullable property over a SQL NULL it can answer otherwise (see the Changelog's Known issues).
- **Deletes follow the same rows.** `Delete(p => p.HospitalId != 5)` also deletes the people with no hospital, as C#
  would select them. The guard refuses only the plain all-rows shapes: `Delete(p => !p.MiddleInitial.Contains(filter))`
  with `filter == null` is refused, but `Delete(p => p.HospitalId == 5 || p.HospitalId != 5)`, true for every row once
  NULLs count, is accepted and deletes everyone (see the Changelog's Known issues).
- Before 3.12.0 most of these shapes dropped the NULL rows, and some selected other rows too:
  `!p.MiddleInitial.Contains(filter)` with `filter == null` selected no one, `p.Id != none` no one, and
  `(p.HospitalId == 5) == (p.Id == 1)` only the rows the split read let through. On PostgreSQL a comparison with a
  variable holding null failed with error 42883. The Changelog's 3.12.0 entry lists the shapes that change.

### Ordering (`OrderBy`)
```csharp
var sorted = provider.Query<Person>()
    .OrderBy(p => p.LastName)
    .ThenBy(p => p.FirstName)
    .ToList();
```
Use one `OrderBy` and add keys with `ThenBy`/`ThenByDescending`. Since v3.10 a second `OrderBy` throws
`NotSupportedException`; see [Advanced.md §5](Advanced.md#5-supported-linq-operators-v310) for the full list of
translated operators and the rules for operators after `Skip`/`Take`.

### Paging (`Skip` / `Take`)
```csharp
var page2 = provider.Query<Person>()
    .OrderBy(p => p.Id)
    .Skip(10)
    .Take(10)
    .ToList();
```

**Generated SQL (SQL Server):**
```sql
SELECT [Id], [FirstName], [LastName], [Birthdate] 
FROM [Person]
ORDER BY [Id]
OFFSET 10 ROWS
FETCH NEXT 10 ROWS ONLY
```

**Generated SQL (PostgreSQL):**
```sql
SELECT id, first_name, last_name, birthdate 
FROM person
ORDER BY id
LIMIT 10 OFFSET 10
```

### Projections (`Select`)
Reshaping results into an anonymous type, a scalar, or a different DTO is done **in memory**, after materializing the entity — a top-level `Select(...)` to a different shape is not translated to SQL and throws `NotSupportedException`:
```csharp
var names = provider.Query<Person>()
    .Where(p => p.LastName.StartsWith("Sm"))
    .ToList()                                       // materialize Person rows from the DB
    .Select(p => new { p.FirstName, p.LastName });  // reshape client-side (LINQ-to-objects)
```
> **Want the database to return only a subset of columns?** Map a `[Table("person")]` DTO to the same table declaring just the properties you need, and query *that* type directly (the "wide table / column subset" pattern). FunkyORM materializes the type you query, so `Query<PersonSummary>()` selects only its columns.

### Aggregates (`Count`, `Any`, `Max`, etc.)
**Performance Tip**: Always chain these off `.Query<T>()` directly so the database does the work.

```csharp
// GOOD: SQL runs "SELECT COUNT(*)..."
var count = provider.Query<Person>().Count(p => p.Gender == "Female");

// BAD: Pulls ALL records into memory, then counts them. Ouch.
// var count = provider.Query<Person>().Where(...).ToList().Count(); 
```

**Generated SQL:**
```sql
SELECT COUNT(*) 
FROM [Person] 
WHERE [Gender] = @p0
```

**Counting past `int.MaxValue`** (v3.10): `LongCount` works like `Count` and returns a `long`. SQL Server computes
it with `COUNT_BIG(*)`.
```csharp
long total = provider.Query<Person>().LongCount(p => p.Gender == "Female");
```

**`Sum`, `Average`, `Min` and `Max` give LINQ to objects' answers** (v3.11.0). `Average` is read as `SUM` and
`COUNT` and divided in C# in the selector type's arithmetic, so `{1, 1, 2}` averages 1.333…, not 1, and a
`decimal`, `float` or `double` property returns that type. Over no rows, `Sum` is 0 (a nullable selector too), a
nullable or reference `Min`/`Max`/`Average` is `null`, and a non-nullable one throws `InvalidOperationException`.
`Min`/`Max` over an enum (`Max(p => (int)p.Status)` works), a `TimeSpan` (store an integer), a `Guid` (opt in with
`provider.QueryOptions.GuidOrdering = GuidOrdering.Database` to take the database's ordering; on PostgreSQL, which
has no `MIN`/`MAX` over `uuid`, order and read the first row's value instead:
`OrderByDescending(p => p.Uid).Take(1).Select(p => p.Uid).ToList()`)
or, on MySQL and SQLite, a `DateTimeOffset` (store a UTC `DateTime`) throw `NotSupportedException` before any
command is sent. String `Min`/`Max` follow the database's collation.

**Filtering aggregates by computed & remote attributes:** aggregates honor the same view-replacing
attributes as any other query. You can `Count`/`Any`/`All`/`Sum`/`Min`/`Max`/`Average` filtered by a
`[JsonPath]`, `[SqlExpression]`, `[SubqueryAggregate]`, or `[RemoteProperty]`/`[RemoteKey]` — the generated
aggregate SQL includes the JSON/expression resolution and any remote `JOIN` the filter needs.

```csharp
// Count rows whose (remote, JOIN-backed) attribute matches — the aggregate emits the LEFT JOIN it needs.
var recent = provider.Query<CallListQueryRow>()
    .Where(r => r.DateUtcCall >= fromUtc)   // [RemoteProperty] → call_0.call_date
    .Count();
```

> Fixed in **v3.8.2**: before that, a `[RemoteProperty]`/`[RemoteKey]` filter on an aggregate omitted the JOIN
> and failed at the engine. `[JsonPath]`/`[SqlExpression]`/`[SubqueryAggregate]` filters (self-contained, no
> join) were unaffected.

**⚠️ Limitation — reverse (one-to-many) remote filters.** If a `[RemoteKey]`/`[RemoteProperty]` resolves through a
*reverse* join (joining on a child's foreign key, e.g. `Country ← Address ← PersonAddress → Person`), the join
fans the base rows out one-per-child. Filtering **`Count`/`LongCount`/`Sum`/`Average`** (or `All`) by such a property would
therefore return an inflated number, so FunkyORM **throws `NotSupportedException`** instead — aggregate in memory:

```csharp
// ✗ throws NotSupportedException — reverse [RemoteKey] filter would fan out and inflate the count
provider.Query<CountryReverseDetail>().Where(c => c.RelatedPersonId == 5).Count();

// ✓ do this instead — materialize, then aggregate in memory
provider.Query<CountryReverseDetail>().Where(c => c.RelatedPersonId == 5).ToList().Count;

// ✓ still fine: Any/Min/Max over a reverse join (fan-out-safe), and ALL forward (many-to-one) remote filters
provider.Query<CountryReverseDetail>().Where(c => c.RelatedPersonId == 5).Any();
provider.Query<PersonDetail>().Where(p => p.EmployerHqCountryName == "USA").Count();
```

> The guard is **entity-wide**: if a detail class declares *any* reverse remote property, a fan-out-sensitive
> aggregate (`Count`/`LongCount`/`All`/`Sum`/`Average`) filtered by *any* of that class's remote properties is rejected —
> even a forward one. If you need `Count`/`Sum` over a forward remote property, keep it on a detail class that
> doesn't also declare a reverse one.

### Advanced Querying: IN and LIKE
We support powerful filtering patterns that translate to efficient SQL.

#### The `IN` Clause
Want to find records where a value matches one of many options? Use `.Contains()` on a local collection (List, Array, etc.). This translates to a SQL `IN` clause.

```csharp
// Define your list of IDs or values
var targetIds = new[] { 1, 5, 10, 20 };

// Query: SELECT * FROM Person WHERE Id IN (1, 5, 10, 20)
var selectedPeople = provider.Query<Person>()
    .Where(p => targetIds.Contains(p.Id))
    .ToList();

// Works with strings too!
var validStates = new List<string> { "NY", "CA", "TX" };
var coastalPeople = provider.Query<Address>()
    .Where(a => validStates.Contains(a.StateCode))
    .ToList();
```

The item you test must be a property of the entity (`p.Id`, `a.StateCode`). When it doesn't come from the row at
all — `allowedRoles.Contains(currentUser.Role)`, `ids.Contains(request.Id)`, a local or a constant — FunkyORM
answers it before sending the query, exactly as C# would (with the collection's own `Contains` and comparer), and
the condition becomes `1=0` (false) or `NOT 1=0` (true):

```csharp
// Admins see every project; everyone else sees only their own.
// SQL, roughly: WHERE (owner_id = @p0 OR NOT 1=0) for an admin, WHERE (owner_id = @p0 OR 1=0) otherwise.
var projects = provider.Query<Project>()
    .Where(p => p.OwnerId == currentUser.Id || adminRoles.Contains(currentUser.Role))
    .ToList();
```

An item that reads the row any other way — `ids.Contains(p.Address.Id)`, `lengths.Contains(p.Name.Length)`, or
`longIds.Contains(p.Id)` with a `List<long>` against an `int` property — throws `NotSupportedException` before any
query is sent. The same translation serves `Delete`, but a delete refuses a predicate that such an answer makes
always true (`p.OwnerId == id || adminRoles.Contains("admin")` for an admin), or that reads no column at all (see
[Delete by predicate](#4-a-where-clause-predicate-is-required-for-deletes)); the query runs it and returns every row.

#### Chained Queries with Ordering and Paging
You can chain multiple `Where` clauses, apply ordering, and paginate results for efficient data retrieval.

```csharp
// Example: Find users by multiple criteria, ordered by name, with pagination
var firstNames = new[] { "Alice", "Bob", "Charlie" };
var lastNames = new[] { "Smith", "Johnson", "Williams" };

var results = provider.Query<Person>()
    .Where(p => firstNames.Contains(p.FirstName))
    .Where(p => lastNames.Contains(p.LastName))
    .OrderBy(p => p.LastName)
    .Skip(2)
    .Take(3)
    .ToList();
// This efficiently filters, sorts, and pages the data in the database.
```

#### The `LIKE` Clause
Want to search for partial text matches? Use `.Contains()`, `.StartsWith()`, or `.EndsWith()` on a string property. The value is matched literally: `%`, `_` and `[` in it are ordinary characters, not wildcards. The SQL escapes them with `!` and names it with `ESCAPE '!'`.

```csharp
// Query: SELECT * FROM Person WHERE LastName LIKE '%Smith%' ESCAPE '!'
var smiths = provider.Query<Person>()
    .Where(p => p.LastName.Contains("Smith"))
    .ToList();

// Query: SELECT * FROM Person WHERE LastName LIKE 'Mc%' ESCAPE '!'
var scots = provider.Query<Person>()
    .Where(p => p.LastName.StartsWith("Mc"))
    .ToList();

// Query: SELECT * FROM Person WHERE LastName LIKE '%!_%' ESCAPE '!'
// Only the last names that contain an underscore.
var underscored = provider.Query<Person>()
    .Where(p => p.LastName.Contains("_"))
    .ToList();
```

The search value can be a literal, a local, a property of an object you hold (including `this`) or a static
member; FunkyORM reads it before the query. A `null` value matches no row, so under a `!` it matches every row
(`NOT 1=0`, since 3.12.0; a delete refuses that as always true), and a comparison that doesn't read the row
(`filter == null`) is evaluated in C# and
sent as a literal, so an optional filter works:

```csharp
// When filter is null, every row is returned (SQL: WHERE NOT 1=0); otherwise the rows whose LastName contains it.
var people = provider.Query<Person>()
    .Where(p => filter == null || p.LastName.Contains(filter))
    .ToList();
```

Call `Contains`, `StartsWith` and `EndsWith` on a property of the entity. A `Contains` on a value that doesn't come
from the row (`role.Contains("admin")`) is answered before the query, as C# would, except that a null `role` (or a
null search value) matches no row where C# would throw; `StartsWith` and `EndsWith` on anything but an entity
property throw `NotSupportedException` — test the value in C# before the query instead.

`Delete`, `Count`, `Any` and `All` translate these the same way (v3.11.0): a search value that is null, a wildcard
character or a property of an object you hold, and a string `Contains` on a value that doesn't come from the row,
delete exactly the rows the query selects. A delete alone refuses a predicate that reads no column, always holds —
the optional filter above with a null `filter` included — or searches a column for an empty value that nothing else
bounds (`p.LastName.Contains("")` is `LIKE '%%'`, every row with a name): decide the delete in C#,
`if (!string.IsNullOrEmpty(filter)) provider.Delete<Person>(p => p.LastName.Contains(filter));`.

`ToString()` on an integer property (`sbyte` through `ulong`, nullable or not) compares the column's text:
`p => p.Id.ToString() == "42"`, and a null value's text is `""`, as in C#. The text is the invariant form (`-5`),
while .NET formats a number in the current culture (`−5`, with U+2212, under fi-FI), so build the text you compare
it with using `CultureInfo.InvariantCulture` before the query. The comparison is on the cast text, so it can't use
an index on the column; where that matters, parse the text in C# and compare the integer. On a string property,
`ToString()` is the property itself. On a property of any other type (a `char` included), or with an argument
(`ToString("D2")`, `ToString(CultureInfo.InvariantCulture)`, on a string property too), it throws
`NotSupportedException`: compare the property's value instead, computed before the query (for a `Guid` key,
`var id = Guid.Parse(idText);` and then `p => p.Id == id`; on SQLite, see the Changelog's Known issues).

In a predicate, `ToString()` on anything but a property of the entity is translated in one case only: compared with
a constant or another captured value, where the whole comparison doesn't read the row, a core type's `ToString()`
with no argument or a format string (`id.ToString("D2") == "05"`) is evaluated in C# and sent as a literal, like
any other row-independent part. Compared with a column (`a.PostalCode == zip.ToString("D5")`), in a projection, or
on a value the evaluation can't run (your own type's `ToString()`, an `IFormatProvider`, an arithmetic or method
result), it throws `NotSupportedException`: compute the string before the query and compare with that.

```csharp
// Not a.PostalCode == zip.ToString("D5"): that throws.
var postalCode = zip.ToString("D5");
var addresses = provider.Query<Address>()
    .Where(a => a.PostalCode == postalCode)
    .ToList();
```

#### Date Parts and String Length
`Year`, `Month` and `Day` of a `DateTime` property (nullable or not) and `Length` of a string property translate to
the database's own functions, on every provider:

```csharp
// SQL Server: WHERE YEAR(person.birthdate) = @p0 AND (LEN(person.last_name + N'x') - 1) > @p1
// PostgreSQL: WHERE EXTRACT(YEAR FROM person.birthdate) = @p0 AND LENGTH(person.last_name) > @p1
var people = provider.Query<Person>()
    .Where(p => p.Birthdate.Year == 1990 && p.LastName.Length > 3)
    .ToList();
```

Any other member of a member — `p.Birthdate.Hour`, `p.Birthdate.Date`, `p.Birthdate.DayOfWeek`, a part of a
`DateTimeOffset`, a member of a nested object — throws `NotSupportedException` naming the member before the SELECT or
DELETE is built (on a type's first use its columns are discovered first), in `Where`, `Count` and `Delete` alike. Compute the value before the query, or compare the property itself
(`p.Birthdate >= from && p.Birthdate < to` instead of `p.Birthdate.Date == day`). The database counts a string's
length in its own units (characters, not UTF-16 code units), so an emoji counts once where C# counts twice.

### Advanced: Ternary Operators
We support C# ternary operators in query **predicates** — they translate to SQL `CASE` statements:

```csharp
// The ternary becomes a SQL CASE inside the WHERE clause.
// Hoist the cutoff to a local — a method call like DateTime.Now.AddYears(-18) inlined in the
// predicate isn't translatable; a captured value parameterizes cleanly.
var cutoff = DateTime.Now.AddYears(-18);
var adults = provider.Query<Person>()
    .Where(p => (p.Birthdate < cutoff ? "Adult" : "Minor") == "Adult")
    .ToList();
```
> To return a `CASE`-computed **value** as a column, declare a `[SqlExpression]` computed attribute on your entity (it resolves to the `CASE` fragment in `SELECT`/`WHERE`/`ORDER BY`). A top-level `Select(p => new { … })` that computes a value is not supported — see [Projections](#projections-select).

---

## Async Support

We love async! All major operations have an `Async` counterpart.

```csharp
// Async Get
var person = await provider.GetAsync<Person>(1);

// Async List
var people = await provider.GetListAsync<Person>();

// Async Query
var adults = await provider.QueryAsync<Person>(p => p.Age >= 18);

// Async Insert (returns Task<object>)
var idObj = await provider.InsertAsync(newPerson);

// Async Generic Insert (returns Task<TKey>)
int newId = await provider.InsertAsync<Person, int>(newPerson);

// Async Update
await provider.UpdateAsync(existingPerson);

// Async Delete (transaction required!)
provider.BeginTransaction();
try
{
    await provider.DeleteAsync<Person>(p => p.Id == 1);
    provider.CommitTransaction();
}
catch
{
    provider.RollbackTransaction();
    throw;
}
```

### Concurrency & Threading

FunkyORM is safe for concurrent async use in environments like Blazor Server and parallel workflows. Each non-transactional operation automatically receives its own dedicated connection from the ADO.NET connection pool, so concurrent operations on the same provider instance will not interfere with each other.

**Within a transaction**, all operations share a single connection (this is an ADO.NET requirement). As a result, transactional operations **must be awaited sequentially** — do not use `Task.WhenAll` or fire-and-forget patterns inside a transaction scope:

```csharp
// ✅ CORRECT — sequential awaits within a transaction
provider.BeginTransaction();
try
{
    await provider.DeleteAsync<Person>(p => p.Id == 1);
    await provider.InsertAsync<Person, int>(newPerson);  // sequential — safe
    provider.CommitTransaction();
}
catch
{
    provider.RollbackTransaction();
    throw;
}

// ❌ WRONG — throws InvalidOperationException with a clear message
provider.BeginTransaction();
await Task.WhenAll(
    provider.DeleteAsync<Person>(p => p.Id == 1),
    provider.InsertAsync<Person, int>(newPerson)  // concurrent transactional usage
);
```

**Outside of a transaction**, concurrent operations are fully supported:

```csharp
// ✅ CORRECT — each gets its own pooled connection
var people = await Task.WhenAll(
    provider.GetAsync<Person>(1),
    provider.GetAsync<Person>(2),
    provider.GetAsync<Person>(3)
);
```

---

## Advanced Features: Remote Keys & Properties

FunkyORM allows you to flatten your object graph by mapping properties directly to columns in related tables, even if they are several hops away. This eliminates the "N+1 query problem" for simple lookups and makes your code cleaner.

### The "Detail" Entity Pattern (Important!)

Before diving into the attributes, it is crucial to understand **where** to put them.

**Do not put remote attributes on your main entity (e.g., `Person`).**

If you add `[RemoteProperty]` to your main `Person` class, FunkyORM will generate `LEFT JOIN`s for *every single query* involving `Person`, even if you just wanted the ID and Name. This kills performance.

Instead, create a derived class (e.g., `PersonDetail`) specifically for these rich queries.

*   **`Person` (Base)**: Maps to the `person` table. Contains only physical columns. Fast, lightweight, no joins.
*   **`PersonDetail` (Derived)**: Inherits from `Person`. Adds `[RemoteProperty]` and `[RemoteKey]` fields. Used only when you need the extra data.

**All examples below assume you are using this `PersonDetail` pattern.**

We provide three attributes to control this behavior:

### 1. `[RemoteKey]` & `[RemoteProperty]` — Flattening the Graph
**"I want data from a related table without loading the whole object."**

Instead of loading a full object graph (e.g., `Person.Employer.Address.Country`), you can project specific fields directly onto your main entity. This is faster and simpler.

*   **`[RemoteKey]`**: Grabs the **ID** of a distant record (e.g., `EmployerHeadquartersCountryId`).
*   **`[RemoteProperty]`**: Grabs a **Value** (e.g., `EmployerHeadquartersCountryName`).

**Example:**
```csharp
public class PersonDetail : Person
{
    // "Get me the ID of the Country where my Employer is located."
    // Path: Person -> Employer -> Address -> Country
    [RemoteKey(remoteEntityType: typeof(CountryEntity), keyPath: new[] { nameof(CountryEntity.Id) })]
    public int? EmployerHeadquartersCountryId { get; set; }

    // "Get me the Name of that Country."
    [RemoteProperty(remoteEntityType: typeof(CountryEntity), keyPath: new[] { nameof(CountryEntity.Name) })]
    public string EmployerHeadquartersCountryName { get; set; }
}
```

### 2. `[RemoteLink]` — Defining Relationships
**"This property connects me to another table."**

> **Note**: This attribute was introduced in v3.0.1 as `[RemoteLink]`.

Use this when a foreign key property name doesn't follow the standard `[EntityName]Id` convention. It tells the ORM how to "walk" from one table to another.

```csharp
// The property is "EmployerId", but it points to "OrganizationEntity".
[RemoteLink(targetType: typeof(OrganizationEntity))]
public int? EmployerId { get; set; }
```

### Path Resolution & Deep Filtering
FunkyORM automatically finds the shortest path between entities using BFS. You can then use these remote properties directly in LINQ queries. The ORM generates the necessary JOINs for you.

```csharp
// Find people whose employer is in "USA".
// Automatically joins Person -> Organization -> Address -> Country.
var americans = provider.Query<PersonDetail>()
    .Where(p => p.EmployerHeadquartersCountryName == "USA")
    .ToList();
```

**Generated SQL:**
```sql
SELECT 
    [person].[Id], [person].[FirstName], ..., 
    [country_1].[Name] AS [EmployerHeadquartersCountryName]
FROM [person]
LEFT JOIN [organization] [organization_0] ON [person].[employer_id] = [organization_0].[id]
LEFT JOIN [address] [address_0] ON [organization_0].[headquarters_address_id] = [address_0].[id]
LEFT JOIN [country] [country_1] ON [address_0].[country_id] = [country_1].[id]
WHERE [country_1].[Name] = @p0
```

### Populating Related Collections (The "Explicit" Way)
FunkyORM does not support Lazy Loading or automatic collection population. Instead, use `[RemoteKey]` to fetch related data efficiently.

**Scenario**: Populate `Person.AssociatedCountries` (Many-to-Many via `PersonAddress`).

**Step 1: Define a Back-Reference**
Create a derived class for the *child* entity that points back to the *parent*.
```csharp
public class CountryDetail : Country
{
    // "Which Person is this Country linked to?"
    // Path: Country <- Address <- PersonAddress -> Person
    [RemoteKey(remoteEntityType: typeof(Person), keyPath: new[] { nameof(Person.Id) })]
    public int PersonId { get; set; }
}
```

**Step 2: Query by Parent ID**
```csharp
var person = provider.Get<Person>(123);

// Fetch all countries for this person in one query
var countries = provider.Query<CountryDetail>()
    .Where(c => c.PersonId == person.Id)
    .ToList();

person.AssociatedCountries.AddRange(countries);

person.AssociatedCountries.AddRange(countries);
```

**Why is this better?**
*   **No Magic**: You see exactly what queries are running.
*   **No N+1 on the Graph**: You aren't pulling down `Organization` and `Address` objects just to get to `Country`. You jump straight to the data you need.
*   **Flexibility**: You can easily combine multiple sources (e.g., `HomeCountryId`, `BillingCountryId`) into a single collection without complex `Union` queries or massive object graphs.

### Advanced Scenario: Many-to-Many with Rich Relationship Data

Sometimes, the relationship itself has data (e.g., "Is this the Primary address?", "Is this a Home or Work address?"). In FunkyORM, you can map the **Join Table** as an entity and pull data from the related table into it.

**Scenario**: A `Person` has many `Addresses`. The link table `person_address` contains `is_primary` and `address_type_value`. We want to load all addresses for a person, including these details, without manually joining tables.

**Step 1: Define the Enum and Relationship Entity**

```csharp
[Flags]
public enum AddressType
{
    Unspecified = 0,
    Billing = 1,
    Shipping = 2,
    Home = 4,
    Work = 8
}

// Maps to the join table "person_address"
[Table("person_address")]
public class PersonAddressDetail
{
    // 1. Local columns in the join table
    public int PersonId { get; set; }
    
    public bool IsPrimary { get; set; }

    public int? AddressTypeValue { get; set; }

    // 2. Computed property for the Enum Label
    // Projects the numeric value to a comma-separated list of names
    public string AddressTypeLabel 
    {
        get 
        {
            var type = (AddressType)(AddressTypeValue ?? 0);
            return type.ToString().Replace(", ", ","); 
        }
    }

    // 3. The Link to the Address Table
    // This property holds the FK to the Address table.
    // Because it follows the convention "AddressId", the ORM knows it points to "AddressEntity".
    public int AddressId { get; set; }

    // 4. Remote Properties: Pulling actual address data from the Address table
    // We use the AddressId property above to "jump" to the AddressEntity
    
    [RemoteProperty(remoteEntityType: typeof(AddressEntity), keyPath: new[] { nameof(AddressId), nameof(AddressEntity.Line1) })]
    public string Line1 { get; set; }

    [RemoteProperty(remoteEntityType: typeof(AddressEntity), keyPath: new[] { nameof(AddressId), nameof(AddressEntity.City) })]
    public string City { get; set; }

    [RemoteProperty(remoteEntityType: typeof(AddressEntity), keyPath: new[] { nameof(AddressId), nameof(AddressEntity.StateCode) })]
    public string StateCode { get; set; }

    [RemoteProperty(remoteEntityType: typeof(AddressEntity), keyPath: new[] { nameof(AddressId), nameof(AddressEntity.PostalCode) })]
    public string PostalCode { get; set; }
}
```

**Step 2: Define the Parent Entity**

```csharp
public class PersonEntity
{
    public int Id { get; set; }
    public string Name { get; set; }

    // The collection holds the "Detail" objects, which contain both 
    // the relationship data (IsPrimary) and the address data (Line1, City).
    [NotMapped]
    public ICollection<PersonAddressDetail> Addresses { get; set; } = new List<PersonAddressDetail>();
}
```

**Step 3: Query and Populate**

```csharp
var personId = 123;
var person = provider.Get<PersonEntity>(personId);

// Fetch all address details for this person in ONE query.
// The ORM automatically joins person_address -> Address to fill the Remote Properties.
person.Addresses = provider.Query<PersonAddressDetail>()
    .Where(pa => pa.PersonId == personId)
    .ToList();

// Result:
// person.Addresses[0].Line1        -> "123 Main St" (from Address table)
// person.Addresses[0].IsPrimary    -> true          (from person_address table)
// person.Addresses[0].AddressTypeLabel -> "Home,Billing" (Computed from person_address table)
```

### Summary Cheat Sheet

| Attribute | Role | Analogy | Returns |
| :--- | :--- | :--- | :--- |
| **`[RemoteLink]`** | **The Bridge** | "The road to the City starts here." | Nothing (It *is* the link) |
| **`[RemoteKey]`** | **The Coordinates** | "What is the Zip Code of that City?" | An ID (`int`/`Guid`) |
| **`[RemoteProperty]`** | **The View** | "What is the Name of that City?" | A Value (`string`/`date`) |

### Putting it all together

Here is a complete example showing how to handle multiple remote keys to the same target table (e.g., a Person has an Employer in one Country, and a Home Address in another).

```csharp
public class PersonDetail : Person
{
    // ============================================================
    // SCENARIO 1: Employer Location (Long Path)
    // Path: Person -> Organization -> Address -> Country
    // ============================================================

    // 1. The Link: Defines the relationship to Organization
    // (Inherited from Person: [RemoteLink(targetType: typeof(OrganizationEntity))])
    // public int? EmployerId { get; set; }

    // 2. The Distant ID: Uses the link above to get the Employer's Country ID
    // We specify the full path of properties to traverse to avoid ambiguity
    [RemoteKey(remoteEntityType: typeof(CountryEntity), keyPath: new[] {
        nameof(EmployerId), 
        nameof(OrganizationEntity.HeadquartersAddressId), 
        nameof(AddressEntity.CountryId), 
        nameof(CountryEntity.Id) })]
    public int? EmployerCountryId { get; set; }

    // 3. The Distant Value: Uses the link above to get the Employer's Country Name
    [RemoteProperty(remoteEntityType: typeof(CountryEntity), keyPath: new[] {
        nameof(EmployerId), 
        nameof(OrganizationEntity.HeadquartersAddressId), 
        nameof(AddressEntity.CountryId), 
        nameof(CountryEntity.Name) })]
    public string EmployerCountryName { get; set; }


    // ============================================================
    // SCENARIO 2: Home Location (Short Path)
    // Path: Person -> Address -> Country
    // ============================================================

    // 1. The Link: Defines the relationship to Address
    [RemoteLink(targetType: typeof(AddressEntity))]
    public int? HomeAddressId { get; set; }

    // 2. The Distant ID: Uses the link above to get the Home Country ID
    // We specify the path here too, ensuring we use HomeAddressId, not EmployerId
    [RemoteKey(remoteEntityType: typeof(CountryEntity), keyPath: new[] {
        nameof(HomeAddressId), 
        nameof(AddressEntity.CountryId), 
        nameof(CountryEntity.Id) })]
    public int? HomeCountryId { get; set; }

    // 3. The Distant Value: Home Country Name
    [RemoteProperty(remoteEntityType: typeof(CountryEntity), keyPath: new[] {
        nameof(HomeAddressId), 
        nameof(AddressEntity.CountryId), 
        nameof(CountryEntity.Name) })]
    public string HomeCountryName { get; set; }
}
```

### Comparison: Achieving "Superpowers" in Other Frameworks

To appreciate why `[RemoteProperty]` and `[RemoteKey]` are "superpowers," let's look at what it takes to achieve the same result—getting a flattened property from a related table—in other popular frameworks.

**Goal**: Get a `Person` object with their `EmployerCountryName` populated.

#### 1. Entity Framework Core
You have two main options, both with trade-offs:

**Option A: Include Everything (The "Heavy" Way)**
You load the entire object graph. This is easy to write but bad for performance if you only need one field.
```csharp
var person = context.People
    .Include(p => p.Employer)
        .ThenInclude(e => e.Address)
            .ThenInclude(a => a.Country)
    .FirstOrDefault(p => p.Id == 1);

// Access: person.Employer.Address.Country.Name
// Downside: You just loaded 3 extra objects into memory.
```

**Option B: Projection (The "Verbose" Way)**
You project into a DTO or anonymous type. This is efficient but requires writing a new class or losing your entity type.
```csharp
var personDto = context.People
    .Select(p => new PersonDto 
    {
        Id = p.Id,
        Name = p.Name,
        // ... manually map every other property ...
        EmployerCountryName = p.Employer.Address.Country.Name
    })
    .FirstOrDefault(p => p.Id == 1);

// Downside: You have to manually map every property you want back.
```

#### 2. Dapper
Dapper is fast, but you are back to writing raw SQL and handling the mapping yourself.

```csharp
var sql = @"
    SELECT p.*, c.Name as EmployerCountryName
    FROM Person p
    LEFT JOIN Organization o ON p.EmployerId = o.Id
    LEFT JOIN Address a ON o.HeadquartersAddressId = a.Id
    LEFT JOIN Country c ON a.CountryId = c.Id
    WHERE p.Id = @Id";

var person = connection.Query<Person, string, Person>(
    sql,
    (person, countryName) => 
    {
        person.EmployerCountryName = countryName;
        return person;
    },
    splitOn: "EmployerCountryName",
    param: new { Id = 1 }
).FirstOrDefault();

// Downside: Raw SQL maintenance, manual join logic, complex mapping code.
```

#### 3. FunkyORM
You define the relationship **once** in your class, and it works automatically everywhere.

```csharp
// In your class definition:
[RemoteProperty(remoteEntityType: typeof(CountryEntity), keyPath: new[] { ..., nameof(CountryEntity.Name) })]
public string EmployerCountryName { get; set; }

// In your code:
var person = provider.Get<PersonDetail>(1); 
// Done. EmployerCountryName is populated. No extra queries, no DTOs.
```

### Performance Considerations: The "Light vs. Heavy" Pattern

While `[RemoteKey]` and `[RemoteProperty]` are powerful, they come with a cost: **Every time you query the entity, the ORM generates the necessary LEFT JOINs.**

If you put these attributes on your main `Person` entity, *every* query for a person will join to `Organization`, `Address`, and `Country`. If you just wanted to check the person's name, this is unnecessary overhead.

**The Solution: Inheritance**

We recommend separating your "Physical" entity (matching the table exactly) from your "Logical" or "Detail" entity (containing the rich remote properties).

1.  **Base Entity (`Person`)**: Contains only the columns physically present in the `person` table. Fast, lightweight, no joins.
2.  **Detail Entity (`PersonDetail`)**: Inherits from `Person` and adds the `[RemoteProperty]` fields.

**Step 1: Define the Base Entity**
```csharp
[Table("person")]
public class Person
{
    public int Id { get; set; }
    public string Name { get; set; }
    
    // Just the link, no remote properties here
    [RemoteLink(targetType: typeof(Organization))]
    public int? EmployerId { get; set; }
}
```

**Step 2: Define the Detail Entity**
```csharp
// Maps to the SAME table
[Table("person")]
public class PersonDetail : Person
{
    // Add the expensive remote properties here
    [RemoteProperty(remoteEntityType: typeof(Country), keyPath: new[] { nameof(Country.Name) })]
    public string EmployerCountryName { get; set; }
}
```

**Step 3: Choose Your Weapon**

```csharp
// Fast: No joins, just reads the person table
var simplePerson = provider.Get<Person>(1);

// Rich: Joins to Organization -> Address -> Country automatically
var richPerson = provider.Get<PersonDetail>(1);
```

This pattern acts like a **SQL View** defined entirely in code. You get the performance of a raw table read when you need it, and the convenience of a rich object graph when you need that, without duplicating code or managing database views.

---

## JSON Column Querying (New in v3.2)

Many modern schemas store semi-structured data in JSON columns (e.g., `NVARCHAR(MAX)` in SQL Server, `jsonb`/`text` in PostgreSQL). Traditionally, querying into these columns required creating SQL views with `JSON_VALUE` or `#>>` expressions. FunkyORM's `[JsonPath]` attribute eliminates this by extracting JSON scalars directly into entity properties — with full LINQ filtering support.

### The `[JsonPath]` Attribute

```csharp
[JsonPath("columnName", "$.json.path")]
public string PropertyName { get; set; }

// With typed extraction:
[JsonPath("columnName", "$.json.path", SqlType = "int")]
public int? PropertyName { get; set; }
```

| Parameter | Description |
|:---|:---|
| `columnName` | The name of the JSON column on the entity's table (e.g., `"metadata"`). |
| `path` | A JSON path expression (e.g., `"$.client.name"`, `"$.risk_level"`). |
| `SqlType` | *(Optional)* SQL type to cast the extracted value to (e.g., `"int"`, `"decimal(10,2)"`). |

### Example: A Project with JSON Metadata

Suppose you have a `project` table with a `metadata` column containing JSON like this:

```json
{
  "priority": "high",
  "tags": ["api", "backend"],
  "client": {
    "name": "Acme Corp",
    "region": "NA"
  },
  "risk_level": 3
}
```

**Step 1: Define the Canonical Entity**

```csharp
[Table("project")]
public class Project
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int OrganizationId { get; set; }
    public decimal? Budget { get; set; }
    public int? Score { get; set; }
    public string Metadata { get; set; }  // Raw JSON column
}
```

**Step 2: Define the Detail Class with `[JsonPath]`**

Follow the same "Detail" pattern used for `[RemoteProperty]` — add JSON extraction to an inherited class, not the canonical entity.

```csharp
[Table("project")]
public class ProjectScorecard : Project
{
    /// <summary>Extract priority from metadata JSON.</summary>
    [JsonPath("metadata", "$.priority")]
    public string Priority { get; set; }

    /// <summary>Extract client name from nested JSON object.</summary>
    [JsonPath("metadata", "$.client.name")]
    public string ClientName { get; set; }

    /// <summary>Extract client region from nested JSON object.</summary>
    [JsonPath("metadata", "$.client.region")]
    public string ClientRegion { get; set; }

    /// <summary>Extract risk level as an integer (typed extraction).</summary>
    [JsonPath("metadata", "$.risk_level", SqlType = "int")]
    public int? RiskLevel { get; set; }
}
```

**Step 3: Query and Filter**

```csharp
// Get by ID — JSON values are extracted automatically
var project = provider.Get<ProjectScorecard>(42);
Console.WriteLine(project.Priority);    // "high"
Console.WriteLine(project.ClientName);  // "Acme Corp"
Console.WriteLine(project.RiskLevel);   // 3

// Filter on JSON values using standard LINQ
var highPriority = provider.Query<ProjectScorecard>()
    .Where(p => p.Priority == "high")
    .ToList();

// Filter on nested JSON values
var emeaProjects = provider.Query<ProjectScorecard>()
    .Where(p => p.ClientRegion == "EMEA")
    .ToList();

// Combine JSON filters with regular column filters
var bigRiskyProjects = provider.Query<ProjectScorecard>()
    .Where(p => p.RiskLevel >= 3)
    .Where(p => p.Budget > 100000)
    .ToList();
```

### Generated SQL

**SQL Server:**
```sql
SELECT project.id, project.name, project.budget, ...,
       JSON_VALUE(project.metadata, '$.priority') AS [Priority],
       JSON_VALUE(project.metadata, '$.client.name') AS [ClientName],
       CAST(JSON_VALUE(project.metadata, '$.risk_level') AS int) AS [RiskLevel]
FROM project
WHERE JSON_VALUE(project.metadata, '$.priority') = @p0
```

**PostgreSQL:**
```sql
SELECT project.id, project.name, project.budget, ...,
       project.metadata #>> '{priority}' AS "Priority",
       project.metadata #>> '{client,name}' AS "ClientName",
       (project.metadata #>> '{risk_level}')::int AS "RiskLevel"
FROM project
WHERE project.metadata #>> '{priority}' = @p0
```

### Combining `[JsonPath]` with `[RemoteProperty]`

Both attribute types work together on the same Detail class. `[RemoteProperty]` generates JOINs to related tables, while `[JsonPath]` extracts values from JSON columns on the *same* table:

```csharp
[Table("project")]
public class ProjectScorecard : Project
{
    // Remote property — joins to the organization table
    [RemoteProperty(typeof(Organization), nameof(OrganizationId), nameof(Organization.Name))]
    public string OrganizationName { get; set; }

    // JSON path — extracts from the metadata column on the project table
    [JsonPath("metadata", "$.priority")]
    public string Priority { get; set; }

    [JsonPath("metadata", "$.client.name")]
    public string ClientName { get; set; }
}

// Both work simultaneously in queries:
var results = provider.Query<ProjectScorecard>()
    .Where(p => p.OrganizationName == "Acme Corp")  // Filter on joined column
    .Where(p => p.Priority == "high")                // Filter on JSON value
    .ToList();
```

### Null Metadata Handling

When the JSON column is `NULL`, all `[JsonPath]` properties on that row resolve to `null` (or `default` for value types). No errors are thrown:

```csharp
var project = provider.Get<ProjectScorecard>(99); // metadata is NULL
Console.WriteLine(project.Priority);   // null
Console.WriteLine(project.RiskLevel);  // null (int? → null)
```

### Performance Considerations

- `[JsonPath]` adds SQL expressions to the SELECT list but does **not** add JOINs — extraction happens on the same table.
- For SQL Server, consider adding computed columns or indexes on frequently filtered JSON paths for performance at scale.
- Like `[RemoteProperty]`, place `[JsonPath]` on Detail classes to avoid unnecessary JSON extraction on every query of the base entity.

---

## Additional Computed Column Attributes (v3.2)

FunkyORM provides three additional attribute types (alongside `[JsonPath]`) designed to eliminate the need for SQL views. All four are implemented as of v3.2.1.

### `[SqlExpression]` — Computed/Expression Columns

Declares a raw SQL expression for a property. Handles `COALESCE`, `CONCAT`, `CASE`, and any other SQL expression. Uses `{PropertyName}` tokens that the framework resolves to fully qualified column references at query time.

```csharp
[Table("project")]
public class ProjectScorecard : ProjectEntity
{
    /// <summary>Falls back to stored score when computed milestone score is null.</summary>
    [SqlExpression("COALESCE({ComputedScore}, {Score})")]
    public int? EffectiveScore { get; set; }

    /// <summary>Lead person full name — provider-specific overrides for string concatenation.</summary>
    [SqlExpression(
        mssql: "CONCAT({LeadFirstName}, CASE WHEN {LeadLastName} IS NOT NULL THEN ' ' + {LeadLastName} ELSE '' END)",
        postgresql: "{LeadFirstName} || COALESCE(' ' || {LeadLastName}, '')")]
    public string LeadName { get; set; }
}
```

**How `{Braces}` work:** The developer writes PascalCase C# property names inside `{}`. The framework resolves them to the correct database column names (snake_case, quoted, aliased) at query time. Text *outside* braces is emitted as-is, so SQL functions (`COALESCE`, `CASE`) and operators (`+`, `||`) work naturally.

**Generated SQL (MSSQL):**
```sql
COALESCE([project].[computed_score], [project].[score]) AS [EffectiveScore]
```

### `[SubqueryAggregate]` — Correlated Scalar Subqueries

Replaces `OUTER APPLY` with attribute-driven correlated subqueries. Generates a scalar subquery in the SELECT list. The constructor takes the child type, its foreign key and the function; `AggregateColumn` (for `Sum`/`Avg`), `ConditionColumn` and `ConditionValue` (for `ConditionalCount`) are properties: `ConditionColumn = …`.

```csharp
[Table("project")]
public class ProjectScorecard : ProjectEntity
{
    /// <summary>Total milestones for this project.</summary>
    [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
        AggregateFunction.Count)]
    public int MilestoneCount { get; set; }

    /// <summary>Completed milestone count (conditional).</summary>
    [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
        AggregateFunction.ConditionalCount,
        ConditionColumn = nameof(ProjectMilestoneEntity.Status),
        ConditionValue = "completed")]
    public int MilestonesCompleted { get; set; }

    /// <summary>Overdue milestone count.</summary>
    [SubqueryAggregate(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
        AggregateFunction.ConditionalCount,
        ConditionColumn = nameof(ProjectMilestoneEntity.Status),
        ConditionValue = "overdue")]
    public int MilestonesOverdue { get; set; }
}
```

**Generated SQL (MSSQL):**
```sql
SELECT project.*,
  (SELECT COUNT(*) FROM project_milestone ms
   WHERE ms.project_id = project.id) AS [MilestoneCount],
  (SELECT COUNT(*) FROM project_milestone ms
   WHERE ms.project_id = project.id AND ms.status = 'completed') AS [MilestonesCompleted],
  (SELECT COUNT(*) FROM project_milestone ms
   WHERE ms.project_id = project.id AND ms.status = 'overdue') AS [MilestonesOverdue]
FROM project
```

### `[JsonCollection]` — JSON Array Projection

Projects child records into a JSON array column, replacing `FOR JSON PATH` (MSSQL) or `json_agg` (PostgreSQL) subqueries. `Columns` and `OrderBy` are properties of the attribute (`Columns = …`, `OrderBy = …`), and the columns are the child table's own: the subquery joins nothing else.

```csharp
[Table("project")]
public class ProjectScorecard : ProjectEntity
{
    /// <summary>Milestones projected as a JSON array.</summary>
    [JsonCollection(typeof(ProjectMilestoneEntity), nameof(ProjectMilestoneEntity.ProjectId),
        Columns = new[] { "Title", "Status", "DueDate", "CompletedDate" },
        OrderBy = "DueDate")]
    public string MilestonesJson { get; set; }

    /// <summary>Notes as a JSON array (the child table's own columns).</summary>
    [JsonCollection(typeof(ProjectNoteEntity), nameof(ProjectNoteEntity.ProjectId),
        Columns = new[] { "Content", "Category", "DateUtcCreated" },
        OrderBy = "DateUtcCreated")]
    public string NotesJson { get; set; }
}
```

**Example output for `MilestonesJson`:**
```json
[
  { "title": "Requirements Gathering", "status": "completed", "due_date": "2025-07-01" },
  { "title": "Design Review", "status": "completed", "due_date": "2025-07-15" },
  { "title": "Development Sprint", "status": "in_progress", "due_date": "2025-08-01" }
]
```

### Attribute Roadmap Summary

| Attribute | Purpose | Status |
|:---|:---|:---|
| `[JsonPath]` | Extract scalar from JSON column | ✅ Stable (v3.2.1) |
| `[SqlExpression]` | Computed column via raw SQL expression | ✅ Stable (v3.2.1) |
| `[SubqueryAggregate]` | Correlated aggregate subquery (COUNT, SUM, conditional) | ✅ Stable (v3.2.1) |
| `[JsonCollection]` | Project child records as JSON array | ✅ Stable (v3.2.1) |

All four follow the "Detail class" pattern. Combined with the existing `[RemoteProperty]` and `[RemoteKey]`, they can replace most SQL views entirely in code.

---

## Troubleshooting & Error Handling

We try to give you helpful error messages. Here are some you might see and what they mean.

### 1. "The table or view for entity 'MyClass' was not found..." (Error 208)
**New in v1.7.0**: If you see this, it means we couldn't find a table that matches your class name.
*   **The Fix**: 
    *   Check your spelling.
    *   Check if the table is in a different schema (e.g., `sales.Person`).
    *   Use the `[Table("ActualTableName")]` attribute on your class.
    *   We automatically check for `MyClass` and `my_class`.

### 2. "The expression ... is not supported in a Where clause"
You tried to do something too fancy in your LINQ query that we can't translate to SQL.
*   **Example**: `Where(p => p.FirstName.GetHashCode() == 123)`
*   **The Fix**: Keep your predicates simple. Use standard operators (`==`, `!=`, `>`, etc.) and supported string methods (`StartsWith`, `Contains`, `EndsWith`) on entity properties. If you need complex C# logic, materialize the data first with `.ToList()` and *then* filter (but be careful with performance!).
*   **"Could not evaluate expression: ..."**: a value FunkyORM reads before the query threw — usually a member of a null captured object (`ids.Contains(request.Id)` with `request == null`), or a lazy collection that throws while it is read. The original exception is the inner one. A null check on the object inside the predicate (`request == null || ids.Contains(request.Id)`) does prevent it: `request == null` is evaluated in C# first, and a disjunction with a true side is sent as `NOT 1=0` without the other side being read, so the query returns every row, as C# would (and a delete of it is refused, because it reads no column). Check the object before building the query when you want the rows filtered. (A null *value* is different: `filter == null || p.Name.Contains(filter)` works, because a null search value matches no row.)

### 3. "Delete operations must be performed within an active transaction"
You tried to call `.Delete()` without starting a transaction.
*   **The Fix**: Wrap it in `provider.BeginTransaction()` and `provider.CommitTransaction()`.

### 4. "A WHERE clause (predicate) is required for deletes"
You passed `null` as the predicate. `Delete(predicate)` and `DeleteAsync(predicate)` also refuse a predicate that plainly matches every row (v3.11.0: `WherePredicateGuard`, the 3.10.0 delete guard renamed; a query runs the same predicate normally and returns every row), before the DELETE is sent, with one of these messages:
*   **"The WHERE clause must reference at least one column of the entity."**: the predicate reads no column (`x => true`, `x => 1 == 1`, `x => someFlag`, `x => ids.Contains(request.Id)`).
*   **"The WHERE clause cannot be a self-referencing column expression."**: it compares a column, or a member of one, or since 3.12.0 any operand that reads the row (a conditional, a call, a template), with itself by `==`, `>=` or `<=`, or negates a `!=`, `<` or `>` of one, whatever the type; a copy through an `[SqlExpression]` alias (a template that is one `{Token}`), a template of the same text, or a part the provider sends as the same SQL, letter case aside outside quoted text (a template that is the dialect's own text for a column's part, such as `[SqlExpression("LENGTH({Name})")]` against `x.Name.Length` on SQLite or PostgreSQL, or two `[JsonPath]` properties on one path), counts as the same operand; a captured value in it is compared as written, never translated a second time, so the same variable is the same value (`x => x.Id == x.Id`, `x => !(x.Id != x.Id)`, `x => x.Code.ToString() == x.Code.ToString()`, `x => x.LastName.Length == x.LastName.Length`, `x => x.N >= x.N` on a nullable, which a query translates as written and which would delete every row but the nulls). `x => x.Id != x.Id` or `x => x.N < x.N` is false for every row, deletes nothing, and is accepted.
*   **"The WHERE clause is trivially true; a non-trivial WHERE clause is required."**: it's always true some other way: an `||` with `true`, with a captured or static `true`, with a comparison of constants and captured values that holds (`x => filter == null || x.LastName == filter` with `filter` null — the optional-filter idiom), or with a `Contains` on a captured string or collection that holds (`x => roles.Contains("admin") || x.OwnerId == me` for an admin); or an `||` with a negated `Contains` over an empty list (`x => x.Id == id || !ids.Contains(x.Id)` with `ids` empty).
*   **"The WHERE clause's Contains, StartsWith or EndsWith has an empty search value, which matches every non-null row; a non-trivial WHERE clause is required."**: `x => x.LastName.Contains(search)` with `search` `""` (an empty search box) sends `LIKE '%%'`, which matches every row with a non-null value; a query runs it, a delete is refused unless another condition bounds it (`x => x.Id == id && x.LastName.Contains(search)` is accepted).

The 3.10.0 messages began with "Delete operation"; match the new text. A delete by a remote property alone (`[RemoteProperty]`, a joined table's column) isn't supported: the DELETE statement has no JOIN, so the database reports an unknown column; 3.10.0 refused it with the "must reference at least one column" message. The 3.10.0 `NotSupportedException` rejections of a delete (a search value that is null, a wildcard or a property of your own class; a `Contains`, `StartsWith`, `EndsWith` or `ToString()` on a value that doesn't read the row; two strings that don't read the row) are gone: the translation now escapes the value, reads the property, and evaluates the `Contains`, the `ToString()` and the comparison in C#, as it does in a query (see the LIKE section above); a `StartsWith`/`EndsWith`/`ToString()` it can't evaluate throws on every path. The empty search value is the one 3.10.0 rejection that stays, as the fourth message above (`InvalidOperationException` now, and only when nothing else bounds the predicate). Comparisons that read a column are never rejected for case or collation; the database's collation decides them, as in 3.9 (and SQLite's `LIKE` folds ASCII case whatever the collation).

A refused delete sends no DELETE. On a type's first use its columns are discovered first, as for a query, so a delete on a table that doesn't exist reports that error, refused predicate or not. The predicate is then translated, so a shape no path translates (`x => x.Child.Id == 1`; a `Contains`, `StartsWith` or `EndsWith` whose receiver reads the row other than as a plain property: `x.Child.Name`, `x.Name.ToString()`, a cast) throws the translator's `NotSupportedException` on a delete exactly as on a query, even where the guard would also refuse it. The second and third messages can also come once the WHERE clause is built, as a backstop: the third for the negated-`Contains` shape, for a `Contains` on a captured collection that holds (the SQL holds a literal `NOT 1=0`), and for a literal `1=1` in the SQL; the second when the whole clause is one operand compared with itself, also in EF's form, which tests the operand's columns for NULL beside the comparison (`(n = n OR (n IS NULL AND n IS NULL))`).
*   **The Fix**: Decide in C# whether to delete, and pass only the column condition: `if (!string.IsNullOrEmpty(filter)) provider.Delete<Person>(x => x.LastName == filter);`. We explicitly block "delete all" operations to prevent catastrophic data loss; a query is not blocked, because selecting every row loses nothing.
    *   **Warning**: These checks catch the plain shapes above, not every predicate that is true for every row (`x => x.Id == 2 || x.Id != 2` is accepted). They evaluate as C# does, and the database can disagree in ways they don't list. Always review your delete logic carefully. If you truly need to truncate a table, use the underlying connection to execute a raw SQL command.
    *   **Note**: Do not look for an `ExecuteNonQuery` method on the provider. We removed it. Using raw SQL execution methods on the provider is considered heresy here. If you must go metal, grab the `Connection` property and do it yourself.

---

## Deprecations (v1.7.0)

We've cleaned up the API a bit.

*   **Obsolete**: `provider.Query<T>(predicate)`
    *   **Why?** It encouraged immediate execution (pulling data into memory) which is often a performance trap.
    *   **Use Instead**: `provider.Query<T>().Where(predicate)`
    *   **Benefit**: This returns an `IQueryable`-like object, allowing you to chain `OrderBy`, `Select`, or `Count` *before* the SQL is executed.

---

Happy Coding! 🚀
