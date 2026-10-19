namespace FoodParkDigital.Application.OnlinePayments;

/// <summary>
/// Mercado Pago, as the use cases of paying online need it (Fase 24I, ADR 0031): connecting a business's own account
/// (OAuth with PKCE), opening a checkout for an order, reading its payments and giving money back. Every call but the
/// connection is made with the business's key, so the money always goes to and from the business's account. Mercado Pago
/// not answering, or answering an error of its own, throws <see cref="OnlinePaymentGatewayUnavailableException"/>: the
/// callers retry later.
/// </summary>
public interface IOnlinePaymentGateway
{
    /// <summary>
    /// Whether the platform configured its Mercado Pago application, the key that encrypts the businesses' keys and the
    /// app's public address. Without it nothing about paying online is offered.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>Whether it works with Mercado Pago's test accounts: their payments are not real money.</summary>
    bool Sandbox { get; }

    /// <summary>Where the owner authorizes TienditApp in Mercado Pago, and comes back with the code.</summary>
    Uri AuthorizationUrl(Guid state, string codeChallenge);

    /// <summary>Exchanges the code Mercado Pago sent back for the business's keys.</summary>
    /// <exception cref="OnlinePaymentGatewayRefusedException">The code expired, was used or does not match.</exception>
    Task<OnlinePaymentCredentials> ConnectAsync(string code, string codeVerifier, CancellationToken cancellationToken);

    /// <summary>New keys before the current ones expire.</summary>
    /// <exception cref="OnlinePaymentGatewayRefusedException">The business revoked the access in Mercado Pago.</exception>
    Task<OnlinePaymentCredentials> RenewAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Mercado Pago's checkout page for an order, in the business's account.</summary>
    Task<OnlinePaymentCheckoutPage> CreateCheckoutAsync(
        string accessToken, OnlinePaymentCheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>A payment of the business's account; null when there is no such payment.</summary>
    Task<OnlinePaymentDetails?> GetPaymentAsync(string accessToken, string paymentId, CancellationToken cancellationToken);

    /// <summary>The payments of a checkout (its external reference), newest first.</summary>
    Task<IReadOnlyList<OnlinePaymentDetails>> FindPaymentsAsync(
        string accessToken, Guid checkoutId, CancellationToken cancellationToken);

    /// <summary>
    /// Gives back <paramref name="amount"/> of a payment. The same <paramref name="idempotencyKey"/> never gives it back
    /// twice, so it is safe to retry.
    /// </summary>
    Task<OnlinePaymentRefundOutcome> RefundAsync(
        string accessToken, string paymentId, decimal amount, Guid idempotencyKey, CancellationToken cancellationToken);
}

/// <summary>The keys of a business's account, as Mercado Pago gave them (not encrypted yet).</summary>
public sealed record OnlinePaymentCredentials(
    long ProviderUserId,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    bool LiveMode);

/// <param name="CheckoutId">Its external reference: the payments are found and verified by it.</param>
/// <param name="Title">What the customer reads in Mercado Pago: «Pedido en Café Luna».</param>
/// <param name="ReturnUrl">Where the customer comes back, paid or not.</param>
/// <param name="StatementDescriptor">What the card statement says, when the card network takes it.</param>
public sealed record OnlinePaymentCheckoutRequest(
    Guid CheckoutId,
    string Title,
    decimal Amount,
    string ReturnUrl,
    DateTimeOffset ExpiresAt,
    string? StatementDescriptor);

public sealed record OnlinePaymentCheckoutPage(string PreferenceId, string CheckoutUrl);

/// <summary>What Mercado Pago says of a payment; only an approved one in the right amount places an order.</summary>
public sealed record OnlinePaymentDetails(
    string Id,
    string Status,
    string? StatusDetail,
    string? ExternalReference,
    decimal Amount,
    string? Currency,
    DateTimeOffset? ApprovedAt,
    long? CollectorId,
    bool LiveMode)
{
    public const string ApprovedStatus = "approved";

    public bool IsApproved => Status == ApprovedStatus && ApprovedAt is not null;
}

/// <summary>Mercado Pago gave the money back, or refused to (it answered, so retrying will not change it).</summary>
public abstract record OnlinePaymentRefundOutcome
{
    public sealed record Done(string? ProviderRefundId) : OnlinePaymentRefundOutcome;

    public sealed record Refused(string Message) : OnlinePaymentRefundOutcome;
}

/// <summary>Mercado Pago did not answer, or failed on its side: try again later.</summary>
public sealed class OnlinePaymentGatewayUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>Mercado Pago answered no (an expired code, a revoked access): retrying will not change it.</summary>
public sealed class OnlinePaymentGatewayRefusedException(string message) : Exception(message);
