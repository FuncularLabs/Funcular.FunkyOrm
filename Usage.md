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

### Reserved Words
FunkyORM automatically handles MSSQL reserved words for you. If you have a table named `User` or a column named `Order`, we will automatically enclose them in brackets (e.g., `[User]`, `[Order]`) in the generated SQL. You don't need to do anything special in your code.

```csharp
// This works even if 'User' is a reserved word in SQL
var user = new User { Name = "Paul", Order = 1 };
provider.Insert(user);
```

**Generated SQL:**
```sql
INSERT INTO [User] ([Name], [Order]) VALUES (@p0, @p1);
SELECT SCOPE_IDENTITY();
```

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

1.  **Reserved Words in Raw SQL**: While we handle reserved words in our generated queries, if you write raw SQL using `ExecuteNonQuery` or similar methods, you are responsible for escaping reserved words yourself.
    ```csharp
    // BAD: 'User' is a reserved word
    provider.ExecuteNonQuery("DELETE FROM User WHERE Id = @Id", new { Id = 1 });

    // GOOD: Enclose it in brackets
    provider.ExecuteNonQuery("DELETE FROM [User] WHERE Id = @Id", new { Id = 1 });
    ```

2.  **Case Sensitivity in Manual Mapping**: If you use `[Column("Name")]` attributes, ensure the name matches the database column exactly if your database collation is case-sensitive. While our auto-discovery is case-insensitive, explicit attributes are taken literally in some contexts.

3.  **Schema Changes**: If you change your database schema (e.g., rename a column), remember to restart your application. FunkyORM caches schema information at startup for performance. It won't know about schema changes until the application restarts or you manually clear the cache.

4.  **Complex LINQ Queries**: We support a subset of LINQ optimized for SQL generation. Highly complex in-memory operations (like custom method calls inside a `.Where()` clause) may not translate to SQL. Keep your predicates simple and focused on data filtering.

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


## Querying (The Fun Part)

This is where FunkyORM shines. You write C#, we write SQL.

### Basic Filtering (`Where`)
**New in v1.7.0**: You can now chain `.Where()` calls!

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

**Generated SQL:**
```sql
SELECT [Id], [FirstName], [LastName], [Birthdate] 
FROM [Person]
ORDER BY [Id]
OFFSET 10 ROWS
FETCH NEXT 10 ROWS ONLY
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

**Generated SQL:**
```sql
SELECT COUNT(*) 
FROM [Person] 
WHERE [Gender] = @p0
```

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
Want to search for partial text matches? Use `.Contains()`, `.StartsWith()`, or `.EndsWith()` on a string property.

```csharp
// Query: SELECT * FROM Person WHERE LastName LIKE '%Smith%'
var smiths = provider.Query<Person>()
    .Where(p => p.LastName.Contains("Smith"))
    .ToList();

// Query: SELECT * FROM Person WHERE LastName LIKE 'Mc%'
var scots = provider.Query<Person>()
    .Where(p => p.LastName.StartsWith("Mc"))
    .ToList();
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

// Async Delete
await provider.DeleteAsync<Person>(p => p.Id == 1);
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

### 1. `[RemoteKey]` — The Distant ID
**"I want the ID of a related record, possibly far away."**

Use this when you want to grab the **Primary Key** (ID) of a related entity without loading the entire object. This is useful for foreign keys that don't exist in your table but exist in a related table (e.g., your Employer's Country ID).

*   **Purpose:** Projects a **Key** (ID) from a distant table into your current entity.
*   **Target:** A property that will hold the distant ID.
*   **Result:** The ORM performs the necessary JOINs to find that specific ID and populates this property.

**Example:**
```csharp
// "I don't have a CountryId column. My Employer has an Address, 
// and that Address has a Country. Get me that Country's ID."
// Defined in PersonDetail (inherits from Person)
[RemoteKey(remoteEntityType: typeof(CountryEntity), keyPath: new[] { nameof(CountryEntity.Id) })]
public int? EmployerHeadquartersCountryId { get; set; }
```

### 2. `[RemoteProperty]` — The Distant Value
**"I want a specific value (like a Name or Date) from a related record."**

Use this when you want to display a piece of information (like a name, description, or date) from a related entity directly on your object, flattening the data structure.

*   **Purpose:** Projects a **Value** (non-key) from a distant table into your current entity.
*   **Target:** A property that will hold the distant value (string, date, etc.).
*   **Result:** The ORM performs the necessary JOINs to find that specific column and populates this property.

**Example:**
```csharp
// "I don't want the whole Country object. Just get me the Name 
// of the Country where my Employer is located."
// Defined in PersonDetail (inherits from Person)
[RemoteProperty(remoteEntityType: typeof(CountryEntity), keyPath: new[] { nameof(CountryEntity.Name) })]
public string EmployerHeadquartersCountryName { get; set; }
```

### 3. `[RemoteLink]` — The Link Builder
**"This property connects me to another table."**

> **⚠️ BREAKING CHANGE NOTICE (v3.0.0-beta3)**
> In versions prior to 3.0.0-beta3 (including beta1 and beta2), this attribute was named `[OrmForeignKey]`. It has been renamed to `[RemoteLink]` to better align with the `RemoteKey` and `RemoteProperty` naming convention. If you are upgrading from an earlier beta, please update your code to use `[RemoteLink]`.

Use this when you have a column in your table that holds the ID of another entity, but the property name doesn't follow the standard `[EntityName]Id` convention. This attribute tells the ORM how to "walk" from one table to another.

*   **Purpose:** Defines the relationship structure (the "edges" of the graph).
*   **Target:** A local column (usually an `int` or `Guid`).
*   **Result:** Does not fetch new data itself; it enables *other* properties to fetch data through it.

**Example:**
```csharp
// Defined in the base Person class (inherited by PersonDetail)
// The property name is just "EmployerId", but it points to the "OrganizationEntity".
// Without this (or the new Smart Inference), the ORM wouldn't know where "EmployerId" goes.
[RemoteLink(targetType: typeof(OrganizationEntity))]
public int? EmployerId { get; set; }
```

### Path Resolution & Ambiguity

When you use `[RemoteKey]` or `[RemoteProperty]`, FunkyORM attempts to find the shortest path from your current entity to the target remote entity using a Breadth-First Search (BFS).

*   **0 Paths Found**: If the system cannot find a way to connect the tables (via Foreign Keys or `[RemoteLink]` attributes), it throws a `PathNotFoundException`.
*   **1 Path Found**: If exactly one shortest path is found, it is used automatically.
*   **> 1 Path Found**: If multiple paths of the same length exist (e.g., a `Person` is linked to `Hospital` via `BirthHospitalId` and `CurrentHospitalId`), the system cannot guess which one you mean. It throws an `AmbiguousMatchException`.

To resolve ambiguity or fix a "Path Not Found" error, use the `[RemoteLink]` attribute on the intermediate properties to explicitly define the relationship.

### Superpower: Deep Filtering

You can use `[RemoteKey]` AND `[RemoteProperty]` properties directly in your LINQ `.Where()` clauses. The ORM will automatically generate the necessary JOINs (even across multiple tables) and filter the results in the database.

**Example:**
```csharp
// Find all people whose employer is headquartered in the country with ID 5.
// The ORM automatically joins Person -> Organization -> Address -> Country.
// Note: We query PersonDetail to access the remote properties.
var people = provider.Query<PersonDetail>()
    .Where(p => p.EmployerHeadquartersCountryId == 5)
    .ToList();

// Find all people whose employer is in "USA".
// This works too!
var americans = provider.Query<PersonDetail>()
    .Where(p => p.EmployerHeadquartersCountryName == "USA")
    .ToList();
```

**Generated SQL (The Magic):**
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

*   **Note**: This allows you to filter by properties that don't even exist on your main table, without writing a single JOIN manually.

### Populating Related Collections (The "Explicit" Way)

Unlike Entity Framework's `.Include()` or Lazy Loading, FunkyORM does not automatically populate collection properties. We believe in **Explicit Intent**: you should know exactly when and how you are fetching data.

However, the `[RemoteKey]` feature makes this incredibly simple. By flattening the graph to get the IDs you need, you can populate complex collections with simple, efficient lookups.

**Scenario**: You want to populate a `Countries` collection on `Person` with every country they are associated with (e.g., via their Employer).

**Step 1: Define the Entity**
```csharp
// We use the Detail entity because we need the RemoteKey to fetch the ID
public class PersonDetail : Person
{
    // The Remote Key gets us the ID automatically
    [RemoteKey(remoteEntityType: typeof(CountryEntity), keyPath: new[] { nameof(CountryEntity.Id) })]
    public int? EmployerHeadquartersCountryId { get; set; }

    // The collection is NOT mapped to the database
    [NotMapped]
    public ICollection<CountryEntity> AssociatedCountries { get; set; } = new List<CountryEntity>();
}
```

**Step 2: Fetch and Populate**
```csharp
// 1. Fetch the person (Remote Key is populated automatically)
var person = provider.Query<PersonDetail>().First(p => p.Id == 123);

// 2. Explicitly fetch the related country using the ID we already have
if (person.EmployerHeadquartersCountryId.HasValue)
{
    var country = provider.Query<CountryEntity>()
        .FirstOrDefault(c => c.Id == person.EmployerHeadquartersCountryId.Value);
    
    if (country != null)
    {
        person.AssociatedCountries.Add(country);
    }
}
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
    
    [Column("is_primary")]
    public bool IsPrimary { get; set; }

    [Column("address_type_value")]
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
    // This property holds the FK to the Address table
    [RemoteLink(targetType: typeof(AddressEntity))]
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
*   **The Fix**: Keep your predicates simple. Use standard operators (`==`, `!=`, `>`, etc.) and supported string methods (`StartsWith`, `Contains`, `EndsWith`). If you need complex C# logic, materialize the data first with `.ToList()` and *then* filter (but be careful with performance!).

### 3. "Delete operations must be performed within an active transaction"
You tried to call `.Delete()` without starting a transaction.
*   **The Fix**: Wrap it in `provider.BeginTransaction()` and `provider.CommitTransaction()`.

### 4. "A WHERE clause (predicate) is required for deletes"
You tried to delete everything, or used a trivial predicate like `x => true` or `x => 1 == 1`. We stopped you.
*   **The Fix**: Provide a valid, non-trivial predicate that references at least one column. We explicitly block "delete all" operations to prevent catastrophic data loss.
    *   **Warning**: While we include rudimentary checks to prevent accidental mass deletes (e.g., blocking `1=1` or `x.Id == x.Id`), we cannot guarantee prevention of all malicious or crafty circumventions (e.g., expressions that evaluate to true for every row). Always review your delete logic carefully. If you truly need to truncate a table, execute a raw SQL command.

---

## Deprecations (v1.7.0)

We've cleaned up the API a bit.

*   **Obsolete**: `provider.Query<T>(predicate)`
    *   **Why?** It encouraged immediate execution (pulling data into memory) which is often a performance trap.
    *   **Use Instead**: `provider.Query<T>().Where(predicate)`
    *   **Benefit**: This returns an `IQueryable`-like object, allowing you to chain `OrderBy`, `Select`, or `Count` *before* the SQL is executed.

---

Happy Coding! 🚀
