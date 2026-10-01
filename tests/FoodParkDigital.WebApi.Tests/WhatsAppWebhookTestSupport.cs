using System.Security.Cryptography;
using System.Text;
using FoodParkDigital.WebApi.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.WebApi.Tests;

internal static class WhatsAppWebhookTestSupport
{
    public const string AppSecret = "test-app-secret";

    /// <summary>Builds the X-Hub-Signature-256 value Meta sends: HMAC-SHA256 of the raw body.</summary>
    public static string Sign(byte[] body, string secret)
        => "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

    public static WhatsAppWebhookSignatureVerifier CreateVerifier(
        string environment, string? appSecret, string? allowUnsignedWebhooks = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [WhatsAppWebhookSignatureVerifier.AppSecretSetting] = appSecret,
                [WhatsAppWebhookSignatureVerifier.AllowUnsignedWebhooksSetting] = allowUnsignedWebhooks
            })
            .Build();

        return new WhatsAppWebhookSignatureVerifier(
            configuration,
            new TestHostEnvironment(environment),
            NullLogger<WhatsAppWebhookSignatureVerifier>.Instance);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "FoodParkDigital.WebApi.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
