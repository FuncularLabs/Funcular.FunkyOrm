# Stored Procedure Execution — Implementation Plan

> **Goal**: Enable developers to execute stored procedures and retrieve scalar values or entity-mapped result sets using the same conventions and mapping pipeline that power `Query<T>`. Parameters may be passed as a class, anonymous object, or param array of tuples/`SqlParam` objects. Add the contract to `IOrmDataProvider`; implement in both MSSQL and PostgreSQL providers.

---

## 1. API Design

### Method Signatures on `IOrmDataProvider`

```csharp
public interface IOrmDataProvider
{
    // ... existing methods ...

    // ?? Result-set execution ??

    /// <summary>
    /// Executes a stored procedure and maps the result set to a collection of <typeparamref name="T"/>.
    /// Procedure name is inferred from <typeparamref name="T"/> using naming conventions.
    /// </summary>
    ICollection<T> ExecProcedure<T>(object parameters = null) where T : class, new();

    /// <summary>
    /// Executes the named stored procedure and maps the result set to a collection of <typeparamref name="T"/>.
    /// </summary>
    ICollection<T> ExecProcedure<T>(string procedureName, object parameters = null) where T : class, new();

    /// <summary>
    /// Executes the named stored procedure with tuple parameters and maps the result set.
    /// </summary>
    ICollection<T> ExecProcedure<T>(string procedureName,
        params (string Name, object Value)[] parameters) where T : class, new();

    /// <summary>
    /// Executes the named stored procedure with SqlParam parameters and maps the result set.
    /// </summary>
    ICollection<T> ExecProcedure<T>(string procedureName,
        params SqlParam[] parameters) where T : class, new();

    // ?? Scalar execution ??

    /// <summary>
    /// Executes a stored procedure and returns a single scalar value.
    /// </summary>
    TResult ExecScalar<TResult>(string procedureName, object parameters = null);

    /// <summary>
    /// Executes a stored procedure and returns a single scalar value, with tuple parameters.
    /// </summary>
    TResult ExecScalar<TResult>(string procedureName,
        params (string Name, object Value)[] parameters);

    /// <summary>
    /// Executes a stored procedure and returns a single scalar value, with SqlParam parameters.
    /// </summary>
    TResult ExecScalar<TResult>(string procedureName,
        params SqlParam[] parameters);

    // ?? Non-query execution ??

    /// <summary>
    /// Executes a stored procedure that performs DML and returns the number of rows affected.
    /// </summary>
    int ExecNonQuery(string procedureName, object parameters = null);

    /// <summary>
    /// Executes a stored procedure that performs DML, with tuple parameters.
    /// </summary>
    int ExecNonQuery(string procedureName,
        params (string Name, object Value)[] parameters);

    /// <summary>
    /// Executes a stored procedure that performs DML, with SqlParam parameters.
    /// </summary>
    int ExecNonQuery(string procedureName,
        params SqlParam[] parameters);

    // ?? Async counterparts ??

    Task<ICollection<T>> ExecProcedureAsync<T>(object parameters = null) where T : class, new();
    Task<ICollection<T>> ExecProcedureAsync<T>(string procedureName, object parameters = null) where T : class, new();
    Task<ICollection<T>> ExecProcedureAsync<T>(string procedureName,
        params (string Name, object Value)[] parameters) where T : class, new();
    Task<ICollection<T>> ExecProcedureAsync<T>(string procedureName,
        params SqlParam[] parameters) where T : class, new();

    Task<TResult> ExecScalarAsync<TResult>(string procedureName, object parameters = null);
    Task<TResult> ExecScalarAsync<TResult>(string procedureName,
        params (string Name, object Value)[] parameters);
    Task<TResult> ExecScalarAsync<TResult>(string procedureName,
        params SqlParam[] parameters);

    Task<int> ExecNonQueryAsync(string procedureName, object parameters = null);
    Task<int> ExecNonQueryAsync(string procedureName,
        params (string Name, object Value)[] parameters);
    Task<int> ExecNonQueryAsync(string procedureName,
        params SqlParam[] parameters);
}
```

### `SqlParam` Type (in `Funcular.Data.Orm.Core`)

```csharp
/// <summary>
/// Represents a named parameter for stored procedure execution.
/// Supports input, output, and input/output directions.
/// </summary>
public class SqlParam
{
    public string Name { get; set; }
    public object Value { get; set; }
    public ParameterDirection Direction { get; set; } = ParameterDirection.Input;
    public DbType? DbType { get; set; }
    public int? Size { get; set; }

    public SqlParam(string name, object value)
    {
        Name = name;
        Value = value;
    }

    public SqlParam(string name, object value, ParameterDirection direction)
    {
        Name = name;
        Value = value;
        Direction = direction;
    }
}
```

Output parameters are supported by passing `SqlParam` instances with `Direction = ParameterDirection.Output` or `ParameterDirection.InputOutput`. After execution, the `Value` property is populated with the output value. This is the only parameter overload that supports output parameters — the tuple and anonymous-object overloads are input-only.

---

## 2. Procedure Name Resolution

Procedure name inference follows the same `IgnoreUnderscoreAndCaseStringComparer` logic used for table/column name resolution:

| Class Name | Matches Procedure |
|:---|:---|
| `SpGetActiveProjects` | `sp_get_active_projects`, `SP_GET_ACTIVE_PROJECTS`, `SpGetActiveProjects` |
| `GetPersonById` | `get_person_by_id`, `GetPersonById`, `GETPERSONBYID` |
| `UspInsertLog` | `usp_insert_log`, `USP_INSERT_LOG`, `UspInsertLog` |

### Resolution Order

1. **Explicit procedure name argument** — `ExecProcedure<T>("my_proc", params)` — used as-is.
2. **`[Procedure]` attribute on the entity class** — `[Procedure("sp_get_active_projects")]`.
3. **Convention inference from class name** — `SpGetActiveProjects` ? query `sys.procedures` (MSSQL) or `pg_proc` (PostgreSQL) using `IgnoreUnderscoreAndCaseStringComparer` to find a match.

### `[Procedure]` Attribute

```csharp
/// <summary>
/// Specifies the stored procedure name for an entity class used with ExecProcedure.
/// Analogous to [Table] for table name overrides.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ProcedureAttribute : Attribute
{
    public string Name { get; }

    public ProcedureAttribute(string name)
    {
        Name = name;
    }
}
```

### Procedure Name Discovery & Caching

At first invocation for a given type `T`, the provider:

1. Checks for `[Procedure("name")]` on `T` — if found, uses it directly (analogous to `[Table]`).
2. Otherwise, normalizes the class name using `IgnoreUnderscoreAndCaseStringComparer` and queries the database catalog for a matching procedure:
   - **MSSQL**: `SELECT name FROM sys.procedures WHERE REPLACE(LOWER(name), '_', '') = @normalized`
   - **PostgreSQL**: `SELECT proname FROM pg_proc p JOIN pg_namespace n ON p.pronamespace = n.oid WHERE n.nspname = 'public' AND prokind = 'p' AND REPLACE(LOWER(proname), '_', '') = @normalized`
3. Caches the result in a `ConcurrentDictionary<Type, string>` (analogous to `_tableNames`).

---

## 3. Parameter Handling

### Three Input Modes

**Mode A — Class / Anonymous Object:**

The framework reflects over the object's public properties and creates one parameter per property. The `@` prefix is added automatically if absent.

```csharp
// Anonymous object
provider.ExecProcedure<ProjectSummary>("sp_get_projects_by_org",
    new { OrganizationId = 5, IsActive = true });

// Typed class
var filter = new ProjectFilter { OrganizationId = 5, IsActive = true };
provider.ExecProcedure<ProjectSummary>("sp_get_projects_by_org", filter);
```

Both produce parameters `@OrganizationId = 5` and `@IsActive = true`. Property-to-parameter name mapping uses the property name directly (no snake_case conversion — procedure parameter names are developer-controlled).

**Mode B — Tuple Array:**

```csharp
provider.ExecProcedure<ProjectSummary>("sp_get_projects_by_org",
    ("@organization_id", 5),
    ("@is_active", true));
```

Each tuple becomes one input parameter. The `@` prefix is added if absent.

**Mode C — `SqlParam` Array (supports output parameters):**

```csharp
var totalCount = new SqlParam("@total_count", null, ParameterDirection.Output) { DbType = DbType.Int32 };

var results = provider.ExecProcedure<ProjectSummary>("sp_get_projects_paged",
    new SqlParam("@page", 1),
    new SqlParam("@page_size", 25),
    totalCount);

int count = (int)totalCount.Value; // populated after execution
```

### Provider-Specific Parameter Conversion

The `OrmDataProvider` base class handles reflection and normalization. Each provider converts the normalized parameters into its native type:

| Provider | Native Parameter Type | Conversion |
|:---|:---|:---|
| **MSSQL** | `SqlParameter` | `new SqlParameter(name, value) { Direction = ... }` |
| **PostgreSQL** | `NpgsqlParameter` | `new NpgsqlParameter(name, value) { Direction = ... }` |

After execution, output `SqlParam.Value` is back-populated from the native parameter's `.Value`.

---

## 4. Result Mapping

### Entity-Mapped Result Sets (`ExecProcedure<T>`)

Result mapping reuses the **existing `MapEntity<T>` / `BuildDataReaderMapper<T>` pipeline** already used by `Query<T>`. This means:

- Column-to-property matching uses `IgnoreUnderscoreAndCaseStringComparer` (e.g., `first_name` ? `FirstName`).
- `[Column("custom_name")]` attributes are respected.
- `[NotMapped]` properties are skipped.
- Nullable types are handled automatically.
- The schema-signature-based mapper cache is reused for performance.

No `DiscoverColumns<T>` call is needed for procedure results — the mapper is built dynamically from the result set's schema at read time, exactly as it already works for `Query<T>`.

### Scalar Results (`ExecScalar<TResult>`)

`ExecuteScalar()` is called on the command; the result is cast to `TResult` via `Convert.ChangeType`. `DBNull` returns `default(TResult)`.

### Non-Query (`ExecNonQuery`)

`ExecuteNonQuery()` is called; the integer rows-affected is returned directly.

---

## 5. PostgreSQL Considerations

PostgreSQL 11+ supports `PROCEDURE` (called via `CALL`) as distinct from `FUNCTION`. Key differences:

| Aspect | MSSQL | PostgreSQL |
|:---|:---|:---|
| **Invocation** | `EXEC proc_name @p1, @p2` | `CALL proc_name(@p1, @p2)` |
| **CommandType** | `CommandType.StoredProcedure` | `CommandType.Text` with `CALL ...` (Npgsql does not support `CommandType.StoredProcedure` for `CALL`) |
| **Result sets** | Procedures return result sets natively | Procedures use `INOUT` refcursor parameters; for tabular results, `FUNCTION` returning `TABLE` or `SETOF` is more natural |
| **Catalog query** | `sys.procedures` | `pg_proc WHERE prokind = 'p'` |

> **Implementation note:** PostgreSQL's `CALL` does not directly return result sets. For `ExecProcedure<T>` to work on PostgreSQL, the provider should detect whether the target is a `PROCEDURE` or a `FUNCTION` and use the appropriate invocation:
> - `PROCEDURE` ? `CALL proc_name(...)` — supports `ExecNonQuery` and output parameters via `INOUT`.
> - `FUNCTION RETURNS TABLE` ? `SELECT * FROM func_name(...)` — supports `ExecProcedure<T>` result-set mapping.
>
> For v1, `ExecProcedure<T>` on PostgreSQL will target `PROCEDURE`s invoked via `CALL`. If the procedure does not return a result set, the provider should throw a clear `NotSupportedException` explaining that PostgreSQL procedures don't return result sets and suggesting use of a function instead. A future enhancement could detect functions automatically. `ExecNonQuery` and `ExecScalar` work with `CALL` directly.

---

## 6. Database Structures for Integration Tests

### MSSQL Stored Procedures (add to `Database/integration_test_db.sql`)

```sql
-- =========================================================================
-- Stored Procedure Test Objects
-- Procedures covering every execution mode: result set, scalar, non-query,
-- output parameters, and multiple parameter styles.
-- =========================================================================

-- Procedure: sp_get_persons_by_gender (result set — basic)
CREATE PROCEDURE sp_get_persons_by_gender
    @gender NVARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT id, first_name, last_name, gender, birthdate
    FROM person
    WHERE gender = @gender;
END;
GO

-- Procedure: sp_get_person_by_id (single-row result set)
CREATE PROCEDURE sp_get_person_by_id
    @person_id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT id, first_name, last_name, gender, birthdate
    FROM person
    WHERE id = @person_id;
END;
GO

-- Procedure: sp_count_persons_by_gender (scalar — returns COUNT)
CREATE PROCEDURE sp_count_persons_by_gender
    @gender NVARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT(*) FROM person WHERE gender = @gender;
END;
GO

-- Procedure: sp_insert_organization (non-query — INSERT, returns rows affected)
CREATE PROCEDURE sp_insert_organization
    @name NVARCHAR(100),
    @headquarters_address_id INT = NULL
AS
BEGIN
    INSERT INTO organization (name, headquarters_address_id)
    VALUES (@name, @headquarters_address_id);
END;
GO

-- Procedure: sp_update_person_gender (non-query — UPDATE, returns rows affected)
CREATE PROCEDURE sp_update_person_gender
    @person_id INT,
    @new_gender NVARCHAR(10)
AS
BEGIN
    UPDATE person SET gender = @new_gender WHERE id = @person_id;
END;
GO

-- Procedure: sp_get_persons_paged (result set + output parameter)
CREATE PROCEDURE sp_get_persons_paged
    @page INT,
    @page_size INT,
    @total_count INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @total_count = COUNT(*) FROM person;

    SELECT id, first_name, last_name, gender, birthdate
    FROM person
    ORDER BY id
    OFFSET (@page - 1) * @page_size ROWS
    FETCH NEXT @page_size ROWS ONLY;
END;
GO

-- Procedure: sp_search_persons (result set — multiple optional parameters)
CREATE PROCEDURE sp_search_persons
    @first_name NVARCHAR(100) = NULL,
    @last_name NVARCHAR(100) = NULL,
    @gender NVARCHAR(10) = NULL,
    @min_birthdate DATE = NULL,
    @max_birthdate DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT id, first_name, last_name, gender, birthdate
    FROM person
    WHERE (@first_name IS NULL OR first_name LIKE '%' + @first_name + '%')
      AND (@last_name IS NULL OR last_name LIKE '%' + @last_name + '%')
      AND (@gender IS NULL OR gender = @gender)
      AND (@min_birthdate IS NULL OR birthdate >= @min_birthdate)
      AND (@max_birthdate IS NULL OR birthdate <= @max_birthdate);
END;
GO

-- Procedure: sp_get_person_full_name (scalar — returns concatenated name)
CREATE PROCEDURE sp_get_person_full_name
    @person_id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CONCAT(first_name, ' ', last_name) FROM person WHERE id = @person_id;
END;
GO

-- Procedure: sp_get_projects_by_org (result set — exercises project table with JSON)
CREATE PROCEDURE sp_get_projects_by_org
    @organization_id INT,
    @min_budget DECIMAL(12,2) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT id, name, organization_id, lead_id, category_id, budget, score, metadata
    FROM project
    WHERE organization_id = @organization_id
      AND (@min_budget IS NULL OR budget >= @min_budget);
END;
GO

-- Procedure: sp_noop (no parameters, no results — edge case)
CREATE PROCEDURE sp_noop
AS
BEGIN
    SET NOCOUNT ON;
    -- intentionally empty
END;
GO
```

### PostgreSQL Procedures (add to the PostgreSQL test setup or a new `Database/integration_test_db_postgres.sql`)

```sql
-- Procedure: sp_update_person_gender (non-query via CALL)
CREATE OR REPLACE PROCEDURE sp_update_person_gender(
    p_person_id INT,
    p_new_gender VARCHAR(10)
)
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE person SET gender = p_new_gender WHERE id = p_person_id;
END;
$$;

-- Procedure: sp_insert_organization (non-query via CALL)
CREATE OR REPLACE PROCEDURE sp_insert_organization(
    p_name VARCHAR(100),
    p_headquarters_address_id INT DEFAULT NULL
)
LANGUAGE plpgsql
AS $$
BEGIN
    INSERT INTO organization (name, headquarters_address_id)
    VALUES (p_name, p_headquarters_address_id);
END;
$$;

-- Procedure: sp_get_persons_paged (INOUT parameter for output)
CREATE OR REPLACE PROCEDURE sp_get_persons_paged(
    p_page INT,
    p_page_size INT,
    INOUT p_total_count INT DEFAULT 0
)
LANGUAGE plpgsql
AS $$
BEGIN
    SELECT COUNT(*) INTO p_total_count FROM person;
    -- Note: CALL does not return result sets in PostgreSQL.
    -- For result-set procedures, use FUNCTION RETURNS TABLE instead.
END;
$$;

-- Procedure: sp_noop (edge case)
CREATE OR REPLACE PROCEDURE sp_noop()
LANGUAGE plpgsql
AS $$
BEGIN
    -- intentionally empty
END;
$$;
```

---

## 7. Integration Tests

### Test Entities for Procedure Results

```csharp
/// <summary>
/// DTO for mapping sp_get_persons_by_gender / sp_search_persons result sets.
/// No [Table] or [Procedure] attribute needed — procedure name is passed explicitly.
/// </summary>
public class PersonProcResult
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Gender { get; set; }
    public DateTime? Birthdate { get; set; }
}

/// <summary>
/// DTO for sp_get_projects_by_org — includes the JSON metadata column.
/// </summary>
public class ProjectProcResult
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int OrganizationId { get; set; }
    public int? LeadId { get; set; }
    public int? CategoryId { get; set; }
    public decimal? Budget { get; set; }
    public int? Score { get; set; }
    public string Metadata { get; set; }
}

/// <summary>
/// Demonstrates [Procedure] attribute for convention-free name resolution.
/// The class name does not match any procedure, so the attribute is required.
/// </summary>
[Procedure("sp_get_persons_by_gender")]
public class GenderFilteredPerson
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Gender { get; set; }
}

/// <summary>
/// Demonstrates convention-based name inference.
/// Class name SpGetPersonById ? matches sp_get_person_by_id via IgnoreUnderscoreAndCaseStringComparer.
/// </summary>
public class SpGetPersonById
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Gender { get; set; }
    public DateTime? Birthdate { get; set; }
}

/// <summary>
/// Parameter class for sp_search_persons — demonstrates class-based parameter passing.
/// </summary>
public class PersonSearchParams
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Gender { get; set; }
    public DateTime? MinBirthdate { get; set; }
    public DateTime? MaxBirthdate { get; set; }
}
```

### Test Matrix

| # | Test Name | Method | Parameter Style | Asserts |
|:---|:---|:---|:---|:---|
| 1 | `ExecProcedure_ResultSet_AnonymousObject` | `ExecProcedure<T>(name, anon)` | Anonymous object | Returns collection, count > 0, properties mapped |
| 2 | `ExecProcedure_ResultSet_TypedClass` | `ExecProcedure<T>(name, class)` | Class instance | Same as #1 |
| 3 | `ExecProcedure_ResultSet_Tuples` | `ExecProcedure<T>(name, tuples)` | Tuple array | Same as #1 |
| 4 | `ExecProcedure_ResultSet_SqlParams` | `ExecProcedure<T>(name, SqlParam[])` | SqlParam array | Same as #1 |
| 5 | `ExecProcedure_ResultSet_NoParams` | `ExecProcedure<T>(name)` | None (optional params) | Returns full table |
| 6 | `ExecProcedure_SingleRow` | `ExecProcedure<T>(name, anon)` | Anonymous object | Returns 1 entity, values match |
| 7 | `ExecProcedure_EmptyResultSet` | `ExecProcedure<T>(name, anon)` | Anonymous object | Returns empty collection, no error |
| 8 | `ExecProcedure_ConventionName` | `ExecProcedure<T>()` | None (name inferred from `SpGetPersonById`) | Procedure found, returns result |
| 9 | `ExecProcedure_AttributeName` | `ExecProcedure<T>()` | None (name from `[Procedure]` on `GenderFilteredPerson`) | Procedure found via attribute |
| 10 | `ExecProcedure_ColumnMapping` | `ExecProcedure<T>(name, anon)` | Anonymous object | `first_name` ? `FirstName`, `last_name` ? `LastName` |
| 11 | `ExecScalar_Int` | `ExecScalar<int>(name, anon)` | Anonymous object | Returns correct count |
| 12 | `ExecScalar_String` | `ExecScalar<string>(name, anon)` | Anonymous object | Returns concatenated name |
| 13 | `ExecScalar_Tuples` | `ExecScalar<int>(name, tuples)` | Tuple array | Same as #11 |
| 14 | `ExecNonQuery_Insert` | `ExecNonQuery(name, anon)` | Anonymous object | Returns 1 row affected, row exists |
| 15 | `ExecNonQuery_Update` | `ExecNonQuery(name, anon)` | Anonymous object | Returns 1 row affected, value changed |
| 16 | `ExecNonQuery_Noop` | `ExecNonQuery(name)` | None | Returns 0 (or -1 with SET NOCOUNT ON), no error |
| 17 | `ExecProcedure_OutputParam` | `ExecProcedure<T>(name, SqlParam[])` | SqlParam with Direction.Output | Result set returned AND output param populated |
| 18 | `ExecProcedure_WithinTransaction` | `ExecProcedure<T>(name, anon)` | Anonymous object (inside `BeginTransaction`) | Uses existing transaction, results correct |
| 19 | `ExecProcedure_ProjectsWithJson` | `ExecProcedure<T>(name, anon)` | Anonymous object | `metadata` column mapped as string, contains JSON |
| 20 | `ExecProcedureAsync_ResultSet` | `ExecProcedureAsync<T>(name, anon)` | Anonymous object | Async version works identically |
| 21 | `ExecScalarAsync_Int` | `ExecScalarAsync<int>(name, anon)` | Anonymous object | Async scalar works |
| 22 | `ExecNonQueryAsync_Update` | `ExecNonQueryAsync(name, anon)` | Anonymous object | Async non-query works |
| 23 | `ExecProcedure_NullParameter` | `ExecProcedure<T>(name, anon)` | Anonymous object with null property | NULL parameter sent correctly, no error |
| 24 | `ExecProcedure_InvalidProcName` | `ExecProcedure<T>("nonexistent")` | N/A | Throws meaningful exception |

### Test Pseudocode Example

```csharp
[TestMethod]
public void ExecProcedure_ResultSet_AnonymousObject()
{
    // Arrange — ensure test data exists
    var testId = InsertTestPerson("Jane", null, "Doe", DateTime.Today.AddYears(-30), "Female", Guid.NewGuid());

    // Act
    var results = _provider.ExecProcedure<PersonProcResult>(
        "sp_get_persons_by_gender",
        new { gender = "Female" });

    // Assert
    Assert.IsNotNull(results);
    Assert.IsTrue(results.Count > 0);
    var jane = results.FirstOrDefault(r => r.Id == testId);
    Assert.IsNotNull(jane);
    Assert.AreEqual("Jane", jane.FirstName);
    Assert.AreEqual("Doe", jane.LastName);
}

[TestMethod]
public void ExecProcedure_OutputParam()
{
    // Arrange
    var totalCount = new SqlParam("@total_count", null, ParameterDirection.Output)
        { DbType = DbType.Int32 };

    // Act
    var results = _provider.ExecProcedure<PersonProcResult>(
        "sp_get_persons_paged",
        new SqlParam("@page", 1),
        new SqlParam("@page_size", 10),
        totalCount);

    // Assert
    Assert.IsNotNull(results);
    Assert.IsTrue(results.Count <= 10);
    Assert.IsNotNull(totalCount.Value);
    Assert.IsTrue((int)totalCount.Value > 0);
}

[TestMethod]
public void ExecProcedure_ConventionName()
{
    // Arrange
    var testId = InsertTestPerson("Bob", null, "Smith", null, "Male", Guid.NewGuid());

    // Act — no procedure name passed; inferred from SpGetPersonById class name
    var results = _provider.ExecProcedure<SpGetPersonById>(
        new { person_id = testId });

    // Assert
    Assert.IsNotNull(results);
    Assert.AreEqual(1, results.Count);
    Assert.AreEqual("Bob", results.First().FirstName);
}

[TestMethod]
public void ExecScalar_Int()
{
    // Arrange
    InsertTestPerson("Alice", null, "Test", null, "Female", Guid.NewGuid());

    // Act
    var count = _provider.ExecScalar<int>(
        "sp_count_persons_by_gender",
        ("@gender", "Female"));

    // Assert
    Assert.IsTrue(count > 0);
}

[TestMethod]
public void ExecNonQuery_Insert()
{
    // Arrange & Act
    _provider.BeginTransaction();
    try
    {
        var rowsAffected = _provider.ExecNonQuery(
            "sp_insert_organization",
            new { name = "Test Org via Proc" });

        // Assert
        Assert.AreEqual(1, rowsAffected);

        var org = _provider.Query<OrganizationEntity>()
            .Where(o => o.Name == "Test Org via Proc")
            .FirstOrDefault();
        Assert.IsNotNull(org);
    }
    finally
    {
        _provider.RollbackTransaction();
    }
}
```

---

## 8. Architecture Touch-Points

### New Files

| Path | Purpose |
|:---|:---|
| `Funcular.Data.Orm.Core/Attributes/ProcedureAttribute.cs` | `[Procedure("name")]` attribute for explicit proc name mapping |
| `Funcular.Data.Orm.Core/SqlParam.cs` | `SqlParam` parameter class with Direction/DbType/Size support |
| `Funcular.Data.Orm.SqlServer.Tests/Domain/Objects/StoredProcedure/PersonProcResult.cs` | Test DTO for person procedure results |
| `Funcular.Data.Orm.SqlServer.Tests/Domain/Objects/StoredProcedure/ProjectProcResult.cs` | Test DTO for project procedure results |
| `Funcular.Data.Orm.SqlServer.Tests/Domain/Objects/StoredProcedure/GenderFilteredPerson.cs` | Test entity with `[Procedure]` attribute |
| `Funcular.Data.Orm.SqlServer.Tests/Domain/Objects/StoredProcedure/SpGetPersonById.cs` | Test entity demonstrating convention inference |
| `Funcular.Data.Orm.SqlServer.Tests/Domain/Objects/StoredProcedure/PersonSearchParams.cs` | Test parameter class |
| `Funcular.Data.Orm.SqlServer.Tests/StoredProcedureIntegrationTests.cs` | Integration tests (24 tests) |

### Modified Files

| Path | Change |
|:---|:---|
| `Funcular.Data.Orm.Core/IOrmDataProvider.cs` | Add `ExecProcedure`, `ExecScalar`, `ExecNonQuery` + async counterparts |
| `Funcular.Data.Orm.Core/OrmDataProvider.cs` | Add `abstract` implementations for new interface methods; add procedure name resolution + caching; add `ReflectParameters` helper |
| `Funcular.Data.Orm.SqlServer/SqlServer/SqlServerOrmDataProvider.cs` | Implement all `ExecProcedure`/`ExecScalar`/`ExecNonQuery` methods using `SqlCommand` with `CommandType.StoredProcedure` |
| `Funcular.Data.Orm.PostgreSql/PostgreSql/PostgreSqlOrmDataProvider.cs` | Implement methods using `CALL` for procedures; throw `NotSupportedException` for `ExecProcedure<T>` on `PROCEDURE` targets with guidance to use functions |
| `Database/integration_test_db.sql` | Add stored procedure definitions |

### Internal Implementation Flow (MSSQL)

```
ExecProcedure<T>("sp_name", parameters)
  ?
  ?? ResolveProcedureName<T>("sp_name")  // explicit name wins
  ?? NormalizeParameters(parameters)     // reflect props or convert tuples/SqlParams
  ?     ?? ? List<SqlParameter>
  ?? BuildSqlCommandObject(procName, connection, sqlParams)
  ?     ?? command.CommandType = CommandType.StoredProcedure
  ?? ExecuteReader()
  ?     ?? while (reader.Read()) ? MapEntity<T>(reader)  // existing mapper pipeline
  ?? BackPopulateOutputParams(sqlParams, originalSqlParams)
  ?? return ICollection<T>
```

---

## 9. Developer Experience Examples

### Simplest Case — Result Set with Anonymous Object

```csharp
var people = provider.ExecProcedure<PersonProcResult>(
    "sp_get_persons_by_gender",
    new { gender = "Female" });
```

### Convention-Based Name (No Procedure Name Needed)

```csharp
// SpGetPersonById ? sp_get_person_by_id
var person = provider.ExecProcedure<SpGetPersonById>(
    new { person_id = 42 })
    .FirstOrDefault();
```

### Attribute-Based Name

```csharp
[Procedure("sp_search_persons")]
public class PersonSearchResult
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
}

var results = provider.ExecProcedure<PersonSearchResult>(
    new PersonSearchParams { Gender = "Male", LastName = "Smith" });
```

### Scalar

```csharp
int count = provider.ExecScalar<int>(
    "sp_count_persons_by_gender",
    ("@gender", "Male"));
```

### Non-Query with Transaction

```csharp
provider.BeginTransaction();
try
{
    int rows = provider.ExecNonQuery(
        "sp_update_person_gender",
        new { person_id = 42, new_gender = "Other" });

    provider.CommitTransaction();
}
catch
{
    provider.RollbackTransaction();
    throw;
}
```

### Output Parameters

```csharp
var total = new SqlParam("@total_count", null, ParameterDirection.Output)
    { DbType = DbType.Int32 };

var page = provider.ExecProcedure<PersonProcResult>(
    "sp_get_persons_paged",
    new SqlParam("@page", 1),
    new SqlParam("@page_size", 25),
    total);

Console.WriteLine($"Page has {page.Count} rows of {total.Value} total");
```

---

## 10. Implementation Priority

| Phase | Scope | Complexity |
|:---|:---|:---|
| **1** | `ExecProcedure<T>(name, params)` — MSSQL, all parameter modes, result-set mapping | Medium |
| **2** | `ExecScalar<TResult>` + `ExecNonQuery` — MSSQL | Low |
| **3** | Async counterparts for all methods | Low (mirrors sync) |
| **4** | `[Procedure]` attribute + convention name inference | Medium |
| **5** | Output parameter support via `SqlParam` | Low-Medium |
| **6** | PostgreSQL `ExecNonQuery` / `ExecScalar` via `CALL` | Medium |
| **7** | Integration tests (24 tests for MSSQL, subset for PostgreSQL) | Medium |

---

## 11. Open Design Notes

### Multiple Result Sets (Future)

Not in scope for v1. If needed later, a `ExecProcedure<T1, T2>(name, params)` overload could return a `(ICollection<T1>, ICollection<T2>)` tuple by calling `reader.NextResult()`.

### `netstandard2.0` Compatibility

All new types (`SqlParam`, `ProcedureAttribute`) use only `System.Data.IDbCommand` / `IDataReader` abstractions. The tuple syntax `(string, object)` requires `System.ValueTuple` which is available in `netstandard2.0`. No compatibility issues expected.

### Logging

All procedure executions should flow through the existing `InvokeLogAction(command)` pipeline, producing the same diagnostic output as `Query<T>` and `Insert<T>`.

### SET NOCOUNT ON

When `SET NOCOUNT ON` is used in a procedure, `ExecuteNonQuery` returns `-1` instead of rows affected. The `ExecNonQuery` documentation should note this. The provider should not treat `-1` as an error.
