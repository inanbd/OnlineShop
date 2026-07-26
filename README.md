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
  OnlineShop.Persistence/    connections, transactions, read models, repositories,
                             and the Dapper-backed Identity stores
  OnlineShop.Api/            one host: Razor Pages UI + JSON API + Bootstrap assets
tests/
  OnlineShop.Architecture.Tests/  rules and SQL syntax; no database needed
  OnlineShop.Integration.Tests/   real SQL Server, primary + replica
database/
  schema.sql                 SQL Server schema for the primary
docs/
  read-write-database-strategy.md
```

## The UI

Server-rendered Razor Pages in the same host and the same process as the JSON
API. Pages send MediatR requests directly — no HTTP hop — so the CQRS connection
guard applies to them unchanged, and `LayeringTests` holds page models to the
same no-connection rule as the API endpoints.

| Area | Pages |
| ---- | ----- |
| Back-office (`/Manage`, merchants only) | Dashboard, product list/create/edit/delete, order list/detail/cancel, shop list/create, member invitations |
| Storefront (`/shop/{slug}`) | Catalog with search, product detail, basket, checkout, order confirmation |
| Accounts | Merchant registration (provisions tenant + first shop + owner), shopper registration per storefront, sign in/out |

Bootstrap 5 and jQuery validation are served from `wwwroot`; nothing is fetched
from a CDN and there is no npm build step.

**Every page shows a badge saying which database served it** — `replica ·
eventual` or `primary · strong`. The read/write split is the point of this
codebase, so the UI states it rather than leaving it implicit. Landing on the
basket after adding an item, the product editor after a save, or the order
confirmation after checkout all show `primary`; browsing shows `replica`.

### Two decisions worth knowing

**Identity runs on a hand-written Dapper store.** ASP.NET Core Identity's
default store is Entity Framework, which rule 1 forbids, so
`IUserStore`/`IRoleStore` are implemented directly
(`OnlineShop.Persistence/Identity`). Those stores read the **write** connection
on purpose — authenticating against a replica that has not yet seen a password
change or a lockout is a security bug, not a stale-UI annoyance.

**The storefront resolves its tenant from the URL.** A shopper at `/shop/acme`
has no tenant yet, so `IShopDirectory` performs the one deliberately global
lookup in the application and publishes the result to `ITenantContext`. The slug
comes from the URL; the tenant comes from the row that slug resolved to, so a
caller cannot name a tenant of their choosing. It is the only tenant-agnostic
read model, and a test pins that.

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

## Tests

```bash
dotnet test                                    # everything
dotnet test tests/OnlineShop.Architecture.Tests # no database needed
dotnet test tests/OnlineShop.Integration.Tests  # needs Docker
```

### Architecture tests — hold the design to its rules, no database needed

| Test | Fails when |
| ---- | ---------- |
| `CqrsConnectionRuleTests` | A query reaches the write connection undocumented, or a command reaches the read connection |
| `DbOperationScopeBehaviorTests` | Request classification or the strong-consistency allowance breaks |
| `TenantIsolationSqlTests` | Any SQL statement omits `@TenantId` or embeds a literal instead of a parameter |
| `LayeringTests` | EF appears, the domain gains a data dependency, an endpoint touches a connection, or a repository acquires a connection factory |
| `CqrsRequestShapeTests` | A request is both a query and a command, or neither, or asks for `Strong` without documenting why |
| `UnitOfWorkTests` | A transaction uses the wrong connection, or fails to commit or roll back |
| `SqlSyntaxTests` | Any statement or schema batch fails to parse under SQL Server's own T-SQL parser |

Two of those rules now cover the UI as well: `Razor_pages_do_not_manage_connections_or_transactions`
and `Only_the_storefront_directory_is_tenant_agnostic`.

### Integration tests — real SQL Server, two real databases

Testcontainers starts SQL Server 2022 and creates **two** databases:
`OnlineShop_Primary` behind `WriteConnection` and `OnlineShop_Replica` behind
`ReadConnection`. Nothing synchronises them, which is the point — replication
lag is unbounded and deterministic, so "which connection did this use" becomes
an observable fact rather than an assumption.

| Test | What it proves |
| ---- | -------------- |
| `ConnectionRoutingTests` | Commands change the primary and leave the replica untouched; queries return rows that exist *only* on the replica |
| `ReadAfterWriteTests` | A default read misses a row just written, `ReadConsistency.Strong` finds it, and replication catches up; a command prices an order from the primary even when the replica disagrees |
| `ReadOnlyReplicaTests` | Every query succeeds against a database SQL Server has been set `READ_ONLY` — proof the read path performs no writes |
| `TransactionTests` | All six order steps commit together or roll back together, including a duplicate-payment abort |
| `ConcurrencyTests` | Six simultaneous checkouts for one unit: exactly one wins; eight concurrent orders get eight distinct numbers |
| `TenantIsolationTests` | Cross-tenant reads, updates, deletes and checkouts all fail, and composite foreign keys reject cross-tenant rows at the database |
| `QueryTests` | Every read-model statement runs and maps, including `%` and `_` treated as literals in search |
| `RepositoryTests` | The repository methods no command reaches still execute correctly |
| `CartTests` | Basket lines merge, quantities are absolute, and the basket read after a write needs strong consistency |
| `IdentityStoreTests` | The Dapper user and role stores round-trip users, roles, lockout and concurrency stamps, and reject cross-tenant customer links |
| `StorefrontTests` | Slug resolution lands on exactly one tenant, inactive shops are unreachable, and merchant provisioning is atomic |

No Docker? The integration tests report themselves as **skipped**, not failed.
Set `ONLINESHOP_TEST_SQL` to an existing server's master connection string to
use that instead of a container.

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
