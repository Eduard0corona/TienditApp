using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.WhatsApp.Queries;

[RequiresPermission(TenantPermission.ManageWhatsApp)]
public record GetConversationMessagesQuery : IRequest<Result<PaginatedList<ChatMessageDto>>>
{
    public Guid ConversationId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public class GetConversationMessagesQueryHandler
    : IRequestHandler<GetConversationMessagesQuery, Result<PaginatedList<ChatMessageDto>>>
{
    private readonly IApplicationDbContext _context;

    public GetConversationMessagesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<PaginatedList<ChatMessageDto>>> Handle(
        GetConversationMessagesQuery request, CancellationToken cancellationToken)
    {
        // Verify conversation belongs to current tenant (global filter handles this)
        var conversationExists = await _context.WhatsAppConversations
            .AsNoTracking()
            .AnyAsync(c => c.Id == request.ConversationId, cancellationToken);

        if (!conversationExists)
            return Result.Failure<PaginatedList<ChatMessageDto>>("Conversación no encontrada.");

        var query = _context.WhatsAppMessages
            .AsNoTracking()
            .Where(m => m.ConversationId == request.ConversationId)
            .OrderByDescending(m => m.SentAt)
            .Select(WhatsAppMappings.MessageAsDto);

        var result = await PaginatedList<ChatMessageDto>.CreateAsync(
            query, request.PageNumber, request.PageSize, cancellationToken);

        return Result.Success(result);
    }
}
