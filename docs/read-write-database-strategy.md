# Read/Write Database Connection Strategy

The application never opens "a database connection". It opens either a **read**
connection or a **write** connection, and which one it may open is decided by
whether the operation in flight is a query or a command.

```
Query   -> MediatR query handler   -> ReadConnection  -> Dapper -> SQL Server read replica
Command -> MediatR command handler -> WriteConnection -> Dapper -> SQL Server primary
```

This document explains the configuration, the rule, how the rule is enforced,
and what to do in the one case where a read genuinely cannot be served from a
replica.

---

## 1. Configuration

Two connection strings, always both:

| Name              | Points at | Serves                                        |
| ----------------- | --------- | --------------------------------------------- |
| `ReadConnection`  | Replica   | Every query: listings, search, details, reports, dashboards |
| `WriteConnection` | Primary   | Every command and every transaction            |

On .NET 8 they live in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "ReadConnection":  "Server=READ-SERVER;Database=OnlineShop;Integrated Security=True;ApplicationIntent=ReadOnly;",
    "WriteConnection": "Server=WRITE-SERVER;Database=OnlineShop;Integrated Security=True;"
  }
}
```

The names are identical to the classic `<connectionStrings>` form, which maps
one-to-one:

```xml
<connectionStrings>
  <add
    name="ReadConnection"
    connectionString="Server=READ-SERVER;Database=OnlineShop;Integrated Security=True;"
    providerName="System.Data.SqlClient" />

  <add
    name="WriteConnection"
    connectionString="Server=WRITE-SERVER;Database=OnlineShop;Integrated Security=True;"
    providerName="System.Data.SqlClient" />
</connectionStrings>
```

`IConfiguration.GetConnectionString("ReadConnection")` reads the JSON section
above; on .NET Framework `ConfigurationManager.ConnectionStrings["ReadConnection"]`
reads the XML. Nothing else in the codebase changes.

> **On the driver.** `providerName="System.Data.SqlClient"` is a configuration
> token naming the ADO.NET provider. The code uses
> **`Microsoft.Data.SqlClient`**, the supported successor to the old
> `System.Data.SqlClient` package — it is the one that still receives fixes and
> supports current TLS and Azure AD authentication. Both expose `SqlConnection`,
> so `SqlConnectionFactory` is unchanged either way.

### Pointing `ReadConnection` at a real replica

For an Always On availability group, target the listener and declare read
intent:

```
Server=agl-onlineshop.contoso.com;Database=OnlineShop;ApplicationIntent=ReadOnly;MultiSubnetFailover=True;
```

SQL Server then routes the session to a readable secondary. Until a replica
exists, both strings may point at the same instance — the code paths are
identical, only the target differs. Startup logs a warning in that case, because
a single database hides read-after-write bugs (see §5).

---

## 2. The rule

```
Queries  -> ReadConnection
Commands -> WriteConnection
```

* A query handler may not use `WriteConnection` **unless there is a documented
  consistency requirement**.
* A command handler may not use `ReadConnection` at all. A write, or a read a
  write depends on, must not be based on possibly stale replica data.
* **Transactions always use `WriteConnection`.**

### Read side

| Operation                    | Connection       |
| ---------------------------- | ---------------- |
| `SELECT`, search             | `ReadConnection` |
| Product listing / details    | `ReadConnection` |
| Order listing / details      | `ReadConnection` |
| Customer listing             | `ReadConnection` |
| Reports, dashboard, analytics| `ReadConnection` |

Handlers: `GetProductsQuery`, `GetProductByIdQuery`, `GetOrdersQuery`,
`GetOrderDetailsQuery`, `GetShopDashboardQuery`.

### Write side

| Operation                       | Connection        |
| ------------------------------- | ----------------- |
| `INSERT`, `UPDATE`, `DELETE`    | `WriteConnection` |
| Transactions                    | `WriteConnection` |
| Inventory updates               | `WriteConnection` |
| Order creation, payment records | `WriteConnection` |
| Member management, shop config  | `WriteConnection` |

Handlers: `CreateShopCommand`, `CreateProductCommand`, `UpdateProductCommand`,
`DeleteProductCommand`, `PlaceOrderCommand`, `CancelOrderCommand`,
`InviteShopMemberCommand`.

---

## 3. The connection factory

There is no generic `CreateConnection()`. The caller must state its intent:

```csharp
public interface IDbConnectionFactory
{
    IDbConnection CreateReadConnection();
    IDbConnection CreateWriteConnection();
}
```

`SqlConnectionFactory` maps each name to its connection string and does nothing
else. Read models call it like this:

```csharp
using (var connection = _connectionFactory.CreateReadConnection())
{
    return await connection.QueryAsync<ProductDto>(sql, parameters);
}
```

### How the rule is enforced, not just documented

A comment cannot stop someone typing `CreateWriteConnection()` in a query
handler. Three mechanisms do:

**1. Every request is typed as a query or a command.**
`IQuery<T>` and `ICommand<T>` are markers on the request itself.
`CqrsRequestShapeTests` fails the build if a request is both, or neither.

**2. A pipeline behavior publishes which one is running.**
`DbOperationScopeBehavior` opens an `AsyncLocal` scope around every handler
recording `Query` or `Command`. Being flow-local, it follows `await`
continuations down into the query classes and does not leak between concurrent
requests.

**3. The factory is wrapped in a guard.**
`CqrsGuardedDbConnectionFactory` decorates `SqlConnectionFactory` and throws
`CqrsConnectionViolationException` when the wrong connection is requested. The
undecorated factory is never registered in the container, so there is nothing to
reach around.

Outside a MediatR request — a migration runner, a background job — the scope is
`Unspecified` and nothing is blocked.

---

## 4. Transactions

`IUnitOfWork` is the only place a write connection is opened and a transaction
is begun:

```csharp
await _unitOfWork.ExecuteAsync(async (transaction, ct) =>
{
    await _orderRepository.InsertAsync(order, transaction, ct);
    await _inventoryRepository.TryReserveAsync(tenantId, productId, qty, transaction, ct);
    await _orderRepository.InsertPaymentAsync(payment, transaction, ct);
    // ...
}, cancellationToken);
```

which expands to exactly the classic shape, with commit and rollback handled for
you:

```
WriteConnection
BEGIN TRANSACTION
  ... work ...
COMMIT                (ROLLBACK if anything throws)
```

### One connection, one transaction, every participant

`PlaceOrderCommand` is the worked example:

```
BEGIN TRANSACTION
  Create Order
  Create Order Items
  Reserve / Update Inventory
  Create Payment Record
  Update Cart
  Create Order Status History
COMMIT
```

All six steps receive **the same `IDbTransaction`**. If stock runs out at step
three, the order and its items vanish with the rollback; there is no
compensating cleanup to get wrong.

### Repositories never open a connection

Every write-repository method takes the transaction it must join, and gets its
connection from that transaction:

```csharp
var connection = transaction.RequireConnection();
```

No repository has an `IDbConnectionFactory` field — it is structurally incapable
of opening a second connection mid-transaction. `LayeringTests` fails the build
if one acquires the field, and `RequireConnection` throws with an explanatory
message if a transaction that has already completed is passed in.

Isolation level defaults to `ReadCommitted`. `PlaceOrderCommand` raises it to
`RepeatableRead` so concurrent checkouts contending for the last unit serialise
on the inventory rows.

### A note on committing

The commit deliberately does **not** take the request's cancellation token. Once
the work has succeeded, aborting mid-commit leaves the outcome genuinely
unknown — the server may have committed anyway — and the rollback that followed
could not put it right. Cancellation is honoured up to that point.

---

## 5. Read replicas and read-after-write

Replication is asynchronous:

```
Create Product -> Write Database -> (replication lag) -> Read Replica
```

so a read issued immediately after a write may not see it:

```
Create Product
  -> Immediately request Product
     -> Read replica does not have it yet -> 404 on a row the user just saved
```

The escape hatch is explicit and narrow:

```csharp
public enum ReadConsistency
{
    Eventual,   // -> ReadConnection   (default)
    Strong,     // -> WriteConnection  (read-after-write)
}
```

```csharp
Task<ProductDto?> GetByIdAsync(Guid tenantId, Guid productId, ReadConsistency consistency);
```

The mapping lives in exactly one place, `DbConnectionFactoryExtensions.CreateConnection`,
so it cannot drift between query classes.

### `Strong` has to be justified in writing

A query may only ask for `Strong` if its type carries the reason:

```csharp
[StrongConsistencyAllowed(
    "Checkout redirects to the order confirmation page immediately after the write transaction commits. " +
    "Replication lag would show the shopper a missing order for the purchase they just completed.")]
public sealed record GetOrderDetailsQuery(Guid OrderId, ReadConsistency Consistency = ReadConsistency.Eventual)
    : IQuery<OrderDetailsDto>, ISupportsReadConsistency;
```

Asking for `Strong` without the attribute throws at run time, and
`CqrsRequestShapeTests` fails the build. This is what stops "just use Strong" from
quietly becoming the default and moving storefront load back onto the primary.

Today exactly two queries carry the allowance — `GetProductByIdQuery` and
`GetOrderDetailsQuery` — and both default to `Eventual`. The dashboard has no
consistency parameter at all: analytics never reads the primary.

### Why local development lies

With one database, replication lag is zero, so a read that *should* have asked
for `Strong` looks perfectly correct locally and fails only once a real replica
appears. Startup logs a warning when both connection strings match, as a
standing reminder.

---

## 6. Tenant isolation

Every tenant-owned statement — read and write — filters on `TenantId`.

```sql
SELECT Id, Name, SKU, Price
FROM Products
WHERE TenantId = @TenantId
  AND Id = @ProductId;
```

```sql
UPDATE Products
SET Name = @Name, Price = @Price, UpdatedAt = SYSUTCDATETIME()
WHERE TenantId = @TenantId
  AND Id = @ProductId;
```

Never `WHERE Id = @Id` alone.

Four layers back this up:

1. **`TenantGuard`** rejects an empty `TenantId` at the start of every query and
   repository method, turning a missing tenant into an immediate failure rather
   than a statement that matches nothing.
2. **`TenantIsolationSqlTests`** reflects over every SQL constant in the layer,
   splits it into statements, and fails the build if one lacks `@TenantId` or
   contains a quoted literal. It covers SQL written long after this document.
3. **Composite foreign keys** in the schema — `(TenantId, ParentId)` references
   `(TenantId, Id)` — make a cross-tenant reference impossible to *store*, not
   merely impossible to write through the application.
4. **`TenantId` leads every clustered key**, so tenant data is physically
   co-located and a query missing its predicate degrades into an obvious scan.

A row belonging to another tenant is indistinguishable from a row that does not
exist, which is deliberate: it stops a caller probing for other tenants'
identifiers.

---

## 7. Layout

```
src/OnlineShop.Persistence/
    Connections/
        IDbConnectionFactory.cs             read/write abstraction
        SqlConnectionFactory.cs             name -> connection string
        CqrsGuardedDbConnectionFactory.cs   enforces the rule
        DbOperationContext.cs               flow-local query/command scope
        DbConnectionFactoryExtensions.cs    ReadConsistency -> connection
    Transactions/
        UnitOfWork.cs                       the only place a transaction begins
    Queries/                                read models -> ReadConnection
        ProductQueries.cs
        OrderQueries.cs
        CustomerQueries.cs
        DashboardQueries.cs
    Repositories/                           write repositories -> caller's transaction
        ProductRepository.cs
        OrderRepository.cs
        ShopRepository.cs
        CustomerRepository.cs
        InventoryRepository.cs
```

Read models return DTOs and project only what a screen needs. Write repositories
take and return domain aggregates. They are separate on purpose: the read side
joins freely across tables the write side keeps apart.

Interfaces live in `OnlineShop.Application/Abstractions/Persistence` so handlers
depend on ports, not on the persistence assembly.

---

## 8. The rules, and where each one lives

| # | Rule | Where it is met |
| - | ---- | --------------- |
| 1 | No Entity Framework | No EF package anywhere; `LayeringTests.No_assembly_references_entity_framework` |
| 2 | Dapper / ADO.NET | `Dapper` + `Microsoft.Data.SqlClient` in `OnlineShop.Persistence` |
| 3 | Two connection strings | `PersistenceOptions`, `appsettings.json` |
| 4 | Queries use `ReadConnection` | `*Queries` classes; enforced by `CqrsGuardedDbConnectionFactory` |
| 5 | Commands use `WriteConnection` | `UnitOfWork`; enforced by the same guard |
| 6 | Transactions always write | `UnitOfWork` calls `CreateWriteConnection()` only; `UnitOfWorkTests` |
| 7 | `TenantId` enforced both ways | `TenantGuard`, `TenantIsolationSqlTests`, composite FKs |
| 8 | `ReadConnection` may be a replica | `ApplicationIntent=ReadOnly` sample; §5 |
| 9 | `WriteConnection` is the primary | `SqlConnectionFactory` |
| 10 | Explicit strong consistency | `ReadConsistency`, `StrongConsistencyAllowedAttribute` |
| 11 | No new connection inside a transaction | Repositories take `IDbTransaction`, hold no factory; `LayeringTests` |
| 12 | All SQL parameterised | `DynamicParameters` / anonymous parameters; `TenantIsolationSqlTests.No_statement_interpolates_a_value` |
| 13 | Connections stay in persistence | Domain has zero package references; `LayeringTests.Endpoints_do_not_manage_connections_or_transactions` |

---

## 9. How this is verified

The rules above are checked two ways.

**Without a database.** `OnlineShop.Architecture.Tests` reflects over the
compiled assemblies: every request is a query or a command, every SQL constant
mentions `@TenantId` and contains no quoted literal, no repository holds a
connection factory, no endpoint touches a connection, Entity Framework appears
nowhere. It also parses every statement and every schema batch with SQL Server's
own T-SQL parser, so a malformed statement fails the build rather than a request.

**With a real database.** `OnlineShop.Integration.Tests` starts SQL Server 2022
and creates two databases — `OnlineShop_Primary` behind `WriteConnection` and
`OnlineShop_Replica` behind `ReadConnection` — with nothing synchronising them.
Replication lag is therefore unbounded and deterministic, which turns claims
into observations:

* a command changes the primary and leaves the replica byte-for-byte unchanged;
* a query returns a row planted *only* in the replica, so it demonstrably read
  the replica;
* a read issued straight after a write misses, `ReadConsistency.Strong` finds
  it, and the ordinary path works once replication runs;
* when the primary and replica are made to disagree about the same row, each
  consistency level returns the value from the database it is supposed to use;
* a command prices an order from the primary even when the replica says
  otherwise;
* every query still succeeds when the replica is set `READ_ONLY` at the server,
  which is the strongest available evidence that the read path performs no
  writes;
* six simultaneous checkouts for one unit of stock produce exactly one order,
  and eight concurrent checkouts receive eight distinct order numbers;
* a failure at step three of order placement leaves no order, no items, no
  payment, no history and no reservation;
* cross-tenant reads, updates, deletes and checkouts all fail, and the composite
  foreign keys reject a cross-tenant row inserted by hand.

Both suites have been checked against deliberately broken implementations —
removing the tenant predicate, removing the oversell guard, misrouting
`ReadConsistency.Strong`, giving a repository its own connection factory — and
each mutation was caught by several tests.

---

## 10. Adding to the codebase

**A new read.** Add a method to the relevant `I*Queries` interface taking
`Guid tenantId`. Implement it with `CreateReadConnection()`. Include
`TenantId = @TenantId`. Add an `IQuery<T>` request and handler. Do not add a
consistency parameter unless a specific caller genuinely reads its own write.

**A new write.** Add a method to the relevant `I*Repository` interface taking
`IDbTransaction`. Get the connection with `transaction.RequireConnection()`.
Include `TenantId = @TenantId`. Add an `ICommand<T>` request whose handler wraps
the work in `IUnitOfWork.ExecuteAsync`.

**A read that must see its own write.** Prefer not to. If it is unavoidable, add
`ISupportsReadConsistency` to the query and
`[StrongConsistencyAllowed("...")]` explaining what breaks otherwise, then pass
`ReadConsistency.Strong` from the one caller that needs it — not from the
default.
