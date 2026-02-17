namespace FoodParkDigital.Application.AiAgents.DTOs;

public class AiAgentDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string Personality { get; set; } = null!;
    public string Language { get; set; } = null!;
    public string Tone { get; set; } = null!;
    public string? BusinessName { get; set; }
    public string? BusinessDescription { get; set; }
    public string? BusinessProductsAndServices { get; set; }
    public string WelcomeMessage { get; set; } = null!;
    public string FallbackMessage { get; set; } = null!;
    public string ClosingMessage { get; set; } = null!;
    public bool AskForName { get; set; }
    public bool AskForPhone { get; set; }
    public bool AskForEmail { get; set; }
    public bool AskForAddress { get; set; }
    public string? CustomQuestions { get; set; }
    public string Model { get; set; } = null!;
    public decimal Temperature { get; set; }
    public int MaxTokens { get; set; }
    public bool UseConversationMemory { get; set; }
    public bool StrictMode { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
