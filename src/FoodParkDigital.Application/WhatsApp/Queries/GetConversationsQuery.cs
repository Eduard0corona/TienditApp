using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.WhatsApp.Queries;

[RequiresPermission(TenantPermission.ManageWhatsApp)]
public record GetConversationsQuery : IRequest<PaginatedList<ConversationDto>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Search { get; init; }
}

public class GetConversationsQueryHandler : IRequestHandler<GetConversationsQuery, PaginatedList<ConversationDto>>
{
    private readonly IApplicationDbContext _context;

    public GetConversationsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<ConversationDto>> Handle(GetConversationsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.WhatsAppConversations
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(c =>
                c.CustomerPhone.Contains(search) ||
                (c.CustomerName != null && c.CustomerName.ToLower().Contains(search)));
        }

        var projected = query
            .OrderByDescending(c => c.LastMessageAt)
            .Select(WhatsAppMappings.ConversationAsDto);

        return await PaginatedList<ConversationDto>.CreateAsync(
            projected, request.PageNumber, request.PageSize, cancellationToken);
    }
}
