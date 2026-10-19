using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.OnlinePayments;

namespace FoodParkDigital.Application.Tests.Support;

/// <summary>
/// Mercado Pago as the tests need it (Fase 24I): the payments it "has" are the ones a test adds, every call is recorded,
/// and it can be made to stop answering. <see cref="Off"/> is a platform without Mercado Pago configured.
/// </summary>
internal sealed class FakeOnlinePaymentGateway : IOnlinePaymentGateway
{
    public const string AccessToken = "APP_USR-access";
    public const string RefreshToken = "TG-refresh";
    public const long AccountId = 987654321;

    public static FakeOnlinePaymentGateway Off { get; } = new() { IsConfigured = false };

    public bool IsConfigured { get; set; } = true;
    public bool Sandbox { get; set; }
    public bool Unavailable { get; set; }
    public bool RefuseCode { get; set; }
    public OnlinePaymentRefundOutcome RefundOutcome { get; set; } = new OnlinePaymentRefundOutcome.Done("refund-1");
    public OnlinePaymentCredentials? Credentials { get; set; }

    /// <summary>The payments of the business's account, by id.</summary>
    public Dictionary<string, OnlinePaymentDetails> Payments { get; } = new();

    public List<OnlinePaymentCheckoutRequest> Checkouts { get; } = [];
    public List<(string PaymentId, decimal Amount, Guid Key)> Refunds { get; } = [];
    public List<string> AccessTokensUsed { get; } = [];
    public List<(string Code, string Verifier)> Connections { get; } = [];
    public int Renewals { get; private set; }

    public Uri AuthorizationUrl(Guid state, string codeChallenge)
        => new($"https://auth.mercadopago.test/authorization?state={state:N}&code_challenge={codeChallenge}");

    public Task<OnlinePaymentCredentials> ConnectAsync(string code, string codeVerifier, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        Connections.Add((code, codeVerifier));
        if (RefuseCode)
            throw new OnlinePaymentGatewayRefusedException("Mercado Pago no aceptó la autorización. Vuelve a conectar tu cuenta.");
        return Task.FromResult(Credentials ?? new OnlinePaymentCredentials(
            AccountId, AccessToken, RefreshToken, DateTimeOffset.UtcNow.AddDays(180), LiveMode: !Sandbox));
    }

    public Task<OnlinePaymentCredentials> RenewAsync(string refreshToken, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        Renewals++;
        return Task.FromResult(new OnlinePaymentCredentials(
            AccountId, $"{AccessToken}-{Renewals}", $"{RefreshToken}-{Renewals}", DateTimeOffset.UtcNow.AddDays(400), !Sandbox));
    }

    public Task<OnlinePaymentCheckoutPage> CreateCheckoutAsync(
        string accessToken, OnlinePaymentCheckoutRequest request, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        AccessTokensUsed.Add(accessToken);
        Checkouts.Add(request);
        return Task.FromResult(new OnlinePaymentCheckoutPage(
            $"pref-{request.CheckoutId:N}", $"https://www.mercadopago.test/checkout?pref_id={request.CheckoutId:N}"));
    }

    public Task<OnlinePaymentDetails?> GetPaymentAsync(string accessToken, string paymentId, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        AccessTokensUsed.Add(accessToken);
        return Task.FromResult(Payments.GetValueOrDefault(paymentId));
    }

    public Task<IReadOnlyList<OnlinePaymentDetails>> FindPaymentsAsync(
        string accessToken, Guid checkoutId, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        AccessTokensUsed.Add(accessToken);
        IReadOnlyList<OnlinePaymentDetails> found = Payments.Values
            .Where(p => p.ExternalReference == checkoutId.ToString())
            .OrderByDescending(p => p.ApprovedAt)
            .ToList();
        return Task.FromResult(found);
    }

    public Task<OnlinePaymentRefundOutcome> RefundAsync(
        string accessToken, string paymentId, decimal amount, Guid idempotencyKey, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        AccessTokensUsed.Add(accessToken);
        Refunds.Add((paymentId, amount, idempotencyKey));
        return Task.FromResult(RefundOutcome);
    }

    /// <summary>An approved payment of the checkout into the business's account, as Mercado Pago would report it.</summary>
    public OnlinePaymentDetails Approve(
        Guid checkoutId, decimal amount, DateTimeOffset approvedAt, string? id = null, long collector = AccountId, string currency = "MXN")
    {
        var payment = new OnlinePaymentDetails(
            id ?? (1_000_000 + Payments.Count).ToString(), OnlinePaymentDetails.ApprovedStatus, "accredited",
            checkoutId.ToString(), amount, currency, approvedAt, collector, LiveMode: !Sandbox);
        Payments[payment.Id] = payment;
        return payment;
    }

    private void ThrowIfUnavailable()
    {
        if (Unavailable)
            throw new OnlinePaymentGatewayUnavailableException("Mercado Pago no respondió. Intenta de nuevo en unos minutos.");
    }
}

/// <summary>The platform's encryption, reversible in tests: the purpose is part of the value, as in AES-GCM.</summary>
internal sealed class FakeSecretProtector : ISecretProtector
{
    public bool IsConfigured { get; set; } = true;

    public string Protect(string plaintext, string purpose) => $"protected:{purpose}:{plaintext}";

    public string Unprotect(string protectedText, string purpose)
    {
        var prefix = $"protected:{purpose}:";
        if (!protectedText.StartsWith(prefix, StringComparison.Ordinal))
            throw new System.Security.Cryptography.CryptographicException("Not for this purpose.");
        return protectedText[prefix.Length..];
    }
}
