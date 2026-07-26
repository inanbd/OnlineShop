using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Domain.Catalog;
using OnlineShop.Domain.Common;

namespace OnlineShop.Application.Products.Commands.UpdateProduct;

public sealed record UpdateProductCommand(
    Guid ProductId,
    string Name,
    string Sku,
    string? Description,
    decimal Price)
    : ICommand;

/// <summary>
/// Loads the aggregate through the transaction, applies the change in the
/// domain, and writes it back on the same connection.
/// </summary>
/// <remarks>
/// The load reads the primary because it goes through the transaction. That is
/// deliberate: a command must never base a write on a possibly stale replica
/// read.
/// </remarks>
public sealed class UpdateProductCommandHandler : ICommandHandler<UpdateProductCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductRepository _productRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeProvider _clock;

    public UpdateProductCommandHandler(
        IUnitOfWork unitOfWork,
        IProductRepository productRepository,
        ITenantContext tenantContext,
        IDateTimeProvider clock)
    {
        _unitOfWork = unitOfWork;
        _productRepository = productRepository;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public Task Handle(UpdateProductCommand request, CancellationToken cancellationToken)
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

                var skuTaken = await _productRepository
                    .SkuExistsAsync(tenantId, product.ShopId, request.Sku, product.Id, transaction, ct)
                    .ConfigureAwait(false);

                if (skuTaken)
                {
                    throw new DomainException($"SKU '{request.Sku}' is already used by another product in this shop.");
                }

                product.UpdateDetails(
                    name: request.Name,
                    sku: request.Sku,
                    description: request.Description,
                    price: request.Price,
                    utcNow: utcNow);

                await _productRepository.UpdateAsync(product, transaction, ct).ConfigureAwait(false);
            },
            cancellationToken);
    }
}
