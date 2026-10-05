using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Purchasing.Enums;

namespace FoodParkDigital.Application.Purchasing.DTOs;

public class PurchaseOrderDto
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = null!;
    public string? SupplierPhone { get; set; }
    public string OrderNumber { get; set; } = null!;
    public PurchaseOrderStatus Status { get; set; }
    public DateTimeOffset OrderedAt { get; set; }
    public DateTimeOffset? ExpectedDeliveryAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public decimal SubTotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public List<PurchaseOrderLineDto> Lines { get; set; } = [];
}

public class PurchaseOrderLineDto
{
    public Guid Id { get; set; }
    public Guid SupplyItemId { get; set; }
    public string SupplyItemName { get; set; } = null!;
    public UnitType Unit { get; set; }
    public Guid InventoryLocationId { get; set; }
    public string InventoryLocationName { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal PendingQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public string? Notes { get; set; }
}
