namespace FoodParkDigital.Infrastructure.Invoicing;

/// <summary>
/// The platform's account with SW Sapien, the PAC that stamps the businesses' invoices (Fase 28A, ADR 0045). With a
/// user and a password TienditApp asks for a token every two hours; a token without expiry (from SW's «Administrador de
/// timbres») can be given instead. Empty, invoicing is not offered anywhere.
/// </summary>
public sealed class SwSapienOptions
{
    public const string SectionName = "SwSapien";

    public string? User { get; set; }

    public string? Password { get; set; }

    /// <summary>A token without expiry; when set, the user and password are not used.</summary>
    public string? Token { get; set; }

    /// <summary>SW's test environment: its invoices have no fiscal effect.</summary>
    public bool Sandbox { get; set; }

    /// <summary>Stamping, cancellation and certificates. Empty: SW's production or test host.</summary>
    public string? ServicesUrl { get; set; }

    /// <summary>PDF and the stamped XML. Empty: SW's production or test host.</summary>
    public string? ApiUrl { get; set; }

    /// <summary>The SAT's public service of the status of a CFDI (SW emulates it in test). Empty: the usual one.</summary>
    public string? StatusUrl { get; set; }
}
