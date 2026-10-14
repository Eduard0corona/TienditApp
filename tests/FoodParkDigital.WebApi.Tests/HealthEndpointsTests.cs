using System.Net;
using System.Text.Json;
using FoodParkDigital.WebApi.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Every health endpoint names the commit the API was built from, whatever its status: the deployment's smoke test
/// compares it with the commit it published, so an instance of the previous version does not pass for this one when
/// the client did not change.
/// </summary>
public sealed class HealthEndpointsTests : IClassFixture<HealthEndpointsTests.ApiWithoutDatabase>
{
    private readonly ApiWithoutDatabase _api;

    public HealthEndpointsTests(ApiWithoutDatabase api)
    {
        _api = api;
    }

    [Theory]
    [InlineData("/health/live", HttpStatusCode.OK, "Healthy")]
    // Without a database: the smoke test still learns which version answered.
    [InlineData("/health/ready", HttpStatusCode.ServiceUnavailable, "Unhealthy")]
    [InlineData("/health/schema", HttpStatusCode.ServiceUnavailable, "Unhealthy")]
    public async Task EachHealthEndpoint_ShouldNameTheCommitOfThisBuild(string path, HttpStatusCode status, string health)
    {
        using var response = await _api.Http.GetAsync(path);

        Assert.Equal(status, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(health, body.RootElement.GetProperty("status").GetString());
        Assert.Equal(BuildInfo.Commit, body.RootElement.GetProperty("commit").GetString());
    }

    [Fact]
    public async Task Readiness_ShouldSayTheBusinessTimeZonesResolve_AsTheSmokeTestReadsIt()
    {
        using var response = await _api.Http.GetAsync("/health/ready");
        var text = await response.Content.ReadAsStringAsync();

        // The zones do not need the database: the entry is Healthy while readiness fails without one.
        using var body = JsonDocument.Parse(text);
        Assert.Equal("Healthy", body.RootElement.GetProperty("checks").GetProperty("business-time-zones").GetString());
        // The exact text .github/scripts/smoke-test.sh looks for.
        Assert.Contains("\"business-time-zones\":\"Healthy\"", text);

        // A zone that falls back to UTC, or the check failing, leaves readiness Degraded (200), never Unhealthy.
        var registration = _api.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Single(check => check.Name == "business-time-zones");
        Assert.Equal(HealthStatus.Degraded, registration.FailureStatus);
    }

    [Fact]
    public async Task Readiness_ShouldSayWhetherAccountEmailsCanBeSent_WithoutFailingForIt()
    {
        using var response = await _api.Http.GetAsync("/health/ready");
        var text = await response.Content.ReadAsStringAsync();

        // appsettings.json carries example SMTP values: they count as no provider. The exact text the smoke test reads.
        Assert.Contains("\"email-provider\":\"Degraded\"", text);
        var registration = _api.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Single(check => check.Name == "email-provider");
        Assert.Equal(HealthStatus.Degraded, registration.FailureStatus);
    }

    [Theory]
    [InlineData("1.0.0+b16e105d33d19afed856bea34b11db7e6b1a489c", "b16e105d33d19afed856bea34b11db7e6b1a489c")]
    [InlineData("1.0.0", null)]
    [InlineData("1.0.0+", null)]
    [InlineData(null, null)]
    public void TheCommit_ShouldBeWhatTheBuildAppendedToTheVersion(string? informationalVersion, string? commit)
    {
        Assert.Equal(commit, BuildInfo.CommitOf(informationalVersion));
    }

    /// <summary>The API with a database that does not answer: readiness and the schema check fail at once.</summary>
    public sealed class ApiWithoutDatabase : IDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ApiWithoutDatabase()
        {
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.UseEnvironment("Development");
                host.UseSetting("ConnectionStrings:DefaultConnection", "Server=127.0.0.1,1;Database=SinBase;Encrypt=False");
                host.UseSetting("Database:AutoMigrateOnStartup", "false");
                host.UseSetting("Database:SeedOnStartup", "false");
                host.UseSetting("BackgroundJobs:Enabled", "false");
            });
            Http = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        }

        public HttpClient Http { get; }

        public IServiceProvider Services => _factory.Services;

        public void Dispose()
        {
            Http.Dispose();
            _factory.Dispose();
        }
    }
}
