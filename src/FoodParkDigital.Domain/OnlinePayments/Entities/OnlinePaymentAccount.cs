using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.OnlinePayments.Entities;

/// <summary>
/// The business's own Mercado Pago account, connected through TienditApp's application (Fase 24I, ADR 0031): the customers
/// pay into it, so the money never goes through TienditApp. Its keys are kept encrypted (TienditApp encrypts them before
/// they reach here) and renewed before they expire; disconnecting deletes them.
/// </summary>
public class OnlinePaymentAccount : AuditableEntity, IAggregateRoot, ITenantScoped
{
    /// <summary>Mercado Pago's keys last 180 days: they are renewed when this little is left.</summary>
    public static readonly TimeSpan RenewBefore = TimeSpan.FromDays(30);

    public const int MaxProtectedTokenLength = 1000;

    private OnlinePaymentAccount() { }

    public Guid TenantId { get; private set; }

    /// <summary>The account's user id in Mercado Pago: every payment into it carries it as its collector.</summary>
    public long ProviderUserId { get; private set; }

    /// <summary>The key TienditApp uses on behalf of the business, encrypted.</summary>
    public string ProtectedAccessToken { get; private set; } = null!;

    /// <summary>The key that renews <see cref="ProtectedAccessToken"/>, encrypted.</summary>
    public string ProtectedRefreshToken { get; private set; } = null!;

    public DateTimeOffset AccessTokenExpiresAt { get; private set; }

    /// <summary>Whether the account takes real money (false for Mercado Pago's test accounts).</summary>
    public bool LiveMode { get; private set; }

    public DateTimeOffset ConnectedAt { get; private set; }

    /// <summary>The owner or administrator who connected it.</summary>
    public Guid ConnectedByUserId { get; private set; }

    /// <summary>Whether the online menu offers to pay online. Off when connected: the business turns it on.</summary>
    public bool AcceptsPayments { get; private set; }

    /// <summary>The business and the renewal of its keys change it: a change on a stale copy is a conflict (409).</summary>
    public byte[] Version { get; private set; } = null!;

    public static OnlinePaymentAccount Connect(
        Guid tenantId,
        long providerUserId,
        string protectedAccessToken,
        string protectedRefreshToken,
        DateTimeOffset accessTokenExpiresAt,
        bool liveMode,
        Guid connectedByUserId,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || connectedByUserId == Guid.Empty)
            throw new DomainException("La cuenta de Mercado Pago necesita un negocio y quién la conecta.");

        var account = new OnlinePaymentAccount { TenantId = tenantId };
        account.Reconnect(providerUserId, protectedAccessToken, protectedRefreshToken, accessTokenExpiresAt, liveMode, connectedByUserId, now);
        return account;
    }

    /// <summary>The business connects again, maybe another account: it keeps whether it accepts payments.</summary>
    public void Reconnect(
        long providerUserId,
        string protectedAccessToken,
        string protectedRefreshToken,
        DateTimeOffset accessTokenExpiresAt,
        bool liveMode,
        Guid connectedByUserId,
        DateTimeOffset now)
    {
        if (providerUserId <= 0)
            throw new DomainException("Mercado Pago no dijo qué cuenta se conectó.");
        if (connectedByUserId == Guid.Empty)
            throw new DomainException("La cuenta de Mercado Pago necesita quién la conecta.");

        ProviderUserId = providerUserId;
        LiveMode = liveMode;
        ConnectedAt = now;
        ConnectedByUserId = connectedByUserId;
        RenewTokens(protectedAccessToken, protectedRefreshToken, accessTokenExpiresAt, now);
    }

    /// <summary>Mercado Pago renewed the keys (they last 180 days).</summary>
    public void RenewTokens(string protectedAccessToken, string protectedRefreshToken, DateTimeOffset accessTokenExpiresAt, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(protectedAccessToken) || string.IsNullOrWhiteSpace(protectedRefreshToken))
            throw new DomainException("Mercado Pago no entregó las llaves de la cuenta.");
        if (protectedAccessToken.Length > MaxProtectedTokenLength || protectedRefreshToken.Length > MaxProtectedTokenLength)
            throw new DomainException("Las llaves de Mercado Pago son más largas de lo esperado.");
        if (accessTokenExpiresAt <= now)
            throw new DomainException("La llave de Mercado Pago ya venció.");

        ProtectedAccessToken = protectedAccessToken;
        ProtectedRefreshToken = protectedRefreshToken;
        AccessTokenExpiresAt = accessTokenExpiresAt;
    }

    public void SetAcceptsPayments(bool accepts) => AcceptsPayments = accepts;

    /// <summary>
    /// Mercado Pago no longer takes the key <paramref name="protectedAccessToken"/> (the owner revoked the access): from now
    /// on it counts as expired, so nothing is offered or asked with it until the account is renewed or connected again. A
    /// key renewed or connected since is not the one rejected, and stays.
    /// </summary>
    public void RejectKey(string protectedAccessToken, DateTimeOffset now)
    {
        if (ProtectedAccessToken == protectedAccessToken && AccessTokenExpiresAt > now)
            AccessTokenExpiresAt = now;
    }

    /// <summary>Whether its key works: an expired one cannot charge, look up payments or give money back.</summary>
    public bool IsUsable(DateTimeOffset now) => AccessTokenExpiresAt > now;

    public bool NeedsRenewal(DateTimeOffset now) => AccessTokenExpiresAt - now <= RenewBefore;
}
