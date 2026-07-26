using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Messaging;
using OnlineShop.Application.Abstractions.Persistence.Queries;
using OnlineShop.Application.Contracts;
using OnlineShop.Application.Contracts.Products;

namespace OnlineShop.Application.Products.Queries.GetProducts;

/// <summary>
/// Product listing for the storefront and the catalog admin grid.
/// </summary>
/// <remarks>
/// Catalog browsing is the archetypal replica workload: high volume, and a few
/// seconds of staleness costs nothing. The query offers no consistency knob, so
/// it can only ever be served from <c>ReadConnection</c>.
/// </remarks>
public sealed record GetProductsQuery(ProductFilter Filter)
    : IQuery<PagedResult<ProductListItemDto>>;

/// <summary>
/// Query -> MediatR query handler -> ReadConnection -> Dapper -> read replica.
/// </summary>
public sealed class GetProductsQueryHandler
    : IQueryHandler<GetProductsQuery, PagedResult<ProductListItemDto>>
{
    private readonly IProductQueries _productQueries;
    private readonly ITenantContext _tenantContext;

    public GetProductsQueryHandler(IProductQueries productQueries, ITenantContext tenantContext)
    {
        _productQueries = productQueries;
        _tenantContext = tenantContext;
    }

    public async Task<PagedResult<ProductListItemDto>> Handle(
        GetProductsQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var filter = request.Filter;

        var totalCount = await _productQueries
            .CountAsync(tenantId, filter, cancellationToken)
            .ConfigureAwait(false);

        if (totalCount == 0)
        {
            return PagedResult<ProductListItemDto>.Empty(filter.Page, filter.PageSize);
        }

        var items = await _productQueries
            .GetAsync(tenantId, filter, cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<ProductListItemDto>(items, totalCount, filter.Page, filter.PageSize);
    }
}
