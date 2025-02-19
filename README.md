# FunkyORM: The Speedy, Lambda-Powered ORM for MSSQL
    
Welcome to FunkyORM, the micro-ORM that promises **speed**, **simplicity**, and **lambda expression support**—all in one lightweight package! 

If you're tired of the overhead of Entity Framework or the limitations of Dapper when it comes to expressive queries, FunkyORM is your answer. Designed for developers who need to get up and running **fast** with minimal setup, FunkyORM bridges the gap with:
    
- **Instant Lambda Queries**: No more wrestling with raw SQL in Dapper to achieve complex queries; use familiar C# lambda expressions to craft your SQL statements effortlessly.
- **Performance without Bulk**: Outperforms many alternatives in benchmarks, offering the power you need without the bloat.
- **Zero to Query in Seconds**: Minimal configuration means you can start querying your MSSQL databases with just your entity classes. Forget about contexts or extensive model setups!
    
Are you ready to see how easy and powerful database operations can be? Clone the repository, set up the test environment, and experience the efficiency of FunkyORM firsthand.
    
---
# Details
FunkyORM is designed to be a low-impedance, no-frills interface to MSSQL. It is a micro-ORM designed to fill a niche between heavy solutions like EntityFramework and other micro-ORMs like Dapper. FunkyORM features what we think is a more natural query syntax: Lambda expressions. It supports the most commonly used types and operators used in query criteria.

FunkyORM requires little configuration. Most of its behaviors can be achieved using bare entities with few or no annotations. No contexts and no entity models are needed. Annotations are supported where needs diverge from the most common use cases; just write or generate entities for your tables and start querying.

### Key attributes:
*   **Fast**: Competitive benchmarks when compared to similar alternatives
*   **Small footprint**: completely agnostic to DbContexts, models, joins, and relationships
*   **Easy to use**: Support lambda queries out of the box
*   **Usability over power**:
    *   Defines sensible defaults like common PK conventions (e.g., `id`, `tablename_id`, `TableNameId`, etc.)
    *   Supports `[key]` attribute for cases where tables diverge from these conventions
    *   Auto maps matching column names ignoring case and underscores by default
    *   Ignores properties/columns not present in both source table/view and target entity by default
*   **Easily customized**: Supports `System.ComponentModel.DataAnnotations` attributes like `[Table]`, `[Column]`, `[Key]`, `[NotMapped]`
Our goal is to make it easy for developers to get up and running quickly doing what they do 80% of the time, while making it hard for them to shoot themselves in the foot; we avoid automating complex behaviors that should really be thought through more thoroughly, like joins, inclusions, recursions, etc.

# Quickstart

The easiest way to get started with FunkyORM is to execute the provided scripts to create and populate the integration test database ([funky_db]). Everything needed to do this is provided in the solution. The test project already contains entities and business objects to demonstrate the basic features of Funky. 

![image.png](/.attachments/image-6720a675-930c-40b8-9af7-fc722c770264.png)

### Walkthrough
- Clone the repository to your local machine.
- Connect to a SQL Server you control.
- Execute the included integration_test_db script to create the database.
- Execute the integration_test_data script to populate the database with mock data.
- If you created the database as funky_db on the default SQL instance on localhost and can connect via SSPI/integrated security, you're good to go and you should be able to run the integration tests.
- If the database is in any other location (different server, SQL instance, database name, etc.), you can edit the connection string in the unit tests, or create and set an environment variable, `FUNKY_CONNECTION`, to point to the server/database you created. The unit tests should recognize this, and it will help prevent you from accidentally checking in SQL credentials.
- **IMPORTANT**: FunkyORM uses Microsoft.Data.SqlServer, which superseded System.Data.SqlServer as of .NET Core 3.0. _This introduced a breaking change_ with connection strings; if your server does not have a CI-trusted certificate installed, you must include either `TrustServerCertificate = true` or `Encrypt = false` in the connection string, or the connection will fail. See https://stackoverflow.com/questions/17615260/the-certificate-chain-was-issued-by-an-authority-that-is-not-trusted-when-conn for more info on this.

![image.png](/.attachments/image-2f4a3240-18e5-4c8a-80f6-3ba9bd15c6ad.png)

## Next steps:
- Review the contents of the integration tests; the initialization is this simple:
![image.png](/.attachments/image-02e8ff74-221b-41e3-8e97-cfe6c7de2f49.png)
- Write a simple lambda-based query just like you would with Entity Framework:
![image.png](/.attachments/image-d8ebc3c0-3d93-4ede-9a1c-e680d7d2b7e6.png)