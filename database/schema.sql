/*
    OnlineShop - SQL Server schema
    ==============================

    Applied to the PRIMARY database, the one WriteConnection points at.
    A read replica receives this schema through replication; it is never
    created there by hand.

    Two conventions run through every table:

      1. Every tenant-owned table carries TenantId, and it is the leading
         column of the clustered key. Tenant data is therefore physically
         co-located, and a query that forgets its TenantId predicate degrades
         into an obvious scan rather than quietly returning another tenant's
         rows.

      2. Foreign keys are composite: (TenantId, ParentId) references
         (TenantId, Id). This makes a cross-tenant reference impossible to
         store, not merely impossible to write through the application. It is
         the backstop for the rule that every statement filters on TenantId.
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ------------------------------------------------------------------ */
/* Tenants                                                            */
/* ------------------------------------------------------------------ */

CREATE TABLE dbo.Tenants
(
    Id          uniqueidentifier NOT NULL CONSTRAINT PK_Tenants PRIMARY KEY CLUSTERED,
    Name        nvarchar(200)    NOT NULL,
    Slug        varchar(100)     NOT NULL,
    CreatedAt   datetime2(3)     NOT NULL CONSTRAINT DF_Tenants_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt   datetime2(3)     NOT NULL CONSTRAINT DF_Tenants_UpdatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT UQ_Tenants_Slug UNIQUE (Slug)
);
GO

/* ------------------------------------------------------------------ */
/* Shops                                                              */
/* ------------------------------------------------------------------ */

CREATE TABLE dbo.Shops
(
    Id           uniqueidentifier NOT NULL,
    TenantId     uniqueidentifier NOT NULL,
    Name         nvarchar(200)    NOT NULL,
    Slug         varchar(100)     NOT NULL,
    CurrencyCode char(3)          NOT NULL,
    IsActive     bit              NOT NULL CONSTRAINT DF_Shops_IsActive DEFAULT 1,
    CreatedAt    datetime2(3)     NOT NULL CONSTRAINT DF_Shops_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt    datetime2(3)     NOT NULL CONSTRAINT DF_Shops_UpdatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_Shops PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_Shops_Tenant_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT UQ_Shops_Tenant_Slug UNIQUE (TenantId, Slug),
    CONSTRAINT FK_Shops_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id)
);
GO

CREATE TABLE dbo.ShopMembers
(
    Id              uniqueidentifier NOT NULL,
    TenantId        uniqueidentifier NOT NULL,
    ShopId          uniqueidentifier NOT NULL,
    Email           nvarchar(320)    NOT NULL,
    Role            int              NOT NULL,  -- ShopMemberRole
    Status          int              NOT NULL,  -- ShopMemberStatus
    InvitedByUserId uniqueidentifier NOT NULL,
    InvitedAt       datetime2(3)     NOT NULL,
    UserId          uniqueidentifier NULL,
    AcceptedAt      datetime2(3)     NULL,
    CreatedAt       datetime2(3)     NOT NULL CONSTRAINT DF_ShopMembers_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt       datetime2(3)     NOT NULL CONSTRAINT DF_ShopMembers_UpdatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_ShopMembers PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_ShopMembers_Tenant_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT UQ_ShopMembers_Tenant_Shop_Email UNIQUE (TenantId, ShopId, Email),
    CONSTRAINT FK_ShopMembers_Shops FOREIGN KEY (TenantId, ShopId) REFERENCES dbo.Shops (TenantId, Id)
);
GO

/* ------------------------------------------------------------------ */
/* Catalog                                                            */
/* ------------------------------------------------------------------ */

CREATE TABLE dbo.Products
(
    Id           uniqueidentifier NOT NULL,
    TenantId     uniqueidentifier NOT NULL,
    ShopId       uniqueidentifier NOT NULL,
    Name         nvarchar(300)    NOT NULL,
    Sku          varchar(64)      NOT NULL,
    Description  nvarchar(max)    NULL,
    Price        decimal(19, 4)   NOT NULL,
    CurrencyCode char(3)          NOT NULL,
    Status       int              NOT NULL,  -- ProductStatus
    CreatedAt    datetime2(3)     NOT NULL CONSTRAINT DF_Products_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt    datetime2(3)     NOT NULL CONSTRAINT DF_Products_UpdatedAt DEFAULT SYSUTCDATETIME(),
    DeletedAt    datetime2(3)     NULL,

    CONSTRAINT PK_Products PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_Products_Tenant_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT CK_Products_Price CHECK (Price >= 0),
    CONSTRAINT FK_Products_Shops FOREIGN KEY (TenantId, ShopId) REFERENCES dbo.Shops (TenantId, Id)
);
GO

-- A SKU is unique per shop among products that are still live. Filtering on
-- DeletedAt lets a SKU be reused after a product is soft-deleted.
CREATE UNIQUE INDEX UX_Products_Tenant_Shop_Sku
    ON dbo.Products (TenantId, ShopId, Sku)
    WHERE DeletedAt IS NULL;
GO

-- Serves the catalog listing: filter on tenant/shop/status, order by name.
CREATE INDEX IX_Products_Tenant_Shop_Status_Name
    ON dbo.Products (TenantId, ShopId, Status, Name)
    INCLUDE (Sku, Price, CurrencyCode, UpdatedAt, DeletedAt);
GO

CREATE TABLE dbo.InventoryItems
(
    Id               uniqueidentifier NOT NULL,
    TenantId         uniqueidentifier NOT NULL,
    ShopId           uniqueidentifier NOT NULL,
    ProductId        uniqueidentifier NOT NULL,
    QuantityOnHand   int              NOT NULL CONSTRAINT DF_InventoryItems_OnHand DEFAULT 0,
    QuantityReserved int              NOT NULL CONSTRAINT DF_InventoryItems_Reserved DEFAULT 0,
    ReorderThreshold int              NOT NULL CONSTRAINT DF_InventoryItems_Reorder DEFAULT 0,
    CreatedAt        datetime2(3)     NOT NULL CONSTRAINT DF_InventoryItems_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt        datetime2(3)     NOT NULL CONSTRAINT DF_InventoryItems_UpdatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_InventoryItems PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_InventoryItems_Tenant_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT UQ_InventoryItems_Tenant_Product UNIQUE (TenantId, ProductId),

    -- The database's own guarantee against overselling. The application's
    -- conditional UPDATE is the first line of defence; this is the second, and
    -- it holds even against a bad ad-hoc statement run by hand.
    CONSTRAINT CK_InventoryItems_OnHand CHECK (QuantityOnHand >= 0),
    CONSTRAINT CK_InventoryItems_Reserved CHECK (QuantityReserved >= 0),
    CONSTRAINT CK_InventoryItems_NotOverReserved CHECK (QuantityReserved <= QuantityOnHand),

    CONSTRAINT FK_InventoryItems_Products FOREIGN KEY (TenantId, ProductId) REFERENCES dbo.Products (TenantId, Id),
    CONSTRAINT FK_InventoryItems_Shops FOREIGN KEY (TenantId, ShopId) REFERENCES dbo.Shops (TenantId, Id)
);
GO

-- Serves the dashboard's low-stock counter.
CREATE INDEX IX_InventoryItems_Tenant_Shop
    ON dbo.InventoryItems (TenantId, ShopId)
    INCLUDE (ProductId, QuantityOnHand, QuantityReserved, ReorderThreshold);
GO

/* ------------------------------------------------------------------ */
/* Customers and carts                                                */
/* ------------------------------------------------------------------ */

CREATE TABLE dbo.Customers
(
    Id        uniqueidentifier NOT NULL,
    TenantId  uniqueidentifier NOT NULL,
    ShopId    uniqueidentifier NOT NULL,
    Email     nvarchar(320)    NOT NULL,
    FullName  nvarchar(200)    NOT NULL,
    CreatedAt datetime2(3)     NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt datetime2(3)     NOT NULL CONSTRAINT DF_Customers_UpdatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_Customers PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_Customers_Tenant_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT UQ_Customers_Tenant_Shop_Email UNIQUE (TenantId, ShopId, Email),
    CONSTRAINT FK_Customers_Shops FOREIGN KEY (TenantId, ShopId) REFERENCES dbo.Shops (TenantId, Id)
);
GO

-- Serves the customer listing, which orders by CreatedAt descending.
CREATE INDEX IX_Customers_Tenant_Shop_CreatedAt
    ON dbo.Customers (TenantId, ShopId, CreatedAt DESC)
    INCLUDE (Email, FullName);
GO

CREATE TABLE dbo.Carts
(
    Id         uniqueidentifier NOT NULL,
    TenantId   uniqueidentifier NOT NULL,
    ShopId     uniqueidentifier NOT NULL,
    CustomerId uniqueidentifier NOT NULL,
    Status     int              NOT NULL,  -- CartStatus
    CreatedAt  datetime2(3)     NOT NULL CONSTRAINT DF_Carts_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt  datetime2(3)     NOT NULL CONSTRAINT DF_Carts_UpdatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_Carts PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_Carts_Tenant_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT FK_Carts_Shops FOREIGN KEY (TenantId, ShopId) REFERENCES dbo.Shops (TenantId, Id),
    CONSTRAINT FK_Carts_Customers FOREIGN KEY (TenantId, CustomerId) REFERENCES dbo.Customers (TenantId, Id)
);
GO

CREATE INDEX IX_Carts_Tenant_Shop_Customer_Status
    ON dbo.Carts (TenantId, ShopId, CustomerId, Status)
    INCLUDE (CreatedAt);
GO

CREATE TABLE dbo.CartItems
(
    Id        uniqueidentifier NOT NULL,
    TenantId  uniqueidentifier NOT NULL,
    CartId    uniqueidentifier NOT NULL,
    ProductId uniqueidentifier NOT NULL,
    Quantity  int              NOT NULL,
    UnitPrice decimal(19, 4)   NOT NULL,

    CONSTRAINT PK_CartItems PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_CartItems_Tenant_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT UQ_CartItems_Tenant_Cart_Product UNIQUE (TenantId, CartId, ProductId),
    CONSTRAINT CK_CartItems_Quantity CHECK (Quantity > 0),
    CONSTRAINT CK_CartItems_UnitPrice CHECK (UnitPrice >= 0),
    CONSTRAINT FK_CartItems_Carts FOREIGN KEY (TenantId, CartId) REFERENCES dbo.Carts (TenantId, Id),
    CONSTRAINT FK_CartItems_Products FOREIGN KEY (TenantId, ProductId) REFERENCES dbo.Products (TenantId, Id)
);
GO

/* ------------------------------------------------------------------ */
/* Orders                                                             */
/* ------------------------------------------------------------------ */

CREATE TABLE dbo.Orders
(
    Id                 uniqueidentifier NOT NULL,
    TenantId           uniqueidentifier NOT NULL,
    ShopId             uniqueidentifier NOT NULL,
    CustomerId         uniqueidentifier NOT NULL,
    OrderNumber        varchar(32)      NOT NULL,
    Status             int              NOT NULL,  -- OrderStatus
    Subtotal           decimal(19, 4)   NOT NULL,
    TaxTotal           decimal(19, 4)   NOT NULL,
    GrandTotal         decimal(19, 4)   NOT NULL,
    CurrencyCode       char(3)          NOT NULL,
    PlacedAt           datetime2(3)     NOT NULL,
    CreatedAt          datetime2(3)     NOT NULL CONSTRAINT DF_Orders_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt          datetime2(3)     NOT NULL CONSTRAINT DF_Orders_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CancelledAt        datetime2(3)     NULL,
    CancellationReason nvarchar(500)    NULL,

    CONSTRAINT PK_Orders PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_Orders_Tenant_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT UQ_Orders_Tenant_Shop_OrderNumber UNIQUE (TenantId, ShopId, OrderNumber),
    CONSTRAINT CK_Orders_Totals CHECK (Subtotal >= 0 AND TaxTotal >= 0 AND GrandTotal >= 0),
    CONSTRAINT FK_Orders_Shops FOREIGN KEY (TenantId, ShopId) REFERENCES dbo.Shops (TenantId, Id),
    CONSTRAINT FK_Orders_Customers FOREIGN KEY (TenantId, CustomerId) REFERENCES dbo.Customers (TenantId, Id)
);
GO

-- Serves the order listing and every dashboard aggregate, all of which filter
-- by shop and a PlacedAt window.
CREATE INDEX IX_Orders_Tenant_Shop_PlacedAt
    ON dbo.Orders (TenantId, ShopId, PlacedAt DESC)
    INCLUDE (CustomerId, Status, GrandTotal, CurrencyCode, OrderNumber);
GO

CREATE INDEX IX_Orders_Tenant_Customer_PlacedAt
    ON dbo.Orders (TenantId, CustomerId, PlacedAt DESC)
    INCLUDE (Status, GrandTotal);
GO

CREATE TABLE dbo.OrderItems
(
    Id          uniqueidentifier NOT NULL,
    TenantId    uniqueidentifier NOT NULL,
    OrderId     uniqueidentifier NOT NULL,
    ProductId   uniqueidentifier NOT NULL,

    -- Copied at order time. The catalog may be renamed, repriced or deleted
    -- later; a historical order must still read the way it was placed.
    Sku         varchar(64)      NOT NULL,
    ProductName nvarchar(300)    NOT NULL,

    Quantity    int              NOT NULL,
    UnitPrice   decimal(19, 4)   NOT NULL,
    LineTotal   decimal(19, 4)   NOT NULL,

    CONSTRAINT PK_OrderItems PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_OrderItems_Tenant_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT CK_OrderItems_Quantity CHECK (Quantity > 0),
    CONSTRAINT CK_OrderItems_UnitPrice CHECK (UnitPrice >= 0),
    CONSTRAINT FK_OrderItems_Orders FOREIGN KEY (TenantId, OrderId) REFERENCES dbo.Orders (TenantId, Id),
    CONSTRAINT FK_OrderItems_Products FOREIGN KEY (TenantId, ProductId) REFERENCES dbo.Products (TenantId, Id)
);
GO

CREATE INDEX IX_OrderItems_Tenant_Order
    ON dbo.OrderItems (TenantId, OrderId)
    INCLUDE (ProductId, Sku, ProductName, Quantity, UnitPrice, LineTotal);
GO

-- Serves the dashboard's best-seller aggregate.
CREATE INDEX IX_OrderItems_Tenant_Product
    ON dbo.OrderItems (TenantId, ProductId)
    INCLUDE (OrderId, Quantity, LineTotal);
GO

CREATE TABLE dbo.Payments
(
    Id                uniqueidentifier NOT NULL,
    TenantId          uniqueidentifier NOT NULL,
    OrderId           uniqueidentifier NOT NULL,
    Provider          nvarchar(100)    NOT NULL,
    ProviderReference nvarchar(200)    NOT NULL,
    Amount            decimal(19, 4)   NOT NULL,
    CurrencyCode      char(3)          NOT NULL,
    Status            int              NOT NULL,  -- PaymentStatus
    CreatedAt         datetime2(3)     NOT NULL CONSTRAINT DF_Payments_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt         datetime2(3)     NOT NULL CONSTRAINT DF_Payments_UpdatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_Payments PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_Payments_Tenant_Id UNIQUE CLUSTERED (TenantId, Id),

    -- A payment provider reference must not be recorded twice: that is how a
    -- retried webhook turns into a double capture.
    CONSTRAINT UQ_Payments_Tenant_Provider_Reference UNIQUE (TenantId, Provider, ProviderReference),

    CONSTRAINT CK_Payments_Amount CHECK (Amount >= 0),
    CONSTRAINT FK_Payments_Orders FOREIGN KEY (TenantId, OrderId) REFERENCES dbo.Orders (TenantId, Id)
);
GO

CREATE INDEX IX_Payments_Tenant_Order
    ON dbo.Payments (TenantId, OrderId)
    INCLUDE (Provider, ProviderReference, Amount, CurrencyCode, Status, CreatedAt);
GO

CREATE TABLE dbo.OrderStatusHistory
(
    Id              uniqueidentifier NOT NULL,
    TenantId        uniqueidentifier NOT NULL,
    OrderId         uniqueidentifier NOT NULL,
    FromStatus      int              NULL,      -- NULL on the row written at creation
    ToStatus        int              NOT NULL,
    Reason          nvarchar(500)    NULL,
    ChangedByUserId uniqueidentifier NULL,
    OccurredAt      datetime2(3)     NOT NULL,

    CONSTRAINT PK_OrderStatusHistory PRIMARY KEY NONCLUSTERED (Id),
    CONSTRAINT UQ_OrderStatusHistory_Tenant_Id UNIQUE CLUSTERED (TenantId, Id),
    CONSTRAINT FK_OrderStatusHistory_Orders FOREIGN KEY (TenantId, OrderId) REFERENCES dbo.Orders (TenantId, Id)
);
GO

CREATE INDEX IX_OrderStatusHistory_Tenant_Order_OccurredAt
    ON dbo.OrderStatusHistory (TenantId, OrderId, OccurredAt)
    INCLUDE (FromStatus, ToStatus, Reason);
GO

/* ------------------------------------------------------------------ */
/* Order numbering                                                    */
/* ------------------------------------------------------------------ */

/*
    One counter row per shop, incremented inside the order transaction. The
    UPDATE holds an exclusive lock on that row until commit, so two concurrent
    checkouts in the same shop cannot be handed the same number.

    A rolled-back order leaves a gap in the sequence. That is intentional:
    gaps are cosmetic, duplicate order numbers are not.
*/
CREATE TABLE dbo.ShopOrderSequences
(
    TenantId   uniqueidentifier NOT NULL,
    ShopId     uniqueidentifier NOT NULL,
    LastNumber bigint           NOT NULL CONSTRAINT DF_ShopOrderSequences_LastNumber DEFAULT 0,

    CONSTRAINT PK_ShopOrderSequences PRIMARY KEY CLUSTERED (TenantId, ShopId),
    CONSTRAINT FK_ShopOrderSequences_Shops FOREIGN KEY (TenantId, ShopId) REFERENCES dbo.Shops (TenantId, Id)
);
GO

/* ------------------------------------------------------------------ */
/* Read replica notes                                                 */
/* ------------------------------------------------------------------ */

/*
    ReadConnection is expected to resolve to a readable secondary, for example
    an Always On availability group listener with ApplicationIntent=ReadOnly.

    Two consequences the application already accounts for:

      * Replication is asynchronous, so a row committed on the primary may not
        be visible on the secondary for some time. Reads that cannot tolerate
        that pass ReadConsistency.Strong and are routed to WriteConnection.

      * A readable secondary is read-only. Any attempt to write through
        ReadConnection fails at the server. CqrsGuardedDbConnectionFactory
        stops that far earlier, at the call site, with a message that explains
        the rule.
*/
