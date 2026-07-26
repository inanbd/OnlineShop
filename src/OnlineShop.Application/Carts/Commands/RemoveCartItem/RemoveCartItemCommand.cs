using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Domain.Ordering;

namespace OnlineShop.Application.Carts.Commands.RemoveCartItem;

public sealed record RemoveCartItemCommand(
    Guid ShopId,
    Guid CustomerId,
    Guid ProductId)
    : ICommand;

public sealed class RemoveCartItemCommandHandler : ICommandHandler<RemoveCartItemCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICustomerRepository _customerRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeProvider _clock;

    public RemoveCartItemCommandHandler(
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

    public Task Handle(RemoveCartItemCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var utcNow = _clock.UtcNow;

        return _unitOfWork.ExecuteAsync(
            async (transaction, ct) =>
            {
                var cart = await _customerRepository
                    .GetOpenCartAsync(tenantId, request.ShopId, request.CustomerId, transaction, ct)
                    .ConfigureAwait(false)
                    ?? throw new NotFoundException(nameof(Cart), request.CustomerId);

                // Removing something that is not there is a no-op rather than an
                // error: the shopper's intent is already satisfied.
                if (cart.Items.All(item => item.ProductId != request.ProductId))
                {
                    return;
                }

                cart.RemoveItem(request.ProductId, utcNow);

                await _customerRepository
                    .RemoveCartItemAsync(tenantId, cart.Id, request.ProductId, transaction, ct)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }
}
