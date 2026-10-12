using FoodParkDigital.Application.Common;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Tenants.Hours;
using FoodParkDigital.Domain.AiAgents.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.AiAgents;

/// <summary>
/// "Respuestas automáticas": answers by keywords (whole words, without accents) with the texts the business wrote.
/// There is no language model behind it, so it never promises what it cannot do: orders go through the online menu
/// or a person of the team. A question about the hours gets the real ones, special days included, read as the public
/// menu reads them; a question about where it is, when its main branch only delivers, gets that it has no premises to
/// visit. No reply carries the address of a branch.
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

    // Not «a que hora(s)», «hora», «atiende», «abrir» or «cerrar»: «¿A qué hora llega mi pedido?», «¿Quién me atiende?»
    // and «¿Me cierras la cuenta?» do not ask for the hours.
    private static readonly string[] HoursKeywords =
    [
        "horario", "horarios", "abren", "abre", "abierto", "abierta", "abiertos", "abiertas",
        "cierran", "cierra", "cerrado", "cerrada", "cerrados", "cerradas", "atienden", "hasta que hora", "hasta que horas",
        "festivo", "festivos", "vacaciones"
    ];

    private static readonly string[] LocationKeywords =
        ["ubicacion", "direccion", "donde estan", "donde se ubican", "donde quedan", "como llego"];

    // Without hours to tell, the description the business wrote answers both.
    private static readonly string[] InformationKeywords = [.. HoursKeywords, .. LocationKeywords];

    public const string DeliveryOnlyText = "Somos solo a domicilio: no tenemos local para visitar.";

    // "ok, gracias" closes the conversation: it gets the closing message instead of "no tengo esa información".
    private static readonly HashSet<string> ThanksWords =
        ["gracias", "muchas", "mil", "ok", "okay", "oki", "vale", "perfecto", "listo", "excelente", "genial", "sale"];

    private readonly IApplicationDbContext _context;
    private readonly IAppLinks _links;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AiAgentAutoReplyService> _logger;

    public AiAgentAutoReplyService(
        IApplicationDbContext context,
        IAppLinks links,
        IDateTimeProvider? clock = null,
        ILogger<AiAgentAutoReplyService>? logger = null)
    {
        _context = context;
        _links = links;
        _clock = clock ?? SystemClock.Instance;
        _logger = logger ?? NullLogger<AiAgentAutoReplyService>.Instance;
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
        return string.IsNullOrWhiteSpace(reply) ? null : LimitSize(reply.Trim(), AutoReplyComposer.MaxLength);
    }

    private async Task<string?> ResolveReplyAsync(
        AiAgent agent,
        IReadOnlyList<string> words,
        string? customerName,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var greeting = AutoReplyKeywords.ContainsAny(words, GreetingKeywords);
        var catalog = AutoReplyKeywords.ContainsAny(words, CatalogKeywords);
        var hoursAsked = AutoReplyKeywords.ContainsAny(words, HoursKeywords);
        var locationAsked = AutoReplyKeywords.ContainsAny(words, LocationKeywords);
        var deliveryOnly = locationAsked && await TryReadDeliveryOnlyAsync(tenantId, cancellationToken);
        var context = hoursAsked || catalog || deliveryOnly ? await TryReadContextAsync(tenantId, hoursAsked, cancellationToken) : null;

        // The hours first, when there is something to say about them; the texts of the business go around them.
        if (hoursAsked && context?.Hours is { } hours && BusinessHoursText.ForChat(hours) is { } hoursText)
        {
            var reply = new AutoReplyComposer();
            if (greeting)
                reply.Owner(agent.WelcomeMessage);
            reply.System(hoursText);
            if (deliveryOnly)
                reply.System(DeliveryOnlyText);
            // Without weekly hours there is no week to tell: the description may say how the business attends.
            if (locationAsked || !hours.HasWeeklyHours)
                reply.Owner(agent.BusinessDescription);
            if (catalog)
                reply.Owner(agent.BusinessProductsAndServices);
            reply.System(Invitation(context, catalog, hoursReply: true));
            if (greeting)
                reply.System(CapturePrompts(agent, customerName));
            return reply.Compose();
        }

        if (greeting)
            return WithCapturePrompts(agent, agent.WelcomeMessage, customerName);

        if (catalog)
        {
            return new AutoReplyComposer()
                .Owner(agent.BusinessProductsAndServices)
                .System(Invitation(context, catalog: true, hoursReply: false))
                .Compose(separator: "\n");
        }

        // Where it is, for a business without premises: there is nowhere to come, the food goes to the customer.
        if (deliveryOnly)
        {
            return new AutoReplyComposer()
                .System(DeliveryOnlyText)
                .Owner(agent.BusinessDescription)
                .System(Invitation(context, catalog: false, hoursReply: false))
                .Compose();
        }

        if (AutoReplyKeywords.ContainsAny(words, InformationKeywords) && !string.IsNullOrWhiteSpace(agent.BusinessDescription))
            return agent.BusinessDescription;

        if (AutoReplyKeywords.OnlyWordsFrom(words, ThanksWords))
            return agent.ClosingMessage;

        if (!agent.StrictMode && !string.IsNullOrWhiteSpace(agent.BusinessDescription))
            return $"{agent.FallbackMessage}\n{agent.BusinessDescription}";

        return agent.FallbackMessage;
    }

    /// <summary>
    /// Whether the main branch of the business only delivers. A failure is logged and the reply goes on as for a
    /// business with premises: with the description it wrote, never with an address.
    /// </summary>
    private async Task<bool> TryReadDeliveryOnlyAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            return await _context.Branches
                .AsNoTracking()
                .AnyAsync(b => b.TenantId == tenantId && b.IsDefault && b.ServiceMode == BranchServiceMode.DeliveryOnly, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Tenant id only: never the customer's phone or text.
            _logger.LogError(ex, "The main branch for the automatic reply failed for tenant {TenantId}", tenantId);
            return false;
        }
    }

    /// <summary>
    /// Its online menu and, when a question needs them, its hours, read once per message in the scope of the business
    /// the message was sent to. A failure is logged and the reply goes on without the hours, as it did before them;
    /// null when even the settings could not be read.
    /// </summary>
    private async Task<ReplyContext?> TryReadContextAsync(Guid tenantId, bool hoursAsked, CancellationToken cancellationToken)
    {
        ReplyContext? context = null;
        try
        {
            var settings = await _context.TenantSettings
                .AsNoTracking()
                .Where(s => s.TenantId == tenantId)
                .Select(s => new { s.OpeningHours, s.TimeZone, s.OnlineMenuMode, s.Tenant.Slug })
                .FirstOrDefaultAsync(cancellationToken);
            if (settings is null)
                return null;

            context = new ReplyContext(settings.OnlineMenuMode, settings.Slug, Hours: null);
            // A catalog question needs them only to tell a menu that takes orders from one that is closed.
            if (hoursAsked || settings.OnlineMenuMode == OnlineMenuMode.Orders)
            {
                var hours = await BusinessHours.ReadAsync(
                    _context, tenantId, settings.OpeningHours, settings.TimeZone, _clock.UtcNow, cancellationToken);
                context = context with { Hours = hours };
            }
            return context;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Tenant id only: never the customer's phone or text.
            _logger.LogError(ex, "Business hours for the automatic reply failed for tenant {TenantId}", tenantId);
            return context;
        }
    }

    /// <summary>
    /// The online menu when the business has it on (to order there or just to see it); otherwise a person helps. While
    /// closed, the menu is only to see: the reply about the hours already says when it opens, and a catalog reply says it.
    /// </summary>
    private string? Invitation(ReplyContext? context, bool catalog, bool hoursReply)
    {
        const string PersonHelps = "Escríbenos qué te gustaría y te ayudamos con tu pedido.";

        var appUrl = _links.BaseUrl?.TrimEnd('/');
        if (string.IsNullOrEmpty(appUrl) || context is null || context.Mode == OnlineMenuMode.Off)
            return catalog ? PersonHelps : null;

        var link = $"{appUrl}/m/{context.Slug}";
        if (context.Mode != OnlineMenuMode.Orders)
            return catalog ? $"Mira el menú aquí: {link}\n{PersonHelps}" : $"Mira el menú aquí: {link}";

        if (context.Hours is { Status.IsOpen: false } hours && BusinessHoursText.ChatStatusLine(hours) is { } status)
            return hoursReply ? $"Mira el menú aquí: {link}" : $"Mira el menú aquí: {link}\n{status}";
        return $"Mira el menú y haz tu pedido aquí: {link}";
    }

    /// <summary>Only with the welcome: the data questions no longer follow every reply.</summary>
    private static string WithCapturePrompts(AiAgent agent, string welcome, string? customerName)
        => CapturePrompts(agent, customerName) is { } prompts ? $"{welcome}\n{prompts}" : welcome;

    private static string? CapturePrompts(AiAgent agent, string? customerName)
    {
        var prompts = new List<string>();

        if (agent.AskForName && string.IsNullOrWhiteSpace(customerName))
            prompts.Add("¿Me compartes tu nombre?");
        if (agent.AskForEmail)
            prompts.Add("Si gustas, compártenos también tu correo.");
        if (agent.AskForAddress)
            prompts.Add("Si es para entrega, compártenos tu dirección.");

        return prompts.Count == 0 ? null : string.Join(" ", prompts);
    }

    private static string LimitSize(string message, int maxLength)
        => message.Length <= maxLength ? message : $"{message[..AutoReplyComposer.CutAt(message, maxLength - 3)]}...";

    /// <summary>What a reply needs from the business: its online menu and, when asked or to take orders, its hours.</summary>
    private sealed record ReplyContext(OnlineMenuMode Mode, string Slug, BusinessHoursNow? Hours);
}
