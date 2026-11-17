using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Assistants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.BackgroundJobs.AssistantAuthorization;

/// <summary>
/// Once a day, deletes what the assistants' authorization (Fase 32E, ADR 0097) no longer needs, in batches: the tokens and
/// codes expired a week ago, the connections disconnected 30 days ago (with their tokens), the clients registered a day ago
/// that no account connected, and those whose last connection was a year ago and have none left. A bot registering clients
/// leaves nothing behind after a day.
/// </summary>
public sealed class AssistantAuthorizationCleanupService : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    public const int BatchSize = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AssistantAuthorizationCleanupService> _logger;

    public AssistantAuthorizationCleanupService(
        IServiceScopeFactory scopeFactory, IDateTimeProvider clock, ILogger<AssistantAuthorizationCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await DeleteBatchAsync(stoppingToken) > 0)
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Typically the database being unreachable: the next pass deletes them.
                _logger.LogError(Services.LogRedaction.RedactDatabaseMessages(ex), "Cleanup of assistants' authorization failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// One batch of each, in the order their keys ask (tokens and codes before the connections and clients they point to);
    /// how many it deleted.
    /// </summary>
    public async Task<int> DeleteBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var now = _clock.UtcNow;
        var expired = now - AssistantToken.KeptAfterExpiry;
        var deleted = 0;

        var tokens = await context.AssistantTokens
            .Where(token => token.ExpiresAt < expired)
            .OrderBy(token => token.ExpiresAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        context.AssistantTokens.RemoveRange(tokens);
        var codes = await context.AssistantAuthorizationCodes
            .Where(code => code.ExpiresAt < expired)
            .OrderBy(code => code.ExpiresAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        context.AssistantAuthorizationCodes.RemoveRange(codes);
        deleted += tokens.Count + codes.Count;
        await context.SaveChangesAsync(cancellationToken);

        // A code of theirs is gone by now: it expired five minutes after it was given, before they were connected.
        var disconnected = now - AssistantConnection.KeptAfterRevoked;
        var connections = await context.AssistantConnections
            .Where(connection => connection.RevokedAt < disconnected
                && !context.AssistantAuthorizationCodes.Any(code => code.ConnectionId == connection.Id))
            .OrderBy(connection => connection.RevokedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        context.AssistantConnections.RemoveRange(connections);
        deleted += connections.Count;
        await context.SaveChangesAsync(cancellationToken);

        // Those no account connected in a day, and those connected long ago with no connection left: an assistant that still
        // holds the client_id of one it connected is not sent to register again for a year. Never one with a code still to
        // exchange: the customer just allowed it.
        var unconnected = now - AssistantClient.TimeToConnect;
        var forgotten = now - AssistantClient.KeptAfterLastConnection;
        var clients = await context.AssistantClients
            .Where(client => ((client.LastConnectedAt == null && client.RegisteredAt < unconnected) || client.LastConnectedAt < forgotten)
                && !context.AssistantConnections.Any(connection => connection.ClientId == client.Id)
                && !context.AssistantAuthorizationCodes.Any(code => code.ClientId == client.Id && code.ExpiresAt > now))
            .OrderBy(client => client.RegisteredAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        context.AssistantClients.RemoveRange(clients);
        deleted += clients.Count;
        await context.SaveChangesAsync(cancellationToken);

        return deleted;
    }
}
