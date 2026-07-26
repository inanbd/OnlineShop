using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Contracts.Products;

namespace OnlineShop.Application.Products.Queries.GetProductById;

/// <summary>
/// Single product projection, for the product detail page and for the redirect
/// that follows <c>CreateProductCommand</c>.
/// </summary>
/// <remarks>
/// <para>
/// Normal catalog traffic leaves <see cref="Consistency"/> at
/// <see cref="ReadConsistency.Eventual"/> and is served from the replica.
/// </para>
/// <para>
/// The admin UI redirects to this product straight after creating or editing
/// it. At that moment replication may not have caught up, and showing "not
/// found" for a product the user just saved is the read-after-write problem
/// this codebase is built to handle. Those callers pass
/// <see cref="ReadConsistency.Strong"/>, which routes the read to
/// <c>WriteConnection</c>. The allowance below is what makes that legal.
/// </para>
/// </remarks>
[StrongConsistencyAllowed(
    "The catalog admin redirects to the product detail view immediately after a create or update. " +
    "Serving that one read from the replica can 404 on a row the user just saved.")]
public sealed record GetProductByIdQuery(
    Guid ProductId,
    ReadConsistency Consistency = ReadConsistency.Eventual)
    : IQuery<ProductDto>, ISupportsReadConsistency;

/// <summary>
/// Query -> MediatR query handler -> read connection (or, when the caller asks
/// for strong consistency, the write connection) -> Dapper -> SQL Server.
/// </summary>
public sealed class GetProductByIdQueryHandler
    : IQueryHandler<GetProductByIdQuery, ProductDto>
{
    private readonly IProductQueries _productQueries;
    private readonly ITenantContext _tenantContext;

    public GetProductByIdQueryHandler(IProductQueries productQueries, ITenantContext tenantContext)
    {
        _productQueries = productQueries;
        _tenantContext = tenantContext;
    }

    public async Task<ProductDto> Handle(
        GetProductByIdQuery request,
        CancellationToken cancellationToken)
    {
        var product = await _productQueries
            .GetByIdAsync(_tenantContext.TenantId, request.ProductId, request.Consistency, cancellationToken)
            .ConfigureAwait(false);

        return product ?? throw new NotFoundException(nameof(Domain.Catalog.Product), request.ProductId);
    }
}
