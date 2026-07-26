using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Domain.Ordering;

namespace OnlineShop.Application.Orders.Commands.CancelOrder;

public sealed record CancelOrderCommand(
    Guid OrderId,
    string Reason,
    Guid? CancelledByUserId = null)
    : ICommand;

/// <summary>
/// Cancels an order and returns its reserved stock, in one transaction on the
/// write connection.
/// </summary>
/// <remarks>
/// Releasing the reservation and recording the cancellation have to be atomic.
/// Splitting them would let a crash between the two leave stock reserved
/// against an order nobody is going to pay for.
/// </remarks>
public sealed class CancelOrderCommandHandler : ICommandHandler<CancelOrderCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOrderRepository _orderRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeProvider _clock;

    public CancelOrderCommandHandler(
        IUnitOfWork unitOfWork,
        IOrderRepository orderRepository,
        IInventoryRepository inventoryRepository,
        ITenantContext tenantContext,
        IDateTimeProvider clock)
    {
        _unitOfWork = unitOfWork;
        _orderRepository = orderRepository;
        _inventoryRepository = inventoryRepository;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public Task Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var utcNow = _clock.UtcNow;

        return _unitOfWork.ExecuteAsync(
            async (transaction, ct) =>
            {
                var order = await _orderRepository
                    .GetByIdAsync(tenantId, request.OrderId, transaction, ct)
                    .ConfigureAwait(false)
                    ?? throw new NotFoundException(nameof(Order), request.OrderId);

                var previousStatus = order.Status;

                // Throws if the order is already cancelled or has been fulfilled.
                order.Cancel(request.Reason, utcNow);

                await _orderRepository.UpdateAsync(order, transaction, ct).ConfigureAwait(false);

                foreach (var item in order.Items)
                {
                    await _inventoryRepository
                        .ReleaseReservationAsync(tenantId, item.ProductId, item.Quantity, transaction, ct)
                        .ConfigureAwait(false);
                }

                var history = OrderStatusHistoryEntry.Record(
                    tenantId: tenantId,
                    orderId: order.Id,
                    fromStatus: previousStatus,
                    toStatus: order.Status,
                    reason: request.Reason,
                    changedByUserId: request.CancelledByUserId,
                    utcNow: utcNow);

                await _orderRepository.InsertStatusHistoryAsync(history, transaction, ct).ConfigureAwait(false);
            },
            cancellationToken);
    }
}
