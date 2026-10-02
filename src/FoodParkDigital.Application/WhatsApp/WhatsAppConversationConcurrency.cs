using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.WhatsApp.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.WhatsApp;

/// <summary>
/// Parallel webhook deliveries, replies and "mark as read" all rewrite the summary of the same
/// conversation (unread count, last message). Its rowversion makes a save based on a stale copy fail
/// with <see cref="DbUpdateConcurrencyException"/> instead of overwriting the other request's change;
/// the writer then detaches what it staged, loads the conversation again and reapplies its change
/// through the aggregate.
/// </summary>
internal static class WhatsAppConversationConcurrency
{
    /// <summary>
    /// Saves attempted before the conflict reaches the caller. Each round of writers that loaded the
    /// same version lets exactly one of them commit, so the last of N simultaneous writers needs N
    /// attempts; this leaves room for a burst of messages, their auto-replies and the reads of an open chat.
    /// </summary>
    public const int MaxSaveAttempts = 10;

    /// <summary>
    /// Stops tracking a conversation and the message a failed save staged in it, so the scoped context
    /// stays usable and the next query returns the committed conversation instead of this stale copy.
    /// </summary>
    public static void Detach(
        this IApplicationDbContext context, WhatsAppConversation conversation, WhatsAppMessage? message = null)
    {
        if (message is not null)
            context.WhatsAppMessages.Entry(message).State = EntityState.Detached;

        context.WhatsAppConversations.Entry(conversation).State = EntityState.Detached;
    }
}
