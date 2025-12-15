# Changelog

All notable changes to this project will be documented in this file.

## [3.0.0-beta3] - 2025-12-12

### Added
- **Guid and String Primary Key Support**: Added full support for `Guid` and `String` primary keys.
- **Non-Identity Key Handling**: The ORM now correctly handles `INSERT` statements for tables with non-identity primary keys (e.g., client-generated Guids), automatically including the PK column in the `INSERT` statement when a value is provided.

## [3.0.0-beta2] - 2025-12-11

### Added
- **Generic Insert Overloads**: Added `Insert<T, TKey>(T entity)` and `InsertAsync<T, TKey>(T entity)` to allow type-safe retrieval of the primary key.
  ```csharp
  var id = provider.Insert<Person, int>(person); // Returns int
  var guid = provider.Insert<Log, Guid>(log);    // Returns Guid
  ```

## [3.0.0-beta1] - 2025-12-10

### Changed
- **Insert Return Type**: The `Insert` method now returns `object` instead of `long`. This is a **breaking change** for code expecting a `long` directly, but enables support for non-integer primary keys.
  - **Migration**: Cast the result to the expected type, or use the new generic overloads (introduced in beta2).

## [2.3.1] - 2025-12-08

### Fixed
- **Parameter Naming in Chained Queries**: Resolved an issue where chained `Where` clauses with array `Contains` would reuse parameter names (e.g., `@p0`), causing SQL Server errors. Parameters now use unique names like `@p__linq__0`, `@p__linq__1`, etc., ensuring correct execution of complex queries.

## [2.2.0] - 2025-12-05

### Fixed
- **Span<T>.Contains Support**: Fixed an issue where `array.Contains(item)` in LINQ queries would fail due to C# 12's preference for `Span<T>.Contains` over `IEnumerable<T>.Contains`. The ORM now properly handles implicit conversions to `Span<T>` and supports both instance and extension method calls.

## [2.1.0] - 2025-12-03

### Added
- **Reserved Word Support**: Added automatic handling for MSSQL reserved words (e.g., `User`, `Key`, `Order`, `Select`).
  - Table and column names that match reserved words are now automatically enclosed in brackets (e.g., `[User]`, `[Order]`) in generated SQL.
  - This applies to `Insert`, `Update`, `Delete`, and `Query` operations.
  - This ensures that legacy databases or schemas using reserved words can be used seamlessly without manual configuration.

## [2.0.0] - 2025-12-02

### Added
- **Chained Where Clauses**: You can now chain multiple `.Where()` calls on a query. They will be combined with `AND`.
  ```csharp
  // Now works!
  provider.Query<Person>().Where(p => p.Age > 18).Where(p => p.Active).ToList();
  ```
- **Enhanced Error Messaging**:
  - **Missing Tables**: If you try to query a table that doesn't exist, we now throw a helpful exception suggesting the expected table name (PascalCase or snake_case) or advising to use the `[Table]` attribute.
  - **Unsupported Expressions**: clearer error messages when using unsupported methods or properties in `Where` and `OrderBy` clauses.

### Changed
- **Deprecation**: The `Query<T>(Expression<Func<T, bool>> predicate)` overload is now marked as `[Obsolete]`.
  - **Reason**: This method caused immediate execution, which often led to performance issues (loading all data into memory before filtering).
  - **Migration**: Change `provider.Query<Person>(p => p.Id == 1)` to `provider.Query<Person>().Where(p => p.Id == 1)`.

### Fixed
- **Parameter Reuse Bug**: Fixed an issue where chaining multiple `.Where()` clauses would reuse parameter names (e.g., `@p0`), causing SQL errors.
- **Performance**: Optimized query generation for predicates involving closures.

---

## [1.6.0]

### Fixed
- Fixed privately reported issue with predicates captured in closures not being translated correctly in some cases.

### Added
- Added CI build and integration tests using MSSQL instance locally and LocalDB in Azure.

---

## [1.5.2]

### Fixed
- Fixed #3: Unmapped properties lacking `[NotMapped]` attribute were included in some SQL statements, causing errors.

---

## [1.5.0]

### Added
- **Ternary Operator Support**: Added support for the ternary operator (`?:`) in `WHERE` clauses, projections (`SELECT`), and `ORDER BY` clauses. These are translated to SQL `CASE` statements.

---

## [1.1.2]

### Documentation
- Explicitly documented immediate vs. deferred execution behaviors of different `Query<T>` overloads.

### Changed
- Centralized Excel logging in performance tests.

---

## [1.1.1]

### Changed
- Minor improvements to integration tests, performance tests, and README.

---

## [1.1.0]

### Added
- **Delete by ID**: Added `Delete<T>(id)` method.
- **Delete Guardrails**: Added additional safety checks to `Delete<T>(predicate)` method.
- **Performance Tests**: Added Entity Framework performance comparisons and charts.

---

## [1.0.0]

### Added
- **RTM Release**: First major release.
- **Delete Methods**: Initial support for delete operations.
- **Performance Enhancements**: General performance tuning.
- **Integration Testing**: Expanded integration test coverage.

---

## [0.9.3]

### Added
- **Async Support**: Added async implementations of public methods (`GetAsync`, `InsertAsync`, etc.) with corresponding test cases.

### Changed
- Reorganized provider members for better code structure.

---

## [0.9.2] - RC-1

### Added
- **Framework Support**: Added support for .NET 4.8.
- **Feature Complete**: Declared feature complete for .NET 8 and NETSTANDARD 2.0.

### Changed
- Code cleanup and dependency compatibility updates.

---

## [0.9.1]

### Added
- **NET Standard Support**: Implemented support for .NET Standard 2.0.
- **Performance Testing**: Added performance testing for update operations.
