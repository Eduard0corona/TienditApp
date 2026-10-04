using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FoodParkDigital.Infrastructure.BackgroundJobs.Outbox;

/// <summary>
/// Writes the domain events of the tracked entities to <see cref="OutboxMessage"/> in the same
/// <c>SaveChanges</c> (and therefore the same transaction) as the change that raised them, and wakes the
/// dispatcher once it is committed. The entities keep their events: <c>ApplicationDbContext</c> still
/// publishes them in-process after the save for the realtime handlers.
/// </summary>
public sealed class OutboxSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly OutboxSignal _signal;
    private bool _wroteMessages;

    public OutboxSaveChangesInterceptor(
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider,
        OutboxSignal signal)
    {
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
        _signal = signal;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        WriteMessages(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        WriteMessages(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        WakeDispatcher(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        WakeDispatcher(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private void WriteMessages(DbContext? context)
    {
        if (context is null)
            return;

        var entries = context.ChangeTracker
            .Entries<BaseEntity>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .ToList();
        if (entries.Count == 0)
            return;

        // A save retried after a failure still tracks the messages added by the failed attempt.
        var stored = context.ChangeTracker.Entries<OutboxMessage>()
            .Select(entry => entry.Entity.Id)
            .ToHashSet();

        var now = _dateTimeProvider.UtcNow;
        foreach (var entry in entries)
        {
            var tenantId = entry.Entity is ITenantScoped tenantScoped ? tenantScoped.TenantId : (Guid?)null;
            foreach (var domainEvent in entry.Entity.DomainEvents)
            {
                if (!stored.Add(domainEvent.EventId))
                    continue;

                context.Add(new OutboxMessage(
                    domainEvent.EventId,
                    OutboxEventSerializer.TypeNameOf(domainEvent),
                    OutboxEventSerializer.Serialize(domainEvent),
                    tenantId,
                    _currentUserService.UserId,
                    _currentUserService.UserName,
                    domainEvent.OccurredOn,
                    now));
                _wroteMessages = true;
            }
        }
    }

    private void WakeDispatcher(DbContext? context)
    {
        if (!_wroteMessages)
            return;

        // Inside a user transaction the messages are visible to the dispatcher only after the commit;
        // its next poll picks them up.
        if (context?.Database.CurrentTransaction is null)
            _signal.Notify();
        _wroteMessages = false;
    }
}
