using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Orders.DTOs;

public class OrderDto
{
    public Guid Id { get; set; }
    public Guid? CustomerUserId { get; set; }
    public Guid? TemporaryCustomerId { get; set; }
    public bool CreatedByBusiness { get; set; }
    public string OrderNumber { get; set; } = null!;
    public BusinessType BusinessType { get; set; }
    public OrderType OrderType { get; set; }
    public OrderStatus Status { get; set; }
    public string CustomerName { get; set; } = null!;
    public string? CustomerPhone { get; set; }
    public string? CustomerNotes { get; set; }
    public string? TableNumber { get; set; }
    /// <summary>True when item prices include IVA; false when IVA was added on top.</summary>
    public bool PricesIncludeTax { get; set; }
    public OrderChannel Channel { get; set; }
    /// <summary>An order of Rappi, Uber Eats or DiDi Food: the folio of the app and who charged it. Null for any other channel.</summary>
    public OrderPlatformDto? PlatformOrder { get; set; }
    /// <summary>Where the order goes and what its delivery charged (Fase 22C); null for an order picked up or served.</summary>
    public OrderDeliveryDto? Delivery { get; set; }
    /// <summary>The charge for the visit of the appointment it sells (Fase 22E); null for any other order.</summary>
    public OrderVisitChargeDto? VisitCharge { get; set; }
    /// <summary>Branch where the sale was made.</summary>
    public Guid? BranchId { get; set; }
    /// <summary>When the device captured it without connection; the sale reached the server later.</summary>
    public DateTimeOffset? CapturedOfflineAt { get; set; }
    /// <summary>Taxable base: item amounts without IVA.</summary>
    public decimal SubTotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    /// <summary>IVA by rate, computed by the server from the items (see Order.GetTaxBreakdown).</summary>
    public List<OrderTaxLineDto> TaxBreakdown { get; set; } = new();
    public decimal PaidAmount { get; set; }
    public decimal Balance { get; set; }
    /// <summary>Tips given with the payments, on top of the sale.</summary>
    public decimal TipTotal { get; set; }
    /// <summary>Money given back; payments are not edited.</summary>
    public decimal RefundedAmount { get; set; }
    /// <summary>Paid minus refunded.</summary>
    public decimal NetPaidAmount { get; set; }
    /// <summary>What the business forgave of what the customer owed (Fase 26B); the balance already leaves it out.</summary>
    public decimal WrittenOffAmount { get; set; }
    public string? CancellationReason { get; set; }
    public bool CanCancel { get; set; }
    public List<OrderStatus> AllowedTransitions { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderStatusHistoryDto> StatusHistory { get; set; } = new();
    public ServiceOrderDetailDto? ServiceDetail { get; set; }
    public List<ServiceOrderPieceDto> ServicePieces { get; set; } = new();
    public List<OrderPaymentDto> Payments { get; set; } = new();
    public List<OrderRefundDto> Refunds { get; set; } = new();
    /// <summary>What the business forgave, each with its reason and who forgave it (Fase 26B).</summary>
    public List<OrderWriteOffDto> WriteOffs { get; set; } = new();
    /// <summary>Dishes of the tab cancelled after they went to the kitchen, with the reason and who cancelled them.</summary>
    public List<OrderItemCancellationDto> ItemCancellations { get; set; } = new();
    /// <summary>The order is (or was) a tab: rounds added over time, charged when the table leaves.</summary>
    public bool IsTab { get; set; }
    /// <summary>A tab still taking rounds.</summary>
    public bool IsOpenTab { get; set; }
    /// <summary>
    /// A counter sale the business closed on credit (Fase 26A): its balance is what the customer owes. Still true once paid.
    /// </summary>
    public bool SoldOnCredit { get; set; }
    public Guid? DiningTableId { get; set; }
    /// <summary>Rounds of the tab sent to the kitchen.</summary>
    public List<KitchenTicketSummaryDto> KitchenTickets { get; set; } = new();
    /// <summary>
    /// Only in the answer to the sale that just happened: recipe supplies it took below zero ("Tortilla quedó en -3"),
    /// when the business sells and warns instead of refusing.
    /// </summary>
    public List<string> StockWarnings { get; set; } = new();
}

/// <summary>
/// What the team sees of an order of a delivery platform. Never its commission, withholdings or expected settlement:
/// those figures are in the reports, for whoever may see them.
/// </summary>
public class OrderPlatformDto
{
    /// <summary>The folio the platform's app shows for the order.</summary>
    public string ExternalOrderNumber { get; set; } = null!;
    /// <summary>The platform, in its app, or the business, when the courier pays on pickup.</summary>
    public PlatformCollectedBy CollectedBy { get; set; }
}

/// <summary>
/// The charge for going to the customer's address in the sale of an appointment at their home (Fase 22E): the appointment,
/// its zone and what the visit was agreed for, with the order's tax policy at <see cref="FeeTaxRate"/>.
/// </summary>
public class OrderVisitChargeDto
{
    public Guid AppointmentId { get; set; }
    public string ZoneName { get; set; } = null!;
    public decimal Fee { get; set; }
    public decimal FeeTaxRate { get; set; }
}

/// <summary>
/// Where an order goes (Fase 22C): the customer's address, the zone that reached it, what the delivery charged (with the
/// order's tax policy, at <see cref="FeeTaxRate"/>) and the courier's trip the business wrote down.
/// </summary>
public class OrderDeliveryDto
{
    public string PostalCode { get; set; } = null!;
    public string AddressLine { get; set; } = null!;
    public string? References { get; set; }
    public string ZoneName { get; set; } = null!;
    /// <summary>0 when the delivery was free.</summary>
    public decimal Fee { get; set; }
    public decimal FeeTaxRate { get; set; }
    public DeliveryCourier? Courier { get; set; }
    public string? CourierFolio { get; set; }
    public string? TrackingUrl { get; set; }
    public DateTimeOffset? ShipmentRegisteredAt { get; set; }
    public string? ShipmentRegisteredBy { get; set; }
    /// <summary>The bill the customer pays the courier with, for the change (Fase 22D).</summary>
    public decimal? PaysWith { get; set; }
    /// <summary>When it went out with the courier; none while it waits.</summary>
    public DateTimeOffset? DispatchedAt { get; set; }
    public string? DispatchedBy { get; set; }
    /// <summary>What the courier collects in cash: the balance when it went out; none when nothing.</summary>
    public decimal? CashToCollect { get; set; }
    /// <summary>Delivered for the courier to collect and not yet paid: the courier owes the balance.</summary>
    public bool AwaitsCourierCash { get; set; }
}

public class KitchenTicketSummaryDto
{
    public Guid Id { get; set; }
    public int Round { get; set; }
    public KitchenTicketStatus Status { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public string? SentBy { get; set; }
    public DateTimeOffset? ReadyAt { get; set; }
    public DateTimeOffset? ServedAt { get; set; }
}

/// <summary>A round of an open tab on the kitchen board, with only its own lines.</summary>
public class KitchenTicketDto
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string? TableNumber { get; set; }
    public string? CustomerNotes { get; set; }
    public int Round { get; set; }
    public KitchenTicketStatus Status { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? ReadyAt { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

public class OrderItemOptionDto
{
    public Guid OptionId { get; set; }
    public string GroupName { get; set; } = null!;
    public string OptionName { get; set; } = null!;
    public decimal PriceDelta { get; set; }
}

/// <summary>What the business forgave of an order a customer owed (Fase 26B, ADR 0034): no money moved.</summary>
public class OrderWriteOffDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public OrderWriteOffReason Reason { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset WrittenOffAt { get; set; }
    public string WrittenOffBy { get; set; } = null!;
    /// <summary>When it was undone and by whom (owed again); null while it stands.</summary>
    public DateTimeOffset? ReversedAt { get; set; }
    public string? ReversedBy { get; set; }
}

public class OrderRefundDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public OrderPaymentMethod Method { get; set; }
    public string Reason { get; set; } = null!;
    public DateTimeOffset RefundedAt { get; set; }
    public string RefundedBy { get; set; } = null!;
    public string? Reference { get; set; }
    public bool RestockItems { get; set; }
    public List<OrderRefundLineDto> Lines { get; set; } = new();
    /// <summary>
    /// An «En línea» refund goes back through Mercado Pago (Fase 24I): asked, given back, or refused (then the business
    /// gives it back from its account). Null for any other method.
    /// </summary>
    public OnlinePaymentRefundStatus? OnlineStatus { get; set; }
    /// <summary>What Mercado Pago answered when it refused it.</summary>
    public string? OnlineFailure { get; set; }
}

public class OrderRefundLineDto
{
    public Guid OrderItemId { get; set; }
    public decimal Quantity { get; set; }
}

/// <summary>Units of a dish of a tab cancelled after it went to the kitchen.</summary>
public class OrderItemCancellationDto
{
    public Guid Id { get; set; }
    public Guid OrderItemId { get; set; }
    public int Round { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>What the units took off the line, at its prices.</summary>
    public decimal Amount { get; set; }
    public string Reason { get; set; } = null!;
    /// <summary>Its supplies went back to stock; otherwise they were lost.</summary>
    public bool ReturnedToStock { get; set; }
    public DateTimeOffset CancelledAt { get; set; }
    public string CancelledBy { get; set; } = null!;
}

public class OrderItemDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    /// <summary>Up to three decimals for weight, volume, length and time.</summary>
    public decimal Quantity { get; set; }
    /// <summary>Quantity given back with refunds.</summary>
    public decimal ReturnedQuantity { get; set; }
    /// <summary>Units of a tab's dish cancelled after it went to the kitchen: the order no longer charges them.</summary>
    public decimal CancelledQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>The line as it was ordered: quantity by unit price.</summary>
    public decimal TotalPrice { get; set; }
    /// <summary>What the line charges: its total less the units cancelled.</summary>
    public decimal ChargedPrice { get; set; }
    public string? Notes { get; set; }
    public decimal TaxRate { get; set; }
    public bool IsTaxExempt { get; set; }
    public UnitType Unit { get; set; }
    public string SatProductCode { get; set; } = null!;
    public string SatUnitCode { get; set; } = null!;
    public CfdiTaxObject TaxObject { get; set; }
    /// <summary>Round of an open tab the line was ordered in (1 for any other sale).</summary>
    public int Round { get; set; }
    /// <summary>Variants and extras chosen, already included in the unit price.</summary>
    public List<OrderItemOptionDto> Options { get; set; } = new();
}

public class OrderTaxLineDto
{
    public decimal RatePercent { get; set; }
    public bool IsExempt { get; set; }
    public decimal TaxableBase { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
}

public class OrderStatusHistoryDto
{
    public Guid Id { get; set; }
    public OrderStatus PreviousStatus { get; set; }
    public OrderStatus NewStatus { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public string? ChangedBy { get; set; }
}

public class ServiceOrderDetailDto
{
    public Guid Id { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? PromisedAt { get; set; }
    public int Priority { get; set; }
    public string? IntakeNotes { get; set; }
    public string? DeliveryNotes { get; set; }
    public string? IntakeChecklistJson { get; set; }
    public string? BeforePhotosJson { get; set; }
    public string? AfterPhotosJson { get; set; }
}

public class ServiceOrderPieceDto
{
    public Guid Id { get; set; }
    public string ItemName { get; set; } = null!;
    public Guid? ServiceProductId { get; set; }
    public bool IsTrackable { get; set; }
    public bool NotifyCustomer { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public string? AssignedToName { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
    public string? Material { get; set; }
    public string? ConditionNotes { get; set; }
    public string? RequestedService { get; set; }
    public string? InternalNotes { get; set; }
    public ServiceOrderPieceStatus Status { get; set; }
}

public class OrderPaymentDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public OrderPaymentMethod Method { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    public Guid? InventoryLocationId { get; set; }
    public string? EvidenceUrl { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    /// <summary>On top of the amount, for the team: not part of the sale.</summary>
    public decimal Tip { get; set; }
    /// <summary>What the customer handed over in cash; null for other methods or payments before it was kept.</summary>
    public decimal? CashReceived { get; set; }
    public decimal Change { get; set; }
}

public class KitchenOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    /// <summary>Where the order came from: the kitchen packs an order of a delivery platform for its courier.</summary>
    public OrderChannel Channel { get; set; }
    /// <summary>The folio of the delivery platform's app, written on the bag; null for any other channel.</summary>
    public string? ExternalOrderNumber { get; set; }
    /// <summary>The order goes to the customer's address (Fase 22C): the kitchen packs it to go. Never the address.</summary>
    public bool IsDelivery { get; set; }
    public OrderStatus Status { get; set; }
    public string CustomerName { get; set; } = null!;
    public string? TableNumber { get; set; }
    public string? CustomerNotes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

public class DashboardMetricsDto
{
    public DateTimeOffset RangeFrom { get; set; }
    public DateTimeOffset RangeTo { get; set; }
    public int TotalOrders { get; set; }
    public int InProgressOrders { get; set; }
    public int PendingOrders { get; set; }
    public int CompletedOrders { get; set; }
    public int CancelledOrders { get; set; }
    public int UniqueCustomers { get; set; }
    public int ActiveProducts { get; set; }
    public int ActiveServices { get; set; }
    /// <summary>Payments registered in the range, whatever the status of their orders.</summary>
    public decimal TotalRevenue { get; set; }
    /// <summary>
    /// The part of <see cref="TotalRevenue"/> the delivery platforms charged in their apps, minus what went back as
    /// «Plataforma»: still to be settled with each platform.
    /// </summary>
    public decimal PlatformCollected { get; set; }
    /// <summary>
    /// The part of <see cref="TotalRevenue"/> customers paid online in Mercado Pago (Fase 24I), minus what went back as
    /// «En línea»: in the business's Mercado Pago account, not in its drawers.
    /// </summary>
    public decimal OnlineCollected { get; set; }
    /// <summary>Revenue divided by the orders that received those payments.</summary>
    public decimal AverageOrderValue { get; set; }
    public int PaidOrders { get; set; }
    /// <summary>Balance still owed by the orders of the range that are not cancelled.</summary>
    public decimal OutstandingBalance { get; set; }
    public int UnpaidOrders { get; set; }
    public Dictionary<string, int> OrdersByStatus { get; set; } = new();
    public List<TopSellingItemDto> TopSellingItems { get; set; } = new();
}

public class TopSellingItemDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public string ItemType { get; set; } = "Product";
    public decimal QuantitySold { get; set; }
    public decimal TotalAmount { get; set; }
}
