# Feature Specification: Remote Foreign Keys & Properties

## Overview
The "Remote Foreign Keys" feature allows entities to map properties directly to columns in "distant" related tables (ancestors) without requiring the developer to write explicit join syntax. The ORM automatically handles the necessary table joins behind the scenes. This flattens object graphs for read-heavy scenarios (like grids or reports) and eliminates the N+1 query problem for simple lookup values.

## Design Philosophy
1.  **Zero Ambiguity**: The system must never "guess" which path to take if multiple options exist. If the path from Entity A to Entity C is ambiguous (e.g., via `Hospital` or via `Patient`), the framework must throw an exception and require the user to specify the path explicitly.
2.  **Type Safety**: The API relies on `nameof` expressions to reference properties. This ensures that renaming properties in the IDE updates the mapping definitions, preventing "magic string" rot.
3.  **Performance**: Path resolution is expensive (graph traversal). Therefore, all paths must be resolved once during startup/reflection and cached. Runtime queries simply read the cached join chain.
4.  **Smart Inference**: The system should infer relationships based on naming conventions (e.g., `BillingAddressId` -> `AddressEntity`) to minimize attribute clutter.

## Public API

### 1. `RemoteLinkAttribute`
Explicitly specifies the target entity type for a foreign key property when the naming convention doesn't match.

```csharp
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class RemoteLinkAttribute : Attribute
{
    public Type TargetType { get; }
    public RemoteLinkAttribute(Type targetType) { ... }
}
```

### 2. `RemoteKeyAttribute`
Used to fetch the **Primary Key** of a distant entity.

```csharp
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class RemoteKeyAttribute : RemoteAttributeBase
{
    public RemoteKeyAttribute(Type remoteEntityType, params string[] keyPath) { ... }
}
```

### 3. `RemotePropertyAttribute`
Used to fetch a **Value** (non-key column) from a distant entity.

```csharp
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class RemotePropertyAttribute : RemoteAttributeBase
{
    public RemotePropertyAttribute(Type remoteEntityType, params string[] keyPath) { ... }
}
```

## Implementation Strategy

### 1. `RemotePathResolver` Service
A service responsible for validating and resolving paths at startup.
*   **Implicit Mode (Inference)**: Performs a Breadth-First Search (BFS) on the schema graph to find the shortest path from the Source Entity to `TRemoteEntity`.
    *   **Smart Inference**: Automatically detects Foreign Keys based on:
        *   `[RemoteLink]` attribute.
        *   Naming convention: `[Name]Id` where `[Name]` matches a known Entity type (e.g., `BillingAddressId` -> `AddressEntity`).
    *   If 0 paths found: Throw `PathNotFoundException`.
    *   If 1 path found: Return it.
    *   If >1 paths found (of equal length): Throw `AmbiguousMatchException`.
*   **Explicit Mode**: Validates that each property in the provided `keyPath` exists and forms a valid Foreign Key chain. If invalid, throw `PathNotFoundException`.

### 2. SQL Generation (`SqlServerOrmDataProvider`)
The `Select` query generator will:
*   Iterate through the cached `ResolvedRemotePath`s.
*   Generate `LEFT JOIN` statements for each step.
*   **Crucial**: Assign deterministic, unique, and descriptive aliases to each joined table.
    *   Format: `[TableName]_[Index]` (e.g., `person_0`, `person_address_0`, `organization_1`).
    *   Increment the numeric suffix when a table appears more than once in the join list or query to ensure uniqueness.
*   Append the final column (aliased) to the `SELECT` list.

### 3. Filtering Support (Where Clause)
The `WhereClauseVisitor` must be updated to support filtering by `[RemoteKey]` properties.
*   **Resolution**: When visiting a member expression, check if it maps to a `[RemoteKey]`.
*   **Substitution**: If mapped, replace the property access with the fully qualified aliased column name (e.g., `[country_0].[id]`) instead of the default table column.
*   **Join Injection**: Ensure that the necessary JOINs are injected into the query even if the property is not selected in the projection (though typically `Query<T>` selects all properties).
*   **Scope**: Initially limited to `[RemoteKey]` (IDs). Filtering by `[RemoteProperty]` (Values) is a future enhancement.

## Integration Test Requirements

### Database Schema Enhancements
To fully test deep remote key resolution, the integration test database must be enhanced with a chain of at least 3 steps.

**Schema Chain:**
`Person` (EmployerId) -> `Organization` (HeadquartersAddressId) -> `Address` (CountryId) -> `Country` (Name).
*   Distance: 3 hops (Person -> Organization -> Address -> Country).

### Test Scenarios
1.  **Outer Join Verification (Null Handling)**:
    *   Scenario: A `Person` exists with an `EmployerId`, but the `Organization` has a `NULL` `HeadquartersAddressId`.
    *   Expectation: The query returns the `Person` object. The remote property is `null`.
2.  **Full Chain Population**:
    *   Scenario: A `Person` exists with all links populated.
    *   Expectation: The remote property on `Person` is correctly populated.
3.  **Smart Inference**:
    *   Scenario: A property `BillingAddressId` exists without attributes.
    *   Expectation: The resolver correctly infers the link to `AddressEntity`.

## Example Usage

```csharp
public class Person : BaseEntity 
{
    // ... existing properties ...

    // Smart Inference: Infers OrganizationEntity from "EmployerId" if "Employer" was a type, 
    // but here we use attribute because property name doesn't match type name exactly.
    [RemoteLink(targetType: typeof(OrganizationEntity))]
    public int? EmployerId { get; set; }

    // --- Remote Property (Value) ---
    // Path: Person -> Organization -> Address -> Country
    // Target: Country.Name
    [RemoteProperty(remoteEntityType: typeof(CountryEntity), keyPath: new[] { nameof(CountryEntity.Name) })]
    public string EmployerHeadquartersCountryName { get; set; }

    // --- Remote Key (ID) ---
    // Target: Country.Id
    [RemoteKey(remoteEntityType: typeof(CountryEntity), keyPath: new[] { nameof(CountryEntity.Id) })]
    public int? EmployerHeadquartersCountryId { get; set; }
}
```

## Rules & Edge Cases
1.  **Always LEFT JOIN**: Remote keys are nullable by nature (the chain might break). Use `LEFT JOIN` to ensure the parent row is still returned even if the remote data is missing.
2.  **Read-Only**: These properties are computed/read-only. The `ObjectHydrator` will populate them, but `Update` operations must ignore them.
3.  **Property vs Column**: The `keyPath` arguments must match **Property Names** on the C# entities, not database column names. The resolver will map them to columns internally.
