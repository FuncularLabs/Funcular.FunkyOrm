> **Recent Changes**
> * **v1.7.0**: Chained `Where` clauses, enhanced error messaging, API cleanup.
> * **v1.6.0**: Fix for closure predicates, added CI/CD.
> * **v1.5.2**: Fix for unmapped properties.

# Funcular / Funky ORM: a speedy, lambda-powered .NET ORM designed for MSSQL

[![CI](https://github.com/FuncularLabs/Funcular.FunkyOrm/actions/workflows/ci.yml/badge.svg)](https://github.com/FuncularLabs/Funcular.FunkyOrm/actions/workflows/ci.yml)

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

Install the NuGet package `Funcular.Data.Orm` and initialize the provider:

```csharp
using Funcular.Data.Orm.SqlServer;

var provider = new SqlServerOrmDataProvider("Server=.;Database=MyDb;Integrated Security=True;TrustServerCertificate=True;");
```

### Basic Usage

```csharp
// Insert
provider.Insert(new Person { FirstName = "Jane", LastName = "Doe" });

// Query
var adults = provider.Query<Person>()
    .Where(p => p.Age >= 18)
    .OrderBy(p => p.LastName)
    .ToList();
```

## Documentation

For detailed usage examples, performance benchmarks, and a comparison with other ORMs, please see our **[Usage Guide](Usage.md)**.

> **Note**: Check out the **Comparison Table** in the Usage Guide to see how we stack up against Entity Framework and Dapper!
