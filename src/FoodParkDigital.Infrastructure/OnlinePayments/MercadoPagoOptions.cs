namespace FoodParkDigital.Infrastructure.OnlinePayments;

/// <summary>
/// TienditApp's own application in Mercado Pago (Fase 24I, ADR 0031): the businesses connect their accounts to it with
/// OAuth, and its webhook secret signs the payment notifications. Empty, paying online is not offered anywhere.
/// </summary>
public sealed class MercadoPagoOptions
{
    public const string SectionName = "MercadoPago";

    /// <summary>The application's id («App ID») and its secret, from Mercado Pago's «Tus integraciones».</summary>
    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>The secret Mercado Pago signs the webhook notifications of the application with.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>With Mercado Pago's test accounts: their payments are not real money, and real ones are not taken.</summary>
    public bool Sandbox { get; set; }

    public string ApiBaseUrl { get; set; } = "https://api.mercadopago.com";

    /// <summary>Where a business authorizes the application (Mexico's site).</summary>
    public string AuthorizationUrl { get; set; } = "https://auth.mercadopago.com.mx/authorization";
}
