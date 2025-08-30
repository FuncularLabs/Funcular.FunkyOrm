# Funcular ORM: a speedy, lambda-powered .NET ORM designed for MSSQL
    
Welcome to Funcular ORM, the micro-ORM designed for **speed**, **simplicity**, and **lambda expression support**. 

If you are tired of ORMs that make you write raw SQL or use name/value pairs to create query predicates, Funcular ORM might be your answer; it's designed for developers who like the ability to use strongly-typed LINQ queries, and who need to get up and running **fast** with minimal setup. Funcular ORM offers:
    
- **Instant Lambda Queries**: No more wrestling with raw SQL to perform complex queries; use familiar C# lambda expressions to craft your SQL statements effortlessly.
- **Parameterized Queries**: All queries are parameterized to protect against SQL injection attacks.
- **Cached Reflection**: Funcular ORM caches reflection results to minimize overhead and maximize performance.
- **Minimal Configuration**: Forget about DbContexts, entity models, or extensive configurations. Just define your entity classes, and you're ready to query.
- **Convention over Configuration**: Sensible defaults for primary key naming conventions (like `id`, `tablename_id`, or `TableNameId`) mean less boilerplate and more productivity.
- **Skip Data Annotations**: Funcular ORM maps case-insensitively by default, ignoring underscores. Data annotation attributes are supported, but *not required* for properties that match the column name, so a FirstName property would automatically be mapped to column FirstName, First\_Name, or first_name.
- **Ignores unmatched properties and columns**: While the ``[NotMapped]`` attribute is supported, it is not required for simple cases like properties that do not map to a database column or vice-versa.
- **Performance without bulk**: Outperforms many alternatives in benchmarks, offering the power you need without the bloat; in our testing, the framework was able to query, instantiate and map and populate over 10,000 rows in 44 to 59 milliseconds. Inserts performed at 3,000 to 4,000 rows per second. Updates are currently row-at-a-time, at about .5 to .6 milliseconds per row (bulk updates are an enhancement consideration).

    
## The General Idea
![FunkyORM-Simplicity-Diagram-Reduced](https://raw.githubusercontent.com/FuncularLabs/Funcular.FunkyOrm/refs/heads/master/Funcular.Data.Orm.SqlServer/Images/FunkyORM-Easy-Ease-Simplicity-Diagram.png)
---
## Usage

Funcular ORM provides a lightweight micro-ORM with lambda-style queries, supporting operations like insert, update, retrieval, and advanced querying with minimal setup. Below are examples of key features, demonstrated using a `Person` entity class (assuming a SQL Server provider and database schema with tables for `Person`, `Address`, and `PersonAddress`).

### Setup

Initialize the ORM provider with a connection string. You can also configure logging for SQL statements.

```csharp
using Funcular.Data.Orm.SqlServer;

var connectionString = "Data Source=localhost;Initial Catalog=funky_db;Integrated Security=SSPI;TrustServerCertificate=true;";
var provider = new SqlServerOrmDataProvider(connectionString)
{
    Log = s => Console.WriteLine(s)  // Optional: Log generated SQL
};
```

### Inserting Entities

Insert a new entity into the database. The ORM handles identity fields implicitly via conventions (name = id or table_id or TableId)_ or annotations ("[Key]").

```csharp
var person = new Person
{
    FirstName = "John",
    LastName = "Doe",
    Birthdate = DateTime.Today.AddYears(-30),
    Gender = "Male",
    UniqueId = Guid.NewGuid(),
    DateUtcCreated = DateTime.UtcNow,
    DateUtcModified = DateTime.UtcNow
};
provider.Insert(person);
// person.Id is now populated with the identity value
```

For related entities, insert separately and link via junction tables.

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

### Retrieving Entities

Retrieve a single entity by its primary key.

```csharp
var retrievedPerson = provider.Get<Person>(person.Id);
Console.WriteLine(retrievedPerson.FirstName);  // Outputs: John
```

Retrieve a list of all entities of a type.

```csharp
var links = provider.GetList<PersonAddress>().ToList();
// links contains all PersonAddress records
```

### Updating Entities

Update an existing entity after modifying its properties.

```csharp
var personToUpdate = provider.Get<Person>(person.Id);
personToUpdate.FirstName = "Updated John";
provider.Update(personToUpdate);

var updatedPerson = provider.Get<Person>(person.Id);
Console.WriteLine(updatedPerson.FirstName);  // Outputs: Updated John
```

### Querying with Where Clauses

Use lambda expressions for filtering.

```csharp
var filteredPersons = provider.Query<Person>()
    .Where(p => p.LastName == "Doe" && p.Gender == "Male")
    .ToList();
// Returns persons matching the criteria
```

Handle null checks.

```csharp
var nullBirthdatePersons = provider.Query<Person>()
    .Where(p => p.Birthdate == null)
    .ToList();
```

Handle deletes (must be in a transaction).
```csharp
provider.BeginTransaction();
// returns the number of rows deleted:
int deleted = _provider.Delete<Person>(x => x.Id == 123);
provider.RollbackTransaction();
// or provider.CommitTransaction();
```

### String Operations in Queries

Support for `StartsWith`, `EndsWith`, and `Contains` on string properties.

```csharp
var startsWithResults = provider.Query<Person>()
    .Where(p => p.LastName.StartsWith("Do"))
    .ToList();

var endsWithResults = provider.Query<Person>()
    .Where(p => p.LastName.EndsWith("e"))
    .ToList();

var containsResults = provider.Query<Person>()
    .Where(p => p.LastName.Contains("oh"))
    .ToList();
```

### Collection Operations in Queries

Use `Contains` for filtering against lists or arrays.

```csharp
var lastNames = new[] { "Doe", "Smith" };
var personsInList = provider.Query<Person>()
    .Where(p => lastNames.Contains(p.LastName))
    .ToList();
```

For GUIDs:

```csharp
var guids = new[] { Guid.NewGuid(), Guid.NewGuid() };
var personsWithGuids = provider.Query<Person>()
    .Where(p => guids.Contains(p.UniqueId))
    .ToList();
```

### DateTime Operations in Queries

Filter by date ranges, OR conditions, or nulls.

```csharp
var fromDate = DateTime.Today.AddYears(-35);
var toDate = DateTime.Today.AddYears(-20);
var personsInRange = provider.Query<Person>()
    .Where(p => p.Birthdate >= fromDate && p.Birthdate <= toDate)
    .ToList();
```

OR conditions:

```csharp
var oldDate = DateTime.Today.AddYears(-100);
var futureDate = DateTime.Today.AddYears(100);
var extremeDates = provider.Query<Person>()
    .Where(p => p.Birthdate <= oldDate || p.Birthdate >= futureDate)
    .ToList();
```

### Ordering Results

Order by one or more properties, ascending or descending.

```csharp
var orderedPersons = provider.Query<Person>()
    .OrderBy(p => p.LastName)
    .ToList();
```

Descending:

```csharp
var descendingPersons = provider.Query<Person>()
    .OrderByDescending(p => p.Birthdate)
    .ToList();
```

Chained ordering with `ThenBy` or `ThenByDescending`:

```csharp
var multiOrdered = provider.Query<Person>()
    .OrderBy(p => p.LastName)
    .ThenBy(p => p.FirstName)
    .ToList();

var multiDescending = provider.Query<Person>()
    .OrderBy(p => p.LastName)
    .ThenByDescending(p => p.MiddleInitial)
    .ToList();
```

### Paging Results

Use `Skip` and `Take` for pagination.

```csharp
var pagedResults = provider.Query<Person>()
    .OrderBy(p => p.LastName)
    .Skip(5)
    .Take(10)
    .ToList();
// Skips first 5, takes next 10
```

### Aggregate Functions

Count matching records.

```csharp
var count = provider.Query<Person>()
    .Count(p => p.Gender == "Male");
// Returns the number of male persons
```

Max and Min on properties.

```csharp
var maxId = provider.Query<Person>()
    .Max(p => p.Id);

var minBirthdate = provider.Query<Person>()
    .Min(p => p.Birthdate);
```

Check existence with `Any`.

```csharp
var hasMales = provider.Query<Person>().Any(p => p.Gender == "Male");
```

Check all match a condition with `All`.

```csharp
var allHaveLastName = provider.Query<Person>()
    .Where(p => p.FirstName == "John")
    .All(p => p.LastName == "Doe");
```

### Transactions

Manage atomic operations with transactions.

Commit example:

```csharp
provider.BeginTransaction();

var transactionalPerson = new Person { /* properties */ };
provider.Insert(transactionalPerson);

// More operations...

provider.CommitTransaction();
// Changes are persisted
```

Rollback example:

```csharp
provider.BeginTransaction();

var tempPerson = new Person { /* properties */ };
provider.Insert(tempPerson);

provider.RollbackTransaction();
// Changes are discarded
```

Multiple operations in a transaction:

```csharp
provider.BeginTransaction();

var personInTx = new Person { /* properties */ };
provider.Insert(personInTx);

var addressInTx = new Address { /* properties */ };
provider.Insert(addressInTx);

var linkInTx = new PersonAddress { PersonId = personInTx.Id, AddressId = addressInTx.Id };
provider.Insert(linkInTx);

provider.CommitTransaction();
```


---
# Details
FunkyORM is designed to be a low-impedance, no-frills interface to MSSQL. It is a micro-ORM designed to fill a niche between heavy solutions like EntityFramework and other micro-ORMs like Dapper. FunkyORM features what we think is a more natural query syntax: Lambda expressions. It supports the most commonly used types and operators used in query criteria.

FunkyORM requires little configuration. Most of its behaviors can be achieved using bare entities with few or no annotations. No contexts and no entity models are needed. Annotations are supported where needs diverge from the most common use cases; just write or generate entities for your tables and start querying.

## Key attributes:
*   **Fast**: Competitive benchmarks when compared to similar alternatives
*   **Small footprint**: completely agnostic to DbContexts, models, joins, and relationships
*   **Easy to use**: Support lambda queries out of the box
*   **Usability over complexity**:
    *   Implements sensible defaults for common PK naming conventions (e.g., any of `id`, `tablename_id`, `TableNameId` are detected automatically)
    *   Supports `[key]` attribute for cases where primary key column names diverge from these conventions
    *   Auto maps matching column names by default, /ignoring case and underscores/
    *   Ignores properties/columns not present in both source table/view and target entity by default
*   **Easily customized**: Supports `System.ComponentModel.DataAnnotations` attributes like `[Table]`, `[Column]`, `[Key]`, `[NotMapped]`
Our goal is to make it easy for developers to get up and running quickly doing what they do 80% of the time, while making it hard for them to shoot themselves in the foot; we avoid automating complex behaviors that should really be thought through more thoroughly, like joins, inclusions, recursions, etc.

# Features
FunkyORM is designed to be a near-drop-in replacement for Entity Framework that is as dependency-free as possible. 

## What It Does:
- GET command (by id / PK)
- SELECT queries
  - Lambdas, with operators `IS NULL , IS NOT NULL , = , <> , > , >= , < , <= , LIKE , AND , OR , IN `
  - C# `.StartsWith,` `EndsWith` and `Contains` invocations on strings
  - C# `.Contains` invocations on arrays (converts these to `IN` clauses with a SqlParameter for each member of the `IN` set)
  - C# `OrderBy, ThenBy, OrderByDescending, ThenByDescending, Skip, Take`
  - C# `Any` (with an optional predicate), `All` (predicate is required)
  - C# Aggregates like `Count, Average, Min, Max` on single column expressions
- UPDATE commands
  - By id / PK
  - By WHERE clause
- INSERT commands
- Observes several System.ComponentModel.DataAnnotations.Schema annotations
  - Table
  - Column
  - Key
  - NotMapped
  - DatabaseGenerated
- DELETE commands\* (Note: deletes can only be performed within a transaction, and they require a valid WHERE clause.)
## What It Does Not Do
- Bulk inserts
- Joins / relationships / foreign-keys / descendants
- Execute query criteria that don't translate to SQL (see the supported operators above)
- Query on columns without corresponding entity properties
- Query on derived expressions or calculated properties (e.g., you can't use expressions like `order.UnitPrice * order.Quantity > 100`)

Funky is made for developers who don't want a bunch of ceremony, and who prefer to do their own relational queries, i.e., get a collection of Customers, project their ids to an array, then get children: `var customerOrders = Orders.Where(x => customerIds.Contains(x.CustomerId))`, instead of letting EntityFramework set you up for an ‘N+1 selects’ problem or inefficient joins.

We made FunkyORM to use ourselves, and we enjoy using it. We hope you do too!



# Quickstart
The easiest way to get started with FunkyORM is to execute the provided scripts to create and populate the integration test database ([funky_db]). Everything needed to do this is provided in the solution. The test project already contains entities and business objects to demonstrate the basic features of Funky. 

### Walkthrough - trying the unit tests is the fastest way to get started:
- **Clone the repository** to your local machine.
- **Connect to a SQL Server** you control.
- **Create the SQL database:** Execute the included integration_test_db script to create the database.
- **Generate mock data:** Execute the provided  integration_test_data script to populate the database with mock data.
  - If you created the database as funky_db on the default SQL instance on localhost and can connect via SSPI/integrated security, you're good to go and you should be able to run the integration tests.
  - If the database is in any other location (different server, SQL instance, database name, etc.), you can edit the connection string in the unit tests, or create and set an environment variable, `FUNKY_CONNECTION`, to point to the server/database you created. The unit tests should recognize this, and it will help prevent you from accidentally checking in SQL credentials.
-  **Set your connection string:** You can edit this in the unit test initialization, or set an environment variable, FUNKY_CONNECTION, which your environment should pick up automatically (may require a restart of VS to refresh environment variables).
  -  Ensure you have either `TrustServerCertificate = true` or `Encrypt = false` in the connection string; see "Important" note below for explanation
-  **Run the unit tests** ...and boom! There you go. 💥

**IMPORTANT**: FunkyORM uses Microsoft.Data.SqlServer, which superseded System.Data.SqlServer as of .NET Core 3.0. _This introduced a breaking change_ with connection strings; if your server does not have a CI-trusted certificate installed, you must include either `TrustServerCertificate = true` or `Encrypt = false` in the connection string, or the connection will fail. See https://stackoverflow.com/questions/17615260/the-certificate-chain-was-issued-by-an-authority-that-is-not-trusted-when-conn for more info on this.

## Next steps:
- Review the contents of the integration tests
- Write a simple lambda-based query just like you would with Entity Framework
