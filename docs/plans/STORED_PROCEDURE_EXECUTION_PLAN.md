# Stored Procedure Execution — Implementation Plan

> **Goal**: Enable developers to execute stored procedures and retrieve scalar values or entity-mapped result sets using the same conventions and mapping pipeline that power `Query<T>`. Parameters may be passed as a class, anonymous object, or `SqlParam` objects (with tuple syntax preserved via implicit conversion). The contract is added to `IOrmDataProvider` with **virtual, throwing default implementations** in `OrmDataProvider`, then overridden per provider according to each platform's actual capabilities. Target release: **v3.7.0**.

> **Revision (2026-06-10)**: Updated for the four-provider reality (SQL Server, PostgreSQL, MySQL, SQLite) and to resolve design-review findings: (1) virtual-throwing base methods instead of abstract (abstract would break SQLite/MySQL compilation); (2) result-set mapping correctly located in each provider (Core owns only parameter normalization and name-resolution helpers — `MapEntity<T>` is provider-typed); (3) overload set de-ambiguated (tuple overloads replaced by an implicit tuple→`SqlParam` conversion); (4) `ExecScalar` conversion specified for `Nullable<T>`/enum/`Guid`; (5) `CommandType` plumbing called out; (6) provider capability matrix added, MySQL gains **full** support, SQLite throws by default; (7) DDL paths corrected to the current repo layout.

---

## 0. Provider Capability Matrix

This feature is intentionally **capability-based**: the API is uniform, but support varies by platform. The matrix below is the contract; anything unsupported throws `NotSupportedException` with a message explaining why and what to use instead.

| Capability | SQL Server | MySQL | PostgreSQL | SQLite |
|:---|:---|:---|:---|:---|
| `ExecProcedure<T>` (result set) | ✅ Full | ✅ Full (`CALL` returns result sets natively) | ⚠️ v1: throws with guidance — PG `PROCEDURE`s don't return result sets; use a `FUNCTION RETURNS TABLE` (auto-detection is a future enhancement) | ❌ Throws (no stored procedures) |
| `ExecScalar<TResult>` | ✅ | ✅ | ✅ via `CALL` (first column of the INOUT result row) | ❌ Throws |
| `ExecNonQuery` | ✅ | ✅ | ✅ via `CALL` | ❌ Throws |
| Output parameters (`SqlParam` Direction) | ✅ `OUTPUT` | ✅ `OUT`/`INOUT` | ✅ `INOUT` | ❌ Throws |
| Convention/attribute name resolution | ✅ `sys.procedures` | ✅ `information_schema.routines` | ✅ `pg_proc` | n/a |
| Invocation mechanism | `CommandType.StoredProcedure` | `CommandType.StoredProcedure` (MySqlConnector emits `CALL`) | `CommandType.Text` + `CALL proc(...)` | n/a |

**Why MySQL is full-support**: MySQL procedures return result sets natively from `CALL`, and MySqlConnector supports `CommandType.StoredProcedure` with named parameter binding and `OUT`/`INOUT` directions. MySQL is therefore on par with SQL Server for this feature (and simpler than PostgreSQL).

**Why SQLite needs no code**: the virtual base implementations throw `NotSupportedException` by default (see §2.1), which *is* the documented SQLite behavior (per the SQLite provider plan §5.8). The SQLite provider ships zero changes for this feature.

---

## 1. API Design

### 1.1 Method Signatures on `IOrmDataProvider`

The overload set is deliberately small to avoid overload-resolution ambiguity (see §1.3). Tuple syntax is preserved through an implicit conversion to `SqlParam` (§1.4), not through separate overloads.

```csharp
public interface IOrmDataProvider
{
    // ... existing methods ...

    // —— Result-set execution ——

    /// <summary>
    /// Executes a stored procedure and maps the result set to a collection of <typeparamref name="T"/>.
    /// The procedure name is inferred from <typeparamref name="T"/> via [Procedure] or naming conventions.
    /// </summary>
    ICollection<T> ExecProcedure<T>(object parameters = null) where T : class, new();

    /// <summary>
    /// Executes the named stored procedure and maps the result set to a collection of <typeparamref name="T"/>.
    /// <paramref name="parameters"/> may be an anonymous object, a typed class, a SqlParam,
    /// or an IEnumerable&lt;SqlParam&gt; (see §3 guards).
    /// </summary>
    ICollection<T> ExecProcedure<T>(string procedureName, object parameters = null) where T : class, new();

    /// <summary>
    /// Executes the named stored procedure with SqlParam parameters (supports output parameters
    /// and tuple syntax via implicit conversion) and maps the result set.
    /// </summary>
    ICollection<T> ExecProcedure<T>(string procedureName, params SqlParam[] parameters) where T : class, new();

    // —— Scalar execution —— (no convention overload: TResult is a value type, not a name source)

    TResult ExecScalar<TResult>(string procedureName, object parameters = null);
    TResult ExecScalar<TResult>(string procedureName, params SqlParam[] parameters);

    // —— Non-query execution ——

    int ExecNonQuery(string procedureName, object parameters = null);
    int ExecNonQuery(string procedureName, params SqlParam[] parameters);

    // —— Async counterparts ——

    Task<ICollection<T>> ExecProcedureAsync<T>(object parameters = null) where T : class, new();
    Task<ICollection<T>> ExecProcedureAsync<T>(string procedureName, object parameters = null) where T : class, new();
    Task<ICollection<T>> ExecProcedureAsync<T>(string procedureName, params SqlParam[] parameters) where T : class, new();

    Task<TResult> ExecScalarAsync<TResult>(string procedureName, object parameters = null);
    Task<TResult> ExecScalarAsync<TResult>(string procedureName, params SqlParam[] parameters);

    Task<int> ExecNonQueryAsync(string procedureName, object parameters = null);
    Task<int> ExecNonQueryAsync(string procedureName, params SqlParam[] parameters);
}
```

### 1.2 Contract Placement & Base-Class Strategy (RESOLVED)

**Decision**: Add the members to `IOrmDataProvider` (at `Funcular.Data.Orm.Core/IOrmDataProvider.cs`) and give `OrmDataProvider` **`public virtual` implementations that throw `NotSupportedException`** by default:

```csharp
// OrmDataProvider (Core)
public virtual ICollection<T> ExecProcedure<T>(object parameters = null) where T : class, new()
    => throw NewProcedureNotSupportedException();
// ... all Exec* members follow the same pattern ...

protected NotSupportedException NewProcedureNotSupportedException() =>
    new NotSupportedException(
        $"{GetType().Name} does not support this stored procedure operation. " +
        "SQL Server and MySQL support stored procedures fully; PostgreSQL supports " +
        "ExecNonQuery/ExecScalar via CALL (use a FUNCTION for result sets); " +
        "SQLite has no stored procedures.");
```

**Rationale**:
- The base class currently declares all provider operations `abstract` (lines 72–88 of `OrmDataProvider.cs`). Adding these as `abstract` would **break compilation of the SQLite and MySQL providers** until each implements ~16 members. `virtual`-throwing avoids that and *is* the correct runtime behavior for SQLite.
- `netstandard2.0` does not support default interface members, so the interface members alone can't carry defaults — the base class must.
- Adding members to `IOrmDataProvider` follows project precedent (`ISqlDialect` gained `ProviderName`, `BuildScalarSubquery`, `BuildJsonCollectionSubquery` in 3.2.1, a minor release). **Caveat for the changelog**: this is a source-breaking change for any *external* code that implements `IOrmDataProvider` directly rather than deriving from `OrmDataProvider`. Considered and rejected alternative: a separate `IStoredProcedureProvider` interface — rejected because consumers typed to `IOrmDataProvider` would need casts, fragmenting the API surface for a rare external-implementor scenario.

### 1.3 Overload Resolution (RESOLVED — why tuples are not separate overloads)

The original draft had `(object)`, `params (string, object)[]`, and `params SqlParam[]` overloads side by side. That set is ambiguous at common call sites (e.g., `ExecProcedure<T>("proc", null)` matches all three; `ExecProcedure<T>(null)` is ambiguous between the convention and named forms). The revised surface keeps two parameter modes — `object` and `params SqlParam[]` — and recovers tuple ergonomics through an **implicit conversion** (§1.4). Resolution behavior at the corner cases:

| Call | Binds to | Notes |
|:---|:---|:---|
| `ExecProcedure<T>()` | convention `(object = null)` | Only applicable candidate |
| `ExecProcedure<T>(new { a = 1 })` | convention `(object)` | Anonymous types have no conversion to `SqlParam` |
| `ExecProcedure<T>("proc")` | `(string, object = null)` | Identity conversion on `string` beats `string→object`; normal form beats expanded `SqlParam[]` form |
| `ExecProcedure<T>("proc", new { a = 1 })` | `(string, object)` | |
| `ExecProcedure<T>("proc", ("@a", 1))` | `(string, params SqlParam[])` | Tuple converts implicitly to `SqlParam`; `SqlParam` is a better conversion target than `object` |
| `ExecProcedure<T>("proc", null)` | `(string, params SqlParam[])` (normal form, null array) | Implementations MUST treat a null/empty array as "no parameters" |

**Phase 0 requirement**: before any provider work, add a small compile-and-bind unit test (`StoredProcOverloadResolutionTests`) that locks in each row of this table (e.g., via a stub provider recording which overload executed). This converts compiler-behavior assumptions into regression-tested facts.

**Guards in `NormalizeParameters`** (defense for the `object` overloads):
- `parameters is string` → throw `ArgumentException` ("a string was passed as the parameters object; did you mean the (procedureName, parameters) overload?").
- `parameters is SqlParam || parameters is IEnumerable<SqlParam>` → handle natively (do not reflect over it).

### 1.4 `SqlParam` Type (in `Funcular.Data.Orm.Core`)

```csharp
/// <summary>
/// Represents a named parameter for stored procedure execution.
/// Supports input, output, and input/output directions, and converts
/// implicitly from (string Name, object Value) tuples.
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
        : this(name, value)
    {
        Direction = direction;
    }

    /// <summary>Preserves terse tuple syntax: ExecScalar&lt;int&gt;("proc", ("@gender", "Male")).</summary>
    public static implicit operator SqlParam((string Name, object Value) tuple)
        => new SqlParam(tuple.Name, tuple.Value);
}
```

- Output parameters are supported by passing `SqlParam` instances with `Direction = ParameterDirection.Output` or `InputOutput`. After execution, `Value` is back-populated from the native parameter. This is the only mode that supports output parameters — the object overload is input-only.
- For `Output` parameters with `Value = null`, set `DbType` (and `Size` for variable-length types) so the native parameter can be typed.
- `System.ValueTuple` is available in `netstandard2.0`; no compatibility issues.

---

## 2. Procedure Name Resolution

Procedure name inference follows the same `IgnoreUnderscoreAndCaseStringComparer` logic used for table/column name resolution:

| Class Name | Matches Procedure |
|:---|:---|
| `SpGetActiveProjects` | `sp_get_active_projects`, `SP_GET_ACTIVE_PROJECTS`, `SpGetActiveProjects` |
| `GetPersonById` | `get_person_by_id`, `GetPersonById`, `GETPERSONBYID` |
| `UspInsertLog` | `usp_insert_log`, `USP_INSERT_LOG`, `UspInsertLog` |

### Resolution Order

1. **Explicit procedure name argument** — `ExecProcedure<T>("my_proc", ...)` — used as-is.
2. **`[Procedure]` attribute on the entity class** — `[Procedure("sp_get_active_projects")]`.
3. **Convention inference from class name** — normalize and query the provider's catalog.

### `[Procedure]` Attribute (in `Funcular.Data.Orm.Core/Attributes`)

```csharp
/// <summary>
/// Specifies the stored procedure name for an entity class used with ExecProcedure.
/// Analogous to [Table] for table name overrides.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ProcedureAttribute : Attribute
{
    public string Name { get; }
    public ProcedureAttribute(string name) => Name = name;
}
```

### Catalog Queries & Caching (per provider, mirroring each provider's `ResolveTableName`)

| Provider | Catalog query |
|:---|:---|
| **SQL Server** | `SELECT name FROM sys.procedures WHERE REPLACE(LOWER(name), '_', '') = @normalized` |
| **MySQL** | `SELECT routine_name FROM information_schema.routines WHERE routine_schema = DATABASE() AND routine_type = 'PROCEDURE' AND REPLACE(LOWER(routine_name), '_', '') = @normalized` |
| **PostgreSQL** | `SELECT proname FROM pg_proc p JOIN pg_namespace n ON p.pronamespace = n.oid WHERE n.nspname = 'public' AND prokind = 'p' AND REPLACE(LOWER(proname), '_', '') = @normalized` |
| **SQLite** | n/a |

Results are cached in a `ConcurrentDictionary<Type, string>` per provider (the same pattern as `_tableNames`). The attribute check and class-name normalization helper live in Core; the catalog lookup is provider code (Core has no connection).

---

## 3. Parameter Handling

### Two Input Modes

**Mode A — Class / Anonymous Object** (input-only):

The framework reflects over the object's public readable properties and creates one parameter per property. The `@` prefix is added automatically if absent. Property-to-parameter mapping uses the property name directly (no snake_case conversion — procedure parameter names are developer-controlled). Null property values are sent as `DBNull.Value`, typed from the property's declared type (anonymous-type properties are statically typed, so this works for anonymous objects too).

```csharp
// Anonymous object
provider.ExecProcedure<ProjectSummary>("sp_get_projects_by_org",
    new { OrganizationId = 5, IsActive = true });

// Typed class
var filter = new ProjectFilter { OrganizationId = 5, IsActive = true };
provider.ExecProcedure<ProjectSummary>("sp_get_projects_by_org", filter);
```

**Mode B — `SqlParam` Array** (supports output parameters; tuples convert implicitly):

```csharp
// Tuple syntax (implicit conversion):
provider.ExecProcedure<ProjectSummary>("sp_get_projects_by_org",
    ("@organization_id", 5),
    ("@is_active", true));

// Output parameter:
var totalCount = new SqlParam("@total_count", null, ParameterDirection.Output) { DbType = DbType.Int32 };
var results = provider.ExecProcedure<ProjectSummary>("sp_get_projects_paged",
    new SqlParam("@page", 1),
    new SqlParam("@page_size", 25),
    totalCount);
int count = (int)totalCount.Value; // populated after execution
```

### Normalization & Conversion Pipeline

`OrmDataProvider` (Core) provides a protected `NormalizeParameters(object parameters)` / `NormalizeParameters(SqlParam[] parameters)` pair returning a provider-agnostic list (name, value, direction, dbType, size, source-`SqlParam` reference). It applies the §1.3 guards. Each provider then converts to its native parameter type and, after execution, back-populates `Value` on source `SqlParam`s with `Direction != Input`:

| Provider | Native Parameter Type |
|:---|:---|
| **SQL Server** | `SqlParameter` |
| **MySQL** | `MySqlParameter` |
| **PostgreSQL** | `NpgsqlParameter` |

---

## 4. Result Mapping (locus: per provider)

### Entity-Mapped Result Sets (`ExecProcedure<T>`)

Result mapping reuses each provider's **existing `MapEntity<T>` / `BuildDataReaderMapper<T>` pipeline** — the one already used by `Query<T>`. Note that this pipeline is **provider code**, typed to each driver's reader (`SqlDataReader` / `MySqlDataReader` / `NpgsqlDataReader`); it does not live in Core. Consequently the `Exec*` execution methods are implemented **in each provider**, and Core contributes only normalization, name-resolution helpers, the `[Procedure]` attribute, `SqlParam`, and the throwing virtual defaults.

Mapping semantics (identical to `Query<T>`):
- Column-to-property matching uses `IgnoreUnderscoreAndCaseStringComparer` (e.g., `first_name` → `FirstName`).
- `[Column("custom_name")]` attributes are respected; `[NotMapped]` properties are skipped.
- Nullable types are handled automatically; the schema-signature-based mapper cache is reused.
- No `DiscoverColumns<T>` call is needed — the mapper is built from the result set's schema at read time.

### Scalar Results (`ExecScalar<TResult>`)

`ExecuteScalar()` is called and the result is converted by a shared Core helper that handles the cases plain `Convert.ChangeType` cannot:

```csharp
protected static TResult ConvertScalar<TResult>(object value)
{
    if (value == null || value is DBNull) return default;                  // incl. Nullable<T> → null
    var target = Nullable.GetUnderlyingType(typeof(TResult)) ?? typeof(TResult);
    if (target.IsInstanceOfType(value)) return (TResult)value;
    if (target.IsEnum) return (TResult)Enum.ToObject(target, value);
    if (target == typeof(Guid)) return (TResult)(object)Guid.Parse(value.ToString());
    return (TResult)Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
}
```

### Non-Query (`ExecNonQuery`)

`ExecuteNonQuery()` is called; the rows-affected integer is returned directly (see §11 for the `SET NOCOUNT ON` / `-1` note).

### Command Construction (`CommandType` plumbing)

The existing `BuildSqlCommandObject(...)` in each provider hard-codes `CommandType.Text`. Add an optional parameter (non-breaking, internal):

```csharp
protected internal SqlCommand BuildSqlCommandObject(string commandText, IDbConnection connection,
    ICollection<SqlParameter> parameters = null, CommandType commandType = CommandType.Text)
```

- **SQL Server / MySQL**: pass `CommandType.StoredProcedure` with the bare procedure name.
- **PostgreSQL**: keep `CommandType.Text` and build `CALL proc_name(@p1, @p2, ...)` (Npgsql does not support `CommandType.StoredProcedure` for `CALL`).

All executions flow through the existing `InvokeLogAction(command)` and `ConnectionScope` pipelines (transactions therefore work exactly as for `Query<T>`).

---

## 5. Provider-Specific Notes

### 5.1 SQL Server (full support)
- `CommandType.StoredProcedure`; parameters bind by name; `OUTPUT` directions supported natively.
- Reference implementation for the feature; everything in §1–§4 applies directly.

### 5.2 MySQL (full support)
- `CommandType.StoredProcedure` via MySqlConnector (emits `CALL` internally); result sets are returned natively; `OUT`/`INOUT` parameters supported. The recommended connection string already includes `AllowUserVariables=true`, which MySqlConnector may use for output-parameter plumbing.
- Catalog lookup via `information_schema.routines` (§2).
- **DDL gotcha**: procedure bodies in `Database/MySql/integration_test_db.sql` need `DELIMITER $$ ... END$$ DELIMITER ;` blocks because CI pipes the file through the `mysql` CLI. Note that `DELIMITER` is a *client* directive — when creating procedures through MySqlConnector (e.g., a fixture), send each `CREATE PROCEDURE` as its own command without `DELIMITER`.

### 5.3 PostgreSQL (partial: non-query + scalar in v1)

PostgreSQL 11+ supports `PROCEDURE` (called via `CALL`) as distinct from `FUNCTION`:

| Aspect | SQL Server / MySQL | PostgreSQL |
|:---|:---|:---|
| **Invocation** | `CommandType.StoredProcedure` | `CommandType.Text` with `CALL proc_name(...)` |
| **Result sets** | Returned natively | `CALL` does not return result sets; `FUNCTION RETURNS TABLE`/`SETOF` is the tabular idiom |
| **Output values** | `OUTPUT`/`OUT` parameters | `INOUT` parameters, returned as a single result row from `CALL` |

v1 behavior:
- `ExecNonQuery` → `CALL proc(...)`, works directly.
- `ExecScalar<TResult>` → `CALL proc(...)`; reads the first column of the single `INOUT` result row.
- `ExecProcedure<T>` → throws `NotSupportedException` with guidance ("PostgreSQL procedures do not return result sets; expose the query as a FUNCTION RETURNS TABLE and query it with raw SQL, or wait for function auto-detection"). **Future enhancement**: detect `prokind = 'f'` and invoke via `SELECT * FROM func_name(...)` to give PostgreSQL full `ExecProcedure<T>` parity.
- Output `SqlParam`s map to `INOUT` arguments; back-population reads the `CALL` result row.

### 5.4 SQLite (throws; zero code)
- No stored procedures exist in SQLite. The provider inherits the virtual base implementations, which throw `NotSupportedException` — exactly the behavior its provider plan prescribes. The only SQLite work is **one negative test** asserting the exception.

---

## 6. Database Structures for Integration Tests

> **Paths (corrected to current repo layout)**: SQL Server → `Database/integration_test_db.sql` (loaded by `ci.yml` via `sqlcmd`); PostgreSQL → `Database/PostgreSql/integration_test_db.sql`; MySQL → `Database/MySql/integration_test_db.sql`. Use idempotent forms: `CREATE OR ALTER PROCEDURE` (SQL Server 2016 SP1+/LocalDB), `CREATE OR REPLACE PROCEDURE` (PostgreSQL), `DROP PROCEDURE IF EXISTS` + `CREATE PROCEDURE` (MySQL — it has no `CREATE OR REPLACE PROCEDURE`), so local persistent databases can re-run the scripts.

### 6.1 SQL Server (append to `Database/integration_test_db.sql`)

```sql
-- =========================================================================
-- Stored Procedure Test Objects
-- Procedures covering every execution mode: result set, scalar, non-query,
-- output parameters, and both parameter styles.
-- =========================================================================

CREATE OR ALTER PROCEDURE sp_get_persons_by_gender
    @gender NVARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT id, first_name, last_name, gender, birthdate
    FROM person
    WHERE gender = @gender;
END;
GO

CREATE OR ALTER PROCEDURE sp_get_person_by_id
    @person_id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT id, first_name, last_name, gender, birthdate
    FROM person
    WHERE id = @person_id;
END;
GO

CREATE OR ALTER PROCEDURE sp_count_persons_by_gender
    @gender NVARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT(*) FROM person WHERE gender = @gender;
END;
GO

CREATE OR ALTER PROCEDURE sp_insert_organization
    @name NVARCHAR(100),
    @headquarters_address_id INT = NULL
AS
BEGIN
    INSERT INTO organization (name, headquarters_address_id)
    VALUES (@name, @headquarters_address_id);
END;
GO

CREATE OR ALTER PROCEDURE sp_update_person_gender
    @person_id INT,
    @new_gender NVARCHAR(10)
AS
BEGIN
    UPDATE person SET gender = @new_gender WHERE id = @person_id;
END;
GO

CREATE OR ALTER PROCEDURE sp_get_persons_paged
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

CREATE OR ALTER PROCEDURE sp_search_persons
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

CREATE OR ALTER PROCEDURE sp_get_person_full_name
    @person_id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CONCAT(first_name, ' ', last_name) FROM person WHERE id = @person_id;
END;
GO

CREATE OR ALTER PROCEDURE sp_get_projects_by_org
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

CREATE OR ALTER PROCEDURE sp_noop
AS
BEGIN
    SET NOCOUNT ON;
    -- intentionally empty
END;
GO
```

### 6.2 MySQL (append to `Database/MySql/integration_test_db.sql`)

Same procedure set as SQL Server, in MySQL syntax. Representative subset (the remaining procedures follow the same pattern):

```sql
-- NOTE: DELIMITER blocks are required when this file is piped through the mysql CLI
-- (as the CI workflow does). DELIMITER is a client directive: when creating these
-- through MySqlConnector instead, send each CREATE PROCEDURE as its own command.

DROP PROCEDURE IF EXISTS sp_get_persons_by_gender;
DELIMITER $$
CREATE PROCEDURE sp_get_persons_by_gender(IN p_gender VARCHAR(10))
BEGIN
    SELECT id, first_name, last_name, gender, birthdate
    FROM person
    WHERE gender = p_gender;
END$$
DELIMITER ;

DROP PROCEDURE IF EXISTS sp_count_persons_by_gender;
DELIMITER $$
CREATE PROCEDURE sp_count_persons_by_gender(IN p_gender VARCHAR(10))
BEGIN
    SELECT COUNT(*) FROM person WHERE gender = p_gender;
END$$
DELIMITER ;

DROP PROCEDURE IF EXISTS sp_insert_organization;
DELIMITER $$
CREATE PROCEDURE sp_insert_organization(IN p_name VARCHAR(100), IN p_headquarters_address_id INT)
BEGIN
    INSERT INTO organization (name, headquarters_address_id)
    VALUES (p_name, p_headquarters_address_id);
END$$
DELIMITER ;

DROP PROCEDURE IF EXISTS sp_get_persons_paged;
DELIMITER $$
CREATE PROCEDURE sp_get_persons_paged(IN p_page INT, IN p_page_size INT, OUT p_total_count INT)
BEGIN
    SELECT COUNT(*) INTO p_total_count FROM person;
    SELECT id, first_name, last_name, gender, birthdate
    FROM person
    ORDER BY id
    LIMIT p_page_size OFFSET ((p_page - 1) * p_page_size);
END$$
DELIMITER ;

DROP PROCEDURE IF EXISTS sp_noop;
DELIMITER $$
CREATE PROCEDURE sp_noop()
BEGIN
    -- intentionally empty
    SET @dummy = 0;
END$$
DELIMITER ;
```

(Also: `sp_get_person_by_id`, `sp_update_person_gender`, `sp_search_persons`, `sp_get_person_full_name`, `sp_get_projects_by_org` — direct translations of the SQL Server versions.)

### 6.3 PostgreSQL (append to `Database/PostgreSql/integration_test_db.sql`)

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

-- Procedure: sp_count_persons (scalar via INOUT — CALL returns the INOUT row)
CREATE OR REPLACE PROCEDURE sp_count_persons(
    INOUT p_total_count INT DEFAULT 0
)
LANGUAGE plpgsql
AS $$
BEGIN
    SELECT COUNT(*) INTO p_total_count FROM person;
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

### 6.4 SQLite

No database objects. One negative test only (§7).

---

## 7. Integration Tests

### Test Entities for Procedure Results

```csharp
/// <summary>DTO for mapping sp_get_persons_by_gender / sp_search_persons result sets.</summary>
public class PersonProcResult
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Gender { get; set; }
    public DateTime? Birthdate { get; set; }
}

/// <summary>DTO for sp_get_projects_by_org — includes the JSON metadata column.</summary>
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

/// <summary>[Procedure] attribute demo — class name does not match any procedure.</summary>
[Procedure("sp_get_persons_by_gender")]
public class GenderFilteredPerson
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Gender { get; set; }
}

/// <summary>Convention inference demo — SpGetPersonById → sp_get_person_by_id.</summary>
public class SpGetPersonById
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Gender { get; set; }
    public DateTime? Birthdate { get; set; }
}

/// <summary>Parameter class for sp_search_persons — class-based parameter passing.</summary>
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

**Phase 0 (Core, no DB)** — `StoredProcOverloadResolutionTests`: one test per row of the §1.3 binding table, plus `NormalizeParameters` guard tests (string-as-parameters throws; SqlParam-as-object handled natively; null property → DBNull).

**SQL Server** (`Funcular.Data.Orm.SqlServer.Tests/StoredProcedureIntegrationTests.cs`) — full matrix:

| # | Test Name | Method | Parameter Style | Asserts |
|:---|:---|:---|:---|:---|
| 1 | `ExecProcedure_ResultSet_AnonymousObject` | `ExecProcedure<T>(name, anon)` | Anonymous object | Returns collection, count > 0, properties mapped |
| 2 | `ExecProcedure_ResultSet_TypedClass` | `ExecProcedure<T>(name, class)` | Class instance | Same as #1 |
| 3 | `ExecProcedure_ResultSet_TupleSyntax` | `ExecProcedure<T>(name, (n,v))` | Tuples → implicit SqlParam | Same as #1 |
| 4 | `ExecProcedure_ResultSet_SqlParams` | `ExecProcedure<T>(name, SqlParam[])` | SqlParam array | Same as #1 |
| 5 | `ExecProcedure_ResultSet_NoParams` | `ExecProcedure<T>(name)` | None (optional params) | Returns full table |
| 6 | `ExecProcedure_SingleRow` | `ExecProcedure<T>(name, anon)` | Anonymous object | Returns 1 entity, values match |
| 7 | `ExecProcedure_EmptyResultSet` | `ExecProcedure<T>(name, anon)` | Anonymous object | Returns empty collection, no error |
| 8 | `ExecProcedure_ConventionName` | `ExecProcedure<T>(anon)` | Name inferred from `SpGetPersonById` | Procedure found, returns result |
| 9 | `ExecProcedure_AttributeName` | `ExecProcedure<T>(anon)` | Name from `[Procedure]` | Procedure found via attribute |
| 10 | `ExecProcedure_ColumnMapping` | `ExecProcedure<T>(name, anon)` | Anonymous object | `first_name` → `FirstName` etc. |
| 11 | `ExecScalar_Int` | `ExecScalar<int>(name, anon)` | Anonymous object | Returns correct count |
| 12 | `ExecScalar_String` | `ExecScalar<string>(name, anon)` | Anonymous object | Returns concatenated name |
| 13 | `ExecScalar_NullableInt` | `ExecScalar<int?>(name, anon)` | Anonymous object | Nullable conversion works (incl. empty → null) |
| 14 | `ExecNonQuery_Insert` | `ExecNonQuery(name, anon)` | Anonymous object | Returns 1 row affected, row exists |
| 15 | `ExecNonQuery_Update` | `ExecNonQuery(name, anon)` | Anonymous object | Returns 1 row affected, value changed |
| 16 | `ExecNonQuery_Noop` | `ExecNonQuery(name)` | None | Returns 0/-1 (NOCOUNT note), no error |
| 17 | `ExecProcedure_OutputParam` | `ExecProcedure<T>(name, SqlParam[])` | SqlParam Direction.Output | Result set returned AND output param populated |
| 18 | `ExecProcedure_WithinTransaction` | `ExecProcedure<T>(name, anon)` | Inside `BeginTransaction` | Uses existing transaction, results correct |
| 19 | `ExecProcedure_ProjectsWithJson` | `ExecProcedure<T>(name, anon)` | Anonymous object | `metadata` mapped as string, contains JSON |
| 20 | `ExecProcedureAsync_ResultSet` | `ExecProcedureAsync<T>(name, anon)` | Anonymous object | Async works identically |
| 21 | `ExecScalarAsync_Int` | `ExecScalarAsync<int>(name, anon)` | Anonymous object | Async scalar works |
| 22 | `ExecNonQueryAsync_Update` | `ExecNonQueryAsync(name, anon)` | Anonymous object | Async non-query works |
| 23 | `ExecProcedure_NullParameter` | `ExecProcedure<T>(name, anon)` | Anon with null property | NULL sent correctly, no error |
| 24 | `ExecProcedure_InvalidProcName` | `ExecProcedure<T>("nonexistent")` | n/a | Throws meaningful exception |

**MySQL** (`Funcular.Data.Orm.MySql.Tests/MySqlStoredProcedureTests.cs`) — the same matrix (1–24) adapted to the MySQL procedures; expected to pass in full.

**PostgreSQL** (`Funcular.Data.Orm.PostgreSql.Tests`) — subset: `ExecNonQuery_Insert`, `ExecNonQuery_Update`, `ExecScalar_ViaInoutCall`, output/INOUT back-population, async variants, plus `ExecProcedure_Throws_WithFunctionGuidance` asserting the v1 `NotSupportedException` message.

**SQLite** (`Funcular.Data.Orm.Sqlite.Tests`) — one negative test: every `Exec*` entry point throws `NotSupportedException` (no DDL, no fixtures).

### Test Pseudocode Examples

```csharp
[TestMethod]
public void ExecProcedure_ResultSet_AnonymousObject()
{
    var testId = InsertTestPerson("Jane", null, "Doe", DateTime.Today.AddYears(-30), "Female", Guid.NewGuid());

    var results = _provider.ExecProcedure<PersonProcResult>(
        "sp_get_persons_by_gender",
        new { gender = "Female" });

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
    var totalCount = new SqlParam("@total_count", null, ParameterDirection.Output)
        { DbType = DbType.Int32 };

    var results = _provider.ExecProcedure<PersonProcResult>(
        "sp_get_persons_paged",
        new SqlParam("@page", 1),
        new SqlParam("@page_size", 10),
        totalCount);

    Assert.IsNotNull(results);
    Assert.IsTrue(results.Count <= 10);
    Assert.IsNotNull(totalCount.Value);
    Assert.IsTrue((int)totalCount.Value > 0);
}

[TestMethod]
public void ExecProcedure_ConventionName()
{
    var testId = InsertTestPerson("Bob", null, "Smith", null, "Male", Guid.NewGuid());

    // No procedure name passed; inferred from SpGetPersonById class name.
    var results = _provider.ExecProcedure<SpGetPersonById>(new { person_id = testId });

    Assert.IsNotNull(results);
    Assert.AreEqual(1, results.Count);
    Assert.AreEqual("Bob", results.First().FirstName);
}

[TestMethod]
public void ExecScalar_Int()
{
    InsertTestPerson("Alice", null, "Test", null, "Female", Guid.NewGuid());

    var count = _provider.ExecScalar<int>(
        "sp_count_persons_by_gender",
        ("@gender", "Female")); // tuple → implicit SqlParam

    Assert.IsTrue(count > 0);
}

[TestMethod]
public void ExecNonQuery_Insert()
{
    _provider.BeginTransaction();
    try
    {
        var rowsAffected = _provider.ExecNonQuery(
            "sp_insert_organization",
            new { name = "Test Org via Proc" });

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
| `Funcular.Data.Orm.Core/Attributes/ProcedureAttribute.cs` | `[Procedure("name")]` attribute |
| `Funcular.Data.Orm.Core/SqlParam.cs` | `SqlParam` with Direction/DbType/Size + implicit tuple conversion |
| `Funcular.Data.Orm.SqlServer.Tests/Domain/Objects/StoredProcedure/*.cs` | Test DTOs (PersonProcResult, ProjectProcResult, GenderFilteredPerson, SpGetPersonById, PersonSearchParams) |
| `Funcular.Data.Orm.SqlServer.Tests/StoredProcedureIntegrationTests.cs` | SQL Server integration tests (24) |
| `Funcular.Data.Orm.MySql.Tests/MySqlStoredProcedureTests.cs` | MySQL integration tests (24, adapted) + DTO copies |
| `Funcular.Data.Orm.PostgreSql.Tests/PostgreSqlStoredProcedureTests.cs` | PostgreSQL subset tests |
| `Funcular.Data.Orm.Sqlite.Tests/SqliteStoredProcedureTests.cs` | Negative test (throws) |
| Core test location TBD (or SqlServer.Tests) | `StoredProcOverloadResolutionTests` (Phase 0) |

### Modified Files

| Path | Change |
|:---|:---|
| `Funcular.Data.Orm.Core/IOrmDataProvider.cs` | Add `ExecProcedure`/`ExecScalar`/`ExecNonQuery` + async counterparts (breaking-change caveat for external interface implementors — changelog) |
| `Funcular.Data.Orm.Core/OrmDataProvider.cs` | Add **virtual throwing** implementations; `NormalizeParameters` + guards; `ConvertScalar<TResult>`; `[Procedure]`-or-normalized-name helper |
| `Funcular.Data.Orm.SqlServer/SqlServer/SqlServerOrmDataProvider.cs` | Override all `Exec*`; `BuildSqlCommandObject` gains optional `CommandType`; catalog name resolution + cache |
| `Funcular.Data.Orm.MySql/MySql/MySqlOrmDataProvider.cs` | Same as SQL Server (full support) |
| `Funcular.Data.Orm.PostgreSql/PostgreSql/PostgreSqlOrmDataProvider.cs` | Override `ExecNonQuery`/`ExecScalar` (+async) via `CALL`; `ExecProcedure<T>` throws with function guidance |
| `Funcular.Data.Orm.Sqlite/...` | **No changes** (inherits throwing defaults) |
| `Database/integration_test_db.sql` | Add SQL Server procedures (`CREATE OR ALTER`) |
| `Database/MySql/integration_test_db.sql` | Add MySQL procedures (`DROP IF EXISTS` + `DELIMITER` blocks) |
| `Database/PostgreSql/integration_test_db.sql` | Add PostgreSQL procedures (`CREATE OR REPLACE`) |

### Internal Implementation Flow (SQL Server / MySQL)

```
ExecProcedure<T>("sp_name", parameters)
  |
  +- ResolveProcedureName<T>("sp_name")   // explicit > [Procedure] > catalog lookup (cached)
  +- NormalizeParameters(parameters)      // Core: reflect object or pass through SqlParams; guards
  |     +- -> normalized list -> native SqlParameter[]/MySqlParameter[]
  +- BuildSqlCommandObject(procName, connection, params, CommandType.StoredProcedure)
  +- ExecuteReader()
  |     +- while (reader.Read()) -> MapEntity<T>(reader)   // provider's existing mapper pipeline
  +- BackPopulateOutputParams(nativeParams -> source SqlParams)
  +- return ICollection<T>
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
// SpGetPersonById → sp_get_person_by_id
var person = provider.ExecProcedure<SpGetPersonById>(new { person_id = 42 })
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

### Scalar (tuple syntax via implicit SqlParam conversion)

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

## 10. Implementation Phases

| Phase | Scope | Complexity |
|:---|:---|:---|
| **0** | Core types (`SqlParam`, `[Procedure]`), interface + virtual-throwing base, `NormalizeParameters`/`ConvertScalar`, **overload-resolution binding tests** | Low-Medium |
| **1** | SQL Server: `ExecProcedure<T>` (all parameter modes, result-set mapping, `CommandType` plumbing) | Medium |
| **2** | SQL Server: `ExecScalar` + `ExecNonQuery` | Low |
| **3** | Async counterparts (SQL Server) | Low (mirrors sync) |
| **4** | `[Procedure]` attribute + convention name inference + catalog caching | Medium |
| **5** | Output parameter support + back-population | Low-Medium |
| **6** | **MySQL: full implementation** (mirrors Phases 1–5; mostly mechanical given the SQL Server reference) | Medium |
| **7** | PostgreSQL: `ExecNonQuery`/`ExecScalar` via `CALL`; `ExecProcedure<T>` guidance throw | Medium |
| **8** | Integration tests: SQL Server 24, MySQL 24, PostgreSQL subset, SQLite negative; DDL for all three databases; CI green | Medium |

---

## 11. Open Design Notes

### Multiple Result Sets (Future)

Not in scope for v1. If needed later, an `ExecProcedure<T1, T2>(name, params)` overload could return a `(ICollection<T1>, ICollection<T2>)` tuple by calling `reader.NextResult()`.

### PostgreSQL Function Auto-Detection (Future)

Detect `prokind = 'f'` during name resolution and invoke `SELECT * FROM func_name(...)` to give PostgreSQL full `ExecProcedure<T>` parity. Deferred from v1 to keep scope bounded.

### `netstandard2.0` Compatibility

All new types use only `System.Data` abstractions; `System.ValueTuple` is in `netstandard2.0`. The virtual-throwing base (not default interface members) carries the defaults, so `netstandard2.0`'s lack of DIM support is irrelevant.

### Versioning / Breaking-Change Note

Target **3.7.0**. Adding members to `IOrmDataProvider` is source-breaking for external code that implements the interface directly (not via `OrmDataProvider`). Project precedent treats interface evolution as acceptable in minor releases (`ISqlDialect` in 3.2.1); the changelog must call it out explicitly.

### Logging

All procedure executions flow through the existing `InvokeLogAction(command)` pipeline, producing the same diagnostic output as `Query<T>` and `Insert<T>`.

### SET NOCOUNT ON

When `SET NOCOUNT ON` is used in a SQL Server procedure, `ExecuteNonQuery` returns `-1` instead of rows affected. The `ExecNonQuery` documentation should note this; the provider must not treat `-1` as an error.

### Null `params` Array

`ExecProcedure<T>("proc", null)` binds to the `params SqlParam[]` overload with a null array (see §1.3). All implementations must treat a null or empty array as "no parameters". Prefer omitting the argument entirely.
