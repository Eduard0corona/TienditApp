using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.AiAgents.Entities;

public class AiAgent : AuditableEntity, ITenantScoped
{
    /// <summary>Inactive ones included: the list of a business shows them all, without pages.</summary>
    public const int MaxAgentsPerTenant = 20;

    private AiAgent() { }

    public AiAgent(Guid tenantId, string name, string role)
    {
        TenantId = tenantId;
        Name = name;
        Role = role;

        Personality = "Profesional y resolutivo";
        Language = "Espanol";
        Tone = "Amigable";

        WelcomeMessage = "Hola, soy tu asistente virtual. Te ayudo con productos, servicios y órdenes.";
        FallbackMessage = "No tengo esa información por ahora, pero puedo ayudarte con otra consulta.";
        ClosingMessage = "Gracias por contactarnos. Quedo atento para ayudarte nuevamente.";

        AskForName = true;
        AskForPhone = true;
        AskForEmail = false;
        AskForAddress = false;

        Model = "gpt-4.1-mini";
        Temperature = 0.4m;
        MaxTokens = 800;
        UseConversationMemory = true;
        StrictMode = false;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    // General
    public string Name { get; private set; } = null!;
    public string Role { get; private set; } = null!;
    public string Personality { get; private set; } = null!;
    public string Language { get; private set; } = null!;
    public string Tone { get; private set; } = null!;

    // Business information
    public string? BusinessName { get; private set; }
    public string? BusinessDescription { get; private set; }
    public string? BusinessProductsAndServices { get; private set; }

    // Messages
    public string WelcomeMessage { get; private set; } = null!;
    public string FallbackMessage { get; private set; } = null!;
    public string ClosingMessage { get; private set; } = null!;

    // Data capture
    public bool AskForName { get; private set; }
    public bool AskForPhone { get; private set; }
    public bool AskForEmail { get; private set; }
    public bool AskForAddress { get; private set; }
    public string? CustomQuestions { get; private set; }

    // AI settings
    public string Model { get; private set; } = null!;
    public decimal Temperature { get; private set; }
    public int MaxTokens { get; private set; }
    public bool UseConversationMemory { get; private set; }
    public bool StrictMode { get; private set; }
    public bool IsActive { get; private set; }

    public void UpdateGeneral(
        string name,
        string role,
        string personality,
        string language,
        string tone,
        string? businessName,
        string? businessDescription,
        string? businessProductsAndServices)
    {
        Name = name;
        Role = role;
        Personality = personality;
        Language = language;
        Tone = tone;
        BusinessName = businessName;
        BusinessDescription = businessDescription;
        BusinessProductsAndServices = businessProductsAndServices;
    }

    public void UpdateMessages(string welcomeMessage, string fallbackMessage, string closingMessage)
    {
        WelcomeMessage = welcomeMessage;
        FallbackMessage = fallbackMessage;
        ClosingMessage = closingMessage;
    }

    public void UpdateDataCapture(
        bool askForName,
        bool askForPhone,
        bool askForEmail,
        bool askForAddress,
        string? customQuestions)
    {
        AskForName = askForName;
        AskForPhone = askForPhone;
        AskForEmail = askForEmail;
        AskForAddress = askForAddress;
        CustomQuestions = customQuestions;
    }

    public void UpdateAiConfiguration(
        string model,
        decimal temperature,
        int maxTokens,
        bool useConversationMemory,
        bool strictMode)
    {
        if (temperature is < 0m or > 1m)
            throw new DomainException("La temperatura debe estar entre 0 y 1.");

        if (maxTokens is < 100 or > 4000)
            throw new DomainException("El máximo de tokens debe estar entre 100 y 4000.");

        Model = model;
        Temperature = temperature;
        MaxTokens = maxTokens;
        UseConversationMemory = useConversationMemory;
        StrictMode = strictMode;
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}
