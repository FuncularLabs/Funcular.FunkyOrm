# Funcular.FunkyOrm Usage Guide

Welcome to the **Funcular.FunkyOrm** usage guide! This document is your "Dummies" book for getting up and running with the framework. It covers everything from basic setup to advanced querying, error handling, and why you might prefer this over the big dogs like Entity Framework or the raw metal of Dapper.

## Why FunkyORM?

Look, we get it. You've got choices.
*   **Entity Framework**: Great, until you need to debug a generated query that looks like it was written by a caffeinated robot, or until you realize you've accidentally pulled down the entire database because of a lazy-loading mishap.
*   **Dapper**: Fast and furious, but you're back to writing raw SQL strings. If you wanted to write SQL, you'd be a DBA, right? (Just kidding, DBAs are cool).

**Funcular.FunkyOrm** sits in the sweet spot. You get the **speed** of a micro-ORM with the **type safety** and **developer joy** of LINQ.

### The "It Just Works" Philosophy
*   **One Instance Per Connection**: You create a `SqlServerOrmDataProvider` with a connection string. That's it. No `DbContext` ceremony, no dependency injection containers required (though you can use them if you want).
*   **Auto-Inference**: Name your table `Person` and your class `Person`. We'll figure it out. Name your column `first_name` and your property `FirstName`. We'll figure that out too.
*   **Forgiving Mapping**: Got a property in your class that isn't in the database? We ignore it. Got a column in the database that isn't in your class? We ignore that too. No more crashing because you added a helper property to your view model.

### Naming Conventions
We strongly encourage following standard naming conventions. If you do, FunkyORM works with zero configuration.

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

---

## Getting Started

### 1. Setup
Install the NuGet package `Funcular.Data.Orm`. Then, instantiate the provider.

```csharp
using Funcular.Data.Orm.SqlServer;

// Connection string (standard SQL Server format)
var connectionString = "Data Source=localhost;Initial Catalog=funky_db;Integrated Security=True;TrustServerCertificate=True;";

// Create the provider instance
// PRO TIP: You usually only need one of these per connection string. Singleton it!
var provider = new SqlServerOrmDataProvider(connectionString)
{
    // Optional: Hook up logging to see the SQL we generate.
    // Great for debugging or just admiring our handywork.
    Log = s => Console.WriteLine(s) 
};
```

### 2. Your Entities
Just plain old C# classes (POCOs).

```csharp
// Maps to table "Person" or "person" automatically
public class Person
{
    // Maps to "Id", "id", "PersonId", "person_id", etc.
    public int Id { get; set; } 
    
    // Maps to "FirstName", "first_name", "FIRST_NAME"
    public string FirstName { get; set; }
    
    public string LastName { get; set; }
    
    // Nullable types supported? You bet.
    public DateTime? Birthdate { get; set; }
    
    // Not in the DB? No problem. It's ignored automatically.
    public string FullName => $"{FirstName} {LastName}";
}
```

If your names don't match our conventions, use attributes:
```csharp
[Table("tbl_Employees")]
public class Employee
{
    [Column("emp_id")]
    [Key] // Explicitly mark the primary key if we can't guess it
    public int EmployeeId { get; set; }
}
```

---

## CRUD Operations

### Insert
```csharp
var person = new Person { FirstName = "Jane", LastName = "Doe" };
provider.Insert(person);
// person.Id is automatically updated with the new identity value!
```

For related entities, insert separately and link via junction tables:

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

### Get All (List)
```csharp
var allPeople = provider.GetList<Person>();
```

### Update
```csharp
jane.LastName = "Smith";
provider.Update(jane);
```

### Delete
**Safety First!** Deletes require a transaction and a non-trivial WHERE clause. We don't want you accidentally wiping the table.

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

## Async Support

We love async! All major operations have an `Async` counterpart.

```csharp
// Async Get
var person = await provider.GetAsync<Person>(1);

// Async List
var people = await provider.GetListAsync<Person>();

// Async Query
var adults = await provider.QueryAsync<Person>(p => p.Age >= 18);

// Async Insert
await provider.InsertAsync(newPerson);

// Async Update
await provider.UpdateAsync(existingPerson);

// Async Delete
await provider.DeleteAsync<Person>(p => p.Id == 1);
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
```

### Ordering (`OrderBy`)
```csharp
var sorted = provider.Query<Person>()
    .OrderBy(p => p.LastName)
    .ThenBy(p => p.FirstName)
    .ToList();
```

### Paging (`Skip` / `Take`)
```csharp
var page2 = provider.Query<Person>()
    .OrderBy(p => p.Id)
    .Skip(10)
    .Take(10)
    .ToList();
```

### Projections (`Select`)
Don't need the whole object? Just grab what you need.
```csharp
var names = provider.Query<Person>()
    .Select(p => new { p.FirstName, p.LastName })
    .ToList();
```

### Aggregates (`Count`, `Any`, `Max`, etc.)
**Performance Tip**: Always chain these off `.Query<T>()` directly so the database does the work.

```csharp
// GOOD: SQL runs "SELECT COUNT(*)..."
var count = provider.Query<Person>().Count(p => p.Gender == "Female");

// BAD: Pulls ALL records into memory, then counts them. Ouch.
// var count = provider.Query<Person>().Where(...).ToList().Count(); 
```

### Advanced: Ternary Operators
We support C# ternary operators in queries! They translate to SQL `CASE` statements.

```csharp
var status = provider.Query<Person>()
    .Select(p => new 
    { 
        Name = p.FirstName, 
        AgeGroup = p.Birthdate < DateTime.Now.AddYears(-18) ? "Adult" : "Minor" 
    })
    .ToList();
```

---

## Troubleshooting & Error Handling

We try to give you helpful error messages. Here are some you might see and what they mean.

### 1. "The table or view for entity 'MyClass' was not found..." (Error 208)
**New in v2.0.0**: If you see this, it means we couldn't find a table that matches your class name.
*   **The Fix**: 
    *   Check your spelling.
    *   Check if the table is in a different schema (e.g., `sales.Person`).
    *   Use the `[Table("ActualTableName")]` attribute on your class.
    *   We automatically check for `MyClass` and `my_class`.

### 2. "The expression ... is not supported in a Where clause"
You tried to do something too fancy in your LINQ query that we can't translate to SQL.
*   **Example**: `Where(p => p.FirstName.GetHashCode() == 123)`
*   **The Fix**: Keep your predicates simple. Use standard operators (`==`, `!=`, `>`, etc.) and supported string methods (`StartsWith`, `Contains`, `EndsWith`). If you need complex C# logic, materialize the data first with `.ToList()` and *then* filter (but be careful with performance!).

### 3. "Delete operations must be performed within an active transaction"
You tried to call `.Delete()` without starting a transaction.
*   **The Fix**: Wrap it in `provider.BeginTransaction()` and `provider.CommitTransaction()`.

### 4. "A WHERE clause (predicate) is required for deletes"
You tried to delete everything, or used a trivial predicate like `x => true` or `x => 1 == 1`. We stopped you.
*   **The Fix**: Provide a valid, non-trivial predicate that references at least one column. We explicitly block "delete all" operations to prevent catastrophic data loss.
    *   **Warning**: While we include rudimentary checks to prevent accidental mass deletes (e.g., blocking `1=1` or `x.Id == x.Id`), we cannot guarantee prevention of all malicious or crafty circumventions (e.g., expressions that evaluate to true for every row). Always review your delete logic carefully. If you truly need to truncate a table, execute a raw SQL command.

---

## Breaking Changes & Deprecations (v2.0.0)

We've cleaned up the API a bit.

*   **Obsolete**: `provider.Query<T>(predicate)`
    *   **Why?** It encouraged immediate execution (pulling data into memory) which is often a performance trap.
    *   **Use Instead**: `provider.Query<T>().Where(predicate)`
    *   **Benefit**: This returns an `IQueryable`-like object, allowing you to chain `OrderBy`, `Select`, or `Count` *before* the SQL is executed.

---



Happy Coding! 🚀
