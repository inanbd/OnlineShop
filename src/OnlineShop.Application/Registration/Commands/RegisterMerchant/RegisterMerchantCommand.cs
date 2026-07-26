using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Domain.Common;
using OnlineShop.Domain.Shops;
using OnlineShop.Domain.Tenants;

namespace OnlineShop.Application.Registration.Commands.RegisterMerchant;

public sealed record RegisterMerchantCommand(
    string BusinessName,
    string TenantSlug,
    string ShopName,
    string ShopSlug,
    string CurrencyCode,
    string OwnerEmail,
    Guid OwnerUserId)
    : ICommand<RegisterMerchantResult>;

public sealed record RegisterMerchantResult(Guid TenantId, Guid ShopId);

/// <summary>
/// Provisions a new tenant with its first shop and owner membership.
/// </summary>
/// <remarks>
/// <para>
/// The only command that does not take its tenant from
/// <see cref="ITenantContext"/> — it creates one. Everything else in the
/// application reads the ambient tenant; this is where that tenant comes from.
/// </para>
/// <para>
/// Tenant, shop and owner membership are written under one transaction, so a
/// half-provisioned merchant with a tenant but no shop cannot exist.
/// </para>
/// </remarks>
public sealed class RegisterMerchantCommandHandler
    : ICommandHandler<RegisterMerchantCommand, RegisterMerchantResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantRepository _tenantRepository;
    private readonly IShopRepository _shopRepository;
    private readonly IDateTimeProvider _clock;

    public RegisterMerchantCommandHandler(
        IUnitOfWork unitOfWork,
        ITenantRepository tenantRepository,
        IShopRepository shopRepository,
        IDateTimeProvider clock)
    {
        _unitOfWork = unitOfWork;
        _tenantRepository = tenantRepository;
        _shopRepository = shopRepository;
        _clock = clock;
    }

    public Task<RegisterMerchantResult> Handle(
        RegisterMerchantCommand request,
        CancellationToken cancellationToken)
    {
        var utcNow = _clock.UtcNow;

        return _unitOfWork.ExecuteAsync(
            async (transaction, ct) =>
            {
                var slugTaken = await _tenantRepository
                    .SlugExistsAsync(request.TenantSlug, transaction, ct)
                    .ConfigureAwait(false);

                if (slugTaken)
                {
                    throw new DomainException($"The address '{request.TenantSlug}' is already taken.");
                }

                var tenant = Tenant.Create(request.BusinessName, request.TenantSlug, utcNow);
                await _tenantRepository.InsertAsync(tenant, transaction, ct).ConfigureAwait(false);

                // From here on every statement is scoped to the tenant just
                // created, exactly as it would be for an established one.
                var shopSlugTaken = await _shopRepository
                    .SlugExistsAsync(tenant.Id, request.ShopSlug, transaction, ct)
                    .ConfigureAwait(false);

                if (shopSlugTaken)
                {
                    throw new DomainException($"The shop address '{request.ShopSlug}' is already taken.");
                }

                var shop = Shop.Create(
                    tenantId: tenant.Id,
                    name: request.ShopName,
                    slug: request.ShopSlug,
                    currencyCode: request.CurrencyCode,
                    utcNow: utcNow);

                await _shopRepository.InsertAsync(shop, transaction, ct).ConfigureAwait(false);

                var owner = ShopMember.Invite(
                    tenantId: tenant.Id,
                    shopId: shop.Id,
                    email: request.OwnerEmail,
                    role: ShopMemberRole.Owner,
                    invitedByUserId: request.OwnerUserId,
                    utcNow: utcNow);

                owner.Accept(request.OwnerUserId, utcNow);
                await _shopRepository.InsertMemberAsync(owner, transaction, ct).ConfigureAwait(false);

                return new RegisterMerchantResult(tenant.Id, shop.Id);
            },
            cancellationToken);
    }
}
