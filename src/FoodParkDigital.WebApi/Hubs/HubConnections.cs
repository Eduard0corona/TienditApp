using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace FoodParkDigital.WebApi.Hubs;

/// <summary>
/// The live hub connections of this instance, each with the rule its hub checked when it connected.
/// <see cref="HubAccessSweeper"/> closes the ones whose session ended or that lost that access.
/// </summary>
public sealed class HubConnections
{
    private readonly ConcurrentDictionary<string, HubConnection> _connections = new();

    public void Add(HubCallerContext context, Func<HubTenantAccess, bool> allowed) =>
        _connections[context.ConnectionId] = new HubConnection(context, allowed);

    /// <summary>A connection of an account, not of a business (ADR 0083): only its session is checked again.</summary>
    public void AddAccount(HubCallerContext context) =>
        _connections[context.ConnectionId] = new HubConnection(context, null);

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    public IReadOnlyList<HubConnection> Snapshot() => _connections.Values.ToList();
}

/// <param name="Allowed">
/// What the hub requires of the business access (its permissions), checked again later; null for the hub of an account,
/// which requires only an open session.
/// </param>
public sealed record HubConnection(HubCallerContext Context, Func<HubTenantAccess, bool>? Allowed);
