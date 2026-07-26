using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Domain.Common;
using OnlineShop.Domain.Shops;

namespace OnlineShop.Application.Shops.Commands.CreateShop;

public sealed record CreateShopCommand(
    string Name,
    string Slug,
    string CurrencyCode,
    Guid OwnerUserId,
    string OwnerEmail)
    : ICommand<Guid>;

/// <summary>
/// Creates a shop and its owner membership in one transaction on the write
/// connection, so a shop can never exist without someone able to administer it.
/// </summary>
public sealed class CreateShopCommandHandler : ICommandHandler<CreateShopCommand, Guid>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IShopRepository _shopRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeProvider _clock;

    public CreateShopCommandHandler(
        IUnitOfWork unitOfWork,
        IShopRepository shopRepository,
        ITenantContext tenantContext,
        IDateTimeProvider clock)
    {
        _unitOfWork = unitOfWork;
        _shopRepository = shopRepository;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public Task<Guid> Handle(CreateShopCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var utcNow = _clock.UtcNow;

        return _unitOfWork.ExecuteAsync(
            async (transaction, ct) =>
            {
                var slugTaken = await _shopRepository
                    .SlugExistsAsync(tenantId, request.Slug, transaction, ct)
                    .ConfigureAwait(false);

                if (slugTaken)
                {
                    throw new DomainException($"Slug '{request.Slug}' is already used by another shop in this tenant.");
                }

                var shop = Shop.Create(
                    tenantId: tenantId,
                    name: request.Name,
                    slug: request.Slug,
                    currencyCode: request.CurrencyCode,
                    utcNow: utcNow);

                await _shopRepository.InsertAsync(shop, transaction, ct).ConfigureAwait(false);

                var owner = ShopMember.Invite(
                    tenantId: tenantId,
                    shopId: shop.Id,
                    email: request.OwnerEmail,
                    role: ShopMemberRole.Owner,
                    invitedByUserId: request.OwnerUserId,
                    utcNow: utcNow);

                // The creator is the owner, so the membership starts active
                // rather than waiting on an invitation e-mail.
                owner.Accept(request.OwnerUserId, utcNow);

                await _shopRepository.InsertMemberAsync(owner, transaction, ct).ConfigureAwait(false);

                return shop.Id;
            },
            cancellationToken);
    }
}
