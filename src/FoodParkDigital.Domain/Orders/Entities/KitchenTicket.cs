using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Entities;

/// <summary>
/// A round of an open tab sent to the kitchen ("comanda"). The tab is charged as one order, while the kitchen
/// prepares each round on its own.
/// </summary>
public class KitchenTicket : BaseEntity, ITenantScoped
{
    private KitchenTicket() { }

    internal KitchenTicket(Guid tenantId, Guid orderId, int round, DateTimeOffset sentAt, string? sentBy)
    {
        TenantId = tenantId;
        OrderId = orderId;
        Round = round;
        Status = KitchenTicketStatus.Pending;
        SentAt = sentAt;
        SentBy = sentBy;
    }

    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public int Round { get; private set; }
    public KitchenTicketStatus Status { get; private set; }
    public DateTimeOffset SentAt { get; private set; }
    public string? SentBy { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? ReadyAt { get; private set; }
    public DateTimeOffset? ServedAt { get; private set; }

    internal void MoveTo(KitchenTicketStatus status, DateTimeOffset at)
    {
        if (!Enum.IsDefined(status) || status <= Status)
            throw new DomainException($"La ronda {Round} ya está más adelante en la cocina.");

        Status = status;
        if (status >= KitchenTicketStatus.Preparing)
            StartedAt ??= at;
        if (status >= KitchenTicketStatus.Ready)
            ReadyAt ??= at;
        if (status == KitchenTicketStatus.Served)
            ServedAt = at;
    }
}
