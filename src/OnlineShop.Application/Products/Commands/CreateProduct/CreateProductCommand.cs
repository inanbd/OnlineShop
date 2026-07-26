using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Domain.Catalog;
using OnlineShop.Domain.Common;
using OnlineShop.Domain.Inventory;

namespace OnlineShop.Application.Products.Commands.CreateProduct;

public sealed record CreateProductCommand(
    Guid ShopId,
    string Name,
    string Sku,
    string? Description,
    decimal Price,
    int InitialQuantityOnHand = 0,
    int ReorderThreshold = 0,
    bool PublishImmediately = false)
    : ICommand<Guid>;

/// <summary>
/// Command -> MediatR command handler -> WriteConnection -> Dapper -> primary.
/// </summary>
/// <remarks>
/// The product row and its opening inventory row are written under one
/// transaction, so the catalog can never contain a product with no stock
/// record. Both repositories are handed the same <c>IDbTransaction</c>.
/// </remarks>
public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductRepository _productRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IShopRepository _shopRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeProvider _clock;

    public CreateProductCommandHandler(
        IUnitOfWork unitOfWork,
        IProductRepository productRepository,
        IInventoryRepository inventoryRepository,
        IShopRepository shopRepository,
        ITenantContext tenantContext,
        IDateTimeProvider clock)
    {
        _unitOfWork = unitOfWork;
        _productRepository = productRepository;
        _inventoryRepository = inventoryRepository;
        _shopRepository = shopRepository;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public Task<Guid> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var utcNow = _clock.UtcNow;

        return _unitOfWork.ExecuteAsync(
            async (transaction, ct) =>
            {
                var shop = await _shopRepository
                    .GetByIdAsync(tenantId, request.ShopId, transaction, ct)
                    .ConfigureAwait(false)
                    ?? throw new NotFoundException(nameof(Domain.Shops.Shop), request.ShopId);

                var skuTaken = await _productRepository
                    .SkuExistsAsync(tenantId, request.ShopId, request.Sku, null, transaction, ct)
                    .ConfigureAwait(false);

                if (skuTaken)
                {
                    throw new DomainException($"SKU '{request.Sku}' is already used by another product in this shop.");
                }

                var product = Product.Create(
                    tenantId: tenantId,
                    shopId: request.ShopId,
                    name: request.Name,
                    sku: request.Sku,
                    description: request.Description,
                    price: request.Price,
                    currencyCode: shop.CurrencyCode,
                    utcNow: utcNow);

                if (request.PublishImmediately)
                {
                    product.Publish(utcNow);
                }

                await _productRepository.InsertAsync(product, transaction, ct).ConfigureAwait(false);

                var inventory = InventoryItem.Create(
                    tenantId: tenantId,
                    shopId: request.ShopId,
                    productId: product.Id,
                    quantityOnHand: request.InitialQuantityOnHand,
                    reorderThreshold: request.ReorderThreshold,
                    utcNow: utcNow);

                await _inventoryRepository.InsertAsync(inventory, transaction, ct).ConfigureAwait(false);

                return product.Id;
            },
            cancellationToken);
    }
}
