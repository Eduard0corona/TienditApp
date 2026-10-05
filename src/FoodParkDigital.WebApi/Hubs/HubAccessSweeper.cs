using System.Security.Claims;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Identity.Services;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.WebApi.Hubs;

/// <summary>How often the live hub connections are checked again.</summary>
public sealed class HubAccessOptions
{
    public const string SectionName = "Hubs";

    public TimeSpan AccessCheckInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// A connection is checked when it opens, but it lives on: every <see cref="HubAccessOptions.AccessCheckInterval"/> the
/// connections of this instance are checked again like a new one would be, and those whose session ended (signing
/// out, a new or recovered password, a reused token, an inactive account) or that lost the business access their hub
/// requires are closed. Without it they would keep receiving the business's events until their token expires.
/// </summary>
public sealed class HubAccessSweeper : BackgroundService
{
    private readonly HubConnections _connections;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<HubAccessOptions> _options;
    private readonly ILogger<HubAccessSweeper> _logger;

    public HubAccessSweeper(
        HubConnections connections,
        IServiceScopeFactory scopeFactory,
        IOptions<HubAccessOptions> options,
        ILogger<HubAccessSweeper> logger)
    {
        _connections = connections;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.Value.AccessCheckInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The connections stay until the next pass: a database that is briefly away closes nobody.
                    _logger.LogWarning(ex, "Live connections could not be checked again; trying on the next pass.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Closes the connections that would not be allowed to connect now. Returns how many it closed.</summary>
    internal async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var live = _connections.Snapshot();
        if (live.Count == 0)
            return 0;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAccessTokenSessionValidator>();
        var tenantAccess = scope.ServiceProvider.GetRequiredService<ITenantAccessService>();

        var open = await sessions.AreOpenAsync(
            live.Select(connection => connection.Context.User ?? new ClaimsPrincipal()).ToList(),
            cancellationToken);
        // One read of the business access per person and business, whatever the number of their connections.
        var accesses = new Dictionary<(string?, string?), HubTenantAccess?>();

        var closed = 0;
        for (var i = 0; i < live.Count; i++)
        {
            var connection = live[i];
            var allowed = open[i];
            if (allowed)
            {
                var user = connection.Context.User;
                var key = (user?.FindFirst(ClaimTypes.NameIdentifier)?.Value, user?.FindFirst("tenant_id")?.Value);
                if (!accesses.TryGetValue(key, out var access))
                    accesses[key] = access = await HubTenantAccess.ResolveAsync(user, tenantAccess, cancellationToken);
                allowed = access is not null && connection.Allowed(access);
            }

            if (allowed)
                continue;

            connection.Context.Abort();
            _connections.Remove(connection.Context.ConnectionId);
            closed++;
        }

        if (closed > 0)
            _logger.LogInformation("Closed {Count} live connections whose session or access ended.", closed);
        return closed;
    }
}
