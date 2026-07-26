# OnlineShop

A multi-tenant online shop built around a strict read/write database split:
CQRS over MediatR, Dapper on SQL Server, no Entity Framework.

Queries read a replica, commands write the primary, and the split is enforced by
the process rather than left to code review.

```
Query   -> MediatR query handler   -> ReadConnection  -> Dapper -> SQL Server read replica
Command -> MediatR command handler -> WriteConnection -> Dapper -> SQL Server primary
```

Full design: **[docs/read-write-database-strategy.md](docs/read-write-database-strategy.md)**

---

## Layout

```
src/
  OnlineShop.Domain/         aggregates and invariants; zero package references
  OnlineShop.Application/    CQRS requests, handlers, persistence ports, DTOs
  OnlineShop.Persistence/    connections, transactions, read models, repositories
  OnlineShop.Api/            minimal-API host; sends requests, touches no connection
tests/
  OnlineShop.Architecture.Tests/
database/
  schema.sql                 SQL Server schema for the primary
docs/
  read-write-database-strategy.md
```

Dependencies point inward: `Api -> Persistence -> Application -> Domain`. The
domain knows nothing about how it is stored.

## Getting started

```bash
dotnet build
dotnet test
```

To run against a database, apply `database/schema.sql` to the **primary**, set
the two connection strings, and start the API:

```bash
dotnet run --project src/OnlineShop.Api
```

Swagger is served at `/swagger` in development.

### Connection strings

```json
{
  "ConnectionStrings": {
    "ReadConnection":  "Server=READ-SERVER;Database=OnlineShop;Integrated Security=True;ApplicationIntent=ReadOnly;",
    "WriteConnection": "Server=WRITE-SERVER;Database=OnlineShop;Integrated Security=True;"
  }
}
```

Both may point at one instance until a replica exists — the code paths are
identical, only the target differs. Startup warns when they match, because a
single database hides read-after-write bugs.

In development, `Tenancy:AllowHeaderTenantResolution` lets an `X-Tenant-Id`
header stand in for the `tenant_id` claim. The host refuses to start with that
enabled outside development, where it would be an authorisation bypass.

## What the tests enforce

`OnlineShop.Architecture.Tests` is not a unit-test suite for business logic; it
holds the architecture to its rules on every build.

| Test | Fails when |
| ---- | ---------- |
| `CqrsConnectionRuleTests` | A query reaches the write connection undocumented, or a command reaches the read connection |
| `DbOperationScopeBehaviorTests` | Request classification or the strong-consistency allowance breaks |
| `TenantIsolationSqlTests` | Any SQL statement omits `@TenantId` or embeds a literal instead of a parameter |
| `LayeringTests` | EF appears, the domain gains a data dependency, an endpoint touches a connection, or a repository acquires a connection factory |
| `CqrsRequestShapeTests` | A request is both a query and a command, or neither, or asks for `Strong` without documenting why |
| `UnitOfWorkTests` | A transaction uses the wrong connection, or fails to commit or roll back |

## Design decisions worth knowing

**Commands cannot read the replica.** Not just discouraged — the guard throws.
A write based on a stale read is a correctness bug that surfaces as data
corruption much later.

**Repositories have no connection factory.** They take the caller's
`IDbTransaction` and use its connection, so they cannot open a second connection
and place work outside the transaction that was supposed to protect it.

**Overselling is prevented in the `WHERE` clause.** Stock reservation is a single
conditional `UPDATE`, not a `SELECT` followed by an `UPDATE`. Concurrent
checkouts serialise on the row lock and exactly one succeeds.

**Products are soft-deleted.** Order items reference the catalog row for their
historical SKU and name; deleting it would rewrite past orders.

**Strong consistency must be justified in writing.** `[StrongConsistencyAllowed]`
carries the reason, and the build fails without it — otherwise "just use Strong"
gradually moves storefront traffic back onto the primary.
