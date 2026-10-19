using System.Security.Cryptography;
using System.Text;
using FoodParkDigital.Infrastructure.OnlinePayments;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace FoodParkDigital.WebApi.Services;

public enum MercadoPagoWebhookSignatureStatus
{
    Valid,
    SecretNotConfigured,
    MissingSignature,
    InvalidSignature
}

/// <summary>
/// Verifies the <c>x-signature</c> header Mercado Pago sends with every notification of TienditApp's application
/// (Fase 24I, ADR 0031): <c>ts=…,v1=…</c>, where <c>v1</c> is the HMAC-SHA256 (hex) of
/// <c>id:{data.id};request-id:{x-request-id};ts:{ts};</c> keyed with <c>MercadoPago:WebhookSecret</c>. The signature
/// covers the notified id, not the body, so nothing but that id is trusted: the payment itself is always read from
/// Mercado Pago. Fails closed: without the secret every notification is rejected.
/// </summary>
public sealed class MercadoPagoWebhookSignatureVerifier
{
    public const string SignatureHeaderName = "x-signature";
    public const string RequestIdHeaderName = "x-request-id";

    private readonly IOptions<MercadoPagoOptions> _options;
    private readonly ILogger<MercadoPagoWebhookSignatureVerifier> _logger;

    public MercadoPagoWebhookSignatureVerifier(
        IOptions<MercadoPagoOptions> options,
        ILogger<MercadoPagoWebhookSignatureVerifier> logger)
    {
        _options = options;
        _logger = logger;
    }

    public MercadoPagoWebhookSignatureStatus Verify(string? dataId, StringValues signatureHeader, StringValues requestIdHeader)
    {
        var secret = _options.Value.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogError(
                "Mercado Pago webhook rejected: {Setting} is not configured, so notifications cannot be authenticated",
                $"{MercadoPagoOptions.SectionName}:{nameof(MercadoPagoOptions.WebhookSecret)}");
            return MercadoPagoWebhookSignatureStatus.SecretNotConfigured;
        }

        if (StringValues.IsNullOrEmpty(signatureHeader))
        {
            _logger.LogWarning("Mercado Pago webhook rejected: {Header} header is missing", SignatureHeaderName);
            return MercadoPagoWebhookSignatureStatus.MissingSignature;
        }

        string? ts = null, v1 = null;
        if (signatureHeader.Count == 1)
        {
            foreach (var part in signatureHeader[0]!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = part.IndexOf('=');
                if (separator <= 0)
                    continue;
                var key = part[..separator].Trim();
                var value = part[(separator + 1)..].Trim();
                if (key == "ts")
                    ts = value;
                else if (key == "v1")
                    v1 = value;
            }
        }

        if (string.IsNullOrEmpty(ts) || string.IsNullOrEmpty(v1))
        {
            _logger.LogWarning("Mercado Pago webhook rejected: {Header} header is malformed", SignatureHeaderName);
            return MercadoPagoWebhookSignatureStatus.InvalidSignature;
        }

        var computed = Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(Manifest(dataId, requestIdHeader, ts))));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(v1.ToLowerInvariant()),
                Encoding.UTF8.GetBytes(computed)))
        {
            _logger.LogWarning("Mercado Pago webhook rejected: {Header} does not match the notification", SignatureHeaderName);
            return MercadoPagoWebhookSignatureStatus.InvalidSignature;
        }

        return MercadoPagoWebhookSignatureStatus.Valid;
    }

    /// <summary>
    /// What Mercado Pago signs. A part the notification does not carry is left out of the template, and an alphanumeric
    /// id is signed in lowercase.
    /// </summary>
    internal static string Manifest(string? dataId, StringValues requestIdHeader, string ts)
    {
        var manifest = new StringBuilder();
        if (!string.IsNullOrEmpty(dataId))
            manifest.Append("id:").Append(dataId.ToLowerInvariant()).Append(';');
        if (requestIdHeader.Count == 1 && !string.IsNullOrEmpty(requestIdHeader[0]))
            manifest.Append("request-id:").Append(requestIdHeader[0]).Append(';');
        manifest.Append("ts:").Append(ts).Append(';');
        return manifest.ToString();
    }
}
