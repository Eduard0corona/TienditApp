using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Orders.Entities;

/// <summary>An option chosen for an order line (size, extra), with the price it added when sold.</summary>
public class OrderItemOption : BaseEntity, ITenantScoped
{
    private OrderItemOption() { }

    internal OrderItemOption(Guid tenantId, Guid orderItemId, OrderLineOption option)
    {
        TenantId = tenantId;
        OrderItemId = orderItemId;
        OptionId = option.OptionId;
        GroupName = option.GroupName;
        OptionName = option.OptionName;
        PriceDelta = option.PriceDelta;
    }

    public Guid TenantId { get; private set; }
    public Guid OrderItemId { get; private set; }
    /// <summary>The catalog option it came from; it may no longer exist.</summary>
    public Guid OptionId { get; private set; }
    public string GroupName { get; private set; } = null!;
    public string OptionName { get; private set; } = null!;
    public decimal PriceDelta { get; private set; }
}
