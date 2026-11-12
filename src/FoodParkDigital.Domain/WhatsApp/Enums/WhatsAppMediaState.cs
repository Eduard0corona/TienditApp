namespace FoodParkDigital.Domain.WhatsApp.Enums;

/// <summary>Where the photo or audio of a message is (Fase 31L, ADR 0092).</summary>
public enum WhatsAppMediaState
{
    /// <summary>Not downloaded yet: the chat shows its marker and that it is on its way.</summary>
    Pending = 0,
    /// <summary>In the business's private storage: the chat shows it.</summary>
    Saved = 1,
    /// <summary>It could not be kept (too big, not what it said, or WhatsApp no longer has it): only its marker stays.</summary>
    Unavailable = 2
}
