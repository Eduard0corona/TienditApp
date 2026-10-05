using System.Linq.Expressions;
using FoodParkDigital.Domain.AiAgents.Entities;

namespace FoodParkDigital.Application.AiAgents.DTOs;

public static class AiAgentMappings
{
    public static readonly Expression<Func<AiAgent, AiAgentDto>> AsDto = agent => new AiAgentDto
    {
        Id = agent.Id,
        Name = agent.Name,
        Role = agent.Role,
        Personality = agent.Personality,
        Language = agent.Language,
        Tone = agent.Tone,
        BusinessName = agent.BusinessName,
        BusinessDescription = agent.BusinessDescription,
        BusinessProductsAndServices = agent.BusinessProductsAndServices,
        WelcomeMessage = agent.WelcomeMessage,
        FallbackMessage = agent.FallbackMessage,
        ClosingMessage = agent.ClosingMessage,
        AskForName = agent.AskForName,
        AskForPhone = agent.AskForPhone,
        AskForEmail = agent.AskForEmail,
        AskForAddress = agent.AskForAddress,
        CustomQuestions = agent.CustomQuestions,
        Model = agent.Model,
        Temperature = agent.Temperature,
        MaxTokens = agent.MaxTokens,
        UseConversationMemory = agent.UseConversationMemory,
        StrictMode = agent.StrictMode,
        IsActive = agent.IsActive,
        CreatedAt = agent.CreatedAt,
        UpdatedAt = agent.UpdatedAt
    };

    private static readonly Func<AiAgent, AiAgentDto> Compiled = AsDto.Compile();

    public static AiAgentDto ToDto(this AiAgent agent) => Compiled(agent);
}
