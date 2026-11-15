using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Entities;

/// <summary>
/// An order an AI assistant prepared for a customer through TienditApp's MCP server (Fase 32B, ADR 0096). It is not an order
/// of the business: it keeps what the customer chose (the lines of a cart, never a price) and the total the server computed
/// for them then, and its link (<see cref="Token"/>) opens the store with that cart, for the customer to review and send.
/// Only then the business receives it, as any online order, and this one is <see cref="AssistantOrderStatus.Confirmed"/>.
/// A bot cannot fill a business with orders: one not sent within <see cref="TimeToConfirm"/> expires.
/// </summary>
public class AssistantOrder : AuditableEntity, IAggregateRoot, ITenantScoped
{
    /// <summary>How long its link fills the cart: a customer may open it later in the day, or the next morning.</summary>
    public static readonly TimeSpan TimeToConfirm = TimeSpan.FromHours(24);

    /// <summary>The orders a business may have prepared and not sent at once: a bot preparing them stops there.</summary>
    public const int MaxPendingPerBusiness = 200;

    public const int MaxSourceLength = 100;

    public const string AlreadyConfirmedMessage = "Este pedido ya se envió.";
    public const string ExpiredMessage = "Este pedido venció: pídele al asistente que lo prepare otra vez.";

    private List<AccountCartLine> _lines = [];

    private AssistantOrder() { }

    public Guid TenantId { get; private set; }

    /// <summary>The secret of its link: whoever has it opens the store with its cart and sees whether it was sent.</summary>
    public string Token { get; private set; } = string.Empty;

    /// <summary>What the customer chose; emptied once it is sent (the order has it then).</summary>
    public IReadOnlyList<AccountCartLine> Lines => _lines;

    /// <summary>Where the customer wants it delivered, if they said: the store offers delivery there.</summary>
    public string? PostalCode { get; private set; }

    /// <summary>What its products cost when it was prepared, as the server computed them; the store gives the total.</summary>
    public decimal ItemsTotal { get; private set; }

    /// <summary>The assistant that prepared it, as it introduced itself (its MCP client), if it did.</summary>
    public string? Source { get; private set; }

    public AssistantOrderStatus Status { get; private set; }
    public DateTimeOffset PreparedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }

    /// <summary>
    /// The online order the customer sent from its link. Two sends from the same link at once both reach the business (the
    /// customer sent them); this one keeps the last.
    /// </summary>
    public Guid? OrderId { get; private set; }

    /// <summary>
    /// An order an assistant prepared: its link, at least one line of a cart, the postal code to deliver to if the customer
    /// said one, what the products cost and who prepared it.
    /// </summary>
    public static AssistantOrder Prepare(
        Guid tenantId,
        string token,
        IEnumerable<AccountCartLine> lines,
        string? postalCode,
        decimal itemsTotal,
        string? source,
        DateTimeOffset now)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("El pedido necesita un negocio.");
        if (!Order.IsTrackingToken(token))
            throw new DomainException("El enlace del pedido no es válido.");
        var kept = AccountCart.NormalizeLines(lines);
        if (kept.Count == 0)
            throw new DomainException("El pedido necesita al menos un producto.");
        var code = DeliveryZone.NormalizePostalCode(postalCode);
        if (code is not null && !DeliveryZone.IsPostalCode(code))
            throw new DomainException("El código postal no es válido.");
        if (itemsTotal < 0 || decimal.Round(itemsTotal, 2) != itemsTotal)
            throw new DomainException("El total del pedido no es válido.");
        var who = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
        if (who is { Length: > MaxSourceLength })
            who = who[..MaxSourceLength].TrimEnd();

        return new AssistantOrder
        {
            TenantId = tenantId,
            Token = token,
            _lines = kept,
            PostalCode = code,
            ItemsTotal = itemsTotal,
            Source = who,
            Status = AssistantOrderStatus.Pending,
            PreparedAt = now,
            ExpiresAt = now + TimeToConfirm
        };
    }

    /// <summary>Whether its time to be sent is over.</summary>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Whether its link still fills the cart: prepared, not sent and in its time.</summary>
    public bool IsOpen(DateTimeOffset now) => Status == AssistantOrderStatus.Pending && !IsExpired(now);

    /// <summary>
    /// The customer sent it from the store as <paramref name="orderId"/>: once, and in its time. What they chose is in the
    /// order now, and this one forgets it.
    /// </summary>
    public void Confirm(Guid orderId, DateTimeOffset now)
    {
        if (Status == AssistantOrderStatus.Confirmed)
            throw new DomainException(AlreadyConfirmedMessage);
        if (IsExpired(now))
            throw new DomainException(ExpiredMessage);
        if (orderId == Guid.Empty)
            throw new DomainException("El pedido enviado no existe.");

        OrderId = orderId;
        ConfirmedAt = now;
        Status = AssistantOrderStatus.Confirmed;
        _lines = [];
    }
}
