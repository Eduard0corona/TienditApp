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
    /// <exception cref="OnlinePaymentAccountRejectedException">Mercado Pago no longer takes the account's key.</exception>
    Task<OnlinePaymentCheckoutPage> CreateCheckoutAsync(
        string accessToken, OnlinePaymentCheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>A payment of the business's account; null when there is no such payment.</summary>
    /// <exception cref="OnlinePaymentAccountRejectedException">Mercado Pago no longer takes the account's key.</exception>
    Task<OnlinePaymentDetails?> GetPaymentAsync(string accessToken, string paymentId, CancellationToken cancellationToken);

    /// <summary>The payments of a checkout (its external reference), newest first.</summary>
    /// <exception cref="OnlinePaymentAccountRejectedException">Mercado Pago no longer takes the account's key.</exception>
    Task<IReadOnlyList<OnlinePaymentDetails>> FindPaymentsAsync(
        string accessToken, Guid checkoutId, CancellationToken cancellationToken);

    /// <summary>
    /// The payments of the business's account whose last change in Mercado Pago falls between <paramref name="from"/> and
    /// <paramref name="to"/>, the oldest change first (Fase 24K): a refund or a dispute changes a payment. Every payment of
    /// the account comes, also the ones TienditApp did not charge; at most <paramref name="limit"/>, from
    /// <paramref name="offset"/>.
    /// </summary>
    /// <exception cref="OnlinePaymentAccountRejectedException">Mercado Pago no longer takes the account's key.</exception>
    Task<IReadOnlyList<OnlinePaymentDetails>> FindChangedPaymentsAsync(
        string accessToken, DateTimeOffset from, DateTimeOffset to, int offset, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// The refunds Mercado Pago has of a payment of the business's account (Fase 24K): the ones TienditApp asked for and the
    /// ones made elsewhere. Empty when there is no such payment.
    /// </summary>
    /// <exception cref="OnlinePaymentAccountRejectedException">Mercado Pago no longer takes the account's key.</exception>
    Task<IReadOnlyList<OnlinePaymentProviderRefund>> GetRefundsAsync(
        string accessToken, string paymentId, CancellationToken cancellationToken);

    /// <summary>
    /// Gives back <paramref name="amount"/> of a payment. The same <paramref name="idempotencyKey"/> never gives it back
    /// twice, so it is safe to retry.
    /// </summary>
    /// <exception cref="OnlinePaymentAccountRejectedException">Mercado Pago no longer takes the account's key.</exception>
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
/// <param name="RefundedAmount">What Mercado Pago gave back of it, whoever asked (Fase 24K).</param>
/// <param name="LastChangedAt">When it last changed in Mercado Pago: approved, refunded, disputed (Fase 24K).</param>
public sealed record OnlinePaymentDetails(
    string Id,
    string Status,
    string? StatusDetail,
    string? ExternalReference,
    decimal Amount,
    string? Currency,
    DateTimeOffset? ApprovedAt,
    long? CollectorId,
    bool LiveMode,
    decimal RefundedAmount = 0,
    DateTimeOffset? LastChangedAt = null)
{
    public const string ApprovedStatus = "approved";
    public const string RefundedStatus = "refunded";
    /// <summary>The customer's bank took it back: <c>in_process</c> while disputed, <c>settled</c> lost, <c>reimbursed</c> won.</summary>
    public const string ChargedBackStatus = "charged_back";
    /// <summary>The customer opened a claim in Mercado Pago, which holds the money until it decides.</summary>
    public const string InMediationStatus = "in_mediation";

    public bool IsApproved => Status == ApprovedStatus && ApprovedAt is not null;

    /// <summary>Whether Mercado Pago may have given part of it back: then its refunds are read.</summary>
    public bool MayHaveRefunds => RefundedAmount > 0 || Status == RefundedStatus || StatusDetail == "partially_refunded";
}

/// <summary>A refund Mercado Pago has of a payment (Fase 24K); only an approved one gave the money back.</summary>
/// <param name="Source">Who made it, as Mercado Pago says: <c>collector</c> (the business), <c>admin</c> (Mercado Pago),
/// <c>bpp</c> (a claim of the customer) or <c>marketplace</c>.</param>
public sealed record OnlinePaymentProviderRefund(string Id, decimal Amount, string Status, string? Source, DateTimeOffset? CreatedAt)
{
    public const string ApprovedStatus = "approved";
    public const string ClaimSource = "bpp";
    public const string MercadoPagoSource = "admin";

    public bool IsApproved => Status == ApprovedStatus;
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

/// <summary>
/// Mercado Pago no longer takes the key of the business's account (401: the owner revoked TienditApp, or the key was
/// invalidated). Retrying will not change it: the account needs to be connected again. A 403 is not this: the key works
/// but may not do that one thing.
/// </summary>
public sealed class OnlinePaymentAccountRejectedException(string message) : Exception(message);
