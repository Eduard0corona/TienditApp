using System.Collections.Frozen;
using System.Text.Json;
using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Infrastructure.BackgroundJobs.Outbox;

/// <summary>Stores domain events as JSON and reads them back by type name.</summary>
public static class OutboxEventSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static readonly FrozenDictionary<string, Type> EventTypes = typeof(DomainEvent).Assembly
        .GetTypes()
        .Where(type => type is { IsAbstract: false, IsClass: true } && typeof(DomainEvent).IsAssignableFrom(type))
        .ToFrozenDictionary(type => type.FullName!, StringComparer.Ordinal);

    public static IReadOnlyCollection<Type> KnownEventTypes => EventTypes.Values;

    public static string TypeNameOf(DomainEvent domainEvent) => domainEvent.GetType().FullName!;

    public static string Serialize(DomainEvent domainEvent)
        => JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), Options);

    /// <returns>Null when the type no longer exists (renamed or removed event).</returns>
    public static DomainEvent? Deserialize(string typeName, string payload)
        => EventTypes.TryGetValue(typeName, out var type)
            ? (DomainEvent?)JsonSerializer.Deserialize(payload, type, Options)
            : null;
}
