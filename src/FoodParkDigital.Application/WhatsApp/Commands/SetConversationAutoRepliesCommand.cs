using FoodParkDigital.Application.Common;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.WhatsApp.Commands;

/// <summary>
/// Pauses the automatic replies of one conversation for a day (a person handles it) or resumes them. Writing in
/// the chat pauses them on its own.
/// </summary>
[RequiresPermission(TenantPermission.ManageWhatsApp)]
public record SetConversationAutoRepliesCommand(Guid ConversationId, bool Paused) : IRequest<Result<ConversationDto>>;

public class SetConversationAutoRepliesCommandHandler : IRequestHandler<SetConversationAutoRepliesCommand, Result<ConversationDto>>
{
    public static readonly TimeSpan ManualPause = TimeSpan.FromHours(24);

    private readonly IApplicationDbContext _context;
    private readonly IWhatsAppNotificationService _notifications;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;

    public SetConversationAutoRepliesCommandHandler(
        IApplicationDbContext context,
        IWhatsAppNotificationService notifications,
        ICurrentTenantService tenantService,
        IDateTimeProvider? clock = null)
    {
        _context = context;
        _notifications = notifications;
        _tenantService = tenantService;
        _clock = clock ?? SystemClock.Instance;
    }

    public async Task<Result<ConversationDto>> Handle(SetConversationAutoRepliesCommand request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            // Conversations are filtered by business: another business's conversation is not found.
            var conversation = await _context.WhatsAppConversations
                .FirstOrDefaultAsync(c => c.Id == request.ConversationId, cancellationToken);
            if (conversation is null)
                return Result.Failure<ConversationDto>("Conversación no encontrada.");

            if (request.Paused)
                conversation.PauseAutoReplies(_clock.UtcNow.Add(ManualPause));
            else
                conversation.ResumeAutoReplies();

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                var dto = conversation.ToDto();
                await _notifications.NotifyConversationUpdated(_tenantService.TenantId, dto);
                return Result.Success(dto);
            }
            catch (DbUpdateConcurrencyException) when (attempt < WhatsAppConversationConcurrency.MaxSaveAttempts)
            {
                // A message arrived after the conversation was loaded: apply the change to it as it is now.
                _context.Detach(conversation);
            }
        }
    }
}
