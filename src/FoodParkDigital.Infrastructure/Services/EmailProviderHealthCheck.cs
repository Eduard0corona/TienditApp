using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FoodParkDigital.Infrastructure.Services;

/// <summary>
/// Degraded (still serving traffic) while no email provider is configured, or only the example values of
/// <c>appsettings.json</c>: nobody who signs up can activate their account, and recovering the password or inviting
/// the team by email sends nothing. It reads the settings the way <see cref="EmailService"/> does
/// (<see cref="EmailDeliverySettings"/>); it does not connect to the provider, so wrong credentials show in the log of
/// each send, not here. The deployment's smoke test warns when it is not Healthy, without failing.
/// </summary>
public sealed class EmailProviderHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;

    public EmailProviderHealthCheck(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var (settings, problem) = EmailDeliverySettings.Read(_configuration);
        // The reason goes to the log only: the endpoint answers with the status.
        return Task.FromResult(settings is not null
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded($"Account emails cannot be sent: {problem}"));
    }
}
