using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Repositories;
using OnlineShop.Domain.Catalog;

namespace OnlineShop.Application.Products.Commands.DeleteProduct;

public sealed record DeleteProductCommand(Guid ProductId) : ICommand;

/// <summary>
/// Soft-deletes a product on the write connection.
/// </summary>
public sealed class DeleteProductCommandHandler : ICommandHandler<DeleteProductCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductRepository _productRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeProvider _clock;

    public DeleteProductCommandHandler(
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

    public Task Handle(DeleteProductCommand request, CancellationToken cancellationToken)
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

                if (product.IsDeleted)
                {
                    return;
                }

                // Runs the domain rule (a deleted product cannot be deleted twice)
                // before the row is touched.
                product.Delete(utcNow);

                await _productRepository
                    .DeleteAsync(tenantId, product.Id, transaction, ct)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }
}
