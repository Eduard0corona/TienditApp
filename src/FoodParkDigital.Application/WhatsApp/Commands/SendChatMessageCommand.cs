using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Domain.WhatsApp.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

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

    public SendChatMessageCommandHandler(
        IApplicationDbContext context,
        IWhatsAppService whatsAppService,
        IWhatsAppNotificationService notificationService,
        ICurrentTenantService tenantService,
        IMapper mapper)
    {
        _context = context;
        _whatsAppService = whatsAppService;
        _notificationService = notificationService;
        _tenantService = tenantService;
        _mapper = mapper;
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
    /// Records the message already sent to the customer. If the customer wrote, or the chat was marked
    /// read, after the conversation was loaded, the message goes into the conversation as it is now.
    /// </summary>
    private async Task<(WhatsAppConversation Conversation, WhatsAppMessage Message)> StoreOutboundMessageAsync(
        Guid tenantId, SendChatMessageCommand request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            // Find or create conversation
            var conversation = await _context.WhatsAppConversations
                .FirstOrDefaultAsync(c => c.CustomerPhone == request.PhoneNumber, cancellationToken);

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
            catch (DbUpdateConcurrencyException) when (attempt < WhatsAppConversationConcurrency.MaxSaveAttempts)
            {
                _context.Detach(conversation, message);
            }
        }
    }
}
