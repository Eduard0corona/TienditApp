using System.Diagnostics.CodeAnalysis;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Messaging;

namespace FoodParkDigital.Domain.Fiscal;

/// <summary>
/// The WhatsApp a customer gets the PDF of their CFDI at (Fase 28M, ADR 0058): one number in E.164 (+ and 8 to 15 digits),
/// already normalized from what was typed. It is not part of the CFDI.
/// </summary>
public static class InvoiceWhatsApp
{
    public const int MaxLength = MessagingPhone.MaxLength;
    public const string InvalidMessage =
        "El WhatsApp no es válido. Escribe 10 dígitos o el número con lada internacional (por ejemplo +52 55 1234 5678).";

    /// <summary>Whether <paramref name="phone"/> is a number in E.164: what a typed number is normalized to.</summary>
    public static bool IsPhone([NotNullWhen(true)] string? phone)
        => phone is { Length: >= 9 and <= 16 }
            && phone[0] == '+'
            && phone[1] is >= '1' and <= '9'
            && phone.Skip(1).All(char.IsAsciiDigit);

    /// <returns>The number, or null when there is none.</returns>
    public static string? Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;
        var trimmed = phone.Trim();
        if (!IsPhone(trimmed))
            throw new DomainException(InvalidMessage);
        return trimmed;
    }
}
