using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.WhatsApp.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.WhatsApp.Commands;

public record MarkConversationReadCommand(Guid ConversationId) : IRequest<Result>;

public class MarkConversationReadCommandHandler : IRequestHandler<MarkConversationReadCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public MarkConversationReadCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(MarkConversationReadCommand request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var conversation = await _context.WhatsAppConversations
                .FirstOrDefaultAsync(c => c.Id == request.ConversationId, cancellationToken);

            if (conversation is null)
                return Result.Failure("Conversación no encontrada.");

            conversation.MarkAsRead();

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }
            catch (DbUpdateConcurrencyException) when (attempt < WhatsAppConversationConcurrency.MaxSaveAttempts)
            {
                // A message or a reply was stored after the conversation was loaded: mark it as it is now.
                _context.Detach(conversation);
            }
        }
    }
}
