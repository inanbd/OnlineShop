using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Domain.Common;
using OnlineShop.Domain.Ordering;

namespace OnlineShop.Application.Carts.Commands.UpdateCartItem;

public sealed record UpdateCartItemCommand(
    Guid ShopId,
    Guid CustomerId,
    Guid ProductId,
    int Quantity)
    : ICommand;

/// <summary>
/// Sets a basket line to an absolute quantity. Zero removes the line.
/// </summary>
/// <remarks>
/// Absolute rather than a delta, so the shopper's browser refreshing or
/// double-submitting cannot change the basket twice.
/// </remarks>
public sealed class UpdateCartItemCommandHandler : ICommandHandler<UpdateCartItemCommand>
{
    private const int MaxLineQuantity = 999;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICustomerRepository _customerRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeProvider _clock;

    public UpdateCartItemCommandHandler(
        IUnitOfWork unitOfWork,
        ICustomerRepository customerRepository,
        ITenantContext tenantContext,
        IDateTimeProvider clock)
    {
        _unitOfWork = unitOfWork;
        _customerRepository = customerRepository;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public Task Handle(UpdateCartItemCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var utcNow = _clock.UtcNow;

        if (request.Quantity is < 0 or > MaxLineQuantity)
        {
            throw new DomainException($"Quantity must be between 0 and {MaxLineQuantity}.");
        }

        return _unitOfWork.ExecuteAsync(
            async (transaction, ct) =>
            {
                var cart = await _customerRepository
                    .GetOpenCartAsync(tenantId, request.ShopId, request.CustomerId, transaction, ct)
                    .ConfigureAwait(false)
                    ?? throw new NotFoundException(nameof(Cart), request.CustomerId);

                var existing = cart.Items.SingleOrDefault(item => item.ProductId == request.ProductId)
                    ?? throw new DomainException("That product is not in your basket.");

                // Runs the cart's own rules, including that a closed cart cannot
                // be edited, before anything is written.
                cart.UpdateItemQuantity(request.ProductId, request.Quantity, utcNow);

                if (request.Quantity == 0)
                {
                    await _customerRepository
                        .RemoveCartItemAsync(tenantId, cart.Id, request.ProductId, transaction, ct)
                        .ConfigureAwait(false);
                    return;
                }

                await _customerRepository
                    .UpsertCartItemAsync(
                        tenantId, cart.Id, request.ProductId, request.Quantity, existing.UnitPrice, transaction, ct)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }
}
