using System.Data;
using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Domain.Common;
using OnlineShop.Domain.Ordering;

namespace OnlineShop.Application.Orders.Commands.PlaceOrder;

public sealed record PlaceOrderCommand(
    Guid ShopId,
    Guid CustomerId,
    Guid CartId,
    string PaymentProvider,
    string PaymentReference)
    : ICommand<PlaceOrderResult>;

public sealed record PlaceOrderResult(Guid OrderId, string OrderNumber, decimal GrandTotal);

/// <summary>
/// Turns a cart into an order.
/// </summary>
/// <remarks>
/// <para>
/// The whole flow runs on <c>WriteConnection</c> under a single transaction:
/// </para>
/// <code>
/// BEGIN TRANSACTION
///   Create Order
///   Create Order Items
///   Reserve / Update Inventory
///   Create Payment Record
///   Update Cart
///   Create Order Status History
/// COMMIT                            (ROLLBACK if any step fails)
/// </code>
/// <para>
/// Every repository taking part receives the same <see cref="IDbTransaction"/>,
/// which is what makes the whole sequence atomic. If stock runs out at step
/// three, the order and its items disappear with the rollback — there is no
/// compensating cleanup to get wrong.
/// </para>
/// <para>
/// The isolation level is raised to <see cref="IsolationLevel.RepeatableRead"/>
/// so that concurrent checkouts contending for the last unit of stock serialise
/// on the inventory rows.
/// </para>
/// </remarks>
public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, PlaceOrderResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOrderRepository _orderRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IShopRepository _shopRepository;
    private readonly IProductRepository _productRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeProvider _clock;

    public PlaceOrderCommandHandler(
        IUnitOfWork unitOfWork,
        IOrderRepository orderRepository,
        IInventoryRepository inventoryRepository,
        ICustomerRepository customerRepository,
        IShopRepository shopRepository,
        IProductRepository productRepository,
        ITenantContext tenantContext,
        IDateTimeProvider clock)
    {
        _unitOfWork = unitOfWork;
        _orderRepository = orderRepository;
        _inventoryRepository = inventoryRepository;
        _customerRepository = customerRepository;
        _shopRepository = shopRepository;
        _productRepository = productRepository;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public Task<PlaceOrderResult> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var utcNow = _clock.UtcNow;

        return _unitOfWork.ExecuteAsync(
            IsolationLevel.RepeatableRead,
            async (transaction, ct) =>
            {
                var shop = await _shopRepository
                    .GetByIdAsync(tenantId, request.ShopId, transaction, ct)
                    .ConfigureAwait(false)
                    ?? throw new NotFoundException(nameof(Domain.Shops.Shop), request.ShopId);

                var cart = await _customerRepository
                    .GetCartByIdAsync(tenantId, request.CartId, transaction, ct)
                    .ConfigureAwait(false)
                    ?? throw new NotFoundException(nameof(Cart), request.CartId);

                if (cart.ShopId != request.ShopId || cart.CustomerId != request.CustomerId)
                {
                    throw new DomainException("The cart does not belong to the given shop and customer.");
                }

                if (cart.Status != CartStatus.Open)
                {
                    throw new DomainException($"Cart '{cart.Id}' has already been {cart.Status}.");
                }

                if (cart.Items.Count == 0)
                {
                    throw new DomainException($"Cart '{cart.Id}' is empty and cannot be checked out.");
                }

                var lines = await BuildOrderLinesAsync(tenantId, cart, transaction, ct).ConfigureAwait(false);

                // 1 + 2. Order header and line items.
                var orderNumber = await _orderRepository
                    .NextOrderNumberAsync(tenantId, request.ShopId, transaction, ct)
                    .ConfigureAwait(false);

                var order = Order.Place(
                    tenantId: tenantId,
                    shopId: request.ShopId,
                    customerId: request.CustomerId,
                    orderNumber: orderNumber,
                    currencyCode: shop.CurrencyCode,
                    lines: lines,
                    utcNow: utcNow);

                await _orderRepository.InsertAsync(order, transaction, ct).ConfigureAwait(false);

                // 3. Reserve stock. The conditional UPDATE is the authoritative
                // oversell check; a failure here rolls the order back.
                foreach (var item in order.Items)
                {
                    var reserved = await _inventoryRepository
                        .TryReserveAsync(tenantId, item.ProductId, item.Quantity, transaction, ct)
                        .ConfigureAwait(false);

                    if (!reserved)
                    {
                        throw new DomainException(
                            $"'{item.ProductName}' does not have {item.Quantity} unit(s) available.");
                    }
                }

                // 4. Payment record.
                var payment = Payment.Record(
                    tenantId: tenantId,
                    orderId: order.Id,
                    provider: request.PaymentProvider,
                    providerReference: request.PaymentReference,
                    amount: order.GrandTotal,
                    currencyCode: order.CurrencyCode,
                    status: PaymentStatus.Authorized,
                    utcNow: utcNow);

                await _orderRepository.InsertPaymentAsync(payment, transaction, ct).ConfigureAwait(false);

                // 5. Close the cart.
                cart.MarkCheckedOut(utcNow);
                await _customerRepository
                    .MarkCartCheckedOutAsync(tenantId, cart.Id, transaction, ct)
                    .ConfigureAwait(false);

                // 6. Opening status history row.
                var history = OrderStatusHistoryEntry.Record(
                    tenantId: tenantId,
                    orderId: order.Id,
                    fromStatus: null,
                    toStatus: order.Status,
                    reason: "Order placed.",
                    changedByUserId: null,
                    utcNow: utcNow);

                await _orderRepository.InsertStatusHistoryAsync(history, transaction, ct).ConfigureAwait(false);

                return new PlaceOrderResult(order.Id, order.OrderNumber, order.GrandTotal);
            },
            cancellationToken);
    }

    /// <summary>
    /// Re-reads each product through the transaction so that name, SKU and price
    /// are taken from the primary at checkout time rather than trusted from the
    /// cart, which may have been filled from a stale replica read.
    /// </summary>
    private async Task<List<OrderLine>> BuildOrderLinesAsync(
        Guid tenantId,
        Cart cart,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var lines = new List<OrderLine>(cart.Items.Count);

        foreach (var cartItem in cart.Items)
        {
            var product = await _productRepository
                .GetByIdAsync(tenantId, cartItem.ProductId, transaction, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new NotFoundException(nameof(Domain.Catalog.Product), cartItem.ProductId);

            if (product.IsDeleted || product.Status != Domain.Catalog.ProductStatus.Active)
            {
                throw new DomainException($"'{product.Name}' is no longer available for purchase.");
            }

            lines.Add(new OrderLine(
                ProductId: product.Id,
                Sku: product.Sku,
                ProductName: product.Name,
                Quantity: cartItem.Quantity,
                UnitPrice: product.Price));
        }

        return lines;
    }
}
