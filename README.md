> **Recent Changes**
> * **v2.3.1**: Fixed parameter naming in chained `Where` clauses to prevent SQL errors.
> * **v2.1.0**: Added support for MSSQL reserved words in table/column names (e.g., `[User]`, `[Order]`).
> * **v2.0.0**: Major release. Breaking change to `Query<T>(predicate)`, safety enhancements for Deletes, chained `Where` clauses.
> * **v1.6.0**: Fix for closure predicates, added CI/CD.


# Funcular / Funky ORM: a speedy, lambda-powered .NET ORM designed for MSSQL

[![NuGet](https://img.shields.io/nuget/v/Funcular.Data.Orm.svg)](https://www.nuget.org/packages/Funcular.Data.Orm)
[![Downloads](https://img.shields.io/nuget/dt/Funcular.Data.Orm.svg)](https://www.nuget.org/packages/Funcular.Data.Orm)
[![CI status](https://img.shields.io/github/actions/workflow/status/FuncularLabs/Funcular.FunkyOrm/ci.yml?branch=master&label=CI)](https://github.com/FuncularLabs/Funcular.FunkyOrm/actions)
[![Tests](https://img.shields.io/github/actions/workflow/status/FuncularLabs/Funcular.FunkyOrm/ci.yml?branch=master&label=Tests)](https://github.com/FuncularLabs/Funcular.FunkyOrm/actions)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)
## Overview

Welcome to **Funcular ORM** (aka FunkyORM), the micro-ORM designed for developers who want the **speed** of a micro-ORM with the **simplicity** and **type safety** of LINQ.

If you're tired of wrestling with raw SQL strings (Dapper) or debugging generated queries from a heavy framework (Entity Framework), FunkyORM is your sweet spot.

### Why FunkyORM?

*   **Instant Lambda Queries**: Write C# lambda expressions, get optimized SQL.
*   **Performance**: Outperforms EF Core in single-row writes and matches it in reads. (See our [Usage Guide](Usage.md) for benchmarks).
*   **Zero Configuration**: No `DbContext`, no mapping files. Just POCOs and a connection string.
*   **Safe**: All queries are parameterized to prevent SQL injection.
*   **Mass Delete Prevention**: Includes safeguards against accidental "delete all" operations (e.g., blocking `1=1`), though this does not guarantee prevention of all crafty circumventions.
*   **Convention over Configuration**: Sensible defaults for primary key naming conventions (like `id`, `tablename_id`, or `TableNameId`) mean less boilerplate and more productivity.
*   **Cached Reflection**: Funcular ORM caches reflection results to minimize overhead and maximize performance.

    
## Getting Started

### 1. Installation

Add the package to your project via the .NET CLI:

```bash
dotnet add package Funcular.Data.Orm
```

### 2. Initialization

Create an instance of `SqlServerOrmDataProvider`. You can do this once and register it as a singleton in your DI container, or create it as needed.

```csharp
using Funcular.Data.Orm.SqlServer;

// Initialize with your connection string
var connectionString = "Server=.;Database=MyDb;Integrated Security=True;TrustServerCertificate=True;";
var provider = new SqlServerOrmDataProvider(connectionString);
```

### 3. Define Your Data Models

FunkyORM is designed to keep your code clean. **You usually don't need attributes.**

By default, we map the **intersection** of your class properties and the database table columns.
*   If your class has a `FullName` property but the table doesn't, we ignore it. No `[NotMapped]` needed.
*   If the table has a `CreatedDate` column but your class doesn't, we ignore it. No errors.

We also infer table names and primary keys automatically.

```csharp
// No attributes needed!
// Maps to table 'Person', 'Persons', 'PERSON', etc.
public class Person
{
    // Automatically detected as Primary Key
    public int Id { get; set; }
    
    // Maps to column 'FirstName', 'first_name', etc.
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public int Age { get; set; }
    
    // Ignored automatically if no matching column exists
    public string FullName => $"{FirstName} {LastName}";
}
```

If you need to deviate from conventions (e.g., mapping `Person` class to `tbl_Users`), you can still use standard `System.ComponentModel.DataAnnotations`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("tbl_Users")]
public class Person
{
    [Key]
    [Column("user_id")]
    public int Id { get; set; }
    // ...
}
```

### 4. Start Querying

```csharp
// Insert a new record
var newPerson = new Person { FirstName = "Jane", LastName = "Doe", Age = 25 };
provider.Insert(newPerson);

// Get by ID
var person = provider.Get<Person>(1);

// Complex Querying with LINQ
var adults = provider.Query<Person>()
    .Where(p => p.Age >= 18)
    .Where(p => p.LastName.StartsWith("D"))
    .OrderByDescending(p => p.Age)
    .Take(10)
    .ToList();
```

## Documentation

For detailed usage examples, performance benchmarks, and a comparison with other ORMs, please see our **[Usage Guide](Usage.md)**.

### Comparison: FunkyORM vs. The World

| Feature | Entity Framework | Dapper | FunkyORM |
| :--- | :--- | :--- | :--- |
| **Setup** | Heavy (DbContext, Config) | Light | **Lightest** |
| **Query Style** | LINQ | SQL Strings | **LINQ** |
| **Performance** | Good (if tuned) | Excellent | **Excellent** |
| **Mapping** | Strict (needs config) | Manual/Strict | **Forgiving/Auto** |
| **SQL Injection** | Protected | Manual Parameterization | **Protected** |
| **Vibe** | Enterprise Java | Hardcore Metal | **Cool Jazz** |
