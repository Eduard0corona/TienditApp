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

        await TryAutoReplyAsync(request, conversation, cancellationToken);

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

        for (var attempt = 1; ; attempt++)
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
                _context.Detach(conversation, message);

                if (ex is not DbUpdateException)
                    throw;

                // A concurrent delivery of the same message can pass the check in Handle; the
                // unique index on ExternalMessageId lets only one insert win.
                if (ex is not DbUpdateConcurrencyException
                    && await IsAlreadyStoredAsync(request.ExternalMessageId, cancellationToken))
                {
                    _logger.LogInformation(
                        "Duplicate WhatsApp message {ExternalMessageId} for tenant {TenantId} ignored; a concurrent delivery stored it first",
                        request.ExternalMessageId,
                        request.TenantId);
                    return null;
                }

                if (attempt == WhatsAppConversationConcurrency.MaxSaveAttempts)
                    throw;

                if (ex is DbUpdateConcurrencyException)
                {
                    // Another request changed the conversation after it was loaded (a parallel message,
                    // a reply, the chat marked read): add the message to the conversation as it is now.
                    conversation = await FindConversationAsync(request, cancellationToken);
                    continue;
                }

                // Two first messages from a new number can both try to start its conversation; the
                // unique index on (TenantId, CustomerPhone) lets only one insert win. The loser retries
                // in the winner's conversation.
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
        ProcessInboundMessageCommand request,
        WhatsAppConversation conversation,
        CancellationToken cancellationToken)
    {
        try
        {
            var reply = await _aiAgentAutoReplyService.BuildReplyAsync(
                request.TenantId,
                request.Body,
                conversation.CustomerName,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(reply))
                return;

            var sendResult = await _whatsAppService.SendTextMessageAsync(
                request.TenantId,
                conversation.CustomerPhone,
                reply,
                cancellationToken);

            if (sendResult.IsFailure)
            {
                _logger.LogWarning(
                    "AI auto-reply could not be sent for tenant {TenantId}, conversation {ConversationId}: {Error}",
                    request.TenantId,
                    conversation.Id,
                    sendResult.Error);
                return;
            }

            var (repliedConversation, outboundMessage) =
                await StoreAutoReplyAsync(request, conversation, reply, cancellationToken);

            var outboundDto = _mapper.Map<ChatMessageDto>(outboundMessage);
            var conversationDto = _mapper.Map<ConversationDto>(repliedConversation);
            await _notificationService.NotifyNewMessage(request.TenantId, conversationDto, outboundDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "AI auto-reply processing failed for tenant {TenantId}, conversation {ConversationId}",
                request.TenantId,
                conversation.Id);
        }
    }

    /// <summary>
    /// Records the reply the AI agent already sent. While the agent answered, the business often marks
    /// the chat read or the customer writes again, so the reply goes into the conversation as it is now.
    /// </summary>
    private async Task<(WhatsAppConversation Conversation, WhatsAppMessage Message)> StoreAutoReplyAsync(
        ProcessInboundMessageCommand request,
        WhatsAppConversation conversation,
        string reply,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var message = conversation.AddMessage(MessageDirection.Outbound, reply, externalMessageId: null);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return (conversation, message);
            }
            catch (Exception ex)
            {
                // Whatever the failure, discard what this attempt staged so the rest of the webhook
                // payload does not save it.
                _context.Detach(conversation, message);

                if (ex is not DbUpdateConcurrencyException
                    || attempt == WhatsAppConversationConcurrency.MaxSaveAttempts)
                    throw;

                conversation = await FindConversationAsync(request, cancellationToken)
                    ?? throw new InvalidOperationException($"Conversation {conversation.Id} no longer exists.");
            }
        }
    }
}
