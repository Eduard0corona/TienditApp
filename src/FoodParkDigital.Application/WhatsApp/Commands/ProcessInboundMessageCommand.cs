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
    public const string DuplicateMessageError = "Mensaje ya procesado.";

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
        if (await IsAlreadyStoredAsync(request.ExternalMessageId, cancellationToken))
            return Result.Failure<ChatMessageDto>(DuplicateMessageError);

        var stored = await StoreInboundMessageAsync(request, cancellationToken);
        if (stored is null)
            return Result.Failure<ChatMessageDto>(DuplicateMessageError);

        var (conversation, message) = stored.Value;
        var messageDto = _mapper.Map<ChatMessageDto>(message);
        var conversationDto = _mapper.Map<ConversationDto>(conversation);

        // Notify via SignalR
        await _notificationService.NotifyNewMessage(request.TenantId, conversationDto, messageDto);

        await TryAutoReplyAsync(request.TenantId, conversation, request.Body, cancellationToken);

        return Result.Success(messageDto);
    }

    /// <summary>
    /// Stores the message in the sender's conversation, starting it with their first message.
    /// Returns <c>null</c> when a concurrent delivery of the same message stored it first.
    /// </summary>
    private async Task<(WhatsAppConversation Conversation, WhatsAppMessage Message)?> StoreInboundMessageAsync(
        ProcessInboundMessageCommand request, CancellationToken cancellationToken)
    {
        var conversation = await FindConversationAsync(request, cancellationToken);

        while (true)
        {
            var startsConversation = conversation is null;
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

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return (conversation, message);
            }
            catch (Exception ex)
            {
                // Whatever the failure, discard what this attempt staged so the scoped context stays
                // usable for the retry below and for the rest of the webhook payload.
                _context.WhatsAppMessages.Entry(message).State = EntityState.Detached;
                _context.WhatsAppConversations.Entry(conversation).State = EntityState.Detached;

                if (ex is not DbUpdateException)
                    throw;

                // A concurrent delivery of the same message can pass the check in Handle; the
                // unique index on ExternalMessageId lets only one insert win.
                if (await IsAlreadyStoredAsync(request.ExternalMessageId, cancellationToken))
                {
                    _logger.LogInformation(
                        "Duplicate WhatsApp message {ExternalMessageId} for tenant {TenantId} ignored; a concurrent delivery stored it first",
                        request.ExternalMessageId,
                        request.TenantId);
                    return null;
                }

                // Two first messages from a new number can both try to start its conversation; the
                // unique index on (TenantId, CustomerPhone) lets only one insert win. The loser retries
                // once in the winner's conversation; that retry starts nothing, so it cannot loop.
                conversation = startsConversation
                    ? await FindConversationAsync(request, cancellationToken)
                    : null;

                if (conversation is null)
                    throw;

                _logger.LogInformation(
                    "WhatsApp message {ExternalMessageId} for tenant {TenantId} joins the conversation a concurrent delivery started",
                    request.ExternalMessageId,
                    request.TenantId);
            }
        }
    }

    // Webhooks run without tenant context, so the tenant the delivery was routed to is filtered explicitly.
    private Task<WhatsAppConversation?> FindConversationAsync(
        ProcessInboundMessageCommand request, CancellationToken cancellationToken)
        => _context.WhatsAppConversations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c =>
                c.TenantId == request.TenantId &&
                c.CustomerPhone == request.FromPhone,
                cancellationToken);

    // Webhooks run without tenant context; Meta message ids are globally unique.
    private Task<bool> IsAlreadyStoredAsync(string externalMessageId, CancellationToken cancellationToken)
        => _context.WhatsAppMessages
            .IgnoreQueryFilters()
            .AnyAsync(m => m.ExternalMessageId == externalMessageId, cancellationToken);

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
