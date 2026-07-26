using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Domain.Catalog;
using OnlineShop.Domain.Common;
using OnlineShop.Domain.Ordering;

namespace OnlineShop.Application.Carts.Commands.AddToCart;

public sealed record AddToCartCommand(
    Guid ShopId,
    Guid CustomerId,
    Guid ProductId,
    int Quantity)
    : ICommand<Guid>;

/// <summary>
/// Adds a product to the shopper's open basket, creating one if needed.
/// </summary>
/// <remarks>
/// <para>
/// The cart lookup, the product read and the line write all share one
/// transaction on the write connection. That matters more than it looks: the
/// new quantity is computed from what the cart currently holds, so reading it
/// on a different connection would let two concurrent adds both read the same
/// starting quantity and one of them would be lost.
/// </para>
/// <para>
/// The price is taken from the catalog read inside this transaction rather than
/// from anything the browser submitted.
/// </para>
/// </remarks>
public sealed class AddToCartCommandHandler : ICommandHandler<AddToCartCommand, Guid>
{
    private const int MaxLineQuantity = 999;

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICustomerRepository _customerRepository;
    private readonly IProductRepository _productRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeProvider _clock;

    public AddToCartCommandHandler(
        IUnitOfWork unitOfWork,
        ICustomerRepository customerRepository,
        IProductRepository productRepository,
        ITenantContext tenantContext,
        IDateTimeProvider clock)
    {
        _unitOfWork = unitOfWork;
        _customerRepository = customerRepository;
        _productRepository = productRepository;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public Task<Guid> Handle(AddToCartCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var utcNow = _clock.UtcNow;

        return _unitOfWork.ExecuteAsync(
            async (transaction, ct) =>
            {
                var product = await _productRepository
                    .GetByIdAsync(tenantId, request.ProductId, transaction, ct)
                    .ConfigureAwait(false)
                    ?? throw new NotFoundException(nameof(Product), request.ProductId);

                if (product.ShopId != request.ShopId)
                {
                    throw new DomainException("That product does not belong to this shop.");
                }

                if (product.IsDeleted || product.Status != ProductStatus.Active)
                {
                    throw new DomainException($"'{product.Name}' is not available to buy.");
                }

                var cart = await _customerRepository
                    .GetOpenCartAsync(tenantId, request.ShopId, request.CustomerId, transaction, ct)
                    .ConfigureAwait(false);

                if (cart is null)
                {
                    cart = Cart.Create(tenantId, request.ShopId, request.CustomerId, utcNow);
                    await _customerRepository.InsertCartAsync(cart, transaction, ct).ConfigureAwait(false);
                }

                // The domain merges the line if the product is already there,
                // and hands back the new absolute quantity.
                var line = cart.AddItem(request.ProductId, request.Quantity, product.Price, utcNow);

                if (line.Quantity > MaxLineQuantity)
                {
                    throw new DomainException($"A basket line cannot hold more than {MaxLineQuantity} units.");
                }

                await _customerRepository
                    .UpsertCartItemAsync(
                        tenantId, cart.Id, request.ProductId, line.Quantity, product.Price, transaction, ct)
                    .ConfigureAwait(false);

                return cart.Id;
            },
            cancellationToken);
    }
}
