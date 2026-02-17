using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Domain.WhatsApp.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
    private readonly IAiAgentAutoReplyService _aiAgentAutoReplyService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IWhatsAppNotificationService _notificationService;
    private readonly IMapper _mapper;
    private readonly ILogger<ProcessInboundMessageCommandHandler> _logger;

    public ProcessInboundMessageCommandHandler(
        IApplicationDbContext context,
        IAiAgentAutoReplyService aiAgentAutoReplyService,
        IWhatsAppService whatsAppService,
        IWhatsAppNotificationService notificationService,
        IMapper mapper,
        ILogger<ProcessInboundMessageCommandHandler> logger)
    {
        _context = context;
        _aiAgentAutoReplyService = aiAgentAutoReplyService;
        _whatsAppService = whatsAppService;
        _notificationService = notificationService;
        _mapper = mapper;
        _logger = logger;
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

        await TryAutoReplyAsync(request.TenantId, conversation, request.Body, cancellationToken);

        return Result.Success(messageDto);
    }

    private async Task TryAutoReplyAsync(
        Guid tenantId,
        WhatsAppConversation conversation,
        string inboundBody,
        CancellationToken cancellationToken)
    {
        try
        {
            var reply = await _aiAgentAutoReplyService.BuildReplyAsync(
                tenantId,
                inboundBody,
                conversation.CustomerName,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(reply))
                return;

            var sendResult = await _whatsAppService.SendTextMessageAsync(
                tenantId,
                conversation.CustomerPhone,
                reply,
                cancellationToken);

            if (sendResult.IsFailure)
            {
                _logger.LogWarning(
                    "AI auto-reply could not be sent for tenant {TenantId}, conversation {ConversationId}: {Error}",
                    tenantId,
                    conversation.Id,
                    sendResult.Error);
                return;
            }

            var outboundMessage = conversation.AddMessage(
                MessageDirection.Outbound,
                reply,
                externalMessageId: null);
            await _context.SaveChangesAsync(cancellationToken);

            var outboundDto = _mapper.Map<ChatMessageDto>(outboundMessage);
            var conversationDto = _mapper.Map<ConversationDto>(conversation);
            await _notificationService.NotifyNewMessage(tenantId, conversationDto, outboundDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "AI auto-reply processing failed for tenant {TenantId}, conversation {ConversationId}",
                tenantId,
                conversation.Id);
        }
    }
}
