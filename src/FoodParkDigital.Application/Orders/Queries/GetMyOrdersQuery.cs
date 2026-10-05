using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Queries;

/// <summary>
/// The customer account's own orders, in every business where they were placed with this account (or claimed by
/// confirming the phone). Only the account's orders: the business filter is replaced by the account filter.
/// </summary>
[RequiresAuthentication]
public record GetMyOrdersQuery : IRequest<PaginatedList<MyOrderDto>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
}

public class GetMyOrdersQueryValidator : AbstractValidator<GetMyOrdersQuery>
{
    public GetMyOrdersQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
    }
}

public class MyOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public string BusinessName { get; set; } = null!;
    /// <summary>For "Pedir otra vez" in the business's online menu (/m/{slug}).</summary>
    public string BusinessSlug { get; set; } = null!;
    public bool CanOrderAgain { get; set; }
    public OrderStatus Status { get; set; }
    public OrderType OrderType { get; set; }
    public decimal Total { get; set; }
    public decimal PaidAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = [];
}

public class MyOrderItemDto
{
    public string ProductName { get; set; } = null!;
    public decimal Quantity { get; set; }
}

public class GetMyOrdersQueryHandler : IRequestHandler<GetMyOrdersQuery, PaginatedList<MyOrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public GetMyOrdersQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PaginatedList<MyOrderDto>> Handle(GetMyOrdersQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return new PaginatedList<MyOrderDto>([], 0, request.PageNumber, request.PageSize);

        var query =
            from order in _context.Orders.IgnoreQueryFilters().AsNoTracking()
            join tenant in _context.Tenants.AsNoTracking() on order.TenantId equals tenant.Id
            where order.CustomerUserId == userId
            orderby order.CreatedAt descending
            select new MyOrderDto
            {
                Id = order.Id,
                OrderNumber = order.OrderNumber,
                BusinessName = tenant.Name,
                BusinessSlug = tenant.Slug,
                CanOrderAgain = _context.TenantSettings.Any(s => s.TenantId == order.TenantId && s.OnlineMenuMode == OnlineMenuMode.Orders),
                Status = order.Status,
                OrderType = order.OrderType,
                Total = order.Total,
                PaidAmount = order.PaidAmount,
                CreatedAt = order.CreatedAt,
                Items = order.Items.Select(i => new MyOrderItemDto { ProductName = i.ProductName, Quantity = i.Quantity }).ToList()
            };

        return await PaginatedList<MyOrderDto>.CreateAsync(query, request.PageNumber, request.PageSize, cancellationToken);
    }
}
