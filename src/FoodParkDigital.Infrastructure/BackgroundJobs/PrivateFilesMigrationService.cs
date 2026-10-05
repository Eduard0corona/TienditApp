using FoodParkDigital.Application.Orders;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.BackgroundJobs;

/// <summary>
/// Once after the app starts, moves the transfer receipts and service photos uploaded before they became private out
/// of the public storage. Until a file moves the API still opens it where it was, and the static path of local
/// uploads is already closed.
/// </summary>
public sealed class PrivateFilesMigrationService : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(15);

    private readonly ILegacyPrivateFileMover _mover;
    private readonly ILogger<PrivateFilesMigrationService> _logger;

    public PrivateFilesMigrationService(ILegacyPrivateFileMover mover, ILogger<PrivateFilesMigrationService> logger)
    {
        _mover = mover;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // The first requests after a restart come before the move.
            await Task.Delay(StartDelay, stoppingToken);
            var moved = await _mover.MoveLegacyFilesAsync(
                key => OrderUploadRules.TryParsePrivateKey(key, out _, out _),
                stoppingToken);
            if (moved > 0)
                _logger.LogInformation("Moved {Count} private files out of the public storage.", moved);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Private files could not be moved out of the public storage; the API still opens them where they are.");
        }
    }
}
