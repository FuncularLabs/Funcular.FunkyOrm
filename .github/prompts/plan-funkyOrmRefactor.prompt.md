## Plan: Refactor & Polish FunkyOrm for v3.0 Release

I have analyzed the framework and identified several architectural inconsistencies, configuration errors, and code quality issues. This plan outlines the steps to resolve them, ensuring a stable and cleaner v3.0 release.

### Steps
1.  **Fix Project Configuration** [COMPLETED]: Corrected the invalid `net10.0` target in `Funcular.Data.Orm.SqlServer.Tests.DotNet10` to `net9.0` and removed the erroneous `<Compile Remove="Visitors\Visitors\**" />` from the main csproj.
2.  **Establish Core Abstractions** [COMPLETED]: Moved `ISqlDataProvider` and generic interfaces from the SqlServer project to `Funcular.Data.Orm.Core`.
3.  **Clean Up Extensions** [COMPLETED]: Decomposed `ExtensionMethods.cs`. Moved generic utilities (`AddRange`, `Contains`, `ToDictionaryKey`, etc.) to `Funcular.Data.Orm.Core` and kept SQL-specific extensions in the SqlServer project.
4.  **Resolve Technical Debt**: Address the `TODO` in `ISqlDataProvider` regarding returning the primary key on insert, ensuring the API is complete for v3.0.
5.  **Refactor God Class (Time Permitting)**: Extract connection management logic from `SqlServerOrmDataProvider` (2000+ lines) into a dedicated `SqlConnectionManager` to improve testability and SRP.
6.  **Fully enable multi-provider support**: Your refactoring plan must also include:
- Changing ISqlDataProvider to use IDbConnection or DbConnection instead of SqlConnection.
- Removing the Microsoft.Data.SqlClient package reference from the Core project.
- If Step 5 covers extracting the SQL logic, you are on the right track. The next logical step would be to make the Core interface generic.

### Further Considerations
1.  **Breaking Changes**: Moving `ISqlDataProvider` to a new assembly is a breaking change. Is this acceptable for v3.0? (Assumed Yes for a major version).
2.  **Naming**: The solution is `FunkyOrm` but namespaces are `Funcular.Data.Orm`. Do you want to unify this now? (Recommendation: Keep namespaces stable to minimize upgrade pain, unless rebranding).
3.  **Testing**: The `DotNet10` project seems to be a placeholder. Should we retarget it to .NET 9 or remove it? (Recommendation: Retarget to .NET 9).
