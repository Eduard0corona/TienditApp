using FoodParkDigital.Application.OnlinePayments;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Mercado Pago for the API in process (Fase 24I): it gives each connection the account the test names, keeps the payments
/// a test approves, and answers each one only to the key of the account that collected it, as Mercado Pago does.
/// </summary>
public sealed class TestOnlinePaymentGateway : IOnlinePaymentGateway
{
    public const string WebhookSecret = "mp-webhook-secret-for-the-api-tests";

    private readonly object _gate = new();
    private readonly Dictionary<string, OnlinePaymentDetails> _payments = new();
    private long _nextAccount = 900_000_001;
    private long _nextPayment = 5_000_000_001;

    public bool IsConfigured => true;
    public bool Sandbox => false;

    /// <summary>The account the next connection gets; a new one each time unless a test sets it.</summary>
    public long? NextAccountId { get; set; }

    public Uri AuthorizationUrl(Guid state, string codeChallenge)
        => new($"https://auth.mercadopago.test/authorization?state={state:N}&code_challenge={codeChallenge}");

    public Task<OnlinePaymentCredentials> ConnectAsync(string code, string codeVerifier, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var account = NextAccountId ?? _nextAccount++;
            NextAccountId = null;
            return Task.FromResult(new OnlinePaymentCredentials(
                account, Key(account), $"TG-refresh-{account}", DateTimeOffset.UtcNow.AddDays(180), LiveMode: true));
        }
    }

    public Task<OnlinePaymentCredentials> RenewAsync(string refreshToken, CancellationToken cancellationToken)
        => throw new OnlinePaymentGatewayRefusedException("No renewals in these tests.");

    public Task<OnlinePaymentCheckoutPage> CreateCheckoutAsync(
        string accessToken, OnlinePaymentCheckoutRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new OnlinePaymentCheckoutPage(
            $"pref-{request.CheckoutId:N}", $"https://www.mercadopago.test/checkout?pref_id={request.CheckoutId:N}"));

    public Task<OnlinePaymentDetails?> GetPaymentAsync(string accessToken, string paymentId, CancellationToken cancellationToken)
    {
        lock (_gate)
            return Task.FromResult(_payments.TryGetValue(paymentId, out var payment) && Key(payment.CollectorId!.Value) == accessToken
                ? payment
                : null);
    }

    public Task<IReadOnlyList<OnlinePaymentDetails>> FindPaymentsAsync(
        string accessToken, Guid checkoutId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<OnlinePaymentDetails> found = _payments.Values
                .Where(p => p.ExternalReference == checkoutId.ToString() && Key(p.CollectorId!.Value) == accessToken)
                .ToList();
            return Task.FromResult(found);
        }
    }

    public Task<OnlinePaymentRefundOutcome> RefundAsync(
        string accessToken, string paymentId, decimal amount, Guid idempotencyKey, CancellationToken cancellationToken)
        => Task.FromResult<OnlinePaymentRefundOutcome>(new OnlinePaymentRefundOutcome.Done($"refund-{idempotencyKey:N}"));

    /// <summary>A payment approved now for a checkout, collected by <paramref name="account"/>.</summary>
    public OnlinePaymentDetails Approve(Guid checkoutId, decimal amount, long account)
    {
        lock (_gate)
        {
            var payment = new OnlinePaymentDetails(
                (_nextPayment++).ToString(), OnlinePaymentDetails.ApprovedStatus, "accredited", checkoutId.ToString(),
                amount, "MXN", DateTimeOffset.UtcNow, account, LiveMode: true);
            _payments[payment.Id] = payment;
            return payment;
        }
    }

    private static string Key(long account) => $"APP_USR-{account}";
}
