using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.BackgroundJobs;

/// <summary>
/// Sends the codes of <see cref="VerificationCodeQueue"/> through <see cref="ISmsService"/>, a few at a time so a slow
/// provider does not hold the rest. Runs in every instance, whether or not background jobs are on: each one sends the
/// codes its own requests queued.
/// </summary>
public sealed class VerificationCodeSender : BackgroundService
{
    internal const int MaxConcurrentSends = 4;

    private readonly VerificationCodeQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VerificationCodeSender> _logger;

    public VerificationCodeSender(
        VerificationCodeQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<VerificationCodeSender> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Parallel.ForEachAsync(
                _queue.Reader.ReadAllAsync(stoppingToken),
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentSends, CancellationToken = stoppingToken },
                async (item, _) => await SendAsync(item));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    internal async Task SendAsync(VerificationCode item)
    {
        try
        {
            // No business: the codes belong to accounts, and the SMS service reads only the platform's settings.
            await using var scope = _scopeFactory.CreateSystemScope(nameof(VerificationCodeSender));
            var sms = scope.ServiceProvider.GetRequiredService<ISmsService>();
            await sms.SendVerificationCodeAsync(item.PhoneNumber, item.Code);
        }
        catch (Exception ex)
        {
            // Without the phone or the code. The person asks for another code once the cooldown ends.
            _logger.LogError(LogRedaction.RedactDatabaseMessages(ex), "A verification code could not be sent.");
        }
    }
}
