using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Domain.Customers;

namespace OnlineShop.Application.Registration.Commands.RegisterShopper;

public sealed record RegisterShopperCommand(
    Guid ShopId,
    string Email,
    string FullName)
    : ICommand<Guid>;

/// <summary>
/// Creates the <see cref="Customer"/> row behind a shopper's account.
/// </summary>
/// <remarks>
/// The tenant comes from the storefront the shopper is registering on, resolved
/// from the shop slug in the URL before this command runs. The sign-in account
/// itself is created separately by Identity; this is the shop-side record that
/// carts and orders hang off.
/// </remarks>
public sealed class RegisterShopperCommandHandler : ICommandHandler<RegisterShopperCommand, Guid>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICustomerRepository _customerRepository;
    private readonly IShopRepository _shopRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeProvider _clock;

    public RegisterShopperCommandHandler(
        IUnitOfWork unitOfWork,
        ICustomerRepository customerRepository,
        IShopRepository shopRepository,
        ITenantContext tenantContext,
        IDateTimeProvider clock)
    {
        _unitOfWork = unitOfWork;
        _customerRepository = customerRepository;
        _shopRepository = shopRepository;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public Task<Guid> Handle(RegisterShopperCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var utcNow = _clock.UtcNow;

        return _unitOfWork.ExecuteAsync(
            async (transaction, ct) =>
            {
                _ = await _shopRepository
                    .GetByIdAsync(tenantId, request.ShopId, transaction, ct)
                    .ConfigureAwait(false)
                    ?? throw new NotFoundException(nameof(Domain.Shops.Shop), request.ShopId);

                var customer = Customer.Create(
                    tenantId: tenantId,
                    shopId: request.ShopId,
                    email: request.Email,
                    fullName: request.FullName,
                    utcNow: utcNow);

                await _customerRepository.InsertAsync(customer, transaction, ct).ConfigureAwait(false);

                return customer.Id;
            },
            cancellationToken);
    }
}
