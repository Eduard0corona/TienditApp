using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.OnlinePayments.Entities;

/// <summary>
/// An owner or administrator went to Mercado Pago to connect the business's account (Fase 24I): its id is the
/// <c>state</c> Mercado Pago sends back, so the answer is taken only once, for the business and the person who asked, and
/// only for a few minutes. It keeps the PKCE verifier the answer is exchanged with.
/// </summary>
public class OnlinePaymentAuthorization : BaseEntity, ITenantScoped
{
    /// <summary>Mercado Pago's code lasts ten minutes; the attempt lasts as much.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public const int MaxCodeVerifierLength = 128;

    private OnlinePaymentAuthorization() { }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string CodeVerifier { get; private set; } = null!;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public static OnlinePaymentAuthorization Start(Guid tenantId, Guid userId, string codeVerifier, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
            throw new DomainException("Conectar Mercado Pago necesita un negocio y quién lo conecta.");
        // RFC 7636: from 43 to 128 characters.
        if (string.IsNullOrWhiteSpace(codeVerifier) || codeVerifier.Length is < 43 or > MaxCodeVerifierLength)
            throw new DomainException("El verificador de la autorización no es válido.");

        return new OnlinePaymentAuthorization
        {
            TenantId = tenantId,
            UserId = userId,
            CodeVerifier = codeVerifier,
            StartedAt = now,
            ExpiresAt = now + Lifetime
        };
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
