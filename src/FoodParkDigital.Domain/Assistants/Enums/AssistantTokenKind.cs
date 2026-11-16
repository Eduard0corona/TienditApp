namespace FoodParkDigital.Domain.Assistants.Enums;

/// <summary>What a token of an assistant's connection does (Fase 32E, ADR 0097).</summary>
public enum AssistantTokenKind
{
    /// <summary>Opens the account's tools at /mcp/cuenta for an hour.</summary>
    Access = 1,

    /// <summary>Gets new tokens for 30 days; each use replaces it.</summary>
    Refresh = 2
}
