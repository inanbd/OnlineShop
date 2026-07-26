using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Domain.Common;
using OnlineShop.Domain.Shops;

namespace OnlineShop.Application.Shops.Commands.InviteShopMember;

public sealed record InviteShopMemberCommand(
    Guid ShopId,
    string Email,
    ShopMemberRole Role,
    Guid InvitedByUserId)
    : ICommand<Guid>;

/// <summary>
/// Invites a user to a shop, or re-invites someone whose access was revoked.
/// </summary>
public sealed class InviteShopMemberCommandHandler : ICommandHandler<InviteShopMemberCommand, Guid>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IShopRepository _shopRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeProvider _clock;

    public InviteShopMemberCommandHandler(
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

    public Task<Guid> Handle(InviteShopMemberCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var utcNow = _clock.UtcNow;

        return _unitOfWork.ExecuteAsync(
            async (transaction, ct) =>
            {
                _ = await _shopRepository
                    .GetByIdAsync(tenantId, request.ShopId, transaction, ct)
                    .ConfigureAwait(false)
                    ?? throw new NotFoundException(nameof(Shop), request.ShopId);

                var existing = await _shopRepository
                    .GetMemberByEmailAsync(tenantId, request.ShopId, request.Email, transaction, ct)
                    .ConfigureAwait(false);

                if (existing is not null)
                {
                    if (existing.Status != ShopMemberStatus.Revoked)
                    {
                        throw new DomainException(
                            $"'{request.Email}' already has {existing.Status} access to this shop.");
                    }

                    // Reinstating revoked access reuses the existing row so the
                    // membership history stays intact. The member goes back to
                    // Invited and has to accept again.
                    existing.Reinstate(request.Role, utcNow);
                    await _shopRepository.UpdateMemberAsync(existing, transaction, ct).ConfigureAwait(false);
                    return existing.Id;
                }

                var member = ShopMember.Invite(
                    tenantId: tenantId,
                    shopId: request.ShopId,
                    email: request.Email,
                    role: request.Role,
                    invitedByUserId: request.InvitedByUserId,
                    utcNow: utcNow);

                await _shopRepository.InsertMemberAsync(member, transaction, ct).ConfigureAwait(false);

                return member.Id;
            },
            cancellationToken);
    }
}
