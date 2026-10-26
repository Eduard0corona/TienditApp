using System.Net.Mail;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Fiscal;

/// <summary>
/// The address a customer gets their CFDI at (Fase 28E, ADR 0049): one plain address, no name or list, as long as an SMTP
/// server takes it, in ASCII (the platform's SMTP sends seven-bit addresses: «peña@…» would never go out).
/// </summary>
public static class InvoiceEmailAddress
{
    public const int MaxLength = 254;
    public const string InvalidMessage = "Escribe un correo válido, como nombre@ejemplo.com.";

    /// <returns>The address without spaces around it, or null when there is none.</returns>
    public static string? Normalize(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;
        var trimmed = email.Trim();
        if (!IsValid(trimmed))
            throw new DomainException(InvalidMessage);
        return trimmed;
    }

    /// <summary>Whether it is one plain address that fits: «nombre@ejemplo.com», not «Nombre &lt;nombre@ejemplo.com&gt;».</summary>
    public static bool IsValid(string email)
    {
        if (email.Length > MaxLength || !email.All(IsPrintableAscii))
            return false;
        var at = email.IndexOf('@');
        // Something before the @, and a domain with a dot after it.
        if (at <= 0 || at != email.LastIndexOf('@') || !email[(at + 1)..].Contains('.') || email.EndsWith('.'))
            return false;
        return MailAddress.TryCreate(email, out var address) && address.Address == email && string.IsNullOrEmpty(address.DisplayName);
    }

    /// <summary>A character an address can carry: printable ASCII, no space.</summary>
    private static bool IsPrintableAscii(char c) => c is > ' ' and <= '~';
}
