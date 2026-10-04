using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.BackgroundJobs.Inbox;

public sealed class WebhookInbox : IWebhookInbox
{
    private readonly ApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly InboxSignal _signal;

    public WebhookInbox(ApplicationDbContext context, IDateTimeProvider clock, InboxSignal signal)
    {
        _context = context;
        _clock = clock;
        _signal = signal;
    }

    public async Task<bool> EnqueueAsync(string source, string payload, CancellationToken cancellationToken = default)
    {
        var hash = InboxMessage.HashOf(payload);
        if (await _context.InboxMessages.AnyAsync(m => m.Source == source && m.PayloadHash == hash, cancellationToken))
            return false;

        var message = new InboxMessage(source, payload, _clock.UtcNow);
        _context.InboxMessages.Add(message);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.Entry(message).State = EntityState.Detached;
            // The same delivery arrived twice at once and the unique index kept the other one.
            if (await IsStoredAsync(source, hash))
                return false;
            throw;
        }

        _signal.Notify();
        return true;
    }

    private Task<bool> IsStoredAsync(string source, string hash)
        => _context.InboxMessages.AsNoTracking().AnyAsync(m => m.Source == source && m.PayloadHash == hash);
}
