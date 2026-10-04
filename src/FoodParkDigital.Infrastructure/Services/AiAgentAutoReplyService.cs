using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Services;

public class AiAgentAutoReplyService : IAiAgentAutoReplyService
{
    private static readonly string[] GreetingKeywords =
    [
        "hola",
        "buenas",
        "hello",
        "hi",
        "que tal",
        "buen dia",
        "buenas tardes",
        "buenas noches"
    ];

    private static readonly string[] CatalogKeywords =
    [
        "precio",
        "cost",
        "costo",
        "servicio",
        "producto",
        "catalogo",
        "menu",
        "promocion",
        "promo"
    ];

    private readonly IApplicationDbContext _context;
    public AiAgentAutoReplyService(
        IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<string?> BuildReplyAsync(
        Guid tenantId,
        string incomingMessage,
        string? customerName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(incomingMessage))
            return null;

        var agent = await _context.AiAgents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (agent is null)
            return null;

        var normalizedMessage = incomingMessage.Trim().ToLowerInvariant();
        var reply = ResolveReply(agent, normalizedMessage);
        var withCapturePrompts = AppendCapturePrompts(agent, reply, customerName);

        return LimitSize(withCapturePrompts, 1300);
    }

    private string ResolveReply(Domain.AiAgents.Entities.AiAgent agent, string normalizedMessage)
    {
        if (GreetingKeywords.Any(keyword => normalizedMessage.Contains(keyword)))
            return agent.WelcomeMessage;

        if (CatalogKeywords.Any(keyword => normalizedMessage.Contains(keyword))
            && !string.IsNullOrWhiteSpace(agent.BusinessProductsAndServices))
        {
            return $"{agent.BusinessProductsAndServices}\nSi te interesa algo, te apoyo a generar tu orden ahora mismo.";
        }

        if (normalizedMessage.Contains("horario") || normalizedMessage.Contains("ubicacion"))
        {
            if (!string.IsNullOrWhiteSpace(agent.BusinessDescription))
                return $"{agent.BusinessDescription}\nSi deseas, te comparto más detalles por aquí.";
        }

        if (!agent.StrictMode && !string.IsNullOrWhiteSpace(agent.BusinessDescription))
            return $"{agent.FallbackMessage}\n{agent.BusinessDescription}";

        return agent.FallbackMessage;
    }

    private string AppendCapturePrompts(
        Domain.AiAgents.Entities.AiAgent agent,
        string reply,
        string? customerName)
    {
        var prompts = new List<string>();

        if (agent.AskForName && string.IsNullOrWhiteSpace(customerName))
            prompts.Add("Para continuar, me compartes tu nombre?");
        if (agent.AskForEmail)
            prompts.Add("Si gustas, comparte también tu email.");
        if (agent.AskForAddress)
            prompts.Add("Si aplica entrega, comparte tu dirección.");

        if (prompts.Count == 0)
            return reply;

        return $"{reply}\n{string.Join(" ", prompts)}";
    }

    private static string LimitSize(string message, int maxLength)
    {
        if (message.Length <= maxLength)
            return message;

        return $"{message[..(maxLength - 3)]}...";
    }
}
