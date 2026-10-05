using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.AiAgents.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.AiAgents;

/// <summary>
/// "Respuestas automáticas": answers by keywords (whole words, without accents) with the texts the business wrote.
/// There is no language model behind it, so it never promises what it cannot do: orders go through the online menu
/// or a person of the team.
/// </summary>
public class AiAgentAutoReplyService : IAiAgentAutoReplyService
{
    private static readonly string[] GreetingKeywords =
        ["hola", "buenas", "buen dia", "buenos dias", "buenas tardes", "buenas noches", "que tal", "hello", "hi", "hey"];

    private static readonly string[] CatalogKeywords =
    [
        "precio", "precios", "cuanto cuesta", "cuanto cuestan", "cuanto sale", "cuanto es", "costo", "costos", "menu", "carta", "catalogo",
        "producto", "productos", "servicio", "servicios", "promocion", "promociones", "promo", "promos",
        "pedido", "pedir", "ordenar"
    ];

    private static readonly string[] InformationKeywords =
        ["horario", "horarios", "abren", "cierran", "abierto", "abiertos", "ubicacion", "direccion", "donde estan", "donde se ubican"];

    // "ok, gracias" closes the conversation: it gets the closing message instead of "no tengo esa información".
    private static readonly HashSet<string> ThanksWords =
        ["gracias", "muchas", "mil", "ok", "okay", "oki", "vale", "perfecto", "listo", "excelente", "genial", "sale"];

    private readonly IApplicationDbContext _context;
    private readonly IAppLinks _links;

    public AiAgentAutoReplyService(IApplicationDbContext context, IAppLinks links)
    {
        _context = context;
        _links = links;
    }

    public async Task<string?> BuildReplyAsync(
        Guid tenantId,
        string incomingMessage,
        string? customerName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(incomingMessage))
            return null;

        // The webhook runs it in the scope of the business the message was sent to.
        var agent = await _context.AiAgents
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (agent is null)
            return null;

        var words = AutoReplyKeywords.Words(incomingMessage);
        var reply = await ResolveReplyAsync(agent, words, customerName, tenantId, cancellationToken);
        return string.IsNullOrWhiteSpace(reply) ? null : LimitSize(reply.Trim(), 1300);
    }

    private async Task<string?> ResolveReplyAsync(
        AiAgent agent,
        IReadOnlyList<string> words,
        string? customerName,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (AutoReplyKeywords.ContainsAny(words, GreetingKeywords))
            return WithCapturePrompts(agent, agent.WelcomeMessage, customerName);

        if (AutoReplyKeywords.ContainsAny(words, CatalogKeywords))
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(agent.BusinessProductsAndServices))
                parts.Add(agent.BusinessProductsAndServices.Trim());
            parts.Add(await OrderInvitationAsync(tenantId, cancellationToken));
            return string.Join("\n", parts);
        }

        if (AutoReplyKeywords.ContainsAny(words, InformationKeywords) && !string.IsNullOrWhiteSpace(agent.BusinessDescription))
            return agent.BusinessDescription;

        if (AutoReplyKeywords.OnlyWordsFrom(words, ThanksWords))
            return agent.ClosingMessage;

        if (!agent.StrictMode && !string.IsNullOrWhiteSpace(agent.BusinessDescription))
            return $"{agent.FallbackMessage}\n{agent.BusinessDescription}";

        return agent.FallbackMessage;
    }

    /// <summary>The online menu when the business has it on (to order there or just to see it); otherwise a person helps.</summary>
    private async Task<string> OrderInvitationAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        const string PersonHelps = "Escríbenos qué te gustaría y te ayudamos con tu pedido.";

        var appUrl = _links.BaseUrl?.TrimEnd('/');
        if (string.IsNullOrEmpty(appUrl))
            return PersonHelps;

        var menu = await _context.TenantSettings
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => new { s.OnlineMenuMode, s.Tenant.Slug })
            .FirstOrDefaultAsync(cancellationToken);
        if (menu is null || menu.OnlineMenuMode == OnlineMenuMode.Off)
            return PersonHelps;

        var link = $"{appUrl}/m/{menu.Slug}";
        return menu.OnlineMenuMode == OnlineMenuMode.Orders
            ? $"Mira el menú y haz tu pedido aquí: {link}"
            : $"Mira el menú aquí: {link}\n{PersonHelps}";
    }

    /// <summary>Only with the welcome: the data questions no longer follow every reply.</summary>
    private static string WithCapturePrompts(AiAgent agent, string welcome, string? customerName)
    {
        var prompts = new List<string>();

        if (agent.AskForName && string.IsNullOrWhiteSpace(customerName))
            prompts.Add("¿Me compartes tu nombre?");
        if (agent.AskForEmail)
            prompts.Add("Si gustas, compártenos también tu correo.");
        if (agent.AskForAddress)
            prompts.Add("Si es para entrega, compártenos tu dirección.");

        return prompts.Count == 0 ? welcome : $"{welcome}\n{string.Join(" ", prompts)}";
    }

    private static string LimitSize(string message, int maxLength)
        => message.Length <= maxLength ? message : $"{message[..(maxLength - 3)]}...";
}
