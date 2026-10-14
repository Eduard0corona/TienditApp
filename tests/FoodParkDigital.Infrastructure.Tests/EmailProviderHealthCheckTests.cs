using FoodParkDigital.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The email provider is Healthy only with settings EmailService would send with: none, incomplete ones or the example
/// values of appsettings.json leave it Degraded, and the reason never carries the settings' values.
/// </summary>
public class EmailProviderHealthCheckTests
{
    private static readonly Dictionary<string, string?> Complete = new()
    {
        ["EmailSettings:Provider"] = "Smtp",
        ["EmailSettings:FromEmail"] = "hola@tienditapp.mx",
        ["EmailSettings:Smtp:Host"] = "smtp.proveedor.com",
        ["EmailSettings:Smtp:Port"] = "587",
        ["EmailSettings:Smtp:Username"] = "cuenta-real",
        ["EmailSettings:Smtp:Password"] = "secreto-real"
    };

    [Fact]
    public async Task CompleteSettings_ShouldBeHealthy()
    {
        var result = await CheckAsync(Complete);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Theory]
    [InlineData("EmailSettings:Provider", "")]
    [InlineData("EmailSettings:Provider", "SendGrid")]
    [InlineData("EmailSettings:Smtp:Host", "")]
    [InlineData("EmailSettings:Smtp:Port", "puerto")]
    [InlineData("EmailSettings:FromEmail", "")]
    [InlineData("EmailSettings:FromEmail", "noreply@foodparkdigital.local")]
    [InlineData("EmailSettings:Smtp:Username", "YOUR_SMTP_USERNAME")]
    [InlineData("EmailSettings:Smtp:Password", "YOUR_SMTP_PASSWORD")]
    public async Task MissingOrExampleSettings_ShouldBeDegraded_WithoutTheirValues(string setting, string value)
    {
        var result = await CheckAsync(new Dictionary<string, string?>(Complete) { [setting] = value });

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.DoesNotContain("secreto-real", result.Description);
        Assert.DoesNotContain("cuenta-real", result.Description);
    }

    [Fact]
    public async Task ARelayWithoutALogin_ShouldBeHealthy_EvenWithTheExamplePasswordLeftInAppSettings()
    {
        // No user: no credentials are sent, so the password left from appsettings.json does not matter.
        var result = await CheckAsync(new Dictionary<string, string?>(Complete)
        {
            ["EmailSettings:Smtp:Username"] = "",
            ["EmailSettings:Smtp:Password"] = "YOUR_SMTP_PASSWORD"
        });

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task TheExampleSettingsOfAppSettings_ShouldBeDegraded()
    {
        // The values src/FoodParkDigital.WebApi/appsettings.json carries.
        var result = await CheckAsync(new Dictionary<string, string?>
        {
            ["EmailSettings:Provider"] = "Smtp",
            ["EmailSettings:FromEmail"] = "noreply@foodparkdigital.local",
            ["EmailSettings:Smtp:Host"] = "smtp.mailtrap.io",
            ["EmailSettings:Smtp:Port"] = "587",
            ["EmailSettings:Smtp:Username"] = "YOUR_SMTP_USERNAME",
            ["EmailSettings:Smtp:Password"] = "YOUR_SMTP_PASSWORD"
        });

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    private static Task<HealthCheckResult> CheckAsync(Dictionary<string, string?> settings)
        => new EmailProviderHealthCheck(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .CheckHealthAsync(new HealthCheckContext());
}
