using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Infrastructure.Persistence;

namespace FoodParkDigital.Infrastructure.BackgroundJobs.Outbox;

/// <summary>
/// Tracks the event as an <see cref="OutboxMessage"/> in the request's context, as <see cref="OutboxSaveChangesInterceptor"/>
/// does with the events of the entities: the next save stores it and wakes the dispatcher.
/// </summary>
public sealed class OutboxWriter : IOutbox
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public OutboxWriter(ApplicationDbContext context, ICurrentUserService currentUserService, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
    }

    public void Add(DomainEvent domainEvent, Guid? tenantId)
        => _context.OutboxMessages.Add(new OutboxMessage(
            domainEvent.EventId,
            OutboxEventSerializer.TypeNameOf(domainEvent),
            OutboxEventSerializer.Serialize(domainEvent),
            tenantId,
            _currentUserService.UserId,
            _currentUserService.UserName,
            domainEvent.OccurredOn,
            _dateTimeProvider.UtcNow));
}
