using AutoMapper;
using AutoMapper.QueryableExtensions;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Catalog.Queries;

public record GetProductsQuery : IRequest<PaginatedList<ProductDto>>
{
    public Guid? CategoryId { get; init; }
    public bool? IsAvailable { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, PaginatedList<ProductDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;

    public GetProductsQueryHandler(IApplicationDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<PaginatedList<ProductDto>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Products.AsNoTracking().AsQueryable();

        if (request.CategoryId.HasValue)
            query = query.Where(p => p.CategoryId == request.CategoryId.Value);

        if (request.IsAvailable.HasValue)
            query = query.Where(p => p.IsAvailable == request.IsAvailable.Value);

        var projectedQuery = query
            .OrderBy(p => p.Name)
            .ProjectTo<ProductDto>(_mapper.ConfigurationProvider);

        return await PaginatedList<ProductDto>.CreateAsync(
            projectedQuery, request.PageNumber, request.PageSize, cancellationToken);
    }
}
