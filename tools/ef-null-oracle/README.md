# EF Core null-semantics oracle

The reference for docs/plans/NULL_SEMANTICS_PLAN.md (3.12.0): what EF Core sends for null-sensitive WHERE
predicates under its default C# null semantics (`UseRelationalNulls(false)`), and how EF Core's queryables behave
with an `is IOrderedQueryable` sort helper (GitHub #16).

`ToQueryString()` builds the SQL without opening a connection, so no database is needed. It prints every predicate's
WHERE for SQL Server and for SQLite.

```
dotnet run --project tools/ef-null-oracle -c Release
```

It is not part of FunkyORM.sln and nothing references it. The Pkcs package pin keeps NuGet's audit quiet
(CVE-2026-84008 / CVE-2026-70162 come in through Microsoft.Data.SqlClient).
