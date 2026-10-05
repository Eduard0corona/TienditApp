using FoodParkDigital.Application.Common;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Domain.WhatsApp.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.WhatsApp.Commands;

[AllowAnonymousRequest("Lo envía el procesador del webhook de WhatsApp (sistema), sin usuario.")]
public record ProcessInboundMessageCommand : IRequest<Result<ChatMessageDto>>
{
    public Guid TenantId { get; init; }
    public string FromPhone { get; init; } = null!;
    public string? ProfileName { get; init; }
    public string Body { get; init; } = null!;
    public string ExternalMessageId { get; init; } = null!;
    /// <summary>A photo, voice note, location... stored as a marker ("📷 Foto"): the automatic replies skip it.</summary>
    public bool IsMedia { get; init; }
}

public class ProcessInboundMessageCommandHandler
    : IRequestHandler<ProcessInboundMessageCommand, Result<ChatMessageDto>>
{
    public const string DuplicateMessageError = "Mensaje ya procesado.";

    private readonly IApplicationDbContext _context;
    private readonly IAiAgentAutoReplyService _aiAgentAutoReplyService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IWhatsAppNotificationService _notificationService;
    private readonly ILogger<ProcessInboundMessageCommandHandler> _logger;
    private readonly MessagingConsentService _consent;
    private readonly IDateTimeProvider _clock;

    public ProcessInboundMessageCommandHandler(
        IApplicationDbContext context,
        IAiAgentAutoReplyService aiAgentAutoReplyService,
        IWhatsAppService whatsAppService,
        IWhatsAppNotificationService notificationService,
        ILogger<ProcessInboundMessageCommandHandler> logger,
        MessagingConsentService? consent = null,
        IDateTimeProvider? clock = null)
    {
        _context = context;
        _aiAgentAutoReplyService = aiAgentAutoReplyService;
        _whatsAppService = whatsAppService;
        _notificationService = notificationService;
        _logger = logger;
        _clock = clock ?? SystemClock.Instance;
        _consent = consent ?? new MessagingConsentService(context, _clock);
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
        var messageDto = message.ToDto();
        var conversationDto = conversation.ToDto();

        // Notify via SignalR
        await _notificationService.NotifyNewMessage(request.TenantId, conversationDto, messageDto);

        // BAJA / ALTA get a confirmation instead of an automatic answer. The bot never answers over a person of the
        // team (who paused it by writing) nor to a photo or voice note it cannot read.
        if (!await TryHandleOptOutKeywordAsync(request, conversation, cancellationToken)
            && !request.IsMedia
            && !conversation.AutoRepliesPausedAt(_clock.UtcNow))
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
    // WhatsApp reports Mexican mobiles as +521…; a conversation the business started with +52… is the same one.
    private async Task<WhatsAppConversation?> FindConversationAsync(
        ProcessInboundMessageCommand request, CancellationToken cancellationToken)
    {
        var variants = MessagingPhone.Variants(request.FromPhone).Append(request.FromPhone).Distinct().ToList();
        // At most one conversation per form of the number: the exact one wins, then the most recent.
        var candidates = await _context.WhatsAppConversations
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == request.TenantId && variants.Contains(c.CustomerPhone))
            .ToListAsync(cancellationToken);
        return candidates
            .OrderByDescending(c => c.CustomerPhone == request.FromPhone)
            .ThenByDescending(c => c.LastMessageAt)
            .FirstOrDefault();
    }

    // Webhooks run without tenant context; Meta message ids are globally unique.
    private Task<bool> IsAlreadyStoredAsync(string externalMessageId, CancellationToken cancellationToken)
        => _context.WhatsAppMessages
            .IgnoreQueryFilters()
            .AnyAsync(m => m.ExternalMessageId == externalMessageId, cancellationToken);

    /// <summary>
    /// Records BAJA / ALTA (campaigns and reminders stop or resume) and confirms it to the customer, who just
    /// wrote, so a plain text reply is inside WhatsApp's 24-hour window.
    /// </summary>
    /// <returns>True when the message was a keyword.</returns>
    private async Task<bool> TryHandleOptOutKeywordAsync(
        ProcessInboundMessageCommand request,
        WhatsAppConversation conversation,
        CancellationToken cancellationToken)
    {
        var keyword = MessagingKeywords.Parse(request.Body);
        if (keyword == MessagingKeyword.None)
            return false;

        var optedOut = keyword == MessagingKeyword.OptOut;
        try
        {
            if (await _consent.SetAsync(request.TenantId, request.FromPhone, optedOut, OptOutSource.CustomerKeyword, cancellationToken))
            {
                _context.AuditLogs.Add(new AuditLog(
                    request.TenantId,
                    "MessagingOptOut",
                    conversation.Id.ToString(),
                    optedOut ? "MessagingOptOut" : "MessagingOptIn",
                    "cliente por WhatsApp",
                    $"{{\"Source\":\"{OptOutSource.CustomerKeyword}\"}}"));
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A concurrent BAJA from the same number stores it first (unique index); discard this attempt so the
            // rest of the webhook payload does not save it.
            foreach (var entry in _context.ChangeTracker.Entries()
                         .Where(e => e.State is EntityState.Added or EntityState.Modified)
                         .Where(e => e.Entity is Domain.Messaging.Entities.MessagingOptOut or AuditLog)
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }

            _logger.LogWarning(
                "Opt-out keyword of conversation {ConversationId} for tenant {TenantId} could not be stored: {ErrorType}",
                conversation.Id,
                request.TenantId,
                ex.GetType().Name);
        }

        await TrySendReplyAsync(
            request,
            conversation,
            optedOut ? MessagingDefaults.OptOutReply : MessagingDefaults.OptInReply,
            "Opt-out confirmation",
            cancellationToken);
        return true;
    }

    private async Task TryAutoReplyAsync(
        ProcessInboundMessageCommand request,
        WhatsAppConversation conversation,
        CancellationToken cancellationToken)
    {
        string? reply;
        try
        {
            reply = await _aiAgentAutoReplyService.BuildReplyAsync(
                request.TenantId,
                request.Body,
                conversation.CustomerName,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "AI auto-reply processing failed for tenant {TenantId}, conversation {ConversationId}",
                request.TenantId,
                conversation.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(reply))
            return;

        await TrySendReplyAsync(request, conversation, reply, "AI auto-reply", cancellationToken);
    }

    /// <summary>Sends a text reply and records it in the conversation; failures are logged, never thrown.</summary>
    private async Task TrySendReplyAsync(
        ProcessInboundMessageCommand request,
        WhatsAppConversation conversation,
        string reply,
        string kind,
        CancellationToken cancellationToken)
    {
        try
        {
            var sendResult = await _whatsAppService.SendTextMessageAsync(
                request.TenantId,
                conversation.CustomerPhone,
                reply,
                cancellationToken);

            if (sendResult.IsFailure)
            {
                _logger.LogWarning(
                    "{ReplyKind} could not be sent for tenant {TenantId}, conversation {ConversationId}: {Error}",
                    kind,
                    request.TenantId,
                    conversation.Id,
                    sendResult.Error);
                return;
            }

            var (repliedConversation, outboundMessage) =
                await StoreAutoReplyAsync(request, conversation, reply, sendResult.ProviderMessageId, cancellationToken);

            var outboundDto = outboundMessage.ToDto();
            var conversationDto = repliedConversation.ToDto();
            await _notificationService.NotifyNewMessage(request.TenantId, conversationDto, outboundDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "{ReplyKind} processing failed for tenant {TenantId}, conversation {ConversationId}",
                kind,
                request.TenantId,
                conversation.Id);
        }
    }

    /// <summary>
    /// Records a reply already sent (AI agent or opt-out confirmation). While it was sent, the business often marks
    /// the chat read or the customer writes again, so the reply goes into the conversation as it is now.
    /// </summary>
    private async Task<(WhatsAppConversation Conversation, WhatsAppMessage Message)> StoreAutoReplyAsync(
        ProcessInboundMessageCommand request,
        WhatsAppConversation conversation,
        string reply,
        string? providerMessageId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            // The wamid lets the delivery statuses (the ticks) reach the reply.
            var message = conversation.AddMessage(MessageDirection.Outbound, reply, providerMessageId);

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
