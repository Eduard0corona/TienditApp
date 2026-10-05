using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Identity.Services;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.BackgroundJobs;

/// <summary>
/// Hashes, once after the deployment, the refresh tokens the previous version stored as plain values (and the plain
/// values of their replacements): their sessions keep working and a copy of the table no longer opens any. A token
/// presented before this gets to it is hashed when it is used.
/// </summary>
public sealed class LegacyRefreshTokenHashingService : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(20);
    private const int BatchSize = 500;
    private const int MaxConflicts = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LegacyRefreshTokenHashingService> _logger;

    public LegacyRefreshTokenHashingService(
        IServiceScopeFactory scopeFactory,
        ILogger<LegacyRefreshTokenHashingService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // The first requests after a restart come before it.
            await Task.Delay(StartDelay, stoppingToken);

            var hashed = 0;
            var conflicts = 0;
            while (true)
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                int batch;
                try
                {
                    batch = await UserSessions.HashLegacyTokensAsync(context, BatchSize, stoppingToken);
                }
                catch (DbUpdateConcurrencyException) when (++conflicts < MaxConflicts)
                {
                    // A renewal changed one of them meanwhile: the next pass reads them again.
                    continue;
                }

                hashed += batch;
                if (batch < BatchSize)
                    break;
            }

            if (hashed > 0)
                _logger.LogInformation("Hashed {Count} refresh tokens stored as plain values.", hashed);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(
                LogRedaction.RedactDatabaseMessages(ex),
                "Refresh tokens stored as plain values could not be hashed; they are hashed when used and on the next start.");
        }
    }
}
