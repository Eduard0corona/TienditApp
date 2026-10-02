using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Domain.WhatsApp.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.WhatsApp.Commands;

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
        RuleFor(x => x.PhoneNumber).NotEmpty()
            .Matches(@"^\+[1-9]\d{6,14}$")
            .WithMessage("El teléfono debe estar en formato E.164.");
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

    public SendChatMessageCommandHandler(
        IApplicationDbContext context,
        IWhatsAppService whatsAppService,
        IWhatsAppNotificationService notificationService,
        ICurrentTenantService tenantService,
        IMapper mapper,
        ILogger<SendChatMessageCommandHandler> logger)
    {
        _context = context;
        _whatsAppService = whatsAppService;
        _notificationService = notificationService;
        _tenantService = tenantService;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<Result<ChatMessageDto>> Handle(SendChatMessageCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;

        // Send via WhatsApp API
        var sendResult = await _whatsAppService.SendTextMessageAsync(
            tenantId, request.PhoneNumber, request.Message, cancellationToken);

        if (sendResult.IsFailure)
            return Result.Failure<ChatMessageDto>(sendResult.Error!);

        var (conversation, message) = await StoreOutboundMessageAsync(tenantId, request, cancellationToken);

        var messageDto = _mapper.Map<ChatMessageDto>(message);
        var conversationDto = _mapper.Map<ConversationDto>(conversation);

        // Notify via SignalR
        await _notificationService.NotifyNewMessage(tenantId, conversationDto, messageDto);

        return Result.Success(messageDto);
    }

    /// <summary>
    /// Records the message already sent to the customer in their conversation, starting it if the
    /// number has none yet.
    /// </summary>
    private async Task<(WhatsAppConversation Conversation, WhatsAppMessage Message)> StoreOutboundMessageAsync(
        Guid tenantId, SendChatMessageCommand request, CancellationToken cancellationToken)
    {
        // Find or create conversation
        var conversation = await FindConversationAsync(request.PhoneNumber, cancellationToken);

        while (true)
        {
            var startsConversation = conversation is null;
            if (conversation is null)
            {
                conversation = new WhatsAppConversation(tenantId, request.PhoneNumber);
                _context.WhatsAppConversations.Add(conversation);
            }

            // Add outbound message
            var message = conversation.AddMessage(MessageDirection.Outbound, request.Message, null);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return (conversation, message);
            }
            catch (DbUpdateException) when (startsConversation)
            {
                // The customer's first message (through the webhook) or another send can start the
                // conversation after this request found none; the unique index on (TenantId, CustomerPhone)
                // lets only one insert win. The message already reached the customer, so the loser discards
                // what it staged and retries once in the winner's conversation; that retry starts nothing,
                // so it cannot loop.
                _context.WhatsAppMessages.Entry(message).State = EntityState.Detached;
                _context.WhatsAppConversations.Entry(conversation).State = EntityState.Detached;

                conversation = await FindConversationAsync(request.PhoneNumber, cancellationToken);
                if (conversation is null)
                    throw;

                _logger.LogInformation(
                    "WhatsApp message sent by tenant {TenantId} joins conversation {ConversationId}, which a concurrent request started",
                    tenantId,
                    conversation.Id);
            }
        }
    }

    // The global query filter limits the lookup to the conversations of the request's tenant.
    private Task<WhatsAppConversation?> FindConversationAsync(string customerPhone, CancellationToken cancellationToken)
        => _context.WhatsAppConversations
            .FirstOrDefaultAsync(c => c.CustomerPhone == customerPhone, cancellationToken);
}
