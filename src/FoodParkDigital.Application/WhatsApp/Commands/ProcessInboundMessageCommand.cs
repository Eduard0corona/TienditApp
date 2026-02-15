using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Domain.WhatsApp.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.WhatsApp.Commands;

public record ProcessInboundMessageCommand : IRequest<Result<ChatMessageDto>>
{
    public Guid TenantId { get; init; }
    public string FromPhone { get; init; } = null!;
    public string? ProfileName { get; init; }
    public string Body { get; init; } = null!;
    public string ExternalMessageId { get; init; } = null!;
}

public class ProcessInboundMessageCommandHandler
    : IRequestHandler<ProcessInboundMessageCommand, Result<ChatMessageDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IWhatsAppNotificationService _notificationService;
    private readonly IMapper _mapper;

    public ProcessInboundMessageCommandHandler(
        IApplicationDbContext context,
        IWhatsAppNotificationService notificationService,
        IMapper mapper)
    {
        _context = context;
        _notificationService = notificationService;
        _mapper = mapper;
    }

    public async Task<Result<ChatMessageDto>> Handle(
        ProcessInboundMessageCommand request, CancellationToken cancellationToken)
    {
        // Dedup: check if message already processed
        var exists = await _context.WhatsAppMessages
            .IgnoreQueryFilters()
            .AnyAsync(m => m.ExternalMessageId == request.ExternalMessageId, cancellationToken);

        if (exists)
            return Result.Failure<ChatMessageDto>("Mensaje ya procesado.");

        // Find or create conversation (use IgnoreQueryFilters since webhook has no tenant context)
        var conversation = await _context.WhatsAppConversations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c =>
                c.TenantId == request.TenantId &&
                c.CustomerPhone == request.FromPhone,
                cancellationToken);

        if (conversation is null)
        {
            conversation = new WhatsAppConversation(request.TenantId, request.FromPhone, request.ProfileName);
            _context.WhatsAppConversations.Add(conversation);
        }
        else
        {
            conversation.UpdateCustomerName(request.ProfileName);
        }

        // Add inbound message
        var message = conversation.AddMessage(
            MessageDirection.Inbound, request.Body, request.ExternalMessageId);

        await _context.SaveChangesAsync(cancellationToken);

        var messageDto = _mapper.Map<ChatMessageDto>(message);
        var conversationDto = _mapper.Map<ConversationDto>(conversation);

        // Notify via SignalR
        await _notificationService.NotifyNewMessage(request.TenantId, conversationDto, messageDto);

        return Result.Success(messageDto);
    }
}
