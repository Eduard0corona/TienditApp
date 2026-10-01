using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace FoodParkDigital.WebApi.Services;

public enum WhatsAppWebhookSignatureStatus
{
    Valid,
    VerificationDisabled,
    SecretNotConfigured,
    MissingSignature,
    InvalidSignature
}

/// <summary>
/// Verifies the X-Hub-Signature-256 header Meta sends with every webhook delivery:
/// an HMAC-SHA256 of the raw request body keyed with the Meta app secret.
/// Fails closed: without a configured secret every delivery is rejected, unless
/// WhatsApp:AllowUnsignedWebhooks is enabled in the Development environment.
/// </summary>
public class WhatsAppWebhookSignatureVerifier
{
    public const string SignatureHeaderName = "X-Hub-Signature-256";
    public const string AppSecretSetting = "WhatsApp:AppSecret";
    public const string AllowUnsignedWebhooksSetting = "WhatsApp:AllowUnsignedWebhooks";

    private const string SignaturePrefix = "sha256=";

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<WhatsAppWebhookSignatureVerifier> _logger;

    public WhatsAppWebhookSignatureVerifier(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<WhatsAppWebhookSignatureVerifier> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public WhatsAppWebhookSignatureStatus Verify(ReadOnlySpan<byte> body, StringValues signatureHeader)
    {
        if (IsVerificationDisabled())
        {
            _logger.LogWarning(
                "WhatsApp webhook signature verification is disabled by {Setting}; this is only honored in Development",
                AllowUnsignedWebhooksSetting);
            return WhatsAppWebhookSignatureStatus.VerificationDisabled;
        }

        var appSecret = _configuration[AppSecretSetting];
        if (string.IsNullOrWhiteSpace(appSecret))
        {
            _logger.LogError(
                "WhatsApp webhook rejected: {Setting} is not configured, so deliveries cannot be authenticated. Configure the Meta app secret for this environment",
                AppSecretSetting);
            return WhatsAppWebhookSignatureStatus.SecretNotConfigured;
        }

        if (StringValues.IsNullOrEmpty(signatureHeader))
        {
            _logger.LogWarning("WhatsApp webhook rejected: {Header} header is missing", SignatureHeaderName);
            return WhatsAppWebhookSignatureStatus.MissingSignature;
        }

        var signature = signatureHeader.Count == 1 ? signatureHeader[0]! : string.Empty;
        if (!signature.StartsWith(SignaturePrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("WhatsApp webhook rejected: {Header} header is malformed", SignatureHeaderName);
            return WhatsAppWebhookSignatureStatus.InvalidSignature;
        }

        var receivedHex = signature[SignaturePrefix.Length..].ToLowerInvariant();
        var computedHex = Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body));

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(receivedHex),
                Encoding.UTF8.GetBytes(computedHex)))
        {
            _logger.LogWarning("WhatsApp webhook rejected: {Header} does not match the request body", SignatureHeaderName);
            return WhatsAppWebhookSignatureStatus.InvalidSignature;
        }

        return WhatsAppWebhookSignatureStatus.Valid;
    }

    private bool IsVerificationDisabled()
    {
        if (!bool.TryParse(_configuration[AllowUnsignedWebhooksSetting], out var allowUnsigned) || !allowUnsigned)
            return false;

        if (_environment.IsDevelopment())
            return true;

        _logger.LogError(
            "{Setting} is ignored in the {Environment} environment; webhook signatures are still verified",
            AllowUnsignedWebhooksSetting,
            _environment.EnvironmentName);
        return false;
    }
}
