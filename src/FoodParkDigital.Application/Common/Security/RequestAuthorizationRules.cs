using System.Collections.Concurrent;
using System.Reflection;

namespace FoodParkDigital.Application.Common.Security;

/// <summary>The authorization rule of each request type, read once by reflection.</summary>
public static class RequestAuthorizationRules
{
    private static readonly ConcurrentDictionary<Type, RequestAuthorizationRule> Cache = new();

    public static RequestAuthorizationRule For(Type requestType)
        => Cache.GetOrAdd(requestType, static type => new RequestAuthorizationRule(
            type.GetCustomAttributes<RequestAuthorizationAttribute>(inherit: false).ToList(),
            type.GetCustomAttribute<RequiresCapabilityAttribute>(inherit: false)));
}

public sealed record RequestAuthorizationRule(
    IReadOnlyList<RequestAuthorizationAttribute> Rules,
    RequiresCapabilityAttribute? Capability)
{
    /// <summary>The single rule of the request; null when it has none or more than one (both are denied).</summary>
    public RequestAuthorizationAttribute? Primary => Rules.Count == 1 ? Rules[0] : null;
}
