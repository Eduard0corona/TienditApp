using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Orders.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Queries;

public record GetServiceBoardPiecesQuery : IRequest<PaginatedList<ServiceBoardPieceDto>>
{
    public string? Search { get; init; }
    public ServiceOrderPieceStatus? Status { get; init; }
    public bool IncludeCompleted { get; init; }
    public bool IncludeCancelled { get; init; }
    public bool OnlyMine { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetServiceBoardPiecesQueryHandler : IRequestHandler<GetServiceBoardPiecesQuery, PaginatedList<ServiceBoardPieceDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public GetServiceBoardPiecesQueryHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    public async Task<PaginatedList<ServiceBoardPieceDto>> Handle(GetServiceBoardPiecesQuery request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);

        if (!TenantCapabilityRules.CanCreateOrderType(tenantCapabilities, OrderType.Service))
            return new PaginatedList<ServiceBoardPieceDto>([], 0, request.PageNumber, request.PageSize);

        var query = _context.ServiceOrderPieces
            .AsNoTracking()
            .Where(p => p.IsTrackable)
            .Where(p => p.Order.OrderType == OrderType.Service)
            .AsQueryable();

        if (request.Status.HasValue)
            query = query.Where(p => p.Status == request.Status.Value);

        if (!request.IncludeCompleted)
            query = query.Where(p => p.Status != ServiceOrderPieceStatus.Completed);

        if (!request.IncludeCancelled)
            query = query.Where(p => p.Status != ServiceOrderPieceStatus.Cancelled);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            query = query.Where(p =>
                p.ItemName.Contains(search)
                || p.Order.OrderNumber.Contains(search)
                || p.Order.CustomerName.Contains(search)
                || (p.Order.CustomerPhone != null && p.Order.CustomerPhone.Contains(search)));
        }

        if (request.OnlyMine)
        {
            if (!Guid.TryParse(_currentUserService.UserId, out var currentUserId))
                return new PaginatedList<ServiceBoardPieceDto>([], 0, request.PageNumber, request.PageSize);

            query = query.Where(p => p.AssignedToUserId == currentUserId);
        }

        var projected = query
            .Select(p => new ServiceBoardPieceDto
            {
                PieceId = p.Id,
                OrderId = p.OrderId,
                OrderNumber = p.Order.OrderNumber,
                CustomerName = p.Order.CustomerName,
                CustomerPhone = p.Order.CustomerPhone,
                OrderCreatedAt = p.Order.CreatedAt,
                PromisedAt = p.Order.ServiceDetail != null ? p.Order.ServiceDetail.PromisedAt : null,
                Priority = p.Order.ServiceDetail != null ? p.Order.ServiceDetail.Priority : 0,
                ItemName = p.ItemName,
                RequestedService = p.RequestedService,
                InternalNotes = p.InternalNotes,
                NotifyCustomer = p.NotifyCustomer,
                Status = p.Status,
                AssignedToUserId = p.AssignedToUserId,
                AssignedToName = p.AssignedToName,
                AssignedAt = p.AssignedAt
            })
            .OrderBy(p => p.Status == ServiceOrderPieceStatus.Accepted ? 0 :
                p.Status == ServiceOrderPieceStatus.InProgress ? 1 :
                p.Status == ServiceOrderPieceStatus.Completed ? 2 : 3)
            .ThenByDescending(p => p.Priority)
            .ThenBy(p => p.PromisedAt ?? p.OrderCreatedAt)
            .ThenBy(p => p.OrderCreatedAt);

        return await PaginatedList<ServiceBoardPieceDto>.CreateAsync(
            projected,
            request.PageNumber,
            request.PageSize,
            cancellationToken);
    }
}
