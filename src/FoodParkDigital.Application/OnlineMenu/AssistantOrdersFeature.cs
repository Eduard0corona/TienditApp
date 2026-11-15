using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Delivery;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.OnlineMenu;

/// <summary>A product an AI assistant asks for: its id and its options' ids from the menu, how many, and the customer's note.</summary>
public sealed record AssistantOrderItemInput
{
    public Guid ProductId { get; init; }
    public int Quantity { get; init; } = 1;
    public List<Guid> OptionIds { get; init; } = new();
    public string? Notes { get; init; }
}

/// <summary>What an assistant gets back from preparing an order: never anything of the customer.</summary>
public sealed class PreparedAssistantOrderDto
{
    /// <summary>The secret of its link: the store opens with the cart at /m/{slug}#pedido={token}.</summary>
    public string Token { get; init; } = null!;
    public string BusinessName { get; init; } = null!;
    public string Slug { get; init; } = null!;
    public List<PreparedAssistantOrderLineDto> Lines { get; init; } = new();
    /// <summary>What the menu did not take, and why, by its place in what the assistant asked.</summary>
    public List<RefusedAssistantOrderLineDto> Refused { get; init; } = new();
    /// <summary>What its products cost now, with the IVA (added when the prices do not include it); delivery apart.</summary>
    public decimal ItemsTotal { get; init; }
    public bool PricesIncludeTax { get; init; }
    /// <summary>Whether and for how much it delivers to the postal code the assistant gave; null without one.</summary>
    public PublicDeliveryQuoteDto? Delivery { get; init; }
    /// <summary>It has nowhere to pick an order up: the customer gives the address in the store.</summary>
    public bool DeliveryOnly { get; init; }
    /// <summary>Whether the customer can send it now; if not, why (closed, paused), and the link keeps it for later.</summary>
    public bool TakesOrdersNow { get; init; }
    public string? NotNowReason { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed record PreparedAssistantOrderLineDto(
    Guid ProductId, string Name, IReadOnlyList<string> Options, int Quantity, decimal UnitPrice, decimal Amount, string? Notes);

public sealed record RefusedAssistantOrderLineDto(int Position, Guid ProductId, string Reason);

/// <summary>
/// An AI assistant prepares an order of a business for a customer (Fase 32B, ADR 0096): the products of its menu, priced
/// now, with a link the customer opens to review and send it from the store. The business receives nothing until then: an
/// order not sent within a day expires. Only a business that takes orders online and did not turn this off.
/// </summary>
[AllowAnonymousRequest("Pedido que prepara un asistente de IA (MCP): no llega al negocio hasta que el cliente lo envía desde la tienda.")]
public record PrepareAssistantOrderCommand : IRequest<Result<PreparedAssistantOrderDto>>, ITransactionalRequest
{
    public string Slug { get; init; } = null!;
    public List<AssistantOrderItemInput> Items { get; init; } = new();
    /// <summary>Where the customer wants it delivered, if they said: the store offers delivery there.</summary>
    public string? PostalCode { get; init; }
    /// <summary>The assistant, as it introduced itself.</summary>
    public string? Source { get; init; }
}

public class PrepareAssistantOrderCommandValidator : AbstractValidator<PrepareAssistantOrderCommand>
{
    public PrepareAssistantOrderCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Items).NotEmpty().WithMessage("Agrega al menos un producto.");
        RuleFor(x => x.Items.Count).LessThanOrEqualTo(AccountCart.MaxLines)
            .WithMessage($"Un pedido lleva hasta {AccountCart.MaxLines} productos distintos.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty().WithMessage("Cada producto necesita su id del menú.");
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, AccountCart.MaxLineQuantity)
                .WithMessage($"La cantidad de un producto va de 1 a {AccountCart.MaxLineQuantity}.");
            item.RuleFor(i => i.OptionIds).NotNull();
            item.RuleFor(i => i.OptionIds.Count).LessThanOrEqualTo(CreateOrderItemDtoValidator.MaxOptionsPerLine)
                .When(i => i.OptionIds is not null);
            item.RuleFor(i => i.OptionIds).Must(ids => ids.All(id => id != Guid.Empty))
                .When(i => i.OptionIds is not null).WithMessage("Una opción no tiene un id válido.");
            item.RuleFor(i => i.Notes).MaximumLength(AccountCart.MaxLineNotesLength);
        });
        RuleFor(x => x.PostalCode)
            .Must(code => DeliveryZone.IsPostalCode(DeliveryZone.NormalizePostalCode(code)))
            .When(x => !string.IsNullOrWhiteSpace(x.PostalCode))
            // Without what was written: a validation message reaches the log (ADR 0017 keeps postal codes out of it).
            .WithMessage(GetPublicDeliveryQuoteQueryValidator.PostalCodeMessage);
        RuleFor(x => x.Source).MaximumLength(AssistantOrder.MaxSourceLength);
    }
}

public class PrepareAssistantOrderCommandHandler : IRequestHandler<PrepareAssistantOrderCommand, Result<PreparedAssistantOrderDto>>
{
    public const string NoOnlineOrdersMessage = "Este negocio no recibe pedidos en línea: el cliente puede ver su menú y pedirle directo.";
    public const string TurnedOffMessage =
        "Este negocio no recibe pedidos desde asistentes de IA: el cliente puede pedir en su página.";
    public const string BusyMessage =
        "Este negocio tiene demasiados pedidos preparados sin enviar: por ahora el cliente puede pedir en su página.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IAssistantOrdersLock _lock;
    private readonly IDateTimeProvider _clock;

    public PrepareAssistantOrderCommandHandler(
        IApplicationDbContext context, ICurrentTenantService tenantService, IAssistantOrdersLock ordersLock, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _lock = ordersLock;
        _clock = clock;
    }

    public async Task<Result<PreparedAssistantOrderDto>> Handle(PrepareAssistantOrderCommand request, CancellationToken cancellationToken)
    {
        var tenant = await PublicMenuTenant.FindAsync(_context, request.Slug, cancellationToken);
        if (tenant is null || tenant.Settings!.OnlineMenuMode == OnlineMenuMode.Off)
            return Result.Failure<PreparedAssistantOrderDto>(AssistantOrders.NotFoundMessage);
        if (tenant.Settings.OnlineMenuMode != OnlineMenuMode.Orders)
            return Result.Failure<PreparedAssistantOrderDto>(NoOnlineOrdersMessage);
        if (!tenant.Settings.AssistantOrders)
            return Result.Failure<PreparedAssistantOrderDto>(TurnedOffMessage);

        // A public request has no tenant: everything below is scoped to the business of the slug.
        _tenantService.SetTenant(tenant.Id);
        // What it chose, as its link fills the cart: the same product with the same options and note is one line (up to 99).
        // Each refusal names the first product of the request the line came from.
        List<AccountCartLine> chosen;
        try
        {
            chosen = AccountCart.NormalizeLines(request.Items.Select(item => new AccountCartLine(
                item.ProductId, item.OptionIds, item.Quantity, item.Notes)));
        }
        catch (DomainException refused)
        {
            return Result.Failure<PreparedAssistantOrderDto>(refused.Message);
        }
        var positions = chosen.Select(line => 1 + request.Items.FindIndex(item => item.ProductId == line.ProductId
                && (string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim()) == line.Notes
                && item.OptionIds.Distinct().Order().SequenceEqual(line.OptionIds)))
            .ToList();

        // Two at once wait for each other: both do not take the last place.
        await _lock.AcquireAsync(tenant.Id, cancellationToken);
        var now = _clock.UtcNow;
        var waiting = await _context.AssistantOrders.CountAsync(
            o => o.Status == AssistantOrderStatus.Pending && o.ExpiresAt > now, cancellationToken);
        if (waiting >= AssistantOrder.MaxPendingPerBusiness)
            return Result.Failure<PreparedAssistantOrderDto>(BusyMessage);

        // Whether the customer can send it now: the rule of the store. Closed or paused, the link keeps it for later.
        var items = chosen
            .Select(line => new CreateOrderItemDto
            {
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                OptionIds = line.OptionIds.ToList(),
                Notes = line.Notes
            })
            .ToList();
        var placement = new PublicOrderPlacement(_context);
        var asked = new PublicOrderRequest { CustomerName = string.Empty, CustomerPhone = string.Empty, Items = items };
        var (branchId, notNow) = await placement.TakesOrderAsync(tenant, asked, now, cancellationToken);
        var deliveryOnly = notNow == PlacePublicOrderCommandHandler.DeliveryRequiredMessage;
        if (deliveryOnly)
            notNow = null;

        var settings = await TenantSalesSettings.ReadAsync(_context, tenant.Id, cancellationToken);
        var (draft, priced) = await placement.PriceLinesAsync(tenant, items, branchId, settings, now, cancellationToken);
        var taken = priced.Where(line => line.Line is not null).ToList();
        if (taken.Count == 0)
            return Result.Failure<PreparedAssistantOrderDto>(
                "No se pudo agregar ningún producto: " + string.Join(" ", priced.Select(line => line.Refusal).Distinct()));

        var postalCode = DeliveryZone.NormalizePostalCode(request.PostalCode);
        PublicDeliveryQuoteDto? delivery = null;
        if (postalCode is not null)
        {
            var zone = await DeliveryZoneLookup.FindAsync(_context, tenant.Id, postalCode, cancellationToken);
            delivery = new PublicDeliveryQuoteDto
            {
                PostalCode = postalCode,
                Covered = zone is not null,
                Fee = zone?.Fee,
                MinimumOrder = zone?.MinimumOrder,
                FreeDeliveryFrom = zone?.FreeDeliveryFrom
            };
        }

        var prepared = AssistantOrder.Prepare(
            tenant.Id,
            OrderTracking.NewToken(),
            taken.Select(line => new AccountCartLine(
                line.Item.ProductId, line.Item.OptionIds, (int)line.Item.Quantity, line.Item.Notes)),
            postalCode,
            draft.Total,
            request.Source,
            now);
        _context.AssistantOrders.Add(prepared);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new PreparedAssistantOrderDto
        {
            Token = prepared.Token,
            BusinessName = tenant.Name,
            Slug = tenant.Slug,
            Lines = taken.Select(line => new PreparedAssistantOrderLineDto(
                    line.Line!.ProductId,
                    line.Line.ProductName,
                    line.Line.Options.Select(option => option.OptionName).ToList(),
                    (int)line.Line.Quantity,
                    line.Line.UnitPrice,
                    line.Line.UnitPrice * line.Line.Quantity,
                    line.Line.Notes))
                .ToList(),
            Refused = priced
                .Select((line, index) => (line, index))
                .Where(pair => pair.line.Line is null)
                .Select(pair => new RefusedAssistantOrderLineDto(positions[pair.index], pair.line.Item.ProductId, pair.line.Refusal!))
                .ToList(),
            ItemsTotal = draft.Total,
            PricesIncludeTax = settings.PricesIncludeTax,
            Delivery = delivery,
            DeliveryOnly = deliveryOnly,
            TakesOrdersNow = notNow is null,
            NotNowReason = notNow,
            ExpiresAt = prepared.ExpiresAt
        });
    }
}

/// <summary>Where an order an assistant prepared stands, as its link says it.</summary>
public enum AssistantOrderState
{
    /// <summary>Not sent yet, and its link still fills the cart.</summary>
    Open = 0,

    /// <summary>The customer sent it from the store: it is an order of the business.</summary>
    Sent = 1,

    /// <summary>Not sent within its time: its link no longer fills the cart.</summary>
    Expired = 2
}

/// <summary>A line of the cart an order an assistant prepared fills.</summary>
public sealed record AssistantOrderLineDto(Guid ProductId, IReadOnlyList<Guid> OptionIds, int Quantity, string? Notes);

/// <summary>An order an assistant prepared, by its link: its cart for the store, and whether it was sent. Nothing of the customer.</summary>
public sealed class AssistantOrderDto
{
    public string Slug { get; init; } = null!;
    public string BusinessName { get; init; } = null!;
    public AssistantOrderState State { get; init; }
    /// <summary>The cart, while it is open; empty once sent or expired.</summary>
    public List<AssistantOrderLineDto> Lines { get; init; } = new();
    public string? PostalCode { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    /// <summary>Sent: the folio of its order and where it is (as its tracking link words it).</summary>
    public string? OrderNumber { get; init; }
    public string? OrderStatus { get; init; }
}

/// <summary>
/// An order an assistant prepared, by its link (Fase 32B): the store fills its cart with it, and the assistant sees whether
/// the customer sent it. Null for a link that is not one, or of a business no longer active.
/// </summary>
/// <param name="Slug">The store whose page opened the link: one of another business is not found. Null for the assistant's tool.</param>
[AllowAnonymousRequest("Pedido que preparó un asistente de IA, por su enlace: su carrito para la tienda y si se envió; nada del cliente.")]
public record GetAssistantOrderQuery(string Token, string? Slug = null) : IRequest<AssistantOrderDto?>;

public class GetAssistantOrderQueryValidator : AbstractValidator<GetAssistantOrderQuery>
{
    public GetAssistantOrderQueryValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(Order.TrackingTokenLength);
        RuleFor(x => x.Slug).MaximumLength(100);
    }
}

public class GetAssistantOrderQueryHandler : IRequestHandler<GetAssistantOrderQuery, AssistantOrderDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;

    public GetAssistantOrderQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
    }

    public async Task<AssistantOrderDto?> Handle(GetAssistantOrderQuery request, CancellationToken cancellationToken)
    {
        var found = await AssistantOrders.FindAsync(_context, request.Token, cancellationToken);
        // The page of another store never reads it.
        if (found is null
            || (request.Slug is { } slug && !string.Equals(found.Value.Slug, slug.Trim(), StringComparison.OrdinalIgnoreCase)))
            return null;

        // Read inside its business, as any of its records.
        _tenantService.SetTenant(found.Value.TenantId);
        var prepared = await _context.AssistantOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == found.Value.Id, cancellationToken);
        var business = await _context.Tenants.AsNoTracking()
            .Where(t => t.Id == found.Value.TenantId)
            .Select(t => new { t.Name, t.Slug })
            .FirstOrDefaultAsync(cancellationToken);
        if (prepared is null || business is null)
            return null;

        var now = _clock.UtcNow;
        var state = prepared.Status == AssistantOrderStatus.Confirmed ? AssistantOrderState.Sent
            : prepared.IsExpired(now) ? AssistantOrderState.Expired
            : AssistantOrderState.Open;
        var order = prepared.OrderId is { } orderId
            ? await _context.Orders.AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => new { o.OrderNumber, o.Status })
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        return new AssistantOrderDto
        {
            Slug = business.Slug,
            BusinessName = business.Name,
            State = state,
            Lines = state == AssistantOrderState.Open
                ? prepared.Lines.Select(line => new AssistantOrderLineDto(line.ProductId, line.OptionIds, line.Quantity, line.Notes)).ToList()
                : new List<AssistantOrderLineDto>(),
            PostalCode = state == AssistantOrderState.Open ? prepared.PostalCode : null,
            ExpiresAt = prepared.ExpiresAt,
            OrderNumber = order?.OrderNumber,
            OrderStatus = order is null ? null : OrderStatusLabels.ToSpanish(order.Status)
        };
    }
}

/// <summary>The orders assistants prepare, by their link (Fase 32B, ADR 0096).</summary>
internal static class AssistantOrders
{
    public const string NotFoundMessage = "No encontramos ese negocio, o su menú en línea está apagado.";

    /// <summary>
    /// The prepared order a link names, of an active business. The link names one of the whole platform before any business
    /// is in context: only which one and its business are read here; the order itself is read inside that business.
    /// </summary>
    public static async Task<(Guid Id, Guid TenantId, string Slug)?> FindAsync(
        IApplicationDbContext context, string token, CancellationToken cancellationToken)
    {
        if (!Order.IsTrackingToken(token))
            return null;

        var found = await (
                from prepared in context.AssistantOrders.IgnoreQueryFilters().AsNoTracking()
                join tenant in context.Tenants.AsNoTracking() on prepared.TenantId equals tenant.Id
                where prepared.Token == token && tenant.Status == TenantStatus.Active
                select new { prepared.Id, prepared.TenantId, prepared.Token, tenant.Slug })
            .FirstOrDefaultAsync(cancellationToken);
        // The column compares without case, as the database does; the link is the exact one.
        return found is null || !string.Equals(found.Token, token, StringComparison.Ordinal)
            ? null
            : (found.Id, found.TenantId, found.Slug);
    }

    /// <summary>
    /// The order the customer just placed in the business in context came from the link of an order an assistant prepared:
    /// that one is confirmed with it, if it is of this business and still open. Otherwise nothing: the customer reviewed
    /// what they sent, and the order stands.
    /// </summary>
    public static async Task ConfirmAsync(
        IApplicationDbContext context, string? token, Guid orderId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!Order.IsTrackingToken(token))
            return;
        var prepared = await context.AssistantOrders.FirstOrDefaultAsync(o => o.Token == token, cancellationToken);
        if (prepared is null || !string.Equals(prepared.Token, token, StringComparison.Ordinal) || !prepared.IsOpen(now))
            return;
        prepared.Confirm(orderId, now);
    }
}
