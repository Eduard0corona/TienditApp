using System.Text.RegularExpressions;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Messaging.Enums;

namespace FoodParkDigital.Domain.Messaging.Entities;

/// <summary>
/// A message the business sends in bulk or on a schedule. Outside the 24-hour window after the customer's last
/// message WhatsApp only delivers templates approved in Meta: <see cref="ProviderTemplateName"/> names it, and
/// the text here is the preview and the fallback inside that window.
/// </summary>
public partial class MessageTemplate : AuditableEntity, ITenantScoped, IAggregateRoot
{
    public const int MaxNameLength = 100;
    public const int MaxBodyLength = 1000;
    public const int MaxProviderTemplateNameLength = 100;
    public const int MaxLanguageCodeLength = 10;
    public const string DefaultLanguageCode = "es_MX";

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex ProviderTemplateNamePattern();

    [GeneratedRegex("^[a-z]{2}(_[A-Z]{2})?$")]
    private static partial Regex LanguageCodePattern();

    private MessageTemplate() { }

    public MessageTemplate(
        Guid tenantId,
        string name,
        MessagePurpose purpose,
        string body,
        string? providerTemplateName = null,
        string? languageCode = null)
    {
        TenantId = tenantId;
        Purpose = purpose;
        IsActive = true;
        Update(name, body, providerTemplateName, languageCode);
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public MessagePurpose Purpose { get; private set; }
    public string Body { get; private set; } = null!;
    public string? ProviderTemplateName { get; private set; }
    public string LanguageCode { get; private set; } = DefaultLanguageCode;
    public bool IsActive { get; private set; }

    public void Update(string name, string body, string? providerTemplateName, string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Ponle un nombre a la plantilla.");
        if (string.IsNullOrWhiteSpace(body))
            throw new DomainException("Escribe el texto del mensaje.");

        var trimmedName = name.Trim();
        var trimmedBody = body.Trim();
        if (trimmedName.Length > MaxNameLength)
            throw new DomainException($"El nombre admite como máximo {MaxNameLength} caracteres.");
        if (trimmedBody.Length > MaxBodyLength)
            throw new DomainException($"El mensaje admite como máximo {MaxBodyLength} caracteres.");
        MessageTemplateText.Validate(trimmedBody, Purpose);

        var provider = string.IsNullOrWhiteSpace(providerTemplateName) ? null : providerTemplateName.Trim();
        if (provider is not null
            && (provider.Length > MaxProviderTemplateNameLength || !ProviderTemplateNamePattern().IsMatch(provider)))
            throw new DomainException("El nombre de la plantilla de Meta solo admite minúsculas, números y guion bajo.");

        var language = string.IsNullOrWhiteSpace(languageCode) ? DefaultLanguageCode : languageCode.Trim();
        if (!LanguageCodePattern().IsMatch(language))
            throw new DomainException("El idioma debe tener el formato de Meta, por ejemplo es_MX.");

        Name = trimmedName;
        Body = trimmedBody;
        ProviderTemplateName = provider;
        LanguageCode = language;
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}
