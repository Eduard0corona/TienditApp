using AutoMapper;
using FoodParkDigital.Application.Common;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Domain.WhatsApp.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.WhatsApp.Commands;

[RequiresPermission(TenantPermission.ManageWhatsApp)]
public record SendChatMessageCommand : IRequest<Result<ChatMessageDto>>
{
    public string PhoneNumber { get; init; } = null!;
    public string Message { get; init; } = null!;
    public Guid? ConversationId { get; init; }
}

public class SendChatMessageCommandValidator : AbstractValidator<SendChatMessageCommand>
{
    public SendChatMessageCommandValidator()
    {
        // Ten Mexican digits or international format: the handler normalizes it.
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Message).NotEmpty()
            .MaximumLength(4096);
    }
}

public class SendChatMessageCommandHandler : IRequestHandler<SendChatMessageCommand, Result<ChatMessageDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IWhatsAppNotificationService _notificationService;
    private readonly ICurrentTenantService _tenantService;
    private readonly IMapper _mapper;
    private readonly ILogger<SendChatMessageCommandHandler> _logger;
    private readonly IPhoneNormalizationService _phones;
    private readonly IDateTimeProvider _clock;

    public SendChatMessageCommandHandler(
        IApplicationDbContext context,
        IWhatsAppService whatsAppService,
        IWhatsAppNotificationService notificationService,
        ICurrentTenantService tenantService,
        IMapper mapper,
        ILogger<SendChatMessageCommandHandler> logger,
        IPhoneNormalizationService phones,
        IDateTimeProvider? clock = null)
    {
        _context = context;
        _whatsAppService = whatsAppService;
        _notificationService = notificationService;
        _tenantService = tenantService;
        _mapper = mapper;
        _logger = logger;
        _phones = phones;
        _clock = clock ?? SystemClock.Instance;
    }

    public async Task<Result<ChatMessageDto>> Handle(SendChatMessageCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        if (!_phones.TryNormalizeToE164(request.PhoneNumber, "MX", out var typedPhone))
            return Result.Failure<ChatMessageDto>("Escribe el teléfono a 10 dígitos o con lada internacional (+52…).");

        // An existing conversation answers to the number as WhatsApp reported it (+521… for Mexican mobiles).
        var existing = await FindConversationAsync(typedPhone, cancellationToken);
        var phone = existing?.CustomerPhone ?? typedPhone;

        // Send via WhatsApp API
        var sendResult = await _whatsAppService.SendTextMessageAsync(
            tenantId, phone, request.Message, cancellationToken);

        if (sendResult.IsFailure)
            return Result.Failure<ChatMessageDto>(sendResult.Error!);

        var (conversation, message) = await StoreOutboundMessageAsync(
            tenantId, phone, request.Message, sendResult.ProviderMessageId, existing, cancellationToken);

        var messageDto = _mapper.Map<ChatMessageDto>(message);
        var conversationDto = _mapper.Map<ConversationDto>(conversation);

        // Notify via SignalR
        await _notificationService.NotifyNewMessage(tenantId, conversationDto, messageDto);

        return Result.Success(messageDto);
    }

    /// <summary>
    /// Records the message already sent to the customer in their conversation, starting it if the
    /// number has none yet. If the customer wrote, or the chat was marked read, after the conversation
    /// was loaded, the message goes into the conversation as it is now.
    /// </summary>
    private async Task<(WhatsAppConversation Conversation, WhatsAppMessage Message)> StoreOutboundMessageAsync(
        Guid tenantId,
        string phone,
        string body,
        string? providerMessageId,
        WhatsAppConversation? conversation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var startsConversation = conversation is null;
            if (conversation is null)
            {
                conversation = new WhatsAppConversation(tenantId, phone);
                _context.WhatsAppConversations.Add(conversation);
            }

            // The wamid lets the delivery statuses of WhatsApp (the ticks) reach this message.
            var message = conversation.AddMessage(MessageDirection.Outbound, body, providerMessageId);
            // A person answered: the automatic replies stay out of this conversation for a while.
            conversation.PauseAutoReplies(_clock.UtcNow.Add(WhatsAppConversation.HumanReplyPause));

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return (conversation, message);
            }
            catch (DbUpdateConcurrencyException) when (attempt < WhatsAppConversationConcurrency.MaxSaveAttempts)
            {
                // The customer wrote, or the chat was marked read, after the conversation was loaded.
                _context.Detach(conversation, message);
                conversation = await FindConversationAsync(phone, cancellationToken);
            }
            catch (DbUpdateException) when (startsConversation)
            {
                // The customer's first message (through the webhook) or another send can start the
                // conversation after this request found none; the unique index on (TenantId, CustomerPhone)
                // lets only one insert win. The message already reached the customer, so the loser discards
                // what it staged and retries once in the winner's conversation; that retry starts nothing,
                // so it cannot loop.
                _context.Detach(conversation, message);

                conversation = await FindConversationAsync(phone, cancellationToken);
                if (conversation is null)
                    throw;

                _logger.LogInformation(
                    "WhatsApp message sent by tenant {TenantId} joins conversation {ConversationId}, which a concurrent request started",
                    tenantId,
                    conversation.Id);
            }
        }
    }

    // The global query filter limits the lookup to the conversations of the request's tenant. The same Mexican
    // mobile can be saved as +52… (typed) or +521… (as WhatsApp reports it): both are one conversation.
    private async Task<WhatsAppConversation?> FindConversationAsync(string customerPhone, CancellationToken cancellationToken)
    {
        var variants = MessagingPhone.Variants(customerPhone).Append(customerPhone).Distinct().ToList();
        // At most one conversation per form of the number: the exact one wins, then the most recent.
        var candidates = await _context.WhatsAppConversations
            .Where(c => variants.Contains(c.CustomerPhone))
            .ToListAsync(cancellationToken);
        return candidates
            .OrderByDescending(c => c.CustomerPhone == customerPhone)
            .ThenByDescending(c => c.LastMessageAt)
            .FirstOrDefault();
    }
}
